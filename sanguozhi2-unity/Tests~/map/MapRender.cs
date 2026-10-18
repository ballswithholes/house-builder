// 战略地图的软件光栅器（无头，对照网页版截图用）：构建 C# 的 MapView（世界剧本），按网页版截图时导出的镜头参数（cams.json）
// 把地形块、水面、海床、道路、桥、关隘、城池（近景 / 远景）、旗帜、势力光圈、海路，以及当帧提交的实例化绘制（树木近景 / 特写 / 公告板、
// 石块、灌丛、村舍、草丛与野花）画成 PNG。
//   z 缓冲、近平面裁剪、背面剔除（Unity 约定：屏幕上顺时针为正面——C# 网格的绕序若与 Unity 相反，这里会直接缺面）；
//   着色：地形按 Sanguo/Terrain 着色器（数据纹理 + 细节纹理，逐像素）在 CPU 上逐行移植，水面按 Sanguo/MapWater，
//   其余为顶点色（× 实例色 / 材质色）+ 半兰伯特（(n·l·0.5+0.5)²·1.15）+ 三色环境光；雾为线性雾。
//   光照常量取网页版 art.js（太阳色 × 0.8625、环境 × 0.56），以便与截图直接比较（Unity 工程里的灯光强度见报告）。
//   不画：阴影、云（战略视角下隐去）、选择光圈。
// 用法：mono maprender.exe <参考值目录> <cams.json> <输出目录> [宽 高]（由 Tests~/map/render.sh 调用；环境变量 CLOUDS=1 时打印云的位置）
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Sanguo;
using UnityEngine;

static class MapRender
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static double P(string s) { return double.Parse(s, Inv); }

    // ------------------------------------------------------------ 光照常量（网页版 art.js）
    static readonly Vector3 SunL = SunDir();
    static Vector3 SunDir() { double p = 48 * Math.PI / 180, y = -35 * Math.PI / 180; return -new Vector3((float)(Math.Sin(y) * Math.Cos(p)), (float)-Math.Sin(p), (float)(Math.Cos(y) * Math.Cos(p))); }
    static readonly Vector3 SunC = new Vector3(1f, 0.94f, 0.84f) * (1.15f * 0.75f);
    static readonly Vector3 AmbSky = new Vector3(0.62f, 0.68f, 0.8f) * 0.56f, AmbEq = new Vector3(0.58f, 0.56f, 0.5f) * 0.56f, AmbGround = new Vector3(0.32f, 0.3f, 0.26f) * 0.56f;
    static readonly Vector3 FogC = new Vector3(0.86f, 0.84f, 0.78f);
    static float fogNear = 90, fogFar = 320;

    static Vector3 Amb(Vector3 n) { float d = n.y; return d >= 0 ? Vector3.Lerp(AmbEq, AmbSky, d) : Vector3.Lerp(AmbEq, AmbGround, -d); }
    static Vector3 Lit(Vector3 albedo, Vector3 n)
    {
        float hl = Vector3.Dot(n, SunL) * 0.5f + 0.5f; float ndl = hl * hl * 1.15f;
        var a = Amb(n);
        return new Vector3(albedo.x * (SunC.x * ndl + a.x), albedo.y * (SunC.y * ndl + a.y), albedo.z * (SunC.z * ndl + a.z));
    }
    static Vector3 Mul(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
    static Vector3 V3(Color c) { return new Vector3(c.r, c.g, c.b); }
    static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static float SS(float a, float b, float x) { float t = Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    static Vector3 LerpV(Vector3 a, Vector3 b, float t) { return a + (b - a) * t; }
    static float Frac(float v) { return v - (float)Math.Floor(v); }

    // ------------------------------------------------------------ 纹理采样（双线性；v = 0 为纹理第 0 行）
    sealed class Tex
    {
        public int w, h; public float[] d; public bool repeat;
        public Tex(Texture2D t, bool repeat)
        {
            w = t.width; h = t.height; this.repeat = repeat; d = new float[w * h * 4];
            for (int i = 0; i < w * h; i++) { var c = t.pixels[i]; d[i * 4] = c.r / 255f; d[i * 4 + 1] = c.g / 255f; d[i * 4 + 2] = c.b / 255f; d[i * 4 + 3] = c.a / 255f; }
        }
        public Vector4 S(float u, float v)
        {
            float x = u * w - 0.5f, y = v * h - 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y); float fx = x - x0, fy = y - y0;
            int xa = X(x0), xb = X(x0 + 1), ya = Y(y0), yb = Y(y0 + 1);
            int a = (ya * w + xa) * 4, b = (ya * w + xb) * 4, c = (yb * w + xa) * 4, e = (yb * w + xb) * 4;
            float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            return new Vector4(d[a] * w00 + d[b] * w10 + d[c] * w01 + d[e] * w11, d[a + 1] * w00 + d[b + 1] * w10 + d[c + 1] * w01 + d[e + 1] * w11,
                d[a + 2] * w00 + d[b + 2] * w10 + d[c + 2] * w01 + d[e + 2] * w11, d[a + 3] * w00 + d[b + 3] * w10 + d[c + 3] * w01 + d[e + 3] * w11);
        }
        int X(int i) { return repeat ? ((i % w) + w) % w : (i < 0 ? 0 : i >= w ? w - 1 : i); }
        int Y(int i) { return repeat ? ((i % h) + h) % h : (i < 0 ? 0 : i >= h ? h - 1 : i); }
    }
    static readonly Dictionary<Texture, Tex> texCache = new Dictionary<Texture, Tex>();
    static Tex T(Texture t, bool repeat) { Tex r; if (!texCache.TryGetValue(t, out r)) texCache[t] = r = new Tex((Texture2D)t, repeat); return r; }
    static Vector4 Vec(Material m, string n) { object o; return m.props.TryGetValue(n, out o) && o is Vector4 ? (Vector4)o : Vector4.zero; }
    static Texture TexP(Material m, string n) { object o; return m.props.TryGetValue(n, out o) ? o as Texture : null; }

    // ------------------------------------------------------------ 帧缓冲与镜头
    static int W = 960, H = 540;
    static float[] col, dep;
    static Vector3 camPos, camF, camR, camU; static float tanV, tanH, camNear = 0.5f, camFar = 1500;

    // 片元：世界坐标、法线、顶点色、uv、uv2
    struct Frag { public Vector3 p, n, lp; public Vector4 c; public Vector2 uv, uv2; public float depth; }
    delegate bool Shade(ref Frag f, out Vector3 rgb, out float a);

    // 一个顶点的全部属性（裁剪时插值）
    struct Vtx { public Vector3 p, n, cp, lp; public Vector4 c; public Vector2 uv, uv2; }
    static Vtx LerpVtx(Vtx a, Vtx b, float t)
    {
        return new Vtx { p = a.p + (b.p - a.p) * t, n = a.n + (b.n - a.n) * t, cp = a.cp + (b.cp - a.cp) * t, lp = a.lp + (b.lp - a.lp) * t, c = new Vector4(Lerp(a.c.x, b.c.x, t), Lerp(a.c.y, b.c.y, t), Lerp(a.c.z, b.c.z, t), Lerp(a.c.w, b.c.w, t)), uv = a.uv + (b.uv - a.uv) * t, uv2 = a.uv2 + (b.uv2 - a.uv2) * t };
    }
    static Vector3 ToCam(Vector3 p) { var d = p - camPos; return new Vector3(Vector3.Dot(d, camR), Vector3.Dot(d, camU), Vector3.Dot(d, camF)); }

    static long trisDrawn, trisCulled;
    // 画一个三角形：cull = 背面剔除（Unity：屏幕上顺时针为正面）；blend = 透明（不写深度）
    static void Tri(Vtx a, Vtx b, Vtx c, bool cull, bool blend, Shade shade)
    {
        a.cp = ToCam(a.p); b.cp = ToCam(b.p); c.cp = ToCam(c.p);
        if (a.cp.z > camFar && b.cp.z > camFar && c.cp.z > camFar) return;
        // 近平面裁剪
        var poly = new List<Vtx>(4) { a, b, c };
        var outp = new List<Vtx>(5);
        for (int i = 0; i < poly.Count; i++)
        {
            var s = poly[i]; var e = poly[(i + 1) % poly.Count];
            bool si = s.cp.z >= camNear, ei = e.cp.z >= camNear;
            if (si) outp.Add(s);
            if (si != ei) outp.Add(LerpVtx(s, e, (camNear - s.cp.z) / (e.cp.z - s.cp.z)));
        }
        if (outp.Count < 3) return;
        for (int i = 1; i + 1 < outp.Count; i++) Raster(outp[0], outp[i], outp[i + 1], cull, blend, shade);
    }
    static void Raster(Vtx a, Vtx b, Vtx c, bool cull, bool blend, Shade shade)
    {
        // NDC（y 向上）→ 屏幕（y 向下）
        float ax = a.cp.x / (a.cp.z * tanH), ay = a.cp.y / (a.cp.z * tanV), bx = b.cp.x / (b.cp.z * tanH), by = b.cp.y / (b.cp.z * tanV), cx = c.cp.x / (c.cp.z * tanH), cy = c.cp.y / (c.cp.z * tanV);
        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (cull && area >= 0) { trisCulled++; return; }   // NDC 中逆时针（面积 > 0）= 背面
        float X0 = (ax * 0.5f + 0.5f) * W, Y0 = (0.5f - ay * 0.5f) * H, X1 = (bx * 0.5f + 0.5f) * W, Y1 = (0.5f - by * 0.5f) * H, X2 = (cx * 0.5f + 0.5f) * W, Y2 = (0.5f - cy * 0.5f) * H;
        float A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
        if (Math.Abs(A) < 1e-9f) return;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(X0, Math.Min(X1, X2)))), maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(X0, Math.Max(X1, X2))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(Y0, Math.Min(Y1, Y2)))), maxY = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(Y0, Math.Max(Y1, Y2))));
        if (minX > maxX || minY > maxY) return;
        trisDrawn++;
        float iz0 = 1 / a.cp.z, iz1 = 1 / b.cp.z, iz2 = 1 / c.cp.z;
        for (int py = minY; py <= maxY; py++)
        {
            float sy = py + 0.5f;
            for (int px = minX; px <= maxX; px++)
            {
                float sx = px + 0.5f;
                float w0 = ((X1 - sx) * (Y2 - sy) - (Y1 - sy) * (X2 - sx)) / A;
                float w1 = ((X2 - sx) * (Y0 - sy) - (Y2 - sy) * (X0 - sx)) / A;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float iz = w0 * iz0 + w1 * iz1 + w2 * iz2, z = 1 / iz;
                int o = py * W + px;
                if (z >= dep[o]) continue;
                float k0 = w0 * iz0 * z, k1 = w1 * iz1 * z, k2 = w2 * iz2 * z;
                var f = new Frag
                {
                    p = a.p * k0 + b.p * k1 + c.p * k2,
                    n = (a.n * k0 + b.n * k1 + c.n * k2).normalized, lp = a.lp * k0 + b.lp * k1 + c.lp * k2,
                    c = new Vector4(a.c.x * k0 + b.c.x * k1 + c.c.x * k2, a.c.y * k0 + b.c.y * k1 + c.c.y * k2, a.c.z * k0 + b.c.z * k1 + c.c.z * k2, a.c.w * k0 + b.c.w * k1 + c.c.w * k2),
                    uv = a.uv * k0 + b.uv * k1 + c.uv * k2, uv2 = a.uv2 * k0 + b.uv2 * k1 + c.uv2 * k2, depth = z,
                };
                Vector3 rgb; float al;
                if (!shade(ref f, out rgb, out al)) continue;
                // 线性雾（按视深）
                float fg = Clamp01((z - fogNear) / (fogFar - fogNear));
                rgb = LerpV(rgb, FogC, fg);
                if (blend)
                {
                    col[o * 3] = Lerp(col[o * 3], rgb.x, al); col[o * 3 + 1] = Lerp(col[o * 3 + 1], rgb.y, al); col[o * 3 + 2] = Lerp(col[o * 3 + 2], rgb.z, al);
                }
                else { col[o * 3] = rgb.x; col[o * 3 + 1] = rgb.y; col[o * 3 + 2] = rgb.z; dep[o] = z; }
            }
        }
    }

    // 网格（世界矩阵 M）；inst = 实例色（Foliage：a = 1 的顶点乘实例色）
    static void DrawMesh(Mesh m, Matrix4x4 M, bool cull, bool blend, Shade shade, Vector4? inst = null)
    {
        var V = m.vertices; var N = m.normals; var C = m.colors; var U = m.uv; var U2 = m.uv2; var t = m.triangles;
        bool hasN = N.Length == V.Length, hasC = C.Length == V.Length, hasU = U.Length == V.Length, hasU2 = U2.Length == V.Length;
        var vs = new Vtx[V.Length];
        for (int i = 0; i < V.Length; i++)
        {
            var c = hasC ? new Vector4(C[i].r, C[i].g, C[i].b, C[i].a) : new Vector4(1, 1, 1, 1);
            if (inst.HasValue) { var k = inst.Value; float l = c.w; c = new Vector4(c.x * Lerp(1, k.x, l), c.y * Lerp(1, k.y, l), c.z * Lerp(1, k.z, l), c.w); }
            // lp：Foliage 的叶簇噪声坐标（three 坐标：模型坐标 + 实例位置 × 0.37）
            var lp = new Vector3(V[i].x + M.m03 * 0.37f, V[i].y + M.m13 * 0.37f, -V[i].z - M.m23 * 0.37f);
            vs[i] = new Vtx { p = M.MultiplyPoint3x4(V[i]), n = hasN ? M.MultiplyVector(N[i]).normalized : Vector3.up, lp = lp, c = c, uv = hasU ? U[i] : Vector2.zero, uv2 = hasU2 ? U2[i] : Vector2.zero };
        }
        for (int i = 0; i + 2 < t.Length; i += 3) Tri(vs[t[i]], vs[t[i + 1]], vs[t[i + 2]], cull, blend, shade);
    }

    // ------------------------------------------------------------ 着色器
    static Shade LowPolyShade(Vector3 tint)
    {
        return (ref Frag f, out Vector3 rgb, out float a) => { rgb = Lit(Mul(new Vector3(f.c.x, f.c.y, f.c.z), tint), f.n); a = 1; return true; };
    }
    // Sanguo/Foliage：叶簇明暗 + 视空间法线起伏（_Kind 1 = 树冠、2 = 石块），只作用于 sgLeaf = 1（顶点色 a）的部分
    static float LN(Vector3 p) { return (float)(Math.Sin(p.x + Math.Sin(p.y * 1.3) * 1.2) * Math.Sin(p.y * 1.1 + Math.Sin(p.z * 1.2) * 1.2) * Math.Sin(p.z * 0.9 + Math.Sin(p.x * 1.4) * 1.2)); }
    static Shade FoliageShade(Material mat)
    {
        float kind = mat.GetFloat("_Kind"), F = mat.GetFloat("_LeafF"), NK = mat.GetFloat("_LeafN");
        return (ref Frag f, out Vector3 rgb, out float a) =>
        {
            var c = new Vector3(f.c.x, f.c.y, f.c.z); var wn = f.n; float leaf = f.c.w;
            if (kind > 0.5f)
            {
                var q = f.lp * F;
                float l1 = LN(q), l2 = LN(q * 2.63f + new Vector3(5.3f, 5.3f, 5.3f)), l3 = LN(q * 6.1f + new Vector3(1.7f, 1.7f, 1.7f));
                if (kind < 1.5f) { float lc = l1 * 0.5f + l2 * 0.32f + l3 * 0.18f; c *= Lerp(1, 0.84f + 0.34f * lc + 0.16f * SS(0.35f, 0.85f, l2 * 0.6f + l3 * 0.4f), leaf); }
                else { float lc = l1 * 0.25f + l2 * 0.35f + l3 * 0.4f; c *= Lerp(1, 0.86f + 0.22f * lc + 0.1f * (float)Math.Sin(f.lp.y * 41 + l1 * 2), leaf); }
                var vn = new Vector3(Vector3.Dot(wn, camR), Vector3.Dot(wn, camU), -Vector3.Dot(wn, camF));
                vn = (vn + new Vector3(l2 + l3 * 0.5f, l1 * 0.6f, l3 - l1 * 0.4f) * (leaf * NK)).normalized;
                wn = (camR * vn.x + camU * vn.y - camF * vn.z).normalized;
            }
            rgb = Lit(c, wn); a = 1; return true;
        };
    }
    static Shade UnlitShade(Material mat)
    {
        var tex = mat.mainTexture != null ? T(mat.mainTexture, false) : null; var mc = mat.color;
        return (ref Frag f, out Vector3 rgb, out float a) =>
        {
            var t = tex != null ? tex.S(f.uv.x, f.uv.y) : new Vector4(1, 1, 1, 1);
            rgb = new Vector3(t.x * f.c.x * mc.r, t.y * f.c.y * mc.g, t.z * f.c.z * mc.b); a = t.w * f.c.w * mc.a; return a > 0.003f;
        };
    }
    static Shade RoadShade(Material mat)
    {
        var tex = T(mat.mainTexture, true);
        return (ref Frag f, out Vector3 rgb, out float a) =>
        {
            var t = tex.S(f.uv.x, f.uv.y);
            rgb = Lit(new Vector3(t.x * f.c.x, t.y * f.c.y, t.z * f.c.z), f.n); a = t.w; return a > 0.003f;
        };
    }
    // Sanguo/Terrain（SG_TERRAIN_MAPS）的逐行移植
    static Shade TerrainShade(Material mat)
    {
        var detA = T(TexP(mat, "_DetA"), true); var detB = T(TexP(mat, "_DetB"), true);
        var mapN = T(TexP(mat, "_MapN"), false); var mapC = T(TexP(mat, "_MapC"), false); var mapM = T(TexP(mat, "_MapM"), false);
        var outC = T(TexP(mat, "_OutC"), false); var outM = T(TexP(mat, "_OutM"), false);
        Vector4 xfN = Vec(mat, "_XfN"), xfC = Vec(mat, "_XfC"), xfO = Vec(mat, "_XfO"), inner = Vec(mat, "_Inner");
        Func<float, Vector3> crop = id =>
        {
            var c = new Vector3(0.42f, 0.57f, 0.23f);
            if (id >= 0.24f) c = new Vector3(0.75f, 0.66f, 0.31f);
            if (id >= 0.42f) c = new Vector3(0.53f, 0.41f, 0.28f);
            if (id >= 0.56f) c = new Vector3(0.33f, 0.49f, 0.2f);
            if (id >= 0.72f) c = new Vector3(0.64f, 0.62f, 0.3f);
            if (id >= 0.87f) c = new Vector3(0.47f, 0.6f, 0.26f);
            return c;
        };
        return (ref Frag f, out Vector3 rgb, out float alpha) =>
        {
            var W3 = new Vector3(f.p.x, f.p.y, -f.p.z);
            float spx = f.p.x, spz = f.p.z, sgH = W3.y;
            var sgN = new Vector3(f.n.x, f.n.y, -f.n.z).normalized;
            float ex = Math.Min(spx - inner.x, inner.z - spx), ez = Math.Min(spz - inner.y, inner.w - spz);
            float wI = Clamp01(Math.Min(ex, ez) * 0.2f);
            var oc = outC.S(spx * xfO.z + xfO.x, spz * xfO.w + xfO.y); var om = outM.S(spx * xfO.z + xfO.x, spz * xfO.w + xfO.y);
            float uC = spx * xfC.z + xfC.x, vC = spz * xfC.w + xfC.y;
            var ic = mapC.S(uC, vC); var im = mapM.S(uC, vC);
            float uN = spx * xfN.z + xfN.x, vN = spz * xfN.w + xfN.y;
            var tn = mapN.S(uN, vN);
            float d1 = 1.1f * xfN.z, d2 = 1.1f * xfN.w;
            float a1 = Math.Min(Math.Min(mapN.S(uN + d1, vN).w, mapN.S(uN - d1, vN).w), Math.Min(mapN.S(uN, vN + d2).w, mapN.S(uN, vN - d2).w));
            float a2 = Math.Min(Math.Min(mapN.S(uN + d1 * 0.5f, vN).w, mapN.S(uN - d1 * 0.5f, vN).w), Math.Min(mapN.S(uN, vN + d2 * 0.5f).w, mapN.S(uN, vN - d2 * 0.5f).w));
            var sgTint = new Vector3(oc.x, oc.y, oc.z) * (1 - wI);
            var sgMat = om * (1 - wI);
            var n2 = new Vector3(tn.x * 2 - 1, 0, tn.y * 2 - 1);
            n2.y = (float)Math.Sqrt(Math.Max(0, 1 - n2.x * n2.x - n2.z * n2.z));
            sgTint += new Vector3(ic.x, ic.y, ic.z) * wI;
            sgMat = sgMat + im * wI;
            float sgForest = ic.w * wI;
            float sgAO = Lerp(0.5f, tn.z, wI);
            if (wI > 0) sgN = LerpV(sgN, n2, wI).normalized;
            float hmin = Math.Min(a1, a2) * 8 - 4;
            float sgNearW = (wI > 0 && sgH < 0.6f) ? Lerp(1, 1 - SS(0.03f, 0.2f, hmin), wI) : 1;
            var sgDA = detA.S(spx * 0.23f, spz * 0.23f);
            var sgDM = detA.S(spx * 0.047f + 0.31f, spz * 0.047f + 0.67f);
            var sgDF = detA.S(spx * 0.93f + 0.53f, spz * 0.93f + 0.19f);
            var sgDN = detA.S(spx * 3.1f + 0.7f, spz * 3.1f + 0.2f);
            var sgDB = detB.S(spx * 0.11f + 0.13f, spz * 0.11f + 0.71f);
            float sgSlope = 1 - sgN.y;
            float sgNear = 1 - SS(18, 40, (f.p - camPos).magnitude);
            var sgCol = sgTint * (0.76f + 0.5f * (sgDA.x * 0.36f + sgDF.x * 0.28f + sgDM.x * 0.36f));
            if (sgNear > 0.01f) sgCol *= 1 + (sgDN.x - 0.5f) * 0.24f * sgNear;
            float lum = sgCol.x * 0.3f + sgCol.y * 0.59f + sgCol.z * 0.11f;
            sgCol = Mul(LerpV(new Vector3(lum, lum, lum), sgCol, 0.9f), LerpV(new Vector3(1.035f, 1, 0.93f), new Vector3(0.96f, 1, 1.04f), sgDM.z));
            sgCol = LerpV(sgCol, Mul(sgCol, new Vector3(1.1f, 1.03f, 0.8f)), SS(0.5f, 0.78f, sgDM.y) * 0.55f);
            sgCol = LerpV(sgCol, Mul(sgCol, new Vector3(0.78f, 0.86f, 0.74f)), SS(0.55f, 0.85f, sgDM.w) * 0.6f);
            sgCol *= 1 - 0.3f * sgForest;
            float sgDry = Clamp01((sgTint.x - sgTint.y * 0.85f) * 4 + 0.3f);
            float sgDirtW = SS(0.66f, 0.9f, sgDM.w * 0.62f + sgSlope * 0.95f + sgDry * 0.2f + (sgDA.y - 0.5f) * 0.2f);
            var sgDirt = LerpV(new Vector3(0.48f, 0.39f, 0.28f), Mul(sgTint, new Vector3(0.95f, 0.85f, 0.7f)), 0.35f) * (0.78f + 0.44f * sgDA.y);
            sgCol = LerpV(sgCol, sgDirt, sgDirtW * 0.85f);
            float sgFarm = 1 - sgMat.w, sgFw = 0;
            if (sgFarm > 0.02f)
            {
                var c3 = crop(sgDB.z) * (0.8f + 0.34f * sgDB.w);
                if (sgDB.z <= 0.03f) c3 = new Vector3(0.58f, 0.5f, 0.36f);
                c3 = LerpV(c3, sgTint, 0.15f);
                if (sgNear > 0.01f)
                {
                    float rx = (float)Math.Cos(sgDB.z * 6.2832), rz = (float)Math.Sin(sgDB.z * 6.2832);
                    float dd = W3.x * rx + (-W3.z) * rz;
                    c3 *= Lerp(1, 0.86f + 0.14f * (float)Math.Sin(dd * 38), sgNear * (sgDB.z >= 0.03f ? 1 : 0));
                }
                sgFw = SS(0.3f, 0.52f, sgFarm + (sgDM.x - 0.5f) * 0.55f) * (1 - SS(0.1f, 0.26f, sgSlope));
                sgCol = LerpV(sgCol, c3, sgFw);
            }
            float sgBeach = (1 - SS(0.2f, 0.48f, sgH + (sgDA.z - 0.5f) * 0.14f)) * Math.Max(sgNearW, 1 - SS(-0.02f, 0.05f, sgH));
            float sgRip = Lerp(0.08f, 0.26f, SS(0.3f, 0.8f, sgMat.x));
            var sgSand = LerpV(new Vector3(0.87f, 0.8f, 0.62f), sgTint * 1.05f, Math.Max(0, Math.Min(0.6f, sgMat.x * 0.7f))) * (1 - sgRip * 0.55f + sgRip * sgDA.z + (sgDF.x - 0.5f) * 0.08f);
            sgSand *= Lerp(0.8f, 1, SS(0, 0.22f, sgH));
            sgCol = LerpV(sgCol, sgSand, Math.Max(sgBeach, SS(0.2f, 0.7f, sgMat.x + (sgDM.w - 0.5f) * 0.25f) * (1 - sgFw)));
            float sgRockW = SS(0.4f, 0.62f, sgSlope + (sgDM.w - 0.5f) * 0.22f - sgMat.y * 0.12f);
            sgRockW = Math.Max(sgRockW, SS(4.4f, 6.6f, sgH + (sgDM.x - 0.5f) * 1.6f + sgMat.y * 1.0f));
            var bw = Mul(sgN, sgN); bw = Mul(bw, bw); bw /= (bw.x + bw.y + bw.z);
            float sgR = detB.S(W3.z * 0.19f, W3.y * 0.19f).x * bw.x + detB.S(W3.x * 0.19f, W3.z * 0.19f).x * bw.y + detB.S(W3.x * 0.19f, W3.y * 0.19f).x * bw.z;
            if (sgRockW > 0.004f)
            {
                var rockC = LerpV(new Vector3(0.46f, 0.44f, 0.41f), new Vector3(0.56f, 0.47f, 0.37f), Clamp01(sgMat.y)) * (0.55f + 0.75f * sgR);
                sgCol = LerpV(sgCol, rockC, sgRockW);
                sgAO += (sgR - 0.5f) * 0.25f * sgRockW;
            }
            float sgSnowW = SS(0.3f, 0.62f, sgMat.z + (sgDA.w - 0.5f) * 0.5f + (sgDM.w - 0.5f) * 0.3f - sgSlope * 0.7f);
            sgCol = LerpV(sgCol, new Vector3(0.8f, 0.83f, 0.88f) * (0.88f + 0.16f * sgDA.w), sgSnowW);
            sgCol = LerpV(sgCol, new Vector3(0.6f, 0.57f, 0.45f) * (0.85f + 0.2f * sgDA.z), SS(-0.05f, -0.7f, sgH) * 0.75f);
            sgCol *= 0.74f + 0.52f * Clamp01(sgAO);
            var wn = new Vector3(sgN.x, sgN.y, -sgN.z);
            var c = Lit(sgCol, wn);
            // 高光肩部
            float peak = Math.Max(c.x, Math.Max(c.y, c.z));
            if (peak > 0.8f) c *= (0.8f + 0.2f * (1 - (float)Math.Exp((0.8f - peak) / 0.2f))) / peak;
            rgb = c; alpha = 1;
            return true;
        };
    }
    // Sanguo/MapWater（时间取 0）
    static Shade WaterShade(Material mat)
    {
        var rip = T(TexP(mat, "_Ripple"), true); var mapN = T(TexP(mat, "_MapN"), false); var outC = T(TexP(mat, "_OutC"), false);
        Vector4 xfN = Vec(mat, "_XfN"), xfO = Vec(mat, "_XfO"), inner = Vec(mat, "_Inner");
        Vector3 deep = new Vector3(0.05f, 0.24f, 0.45f), shallow = new Vector3(0.22f, 0.66f, 0.68f), horizon = new Vector3(0.86f, 0.87f, 0.84f), sky = new Vector3(0.62f, 0.74f, 0.88f);
        var L = new Vector3(SunL.x, SunL.y, -SunL.z); var amb = AmbSky;
        return (ref Frag f, out Vector3 rgb, out float alpha) =>
        {
            var vW = new Vector3(f.p.x, f.p.y, -f.p.z); var cam = new Vector3(camPos.x, camPos.y, -camPos.z);
            float px = f.p.x, pz = f.p.z;
            float ex = Math.Min(px - inner.x, inner.z - px), ez = Math.Min(pz - inner.y, inner.w - pz);
            float wI = Clamp01(Math.Min(ex, ez) * 0.2f);
            float ho = outC.S(px * xfO.z + xfO.x, pz * xfO.w + xfO.y).w * 8 - 4, hn = mapN.S(px * xfN.z + xfN.x, pz * xfN.w + xfN.y).w * 8 - 4;
            float depth = Math.Max(0, -(ho * (1 - wI) + hn * wI));
            var r1 = rip.S(px * 0.061f, pz * 0.061f); var r2 = rip.S(px * 0.137f, pz * 0.137f); var r3 = rip.S(px * 0.019f, pz * 0.019f);
            float r1x = r1.x * 2 - 1, r1y = r1.y * 2 - 1, r2x = r2.x * 2 - 1, r2y = r2.y * 2 - 1, r3x = r3.x * 2 - 1, r3y = r3.y * 2 - 1;
            float calm = Lerp(0.45f, 1, SS(0.2f, 2.5f, depth));
            float slx = (r1x * 0.5f + r2x * 0.32f + r3x * 0.55f) * 0.15f * calm, sly = (r1y * 0.5f + r2y * 0.32f + r3y * 0.55f) * 0.15f * calm;
            float aa1 = px * 0.42f + pz * 0.27f, aa2 = -px * 0.21f + pz * 0.55f;
            slx += ((float)Math.Cos(aa1) * 0.42f - (float)Math.Cos(aa2) * 0.21f) * 0.03f * calm; sly += ((float)Math.Cos(aa1) * 0.27f + (float)Math.Cos(aa2) * 0.55f) * 0.03f * calm;
            var n = new Vector3(-slx, 1, sly).normalized;
            var V = (cam - vW).normalized;
            float dk = 1 - (float)Math.Exp(-depth * 0.8f);
            var water = LerpV(shallow, deep, dk);
            float ndl = Math.Max(Vector3.Dot(n, L), 0) * 0.5f + 0.5f;
            var c = Mul(water, SunC * ndl * 0.8f + amb);
            float nv = Math.Max(Vector3.Dot(n, V), 0);
            float fres = 0.02f + 0.98f * (float)Math.Pow(1 - nv, 5);
            var R = -V - 2 * Vector3.Dot(-V, n) * n;
            var refl = LerpV(horizon, sky, Clamp01(R.y * 1.6f));
            c = LerpV(c, refl, Math.Min(0.8f, Clamp01(fres * 0.85f)));
            float nh = Math.Max(Vector3.Dot(n, (L + V).normalized), 0);
            c += SunC * ((float)Math.Pow(nh, 220) * 1.4f + (float)Math.Pow(nh, 28) * 0.1f) * Lerp(0.4f, 1, dk);
            float fN = 0.5f + 0.5f * r2x;
            float shoreLine = 1 - SS(0, 0.09f + 0.08f * fN, depth);
            float bands = SS(0.6f, 0.95f, (float)Math.Sin(depth * 15 + fN * 4)) * (1 - SS(0.05f, 0.55f, depth));
            float foam = Clamp01(shoreLine + bands * 0.5f) * (0.6f + 0.4f * fN);
            c = LerpV(c, new Vector3(0.95f, 0.97f, 0.96f), foam * 0.82f);
            float a = Lerp(0.42f, 0.93f, SS(0, 1.8f, depth));
            alpha = Math.Max(a, foam * 0.9f);
            rgb = c;
            return true;
        };
    }
    // 远景树公告板（Sanguo/TreeSprite）：按实例矩阵的位置与缩放展开成朝向镜头的面片
    static void DrawSprites(Graphics.InstancedDraw d)
    {
        var atlas = T(d.mat.mainTexture, false);
        var U = (camU + Vector3.up * 1f).normalized;   // normalize(lerp(V[1], up, 0.5))
        for (int i = 0; i < d.count; i++)
        {
            var m = d.m[i];
            var c = new Vector3(m.m03, m.m13, m.m23);
            float sx = new Vector3(m.m00, m.m10, m.m20).magnitude, sy = new Vector3(m.m01, m.m11, m.m21).magnitude;
            float cellI = d.cells != null ? d.cells[i] : 0; float cu = cellI % 4, cv = (float)Math.Floor(cellI / 4);
            var tint = d.cols != null ? new Vector3(d.cols[i].x, d.cols[i].y, d.cols[i].z) : Vector3.one;
            Func<float, float, Vtx> V = (x, y) =>
            {
                var uv = new Vector2((cu + x + 0.5f) / 4f, 1 - (cv + 1 - y) / 4f);
                return new Vtx { p = c + camR * x * sx + U * y * sy, n = Vector3.up, c = new Vector4(1, 1, 1, 1), uv = uv };
            };
            Shade sh = (ref Frag f, out Vector3 rgb, out float a) =>
            {
                var t = atlas.S(f.uv.x, f.uv.y);
                rgb = new Vector3(t.x * tint.x * 1.02f, t.y * tint.y, t.z * tint.z * 0.94f); a = 1;
                return t.w >= 0.42f;
            };
            Vtx a0 = V(-0.5f, 0), a1 = V(-0.5f, 1), a2 = V(0.5f, 1), a3 = V(0.5f, 0);
            Tri(a0, a1, a2, true, false, sh); Tri(a0, a2, a3, true, false, sh);
        }
    }
    // 海路虚线（Sanguo/SeaLane）：顶点按 uv 方向展开半宽 0.16 × clamp(镜头距离 / 60, 0.35, 1)
    static void DrawLanes(Mesh m)
    {
        var V = m.vertices; var O = m.uv; var t = m.triangles;
        var vs = new Vtx[V.Length];
        for (int i = 0; i < V.Length; i++)
        {
            float d = Vector3.Distance(camPos, V[i]);
            var p = V[i] + new Vector3(O[i].x, 0, O[i].y) * 0.16f * Math.Max(0.35f, Math.Min(1, d / 60));
            vs[i] = new Vtx { p = p, n = Vector3.up, c = new Vector4(1, 1, 1, SS(12, 28, d)) };
        }
        Shade sh = (ref Frag f, out Vector3 rgb, out float a) => { rgb = new Vector3(0.88f, 0.93f, 0.98f); a = 0.62f * f.c.w; return a > 0.003f; };
        for (int i = 0; i + 2 < t.Length; i += 3) Tri(vs[t[i]], vs[t[i + 1]], vs[t[i + 2]], false, true, sh);
    }

    // 云团（Sanguo/Cloud）：顶点 = 云团中心，在视空间按角点 × 半径展开；贴图 × 色调 × 明暗，不透明度 _Opacity
    static void DrawCloud(Mesh m, Matrix4x4 M, Material mat)
    {
        var tex = T(mat.mainTexture, false); float op = mat.GetFloat("_Opacity");
        var V = m.vertices; var C = m.uv; var S = m.uv2; var t = m.triangles;
        var vs = new Vtx[V.Length];
        for (int i = 0; i < V.Length; i++)
        {
            var p = M.MultiplyPoint3x4(V[i]) + (camR * C[i].x + camU * C[i].y) * S[i].x;
            vs[i] = new Vtx { p = p, n = Vector3.up, c = new Vector4(S[i].y, 0, 0, 1), uv = new Vector2(C[i].x * 0.5f + 0.5f, 0.5f - C[i].y * 0.5f) };
        }
        Shade sh = (ref Frag f, out Vector3 rgb, out float a) =>
        {
            var c = tex.S(f.uv.x, f.uv.y);
            rgb = new Vector3(c.x * 1.0f, c.y * 0.99f, c.z * 0.96f) * f.c.x; a = c.w * op; return a > 0.003f;
        };
        for (int i = 0; i + 2 < t.Length; i += 3) Tri(vs[t[i]], vs[t[i + 1]], vs[t[i + 2]], false, true, sh);
    }
    static Matrix4x4 World(Transform t)
    {
        return Matrix4x4.TRS(t.position, t.rotation, t.localScale);
    }

    // ------------------------------------------------------------ PNG
    static byte[] Png(float[] rgb, int w, int h)
    {
        var raw = new byte[(w * 3 + 1) * h];
        for (int y = 0; y < h; y++)
        {
            raw[y * (w * 3 + 1)] = 0;
            for (int x = 0; x < w * 3; x++) { float v = rgb[y * w * 3 + x]; raw[y * (w * 3 + 1) + 1 + x] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }
        }
        return PngRaw(raw, w, h, 2);
    }
    static byte[] PngRGBA(byte[] rgba, int w, int h)
    {
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++) Array.Copy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4);
        return PngRaw(raw, w, h, 6);
    }
    static byte[] PngRaw(byte[] raw, int w, int h, byte colorType)
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

    // ------------------------------------------------------------ 剧本（以网页版城池表为准，与 MapViewHarness 相同）
    static void SyncCities(GameState g, List<string[]> rows, string which)
    {
        var js = rows.Where(r => r[0] == "city" && r[1] == which).ToList();
        bool same = js.Count == g.cities.Count;
        for (int i = 0; same && i < js.Count; i++) same = g.cities[i].key == js[i][3];
        if (same) return;
        var byKey = g.cities.ToDictionary(c => c.key, c => c);
        var list = new List<City>();
        foreach (var r in js)
        {
            City old; byKey.TryGetValue(r[3], out old);
            var c = new City { id = int.Parse(r[2]), key = r[3], name = old != null ? old.name : r[3], lon = (float)P(r[4]), lat = (float)P(r[5]), town = int.Parse(r[6]), culture = r[7], owner = old != null ? old.owner : -1 };
            if (r[8] != "-") foreach (var s in r[8].Split(',')) c.links.Add(int.Parse(s));
            list.Add(c);
        }
        g.cities = list;
        g.seaLinks = new HashSet<int>();
        var sr = rows.First(x => x[0] == "sealinks" && x[1] == which);
        for (int k = 3; k < sr.Length; k++) { var ab = sr[k].Split('-'); int a = int.Parse(ab[0]), b = int.Parse(ab[1]); g.seaLinks.Add(Math.Min(a, b) * 4096 + Math.Max(a, b)); }
    }
    static void RunAll(IEnumerator it)
    {
        var st = new Stack<IEnumerator>(); st.Push(it);
        while (st.Count > 0) { var top = st.Peek(); if (!top.MoveNext()) { st.Pop(); continue; } var sub = top.Current as IEnumerator; if (sub != null) st.Push(sub); }
    }
    static double[] Arr(string json, string view, string key)
    {
        int i = json.IndexOf("\"" + view + "\"");
        int j = json.IndexOf("\"" + key + "\"", i);
        var m = Regex.Match(json.Substring(j + key.Length + 2), @"^\s*:\s*(\[[^\]]*\]|[-0-9.eE+]+)");
        var s = m.Groups[1].Value.Trim('[', ']');
        return s.Split(',').Select(x => double.Parse(x.Trim(), Inv)).ToArray();
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args[0], camsPath = args[1], outDir = args[2];
        if (args.Length > 4) { W = int.Parse(args[3]); H = int.Parse(args[4]); }
        Directory.CreateDirectory(outDir);
        var rows = File.ReadAllLines(Path.Combine(dir, "refs.txt")).Where(l => l.Length > 0).Select(l => l.Split(' ')).ToList();
        var json = File.ReadAllText(camsPath);
        var views = Regex.Matches(json, "\"([a-zA-Z0-9_-]+)\"\\s*:\\s*\\{").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        var camGo = new GameObject("Main Camera");
        var cam = Camera.main = camGo.AddComponent<Camera>();
        cam.fieldOfView = 34; cam.aspect = (float)W / H; cam.nearClipPlane = 0.5f; cam.farClipPlane = 1500;
        // 构建时的镜头与网页版截图页相同（rig.focusMap(60, 50, 95)：视线落在地图点 (60, 50)；世界地图的云围绕它摆放）
        cam.transform.position = new Vector3(60, 80, 50 - 80 / (float)Math.Tan(50 * Math.PI / 180));
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(60, 0, 50) - cam.transform.position);
        var update = typeof(MapView).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        var fk = WorldScenarioData.Factions[0].Split('|')[0];
        var g = GameState.NewGame(fk, "world");
        SyncCities(g, rows, "world");
        var mv = new GameObject("Map").AddComponent<MapView>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        mv.Build(g, "world");
        Console.WriteLine("构建 " + sw.ElapsedMilliseconds + " ms");
        Func<string> cloudPos = () =>
        {
            var l = new List<string>();
            for (int i = 0; i < mv.Root.childCount; i++) { var t = mv.Root.GetChild(i); if (t.gameObject.name == "Cloud") l.Add(t.position.x.ToString("F2", Inv) + "," + t.position.z.ToString("F2", Inv) + "," + (t.gameObject.GetComponent<MeshRenderer>().enabled ? t.gameObject.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Opacity").ToString("F2", Inv) : "-")); }
            return string.Join(" ", l);
        };
        if (Environment.GetEnvironmentVariable("CLOUDS") != null) Console.WriteLine("云（构建后）" + cloudPos());
        // 远景树公告板图集（RGBA，第 0 行在下 → PNG 第 0 行在上）
        {
            var at = MapVeg.SpriteAtlas(); int aw = at.width, ah = at.height;
            var rgba = new byte[aw * ah * 4];
            for (int y = 0; y < ah; y++) for (int x = 0; x < aw; x++) { var c = at.pixels[(ah - 1 - y) * aw + x]; int o = (y * aw + x) * 4; rgba[o] = c.r; rgba[o + 1] = c.g; rgba[o + 2] = c.b; rgba[o + 3] = c.a; }
            File.WriteAllBytes(Path.Combine(outDir, "unity-atlas.png"), PngRGBA(rgba, aw, ah));
        }
        foreach (var name in views)
        {
            var pos = Arr(json, name, "pos"); var fwd = Arr(json, name, "fwd");
            float fov = (float)Arr(json, name, "fov")[0], aspect = (float)Arr(json, name, "aspect")[0];
            fogNear = (float)Arr(json, name, "fogNear")[0]; fogFar = (float)Arr(json, name, "fogFar")[0];
            // three → Unity：z 取反
            cam.transform.position = new Vector3((float)pos[0], (float)pos[1], (float)-pos[2]);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3((float)fwd[0], (float)fwd[1], (float)-fwd[2]));
            cam.fieldOfView = fov; cam.aspect = aspect;
            // 与截图页相同：每个视角 80 帧（最后一帧记录实例化绘制）
            for (int f = 0; f < 79; f++) { Time.time += 1f / 60; Time.deltaTime = 1f / 60; update.Invoke(mv, null); }
            Graphics.Recorded = new List<Graphics.InstancedDraw>();
            update.Invoke(mv, null);
            var draws = Graphics.Recorded; Graphics.Recorded = null;

            camPos = cam.transform.position; camF = cam.transform.forward; camU = cam.transform.up; camR = cam.transform.right;
            tanV = (float)Math.Tan(fov * Math.PI / 360); tanH = tanV * aspect; camFar = 1500;
            col = new float[W * H * 3]; dep = new float[W * H];
            for (int i = 0; i < W * H; i++) { dep[i] = float.PositiveInfinity; }
            // 背景：天空（上蓝下地平线色）
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { float k = (float)y / H; var c = LerpV(new Vector3(0.32f, 0.55f, 0.86f), FogC, Math.Min(1, k * 2)); col[(y * W + x) * 3] = c.x; col[(y * W + x) * 3 + 1] = c.y; col[(y * W + x) * 3 + 2] = c.z; }
            trisDrawn = trisCulled = 0;
            var tr = System.Diagnostics.Stopwatch.StartNew();
            // 场景中的普通网格（激活且渲染器开启）：先不透明，后透明
            var opaque = new List<KeyValuePair<Transform, MeshRenderer>>(); var trans = new List<KeyValuePair<Transform, MeshRenderer>>();
            Action<Transform> walk = null;
            walk = t =>
            {
                if (!t.gameObject.activeSelf) return;
                var mf = t.gameObject.GetComponent<MeshFilter>(); var mr = t.gameObject.GetComponent<MeshRenderer>();
                if (mf != null && mr != null && mr.enabled && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
                {
                    string sn = mr.sharedMaterial != null && mr.sharedMaterial.shader != null ? mr.sharedMaterial.shader.name : "";
                    bool tr2 = sn.EndsWith("MapWater") || sn.EndsWith("Unlit") || sn.EndsWith("Road") || sn.EndsWith("SeaLane") || sn.EndsWith("Cloud");
                    (tr2 ? trans : opaque).Add(new KeyValuePair<Transform, MeshRenderer>(t, mr));
                }
                for (int i = 0; i < t.childCount; i++) walk(t.GetChild(i));
            };
            walk(mv.Root);
            var counts = new Dictionary<string, int>();
            foreach (var kv in opaque)
            {
                var t = kv.Key; var mr = kv.Value; var mesh = t.gameObject.GetComponent<MeshFilter>().sharedMesh; var mat = mr.sharedMaterial;
                string sn = mat.shader != null ? mat.shader.name : "";
                Shade sh;
                if (sn.EndsWith("Terrain")) sh = TerrainShade(mat);
                else
                {
                    var tint = V3(mat.color);
                    object o; if (mr.lastBlock != null && mr.lastBlock.values.TryGetValue("_Color", out o)) tint = V3((Color)o);
                    sh = LowPolyShade(tint);
                }
                DrawMesh(mesh, World(t), true, false, sh);
                int c0; counts.TryGetValue(t.gameObject.name, out c0); counts[t.gameObject.name] = c0 + 1;
            }
            // 实例化绘制（树木、石块、灌丛、村舍、草丛）
            foreach (var d in draws)
            {
                string sn = d.mat.shader != null ? d.mat.shader.name : "";
                if (sn.EndsWith("TreeSprite")) { DrawSprites(d); continue; }
                var fs = FoliageShade(d.mat);
                for (int i = 0; i < d.count; i++) DrawMesh(d.mesh, d.m[i], true, false, fs, d.cols != null ? d.cols[i] : new Vector4(1, 1, 1, 1));
            }
            // 透明：水面、道路、光圈、海路（按 Unity 的渲染队列顺序：水面 Transparent-10 → 道路 AlphaTest+10 已在前 → 光圈 → 海路）
            Func<string, int> order = sn => sn.EndsWith("Road") ? 0 : sn.EndsWith("MapWater") ? 1 : sn.EndsWith("Unlit") ? 2 : sn.EndsWith("Cloud") ? 4 : 3;
            foreach (var kv in trans.OrderBy(k => order(k.Value.sharedMaterial.shader.name)))
            {
                var t = kv.Key; var mr = kv.Value; var mesh = t.gameObject.GetComponent<MeshFilter>().sharedMesh; var mat = mr.sharedMaterial; string sn = mat.shader.name;
                if (sn.EndsWith("Cloud")) { DrawCloud(mesh, World(t), mat); continue; }
                if (sn.EndsWith("SeaLane")) { DrawLanes(mesh); continue; }
                Shade sh = sn.EndsWith("MapWater") ? WaterShade(mat) : sn.EndsWith("Road") ? RoadShade(mat) : UnlitShade(mat);
                DrawMesh(mesh, World(t), !sn.EndsWith("Unlit"), true, sh);
                int c0; counts.TryGetValue(t.gameObject.name, out c0); counts[t.gameObject.name] = c0 + 1;
            }
            var file = Path.Combine(outDir, "unity-" + name + ".png");
            File.WriteAllBytes(file, Png(col, W, H));
            Console.WriteLine(string.Format("{0}：三角形 {1}（背面剔除 {2}），实例化绘制 {3} 次，{4} ms → {5}", name, trisDrawn, trisCulled, draws.Count, tr.ElapsedMilliseconds, file));
            Console.WriteLine("    " + string.Join("，", counts.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value)));
        }
        if (Environment.GetEnvironmentVariable("CLOUDS") != null) Console.WriteLine("云（最后）" + cloudPos());
        return 0;
    }
}
