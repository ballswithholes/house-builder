// 单挑格斗（第二版 §4E）人机强度回归 —— 网页版 tests/duel-bots.js 的逐条移植（同样的脚本玩家、同样的组合、同样的统计）。
// 随机数：DuelSim.Rnd = UnityStub 的 Random.NextDouble（与 SG.Random.seed(s) 相同的 mulberry32），
// 外观的文化 / 性别查询置空（node 测试只载入 core.js、data.js、duel-game.js，没有 SG.WorldData），
// 因此同一种子下每一场都与 JS 逐步一致，胜率应与 node 的输出完全相同。
// 用法：mono HarnessDuel.exe [每组场数，默认 40] [种子，默认 7]      退出码 = 失败数
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;

public static class HarnessDuel
{
    static readonly Dictionary<string, General> gens = new Dictionary<string, General>();
    static int fails, checks;
    static void Ok(bool cond, string msg) { checks++; if (!cond) { fails++; Console.WriteLine("  FAIL: " + msg); } }
    static string F0(double v) { return Math.Floor(v + 0.5).ToString(System.Globalization.CultureInfo.InvariantCulture); }   // toFixed(0)（非负）

    // 与 tests/duel.html 的 botCtl 相同的两个脚本玩家
    sealed class Bot : IDuelCtrl
    {
        readonly string kind; double nt;
        readonly DuelHeld held = new DuelHeld(); readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }
        public Bot(string kind) { this.kind = kind; }
        public void Update(double dt, DuelSim sim)
        {
            var me = sim.f[0]; var o = sim.f[1]; double d = Math.Abs(o.x - me.x);
            if (kind == "jumper")
            {
                held.x = me.face;
                if (sim.clock < nt) return;
                if (sim.Actionable(me) && d < 3.2 && d > 1.2) { taps.up++; nt = sim.clock + 0.1; }
                else if (me.state == "jump" && d < me.reach + 0.3) { taps.light++; nt = sim.clock + 0.1; }
                else if (me.state != "jump" && d < me.reach) { taps.light++; nt = sim.clock + 0.1; }
            }
            else
            {
                held.x = d > me.reach * 0.9 ? me.face : 0;
                if (sim.clock > nt && d < me.reach * 1.1) { taps.light++; nt = sim.clock + 0.12; }
                if (me.rage >= 100) taps.special++;
            }
        }
    }
    static double Rate(string kind, string p, string a, int n)
    {
        var P = gens[p]; var A = gens[a];
        int w = 0;
        for (int i = 0; i < n; i++)
        {
            var sim = new DuelSim(P, A, DuelLooks.LookOf(P), DuelLooks.LookOf(A));
            sim.f[0].ctrl = new Bot(kind); sim.f[1].ctrl = new DuelAI(sim.f[1]);
            sim.RunToEnd();
            if (sim.Outcome().winner == 0) w++;
        }
        return 100.0 * w / n;
    }
    static void Section(string name, Action fn)
    {
        Console.WriteLine("== " + name);
        try { fn(); } catch (Exception e) { fails++; Console.WriteLine("  FAILED: " + e); }
    }

    public static int Main(string[] args)
    {
        int N = args.Length > 0 && int.Parse(args[0]) > 0 ? int.Parse(args[0]) : 40;
        int seed = args.Length > 1 && int.Parse(args[1]) != 0 ? int.Parse(args[1]) : 7;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        UnityEngine.Random.InitState(seed);
        DuelSim.Rnd = UnityEngine.Random.NextDouble;
        DuelLooks.CultureLookup = null; DuelLooks.SexLookup = null;
        foreach (var s in ScenarioData.Generals)
        {
            var p = s.Split('|');
            gens[p[0]] = new General { name = p[0], war = int.Parse(p[1]), intel = int.Parse(p[2]), pol = int.Parse(p[3]) };
        }

        Section("(1) botTally regression (same cases as tests/duel.html)", () =>
        {
            var cases = new object[][] {
                new object[] { "jumper", "孙乾", "吕布", 0, 10 }, new object[] { "jumper", "刘备", "吕布", 0, 40 }, new object[] { "jumper", "关羽", "关羽", 0, 75 },
                new object[] { "masher", "孙乾", "吕布", 0, 10 }, new object[] { "masher", "刘备", "吕布", 0, 40 }, new object[] { "masher", "关羽", "关羽", 5, 75 }, new object[] { "masher", "张飞", "刘备", 60, 100 },
            };
            foreach (var c in cases)
            {
                string kind = (string)c[0], p = (string)c[1], a = (string)c[2]; int lo = (int)c[3], hi = (int)c[4];
                double pct = Rate(kind, p, a, N);
                Console.WriteLine("  " + kind + " " + p + "(" + gens[p].war + ") vs AI " + a + "(" + gens[a].war + "): " + F0(pct) + "% (want " + lo + "-" + hi + ")");
                Ok(pct >= lo && pct <= hi, kind + " " + p + " vs " + a + ": " + F0(pct) + "% outside " + lo + "-" + hi);
            }
        });

        // 同武力的若干组合（含长枪对短兵、双剑、大斧、骨朵等兵器差异）
        var MID = new[] { new[] { "刘备", "刘备" }, new[] { "曹操", "曹操" }, new[] { "王平", "王平" }, new[] { "郭汜", "郭汜" }, new[] { "李傕", "刘备" }, new[] { "刘备", "王平" }, new[] { "霍峻", "刘备" },
            new[] { "张济", "曹操" }, new[] { "祖茂", "黄祖" }, new[] { "郭汜", "潘凤" }, new[] { "潘凤", "孟优" }, new[] { "吴懿", "孟优" }, new[] { "孟达", "潘凤" }, new[] { "孟优", "吴懿" } };
        Section("(2) masher vs equal-war AI", () =>
        {
            var rows = MID.Select(pa => new { p = pa[0], a = pa[1], r = Rate("masher", pa[0], pa[1], N) }).ToList();
            double avg = rows.Sum(r => r.r) / rows.Count, max = rows.Max(r => r.r);
            Console.WriteLine("  war 72-76: " + string.Join("  ", rows.Select(r => r.p + ">" + r.a + " " + F0(r.r))));
            Console.WriteLine("  war 72-76 average " + F0(avg) + "%, worst pair " + F0(max) + "%");
            Ok(avg <= 45, "masher average vs war 72-76 AI is " + F0(avg) + "% (want <= 45%)");
            Ok(max <= 65, "masher worst pair vs war 72-76 AI is " + F0(max) + "% (want <= 65%)");
            Ok(avg >= 5, "war 72-76 AI should still be beatable by mashing sometimes (got " + F0(avg) + "%)");

            var weak = new[] { new[] { "孙乾", "孙乾" }, new[] { "简雍", "孙乾" }, new[] { "沮授", "陶谦" }, new[] { "陶谦", "沮授" } }.Select(pa => Rate("masher", pa[0], pa[1], N)).ToList();
            double wAvg = weak.Sum() / weak.Count;
            Console.WriteLine("  war <= 40: " + string.Join(" ", weak.Select(F0)) + "  average " + F0(wAvg) + "%");
            Ok(wAvg >= 80, "weak generals (war <= 40) should stay easy to mash (got " + F0(wAvg) + "%)");

            var strong = new[] { new[] { "关羽", "关羽" }, new[] { "张飞", "赵云" }, new[] { "华雄", "张郃" }, new[] { "吕布", "吕布" } }.Select(pa => Rate("masher", pa[0], pa[1], N)).ToList();
            double sAvg = strong.Sum() / strong.Count;
            Console.WriteLine("  war >= 90: " + string.Join(" ", strong.Select(F0)) + "  average " + F0(sAvg) + "%");
            Ok(sAvg <= 30 && sAvg >= 3, "strong generals (war >= 90) should be hard but not impossible to mash (got " + F0(sAvg) + "%)");
            Ok(sAvg < avg + 5, "strong generals should not be easier to mash than average ones (" + F0(sAvg) + "% vs " + F0(avg) + "%)");
        });

        Section("(3) AI vs AI: war decides, mirror is even", () =>
        {
            Func<string, string, double> sim = (a, b) => { int w = 0; for (int i = 0; i < N; i++) if (DuelSim.Simulate(gens[a], gens[b]).winner == 0) w++; return 100.0 * w / N; };
            double big = sim("吕布", "孙乾"), gap = sim("典韦", "李典"), mirror = sim("关羽", "关羽");
            Console.WriteLine("  吕布 vs 孙乾 " + F0(big) + "%, 典韦 vs 李典 " + F0(gap) + "%, 关羽 mirror " + F0(mirror) + "%");
            Ok(big >= 95, "吕布 should crush 孙乾");
            Ok(gap >= 75, "典韦(96) should usually beat 李典(78)");
            Ok(mirror >= 25 && mirror <= 75, "mirror match should be roughly even");
        });

        Console.WriteLine(checks + " checks");
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
}
