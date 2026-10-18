// 人物模型库的软件光栅器（无头，对照网页版 tests/figures.html?shot=1 的截图用）：按 render.mjs 写出的 scenes.json
// （figures.html 的摆放、片段与时刻、镜头）用 C# 移植重建人物 / 士兵 / 战马，摆好姿势后画成 PNG。
//   · 全部在 Unity 坐标里做：网格经 FigConv 换手系（x 镜像、绕序反转），骨骼矩阵 S·M·S，人物 root 绕 Y 转 180° − θ，
//     世界坐标 (x, y, −z)；背面剔除按 Unity 约定（屏幕上顺时针为正面）——换手系做错会直接缺面或左右颠倒。
//   · 蒙皮：Σ w·(骨骼矩阵 × 绑定逆矩阵)，与 SkinnedMeshRenderer 相同；头、兵器、盾挂在骨骼 / 挂点上。
//   · 着色：Sanguo/Figure 着色器（SgStandard）的逐行 C# 参照实现（Shade）——同一套输入与公式：图集反照率 × 顶点色（势力色遮罩）、
//     法线贴图（按三角形的 UV 求切线，= Mesh.RecalculateTangents）、ORM、几何粗糙度（法线的屏幕导数）、
//     three r158 PhysicalMaterial 的直接光（Lambert + GGX）、半球光（= Unity Trilight 环境光，强度已除以 π）、
//     环境贴图 = FigAtlas.MakeEnvCubes 的立方体贴图（与 Unity 的 _EnvCube / _EnvIrr 同一份数据，按 sgEnvLod 取级别、三线性）、
//     DFGApprox 多次散射、AO 与镜面遮蔽、轮廓光、地面贴合。灯光按 Unity 约定（JS 强度 / π）。
//     地面圆盘（径向渐变）接受太阳阴影（阴影贴图 + 3×3 PCF）；线性雾；2×2 超采样抗锯齿。
//   · 不画：人物自阴影（网页版也关闭了 receiveShadow）；立方体贴图跨面的无缝过滤（这里在面内夹紧）。
// 用法：mono figurerender.exe <scenes.json 所在目录>      输出 unity-<视角>.png（由 Tests~/figure/render.sh 调用）
using System;
using System.Collections.Generic;
using System.IO;
using Sanguo;

static class FigureRender
{
    // ------------------------------------------------------------ 小向量
    struct V { public double x, y, z; public V(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static V operator +(V a, V b) { return new V(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static V operator -(V a, V b) { return new V(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static V operator *(V a, double k) { return new V(a.x * k, a.y * k, a.z * k); }
        public static V operator *(V a, V b) { return new V(a.x * b.x, a.y * b.y, a.z * b.z); }
        public double Dot(V b) { return x * b.x + y * b.y + z * b.z; }
        public V Cross(V b) { return new V(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x); }
        public double Len { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public V N { get { double l = Len; return l > 1e-12 ? this * (1 / l) : new V(0, 1, 0); } } }
    static V Col(string hexc) { var c = FigMath.col(hexc); return new V(c.r, c.g, c.b); }
    static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static V Lerp(V a, V b, double t) { return a + (b - a) * t; }

    // 4×4（列主序，同 M4d）
    static V Mul(M4d m, V p) { var e = m.e; return new V(e[0] * p.x + e[4] * p.y + e[8] * p.z + e[12], e[1] * p.x + e[5] * p.y + e[9] * p.z + e[13], e[2] * p.x + e[6] * p.y + e[10] * p.z + e[14]); }
    static V MulD(M4d m, V p) { var e = m.e; return new V(e[0] * p.x + e[4] * p.y + e[8] * p.z, e[1] * p.x + e[5] * p.y + e[9] * p.z, e[2] * p.x + e[6] * p.y + e[10] * p.z); }
    static M4d RootUnity(double x, double z, double yawRad)
    {
        // Unity：localEulerAngles.y = 180 − θ（度），位置 (x, 0, −z)
        double a = (180 - yawRad * 180 / Math.PI) * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var m = M4d.Translation(x, 0, -z);
        // Ry(a)：(x, z) → (x·c + z·s, −x·s + z·c)
        var r = new M4d(); var e = r.e;     // 单位矩阵起步
        e[0] = c; e[2] = -s; e[8] = s; e[10] = c;
        return M4d.Mul(m, r);
    }

    // ------------------------------------------------------------ 纹理
    sealed class Tex
    {
        public int w, h; public float[] d; public bool topFirst;
        public Tex(byte[] rgba, int w, int h, bool topFirst) { this.w = w; this.h = h; this.topFirst = topFirst; d = new float[w * h * 4]; for (int i = 0; i < d.Length; i++) d[i] = rgba[i] / 255f; }
        // (u, v)：v = 0 为纹理底边（Unity / three 约定）
        public void S(double u, double v, float[] o)
        {
            double fy = topFirst ? (1 - v) * h - 0.5 : v * h - 0.5, fx = u * w - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); double tx = fx - x0, ty = fy - y0;
            int xa = Math.Max(0, Math.Min(w - 1, x0)), xb = Math.Max(0, Math.Min(w - 1, x0 + 1)), ya = Math.Max(0, Math.Min(h - 1, y0)), yb = Math.Max(0, Math.Min(h - 1, y0 + 1));
            for (int c = 0; c < 4; c++)
            {
                double a = d[(ya * w + xa) * 4 + c], b = d[(ya * w + xb) * 4 + c], e = d[(yb * w + xa) * 4 + c], f = d[(yb * w + xb) * 4 + c];
                o[c] = (float)((a * (1 - tx) + b * tx) * (1 - ty) + (e * (1 - tx) + f * tx) * ty);
            }
        }
    }
    static Tex atlasMap, atlasNrm, atlasOrm;
    // 环境立方体贴图（FigAtlas.MakeEnvCubes：Unity 的 _EnvCube 128² 8 级、_EnvIrr 8²）；方向为 Unity 世界坐标
    static FigAtlas.EnvCubes envC;
    static V EnvRad(V d, double lod) { var o = new double[3]; envC.Radiance(d.x, d.y, d.z, lod, o); return new V(o[0], o[1], o[2]); }
    static V EnvIrr(V n) { var o = new double[3]; envC.Irradiance(n.x, n.y, n.z, o); return new V(o[0], o[1], o[2]); }
    static void BuildEnv() { envC = FigAtlas.MakeEnvCubes(); }

    // ------------------------------------------------------------ 场景三角形（Unity 世界坐标）
    struct Vt { public V p, n; public double u, v; public float r, g, b, a; }
    sealed class Mat { public Tex map, nrm, orm; public V team; public bool atlas; }
    sealed class Tri { public Vt a, b, c; public Mat m; public V T, B; public double gR; }
    static readonly List<Tri> tris = new List<Tri>();
    static Mat faceMat(Raster face)
    {
        var flatN = new byte[16 * 4]; var flatO = new byte[16 * 4];
        for (int i = 0; i < 16; i++) { flatN[i * 4] = 128; flatN[i * 4 + 1] = 128; flatN[i * 4 + 2] = 255; flatN[i * 4 + 3] = 255; flatO[i * 4] = 255; flatO[i * 4 + 1] = 178; flatO[i * 4 + 2] = 0; flatO[i * 4 + 3] = 255; }
        return new Mat { map = new Tex(face.ToRGBA8(), face.W, face.H, true), nrm = new Tex(flatN, 4, 4, false), orm = new Tex(flatO, 4, 4, false), team = new V(1, 1, 1) };
    }
    // 网格 g（three 坐标）→ Unity：顶点 x 镜像，再按 skin（每骨 Unity 矩阵）或 rigid（单个 Unity 矩阵）变换到世界
    static void AddGeo(FigGeo g, Mat m, M4d world, M4d[] skin, bool ground)
    {
        int n = g.Verts;
        var vs = new Vt[n];
        for (int i = 0; i < n; i++)
        {
            var p = new V(-g.pos[i * 3], g.pos[i * 3 + 1], g.pos[i * 3 + 2]);
            var nn = new V(-g.nrm[i * 3], g.nrm[i * 3 + 1], g.nrm[i * 3 + 2]);
            V sp = p, sn = nn;
            if (skin != null)
            {
                sp = new V(0, 0, 0); sn = new V(0, 0, 0);
                for (int k = 0; k < 4; k++)
                {
                    double w = g.sw[i * 4 + k]; if (w == 0) continue;
                    var M = skin[g.si[i * 4 + k]];
                    sp = sp + Mul(M, p) * w; sn = sn + MulD(M, nn) * w;
                }
                if (ground) sp.y = Math.Max(sp.y, 0.006);    // 地面贴合（着色器）：人物局部 y ≥ 0.006
            }
            vs[i] = new Vt { p = Mul(world, sp), n = MulD(world, sn).N, u = g.uv[i * 2], v = g.uv[i * 2 + 1], r = g.col[i * 4], g = g.col[i * 4 + 1], b = g.col[i * 4 + 2], a = g.col[i * 4 + 3] };
        }
        var idx = FigConv.Tris(g.idx);
        for (int i = 0; i < idx.Length; i += 3)
        {
            var t = new Tri { a = vs[idx[i]], b = vs[idx[i + 1]], c = vs[idx[i + 2]], m = m };
            // 切线（= Mesh.RecalculateTangents 的做法：由位置与 UV 求 dP/du、dP/dv）
            V e1 = t.b.p - t.a.p, e2 = t.c.p - t.a.p;
            double du1 = t.b.u - t.a.u, dv1 = t.b.v - t.a.v, du2 = t.c.u - t.a.u, dv2 = t.c.v - t.a.v, det = du1 * dv2 - du2 * dv1;
            if (Math.Abs(det) > 1e-14) { t.T = (e1 * dv2 - e2 * dv1) * (1 / det); t.B = (e2 * du1 - e1 * du2) * (1 / det); }
            tris.Add(t);
        }
    }
    static M4d U(M4d threeM) { return FigConv.M(threeM); }
    static readonly Dictionary<string, FigTemplate> tplCache = new Dictionary<string, FigTemplate>();
    static readonly Dictionary<string, Mat> faceCache = new Dictionary<string, Mat>();
    static Mat AtlasMat(string team) { return new Mat { map = atlasMap, nrm = atlasNrm, orm = atlasOrm, team = Col(team ?? "#ffffff"), atlas = true }; }

    static void AddFigure(JObj it)
    {
        FigLook look; string key;
        if (it.S("kind") == "soldier")
        {
            var o = new FigOpts { type = it.S("type"), culture = it.S("culture"), lod = it.S("lod"), seed = (int)it.D("seed"), color = it.S("color") };
            look = FigBuild.lookOfSoldier(o); key = FigBuild.soldierKey(look);
        }
        else
        {
            var g = it.O("gen");
            var gen = new PortraitGen { name = g.S("name"), war = g.Has("war") ? g.D("war") : (double?)null, intel = g.Has("intel") ? g.D("intel") : (double?)null, pol = g.Has("pol") ? g.D("pol") : (double?)null, culture = g.S("culture"), color = g.S("color") };
            var o = new FigOpts { lod = it.S("lod"), color = it.S("color") };
            look = FigBuild.lookOfGeneral(gen, o); key = FigBuild.generalKey(look, o);
        }
        FigTemplate tpl;
        if (!tplCache.TryGetValue(key, out tpl)) { look.key = key; tpl = FigBuild.buildTemplate(look); tplCache[key] = tpl; }
        var rig = new FigRig(tpl);
        string clip = it.S("clip") ?? "idle";
        var info = FigClips.Info(clip) ?? FigClips.Info("idle");
        double tt = it.G("tf") != null ? it.D("tf") * info.dur : (info.loop ? 0 : Math.Min(0 % (info.dur + 0.8), info.dur));
        string cl = FigClips.Info(clip) != null ? clip : "idle";
        M4d root;
        var ho = it.O("horse");
        if (ho != null)
        {
            var hopt = new FigHorseOpts { lod = ho.S("lod"), seed = (int)ho.D("seed"), color = ho.S("color"), name = ho.S("name"), armor = ho.S("armor"), general = ho.T("general") };
            var ht = FigBuild.buildHorseTemplate(hopt);
            var hr = new FigHorseRig(ht);
            rig.mounted = hr; hr.riders.Add(rig);
            rig.SetPose(rig.hasClip ? rig.clipName : "idle", rig.hasClip ? rig.clipT : 0, null);       // JS mount
            string hc = ho.S("clip");
            hr.SetPose(hc, 0);
            rig.SetPose(rig.clipName, rig.clipT, new FigPoseOpts { gallop = hc == "gallop" });      // JS Horse.setPose 的骑手同步
            rig.SetPose(cl, tt, new FigPoseOpts { gallop = clip == "gallop" });
            var hroot = RootUnity(it.D("x"), it.D("z"), it.D("yaw"));
            var hm = hr.BoneMatrices();
            var hskin = new M4d[hm.Length];
            for (int i = 0; i < hm.Length; i++) hskin[i] = U(M4d.Mul(hm[i], ht.inv[i]));
            AddGeo(ht.geometry, AtlasMat(hopt.color ?? "#8a2a24"), hroot, hskin, false);
            // 骑手 root = 鞍座骨 × (0, −J.pelvis.y + 0.08, −0.04)
            var off = FigHorseRig.RiderOffset(rig);
            root = M4d.Mul(hroot, U(M4d.Mul(hm[FigHorseRig.Saddle], M4d.Translation(off.x, off.y, off.z))));
        }
        else
        {
            rig.SetPose(cl, tt, null);
            root = RootUnity(it.D("x"), it.D("z"), it.D("yaw"));
        }
        var bm = rig.BoneMatrices();
        var skin = new M4d[bm.Length];
        for (int i = 0; i < bm.Length; i++) skin[i] = U(M4d.Mul(bm[i], tpl.boneInverses[i]));
        var mat = AtlasMat(Js.Or(it.S("color"), look.factionHex));
        AddGeo(tpl.geometry, mat, root, skin, rig.groundClamp && rig.mounted == null);
        Func<int, M4d> boneW = i => M4d.Mul(root, U(bm[i]));
        if (tpl.headGeo != null)
        {
            string fk = FigTexturesKey(look, tpl.faceSize);
            Mat fm;
            if (!faceCache.TryGetValue(fk, out fm)) faceCache[fk] = fm = faceMat(FigAtlas.PaintFace(FigFaceDesc.FromSpec(look.spec ?? new JObj()), tpl.faceSize));
            AddGeo(tpl.headGeo, fm, boneW(FigBones.head), null, false);
        }
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
    static string FigTexturesKey(FigLook look, int size) { var sp = look.spec; return ((sp != null ? (sp.S("key") ?? sp.S("name")) : null) ?? "?") + "|" + size; }

    // ------------------------------------------------------------ 光照（figures.html：半球光、太阳、逆光）
    // Unity 约定：灯光强度 = JS 强度 / π（太阳 0.85、逆光 0.55），Trilight 环境色 = 半球光颜色 × 0.5
    static V sunL, rimL, camPos, camF, camR, camU;
    static readonly V SKY = Col("#9aaccc"), GND = Col("#4a3c2e"), SUN = Col("#fff0d8"), RIMC = Col("#ffc890"), BG = new V(0.2, 0.21, 0.25);
    static readonly V RimDirView = new V(0.55, 0.62, -0.56).N;
    static V rimDirW;
    // ---- Sanguo/Figure 着色器的 BRDF（sgFresnel / sgSpecVD / sgDFG / sgEnvLod），逐行对照
    static V Fresnel(V f0, double dotVH) { double fr = Math.Pow(2, (-5.55473 * dotVH - 6.98316) * dotVH); return f0 * (1 - fr) + new V(fr, fr, fr); }
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
    static double EnvLod(double r) { return FigAtlas.EnvCubes.Lod(r); }
    static double Sat(double v) { return Clamp01(v); }
    static V Reflect(V i, V n) { return i - n * (2 * n.Dot(i)); }
    static V ShadeFigure(Tri t, V p, V nGeo, double u, double v, V vc, double va)
    {
        // ---- surf
        var m = t.m; var tex = new float[4]; var nm = new float[4]; var orm = new float[4];
        m.map.S(u, v, tex); m.nrm.S(u, v, nm); m.orm.S(u, v, orm);
        var vcol = Lerp(vc, vc * m.team, va);
        var albedo = new V(tex[0], tex[1], tex[2]) * Lerp(new V(1, 1, 1), vcol, tex[3]);
        double rough = Math.Min(Math.Max(orm[1], 0.0525) + t.gR, 1), metal = orm[2];
        double ao = 1 + (orm[0] - 1) * 0.8;
        var n = nGeo;
        var V0 = (camPos - p).N;
        var mn = new V(nm[0] * 2 - 1, nm[1] * 2 - 1, nm[2] * 2 - 1).N;
        if (t.T.Len > 0 && t.B.Len > 0)
        {
            var T = (t.T - n * n.Dot(t.T)).N;
            var Bc = n.Cross(T); if (Bc.Dot(t.B) < 0) Bc = Bc * -1;
            n = (T * mn.x + Bc * mn.y + n * mn.z).N;
        }
        var diffC = albedo * (1 - metal);
        var specC = Lerp(new V(0.04, 0.04, 0.04), albedo, metal);
        double dotNV = Sat(n.Dot(V0));
        // ---- LightingSgStandard：直接光（太阳、逆光）
        V col = new V(0, 0, 0);
        foreach (var L in new[] { new { d = sunL, c = SUN * 0.85 }, new { d = rimL, c = RIMC * 0.55 } })
        {
            var H = (L.d + V0).N;
            double dotNL = Sat(n.Dot(L.d)), dotNH = Sat(n.Dot(H)), dotVH = Sat(V0.Dot(H));
            var spec = Fresnel(specC, dotVH) * (SpecVD(rough * rough, dotNL, dotNV, dotNH) * Math.PI);
            col = col + L.c * (diffC + spec) * dotNL;
        }
        // ---- LightingSgStandard_GI：球谐环境光（= 半球光 / π）与环境贴图
        var hemi = Lerp(GND, SKY, 0.5 * n.y + 0.5) * 0.5;
        var irr = EnvIrr(n) * 0.85;
        var R = Lerp(Reflect(V0 * -1, n), n, rough * rough).N;
        var rad = EnvRad(R, EnvLod(rough)) * 0.85;
        double fa, fb; DFG(dotNV, rough, out fa, out fb);
        var FssEss = specC * fa + new V(fb, fb, fb);
        double Ems = 1 - (fa + fb);
        var Favg = specC + (new V(1, 1, 1) - specC) * 0.047619;
        var ms = new V(FssEss.x * Favg.x / (1 - Ems * Favg.x), FssEss.y * Favg.y / (1 - Ems * Favg.y), FssEss.z * Favg.z / (1 - Ems * Favg.z)) * Ems;
        var tot = FssEss + ms;
        var diffE = diffC * (1 - Math.Max(tot.x, Math.Max(tot.y, tot.z)));
        double specAO = Sat(Math.Pow(dotNV + ao, Math.Pow(2, -16 * rough - 1)) - 1 + ao);
        col = col + (hemi * diffC + diffE * irr) * ao + (rad * FssEss + ms * irr) * specAO;
        // ---- 自发光：轮廓光（镜头空间右上后方；粗糙度用贴图原值）
        double sgR = 1 - dotNV, sgL = Sat(n.Dot(rimDirW) * 0.5 + 0.5), sgR2 = sgR * sgR;
        var rimC = new V(1.0, 0.9, 0.78) * (sgR2 * sgR2 * 0.42 * sgL * sgL * 1.6);
        col = col + rimC * (new V(0.2, 0.2, 0.2) + albedo * 0.6) * (1 + (0.45 - 1) * orm[1]);
        return col;
    }

    // ------------------------------------------------------------ 地面阴影贴图（太阳，正交）
    static int SM = 1024; static float[] smap; static double smX0, smY0, smS; static V smR, smU;
    static void BuildShadow()
    {
        smR = new V(0, 1, 0).Cross(sunL).N; smU = sunL.Cross(smR).N;
        double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9;
        foreach (var t in tris) foreach (var q in new[] { t.a.p, t.b.p, t.c.p }) { double a = q.Dot(smR), b = q.Dot(smU); x0 = Math.Min(x0, a); x1 = Math.Max(x1, a); y0 = Math.Min(y0, b); y1 = Math.Max(y1, b); }
        smX0 = x0 - 0.2; smY0 = y0 - 0.2; smS = Math.Max(x1 - x0, y1 - y0) + 0.4;
        smap = new float[SM * SM]; for (int i = 0; i < smap.Length; i++) smap[i] = float.MinValue;
        foreach (var t in tris)
        {
            var P = new[] { t.a.p, t.b.p, t.c.p };
            double[] X = new double[3], Y = new double[3], Z = new double[3];
            for (int k = 0; k < 3; k++) { X[k] = (P[k].Dot(smR) - smX0) / smS * SM; Y[k] = (P[k].Dot(smU) - smY0) / smS * SM; Z[k] = P[k].Dot(sunL); }
            double A = (X[1] - X[0]) * (Y[2] - Y[0]) - (Y[1] - Y[0]) * (X[2] - X[0]);
            if (Math.Abs(A) < 1e-12) continue;
            int mx = Math.Max(0, (int)Math.Floor(Math.Min(X[0], Math.Min(X[1], X[2])))), Mx = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(X[0], Math.Max(X[1], X[2]))));
            int my = Math.Max(0, (int)Math.Floor(Math.Min(Y[0], Math.Min(Y[1], Y[2])))), My = Math.Min(SM - 1, (int)Math.Ceiling(Math.Max(Y[0], Math.Max(Y[1], Y[2]))));
            for (int y = my; y <= My; y++) for (int x = mx; x <= Mx; x++)
            {
                double sx = x + 0.5, sy = y + 0.5;
                double w0 = ((X[1] - sx) * (Y[2] - sy) - (Y[1] - sy) * (X[2] - sx)) / A, w1 = ((X[2] - sx) * (Y[0] - sy) - (Y[2] - sy) * (X[0] - sx)) / A, w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = (float)(w0 * Z[0] + w1 * Z[1] + w2 * Z[2]);
                if (z > smap[y * SM + x]) smap[y * SM + x] = z;
            }
        }
    }
    static double Lit(V p)
    {
        double fx = (p.Dot(smR) - smX0) / smS * SM, fy = (p.Dot(smU) - smY0) / smS * SM, z = p.Dot(sunL);
        int cx = (int)fx, cy = (int)fy; double s = 0;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if (x < 0 || y < 0 || x >= SM || y >= SM) { s += 1; continue; }
            s += smap[y * SM + x] > z + 0.01 ? 0 : 1;
        }
        return s / 9;
    }

    // ------------------------------------------------------------ 光栅化
    static int W, H; static double tanV, tanH; static float[] col, dep;
    const int SSK = 2;      // 超采样倍数（Main 的 SS）
    static V ToCam(V p) { var d = p - camPos; return new V(d.Dot(camR), d.Dot(camU), d.Dot(camF)); }
    static long culled, drawn;
    static void Raster(Tri t)
    {
        V a = ToCam(t.a.p), b = ToCam(t.b.p), c = ToCam(t.c.p);
        const double near = 0.05;
        if (a.z < near || b.z < near || c.z < near) return;      // 近处人物不会穿过近平面
        double ax = a.x / (a.z * tanH), ay = a.y / (a.z * tanV), bx = b.x / (b.z * tanH), by = b.y / (b.z * tanV), cx = c.x / (c.z * tanH), cy = c.y / (c.z * tanV);
        double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (area >= 0) { culled++; return; }                     // Unity：屏幕上逆时针（NDC 面积 > 0）= 背面
        double X0 = (ax * 0.5 + 0.5) * W, Y0 = (0.5 - ay * 0.5) * H, X1 = (bx * 0.5 + 0.5) * W, Y1 = (0.5 - by * 0.5) * H, X2 = (cx * 0.5 + 0.5) * W, Y2 = (0.5 - cy * 0.5) * H;
        double A = (X1 - X0) * (Y2 - Y0) - (Y1 - Y0) * (X2 - X0);
        if (Math.Abs(A) < 1e-12) return;
        {
            // 几何粗糙度（three geometryRoughness = max(|dFdx(n)|, |dFdy(n)|) 的最大分量）：法线在屏幕上的仿射梯度，按最终分辨率（× 超采样倍数）
            double g = 0;
            foreach (var k in new[] { 0, 1, 2 })
            {
                double a0 = k == 0 ? t.a.n.x : k == 1 ? t.a.n.y : t.a.n.z, a1 = k == 0 ? t.b.n.x : k == 1 ? t.b.n.y : t.b.n.z, a2 = k == 0 ? t.c.n.x : k == 1 ? t.c.n.y : t.c.n.z;
                double ddx = ((a1 - a0) * (Y2 - Y0) - (a2 - a0) * (Y1 - Y0)) / A, ddy = ((a2 - a0) * (X1 - X0) - (a1 - a0) * (X2 - X0)) / A;
                g = Math.Max(g, Math.Max(Math.Abs(ddx), Math.Abs(ddy)) * SSK);
            }
            t.gR = g;
        }
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(X0, Math.Min(X1, X2)))), maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(X0, Math.Max(X1, X2))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(Y0, Math.Min(Y1, Y2)))), maxY = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(Y0, Math.Max(Y1, Y2))));
        if (minX > maxX || minY > maxY) return;
        drawn++;
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
                int o = py * W + px;
                if (z >= dep[o]) continue;
                double k0 = w0 * iz0 * z, k1 = w1 * iz1 * z, k2 = w2 * iz2 * z;
                var p = t.a.p * k0 + t.b.p * k1 + t.c.p * k2;
                var n = (t.a.n * k0 + t.b.n * k1 + t.c.n * k2).N;
                double u = t.a.u * k0 + t.b.u * k1 + t.c.u * k2, v = t.a.v * k0 + t.b.v * k1 + t.c.v * k2;
                var vc = new V(t.a.r * k0 + t.b.r * k1 + t.c.r * k2, t.a.g * k0 + t.b.g * k1 + t.c.g * k2, t.a.b * k0 + t.b.b * k1 + t.c.b * k2);
                double va = t.a.a * k0 + t.b.a * k1 + t.c.a * k2;
                var rgb = ShadeFigure(t, p, n, u, v, vc, va);
                rgb = Fog(rgb, z);
                dep[o] = (float)z;
                col[o * 3] = (float)rgb.x; col[o * 3 + 1] = (float)rgb.y; col[o * 3 + 2] = (float)rgb.z;
            }
        }
    }
    static V Fog(V c, double z) { double f = Clamp01((z - 18) / (60 - 18)); return Lerp(c, BG, f); }
    // 背景与地面（半径 30 的圆盘，径向渐变贴图 256²：r0 = 10、r1 = 128 像素，#6a6458 → 0.7 #3c3832 → #2a2826）
    static V GroundAlbedo(double r)
    {
        double dpx = r / 30 * 128, t = Clamp01((dpx - 10) / 118);
        V a = Col("#6a6458"), b = Col("#3c3832"), c = Col("#2a2826");
        return t < 0.7 ? Lerp(a, b, t / 0.7) : Lerp(b, c, (t - 0.7) / 0.3);
    }
    static void Background()
    {
        var up = new V(0, 1, 0);
        for (int py = 0; py < H; py++) for (int px = 0; px < W; px++)
        {
            double nx = ((px + 0.5) / W * 2 - 1) * tanH, ny = (1 - (py + 0.5) / H * 2) * tanV;
            var d = (camF + camR * nx + camU * ny);
            int o = py * W + px;
            V c = BG;
            if (d.y < 0)
            {
                double s = -camPos.y / d.y;
                var p = camPos + d * s;
                double r = Math.Sqrt(p.x * p.x + p.z * p.z);
                if (r < 30)
                {
                    var alb = GroundAlbedo(r);
                    double sh = Lit(p);
                    var lit = SKY * 0.5 + SUN * (0.85 * Clamp01(sunL.y) * sh) + RIMC * (0.55 * Clamp01(rimL.y));
                    c = Fog(alb * lit, s);         // 雾按视深：s 为沿 d 的距离，d·camF = 1 → 视深 = s
                }
            }
            col[o * 3] = (float)c.x; col[o * 3 + 1] = (float)c.y; col[o * 3 + 2] = (float)c.z;
            dep[o] = float.MaxValue;
        }
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "out/figure/render";
        var S = (JObj)JObj.ParseJson(File.ReadAllText(Path.Combine(dir, "scenes.json")));
        double t0 = FigMath.Now();
        var A = FigAtlas.Data;
        atlasMap = new Tex(A.map, FigAtlas.N, FigAtlas.N, false); atlasNrm = new Tex(A.nrm, FigAtlas.N, FigAtlas.N, false); atlasOrm = new Tex(A.orm, FigAtlas.N, FigAtlas.N, false);
        BuildEnv();
        Console.WriteLine(string.Format("图集与环境贴图 {0:F0} ms", FigMath.Now() - t0));
        const int SS = SSK;
        foreach (var kv in S)
        {
            double t1 = FigMath.Now();
            var sc = (JObj)kv.Value;
            int w = (int)sc.D("w"), h = (int)sc.D("h");
            W = w * SS; H = h * SS;
            var cam = sc.L("cam"); var tg = sc.L("target");
            // three → Unity：(x, y, −z)
            Func<List<object>, int, double> D = (l, i) => Convert.ToDouble(l[i]);
            camPos = new V(D(cam, 0), D(cam, 1), -D(cam, 2));
            var target = new V(D(tg, 0), D(tg, 1), -D(tg, 2));
            camF = (target - camPos).N; camR = new V(0, 1, 0).Cross(camF).N; camU = camF.Cross(camR).N;
            tanV = Math.Tan(sc.D("fov") / 2 * Math.PI / 180); tanH = tanV * w / h;
            // 光：太阳在 (−4, 7, 6)、目标 = 镜头目标；逆光 (5, 4, −6) → 原点（three 坐标）
            var tgT = new V(D(tg, 0), D(tg, 1), D(tg, 2));
            var sl = (new V(-4, 7, 6) - tgT).N; sunL = new V(sl.x, sl.y, -sl.z);
            var rl = new V(5, 4, -6).N; rimL = new V(rl.x, rl.y, -rl.z);
            // 镜头空间 → 世界：x = camR、y = camU、z = 指向镜头（−camF）
            rimDirW = (camR * RimDirView.x + camU * RimDirView.y + camF * -RimDirView.z).N;
            tris.Clear();
            foreach (JObj it in sc.L("items")) AddFigure(it);
            col = new float[W * H * 3]; dep = new float[W * H];
            BuildShadow();
            Background();
            culled = drawn = 0;
            foreach (var t in tris) Raster(t);
            // 2×2 超采样 → PNG
            var o = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double s = 0;
                    for (int j = 0; j < SS; j++) for (int i = 0; i < SS; i++) s += Clamp01(col[((y * SS + j) * W + x * SS + i) * 3 + c]);
                    o[(y * w + x) * 4 + c] = (byte)Math.Round(s / (SS * SS) * 255);
                }
                o[(y * w + x) * 4 + 3] = 255;
            }
            FigPng.Save(Path.Combine(dir, "unity-" + kv.Key + ".png"), o, w, h);
            // 人物像素的遮罩（2×2 子像素全部落在人物上；render.mjs compose 只在这些像素上统计差异，不计地面与背景）
            var mk = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                bool all = true;
                for (int j = 0; j < SS; j++) for (int i = 0; i < SS; i++) if (dep[(y * SS + j) * W + x * SS + i] == float.MaxValue) all = false;
                byte b = all ? (byte)255 : (byte)0; mk[(y * w + x) * 4] = b; mk[(y * w + x) * 4 + 1] = b; mk[(y * w + x) * 4 + 2] = b; mk[(y * w + x) * 4 + 3] = 255;
            }
            FigPng.Save(Path.Combine(dir, "mask-" + kv.Key + ".png"), mk, w, h);
            Console.WriteLine(string.Format("  unity-{0}.png：{1} 个三角形（画 {2}、背面剔除 {3}），{4:F0} ms", kv.Key, tris.Count, drawn, culled, FigMath.Now() - t1));
        }
        return 0;
    }
}
