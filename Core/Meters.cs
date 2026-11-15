namespace ombarella
{
    /// <summary>Shared state: per-player meters (patches read it) and the local player's value (HUD).</summary>
    public static class Meters
    {
        public static readonly ombarella.Core.VisibilityRegistry Registry = new ombarella.Core.VisibilityRegistry();

        /// <summary>Local player's raw averaged meter, for the HUD.</summary>
        public static float LocalRaw = 1f;
    }
}
