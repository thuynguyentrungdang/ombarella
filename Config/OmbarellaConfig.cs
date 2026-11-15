using BepInEx.Configuration;
using System;

namespace ombarella
{
    /// <summary>All config bindings. Section/key names kept from 0.4.x so existing user configs carry over.</summary>
    public static class OmbarellaConfig
    {
        public static ConfigEntry<bool> MasterSwitch, MeterViz, OnDemandSampling;
        public static ConfigEntry<float> MeterAttenuationCoef, SamplesPerSec, MeterAverageSamples, AimNerf;
        public static ConfigEntry<float> CameraFOV, Exposure, RenderTextureResolution, VisionCurveExponent, VisionRampNear, VisionRampFar;
        public static ConfigEntry<bool> UseAsyncGPUReadback, IgnoreTransparentPixels, ExcludeSky, UseEftAmbient;
        public static ConfigEntry<float> RedLumaMulti, GreenLumaMulti, BlueLumaMulti;
        public static ConfigEntry<float> RedColorDepthMulti, GreenColorDepthMulti, BlueColorDepthMulti;
        public static ConfigEntry<float> CamHorizontalOffset, CameraFocusHeightOffset, OrbitCameraRadius, OrbitCameraHeightOffset;
        public static ConfigEntry<HudAnchor> MeterAnchor;
        public static ConfigEntry<float> MeterOffsetX, MeterOffsetY, MeterFontSize;
        public static ConfigEntry<bool> VerboseDebug;
        public static ConfigEntry<bool> ForceTargetPlayerRenderers, UseOrbitCameraSampling, RejectOccludedSamples, ExcludeOpticRenderers;
        public static ConfigEntry<float> DebugUpdateFreq, RenderTexturePreviewSize, FixedOrbitAngle;
        public static ConfigEntry<bool> IsDebug, ShowRenderTexturePreview, UseFixedOrbitAngle, DumpRenderTexture;

        // hot-path caches (patches read these, not ConfigEntry)
        public static volatile bool Enabled = true;
        public static float Strength = 1f;
        public static float AimNerfValue = 0.03f;

        public static void Bind(ConfigFile c)
        {
            MasterSwitch = B(c, true, "a - Toggles", "Master Switch", "Toggle all mod functions on/off");
            MeterViz = B(c, true, "a - Toggles", "Enable light meter indicator", "Visual representation of how much you are being lit and how visible you are");
            MeterAnchor = c.Bind("a - Toggles", "Meter anchor", HudAnchor.TopLeft, "Screen corner the light meter (and debug text) is anchored to");
            MeterOffsetX = F(c, 20f, "a - Toggles", "Meter offset X", "Pixels from the anchored screen edge, horizontal", 0f, 4000f);
            MeterOffsetY = F(c, 10f, "a - Toggles", "Meter offset Y", "Pixels from the anchored screen edge, vertical", 0f, 4000f);
            MeterFontSize = F(c, 18f, "a - Toggles", "Meter font size", "Font size of the light meter and debug text", 8f, 48f);
            OnDemandSampling = B(c, true, "a - Toggles", "On-demand sampling", "Only render light samples while a bot is looking at a player it has not detected yet. Saves performance when no bot is watching; the meter holds its last value meanwhile.");

            SamplesPerSec = F(c, 1f, "b - Main Settings", "1-Light samples per second", "Main throttle of the mod; higher = more accurate reading / less perf", 1f, 60f);
            MeterAverageSamples = F(c, 15f, "b - Main Settings", "2-Light meter average samples", "Number of valid light samples to average before applying the result. Higher values smooth noisy orbit sampling.", 1f, 600f);
            MeterAttenuationCoef = F(c, 1f, "b - Main Settings", "3-Light meter strength", "Determines how quickly bots can spot you per your visiblity level (100% = bots get full effect, slower recognition time). In Fika the host/headless value applies.", 0f, 1f);
            AimNerf = F(c, 0.03f, "b - Main Settings", "4-Bot aim handicap", "Extra aim spread for bots when you are in the dark (higher = worse bot aim in low light; zero = no penalty). Bright light leaves bot aim untouched. In Fika the host/headless value applies.", 0f, 0.1f);

            VisionCurveExponent = F(c, 0.5f, "b - Main Settings", "5-Darkness curve exponent", "Bot vision speed = meter ^ this. 1 = linear (meter 0.1 means 10x slower), 0.5 = about 3x slower at 0.1, closer to human vision. In Fika the host/headless value applies.", 0.1f, 1f);
            VisionRampNear = F(c, 5f, "b - Main Settings", "6-Darkness effect starts (m)", "Bots closer than this ignore the light meter. In Fika the host/headless value applies.", 0f, 100f);
            VisionRampFar = F(c, 30f, "b - Main Settings", "7-Darkness full effect (m)", "Bots this far or farther get the full light meter effect; fades in linearly from the start distance.", 1f, 200f);
            CameraFOV = F(c, 30f, "c - Advanced Settings", "CameraFOV", "Size of light camera FOV", 10f, 170f);
            UseEftAmbient = B(c, true, "c - Advanced Settings", "Use EFT sky ambient", "Attaches EFT's own ambient-light pass to the light camera. Without it only lamps light the frame and outdoor daylight reads near zero. Turn off if it causes rendering problems.");
            Exposure = F(c, 2f, "c - Advanced Settings", "Exposure (EV)", "Stops applied to the raw light-camera image before scoring, clipped at white like a camera (+2 = 4x). The raw render is dark. Raise if bright days score low, lower if dark interiors score high.", -4f, 8f);
            RenderTextureResolution = F(c, 64f, "c - Advanced Settings", "Render texture resolution", "Resolution of the light camera render texture. Applied before raid start and rounded to the nearest multiple of 8.", 16f, 512f);
            UseAsyncGPUReadback = B(c, true, "c - Advanced Settings", "Use async GPU readback", "Avoids blocking the main thread while reading the light camera texture. Disable to use a blocking readback.");
            ExcludeSky = B(c, true, "c - Advanced Settings", "Exclude sky dome", "Hides EFT's sky dome renderers while the light camera renders. The dome ignores the far plane and drew white at night, inflating the score.");
            IgnoreTransparentPixels = B(c, true, "c - Advanced Settings", "Ignore transparent pixels", "Ignores transparent clear/background pixels when calculating luma and color depth");

            RedLumaMulti = F(c, 0.79f, "d - Color Settings", "1-Red luma multi", "Red color in pixel analysis is multiplied by this to produce the luma calculation", 0f, 1f);
            GreenLumaMulti = F(c, 0.29f, "d - Color Settings", "2-Green luma multi", "Green color in pixel analysis is multiplied by this to produce the luma calculation", 0f, 1f);
            BlueLumaMulti = F(c, 0.93f, "d - Color Settings", "3-Blue luma multi", "Blue color in pixel analysis is multiplied by this to produce the luma calculation", 0f, 1f);
            RedColorDepthMulti = F(c, 0.79f, "d - Color Settings", "4-Red color depth multi", "Red color range in pixel analysis is multiplied by this to produce the color depth calculation", 0f, 1f);
            GreenColorDepthMulti = F(c, 0.29f, "d - Color Settings", "5-Green color depth multi", "Green color range in pixel analysis is multiplied by this to produce the color depth calculation", 0f, 1f);
            BlueColorDepthMulti = F(c, 0.93f, "d - Color Settings", "6-Blue color depth multi", "Blue color range in pixel analysis is multiplied by this to produce the color depth calculation", 0f, 1f);

            CamHorizontalOffset = F(c, 4f, "e - Camera Rig Settings", "Camera horizontal offset", "Distance between the camera and the player focus point on horizontal plane", 0.1f, 5f);
            CameraFocusHeightOffset = F(c, -0.7f, "e - Camera Rig Settings", "Camera focus height offset", "Vertical offset from the player's ribcage bone. Negative values focus lower on the chest.", -1f, 1f);
            ForceTargetPlayerRenderers = B(c, true, "e - Camera Rig Settings", "Force target player renderers", "Temporarily forces the sampled player's renderers visible and renderable by the light camera, then restores them");
            UseOrbitCameraSampling = B(c, true, "e - Camera Rig Settings", "Use orbit camera sampling", "Samples your player from a random orbit around the chest instead of from the nearest other player's position");
            OrbitCameraRadius = F(c, 3f, "e - Camera Rig Settings", "Orbit camera radius", "Distance from the player's chest when orbit camera sampling is enabled", 0.25f, 12f);
            OrbitCameraHeightOffset = F(c, 2f, "e - Camera Rig Settings", "Orbit camera height (looks down)", "Camera height above the player's chest. Higher = steeper downward view = less sky/empty distance in frame. At radius 3: 2 gives about 34 degrees down.", -1f, 4f);
            RejectOccludedSamples = B(c, true, "e - Camera Rig Settings", "Reject occluded samples", "Skips a light camera sample when world geometry blocks the ray from the light camera to the player's chest");
            ExcludeOpticRenderers = B(c, true, "e - Camera Rig Settings", "Exclude optic renderers", "Hides ranged optic item renderers while the light camera renders");

            IsDebug = B(c, false, "y - Debug", "1) Enable debug logging", "Also logs sample cost (ms) every few seconds");
            DebugUpdateFreq = F(c, 1f, "y - Debug", "2) Debug updates per second", "How frequently the debug logger updates per second", 1f, 10f);
            VerboseDebug = B(c, false, "y - Debug", "2b) Verbose on-screen debug", "Prints detailed meter info on screen: scores, average, vision multiplier, demand/sampling state, role, perf, camera settings");
            ShowRenderTexturePreview = B(c, false, "y - Debug", "3) Show render texture preview", "Draws the light-meter render texture in the game window for debugging");
            RenderTexturePreviewSize = F(c, 256f, "y - Debug", "4) Render texture preview size", "Size of the render texture debug preview in pixels", 64f, 512f);
            UseFixedOrbitAngle = B(c, false, "y - Debug", "5) Use fixed orbit angle", "Uses the configured orbit angle instead of a random orbit angle for the actual light-meter sample");
            FixedOrbitAngle = F(c, 0f, "y - Debug", "6) Fixed orbit angle", "Camera angle around the sampled player's chest when fixed orbit sampling is enabled", 0f, 360f);
            DumpRenderTexture = B(c, false, "y - Debug", "7) Dump next frame to PNG", "Writes the next metered frame to BepInEx\\ombarella_rgb.png and ombarella_alpha.png (8x upscaled), logs pixel stats, then turns itself off");

            RemoveObsolete(c);
            Refresh();
            MasterSwitch.SettingChanged += (s, e) => Refresh();
            MeterAttenuationCoef.SettingChanged += (s, e) => Refresh();
            AimNerf.SettingChanged += (s, e) => Refresh();
        }

        static void Refresh()
        {
            Enabled = MasterSwitch.Value;
            Strength = MeterAttenuationCoef.Value;
            AimNerfValue = AimNerf.Value;
        }

        static void RemoveObsolete(ConfigFile c)
        {
            bool removed = false;
            removed |= Rm(c, "a - Toggles", "Use Fika player averaging"); // replaced by true per-client Fika sync
            removed |= Rm(c, "a - Toggles", "Use Luma meter");
            removed |= Rm(c, "c - Advanced Settings", "Analysis exposure multiplier");
            removed |= Rm(c, "c - Advanced Settings", "Player render fill intensity");
            removed |= Rm(c, "c - Advanced Settings", "Environment render fill intensity");
            removed |= Rm(c, "c - Advanced Settings", "Use light camera fill light");
            removed |= Rm(c, "c - Advanced Settings", "Light camera fill intensity");
            removed |= Rm(c, "c - Advanced Settings", "Exclude sky from render texture");
            removed |= Rm(c, "e - Camera Rig Settings", "Render player only");
            removed |= Rm(c, "e - Camera Rig Settings", "Exclude in-hands item renderers");
            removed |= Rm(c, "d - Color Settings", "4-Red breadth multi");
            removed |= Rm(c, "d - Color Settings", "5-Green breadth multi");
            removed |= Rm(c, "d - Color Settings", "6-Blue breadth multi");
            removed |= Rm(c, "z - Dev", "dev1");
            removed |= Rm(c, "z - Dev", "dev2");
            if (removed) c.Save();
        }

        static bool Rm(ConfigFile c, string section, string key)
        {
            var def = new ConfigDefinition(section, key);
            return c.ContainsKey(def) && c.Remove(def);
        }

        static ConfigEntry<float> F(ConfigFile c, float def, string section, string key, string desc, float min, float max)
        {
            return c.Bind(section, key, def, new ConfigDescription(desc, new AcceptableValueRange<float>(min, max), Array.Empty<object>()));
        }

        static ConfigEntry<bool> B(ConfigFile c, bool def, string section, string key, string desc)
        {
            return c.Bind(section, key, def, new ConfigDescription(desc, null, Array.Empty<object>()));
        }
    }
}
