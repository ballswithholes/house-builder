// 人物模型库测试的公用小工具：PNG 写出（RGB / RGBA，与 Tests~/map/MapRender.cs 相同的 zlib + CRC 写法）与简单的图像拼接
using System;
using System.IO;
using System.IO.Compression;

static class FigPng
{
    // rgba：行优先，第 0 行 = 图像顶边
    public static void Save(string path, byte[] rgba, int w, int h)
    {
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++) Array.Copy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4);
        File.WriteAllBytes(path, Raw(raw, w, h, 6));
    }
    static byte[] Raw(byte[] raw, int w, int h, byte colorType)
    {
        byte[] z;
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(0x78); ms.WriteByte(0x01);
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
            uint a = 1, b = 0; foreach (var x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
            uint ad = (b << 16) | a;
            ms.WriteByte((byte)(ad >> 24)); ms.WriteByte((byte)(ad >> 16)); ms.WriteByte((byte)(ad >> 8)); ms.WriteByte((byte)ad);
            z = ms.ToArray();
        }
        using (var o = new MemoryStream())
        {
            o.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            Action<string, byte[]> chunk = (type, data) =>
            {
                var tb = System.Text.Encoding.ASCII.GetBytes(type);
                var len = data.Length; o.WriteByte((byte)(len >> 24)); o.WriteByte((byte)(len >> 16)); o.WriteByte((byte)(len >> 8)); o.WriteByte((byte)len);
                o.Write(tb, 0, 4); o.Write(data, 0, data.Length);
                uint c = Crc(Crc(0xffffffff, tb), data) ^ 0xffffffff;
                o.WriteByte((byte)(c >> 24)); o.WriteByte((byte)(c >> 16)); o.WriteByte((byte)(c >> 8)); o.WriteByte((byte)c);
            };
            var ih = new byte[13];
            ih[0] = (byte)(w >> 24); ih[1] = (byte)(w >> 16); ih[2] = (byte)(w >> 8); ih[3] = (byte)w;
            ih[4] = (byte)(h >> 24); ih[5] = (byte)(h >> 16); ih[6] = (byte)(h >> 8); ih[7] = (byte)h;
            ih[8] = 8; ih[9] = colorType;
            chunk("IHDR", ih); chunk("IDAT", z); chunk("IEND", new byte[0]);
            return o.ToArray();
        }
    }
    static uint[] crcT;
    static uint Crc(uint c, byte[] d)
    {
        if (crcT == null) { crcT = new uint[256]; for (uint n = 0; n < 256; n++) { uint k = n; for (int i = 0; i < 8; i++) k = (k & 1) != 0 ? 0xedb88320 ^ (k >> 1) : k >> 1; crcT[n] = k; } }
        foreach (var b in d) c = crcT[(c ^ b) & 0xff] ^ (c >> 8);
        return c;
    }

    // 图像（RGBA8，第 0 行 = 顶边）
    public sealed class Img
    {
        public int w, h; public byte[] d;
        public Img(int w, int h) { this.w = w; this.h = h; d = new byte[w * h * 4]; for (int i = 3; i < d.Length; i += 4) d[i] = 255; }
        public Img(int w, int h, byte[] d) { this.w = w; this.h = h; this.d = d; }
        public void Fill(byte r, byte g, byte b) { for (int i = 0; i < w * h; i++) { d[i * 4] = r; d[i * 4 + 1] = g; d[i * 4 + 2] = b; d[i * 4 + 3] = 255; } }
        // 把 src 的 (sx, sy, sw, sh) 区域缩放 k 倍（最近邻；k < 1 时取平均）放到 (dx, dy)
        public void Blit(Img src, int dx, int dy, double k = 1, int sx = 0, int sy = 0, int sw = -1, int sh = -1)
        {
            if (sw < 0) sw = src.w; if (sh < 0) sh = src.h;
            int ow = (int)Math.Round(sw * k), oh = (int)Math.Round(sh * k);
            for (int y = 0; y < oh; y++)
            {
                int ty = dy + y; if (ty < 0 || ty >= h) continue;
                for (int x = 0; x < ow; x++)
                {
                    int tx = dx + x; if (tx < 0 || tx >= w) continue;
                    double fx0 = sx + x / k, fy0 = sy + y / k, fx1 = sx + (x + 1) / k, fy1 = sy + (y + 1) / k;
                    int x0 = (int)fx0, y0 = (int)fy0, x1 = Math.Max(x0 + 1, (int)Math.Ceiling(fx1)), y1 = Math.Max(y0 + 1, (int)Math.Ceiling(fy1));
                    double r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int yy = y0; yy < y1 && yy < src.h; yy++) for (int xx = x0; xx < x1 && xx < src.w; xx++)
                    { int i = (yy * src.w + xx) * 4; r += src.d[i]; g += src.d[i + 1]; b += src.d[i + 2]; a += src.d[i + 3]; n++; }
                    if (n == 0) continue;
                    int o = (ty * w + tx) * 4;
                    // 透明像素按棋盘格底色合成（面部贴图画布底色透明）
                    double al = a / n / 255, bg = (((tx >> 3) + (ty >> 3)) & 1) != 0 ? 200 : 150;
                    d[o] = (byte)Math.Round(r / n * al + bg * (1 - al)); d[o + 1] = (byte)Math.Round(g / n * al + bg * (1 - al)); d[o + 2] = (byte)Math.Round(b / n * al + bg * (1 - al)); d[o + 3] = 255;
                }
            }
        }
        public void Save(string path) { FigPng.Save(path, d, w, h); }
    }
    // 取单个通道显示成灰度（c = 0..3）
    public static Img Channel(Img src, int c)
    {
        var o = new Img(src.w, src.h);
        for (int i = 0; i < src.w * src.h; i++) { byte v = src.d[i * 4 + c]; o.d[i * 4] = o.d[i * 4 + 1] = o.d[i * 4 + 2] = v; o.d[i * 4 + 3] = 255; }
        return o;
    }
    // 上下翻转（DataTexture 第 0 行 = v 0 → 图像顶边在上）
    public static Img FlipY(Img src)
    {
        var o = new Img(src.w, src.h);
        for (int y = 0; y < src.h; y++) Array.Copy(src.d, (src.h - 1 - y) * src.w * 4, o.d, y * src.w * 4, src.w * 4);
        return o;
    }
    // 差异图：|a − b| × k（RGB）
    public static Img Diff(Img a, Img b, double k, bool alpha = false)
    {
        var o = new Img(a.w, a.h);
        for (int i = 0; i < a.w * a.h; i++)
            for (int c = 0; c < 3; c++)
            {
                int ci = alpha ? 3 : c;
                o.d[i * 4 + c] = (byte)Math.Min(255, Math.Abs(a.d[i * 4 + ci] - b.d[i * 4 + ci]) * k);
            }
        return o;
    }
}
