namespace ombarella
{
    /// <summary>Shared state: per-player meters (patches read it) and the local player's value (HUD).</summary>
    public static class Meters
    {
        public static readonly ombarella.Core.VisibilityRegistry Registry = new ombarella.Core.VisibilityRegistry();

        /// <summary>Local player's raw averaged meter, for the HUD.</summary>
        public static float LocalRaw = 1f;

        /// <summary>Time.time as of the last poll tick (4 Hz). Hot-path patches read this instead of the native Time.time call; staleness windows are seconds, so the lag is irrelevant.</summary>
        public static float Now;
    }
}
