// 自写的二维矢量栅格器：Canvas2D 的一个子集，画在浮点 RGBA 缓冲（预乘 alpha）上。
// 头像（View/Portrait.cs）用它代替网页版的 Canvas2D / Path2D：
//   save / restore、translate / scale / rotate / transform / setTransform、
//   beginPath / moveTo / lineTo / bezierCurveTo / quadraticCurveTo / arc / ellipse / rect / roundRect / closePath、
//   Path2D（含 SVG 路径字符串，支持 M L H V C S Q T A Z 及其相对形式）、
//   fill（nonzero / evenodd；纯色、线性 / 径向渐变）、stroke（线宽、圆 / 尖 / 斜接、端点、虚线）、
//   globalAlpha、clip（抗锯齿蒙版，可嵌套）、clearRect、drawImage（另一张 Raster）。
// 抗锯齿：每像素行 16 条子扫描线，每条子扫描线上的跨度按精确的横向覆盖累加（与 Skia 的超采样同理）。
// 描边：折线化后拆成线段四边形 + 连接 / 端点多边形，按 nonzero 取并集（重叠处不重复着色）。
// 设备宽度不足 1 像素的描边按 1 像素画并按宽度降低不透明度（与 Skia / Chrome 的细线处理一致）。
// 纯 C#、不依赖 UnityEngine（Unity 部分在文件末尾，SANGUO_HEADLESS 时不编译），可在后台线程使用（每个实例单线程）。
using System;
using System.Collections.Generic;
using System.Globalization;
#if !SANGUO_HEADLESS
using UnityEngine;
#endif

namespace Sanguo
{
    public enum FillRule { NonZero, EvenOdd }
    public enum LineJoin { Miter, Round, Bevel }
    public enum LineCap { Butt, Round, Square }

    // 仿射矩阵（与 Canvas 的 transform(a, b, c, d, e, f) 同义）：x' = a·x + c·y + e，y' = b·x + d·y + f
    public struct Mat2D
    {
        public double a, b, c, d, e, f;
        public static readonly Mat2D Identity = new Mat2D(1, 0, 0, 1, 0, 0);
        public Mat2D(double a, double b, double c, double d, double e, double f) { this.a = a; this.b = b; this.c = c; this.d = d; this.e = e; this.f = f; }
        // this × m：先作用 m，再作用 this
        public Mat2D Mul(Mat2D m)
        {
            return new Mat2D(a * m.a + c * m.b, b * m.a + d * m.b, a * m.c + c * m.d, b * m.c + d * m.d, a * m.e + c * m.f + e, b * m.e + d * m.f + f);
        }
        public double X(double x, double y) { return a * x + c * y + e; }
        public double Y(double x, double y) { return b * x + d * y + f; }
        public Mat2D Inverse()
        {
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12) return Identity;
            double id = 1 / det;
            return new Mat2D(d * id, -b * id, -c * id, a * id, (c * f - d * e) * id, (b * e - a * f) * id);
        }
        // 线宽等长度的缩放（相似变换时精确）
        public double LenScale { get { return Math.Sqrt(Math.Abs(a * d - b * c)); } }
    }

    // 颜色（非预乘，0..1，sRGB 空间混合，与浏览器一致）
    public struct RGBA
    {
        public float r, g, b, a;
        public RGBA(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        // '#rgb' / '#rrggbb' / 'rgb(r,g,b)' / 'rgba(r,g,b,a)' / 'none' / 'transparent'
        public static RGBA Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return new RGBA(0, 0, 0, 1);
            s = s.Trim();
            if (s[0] == '#')
            {
                string h = s.Substring(1);
                if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
                int n;
                if (h.Length >= 6 && int.TryParse(h.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
                    return new RGBA(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f, 1);
                return new RGBA(0, 0, 0, 1);
            }
            if (s.StartsWith("rgb"))
            {
                int i0 = s.IndexOf('('), i1 = s.IndexOf(')');
                if (i0 > 0 && i1 > i0)
                {
                    var p = s.Substring(i0 + 1, i1 - i0 - 1).Split(',');
                    float[] v = { 0, 0, 0, 1 };
                    for (int i = 0; i < p.Length && i < 4; i++) float.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
                    return new RGBA(v[0] / 255f, v[1] / 255f, v[2] / 255f, v[3]);
                }
            }
            if (s == "white") return new RGBA(1, 1, 1, 1);
            if (s == "transparent" || s == "none") return new RGBA(0, 0, 0, 0);
            return new RGBA(0, 0, 0, 1);
        }
    }

    // 填充样式：纯色或渐变。渐变的几何在“渐变空间”定义，toGrad 把设备坐标映射到渐变空间
    public sealed class Paint
    {
        public RGBA color;
        public int kind;            // 0 纯色 1 线性 2 径向
        public double x1, y1, x2, y2, r, fx, fy;   // 线性：(x1,y1)→(x2,y2)；径向：圆心 (x1,y1)、半径 r、焦点 (fx,fy)
        public float[] stops;       // offset, r, g, b, a …
        public Mat2D toGrad = Mat2D.Identity;
        public static Paint Solid(RGBA c) { return new Paint { color = c }; }
        public static Paint Solid(string css) { return new Paint { color = RGBA.Parse(css) }; }
        public static Paint Linear(double x1, double y1, double x2, double y2) { return new Paint { kind = 1, x1 = x1, y1 = y1, x2 = x2, y2 = y2, stops = new float[0] }; }
        public static Paint Radial(double cx, double cy, double r) { return new Paint { kind = 2, x1 = cx, y1 = cy, r = r, fx = cx, fy = cy, stops = new float[0] }; }
        public Paint AddStop(double offset, RGBA c)
        {
            var s = new float[stops.Length + 5];
            Array.Copy(stops, s, stops.Length);
            float o = (float)Math.Max(0, Math.Min(1, offset));
            if (stops.Length > 0) o = Math.Max(o, stops[stops.Length - 5]);
            s[stops.Length] = o; s[stops.Length + 1] = c.r; s[stops.Length + 2] = c.g; s[stops.Length + 3] = c.b; s[stops.Length + 4] = c.a;
            stops = s;
            return this;
        }
        public Paint Clone() { return (Paint)MemberwiseClone(); }
        // 设备像素中心 (px, py) 处的颜色
        public RGBA At(double px, double py)
        {
            if (kind == 0) return color;
            double gx = toGrad.X(px, py), gy = toGrad.Y(px, py), t;
            if (kind == 1)
            {
                double dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy;
                t = l2 > 0 ? ((gx - x1) * dx + (gy - y1) * dy) / l2 : 0;
            }
            else
            {
                double dx = gx - fx, dy = gy - fy;
                if (fx == x1 && fy == y1) t = r > 0 ? Math.Sqrt(dx * dx + dy * dy) / r : 1;
                else
                {
                    // 焦点不在圆心：解 |f + t·(p−f) 方向| 与圆的交点
                    double cdx = fx - x1, cdy = fy - y1;
                    double A = dx * dx + dy * dy, B = 2 * (dx * cdx + dy * cdy), C = cdx * cdx + cdy * cdy - r * r;
                    double disc = B * B - 4 * A * C;
                    double k = A > 0 && disc >= 0 ? (-B + Math.Sqrt(disc)) / (2 * A) : 1;
                    t = k > 0 ? 1 / k : 1;
                }
            }
            return Sample(t);
        }
        RGBA Sample(double t)
        {
            int n = stops.Length / 5;
            if (n == 0) return new RGBA(0, 0, 0, 0);
            if (t <= stops[0]) return new RGBA(stops[1], stops[2], stops[3], stops[4]);
            int last = (n - 1) * 5;
            if (t >= stops[last]) return new RGBA(stops[last + 1], stops[last + 2], stops[last + 3], stops[last + 4]);
            for (int i = 0; i < n - 1; i++)
            {
                int j = i * 5;
                float o0 = stops[j], o1 = stops[j + 5];
                if (t >= o0 && t <= o1)
                {
                    float k = o1 > o0 ? (float)((t - o0) / (o1 - o0)) : 1f;
                    // 预乘插值（浏览器的做法；不透明色标时与直接插值相同）
                    float a0 = stops[j + 4], a1 = stops[j + 9];
                    float a = a0 + (a1 - a0) * k;
                    if (a <= 0) return new RGBA(0, 0, 0, 0);
                    float r = (stops[j + 1] * a0 + (stops[j + 6] * a1 - stops[j + 1] * a0) * k) / a;
                    float g = (stops[j + 2] * a0 + (stops[j + 7] * a1 - stops[j + 2] * a0) * k) / a;
                    float b = (stops[j + 3] * a0 + (stops[j + 8] * a1 - stops[j + 3] * a0) * k) / a;
                    return new RGBA(r, g, b, a);
                }
            }
            return new RGBA(stops[last + 1], stops[last + 2], stops[last + 3], stops[last + 4]);
        }
    }

    // 路径（Canvas 的 Path2D）：坐标为用户空间，填充 / 描边时乘以当前变换
    public sealed class Path2D
    {
        // 指令：0 M(x,y) 1 L(x,y) 2 C(x1,y1,x2,y2,x,y) 3 Q(x1,y1,x,y) 4 Z
        internal readonly List<byte> cmds = new List<byte>();
        internal readonly List<double> pts = new List<double>();
        double cx, cy, sx, sy;
        bool hasCur;

        public Path2D() { }
        public Path2D(string svgPathData) { AddSvg(svgPathData); }

        public bool Empty { get { return cmds.Count == 0; } }
        public void MoveTo(double x, double y) { cmds.Add(0); pts.Add(x); pts.Add(y); cx = sx = x; cy = sy = y; hasCur = true; }
        public void LineTo(double x, double y) { if (!hasCur) { MoveTo(x, y); return; } cmds.Add(1); pts.Add(x); pts.Add(y); cx = x; cy = y; }
        public void BezierCurveTo(double x1, double y1, double x2, double y2, double x, double y)
        {
            if (!hasCur) MoveTo(x1, y1);
            cmds.Add(2); pts.Add(x1); pts.Add(y1); pts.Add(x2); pts.Add(y2); pts.Add(x); pts.Add(y); cx = x; cy = y;
        }
        public void QuadraticCurveTo(double x1, double y1, double x, double y)
        {
            if (!hasCur) MoveTo(x1, y1);
            cmds.Add(3); pts.Add(x1); pts.Add(y1); pts.Add(x); pts.Add(y); cx = x; cy = y;
        }
        public void ClosePath() { if (!hasCur) return; cmds.Add(4); cx = sx; cy = sy; }
        public void Rect(double x, double y, double w, double h) { MoveTo(x, y); LineTo(x + w, y); LineTo(x + w, y + h); LineTo(x, y + h); ClosePath(); MoveTo(x, y); }
        public void RoundRect(double x, double y, double w, double h, double r)
        {
            r = Math.Max(0, Math.Min(r, Math.Min(Math.Abs(w), Math.Abs(h)) / 2));
            if (r <= 0) { Rect(x, y, w, h); return; }
            const double K = 0.5522847498307936;
            MoveTo(x + r, y);
            LineTo(x + w - r, y); BezierCurveTo(x + w - r + r * K, y, x + w, y + r - r * K, x + w, y + r);
            LineTo(x + w, y + h - r); BezierCurveTo(x + w, y + h - r + r * K, x + w - r + r * K, y + h, x + w - r, y + h);
            LineTo(x + r, y + h); BezierCurveTo(x + r - r * K, y + h, x, y + h - r + r * K, x, y + h - r);
            LineTo(x, y + r); BezierCurveTo(x, y + r - r * K, x + r - r * K, y, x + r, y);
            ClosePath(); MoveTo(x, y);
        }
        public void Arc(double x, double y, double r, double a0, double a1, bool ccw = false) { Ellipse(x, y, r, r, 0, a0, a1, ccw); }
        // Canvas 的 ellipse：有当前点时先连线到起点；整圈（|Δ| ≥ 2π）时闭合
        public void Ellipse(double x, double y, double rx, double ry, double rot, double a0, double a1, bool ccw = false)
        {
            const double TAU = Math.PI * 2;
            double sweep = a1 - a0;
            bool full = false;
            if (!ccw) { if (sweep >= TAU) { sweep = TAU; full = true; } else if (sweep < 0) { sweep = sweep % TAU; if (sweep < 0) sweep += TAU; } }
            else { if (-sweep >= TAU) { sweep = -TAU; full = true; } else if (sweep > 0) { sweep = sweep % TAU; if (sweep > 0) sweep -= TAU; } }
            double cr = Math.Cos(rot), sr = Math.Sin(rot);
            Func<double, double> PX = t => x + rx * Math.Cos(t) * cr - ry * Math.Sin(t) * sr;
            Func<double, double> PY = t => y + rx * Math.Cos(t) * sr + ry * Math.Sin(t) * cr;
            double sx0 = PX(a0), sy0 = PY(a0);
            if (hasCur) LineTo(sx0, sy0); else MoveTo(sx0, sy0);
            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
            double step = sweep / n, k = 4.0 / 3.0 * Math.Tan(step / 4);
            double t0 = a0;
            for (int i = 0; i < n; i++)
            {
                double t1 = t0 + step;
                double c0 = Math.Cos(t0), s0 = Math.Sin(t0), c1 = Math.Cos(t1), s1 = Math.Sin(t1);
                // 单位圆上的控制点 → 椭圆 → 旋转
                double ux1 = c0 - k * s0, uy1 = s0 + k * c0, ux2 = c1 + k * s1, uy2 = s1 - k * c1;
                BezierCurveTo(x + rx * ux1 * cr - ry * uy1 * sr, y + rx * ux1 * sr + ry * uy1 * cr,
                              x + rx * ux2 * cr - ry * uy2 * sr, y + rx * ux2 * sr + ry * uy2 * cr,
                              x + rx * c1 * cr - ry * s1 * sr, y + rx * c1 * sr + ry * s1 * cr);
                t0 = t1;
            }
            if (full) ClosePath();
        }
        public void AddPath(Path2D p)
        {
            int k = 0;
            foreach (var c in p.cmds)
            {
                switch (c)
                {
                    case 0: MoveTo(p.pts[k], p.pts[k + 1]); k += 2; break;
                    case 1: LineTo(p.pts[k], p.pts[k + 1]); k += 2; break;
                    case 2: BezierCurveTo(p.pts[k], p.pts[k + 1], p.pts[k + 2], p.pts[k + 3], p.pts[k + 4], p.pts[k + 5]); k += 6; break;
                    case 3: QuadraticCurveTo(p.pts[k], p.pts[k + 1], p.pts[k + 2], p.pts[k + 3]); k += 4; break;
                    case 4: ClosePath(); break;
                }
            }
        }

        // ---------------------------------------------------------------- SVG 路径数据
        public void AddSvg(string d)
        {
            if (string.IsNullOrEmpty(d)) return;
            int i = 0, n = d.Length;
            char cmd = ' ';
            double lcx = 0, lcy = 0;   // 上一个 C/S 或 Q/T 的控制点（用于反射）
            char prev = ' ';
            while (true)
            {
                SkipSep(d, ref i);
                if (i >= n) break;
                char ch = d[i];
                if (char.IsLetter(ch) && ch != 'e' && ch != 'E') { cmd = ch; i++; }
                else if (cmd == ' ') break;          // 数字前没有指令：无效
                bool rel = char.IsLower(cmd);
                double ox = rel ? cx : 0, oy = rel ? cy : 0;
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        {
                            double x, y; if (!Num(d, ref i, out x) || !Num(d, ref i, out y)) return;
                            MoveTo(ox + x, oy + y);
                            cmd = rel ? 'l' : 'L';   // 其后的坐标对按 lineto 处理
                            prev = 'M';
                            continue;
                        }
                    case 'L':
                        {
                            double x, y; if (!Num(d, ref i, out x) || !Num(d, ref i, out y)) return;
                            LineTo(ox + x, oy + y); prev = 'L'; break;
                        }
                    case 'H':
                        {
                            double x; if (!Num(d, ref i, out x)) return;
                            LineTo(ox + x, cy); prev = 'L'; break;
                        }
                    case 'V':
                        {
                            double y; if (!Num(d, ref i, out y)) return;
                            LineTo(cx, oy + y); prev = 'L'; break;
                        }
                    case 'C':
                        {
                            double x1, y1, x2, y2, x, y;
                            if (!Num(d, ref i, out x1) || !Num(d, ref i, out y1) || !Num(d, ref i, out x2) || !Num(d, ref i, out y2) || !Num(d, ref i, out x) || !Num(d, ref i, out y)) return;
                            BezierCurveTo(ox + x1, oy + y1, ox + x2, oy + y2, ox + x, oy + y);
                            lcx = ox + x2; lcy = oy + y2; prev = 'C'; break;
                        }
                    case 'S':
                        {
                            double x2, y2, x, y;
                            if (!Num(d, ref i, out x2) || !Num(d, ref i, out y2) || !Num(d, ref i, out x) || !Num(d, ref i, out y)) return;
                            double x1 = prev == 'C' ? 2 * cx - lcx : cx, y1 = prev == 'C' ? 2 * cy - lcy : cy;
                            BezierCurveTo(x1, y1, ox + x2, oy + y2, ox + x, oy + y);
                            lcx = ox + x2; lcy = oy + y2; prev = 'C'; break;
                        }
                    case 'Q':
                        {
                            double x1, y1, x, y;
                            if (!Num(d, ref i, out x1) || !Num(d, ref i, out y1) || !Num(d, ref i, out x) || !Num(d, ref i, out y)) return;
                            QuadraticCurveTo(ox + x1, oy + y1, ox + x, oy + y);
                            lcx = ox + x1; lcy = oy + y1; prev = 'Q'; break;
                        }
                    case 'T':
                        {
                            double x, y; if (!Num(d, ref i, out x) || !Num(d, ref i, out y)) return;
                            double x1 = prev == 'Q' ? 2 * cx - lcx : cx, y1 = prev == 'Q' ? 2 * cy - lcy : cy;
                            QuadraticCurveTo(x1, y1, ox + x, oy + y);
                            lcx = x1; lcy = y1; prev = 'Q'; break;
                        }
                    case 'A':
                        {
                            double rx, ry, rot, x, y; bool large, sweep;
                            if (!Num(d, ref i, out rx) || !Num(d, ref i, out ry) || !Num(d, ref i, out rot) || !Flag(d, ref i, out large) || !Flag(d, ref i, out sweep) || !Num(d, ref i, out x) || !Num(d, ref i, out y)) return;
                            SvgArc(cx, cy, rx, ry, rot, large, sweep, ox + x, oy + y);
                            prev = 'A'; break;
                        }
                    case 'Z':
                        ClosePath(); prev = 'Z';
                        continue;
                    default: return;
                }
            }
        }
        static void SkipSep(string d, ref int i) { while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\t' || d[i] == '\r')) i++; }
        static bool Num(string d, ref int i, out double v)
        {
            SkipSep(d, ref i);
            int s = i, n = d.Length;
            if (i < n && (d[i] == '-' || d[i] == '+')) i++;
            bool dot = false, dig = false;
            while (i < n)
            {
                char c = d[i];
                if (c >= '0' && c <= '9') { dig = true; i++; }
                else if (c == '.' && !dot) { dot = true; i++; }
                else break;
            }
            if (dig && i < n && (d[i] == 'e' || d[i] == 'E'))
            {
                int j = i + 1;
                if (j < n && (d[j] == '-' || d[j] == '+')) j++;
                if (j < n && d[j] >= '0' && d[j] <= '9') { i = j; while (i < n && d[i] >= '0' && d[i] <= '9') i++; }
            }
            if (!dig) { v = 0; i = s; return false; }
            v = FastNum(d, s, i);
            return true;
        }
        // 十进制数（无指数、≤ 15 位有效数字时与 double.Parse 结果相同：整数尾数 / 10^k 一次正确舍入）
        static readonly double[] P10 = { 1, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15 };
        static double FastNum(string d, int s, int e)
        {
            int i = s; bool neg = false;
            if (d[i] == '-' || d[i] == '+') { neg = d[i] == '-'; i++; }
            long m = 0; int digits = 0, frac = -1;
            for (; i < e; i++)
            {
                char c = d[i];
                if (c == '.') { frac = 0; continue; }
                if (c < '0' || c > '9') return double.Parse(d.Substring(s, e - s), NumberStyles.Float, CultureInfo.InvariantCulture);
                if (digits >= 15) return double.Parse(d.Substring(s, e - s), NumberStyles.Float, CultureInfo.InvariantCulture);
                m = m * 10 + (c - '0');
                if (m != 0) digits++;
                if (frac >= 0) frac++;
            }
            double v = frac > 0 ? m / P10[frac] : m;
            return neg ? -v : v;
        }
        static bool Flag(string d, ref int i, out bool v)
        {
            SkipSep(d, ref i);
            if (i < d.Length && (d[i] == '0' || d[i] == '1')) { v = d[i] == '1'; i++; return true; }
            v = false; return false;
        }
        // SVG 椭圆弧（端点参数化 → 圆心参数化，SVG 1.1 附录 F.6）
        void SvgArc(double x0, double y0, double rx, double ry, double rotDeg, bool large, bool sweep, double x, double y)
        {
            if (x0 == x && y0 == y) return;
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            if (rx == 0 || ry == 0) { LineTo(x, y); return; }
            double phi = rotDeg * Math.PI / 180, cp = Math.Cos(phi), sp = Math.Sin(phi);
            double dx2 = (x0 - x) / 2, dy2 = (y0 - y) / 2;
            double x1p = cp * dx2 + sp * dy2, y1p = -sp * dx2 + cp * dy2;
            double lam = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lam > 1) { double s = Math.Sqrt(lam); rx *= s; ry *= s; }
            double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
            double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
            double co = den > 0 ? Math.Sqrt(Math.Max(0, num / den)) : 0;
            if (large == sweep) co = -co;
            double cxp = co * rx * y1p / ry, cyp = -co * ry * x1p / rx;
            double ccx = cp * cxp - sp * cyp + (x0 + x) / 2, ccy = sp * cxp + cp * cyp + (y0 + y) / 2;
            double th1 = Ang(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            double dth = Ang((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
            if (!sweep && dth > 0) dth -= 2 * Math.PI;
            else if (sweep && dth < 0) dth += 2 * Math.PI;
            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(dth) / (Math.PI / 2) - 1e-9));
            double step = dth / n, k = 4.0 / 3.0 * Math.Tan(step / 4);
            double t0 = th1;
            for (int i = 0; i < n; i++)
            {
                double t1 = t0 + step;
                double c0 = Math.Cos(t0), s0 = Math.Sin(t0), c1 = Math.Cos(t1), s1 = Math.Sin(t1);
                double ux1 = c0 - k * s0, uy1 = s0 + k * c0, ux2 = c1 + k * s1, uy2 = s1 - k * c1;
                double ex = i == n - 1 ? x : ccx + rx * c1 * cp - ry * s1 * sp, ey = i == n - 1 ? y : ccy + rx * c1 * sp + ry * s1 * cp;
                BezierCurveTo(ccx + rx * ux1 * cp - ry * uy1 * sp, ccy + rx * ux1 * sp + ry * uy1 * cp,
                              ccx + rx * ux2 * cp - ry * uy2 * sp, ccy + rx * ux2 * sp + ry * uy2 * cp, ex, ey);
                t0 = t1;
            }
        }
        static double Ang(double ux, double uy, double vx, double vy)
        {
            double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }

        // ---------------------------------------------------------------- 折线化
        // 在矩阵 m 下（设备空间）展开为折线；tol 为设备像素容差。
        internal void Flatten(Mat2D m, double tol, PolyList outp)
        {
            outp.Clear();
            int k = 0;
            double px = 0, py = 0, spx = 0, spy = 0;   // 设备空间当前点 / 子路径起点
            double ux = 0, uy = 0, usx = 0, usy = 0;   // 用户空间当前点
            bool open = false;
            for (int ci = 0; ci < cmds.Count; ci++)
            {
                byte c = cmds[ci];
                if (c != 0 && c != 4 && !open)
                {
                    // Z 之后直接画线：从子路径起点开新子路径
                    outp.Begin(spx, spy); open = true;
                }
                switch (c)
                {
                    case 0:
                        ux = usx = pts[k]; uy = usy = pts[k + 1]; k += 2;
                        px = spx = m.X(ux, uy); py = spy = m.Y(ux, uy);
                        outp.Begin(px, py); open = true;
                        break;
                    case 1:
                        ux = pts[k]; uy = pts[k + 1]; k += 2;
                        px = m.X(ux, uy); py = m.Y(ux, uy);
                        outp.Add(px, py);
                        break;
                    case 2:
                        {
                            double x1 = m.X(pts[k], pts[k + 1]), y1 = m.Y(pts[k], pts[k + 1]);
                            double x2 = m.X(pts[k + 2], pts[k + 3]), y2 = m.Y(pts[k + 2], pts[k + 3]);
                            ux = pts[k + 4]; uy = pts[k + 5];
                            double x3 = m.X(ux, uy), y3 = m.Y(ux, uy);
                            k += 6;
                            double ddx = Math.Max(Math.Abs(px - 2 * x1 + x2), Math.Abs(x1 - 2 * x2 + x3));
                            double ddy = Math.Max(Math.Abs(py - 2 * y1 + y2), Math.Abs(y1 - 2 * y2 + y3));
                            double dd = Math.Sqrt(ddx * ddx + ddy * ddy);
                            int n = (int)Math.Ceiling(Math.Sqrt(0.75 * dd / tol));
                            if (n < 1) n = 1; else if (n > 128) n = 128;
                            for (int i = 1; i <= n; i++)
                            {
                                double t = (double)i / n, s = 1 - t;
                                double a0 = s * s * s, a1 = 3 * s * s * t, a2 = 3 * s * t * t, a3 = t * t * t;
                                outp.Add(a0 * px + a1 * x1 + a2 * x2 + a3 * x3, a0 * py + a1 * y1 + a2 * y2 + a3 * y3);
                            }
                            px = x3; py = y3;
                            break;
                        }
                    case 3:
                        {
                            double x1 = m.X(pts[k], pts[k + 1]), y1 = m.Y(pts[k], pts[k + 1]);
                            ux = pts[k + 2]; uy = pts[k + 3];
                            double x2 = m.X(ux, uy), y2 = m.Y(ux, uy);
                            k += 4;
                            double ddx = Math.Abs(px - 2 * x1 + x2), ddy = Math.Abs(py - 2 * y1 + y2);
                            double dd = Math.Sqrt(ddx * ddx + ddy * ddy);
                            int n = (int)Math.Ceiling(Math.Sqrt(0.25 * dd / tol));
                            if (n < 1) n = 1; else if (n > 128) n = 128;
                            for (int i = 1; i <= n; i++)
                            {
                                double t = (double)i / n, s = 1 - t;
                                outp.Add(s * s * px + 2 * s * t * x1 + t * t * x2, s * s * py + 2 * s * t * y1 + t * t * y2);
                            }
                            px = x2; py = y2;
                            break;
                        }
                    case 4:
                        if (open) { outp.Close(); open = false; }
                        px = spx; py = spy; ux = usx; uy = usy;
                        break;
                }
            }
            outp.End();
        }
        // 用户空间的几何包围盒（渐变 objectBoundingBox 用）
        internal void Bounds(out double x0, out double y0, out double x1, out double y1)
        {
            var pl = new PolyList();
            Flatten(Mat2D.Identity, 0.05, pl);
            x0 = y0 = double.MaxValue; x1 = y1 = double.MinValue;
            for (int i = 0; i < pl.n; i++) { double x = pl.xs[i], y = pl.ys[i]; if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x0 > x1) { x0 = y0 = x1 = y1 = 0; }
        }
    }

    // 折线集合（扁平数组，减少分配）：子路径 i 的点为 [start[i], start[i+1])
    internal sealed class PolyList
    {
        public double[] xs = new double[256], ys = new double[256];
        public int n;
        public readonly List<int> start = new List<int>();
        public readonly List<bool> closed = new List<bool>();
        public readonly List<bool> seg = new List<bool>();   // 子路径是否有线段（只有 moveTo 的子路径不描边）
        bool open;
        public void Clear() { n = 0; start.Clear(); closed.Clear(); seg.Clear(); open = false; }
        public void Begin(double x, double y)
        {
            if (open) End();
            start.Add(n); closed.Add(false); seg.Add(false); open = true;
            Pt(x, y);
        }
        public void Add(double x, double y) { Pt(x, y); seg[seg.Count - 1] = true; }
        void Pt(double x, double y)
        {
            if (n == xs.Length) { Array.Resize(ref xs, n * 2); Array.Resize(ref ys, n * 2); }
            xs[n] = x; ys[n] = y; n++;
        }
        public void Close() { if (open) { closed[closed.Count - 1] = true; seg[seg.Count - 1] = true; open = false; } }
        public void End() { open = false; }
        public int Count { get { return start.Count; } }
        public int From(int i) { return start[i]; }
        public int To(int i) { return i + 1 < start.Count ? start[i + 1] : n; }
    }

    // 抗锯齿蒙版：包围盒内的覆盖率，盒外为 0
    internal sealed class ClipMask
    {
        public int x0, y0, w, h;
        public float[] a;
        public float At(int x, int y)
        {
            x -= x0; y -= y0;
            if ((uint)x >= (uint)w || (uint)y >= (uint)h) return 0;
            return a[y * w + x];
        }
    }

    public sealed class Raster
    {
        public readonly int W, H;
        public readonly float[] Px;     // 预乘 RGBA，行优先，左上为原点

        const int SUB = 16;             // 每像素行的子扫描线数
        const float SUBW = 1f / SUB;
        const double TOL = 0.12;        // 折线化容差（设备像素）

        struct State
        {
            public Mat2D m;
            public ClipMask clip;
            public float alpha;
            public Paint fill, stroke;
            public double lineWidth, miterLimit;
            public LineJoin join;
            public LineCap cap;
            public double[] dash;
            public double dashOffset;
        }
        State st;
        readonly Stack<State> stack = new Stack<State>();
        Path2D cur = new Path2D();

        public Raster(int w, int h)
        {
            W = w; H = h;
            Px = new float[w * h * 4];
            st.m = Mat2D.Identity; st.alpha = 1;
            st.fill = Paint.Solid(new RGBA(0, 0, 0, 1)); st.stroke = Paint.Solid(new RGBA(0, 0, 0, 1));
            st.lineWidth = 1; st.miterLimit = 10; st.join = LineJoin.Miter; st.cap = LineCap.Butt;
        }

        // ---------------------------------------------------------------- 状态
        public void Save() { stack.Push(st); }
        public void Restore() { if (stack.Count > 0) st = stack.Pop(); }
        public Mat2D Transform { get { return st.m; } }
        public void SetTransform(double a, double b, double c, double d, double e, double f) { st.m = new Mat2D(a, b, c, d, e, f); }
        public void SetTransform(Mat2D m) { st.m = m; }
        public void ApplyTransform(double a, double b, double c, double d, double e, double f) { st.m = st.m.Mul(new Mat2D(a, b, c, d, e, f)); }
        public void Translate(double x, double y) { st.m = st.m.Mul(new Mat2D(1, 0, 0, 1, x, y)); }
        public void Scale(double x, double y) { st.m = st.m.Mul(new Mat2D(x, 0, 0, y, 0, 0)); }
        public void Rotate(double r) { double c = Math.Cos(r), s = Math.Sin(r); st.m = st.m.Mul(new Mat2D(c, s, -s, c, 0, 0)); }
        public float GlobalAlpha { get { return st.alpha; } set { if (value >= 0 && value <= 1) st.alpha = value; } }
        public Paint FillStyle { get { return st.fill; } set { st.fill = value; } }
        public Paint StrokeStyle { get { return st.stroke; } set { st.stroke = value; } }
        public string FillColor { set { st.fill = Paint.Solid(value); } }
        public string StrokeColor { set { st.stroke = Paint.Solid(value); } }
        public double LineWidth { get { return st.lineWidth; } set { if (value > 0 && !double.IsInfinity(value)) st.lineWidth = value; } }
        public double MiterLimit { get { return st.miterLimit; } set { if (value > 0) st.miterLimit = value; } }
        public LineJoin LineJoin { get { return st.join; } set { st.join = value; } }
        public LineCap LineCap { get { return st.cap; } set { st.cap = value; } }
        public void SetLineDash(double[] d)
        {
            if (d == null || d.Length == 0) { st.dash = null; return; }
            foreach (var v in d) if (v < 0 || double.IsNaN(v) || double.IsInfinity(v)) return;
            if (d.Length % 2 == 1) { var e = new double[d.Length * 2]; d.CopyTo(e, 0); d.CopyTo(e, d.Length); d = e; }
            double sum = 0; foreach (var v in d) sum += v;
            st.dash = sum > 0 ? (double[])d.Clone() : null;
        }
        public double LineDashOffset { get { return st.dashOffset; } set { st.dashOffset = value; } }

        // ---------------------------------------------------------------- 当前路径
        public void BeginPath() { cur = new Path2D(); }
        public void MoveTo(double x, double y) { cur.MoveTo(x, y); }
        public void LineTo(double x, double y) { cur.LineTo(x, y); }
        public void BezierCurveTo(double x1, double y1, double x2, double y2, double x, double y) { cur.BezierCurveTo(x1, y1, x2, y2, x, y); }
        public void QuadraticCurveTo(double x1, double y1, double x, double y) { cur.QuadraticCurveTo(x1, y1, x, y); }
        public void Arc(double x, double y, double r, double a0, double a1, bool ccw = false) { cur.Arc(x, y, r, a0, a1, ccw); }
        public void Ellipse(double x, double y, double rx, double ry, double rot, double a0, double a1, bool ccw = false) { cur.Ellipse(x, y, rx, ry, rot, a0, a1, ccw); }
        public void Rect(double x, double y, double w, double h) { cur.Rect(x, y, w, h); }
        public void RoundRect(double x, double y, double w, double h, double r) { cur.RoundRect(x, y, w, h, r); }
        public void ClosePath() { cur.ClosePath(); }
        public void Fill(FillRule rule = FillRule.NonZero) { Fill(cur, rule); }
        public void Stroke() { Stroke(cur); }
        public void Clip(FillRule rule = FillRule.NonZero) { Clip(cur, rule); }

        public void Clear() { Array.Clear(Px, 0, Px.Length); }
        public void ClearRect(double x, double y, double w, double h)
        {
            // 只支持与像素对齐的轴向矩形（头像只在单位变换下整幅清空）
            int x0 = Math.Max(0, (int)Math.Floor(st.m.X(x, y))), y0 = Math.Max(0, (int)Math.Floor(st.m.Y(x, y)));
            int x1 = Math.Min(W, (int)Math.Ceiling(st.m.X(x + w, y + h))), y1 = Math.Min(H, (int)Math.Ceiling(st.m.Y(x + w, y + h)));
            for (int yy = y0; yy < y1; yy++) Array.Clear(Px, (yy * W + x0) * 4, Math.Max(0, x1 - x0) * 4);
        }
        public void FillRect(double x, double y, double w, double h) { var p = new Path2D(); p.Rect(x, y, w, h); Fill(p); }

        // 另一张同尺寸（或更小）的栅格按当前 globalAlpha 叠到 (dx, dy)（整像素，不缩放；头像的背景 / 边框缓存用）
        public void DrawImage(Raster src, int dx = 0, int dy = 0)
        {
            float ga = st.alpha;
            for (int y = 0; y < src.H; y++)
            {
                int ty = y + dy;
                if (ty < 0 || ty >= H) continue;
                for (int x = 0; x < src.W; x++)
                {
                    int tx = x + dx;
                    if (tx < 0 || tx >= W) continue;
                    int si = (y * src.W + x) * 4, di = (ty * W + tx) * 4;
                    float m = st.clip != null ? st.clip.At(tx, ty) * ga : ga;
                    float sa = src.Px[si + 3] * m;
                    if (sa <= 0) continue;
                    float k = 1 - sa;
                    Px[di] = src.Px[si] * m + Px[di] * k;
                    Px[di + 1] = src.Px[si + 1] * m + Px[di + 1] * k;
                    Px[di + 2] = src.Px[si + 2] * m + Px[di + 2] * k;
                    Px[di + 3] = sa + Px[di + 3] * k;
                }
            }
        }

        // ---------------------------------------------------------------- 填充 / 描边 / 裁剪
        readonly PolyList poly = new PolyList();
        readonly EdgeSet edges = new EdgeSet();

        public void Fill(Path2D p, FillRule rule = FillRule.NonZero)
        {
            if (p == null || p.Empty) return;
            p.Flatten(st.m, TOL, poly);
            edges.Clear();
            for (int s = 0; s < poly.Count; s++) edges.AddPolygon(poly, poly.From(s), poly.To(s), 1);
            Composite(rule, PaintFor(st.fill, p), st.alpha);
        }
        public void Stroke(Path2D p)
        {
            if (p == null || p.Empty) return;
            double sc = st.m.LenScale;
            double w = st.lineWidth * sc;
            if (w <= 0) return;
            float alpha = st.alpha;
            // 细线：按 1 像素宽画、按宽度降低不透明度
            if (w < 1) { alpha *= (float)w; w = 1; }
            p.Flatten(st.m, TOL, poly);
            edges.Clear();
            var str = new Stroker(edges, w / 2, st.join, st.cap, st.miterLimit);
            for (int s = 0; s < poly.Count; s++)
            {
                int a = poly.From(s), b = poly.To(s);
                bool closed = poly.closed[s];
                if (!poly.seg[s]) continue;
                if (st.dash != null) Dash(poly, a, b, closed, sc, str);
                else str.Polyline(poly.xs, poly.ys, a, b, closed);
            }
            Composite(FillRule.NonZero, PaintFor(st.stroke, p), alpha);
        }
        public void Clip(Path2D p, FillRule rule = FillRule.NonZero)
        {
            p.Flatten(st.m, TOL, poly);
            edges.Clear();
            for (int s = 0; s < poly.Count; s++) edges.AddPolygon(poly, poly.From(s), poly.To(s), 1);
            int bx0, by0, bx1, by1;
            Bbox(out bx0, out by0, out bx1, out by1);
            var mk = new ClipMask { x0 = bx0, y0 = by0, w = Math.Max(0, bx1 - bx0), h = Math.Max(0, by1 - by0) };
            mk.a = new float[mk.w * mk.h];
            if (mk.w > 0 && mk.h > 0)
            {
                Scan(rule, bx0, by0, bx1, by1, (y, row) =>
                {
                    int o = (y - by0) * mk.w;
                    for (int x = 0; x < mk.w; x++)
                    {
                        float c = row[x];
                        if (c <= 0) continue;
                        if (c > 1) c = 1;
                        if (st.clip != null) c *= st.clip.At(x + bx0, y);
                        mk.a[o + x] = c;
                    }
                });
            }
            st.clip = mk;
        }

        // 渐变：objectBoundingBox 之外的渐变（设备空间直接给出）由调用方设好 toGrad
        Paint PaintFor(Paint p, Path2D path)
        {
            if (p.kind == 0 || !bboxUnits.Contains(p)) return p;
            double x0, y0, x1, y1;
            path.Bounds(out x0, out y0, out x1, out y1);
            var q = p.Clone();
            double bw = x1 - x0, bh = y1 - y0;
            if (bw <= 0 || bh <= 0) return Paint.Solid(new RGBA(0, 0, 0, 0));
            var g = st.m.Mul(new Mat2D(bw, 0, 0, bh, x0, y0));
            q.toGrad = g.Inverse();
            return q;
        }
        readonly HashSet<Paint> bboxUnits = new HashSet<Paint>();
        // 标记一个渐变为 objectBoundingBox 单位（SVG 渐变的默认值）
        public void UseBoundingBoxUnits(Paint p) { bboxUnits.Add(p); }

        void Dash(PolyList pl, int a, int b, bool closed, double sc, Stroker str)
        {
            var dash = st.dash;
            double total = 0; foreach (var v in dash) total += v * sc;
            if (total <= 0) { str.Polyline(pl.xs, pl.ys, a, b, closed); return; }
            // 起始相位
            double off = st.dashOffset * sc % total; if (off < 0) off += total;
            int di = 0; double rem = dash[0] * sc;
            while (off > 0) { if (off >= rem) { off -= rem; di = (di + 1) % dash.Length; rem = dash[di] * sc; } else { rem -= off; off = 0; } }
            bool on = di % 2 == 0;
            var dx = new List<double>(); var dy = new List<double>();
            int cnt = b - a + (closed ? 1 : 0);
            if (on) { dx.Add(pl.xs[a]); dy.Add(pl.ys[a]); }
            for (int i = 0; i < cnt - 1; i++)
            {
                int i0 = a + i, i1 = a + (i + 1) % (b - a);
                double x0 = pl.xs[i0], y0 = pl.ys[i0], x1 = pl.xs[i1], y1 = pl.ys[i1];
                double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)), pos = 0;
                while (len - pos > rem)
                {
                    pos += rem;
                    double t = pos / len, x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t;
                    if (on) { dx.Add(x); dy.Add(y); str.Polyline(dx.ToArray(), dy.ToArray(), 0, dx.Count, false); dx.Clear(); dy.Clear(); }
                    else { dx.Add(x); dy.Add(y); }
                    on = !on;
                    di = (di + 1) % dash.Length; rem = dash[di] * sc;
                }
                rem -= len - pos;
                if (on) { dx.Add(x1); dy.Add(y1); }
            }
            if (on && dx.Count > 0) str.Polyline(dx.ToArray(), dy.ToArray(), 0, dx.Count, false);
        }

        void Bbox(out int bx0, out int by0, out int bx1, out int by1)
        {
            if (edges.n == 0 || edges.minX > edges.maxX) { bx0 = by0 = bx1 = by1 = 0; return; }
            bx0 = Math.Max(0, (int)Math.Floor(edges.minX)); by0 = Math.Max(0, (int)Math.Floor(edges.minY));
            bx1 = Math.Min(W, (int)Math.Ceiling(edges.maxX) + 1); by1 = Math.Min(H, (int)Math.Ceiling(edges.maxY));
            if (st.clip != null)
            {
                bx0 = Math.Max(bx0, st.clip.x0); by0 = Math.Max(by0, st.clip.y0);
                bx1 = Math.Min(bx1, st.clip.x0 + st.clip.w); by1 = Math.Min(by1, st.clip.y0 + st.clip.h);
            }
        }

        void Composite(FillRule rule, Paint paint, float alpha)
        {
            if (edges.n == 0 || alpha <= 0) return;
            int bx0, by0, bx1, by1;
            Bbox(out bx0, out by0, out bx1, out by1);
            if (bx1 <= bx0 || by1 <= by0) return;
            bool solid = paint.kind == 0;
            RGBA col = paint.color;
            var clip = st.clip;
            Scan(rule, bx0, by0, bx1, by1, (y, row) =>
            {
                int bw = bx1 - bx0;
                for (int x = 0; x < bw; x++)
                {
                    float c = row[x];
                    if (c <= 0.0005f) continue;
                    if (c > 1) c = 1;
                    int px = x + bx0;
                    if (clip != null) { c *= clip.At(px, y); if (c <= 0) continue; }
                    RGBA s = solid ? col : paint.At(px + 0.5, y + 0.5);
                    float sa = s.a * c * alpha;
                    if (sa <= 0) continue;
                    int i = (y * W + px) * 4;
                    float k = 1 - sa;
                    Px[i] = s.r * sa + Px[i] * k;
                    Px[i + 1] = s.g * sa + Px[i + 1] * k;
                    Px[i + 2] = s.b * sa + Px[i + 2] * k;
                    Px[i + 3] = sa + Px[i + 3] * k;
                }
            });
        }

        // ---------------------------------------------------------------- 扫描转换
        float[] rowBuf = new float[64], diff = new float[66];
        float[] crossX = new float[64]; int[] crossD = new int[64];
        int[] active = new int[64];

        // 对 edges 在 [bx0,bx1)×[by0,by1) 内逐行计算覆盖率，回调 (y, row)，row[x - bx0]
        void Scan(FillRule rule, int bx0, int by0, int bx1, int by1, Action<int, float[]> sink)
        {
            int bw = bx1 - bx0;
            if (rowBuf.Length < bw) { rowBuf = new float[bw * 2]; diff = new float[bw * 2 + 2]; }
            edges.SortByY();
            int ne = edges.n, next = 0, na = 0;
            var E = edges;
            bool nz = rule == FillRule.NonZero;
            // 跳过 by0 以上就已结束的边
            for (int py = by0; py < by1; py++)
            {
                Array.Clear(diff, 0, bw + 2);
                bool any = false;
                for (int s = 0; s < SUB; s++)
                {
                    float sy = py + (s + 0.5f) * SUBW;
                    while (next < ne && E.y0[E.order[next]] <= sy)
                    {
                        if (na == active.Length) Array.Resize(ref active, na * 2);
                        active[na++] = E.order[next++];
                    }
                    int nc = 0;
                    for (int k = 0; k < na; k++)
                    {
                        int e = active[k];
                        if (E.y1[e] <= sy) { active[k] = active[--na]; k--; continue; }
                        if (nc == crossX.Length) { Array.Resize(ref crossX, nc * 2); Array.Resize(ref crossD, nc * 2); }
                        crossX[nc] = E.x0[e] + (sy - E.y0[e]) * E.dxdy[e];
                        crossD[nc] = E.dir[e];
                        nc++;
                    }
                    if (nc < 2) continue;
                    // 交点排序：少时插入排序，多时内省排序
                    if (nc <= 24)
                    {
                        for (int k = 1; k < nc; k++)
                        {
                            float x = crossX[k]; int dd = crossD[k], j = k;
                            while (j > 0 && crossX[j - 1] > x) { crossX[j] = crossX[j - 1]; crossD[j] = crossD[j - 1]; j--; }
                            crossX[j] = x; crossD[j] = dd;
                        }
                    }
                    else Array.Sort(crossX, crossD, 0, nc);
                    int wnd = 0;
                    float xa = 0;
                    for (int k = 0; k < nc; k++)
                    {
                        bool inPrev = nz ? wnd != 0 : (wnd & 1) != 0;
                        wnd += crossD[k];
                        bool inNow = nz ? wnd != 0 : (wnd & 1) != 0;
                        if (!inPrev && inNow) xa = crossX[k];
                        else if (inPrev && !inNow)
                        {
                            float xb = crossX[k];
                            float a = xa - bx0, b = xb - bx0;
                            if (a < 0) a = 0;
                            if (b > bw) b = bw;
                            if (b <= a) continue;
                            int ia = (int)a, ib = (int)b;
                            float fa = a - ia, fb = b - ib;
                            diff[ia] += SUBW * (1 - fa); diff[ia + 1] += SUBW * fa;
                            diff[ib] -= SUBW * (1 - fb); diff[ib + 1] -= SUBW * fb;
                            any = true;
                        }
                    }
                }
                if (!any) continue;
                float acc = 0;
                for (int x = 0; x < bw; x++) { acc += diff[x]; rowBuf[x] = acc; }
                sink(py, rowBuf);
            }
        }

        // ---------------------------------------------------------------- 输出
        // 非预乘 RGBA8，行优先、首行为图像顶部（PNG 次序）
        public byte[] ToRGBA8()
        {
            var o = new byte[W * H * 4];
            for (int i = 0; i < W * H; i++)
            {
                float a = Px[i * 4 + 3];
                if (a <= 0) continue;
                float k = a >= 1 ? 1 : 1 / a;
                o[i * 4] = B8(Px[i * 4] * k); o[i * 4 + 1] = B8(Px[i * 4 + 1] * k); o[i * 4 + 2] = B8(Px[i * 4 + 2] * k); o[i * 4 + 3] = B8(a);
            }
            return o;
        }
        static byte B8(float v) { int i = (int)(v * 255 + 0.5f); return (byte)(i < 0 ? 0 : i > 255 ? 255 : i); }

#if !SANGUO_HEADLESS
        // Unity 纹理的行序为自下而上
        public Color32[] ToColor32()
        {
            var o = new Color32[W * H];
            var b = ToRGBA8();
            for (int y = 0; y < H; y++)
            {
                int sy = H - 1 - y;
                for (int x = 0; x < W; x++)
                {
                    int i = (sy * W + x) * 4;
                    o[y * W + x] = new Color32(b[i], b[i + 1], b[i + 2], b[i + 3]);
                }
            }
            return o;
        }
        public Texture2D ToTexture(bool mipmaps = false)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, mipmaps);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixels32(ToColor32());
            t.Apply(mipmaps, false);
            return t;
        }
#endif
    }

    // 边表（设备坐标，y0 < y1；dir = ±1 为绕向）
    internal sealed class EdgeSet
    {
        public float[] x0 = new float[256], y0 = new float[256], y1 = new float[256], dxdy = new float[256];
        public int[] dir = new int[256], order = new int[256];
        float[] keys = new float[256];
        public int n;
        public double minX, minY, maxX, maxY;
        public void Clear() { n = 0; minX = minY = double.MaxValue; maxX = maxY = double.MinValue; }
        public void Add(double ax, double ay, double bx, double by, int d)
        {
            if (double.IsNaN(ax) || double.IsNaN(ay) || double.IsNaN(bx) || double.IsNaN(by)) return;
            if (ax < minX) minX = ax; if (ax > maxX) maxX = ax; if (bx < minX) minX = bx; if (bx > maxX) maxX = bx;
            if (ay < minY) minY = ay; if (ay > maxY) maxY = ay; if (by < minY) minY = by; if (by > maxY) maxY = by;
            if (ay == by) return;
            if (n == x0.Length)
            {
                int m = n * 2;
                Array.Resize(ref x0, m); Array.Resize(ref y0, m); Array.Resize(ref y1, m); Array.Resize(ref dxdy, m);
                Array.Resize(ref dir, m); Array.Resize(ref order, m); Array.Resize(ref keys, m);
            }
            if (ay > by) { double t = ax; ax = bx; bx = t; t = ay; ay = by; by = t; d = -d; }
            x0[n] = (float)ax; y0[n] = (float)ay; y1[n] = (float)by; dxdy[n] = (float)((bx - ax) / (by - ay)); dir[n] = d;
            n++;
        }
        // 折线（自动闭合）作为一个多边形加入；sign 为整体绕向系数
        public void AddPolygon(PolyList pl, int a, int b, int sign)
        {
            if (b - a < 2) return;
            for (int i = a; i < b; i++)
            {
                int j = i + 1 < b ? i + 1 : a;
                Add(pl.xs[i], pl.ys[i], pl.xs[j], pl.ys[j], sign);
            }
        }
        // 凸片（描边用）：按面积符号统一为正绕向，使并集在 nonzero 下不互相抵消
        public void AddPiece(double[] px, double[] py, int cnt)
        {
            if (cnt < 3) return;
            double area = 0;
            for (int i = 0; i < cnt; i++) { int j = (i + 1) % cnt; area += px[i] * py[j] - px[j] * py[i]; }
            if (Math.Abs(area) < 1e-9) return;
            int s = area > 0 ? 1 : -1;
            for (int i = 0; i < cnt; i++) { int j = (i + 1) % cnt; Add(px[i], py[i], px[j], py[j], s); }
        }
        public void SortByY()
        {
            for (int i = 0; i < n; i++) { order[i] = i; keys[i] = y0[i]; }
            Array.Sort(keys, order, 0, n);
        }
    }

    // 描边（设备空间）：每条子路径生成一条轮廓多边形（左侧偏移正向 + 末端端点 + 右侧偏移反向 + 起端端点；
    // 闭合子路径为内外两圈），外侧按连接方式补圆弧 / 尖角 / 斜角，内侧经由折点连接（同 Skia 的描边器）。
    // 所有轮廓绕向一致，按 nonzero 填充即为并集。
    internal sealed class Stroker
    {
        readonly EdgeSet E;
        readonly double hw, miterLimit, stepAng;
        readonly LineJoin join;
        readonly LineCap cap;
        double[] cx = new double[64], cy = new double[64], ux = new double[64], uy = new double[64];
        double[] ox = new double[256], oy = new double[256];
        int on;
        public Stroker(EdgeSet e, double halfWidth, LineJoin j, LineCap c, double ml)
        {
            E = e; hw = halfWidth; join = j; cap = c; miterLimit = ml;
            // 圆弧步长：弦高误差 ≤ 0.08 像素
            double k = 1 - 0.08 / Math.Max(hw, 0.1);
            stepAng = k <= -1 ? Math.PI / 2 : Math.Max(0.15, Math.Acos(Math.Max(-1, k)) * 2);
        }
        void Pt(double x, double y)
        {
            if (on == ox.Length) { Array.Resize(ref ox, on * 2); Array.Resize(ref oy, on * 2); }
            ox[on] = x; oy[on] = y; on++;
        }
        // 当前轮廓作为闭合多边形加入（sign = +1：与开放轮廓的绕向一致）
        void Flush(int sign)
        {
            for (int i = 0; i < on; i++) { int j = i + 1 < on ? i + 1 : 0; E.Add(ox[i], oy[i], ox[j], oy[j], sign); }
            on = 0;
        }
        // 以 (vx,vy) 为心、从向量 a 转到向量 b 的圆弧点（不含两端），转角 d（带符号）
        void Arc(double vx, double vy, double ax, double ay, double d)
        {
            int steps = (int)Math.Ceiling(Math.Abs(d) / stepAng);
            if (steps < 2) return;
            double a0 = Math.Atan2(ay, ax);
            for (int i = 1; i < steps; i++) { double a = a0 + d * i / steps; Pt(vx + Math.Cos(a) * hw, vy + Math.Sin(a) * hw); }
        }
        static double Wrap(double d) { while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; }
        // 折点 i 处一侧的连接：na / nb 为前后两段在该侧的偏移向量；outer 为外侧
        void Join(int i, double nax, double nay, double nbx, double nby, bool outer, double cross, double dot)
        {
            double vx = cx[i], vy = cy[i];
            Pt(vx + nax, vy + nay);
            if (Math.Abs(cross) < 1e-9 && dot > 0) return;               // 共线：不需要连接
            if (!outer) { Pt(vx, vy); Pt(vx + nbx, vy + nby); return; }  // 内侧：经由折点
            double turn = Math.Atan2(Math.Abs(cross), dot);
            if (join == LineJoin.Round) { if (turn >= 0.3) Arc(vx, vy, nax, nay, Wrap(Math.Atan2(nby, nbx) - Math.Atan2(nay, nax))); }
            else if (join == LineJoin.Miter)
            {
                double cosHalf = Math.Cos(turn / 2);
                if (cosHalf > 1e-6 && 1 / cosHalf <= miterLimit)
                {
                    double mx = nax + nbx, my = nay + nby, ml = Math.Sqrt(mx * mx + my * my);
                    if (ml > 1e-12) Pt(vx + mx / ml * hw / cosHalf, vy + my / ml * hw / cosHalf);
                }
            }
            Pt(vx + nbx, vy + nby);
        }
        // 端点：从 a 侧（点 p + a）绕到 -a 侧，经过方向 (dx,dy)
        void Cap(double px, double py, double ax, double ay, double dx, double dy)
        {
            if (cap == LineCap.Round)
            {
                // 从 a 转 ±π 到 -a，取经过 d 的那一边
                double a0 = Math.Atan2(ay, ax), md = Math.Atan2(dy, dx);
                double d = Wrap(md - a0) >= 0 ? Math.PI : -Math.PI;
                Arc(px, py, ax, ay, d);
            }
            else if (cap == LineCap.Square)
            {
                Pt(px + ax + dx * hw, py + ay + dy * hw);
                Pt(px - ax + dx * hw, py - ay + dy * hw);
            }
        }
        public void Polyline(double[] xs, double[] ys, int a, int b, bool closed)
        {
            // 去掉重合的相邻点
            int n = 0;
            if (cx.Length < b - a + 1) { int m = (b - a) * 2 + 2; cx = new double[m]; cy = new double[m]; ux = new double[m]; uy = new double[m]; }
            for (int i = a; i < b; i++)
            {
                if (n > 0 && Math.Abs(xs[i] - cx[n - 1]) < 1e-7 && Math.Abs(ys[i] - cy[n - 1]) < 1e-7) continue;
                cx[n] = xs[i]; cy[n] = ys[i]; n++;
            }
            if (closed && n > 1 && Math.Abs(cx[0] - cx[n - 1]) < 1e-7 && Math.Abs(cy[0] - cy[n - 1]) < 1e-7) n--;
            if (n == 0) return;
            if (n == 1)
            {
                // 零长子路径：圆 / 方端点时画点
                on = 0;
                if (cap == LineCap.Round) { Pt(cx[0] + hw, cy[0]); Arc(cx[0], cy[0], hw, 0, -2 * Math.PI); Flush(1); }
                else if (cap == LineCap.Square) { Pt(cx[0] - hw, cy[0] + hw); Pt(cx[0] + hw, cy[0] + hw); Pt(cx[0] + hw, cy[0] - hw); Pt(cx[0] - hw, cy[0] - hw); Flush(1); }
                return;
            }
            if (closed && n == 2) closed = false;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int j = (i + 1) % n;
                double dx = cx[j] - cx[i], dy = cy[j] - cy[i], l = Math.Sqrt(dx * dx + dy * dy);
                ux[i] = dx / l; uy[i] = dy / l;
            }
            on = 0;
            if (!closed)
            {
                // 左侧正向
                Pt(cx[0] - uy[0] * hw, cy[0] + ux[0] * hw);
                for (int i = 1; i < n - 1; i++) SideJoin(i, i - 1, i, 1);
                int e = n - 1, s = n - 2;
                double nx = -uy[s] * hw, ny = ux[s] * hw;
                Pt(cx[e] + nx, cy[e] + ny);
                Cap(cx[e], cy[e], nx, ny, ux[s], uy[s]);
                // 右侧反向
                Pt(cx[e] - nx, cy[e] - ny);
                for (int i = n - 2; i >= 1; i--) SideJoin(i, i, i - 1, -1);
                double sx = -uy[0] * hw, sy = ux[0] * hw;
                Pt(cx[0] - sx, cy[0] - sy);
                Cap(cx[0], cy[0], -sx, -sy, -ux[0], -uy[0]);
                Flush(1);
            }
            else
            {
                for (int i = 0; i < n; i++) SideJoin(i, (i - 1 + n) % n, i, 1);
                Flush(1);
                for (int i = n - 1; i >= 0; i--) SideJoin(i, i, (i - 1 + n) % n, -1);
                Flush(1);
            }
        }
        // 折点 i：沿行进方向从段 sa 转到段 sb；side = +1 左侧（正向遍历），-1 右侧（反向遍历）
        void SideJoin(int i, int sa, int sb, int side)
        {
            // 以正向的前后段判断内外：前段 = min 序的那段
            int pIn = side > 0 ? sa : sb, pOut = side > 0 ? sb : sa;
            double cross = ux[pIn] * uy[pOut] - uy[pIn] * ux[pOut], dot = ux[pIn] * ux[pOut] + uy[pIn] * uy[pOut];
            bool leftOuter = cross <= 0;
            bool outer = side > 0 ? leftOuter : !leftOuter;
            double nax = -uy[sa] * hw * side, nay = ux[sa] * hw * side, nbx = -uy[sb] * hw * side, nby = ux[sb] * hw * side;
            Join(i, nax, nay, nbx, nby, outer, cross, dot);
        }
    }
}
