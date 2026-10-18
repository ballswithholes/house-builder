// 攻击画面（第三版，Battle/Clash.cs）无头比对：按 cases.json 的用例（与 web.mjs 同一组 tests/clash.html 参数）
// 用 C# 移植建场景（ClashScene.BuildSync），与网页版导出的 web-<名>.json 逐项比对：
//   · 布景：文化 / 群落 / 城墙 / 房屋 / 登陆与群落参数；双方颜色、识别色、阵型；随机种子；
//   · 地形：高度采样（182 点）；地面网格（顶点 / 三角形数、位置 / 顶点色 / aGnd 的和）；道具网格（近 / 远：顶点 / 三角形数、
//     位置与顶点色的和、aKind 直方图）；水面；草丛与野花（实例数、位置 / 缩放 / 颜色的和，网格本身）；
//   · 两军：人数、前沿、冲锋距离、每名士兵的角色 / 兵种 / 阵位 / 入场与冲锋延迟 / 倒地时刻与方式 / 中箭数；武将；
//     箭（射手方、目标、起落时刻、必杀）、兵力碎块、事件时刻；士兵群统计（人数、组数、绘制次数、三角形数）、单独人物数；
//   · 用例的各个时刻（cases.json 的 times，缺省接敌 1.66 s 与混战 2.2 s；画面尺寸 screen，缺省 960 × 540）：按与网页版相同的方式
//     推进（逐帧 Tick，停在该时刻），比较镜头（位置、前向、上方、视角、宽高比）、雾、每名士兵（士兵群的实例矩阵：位置、朝向、缩放、
//     片段、起始时刻）、旗手与旗面、武将（战马位置、朝向、片段）、帅旗与旗杆。
// 另有三节（用例之外的名字）：settings = 设定与第三版迁移（Core/Game.cs 的 MigrateV3 由 run.sh 摘出编译）对照网页版 loadMode + migrateV3；
//   sceneof = Clash.SceneOf 对照 sceneof.txt（网页版 fromBattle(...).scene，289 张真实战场）；flow = Clash.Play 的流程冒烟测试。
// 坐标一律换成网页版的 three 坐标（本画面根 Clash.Origin 为原点，z 取反）比较。
// 之后（NO_RENDER 为空时）按同一时刻用 ClashRender.cs 画 unity-<名>-<时刻>.png（士兵群改走回退路径：每名士兵一个 Figure）。
// 用法：mono clash.exe <输出目录（含 cases.json、web-*.json 与 sceneof.txt）> [名…]      退出码 = 不一致的项数
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sanguo;
using UnityEngine;

namespace Sanguo
{
    // 头像模块的 Unity 部分（Portrait.Describe）无头时不编译：同 Portrait.DescribeDefault 的取法
    static class ClashHeadless
    {
        public static PortraitGen Describe(General g)
        {
            var pg = new PortraitGen { name = g.name, war = g.war, intel = g.intel, pol = g.pol, culture = g.culture };
            if (g.born.HasValue && g.born.Value > 0) pg.born = g.born.Value;
            pg.sex = g.sex;
            if (pg.sex == null && g.female) pg.sex = "f";
            return pg;
        }
    }
}

static class ClashHarness
{
    static int fails, checks;
    static string curCase = "";
    static readonly List<string> failLines = new List<string>();
    static void Fail(string what) { fails++; string s = "  ✗ " + curCase + " " + what; failLines.Add(s); if (failLines.Count < 400) Console.WriteLine(s); }
    static void Eq(string what, object cs, object web)
    {
        checks++;
        string a = cs == null ? "null" : Convert.ToString(cs, System.Globalization.CultureInfo.InvariantCulture), b = web == null ? "null" : Convert.ToString(web, System.Globalization.CultureInfo.InvariantCulture);
        if (a != b) Fail(what + "：C# " + a + " ≠ 网页 " + b);
    }
    static double maxRel;
    static void Near(string what, double cs, double web, double tol)
    {
        checks++;
        if (double.IsNaN(cs) || double.IsNaN(web) || Math.Abs(cs - web) > tol) Fail(what + string.Format("：C# {0:R} ≠ 网页 {1:R}（差 {2:G3}，容差 {3:G3}）", cs, web, cs - web, tol));
    }
    static void NearV(string what, double[] cs, List<object> web, double tol)
    {
        if (web == null) { Fail(what + "：网页版缺"); return; }
        for (int k = 0; k < cs.Length; k++) Near(what + "[" + k + "]", cs[k], Num(web[k]), tol);
    }
    static double Num(object o) { return o is double ? (double)o : o is int ? (int)o : o is long ? (long)o : o is bool ? ((bool)o ? 1 : 0) : double.NaN; }
    static string Str(object o) { return o == null ? null : o is bool ? ((bool)o ? "true" : "false") : o is double ? ((double)o).ToString("R", System.Globalization.CultureInfo.InvariantCulture) : o.ToString(); }
    static string HexOf(Color c) { return ClashUtil.HexJs(c); }
    static string HexOrNull(Color? c) { return c.HasValue ? ClashUtil.HexJs(c.Value) : null; }
    // 颜色（网页版 getHexString 的 '#rrggbb'）：按 0–255 比较，容差 0.51（float 的 0.7 × 255 = 178.49998 与 double 的 178.5 取整不同，颜色本身一致）
    static void ColNear(string what, Color? c, string hex)
    {
        checks++;
        if (!c.HasValue || hex == null) { if (c.HasValue != (hex != null)) Fail(what + "：C# " + HexOrNull(c) + " ≠ 网页 " + (hex ?? "null")); return; }
        int n = Convert.ToInt32(hex.Substring(1), 16);
        double dr = c.Value.r * 255.0 - ((n >> 16) & 255), dg = c.Value.g * 255.0 - ((n >> 8) & 255), db = c.Value.b * 255.0 - (n & 255);
        if (Math.Max(Math.Abs(dr), Math.Max(Math.Abs(dg), Math.Abs(db))) > 0.51) Fail(what + "：C# " + HexOrNull(c) + " ≠ 网页 " + hex);
    }

    // ---------------------------------------------------------- 用例（= tests/clash.html 的 makeOpts）
    sealed class GenDef { public string name, culture; public int war, intel; }
    static GenDef G(string n, int war, int intel, string culture = null) { return new GenDef { name = n, war = war, intel = intel, culture = culture }; }
    static readonly Dictionary<string, GenDef> GEN = new[]
    {
        G("关羽", 98, 80), G("张飞", 99, 30), G("赵云", 96, 76), G("黄忠", 93, 60), G("曹仁", 86, 62), G("夏侯惇", 90, 58), G("曹操", 85, 96),
        G("吕布", 100, 26), G("孙策", 94, 70), G("周瑜", 76, 97), G("黄祖", 70, 52), G("纪灵", 82, 40), G("孟获", 87, 42, "nanman"),
        G("诸葛亮", 38, 100), G("陆逊", 70, 95), G("颜良", 93, 42), G("塞维鲁", 80, 82, "roman"), G("沃洛吉斯", 74, 70, "persia"),
    }.ToDictionary(g => g.name);
    // = tests/clash.html 的 CGEN（文化 → 武将, 势力色）与 CPAIR（文化对阵；日耳曼 vs 萨尔马提亚、罗马 vs 凯尔特 故意用相近颜色）
    static readonly Dictionary<string, KeyValuePair<GenDef, string>> CGEN = new Dictionary<string, KeyValuePair<GenDef, string>>
    {
        { "han", CG(G("卫温", 72, 60), "#c8382c") }, { "nanman", CG(GEN["孟获"], "#8a5a2a") }, { "wa", CG(G("难升米", 70, 62, "wa"), "#b8442e") },
        { "yi", CG(G("夷洲酋", 66, 30, "yi"), "#4f8a3a") }, { "korea", CG(G("乙巴素", 64, 88, "korea"), "#3a5ea8") },
        { "steppe", CG(G("轲比能", 86, 64, "steppe"), "#7a5a2a") }, { "seasia", CG(G("区连", 72, 58, "seasia"), "#c07a1c") },
        { "tarim", CG(G("白霸", 62, 66, "tarim"), "#3aa5a0") }, { "kushan", CG(G("波调", 74, 70, "kushan"), "#7a3a8a") },
        { "persia", CG(GEN["沃洛吉斯"], "#3c7a8a") }, { "arab", CG(G("阿卜加尔", 70, 72, "arab"), "#c99a2e") },
        { "roman", CG(GEN["塞维鲁"], "#9c1b2a") }, { "celt", CG(G("阿根托克", 78, 50, "celt"), "#b03a2a") },
        { "german", CG(G("巴洛马尔", 84, 56, "german"), "#5a4a8a") }, { "sarmatian", CG(G("赞提克", 82, 52, "sarmatian"), "#4a3a7a") },
    };
    static KeyValuePair<GenDef, string> CG(GenDef g, string col) { return new KeyValuePair<GenDef, string>(g, col); }
    static readonly Dictionary<string, string> FAC = new Dictionary<string, string>
    {
        { "liubei", "#2f9a55" }, { "cao", "#2c3d8f" }, { "sun", "#c8382c" }, { "dong", "#7a2a3a" }, { "yuanshu", "#c99a2e" }, { "gongsun", "#e0e0e8" },
        { "yuanshao", "#2f6db5" }, { "nanman", "#8a5a2a" }, { "liubiao", "#3aa5a0" },
    };
    // = tests/clash.html 的 MATCH（攻方, 势力, 守方, 势力）、RATIO（攻前, 攻后, 守前, 守后, 攻阵, 守阵）、SPECIAL、CPAIR
    static readonly Dictionary<string, string[]> MATCH = new Dictionary<string, string[]>
    {
        { "plain", new[] { "关羽", "liubei", "曹仁", "cao" } }, { "forest", new[] { "孙策", "sun", "黄祖", "liubiao" } }, { "hill", new[] { "张飞", "liubei", "吕布", "dong" } },
        { "mountain", new[] { "赵云", "gongsun", "颜良", "yuanshao" } }, { "river", new[] { "周瑜", "sun", "曹操", "cao" } }, { "gate", new[] { "夏侯惇", "cao", "纪灵", "yuanshu" } },
        { "castle", new[] { "曹操", "cao", "吕布", "dong" } }, { "wall", new[] { "孟获", "nanman", "诸葛亮", "liubei" } },
    };
    static readonly string[] FORMS = { "方圆", "长蛇", "鱼鳞", "鹤翼", "偃月", "锋矢", "雁行", "衡轭" };
    static readonly Dictionary<string, object[]> RATIO = new Dictionary<string, object[]>
    {
        { "even", new object[] { 6200, 5420, 5800, 4870, "鱼鳞", "鹤翼" } }, { "big", new object[] { 8400, 8190, 2600, 1180, "锋矢", "方圆" } },
        { "small", new object[] { 1800, 1160, 7600, 7390, "长蛇", "衡轭" } }, { "rout", new object[] { 5200, 5050, 1300, 0, "偃月", "雁行" } },
        { "fail", new object[] { 1500, 0, 6400, 6150, "锋矢", "方圆" } }, { "typ", new object[] { 3200, 2950, 2400, 1980, "鹤翼", "锋矢" } },
        { "tiny", new object[] { 700, 520, 450, 210, "方圆", "长蛇" } },
    };
    static readonly Dictionary<string, string[]> SPECIAL = new Dictionary<string, string[]>
    {
        { "smite", new[] { "关羽", "青龙偃月斩", "#38d39a", "smite", "liubei", "关云长在此！" } }, { "storm", new[] { "诸葛亮", "借东风", "#8fd3ff", "storm", "liubei", "东风已起，天命在我！" } },
        { "volley", new[] { "黄忠", "百步穿杨", "#ffd24d", "volley", "liubei", "老夫宝刀未老！" } }, { "blaze", new[] { "陆逊", "火烧连营", "#ff7a2a", "blaze", "sun", "连营七百里，一炬尽焚之！" } },
    };
    static readonly Dictionary<string, string[]> CPAIR = new Dictionary<string, string[]>
    {
        { "wa-korea", new[] { "wa", "korea" } }, { "steppe-han", new[] { "steppe", "han" } }, { "seasia-nanman", new[] { "seasia", "nanman" } },
        { "tarim-kushan", new[] { "tarim", "kushan" } }, { "persia-arab", new[] { "persia", "arab" } }, { "roman-celt", new[] { "roman", "celt" } },
        { "german-sarmatian", new[] { "german", "sarmatian" } }, { "yi-han", new[] { "yi", "han" } },
    };
    static readonly Dictionary<string, Terrain> TERR = new Dictionary<string, Terrain>
    {
        { "plain", Terrain.Plain }, { "forest", Terrain.Forest }, { "hill", Terrain.Hill }, { "mountain", Terrain.Mountain }, { "river", Terrain.River },
        { "wall", Terrain.Wall }, { "gate", Terrain.Gate }, { "castle", Terrain.Castle },
    };
    static General Gen(GenDef d) { return new General { name = d.name, war = d.war, intel = d.intel, pol = 60, troops = 0, formation = 0, culture = d.culture }; }

    static ClashScene MakeScene(JObj c)
    {
        var parts = c.S("case").Split('-');
        var m = MATCH[parts[0]]; var r = RATIO[parts.Length > 1 ? parts[1] : "even"];
        string an = m[0], ac = FAC[m[1]], dn = m[2], dc = FAC[m[3]];
        ClashSpecial sp = null;
        string spk = c.S("special");
        if (spk != null) { var q = SPECIAL[spk]; an = q[0]; ac = FAC[q[4]]; sp = new ClashSpecial { name = q[1], color = Art.Hex(q[2]), kind = q[3], cry = q[5] }; }
        GenDef ag = GEN[an], dg = GEN[dn];
        string acu = ag.culture, dcu = dg.culture;
        string cult = c.S("cult");
        // 各文化对阵：武将、势力色、文化取 CGEN（文化 = 键名，汉也写明 "han"，同网页版）
        if (cult != null) { var p = CPAIR[cult]; ag = CGEN[p[0]].Key; ac = CGEN[p[0]].Value; acu = p[0]; dg = CGEN[p[1]].Key; dc = CGEN[p[1]].Value; dcu = p[1]; }
        var a = new ClashSide { gen = Gen(ag), side = 0, color = Art.Hex(ac), troopsBefore = (int)r[0], troopsAfter = (int)r[1], formation = Array.IndexOf(FORMS, (string)r[4]), culture = acu };
        var d = new ClashSide { gen = Gen(dg), side = 1, color = Art.Hex(dc), troopsBefore = (int)r[2], troopsAfter = (int)r[3], formation = Array.IndexOf(FORMS, (string)r[5]), culture = dcu };
        ClashSceneInfo scn = null;
        if (c.S("scene") != null || c.S("biome") != null || c.T("coast"))
            scn = new ClashSceneInfo { culture = c.S("scene") ?? (d.culture ?? "han"), biome = c.S("biome"), coast = c.T("coast") };
        return new ClashScene(a, d, TERR[parts[0]], sp, 0, 1f, scn);
    }

    // three 坐标（本画面根为原点）
    static Vector3 T3(Vector3 unityLocal) { return new Vector3(unityLocal.x, unityLocal.y, -unityLocal.z); }
    static Vector3 W3(Vector3 unityWorld) { var d = unityWorld - Clash.Origin; return new Vector3(d.x, d.y, -d.z); }

    // ---------------------------------------------------------- 静态部分
    static double[] SumV(Vector3[] v, bool negZ, out double absSum)
    {
        double x = 0, y = 0, z = 0; absSum = 0;
        foreach (var p in v) { x += p.x; y += p.y; z += negZ ? -p.z : p.z; absSum += Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z); }
        return new[] { x, y, z };
    }
    static double[] SumC(Color[] c, out double absSum)
    {
        double r = 0, g = 0, b = 0; absSum = 0;
        foreach (var k in c) { r += k.r; g += k.g; b += k.b; absSum += Math.Abs(k.r) + Math.Abs(k.g) + Math.Abs(k.b); }
        return new[] { r, g, b };
    }
    static void CompareMesh(string what, Mesh m, JObj w, bool kinds)
    {
        if (m == null || w == null) { Eq(what + " 存在", m != null, w != null); return; }
        Eq(what + " 顶点数", m.vertexCount, (int)w.D("verts"));
        Eq(what + " 三角形数", m.triangles.Length / 3, (int)w.D("tris"));
        if (m.vertexCount != (int)w.D("verts")) return;
        double ab; var ps = SumV(m.vertices, true, out ab);
        NearV(what + " 位置和", ps, w.L("pos"), 4e-7 * ab + 2e-3);
        if (w.L("col") != null && m.colors.Length > 0) { var cs = SumC(m.colors, out ab); NearV(what + " 顶点色和", cs, w.L("col"), 4e-7 * ab + 2e-3); }
        if (kinds && w.L("kinds") != null)
        {
            var h = new int[5];
            foreach (var u in m.uv2) h[Math.Max(0, Math.Min(4, (int)Math.Floor(u.x + 0.5)))]++;
            var wk = w.L("kinds");
            for (int k = 0; k < 5; k++) Eq(what + " aKind=" + k, h[k], (int)Num(wk[k]));
        }
    }
    static void CompareStatic(ClashScene sc, JObj S)
    {
        Eq("kind", sc.kind, S.S("kind"));
        Eq("seed", sc.seed, (int)S.D("seed"));
        Near("xc", sc.xc, S.D("xc"), 1e-5);
        Eq("recolored", sc.recolored, S.G("recolored"));
        var L = S.O("look"); var lk = sc.look;
        Eq("look.culture", lk.culture, L.S("culture")); Eq("look.biome", lk.biome, L.S("biome")); Eq("look.wall", lk.wall, L.S("wall")); Eq("look.house", lk.house, L.S("house"));
        Eq("look.coast", lk.coast, L.G("coast"));
        foreach (var k in new[] { "treeK", "farm", "snow", "sandy", "flowers" })
        {
            float v = k == "treeK" ? lk.treeK : k == "farm" ? lk.farm : k == "snow" ? lk.snow : k == "sandy" ? lk.sandy : lk.flowers;
            Near("look." + k, v, L.D(k), 1e-6);
        }
        ColNear("look.grass", lk.grass, L.S("grass")); ColNear("look.tuft", lk.tuft, L.S("tuft"));
        ColNear("look.leaf", lk.leaf, L.S("leaf")); ColNear("look.fleaf", lk.fleaf, L.S("fleaf"));
        Eq("look.trees", string.Join(",", lk.treeKinds.Select((t, i) => t + ":" + Str((double)lk.treeW[i]))), L.S("trees"));
        foreach (var pr in new[] { new { n = "A", i = sc.A }, new { n = "D", i = sc.D } })
        {
            var w = S.O(pr.n);
            Eq(pr.n + ".name", pr.i.name, w.S("name")); Eq(pr.n + ".color", pr.i.Hex, w.S("color")); Eq(pr.n + ".accent", HexOrNull(pr.i.accent), w.S("accent"));
            Eq(pr.n + ".culture", pr.i.culture, w.S("culture")); Eq(pr.n + ".before", pr.i.before, (int)w.D("before")); Eq(pr.n + ".after", pr.i.after, (int)w.D("after"));
            Eq(pr.n + ".formation", pr.i.formation, (int)w.D("formation"));
        }
        // 高度
        var hs = S.L("heights"); int hi = 0; double hmax = 0;
        for (int z = 8; z >= -70; z -= 6) for (int x = -24; x <= 24; x += 4) { double d = Math.Abs(sc.h.H(x, z) - Num(hs[hi++])); hmax = Math.Max(hmax, d); }
        Near("地形高度（" + hi + " 点）最大差", hmax, 0, 1e-6);
        // 网格
        var O = sc.terrainOut; var M = S.O("meshes");
        CompareMesh("地面", O.ground, M.O("ground"), false);
        if (O.ground != null && M.O("ground") != null && O.ground.uvs4.ContainsKey(1))
        {
            var g = O.ground.uvs4[1]; double[] s = new double[4]; double ab = 0;
            foreach (var v in g) { s[0] += v.x; s[1] += v.y; s[2] += v.z; s[3] += v.w; ab += Math.Abs(v.x) + Math.Abs(v.y) + Math.Abs(v.z) + Math.Abs(v.w); }
            NearV("地面 aGnd 和", s, M.O("ground").L("gnd"), 4e-7 * ab + 2e-3);
        }
        var props = M.L("props").Cast<JObj>().ToList();
        CompareMesh("近处道具", O.propsNear, props.FirstOrDefault(p => p.T("near")), true);
        CompareMesh("远处道具", O.propsFar, props.FirstOrDefault(p => !p.T("near")), true);
        var water = M.L("water").Cast<JObj>().ToList();
        var cw = new[] { O.river, O.sea }.Where(m => m != null).ToList();
        Eq("水面网格数", cw.Count, water.Count);
        for (int i = 0; i < Math.Min(cw.Count, water.Count); i++) CompareMesh("水面 " + i, cw[i], water[i], false);
        var grass = M.L("grass").Cast<JObj>().ToList();
        var cg = new List<KeyValuePair<Mesh, KeyValuePair<List<Matrix4x4>, List<Vector4>>>>();
        if (O.tuftMesh != null && O.tufts.Count > 0) cg.Add(new KeyValuePair<Mesh, KeyValuePair<List<Matrix4x4>, List<Vector4>>>(O.tuftMesh, new KeyValuePair<List<Matrix4x4>, List<Vector4>>(O.tufts, O.tuftCols)));
        if (O.flowerMesh != null && O.flowers.Count > 0) cg.Add(new KeyValuePair<Mesh, KeyValuePair<List<Matrix4x4>, List<Vector4>>>(O.flowerMesh, new KeyValuePair<List<Matrix4x4>, List<Vector4>>(O.flowers, O.flowerCols)));
        Eq("草丛 / 野花组数", cg.Count, grass.Count);
        for (int i = 0; i < Math.Min(cg.Count, grass.Count); i++)
        {
            string nm = i == 0 ? "草丛" : "野花"; var w = grass[i]; var ms = cg[i].Value.Key; var cs = cg[i].Value.Value;
            Eq(nm + " 实例数", ms.Count, (int)w.D("count"));
            double sx = 0, sy = 0, sz = 0, ss = 0, sh = 0, cr = 0, cgg = 0, cb = 0, ab = 0;
            for (int k = 0; k < ms.Count; k++)
            {
                var mm = ms[k]; sx += mm.m03; sy += mm.m13; sz -= mm.m23; ab += Math.Abs(mm.m03) + Math.Abs(mm.m13) + Math.Abs(mm.m23);
                ss += new Vector3(mm.m00, mm.m10, mm.m20).magnitude; sh += new Vector3(mm.m01, mm.m11, mm.m21).magnitude;
                cr += cs[k].x; cgg += cs[k].y; cb += cs[k].z;
            }
            NearV(nm + " 位置和", new[] { sx, sy, sz }, w.L("pos"), 4e-7 * ab + 2e-3);
            NearV(nm + " 缩放和", new[] { ss, sh }, w.L("scale"), 1e-6 * ms.Count + 1e-3);
            NearV(nm + " 颜色和", new[] { cr, cgg, cb }, w.L("col"), 1e-6 * ms.Count + 1e-3);
            Eq(nm + " 网格顶点数", cg[i].Key.vertexCount, (int)w.D("verts"));
            Eq(nm + " 网格三角形数", cg[i].Key.triangles.Length / 3, (int)w.D("tris"));
            double ab2; NearV(nm + " 网格位置和", SumV(cg[i].Key.vertices, true, out ab2), w.L("gpos"), 1e-6 * ab2 + 1e-4);
            NearV(nm + " 网格顶点色和", SumC(cg[i].Key.colors, out ab2), w.L("gcol"), 1e-6 * ab2 + 1e-4);
        }
        // 两军
        var WA = S.L("armies").Cast<JObj>().ToList();
        for (int ai = 0; ai < 2; ai++)
        {
            var A = sc.armies[ai]; var w = WA[ai]; string an = "军" + ai;
            Eq(an + ".n", A.n, (int)w.D("n")); Eq(an + ".dir", A.dir, (int)w.D("dir"));
            Near(an + ".xFront", A.xFront, w.D("xFront"), 1e-5); Near(an + ".minAll", A.minAll, w.D("minAll"), 1e-5);
            Eq(an + ".routed", A.routed, w.G("routed")); Eq(an + ".brace", A.brace, w.G("brace")); Eq(an + ".cheer", A.cheer, w.G("cheer"));
            Near(an + ".chargeDX", A.chargeDX, w.D("chargeDX"), 1e-5); Near(an + ".genDX", A.genDX, w.D("genDX"), 1e-5);
            var hx = w.O("hex");
            if (hx != null) { Eq(an + ".hex.base", A.hexBase, hx.S("base")); Eq(an + ".hex.flash", A.hexFlash, hx.S("flash")); Eq(an + ".hex.dead", A.hexDead, hx.S("dead")); }
            var ws = w.L("soldiers").Cast<JObj>().ToList();
            Eq(an + " 士兵数", A.soldiers.Count, ws.Count);
            for (int i = 0; i < Math.Min(A.soldiers.Count, ws.Count); i++)
            {
                var s = A.soldiers[i]; var q = ws[i]; string sn = an + ".兵" + i;
                Eq(sn + ".role", s.role, q.S("role")); Eq(sn + ".type", s.type, q.S("type")); Eq(sn + ".file", s.file, (int)q.D("file"));
                Near(sn + ".f", s.f, q.D("f"), 1e-5); Near(sn + ".l", s.l, q.D("l"), 1e-5); Near(sn + ".x0", s.x0, q.D("x0"), 1e-5); Near(sn + ".z0", s.z0, q.D("z0"), 1e-5);
                Near(sn + ".ed", s.ed, q.D("ed"), 1e-6); Near(sn + ".cd", s.cd, q.D("cd"), 1e-6); Near(sn + ".rd", s.rd, q.D("rd"), 1e-6); Near(sn + ".push", s.push, q.D("push"), 1e-6);
                Near(sn + ".shotDelay", s.shotDelay, q.D("shotDelay"), 1e-6);
                bool wf = q.G("fallT") != null;
                Eq(sn + " 倒地", !float.IsInfinity(s.fallT), wf);
                if (wf && !float.IsInfinity(s.fallT)) Near(sn + ".fallT", s.fallT, q.D("fallT"), 2e-5);
                Eq(sn + ".fallKind", s.fallKind, q.S("fallKind")); Near(sn + ".fallDir", s.fallDir, q.D("fallDir"), 1e-6); Near(sn + ".kb", s.kb, q.D("kb"), 1e-5);
                Eq(sn + " 中箭数", s.hits.Count, (int)q.D("hits"));
            }
            var g = w.O("general");
            Near(an + ".武将.x0", A.general.x0, g.D("x0"), 1e-5); Near(an + ".武将.z0", A.general.z0, g.D("z0"), 1e-5);
            Near(an + ".武将.p0", A.general.p0, g.D("p0"), 1e-5);
        }
        var WR = S.L("arrows").Cast<JObj>().ToList();
        Eq("箭数", sc.arrows.Count, WR.Count);
        for (int i = 0; i < Math.Min(sc.arrows.Count, WR.Count); i++)
        {
            var r = sc.arrows[i]; var q = WR[i];
            Eq("箭" + i + ".S", r.S.si, (int)q.D("S")); Eq("箭" + i + ".E", r.E.si, (int)q.D("E")); Eq("箭" + i + ".spec", r.spec, q.G("spec"));
            Near("箭" + i + ".t0", r.t0, q.D("t0"), 2e-5); Near("箭" + i + ".T", r.T, q.D("T"), 2e-5); Near("箭" + i + ".land", r.land, q.D("land"), 2e-5);
            Eq("箭" + i + ".target", r.target != null ? r.E.si + ":" + r.E.soldiers.IndexOf(r.target) : null, q.S("target"));
        }
        var WC = S.L("chunks").Cast<JObj>().ToList();
        Eq("兵力碎块数", sc.chunks.Count, WC.Count);
        for (int i = 0; i < Math.Min(sc.chunks.Count, WC.Count); i++)
        {
            var k = sc.chunks[i]; var q = WC[i];
            Eq("碎块" + i, k.si + "/" + k.amount + "/" + k.kind, (int)q.D("si") + "/" + (int)q.D("amount") + "/" + q.S("kind"));
            Near("碎块" + i + ".t", k.t, q.D("t"), 2e-5);
        }
        var WE = S.L("events");
        Eq("事件数", sc.events.Count, WE.Count);
        double emax = 0; for (int i = 0; i < Math.Min(sc.events.Count, WE.Count); i++) emax = Math.Max(emax, Math.Abs(sc.events[i].Key - Num(WE[i])));
        Near("事件时刻最大差", emax, 0, 5e-5);
        var cst = sc.crowd.Stats(); var wcr = S.O("crowd");
        Eq("士兵群.人数", cst.soldiers, (int)wcr.D("soldiers")); Eq("士兵群.组数", cst.groups, (int)wcr.D("groups"));
        Eq("士兵群.绘制次数", cst.drawCalls, (int)wcr.D("drawCalls")); Eq("士兵群.三角形数", cst.triangles, (int)wcr.D("triangles"));
        Eq("单独人物数（旗手、武将、战马）", sc.figs.Count + sc.horses.Count, (int)S.D("figs"));
    }

    // ---------------------------------------------------------- 设定（开 / 快 / 关）与第三版迁移
    // 网页版参考：clash.js 载入时的 loadMode() 与 game.js 的 migrateV3()（逐行转写；localStorage = 字典）。返回这一次启动的设定。
    const string V3Key = "sanguozhi2_clash_v3", V2Key = "sanguozhi2_clash", Mark = "sanguozhi2_v3_clash_reset";
    static string Get(Dictionary<string, string> ls, string k) { string v; return ls.TryGetValue(k, out v) ? v : null; }
    static string WebBoot(Dictionary<string, string> ls)
    {
        string v = Get(ls, V3Key), m;
        if (v == "on" || v == "fast" || v == "off") m = v;
        else if (Get(ls, V2Key) == "fast") m = "fast";
        else m = "on";
        bool enabled = m != "off"; float speed = m == "fast" ? 2 : 1;
        if (Get(ls, Mark) == null)
        {
            string old = Get(ls, V2Key);
            if (old != "fast") { ls.Remove(V2Key); enabled = true; speed = 1; }
            ls[Mark] = "1";
        }
        return !enabled ? "off" : speed > 1.01f ? "fast" : "on";
    }
    // C#：一次启动 = Clash 的静态设定回到未载入，再跑 Game.MigrateV3（run.sh 从 Core/Game.cs 原样摘出），之后读 Clash.Mode
    static readonly FieldInfo LoadedF = typeof(Clash).GetField("loaded", BindingFlags.NonPublic | BindingFlags.Static);
    static string CsBoot()
    {
        LoadedF.SetValue(null, false);
        GameV3Migrate.Run();
        return Clash.ModeName(Clash.Mode);
    }
    static string Dump(Dictionary<string, string> ls) { return (Get(ls, V3Key) ?? "-") + "/" + (Get(ls, V2Key) ?? "-") + "/" + (Get(ls, Mark) != null ? "1" : "-"); }
    static string DumpPrefs() { return (PlayerPrefs.HasKey(V3Key) ? PlayerPrefs.GetString(V3Key) : "-") + "/" + (PlayerPrefs.HasKey(V2Key) ? PlayerPrefs.GetString(V2Key) : "-") + "/" + (PlayerPrefs.HasKey(Mark) ? "1" : "-"); }
    static void CompareSettings()
    {
        curCase = "settings";
        int f0 = fails, c0 = checks;
        Eq("Clash.ModeKeyV3", Clash.ModeKeyV3, V3Key); Eq("Clash.ModeKey（= game.js CLASH_KEY）", Clash.ModeKey, V2Key); Eq("Clash.OldModeKey", Clash.OldModeKey, V2Key);
        Eq("Game.ClashResetMark", GameV3Migrate.Mark, Mark);
        // 初始存储：v3 键 / 旧键 / 迁移标记（null = 没有）
        var starts = new[]
        {
            new string[] { null, null, null }, new string[] { null, "off", null }, new string[] { null, "fast", null }, new string[] { null, "on", null },
            new string[] { null, "off", "1" }, new string[] { "off", null, null }, new string[] { "fast", "off", null }, new string[] { "off", "fast", null },
            new string[] { "fast", null, "1" }, new string[] { "on", "fast", "1" },
        };
        foreach (var st in starts)
        {
            var ls = new Dictionary<string, string>();
            PlayerPrefs.DeleteAll();
            if (st[0] != null) { ls[V3Key] = st[0]; PlayerPrefs.SetString(V3Key, st[0]); }
            if (st[1] != null) { ls[V2Key] = st[1]; PlayerPrefs.SetString(V2Key, st[1]); }
            if (st[2] != null) { ls[Mark] = "1"; PlayerPrefs.SetInt(Mark, 1); }
            string tag = "起始 " + Dump(ls);
            // 第一、二次启动；第二次启动后玩家循环切换（开 → 快 → 关 → 开）并在每一步后重新启动
            for (int boot = 1; boot <= 2; boot++)
            {
                string w = WebBoot(ls), c = CsBoot();
                Eq(tag + " 第 " + boot + " 次启动的设定", c, w);
                Eq(tag + " 第 " + boot + " 次启动后的存储", DumpPrefs(), Dump(ls));
            }
            for (int k = 0; k < 3; k++)
            {
                string cur = Clash.ModeName(Clash.Mode);
                string next = cur == "on" ? "fast" : cur == "fast" ? "off" : "on";
                ls[V3Key] = next;                                   // 网页版 mode 的写入：存入 MODE_KEY
                Eq(tag + " 切换 " + cur + "→", Clash.ModeName(Clash.CycleMode()), next);
                Eq(tag + " 切换后重新启动", CsBoot(), WebBoot(ls));
                Eq(tag + " 切换后的存储", DumpPrefs(), Dump(ls));
            }
        }
        PlayerPrefs.DeleteAll(); LoadedF.SetValue(null, false);
        Console.WriteLine(string.Format("settings：{0} 项，{1} 项不一致（Game.MigrateV3 {2}）", checks - c0, fails - f0, GameV3Migrate.Found ? "取自 Core/Game.cs" : "不存在，只有 Clash 自己的旧键处理"));
    }

    // ---------------------------------------------------------- 布景取自战场（Clash.SceneOf）
    // 参考 sceneof.txt（sceneof.js：网页版 fromBattle(...).scene）：同样的剧本、同样的战场（BattleTerrain.Generate，地形模型 =
    // TerrainModel.BuildHeadless）、同样的攻守格，比较文化、群落与是否海岸
    static void CompareSceneOf(string dir)
    {
        curCase = "sceneof";
        string f = Path.Combine(dir, "sceneof.txt");
        if (!File.Exists(f)) { Fail("缺网页版参考 " + f); return; }
        var lines = File.ReadAllLines(f);
        int c0 = checks, f0 = fails, coast = 0, maps = 0;
        double t0 = FigMath.Now();
        foreach (var which in new[] { "china", "world" })
        {
            var hdr = lines.FirstOrDefault(l => l.StartsWith("scenario " + which + " "));
            if (hdr == null) { Fail("sceneof.txt 缺剧本 " + which); continue; }
            var sc = hdr.Split(' ');
            UnityEngine.Random.InitState(1);
            var g = GameState.Current = which == "world" ? GameState.NewGame(sc[2], "world") : GameState.NewGame("liubei", "classic");
            var tm = TerrainModel.BuildHeadless(g, which);
            BattleTerrain.ModelSource = () => tm;
            var cache = new Dictionary<string, BattleModel>();
            foreach (var l in lines)
            {
                if (!l.StartsWith("S " + which + " ")) continue;
                var p = l.Split(' ');
                int cid = int.Parse(p[2]), fid = int.Parse(p[3]); bool sea = p[4] == "1";
                int ax = int.Parse(p[5]), ay = int.Parse(p[6]), tx = int.Parse(p[7]), ty = int.Parse(p[8]);
                string key = cid + " " + fid;
                BattleModel m;
                if (!cache.TryGetValue(key, out m))
                {
                    var T = BattleTerrain.Generate(new BattleTerrain.Options { city = g.cities[cid], fromCity = g.cities[fid], sea = sea, view = tm });
                    // 只用到 W / H / map / terrain：不经构造函数（免得排兵布阵）
                    m = (BattleModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BattleModel));
                    m.W = T.W; m.H = T.H; m.map = T.map; m.terrain = T;
                    cache[key] = m; maps++;
                }
                var a = new BUnit { x = ax, y = ay, side = 0, gen = new General { name = "A", culture = "han" } };
                var t = new BUnit { x = tx, y = ty, side = 1, gen = new General { name = "D", culture = "nanman" } };
                var s = Clash.SceneOf(m, a, t);
                if (s.coast) coast++;
                Eq(string.Join(" ", p.Skip(1).Take(8)), s.culture + " " + (s.biome ?? "undefined") + " " + (s.coast ? 1 : 0),
                   p[9] + " " + p[10] + " " + p[11]);
            }
        }
        BattleTerrain.ModelSource = null; GameState.Current = null;
        Console.WriteLine(string.Format("sceneof：{0} 张战场 {1} 项（海岸 {2}），{3} 项不一致（{4:F0} ms）", maps, checks - c0, coast, fails - f0, FigMath.Now() - t0));
    }

    // ---------------------------------------------------------- 播放流程（Clash.Play）
    // 两场排队（prep > build > wait > run > (out) > done，第二场在第一场之后）、播放时接管主相机 / 太阳 / 雾、结束后全部恢复并销毁场景根；
    // 播放中按空格跳过（done(true)，停留 0.4 s + 淡出 0.2 s）；必杀场（特写替身不暂停）；「关」时立即回调并存进 ModeKeyV3
    static void FlowCheck(string what, bool ok, string info = "") { checks++; if (!ok) Fail(what + (info != "" ? "  " + info : "")); }
    static ClashSide FlowSide(string name, int side, string col, int b, int a, int form)
    { return new ClashSide { gen = new General { name = name, war = 80, intel = 60, pol = 60 }, side = side, color = Art.Hex(col), troopsBefore = b, troopsAfter = a, formation = form }; }
    static void CompareFlow()
    {
        curCase = "flow";
        int f0 = fails, c0 = checks;
        PlayerPrefs.DeleteAll(); LoadedF.SetValue(null, false);
        SystemInfo.supportsInstancing = true; Clash.FreezeAt = -1;
        Screen.width = 960; Screen.height = 540;
        // 主场景：一台相机、一盏太阳、雾与环境光
        var mainCamGo = new GameObject("Main Camera"); var mainCam = mainCamGo.AddComponent<Camera>(); mainCam.enabled = true;
        var sunGo = new GameObject("Sun"); var mainSun = sunGo.AddComponent<Light>(); mainSun.cullingMask = -1;
        RenderSettings.fog = false; RenderSettings.fogStartDistance = 10; RenderSettings.fogEndDistance = 500;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientSkyColor = Color.red;
        QualitySettings.shadowDistance = 150;

        // 1. 正常播放（两场排队）
        var log = new List<string>();
        var it1 = Clash.Play(FlowSide("关羽", 0, "#2f9a55", 6200, 5420, 2), FlowSide("曹仁", 1, "#2c3d8f", 5800, 4870, 3), Terrain.Plain, null, 0, 1f, sk => log.Add("done1:" + sk));
        var it2 = Clash.Play(FlowSide("张飞", 0, "#2f9a55", 3000, 2900, 5), FlowSide("吕布", 1, "#7a2a3a", 2000, 0, 0), Terrain.Gate, null, 0, 1f, sk => log.Add("done2:" + sk));
        bool a1 = true, a2 = true;
        float dt = 1f / 30; int frames = 0;
        var states = new List<string>(); ClashScene first = null, second = null;
        float runStart = -1, run1End = -1;
        bool camOffDuring = false, sunMaskedDuring = false, fogOnDuring = false;
        while ((a1 || a2) && frames < 3000)
        {
            frames++;
            Time.realtimeSinceStartup += dt; Time.unscaledDeltaTime = dt;
            if (a1) a1 = it1.MoveNext();
            if (a2) a2 = it2.MoveNext();
            var sc = Clash.active;
            if (sc != null)
            {
                if (first == null) first = sc; else if (sc != first && second == null) { second = sc; run1End = Time.realtimeSinceStartup; }
                if (sc == first && sc.state == "run") { camOffDuring |= !mainCam.enabled; sunMaskedDuring |= (mainSun.cullingMask & (1 << Clash.Layer)) == 0; fogOnDuring |= RenderSettings.fog; }
                sc.Tick(dt); sc.Draw();
            }
            // 第一场的状态按它自己记（它结束后 Clash.active 已换成第二场）
            if (first != null && (states.Count == 0 || states[states.Count - 1] != first.state)) { states.Add(first.state); if (first.state == "run") runStart = Time.realtimeSinceStartup; }
        }
        Console.WriteLine("  flow：frames " + frames + " states " + string.Join(">", states) + " log " + string.Join(",", log));
        FlowCheck("两场都结束", !a1 && !a2 && log.Count == 2 && log[0] == "done1:False" && log[1] == "done2:False", string.Join(",", log));
        // 无头的 HUD 替身 FadeOut 立即回调：out → done 在同一次 Tick 内完成，看不到 out；士兵模板已烘焙好（前面的用例跑过）时
        // prep 在第一帧内就过去，也看不到
        string seq = string.Join(">", states);
        FlowCheck("第一场状态序列 (prep>)build>wait>run>done", seq == "prep>build>wait>run>done" || seq == "build>wait>run>done", seq);
        FlowCheck("第二场在第一场之后开始", second != null && first.Finished);
        FlowCheck("播放时主相机关闭、太阳去掉本层、雾开启", camOffDuring && sunMaskedDuring && fogOnDuring);
        FlowCheck("结束后主相机恢复", mainCam.enabled);
        FlowCheck("结束后太阳 cullingMask 恢复", mainSun.cullingMask == -1, mainSun.cullingMask.ToString());
        FlowCheck("结束后雾 / 环境光 / 阴影距离恢复", !RenderSettings.fog && RenderSettings.fogStartDistance == 10 && RenderSettings.fogEndDistance == 500 && RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat && RenderSettings.ambientSkyColor.r == 1 && RenderSettings.ambientSkyColor.g == 0 && QualitySettings.shadowDistance == 150,
            RenderSettings.fog + " " + RenderSettings.fogStartDistance + " " + RenderSettings.fogEndDistance + " " + RenderSettings.ambientMode + " " + QualitySettings.shadowDistance);
        FlowCheck("Clash.active 清空", Clash.active == null);
        FlowCheck("第一场进入 run、其后第二场开始", runStart > 0 && run1End > runStart, "run→下一场开始 " + (run1End - runStart).ToString("F2") + " s");
        Console.WriteLine("  flow：第一场 run → 第二场开始 " + (run1End - runStart).ToString("F2") + " s");
        FlowCheck("场景根已销毁", first.root == null);

        // 2. 跳过：播放 1 秒后按空格
        Input.Down.Clear();
        bool done3 = false, sk3 = false;
        var it3 = Clash.Play(FlowSide("赵云", 0, "#e0e0e8", 1800, 1160, 1), FlowSide("颜良", 1, "#2f6db5", 7600, 7390, 7), Terrain.Castle, null, -1, 1f, sk => { done3 = true; sk3 = sk; });
        bool a3 = true; frames = 0; float t0 = -1, tEnd = -1; ClashScene s3 = null; string hud = "";
        while (a3 && frames < 3000)
        {
            frames++;
            Time.realtimeSinceStartup += dt; Time.unscaledDeltaTime = dt;
            a3 = it3.MoveNext();
            var sc = Clash.active;
            if (sc != null)
            {
                s3 = sc;
                if (sc.state == "run" && t0 < 0) t0 = Time.realtimeSinceStartup;
                if (t0 > 0 && Time.realtimeSinceStartup - t0 > 1.0f && Time.realtimeSinceStartup - t0 < 1.0f + dt * 1.5f) Input.Down.Add(KeyCode.Space); else Input.Down.Clear();
                sc.Tick(dt); sc.Draw();
            }
        }
        tEnd = Time.realtimeSinceStartup;
        FlowCheck("跳过：done(true)", done3 && sk3);
        FlowCheck("跳过后 0.4 s 停留 + 0.2 s 淡出", t0 > 0 && tEnd - t0 < 1.0f + 0.4f + 0.2f + 0.25f, "run→结束 " + (tEnd - t0).ToString("F2") + " s");

        // 3. 必杀（特写替身返回 null：不暂停）
        bool done4 = false;
        var it4 = Clash.Play(FlowSide("关羽", 0, "#2f9a55", 6200, 5420, 2), FlowSide("曹仁", 1, "#2c3d8f", 5800, 4870, 3), Terrain.Plain,
            new ClashSpecial { name = "青龙偃月斩", color = Art.Hex("#38d39a"), kind = "smite", cry = "关云长在此！" }, 0, 1f, sk => done4 = true);
        bool a4 = true; frames = 0; int fxMax = 0;
        while (a4 && frames < 3000)
        {
            frames++;
            Time.realtimeSinceStartup += dt; Time.unscaledDeltaTime = dt;
            a4 = it4.MoveNext();
            var sc = Clash.active;
            if (sc != null) { sc.Tick(dt); sc.Draw(); }
        }
        FlowCheck("必杀场结束", done4, frames + " 帧");

        // 4. 模式：Off 时立即回调
        Clash.Mode = ClashMode.Off;
        bool done5 = false;
        var it5 = Clash.Play(FlowSide("A", 0, "#ff0000", 100, 50, 0), FlowSide("B", 1, "#0000ff", 100, 50, 0), Terrain.Plain, null, 0, 1f, sk => done5 = true);
        it5.MoveNext();
        FlowCheck("Off：立即回调", done5);
        FlowCheck("ModeKeyV3 存 off", PlayerPrefs.GetString(Clash.ModeKeyV3, "") == "off", PlayerPrefs.GetString(Clash.ModeKeyV3, ""));
        Clash.Mode = ClashMode.On;
        PlayerPrefs.DeleteAll(); LoadedF.SetValue(null, false);
        Console.WriteLine(string.Format("flow：{0} 项，{1} 项不一致", checks - c0, fails - f0));
    }

    // ---------------------------------------------------------- 时刻
    static readonly FieldInfo ItemsF = typeof(FigureCrowd).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
    static object CrowdItem(FigureCrowd cr, int id) { var d = (IDictionary)ItemsF.GetValue(cr); return d.Contains(id) ? d[id] : null; }
    static T IF<T>(object it, string f) { return (T)it.GetType().GetField(f, BindingFlags.Public | BindingFlags.Instance).GetValue(it); }
    // 朝向（three 坐标的前向 (sin θ, 0, cos θ) 的 θ）
    static double YawOfUnity(Quaternion q) { var f = q * Vector3.forward; return Math.Atan2(f.x, -f.z); }
    static double AngDiff(double a, double b) { double d = (a - b) % (2 * Math.PI); if (d > Math.PI) d -= 2 * Math.PI; if (d < -Math.PI) d += 2 * Math.PI; return Math.Abs(d); }
    static double Angle(Vector3 a, Vector3 b) { double d = Vector3.Dot(a.normalized, b.normalized); return Math.Acos(Math.Max(-1, Math.Min(1, d))) * 180 / Math.PI; }
    static Vector3 WorldPos(Transform t) { var m = t.localToWorldMatrix; return new Vector3(m.m03, m.m13, m.m23); }

    // 时刻表（秒；与 web.mjs 的 TIME 相同）：行进、冲锋、接敌、必杀特效、混战、整队、收尾。用例的 times 缺省为接敌与混战
    static readonly Dictionary<string, float> TIME = new Dictionary<string, float>
    {
        { "march", 0.35f }, { "charge", 1.3f }, { "contact", 1.66f }, { "fx", 1.8f }, { "melee", 2.2f }, { "regroup", 2.9f }, { "end", 3.3f },
    };
    static List<KeyValuePair<string, float>> TimesOf(JObj c)
    {
        var names = c.L("times") != null ? c.L("times").Select(o => o as string).ToList() : new List<string> { "contact", "melee" };
        if (names.Any(n => n == null || !TIME.ContainsKey(n))) return null;
        return names.Select(n => new KeyValuePair<string, float>(n, TIME[n])).OrderBy(kv => kv.Value).ToList();
    }

    static void Advance(ClashScene sc, float T)
    {
        Clash.FreezeAt = T;
        int guard = 0;
        while (sc.t < T - 1e-6f && guard++ < 2000) sc.Tick(1f / 30);
        for (int i = 0; i < 30; i++) sc.Tick(1f / 30);       // 停住后再走 1 秒（镜头晃动衰减；同网页版停住后的若干帧）
    }
    static void CompareTime(ClashScene sc, string tn, JObj W)
    {
        string p = "[" + tn + "] ";
        Near(p + "t", sc.t, W.D("t"), 1e-5);
        var cam = sc.cam.transform; var wc = W.O("cam");
        var cp = W3(cam.position); var wp = wc.L("p");
        Near(p + "镜头.x", cp.x, Num(wp[0]), 2e-3); Near(p + "镜头.y", cp.y, Num(wp[1]), 2e-3); Near(p + "镜头.z", cp.z, Num(wp[2]), 2e-3);
        var wq = wc.L("q"); var q3 = new Quaternion((float)Num(wq[0]), (float)Num(wq[1]), (float)Num(wq[2]), (float)Num(wq[3]));
        var f3 = q3 * new Vector3(0, 0, -1); var u3 = q3 * Vector3.up;
        var fu = cam.rotation * Vector3.forward; var uu = cam.rotation * Vector3.up;
        Near(p + "镜头前向夹角（度）", Angle(new Vector3(fu.x, fu.y, -fu.z), f3), 0, 0.05);
        Near(p + "镜头上方夹角（度）", Angle(new Vector3(uu.x, uu.y, -uu.z), u3), 0, 0.05);
        Near(p + "镜头视角", sc.cam.fieldOfView, wc.D("fov"), 1e-4);
        Near(p + "镜头宽高比", sc.cam.aspect, wc.D("aspect"), 1e-4);
        var wfog = W.L("fog");
        if (wfog != null) { Near(p + "雾.近", RenderSettings.fogStartDistance, Num(wfog[0]), 2e-3); Near(p + "雾.远", RenderSettings.fogEndDistance, Num(wfog[1]), 2e-3); }
        Near(p + "士兵群时钟", sc.crowd.Time, W.D("crowdTime"), 1e-5);
        var WS = W.L("soldiers");
        int bad = 0; double dmax = 0, ymax = 0;
        for (int ai = 0; ai < 2; ai++)
        {
            var A = sc.armies[ai]; var ws = (List<object>)WS[ai];
            for (int i = 0; i < A.soldiers.Count; i++)
            {
                var s = A.soldiers[i]; var q = ws[i] as JObj; if (q == null) continue;
                string sn = p + "军" + ai + ".兵" + i;
                if ((s.fig != null) != (q.G("flag") != null)) { Fail(sn + " 旗手 / 士兵群不一致"); continue; }
                if (s.fig != null)
                {
                    var r = s.fig.Root; var pp = T3(r.localPosition);
                    double d = Math.Sqrt(Math.Pow(pp.x - q.D("x"), 2) + Math.Pow(pp.y - q.D("y"), 2) + Math.Pow(pp.z - q.D("z"), 2));
                    dmax = Math.Max(dmax, d); if (d > 2e-3) { bad++; Fail(sn + "（旗手）位置差 " + d.ToString("G4")); }
                    double ya = AngDiff(YawOfUnity(r.localRotation), q.D("yaw")); ymax = Math.Max(ymax, ya);
                    if (ya > 2e-3) Fail(sn + "（旗手）朝向差 " + ya.ToString("G4"));
                    Eq(sn + "（旗手）片段", s.fig.Rig.clipName, q.S("clip"));
                    if (Math.Abs(s.fig.Rig.clipT - q.D("ct")) > 2e-4) Fail(sn + "（旗手）片段时刻 " + s.fig.Rig.clipT + " ≠ " + q.D("ct"));
                    var fw = W3(WorldPos(s.flag.go.transform)); var wf = q.L("flag");
                    double fd = Math.Sqrt(Math.Pow(fw.x - Num(wf[0]), 2) + Math.Pow(fw.y - Num(wf[1]), 2) + Math.Pow(fw.z - Num(wf[2]), 2));
                    checks++; if (fd > 5e-3) Fail(sn + " 旗面位置差 " + fd.ToString("G4") + "（C# " + fw + "）");
                    continue;
                }
                var it = CrowdItem(sc.crowd, s.cid);
                if (it == null) { Fail(sn + " 不在士兵群"); continue; }
                var m = IF<Matrix4x4>(it, "m");
                var pos = new Vector3(m.m03, m.m13, -m.m23);
                double dd = Math.Sqrt(Math.Pow(pos.x - q.D("x"), 2) + Math.Pow(pos.y - q.D("y"), 2) + Math.Pow(pos.z - q.D("z"), 2));
                checks++; dmax = Math.Max(dmax, dd);
                if (dd > 2e-3) { bad++; Fail(sn + " 位置差 " + dd.ToString("G4") + "（C# " + pos.x.ToString("F4") + "," + pos.y.ToString("F4") + "," + pos.z.ToString("F4") + " 网页 " + q.D("x").ToString("F4") + "," + q.D("y").ToString("F4") + "," + q.D("z").ToString("F4") + "）"); }
                double yd = AngDiff(YawOfUnity(m.rotation), q.D("yaw")); ymax = Math.Max(ymax, yd); checks++;
                if (yd > 2e-3) Fail(sn + " 朝向差 " + yd.ToString("G4"));
                Near(sn + " 缩放", m.lossyScale.x, q.D("s"), 1e-4);
                Eq(sn + " 片段", IF<string>(it, "clip"), q.S("clip"));
                Near(sn + " 片段起点", IF<double>(it, "t0"), q.D("t0"), 2e-4);
                Near(sn + " 片段速度", IF<double>(it, "speed"), q.D("speed"), 1e-5);
            }
        }
        var WG = W.L("generals");
        for (int ai = 0; ai < 2; ai++)
        {
            var G = sc.armies[ai].general; var q = (JObj)WG[ai]; string gn = p + "军" + ai + ".武将";
            var hp = T3(G.horse.Root.localPosition);
            Near(gn + ".x", hp.x, q.D("x"), 2e-3); Near(gn + ".y", hp.y, q.D("y"), 2e-3); Near(gn + ".z", hp.z, q.D("z"), 2e-3);
            Near(gn + " 朝向差", AngDiff(YawOfUnity(G.horse.Root.localRotation), q.D("yaw")), 0, 2e-3);
            Eq(gn + " 片段", G.fig.Rig.clipName, q.S("clip"));
            Near(gn + " 片段时刻", G.fig.Rig.clipT, q.D("ct"), 2e-4);
            var fw = W3(WorldPos(G.flag.go.transform)); var wf = q.L("flag");
            Near(gn + " 帅旗面位置差", Math.Sqrt(Math.Pow(fw.x - Num(wf[0]), 2) + Math.Pow(fw.y - Num(wf[1]), 2) + Math.Pow(fw.z - Num(wf[2]), 2)), 0, 5e-3);
            var pw = W3(WorldPos(G.pole.transform)); var wpp = q.L("pole");
            Near(gn + " 帅旗杆位置差", Math.Sqrt(Math.Pow(pw.x - Num(wpp[0]), 2) + Math.Pow(pw.y - Num(wpp[1]), 2) + Math.Pow(pw.z - Num(wpp[2]), 2)), 0, 5e-3);
        }
        Console.WriteLine(string.Format("  {0}{1}：士兵位置最大差 {2:G3}、朝向最大差 {3:G3} rad、镜头 ({4:F3}, {5:F3}, {6:F3})、雾 {7:F1}–{8:F1}", p, curCase, dmax, ymax, cp.x, cp.y, cp.z, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance));
    }

    static ClashScene Prepare(JObj c, bool instanced, JObj web)
    {
        SystemInfo.supportsInstancing = instanced;
        var sc = MakeScene(c);
        sc.BuildSync();
        sc.SetupCamera();
        sc.cam.aspect = Screen.width / (float)Screen.height;
        ClashUtil.SetLayer(sc.root, Clash.Layer);
        ClashScene.SetGlobals();
        sc.hud = new ClashHud(sc);
        sc.shown = true;
        sc.state = "run";
        sc.Resize();
        // 雾的初值（Show 设定；CameraPose 随机位平移）
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = ClashScene.FOG;
        RenderSettings.fogStartDistance = 34; RenderSettings.fogEndDistance = 135;
        return sc;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "out/clash";
        var only = args.Skip(1).ToList();
        bool render = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_RENDER"));
        if (only.Count == 0 || only.Contains("settings")) CompareSettings();
        if (only.Count == 0 || only.Contains("sceneof")) CompareSceneOf(dir);
        var cases = ((List<object>)JObj.ParseJson(File.ReadAllText(Path.Combine(dir, "cases.json")))).Cast<JObj>().Where(c => only.Count == 0 || only.Contains(c.S("name"))).ToList();
        foreach (var c in cases)
        {
            curCase = c.S("name");
            var scr = c.L("screen");
            Screen.width = scr != null && scr.Count == 2 ? (int)Num(scr[0]) : 960; Screen.height = scr != null && scr.Count == 2 ? (int)Num(scr[1]) : 540;
            var tlist = TimesOf(c);
            if (tlist == null) { Fail("cases.json 的 times 含未知时刻"); continue; }
            string wf = Path.Combine(dir, "web-" + curCase + ".json");
            if (!File.Exists(wf)) { Fail("缺网页版参考 " + wf); continue; }
            var web = (JObj)JObj.ParseJson(File.ReadAllText(wf));
            int f0 = fails, c0 = checks;
            double t0 = FigMath.Now();
            var sc = Prepare(c, true, web);
            double tb = FigMath.Now() - t0;
            CompareStatic(sc, web.O("static"));
            var times = web.O("times");
            foreach (var kv in tlist)
            {
                var W = times.O(kv.Key);
                if (W == null) { Fail("网页版参考缺时刻 " + kv.Key + "（cases.json 改过？重跑 web.mjs）"); continue; }
                ClashHud.BarNdc = W.G("hudNdc") != null ? (float)W.D("hudNdc") : 1;
                Advance(sc, kv.Value);
                CompareTime(sc, kv.Key, W);
            }
            sc.Dispose();
            Console.WriteLine(string.Format("{0}：{1} 项，{2} 项不一致（建场景 {3:F0} ms）", curCase, checks - c0, fails - f0, tb));
            if (!render) continue;
            // 画图：士兵群走回退路径（每名士兵一个 Figure，便于取姿势），其余同上
            foreach (var kv in tlist)
            {
                var W = times.O(kv.Key);
                if (W == null) continue;
                ClashHud.BarNdc = W.G("hudNdc") != null ? (float)W.D("hudNdc") : 1;
                var rs = Prepare(c, false, web);
                Advance(rs, kv.Value);
                double t1 = FigMath.Now();
                string png = Path.Combine(dir, "unity-" + curCase + "-" + kv.Key + ".png");
                ClashRender.Frame(rs, png, Screen.width, Screen.height);
                Console.WriteLine(string.Format("  {0} {1}：{2:F0} ms", Path.GetFileName(png), ClashRender.Info, FigMath.Now() - t1));
                rs.Dispose();
            }
            SystemInfo.supportsInstancing = true;
        }
        Screen.width = 960; Screen.height = 540;
        if (only.Count == 0 || only.Contains("flow")) CompareFlow();
        Console.WriteLine(string.Format("== 共 {0} 项，{1} 项不一致", checks, fails));
        return Math.Min(fails, 98);
    }
}
