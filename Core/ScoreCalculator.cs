using System;

namespace ombarella.Core
{
    /// <summary>Snapshot of score config, captured at sample time.</summary>
    public struct ScoreSettings
    {
        public float LumaCoef;
        public float RedLumaMulti, GreenLumaMulti, BlueLumaMulti;
        public float RedColorDepthMulti, GreenColorDepthMulti, BlueColorDepthMulti;
        public float DepthWeight; // score = luma + DepthWeight * colorDepth, clamped
        public bool IgnoreTransparentPixels;
    }

    /// <summary>Pure pixel to score. No Unity types so it unit-tests.</summary>
    public static class ScoreCalculator
    {
        public const float MinScore = 0.01f;

        /// <summary>rgba = tightly packed RGBA32. False when no usable pixel.</summary>
        public static bool TryCalculate(ReadOnlySpan<byte> rgba, in ScoreSettings s, out float score, out float luma, out float colorDepth)
        {
            score = luma = colorDepth = 0f;
            long sumR = 0, sumG = 0, sumB = 0;
            int rLow = 255, gLow = 255, bLow = 255, rHigh = 0, gHigh = 0, bHigh = 0;
            int count = 0;
            bool skipClear = s.IgnoreTransparentPixels;

            for (int i = 0; i + 3 < rgba.Length; i += 4)
            {
                // old rule a/255 <= 0.001 only true when a == 0
                if (skipClear && rgba[i + 3] == 0) continue;
                int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                count++;
                sumR += r; sumG += g; sumB += b;
                if (r < rLow) rLow = r;
                if (g < gLow) gLow = g;
                if (b < bLow) bLow = b;
                if (r > rHigh) rHigh = r;
                if (g > gHigh) gHigh = g;
                if (b > bHigh) bHigh = b;
            }
            if (count == 0) return false;

            float l = (sumR * s.RedLumaMulti + sumG * s.GreenLumaMulti + sumB * s.BlueLumaMulti) / (255f * count);
            if (float.IsNaN(l) || float.IsInfinity(l)) return false;
            float cd = ((rHigh - rLow) * s.RedColorDepthMulti + (gHigh - gLow) * s.GreenColorDepthMulti + (bHigh - bLow) * s.BlueColorDepthMulti) / (255f * 3f);

            luma = Clamp01(l * s.LumaCoef);
            colorDepth = Clamp01(cd);
            score = Clamp(luma + colorDepth * s.DepthWeight, MinScore, 1f);
            return true;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
