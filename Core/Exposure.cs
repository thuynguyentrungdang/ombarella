using System;

namespace ombarella.Core
{
    /// <summary>Pure exposure on an RGBA32 buffer: rgb * gain, clipped at white like a camera. Alpha untouched. No Unity types so it unit-tests.</summary>
    public static class Exposure
    {
        public static float GainFromEv(float ev) { return (float)Math.Pow(2.0, ev); }

        public static void Apply(ReadOnlySpan<byte> src, Span<byte> dst, float gain)
        {
            int n = Math.Min(src.Length, dst.Length);
            for (int i = 0; i + 3 < n; i += 4)
            {
                dst[i] = Scale(src[i], gain);
                dst[i + 1] = Scale(src[i + 1], gain);
                dst[i + 2] = Scale(src[i + 2], gain);
                dst[i + 3] = src[i + 3];
            }
        }

        static byte Scale(byte v, float gain)
        {
            float f = v * gain + 0.5f;
            return f >= 255f ? (byte)255 : (byte)f;
        }
    }
}
