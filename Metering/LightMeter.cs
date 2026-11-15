using EFT;
using ombarella.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace ombarella
{
    /// <summary>
    /// Owns light camera + render texture. One target per sample: renders the local player, reads pixels back
    /// (async by default), scores them, feeds a rolling average. No per-sample managed allocations.
    /// </summary>
    public sealed class LightMeter : IDisposable
    {
        readonly GameObject _host;
        readonly MeterAverager _averager = new MeterAverager();
        readonly PlayerRendererSet _renderers = new PlayerRendererSet();
        readonly Action<AsyncGPUReadbackRequest> _onReadback;
        readonly Stopwatch _watch = new Stopwatch();

        Camera _cam;
        RenderTexture _rt;
        Texture2D _readTex;
        int _texSize;
        bool _pending;
        bool _disposed;
        ScoreSettings _pendingSettings;

        public RenderTexture Texture { get { return _rt; } }
        public bool HasValue { get; private set; }
        /// <summary>Rolling average of raw score, 0.01..1.</summary>
        public float Average { get; private set; }
        public float LastLuma { get; private set; }
        public float LastColorDepth { get; private set; }
        public float LastScore { get; private set; }

        // perf counters (debug)
        public int StatSamples;
        public double StatTotalMs;
        public double StatMaxMs;

        public LightMeter(GameObject host)
        {
            _host = host;
            Average = 1f;
            _onReadback = OnReadback;
        }

        public void ResetAverage()
        {
            _averager.Clear();
            HasValue = false;
            Average = 1f;
            _pending = false;
        }

        /// <summary>Drop averaging history, keep last published value (used after idle).</summary>
        public void ResetAverageKeepValue() { _averager.Clear(); }

        public void ResetStats() { StatSamples = 0; StatTotalMs = 0; StatMaxMs = 0; }

        public void ReleaseTarget() { _renderers.Clear(); }

        /// <summary>(Re)create RT/camera when config size changed. Cheap no-op otherwise.</summary>
        public void EnsureResources()
        {
            int size = ConfiguredSize();
            if (_rt != null && _cam != null && size == _texSize)
            {
                ApplyCameraSettings();
                return;
            }
            ReleaseRt();
            _texSize = size;
            _rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32);
            _rt.enableRandomWrite = false;
            _rt.stencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            _rt.dimension = TextureDimension.Tex2D;
            _rt.Create();

            if (_cam == null) _cam = _host.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.enabled = false;
            ApplyCameraSettings();
            CameraRig.Initialize(_cam);
        }

        static int ConfiguredSize()
        {
            int v = Mathf.Clamp(Mathf.RoundToInt(OmbarellaConfig.RenderTextureResolution.Value), 16, 512);
            return Mathf.Max(8, Mathf.RoundToInt(v / 8f) * 8);
        }

        void ApplyCameraSettings()
        {
            _cam.fieldOfView = OmbarellaConfig.CameraFOV.Value;
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = -1;
            _cam.renderingPath = RenderingPath.UsePlayerSettings;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = true;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 20f;
        }

        void ReleaseRt()
        {
            _pending = false;
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (_readTex != null) { UnityEngine.Object.Destroy(_readTex); _readTex = null; }
        }

        /// <summary>
        /// Position camera, render target, start readback. Returns false when nothing rendered
        /// (readback already in flight, bad geometry, occluded).
        /// </summary>
        public bool Sample(Player target, List<Player> observers)
        {
            if (_pending || _disposed) return false;
            EnsureResources();
            if (_cam == null || _rt == null) return false;

            _watch.Restart();
            if (!PositionCamera(target, observers)) return false;
            if (!_renderers.Refresh(target, OmbarellaConfig.ExcludeOpticRenderers.Value)) return false;

            _renderers.Apply(OmbarellaConfig.ForceTargetPlayerRenderers.Value);
            try { _cam.Render(); }
            finally { _renderers.Restore(); }

            _pendingSettings = CaptureSettings();
            if (OmbarellaConfig.UseAsyncGPUReadback.Value && SystemInfo.supportsAsyncGPUReadback)
            {
                _pending = true;
                AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, _onReadback);
                _watch.Stop();
            }
            else
            {
                ReadBlocking();
                _watch.Stop();
            }
            RecordCost();
            return true;
        }

        bool PositionCamera(Player target, List<Player> observers)
        {
            Vector3 focus;
            bool ok = OmbarellaConfig.UseOrbitCameraSampling.Value
                ? CameraRig.TryRepositionCameraOnOrbit(target, OmbarellaConfig.OrbitCameraRadius.Value, OmbarellaConfig.OrbitCameraHeightOffset.Value, OrbitAngle(), out focus)
                : CameraRig.TryRepositionCamera(target, observers, out focus);
            return ok && (!OmbarellaConfig.RejectOccludedSamples.Value || HasLineOfSight(focus));
        }

        static float OrbitAngle()
        {
            return OmbarellaConfig.UseFixedOrbitAngle.Value ? OmbarellaConfig.FixedOrbitAngle.Value : UnityEngine.Random.Range(0f, 360f);
        }

        static int _occlusionMask;
        bool HasLineOfSight(Vector3 focus)
        {
            Vector3 from = _cam.transform.position;
            Vector3 dir = focus - from;
            float dist = dir.magnitude;
            if (dist <= 0.001f || !Utils.IsFinite(dist)) return false;
            if (_occlusionMask == 0)
            {
                int m = LayerMask.GetMask("Terrain", "HighPolyCollider", "LowPolyCollider", "DoorLowPolyCollider");
                int playerLayer = LayerMask.NameToLayer("Player");
                _occlusionMask = m != 0 ? m : Physics.DefaultRaycastLayers & ~(playerLayer < 0 ? 0 : 1 << playerLayer);
            }
            return !Physics.Raycast(from, dir / dist, dist, _occlusionMask, QueryTriggerInteraction.Ignore);
        }

        static ScoreSettings CaptureSettings()
        {
            return new ScoreSettings
            {
                LumaCoef = OmbarellaConfig.LumaCoef.Value,
                RedLumaMulti = OmbarellaConfig.RedLumaMulti.Value,
                GreenLumaMulti = OmbarellaConfig.GreenLumaMulti.Value,
                BlueLumaMulti = OmbarellaConfig.BlueLumaMulti.Value,
                RedColorDepthMulti = OmbarellaConfig.RedColorDepthMulti.Value,
                GreenColorDepthMulti = OmbarellaConfig.GreenColorDepthMulti.Value,
                BlueColorDepthMulti = OmbarellaConfig.BlueColorDepthMulti.Value,
                IgnoreTransparentPixels = OmbarellaConfig.IgnoreTransparentPixels.Value
            };
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            if (_disposed || !_pending) return;
            _pending = false;
            if (request.hasError) return;
            Accept(request.GetData<byte>().AsReadOnlySpan());
        }

        void ReadBlocking()
        {
            if (_readTex == null || _readTex.width != _texSize)
            {
                if (_readTex != null) UnityEngine.Object.Destroy(_readTex);
                _readTex = new Texture2D(_texSize, _texSize, TextureFormat.RGBA32, false);
            }
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _readTex.ReadPixels(new Rect(0, 0, _texSize, _texSize), 0, 0, false);
            RenderTexture.active = prev;
            Accept(_readTex.GetRawTextureData<byte>().AsReadOnlySpan());
        }

        void Accept(ReadOnlySpan<byte> rgba)
        {
            float score, luma, cd;
            if (!ScoreCalculator.TryCalculate(rgba, _pendingSettings, out score, out luma, out cd)) return;
            LastScore = score; LastLuma = luma; LastColorDepth = cd;
            float mean;
            int window = Mathf.RoundToInt(OmbarellaConfig.MeterAverageSamples.Value);
            if (!_averager.Add(score, window, out mean)) return;
            Average = mean;
            HasValue = true;
        }

        void RecordCost()
        {
            double ms = _watch.Elapsed.TotalMilliseconds;
            StatSamples++;
            StatTotalMs += ms;
            if (ms > StatMaxMs) StatMaxMs = ms;
        }

        public void Dispose()
        {
            _disposed = true;
            _renderers.Clear();
            ReleaseRt();
            if (_cam != null) UnityEngine.Object.Destroy(_cam);
        }
    }
}
