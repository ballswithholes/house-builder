// ==========================================================================
// 三国志II 霸王的大陆 · 单挑格斗 · 程序化低多边形模型（网页版 js/duel-game.js「视图：网格构建 / 兵器 / 武将模型」的移植）
//
// DuelPB 与网页版的 PB 一样直接使用 three.js 坐标（右手系，逆时针为正面）书写，几何代码可逐行照搬；
// 生成 Unity 网格时把每个顶点的 z 取反（并因此保持三角形顺序 = Unity 的顺时针正面），法线由 Unity 重算。
// 变换（DuelXf）同理：three 坐标下的位置 / 四元数 → Unity（x, y, −z）/（−qx, −qy, qz, qw）。
// ==========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    // three 坐标 → Unity 的换算与欧拉角（three 的弧度、指定顺序）
    public static class DuelXf
    {
        public static Vector3 P(Vector3 t) { return new Vector3(t.x, t.y, -t.z); }
        public static Vector3 P(float x, float y, float z) { return new Vector3(x, y, -z); }
        public static Quaternion Q(Quaternion t) { return new Quaternion(-t.x, -t.y, t.z, t.w); }
        const float R2D = Mathf.Rad2Deg;
        static Quaternion AX(float r) { return Quaternion.AngleAxis(r * R2D, Vector3.right); }
        static Quaternion AY(float r) { return Quaternion.AngleAxis(r * R2D, Vector3.up); }
        static Quaternion AZ(float r) { return Quaternion.AngleAxis(r * R2D, Vector3.forward); }
        // three 的 Euler（XYZ 顺序 = Rx·Ry·Rz）在 three 坐标下的四元数
        public static Quaternion EulerXYZ(float x, float y, float z) { return AX(x) * AY(y) * AZ(z); }
        // three 的 Euler（YZX 顺序 = Ry·Rz·Rx）
        public static Quaternion EulerYZX(float x, float y, float z) { return AY(y) * AZ(z) * AX(x); }
        public static void Pos(Transform tr, Vector3 three) { tr.localPosition = P(three); }
        public static void Pos(Transform tr, float x, float y, float z) { tr.localPosition = new Vector3(x, y, -z); }
        public static void Rot(Transform tr, Quaternion three) { tr.localRotation = Q(three); }
        public static void RotXYZ(Transform tr, float x, float y, float z) { tr.localRotation = Q(EulerXYZ(x, y, z)); }
        public static void RotYZX(Transform tr, float x, float y, float z) { tr.localRotation = Q(EulerYZX(x, y, z)); }

        static readonly Dictionary<string, Color> cache = new Dictionary<string, Color>();
        public static Color C(string hex)
        {
            if (hex == null) return Color.white;
            Color c;
            if (cache.TryGetValue(hex, out c)) return c;
            if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.white;
            c.a = 1;
            cache[hex] = c;
            return c;
        }
        // = SG.Gfx.shade / lerpColor
        public static Color Sh(Color c, float k) { var s = Art.Shade(c, k); s.a = 1; return s; }
        public static Color Sh(string c, float k) { return Sh(C(c), k); }
        public static Color Mix(Color a, Color b, float t) { var m = Color.Lerp(a, b, t); m.a = 1; return m; }
    }

    // 平面着色、顶点色的小型网格构建器（three 坐标）
    public sealed class DuelPB
    {
        readonly List<Vector3> p = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        bool hasM; Matrix4x4 M = Matrix4x4.identity;

        static Color C(string h) { return DuelXf.C(h); }
        static Color Sh(Color c, float k) { return DuelXf.Sh(c, k); }
        public static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        // 局部变换：位置 + 欧拉角（XYZ）+ 缩放
        public DuelPB At(float x, float y, float z, float rx = 0, float ry = 0, float rz = 0, float s = 1)
        {
            if (s == 0) s = 1;
            M = Matrix4x4.TRS(new Vector3(x, y, z), DuelXf.EulerXYZ(rx, ry, rz), new Vector3(s, s, s));
            hasM = true;
            return this;
        }
        public DuelPB At() { hasM = false; return this; }
        void Vx(Vector3 v) { p.Add(hasM ? M.MultiplyPoint3x4(v) : v); }
        public void Tri(Vector3 a, Vector3 b, Vector3 cc, Color col) { Vx(a); Vx(b); Vx(cc); c.Add(col); c.Add(col); c.Add(col); }
        public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col) { Tri(a, b, cc, col); Tri(a, cc, d, col); }
        // 逐顶点着色的三角形（颜色在面内平滑过渡）
        public void TriV(Vector3 a, Vector3 b, Vector3 cc, Color ca, Color cb, Color c3) { Vx(a); Vx(b); Vx(cc); c.Add(ca); c.Add(cb); c.Add(c3); }
        // 任意凸六面体：P[i]，i = x位 + 2·y位 + 4·z位
        public void Hex(Vector3[] P, Color col, Color? top = null, Color? bot = null)
        {
            Quad(P[0], P[4], P[6], P[2], col); Quad(P[1], P[3], P[7], P[5], col);
            Quad(P[4], P[5], P[7], P[6], Sh(col, 0.04f)); Quad(P[0], P[2], P[3], P[1], Sh(col, -0.06f));
            Quad(P[2], P[6], P[7], P[3], top ?? Sh(col, 0.1f)); Quad(P[0], P[1], P[5], P[4], bot ?? Sh(col, -0.3f));
        }
        public void Box(float cx, float cy, float cz, float sx, float sy, float sz, Color col, Color? top = null)
        {
            float x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
            Hex(new[] { V(x0, y0, z0), V(x1, y0, z0), V(x0, y1, z0), V(x1, y1, z0), V(x0, y0, z1), V(x1, y0, z1), V(x0, y1, z1), V(x1, y1, z1) }, col, top);
        }
        public void Box(float cx, float cy, float cz, float sx, float sy, float sz, string col) { Box(cx, cy, cz, sx, sy, sz, C(col)); }
        // 竖直棱台：底面 (sx0 × sz0) 在 y0，顶面 (sx1 × sz1) 在 y1，顶面可偏移 (ox, oz)
        public void Taper(float cx, float cz, float y0, float y1, float sx0, float sz0, float sx1, float sz1, Color col, float ox = 0, float oz = 0, Color? top = null)
        {
            float a = sx0 / 2, b = sz0 / 2, cc = sx1 / 2, d = sz1 / 2, tx = cx + ox, tz = cz + oz;
            Hex(new[] { V(cx - a, y0, cz - b), V(cx + a, y0, cz - b), V(tx - cc, y1, tz - d), V(tx + cc, y1, tz - d),
                V(cx - a, y0, cz + b), V(cx + a, y0, cz + b), V(tx - cc, y1, tz + d), V(tx + cc, y1, tz + d) }, col, top);
        }
        public void Taper(float cx, float cz, float y0, float y1, float sx0, float sz0, float sx1, float sz1, string col, float ox = 0, float oz = 0) { Taper(cx, cz, y0, y1, sx0, sz0, sx1, sz1, C(col), ox, oz); }
        // 竖直棱柱 / 圆台
        public void Prism(float cx, float cz, float y0, float y1, float r0, float r1, int seg, Color col)
        {
            Color top = Sh(col, 0.1f), bot = Sh(col, -0.3f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = (float)i / seg * Mathf.PI * 2, a1 = (float)(i + 1) / seg * Mathf.PI * 2;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                Vector3 b0 = V(cx + c0 * r0, y0, cz - s0 * r0), b1 = V(cx + c1 * r0, y0, cz - s1 * r0);
                Vector3 t0 = V(cx + c0 * r1, y1, cz - s0 * r1), t1 = V(cx + c1 * r1, y1, cz - s1 * r1);
                Color side = i % 2 == 1 ? col : Sh(col, -0.05f);
                if (r1 > 1e-4f) Quad(b0, b1, t1, t0, side); else Tri(b0, b1, t0, side);
                if (r1 > 1e-4f) Tri(V(cx, y1, cz), t0, t1, top);
                if (r0 > 1e-4f) Tri(V(cx, y0, cz), b1, b0, bot);
            }
        }
        public void Prism(float cx, float cz, float y0, float y1, float r0, float r1, int seg, string col) { Prism(cx, cz, y0, y1, r0, r1, seg, C(col)); }
        // 任意方向的棱柱：a → b
        public void Rod(Vector3 a, Vector3 b, float r0, float r1, int seg, Color col)
        {
            float len = Vector3.Distance(a, b);
            if (len < 1e-5f) return;
            var dir = (b - a) / len;
            var local = Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one);
            bool hs = hasM; var save = M;
            M = hs ? save * local : local; hasM = true;
            Prism(0, 0, 0, len, r0, r1, seg, col);
            M = save; hasM = hs;
        }
        public void Rod(Vector3 a, Vector3 b, float r0, float r1, int seg, string col) { Rod(a, b, r0, r1, seg, C(col)); }
        // x-y 平面上的多边形沿 z 拉伸（刀刃、旗面、披风）；以重心扇形三角化
        public void Slab(Vector2[] pts, float z0, float z1, Color col, Color? edge = null)
        {
            if (z0 > z1) { var t = z0; z0 = z1; z1 = t; }
            Color e = edge ?? Sh(col, -0.15f);
            float cx = 0, cy = 0;
            foreach (var q in pts) { cx += q.x; cy += q.y; }
            cx /= pts.Length; cy /= pts.Length;
            int n = pts.Length;
            Color back = Sh(col, -0.08f);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % n];
                Tri(V(cx, cy, z1), V(a.x, a.y, z1), V(b.x, b.y, z1), col);
                Tri(V(cx, cy, z0), V(b.x, b.y, z0), V(a.x, a.y, z0), back);
                Quad(V(a.x, a.y, z0), V(b.x, b.y, z0), V(b.x, b.y, z1), V(a.x, a.y, z1), e);
            }
        }
        public void Slab(Vector2[] pts, float z0, float z1, string col, string edge = null) { Slab(pts, z0, z1, C(col), edge != null ? C(edge) : (Color?)null); }
        public void Slab(Vector2[] pts, float z0, float z1, Color col, string edge) { Slab(pts, z0, z1, col, edge != null ? C(edge) : (Color?)null); }
        // 低多边形椭球（八面体细分一次）
        static readonly Vector3[] BP = { V(0, 1, 0), V(0, -1, 0), V(-1, 0, 0), V(1, 0, 0), V(0, 0, 1), V(0, 0, -1) };
        static readonly int[,] BF = { { 0, 4, 3 }, { 0, 3, 5 }, { 0, 5, 2 }, { 0, 2, 4 }, { 1, 3, 4 }, { 1, 5, 3 }, { 1, 2, 5 }, { 1, 4, 2 } };
        public void Blob(float cx, float cy, float cz, float rx, float ry, float rz, Color col, int seed = 1)
        {
            var r = new SeededRandom(seed != 0 ? seed : 1);
            for (int fi = 0; fi < 8; fi++)
            {
                Vector3 a = BP[BF[fi, 0]], b = BP[BF[fi, 1]], cc = BP[BF[fi, 2]];
                Vector3 ab = (a + b).normalized, bc = (b + cc).normalized, ca = (cc + a).normalized;
                var tris = new[] { new[] { a, ab, ca }, new[] { ab, b, bc }, new[] { ca, bc, cc }, new[] { ab, bc, ca } };
                foreach (var tr in tris)
                {
                    float k = 0.94f + (float)r.NextDouble() * 0.12f;
                    Vector3 q0 = V(cx + tr[0].x * rx * k, cy + tr[0].y * ry * k, cz + tr[0].z * rz * k);
                    Vector3 q1 = V(cx + tr[1].x * rx * k, cy + tr[1].y * ry * k, cz + tr[1].z * rz * k);
                    Vector3 q2 = V(cx + tr[2].x * rx * k, cy + tr[2].y * ry * k, cz + tr[2].z * rz * k);
                    Tri(q0, q1, q2, Sh(col, (tr[0].y + tr[1].y + tr[2].y) * 0.05f));
                }
            }
        }
        public void Blob(float cx, float cy, float cz, float rx, float ry, float rz, string col, int seed = 1) { Blob(cx, cy, cz, rx, ry, rz, C(col), seed); }
        public bool Empty { get { return p.Count == 0; } }
        public int Count { get { return p.Count; } }

        // → Unity 网格（z 取反；三角形顺序不变即为 Unity 的正面）
        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (p.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var v = new List<Vector3>(p.Count);
            foreach (var q in p) v.Add(new Vector3(q.x, q.y, -q.z));
            var t = new int[p.Count];
            for (int i = 0; i < t.Length; i++) t[i] = i;
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    // 兵器、盾、弓、箭与武将各部件
    public static class DuelModels
    {
        public const string STEEL = "#cdd3da", EDGE = "#eef2f6", GOLD = "#c9a24e", RED = "#c8382c";
        static Color C(string h) { return DuelXf.C(h); }
        static Color Sh(Color c, float k) { return DuelXf.Sh(c, k); }
        static Color Sh(string c, float k) { return DuelXf.Sh(c, k); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Vector2 P2(float x, float y) { return new Vector2(x, y); }
        static float Cos(float a) { return Mathf.Cos(a); }
        static float Sin(float a) { return Mathf.Sin(a); }
        const float PI = Mathf.PI;

        // ------------------------------------------------------------ 兵器 --
        // 兵器在局部坐标中沿 +y 伸出，握点（右手）在原点；+x 一侧为刃口
        static void Tassel(DuelPB pb, float y, string col)
        {
            pb.Blob(0, y, 0, 0.05f, 0.035f, 0.05f, col, 3);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * PI * 2;
                pb.Rod(V(Cos(a) * 0.03f, y - 0.01f, Sin(a) * 0.03f), V(Cos(a) * 0.06f, y - 0.14f - (i % 2) * 0.03f, Sin(a) * 0.05f), 0.014f, 0.004f, 3, col);
            }
        }
        static void Leaf(DuelPB pb, float y0, float len, float w, string col)
        {
            pb.Slab(new[] { P2(0, y0), P2(w, y0 + len * 0.22f), P2(w * 0.7f, y0 + len * 0.6f), P2(0, y0 + len), P2(-w * 0.7f, y0 + len * 0.6f), P2(-w, y0 + len * 0.22f) }, -0.009f, 0.009f, col, EDGE);
            pb.Box(0, y0 + len * 0.42f, 0, 0.012f, len * 0.7f, 0.022f, Sh(col, -0.2f));
        }
        static void Shaft(DuelPB pb, float y0, float y1, string col, float r = 0.022f)
        {
            pb.Prism(0, 0, y0, y1, r, r * 0.92f, 6, col);
            pb.Prism(0, 0, y0 - 0.06f, y0, r * 0.5f, r * 1.1f, 6, GOLD);   // 鐏
        }
        static Vector2[] Cres(float y, float k, float s)
        {
            return new[] { P2(0.02f * s, y - 0.06f * k), P2(0.11f * s * k, y - 0.1f * k), P2(0.21f * s * k, y - 0.04f * k), P2(0.25f * s * k, y + 0.05f * k), P2(0.19f * s * k, y + 0.12f * k), P2(0.12f * s * k, y + 0.06f * k), P2(0.03f * s, y + 0.03f * k) };
        }
        static void JiHead(DuelPB pb, float y, float k, string col)
        {
            Leaf(pb, y, 0.34f * k, 0.04f * k, col);
            pb.Prism(0, 0, y - 0.05f * k, y + 0.02f, 0.03f * k, 0.03f * k, 6, GOLD);
            pb.Slab(Cres(y, k, 1), -0.008f, 0.008f, col, EDGE);
            var r = Cres(y, k, -1); System.Array.Reverse(r);
            pb.Slab(r, -0.008f, 0.008f, col, EDGE);
        }
        public sealed class WeaponMesh { public DuelPB pb; public float tip, len; }
        public static WeaponMesh BuildWeapon(string type, DuelLook look)
        {
            var pb = new DuelPB();
            string blade = look.goldTip ? "#e0b64a" : STEEL;
            float tip = 1.8f, len = 0.45f;
            switch (type)
            {
                case "spear":
                    Shaft(pb, -0.6f, 1.45f, look.goldTip ? "#7a2a1c" : "#5a1e18");
                    pb.Prism(0, 0, 1.4f, 1.48f, 0.03f, 0.026f, 6, GOLD);
                    Tassel(pb, 1.42f, look.weaponName == "涯角枪" ? "#e8e8ee" : RED);
                    Leaf(pb, 1.48f, 0.42f, 0.05f, blade);
                    tip = 1.9f; len = 0.42f;
                    break;
                case "snake":
                    {
                        Shaft(pb, -0.6f, 1.5f, "#1e1a1a", 0.024f);
                        pb.Prism(0, 0, 1.45f, 1.53f, 0.032f, 0.028f, 6, "#6a6a72");
                        Tassel(pb, 1.46f, "#2a2a2a");
                        // 蜿蜒的蛇形矛刃
                        int N = 9; float y0 = 1.52f, L = 0.55f;
                        System.Func<float, float> xc = t => Sin(t * PI * 3) * 0.045f * (1 - t * 0.6f);
                        System.Func<float, float> w = t => 0.04f * (1 - t) + 0.004f;
                        for (int i = 0; i < N; i++)
                        {
                            float t0 = (float)i / N, t1 = (float)(i + 1) / N;
                            pb.Slab(new[] { P2(xc(t0) - w(t0), y0 + t0 * L), P2(xc(t0) + w(t0), y0 + t0 * L), P2(xc(t1) + w(t1), y0 + t1 * L), P2(xc(t1) - w(t1), y0 + t1 * L) }, -0.009f, 0.009f, STEEL, EDGE);
                        }
                        tip = y0 + L; len = L;
                        break;
                    }
                case "ji":
                    Shaft(pb, -0.6f, 1.42f, "#7a2a1c");
                    JiHead(pb, 1.46f, 1, blade);
                    Tassel(pb, 1.32f, RED);
                    tip = 1.8f; len = 0.45f;
                    break;
                case "guandao":
                    Shaft(pb, -0.65f, 1.22f, "#3b2a1c", 0.024f);
                    pb.Prism(0, 0, -0.66f, -0.56f, 0.0f, 0.03f, 6, GOLD);
                    pb.Blob(0, 1.24f, 0, 0.055f, 0.06f, 0.05f, "#2f7d4a", 5);          // 龙吞口
                    pb.Box(0.04f, 1.27f, 0, 0.05f, 0.04f, 0.03f, GOLD);
                    Tassel(pb, 1.17f, RED);
                    pb.Slab(new[] { P2(-0.03f, 1.24f), P2(0.07f, 1.22f), P2(0.17f, 1.36f), P2(0.21f, 1.56f), P2(0.18f, 1.76f), P2(0.06f, 1.92f), P2(0.02f, 1.82f), P2(-0.015f, 1.62f), P2(-0.04f, 1.5f), P2(-0.1f, 1.46f), P2(-0.05f, 1.36f) }, -0.01f, 0.01f, STEEL, EDGE);
                    pb.Slab(new[] { P2(0.0f, 1.32f), P2(0.08f, 1.33f), P2(0.1f, 1.42f), P2(0.03f, 1.44f) }, 0.01f, 0.014f, "#2f7d4a");
                    tip = 1.9f; len = 0.68f;
                    break;
                case "poleblade":
                    Shaft(pb, -0.62f, 1.2f, "#4a2a1a");
                    pb.Prism(0, 0, 1.16f, 1.24f, 0.032f, 0.03f, 6, GOLD);
                    Tassel(pb, 1.14f, RED);
                    pb.Slab(new[] { P2(-0.03f, 1.22f), P2(0.06f, 1.22f), P2(0.14f, 1.4f), P2(0.16f, 1.62f), P2(0.08f, 1.82f), P2(0.0f, 1.72f), P2(-0.03f, 1.46f) }, -0.01f, 0.01f, STEEL, EDGE);
                    tip = 1.8f; len = 0.6f;
                    break;
                case "axe":
                    Shaft(pb, -0.6f, 1.48f, "#4a2a1a", 0.025f);
                    Leaf(pb, 1.48f, 0.2f, 0.03f, STEEL);
                    pb.Slab(new[] { P2(0.02f, 1.16f), P2(0.1f, 1.1f), P2(0.24f, 1.06f), P2(0.3f, 1.26f), P2(0.25f, 1.48f), P2(0.11f, 1.44f), P2(0.02f, 1.38f) }, -0.012f, 0.012f, STEEL, EDGE);
                    pb.Slab(new[] { P2(-0.02f, 1.38f), P2(-0.1f, 1.33f), P2(-0.02f, 1.22f) }, -0.01f, 0.01f, "#8e939c");
                    pb.Prism(0, 0, 1.12f, 1.44f, 0.034f, 0.034f, 6, "#5c5c64");
                    tip = 1.68f; len = 0.4f;
                    break;
                case "bigblade":
                    pb.Prism(0, 0, -0.5f, 0.32f, 0.026f, 0.026f, 6, "#3a2418");
                    for (float y = -0.4f; y < 0.3f; y += 0.12f) pb.Prism(0, 0, y, y + 0.04f, 0.03f, 0.03f, 6, "#7a2a1c");
                    pb.Box(0, 0.33f, 0, 0.16f, 0.035f, 0.06f, GOLD);
                    pb.Slab(new[] { P2(-0.025f, 0.35f), P2(0.075f, 0.35f), P2(0.1f, 0.62f), P2(0.115f, 1.2f), P2(0.09f, 1.45f), P2(0.0f, 1.52f), P2(-0.03f, 1.32f), P2(-0.035f, 0.62f) }, -0.012f, 0.012f, STEEL, EDGE);
                    tip = 1.5f; len = 1.1f;
                    break;
                case "sword":
                case "twinsword":
                    pb.Prism(0, 0, -0.12f, 0.06f, 0.02f, 0.02f, 6, "#2a1c14");
                    pb.Blob(0, -0.14f, 0, 0.03f, 0.03f, 0.03f, GOLD, 2);
                    pb.Box(0, 0.075f, 0, 0.13f, 0.03f, 0.045f, GOLD);
                    pb.Slab(new[] { P2(-0.022f, 0.09f), P2(0.022f, 0.09f), P2(0.02f, 0.84f), P2(0, 0.93f), P2(-0.02f, 0.84f) }, -0.007f, 0.007f, STEEL, EDGE);
                    pb.Box(0, 0.48f, 0, 0.008f, 0.72f, 0.016f, Sh(STEEL, -0.2f));
                    tip = 0.93f; len = 0.82f;
                    break;
                case "dao":
                case "twindao":
                    pb.Prism(0, 0, -0.13f, 0.06f, 0.02f, 0.02f, 6, "#2a1c14");
                    pb.Prism(0, 0, -0.2f, -0.13f, 0.04f, 0.04f, 6, GOLD);            // 环首
                    pb.Box(0, 0.07f, 0, 0.1f, 0.03f, 0.05f, GOLD);
                    pb.Slab(new[] { P2(-0.02f, 0.09f), P2(0.025f, 0.09f), P2(0.042f, 0.5f), P2(0.05f, 0.74f), P2(0.02f, 0.88f), P2(-0.025f, 0.8f), P2(-0.022f, 0.42f) }, -0.007f, 0.007f, STEEL, EDGE);
                    tip = 0.86f; len = 0.78f;
                    break;
                case "shortji":
                case "twinji":
                    Shaft(pb, -0.32f, 0.72f, type == "twinji" ? "#2a2a30" : "#5a1e18");
                    JiHead(pb, 0.76f, 0.85f, STEEL);
                    Tassel(pb, 0.66f, RED);
                    tip = 1.05f; len = 0.36f;
                    break;
                case "mace":
                    pb.Prism(0, 0, -0.28f, 0.56f, 0.024f, 0.024f, 6, "#3a2418");
                    pb.Blob(0, 0.66f, 0, 0.1f, 0.12f, 0.1f, "#55565e", 4);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i / 8f * PI * 2, y = 0.62f + (i % 2) * 0.08f;
                        pb.Rod(V(Cos(a) * 0.08f, y, Sin(a) * 0.08f), V(Cos(a) * 0.17f, y + 0.02f, Sin(a) * 0.17f), 0.025f, 0, 4, "#8e939c");
                    }
                    pb.Rod(V(0, 0.76f, 0), V(0, 0.88f, 0), 0.03f, 0, 4, "#8e939c");
                    tip = 0.86f; len = 0.3f;
                    break;
                case "gladius":   // 罗马短剑：宽刃尖头、圆柄头、骨柄
                    pb.Prism(0, 0, -0.11f, 0.05f, 0.022f, 0.02f, 6, "#e8dcc0");
                    pb.Blob(0, -0.13f, 0, 0.04f, 0.035f, 0.04f, "#c8a050", 3);
                    pb.Box(0, 0.065f, 0, 0.1f, 0.035f, 0.05f, "#c8a050");
                    pb.Slab(new[] { P2(-0.03f, 0.08f), P2(0.03f, 0.08f), P2(0.028f, 0.5f), P2(0.033f, 0.58f), P2(0, 0.7f), P2(-0.033f, 0.58f), P2(-0.028f, 0.5f) }, -0.008f, 0.008f, STEEL, EDGE);
                    tip = 0.7f; len = 0.6f;
                    break;
                case "tachi":     // 倭直刀：单刃直身、圆镡、长柄
                    pb.Prism(0, 0, -0.2f, 0.04f, 0.02f, 0.02f, 6, "#2a1c14");
                    pb.Prism(0, 0, 0.04f, 0.06f, 0.06f, 0.06f, 8, "#4a4a50");
                    pb.Slab(new[] { P2(-0.018f, 0.06f), P2(0.024f, 0.06f), P2(0.024f, 0.8f), P2(-0.005f, 0.9f), P2(-0.018f, 0.82f) }, -0.006f, 0.006f, STEEL, EDGE);
                    tip = 0.9f; len = 0.84f;
                    break;
                case "handaxe":   // 日耳曼战斧：短柄 + 胡形斧头
                    pb.Prism(0, 0, -0.25f, 0.66f, 0.022f, 0.02f, 6, "#5a3a22");
                    pb.Slab(new[] { P2(0.0f, 0.44f), P2(0.12f, 0.38f), P2(0.2f, 0.36f), P2(0.22f, 0.52f), P2(0.18f, 0.66f), P2(0.1f, 0.62f), P2(0.0f, 0.62f) }, -0.012f, 0.012f, STEEL, EDGE);
                    pb.Prism(0, 0, 0.42f, 0.66f, 0.03f, 0.03f, 6, "#5c5c64");
                    tip = 0.7f; len = 0.3f;
                    break;
                case "lance":     // 骑矛：更长的矛杆、小矛头、三角旗
                    Shaft(pb, -0.7f, 1.62f, "#6a4a2a", 0.022f);
                    pb.Prism(0, 0, 1.58f, 1.65f, 0.028f, 0.024f, 6, "#5c5c64");
                    Leaf(pb, 1.64f, 0.36f, 0.042f, blade);
                    pb.Slab(new[] { P2(0, 1.32f), P2(0.3f, 1.4f), P2(0, 1.52f) }, -0.004f, 0.004f, RED);
                    tip = 2.0f; len = 0.36f;
                    break;
                default:
                    pb.Box(0, 0.4f, 0, 0.04f, 0.8f, 0.02f, STEEL);
                    tip = 0.8f; len = 0.6f;
                    break;
            }
            return new WeaponMesh { pb = pb, tip = tip, len = len };
        }

        // 盾（左手持；局部 +x 为盾面朝向，原点在握把）。scutum 罗马长方弧盾 / round 圆盾 / oval 椭圆盾 / wood 倭木盾
        public static DuelPB BuildShield(string shape, Color team)
        {
            var pb = new DuelPB();
            Color face = team, rim = C(GOLD);
            if (shape == "scutum")
            {
                foreach (var k in new[] { -1, 0, 1 })
                {   // 三片拼出弧面
                    pb.At(-0.02f * Mathf.Abs(k), 0, k * 0.15f, 0, k * 0.32f, 0);
                    pb.Box(0.0f, 0, 0, 0.03f, 0.78f, 0.155f, face);
                    pb.Box(0.017f, 0.37f, 0, 0.012f, 0.03f, 0.16f, rim); pb.Box(0.017f, -0.37f, 0, 0.012f, 0.03f, 0.16f, rim);
                    pb.At();
                }
                pb.Blob(0.035f, 0, 0, 0.06f, 0.06f, 0.06f, rim, 71);
                foreach (var y in new[] { -0.22f, 0.22f }) pb.Box(0.018f, y, 0, 0.012f, 0.025f, 0.4f, Sh(face, 0.35f));
            }
            else if (shape == "oval" || shape == "round" || shape == "wood")
            {
                float R = shape == "round" ? 0.27f : 0.25f, ry = shape == "oval" ? 1.7f : shape == "wood" ? 1.9f : 1;
                var pts = new List<Vector2>();
                for (int i = 0; i < 12; i++) { float a = i / 12f * PI * 2; pts.Add(P2(Cos(a) * R * (shape == "wood" ? 0.85f : 1), Sin(a) * R * ry)); }
                if (shape == "wood") { pts.Clear(); pts.Add(P2(-0.2f, -0.45f)); pts.Add(P2(0.2f, -0.45f)); pts.Add(P2(0.22f, 0.45f)); pts.Add(P2(-0.22f, 0.45f)); }
                // Slab 在 xy 平面挤出 z；绕 y 转 90° 让盾面朝 +x
                pb.At(0, 0, 0, 0, PI / 2, 0);
                pb.Slab(pts.ToArray(), -0.02f, 0.02f, shape == "wood" ? C("#8a6038") : face, shape == "wood" ? C("#5a3a20") : Sh(face, 0.3f));
                if (shape == "wood") { foreach (var y in new[] { -0.25f, 0, 0.25f }) pb.Box(0, y, 0.024f, 0.4f, 0.03f, 0.01f, "#3a2a1a"); pb.Box(0, 0, 0.026f, 0.05f, 0.86f, 0.01f, "#c8382c"); }
                pb.At();
                if (shape != "wood") pb.Blob(0.04f, 0, 0, 0.065f, 0.065f, 0.065f, rim, 73);
                if (shape == "oval") pb.Box(0.03f, 0, 0, 0.012f, 0.7f, 0.04f, Sh(face, 0.4f));
            }
            return pb;
        }

        // 手持的弓（握把在原点，弓臂沿 ±y，弓背朝 +x；弦另画）
        public const float BowTipX = -0.075f, BowTipY = 0.66f;
        public static DuelPB BuildBow()
        {
            var pb = new DuelPB();
            System.Func<float, float> lim = y => -0.12f * Mathf.Pow(Mathf.Min(1, Mathf.Abs(y) / 0.55f), 1.6f);
            foreach (var sg in new[] { 1, -1 })
            {
                var prev = V(0, 0, 0);
                for (int i = 1; i <= 6; i++)
                {
                    float y = sg * 0.55f * i / 6; var q = V(lim(y), y, 0);
                    pb.Rod(prev, q, 0.02f - i * 0.0015f, 0.02f - (i + 1) * 0.0015f, 5, i % 3 == 0 ? GOLD : "#3a1e14");
                    prev = q;
                }
                pb.Rod(prev, V(BowTipX, sg * BowTipY, 0), 0.01f, 0.007f, 4, "#e8dcc0");      // 反曲弓梢（角质）
            }
            pb.Prism(0, 0, -0.07f, 0.07f, 0.024f, 0.024f, 6, "#c8a050");                    // 握把缠绳
            return pb;
        }
        // 箭（沿 +x，箭尾在原点）
        public static DuelPB BuildArrow(bool fin)
        {
            var pb = new DuelPB();
            pb.Rod(V(0, 0, 0), V(0.8f, 0, 0), 0.008f, 0.008f, 4, "#d8c8a0");
            pb.Slab(new[] { P2(0.78f, -0.022f), P2(0.92f, 0), P2(0.78f, 0.022f), P2(0.8f, 0) }, -0.004f, 0.004f, fin ? "#ffe08a" : STEEL, EDGE);
            foreach (var k in new[] { -1, 1 }) pb.Slab(new[] { P2(0.02f, 0), P2(0.16f, 0), P2(0.12f, k * 0.035f), P2(0.0f, k * 0.035f) }, -0.003f, 0.003f, k > 0 ? RED : "#f4efe4");
            pb.Slab(new[] { P2(0.02f, -0.003f), P2(0.16f, -0.003f), P2(0.12f, 0.003f), P2(0.0f, 0.003f) }, -0.035f, 0.035f, "#f4efe4");
            return pb;
        }

        // ------------------------------------------------------------ 武将 --
        // 局部坐标：+x 为正面，+y 向上，+z 为右侧（面向右时朝向镜头）
        public const float LenThigh = 0.46f, LenShin = 0.44f, LenUpper = 0.3f, LenFore = 0.28f;

        public static Dictionary<string, DuelPB> BuildParts(DuelLook look, Color team, double war)
        {
            float bk = (float)look.bulk;
            Color teamC = team;
            Color armor = look.armor != null ? C(look.armor) : teamC;
            Color armorD = Sh(armor, -0.42f);
            Color metal = C(look.metal);
            Color trim = C(war >= 90 ? "#dcb85c" : "#b89a58");
            // 袍服文化：袍色与势力色相混，保证两边仍可分辨
            Color? robe = look.robe != null ? C(look.robe) : look.coat != null && look.torso == "coat" ? DuelXf.Mix(C(look.coat), teamC, 0.2f) : (Color?)null;
            Color cloth = robe.HasValue ? Sh(robe.Value, -0.25f) : look.torso == "segm" ? Sh(teamC, -0.08f) : DuelXf.Mix(Sh(teamC, -0.6f), C("#3a2e28"), 0.5f);
            Color skin = C(look.skin), hair = C(look.hair);
            Color leather = C("#4a3222"), boot = C("#241b16");
            Color capeC = look.cape != null ? C(look.cape) : Sh(teamC, -0.18f);
            bool nan = look.culture == "nanman" || look.culture == "yi" || look.culture == "seasia";
            var P = new Dictionary<string, DuelPB>();

            // ---- 骨盆 / 腰裙
            var pb = new DuelPB();
            pb.Taper(0, 0, -0.1f, 0.06f, 0.24f * bk, 0.31f * bk, 0.25f * bk, 0.31f * bk, cloth);
            pb.Taper(0, 0, 0.01f, 0.09f, 0.27f * bk, 0.34f * bk, 0.27f * bk, 0.34f * bk, look.bare || look.belly ? C("#5a3a24") : leather);
            pb.Box(0.14f * bk, 0.05f, 0, 0.035f, 0.075f, 0.1f, trim);
            if (robe.HasValue)
            {
                pb.At(0.1f * bk, 0.02f, 0, 0, 0, 0.1f); pb.Taper(0, 0, -0.5f, 0, 0.03f, 0.3f * bk, 0.03f, 0.26f * bk, robe.Value); pb.At();
                pb.At(-0.11f * bk, 0.02f, 0, 0, 0, -0.12f); pb.Taper(0, 0, -0.52f, 0, 0.03f, 0.32f * bk, 0.03f, 0.28f * bk, Sh(robe.Value, -0.1f)); pb.At();
            }
            else if (nan)
            {
                pb.At(0.12f * bk, 0.02f, 0, 0, 0, 0.1f); pb.Taper(0, 0, -0.3f, 0, 0.025f, 0.2f * bk, 0.025f, 0.18f * bk, "#8a6a3a"); pb.At();
                pb.At(-0.12f * bk, 0.02f, 0, 0, 0, -0.1f); pb.Taper(0, 0, -0.3f, 0, 0.025f, 0.22f * bk, 0.025f, 0.2f * bk, "#6a4a2a"); pb.At();
            }
            else
            {
                pb.At(0.125f * bk, 0.02f, 0, 0, 0, 0.12f); pb.Taper(0, 0, -0.3f, 0, 0.03f, 0.22f * bk, 0.03f, 0.2f * bk, armor); pb.Box(0.016f, -0.15f, 0, 0.012f, 0.02f, 0.21f * bk, armorD); pb.At();
                pb.At(-0.125f * bk, 0.02f, 0, 0, 0, -0.12f); pb.Taper(0, 0, -0.32f, 0, 0.03f, 0.24f * bk, 0.03f, 0.22f * bk, Sh(armor, -0.08f)); pb.At();
            }
            if (look.bells) foreach (var z in new[] { -0.12f, 0.12f }) pb.Blob(0.08f, 0.0f, z * bk, 0.025f, 0.025f, 0.025f, GOLD, 7);
            P["pelvis"] = pb;

            // ---- 躯干（原点在腰）
            pb = new DuelPB();
            Color chestC = look.bare ? skin : robe ?? (look.rattan ? C("#b08a4a") : nan ? skin : armor);
            if (look.belly)
            {
                pb.Taper(0.02f, 0, 0.0f, 0.2f, 0.3f * bk, 0.34f * bk, 0.31f * bk, 0.36f * bk, chestC);
                pb.Blob(0.1f * bk, 0.13f, 0, 0.13f * bk, 0.13f, 0.17f * bk, chestC, 9);
            }
            else pb.Taper(0, 0, 0.0f, 0.17f, 0.22f * bk, 0.29f * bk, 0.24f * bk, 0.32f * bk, chestC);
            pb.Taper(0, 0, 0.17f, 0.44f, 0.24f * bk, 0.32f * bk, 0.27f * bk, 0.44f * bk, chestC);
            pb.Taper(0, 0, 0.44f, 0.5f, 0.27f * bk, 0.44f * bk, 0.2f * bk, 0.26f * bk, look.bare || nan ? skin : robe.HasValue ? Sh(robe.Value, -0.1f) : armorD);
            if (look.bare)
            {
                foreach (var z in new[] { -0.09f, 0.09f }) pb.Box(0.12f * bk, 0.34f, z * bk, 0.04f, 0.1f, 0.15f * bk, Sh(skin, -0.06f));
                pb.At(0.0f, 0.3f, 0, 0.6f, 0, 0); pb.Box(0, 0, 0, 0.29f * bk, 0.06f, 0.5f * bk, "#5a3a24"); pb.At();   // 斜挎带
            }
            else if (look.rattan)
            {
                for (float y = 0.04f; y < 0.44f; y += 0.07f) pb.Taper(0, 0, y, y + 0.02f, 0.25f * bk, 0.34f * bk, 0.26f * bk, 0.36f * bk, "#8a6a34");
            }
            else if (nan)
            {
                // 兽皮斜披 + 骨饰项链
                pb.At(0.0f, 0.3f, 0.02f, -0.55f, 0, 0); pb.Box(0, 0, 0, 0.29f * bk, 0.14f, 0.47f * bk, "#a07a44"); pb.At();
                for (int i = 0; i < 4; i++) pb.Blob(0.1f * bk + (i % 2) * 0.01f, 0.2f + i * 0.07f, 0.06f - i * 0.04f, 0.02f, 0.02f, 0.02f, "#5a3a20", 11 + i);
                for (int i = -3; i <= 3; i++) pb.Rod(V(0.12f * bk, 0.47f, i * 0.035f), V(0.14f * bk, 0.4f - Mathf.Abs(i) * 0.005f, i * 0.04f), 0.012f, 0.002f, 3, "#efe6d0");
            }
            else if (look.torso == "segm" && !robe.HasValue)
            {
                // 罗马环片甲：钢片横带（深色接缝）+ 胸前搭扣
                for (float y = 0.04f; y < 0.44f; y += 0.055f) pb.Taper(0, 0, y, y + 0.01f, 0.226f * bk + y * 0.06f, 0.3f * bk + y * 0.3f, 0.226f * bk + y * 0.06f, 0.3f * bk + y * 0.3f, armorD);
                foreach (var z in new[] { -0.05f, 0.05f }) pb.Box(0.13f * bk, 0.34f, z * bk, 0.012f, 0.16f, 0.02f, GOLD);
                pb.Box(0.125f * bk, 0.02f, 0, 0.012f, 0.05f, 0.27f * bk, leather);
            }
            else if (look.torso == "scale" && !robe.HasValue)
            {
                // 鱼鳞甲：错位的小甲片
                for (int r = 0; r < 6; r++)
                {
                    float y = 0.06f + r * 0.065f, x = 0.115f * bk + r * 0.005f;
                    for (int k = -2; k <= 2; k++) pb.Box(x, y, (k + (r % 2) * 0.5f - 0.25f) * 0.055f * bk, 0.014f, 0.05f, 0.045f, (k + r) % 2 != 0 ? metal : armorD);
                }
                pb.Box(0.125f * bk, 0.02f, 0, 0.012f, 0.05f, 0.27f * bk, leather);
            }
            else
            {
                // 札甲：横向甲片带 + 护心镜
                foreach (var y in new[] { 0.05f, 0.12f, 0.24f, 0.31f, 0.38f }) pb.Taper(0, 0, y, y + 0.012f, 0.226f * bk + y * 0.06f, 0.3f * bk + y * 0.3f, 0.226f * bk + y * 0.06f, 0.3f * bk + y * 0.3f, robe.HasValue ? Sh(robe.Value, -0.2f) : armorD);
                pb.At(0.135f * bk, 0.31f, 0, 0, 0, -PI / 2); pb.Prism(0, 0, -0.01f, 0.025f, 0.075f, 0.07f, 8, metal); pb.Prism(0, 0, 0.024f, 0.032f, 0.05f, 0.04f, 8, trim); pb.At();
                if (!robe.HasValue) pb.Box(0.125f * bk, 0.22f, 0, 0.012f, 0.26f, 0.035f, leather);
                if (robe.HasValue)
                {  // 外袍 + 胸甲
                    pb.At(0.125f * bk, 0.32f, 0.1f * bk, 0, 0.0f, 0); pb.Box(0, 0, 0, 0.025f, 0.14f, 0.12f, metal); pb.At();
                }
            }
            pb.Prism(0, 0, 0.48f, 0.58f, 0.058f, 0.052f, 6, skin);
            if (!look.bare && !nan) pb.Taper(0, 0, 0.46f, 0.53f, 0.17f * bk, 0.21f * bk, 0.14f, 0.17f, robe ?? (look.torso == "segm" ? cloth : C("#8a2a20")));
            // 文化饰物：彩绘 / 毛皮领 / 勾玉 / 金饰
            if (look.paint != null)
            {
                Color pc = C(look.paint);
                foreach (var z in new[] { -0.07f, 0.07f }) { pb.At(0.148f * bk, 0.26f, z * bk, 0, 0, 0); pb.Box(0, 0, 0, 0.014f, 0.2f, 0.025f, pc); pb.Box(0, 0.06f, z > 0 ? 0.03f : -0.03f, 0.014f, 0.025f, 0.07f, pc); pb.At(); }
                pb.Box(0.14f * bk, 0.12f, 0, 0.014f, 0.03f, 0.2f * bk, pc);
                foreach (var y in new[] { 0.32f, 0.38f }) pb.Box(0.0f, y, 0.15f * bk, 0.12f, 0.022f, 0.014f, pc);
            }
            if (look.fur) { pb.Taper(0, 0, 0.42f, 0.52f, 0.29f * bk, 0.46f * bk, 0.24f * bk, 0.34f * bk, "#8a6a46"); pb.Blob(-0.02f, 0.48f, 0, 0.16f * bk, 0.06f, 0.24f * bk, "#a08058", 41); }
            if (look.magatama || look.shells)
                for (int i = -2; i <= 2; i++)
                    pb.Blob(0.14f * bk, 0.4f - Mathf.Abs(i) * 0.025f, i * 0.045f, 0.018f, 0.026f, 0.016f, look.shells ? (i % 2 != 0 ? "#d8703a" : "#f4ece0") : i % 2 != 0 ? "#e8dcc0" : "#3aa060", 43 + i);
            if (look.gold) { pb.Taper(0, 0, 0.44f, 0.49f, 0.29f * bk, 0.45f * bk, 0.25f * bk, 0.36f * bk, GOLD); pb.Box(0.135f * bk, 0.38f, 0, 0.014f, 0.06f, 0.1f, GOLD); }
            if (look.bow)
            {   // 箭囊（背弓单独成件，放箭时取到手上）
                pb.At(-0.17f * bk, 0.28f, 0.1f, 0.4f, 0, 0); pb.Box(0, 0, 0, 0.08f, 0.36f, 0.08f, "#6a4a2a"); for (int i = 0; i < 3; i++) pb.Rod(V(0, 0.18f, -0.02f + i * 0.02f), V(0, 0.3f, -0.02f + i * 0.02f), 0.006f, 0.006f, 3, "#e8dcc0"); pb.At();
                var bb = new DuelPB();
                for (int i = 0; i < 6; i++)
                {
                    float a0 = -1.1f + i * 0.37f, a1 = a0 + 0.37f;
                    bb.Rod(V(-0.17f * bk + Cos(a0) * 0.04f, 0.24f + Sin(a0) * 0.42f, -0.04f + Cos(a0) * 0.18f), V(-0.17f * bk + Cos(a1) * 0.04f, 0.24f + Sin(a1) * 0.42f, -0.04f + Cos(a1) * 0.18f), 0.012f, 0.012f, 4, "#5a3a20");
                }
                P["backbow"] = bb;
            }
            P["torso"] = pb;

            // ---- 头（原点在颈根）
            pb = new DuelPB();
            Color face = skin;
            pb.Taper(0.012f, 0, 0.04f, 0.13f, 0.15f, 0.13f, 0.19f, 0.17f, face);
            pb.Taper(0.0f, 0, 0.13f, 0.28f, 0.19f, 0.17f, 0.18f, 0.165f, face);
            pb.Box(0.105f, 0.135f, 0, 0.035f, 0.055f, 0.035f, Sh(face, -0.06f));                   // 鼻
            foreach (var z in new[] { -0.042f, 0.042f })
            {
                pb.Box(0.094f, 0.168f, z, 0.012f, 0.022f, 0.034f, "#1a1414");                      // 眼
                pb.Box(0.097f, 0.195f, z, 0.016f, 0.016f, 0.052f, hair);                           // 眉
                pb.Box(-0.005f, 0.15f, z * 2.25f, 0.045f, 0.06f, 0.022f, Sh(face, -0.04f));          // 耳
            }
            pb.Box(0.096f, 0.08f, 0, 0.012f, 0.012f, 0.055f, "#6a2a22");
            pb.Box(-0.055f, 0.17f, 0, 0.1f, 0.17f, 0.17f, hair);                                   // 脑后发
            if (look.eyepatch) { pb.Box(0.1f, 0.168f, 0.044f, 0.018f, 0.04f, 0.044f, "#101010"); pb.At(0.0f, 0.2f, 0, 0, 0, -0.35f); pb.Box(0, 0, 0, 0.21f, 0.012f, 0.18f, "#101010"); pb.At(); }
            switch (look.beard)
            {
                case "short": pb.Box(0.075f, 0.055f, 0, 0.065f, 0.06f, 0.12f, hair); pb.Box(0.104f, 0.096f, 0, 0.02f, 0.016f, 0.08f, hair); break;
                case "long":
                    pb.Box(0.104f, 0.096f, 0, 0.02f, 0.016f, 0.09f, hair);
                    pb.Taper(0.085f, 0, -0.26f, 0.08f, 0.035f, 0.05f, 0.07f, 0.15f, hair, -0.01f, 0);
                    pb.Box(0.05f, 0.08f, 0, 0.08f, 0.08f, 0.17f, hair);
                    break;
                case "bushy":
                    pb.Blob(0.055f, 0.065f, 0, 0.1f, 0.075f, 0.115f, hair, 21);
                    for (int i = 0; i < 7; i++)
                    {
                        float a = -1.2f + i * 0.4f;
                        pb.Rod(V(0.07f, 0.06f, Sin(a) * 0.07f), V(0.12f + Cos(a) * 0.04f, -0.04f - Cos(a) * 0.03f, Sin(a) * 0.16f), 0.035f, 0.0f, 4, hair);
                    }
                    break;
                case "stubble": pb.Box(0.06f, 0.06f, 0, 0.09f, 0.06f, 0.16f, Sh(face, -0.25f)); break;
            }
            BuildHelm(pb, look, metal, trim, armorD, hair, skin);
            P["head"] = pb;

            // ---- 手臂（sd：+1 右 / −1 左；局部 +z 为外侧）
            foreach (var sd in new[] { 1, -1 })
            {
                pb = new DuelPB();
                bool bareA = look.bareArms || look.bare || nan;
                pb.Taper(0, 0, -LenUpper, 0.02f, 0.085f * bk, 0.09f * bk, 0.105f * bk, 0.11f * bk, bareA ? skin : cloth);
                if (!bareA || look.culture == "han")
                {
                    // 披膊：肩顶 + 外侧垂片
                    pb.Taper(0, sd * 0.02f, -0.03f, 0.07f, 0.17f * bk, 0.16f * bk, 0.12f * bk, 0.11f * bk, look.rattan ? C("#8a6a34") : armor);
                    pb.At(0, -0.01f, sd * 0.07f * bk, sd * -0.32f, 0, 0);
                    pb.Taper(0, 0, -0.19f, 0, 0.18f * bk, 0.026f, 0.17f * bk, 0.026f, look.rattan ? C("#8a6a34") : armor);
                    pb.Box(0, -0.1f, sd * 0.016f, 0.18f * bk, 0.014f, 0.012f, armorD);
                    pb.Box(0, -0.185f, 0, 0.185f * bk, 0.02f, 0.03f, trim);
                    pb.At();
                }
                else if (nan) pb.Blob(0, 0.0f, sd * 0.03f, 0.09f * bk, 0.06f, 0.08f * bk, "#a07a44", 31);
                P[sd > 0 ? "upperR" : "upperL"] = pb;
                pb = new DuelPB();
                pb.Taper(0, 0, -0.245f, 0, 0.08f * bk, 0.08f * bk, 0.09f * bk, 0.09f * bk, bareA ? skin : (look.rattan ? C("#8a6a34") : leather));
                if (!bareA) pb.Taper(0, 0, -0.2f, -0.04f, 0.095f * bk, 0.095f * bk, 0.1f * bk, 0.1f * bk, metal);
                else pb.Taper(0, 0, -0.24f, -0.17f, 0.09f * bk, 0.09f * bk, 0.09f * bk, 0.09f * bk, leather);
                pb.Box(0, -LenFore, 0, 0.095f, 0.095f, 0.09f, skin);
                P[sd > 0 ? "foreR" : "foreL"] = pb;
            }
            // ---- 腿
            foreach (var sd in new[] { 1, -1 })
            {
                pb = new DuelPB();
                pb.Taper(0, 0, -LenThigh, 0.02f, 0.12f * bk, 0.12f * bk, 0.155f * bk, 0.15f * bk, cloth);
                if (!look.bare && !nan && !robe.HasValue)
                {
                    pb.At(0.005f, -0.0f, sd * 0.085f * bk, sd * -0.12f, 0, 0);
                    pb.Taper(0, 0, -0.32f, 0.02f, 0.19f * bk, 0.03f, 0.2f * bk, 0.03f, armor);
                    foreach (var y in new[] { -0.1f, -0.2f }) pb.Box(0, y, sd * 0.017f, 0.2f * bk, 0.014f, 0.01f, armorD);
                    pb.Box(0, -0.315f, 0, 0.2f * bk, 0.02f, 0.034f, trim);
                    pb.At();
                }
                P[sd > 0 ? "thighR" : "thighL"] = pb;
                pb = new DuelPB();
                pb.Taper(0, 0, -0.42f, 0.0f, 0.1f * bk, 0.1f * bk, 0.12f * bk, 0.115f * bk, nan ? skin : leather);
                if (!nan) pb.Taper(0.012f, 0, -0.33f, -0.04f, 0.1f * bk, 0.11f * bk, 0.11f * bk, 0.12f * bk, metal);
                pb.Box(0.05f, -0.02f, 0, 0.06f, 0.08f, 0.1f, nan ? Sh(skin, -0.1f) : metal);
                P[sd > 0 ? "shinR" : "shinL"] = pb;
                pb = new DuelPB();
                pb.Box(0.05f, -0.035f, 0, 0.24f, 0.07f, 0.11f, nan ? Sh(skin, -0.12f) : boot);
                if (!nan) { pb.Box(0.0f, 0.02f, 0, 0.13f, 0.08f, 0.12f, boot); pb.Box(0.175f, -0.02f, 0, 0.04f, 0.05f, 0.09f, Sh(boot, 0.15f)); }
                P[sd > 0 ? "footR" : "footL"] = pb;
            }
            // ---- 披风（两段，分别摆动）
            if (look.plaid)
            {   // 方格斗篷（凯尔特）：赤膊也披
                Color lc = Sh(capeC, 0.35f), dc = Sh(capeC, -0.35f);
                pb = new DuelPB(); pb.Taper(-0.015f, 0, -0.5f, 0.0f, 0.03f, 0.46f * bk, 0.03f, 0.4f * bk, capeC);
                foreach (var z in new[] { -0.12f, 0.0f, 0.12f }) pb.Box(-0.032f, -0.25f, z * bk, 0.012f, 0.5f, 0.03f, lc);
                foreach (var y in new[] { -0.1f, -0.3f }) pb.Box(-0.034f, y, 0, 0.012f, 0.03f, 0.42f * bk, dc);
                P["cape1"] = pb;
                pb = new DuelPB(); pb.Taper(0, 0, -0.42f, 0.0f, 0.025f, 0.5f * bk, 0.03f, 0.46f * bk, Sh(capeC, -0.12f));
                foreach (var z in new[] { -0.12f, 0.0f, 0.12f }) pb.Box(-0.018f, -0.21f, z * bk, 0.012f, 0.42f, 0.03f, lc);
                pb.Box(-0.02f, -0.2f, 0, 0.012f, 0.03f, 0.46f * bk, dc);
                P["cape2"] = pb;
            }
            else if (!nan && !look.bare)
            {
                pb = new DuelPB(); pb.Taper(-0.015f, 0, -0.5f, 0.0f, 0.03f, 0.46f * bk, 0.03f, 0.4f * bk, capeC); P["cape1"] = pb;
                pb = new DuelPB(); pb.Taper(0, 0, -0.42f, 0.0f, 0.025f, 0.5f * bk, 0.03f, 0.46f * bk, Sh(capeC, -0.12f)); P["cape2"] = pb;
            }
            return P;
        }

        // 头盔 / 冠帽 / 发型（写在头部网格里；原点在颈根，头顶约 y = 0.28）
        static void BuildHelm(DuelPB pb, DuelLook look, Color kMetal, Color kTrim, Color kArmorD, Color kHair, Color kSkin)
        {
            Color helmC = look.helmColor != null ? C(look.helmColor) : kMetal;
            System.Action<Color> plume = col =>
            {
                pb.Rod(V(0, 0.42f, 0), V(-0.08f, 0.5f, 0), 0.035f, 0.03f, 5, col);
                pb.Rod(V(-0.08f, 0.5f, 0), V(-0.22f, 0.5f, 0), 0.03f, 0.022f, 5, Sh(col, -0.06f));
                pb.Rod(V(-0.22f, 0.5f, 0), V(-0.34f, 0.42f, 0), 0.022f, 0.004f, 5, Sh(col, -0.12f));
            };
            System.Action<Color, bool> hanHelm = (col, crest) =>
            {
                pb.Taper(0, 0, 0.19f, 0.3f, 0.215f, 0.2f, 0.17f, 0.16f, col);
                pb.Taper(0, 0, 0.3f, 0.37f, 0.17f, 0.16f, 0.07f, 0.07f, Sh(col, 0.06f));
                pb.Taper(0, 0, 0.185f, 0.225f, 0.225f, 0.21f, 0.222f, 0.208f, kTrim);
                pb.Box(0.115f, 0.205f, 0, 0.04f, 0.016f, 0.19f, kTrim);                         // 眉庇
                pb.At(-0.105f, 0.22f, 0, 0, 0, -0.32f); pb.Taper(0, 0, -0.15f, 0, 0.03f, 0.22f, 0.03f, 0.2f, kArmorD); pb.At();   // 顿项
                foreach (var s in new[] { -1, 1 }) { pb.At(0.0f, 0.22f, s * 0.1f, s * 0.25f, 0, 0); pb.Taper(0, 0, -0.13f, 0, 0.13f, 0.025f, 0.15f, 0.025f, kArmorD); pb.At(); }
                pb.Prism(0, 0, 0.36f, 0.45f, 0.014f, 0.008f, 4, kTrim);
                if (crest)
                {
                    pb.Blob(0, 0.44f, 0, 0.05f, 0.03f, 0.05f, RED, 13);
                    for (int i = 0; i < 6; i++) { float a = i / 6f * PI * 2; pb.Rod(V(0, 0.44f, 0), V(Cos(a) * 0.07f - 0.03f, 0.33f, Sin(a) * 0.07f), 0.016f, 0.004f, 3, RED); }
                }
            };
            switch (look.helm)
            {
                case "han": hanHelm(helmC, true); break;
                case "plume": hanHelm(helmC, false); plume(C(look.plume ?? "#f0f0f0")); break;
                case "crown":
                    {
                        hanHelm(C(GOLD), false);
                        // 凤翅：头盔两侧向上后方翻卷的金翅
                        foreach (var s in new[] { -1, 1 }) pb.Slab(new[] { P2(0.04f, 0.26f), P2(0.02f, 0.34f), P2(-0.06f, 0.44f), P2(-0.1f, 0.42f), P2(-0.08f, 0.32f), P2(-0.03f, 0.25f) }, s * 0.105f, s * 0.117f, GOLD, "#e8cc80");
                        pb.Blob(0.0f, 0.4f, 0, 0.035f, 0.03f, 0.035f, RED, 17);
                        break;
                    }
                case "pheasant":
                    {
                        // 吕布：紫金冠 + 两根长雉翎
                        hanHelm(C("#d9a83e"), false);
                        pb.Box(0.1f, 0.3f, 0, 0.03f, 0.08f, 0.08f, RED);
                        foreach (var s in new[] { -1, 1 })
                        {
                            var prev = V(0.03f, 0.36f, s * 0.04f);
                            for (int i = 1; i <= 12; i++)
                            {
                                float t = i / 12f;
                                var q = V(0.03f - 0.62f * t + 0.25f * t * t, 0.36f + 0.95f * t - 0.42f * t * t, s * (0.04f + 0.06f * t));
                                pb.Rod(prev, q, 0.016f * (1 - t * 0.6f), 0.016f * (1 - t * 0.6f), 4, i % 2 != 0 ? "#8a5a2a" : "#e8dcc0");
                                prev = q;
                            }
                        }
                        break;
                    }
                case "lion":
                    {
                        // 马超：狮盔
                        pb.Taper(0, 0, 0.19f, 0.31f, 0.22f, 0.205f, 0.17f, 0.16f, "#e8eaee");
                        pb.Taper(0, 0, 0.31f, 0.37f, 0.17f, 0.16f, 0.08f, 0.08f, "#e8eaee");
                        pb.Box(0.115f, 0.27f, 0, 0.04f, 0.09f, 0.14f, GOLD);
                        pb.Box(0.137f, 0.29f, 0, 0.012f, 0.02f, 0.1f, "#101010");
                        for (int i = 0; i < 9; i++)
                        {
                            float a = -1.3f + i * 0.32f;
                            pb.Rod(V(-0.03f, 0.3f, Sin(a) * 0.08f), V(-0.2f - Cos(a) * 0.05f, 0.2f + Cos(a) * 0.12f, Sin(a) * 0.2f), 0.045f, 0.0f, 4, i % 2 != 0 ? "#f4f4f4" : "#dcdcdc");
                        }
                        pb.Prism(0, 0, 0.37f, 0.43f, 0.014f, 0.008f, 4, GOLD);
                        break;
                    }
                case "scarf":
                    {
                        pb.Taper(0, 0, 0.19f, 0.3f, 0.215f, 0.2f, 0.2f, 0.185f, helmC);
                        pb.Blob(-0.02f, 0.32f, 0, 0.09f, 0.06f, 0.09f, Sh(helmC, 0.05f), 19);
                        pb.Blob(-0.1f, 0.27f, 0, 0.04f, 0.04f, 0.04f, Sh(helmC, -0.1f), 23);
                        foreach (var s in new[] { -1, 1 }) { pb.At(-0.12f, 0.26f, s * 0.035f, 0, 0, -0.45f - s * 0.08f); pb.Box(0, -0.12f, 0, 0.022f, 0.24f, 0.045f, Sh(helmC, -0.15f)); pb.At(); }
                        break;
                    }
                case "guan":
                    pb.Taper(0, 0, 0.2f, 0.4f, 0.2f, 0.185f, 0.17f, 0.13f, "#2a2a3a", -0.03f, 0);
                    pb.Box(-0.02f, 0.37f, 0, 0.18f, 0.02f, 0.15f, "#3a3a50");
                    break;
                case "bald":
                    pb.Taper(0, 0, 0.27f, 0.3f, 0.17f, 0.155f, 0.12f, 0.1f, kSkin);
                    pb.Blob(-0.03f, 0.32f, 0, 0.04f, 0.04f, 0.04f, kHair, 29);
                    break;
                case "topknot":
                    {
                        pb.Taper(0, 0, 0.2f, 0.3f, 0.2f, 0.18f, 0.17f, 0.16f, kHair);
                        pb.Blob(0.0f, 0.35f, 0, 0.06f, 0.07f, 0.06f, kHair, 33);
                        pb.Taper(0, 0, 0.21f, 0.25f, 0.205f, 0.188f, 0.205f, 0.188f, "#b8302a");
                        foreach (var z in new[] { -0.07f, 0, 0.07f }) pb.Rod(V(0.1f, 0.23f, z), V(0.135f, 0.2f, z * 1.1f), 0.012f, 0.0f, 3, "#efe6d0");   // 兽牙
                        var fc = new[] { "#d43a2a", "#e8b030", "#2a7ad4" };
                        for (int i = 0; i < 3; i++) pb.Rod(V(-0.02f, 0.36f, (i - 1) * 0.03f), V(-0.12f - i * 0.04f, 0.62f - i * 0.06f, (i - 1) * 0.08f), 0.02f, 0.004f, 4, fc[i]);
                        break;
                    }
                case "fur":
                    pb.Taper(0, 0, 0.2f, 0.42f, 0.19f, 0.18f, 0.06f, 0.06f, "#7a4a2a");
                    pb.Taper(0, 0, 0.18f, 0.25f, 0.24f, 0.22f, 0.23f, 0.21f, "#c8b090");
                    break;
                case "feather":
                    pb.Taper(0, 0, 0.2f, 0.36f, 0.2f, 0.18f, 0.12f, 0.1f, "#3a2a20");
                    foreach (var s in new[] { -1, 1 }) pb.Rod(V(0, 0.34f, s * 0.04f), V(-0.02f, 0.6f, s * 0.08f), 0.018f, 0.004f, 4, "#f0ead8");
                    break;
                case "mizura":
                    pb.Taper(0, 0, 0.2f, 0.3f, 0.2f, 0.18f, 0.17f, 0.16f, kHair);
                    foreach (var s in new[] { -1, 1 }) pb.Blob(0.0f, 0.1f, s * 0.11f, 0.035f, 0.08f, 0.03f, kHair, 37);
                    pb.Taper(0, 0, 0.22f, 0.25f, 0.205f, 0.188f, 0.205f, 0.188f, "#e8e0cc");
                    break;
                case "galea":
                    // 罗马头盔：铜盔 + 横向红缨 + 护颊 + 护颈
                    pb.Taper(0, 0, 0.19f, 0.32f, 0.22f, 0.2f, 0.16f, 0.15f, "#c8a050");
                    pb.Slab(new[] { P2(0.08f, 0.32f), P2(0.02f, 0.44f), P2(-0.08f, 0.46f), P2(-0.16f, 0.38f), P2(-0.1f, 0.32f) }, -0.02f, 0.02f, RED);
                    pb.Box(0.11f, 0.21f, 0, 0.03f, 0.02f, 0.2f, "#b08a40");
                    foreach (var z in new[] { -0.1f, 0.1f }) pb.Box(0.05f, 0.12f, z, 0.1f, 0.13f, 0.018f, "#c8a050");
                    pb.At(-0.1f, 0.2f, 0, 0, 0, -0.5f); pb.Box(0, -0.04f, 0, 0.02f, 0.1f, 0.24f, "#b08a40"); pb.At();
                    break;
                case "tiara":
                    // 安息提亚拉冠：高圆冠 + 金箍 + 护颈垂巾；冠下露出卷发
                    pb.Taper(0, 0, 0.19f, 0.42f, 0.21f, 0.19f, 0.12f, 0.12f, "#e0d8c8", 0.03f, 0);
                    pb.Taper(0, 0, 0.19f, 0.23f, 0.22f, 0.2f, 0.22f, 0.2f, GOLD);
                    for (int i = 0; i < 4; i++) pb.Blob(0.0f, 0.3f + i * 0.03f, 0, 0.012f, 0.012f, 0.012f, i % 2 != 0 ? RED : GOLD, 51 + i);
                    pb.At(-0.1f, 0.2f, 0, 0, 0, -0.25f); pb.Taper(0, 0, -0.16f, 0, 0.03f, 0.24f, 0.03f, 0.2f, "#d8ccb0"); pb.At();
                    foreach (var s in new[] { -1, 1 }) pb.Blob(-0.02f, 0.15f, s * 0.1f, 0.05f, 0.07f, 0.035f, kHair, 53);
                    break;
                case "yifeather":
                    {
                        // 夷洲：长发 + 编织头带插一圈羽毛
                        pb.Taper(0, 0, 0.19f, 0.3f, 0.205f, 0.19f, 0.18f, 0.17f, kHair);
                        pb.Box(-0.09f, 0.08f, 0, 0.08f, 0.26f, 0.17f, kHair);
                        pb.Taper(0, 0, 0.21f, 0.25f, 0.212f, 0.196f, 0.212f, 0.196f, "#d8b060");
                        for (int i = 0; i < 7; i++)
                        {
                            float a = -1.25f + i * 0.42f;
                            pb.Rod(V(-0.02f, 0.26f, Sin(a) * 0.1f), V(-0.06f - Cos(a) * 0.02f, 0.5f + Cos(a) * 0.06f, Sin(a) * 0.2f), 0.022f, 0.004f, 4, i % 2 != 0 ? "#f4f0e8" : "#1a1a1a");
                        }
                        break;
                    }
                case "goldcrown":
                    // 林邑 / 扶南：尖塔形金高冠 + 耳饰
                    pb.Taper(0, 0, 0.2f, 0.28f, 0.2f, 0.18f, 0.18f, 0.165f, GOLD);
                    pb.Taper(0, 0, 0.28f, 0.52f, 0.16f, 0.15f, 0.02f, 0.02f, "#e8c060");
                    for (int i = 0; i < 3; i++) pb.Taper(0, 0, 0.32f + i * 0.06f, 0.34f + i * 0.06f, 0.15f - i * 0.04f, 0.14f - i * 0.04f, 0.14f - i * 0.04f, 0.13f - i * 0.04f, Sh(C(GOLD), -0.2f));
                    foreach (var z in new[] { -0.11f, 0.11f }) pb.Blob(0.0f, 0.1f, z, 0.02f, 0.03f, 0.02f, GOLD, 57);
                    break;
                case "hutmao":
                    // 西域胡帽：翻檐尖顶毡帽
                    pb.Taper(0, 0, 0.19f, 0.25f, 0.235f, 0.22f, 0.225f, 0.21f, "#e8dcc0");
                    pb.Taper(0.0f, 0, 0.24f, 0.5f, 0.19f, 0.18f, 0.03f, 0.03f, look.helmColor == null ? C("#a0383a") : helmC, -0.05f, 0);
                    pb.Box(0.11f, 0.23f, 0, 0.03f, 0.08f, 0.12f, "#e8dcc0");
                    break;
                case "pointed":
                    // 贵霜 / 康居：高尖帽（顶向后弯）+ 金箍
                    pb.Taper(0, 0, 0.19f, 0.24f, 0.215f, 0.2f, 0.21f, 0.195f, GOLD);
                    pb.Taper(0, 0, 0.24f, 0.44f, 0.2f, 0.19f, 0.08f, 0.08f, "#e8d8b0", -0.03f, 0);
                    pb.Rod(V(-0.03f, 0.44f, 0), V(-0.12f, 0.6f, 0), 0.04f, 0.012f, 5, "#e8d8b0");
                    break;
                case "kufiya":
                    // 阿拉伯头巾：裹头 + 垂到肩后的头巾 + 黑色头箍
                    pb.Taper(0, 0, 0.19f, 0.31f, 0.225f, 0.21f, 0.2f, 0.19f, helmC);
                    pb.Blob(-0.01f, 0.31f, 0, 0.1f, 0.05f, 0.1f, helmC, 59);
                    pb.Taper(0, 0, 0.24f, 0.27f, 0.232f, 0.216f, 0.232f, 0.216f, "#1a1414");
                    pb.At(-0.11f, 0.26f, 0, 0, 0, -0.18f); pb.Taper(0, 0, -0.3f, 0, 0.03f, 0.24f, 0.03f, 0.2f, Sh(helmC, -0.08f)); pb.At();
                    foreach (var sd in new[] { -1, 1 }) { pb.At(-0.02f, 0.24f, sd * 0.11f, sd * 0.1f, 0, 0); pb.Box(0, -0.12f, 0, 0.12f, 0.24f, 0.02f, Sh(helmC, -0.05f)); pb.At(); }
                    break;
                case "celtic":
                    // 凯尔特：石灰硬化的蓬乱长发，向后刺出
                    pb.Taper(0, 0, 0.19f, 0.3f, 0.205f, 0.19f, 0.18f, 0.17f, kHair);
                    for (int i = 0; i < 7; i++)
                    {
                        float a = -1.2f + i * 0.4f;
                        pb.Rod(V(-0.02f, 0.28f, Sin(a) * 0.06f), V(-0.2f - Cos(a) * 0.04f, 0.4f + Cos(a) * 0.06f, Sin(a) * 0.16f), 0.04f, 0.0f, 4, Sh(kHair, 0.15f * (i % 2)));
                    }
                    pb.Box(-0.09f, 0.06f, 0, 0.08f, 0.26f, 0.17f, kHair);   // 披肩长发
                    break;
                case "suebian":
                    // 苏维汇发髻：长发向一侧束成结
                    pb.Taper(0, 0, 0.19f, 0.31f, 0.21f, 0.195f, 0.17f, 0.16f, kHair);
                    pb.Blob(-0.02f, 0.3f, 0.12f, 0.06f, 0.06f, 0.05f, kHair, 61);
                    pb.Blob(-0.03f, 0.34f, 0.15f, 0.035f, 0.05f, 0.03f, Sh(kHair, -0.15f), 63);
                    pb.Box(-0.09f, 0.08f, 0, 0.08f, 0.24f, 0.17f, kHair);
                    break;
                case "conical":
                    // 萨尔马提亚尖盔：分片铆合尖顶盔 + 护鼻 + 锁子护颈
                    pb.Taper(0, 0, 0.19f, 0.3f, 0.22f, 0.205f, 0.17f, 0.16f, kMetal);
                    pb.Taper(0, 0, 0.3f, 0.5f, 0.17f, 0.16f, 0.0f, 0.0f, Sh(kMetal, 0.06f));
                    foreach (var z in new[] { -0.06f, 0.06f }) pb.Rod(V(0.06f, 0.2f, z), V(0.0f, 0.48f, 0), 0.012f, 0.006f, 3, kTrim);
                    pb.Box(0.112f, 0.15f, 0, 0.012f, 0.11f, 0.022f, kMetal);
                    pb.At(-0.1f, 0.2f, 0, 0, 0, -0.2f); pb.Taper(0, 0, -0.17f, 0, 0.03f, 0.24f, 0.03f, 0.22f, Sh(kMetal, -0.25f)); pb.At();
                    break;
                default: hanHelm(helmC, true); break;
            }
        }
    }
}
