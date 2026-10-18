// 人物模型库的贴图比对：C# 的程序图集（View/FigureAtlas.cs 经 View/Raster.cs 绘制）、名将面部贴图、环境贴图
// 与网页版（tex-web.mjs 在 headless Chromium 的真 Canvas2D 上画出的结果）逐像素比较，并拼出对照图（左 网页版、中 C#、右 差 × 4）。
// 两边的抗锯齿实现不同（Skia 与 Raster.cs），边缘处有 1 像素级的差；判定用平均差与大差像素占比（阈值见下）。
// 用法：mono figuretex.exe <tex-web.mjs 的输出目录>      退出码 = 失败项数
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sanguo;

static class FigureTex
{
    static int fails;
    static readonly List<string> lines = new List<string>();
    // 平均绝对差（0–255）与差 > 40 的像素占比；c0..c1 通道
    static void Stat(byte[] a, byte[] b, int w, int x0, int y0, int rw, int rh, int c0, int c1, out double mean, out double big)
    {
        double s = 0; long n = 0, nb = 0;
        for (int y = y0; y < y0 + rh; y++) for (int x = x0; x < x0 + rw; x++)
        {
            int i = (y * w + x) * 4; bool bad = false;
            for (int c = c0; c <= c1; c++) { int d = Math.Abs(a[i + c] - b[i + c]); s += d; if (d > 40) bad = true; }
            n++; if (bad) nb++;
        }
        mean = s / (n * (c1 - c0 + 1)); big = (double)nb / n;
    }
    static void Check(string what, double mean, double big, double maxMean, double maxBig)
    {
        bool ok = mean <= maxMean && big <= maxBig;
        string l = string.Format("  {0,-28} 平均差 {1,6:F2}  大差像素 {2,6:P2}{3}", what, mean, big, ok ? "" : "   ← 超出（" + maxMean + " / " + maxBig.ToString("P0") + "）");
        lines.Add(l); Console.WriteLine(l);
        if (!ok) fails++;
    }
    static FigPng.Img Side(FigPng.Img a, FigPng.Img b, double k, bool diff = true, bool alpha = false)
    {
        int w = (int)Math.Round(a.w * k), h = (int)Math.Round(a.h * k);
        var o = new FigPng.Img(w * (diff ? 3 : 2) + (diff ? 8 : 4), h); o.Fill(40, 40, 46);
        o.Blit(a, 0, 0, k); o.Blit(b, w + 4, 0, k);
        if (diff) o.Blit(FigPng.Diff(a, b, 4, alpha), 2 * w + 8, 0, k);
        return o;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "out/figure/tex";
        var M = (JObj)JObj.ParseJson(File.ReadAllText(Path.Combine(dir, "web-tex.json")));
        int N = FigAtlas.N;
        // ---- 图集
        double t0 = FigMath.Now(); int steps = 0; double worst = 0, last = t0;
        foreach (var _ in FigAtlas.Generate()) { steps++; double t = FigMath.Now(); worst = Math.Max(worst, t - last); last = t; }
        var A = FigAtlas.Data;
        Console.WriteLine(string.Format("C# 图集：{0:F0} ms，{1} 步（单步最长 {2:F1} ms）；网页版 {3:F0} ms", FigMath.Now() - t0, steps, worst, M.D("atlasMs")));
        var wm = File.ReadAllBytes(Path.Combine(dir, "web-atlas-map.rgba")); var wn = File.ReadAllBytes(Path.Combine(dir, "web-atlas-nrm.rgba")); var wo = File.ReadAllBytes(Path.Combine(dir, "web-atlas-orm.rgba"));
        double mean, big;
        Console.WriteLine("== 图集（整张）");
        Stat(wm, A.map, N, 0, 0, N, N, 0, 2, out mean, out big); Check("反照率 RGB", mean, big, 2.5, 0.02);
        Stat(wm, A.map, N, 0, 0, N, N, 3, 3, out mean, out big); Check("染色遮罩 A", mean, big, 2.5, 0.02);
        Stat(wn, A.nrm, N, 0, 0, N, N, 0, 2, out mean, out big); Check("法线", mean, big, 2.5, 0.02);
        Stat(wo, A.orm, N, 0, 0, N, N, 0, 0, out mean, out big); Check("AO", mean, big, 2.5, 0.02);
        Stat(wo, A.orm, N, 0, 0, N, N, 1, 1, out mean, out big); Check("粗糙度", mean, big, 2.5, 0.02);
        Stat(wo, A.orm, N, 0, 0, N, N, 2, 2, out mean, out big); Check("金属度", mean, big, 2.5, 0.02);
        Console.WriteLine("== 图集（各区域，反照率 / 遮罩 / 法线 / ORM）");
        var regWorst = new List<KeyValuePair<string, double>>();
        foreach (var r in FigAtlasLayout.Order)
        {
            int ty = N - r.py - r.ph;     // 贴图行序（第 0 行 = v 0）
            double ma, ba, mm, bm, mn, bn, mo, bo;
            Stat(wm, A.map, N, r.px, ty, r.pw, r.ph, 0, 2, out ma, out ba);
            Stat(wm, A.map, N, r.px, ty, r.pw, r.ph, 3, 3, out mm, out bm);
            Stat(wn, A.nrm, N, r.px, ty, r.pw, r.ph, 0, 2, out mn, out bn);
            Stat(wo, A.orm, N, r.px, ty, r.pw, r.ph, 0, 2, out mo, out bo);
            double wv = Math.Max(Math.Max(ma, mm), Math.Max(mn, mo)), wb = Math.Max(Math.Max(ba, bm), Math.Max(bn, bo));
            regWorst.Add(new KeyValuePair<string, double>(r.name, wv + wb * 100));
            Check(r.name + string.Format("（{0:F1}/{1:F1}/{2:F1}/{3:F1}）", ma, mm, mn, mo), wv, wb, 6, 0.06);
        }
        // 对照图：半分辨率整张 + 最差的几个区域原尺寸 × 2
        Func<byte[], FigPng.Img> img = d => FigPng.FlipY(new FigPng.Img(N, N, (byte[])d.Clone()));
        Func<byte[], byte[]> opaque = d => { var o = (byte[])d.Clone(); for (int i = 3; i < o.Length; i += 4) o[i] = 255; return o; };
        Side(img(opaque(wm)), img(opaque(A.map)), 0.5).Save(Path.Combine(dir, "tex-atlas-map.png"));
        Side(FigPng.Channel(img(wm), 3), FigPng.Channel(img(A.map), 3), 0.5).Save(Path.Combine(dir, "tex-atlas-mask.png"));
        Side(img(wn), img(A.nrm), 0.5).Save(Path.Combine(dir, "tex-atlas-nrm.png"));
        Side(img(wo), img(A.orm), 0.5).Save(Path.Combine(dir, "tex-atlas-orm.png"));
        var WM = img(opaque(wm)); var CM = img(opaque(A.map)); var WN = img(wn); var CN = img(A.nrm); var WO = img(wo); var CO = img(A.orm);
        foreach (var kv in regWorst.OrderByDescending(k => k.Value).Take(6))
        {
            var r = FigAtlasLayout.REG(kv.Key);
            Func<FigPng.Img, FigPng.Img> crop = s => { var c = new FigPng.Img(r.pw, r.ph); c.Blit(s, 0, 0, 1, r.px, r.py, r.pw, r.ph); return c; };
            var top = Side(crop(WM), crop(CM), 2); var bot = Side(crop(WN), crop(CN), 2); var orm = Side(crop(WO), crop(CO), 2);
            var o = new FigPng.Img(top.w, top.h * 3 + 8); o.Fill(40, 40, 46); o.Blit(top, 0, 0); o.Blit(bot, 0, top.h + 4); o.Blit(orm, 0, top.h * 2 + 8);
            o.Save(Path.Combine(dir, "tex-region-" + kv.Key + ".png"));
        }
        // ---- 面部贴图
        Console.WriteLine("== 名将面部贴图");
        var faceImgs = new List<FigPng.Img[]>();
        foreach (JObj f in M.L("faces"))
        {
            int size = (int)f.D("size");
            var g = f.O("gen");
            var gen = new PortraitGen { name = g.S("name"), war = g.Has("war") ? g.D("war") : (double?)null, intel = g.Has("intel") ? g.D("intel") : (double?)null, pol = g.Has("pol") ? g.D("pol") : (double?)null, culture = g.S("culture"), color = g.S("color") };
            var look = FigBuild.lookOfGeneral(gen, new FigOpts { lod = f.S("lod") });
            double tf = FigMath.Now();
            var r = FigAtlas.PaintFace(FigFaceDesc.FromSpec(look.spec ?? new JObj()), size);
            tf = FigMath.Now() - tf;
            var cs = r.ToRGBA8();
            var web = File.ReadAllBytes(Path.Combine(dir, f.S("file")));
            Stat(web, cs, size, 0, 0, size, size, 0, 3, out mean, out big);
            Check(f.S("name") + " " + size + string.Format("（{0:F0} ms）", tf), mean, big, 3, 0.03);
            faceImgs.Add(new[] { new FigPng.Img(size, size, web), new FigPng.Img(size, size, cs) });
        }
        // 4 列 × n 行，每格 网页版 | C#（各 256 像素）
        int cols = 4, cell = 256, rows = (faceImgs.Count + cols - 1) / cols;
        var grid = new FigPng.Img(cols * (cell * 2 + 12), rows * (cell + 8)); grid.Fill(40, 40, 46);
        for (int i = 0; i < faceImgs.Count; i++)
        {
            int cx = (i % cols) * (cell * 2 + 12), cy = (i / cols) * (cell + 8);
            double k = (double)cell / faceImgs[i][0].w;
            grid.Blit(faceImgs[i][0], cx, cy, k); grid.Blit(faceImgs[i][1], cx + cell + 2, cy, k);
        }
        grid.Save(Path.Combine(dir, "tex-faces.png"));
        // 原尺寸的两张（关羽、诸葛亮）
        Side(faceImgs[0][0], faceImgs[0][1], 1).Save(Path.Combine(dir, "tex-face-0.png"));
        Side(faceImgs[4][0], faceImgs[4][1], 1).Save(Path.Combine(dir, "tex-face-4.png"));
        // ---- 环境贴图
        Console.WriteLine("== 环境贴图");
        var env = FigAtlas.PaintEnv().ToRGBA8(); var wenv = File.ReadAllBytes(Path.Combine(dir, "web-env.rgba"));
        int EW = FigAtlas.ENV_W, EH = FigAtlas.ENV_H;
        Stat(wenv, env, EW, 0, 0, EW, EH, 0, 2, out mean, out big); Check("环境 256×128", mean, big, 2, 0.01);
        Side(new FigPng.Img(EW, EH, wenv), new FigPng.Img(EW, EH, env), 2).Save(Path.Combine(dir, "tex-env.png"));
        // ---- 环境贴图的预滤波：C# 的立方体贴图（FigAtlas.MakeEnvCubes，= Unity 的 _EnvCube / _EnvIrr，按 sgEnvLod 取级别）
        //      vs three PMREM 的 textureCubeUV（web-pmrem.json：256 个方向 × 13 个粗糙度，8 位）
        var pmPath = Path.Combine(dir, "web-pmrem.json");
        if (File.Exists(pmPath))
        {
            Console.WriteLine("== 环境贴图预滤波（PMREM 对照，0–255）");
            var P = (JObj)JObj.ParseJson(File.ReadAllText(pmPath));
            int PN = (int)P.D("n"); var rough = P.L("rough"); var dirs = P.L("dirs"); var px = P.L("px");
            double tc = FigMath.Now(); var E = FigAtlas.MakeEnvCubes(); double cms = FigMath.Now() - tc;
            Console.WriteLine(string.Format("  C# 立方体贴图生成 {0:F0} ms（网页版 PMREM {1}）", cms, P.S("size")));
            var o = new double[3];
            for (int r = 0; r < rough.Count; r++)
            {
                double rv = Convert.ToDouble(rough[r]), sum = 0, mx = 0, sumI = 0, mxI = 0; int bigN = 0;
                for (int i = 0; i < PN; i++)
                {
                    double x = Convert.ToDouble(dirs[i * 4]), y = Convert.ToDouble(dirs[i * 4 + 1]), z = Convert.ToDouble(dirs[i * 4 + 2]);
                    E.Radiance(x, y, -z, FigAtlas.EnvCubes.Lod(rv), o);         // three → Unity：z 取反
                    double d = 0;
                    for (int c = 0; c < 3; c++) d = Math.Max(d, Math.Abs(o[c] * 255 - Convert.ToDouble(px[(r * PN + i) * 4 + c])));
                    sum += d; mx = Math.Max(mx, d); if (d > 24) bigN++;
                    if (r == rough.Count - 1)
                    {
                        E.Irradiance(x, y, -z, o); double di = 0;
                        for (int c = 0; c < 3; c++) di = Math.Max(di, Math.Abs(o[c] * 255 - Convert.ToDouble(px[(r * PN + i) * 4 + c])));
                        sumI += di; mxI = Math.Max(mxI, di);
                    }
                }
                // 粗糙度很低时是原图的锐利细节（太阳、亮带），两边的取样位置不同，按大差占比判定；其余按平均差
                Check(string.Format("反射 粗糙度 {0:F2}（最大 {1:F0}）", rv, mx), sum / PN, (double)bigN / PN, 4, rv < 0.25 ? 0.08 : 0.02);
                if (r == rough.Count - 1) Check(string.Format("辐照度 _EnvIrr（最大 {0:F0}）", mxI), sumI / PN, 0, 3, 0);
            }
        }
        Console.WriteLine(fails == 0 ? "== 人物贴图：全部通过" : "== 人物贴图：" + fails + " 项超出");
        return fails;
    }
}
