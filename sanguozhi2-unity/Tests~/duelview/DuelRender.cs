// 单挑画面的软件光栅器（无头，对照网页版 tests/duel.html 的截图用；结构同 Tests~/clash/ClashRender.cs）——
//   · 物体：单挑世界根下的全部 GameObject（MeshRenderer + MeshFilter、SkinnedMeshRenderer），加上这一帧 DuelArena.Update 提交的
//     Graphics.DrawMesh（植被与石块逐株；测试时关掉实例化）；士兵群用回退路径（每名士兵一个 Figure，按同一时钟摆姿势）。
//     不画：粒子、刀光、星芒与冲击环（Additive / Unlit；镜头摆拍时都没有）、弓弦（1 像素的线）。
//   · 坐标：Unity 世界坐标；镜头 = 场景相机的 Transform、视角 30°、画面宽高比；背面剔除按 Unity 约定（屏幕上顺时针为正面），
//     近平面裁剪；2×2 超采样；先画不透明物体（可见性缓冲），再按提交顺序混合半透明物体（水面）。
//   · 着色：逐行移植本画面的着色器——Sanguo/DuelGround（细节纹理 = Art.TerrainDetailBytesA / B，三线性 + 按视角估计的 mip）、
//     DuelProp（顶点色 × 贴图 × _Color、砖纹、_HL 半兰伯特 / Lambert、双面）、DuelFoliage（实例色、叶簇 / 石纹明暗与视空间法线起伏）、
//     DuelWater（起伏、闪光点、涟漪、天空反射、光带、浪花、半透明）、DuelSky（天空，不受雾）；
//     人物 = Sanguo/Figure 的 C# 参照实现（同 Tests~/figure/FigureRender.cs，灯光换成本画面的主光 + 轮廓光 + Trilight）。
//     LowPoly 系光照：Σ 灯光 颜色 × 强度 ×（半兰伯特 (n·l·0.5+0.5)² × 1.15 | Lambert）× 阴影（主光，强度 0.55）+ 三色环境光
//     （天空 / 地面 = 调色板半球光 × 强度，地平线 = 主场景 (0.58, 0.56, 0.5) × 0.56；_HL = 0 时为半球光插值）；地面再经高光肩部。
//     主光阴影：正交阴影贴图（2048²，覆盖镜头前 30 米，= Unity 的阴影距离），3×3 PCF；投影者 = 投影开关打开的物体。
//     线性雾（调色板的远近，视深）。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;
using UnityEngine;

static class DuelRender
{
    public sealed class Scene { public Transform world; public Camera cam; public DuelPalette pal; public Light key, rim; public List<Graphics.InstancedDraw> inst; public List<Graphics.MeshDraw> meshes; }
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
    static D3 Hex(string h) { return C3(DuelXf.C(h)); }
    static D3 MulP(Matrix4x4 m, D3 p) { return new D3(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23); }
    static D3 MulV(Matrix4x4 m, D3 p) { return new D3(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z); }
    static double[] NormalM(Matrix4x4 m)
    {
        double a = m.m00, b = m.m01, c = m.m02, d = m.m10, e = m.m11, f = m.m12, g = m.m20, h = m.m21, i = m.m22;
        double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g, D = -(b * i - c * h), E = a * i - c * g, F = -(a * h - b * g), G = b * f - c * e, H = -(a * f - c * d), I = a * e - b * d;
        double det = a * A + b * B + c * C; if (Math.Abs(det) < 1e-20) det = 1e-20;
        return new[] { A / det, B / det, C / det, D / det, E / det, F / det, G / det, H / det, I / det };
    }
    static D3 MulN(double[] n, D3 v) { return new D3(n[0] * v.x + n[1] * v.y + n[2] * v.z, n[3] * v.x + n[4] * v.y + n[5] * v.z, n[6] * v.x + n[7] * v.y + n[8] * v.z); }
    static double Det3(Matrix4x4 m) { return m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20); }

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
        if (t2 == null || t2.pixels == null) return null;
        Tex r;
        if (texCache.TryGetValue(t2, out r)) return r;
        r = new Tex(t2.pixels, t2.width, t2.height, t2.wrapMode == TextureWrapMode.Repeat, mips);
        texCache[t2] = r;
        return r;
    }
    static Tex detA, detB, ripple, atlasMap, atlasNrm, atlasOrm;
    static FigAtlas.EnvCubes env;

    // ------------------------------------------------------------ 材质与三角形
    enum K { Ground, Prop, Veg, Water, Figure }
    sealed class Mat
    {
        public K k; public Tex map, nrm, orm; public D3 tint = new D3(1, 1, 1); public D3 team = new D3(1, 1, 1), hitTint, inst = new D3(1, 1, 1);
        public bool cast, receive = true, cullOff, brick, hl = true; public double kind, leafF, leafN;
    }
    struct Vt { public D3 p, n, lp; public double u, v, r, g, b, a, e0, e1, e2, e3; }
    sealed class Tri { public Vt a, b, c; public Mat m; public double gR; public D3 T, B; public bool hasTB; }
    static readonly List<Tri> opaque = new List<Tri>(), blend = new List<Tri>();
    static Vt Mix(Vt a, Vt b, double t)
    {
        return new Vt
        {
            p = Lerp(a.p, b.p, t), n = Lerp(a.n, b.n, t), lp = Lerp(a.lp, b.lp, t), u = Lerp(a.u, b.u, t), v = Lerp(a.v, b.v, t), r = Lerp(a.r, b.r, t), g = Lerp(a.g, b.g, t), b = Lerp(a.b, b.b, t), a = Lerp(a.a, b.a, t),
            e0 = Lerp(a.e0, b.e0, t), e1 = Lerp(a.e1, b.e1, t), e2 = Lerp(a.e2, b.e2, t), e3 = Lerp(a.e3, b.e3, t),
        };
    }

    // ------------------------------------------------------------ 场景常量
    static D3 keyL, keyC, rimL, rimC, ambSky, ambGnd, ambEq;
    static D3 camPos, camF, camR, camU, fogC; static double fogS, fogE, tanV, tanH;
    static double[] viewRot;
    static D3 SgAmb(double ny) { return ny >= 0 ? Lerp(ambEq, ambSky, ny) : Lerp(ambEq, ambGnd, -ny); }
    static D3 Hemi(double ny) { return Lerp(ambGnd, ambSky, ny * 0.5 + 0.5); }
    // LowPoly 系：主光（阴影强度 0.55）+ 轮廓光 + 环境光（自发光）
    static D3 Lit(D3 albedo, D3 n, double shadow, bool hl, D3 amb)
    {
        Func<D3, double> nd = L => { double d = n.Dot(L); if (!hl) return Math.Max(d, 0); double h = d * 0.5 + 0.5; return h * h * 1.15; };
        double atten = 1 - 0.55 * (1 - shadow);
        return albedo * keyC * (nd(keyL) * atten) + albedo * rimC * nd(rimL) + albedo * amb;
    }
    static D3 Fog(D3 c, double z) { double f = Sat((fogE - z) / (fogE - fogS)); return Lerp(fogC, c, f); }

    // ------------------------------------------------------------ 收集
    static void AddMesh(Mesh mesh, Matrix4x4 m, Mat mat, Matrix4x4[] skin, Matrix4x4? groundLocal)
    {
        int n = mesh.vertexCount; if (n == 0) return;
        var nm = NormalM(m);
        bool flip = Det3(m) < 0;      // 镜像（单挑的 x 缩放 −1）：Unity 自动翻转绕序
        var vs = new Vt[n];
        bool hasN = mesh.normals.Length == n, hasC = mesh.colors.Length == n, hasUV = mesh.uv.Length == n;
        Vector4[] u2 = null; mesh.uvs4.TryGetValue(1, out u2);
        Matrix4x4 gInv = groundLocal.HasValue ? Inv(groundLocal.Value) : Matrix4x4.identity;
        for (int i = 0; i < n; i++)
        {
            D3 p = mesh.vertices[i], nn = hasN ? (D3)mesh.normals[i] : new D3(0, 1, 0);
            D3 wp, wn;
            if (skin != null)
            {
                var bw = mesh.boneWeights[i];
                wp = new D3(0, 0, 0); wn = new D3(0, 0, 0);
                int[] bi = { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 }; float[] bwt = { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };
                for (int k = 0; k < 4; k++)
                {
                    if (bwt[k] == 0) continue;
                    var M = skin[bi[k]];
                    wp = wp + MulP(M, p) * bwt[k]; wn = wn + MulN(NormalM(M), nn) * bwt[k];
                }
                if (groundLocal.HasValue)
                {
                    var lp = MulP(gInv, wp);
                    if (lp.y < 0.006) { lp.y = 0.006; wp = MulP(groundLocal.Value, lp); }
                }
                wn = wn.N;
            }
            else { wp = MulP(m, p); wn = MulN(nm, nn).N; }
            var c = hasC ? mesh.colors[i] : Color.white;
            var vt = new Vt { p = wp, n = wn, lp = p, r = c.r, g = c.g, b = c.b, a = c.a };
            if (hasUV) { vt.u = mesh.uv[i].x; vt.v = mesh.uv[i].y; }
            if (u2 != null && u2.Length == n) { vt.e0 = u2[i].x; vt.e1 = u2[i].y; vt.e2 = u2[i].z; vt.e3 = u2[i].w; }
            else if (mesh.uv2.Length == n) { vt.e0 = mesh.uv2[i].x; vt.e1 = mesh.uv2[i].y; }
            vs[i] = vt;
        }
        var t = mesh.triangles;
        var list = mat.k == K.Water ? blend : opaque;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            var tr = flip ? new Tri { a = vs[t[i]], b = vs[t[i + 2]], c = vs[t[i + 1]], m = mat } : new Tri { a = vs[t[i]], b = vs[t[i + 1]], c = vs[t[i + 2]], m = mat };
            if (mat.k == K.Figure)
            {
                D3 e1 = tr.b.p - tr.a.p, e2 = tr.c.p - tr.a.p;
                double du1 = tr.b.u - tr.a.u, dv1 = tr.b.v - tr.a.v, du2 = tr.c.u - tr.a.u, dv2 = tr.c.v - tr.a.v, det = du1 * dv2 - du2 * dv1;
                if (Math.Abs(det) > 1e-14) { tr.T = (e1 * dv2 - e2 * dv1) * (1 / det); tr.B = (e2 * du1 - e1 * du2) * (1 / det); tr.hasTB = true; }
            }
            list.Add(tr);
        }
    }
    static Matrix4x4 Inv(Matrix4x4 m)
    {
        var n = NormalM(m);
        var r = new Matrix4x4 { m00 = (float)n[0], m01 = (float)n[3], m02 = (float)n[6], m10 = (float)n[1], m11 = (float)n[4], m12 = (float)n[7], m20 = (float)n[2], m21 = (float)n[5], m22 = (float)n[8], m33 = 1 };
        var tt = MulV(r, new D3(m.m03, m.m13, m.m23));
        r.m03 = (float)-tt.x; r.m13 = (float)-tt.y; r.m23 = (float)-tt.z;
        return r;
    }
    static Mat MatOf(Material mat, Renderer r)
    {
        string sn = mat.shader != null ? mat.shader.name : "";
        var m = new Mat { cast = r == null || r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off, receive = r == null || r.receiveShadows };
        object o;
        if (sn.EndsWith("DuelGround")) m.k = K.Ground;
        else if (sn.EndsWith("DuelProp"))
        {
            m.k = K.Prop; m.brick = mat.GetFloat("_Brick") > 0.5f; m.hl = mat.GetFloat("_HL") > 0.5f; m.cullOff = mat.GetFloat("_Cull") < 0.5f;
            m.map = TexOf(mat.mainTexture ?? mat.GetTexture("_MainTex"), true);
            if (mat.props.TryGetValue("_Color", out o) && o is Color) m.tint = C3((Color)o);
        }
        else if (sn.EndsWith("DuelFoliage")) { m.k = K.Veg; m.kind = mat.GetFloat("_Kind"); m.leafF = mat.GetFloat("_LeafF"); m.leafN = mat.GetFloat("_LeafN"); }
        else if (sn.EndsWith("DuelWater")) { m.k = K.Water; m.cast = false; }
        else if (sn.EndsWith("Figure") || sn.EndsWith("FigureCrowd")) m.k = K.Figure;
        else if (sn.EndsWith("LowPoly")) { m.k = K.Prop; m.hl = true; }
        else return null;      // 粒子、刀光、星芒、冲击环、天空等：不画
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
                var mat = MatOf(sk.sharedMaterial, sk); if (mat == null) continue;
                var mesh = sk.sharedMesh; var bp = mesh.bindposes;
                var skin = new Matrix4x4[sk.bones.Length];
                for (int i = 0; i < skin.Length; i++) skin[i] = sk.bones[i].localToWorldMatrix * bp[i];
                Matrix4x4? gl = null;
                object g; if (sk.lastBlock != null && sk.lastBlock.values.TryGetValue("_SgGround", out g) && g is Vector4 && ((Vector4)g).x > 0.5f) gl = sk.transform.localToWorldMatrix;
                AddMesh(mesh, sk.transform.localToWorldMatrix, mat, skin, gl);
                continue;
            }
            var mr = comp as MeshRenderer;
            if (mr != null && mr.enabled && mr.sharedMaterial != null)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mat = MatOf(mr.sharedMaterial, mr); if (mat == null) continue;
                if (mat.k == K.Water) { AddMesh(mf.sharedMesh, t.localToWorldMatrix, mat, null, null); WaveLast(mf.sharedMesh, mr.sharedMaterial.GetFloat("_Amp")); }
                else AddMesh(mf.sharedMesh, t.localToWorldMatrix, mat, null, null);
            }
        }
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
    }
    static double timeNow = 5, waterAmp = 0.08, waterAlpha = 0.86;
    static void WaveLast(Mesh mesh, float amp)
    {
        waterAmp = amp > 0 ? amp : 0.08;
        int n0 = blend.Count - mesh.triangles.Length / 3;
        for (int i = n0; i < blend.Count; i++) { var tr = blend[i]; tr.a = Wave(tr.a); tr.b = Wave(tr.b); tr.c = Wave(tr.c); }
    }
    static Vt Wave(Vt v)
    {
        double ux = v.p.x, uz = v.p.z, tt = timeNow;
        double h = Math.Sin(tt * 1.3 + ux * 0.55 + uz * 0.35) * 0.6 + Math.Sin(tt * 0.9 - ux * 0.27 + uz * 0.71) * 0.4;
        v.e0 = v.r;                         // shore
        v.p.y += h * waterAmp * (1 - v.g);
        return v;
    }

    // ------------------------------------------------------------ 阴影贴图（主光，正交）
    const int SM = 2048; static float[] smap; static double smX0, smY0, smS; static D3 smR, smU;
    static void BuildShadow()
    {
        smR = new D3(0, 1, 0).Cross(keyL).N; smU = keyL.Cross(smR).N;
        // 覆盖镜头前 30 米（Unity 阴影距离）：取镜头位置附近的包围盒
        double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9;
        foreach (var X in new[] { -1.0, 1 }) foreach (var Y in new[] { -2.0, 14 }) foreach (var Z in new[] { 0.0, 1 })
        {
            var q = camPos + camR * (X * 30 * tanH) + new D3(0, Y, 0) + camF * (Z * 30);
            double a = q.Dot(smR), b = q.Dot(smU);
            x0 = Math.Min(x0, a); x1 = Math.Max(x1, a); y0 = Math.Min(y0, b); y1 = Math.Max(y1, b);
        }
        smX0 = x0; smY0 = y0; smS = Math.Max(x1 - x0, y1 - y0);
        smap = new float[SM * SM]; for (int i = 0; i < smap.Length; i++) smap[i] = float.MinValue;
        var X3 = new double[3]; var Y3 = new double[3]; var Z3 = new double[3];
        foreach (var t in opaque)
        {
            if (!t.m.cast) continue;
            var P = new[] { t.a.p, t.b.p, t.c.p };
            if (P.All(p => (p - camPos).Dot(camF) > 34)) continue;
            for (int k = 0; k < 3; k++) { X3[k] = (P[k].Dot(smR) - smX0) / smS * SM; Y3[k] = (P[k].Dot(smU) - smY0) / smS * SM; Z3[k] = P[k].Dot(keyL); }
            double A = (X3[1] - X3[0]) * (Y3[2] - Y3[0]) - (Y3[1] - Y3[0]) * (X3[2] - X3[0]);
            if (Math.Abs(A) < 1e-12) continue;
            int mx = Math.Max(0, (int)Math.Floor(Math.Min(X3[0], Math.Min(X3[1], X3[2])))), Mx = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(X3[0], Math.Max(X3[1], X3[2]))));
            int my = Math.Max(0, (int)Math.Floor(Math.Min(Y3[0], Math.Min(Y3[1], Y3[2])))), My = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(Y3[0], Math.Max(Y3[1], Y3[2]))));
            for (int y = my; y <= My; y++) for (int x = mx; x <= Mx; x++)
            {
                double sx = x + 0.5, sy = y + 0.5;
                double w0 = ((X3[1] - sx) * (Y3[2] - sy) - (Y3[1] - sy) * (X3[2] - sx)) / A, w1 = ((X3[2] - sx) * (Y3[0] - sy) - (Y3[2] - sy) * (X3[0] - sx)) / A, w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = (float)(w0 * Z3[0] + w1 * Z3[1] + w2 * Z3[2]);
                if (z > smap[y * SM + x]) smap[y * SM + x] = z;
            }
        }
    }
    static double Shadow(D3 p, D3 n)
    {
        if ((p - camPos).Dot(camF) > 30) return 1;
        p = p + n * 0.03;
        double fx = (p.Dot(smR) - smX0) / smS * SM, fy = (p.Dot(smU) - smY0) / smS * SM, z = p.Dot(keyL);
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
    static readonly float[] t0 = new float[4], t1 = new float[4], t2 = new float[4], tA = new float[4], tM = new float[4], tF = new float[4], tN = new float[4], tB = new float[4];
    static double LodFor(double texelsPerUnit, double z, D3 n, D3 V)
    {
        double wpp = z * 2 * tanV / Hs;
        double ndv = Math.Max(0.2, Math.Abs(n.Dot(V)));
        return Math.Log(Math.Max(1e-6, wpp * texelsPerUnit / ndv), 2);
    }
    // ---- Sanguo/DuelGround（surf 逐行）
    static D3 Crop(double id)
    {
        var c = new D3(0.42, 0.57, 0.23);
        c = Lerp(c, new D3(0.75, 0.66, 0.31), Step(0.24, id)); c = Lerp(c, new D3(0.53, 0.41, 0.28), Step(0.42, id)); c = Lerp(c, new D3(0.33, 0.49, 0.2), Step(0.56, id));
        c = Lerp(c, new D3(0.64, 0.62, 0.3), Step(0.72, id)); c = Lerp(c, new D3(0.47, 0.6, 0.26), Step(0.87, id));
        return c;
    }
    static D3 ShadeGround(Tri t, Vt v, double z)
    {
        var nU = v.n.N; var V = (camPos - v.p).N;
        D3 W3 = new D3(v.p.x, v.p.y, -v.p.z);
        double px = v.p.x, pz = v.p.z;               // sgP = worldPos.xz（Unity）
        double sgH = 1.2 + W3.y * 0.32;
        D3 sgN = new D3(nU.x, nU.y, -nU.z);
        D3 tint = new D3(v.r, v.g, v.b);
        double mr = v.e0, mg = v.e1, mb = v.e2, ma = v.e3;
        double TN = Art.TexN;
        detA.S(px * 0.23, pz * 0.23, LodFor(0.23 * TN, z, nU, V), tA);
        detA.S(px * 0.047 + 0.31, pz * 0.047 + 0.67, LodFor(0.047 * TN, z, nU, V), tM);
        detA.S(px * 0.93 + 0.53, pz * 0.93 + 0.19, LodFor(0.93 * TN, z, nU, V), tF);
        detA.S(px * 3.1 + 0.7, pz * 3.1 + 0.2, LodFor(3.1 * TN, z, nU, V), tN);
        detB.S(px * 0.11 + 0.13, pz * 0.11 + 0.71, LodFor(0.11 * TN, z, nU, V), tB);
        double slope = 1 - sgN.y;
        double near = 1 - Smooth(18, 40, (v.p - camPos).Len);
        D3 col = tint * (0.76 + 0.5 * (tA[0] * 0.36 + tF[0] * 0.28 + tM[0] * 0.36));
        if (near > 0.01) col = col * (1 + (tN[0] - 0.5) * 0.24 * near);
        double lum = col.Dot(new D3(0.3, 0.59, 0.11));
        col = Lerp(new D3(lum, lum, lum), col, 0.9) * Lerp(new D3(1.035, 1.0, 0.93), new D3(0.96, 1.0, 1.04), tM[2]);
        col = Lerp(col, col * new D3(1.1, 1.03, 0.8), Smooth(0.5, 0.78, tM[1]) * 0.55);
        col = Lerp(col, col * new D3(0.78, 0.86, 0.74), Smooth(0.55, 0.85, tM[3]) * 0.6);
        double dry = Sat((tint.x - tint.y * 0.85) * 4 + 0.3);
        double dirtW = Smooth(0.66, 0.9, tM[3] * 0.62 + slope * 0.95 + dry * 0.2 + (tA[1] - 0.5) * 0.2);
        D3 dirt = Lerp(new D3(0.48, 0.39, 0.28), tint * new D3(0.95, 0.85, 0.7), 0.35) * (0.78 + 0.44 * tA[1]);
        col = Lerp(col, dirt, dirtW * 0.85);
        double farm = 1 - ma, fw = 0;
        if (farm > 0.02)
        {
            D3 crop = Crop(tB[2]) * (0.8 + 0.34 * tB[3]);
            crop = Lerp(crop, new D3(0.58, 0.5, 0.36), Step(tB[2], 0.03));
            crop = Lerp(crop, tint, 0.15);
            if (near > 0.01)
            {
                double rx = Math.Cos(tB[2] * 6.2832), ry = Math.Sin(tB[2] * 6.2832);
                crop = crop * Lerp(1.0, 0.86 + 0.14 * Math.Sin((px * rx + pz * ry) * 38.0), near * Step(0.03, tB[2]));
            }
            fw = Smooth(0.3, 0.52, farm + (tM[0] - 0.5) * 0.55) * (1 - Smooth(0.1, 0.26, slope));
            col = Lerp(col, crop, fw);
        }
        double beach = (1 - Smooth(0.2, 0.48, sgH + (tA[2] - 0.5) * 0.14)) * Math.Max(1.0, 1 - Smooth(-0.02, 0.05, sgH));
        double rip = Lerp(0.08, 0.26, Smooth(0.3, 0.8, mr));
        D3 sand = Lerp(new D3(0.87, 0.8, 0.62), tint * 1.05, Math.Max(0, Math.Min(0.6, mr * 0.7))) * (1 - rip * 0.55 + rip * tA[2] + (tF[0] - 0.5) * 0.08);
        sand = sand * Lerp(0.8, 1.0, Smooth(0.0, 0.22, sgH));
        col = Lerp(col, sand, Math.Max(beach, Smooth(0.2, 0.7, mr + (tM[3] - 0.5) * 0.25) * (1 - fw)));
        double rockW = Smooth(0.4, 0.62, slope + (tM[3] - 0.5) * 0.22 - mg * 0.12);
        rockW = Math.Max(rockW, Smooth(4.4, 6.6, sgH + (tM[0] - 0.5) * 1.6 + mg * 1.0));
        D3 bw = sgN * sgN; bw = bw * bw; bw = bw * (1 / (bw.x + bw.y + bw.z));
        double ao = 0.5;
        if (rockW > 0.004)
        {
            double l = LodFor(0.19 * TN, z, nU, V);
            detB.S(W3.z * 0.19, W3.y * 0.19, l, t0); double rx = t0[0];
            detB.S(W3.x * 0.19, W3.z * 0.19, l, t0); double ry = t0[0];
            detB.S(W3.x * 0.19, W3.y * 0.19, l, t0); double rz = t0[0];
            double sgR = rx * bw.x + ry * bw.y + rz * bw.z;
            D3 rockC = Lerp(new D3(0.46, 0.44, 0.41), new D3(0.56, 0.47, 0.37), Sat(mg)) * (0.55 + 0.75 * sgR);
            col = Lerp(col, rockC, rockW);
            ao += (sgR - 0.5) * 0.25 * rockW;
        }
        double snowW = Smooth(0.3, 0.62, mb + (tA[3] - 0.5) * 0.5 + (tM[3] - 0.5) * 0.3 - slope * 0.7);
        col = Lerp(col, new D3(0.8, 0.83, 0.88) * (0.88 + 0.16 * tA[3]), snowW);
        col = Lerp(col, new D3(0.6, 0.57, 0.45) * (0.85 + 0.2 * tA[2]), Smooth(-0.05, -0.7, sgH) * 0.75);
        col = col * (0.74 + 0.52 * Sat(ao));
        var c = Lit(col, nU, t.m.receive ? Shadow(v.p, nU) : 1, true, SgAmb(sgN.y));
        // 高光肩部
        double peak = Math.Max(c.x, Math.Max(c.y, c.z));
        if (peak > 0.8) c = c * ((0.8 + 0.2 * (1 - Math.Exp((0.8 - peak) / 0.2))) / peak);
        return c;
    }
    // ---- Sanguo/DuelProp
    static double PH(double x, double y) { return Fract(Math.Sin(x * 127.1 + y * 311.7) * 43758.5453); }
    static D3 ShadeProp(Tri t, Vt v, bool back)
    {
        var m = t.m;
        D3 c = new D3(v.r, v.g, v.b) * m.tint;
        if (m.map != null) { m.map.S(v.u, v.v, 0, t0); c = c * new D3(t0[0], t0[1], t0[2]); }
        var wn = v.n.N;
        if (m.brick)
        {
            D3 p = new D3(v.p.x, v.p.y, -v.p.z), n = new D3(wn.x, wn.y, -wn.z);
            double mx = Math.Max(c.x, Math.Max(c.y, c.z)), mn = Math.Min(c.x, Math.Min(c.y, c.z));
            double stone = Sat(1 - ((mx - mn) / Math.Max(mx, 0.001) - 0.22) * 3);
            double k = 1;
            if (Math.Abs(n.y) < 0.5)
            {
                double u = Math.Abs(n.x) > Math.Abs(n.z) ? p.z : p.x;
                double row = Math.Floor(p.y / 0.21);
                double uu = u / 0.44 + (row - 2 * Math.Floor(row * 0.5)) * 0.5;
                double fy = Fract(p.y / 0.21), fu = Fract(uu);
                double mort = 1 - Smooth(0.03, 0.09, Math.Min(fy, 1 - fy)) * Smooth(0.015, 0.045, Math.Min(fu, 1 - fu));
                double h = PH(Math.Floor(uu), row);
                double wear = 0.5 + 0.5 * Math.Sin(p.x * 0.7 + Math.Sin(p.y * 1.3) * 2) * Math.Sin(p.y * 0.9 + p.x * 0.21);
                k = (0.84 + 0.22 * h) * (1 - 0.32 * mort) * (0.9 + 0.14 * wear) * (0.74 + 0.26 * Smooth(0, 2.2, p.y));
                double moss = (1 - Smooth(0, 1.4, p.y + wear * 0.6)) * stone;
                c = Lerp(c, c * new D3(0.72, 0.86, 0.6), moss * 0.6);
            }
            else
            {
                double gx = p.x / 0.8, gz = p.z / 0.8; double fx = Math.Abs(Fract(gx) - 0.5), fz = Math.Abs(Fract(gz) - 0.5);
                k = (0.9 + 0.12 * PH(Math.Floor(gx), Math.Floor(gz))) * (0.84 + 0.16 * (1 - Smooth(0.44, 0.49, Math.Max(fx, fz))));
            }
            c = c * Lerp(1.0, k, stone);
        }
        if (back) wn = wn * -1;
        var amb = m.hl ? SgAmb(wn.y) : Hemi(wn.y);
        return Lit(c, wn, m.receive ? Shadow(v.p, wn) : 1, m.hl, amb);
    }
    // ---- Sanguo/DuelFoliage
    static double LN(D3 p) { return Math.Sin(p.x + Math.Sin(p.y * 1.3) * 1.2) * Math.Sin(p.y * 1.1 + Math.Sin(p.z * 1.2) * 1.2) * Math.Sin(p.z * 0.9 + Math.Sin(p.x * 1.4) * 1.2); }
    static D3 ShadeVeg(Tri t, Vt v)
    {
        var m = t.m;
        double leaf = v.a;
        D3 c = new D3(v.r, v.g, v.b) * Lerp(new D3(1, 1, 1), m.inst, leaf);
        var wn = v.n.N;
        D3 q = v.lp * m.leafF;
        double l1 = LN(q), l2 = LN(q * 2.63 + new D3(5.3, 5.3, 5.3)), l3 = LN(q * 6.1 + new D3(1.7, 1.7, 1.7));
        if (m.kind < 1.5) { double lc = l1 * 0.5 + l2 * 0.32 + l3 * 0.18; c = c * Lerp(1.0, 0.84 + 0.34 * lc + 0.16 * Smooth(0.35, 0.85, l2 * 0.6 + l3 * 0.4), leaf); }
        else { double lc = l1 * 0.25 + l2 * 0.35 + l3 * 0.4; c = c * Lerp(1.0, 0.86 + 0.22 * lc + 0.1 * Math.Sin(v.lp.y * 41.0 + l1 * 2.0), leaf); }
        var R = viewRot;
        D3 vn = new D3(R[0] * wn.x + R[1] * wn.y + R[2] * wn.z, R[3] * wn.x + R[4] * wn.y + R[5] * wn.z, R[6] * wn.x + R[7] * wn.y + R[8] * wn.z);
        vn = (vn + new D3(l2 + l3 * 0.5, l1 * 0.6, l3 - l1 * 0.4) * (leaf * m.leafN)).N;
        var n2 = new D3(R[0] * vn.x + R[3] * vn.y + R[6] * vn.z, R[1] * vn.x + R[4] * vn.y + R[7] * vn.z, R[2] * vn.x + R[5] * vn.y + R[8] * vn.z).N;
        return Lit(c, n2, m.receive ? Shadow(v.p, wn) : 1, true, SgAmb(n2.y));
    }
    // ---- Sanguo/DuelWater（半透明）
    static D3 ShadeWater(Vt v, out double alpha)
    {
        double ux = v.p.x, uz = v.p.z, tt = timeNow;
        double a1 = tt * 1.3 + ux * 0.55 + uz * 0.35, a2 = tt * 0.9 - ux * 0.27 + uz * 0.71;
        double dx = Math.Cos(a1) * 0.33 - Math.Cos(a2) * 0.11, dz = Math.Cos(a1) * 0.21 + Math.Cos(a2) * 0.28;
        D3 n = new D3(-dx * waterAmp * 4, 1, dz * waterAmp * 4).N;
        double shore = Sat(v.e0);
        double sparkle = Math.Sin(ux * 3.1 + tt * 2) * Math.Sin(uz * 2.7 - tt * 1.7);
        double fw = 0.1;
        D3 albedo = Lerp(new D3(0.07, 0.27, 0.48), new D3(0.25, 0.7, 0.75), shore) + new D3(1, 1, 1) * (Sat(sparkle - 0.85) * 0.6 * (1 - Smooth(0.3, 0.9, fw)));
        D3 w3 = new D3(v.p.x, v.p.y, -v.p.z), cam3 = new D3(camPos.x, camPos.y, -camPos.z);
        D3 V = (cam3 - w3).N;
        ripple.S(ux * 0.045 + tt * 0.012, uz * 0.045 + tt * 0.008, 0, t0);
        ripple.S(ux * 0.093 - tt * 0.010, uz * 0.093 + tt * 0.015, 0, t1);
        double rx = ((t0[0] * 2 - 1) + (t1[0] * 2 - 1) * 0.7) * 0.17, ry = ((t0[1] * 2 - 1) + (t1[1] * 2 - 1) * 0.7) * 0.17;
        D3 ns = new D3(-dx * waterAmp * 2 + rx, 1, dz * waterAmp * 2 - ry).N;
        D3 L = new D3(0.383797, 0.743145, 0.54812).N, sunC = new D3(0.8625, 0.81075, 0.7245), amb = new D3(0.3472, 0.3808, 0.448);
        double ndl = Math.Max(n.Dot(L), 0);
        double spec = Math.Pow(Math.Max(ns.Dot((L + V).N), 0), 44.8) * 0.8;
        D3 col = albedo * (sunC * ndl + amb) + sunC * (0.5 * spec);
        D3 up = new D3(0, 1, 0);
        double fres = Math.Pow(1 - Math.Max(up.Dot(V), 0), 5);
        D3 S = new D3(0.30421, 0.40562, -0.86194).N;
        D3 rv = V * -1 - up * (2 * up.Dot(V * -1));
        double glow = Math.Pow(Math.Max(rv.Dot(S), 0), 10), glint = Math.Pow(Math.Max(ns.Dot((S + V).N), 0), 60);
        col = Lerp(col, new D3(0.8, 0.84, 0.86), fres * 0.45) + new D3(1, 0.93, 0.78) * (glow * 0.16 + glint * 0.26);
        double foam = Smooth(0.86, 0.99, shore) * (0.55 + 0.45 * Math.Sin(tt * 1.6 + (ux + uz) * 0.9 + shore * 18));
        col = Lerp(col, new D3(0.93, 0.95, 0.92), foam * 0.22);
        alpha = Lerp(waterAlpha, waterAlpha * 0.75, shore);
        return col;
    }
    // ---- Sanguo/Figure（同 Tests~/figure/FigureRender.cs 的 ShadeFigure；灯光 = 主光 + 轮廓光 + Trilight）
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
        D3 col = new D3(0, 0, 0);
        foreach (var Lc in new[] { new[] { keyL, keyC }, new[] { rimL, rimC } })
        {
            var L = Lc[0]; var H = (L + V0).N;
            double dotNL = Sat(n.Dot(L)), dotNH = Sat(n.Dot(H)), dotVH = Sat(V0.Dot(H));
            var spec = Fresnel(specC, dotVH) * (SpecVD(rough * rough, dotNL, dotNV, dotNH) * Math.PI);
            col = col + Lc[1] * (diffC + spec) * dotNL;      // 人物不接受阴影（同网页版）
        }
        var hemi = Hemi(n.y);
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
        var rimDir = new D3(viewRot[0] * 0.55 + viewRot[3] * 0.62 + viewRot[6] * -0.56, viewRot[1] * 0.55 + viewRot[4] * 0.62 + viewRot[7] * -0.56, viewRot[2] * 0.55 + viewRot[5] * 0.62 + viewRot[8] * -0.56).N;
        double sgR = 1 - dotNV, sgL = Sat(n.Dot(rimDir) * 0.5 + 0.5), sgR2 = sgR * sgR;
        var rimCc = new D3(1.0, 0.9, 0.78) * (sgR2 * sgR2 * 0.42 * sgL * sgL * 1.6);
        col = col + rimCc * (new D3(0.2, 0.2, 0.2) + albedo * 0.6) * (1 + (0.45 - 1) * t2[1]);
        col = col + m.hitTint * (0.2 + 0.8 * Math.Pow(Math.Max(sgR, 1e-4), 1.6));
        return col;
    }

    // ------------------------------------------------------------ 光栅化
    static int Ws, Hs; static float[] col, dep; static int[] vid; static float[] vk1, vk2; static bool[] vback;
    static readonly List<Tri> fin = new List<Tri>();
    static D3 ToCam(D3 p) { var d = p - camPos; return new D3(d.Dot(camR), d.Dot(camU), d.Dot(camF)); }
    const double NEAR = 0.3;
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
    static void Project(D3 c, out double sx, out double sy) { sx = (c.x / (c.z * tanH) * 0.5 + 0.5) * Ws; sy = (0.5 - c.y / (c.z * tanV) * 0.5) * Hs; }
    static void RasterVis(Tri t0_)
    {
        foreach (var vv in Clip(t0_))
        {
            var t = new Tri { a = vv[0], b = vv[1], c = vv[2], m = t0_.m, T = t0_.T, B = t0_.B, hasTB = t0_.hasTB };
            D3 a = ToCam(t.a.p), b = ToCam(t.b.p), c = ToCam(t.c.p);
            double X0, Y0, X1, Y1, X2, Y2;
            Project(a, out X0, out Y0); Project(b, out X1, out Y1); Project(c, out X2, out Y2);
            double A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
            if (Math.Abs(A) < 1e-12) continue;
            bool back = A < 0;
            if (back && !t.m.cullOff) continue;
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
                    dep[o] = (float)z; vid[o] = id; vk1[o] = (float)(w1 * iz1 * z); vk2[o] = (float)(w2 * iz2 * z); vback[o] = back;
                }
            }
        }
    }
    static Vt At(Tri t, double k1, double k2)
    {
        double k0 = 1 - k1 - k2; var a = t.a; var b = t.b; var c = t.c;
        return new Vt
        {
            p = a.p * k0 + b.p * k1 + c.p * k2, n = a.n * k0 + b.n * k1 + c.n * k2, lp = a.lp * k0 + b.lp * k1 + c.lp * k2, u = a.u * k0 + b.u * k1 + c.u * k2, v = a.v * k0 + b.v * k1 + c.v * k2,
            r = a.r * k0 + b.r * k1 + c.r * k2, g = a.g * k0 + b.g * k1 + c.g * k2, b = a.b * k0 + b.b * k1 + c.b * k2, a = a.a * k0 + b.a * k1 + c.a * k2,
            e0 = a.e0 * k0 + b.e0 * k1 + c.e0 * k2, e1 = a.e1 * k0 + b.e1 * k1 + c.e1 * k2, e2 = a.e2 * k0 + b.e2 * k1 + c.e2 * k2, e3 = a.e3 * k0 + b.e3 * k1 + c.e3 * k2,
        };
    }
    static void RasterBlend(Tri t0_)
    {
        foreach (var vv in Clip(t0_))
        {
            var t = new Tri { a = vv[0], b = vv[1], c = vv[2], m = t0_.m };
            D3 a = ToCam(t.a.p), b = ToCam(t.b.p), c = ToCam(t.c.p);
            double X0, Y0, X1, Y1, X2, Y2;
            Project(a, out X0, out Y0); Project(b, out X1, out Y1); Project(c, out X2, out Y2);
            double A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
            if (Math.Abs(A) < 1e-12 || A < 0) continue;
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
                    double al; D3 src = Fog(ShadeWater(v, out al), z);
                    double cr = col[o * 3], cg = col[o * 3 + 1], cb = col[o * 3 + 2];
                    cr = cr * (1 - al) + src.x * al; cg = cg * (1 - al) + src.y * al; cb = cb * (1 - al) + src.z * al;
                    col[o * 3] = (float)cr; col[o * 3 + 1] = (float)cg; col[o * 3 + 2] = (float)cb;
                }
            }
        }
    }
    // ---- Sanguo/DuelSky（方向换成 three 坐标）
    static D3 skyTop, skyHor, skyLow, skySun, skyDir;
    static D3 Sky(D3 du)
    {
        du = du.N; D3 d = new D3(du.x, du.y, -du.z); double y = d.y;
        D3 c = y > 0 ? Lerp(skyHor, skyTop, Math.Pow(Sat(y * 1.6), 0.6)) : Lerp(skyHor, skyLow, Sat(-y * 4));
        double s = Math.Max(d.Dot(skyDir), 0);
        c = c + skySun * (Math.Pow(s, 220) * 1.2 + Math.Pow(s, 12) * 0.28 + Math.Pow(s, 3) * 0.1);
        double band = Math.Sin(d.x * 9 + d.z * 4) * 0.5 + 0.5;
        return Lerp(c, c * 1.06 + new D3(0.02, 0.02, 0.02), Smooth(0.08, 0.2, y) * (1 - Smooth(0.2, 0.45, y)) * band * 0.6);
    }

    const int SS = 2;
    public static void Frame(Scene sc, string png, int W, int H)
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
        var pal = sc.pal;
        var cam = sc.cam; var ct = cam.transform;
        camPos = ct.position; var rot = ct.rotation;
        camF = (D3)(rot * Vector3.forward); camR = (D3)(rot * Vector3.right); camU = (D3)(rot * Vector3.up);
        viewRot = new[] { camR.x, camR.y, camR.z, camU.x, camU.y, camU.z, -camF.x, -camF.y, -camF.z };
        tanV = Math.Tan(cam.fieldOfView * Math.PI / 360); tanH = tanV * W / (double)H;
        var kf = sc.key.transform.forward; keyL = new D3(-kf.x, -kf.y, -kf.z).N; keyC = C3(sc.key.color) * sc.key.intensity;
        var rf = sc.rim.transform.forward; rimL = new D3(-rf.x, -rf.y, -rf.z).N; rimC = C3(sc.rim.color) * sc.rim.intensity;
        ambSky = Hex(pal.hemiS) * pal.hemiI; ambGnd = Hex(pal.hemiG) * pal.hemiI; ambEq = new D3(0.58, 0.56, 0.5) * 0.56;
        fogC = Hex(pal.fog); fogS = pal.fogN; fogE = pal.fogF;
        skyTop = Hex(pal.top); skyHor = Hex(pal.hor); skyLow = Hex(pal.low); skySun = Hex(pal.sun); skyDir = ((D3)pal.sunDir).N;
        // 物体
        Walk(sc.world);
        // 本帧提交的绘制（植被逐株：实例色在属性块 _InstColor）
        foreach (var d in sc.meshes ?? new List<Graphics.MeshDraw>())
        {
            var m = MatOf(d.mat, null); if (m == null) continue;
            object ic;
            if (d.block != null && d.block.TryGetValue("_InstColor", out ic) && ic is Vector4) { var v4 = (Vector4)ic; m.inst = new D3(v4.x, v4.y, v4.z); }
            m.cast = m.k == K.Veg && m.kind > 1.5;     // 只有石块投影（= 网页版 InstancedMesh.castShadow）
            AddMesh(d.mesh, d.m, m, null, null);
        }
        foreach (var d in sc.inst ?? new List<Graphics.InstancedDraw>())
            for (int i = 0; i < d.count; i++)
            {
                var m = MatOf(d.mat, null); if (m == null) continue;
                if (d.cols != null) m.inst = new D3(d.cols[i].x, d.cols[i].y, d.cols[i].z);
                m.cast = m.k == K.Veg && m.kind > 1.5;
                AddMesh(d.mesh, d.m[i], m, null, null);
            }
        BuildShadow();
        // 画
        Ws = W * SS; Hs = H * SS;
        col = new float[Ws * Hs * 3]; dep = new float[Ws * Hs]; vid = new int[Ws * Hs]; vk1 = new float[Ws * Hs]; vk2 = new float[Ws * Hs]; vback = new bool[Ws * Hs];
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
                case K.Veg: c = ShadeVeg(t, v); break;
                case K.Figure: c = ShadeFigure(t, v); break;
                default: c = ShadeProp(t, v, vback[o]); break;
            }
            c = Fog(c, z);
            col[o * 3] = (float)c.x; col[o * 3 + 1] = (float)c.y; col[o * 3 + 2] = (float)c.z;
        }
        foreach (var t in blend) RasterBlend(t);
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
        col = null; dep = null; vid = null; vk1 = null; vk2 = null; vback = null; smap = null;
    }
}
