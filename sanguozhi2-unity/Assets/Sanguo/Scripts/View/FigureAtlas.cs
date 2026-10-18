// ==========================================================================
// 三国志-霸王的世界 · 人物模型库 —— 程序贴图：图集、名将面部贴图、环境贴图（网页版 js/figure.js 的移植）
//
// 1024² 图集，128px 为一格（区域布局见 FigureMesh.cs 的 FigAtlasLayout）。每个区域在 5 个通道上绘制：
// 反照率（RGB）、染色遮罩、高度、粗糙度、金属度（与 JS 相同，各通道各自一张画布），后处理加噪声 / 拉丝 / 木纹，
// 再合成三张贴图：map（RGB + A = 染色遮罩）、normal（由高度求得）、ORM（R = AO、G = 粗糙度、B = 金属度）。
// Canvas2D → View/Raster.cs（同一子集：路径、贝塞尔、圆弧、线性 / 同心径向渐变、描边、虚线、裁剪、变换）。
// 每个区域画在一张区域大小的栅格上（JS 的每次绘制都裁剪到区域矩形，结果相同）。
// 生成过程是 IEnumerable（每步约 10–30ms，Figure.Preload 的协程分片执行）。纯 C#，可在后台线程或无头测试中运行；
// Unity 部分（Texture2D / Cubemap）在文件末尾（SANGUO_HEADLESS 时不编译）。
// ==========================================================================
using System;
using System.Collections.Generic;
using static Sanguo.FigMath;
#if !SANGUO_HEADLESS
using UnityEngine;
#endif

namespace Sanguo
{
    // 5 通道画家（= JS Painter）：vals 的每项可为 css 字符串 / FCol / 数值（灰度）/ 渐变（FigPainter.G）
    public sealed class FigPainter
    {
        public static readonly string[] CH = { "a", "m", "h", "r", "mt" };
        public readonly double x, y, w, h;
        readonly Raster[] ctx;          // 按通道（缺的通道为 null）
        public FigPainter(Raster[] ctx, double px, double py, double pw, double ph) { this.ctx = ctx; x = px; y = py; w = pw; h = ph; }
        // 分片绘制：每次 Fill / Stroke 在一个通道上算一个操作；本轮只栅格化编号 ≥ opLo 的操作，超过 deadline（毫秒时刻）后停下，
        // 停下处记在 stop（−1 = 已画完）。未栅格化的操作仍照常构建路径（路径函数里可能取随机数），所以多轮重放的结果与一次画完逐位相同。
        public int opLo, stop = -1, op;
        public double deadline = double.MaxValue;
        bool Live()
        {
            int i = op++;
            if (i < opLo || stop >= 0) return false;
            if (i > opLo && Now() > deadline) { stop = i; return false; }
            return true;
        }
        // 渐变：按通道生成 Paint（stops 的值：通道 a 为颜色，其余为灰度）
        public sealed class G { public Func<int, Paint> make; }
        public sealed class V
        {
            public object a, m, h, r, mt;
            public object this[int k] { get { return k == 0 ? a : k == 1 ? m : k == 2 ? h : k == 3 ? r : mt; } }
        }
        static string grey(double v) { int c = (int)jround(clamp01(v) * 255); return "rgb(" + c + "," + c + "," + c + ")"; }
        static string cssOf(object a) { return a is string ? (string)a : a is FCol ? hex((FCol)a) : "#000000"; }
        static Paint Value(object v, int k)
        {
            var g = v as G;
            if (g != null) return g.make(k);
            if (k == 0) return Paint.Solid(cssOf(v));
            double d = v is double ? (double)v : v is int ? (int)v : v is float ? (float)v : 0;
            return Paint.Solid(grey(d));
        }
        // dry：本轮不栅格化时代替 fn 执行（只构建路径，保持随机数序列）；null = 跳过
        public void Each(V vals, Action<Raster, Paint> fn, Action<Raster> dry = null)
        {
            for (int k = 0; k < 5; k++)
            {
                var v = vals[k];
                if (v == null) continue;
                var c = ctx[k];
                if (c == null) continue;
                bool live = Live();
                if (!live && dry == null) continue;
                c.Save();
                if (live) fn(c, Value(v, k)); else dry(c);
                c.Restore();
            }
        }
        // 渐变按填充时的变换解释（Canvas 语义）：toGrad = 当前变换的逆
        static Paint AtFill(Raster c, Paint p) { if (p.kind == 0) return p; var q = p.Clone(); q.toGrad = c.Transform.Inverse(); return q; }
        public void Fill(Action<Raster> path, V vals) { Each(vals, (c, st) => { c.BeginPath(); path(c); c.FillStyle = AtFill(c, st); c.Fill(); }, c => { c.BeginPath(); path(c); }); }
        public void Stroke(Action<Raster> path, V vals, double lw = 1)
        {
            Each(vals, (c, st) => { c.LineWidth = lw != 0 ? lw : 1; c.LineCap = LineCap.Round; c.LineJoin = LineJoin.Round; c.BeginPath(); path(c); c.StrokeStyle = AtFill(c, st); c.Stroke(); }, c => { c.BeginPath(); path(c); });
        }
        public void Rect(double rx, double ry, double rw, double rh, V vals) { Fill(c => c.Rect(x + rx, y + ry, rw, rh), vals); }
        public void All(V vals) { Rect(-2, -2, w + 4, h + 4, vals); }
        // 局部坐标的线性 / 径向（同心）渐变；stops：[offset, 值]
        public G Lin(double x0, double y0, double x1, double y1, params object[] stops)
        {
            double X0 = x + x0, Y0 = y + y0, X1 = x + x1, Y1 = y + y1;
            return new G { make = k => { var p = Paint.Linear(X0, Y0, X1, Y1); for (int i = 0; i < stops.Length; i += 2) p.AddStop(Convert.ToDouble(stops[i]), StopCol(stops[i + 1], k)); return p; } };
        }
        public G Rad(double cx, double cy, double r0, double r1, params object[] stops)
        {
            double X = x + cx, Y = y + cy;
            return new G { make = k => GradRad(X, Y, r0, r1, k, stops) };
        }
        // Canvas createRadialGradient(x, y, r0, x, y, r1)（同心）：t = (d − r0)/(r1 − r0) → Raster 的 t' = d / r1，色标偏移换算为 (s·(r1 − r0) + r0)/r1
        public static Paint GradRad(double X, double Y, double r0, double r1, int k, object[] stops)
        {
            var p = Paint.Radial(X, Y, r1);
            for (int i = 0; i < stops.Length; i += 2) p.AddStop((Convert.ToDouble(stops[i]) * (r1 - r0) + r0) / r1, StopCol(stops[i + 1], k));
            return p;
        }
        static RGBA StopCol(object v, int k) { return RGBA.Parse(k == 0 ? cssOf(v) : grey(v is double ? (double)v : Convert.ToDouble(v))); }
        public static V Vs(object a = null, object m = null, object h = null, object r = null, object mt = null) { return new V { a = a, m = m, h = h, r = r, mt = mt }; }
    }

    // 面部绘制描述（= JS faceDesc 的结果 / GENERIC_FACES）
    public sealed class FigFaceDesc
    {
        public string skin; public bool fem, generic; public double age = 30, blush = 1;
        public string expr;
        public string eyeShape, eyeColor; public double eyeSize = 1;
        public string browShape, browColor; public double browThick = 1, browY;
        public JObj mouth;
        public string beard, beardColor;
        public string hairStyle, hairColor; public double hairRecede;
        public List<object> marks = new List<object>(), acc = new List<object>();
        bool Has(List<object> l, string s) { foreach (var x in l) if (x as string == s) return true; return false; }
        public bool Mark(string s) { return Has(marks, s); }
        public bool Acc(string s) { return Has(acc, s); }
        static string S(JObj o, string k) { if (o == null) return null; var s = o.S(k); return string.IsNullOrEmpty(s) ? null : s; }
        // 外貌参数 → 面部绘制描述
        public static FigFaceDesc FromSpec(JObj sp)
        {
            var eyes = sp.O("eyes"); var brows = sp.O("brows"); var beard = sp.O("beard"); var hair = sp.O("hair");
            var d = new FigFaceDesc
            {
                skin = S(sp, "skin"), fem = sp.S("sex") == "f", age = sp.Or("age", 30.0), expr = S(sp, "expr"), blush = sp.Or("blush", 1.0),
                eyeShape = S(eyes, "shape"), eyeColor = S(eyes, "color"), eyeSize = eyes != null ? eyes.Or("size", 1.0) : 1,
                browShape = S(brows, "shape"), browThick = brows != null ? brows.Or("thick", 1.0) : 1, browColor = S(brows, "color"), browY = brows != null && brows.Or("y", 0.0) != 0 ? brows.D("y") * 0.4 : 0,
                mouth = sp.O("mouth"), beard = S(beard, "style"), beardColor = S(beard, "color"),
                hairStyle = S(hair, "style"), hairColor = S(hair, "color"), hairRecede = hair != null ? hair.Or("recede", 0.0) : 0,
                marks = sp.L("marks") ?? new List<object>(), acc = sp.L("acc") ?? new List<object>(),
            };
            return d;
        }
        // 通用脸（GENERIC_FACES）直接是绘制描述的格式：eye / brow / hair 为对象，beard 为样式名
        public static FigFaceDesc FromGeneric(JObj g)
        {
            var eye = g.O("eye"); var brow = g.O("brow"); var hair = g.O("hair");
            return new FigFaceDesc
            {
                generic = g.G("generic") is bool && (bool)g.G("generic"), skin = S(g, "skin"), fem = g.G("fem") is bool && (bool)g.G("fem"), age = g.Or("age", 30.0), expr = S(g, "expr"), blush = g.Or("blush", 1.0),
                eyeShape = S(eye, "shape"), eyeColor = S(eye, "color"), eyeSize = eye != null ? eye.Or("size", 1.0) : 1,
                browShape = S(brow, "shape"), browThick = brow != null ? brow.Or("thick", 1.0) : 1, browColor = S(brow, "color"), browY = brow != null ? brow.Or("y", 0.0) : 0,
                mouth = g.O("mouth"), beard = S(g, "beard"), beardColor = S(g, "beardColor"),
                hairStyle = S(hair, "style"), hairColor = S(hair, "color"), hairRecede = hair != null ? hair.Or("recede", 0.0) : 0,
                marks = g.L("marks") ?? new List<object>(), acc = g.L("acc") ?? new List<object>(),
            };
        }
    }

    public static class FigAtlasPaint
    {
        static FigPainter.V Vs(object a = null, object m = null, object h = null, object r = null, object mt = null) { return FigPainter.Vs(a, m, h, r, mt); }
        static readonly double SKIN_EXP = FigureData.SKIN_EXP;
        static string grey(double v) { int c = (int)jround(clamp01(v) * 255); return "rgb(" + c + "," + c + "," + c + ")"; }
        static string N(double v) { return Js.Num(v); }

        // ---------------------------------------------------------- 各区域绘制（= JS PAINT）
        public static void PaintRegion(string name, FigPainter P)
        {
            switch (name)
            {
                case "metal": metal(P); break;
                case "brushed": P.All(Vs("#d0d2d6", 1, 0.5, 0.34, 1)); break;
                case "lamellar": lamellarPaint(P, false); break;
                case "lamLace": lamellarPaint(P, true); break;
                case "scale": scale(P); break;
                case "mail": mail(P); break;
                case "cloth": P.All(Vs("#d6d6d6", 1, 0.5, 0.88, 0)); break;
                case "hem": hem(P); break;
                case "brocade": brocade(P); break;
                case "plaid": plaid(P); break;
                case "fur": fur(P); break;
                case "hair": hair(P); break;
                case "goldEngr": goldEngr(P); break;
                case "horse": P.All(Vs("#d2d2d2", 1, 0.5, 0.62, 0)); break;
                case "tiger": tiger(P); break;
                case "leopard": leopard(P); break;
                case "feather": feather(P); break;
                case "blade": blade(P); break;
                // 肤色反照率按 SKIN_EXP 压低；名将面部贴图用同一系数
                case "skin": P.All(Vs(grey(SKIN_EXP), 1, 0.5, 0.7, 0)); break;
                case "faceA": paintFaceInto(P, Generic(0)); break;
                case "faceB": paintFaceInto(P, Generic(1)); break;
                case "faceC": paintFaceInto(P, Generic(2)); break;
                case "faceD": paintFaceInto(P, Generic(3)); break;
                case "leather": leather(P); break;
                case "wood": P.All(Vs("#c4c4c4", 1, 0.5, 0.66, 0)); break;
                case "wrap": wrap(P); break;
                case "tassel": tassel(P); break;
                case "gold": gold(P); break;
                case "rattan": rattan(P); break;
                case "white": P.All(Vs("#ffffff", 1, 0.5, 0.6, 0)); break;
                case "rivet": rivet(P); break;
                case "buckle": buckle(P); break;
                case "mirror": mirror(P); break;
                case "lion": lion(P); break;
                case "scutum": scutum(P); break;
                case "shieldRound": shieldRound(P); break;
                default: throw new Exception("no paint for region " + name);
            }
        }
        static FigFaceDesc Generic(int i) { return FigFaceDesc.FromGeneric((JObj)FigTables.GENERIC_FACES[i]); }

        static void metal(FigPainter P)
        {
            P.All(Vs("#d2d4d8", 1, 0.5, 0.3, 1));
            // 边缘磨亮
            P.All(Vs(P.Lin(P.w, 0, P.w - 30, 0, 0, "rgba(255,255,255,0.35)", 1, "rgba(255,255,255,0)")));
            // 三边（右、上、下）凸起包边 + 錾刻线 + 卷云纹带
            double W = P.w, H = P.h;
            Action<double, double, FigPainter.V> edge = (o, lw, vals) => P.Stroke(c => { c.MoveTo(P.x - 4, P.y + o); c.LineTo(P.x + W - o, P.y + o); c.LineTo(P.x + W - o, P.y + H - o); c.LineTo(P.x - 4, P.y + H - o); }, vals, lw);
            edge(4, 6, Vs("#e8eaee", null, 0.68, 0.22));
            edge(9, 1.6, Vs("#8a8c92", null, 0.36, 0.5));
            edge(25, 1.6, Vs("#8a8c92", null, 0.36, 0.5));
            // 卷云纹：沿三边排列的小涡旋
            Action<double, double, double, double> curl = (cx, cy, s, rot) => P.Stroke(c =>
            {
                for (int i = 0; i <= 24; i++)
                {
                    double a = rot + i / 24.0 * TAU * 1.1, rr = s * (1 - i / 30.0);
                    double x = P.x + cx + Math.Cos(a) * rr, y = P.y + cy + Math.Sin(a) * rr;
                    if (i == 0) c.MoveTo(x, y); else c.LineTo(x, y);
                }
            }, Vs("#94969c", null, 0.38, 0.5), 1.3);
            for (double x = 6; x < W - 26; x += 16) { curl(x, 17, 5, (x / 16) % 2 != 0 ? 0 : PI); curl(x, H - 17, 5, (x / 16) % 2 != 0 ? PI : 0); }
            for (double y = 26; y < H - 26; y += 16) curl(W - 17, y, 5, (y / 16) % 2 != 0 ? PI / 2 : -PI / 2);
            // 划痕
            var rr2 = new FigRng(11);
            for (int i = 0; i < 60; i++)
            {
                double x = rr2.range(0, W), y = rr2.range(30, H - 30), l = rr2.range(6, 26), a = rr2.range(-0.6, 0.6) + (rr2.chance(0.5) ? 0 : PI / 2);
                var vals = rr2.chance(0.6) ? Vs("rgba(250,252,255,0.5)", null, null, 0.2) : Vs("rgba(60,62,70,0.35)", null, 0.46, 0.5);
                double lw = rr2.range(0.5, 1.1);
                P.Stroke(c => { c.MoveTo(P.x + x, P.y + y); c.LineTo(P.x + x + Math.Cos(a) * l, P.y + y + Math.Sin(a) * l); }, vals, lw);
            }
        }
        static void scale(FigPainter P)
        {
            P.All(Vs("#2a2a2e", 1, 0.3, 0.6, 1));
            double sw = 16, step = 10;
            for (int row = (int)Math.Ceiling(P.h / step) + 1; row >= -1; row--)
            {
                double y = row * step, off = (row % 2) != 0 ? sw / 2 : 0;
                for (double x = -sw; x < P.w + sw; x += sw)
                {
                    double cx = x + off;
                    Action<Raster> path = c => { c.MoveTo(P.x + cx - sw / 2, P.y + y - 6); c.LineTo(P.x + cx + sw / 2, P.y + y - 6); c.LineTo(P.x + cx + sw / 2, P.y + y + 2); c.Arc(P.x + cx, P.y + y + 2, sw / 2, 0, PI); c.ClosePath(); };
                    P.Fill(path, Vs(P.Rad(cx - 2, y + 2, 1, sw * 0.8, 0, "#eef0f2", 1, "#9a9ca2"), 1, P.Rad(cx, y + 3, 1, sw * 0.7, 0, 0.78, 1, 0.5), 0.3, 1));
                    P.Stroke(c => { c.MoveTo(P.x + cx, P.y + y - 4); c.LineTo(P.x + cx, P.y + y + sw / 2); }, Vs("#f2f4f6", null, 0.66), 1);
                }
            }
        }
        static void mail(FigPainter P)
        {
            P.All(Vs("#3a3c42", 1, 0.3, 0.5, 1));
            for (double y = 0; y < P.h + 8; y += 5) for (double x = 0; x < P.w + 8; x += 6)
            {
                double cx = x + ((y / 5) % 2 != 0 ? 3 : 0);
                P.Stroke(c => c.Arc(P.x + cx, P.y + y, 2.6, 0, TAU), Vs("#c8cad0", null, 0.7, 0.35), 1.3);
            }
        }
        static void hem(FigPainter P)
        {
            P.All(Vs("#d6d6d6", 1, 0.5, 0.88, 0));
            double H = P.h, b0 = H - 50, b1 = H - 8;
            P.Rect(-2, b0, P.w + 4, b1 - b0, Vs("#9a9a9a", null, 0.48));
            var g = Vs(GOLDTHREAD, 0, 0.66, 0.45, 0.55);
            P.Rect(-2, b0, P.w + 4, 3, g); P.Rect(-2, b1 - 3, P.w + 4, 3, g); P.Rect(-2, H - 5, P.w + 4, 5, g);
            // 卷云纹（如意云头）
            for (double x = 0; x < P.w + 32; x += 32)
            {
                double cy = (b0 + b1) / 2;
                P.Stroke(c =>
                {
                    c.MoveTo(P.x + x - 14, P.y + cy + 6);
                    c.BezierCurveTo(P.x + x - 14, P.y + cy - 10, P.x + x + 2, P.y + cy - 12, P.x + x + 2, P.y + cy - 2);
                    c.BezierCurveTo(P.x + x + 2, P.y + cy + 6, P.x + x - 6, P.y + cy + 4, P.x + x - 5, P.y + cy - 1);
                    c.MoveTo(P.x + x + 2, P.y + cy - 2);
                    c.BezierCurveTo(P.x + x + 4, P.y + cy + 12, P.x + x + 18, P.y + cy + 10, P.x + x + 18, P.y + cy);
                }, g, 2.2);
                P.Fill(c => c.Arc(P.x + x + 9, P.y + cy - 11, 2.4, 0, TAU), g);
            }
        }
        static void brocade(FigPainter P)
        {
            P.All(Vs("#c4c4c4", 1, 0.5, 0.82, 0));
            for (int row = 0; row < 3; row++) for (double x = -32; x < P.w + 64; x += 64)
            {
                double cx = x + (row % 2 != 0 ? 32 : 0), cy = 16 + row * 48;
                P.Stroke(c => c.Arc(P.x + cx, P.y + cy, 16, 0, TAU), Vs("#e8e8e8", null, 0.6), 2.5);
                for (int k = 0; k < 6; k++)
                {
                    double a = k / 6.0 * TAU;
                    P.Fill(c => c.Ellipse(P.x + cx + Math.Cos(a) * 9, P.y + cy + Math.Sin(a) * 9, 5, 2.6, a, 0, TAU), Vs("#e2e2e2", null, 0.58));
                }
                P.Fill(c => c.Arc(P.x + cx, P.y + cy, 3, 0, TAU), Vs(GOLDTHREAD, 0, 0.62, 0.5, 0.5));
            }
        }
        static void plaid(FigPainter P)
        {
            P.All(Vs("#9a9a9a", 1, 0.5, 0.92, 0));
            for (int i = 0; i < 4; i++)
            {
                double o = i * 64;
                P.Rect(o + 6, -2, 22, P.h + 4, Vs("rgba(30,30,30,0.45)"));
                P.Rect(-2, (o % 128) + 10, P.w + 4, 22, Vs("rgba(30,30,30,0.45)"));
                P.Rect(o + 40, -2, 8, P.h + 4, Vs("rgba(255,255,255,0.4)"));
                P.Rect(-2, (o % 128) + 46, P.w + 4, 6, Vs("rgba(255,255,255,0.4)"));
                P.Rect(o + 52, -2, 2, P.h + 4, Vs("#d8b040", 0));
                P.Rect(-2, (o % 128) + 56, P.w + 4, 2, Vs("#b83028", 0));
            }
        }
        static void fur(FigPainter P)
        {
            P.All(Vs("#a8a8a8", 1, 0.5, 1, 0));
            var rr = new FigRng(5);
            for (int i = 0; i < 1600; i++)
            {
                double x = rr.range(-4, P.w + 4), y = rr.range(-8, P.h + 4), l = rr.range(6, 15), a = PI / 2 + rr.range(-0.5, 0.5);
                double v = rr.range(0.45, 1);
                double lw = rr.range(1, 2.2);
                P.Stroke(c => { c.MoveTo(P.x + x, P.y + y); c.QuadraticCurveTo(P.x + x + Math.Cos(a) * l * 0.5 + 2, P.y + y + Math.Sin(a) * l * 0.5, P.x + x + Math.Cos(a) * l, P.y + y + Math.Sin(a) * l); },
                    Vs(grey(v * 0.9), null, v * 0.7), lw);
            }
        }
        static void hair(FigPainter P)
        {
            P.All(Vs("#8a8a8a", 1, 0.5, 0.5, 0));
            var rr = new FigRng(7);
            for (int i = 0; i < 700; i++)
            {
                double x = rr.range(-4, P.w + 4), w = rr.range(-6, 6), v = rr.range(0.55, 1.05);
                double lw = rr.range(0.6, 1.4);
                P.Stroke(c => { c.MoveTo(P.x + x, P.y - 2); c.BezierCurveTo(P.x + x + w, P.y + P.h * 0.33, P.x + x - w, P.y + P.h * 0.66, P.x + x + w * 0.5, P.y + P.h + 2); },
                    Vs(grey(v * 0.88), null, v * 0.6), lw);
            }
            P.All(Vs(P.Lin(0, P.h * 0.2, 0, P.h * 0.55, 0, "rgba(255,255,255,0)", 0.5, "rgba(255,255,255,0.28)", 1, "rgba(255,255,255,0)"), null, null, P.Lin(0, P.h * 0.2, 0, P.h * 0.55, 0, 0.5, 0.5, 0.36, 1, 0.5)));
        }
        static void goldEngr(FigPainter P)
        {
            P.All(Vs("#e6e6e6", 1, 0.5, 0.26, 1));
            P.Rect(-2, 0, P.w + 4, 9, Vs("#f4f4f4", null, 0.7)); P.Rect(-2, P.h - 9, P.w + 4, 9, Vs("#f4f4f4", null, 0.7));
            P.Rect(-2, 9, P.w + 4, 2, Vs("#8a8a8a", null, 0.35)); P.Rect(-2, P.h - 11, P.w + 4, 2, Vs("#8a8a8a", null, 0.35));
            double cy = P.h / 2;
            for (double x = -20; x < P.w + 40; x += 40)
            {
                P.Stroke(c =>
                {
                    c.MoveTo(P.x + x, P.y + cy);
                    c.BezierCurveTo(P.x + x + 8, P.y + cy - 38, P.x + x + 26, P.y + cy - 30, P.x + x + 22, P.y + cy - 10);
                    c.BezierCurveTo(P.x + x + 19, P.y + cy + 2, P.x + x + 8, P.y + cy - 4, P.x + x + 12, P.y + cy - 14);
                    c.MoveTo(P.x + x, P.y + cy);
                    c.BezierCurveTo(P.x + x - 8, P.y + cy + 38, P.x + x - 26, P.y + cy + 30, P.x + x - 22, P.y + cy + 10);
                    c.BezierCurveTo(P.x + x - 19, P.y + cy - 2, P.x + x - 8, P.y + cy + 4, P.x + x - 12, P.y + cy + 14);
                }, Vs("#8c8c8c", null, 0.32, 0.45), 3);
                P.Fill(c => c.Arc(P.x + x + 20, P.y + cy + 24, 4, 0, TAU), Vs("#f8f8f8", null, 0.72));
                P.Fill(c => c.Arc(P.x + x - 20 + 40, P.y + cy - 24, 4, 0, TAU), Vs("#f8f8f8", null, 0.72));
            }
        }
        static void tiger(FigPainter P)
        {
            P.All(Vs("#d88a2a", 0, 0.5, 0.95, 0));
            P.Rect(-2, P.h - 30, P.w + 4, 32, Vs(P.Lin(0, P.h - 30, 0, P.h - 14, 0, "rgba(240,228,200,0)", 1, "#f0e4c8")));
            var rr = new FigRng(13);
            for (double x = 0; x < P.w + 20; x += rr.range(14, 22))
            {
                double w = rr.range(3, 6), sk = rr.range(-10, 10), len = rr.range(50, 100);
                P.Fill(c => { c.MoveTo(P.x + x, P.y - 4); c.QuadraticCurveTo(P.x + x + sk + w, P.y + len * 0.5, P.x + x + sk * 0.3, P.y + len); c.QuadraticCurveTo(P.x + x + sk - w, P.y + len * 0.5, P.x + x - w, P.y - 4); }, Vs("#1a1410", null, 0.46));
            }
        }
        static void leopard(FigPainter P)
        {
            P.All(Vs("#d4a85a", 0, 0.5, 0.95, 0));
            var rr = new FigRng(17);
            for (int i = 0; i < 46; i++)
            {
                double x = rr.range(0, P.w), y = rr.range(0, P.h), s = rr.range(4, 7);
                double rot = rr.range(0, PI);
                P.Stroke(c => c.Ellipse(P.x + x, P.y + y, s, s * 0.8, rot, 0.3, TAU - 0.6), Vs("#2a1a10"), 2.2);
                P.Fill(c => c.Ellipse(P.x + x, P.y + y, s * 0.5, s * 0.4, 0, 0, TAU), Vs("#b07a38"));
            }
        }
        static void feather(FigPainter P)
        {
            P.All(Vs("#c8a060", 0, 0.5, 0.6, 0));
            for (double y = -10; y < P.h + 20; y += 22)
            {
                P.Fill(c => { c.MoveTo(P.x - 2, P.y + y + 14); c.LineTo(P.x + P.w / 2, P.y + y); c.LineTo(P.x + P.w + 2, P.y + y + 14); c.LineTo(P.x + P.w + 2, P.y + y + 22); c.LineTo(P.x + P.w / 2, P.y + y + 8); c.LineTo(P.x - 2, P.y + y + 22); c.ClosePath(); }, Vs("#3a2618", null, 0.46));
                P.Fill(c => { c.MoveTo(P.x - 2, P.y + y + 12); c.LineTo(P.x + P.w / 2, P.y + y - 2); c.LineTo(P.x + P.w + 2, P.y + y + 12); c.LineTo(P.x + P.w + 2, P.y + y + 14); c.LineTo(P.x + P.w / 2, P.y + y); c.LineTo(P.x - 2, P.y + y + 14); c.ClosePath(); }, Vs("#f0dcb0"));
            }
            P.All(Vs(P.Lin(0, 0, P.w, 0, 0, "rgba(40,24,10,0.5)", 0.25, "rgba(40,24,10,0)", 0.75, "rgba(40,24,10,0)", 1, "rgba(40,24,10,0.5)")));
            P.Rect(P.w / 2 - 2, -2, 4, P.h + 4, Vs("#f4ecd8", null, 0.64));
        }
        static void blade(FigPainter P)
        {
            double W = P.w, H = P.h;
            P.All(Vs("#ccd2d8", 0.15, 0.5, 0.16, 1));
            P.All(Vs(P.Lin(0, 0, W, 0, 0, "#f6f8fa", 0.1, "#e6eaee", 0.16, "#b8bec6", 0.5, "#d4dae0", 0.84, "#b8bec6", 0.9, "#e6eaee", 1, "#f6f8fa"), null, null,
                P.Lin(0, 0, W, 0, 0, 0.08, 0.12, 0.12, 0.3, 0.2, 0.5, 0.14, 0.7, 0.2, 0.88, 0.12, 1, 0.08)));
            // 刃纹（波浪）
            P.Stroke(c => { for (double y = 0; y <= H; y += 4) { double x = W * 0.2 + Math.Sin(y * 0.12) * 3 + Math.Sin(y * 0.31) * 1.5; if (y == 0) c.MoveTo(P.x + x, P.y + y); else c.LineTo(P.x + x, P.y + y); } }, Vs("rgba(255,255,255,0.5)"), 2);
            P.Stroke(c => { for (double y = 0; y <= H; y += 4) { double x = W * 0.8 - Math.Sin(y * 0.12) * 3 - Math.Sin(y * 0.31) * 1.5; if (y == 0) c.MoveTo(P.x + x, P.y + y); else c.LineTo(P.x + x, P.y + y); } }, Vs("rgba(255,255,255,0.5)"), 2);
            P.Rect(W / 2 - 1, -2, 2, H + 4, Vs("#eef2f6", null, 0.62));
            // 刃根金错纹（下端 = t 小）
            var g = Vs("#d8b050", 0, 0.58, 0.3, 1);
            double y0 = H - 46;
            P.Stroke(c => { c.MoveTo(P.x + W * 0.3, P.y + y0); c.BezierCurveTo(P.x + W * 0.8, P.y + y0 + 8, P.x + W * 0.2, P.y + y0 + 18, P.x + W * 0.7, P.y + y0 + 28); c.BezierCurveTo(P.x + W * 0.9, P.y + y0 + 34, P.x + W * 0.5, P.y + y0 + 40, P.x + W * 0.5, P.y + H); }, g, 2.4);
            P.Fill(c => c.Arc(P.x + W * 0.5, P.y + y0 - 6, 4, 0, TAU), g);
        }
        static void leather(FigPainter P)
        {
            P.All(Vs("#bcbcbc", 1, 0.5, 0.72, 0));
            var st = Vs("#6a6a6a", null, 0.4);
            P.Stroke(c => { c.SetLineDash(new double[] { 4, 3 }); c.MoveTo(P.x - 2, P.y + 6); c.LineTo(P.x + P.w - 6, P.y + 6); c.LineTo(P.x + P.w - 6, P.y + P.h - 6); c.LineTo(P.x - 2, P.y + P.h - 6); }, st, 1.4);
        }
        static void wrap(FigPainter P)
        {
            P.All(Vs("#5a5a5a", 1, 0.3, 0.85, 0));
            for (double x = -P.h; x < P.w + P.h; x += 14)
            {
                P.Fill(c => { c.MoveTo(P.x + x, P.y + P.h + 2); c.LineTo(P.x + x + 10, P.y + P.h + 2); c.LineTo(P.x + x + 10 + P.h + 4, P.y - 2); c.LineTo(P.x + x + P.h + 4, P.y - 2); c.ClosePath(); },
                    Vs(P.Lin(0, 0, 14, 0, 0, "#a0a0a0", 0.5, "#e8e8e8", 1, "#a0a0a0"), null, 0.7));
            }
        }
        static void tassel(FigPainter P)
        {
            P.All(Vs("#c8c8c8", 1, 0.5, 0.85, 0));
            var rr = new FigRng(19);
            for (int i = 0; i < 260; i++)
            {
                double x = rr.range(0, P.w), v = rr.range(0.55, 1);
                // 求值次序同 JS：线宽参数先于路径；路径里的 rr.range(-3, 3) 每个通道（a、h）各调用一次
                var vals = Vs(grey(v), null, v * 0.7);
                double lw = rr.range(0.7, 1.6);
                P.Stroke(c => { c.MoveTo(P.x + x, P.y - 2); c.LineTo(P.x + x + rr.range(-3, 3), P.y + P.h + 2); }, vals, lw);
            }
        }
        static void gold(FigPainter P)
        {
            P.All(Vs("#eaeaea", 1, 0.5, 0.26, 1));
            var rr = new FigRng(23);
            for (int i = 0; i < 14; i++)
            {
                double cx = rr.range(0, P.w), cy = rr.range(0, P.h), s = rr.range(8, 16);
                P.Stroke(c => { for (int k = 0; k <= 30; k++) { double a = k / 30.0 * TAU * 1.5, r0 = s * (1 - k / 34.0); double x = P.x + cx + Math.Cos(a) * r0, y = P.y + cy + Math.Sin(a) * r0; if (k == 0) c.MoveTo(x, y); else c.LineTo(x, y); } }, Vs("#9a9a9a", null, 0.35), 2);
                P.Fill(c => c.Arc(P.x + cx, P.y + cy, 2.5, 0, TAU), Vs("#ffffff", null, 0.75));
            }
        }
        static void rattan(FigPainter P)
        {
            P.All(Vs("#7a7a7a", 1, 0.3, 0.6, 0));
            double s = 12;
            for (double y = 0; y < P.h; y += s) for (double x = 0; x < P.w; x += s)
            {
                bool hor = ((x + y) / s) % 2 == 0;
                P.Fill(c => c.Rect(P.x + x + 1, P.y + y + 1, s - 2, s - 2), Vs(P.Lin(hor ? 0 : x, hor ? y : 0, hor ? 0 : x + s, hor ? y + s : 0, 0, "#b8b8b8", 0.5, "#f0f0f0", 1, "#b0b0b0"), null, 0.72));
            }
        }
        static void rivet(FigPainter P)
        {
            P.All(Vs("#cfd1d6", 1, 0.5, 0.34, 1));
            P.Rect(-2, 0, P.w + 4, 4, Vs("#eeeeee", null, 0.66)); P.Rect(-2, P.h - 4, P.w + 4, 4, Vs("#eeeeee", null, 0.66));
            foreach (double y in new[] { 12, P.h - 12 }) for (double x = 6; x < P.w; x += 20)
                P.Fill(c => c.Arc(P.x + x, P.y + y, 4.5, 0, TAU), Vs(P.Rad(x - 1.5, y - 1.5, 0.5, 5, 0, "#ffffff", 1, "#8a8c92"), null, P.Rad(x, y, 0, 5, 0, 0.9, 1, 0.52)));
        }
        static void buckle(FigPainter P)
        {
            double c0 = P.w / 2, R0 = P.w / 2 - 4;
            P.All(Vs("#d8d8d8", 1, 0.45, 0.3, 1));
            P.Fill(c => c.Arc(P.x + c0, P.y + c0, R0, 0, TAU), Vs(P.Rad(c0 - 14, c0 - 14, 4, R0 * 1.3, 0, "#ffffff", 1, "#a8a8a8"), null, P.Rad(c0, c0, R0 * 0.2, R0, 0, 0.62, 0.85, 0.66, 1, 0.5)));
            P.Stroke(c => c.Arc(P.x + c0, P.y + c0, R0 - 6, 0, TAU), Vs("#808080", null, 0.4), 2);
            // 鬃毛花瓣
            for (int k = 0; k < 18; k++) { double a = k / 18.0 * TAU; P.Fill(c => c.Ellipse(P.x + c0 + Math.Cos(a) * (R0 - 15), P.y + c0 + Math.Sin(a) * (R0 - 15), 7, 3.2, a, 0, TAU), Vs("#f0f0f0", null, 0.72)); }
            // 兽面：眉、眼、鼻、獠牙口环
            var Fv = Vs("#a0a0a0", null, 0.36); var Bv = Vs("#ffffff", null, 0.8);
            foreach (int sx in new[] { -1, 1 })
            {
                P.Fill(c => c.Ellipse(P.x + c0 + sx * 13, P.y + c0 - 8, 7, 5, sx * 0.3, 0, TAU), Bv);
                P.Fill(c => c.Arc(P.x + c0 + sx * 13, P.y + c0 - 8, 2.6, 0, TAU), Fv);
                P.Stroke(c => { c.MoveTo(P.x + c0 + sx * 4, P.y + c0 - 16); c.QuadraticCurveTo(P.x + c0 + sx * 16, P.y + c0 - 26, P.x + c0 + sx * 24, P.y + c0 - 14); }, Bv, 3);
            }
            P.Fill(c => { c.MoveTo(P.x + c0, P.y + c0 - 6); c.LineTo(P.x + c0 + 7, P.y + c0 + 8); c.LineTo(P.x + c0 - 7, P.y + c0 + 8); c.ClosePath(); }, Bv);
            P.Stroke(c => c.Arc(P.x + c0, P.y + c0 + 18, 9, 0.15 * PI, 0.85 * PI), Fv, 2.5);
            P.Stroke(c => c.Arc(P.x + c0, P.y + c0 + 22, 7, 0, TAU), Bv, 2.4);
        }
        static void mirror(FigPainter P)
        {
            double c0 = P.w / 2, R0 = P.w / 2 - 3;
            P.All(Vs("#d0d0d0", 1, 0.45, 0.3, 1));
            P.Fill(c => c.Arc(P.x + c0, P.y + c0, R0, 0, TAU), Vs("#e2e2e2", null, P.Rad(c0, c0, 0, R0, 0, 0.6, 0.92, 0.62, 1, 0.48)));
            P.Fill(c => c.Arc(P.x + c0, P.y + c0, R0 * 0.58, 0, TAU), Vs(P.Rad(c0 - 12, c0 - 14, 2, R0 * 0.7, 0, "#ffffff", 1, "#c4c8cc"), null, P.Rad(c0, c0, 0, R0 * 0.58, 0, 0.68, 1, 0.6), 0.07));
            foreach (double k in new[] { 0.62, 0.8 }) P.Stroke(c => c.Arc(P.x + c0, P.y + c0, R0 * k, 0, TAU), Vs("#8a8a8a", null, 0.42), 2);
            for (int i = 0; i < 24; i++) { double a = i / 24.0 * TAU; P.Fill(c => c.Arc(P.x + c0 + Math.Cos(a) * R0 * 0.9, P.y + c0 + Math.Sin(a) * R0 * 0.9, 2.4, 0, TAU), Vs("#f6f6f6", null, 0.75)); }
        }
        static void lion(FigPainter P)
        {
            double c0 = P.w / 2;
            P.All(Vs("#d6d6d6", 1, 0.45, 0.3, 1));
            for (int k = 0; k < 26; k++) { double a = k / 26.0 * TAU, r0 = 30 + (k % 2) * 8; P.Stroke(c => { c.MoveTo(P.x + c0 + Math.Cos(a) * 20, P.y + c0 + Math.Sin(a) * 20); c.QuadraticCurveTo(P.x + c0 + Math.Cos(a + 0.2) * r0, P.y + c0 + Math.Sin(a + 0.2) * r0, P.x + c0 + Math.Cos(a + 0.1) * (r0 + 18), P.y + c0 + Math.Sin(a + 0.1) * (r0 + 18)); }, Vs("#f2f2f2", null, 0.72), 6); }
            P.Fill(c => c.Ellipse(P.x + c0, P.y + c0 + 4, 26, 30, 0, 0, TAU), Vs(P.Rad(c0 - 8, c0 - 8, 2, 34, 0, "#ffffff", 1, "#a8a8a8"), null, P.Rad(c0, c0, 2, 30, 0, 0.78, 1, 0.55)));
            foreach (int sx in new[] { -1, 1 }) P.Fill(c => c.Ellipse(P.x + c0 + sx * 11, P.y + c0 - 6, 6, 3.5, sx * 0.35, 0, TAU), Vs("#6a6a6a", null, 0.35));
            P.Fill(c => c.Ellipse(P.x + c0, P.y + c0 + 10, 7, 5, 0, 0, TAU), Vs("#8a8a8a", null, 0.85));
            P.Stroke(c => { c.MoveTo(P.x + c0 - 12, P.y + c0 + 22); c.QuadraticCurveTo(P.x + c0, P.y + c0 + 30, P.x + c0 + 12, P.y + c0 + 22); }, Vs("#6a6a6a", null, 0.35), 2.5);
        }
        static void scutum(FigPainter P)
        {
            double W = P.w, H = P.h; var g = Vs("#e0b444", 0, 0.62, 0.4, 0.8);
            P.All(Vs("#e8e8e8", 1, 0.5, 0.7, 0));
            P.Stroke(c => c.Rect(P.x + 7, P.y + 7, W - 14, H - 14), g, 6);
            P.Stroke(c => c.Rect(P.x + 16, P.y + 16, W - 32, H - 32), g, 1.6);
            double cx = W / 2, cy = H / 2;
            // 雷纹与鹰翼
            foreach (int sy in new[] { -1, 1 }) foreach (int sx in new[] { -1, 1 })
            {
                P.Stroke(c => { c.MoveTo(P.x + cx + sx * 24, P.y + cy + sy * 6); c.BezierCurveTo(P.x + cx + sx * 60, P.y + cy + sy * 10, P.x + cx + sx * 80, P.y + cy + sy * 50, P.x + cx + sx * 96, P.y + cy + sy * 84); }, g, 4);
                for (int k = 0; k < 4; k++) P.Stroke(c => { c.MoveTo(P.x + cx + sx * (34 + k * 14), P.y + cy + sy * (8 + k * 9)); c.LineTo(P.x + cx + sx * (30 + k * 14), P.y + cy + sy * (30 + k * 14)); }, g, 3);
                P.Stroke(c => { c.MoveTo(P.x + cx + sx * 6, P.y + cy + sy * 26); c.LineTo(P.x + cx + sx * 16, P.y + cy + sy * 46); c.LineTo(P.x + cx + sx * 4, P.y + cy + sy * 52); c.LineTo(P.x + cx + sx * 14, P.y + cy + sy * 80); }, g, 3);
            }
            P.Rect(cx - 4, 18, 8, H - 36, g);
            P.Fill(c => c.Arc(P.x + cx, P.y + cy, 26, 0, TAU), Vs(P.Rad(cx - 8, cy - 8, 2, 30, 0, "#f4f6f8", 1, "#8a8e96"), 0, P.Rad(cx, cy, 0, 26, 0, 0.9, 1, 0.6), 0.28, 1));
        }
        static void shieldRound(FigPainter P)
        {
            double c0 = P.w / 2, R0 = c0 - 3; var g = Vs("#d8ac44", 0, 0.62, 0.4, 0.8);
            P.All(Vs("#e4e4e4", 1, 0.5, 0.75, 0));
            // 木板纹（染色）
            for (double x = 0; x < P.w; x += 22) P.Rect(x, -2, 1.5, P.h + 4, Vs("rgba(0,0,0,0.18)", null, 0.42));
            P.Stroke(c => c.Arc(P.x + c0, P.y + c0, R0 - 4, 0, TAU), Vs("#b8b8b8", 0, 0.7, 0.35, 1), 8);
            for (int i = 0; i < 20; i++) { double a = i / 20.0 * TAU; P.Fill(c => c.Arc(P.x + c0 + Math.Cos(a) * (R0 - 4), P.y + c0 + Math.Sin(a) * (R0 - 4), 2.6, 0, TAU), Vs("#f0f0f0", 0, 0.85, null, 1)); }
            P.Stroke(c => c.Arc(P.x + c0, P.y + c0, R0 * 0.74, 0, TAU), g, 3);
            for (int i = 0; i < 8; i++)
            {
                double a = i / 8.0 * TAU;
                P.Stroke(c => { c.MoveTo(P.x + c0 + Math.Cos(a) * 34, P.y + c0 + Math.Sin(a) * 34); c.QuadraticCurveTo(P.x + c0 + Math.Cos(a + 0.35) * 62, P.y + c0 + Math.Sin(a + 0.35) * 62, P.x + c0 + Math.Cos(a + 0.1) * 84, P.y + c0 + Math.Sin(a + 0.1) * 84); }, g, 3);
            }
            P.Fill(c => c.Arc(P.x + c0, P.y + c0, 30, 0, TAU), Vs(P.Rad(c0 - 9, c0 - 9, 2, 34, 0, "#f6f6f6", 1, "#8a8e96"), 0, P.Rad(c0, c0, 0, 30, 0, 0.95, 1, 0.6), 0.28, 1));
        }
        static void lamellarPaint(FigPainter P, bool lace)
        {
            string plateCol = lace ? "#a6aab2" : "#d8dadf"; double plateM = lace ? 0 : 1;
            var cord = lace ? Vs("#f0f0f0", 1, 0.62, 0.85, 0) : Vs("#7a2a22", 0, 0.62, 0.85, 0);
            P.All(Vs("#26262a", plateM, 0.28, 0.7, 0.5));
            double pw = 24, step = 27, ph = 34;
            for (int row = (int)Math.Ceiling(P.h / step); row >= -1; row--)
            {
                double y = row * step - 4, off = (row % 2) != 0 ? pw / 2 : 0;
                for (double x = -pw; x < P.w + pw; x += pw)
                {
                    double x0 = x + off + 0.8, w = pw - 1.6;
                    Action<Raster> path = c =>
                    {
                        double X = P.x + x0, Y = P.y + y, r = 5;
                        c.MoveTo(X, Y); c.LineTo(X + w, Y); c.LineTo(X + w, Y + ph - r); c.QuadraticCurveTo(X + w, Y + ph, X + w - r, Y + ph);
                        c.LineTo(X + r, Y + ph); c.QuadraticCurveTo(X, Y + ph, X, Y + ph - r); c.ClosePath();
                    };
                    P.Fill(path, Vs(P.Lin(x0, 0, x0 + w, 0, 0, shade(plateCol, -0.28), 0.35, shade(plateCol, 0.18), 1, shade(plateCol, -0.34)), plateM, P.Lin(x0, 0, x0 + w, 0, 0, 0.52, 0.4, 0.76, 1, 0.5), 0.32, 1));
                    P.Fill(c => c.Arc(P.x + x0 + w / 2, P.y + y + ph - 7, 2, 0, TAU), Vs("#f4f4f4", plateM, 0.85));
                    P.Stroke(c => { c.MoveTo(P.x + x0 + 3, P.y + y + 6); c.LineTo(P.x + x0 + w - 3, P.y + y + 6); }, Vs("rgba(40,40,46,0.5)", null, 0.45), 1);
                }
                // 甲片之间的编绳
                P.Stroke(c => { for (double x = -pw; x < P.w + pw; x += 6) { double yy = P.y + y + 4 + ((x / 6) % 2 != 0 ? 3 : -3); if (x == -pw) c.MoveTo(P.x + x, yy); else c.LineTo(P.x + x, yy); } }, cord, 3.2);
            }
        }

        // ---------------------------------------------------------- 面部绘制（= JS paintFaceInto）
        public static void paintFaceInto(FigPainter P, FigFaceDesc F)
        {
            double W = P.w, Ht = P.h;
            Func<double, double, double[]> E = (th, ph) => { var q = FigBuild.headUV(th, ph); return new[] { P.x + q[0] * W, P.y + (1 - q[1]) * Ht }; };
            Func<double, double> sxAt = th => { const double d = 0.01; return (E(th + d, 0)[0] - E(th - d, 0)[0]) / (2 * d); };
            double eyeT = FigureData.FEAT_eyeT, eyeP = FigureData.FEAT_eyeP, browP = FigureData.FEAT_browP, noseP = FigureData.FEAT_noseP, mouthP = FigureData.FEAT_mouthP, hairP = FigureData.FEAT_hairP;
            double sy = Math.Abs(E(0, eyeP + 0.02)[1] - E(0, eyeP - 0.02)[1]) / 0.04;
            string skin;
            if (F.generic) skin = grey(SKIN_EXP);
            else { var c = col(F.skin ?? "#e8c09a"); skin = hex(new FCol(c.r * SKIN_EXP, c.g * SKIN_EXP, c.b * SKIN_EXP)); }
            bool fem = F.fem; double age = F.age != 0 ? F.age : 30;
            var sk = col(skin);
            Func<double, double, string> dark = (k, a) => "rgba(" + (int)jround(sk.r * 255 * k) + "," + (int)jround(sk.g * 255 * k * 0.92) + "," + (int)jround(sk.b * 255 * k * 0.88) + "," + N(a) + ")";
            P.All(Vs(skin, 1, 0.5, 0.68, 0));
            // 暖色：双颊、鼻、耳；冷色：眼窝；下巴下方阴影（渐变随椭圆缩放：单位圆渐变 + 平移缩放）
            Action<double, double, double, double, string> blob = (th, ph, rw, rh, color) =>
            {
                var c = E(th, ph);
                string c0 = color, c1 = System.Text.RegularExpressions.Regex.Replace(color, @"[\d.]+\)$", "0)");
                var g = new FigPainter.G { make = k => FigPainter.GradRad(0, 0, 0, 1, k, new object[] { 0.0, c0, 1.0, c1 }) };
                P.Fill(ctx => { ctx.Translate(c[0], c[1]); ctx.Scale(Math.Max(1e-3, rw), Math.Max(1e-3, rh)); ctx.Arc(0, 0, 1, 0, TAU); }, Vs(g));
            };
            double bl = (F.blush != 0 ? F.blush : 1) * (fem ? 1.4 : 1);
            foreach (int s in new[] { 1, -1 })
            {
                blob(s * 0.75, -0.25, sxAt(0.75) * 0.32, sy * 0.2, "rgba(210,90,80," + (0.16 * bl).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ")");
                blob(s * eyeT, eyeP, sxAt(eyeT) * 0.26, sy * 0.12, dark(0.7, 0.35));
                blob(s * PI / 2, -0.06, sxAt(1.5) * 0.25, sy * 0.25, "rgba(200,90,80,0.16)");
            }
            blob(0, noseP + 0.02, sxAt(0.05) * 0.08, sy * 0.1, "rgba(210,100,90,0.14)");
            // 下巴下方阴影：女性与深肤色减弱
            double skinL = 0.3 * sk.r + 0.59 * sk.g + 0.11 * sk.b;
            blob(0, -1.2, sxAt(0.3) * 0.9, sy * 0.25, dark(0.75, fem ? 0.12 : skinL < 0.4 ? 0.22 : 0.4));
            // 胡茬 / 须根
            string bs = F.beard ?? "none";
            if (!fem && bs != "none")
            {
                double amt = bs == "stubble" ? 0.32 : 0.2;
                var bc = col(F.beardColor ?? "#2a2020");
                string bcs = "rgba(" + (int)jround(bc.r * 160 + 40) + "," + (int)jround(bc.g * 160 + 50) + "," + (int)jround(bc.b * 160 + 70) + "," + N(amt) + ")";
                var pts = new[] { new[] { -1.25, -0.25 }, new[] { -0.9, -0.42 }, new[] { -0.45, -0.38 }, new[] { -0.2, -0.33 }, new[] { 0, -0.32 }, new[] { 0.2, -0.33 }, new[] { 0.45, -0.38 }, new[] { 0.9, -0.42 }, new[] { 1.25, -0.25 }, new[] { 1.4, -0.7 }, new[] { 0.8, -1.3 }, new[] { 0, -1.45 }, new[] { -0.8, -1.3 }, new[] { -1.4, -0.7 } };
                P.Fill(ctx => { for (int i = 0; i < pts.Length; i++) { var c = E(pts[i][0], pts[i][1]); if (i != 0) ctx.LineTo(c[0], c[1]); else ctx.MoveTo(c[0], c[1]); } ctx.ClosePath(); }, Vs(bcs));
            }
            // 头发（发际线以上；髡发只留两侧与后脑）
            string hs = F.hairStyle ?? "topknot", hcol = F.hairColor != null ? hex(F.hairColor) : "#1c1816";
            if (hs != "bald")
            {
                double front = hs == "kunfa" ? 1.2 : hairP - F.hairRecede * -0.25;
                Func<double, double> line = th =>
                {
                    double a = Math.Abs(th);
                    if (a < 0.95) return front + (hs == "kunfa" ? 0 : 0.06 * Math.Cos(th * 5) * gauss(a, 0.5, 0.3));
                    if (a < 1.45) return lerp(front - 0.15, 0.08, sstep(0.95, 1.45, a));
                    if (a < 1.75) return lerp(0.08, 0.15, sstep(1.45, 1.75, a));
                    return lerp(0.1, -0.55, sstep(1.75, 2.6, a));
                };
                P.Fill(ctx =>
                {
                    const int Nn = 48;
                    for (int i = 0; i <= Nn; i++) { double th = -PI + TAU * i / Nn; var c = E(th, line(th)); if (i != 0) ctx.LineTo(c[0], c[1]); else ctx.MoveTo(c[0], c[1]); }
                    var c1 = E(PI, PI / 2); var c0 = E(-PI, PI / 2);
                    ctx.LineTo(c1[0], c1[1]); ctx.LineTo(c0[0], c0[1]); ctx.ClosePath();
                }, Vs(hcol, F.generic ? 0 : 1, null, 0.5));
                // 发丝
                var rr = new FigRng(hashStr(hcol));
                for (int i = 0; i < (W > 200 ? 260 : 60); i++)
                {
                    double th = rr.range(-PI, PI), ph0 = line(th), l = rr.range(0.08, 0.25);
                    var a = E(th, ph0 + l); var b = E(th + rr.range(-0.05, 0.05), ph0 + 0.01);
                    var vals = Vs(rr.chance(0.5) ? "rgba(255,255,255,0.12)" : "rgba(0,0,0,0.25)");
                    P.Stroke(ctx => { ctx.MoveTo(a[0], a[1]); ctx.LineTo(b[0], b[1]); }, vals, W > 200 ? 1 : 0.6);
                }
            }
            // 皱纹（年长）
            if (age > 44)
            {
                double k = clamp01((age - 44) / 25);
                foreach (double q in new[] { 0.5, 0.58 }) P.Stroke(ctx => { var a = E(-0.45, q); var b = E(0, q + 0.02); var c = E(0.45, q); ctx.MoveTo(a[0], a[1]); ctx.QuadraticCurveTo(b[0], b[1], c[0], c[1]); }, Vs(dark(0.6, 0.35 * k)), W > 200 ? 1.4 : 0.8);
                foreach (int s in new[] { 1, -1 })
                {
                    P.Stroke(ctx => { var a = E(s * 0.17, noseP - 0.02); var b = E(s * 0.27, -0.42); var c = E(s * 0.25, -0.58); ctx.MoveTo(a[0], a[1]); ctx.QuadraticCurveTo(b[0], b[1], c[0], c[1]); }, Vs(dark(0.6, 0.45 * k)), W > 200 ? 1.6 : 0.8);
                    for (int j = 0; j < 3; j++) P.Stroke(ctx => { var a = E(s * 0.6, eyeP + 0.02 - j * 0.04); var b = E(s * 0.7, eyeP + 0.03 - j * 0.06); ctx.MoveTo(a[0], a[1]); ctx.LineTo(b[0], b[1]); }, Vs(dark(0.6, 0.3 * k)), W > 200 ? 1 : 0.6);
                }
            }
            // 眼睛
            string shape = F.eyeShape ?? "normal"; double esz = F.eyeSize != 0 ? F.eyeSize : 1;
            bool patch = F.Acc("eyepatch");
            const string lineC = "#1c120e";
            var SH = new Dictionary<string, double[]> { { "normal", new[] { 1, 1, 0.05, 1 } }, { "sharp", new[] { 1.05, 0.8, 0.12, 1.15 } }, { "phoenix", new[] { 1.1, 0.78, 0.2, 1.2 } }, { "round", new[] { 0.95, 1.35, 0, 0.9 } },
                { "narrow", new[] { 1.05, 0.6, 0.06, 1.1 } }, { "gentle", new[] { 1, 1, -0.06, 0.85 } }, { "big", new[] { 1.08, 1.25, 0, 0.95 } }, { "sleepy", new[] { 1, 0.68, -0.04, 1 } } };
            double[] sh; if (!SH.TryGetValue(shape, out sh)) sh = SH["normal"];
            foreach (int s in new[] { 1, -1 })
            {
                double cx = s * eyeT, cy = eyeP;
                double hw = 0.19 * sh[0] * esz, hh = 0.05 * sh[1] * esz * (fem ? 1.12 : 1), tilt = sh[2];
                var inner = E(s * (eyeT - hw), cy - 0.01); var outer = E(s * (eyeT + hw), cy + tilt * 0.5 + 0.005);
                var top = E(cx + s * hw * 0.1, cy + hh); var bot = E(cx, cy - hh * 0.7);
                if (patch && s > 0)
                {
                    var ec = E(cx, cy);
                    P.Fill(ctx => ctx.Ellipse(ec[0], ec[1], sxAt(cx) * hw * 1.45, sy * hh * 2.6, 0.1, 0, TAU), Vs("#141214", 0, null, 0.4));
                    continue;
                }
                Action<Raster> almond = ctx => { ctx.MoveTo(inner[0], inner[1]); ctx.QuadraticCurveTo(top[0], top[1] - sy * hh * 0.9 * sh[3], outer[0], outer[1]); ctx.QuadraticCurveTo(bot[0], bot[1] + sy * hh * 0.6, inner[0], inner[1]); ctx.ClosePath(); };
                P.Fill(almond, Vs("#e6ddd0", 0, null, 0.25));
                var ic = E(cx + s * hw * 0.05, cy + 0.005); double ir = Math.Min(sxAt(cx) * hw * 0.5, sy * hh * 1.25);
                P.Each(Vs(hex(F.eyeColor ?? "#3a2618"), 0, null, 0.2), (ctx, st) => { ctx.Save(); ctx.BeginPath(); almond(ctx); ctx.Clip(); ctx.FillStyle = st; ctx.BeginPath(); ctx.Arc(ic[0], ic[1], ir, 0, TAU); ctx.Fill(); ctx.Restore(); });
                P.Each(Vs("#0c0808"), (ctx, st) => { ctx.Save(); ctx.BeginPath(); almond(ctx); ctx.Clip(); ctx.FillStyle = st; ctx.BeginPath(); ctx.Arc(ic[0], ic[1], ir * 0.45, 0, TAU); ctx.Fill(); ctx.FillStyle = Paint.Solid("rgba(255,255,255,0.9)"); ctx.BeginPath(); ctx.Arc(ic[0] - ir * 0.3, ic[1] - ir * 0.35, ir * 0.22, 0, TAU); ctx.Fill(); ctx.Restore(); });
                // 上眼睑粗线 + 下眼睑细线 + 双眼皮线
                double lw = Math.Max(1, W / 170);
                P.Stroke(ctx => { ctx.MoveTo(inner[0], inner[1]); ctx.QuadraticCurveTo(top[0], top[1] - sy * hh * 0.9 * sh[3], outer[0] + s * lw, outer[1] - lw * 0.4); }, Vs(lineC, 0), lw * 1.8);
                P.Stroke(ctx => { ctx.MoveTo(inner[0], inner[1]); ctx.QuadraticCurveTo(bot[0], bot[1] + sy * hh * 0.6, outer[0], outer[1]); }, Vs("rgba(60,30,24,0.6)", 0), lw * 0.7);
                P.Stroke(ctx => { var a = E(s * (eyeT - hw * 0.6), cy + hh * 1.5); var b = E(cx, cy + hh * 2.2); var c = E(s * (eyeT + hw * 0.85), cy + hh * 1.2 + tilt * 0.4); ctx.MoveTo(a[0], a[1]); ctx.QuadraticCurveTo(b[0], b[1], c[0], c[1]); }, Vs(dark(0.55, 0.6)), lw * 0.7);
                if (fem) P.Stroke(ctx => { ctx.MoveTo(top[0], top[1] - sy * hh * 0.5); ctx.LineTo(outer[0] + s * lw * 3, outer[1] - lw * 2.5); }, Vs(lineC, 0), lw);
            }
            if (patch)
            {
                // 眼罩系带：从左眼斜过额头绕到脑后
                P.Stroke(ctx => { var a = E(-PI, 0.55); var b = E(-0.9, 0.62); var c = E(0.1, 0.5); var d = E(eyeT, eyeP); var e = E(1.2, -0.05); var f = E(PI, 0.0); ctx.MoveTo(a[0], a[1]); ctx.QuadraticCurveTo(b[0], b[1], c[0], c[1]); ctx.LineTo(d[0], d[1]); ctx.QuadraticCurveTo(e[0], e[1], f[0], f[1]); }, Vs("#141214", 0), Math.Max(1.5, W / 90));
            }
            // 眉
            string bshape = F.browShape ?? "straight"; double bth = F.browThick != 0 ? F.browThick : 1; string bcol = hex(F.browColor ?? hcol);
            string expr = F.expr ?? "neutral";
            var BR = new Dictionary<string, double[]> { { "straight", new[] { 0, 0, 0, 1.0 } }, { "arched", new[] { 0.03, 0.05, 0, 0.9 } }, { "angled", new[] { -0.02, 0.0, 0.1, 1.05 } }, { "bushy", new[] { 0, 0.02, 0.03, 1.5 } }, { "thin", new[] { 0.01, 0.03, 0.01, 0.55 } }, { "knit", new[] { -0.04, -0.02, 0.09, 1.15 } },
                { "silkworm", new[] { 0.02, 0.06, 0.05, 1.5 } }, { "sword", new[] { -0.015, -0.01, 0.13, 1.15 } }, { "worried", new[] { 0.06, 0.0, -0.05, 0.95 } } };
            double[] br; if (!BR.TryGetValue(bshape, out br)) br = BR["straight"];
            double fierce = expr == "fierce" ? 0.035 : expr == "stern" ? 0.015 : 0;
            foreach (int s in new[] { 1, -1 })
            {
                double by = browP + F.browY;
                var i0 = E(s * 0.12, by - br[0] * 0 + 0.0 - fierce + (br[0] < 0 ? br[0] : 0)); var m0 = E(s * 0.38, by + br[1] + 0.03); var o0 = E(s * 0.66, by + br[2] - 0.02);
                double t = sy * 0.035 * bth * br[3] * (fem ? 0.7 : 1);
                P.Fill(ctx =>
                {
                    ctx.MoveTo(i0[0], i0[1] - t * 0.9); ctx.QuadraticCurveTo(m0[0], m0[1] - t * 1.4, o0[0], o0[1] - t * 0.25);
                    ctx.QuadraticCurveTo(m0[0], m0[1] + t * 0.15, i0[0], i0[1] + t * 0.7); ctx.ClosePath();
                }, Vs(bcol, 0, null, 0.6));
                if (br[3] > 1.1 && W > 200)
                {
                    var rr = new FigRng(unchecked((uint)((long)hashStr(bshape) + s)));
                    for (int i = 0; i < 26; i++)
                    {
                        double k = rr.f(); var a = new[] { lerp(i0[0], o0[0], k), lerp(i0[1], o0[1], k) - t * 0.5 - Math.Sin(k * PI) * t * 0.6 };
                        P.Stroke(ctx => { ctx.MoveTo(a[0], a[1] + t * 0.6); ctx.LineTo(a[0] + s * rr.range(2, 6), a[1] - t * rr.range(0.2, 0.9)); }, Vs(bcol, 0), 1.2);
                    }
                }
            }
            // 鼻孔（小而淡）与鼻翼两侧阴影
            foreach (int s in new[] { 1, -1 })
            {
                var c = E(s * 0.11, noseP - 0.065); P.Fill(ctx => ctx.Ellipse(c[0], c[1], sxAt(0.11) * 0.034, sy * 0.014, s * 0.45, 0, TAU), Vs(dark(0.45, 0.5)));
                blob(s * 0.2, noseP + 0.02, sxAt(0.2) * 0.06, sy * 0.1, dark(0.7, 0.22));
            }
            P.Stroke(ctx => { var a = E(-0.1, 0.18); var b = E(-0.11, noseP + 0.02); ctx.MoveTo(a[0], a[1]); ctx.LineTo(b[0], b[1]); }, Vs(dark(0.75, 0.35)), Math.Max(1, W / 120));
            // 嘴
            var mo = F.mouth ?? new JObj(); double mw = 0.3 * mo.Or("w", 1.0) * (fem ? 0.85 : 1);
            double smile = expr == "smile" ? 0.03 : expr == "sly" ? 0.015 : expr == "fierce" || expr == "stern" ? -0.02 : 0;
            double my = mouthP + mo.Or("y", 0.0);
            FCol lc = fem ? col("#a8323c") : mix(skin, "#8a3a32", 0.55);
            double lips = mo.Or("lips", 1.0);
            P.Fill(ctx => { var a = E(-mw, my + smile * 0.6); var b = E(0, my - 0.075 * lips); var c = E(mw, my + smile * (expr == "sly" ? 1.6 : 0.6)); ctx.MoveTo(a[0], a[1]); ctx.QuadraticCurveTo(b[0], b[1], c[0], c[1]); ctx.QuadraticCurveTo(E(0, my)[0], E(0, my)[1], a[0], a[1]); }, Vs(fem ? (object)lc : hex(mix(lc, col(skin), 0.35)), 0, null, 0.4));
            P.Fill(ctx => { var a = E(-mw, my + smile * 0.6); var b = E(0, my + 0.055); var c = E(mw, my + smile * 0.6); ctx.MoveTo(a[0], a[1]); ctx.QuadraticCurveTo(b[0], b[1], c[0], c[1]); ctx.QuadraticCurveTo(E(0, my + 0.01)[0], E(0, my + 0.01)[1], a[0], a[1]); }, Vs(hex(mix(lc, col(skin), fem ? 0.1 : 0.45)), 0, null, 0.4));
            P.Stroke(ctx => { var a = E(-mw * 1.05, my + smile); var b = E(0, my - 0.005); var c = E(mw * 1.05, my + smile * (expr == "sly" ? 2 : 1)); ctx.MoveTo(a[0], a[1]); ctx.QuadraticCurveTo(b[0], b[1], c[0], c[1]); }, Vs("#3a1a16", 0), Math.Max(1, W / 140));
            // 面部标记
            Action<string, double, double[][]> mk = (color, lw, pts) => P.Stroke(ctx => { for (int i = 0; i < pts.Length; i++) { var c = E(pts[i][0], pts[i][1]); if (i != 0) ctx.LineTo(c[0], c[1]); else ctx.MoveTo(c[0], c[1]); } }, Vs(color, 0), lw);
            double L = Math.Max(1, W / 110);
            Func<double, double, double[]> p2 = (a, b) => new[] { a, b };
            if (F.Mark("scar")) mk("rgba(150,60,55,0.85)", L, new[] { p2(-0.85, 0.2), p2(-0.55, -0.15), p2(-0.4, -0.38) });
            if (F.Mark("scars")) { mk("rgba(150,60,55,0.8)", L, new[] { p2(0.5, 0.35), p2(0.8, -0.2) }); mk("rgba(150,60,55,0.8)", L, new[] { p2(-0.3, -0.6), p2(-0.65, -0.85) }); }
            if (F.Mark("mole")) { var c = E(0.32, -0.36); P.Fill(ctx => ctx.Arc(c[0], c[1], L * 1.2, 0, TAU), Vs("#3a2218", 0)); }
            if (F.Mark("woad")) foreach (int s in new[] { 1, -1 }) { mk("rgba(40,70,160,0.85)", L * 1.6, new[] { p2(s * 0.25, 0.42), p2(s * 0.6, 0.2), p2(s * 0.85, -0.25) }); mk("rgba(40,70,160,0.8)", L * 1.2, new[] { p2(s * 0.45, -0.3), p2(s * 0.7, -0.55), p2(s * 0.5, -0.8) }); }
            if (F.Mark("warpaint")) foreach (int s in new[] { 1, -1 }) { mk("rgba(190,40,30,0.85)", L * 2.5, new[] { p2(s * 0.2, -0.05), p2(s * 0.85, -0.12) }); mk("rgba(240,235,220,0.85)", L * 1.4, new[] { p2(s * 0.25, -0.18), p2(s * 0.8, -0.26) }); }
            if (F.Mark("tattoo-wa")) foreach (int s in new[] { 1, -1 }) { for (int j = 0; j < 3; j++) mk("rgba(40,40,60,0.75)", L, new[] { p2(s * (0.55 + j * 0.08), 0.05), p2(s * (0.62 + j * 0.08), -0.35) }); }
            if (F.Mark("tattoo-yi")) { for (int j = 0; j < 4; j++) mk("rgba(30,40,70,0.75)", L, new[] { p2(-0.4 + j * 0.25, 0.55), p2(-0.4 + j * 0.25, 0.4) }); mk("rgba(30,40,70,0.75)", L, new[] { p2(-0.3, -0.75), p2(0, -0.85), p2(0.3, -0.75) }); }
            if (F.Mark("freckles")) { var rr = new FigRng(5); for (int i = 0; i < 30; i++) { var c = E(rr.range(-0.7, 0.7), rr.range(-0.35, 0.0)); P.Fill(ctx => ctx.Arc(c[0], c[1], L * 0.6, 0, TAU), Vs("rgba(150,80,40,0.45)")); } }
            if (F.Mark("scales")) { for (int i = 0; i < 12; i++) { var c = E(-0.9 + (i % 4) * 0.12, 0.3 - Math.Floor(i / 4.0) * 0.12); P.Stroke(ctx => ctx.Arc(c[0], c[1], L * 2, 0, PI), Vs("rgba(60,90,70,0.6)", 0), L * 0.8); } }
        }
    }

    // ------------------------------------------------------------------ 图集生成（= JS genAtlas，分步）
    public sealed class FigAtlasData
    {
        public byte[] map, nrm, orm;            // 1024² RGBA，第 0 行 = v 0（画布底边），与 Unity Texture2D 的行序相同
        public byte[][] channels;               // 后处理后的 5 个通道（画布行序；只在 FigAtlas.KeepChannels 时保留，调试用）
        public double ms;
    }
    public static class FigAtlas
    {
        public const int N = FigureData.ATLAS_N;
        static FigAtlasData data;
        public static FigAtlasData Data { get { if (data == null) foreach (var _ in Generate()) { } return data; } }
        public static bool Ready { get { return data != null; } }
        // 调试：保留后处理后的 5 个通道（每个 4 MB；缺省丢弃）
        public static bool KeepChannels;
        // 分步生成时每步的目标时长（毫秒）
        public static double STEP_MS = 10;
        static byte U8C(double v) { if (double.IsNaN(v)) return 0; if (v <= 0) return 0; if (v >= 255) return 255; return (byte)Math.Round(v, MidpointRounding.ToEven); }   // Uint8ClampedArray
        static void addV(byte[] arr, int I, double d) { arr[I] = U8C(arr[I] + d); arr[I + 1] = U8C(arr[I + 1] + d); arr[I + 2] = U8C(arr[I + 2] + d); }
        delegate void PostFn(int x, int y, int I);
        static void postRegion(string name, PostFn fn)
        {
            var r = FigAtlasLayout.REG(name);
            for (int y = 0; y < r.ph; y++) for (int x = 0; x < r.pw; x++) fn(x, y, ((r.py + y) * N + r.px + x) * 4);
        }
        // 分步生成；每次 MoveNext 约 10–30ms（主线程分片或后台线程整段跑完）
        public static IEnumerable<object> Generate()
        {
            if (data != null) yield break;
            double t0 = Now();
            var D = new byte[5][];
            for (int k = 0; k < 5; k++)
            {
                D[k] = new byte[N * N * 4];
                // 画布底色：a #808080，m 1，h 0.5，r 0.7，mt 0
                byte v = k == 0 ? (byte)0x80 : (byte)jround(clamp01(k == 1 ? 1 : k == 2 ? 0.5 : k == 3 ? 0.7 : 0) * 255);
                for (int i = 0; i < N * N; i++) { D[k][i * 4] = v; D[k][i * 4 + 1] = v; D[k][i * 4 + 2] = v; D[k][i * 4 + 3] = 255; }
                yield return null;
            }
            foreach (var reg in FigAtlasLayout.Order)
            {
                var R = new Raster[5];
                for (int k = 0; k < 5; k++)
                {
                    R[k] = new Raster(reg.pw, reg.ph);
                    // 区域底色（= 画布在该区域的初始值）
                    var bg = D[k]; int I0 = (reg.py * N + reg.px) * 4;
                    float c = bg[I0] / 255f;
                    for (int i = 0; i < reg.pw * reg.ph; i++) { R[k].Px[i * 4] = c; R[k].Px[i * 4 + 1] = c; R[k].Px[i * 4 + 2] = c; R[k].Px[i * 4 + 3] = 1; }
                    R[k].SetTransform(1, 0, 0, 1, -reg.px, -reg.py);
                }
                // 一个区域分几轮画完（每轮约 STEP_MS 毫秒，见 FigPainter.opLo）
                for (int lo = 0; ;)
                {
                    var P = new FigPainter(R, reg.px, reg.py, reg.pw, reg.ph) { opLo = lo, deadline = Now() + STEP_MS };
                    FigAtlasPaint.PaintRegion(reg.name, P);
                    if (P.stop < 0) break;
                    lo = P.stop;
                    yield return null;
                }
                for (int k = 0; k < 5; k++)
                {
                    var b = R[k].ToRGBA8();
                    for (int y = 0; y < reg.ph; y++) Buffer.BlockCopy(b, y * reg.pw * 4, D[k], ((reg.py + y) * N + reg.px) * 4, reg.pw * 4);
                }
                yield return null;
            }
            // 后处理：噪声、拉丝、木纹等
            var A = D[0]; var Hh = D[2]; var Ro = D[3];
            postRegion("metal", (x, y, I) => { double n = fbm(x * 0.6, y * 0.03, 2, 3) - 0.5; addV(A, I, n * 18); addV(Ro, I, -n * 26 + (h2(x, y, 9) - 0.5) * 8); }); yield return "post";
            postRegion("brushed", (x, y, I) => { double n = fbm(x * 0.7, y * 0.035, 2, 4) - 0.5; addV(A, I, n * 22); addV(Ro, I, -n * 30); }); yield return "post";
            postRegion("rivet", (x, y, I) => { double n = fbm(x * 0.6, y * 0.04, 2, 5) - 0.5; addV(A, I, n * 16); }); yield return "post";
            foreach (var nm in new[] { "cloth", "hem", "brocade", "plaid" })
            {
                postRegion(nm, (x, y, I) =>
                {
                    int w = ((x + y) & 1) != 0 ? 1 : -1, th = (x % 3 == 0 ? -1 : 0) + (y % 3 == 0 ? -1 : 0);
                    double n = fbm(x * 0.05, y * 0.05, 3, 6) - 0.5;
                    addV(A, I, w * 3 + th * 4 + n * 22); addV(Hh, I, w * 6 + th * 6);
                });
                yield return "post";
            }
            postRegion("skin", (x, y, I) => { double n = fbm(x * 0.08, y * 0.08, 3, 7) - 0.5, p = h2(x, y, 8) - 0.5; A[I] = U8C(clamp(A[I] + n * 10 + 2, 0, 255)); A[I + 1] = U8C(clamp(A[I + 1] + n * 8, 0, 255)); A[I + 2] = U8C(clamp(A[I + 2] + n * 6 - 2, 0, 255)); addV(Hh, I, p * 1.5); }); yield return "post";
            postRegion("leather", (x, y, I) => { double n = fbm(x * 0.25, y * 0.25, 3, 10) - 0.5; addV(A, I, n * 36); addV(Hh, I, n * 40); addV(Ro, I, n * 30); }); yield return "post";
            postRegion("wood", (x, y, I) => { double g = Math.Sin((x + fbm(x * 0.03, y * 0.01, 3, 11) * 40) * 0.55); addV(A, I, g * 16 + (fbm(x * 0.2, y * 0.02, 2, 12) - 0.5) * 30); addV(Hh, I, g * 12); }); yield return "post";
            postRegion("horse", (x, y, I) => { double n = fbm(x * 0.02, y * 0.3, 2, 13) - 0.5, d = fbm(x * 0.04, y * 0.06, 3, 14) - 0.5; addV(A, I, n * 14 + d * 18); addV(Hh, I, n * 16); }); yield return "post";
            postRegion("gold", (x, y, I) => { addV(A, I, (fbm(x * 0.1, y * 0.1, 2, 15) - 0.5) * 16); }); yield return "post";
            postRegion("goldEngr", (x, y, I) => { addV(A, I, (fbm(x * 0.4, y * 0.05, 2, 16) - 0.5) * 12); }); yield return "post";
            postRegion("blade", (x, y, I) => { double n = fbm(x * 0.5, y * 0.02, 2, 18) - 0.5; addV(A, I, n * 10); }); yield return "post";
            postRegion("tiger", (x, y, I) => { double n = fbm(x * 0.15, y * 0.4, 2, 19) - 0.5; addV(A, I, n * 30); addV(Hh, I, n * 30); }); yield return "post";
            postRegion("leopard", (x, y, I) => { double n = fbm(x * 0.15, y * 0.4, 2, 20) - 0.5; addV(A, I, n * 26); addV(Hh, I, n * 26); }); yield return "post";
            postRegion("scutum", (x, y, I) => { double n = fbm(x * 0.04, y * 0.04, 3, 21) - 0.5; addV(A, I, n * 16); }); yield return "post";
            postRegion("shieldRound", (x, y, I) => { double n = fbm(x * 0.04, y * 0.04, 3, 22) - 0.5; addV(A, I, n * 18); }); yield return "post";
            // 高度 → 法线、AO
            int NN = N * N;
            var hgt = new float[NN];
            for (int i = 0; i < NN; i++) hgt[i] = (float)(D[2][i * 4] / 255.0);
            yield return "hgt";
            var hs = new float[NN]; var hb = new float[NN];
            foreach (var _ in Blur(hgt, 1, hs)) yield return null;
            foreach (var _ in Blur(hgt, 5, hb)) yield return null;
            var map = new byte[NN * 4]; var nrm = new byte[NN * 4]; var orm = new byte[NN * 4];
            const double S = 10;
            for (int y = 0; y < N; y++)
            {
                int ty = N - 1 - y;                    // 第 0 行 = v 0（画布底边）
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x, I = i * 4, O = (ty * N + x) * 4;
                    map[O] = D[0][I]; map[O + 1] = D[0][I + 1]; map[O + 2] = D[0][I + 2]; map[O + 3] = D[1][I];
                    int xl = x > 0 ? i - 1 : i, xr = x < N - 1 ? i + 1 : i, yu = y > 0 ? i - N : i, yd = y < N - 1 ? i + N : i;
                    double dx = ((double)hs[xr] - hs[xl]) * S, dy = ((double)hs[yd] - hs[yu]) * S;
                    double inv = 1 / Math.Sqrt(dx * dx + dy * dy + 1);
                    nrm[O] = (byte)(int)((-dx * inv * 0.5 + 0.5) * 255); nrm[O + 1] = (byte)(int)((dy * inv * 0.5 + 0.5) * 255); nrm[O + 2] = (byte)(int)((inv * 0.5 + 0.5) * 255); nrm[O + 3] = 255;
                    double cav = Math.Max(0, (double)hb[i] - hs[i]);
                    orm[O] = (byte)(int)(clamp(1 - cav * 4, 0.35, 1) * 255); orm[O + 1] = D[3][I]; orm[O + 2] = D[4][I]; orm[O + 3] = 255;
                }
                if ((y & 63) == 63) yield return "nrm";
            }
            FigStats.textures += 3;
            FigStats.atlasMs = jround(Now() - t0);
            data = new FigAtlasData { map = map, nrm = nrm, orm = orm, channels = KeepChannels ? D : null, ms = Now() - t0 };
        }
        // 可分离盒式模糊（= JS blur：Float32 存储，累加与系数为双精度）
        static IEnumerable<object> Blur(float[] src, int rad, float[] outp)
        {
            int NN = N * N; var tmp = new float[NN]; double k = 1.0 / (2 * rad + 1);
            for (int y = 0; y < N; y++)
            {
                double s = 0; int o = y * N;
                for (int x = -rad; x <= rad; x++) s += src[o + Math.Max(0, Math.Min(N - 1, x))];
                for (int x = 0; x < N; x++) { tmp[o + x] = (float)(s * k); s += (double)src[o + Math.Min(N - 1, x + rad + 1)] - src[o + Math.Max(0, x - rad)]; }
                if ((y & 255) == 255) yield return null;
            }
            for (int x = 0; x < N; x++)
            {
                double s = 0;
                for (int y = -rad; y <= rad; y++) s += tmp[Math.Max(0, Math.Min(N - 1, y)) * N + x];
                for (int y = 0; y < N; y++) { outp[y * N + x] = (float)(s * k); s += (double)tmp[Math.Min(N - 1, y + rad + 1) * N + x] - tmp[Math.Max(0, y - rad) * N + x]; }
                if ((x & 255) == 255) yield return null;
            }
        }
        // 丢弃 CPU 端的图集数据（FigTextures.EnsureAtlas 上传后调用；再取 Data 会重新生成）
        public static void Reset() { data = null; }

        // ---- 名将面部贴图（只有反照率一个通道；画布底色透明，P.all 先铺满肤色）
        public static Raster PaintFace(FigFaceDesc F, int size)
        {
            var r = new Raster(size, size);
            FigAtlasPaint.paintFaceInto(new FigPainter(new[] { r, null, null, null, null }, 0, 0, size, size), F);
            return r;
        }

        // ---- 环境贴图（256 × 128 的等距柱状投影；JS envMap）
        public const int ENV_W = 256, ENV_H = 128;
        public static Raster PaintEnv()
        {
            int W = ENV_W, H = ENV_H;
            var g = new Raster(W, H);
            var sky = Paint.Linear(0, 0, 0, H / 2.0).AddStop(0, RGBA.Parse("#4a6aa0")).AddStop(0.65, RGBA.Parse("#a8b8cc")).AddStop(1, RGBA.Parse("#e6dccb"));
            g.FillStyle = sky; g.FillRect(0, 0, W, H / 2.0);
            // 地面一侧不要太暗：刀刃 / 金属朝下的面反射地面
            var gr = Paint.Linear(0, H / 2.0, 0, H).AddStop(0, RGBA.Parse("#b4a78e")).AddStop(0.25, RGBA.Parse("#9a8c74")).AddStop(1, RGBA.Parse("#8a7c66"));
            g.FillStyle = gr; g.FillRect(0, H / 2.0, W, H / 2.0);
            // 云带
            var rr = new FigRng(31);
            for (int i = 0; i < 26; i++)
            {
                double x = rr.range(0, W), y = rr.range(H * 0.18, H * 0.44), w = rr.range(20, 60), h = rr.range(3, 8);
                g.FillStyle = Paint.Solid("rgba(255,250,240," + rr.range(0.15, 0.4).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ")");
                g.BeginPath(); g.Ellipse(x, y, w, h, 0, 0, TAU); g.Fill();
            }
            // 太阳（方位约 40°，仰角约 35°）+ 一侧较暗的天空
            double sx = W * 0.62, sy = H * 0.3;
            g.FillStyle = FigPainter.GradRad(sx, sy, 0, 34, 0, new object[] { 0.0, "rgba(255,252,236,1)", 0.18, "rgba(255,240,200,0.9)", 1.0, "rgba(255,230,180,0)" }); g.FillRect(0, 0, W, H);
            var dark = Paint.Linear(0, 0, W, 0).AddStop(0, RGBA.Parse("rgba(20,24,40,0.35)")).AddStop(0.3, RGBA.Parse("rgba(20,24,40,0)")).AddStop(0.9, RGBA.Parse("rgba(20,24,40,0)")).AddStop(1, RGBA.Parse("rgba(20,24,40,0.35)"));
            g.FillStyle = dark; g.FillRect(0, 0, W, H);
            // 地平线一道亮带 + 两三道柔光箱式的细亮带
            g.FillStyle = Paint.Solid("rgba(255,244,220,0.5)"); g.FillRect(0, H / 2.0 - 2, W, 3);
            g.FillStyle = Paint.Solid("rgba(255,252,244,0.55)"); g.FillRect(W * 0.08, H * 0.4, W * 0.3, 2); g.FillRect(W * 0.55, H * 0.43, W * 0.25, 2);
            g.FillStyle = Paint.Solid("rgba(255,252,244,0.4)"); g.FillRect(W * 0.3, H * 0.16, W * 0.35, 2);
            return g;
        }
        // three 的等距柱状映射：世界方向 d（three 坐标）→ (u, v)；v = 1 为画布顶边
        public static void EquirectUV(double dx, double dy, double dz, out double u, out double v)
        {
            u = Math.Atan2(dz, dx) / TAU + 0.5;
            v = Math.Asin(Math.Max(-1, Math.Min(1, dy))) / PI + 0.5;
        }

        // ---- 环境贴图 → 立方体贴图（three 把 envMap 预滤波成 PMREM；这里在 CPU 上生成等效的各级模糊，Sanguo/Figure 着色器取样）
        // 面的朝向按 Unity（D3D）约定，s、t ∈ [−1, 1]，t 自上而下（像素第 0 行 = 面的顶边）：
        //   +X (1, −t, −s)  −X (−1, −t, s)  +Y (s, 1, t)  −Y (s, −1, −t)  +Z (s, −t, 1)  −Z (−s, −t, −1)
        // 方向（Unity 世界坐标）→ three 方向 (x, y, −z) → 等距柱状 UV（EquirectUV）。
        // 每一级是原图的球面高斯模糊（von Mises–Fisher 核 exp((cos θ − 1) / σ²)，小角度时 = exp(−θ² / 2σ²)，按像素立体角加权）。
        // σ 取 three PMREM 各级的等效值：PMREM 是沿两组大圆的可分离近似，名义 σ（1/32、1/16、0.125、0.215、0.35、0.446、0.526、0.582）
        // 偏大；用网页版真实的 textureCubeUV 输出（Tests~/figure/tex.sh 的 web-pmrem.json，256 个方向）拟合出各整数 mip 的等效 σ：
        //   mip 6 0.02  5 0.045  4 0.08  3 0.15  2 0.225  1 0.325  0 0.425  −1 0.5  −2 0.55（直接模糊时平均差约 1/255）。
        // 反射贴图 ENV_CUBE² 共 8 级（面边长逐级减半；一级能表示的模糊约 σ ≥ 面上一个像素的角宽，所以底边取 128）：
        //   0–3 级 = σ 0.02 / 0.045 / 0.08 / 0.15（PMREM mip 6 / 5 / 4 / 3）；4 级 = σ 0.325（mip 1）；5–7 级 = σ 0.55（mip −2 = 粗糙度 1）。
        //   σ ≤ 0.15 的四级：等距柱状图上的可分离高斯（纬向按 cos φ 放宽）后双线性取样；更宽的用球面核直接求和（小图）。
        //   着色器按 three roughnessToMip 求出 PMREM mip，再换成本贴图的级别（Figure.shader 的 sgEnvLod；mip 2、0、−1 落在两级之间，
        //   三线性过滤 = PMREM 的两级插值）。
        // 辐照度贴图 ENV_IRR²（无 mip）= σ 0.55（three getIBLIrradiance：textureCubeUV(envMap, n, 1.0)）。
        public const int ENV_CUBE = 128, ENV_IRR = 8;
        public static readonly double[] ENV_SIGMA = { 0.02, 0.045, 0.08, 0.15, 0.325, 0.55, 0.55, 0.55 };
        public const double ENV_SEP_MAX = 0.15;                  // σ 不超过它的级别用可分离模糊
        public const double ENV_IRR_SIGMA = 0.55;
        public static void CubeDir(int face, double s, double t, out double x, out double y, out double z)
        {
            switch (face)
            {
                case 0: x = 1; y = -t; z = -s; break;
                case 1: x = -1; y = -t; z = s; break;
                case 2: x = s; y = 1; z = t; break;
                case 3: x = s; y = -1; z = -t; break;
                case 4: x = s; y = -t; z = 1; break;
                default: x = -s; y = -t; z = -1; break;
            }
            double l = Math.Sqrt(x * x + y * y + z * z); x /= l; y /= l; z /= l;
        }
        // 等距柱状图（RGB 0..1，第 0 行 = 画布顶边）
        public sealed class EnvImage
        {
            public int W, H; public float[] rgb;
            public static EnvImage FromRaster(Raster r)
            {
                var px = r.ToRGBA8(); var e = new EnvImage { W = r.W, H = r.H, rgb = new float[r.W * r.H * 3] };
                for (int i = 0; i < r.W * r.H; i++) { e.rgb[i * 3] = px[i * 4] / 255f; e.rgb[i * 3 + 1] = px[i * 4 + 1] / 255f; e.rgb[i * 3 + 2] = px[i * 4 + 2] / 255f; }
                return e;
            }
            // 2× 缩小（2×2 平均）
            public EnvImage Half()
            {
                var e = new EnvImage { W = W / 2, H = H / 2 }; e.rgb = new float[e.W * e.H * 3];
                for (int y = 0; y < e.H; y++) for (int x = 0; x < e.W; x++) for (int c = 0; c < 3; c++)
                    e.rgb[(y * e.W + x) * 3 + c] = (rgb[((2 * y) * W + 2 * x) * 3 + c] + rgb[((2 * y) * W + 2 * x + 1) * 3 + c] + rgb[((2 * y + 1) * W + 2 * x) * 3 + c] + rgb[((2 * y + 1) * W + 2 * x + 1) * 3 + c]) * 0.25f;
                return e;
            }
            // three 方向 → 双线性取样（u 环绕、v 夹紧；= 贴图 RepeatWrapping / ClampToEdge）
            public void Sample(double dx, double dy, double dz, double[] o)
            {
                double u, v; EquirectUV(dx, dy, dz, out u, out v);
                double fx = u * W - 0.5, fy = (1 - v) * H - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); double tx = fx - x0, ty = fy - y0;
                int xa = ((x0 % W) + W) % W, xb = (xa + 1) % W, ya = Math.Max(0, Math.Min(H - 1, y0)), yb = Math.Max(0, Math.Min(H - 1, y0 + 1));
                for (int c = 0; c < 3; c++)
                {
                    double a = rgb[(ya * W + xa) * 3 + c], b = rgb[(ya * W + xb) * 3 + c], d = rgb[(yb * W + xa) * 3 + c], f = rgb[(yb * W + xb) * 3 + c];
                    o[c] = (a * (1 - tx) + b * tx) * (1 - ty) + (d * (1 - tx) + f * tx) * ty;
                }
            }
            // 可分离高斯模糊（σ 弧度）：先沿纬线（像素角宽 2π/W·cos φ，环绕），再沿经线（π/H，两极夹紧）；σ 较小时 ≈ 球面高斯
            public EnvImage BlurSep(double sigma)
            {
                var t = new float[W * H * 3]; var o = new EnvImage { W = W, H = H, rgb = new float[W * H * 3] };
                for (int y = 0; y < H; y++)
                {
                    double lat = (0.5 - (y + 0.5) / H) * PI, sx = sigma / (TAU / W * Math.Max(1e-3, Math.Cos(lat)));
                    int R = (int)Math.Min(W / 2, Math.Ceiling(sx * 3.5));
                    var w = new double[R + 1]; double ws = 0;
                    for (int k = 0; k <= R; k++) { w[k] = Math.Exp(-0.5 * k * k / (sx * sx)); ws += k == 0 ? w[k] : 2 * w[k]; }
                    for (int x = 0; x < W; x++) for (int c = 0; c < 3; c++)
                    {
                        double a = rgb[(y * W + x) * 3 + c] * w[0];
                        for (int k = 1; k <= R; k++) a += (rgb[(y * W + (x + k) % W) * 3 + c] + rgb[(y * W + (x - k + W * 4) % W) * 3 + c]) * w[k];
                        t[(y * W + x) * 3 + c] = (float)(a / ws);
                    }
                }
                double sy = sigma / (PI / H); int Ry = (int)Math.Ceiling(sy * 3.5);
                var wy = new double[Ry + 1]; for (int k = 0; k <= Ry; k++) wy[k] = Math.Exp(-0.5 * k * k / (sy * sy));
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) for (int c = 0; c < 3; c++)
                {
                    double a = 0, ws = 0;
                    for (int k = -Ry; k <= Ry; k++) { int yy = Math.Max(0, Math.Min(H - 1, y + k)); double ww = wy[Math.Abs(k)]; a += t[(yy * W + x) * 3 + c] * ww; ws += ww; }
                    o.rgb[(y * W + x) * 3 + c] = (float)(a / ws);
                }
                return o;
            }
            double[] dirs, wts, lats;
            // 球面高斯模糊后在 three 方向 d 的值（只扫核半径 θmax 以内的行与列：权重 < e^−14 的不计）
            public void Blur(double dx, double dy, double dz, double sigma, double[] o)
            {
                if (dirs == null)
                {
                    dirs = new double[W * H * 3]; wts = new double[W * H]; lats = new double[H];
                    for (int y = 0; y < H; y++)
                    {
                        double lat = lats[y] = (0.5 - (y + 0.5) / H) * PI;     // 第 0 行 = 顶边（北极一侧）
                        for (int x = 0; x < W; x++)
                        {
                            double lon = ((x + 0.5) / W - 0.5) * TAU;      // EquirectUV 的反函数：u = atan2(z, x) / 2π + 0.5
                            int i = y * W + x;
                            dirs[i * 3] = Math.Cos(lat) * Math.Cos(lon); dirs[i * 3 + 1] = Math.Sin(lat); dirs[i * 3 + 2] = Math.Cos(lat) * Math.Sin(lon);
                            wts[i] = Math.Cos(lat);
                        }
                    }
                }
                double k = 1 / (sigma * sigma), r = 0, g = 0, b = 0, ws = 0, cut = 1 - 14 * sigma * sigma;
                double tmax = cut > -1 ? Math.Acos(cut) : PI, latd = Math.Asin(Math.Max(-1, Math.Min(1, dy))), lond = Math.Atan2(dz, dx);
                double pad = PI / H;                                         // 半个像素以上的余量
                for (int y = 0; y < H; y++)
                {
                    double lat = lats[y];
                    if (Math.Abs(lat - latd) > tmax + pad) continue;
                    // 这一行上离 d 不超过 θmax 的经度范围：|Δλ| ≤ asin(sin θmax / cos φ)（θmax < π/2 且 cos φ > sin θmax 时），否则整行
                    int x0 = 0, x1 = W - 1;
                    double cl = Math.Cos(lat), st = Math.Sin(Math.Min(tmax + pad, PI / 2));
                    if (tmax + pad < PI / 2 && cl > st)
                    {
                        double hw = Math.Asin(st / cl) + TAU / W;
                        double xc = (lond / TAU + 0.5) * W - 0.5;
                        x0 = (int)Math.Floor(xc - hw / TAU * W); x1 = (int)Math.Ceiling(xc + hw / TAU * W);
                        if (x1 - x0 >= W - 1) { x0 = 0; x1 = W - 1; }
                    }
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        int i = y * W + ((xx % W) + W) % W;
                        double c = dx * dirs[i * 3] + dy * dirs[i * 3 + 1] + dz * dirs[i * 3 + 2];
                        if (c < cut) continue;
                        double w = Math.Exp((c - 1) * k) * wts[i];
                        r += rgb[i * 3] * w; g += rgb[i * 3 + 1] * w; b += rgb[i * 3 + 2] * w; ws += w;
                    }
                }
                if (ws <= 0) { Sample(dx, dy, dz, o); return; }
                o[0] = r / ws; o[1] = g / ws; o[2] = b / ws;
            }
        }
        // 立方体贴图的一面（RGB，第 0 行 = 面的顶边）；sigma = 0：直接取样
        static float[] CubeFace(EnvImage src, int face, int size, double sigma)
        {
            var o = new float[size * size * 3]; var c = new double[3];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                double dx, dy, dz; CubeDir(face, (x + 0.5) / size * 2 - 1, (y + 0.5) / size * 2 - 1, out dx, out dy, out dz);
                if (sigma > 0) src.Blur(dx, dy, -dz, sigma, c); else src.Sample(dx, dy, -dz, c);
                int i = (y * size + x) * 3; o[i] = (float)c[0]; o[i + 1] = (float)c[1]; o[i + 2] = (float)c[2];
            }
            return o;
        }
        // 模糊的取样源：σ 越大用越小的等距柱状图（像素角宽 ≤ σ / 2 左右）
        static EnvImage BlurSource(List<EnvImage> pyr, double sigma)
        {
            var e = pyr[0];
            foreach (var p in pyr) if (PI / p.H <= sigma * 0.5) e = p;
            return e;
        }
        // levels[级][面]、irr[面]（RGB，第 0 行 = 面的顶边）；Radiance / Irradiance 为 CPU 端的取样（测试与软件光栅器用，= 着色器的 texCUBElod）
        public sealed class EnvCubes
        {
            public float[][][] levels; public float[][] irr; public double ms;
            // 方向 d（Unity 世界坐标）按主轴选面（CubeDir 的反函数），面内双线性（边上夹紧，不做跨面过滤）
            public static void Sample(float[][] faces, int size, double dx, double dy, double dz, double[] o)
            {
                double ax = Math.Abs(dx), ay = Math.Abs(dy), az = Math.Abs(dz), m, sc, tc; int f;
                if (ax >= ay && ax >= az) { m = ax; if (dx > 0) { f = 0; sc = -dz; tc = -dy; } else { f = 1; sc = dz; tc = -dy; } }
                else if (ay >= az) { m = ay; if (dy > 0) { f = 2; sc = dx; tc = dz; } else { f = 3; sc = dx; tc = -dz; } }
                else { m = az; if (dz > 0) { f = 4; sc = dx; tc = -dy; } else { f = 5; sc = -dx; tc = -dy; } }
                double fx = (sc / m * 0.5 + 0.5) * size - 0.5, fy = (tc / m * 0.5 + 0.5) * size - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); double tx = fx - x0, ty = fy - y0;
                int xa = Math.Max(0, Math.Min(size - 1, x0)), xb = Math.Max(0, Math.Min(size - 1, x0 + 1)), ya = Math.Max(0, Math.Min(size - 1, y0)), yb = Math.Max(0, Math.Min(size - 1, y0 + 1));
                var F = faces[f];
                for (int c = 0; c < 3; c++)
                {
                    double a = F[(ya * size + xa) * 3 + c], b = F[(ya * size + xb) * 3 + c], e = F[(yb * size + xa) * 3 + c], g = F[(yb * size + xb) * 3 + c];
                    o[c] = (a * (1 - tx) + b * tx) * (1 - ty) + (e * (1 - tx) + g * tx) * ty;
                }
            }
            // texCUBElod(_EnvCube, (d, lod))：级内双线性、级间线性
            public void Radiance(double dx, double dy, double dz, double lod, double[] o)
            {
                int n = levels.Length;
                lod = Math.Max(0, Math.Min(n - 1, lod));
                int l0 = (int)Math.Floor(lod), l1 = Math.Min(n - 1, l0 + 1); double t = lod - l0;
                Sample(levels[l0], ENV_CUBE >> l0, dx, dy, dz, o);
                if (t == 0) return;
                var o2 = new double[3]; Sample(levels[l1], ENV_CUBE >> l1, dx, dy, dz, o2);
                for (int c = 0; c < 3; c++) o[c] += (o2[c] - o[c]) * t;
            }
            public void Irradiance(double dx, double dy, double dz, double[] o) { Sample(irr, ENV_IRR, dx, dy, dz, o); }
            // three roughnessToMip（PMREM 的 mip −2..6）→ 本贴图的级别（= Figure.shader 的 sgEnvLod）
            public static double Lod(double r)
            {
                double pm;
                if (r >= 0.8) pm = (1 - r) * 5 - 2;
                else if (r >= 0.4) pm = (0.8 - r) * 7.5 - 1;
                else if (r >= 0.305) pm = (0.4 - r) / 0.095 + 2;
                else if (r >= 0.21) pm = (0.305 - r) / 0.095 + 3;
                else pm = -2 * Math.Log(1.16 * r, 2);
                pm = Math.Max(-2, Math.Min(6, pm));
                return pm >= 3 ? 6 - pm : (pm >= 1 ? 3 + (3 - pm) * 0.5 : 4 + (1 - pm) / 3);
            }
        }
        // 分步生成（每面一步，约 1–5 ms；Figure.Preload 分片用）；最后一步之后 outp 填好
        public static IEnumerable<object> EnvCubesGen(EnvCubes outp)
        {
            double t0 = Now();
            var pyr = new List<EnvImage> { EnvImage.FromRaster(PaintEnv()) };
            while (pyr[pyr.Count - 1].H > 16) pyr.Add(pyr[pyr.Count - 1].Half());
            var levels = new float[ENV_SIGMA.Length][][]; var irr = new float[6][];
            for (int l = 0; l < ENV_SIGMA.Length; l++)
            {
                levels[l] = new float[6][];
                int size = ENV_CUBE >> l;
                // σ 与上一级相同（5–7 级）：由上一级 2×2 平均
                if (l > 0 && ENV_SIGMA[l] == ENV_SIGMA[l - 1] && size * 2 == ENV_CUBE >> (l - 1))
                {
                    for (int f = 0; f < 6; f++)
                    {
                        var a = levels[l - 1][f]; var o = new float[size * size * 3];
                        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) for (int c = 0; c < 3; c++)
                            o[(y * size + x) * 3 + c] = (a[((2 * y) * size * 2 + 2 * x) * 3 + c] + a[((2 * y) * size * 2 + 2 * x + 1) * 3 + c] + a[((2 * y + 1) * size * 2 + 2 * x) * 3 + c] + a[((2 * y + 1) * size * 2 + 2 * x + 1) * 3 + c]) * 0.25f;
                        levels[l][f] = o;
                    }
                    continue;
                }
                EnvImage sep = null;
                if (ENV_SIGMA[l] <= ENV_SEP_MAX)
                {
                    // 取样源：像素角宽 ≤ σ / 1.5 的最小一张（σ 0.08 → 128×64、0.15 → 64×32）
                    var src = pyr[0]; foreach (var p in pyr) if (PI / p.H <= ENV_SIGMA[l] / 1.5) src = p;
                    sep = src.BlurSep(ENV_SIGMA[l]); yield return null;
                }
                for (int f = 0; f < 6; f++)
                {
                    levels[l][f] = sep != null ? CubeFace(sep, f, size, 0) : CubeFace(BlurSource(pyr, ENV_SIGMA[l]), f, size, ENV_SIGMA[l]);
                    yield return null;
                }
            }
            for (int f = 0; f < 6; f++) irr[f] = CubeFace(BlurSource(pyr, ENV_IRR_SIGMA), f, ENV_IRR, ENV_IRR_SIGMA);
            outp.levels = levels; outp.irr = irr; outp.ms = Now() - t0;
        }
        public static EnvCubes MakeEnvCubes() { var r = new EnvCubes(); foreach (var _ in EnvCubesGen(r)) { } return r; }
    }

#if !SANGUO_HEADLESS
    // ------------------------------------------------------------------ Unity 贴图（图集、面部、环境）
    public static class FigTextures
    {
        static Texture2D map, nrm, orm, flatN, flatOrm;
        static Cubemap cube, irr;
        static Texture2D Tex(byte[] data, int n, bool linear, string name)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, linear);
            t.name = name;
            t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4;
            t.SetPixelData(data, 0);          // 只给第 0 级（LoadRawTextureData 要求整条 mip 链的数据）；Apply 生成其余各级
            t.Apply(true, true);
            return t;
        }
        public static bool AtlasReady { get { return map != null; } }
        // 上传图集（首次用到时同步生成，约 0.3–1 秒；Figure.Preload 可提前分片生成）。上传后丢弃 CPU 端的数据（3 × 4 MB）
        public static void EnsureAtlas()
        {
            if (map != null) return;
            var d = FigAtlas.Data;
            map = Tex(d.map, FigAtlas.N, false, "SG.Figure.atlas");
            nrm = Tex(d.nrm, FigAtlas.N, true, "SG.Figure.atlasNormal");
            orm = Tex(d.orm, FigAtlas.N, true, "SG.Figure.atlasORM");
            FigAtlas.Reset();
        }
        public static Texture2D Map { get { EnsureAtlas(); return map; } }
        public static Texture2D Normal { get { EnsureAtlas(); return nrm; } }
        public static Texture2D ORM { get { EnsureAtlas(); return orm; } }
        // 头部（面部贴图）用的平坦法线 / ORM 小贴图（R = AO 1、G = 粗糙度 0.7、B = 金属度 0）
        static Texture2D Small(byte r, byte g, byte b, bool linear)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
            var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(r, g, b, 255);
            t.SetPixels32(px); t.filterMode = FilterMode.Bilinear; t.Apply(false, true);
            FigStats.textures++;
            return t;
        }
        public static Texture2D FlatNormal { get { return flatN ?? (flatN = Small(128, 128, 255, true)); } }
        public static Texture2D FlatORM { get { return flatOrm ?? (flatOrm = Small(255, 178, 0, true)); } }
        // 环境贴图（= JS 材质的 envMap）：反射立方体贴图 _EnvCube（128²，8 级预滤波，≈ 0.5 MB）与辐照度立方体贴图 _EnvIrr（8²），
        // 由 FigMaterials 设到每个人物 / 士兵群材质上（不依赖 RenderSettings）；生成方法见 FigAtlas.MakeEnvCubes
        static readonly CubemapFace[] FACES = { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
        static Color[] Colors(float[] rgb)
        {
            var c = new Color[rgb.Length / 3];
            for (int i = 0; i < c.Length; i++) c[i] = new Color(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], 1);
            return c;
        }
        public static bool EnvReady { get { return cube != null && irr != null; } }
        // 分步生成并上传（Figure.Preload 的一项；每步约 1–5 ms）
        public static IEnumerable<object> EnvGen()
        {
            if (EnvReady) yield break;
            var e = new FigAtlas.EnvCubes();
            foreach (var o in FigAtlas.EnvCubesGen(e)) yield return o;
            if (!EnvReady) Upload(e);
        }
        static void MakeEnv() { foreach (var _ in EnvGen()) { } }
        static void Upload(FigAtlas.EnvCubes e)
        {
            cube = new Cubemap(FigAtlas.ENV_CUBE, TextureFormat.RGBA32, true) { name = "SG.Figure.envCube" };
            cube.filterMode = FilterMode.Trilinear; cube.wrapMode = TextureWrapMode.Clamp;
            for (int l = 0; l < e.levels.Length; l++) for (int f = 0; f < 6; f++) cube.SetPixels(Colors(e.levels[l][f]), FACES[f], l);
            cube.Apply(false, true);         // 各级都已给出，不再自动生成 mip
            irr = new Cubemap(FigAtlas.ENV_IRR, TextureFormat.RGBA32, false) { name = "SG.Figure.envIrr" };
            irr.filterMode = FilterMode.Bilinear; irr.wrapMode = TextureWrapMode.Clamp;
            for (int f = 0; f < 6; f++) irr.SetPixels(Colors(e.irr[f]), FACES[f]);
            irr.Apply(false, true);
            FigStats.textures += 2; FigStats.envMs = Math.Round(e.ms);
        }
        public static Cubemap EnvCube() { if (cube == null) MakeEnv(); return cube; }
        public static Cubemap EnvIrr { get { if (irr == null) MakeEnv(); return irr; } }
        // 名将面部贴图（按外貌缓存，引用计数）
        sealed class FaceEntry { public Texture2D tex; public int refs; }
        static readonly Dictionary<string, FaceEntry> faces = new Dictionary<string, FaceEntry>();
        public static int FaceCount { get { return faces.Count; } }
        public static string FaceKey(JObj sp, int size) { return ((sp != null ? (sp.S("key") ?? sp.S("name")) : null) ?? "?") + "|" + size; }
        public static bool HasFace(string key) { return faces.ContainsKey(key); }
        public static Texture2D Face(JObj sp, int size)
        {
            string key = FaceKey(sp, size);
            FaceEntry e;
            if (faces.TryGetValue(key, out e)) { e.refs++; return e.tex; }
            var r = FigAtlas.PaintFace(FigFaceDesc.FromSpec(sp ?? new JObj()), size);
            var t = r.ToTexture(true, false);
            t.anisoLevel = 4; t.filterMode = FilterMode.Trilinear; t.name = "SG.Figure.face:" + key;
            faces[key] = new FaceEntry { tex = t, refs = 1 };
            FigStats.textures++;
            return t;
        }
        // 后台线程画好的面部栅格（Preload 用）
        public static void AddFace(string key, Raster r)
        {
            if (faces.ContainsKey(key)) return;
            var t = r.ToTexture(true, false);
            t.anisoLevel = 4; t.filterMode = FilterMode.Trilinear; t.name = "SG.Figure.face:" + key;
            faces[key] = new FaceEntry { tex = t, refs = 0 };
            FigStats.textures++;
        }
        public static void ReleaseFace(Texture2D t)
        {
            if (t == null) return;
            foreach (var kv in faces)
                if (kv.Value.tex == t)
                {
                    if (--kv.Value.refs <= 0) { faces.Remove(kv.Key); UnityEngine.Object.Destroy(t); FigStats.textures--; }
                    return;
                }
        }
    }
#endif
}
