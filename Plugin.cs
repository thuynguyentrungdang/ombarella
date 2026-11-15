using BepInEx;
using BepInEx.Logging;
using EFT;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ombarella
{
    [BepInPlugin(ModGuid, ModName, ModVersion.Value)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "Ombarella";
        public const string ModName = "Ombarella";

        public static Plugin Instance;
        public static ManualLogSource Log;

        // Poll tick, not per-frame Update: raid detection + sampling cadence need no more than this.
        const float MaxPollInterval = 0.25f;
        const float DemandWindow = 2f;
        const float IdleResetSeconds = 10f;
        const float StatLogSeconds = 5f;

        LightMeter _meter;
        MeterHud _hud;
        RaidRole _role = RaidRole.Solo;
        bool _inRaid;
        bool _canRender;
        bool _seedSample;
        Player _player;
        string _profileId;
        float _nextSample;
        float _lastDemandTime;
        float _lastStatLog;
        bool _idleLogged;
        float _lastPollInterval = -1f;
        WaitForSecondsRealtime _wait;
        bool _pollErrorLogged;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            Utils.Logger = Logger;
            OmbarellaConfig.Bind(Config);

            _meter = new LightMeter(gameObject);
            _hud = gameObject.AddComponent<MeterHud>();
            _hud.enabled = false;
            _hud.GetValue = () => Meters.LocalRaw;
            _hud.GetDebug = DebugString;
            _hud.GetTexture = () => _meter.Texture;

            LoadPatches();
            StartCoroutine(PollLoop());
        }

        void OnDestroy()
        {
            EndRaid();
            if (_meter != null) _meter.Dispose();
        }

        void LoadPatches()
        {
            TryLoadPatch(new Patch_VisionSpeed());
            TryLoadPatch(new Patch_AimOffset());
        }

        void TryLoadPatch(SPT.Reflection.Patching.ModulePatch patch)
        {
            try
            {
                patch.Enable();
                Log.LogInfo("patch active: " + patch.GetType().Name);
            }
            catch (Exception e)
            {
                Log.LogError("FAILED to load patch " + patch.GetType().Name + ": " + e);
                throw;
            }
        }

        IEnumerator PollLoop()
        {
            while (true)
            {
                float interval = Mathf.Min(MaxPollInterval, 1f / Mathf.Max(1f, OmbarellaConfig.SamplesPerSec.Value));
                if (_wait == null || Mathf.Abs(interval - _lastPollInterval) > 0.0001f)
                {
                    _wait = new WaitForSecondsRealtime(interval);
                    _lastPollInterval = interval;
                }
                yield return _wait;

                try { Poll(); }
                catch (Exception e)
                {
                    if (!_pollErrorLogged)
                    {
                        _pollErrorLogged = true;
                        Log.LogError("poll failed (logged once): " + e);
                    }
                }
            }
        }

        void Poll()
        {
            if (!OmbarellaConfig.Enabled)
            {
                if (_inRaid) EndRaid();
                return;
            }

            bool raid = Utils.IsInRaid();
            if (raid && !_inRaid) StartRaid();
            else if (!raid && _inRaid) { EndRaid(); return; }
            if (!_inRaid) return;

            float now = Time.time;
            if (_role == RaidRole.Host || _role == RaidRole.Headless) { var tick = OmbarellaApi.HostTick; if (tick != null) tick(now); }
            if (_role == RaidRole.Headless || !_canRender) return;

            if (_player == null) BindPlayer();
            if (_player == null) return;

            UpdateHud();
            SampleIfDue(now);
            Publish(now);
            LogStats(now);
        }

        void StartRaid()
        {
            _inRaid = true;
            _role = OmbarellaApi.ResolveRole();
            _canRender = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            _seedSample = true;
            _nextSample = 0f;
            _lastDemandTime = Time.time;
            _lastStatLog = Time.time;
            _meter.ResetAverage();
            _meter.ResetStats();
            Meters.Registry.Clear();
            Meters.LocalRaw = 1f;
            OmbarellaApi.FireRaidReset();
            if (_role != RaidRole.Headless && _canRender) _meter.EnsureResources();
            Log.LogInfo("raid start, role " + _role + (_canRender ? "" : ", no GPU: sampling off"));
        }

        void EndRaid()
        {
            if (!_inRaid && _player == null) return;
            _inRaid = false;
            _player = null;
            _profileId = null;
            _meter.ResetAverage();
            _meter.ReleaseTarget();
            Meters.Registry.Clear();
            Meters.LocalRaw = 1f;
            OmbarellaApi.FireRaidReset();
            _hud.enabled = false;
        }

        void BindPlayer()
        {
            Player p = Utils.GetMainPlayer();
            if (p == null || string.IsNullOrEmpty(p.ProfileId)) return;
            _player = p;
            _profileId = p.ProfileId;
            Meters.Registry.Track(_profileId);
        }

        void UpdateHud()
        {
            bool want = OmbarellaConfig.MeterViz.Value || OmbarellaConfig.ShowRenderTexturePreview.Value;
            if (_hud.enabled != want) _hud.enabled = want;
        }

        bool HasDemand(float now)
        {
            if (!OmbarellaConfig.OnDemandSampling.Value) return true;
            if (_role == RaidRole.Client) return OmbarellaApi.ShouldSampleAsClient();
            return Meters.Registry.HasDemand(_profileId, now, DemandWindow);
        }

        void SampleIfDue(float now)
        {
            if (now < _nextSample) return;

            bool demand = _seedSample || HasDemand(now);
            if (!demand)
            {
                if (!_idleLogged && now - _lastDemandTime > IdleResetSeconds)
                {
                    _idleLogged = true;
                    // stale average would blend old light into the first fresh sample
                    _meter.ResetAverageKeepValue();
                }
                return;
            }

            _lastDemandTime = now;
            _idleLogged = false;
            _seedSample = false;
            _nextSample = now + 1f / Mathf.Max(1f, OmbarellaConfig.SamplesPerSec.Value);

            List<Player> observers = OmbarellaConfig.UseOrbitCameraSampling.Value ? null : Utils.GetAllPlayers();
            _meter.Sample(_player, observers);
        }

        /// <summary>Hold-last-value publish every tick: keeps registry entry fresh while sampling is paused.</summary>
        void Publish(float now)
        {
            if (!_meter.HasValue) return;
            float raw = _meter.Average;
            Meters.LocalRaw = raw;

            if (_role == RaidRole.Client) OmbarellaApi.FireClientPublish(_profileId, raw, now);
            else Meters.Registry.SetMeter(_profileId, raw, now);
        }

        void LogStats(float now)
        {
            if (!OmbarellaConfig.IsDebug.Value || now - _lastStatLog < StatLogSeconds) return;
            _lastStatLog = now;
            if (_meter.StatSamples == 0) { Log.LogInfo("[perf] no samples (idle / no demand)"); return; }
            Log.LogInfo(string.Format("[perf] {0} samples, avg {1:F2} ms, max {2:F2} ms (render+apply, async readback excluded)",
                _meter.StatSamples, _meter.StatTotalMs / _meter.StatSamples, _meter.StatMaxMs));
            _meter.ResetStats();
        }

        string DebugString()
        {
            return string.Format("score {0:F3}, luma {1:F3}, color depth {2:F3}, role {3}", _meter.LastScore, _meter.LastLuma, _meter.LastColorDepth, _role);
        }
    }
}
