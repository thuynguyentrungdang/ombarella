namespace ombarella
{
    /// <summary>ThatsLit-style meter: "  ▱▱▱▱▱▱▱▱▱▱  ▰▰▰▱▱▱▱▱▱▱▱". Left half stays empty (this meter is one-sided), right half fills 0..10.</summary>
    public static class Visualiser
    {
        const string Empty = "▱";
        const string Full = "▰";
        static readonly string[] Levels = Build();

        static string[] Build()
        {
            var levels = new string[11];
            for (int n = 0; n <= 10; n++)
            {
                var sb = new System.Text.StringBuilder("  ");
                for (int i = 0; i < 10; i++) sb.Append(Empty);
                sb.Append("  ");
                for (int i = 0; i < 10; i++) sb.Append(i < n ? Full : Empty);
                levels[n] = sb.ToString();
            }
            return levels;
        }

        /// <summary>Cached strings, no per-frame allocation. averageLight 0..1 (same 0.0999 step as ThatsLit).</summary>
        public static string GetLevelString(float averageLight, bool isDebug)
        {
            if (isDebug) return "debug value = " + averageLight;
            if (float.IsNaN(averageLight) || averageLight <= 0f) return Levels[0];
            int n = (int)(averageLight / 0.0999f);
            return Levels[n < 0 ? 0 : (n > 10 ? 10 : n)];
        }
    }
}
