// 无头头像测试（不依赖 UnityEngine；与 View/Raster.cs、View/Portrait.cs、Data/Generated/PortraitData.g.cs、
// Data/ScenarioData.cs 一起以 -define:SANGUO_HEADLESS 编译，见 run.sh）
//   mono raster.exe data                         外貌数据完整性（条目数 + 哈希与导出工具一致）
//   mono raster.exe svgdiff cases.json js.json   C# 生成的 SVG 与网页版逐条比对（去掉 id 流水号）
//   mono raster.exe sheet cases.json out.png [cols] [cell]  渲染联系表 PNG，并报告每张耗时
//   mono raster.exe bench [n] [px]               n 张 px 像素头像的生成 + 栅格化耗时
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Sanguo;

static class RasterHarness
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string mode = args.Length > 0 ? args[0] : "data";
        try
        {
            switch (mode)
            {
                case "data": return Data();
                case "svgdiff": return SvgDiff(args[1], args[2]);
                case "sheet": return Sheet(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 8, args.Length > 4 ? int.Parse(args[4]) : 128);
                case "bench": return Bench(args.Length > 1 ? int.Parse(args[1]) : 300, args.Length > 2 ? int.Parse(args[2]) : 128);
                case "svg":   // mono raster.exe svg in.svg out.png px：任意 SVG（头像所用子集）→ PNG
                    {
                        var r = Portrait.RenderSvgDocument(File.ReadAllText(args[1], Encoding.UTF8), int.Parse(args[3]));
                        File.WriteAllBytes(args[2], Png.Encode(r.ToRGBA8(), r.W, r.H));
                        return 0;
                    }
            }
        }
        catch (Exception e) { Console.WriteLine("异常：" + e); return 1; }
        Console.WriteLine("未知模式 " + mode);
        return 1;
    }

    // ---------------------------------------------------------------- 数据完整性
    static int Data()
    {
        uint h = 2166136261;
        int n = 0;
        var src = PortraitData.Entries;
        for (int i = 0; i + 1 < src.Length; i += 2)
        {
            var e = PortraitDataStore.Get(src[i]);
            string s = src[i] + "\u0001" + JObj.ToJson(e) + "\u0002";
            foreach (char c in s) { h ^= c; h *= 16777619; }
            n++;
        }
        bool ok = n == PortraitData.Count && h == PortraitData.Hash;
        Console.WriteLine((ok ? "通过" : "失败") + $"：外貌数据 {n}/{PortraitData.Count} 条，hash 0x{h:x8} / 0x{PortraitData.Hash:x8}；文化 {Portrait.Cultures.Length} 种");
        return ok ? 0 : 1;
    }

    // ---------------------------------------------------------------- 用例
    sealed class Case { public PortraitGen gen; public PortraitOpts o; public string label; }
    static List<Case> LoadCases(string path)
    {
        var list = (List<object>)JObj.ParseJson(File.ReadAllText(path, Encoding.UTF8));
        var o = new List<Case>();
        foreach (JObj it in list)
        {
            var g = it.O("gen"); var op = it.O("opts") ?? new JObj();
            var gen = new PortraitGen { name = g.S("name"), culture = g.S("culture"), sex = g.S("sex"), portrait = g.O("portrait"), color = g.S("color") };
            if (g.Has("war")) gen.war = g.D("war");
            if (g.Has("intel")) gen.intel = g.D("intel");
            if (g.Has("pol")) gen.pol = g.D("pol");
            if (g.Has("born")) gen.born = g.D("born");
            var opts = PortraitOpts.Make((int)op.Or("size", 96), op.S("color"), op.Or("mood", "neutral"), !op.IsFalse("frame"), op.T("flip"), op.Or("scale", 1), gen);
            o.Add(new Case { gen = gen, o = opts, label = it.S("label") ?? gen.name });
        }
        return o;
    }

    // ---------------------------------------------------------------- SVG 逐条比对
    static readonly Regex UID = new Regex("q[0-9a-z]+(?=[a-z]*[\"#)])");
    static int SvgDiff(string casesPath, string jsPath)
    {
        var cases = LoadCases(casesPath);
        var js = (List<object>)JObj.ParseJson(File.ReadAllText(jsPath, Encoding.UTF8));
        int same = 0, numOnly = 0, bad = 0, shown = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < cases.Count; i++)
        {
            string cs = UID.Replace(Portrait.SvgSource(cases[i].gen, cases[i].o), "");
            string j = (string)js[i];
            if (cs == j) { same++; continue; }
            // 只差个别 0.1 的舍入（V8 与 Mono 的 sin/pow 末位差异）算“数值级相同”
            if (NumericClose(cs, j)) { numOnly++; continue; }
            bad++;
            if (shown++ < 4)
            {
                int k = 0; while (k < cs.Length && k < j.Length && cs[k] == j[k]) k++;
                Console.WriteLine($"[{i}] {cases[i].gen.name} 不同（C# {cs.Length} / JS {j.Length} 字符），首个差异 @{k}：");
                Console.WriteLine("  C#: " + Ctx(cs, k));
                Console.WriteLine("  JS: " + Ctx(j, k));
            }
        }
        Console.WriteLine($"SVG 比对：{cases.Count} 例，逐字相同 {same}，仅末位舍入差异 {numOnly}，不同 {bad}（{sw.ElapsedMilliseconds} ms）");
        return bad == 0 ? 0 : 1;
    }
    static string Ctx(string s, int k) { int a = Math.Max(0, k - 60); return s.Substring(a, Math.Min(s.Length - a, 140)).Replace("\n", " "); }
    static readonly Regex NUM = new Regex(@"-?\d+(?:\.\d+)?");
    // 去掉数字后结构相同，且每个数字相差 ≤ 0.1（一位小数的舍入）
    static bool NumericClose(string a, string b)
    {
        if (NUM.Replace(a, "#") != NUM.Replace(b, "#")) return false;
        var ma = NUM.Matches(a); var mb = NUM.Matches(b);
        if (ma.Count != mb.Count) return false;
        for (int i = 0; i < ma.Count; i++)
        {
            if (ma[i].Value == mb[i].Value) continue;
            double x = double.Parse(ma[i].Value, System.Globalization.CultureInfo.InvariantCulture), y = double.Parse(mb[i].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (Math.Abs(x - y) > 0.1001) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- 联系表
    static int Sheet(string casesPath, string outPng, int cols, int cell)
    {
        var cases = LoadCases(casesPath);
        int rows = (cases.Count + cols - 1) / cols, pad = 4;
        int W = cols * (cell + pad) + pad, H = rows * (cell + pad) + pad;
        var sheet = new byte[W * H * 4];
        for (int i = 0; i < sheet.Length; i += 4) { sheet[i] = 0x16; sheet[i + 1] = 0x14; sheet[i + 2] = 0x1c; sheet[i + 3] = 255; }
        var times = new List<double>();
        // 预热（JIT、背景 / 边框缓存）
        Portrait.Render(cases[0].gen, cases[0].o);
        var sb = new StringBuilder();
        for (int i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var sw = Stopwatch.StartNew();
            var r = Portrait.Render(c.gen, c.o);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            var px = r.ToRGBA8();
            int ox = pad + (i % cols) * (cell + pad), oy = pad + (i / cols) * (cell + pad);
            Blit(sheet, W, px, r.W, r.H, ox, oy);
            sb.Append($"{i,3} {c.label,-12} {c.o.px}px {sw.Elapsed.TotalMilliseconds,6:0.0} ms\n");
        }
        File.WriteAllBytes(outPng, Png.Encode(sheet, W, H));
        times.Sort();
        double sum = 0; foreach (var t in times) sum += t;
        Console.Write(sb);
        Console.WriteLine($"联系表 {cases.Count} 张 → {outPng}（{W}×{H}）；每张耗时 平均 {sum / times.Count:0.0} ms，中位 {times[times.Count / 2]:0.0} ms，最长 {times[times.Count - 1]:0.0} ms");
        return 0;
    }
    // 非预乘 RGBA 叠到不透明底上
    static void Blit(byte[] dst, int dw, byte[] src, int sw, int sh, int ox, int oy)
    {
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int s = (y * sw + x) * 4, d = ((oy + y) * dw + ox + x) * 4;
                int a = src[s + 3];
                for (int k = 0; k < 3; k++) dst[d + k] = (byte)((src[s + k] * a + dst[d + k] * (255 - a) + 127) / 255);
                dst[d + 3] = 255;
            }
    }

    // ---------------------------------------------------------------- 性能
    static int Bench(int n, int px)
    {
        var cultures = Portrait.Cultures;
        var list = new List<PortraitGen>();
        // 经典剧本武将 + 补足的随机文化样例（同 tests/portraits.html 的性能测试）
        foreach (var line in ScenarioData.Generals)
        {
            var p = line.Split('|');
            list.Add(new PortraitGen { name = p[0], war = double.Parse(p[1]), intel = double.Parse(p[2]), pol = double.Parse(p[3]) });
        }
        int i = 0;
        while (list.Count < n)
        {
            string c = cultures[i % cultures.Length];
            list.Add(new PortraitGen { name = "〔性能〕" + c + i, culture = c, war = 30 + (i * 37) % 70, intel = 30 + (i * 53) % 70, pol = 30 + (i * 29) % 70, sex = i % 7 == 0 ? "f" : "m" });
            i++;
        }
        if (list.Count > n) list.RemoveRange(n, list.Count - n);
        var o = PortraitOpts.Make(px, "#5a6a8a");
        Portrait.Render(list[0], o);
        var sw = Stopwatch.StartNew();
        double svgMs = 0, worst = 0;
        foreach (var g in list)
        {
            var t0 = sw.Elapsed.TotalMilliseconds;
            Portrait.SvgSource(g, o);
            var t1 = sw.Elapsed.TotalMilliseconds;
            Portrait.Render(g, o);
            var t2 = sw.Elapsed.TotalMilliseconds;
            svgMs += t1 - t0;
            worst = Math.Max(worst, t2 - t1);
        }
        double total = sw.Elapsed.TotalMilliseconds;
        double renderMs = total - svgMs;
        Console.WriteLine($"性能：{list.Count} 张 {px}px，栅格化（含生成 SVG）合计 {renderMs:0} ms = 每张 {renderMs / list.Count:0.0} ms（最长 {worst:0.0} ms）；单独生成 SVG 每张 {svgMs / list.Count:0.00} ms");
        return 0;
    }
}

// 最小 PNG 编码器：RGBA8、无滤波；zlib = 2 字节头 + Deflate（System.IO.Compression）+ Adler-32
static class Png
{
    public static byte[] Encode(byte[] rgba, int w, int h)
    {
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; Buffer.BlockCopy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4); }
        byte[] def;
        using (var ms = new MemoryStream())
        {
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
            def = ms.ToArray();
        }
        uint a = 1, b = 0;
        foreach (var x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
        var z = new byte[def.Length + 6];
        z[0] = 0x78; z[1] = 0x9c;
        Buffer.BlockCopy(def, 0, z, 2, def.Length);
        uint ad = (b << 16) | a;
        z[z.Length - 4] = (byte)(ad >> 24); z[z.Length - 3] = (byte)(ad >> 16); z[z.Length - 2] = (byte)(ad >> 8); z[z.Length - 1] = (byte)ad;
        using (var o = new MemoryStream())
        {
            o.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
            Chunk(o, "IHDR", ihdr); Chunk(o, "IDAT", z); Chunk(o, "IEND", new byte[0]);
            return o.ToArray();
        }
    }
    static void BE(byte[] b, int i, uint v) { b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v; }
    static void Chunk(Stream o, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); o.Write(len, 0, 4);
        var t = Encoding.ASCII.GetBytes(type);
        o.Write(t, 0, 4); o.Write(data, 0, data.Length);
        uint c = Crc(Crc(0xffffffff, t), data) ^ 0xffffffff;
        var cb = new byte[4]; BE(cb, 0, c); o.Write(cb, 0, 4);
    }
    static uint[] table;
    static uint Crc(uint c, byte[] d)
    {
        if (table == null)
        {
            table = new uint[256];
            for (uint n = 0; n < 256; n++) { uint k = n; for (int i = 0; i < 8; i++) k = (k & 1) != 0 ? 0xedb88320 ^ (k >> 1) : k >> 1; table[n] = k; }
        }
        foreach (var x in d) c = table[(c ^ x) & 0xff] ^ (c >> 8);
        return c;
    }
}
