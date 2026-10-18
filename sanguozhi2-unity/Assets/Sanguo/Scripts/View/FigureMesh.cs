// ==========================================================================
// 三国志-霸王的世界 · 人物 / 士兵 / 战马 / 兵器模型库 —— 几何（网页版 js/figure.js 的移植，DESIGN-V3 §1、§8）
//
// 本文件是纯 C#（不依赖 UnityEngine，可无头测试）：
//   · FigMath：与 JS 相同的小工具（clamp / lerp / sstep / gauss、哈希、mulberry32 随机数、颜色、双精度向量）
//   · V3d / Q4d / M4d：three.js 语义的双精度向量 / 四元数 / 4×4 矩阵（右手系；公式逐行照抄 three r158）
//   · FigGB：索引网格构建器（位置、图集 UV、顶点色 + 势力色遮罩、4 骨蒙皮），构建后算平滑法线并焊接同位顶点
//   · 放样 loft / tube / lathe / ellipsoid、双面布片 panel、刀刃 blade
//   · 人形骨架的关节位置与蒙皮权重、头部曲面、躯干与衣甲、冠帽 / 发 / 须、兵器与盾、战马
//   · 外观设定（名将 / 士兵）与模板（共用几何体，超预算时按 SQUEEZE 逐级压缩重建）
// 全部几何在 three 坐标中生成（与 JS 逐行一致，便于对照与测试）；交给 Unity 时由 FigConv 换手系：
//   位置 / 法线 (x, y, z) → (−x, y, z)，三角形绕序反转，四元数 (x, y, z, w) → (x, −y, −z, w)。
//   即 UNITY-V3 §1 的「z 取反 + 根节点绕 Y 转 180°」合成为一次 x 镜像：模型在 Unity 里朝 +Z、右手在 +X。
// 数据表（衣甲、冠帽、名将造型、兵器、士兵、LOD、动作关键帧……）由 tools/export-unity-figure.js 导出到 FigureData.g.cs。
// ==========================================================================
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sanguo
{
    // ------------------------------------------------------------------ 双精度向量（= JS 的 [x, y, z]）
    public struct V3d
    {
        public double x, y, z;
        public V3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public double this[int i]
        {
            get { return i == 0 ? x : i == 1 ? y : z; }
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }
        public static V3d operator +(V3d a, V3d b) { return new V3d(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static V3d operator -(V3d a, V3d b) { return new V3d(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static V3d operator -(V3d a) { return new V3d(-a.x, -a.y, -a.z); }
        public static V3d operator *(V3d a, double k) { return new V3d(a.x * k, a.y * k, a.z * k); }
        public double Length() { return Math.Sqrt(x * x + y * y + z * z); }
        public double LengthSq() { return x * x + y * y + z * z; }
        // three Vector3.normalize（长度为 0 时不变）
        public V3d Normalized() { double l = Math.Sqrt(x * x + y * y + z * z); if (l == 0) l = 1; return new V3d(x / l, y / l, z / l); }
        public double Dot(V3d b) { return x * b.x + y * b.y + z * b.z; }
        // three Vector3.applyQuaternion（r158 的展开式）
        public V3d ApplyQ(Q4d q)
        {
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w;
            double ix = qw * x + qy * z - qz * y, iy = qw * y + qz * x - qx * z, iz = qw * z + qx * y - qy * x, iw = -qx * x - qy * y - qz * z;
            return new V3d(ix * qw + iw * -qx + iy * -qz - iz * -qy, iy * qw + iw * -qy + iz * -qx - ix * -qz, iz * qw + iw * -qz + ix * -qy - iy * -qx);
        }
        public V3d ApplyM4(M4d m)
        {
            var e = m.e;
            double w = 1 / (e[3] * x + e[7] * y + e[11] * z + e[15]);
            return new V3d((e[0] * x + e[4] * y + e[8] * z + e[12]) * w, (e[1] * x + e[5] * y + e[9] * z + e[13]) * w, (e[2] * x + e[6] * y + e[10] * z + e[14]) * w);
        }
        public override string ToString() { return x.ToString("F4", CultureInfo.InvariantCulture) + "," + y.ToString("F4", CultureInfo.InvariantCulture) + "," + z.ToString("F4", CultureInfo.InvariantCulture); }
    }

    // ------------------------------------------------------------------ 四元数（three Quaternion 语义）
    public struct Q4d
    {
        public double x, y, z, w;
        public Q4d(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static readonly Q4d Identity = new Q4d(0, 0, 0, 1);
        public static Q4d AxisAngle(V3d a, double ang) { double h = ang / 2, s = Math.Sin(h); return new Q4d(a.x * s, a.y * s, a.z * s, Math.Cos(h)); }
        // a × b（three：a.multiply(b)）
        public static Q4d operator *(Q4d a, Q4d b)
        {
            return new Q4d(a.x * b.w + a.w * b.x + a.y * b.z - a.z * b.y, a.y * b.w + a.w * b.y + a.z * b.x - a.x * b.z,
                a.z * b.w + a.w * b.z + a.x * b.y - a.y * b.x, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }
        public Q4d Inverse() { return new Q4d(-x, -y, -z, w); }   // three invert = 共轭
        public Q4d Normalized()
        {
            double l = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (l == 0) return new Q4d(0, 0, 0, 1);
            l = 1 / l; return new Q4d(x * l, y * l, z * l, w * l);
        }
        // three Euler → Quaternion（rx, ry, rz 为弧度；order：XYZ YXZ ZXY ZYX YZX XZY）
        public static Q4d Euler(double ex, double ey, double ez, string order)
        {
            double c1 = Math.Cos(ex / 2), c2 = Math.Cos(ey / 2), c3 = Math.Cos(ez / 2), s1 = Math.Sin(ex / 2), s2 = Math.Sin(ey / 2), s3 = Math.Sin(ez / 2);
            switch (order)
            {
                case "XYZ": return new Q4d(s1 * c2 * c3 + c1 * s2 * s3, c1 * s2 * c3 - s1 * c2 * s3, c1 * c2 * s3 + s1 * s2 * c3, c1 * c2 * c3 - s1 * s2 * s3);
                case "YXZ": return new Q4d(s1 * c2 * c3 + c1 * s2 * s3, c1 * s2 * c3 - s1 * c2 * s3, c1 * c2 * s3 - s1 * s2 * c3, c1 * c2 * c3 + s1 * s2 * s3);
                case "ZXY": return new Q4d(s1 * c2 * c3 - c1 * s2 * s3, c1 * s2 * c3 + s1 * c2 * s3, c1 * c2 * s3 + s1 * s2 * c3, c1 * c2 * c3 - s1 * s2 * s3);
                case "ZYX": return new Q4d(s1 * c2 * c3 - c1 * s2 * s3, c1 * s2 * c3 + s1 * c2 * s3, c1 * c2 * s3 - s1 * s2 * c3, c1 * c2 * c3 + s1 * s2 * s3);
                case "YZX": return new Q4d(s1 * c2 * c3 + c1 * s2 * s3, c1 * s2 * c3 + s1 * c2 * s3, c1 * c2 * s3 - s1 * s2 * c3, c1 * c2 * c3 - s1 * s2 * s3);
                case "XZY": return new Q4d(s1 * c2 * c3 - c1 * s2 * s3, c1 * s2 * c3 - s1 * c2 * s3, c1 * c2 * s3 + s1 * s2 * c3, c1 * c2 * c3 + s1 * s2 * s3);
            }
            return Identity;
        }
        // three setFromRotationMatrix（只用左上 3×3）
        public static Q4d FromMatrix(M4d m)
        {
            var te = m.e;
            double m11 = te[0], m12 = te[4], m13 = te[8], m21 = te[1], m22 = te[5], m23 = te[9], m31 = te[2], m32 = te[6], m33 = te[10], tr = m11 + m22 + m33;
            if (tr > 0) { double s = 0.5 / Math.Sqrt(tr + 1.0); return new Q4d((m32 - m23) * s, (m13 - m31) * s, (m21 - m12) * s, 0.25 / s); }
            if (m11 > m22 && m11 > m33) { double s = 2.0 * Math.Sqrt(1.0 + m11 - m22 - m33); return new Q4d(0.25 * s, (m12 + m21) / s, (m13 + m31) / s, (m32 - m23) / s); }
            if (m22 > m33) { double s = 2.0 * Math.Sqrt(1.0 + m22 - m11 - m33); return new Q4d((m12 + m21) / s, 0.25 * s, (m23 + m32) / s, (m13 - m31) / s); }
            { double s = 2.0 * Math.Sqrt(1.0 + m33 - m11 - m22); return new Q4d((m13 + m31) / s, (m23 + m32) / s, 0.25 * s, (m21 - m12) / s); }
        }
        // three Quaternion.slerp（this → qb）
        public Q4d Slerp(Q4d qb, double t)
        {
            if (t == 0) return this;
            if (t == 1) return qb;
            double X = x, Y = y, Z = z, W = w;
            double cosH = W * qb.w + X * qb.x + Y * qb.y + Z * qb.z;
            Q4d r;
            if (cosH < 0) { r = new Q4d(-qb.x, -qb.y, -qb.z, -qb.w); cosH = -cosH; }
            else r = qb;
            if (cosH >= 1.0) return new Q4d(X, Y, Z, W);
            double sqrSin = 1.0 - cosH * cosH;
            if (sqrSin <= 2.220446049250313e-16)
            {
                double s = 1 - t;
                return new Q4d(s * X + t * r.x, s * Y + t * r.y, s * Z + t * r.z, s * W + t * r.w).Normalized();
            }
            double sinH = Math.Sqrt(sqrSin), halfT = Math.Atan2(sinH, cosH);
            double ra = Math.Sin((1 - t) * halfT) / sinH, rb = Math.Sin(t * halfT) / sinH;
            return new Q4d(X * ra + r.x * rb, Y * ra + r.y * rb, Z * ra + r.z * rb, W * ra + r.w * rb);
        }
        // three Euler.setFromQuaternion(q, 'YXZ') → (x, y, z) 弧度
        public V3d ToEulerYXZ()
        {
            var m = M4d.Compose(new V3d(0, 0, 0), this, new V3d(1, 1, 1)).e;
            double m11 = m[0], m13 = m[8], m21 = m[1], m22 = m[5], m23 = m[9], m31 = m[2], m33 = m[10];
            double ex = Math.Asin(-Math.Max(-1, Math.Min(1, m23))), ey, ez;
            if (Math.Abs(m23) < 0.9999999) { ey = Math.Atan2(m13, m33); ez = Math.Atan2(m21, m22); }
            else { ey = Math.Atan2(-m31, m11); ez = 0; }
            return new V3d(ex, ey, ez);
        }
    }

    // ------------------------------------------------------------------ 4×4 矩阵（three Matrix4：列主序 elements）
    public sealed class M4d
    {
        public readonly double[] e = new double[16];
        public M4d() { e[0] = e[5] = e[10] = e[15] = 1; }
        public M4d Copy() { var m = new M4d(); Array.Copy(e, m.e, 16); return m; }
        public static M4d Translation(double x, double y, double z) { var m = new M4d(); m.e[12] = x; m.e[13] = y; m.e[14] = z; return m; }
        // 列 = X, Y, Z
        public static M4d Basis(V3d X, V3d Y, V3d Z)
        {
            var m = new M4d(); var t = m.e;
            t[0] = X.x; t[1] = X.y; t[2] = X.z; t[4] = Y.x; t[5] = Y.y; t[6] = Y.z; t[8] = Z.x; t[9] = Z.y; t[10] = Z.z;
            return m;
        }
        public static M4d Compose(V3d p, Q4d q, V3d s)
        {
            var m = new M4d(); var te = m.e;
            double x = q.x, y = q.y, z = q.z, w = q.w, x2 = x + x, y2 = y + y, z2 = z + z;
            double xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
            te[0] = (1 - (yy + zz)) * s.x; te[1] = (xy + wz) * s.x; te[2] = (xz - wy) * s.x; te[3] = 0;
            te[4] = (xy - wz) * s.y; te[5] = (1 - (xx + zz)) * s.y; te[6] = (yz + wx) * s.y; te[7] = 0;
            te[8] = (xz + wy) * s.z; te[9] = (yz - wx) * s.z; te[10] = (1 - (xx + yy)) * s.z; te[11] = 0;
            te[12] = p.x; te[13] = p.y; te[14] = p.z; te[15] = 1;
            return m;
        }
        // a × b
        public static M4d Mul(M4d a, M4d b)
        {
            var r = new M4d(); var ae = a.e; var be = b.e; var te = r.e;
            for (int c = 0; c < 4; c++) for (int row = 0; row < 4; row++)
                te[c * 4 + row] = ae[row] * be[c * 4] + ae[4 + row] * be[c * 4 + 1] + ae[8 + row] * be[c * 4 + 2] + ae[12 + row] * be[c * 4 + 3];
            return r;
        }
        public V3d Pos { get { return new V3d(e[12], e[13], e[14]); } }
    }

    // ------------------------------------------------------------------ 颜色（0..1；m = 势力色遮罩，= JS 数组的第 4 分量）
    public struct FCol
    {
        public double r, g, b, m;
        public FCol(double r, double g, double b, double m = 0) { this.r = r; this.g = g; this.b = b; this.m = m; }
        public static bool operator ==(FCol a, FCol b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.m == b.m; }
        public static bool operator !=(FCol a, FCol b) { return !(a == b); }
        public override bool Equals(object o) { return o is FCol && (FCol)o == this; }
        public override int GetHashCode() { return r.GetHashCode() ^ (g.GetHashCode() * 3) ^ (b.GetHashCode() * 7); }
    }
    // 蒙皮权重的一项（骨骼编号, 权重）
    public struct FBW
    {
        public int b; public double w;
        public FBW(int b, double w) { this.b = b; this.w = w; }
    }

    // ------------------------------------------------------------------ 工具（与 JS 同名同义）
    public static class FigMath
    {
        public const double PI = Math.PI, TAU = Math.PI * 2, D2R = Math.PI / 180;
        public static double clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        public static double clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        public static double lerp(double a, double b, double t) { return a + (b - a) * t; }
        public static double sstep(double e0, double e1, double x) { double t = clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        public static double gauss(double x, double m, double s) { double d = (x - m) / s; return Math.Exp(-d * d); }
        public static double smooth(double t) { return t * t * (3 - 2 * t); }
        public static double hypot(double a, double b) { return Math.Sqrt(a * a + b * b); }
        public static double hypot(double a, double b, double c) { return Math.Sqrt(a * a + b * b + c * c); }
        public static double sign(double v) { return v > 0 ? 1 : v < 0 ? -1 : 0; }
        // JS Math.round（.5 向 +∞）
        public static double jround(double v) { return Math.Floor(v + 0.5); }
        public static double superE(double s, double e) { return sign(s) * Math.Pow(Math.Abs(s), 2 / e); }

        // 向量（JS 的 v3add 等）
        public static V3d V(double x, double y, double z) { return new V3d(x, y, z); }
        public static V3d v3add(V3d a, V3d b) { return new V3d(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static V3d v3sub(V3d a, V3d b) { return new V3d(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static V3d v3mul(V3d a, double k) { return new V3d(a.x * k, a.y * k, a.z * k); }
        public static double v3dot(V3d a, V3d b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static V3d v3cross(V3d a, V3d b) { return new V3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static double v3len(V3d a) { return Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z); }
        public static V3d v3norm(V3d a) { double l = v3len(a); if (l == 0) l = 1; return new V3d(a.x / l, a.y / l, a.z / l); }
        public static V3d v3lerp(V3d a, V3d b, double t) { return new V3d(lerp(a.x, b.x, t), lerp(a.y, b.y, t), lerp(a.z, b.z, t)); }
        public static V3d v3madd(V3d a, V3d b, double k) { return new V3d(a.x + b.x * k, a.y + b.y * k, a.z + b.z * k); }
        public static V3d v3rot(V3d v, V3d axis, double ang)
        {
            double c = Math.Cos(ang), s = Math.Sin(ang), k = v3dot(axis, v); var cr = v3cross(axis, v);
            return new V3d(v.x * c + cr.x * s + axis.x * k * (1 - c), v.y * c + cr.y * s + axis.y * k * (1 - c), v.z * c + cr.z * s + axis.z * k * (1 - c));
        }

        // FNV-1a + murmur 收尾（按 UTF-16 码元，= JS hashStr）
        public static uint hashStr(string s)
        {
            uint h = 2166136261u;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h = unchecked(h * 16777619u); }
            h ^= h >> 16; h = unchecked(h * 0x85ebca6bu); h ^= h >> 13; h = unchecked(h * 0xc2b2ae35u); h ^= h >> 16;
            return h;
        }
        // 值噪声（确定性，= JS h2 / vnoise / fbm）
        public static double h2(int i, int j, int seed)
        {
            int h = unchecked(i * 374761393 + j * 668265263 + seed * 1442695041);
            h = unchecked((h ^ (int)((uint)h >> 13)) * 1274126177);
            return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
        }
        public static double vnoise(double x, double y, int seed)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int xi = (int)fx, yi = (int)fy; double xf = x - fx, yf = y - fy;
            double u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            double a = h2(xi, yi, seed), b = h2(xi + 1, yi, seed), c = h2(xi, yi + 1, seed), d = h2(xi + 1, yi + 1, seed);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }
        public static double fbm(double x, double y, int oct, int seed)
        {
            double s = 0, a = 0.5, f = 1, n = 0;
            for (int i = 0; i < oct; i++) { s += a * vnoise(x * f, y * f, seed + i * 17); n += a; a *= 0.5; f *= 2.03; }
            return s / n;
        }

        // ---- 颜色：'#rrggbb' → FCol（Gamma 工作流下直接作线性值使用）
        static readonly Dictionary<string, FCol> colCache = new Dictionary<string, FCol>();
        public static FCol col(string c)
        {
            if (c == null) return new FCol(1, 1, 1);
            FCol v;
            lock (colCache) if (colCache.TryGetValue(c, out v)) return v;
            string h = c.Replace("#", "");
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            int n = 0;
            if (h.Length >= 6) int.TryParse(h.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n);
            else int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n);
            v = new FCol(((n >> 16) & 255) / 255.0, ((n >> 8) & 255) / 255.0, (n & 255) / 255.0);
            lock (colCache) colCache[c] = v;
            return v;
        }
        public static FCol col(FCol c) { return c; }
        public static FCol col(double r, double g, double b) { return new FCol(r, g, b); }
        // 第 4 分量 = 势力色遮罩（取两者较大者）
        public static FCol mix(FCol a, FCol b, double t) { return new FCol(lerp(a.r, b.r, t), lerp(a.g, b.g, t), lerp(a.b, b.b, t), Math.Max(a.m, b.m)); }
        public static FCol mix(string a, string b, double t) { return mix(col(a), col(b), t); }
        public static FCol mix(FCol a, string b, double t) { return mix(a, col(b), t); }
        public static FCol mix(string a, FCol b, double t) { return mix(col(a), b, t); }
        // k > 0 提亮（向白）、k < 0 压暗
        public static FCol shade(FCol c, double k)
        {
            if (k >= 0) return mix(c, new FCol(1, 1, 1), k);
            return new FCol(c.r * (1 + k), c.g * (1 + k), c.b * (1 + k), c.m);
        }
        public static FCol shade(string c, double k) { return shade(col(c), k); }
        // 势力色占位：顶点色为白、遮罩为 1，渲染时乘以材质的势力色
        public static readonly FCol TEAM = new FCol(1, 1, 1, 1);
        public static readonly FCol WHITE1 = new FCol(1, 1, 1);
        public static string hex(FCol c)
        {
            Func<double, string> h = v => ((int)jround(clamp01(v) * 255)).ToString("x2");
            return "#" + h(c.r) + h(c.g) + h(c.b);
        }
        public static string hex(string c) { return hex(col(c)); }
        public static double lum(FCol c) { return c.r * 0.3 + c.g * 0.59 + c.b * 0.11; }
        public static double lum(string c) { return lum(col(c)); }

        // 常用金属色 / 布色
        public const string STEEL = "#9aa2ae", DSTEEL = "#5c626e", IRON = "#4a4e58", GOLD = "#e0b050", BRONZE = "#b08850", SILVER = "#d4d8e0", BLACKM = "#2c2e36";
        public const string LEATHER = "#6a4a30", DLEATHER = "#3e2c20", WOOD = "#7a5634", DWOOD = "#4a3020", RED = "#b02a24", WHITEC = "#ece8e0", BLACK = "#1c1a1e";
        public const string GOLDTHREAD = "#e4bc5c";

        public static double Now() { return System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
    }

    // ------------------------------------------------------------------ JS Rng（mulberry32；与 figure.js 的 Rng 逐位相同）
    public sealed class FigRng
    {
        int a;
        public FigRng(uint seed) { a = (int)seed; if (a == 0) a = 1; }
        public double f()
        {
            unchecked
            {
                a = a + 0x6D2B79F5;
                int t = (a ^ (int)((uint)a >> 15)) * (1 | a);
                t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
                return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
            }
        }
        public double range(double a0, double b0) { return a0 + (b0 - a0) * f(); }
        public int @int(int a0, int b0) { return a0 + (int)Math.Floor(f() * (b0 - a0 + 1)); }
        public T pick<T>(IList<T> arr) { return arr[(int)Math.Floor(f() * arr.Count) % arr.Count]; }
        public object pick(List<object> arr) { return arr[(int)Math.Floor(f() * arr.Count) % arr.Count]; }
        public bool chance(double p) { return f() < p; }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    // ------------------------------------------------------------------ 构建好的几何体（three 坐标，三角形为 three 绕序）
    public sealed class FigGeo
    {
        public float[] pos, nrm, uv, col;       // 每顶点 3 / 3 / 2 / 4（col 第 4 分量 = 势力色遮罩）
        public ushort[] si; public float[] sw;  // 每顶点 4 骨（skin = false 时为 null）
        public int[] idx;
        public V3d bmin, bmax;
        public int Verts { get { return pos.Length / 3; } }
        public int Tris { get { return idx.Length / 3; } }
    }

    // ------------------------------------------------------------------ 索引网格构建器（= JS class GB）
    // 权重 w：null = 0 号骨；否则为若干 (骨, 权重)，取权重最大的 4 个并归一化。
    public sealed class FigGB
    {
        public readonly List<double> P = new List<double>(4096), UV = new List<double>(2048), C = new List<double>(4096), SW = new List<double>(4096);
        public readonly List<int> SI = new List<int>(4096), I = new List<int>(8192), G = new List<int>(1024);
        public int grp, nv;
        public M4d xf;                 // 当前变换（仅作用于位置）
        public bool noEdges;
        public int detail = -1;        // JS gb.detail（未设定 = undefined）
        public int group() { return ++grp; }
        public int v(V3d p, double[] uv, FCol? c, FBW[] w, int g)
        {
            double x = p.x, y = p.y, z = p.z;
            if (xf != null)
            {
                var e = xf.e;
                double X = e[0] * x + e[4] * y + e[8] * z + e[12], Y = e[1] * x + e[5] * y + e[9] * z + e[13], Z = e[2] * x + e[6] * y + e[10] * z + e[14];
                x = X; y = Y; z = Z;
            }
            P.Add(x); P.Add(y); P.Add(z);
            UV.Add(uv[0]); UV.Add(uv[1]);
            var cc = c ?? new FCol(1, 1, 1);
            C.Add(cc.r); C.Add(cc.g); C.Add(cc.b); C.Add(cc.m);
            if (w == null || w.Length == 0) { SI.Add(0); SI.Add(0); SI.Add(0); SI.Add(0); SW.Add(1); SW.Add(0); SW.Add(0); SW.Add(0); }
            else
            {
                FBW[] ws = w;
                if (w.Length > 4)
                {
                    // 稳定排序（与 JS Array.sort 一致：权重相同时保持原顺序）取前 4
                    ws = (FBW[])w.Clone();
                    for (int i = 1; i < ws.Length; i++) { var t = ws[i]; int j = i - 1; while (j >= 0 && ws[j].w < t.w) { ws[j + 1] = ws[j]; j--; } ws[j + 1] = t; }
                    Array.Resize(ref ws, 4);
                }
                double s = 0; foreach (var e in ws) s += e.w;
                if (s == 0 || double.IsNaN(s)) s = 1;
                for (int i = 0; i < 4; i++) { if (i < ws.Length) { SI.Add(ws[i].b); SW.Add(ws[i].w / s); } else { SI.Add(0); SW.Add(0); } }
            }
            G.Add(g);
            return nv++;
        }
        public void tri(int a, int b, int c) { I.Add(a); I.Add(b); I.Add(c); }
        public void quad(int a, int b, int c, int d) { I.Add(a); I.Add(b); I.Add(c); I.Add(a); I.Add(c); I.Add(d); }
        public int tris { get { return I.Count / 3; } }
        public V3d Pt(int i) { return new V3d(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]); }
        public double[] UVof(int i) { return new[] { UV[i * 2], UV[i * 2 + 1] }; }
        public FCol Cof(int i) { return new FCol(C[i * 4], C[i * 4 + 1], C[i * 4 + 2], C[i * 4 + 3]); }

        // 构建：平滑法线（three computeVertexNormals：未归一化的面法线累加）+ 同一平滑组内同位顶点的法线焊接
        public FigGeo build(bool skin = true)
        {
            var g = new FigGeo();
            int n = nv;
            g.pos = new float[n * 3]; for (int i = 0; i < n * 3; i++) g.pos[i] = (float)P[i];
            g.uv = new float[n * 2]; for (int i = 0; i < n * 2; i++) g.uv[i] = (float)UV[i];
            g.col = new float[n * 4]; for (int i = 0; i < n * 4; i++) g.col[i] = (float)C[i];
            if (skin)
            {
                g.si = new ushort[n * 4]; g.sw = new float[n * 4];
                for (int i = 0; i < n * 4; i++) { g.si[i] = (ushort)SI[i]; g.sw[i] = (float)SW[i]; }
            }
            g.idx = I.ToArray();
            var pos = g.pos; var nrm = new float[n * 3];
            for (int f = 0; f < g.idx.Length; f += 3)
            {
                int a = g.idx[f], b = g.idx[f + 1], c = g.idx[f + 2];
                double ax = pos[a * 3], ay = pos[a * 3 + 1], az = pos[a * 3 + 2], bx = pos[b * 3], by = pos[b * 3 + 1], bz = pos[b * 3 + 2], cx = pos[c * 3], cy = pos[c * 3 + 1], cz = pos[c * 3 + 2];
                double cbx = cx - bx, cby = cy - by, cbz = cz - bz, abx = ax - bx, aby = ay - by, abz = az - bz;
                double nx = cby * abz - cbz * aby, ny = cbz * abx - cbx * abz, nz = cbx * aby - cby * abx;
                nrm[a * 3] = (float)(nrm[a * 3] + nx); nrm[a * 3 + 1] = (float)(nrm[a * 3 + 1] + ny); nrm[a * 3 + 2] = (float)(nrm[a * 3 + 2] + nz);
                nrm[b * 3] = (float)(nrm[b * 3] + nx); nrm[b * 3 + 1] = (float)(nrm[b * 3 + 1] + ny); nrm[b * 3 + 2] = (float)(nrm[b * 3 + 2] + nz);
                nrm[c * 3] = (float)(nrm[c * 3] + nx); nrm[c * 3 + 1] = (float)(nrm[c * 3 + 1] + ny); nrm[c * 3 + 2] = (float)(nrm[c * 3 + 2] + nz);
            }
            for (int i = 0; i < n; i++)
            {
                double x = nrm[i * 3], y = nrm[i * 3 + 1], z = nrm[i * 3 + 2], l = Math.Sqrt(x * x + y * y + z * z);
                if (l == 0) l = 1;
                nrm[i * 3] = (float)(x / l); nrm[i * 3 + 1] = (float)(y / l); nrm[i * 3 + 2] = (float)(z / l);
            }
            // 焊接
            const double q = 1e4;
            var map = new Dictionary<string, List<int>>();
            var order = new List<List<int>>();
            for (int i = 0; i < n; i++)
            {
                int gr = G[i];
                if (gr == 0) continue;
                string k = gr + ":" + jround(pos[i * 3] * q) + "," + jround(pos[i * 3 + 1] * q) + "," + jround(pos[i * 3 + 2] * q);
                List<int> l;
                if (!map.TryGetValue(k, out l)) { map[k] = l = new List<int>(2); order.Add(l); }
                l.Add(i);
            }
            foreach (var l in order)
            {
                if (l.Count < 2) continue;
                double x = 0, y = 0, z = 0;
                foreach (int i in l) { x += nrm[i * 3]; y += nrm[i * 3 + 1]; z += nrm[i * 3 + 2]; }
                double nn = Math.Sqrt(x * x + y * y + z * z); if (nn == 0) nn = 1;
                foreach (int i in l) { nrm[i * 3] = (float)(x / nn); nrm[i * 3 + 1] = (float)(y / nn); nrm[i * 3 + 2] = (float)(z / nn); }
            }
            g.nrm = nrm;
            // 包围盒
            double mx = double.MaxValue;
            V3d mn = new V3d(mx, mx, mx), mxv = new V3d(-mx, -mx, -mx);
            for (int i = 0; i < n; i++)
            {
                double x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
                if (x < mn.x) mn.x = x; if (y < mn.y) mn.y = y; if (z < mn.z) mn.z = z;
                if (x > mxv.x) mxv.x = x; if (y > mxv.y) mxv.y = y; if (z > mxv.z) mxv.z = z;
            }
            g.bmin = mn; g.bmax = mxv;
            return g;
        }
    }

    // ------------------------------------------------------------------ 放样截面
    // { c 中心, X 截面局部 x 轴, Z 截面局部 z 轴（θ = 0 的方向）, rx, rz, sq 超椭圆指数, t 纵向贴图坐标, w 权重, col, pole 收为一点 }
    public sealed class FigSec
    {
        public V3d c, X = new V3d(1, 0, 0), Z = new V3d(0, 0, 1);
        public double rx, rz, sq;           // sq = 0：未设定
        public double t = double.NaN;       // NaN：按序号
        public FBW[] w; public bool hasW;   // hasW：JS 的 s.w !== undefined
        public FCol? col;
        public bool pole;
        public Func<double, FigSec, int, double> off;
        public double kk, off2;             // headShell / bandRing 的附加参数
        public void SetW(FBW[] v) { w = v; hasW = true; }
    }
    // 图集区域内的三个轴（lathe / ellipsoid 的朝向）
    public struct FigAxes
    {
        public V3d X, Y, Z;
        public FigAxes(V3d x, V3d y, V3d z) { X = x; Y = y; Z = z; }
        public static readonly FigAxes Std = new FigAxes(new V3d(1, 0, 0), new V3d(0, 1, 0), new V3d(0, 0, 1));
    }

    // ------------------------------------------------------------------ 构建选项（JS 各函数的 o 对象：所有可能的字段）
    public sealed class FigOpt
    {
        // loft
        public int n; public string reg; public double[] uvc; public int group;
        public double[] arc; public bool mirror = true;
        public double u0 = double.NaN, u1 = double.NaN;
        public FCol? col; public Func<double, FigSec, int, FCol> colFn;
        public bool flip, capStart, capEnd;
        public Func<V3d, double, int, FBW[]> wf;
        public FBW[] w;
        public Func<double, FigSec, int, double> off;
        public Func<double, FigSec, int, V3d> ptf;
        public double sq;
        // tube
        public double r; public Func<double, int, double> rF; public double[] rArr;
        public double rz = double.NaN; public Func<double, int, double> rzF;
        public double twist; public V3d? up; public Func<double, double> tF; public Func<double, FCol> colS;
        public Func<double, V3d, FBW[]> wT;
        // lathe / ellipsoid
        public V3d? c; public FigAxes? axes; public double sx = 1, sz = 1; public int nl;
        // panel
        public int nu, nv; public double th; public FCol? colB; public Func<double, double, FCol> colPF; public FCol? colE;
        public bool mirrorS; public Func<double, double, double[]> uv; public bool edge = true;
        public Func<V3d, double, double, FBW[]> wP;
        // blade
        public bool capBase = true;
        public FigOpt Clone() { return (FigOpt)MemberwiseClone(); }
    }

    // 刀刃站点：{ p 中心点, e 刃口方向（单位）, n 厚度方向（单位）, we 向刃口的宽度, wb 向刃背的宽度, th 半厚, dbl 双刃 }
    public sealed class FigBladeSt
    {
        public V3d p, e, n; public double we, wb, th; public bool dbl;
    }

    // 图集区域（像素矩形 + UV 范围）
    public sealed class FigRegion
    {
        public string name; public int px, py, pw, ph; public double u0, u1, vb, vt;
    }

    // ------------------------------------------------------------------ 网格基元（= JS loft / tube / lathe / ellipsoid / panel / blade）
    public static partial class FigBuild
    {
        public static double[] ruv(FigRegion r, double s, double t) { return new[] { r.u0 + clamp01(s) * (r.u1 - r.u0), r.vb + clamp01(t) * (r.vt - r.vb) }; }

        // 放样：截面序列 → 环带（三角形绕序同 JS）
        public static List<int[]> loft(FigGB gb, List<FigSec> secs, FigOpt o)
        {
            int n = o.n;
            FigRegion reg = o.uvc != null ? null : FigAtlasLayout.REG(o.reg ?? "white");
            int grp = o.group != 0 ? o.group : gb.group();
            var arc = o.arc;
            var ring = new List<int[]>();
            int cnt = arc != null ? n + 1 : n;
            for (int k = 0; k < secs.Count; k++)
            {
                var s = secs[k];
                var row = new int[cnt];
                double t = !double.IsNaN(s.t) ? s.t : (double)k / (secs.Count - 1);
                if (s.pole)
                {
                    var p = s.c;
                    var uv = o.uvc ?? ruv(reg, !double.IsNaN(o.u0) ? (o.u0 + o.u1) / 2 : 0.5, t);
                    var w = o.wf != null ? o.wf(p, 0, k) : (s.hasW ? s.w : o.w);
                    var c = o.colFn != null ? o.colFn(0, s, k) : (s.col ?? o.col);
                    int id = gb.v(p, uv, c, w, grp);
                    for (int i = 0; i < cnt; i++) row[i] = id;
                    ring.Add(row);
                    continue;
                }
                double sq = s.sq != 0 ? s.sq : o.sq != 0 ? o.sq : 2;
                for (int i = 0; i < cnt; i++)
                {
                    double th = arc != null ? lerp(arc[0], arc[1], (double)i / n) : -PI + ((double)i / n) * TAU;
                    double sn = Math.Sin(th), cs = Math.Cos(th);
                    double ex = sq == 2 ? sn : superE(sn, sq), ez = sq == 2 ? cs : superE(cs, sq);
                    double rx = s.rx, rz = s.rz;
                    double ox = ex * rx, oz = ez * rz;
                    if (s.off != null || o.off != null)
                    {
                        double d = (s.off != null ? s.off(th, s, k) : 0) + (o.off != null ? o.off(th, s, k) : 0);
                        // 近似法线方向
                        double nx = sn / (rx != 0 ? rx : 1e-4), nz = cs / (rz != 0 ? rz : 1e-4), nl = Math.Sqrt(nx * nx + nz * nz);
                        if (nl == 0) nl = 1;
                        ox += nx / nl * d; oz += nz / nl * d;
                    }
                    V3d p = o.ptf != null ? o.ptf(th, s, k) : new V3d(s.c.x + s.X.x * ox + s.Z.x * oz, s.c.y + s.X.y * ox + s.Z.y * oz, s.c.z + s.X.z * ox + s.Z.z * oz);
                    double u;
                    if (arc != null) u = (double)i / n;
                    else u = !o.mirror ? (double)i / n : Math.Abs(th) / PI;
                    if (!double.IsNaN(o.u0)) u = lerp(o.u0, o.u1, u);
                    var uv = o.uvc ?? ruv(reg, u, t);
                    var w = o.wf != null ? o.wf(p, th, k) : (s.hasW ? s.w : o.w);
                    var c = o.colFn != null ? o.colFn(th, s, k) : (s.col ?? o.col);
                    row[i] = gb.v(p, uv, c, w, grp);
                }
                ring.Add(row);
            }
            bool flip = o.flip;
            for (int k = 0; k < ring.Count - 1; k++)
            {
                var A = ring[k]; var B = ring[k + 1];
                for (int i = 0; i < n; i++)
                {
                    int i1 = arc != null ? i + 1 : (i + 1) % n;
                    int a = A[i], b = A[i1], c = B[i1], d = B[i];
                    if (a == b && c == d) continue;
                    if (a == b) { if (flip) gb.tri(a, d, c); else gb.tri(a, c, d); continue; }
                    if (c == d) { if (flip) gb.tri(a, c, b); else gb.tri(a, b, c); continue; }
                    if (flip) gb.quad(a, d, c, b); else gb.quad(a, b, c, d);
                }
            }
            // 端面
            if (o.capStart && !secs[0].pole) capRing(gb, ring[0], secs[0], reg, grp, !flip, o);
            if (o.capEnd && !secs[secs.Count - 1].pole) capRing(gb, ring[ring.Count - 1], secs[secs.Count - 1], reg, grp, flip, o);
            return ring;
        }
        static void capRing(FigGB gb, int[] row, FigSec s, FigRegion reg, int grp, bool flip, FigOpt o)
        {
            int cg = gb.group();
            // 端面中心 = 环上顶点的平均（已是变换后的坐标）；端面用独立顶点（硬边），添加时暂时关闭变换
            double cx = 0, cy = 0, cz = 0;
            foreach (int i in row) { cx += gb.P[i * 3]; cy += gb.P[i * 3 + 1]; cz += gb.P[i * 3 + 2]; }
            cx /= row.Length; cy /= row.Length; cz /= row.Length;
            var sxf = gb.xf; gb.xf = null;
            Func<V3d, FBW[]> wOf = q => o.wf != null ? o.wf(q, 0, 0) : (s.hasW ? s.w : o.w);
            int ci = row[0];
            int id = gb.v(new V3d(cx, cy, cz), gb.UVof(ci), gb.Cof(ci), wOf(new V3d(cx, cy, cz)), cg);
            var ids = new int[row.Length];
            for (int j = 0; j < row.Length; j++) { int i = row[j]; var p = gb.Pt(i); ids[j] = gb.v(p, gb.UVof(i), gb.Cof(i), wOf(p), cg); }
            gb.xf = sxf;
            for (int i = 0; i < ids.Length; i++)
            {
                int a = ids[i], b = ids[(i + 1) % ids.Length];
                if (flip) gb.tri(id, b, a); else gb.tri(id, a, b);
            }
        }
        // 沿折线的平行移动标架
        public struct Frames { public V3d[] T, X, Z; }
        public static Frames frames(IList<V3d> pts, V3d? up)
        {
            int N = pts.Count;
            var F = new Frames { T = new V3d[N], X = new V3d[N], Z = new V3d[N] };
            for (int i = 0; i < N; i++)
            {
                var a = pts[Math.Max(0, i - 1)]; var b = pts[Math.Min(N - 1, i + 1)];
                F.T[i] = v3norm(v3sub(b, a));
            }
            V3d rf = up ?? new V3d(0, 0, 1);
            if (Math.Abs(v3dot(rf, F.T[0])) > 0.95) rf = new V3d(1, 0, 0);
            var x = v3norm(v3cross(rf, F.T[0]));
            for (int i = 0; i < N; i++)
            {
                if (i > 0)
                {
                    var ax = v3cross(F.T[i - 1], F.T[i]); double l = v3len(ax);
                    if (l > 1e-6) x = v3rot(x, v3mul(ax, 1 / l), Math.Asin(clamp(l, -1, 1)) * (v3dot(F.T[i - 1], F.T[i]) < 0 ? -1 : 1));
                    x = v3norm(v3sub(x, v3mul(F.T[i], v3dot(x, F.T[i]))));
                }
                F.X[i] = x;
                F.Z[i] = v3norm(v3cross(x, F.T[i]));
            }
            return F;
        }
        // 沿折线 / 曲线的管：r（常数 / 函数 (s, i) / 数组）、rz 比例、twist
        public static List<int[]> tube(FigGB gb, IList<V3d> pts, FigOpt o)
        {
            var F = frames(pts, o.up);
            var secs = new List<FigSec>();
            int N = pts.Count;
            for (int i = 0; i < N; i++)
            {
                double s = N > 1 ? (double)i / (N - 1) : 0;
                double r = o.rF != null ? o.rF(s, i) : o.rArr != null ? o.rArr[i] : o.r;
                double rz = r * (o.rzF != null ? o.rzF(s, i) : !double.IsNaN(o.rz) ? o.rz : 1);
                V3d X = F.X[i], Z = F.Z[i];
                if (o.twist != 0) { double a = o.twist * s; X = v3rot(X, F.T[i], a); Z = v3rot(Z, F.T[i], a); }
                var sec = new FigSec { c = pts[i], X = X, Z = Z, rx = r, rz = rz, t = o.tF != null ? o.tF(s) : s };
                sec.SetW(o.wT != null ? o.wT(s, pts[i]) : o.w);
                if (r <= 1e-5 && (i == 0 || i == N - 1)) sec.pole = true;
                if (o.col.HasValue) sec.col = o.col;
                if (o.colS != null) sec.col = o.colS(s);
                secs.Add(sec);
            }
            return loft(gb, secs, o);
        }
        // 绕 Y 轴的旋转体：prof = [r, y, (rz), (t)]（自下而上）
        public static List<int[]> lathe(FigGB gb, double[][] prof, FigOpt o)
        {
            V3d c = o.c ?? new V3d(0, 0, 0); var AX = o.axes ?? FigAxes.Std;
            var secs = new List<FigSec>();
            for (int k = 0; k < prof.Length; k++)
            {
                var p = prof[k];
                double r = p[0], rz = p.Length > 2 && !double.IsNaN(p[2]) ? p[2] : r;
                var s = new FigSec { c = v3madd(c, AX.Y, p[1]), X = AX.X, Z = AX.Z, rx = r * o.sx, rz = rz * o.sz, t = p.Length > 3 && !double.IsNaN(p[3]) ? p[3] : (double)k / (prof.Length - 1) };
                if (o.w != null) s.SetW(o.w);
                if (r <= 1e-5 && (k == 0 || k == prof.Length - 1)) s.pole = true;
                secs.Add(s);
            }
            return loft(gb, secs, o);
        }
        // 椭球（沿 Y 的纬圈放样）
        public static List<int[]> ellipsoid(FigGB gb, V3d c, V3d r, FigOpt o)
        {
            int nl = o.nl != 0 ? o.nl : 8;
            var AX = o.axes ?? FigAxes.Std;
            var secs = new List<FigSec>();
            for (int k = 0; k <= nl; k++)
            {
                double ph = -PI / 2 + PI * ((double)k / nl);
                double y = Math.Sin(ph) * r.y, rr = Math.Cos(ph);
                var s = new FigSec { c = v3madd(c, AX.Y, y), X = AX.X, Z = AX.Z, rx = r.x * rr, rz = r.z * rr, t = (double)k / nl };
                if (o.w != null) s.SetW(o.w);
                if (k == 0 || k == nl) s.pole = true;
                secs.Add(s);
            }
            return loft(gb, secs, o);
        }

        // 双面布片 / 甲片：fn(s, t) → 点（s 横向 0..1，t 纵向 0..1）；正反两面（厚度 th，沿曲面法线）并封边
        public sealed class PanelRes { public int[] F, B; public V3d[] P, N; }
        public static PanelRes panel(FigGB gb, Func<double, double, V3d> fn, FigOpt o)
        {
            int nu = o.nu, nv = o.nv; double th = o.th != 0 ? o.th : 0.006; var reg = FigAtlasLayout.REG(o.reg ?? "cloth");
            var P = new V3d[(nu + 1) * (nv + 1)]; var Nn = new V3d[P.Length];
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++) P[j * (nu + 1) + i] = fn((double)i / nu, (double)j / nv);
            Func<int, int, V3d> at = (i, j) => P[(int)clamp(j, 0, nv) * (nu + 1) + (int)clamp(i, 0, nu)];
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
            {
                var du = v3sub(at(i + 1, j), at(i - 1, j)); var dv = v3sub(at(i, j + 1), at(i, j - 1));
                var nn = v3cross(du, dv);
                if (v3len(nn) < 1e-9) nn = new V3d(0, 0, 1);
                Nn[j * (nu + 1) + i] = v3norm(nn);
            }
            Func<double, double, double[]> uvOf = (s, t) =>
            {
                double[] lc = o.uv != null ? o.uv(s, t) : new[] { o.mirrorS ? Math.Abs(2 * s - 1) : s, t };
                return ruv(reg, lc[0], lc[1]);
            };
            int gF = gb.group(), gBk = gb.group();
            var Fv = new int[P.Length]; var Bv = new int[P.Length];
            FCol colF = o.col ?? new FCol(1, 1, 1);
            FCol colB = o.colB ?? shade(colF, -0.25);
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
            {
                int k = j * (nu + 1) + i; var p = P[k]; var nrm = Nn[k]; double s = (double)i / nu, t = (double)j / nv;
                var w = o.wP != null ? o.wP(p, s, t) : o.w;
                FCol cf = o.colPF != null ? o.colPF(s, t) : colF;
                FCol cb = colB;
                Fv[k] = gb.v(v3madd(p, nrm, th / 2), uvOf(s, t), cf, w, gF);
                Bv[k] = gb.v(v3madd(p, nrm, -th / 2), uvOf(s, t), cb, w, gBk);
            }
            for (int j = 0; j < nv; j++) for (int i = 0; i < nu; i++)
            {
                int a = j * (nu + 1) + i, b = a + 1, c = a + nu + 2, d = a + nu + 1;
                gb.quad(Fv[a], Fv[b], Fv[c], Fv[d]);
                gb.quad(Bv[a], Bv[d], Bv[c], Bv[b]);
            }
            if (o.edge && !gb.noEdges)
            {
                // 封边：沿四条边连接正反面
                int gE = gb.group();
                var loop = new List<int>();
                for (int i = 0; i <= nu; i++) loop.Add(i);
                for (int j = 1; j <= nv; j++) loop.Add(j * (nu + 1) + nu);
                for (int i = nu - 1; i >= 0; i--) loop.Add(nv * (nu + 1) + i);
                for (int j = nv - 1; j >= 1; j--) loop.Add(j * (nu + 1));
                var ef = new int[loop.Count]; var eb = new int[loop.Count];
                for (int q = 0; q < loop.Count; q++)
                {
                    int k = loop[q];
                    var pf = gb.Pt(Fv[k]); var pb = gb.Pt(Bv[k]);
                    var uv = gb.UVof(Fv[k]);
                    var w = o.wP != null ? o.wP(P[k], 0, 0) : o.w;
                    FCol ce = o.colPF != null ? o.colPF(0, 0) : (o.colE ?? colF);
                    var sxf = gb.xf; gb.xf = null;
                    ef[q] = gb.v(pf, uv, ce, w, gE); eb[q] = gb.v(pb, uv, ce, w, gE);
                    gb.xf = sxf;
                }
                int L = loop.Count;
                for (int i = 0; i < L; i++) { int i1 = (i + 1) % L; gb.quad(ef[i], eb[i], eb[i1], ef[i1]); }
            }
            return new PanelRes { F = Fv, B = Bv, P = P, N = Nn };
        }

        // 刀刃：逐段放样，每个面一条带（刃口与脊线为硬边，沿长度方向平滑）
        public static void blade(FigGB gb, List<FigBladeSt> st, FigOpt o)
        {
            var reg = FigAtlasLayout.REG(o.reg ?? "blade");
            bool simple = gb.noEdges || gb.detail == 1;
            Func<FigBladeSt, double[][]> prof = s =>
            {
                if (simple) return s.dbl ? new[] { new[] { s.we, 0 }, new[] { 0, s.th * 1.2 }, new[] { -s.wb, 0 }, new[] { 0, -s.th * 1.2 } }
                    : new[] { new[] { s.we, 0 }, new[] { -s.wb * 0.8, s.th }, new[] { -s.wb, 0 }, new[] { -s.wb * 0.8, -s.th } };
                return s.dbl ? new[] { new[] { s.we, 0 }, new[] { s.we * 0.42, s.th * 0.85 }, new[] { 0, s.th }, new[] { -s.wb * 0.42, s.th * 0.85 }, new[] { -s.wb, 0 }, new[] { -s.wb * 0.42, -s.th * 0.85 }, new[] { 0, -s.th }, new[] { s.we * 0.42, -s.th * 0.85 } }
                    : new[] { new[] { s.we, 0 }, new[] { s.we * 0.3, s.th * 0.75 }, new[] { -s.wb * 0.85, s.th }, new[] { -s.wb, s.th * 0.55 }, new[] { -s.wb, -s.th * 0.55 }, new[] { -s.wb * 0.85, -s.th }, new[] { s.we * 0.3, -s.th * 0.75 } };
            };
            int N = st.Count;
            var rows = new V3d[N][];
            for (int k = 0; k < N; k++)
            {
                var s = st[k]; var pr = prof(s);
                rows[k] = new V3d[pr.Length];
                for (int q = 0; q < pr.Length; q++) rows[k][q] = v3add(v3add(s.p, v3mul(s.e, pr[q][0])), v3mul(s.n, pr[q][1]));
            }
            int np = rows[0].Length;
            FCol cc = o.col ?? new FCol(1, 1, 1);
            // 绕序：(e, n, 长度方向) 为右手系时需要反转
            bool sg = v3dot(v3cross(st[0].e, st[0].n), v3sub(st[N - 1].p, st[0].p)) > 0;
            for (int f = 0; f < np; f++)
            {
                int g = gb.group(), f1 = (f + 1) % np;
                var ids = new int[N, 2];
                for (int k = 0; k < N; k++)
                {
                    double t = (double)k / (N - 1);
                    double uA = (double)f / np, uB = (double)(f + 1) / np;
                    ids[k, 0] = gb.v(rows[k][f], ruv(reg, uA, t), cc, o.w, g);
                    ids[k, 1] = gb.v(rows[k][f1], ruv(reg, uB, t), cc, o.w, g);
                }
                for (int k = 0; k < N - 1; k++)
                {
                    if (sg) gb.quad(ids[k, 0], ids[k, 1], ids[k + 1, 1], ids[k + 1, 0]);
                    else gb.quad(ids[k, 0], ids[k + 1, 0], ids[k + 1, 1], ids[k, 1]);
                }
            }
            // 根部封口
            if (o.capBase)
            {
                int g = gb.group();
                int c = gb.v(st[0].p, ruv(reg, 0.5, 0), cc, o.w, g);
                var ids = new int[np];
                for (int i = 0; i < np; i++) ids[i] = gb.v(rows[0][i], ruv(reg, 0.5, 0), cc, o.w, g);
                for (int i = 0; i < np; i++) { if (sg) gb.tri(c, ids[(i + 1) % np], ids[i]); else gb.tri(c, ids[i], ids[(i + 1) % np]); }
            }
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    // ------------------------------------------------------------------ 导出的数据表（FigureData.g.cs 的 JSON → JObj，首次用到时解析）
    public static class FigTables
    {
        static Dictionary<string, object> T;
        static readonly object lk = new object();
        public static object Get(string name)
        {
            lock (lk)
            {
                if (T == null)
                {
                    var d = new Dictionary<string, object>();
                    for (int i = 0; i + 1 < FigureData.Tables.Length; i += 2) d[FigureData.Tables[i]] = JObj.ParseJson(FigureData.Tables[i + 1]);
                    T = d;
                }
                return T[name];
            }
        }
        public static JObj O(string name) { return (JObj)Get(name); }
        public static List<object> L(string name) { return (List<object>)Get(name); }
        public static JObj OUTFIT { get { return O("OUTFIT"); } }
        public static JObj HAT_MAP { get { return O("HAT_MAP"); } }
        public static JObj SIG { get { return O("SIG"); } }
        public static JObj PWEAPON { get { return O("PWEAPON"); } }
        public static JObj CULT_WEAPON { get { return O("CULT_WEAPON"); } }
        public static JObj CULT_SHIELD { get { return O("CULT_SHIELD"); } }
        public static JObj SOLDIER { get { return O("SOLDIER"); } }
        public static JObj SOLDIER_WEAPON { get { return O("SOLDIER_WEAPON"); } }
        public static JObj WEAPONS { get { return O("WEAPONS"); } }
        public static JObj DUAL_BASE { get { return O("DUAL_BASE"); } }
        public static JObj LODP { get { return O("LODP"); } }
        public static JObj TRI_BUDGET { get { return O("TRI_BUDGET"); } }
        public static JObj SQUEEZE { get { return O("SQUEEZE"); } }
        public static List<object> HORSE_COATS { get { return L("HORSE_COATS"); } }
        public static JObj HORSE_NAMED { get { return O("HORSE_NAMED"); } }
        public static JObj HCLIPS { get { return O("HCLIPS"); } }
        public static JObj MOUNT_CLIPS { get { return O("MOUNT_CLIPS"); } }
        public static List<object> CROWD_CLIPS { get { return L("CROWD_CLIPS"); } }
        public static List<object> CULTURES { get { return L("CULTURES"); } }
        public static List<object> REGION_DEFS { get { return L("REGION_DEFS"); } }
        public static List<object> GENERIC_FACES { get { return L("GENERIC_FACES"); } }
        public static JObj CLIPS { get { return O("CLIPS"); } }
        public static List<object> SOLDIER_TYPES { get { return L("SOLDIER_TYPES"); } }
        public static string[] Strings(List<object> l) { var a = new string[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = (string)l[i]; return a; }
    }

    // ------------------------------------------------------------------ LOD 参数（= JS LODP 的一项）
    public sealed class FigLod
    {
        public int torN, torK, limbN, armK, legK, pn, pv, foot, ring, tube, detail;
        public int[] head = new int[2], hand = new int[2];
        static int I(JObj o, string k, int d) { double v = o.D(k); return double.IsNaN(v) ? d : (int)v; }
        static int[] A(JObj o, string k, int[] d) { var l = o.L(k); return l == null ? d : new[] { (int)(double)l[0], (int)(double)l[1] }; }
        public FigLod With(JObj o)
        {
            var r = (FigLod)MemberwiseClone();
            r.torN = I(o, "torN", torN); r.torK = I(o, "torK", torK); r.limbN = I(o, "limbN", limbN); r.armK = I(o, "armK", armK); r.legK = I(o, "legK", legK);
            r.pn = I(o, "pn", pn); r.pv = I(o, "pv", pv); r.foot = I(o, "foot", foot); r.ring = I(o, "ring", ring); r.tube = I(o, "tube", tube); r.detail = I(o, "detail", detail);
            r.head = A(o, "head", head); r.hand = A(o, "hand", hand);
            return r;
        }
        static readonly Dictionary<string, FigLod> cache = new Dictionary<string, FigLod>();
        public static FigLod Of(string lod)
        {
            lock (cache)
            {
                FigLod r;
                if (lod == null || !FigTables.LODP.Has(lod)) lod = "high";
                if (cache.TryGetValue(lod, out r)) return r;
                r = new FigLod().With(FigTables.LODP.O(lod));
                cache[lod] = r;
                return r;
            }
        }
        public static FigLod Squeezed(string lod, int lv) { return lv == 0 ? Of(lod) : Of(lod).With((JObj)FigTables.SQUEEZE.L(lod)[lv - 1]); }
        public static int SqueezeLevels(string lod) { var l = FigTables.SQUEEZE.L(lod ?? "high"); return l == null ? 0 : l.Count; }
    }

    // ------------------------------------------------------------------ 图集布局（= JS packRegions；1024²，128px 一格）
    public static class FigAtlasLayout
    {
        public const int N = FigureData.ATLAS_N, CELL = FigureData.CELL;
        static Dictionary<string, FigRegion> R;
        static List<FigRegion> order;
        public static Dictionary<string, FigRegion> Regions { get { Pack(); return R; } }
        public static List<FigRegion> Order { get { Pack(); return order; } }
        static void Pack()
        {
            if (R != null) return;
            int G = N / CELL; var used = new bool[G * G]; var r = new Dictionary<string, FigRegion>(); var ord = new List<FigRegion>();
            foreach (List<object> d in FigTables.REGION_DEFS)
            {
                string name = (string)d[0]; int cw = (int)(double)d[1], ch = (int)(double)d[2];
                bool done = false;
                for (int y = 0; y <= G - ch && !done; y++) for (int x = 0; x <= G - cw && !done; x++)
                {
                    bool free = true;
                    for (int j = 0; j < ch && free; j++) for (int i = 0; i < cw && free; i++) if (used[(y + j) * G + x + i]) free = false;
                    if (!free) continue;
                    for (int j = 0; j < ch; j++) for (int i = 0; i < cw; i++) used[(y + j) * G + x + i] = true;
                    int px = x * CELL, py = y * CELL, pw = cw * CELL, ph = ch * CELL, ins = 3;
                    var reg = new FigRegion { name = name, px = px, py = py, pw = pw, ph = ph, u0 = (double)(px + ins) / N, u1 = (double)(px + pw - ins) / N, vb = 1 - (double)(py + ph - ins) / N, vt = 1 - (double)(py + ins) / N };
                    r[name] = reg; ord.Add(reg);
                    done = true;
                }
                if (!done) throw new Exception("figure atlas full: " + name);
            }
            order = ord; R = r;
        }
        public static FigRegion REG(string name)
        {
            FigRegion r;
            if (!Regions.TryGetValue(name, out r)) throw new Exception("no atlas region " + name);
            return r;
        }
    }

    // ------------------------------------------------------------------ 人形骨架
    // 绑定姿势：A 字（双臂外展 40°，肘直），全部骨骼在绑定时旋转为单位（局部轴 = 人物轴）
    public static class FigBones
    {
        public static readonly string[] BONES = FigureData.BONES;
        public static readonly int[] PARENT = FigureData.PARENT;
        static Dictionary<string, int> idx;
        public static int B(string n)
        {
            if (idx == null) { var d = new Dictionary<string, int>(); for (int i = 0; i < FigureData.BONES.Length; i++) d[FigureData.BONES[i]] = i; idx = d; }
            return idx[n];
        }
        public static readonly int root = B("root"), pelvis = B("pelvis"), spine = B("spine"), chest = B("chest"), neck = B("neck"), head = B("head"),
            clavL = B("clavL"), upperArmL = B("upperArmL"), foreArmL = B("foreArmL"), handL = B("handL"), clavR = B("clavR"), upperArmR = B("upperArmR"), foreArmR = B("foreArmR"), handR = B("handR"),
            thighL = B("thighL"), shinL = B("shinL"), footL = B("footL"), thighR = B("thighR"), shinR = B("shinR"), footR = B("footR"),
            cape1 = B("cape1"), cape2 = B("cape2"), cape3 = B("cape3"), skirtF = B("skirtF"), skirtB = B("skirtB"), plume1 = B("plume1"), plume2 = B("plume2"), hair1 = B("hair1"),
            shL = B("shL"), shR = B("shR"), dlL = B("dlL"), dlR = B("dlR");
        public static int Count { get { return BONES.Length; } }
        public static int GROUND_SLOT { get { return BONES.Length; } }
        public const double ARM_A = 40 * D2R;
        // 战马
        public static readonly string[] HBONES = FigureData.HBONES;
        public static readonly int[] HPARENT = FigureData.HPARENT;
        static Dictionary<string, int> hidx;
        public static int HB(string n)
        {
            if (hidx == null) { var d = new Dictionary<string, int>(); for (int i = 0; i < FigureData.HBONES.Length; i++) d[FigureData.HBONES[i]] = i; hidx = d; }
            return hidx[n];
        }
        // 权重的简写
        public static FBW[] W1(int b) { return new[] { new FBW(b, 1) }; }
        public static FBW[] W2(int a, double wa, int b, double wb) { return new[] { new FBW(a, wa), new FBW(b, wb) }; }
    }

    // 体型 → 关节位置（人物坐标，米）
    public sealed class FigProps
    {
        public V3d[] J;      // 按骨骼编号
        public double k, sw, hw, limb, fat, build, upL, foL, H;
        public bool fem;
        public V3d armDirL;
        public V3d this[string n] { get { return J[FigBones.B(n)]; } }
    }

    // 链式权重的一节
    public struct FigChain { public int bone; public V3d p; public FigChain(int b, V3d p) { bone = b; this.p = p; } }

    // 身体各部位的权重函数
    public sealed class FigWeights
    {
        public Func<V3d, FBW[]> torso, neck;
        public Func<string, Func<V3d, FBW[]>> arm, leg;
    }

    public static partial class FigBuild
    {
        public static FigProps makeProps(FigLook look)
        {
            bool fem = look.sex == "f";
            double h = (look.height != 0 ? look.height : 1) * (fem ? 0.94 : 1);
            double k = h;
            double build = clamp(look.build != 0 ? look.build : 1, 0.82, 1.3), fat = clamp(look.fat, 0, 1);
            // 体格差异要拉开：肩宽约 ±20%、四肢粗细、髋宽随 build / fat
            double bk = fem ? (build - 0.9) * 0.35 : (build - 1) * 0.85;
            double sw = (fem ? 0.86 : 1) * (1 + bk) * (1 + fat * 0.06);
            double hw = (fem ? 1.04 : 1) * (1 + bk * 0.4) * (1 + fat * 0.14);
            double limb = (fem ? 0.86 : 1) * (1 + bk * 1.05) * (1 + fat * 0.22);
            var J = new V3d[FigBones.Count];
            Action<string, V3d> S = (n, v) => J[FigBones.B(n)] = v;
            S("root", V(0, 0, 0));
            S("pelvis", V(0, 0.995 * k, 0));
            S("spine", V(0, 1.09 * k, -0.006));
            S("chest", V(0, 1.27 * k, -0.012));
            S("neck", V(0, 1.5 * k, -0.022));
            S("head", V(0, 1.585 * k, -0.01));
            var ad = V(Math.Sin(FigBones.ARM_A), -Math.Cos(FigBones.ARM_A), 0);
            double upL = 0.3 * k, foL = 0.265 * k;
            foreach (int s in new[] { 1, -1 })
            {
                string Sd = s > 0 ? "L" : "R";
                S("clav" + Sd, V(0.035 * s * k, 1.455 * k, 0.0));
                var ua = V(0.188 * s * k * sw, 1.462 * k, -0.026);
                S("upperArm" + Sd, ua);
                var d = V(ad.x * s, ad.y, 0);
                var fa = v3madd(ua, d, upL);
                S("foreArm" + Sd, fa);
                S("hand" + Sd, v3madd(fa, d, foL));
                S("thigh" + Sd, V(0.092 * s * k * hw, 0.965 * k, 0));
                S("shin" + Sd, V(0.1 * s * k * hw, 0.53 * k, 0.012));
                S("foot" + Sd, V(0.106 * s * k * hw, 0.088 * k, -0.018));
            }
            S("cape1", V(0, 1.47 * k, -0.15 * sw));
            S("cape2", V(0, 1.1 * k, -0.2 * sw));
            S("cape3", V(0, 0.7 * k, -0.22 * sw));
            S("skirtF", V(0, 1.02 * k, 0.12 * hw));
            S("skirtB", V(0, 1.02 * k, -0.13 * hw));
            S("plume1", V(0, 1.86 * k, -0.01));
            S("plume2", V(0, 2.0 * k, -0.07));
            S("hair1", V(0, 1.71 * k, -0.1));
            S("shL", J[FigBones.upperArmL]); S("shR", J[FigBones.upperArmR]); S("dlL", J[FigBones.upperArmL]); S("dlR", J[FigBones.upperArmR]);
            return new FigProps { J = J, k = k, sw = sw, hw = hw, limb = limb, fat = fat, fem = fem, build = build, armDirL = ad, upL = upL, foL = foL, H = 1.86 * k };
        }

        // 握点挂点：右手握点在手腕沿前臂方向 7cm 处；挂点 +Y = 兵器杆方向（绑定时朝前 +Z），+Z = 指节方向（沿前臂向外下），+X = Y × Z
        public struct Grip { public V3d p; public Q4d q; public V3d X, Y, Z; }
        public static Grip gripLocal(FigProps pr, string side)
        {
            double s = side == "L" ? 1 : -1;
            var d = V(pr.armDirL.x * s, pr.armDirL.y, 0);
            V3d Y = V(0, 0, 1), Z = d, X = v3cross(Y, Z);
            var q = Q4d.FromMatrix(M4d.Basis(X, Y, Z));
            return new Grip { p = v3mul(d, 0.072 * pr.k), q = q, X = X, Y = Y, Z = Z };
        }
        // 绑定姿势下挂点的人物坐标变换（把兵器 / 拳头几何放到绑定位置用）
        public static M4d socketRest(FigProps pr, string side)
        {
            var g = gripLocal(pr, side);
            var J = pr["hand" + side];
            return M4d.Compose(V(J.x + g.p.x, J.y + g.p.y, J.z + g.p.z), g.q, V(1, 1, 1));
        }

        // 链式权重：沿关节链投影，在关节附近 ±blend 米内平滑过渡（chain 最后一节的 bone = -1：终点）
        public static FBW[] chainW(V3d p, FigChain[] chain, double blend)
        {
            int best = 0; double bestD = 1e9, bestT = 0;
            for (int i = 0; i < chain.Length - 1; i++)
            {
                var a = chain[i].p; var b = chain[i + 1].p; var ab = v3sub(b, a); double l2 = v3dot(ab, ab); if (l2 == 0) l2 = 1e-9;
                double t = clamp(v3dot(v3sub(p, a), ab) / l2, 0, 1);
                var q = v3madd(a, ab, t); double d = v3len(v3sub(p, q));
                if (d < bestD - 1e-6) { bestD = d; best = i; bestT = t; }
            }
            int segs = chain.Length - 1;
            var A = chain[best].p; var Bp = chain[best + 1].p; double L = v3len(v3sub(Bp, A)); if (L == 0) L = 1e-6;
            double dist0 = bestT * L, dist1 = (1 - bestT) * L;
            int bi = chain[best].bone;
            if (dist0 < blend && best > 0)
            {
                double k = 0.5 + 0.5 * (dist0 / blend);
                return new[] { new FBW(bi, k), new FBW(chain[best - 1].bone, 1 - k) };
            }
            if (dist1 < blend && best < segs - 1 && chain[best + 1].bone >= 0)
            {
                double k = 0.5 + 0.5 * (dist1 / blend);
                return new[] { new FBW(bi, k), new FBW(chain[best + 1].bone, 1 - k) };
            }
            return new[] { new FBW(bi, 1) };
        }
        public static FigWeights weightsFor(FigProps pr)
        {
            var J = pr.J;
            Func<string, V3d> j = n => J[FigBones.B(n)];
            var spineChain = new[] { new FigChain(FigBones.pelvis, V(0, j("pelvis").y - 0.2, 0)), new FigChain(FigBones.spine, j("spine")), new FigChain(FigBones.chest, j("chest")),
                new FigChain(FigBones.neck, j("neck")), new FigChain(FigBones.head, j("head")), new FigChain(-1, V(0, j("head").y + 0.3, 0)) };
            Func<string, FigChain[]> arm = S => new[] { new FigChain(FigBones.B("clav" + S), j("clav" + S)), new FigChain(FigBones.B("upperArm" + S), j("upperArm" + S)),
                new FigChain(FigBones.B("foreArm" + S), j("foreArm" + S)), new FigChain(FigBones.B("hand" + S), j("hand" + S)),
                new FigChain(-1, v3madd(j("hand" + S), V(pr.armDirL.x * (S == "L" ? 1 : -1), pr.armDirL.y, 0), 0.2)) };
            Func<string, FigChain[]> leg = S => new[] { new FigChain(FigBones.pelvis, V(j("thigh" + S).x * 0.5, j("pelvis").y + 0.1, 0)), new FigChain(FigBones.B("thigh" + S), j("thigh" + S)),
                new FigChain(FigBones.B("shin" + S), j("shin" + S)), new FigChain(FigBones.B("foot" + S), j("foot" + S)), new FigChain(-1, v3add(j("foot" + S), V(0, -0.05, 0.25))) };
            var AL = arm("L"); var AR = arm("R"); var LL = leg("L"); var LR = leg("R");
            var W = new FigWeights();
            W.torso = p =>
            {
                var w = new List<FBW>(chainW(p, spineChain, 0.07));
                // 肩部：靠近肩关节的躯干顶点带一点锁骨 / 三角肌过渡骨的权重
                foreach (var S in new[] { "L", "R" })
                {
                    double d = v3len(v3sub(p, j("upperArm" + S)));
                    if (d < 0.12 && Math.Abs(p.x) > 0.09)
                    {
                        double k = (1 - d / 0.12) * 0.6;
                        for (int i = 0; i < w.Count; i++) w[i] = new FBW(w[i].b, w[i].w * (1 - k));
                        w.Add(new FBW(FigBones.B("clav" + S), k * 0.45)); w.Add(new FBW(FigBones.B("dl" + S), k * 0.55));
                    }
                }
                return w.ToArray();
            };
            // 臂：袖根（肩关节内外约 7cm）经由肩部辅助骨过渡到上臂，避免高举时肩部撕裂
            W.arm = S => p =>
            {
                double sg = S == "L" ? 1 : -1; var d = V(pr.armDirL.x * sg, pr.armDirL.y, 0);
                double s = v3dot(v3sub(p, j("upperArm" + S)), d);
                if (s < 0.08)
                {
                    double k1 = sstep(-0.075, -0.015, s), k2 = sstep(-0.01, 0.08, s);
                    return new[] { new FBW(FigBones.B("clav" + S), 1 - k1), new FBW(FigBones.B("dl" + S), k1 * (1 - k2)), new FBW(FigBones.B("upperArm" + S), k1 * k2) };
                }
                return chainW(p, S == "L" ? AL : AR, 0.05);
            };
            W.leg = S => p => chainW(p, S == "L" ? LL : LR, 0.06);
            W.neck = p => chainW(p, spineChain, 0.05);
            return W;
        }
        // 每个模板共用的骨骼绑定逆矩阵（绑定旋转为单位 → 逆矩阵只有平移）
        public static M4d[] makeBoneInverses(FigProps pr)
        {
            var r = new M4d[FigBones.Count];
            for (int i = 0; i < r.Length; i++) r[i] = M4d.Translation(-pr.J[i].x, -pr.J[i].y, -pr.J[i].z);
            return r;
        }

        // ======================================================== 头部与面部 ==
        // 头部局部坐标：原点 = 头骨（颈顶），面朝 +Z。头部在球面参数 (θ 经度：0 = 正前，φ 纬度：0 = 头中心高度) 上变形；
        // 面部贴图按同一参数映射（经度方向非线性拉伸，让脸部占更多像素），五官位置在参数空间里定义
        public static readonly V3d HEAD_C = V(FigureData.HEAD_C[0], FigureData.HEAD_C[1], FigureData.HEAD_C[2]);
        public const double Y_BOT = -0.022, Y_TOP = 0.236;
        public static double Yph(double ph)
        {
            var T = FigureData.PHI_Y;
            if (ph <= T[0][0]) return T[0][1];
            for (int i = 0; i < T.Length - 1; i++) { var a = T[i]; var b = T[i + 1]; if (ph <= b[0]) return lerp(a[1], b[1], (ph - a[0]) / (b[0] - a[0])); }
            return Y_TOP;
        }
        public static double[] headUV(double th, double ph)
        {
            double x = clamp(th / PI, -1, 1);
            double u = 0.5 + 0.5 * sign(x) * Math.Pow(Math.Abs(x), 0.7);
            return new[] { u, clamp01((Yph(ph) - Y_BOT) / (Y_TOP - Y_BOT)) };
        }
        public sealed class HeadP
        {
            public bool fem; public double fat, w, len, jaw, chin, cheek, brow, hollow, noseLen, noseW, bridge, aquiline, flat, bulb, ear, lips, size, lodF = 1;
        }
        public static HeadP headParams(JObj sp)
        {
            sp = sp ?? new JObj();
            var f = sp.O("face") ?? new JObj(); bool fem = sp.S("sex") == "f"; var nose = sp.O("nose") ?? new JObj(); var ears = sp.O("ears") ?? new JObj();
            string ns = nose.S("shape");
            var mouth = sp.O("mouth");
            return new HeadP
            {
                fem = fem, fat = sp.Or("fat", 0.0),
                w = f.Or("w", 1.0) * (fem ? 0.95 : 1), len = f.Or("len", 1.0), jaw = f.Or("jaw", 1.0) * (fem ? 0.85 : 1), chin = f.Or("chin", 1.0), cheek = f.Or("cheek", 0.0),
                brow = f.Has("brow") ? f.D("brow") : 0.4, hollow = f.Or("hollow", 0.0),
                noseLen = nose.Or("len", 1.0) * nose.Or("size", 1.0) * (fem ? 0.8 : 1),
                noseW = nose.Or("w", 1.0) * (ns == "broad" || ns == "flat" ? 1.3 : ns == "small" || ns == "button" ? 0.85 : 1),
                bridge = nose.Has("bridge") ? nose.D("bridge") : 0.5, aquiline = ns == "aquiline" ? 1 : 0, flat = ns == "flat" ? 1 : 0, bulb = ns == "bulb" ? 1 : 0,
                ear = ears.Or("size", 1.0), lips = mouth != null ? mouth.Or("lips", 1.0) : 1, size = fem ? 1.03 : 1.07, lodF = 1,
            };
        }
        // 头部截面（y, 半宽, 前缘 z, 后缘 z, 前半部方度）的 Catmull-Rom 插值
        static double[] hprofAt(double y)
        {
            var R = FigureData.HPROF;
            if (y <= R[0][0]) return R[0];
            if (y >= R[R.Length - 1][0]) return R[R.Length - 1];
            int i = 0;
            while (i < R.Length - 2 && R[i + 1][0] < y) i++;
            var a = R[Math.Max(0, i - 1)]; var b = R[i]; var c = R[i + 1]; var d = R[Math.Min(R.Length - 1, i + 2)];
            double t = (y - b[0]) / (c[0] - b[0]), t2 = t * t, t3 = t2 * t;
            Func<int, double> cr = k => 0.5 * (2 * b[k] + (-a[k] + c[k]) * t + (2 * a[k] - 5 * b[k] + 4 * c[k] - d[k]) * t2 + (-a[k] + 3 * b[k] - 3 * c[k] + d[k]) * t3);
            return new[] { y, Math.Max(0, cr(1)), Math.Max(0, cr(2)), Math.Min(0, cr(3)), lerp(b[4], c[4], t) };
        }
        public static V3d headPoint(double th, double ph, HeadP Hp)
        {
            double y = Yph(ph);
            // 下半脸长度（以眼线为界）
            const double eyeY = 0.112;
            double yy = y < eyeY ? eyeY - (eyeY - y) / (0.94 + 0.06 * Hp.len) : y;
            var pr = hprofAt(yy);
            double fat = Hp.fat;
            double xw = pr[1] * Hp.w * (1 + fat * 0.08 * (1 - sstep(0.06, 0.14, y)));
            double zf = pr[2], zb = pr[3];
            // 下颌：越往下越收（方下巴收得少）
            double low = 1 - sstep(0.0, 0.075, y);
            double jawK = clamp(1.25 - 0.45 * Hp.jaw + fat * 0.3, 0.6, 1.2);
            xw *= 1 + 0.08 * low * (Hp.jaw - 1);
            double sn = Math.Sin(th), cs = Math.Cos(th);
            double x, z;
            if (cs >= 0)
            {
                double sq = pr[4];
                double ex = superE(sn, sq), ez = superE(cs, sq);
                double tap = 1 - 0.46 * low * jawK * Math.Pow(cs, 1.2);
                x = ex * xw * tap; z = ez * zf;
            }
            else { x = sn * xw; z = -cs * zb; }
            // 五官起伏（大多沿 +z）
            double ax = Math.Abs(x);
            double dz = 0, dn = 0;
            if (cs > 0)
            {
                // 细部起伏按 LOD 取舍：高精度（lodF = 1）细致的鼻、唇、眼窝；中低精度只保留宽缓的鼻梁与眉骨
                double F = Hp.lodF;
                const double tipY = 0.074, rootY = 0.122;
                double nh = 0, sig = 0.016;
                if (y >= tipY && y <= rootY + 0.01) { double t = clamp01((y - tipY) / (rootY - tipY)); nh = lerp(0.024, 0.003 + 0.006 * Hp.bridge, Math.Pow(t, 0.85)); sig = lerp(0.0165, 0.0115, t); if (Hp.aquiline != 0) nh += 0.004 * gauss(y, 0.1, 0.01); }
                else if (y < tipY) { nh = 0.024 * (1 - sstep(tipY, tipY - 0.015, y)); sig = 0.0165; }
                if (Hp.flat != 0) nh *= 0.65;
                sig *= Hp.noseW * (1 + Hp.bulb * 0.35 * gauss(y, tipY, 0.01));
                double broad = 0.009 * gauss(x, 0, 0.026) * gauss(y, 0.088, 0.024);
                dz += F * nh * Hp.noseLen * Math.Exp(-(x * x) / (sig * sig)) + (1 - F) * broad * Hp.noseLen;
                // 鼻翼
                dz += F * 0.006 * gauss(ax, 0.018 * Hp.noseW, 0.008) * gauss(y, 0.068, 0.008);
                // 眼窝与眼球
                dz += lerp(0.4, 1, F) * (-0.01 * gauss(ax, 0.034, 0.018) * gauss(y, 0.112, 0.012)) + F * 0.004 * gauss(ax, 0.034, 0.011) * gauss(y, 0.111, 0.007);
                // 眉弓
                dz += lerp(0.6, 1, F) * (0.003 + 0.004 * Hp.brow) * gauss(y, 0.129, 0.009) * (1 - sstep(0.05, 0.07, ax)) * (1 - 0.3 * gauss(x, 0, 0.01));
                // 唇、口角、下巴、人中、法令
                double lp = Hp.lips * (Hp.fem ? 1.25 : 1) * F;
                dz += 0.0028 * lp * gauss(x, 0, 0.02) * gauss(y, 0.054, 0.0068) + 0.0038 * lp * gauss(x, 0, 0.018) * gauss(y, 0.037, 0.0075);
                dz += F * (-0.0016 * gauss(x, 0, 0.024) * gauss(y, 0.0455, 0.004) - 0.0024 * gauss(ax, 0.026, 0.008) * gauss(y, 0.046, 0.008));
                dz += 0.006 * (0.6 + 0.4 * Hp.chin) * gauss(x, 0, 0.022) * gauss(y, 0.013, 0.011);
                dz += -0.002 * F * gauss(ax, 0.029, 0.009) * gauss(y, 0.06, 0.013);
                // 颧骨、胖瘦、两颊凹陷（沿截面法线）
                dn += (0.004 + 0.005 * Hp.cheek) * gauss(ax, 0.056, 0.016) * gauss(y, 0.095, 0.012);
                dn += 0.014 * fat * gauss(ax, 0.055, 0.022) * gauss(y, 0.05, 0.025) - 0.006 * Hp.hollow * gauss(ax, 0.052, 0.012) * gauss(y, 0.062, 0.013);
            }
            if (dn != 0) { x += sn * dn; z += cs * dn; }
            double hsz = Hp.size != 0 ? Hp.size : 1;
            return V(x * hsz, y * hsz, (z + dz) * hsz);
        }
        // 头部网格（头部局部坐标）。reg：图集区域（士兵的通用脸），null = 名将的独立面部贴图（UV 0..1）
        public static void buildHead(FigGB gb, HeadP Hp, int nLat, int nLon, string reg, FCol? cc, FBW[] w, bool ears)
        {
            int nl = nLat, nn = nLon, grp = gb.group();
            FigRegion R = reg != null ? FigAtlasLayout.REG(reg) : null;
            Func<double, double, double[]> uvOf = (th, ph) => { var q = headUV(th, ph); return R != null ? ruv(R, q[0], q[1]) : q; };
            var rows = new List<int[]>();
            // 经度分布：高精度时正面加密（θ = π·(a·u + (1 − a)·u³)）
            double wa = nn >= 24 ? 0.55 : nn >= 14 ? 0.8 : 1;
            Func<int, double> thOf = i => { double u = -1 + 2.0 * i / nn; return PI * (wa * u + (1 - wa) * u * u * u); };
            for (int k = 0; k <= nl; k++)
            {
                // 纬度分布：脸部加密
                double s = (double)k / nl;
                double ph = -PI / 2 + PI * (s + 0.06 * Math.Sin(s * TAU));
                var row = new int[nn + 1];
                if (k == 0 || k == nl)
                {
                    var p = headPoint(0, ph, Hp);
                    int id = gb.v(p, uvOf(0, ph), cc, w, grp);
                    for (int i = 0; i <= nn; i++) row[i] = id;
                }
                else
                {
                    for (int i = 0; i <= nn; i++) { double th = thOf(i); row[i] = gb.v(headPoint(th, ph, Hp), uvOf(th, ph), cc, w, grp); }
                }
                rows.Add(row);
            }
            for (int k = 0; k < nl; k++) for (int i = 0; i < nn; i++)
            {
                int a = rows[k][i], b = rows[k][i + 1], c = rows[k + 1][i + 1], d = rows[k + 1][i];
                if (a == b) gb.tri(a, c, d); else if (c == d) gb.tri(a, b, c); else gb.quad(a, b, c, d);
            }
            // 耳朵：扁椭球，贴图取面部贴图耳旁的肤色
            if (ears)
            {
                foreach (int sd in new[] { 1, -1 })
                {
                    double th = sd * PI / 2 * 0.95, ph = -0.07;
                    var bp = headPoint(th, ph, Hp);
                    double sz = 0.023 * Hp.ear;
                    ellipsoid(gb, V(bp.x - sd * 0.002, bp.y - 0.006 * (Hp.ear - 1), bp.z - 0.012), V(0.007, sz * (1 + 0.5 * (Hp.ear - 1)), sz * 0.58),
                        new FigOpt { nl = nLon >= 24 ? 6 : 4, n = nLon >= 24 ? 10 : 6, uvc = uvOf(th, ph), col = cc, w = w });
                }
            }
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using static FigMath;

    // ------------------------------------------------------------------ 构建选项（SG.Figure.general / soldier 的 opts）
    public sealed class FigOpts
    {
        public string culture;          // 文化（缺省按头像外貌）
        public string color;            // 势力色 '#rrggbb'
        public string lod;              // 'high' | 'mid' | 'low'
        public string weapon;           // 兵器名（缺省按名将设定 / 外貌 / 能力值）
        public bool shieldCult;         // true = 按文化配盾（JS shield: true）
        public string shield;           // 盾名（JS shield: 'scutum' 等）
        public bool mounted;            // 骑乘（配合 Horse 使用）
        public bool shadow = true;
        // 士兵
        public string type;             // 'spear' | 'sword' | 'archer' | 'cavalry'
        public int seed;
        public FigOpts Clone() { return (FigOpts)MemberwiseClone(); }
    }

    // 外观设定里的颜色（= JS look.C）
    public sealed class FigColors
    {
        public FCol faction, cloth, cloth2, metal, trim, leather, skin, plume;
        public FCol? cape, tabard, trousers;
    }

    // 解析后的外观（= JS look）
    public sealed class FigLook
    {
        public string kind, name, culture, sex = "m", factionHex, type, face, key, lod, weapon, shield, ribbons;
        public double age, height, build, fat;
        public int seed;
        public JObj spec, hair, beard, mus, hat, L, sig;
        public string skin;
        public List<object> acc = new List<object>(), marks = new List<object>();
        public FigColors C = new FigColors();
        public bool backBow, quiver;
        public FigLod lodP;             // JS look._lodP（压缩重建时）
        public bool HasAcc(string a) { foreach (var x in acc) if (x as string == a) return true; return false; }
    }

    public static partial class FigBuild
    {
        // ======================================================== 外观设定 ==
        // 文化 → 本表中有条目的代号（先查自身，再查回退文化 Defs.CultureBase，最后 han）
        public static string cultBase(string c) { return Defs.CultureBase(c); }
        public static string cultOf(string c, JObj table) { return c != null && table.Has(c) ? c : table.Has(cultBase(c)) ? cultBase(c) : "han"; }

        // 简化外貌（没有头像模块时；Unity 端头像模块恒在，只在 Portrait.Spec 抛异常时用到）
        public static JObj fallbackSpec(PortraitGen gen)
        {
            string name = string.IsNullOrEmpty(gen.name) ? "无名" : gen.name; var r = new FigRng(hashStr(name));
            double war = gen.war ?? 60, intel = gen.intel ?? 50;
            string cult = gen.culture ?? "han";
            string role = war >= 70 ? "warrior" : intel >= 75 ? "strategist" : "general";
            var skins = new Dictionary<string, string> { { "han", "#e2b48c" }, { "nanman", "#a8714c" }, { "seasia", "#a06c40" }, { "roman", "#e6c09e" }, { "celt", "#f0d0b8" }, { "german", "#ecccb2" },
                { "arab", "#c48e5e" }, { "persia", "#ca9a72" }, { "india", "#a8724a" }, { "africa", "#6c4026" }, { "moor", "#b07e54" } };
            string sk; if (!skins.TryGetValue(cult, out sk)) sk = "#e2b48c";
            var eyes = new[] { "normal", "sharp", "narrow" }; var brows = new[] { "straight", "angled", "bushy" }; var beards = new[] { "none", "short", "goatee", "full" };
            var sp = new JObj
            {
                { "name", name }, { "culture", cult }, { "sex", gen.sex ?? "m" }, { "role", role }, { "age", 34.0 }, { "war", war }, { "intel", intel }, { "pol", gen.pol ?? 50 }, { "fat", 0.0 },
                { "face", new JObj() }, { "skin", sk }, { "build", 1 + (war - 60) / 300 },
            };
            sp["eyes"] = new JObj { { "shape", r.pick(eyes) }, { "color", "#3a2618" } };
            sp["brows"] = new JObj { { "shape", r.pick(brows) }, { "thick", 1.1 }, { "color", "#221a18" } };
            sp["nose"] = new JObj(); sp["mouth"] = new JObj(); sp["ears"] = new JObj();
            sp["hair"] = new JObj { { "style", cult == "roman" ? "short" : "topknot" }, { "color", "#221a18" } };
            sp["beard"] = new JObj { { "style", r.pick(beards) }, { "len", 1.0 }, { "color", "#221a18" } };
            sp["mustache"] = new JObj { { "style", "thin" } };
            sp["hat"] = new JObj { { "type", role == "strategist" ? "jinxian" : cult == "roman" ? "galea" : "helmet" }, { "variant", "round" }, { "plume", "#c0302a" } };
            sp["outfit"] = new JObj { { "type", role == "strategist" ? "robe" : cult == "roman" ? "segmentata" : "lamellar" }, { "color", "#7a2a2a" }, { "color2", "#3a2a2a" }, { "metal", "#7a808c" } };
            sp["acc"] = new List<object>(); sp["marks"] = new List<object>(); sp["weapon"] = null; sp["expr"] = "stern"; sp["key"] = "fb:" + name;
            return sp;
        }
        public static JObj specOf(PortraitGen gen)
        {
            try { return Portrait.Spec(gen); } catch (Exception) { /* 回退 */ }
            return fallbackSpec(gen);
        }
        static string Str(JObj o, string k) { var s = o != null ? o.S(k) : null; return string.IsNullOrEmpty(s) ? null : s; }
        static FCol C(string s) { return col(s); }

        // 武将外观
        public static FigLook lookOfGeneral(PortraitGen gen, FigOpts opts)
        {
            gen = gen ?? new PortraitGen { name = "无名" };
            opts = opts ?? new FigOpts();
            var g2 = gen;
            if (!string.IsNullOrEmpty(opts.culture) && string.IsNullOrEmpty(gen.culture))
                g2 = new PortraitGen { name = gen.name, war = gen.war, intel = gen.intel, pol = gen.pol, born = gen.born, culture = opts.culture, sex = gen.sex, portrait = gen.portrait, color = gen.color };
            var sp = specOf(g2);
            string name = Str(sp, "name") ?? gen.name;
            var sig = FigTables.SIG.O(name ?? "") ?? new JObj();
            string culture = Js.Or(opts.culture, Str(sp, "culture") ?? "han");
            string factionHex = Js.Or(opts.color, Js.Or(gen.color, "#8a2a24"));
            FCol faction = TEAM;
            var of = sp.O("outfit") ?? new JObj();
            string ot = Str(of, "type") != null && FigTables.OUTFIT.Has(of.S("type")) ? of.S("type") : "lamellar";
            var L = new JObj(FigTables.OUTFIT.O(ot));
            double war = sp.Has("war") ? sp.D("war") : 60;
            string metal = Str(sig, "metal") ?? Str(of, "metal") ?? (ot == "plate" ? "#9aa0aa" : "#6a6e78");
            FCol cloth = Str(of, "color") != null ? C(of.S("color")) : faction;
            var hatSrc = sp.O("hat");
            string hatT = (hatSrc != null ? Str(hatSrc, "type") : null) ?? "none";
            var look = new FigLook
            {
                kind = "general", name = name, culture = culture, sex = Str(sp, "sex") ?? "m", age = sp.Or("age", 32.0), spec = sp, factionHex = factionHex,
                height = (sp.S("sex") == "f" ? 1 : 1.0 + clamp((war - 70) / 600, -0.03, 0.05)) * (name == "许褚" || name == "典韦" ? 1.03 : 1),
                build = sp.Or("build", 1.0), fat = sp.Or("fat", 0.0),
                skin = Str(sp, "skin") ?? "#e2b48c",
                hair = JObj.Assign(new JObj { { "style", "topknot" }, { "color", "#1c1816" } }, sp.O("hair")),
                beard = JObj.Assign(new JObj { { "style", "none" }, { "len", 1.0 } }, sp.O("beard")), mus = JObj.Assign(new JObj { { "style", "none" } }, sp.O("mustache")),
                hat = JObj.Assign(new JObj(), hatSrc, new JObj { { "type", Str(FigTables.HAT_MAP, hatT) ?? "none" }, { "ptype", hatT } }),
                acc = sp.L("acc") ?? new List<object>(), marks = sp.L("marks") ?? new List<object>(),
                L = L,
                weapon = Js.Or(opts.weapon, Str(sig, "weapon") ?? (Str(sp, "weapon") != null ? Str(FigTables.PWEAPON, sp.S("weapon")) : null)),
                shield = null, lod = Js.Or(opts.lod, "high"), sig = sig,
                backBow = sig.T("backBow"), quiver = sig.T("quiver"), ribbons = Str(sig, "ribbons"),
            };
            look.C = new FigColors
            {
                faction = faction, cloth = cloth, cloth2 = Str(of, "color2") != null ? C(of.S("color2")) : shade(cloth, -0.35), metal = C(metal), trim = C(Str(of, "trim") ?? GOLD),
                leather = C(culture == "han" ? DLEATHER : LEATHER), skin = C(Str(sp, "skin") ?? "#e2b48c"),
                cape = null, tabard = null, plume = C((hatSrc != null ? Str(hatSrc, "plume") : null) ?? RED), trousers = Str(of, "trousers") != null ? C(of.S("trousers")) : (FCol?)null,
            };
            if (of.T("pelt")) L["pelt"] = of.G("pelt");
            // 白麻袍（阿克苏姆 / 库施）：袍身白色，原衣色作披巾 / 镶边；毛里斗篷：衣色作斗篷，第二色作内衫
            if (L.T("white"))
            {
                look.C.cloth = C(Str(of, "toga") ?? "#ece6d8");
                string oc = Str(of, "color");
                look.C.cloth2 = C(oc != null && !Regex.IsMatch(oc, "^#e[0-9a-f]e[0-9a-f]d[0-9a-f]$", RegexOptions.IgnoreCase) ? oc : (Str(of, "color2") ?? "#a01818"));
            }
            if (ot == "burnous") { look.C.cape = C(Str(of, "color") ?? "#e8e0cc"); look.C.cloth = C(Str(of, "color2") ?? "#c8b48c"); }
            // 袈裟 / 白衣（第三版的僧人）：头像里 outfit.toga 为袍色
            if (ot == "toga" && Str(of, "toga") != null) look.C.cloth = C(of.S("toga"));
            if ((culture == "africa" || culture == "india") && L.S("torso") == "bare" && L.S("legs") == "trousers") { L["legs"] = "bare"; L["boots"] = "sandal"; }
            // 安息 / 萨尔马特将领在三维里披鳞甲；贵霜长袍加鳞片护肩、护胸与护臂
            {
                string cb = cultBase(culture);
                if ((cb == "persia" || cb == "sarmatian") && (ot == "tunic" || ot == "jacket")) { L["torso"] = "scale"; L["pauldron"] = "scale"; L["skirt"] = "scale"; L["skirtLen"] = 0.7; L["sleeves"] = "mail"; L["bracer"] = "metal"; }
                if (cb == "kushan" && ot == "kaftan") { L["pauldron"] = "scale"; L["chest"] = "plate"; L["bracer"] = "metal"; }
            }
            if (Str(sig, "sleeves") != null) L["sleeves"] = sig.S("sleeves");
            if (Str(sig, "pauldron") != null) L["pauldron"] = sig.S("pauldron");
            // 披风 / 前垂颜色
            string capeSpec = Str(sig, "cape") ?? Str(of, "cape") ?? (L.T("cape") ? "faction" : null);
            // 自带披挂的第三版衣装（帔帛、白麻披巾、流苏披肩、斗篷）不再加君主的势力色披风（与头像一致）
            if (capeSpec != null && !(capeSpec == "faction" && (ot == "burnous" || ot == "dhoti" || ot == "shamma" || ot == "meroitic"))) look.C.cape = capeSpec == "faction" ? faction : C(capeSpec);
            // 库施王袍：外披衣色的流苏披肩
            if (ot == "meroitic") look.C.cape = C(Str(of, "color") ?? "#a01818");
            if (L.T("tabard")) look.C.tabard = Str(sig, "tabard") != null ? C(sig.S("tabard")) : (Str(of, "color") != null && ot != "lamellar" ? C(of.S("color")) : faction);
            if (sig.Or("beardLen", 0.0) != 0) look.beard["len"] = sig.D("beardLen");
            // 女性与文官
            if (look.sex == "f" && ot == "robe") L["legs"] = "trousers";
            // 兵器缺省
            if (string.IsNullOrEmpty(look.weapon))
            {
                var cw = FigTables.CULT_WEAPON.O(cultOf(culture, FigTables.CULT_WEAPON));
                var r = new FigRng(hashStr(name + "w"));
                if (look.HasAcc("fan")) look.weapon = "fan";
                else look.weapon = (string)r.pick(cw.L(war >= 80 ? "hi" : war >= 60 ? "mid" : "lo"));
            }
            // 弓：主兵器为刀剑，背弓
            if (look.weapon == "bow") { look.weapon = culture == "han" ? "dao" : (string)FigTables.CULT_WEAPON.O(cultOf(culture, FigTables.CULT_WEAPON)).L("lo")[0]; look.backBow = true; look.quiver = true; }
            // 天竺的「长剑」是坎达剑
            if (look.weapon == "jian" && culture == "india" && Str(sig, "weapon") == null && string.IsNullOrEmpty(opts.weapon)) look.weapon = "khanda";
            // 光头、着袈裟 / 白衣的僧人持锡杖，不佩刀剑
            if (look.hair.S("style") == "bald" && ot == "toga" && Str(sig, "weapon") == null && string.IsNullOrEmpty(opts.weapon) && !sp.T("weapon")) { look.weapon = "staff"; look.backBow = false; look.quiver = false; }
            if (opts.shieldCult) look.shield = Str(FigTables.CULT_SHIELD, cultOf(culture, FigTables.CULT_SHIELD)) ?? "round";
            else if (!string.IsNullOrEmpty(opts.shield)) look.shield = opts.shield;
            return look;
        }

        // 士兵外观：各文化的躯干 / 腰裙 / 冠帽 / 腿 / 盾；按兵种微调
        public static FigLook lookOfSoldier(FigOpts opts)
        {
            string type = Js.Or(opts.type, "spear"), culture = cultOf(opts.culture, FigTables.SOLDIER);
            var S = FigTables.SOLDIER.O(culture); var T = S.O(type) ?? new JObj();
            string factionHex = Js.Or(opts.color, "#8a2a24");
            FCol faction = TEAM;
            int seed = opts.seed % 3;
            var r = new FigRng(unchecked(hashStr(culture + type) + (uint)(seed * 7919)));
            string ot = T.S("outfit") ?? S.S("outfit");
            var L = new JObj(FigTables.OUTFIT.O(ot));
            // 士兵简化：不用胫甲 / 护心镜 / 飘带
            L["greave"] = "none"; L["mirror"] = false;
            if ((culture == "africa" || culture == "india") && L.S("torso") == "bare" && L.S("legs") == "trousers") { L["legs"] = "bare"; L["boots"] = "sandal"; }
            if (type == "archer") { L["pauldron"] = "none"; L["tabard"] = false; }
            var hat = JObj.Assign(new JObj { { "color", null } }, T.O("hat") ?? S.O("hat"));
            hat["type"] = FigTables.HAT_MAP.S(hat.S("type") ?? "") ?? hat.S("type");
            string metal = S.S("metal") ?? "#6a6e78";
            double height = 0.97 + r.range(-0.02, 0.02), build = 1 + r.range(-0.04, 0.06);
            var pick = r.pick(new[] { "#d8a080", "#f0d0b0", "#b88060" });
            string skin = hex(mix(S.S("skin"), pick, r.range(0, 0.18)));
            var look = new FigLook
            {
                kind = "soldier", type = type, culture = culture, name = "", sex = "m", age = 30, spec = null, factionHex = factionHex, seed = seed,
                height = height, build = build, fat = 0, skin = skin,
                hair = new JObj { { "style", S.S("hairStyle") ?? "topknot" }, { "color", S.S("hair") ?? "#1c1816" } },
                beard = new JObj { { "style", new[] { "none", "short", "full", "stubble" }[seed % 4] }, { "len", 1.0 }, { "color", S.S("hair") } }, mus = new JObj { { "style", "none" } },
                hat = hat, acc = new List<object>(), marks = S.L("marks") ?? new List<object>(), face = "face" + "ABCD"[(seed + (culture.Length % 2)) % 4],
                L = L,
                weapon = type == "archer" ? (S.T("longbow") ? "longbow" : "bow") : FigTables.SOLDIER_WEAPON.O(type == "cavalry" ? "cavalry" : type).S(culture),
                shield = type == "sword" ? FigTables.CULT_SHIELD.S(culture) : (culture == "roman" && type == "spear" ? "scutum" : null),
                quiver = type == "archer", lod = Js.Or(opts.lod, "low"),
            };
            look.C = new FigColors
            {
                faction = faction, cloth = faction, cloth2 = shade(faction, -0.4), metal = C(metal), trim = C(culture == "han" || culture == "korea" ? "#b89048" : BRONZE),
                leather = C(LEATHER), skin = C(S.S("skin")), cape = null, tabard = L.T("tabard") ? faction : (FCol?)null, plume = faction,
            };
            if (L.T("cape") && culture == "roman") look.C.cape = C("#a02a24");
            else if (L.T("cape")) look.C.cape = shade(faction, -0.2);
            // 白麻袍的士兵：袍白、镶边 / 披肩为势力色
            if (L.T("white")) { look.C.cloth = C("#e8e2d2"); look.C.cloth2 = faction; }
            if (culture == "india" || culture == "africa") look.beard["style"] = seed == 1 ? "short" : "none";
            if (culture == "celt" || culture == "german" || culture == "nanman" || culture == "seasia" || culture == "yi") look.beard["style"] = seed == 0 ? "none" : "full";
            if (culture == "roman") look.beard["style"] = seed == 2 ? "short" : "none";
            return look;
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    // 躯干截面
    public struct FigRing { public double y, rx, rz, zc, sq; }

    // 构建上下文（= JS makeCtx 的 X）
    public sealed class FigCtx
    {
        public FigLook look; public FigLod lod; public FigProps pr; public FigWeights W; public FigRing[] R; public FigGB gb; public FigColors C; public JObj L; public int hi;
    }

    public static partial class FigBuild
    {
        static FBW[] W1(int b) { return FigBones.W1(b); }
        static int Bn(string n) { return FigBones.B(n); }
        static double[] P2(double a, double b) { return new[] { a, b }; }

        // ======================================================== 身体与衣甲 ==
        static readonly double[][] TORSO_BASE = {
            new[] { 0.84, 0.112, 0.094, 0.000, 2.1 }, new[] { 0.9, 0.146, 0.11, -0.004, 2.3 }, new[] { 0.97, 0.166, 0.118, -0.008, 2.4 }, new[] { 1.04, 0.16, 0.112, -0.004, 2.3 },
            new[] { 1.11, 0.149, 0.106, 0.002, 2.2 }, new[] { 1.19, 0.155, 0.111, 0.006, 2.2 }, new[] { 1.27, 0.168, 0.12, 0.01, 2.3 }, new[] { 1.34, 0.182, 0.126, 0.012, 2.4 },
            // 肩线：1.485–1.54 为斜方肌坡，颈根更粗
            new[] { 1.4, 0.19, 0.121, 0.004, 2.45 }, new[] { 1.445, 0.178, 0.107, -0.008, 2.35 }, new[] { 1.485, 0.148, 0.093, -0.016, 2.2 }, new[] { 1.515, 0.106, 0.077, -0.02, 2.05 }, new[] { 1.545, 0.07, 0.065, -0.022, 2.0 },
        };
        public static FigRing[] torsoProfile(FigProps pr)
        {
            double k = pr.k;
            var R = new FigRing[TORSO_BASE.Length];
            for (int i = 0; i < R.Length; i++)
            {
                var b = TORSO_BASE[i]; double y = b[0], rx = b[1], rz = b[2], zc = b[3], sq = b[4];
                double up = sstep(1.05, 1.4, y);
                double X = rx * lerp(pr.hw, pr.sw, up), Z = rz * lerp(1 + pr.fat * 0.08, 0.86 + 0.14 * pr.build + pr.fat * 0.06, up), Cz = zc;
                if (y > 1.5) X = lerp(X, rx * (1 + (pr.sw - 1) * 0.5), sstep(1.5, 1.545, y));        // 颈根随体格只放宽一半
                double belly = gauss(y, 1.12, 0.12) * pr.fat;
                X += belly * 0.045; Z += belly * 0.1; Cz += belly * 0.07;
                if (pr.fem) { X *= 1 - 0.12 * gauss(y, 1.13, 0.07); Z *= 1 - 0.06 * gauss(y, 1.13, 0.07); }
                R[i] = new FigRing { y = y * k, rx = X * k, rz = Z * k, zc = Cz * k, sq = sq };
            }
            return R;
        }
        public static FigRing ringAt(FigRing[] R, double y)
        {
            if (y <= R[0].y) return R[0];
            if (y >= R[R.Length - 1].y) return R[R.Length - 1];
            int i = 0;
            while (i < R.Length - 2 && R[i + 1].y < y) i++;
            var a = R[Math.Max(0, i - 1)]; var b = R[i]; var c = R[i + 1]; var d = R[Math.Min(R.Length - 1, i + 2)];
            double t = (y - b.y) / (c.y - b.y);
            Func<double, double, double, double, double> cr = (p0, p1, p2, p3) => { double t2 = t * t, t3 = t2 * t; return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3); };
            return new FigRing { y = y, rx = cr(a.rx, b.rx, c.rx, d.rx), rz = cr(a.rz, b.rz, c.rz, d.rz), zc = cr(a.zc, b.zc, c.zc, d.zc), sq = lerp(b.sq, c.sq, t) };
        }
        // 躯干表面点：θ = 0 正前，off 为沿法线的外扩（米），mus 为肌肉起伏强度
        public static V3d torsoPt(FigRing[] R, double y, double th, double off, double mus, bool fem)
        {
            var r = ringAt(R, y); double sn = Math.Sin(th), cs = Math.Cos(th);
            double ex = superE(sn, r.sq), ez = superE(cs, r.sq);
            double nx = sn / r.rx, nz = cs / r.rz;
            double nl = Math.Sqrt(nx * nx + nz * nz); if (nl == 0) nl = 1; nx /= nl; nz /= nl;
            double d = off;
            if (mus != 0)
            {
                double yk = y, a = Math.Abs(th);
                d += mus * (0.011 * gauss(a, 0.48, 0.32) * gauss(yk, 1.335, 0.05)                 // 胸肌
                    - 0.004 * gauss(a, 0.5, 0.35) * gauss(yk, 1.27, 0.012)                          // 胸肌下缘
                    + 0.004 * gauss(a, 0.13, 0.1) * (0.5 + 0.5 * Math.Cos((yk - 1.08) / 0.05 * PI)) * sstep(1.0, 1.06, yk) * (1 - sstep(1.2, 1.26, yk))   // 腹肌
                    - 0.005 * gauss(a, 0, 0.05) * sstep(1.0, 1.05, yk) * (1 - sstep(1.24, 1.28, yk))  // 腹白线
                    + 0.006 * gauss(a, 2.55, 0.35) * gauss(yk, 1.37, 0.06)                          // 肩胛
                    - 0.005 * gauss(a, PI, 0.12) * sstep(1.0, 1.1, yk)                              // 脊沟
                    + 0.006 * gauss(a, 1.25, 0.3) * gauss(yk, 1.2, 0.07));                          // 腹外斜
            }
            if (fem) d += 0.032 * gauss(Math.Abs(th), 0.46, 0.3) * gauss(y, 1.33, 0.055);
            return V(ex * r.rx + nx * d, y, r.zc + ez * r.rz + nz * d);
        }
        public static V3d torsoNormal(FigRing[] R, double y, double th)
        {
            var r = ringAt(R, y); double nx = Math.Sin(th) / r.rx, nz = Math.Cos(th) / r.rz; double l = hypot(nx, nz); if (l == 0) l = 1;
            return V(nx / l, 0, nz / l);
        }
        public static FigCtx makeCtx(FigLook look)
        {
            var lod = look.lodP ?? FigLod.Of(look.lod);
            var pr = makeProps(look);
            return new FigCtx { look = look, lod = lod, pr = pr, W = weightsFor(pr), R = torsoProfile(pr), gb = new FigGB(), C = look.C, L = look.L, hi = lod.detail };
        }
        // 链式权重（按参数 t）：list = [(bone, t0), (bone, t1), ...]
        public static FBW[] wT(double t, params double[] list)
        {
            int n = list.Length / 2;
            if (t <= list[1]) return W1((int)list[0]);
            for (int i = 0; i < n - 1; i++)
            {
                double ab = list[i * 2], at = list[i * 2 + 1], bb = list[i * 2 + 2], bt = list[i * 2 + 3];
                if (t <= bt) { double k = smooth((t - at) / (bt - at)); return new[] { new FBW((int)ab, 1 - k), new FBW((int)bb, k) }; }
            }
            return W1((int)list[(n - 1) * 2]);
        }
        // quilt（第三版絮甲）借用藤编区域
        static readonly Dictionary<string, string> REG_OF = new Dictionary<string, string> { { "lamLace", "lamLace" }, { "lamellar", "lamellar" }, { "plate", "lamellar" }, { "scale", "scale" }, { "segm", "cloth" },
            { "muscle", "metal" }, { "tanko", "cloth" }, { "rattan", "rattan" }, { "cloth", "cloth" }, { "robe", "brocade" }, { "bare", "skin" }, { "fur", "fur" }, { "mail", "mail" }, { "quilt", "rattan" } };
        static string Ls(JObj L, string k, string def) { var s = L.S(k); return string.IsNullOrEmpty(s) ? def : s; }

        // ----------------------------------------------------------- 躯干 --
        static void buildTorso(FigCtx X)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var R = X.R; var C = X.C; var L = X.L; var W = X.W;
            string t = Ls(L, "torso", "cloth");
            string reg; if (!REG_OF.TryGetValue(t, out reg)) reg = "cloth";
            bool bare = t == "bare";
            double mus = (t == "bare" ? 1 : t == "muscle" ? 1.4 : 0) * (1 + clamp((pr.build - 1) * 4, 0, 0.9)) * (1 - pr.fat * 0.5);
            FCol cc = t == "bare" ? C.skin : t == "lamLace" ? C.cloth : (t == "lamellar" || t == "plate" || t == "scale" || t == "muscle" || t == "mail") ? C.metal
                : t == "rattan" ? col("#c8a060") : t == "fur" ? col("#8a6a4a") : (t == "segm" || t == "tanko") ? (X.look.culture == "roman" ? col("#a02a24") : C.cloth) : C.cloth;
            double y0 = R[0].y, y1 = R[R.Length - 1].y + 0.01 * pr.k;
            int n = lod.torK; var secs = new List<FigSec>();
            // 赤膊外罩长袍 / 纱笼：腰下（裙内）的身体画成衬布色
            FCol? lowIn = bare && robeOverBare(L) ? shade(C.cloth, -0.1) : (FCol?)null;
            for (int k = 0; k <= n; k++)
            {
                double y = lerp(y0, y1, Math.Pow((double)k / n, 0.95));
                var r = ringAt(R, y);
                secs.Add(new FigSec { c = V(0, y, r.zc), rx = r.rx, rz = r.rz, sq = r.sq, t = (double)k / n, col = lowIn.HasValue && y < 1.0 * pr.k ? lowIn : null });
            }
            bool segLow = (t == "segm" || t == "tanko") && X.hi == 0;
            loft(gb, secs, new FigOpt { n = lod.torN, reg = segLow ? "rivet" : reg, col = segLow ? C.metal : cc, wf = (p, th, k) => W.torso(p), ptf = (th, s, k) => torsoPt(R, s.c.y, th, 0, mus, pr.fem), capStart = X.hi >= 1 });
            // 环片甲 / 短甲：一圈圈叠压的金属带
            if ((t == "segm" || t == "tanko") && X.hi >= 1)
            {
                var bands = t == "segm" ? new[] { P2(1.04, 1.1), P2(1.1, 1.16), P2(1.16, 1.22), P2(1.22, 1.28), P2(1.29, 1.36), P2(1.37, 1.45) } : new[] { P2(1.0, 1.08), P2(1.09, 1.17), P2(1.18, 1.26), P2(1.27, 1.35), P2(1.36, 1.44) };
                foreach (var b in bands) bandRing(X, b[0] * pr.k, b[1] * pr.k, 0.012, 0.004, "rivet", C.metal, mus * 0, W.torso);
                if (t == "segm")
                {
                    // 胸前搭扣板与肩带
                    foreach (int s in new[] { 1, -1 }) shellPanel(X, new ShellO { th0 = s * 0.05, th1 = s * 0.55, y0 = 1.36, y1 = 1.44, off = 0.02, reg = "rivet", col = C.metal, th = 0.005 });
                }
            }
            // 明光铠：双圆护胸 + 背甲
            if (t == "plate" || L.S("chest") == "plate") chestPlates(X);
            if (t == "muscle") { bandRing(X, 1.0 * pr.k, 1.03 * pr.k, 0.016, 0.006, "goldEngr", C.trim, 0, W.torso); bandRing(X, 1.47 * pr.k, 1.5 * pr.k, 0.012, 0.004, "goldEngr", C.trim, 0, W.torso); }
            // 护心镜（札甲）
            if (L.T("mirror") && t != "plate" && X.hi >= 1) mirrorDisc(X, 0, 1.33, 0.058, 0.026);
            // 交领（袍服）
            if (L.S("collar") == "cross" || t == "robe") crossCollar(X);
            // 胸前斜挎的虎皮 / 豹皮 / 绶带 / 托加
            if (L.T("pelt")) sashBand(X, L.S("pelt") == "leopard" ? "leopard" : "tiger", col("#ffffff"), 0.15, 0.016);
            if (L.T("sash")) sashBand(X, "hem", C.cloth2, 0.07, 0.01);
            if (L.T("toga")) sashBand(X, "hem", L.T("white") ? C.cloth : col("#ece6d8"), 0.3, 0.02);
            // 女性赤膊类衣装：加抹胸
            if (pr.fem && (t == "bare" || t == "rattan")) bandRing(X, 1.25 * pr.k, 1.39 * pr.k, 0.01, 0.002, L.T("pelt") ? (L.S("pelt") == "leopard" ? "leopard" : "tiger") : "cloth", L.T("pelt") ? col("#ffffff") : C.cloth2, 0, W.torso);
            if (L.T("vest")) foreach (int s in new[] { 1, -1 }) shellPanel(X, new ShellO { th0 = s * 0.35, th1 = s * 2.9, y0 = 1.06, y1 = 1.47, off = 0.012, reg = "cloth", col = C.cloth, th = 0.006 });
        }
        sealed class ShellO { public double th0, th1, y0, y1, off, bulge, mus, th; public string reg; public FCol col; public int nu, nv; public bool mirrorS; }
        // 贴在躯干表面的甲片（双面布片）
        static PanelRes shellPanel(FigCtx X, ShellO o)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var R = X.R; var W = X.W;
            double k = pr.k;
            return panel(gb, (s, t) =>
            {
                double th = lerp(o.th0, o.th1, s), y = lerp(o.y0, o.y1, t) * k;
                return torsoPt(R, y, th, (o.off != 0 ? o.off : 0.01) + (o.bulge != 0 ? o.bulge * Math.Sin(PI * s) * Math.Sin(PI * t) : 0), o.mus, pr.fem);
            }, new FigOpt { nu = o.nu != 0 ? o.nu : Math.Max(2, lod.pn), nv = o.nv != 0 ? o.nv : Math.Max(2, (int)jround(lod.pv * 0.6)), th = o.th != 0 ? o.th : 0.006, reg = o.reg, col = o.col, colB = shade(o.col, -0.4), wP = (p, s, t) => W.torso(p), mirrorS = o.mirrorS });
        }
        // 一圈带子（腰带、甲带、包边）：y0..y1，外扩 off，下缘多外扩 flare
        static void bandRing(FigCtx X, double y0, double y1, double off, double flare, string reg, FCol colr, double mus, Func<V3d, FBW[]> wf)
        {
            var R = X.R; var pr = X.pr;
            var secs = new List<FigSec>();
            bool low = X.hi == 0;
            var yy = low ? new[] { y0, y1 } : new[] { y0, y0 + (y1 - y0) * 0.15, y1 - (y1 - y0) * 0.15, y1 };
            var oo = low ? new[] { off + flare * 0.6, off } : new[] { off * 0.55 + flare, off + flare * 0.6, off, off * 0.55 };
            for (int i = 0; i < yy.Length; i++) secs.Add(new FigSec { c = V(0, yy[i], 0), rx = 1, rz = 1, t = (double)i / (yy.Length - 1), off2 = oo[i] });
            var w = wf ?? X.W.torso;
            loft(X.gb, secs, new FigOpt { n = X.lod.torN, reg = reg, col = colr, wf = (p, th, k) => w(p), ptf = (th, s, k) => torsoPt(R, s.c.y, th, s.off2, 0, pr.fem) });
        }
        static void chestPlates(FigCtx X)
        {
            var C = X.C; int hi = X.hi;
            foreach (int s in new[] { 1, -1 })
            {
                shellPanel(X, new ShellO { th0 = s * 0.06, th1 = s * 1.2, y0 = 1.2, y1 = 1.43, off = 0.016, bulge = 0.008, reg = "metal", col = C.metal, th = 0.008 });
                if (hi >= 1) mirrorDisc(X, s * 0.55, 1.315, 0.05, 0.03);
            }
            // 背甲
            shellPanel(X, new ShellO { th0 = 1.9, th1 = TAU - 1.9, y0 = 1.18, y1 = 1.44, off = 0.014, reg = "metal", col = C.metal, th = 0.008 });
            // 胸前横带（束甲绊）
            bandRing(X, 1.185 * X.pr.k, 1.215 * X.pr.k, 0.03, 0.002, "leather", shade(C.cloth2, -0.1), 0, X.W.torso);
        }
        // 圆镜：在躯干表面 (θ, y) 处的扁圆盘
        static void mirrorDisc(FigCtx X, double th, double y, double r, double off)
        {
            var gb = X.gb; var lod = X.lod; var R = X.R; var pr = X.pr; var C = X.C;
            double yy = y * pr.k;
            var c = torsoPt(R, yy, th, off, 0, pr.fem); var n = torsoNormal(R, yy, th);
            var AX = axesFromY(n);
            var prof = new[] { new[] { r, -0.008, double.NaN, 0.0 }, new[] { r * 1.02, 0.0, double.NaN, 0.03 }, new[] { r * 0.96, 0.006, double.NaN, 0.1 }, new[] { r * 0.6, 0.009, double.NaN, 0.5 }, new[] { 0, 0.011, double.NaN, 1 } };
            int start = gb.nv;
            lathe(gb, prof, new FigOpt { c = c, axes = AX, n = lod.ring, reg = "mirror", col = C.trim == col(GOLD) ? col("#e8e0c8") : C.metal, w = X.W.torso(c) });
            fixDiscUV(gb, FigAtlasLayout.REG("mirror"), c, AX, r, start);
        }
        // 由一个方向（作为 Y）构造正交轴，满足 Z × X = Y
        public static FigAxes axesFromY(V3d Y)
        {
            Y = v3norm(Y);
            var rf = Math.Abs(Y.y) < 0.9 ? V(0, 1, 0) : V(0, 0, 1);
            var Xa = v3norm(v3cross(rf, Y)); var Za = v3cross(Xa, Y);
            return new FigAxes(Xa, Y, Za);
        }
        // 把最近构建的圆盘顶点改为平面投影 UV
        static void fixDiscUV(FigGB gb, FigRegion reg, V3d c, FigAxes AX, double r, int start)
        {
            for (int i = start; i < gb.nv; i++)
            {
                var p = V(gb.P[i * 3] - c.x, gb.P[i * 3 + 1] - c.y, gb.P[i * 3 + 2] - c.z);
                double u = 0.5 + 0.5 * v3dot(p, AX.X) / (r * 1.04), v = 0.5 + 0.5 * v3dot(p, AX.Z) / (r * 1.04);
                var q = ruv(reg, u, v);
                gb.UV[i * 2] = q[0]; gb.UV[i * 2 + 1] = q[1];
            }
        }
        static void crossCollar(FigCtx X)
        {
            var C = X.C;
            // 左襟压右襟：从左肩斜到右腰
            bandPath(X, new[] { P2(0.55, 1.5), P2(0.25, 1.4), P2(-0.15, 1.27), P2(-0.55, 1.13) }, 0.045, 0.014, "hem", C.cloth2);
            bandPath(X, new[] { P2(-0.55, 1.5), P2(-0.3, 1.42), P2(-0.08, 1.36) }, 0.04, 0.009, "hem", C.cloth2);
        }
        // 沿躯干表面路径（[θ, y] 点列）的一条带子
        static void bandPath(FigCtx X, double[][] path, double width, double off, string reg, FCol colr)
        {
            var gb = X.gb; var lod = X.lod; var R = X.R; var pr = X.pr; var W = X.W;
            double k = pr.k;
            int seg = Math.Max(3, (path.Length - 1) * (lod.detail + 1) * 2);
            Func<double, double[]> at = s => { double f = s * (path.Length - 1); int i = Math.Min(path.Length - 2, (int)Math.Floor(f)); double t = f - i; return P2(lerp(path[i][0], path[i + 1][0], t), lerp(path[i][1], path[i + 1][1], t) * k); };
            panel(gb, (s, t) =>
            {
                var a = at(t); var b = at(Math.Min(1, t + 0.02)); var a2 = at(Math.Max(0, t - 0.02));
                // 横向：在 (θ, y) 平面中垂直于路径方向
                double dth = b[0] - a2[0], dy = b[1] - a2[1];
                double rr = ringAt(R, a[1]).rx;
                double l = hypot(dth * rr, dy); if (l == 0) l = 1;
                double nth = -dy / l / rr * width, ny = dth * rr / l * width;
                // 带子不超出肩顶
                double th = a[0] + (s - 0.5) * nth * 2, y = Math.Min(a[1] + (s - 0.5) * ny * 2, 1.49 * k);
                return torsoPt(R, y, th, off, 0, pr.fem);
            }, new FigOpt { nu = 1, nv = seg, th = 0.005, reg = reg, col = colr, wP = (p, s, t) => W.torso(p), uv = (s, t) => P2(t, s) });
        }
        static void sashBand(FigCtx X, string reg, FCol colr, double width, double off)
        {
            // 从右腰前 → 左肩 → 背后右腰（闭合一圈的斜带）
            bandPath(X, new[] { P2(-0.75, 1.05), P2(-0.1, 1.24), P2(0.45, 1.42), P2(1.1, 1.5), P2(1.7, 1.49), P2(2.4, 1.38), P2(2.95, 1.2), P2(3.6, 1.05) }, width, off, reg, colr);
        }

        // ------------------------------------------------------------- 颈 --
        static void buildNeck(FigCtx X)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var W = X.W; var C = X.C; var look = X.look;
            double k = pr.k;
            double nk = look.spec != null ? look.spec.D("neck") : double.NaN;
            double r = 0.056 * (!double.IsNaN(nk) && nk != 0 ? clamp(0.82 + 0.22 * nk, 0.92, 1.25) : 1) * (pr.fem ? 0.82 : 1) * (1 + (pr.sw - 1) * 0.35) * k;
            var secs = new List<FigSec>();
            var ys = X.hi == 0 ? new[] { 1.5, 1.63 } : new[] { 1.5, 1.545, 1.59, 1.63 };
            for (int i = 0; i < ys.Length; i++) secs.Add(new FigSec { c = V(0, ys[i] * k, (-0.02 + i * 0.004) * k), rx = r * (i == 0 ? 1.3 : i == 1 ? 1.06 : 1), rz = r * (i == 0 ? 1.12 : 0.96), t = (double)i / (ys.Length - 1) });
            loft(gb, secs, new FigOpt { n = Math.Max(6, lod.limbN), reg = "skin", col = C.skin, wf = (p, th, kk) => W.neck(p) });
            string cl = X.L.S("collar");
            if ((cl == "scarf" || cl == "fur" || cl == "gold") && X.hi >= 1)
            {
                // 领巾 / 毛领 / 金项圈：贴着躯干颈根表面外扩，上缘一道圆边围住颈根
                string reg = cl == "fur" ? "fur" : cl == "gold" ? "goldEngr" : "cloth";
                FCol cc = cl == "fur" ? col("#8a6a48") : cl == "gold" ? col(GOLD) : (X.look.kind == "general" ? (C.tabard ?? C.faction) : C.faction);
                double th = cl == "fur" ? 0.022 : 0.012;
                bandRing(X, (cl == "gold" ? 1.44 : 1.462) * k, 1.545 * k, th, cl == "gold" ? 0.01 : 0.006, reg, cc, 0, W.neck);
                var top = ringAt(X.R, 1.545 * k);
                tube(gb, ringPts(V(0, 1.545 * k, top.zc), V(1, 0, 0), V(0, 0, 1), top.rx + th, top.rz + th, Math.Max(8, lod.limbN)), new FigOpt { r = (cl == "fur" ? 0.014 : 0.008) * k, n = X.hi >= 2 ? 6 : 4, reg = reg, col = cc, w = W.neck(V(0, 1.545 * k, 0)) });
            }
        }

        // ------------------------------------------------------------- 臂 --
        static V3d armPt(FigProps pr, string S, double s)
        {
            double sg = S == "L" ? 1 : -1;
            var d = V(pr.armDirL.x * sg, pr.armDirL.y, 0); double L = pr.upL + pr.foL;
            return v3madd(pr["upperArm" + S], d, s * L);
        }
        static double[] armR(double s)
        {
            // 肩头：半球形收口
            if (s < -0.02) { double q = Math.Sqrt(Math.Max(0, 1 - Math.Pow((s + 0.02) / 0.1, 2))); return P2(0.064 * q, 0.066 * q); }
            var A = FigureData.ARM_R;
            for (int i = 0; i < A.Length - 1; i++)
            {
                var a = A[i]; var b = A[i + 1];
                if (s <= b[0]) { double t = smooth((s - a[0]) / (b[0] - a[0])); return P2(lerp(a[1], b[1], t), lerp(a[2], b[2], t)); }
            }
            return P2(A[A.Length - 1][1], A[A.Length - 1][2]);
        }
        static void buildArm(FigCtx X, string S)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var W = X.W; var C = X.C; var L = X.L;
            double sg = S == "L" ? 1 : -1, k = pr.k * pr.limb;
            var d = V(pr.armDirL.x * sg, pr.armDirL.y, 0); var Z = V(0, 0, 1); var Xa = v3cross(d, Z);
            string sl = Ls(L, "sleeves", "cloth");
            var segs = new List<object[]>();
            if (sl == "bare" || (X.hi == 0 && sl == "short")) segs.Add(new object[] { "skin", -0.12, 1.0, 1.0 });
            else if (sl == "short") { segs.Add(new object[] { "cloth", -0.12, 0.3, 1.28 }); segs.Add(new object[] { "skin", 0.3, 1.0, 1.0 }); }
            else if (sl == "mail") { segs.Add(new object[] { "mail", -0.12, 0.62, 1.12 }); segs.Add(new object[] { "skin", 0.62, 1.0, 1.0 }); }
            else if (sl == "wide") segs.Add(new object[] { "cloth", -0.12, 1.0, 1.35 });
            else segs.Add(new object[] { "cloth", -0.12, 1.0, 1.22 });
            bool musc = sl == "bare" || sl == "short";
            foreach (var sgm in segs)
            {
                string reg = (string)sgm[0]; double s0 = (double)sgm[1], s1 = (double)sgm[2], sc = (double)sgm[3];
                int n = X.hi == 0 ? Math.Max(1, (int)jround(3 * (s1 - s0))) : Math.Max(2, (int)jround(lod.armK * (s1 - s0)));
                // 截面：肩头半球处加密，其余均匀
                double[] cap = s0 <= -0.11 ? (X.hi >= 2 ? new[] { -0.12, -0.112, -0.098, -0.078, -0.052, -0.02 } : X.hi == 1 ? new[] { -0.12, -0.1, -0.055, -0.02 } : new[] { -0.12, -0.06, -0.02 }) : null;
                var ss = new List<double>();
                if (cap != null) { foreach (var c in cap) ss.Add(c); int m = Math.Max(1, (int)jround(n * (s1 + 0.02) / (s1 - s0)) - (X.hi == 0 ? 1 : 0)); for (int i = 1; i <= m; i++) ss.Add(lerp(-0.02, s1, (double)i / m)); }
                else for (int i = 0; i <= n; i++) ss.Add(lerp(s0, s1, (double)i / n));
                var secs = new List<FigSec>();
                for (int i = 0; i < ss.Count; i++)
                {
                    double s = ss[i];
                    var rr = armR(s); double rx = rr[0], rz = rr[1];
                    var c = armPt(pr, S, s);
                    double g = sc;
                    if (sl == "wide" && reg == "cloth")
                    {
                        // 广袖：自肘起外张、下垂
                        double f = sstep(0.35, 1.0, s);
                        g = sc + f * 1.6;
                        c = v3add(c, V(0, -f * 0.05 * pr.k, f * 0.01));
                    }
                    if (reg == "cloth" && i == ss.Count - 1 && s1 < 1) g *= 1.1;
                    bool pole = s <= -0.115;
                    secs.Add(new FigSec { c = c, X = Xa, Z = Z, rx = pole ? 0 : rx * k * g, rz = pole ? 0 : rz * k * g, t = (s - s0) / (s1 - s0), pole = pole });
                }
                FCol ccol = reg == "skin" ? C.skin : reg == "mail" ? C.metal : (sl == "wide" ? C.cloth : (X.look.kind == "general" ? C.cloth : shade(C.faction, -0.15)));
                var wa = W.arm(S);
                Func<double, FigSec, int, double> offF = null;
                if (musc && reg == "skin")
                    offF = (th, sec, kk) =>
                    {
                        double s = v3dot(v3sub(sec.c, pr["upperArm" + S]), d) / (pr.upL + pr.foL); double mk = 1 + clamp((pr.build - 1) * 5, 0, 1);
                        return k * mk * (0.008 * gauss(th, 0, 0.7) * gauss(s, 0.3, 0.1) + 0.006 * gauss(Math.Abs(th), PI, 0.8) * gauss(s, 0.24, 0.12) + 0.01 * gauss(s, 0.04, 0.07) + 0.005 * gauss(s, 0.66, 0.08));
                    };
                loft(gb, secs, new FigOpt { n = lod.limbN, reg = reg, col = ccol, wf = (p, th, kk) => wa(p), off = offF });
                if (reg == "cloth" && s1 < 1)
                {
                    // 袖口包边
                    var ap = armPt(pr, S, s1);
                    tube(gb, ringPts(ap, Xa, Z, armR(s1)[0] * k * sc * 1.1, armR(s1)[1] * k * sc * 1.1, lod.limbN), new FigOpt { r = 0.006 * pr.k, n = 4, reg = "goldEngr", col = C.trim, w = wa(ap) });
                }
            }
            // 臂钏（天竺、库施）：露臂时上臂一道金箍
            if (L.T("armlet") && X.hi >= 1 && (sl == "bare" || sl == "short"))
            {
                double s = sl == "short" ? 0.36 : 0.3; var rr = armR(s); var ap = armPt(pr, S, s);
                tube(gb, ringPts(ap, Xa, Z, rr[0] * k * 1.08 + 0.003, rr[1] * k * 1.08 + 0.003, lod.limbN), new FigOpt { r = 0.006 * pr.k, n = 4, reg = "gold", col = col(GOLD), w = W.arm(S)(ap) });
            }
            // 护臂
            string br = X.hi == 0 ? "none" : Ls(L, "bracer", "none");
            if (br != "none")
            {
                string reg = br == "leather" ? "leather" : br == "gold" ? "gold" : "metal";
                FCol cc = br == "leather" ? C.leather : br == "gold" ? col(GOLD) : C.metal;
                double s0 = br == "gold" ? 0.86 : 0.6, s1 = 0.97;
                int n = Math.Max(2, (int)jround(lod.armK * 0.5));
                var secs = new List<FigSec>();
                for (int i = 0; i <= n; i++)
                {
                    double s = lerp(s0, s1, (double)i / n);
                    var rr = armR(s);
                    double fl = br == "gold" ? 1.15 : 1.18 + 0.12 * sstep(0.85, 0.97, s);
                    secs.Add(new FigSec { c = armPt(pr, S, s), X = Xa, Z = Z, rx = rr[0] * k * fl + 0.004, rz = rr[1] * k * fl + 0.004, t = (s - s0) / (s1 - s0) });
                }
                var wa = W.arm(S);
                loft(gb, secs, new FigOpt { n = lod.limbN, reg = reg, col = cc, wf = (p, th, kk) => wa(p), u0 = 0, u1 = 0.98 });
                if (X.hi >= 1 && br == "metal") foreach (double s in new[] { s0, s1 }) { var ap = armPt(pr, S, s); tube(gb, ringPts(ap, Xa, Z, armR(s)[0] * k * 1.24 + 0.005, armR(s)[1] * k * 1.24 + 0.005, lod.limbN), new FigOpt { r = 0.0045 * pr.k, n = 4, reg = "goldEngr", col = C.trim, w = wa(ap) }); }
            }
            buildHand(X, S);
        }
        // 截面环的点列（闭合，用于包边管）
        public static List<V3d> ringPts(V3d c, V3d Xa, V3d Z, double rx, double rz, int n)
        {
            var pts = new List<V3d>(n + 1);
            for (int i = 0; i <= n; i++) { double a = (double)i / n * TAU; pts.Add(v3add(c, v3add(v3mul(Xa, Math.Sin(a) * rx), v3mul(Z, Math.Cos(a) * rz)))); }
            return pts;
        }
        static void buildHand(FigCtx X, string S)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var C = X.C; var L = X.L; var look = X.look;
            var m = socketRest(pr, S);
            var save = gb.xf;
            gb.xf = m;
            bool gloved = look.kind == "general" && L.S("bracer") == "metal";
            string reg = gloved ? "leather" : "skin"; FCol cc = gloved ? col(DLEATHER) : C.skin;
            double k = pr.k * (pr.fem ? 0.9 : 1) * (0.94 + 0.06 * pr.limb);
            var w = W1(Bn("hand" + S));
            if (X.hi >= 2)
            {
                // 高精度：握拳的手 = 手背 / 掌（圆角块）+ 四指（绕握柄卷曲的圆管）+ 拇指
                double sb = S == "L" ? 1 : -1;            // 手背所在的 X 符号
                double R = 0.026 * k; var fl = new[] { 0.0105, 0.0112, 0.0108, 0.0095 };
                ellipsoid(gb, V(sb * 0.017 * k, -0.002 * k, -0.034 * k), V(0.017 * k, 0.043 * k, 0.046 * k), new FigOpt { nl = 6, n = 10, reg = reg, col = cc, w = w, sq = 3.4 });
                for (int i = 0; i < 4; i++)
                {
                    double y = (0.03 - i * 0.02) * k, rf = fl[i] * k; var pts = new List<V3d>();
                    double a0 = -0.95, a1 = 2.45 - i * 0.05;     // 自指根（手背前方）经 +Z 绕到掌心一侧
                    for (int j = 0; j <= 5; j++) { double a = lerp(a0, a1, j / 5.0), rr = R + rf * 0.25 * (1 - j / 5.0); pts.Add(V(-sb * Math.Sin(a) * rr, y, Math.Cos(a) * rr - 0.002 * k)); }
                    pts[0] = V(sb * 0.012 * k, y, -0.006 * k);
                    double rfc = rf;
                    tube(gb, pts, new FigOpt { rF = (q, ii) => rfc * (1 - 0.18 * q), n = 6, reg = reg, col = cc, w = w, up = V(0, 1, 0), capEnd = true });
                }
                // 拳心（空手时填住握柄留下的孔）
                ellipsoid(gb, V(0, -0.004 * k, 0), V(0.016 * k, 0.036 * k, 0.016 * k), new FigOpt { nl = 4, n = 6, reg = reg, col = cc, w = w });
                // 拇指：自掌根绕过握柄压在食指第二节上
                var tp = new List<V3d> { V(-sb * 0.006 * k, 0.034 * k, -0.06 * k), V(-sb * 0.024 * k, 0.04 * k, -0.034 * k), V(-sb * 0.03 * k, 0.046 * k, -0.006 * k), V(-sb * 0.018 * k, 0.047 * k, 0.016 * k) };
                tube(gb, tp, new FigOpt { rF = (q, ii) => (0.012 - 0.002 * q) * k, n = 6, reg = reg, col = cc, w = w, up = V(0, 1, 0), capEnd = true });
            }
            else
            {
                ellipsoid(gb, V(0, 0, -0.014 * k), V(0.03 * k, 0.047 * k, 0.052 * k), new FigOpt { nl = lod.hand[0], n = lod.hand[1], reg = reg, col = cc, w = w, sq = X.hi >= 1 ? 3.2 : 2 });
                // 拇指压在握柄上
                if (X.hi >= 1) tube(gb, new List<V3d> { V(0.018 * k, 0.03 * k, -0.045 * k), V(0.03 * k, 0.04 * k, -0.012 * k), V(0.022 * k, 0.046 * k, 0.012 * k) }, new FigOpt { rF = (s, ii) => (0.013 - 0.003 * s) * k, n = 6, reg = reg, col = cc, w = w });
            }
            gb.xf = save;
        }

        // ------------------------------------------------------------- 腿 --
        static double[] legR(double s)
        {
            var A = FigureData.LEG_R;
            for (int i = 0; i < A.Length - 1; i++) { var a = A[i]; var b = A[i + 1]; if (s <= b[0]) { double t = smooth((s - a[0]) / (b[0] - a[0])); return P2(lerp(a[1], b[1], t), lerp(a[2], b[2], t)); } }
            return P2(A[A.Length - 1][1], A[A.Length - 1][2]);
        }
        static V3d legPt(FigProps pr, string S, double s)
        {
            var h = pr["thigh" + S]; var kn = pr["shin" + S]; var an = pr["foot" + S];
            if (s <= 0.5) return v3lerp(h, kn, s / 0.5);
            return v3lerp(kn, an, (s - 0.5) / 0.5);
        }
        struct LegF { public V3d X, Z, d; }
        static LegF legFrame(FigProps pr, string S, double s)
        {
            var d = s <= 0.5 ? v3norm(v3sub(pr["shin" + S], pr["thigh" + S])) : v3norm(v3sub(pr["foot" + S], pr["shin" + S]));
            var Z = v3norm(v3sub(V(0, 0, 1), v3mul(d, d.z)));
            return new LegF { X = v3cross(d, Z), Z = Z, d = d };
        }
        static void buildLeg(FigCtx X, string S)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var W = X.W; var C = X.C; var L = X.L; var look = X.look;
            double k = pr.k * pr.limb;
            string lg = Ls(L, "legs", "trousers");
            double bootTop = L.S("boots") == "boot" ? 0.74 : 2;
            var segs = new List<object[]>();   // reg, s0, s1, sc, col, nFix
            // 第三版修订：长袍 / 长裙（长过膝）下的赤腿——裙内一段画成同色的衬布（略收细），下摆以下才露肤色
            bool robeSk = robeOverBare(L);
            if (robeSk)
            {
                double yHem = Math.Max(L.S("skirt") == "robe" ? 0.1 : 0, 1.04 - L.D("skirtLen")) * pr.k + 0.05 * pr.k;
                double sH = 1;
                for (double s = 0.4; s <= 1.0001; s += 0.02) if (legPt(pr, S, s).y <= yHem) { sH = s; break; }
                string ir = L.S("skirt") == "sarong" ? "brocade" : "cloth";
                FCol inner = shade(C.cloth, -0.1);
                // 两段的环数之和 = 原来整条腿的环数
                int nT = Math.Max(2, (int)jround(lod.legK * 1.05)), n1 = (int)clamp(jround(nT * (sH + 0.05) / 1.05), 1, nT - 1);
                if (sH > 0.9) segs.Add(new object[] { ir, -0.05, 1.0, 0.97, inner, 0 });
                else { segs.Add(new object[] { ir, -0.05, sH, 0.97, inner, n1 }); segs.Add(new object[] { "skin", sH, 1.0, 1.0, C.skin, nT - n1 }); }
            }
            else if (lg == "bare") segs.Add(new object[] { "skin", -0.05, 1.0, 1.0, C.skin, 0 });
            else if (lg == "hakama") { segs.Add(new object[] { "cloth", -0.05, 0.56, 1.45, C.cloth2, 0 }); segs.Add(new object[] { "skin", 0.56, 1.0, 1.0, C.skin, 0 }); }
            else segs.Add(new object[] { Ls(L, "legsReg", "cloth"), -0.05, Math.Min(1, bootTop + 0.04), 1.28, L.S("legsReg") == "plaid" ? C.cloth : (look.kind == "general" ? (C.trousers ?? shade(C.cloth, -0.55)) : shade(C.cloth2, 0.05)), 0 });
            var wl = W.leg(S);
            foreach (var sgm in segs)
            {
                string reg = (string)sgm[0]; double s0 = (double)sgm[1], s1 = (double)sgm[2], sc = (double)sgm[3]; FCol cc = (FCol)sgm[4]; int nFix = (int)sgm[5];
                int n = nFix != 0 ? nFix : Math.Max(2, (int)jround(lod.legK * (s1 - s0)));
                var secs = new List<FigSec>();
                for (int i = 0; i <= n; i++)
                {
                    double s = lerp(s0, s1, (double)i / n);
                    var F = legFrame(pr, S, s);
                    var rr = legR(s); double rx = rr[0], rz = rr[1];
                    double g = sc;
                    // 裤：大腿根贴身、膝下宽松后收进靴；袍内衬布：大腿至膝收细，近踝略收
                    if (robeSk && reg != "skin") g = sc * lerp(0.78, 1, sstep(0.42, 0.72, s)) * (1 - 0.06 * sstep(0.7, 1, s));
                    else if (reg != "skin") g = lerp(1.02, sc, sstep(0.3, 0.6, s)) * (1 - 0.15 * sstep(0.55, 1, s)) + (lg == "hakama" ? 0.15 * sstep(0.3, 0.5, s) * (1 - sstep(0.5, 0.56, s)) : 0);
                    secs.Add(new FigSec { c = legPt(pr, S, s), X = F.X, Z = F.Z, rx = rx * k * g, rz = rz * k * g, t = (s - s0) / (s1 - s0) });
                }
                Func<double, FigSec, int, double> offF = null;
                if (reg == "skin") { double S0 = s0, S1 = s1; int N0 = n; offF = (th, sec, i) => { double s = lerp(S0, S1, (double)i / N0); return k * (0.008 * gauss(s, 0.66, 0.08) * gauss(Math.Abs(th), PI * 0.75, 0.7) + 0.006 * gauss(s, 0.22, 0.12) * gauss(th, 0, 0.8)); }; }
                loft(gb, secs, new FigOpt { n = lod.limbN, reg = reg, col = cc, wf = (p, th, kk) => wl(p), off = offF });
            }
            // 靴
            if (L.S("boots") == "boot")
            {
                int n = X.hi == 0 ? 1 : Math.Max(2, (int)jround(lod.legK * 0.3)); var secs = new List<FigSec>();
                for (int i = 0; i <= n; i++)
                {
                    double s = lerp(bootTop, 1.0, (double)i / n); var F = legFrame(pr, S, s);
                    var rr = legR(s);
                    double g = 1.18 + (i == 0 ? 0.08 : 0);
                    secs.Add(new FigSec { c = legPt(pr, S, s), X = F.X, Z = F.Z, rx = rr[0] * k * g + 0.004, rz = rr[1] * k * g + 0.004, t = (double)i / n });
                }
                loft(gb, secs, new FigOpt { n = lod.limbN, reg = "leather", col = col(DLEATHER), wf = (p, th, kk) => wl(p) });
                if (X.hi >= 1) { var F = legFrame(pr, S, bootTop); var rr = legR(bootTop); var lp = legPt(pr, S, bootTop); tube(gb, ringPts(lp, F.X, F.Z, rr[0] * k * 1.27 + 0.004, rr[1] * k * 1.27 + 0.004, lod.limbN), new FigOpt { r = 0.007 * pr.k, n = 4, reg = "leather", col = shade(DLEATHER, 0.2), w = wl(lp) }); }
            }
            // 胫甲 + 护膝
            if (L.S("greave") == "metal")
            {
                double s0 = 0.54, s1 = 0.9;
                panel(gb, (u, v) =>
                {
                    double s = lerp(s0, s1, v); var F = legFrame(pr, S, s); var rr = legR(s);
                    double th = lerp(-1.7, 1.7, u), g = 1.32;
                    return v3add(legPt(pr, S, s), v3add(v3mul(F.X, Math.Sin(th) * (rr[0] * k * g + 0.006)), v3mul(F.Z, Math.Cos(th) * (rr[1] * k * g + 0.008))));
                }, new FigOpt { nu = Math.Max(2, lod.pn), nv = Math.Max(2, lod.pv - 3), th = 0.006, reg = "metal", col = C.metal, wP = (p, s, t) => wl(p), mirrorS = true });
                var kp = v3add(legPt(pr, S, 0.5), V(0, 0.0, 0.065 * k));
                ellipsoid(gb, kp, V(0.045 * k, 0.05 * k, 0.025 * k), new FigOpt { nl = Math.Max(3, lod.hand[0] - 2), n = lod.hand[1], reg = "metal", col = C.metal, w = wl(kp) });
            }
            // 凉鞋绑带
            if (L.S("boots") == "sandal" && X.hi >= 1) foreach (double s in new[] { 0.8, 0.88, 0.96 }) { var F = legFrame(pr, S, s); var rr = legR(s); var lp = legPt(pr, S, s); tube(gb, ringPts(lp, F.X, F.Z, rr[0] * k * 1.04 + 0.002, rr[1] * k * 1.04 + 0.002, lod.limbN), new FigOpt { r = 0.004 * pr.k, n = 3, reg = "leather", col = col(LEATHER), w = wl(lp) }); }
            buildFoot(X, S);
        }
        static void buildFoot(FigCtx X, string S)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var C = X.C; var L = X.L;
            double k = pr.k; var a = pr["foot" + S];
            string boots = L.S("boots");
            bool bare = boots == "sandal" || boots == "bare";
            string reg = bare ? "skin" : boots == "shoe" ? "cloth" : "leather"; FCol cc = bare ? C.skin : boots == "shoe" ? col("#2a2420") : col(DLEATHER);
            var st = X.hi == 0 ? new[] { new[] { -0.07, 0, 0, 0.03 }, new[] { -0.03, 0.046, 0.06, 0.055 }, new[] { 0.1, 0.05, 0.032, 0.03 }, new[] { 0.17, 0, 0, 0.022 } }
                : new[] { new[] { -0.07, 0, 0, 0.03 }, new[] { -0.058, 0.036, 0.04, 0.042 }, new[] { -0.02, 0.046, 0.066, 0.06 }, new[] { 0.04, 0.05, 0.05, 0.045 }, new[] { 0.1, 0.05, 0.032, 0.03 }, new[] { 0.15, 0.043, 0.024, 0.024 }, new[] { 0.175, 0, 0, 0.022 } };
            var secs = new List<FigSec>();
            for (int i = 0; i < st.Length; i++)
            {
                var q = st[i];
                secs.Add(new FigSec { c = V(a.x, q[3] * k, a.z + q[0] * k), X = V(-1, 0, 0), Z = V(0, 1, 0), rx = q[1] * k * (bare ? 0.92 : 1.06), rz = q[2] * k * (bare ? 0.9 : 1.05), t = (double)i / (st.Length - 1), pole = q[1] == 0, sq = 2.6 });
            }
            int bf = Bn("foot" + S), bs = Bn("shin" + S);
            loft(gb, secs, new FigOpt { n = X.hi == 0 ? 6 : Math.Max(6, lod.foot), reg = reg, col = cc, wf = (p, th, kk) => p.y > 0.075 * k ? new[] { new FBW(bf, 0.6), new FBW(bs, 0.4) } : W1(bf) });
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    public static partial class FigBuild
    {
        // ------------------------------------------------------------- 腰裙 --
        sealed class SkirtO
        {
            public double th0, th1, yTop, yBot, off, flare, curve, wave, waves, th; public string reg; public FCol col;
            public Func<V3d, double, double, FBW[]> w; public Func<double, double, double[]> uv; public bool mirrorS; public bool edge = true; public int nu, nv;
        }
        // 一片裙甲：θ0..θ1，从腰（yTop）到下摆（yBot），下摆外张 flare；权重：上 = 骨盆，下 = 大腿 / 前后垂骨
        static PanelRes skirtPanel(FigCtx X, SkirtO o)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var R = X.R;
            double k = pr.k;
            return panel(gb, (s, t) =>
            {
                double th = lerp(o.th0, o.th1, s);
                double y = lerp(o.yTop, o.yBot, t) * k - o.curve * Math.Pow(Math.Abs(Math.Sin(th)), 2) * t * k;
                var top = torsoPt(R, Math.Max(R[0].y + 0.02, o.yTop * k), th, o.off, 0, false);
                double fl = (o.flare != 0 ? o.flare : 0.06) * Math.Pow(t, 1.15) * k;
                var n = torsoNormal(R, o.yTop * k, th);
                double wav = o.wave != 0 ? o.wave * Math.Sin(s * PI * (o.waves != 0 ? o.waves : 3)) * t : 0;
                return V(top.x + n.x * (fl + wav), y, top.z + n.z * (fl + wav));
            }, new FigOpt { nu = o.nu != 0 ? o.nu : Math.Max(2, lod.pn), nv = o.nv != 0 ? o.nv : Math.Max(2, lod.pv - 2), th = o.th != 0 ? o.th : 0.007, reg = o.reg, col = shade(o.col, -0.4), colB = o.col, wP = o.w, uv = o.uv, mirrorS = o.mirrorS, edge = o.edge });   // 裙片外侧（玩家看到的一面）画 colB：外亮内暗
        }
        static Func<V3d, double, double, FBW[]> legSideW(string S, double top, double bot)
        {
            int bt = Bn("thigh" + S);
            return (p, s, t) => { double T = clamp01(t); return new[] { new FBW(FigBones.pelvis, 1 - T * bot), new FBW(bt, T * bot) }; };
        }
        // 赤腿外罩过膝的袍 / 长衣 / 纱笼（腿在裙内画成衬布，见 buildLeg）
        public static bool robeOverBare(JObj L)
        {
            string sk = L.S("skirt"); double len = L.D("skirtLen"); if (double.IsNaN(len)) len = 0;
            return Ls(L, "legs", "trousers") == "bare" && (sk == "robe" || sk == "coat" || sk == "sarong") && len >= 0.58;
        }
        // 这种袍片的权重：盖在大腿正前方的一带随同侧大腿多（0.82），前中线略少（≈ 0.6），后片少（0.4）
        static Func<V3d, double, double, FBW[]> robeSideW(string S, double th0, double th1)
        {
            double c = S == "L" ? 0.65 : -0.65; int bt = Bn("thigh" + S);
            return (p, s, t) =>
            {
                double th = lerp(th0, th1, s), f = Math.Exp(-(th - c) * (th - c) / 0.5);
                // 前中线与后片在腰下起得慢
                double w = sstep(0, lerp(0.8, 0.4, f), t) * (0.4 + 0.42 * f);
                return new[] { new FBW(FigBones.pelvis, 1 - w), new FBW(bt, w) };
            };
        }
        static void buildSkirt(FigCtx X)
        {
            var lod = X.lod; var pr = X.pr; var C = X.C; var L = X.L; var look = X.look;
            string sk = Ls(L, "skirt", "none");
            double top = 1.04, len = L.Or("skirtLen", 0.55), bot = top - len;
            bool lowL = lod.detail == 0;
            Func<double, double, double[]> uvFlip = (s, t) => P2(s, 1 - t);
            if (sk == "lamellar" || sk == "rattan" || sk == "scale")
            {
                string reg = sk == "rattan" ? "rattan" : sk == "scale" ? "scale" : (L.S("torso") == "lamLace" ? "lamLace" : "lamellar");
                FCol cc = sk == "rattan" ? col("#c8a060") : reg == "lamLace" ? C.cloth : C.metal;
                var P4 = new[] { new object[] { 0.06, 1.62, "L", 0.75 }, new object[] { -1.62, -0.06, "R", 0.75 }, new object[] { 1.52, 3.1, "L", 0.55 }, new object[] { -3.1, -1.52, "R", 0.55 } };
                foreach (var q in P4) skirtPanel(X, new SkirtO { th0 = (double)q[0], th1 = (double)q[1], yTop = top, yBot = bot, off = 0.016, flare = 0.07, curve = 0.04, reg = reg, col = cc, w = legSideW((string)q[2], 0, (double)q[3]), nv = Math.Max(2, lod.pv - 2) });
                // 腰甲上缘的包边
                if (!lowL) bandRing(X, (top - 0.02) * pr.k, (top + 0.03) * pr.k, 0.024, 0.003, "goldEngr", C.trim, 0, X.W.torso);
            }
            else if (sk == "robe" || sk == "coat" || sk == "sarong")
            {
                string reg = sk == "sarong" ? "brocade" : "hem";
                FCol cc = C.cloth;
                double fl = sk == "robe" ? 0.12 : sk == "coat" ? 0.07 : 0.03;
                double yb = sk == "robe" ? Math.Max(0.1, bot) : bot;
                double wk = sk == "robe" ? 0.45 : 0.6; bool rb = robeOverBare(L);
                double wave = X.hi >= 1 ? (sk == "robe" && X.hi >= 2 ? 0.018 : 0.008) : 0, waves = sk == "robe" && X.hi >= 2 ? 9 : 5;
                int nu = sk == "robe" && X.hi >= 2 ? 30 : Math.Max(3, lod.pn + 2);
                // 左右两片（左压右），各自随同侧大腿
                skirtPanel(X, new SkirtO { th0 = -0.25, th1 = PI + 0.25, yTop = top + 0.02, yBot = yb, off = 0.014, flare = fl, reg = reg, col = cc, w = rb ? robeSideW("L", -0.25, PI + 0.25) : legSideW("L", 0, wk), uv = uvFlip, wave = wave, waves = waves, nu = nu });
                skirtPanel(X, new SkirtO { th0 = -PI - 0.25, th1 = 0.25, yTop = top + 0.02, yBot = yb, off = 0.008, flare = fl, reg = reg, col = cc, w = rb ? robeSideW("R", -PI - 0.25, 0.25) : legSideW("R", 0, wk), uv = uvFlip, wave = wave, waves = waves, nu = nu });
            }
            else if (sk == "tunic" || sk == "kilt" || sk == "bands" || sk == "pteruges")
            {
                bool tun = sk == "tunic" || sk == "pteruges" || L.T("tunic");
                if (tun)
                {
                    FCol cc = look.culture == "roman" ? col("#a02a24") : C.cloth;
                    double yb = sk == "pteruges" ? top - 0.5 : bot;
                    skirtPanel(X, new SkirtO { th0 = -0.15, th1 = PI + 0.15, yTop = top, yBot = yb, off = 0.008, flare = 0.05, reg = "hem", col = cc, w = legSideW("L", 0, 0.6), uv = uvFlip });
                    skirtPanel(X, new SkirtO { th0 = -PI - 0.15, th1 = 0.15, yTop = top, yBot = yb, off = 0.004, flare = 0.05, reg = "hem", col = cc, w = legSideW("R", 0, 0.6), uv = uvFlip });
                }
                if (sk == "pteruges")
                {
                    // 皮条裙：一圈窄皮条
                    int n = lowL ? 6 : 14;
                    for (int i = 0; i < n; i++)
                    {
                        double a = -PI + (i + 0.5) / n * TAU, w = TAU / n * 0.46;
                        string S = Math.Sin(a) >= 0 ? "L" : "R";
                        skirtPanel(X, new SkirtO { th0 = a - w, th1 = a + w, yTop = top + 0.01, yBot = top - 0.3, off = 0.03, flare = 0.04, reg = "leather", col = shade(C.leather, 0.15), w = legSideW(S, 0, 0.5), nu = 1, nv = lowL ? 1 : 3, th = 0.005 });
                    }
                }
                else if (sk == "kilt")
                {
                    string reg = L.S("pelt") == "tiger" ? "tiger" : L.S("pelt") == "leopard" ? "leopard" : "cloth";
                    FCol cc = reg == "cloth" ? C.cloth : col("#ffffff");
                    skirtPanel(X, new SkirtO { th0 = -0.7, th1 = 0.7, yTop = top, yBot = bot, off = 0.014, flare = 0.04, reg = reg, col = cc, w = (p, s, t) => wT(t, FigBones.pelvis, 0, FigBones.skirtF, 0.7) });
                    skirtPanel(X, new SkirtO { th0 = PI - 0.9, th1 = PI + 0.9, yTop = top, yBot = bot + 0.04, off = 0.014, flare = 0.04, reg = reg, col = cc, w = (p, s, t) => wT(t, FigBones.pelvis, 0, FigBones.skirtB, 0.7) });
                    foreach (var S in new[] { "L", "R" }) { double sg = S == "L" ? 1 : -1; skirtPanel(X, new SkirtO { th0 = sg > 0 ? 0.6 : -2.4, th1 = sg > 0 ? 2.4 : -0.6, yTop = top, yBot = top - 0.22, off = 0.01, flare = 0.04, reg = reg, col = cc, w = legSideW(S, 0, 0.5) }); }
                }
                else if (sk == "bands")
                {
                    // 草摺：多层横带
                    for (int j = 0; j < (X.hi == 0 ? 2 : 4); j++)
                    {
                        double yt = top - j * (X.hi == 0 ? 0.2 : 0.1); int jj = j;
                        skirtPanel(X, new SkirtO { th0 = -PI, th1 = PI, yTop = yt, yBot = yt - 0.12, off = 0.02 + j * 0.022, flare = 0.02, reg = "rivet", col = C.metal, w = (p, s, t) => new[] { new FBW(FigBones.pelvis, 1 - jj * 0.12), new FBW(p.x > 0 ? FigBones.thighL : FigBones.thighR, jj * 0.12) }, nu = Math.Max(4, lod.torN), nv = 1, th = 0.005 });
                    }
                }
            }
            // 前垂（势力色、金线下摆）与后垂
            if (C.tabard.HasValue && L.T("tabard"))
            {
                double ttop = top + 0.01, tb = 0.42;
                skirtPanel(X, new SkirtO { th0 = -0.42, th1 = 0.42, yTop = ttop, yBot = tb, off = sk == "lamellar" || sk == "scale" ? 0.045 : 0.03, flare = 0.08, reg = "hem", col = C.tabard.Value, uv = (s, t) => P2(Math.Abs(2 * s - 1), 1 - t),
                    w = (p, s, t) => wT(t, FigBones.pelvis, 0, FigBones.skirtF, 0.35), nu = Math.Max(2, lod.pn - 1), nv = Math.Max(2, lod.pv - 1), th = 0.006 });
                if (look.kind == "general") skirtPanel(X, new SkirtO { th0 = PI - 0.5, th1 = PI + 0.5, yTop = ttop, yBot = tb + 0.06, off = sk == "lamellar" || sk == "scale" ? 0.045 : 0.03, flare = 0.08, reg = "hem", col = shade(C.tabard.Value, -0.1), uv = (s, t) => P2(Math.Abs(2 * s - 1), 1 - t),
                    w = (p, s, t) => wT(t, FigBones.pelvis, 0, FigBones.skirtB, 0.35), nu = Math.Max(2, lod.pn - 1), nv = Math.Max(2, lod.pv - 1), th = 0.006 });
            }
            // 腰带
            if (L.T("belt")) buildBelt(X, top);
        }
        static void buildBelt(FigCtx X, double top)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var R = X.R; var C = X.C; var L = X.L; var W = X.W;
            double k = pr.k; bool sash = L.S("belt") == "sash";
            double y0 = (top - 0.035) * k, y1 = (top + 0.035) * k;
            bandRing(X, y0, y1, sash ? 0.03 : 0.034, 0.002, sash ? "cloth" : "leather", sash ? C.cloth2 : (X.look.kind == "general" ? col("#3a2a22") : C.leather), 0, W.torso);
            if (!sash && X.hi >= 1)
            {
                // 兽面带扣
                var c = torsoPt(R, (y0 + y1) / 2, 0, 0.05, 0, pr.fem); var n = torsoNormal(R, (y0 + y1) / 2, 0);
                var AX = axesFromY(n);
                double r = 0.05 * k * (X.look.kind == "general" ? 1.1 : 0.8);
                int start = gb.nv;
                lathe(gb, new[] { new[] { r, -0.012, double.NaN, 0 }, new[] { r * 1.02, 0, double.NaN, 0.1 }, new[] { r * 0.9, 0.009, double.NaN, 0.3 }, new[] { r * 0.4, 0.016, double.NaN, 0.7 }, new[] { 0, 0.019, double.NaN, 1 } },
                    new FigOpt { c = c, axes = AX, n = lod.ring, reg = "buckle", col = col(GOLD), w = W.torso(c) });
                fixDiscUV(gb, FigAtlasLayout.REG("buckle"), c, AX, r, start);
            }
            else if (sash && X.hi >= 1)
            {
                // 腰结与垂带
                var c = torsoPt(R, (y0 + y1) / 2, -0.35, 0.035, 0, pr.fem);
                foreach (var q in new[] { P2(0.0, 0.32), P2(0.035, 0.26) })
                {
                    double dx = q[0], ln = q[1];
                    panel(gb, (s, t) => V(c.x - 0.02 + dx + s * 0.05 + t * 0.02, c.y - t * ln * k, c.z + 0.01 + t * 0.03), new FigOpt { nu = 1, nv = 3, th = 0.004, reg = "hem", col = C.cloth2, uv = (s, t) => P2(s, 1 - t), wP = (p, s, t) => wT(t, FigBones.pelvis, 0, FigBones.skirtF, 0.5) });
                }
            }
        }

        // ------------------------------------------------------------- 护肩 --
        static void buildPauldron(FigCtx X, string S)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var C = X.C; var L = X.L;
            string tp = Ls(L, "pauldron", "none");
            if (tp == "none") return;
            double sg = S == "L" ? 1 : -1, k = pr.k * (0.9 + 0.1 * pr.limb);
            var J = pr["upperArm" + S];
            var d = V(pr.armDirL.x * sg, pr.armDirL.y, 0);
            // 外上方向（垂直于手臂）
            var o0 = V(sg, 0.55, 0);
            var outv = v3norm(v3sub(o0, v3mul(d, v3dot(o0, d))));
            // 护肩 70% 随肩部辅助骨、30% 随上臂；所有甲片同一权重，不会散开
            int bsh = Bn("sh" + S), bua = Bn("upperArm" + S);
            Func<V3d, double, double, FBW[]> wpf = (p, s, t) => new[] { new FBW(bsh, 0.7), new FBW(bua, 0.3) };
            int lnv = X.hi == 0 ? 1 : Math.Max(2, lod.pv - 4);
            var side = v3norm(v3cross(d, outv));
            Action<double, double, double, double, double, string, FCol, double, double, double> lame = (a0, a1, r0, r1, half, reg, cc, wk, thk, c0) => panel(gb, (s, t) =>
            {
                double ph = c0 + lerp(-half, half, s), al = lerp(a0, a1, t) * k;
                // 围绕手臂轴旋转「外上」方向：ph 正 = 向前
                var dir = v3add(v3mul(outv, Math.Cos(ph)), v3mul(side, Math.Sin(ph) * -sg));
                double r = lerp(r0, r1, Math.Pow(t, 1.4)) * k;
                return v3add(v3madd(J, d, al), v3mul(dir, r));
            }, new FigOpt { nu = Math.Max(3, lod.pn + 1), nv = lnv, th = thk != 0 ? thk : 0.007, reg = reg, col = cc, colB = shade(cc, -0.45), wP = wpf, mirrorS = true });
            if (tp == "plate" || tp == "lion")
            {
                lame(-0.07, 0.05, 0.087, 0.108, 1.6, tp == "lion" ? "gold" : "metal", tp == "lion" ? col(GOLD) : C.metal, 0.6, 0.009, 0);
                lame(0.0, 0.12, 0.1, 0.114, 1.5, "metal", C.metal, 0.85, 0, 0);
                if (X.hi >= 2) lame(0.07, 0.19, 0.104, 0.11, 1.4, "metal", C.metal, 1, 0, 0);
                // 兽面（狮子吞肩）
                if (tp == "lion" && X.hi >= 1)
                {
                    var c = v3add(v3madd(J, d, 0.0), v3mul(outv, 0.105 * k)); var AX = axesFromY(outv); double r = 0.05 * k; int start = gb.nv;
                    lathe(gb, new[] { new[] { r, -0.01, double.NaN, 0 }, new[] { r * 1.02, 0, double.NaN, 0.1 }, new[] { r * 0.85, 0.012, double.NaN, 0.35 }, new[] { r * 0.3, 0.022, double.NaN, 0.8 }, new[] { 0, 0.024, double.NaN, 1 } },
                        new FigOpt { c = c, axes = AX, n = lod.ring, reg = "lion", col = col(GOLD), w = wpf(c, 0, 0) });
                    fixDiscUV(gb, FigAtlasLayout.REG("lion"), c, AX, r, start);
                }
            }
            else if (tp == "lamellar" || tp == "scale" || tp == "rattan")
            {
                string reg = tp == "rattan" ? "rattan" : tp == "scale" ? "scale" : (L.S("torso") == "lamLace" ? "lamLace" : "lamellar");
                FCol cc = tp == "rattan" ? col("#c8a060") : reg == "lamLace" ? C.cloth : C.metal;
                lame(-0.06, 0.1, 0.087, 0.115, 1.55, reg, cc, 0.7, 0, 0);
                if (X.hi >= 1) lame(0.06, 0.2, 0.1, 0.114, 1.45, reg, cc, 1, 0, 0);
            }
            else if (tp == "segm" || tp == "bands")
            {
                int n = X.hi >= 1 ? 4 : 2;
                for (int i = 0; i < n; i++) lame(-0.06 + i * 0.045, -0.06 + i * 0.045 + 0.06, 0.074 + i * 0.006, 0.084 + i * 0.006, 1.5, "rivet", C.metal, 0.6 + i * 0.1, 0.005, 0);
            }
            else if (tp == "fur")
            {
                lame(-0.07, 0.08, 0.08, 0.11, 1.7, "fur", col("#8a6a48"), 0.7, 0.02, 0);
            }
            else if (tp == "pteruges")
            {
                int n = X.hi >= 1 ? 5 : 3;
                for (int i = 0; i < n; i++)
                {
                    double a = lerp(-1.2, 1.2, (i + 0.5) / n);
                    lame(0.02, 0.17, 0.068, 0.078, 0.2, "leather", shade(C.leather, 0.15), 1, 0.005, a);
                }
            }
        }

        // ------------------------------------------------------------- 披风 --
        static void buildCape(FigCtx X)
        {
            var gb = X.gb; var pr = X.pr; var C = X.C; var look = X.look;
            if (!C.cape.HasValue) return;
            FCol cape = C.cape.Value;
            double k = pr.k, sw = pr.sw;
            string reg = Ls(look.L, "capeReg", "hem");
            double yb = look.kind == "soldier" ? 0.62 : 0.3;
            bool lam = look.L.S("skirt") == "lamellar";
            // 下摆保持弧形横截面（裹住双腿），越往下越深的 4–5 道竖褶，厚度 12 毫米
            panel(gb, (s, t) =>
            {
                double a = lerp(-1.3, 1.3, s), sa = Math.Sin(a), ca = Math.Cos(a);
                var tp = V(sa * 0.19 * sw * k, (1.475 - 0.03 * (1 - ca)) * k, (-ca * 0.13 - 0.03) * sw * k);
                var bt = V(sa * 0.34 * k, yb * k, (-ca * 0.27 - 0.06) * k);
                var p = v3lerp(tp, bt, Math.Pow(t, 0.9));
                p.z += -0.06 * Math.Sin(PI * t) * k - (lam ? 0.02 : 0);
                if (X.hi >= 1)
                {
                    double f = (Math.Abs(Math.Sin(s * PI * 4.5 + 0.7)) - 0.55) * 0.05 * t * k;
                    p.x += sa * f + Math.Sin(s * PI * 9) * 0.008 * t * k; p.z += -ca * f;
                }
                return p;
            }, new FigOpt
            {
                nu = X.hi >= 2 ? 12 : X.hi >= 1 ? 6 : 3, nv = X.hi >= 2 ? 12 : X.hi >= 1 ? 5 : 2, th = 0.012, reg = reg,
                // 褶皱明暗烘进顶点色（褶峰亮、褶谷暗，越往下越明显）+ 肩下的遮蔽
                col = X.hi >= 1 ? (FCol?)null : cape,
                colPF = X.hi >= 1 ? (Func<double, double, FCol>)((s, t) => { double f = Math.Abs(Math.Sin(s * PI * 4.5 + 0.7)), q = (0.8 + 0.2 * f * (0.4 + 0.6 * t)) * (0.8 + 0.2 * sstep(0, 0.3, t)); return shade(cape, q - 1); }) : null,
                colB = shade(cape, -0.5), uv = (s, t) => P2(Math.Abs(2 * s - 1), 1 - t),
                wP = (p, s, t) => wT(t, FigBones.chest, 0.02, FigBones.cape1, 0.12, FigBones.cape2, 0.5, FigBones.cape3, 0.9),
            });
        }
        // 飘带：自腰后两侧垂下
        static void buildRibbons(FigCtx X)
        {
            var gb = X.gb; var pr = X.pr; var look = X.look;
            if (look.ribbons == null || X.hi < 1) return;
            double k = pr.k;
            foreach (int sg in new[] { 1, -1 })
            {
                panel(gb, (s, t) =>
                {
                    double x = sg * (0.1 + 0.08 * t) * k + (s - 0.5) * 0.06 * k;
                    return V(x, (1.02 - t * 0.85) * k, (-0.17 - 0.2 * t - 0.03 * Math.Sin(t * 6 + sg)) * k);
                }, new FigOpt { nu = 1, nv = X.hi >= 2 ? 10 : 4, th = 0.003, reg = "cloth", col = col(look.ribbons), wP = (p, s, t) => wT(t, FigBones.pelvis, 0, FigBones.skirtB, 0.15, FigBones.cape3, 0.7) });
            }
        }
        // 箭壶、背弓
        static void buildBackGear(FigCtx X)
        {
            var gb = X.gb; var lod = X.lod; var pr = X.pr; var look = X.look;
            double k = pr.k;
            if (look.quiver)
            {
                V3d a = V(0.1 * k, 1.05 * k, -0.17 * k), b = V(-0.12 * k, 1.5 * k, -0.2 * k);
                tube(gb, new List<V3d> { a, v3lerp(a, b, 0.5), b }, new FigOpt { rF = (s, i) => (0.042 - 0.006 * s) * k, n = Math.Max(5, lod.tube), reg = "leather", col = col(LEATHER), w = W1(FigBones.chest), capStart = true });
                if (X.hi >= 1) for (int i = 0; i < 5; i++)
                {
                    var o = V((-0.012 + (i % 3) * 0.012) * k, 0, (-0.01 + Math.Floor(i / 3.0) * 0.02) * k);
                    var p0 = v3add(b, o); var p1 = v3add(p0, v3mul(v3norm(v3sub(b, a)), 0.1 * k));
                    tube(gb, new List<V3d> { p0, p1 }, new FigOpt { rF = (s, ii) => 0.012 * k * (s > 0.3 ? 1 : 0.4), rz = 0.25, n = 3, reg = "feather", col = new FCol(1, 1, 1), w = W1(FigBones.chest) });
                }
            }
            if (look.backBow)
            {
                var pts = new List<V3d>();
                for (int i = 0; i <= 8; i++) { double t = i / 8.0; pts.Add(V(lerp(0.3, -0.28, t) * k, lerp(0.95, 1.6, t) * k, (-0.2 - 0.07 * Math.Sin(PI * t)) * k)); }
                tube(gb, pts, new FigOpt { rF = (s, i) => (0.012 - 0.006 * Math.Abs(s - 0.5)) * k, n = 5, reg = "wood", col = col("#5a3418"), w = W1(FigBones.chest) });
            }
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    public static partial class FigBuild
    {
        // ======================================================== 冠帽、发、须 ==
        // 均在头部局部坐标中构建（gb.xf = 头骨绑定位置），权重为头骨（盔缨 / 长发用 plume / hair 骨）
        static double FEATP(string k)
        {
            switch (k) { case "eyeT": return FigureData.FEAT_eyeT; case "eyeP": return FigureData.FEAT_eyeP; case "browP": return FigureData.FEAT_browP; case "noseP": return FigureData.FEAT_noseP; case "mouthP": return FigureData.FEAT_mouthP; case "chinP": return FigureData.FEAT_chinP; default: return FigureData.FEAT_hairP; }
        }
        public static double hairline(double th, string style, double recede)
        {
            double a = Math.Abs(th), front = (style == "kunfa" ? 1.2 : FigureData.FEAT_hairP) + recede * 0.25;
            if (a < 0.95) return front + (style == "kunfa" ? 0 : 0.06 * Math.Cos(th * 5) * gauss(a, 0.5, 0.3));
            if (a < 1.45) return lerp(front - 0.15, 0.08, sstep(0.95, 1.45, a));
            if (a < 1.75) return lerp(0.08, 0.15, sstep(1.45, 1.75, a));
            return lerp(0.1, -0.55, sstep(1.75, 2.6, a));
        }
        static V3d headN(V3d p) { return v3norm(v3sub(p, HEAD_C)); }
        sealed class ShellHO
        {
            public Func<double, double> phi0; public Func<double, double, double, double> off; public double sy, apex, tilt, pw; public int nK, n; public string reg; public FCol col; public FBW[] w; public bool mirror = true;
        }
        // 头部外壳：在头部参数面 φ ∈ [φ0(θ), π/2] 上外扩（盔体、帽、发壳）
        static List<int[]> headShell(FigCtx X, HeadP Hp, ShellHO o)
        {
            var gb = X.gb; var lod = X.lod;
            int nK = o.nK != 0 ? o.nK : Math.Max(3, (int)jround(lod.head[0] * 0.45)), n = o.n != 0 ? o.n : Math.Max(8, lod.head[1]);
            var secs = new List<FigSec>();
            for (int k = 0; k <= nK; k++) secs.Add(new FigSec { c = V(0, 0, 0), rx = 1, rz = 1, t = (double)k / nK, pole = k == nK, kk = (double)k / nK });
            Func<double, FigSec, int, V3d> ptf = (th, s, kk) =>
            {
                double u = s.kk;
                double p0 = o.phi0(th), ph = lerp(p0, PI / 2 - 0.001, Math.Pow(u, o.pw != 0 ? o.pw : 0.85));
                var p = headPoint(th, ph, Hp);
                var nn = headN(p);
                double d = o.off(th, u, ph);
                var q = v3madd(p, nn, d);
                // 拔高：以头中心高度为界向上等比拉伸（顶部保持圆润）
                if (o.sy != 0 && q.y > 0.12) q.y = 0.12 + (q.y - 0.12) * (1 + o.sy / 0.125);
                if (o.apex != 0) q.y += o.apex * Math.Pow(u, 3);
                if (o.tilt != 0) q.z += o.tilt * Math.Pow(Math.Max(0, q.y - 0.12) / 0.2, 2);
                return q;
            };
            // 顶点 pole 位置
            secs[nK].c = ptf(0, new FigSec { kk = 1 }, 0);
            return loft(gb, secs, new FigOpt { n = n, reg = o.reg, col = o.col, w = o.w ?? W1(FigBones.head), ptf = ptf, mirror = o.mirror });
        }
        // 沿头部表面一圈的带子（发带、抹额、冠圈、盔沿）：phi(θ) 高度，off 外扩，r 管半径
        static void headRing(FigCtx X, HeadP Hp, Func<double, double> phi, double off, double r, string reg, FCol cc, double rz = double.NaN, int tn = 0, double a0 = -PI, double a1 = PI)
        {
            var gb = X.gb; var lod = X.lod;
            int n = X.hi == 0 ? 8 : Math.Max(10, lod.head[1]); var pts = new List<V3d>();
            for (int i = 0; i <= n; i++) { double th = lerp(a0, a1, (double)i / n); var p = headPoint(th, phi(th), Hp); pts.Add(v3madd(p, headN(p), off)); }
            tube(gb, pts, new FigOpt { r = r, rz = !double.IsNaN(rz) && rz != 0 ? rz : 1, n = X.hi == 0 ? 3 : (tn != 0 ? tn : Math.Max(4, lod.tube - 2)), reg = reg, col = cc, w = W1(FigBones.head), up = V(0, 1, 0) });
        }
        static V3d headAt(HeadP Hp, double th, double ph, double off) { var p = headPoint(th, ph, Hp); return v3madd(p, headN(p), off); }

        // 盔缨（红缨）：自 p 处向上再下垂的一簇马尾
        public static void tassel(FigGB gb, FigLod lod, int hi, V3d p, double size, FCol cc, FBW[] w)
        {
            double s = size;
            double[][] prof = hi == 0 ? new[] { P2(0.005, 0), P2(0.05 * s, 0.05 * s), P2(0.07 * s, -0.04 * s), P2(0.006, -0.01) }
                : new[] { P2(0.005, 0), P2(0.011, 0.02), P2(0.03 * s, 0.05 * s), P2(0.055 * s, 0.045 * s), P2(0.07 * s, 0.015 * s), P2(0.07 * s, -0.04 * s), P2(0.05 * s, -0.07 * s), P2(0.02 * s, -0.04 * s), P2(0.006, -0.005) };
            Array.Reverse(prof);
            var pr4 = new double[prof.Length][];
            for (int i = 0; i < prof.Length; i++) pr4[i] = new[] { prof[i][0], prof[i][1], double.NaN, (double)i / (prof.Length - 1) };
            var ww = w ?? W1(FigBones.plume1);
            lathe(gb, pr4, new FigOpt { c = p, n = Math.Max(6, lod.ring - 4), reg = "tassel", col = cc, w = ww });
            // 高精度：缨顶散出一圈细丝，向外张开、向后下垂
            if (hi >= 2)
            {
                int n = 10;
                for (int i = 0; i < n; i++)
                {
                    double a = (double)i / n * TAU + 0.2, ca = Math.Cos(a), sa = Math.Sin(a), r0 = 0.03 * s, r1 = (0.07 + 0.03 * (i % 3) / 2.0) * s;
                    double back = 0.07 * s * (1 - 0.5 * Math.Max(0, sa));
                    tube(gb, new List<V3d> { V(p.x + ca * r0, p.y + 0.045 * s, p.z + sa * r0), V(p.x + ca * (r0 + r1) * 0.6, p.y + 0.03 * s, p.z + sa * (r0 + r1) * 0.6 - back * 0.4), V(p.x + ca * r1, p.y - 0.09 * s, p.z + sa * r1 - back) },
                        new FigOpt { rF = (t, ii) => 0.005 * (1 - 0.6 * t), n = 3, reg = "tassel", col = cc, w = ww });
                }
            }
        }
        // 羽毛 / 雉翎 / 飘带：沿曲线的扁带，宽度 w0 → w1（w 为常量权重，wfn 为按 (p, s, t) 的权重函数）
        public static void featherStrip(FigGB gb, List<V3d> pts, double w0, double w1, string reg, FCol cc, FBW[] w, Func<V3d, double, double, FBW[]> wfn, int nv = 0)
        {
            var F = frames(pts, V(0, 0, 1));
            int N = pts.Count;
            panel(gb, (s, t) =>
            {
                double f = t * (N - 1); int i = Math.Min(N - 2, (int)Math.Floor(f)); double u = f - i;
                var ap = v3lerp(pts[i], pts[i + 1], u); var ax = v3norm(v3lerp(F.X[i], F.X[i + 1], u));
                return v3madd(ap, ax, (s - 0.5) * lerp(w0, w1, t));
            }, new FigOpt { nu = 1, nv = nv != 0 ? nv : Math.Max(4, (N - 1) * 2), th = 0.003, reg = reg ?? "feather", col = cc, colB = cc, w = w, wP = wfn, uv = (s, t) => P2(s, t) });
        }
        public static List<V3d> bez(V3d p0, V3d p1, V3d p2, V3d p3, int n)
        {
            var o = new List<V3d>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n, u = 1 - t;
                o.Add(V(u * u * u * p0.x + 3 * u * u * t * p1.x + 3 * u * t * t * p2.x + t * t * t * p3.x, u * u * u * p0.y + 3 * u * u * t * p1.y + 3 * u * t * t * p2.y + t * t * t * p3.y, u * u * u * p0.z + 3 * u * u * t * p1.z + 3 * u * t * t * p2.z + t * t * t * p3.z));
            }
            return o;
        }
        static Func<double, double> rim(double front, double back) { return th => lerp(back, front, (1 + Math.Cos(th)) / 2); }
        static Func<V3d, double, double, FBW[]> wHeadHair(double k) { return (p, s, t) => wT(t, FigBones.head, 0, FigBones.hair1, k); }

        public sealed class HGRes { public bool coversTop, coversBack, showBun; }
        static FCol HC(JObj hat, string k, string def) { var s = hat.S(k); return col(string.IsNullOrEmpty(s) ? def : s); }
        static string HS(JObj hat, string k) { var s = hat.S(k); return string.IsNullOrEmpty(s) ? null : s; }

        static HGRes buildHeadgear(FigCtx X, HeadP Hp)
        {
            var gb = X.gb; var lod = X.lod; var look = X.look; var C = X.C;
            var hat = look.hat ?? new JObj { { "type", "none" } };
            string tp = HS(hat, "type") ?? "none";
            FCol metal = HS(hat, "color") != null ? col(hat.S("color")) : C.metal;
            FCol plume = HS(hat, "plume") != null ? col(hat.S("plume")) : C.plume;
            int hi = X.hi;
            var coverT = new Dictionary<string, double> { { "none", 0 }, { "headband", 0 }, { "diadem", 0 }, { "laurel", 0 }, { "crown", 0.3 }, { "pheasant", 0.3 }, { "feathers", 0 }, { "chief", 0 }, { "shaman", 0 }, { "goldcrown", 0 }, { "featherband", 0 } };
            double cover; bool hasCover = coverT.TryGetValue(tp, out cover);
            var backs = new[] { "helmet", "galea", "wahelm", "keffiyeh", "conical", "spangen", "celthelm", "tiara", "felt", "furhat", "turban" };
            var res = new HGRes { coversTop = !hasCover || cover > 0.5, coversBack = Array.IndexOf(backs, tp) >= 0 };
            var wHead = W1(FigBones.head);
            Func<ShellHO, List<int[]>> bowl = o =>
            {
                if (o.phi0 == null) o.phi0 = rim(0.3, -0.3);
                if (o.off == null) o.off = (th, u, ph) => 0.014 + 0.01 * u;
                if (o.reg == null) o.reg = "metal";
                return headShell(X, Hp, o);
            };
            Func<double, double, double> apexY = (sy, ap) => headAt(Hp, 0, PI / 2 - 0.001, 0.024).y + sy + ap;
            if (tp == "helmet")
            {
                string v = HS(hat, "variant") ?? "plain";
                double spike = v == "spike" ? 0.09 : 0;
                var rm = rim(0.3, -0.25);
                bowl(new ShellHO { phi0 = rm, sy = 0.012, apex = spike, reg = "metal", col = metal });
                // 盔沿
                if (hi >= 1) headRing(X, Hp, rm, 0.017, 0.007, "goldEngr", C.trim);
                // 竖棱
                if (hi >= 2) for (int i = 0; i < 4; i++)
                {
                    double th = i * PI / 2 + PI / 4; var pts = new List<V3d>();
                    for (int j = 0; j <= 5; j++) { double ph = lerp(rm(th), PI / 2 - 0.02, j / 5.0); var p = headAt(Hp, th, ph, 0.026 + 0.004 * j / 5.0); p.y += 0.012 * Math.Pow(j / 5.0, 2) + spike * Math.Pow(j / 5.0, 3); pts.Add(p); }
                    tube(gb, pts, new FigOpt { r = 0.004, n = 4, reg = "gold", col = C.trim, w = wHead });
                }
                // 顿项（护颈）
                FCol? ng = HS(hat, "neck") != null ? col(hat.S("neck")) : (look.kind == "general" ? shade(C.cloth2, -0.1) : (FCol?)null);
                panel(gb, (s, t) =>
                {
                    double th = lerp(1.15, TAU - 1.15, s);
                    var p = headAt(Hp, th, rm(th) + 0.02, 0.018);
                    var n = headN(p);
                    return V(p.x + n.x * 0.04 * t, p.y - t * 0.1, p.z + n.z * 0.05 * t);
                }, new FigOpt { nu = Math.Max(3, lod.pn + 1), nv = Math.Max(1, lod.pv - 6), th = 0.006, reg = ng.HasValue ? "cloth" : "lamellar", col = ng ?? metal, wP = (p, s, t) => new[] { new FBW(FigBones.head, 1 - 0.4 * t), new FBW(FigBones.neck, 0.4 * t) } });
                // 护耳
                if (hi >= 1) foreach (int sg in new[] { 1, -1 }) panel(gb, (s, t) =>
                {
                    double th = sg * lerp(0.95, 1.65, s);
                    var p = headAt(Hp, th, rm(th) + 0.01, 0.02 + 0.006 * t);
                    return V(p.x + sg * 0.008 * t, p.y - t * 0.085, p.z + 0.006 * t);
                }, new FigOpt { nu = 2, nv = 2, th = 0.006, reg = "metal", col = metal, w = wHead });
                // 眉庇
                panel(gb, (s, t) => { double th = lerp(-0.85, 0.85, s); var p = headAt(Hp, th, 0.3, 0.02); var n = headN(p); return V(p.x + n.x * 0.03 * t, p.y - 0.004 * t, p.z + n.z * 0.035 * t); },
                    new FigOpt { nu = Math.Max(2, lod.pn), nv = 1, th = 0.005, reg = "goldEngr", col = C.trim, w = wHead, mirrorS = true });
                var top = V(0, apexY(0.012, spike), HEAD_C.z - 0.004);
                if (v == "wing" && hi >= 1) foreach (int sg in new[] { 1, -1 })
                    featherStrip(gb, bez(headAt(Hp, sg * 1.45, 0.45, 0.02), V(sg * 0.13, 0.27, -0.01), V(sg * 0.18, 0.33, -0.05), V(sg * 0.2, 0.4, -0.1), 6), 0.07, 0.02, "gold", C.trim, wHead, null, 6);
                if (v == "horn" && hi >= 0) foreach (int sg in new[] { 1, -1 })
                    tube(gb, bez(headAt(Hp, sg * 1.2, 0.6, 0.015), V(sg * 0.13, 0.25, 0.02), V(sg * 0.16, 0.33, 0.04), V(sg * 0.13, 0.4, 0.07), 6), new FigOpt { rF = (s, ii) => 0.014 * (1 - s) + 0.002, n = 6, reg = "gold", col = col("#e8e0c8"), w = wHead });
                if (v == "lion")
                {
                    // 狮首（马超）：盔前一张浮雕狮面 + 隆起的口鼻、眉骨、红宝石眼、双耳，四周一圈鬃毛簇
                    var N = v3norm(V(0, 0.3, 1)); var c = headAt(Hp, 0, 0.6, 0.028); var AX = axesFromY(N); double r = 0.056; int start = gb.nv;
                    var up = v3norm(v3sub(V(0, 1, 0), v3mul(N, N.y))); var sd = v3cross(up, N);
                    Func<double, double, double, V3d> at = (x, y, z) => v3add(c, v3add(v3mul(sd, x), v3add(v3mul(up, y), v3mul(N, z))));
                    lathe(gb, new[] { new[] { r, -0.01, double.NaN, 0 }, new[] { r * 1.03, 0, double.NaN, 0.1 }, new[] { r * 0.92, 0.007, double.NaN, 0.3 }, new[] { r * 0.55, 0.011, double.NaN, 0.65 }, new[] { 0, 0.012, double.NaN, 1 } },
                        new FigOpt { c = c, axes = AX, n = lod.ring, reg = "lion", col = col(GOLD), w = wHead });
                    fixDiscUV(gb, FigAtlasLayout.REG("lion"), c, AX, r, start);
                    FCol g = col(GOLD), gd = shade(GOLD, -0.25);
                    var axs = new FigAxes(sd, up, N);
                    ellipsoid(gb, at(0, -0.016, 0.016), V(0.021, 0.015, 0.014), new FigOpt { nl = hi >= 1 ? 5 : 3, n = hi >= 1 ? 8 : 6, reg = "gold", col = g, w = wHead, axes = axs });    // 口鼻
                    ellipsoid(gb, at(0, -0.004, 0.026), V(0.009, 0.006, 0.007), new FigOpt { nl = 3, n = 6, reg = "gold", col = gd, w = wHead, axes = axs });                         // 鼻头
                    if (hi >= 1)
                    {
                        foreach (int sg in new[] { 1, -1 })
                        {
                            tube(gb, new List<V3d> { at(sg * 0.006, 0.014, 0.014), at(sg * 0.02, 0.02, 0.016), at(sg * 0.034, 0.015, 0.01) }, new FigOpt { rF = (q, ii) => 0.006 * (1 - 0.5 * q), n = 5, reg = "gold", col = g, w = wHead });   // 眉骨
                            ellipsoid(gb, at(sg * 0.02, 0.008, 0.014), V(0.006, 0.004, 0.004), new FigOpt { nl = 3, n = 6, reg = "white", col = col("#d02828"), w = wHead });                                     // 宝石眼
                            tube(gb, new List<V3d> { at(sg * 0.04, 0.036, 0.0), at(sg * 0.046, 0.052, -0.004) }, new FigOpt { rF = (q, ii) => 0.01 * (1 - q) + 0.002, n = 5, reg = "gold", col = g, w = wHead });                     // 耳
                            tube(gb, new List<V3d> { at(sg * 0.012, -0.03, 0.016), at(sg * 0.014, -0.042, 0.012) }, new FigOpt { rF = (q, ii) => 0.004 * (1 - q) + 0.001, n = 4, reg = "white", col = col("#f0ead8"), w = wHead });  // 獠牙
                        }
                        int nm = hi >= 2 ? 14 : 9;
                        for (int i = 0; i < nm; i++)
                        {
                            double a = PI * 0.15 + (i + 0.5) / nm * PI * 1.7, dx = Math.Cos(a), dy = Math.Sin(a) * 0.95;
                            var p0 = at(dx * r * 0.85, dy * r * 0.85, -0.004); var p1 = at(dx * r * 1.32, dy * r * 1.32 + 0.004, -0.018);
                            tube(gb, new List<V3d> { p0, v3lerp(p0, p1, 0.5), p1 }, new FigOpt { rF = (q, ii) => 0.011 * (1 - q) + 0.0015, rz = 0.6, n = 5, reg = "gold", col = g, w = wHead });
                        }
                    }
                }
                if (v == "plume" || look.culture == "korea") { featherStrip(gb, bez(top, V(0, top.y + 0.12, top.z - 0.02), V(0, top.y + 0.22, top.z - 0.08), V(0, top.y + 0.3, top.z - 0.16), 6), 0.05, 0.012, "tassel", plume, null, (p, s, t) => wT(t, FigBones.plume1, 0, FigBones.plume2, 0.8)); }
                else
                {
                    if (hi >= 1) tube(gb, new List<V3d> { top, V(top.x, top.y + 0.03, top.z) }, new FigOpt { r = 0.008, n = 6, reg = "gold", col = C.trim, w = wHead });
                    tassel(gb, lod, hi, V(top.x, top.y + (hi >= 1 ? 0.03 : 0.005), top.z), look.kind == "general" ? 1.15 : 0.85, plume, null);
                }
                res.coversTop = true;
            }
            else if (tp == "galea")
            {
                var rm = rim(0.32, -0.15);
                bowl(new ShellHO { phi0 = rm, sy = 0.01, reg = "metal", col = HS(hat, "color") != null ? col(hat.S("color")) : col(look.kind == "soldier" ? "#b8bcc4" : BRONZE) });
                headRing(X, Hp, rm, 0.018, 0.008, "gold", col(BRONZE));
                // 护颈外翻
                panel(gb, (s, t) => { double th = lerp(1.6, TAU - 1.6, s); var p = headAt(Hp, th, -0.15 + 0.03, 0.02); var n = headN(p); return V(p.x + n.x * 0.06 * t, p.y - 0.03 * t, p.z + n.z * 0.06 * t); },
                    new FigOpt { nu = Math.Max(2, lod.pn), nv = 1, th = 0.006, reg = "metal", col = HC(hat, "color", "#b8bcc4"), w = wHead });
                // 护颊
                if (hi >= 1) foreach (int sg in new[] { 1, -1 }) panel(gb, (s, t) => { double th = sg * lerp(0.75, 1.55, s); return headAt(Hp, th, lerp(0.15, -0.65, t), 0.016 + 0.004 * t); },
                    new FigOpt { nu = 2, nv = Math.Max(1, lod.pv - 6), th = 0.006, reg = "metal", col = col(BRONZE), w = wHead });
                // 冠饰（纵向马鬃）
                int N = Math.Max(4, lod.head[0] - 8);
                var secs = new List<FigSec>();
                for (int i = 0; i <= N; i++)
                {
                    double pp = (double)i / N;
                    double phi = pp < 0.5 ? lerp(0.85, PI / 2, pp * 2) : lerp(PI / 2, 0.6, (pp - 0.5) * 2);
                    double thh = pp < 0.5 ? 0 : PI;
                    var p = headAt(Hp, thh, phi, 0.034);
                    p.y += 0.01;
                    var o = headN(p);
                    secs.Add(new FigSec { c = v3madd(p, o, 0.03), X = V(1, 0, 0), Z = o, rx = 0.011, rz = 0.04 * Math.Sin(PI * clamp(pp * 1.1, 0, 1)) + 0.008, t = pp });
                }
                loft(gb, secs, new FigOpt { n = hi >= 1 ? 6 : 4, reg = "tassel", col = plume, w = wHead });
                res.coversTop = true;
            }
            else if (tp == "ze" || tp == "jinxian" || tp == "futou" || tp == "lunjin" || tp == "turban")
            {
                // 纶巾缺省为青色（与头像一致），帻露出发髻
                FCol cc = HC(hat, "color", tp == "turban" ? "#3a6a3a" : tp == "lunjin" ? "#3a5a8c" : "#22202a");
                double tall = tp == "lunjin" ? 0.045 : tp == "futou" ? 0.025 : tp == "turban" ? 0.03 : 0.006;
                headShell(X, Hp, new ShellHO { phi0 = rim(0.42, -0.1), off = (th, u, ph) => 0.01 + (tp == "turban" ? 0.018 : 0.006) * u, sy = tall, tilt = tp == "lunjin" ? -0.03 : 0, reg = "cloth", col = cc });
                if (tp == "lunjin") foreach (var fb in hi >= 1 ? new[] { P2(0.7, 0.35), P2(0.98, 0.7), P2(1.22, 1.0) } : new[] { P2(0.85, 0.5) }) headRing(X, Hp, rim(fb[0], fb[1]), 0.016 + 0.004 * fb[0], 0.0045, "cloth", col("#d8e2ee"), 0.6, 4);
                if (tp == "ze") res.showBun = true;
                if (tp == "turban")
                {
                    foreach (var fbo in new[] { new[] { 0.42, -0.08, 0.02 }, new[] { 0.62, 0.12, 0.028 } }) headRing(X, Hp, rim(fbo[0], fbo[1]), fbo[2], 0.013, "cloth", shade(cc, -0.08));
                    // 头巾结与垂带
                    var kp = headAt(Hp, PI, 0.15, 0.03);
                    ellipsoid(gb, kp, V(0.025, 0.02, 0.02), new FigOpt { nl = 4, n = 6, reg = "cloth", col = cc, w = wHead });
                    if (hi >= 1) foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(kp, V(kp.x + sg * 0.03, kp.y - 0.06, kp.z - 0.03), V(kp.x + sg * 0.05, kp.y - 0.14, kp.z - 0.05), V(kp.x + sg * 0.06, kp.y - 0.24, kp.z - 0.06), 5), 0.05, 0.035, "cloth", cc, null, wHeadHair(0.6));
                }
                else if (tp == "jinxian")
                {
                    // 进贤冠：前高后低的冠梁
                    var f = headAt(Hp, 0, 0.95, 0.02); var bk = headAt(Hp, PI, 1.25, 0.02);
                    panel(gb, (s, t) => V(lerp(-0.035, 0.035, s), lerp(f.y + 0.07, bk.y + 0.02, t), lerp(f.z - 0.0, bk.z + 0.02, t)), new FigOpt { nu = 2, nv = 2, th = 0.008, reg = "cloth", col = cc, w = wHead });
                    // 冠梁下补一片竖板连到帻顶
                    Func<double, V3d> midAt = t => { double ph = t < 0.5 ? lerp(0.95, PI / 2 - 0.001, t * 2) : lerp(PI / 2 - 0.001, 1.25, (t - 0.5) * 2); return headAt(Hp, t < 0.5 ? 0 : PI, ph, 0.012); };
                    panel(gb, (s, t) => { var m0 = midAt(t); double ty = lerp(f.y + 0.07, bk.y + 0.02, t), tz = lerp(f.z, bk.z + 0.02, t); return V(0, lerp(m0.y, ty, s), lerp(m0.z, tz, s)); },
                        new FigOpt { nu = 1, nv = hi >= 1 ? 6 : 3, th = 0.01, reg = "cloth", col = shade(cc, -0.1), w = wHead });
                    headRing(X, Hp, rim(0.42, -0.1), 0.012, 0.006, "cloth", shade(cc, 0.2));
                }
                else if (tp == "futou" && hi >= 1)
                {
                    var kp = headAt(Hp, PI, 0.4, 0.02);
                    foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(kp, V(kp.x + sg * 0.02, kp.y - 0.05, kp.z - 0.02), V(kp.x + sg * 0.03, kp.y - 0.12, kp.z - 0.03), V(kp.x + sg * 0.035, kp.y - 0.2, kp.z - 0.03), 4), 0.03, 0.025, "cloth", cc, null, wHeadHair(0.6));
                }
                else if (tp == "lunjin" && hi >= 1)
                {
                    var kp = headAt(Hp, PI, 0.6, 0.02);
                    foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(kp, V(kp.x + sg * 0.03, kp.y - 0.06, kp.z - 0.03), V(kp.x + sg * 0.04, kp.y - 0.16, kp.z - 0.05), V(kp.x + sg * 0.05, kp.y - 0.3, kp.z - 0.06), 5), 0.025, 0.022, "cloth", shade(cc, 0.15), null, wHeadHair(0.6));
                }
                res.coversTop = true;
            }
            else if (tp == "pagri" || tp == "mukuta" || tp == "aksumcrown")
            {
                // 天竺大缠头 / 天竺宝冠 / 阿克苏姆金冠
                FCol cc = tp == "pagri" ? HC(hat, "color", "#ece6d8") : HC(hat, "color2", tp == "mukuta" ? "#a8302a" : "#ece6d8");
                headShell(X, Hp, new ShellHO { phi0 = rim(0.42, -0.1), off = (th, u, ph) => 0.012 + 0.022 * u, sy = tp == "aksumcrown" ? 0.02 : 0.04, reg = "cloth", col = cc });
                foreach (var fbo in hi >= 1 ? new[] { new[] { 0.42, -0.08, 0.022 }, new[] { 0.66, 0.14, 0.03 }, new[] { 0.9, 0.42, 0.034 } } : new[] { new[] { 0.5, 0.0, 0.026 } }) headRing(X, Hp, rim(fbo[0], fbo[1]), fbo[2], 0.013, "cloth", shade(cc, -0.08));
                if (tp == "pagri")
                {
                    var kp = headAt(Hp, 0, 1.0, 0.05); kp.y += 0.035;
                    ellipsoid(gb, kp, V(0.046, 0.04, 0.04), new FigOpt { nl = hi >= 1 ? 6 : 4, n = hi >= 1 ? 8 : 6, reg = "cloth", col = cc, w = wHead });
                    var gp = headAt(Hp, 0, 0.62, 0.04);
                    ellipsoid(gb, gp, V(0.017, 0.019, 0.007), new FigOpt { nl = 3, n = 6, reg = "gold", col = col(GOLD), w = wHead });
                    if (hi >= 1) ellipsoid(gb, v3add(gp, V(0, 0, 0.006)), V(0.007, 0.007, 0.004), new FigOpt { nl = 3, n = 5, reg = "white", col = HC(hat, "gem", "#c02a3a"), w = wHead });
                }
                else
                {
                    FCol gc = HC(hat, "color", GOLD); var rr = rim(0.62, 0.3);
                    headRing(X, Hp, rr, 0.036, 0.012, "goldEngr", gc, 0.5);
                    var plates = tp == "mukuta" ? new[] { new[] { -0.62, 0.08, 0.05 }, new[] { 0.62, 0.08, 0.05 }, new[] { 0, 0.13, 0.07 } }
                        : (hi >= 1 ? new[] { new[] { -1.15, 0.05, 0.026 }, new[] { -0.58, 0.06, 0.028 }, new[] { 0, 0.075, 0.03 }, new[] { 0.58, 0.06, 0.028 }, new[] { 1.15, 0.05, 0.026 } } : new[] { new[] { -0.58, 0.06, 0.028 }, new[] { 0, 0.075, 0.03 }, new[] { 0.58, 0.06, 0.028 } });
                    foreach (var pl in plates)
                    {
                        double th = pl[0], h = pl[1], w = pl[2];
                        var p0 = headAt(Hp, th, rr(th) + 0.03, 0.036); var n = headN(p0); double tx = Math.Cos(th), tz = -Math.Sin(th);
                        panel(gb, (u, t) => { double x = (u - 0.5) * w * (1 - t * (tp == "mukuta" ? 0.75 : 0.97)); return V(p0.x + x * tx + n.x * 0.012 * t, p0.y + h * t, p0.z + x * tz + n.z * 0.012 * t); },
                            new FigOpt { nu = 2, nv = hi >= 1 ? 3 : 1, th = 0.004, reg = "gold", col = gc, w = wHead });
                        if (tp == "mukuta" && hi >= 1) ellipsoid(gb, V(p0.x + n.x * 0.01, p0.y + h * 0.38, p0.z + n.z * 0.01), V(0.009, 0.009, 0.005), new FigOpt { nl = 3, n = 5, reg = "white", col = th == 0 ? HC(hat, "gem", "#c02a3a") : col("#2a8a6a"), w = wHead });
                    }
                    if (tp == "mukuta" && hi >= 1) { var kp = headAt(Hp, PI, 0.4, 0.04); foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(kp, V(kp.x + sg * 0.03, kp.y - 0.05, kp.z - 0.04), V(kp.x + sg * 0.05, kp.y - 0.14, kp.z - 0.08), V(kp.x + sg * 0.06, kp.y - 0.26, kp.z - 0.12), 5), 0.03, 0.022, "cloth", HC(hat, "ribbon", "#e8c860"), null, wHeadHair(0.6)); }
                    if (tp == "aksumcrown" && hi >= 1) foreach (int sg in new[] { 1, -1 })
                    {
                        var b0 = headAt(Hp, sg * 1.35, 0.55, 0.04); var pts = bez(b0, V(b0.x + sg * 0.02, b0.y + 0.06, b0.z), V(b0.x + sg * 0.05, b0.y + 0.12, b0.z - 0.01), V(b0.x + sg * 0.07, b0.y + 0.17, b0.z - 0.02), 4);
                        tube(gb, pts, new FigOpt { r = 0.003, n = 3, reg = "gold", col = gc, w = wHead });
                        for (int j = 2; j <= 4; j++) foreach (int sd in new[] { 1, -1 }) ellipsoid(gb, v3add(pts[j], V(sd * 0.008, 0, 0)), V(0.006, 0.011, 0.004), new FigOpt { nl = 3, n = 4, reg = "gold", col = gc, w = wHead, axes = axesFromY(v3norm(V(sd * 0.6, 1, 0))) });
                    }
                }
                res.coversTop = true; res.coversBack = true;
            }
            else if (tp == "kushcrown")
            {
                // 库施王冠：贴头的深色小帽 + 窄金王带 + 额前一对昂首张颈的眼镜蛇 + 脑后两条飘带
                string c2 = HS(hat, "color2");
                FCol capC = c2 != null && lum(c2) < 0.3 ? col(c2) : col("#2a2422");
                FCol gc = HC(hat, "color", GOLD);
                Func<double, double> capRim = th => { double a = Math.Abs(th); return a < 1.0 ? 0.48 : a < 1.3 ? lerp(0.48, 0.05, sstep(1.0, 1.3, a)) : a < 1.65 ? 0.05 : lerp(0.05, -0.66, sstep(1.65, 2.4, a)); };
                headShell(X, Hp, new ShellHO { phi0 = capRim, off = (th, u, ph) => 0.005 + 0.002 * u, reg = "cloth", col = capC, pw = 0.8 });
                var bandP = rim(0.53, 0.28);
                headRing(X, Hp, bandP, 0.0058, 0.0022, "goldEngr", gc, 2.8);
                foreach (int sg in new[] { 1, -1 })
                {
                    double th = sg * 0.15; var p0 = headAt(Hp, th, bandP(th) + 0.01, 0.008); var nn = headN(p0);
                    var fw = v3norm(V(nn.x, 0, nn.z)); var up = V(0, 1, 0); var sd = v3norm(v3cross(up, fw));
                    Func<double, double, V3d> at = (u, f) => v3add(p0, v3add(v3mul(up, u * 1.2), v3mul(fw, f * 1.2)));
                    if (hi == 0) { ellipsoid(gb, at(0.012, 0.004), V(0.006, 0.012, 0.003), new FigOpt { nl = 3, n = 4, reg = "gold", col = gc, w = wHead, axes = new FigAxes(sd, up, fw) }); continue; }
                    tube(gb, bez(at(-0.002, -0.002), at(0.01, 0.009), at(0.014, -0.003), at(0.026, 0.002), 5), new FigOpt { rF = (s2, ii) => 0.0042 - 0.001 * s2, n = 5, reg = "gold", col = gc, w = wHead });
                    // 颈盾：上宽下窄，略向前弓
                    ellipsoid(gb, at(0.033, 0.004), V(0.0105, 0.015, 0.003), new FigOpt { nl = hi >= 2 ? 5 : 3, n = hi >= 2 ? 8 : 5, reg = "goldEngr", col = gc, w = wHead, axes = new FigAxes(sd, v3norm(V(fw.x * 0.2, 1, fw.z * 0.2)), fw) });
                    ellipsoid(gb, at(0.046, 0.009), V(0.005, 0.004, 0.0075), new FigOpt { nl = 3, n = hi >= 2 ? 6 : 4, reg = "gold", col = gc, w = wHead, axes = new FigAxes(sd, up, fw) });
                }
                // 飘带：自王带脑后结处垂下，过肩后
                if (hi >= 1) { var kp = headAt(Hp, PI, bandP(PI), 0.012); foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(kp, V(kp.x + sg * 0.014, kp.y - 0.05, kp.z - 0.03), V(kp.x + sg * 0.026, kp.y - 0.16, kp.z - 0.07), V(kp.x + sg * 0.034, kp.y - 0.3, kp.z - 0.1), 5), 0.022, 0.018, "cloth", HC(hat, "ribbon", "#e8e0d0"), null, wHeadHair(0.6)); }
                res.coversTop = true; res.coversBack = true;
            }
            else if (tp == "bellcrown")
            {
                // 僧伽罗式圆冠（钟形金冠，层层横箍，顶上尖钮）
                FCol cc = HC(hat, "color", GOLD); var c = V(0, apexY(0, 0) - 0.07, HEAD_C.z);
                lathe(gb, new[] { P2(0.088, 0), P2(0.09, 0.03), P2(0.084, 0.06), P2(0.07, 0.09), P2(0.05, 0.115), P2(0.026, 0.135), P2(0.012, 0.15), P2(0.008, 0.19), P2(0.002, 0.22) }, new FigOpt { c = c, n = lod.ring, reg = "goldEngr", col = cc, w = wHead });
                if (hi >= 1) foreach (var yr in new[] { P2(0.03, 0.093), P2(0.075, 0.08) }) tube(gb, ringPts(V(c.x, c.y + yr[0], c.z), V(1, 0, 0), V(0, 0, 1), yr[1], yr[1], Math.Max(10, lod.ring)), new FigOpt { r = 0.0045, n = 4, reg = "gold", col = cc, w = wHead });
                res.coversTop = true;
            }
            else if (tp == "headband" || tp == "diadem" || tp == "featherband")
            {
                FCol cc = tp == "diadem" ? HC(hat, "color", GOLD) : HC(hat, "color", RED);
                // 库施弓手的插羽额带：脑侧一根立起的白羽
                if (tp == "featherband") { var p0 = headAt(Hp, 1.2, 0.5, 0.02); featherStrip(gb, bez(p0, V(p0.x + 0.01, p0.y + 0.08, p0.z - 0.01), V(p0.x + 0.015, p0.y + 0.16, p0.z - 0.03), V(p0.x + 0.02, p0.y + 0.22, p0.z - 0.05), 4), 0.03, 0.014, "feather", HC(hat, "feather", "#f0ece0"), wHead, null, 4); }
                headRing(X, Hp, rim(0.56, 0.3), 0.006, 0.011, tp == "diadem" ? "goldEngr" : "cloth", cc, 0.4);
                if (HS(hat, "gem") != null || tp == "diadem") ellipsoid(gb, headAt(Hp, 0, 0.56, 0.014), V(0.011, 0.013, 0.006), new FigOpt { nl = 4, n = 6, reg = "white", col = HC(hat, "gem", "#2a6db5"), w = wHead });
                if (hi >= 1) { var kp = headAt(Hp, PI, 0.3, 0.015); foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(kp, V(kp.x + sg * 0.03, kp.y - 0.04, kp.z - 0.04), V(kp.x + sg * 0.05, kp.y - 0.12, kp.z - 0.08), V(kp.x + sg * 0.06, kp.y - 0.24, kp.z - 0.12), 5), 0.03, 0.02, "cloth", tp == "diadem" ? col("#e8e0d0") : cc, null, wHeadHair(0.6)); }
            }
            else if (tp == "crown" || tp == "pheasant" || tp == "goldcrown" || tp == "tallcrown")
            {
                FCol cc = HC(hat, "color", GOLD);
                if (tp == "pheasant" || tp == "goldcrown")
                {
                    headRing(X, Hp, rim(0.75, 0.45), 0.012, 0.014, "goldEngr", cc, 0.45);
                    // 前额金饰
                    var c = headAt(Hp, 0, 0.92, 0.02); var AX = axesFromY(v3norm(V(0, 0.5, 1))); double r = 0.035; int start = gb.nv;
                    lathe(gb, new[] { new[] { r, -0.008, double.NaN, 0 }, new[] { r, 0, double.NaN, 0.1 }, new[] { r * 0.6, 0.012, double.NaN, 0.5 }, new[] { 0, 0.016, double.NaN, 1 } }, new FigOpt { c = c, axes = AX, n = lod.ring, reg = "buckle", col = cc, w = wHead });
                    fixDiscUV(gb, FigAtlasLayout.REG("buckle"), c, AX, r, start);
                    ellipsoid(gb, v3add(c, V(0, 0.0, 0.016)), V(0.008, 0.008, 0.006), new FigOpt { nl = 4, n = 6, reg = "white", col = HC(hat, "gem", "#c8302a"), w = wHead });
                }
                if (tp == "pheasant")
                {
                    // 雉翎：两根长尾羽，先向前再向后外方扫开的 S 形
                    foreach (int sg in new[] { 1, -1 })
                    {
                        var p0 = headAt(Hp, sg * 0.35, 1.0, 0.02);
                        featherStrip(gb, bez(p0, V(p0.x + sg * 0.04, p0.y + 0.32, p0.z + 0.06), V(p0.x + sg * 0.26, p0.y + 0.68, p0.z - 0.12), V(p0.x + sg * 0.46, p0.y + 0.76, p0.z - 0.56), hi >= 2 ? 16 : 7),
                            0.032, 0.008, "feather", col("#ffffff"), null, (p, s, t) => wT(t, FigBones.head, 0, FigBones.plume1, 0.15, FigBones.plume2, 0.6), hi >= 2 ? 18 : 6);
                    }
                }
                if (tp == "goldcrown" && hi >= 0)
                {
                    for (int i = -1; i <= 1; i++) { var p0 = headAt(Hp, i * 0.55, 0.8, 0.025); tube(gb, new List<V3d> { p0, V(p0.x, p0.y + 0.12, p0.z + 0.01) }, new FigOpt { r = 0.007, n = 4, reg = "gold", col = cc, w = wHead }); }
                }
                if (tp == "tallcrown")
                {
                    var c = V(0, apexY(0, 0) - 0.04, HEAD_C.z);
                    lathe(gb, new[] { P2(0.08, 0), P2(0.085, 0.03), P2(0.06, 0.05), P2(0.065, 0.08), P2(0.04, 0.12), P2(0.042, 0.15), P2(0.02, 0.2), P2(0.004, 0.26) }, new FigOpt { c = c, n = lod.ring, reg = "gold", col = cc, w = wHead });
                    res.coversTop = true;
                }
            }
            else if (tp == "feathers" || tp == "chief")
            {
                headRing(X, Hp, rim(0.6, 0.35), 0.012, 0.012, "cloth", HC(hat, "color", "#b8862a"), 0.5);
                int n = tp == "chief" ? (hi >= 1 ? 9 : 5) : (hi >= 1 ? 6 : 3);
                var cols = hat.L("colors") ?? new List<object> { "#d8a020", "#c03028", "#ece4d4" };
                for (int i = 0; i < n; i++)
                {
                    double a = lerp(-1.1, 1.1, n > 1 ? (double)i / (n - 1) : 0.5) + PI * 0;
                    var p0 = headAt(Hp, a, 0.6, 0.02); var d = headN(p0);
                    double Lh = (tp == "chief" ? 0.3 : 0.2) * (1 - 0.3 * Math.Abs(a));
                    featherStrip(gb, new List<V3d> { p0, v3add(p0, V(d.x * 0.04, Lh * 0.5, d.z * 0.04 - 0.02)), v3add(p0, V(d.x * 0.09, Lh, d.z * 0.06 - 0.06)) }, 0.03, 0.014, "feather", col((string)cols[i % cols.Count]), wHead, null, 3);
                }
            }
            else if (tp == "wahelm")
            {
                var rm = rim(0.3, -0.25);
                bowl(new ShellHO { phi0 = rm, sy = 0.02, reg = "rivet", col = HC(hat, "color", "#3a3c44") });
                headRing(X, Hp, rm, 0.018, 0.006, "metal", col("#3a3c44"));
                // 衝角（前方鸟喙状脊）
                var secs = new List<FigSec>();
                if (hi >= 1)
                {
                    for (int i = 0; i <= 4; i++) { double t = i / 4.0; var p = headAt(Hp, 0, lerp(0.3, 1.2, t), 0.03); p.z += 0.035 * (1 - t); p.y += 0.01 * t; secs.Add(new FigSec { c = p, X = V(1, 0, 0), Z = headN(p), rx = 0.006, rz = 0.012, t = t }); }
                    loft(gb, secs, new FigOpt { n = 4, reg = "metal", col = col("#3a3c44"), w = wHead });
                }
                for (int j = 0; j < (hi >= 1 ? 3 : 1); j++)
                {
                    int jj = j;
                    panel(gb, (s, t) => { double th = lerp(1.0, TAU - 1.0, s); var p = headAt(Hp, th, -0.2 + 0.02, 0.022 + jj * 0.012); var n = headN(p); return V(p.x + n.x * 0.03 * t, p.y - jj * 0.03 - t * 0.04, p.z + n.z * 0.035 * t); },
                        new FigOpt { nu = Math.Max(3, lod.pn), nv = 1, th = 0.004, reg = "rivet", col = col("#3a3c44"), wP = (p, s, t) => new[] { new FBW(FigBones.head, 1 - jj * 0.15), new FBW(FigBones.neck, jj * 0.15) } });
                }
                res.coversTop = true;
            }
            else if (tp == "jeolpung")
            {
                headShell(X, Hp, new ShellHO { phi0 = rim(0.55, 0.2), off = (th, u, ph) => 0.01 + 0.004 * u, apex = 0.07, reg = "cloth", col = HC(hat, "color", "#c84a2a") });
                foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(headAt(Hp, sg * 1.3, 0.6, 0.02), V(sg * 0.1, 0.3, -0.01), V(sg * 0.11, 0.38, -0.03), V(sg * 0.1, 0.46, -0.05), 4), 0.025, 0.01, "feather", col("#ffffff"), wHead, null, 4);
                res.coversTop = true;
            }
            else if (tp == "felt" || tp == "furhat" || tp == "pointcap" || tp == "tallhat" || tp == "phrygian" || tp == "tiara" || tp == "conical" || tp == "spangen" || tp == "celthelm")
            {
                var dcol = new Dictionary<string, string> { { "felt", "#e0d4b8" }, { "furhat", "#5a4a3a" }, { "pointcap", "#e8dcc0" }, { "tallhat", "#e8dcc0" }, { "phrygian", "#9a2a24" }, { "tiara", "#2a2a5a" }, { "conical", "#8a909a" }, { "spangen", "#8a909a" }, { "celthelm", BRONZE } };
                FCol cc = HC(hat, "color", dcol[tp]);
                bool metalH = tp == "conical" || tp == "spangen" || tp == "celthelm";
                var dap = new Dictionary<string, double> { { "felt", 0.03 }, { "furhat", 0.03 }, { "pointcap", 0.16 }, { "tallhat", 0.13 }, { "phrygian", 0.08 }, { "tiara", 0.1 }, { "conical", 0.12 }, { "spangen", 0.08 }, { "celthelm", 0.02 } };
                double ap = dap[tp];
                var rm = rim(0.38, -0.12);
                headShell(X, Hp, new ShellHO { phi0 = rm, off = (th, u, ph) => 0.013 + (metalH ? 0.006 : 0.008) * u, apex = ap, tilt = tp == "phrygian" ? 0.09 : tp == "pointcap" ? -0.03 : 0, reg = metalH ? "metal" : "cloth", col = cc, pw = tp == "tallhat" ? 0.5 : 0.85 });
                if (tp == "furhat" || tp == "felt") headRing(X, Hp, rm, 0.022, tp == "furhat" ? 0.024 : 0.012, tp == "furhat" ? "fur" : "cloth", tp == "furhat" ? col("#8a6a48") : shade(cc, -0.2));
                if (tp == "tallhat" || tp == "tiara" || tp == "conical" || tp == "spangen") headRing(X, Hp, rm, 0.016, 0.008, "goldEngr", col(GOLD));
                if (tp == "celthelm") { var top = V(0, apexY(0, 0.02), HEAD_C.z); ellipsoid(gb, top, V(0.015, 0.02, 0.015), new FigOpt { nl = 4, n = 6, reg = "gold", col = col(BRONZE), w = wHead }); }
                if (tp == "felt" || tp == "tiara" || tp == "conical" || tp == "spangen")
                {
                    // 护耳 / 护颈垂片（锁子 / 毡）
                    string reg = tp == "felt" ? "cloth" : tp == "tiara" ? "cloth" : "mail";
                    panel(gb, (s, t) => { double th = lerp(0.9, TAU - 0.9, s); var p = headAt(Hp, th, rm(th) + 0.02, 0.018); var n = headN(p); return V(p.x + n.x * 0.035 * t, p.y - t * 0.11, p.z + n.z * 0.04 * t); },
                        new FigOpt { nu = Math.Max(3, lod.pn + 1), nv = Math.Max(1, lod.pv - 6), th = 0.006, reg = reg, col = reg == "mail" ? col("#8a909a") : cc, wP = (p, s, t) => new[] { new FBW(FigBones.head, 1 - 0.4 * t), new FBW(FigBones.neck, 0.4 * t) } });
                }
                if (tp == "spangen" && hi >= 1) tube(gb, new List<V3d> { headAt(Hp, 0, 0.35, 0.02), headAt(Hp, 0, 0.0, 0.035) }, new FigOpt { r = 0.006, n = 4, reg = "metal", col = col("#8a909a"), w = wHead });
                if (tp == "tiara" && hi >= 1)
                {
                    // 金珠钉在帽壳表面（与 headShell 同一套偏移与拔顶）
                    double p0 = rm(PI);
                    for (int i = 0; i < 5; i++)
                    {
                        double ph = lerp(Math.Max(p0 + 0.08, 0.5), 1.3, i / 4.0), u = Math.Pow(clamp01((ph - p0) / (PI / 2 - 0.001 - p0)), 1 / 0.85);
                        var p = headAt(Hp, PI, ph, 0.013 + 0.008 * u + 0.006);
                        p.y += ap * Math.Pow(u, 3);
                        ellipsoid(gb, p, V(0.006, 0.012, 0.012), new FigOpt { nl = 3, n = 5, reg = "gold", col = col(GOLD), w = wHead });
                    }
                }
                res.coversTop = true;
            }
            else if (tp == "keffiyeh")
            {
                FCol cc = HC(hat, "color", "#ece6da");
                var rm = rim(0.5, -0.4);
                headShell(X, Hp, new ShellHO { phi0 = rm, off = (th, u, ph) => 0.012 + 0.006 * u, reg = "cloth", col = cc });
                panel(gb, (s, t) => { double th = lerp(1.0, TAU - 1.0, s); var p = headAt(Hp, th, rm(th) + 0.04, 0.016); var n = headN(p); return V(p.x + n.x * 0.06 * t, p.y - t * 0.18, p.z + n.z * 0.06 * t - 0.02 * t); },
                    new FigOpt { nu = Math.Max(3, lod.pn + 1), nv = Math.Max(2, lod.pv - 5), th = 0.006, reg = "cloth", col = cc, wP = (p, s, t) => new[] { new FBW(FigBones.head, 1 - 0.6 * t), new FBW(FigBones.neck, 0.6 * t) } });
                headRing(X, Hp, rim(0.75, 0.45), 0.018, 0.009, "cloth", col("#1c1a1e"));
                if (hi >= 1) headRing(X, Hp, rim(0.66, 0.36), 0.019, 0.008, "cloth", col("#1c1a1e"));
                res.coversTop = true; res.coversBack = true;
            }
            else if (tp == "laurel")
            {
                int n = hi >= 1 ? 18 : 8;
                for (int i = 0; i < n; i++)
                {
                    double th = lerp(-PI * 0.88, PI * 0.88, (double)i / (n - 1)); var p = headAt(Hp, th, lerp(0.72, 0.5, Math.Abs(th) / PI), 0.018);
                    ellipsoid(gb, p, V(0.007, 0.018, 0.006), new FigOpt { nl = 3, n = 5, reg = "gold", col = col("#d8b850"), w = wHead, axes = axesFromY(v3norm(V(Math.Cos(th) * 0.4, 0.8, -Math.Sin(th) * 0.4))) });
                }
            }
            else if (tp == "shaman")
            {
                headRing(X, Hp, rim(0.62, 0.3), 0.014, 0.012, "cloth", col("#e8dcc0"), 0.5);
                var c = headAt(Hp, 0, 0.75, 0.03); var AX = axesFromY(v3norm(V(0, 0.3, 1))); double r = 0.04; int start = gb.nv;
                lathe(gb, new[] { new[] { r, -0.006, double.NaN, 0 }, new[] { r, 0, double.NaN, 0.1 }, new[] { r * 0.8, 0.006, double.NaN, 0.4 }, new[] { 0, 0.008, double.NaN, 1 } }, new FigOpt { c = c, axes = AX, n = lod.ring, reg = "mirror", col = col(BRONZE), w = wHead });
                fixDiscUV(gb, FigAtlasLayout.REG("mirror"), c, AX, r, start);
            }
            return res;
        }

        // ------------------------------------------------------------- 头发 --
        static void buildHair(FigCtx X, HeadP Hp, HGRes cov)
        {
            var gb = X.gb; var lod = X.lod; var look = X.look;
            string hs = look.hair.S("style") ?? "topknot";
            if (hs == "") hs = "topknot";
            if (hs == "bald") return;
            string hcS = look.hair.S("color"); FCol hc = col(string.IsNullOrEmpty(hcS) ? "#1c1816" : hcS);
            double vol = look.hair.Or("vol", 1.0);
            double recede = look.hair.Or("recede", 0.0);
            var wHead = W1(FigBones.head);
            // 发壳（被盔帽完全盖住时省略）
            if (!cov.coversTop)
            {
                var thickT = new Dictionary<string, double> { { "short", 0.006 }, { "bob", 0.012 }, { "curly", 0.014 }, { "bushy", 0.02 }, { "wild", 0.024 }, { "spiky", 0.012 }, { "kunfa", 0.004 }, { "romanf", 0.012 }, { "tightcurl", 0.007 }, { "ringlets", 0.014 } };
                double thick; if (!thickT.TryGetValue(hs, out thick) || thick == 0) thick = 0.01;
                headShell(X, Hp, new ShellHO
                {
                    phi0 = th => hairline(th, hs, recede), reg = "hair", col = hc, pw = 0.9,
                    off = (th, u, ph) => (0.003 + thick * vol * sstep(0, 0.25, ph - hairline(th, hs, recede))) * (hs == "curly" || hs == "wild" || hs == "ringlets" ? 1 + 0.3 * Math.Sin(th * 7) * Math.Sin(ph * 9) : hs == "tightcurl" ? 1 + 0.25 * Math.Sin(th * 13) * Math.Sin(ph * 15) : 1),
                });
            }
            Action<V3d, V3d, bool, string> bunAt = (p, r, noTie, tie) =>
            {
                ellipsoid(gb, p, r, new FigOpt { nl = Math.Max(4, lod.hand[0]), n = Math.Max(6, lod.hand[1]), reg = "hair", col = hc, w = wHead });
                if (X.hi >= 1 && !noTie) headRingSmall(X, p, r.x * 1.02, r.y * -0.55, col(string.IsNullOrEmpty(tie) ? "#c03028" : tie));
            };
            string tieS = look.hair.S("tie");
            if (hs == "topknot" || hs == "highbun" || hs == "mallet")
            {
                if (cov.coversTop && hs == "topknot" && !cov.showBun) return;
                var p = hs == "mallet" ? headAt(Hp, PI, 0.75, 0.04) : headAt(Hp, PI, cov.showBun ? 1.47 : 1.38, cov.showBun ? 0.034 : 0.03);
                if (hs != "mallet") p.y += hs == "highbun" ? 0.05 : cov.showBun ? 0.012 : 0.025;
                // 露出冠帽的发髻贴着帽顶，并有一小段发根连到头顶
                if (cov.showBun && hs != "mallet") tube(gb, new List<V3d> { headAt(Hp, PI, 1.47, 0.012), p }, new FigOpt { r = 0.022, n = Math.Max(6, lod.hand[1]), reg = "hair", col = hc, w = wHead });
                bool crown = look.hat != null && look.hat.S("type") == "crown";
                bunAt(p, hs == "mallet" ? V(0.045, 0.042, 0.042) : V(0.036, 0.032, 0.036), crown, tieS);
                if (crown)
                {
                    // 束发冠：包住发髻的金冠 + 横簪
                    var q = new[] { P2(0.03, -0.022), P2(0.042, -0.012), P2(0.044, 0.012), P2(0.036, 0.03), P2(0.03, 0.036) };
                    var pr4 = new double[q.Length][]; for (int i = 0; i < q.Length; i++) pr4[i] = new[] { q[i][0], q[i][1], double.NaN, i / 4.0 };
                    lathe(gb, pr4, new FigOpt { c = p, n = Math.Max(8, X.lod.ring), reg = "goldEngr", col = HC(look.hat, "color", GOLD), w = wHead, sz = 0.85 });
                    if (HS(look.hat, "gem") != null) ellipsoid(gb, V(p.x, p.y + 0.0, p.z + 0.04), V(0.008, 0.008, 0.005), new FigOpt { nl = 3, n = 5, reg = "white", col = col(look.hat.S("gem")), w = wHead });
                }
                if (X.hi >= 1 && (hs == "topknot" || crown)) tube(gb, new List<V3d> { V(p.x - (crown ? 0.075 : 0.05), p.y - 0.004, p.z), V(p.x + (crown ? 0.075 : 0.05), p.y - 0.004, p.z) }, new FigOpt { r = crown ? 0.005 : 0.0035, n = 4, reg = "gold", col = HC(look.hair, "pin", GOLD), w = wHead });
            }
            else if (hs == "bun" || hs == "romanf" || hs == "twinbun")
            {
                if (hs == "twinbun") foreach (int sg in new[] { 1, -1 }) bunAt(headAt(Hp, sg * 1.1, 1.1, 0.03), V(0.03, 0.03, 0.03), false, tieS);
                else bunAt(headAt(Hp, PI, 0.55, 0.045), V(0.042, 0.038, 0.035), false, tieS);
            }
            else if (hs == "mizura")
            {
                foreach (int sg in new[] { 1, -1 })
                {
                    var c = headAt(Hp, sg * 1.5, -0.25, 0.025);
                    tube(gb, bez(V(c.x, c.y + 0.06, c.z - 0.005), V(c.x + sg * 0.05, c.y + 0.04, c.z), V(c.x + sg * 0.05, c.y - 0.06, c.z), V(c.x + sg * 0.005, c.y - 0.05, c.z - 0.005), X.hi >= 1 ? 6 : 3), new FigOpt { r = 0.014, n = X.hi >= 1 ? 6 : 4, reg = "hair", col = hc, w = wHead });
                    if (X.hi >= 1) tube(gb, new List<V3d> { V(c.x + sg * 0.03, c.y - 0.004, c.z - 0.01), V(c.x + sg * 0.03, c.y + 0.012, c.z - 0.01) }, new FigOpt { r = 0.016, n = 6, reg = "cloth", col = col("#e8dcc0"), w = wHead });
                }
            }
            else if (hs == "suebian")
            {
                var c = headAt(Hp, -1.9, 0.5, 0.04);
                ellipsoid(gb, c, V(0.03, 0.036, 0.034), new FigOpt { nl = X.hi == 0 ? 3 : 5, n = X.hi == 0 ? 5 : 7, reg = "hair", col = hc, w = wHead });
                tube(gb, bez(c, V(c.x - 0.02, c.y - 0.04, c.z - 0.02), V(c.x - 0.02, c.y - 0.09, c.z - 0.04), V(c.x - 0.01, c.y - 0.14, c.z - 0.05), X.hi == 0 ? 2 : 5),
                    new FigOpt { rF = (s, ii) => 0.02 * (1 - s) + 0.006, n = X.hi == 0 ? 4 : 6, reg = "hair", col = hc, wT = (s, pt) => new[] { new FBW(FigBones.head, 1 - s * 0.5), new FBW(FigBones.hair1, s * 0.5) } });
            }
            // 长发 / 辫 / 髡发两鬓
            if (hs == "long" || hs == "wahair" || hs == "wild" || hs == "bushy" || hs == "bob" || hs == "romanf" || hs == "ringlets")
            {
                var lenT = new Dictionary<string, double> { { "long", 0.28 }, { "wahair", 0.36 }, { "wild", 0.18 }, { "bushy", 0.12 }, { "bob", 0.07 }, { "romanf", 0.05 }, { "ringlets", 0.13 } };
                double len = lenT[hs] * (look.sex == "f" ? 1.2 : 1);
                if (!(cov.coversBack && len < 0.15)) panel(gb, (s, t) =>
                {
                    double th = lerp(1.6, TAU - 1.6, s);
                    var p = headAt(Hp, th, lerp(0.2, -0.45, t), 0.012 + 0.012 * vol);
                    var n = headN(p);
                    double yy = t < 0.5 ? p.y : lerp(p.y, p.y - len, (t - 0.5) * 2);
                    return V(p.x * (1 + 0.15 * t) + n.x * 0.01 * t, t < 0.5 ? p.y : yy, p.z + n.z * 0.02 * t - (t > 0.5 ? (t - 0.5) * 0.06 : 0));
                }, new FigOpt { nu = Math.Max(3, lod.pn + 1), nv = Math.Max(2, lod.pv - 4), th = 0.008, reg = "hair", col = hc, wP = (p, s, t) => wT(t, FigBones.head, 0.3, FigBones.hair1, 0.9), uv = (s, t) => P2(s, 1 - t) });
            }
            if (hs == "braids" || hs == "koreanbraid" || hs == "kunfa")
            {
                foreach (int sg in hs == "koreanbraid" ? new[] { 0 } : new[] { 1, -1 })
                {
                    var p0 = hs == "kunfa" ? headAt(Hp, sg * 1.35, 0.2, 0.02) : headAt(Hp, sg * 2.2, -0.1, 0.02);
                    double Lb = hs == "kunfa" ? 0.14 : 0.3;
                    var pts = bez(p0, V(p0.x + sg * 0.02, p0.y - Lb * 0.3, p0.z - 0.02), V(p0.x + sg * 0.03, p0.y - Lb * 0.7, p0.z - 0.0), V(p0.x + sg * 0.03, p0.y - Lb, p0.z + 0.02), X.hi == 0 ? 2 : 6);
                    tube(gb, pts, new FigOpt { rF = (s, ii) => (0.012 - 0.004 * s) * (X.hi == 0 ? 1.1 : 1 + 0.25 * Math.Sin(s * 40)), n = X.hi == 0 ? 4 : 6, reg = "hair", col = hc, wT = (s, pt) => wT(s, FigBones.head, 0.2, FigBones.hair1, 1) });
                }
            }
        }
        static void headRingSmall(FigCtx X, V3d c, double r, double dy, FCol cc)
        {
            var pts = new List<V3d>();
            for (int i = 0; i <= 10; i++) { double a = i / 10.0 * TAU; pts.Add(V(c.x + Math.Sin(a) * r, c.y + dy, c.z + Math.Cos(a) * r)); }
            tube(X.gb, pts, new FigOpt { r = 0.004, n = 4, reg = "cloth", col = cc, w = W1(FigBones.head) });
        }

        // ------------------------------------------------------------- 胡须 --
        static void buildBeard(FigCtx X, HeadP Hp)
        {
            var gb = X.gb; var lod = X.lod; var look = X.look;
            string st = look.beard.S("style"); if (string.IsNullOrEmpty(st)) st = "none";
            string bcS = Js.Or(look.beard.S("color"), Js.Or(look.hair.S("color"), "#1c1816")); FCol bc = col(bcS);
            double len = look.beard.Or("len", 1.0);
            string ms = look.mus.S("style"); if (string.IsNullOrEmpty(ms)) ms = "none";
            if (look.sex == "f") return;
            var wHead = W1(FigBones.head);
            double mouthP = FigureData.FEAT_mouthP, noseP = FigureData.FEAT_noseP;
            // 下颌须壳：θ ∈ [−w, w]，φ 从脸颊 / 下唇下方 到下巴之下
            Action<double, double, double, bool> shell = (w, thick, extra, bristle) =>
            {
                Func<double, double> topPhi = th => { double a = Math.Abs(th) / w; return lerp(mouthP - 0.13, -0.12, sstep(0.25, 1, a)); };
                panel(gb, (s, t) =>
                {
                    double th = lerp(-w, w, s), ph = lerp(topPhi(th), -1.48, t);
                    var p = headPoint(th, ph, Hp); var n = headN(p);
                    double edge = Math.Min(sstep(0, 0.25, t), sstep(0, 0.12, Math.Min(s, 1 - s)));
                    double d = 0.002 + thick * edge;
                    if (bristle) d *= 1 + 0.45 * Math.Sin(th * 13) * Math.Sin(ph * 11);
                    var q = v3madd(p, n, d);
                    q.y -= extra * Math.Pow(t, 2) * gauss(th, 0, 0.9);
                    q.z += extra * 0.4 * Math.Pow(t, 2) * gauss(th, 0, 0.9);
                    return q;
                }, new FigOpt { nu = X.hi == 0 ? 3 : Math.Max(3, lod.pn + 2), nv = Math.Max(2, lod.pv - 4), th = 0.004, reg = "hair", col = bc, colB = shade(bcS, -0.3), w = wHead, uv = (s, t) => P2(s, 1 - t) });
            };
            // 垂须：自下巴垂下的锥形（美髯 / 长须 / 分叉）
            Action<double, double, bool, bool> hang = (L, w0, fork, curl) =>
            {
                var c = headPoint(0, -1.05, Hp);
                int N = X.hi >= 2 ? 10 : X.hi >= 1 ? 5 : 3;
                foreach (int f in fork ? new[] { -1, 1 } : new[] { 0 })
                {
                    var pts = new List<V3d>();
                    for (int i = 0; i <= N; i++) { double t = (double)i / N; pts.Add(V(c.x + f * 0.018 * t, c.y + 0.01 - L * t, c.z + 0.012 + 0.03 * Math.Sin(t * 2.4) + (curl ? 0.01 * Math.Sin(t * 12) : 0))); }
                    tube(gb, pts, new FigOpt { rF = (s, ii) => (w0 * Math.Sin(PI * Math.Min(1, 0.25 + s * 0.85)) * (1 - s * 0.6) + 0.002) * (fork ? 0.7 : 1), rz = 0.55, n = Math.Max(5, lod.tube), reg = "hair", col = bc, up = V(0, 0, 1),
                        wT = (s, pt) => L > 0.2 ? wT(s, FigBones.head, 0.25, FigBones.neck, 0.7) : wHead });
                }
            };
            // 簇：自下颌向外下方伸出的尖锥（虬髯 / 满腮胡的蓬松轮廓）
            Action<int, double, double, double> tufts = (n, L, r0, spread) =>
            {
                for (int i = 0; i < n; i++)
                {
                    double a = lerp(-spread, spread, n > 1 ? (double)i / (n - 1) : 0.5), ph = lerp(-0.95, -1.35, Math.Abs(a) / Math.Max(spread, 1e-3));
                    var p = headPoint(a, ph, Hp); var nn = headN(p); var dir = v3norm(V(nn.x * 0.9, -0.75 - 0.2 * Math.Cos(a * 2), nn.z * 0.7));
                    double Ln = L * (1 - 0.25 * Math.Abs(a) / Math.Max(spread, 1e-3)) * (0.85 + 0.3 * ((i * 0.618) % 1));
                    tube(gb, new List<V3d> { v3madd(p, nn, -0.004), v3madd(p, dir, Ln * 0.5), v3madd(p, dir, Ln) }, new FigOpt { rF = (q, ii) => r0 * (1 - q) + 0.0012, rz = 0.7, n = X.hi >= 2 ? 5 : 4, reg = "hair", col = bc, w = wHead, up = V(0, 0, 1) });
                }
            };
            if (st == "goatee") { hang(0.05 * len, 0.018, false, false); }
            else if (st == "short") shell(1.35, 0.008 * len, 0.004, false);
            else if (st == "full" || st == "bristle" || st == "curled" || st == "braided")
            {
                shell(1.42, 0.016 * len, 0.03 * len, st == "bristle");
                if (X.hi >= 1 && (st == "bristle" || st == "full")) tufts(X.hi >= 2 ? 9 : 5, (st == "bristle" ? 0.05 : 0.04) * len, st == "bristle" ? 0.011 : 0.013, 1.2);
                if (st == "braided") hang(0.12 * len, 0.016, false, true);
            }
            else if (st == "long" || st == "flowing" || st == "forked")
            {
                shell(1.3, 0.008 * len, 0.008, false);
                hang((st == "flowing" ? 0.3 : st == "forked" ? 0.16 : 0.15) * len, st == "flowing" ? 0.04 : 0.028, st == "forked", false);
                // 美髯两侧的鬓须：自腮边贴着脸颊汇入主须
                if (st == "flowing" && X.hi >= 1) foreach (int sg in new[] { 1, -1 })
                {
                    var p0 = headPoint(sg * 0.75, -0.7, Hp); var c0 = headPoint(0, -1.05, Hp);
                    tube(gb, new List<V3d> { v3madd(p0, headN(p0), -0.003), V(lerp(p0.x, c0.x, 0.55), p0.y - 0.07 * len, p0.z + 0.025), V(sg * 0.012, c0.y - 0.12 * len, c0.z + 0.035) },
                        new FigOpt { rF = (s, ii) => 0.013 * (1 - s) + 0.004, rz = 0.55, n = 5, reg = "hair", col = bc, wT = (s, pt) => wT(s, FigBones.head, 0.3, FigBones.neck, 0.9) });
                }
            }
            // 八字胡
            if (ms != "none" && X.hi >= 0)
            {
                foreach (int sg in new[] { 1, -1 })
                {
                    var a = headPoint(sg * 0.06, noseP - 0.14, Hp); var b = headPoint(sg * 0.3, mouthP + 0.06, Hp); var c = headPoint(sg * 0.44, ms == "droop" ? mouthP - 0.32 : ms == "curl" ? mouthP + 0.12 : mouthP - 0.06, Hp);
                    Func<V3d, V3d> off = p => v3madd(p, headN(p), 0.004);
                    double thk = ms == "thin" ? 0.0035 : ms == "bristle" ? 0.008 : 0.006;
                    tube(gb, new List<V3d> { off(a), off(v3lerp(a, b, 0.5)), off(b), off(c) }, new FigOpt { rF = (s, ii) => thk * (1 - s * 0.6), rz = 0.6, n = 4, reg = "hair", col = bc, w = wHead });
                }
            }
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    // 兵器信息（= JS info / mesh.userData.weapon）：原点为右手握点，杆 / 刃沿 +Y（three 坐标）
    public sealed class FigWeaponInfo
    {
        public string id, name, style;
        public double len = 1;
        public V3d tip = new V3d(0, 1, 0), @base = new V3d(0, 0.1, 0);
        public V3d? off;                        // 左手握点（双手兵器）
        public V3d? str, nockTop, nockBot;      // 弓：弦中点、上下弓梢
    }
    public sealed class FigShieldInfo { public double r; }

    public static partial class FigBuild
    {
        // ======================================================== 兵器与盾 ==
        // 兵器局部坐标：原点 = 右手握点，杆 / 刃沿 +Y（尖端方向），刃口朝 +Z，厚度方向 X
        sealed class WK { public FigGB gb; public FigLod lod; public FBW[] w; public int hi; }
        static void shaft(WK K, double y0, double y1, double r, string reg, string cc, double taper = 0)
        {
            tube(K.gb, K.hi == 0 ? new List<V3d> { V(0, y0, 0), V(0, y1, 0) } : new List<V3d> { V(0, y0, 0), V(0, (y0 + y1) / 2, 0), V(0, y1, 0) },
                new FigOpt { rF = (s, i) => r * (1 - taper * s) * (K.hi == 0 ? 1.15 : 1), n = K.hi == 0 ? 4 : Math.Max(5, K.lod.tube), reg = reg, col = col(cc), w = K.w, capStart = K.hi >= 1, capEnd = K.hi >= 1 });
        }
        static void ringAtY(WK K, double y, double r, double h, string reg = null, string cc = null)
        {
            if (K.hi == 0) return;
            lathe(K.gb, new[] { new[] { r * 0.85, y - h / 2, double.NaN, 0 }, new[] { r, y - h * 0.3, double.NaN, 0.25 }, new[] { r, y + h * 0.3, double.NaN, 0.75 }, new[] { r * 0.85, y + h / 2, double.NaN, 1 } },
                new FigOpt { n = Math.Max(6, K.lod.tube), reg = reg ?? "goldEngr", col = col(cc ?? GOLD), w = K.w });
        }
        static void spikeAt(WK K, double y, double len, double r, string cc, bool down = false)
        {
            if (K.hi == 0) return;
            var prof = down ? new[] { P2(0, y - len), P2(r * 0.5, y - len * 0.6), P2(r, y - 0.01), P2(r * 0.9, y) } : new[] { P2(r * 0.9, y), P2(r, y + 0.01), P2(r * 0.5, y + len * 0.6), P2(0, y + len) };
            lathe(K.gb, prof, new FigOpt { n = Math.Max(5, K.lod.tube), reg = "brushed", col = col(cc ?? STEEL), w = K.w, capStart = !down, capEnd = down });
        }
        // 缨：套在杆上向下垂的马尾（y 处，长 len）
        static void shaftTassel(WK K, double y, double len, double r, string cc)
        {
            var prof = K.hi == 0 ? new[] { P2(0.012, y + 0.01), P2(r, y - len * 0.45), P2(r * 0.4, y - len), P2(0.012, y - len * 0.7) }
                : new[] { P2(0.012, y + 0.01), P2(r * 0.6, y - len * 0.15), P2(r, y - len * 0.45), P2(r * 0.85, y - len * 0.85), P2(r * 0.4, y - len), P2(0.012, y - len * 0.7) };
            Array.Reverse(prof);
            lathe(K.gb, prof, new FigOpt { n = K.hi == 0 ? 4 : Math.Max(6, K.lod.tube + 2), reg = "tassel", col = col(cc ?? RED), w = K.w });
        }
        // 刃：沿 +Y 的站点（y、刃口宽、刃背宽、半厚、z 偏移）
        static void bladeY(WK K, double[][] pts, bool dbl, string cc = null)
        {
            var st = new List<FigBladeSt>();
            foreach (var q in pts) st.Add(new FigBladeSt { p = V(0, q[0], q.Length > 4 ? q[4] : 0), e = V(0, 0, 1), n = V(1, 0, 0), we = q[1], wb = q[2], th = q[3], dbl = dbl });
            // 弯刃：刃口方向随中线切向旋转
            for (int i = 0; i < st.Count; i++)
            {
                var a = st[Math.Max(0, i - 1)].p; var b = st[Math.Min(st.Count - 1, i + 1)].p;
                var T = v3norm(v3sub(b, a));
                st[i].e = v3norm(-v3cross(V(1, 0, 0), T));
                if (st[i].e.z < 0) st[i].e = -st[i].e;
            }
            blade(K.gb, st, new FigOpt { col = col(cc ?? "#ffffff"), w = K.w });
        }
        static void dragonHead(WK K, double y, string cc, double s = 1)
        {
            var gb = K.gb; FCol c = col(cc ?? GOLD);
            ellipsoid(gb, V(0, y, 0.01 * s), V(0.032 * s, 0.05 * s, 0.04 * s), new FigOpt { nl = Math.Max(4, K.lod.hand[0]), n = Math.Max(6, K.lod.hand[1]), reg = "gold", col = c, w = K.w });
            // 吻部（朝刃口方向）与双角
            tube(gb, new List<V3d> { V(0, y + 0.01 * s, 0.03 * s), V(0, y + 0.03 * s, 0.07 * s) }, new FigOpt { rF = (t, i) => (0.022 - 0.01 * t) * s, n = 6, reg = "gold", col = c, w = K.w, capEnd = true });
            if (K.hi >= 1) foreach (int sg in new[] { 1, -1 }) tube(gb, bez(V(sg * 0.015 * s, y + 0.03 * s, -0.01 * s), V(sg * 0.03 * s, y + 0.06 * s, -0.03 * s), V(sg * 0.03 * s, y + 0.09 * s, -0.05 * s), V(sg * 0.02 * s, y + 0.12 * s, -0.06 * s), 4), new FigOpt { rF = (t, i) => 0.007 * (1 - t) + 0.002, n = 4, reg = "gold", col = c, w = K.w });
        }
        public static string WeaponStyle(string id) { var d = FigTables.WEAPONS.O(id) ?? FigTables.WEAPONS.O("jian"); return d.S("style"); }
        public static bool HasWeapon(string id) { return id != null && FigTables.WEAPONS.Has(id); }

        public static FigWeaponInfo buildWeaponInto(FigGB gb, string id, FigLod lod, FBW[] w)
        {
            var K = new WK { gb = gb, lod = lod, w = w ?? W1(FigBones.handR), hi = lod.detail };
            var def = FigTables.WEAPONS.O(id) ?? FigTables.WEAPONS.O("jian");
            var info = new FigWeaponInfo { id = id, name = def.S("name"), style = def.S("style"), len = 1, tip = V(0, 1, 0), off = null, @base = V(0, 0.1, 0) };
            string SH = "#3a2418";
            string sw = FigTables.DUAL_BASE.S(id) ?? id;
            switch (sw)
            {
                case "guandao":
                    {
                        shaft(K, -0.9, 1.02, 0.017, "wood", "#2a3a2c");
                        spikeAt(K, -0.9, 0.1, 0.017, GOLD, true);
                        foreach (double y in new[] { -0.82, 0.25, 0.95 }) ringAtY(K, y, 0.021, 0.04);
                        shaftTassel(K, 1.0, 0.16, 0.05, "#c8302a");
                        dragonHead(K, 1.07, "#5aa070");
                        bladeY(K, new[] { new[] { 1.1, 0.06, 0.02, 0.009, 0 }, new[] { 1.22, 0.1, 0.022, 0.008, -0.005 }, new[] { 1.36, 0.14, 0.02, 0.007, -0.012 }, new[] { 1.5, 0.15, 0.018, 0.006, -0.025 }, new[] { 1.62, 0.12, 0.015, 0.005, -0.045 }, new[] { 1.72, 0.06, 0.01, 0.004, -0.07 }, new[] { 1.78, 0.0, 0.0, 0.002, -0.1 } }, false);
                        // 刀背倒钩
                        if (K.hi >= 1) tube(gb, bez(V(0, 1.3, -0.02), V(0, 1.31, -0.06), V(0, 1.34, -0.085), V(0, 1.38, -0.095), 4), new FigOpt { rF = (t, i) => 0.01 * (1 - t) + 0.001, rz = 0.4, n = 4, reg = "blade", col = new FCol(1, 1, 1), w = K.w });
                        info.len = 1.78; info.tip = V(0, 1.7, 0.02); info.@base = V(0, 1.15, 0); info.off = V(0, 0.6, 0);
                        break;
                    }
                case "snake":
                    {
                        shaft(K, -1.0, 1.42, 0.016, "wood", "#1e1a1e");
                        spikeAt(K, -1.0, 0.08, 0.016, GOLD, true);
                        foreach (double y in new[] { -0.9, 0.3, 1.35 }) ringAtY(K, y, 0.02, 0.035);
                        shaftTassel(K, 1.4, 0.15, 0.045, "#c8302a");
                        ringAtY(K, 1.43, 0.022, 0.05, "gold", GOLD);
                        var pts = new double[13][];
                        for (int i = 0; i <= 12; i++) { double t = i / 12.0; pts[i] = new[] { 1.46 + t * 0.52, 0.04 * (1 - t * 0.85), 0.04 * (1 - t * 0.85), 0.006, 0.03 * Math.Sin(t * PI * 3) * (1 - t * 0.6) }; }
                        pts[12][1] = pts[12][2] = 0;
                        bladeY(K, pts, true);
                        info.len = 1.98; info.tip = V(0, 1.95, 0); info.@base = V(0, 1.46, 0); info.off = V(0, 0.65, 0);
                        break;
                    }
                case "halberd":
                    {
                        shaft(K, -0.95, 1.38, 0.017, "wood", "#5a1a1a");
                        spikeAt(K, -0.95, 0.09, 0.017, GOLD, true);
                        foreach (double y in new[] { -0.85, 0.2, 1.28 }) ringAtY(K, y, 0.021, 0.04);
                        ringAtY(K, 1.37, 0.026, 0.06, "gold", GOLD);
                        bladeY(K, new[] { new[] { 1.4, 0.03, 0.03, 0.008 }, new[] { 1.5, 0.036, 0.036, 0.007 }, new[] { 1.65, 0.03, 0.03, 0.006 }, new[] { 1.78, 0.014, 0.014, 0.005 }, new[] { 1.86, 0, 0, 0.002 } }, true);
                        // 双月牙
                        foreach (int sg in new[] { 1, -1 })
                        {
                            var st = new List<FigBladeSt>();
                            for (int i = 0; i <= 8; i++)
                            {
                                double a = lerp(-1.25, 1.25, i / 8.0), R = 0.13;
                                var c = V(0, 1.36 + Math.Sin(a) * R, sg * (0.035 + Math.Cos(a) * R * 0.75));
                                var e = v3norm(V(0, Math.Sin(a) * 0.75, sg * Math.Cos(a)));
                                double wa = 0.045 * (1 - Math.Pow(Math.Abs(a) / 1.25, 2)) + 0.004;
                                st.Add(new FigBladeSt { p = c, e = e, n = V(1, 0, 0), we = wa, wb = 0.012, th = 0.006, dbl = false });
                            }
                            st[0].we = st[8].we = 0.003;
                            blade(gb, st, new FigOpt { col = col("#f0f0f0"), w = K.w });
                            tube(gb, new List<V3d> { V(0, 1.36, 0), V(0, 1.36, sg * 0.05) }, new FigOpt { r = 0.012, n = 5, reg = "gold", col = col(GOLD), w = K.w });
                        }
                        // 彩带
                        if (K.hi >= 1) foreach (int sg in new[] { 1, -1 }) featherStrip(gb, bez(V(sg * 0.01, 1.34, 0), V(sg * 0.03, 1.25, 0.02), V(sg * 0.04, 1.12, 0.0), V(sg * 0.05, 0.98, -0.03), 5), 0.035, 0.03, "cloth", col("#c8302a"), K.w, null, 5);
                        info.len = 1.86; info.tip = V(0, 1.82, 0); info.@base = V(0, 1.4, 0); info.off = V(0, 0.62, 0);
                        break;
                    }
                case "bigblade": case "fengzui": case "poleblade":
                    {
                        bool big = id == "bigblade", feng = id == "fengzui";
                        double top = big ? 0.72 : 1.0;
                        shaft(K, big ? -0.55 : -0.85, top + 0.02, big ? 0.02 : 0.017, "wood", big ? "#2a2420" : "#3a2418");
                        spikeAt(K, big ? -0.55 : -0.85, 0.08, 0.018, STEEL, true);
                        ringAtY(K, top, 0.03, 0.06, "gold", feng ? GOLD : BRONZE);
                        if (feng) dragonHead(K, top + 0.06, GOLD, 0.8);
                        if (!big) shaftTassel(K, top - 0.02, 0.13, 0.045, "#c8302a");
                        double L = big ? 0.78 : 0.62, W0 = big ? 0.13 : 0.08, W1v = big ? 0.2 : 0.12;
                        double curve = feng ? 0.12 : 0.04;
                        var pts = new double[9][];
                        for (int i = 0; i <= 8; i++) { double t = i / 8.0; pts[i] = new[] { top + 0.04 + t * L, lerp(W0, W1v, Math.Sin(t * PI * 0.8)) * (t > 0.85 ? (1 - t) / 0.15 : 1), big ? 0.03 : 0.018, big ? 0.011 : 0.008, -curve * t * t }; }
                        bladeY(K, pts, false, big ? "#c8ccd2" : "#ffffff");
                        info.len = top + 0.04 + L; info.tip = V(0, top + L, -curve * 0.8); info.@base = V(0, top + 0.1, 0); info.off = V(0, big ? 0.45 : 0.55, 0);
                        break;
                    }
                case "spear": case "bawang": case "yajiao": case "hutou": case "lance": case "pilum": case "trident":
                    {
                        bool lance = id == "lance", pil = id == "pilum";
                        double top = lance ? 1.6 : pil ? 0.9 : 1.32;
                        string sc = id == "yajiao" ? "#e4e4ec" : id == "hutou" ? "#d8c8a0" : id == "bawang" ? "#4a1a1a" : SH;
                        shaft(K, lance ? -1.4 : pil ? -0.7 : -0.95, top + 0.02, lance ? 0.019 : 0.016, "wood", sc);
                        if (!pil) spikeAt(K, lance ? -1.4 : -0.95, 0.07, 0.016, id == "hutou" ? GOLD : STEEL, true);
                        string fit = id == "hutou" ? GOLD : id == "yajiao" ? SILVER : id == "bawang" ? GOLD : BRONZE;
                        if (K.hi >= 1 && !pil) foreach (double y in new[] { -0.85, 0.3 }) ringAtY(K, y, 0.019, 0.03, "goldEngr", fit);
                        ringAtY(K, top, 0.022, 0.05, "gold", fit);
                        if (id == "yajiao" || id == "hutou") dragonHead(K, top + 0.04, id == "hutou" ? GOLD : "#e8eaf0", 0.75);
                        if (!lance && !pil) shaftTassel(K, top - 0.01, id == "spear" ? 0.09 : 0.14, id == "spear" ? 0.032 : 0.045, id == "yajiao" || id == "hutou" ? "#f0f0f0" : "#c8302a");
                        double b0 = top + (id == "yajiao" || id == "hutou" ? 0.1 : 0.03);
                        if (pil)
                        {
                            tube(gb, new List<V3d> { V(0, top, 0), V(0, top + 0.45, 0) }, new FigOpt { r = 0.006, n = 4, reg = "brushed", col = col(IRON), w = K.w });
                            bladeY(K, new[] { new[] { top + 0.44, 0.012, 0.012, 0.008 }, new[] { top + 0.5, 0, 0, 0.002 } }, true);
                            info.len = top + 0.5;
                        }
                        else if (id == "trident")
                        {
                            bladeY(K, new[] { new[] { b0, 0.025, 0.025, 0.007 }, new[] { b0 + 0.15, 0.03, 0.03, 0.006 }, new[] { b0 + 0.3, 0, 0, 0.002 } }, true);
                            foreach (int sg in new[] { 1, -1 })
                            {
                                var st = new List<FigBladeSt>();
                                for (int i = 0; i <= 5; i++) { double t = i / 5.0; st.Add(new FigBladeSt { p = V(0, b0 + t * 0.2, sg * (0.03 + 0.05 * Math.Sin(t * PI * 0.6))), e = V(0, 0, sg), n = V(1, 0, 0), we = 0.015 * (1 - t) + 0.002, wb = 0.008 * (1 - t) + 0.002, th = 0.005, dbl = true }); }
                                blade(gb, st, new FigOpt { col = new FCol(1, 1, 1), w = K.w });
                            }
                            info.len = b0 + 0.3;
                        }
                        else
                        {
                            double L = lance ? 0.22 : id == "bawang" ? 0.4 : id == "spear" ? 0.26 : 0.36, Wd = lance ? 0.022 : id == "bawang" ? 0.05 : 0.034;
                            bladeY(K, new[] { new[] { b0, Wd * 0.5, Wd * 0.5, 0.007 }, new[] { b0 + L * 0.3, Wd, Wd, 0.007 }, new[] { b0 + L * 0.7, Wd * 0.7, Wd * 0.7, 0.006 }, new[] { b0 + L, 0, 0, 0.002 } }, true, id == "hutou" ? "#f0d890" : "#ffffff");
                            info.len = b0 + L;
                        }
                        info.tip = V(0, info.len - 0.04, 0); info.@base = V(0, b0, 0); info.off = V(0, lance ? 0.7 : 0.6, 0);
                        break;
                    }
                case "staff":
                    {
                        // 锡杖（僧人）：木杖、铜箍，杖头为桃形铜环套小环
                        double top = 1.25;
                        shaft(K, -0.95, top, 0.017, "wood", "#6a4a2a", 0.1);
                        ringAtY(K, -0.9, 0.019, 0.04, "goldEngr", BRONZE);
                        ringAtY(K, top - 0.02, 0.02, 0.05, "goldEngr", BRONZE);
                        var ring = ringPts(V(0, top + 0.11, 0), V(1, 0, 0), V(0, 1, 0), 0.075, 0.11, K.hi >= 1 ? 14 : 8); ring.Add(V(0.0, top + 0.0, 0));
                        tube(gb, ring, new FigOpt { r = 0.007, n = 5, reg = "gold", col = col(BRONZE), w = K.w });
                        if (K.hi >= 1) foreach (int sx in new[] { -1, 1 }) foreach (double yy in new[] { 0.06, 0.15 }) tube(gb, ringPts(V(sx * 0.075, top + yy, 0), V(0, 0, 1), V(0, 1, 0), 0.02, 0.02, 8), new FigOpt { r = 0.004, n = 4, reg = "gold", col = col(BRONZE), w = K.w });
                        info.len = top + 0.22;
                        info.tip = V(0, top + 0.2, 0); info.@base = V(0, top - 0.3, 0); info.off = V(0, 0.6, 0);
                        break;
                    }
                case "bigaxe": case "axe":
                    {
                        bool big = id == "bigaxe";
                        double top = big ? 1.05 : 0.95;
                        shaft(K, -0.6, top + 0.08, 0.019, "wood", "#2a2018");
                        spikeAt(K, -0.6, 0.06, 0.018, STEEL, true);
                        ringAtY(K, top - 0.15, 0.024, 0.04, "gold", BRONZE);
                        spikeAt(K, top + 0.08, 0.14, 0.018, STEEL);
                        // 斧刃：朝 +Z 的半月形
                        var st = new List<FigBladeSt>();
                        for (int i = 0; i <= 8; i++)
                        {
                            double a = lerp(-1.1, 1.1, i / 8.0), R = big ? 0.2 : 0.14;
                            st.Add(new FigBladeSt { p = V(0, top + Math.Sin(a) * R * 0.75, 0.03 + Math.Cos(a) * R * 0.25), e = v3norm(V(0, Math.Sin(a) * 0.5, Math.Cos(a))), n = V(1, 0, 0), we = (big ? 0.1 : 0.07) * Math.Cos(a * 0.9), wb = 0.03, th = 0.01, dbl = false });
                        }
                        blade(gb, st, new FigOpt { col = col("#d8dce2"), w = K.w });
                        info.len = top + 0.22; info.tip = V(0, top, 0.2); info.@base = V(0, top - 0.1, 0); info.off = V(0, 0.45, 0);
                        break;
                    }
                case "ji1": case "sji1":
                    {
                        bool shrt = id == "shortji";
                        double top = shrt ? 0.45 : 0.55;
                        shaft(K, -0.25, top + 0.02, 0.016, "wrap", shrt ? "#5a2a1a" : "#2a2a2e");
                        spikeAt(K, -0.25, 0.05, 0.016, STEEL, true);
                        ringAtY(K, top, 0.022, 0.04, "gold", shrt ? GOLD : IRON);
                        bladeY(K, new[] { new[] { top + 0.03, 0.022, 0.022, 0.008 }, new[] { top + 0.1, 0.03, 0.03, 0.007 }, new[] { top + (shrt ? 0.22 : 0.26), 0, 0, 0.002 } }, true, shrt ? "#ffffff" : "#b8bcc4");
                        var st = new List<FigBladeSt>();
                        for (int i = 0; i <= 6; i++) { double a = lerp(-1.2, 1.0, i / 6.0), R = shrt ? 0.085 : 0.11; st.Add(new FigBladeSt { p = V(0, top - 0.02 + Math.Sin(a) * R, 0.025 + Math.Cos(a) * R * 0.7), e = v3norm(V(0, Math.Sin(a) * 0.7, Math.Cos(a))), n = V(1, 0, 0), we = 0.04 * (1 - Math.Pow(Math.Abs(a + 0.1) / 1.1, 2)) + 0.004, wb = 0.012, th = 0.007, dbl = false }); }
                        blade(gb, st, new FigOpt { col = col(shrt ? "#ffffff" : "#b8bcc4"), w = K.w });
                        tube(gb, new List<V3d> { V(0, top - 0.04, 0), V(0, top - 0.04, -0.07) }, new FigOpt { rF = (t, i) => 0.01 * (1 - t) + 0.002, n = 4, reg = "brushed", col = col(IRON), w = K.w });
                        if (shrt) shaftTassel(K, top - 0.01, 0.09, 0.03, "#c8302a");
                        info.len = top + 0.26; info.tip = V(0, top + 0.22, 0); info.@base = V(0, top, 0);
                        break;
                    }
                case "jian": case "yitian": case "gladius": case "spatha": case "dao": case "guding": case "tachi":
                    {
                        bool curved = id == "dao" || id == "guding", glad = id == "gladius", ring = id == "dao" || id == "guding" || id == "tachi";
                        double L = glad ? 0.5 : id == "spatha" ? 0.78 : id == "yitian" ? 0.86 : 0.76;
                        string gripC = glad ? "#d8c8a0" : id == "yitian" ? "#5a1a1a" : "#2a2018";
                        tube(gb, K.hi == 0 ? new List<V3d> { V(0, -0.11, 0), V(0, 0.07, 0) } : new List<V3d> { V(0, -0.11, 0), V(0, -0.02, 0), V(0, 0.07, 0) }, new FigOpt { r = 0.015, n = K.hi == 0 ? 4 : Math.Max(5, K.lod.tube), reg = glad ? "wood" : "wrap", col = col(gripC), w = K.w });
                        string fit = id == "yitian" || id == "guding" ? GOLD : glad || id == "spatha" ? BRONZE : "#b8a070";
                        if (ring && K.hi >= 1) tube(gb, ringPts(V(0, -0.15, 0), V(0, 0, 1), V(0, 1, 0), 0.03, 0.03, 10), new FigOpt { r = 0.006, n = 4, reg = "gold", col = col(fit), w = K.w });
                        else if (K.hi >= 1) ellipsoid(gb, V(0, -0.125, 0), V(0.024, 0.02, 0.024), new FigOpt { nl = 4, n = 6, reg = "gold", col = col(fit), w = K.w });
                        // 护手
                        if (K.hi == 0) ellipsoid(gb, V(0, 0.075, 0), V(0.022, 0.012, 0.045), new FigOpt { nl = 2, n = 4, reg = "gold", col = col(fit), w = K.w });
                        else if (glad || id == "spatha") ellipsoid(gb, V(0, 0.075, 0), V(0.025, 0.014, 0.04), new FigOpt { nl = 4, n = 6, reg = "wood", col = col(gripC), w = K.w });
                        else if (ring) lathe(gb, new[] { P2(0.0, 0.065), P2(0.035, 0.066), P2(0.036, 0.075), P2(0.0, 0.077) }, new FigOpt { n = 8, reg = "gold", col = col(fit), w = K.w, sz = 1.3 });
                        else
                        {
                            ellipsoid(gb, V(0, 0.075, 0), V(0.024, 0.018, 0.06), new FigOpt { nl = 4, n = 6, reg = "gold", col = col(fit), w = K.w });
                            if (K.hi >= 1) foreach (int sg in new[] { 1, -1 }) tube(gb, bez(V(0, 0.075, sg * 0.05), V(0, 0.065, sg * 0.07), V(0, 0.085, sg * 0.08), V(0, 0.1, sg * 0.075), 3), new FigOpt { r = 0.006, n = 4, reg = "gold", col = col(fit), w = K.w });
                        }
                        double w0 = glad ? 0.03 : curved ? 0.022 : 0.021;
                        int N = K.hi >= 1 ? 6 : 2;
                        var pts = new double[N + 1][];
                        for (int i = 0; i <= N; i++)
                        {
                            double t = (double)i / N;
                            double wt = glad ? (t < 0.75 ? w0 * (1 - 0.15 * t) : w0 * (1 - t) / 0.25 * 0.85) : (t < 0.88 ? w0 * (1 - 0.15 * t) : w0 * 0.87 * (1 - t) / 0.12);
                            pts[i] = new[] { 0.08 + t * L, curved ? wt * 1.4 : wt, curved ? wt * 0.35 : wt, curved ? 0.007 : 0.006, curved ? -0.04 * t * t : 0 };
                        }
                        bladeY(K, pts, !curved && id != "tachi");
                        if ((id == "jian" || id == "yitian") && K.hi >= 1) shaftTassel(K, -0.14, 0.12, 0.022, "#c8302a");
                        info.len = 0.08 + L; info.tip = V(0, 0.06 + L, curved ? -0.03 : 0); info.@base = V(0, 0.12, 0);
                        break;
                    }
                case "khanda":
                    {
                        // 天竺坎达剑：直身双刃、向尖端渐宽、钝圆头；圆盘护手、护指弯条、盘形柄头
                        double L = 0.84;
                        tube(gb, K.hi == 0 ? new List<V3d> { V(0, -0.1, 0), V(0, 0.06, 0) } : new List<V3d> { V(0, -0.1, 0), V(0, -0.02, 0), V(0, 0.06, 0) }, new FigOpt { r = 0.014, n = K.hi == 0 ? 4 : Math.Max(5, K.lod.tube), reg = "wrap", col = col("#3a2418"), w = K.w });
                        lathe(gb, new[] { P2(0.0, -0.124), P2(0.034, -0.12), P2(0.036, -0.11), P2(0.012, -0.1) }, new FigOpt { n = K.hi == 0 ? 5 : 8, reg = "gold", col = col(GOLD), w = K.w });
                        lathe(gb, new[] { P2(0.0, 0.06), P2(0.032, 0.062), P2(0.034, 0.072), P2(0.0, 0.076) }, new FigOpt { n = K.hi == 0 ? 5 : 8, reg = "gold", col = col(GOLD), w = K.w, sz = 1.4 });
                        if (K.hi >= 1)
                        {
                            tube(gb, new List<V3d> { V(0, -0.126, 0), V(0, -0.165, 0) }, new FigOpt { rF = (q, i) => 0.006 * (1 - q) + 0.0012, n = 4, reg = "gold", col = col(GOLD), w = K.w });
                            tube(gb, bez(V(0, 0.068, 0.04), V(0, 0.04, 0.075), V(0, -0.07, 0.075), V(0, -0.112, 0.032), 5), new FigOpt { r = 0.0048, n = 4, reg = "gold", col = col(GOLD), w = K.w });
                        }
                        int N = K.hi >= 1 ? 8 : 3;
                        var pts = new double[N + 1][];
                        for (int i = 0; i <= N; i++)
                        {
                            double t = (double)i / N, wt = t < 0.86 ? 0.019 + 0.012 * t : 0.0293 * Math.Sqrt(Math.Max(0, 1 - Math.Pow((t - 0.86) / 0.14, 2)));
                            pts[i] = new[] { 0.08 + t * L, Math.Max(0.003, wt), Math.Max(0.003, wt), 0.0055, 0 };
                        }
                        bladeY(K, pts, true);
                        info.len = 0.08 + L; info.tip = V(0, 0.08 + L, 0); info.@base = V(0, 0.12, 0);
                        break;
                    }
                case "mace":
                    {
                        shaft(K, -0.14, 0.5, 0.017, "wrap", "#3a2418");
                        lathe(gb, new[] { P2(0.018, 0.48), P2(0.036, 0.51), P2(0.05, 0.56), P2(0.052, 0.62), P2(0.04, 0.67), P2(0.015, 0.7), P2(0, 0.71) }, new FigOpt { n = 16, reg = "brushed", col = col("#8a8e98"), w = K.w, off = (th, s, k) => 0.016 * Math.Pow(Math.Max(0, Math.Cos(th * 3)), 4) });
                        ringAtY(K, 0.49, 0.022, 0.03, "gold", BRONZE);
                        info.len = 0.7; info.tip = V(0, 0.62, 0); info.@base = V(0, 0.5, 0);
                        break;
                    }
                case "handaxe":
                    {
                        shaft(K, -0.12, 0.5, 0.016, "wood", "#5a3a20");
                        var st = new List<FigBladeSt>();
                        for (int i = 0; i <= 5; i++) { double t = i / 5.0; st.Add(new FigBladeSt { p = V(0, 0.38 + t * 0.12, 0.02 + 0.02 * t), e = v3norm(V(0, -0.5 + t * 0.8, 1)), n = V(1, 0, 0), we = 0.04 + 0.06 * t * t, wb = 0.02, th = 0.008, dbl = false }); }
                        blade(gb, st, new FigOpt { col = col("#d0d4d8"), w = K.w });
                        info.len = 0.52; info.tip = V(0, 0.45, 0.1); info.@base = V(0, 0.42, 0);
                        break;
                    }
                case "fan":
                    {
                        tube(gb, new List<V3d> { V(0, -0.08, 0), V(0, 0.08, 0) }, new FigOpt { r = 0.012, n = 6, reg = "gold", col = col("#c8a050"), w = K.w });
                        ellipsoid(gb, V(0, 0.1, 0), V(0.02, 0.02, 0.02), new FigOpt { nl = 4, n = 6, reg = "gold", col = col(GOLD), w = K.w });
                        // 羽面：扁平椭圆（白羽）
                        panel(gb, (s, t) => { double a = (s - 0.5) * PI * 0.62, r = 0.03 + t * 0.27; return V(Math.Sin(a) * r * 0.05 + 0.01 * Math.Sin(s * PI * 9) * t, 0.1 + Math.Cos(a) * r, Math.Sin(a) * r); }, new FigOpt { nu = K.hi >= 1 ? 9 : 3, nv = K.hi >= 1 ? 4 : 2, th = 0.008, reg = "tassel", col = col("#f4f2ee"), colB = col("#e8e4dc"), w = K.w, uv = (s, t) => P2(s, t) });
                        if (K.hi >= 1) for (int i = 0; i < 7; i++) { double a = (i / 6.0 - 0.5) * PI * 0.6; tube(gb, new List<V3d> { V(0, 0.1, 0), V(Math.Sin(a) * 0.005, 0.1 + Math.Cos(a) * 0.27, Math.Sin(a) * 0.27) }, new FigOpt { r = 0.0025, n = 3, reg = "gold", col = col("#e8d8a8"), w = K.w }); }
                        info.len = 0.4; info.tip = V(0, 0.38, 0); info.@base = V(0, 0.12, 0);
                        break;
                    }
                case "bow": case "huangbow": case "longbow":
                    {
                        // 反曲弓：沿 ±Y 两臂，弓背朝 +Z，弦在 −Z 一侧；longbow 更长、单弯、竹色
                        bool gold = id == "huangbow", lb = id == "longbow";
                        double half = gold ? 0.62 : lb ? 0.74 : 0.55; var pts = new List<V3d>();
                        int N = K.hi >= 1 ? 12 : 6;
                        for (int i = 0; i <= N; i++) { double t = (double)i / N * 2 - 1, a = Math.Abs(t); pts.Add(V(0, t * half, lb ? 0.07 * (1 - a * a) : 0.06 * (1 - a * a) - 0.05 * Math.Pow(a, 6) * 1.2 + (a > 0.85 ? -0.02 * (a - 0.85) / 0.15 : 0))); }
                        tube(gb, pts, new FigOpt { rF = (s, i) => (0.013 - 0.007 * Math.Abs(2 * s - 1)) * (gold ? 1.1 : 1), rz = 0.8, n = Math.Max(5, K.lod.tube), reg = gold ? "gold" : "wood", col = col(gold ? "#c89a48" : lb ? "#a8884a" : "#5a3418"), w = K.w });
                        tube(gb, new List<V3d> { V(0, -0.06, 0.05), V(0, 0.06, 0.05) }, new FigOpt { r = 0.017, n = 6, reg = "wrap", col = col("#3a2418"), w = K.w });
                        // 弦（细管）
                        tube(gb, new List<V3d> { pts[0], V(0, 0, pts[0].z - 0.005), pts[N] }, new FigOpt { r = 0.0018, n = 3, reg = "white", col = col("#e8e0d0"), w = K.w });
                        info.len = half; info.tip = V(0, half, 0); info.@base = V(0, 0, 0); info.str = V(0, 0, pts[0].z); info.nockTop = pts[N]; info.nockBot = pts[0];
                        break;
                    }
                default: return buildWeaponInto(gb, "jian", lod, w);
            }
            return info;
        }

        // 盾：局部 +Z 为盾面朝外，+Y 沿前臂（朝手）方向；原点在前臂处
        public static FigShieldInfo buildShieldInto(FigGB gb, string id, FigLod lod, FCol? colr, FBW[] w)
        {
            var K = new WK { gb = gb, lod = lod, w = w ?? W1(FigBones.foreArmL), hi = lod.detail };
            FCol fc = colr ?? col("#8a2a24");
            if (id == "scutum")
            {
                double W0 = 0.62, H0 = 1.02, Rc = 0.5;
                panel(gb, (s, t) => { double a = (s - 0.5) * W0 / Rc; return V(Math.Sin(a) * Rc, (t - 0.5) * H0 + 0.02, Math.Cos(a) * Rc - Rc + 0.07); }, new FigOpt { nu = K.hi >= 1 ? 6 : 3, nv = K.hi >= 1 ? 4 : 2, th = 0.02, reg = "scutum", col = fc, colB = col("#6a4a30"), w = K.w, uv = (s, t) => P2(s, t) });
                var c = V(0, 0.02, 0.09); var AX = axesFromY(V(0, 0, 1)); double r = 0.07;
                lathe(gb, new[] { new[] { r, -0.004, double.NaN, 0 }, new[] { r * 0.9, 0.01, double.NaN, 0.3 }, new[] { r * 0.5, 0.03, double.NaN, 0.7 }, new[] { 0, 0.036, double.NaN, 1 } }, new FigOpt { c = c, axes = AX, n = Math.Max(6, lod.ring - 4), reg = "brushed", col = col("#c8ccd2"), w = K.w, capStart = true });
                return new FigShieldInfo { r = 0.5 };
            }
            if (id == "wa" || id == "pai")
            {
                double W0 = id == "wa" ? 0.4 : 0.46, H0 = id == "wa" ? 0.92 : 0.76;
                panel(gb, (s, t) => V((s - 0.5) * W0, (t - 0.5) * H0, 0.06 - 0.06 * Math.Pow(2 * s - 1, 2)), new FigOpt { nu = 3, nv = 2, th = 0.02, reg = id == "wa" ? "brocade" : "wood", col = id == "wa" ? col("#7a2a1a") : fc, colB = col("#5a3a24"), w = K.w, uv = (s, t) => P2(s, t) });
                var c = V(0, 0.02, 0.085); var AX = axesFromY(V(0, 0, 1)); double r = id == "wa" ? 0.08 : 0.11; int start = gb.nv;
                lathe(gb, new[] { new[] { r, -0.006, double.NaN, 0 }, new[] { r, 0.0, double.NaN, 0.1 }, new[] { r * 0.85, 0.012, double.NaN, 0.35 }, new[] { r * 0.3, 0.02, double.NaN, 0.8 }, new[] { 0, 0.022, double.NaN, 1 } }, new FigOpt { c = c, axes = AX, n = Math.Max(6, lod.ring - 4), reg = id == "wa" ? "mirror" : "buckle", col = col(GOLD), w = K.w });
                fixDiscUV(gb, FigAtlasLayout.REG(id == "wa" ? "mirror" : "buckle"), c, AX, r, start);
                return new FigShieldInfo { r = H0 / 2 };
            }
            if (id == "long")
            {
                // 天竺长盾（桑奇浮雕）——窄长、顶边圆拱、竖向弯曲的木盾，正中金泡钉
                double W0 = 0.4, H0 = 0.98;
                panel(gb, (s, t) => { double e = 1 - Math.Pow(2 * s - 1, 2); return V((s - 0.5) * W0, (t - 0.5) * H0 + Math.Pow(t, 4) * 0.11 * e, 0.07 * e); },
                    new FigOpt { nu = K.hi >= 1 ? 4 : 2, nv = K.hi >= 1 ? 4 : 2, th = 0.02, reg = "wood", col = fc, colB = col("#5a3a24"), w = K.w, uv = (s, t) => P2(s, t) });
                var c = V(0, 0.04, 0.09); var AX = axesFromY(V(0, 0, 1)); double r = 0.06; int start = gb.nv;
                lathe(gb, new[] { new[] { r, -0.006, double.NaN, 0 }, new[] { r, 0.0, double.NaN, 0.1 }, new[] { r * 0.85, 0.012, double.NaN, 0.35 }, new[] { r * 0.3, 0.02, double.NaN, 0.8 }, new[] { 0, 0.022, double.NaN, 1 } }, new FigOpt { c = c, axes = AX, n = Math.Max(6, lod.ring - 4), reg = "buckle", col = col(GOLD), w = K.w });
                fixDiscUV(gb, FigAtlasLayout.REG("buckle"), c, AX, r, start);
                return new FigShieldInfo { r = H0 / 2 };
            }
            // 圆盾 / 椭圆盾 / 藤牌；hide（阿克苏姆 / 库施的长皮盾，椭圆、皮色）
            bool oval = id == "oval" || id == "hide", rat = id == "rattan";
            double R0 = oval ? 0.26 : rat ? 0.33 : 0.32;
            var cc = V(0, 0, 0.07); var AXr = axesFromY(V(0, 0, 1)); int st0 = gb.nv;
            string reg = rat ? "rattan" : id == "hide" ? "cloth" : "shieldRound";
            lathe(gb, new[] { new[] { R0, -0.02, double.NaN, 0 }, new[] { R0 * 1.02, 0.0, double.NaN, 0.04 }, new[] { R0 * 0.95, 0.012, double.NaN, 0.1 }, new[] { R0 * 0.5, 0.03, double.NaN, 0.5 }, new[] { 0, 0.04, double.NaN, 1 } },
                new FigOpt { c = cc, axes = AXr, n = Math.Max(8, lod.ring), reg = reg, col = rat ? col("#c8a060") : id == "hide" ? col("#a07a4e") : fc, w = K.w, sx = 1, sz = id == "hide" ? 2.1 : oval ? 1.8 : 1, capStart = true });
            fixDiscUV(gb, FigAtlasLayout.REG(reg), cc, AXr, R0, st0);
            if (id == "hide" && K.hi >= 1)
            {
                // 皮盾：纵向一道木脊 + 正中皮钮
                tube(gb, new List<V3d> { V(0, -R0 * 1.9, 0.105), V(0, 0, 0.118), V(0, R0 * 1.9, 0.105) }, new FigOpt { r = 0.012, rz = 0.6, n = 4, reg = "wood", col = col("#6a4a2a"), w = K.w });
                ellipsoid(gb, V(0, 0, 0.12), V(0.04, 0.05, 0.02), new FigOpt { nl = 4, n = 6, reg = "leather", col = col("#5a3a20"), w = K.w });
            }
            return new FigShieldInfo { r = oval ? R0 * 1.8 : R0 };
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    // 战马选项（= JS horse(opts)）
    public sealed class FigHorseOpts
    {
        public string color;        // 马衣 / 马铠的势力色
        public string coat, mane;   // 毛色 '#hex'（缺省按 seed 选）
        public string lod = "mid";
        public string armor;        // 'cloth' | 'scale' | 'none'（缺省 cloth）
        public string culture, name, metal, plume;
        public int seed;
        public bool general, chamfron, shadow = true;
        public FigHorseOpts Clone() { return (FigHorseOpts)MemberwiseClone(); }
        public string Key() { return string.Join("|", new[] { lod, coat ?? "", armor ?? "", seed.ToString(), name ?? "", general ? "true" : "false", metal ?? "", plume ?? "" }); }
    }

    public static partial class FigBuild
    {
        // ======================================================== 战马 ==
        static int HBi(string n) { return FigBones.HB(n); }
        public static V3d[] horseJoints(double k)
        {
            var J = new V3d[FigBones.HBONES.Length];
            Action<string, V3d> S = (n, v) => J[HBi(n)] = v;
            S("root", V(0, 0, 0)); S("body", V(0, 1.15, -0.1)); S("chest", V(0, 1.18, 0.38)); S("neck1", V(0, 1.36, 0.5)); S("neck2", V(0, 1.62, 0.74)); S("head", V(0, 1.86, 0.92));
            S("tail1", V(0, 1.3, -0.8)); S("tail2", V(0, 0.95, -0.9)); S("saddle", V(0, 1.47, -0.02));
            foreach (int s in new[] { 1, -1 })
            {
                string Sd = s > 0 ? "L" : "R";
                S("upF" + Sd, V(0.15 * s, 1.02, 0.42)); S("loF" + Sd, V(0.15 * s, 0.55, 0.45)); S("ftF" + Sd, V(0.15 * s, 0.2, 0.46));
                S("upH" + Sd, V(0.17 * s, 0.98, -0.5)); S("loH" + Sd, V(0.17 * s, 0.58, -0.7)); S("ftH" + Sd, V(0.16 * s, 0.2, -0.64));
            }
            for (int i = 0; i < J.Length; i++) J[i] = v3mul(J[i], k);
            return J;
        }
        static double[] ringAtHorse(double[][] S, double z)
        {
            for (int i = 0; i < S.Length - 1; i++) { var a = S[i]; var b = S[i + 1]; if (z <= b[0]) { double d = b[0] - a[0]; double t = (z - a[0]) / (d != 0 ? d : 1); return new[] { lerp(a[1], b[1], t), lerp(a[2], b[2], t), lerp(a[3], b[3], t) }; } }
            var l = S[S.Length - 1]; return new[] { l[1], l[2], l[3] };
        }
        public sealed class HorseGeo { public FigGB gb; public V3d[] J; public double k; }
        public static HorseGeo buildHorseGeometry(FigHorseOpts opts)
        {
            var lod = FigLod.Of(opts.lod ?? "mid");
            if (!FigTables.LODP.Has(opts.lod ?? "mid")) lod = FigLod.Of("mid");
            double k = 1.0;
            var J = horseJoints(k);
            var gb = new FigGB();
            if (lod.detail == 0) gb.noEdges = true;
            var named = FigTables.HORSE_NAMED.L(opts.name ?? "");
            List<object> coatPair;
            if (!string.IsNullOrEmpty(opts.coat)) coatPair = new List<object> { opts.coat, opts.mane ?? hex(shade(opts.coat, -0.6)) };
            else coatPair = named ?? (List<object>)FigTables.HORSE_COATS[opts.seed % FigTables.HORSE_COATS.Count];
            FCol coat = col((string)coatPair[0]), mane = !string.IsNullOrEmpty(opts.coat) && opts.mane == null ? shade(opts.coat, -0.6) : col((string)coatPair[1]);
            FCol fc = TEAM;
            string armor = opts.armor ?? "cloth";
            int hi = lod.detail;
            int N = lod.detail >= 2 ? 18 : lod.detail >= 1 ? 10 : 7;
            int Hbody = HBi("body"), Hchest = HBi("chest"), Hneck1 = HBi("neck1"), Hneck2 = HBi("neck2"), Hhead = HBi("head"), Htail1 = HBi("tail1"), Htail2 = HBi("tail2");
            // 躯干
            var bodyS = new[] { new[] { -0.88, 0, 0, 1.22 }, new[] { -0.84, 0.15, 0.17, 1.22 }, new[] { -0.74, 0.27, 0.3, 1.18 }, new[] { -0.56, 0.32, 0.36, 1.15 }, new[] { -0.3, 0.31, 0.35, 1.11 }, new[] { 0.0, 0.315, 0.36, 1.08 }, new[] { 0.25, 0.305, 0.37, 1.1 }, new[] { 0.45, 0.285, 0.375, 1.14 }, new[] { 0.58, 0.23, 0.32, 1.18 }, new[] { 0.66, 0.13, 0.18, 1.24 }, new[] { 0.7, 0, 0, 1.26 } };
            Func<V3d, FBW[]> bw = p => p.z > 0.3 ? W1(Hchest) : p.z > 0.1 ? new[] { new FBW(Hchest, (p.z - 0.1) / 0.2), new FBW(Hbody, 1 - (p.z - 0.1) / 0.2) } : W1(Hbody);
            Func<V3d, FBW[]> tailW = p => p.z < -0.78 ? new[] { new FBW(Hbody, 0.6), new FBW(Htail1, 0.4) } : null;
            {
                var secs = new List<FigSec>();
                for (int i = 0; i < bodyS.Length; i++) { var b = bodyS[i]; secs.Add(new FigSec { c = V(0, b[3] * k, b[0] * k), X = V(-1, 0, 0), Z = V(0, 1, 0), rx = b[1] * k, rz = b[2] * k, t = (double)i / (bodyS.Length - 1), pole = b[1] == 0, sq = 2.2 }); }
                loft(gb, secs, new FigOpt
                {
                    n = N + 2, reg = "horse", col = coat, wf = (p, th, kk) => tailW(p) ?? bw(p),
                    off = (th, s, kk) => { double z = s.c.z; return (0.02 * gauss(z, -0.5, 0.15) * gauss(Math.Abs(th), 0.9, 0.5) + 0.018 * gauss(z, 0.42, 0.12) * gauss(Math.Abs(th), 1.2, 0.5) - 0.015 * gauss(z, 0.0, 0.3) * gauss(Math.Abs(th), PI, 0.6)) * k; },
                });
            }
            // 颈
            var nk = new List<V3d> { V(0, 1.2, 0.38), V(0, 1.4, 0.56), V(0, 1.6, 0.72), V(0, 1.78, 0.86), V(0, 1.88, 0.94) };
            var nr = new[] { P2(0.17, 0.34), P2(0.135, 0.32), P2(0.11, 0.25), P2(0.09, 0.19), P2(0.082, 0.15) };
            var nF = frames(nk, V(0, 1, 0));
            Func<V3d, FBW[]> neckW = p => { double t = clamp01((p.y - 1.3) / 0.55); return wT(t, Hchest, 0, Hneck1, 0.25, Hneck2, 0.6, Hhead, 1); };
            {
                var secs = new List<FigSec>();
                for (int i = 0; i < nk.Count; i++)
                {
                    var d = nF.T[i];
                    var Zs = v3norm(v3sub(V(0, 1, -0.6), v3mul(d, v3dot(V(0, 1, -0.6), d))));
                    secs.Add(new FigSec { c = v3mul(nk[i], k), X = v3cross(d, Zs), Z = Zs, rx = nr[i][0] * k, rz = nr[i][1] * 0.55 * k, t = i / 4.0 });
                }
                loft(gb, secs, new FigOpt { n = N, reg = "horse", col = coat, wf = (p, th, kk) => neckW(p) });
            }
            // 头：自后脑向前下方
            var hd = new List<V3d> { V(0, 1.9, 0.9), V(0, 1.88, 0.98), V(0, 1.8, 1.1), V(0, 1.68, 1.24), V(0, 1.58, 1.33), V(0, 1.53, 1.37) };
            var hr = new[] { P2(0.08, 0.1), P2(0.1, 0.13), P2(0.092, 0.115), P2(0.072, 0.09), P2(0.068, 0.082), P2(0.035, 0.045) };
            var hF = frames(hd, V(0, 1, 0));
            {
                var secs = new List<FigSec>();
                for (int i = 0; i < hd.Count; i++)
                {
                    var d = hF.T[i]; var Zs = v3norm(v3sub(V(0, 1, 0.3), v3mul(d, v3dot(V(0, 1, 0.3), d))));
                    secs.Add(new FigSec { c = v3mul(hd[i], k), X = v3cross(d, Zs), Z = Zs, rx = hr[i][0] * k, rz = hr[i][1] * k, t = i / 5.0, pole = false });
                }
                loft(gb, secs, new FigOpt { n = N, reg = "horse", col = coat, w = W1(Hhead), capEnd = true, capStart = true });
            }
            // 耳、眼、鼻孔
            foreach (int s in new[] { 1, -1 })
            {
                tube(gb, new List<V3d> { V(s * 0.05 * k, 1.95 * k, 0.93 * k), V(s * 0.065 * k, 2.03 * k, 0.9 * k), V(s * 0.07 * k, 2.09 * k, 0.88 * k) }, new FigOpt { rF = (t, i) => (0.022 * (1 - t) + 0.002) * k, rz = 0.55, n = 5, reg = "horse", col = coat, w = W1(Hhead) });
                ellipsoid(gb, V(s * 0.088 * k, 1.84 * k, 1.04 * k), V(0.012 * k, 0.016 * k, 0.02 * k), new FigOpt { nl = 4, n = 6, reg = "white", col = col("#140e0c"), w = W1(Hhead) });
                ellipsoid(gb, V(s * 0.04 * k, 1.56 * k, 1.36 * k), V(0.012 * k, 0.014 * k, 0.01 * k), new FigOpt { nl = 3, n = 5, reg = "white", col = col("#1a1210"), w = W1(Hhead) });
            }
            // 鬃
            panel(gb, (s, t) => { var p = v3lerp(nk[1], V(0, 1.95, 0.9), t); return V(lerp(-0.012, 0.03, s) * k + 0.02 * k * t, (p.y + 0.13 - 0.06 * t + 0.0 * s) * k - s * 0.1 * k, (p.z - 0.06 * (1 - t) - 0.02) * k); },
                new FigOpt { nu = 1, nv = hi >= 1 ? 6 : 3, th = 0.03 * k, reg = "hair", col = mane, wP = (p, s, t) => neckW(p), uv = (s, t) => P2(s, t) });
            // 腿
            foreach (var fs in new[] { new[] { "F", "L" }, new[] { "F", "R" }, new[] { "H", "L" }, new[] { "H", "R" } })
            {
                string fh = fs[0], S = fs[1];
                V3d up = J[HBi("up" + fh + S)], lo = J[HBi("lo" + fh + S)], ft = J[HBi("ft" + fh + S)];
                var hoof = V(ft.x, 0.0, ft.z + 0.04 * k);
                var top = fh == "F" ? V(up.x * 0.9, 1.22 * k, up.z - 0.04 * k) : V(up.x * 0.9, 1.22 * k, up.z + 0.05 * k);
                var pts = new List<V3d> { top, up, v3lerp(up, lo, 0.35), v3lerp(up, lo, 0.75), lo, v3lerp(lo, ft, 0.5), ft, V(ft.x, 0.11 * k, ft.z + 0.025 * k) };
                var rr = fh == "F" ? new[] { 0.15, 0.105, 0.088, 0.066, 0.058, 0.043, 0.054, 0.046 } : new[] { 0.17, 0.14, 0.105, 0.075, 0.064, 0.045, 0.055, 0.046 };
                if (hi == 0) { pts = new List<V3d> { pts[0], pts[1], pts[4], pts[6], pts[7] }; rr = new[] { rr[0], rr[1], rr[4], rr[6], rr[7] }; }
                var chain = fh == "F" ? new[] { new FigChain(Hchest, top), new FigChain(HBi("upF" + S), up), new FigChain(HBi("loF" + S), lo), new FigChain(HBi("ftF" + S), ft), new FigChain(-1, hoof) }
                    : new[] { new FigChain(Hbody, top), new FigChain(HBi("upH" + S), up), new FigChain(HBi("loH" + S), lo), new FigChain(HBi("ftH" + S), ft), new FigChain(-1, hoof) };
                // JS 的 col 为立即调用的 (s => s > 0.78 && … ? 白 : coat)(0)：s = 0，恒为毛色
                FCol legC = coat;
                var rra = rr;
                tube(gb, pts, new FigOpt { rF = (s, i) => rra[i] * k, rz = 1.1, n = hi == 0 ? 5 : Math.Max(6, N - 4), reg = "horse", col = legC, wf = (p, th, kk) => chainW(p, chain, 0.05), up = V(0, 0, 1) });
                // 蹄
                lathe(gb, hi == 0 ? new[] { P2(0.06 * k, 0.0), P2(0.046 * k, 0.105 * k) } : new[] { P2(0.056 * k, 0.0), P2(0.062 * k, 0.006 * k), P2(0.054 * k, 0.075 * k), P2(0.044 * k, 0.105 * k) },
                    new FigOpt { c = V(hoof.x, 0, hoof.z - 0.01 * k), n = hi == 0 ? 5 : Math.Max(6, N - 4), reg = "leather", col = col("#262220"), w = W1(HBi("ft" + fh + S)), capStart = true });
            }
            // 尾
            var tp = new List<V3d> { v3mul(V(0, 1.3, -0.82), k), v3mul(V(0, 1.22, -0.92), k), v3mul(V(0, 1.0, -0.98), k), v3mul(V(0, 0.78, -0.98), k), v3mul(V(0, 0.6, -0.96), k) };
            tube(gb, tp, new FigOpt { rF = (s, i) => (0.045 + 0.05 * Math.Sin(s * PI * 0.9)) * k, rz = 0.55, n = Math.Max(5, N - 6), reg = "hair", col = mane, wT = (s, pt) => wT(s, Hbody, 0, Htail1, 0.15, Htail2, 0.6), capEnd = true });
            // ---- 马具 ----
            // 鞍
            panel(gb, (s, t) => { double a = lerp(-1.25, 1.25, s); double z = lerp(-0.22, 0.2, t) * k; double y = (1.43 + 0.05 * (Math.Pow(2 * t - 1, 2))) * k; return V(Math.Sin(a) * 0.3 * k, y - (1 - Math.Cos(a)) * 0.16 * k, z); },
                new FigOpt { nu = hi >= 1 ? 6 : 3, nv = hi >= 1 ? 4 : 2, th = 0.03, reg = "leather", col = col("#4a2a1a"), w = W1(Hbody), mirrorS = true });
            if (hi >= 1) foreach (double zz in new[] { -0.22, 0.2 })
            {
                var pts = new List<V3d>();
                for (int i = 0; i <= 8; i++) { double a = lerp(-1.3, 1.3, i / 8.0); pts.Add(V(Math.Sin(a) * 0.31 * k, (1.47 + (zz > 0 ? 0.05 : 0.04) - (1 - Math.Cos(a)) * 0.16) * k, zz * k)); }
                tube(gb, pts, new FigOpt { r = 0.014 * k, n = 5, reg = "gold", col = col(GOLD), w = W1(Hbody) });
            }
            // 马衣（鞍下垂到腹下的布）或具装鳞甲
            foreach (int s in new[] { 1, -1 })
            {
                if (armor == "none") break;
                bool scale = armor == "scale";
                panel(gb, (u, t) =>
                {
                    double z = lerp(scale ? -0.75 : -0.45, scale ? 0.6 : 0.35, u) * k;
                    var ring = ringAtHorse(bodyS, z / k);
                    double a = lerp(0.35, 1.75, t);
                    double yb = ring[2] + Math.Cos(a) * ring[1] * 1.06, xb = Math.Sin(a) * ring[0] * 1.08 + 0.012;
                    return V(s * xb * k, yb * k - (t > 0.9 ? (t - 0.9) * 0.3 : 0), z);
                }, new FigOpt { nu = hi >= 1 ? 8 : 3, nv = hi >= 1 ? 4 : 2, th = 0.008, reg = scale ? "scale" : "hem", col = scale ? col(opts.metal ?? "#8a909a") : fc, colB = shade(TEAM, -0.5), uv = (u, t) => P2(u, 1 - t), wP = (p, ss, t) => bw(p) });
            }
            // 胸带
            if (hi >= 1)
            {
                var pts = new List<V3d>();
                for (int i = 0; i <= 10; i++) { double a = lerp(-1.6, 1.6, i / 10.0); pts.Add(V(Math.Sin(a) * 0.26 * k, (1.15 + Math.Cos(a) * 0.08 - 0.05) * k, (0.5 + Math.Cos(a) * 0.14) * k)); }
                tube(gb, pts, new FigOpt { r = 0.018 * k, rz = 0.4, n = 4, reg = "leather", col = col("#5a1a14"), w = W1(Hchest) });
            }
            if (hi >= 1) foreach (double zz in new[] { 0.62 }) ellipsoid(gb, V(0, 1.2 * k, zz * k), V(0.035 * k, 0.035 * k, 0.015 * k), new FigOpt { nl = 4, n = 6, reg = "gold", col = col(GOLD), w = W1(Hchest) });
            // 络头与缰绳
            if (hi >= 1)
            {
                tube(gb, ringPts(V(0, 1.62 * k, 1.27 * k), V(1, 0, 0), v3norm(V(0, 0.6, -0.8)), 0.072 * k, 0.09 * k, 10), new FigOpt { r = 0.008 * k, n = 3, reg = "leather", col = col("#3a1a14"), w = W1(Hhead) });
                tube(gb, ringPts(V(0, 1.88 * k, 0.95 * k), V(1, 0, 0), v3norm(V(0, 0.2, 1)), 0.1 * k, 0.11 * k, 10), new FigOpt { r = 0.008 * k, n = 3, reg = "leather", col = col("#3a1a14"), w = W1(Hhead) });
                foreach (int s in new[] { 1, -1 }) tube(gb, new List<V3d> { V(s * 0.065 * k, 1.56 * k, 1.28 * k), V(s * 0.15 * k, 1.42 * k, 0.75 * k), V(s * 0.16 * k, 1.5 * k, 0.28 * k) }, new FigOpt { r = 0.006 * k, n = 3, reg = "leather", col = col("#3a1a14"), wT = (s2, pt) => wT(s2, Hhead, 0, Hneck2, 0.4, Hchest, 1) });
            }
            // 马面（护额甲）：面帘贴着马脸，青铜色、錾花
            if (opts.chamfron || armor == "scale" || opts.general)
            {
                panel(gb, (s, t) => { var d = v3lerp(hd[1], hd[4], t); double th = lerp(-1.0, 1.0, s); double R = lerp(0.1, 0.065, t); return V(Math.Sin(th) * R * 0.95 * k, (d.y + Math.Cos(th) * R * 0.62) * k + 0.012, (d.z + Math.Cos(th) * R * 0.5) * k + 0.004); },
                    new FigOpt { nu = hi >= 1 ? 4 : 2, nv = hi >= 1 ? 4 : 2, th = 0.005, reg = "goldEngr", col = !string.IsNullOrEmpty(opts.metal) ? shade(opts.metal, -0.25) : col("#8a6e44"), w = W1(Hhead), mirrorS = true });
                if (hi >= 1) tassel(gb, lod, hi, V(0, 1.98 * k, 0.94 * k), 0.7, col(opts.plume ?? "#c8302a"), W1(Hhead));
            }
            // 镫
            if (hi >= 1) foreach (int s in new[] { 1, -1 })
            {
                tube(gb, new List<V3d> { V(s * 0.3 * k, 1.36 * k, 0.0), V(s * 0.33 * k, 1.0 * k, 0.02 * k) }, new FigOpt { r = 0.006 * k, n = 3, reg = "leather", col = col("#3a1a14"), w = W1(Hbody) });
                tube(gb, ringPts(V(s * 0.33 * k, 0.96 * k, 0.03 * k), V(0, 0, 1), V(0, 1, 0), 0.04 * k, 0.035 * k, 8), new FigOpt { r = 0.006 * k, n = 3, reg = "gold", col = col(BRONZE), w = W1(Hbody) });
            }
            return new HorseGeo { gb = gb, J = J, k = k };
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using static FigMath;

    // ------------------------------------------------------------------ 模板（共用几何体，= JS tpl）
    public sealed class FigTemplate
    {
        public FigLook look; public FigProps pr; public string lod, key, style;
        public M4d[] boneInverses;
        public int refs, squeeze, faceSize;
        public Dictionary<string, int> parts = new Dictionary<string, int>();
        public FigWeaponInfo weapon; public FigShieldInfo shieldInfo; public bool hasShield;
        public FigGeo geometry, headGeo, weaponGeo, shieldGeo;
        public double tris, ms;
        public object unity;            // Unity 端的网格缓存（View/Figure.cs 填写与释放）
    }
    // 战马模板
    public sealed class FigHorseTemplate
    {
        public string key; public FigGeo geometry; public V3d[] J; public double k; public int refs; public double tris, ms; public M4d[] inv;
        public object unity;
    }

    // 统计（= JS STATS）
    public static class FigStats
    {
        public static int built, templates, textures;
        public static double atlasMs, envMs, buildMs, lastBuildMs, bakeMs, preloadMs, maxStepMs, maxSliceMs;
        public static string maxStepJob;
        public static readonly Dictionary<string, int> tris = new Dictionary<string, int>();
        public static readonly Dictionary<string, Dictionary<string, int>> parts = new Dictionary<string, Dictionary<string, int>>();
    }

    public static partial class FigBuild
    {
        // 盾的挂点：前臂上、外侧
        public struct Sock { public V3d p; public Q4d q; }
        public static Sock shieldLocal(FigProps pr)
        {
            var d = V(pr.armDirL.x, pr.armDirL.y, 0); var outv = V(Math.Cos(FigBones.ARM_A), Math.Sin(FigBones.ARM_A), 0);
            V3d Y = d, Z = outv, X = v3cross(Y, Z);
            var q = Q4d.FromMatrix(M4d.Basis(X, Y, Z));
            return new Sock { p = v3add(v3mul(d, pr.foL * 0.45), v3mul(outv, 0.035)), q = q };
        }
        public static M4d shieldRest(FigProps pr)
        {
            var s = shieldLocal(pr); var J = pr["foreArmL"];
            return M4d.Compose(V(J.x + s.p.x, J.y + s.p.y, J.z + s.p.z), s.q, V(1, 1, 1));
        }
        // 构建模板；超出该 LOD 的三角形预算时按 SQUEEZE 逐级压缩重建
        public static FigTemplate buildTemplate(FigLook look)
        {
            double t0 = Now();
            string lodName = look.lod ?? "high";
            double budget = FigTables.TRI_BUDGET.Or(lodName, 15000.0);
            FigTemplate tpl = null;
            int levels = FigLod.SqueezeLevels(lodName);
            for (int lv = 0; lv <= levels; lv++)
            {
                look.lodP = FigLod.Squeezed(lodName, lv);
                tpl = buildTemplateOnce(look);
                tpl.squeeze = lv;
                if (tpl.tris <= budget) break;
                if (lv < levels) FigStats.templates--;
            }
            tpl.ms = Now() - t0; FigStats.lastBuildMs = tpl.ms;
            return tpl;
        }
        public static FigTemplate buildTemplateOnce(FigLook look)
        {
            double t0 = Now();
            var X = makeCtx(look);
            var gb = X.gb; var pr = X.pr; var lod = X.lod;
            if (lod.detail == 0) gb.noEdges = true;
            gb.detail = lod.detail;
            var parts = new Dictionary<string, int>();
            int last = 0;
            Action<string> mark = name => { int v; parts.TryGetValue(name, out v); parts[name] = v + gb.tris - last; last = gb.tris; };
            buildTorso(X); mark("torso"); buildNeck(X); mark("neck");
            foreach (var S in new[] { "L", "R" }) { buildArm(X, S); mark("arms"); buildLeg(X, S); mark("legs"); buildPauldron(X, S); mark("pauldrons"); }
            buildSkirt(X); mark("skirt"); buildCape(X); mark("cape"); buildRibbons(X); buildBackGear(X); mark("back");
            var Hp = headParams(look.spec);
            Hp.lodF = lod.head[1] >= 24 ? 1 : lod.head[1] >= 14 ? 0.3 : 0;
            var hj = pr["head"];
            gb.xf = M4d.Translation(hj.x, hj.y, hj.z);
            var cov = buildHeadgear(X, Hp); mark("headgear");
            buildHair(X, Hp, cov); mark("hair");
            buildBeard(X, Hp); mark("beard");
            var tpl = new FigTemplate { look = look, pr = pr, lod = look.lod, boneInverses = makeBoneInverses(pr), refs = 0, key = look.key, parts = parts };
            string weaponId = look.weapon ?? "jian";
            string wstyle = WeaponStyle(weaponId);
            tpl.style = wstyle;
            if (look.kind == "soldier")
            {
                // 头、兵器、盾并入同一蒙皮网格（一次绘制）
                buildHead(gb, Hp, lod.head[0], lod.head[1], look.face ?? "faceA", col(look.skin), W1(FigBones.head), lod.detail >= 1); mark("head");
                string side = wstyle == "bow" ? "L" : "R";
                gb.xf = socketRest(pr, side);
                tpl.weapon = buildWeaponInto(gb, weaponId, lod, W1(Bn("hand" + side))); mark("weapon");
                if (look.shield != null) { gb.xf = shieldRest(pr); tpl.shieldInfo = buildShieldInto(gb, look.shield, lod, look.C.faction, W1(FigBones.foreArmL)); tpl.hasShield = true; mark("shield"); }
                gb.xf = null;
            }
            else
            {
                gb.xf = null;
                var hgb = new FigGB();
                buildHead(hgb, Hp, lod.head[0], lod.head[1], null, new FCol(1, 1, 1), W1(0), true);
                tpl.headGeo = hgb.build(false);
                tpl.faceSize = look.lod == "high" ? 512 : look.lod == "mid" ? 256 : 128;
                var wgb = new FigGB();
                wgb.detail = lod.detail;
                tpl.weapon = buildWeaponInto(wgb, weaponId, lod, W1(0));
                tpl.weaponGeo = wgb.build(false);
                if (look.shield != null) { var sgb = new FigGB(); tpl.shieldInfo = buildShieldInto(sgb, look.shield, lod, look.C.faction, W1(0)); tpl.shieldGeo = sgb.build(false); }
            }
            tpl.geometry = gb.build(true);
            tpl.tris = gb.tris + (tpl.headGeo != null ? tpl.headGeo.Tris : 0) + (tpl.weaponGeo != null ? tpl.weaponGeo.Tris * (wstyle == "dual" ? 2 : 1) : 0) + (tpl.shieldGeo != null ? tpl.shieldGeo.Tris : 0);
            tpl.ms = Now() - t0;
            FigStats.templates++;
            FigStats.buildMs += tpl.ms; FigStats.lastBuildMs = tpl.ms;
            string tk = (look.kind == "soldier" ? look.type + "/" + look.culture : look.name) + "@" + look.lod;
            lock (FigStats.tris) { FigStats.tris[tk] = (int)jround(tpl.tris); FigStats.parts[tk] = parts; }
            return tpl;
        }

        // ---- 战马模板
        public static FigHorseTemplate buildHorseTemplate(FigHorseOpts opts)
        {
            double t0 = Now();
            var r = buildHorseGeometry(opts);
            var t = new FigHorseTemplate { key = opts.Key(), geometry = r.gb.build(true), J = r.J, k = r.k, refs = 0, tris = r.gb.tris };
            t.inv = new M4d[r.J.Length];
            for (int i = 0; i < r.J.Length; i++) t.inv[i] = M4d.Translation(-r.J[i].x, -r.J[i].y, -r.J[i].z);
            t.ms = Now() - t0;
            FigStats.buildMs += t.ms; FigStats.templates++;
            lock (FigStats.tris) FigStats.tris["horse@" + opts.lod] = (int)t.tris;
            return t;
        }

        // ---- 外观 → 模板键（= JS generalKey 与士兵的键）
        public static string generalKey(FigLook look, FigOpts opts)
        {
            var sp = look.spec;
            string spk = sp != null ? (sp.S("key") ?? "") : "";
            return string.Join("|", new[] { "G", look.name ?? "", look.culture ?? "", look.lod ?? "", look.weapon ?? "", look.shield ?? "", spk, opts != null && opts.mounted ? "m" : "" });
        }
        public static string soldierKey(FigLook look) { return string.Join("|", new[] { "S", look.type, look.culture, look.lod, look.seed.ToString() }); }
    }

    // ------------------------------------------------------------------ 模板缓存（= JS TEMPLATES / HTPL）
    public static class FigTemplates
    {
        static readonly Dictionary<string, FigTemplate> T = new Dictionary<string, FigTemplate>();
        static readonly Dictionary<string, FigHorseTemplate> H = new Dictionary<string, FigHorseTemplate>();
        public static Action<FigTemplate> OnFree;          // Unity 端：释放网格
        public static Action<FigHorseTemplate> OnFreeHorse;
        public static int Count { get { return T.Count; } }
        public static int HorseCount { get { return H.Count; } }
        public static FigTemplate Get(FigLook look, string key)
        {
            FigTemplate t;
            if (!T.TryGetValue(key, out t)) { look.key = key; t = FigBuild.buildTemplate(look); T[key] = t; }
            t.refs++;
            return t;
        }
        public static bool Has(string key) { return T.ContainsKey(key); }
        public static void Release(FigTemplate t)
        {
            if (t == null) return;
            t.refs--;
            // 士兵模板常驻（复用率高）；名将模板在无人使用时释放
            if (t.refs <= 0 && t.look.kind != "soldier")
            {
                T.Remove(t.key);
                if (OnFree != null) OnFree(t);
            }
        }
        public static FigHorseTemplate GetHorse(FigHorseOpts opts)
        {
            string key = opts.Key();
            FigHorseTemplate t;
            if (!H.TryGetValue(key, out t)) { t = FigBuild.buildHorseTemplate(opts); H[key] = t; }
            t.refs++;
            return t;
        }
        public static void ReleaseHorse(FigHorseTemplate t)
        {
            if (t == null) return;
            if (--t.refs <= 0) { H.Remove(t.key); if (OnFreeHorse != null) OnFreeHorse(t); }
        }
        // 释放无人使用的模板几何体
        public static void Clear()
        {
            var dead = new List<string>();
            foreach (var kv in T) if (kv.Value.refs <= 0) dead.Add(kv.Key);
            foreach (var k in dead) { var t = T[k]; T.Remove(k); if (OnFree != null) OnFree(t); }
        }
    }

    // ------------------------------------------------------------------ three 坐标 → Unity 坐标（x 镜像）
    // = UNITY-V3 §1 的「z 取反 + 根节点绕 Y 转 180°」：位置 / 法线 (x, y, z) → (−x, y, z)，
    //   三角形绕序反转，四元数 (x, y, z, w) → (x, −y, −z, w)，矩阵 M → S·M·S（S = diag(−1, 1, 1)）。
    public static class FigConv
    {
        public static void Pos(float[] src, float[] dst) { for (int i = 0; i < src.Length; i += 3) { dst[i] = -src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2]; } }
        public static int[] Tris(int[] idx) { var r = new int[idx.Length]; for (int i = 0; i < idx.Length; i += 3) { r[i] = idx[i]; r[i + 1] = idx[i + 2]; r[i + 2] = idx[i + 1]; } return r; }
        public static V3d P(V3d p) { return new V3d(-p.x, p.y, p.z); }
        public static Q4d Q(Q4d q) { return new Q4d(q.x, -q.y, -q.z, q.w); }
        // S·M·S：第 0 行与第 0 列取反（[0][0] 不变）
        public static M4d M(M4d m)
        {
            var r = m.Copy(); var e = r.e;
            // 列主序：e[c*4 + r]；第 0 行（r = 0）：e[4], e[8], e[12]；第 0 列（c = 0）：e[1], e[2], e[3]
            e[4] = -e[4]; e[8] = -e[8]; e[12] = -e[12]; e[1] = -e[1]; e[2] = -e[2]; e[3] = -e[3];
            return r;
        }
    }
}
