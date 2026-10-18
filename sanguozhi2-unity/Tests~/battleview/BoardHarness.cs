// 战场画面（第三版）的无头对照：用 C# 移植（Battle/BattleLook.cs 的 BattleBoard：地形模型、平滑地形网格、水面、
// 各格站立高度、树石草与点缀的实例表、城池与桥、登陆的船、士兵队形）为 cases.json 的各个用例建棋盘，
// 与网页版（web.mjs 导出的 web-<名>.json）逐项比对：
//   地图（BattleTerrain 生成，应与网页版逐格相同）、种子与 notes、外观（文化 / 城墙样式 / 地表）、
//   view.stats 的各项（地形 / 水面 / 城池 / 实例三角形数，树、石、草、点缀、船的数量，裙边）、
//   地形网格高度的和、各格站立高度、每个实例批次（种类 × 近 / 远）的数量、模板三角形、位置 / 缩放 / 实例色的和、
//   各部队的贴地位置与阵营光环的高度、士兵的兵种与队形。
// 部队的武将与位置取自网页版的导出（网页版按 Conquest.prepare + BattleModel 布阵；这里只比对画面）。
// "terrain": false 的用例（网页版不带取样栅格）：地图与城池照网页版，比对由地块类型合成的取样场（SynthGrid）建出的棋盘。
// 输出：<输出目录>/board-<名>.txt（逐项结果），汇总打印；退出码 = 不一致的项数。
// 用法：mono board.exe <输出目录> [名…]      （由 Tests~/battleview/run.sh 调用）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Sanguo;
using UnityEngine;

static class BoardHarness
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int fails;
    static StringBuilder log;
    static void Check(string what, bool ok, string detail)
    {
        string line = (ok ? "  OK   " : "  FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "：" + detail);
        log.AppendLine(line);
        if (!ok) { fails++; Console.WriteLine(line); }
    }
    static double D(object o) { return o == null ? 0 : Convert.ToDouble(o, Inv); }
    static Dictionary<string, object> O(object o) { return o as Dictionary<string, object>; }
    static List<object> L(object o) { return o as List<object>; }
    static string F(double v) { return v.ToString("0.######", Inv); }

    // 网页版的部队 → C# 的 BattleModel（地形由 BattleTerrain.ApplyTo 生成；部队的位置与兵力照网页版）
    public static BattleModel ModelFor(GameState g, Dictionary<string, object> web, string cityName, string fromName)
    {
        var tgt = g.cities.First(c => c.name == cityName);
        var src = g.cities.First(c => c.name == fromName);
        var s = new BattleSetup { attacker = (int)D(web["attacker"]), defender = (int)D(web["defender"]), src = src, target = tgt, atkFood = 3000 };
        var wu = L(web["units"]);
        foreach (Dictionary<string, object> u in wu)
        {
            int id = (int)D(u["id"]);
            var gen = g.generals.First(x => x.id == id);
            gen.troops = (int)D(u["troops"]);
            if ((int)D(u["side"]) == 0) s.atk.Add(gen); else s.def.Add(gen);
        }
        var m = new BattleModel(s);
        // 网页版没有取样栅格（旧的随机地形）：地图与城池位置照网页版，C# 也不带取样栅格（BattleBoard 走 SynthGrid）
        if (web["terrain"] == null)
        {
            m.terrain = null;
            var rows = L(web["map"]).Cast<string>().ToList();
            const string ch = "pfhmrwgcW";
            for (int y = 0; y < m.H; y++) for (int x = 0; x < m.W; x++) m.map[x, y] = (Terrain)ch.IndexOf(rows[m.H - 1 - y][x]);
            var wc = O(web["castle"]);
            if (wc != null) m.castle = new Vector2Int((int)D(wc["x"]), (int)D(wc["y"]));
        }
        foreach (Dictionary<string, object> u in wu)
        {
            int id = (int)D(u["id"]);
            var bu = m.units.First(x => x.gen.id == id);
            bu.x = (int)D(u["x"]); bu.y = (int)D(u["y"]); bu.alive = (bool)u["alive"]; bu.commander = (bool)u["commander"];
        }
        return m;
    }
    public static Color FactionCol(GameState g, int idx) { return idx >= 0 && idx < g.factions.Count ? g.factions[idx].Col : new Color(0.5f, 0.5f, 0.5f); }
    // 网页版 BattleView.cultureOf(side)
    public static string CultureOf(BattleModel m, BattleLook.Look look, int side)
    {
        var city = side == 1 ? m.setup.target : m.setup.src;
        string c = city != null ? CultureArt.CultureOfCity(city) : null;
        return !string.IsNullOrEmpty(c) ? c : (look != null ? look.culture : "han");
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string outDir = args.Length > 0 ? args[0] : "out";
        var only = new HashSet<string>(args.Skip(1));
        string here = Path.GetDirectoryName(Path.GetFullPath(typeof(BoardHarness).Assembly.Location));
        string casesPath = Environment.GetEnvironmentVariable("BV_CASES") ?? Path.Combine(outDir, "cases.json");
        var cases = L(MiniJson.Parse(File.ReadAllText(casesPath)));
        UnityEngine.Random.InitState(7);
        var g = GameState.Current = GameState.NewGame("cao", "world");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tm = TerrainModel.BuildHeadless(g, "world");
        BattleTerrain.ModelSource = () => tm;
        Console.WriteLine("地形模型（无头）" + Math.Round(sw.Elapsed.TotalMilliseconds) + " ms");
        int total = 0;
        foreach (Dictionary<string, object> cs in cases)
        {
            string name = (string)cs["name"];
            if (only.Count > 0 && !only.Contains(name)) continue;
            string wp = Path.Combine(outDir, "web-" + name + ".json");
            if (!File.Exists(wp)) { Console.WriteLine("  缺网页版参考 " + wp); fails++; continue; }
            log = new StringBuilder();
            int f0 = fails;
            var web = O(MiniJson.Parse(File.ReadAllText(wp)));
            var m = ModelFor(g, web, (string)cs["city"], (string)cs["from"]);
            var look = BattleLook.Of(BattleLook.CultureOf(m.setup));
            sw.Restart();
            var B = new BattleBoard(m, look, FactionCol(g, m.setup.attacker), FactionCol(g, m.setup.defender));
            double ms = sw.Elapsed.TotalMilliseconds;
            log.AppendLine(name + "：" + cs["city"] + "<" + cs["from"] + "  C# 构建 " + Math.Round(ms) + " ms（网页版 " + Math.Round(D(web["viewMs"])) + " ms）");
            Compare(web, m, B, look);
            File.WriteAllText(Path.Combine(outDir, "board-" + name + ".txt"), log.ToString());
            Console.WriteLine("== " + name + "：" + (fails == f0 ? "全部一致" : (fails - f0) + " 项不一致") + "（C# " + Math.Round(ms) + " ms）");
            total++;
        }
        Console.WriteLine(total + " 个用例，" + (fails == 0 ? "全部通过" : fails + " 项不一致"));
        return fails;
    }

    static void Compare(Dictionary<string, object> web, BattleModel m, BattleBoard B, BattleLook.Look look)
    {
        // 地图
        var rows = L(web["map"]).Cast<string>().ToList();
        const string ch = "pfhmrwgcW";
        var my = new List<string>();
        for (int y = m.H - 1; y >= 0; y--) { var sb = new StringBuilder(); for (int x = 0; x < m.W; x++) sb.Append(ch[(int)m.map[x, y]]); my.Add(sb.ToString()); }
        Check("地图（BattleTerrain）", rows.SequenceEqual(my), rows.SequenceEqual(my) ? "" : "\n    网页 " + string.Join("/", rows) + "\n    C#   " + string.Join("/", my));
        var wt = O(web["terrain"]);
        if (wt != null && B.Tr != null)
        {
            Check("种子", (uint)D(wt["seed"]) == B.Tr.seed, D(wt["seed"]) + " / " + B.Tr.seed);
            var wn = string.Join(",", L(wt["notes"]).Cast<string>());
            Check("notes", wn == string.Join(",", B.Tr.notes), wn + " / " + string.Join(",", B.Tr.notes));
        }
        else Check("取样栅格", (wt == null) == (B.Tr == null), "网页 " + (wt != null) + " / C# " + (B.Tr != null));
        var wl = O(web["look"]);
        Check("外观", (string)wl["culture"] == look.culture && (string)wl["style"] == look.style && (string)wl["groundKind"] == look.groundKind,
            wl["culture"] + "/" + wl["style"] + "/" + wl["groundKind"] + " vs " + look.culture + "/" + look.style + "/" + look.groundKind);
        // 统计
        var st = O(web["stats"]);
        Action<string, int> stat = (k, v) => { int w = st.ContainsKey(k) ? (int)D(st[k]) : 0; Check("stats." + k, w == v, w + " / " + v); };
        stat("terrainTris", B.Stats.terrainTris); stat("waterTris", B.Stats.waterTris); stat("fortTris", B.Stats.fortTris); stat("propTris", B.Stats.propTris);
        stat("trees", B.Stats.trees); stat("rocks", B.Stats.rocks); stat("tufts", B.Stats.tufts); stat("dress", B.Stats.dress); stat("boats", B.Stats.boats);
        Check("stats.skirt", (string)st["skirt"] == B.Stats.skirt, st["skirt"] + " / " + B.Stats.skirt);
        // 地形网格
        double hs = 0; foreach (var v in B.hh) hs += v;
        Check("网格尺寸", (int)D(web["nx"]) == B.gnx && (int)D(web["ny"]) == B.gny, D(web["nx"]) + "×" + D(web["ny"]) + " / " + B.gnx + "×" + B.gny);
        Check("地形高度之和", Math.Abs(hs - D(web["hhSum"])) < 1e-2, F(D(web["hhSum"])) + " / " + F(hs));
        var wty = L(web["tileY"]); double dty = 0; int at = -1;
        for (int i = 0; i < wty.Count && i < B.tileY.Length; i++) { double d = Math.Abs(D(wty[i]) - B.tileY[i]); if (d > dty) { dty = d; at = i; } }
        Check("各格站立高度", dty < 1e-4, "最大差 " + F(dty) + (at >= 0 ? "（格 " + (at % m.W) + "," + (at / m.W) + "）" : ""));
        // 实例批次
        var wp = L(web["props"]);
        var names = B.Props.Select(p => "Props_" + p.kind + (p.near ? "" : "_far")).ToList();
        Check("实例批次", wp.Count == B.Props.Count && wp.Select(o => (string)O(o)["name"]).SequenceEqual(names),
            "网页 " + string.Join(" ", wp.Select(o => O(o)["name"] + ":" + O(o)["count"])) + "\n    C#   " + string.Join(" ", B.Props.Select(p => "Props_" + p.kind + (p.near ? "" : "_far") + ":" + p.items.Count)));
        foreach (Dictionary<string, object> w in wp)
        {
            string nm = (string)w["name"]; int k = names.IndexOf(nm);
            if (k < 0) { Check(nm, false, "C# 没有"); continue; }
            var p = B.Props[k];
            double sx = 0, sy = 0, sz = 0, ss = 0, sr = 0, sg = 0, sb = 0;
            for (int i = 0; i < p.items.Count; i++)
            {
                var M = p.MatrixOf(i);
                sx += M.m03; sy += M.m13; sz -= M.m23;   // three z = −Unity z
                ss += Math.Sqrt(M.m00 * M.m00 + M.m10 * M.m10 + M.m20 * M.m20) + Math.Sqrt(M.m01 * M.m01 + M.m11 * M.m11 + M.m21 * M.m21);
                sr += p.items[i].r; sg += p.items[i].g; sb += p.items[i].b;
            }
            int tplTris = BattleBoard.PropTemplate(p.kind).t.Count / 3;
            bool ok = (int)D(w["count"]) == p.items.Count && (int)D(w["tris"]) == tplTris && Math.Abs(sx - D(w["sx"])) < 0.05 && Math.Abs(sy - D(w["sy"])) < 0.05 && Math.Abs(sz - D(w["sz"])) < 0.05
                && Math.Abs(ss - D(w["ss"])) < 0.01 && Math.Abs(sr - D(w["sr"])) < 0.01 && Math.Abs(sg - D(w["sg"])) < 0.01 && Math.Abs(sb - D(w["sb"])) < 0.01;
            Check(nm, ok, "数量 " + D(w["count"]) + "/" + p.items.Count + " 模板三角形 " + D(w["tris"]) + "/" + tplTris
                + " Σx " + F(D(w["sx"])) + "/" + F(sx) + " Σy " + F(D(w["sy"])) + "/" + F(sy) + " Σz " + F(D(w["sz"])) + "/" + F(sz) + " Σs " + F(D(w["ss"])) + "/" + F(ss)
                + " Σc " + F(D(w["sr"]) + D(w["sg"]) + D(w["sb"])) + "/" + F(sr + sg + sb));
        }
        // 部队：贴地位置（格子中心的站立高度与地表取大）、阵营光环、士兵队形
        foreach (Dictionary<string, object> u in L(web["units"]))
        {
            int id = (int)D(u["id"]);
            var bu = m.units.First(x => x.gen.id == id);
            if (!bu.alive) continue;
            var t = B.Tile(bu.x, bu.y);
            double gy = B.GroundY(t.x, t.z), y = Math.Max(t.y, gy);
            var pos = L(u["pos"]);
            double dx = Math.Abs(D(pos[0]) - t.x), dyv = Math.Abs(D(pos[1]) - y), dz = Math.Abs(-D(pos[2]) - t.z);
            // 光环：格子周围半径 0.9 的最高地表
            double ring = t.y; for (int k = 0; k < 8; k++) { double a = k * Math.PI / 4; ring = Math.Max(ring, B.GroundY(t.x + Math.Cos(a) * 0.9, t.z + Math.Sin(a) * 0.9)); }
            double ringY = 0.04 + Math.Min(0.45, Math.Max(0, ring - t.y));
            string cul = CultureOf(m, look, bu.side);
            int nb = BattleBoard.SoldierBucket(bu.Troops, bu.alive), ns = BattleBoard.SoldierCount(nb, cul);
            var slots = BattleBoard.SoldierSlots(cul, ns);
            var ws = L(u["sold"]);
            bool soldOk = ws.Count == slots.Count;
            for (int i = 0; soldOk && i < slots.Count; i++)
            {
                var q = O(ws[i]);
                soldOk = (string)q["type"] == slots[i].type && (bool)q["mounted"] == slots[i].mounted && Math.Abs(D(q["x"]) - slots[i].x) < 1e-9 && Math.Abs(D(q["z"]) - slots[i].z) < 1e-9;
            }
            Check("部队 " + u["name"], dx < 0.04 && dz < 1e-3 && dyv < 1e-3 && Math.Abs(D(u["ringY"]) - ringY) < 1e-3 && (string)u["culture"] == cul && soldOk,
                "位置差 " + F(dx) + "," + F(dyv) + "," + F(dz) + " 光环 " + F(D(u["ringY"])) + "/" + F(ringY) + " 文化 " + u["culture"] + "/" + cul + " 士兵 " + ws.Count + "/" + slots.Count + (soldOk ? "" : " 队形不同"));
        }
    }
}
