// 三国志-霸王的世界 · 材质、程序化纹理、网格工具与常用模型 ← 网页版 js/art.js
//
// 第三版（DESIGN-V3 §1：平滑、细致）新增 / 改进（与网页版同名同参）：
//   MeshBuilder.TriN(a, b, c, na, nb, nc, ca, cb?, cc?)   逐顶点法线与颜色的三角形（正反面按法线自动确定）
//   MeshBuilder.Sphere(center, radius, col, detail = 1, opt)   平滑（可起伏的）椭球：二十面体细分，opt = seed / rough / top / flat / bottomShade / smoothN
//   MeshBuilder.Lathe(base, LP[] profile, seg, col, capTop, capBottom)   平滑旋转体（穹顶、塔、帐、树冠层）；LP = (r, y, 颜色?, 折角?)
//   MeshBuilder.SmoothCylinder(bottom, r0, r1, h, seg, col, cap)   MeshBuilder.Tube(points, radii, seg, col, capEnd)
//   MeshBuilder.ChineseRoof(...)   参数不变；凹曲屋面、翼角起翘、平滑法线并加鸱吻（所有用到它的场景一起变得细致）
//   Models.Tree(...)               参数不变；平滑树干与圆润树冠
//   Art.TerrainDetail / TerrainDetailGen()   程序化地形细节纹理 A / B（草、土、沙纹、岩石、农田地块、犁沟），与网页版逐字节相同
//   Art.TerrainMaterial(maps)      Sanguo/Terrain 着色器（按坡度 / 高度 / 群落混合草地、泥土、农田、沙、三平面投影岩石、雪、海床）。
//                                  maps = 战略地图的数据纹理（TerrainMaps）；maps = null 为顶点版：顶点色 = 群落底色，
//                                  uv2（TEXCOORD1，Vector4）= aMat（沙, 岩石倾向, 雪, 1 − 农田）
//   Art.MasonryMaterial(near, far) 城池 / 关隘 / 桥的砖石材质（Sanguo/Masonry：墙面错缝砖、坡面筒瓦、平顶方砖，按世界坐标程序化，随距离淡出）
//   Art.Foliage(kind)              植被 / 石块 / 村舍的实例化材质（Sanguo/Foliage：顶点色 a = sgLeaf 的部分乘实例颜色 _InstColor，叶簇 / 石纹明暗与法线起伏）
//   Art.Ripple                     可平铺的涟漪法线贴图（RG = 斜率），水面高光用
//   Art.EnsureAutoQuality()        自适应画质：持续掉帧（平均 > 28 ms）或最近 8 帧有 5 帧超过 150 ms 时依次降低渲染分辨率、阴影贴图尺寸
// 原有接口（Art.LowPoly / NewLowPoly / Water / Sky / NewUnlit / NewAdditive、MeshBuilder 的平面着色方法、Models.Soldier / Commander / City）不变。
//
// 坐标：MeshBuilder 一律接受 Unity 坐标（x 东、y 上、z 北），输出 Unity 网格（网页版的 MeshBuilder 接受同样的坐标再自行换手系）。
// 颜色：Gamma 工作流，颜色数值照抄网页版。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    // 战略地图的地形数据纹理（MapView 生成）：uv = 地图坐标 × zw + xy
    public sealed class TerrainMaps
    {
        public Texture2D n, c, m, oc, om;
        public Vector4 xfN, xfC, xfO, inner;
    }

    // 材质、程序化纹理与网格工具
    public static class Art
    {
        static Material lowPoly, water, unlit, additive, sky;
        static Texture2D softDot;

        public static Shader LoadShader(string name)
        {
            var s = Resources.Load<Shader>("Shaders/" + name);
            if (s == null) s = Shader.Find("Sanguo/" + name);
            if (s == null) { Debug.LogWarning("Missing shader " + name); s = Shader.Find("Diffuse"); }
            return s;
        }
        public static Material LowPoly { get { if (lowPoly == null) lowPoly = new Material(LoadShader("LowPoly")) { enableInstancing = true }; return lowPoly; } }
        public static Material NewLowPoly() { return new Material(LowPoly); }
        public static Material Water { get { if (water == null) water = new Material(LoadShader("Water")); return water; } }
        public static Material Sky { get { if (sky == null) sky = new Material(LoadShader("Sky")); return sky; } }
        public static Material NewUnlit(Color c, Texture tex = null)
        {
            if (unlit == null) unlit = new Material(LoadShader("Unlit"));
            var m = new Material(unlit) { color = c };
            if (tex != null) m.mainTexture = tex;
            return m;
        }
        public static Material NewAdditive(Color c)
        {
            if (additive == null) { additive = new Material(LoadShader("Additive")); additive.mainTexture = SoftDot; }
            return new Material(additive) { color = c };
        }

        public static Texture2D SoftDot
        {
            get
            {
                if (softDot != null) return softDot;
                int n = 64;
                softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1 - d); a = a * a;
                        softDot.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                softDot.Apply();
                return softDot;
            }
        }

        // 圆环纹理（选择光圈）
        static Texture2D ring;
        public static Texture2D Ring
        {
            get
            {
                if (ring != null) return ring;
                int n = 128;
                ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1 - Mathf.Abs(d - 0.82f) / 0.12f);
                        a = a * a + Mathf.Clamp01(0.8f - d) * 0.18f;
                        ring.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                ring.Apply();
                return ring;
            }
        }

        public static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }
        public static Color Shade(Color c, float k) { return k >= 0 ? Color.Lerp(c, Color.white, k) : Color.Lerp(c, Color.black, -k); }

        public static GameObject MakeObject(string name, Mesh mesh, Material mat, Transform parent, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }

        // JS Math.round（.5 向上）与 b8（0–1 → 0–255，四舍五入并截断）
        public static int JsRound(double v) { return (int)Math.Floor(v + 0.5); }
        public static byte B8(double v) { return v <= 0 ? (byte)0 : v >= 1 ? (byte)255 : (byte)JsRound(v * 255); }
        // 运行时生成的纹理：RGBA32、线性（不做 sRGB 转换）、行优先（第 0 行在 v = 0，与 three 的 DataTexture 相同）
        public static Texture2D DataTexture(byte[] rgba, int w, int h, bool repeat, bool mip)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mip, true);
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = mip ? FilterMode.Trilinear : FilterMode.Bilinear;
            if (repeat && mip) t.anisoLevel = 4;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            t.SetPixels32(px);
            t.Apply(mip, false);
            return t;
        }

        // ------------------------------------------------------------- 涟漪 --
        // 可平铺的涟漪法线贴图（RG = 斜率），供水面高光使用；mipmap 让远处自然变平
        static Texture2D ripple;
        public static byte[] RippleBytes()
        {
            const int N = 128;
            var rnd = new SeededRandom(1234);
            var hgt = new float[N * N];
            int[] periods = { 8, 16, 32 }; double[] amps = { 1, 0.5, 0.25 };
            for (int q = 0; q < 3; q++)
            {
                int period = periods[q]; double amp = amps[q];
                var lat = new float[period * period];
                for (int i = 0; i < lat.Length; i++) lat[i] = (float)rnd.NextDouble();
                double cell = (double)N / period;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        double gx = x / cell, gy = y / cell; int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
                        double fx = gx - ix, fy = gy - iy;
                        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                        int x0 = ix % period, x1 = (ix + 1) % period, y0 = iy % period, y1 = (iy + 1) % period;
                        double a = lat[y0 * period + x0], b = lat[y0 * period + x1], c = lat[y1 * period + x0], d = lat[y1 * period + x1];
                        hgt[y * N + x] = (float)(hgt[y * N + x] + amp * ((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy));
                    }
            }
            var sl = new float[N * N * 2];
            double mx = 1e-6;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float sx = hgt[y * N + (x + 1) % N] - hgt[y * N + (x + N - 1) % N];
                    float sy = hgt[((y + 1) % N) * N + x] - hgt[((y + N - 1) % N) * N + x];
                    sl[(y * N + x) * 2] = sx; sl[(y * N + x) * 2 + 1] = sy;
                    mx = Math.Max(mx, Math.Max(Math.Abs((double)sx), Math.Abs((double)sy)));
                }
            var data = new byte[N * N * 4];
            for (int i = 0; i < N * N; i++)
            {
                data[i * 4] = (byte)JsRound((sl[i * 2] / mx * 0.5 + 0.5) * 255);
                data[i * 4 + 1] = (byte)JsRound((sl[i * 2 + 1] / mx * 0.5 + 0.5) * 255);
                data[i * 4 + 2] = 128; data[i * 4 + 3] = 255;
            }
            return data;
        }
        public static Texture2D Ripple { get { if (ripple == null) ripple = DataTexture(RippleBytes(), 128, 128, true, true); return ripple; } }

        // ======================================================== 地形细节纹理 --
        // 程序化细节纹理（可平铺，256²，RGBA，带 mipmap）——全部在代码中生成：
        //   A.r 草地明暗（簇状噪声）  A.g 泥土 / 碎石  A.b 沙纹  A.a 柔和噪声（雪面、过渡边缘）
        //   B.r 岩石明暗（层理 + 裂隙 + 脊状噪声）  B.g 大尺度变化  B.b 农田地块编号（0 = 田埂小路）  B.a 犁沟
        public const int TexN = 256;
        static float[] PeriodicNoise(int N, int P, SeededRandom rnd)
        {
            var lat = new float[P * P];
            for (int i = 0; i < lat.Length; i++) lat[i] = (float)rnd.NextDouble();
            var o = new float[N * N]; double cell = (double)N / P;
            for (int y = 0; y < N; y++)
            {
                double gy = y / cell; int iy = (int)Math.Floor(gy);
                double fy = gy - iy; fy = fy * fy * (3 - 2 * fy);
                int y0 = (iy % P) * P, y1 = ((iy + 1) % P) * P;
                for (int x = 0; x < N; x++)
                {
                    double gx = x / cell; int ix = (int)Math.Floor(gx);
                    double fx = gx - ix; fx = fx * fx * (3 - 2 * fx);
                    int x0 = ix % P, x1 = (ix + 1) % P;
                    double a = lat[y0 + x0], b = lat[y0 + x1], c = lat[y1 + x0], d = lat[y1 + x1];
                    o[y * N + x] = (float)((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy);
                }
            }
            return o;
        }
        static IEnumerator FbmTex(int N, int[] periods, double[] amps, int seed, float[][] outV)
        {
            var rnd = new SeededRandom(seed); var acc = new float[N * N];
            for (int o = 0; o < periods.Length; o++)
            {
                var n = PeriodicNoise(N, periods[o], rnd);
                for (int i = 0; i < acc.Length; i++) acc[i] = (float)(acc[i] + n[i] * amps[o]);
                yield return null;
            }
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int i = 0; i < acc.Length; i++) { lo = Math.Min(lo, acc[i]); hi = Math.Max(hi, acc[i]); }
            for (int i = 0; i < acc.Length; i++) acc[i] = (float)((acc[i] - lo) / Math.Max(1e-6, hi - lo));
            outV[0] = acc;
        }
        // 可平铺的 Worley 噪声：f1 = 到最近特征点的距离，f2 − f1（裂隙 / 卵石边缘）
        static IEnumerator WorleyTex(int N, int P, int seed, float[][] outV)
        {
            var rnd = new SeededRandom(seed); double cell = (double)N / P;
            var fx = new float[P * P]; var fy = new float[P * P];
            for (int i = 0; i < P * P; i++) { fx[i] = (float)rnd.NextDouble(); fy[i] = (float)rnd.NextDouble(); }
            var f1 = new float[N * N]; var f21 = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                int cy = (int)Math.Floor(y / cell);
                for (int x = 0; x < N; x++)
                {
                    int cx = (int)Math.Floor(x / cell);
                    double d1 = 1e9, d2 = 1e9;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int gx = cx + i, gy = cy + j, k = ((gy + P) % P) * P + ((gx + P) % P);
                            double px = (gx + (double)fx[k]) * cell, py = (gy + (double)fy[k]) * cell;
                            double d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                            if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                        }
                    f1[y * N + x] = (float)(d1 / cell); f21[y * N + x] = (float)((d2 - d1) / cell);
                }
                if ((y & 31) == 31) yield return null;
            }
            outV[0] = f1; outV[1] = f21;
        }
        static byte[] detA, detB;
        static Texture2D detTexA, detTexB;
        // 细节纹理的像素（A、B 各 256² × RGBA）；生成器可分片，完成后 TerrainDetailA / B 直接返回
        public static IEnumerator TerrainDetailGen()
        {
            if (detA != null) yield break;
            const int N = TexN, NN = N * N;
            var g = new float[1][]; var m = new float[1][]; var s = new float[1][]; var r = new float[1][]; var w = new float[2][]; var w2 = new float[2][]; var lo = new float[1][];
            yield return FbmTex(N, new[] { 4, 8, 16, 32, 64, 128 }, new[] { 0.1, 0.16, 0.2, 0.2, 0.18, 0.16 }, 101, g);
            yield return FbmTex(N, new[] { 2, 4, 8, 16 }, new[] { 0.4, 0.3, 0.2, 0.1 }, 202, lo);
            yield return FbmTex(N, new[] { 8, 16, 32 }, new[] { 0.5, 0.3, 0.2 }, 303, m);
            yield return WorleyTex(N, 22, 404, w);
            yield return FbmTex(N, new[] { 4, 8, 16, 32, 64 }, new[] { 0.32, 0.26, 0.2, 0.14, 0.08 }, 505, r);
            yield return WorleyTex(N, 16, 606, w2);
            yield return FbmTex(N, new[] { 4, 8 }, new[] { 0.6, 0.4 }, 707, s);
            float[] G = g[0], Mv = m[0], S = s[0], R = r[0], LO = lo[0], WF1 = w[0], W2F21 = w2[1];
            var A = new byte[NN * 4]; var B = new byte[NN * 4];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x, o = i * 4;
                    // 草地：细噪声 + 簇（向两极拉开）
                    double gr = G[i];
                    gr = 0.5 + (gr - 0.5) * 1.6;
                    A[o] = B8(gr);
                    // 泥土：以 fBm 为主，卵石只是很淡的、经噪声扭曲的斑点
                    int px = (x + JsRound((S[i] - 0.5) * 24) + N) % N, py = (y + JsRound((LO[i] - 0.5) * 24) + N) % N;
                    double peb = Math.Min(1, WF1[py * N + px] * 1.3);
                    A[o + 1] = B8(0.22 + 0.34 * Mv[i] + 0.3 * G[i] + 0.12 * (1 - peb * peb));
                    // 沙纹：三个方向的整数周期波纹（可平铺），按低频噪声混合，并用噪声扭曲
                    double wa = S[i], wb = LO[((y + 97) % N) * N + (x + 141) % N];
                    double k1 = Math.Max(0, 1 - Math.Abs(wa - 0.25) * 3), k2 = Math.Max(0, 1 - Math.Abs(wa - 0.6) * 3), k3 = Math.Max(0, 1 - Math.Abs(wb - 0.55) * 2.6);
                    double kw = k1 + k2 + k3 + 1e-3, warp = (Mv[i] - 0.5) * 2.4;
                    double r1 = Math.Sin(((double)(x + y) / N * 14 + warp) * Math.PI * 2), r2 = Math.Sin(((double)(x - 2 * y) / N * 9 + warp) * Math.PI * 2), r3 = Math.Sin(((double)(3 * x + y) / N * 6 + warp) * Math.PI * 2);
                    double rip = 0.5 + 0.5 * (r1 * k1 + r2 * k2 + r3 * k3) / kw;
                    A[o + 2] = B8(0.25 + 0.45 * rip * rip + 0.3 * G[i]);
                    A[o + 3] = B8(LO[i]);
                    // 岩石：脊状噪声 + 层理 + 颗粒 + 细裂隙（裂隙网按噪声扭曲）
                    double ridge = 1 - Math.Abs(R[i] * 2.0 - 1);
                    double strata = 0.5 + 0.5 * Math.Sin(((double)y / N * 9 + R[i] * 1.6) * Math.PI * 2);
                    double rock = 0.22 + 0.4 * ridge * ridge + 0.14 * strata + 0.2 * G[i] + 0.1 * Mv[i];
                    int wx = (x + JsRound((Mv[i] - 0.5) * 40) + N) % N, wy = (y + JsRound((LO[i] - 0.5) * 40) + N) % N;
                    double cr = W2F21[wy * N + wx];
                    if (cr < 0.04) rock *= 0.78 + 0.22 * cr / 0.04;
                    B[o] = B8(rock);
                    B[o + 1] = B8(LO[(i * 7) % NN] * 0.5 + Mv[i] * 0.5);
                }
                if ((y & 63) == 63) yield return null;
            }
            // 农田：按行切成田块（行高、田宽随机，可平铺），田埂编号 0；犁沟方向逐块随机（周期 8 像素，整除 256）
            var rnd = new SeededRandom(808);
            Func<double, double, double, List<double>> cut = (total, min, max) =>
            {
                var a = new List<double>(); double t = 0;
                while (t < total) { double v = min + rnd.NextDouble() * (max - min); a.Add(v); t += v; }
                double k = total / t, c = 0;
                for (int q = 0; q < a.Count; q++) { c += a[q] * k; a[q] = c; }
                return a;
            };
            var rows = cut(N, 34, 84);
            double r0 = 0;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                double r1 = rows[ri]; int shift = (int)Math.Floor(rnd.NextDouble() * N);
                var cols = cut(N, 30, 96);
                var ids = new double[cols.Count]; var dirs = new bool[cols.Count];
                for (int q = 0; q < cols.Count; q++) ids[q] = 0.06 + rnd.NextDouble() * 0.94;
                for (int q = 0; q < cols.Count; q++) dirs[q] = rnd.NextDouble() < 0.5;
                for (int y = (int)Math.Floor(r0); y < (int)Math.Floor(r1); y++)
                {
                    for (int xx = 0; xx < N; xx++)
                    {
                        int x = (xx + shift) % N;
                        double c0 = 0; int k = 0;
                        while (k < cols.Count - 1 && cols[k] <= xx) { c0 = cols[k]; k++; }
                        double edge = Math.Min(Math.Min(xx - c0, cols[k] - xx), Math.Min(y - r0, r1 - y));
                        int o = (y * N + x) * 4;
                        double furrow = 0.5 + 0.5 * Math.Sin(((dirs[k] ? x : y) / 8.0) * Math.PI * 2);
                        B[o + 2] = edge < 1.6 ? (byte)0 : B8(ids[k]);
                        B[o + 3] = B8(0.55 + 0.45 * furrow * (0.7 + 0.3 * G[y * N + x]));
                    }
                }
                r0 = r1;
                yield return null;
            }
            detA = A; detB = B;
        }
        static void EnsureDetail() { if (detA == null) RunNested(TerrainDetailGen()); }
        public static byte[] TerrainDetailBytesA { get { EnsureDetail(); return detA; } }
        public static byte[] TerrainDetailBytesB { get { EnsureDetail(); return detB; } }
        public static Texture2D TerrainDetailA { get { if (detTexA == null) detTexA = DataTexture(TerrainDetailBytesA, TexN, TexN, true, true); return detTexA; } }
        public static Texture2D TerrainDetailB { get { if (detTexB == null) detTexB = DataTexture(TerrainDetailBytesB, TexN, TexN, true, true); return detTexB; } }

        // 把嵌套的 IEnumerator 一次跑完（yield return 子 IEnumerator 为嵌套）
        public static void RunNested(IEnumerator it)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(it);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                var sub = top.Current as IEnumerator;
                if (sub != null) stack.Push(sub);
            }
        }

        // ======================================================== 第三版材质 --
        // 地形材质（每张地图新建一份：数据纹理随地图而定）。maps = null 为顶点版（顶点色 = 群落底色，uv2 = aMat）
        public static Material TerrainMaterial(TerrainMaps maps)
        {
            var mat = new Material(LoadShader("Terrain")) { name = maps != null ? "TerrainMaps" : "TerrainVerts" };
            mat.SetTexture("_DetA", TerrainDetailA);
            mat.SetTexture("_DetB", TerrainDetailB);
            if (maps != null)
            {
                mat.EnableKeyword("SG_TERRAIN_MAPS");
                mat.SetTexture("_MapN", maps.n); mat.SetTexture("_MapC", maps.c); mat.SetTexture("_MapM", maps.m);
                mat.SetTexture("_OutC", maps.oc); mat.SetTexture("_OutM", maps.om);
                mat.SetVector("_XfN", maps.xfN); mat.SetVector("_XfC", maps.xfC); mat.SetVector("_XfO", maps.xfO); mat.SetVector("_Inner", maps.inner);
            }
            else mat.DisableKeyword("SG_TERRAIN_MAPS");
            return mat;
        }

        // 砖石材质（共享实例，勿 Destroy）：near / far 为淡出的镜头距离（缺省 16 / 30）
        static readonly Dictionary<string, Material> masonry = new Dictionary<string, Material>();
        public static Material MasonryMaterial(float near = 16, float far = 30)
        {
            string key = near + ":" + far;
            Material m;
            if (masonry.TryGetValue(key, out m) && m != null) return m;
            m = new Material(LoadShader("Masonry")) { name = "Masonry", enableInstancing = true };
            m.SetVector("_MFade", new Vector4(near, far, 0, 0));
            masonry[key] = m;
            return m;
        }

        // 植被 / 石块 / 村舍的实例化材质（共享）：kind = "leaf"（树冠：叶簇明暗 + 法线起伏）| "rock"（颗粒、层理）| "plain"（村舍）| "grass"（随风轻摆）
        static readonly Dictionary<string, Material> foliage = new Dictionary<string, Material>();
        public static Material Foliage(string kind)
        {
            if (string.IsNullOrEmpty(kind)) kind = "plain";
            Material m;
            if (foliage.TryGetValue(kind, out m) && m != null) return m;
            m = new Material(LoadShader("Foliage")) { name = kind == "leaf" ? "TreeSmooth" : kind == "rock" ? "RockSmooth" : kind == "grass" ? "GrassSmooth" : "VegSmooth", enableInstancing = true };
            m.SetFloat("_Kind", kind == "leaf" ? 1 : kind == "rock" ? 2 : 0);
            m.SetFloat("_LeafF", kind == "leaf" ? 16f : 11f);
            m.SetFloat("_LeafN", kind == "leaf" ? 0.42f : kind == "rock" ? 0.3f : 0);
            m.SetFloat("_Wind", kind == "grass" ? 1 : 0);
            foliage[kind] = m;
            return m;
        }

        // ======================================================== 自适应画质 --
        // 持续掉帧（120 帧平均 > 28 ms）时降一档；长帧也计入（截到 1 秒），最近 8 帧有 5 帧超过 150 毫秒则立刻降一档。
        // 档位：先降渲染分辨率（移动设备：像素比上限 2，每次 −0.25，最低 1），再把阴影贴图降到中档（≈ 1024）
        public static bool AutoQualityEnabled = true;
        public static float MaxPixelRatio = 2;
        static AutoQuality aq;
        public static void EnsureAutoQuality()
        {
            if (aq != null) return;
            var go = new GameObject("AutoQuality");
            UnityEngine.Object.DontDestroyOnLoad(go);
            aq = go.AddComponent<AutoQuality>();
        }
        public sealed class PerfState { public double last, acc; public int n, slow; public double t0 = -1; }
        public static readonly PerfState Perf = new PerfState();
        // 每帧调用一次（now：秒）；返回本帧是否降了一档
        public static bool TrackPerf(double now)
        {
            var p = Perf;
            if (p.t0 < 0) p.t0 = now;
            bool did = false;
            if (AutoQualityEnabled && p.last > 0 && now - p.t0 > 4)
            {
                double dt = (now - p.last) * 1000;
                p.acc += Math.Min(dt, 1000); p.n++;
                p.slow = ((p.slow << 1) | (dt > 150 ? 1 : 0)) & 0xff;
                int k = 0; for (int b = p.slow; b != 0; b >>= 1) k += b & 1;
                if (k >= 5) { did = Degrade(); p.slow = 0; p.acc = 0; p.n = 0; }
                else if (p.n >= 120) { if (p.acc / p.n > 28) did = Degrade(); p.acc = 0; p.n = 0; }
            }
            p.last = now;
            return did;
        }
        static int nativeW, nativeH;
        static float DevicePixelRatio { get { return Application.isMobilePlatform && Screen.dpi > 0 ? Mathf.Max(1, Screen.dpi / 160f) : 1; } }
        // 按像素比上限重设渲染分辨率（只在移动设备上；桌面的像素比视为 1）
        public static void ApplyPixelRatio()
        {
            float dpr = DevicePixelRatio;
            if (dpr <= 1) return;
            if (nativeW == 0) { nativeW = Screen.width; nativeH = Screen.height; }
            float k = Mathf.Min(dpr, MaxPixelRatio) / dpr;
            int w = Mathf.Max(1, Mathf.RoundToInt(nativeW * k)), h = Mathf.Max(1, Mathf.RoundToInt(nativeH * k));
            if (w != Screen.width || h != Screen.height) Screen.SetResolution(w, h, Screen.fullScreen);
        }
        // 降一档画质；已是最低时返回 false
        public static bool Degrade()
        {
            float dpr = DevicePixelRatio;
            if (MaxPixelRatio > 1 && dpr > 1 && Mathf.Min(dpr, MaxPixelRatio) > 1)
            {
                MaxPixelRatio = Mathf.Max(1, Mathf.Min(MaxPixelRatio, dpr) - 0.25f);
                ApplyPixelRatio();
                return true;
            }
            if (QualitySettings.shadowResolution > ShadowResolution.Medium)
            {
                QualitySettings.shadowResolution = ShadowResolution.Medium;
                return true;
            }
            return false;
        }
    }

    // 自适应画质的每帧计时（Art.EnsureAutoQuality 创建，常驻）
    public sealed class AutoQuality : MonoBehaviour
    {
        void Start() { Art.ApplyPixelRatio(); }
        void LateUpdate() { Art.TrackPerf(Time.realtimeSinceStartup); }
    }

    // 旋转体的轮廓点（由下而上，相对 base）：半径 r、高度 y、可选颜色、sharp = 折角（两侧法线不平均）
    public struct LP
    {
        public float r, y; public bool hasCol; public Color col; public bool sharp;
        public LP(float r, float y, bool sharp = false) { this.r = r; this.y = y; hasCol = false; col = Color.white; this.sharp = sharp; }
        public LP(float r, float y, Color c, bool sharp = false) { this.r = r; this.y = y; hasCol = true; col = c; this.sharp = sharp; }
    }

    // 平滑椭球的选项（与网页版 sphere 的 opts 同名）
    public sealed class SphereOpt
    {
        public float seed, rough, top = 0.06f, flat = -2, bottomShade, smoothN;
    }

    // 网格构建器：平面着色三角形（每个三角形独立顶点 + 面法线 + 顶点色）与第三版的平滑网格（逐顶点法线）。
    // 输入为 Unity 坐标；M 为可选的变换（Unity 坐标系下先变换再输出）
    public class MeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Color> c = new List<Color>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public Matrix4x4 M = Matrix4x4.identity;
        public bool Smooth;

        public int Count { get { return v.Count; } }
        public void Clear() { v.Clear(); n.Clear(); c.Clear(); uv.Clear(); t.Clear(); }

        // 面法线 = (b − a) × (c − a) 归一化；长度 ≤ 1e-12 时（退化三角形）取向上——与网页版 MeshBuilder._emit 相同。
        // 不用 Vector3.normalized：它在长度 ≤ 1e-5 时返回零向量，细小的三角形（如拱窗的扇形片）会被误当成退化
        static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 cc)
        {
            var cr = Vector3.Cross(b - a, cc - a);
            float m = cr.magnitude;
            return m > 1e-12f ? cr / m : Vector3.up;
        }
        public void Tri(Vector3 a, Vector3 b, Vector3 cc, Color col)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); cc = M.MultiplyPoint3x4(cc);
            var nn = FaceNormal(a, b, cc);
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc);
            n.Add(nn); n.Add(nn); n.Add(nn);
            c.Add(col); c.Add(col); c.Add(col);
            uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col) { Tri(a, b, cc, col); Tri(a, cc, d, col); }

        // 带 UV 的平面四边形（用于透明贴图面片）
        public void FlatQuad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); cc = M.MultiplyPoint3x4(cc); d = M.MultiplyPoint3x4(d);
            int i = v.Count;
            var nn = FaceNormal(a, b, cc);
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(col); }
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void Box(Vector3 center, Vector3 size, Color col, float topShade = 0.08f)
        {
            var h = size * 0.5f;
            Vector3 p0 = center + new Vector3(-h.x, -h.y, -h.z), p1 = center + new Vector3(h.x, -h.y, -h.z), p2 = center + new Vector3(h.x, -h.y, h.z), p3 = center + new Vector3(-h.x, -h.y, h.z);
            Vector3 q0 = center + new Vector3(-h.x, h.y, -h.z), q1 = center + new Vector3(h.x, h.y, -h.z), q2 = center + new Vector3(h.x, h.y, h.z), q3 = center + new Vector3(-h.x, h.y, h.z);
            var top = Art.Shade(col, topShade);
            Quad(q0, q3, q2, q1, top);
            Quad(p0, q0, q1, p1, col);
            Quad(p1, q1, q2, p2, col);
            Quad(p2, q2, q3, p3, col);
            Quad(p3, q3, q0, p0, col);
            Quad(p0, p1, p2, p3, Art.Shade(col, -0.3f));
        }

        // 棱柱 / 圆柱（低段数）
        public void Cylinder(Vector3 bottom, float r0, float r1, float h, int seg, Color col, bool cap = true)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var b0 = bottom + d0 * r0; var b1 = bottom + d1 * r0;
                var t0 = bottom + Vector3.up * h + d0 * r1; var t1 = bottom + Vector3.up * h + d1 * r1;
                if (r1 > 0.0001f) Quad(b0, t0, t1, b1, col); else Tri(b0, t0, b1, col);
                if (cap && r1 > 0.0001f) Tri(bottom + Vector3.up * h, t1, t0, Art.Shade(col, 0.08f));
            }
        }
        public void Cone(Vector3 bottom, float r, float h, int seg, Color col) { Cylinder(bottom, r, 0, h, seg, col, false); }

        // 低多边形球（八面体细分一次）
        public void Blob(Vector3 center, Vector3 radius, Color col, int seed = 0)
        {
            var pts = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[,] faces = { { 0, 4, 3 }, { 0, 3, 5 }, { 0, 5, 2 }, { 0, 2, 4 }, { 1, 3, 4 }, { 1, 5, 3 }, { 1, 2, 5 }, { 1, 4, 2 } };
            var rnd = new System.Random(seed);
            for (int f = 0; f < 8; f++)
            {
                Vector3 a = pts[faces[f, 0]], b = pts[faces[f, 1]], cc = pts[faces[f, 2]];
                Vector3 ab = (a + b).normalized, bc = (b + cc).normalized, ca = (cc + a).normalized;
                var tris = new[] { new[] { a, ab, ca }, new[] { ab, b, bc }, new[] { ca, bc, cc }, new[] { ab, bc, ca } };
                foreach (var tr in tris)
                {
                    var k = 0.95f + (float)rnd.NextDouble() * 0.1f;
                    Tri(center + Vector3.Scale(tr[0], radius) * k, center + Vector3.Scale(tr[1], radius) * k, center + Vector3.Scale(tr[2], radius) * k, Art.Shade(col, (tr[0].y + tr[1].y + tr[2].y) * 0.04f));
                }
            }
        }

        // 中式屋顶（第三版：凹曲屋面、檐口两端起翘、平滑法线，正脊两端加鸱吻）。bc 为檐口中心，w × d 为檐口外缘，h 为脊高
        public void ChineseRoof(Vector3 bc, float w, float d, float h, Color col)
        {
            float rw = w * 0.22f, ew = w * 0.5f, ed = d * 0.5f;
            int NS = w > 0.7f ? 4 : w > 0.45f ? 3 : 2;
            // 四圈（檐口 → 两道中圈 → 正脊）：x 半宽、z 半深、高度、翼角起翘
            float[,] R = { { ew, ed, h * 0.08f, h * 0.16f }, { rw + (ew - rw) * 0.64f, ed * 0.62f, h * 0.27f, h * 0.06f }, { rw + (ew - rw) * 0.3f, ed * 0.3f, h * 0.6f, h * 0.015f }, { rw, 0, h, 0 } };
            Color dark = Art.Shade(col, -0.18f), mid = Art.Shade(col, -0.06f), under = Art.Shade(col, -0.45f);
            var P = new Vector3[4, NS + 1]; var N = new Vector3[4, NS + 1];
            for (int side = 0; side < 4; side++)
            {
                // 网格点 P[k][i]，各点法线为相邻面法线之和（同一坡面内平滑，戗脊处分开）
                for (int k = 0; k < 4; k++)
                    for (int i = 0; i <= NS; i++)
                    {
                        float tt = (float)i / NS, e2 = Mathf.Abs(2 * tt - 1);
                        float lift = R[k, 3] * Mathf.Pow(e2, 2.4f), o = 1 + 0.06f * Mathf.Pow(e2, 3) * (k == 0 ? 1 : 0);
                        float x, z;
                        // side 0 南（−z）1 东（+x）2 北（+z）3 西（−x）；t 沿边 0→1
                        if (side == 0) { x = (-R[k, 0] + 2 * R[k, 0] * tt) * o; z = -R[k, 1] * o; }
                        else if (side == 1) { x = R[k, 0] * o; z = (-R[k, 1] + 2 * R[k, 1] * tt) * o; }
                        else if (side == 2) { x = (R[k, 0] - 2 * R[k, 0] * tt) * o; z = R[k, 1] * o; }
                        else { x = -R[k, 0] * o; z = (R[k, 1] - 2 * R[k, 1] * tt) * o; }
                        P[k, i] = new Vector3(bc.x + x, bc.y + R[k, 2] + lift, bc.z + z);
                        N[k, i] = Vector3.zero;
                    }
                var outward = side == 0 ? new Vector3(0, 0, -1) : side == 1 ? new Vector3(1, 0, 0) : side == 2 ? new Vector3(0, 0, 1) : new Vector3(-1, 0, 0);
                for (int k = 0; k < 3; k++)
                    for (int i = 0; i < NS; i++)
                    {
                        var fn = Vector3.Cross(P[k, i + 1] - P[k, i], P[k + 1, i] - P[k, i]);
                        if (fn.y < 0) fn = -fn;
                        fn += outward * 0.02f;
                        N[k, i] += fn; N[k, i + 1] += fn; N[k + 1, i] += fn; N[k + 1, i + 1] += fn;
                    }
                for (int k = 0; k < 3; k++)
                    for (int i = 0; i < NS; i++)
                    {
                        Color c0 = k == 0 ? dark : k == 1 ? mid : col, c1 = k == 0 ? mid : col;
                        Vector3 a = P[k, i], b = P[k, i + 1], cc = P[k + 1, i + 1], dd = P[k + 1, i];
                        Vector3 na = N[k, i].normalized, nb = N[k, i + 1].normalized, nc = N[k + 1, i + 1].normalized, nd = N[k + 1, i].normalized;
                        TriN(a, b, cc, na, nb, nc, c0, c0, c1);
                        if ((dd - cc).sqrMagnitude > 1e-12f) TriN(a, cc, dd, na, nc, nd, c0, c1, c1);
                    }
                // 檐下（深色，略厚）
                Vector3 e0 = P[0, 0], e1 = P[0, NS]; float th = h * 0.05f;
                var u0 = new Vector3(e0.x, e0.y - th, e0.z); var u1 = new Vector3(e1.x, e1.y - th, e1.z);
                TriN(e0, u1, e1, outward, outward, outward, under);
                TriN(e0, u0, u1, outward, outward, outward, under);
            }
            // 正脊 + 鸱吻（小屋顶只有正脊）
            var ridgeC = Art.Shade(col, -0.32f);
            Box(new Vector3(bc.x, bc.y + h + h * 0.04f, bc.z), new Vector3(rw * 2 + h * 0.12f, h * 0.1f, Mathf.Max(0.01f, h * 0.09f)), ridgeC);
            if (w > 0.5f)
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Box(new Vector3(bc.x + sx * (rw + h * 0.04f), bc.y + h + h * 0.14f, bc.z), new Vector3(h * 0.08f, h * 0.22f, h * 0.08f), ridgeC);
                    Box(new Vector3(bc.x + sx * (rw + h * 0.0f), bc.y + h + h * 0.26f, bc.z), new Vector3(h * 0.1f, h * 0.06f, h * 0.06f), ridgeC);
                }
        }

        public void Flag(Vector3 pole, float h, float fw, float fh, Color col)
        {
            Box(pole + Vector3.up * h * 0.5f, new Vector3(0.04f, h, 0.04f), new Color(0.3f, 0.22f, 0.12f));
            var top = pole + Vector3.up * h;
            var a = top + new Vector3(0.02f, 0, 0); var b = top + new Vector3(fw, -fh * 0.1f, 0);
            var cc = top + new Vector3(fw, -fh * 1.1f, 0); var d = top + new Vector3(0.02f, -fh, 0);
            Quad(a, b, cc, d, col); Quad(a, d, cc, b, col);
        }

        // ---- 第三版：平滑网格（逐顶点法线，DESIGN-V3 §1）----
        // 法线按 M 的线性部分变换后归一化（与网页版相同：不取逆转置）
        Vector3 Nm(Vector3 nn)
        {
            nn = M.MultiplyVector(nn);
            float l = nn.magnitude;
            return l > 1e-12f ? nn / l : Vector3.up;
        }
        // 三角形 + 各顶点法线与颜色。正反面按给定法线自动确定（几何法线与三个法线之和同向）
        public void TriN(Vector3 a, Vector3 b, Vector3 cc, Vector3 na, Vector3 nb, Vector3 nc, Color ca) { TriN(a, b, cc, na, nb, nc, ca, ca, ca); }
        public void TriN(Vector3 a, Vector3 b, Vector3 cc, Vector3 na, Vector3 nb, Vector3 nc, Color ca, Color cb, Color ccol)
        {
            Vector3 A = M.MultiplyPoint3x4(a), B = M.MultiplyPoint3x4(b), C = M.MultiplyPoint3x4(cc);
            Vector3 NA = Nm(na), NB = Nm(nb), NC = Nm(nc);
            var g = Vector3.Cross(B - A, C - A);
            if (Vector3.Dot(g, NA + NB + NC) < 0)
            {
                var tp = B; B = C; C = tp;
                tp = NB; NB = NC; NC = tp;
                var tc = cb; cb = ccol; ccol = tc;
            }
            int i = v.Count;
            v.Add(A); v.Add(B); v.Add(C);
            n.Add(NA); n.Add(NB); n.Add(NC);
            c.Add(ca); c.Add(cb); c.Add(ccol);
            uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.right);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        // 平滑椭球（二十面体细分 detail 次：0 = 20、1 = 80、2 = 320 个三角形）
        public void Sphere(Vector3 center, Vector3 r, Color col, int detail = 1, SphereOpt o = null)
        {
            if (o == null) o = DefaultSphere;
            var ico = Ico.Get(detail);
            int nv = ico.v.Length / 3;
            var P = new double[nv * 3];
            double rough = o.rough, s = o.seed * 0.618, flat = o.flat;
            for (int i = 0; i < nv; i++)
            {
                double dx = ico.v[i * 3], dy = ico.v[i * 3 + 1], dz = ico.v[i * 3 + 2];
                double k = 1;
                if (rough != 0) k += rough * (Math.Sin(dx * 3.1 + s * 5.3) * Math.Sin(dy * 2.7 + s * 2.1 + 1) * Math.Sin(dz * 3.3 + s * 3.7 + 2) * 1.6 +
                    Math.Sin(dx * 6.7 + dz * 5.1 + s * 9.1) * 0.35);
                double y = dy < flat ? flat + (dy - flat) * 0.12 : dy;
                P[i * 3] = center.x + dx * r.x * k; P[i * 3 + 1] = center.y + y * r.y * k; P[i * 3 + 2] = center.z + dz * r.z * k;
            }
            var N = SmoothNormals(P, ico.f, center);
            // smoothN（0–1）：法线向未起伏的椭球法线混合——轮廓仍有起伏，明暗却圆润
            if (o.smoothN > 0)
            {
                double k = Math.Min(1, o.smoothN);
                for (int i = 0; i < nv; i++)
                {
                    double ex = ico.v[i * 3] / r.x, ey = ico.v[i * 3 + 1] / r.y, ez = ico.v[i * 3 + 2] / r.z;
                    double el = Math.Sqrt(ex * ex + ey * ey + ez * ez); if (el == 0) el = 1;
                    ex = N[i * 3] * (1 - k) + ex / el * k; ey = N[i * 3 + 1] * (1 - k) + ey / el * k; ez = N[i * 3 + 2] * (1 - k) + ez / el * k;
                    double l = Math.Sqrt(ex * ex + ey * ey + ez * ez); if (l == 0) l = 1;
                    N[i * 3] = ex / l; N[i * 3 + 1] = ey / l; N[i * 3 + 2] = ez / l;
                }
            }
            float top = o.top, bot = o.bottomShade;
            var K = new Color[nv];
            for (int i = 0; i < nv; i++)
            {
                float dy = ico.v[i * 3 + 1];
                K[i] = Art.Shade(col, dy * top - (dy < 0 ? -dy * bot : 0));
            }
            var f = ico.f;
            for (int q = 0; q < f.Length; q += 3)
            {
                int a = f[q], b = f[q + 1], cq = f[q + 2];
                TriN(PV(P, a), PV(P, b), PV(P, cq), PV(N, a), PV(N, b), PV(N, cq), K[a], K[b], K[cq]);
            }
        }
        public void Sphere(Vector3 center, float radius, Color col, int detail = 1, SphereOpt o = null) { Sphere(center, new Vector3(radius, radius, radius), col, detail, o); }
        static readonly SphereOpt DefaultSphere = new SphereOpt();
        static Vector3 PV(double[] a, int i) { return new Vector3((float)a[i * 3], (float)a[i * 3 + 1], (float)a[i * 3 + 2]); }
        // 索引网格的平滑法线（面法线按面积累加），朝离开 center 的方向
        static double[] SmoothNormals(double[] P, int[] F, Vector3 center)
        {
            var N = new double[P.Length];
            for (int q = 0; q < F.Length; q += 3)
            {
                int a = F[q] * 3, b = F[q + 1] * 3, c = F[q + 2] * 3;
                double ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2], vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double ox = (P[a] + P[b] + P[c]) / 3 - center.x, oy = (P[a + 1] + P[b + 1] + P[c + 1]) / 3 - center.y, oz = (P[a + 2] + P[b + 2] + P[c + 2]) / 3 - center.z;
                if (nx * ox + ny * oy + nz * oz < 0) { nx = -nx; ny = -ny; nz = -nz; }
                N[a] += nx; N[a + 1] += ny; N[a + 2] += nz;
                N[b] += nx; N[b + 1] += ny; N[b + 2] += nz;
                N[c] += nx; N[c + 1] += ny; N[c + 2] += nz;
            }
            for (int i = 0; i < N.Length; i += 3)
            {
                double l = Math.Sqrt(N[i] * N[i] + N[i + 1] * N[i + 1] + N[i + 2] * N[i + 2]); if (l == 0) l = 1;
                N[i] /= l; N[i + 1] /= l; N[i + 2] /= l;
            }
            return N;
        }
        // 单位二十面体细分（缓存）：v = 顶点（单位向量，float），f = 三角形顶点下标
        sealed class Ico
        {
            public float[] v; public int[] f;
            static readonly Ico[] cache = new Ico[4];
            public static Ico Get(int detail)
            {
                detail = Math.Max(0, Math.Min(3, detail));
                if (cache[detail] != null) return cache[detail];
                double t = (1 + Math.Sqrt(5)) / 2;
                double[][] raw = { new[] { -1, t, 0 }, new[] { 1, t, 0 }, new[] { -1, -t, 0 }, new[] { 1, -t, 0 }, new[] { 0, -1, t }, new[] { 0, 1, t }, new[] { 0, -1, -t }, new[] { 0, 1, -t }, new[] { t, 0, -1 }, new[] { t, 0, 1 }, new[] { -t, 0, -1 }, new[] { -t, 0, 1 } };
                var V = new List<double[]>();
                foreach (var p in raw) { double l = Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]); V.Add(new[] { p[0] / l, p[1] / l, p[2] / l }); }
                var F = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
                for (int d = 0; d < detail; d++)
                {
                    var mid = new Dictionary<int, int>(); var F2 = new List<int>();
                    Func<int, int, int> m = (a, b) =>
                    {
                        int k = a < b ? a * 4096 + b : b * 4096 + a, i;
                        if (!mid.TryGetValue(k, out i))
                        {
                            double[] p = V[a], q = V[b]; double x = p[0] + q[0], y = p[1] + q[1], z = p[2] + q[2], l = Math.Sqrt(x * x + y * y + z * z);
                            i = V.Count; V.Add(new[] { x / l, y / l, z / l }); mid[k] = i;
                        }
                        return i;
                    };
                    for (int q = 0; q < F.Count; q += 3)
                    {
                        int a = F[q], b = F[q + 1], c = F[q + 2], ab = m(a, b), bc = m(b, c), ca = m(c, a);
                        F2.Add(a); F2.Add(ab); F2.Add(ca); F2.Add(b); F2.Add(bc); F2.Add(ab); F2.Add(c); F2.Add(ca); F2.Add(bc); F2.Add(ab); F2.Add(bc); F2.Add(ca);
                    }
                    F = F2;
                }
                var ic = new Ico { v = new float[V.Count * 3], f = F.ToArray() };
                for (int i = 0; i < V.Count; i++) { ic.v[i * 3] = (float)V[i][0]; ic.v[i * 3 + 1] = (float)V[i][1]; ic.v[i * 3 + 2] = (float)V[i][2]; }
                cache[detail] = ic;
                return ic;
            }
        }

        // 旋转体（平滑）：profile 由下而上（相对 b），seg 段；sharp 的轮廓点两侧法线不平均（折角）
        public void Lathe(Vector3 b, LP[] prof, int seg, Color col, bool capTop = false, bool capBottom = false)
        {
            int np = prof.Length;
            Func<int, float, Vector3> ring = (k, a) => new Vector3(b.x + Mathf.Cos(a) * prof[k].r, b.y + prof[k].y, b.z + Mathf.Sin(a) * prof[k].r);
            Func<int, int, Vector2> tan = (i, j) =>
            {
                float dr = prof[j].r - prof[i].r, dy = prof[j].y - prof[i].y, l = Mathf.Sqrt(dr * dr + dy * dy);
                if (l == 0) l = 1;
                return new Vector2(dy / l, -dr / l);
            };
            Func<int, int, Vector2> nAt = (k, segIdx) =>
            {
                // segIdx：所在的段（k 与 k+1 或 k−1 与 k）；sharp 点只取本段方向
                var own = segIdx == k ? tan(k, k + 1) : tan(k - 1, k);
                if (prof[k].sharp || k == 0 || k == np - 1) return own;
                Vector2 p = tan(k - 1, k), q = tan(k, k + 1);
                float x = p.x + q.x, y = p.y + q.y, l = Mathf.Sqrt(x * x + y * y);
                if (l == 0) l = 1;
                return new Vector2(x / l, y / l);
            };
            Func<int, Color> colAt = k => prof[k].hasCol ? prof[k].col : col;
            for (int k = 0; k + 1 < np; k++)
            {
                Vector2 n0 = nAt(k, k), n1 = nAt(k + 1, k);
                Color c0 = colAt(k), c1 = colAt(k + 1);
                if (prof[k].r < 1e-5f && prof[k + 1].r < 1e-5f) continue;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                    Vector3 p00 = ring(k, a0), p01 = ring(k, a1), p10 = ring(k + 1, a0), p11 = ring(k + 1, a1);
                    Vector3 N0a0 = LN(n0, a0), N1a0 = LN(n1, a0), N0a1 = LN(n0, a1), N1a1 = LN(n1, a1);
                    if (prof[k].r > 1e-5f) TriN(p00, p10, p01, N0a0, N1a0, N0a1, c0, c1, c0);
                    if (prof[k + 1].r > 1e-5f) TriN(p01, p10, p11, N0a1, N1a0, N1a1, c0, c1, c1);
                }
            }
            Action<int, bool> cap = (k, up) =>
            {
                if (!(prof[k].r > 1e-5f)) return;
                var cc = Art.Shade(colAt(k), up ? 0.06f : -0.2f);
                var ctr = new Vector3(b.x, b.y + prof[k].y, b.z); var nn = new Vector3(0, up ? 1 : -1, 0);
                for (int i = 0; i < seg; i++) TriN(ctr, ring(k, i * Mathf.PI * 2 / seg), ring(k, (i + 1) * Mathf.PI * 2 / seg), nn, nn, nn, cc);
            };
            if (capTop) cap(np - 1, true);
            if (capBottom) cap(0, false);
        }
        static Vector3 LN(Vector2 nn, float a) { return new Vector3(Mathf.Cos(a) * nn.x, nn.y, Mathf.Sin(a) * nn.x); }
        // 平滑圆柱 / 圆台（可带顶盖）
        public void SmoothCylinder(Vector3 bottom, float r0, float r1, float h, int seg, Color col, bool cap = true)
        {
            Lathe(bottom, new[] { new LP(r0, 0), new LP(r1, h) }, seg, col, cap && r1 > 1e-5f);
        }
        // 沿折线的平滑管（树干、绳索）：pts 为各点，radii 为各点半径
        public void Tube(Vector3[] pts, float[] radii, int seg, Color col, bool capEnd = false)
        {
            int np = pts.Length;
            if (np < 2) return;
            var R = new Vector3[np, seg]; var Nn = new Vector3[np, seg];
            var refv = new Vector3(1, 0, 0);
            for (int i = 0; i < np; i++)
            {
                Vector3 a = pts[Math.Max(0, i - 1)], b = pts[Math.Min(np - 1, i + 1)];
                var tg = (b - a).normalized;
                if (Mathf.Abs(Vector3.Dot(tg, refv)) > 0.9f) refv = new Vector3(0, 0, 1);
                var u = Vector3.Cross(tg, refv).normalized; var w = Vector3.Cross(tg, u).normalized;
                for (int j = 0; j < seg; j++)
                {
                    float ang = j * Mathf.PI * 2 / seg, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    var nn = new Vector3(u.x * ca + w.x * sa, u.y * ca + w.y * sa, u.z * ca + w.z * sa);
                    Nn[i, j] = nn;
                    R[i, j] = pts[i] + nn * radii[i];
                }
            }
            for (int i = 0; i + 1 < np; i++)
                for (int j = 0; j < seg; j++)
                {
                    int j1 = (j + 1) % seg;
                    TriN(R[i, j], R[i + 1, j], R[i, j1], Nn[i, j], Nn[i + 1, j], Nn[i, j1], col);
                    TriN(R[i, j1], R[i + 1, j], R[i + 1, j1], Nn[i, j1], Nn[i + 1, j], Nn[i + 1, j1], col);
                }
            if (capEnd)
            {
                var cp = pts[np - 1]; var tn = (cp - pts[np - 2]).normalized; var ccol = Art.Shade(col, 0.05f);
                for (int j = 0; j < seg; j++) TriN(cp, R[np - 1, j], R[np - 1, (j + 1) % seg], tn, tn, tn, ccol);
            }
        }

        public Mesh ToMesh(string name = "mesh")
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // 常用模型
    public static class Models
    {
        // 第三版：平滑树干与圆润树冠（阔叶约 92、针叶约 72 个三角形）
        public static void Tree(MeshBuilder mb, Vector3 p, float s, Color leaf, bool pine, int seed)
        {
            mb.Lathe(p, new[] { new LP(0.065f * s, 0), new LP(0.045f * s, 0.4f * s) }, 6, new Color(0.38f, 0.27f, 0.16f));
            if (pine)
            {
                mb.Lathe(p + new Vector3(0, 0.22f * s, 0), new[] { new LP(0, 0.06f * s), new LP(0.44f * s, 0, true), new LP(0.2f * s, 0.36f * s), new LP(0, 0.66f * s) }, 10, Art.Shade(leaf, -0.04f));
                mb.Lathe(p + new Vector3(0, 0.6f * s, 0), new[] { new LP(0, 0.05f * s), new LP(0.32f * s, 0, true), new LP(0.13f * s, 0.32f * s), new LP(0, 0.58f * s) }, 10, Art.Shade(leaf, 0.08f));
            }
            else mb.Sphere(p + new Vector3(0, 0.66f * s, 0), new Vector3(0.43f * s, 0.41f * s, 0.43f * s), leaf, 1, new SphereOpt { seed = seed, rough = 0.07f, bottomShade = 0.25f });
        }

        // 城池：城墙、角楼、主殿、旗帜。size 约为占地半径。
        public static Mesh City(float size, Color banner, bool capital)
        {
            var mb = new MeshBuilder();
            CityInto(mb, size, banner, capital);
            return mb.ToMesh("city");
        }
        // 同上，但写入已有的 MeshBuilder（用于合并网格）
        public static void CityInto(MeshBuilder mb, float size, Color banner, bool capital)
        {
            var stone = new Color(0.72f, 0.69f, 0.62f);
            var wallH = 0.5f * size;
            float r = size;
            mb.Box(new Vector3(0, 0.05f, 0), new Vector3(r * 2.3f, 0.1f, r * 2.3f), new Color(0.62f, 0.58f, 0.48f));
            // 城墙
            float t = 0.22f * size;
            mb.Box(new Vector3(0, wallH / 2, -r), new Vector3(r * 2, wallH, t), stone);
            mb.Box(new Vector3(0, wallH / 2, r), new Vector3(r * 2, wallH, t), stone);
            mb.Box(new Vector3(-r, wallH / 2, 0), new Vector3(t, wallH, r * 2), stone);
            mb.Box(new Vector3(r, wallH / 2, 0), new Vector3(t, wallH, r * 2), stone);
            // 垛口
            for (int i = -3; i <= 3; i++)
            {
                float x = i * r / 3.5f;
                mb.Box(new Vector3(x, wallH + 0.06f * size, -r), new Vector3(0.14f * size, 0.12f * size, t * 1.05f), Art.Shade(stone, 0.06f));
                mb.Box(new Vector3(x, wallH + 0.06f * size, r), new Vector3(0.14f * size, 0.12f * size, t * 1.05f), Art.Shade(stone, 0.06f));
            }
            // 城门楼
            mb.Box(new Vector3(0, wallH + 0.22f * size, -r), new Vector3(0.7f * size, 0.36f * size, 0.36f * size), new Color(0.62f, 0.2f, 0.16f));
            mb.ChineseRoof(new Vector3(0, wallH + 0.4f * size, -r), 1.0f * size, 0.6f * size, 0.32f * size, new Color(0.22f, 0.26f, 0.34f));
            mb.Box(new Vector3(0, 0.2f * size, -r - 0.01f), new Vector3(0.3f * size, 0.4f * size, t * 1.1f), new Color(0.18f, 0.12f, 0.08f));
            // 角楼
            foreach (var cx in new[] { -1, 1 })
                foreach (var cz in new[] { -1, 1 })
                {
                    var p = new Vector3(cx * r, 0, cz * r);
                    mb.Box(p + Vector3.up * wallH * 0.65f, new Vector3(t * 1.8f, wallH * 1.3f, t * 1.8f), Art.Shade(stone, -0.05f));
                    mb.ChineseRoof(p + Vector3.up * wallH * 1.3f, t * 2.6f, t * 2.6f, 0.22f * size, new Color(0.24f, 0.28f, 0.36f));
                }
            // 城内建筑
            var hall = new Color(0.66f, 0.22f, 0.18f);
            mb.Box(new Vector3(0, 0.25f * size, 0.15f * size), new Vector3(0.9f * size, 0.5f * size, 0.6f * size), hall);
            mb.ChineseRoof(new Vector3(0, 0.5f * size, 0.15f * size), 1.3f * size, 0.95f * size, 0.42f * size, capital ? new Color(0.85f, 0.65f, 0.2f) : new Color(0.25f, 0.3f, 0.38f));
            if (capital)
            {
                mb.Box(new Vector3(0, 0.82f * size, 0.15f * size), new Vector3(0.6f * size, 0.3f * size, 0.4f * size), hall);
                mb.ChineseRoof(new Vector3(0, 0.97f * size, 0.15f * size), 0.9f * size, 0.65f * size, 0.34f * size, new Color(0.85f, 0.65f, 0.2f));
            }
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3((i % 2 == 0 ? -0.55f : 0.55f) * size, 0, (i < 2 ? -0.4f : 0.6f) * size);
                mb.Box(p + Vector3.up * 0.14f * size, new Vector3(0.36f * size, 0.28f * size, 0.3f * size), new Color(0.86f, 0.8f, 0.68f));
                mb.ChineseRoof(p + Vector3.up * 0.28f * size, 0.5f * size, 0.42f * size, 0.18f * size, new Color(0.3f, 0.32f, 0.36f));
            }
        }

        // 一名士兵（约 0.5 高）
        public static void Soldier(MeshBuilder mb, Vector3 p, Color team, bool spear, float s = 1f)
        {
            var skin = new Color(0.95f, 0.8f, 0.62f);
            mb.Box(p + new Vector3(-0.045f, 0.09f, 0) * s, new Vector3(0.06f, 0.18f, 0.07f) * s, new Color(0.25f, 0.2f, 0.18f));
            mb.Box(p + new Vector3(0.045f, 0.09f, 0) * s, new Vector3(0.06f, 0.18f, 0.07f) * s, new Color(0.25f, 0.2f, 0.18f));
            mb.Box(p + new Vector3(0, 0.27f, 0) * s, new Vector3(0.18f, 0.2f, 0.11f) * s, team);
            mb.Box(p + new Vector3(0, 0.3f, 0.002f) * s, new Vector3(0.19f, 0.05f, 0.115f) * s, Art.Shade(team, -0.35f));
            mb.Box(p + new Vector3(0, 0.43f, 0) * s, new Vector3(0.11f, 0.11f, 0.11f) * s, skin);
            mb.Cone(p + new Vector3(0, 0.48f, 0) * s, 0.09f * s, 0.07f * s, 6, new Color(0.45f, 0.45f, 0.5f));
            if (spear)
            {
                mb.Box(p + new Vector3(0.12f, 0.38f, 0) * s, new Vector3(0.02f, 0.62f, 0.02f) * s, new Color(0.4f, 0.3f, 0.18f));
                mb.Cone(p + new Vector3(0.12f, 0.69f, 0) * s, 0.025f * s, 0.08f * s, 4, new Color(0.85f, 0.85f, 0.9f));
            }
        }

        // 武将：骑马、披风、军旗
        public static void Commander(MeshBuilder mb, Vector3 p, Color team, float s = 1f)
        {
            var horse = new Color(0.45f, 0.3f, 0.2f);
            mb.Box(p + new Vector3(0, 0.3f, 0) * s, new Vector3(0.18f, 0.18f, 0.46f) * s, horse);
            mb.Box(p + new Vector3(0, 0.44f, 0.24f) * s, new Vector3(0.1f, 0.2f, 0.12f) * s, horse);
            mb.Box(p + new Vector3(0, 0.5f, 0.3f) * s, new Vector3(0.08f, 0.08f, 0.14f) * s, Art.Shade(horse, -0.1f));
            foreach (var lx in new[] { -0.06f, 0.06f }) foreach (var lz in new[] { -0.17f, 0.17f })
                    mb.Box(p + new Vector3(lx, 0.11f, lz) * s, new Vector3(0.05f, 0.22f, 0.05f) * s, Art.Shade(horse, -0.2f));
            mb.Box(p + new Vector3(0, 0.55f, 0) * s, new Vector3(0.2f, 0.22f, 0.13f) * s, team);
            mb.Box(p + new Vector3(0, 0.55f, -0.08f) * s, new Vector3(0.22f, 0.26f, 0.03f) * s, Art.Shade(team, -0.35f));
            mb.Box(p + new Vector3(0, 0.72f, 0) * s, new Vector3(0.12f, 0.12f, 0.12f) * s, new Color(0.95f, 0.8f, 0.62f));
            mb.Cone(p + new Vector3(0, 0.77f, 0) * s, 0.1f * s, 0.1f * s, 6, new Color(0.85f, 0.7f, 0.25f));
            mb.Box(p + new Vector3(0, 0.88f, 0) * s, new Vector3(0.02f, 0.08f, 0.02f) * s, new Color(0.85f, 0.2f, 0.15f));
            mb.Flag(p + new Vector3(-0.16f, 0.4f, -0.1f) * s, 0.9f * s, 0.36f * s, 0.28f * s, team);
        }
    }
}
