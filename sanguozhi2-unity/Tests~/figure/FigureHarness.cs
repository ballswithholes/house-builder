// 人物模型库的无头比对（UNITY-V3 §4）：读取 refs.js 导出的网页版参考值（refs.json + refs.bin），
// 用 C# 移植（View/FigureMesh.cs、FigureRig.cs）重建同样的外观 / 模板 / 姿势，逐项比对：
//   外观解析（头像外貌参数、衣甲层、兵器、颜色）、三角形数与压缩级别、各部位三角形数、
//   网格逐顶点（位置 / 法线 / UV / 顶点色 / 蒙皮骨与权重 / 索引）、兵器信息、
//   骨骼世界矩阵（全部片段 × 3 个时刻、选项、grip / ikHand / 弹簧）、骑乘、士兵群烘焙（半精度逐位）。
// 另检查换手系（FigConv）：x 镜像后三角形绕序反转、法线仍朝外（Unity 背面剔除）、矩阵换算 = S·M·S。
// 用法：mono figure.exe <参考值目录> [过滤]       退出码 = 失败项数
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sanguo;

static class FigureHarness
{
    static byte[] bin;
    static int fails, checks;
    static readonly List<string> failLines = new List<string>();
    static void Fail(string s) { fails++; if (failLines.Count < 400) failLines.Add(s); }
    static float[] F32(double off, int n) { var a = new float[n]; Buffer.BlockCopy(bin, (int)off, a, 0, n * 4); return a; }
    static uint[] U32(double off, int n) { var a = new uint[n]; Buffer.BlockCopy(bin, (int)off, a, 0, n * 4); return a; }
    static ushort[] U16(double off, int n) { var a = new ushort[n]; Buffer.BlockCopy(bin, (int)off, a, 0, n * 2); return a; }
    static double[] F64(double off, int n) { var a = new double[n]; Buffer.BlockCopy(bin, (int)off, a, 0, n * 8); return a; }
    static double D(object o) { return o is double ? (double)o : 0; }

    // ---- JSON 值的语义比较（键无序；数值容差；JS undefined / null 与缺失等价）
    static bool Same(object a, object b, double tol, string path, List<string> diffs)
    {
        if (a == null && b == null) return true;
        if ((a == null && b is bool && !(bool)b) || (b == null && a is bool && !(bool)a)) return true;   // JS undefined 与 false
        if (a is double || b is double)
        {
            double x = a is double ? (double)a : double.NaN, y = b is double ? (double)b : double.NaN;
            if (Math.Abs(x - y) <= tol * Math.Max(1, Math.Abs(x))) return true;
            diffs.Add(path + ": " + Show(a) + " ≠ " + Show(b)); return false;
        }
        var oa = a as JObj; var ob = b as JObj;
        if (oa != null || ob != null)
        {
            if (oa == null || ob == null) { diffs.Add(path + ": " + Show(a) + " ≠ " + Show(b)); return false; }
            bool ok = true;
            foreach (var k in oa.Keys.Union(ob.Keys)) if (!Same(oa.G(k), ob.G(k), tol, path + "." + k, diffs)) ok = false;
            return ok;
        }
        var la = a as List<object>; var lb = b as List<object>;
        if (la != null || lb != null)
        {
            if (la == null || lb == null || la.Count != lb.Count) { diffs.Add(path + ": " + Show(a) + " ≠ " + Show(b)); return false; }
            bool ok = true;
            for (int i = 0; i < la.Count; i++) if (!Same(la[i], lb[i], tol, path + "[" + i + "]", diffs)) ok = false;
            return ok;
        }
        if (Equals(a, b)) return true;
        diffs.Add(path + ": " + Show(a) + " ≠ " + Show(b)); return false;
    }
    static string Show(object v) { var s = v == null ? "null" : JObj.ToJson(v); return s.Length > 120 ? s.Substring(0, 120) + "…" : s; }
    static List<object> Col(FCol c) { return new List<object> { c.r, c.g, c.b, c.m }; }
    static List<object> Col(FCol? c) { return c.HasValue ? Col(c.Value) : null; }
    static List<object> Vec(V3d v) { return new List<object> { v.x, v.y, v.z }; }
    static List<object> Vec(V3d? v) { return v.HasValue ? Vec(v.Value) : null; }
    static JObj LookSummary(FigLook l)
    {
        var o = new JObj
        {
            { "kind", l.kind }, { "name", l.name }, { "culture", l.culture }, { "sex", l.sex }, { "age", l.age }, { "height", l.height }, { "build", l.build }, { "fat", l.fat }, { "skin", l.skin },
            { "weapon", l.weapon }, { "shield", l.shield }, { "backBow", l.backBow }, { "quiver", l.quiver }, { "ribbons", l.ribbons }, { "face", l.face },
            { "hair", l.hair }, { "beard", l.beard }, { "mus", l.mus }, { "hat", l.hat }, { "L", l.L }, { "spec", l.spec },
        };
        if (l.kind == "soldier") o["seed"] = (double)l.seed;
        var C = new JObj { { "faction", Col(l.C.faction) }, { "cloth", Col(l.C.cloth) }, { "cloth2", Col(l.C.cloth2) }, { "metal", Col(l.C.metal) }, { "trim", Col(l.C.trim) },
            { "leather", Col(l.C.leather) }, { "skin", Col(l.C.skin) }, { "cape", Col(l.C.cape) }, { "tabard", Col(l.C.tabard) }, { "plume", Col(l.C.plume) }, { "trousers", Col(l.C.trousers) } };
        o["C"] = C;
        return o;
    }
    // JS 端颜色可能是 '#hex'（字符串，无遮罩）或数组；统一成 4 元素数组
    static object NormColors(object v)
    {
        var o = v as JObj; if (o == null) return v;
        var r = new JObj();
        foreach (var kv in o)
        {
            if (kv.Value is string) { var c = FigMath.col((string)kv.Value); r[kv.Key] = Col(c); }
            else if (kv.Value is List<object>) { var l = (List<object>)kv.Value; r[kv.Key] = new List<object> { l[0], l[1], l[2], l.Count > 3 && l[3] != null ? l[3] : 0.0 }; }
            else r[kv.Key] = kv.Value;
        }
        return r;
    }
    static void CompareLook(string what, JObj js, FigLook cs)
    {
        checks++;
        var a = new JObj(js); a["C"] = NormColors(js.O("C"));
        var b = LookSummary(cs);
        // spec 的 key 字段含年份等，JS 与 C# 同格式；比较全部字段
        var diffs = new List<string>();
        if (!Same(a, b, 1e-9, what + ".look", diffs)) Fail(what + " 外观不同：" + string.Join("；", diffs.Take(6)));
    }

    // ---- 网格逐顶点比较
    static void CompareGeo(string what, JObj js, FigGeo g)
    {
        checks++;
        if (js == null && g == null) return;
        if (js == null || g == null) { Fail(what + "：一方没有几何体（JS " + (js != null) + "，C# " + (g != null) + "）"); return; }
        int nv = (int)js.D("verts"), nt = (int)js.D("tris");
        if (nv != g.Verts || nt != g.Tris) { Fail(what + "：顶点 / 三角形 " + g.Verts + "/" + g.Tris + " ≠ JS " + nv + "/" + nt); return; }
        var pos = F32(js.D("pos"), nv * 3); var nrm = F32(js.D("nrm"), nv * 3); var uv = F32(js.D("uv"), nv * 2); var col = F32(js.D("col"), nv * 4); var idx = U32(js.D("idx"), nt * 3);
        double mp = 0, mn = 0, mu = 0, mc = 0, msw = 0; int bad = 0, firstBad = -1; string firstMsg = null;
        for (int i = 0; i < nv; i++)
        {
            double dp = 0, dn = 0;
            for (int k = 0; k < 3; k++) { dp = Math.Max(dp, Math.Abs(pos[i * 3 + k] - g.pos[i * 3 + k])); dn = Math.Max(dn, Math.Abs(nrm[i * 3 + k] - g.nrm[i * 3 + k])); }
            double du = Math.Max(Math.Abs(uv[i * 2] - g.uv[i * 2]), Math.Abs(uv[i * 2 + 1] - g.uv[i * 2 + 1]));
            double dc = 0; for (int k = 0; k < 4; k++) dc = Math.Max(dc, Math.Abs(col[i * 4 + k] - g.col[i * 4 + k]));
            mp = Math.Max(mp, dp); mn = Math.Max(mn, dn); mu = Math.Max(mu, du); mc = Math.Max(mc, dc);
            if (dp > 1e-4 || dn > 2e-3 || du > 1e-5 || dc > 1e-5)
            {
                bad++;
                if (firstBad < 0) { firstBad = i; firstMsg = string.Format(CultureInfo.InvariantCulture, "v{0} pos JS({1:F5},{2:F5},{3:F5}) C#({4:F5},{5:F5},{6:F5}) n {7:F4} uv {8:F6} col {9:F5}", i, pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2], g.pos[i * 3], g.pos[i * 3 + 1], g.pos[i * 3 + 2], dn, du, dc); }
            }
        }
        double siOff = js.D("si");
        if (siOff >= 0)
        {
            var si = F32(siOff, nv * 4); var sw = F32(js.D("sw"), nv * 4);
            if (g.si == null) { Fail(what + "：C# 缺蒙皮属性"); return; }
            for (int i = 0; i < nv; i++)
            {
                // 4 个影响按 (骨, 权重) 比较（顺序相同）
                for (int k = 0; k < 4; k++)
                {
                    double ws = sw[i * 4 + k], wc = g.sw[i * 4 + k];
                    msw = Math.Max(msw, Math.Abs(ws - wc));
                    if (Math.Abs(ws - wc) > 1e-5 || (ws > 1e-6 && (int)si[i * 4 + k] != g.si[i * 4 + k]))
                    {
                        bad++;
                        if (firstBad < 0) { firstBad = i; firstMsg = "v" + i + " 蒙皮 JS(" + si[i * 4 + k] + ":" + ws + ") C#(" + g.si[i * 4 + k] + ":" + wc + ") 第 " + k + " 项"; }
                        break;
                    }
                }
            }
        }
        int badIdx = 0;
        for (int i = 0; i < nt * 3; i++) if (idx[i] != g.idx[i]) { badIdx++; if (firstMsg == null) firstMsg = "索引 " + i + "：JS " + idx[i] + " C# " + g.idx[i]; }
        var bmin = js.L("bmin"); var bmax = js.L("bmax");
        double db = 0; for (int k = 0; k < 3; k++) { db = Math.Max(db, Math.Abs(D(bmin[k]) - g.bmin[k])); db = Math.Max(db, Math.Abs(D(bmax[k]) - g.bmax[k])); }
        if (bad > 0 || badIdx > 0 || db > 1e-4)
            Fail(string.Format(CultureInfo.InvariantCulture, "{0}：{1} 个顶点不同、{2} 个索引不同（最大差：位置 {3:E2} 法线 {4:E2} UV {5:E2} 色 {6:E2} 权重 {7:E2} 包围盒 {8:E2}）；首个 {9}", what, bad, badIdx, mp, mn, mu, mc, msw, db, firstMsg));
        maxPos = Math.Max(maxPos, mp); maxNrm = Math.Max(maxNrm, mn);
    }
    static double maxPos, maxNrm, maxMat;
    // FigConv 的自检：x 镜像后三角形法线（绕序）与顶点法线方向一致（法线朝外的网格在 Unity 里不被背面剔除）
    static int convChecked, convBad;
    static void CheckConv(FigGeo g, string what = null)
    {
        if (g == null) return;
        var p = new float[g.pos.Length]; FigConv.Pos(g.pos, p); var n = new float[g.nrm.Length]; FigConv.Pos(g.nrm, n); var t = FigConv.Tris(g.idx);
        int agree = 0, total = 0;
        for (int f = 0; f < t.Length; f += 3)
        {
            int a = t[f], b = t[f + 1], c = t[f + 2];
            double ux = p[b * 3] - p[a * 3], uy = p[b * 3 + 1] - p[a * 3 + 1], uz = p[b * 3 + 2] - p[a * 3 + 2];
            double vx = p[c * 3] - p[a * 3], vy = p[c * 3 + 1] - p[a * 3 + 1], vz = p[c * 3 + 2] - p[a * 3 + 2];
            // Unity（左手系）：顺时针为正面，正面法线 = cross(b − a, c − a) 的反向在左手系里看是 cross(u, v)（Unity 的 Vector3.Cross 同公式）
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l < 1e-12) continue;
            double vnx = n[a * 3] + n[b * 3] + n[c * 3], vny = n[a * 3 + 1] + n[b * 3 + 1] + n[c * 3 + 1], vnz = n[a * 3 + 2] + n[b * 3 + 2] + n[c * 3 + 2];
            total++; if (nx * vnx + ny * vny + nz * vnz > 0) agree++;
        }
        convChecked++;
        // Unity 的面法线 = Cross(b − a, c − a)（左手系规则下顺时针面朝外）：与顶点法线同向的比例应接近 100%；整体翻反时接近 0%
        if (total > 0 && agree < total * 0.5) { convBad++; Console.WriteLine("  换手系：" + what + " 同向 " + agree + "/" + total); }
    }

    static FigRig MakeRig(FigTemplate t) { return new FigRig(t); }
    static void CompareMats(string what, double off, M4d[] cs, int n)
    {
        checks++;
        var js = F64(off, n * 16);
        double md = 0; int worst = -1;
        for (int i = 0; i < n; i++) for (int k = 0; k < 16; k++) { double d = Math.Abs(js[i * 16 + k] - cs[i].e[k]); if (d > md) { md = d; worst = i; } }
        maxMat = Math.Max(maxMat, md);
        if (md > 1e-3) Fail(string.Format(CultureInfo.InvariantCulture, "{0}：骨骼矩阵最大差 {1:E2}（骨 {2} {3}）", what, md, worst, worst >= 0 && worst < FigBones.Count ? FigBones.BONES[worst] : "?"));
    }
    static FigPoseOpts PoseOpts(JObj o)
    {
        if (o == null) return null;
        var p = new FigPoseOpts();
        if (o.G("ik") is bool) p.ik = (bool)o.G("ik");
        if (o.G("clamp") is bool) p.clamp = (bool)o.G("clamp");
        if (o.S("lower") != null) p.lower = o.S("lower");
        if (o.Has("lowerT")) p.lowerT = o.D("lowerT");
        if (o.G("gallop") is bool) p.gallop = (bool)o.G("gallop");
        var b = o.O("blend");
        if (b != null) { p.blendClip = b.S("clip"); p.blendT = b.Has("t") ? b.D("t") : 0; p.blendW = b.Has("w") ? b.D("w") : 0.5; }
        return p;
    }
    static PortraitGen Gen(JObj g)
    {
        return new PortraitGen { name = g.S("name"), war = g.Has("war") ? g.D("war") : (double?)null, intel = g.Has("intel") ? g.D("intel") : (double?)null, pol = g.Has("pol") ? g.D("pol") : (double?)null, culture = g.S("culture"), color = g.S("color") };
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "out/figure";
        string filter = args.Length > 1 ? args[1] : null;
        bin = File.ReadAllBytes(Path.Combine(dir, "refs.bin"));
        var M = (JObj)JObj.ParseJson(File.ReadAllText(Path.Combine(dir, "refs.json")));
        double t0 = FigMath.Now();
        // ---- 图集布局
        if (filter == null || filter == "atlas")
        {
            checks++;
            foreach (var kv in M.O("atlas"))
            {
                var r = (List<object>)kv.Value; var c = FigAtlasLayout.REG(kv.Key);
                if (c.px != D(r[0]) || c.py != D(r[1]) || c.pw != D(r[2]) || c.ph != D(r[3])) Fail("图集区域 " + kv.Key + " 位置不同");
            }
        }
        var tpls = new Dictionary<string, FigTemplate>();
        // ---- 武将
        int nG = 0; double tg = FigMath.Now();
        if (filter == null || filter == "generals" || filter == "poses")
            foreach (JObj e in M.L("generals"))
            {
                string what = e.S("name") + "@" + e.S("lod");
                var o = e.O("opts");
                var opts = new FigOpts { lod = e.S("lod"), color = o.S("color"), shieldCult = o.G("shield") is bool && (bool)o.G("shield") };
                var look = FigBuild.lookOfGeneral(Gen(e.O("gen")), opts);
                CompareLook(what, e.O("look"), look);
                string key = FigBuild.generalKey(look, opts);
                if (key != e.S("key")) Fail(what + " 模板键不同：" + key + " ≠ " + e.S("key"));
                look.key = key;
                var t = FigBuild.buildTemplate(look);
                tpls[what] = t;
                nG++;
                checks++;
                if (Math.Abs(t.tris - e.D("tris")) > 0.5 || t.squeeze != (int)e.D("squeeze") || t.style != e.S("style") || t.faceSize != (int)e.D("faceSize"))
                    Fail(what + string.Format("：三角形 {0} / 压缩 {1} / 流派 {2} / 面部贴图 {3} ≠ JS {4} / {5} / {6} / {7}", t.tris, t.squeeze, t.style, t.faceSize, e.D("tris"), e.D("squeeze"), e.S("style"), e.D("faceSize")));
                var pdiff = new List<string>(); var pj = e.O("parts"); var pc = new JObj(); foreach (var kv in t.parts) pc[kv.Key] = (double)kv.Value;
                if (!Same(pj, pc, 0, what + ".parts", pdiff)) Fail(what + " 部位三角形：" + string.Join("；", pdiff.Take(4)));
                CompareWeapon(what, e.O("weapon"), t.weapon);
                CompareGeo(what + " 身体", e.O("body"), t.geometry);
                CompareGeo(what + " 头", e.O("head"), t.headGeo);
                CompareGeo(what + " 兵器", e.O("wgeo"), t.weaponGeo);
                CompareGeo(what + " 盾", e.O("sgeo"), t.shieldGeo);
                CheckConv(t.geometry, what); CheckConv(t.headGeo, what + " 头"); CheckConv(t.weaponGeo, what + " 兵器");
            }
        tg = FigMath.Now() - tg;
        // ---- 士兵
        if (filter == null || filter == "soldiers" || filter == "poses")
            foreach (JObj e in M.L("soldiers"))
            {
                string what = "S:" + e.S("type") + "/" + e.S("culture") + "@" + e.S("lod") + "#" + e.D("seed");
                var opts = new FigOpts { type = e.S("type"), culture = e.S("culture"), lod = e.S("lod"), seed = (int)e.D("seed"), color = "#2f7d4a" };
                var look = FigBuild.lookOfSoldier(opts);
                CompareLook(what, e.O("look"), look);
                string key = FigBuild.soldierKey(look);
                if (key != e.S("key")) Fail(what + " 模板键不同：" + key + " ≠ " + e.S("key"));
                look.key = key;
                var t = FigBuild.buildTemplate(look);
                tpls[what] = t;
                checks++;
                if (Math.Abs(t.tris - e.D("tris")) > 0.5 || t.squeeze != (int)e.D("squeeze") || t.style != e.S("style") || t.hasShield != (e.G("hasShield") is bool && (bool)e.G("hasShield")))
                    Fail(what + string.Format("：三角形 {0} / 压缩 {1} / 流派 {2} ≠ JS {3} / {4} / {5}", t.tris, t.squeeze, t.style, e.D("tris"), e.D("squeeze"), e.S("style")));
                CompareWeapon(what, e.O("weapon"), t.weapon);
                CompareGeo(what, e.O("body"), t.geometry);
                CheckConv(t.geometry, what);
            }
        // ---- 战马
        if (filter == null || filter == "horses")
            foreach (JObj e in M.L("horses"))
            {
                var o = e.O("opts");
                var ho = new FigHorseOpts { lod = o.S("lod") ?? "mid", seed = (int)o.D("seed"), armor = o.S("armor"), general = o.T("general"), name = o.S("name"), metal = o.S("metal"), plume = o.S("plume"), coat = o.S("coat"), chamfron = o.T("chamfron"), color = "#2c3d8f" };
                string what = "马 " + JObj.ToJson(o);
                var t = FigBuild.buildHorseTemplate(ho);
                checks++;
                if (t.key != e.S("key")) Fail(what + " 模板键 " + t.key + " ≠ " + e.S("key"));
                if (Math.Abs(t.tris - e.D("tris")) > 0.5) Fail(what + "：三角形 " + t.tris + " ≠ " + e.D("tris"));
                CompareGeo(what, e.O("body"), t.geometry);
                CheckConv(t.geometry);
            }
        // ---- 兵器与盾
        if (filter == null || filter == "weapons")
        {
            foreach (JObj e in M.L("weapons"))
            {
                string what = "兵器 " + e.S("id") + "@" + e.S("lod");
                var gb = new FigGB();
                var info = FigBuild.buildWeaponInto(gb, FigBuild.HasWeapon(e.S("id")) ? e.S("id") : "jian", FigLod.Of(e.S("lod")), FigBones.W1(0));
                var g = gb.build(false);
                checks++;
                if (gb.tris != (int)e.D("tris")) Fail(what + "：三角形 " + gb.tris + " ≠ " + e.D("tris"));
                CompareWeapon(what, e.O("info"), info);
                CompareGeo(what, e.O("body"), g);
                CheckConv(g, what);
            }
            foreach (JObj e in M.L("shields"))
            {
                string what = "盾 " + e.S("id") + "@" + e.S("lod");
                var gb = new FigGB();
                FigBuild.buildShieldInto(gb, e.S("id"), FigLod.Of(e.S("lod")), FigMath.TEAM, FigBones.W1(0));
                var g = gb.build(false);
                checks++;
                if (gb.tris != (int)e.D("tris")) Fail(what + "：三角形 " + gb.tris + " ≠ " + e.D("tris"));
                CompareGeo(what, e.O("body"), g);
                CheckConv(g);
            }
        }
        // ---- 姿势
        int nP = 0;
        if (filter == null || filter == "poses")
        {
            var rigs = new Dictionary<string, FigRig>();
            foreach (JObj e in M.L("poses"))
            {
                string fig = e.S("fig");
                FigRig rig;
                if (!rigs.TryGetValue(fig, out rig))
                {
                    FigTemplate t = null;
                    if (fig.StartsWith("S:")) tpls.TryGetValue(fig + "@low#0", out t);
                    else if (fig.StartsWith("opts:")) tpls.TryGetValue(fig.Substring(5) + "@high", out t);
                    else tpls.TryGetValue(fig, out t);
                    if (t == null) { Fail("姿势：找不到模板 " + fig); continue; }
                    rigs[fig] = rig = MakeRig(t);
                }
                string clip = e.S("clip"); double tt = e.D("t");
                if (clip == "grip-dir") { rig.SetPose("idle", 0.3, null); rig.Grip(new V3d(-0.3, 1.3, 0.35), new V3d(0.2, 0.9, 0.4), null, 15); }
                else if (clip == "grip-quat") rig.Grip(new V3d(-0.2, 1.1, 0.4), null, Q4d.Euler(0.4, -0.3, 0.2, "YXZ"), 0);
                else if (clip == "ikHand-L") rig.IkHand("L", new V3d(0.3, 1.25, 0.3), null);
                else if (clip == "update-run") { rig.SetPose("run", 0.1, null); for (int i = 0; i < 20; i++) { rig.SetPose("run", 0.1 + i / 60.0, null); rig.Update(1 / 60.0); } }
                else rig.SetPose(clip, tt, PoseOpts(e.O("opts")));
                CompareMats(fig + " " + clip + "@" + tt.ToString("F3", CultureInfo.InvariantCulture) + (e.O("opts") != null ? " " + JObj.ToJson(e.O("opts")) : ""), e.D("mats"), rig.BoneMatrices(), FigBones.Count);
                if (Math.Abs(rig.lift - e.D("lift")) > 1e-6 && !clip.StartsWith("grip") && !clip.StartsWith("ik")) Fail(fig + " " + clip + "：贴地抬升 " + rig.lift + " ≠ " + e.D("lift"));
                nP++;
            }
        }
        // ---- 骑乘
        if (filter == null || filter == "mounted")
        {
            var cacheT = new Dictionary<string, FigTemplate>();
            foreach (JObj e in M.L("mounted"))
            {
                string n = e.S("fig"); var ho = e.O("horse");
                FigTemplate t;
                if (!cacheT.TryGetValue(n, out t))
                {
                    var opts = new FigOpts { lod = "mid", mounted = true };
                    var look = FigBuild.lookOfGeneral(Gen(e.O("gen")), opts);
                    t = FigBuild.buildTemplate(look); cacheT[n] = t;
                }
                var hopts = new FigHorseOpts { lod = "mid", seed = (int)ho.D("seed"), armor = ho.S("armor"), general = ho.T("general"), name = ho.S("name"), color = "#2c3d8f" };
                var ht = FigBuild.buildHorseTemplate(hopts);
                var hr = new FigHorseRig(ht);
                var rider = new FigRig(t);
                rider.mounted = hr; hr.riders.Add(rider);
                rider.SetPose("idle", 0, null);
                rider.SetPose(e.S("riderClip"), e.D("riderT"), null);
                string hc = e.S("clip"); double tt = e.D("t");
                hr.SetPose(hc, tt);
                rider.SetPose(rider.clipName, rider.clipT, new FigPoseOpts { gallop = hc == "gallop" });
                var hm = hr.BoneMatrices();
                CompareMats("骑乘 " + n + " 马 " + hc + "@" + tt.ToString("F2", CultureInfo.InvariantCulture), e.D("horseMats"), hm, FigBones.HBONES.Length);
                var sad = M4d.Mul(hm[FigHorseRig.Saddle], M4d.Translation(FigHorseRig.RiderOffset(rider).x, FigHorseRig.RiderOffset(rider).y, FigHorseRig.RiderOffset(rider).z));
                var rm = rider.BoneMatrices().Select(m => M4d.Mul(sad, m)).ToArray();
                CompareMats("骑乘 " + n + " 骑手 " + hc + "@" + tt.ToString("F2", CultureInfo.InvariantCulture), e.D("riderMats"), rm, FigBones.Count);
            }
        }
        // ---- 士兵群烘焙：组键 F|<士兵键>、R|<士兵键>|<战马键>、H|<战马键>|<士兵键>；士兵键 = S|兵种|文化|lod|seed，战马键 = lod|毛色|马铠|seed
        long bakeHalves = 0, bakeExact = 0; double bakeMax = 0;
        if (filter == null || filter == "bakes")
        {
            var mountedCache = new Dictionary<string, FigBakeData[]>();
            foreach (JObj e in M.L("bakes"))
            {
                string gk = e.S("group"); var p = gk.Split('|');
                checks++;
                string[] sk = p[0] == "H" ? p.Skip(5).Take(5).ToArray() : p.Skip(1).Take(5).ToArray();
                string[] hk = p[0] == "R" ? p.Skip(6).Take(4).ToArray() : p[0] == "H" ? p.Skip(1).Take(4).ToArray() : null;
                var look = FigBuild.lookOfSoldier(new FigOpts { type = sk[1], culture = sk[2], lod = sk[3], seed = int.Parse(sk[4]), color = "#8a2a24" });
                look.key = FigBuild.soldierKey(look);
                FigBakeData bd;
                double tb = FigMath.Now();
                if (p[0] == "F")
                {
                    var tpl = FigBuild.buildTemplate(look);
                    bd = new FigBakeData();
                    foreach (var _ in FigBake.Soldier(tpl, bd)) { }
                }
                else
                {
                    string mk = string.Join("|", sk) + "#" + string.Join("|", hk);
                    FigBakeData[] pair;
                    if (!mountedCache.TryGetValue(mk, out pair))
                    {
                        var tpl = FigBuild.buildTemplate(look);
                        var ho = new FigHorseOpts { lod = hk[0], coat = hk[1] == "" ? null : hk[1], armor = hk[2], seed = int.Parse(hk[3]), shadow = false };
                        var ht = FigBuild.buildHorseTemplate(ho);
                        pair = new[] { new FigBakeData(), new FigBakeData() };
                        foreach (var _ in FigBake.Mounted(tpl, ht, pair[0], pair[1])) { }
                        mountedCache[mk] = pair;
                    }
                    bd = p[0] == "R" ? pair[0] : pair[1];
                }
                tb = FigMath.Now() - tb;
                if (bd.W != (int)e.D("W") || bd.rows != (int)e.D("rows")) { Fail("烘焙 " + gk + "：尺寸 " + bd.W + "×" + bd.rows + " ≠ " + e.D("W") + "×" + e.D("rows")); continue; }
                foreach (var kv in e.O("table"))
                {
                    var je = (JObj)kv.Value; FigBakeEntry ce;
                    if (!bd.table.TryGetValue(kv.Key, out ce) || ce.row != (int)je.D("row") || ce.n != (int)je.D("n") || ce.loop != je.T("loop") || Math.Abs(ce.dur - je.D("dur")) > 1e-12)
                        Fail("烘焙 " + gk + "：片段表 " + kv.Key + " 不同");
                }
                // JS 为 three 坐标的 M·inv（前 3 行）；C# 为 S·(M·inv)·S → 第 0 行的 1–3 列、第 1、2 行的第 0 列取反（半精度只翻符号位）
                var hj = U16(e.D("half"), bd.data.Length);
                var hc = FigBake.Halves(bd);
                int W = bd.W, bad = 0;
                for (int i = 0; i < hc.Length; i++)
                {
                    int comp = i & 3, r = ((i >> 2) % W) % 3;
                    bool flip = r == 0 ? comp != 0 : comp == 0;
                    ushort j = (ushort)(flip ? hj[i] ^ 0x8000 : hj[i]);
                    bakeHalves++;
                    if (j == hc[i] || (((j | hc[i]) & 0x7fff) == 0)) { bakeExact++; continue; }
                    double d = Math.Abs(FigBake.FromHalf(j) - FigBake.FromHalf(hc[i]));
                    bakeMax = Math.Max(bakeMax, d);
                    if (d > 2e-3 * Math.Max(1, Math.Abs(FigBake.FromHalf(j)))) bad++;
                }
                if (bad > 0) Fail("烘焙 " + gk + "：" + bad + " 个半精度值超差");
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  烘焙 {0}：{1}×{2}，C# {3:F0} ms", gk, bd.W, bd.rows, tb));
            }
        }
        // ---- 三角形预算（只在过滤为 budget 时运行，约半分钟）：世界剧本全部武将 high ≤ 15000、mid ≤ 4000；全部文化 × 兵种 × seed 0–2 的士兵 ≤ 900
        if (filter == "budget")
        {
            var fac = new Dictionary<string, string>();
            foreach (var l in WorldScenarioData.Factions) { var p = l.Split('|'); fac[p[0]] = p[3]; }
            int hi = 0, mid = 0, lo = 0; string hn = "", mn = "", ln = ""; int n = 0;
            foreach (var l in WorldScenarioData.Generals)
            {
                var p = l.Split('|');
                var g = new PortraitGen { name = p[0], war = double.Parse(p[1], CultureInfo.InvariantCulture), intel = double.Parse(p[2], CultureInfo.InvariantCulture), pol = double.Parse(p[3], CultureInfo.InvariantCulture), culture = p.Length > 9 && p[9] != "" ? p[9] : "han", color = fac.ContainsKey(p[4]) ? fac[p[4]] : "#8a8a8a" };
                foreach (var lod in new[] { "high", "mid" })
                {
                    var o = new FigOpts { lod = lod };
                    var look = FigBuild.lookOfGeneral(g, o); look.key = FigBuild.generalKey(look, o);
                    int t = (int)Math.Round(FigBuild.buildTemplate(look).tris);
                    if (lod == "high" && t > hi) { hi = t; hn = p[0]; }
                    if (lod == "mid" && t > mid) { mid = t; mn = p[0]; }
                }
                n++;
            }
            foreach (var c in FigTables.CULTURES) foreach (var ty in new[] { "spear", "sword", "archer", "cavalry" }) for (int sd = 0; sd < 3; sd++)
            {
                var look = FigBuild.lookOfSoldier(new FigOpts { type = ty, culture = (string)c, seed = sd, lod = "low" }); look.key = FigBuild.soldierKey(look);
                int t = (int)Math.Round(FigBuild.buildTemplate(look).tris);
                if (t > lo) { lo = t; ln = c + "/" + ty + sd; }
            }
            checks += 3;
            Console.WriteLine(string.Format("三角形预算：{0} 名武将 high 最多 {1}（{2}）、mid 最多 {3}（{4}）；士兵 low 最多 {5}（{6}）", n, hi, hn, mid, mn, lo, ln));
            if (hi > 15000) Fail("high 超预算：" + hi + " " + hn);
            if (mid > 4000) Fail("mid 超预算：" + mid + " " + mn);
            if (lo > 900) Fail("士兵超预算：" + lo + " " + ln);
        }
        double ms = FigMath.Now() - t0;
        if (bakeHalves > 0) Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "士兵群烘焙：半精度值 {0} 个，逐位相同 {1:P3}，最大差 {2:E2}", bakeHalves, (double)bakeExact / bakeHalves, bakeMax));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "比对 {0} 项：武将模板 {1} 个（C# 构建 {2:F0} ms）、姿势 {3}；最大差：位置 {4:E2} 法线 {5:E2} 骨骼矩阵 {6:E2}；换手系检查 {7} 个网格（绕序不符 {8}）；共 {9:F0} ms",
            checks, nG, tg, nP, maxPos, maxNrm, maxMat, convChecked, convBad, ms));
        if (convBad > 0) Fail("换手系：" + convBad + " 个网格的三角形绕序与法线方向不一致");
        foreach (var l in failLines) Console.WriteLine("  ✗ " + l);
        Console.WriteLine(fails == 0 ? "== 人物模型：全部通过" : "== 人物模型：失败 " + fails + " 项");
        return Math.Min(fails, 250);
    }
    static void CompareWeapon(string what, JObj js, FigWeaponInfo w)
    {
        checks++;
        if (js == null && w == null) return;
        if (js == null || w == null) { Fail(what + " 兵器信息缺失"); return; }
        var c = new JObj { { "id", w.id }, { "name", w.name }, { "style", w.style }, { "len", w.len }, { "tip", Vec(w.tip) }, { "base", Vec(w.@base) }, { "off", Vec(w.off) } };
        if (js.Has("string")) c["string"] = Vec(w.str);
        if (js.Has("nockTop")) { c["nockTop"] = Vec(w.nockTop); c["nockBot"] = Vec(w.nockBot); }
        var diffs = new List<string>();
        if (!Same(js, c, 1e-9, what + ".weapon", diffs)) Fail(what + " 兵器信息：" + string.Join("；", diffs.Take(4)));
    }
}
