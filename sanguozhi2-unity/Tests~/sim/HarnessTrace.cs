// JS ↔ C# 逐步对照：用 node 运行网页版规则（trace.js），与 C# 规则在同一种子下逐月比较整局 JSON、逐场比较战术战斗的结果与行动记录。
// 两边用同一随机序列（UnityStub 的 Random = SG.Random.seed）；Unity 的 Random.value / Random.Range(float, float) 返回 float，
// trace.js 用 Math.fround 照样取整，因此规则移植正确时轨迹逐字相同。任何分岔（对局任一月、任一场战斗）都判为失败。
// 找不到 node（或网页版目录）时判失败（设 SANGUO_ALLOW_NO_NODE=1 才跳过）。网页版目录取环境变量 SANGUO_WEB，缺省 ../../../sanguozhi2-web。
// 第三版：对局 JSON 含成长字段（经验、等级、习得、战绩：自动结算的经验与电脑自动习得逐月对照）；另加
//   pbattles：成长后的战术战斗（全电脑对局 36 个月后的局面经存档往返后开战、随机等级的局面），比对用招 / 计策编号与战后经验结算；
//   learn：升级选择序列（候选、空格、等阶、电脑之选、习得与一口气升到 10 级的自动习得）。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;

class PT
{
    static int fails;
    static string Node(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("node", "trace.js " + args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new System.Text.UTF8Encoding(false) };
        using (var p = System.Diagnostics.Process.Start(psi))
        {
            var text = p.StandardOutput.ReadToEnd(); var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception("node trace.js " + args + " failed: " + err);
            return text;
        }
    }
    static bool HaveNode()
    {
        try { Node("game 1 cao 1 classic"); return true; }
        catch (Exception e) { Console.WriteLine("  (node trace.js 不可用：" + e.Message.Split('\n')[0] + ")"); return false; }
    }

    static void Game(int seed, string key, int months, string scen)
    {
        var js = Node($"game {seed} {key} {months} {scen}").Split('\n');
        UnityEngine.Random.InitState(seed);
        var g = GameState.Current = GameState.NewGame(key, scen);
        g.player = -1;
        for (int m = 0; m < months; m++)
        {
            StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth();
            var cs = g.ToJson();
            if (cs != js[m])
            {
                int i = 0; while (i < cs.Length && i < js[m].Length && cs[i] == js[m][i]) i++;
                int a = Math.Max(0, cs.LastIndexOf('{', Math.Max(0, i - 1)));
                Func<string, string> ctx = s => a < s.Length ? s.Substring(a, Math.Min(160, s.Length - a)) : "";
                fails++;
                Console.WriteLine($"  FAIL {scen} seed {seed} ({key}): diverges after month {m + 1}\n    JS {ctx(js[m])}\n    C# {ctx(cs)}");
                return;
            }
        }
        Console.WriteLine($"  {scen} seed {seed} ({key}): {months} months identical");
    }

    static void Battles()
    {
        var js = Node("battles").Split('\n');
        var pairs = Sim.Pairs.Concat(new[] { new[] { "xiapi", "xiaopei" }, new[] { "changan", "tianshui" }, new[] { "chengdu", "jiangzhou" }, new[] { "ye", "jinyang" }, new[] { "shouchun", "lujiang" } }).ToArray();
        int n = 0, diff = 0;
        for (int on = 0; on <= 1; on++)
        {
            BattleModel.specialsEnabled = on == 1;
            for (int seed = 1; seed <= 200; seed++)
            {
                var pr = pairs[seed % pairs.Length];
                UnityEngine.Random.InitState((1000 + seed) * 7);
                var g = GameState.Current = GameState.NewGame("cao", "classic");
                var src = g.CityByKey(pr[0]); var tgt = g.CityByKey(pr[1]);
                var s = Conquest.Prepare(src.owner, src, tgt, g.OfficersIn(src).Where(x => x.troops > 0).Take(5).ToList(), 3000, 0);
                var M = new BattleModel(s); int guard = 0; var log = new List<string>();
                while (M.result == 0 && guard++ < 2000)
                {
                    for (int pass = 0; pass < 4 && M.result == 0; pass++)
                    {
                        var order = M.Alive(M.side).Where(M.CanAct).ToList();
                        if (order.Count == 0) break;
                        foreach (var u in order)
                        {
                            if (!M.CanAct(u) || M.result != 0) continue;
                            var plan = M.PlanFor(u);
                            if (plan.move.HasValue) M.Move(u, plan.move.Value);
                            if (M.result != 0) break;
                            var a = M.PlanAction(u);
                            if (a.kind == "attack") { var r = M.Attack(u, a.target); log.Add("A" + r.dmg + "/" + r.counter); }
                            else if (a.kind == "tactic") { var r = M.UseTactic(u, a.tactic, a.target); log.Add("T" + r.Value); }
                            else if (a.kind == "duel") { var r = M.Duel(u, a.target); log.Add("D" + r.winner); }
                            else if (a.kind == "special") { var r = M.UseSpecial(u, a.target); log.Add("S" + r.kind + r.dmg + "/" + r.heal); }
                            M.Spend(u);
                        }
                    }
                    if (M.result == 0) M.EndSide();
                }
                var line = string.Join("|", new[] { on.ToString(), seed.ToString(), M.result.ToString(), M.day.ToString(), M.resultReason,
                    string.Join(";", M.units.Select(u => string.Join(",", new[] { u.Troops, u.morale, u.alive ? 1 : 0, u.x, u.y, u.specialUsed ? 1 : 0 }))), string.Join(" ", log) });
                if (line != js[n]) { diff++; if (diff <= 3) Console.WriteLine($"  differs: specials {(on == 1 ? "on" : "off")} seed {seed}\n    JS {js[n].Substring(0, Math.Min(200, js[n].Length))}\n    C# {line.Substring(0, Math.Min(200, line.Length))}"); }
                n++;
            }
        }
        BattleModel.specialsEnabled = true;
        Console.WriteLine($"  {n} battles (specials off / on), {diff} differ from JS");
        if (diff > 0) fails++;
    }

    static string N(double v) { return Specials.JsNum(v); }
    static string B(bool v) { return v ? "true" : "false"; }
    static int Compare(string what, string[] js, List<string> cs)
    {
        int diff = 0;
        for (int i = 0; i < Math.Max(js.Length - 1, cs.Count); i++)
        {
            string a = i < js.Length ? js[i] : "(none)", b = i < cs.Count ? cs[i] : "(none)";
            if (a == b) continue;
            diff++;
            if (diff <= 3)
            {
                int k = 0; while (k < a.Length && k < b.Length && a[k] == b[k]) k++;
                int st = Math.Max(0, k - 80);
                Func<string, string> ctx = x => st < x.Length ? x.Substring(st, Math.Min(240, x.Length - st)) : "";
                Console.WriteLine($"  {what} line {i + 1} differs at char {k}:\n    JS …{ctx(a)}\n    C# …{ctx(b)}");
            }
        }
        if (js.Length - 1 != cs.Count) Console.WriteLine($"  {what}: JS {js.Length - 1} lines, C# {cs.Count}");
        return diff;
    }

    // 成长后的战术战斗（trace.js pbattles）
    static string GState(General x) { return string.Join(":", new[] { x.name, x.exp.ToString(), x.level.ToString(), string.Join("+", x.specials), string.Join("+", x.tactics), x.record.battles + "/" + x.record.wins + "/" + x.record.routs + "/" + x.record.duels }); }
    static string Fight(string tag, BattleSetup s)
    {
        var M = new BattleModel(s); int guard = 0; var log = new List<string>();
        while (M.result == 0 && guard++ < 2000)
        {
            for (int pass = 0; pass < 4 && M.result == 0; pass++)
            {
                var order = M.Alive(M.side).Where(M.CanAct).ToList();
                if (order.Count == 0) break;
                foreach (var u in order)
                {
                    if (!M.CanAct(u) || M.result != 0) continue;
                    var plan = M.PlanFor(u);
                    if (plan.move.HasValue) M.Move(u, plan.move.Value);
                    if (M.result != 0) break;
                    var a = M.PlanAction(u);
                    if (a.kind == "attack") { var r = M.Attack(u, a.target); log.Add("A" + r.dmg + "/" + r.counter); }
                    else if (a.kind == "tactic") { var r = M.UseTactic(u, a.tactic, a.target); log.Add("T" + a.tactic.Id + ":" + (r.Key ? 1 : 0) + ":" + r.Value); }
                    else if (a.kind == "duel") { var r = M.Duel(u, a.target); log.Add("D" + r.winner); }
                    else if (a.kind == "special") { var r = M.UseSpecial(u, a.target); log.Add("S" + (r.sp != null ? r.sp.id : "-") + ":" + r.kind + r.dmg + "/" + r.heal); }
                    M.Spend(u);
                }
            }
            if (M.result == 0) M.EndSide();
        }
        var pr = string.Join(";", (M.progress ?? new List<ProgressResult>()).Select(p => string.Join(":", new[] { p.gen.name, p.side.ToString(), p.won ? "1" : "0", p.exp.ToString(), p.from + ">" + p.to,
            string.Join("+", p.learned.Select(c => c.id)), string.Join("/", new[] { N(p.xp.dmg), N(p.xp.heal), N(p.xp.routs), N(p.xp.cmdRouts), p.xp.duelWin.ToString(), p.xp.duelLoss.ToString(),
            p.xp.specials.ToString(), p.xp.tactics.ToString(), p.xp.tacFail.ToString() }) })));
        return string.Join("|", new[] { tag, M.result.ToString(), M.day.ToString(), M.resultReason,
            string.Join(";", M.units.Select(u => string.Join(",", new[] { u.Troops.ToString(), u.morale.ToString(), u.alive ? "1" : "0", u.x.ToString(), u.y.ToString(), u.specialUsed ? "1" : "0", string.Join("+", u.spUsed) }))),
            string.Join(" ", log), pr, string.Join(";", s.atk.Concat(s.def).Select(GState)) });
    }
    static void PBattles()
    {
        var js = Node("pbattles").Split('\n');
        var cs = new List<string>();
        var pairs = Sim.Pairs;
        for (int seed = 1; seed <= 3; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var g0 = GameState.Current = GameState.NewGame("liubei", "classic");
            g0.player = -1;
            for (int m = 0; m < 36; m++) { StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth(); }
            var text = g0.ToJson();
            for (int k = 0; k < 14; k++)
            {
                var g = GameState.Current = GameState.FromJson(text);
                g.player = -1;
                UnityEngine.Random.InitState(500 + seed * 100 + k);
                var cand = new List<City[]>();
                foreach (var c in g.cities)
                {
                    if (c.owner < 0) continue;
                    foreach (var j in c.links) { var t = g.cities[j]; if (t.owner >= 0 && t.owner != c.owner && g.OfficersIn(c).Any(x => x.troops > 500) && g.OfficersIn(t).Any()) cand.Add(new[] { c, t }); }
                }
                if (cand.Count == 0) continue;
                var ct = cand[(k * 7 + seed) % cand.Count];
                var atk = g.OfficersIn(ct[0]).Where(x => x.troops > 0).OrderByDescending(x => x.troops).Take(5).ToList();
                cs.Add(Fight("campaign " + seed + " " + k, Conquest.Prepare(ct[0].owner, ct[0], ct[1], atk, 3000, 0)));
            }
        }
        for (int seed = 1; seed <= 24; seed++)
        {
            UnityEngine.Random.InitState(seed * 11);
            var g = GameState.Current = GameState.NewGame("cao", "classic");
            g.player = -1;
            var r = new SeededRandom(seed);
            foreach (var x in g.generals) Progress.Gain(x, r.Next(Progress.Exp[10] + 1));
            var pr = pairs[seed % pairs.Length];
            var src = g.CityByKey(pr[0]); var tgt = g.CityByKey(pr[1]);
            cs.Add(Fight("random " + seed, Conquest.Prepare(src.owner, src, tgt, g.OfficersIn(src).Where(x => x.troops > 0).Take(5).ToList(), 3000, 0)));
        }
        int diff = Compare("pbattles", js, cs);
        Console.WriteLine($"  {cs.Count} progressed battles (36-month campaign snapshots via save round trip + random levels), {diff} differ from JS");
        if (diff > 0) fails++;
    }

    // 升级选择序列（trace.js learn）
    static void Learn()
    {
        var js = Node("learn").Split('\n');
        var cs = new List<string>();
        UnityEngine.Random.InitState(3);
        var g = GameState.Current = GameState.NewGame("cao", "classic");
        int n = 0;
        Func<int, int, int, int, General> mk = (war, intel, lv, faction) =>
        {
            var x = new General { id = 99000 + n, name = "测试" + (n++), war = war, intel = intel, pol = 50, faction = faction, city = 0, troops = 3000, training = 60, loyalty = 80 };
            Progress.Ensure(x); x.exp = Progress.Exp[Math.Max(1, lv)]; Progress.Ensure(x);
            return x;
        };
        Func<SlotInfo, string> slot = q => string.Join("/", new[] { q.max, q.unlocked, q.used, q.open, q.tier, q.statTier, q.levelTier });
        var rnd = new SeededRandom(77);
        for (int i = 0; i < 300; i++)
        {
            int w = 40 + rnd.Next(61), it = 40 + rnd.Next(61), lv = 1 + rnd.Next(10);
            var x = mk(w, it, lv, -1);
            var line = new List<string> { x.name, x.war.ToString(), x.intel.ToString(), x.level.ToString(), string.Join("+", x.tactics) };
            for (int step = 0; step < 6; step++)
            {
                var c1 = Progress.Candidates(x); var op = Progress.OpeningsOf(x); var ai = Progress.AiPick(x, c1);
                line.Add(string.Join(",", c1.Select(c => c.type[0] + c.id + "@" + c.tier)) + ";" + op.special + "/" + op.tactic + ";" + slot(Progress.SpecialSlots(x)) + ";" + slot(Progress.TacticSlots(x)) + ";ai=" + (ai != null ? ai.id : "-"));
                if (c1.Count == 0) break;
                var pick = c1[rnd.Next(c1.Count)];
                bool l1 = Progress.Learn(x, pick), l2 = Progress.Learn(x, pick);
                line.Add("learn=" + pick.id + ":" + B(l1) + ":" + B(l2));
            }
            cs.Add(string.Join("|", line));
        }
        for (int k = 0; k < 40; k++)
        {
            int war = 55 + (k * 7) % 46, intel = 45 + (k * 11) % 56;
            var ai = mk(war, intel, 1, 3); var me = mk(war, intel, 1, g.player); var me2 = mk(war, intel, 1, g.player);
            Progress.playerAutoLearn = false;
            ProgressResult ra, rm;
            try { ra = Progress.Gain(ai, Progress.Exp[10]); rm = Progress.Gain(me, Progress.Exp[10]); } finally { Progress.playerAutoLearn = true; }
            var rm2 = Progress.Gain(me2, Progress.Exp[10]);
            cs.Add(string.Join("|", new[] { "auto", war.ToString(), intel.ToString(), string.Join("+", ra.learned.Select(c => c.id)), rm.learned.Count.ToString(), Progress.OpeningsOf(me).total.ToString(),
                string.Join("+", rm2.learned.Select(c => c.id)), string.Join("+", ai.specials), string.Join("+", ai.tactics) }));
        }
        cs.Add("log " + string.Join(" / ", g.log.Skip(Math.Max(0, g.log.Count - 5))));
        int diff = Compare("learn", js, cs);
        Console.WriteLine($"  {cs.Count} learning lines (300 offer sequences, 40 auto-learn to level 10), {diff} differ from JS");
        if (diff > 0) fails++;
    }

    static int Main()
    {
        BattleModel.specialsEnabled = true;   // 同网页版默认（游戏内暂时默认 false，见 BattleModel.specialsEnabled）
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.WriteLine("== JS ↔ C# traces (node trace.js) ==");
        // 找不到 node 或网页版目录时不能悄悄算作通过（这是成长规则唯一的自动 JS 对照）：判失败；确需跳过时设 SANGUO_ALLOW_NO_NODE=1
        if (!HaveNode())
        {
            if (Environment.GetEnvironmentVariable("SANGUO_ALLOW_NO_NODE") == "1") { Console.WriteLine("SKIPPED (SANGUO_ALLOW_NO_NODE=1)：没有与 JS 对照"); return 0; }
            Console.WriteLine("FAILURES: 1（node / 网页版不可用，未与 JS 对照；设 SANGUO_WEB 指向 sanguozhi2-web，或设 SANGUO_ALLOW_NO_NODE=1 跳过）");
            return 1;
        }
        try
        {
            foreach (var seed in new[] { 1, 2, 3 }) Game(seed, "liubei", 480, "classic");
            foreach (var seed in new[] { 1, 3, 1001 }) Game(seed, "roma", 300, "world");   // 种子 3 在 float 取整未模拟时于第 151 个月分岔
            Battles();
            PBattles();
            Learn();
        }
        catch (Exception e) { fails++; Console.WriteLine("trace FAILED: " + e); }
        Scenarios.Current = Scenarios.Classic;
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
}
