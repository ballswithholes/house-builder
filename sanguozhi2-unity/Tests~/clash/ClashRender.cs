// 攻击画面的软件光栅器（无头，对照网页版 tests/clash.html 的截图用）：把 C# 场景里实际建出的物体按 Unity 的约定画成 PNG——
//   · 物体：本画面根下的全部 GameObject（MeshRenderer + MeshFilter、SkinnedMeshRenderer），加上这一帧 Draw() 提交的
//     Graphics.DrawMesh / DrawMeshInstanced（箭、草丛野花、特效、粒子）；士兵群用回退路径（每名士兵一个 Figure，按同一时钟摆姿势）。
//   · 坐标：Unity 世界坐标；镜头 = 场景相机的 Transform、视角 34°、画面宽高比；背面剔除按 Unity 约定（屏幕上顺时针为正面），
//     近平面裁剪；2×2 超采样；先画不透明物体（可见性缓冲：每像素只着色一次；旗面按贴图 alpha 0.5 裁剪），再按提交顺序混合半透明物体。
//   · 着色：逐行移植本画面的着色器——Sanguo/ClashGround（细节纹理 = Art.TerrainDetailBytesA / B，三线性 + 按视角估计的 mip 级别）、
//     ClashProp（aKind 0–4，叶 / 石的视空间法线起伏）、ClashGrass（实例色、随风摆动）、ClashFlag（贴图 + alpha 裁剪）、
//     ClashWater（顶点起伏、闪光点、涟漪、天空反射、光带、浪花、半透明）、Unlit / Additive（粒子、斩光）；
//     人物 = Sanguo/Figure 的 C# 参照实现（同 Tests~/figure/FigureRender.cs：图集反照率 × 势力色、法线贴图、ORM、几何粗糙度、
//     GGX 直接光、Trilight 环境光、环境立方体贴图、多次散射、AO、轮廓光、受击着色），地面贴合（身体 root 局部 y ≥ 0.006）。
//     LowPoly 光照：半兰伯特 (n·l·0.5+0.5)²×1.15 × 太阳（颜色 × 0.8625）× 阴影（强度 0.55）+ 网页版三色环境光 × 0.56。
//     太阳阴影：正交阴影贴图（2048²，覆盖战场），3×3 PCF；投影者 = 近处道具、人物、旗、帅旗杆、箭（远处道具、草、水不投影）。
//     线性雾（RenderSettings 的远近，随镜头平移）；背景 = Sky 着色器的天空（不受雾）。
//   · 不画：HUD；Unity 的级联阴影细节、阴影距离淡出；粒子按方片画（同游戏内）。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;
using UnityEngine;

static class ClashRender
{
    public static string Info = "";
    // ------------------------------------------------------------ 小向量（double）
    struct D3
    {
        public double x, y, z;
        public D3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static D3 operator +(D3 a, D3 b) { return new D3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static D3 operator -(D3 a, D3 b) { return new D3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static D3 operator *(D3 a, double k) { return new D3(a.x * k, a.y * k, a.z * k); }
        public static D3 operator *(D3 a, D3 b) { return new D3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public double Dot(D3 b) { return x * b.x + y * b.y + z * b.z; }
        public D3 Cross(D3 b) { return new D3(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x); }
        public double Len { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public D3 N { get { double l = Len; return l > 1e-12 ? this * (1 / l) : new D3(0, 1, 0); } }
        public static implicit operator D3(Vector3 v) { return new D3(v.x, v.y, v.z); }
    }
    static double Sat(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static D3 Lerp(D3 a, D3 b, double t) { return a + (b - a) * t; }
    static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
    static double Smooth(double e0, double e1, double x) { double t = Sat((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
    static double Step(double e, double x) { return x < e ? 0 : 1; }
    static double Fract(double x) { return x - Math.Floor(x); }
    static D3 C3(Color c) { return new D3(c.r, c.g, c.b); }
    static D3 MulP(Matrix4x4 m, D3 p) { return new D3(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23); }
    static D3 MulV(Matrix4x4 m, D3 p) { return new D3(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z); }
    // 法线矩阵（逆转置的 3×3；仿射矩阵）
    static double[] NormalM(Matrix4x4 m)
    {
        double a = m.m00, b = m.m01, c = m.m02, d = m.m10, e = m.m11, f = m.m12, g = m.m20, h = m.m21, i = m.m22;
        double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g, D = -(b * i - c * h), E = a * i - c * g, F = -(a * h - b * g), G = b * f - c * e, H = -(a * f - c * d), I = a * e - b * d;
        double det = a * A + b * B + c * C; if (Math.Abs(det) < 1e-20) det = 1e-20;
        // 逆矩阵 = adj / det，逆转置 = (cofactor) / det
        return new[] { A / det, B / det, C / det, D / det, E / det, F / det, G / det, H / det, I / det };
    }
    static D3 MulN(double[] n, D3 v) { return new D3(n[0] * v.x + n[1] * v.y + n[2] * v.z, n[3] * v.x + n[4] * v.y + n[5] * v.z, n[6] * v.x + n[7] * v.y + n[8] * v.z); }
    static Matrix4x4 Inverse(Matrix4x4 m)
    {
        var n = NormalM(m);      // 逆转置
        var r = new Matrix4x4 { m00 = (float)n[0], m01 = (float)n[3], m02 = (float)n[6], m10 = (float)n[1], m11 = (float)n[4], m12 = (float)n[7], m20 = (float)n[2], m21 = (float)n[5], m22 = (float)n[8], m33 = 1 };
        var t = MulV(r, new D3(m.m03, m.m13, m.m23));
        r.m03 = (float)-t.x; r.m13 = (float)-t.y; r.m23 = (float)-t.z;
        return r;
    }

    // ------------------------------------------------------------ 纹理（第 0 行 = v 0；可带 mip 链）
    sealed class Tex
    {
        public readonly int w, h; readonly bool repeat; readonly List<float[]> lv = new List<float[]>(); readonly List<int> lw = new List<int>(), lh = new List<int>();
        public Tex(byte[] rgba, int w, int h, bool repeat, bool mips) : this(w, h, repeat) { var d = new float[w * h * 4]; for (int i = 0; i < d.Length; i++) d[i] = rgba[i] / 255f; Init(d, mips); }
        public Tex(Color32[] px, int w, int h, bool repeat, bool mips) : this(w, h, repeat)
        {
            var d = new float[w * h * 4];
            for (int i = 0; i < w * h; i++) { d[i * 4] = px[i].r / 255f; d[i * 4 + 1] = px[i].g / 255f; d[i * 4 + 2] = px[i].b / 255f; d[i * 4 + 3] = px[i].a / 255f; }
            Init(d, mips);
        }
        Tex(int w, int h, bool repeat) { this.w = w; this.h = h; this.repeat = repeat; }
        void Init(float[] d, bool mips)
        {
            lv.Add(d); lw.Add(w); lh.Add(h);
            if (!mips) return;
            int cw = w, ch = h;
            while (cw > 1 || ch > 1)
            {
                int nw = Math.Max(1, cw / 2), nh = Math.Max(1, ch / 2); var s = lv[lv.Count - 1]; var n = new float[nw * nh * 4];
                for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++) for (int c = 0; c < 4; c++)
                {
                    int x0 = Math.Min(cw - 1, x * 2), x1 = Math.Min(cw - 1, x * 2 + 1), y0 = Math.Min(ch - 1, y * 2), y1 = Math.Min(ch - 1, y * 2 + 1);
                    n[(y * nw + x) * 4 + c] = (s[(y0 * cw + x0) * 4 + c] + s[(y0 * cw + x1) * 4 + c] + s[(y1 * cw + x0) * 4 + c] + s[(y1 * cw + x1) * 4 + c]) * 0.25f;
                }
                lv.Add(n); lw.Add(nw); lh.Add(nh); cw = nw; ch = nh;
            }
        }
        public int Levels { get { return lv.Count; } }
        void Bil(int l, double u, double v, float[] o, double k, bool add)
        {
            int W = lw[l], H = lh[l]; var d = lv[l];
            double fx = u * W - 0.5, fy = v * H - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); double tx = fx - x0, ty = fy - y0;
            int xa, xb, ya, yb;
            if (repeat) { xa = ((x0 % W) + W) % W; xb = (xa + 1) % W; ya = ((y0 % H) + H) % H; yb = (ya + 1) % H; }
            else { xa = Math.Max(0, Math.Min(W - 1, x0)); xb = Math.Max(0, Math.Min(W - 1, x0 + 1)); ya = Math.Max(0, Math.Min(H - 1, y0)); yb = Math.Max(0, Math.Min(H - 1, y0 + 1)); }
            for (int c = 0; c < 4; c++)
            {
                double a = d[(ya * W + xa) * 4 + c], b = d[(ya * W + xb) * 4 + c], e = d[(yb * W + xa) * 4 + c], f = d[(yb * W + xb) * 4 + c];
                float val = (float)(((a * (1 - tx) + b * tx) * (1 - ty) + (e * (1 - tx) + f * tx) * ty) * k);
                if (add) o[c] += val; else o[c] = val;
            }
        }
        // lod：mip 级别（三线性）
        public void S(double u, double v, double lod, float[] o)
        {
            if (lv.Count == 1 || lod <= 0) { Bil(0, u, v, o, 1, false); return; }
            lod = Math.Min(lod, lv.Count - 1);
            int l0 = (int)Math.Floor(lod), l1 = Math.Min(lv.Count - 1, l0 + 1); double t = lod - l0;
            Bil(l0, u, v, o, 1 - t, false);
            if (t > 0) Bil(l1, u, v, o, t, true);
        }
    }
    static readonly Dictionary<Texture, Tex> texCache = new Dictionary<Texture, Tex>();
    static Tex TexOf(Texture t, bool mips)
    {
        var t2 = t as Texture2D;
        if (t2 == null) return null;
        Tex r;
        if (texCache.TryGetValue(t2, out r)) return r;
        r = new Tex(t2.pixels, t2.width, t2.height, t2.wrapMode == TextureWrapMode.Repeat, mips);
        texCache[t2] = r;
        return r;
    }
    static Tex detA, detB, ripple, atlasMap, atlasNrm, atlasOrm;
    static FigAtlas.EnvCubes env;

    // ------------------------------------------------------------ 材质与三角形
    enum K { Ground, Prop, Grass, Flag, Water, Figure, Unlit, Additive }
    sealed class Mat
    {
        public K k; public Tex map, nrm, orm; public D3 tint = new D3(1, 1, 1); public double tintA = 1; public D3 team = new D3(1, 1, 1), hitTint;
        public bool cast, receive = true, cullOff; public double snowH = 99, lite;
    }
    struct Vt { public D3 p, n; public double u, v, r, g, b, a, e0, e1, e2, e3, f0, f1, f2, f3; }
    sealed class Tri { public Vt a, b, c; public Mat m; public double gR; public D3 T, B; public bool hasTB; }
    static readonly List<Tri> opaque = new List<Tri>(), blend = new List<Tri>();
    static Vt Mix(Vt a, Vt b, double t)
    {
        return new Vt
        {
            p = Lerp(a.p, b.p, t), n = Lerp(a.n, b.n, t), u = Lerp(a.u, b.u, t), v = Lerp(a.v, b.v, t), r = Lerp(a.r, b.r, t), g = Lerp(a.g, b.g, t), b = Lerp(a.b, b.b, t), a = Lerp(a.a, b.a, t),
            e0 = Lerp(a.e0, b.e0, t), e1 = Lerp(a.e1, b.e1, t), e2 = Lerp(a.e2, b.e2, t), e3 = Lerp(a.e3, b.e3, t), f0 = Lerp(a.f0, b.f0, t), f1 = Lerp(a.f1, b.f1, t), f2 = Lerp(a.f2, b.f2, t), f3 = Lerp(a.f3, b.f3, t),
        };
    }

    // ------------------------------------------------------------ 场景常量
    static D3 sunL, sunC;                      // 指向太阳（Unity 世界）、颜色 × 强度
    static readonly D3 AMB_SKY = new D3(0.62, 0.68, 0.8) * 0.56, AMB_EQ = new D3(0.58, 0.56, 0.5) * 0.56, AMB_GND = new D3(0.32, 0.3, 0.26) * 0.56;
    static D3 origin, camPos, camF, camR, camU, fogC; static double fogS, fogE, tanV, tanH, timeReal;
    static double[] viewRot;                   // 世界 → 视空间的 3×3（Unity UNITY_MATRIX_V 的旋转部分）

    static D3 SgAmb(double ny) { return ny >= 0 ? Lerp(AMB_EQ, AMB_SKY, ny) : Lerp(AMB_EQ, AMB_GND, -ny); }
    static D3 LowPoly(D3 albedo, D3 n, double shadow)
    {
        double ndl = n.Dot(sunL) * 0.5 + 0.5; ndl *= ndl;
        double atten = 1 - 0.55 * (1 - shadow);
        return albedo * sunC * (ndl * atten * 1.15) + albedo * SgAmb(n.y);
    }
    static D3 Fog(D3 c, double z) { double f = Sat((fogE - z) / (fogE - fogS)); return Lerp(fogC, c, f); }

    // ------------------------------------------------------------ 收集
    static void AddMesh(Mesh mesh, Matrix4x4 m, Mat mat, Matrix4x4[] skin, Matrix4x4? groundLocal, Func<D3, D3, D3> vfx = null)
    {
        int n = mesh.vertexCount; if (n == 0) return;
        var nm = NormalM(m);
        var vs = new Vt[n];
        Vector4[] g4 = null; mesh.uvs4.TryGetValue(1, out g4);
        bool hasN = mesh.normals.Length == n, hasC = mesh.colors.Length == n, hasUV = mesh.uv.Length == n, hasUV2 = mesh.uv2.Length == n;
        Matrix4x4 gInv = groundLocal.HasValue ? Inverse(groundLocal.Value) : Matrix4x4.identity;
        for (int i = 0; i < n; i++)
        {
            D3 p = mesh.vertices[i], nn = hasN ? (D3)mesh.normals[i] : new D3(0, 1, 0);
            D3 wp, wn;
            if (skin != null)
            {
                var bw = mesh.boneWeights[i];
                wp = new D3(0, 0, 0); wn = new D3(0, 0, 0);
                foreach (var kv in new[] { new KeyValuePair<int, float>(bw.boneIndex0, bw.weight0), new KeyValuePair<int, float>(bw.boneIndex1, bw.weight1), new KeyValuePair<int, float>(bw.boneIndex2, bw.weight2), new KeyValuePair<int, float>(bw.boneIndex3, bw.weight3) })
                {
                    if (kv.Value == 0) continue;
                    var M = skin[kv.Key];
                    wp = wp + MulP(M, p) * kv.Value; wn = wn + MulV(M, nn) * kv.Value;
                }
                if (groundLocal.HasValue)
                {
                    var lp = MulP(gInv, wp);
                    if (lp.y < 0.006) { lp.y = 0.006; wp = MulP(groundLocal.Value, lp); }
                }
                wn = wn.N;
            }
            else { wp = MulP(m, p); wn = MulN(nm, nn).N; }
            if (vfx != null) wp = vfx(wp, p);
            var c = hasC ? mesh.colors[i] : Color.white;
            var vt = new Vt { p = wp, n = wn, r = c.r, g = c.g, b = c.b, a = c.a };
            if (hasUV) { vt.u = mesh.uv[i].x; vt.v = mesh.uv[i].y; }
            if (hasUV2) vt.e0 = mesh.uv2[i].x;
            if (g4 != null && g4.Length == n) { vt.e0 = g4[i].x; vt.e1 = g4[i].y; vt.e2 = g4[i].z; vt.e3 = g4[i].w; }
            vs[i] = vt;
        }
        if (mat.k == K.Ground)
        {
            // 顶点里采样的低频斑块（tex2Dlod 第 0 级）
            var o = new float[4];
            for (int i = 0; i < n; i++)
            {
                var w = vs[i].p - origin; double x3 = w.x, z3 = -w.z;
                detA.S(x3 * 0.071 + 0.31, z3 * 0.071 + 0.67, 0, o);
                vs[i].f0 = o[0]; vs[i].f1 = o[1]; vs[i].f2 = o[2]; vs[i].f3 = o[3];
            }
        }
        var t = mesh.triangles;
        var list = mat.k == K.Water || mat.k == K.Unlit || mat.k == K.Additive ? blend : opaque;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            var tr = new Tri { a = vs[t[i]], b = vs[t[i + 1]], c = vs[t[i + 2]], m = mat };
            if (mat.k == K.Figure)
            {
                D3 e1 = tr.b.p - tr.a.p, e2 = tr.c.p - tr.a.p;
                double du1 = tr.b.u - tr.a.u, dv1 = tr.b.v - tr.a.v, du2 = tr.c.u - tr.a.u, dv2 = tr.c.v - tr.a.v, det = du1 * dv2 - du2 * dv1;
                if (Math.Abs(det) > 1e-14) { tr.T = (e1 * dv2 - e2 * dv1) * (1 / det); tr.B = (e2 * du1 - e1 * du2) * (1 / det); tr.hasTB = true; }
            }
            list.Add(tr);
        }
    }
    static Mat MatOf(Material mat, Renderer r)
    {
        string sn = mat.shader != null ? mat.shader.name : "";
        var m = new Mat { cast = r == null || r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off, receive = r == null || r.receiveShadows };
        object o;
        if (sn.EndsWith("ClashGround")) { m.k = K.Ground; m.snowH = mat.GetFloat("_SnowH"); m.lite = mat.GetFloat("_Lite"); }
        else if (sn.EndsWith("ClashProp")) { m.k = K.Prop; }
        else if (sn.EndsWith("ClashGrass")) { m.k = K.Grass; }
        else if (sn.EndsWith("ClashFlag")) { m.k = K.Flag; m.map = TexOf(mat.mainTexture ?? mat.GetTexture("_MainTex"), true); }
        else if (sn.EndsWith("ClashWater")) { m.k = K.Water; m.cast = false; }
        else if (sn.EndsWith("Figure")) { m.k = K.Figure; }
        else if (sn.EndsWith("Additive")) { m.k = K.Additive; m.cast = false; m.cullOff = true; m.map = TexOf(mat.mainTexture, false); m.tint = C3(mat.color); m.tintA = mat.color.a; }
        else if (sn.EndsWith("Unlit")) { m.k = K.Unlit; m.cast = false; m.cullOff = true; m.map = TexOf(mat.mainTexture, false); m.tint = C3(mat.color); m.tintA = mat.color.a; }
        else { m.k = K.Prop; }
        if (m.k == K.Figure)
        {
            var mt = mat.GetTexture("_MainTex");
            if (mt == FigTextures.Map) { m.map = atlasMap; m.nrm = atlasNrm; m.orm = atlasOrm; }
            else { m.map = TexOf(mt, false); m.nrm = TexOf(mat.GetTexture("_BumpMap"), false); m.orm = TexOf(mat.GetTexture("_ORM"), false); }
            if (mat.props.TryGetValue("_Team", out o) && o is Color) m.team = C3((Color)o);
            if (r != null && r.lastBlock != null && r.lastBlock.values.TryGetValue("_Tint", out o) && o is Color) m.hitTint = C3((Color)o);
        }
        return m;
    }
    static void Walk(Transform t)
    {
        var go = t.gameObject;
        if (go.destroyed || !go.activeSelf) return;
        foreach (var comp in go.Components)
        {
            var sk = comp as SkinnedMeshRenderer;
            if (sk != null && sk.enabled && sk.sharedMesh != null && sk.sharedMaterial != null)
            {
                var mesh = sk.sharedMesh; var bp = mesh.bindposes;
                var skin = new Matrix4x4[sk.bones.Length];
                for (int i = 0; i < skin.Length; i++) skin[i] = sk.bones[i].localToWorldMatrix * bp[i];
                Matrix4x4? gl = null;
                object g; if (sk.lastBlock != null && sk.lastBlock.values.TryGetValue("_SgGround", out g) && g is Vector4 && ((Vector4)g).x > 0.5f) gl = sk.transform.localToWorldMatrix;
                AddMesh(mesh, sk.transform.localToWorldMatrix, MatOf(sk.sharedMaterial, sk), skin, gl);
                continue;
            }
            var mr = comp as MeshRenderer;
            if (mr != null && mr.enabled && mr.sharedMaterial != null)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mat = MatOf(mr.sharedMaterial, mr);
                if (mat.k == K.Water) { var m0 = mr.sharedMaterial; AddWater(mf.sharedMesh, t.localToWorldMatrix, mat, m0.GetFloat("_Amp")); }
                else AddMesh(mf.sharedMesh, t.localToWorldMatrix, mat, null, null);
            }
        }
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
    }
    static double waterAlpha = 0.66, waterAmp = 0.06;
    static void AddWater(Mesh mesh, Matrix4x4 m, Mat mat, float amp)
    {
        waterAmp = amp > 0 ? amp : 0.06;
        double tt = timeReal;
        // 顶点起伏（ClashWater.vert）：w.y += h · _Amp · (1 − 顶点色 g)——加入后逐顶点改 y
        AddMesh(mesh, m, mat, null, null);
        int n0 = blend.Count - mesh.triangles.Length / 3;
        for (int i = n0; i < blend.Count; i++)
        {
            var tr = blend[i];
            tr.a = Wave(tr.a, tt); tr.b = Wave(tr.b, tt); tr.c = Wave(tr.c, tt);
        }
    }
    static Vt Wave(Vt v, double tt)
    {
        var r = v.p - origin; double ux = r.x, uz = r.z;
        double h = Math.Sin(tt * 1.3 + ux * 0.55 + uz * 0.35) * 0.6 + Math.Sin(tt * 0.9 - ux * 0.27 + uz * 0.71) * 0.4;
        v.p.y += h * waterAmp * (1 - v.g);
        return v;
    }

    // ------------------------------------------------------------ 阴影贴图（太阳，正交）
    const int SM = 2048; static float[] smap; static double smX0, smY0, smS; static D3 smR, smU;
    static void BuildShadow()
    {
        smR = new D3(0, 1, 0).Cross(sunL).N; smU = sunL.Cross(smR).N;
        // 覆盖战场：three x ∈ [−26, 26]、z ∈ [−34, 12]、y ∈ [−3, 12]
        double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9;
        foreach (var X in new[] { -26.0, 26 }) foreach (var Y in new[] { -3.0, 12 }) foreach (var Z in new[] { -34.0, 12 })
        {
            var q = origin + new D3(X, Y, -Z); double a = q.Dot(smR), b = q.Dot(smU);
            x0 = Math.Min(x0, a); x1 = Math.Max(x1, a); y0 = Math.Min(y0, b); y1 = Math.Max(y1, b);
        }
        smX0 = x0; smY0 = y0; smS = Math.Max(x1 - x0, y1 - y0);
        smap = new float[SM * SM]; for (int i = 0; i < smap.Length; i++) smap[i] = float.MinValue;
        var X3 = new double[3]; var Y3 = new double[3]; var Z3 = new double[3];
        foreach (var t in opaque)
        {
            if (!t.m.cast) continue;
            var P = new[] { t.a.p, t.b.p, t.c.p };
            for (int k = 0; k < 3; k++) { X3[k] = (P[k].Dot(smR) - smX0) / smS * SM; Y3[k] = (P[k].Dot(smU) - smY0) / smS * SM; Z3[k] = P[k].Dot(sunL); }
            double A = (X3[1] - X3[0]) * (Y3[2] - Y3[0]) - (Y3[1] - Y3[0]) * (X3[2] - X3[0]);
            if (Math.Abs(A) < 1e-12) continue;
            int mx = Math.Max(0, (int)Math.Floor(Math.Min(X3[0], Math.Min(X3[1], X3[2])))), Mx = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(X3[0], Math.Max(X3[1], X3[2]))));
            int my = Math.Max(0, (int)Math.Floor(Math.Min(Y3[0], Math.Min(Y3[1], Y3[2])))), My = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(Y3[0], Math.Max(Y3[1], Y3[2]))));
            bool flag = t.m.k == K.Flag;
            for (int y = my; y <= My; y++) for (int x = mx; x <= Mx; x++)
            {
                double sx = x + 0.5, sy = y + 0.5;
                double w0 = ((X3[1] - sx) * (Y3[2] - sy) - (Y3[1] - sy) * (X3[2] - sx)) / A, w1 = ((X3[2] - sx) * (Y3[0] - sy) - (Y3[2] - sy) * (X3[0] - sx)) / A, w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                if (flag && t.m.map != null)
                {
                    var o = new float[4]; t.m.map.S(w0 * t.a.u + w1 * t.b.u + w2 * t.c.u, w0 * t.a.v + w1 * t.b.v + w2 * t.c.v, 0, o);
                    if (o[3] < 0.5) continue;
                }
                float z = (float)(w0 * Z3[0] + w1 * Z3[1] + w2 * Z3[2]);
                if (z > smap[y * SM + x]) smap[y * SM + x] = z;
            }
        }
    }
    static double Lit(D3 p, D3 n)
    {
        p = p + n * 0.03;
        double fx = (p.Dot(smR) - smX0) / smS * SM, fy = (p.Dot(smU) - smY0) / smS * SM, z = p.Dot(sunL);
        int cx = (int)Math.Floor(fx), cy = (int)Math.Floor(fy); double s = 0;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if (x < 0 || y < 0 || x >= SM || y >= SM) { s += 1; continue; }
            s += smap[y * SM + x] > z + 0.02 ? 0 : 1;
        }
        return s / 9;
    }

    // ------------------------------------------------------------ 着色
    static readonly float[] t0 = new float[4], t1 = new float[4], t2 = new float[4];
    static double LodFor(double scaleTexelsPerUnit, double z, D3 n, D3 V)
    {
        double wpp = z * 2 * tanV / Hs;                                 // 每像素的世界尺寸（超采样后的像素）
        double ndv = Math.Max(0.2, Math.Abs(n.Dot(V)));
        return Math.Log(Math.Max(1e-6, wpp * scaleTexelsPerUnit / ndv), 2);
    }
    static D3 ShadeGround(Tri t, Vt v, double z)
    {
        var w = v.p - origin; D3 vCW = new D3(w.x, w.y, -w.z);
        var nU = v.n.N; D3 n = new D3(nU.x, nU.y, -nU.z);
        var V = (camPos - v.p).N;
        double px = vCW.x, pz = vCW.z;
        detA.S(px * 0.42, pz * 0.42, LodFor(0.42 * 256, z, nU, V), t0);
        double dAr = t0[0], dAg = t0[1], dAb = t0[2], dAa = t0[3];
        double dMr = v.f0, dMg = v.f1, dMa = v.f3;
        double dF = 0.5;
        if (t.m.lite < 0.5 && z < 30) { detA.S(px * 1.6 + 0.53, pz * 1.6 + 0.19, LodFor(1.6 * 256, z, nU, V), t1); dF = Lerp(t1[0], 0.5, Smooth(20, 30, z)); }
        double slope = 1 - n.y;
        D3 bas = new D3(v.r, v.g, v.b);
        D3 col = bas * (0.8 + 0.42 * (dAr * 0.34 + dF * 0.3 + dMr * 0.36));
        col = Lerp(col, col * new D3(1.1, 1.04, 0.8), Smooth(0.5, 0.8, dMg) * 0.5);
        col = Lerp(col, col * new D3(0.8, 0.88, 0.76), Smooth(0.55, 0.85, dMa) * 0.5);
        if (v.e3 > 0.01)
        {
            detB.S(px * 0.045 + 0.13, pz * 0.045 + 0.71, LodFor(0.045 * 256, z, nU, V), t2);
            double id = t2[2];
            D3 cc = new D3(0.46, 0.6, 0.25);
            cc = Lerp(cc, new D3(0.78, 0.68, 0.33), Step(0.24, id)); cc = Lerp(cc, new D3(0.56, 0.44, 0.3), Step(0.42, id)); cc = Lerp(cc, new D3(0.36, 0.52, 0.22), Step(0.56, id));
            cc = Lerp(cc, new D3(0.66, 0.64, 0.32), Step(0.72, id)); cc = Lerp(cc, new D3(0.5, 0.63, 0.28), Step(0.87, id));
            D3 crop = cc * (0.8 + 0.34 * t2[3]);
            crop = Lerp(crop, new D3(0.6, 0.52, 0.38), Step(id, 0.03));
            col = Lerp(col, crop, Smooth(0.25, 0.6, v.e3 + (dMr - 0.5) * 0.4) * (1 - Smooth(0.12, 0.26, slope)));
        }
        double dW = Smooth(0.3, 0.72, v.e0 + (dMa - 0.5) * 0.45 + (dAg - 0.5) * 0.35);
        D3 dirt = Lerp(new D3(0.55, 0.46, 0.33), bas * new D3(0.95, 0.85, 0.7), 0.25) * (0.76 + 0.46 * dAg);
        col = Lerp(col, dirt, dW);
        double sW = Smooth(0.3, 0.7, v.e1 + (dAb - 0.5) * 0.3);
        D3 sand = new D3(0.8, 0.74, 0.57) * (0.86 + 0.2 * dF + 0.08 * dAb);
        col = Lerp(col, sand, sW);
        double rW = Smooth(0.3, 0.52, slope + (dMa - 0.5) * 0.22 + v.e2 * 0.35);
        if (rW > 0.004)
        {
            double ru = Lerp(vCW.x, vCW.z, Step(Math.Abs(n.z), Math.Abs(n.x))), rv = vCW.y;
            if (n.y >= 0.75) { ru = vCW.x; rv = vCW.z; }
            detB.S(ru * 0.11, rv * 0.11, LodFor(0.11 * 256, z, nU, V), t2);
            D3 rc = Lerp(new D3(0.5, 0.48, 0.45), new D3(0.58, 0.5, 0.4), dMr) * (0.58 + 0.72 * t2[0]);
            col = Lerp(col, rc, rW);
        }
        double snW = Smooth(t.m.snowH, t.m.snowH + 3, vCW.y + (dMa - 0.5) * 4) * (1 - Smooth(0.45, 0.75, slope));
        col = Lerp(col, new D3(0.86, 0.88, 0.92) * (0.9 + 0.12 * dAa), snW);
        return LowPoly(col, nU, t.m.receive ? Lit(v.p, nU) : 1);
    }
    static double PN(D3 p) { return Math.Sin(p.x + Math.Sin(p.y * 1.3) * 1.2) * Math.Sin(p.y * 1.1 + Math.Sin(p.z * 1.2) * 1.2) * Math.Sin(p.z * 0.9 + Math.Sin(p.x * 1.4) * 1.2); }
    static double PH(double x, double y) { return Fract(Math.Sin(x * 127.1 + y * 311.7) * 43758.5453); }
    static double GMod(double x, double y) { return x - y * Math.Floor(x / y); }
    static D3 ShadeProp(Tri t, Vt v)
    {
        D3 c = new D3(v.r, v.g, v.b) * t.m.tint;
        var nU = v.n.N; D3 n = new D3(nU.x, nU.y, -nU.z);
        var w = v.p - origin; D3 p = new D3(w.x, w.y, -w.z);
        double L1 = 0, L2 = 0, L3 = 0, LW = 0, k = 1, kd = t.a.e0;
        if (kd > 0.5 && kd < 2.5)
        {
            D3 q = p * (kd < 1.5 ? 4.2 : 3.0);
            L1 = PN(q); L2 = PN(q * 2.63 + new D3(5.3, 5.3, 5.3)); L3 = PN(q * 6.1 + new D3(1.7, 1.7, 1.7));
            if (kd < 1.5) { double cc = L1 * 0.5 + L2 * 0.32 + L3 * 0.18; k = 0.82 + 0.36 * cc + 0.16 * Smooth(0.35, 0.85, L2 * 0.6 + L3 * 0.4); LW = 0.45; }
            else { double cc = L1 * 0.25 + L2 * 0.35 + L3 * 0.4; k = 0.84 + 0.24 * cc + 0.1 * Math.Sin(p.y * 13 + L1 * 2); LW = 0.28; }
        }
        else if (kd > 2.5 && kd < 3.5)
        {
            if (Math.Abs(n.y) < 0.6)
            {
                double u = Math.Abs(n.x) > Math.Abs(n.z) ? p.z : p.x;
                double row = Math.Floor(p.y / 0.21), uu = u / 0.46 + GMod(row, 2) * 0.5;
                double fy = Fract(p.y / 0.21), fu = Fract(uu);
                double mort = 1 - Smooth(0.04, 0.12, Math.Min(fy, 1 - fy)) * Smooth(0.02, 0.06, Math.Min(fu, 1 - fu));
                double h = PH(Math.Floor(uu), row);
                k = (0.88 + 0.2 * h) * (1 - 0.3 * mort) * (0.94 + 0.08 * PN(p * 3.1));
            }
            else
            {
                double gx = p.x / 0.5, gz = p.z / 0.5;
                double fx = Math.Abs(Fract(gx) - 0.5), fz = Math.Abs(Fract(gz) - 0.5);
                k = (0.9 + 0.12 * PH(Math.Floor(gx), Math.Floor(gz))) * (0.84 + 0.16 * (1 - Smooth(0.42, 0.48, Math.Max(fx, fz))));
            }
        }
        else if (kd > 3.5)
        {
            double tx = -n.z + 1e-4, tz = n.x, tl = Math.Sqrt(tx * tx + tz * tz); tx /= tl; tz /= tl;
            double u = p.x * tx + p.z * tz;
            double roll = 0.5 + 0.5 * Math.Sin(u / 0.16 * 6.2832), fy = Fract(p.y / 0.11);
            k = (0.8 + 0.26 * roll * roll) * (1 - 0.2 * (1 - Smooth(0, 0.25, fy)));
        }
        c = c * k;
        if (LW > 0)
        {
            var R = viewRot;
            D3 vn = new D3(R[0] * nU.x + R[1] * nU.y + R[2] * nU.z, R[3] * nU.x + R[4] * nU.y + R[5] * nU.z, R[6] * nU.x + R[7] * nU.y + R[8] * nU.z);
            vn = (vn + new D3(L2 + L3 * 0.5, L1 * 0.6, L3 - L1 * 0.4) * LW).N;
            nU = new D3(R[0] * vn.x + R[3] * vn.y + R[6] * vn.z, R[1] * vn.x + R[4] * vn.y + R[7] * vn.z, R[2] * vn.x + R[5] * vn.y + R[8] * vn.z).N;
        }
        return LowPoly(c, nU, t.m.receive ? Lit(v.p, v.n.N) : 1);
    }
    static D3 ShadeGrass(Tri t, Vt v) { var nU = v.n.N; return LowPoly(new D3(v.r, v.g, v.b), nU, t.m.receive ? Lit(v.p, nU) : 1); }
    static D3 ShadeFlag(Tri t, Vt v, double z)
    {
        t.m.map.S(v.u, v.v, 0, t0);
        var nU = v.n.N;
        return LowPoly(new D3(t0[0] * v.r, t0[1] * v.g, t0[2] * v.b), nU, 1);
    }
    // ---- Sanguo/Figure（同 Tests~/figure/FigureRender.cs 的 ShadeFigure；灯光 = 本画面的太阳 + Trilight）
    static D3 Fresnel(D3 f0, double dotVH) { double fr = Math.Pow(2, (-5.55473 * dotVH - 6.98316) * dotVH); return f0 * (1 - fr) + new D3(fr, fr, fr); }
    static double SpecVD(double alpha, double dotNL, double dotNV, double dotNH)
    {
        double a2 = alpha * alpha;
        double gv = dotNL * Math.Sqrt(a2 + (1 - a2) * dotNV * dotNV), gl = dotNV * Math.Sqrt(a2 + (1 - a2) * dotNL * dotNL);
        double vis = 0.5 / Math.Max(gv + gl, 1e-6), dn = dotNH * dotNH * (a2 - 1) + 1;
        return vis * a2 / (Math.PI * dn * dn);
    }
    static void DFG(double dotNV, double rough, out double fa, out double fb)
    {
        double rx = rough * -1 + 1, ry = rough * -0.0275 + 0.0425, rz = rough * -0.572 + 1.04, rw = rough * 0.022 - 0.04;
        double a004 = Math.Min(rx * rx, Math.Pow(2, -9.28 * dotNV)) * rx + ry;
        fa = -1.04 * a004 + rz; fb = 1.04 * a004 + rw;
    }
    static D3 Reflect(D3 i, D3 n) { return i - n * (2 * n.Dot(i)); }
    static readonly double[] eo = new double[3];
    static D3 ShadeFigure(Tri t, Vt v)
    {
        var m = t.m;
        m.map.S(v.u, v.v, 0, t0); m.nrm.S(v.u, v.v, 0, t1); m.orm.S(v.u, v.v, 0, t2);
        var vc = new D3(v.r, v.g, v.b);
        var vcol = Lerp(vc, vc * m.team, v.a);
        var albedo = new D3(t0[0], t0[1], t0[2]) * Lerp(new D3(1, 1, 1), vcol, t0[3]);
        double rough = Math.Min(Math.Max(t2[1], 0.0525) + t.gR, 1), metal = t2[2];
        double ao = 1 + (t2[0] - 1) * 0.8;
        var n = v.n.N;
        var V0 = (camPos - v.p).N;
        var mn = new D3(t1[0] * 2 - 1, t1[1] * 2 - 1, t1[2] * 2 - 1).N;
        if (t.hasTB)
        {
            var T = (t.T - n * n.Dot(t.T)).N;
            var Bc = n.Cross(T); if (Bc.Dot(t.B) < 0) Bc = Bc * -1;
            n = (T * mn.x + Bc * mn.y + n * mn.z).N;
        }
        var diffC = albedo * (1 - metal);
        var specC = Lerp(new D3(0.04, 0.04, 0.04), albedo, metal);
        double dotNV = Sat(n.Dot(V0));
        D3 col;
        {
            var H = (sunL + V0).N;
            double dotNL = Sat(n.Dot(sunL)), dotNH = Sat(n.Dot(H)), dotVH = Sat(V0.Dot(H));
            var spec = Fresnel(specC, dotVH) * (SpecVD(rough * rough, dotNL, dotNV, dotNH) * Math.PI);
            col = sunC * (diffC + spec) * dotNL;      // 人物不接受阴影（同网页版）
        }
        var hemi = Lerp(AMB_GND, AMB_SKY, 0.5 * n.y + 0.5);
        env.Irradiance(n.x, n.y, n.z, eo); var irr = new D3(eo[0], eo[1], eo[2]) * 0.85;
        var R = Lerp(Reflect(V0 * -1, n), n, rough * rough).N;
        env.Radiance(R.x, R.y, R.z, FigAtlas.EnvCubes.Lod(rough), eo); var rad = new D3(eo[0], eo[1], eo[2]) * 0.85;
        double fa, fb; DFG(dotNV, rough, out fa, out fb);
        var FssEss = specC * fa + new D3(fb, fb, fb);
        double Ems = 1 - (fa + fb);
        var Favg = specC + (new D3(1, 1, 1) - specC) * 0.047619;
        var ms = new D3(FssEss.x * Favg.x / (1 - Ems * Favg.x), FssEss.y * Favg.y / (1 - Ems * Favg.y), FssEss.z * Favg.z / (1 - Ems * Favg.z)) * Ems;
        var tot = FssEss + ms;
        var diffE = diffC * (1 - Math.Max(tot.x, Math.Max(tot.y, tot.z)));
        double specAO = Sat(Math.Pow(dotNV + ao, Math.Pow(2, -16 * rough - 1)) - 1 + ao);
        col = col + (hemi * diffC + diffE * irr) * ao + (rad * FssEss + ms * irr) * specAO;
        // 轮廓光 + 受击着色
        var rimDir = new D3(viewRot[0] * 0.55 + viewRot[3] * 0.62 + viewRot[6] * -0.56, viewRot[1] * 0.55 + viewRot[4] * 0.62 + viewRot[7] * -0.56, viewRot[2] * 0.55 + viewRot[5] * 0.62 + viewRot[8] * -0.56).N;
        double sgR = 1 - dotNV, sgL = Sat(n.Dot(rimDir) * 0.5 + 0.5), sgR2 = sgR * sgR;
        var rimC = new D3(1.0, 0.9, 0.78) * (sgR2 * sgR2 * 0.42 * sgL * sgL * 1.6);
        col = col + rimC * (new D3(0.2, 0.2, 0.2) + albedo * 0.6) * (1 + (0.45 - 1) * t2[1]);
        col = col + m.hitTint * (0.2 + 0.8 * Math.Pow(Math.Max(sgR, 1e-4), 1.6));
        return col;
    }
    // ---- ClashWater（半透明）
    static D3 ShadeWater(Vt v, out double alpha)
    {
        var r = v.p - origin; double ux = r.x, uz = r.z, tt = timeReal, A = waterAmp;
        D3 w3 = new D3(r.x, r.y, -r.z);
        double a1 = tt * 1.3 + ux * 0.55 + uz * 0.35, a2 = tt * 0.9 - ux * 0.27 + uz * 0.71;
        double dx = Math.Cos(a1) * 0.33 - Math.Cos(a2) * 0.11, dz = Math.Cos(a1) * 0.21 + Math.Cos(a2) * 0.28;
        D3 n = new D3(-dx * A * 4, 1, dz * A * 4).N;
        double shore = Sat(v.r);
        double sparkle = Math.Sin(ux * 3.1 + tt * 2) * Math.Sin(uz * 2.7 - tt * 1.7);
        D3 albedo = Lerp(new D3(0.07, 0.27, 0.48), new D3(0.25, 0.7, 0.75), shore) + new D3(1, 1, 1) * (Sat(sparkle - 0.85) * 0.6);     // fwidth 淡出：近处为 1
        var cu = camPos - origin; D3 cam3 = new D3(cu.x, cu.y, -cu.z);
        D3 V = (cam3 - w3).N;
        ripple.S(ux * 0.045 + tt * 0.012, uz * 0.045 + tt * 0.008, 0, t0); ripple.S(ux * 0.093 - tt * 0.010, uz * 0.093 + tt * 0.015, 0, t1);
        double rx = ((t0[0] * 2 - 1) + (t1[0] * 2 - 1) * 0.7) * 0.17, ry = ((t0[1] * 2 - 1) + (t1[1] * 2 - 1) * 0.7) * 0.17;
        D3 ns = new D3(-dx * A * 2 + rx, 1, dz * A * 2 - ry).N;
        D3 L = new D3(0.383797, 0.743145, 0.54812).N;
        D3 sunCol = new D3(0.8625, 0.81075, 0.7245), amb = new D3(0.3472, 0.3808, 0.448);
        double ndl = Math.Max(n.Dot(L), 0), spec = Math.Pow(Math.Max(ns.Dot((L + V).N), 0), 44.8) * 0.8;
        D3 col = albedo * (sunCol * ndl + amb) + sunCol * (0.5 * spec);
        D3 up = new D3(0, 1, 0);
        double fres = Math.Pow(1 - Math.Max(up.Dot(V), 0), 5);
        D3 S = new D3(0.30421, 0.40562, -0.86194);
        double glow = Math.Pow(Math.Max(Reflect(V * -1, up).Dot(S), 0), 10), glint = Math.Pow(Math.Max(ns.Dot((S + V).N), 0), 60);
        col = Lerp(col, new D3(0.8, 0.84, 0.86), fres * 0.45) + new D3(1, 0.93, 0.78) * (glow * 0.16 + glint * 0.26);
        double foam = Smooth(0.86, 0.99, shore) * (0.55 + 0.45 * Math.Sin(tt * 1.6 + (ux + uz) * 0.9 + shore * 18));
        col = Lerp(col, new D3(0.93, 0.95, 0.92), foam * 0.22);
        alpha = Lerp(waterAlpha, waterAlpha * 0.75, shore);
        return col;
    }

    // ------------------------------------------------------------ 光栅化
    static int Ws, Hs; static float[] col, dep; static int[] vid; static float[] vk1, vk2;
    static readonly List<Tri> fin = new List<Tri>();
    static D3 ToCam(D3 p) { var d = p - camPos; return new D3(d.Dot(camR), d.Dot(camU), d.Dot(camF)); }
    const double NEAR = 0.3;
    // 近平面裁剪（视空间 z > NEAR），结果为若干三角形（顶点仍为世界坐标的 Vt）
    static List<Vt[]> Clip(Tri t)
    {
        var poly = new List<Vt> { t.a, t.b, t.c };
        var zs = poly.Select(q => ToCam(q.p).z).ToList();
        if (zs.All(z => z >= NEAR)) return new List<Vt[]> { new[] { t.a, t.b, t.c } };
        if (zs.All(z => z < NEAR)) return new List<Vt[]>();
        var outp = new List<Vt>();
        for (int i = 0; i < 3; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % 3]; double za = zs[i], zb = zs[(i + 1) % 3];
            if (za >= NEAR) outp.Add(a);
            if ((za >= NEAR) != (zb >= NEAR)) outp.Add(Mix(a, b, (NEAR - za) / (zb - za)));
        }
        var r = new List<Vt[]>();
        for (int i = 1; i + 1 < outp.Count; i++) r.Add(new[] { outp[0], outp[i], outp[i + 1] });
        return r;
    }
    static bool Project(D3 c, out double sx, out double sy) { sx = (c.x / (c.z * tanH) * 0.5 + 0.5) * Ws; sy = (0.5 - c.y / (c.z * tanV) * 0.5) * Hs; return true; }
    // 可见性缓冲：写入深度与三角形编号、透视校正的重心坐标
    static void RasterVis(Tri t0_)
    {
        foreach (var vv in Clip(t0_))
        {
            var t = new Tri { a = vv[0], b = vv[1], c = vv[2], m = t0_.m, T = t0_.T, B = t0_.B, hasTB = t0_.hasTB };
            D3 a = ToCam(t.a.p), b = ToCam(t.b.p), c = ToCam(t.c.p);
            double X0, Y0, X1, Y1, X2, Y2;
            Project(a, out X0, out Y0); Project(b, out X1, out Y1); Project(c, out X2, out Y2);
            // Unity：屏幕上（y 向下）顺时针为正面 → y 向上的面积 < 0
            double A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
            if (Math.Abs(A) < 1e-12) continue;
            if (A < 0 && !t.m.cullOff) continue;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(X0, Math.Min(X1, X2)))), maxX = Math.Min(Ws - 1, (int)Math.Ceiling(Math.Max(X0, Math.Max(X1, X2))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(Y0, Math.Min(Y1, Y2)))), maxY = Math.Min(Hs - 1, (int)Math.Ceiling(Math.Max(Y0, Math.Max(Y1, Y2))));
            if (minX > maxX || minY > maxY) continue;
            if (t.m.k == K.Figure)
            {
                double g = 0;
                for (int k = 0; k < 3; k++)
                {
                    double a0 = k == 0 ? t.a.n.x : k == 1 ? t.a.n.y : t.a.n.z, a1 = k == 0 ? t.b.n.x : k == 1 ? t.b.n.y : t.b.n.z, a2 = k == 0 ? t.c.n.x : k == 1 ? t.c.n.y : t.c.n.z;
                    double ddx = ((a1 - a0) * (Y2 - Y0) - (a2 - a0) * (Y1 - Y0)) / A, ddy = ((a2 - a0) * (X1 - X0) - (a1 - a0) * (X2 - X0)) / A;
                    g = Math.Max(g, Math.Max(Math.Abs(ddx), Math.Abs(ddy)) * SS);
                }
                t.gR = g;
            }
            int id = fin.Count; fin.Add(t);
            double iz0 = 1 / a.z, iz1 = 1 / b.z, iz2 = 1 / c.z;
            bool flag = t.m.k == K.Flag;
            for (int py = minY; py <= maxY; py++)
            {
                double sy = py + 0.5;
                for (int px = minX; px <= maxX; px++)
                {
                    double sx = px + 0.5;
                    double w0 = ((X1 - sx) * (Y2 - sy) - (Y1 - sy) * (X2 - sx)) / A, w1 = ((X2 - sx) * (Y0 - sy) - (Y2 - sy) * (X0 - sx)) / A, w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    double iz = w0 * iz0 + w1 * iz1 + w2 * iz2, z = 1 / iz;
                    int o = py * Ws + px;
                    if (z >= dep[o]) continue;
                    double k1 = w1 * iz1 * z, k2 = w2 * iz2 * z;
                    if (flag)
                    {
                        double k0 = 1 - k1 - k2;
                        t.m.map.S(t.a.u * k0 + t.b.u * k1 + t.c.u * k2, t.a.v * k0 + t.b.v * k1 + t.c.v * k2, 0, t1);
                        if (t1[3] < 0.5) continue;
                    }
                    dep[o] = (float)z; vid[o] = id; vk1[o] = (float)k1; vk2[o] = (float)k2;
                }
            }
        }
    }
    static Vt At(Tri t, double k1, double k2)
    {
        double k0 = 1 - k1 - k2; var a = t.a; var b = t.b; var c = t.c;
        return new Vt
        {
            p = a.p * k0 + b.p * k1 + c.p * k2, n = a.n * k0 + b.n * k1 + c.n * k2, u = a.u * k0 + b.u * k1 + c.u * k2, v = a.v * k0 + b.v * k1 + c.v * k2,
            r = a.r * k0 + b.r * k1 + c.r * k2, g = a.g * k0 + b.g * k1 + c.g * k2, b = a.b * k0 + b.b * k1 + c.b * k2, a = a.a * k0 + b.a * k1 + c.a * k2,
            e0 = a.e0 * k0 + b.e0 * k1 + c.e0 * k2, e1 = a.e1 * k0 + b.e1 * k1 + c.e1 * k2, e2 = a.e2 * k0 + b.e2 * k1 + c.e2 * k2, e3 = a.e3 * k0 + b.e3 * k1 + c.e3 * k2,
            f0 = a.f0 * k0 + b.f0 * k1 + c.f0 * k2, f1 = a.f1 * k0 + b.f1 * k1 + c.f1 * k2, f2 = a.f2 * k0 + b.f2 * k1 + c.f2 * k2, f3 = a.f3 * k0 + b.f3 * k1 + c.f3 * k2,
        };
    }
    // 半透明：直接混合（深度测试，不写深度）
    static void RasterBlend(Tri t0_)
    {
        foreach (var vv in Clip(t0_))
        {
            var t = new Tri { a = vv[0], b = vv[1], c = vv[2], m = t0_.m };
            D3 a = ToCam(t.a.p), b = ToCam(t.b.p), c = ToCam(t.c.p);
            double X0, Y0, X1, Y1, X2, Y2;
            Project(a, out X0, out Y0); Project(b, out X1, out Y1); Project(c, out X2, out Y2);
            double A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
            if (Math.Abs(A) < 1e-12) continue;
            if (A < 0 && !t.m.cullOff) continue;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(X0, Math.Min(X1, X2)))), maxX = Math.Min(Ws - 1, (int)Math.Ceiling(Math.Max(X0, Math.Max(X1, X2))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(Y0, Math.Min(Y1, Y2)))), maxY = Math.Min(Hs - 1, (int)Math.Ceiling(Math.Max(Y0, Math.Max(Y1, Y2))));
            if (minX > maxX || minY > maxY) continue;
            double iz0 = 1 / a.z, iz1 = 1 / b.z, iz2 = 1 / c.z;
            for (int py = minY; py <= maxY; py++)
            {
                double sy = py + 0.5;
                for (int px = minX; px <= maxX; px++)
                {
                    double sx = px + 0.5;
                    double w0 = ((X1 - sx) * (Y2 - sy) - (Y1 - sy) * (X2 - sx)) / A, w1 = ((X2 - sx) * (Y0 - sy) - (Y2 - sy) * (X0 - sx)) / A, w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    double iz = w0 * iz0 + w1 * iz1 + w2 * iz2, z = 1 / iz;
                    int o = py * Ws + px;
                    if (z >= dep[o]) continue;
                    var v = At(t, w1 * iz1 * z, w2 * iz2 * z);
                    D3 src; double al; bool add = false;
                    if (t.m.k == K.Water) { src = Fog(ShadeWater(v, out al), z); }
                    else
                    {
                        if (t.m.map != null) t.m.map.S(v.u, v.v, 0, t0); else { t0[0] = t0[1] = t0[2] = t0[3] = 1; }
                        src = new D3(t0[0] * v.r, t0[1] * v.g, t0[2] * v.b) * t.m.tint;
                        al = t0[3] * v.a * t.m.tintA;
                        add = t.m.k == K.Additive;
                    }
                    if (al <= 0) continue;
                    double cr = col[o * 3], cg = col[o * 3 + 1], cb = col[o * 3 + 2];
                    if (add) { cr += src.x * al; cg += src.y * al; cb += src.z * al; }
                    else { cr = cr * (1 - al) + src.x * al; cg = cg * (1 - al) + src.y * al; cb = cb * (1 - al) + src.z * al; }
                    col[o * 3] = (float)cr; col[o * 3 + 1] = (float)cg; col[o * 3 + 2] = (float)cb;
                }
            }
        }
    }
    static D3 Sky(D3 d)
    {
        d = d.N; double y = d.y;
        D3 top = new D3(0.32, 0.55, 0.86), hor = new D3(0.93, 0.88, 0.78), bot = new D3(0.55, 0.62, 0.66);
        D3 c = y > 0 ? Lerp(hor, top, Math.Pow(Sat(y), 0.55)) : Lerp(hor, bot, Sat(-y * 3));
        double s = Sat(d.Dot(new D3(0.3, 0.4, 0.85).N));
        return c + new D3(1, 0.9, 0.7) * (Math.Pow(s, 64) * 0.9 + Math.Pow(s, 6) * 0.18);
    }

    const int SS = 2;
    public static void Frame(ClashScene sc, string png, int W, int H)
    {
        if (atlasMap == null)
        {
            var A = FigAtlas.Data;
            atlasMap = new Tex(A.map, FigAtlas.N, FigAtlas.N, false, false); atlasNrm = new Tex(A.nrm, FigAtlas.N, FigAtlas.N, false, false); atlasOrm = new Tex(A.orm, FigAtlas.N, FigAtlas.N, false, false);
            env = FigAtlas.MakeEnvCubes();
            detA = new Tex(Art.TerrainDetailBytesA, Art.TexN, Art.TexN, true, true); detB = new Tex(Art.TerrainDetailBytesB, Art.TexN, Art.TexN, true, true);
            ripple = TexOf(Art.Ripple, false);
        }
        opaque.Clear(); blend.Clear(); fin.Clear();
        origin = Clash.Origin;
        timeReal = sc.real;
        var cam = sc.cam; var ct = cam.transform;
        camPos = ct.position; var rot = ct.rotation;
        camF = (D3)(rot * Vector3.forward); camR = (D3)(rot * Vector3.right); camU = (D3)(rot * Vector3.up);
        // UNITY_MATRIX_V 的旋转：行 = right、up、−forward
        viewRot = new[] { camR.x, camR.y, camR.z, camU.x, camU.y, camU.z, -camF.x, -camF.y, -camF.z };
        tanV = Math.Tan(cam.fieldOfView * Math.PI / 360); tanH = tanV * W / (double)H;
        var sunFwd = Quaternion.Euler(48, -35, 0) * Vector3.forward;
        sunL = new D3(-sunFwd.x, -sunFwd.y, -sunFwd.z).N;
        sunC = new D3(1, 0.94, 0.84) * (1.15 * 0.75);
        fogC = C3(RenderSettings.fogColor); fogS = RenderSettings.fogStartDistance; fogE = RenderSettings.fogEndDistance;
        // 物体
        Walk(sc.root.transform);
        // 本帧提交的绘制
        Graphics.Recorded = new List<Graphics.InstancedDraw>(); Graphics.RecordedMeshes = new List<Graphics.MeshDraw>();
        sc.Draw();
        foreach (var d in Graphics.RecordedMeshes)
        {
            var m = MatOf(d.mat, null);
            object ic;
            if (m.k == K.Grass) { AddGrass(d.mesh, d.m, d.block != null && d.block.TryGetValue("_InstColor", out ic) && ic is Vector4 ? (Vector4)ic : Vector4.one); continue; }
            m.cast = d.castShadows && (m.k == K.Prop);
            if (d.hasTint && m.k == K.Prop) m.tint = C3(d.tint);
            AddMesh(d.mesh, d.m, m, null, null);
        }
        foreach (var d in Graphics.Recorded)
            for (int i = 0; i < d.count; i++) AddGrass(d.mesh, d.m[i], d.cols != null ? d.cols[i] : Vector4.one);
        Graphics.Recorded = null; Graphics.RecordedMeshes = null;
        BuildShadow();
        // 画
        Ws = W * SS; Hs = H * SS;
        col = new float[Ws * Hs * 3]; dep = new float[Ws * Hs]; vid = new int[Ws * Hs]; vk1 = new float[Ws * Hs]; vk2 = new float[Ws * Hs];
        for (int py = 0; py < Hs; py++) for (int px = 0; px < Ws; px++)
        {
            double nx = ((px + 0.5) / Ws * 2 - 1) * tanH, ny = (1 - (py + 0.5) / Hs * 2) * tanV;
            var c = Sky(camF + camR * nx + camU * ny);
            int o = py * Ws + px;
            col[o * 3] = (float)c.x; col[o * 3 + 1] = (float)c.y; col[o * 3 + 2] = (float)c.z; dep[o] = float.MaxValue; vid[o] = -1;
        }
        foreach (var t in opaque) RasterVis(t);
        for (int o = 0; o < Ws * Hs; o++)
        {
            int id = vid[o]; if (id < 0) continue;
            var t = fin[id]; var v = At(t, vk1[o], vk2[o]); double z = dep[o];
            D3 c;
            switch (t.m.k)
            {
                case K.Ground: c = ShadeGround(t, v, z); break;
                case K.Grass: c = ShadeGrass(t, v); break;
                case K.Flag: c = ShadeFlag(t, v, z); break;
                case K.Figure: c = ShadeFigure(t, v); break;
                default: c = ShadeProp(t, v); break;
            }
            c = Fog(c, z);
            col[o * 3] = (float)c.x; col[o * 3 + 1] = (float)c.y; col[o * 3 + 2] = (float)c.z;
        }
        foreach (var t in blend) RasterBlend(t);
        // 降采样、写出
        var rgba = new byte[W * H * 4];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            double r = 0, g = 0, b = 0;
            for (int sy = 0; sy < SS; sy++) for (int sx = 0; sx < SS; sx++) { int o = ((y * SS + sy) * Ws + x * SS + sx) * 3; r += col[o]; g += col[o + 1]; b += col[o + 2]; }
            int q = (y * W + x) * 4; double k = 1.0 / (SS * SS);
            rgba[q] = (byte)Math.Max(0, Math.Min(255, Math.Round(r * k * 255))); rgba[q + 1] = (byte)Math.Max(0, Math.Min(255, Math.Round(g * k * 255))); rgba[q + 2] = (byte)Math.Max(0, Math.Min(255, Math.Round(b * k * 255))); rgba[q + 3] = 255;
        }
        FigPng.Save(png, rgba, W, H);
        Info = string.Format("不透明 {0} 个三角形、半透明 {1}", opaque.Count, blend.Count);
        col = null; dep = null; vid = null; vk1 = null; vk2 = null; smap = null;
    }
    // 草丛 / 野花（ClashGrass）：实例色按顶点 a（aTint）混入；随风摆动——相位取实例的位置（three 坐标），局部顶点按高度偏移
    static void AddGrass(Mesh mesh, Matrix4x4 M, Vector4 ic)
    {
        var m = new Mat { k = K.Grass, cast = false, receive = true };
        double tt = timeReal;
        double ix = M.m03 - origin.x, iz = -(M.m23 - origin.z);
        double ph = ix * 0.9 + iz * 0.6;
        double sx = Math.Sin(tt * 2.1 + ph) * 0.2, sz = Math.Cos(tt * 1.6 + ph * 1.3) * 0.12;
        int n0 = opaque.Count;
        AddMesh(mesh, M, m, null, null, (wp, lp) => MulP(M, new D3(lp.x + sx * lp.y, lp.y, lp.z - sz * lp.y)));
        for (int k = n0; k < opaque.Count; k++)
        {
            var tr = opaque[k];
            tr.a = Tint(tr.a, ic); tr.b = Tint(tr.b, ic); tr.c = Tint(tr.c, ic);
        }
    }
    static Vt Tint(Vt v, Vector4 ic) { v.r *= Lerp(1, ic.x, v.a); v.g *= Lerp(1, ic.y, v.a); v.b *= Lerp(1, ic.z, v.a); return v; }
}
