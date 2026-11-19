namespace ombarella.Core
{
    public static class MeterMath
    {
        /// <summary>
        /// Extremes curve: identity inside [lo, hi]; past an edge the score is pushed further out, quadratically
        /// (s + k*(s-hi)^2 above, s - k*(lo-s)^2 below), clamped to [0.01, 1]. Slope is 1 at both edges (no kink).
        /// k = 0 = identity.
        /// </summary>
        public static float Curve(float raw, float lo, float hi, float k)
        {
            float v = raw;
            if (v > hi) { float d = v - hi; v += k * d * d; }
            else if (v < lo) { float d = lo - v; v -= k * d * d; }
            return v < 0.01f ? 0.01f : (v > 1f ? 1f : v);
        }

        /// <summary>strength 1 = raw meter, 0 = neutral (1).</summary>
        public static float Attenuate(float raw, float strength)
        {
            return raw + (1f - raw) * (1f - strength);
        }

        /// <summary>Extra aim-spread multiplier = (1 - final) * 100 * aimNerf. 0 at full light or aimNerf 0 (penalty is dark-only).</summary>
        public static float AimOffsetScale(float finalMeter, float aimNerf)
        {
            return (1f - finalMeter) * 100f * aimNerf;
        }

        public const float AimRampFull = 0.1f; // score at or below this = full aim penalty

        /// <summary>
        /// Aim penalty with score ramp: full at score <= AimRampFull, fades linearly to 0 at rampStart, none above it.
        /// rampStart <= AimRampFull = hard step at AimRampFull.
        /// </summary>
        public static float AimOffsetScale(float finalMeter, float aimNerf, float rampStart)
        {
            float ramp = rampStart <= AimRampFull
                ? (finalMeter <= AimRampFull ? 1f : 0f)
                : (rampStart - finalMeter) / (rampStart - AimRampFull);
            ramp = ramp < 0f ? 0f : (ramp > 1f ? 1f : ramp);
            return AimOffsetScale(finalMeter, aimNerf) * ramp;
        }

        /// <summary>
        /// Vision-speed multiplier for a bot looking at a player. Power curve (human latency ~ luminance^-k, so 0.5 not linear),
        /// then strength blend, then NVG blend, then faded in by distance: no effect at or below nearM, full at farM and beyond.
        /// </summary>
        public static float VisionSpeed(float raw, float exponent, float strength, bool nvg, float distance, float nearM, float farM)
        {
            float v = (float)System.Math.Pow(raw < 0.01f ? 0.01f : raw, exponent);
            v = Attenuate(v, strength);
            if (nvg) v = NightVisionBlend(v);
            float ramp = farM <= nearM ? 1f : (distance - nearM) / (farM - nearM);
            ramp = ramp < 0f ? 0f : (ramp > 1f ? 1f : ramp);
            return 1f + (v - 1f) * ramp;
        }

        public static float NightVisionBlend(float finalMeter)
        {
            return finalMeter + (1f - finalMeter) * 0.7f;
        }
    }
}
