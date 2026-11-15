namespace ombarella.Core
{
    public static class MeterMath
    {
        /// <summary>strength 1 = raw meter, 0 = neutral (1).</summary>
        public static float Attenuate(float raw, float strength)
        {
            return raw + (1f - raw) * (1f - strength);
        }

        /// <summary>Preserved 3.11/4.0 behaviour: offset multiplier = (1 - final) * 100 * aimNerf.</summary>
        public static float AimOffsetScale(float finalMeter, float aimNerf)
        {
            return (1f - finalMeter) * 100f * aimNerf;
        }

        public static float NightVisionBlend(float finalMeter)
        {
            return finalMeter + (1f - finalMeter) * 0.7f;
        }
    }
}
