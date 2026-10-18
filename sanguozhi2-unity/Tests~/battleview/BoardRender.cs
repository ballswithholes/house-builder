// 战场画面（第三版）的软件光栅器（无头，对照网页版截图用）：用 C# 移植（Battle/BattleLook.cs 的 BattleBoard）为 cases.json 的用例建棋盘，
// 部队照网页版的导出（BoardHarness.ModelFor），按网页版截图时的镜头（web-<名>.json 的 cam：three 坐标的位置、四元数、视角）
// 画成 unity-<名>.png（960 × 540，2×2 超采样），由 web.mjs compose 与网页版截图左右拼成 side-<名>.png。
//   · 全部在 Unity 世界坐标里做；背面剔除按 Unity 约定（屏幕上顺时针为正面——C# 网格的绕序若与 Unity 相反，这里会直接缺面）；
//     近平面裁剪、z 缓冲。不透明物先光栅化成「三角形号 + 重心坐标」（延迟着色），再逐个子像素着色；
//     屏幕导数（= GPU 的 ddx / ddy，按最终分辨率）由相邻像素的视线与三角形平面求交得到，细节纹理按此选 mip 级别（三线性）。
//   · 着色：平滑地形 = Sanguo/BattleTerrain 着色器的逐行 C# 移植（细节纹理 Art.TerrainDetailBytesA / B、BT 地块外观、
//     格线与棋盘边框、高光肩部）；树石草与点缀 = Sanguo/BattleProp（顶点色 × 实例色、半兰伯特）；城池与桥 = LowPoly + 网页版的
//     砖石图案（masonryMaterial(36, 90)）；船 = LowPoly；水面与海面裙边 = Sanguo/BattleWater 的逐行移植（半透明、不写深度）；
//     挡住部队的树 = Sanguo/BattlePropFade（深度前置遍 + 半透明，淡出量按 BattleView.UpdatePropFade 的目标值）；
//     阵营光环 = Unlit（环形纹理 × 0.7）；主将（中模，骑马）与士兵（低模，步兵 / 骑兵）= FigBuild / FigRig 摆好待机姿势，
//     按 Sanguo/Figure（SgStandard）着色：图集反照率 × 势力色遮罩、法线贴图、ORM、几何粗糙度、太阳 + 半球光 + 环境贴图（× 0.85）、
//     轮廓光（与 Tests~/figure/FigureRender.cs 相同的公式，灯光换成战场的）；图集各向异性 4。人物不接受阴影（同网页版）。
//   · 光照为网页版 art.js 的常量（太阳色 × 1.15 × 0.75、三色环境光 × 0.56，半兰伯特 (n·l·0.5 + 0.5)² × 1.15）；
//     太阳阴影：正交阴影贴图 2048²（中心 = 镜头目标、半径 = max(30, 距离 × 0.95)，同 tests/battle-view.html 的 setShadowFocus），
//     3×3 PCF，阴影强度 0.55；雾 = three 的线性雾（smoothstep(90, 320, 视深)）；无几何处画天空穹顶（Sky.shader）。
//   · 不画：名牌（网页版为 HTML、Unity 为 UGUI）、选择光标、特效。
// 用法：mono boardrender.exe <输出目录> [名…]      （由 Tests~/battleview/run.sh 调用；读 cases.json 与 web-<名>.json）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Sanguo;
using UnityEngine;

static class BoardRender
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static double D(object o) { return o == null ? 0 : Convert.ToDouble(o, Inv); }
    static Dictionary<string, object> O(object o) { return o as Dictionary<string, object>; }
    static List<object> L(object o) { return o as List<object>; }

    // ------------------------------------------------------------ 光照常量（网页版 art.js）
    static readonly Vector3 SunL = SunDir();
    static Vector3 SunDir() { double p = 48 * Math.PI / 180, y = -35 * Math.PI / 180; return -new Vector3((float)(Math.Sin(y) * Math.Cos(p)), (float)-Math.Sin(p), (float)(Math.Cos(y) * Math.Cos(p))); }
    static readonly Vector3 SunC = new Vector3(1f, 0.94f, 0.84f) * (1.15f * 0.75f);
    static readonly Vector3 AmbSky = new Vector3(0.62f, 0.68f, 0.8f) * 0.56f, AmbEq = new Vector3(0.58f, 0.56f, 0.5f) * 0.56f, AmbGround = new Vector3(0.32f, 0.3f, 0.26f) * 0.56f;
    static readonly Vector3 FogC = new Vector3(0.86f, 0.84f, 0.78f);
    const float FogNear = 90, FogFar = 320, ShadowK = 0.55f, CamNear = 0.5f, CamFar = 700;
    // 三色环境光（LowPoly 的着色器补丁：法线朝上在地平线色与天空色之间、朝下在地平线色与地面色之间）
    static Vector3 Amb(Vector3 n) { float d = n.y; return d >= 0 ? Vector3.Lerp(AmbEq, AmbSky, d) : Vector3.Lerp(AmbEq, AmbGround, -d); }
    // 人物材质（MeshStandardMaterial）的半球光：地面色 → 天空色
    static Vector3 Hemi(Vector3 n) { return Vector3.Lerp(AmbGround, AmbSky, n.y * 0.5f + 0.5f); }

    static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
    static float SS(float a, float b, float x) { float t = Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    static Vector3 LerpV(Vector3 a, Vector3 b, float t) { return a + (b - a) * t; }
    static Vector3 Mul(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
    static float Frac(float v) { return v - (float)Math.Floor(v); }
    static float Step(float edge, float x) { return x >= edge ? 1 : 0; }
    static float Lum(Vector3 c) { return c.x * 0.3f + c.y * 0.59f + c.z * 0.11f; }
    static Vector3 Col(string hex) { var c = FigMath.col(hex); return new Vector3((float)c.r, (float)c.g, (float)c.b); }

    // ------------------------------------------------------------ 纹理（双线性 + mip 链，三线性取样）
    sealed class Tex
    {
        public readonly int w, h; public readonly bool repeat, topFirst;
        readonly List<float[]> lv = new List<float[]>(); readonly List<int> lw = new List<int>(), lh = new List<int>();
        public Tex(byte[] rgba, int w, int h, bool repeat, bool topFirst)
        {
            this.w = w; this.h = h; this.repeat = repeat; this.topFirst = topFirst;
            var d = new float[w * h * 4]; for (int i = 0; i < d.Length; i++) d[i] = rgba[i] / 255f;
            lv.Add(d); lw.Add(w); lh.Add(h);
            // 2×2 盒式下采样（同 GPU 的 mipmap 生成）
            while (w > 1 || h > 1)
            {
                int w2 = Math.Max(1, w / 2), h2 = Math.Max(1, h / 2);
                var s = lv[lv.Count - 1]; var n = new float[w2 * h2 * 4];
                for (int y = 0; y < h2; y++) for (int x = 0; x < w2; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1), y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                        n[(y * w2 + x) * 4 + c] = (s[(y0 * w + x0) * 4 + c] + s[(y0 * w + x1) * 4 + c] + s[(y1 * w + x0) * 4 + c] + s[(y1 * w + x1) * 4 + c]) * 0.25f;
                    }
                lv.Add(n); lw.Add(w2); lh.Add(h2); w = w2; h = h2;
            }
        }
        int Wrap(int i, int n) { return repeat ? ((i % n) + n) % n : (i < 0 ? 0 : i >= n ? n - 1 : i); }
        void Bil(int l, float u, float v, ref Vector4 o)
        {
            int w = lw[l], h = lh[l]; var d = lv[l];
            float fy = topFirst ? (1 - v) * h - 0.5f : v * h - 0.5f, fx = u * w - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); float tx = fx - x0, ty = fy - y0;
            int xa = Wrap(x0, w), xb = Wrap(x0 + 1, w), ya = Wrap(y0, h), yb = Wrap(y0 + 1, h);
            int a = (ya * w + xa) * 4, b = (ya * w + xb) * 4, c = (yb * w + xa) * 4, e = (yb * w + xb) * 4;
            float w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty), w01 = (1 - tx) * ty, w11 = tx * ty;
            o = new Vector4(d[a] * w00 + d[b] * w10 + d[c] * w01 + d[e] * w11, d[a + 1] * w00 + d[b + 1] * w10 + d[c + 1] * w01 + d[e + 1] * w11,
                d[a + 2] * w00 + d[b + 2] * w10 + d[c + 2] * w01 + d[e + 2] * w11, d[a + 3] * w00 + d[b + 3] * w10 + d[c + 3] * w01 + d[e + 3] * w11);
        }
        public Vector4 S(float u, float v, float lod)
        {
            var o = new Vector4();
            int top = lv.Count - 1;
            if (!(lod > 0)) { Bil(0, u, v, ref o); return o; }
            if (lod >= top) { Bil(top, u, v, ref o); return o; }
            int l0 = (int)lod; float f = lod - l0;
            Bil(l0, u, v, ref o);
            if (f > 0.001f) { var o2 = new Vector4(); Bil(l0 + 1, u, v, ref o2); o = o * (1 - f) + o2 * f; }
            return o;
        }
        // uv 的屏幕导数 → 级别（同 GPU：ρ = max(|∂uv/∂x|, |∂uv/∂y|) 个纹素；aniso > 1 时按各向异性过滤取短轴，
        // 长轴 / 短轴之比最多 aniso 倍——这里只在该级别取一次样，不沿长轴多次取样）
        public float Lod(float dux, float dvx, float duy, float dvy, float aniso = 1)
        {
            float a = (dux * dux + dvx * dvx) * w * w, b = (duy * duy + dvy * dvy) * w * w;
            float r = Math.Max(a, b);
            if (aniso > 1) { float mn = Math.Min(a, b); float k = Math.Min(aniso * aniso, mn > 1e-12f ? r / mn : aniso * aniso); r /= k; }
            return r > 1e-12f ? 0.5f * (float)(Math.Log(r) / Math.Log(2)) : 0;
        }
    }
    static Tex detA, detB, ripple, atlas, atlasNrm, atlasOrm, flatNrm, flatOrm;
    static FigAtlas.EnvCubes envC;

    // ------------------------------------------------------------ 场景三角形
    struct Vt { public Vector3 p, n; public Vector4 c, a, b; public float u, v; }
    const int K_TERRAIN = 0, K_LOW = 1, K_MASON = 2, K_FIG = 3, K_WATER = 4, K_RING = 5;
    sealed class Mat { public int kind; public Tex map, nrm, orm; public Vector3 team = Vector3.one, col; public float alpha = 1; public Vector3 center; public float radius; }
    sealed class Tri { public Vt a, b, c; public Mat m; public bool cast, recv; public float alpha = 1, lod, gR; public Vector3 gn, T, B; }
    static readonly List<Tri> opaque = new List<Tri>(), fades = new List<Tri>(), waters = new List<Tri>(), rings = new List<Tri>();
    static readonly Mat mTerrain = new Mat { kind = K_TERRAIN }, mLow = new Mat { kind = K_LOW }, mMason = new Mat { kind = K_MASON }, mWater = new Mat { kind = K_WATER };
    static Vector4 V4(Color c) { return new Vector4(c.r, c.g, c.b, c.a); }

    static void Add(List<Tri> list, Vt a, Vt b, Vt c, Mat m, bool cast, bool recv, float alpha = 1)
    {
        var t = new Tri { a = a, b = b, c = c, m = m, cast = cast, recv = recv, alpha = alpha };
        t.gn = Vector3.Cross(b.p - a.p, c.p - a.p);
        list.Add(t);
    }
    // MeshBuilder（Unity 坐标）× 矩阵；法线按逆转置（TRS 的缩放取倒数）
    static void AddMB(List<Tri> list, MeshBuilder mb, Matrix4x4 M, Vector3 scale, Vector4 tint, Mat m, bool cast, bool recv, float alpha = 1)
    {
        var vs = new Vt[mb.v.Count];
        var inv2 = new Vector3(1 / (scale.x * scale.x), 1 / (scale.y * scale.y), 1 / (scale.z * scale.z));
        for (int i = 0; i < vs.Length; i++)
        {
            var c = mb.c[i];
            vs[i] = new Vt { p = M.MultiplyPoint3x4(mb.v[i]), n = M.MultiplyVector(Mul(mb.n[i], inv2)).normalized, c = new Vector4(c.r * tint.x, c.g * tint.y, c.b * tint.z, c.a) };
        }
        for (int i = 0; i + 2 < mb.t.Count; i += 3) Add(list, vs[mb.t[i]], vs[mb.t[i + 1]], vs[mb.t[i + 2]], m, cast, recv, alpha);
    }

    // ------------------------------------------------------------ 镜头与缓冲
    const int SSK = 2;
    static int W, H, OW, OH;
    static float tanV, tanH;
    static Vector3 camPos, camF, camR, camU;
    static int[] gTri, fTri; static float[] gZ, gB0, gB1, fZ, fB0, fB1;
    static float[] colBuf;
    static Vector3 ToCam(Vector3 p) { var d = p - camPos; return new Vector3(Vector3.Dot(d, camR), Vector3.Dot(d, camU), Vector3.Dot(d, camF)); }
    static Vector3 RayDir(float sx, float sy) { return camF + camR * ((sx / W * 2 - 1) * tanH) + camU * ((1 - sy / H * 2) * tanV); }

    struct CV { public Vector3 c; public float b0, b1, b2; }
    delegate void Frag(int o, float z, float b0, float b1, float b2);
    static long drawn, culled;
    static CV LerpCV(CV a, CV b, float t) { return new CV { c = a.c + (b.c - a.c) * t, b0 = Lerp(a.b0, b.b0, t), b1 = Lerp(a.b1, b.b1, t), b2 = Lerp(a.b2, b.b2, t) }; }
    static readonly List<CV> polyIn = new List<CV>(4), polyOut = new List<CV>(5);
    static void RasterTri(Vector3 pa, Vector3 pb, Vector3 pc, bool cull, Frag f)
    {
        var a = new CV { c = ToCam(pa), b0 = 1 }; var b = new CV { c = ToCam(pb), b1 = 1 }; var c = new CV { c = ToCam(pc), b2 = 1 };
        if (a.c.z > CamFar && b.c.z > CamFar && c.c.z > CamFar) return;
        if (a.c.z >= CamNear && b.c.z >= CamNear && c.c.z >= CamNear) { RasterCV(a, b, c, cull, f); return; }
        polyIn.Clear(); polyIn.Add(a); polyIn.Add(b); polyIn.Add(c); polyOut.Clear();
        for (int i = 0; i < 3; i++)
        {
            var s = polyIn[i]; var e = polyIn[(i + 1) % 3];
            bool si = s.c.z >= CamNear, ei = e.c.z >= CamNear;
            if (si) polyOut.Add(s);
            if (si != ei) polyOut.Add(LerpCV(s, e, (CamNear - s.c.z) / (e.c.z - s.c.z)));
        }
        if (polyOut.Count < 3) return;
        var q = polyOut.ToArray();
        for (int i = 1; i + 1 < q.Length; i++) RasterCV(q[0], q[i], q[i + 1], cull, f);
    }
    static void RasterCV(CV a, CV b, CV c, bool cull, Frag f)
    {
        float ax = a.c.x / (a.c.z * tanH), ay = a.c.y / (a.c.z * tanV), bx = b.c.x / (b.c.z * tanH), by = b.c.y / (b.c.z * tanV), cx = c.c.x / (c.c.z * tanH), cy = c.c.y / (c.c.z * tanV);
        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (cull && area >= 0) { culled++; return; }          // NDC 中逆时针（面积 > 0）= Unity 的背面
        float X0 = (ax * 0.5f + 0.5f) * W, Y0 = (0.5f - ay * 0.5f) * H, X1 = (bx * 0.5f + 0.5f) * W, Y1 = (0.5f - by * 0.5f) * H, X2 = (cx * 0.5f + 0.5f) * W, Y2 = (0.5f - cy * 0.5f) * H;
        float A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
        if (Math.Abs(A) < 1e-12f) return;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(X0, Math.Min(X1, X2)))), maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(X0, Math.Max(X1, X2))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(Y0, Math.Min(Y1, Y2)))), maxY = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(Y0, Math.Max(Y1, Y2))));
        if (minX > maxX || minY > maxY) return;
        drawn++;
        float iz0 = 1 / a.c.z, iz1 = 1 / b.c.z, iz2 = 1 / c.c.z, iA = 1 / A;
        for (int py = minY; py <= maxY; py++)
        {
            float sy = py + 0.5f;
            for (int px = minX; px <= maxX; px++)
            {
                float sx = px + 0.5f;
                float w0 = ((X1 - sx) * (Y2 - sy) - (Y1 - sy) * (X2 - sx)) * iA, w1 = ((X2 - sx) * (Y0 - sy) - (Y2 - sy) * (X0 - sx)) * iA, w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float iz = w0 * iz0 + w1 * iz1 + w2 * iz2, z = 1 / iz;
                float k0 = w0 * iz0 * z, k1 = w1 * iz1 * z, k2 = w2 * iz2 * z;
                f(py * W + px, z, k0 * a.b0 + k1 * b.b0 + k2 * c.b0, k0 * a.b1 + k1 * b.b1 + k2 * c.b1, k0 * a.b2 + k1 * b.b2 + k2 * c.b2);
            }
        }
    }

    // ------------------------------------------------------------ 太阳阴影贴图（正交）
    const int SM = 2048; static float[] smap; static float smX0, smY0, smS; static Vector3 smR, smU;
    static void BuildShadow(Vector3 center, float radius)
    {
        smR = Vector3.Cross(Vector3.up, SunL).normalized; smU = Vector3.Cross(SunL, smR).normalized;
        smX0 = Vector3.Dot(center, smR) - radius; smY0 = Vector3.Dot(center, smU) - radius; smS = radius * 2;
        smap = new float[SM * SM]; for (int i = 0; i < smap.Length; i++) smap[i] = float.MinValue;
        foreach (var list in new[] { opaque, fades })
            foreach (var t in list)
            {
                if (!t.cast) continue;
                float X0 = (Vector3.Dot(t.a.p, smR) - smX0) / smS * SM, Y0 = (Vector3.Dot(t.a.p, smU) - smY0) / smS * SM, Z0 = Vector3.Dot(t.a.p, SunL);
                float X1 = (Vector3.Dot(t.b.p, smR) - smX0) / smS * SM, Y1 = (Vector3.Dot(t.b.p, smU) - smY0) / smS * SM, Z1 = Vector3.Dot(t.b.p, SunL);
                float X2 = (Vector3.Dot(t.c.p, smR) - smX0) / smS * SM, Y2 = (Vector3.Dot(t.c.p, smU) - smY0) / smS * SM, Z2 = Vector3.Dot(t.c.p, SunL);
                float A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
                if (Math.Abs(A) < 1e-9f) continue;
                int mx = Math.Max(0, (int)Math.Floor(Math.Min(X0, Math.Min(X1, X2)))), Mx = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(X0, Math.Max(X1, X2))));
                int my = Math.Max(0, (int)Math.Floor(Math.Min(Y0, Math.Min(Y1, Y2)))), My = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(Y0, Math.Max(Y1, Y2))));
                for (int y = my; y <= My; y++) for (int x = mx; x <= Mx; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    float w0 = ((X1 - sx) * (Y2 - sy) - (Y1 - sy) * (X2 - sx)) / A, w1 = ((X2 - sx) * (Y0 - sy) - (Y2 - sy) * (X0 - sx)) / A, w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = w0 * Z0 + w1 * Z1 + w2 * Z2;
                    if (z > smap[y * SM + x]) smap[y * SM + x] = z;
                }
            }
    }
    // 受光比例（0 = 全在影中）；法线偏移 0.035、深度偏移约 0.06（three 的 normalBias / bias）
    static float Lit(Vector3 p, Vector3 n)
    {
        p = p + n * 0.035f;
        float fx = (Vector3.Dot(p, smR) - smX0) / smS * SM - 0.5f, fy = (Vector3.Dot(p, smU) - smY0) / smS * SM - 0.5f, z = Vector3.Dot(p, SunL) + 0.06f;
        int cx = (int)Math.Floor(fx), cy = (int)Math.Floor(fy); float tx = fx - cx, ty = fy - cy;
        // 3×3 PCF（每个取样双线性 → 软边）
        float s = 0;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            float a = Tap(cx + dx, cy + dy, z), b = Tap(cx + dx + 1, cy + dy, z), c = Tap(cx + dx, cy + dy + 1, z), d = Tap(cx + dx + 1, cy + dy + 1, z);
            s += (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
        }
        return s / 9;
    }
    static float Tap(int x, int y, float z) { if (x < 0 || y < 0 || x >= SM || y >= SM) return 1; return smap[y * SM + x] > z ? 0 : 1; }
    static float Atten(Vector3 p, Vector3 n) { return 1 - ShadowK * (1 - Lit(p, n)); }

    // ------------------------------------------------------------ 着色
    static float Fog(float z) { return SS(FogNear, FogFar, z); }
    static Vector3 Shoulder(Vector3 c)
    {
        float peak = Math.Max(Math.Max(c.x, c.y), c.z);
        if (peak > 0.8f) c *= (0.8f + 0.2f * (1 - (float)Math.Exp((0.8f - peak) / 0.2f))) / peak;
        return c;
    }
    static Vector3 HalfLambert(Vector3 alb, Vector3 n, float atten)
    {
        float hl = Vector3.Dot(n, SunL) * 0.5f + 0.5f; float ndl = hl * hl * 1.15f * atten;
        var a = Amb(n);
        return new Vector3(alb.x * (SunC.x * ndl + a.x), alb.y * (SunC.y * ndl + a.y), alb.z * (SunC.z * ndl + a.z));
    }
    static Vector3 Crop(float id)
    {
        var c = new Vector3(0.42f, 0.57f, 0.23f);
        c = LerpV(c, new Vector3(0.75f, 0.66f, 0.31f), Step(0.24f, id));
        c = LerpV(c, new Vector3(0.53f, 0.41f, 0.28f), Step(0.42f, id));
        c = LerpV(c, new Vector3(0.33f, 0.49f, 0.2f), Step(0.56f, id));
        c = LerpV(c, new Vector3(0.64f, 0.62f, 0.3f), Step(0.72f, id));
        c = LerpV(c, new Vector3(0.47f, 0.6f, 0.26f), Step(0.87f, id));
        return c;
    }
    // 平面映射 uv = (a, b) · k + off 的取样；(da, db) 为 a、b 的屏幕导数
    static Vector4 Smp(Tex t, float a, float b, float k, float ox, float oy, float dax, float dbx, float day, float dby)
    {
        return t.S(a * k + ox, b * k + oy, t.Lod(dax * k, dbx * k, day * k, dby * k));
    }
    static Vector4 boardRect;   // _Board = (原点 x, 原点 z, W, H)
    const float GridK = 0.16f;

    // 三平面投影的岩石取样：W3.zy、W3.xz、W3.xy（W3 的导数：x、y 同 Unity，z 取反）
    static float Tri3(float k, Vector3 W3, Vector3 bw, Vector3 dPx, Vector3 dPy)
    {
        float dWzx = -dPx.z, dWzy = -dPy.z;
        return Smp(detB, W3.z, W3.y, k, 0, 0, dWzx, dPx.y, dWzy, dPy.y).x * bw.x + Smp(detB, W3.x, W3.z, k, 0, 0, dPx.x, dWzx, dPy.x, dWzy).x * bw.y
            + Smp(detB, W3.x, W3.y, k, 0, 0, dPx.x, dPx.y, dPy.x, dPy.y).x * bw.z;
    }
    // Sanguo/BattleTerrain 的 surf（逐行）：返回反照率；WN = Unity 世界法线
    static Vector3 TerrainAlbedo(Vector3 P, Vector3 Nn, Vector4 vc, Vector4 mat, Vector4 bt, Vector3 dPx, Vector3 dPy, out Vector3 WN)
    {
        var W3 = new Vector3(P.x, P.y, -P.z);
        float sgH = W3.y;
        var sgN = new Vector3(Nn.x, Nn.y, -Nn.z).normalized;
        var tint = new Vector3(vc.x, vc.y, vc.z);
        float sgForest = 0, sgAO = 0.5f, sgNearW = 1;
        float px = P.x, pz = P.z;
        var DA = Smp(detA, px, pz, 0.23f, 0, 0, dPx.x, dPx.z, dPy.x, dPy.z);
        var DM = Smp(detA, px, pz, 0.047f, 0.31f, 0.67f, dPx.x, dPx.z, dPy.x, dPy.z);
        var DF = Smp(detA, px, pz, 0.93f, 0.53f, 0.19f, dPx.x, dPx.z, dPy.x, dPy.z);
        var DN = Smp(detA, px, pz, 3.1f, 0.7f, 0.2f, dPx.x, dPx.z, dPy.x, dPy.z);
        var DB = Smp(detB, px, pz, 0.11f, 0.13f, 0.71f, dPx.x, dPx.z, dPy.x, dPy.z);
        float slope = 1 - sgN.y;
        float near = 1 - SS(18, 40, Vector3.Distance(camPos, P));
        var col = tint * (0.76f + 0.5f * (DA.x * 0.36f + DF.x * 0.28f + DM.x * 0.36f));
        if (near > 0.01f) col *= 1 + (DN.x - 0.5f) * 0.24f * near;
        float l0 = Lum(col);
        col = Mul(LerpV(new Vector3(l0, l0, l0), col, 0.9f), LerpV(new Vector3(1.035f, 1, 0.93f), new Vector3(0.96f, 1, 1.04f), DM.z));
        col = LerpV(col, Mul(col, new Vector3(1.1f, 1.03f, 0.8f)), SS(0.5f, 0.78f, DM.y) * 0.55f);
        col = LerpV(col, Mul(col, new Vector3(0.78f, 0.86f, 0.74f)), SS(0.55f, 0.85f, DM.w) * 0.6f);
        col *= 1 - 0.3f * sgForest;
        float dry = Clamp((tint.x - tint.y * 0.85f) * 4 + 0.3f, 0, 1);
        float dirtW = SS(0.66f, 0.9f, DM.w * 0.62f + slope * 0.95f + dry * 0.2f + (DA.y - 0.5f) * 0.2f);
        var dirt = LerpV(new Vector3(0.48f, 0.39f, 0.28f), Mul(tint, new Vector3(0.95f, 0.85f, 0.7f)), 0.35f) * (0.78f + 0.44f * DA.y);
        col = LerpV(col, dirt, dirtW * 0.85f);
        float farm = 1 - mat.w, fw = 0;
        if (farm > 0.02f)
        {
            var crop = Crop(DB.z) * (0.8f + 0.34f * DB.w);
            crop = LerpV(crop, new Vector3(0.58f, 0.5f, 0.36f), Step(DB.z, 0.03f));
            crop = LerpV(crop, tint, 0.15f);
            if (near > 0.01f)
            {
                float rx = (float)Math.Cos(DB.z * 6.2832f), ry = (float)Math.Sin(DB.z * 6.2832f);
                crop *= Lerp(1, 0.86f + 0.14f * (float)Math.Sin((px * rx + pz * ry) * 38), near * Step(0.03f, DB.z));
            }
            fw = SS(0.3f, 0.52f, farm + (DM.x - 0.5f) * 0.55f) * (1 - SS(0.1f, 0.26f, slope));
            col = LerpV(col, crop, fw);
        }
        float beach = (1 - SS(0.2f, 0.48f, sgH + (DA.z - 0.5f) * 0.14f)) * Math.Max(sgNearW, 1 - SS(-0.02f, 0.05f, sgH));
        float rip = Lerp(0.08f, 0.26f, SS(0.3f, 0.8f, mat.x));
        var sand = LerpV(new Vector3(0.87f, 0.8f, 0.62f), tint * 1.05f, Clamp(mat.x * 0.7f, 0, 0.6f)) * (1 - rip * 0.55f + rip * DA.z + (DF.x - 0.5f) * 0.08f);
        sand *= Lerp(0.8f, 1, SS(0, 0.22f, sgH));
        col = LerpV(col, sand, Math.Max(beach, SS(0.2f, 0.7f, mat.x + (DM.w - 0.5f) * 0.25f) * (1 - fw)));
        float rockW = SS(0.4f, 0.62f, slope + (DM.w - 0.5f) * 0.22f - mat.y * 0.12f);
        rockW = Math.Max(rockW, SS(4.4f, 6.6f, sgH + (DM.x - 0.5f) * 1.6f + mat.y * 1.0f));
        var bw = Mul(sgN, sgN); bw = Mul(bw, bw); bw /= (bw.x + bw.y + bw.z);
        // 三平面投影：W3.zy、W3.xz、W3.xy（W3 的导数：x、y 同 Unity，z 取反）
        float R = Tri3(0.19f, W3, bw, dPx, dPy), btR = Tri3(0.31f, W3, bw, dPx, dPy);
        if (rockW > 0.004f)
        {
            var rockC = LerpV(new Vector3(0.46f, 0.44f, 0.41f), new Vector3(0.56f, 0.47f, 0.37f), Clamp(mat.y, 0, 1)) * (0.55f + 0.75f * R);
            col = LerpV(col, rockC, rockW);
            sgAO += (R - 0.5f) * 0.25f * rockW;
        }
        float snowW = SS(0.3f, 0.62f, mat.z + (DA.w - 0.5f) * 0.5f + (DM.w - 0.5f) * 0.3f - slope * 0.7f);
        col = LerpV(col, new Vector3(0.8f, 0.83f, 0.88f) * (0.88f + 0.16f * DA.w), snowW);
        col = LerpV(col, new Vector3(0.6f, 0.57f, 0.45f) * (0.85f + 0.2f * DA.z), SS(-0.05f, -0.7f, sgH) * 0.75f);
        col *= 0.74f + 0.52f * Clamp(sgAO, 0, 1);
        // ---- BT_GLSL
        {
            float sgHl = Clamp(bt.x, 0, 1), sgMt = Clamp(bt.y, 0, 1);
            var c = col;
            if (sgHl > 0.01f)
            {
                float lum = Lum(c);
                var dryC = new Vector3(0.74f, 0.69f, 0.43f) * ((0.55f + 0.8f * lum) * (0.86f + 0.28f * DF.x) * (0.9f + 0.16f * SS(0.95f, 1.45f, sgH)));
                c = LerpV(c, dryC, sgHl * (0.3f + 0.3f * DM.y));
            }
            if (sgMt > 0.01f)
            {
                float r = btR;
                float strata = 0.9f + 0.12f * (float)Math.Sin(sgH * 7 + r * 4);
                var rock = LerpV(new Vector3(0.36f, 0.34f, 0.32f), new Vector3(0.47f, 0.42f, 0.36f), DM.x) * ((0.55f + 0.7f * r) * strata);
                var scree = new Vector3(0.52f, 0.48f, 0.42f) * (0.75f + 0.4f * DA.y);
                float bse = 1 - SS(1.05f, 1.7f, sgH + (DA.z - 0.5f) * 0.3f);
                rock = LerpV(rock, scree, bse * 0.6f);
                c = LerpV(c, rock, sgMt * (0.62f + 0.38f * SS(1.0f, 1.9f, sgH)));
                float snow = SS(0.3f, 0.6f, bt.z + (DA.w - 0.5f) * 0.35f + (r - 0.5f) * 0.3f) * (1 - SS(0.6f, 0.88f, slope));
                c = LerpV(c, new Vector3(0.86f, 0.88f, 0.92f) * (0.9f + 0.12f * DA.w), snow);
            }
            col = c;
        }
        // ---- GRID_GLSL
        {
            float bx = (P.x - boardRect.x) * 0.5f, bz = (P.z - boardRect.y) * 0.5f;
            float ox = Math.Max(Math.Max(-bx, bx - boardRect.z), 0), oz = Math.Max(Math.Max(-bz, bz - boardRect.w), 0);
            float outD = (float)Math.Sqrt(ox * ox + oz * oz);
            float ins = 1 - Step(1e-4f, outD);
            float gx = Math.Abs(Frac(bx) - 0.5f), gz = Math.Abs(Frac(bz) - 0.5f);
            float fwx = Math.Max((Math.Abs(dPx.x) + Math.Abs(dPy.x)) * 0.5f, 1e-4f), fwz = Math.Max((Math.Abs(dPx.z) + Math.Abs(dPy.z)) * 0.5f, 1e-4f);
            float lx = 1 - SS(0, fwx * 1.3f + 0.012f, 0.5f - gx), lz = 1 - SS(0, fwz * 1.3f + 0.012f, 0.5f - gz);
            float ln = Math.Max(lx, lz) * ins;
            float far = 1 - SS(0.35f, 0.9f, Math.Max(fwx, fwz));
            col *= 1 - GridK * ln * far;
            float oo = SS(0, 1.6f, outD);
            float lum = Lum(col);
            col = LerpV(col, LerpV(col, new Vector3(lum, lum, lum), 0.25f) * 0.84f, oo);
            float ex = Math.Min(bx, boardRect.z - bx), ez = Math.Min(bz, boardRect.w - bz), em = Math.Min(ex, ez);
            float edge = (1 - SS(0, fwx * 1.5f + 0.035f, Math.Abs(em))) * Step(-0.05f, em);
            col *= 1 - 0.35f * edge;
        }
        WN = new Vector3(sgN.x, sgN.y, -sgN.z);
        return col;
    }

    // 网页版 masonryMaterial 的砖石图案（three 坐标；随视深 36 → 90 淡出）
    static float MHash(float x, float y) { return Frac((float)Math.Sin(x * 127.1f + y * 311.7f) * 43758.5453f); }
    static float GMod(float x, float y) { return x - y * (float)Math.Floor(x / y); }
    static Vector3 Masonry(Vector3 c, Vector3 Pu, Vector3 Nu, float viewZ)
    {
        float fd = 1 - SS(36, 90, viewZ);
        if (fd <= 0) return c;
        var n = new Vector3(Nu.x, Nu.y, -Nu.z).normalized; var p = new Vector3(Pu.x, Pu.y, -Pu.z);
        float mx = Math.Max(Math.Max(c.x, c.y), c.z), mn = Math.Min(Math.Min(c.x, c.y), c.z);
        float sat = (mx - mn) / Math.Max(mx, 0.001f);
        float ay = Math.Abs(n.y), k = 1;
        float wW = (1 - SS(0.25f, 0.4f, ay)) * Clamp(1 - (sat - 0.25f) * 2.2f, 0, 1);
        if (wW > 0)
        {
            float u = Math.Abs(n.x) > Math.Abs(n.z) ? p.z : p.x;
            float row = (float)Math.Floor(p.y / 0.052f);
            float uu = u / 0.12f + GMod(row, 2) * 0.5f;
            float fy = Frac(p.y / 0.052f), fu = Frac(uu);
            float mort = 1 - SS(0.05f, 0.16f, Math.Min(fy, 1 - fy)) * SS(0.03f, 0.08f, Math.Min(fu, 1 - fu));
            float h = MHash((float)Math.Floor(uu), row);
            k *= Lerp(1, (0.9f + 0.16f * h) * (1 - 0.24f * mort), wW);
        }
        float rW = SS(0.25f, 0.4f, ay) * (1 - SS(0.9f, 0.97f, ay));
        if (rW > 0)
        {
            float tx = -n.z + 1e-4f, tz = n.x, tl = (float)Math.Sqrt(tx * tx + tz * tz); tx /= tl; tz /= tl;
            float u = p.x * tx + p.z * tz;
            float roll = 0.5f + 0.5f * (float)Math.Sin(u / 0.04f * 6.2832f);
            float fy = Frac(p.y / 0.03f);
            k *= Lerp(1, (0.84f + 0.22f * roll * roll) * (1 - 0.18f * (1 - SS(0, 0.22f, fy))), rW);
        }
        float fW = SS(0.9f, 0.97f, ay) * Clamp(1 - (sat - 0.25f) * 2.2f, 0, 1);
        if (fW > 0)
        {
            float gx = p.x / 0.14f, gz = p.z / 0.14f;
            float fx = Math.Abs(Frac(gx) - 0.5f), fz = Math.Abs(Frac(gz) - 0.5f);
            float j = 1 - SS(0.42f, 0.48f, Math.Max(fx, fz));
            k *= Lerp(1, (0.93f + 0.1f * MHash((float)Math.Floor(gx), (float)Math.Floor(gz))) * (0.86f + 0.14f * j), fW * 0.8f);
        }
        return c * Lerp(1, k, fd);
    }

    // Sanguo/BattleWater（逐行）：p = 起伏后的世界坐标，shore = 顶点色 r；返回颜色与不透明度
    const float WaterT = 1.5f, WAmp = 0.08f, WAlpha = 0.86f;
    static readonly Vector3 WDeep = new Vector3(0.07f, 0.27f, 0.48f), WShallow = new Vector3(0.25f, 0.7f, 0.75f);
    static readonly Vector3 WSunC = new Vector3(0.8625f, 0.81075f, 0.7245f), WAmb = new Vector3(0.3472f, 0.3808f, 0.448f);
    static readonly Vector3 WSheen = new Vector3(0.30421f, 0.40562f, 0.86194f).normalized, WSheenC = new Vector3(1, 0.93f, 0.78f), WSky = new Vector3(0.8f, 0.84f, 0.86f);
    static float WaveH(float ux, float uz) { float t = WaterT; return (float)(Math.Sin(t * 1.3 + ux * 0.55 + uz * 0.35) * 0.6 + Math.Sin(t * 0.9 - ux * 0.27 + uz * 0.71) * 0.4); }
    static Vector3 WaterShade(Vector3 P, float shoreIn, Vector3 dPx, Vector3 dPy, out float alpha)
    {
        float ux = P.x, uz = P.z, t = WaterT;
        float a1 = t * 1.3f + ux * 0.55f + uz * 0.35f, a2 = t * 0.9f - ux * 0.27f + uz * 0.71f;
        float dx = (float)Math.Cos(a1) * 0.33f - (float)Math.Cos(a2) * 0.11f, dz = (float)Math.Cos(a1) * 0.21f + (float)Math.Cos(a2) * 0.28f;
        var n = new Vector3(-dx * WAmp * 4, 1, -dz * WAmp * 4).normalized;
        float shore = Clamp01(shoreIn);
        float fw = Math.Max(3.1f * (Math.Abs(dPx.x) + Math.Abs(dPy.x)), 2.7f * (Math.Abs(dPx.z) + Math.Abs(dPy.z)));
        float sparkle = (float)(Math.Sin(ux * 3.1 + t * 2.0) * Math.Sin(uz * 2.7 - t * 1.7));
        var albedo = LerpV(WDeep, WShallow, shore) + Vector3.one * (Clamp01(sparkle - 0.85f) * 0.6f * (1 - SS(0.3f, 0.9f, fw)));
        var V = (camPos - P).normalized;
        var r1 = ripple.S(ux * 0.045f + t * 0.012f, uz * 0.045f + t * 0.008f, ripple.Lod(dPx.x * 0.045f, dPx.z * 0.045f, dPy.x * 0.045f, dPy.z * 0.045f));
        var r2 = ripple.S(ux * 0.093f - t * 0.010f, uz * 0.093f + t * 0.015f, ripple.Lod(dPx.x * 0.093f, dPx.z * 0.093f, dPy.x * 0.093f, dPy.z * 0.093f));
        float ripx = ((r1.x * 2 - 1) + (r2.x * 2 - 1) * 0.7f) * 0.17f, ripy = ((r1.y * 2 - 1) + (r2.y * 2 - 1) * 0.7f) * 0.17f;
        var ns = new Vector3(-dx * WAmp * 2 + ripx, 1, -dz * WAmp * 2 + ripy).normalized;
        var Ld = new Vector3(0.383797f, 0.743145f, -0.54812f).normalized;
        float ndl = Math.Max(Vector3.Dot(n, Ld), 0);
        float spec = (float)Math.Pow(Math.Max(Vector3.Dot(ns, (Ld + V).normalized), 0), 44.8) * 0.8f;
        var col = Mul(albedo, WSunC * ndl + WAmb) + WSunC * (0.5f * spec);
        float fres = (float)Math.Pow(1 - Math.Max(V.y, 0), 5);
        var refl = new Vector3(-V.x, V.y, -V.z);     // reflect(−V, up)
        float glow = (float)Math.Pow(Math.Max(Vector3.Dot(refl, WSheen), 0), 10);
        float glint = (float)Math.Pow(Math.Max(Vector3.Dot(ns, (WSheen + V).normalized), 0), 60);
        col = LerpV(col, WSky, fres * 0.45f) + WSheenC * (glow * 0.16f + glint * 0.26f);
        float foam = SS(0.86f, 0.99f, shore) * (0.55f + 0.45f * (float)Math.Sin(t * 1.6f + (ux + uz) * 0.9f + shore * 18));
        col = LerpV(col, new Vector3(0.93f, 0.95f, 0.92f), foam * 0.22f);
        alpha = Lerp(WAlpha, WAlpha * 0.75f, shore);
        return col;
    }
    // 环形纹理（art.js ringTexture）
    static float RingAlpha(float d) { float a = Clamp01(1 - Math.Abs(d - 0.82f) / 0.12f); return a * a + Clamp01(0.8f - d) * 0.18f; }
    // 天空穹顶（Sky.shader）：d 为 Unity 方向
    static Vector3 Sky(Vector3 dU)
    {
        var d = new Vector3(dU.x, dU.y, -dU.z).normalized; float y = d.y;
        Vector3 top = new Vector3(0.32f, 0.55f, 0.86f), hor = new Vector3(0.93f, 0.88f, 0.78f), bot = new Vector3(0.55f, 0.62f, 0.66f);
        var c = y > 0 ? LerpV(hor, top, (float)Math.Pow(Clamp01(y), 0.55)) : LerpV(hor, bot, Clamp01(-y * 3));
        float s = Clamp01(Vector3.Dot(d, new Vector3(0.3f, 0.4f, -0.85f).normalized));
        return c + new Vector3(1, 0.9f, 0.7f) * ((float)Math.Pow(s, 64) * 0.9f + (float)Math.Pow(s, 6) * 0.18f);
    }

    // 子像素 (px, py) 处的位置与相邻像素（最终分辨率，相隔 SSK 个子像素）的位置差：视线与三角形平面求交
    static void Derivs(Tri t, int px, int py, out Vector3 dPx, out Vector3 dPy)
    {
        var n = t.gn; float nd = Vector3.Dot(t.a.p - camPos, n);
        var p0 = RayPlane(px + 0.5f, py + 0.5f, n, nd);
        dPx = RayPlane(px + 0.5f + SSK, py + 0.5f, n, nd) - p0; dPy = RayPlane(px + 0.5f, py + 0.5f + SSK, n, nd) - p0;
        // 掠射角时限制在合理范围（GPU 在三角形内取有限差分，不会无穷大）
        float lim = 6;
        if (dPx.magnitude > lim) dPx = dPx.normalized * lim;
        if (dPy.magnitude > lim) dPy = dPy.normalized * lim;
    }
    static Vector3 RayPlane(float sx, float sy, Vector3 n, float nd)
    {
        var d = RayDir(sx, sy); float dn = Vector3.Dot(d, n);
        if (Math.Abs(dn) < 1e-9f) dn = dn < 0 ? -1e-9f : 1e-9f;
        return camPos + d * (nd / dn);
    }
    static Vt Bary(Tri t, float b0, float b1, float b2)
    {
        var a = t.a; var b = t.b; var c = t.c;
        return new Vt
        {
            p = a.p * b0 + b.p * b1 + c.p * b2, n = (a.n * b0 + b.n * b1 + c.n * b2).normalized,
            c = a.c * b0 + b.c * b1 + c.c * b2, a = a.a * b0 + b.a * b1 + c.a * b2, b = a.b * b0 + b.b * b1 + c.b * b2,
            u = a.u * b0 + b.u * b1 + c.u * b2, v = a.v * b0 + b.v * b1 + c.v * b2,
        };
    }
    static Vector3 ShadeOpaque(Tri t, Vt f, int px, int py, float z)
    {
        var m = t.m;
        switch (m.kind)
        {
            case K_TERRAIN:
            {
                Vector3 dPx, dPy; Derivs(t, px, py, out dPx, out dPy);
                Vector3 WN;
                var alb = TerrainAlbedo(f.p, f.n, f.c, f.a, f.b, dPx, dPy, out WN);
                return Shoulder(HalfLambert(alb, WN, t.recv ? Atten(f.p, WN) : 1));
            }
            case K_MASON:
            {
                var alb = Masonry(new Vector3(f.c.x, f.c.y, f.c.z), f.p, f.n, z);
                return HalfLambert(alb, f.n, t.recv ? Atten(f.p, f.n) : 1);
            }
            case K_FIG:
                return ShadeFigure(t, f.p, f.n, f.u, f.v, new Vector3(f.c.x, f.c.y, f.c.z), f.c.w);
            default:
                return HalfLambert(new Vector3(f.c.x, f.c.y, f.c.z), f.n, t.recv ? Atten(f.p, f.n) : 1);
        }
    }

    // ---- Sanguo/Figure 着色器（SgStandard）：Tests~/figure/FigureRender.cs 的 ShadeFigure（逐行对照）换成战场的灯光——
    //      太阳（SunC，方向 SunL）、半球光（Hemi）、环境贴图 × 0.85（FigAtlas.MakeEnvCubes）、轮廓光（镜头空间右上后方）；人物不接受阴影
    static readonly Vector3 RimDirView = new Vector3(0.55f, 0.62f, -0.56f).normalized;
    static Vector3 rimDirW;
    static Vector3 Fresnel(Vector3 f0, float dotVH) { float fr = (float)Math.Pow(2, (-5.55473 * dotVH - 6.98316) * dotVH); return f0 * (1 - fr) + new Vector3(fr, fr, fr); }
    static float SpecVD(float alpha, float dotNL, float dotNV, float dotNH)
    {
        float a2 = alpha * alpha;
        float gv = dotNL * (float)Math.Sqrt(a2 + (1 - a2) * dotNV * dotNV), gl = dotNV * (float)Math.Sqrt(a2 + (1 - a2) * dotNL * dotNL);
        float vis = 0.5f / Math.Max(gv + gl, 1e-6f), dn = dotNH * dotNH * (a2 - 1) + 1;
        return vis * a2 / ((float)Math.PI * dn * dn);
    }
    static void DFG(float dotNV, float rough, out float fa, out float fb)
    {
        float rx = rough * -1 + 1, ry = rough * -0.0275f + 0.0425f, rz = rough * -0.572f + 1.04f, rw = rough * 0.022f - 0.04f;
        float a004 = Math.Min(rx * rx, (float)Math.Pow(2, -9.28 * dotNV)) * rx + ry;
        fa = -1.04f * a004 + rz; fb = 1.04f * a004 + rw;
    }
    static readonly double[] envO = new double[3];
    static Vector3 EnvRad(Vector3 d, double lod) { envC.Radiance(d.x, d.y, d.z, lod, envO); return new Vector3((float)envO[0], (float)envO[1], (float)envO[2]); }
    static Vector3 EnvIrr(Vector3 n) { envC.Irradiance(n.x, n.y, n.z, envO); return new Vector3((float)envO[0], (float)envO[1], (float)envO[2]); }
    static Vector3 Reflect(Vector3 i, Vector3 n) { return i - n * (2 * Vector3.Dot(n, i)); }
    static Vector3 ShadeFigure(Tri t, Vector3 p, Vector3 nGeo, float u, float v, Vector3 vc, float va)
    {
        var m = t.m;
        var tex = m.map.S(u, v, t.lod); var nm = m.nrm.S(u, v, t.lod); var orm = m.orm.S(u, v, t.lod);
        var vcol = LerpV(vc, Mul(vc, m.team), va);
        var albedo = Mul(new Vector3(tex.x, tex.y, tex.z), LerpV(Vector3.one, vcol, tex.w));
        float rough = Math.Min(Math.Max(orm.y, 0.0525f) + t.gR, 1), metal = orm.z;
        float ao = 1 + (orm.x - 1) * 0.8f;
        var n = nGeo;
        var V0 = (camPos - p).normalized;
        var mn = new Vector3(nm.x * 2 - 1, nm.y * 2 - 1, nm.z * 2 - 1).normalized;
        if (t.T.sqrMagnitude > 0 && t.B.sqrMagnitude > 0)
        {
            var T = (t.T - n * Vector3.Dot(n, t.T)).normalized;
            var Bc = Vector3.Cross(n, T); if (Vector3.Dot(Bc, t.B) < 0) Bc = -Bc;
            n = (T * mn.x + Bc * mn.y + n * mn.z).normalized;
        }
        var diffC = albedo * (1 - metal);
        var specC = LerpV(new Vector3(0.04f, 0.04f, 0.04f), albedo, metal);
        float dotNV = Clamp01(Vector3.Dot(n, V0));
        // 直接光：太阳
        var H = (SunL + V0).normalized;
        float dotNL = Clamp01(Vector3.Dot(n, SunL)), dotNH = Clamp01(Vector3.Dot(n, H)), dotVH = Clamp01(Vector3.Dot(V0, H));
        var spec = Fresnel(specC, dotVH) * (SpecVD(rough * rough, dotNL, dotNV, dotNH) * (float)Math.PI);
        var col = Mul(SunC, diffC + spec) * dotNL;
        // 环境：半球光 + 环境贴图（× 0.85）
        var hemi = Hemi(n);
        var irr = EnvIrr(n) * 0.85f;
        var R = LerpV(Reflect(-V0, n), n, rough * rough).normalized;
        var rad = EnvRad(R, FigAtlas.EnvCubes.Lod(rough)) * 0.85f;
        float fa, fb; DFG(dotNV, rough, out fa, out fb);
        var FssEss = specC * fa + new Vector3(fb, fb, fb);
        float Ems = 1 - (fa + fb);
        var Favg = specC + (Vector3.one - specC) * 0.047619f;
        var ms = new Vector3(FssEss.x * Favg.x / (1 - Ems * Favg.x), FssEss.y * Favg.y / (1 - Ems * Favg.y), FssEss.z * Favg.z / (1 - Ems * Favg.z)) * Ems;
        var tot = FssEss + ms;
        var diffE = diffC * (1 - Math.Max(tot.x, Math.Max(tot.y, tot.z)));
        float specAO = Clamp01((float)Math.Pow(dotNV + ao, Math.Pow(2, -16 * rough - 1)) - 1 + ao);
        col = col + (Mul(hemi, diffC) + Mul(diffE, irr)) * ao + (Mul(rad, FssEss) + Mul(ms, irr)) * specAO;
        // 轮廓光（自发光）
        float sgR = 1 - dotNV, sgL = Clamp01(Vector3.Dot(n, rimDirW) * 0.5f + 0.5f), sgR2 = sgR * sgR;
        var rimC = new Vector3(1.0f, 0.9f, 0.78f) * (sgR2 * sgR2 * 0.42f * sgL * sgL * 1.6f);
        col = col + Mul(rimC, new Vector3(0.2f, 0.2f, 0.2f) + albedo * 0.6f) * (1 + (0.45f - 1) * orm.y);
        return col;
    }

    // ------------------------------------------------------------ 人物（FigBuild / FigRig，Unity 坐标）
    static readonly Dictionary<string, FigTemplate> tplCache = new Dictionary<string, FigTemplate>();
    static readonly Dictionary<string, FigHorseTemplate> htplCache = new Dictionary<string, FigHorseTemplate>();
    static readonly Dictionary<string, Mat> faceCache = new Dictionary<string, Mat>();
    static M4d U(M4d threeM) { return FigConv.M(threeM); }
    static M4d FromUnity(Matrix4x4 m)
    {
        var r = new M4d(); var e = r.e;
        for (int c = 0; c < 4; c++) for (int rr = 0; rr < 4; rr++) e[c * 4 + rr] = m[rr, c];
        return r;
    }
    static Vector3 MulP(M4d m, double x, double y, double z) { var e = m.e; return new Vector3((float)(e[0] * x + e[4] * y + e[8] * z + e[12]), (float)(e[1] * x + e[5] * y + e[9] * z + e[13]), (float)(e[2] * x + e[6] * y + e[10] * z + e[14])); }
    static Vector3 MulD(M4d m, double x, double y, double z) { var e = m.e; return new Vector3((float)(e[0] * x + e[4] * y + e[8] * z), (float)(e[1] * x + e[5] * y + e[9] * z), (float)(e[2] * x + e[6] * y + e[10] * z)); }
    static void AddGeo(FigGeo g, Mat m, M4d world, M4d[] skin, bool ground)
    {
        int n = g.Verts;
        var vs = new Vt[n];
        for (int i = 0; i < n; i++)
        {
            double x = -g.pos[i * 3], y = g.pos[i * 3 + 1], z = g.pos[i * 3 + 2];
            double nx = -g.nrm[i * 3], ny = g.nrm[i * 3 + 1], nz = g.nrm[i * 3 + 2];
            double sx = x, sy = y, sz = z, snx = nx, sny = ny, snz = nz;
            if (skin != null)
            {
                sx = sy = sz = snx = sny = snz = 0;
                for (int k = 0; k < 4; k++)
                {
                    double w = g.sw[i * 4 + k]; if (w == 0) continue;
                    var e = skin[g.si[i * 4 + k]].e;
                    sx += (e[0] * x + e[4] * y + e[8] * z + e[12]) * w; sy += (e[1] * x + e[5] * y + e[9] * z + e[13]) * w; sz += (e[2] * x + e[6] * y + e[10] * z + e[14]) * w;
                    snx += (e[0] * nx + e[4] * ny + e[8] * nz) * w; sny += (e[1] * nx + e[5] * ny + e[9] * nz) * w; snz += (e[2] * nx + e[6] * ny + e[10] * nz) * w;
                }
                if (ground) sy = Math.Max(sy, 0.006);     // 地面贴合（着色器）：人物局部 y ≥ 0.006
            }
            vs[i] = new Vt { p = MulP(world, sx, sy, sz), n = MulD(world, snx, sny, snz).normalized, u = g.uv[i * 2], v = g.uv[i * 2 + 1], c = new Vector4(g.col[i * 4], g.col[i * 4 + 1], g.col[i * 4 + 2], g.col[i * 4 + 3]) };
        }
        var idx = FigConv.Tris(g.idx);
        for (int i = 0; i < idx.Length; i += 3)
        {
            Add(opaque, vs[idx[i]], vs[idx[i + 1]], vs[idx[i + 2]], m, true, false);
            // 切线（= Mesh.RecalculateTangents：由位置与 UV 求 dP/du、dP/dv）
            var t = opaque[opaque.Count - 1];
            Vector3 e1 = t.b.p - t.a.p, e2 = t.c.p - t.a.p;
            float du1 = t.b.u - t.a.u, dv1 = t.b.v - t.a.v, du2 = t.c.u - t.a.u, dv2 = t.c.v - t.a.v, det = du1 * dv2 - du2 * dv1;
            if (Math.Abs(det) > 1e-12f) { t.T = (e1 * dv2 - e2 * dv1) * (1 / det); t.B = (e2 * du1 - e1 * du2) * (1 / det); }
        }
    }
    static Mat AtlasMat(string hex) { return new Mat { kind = K_FIG, map = atlas, nrm = atlasNrm, orm = atlasOrm, team = Col(hex) }; }
    static Mat FaceMat(FigLook look, FigTemplate tpl)
    {
        var sp = look.spec;
        string fk = ((sp != null ? (sp.S("key") ?? sp.S("name")) : null) ?? "?") + "|" + tpl.faceSize;
        Mat fm;
        if (!faceCache.TryGetValue(fk, out fm))
        {
            var face = FigAtlas.PaintFace(FigFaceDesc.FromSpec(sp ?? new JObj()), tpl.faceSize);
            faceCache[fk] = fm = new Mat { kind = K_FIG, map = new Tex(face.ToRGBA8(), face.W, face.H, false, true), nrm = flatNrm, orm = flatOrm, team = Vector3.one };
        }
        return fm;
    }
    static FigTemplate Tpl(FigLook look, string key)
    {
        FigTemplate tpl;
        if (!tplCache.TryGetValue(key, out tpl)) { look.key = key; tpl = FigBuild.buildTemplate(look); tplCache[key] = tpl; }
        return tpl;
    }
    static FigHorseTemplate HTpl(FigHorseOpts ho)
    {
        string k = ho.Key() + "|" + (ho.culture ?? "");
        FigHorseTemplate t;
        if (!htplCache.TryGetValue(k, out t)) htplCache[k] = t = FigBuild.buildHorseTemplate(ho);
        return t;
    }
    // 一个人物（可骑马）：root = 部队 / 士兵的 Unity 矩阵；骑马时 horseRoot 为战马根
    static void AddFigure(FigLook look, FigTemplate tpl, string hex, M4d root, FigHorseOpts ho, M4d horseRoot, string clip, double tt)
    {
        var rig = new FigRig(tpl);
        M4d riderRoot = root;
        if (ho != null)
        {
            var ht = HTpl(ho);
            var hr = new FigHorseRig(ht);
            rig.mounted = hr; hr.riders.Add(rig);
            rig.SetPose("idle", 0, null);
            hr.SetPose("idle", tt);
            rig.SetPose(clip, tt, new FigPoseOpts { gallop = false });
            var hm = hr.BoneMatrices();
            var hskin = new M4d[hm.Length];
            for (int i = 0; i < hm.Length; i++) hskin[i] = U(M4d.Mul(hm[i], ht.inv[i]));
            AddGeo(ht.geometry, AtlasMat(ho.color ?? hex), horseRoot, hskin, false);
            var off = FigHorseRig.RiderOffset(rig);
            riderRoot = M4d.Mul(horseRoot, U(M4d.Mul(hm[FigHorseRig.Saddle], M4d.Translation(off.x, off.y, off.z))));
        }
        else rig.SetPose(clip, tt, null);
        var bm = rig.BoneMatrices();
        var skin = new M4d[bm.Length];
        for (int i = 0; i < bm.Length; i++) skin[i] = U(M4d.Mul(bm[i], tpl.boneInverses[i]));
        var mat = AtlasMat(hex);
        AddGeo(tpl.geometry, mat, riderRoot, skin, rig.groundClamp && rig.mounted == null);
        Func<int, M4d> boneW = i => M4d.Mul(riderRoot, U(bm[i]));
        if (tpl.headGeo != null) AddGeo(tpl.headGeo, FaceMat(look, tpl), boneW(FigBones.head), null, false);
        if (tpl.weaponGeo != null)
        {
            var gR = M4d.Compose(rig.gripR.p, rig.gripR.q, new V3d(1, 1, 1)); var gL = M4d.Compose(rig.gripL.p, rig.gripL.q, new V3d(1, 1, 1));
            bool bow = rig.style == "bow";
            AddGeo(tpl.weaponGeo, mat, M4d.Mul(boneW(bow ? FigBones.handL : FigBones.handR), U(bow ? gL : gR)), null, false);
            if (rig.style == "dual") AddGeo(tpl.weaponGeo, mat, M4d.Mul(boneW(FigBones.handL), U(gL)), null, false);
        }
        if (tpl.shieldGeo != null)
            AddGeo(tpl.shieldGeo, mat, M4d.Mul(boneW(FigBones.foreArmL), U(M4d.Compose(rig.shield.p, rig.shield.q, new V3d(1, 1, 1)))), null, false);
    }
    static string Hex(Color c)
    {
        Func<float, int> b = v => (int)Math.Floor(Mathf.Clamp01(v) * 255 + 0.5);
        return "#" + b(c.r).ToString("x2") + b(c.g).ToString("x2") + b(c.b).ToString("x2");
    }
    static M4d TRS(Vector3 p, float yawDeg, float s) { return FromUnity(Matrix4x4.TRS(p, Quaternion.Euler(0, yawDeg, 0), new Vector3(s, s, s))); }

    // 部队：主将（中模，骑马）+ 士兵（低模）+ 阵营光环；返回部队的贴地位置（树的淡出用）
    static Vector3 AddUnit(GameState g, BattleModel m, BattleBoard B, BattleLook.Look look, BUnit bu, Color col)
    {
        string hex = Hex(col);
        string culture = BoardHarness.CultureOf(m, look, bu.side);
        var t = B.Tile(bu.x, bu.y);
        var gp = new Vector3(t.x, (float)Math.Max(t.y, B.GroundY(t.x, t.z)), t.z);
        float yaw = bu.side == 0 ? 90 : -90;
        // 主将与战马：figRoot 缩放 0.7，战马根在 (0, 0, 0.36 / 0.7)
        var gen = bu.gen;
        var pg = new PortraitGen { name = gen.name, war = gen.war, intel = gen.intel, pol = gen.pol, culture = gen.culture, sex = gen.sex };
        if (gen.born.HasValue && gen.born.Value > 0) pg.born = gen.born.Value;
        var fo = new FigOpts { color = hex, lod = "mid", mounted = true };
        var look0 = FigBuild.lookOfGeneral(pg, fo);
        var tpl = Tpl(look0, FigBuild.generalKey(look0, fo));
        var ho = new FigHorseOpts
        {
            color = hex, lod = "mid", seed = Math.Abs(gen.id) % 5, culture = culture, general = true,
            armor = culture == "persia" || culture == "sarmatian" ? "scale" : "cloth",
        };
        var figRoot = TRS(gp, yaw, BattleBoard.FIG_S);
        var horseRoot = M4d.Mul(figRoot, M4d.Translation(0, 0, 0.36 / BattleBoard.FIG_S));
        var idle = FigClips.Info("idle");
        double ut = (Math.Abs(gen.id) * 0.37) % 1 * (idle != null ? idle.dur : 1);
        AddFigure(look0, tpl, hex, horseRoot, ho, horseRoot, "idle", ut);
        // 士兵（FigureCrowd 的低模；位置 = 部队位置 + 旋转后的队形偏移，贴地）
        int nb = BattleBoard.SoldierBucket(bu.Troops, bu.alive), ns = BattleBoard.SoldierCount(nb, culture);
        var slots = BattleBoard.SoldierSlots(culture, ns);
        string cul = BattleBoard.FigCult(culture);
        float lift = gp.y - (float)B.GroundY(gp.x, gp.z);
        var rot = Quaternion.Euler(0, yaw, 0);
        for (int i = 0; i < slots.Count; i++)
        {
            var q = slots[i];
            var w = gp + rot * new Vector3((float)q.x, 0, (float)-q.z);
            var sp = new Vector3(w.x, (float)B.GroundY(w.x, w.z) + Math.Max(0, lift), w.z);
            var root = TRS(sp, yaw, BattleBoard.FIG_S);
            int seed = (Math.Abs(gen.id) + i) % 2;
            var so = new FigOpts { type = q.mounted ? "cavalry" : q.type, culture = cul, color = hex, seed = seed, lod = "low" };
            var lk = FigBuild.lookOfSoldier(so);
            var stpl = Tpl(lk, FigBuild.soldierKey(lk));
            double jt = ((i * 0.37) % 1) * (idle != null ? idle.dur : 1);
            FigHorseOpts sho = null;
            if (q.mounted) sho = new FigHorseOpts { lod = "low", seed = seed % 2, armor = lk.culture == "persia" || lk.culture == "sarmatian" ? "scale" : "cloth", shadow = false, color = hex };
            AddFigure(lk, stpl, hex, root, sho, root, "idle", jt);
        }
        // 阵营光环（Unlit，环形纹理 × 0.7；半径 0.95，高出地面 ringY）
        double ring = t.y; for (int k = 0; k < 8; k++) { double a = k * Math.PI / 4; ring = Math.Max(ring, B.GroundY(t.x + Math.Cos(a) * 0.9, t.z + Math.Sin(a) * 0.9)); }
        float ringY = 0.04f + (float)Math.Min(0.45, Math.Max(0, ring - t.y));
        var rc = bu.side == 0 ? new Vector3(0.45f, 0.75f, 1f) : new Vector3(1f, 0.5f, 0.42f);
        var rm = new Mat { kind = K_RING, col = rc, alpha = 0.7f, center = gp + Vector3.up * ringY, radius = 0.95f };
        float r0 = 0.95f; var cy = gp.y + ringY;
        Func<float, float, Vt> rv = (x, z) => new Vt { p = new Vector3(gp.x + x, cy, gp.z + z), n = Vector3.up, c = Vector4.one };
        Vt v0 = rv(-r0, -r0), v1 = rv(-r0, r0), v2 = rv(r0, r0), v3 = rv(r0, -r0);
        Add(rings, v0, v1, v2, rm, false, false); Add(rings, v0, v2, v3, rm, false, false);
        return gp;
    }

    // BattleView.UpdatePropFade 的目标值（固定步长 90 帧后已收敛）
    static void PropFade(BattleBoard B, List<Vector3> unitPos)
    {
        const float T = BattleBoard.T;
        foreach (var it in B.fadeItems) it.t = 0;
        var cp = camPos;
        foreach (var p in unitPos)
        {
            double cx = cp.x - p.x, cz = cp.z - p.z;
            double l = Math.Sqrt(cx * cx + cz * cz); if (l == 0) l = 1; cx /= l; cz /= l;
            double tanEl = Math.Max(0.3, (cp.y - p.y) / l);
            int ti = BattleBoard.JsRound((p.x - BattleBoard.Origin.x) / T - 0.5), tj = BattleBoard.JsRound((p.z - BattleBoard.Origin.z) / T - 0.5);
            for (int dj = -3; dj <= 3; dj++)
                for (int di = -3; di <= 3; di++)
                {
                    List<BattleBoard.FadeItem> Lst;
                    if (!B.fadeByTile.TryGetValue(BattleBoard.TileKey(ti + di, tj + dj), out Lst)) continue;
                    foreach (var it in Lst)
                    {
                        double ox = it.x - p.x, oz = it.z - p.z, along = ox * cx + oz * cz, perp = Math.Abs(ox * cz - oz * cx);
                        if (along < -0.3 * T || along > (2.3 * it.s + 0.5) / tanEl + 0.3 * T) continue;
                        double w = 0.75 * T * Math.Max(0.8, it.s) + 0.25;
                        if (perp < w) it.t = Math.Max(it.t, (float)Math.Min(1, Math.Max(0, (w - perp) / (0.35 * T))));
                    }
                }
        }
        foreach (var it in B.fadeItems) it.cur = it.t;
    }

    // ------------------------------------------------------------ 一个用例
    static void RenderCase(GameState g, Dictionary<string, object> cs, Dictionary<string, object> web, string outPng)
    {
        double t0 = FigMath.Now();
        opaque.Clear(); fades.Clear(); waters.Clear(); rings.Clear();
        var m = BoardHarness.ModelFor(g, web, (string)cs["city"], (string)cs["from"]);
        var look = BattleLook.Of(BattleLook.CultureOf(m.setup));
        Color atk = BoardHarness.FactionCol(g, m.setup.attacker), def = BoardHarness.FactionCol(g, m.setup.defender);
        var B = new BattleBoard(m, look, atk, def);
        // 镜头（three → Unity：位置 z 取反；前方 = q·(0, 0, −1)、上方 = q·(0, 1, 0)，再 z 取反）
        var cam = O(web["cam"]); var cpos = L(cam["pos"]); var q = L(cam["quat"]);
        double qx = D(q[0]), qy = D(q[1]), qz = D(q[2]), qw = D(q[3]);
        Func<double, double, double, Vector3> rotQ = (x, y, z) =>
        {
            // v' = q v q*
            double ix = qw * x + qy * z - qz * y, iy = qw * y + qz * x - qx * z, iz = qw * z + qx * y - qy * x, iw = -qx * x - qy * y - qz * z;
            return new Vector3((float)(ix * qw + iw * -qx + iy * -qz - iz * -qy), (float)(iy * qw + iw * -qy + iz * -qx - ix * -qz), (float)(iz * qw + iw * -qz + ix * -qy - iy * -qx));
        };
        var f3 = rotQ(0, 0, -1); var u3 = rotQ(0, 1, 0);
        camPos = new Vector3((float)D(cpos[0]), (float)D(cpos[1]), -(float)D(cpos[2]));
        camF = new Vector3(f3.x, f3.y, -f3.z).normalized; camU = new Vector3(u3.x, u3.y, -u3.z).normalized;
        camR = Vector3.Cross(camU, camF).normalized;
        // 轮廓光方向：镜头空间 → 世界（x = camR、y = camU、z = 指向镜头 = −camF）
        rimDirW = (camR * RimDirView.x + camU * RimDirView.y + camF * -RimDirView.z).normalized;
        tanV = (float)Math.Tan(D(cam["fov"]) / 2 * Math.PI / 180); tanH = tanV * OW / OH;
        float dist = cs.ContainsKey("dist") ? (float)D(cs["dist"]) : 46;
        var target = camPos + camF * dist;
        boardRect = new Vector4(BattleBoard.Origin.x, BattleBoard.Origin.z, B.W, B.H);

        // ---- 场景
        // 地形网格
        for (int i = 0; i < B.tTri.Length; i += 3)
        {
            Func<int, Vt> tv = k => new Vt { p = B.tPos[k], n = B.tNrm[k], c = V4(B.tCol[k]), a = B.tMat[k], b = B.tBt[k] };
            Add(opaque, tv(B.tTri[i]), tv(B.tTri[i + 1]), tv(B.tTri[i + 2]), mTerrain, false, true);
        }
        // 裙边：陆地 = 地形材质（aMat = (0, 0, 0, 1)、aBt = 0），海面 = 水面材质（同水面的顶点起伏）
        for (int i = 0; i < B.sTri.Length; i += 3)
        {
            Func<int, Vt> sv = k =>
            {
                var p = B.sPos[k]; var c = B.sCol[k];
                if (B.seaSkirt) p.y += WaveH(p.x, p.z) * WAmp * (1 - c.g);
                return new Vt { p = p, n = Vector3.up, c = V4(c), a = new Vector4(0, 0, 0, 1) };
            };
            if (B.seaSkirt) Add(waters, sv(B.sTri[i]), sv(B.sTri[i + 1]), sv(B.sTri[i + 2]), mWater, false, false);
            else Add(opaque, sv(B.sTri[i]), sv(B.sTri[i + 1]), sv(B.sTri[i + 2]), mTerrain, false, true);
        }
        // 水面（顶点起伏：g = 1 的远海不起伏）
        for (int i = 0; i < B.wTri.Count; i += 3)
        {
            Func<int, Vt> wv = k =>
            {
                var p = B.wPos[k]; var c = B.wCol[k];
                p.y += WaveH(p.x, p.z) * WAmp * (1 - c.g);
                return new Vt { p = p, n = Vector3.up, c = V4(c) };
            };
            Add(waters, wv(B.wTri[i]), wv(B.wTri[i + 1]), wv(B.wTri[i + 2]), mWater, false, false);
        }
        // 城池与桥（砖石）、船
        AddMB(opaque, B.Fort, Matrix4x4.identity, Vector3.one, Vector4.one, mMason, true, true);
        if (B.Boats != null) AddMB(opaque, B.Boats, Matrix4x4.identity, Vector3.one, Vector4.one, mLow, true, true);
        // 部队（先算位置：树的淡出要用）
        var unitPos = new List<Vector3>();
        foreach (var bu in m.units)
        {
            if (!bu.alive) continue;
            unitPos.Add(AddUnit(g, m, B, look, bu, bu.side == 0 ? atk : def));
        }
        PropFade(B, unitPos);
        var fadeOf = new Dictionary<long, float>();
        foreach (var it in B.fadeItems) if (it.cur > 0) fadeOf[((long)it.batch << 32) | (uint)it.k] = it.cur;
        // 实例化的树、石、草与点缀
        int nInst = 0;
        for (int bi = 0; bi < B.Props.Count; bi++)
        {
            var pb = B.Props[bi];
            var tplm = BattleBoard.PropTemplate(pb.kind);
            for (int k = 0; k < pb.items.Count; k++)
            {
                var it = pb.items[k];
                var M = pb.MatrixOf(k);
                var sc = new Vector3(Mathf.Sqrt(M.m00 * M.m00 + M.m10 * M.m10 + M.m20 * M.m20), Mathf.Sqrt(M.m01 * M.m01 + M.m11 * M.m11 + M.m21 * M.m21), Mathf.Sqrt(M.m02 * M.m02 + M.m12 * M.m12 + M.m22 * M.m22));
                float fd;
                if (pb.fade && fadeOf.TryGetValue(((long)bi << 32) | (uint)k, out fd)) AddMB(fades, tplm, M, sc, new Vector4(it.r, it.g, it.b, 1), mLow, pb.castShadow, false, 1 - 0.75f * Clamp01(fd));
                else AddMB(opaque, tplm, M, sc, new Vector4(it.r, it.g, it.b, 1), mLow, pb.castShadow, pb.receiveShadow);
                nInst++;
            }
        }
        double tScene = FigMath.Now() - t0;

        // ---- 阴影
        float radius = (float)Math.Ceiling(Mathf.Clamp(Math.Max(30, dist * 0.95f), 16, 170) / 4) * 4;
        BuildShadow(target, radius);
        double tShadow = FigMath.Now() - t0 - tScene;

        // ---- 不透明物：三角形号 + 重心坐标
        int NS = W * H;
        gTri = new int[NS]; gZ = new float[NS]; gB0 = new float[NS]; gB1 = new float[NS];
        for (int i = 0; i < NS; i++) { gTri[i] = -1; gZ[i] = float.MaxValue; }
        drawn = culled = 0;
        for (int id = 0; id < opaque.Count; id++)
        {
            var t = opaque[id];
            if (t.m.kind == K_FIG) t.lod = FigLod(t);
            int tid = id;
            RasterTri(t.a.p, t.b.p, t.c.p, true, (o, z, b0, b1, b2) =>
            {
                if (z >= gZ[o]) return;
                gZ[o] = z; gTri[o] = tid; gB0[o] = b0; gB1[o] = b1;
            });
        }
        // ---- 着色
        colBuf = new float[NS * 3];
        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                int o = py * W + px;
                Vector3 c;
                if (gTri[o] < 0) c = Sky(RayDir(px + 0.5f, py + 0.5f));
                else
                {
                    var t = opaque[gTri[o]];
                    float b0 = gB0[o], b1 = gB1[o];
                    var f = Bary(t, b0, b1, 1 - b0 - b1);
                    c = ShadeOpaque(t, f, px, py, gZ[o]);
                    c = LerpV(c, FogC, Fog(gZ[o]));
                }
                colBuf[o * 3] = c.x; colBuf[o * 3 + 1] = c.y; colBuf[o * 3 + 2] = c.z;
            }
        // ---- 半透明：水面（3001）→ 阵营光环（3002）→ 淡去的树（3005：深度前置遍 + 混合）
        foreach (var t in waters)
        {
            var tt = t;
            RasterTri(t.a.p, t.b.p, t.c.p, true, (o, z, b0, b1, b2) =>
            {
                if (z >= gZ[o]) return;
                var f = Bary(tt, b0, b1, b2);
                Vector3 dPx, dPy; Derivs(tt, o % W, o / W, out dPx, out dPy);
                float a;
                var c = WaterShade(f.p, f.c.x, dPx, dPy, out a);
                c = LerpV(c, FogC, Fog(z));
                Blend(o, c, a);
            });
        }
        foreach (var t in rings)
        {
            var tt = t;
            RasterTri(t.a.p, t.b.p, t.c.p, false, (o, z, b0, b1, b2) =>
            {
                if (z >= gZ[o] + 0.02f) return;     // Unlit 的深度偏移
                var f = Bary(tt, b0, b1, b2);
                var dd = f.p - tt.m.center; float d = (float)Math.Sqrt(dd.x * dd.x + dd.z * dd.z) / tt.m.radius;
                float a = RingAlpha(d) * tt.m.alpha;
                if (a <= 0) return;
                Blend(o, LerpV(tt.m.col, FogC, Fog(z)), a);
            });
        }
        if (fades.Count > 0)
        {
            fTri = new int[NS]; fZ = new float[NS]; fB0 = new float[NS]; fB1 = new float[NS];
            for (int i = 0; i < NS; i++) { fTri[i] = -1; fZ[i] = float.MaxValue; }
            for (int id = 0; id < fades.Count; id++)
            {
                var t = fades[id]; int tid = id;
                RasterTri(t.a.p, t.b.p, t.c.p, true, (o, z, b0, b1, b2) =>
                {
                    if (z >= gZ[o] || z >= fZ[o]) return;
                    fZ[o] = z; fTri[o] = tid; fB0[o] = b0; fB1[o] = b1;
                });
            }
            for (int o = 0; o < NS; o++)
            {
                if (fTri[o] < 0) continue;
                var t = fades[fTri[o]];
                var f = Bary(t, fB0[o], fB1[o], 1 - fB0[o] - fB1[o]);
                var c = HalfLambert(new Vector3(f.c.x, f.c.y, f.c.z), f.n, 1);
                Blend(o, LerpV(c, FogC, Fog(fZ[o])), t.alpha);
            }
        }
        // ---- 2×2 超采样 → PNG
        var outB = new byte[OW * OH * 4];
        for (int y = 0; y < OH; y++) for (int x = 0; x < OW; x++)
        {
            for (int c = 0; c < 3; c++)
            {
                double s = 0;
                for (int j = 0; j < SSK; j++) for (int i = 0; i < SSK; i++) s += Clamp01(colBuf[((y * SSK + j) * W + x * SSK + i) * 3 + c]);
                outB[(y * OW + x) * 4 + c] = (byte)Math.Round(s / (SSK * SSK) * 255);
            }
            outB[(y * OW + x) * 4 + 3] = 255;
        }
        FigPng.Save(outPng, outB, OW, OH);
        Console.WriteLine(string.Format("  {0}：不透明 {1} + 淡去 {2} + 水 {3} 个三角形（实例 {4}，画 {5}、背面剔除 {6}）；建场景 {7:F0} ms、阴影 {8:F0} ms、共 {9:F0} ms",
            Path.GetFileName(outPng), opaque.Count, fades.Count, waters.Count, nInst, drawn, culled, tScene, tShadow, FigMath.Now() - t0));
    }
    static void Blend(int o, Vector3 c, float a)
    {
        colBuf[o * 3] = colBuf[o * 3] * (1 - a) + c.x * a; colBuf[o * 3 + 1] = colBuf[o * 3 + 1] * (1 - a) + c.y * a; colBuf[o * 3 + 2] = colBuf[o * 3 + 2] * (1 - a) + c.z * a;
    }
    // 人物三角形的 mip 级别：uv 对屏幕（最终分辨率）的仿射导数
    static float FigLod(Tri t)
    {
        Vector3 a = ToCam(t.a.p), b = ToCam(t.b.p), c = ToCam(t.c.p);
        if (a.z < CamNear || b.z < CamNear || c.z < CamNear) return 0;
        float X0 = a.x / (a.z * tanH) * 0.5f * OW, Y0 = -a.y / (a.z * tanV) * 0.5f * OH, X1 = b.x / (b.z * tanH) * 0.5f * OW, Y1 = -b.y / (b.z * tanV) * 0.5f * OH, X2 = c.x / (c.z * tanH) * 0.5f * OW, Y2 = -c.y / (c.z * tanV) * 0.5f * OH;
        float A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
        if (Math.Abs(A) < 1e-9f) return 8;
        float dux = ((t.b.u - t.a.u) * (Y2 - Y0) - (t.c.u - t.a.u) * (Y1 - Y0)) / A, duy = ((t.c.u - t.a.u) * (X1 - X0) - (t.b.u - t.a.u) * (X2 - X0)) / A;
        float dvx = ((t.b.v - t.a.v) * (Y2 - Y0) - (t.c.v - t.a.v) * (Y1 - Y0)) / A, dvy = ((t.c.v - t.a.v) * (X1 - X0) - (t.b.v - t.a.v) * (X2 - X0)) / A;
        // 几何粗糙度（three geometryRoughness：法线屏幕导数的最大分量；按最终分辨率）
        float gr = 0;
        for (int k = 0; k < 3; k++)
        {
            float a0 = t.a.n[k], a1 = t.b.n[k], a2 = t.c.n[k];
            float ddx = ((a1 - a0) * (Y2 - Y0) - (a2 - a0) * (Y1 - Y0)) / A, ddy = ((a2 - a0) * (X1 - X0) - (a1 - a0) * (X2 - X0)) / A;
            gr = Math.Max(gr, Math.Max(Math.Abs(ddx), Math.Abs(ddy)));
        }
        t.gR = gr;
        // 图集各向异性 4（figure.js 的 t.anisotropy = 4）
        return t.m.map.Lod(dux, dvx, duy, dvy, t.m.map == atlas ? 4 : 1);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string outDir = args.Length > 0 ? args[0] : "out";
        var only = new HashSet<string>(args.Skip(1));
        var cases = L(MiniJson.Parse(File.ReadAllText(Path.Combine(outDir, "cases.json"))));
        double t0 = FigMath.Now();
        UnityEngine.Random.InitState(7);
        var g = GameState.Current = GameState.NewGame("cao", "world");
        var tm = TerrainModel.BuildHeadless(g, "world");
        BattleTerrain.ModelSource = () => tm;
        detA = new Tex(Art.TerrainDetailBytesA, Art.TexN, Art.TexN, true, false);
        detB = new Tex(Art.TerrainDetailBytesB, Art.TexN, Art.TexN, true, false);
        ripple = new Tex(Art.RippleBytes(), 128, 128, true, false);
        var A = FigAtlas.Data;
        atlas = new Tex(A.map, FigAtlas.N, FigAtlas.N, false, false); atlasNrm = new Tex(A.nrm, FigAtlas.N, FigAtlas.N, false, false); atlasOrm = new Tex(A.orm, FigAtlas.N, FigAtlas.N, false, false);
        {
            var fN = new byte[16 * 4]; var fO = new byte[16 * 4];
            for (int i = 0; i < 16; i++) { fN[i * 4] = 128; fN[i * 4 + 1] = 128; fN[i * 4 + 2] = 255; fN[i * 4 + 3] = 255; fO[i * 4] = 255; fO[i * 4 + 1] = 178; fO[i * 4 + 2] = 0; fO[i * 4 + 3] = 255; }
            flatNrm = new Tex(fN, 4, 4, false, false); flatOrm = new Tex(fO, 4, 4, false, false);
        }
        envC = FigAtlas.MakeEnvCubes();
        Console.WriteLine(string.Format("地形模型、细节纹理与人物图集 {0:F0} ms", FigMath.Now() - t0));
        OW = 960; OH = 540; W = OW * SSK; H = OH * SSK;
        int n = 0;
        foreach (Dictionary<string, object> cs in cases)
        {
            string name = (string)cs["name"];
            if (only.Count > 0 && !only.Contains(name)) continue;
            string wp = Path.Combine(outDir, "web-" + name + ".json");
            if (!File.Exists(wp)) { Console.WriteLine("  缺网页版参考 " + wp); continue; }
            var web = O(MiniJson.Parse(File.ReadAllText(wp)));
            RenderCase(g, cs, web, Path.Combine(outDir, "unity-" + name + ".png"));
            n++;
        }
        return n > 0 ? 0 : 1;
    }
}
