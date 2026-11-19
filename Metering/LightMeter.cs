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
        readonly SkyHider _sky = new SkyHider();
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
        public double LastMs;
        public int TexSize { get { return _texSize; } }
        public int StatSamples;
        public double StatTotalMs;
        public double StatMaxMs;
        // phase split of the sample cost (diagnostic): position+raycast, renderer set+sky, camera render, readback request
        public double StatPosMs, StatSetMs, StatRenderMs, StatMaxRenderMs;
        // cold = first render after a >2 s pause (cold-path hypothesis); slow = render over 30 ms
        public double StatColdMs; public int StatColdN, StatSlow;
        // extra phases: EFT ambient hook, readback request call, readback callback (exposure+score+average)
        public double StatAmbientMs, StatReqMs, StatAcceptMs; public int StatAccepts;
        public int RendererRebuilds { get { return _renderers.RebuildCount; } }
        public double RendererRebuildMs { get { return _renderers.RebuildMs; } }
        public int SkyScans { get { return _sky.ScanCount; } }
        public int SkyScanRenderers { get { return _sky.ScanRenderers; } }
        public double SkyScanMs { get { return _sky.ScanMs; } }
        public double SkyScanMaxMs { get { return _sky.ScanMaxMs; } }
        float _lastSampleAt = -100f;

        /// <summary>One-line camera + player renderer facts for the environment log.</summary>
        public string CameraInfo()
        {
            if (_cam == null) return "no camera";
            return "path " + _cam.actualRenderingPath + ", hdr " + _cam.allowHDR + ", occlusion " + _cam.useOcclusionCulling +
                   ", rt " + _texSize + "px, player renderers " + _renderers.Count;
        }

        public LightMeter(GameObject host)
        {
            _host = host;
            Average = 1f;
            _onReadback = OnReadback;
        }

        bool _suspended;

        public void ResetAverage()
        {
            _averager.Clear();
            HasValue = false;
            Average = 1f;
            _pending = false;
            _suspended = false;
        }

        /// <summary>Weapon light on: no rendering, score pinned to 1 (fully lit). A readback still in flight is dropped.</summary>
        public void HoldFull()
        {
            _suspended = true;
            _averager.Clear();
            Average = 1f;
            HasValue = true;
        }

        /// <summary>Weapon light off: sampling allowed again. Average stays 1 until the first fresh sample replaces it.</summary>
        public void Resume() { _suspended = false; }

        /// <summary>Drop averaging history, keep last published value (used after idle).</summary>
        public void ResetAverageKeepValue() { _averager.Clear(); }

        public void ResetStats() { StatSamples = 0; StatTotalMs = 0; StatMaxMs = 0; StatPosMs = 0; StatSetMs = 0; StatRenderMs = 0; StatMaxRenderMs = 0; StatColdMs = 0; StatColdN = 0; StatSlow = 0; StatAmbientMs = 0; StatReqMs = 0; StatAcceptMs = 0; StatAccepts = 0; _renderers.ResetStats(); _sky.ResetStats(); }

        public void ReleaseTarget() { _renderers.Clear(); _lightMatch.Release(); }

        readonly LightMatch _lightMatch = new LightMatch();

        /// <summary>Raid-start log line: requested light-match mode and what it resolved to.</summary>
        public string DescribeLightMatch() { return _lightMatch.Describe(); }

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
            Plugin.Log.LogInfo("[meter] camera type " + _cam.cameraType + ", " + size + " px");
        }

        static int ConfiguredSize()
        {
            int v = Mathf.Clamp(Mathf.RoundToInt(OmbarellaConfig.RenderTextureResolution.Value), 16, 512);
            return Mathf.Max(8, Mathf.RoundToInt(v / 8f) * 8);
        }

        AmbientLight _ambient;

        /// <summary>
        /// EFT adds its sky/natural ambient as an AfterLighting command buffer ("Custom Ambient", AmbientLight.cs) that
        /// OnRenderObjectManager only attaches to CameraManager.Camera and the optic camera. Our camera never got it, so only real
        /// Unity lights (lamps) lit the frame and outdoor light read ~0 at midday (sun light is 0 in overcast, RenderSettings ambient is 0).
        /// Calling the public hook for our camera makes AmbientLight create its per-camera buffers. Cheap: a dictionary hit after the first call.
        /// </summary>
        void RegisterEftAmbient()
        {
            if (!OmbarellaConfig.UseEftAmbient.Value) return;
            if (_ambient == null) _ambient = UnityEngine.Object.FindObjectOfType<AmbientLight>();
            if (_ambient == null) return;
            try { _ambient.ManualOnRenderObject(_cam); }
            catch (Exception e)
            {
                OmbarellaConfig.UseEftAmbient.Value = false; // never break sampling over a cosmetic pass
                Plugin.Log.LogError("EFT ambient hook failed, disabled: " + e);
            }
        }

        void ApplyCameraSettings()
        {
            _cam.fieldOfView = OmbarellaConfig.CameraFOV.Value;
            _cam.rect = new Rect(0f, 0f, 1f, 1f);
            // Alpha-0 clear = empty view is skipped by ScoreCalculator (IgnoreTransparentPixels).
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask = -1;
            // FuckThisCulling (Hi-Z) treats every non-main CameraType.Game camera as "other camera": on each of its PreCull it runs
            // RestoreAll() on all geometry it hid, then re-hides it for the gameplay camera afterwards. Log-verified in one raid:
            // camera render 2-3 ms with Hi-Z off, 5-14 ms with Hi-Z on (restore runs inside Render), plus re-hide churn on the main camera.
            // It skips cameras whose cameraType != Game, so we declare ours a Reflection camera (a public, documented API; no reflection into the mod).
            // Trade: with Hi-Z on, geometry it hid for the main view stays hidden in our render too (some off-view shadow casters/background missing).
            _cam.cameraType = CameraType.Reflection;
            _cam.renderingPath = RenderingPath.UsePlayerSettings;
            // HDR on: with LDR (false) the deferred light buffer is log-encoded and every empty pixel resolves white,
            // ignoring the clear color (dump-verified; forward and HDR-deferred both clear correctly).
            _cam.allowHDR = true;
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
            if (_previewTex != null) { UnityEngine.Object.Destroy(_previewTex); _previewTex = null; }
        }

        /// <summary>
        /// Position camera, render target, start readback. Returns false when nothing rendered
        /// (readback already in flight, bad geometry, occluded).
        /// </summary>
        /// <summary>Diagnostics: shadow distance actually in effect during the last meter render (after clamp).</summary>
        public float LastRenderShadowDist;

        public bool Sample(Player target, List<Player> observers)
        {
            if (_pending || _disposed || _suspended) return false;
            EnsureResources();
            if (_cam == null || _rt == null) return false;

            _watch.Restart();
            if (!PositionCamera(target, observers)) return false;
            double t0 = _watch.Elapsed.TotalMilliseconds;
            if (!_renderers.Refresh(target, OmbarellaConfig.ExcludeOpticRenderers.Value)) return false;

            double ta = _watch.Elapsed.TotalMilliseconds;
            RegisterEftAmbient();
            _lightMatch.Sync(_cam);
            StatAmbientMs += _watch.Elapsed.TotalMilliseconds - ta;
            _renderers.Apply(OmbarellaConfig.ForceTargetPlayerRenderers.Value);
            if (OmbarellaConfig.ExcludeSky.Value) _sky.Hide();
            if (OmbarellaConfig.DumpRenderTexture.Value) ProbeFrustum();
            double t1 = _watch.Elapsed.TotalMilliseconds;
            // Shadows stay on, but the global shadow distance (40 in EFT, cascades span it) only needs to reach our far plane:
            // anything past it is never in frame. Smaller cascades = less shadow-map work per sample. Restored right after.
            float prevShadowDist = QualitySettings.shadowDistance;
            if (prevShadowDist > _cam.farClipPlane) QualitySettings.shadowDistance = _cam.farClipPlane;
            LastRenderShadowDist = QualitySettings.shadowDistance;
            try
            {
                _cam.Render();
                if (OmbarellaConfig.DumpRenderTexture.Value) DumpVariants();
            }
            finally { QualitySettings.shadowDistance = prevShadowDist; _sky.Restore(); _renderers.Restore(); }
            double t2 = _watch.Elapsed.TotalMilliseconds;
            StatPosMs += t0; StatSetMs += t1 - t0; StatRenderMs += t2 - t1;
            if (t2 - t1 > StatMaxRenderMs) StatMaxRenderMs = t2 - t1;
            float at = Time.unscaledTime;
            if (at - _lastSampleAt > 2f) { StatColdN++; StatColdMs += t2 - t1; }
            _lastSampleAt = at;
            if (t2 - t1 > 30.0) StatSlow++;

            _pendingSettings = CaptureSettings();
            _pendingSettings.Width = _texSize;
            if (OmbarellaConfig.UseAsyncGPUReadback.Value && SystemInfo.supportsAsyncGPUReadback)
            {
                _pending = true;
                double tr = _watch.Elapsed.TotalMilliseconds;
                AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, _onReadback);
                StatReqMs += _watch.Elapsed.TotalMilliseconds - tr;
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

        const float ClipCheckRadius = 0.05f; // near clip 0.01 + margin
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
            // Camera clipped into a wall: ray starts inside the collider and never hits, so test overlap too. Skip = score holds last value.
            if (Physics.CheckSphere(from, ClipCheckRadius, _occlusionMask, QueryTriggerInteraction.Ignore)) return false;
            return !Physics.Raycast(from, dir / dist, dist, _occlusionMask, QueryTriggerInteraction.Ignore);
        }

        static ScoreSettings CaptureSettings()
        {
            return new ScoreSettings
            {
                LumaCoef = 1f, // exposure is applied to the pixels instead (Core/Exposure.cs)
                RedLumaMulti = OmbarellaConfig.RedLumaMulti.Value,
                GreenLumaMulti = OmbarellaConfig.GreenLumaMulti.Value,
                BlueLumaMulti = OmbarellaConfig.BlueLumaMulti.Value,
                RedColorDepthMulti = OmbarellaConfig.RedColorDepthMulti.Value,
                GreenColorDepthMulti = OmbarellaConfig.GreenColorDepthMulti.Value,
                BlueColorDepthMulti = OmbarellaConfig.BlueColorDepthMulti.Value,
                DepthWeight = OmbarellaConfig.ColorDepthWeight.Value,
                IgnoreTransparentPixels = OmbarellaConfig.IgnoreTransparentPixels.Value,
                CenterFocus = OmbarellaConfig.CenterWeightEnabled.Value ? OmbarellaConfig.CenterFocus.Value : 0f,
                CenterSigma = OmbarellaConfig.CenterWidth.Value
            };
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            if (_disposed || !_pending) return;
            _pending = false;
            if (_suspended) return; // frame was rendered before the light came on
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

        /// <summary>
        /// Debug only (dump toggle on): re-render the same pose with fog off / rain+particles hidden / magenta clear and write
        /// ombarella_rgb_{variant}.png each, to bisect what paints the white. Blocking reads, runs once per dump.
        /// </summary>
        void DumpVariants()
        {
            try
            {
                var hide = new List<Renderer>();
                Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i];
                    if (r == null || r.forceRenderingOff || !r.enabled) continue;
                    Material m = r.sharedMaterial;
                    string sh = m != null && m.shader != null ? m.shader.name : "";
                    // additive/overlay effect shaders: rain, sparks, particles
                    if (sh.IndexOf("Rain", StringComparison.OrdinalIgnoreCase) >= 0 || sh.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0) hide.Add(r);
                }

                bool fog = RenderSettings.fog;
                Color bg = _cam.backgroundColor;
                string dir = BepInEx.Paths.BepInExRootPath;
                // Last dump: background stayed white even with magenta clear, fog off, rain/decals/sky hidden, so it is not a
                // Renderer. Suspect the deferred LDR path (allowHDR=false). Compare rendering path and HDR, whole scene and player only.
                int pm = LayerMask.GetMask("Player");
                int cm = _cam.cullingMask;
                RenderingPath path = _cam.renderingPath;
                bool hdr = _cam.allowHDR;
                _cam.backgroundColor = new Color(1f, 0f, 1f, 1f); // magenta clear: shows what is genuinely empty
                try
                {
                    _cam.renderingPath = RenderingPath.Forward;
                    DumpVariant(dir, "forward", true, hide, false);
                    if (pm != 0) { _cam.cullingMask = pm; DumpVariant(dir, "forward_playeronly", true, hide, false); _cam.cullingMask = cm; }
                    _cam.renderingPath = path;
                    _cam.allowHDR = true;
                    DumpVariant(dir, "hdr", true, hide, false);
                    if (pm != 0) { _cam.cullingMask = pm; DumpVariant(dir, "hdr_playeronly", true, hide, false); _cam.cullingMask = cm; }
                    DumpCopyMain(dir, hide);
                    // what the player's own camera shows right now (includes HUD), written at end of frame
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "ombarella_screen.png"));
                }
                finally
                {
                    _cam.renderingPath = path; _cam.allowHDR = hdr; _cam.cullingMask = cm; _cam.backgroundColor = bg;
                }
                RenderSettings.fog = fog;
                _cam.Render(); // restore: the pending async readback must see the normal frame, not the last variant
                Plugin.Log.LogInfo("[dumpvariants] wrote 4 variants (forward, forward_playeronly, hdr, hdr_playeronly), hid " + hide.Count + " effect renderers");
            }
            catch (Exception e) { Plugin.Log.LogError("dump variants failed: " + e.Message); }
        }

        /// <summary>Debug only: same pose, but with every Camera property copied from the main camera (HDR, path, clip, occlusion, etc.).</summary>
        void DumpCopyMain(string dir, List<Renderer> hide)
        {
            Camera main = Camera.main;
            if (main == null) { Plugin.Log.LogInfo("[dumpvariants] copymain skipped: no Camera.main"); return; }
            Vector3 pos = _cam.transform.position;
            Quaternion rot = _cam.transform.rotation;
            float fov = _cam.fieldOfView;
            try
            {
                _cam.CopyFrom(main);
                _cam.transform.SetPositionAndRotation(pos, rot);
                _cam.targetTexture = _rt;
                _cam.rect = new Rect(0f, 0f, 1f, 1f); // CopyFrom brings main's viewport rect: rendered into a sub-rectangle of our RT
                _cam.ResetAspect();
                _cam.fieldOfView = fov;
                _cam.nearClipPlane = 0.01f;
                _cam.farClipPlane = 20f;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
                _cam.cullingMask = -1;
                Plugin.Log.LogInfo(string.Format("[dumpvariants] copymain: path {0} hdr {1} occlusion {2} dynRes {3} clearFlags was {4}",
                    _cam.actualRenderingPath, _cam.allowHDR, _cam.useOcclusionCulling, _cam.allowDynamicResolution, main.clearFlags));
                DumpVariant(dir, "copymain", true, hide, false);
            }
            finally
            {
                ApplyCameraSettings();
                _cam.targetTexture = _rt;
                _cam.transform.SetPositionAndRotation(pos, rot);
            }
        }

        void DumpVariant(string dir, string name, bool fogOn, List<Renderer> hide, bool hideEffects)
        {
            bool fogWas = RenderSettings.fog;
            RenderSettings.fog = fogOn;
            if (hideEffects) for (int i = 0; i < hide.Count; i++) if (hide[i] != null) hide[i].forceRenderingOff = true;
            try
            {
                _cam.Render();
                if (_readTex == null || _readTex.width != _texSize)
                {
                    if (_readTex != null) UnityEngine.Object.Destroy(_readTex);
                    _readTex = new Texture2D(_texSize, _texSize, TextureFormat.RGBA32, false);
                }
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _readTex.ReadPixels(new Rect(0, 0, _texSize, _texSize), 0, 0, false);
                RenderTexture.active = prev;
                byte[] raw = _readTex.GetRawTextureData<byte>().ToArray();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "ombarella_rgb_" + name + ".png"), PngWriter.Encode(raw, _texSize, false, 8));
            }
            finally
            {
                if (hideEffects) for (int i = 0; i < hide.Count; i++) if (hide[i] != null) hide[i].forceRenderingOff = false;
                RenderSettings.fog = fogWas;
            }
        }

        /// <summary>Debug only (dump toggle on): log the biggest enabled renderers inside the light camera frustum, to find what draws the white.</summary>
        int _probeCamLogs;

        /// <summary>Probe only: logs what the sun/ambient look like at the moment each camera starts rendering.</summary>
        void ProbeOnPreRender(Camera c)
        {
            if (c != _cam && c != Camera.main) return;
            TOD_Sky tod = TOD_Sky.Instance;
            Light sun = tod != null && tod.Components != null ? tod.Components.LightSource : null;
            Plugin.Log.LogInfo(string.Format("[probe] prerender {0}: sun {1:F2} enabled {2}, ambient {3}, mode {4}, probe L0 {5:F2}, frame {6}",
                c == _cam ? "OMBARELLA" : "main", sun != null ? sun.intensity : -1f, sun != null && sun.isActiveAndEnabled,
                RenderSettings.ambientLight, RenderSettings.ambientMode, RenderSettings.ambientProbe[0, 0], Time.frameCount));
            if (--_probeCamLogs <= 0) Camera.onPreRender -= ProbeOnPreRender;
        }

        void ProbeFrustum()
        {
            try
            {
                Plugin.Log.LogInfo("[probe] sky: " + _sky.Status + ", clearFlags " + _cam.clearFlags + ", bg " + _cam.backgroundColor + ", far " + _cam.farClipPlane + ", mask " + _cam.cullingMask + ", path " + _cam.actualRenderingPath + ", fog " + RenderSettings.fog);
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_cam);
                TOD_Sky tod = TOD_Sky.Instance;
                Light sun = tod != null && tod.Components != null ? tod.Components.LightSource : null;
                Camera main = Camera.main;
                Plugin.Log.LogInfo(string.Format("[probe] light: colorSpace {0}, ambientMode {1}, ambientIntensity {2:F2}, ambient {3}, todLightIntensity {4:F2}, lightSource {5} intensity {6:F2} shadows {7}, mainCam hdr {8} path {9}, cam hdr {10}, rt {11}",
                    QualitySettings.activeColorSpace, RenderSettings.ambientMode, RenderSettings.ambientIntensity, RenderSettings.ambientLight,
                    tod != null ? tod.LightIntensity : -1f, sun != null ? sun.name : "-", sun != null ? sun.intensity : -1f, sun != null ? sun.shadows.ToString() : "-",
                    main != null ? main.allowHDR.ToString() : "-", main != null ? main.actualRenderingPath.ToString() : "-", _cam.allowHDR, _rt != null ? _rt.format + "/" + _rt.sRGB : "-"));
                var comps = new System.Text.StringBuilder();
                if (main != null)
                {
                    foreach (Behaviour b in main.GetComponents<Behaviour>())
                        if (b != null) comps.Append(b.GetType().Name).Append(b.enabled ? "" : "(off)").Append(' ');
                }
                Plugin.Log.LogInfo("[probe] scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ", tod " + (tod != null) + ", mainCam components: " + comps);
                if (tod != null)
                    Plugin.Log.LogInfo(string.Format("[probe] tod hour {0:F2}, sunZenith {1:F1}, isDay {2}, lerp {3:F2}, dayIntensity {4:F2}, nightIntensity {5:F2}",
                        tod.Cycle != null ? tod.Cycle.Hour : -1f, tod.SunZenith, tod.IsDay, tod.LerpValue, tod.Day != null ? tod.Day.LightIntensity : -1f, tod.Night != null ? tod.Night.LightIntensity : -1f));
                _probeCamLogs = 8;
                Camera.onPreRender -= ProbeOnPreRender;
                Camera.onPreRender += ProbeOnPreRender;
                Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
                int dirs = 0;
                for (int i = 0; i < lights.Length; i++)
                {
                    Light l = lights[i];
                    if (l == null || l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                    dirs++;
                    Plugin.Log.LogInfo(string.Format("[probe] dir light {0} intensity {1:F2} color {2} mask {3} shadows {4}", l.name, l.intensity, l.color, l.cullingMask, l.shadows));
                }
                Plugin.Log.LogInfo("[probe] lights total " + lights.Length + ", directional " + dirs);
                Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
                var hits = new List<Renderer>();
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i];
                    if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                    if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) hits.Add(r);
                }
                hits.Sort((a, b) => b.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));
                Plugin.Log.LogInfo("[probe] " + hits.Count + " renderers in frustum, largest:");
                for (int i = 0; i < hits.Count && i < 12; i++)
                {
                    Renderer r = hits[i];
                    Material m = r.sharedMaterial;
                    Plugin.Log.LogInfo(string.Format("[probe]  {0} layer {1} ({2}) size {3:F0} shader {4} queue {5}",
                        r.name, r.gameObject.layer, LayerMask.LayerToName(r.gameObject.layer), r.bounds.size.magnitude,
                        m != null && m.shader != null ? m.shader.name : "-", m != null ? m.renderQueue : -1));
                }
            }
            catch (Exception e) { Plugin.Log.LogError("probe failed: " + e.Message); }
        }

        void DumpFrame(ReadOnlySpan<byte> rgba)
        {
            OmbarellaConfig.DumpRenderTexture.Value = false;
            try
            {
                int n = rgba.Length / 4, clear = 0, white = 0, litNoAlpha = 0;
                int[] rowSum = new int[_texSize];
                for (int i = 0; i < n; i++)
                {
                    int o = i * 4, r = rgba[o], g = rgba[o + 1], b = rgba[o + 2], a = rgba[o + 3];
                    if (a == 0) { clear++; if ((r | g | b) != 0) litNoAlpha++; }
                    if (r > 240 && g > 240 && b > 240) white++;
                    rowSum[i / _texSize] += (r + g + b) / 3;
                }
                string dir = BepInEx.Paths.BepInExRootPath;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "ombarella_rgb.png"), PngWriter.Encode(rgba, _texSize, false, 8));
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "ombarella_alpha.png"), PngWriter.Encode(rgba, _texSize, true, 8));
                // readback rows are bottom-first: row 0 = bottom of frame
                int third = _texSize / 3;
                Plugin.Log.LogInfo(string.Format("[dump] {0}px: alpha0 {1:P0} (of those rgb>0: {2}), white(>240) {3:P0}, mean RGB bottom/mid/top third {4}/{5}/{6}",
                    n, clear / (float)n, litNoAlpha, white / (float)n,
                    ThirdMean(rowSum, 0, third), ThirdMean(rowSum, third, 2 * third), ThirdMean(rowSum, 2 * third, _texSize)));
            }
            catch (Exception e) { Plugin.Log.LogError("dump failed: " + e.Message); }
        }

        static int ThirdMean(int[] rowSum, int from, int to)
        {
            long s = 0; int c = Math.Max(1, to - from);
            for (int i = from; i < to; i++) s += rowSum[i];
            return (int)(s / c / Math.Max(1, rowSum.Length));
        }

        Texture2D _previewTex;
        byte[] _previewBytes;

        /// <summary>Debug preview: the exact post-exposure pixels that get scored (not the live RT, which dump variants overwrite).</summary>
        public Texture PreviewTexture { get { return _previewTex; } }
        byte[] _exposed;

        void UpdatePreview(ReadOnlySpan<byte> rgba)
        {
            int n = _texSize * _texSize * 4;
            if (rgba.Length != n) return;
            if (_previewTex == null || _previewTex.width != _texSize)
            {
                if (_previewTex != null) UnityEngine.Object.Destroy(_previewTex);
                _previewTex = new Texture2D(_texSize, _texSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                _previewBytes = new byte[n];
            }
            byte[] o = _previewBytes;
            for (int i = 0; i < n; i += 4)
            {
                if (rgba[i + 3] == 0) { o[i] = o[i + 1] = o[i + 2] = 48; o[i + 3] = 255; continue; } // empty/clear = grey, distinct from black
                o[i] = rgba[i]; o[i + 1] = rgba[i + 1]; o[i + 2] = rgba[i + 2]; o[i + 3] = 255;
            }
            _previewTex.LoadRawTextureData(o);
            _previewTex.Apply(false);
        }

        void Accept(ReadOnlySpan<byte> raw)
        {
            long tacc = System.Diagnostics.Stopwatch.GetTimestamp();
            try { AcceptCore(raw); }
            finally
            {
                StatAcceptMs += (System.Diagnostics.Stopwatch.GetTimestamp() - tacc) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                StatAccepts++;
            }
        }

        void AcceptCore(ReadOnlySpan<byte> raw)
        {
            if (OmbarellaConfig.DumpRenderTexture.Value) DumpFrame(raw); // raw, pre-exposure
            if (_exposed == null || _exposed.Length != raw.Length) _exposed = new byte[raw.Length];
            Exposure.Apply(raw, _exposed, Exposure.GainFromEv(OmbarellaConfig.Exposure.Value));
            ReadOnlySpan<byte> rgba = _exposed;
            if (OmbarellaConfig.ShowRenderTexturePreview.Value) UpdatePreview(rgba);
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
            LastMs = ms;
            StatSamples++;
            StatTotalMs += ms;
            if (ms > StatMaxMs) StatMaxMs = ms;
        }

        public void Dispose()
        {
            _disposed = true;
            _lightMatch.Release();
            _renderers.Clear();
            ReleaseRt();
            if (_cam != null) UnityEngine.Object.Destroy(_cam);
        }
    }
}
