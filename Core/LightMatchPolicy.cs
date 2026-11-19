namespace ombarella.Core
{
    /// <summary>How the meter camera is made to see the light the player sees. Off = EFT's own flat ambient (as before).</summary>
    public enum LightMatchMode { Off = 0, Attach = 1 }

    public enum LightMatchState
    {
        Off,          // mode Off
        Attached,     // TraceSix's light pass goes on the meter camera
        NoTraceSix,   // Attach asked for, TraceSix plugin not loaded
        NoBridge,     // Attach asked for, this build has no TraceSix reference (built without TraceSix.dll)
        ApiTooOld,    // TraceSix loaded but its API is older than we need, or missing
    }

    /// <summary>Pure decision, no Unity/BepInEx types: which state a requested mode ends in.</summary>
    public static class LightMatchPolicy
    {
        public const int MinApiVersion = 1;

        /// <param name="apiVersion">TraceSixApi.Version, or -1 when it could not be read.</param>
        public static LightMatchState Resolve(LightMatchMode mode, bool pluginLoaded, bool bridgeCompiled, int apiVersion)
        {
            if (mode != LightMatchMode.Attach) return LightMatchState.Off;
            if (!pluginLoaded) return LightMatchState.NoTraceSix;
            if (!bridgeCompiled) return LightMatchState.NoBridge;
            if (apiVersion < MinApiVersion) return LightMatchState.ApiTooOld;
            return LightMatchState.Attached;
        }
    }
}
