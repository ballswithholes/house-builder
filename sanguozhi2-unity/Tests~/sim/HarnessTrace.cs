// JS ↔ C# 逐步对照：用 node 运行网页版规则（trace.js），与 C# 规则在同一种子下逐月比较整局 JSON、逐场比较战术战斗的结果与行动记录。
// 两边用同一随机序列（UnityStub 的 Random = SG.Random.seed）；Unity 的 Random.value / Random.Range(float, float) 返回 float，
// trace.js 用 Math.fround 照样取整，因此规则移植正确时轨迹逐字相同。任何分岔（对局任一月、任一场战斗）都判为失败。
// 找不到 node（或网页版目录）时跳过。网页版目录取环境变量 SANGUO_WEB，缺省 ../../../sanguozhi2-web。
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
        catch (Exception e) { Console.WriteLine("  (skipped: " + e.Message.Split('\n')[0] + ")"); return false; }
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

    static int Main()
    {
        BattleModel.specialsEnabled = true;   // 同网页版默认（游戏内暂时默认 false，见 BattleModel.specialsEnabled）
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.WriteLine("== JS ↔ C# traces (node trace.js) ==");
        if (!HaveNode()) { Console.WriteLine("ALL OK (skipped)"); return 0; }
        try
        {
            foreach (var seed in new[] { 1, 2, 3 }) Game(seed, "liubei", 480, "classic");
            foreach (var seed in new[] { 1, 3, 1001 }) Game(seed, "roma", 300, "world");   // 种子 3 在 float 取整未模拟时于第 151 个月分岔
            Battles();
        }
        catch (Exception e) { fails++; Console.WriteLine("trace FAILED: " + e); }
        Scenarios.Current = Scenarios.Classic;
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
}
