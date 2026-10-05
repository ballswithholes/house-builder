// Minimal PNG codec: writes 8-bit RGB, reads 8-bit RGB / RGBA / grey(+alpha) / palette, non-interlaced (all the
// repo's art). Rows are flipped on load so row 0 is the bottom, like Unity textures.
using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Lanternvale.Preview
{
    public static class Png
    {
        public static void SaveRgb(string path, int w, int h, byte[] rgbTopDown)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(path);
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
            Chunk(fs, "IHDR", ihdr);
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true))
            {
                // adaptive filtering (libpng's heuristic): per row, the filter with the smallest sum of |signed bytes|
                int n = w * 3;
                var prev = new byte[n];
                var cand = new byte[5][];
                for (int f = 0; f < 5; f++) cand[f] = new byte[n + 1];
                for (int y = 0; y < h; y++)
                {
                    int o = y * n;
                    long best = long.MaxValue;
                    int bestF = 0;
                    for (int f = 0; f < 5; f++)
                    {
                        var line = cand[f];
                        line[0] = (byte)f;
                        long sum = 0;
                        for (int i = 0; i < n; i++)
                        {
                            int x = rgbTopDown[o + i];
                            int a = i >= 3 ? rgbTopDown[o + i - 3] : 0, b = prev[i], c = i >= 3 ? prev[i - 3] : 0;
                            int pred = f switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => Paeth(a, b, c) };
                            byte v = (byte)(x - pred);
                            line[i + 1] = v;
                            sum += v < 128 ? v : 256 - v;
                        }
                        if (sum < best) { best = sum; bestF = f; }
                    }
                    z.Write(cand[bestF], 0, n + 1);
                    Buffer.BlockCopy(rgbTopDown, o, prev, 0, n);
                }
            }
            Chunk(fs, "IDAT", ms.ToArray());
            Chunk(fs, "IEND", Array.Empty<byte>());
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        public static Texture2D Load(string path)
        {
            try { return Decode(File.ReadAllBytes(path)); }
            catch (Exception e) { Debug.LogWarning($"png {path}: {e.Message}"); return null; }
        }

        public static Texture2D Decode(byte[] data)
        {
            int pos = 8, w = 0, h = 0, depth = 0, type = 0, interlace = 0;
            byte[] palette = null, trns = null;
            var idat = new MemoryStream();
            while (pos + 8 <= data.Length)
            {
                int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
                string kind = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                int body = pos + 8;
                if (kind == "IHDR")
                {
                    w = (data[body] << 24) | (data[body + 1] << 16) | (data[body + 2] << 8) | data[body + 3];
                    h = (data[body + 4] << 24) | (data[body + 5] << 16) | (data[body + 6] << 8) | data[body + 7];
                    depth = data[body + 8]; type = data[body + 9]; interlace = data[body + 12];
                }
                else if (kind == "PLTE") { palette = new byte[len]; Array.Copy(data, body, palette, 0, len); }
                else if (kind == "tRNS") { trns = new byte[len]; Array.Copy(data, body, trns, 0, len); }
                else if (kind == "IDAT") idat.Write(data, body, len);
                else if (kind == "IEND") break;
                pos = body + len + 4;
            }
            if (depth != 8 || interlace != 0) throw new NotSupportedException($"png depth {depth} interlace {interlace}");
            int ch = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException("png type " + type) };
            int stride = w * ch;
            var raw = new byte[(stride + 1) * h];
            idat.Position = 0;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length) { int n = z.Read(raw, read, raw.Length - read); if (n <= 0) break; read += n; }
            }
            var cur = new byte[stride];
            var prev = new byte[stride];
            var tex = new Texture2D(w, h);
            for (int y = 0; y < h; y++)
            {
                int f = raw[y * (stride + 1)];
                int o = y * (stride + 1) + 1;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= ch ? cur[i - ch] : 0, b = prev[i], c = i >= ch ? prev[i - ch] : 0;
                    int x = raw[o + i];
                    switch (f)
                    {
                        case 1: x += a; break;
                        case 2: x += b; break;
                        case 3: x += (a + b) >> 1; break;
                        case 4:
                        {
                            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                            x += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                            break;
                        }
                    }
                    cur[i] = (byte)x;
                }
                int row = (h - 1 - y) * w;
                for (int px = 0; px < w; px++)
                {
                    Color32 c;
                    switch (type)
                    {
                        case 0: c = new Color32(cur[px], cur[px], cur[px], 255); break;
                        case 2: c = new Color32(cur[px * 3], cur[px * 3 + 1], cur[px * 3 + 2], 255); break;
                        case 3:
                        {
                            int k = cur[px];
                            c = new Color32(palette[k * 3], palette[k * 3 + 1], palette[k * 3 + 2], trns != null && k < trns.Length ? trns[k] : (byte)255);
                            break;
                        }
                        case 4: c = new Color32(cur[px * 2], cur[px * 2], cur[px * 2], cur[px * 2 + 1]); break;
                        default: c = new Color32(cur[px * 4], cur[px * 4 + 1], cur[px * 4 + 2], cur[px * 4 + 3]); break;
                    }
                    tex.Pixels[row + px] = c;
                }
                var t = prev; prev = cur; cur = t;
            }
            return tex;
        }

        static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
            uint crc = Crc(t, 0xffffffffu); crc = Crc(data, crc) ^ 0xffffffffu;
            var cb = new byte[4]; BE(cb, 0, (int)crc); s.Write(cb);
        }

        static uint[] table;
        static uint Crc(byte[] d, uint c)
        {
            if (table == null) { var tb = new uint[256]; for (uint n = 0; n < 256; n++) { uint k = n; for (int i = 0; i < 8; i++) k = (k & 1) != 0 ? 0xedb88320u ^ (k >> 1) : k >> 1; tb[n] = k; } table = tb; }
            foreach (var b in d) c = table[(c ^ b) & 0xff] ^ (c >> 8);
            return c;
        }
    }
}
