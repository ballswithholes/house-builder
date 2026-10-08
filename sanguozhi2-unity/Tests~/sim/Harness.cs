// tests/sim.js (1)(1b)(1c) 的 C# 版：
//   (1) 全电脑模拟：6 个种子 × 40 年，每月检查一致性；
//   (1b) 40 场电脑对电脑的战术战斗（会施展必杀技，每次施展后检查状态）；
//   (1c) 必杀技平衡：同一批种子与城池组合，关闭 / 开启必杀技各打 200 场，统计胜率、天数、必杀技与普通攻击的伤害比。
// 输出格式与 JS 相同，便于逐行对照（JS 与 C# 用同一随机序列，数值多数逐字相同；浮点细节不同时允许小幅偏差）。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;

class P
{
    static int Main()
    {
        BattleModel.specialsEnabled = true;   // 同网页版默认（游戏内暂时默认 false，见 BattleModel.specialsEnabled）
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        int fails = 0;
        var t0 = DateTime.Now;
        // =========================================================== (1) 全电脑模拟 --
        Console.WriteLine("== (1) all-AI simulation: 6 seeds x 40 years ==");
        var alive10 = new List<int>();
        for (int seed = 1; seed <= 6; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var g = GameState.Current = GameState.NewGame("liubei");
            g.player = -1; // 让所有势力由电脑控制（RunAI 跳过 player）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int month = 0; string line = "";
            try
            {
                for (; month < 12 * 40; month++)
                {
                    var pb = StrategyAI.RunAI(new List<string>());
                    if (pb.Count > 0) throw new Exception("player battles with no player");
                    StrategyAI.EndMonth();
                    Sim.Consistency(g);
                    int alive = g.factions.Count(f => f.alive);
                    if (month == 119) { line += "[10y alive " + alive + "] "; alive10.Add(alive); }
                    if (alive <= 1) break;
                }
                var top = g.factions.Where(f => f.alive).Select(f => new { f, n = g.CityCount(f.id) }).OrderByDescending(o => o.n).Take(5).Select(o => o.f.name + ":" + o.n);
                Console.WriteLine($"{line}seed {seed}: {month} months, alive {g.factions.Count(f => f.alive)}, neutral {g.cities.Count(c => c.owner < 0)}, top [{string.Join(", ", top)}]  {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception e) { fails++; Console.WriteLine($"seed {seed} FAILED at month {month}: {e}"); }
        }
        if (alive10.Count > 0)
            Console.WriteLine($"factions alive at year 10: [{string.Join(", ", alive10)}]  (min {alive10.Min()}, max {alive10.Max()}, avg {Sim.F(alive10.Average(), 1)}; Unity tuning gave roughly 4-7)");

        // ============================================== (1b) 电脑对电脑的战术战斗 --
        Console.WriteLine("== (1b) 40 AI-vs-AI tactical battles (with specials) ==");
        int bw = 0, bl = 0, bdays = 0, bsp = 0;
        var reasons = new Dictionary<string, int>(); var reasonOrder = new List<string>();
        for (int seed = 1; seed <= 40; seed++)
        {
            var pr = Sim.Pairs[seed % Sim.Pairs.Length];
            try
            {
                var st = new Sim.Stats();
                var run = Sim.RunBattle(seed, pr, st);
                var M = run.M; var s = run.s; var g = run.g;
                bdays += M.day; if (M.result == 1) bw++; else bl++;
                bsp += st.specials;
                if (!reasons.ContainsKey(M.resultReason)) { reasons[M.resultReason] = 0; reasonOrder.Add(M.resultReason); }
                reasons[M.resultReason]++;
                s.attackerWon = M.result == 1;
                Conquest.Apply(s);
                if (s.captives.Count > 0) Conquest.AiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
                Sim.Consistency(g);
                if (seed <= 5) Console.WriteLine($"battle {pr[0]}->{pr[1]}: result {M.result} day {M.day} reason {M.resultReason} routed {s.routed.Count} captives {s.captives.Count} specials {st.specials}");
            }
            catch (Exception e) { fails++; Console.WriteLine("battle seed " + seed + " FAILED: " + e); }
        }
        Console.WriteLine($"tactical battles: attacker won {bw}, defender won {bl}, avg days {Sim.F(bdays / (double)Math.Max(1, bw + bl), 1)}, specials used {bsp} ({Sim.F(bsp / 40.0, 1)} per battle)");
        Console.WriteLine("  end reasons: " + string.Join("  ", reasonOrder.Select(k => k + " ×" + reasons[k])));

        // ==================================================== (1c) 必杀技平衡 --
        Console.WriteLine("== (1c) specials balance: 200 battles with specials off vs on ==");
        try
        {
            var pairs2 = Sim.Pairs.Concat(new[] { new[] { "xiapi", "xiaopei" }, new[] { "changan", "tianshui" }, new[] { "chengdu", "jiangzhou" }, new[] { "ye", "jinyang" }, new[] { "shouchun", "lujiang" } }).ToArray();
            var w = new int[2]; var n = new int[2]; var days = new int[2]; var timeout = new int[2]; var sts = new[] { new Sim.Stats(), new Sim.Stats() };
            for (int on = 0; on <= 1; on++)
            {
                BattleModel.specialsEnabled = on == 1;
                for (int seed = 1; seed <= 200; seed++)
                {
                    var pr = pairs2[seed % pairs2.Length];
                    var M = Sim.RunBattle(1000 + seed, pr, sts[on]).M;
                    n[on]++; days[on] += M.day; if (M.result == 1) w[on]++;
                    if (M.resultReason.IndexOf("期限") >= 0) timeout[on]++;
                }
            }
            BattleModel.specialsEnabled = true;
            for (int on = 0; on <= 1; on++)
                Console.WriteLine($"  specials {(on == 1 ? "ON " : "OFF")}: attacker win {Sim.F(100.0 * w[on] / n[on], 1)}% ({w[on]}/{n[on]}), avg days {Sim.F(days[on] / (double)n[on], 1)}, timeouts {timeout[on]}, normal attack avg dmg {Sim.F(Sim.Avg(sts[on].atkDmg), 0)}");
            var o = sts[1];
            double ratio = Sim.Median(o.ratio), ratio1 = Sim.Median(o.ratio1);
            Console.WriteLine($"  specials per battle {Sim.F(o.specials / (double)n[1], 2)}; damage specials avg {Sim.F(Sim.Avg(o.spDmg), 0)} vs normal attack avg {Sim.F(Sim.Avg(o.atkDmg), 0)}");
            Console.WriteLine($"  same unit, same moment: special damage = {Sim.F(ratio, 2)}x its own normal action (median over {o.ratio.Count} uses; per unit hit {Sim.F(ratio1, 2)}x)");
            Console.WriteLine("    (normal action = one normal attack on the same target for war-based specials, best of attack / fire tactic for intel-based; target 1.8-2.5; specials take no counter-attack)");
            Console.WriteLine("    by kind (median): " + string.Join(", ", o.byKind.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k + " " + Sim.F(Sim.Median(o.byKind[k]), 2))));
            Console.WriteLine("  kinds used: " + string.Join(" ", o.kindOrder.OrderByDescending(k => o.kinds[k]).Select(k => k + "×" + o.kinds[k])));
            if (o.assassin.Count > 0) Console.WriteLine($"  assassinations: {o.assassin.Count(x => x > 0)}/{o.assassin.Count} succeeded");
            double dw = Math.Abs(w[1] - w[0]) / (double)n[1];
            if (dw > 0.15) throw new Exception("attacker win rate shifted by " + Sim.F(dw * 100, 1) + " points with specials");
            if (o.specials < n[1] * 0.5) throw new Exception("AI almost never uses specials: " + o.specials);
            if (!(ratio >= 1.6 && ratio <= 2.6)) throw new Exception("special/attack damage ratio out of range: " + Sim.F(ratio, 2));
            if (!(ratio1 >= 1.0 && ratio1 <= 2.4)) throw new Exception("special/attack per-unit ratio out of range: " + Sim.F(ratio1, 2));
            // 各机制的实战中位数（样本 ≥ 25 才判定）
            foreach (var k in o.byKind.Keys)
                if (o.byKind[k].Count >= 25) { double m = Sim.Median(o.byKind[k]); if (!(m >= 1.3 && m <= 3.0)) throw new Exception("in-battle median for " + k + " out of range: " + Sim.F(m, 2)); }
            if (timeout[1] > timeout[0] + 30) throw new Exception("specials made many more battles time out");
        }
        catch (Exception e) { BattleModel.specialsEnabled = true; fails++; Console.WriteLine("specials balance FAILED: " + e); }

        Console.WriteLine($"total {Sim.F((DateTime.Now - t0).TotalSeconds, 1)}s");
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
}
