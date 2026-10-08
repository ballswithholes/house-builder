// 世界剧本「天下大势」检测：tests/world.js 的 C# 版。
//   (0) 投影 MapProjection 与 JS 的 SG.project / SG.unproject 参考值（ref/project-ref.txt）一致；
//   (1) 数据自检：格式、计数、经典剧本原样包含、连线（全图连通、海路是子集、度数）、势力 / 君主 / 都城、武将、资料表、
//       城池间距（投影后 ≥ 3）、势力颜色（CIEDE2000 色差）、显示名长度、开局状态与存档往返；
//   (2) 全电脑模拟：种子 × 年数（缺省同 JS：1-12,101-124,1001-1024 × 25 年；环境变量 WORLD_SEEDS / WORLD_YEARS 可改），
//       统计每秒月数、单月耗时、各地域势力存活、首个势力超过全部城池 25% 的时间、地域 3 年内全灭、罗马 / 安息 / 贵霜的城数走势；
//   (3) 经典剧本不受影响：同一种子下，世界剧本对局前后的经典剧本 5 年全电脑对局 JSON 完全相同（打印 MD5，可与 JS 对照）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Sanguo;

class PW
{
    static int fails;
    static bool Check(bool cond, string msg) { if (!cond) { fails++; Console.WriteLine("  FAIL " + msg); } return cond; }
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] CULTURES = { "han", "nanman", "wa", "yi", "korea", "steppe", "seasia", "tarim", "kushan", "persia", "arab", "roman", "celt", "german", "sarmatian" };
    static int CodePoints(string s) { int n = 0; foreach (var ch in s) if (!char.IsLowSurrogate(ch)) n++; return n; }

    static string ClassicHash()
    {
        UnityEngine.Random.InitState(7);
        var g = GameState.Current = GameState.NewGame("cao", "classic"); g.player = -1;
        for (int m = 0; m < 60; m++) { StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth(); }
        var t = g.ToJson();
        using (var md5 = System.Security.Cryptography.MD5.Create())
            return string.Concat(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(t)).Select(b => b.ToString("x2"))) + " " + t.Length;
    }

    static int Main()
    {
        BattleModel.specialsEnabled = true;   // 同网页版默认（游戏内暂时默认 false，见 BattleModel.specialsEnabled）
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var t0 = DateTime.Now;
        string classicBefore = ClassicHash();
        Projection();
        Data();
        Simulate();
        string classicAfter = ClassicHash();
        Console.WriteLine($"== (3) classic 5-year all-AI game, seed 7: before any world game {classicBefore} | after {classicAfter}");
        Check(classicBefore == classicAfter, "classic scenario identical before and after playing the world scenario");
        Scenarios.Current = Scenarios.Classic;
        Console.WriteLine($"{(fails == 0 ? "ALL OK" : fails + " FAILED")}  ({Sim.F((DateTime.Now - t0).TotalSeconds, 1)}s)");
        return fails;
    }

    // =========================================================== (0) 投影 --
    static void Projection()
    {
        Console.WriteLine("== (0) map projection vs JS reference (ref/project-ref.txt) ==");
        int n = 0; double worst = 0;
        foreach (var line in System.IO.File.ReadAllLines("ref/project-ref.txt"))
        {
            var p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 6 || p[3] != "=>" || (p[0] != "project" && p[0] != "unproject")) continue;
            double a = double.Parse(p[1], Inv), b = double.Parse(p[2], Inv), wx = double.Parse(p[4], Inv), wy = double.Parse(p[5], Inv), x, y;
            if (p[0] == "project") MapProjection.Project(a, b, out x, out y); else MapProjection.Unproject(a, b, out x, out y);
            double e = Math.Max(Math.Abs(x - wx), Math.Abs(y - wy));
            worst = Math.Max(worst, e); n++;
            if (e > 2e-6) Check(false, $"{p[0]} {p[1]} {p[2]} => {x:F6} {y:F6}, JS {p[4]} {p[5]}");
        }
        Console.WriteLine($"{n} samples, max |C# − JS| {worst:E1}");
        Check(n > 1000, "enough projection samples");
        // 经典剧本的城池坐标与第一版公式相同
        foreach (var l in ScenarioData.Cities)
        {
            var p = l.Split('|'); double lon = double.Parse(p[2], Inv), lat = double.Parse(p[3], Inv), x, y;
            MapProjection.Project(lon, lat, out x, out y);
            Check(Math.Abs(x - (lon - 100) * 5) < 1e-9 && Math.Abs(y - (lat - 23) * 5.6) < 1e-9, "classic city keeps the old map position " + p[0]);
        }
    }

    // =========================================================== (1) 数据 --
    static void Data()
    {
        Console.WriteLine("== (1) world scenario data ==");
        var W = Scenarios.World;
        var C = W.Cities.Select(l => l.Split('|')).ToList();
        var F = W.Factions.Select(l => l.Split('|')).ToList();
        var G = W.Generals.Select(l => l.Split('|')).ToList();
        int nC = ScenarioData.Cities.Length, nF = ScenarioData.Factions.Length, nG = ScenarioData.Generals.Length;
        Console.WriteLine($"cities {C.Count} (classic {nC} + new {C.Count - nC}), links {W.Links.Length} (sea {W.SeaLinks.Length}), factions {F.Count} (+{F.Count - nF}), generals {G.Count} (+{G.Count - nG}; hidden {G.Count(p => p[8] == "1")}), regions {W.Regions.Length}");
        Check(W.StartYear == 190 && W.StartMonth == 1, "start 190-1");
        Check(W.Intro.EndsWith("你将选择一位君主……"), "Intro ends with 你将选择一位君主……");
        for (int i = 0; i < nC; i++) Check(W.Cities[i].StartsWith(ScenarioData.Cities[i] + "|"), "classic city unchanged " + ScenarioData.Cities[i]);
        for (int i = 0; i < nF; i++) Check(W.Factions[i].StartsWith(ScenarioData.Factions[i] + "|"), "classic faction unchanged " + ScenarioData.Factions[i]);
        for (int i = 0; i < nG; i++) Check(W.Generals[i].StartsWith(ScenarioData.Generals[i] + "|"), "classic general unchanged " + ScenarioData.Generals[i]);
        foreach (var l in ScenarioData.Links) Check(W.Links.Contains(l), "classic link kept " + l);
        // 城池
        var cityKeys = new List<string>(); var cityNames = new HashSet<string>();
        foreach (var p in C)
        {
            Check(p.Length == 11, "city has 11 columns: " + string.Join("|", p));
            Check(!cityKeys.Contains(p[0]), "unique city key " + p[0]); cityKeys.Add(p[0]);
            Check(cityNames.Add(p[1]), "unique city name " + p[1]);
            double tmp;
            for (int i = 2; i <= 9; i++) Check(double.TryParse(p[i], NumberStyles.Float, Inv, out tmp), "numeric city field " + p[0] + "[" + i + "]");
            Check(CULTURES.Contains(p[10]), "city culture " + p[0] + " " + p[10]);
            int land = int.Parse(p[4]), ind = int.Parse(p[5]), town = int.Parse(p[6]);
            Check(land <= 999 && ind <= 999 && town <= 999 && land > 0 && ind > 0 && town > 0, "city stats in range " + p[0]);
        }
        // 连线
        var adj = cityKeys.ToDictionary(k => k, k => new List<string>());
        var linkSet = new HashSet<string>();
        foreach (var l in W.Links)
        {
            var ab = l.Split('-'); string a = ab[0], b = ab[1];
            Check(adj.ContainsKey(a) && adj.ContainsKey(b), "link resolves " + l);
            Check(a != b, "no self link " + l);
            var id = string.CompareOrdinal(a, b) < 0 ? a + "-" + b : b + "-" + a;
            Check(linkSet.Add(id), "no duplicate link " + l);
            if (adj.ContainsKey(a) && adj.ContainsKey(b)) { adj[a].Add(b); adj[b].Add(a); }
        }
        foreach (var l in W.SeaLinks) Check(W.Links.Contains(l), "sea link is in Links " + l);
        {
            var seen = new HashSet<string> { C[0][0] }; var q = new Stack<string>(); q.Push(C[0][0]);
            while (q.Count > 0) { var x = q.Pop(); foreach (var y in adj[x]) if (seen.Add(y)) q.Push(y); }
            Check(seen.Count == cityKeys.Count, "graph connected (" + seen.Count + "/" + cityKeys.Count + ")");
            var degs = cityKeys.Select(k => adj[k].Count).ToList();
            var hist = degs.GroupBy(d => d).OrderBy(gr => gr.Key).Select(gr => "\"" + gr.Key + "\":" + gr.Count());
            int in25 = degs.Count(d => d >= 2 && d <= 5);
            Console.WriteLine($"degree histogram {{{string.Join(",", hist)}}}; {in25}/{degs.Count} in 2..5; max {degs.Max()} ({string.Join(",", cityKeys.Where(k => adj[k].Count >= 6))})");
            Check(degs.Max() <= 6, "max degree ≤ 6");
            Check(in25 / (double)degs.Count >= 0.85, "degree mostly 2..5");
        }
        // 势力
        var facKeys = new HashSet<string>();
        var regionIds = new HashSet<string>(W.Regions.Select(r => r.Id));
        var inRegion = new Dictionary<string, string>();
        foreach (var r in W.Regions) foreach (var k in r.Factions) { Check(!inRegion.ContainsKey(k), "faction in one region " + k); inRegion[k] = r.Id; }
        var genByName = new Dictionary<string, string[]>();
        foreach (var p in G) genByName[p[0]] = p;
        foreach (var p in F)
        {
            Check(p.Length == 8, "faction has 8 columns: " + string.Join("|", p));
            Check(facKeys.Add(p[0]), "unique faction key " + p[0]);
            Check(Regex.IsMatch(p[3], "^#[0-9a-fA-F]{6}$"), "faction colour " + p[0]);
            Check(CULTURES.Contains(p[6]), "faction culture " + p[0]);
            Check(regionIds.Contains(p[7]) && inRegion.ContainsKey(p[0]) && inRegion[p[0]] == p[7], "faction region " + p[0] + " " + p[7]);
            string[] r;
            if (Check(genByName.TryGetValue(p[2], out r) && r[4] == p[0], "ruler " + p[2] + " is a general of " + p[0]))
            {
                var fi = WorldScenarioData.InfoOfFaction(p[0]);
                if (fi != null) Check(fi.Capital == r[5] && fi.Ruler == p[2], "ruler " + p[2] + " sits in capital " + fi.Capital);
            }
        }
        foreach (var r in W.Regions) Check(r.Factions.Length > 0 && !string.IsNullOrEmpty(r.Name), "region " + r.Id);
        Check(inRegion.Count == F.Count, "every faction has a region");
        // 武将
        var names = new HashSet<string>();
        foreach (var p in G)
        {
            Check(p.Length == 11, "general has 11 columns: " + string.Join("|", p));
            Check(names.Add(p[0]), "unique general name " + p[0]);
            Check(p[4] == "-" || facKeys.Contains(p[4]), "general faction " + p[0] + " " + p[4]);
            Check(cityKeys.Contains(p[5]), "general city " + p[0] + " " + p[5]);
            Check(CULTURES.Contains(p[9]), "general culture " + p[0] + " " + p[9]);
            for (int i = 1; i <= 3; i++) Check(int.Parse(p[i]) >= 1 && int.Parse(p[i]) <= 100, "stat range " + p[0]);
            if (p[8] == "1") Check(p[4] == "-" && p[6] == "0" && p[7] == "0", "hidden general loyalty 0 troops 0 " + p[0]);
            else Check(p[4] != "-", "non-hidden general belongs to a faction " + p[0]);
        }
        var newGens = G.Skip(nG).ToList();
        foreach (var p in newGens)
        {
            var wi = WorldScenarioData.InfoOfGeneral(p[0]);
            if (!Check(wi != null, "WorldInfo for " + p[0])) continue;
            Check(wi.Full != null && wi.Orig != null && wi.Role != null && wi.Note != null && wi.Attested != null && wi.Culture != null && wi.Sex != null, "WorldInfo fields " + p[0]);
            Check(wi.Sex == "m" || wi.Sex == "f", "WorldInfo.sex m/f " + p[0]);
            Check(wi.Died == null || wi.Died >= 190, "nobody died before 190: " + p[0] + " " + wi.Died);
            Check(wi.Culture == p[9], "WorldInfo culture matches " + p[0]);
            if (p[8] != "1" && p[4] != "-") Check(int.Parse(p[7]) >= 0 && int.Parse(p[7]) <= 6000, "troops within the classic range 0..6000 " + p[0]);
            Check(wi.Born == null || wi.Born <= 205, "born ≤ 205 " + p[0]);
            Check((wi.Born == null ? "" : wi.Born.Value.ToString()) == p[10], "born column matches WorldInfo " + p[0]);
        }
        foreach (var p in F.Skip(nF)) { var fi = WorldScenarioData.InfoOfFaction(p[0]); Check(fi != null && fi.Name == p[1] && fi.Ruler == p[2], "FactionInfo name / ruler match " + p[0]); }
        // 性别：四位女性；SexOf 覆盖经典武将（祝融）
        {
            var fem = newGens.Where(p => { var wi = WorldScenarioData.InfoOfGeneral(p[0]); return wi != null && wi.Sex == "f"; }).Select(p => p[0]).OrderBy(x => x, StringComparer.Ordinal);
            Check(string.Join(",", fem) == string.Join(",", new[] { "卑弥呼", "多姆娜", "玛伊莎", "玛琪亚" }.OrderBy(x => x, StringComparer.Ordinal)), "female generals " + string.Join(",", fem));
            Check(WorldLookup.SexOf("玛琪亚") == "f" && WorldLookup.SexOf("祝融") == "f" && WorldLookup.SexOf("曹操") == "m" && WorldLookup.SexOf("塞维鲁") == "m", "WorldLookup.SexOf");
            Check(WorldLookup.BornOf("曹操") == 155 && WorldLookup.BornOf("塞维鲁") == 145 && WorldLookup.BornOf("张飞") == null, "WorldLookup.BornOf");
        }
        // 给玩家看的文字：不得残留研究 / 合并阶段的说明、内部键名或 ASCII 引号
        {
            var META = new Regex("合并时|去重|研究摘要|JSON|data\\.js|DESIGN|notes?\\b|本区|中国区|【游戏取舍】|\"");
            var allKeys = new HashSet<string>(cityKeys.Concat(F.Select(p => p[0])));
            var bad = new List<string>();
            Action<string, string, string> scan = (tab, key, t) =>
            {
                if (t == null) return;
                if (META.IsMatch(t) || Regex.Matches(t, "\\b[a-z_]{4,}\\b").Cast<Match>().Any(m => allKeys.Contains(m.Value))) bad.Add(tab + "." + key);
            };
            foreach (var x in WorldScenarioData.GeneralInfo) { scan("WorldInfo", x.Name + ".full", x.Full); scan("WorldInfo", x.Name + ".role", x.Role); scan("WorldInfo", x.Name + ".note", x.Note); scan("WorldInfo", x.Name + ".liberty", x.Liberty); }
            foreach (var x in WorldScenarioData.FactionInfo) { scan("FactionInfo", x.Key + ".full", x.Full); scan("FactionInfo", x.Key + ".note", x.Note); scan("FactionInfo", x.Key + ".liberty", x.Liberty); }
            foreach (var x in WorldScenarioData.CityInfo) { scan("CityInfo", x.Key + ".full", x.Full); scan("CityInfo", x.Key + ".note", x.Note); scan("CityInfo", x.Key + ".modern", x.Modern); }
            Check(bad.Count == 0, "player-facing text has no research / merge leftovers: " + string.Join(", ", bad));
            var NOTE_META = new Regex("维基|文献作|英文文献|未找到|译名|自拟|数值|三维|按规则|放宽|游戏安排|游戏中|设为空城|在任武将|为凑成|校准|[武智政德望]极?[高低]|忠诚(?:偏|设)?低|故(?:武力|智力|政治|政略|智谋)");
            var noteBad = WorldScenarioData.GeneralInfo.Where(x => NOTE_META.IsMatch(x.Note ?? "")).Select(x => "WorldInfo." + x.Name)
                .Concat(WorldScenarioData.FactionInfo.Where(x => NOTE_META.IsMatch(x.Note ?? "")).Select(x => "FactionInfo." + x.Key)).ToList();
            Check(noteBad.Count == 0, "history notes carry no source / stat / rule remarks: " + string.Join(", ", noteBad));
            Func<string, bool> rare = s => s != null && s.Any(char.IsSurrogate);
            var rareL = WorldScenarioData.GeneralInfo.Where(x => rare(x.Full) || rare(x.Role) || rare(x.Note) || rare(x.Liberty)).Select(x => x.Name)
                .Concat(WorldScenarioData.FactionInfo.Where(x => rare(x.Full) || rare(x.Note) || rare(x.Liberty)).Select(x => x.Key)).ToList();
            Check(rareL.Count == 0, "no CJK Extension B+ characters (most system fonts lack them): " + string.Join(", ", rareL));
            Func<string, bool> dash = s => s != null && s.Contains("–");
            var dashL = WorldScenarioData.GeneralInfo.Where(x => dash(x.Full) || dash(x.Role) || dash(x.Note) || dash(x.Liberty)).Select(x => x.Name)
                .Concat(WorldScenarioData.FactionInfo.Where(x => dash(x.Full) || dash(x.Note) || dash(x.Liberty)).Select(x => x.Key)).ToList();
            Check(dashL.Count == 0, "date ranges use 「—」, not 「–」: " + string.Join(", ", dashL));
        }
        foreach (var p in C.Skip(nC)) Check(WorldScenarioData.InfoOfCity(p[0]) != null, "CityInfo " + p[0]);
        foreach (var l in WorldScenarioData.GeneralBorn) { var p = l.Split('|'); Check(genByName.ContainsKey(p[0]) && genByName[p[0]][10] == p[1], "GeneralBorn " + p[0]); }
        foreach (var n in new[] { "孟获", "祝融", "孟优", "兀突骨", "带来洞主", "沙摩柯" }) Check(genByName[n][9] == "nanman", "nanman " + n);
        Check(WorldLookup.CultureOfGeneral("卑弥呼") == "wa" && WorldLookup.RegionOfFaction("roma") == "roma", "WorldLookup lookups");
        // 统计值与经典剧本同一尺度
        {
            Func<IEnumerable<string[]>, int, double> avg = (rows, i) => rows.Average(p => double.Parse(p[i], Inv));
            var cg = G.Take(nG).ToList(); var ng = newGens;
            Console.WriteLine($"general stat means  classic war {Sim.F(avg(cg, 1), 0)} int {Sim.F(avg(cg, 2), 0)} pol {Sim.F(avg(cg, 3), 0)}  |  new war {Sim.F(avg(ng, 1), 0)} int {Sim.F(avg(ng, 2), 0)} pol {Sim.F(avg(ng, 3), 0)}");
            var cc = C.Take(nC).ToList(); var nc = C.Skip(nC).ToList();
            Console.WriteLine($"city means  classic land {Sim.F(avg(cc, 4), 0)} ind {Sim.F(avg(cc, 5), 0)} town {Sim.F(avg(cc, 6), 0)} pop {Sim.F(avg(cc, 7) / 1e4, 0)}万 gold {Sim.F(avg(cc, 8), 0)} food {Sim.F(avg(cc, 9), 0)}  |  new land {Sim.F(avg(nc, 4), 0)} ind {Sim.F(avg(nc, 5), 0)} town {Sim.F(avg(nc, 6), 0)} pop {Sim.F(avg(nc, 7) / 1e4, 0)}万 gold {Sim.F(avg(nc, 8), 0)} food {Sim.F(avg(nc, 9), 0)}");
            Check(Math.Abs(avg(cg, 1) - avg(ng, 1)) < 8 && Math.Abs(avg(cg, 2) - avg(ng, 2)) < 8 && Math.Abs(avg(cg, 3) - avg(ng, 3)) < 8, "new general stats on the classic scale");
        }
        // 间距
        {
            double min = 1e9; string pair = "";
            var P = C.Select(p => { double x, y; MapProjection.Project(double.Parse(p[2], Inv), double.Parse(p[3], Inv), out x, out y); return new { k = p[1], x, y }; }).ToList();
            for (int i = 0; i < P.Count; i++) for (int j = i + 1; j < P.Count; j++)
                {
                    double d = Math.Sqrt((P[i].x - P[j].x) * (P[i].x - P[j].x) + (P[i].y - P[j].y) * (P[i].y - P[j].y));
                    if (d < min) { min = d; pair = P[i].k + "–" + P[j].k; }
                    if (i >= nC || j >= nC) Check(d >= 3, $"cities ≥ 3 apart: {P[i].k}–{P[j].k} {Sim.F(d, 2)}");
                }
            Console.WriteLine($"closest cities {pair} {Sim.F(min, 2)} map units");
        }
        // 颜色（CIEDE2000）
        {
            double min = 1e9, cmin = 1e9; string pair = "", cpair = "";
            var labs = F.Select(p => Lab(p[3])).ToList();
            for (int i = 0; i < F.Count; i++) for (int j = i + 1; j < F.Count; j++)
                {
                    double d = De2000(labs[i], labs[j]);
                    if (j >= nF && d < min) { min = d; pair = F[i][1] + "/" + F[j][1]; }
                    if (j < nF && d < cmin) { cmin = d; cpair = F[i][1] + "/" + F[j][1]; }
                }
            Console.WriteLine($"faction colours: min ΔE2000 for pairs with a new faction {Sim.F(min, 1)} ({pair}); classic-only pairs (unchanged) {Sim.F(cmin, 1)} ({cpair})");
            Check(min >= 10, "new faction colours ≥ 10 ΔE2000 from every other faction");
        }
        // 显示名长度
        {
            var longC = C.Skip(nC).Where(p => CodePoints(p[1]) > 4).Select(p => p[1]).ToList();
            var longG = newGens.Where(p => CodePoints(p[0]) > 4).Select(p => p[0]).ToList();
            var longF = F.Skip(nF).Where(p => CodePoints(p[1]) > 4).Select(p => p[1]).ToList();
            var g6 = newGens.Where(p => CodePoints(p[0]) > 5).Select(p => p[0]).ToList();
            Console.WriteLine($"names > 4 chars: cities {longC.Count} [{string.Join(" ", longC)}], generals {longG.Count} [{string.Join(" ", longG)}], factions {longF.Count} [{string.Join(" ", longF)}]");
            Check(C.Skip(nC).All(p => CodePoints(p[1]) <= 5) && F.Skip(nF).All(p => CodePoints(p[1]) <= 5), "city / faction display names ≤ 5 chars");
            Check(newGens.All(p => CodePoints(p[0]) <= 6) && g6.Count <= 1, "general display names ≤ 5 chars (at most one of 6): " + string.Join(",", g6));
        }
        // 开局状态
        try
        {
            UnityEngine.Random.InitState(1);
            var g0 = GameState.Current = GameState.NewGame("roma", "world");
            foreach (var f in g0.factions) Check(g0.CityCount(f.id) >= 1, "faction owns a city at start " + f.name);
            foreach (var c in g0.cities) if (c.owner >= 0) Check(g0.OfficersIn(c).Any(), "owned city has an officer " + c.name);
            int nNeutral = g0.cities.Count(c => c.owner < 0);
            Console.WriteLine($"start: {g0.factions.Count} factions, {g0.cities.Count - nNeutral} owned cities, {nNeutral} neutral");
            Check(g0.scenario == "world" && g0.seaLinks.Count == W.SeaLinks.Length && g0.cities.All(c => c.culture != null) && g0.factions.All(f => f.region != null), "world derived fields (scenario, sea links, culture, region)");
            Check(g0.LinkIsSea(g0.CityByKey(W.SeaLinks[0].Split('-')[0]).id, g0.CityByKey(W.SeaLinks[0].Split('-')[1]).id), "LinkIsSea");
            var g1 = GameState.FromJson(g0.ToJson());
            Check(g1 != null && g1.cities.Count == g0.cities.Count && g1.cities.Select((c, i) => c.links.Count == g0.cities[i].links.Count).All(b => b), "save round trip");
        }
        catch (Exception e) { Check(false, "newGame: " + e); }
    }

    static double[] Lab(string h)
    {
        Func<int, double> lin = c => { double v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
        double r = lin(int.Parse(h.Substring(1, 2), NumberStyles.HexNumber)), g = lin(int.Parse(h.Substring(3, 2), NumberStyles.HexNumber)), b = lin(int.Parse(h.Substring(5, 2), NumberStyles.HexNumber));
        Func<double, double> f = t => t > 0.008856 ? Math.Pow(t, 1.0 / 3) : 7.787 * t + 16.0 / 116;
        double x = f((r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047), y = f(r * 0.2126 + g * 0.7152 + b * 0.0722), z = f((r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883);
        return new[] { 116 * y - 16, 500 * (x - y), 200 * (y - z) };
    }
    static double De2000(double[] c1, double[] c2)
    {
        double L1 = c1[0], a1 = c1[1], b1 = c1[2], L2 = c2[0], a2 = c2[1], b2 = c2[2];
        double rad = Math.PI / 180; Func<double, double> p7 = v => Math.Pow(v, 7);
        Func<double, double, double> hyp = (p, q) => Math.Sqrt(p * p + q * q);
        double Cb = (hyp(a1, b1) + hyp(a2, b2)) / 2, Gk = 0.5 * (1 - Math.Sqrt(p7(Cb) / (p7(Cb) + p7(25))));
        double a1p = (1 + Gk) * a1, a2p = (1 + Gk) * a2, C1 = hyp(a1p, b1), C2 = hyp(a2p, b2);
        double h1 = (Math.Atan2(b1, a1p) / rad + 360) % 360, h2 = (Math.Atan2(b2, a2p) / rad + 360) % 360;
        double dh = h2 - h1; if (C1 * C2 == 0) dh = 0; else if (dh > 180) dh -= 360; else if (dh < -180) dh += 360;
        double dH = 2 * Math.Sqrt(C1 * C2) * Math.Sin(dh / 2 * rad), Lb = (L1 + L2) / 2, Cbp = (C1 + C2) / 2;
        double hb = h1 + h2; if (C1 * C2 != 0) { if (Math.Abs(h1 - h2) > 180) hb += hb < 360 ? 360 : -360; hb /= 2; }
        double T = 1 - 0.17 * Math.Cos((hb - 30) * rad) + 0.24 * Math.Cos(2 * hb * rad) + 0.32 * Math.Cos((3 * hb + 6) * rad) - 0.2 * Math.Cos((4 * hb - 63) * rad);
        double SL = 1 + 0.015 * (Lb - 50) * (Lb - 50) / Math.Sqrt(20 + (Lb - 50) * (Lb - 50)), SC = 1 + 0.045 * Cbp, SH = 1 + 0.015 * Cbp * T;
        double RT = -Math.Sin(60 * Math.Exp(-Math.Pow((hb - 275) / 25, 2)) * rad) * 2 * Math.Sqrt(p7(Cbp) / (p7(Cbp) + p7(25)));
        return Math.Sqrt((L2 - L1) * (L2 - L1) / (SL * SL) + (C2 - C1) * (C2 - C1) / (SC * SC) + dH * dH / (SH * SH) + RT * ((C2 - C1) / SC) * (dH / SH));
    }

    // =========================================================== (2) 模拟 --
    static List<int> SeedList()
    {
        var a = Environment.GetEnvironmentVariable("WORLD_SEEDS");
        if (string.IsNullOrEmpty(a)) a = "1-12,101-124,1001-1024";
        var outList = new List<int>();
        if (a.Contains("-") || a.Contains(","))
        {
            foreach (var part in a.Split(','))
            {
                var lh = part.Split('-'); int lo = int.Parse(lh[0]), hi = lh.Length > 1 && lh[1].Length > 0 ? int.Parse(lh[1]) : lo;
                for (int s = lo; s <= hi; s++) outList.Add(s);
            }
            return outList;
        }
        int n = int.Parse(a); for (int i = 0; i < n; i++) outList.Add(i + 1);
        return outList;
    }
    static void Simulate()
    {
        var W = Scenarios.World;
        var seeds = SeedList(); int SEEDS = seeds.Count;
        int YEARS = int.TryParse(Environment.GetEnvironmentVariable("WORLD_YEARS"), out YEARS) && YEARS > 0 ? YEARS : 25;
        Console.WriteLine($"== (2) all-AI simulation: {SEEDS} seeds x {YEARS} years ==");
        var regionName = W.Regions.ToDictionary(r => r.Id, r => r.Name);
        var CHECK_Y = new[] { 1, 3, 5, 10, 15, 20, 25 }.Where(y => y <= YEARS).ToArray();
        var surv = W.Regions.ToDictionary(r => r.Id, r => new double[CHECK_Y.Length]);
        var watch = new[] { "roma", "parthia", "kushan" };
        var watchN = watch.ToDictionary(k => k, k => CHECK_Y.Select(_ => new List<int>()).ToArray());
        var WEST_REG = new[] { "roma", "barbar", "bosi", "arab" };
        var WEST_CULT = new[] { "roman", "celt", "german", "sarmatian", "persia", "arab" };
        var westShare = CHECK_Y.Select(_ => new List<double>()).ToArray();
        int WEST_N = 0;
        var first25 = new List<KeyValuePair<int, string>?>(); var wiped = new List<string>(); var wipedFast = new List<string>();
        int months = 0; double simMs = 0, maxMs = 0; string maxAt = "";
        var wall = new List<double>();
        var finals = new List<string>();
        foreach (var seed in seeds)
        {
            UnityEngine.Random.InitState(seed);
            var g = GameState.Current = GameState.NewGame("roma", "world");
            g.player = -1;
            int total = g.cities.Count;
            var regF = W.Regions.ToDictionary(r => r.Id, r => r.Factions.Select(k => g.FactionByKey(k).id).ToArray());
            var regDead = new Dictionary<string, int>();
            var westCities = new HashSet<int>();
            foreach (var c in g.cities)
                if (c.owner >= 0 ? WEST_REG.Contains(g.factions[c.owner].region) : WEST_CULT.Contains(W.Cities[c.id].Split('|')[10])) westCities.Add(c.id);
            KeyValuePair<int, string>? f25 = null;
            try
            {
                for (int m = 0; m < YEARS * 12; m++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var pb = StrategyAI.RunAI(new List<string>());
                    if (pb.Count > 0) throw new Exception("player battle without player");
                    StrategyAI.EndMonth();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    simMs += ms; months++; wall.Add(ms);
                    if (ms > maxMs) { maxMs = ms; maxAt = $"seed {seed} month {m}"; }
                    Sim.Consistency(g);
                    if (f25 == null) foreach (var f in g.factions) if (f.alive && g.CityCount(f.id) > total * 0.25) { f25 = new KeyValuePair<int, string>(m, f.name); break; }
                    foreach (var r in W.Regions) if (!regDead.ContainsKey(r.Id) && regF[r.Id].All(i => !g.factions[i].alive)) regDead[r.Id] = m + 1;
                    int yi = (m + 1) % 12 == 0 ? Array.IndexOf(CHECK_Y, (m + 1) / 12) : -1;
                    if (yi >= 0)
                    {
                        foreach (var r in W.Regions) surv[r.Id][yi] += regF[r.Id].Count(i => g.factions[i].alive);
                        foreach (var k in watch) watchN[k][yi].Add(g.CityCount(g.FactionByKey(k).id));
                        int rid = g.FactionByKey("roma").id; int rw = westCities.Count(id => g.cities[id].owner == rid);
                        westShare[yi].Add(rw / (double)westCities.Count); WEST_N = westCities.Count;
                    }
                }
            }
            catch (Exception e) { Check(false, $"seed {seed}: {e}"); continue; }
            first25.Add(f25);
            foreach (var kv in regDead) if (kv.Value < 36) wiped.Add($"seed {seed} {regionName[kv.Key]} ({kv.Value} months)");
            foreach (var kv in regDead) if (kv.Value < 24) wipedFast.Add($"seed {seed} {regionName[kv.Key]} ({kv.Value} months)");
            var top = g.factions.Where(f => f.alive).Select(f => new { f, n = g.CityCount(f.id) }).OrderByDescending(o => o.n).Take(6).Select(o => o.f.name + ":" + o.n);
            string f25s = f25 != null ? f25.Value.Value + " @" + (190 + f25.Value.Key / 12) + "-" + (f25.Value.Key % 12 + 1) : "never";
            finals.Add($"seed {seed}: alive {g.factions.Count(f => f.alive)}/{g.factions.Count}, neutral {g.cities.Count(c => c.owner < 0)}, first >25% {f25s}; top [{string.Join(", ", top)}]");
        }
        foreach (var l in finals) Console.WriteLine(l);
        wall.Sort();
        Func<double, string> pct = p => Sim.F(wall[Math.Min(wall.Count - 1, (int)Math.Floor(wall.Count * p))], 1);
        Console.WriteLine($"speed: {months} months in {Sim.F(simMs / 1000, 1)}s = {Sim.F(months / (simMs / 1000), 0)} months/s; wall ms per month avg {Sim.F(simMs / months, 2)}, p99 {pct(0.99)}, p99.9 {pct(0.999)}, max {Sim.F(wall[wall.Count - 1], 1)} ({maxAt})");
        Check(double.Parse(pct(0.999), Inv) < 60, "p99.9 per-month wall time < 60 ms");
        Console.WriteLine("region factions alive (avg over seeds) at years " + string.Join("/", CHECK_Y) + ":");
        foreach (var r in W.Regions) Console.WriteLine($"  {(r.Name + "　　　　").Substring(0, 6)} {r.Factions.Length} → {string.Join(" / ", surv[r.Id].Select(v => Sim.F(v / SEEDS, 1)))}");
        Func<List<int>, string> avgN = a => a.Count > 0 ? Sim.F(a.Average(), 1) : "-";
        Func<string, string> fname = k => W.Factions.First(l => l.StartsWith(k + "|")).Split('|')[1];
        foreach (var k in watch) Console.WriteLine($"  {fname(k)} cities avg [min..max]: " + string.Join(", ", CHECK_Y.Select((y, i) => $"{y}y {avgN(watchN[k][i])} [{watchN[k][i].Min()}..{watchN[k][i].Max()}]")));
        Console.WriteLine($"  罗马 share of the West's {WEST_N} cities avg [max]: " + string.Join(", ", CHECK_Y.Select((y, i) => $"{y}y {Sim.F(100 * westShare[i].Sum() / Math.Max(1, westShare[i].Count), 0)}% [{Sim.F(100 * westShare[i].Max(), 0)}%]")));
        foreach (var k in watch) Console.WriteLine($"  {fname(k)} alive in " + string.Join(", ", CHECK_Y.Select((y, i) => $"{y}y {watchN[k][i].Count(n => n > 0)}/{watchN[k][i].Count}")) + " seeds");
        var f25m = first25.Select(x => x != null ? (double)x.Value.Key : double.PositiveInfinity).ToList();
        Console.WriteLine("first faction > 25% of cities: " + string.Join(", ", first25.Select(x => x != null ? Sim.F(x.Value.Key / 12.0, 1) + "y " + x.Value.Value : "never")));
        Console.WriteLine("regions wiped out in < 3 years: " + (wiped.Count > 0 ? string.Join("; ", wiped) : "none"));
        {
            var hit = first25.Where(x => x != null).Select(x => x.Value).ToList();
            var byF = new List<string>(); var cnt = new Dictionary<string, int>();
            foreach (var x in hit) { if (!cnt.ContainsKey(x.Value)) { cnt[x.Value] = 0; byF.Add(x.Value); } cnt[x.Value]++; }
            var ys = hit.Select(x => x.Key / 12.0).OrderBy(x => x).ToList();
            Console.WriteLine($"first > 25%: {hit.Count}/{first25.Count} seeds [{string.Join(", ", byF.Select(k => k + " " + cnt[k]))}], earliest {(ys.Count > 0 ? Sim.F(ys[0], 1) + "y" : "-")}");
            var alive = finals.Select(l => int.Parse(Regex.Match(l, "alive (\\d+)").Groups[1].Value)).ToList();
            if (alive.Count > 0) Console.WriteLine($"factions alive after {YEARS}y: {alive.Min()}–{alive.Max()} of {W.Factions.Length}");
        }
        var sorted25 = f25m.OrderBy(x => x).ToList();
        double med = sorted25.Count > 0 ? sorted25[sorted25.Count >> 1] : double.PositiveInfinity;
        Console.WriteLine($"median time to 25%: {(double.IsInfinity(med) ? "never" : Sim.F(med / 12, 1) + "y")} (classic scenario, same harness: ~2y)");
        Check(wiped.Count <= Math.Max(1, SEEDS / 12), $"regions wiped out within 3 years: at most 1 per 12 seeds (got {wiped.Count})");
        Check(wipedFast.Count <= SEEDS / 48, $"regions wiped out within 2 years: at most 1 per 48 seeds (got {wipedFast.Count}{(wipedFast.Count > 0 ? ": " + string.Join("; ", wipedFast) : "")})");
        Check(f25m.All(m => m >= 24), "no faction above 25% of all cities within 2 years");
        Check(med >= 48, "median time to 25% ≥ 4 years");
        int yi3 = Array.IndexOf(CHECK_Y, 3), yi5 = Array.IndexOf(CHECK_Y, 5);
        if (yi3 >= 0) Check(watchN["roma"][yi3].All(n => n >= 6), "Rome keeps ≥ 6 cities at year 3 in every seed");
        if (yi5 >= 0)
        {
            double ws5 = westShare[yi5].Average();
            Check(westShare[yi5].All(v => v < 0.95) && ws5 < 0.7, $"Rome holds < 95% of the West at year 5 in every seed and < 70% on average (avg {Sim.F(100 * ws5, 0)}%, max {Sim.F(100 * westShare[yi5].Max(), 0)}%)");
            Check(watchN["roma"][yi5].All(n => n >= 12), "Rome keeps ≥ 12 cities at year 5 in every seed");
            Check(surv["barbar"][yi5] / SEEDS >= 1.5, "barbarian factions: ≥ 1.5 alive on average at year 5");
            Check(watchN["parthia"][yi5].Count(n => n > 0) >= SEEDS * 0.5, "Parthia alive at year 5 in ≥ 50% of seeds");
            Check(watchN["kushan"][yi5].Count(n => n > 0) >= SEEDS * 0.5, "Kushan alive at year 5 in ≥ 50% of seeds");
        }
    }
}
