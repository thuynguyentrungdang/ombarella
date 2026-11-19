using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using ombarella.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ombarella
{
    [BepInPlugin(ModGuid, ModName, ModVersion.Value)]
    [BepInDependency(TraceSixBridge.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
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
        readonly WeaponLightProbe _weaponLight = new WeaponLightProbe();
        bool _weaponLightOn;
        // perf diagnostics (debug only)
        bool _sampledThisTick, _envLogged;
        float _dtPostSum, _dtPostMax, _dtBaseSum;
        int _dtPostN, _dtBaseN, _pollTicks, _gc0Last;
        double _pollMsSum, _pollMsMax;
        long _memLast;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            Utils.Logger = Logger;
            OmbarellaConfig.Bind(Config);

            _meter = new LightMeter(gameObject);
            _hud = gameObject.AddComponent<MeterHud>();
            _hud.enabled = false;
            _hud.GetActive = () => _inRaid && _player != null;
            _hud.GetValue = () => Meters.LocalRaw;
            _hud.GetDebug = DebugString;
            _hud.GetVerbose = VerboseString;
            _hud.GetTexture = () => _meter.PreviewTexture;
            _hud.GetPreviewGain = () => OmbarellaConfig.Exposure.Value;

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

                _sampledThisTick = false;
                long pollT0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try { Poll(); }
                catch (Exception e)
                {
                    if (!_pollErrorLogged)
                    {
                        _pollErrorLogged = true;
                        Log.LogError("poll failed (logged once): " + e);
                    }
                }
                double pollMs = (System.Diagnostics.Stopwatch.GetTimestamp() - pollT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _pollMsSum += pollMs; _pollTicks++;
                if (pollMs > _pollMsMax) _pollMsMax = pollMs;
                if (_sampledThisTick && OmbarellaConfig.IsDebug.Value)
                {
                    // next frame's deltaTime = how long the frame holding our render really took (render + GPU waits)
                    yield return null;
                    float dt = Time.unscaledDeltaTime * 1000f;
                    _dtPostSum += dt; _dtPostN++;
                    if (dt > _dtPostMax) _dtPostMax = dt;
                }
            }
        }

        void Poll()
        {
            Meters.Now = Time.time;
            _dtBaseSum += Time.unscaledDeltaTime * 1000f; _dtBaseN++; // baseline frame time: ticks run well after our last render
            if (!OmbarellaConfig.Enabled)
            {
                if (_inRaid) EndRaid();
                return;
            }

            bool raid = Utils.IsInRaid();
            if (!raid) ProbeNotInRaid();
            if (raid && !_inRaid) StartRaid();
            else if (!raid && _inRaid) { EndRaid(); return; }
            if (!_inRaid) return;

            float now = Time.time;
            if (_role == RaidRole.Host || _role == RaidRole.Headless) { var tick = OmbarellaApi.HostTick; if (tick != null) tick(now); }
            if (_role == RaidRole.Headless || !_canRender) return;

            if (_player == null) BindPlayer();
            if (_player == null) return;

            UpdateHud();
            // White light: everyone sees it, so pause rendering and pin score at 1. IR light: only NVG bots see it
            // (Patch_VisionSpeed), so keep metering normally for the rest and just publish the flag.
            WeaponLight light = _weaponLight.Read(_player, now);
            bool white = light == WeaponLight.White;
            if (white != _weaponLightOn)
            {
                _weaponLightOn = white;
                if (white) _meter.HoldFull();
                else { _meter.Resume(); _seedSample = true; _nextSample = 0f; } // fresh sample right away, history was dropped
                Log.LogInfo(white ? "white weapon light on: sampling paused, score held at 1" : "white weapon light off: sampling resumed");
            }
            if (!white) SampleIfDue(now);
            Publish(now, light == WeaponLight.IR);
            LogStats(now);
        }

        // Diagnostic: headless host never logged "raid start". Logs why IsInRaid said no whenever a game or world exists
        // (menu = neither = silent). Only on state change, capped, so it cannot flood.
        string _probeSig;
        int _probeLogs;

        void ProbeNotInRaid()
        {
            if (_probeLogs >= 6) return;
            AbstractGame game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (game == null && world == null) return;
            string sig = (game == null ? "none" : game.GetType().Name + " inRaid=" + game.InRaid) +
                         ", world=" + (world != null) + ", mainPlayer=" + (world != null && world.MainPlayer != null) +
                         ", alive=" + (world != null && world.AllAlivePlayersList != null ? world.AllAlivePlayersList.Count : -1);
            if (sig == _probeSig) return;
            _probeSig = sig;
            _probeLogs++;
            Log.LogInfo("[probe] IsInRaid=false: game " + sig);
        }

        void StartRaid()
        {
            _inRaid = true;
            _role = OmbarellaApi.ResolveRole();
            _canRender = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            _seedSample = true;
            _nextSample = 0f;
            _weaponLightOn = false;
            _envLogged = false;
            _weaponLight.Clear();
            _lastDemandTime = Time.time;
            _lastStatLog = Time.time;
            _meter.ResetAverage();
            _meter.ResetStats();
            Meters.Registry.Clear();
            Meters.LocalRaw = 1f;
            OmbarellaApi.FireRaidReset();
            if (_role != RaidRole.Headless && _canRender) _meter.EnsureResources();
            Log.LogInfo("raid start, role " + _role + (_canRender ? "" : ", no GPU: sampling off") +
                        (_role != RaidRole.Headless && _canRender ? ", " + _meter.DescribeLightMatch() : ""));
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
            bool want = OmbarellaConfig.MeterViz.Value || OmbarellaConfig.ShowRenderTexturePreview.Value || OmbarellaConfig.VerboseDebug.Value;
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
            if (_meter.Sample(_player, observers))
            {
                _sampledThisTick = true;
                if (!_envLogged && OmbarellaConfig.IsDebug.Value) { _envLogged = true; LogEnvironment(); }
            }
        }

        /// <summary>Hold-last-value publish every tick: keeps registry entry fresh while sampling is paused.</summary>
        void Publish(float now, bool ir)
        {
            if (!_meter.HasValue) return;
            float raw = _meter.Average;
            Meters.LocalRaw = raw;

            if (_role == RaidRole.Client) OmbarellaApi.FireClientPublish(_profileId, raw, now, ir);
            else Meters.Registry.SetMeter(_profileId, raw, now, ir);
        }

        /// <summary>One-off (first sample, debug only): everything that could make our extra render heavier here than in solo.</summary>
        void LogEnvironment()
        {
            try
            {
                var sb = new System.Text.StringBuilder("[env] ");
                sb.Append("role ").Append(_role).Append(" | gpu ").Append(SystemInfo.graphicsDeviceName).Append(' ').Append(SystemInfo.graphicsMemorySize).Append(" MB, ")
                  .Append(SystemInfo.graphicsDeviceType).Append(" | cpu ").Append(SystemInfo.processorCount).Append(" threads ").Append(SystemInfo.processorType)
                  .Append(" | screen ").Append(Screen.width).Append('x').Append(Screen.height).Append(" vsync ").Append(QualitySettings.vSyncCount)
                  .Append(" | shadows ").Append(QualitySettings.shadows).Append(" dist ").Append(QualitySettings.shadowDistance)
                  .Append(" cascades ").Append(QualitySettings.shadowCascades).Append(" res ").Append(QualitySettings.shadowResolution)
                  .Append(" pixelLights ").Append(QualitySettings.pixelLightCount).Append(" | cam ").Append(_meter.CameraInfo());

                Vector3 pos = _player != null ? _player.Position : Vector3.zero;
                Light[] lights = FindObjectsOfType<Light>();
                int on = 0, shadowed = 0, near = 0, nearShadowed = 0;
                for (int i = 0; i < lights.Length; i++)
                {
                    Light l = lights[i];
                    if (l == null || !l.enabled || !l.gameObject.activeInHierarchy) continue;
                    on++;
                    bool sh = l.shadows != LightShadows.None;
                    if (sh) shadowed++;
                    if (l.type != LightType.Directional && (l.transform.position - pos).sqrMagnitude < 40f * 40f) { near++; if (sh) nearShadowed++; }
                }
                sb.Append(" | lights ").Append(on).Append(" on, ").Append(shadowed).Append(" shadowed, within 40 m ").Append(near).Append(" (").Append(nearShadowed).Append(" shadowed)");
                sb.Append(" | scene renderers ").Append(FindObjectsOfType<Renderer>().Length).Append(", players alive ").Append(Utils.GetAllPlayers().Count);

                var procs = System.Diagnostics.Process.GetProcesses();
                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < procs.Length; i++)
                {
                    string pn = procs[i].ProcessName;
                    if (pn.IndexOf("Escape", StringComparison.OrdinalIgnoreCase) >= 0 || pn.IndexOf("Headless", StringComparison.OrdinalIgnoreCase) >= 0 || pn.IndexOf("SPT", StringComparison.OrdinalIgnoreCase) >= 0)
                        names.Add(pn);
                }
                sb.Append(" | game-related processes on this PC: ").Append(string.Join(",", names.ToArray()));
                Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Log.LogWarning("[env] failed: " + e.Message); }
        }

        /// <summary>
        /// Debug only, once per stats window. Co-op diagnosis: other human players (count, distance, active),
        /// lights near the camera, shadow distance in effect for the meter render. Compare against camera render / frame ms.
        /// </summary>
        void LogCoopScene()
        {
            try
            {
                Vector3 pos = _player != null ? _player.Position : Vector3.zero;
                var all = Utils.GetAllPlayers();
                int humans = 0, humansActive = 0, aiActive = 0, aiInactive = 0, headless = 0;
                float nearest = -1f;
                for (int i = 0; i < all.Count; i++)
                {
                    Player p = all[i];
                    if (p == null || p == _player) continue;
                    bool active = p.gameObject.activeInHierarchy;
                    if (p.IsAI) { if (active) aiActive++; else aiInactive++; continue; }
                    if (Utils.IsHeadlessPlayer(p)) { headless++; continue; }
                    humans++;
                    if (active) humansActive++;
                    float d = (p.Position - pos).magnitude;
                    if (nearest < 0f || d < nearest) nearest = d;
                }
                Light[] lights = FindObjectsOfType<Light>();
                int n20 = 0, n20s = 0, n40 = 0, n40s = 0;
                for (int i = 0; i < lights.Length; i++)
                {
                    Light l = lights[i];
                    if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.type == LightType.Directional) continue;
                    float sq = (l.transform.position - pos).sqrMagnitude;
                    bool sh = l.shadows != LightShadows.None;
                    if (sq < 40f * 40f) { n40++; if (sh) n40s++; }
                    if (sq < 20f * 20f) { n20++; if (sh) n20s++; }
                }
                Log.LogInfo(string.Format("[perf] coop: other humans {0} ({1} active, nearest {2:F0} m), headless {3}, AI active {4} / inactive {5} | lights within 20 m {6} ({7} shadowed), 40 m {8} ({9} shadowed) | shadowDist global {10:F1}, in meter render {11:F1}, pixelLights {12}",
                    humans, humansActive, nearest, headless, aiActive, aiInactive, n20, n20s, n40, n40s,
                    QualitySettings.shadowDistance, _meter.LastRenderShadowDist, QualitySettings.pixelLightCount));
            }
            catch (Exception e) { Log.LogWarning("[perf] coop line failed: " + e.Message); }
        }

        void LogStats(float now)
        {
            if (!OmbarellaConfig.IsDebug.Value || now - _lastStatLog < StatLogSeconds) return;
            _lastStatLog = now;
            if (_meter.StatSamples == 0) { Log.LogInfo("[perf] no samples (idle / no demand)"); return; }
            int n = _meter.StatSamples;
            Log.LogInfo(string.Format("[perf] {0} samples, avg {1:F2} ms, max {2:F2} ms (render+apply, async readback excluded); avg split: position {3:F2}, renderers+sky {4:F2}, camera render {5:F2} (max {6:F2})",
                n, _meter.StatTotalMs / n, _meter.StatMaxMs, _meter.StatPosMs / n, _meter.StatSetMs / n, _meter.StatRenderMs / n, _meter.StatMaxRenderMs));
            Log.LogInfo(string.Format("[perf] cold(>2s gap) {0}/{1} avg {2:F1} ms; slow(>30ms) {3}; frame ms: baseline {4:F1}, frame holding a sample avg {5:F1} max {6:F1} (n {7}); role {8}, alive {9}",
                _meter.StatColdN, n, _meter.StatColdN == 0 ? 0 : _meter.StatColdMs / _meter.StatColdN, _meter.StatSlow,
                _dtBaseN == 0 ? 0 : _dtBaseSum / _dtBaseN, _dtPostN == 0 ? 0 : _dtPostSum / _dtPostN, _dtPostMax, _dtPostN, _role, Utils.GetAllPlayers().Count));
            _dtPostSum = _dtPostMax = _dtBaseSum = 0f; _dtPostN = _dtBaseN = 0;
            LogCoopScene();

            // where the rest of the tick goes (compare with ModProfiler's PollLoop number): whole Poll() per tick, the extra
            // phases, renderer-set rebuilds (hands/renderer churn), decal scans, readback callback, GC activity
            int gc0 = GC.CollectionCount(0);
            long mem = GC.GetTotalMemory(false);
            Log.LogInfo(string.Format("[perf] poll ticks {0}: avg {1:F2} ms max {2:F2} ms total {3:F0} ms | ambient hook {4:F2}, readback request {5:F2} avg/sample | readback callback {6:F2} avg ({7}) | renderer-set rebuilds {8} ({9:F1} ms total) | decal scans {10} ({11} renderers, {12:F0} ms total, max {13:F0}) | gc gen0 +{14}, heap {15:+0.0;-0.0} MB",
                _pollTicks, _pollTicks == 0 ? 0 : _pollMsSum / _pollTicks, _pollMsMax, _pollMsSum, _meter.StatAmbientMs / n, _meter.StatReqMs / n,
                _meter.StatAccepts == 0 ? 0 : _meter.StatAcceptMs / _meter.StatAccepts, _meter.StatAccepts, _meter.RendererRebuilds, _meter.RendererRebuildMs,
                _meter.SkyScans, _meter.SkyScanRenderers, _meter.SkyScanMs, _meter.SkyScanMaxMs, gc0 - _gc0Last, (mem - _memLast) / 1048576.0));
            _gc0Last = gc0; _memLast = mem; _pollMsSum = _pollMsMax = 0; _pollTicks = 0;
            _meter.ResetStats();
        }

        // Debug text only: rebuilt a few times a second, allocation here is irrelevant.
        string _verbose = "";
        float _verboseAt = -1f;

        string VerboseString()
        {
            float t = Time.unscaledTime;
            if (t - _verboseAt < 0.25f) return _verbose;
            _verboseAt = t;
            float now = Time.time;
            float raw = Meters.LocalRaw;
            float final = MeterMath.Attenuate(OmbarellaConfig.Shape(raw), OmbarellaConfig.Strength);
            double avgMs = _meter.StatSamples == 0 ? 0 : _meter.StatTotalMs / _meter.StatSamples;
            _verbose = string.Format(
                "score {0:F3}  luma {1:F3}  depth {2:F3}\n" +
                "average {3:F3} (window {4})  published {5:F3}  hasValue {6}\n" +
                "vision x{7:F2}  strength {8:F2}  aim scale {9:F2}\n" +
                "role {10}  in raid {11}  render {12}\n" +
                "demand {13}  idle {14:F1}s  seed {15}  samples/s {16:F1}\n" +
                "cost last {17:F2} ms  avg {18:F2}  max {19:F2}  ({20} samples/5s)\n" +
                "RT {21}px  FOV {22:F0}  orbit {23} r{24:F1} h{25:F1}  occlusion {26}\n" +
                "ignore clear {27}  exposure {28:+0.0;-0.0;0.0} EV",
                _meter.LastScore, _meter.LastLuma, _meter.LastColorDepth,
                _meter.Average, Mathf.RoundToInt(OmbarellaConfig.MeterAverageSamples.Value), raw, _meter.HasValue,
                final, OmbarellaConfig.Strength, MeterMath.AimOffsetScale(final, OmbarellaConfig.AimNerfValue, OmbarellaConfig.AimRampStartValue),
                _role, _inRaid, _canRender,
                HasDemand(now), now - _lastDemandTime, _seedSample, OmbarellaConfig.SamplesPerSec.Value,
                _meter.LastMs, avgMs, _meter.StatMaxMs, _meter.StatSamples,
                _meter.TexSize, OmbarellaConfig.CameraFOV.Value, OmbarellaConfig.UseOrbitCameraSampling.Value,
                OmbarellaConfig.OrbitCameraRadius.Value, OmbarellaConfig.OrbitCameraHeightOffset.Value, OmbarellaConfig.RejectOccludedSamples.Value,
                OmbarellaConfig.IgnoreTransparentPixels.Value, OmbarellaConfig.Exposure.Value);
            return _verbose;
        }

        string DebugString()
        {
            return string.Format("score {0:F3}, luma {1:F3}, color depth {2:F3}, role {3}", _meter.LastScore, _meter.LastLuma, _meter.LastColorDepth, _role);
        }
    }
}
