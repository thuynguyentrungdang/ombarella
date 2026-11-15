namespace ombarella.Core
{
    public static class MeterMath
    {
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
