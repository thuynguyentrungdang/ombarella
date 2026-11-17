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
        // Centre weighting (frame is Width x Width, camera aims at the player so the player sits in the middle).
        // CenterFocus 0 or Width 0 = uniform (old behaviour). CenterSigma = gaussian sigma as a fraction of the half-frame.
        public int Width;
        public float CenterFocus, CenterSigma;
    }

    /// <summary>Pure pixel to score. No Unity types so it unit-tests.</summary>
    public static class ScoreCalculator
    {
        public const float MinScore = 0.01f;

        /// <summary>rgba = tightly packed RGBA32. False when no usable pixel.</summary>
        public static bool TryCalculate(ReadOnlySpan<byte> rgba, in ScoreSettings s, out float score, out float luma, out float colorDepth)
        {
            score = luma = colorDepth = 0f;
            double sumR = 0, sumG = 0, sumB = 0, sumW = 0;
            int rLow = 255, gLow = 255, bLow = 255, rHigh = 0, gHigh = 0, bHigh = 0;
            int count = 0, rangeCount = 0;
            bool skipClear = s.IgnoreTransparentPixels;

            // Weighted mean for luma. A range (max - min) cannot be weighted, so colour depth only looks at the central disc.
            bool weighted = s.CenterFocus > 0f && s.Width > 0 && rgba.Length == s.Width * s.Width * 4 && Weights(s.Width, s.CenterFocus, s.CenterSigma);

            for (int i = 0; i + 3 < rgba.Length; i += 4)
            {
                // old rule a/255 <= 0.001 only true when a == 0
                if (skipClear && rgba[i + 3] == 0) continue;
                int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                count++;
                float w = weighted ? _w[i >> 2] : 1f;
                sumR += r * w; sumG += g * w; sumB += b * w; sumW += w;
                if (weighted && !_disc[i >> 2]) continue;
                rangeCount++;
                if (r < rLow) rLow = r;
                if (g < gLow) gLow = g;
                if (b < bLow) bLow = b;
                if (r > rHigh) rHigh = r;
                if (g > gHigh) gHigh = g;
                if (b > bHigh) bHigh = b;
            }
            if (count == 0 || sumW <= 0) return false;

            float l = (float)((sumR * s.RedLumaMulti + sumG * s.GreenLumaMulti + sumB * s.BlueLumaMulti) / (255.0 * sumW));
            if (float.IsNaN(l) || float.IsInfinity(l)) return false;
            // no opaque pixel in the disc = no contrast information there
            float cd = rangeCount == 0 ? 0f : ((rHigh - rLow) * s.RedColorDepthMulti + (gHigh - gLow) * s.GreenColorDepthMulti + (bHigh - bLow) * s.BlueColorDepthMulti) / (255f * 3f);

            luma = Clamp01(l * s.LumaCoef);
            colorDepth = Clamp01(cd);
            score = Clamp(luma + colorDepth * s.DepthWeight, MinScore, 1f);
            return true;
        }

        // Cached per (width, focus, sigma); sampling runs on the main thread (readback callback), so no locking.
        static float[] _w;
        static bool[] _disc;
        static int _kw;
        static float _kf, _ks;
        const float DiscGauss = 0.25f; // disc = pixels where the gaussian >= this (r <= 1.67 sigma). Guess.

        static bool Weights(int width, float focus, float sigma)
        {
            if (sigma < 0.05f) sigma = 0.05f;
            if (_w != null && _kw == width && _kf == focus && _ks == sigma) return true;
            int n = width * width;
            var w = new float[n];
            var disc = new bool[n];
            float inv = 1f / (2f * sigma * sigma);
            for (int y = 0; y < width; y++)
            {
                float dy = (y + 0.5f) / width * 2f - 1f;
                for (int x = 0; x < width; x++)
                {
                    float dx = (x + 0.5f) / width * 2f - 1f;
                    float g = (float)Math.Exp(-(dx * dx + dy * dy) * inv);
                    w[y * width + x] = (1f - focus) + focus * g;
                    disc[y * width + x] = g >= DiscGauss;
                }
            }
            _w = w; _disc = disc; _kw = width; _kf = focus; _ks = sigma;
            return true;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
