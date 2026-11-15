using System;
using System.IO;

namespace ombarella.Core
{
    /// <summary>Minimal uncompressed PNG encoder for debug dumps (no Unity ImageConversion dependency).</summary>
    public static class PngWriter
    {
        /// <summary>rgba = tightly packed RGBA32, bottom row first (Unity readback). alphaAsGray = write the alpha channel as gray instead of RGB. scale = nearest-neighbour upscale.</summary>
        public static byte[] Encode(ReadOnlySpan<byte> rgba, int size, bool alphaAsGray, int scale)
        {
            if (scale < 1) scale = 1;
            int outSize = size * scale;
            int stride = outSize * 3 + 1;
            var raw = new byte[stride * outSize];
            for (int y = 0; y < outSize; y++)
            {
                int srcY = size - 1 - (y / scale); // flip: readback is bottom-up
                int row = y * stride;
                raw[row] = 0; // filter none
                for (int x = 0; x < outSize; x++)
                {
                    int s = (srcY * size + x / scale) * 4;
                    int d = row + 1 + x * 3;
                    if (alphaAsGray) { raw[d] = raw[d + 1] = raw[d + 2] = rgba[s + 3]; }
                    else { raw[d] = rgba[s]; raw[d + 1] = rgba[s + 1]; raw[d + 2] = rgba[s + 2]; }
                }
            }

            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13];
                BE(ihdr, 0, outSize); BE(ihdr, 4, outSize);
                ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
                Chunk(ms, "IHDR", ihdr);
                Chunk(ms, "IDAT", Zlib(raw));
                Chunk(ms, "IEND", new byte[0]);
                return ms.ToArray();
            }
        }

        static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x01);
                int pos = 0;
                while (pos < data.Length)
                {
                    int n = Math.Min(65535, data.Length - pos);
                    bool last = pos + n >= data.Length;
                    ms.WriteByte(last ? (byte)1 : (byte)0); // stored block
                    ms.WriteByte((byte)(n & 0xFF)); ms.WriteByte((byte)(n >> 8));
                    ms.WriteByte((byte)(~n & 0xFF)); ms.WriteByte((byte)((~n >> 8) & 0xFF));
                    ms.Write(data, pos, n);
                    pos += n;
                }
                uint a = 1, b = 0;
                for (int i = 0; i < data.Length; i++) { a = (a + data[i]) % 65521; b = (b + a) % 65521; }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BE(len, 0, data.Length);
            s.Write(len, 0, 4);
            var body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, body, 4, data.Length);
            s.Write(body, 0, body.Length);
            var crc = new byte[4]; BE(crc, 0, (int)Crc32(body));
            s.Write(crc, 0, 4);
        }

        static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static uint[] _table;
        static uint Crc32(byte[] data)
        {
            if (_table == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                _table = t;
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++) crc = _table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
