// 单挑格斗（第二版 §4E，第三版格开 §3）人机强度与格开规则回归 —— 网页版 tests/duel-bots.js 的逐条移植
// （同样的脚本玩家、同样的组合、同样的统计、同样的输出格式；(1)–(7) 全部各节，含 refine 新增的 turtle / cautious / turtleP / backstepper）。
// 随机数：DuelSim.Rnd = UnityStub 的 Random.NextDouble（与 SG.Random.seed(s) 相同的 mulberry32），整个文件按 JS 的顺序消耗；
// 外观的文化 / 性别查询置空（node 测试只载入 core.js、data.js、duel-game.js，没有 SG.WorldData），
// 因此同一种子下每一场都与 JS 逐步一致，输出应与 node 的输出逐行相同（Tests~/duel/run.sh 逐行对照）。
// 用法：mono HarnessDuel.exe [每组场数，默认 40] [种子，默认 7]      退出码 = 失败数
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;

public static class HarnessDuel
{
    static readonly Dictionary<string, General> gens = new Dictionary<string, General>();
    static readonly List<General> gensOrder = new List<General>();   // = Object.values(gens) 的顺序
    static int fails, checks, N;
    static void Ok(bool cond, string msg) { checks++; if (!cond) { fails++; Console.WriteLine("  FAIL: " + msg); } }
    static void Section(string name, Action fn)
    {
        Console.WriteLine("== " + name);
        try { fn(); } catch (Exception e) { fails++; Console.WriteLine("  FAILED: " + e); }
    }
    static bool Near(double a, double b, double eps) { return Math.Abs(a - b) <= eps; }
    static string F(double v, int n) { return Specials.JsToFixed(v, n); }   // = JS toFixed
    static double R() { return DuelSim.Rnd(); }   // = SG.Random.value()
    static double Gz() { return (R() + R() + R() + R() - 2) * 1.732; }
    static readonly DuelMove MV1 = DuelSim.Light1, MV2 = DuelSim.Light2, MV3 = DuelSim.Light3, MVH = DuelSim.Heavy;
    static string War(string p) { return gens[p].war.ToString(); }

    // ------------------------------------------------------------ BOTS（本文件另加的脚本玩家）--
    //   turtle 龟缩：电脑在兵器距离 +1.2 米内就一直按住格挡，只抓看得见的破绽（收招 / 落空 / 被格开；蓄力够得着也打），
    //          在跟前蓄力的一半会被他上前打断；隔 1.5–2.5 秒才试探一下；从不格开——量电脑会不会用满蓄重击破防（在他够不着处蓄满）
    //   cautious 谨慎：同 guard，但知道「被挡之后轮到对方」：自己的试探被挡就继续举防约 0.3–0.5 秒，
    //          一半时候还按人的节奏（σ 0.045 秒）格开电脑抢回合的那一刀
    //   turtleP 龟缩 + 格开：同 turtle，另看见电脑松开蓄到六成以上的重击就按人的反应（0.22 ± 0.04 秒）格开
    //   backstepper 后撤：同 guard，自己的试探被挡就立刻后撤步，再抓电脑挥空的收招（看见 0.2 秒后冲刺斩）
    sealed class Turtle : IDuelCtrl
    {
        readonly int idx; readonly DuelHeld held = new DuelHeld(); readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }
        double nt = -1; int cSeq = -1; bool cHit;
        public Turtle(int idx) { this.idx = idx; }
        public void Update(double dt, DuelSim sim)
        {
            var me = sim.f[idx]; var o = sim.f[1 - idx]; double d = Math.Abs(o.x - me.x), now = sim.clock; var H = held; var T = taps;
            double reach = me.reach * MV1.reachK + 0.2 - 0.03; bool free = sim.Actionable(me) || me.state == "guard";
            H.x = 0; H.heavy = false;
            if (nt < 0) nt = now + 1 + R();
            if (me.state == "parryok" || sim.Countering(me)) { H.guard = false; if (d <= reach + 0.05) T.light++; else H.x = me.face; return; }
            if ((me.state == "light1" || me.state == "light2") && me.hitIdx > 0 && o.state == "hitstun") { T.light++; return; }
            var seen = sim.Seen(o.idx, 0.2);
            if (seen != null && seen.state == "charge" && seen.seq != cSeq) { cSeq = seen.seq; cHit = R() < 0.5; }   // 看见对手在跟前蓄力：一半时候上前打断
            bool open = seen != null && o.seq == seen.seq && (seen.phase == "recovery" || seen.state == "parrywhiff" || seen.state == "parried" || (seen.state == "charge" && cHit));
            if (open && d <= reach + 0.1 && free) { H.guard = false; T.light++; return; }
            if (me.rage >= 100 && d < reach + 1 && sim.Actionable(me)) { H.guard = false; T.special++; return; }
            if (d > o.reach + 1.2) { H.guard = false; H.x = me.face; return; }
            if (now > nt && d <= reach * 0.95 && free && !(seen != null && seen.atk)) { nt = now + 1.5 + R(); H.guard = false; T.light++; return; }
            H.guard = true;
        }
    }
    sealed class Cautious : IDuelCtrl
    {
        readonly int idx; readonly DuelScriptBot bs;
        public DuelHeld Held { get { return bs.Held; } }
        public DuelTaps Taps { get { return bs.Taps; } }
        double holdUntil, pAt = -1; int gSeq = -1;
        public Cautious(int idx) { this.idx = idx; bs = new DuelScriptBot("guard", idx); }
        public void Update(double dt, DuelSim sim)
        {
            var me = sim.f[idx]; var o = sim.f[1 - idx]; double now = sim.clock; var H = Held; var T = Taps;
            // 自己的出手被挡（对手进入防御硬直）：料定对手硬直一结束就还手
            if (o.state == "guardstun" && o.seq != gSeq && DuelSim.IsAttack(me.state) && me.hitIdx > 0)
            {
                gSeq = o.seq;
                double freeAt = now + Math.Max(0, o.stun - o.t);
                holdUntil = freeAt + 0.3 + 0.2 * R();
                if (sim.ParryReady(me) && R() < 0.5) pAt = freeAt + MV1.startup / o.spd - me.parryW * 0.5 + 0.045 * Gz();
            }
            bs.Update(dt, sim);
            if (me.state == "parryok" || sim.Countering(me)) { pAt = -1; holdUntil = 0; return; }
            if (pAt >= 0 && now >= pAt && (sim.Actionable(me) || me.state == "guard" || me.state == "guardstun")) { T.parry++; pAt = -1; }
            if (now < holdUntil && (sim.Actionable(me) || me.state == "guard")) { H.guard = true; H.x = 0; T.light = 0; T.heavy = 0; T.special = 0; }
        }
    }
    sealed class TurtleP : IDuelCtrl
    {
        readonly int idx; readonly Turtle bs;
        public DuelHeld Held { get { return bs.Held; } }
        public DuelTaps Taps { get { return bs.Taps; } }
        int hvSeq = -1; double pAt = -1;
        public TurtleP(int idx) { this.idx = idx; bs = new Turtle(idx); }
        public void Update(double dt, DuelSim sim)
        {
            var me = sim.f[idx]; var o = sim.f[1 - idx]; double now = sim.clock;
            bs.Update(dt, sim);
            if (o.state == "heavy" && o.seq != hvSeq)
            {
                hvSeq = o.seq;
                if (o.charge >= 0.6 && sim.ParryReady(me)) pAt = now - o.t / o.spd + Math.Max(0.12, 0.22 + 0.04 * Gz());
            }
            if (pAt >= 0 && now >= pAt) { Taps.parry++; pAt = -1; }
            if (me.state == "parryok" || sim.Countering(me)) pAt = -1;
        }
    }
    sealed class Backstepper : IDuelCtrl
    {
        readonly int idx; readonly DuelScriptBot bs;
        public DuelHeld Held { get { return bs.Held; } }
        public DuelTaps Taps { get { return bs.Taps; } }
        int gSeq = -1, pun = -1; bool bsPending;
        public Backstepper(int idx) { this.idx = idx; bs = new DuelScriptBot("guard", idx); }
        public void Update(double dt, DuelSim sim)
        {
            var me = sim.f[idx]; var o = sim.f[1 - idx]; var H = Held; var T = Taps; double d = Math.Abs(o.x - me.x);
            if (o.state == "guardstun" && o.seq != gSeq && DuelSim.IsAttack(me.state) && me.hitIdx > 0) { gSeq = o.seq; bsPending = true; }
            bs.Update(dt, sim);
            H.dash = 0;
            if (bsPending)
            {
                if (sim.Actionable(me) || me.state == "guard") { H.guard = false; H.dash = -me.face; T.light = 0; bsPending = false; }
                else T.light = 0;
                return;
            }
            // 电脑挥空的收招（看见 0.2 秒后）：近则出手，稍远冲刺斩
            var seen = sim.Seen(o.idx, 0.2); double reach = me.reach * MV1.reachK + 0.2 - 0.03;
            if (seen != null && o.seq == seen.seq && DuelSim.IsAttack(o.state) && seen.phase == "recovery" && o.hitIdx == 0 && o.seq != pun && (sim.Actionable(me) || me.state == "guard" || me.state == "backstep") && d < reach + 1.2)
            {
                pun = o.seq; H.guard = false; if (d > reach) H.dash = me.face; T.light++;
            }
        }
    }
    static IDuelCtrl Bot(string kind, int idx)
    {
        switch (kind)
        {
            case "turtle": return new Turtle(idx);
            case "cautious": return new Cautious(idx);
            case "turtleP": return new TurtleP(idx);
            case "backstepper": return new Backstepper(idx);
        }
        return new DuelScriptBot(kind, idx);
    }

    // 脚本玩家（左，f[0]）对 真实电脑（右）；返回玩家胜率 % 与格开统计
    // by：格开按「谁 途径/招式」分类计数（如 'P hitstun/light3'、'AI stance/heavy'）；heavies：双方松手的重击数（满蓄 / 全部）
    sealed class PlayRes { public double pct, parries, tries, aiParries; public SortedDictionary<string, int> by = new SortedDictionary<string, int>(StringComparer.Ordinal); public int hP, hPFull, hAI, hAIFull; }
    static PlayRes Play(string kind, string p, string a, int n)
    {
        var P = gens[p]; var A = gens[a];
        int w = 0; double parries = 0, tries = 0, aiParries = 0;
        var res = new PlayRes();
        for (int i = 0; i < n; i++)
        {
            var sim = new DuelSim(P, A, DuelLooks.LookOf(P), DuelLooks.LookOf(A));
            sim.f[0].ctrl = kind == "ai" ? (IDuelCtrl)new DuelAI(sim.f[0]) : Bot(kind, 0); sim.f[1].ctrl = new DuelAI(sim.f[1]);
            sim.running = true;
            for (int k = 0; k < 60 * 75 && !sim.hasResult; k++)
            {
                sim.events.Clear(); sim.Step();
                foreach (var e in sim.events)
                {
                    if (e.type == "parry") { string key = (e.def == 0 ? "P " : "AI ") + e.route + "/" + e.move; res.by[key] = (res.by.ContainsKey(key) ? res.by[key] : 0) + 1; }
                    else if (e.type == "heavyGo") { if (e.who == 0) { res.hP++; if (e.charge >= 0.85) res.hPFull++; } else { res.hAI++; if (e.charge >= 0.85) res.hAIFull++; } }
                }
            }
            if (!sim.hasResult) sim.TimeUp();
            var o = sim.Outcome();
            if (o.winner == 0) w++;
            parries += o.stats[0].parries; tries += o.stats[0].parryTries; aiParries += o.stats[1].parries;
        }
        res.pct = 100.0 * w / n; res.parries = parries / n; res.tries = tries / n; res.aiParries = aiParries / n;
        return res;
    }
    static double Rate(string kind, string p, string a, int n) { return Play(kind, p, a, n).pct; }

    // ------------------------------------------------------------ (4) 用的手动操作者与场景 --
    sealed class Manual : IDuelCtrl
    {
        readonly DuelHeld held = new DuelHeld(); readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }
        public void Update(double dt, DuelSim sim) { }
    }
    static Manual Ctl(DuelFighter f) { return (Manual)f.ctrl; }
    // 场景：左 f[0]、右 f[1] 相距 dist，双方手动操作
    static DuelSim Stage(string a, string b, double dist)
    {
        var A = gens[a]; var B = gens[b];
        var sim = new DuelSim(A, B, DuelLooks.LookOf(A), DuelLooks.LookOf(B));
        sim.f[0].x = -dist / 2; sim.f[1].x = dist / 2;
        sim.f[0].ctrl = new Manual(); sim.f[1].ctrl = new Manual();
        sim.running = true;
        sim.Step(); sim.events.Clear();
        return sim;
    }
    static readonly DuelEvent TrueEv = new DuelEvent { type = "<true>" };   // = JS 的 true（pred(sim, null) 为真）
    // 推进到 pred(sim, ev) 为真（ev 为本步的事件），最多 sec 秒；返回触发的事件或 TrueEv / null
    static DuelEvent Until(DuelSim sim, Func<DuelSim, DuelEvent, bool> pred, double sec)
    {
        int steps = (int)Math.Floor(sec * 60 + 0.5);
        for (int i = 0; i < steps; i++)
        {
            sim.events.Clear();
            sim.Step();
            foreach (var e in sim.events.ToList()) if (pred(sim, e)) return e;
            if (pred(sim, null)) return TrueEv;
        }
        return null;
    }
    static DuelEvent Run(DuelSim sim, double sec) { return Until(sim, (s, e) => false, sec); }

    static string Json(int parried, int hit, int guarded, int gbreak, int avoid) { return "{\"parried\":" + parried + ",\"hit\":" + hit + ",\"guarded\":" + guarded + ",\"gbreak\":" + gbreak + ",\"avoid\":" + avoid + "}"; }

    public static int Main(string[] args)
    {
        int a0, s0;
        N = args.Length > 0 && int.TryParse(args[0], out a0) && a0 > 0 ? a0 : 40;
        int seed = args.Length > 1 && int.TryParse(args[1], out s0) && s0 != 0 ? s0 : 7;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        UnityEngine.Random.InitState(seed);
        DuelSim.Rnd = UnityEngine.Random.NextDouble;
        DuelLooks.CultureLookup = null; DuelLooks.SexLookup = null;
        foreach (var s in ScenarioData.Generals)
        {
            var p = s.Split('|');
            var g = new General { name = p[0], war = int.Parse(p[1]), intel = int.Parse(p[2]), pol = int.Parse(p[3]) };
            if (!gens.ContainsKey(p[0])) gensOrder.Add(g); else gensOrder[gensOrder.IndexOf(gens[p[0]])] = g;
            gens[p[0]] = g;
        }

        Section("(1) botTally regression (same cases as tests/duel.html)", () =>
        {
            var cases = new object[][] {
                new object[] { "jumper", "孙乾", "吕布", 0, 10 }, new object[] { "jumper", "刘备", "吕布", 0, 40 }, new object[] { "jumper", "关羽", "关羽", 0, 75 },
                new object[] { "masher", "孙乾", "吕布", 0, 10 }, new object[] { "masher", "刘备", "吕布", 0, 40 }, new object[] { "masher", "关羽", "关羽", 0, 75 }, new object[] { "masher", "张飞", "刘备", 50, 100 },
            };
            foreach (var c in cases)
            {
                string kind = (string)c[0], p = (string)c[1], a = (string)c[2]; int lo = (int)c[3], hi = (int)c[4];
                double pct = Rate(kind, p, a, N);
                Console.WriteLine("  " + kind + " " + p + "(" + War(p) + ") vs AI " + a + "(" + War(a) + "): " + F(pct, 0) + "% (want " + lo + "-" + hi + ")");
                Ok(pct >= lo && pct <= hi, kind + " " + p + " vs " + a + ": " + F(pct, 0) + "% outside " + lo + "-" + hi);
            }
        });

        // 同武力的若干组合（含长枪对短兵、双剑、大斧、骨朵等兵器差异）
        var MID = new[] { new[] { "刘备", "刘备" }, new[] { "曹操", "曹操" }, new[] { "王平", "王平" }, new[] { "郭汜", "郭汜" }, new[] { "李傕", "刘备" }, new[] { "刘备", "王平" }, new[] { "霍峻", "刘备" },
            new[] { "张济", "曹操" }, new[] { "祖茂", "黄祖" }, new[] { "郭汜", "潘凤" }, new[] { "潘凤", "孟优" }, new[] { "吴懿", "孟优" }, new[] { "孟达", "潘凤" }, new[] { "孟优", "吴懿" } };
        Section("(2) masher vs equal-war AI", () =>
        {
            var rows = MID.Select(pa => new { p = pa[0], a = pa[1], r = Rate("masher", pa[0], pa[1], N) }).ToList();
            double avg = rows.Sum(r => r.r) / rows.Count, max = rows.Max(r => r.r);
            Console.WriteLine("  war 72-76: " + string.Join("  ", rows.Select(r => r.p + ">" + r.a + " " + F(r.r, 0))));
            Console.WriteLine("  war 72-76 average " + F(avg, 0) + "%, worst pair " + F(max, 0) + "%");
            Ok(avg <= 30, "masher average vs war 72-76 AI is " + F(avg, 0) + "% (want <= 30%; v3 before the AI refine: about 30-35%)");
            Ok(max <= 65, "masher worst pair vs war 72-76 AI is " + F(max, 0) + "% (want <= 65%)");
            Ok(avg >= 5, "war 72-76 AI should still be beatable by mashing sometimes (got " + F(avg, 0) + "%)");

            var weak = new[] { new[] { "孙乾", "孙乾" }, new[] { "简雍", "孙乾" }, new[] { "沮授", "陶谦" }, new[] { "陶谦", "沮授" } }.Select(pa => Rate("masher", pa[0], pa[1], N)).ToList();
            double wAvg = weak.Sum() / weak.Count;
            Console.WriteLine("  war <= 40: " + string.Join(" ", weak.Select(x => F(x, 0))) + "  average " + F(wAvg, 0) + "%");
            Ok(wAvg >= 80, "weak generals (war <= 40) should stay easy to mash (got " + F(wAvg, 0) + "%)");

            // 强将：每组至少 75 场（合计 ≥ 300 场），「并非无法取胜」才不至于碰运气
            int nS = Math.Max(N, 75);
            var strong = new[] { new[] { "关羽", "关羽" }, new[] { "张飞", "赵云" }, new[] { "华雄", "张郃" }, new[] { "吕布", "吕布" } }.Select(pa => Rate("masher", pa[0], pa[1], nS)).ToList();
            double sAvg = strong.Sum() / strong.Count;
            Console.WriteLine("  war >= 90 (" + nS + " each): " + string.Join(" ", strong.Select(x => F(x, 1))) + "  average " + F(sAvg, 1) + "%");
            Ok(sAvg <= 30 && sAvg >= 1, "strong generals (war >= 90) should be hard but not impossible to mash (got " + F(sAvg, 1) + "%)");
            Ok(sAvg < avg + 5, "strong generals should not be easier to mash than average ones (" + F(sAvg, 0) + "% vs " + F(avg, 0) + "%)");
        });

        Section("(3) AI vs AI: war decides, mirror is even; strong AI parries, weak AI does not", () =>
        {
            int n = N;
            Func<string, string, double> sim = (a, b) => { int w = 0; for (int i = 0; i < n; i++) if (DuelSim.Simulate(gens[a], gens[b]).winner == 0) w++; return 100.0 * w / n; };
            double big = sim("吕布", "孙乾"), gap = sim("典韦", "李典"), mirror = sim("关羽", "关羽");
            Console.WriteLine("  吕布 vs 孙乾 " + F(big, 0) + "%, 典韦 vs 李典 " + F(gap, 0) + "%, 关羽 mirror " + F(mirror, 0) + "%");
            Ok(big >= 95, "吕布 should crush 孙乾");
            Ok(gap >= 75, "典韦(96) should usually beat 李典(78)");
            Ok(mirror >= 25 && mirror <= 75, "mirror match should be roughly even");
            // 电脑对电脑（无画面模拟 / 观战）不该耗到时间：同将对打平均时长与超时比例
            Func<string, string, int, double[]> dur = (a, b, m) => { double t = 0, to = 0; for (int i = 0; i < m; i++) { var r = DuelSim.Simulate(gens[a], gens[b]); t += r.time; if (r.kind == "time") to++; } return new[] { t / m, 100 * to / m }; };
            var dMid = dur("刘备", "刘备", Math.Max(40, n)); var dWeak = dur("孙乾", "孙乾", Math.Max(80, n));
            Console.WriteLine("  AI mirror duration: 刘备 " + F(dMid[0], 1) + "s (time-outs " + F(dMid[1], 0) + "%), 孙乾 " + F(dWeak[0], 1) + "s (time-outs " + F(dWeak[1], 0) + "%)");
            Ok(dMid[0] <= 42 && dMid[1] <= 10, "AI mid mirrors should not drag (刘备 mirror " + F(dMid[0], 1) + "s, " + F(dMid[1], 0) + "% time-outs; want <= 42s, <= 10%)");
            Ok(dWeak[1] <= 45, "weak AI mirrors should mostly end before the clock (孙乾 mirror " + F(dWeak[1], 0) + "% time-outs; want <= 45%)");
            var strongAI = Play("ai", "吕布", "吕布", Math.Max(10, n >> 1)); var weakAI = Play("ai", "孙乾", "孙乾", Math.Max(10, n >> 1));
            Console.WriteLine("  parries per match: 吕布 mirror " + F(strongAI.parries, 2) + " + " + F(strongAI.aiParries, 2) + ", 孙乾 mirror " + F(weakAI.parries, 2) + " (tries " + F(weakAI.tries, 2) + ")");
            Ok(strongAI.parries + strongAI.aiParries >= 0.4, "strong AIs should parry each other sometimes (got " + F(strongAI.parries + strongAI.aiParries, 2) + " per match)");
            Ok(weakAI.tries == 0, "war 30 AI should not parry (tried " + F(weakAI.tries, 2) + " per match)");
        });

        // ---------------------------------------------------------------- (4) 格开规则 --
        Section("(4) parry rules (scripted)", () =>
        {
            // 窗口随武力
            Console.WriteLine("  window: war 30 " + F(DuelSim.ParryWindow(30), 3) + "s, war 65 " + F(DuelSim.ParryWindow(65), 3) + "s, war 100 " + F(DuelSim.ParryWindow(100), 3) + "s; cooldown " + Specials.JsNum(DuelSim.PARRY_CD) + "s");
            Ok(Near(DuelSim.ParryWindow(30), 0.15, 1e-6) && Near(DuelSim.ParryWindow(100), 0.22, 1e-6) && DuelSim.ParryWindow(65) > 0.15 && DuelSim.ParryWindow(65) < 0.22, "parry window should scale 0.15 → 0.22 s with war");
            // 冷却明显长于普通攻击（轻击一个循环）与格挡（随按随放）
            double cyc = (MV1.startup + MV1.active + MV1.recovery) / 0.9;
            Ok(DuelSim.PARRY_CD >= 2 && DuelSim.PARRY_CD > cyc * 4, "parry cooldown " + DuelSim.PARRY_CD + "s should be clearly longer than a light attack cycle (" + F(cyc, 2) + "s)");

            // A. 成功格开：右方轻击，左方在起手中按 U → 对手 parried ≈0.6 秒，己方 parryok + 0.7 秒反击窗口
            {
                var sim = Stage("关羽", "关羽", 1.6); var L = sim.f[0]; var Rt = sim.f[1];
                Ctl(Rt).Taps.light++;
                Run(sim, 0.03);
                Ctl(L).Taps.parry++;
                var e = Until(sim, (s, ev) => ev != null && ev.type == "parry", 0.5);
                Ok(e != null && e.def == 0 && e.move == "light1", "a light attack landing inside the window should be parried");
                Ok(Rt.state == "parried" && Near(Rt.stun, DuelSim.PARRY_STAGGER, 1e-6), "attacker should be staggered " + DuelSim.PARRY_STAGGER + "s (state " + Rt.state + ", stun " + Rt.stun + ")");
                Ok(L.state == "parryok" && Near(L.counterUntil - sim.clock, DuelSim.COUNTER_WIN, 0.02), "parrier should get a " + DuelSim.COUNTER_WIN + "s counter window (state " + L.state + ")");
                Ok(Rt.hp == 100 && L.hp == 100, "a parry deals no damage");
                // 对手硬直结束后按住格挡，反击仍在窗口内 → 不可格挡、×1.5
                Ctl(Rt).Held.guard = true;
                double tParry = sim.clock;
                Until(sim, (s, ev) => s.clock >= tParry + 0.52, 1);
                Ok(Rt.state == "parried", "attacker is still staggered 0.52 s after the parry");
                Ctl(L).Taps.light++;
                var h = Until(sim, (s, ev) => ev != null && ev.type == "hit" && ev.att == 0, 0.4);
                Ok(h != null && h.riposte && !h.guarded, "the first hit inside the counter window should be an unblockable riposte (riposte " + (h != null && h.riposte) + ", guarded " + (h != null && h.guarded) + ", defender " + Rt.state + ")");
                double bse = MV1.dmg * DuelLooks.Weapons["guandao"].pow * DuelSim.COUNTER_K, exp = DuelSim.Damage(bse, 98, 98);
                Ok(h != null && h.dmg >= Math.Floor(exp * 0.92) && h.dmg <= Math.Ceiling(exp * 1.08), "riposte damage should be x" + DuelSim.COUNTER_K + " (got " + (h != null ? h.dmg.ToString() : "null") + ", expected about " + F(exp, 1) + ")");
                Ok(!sim.Countering(L), "the counter window is used up by the first hit");
                // 冷却中再按 U：不进入格开
                double cdLeft = sim.ParryCd(L);
                Ok(cdLeft > 1.5 && cdLeft < DuelSim.PARRY_CD, "parry should be on cooldown right after use (" + F(cdLeft, 2) + "s left)");
            }
            // B. 落空：没有来招 → 窗口过后 0.4 秒破绽；冷却 2.5 秒（期间再按无效），冷却好了才能再格开；挨打算「破绽」
            {
                var sim = Stage("刘备", "刘备", 3.5); var L = sim.f[0];
                Ctl(L).Taps.parry++;
                Run(sim, 0.02);
                Ok(L.state == "parry", "U should enter the parry stance (state " + L.state + ")");
                Until(sim, (s, ev) => L.state != "parry", 0.5);
                Ok(L.state == "parrywhiff", "an empty parry should whiff (state " + L.state + ")");
                double t0 = sim.clock;
                Until(sim, (s, ev) => L.state != "parrywhiff", 1);
                Ok(Near(sim.clock - t0, DuelSim.PARRY_WHIFF, 0.03), "whiff recovery should last " + DuelSim.PARRY_WHIFF + "s (got " + F(sim.clock - t0, 2) + ")");
                Run(sim, 0.4);
                Ctl(L).Taps.parry++;
                Run(sim, 0.05);
                Ok(L.state != "parry", "pressing U during the cooldown should do nothing");
                Until(sim, (s, ev) => s.ParryReady(L), 3);
                Ctl(L).Taps.parry++;
                Run(sim, 0.03);
                Ok(L.state == "parry", "after the cooldown U should parry again (state " + L.state + ")");
                // 落空破绽中挨打 = 破绽 ×1.2
                var sim2 = Stage("刘备", "刘备", 1.6); var L2 = sim2.f[0]; var R2 = sim2.f[1];
                Ctl(L2).Taps.parry++;
                Until(sim2, (s, ev) => L2.state == "parrywhiff", 0.5);
                Ctl(R2).Taps.light++;
                var h = Until(sim2, (s, ev) => ev != null && ev.type == "hit" && ev.att == 1, 0.4);
                Ok(h != null && h.counter && !h.guarded, "a hit during the whiff recovery should be a counter hit (破绽)");
            }
            // C. 看破连段：受击硬直中按 U 只格开第三段；只打两段则硬直结束时收势破绽
            {
                Func<DuelSim, DuelFighter, int, Action> combo = (sim, Rf, n) =>
                {
                    Ctl(Rf).Taps.light++;
                    int pressed = 1;
                    return () => { if (pressed < n && DuelSim.IsAttack(Rf.state) && Rf.state == "light" + pressed && Rf.hitIdx > 0) { Ctl(Rf).Taps.light++; pressed++; } };
                };
                // 踩准节奏：在第三段打到前 0.3 × 窗口按下 → 格开；挨了第二段立刻按（早于 0.6 × 窗口）→ 白费，第三段照样打中、提示「过早」
                Func<bool, object[]> third = early =>
                {
                    var sim = Stage("刘备", "关羽", 1.5); var L = sim.f[0]; var Rf = sim.f[1];
                    var tick = combo(sim, Rf, 3);
                    bool pressedU = false; DuelEvent ev = null, earlyEv = null;
                    for (int i = 0; i < 120 && ev == null; i++)
                    {
                        tick();
                        bool now2 = early ? Rf.state == "light2" && Rf.hitIdx > 0 : Rf.state == "light3" && (MV3.startup - Rf.t) / Rf.spd <= L.parryW * DuelSim.HS_PARRY_K * 0.5;
                        if (!pressedU && now2 && L.state == "hitstun") { Ctl(L).Taps.parry++; pressedU = true; }
                        sim.events.Clear(); sim.Step();
                        ev = sim.events.FirstOrDefault(e => e.type == "parry" || (e.type == "hit" && e.move == "light3"));
                        earlyEv = earlyEv ?? sim.events.FirstOrDefault(e => e.type == "parryEarly");
                    }
                    return new object[] { pressedU, ev, earlyEv };
                };
                var good = third(false); var bad = third(true);
                var gEv = (DuelEvent)good[1]; var bEv = (DuelEvent)bad[1];
                Ok((bool)good[0] && gEv != null && gEv.type == "parry" && gEv.move == "light3" && gEv.route == "hitstun", "U pressed on the 3rd hit's rhythm should parry it (got " + (gEv != null ? gEv.type + " " + gEv.move : "none") + ")");
                Ok((bool)bad[0] && bEv != null && bEv.type == "hit" && bad[2] != null, "U pressed right as the 2nd hit lands is too early: the 3rd hit lands, flagged 过早 (got " + (bEv != null ? bEv.type : "none") + ", early " + (bad[2] != null) + ")");
                double gap23 = (MV2.active * 0.6 + MV3.startup) / 1.12;   // 第二段打中 → 第三段打中（武力 100 出招最快）
                Ok(DuelSim.HS_PARRY_K * DuelSim.ParryWindow(100) < gap23 - 0.03, "the hitstun parry must be timed: its window (" + F(DuelSim.HS_PARRY_K * DuelSim.ParryWindow(100), 3) + "s) should be clearly shorter than the 2nd→3rd hit gap (" + F(gap23, 3) + "s)");
                // 只打两段
                var sim2 = Stage("刘备", "关羽", 1.5); var L2 = sim2.f[0]; var R2 = sim2.f[1];
                var tick2 = combo(sim2, R2, 2);
                bool pressed2 = false, whiff = false;
                for (int i = 0; i < 90; i++)
                {
                    tick2();
                    if (!pressed2 && R2.state == "light2" && R2.hitIdx > 0 && L2.state == "hitstun") { Ctl(L2).Taps.parry++; pressed2 = true; }
                    sim2.Step();
                    if (L2.state == "parrywhiff") { whiff = true; break; }
                }
                Ok(pressed2 && whiff && L2.whiffT < DuelSim.PARRY_WHIFF, "a hitstun parry that never meets a 3rd hit should leave a short whiff when the hitstun ends");
                // 第一段挨打后按 U 不能格开第二段（连段前两段只能硬吃或格挡）
                var sim3 = Stage("刘备", "关羽", 1.5); var L3 = sim3.f[0]; var R3 = sim3.f[1];
                var tick3 = combo(sim3, R3, 2);
                bool p3 = false, parried = false;
                for (int i = 0; i < 60; i++)
                {
                    tick3();
                    if (!p3 && R3.state == "light1" && R3.hitIdx > 0 && L3.state == "hitstun") { Ctl(L3).Taps.parry++; p3 = true; }
                    sim3.events.Clear(); sim3.Step();
                    if (sim3.events.Any(e => e.type == "parry")) parried = true;
                }
                Ok(p3 && !parried, "a hitstun parry should not parry the 2nd hit of a combo");
            }
            // D. 挡下一击后格开连段的下一击（举防 → 格开 → 反击）
            {
                var sim = Stage("关羽", "刘备", 1.5); var L = sim.f[0]; var Rf = sim.f[1];
                Ctl(L).Held.guard = true;
                Run(sim, 0.05);
                Ctl(Rf).Taps.light++;
                bool chained = false, pressed = false; DuelEvent ev = null;
                for (int i = 0; i < 60 && ev == null; i++)
                {
                    if (!chained && Rf.state == "light1" && Rf.hitIdx > 0) { Ctl(Rf).Taps.light++; chained = true; }
                    if (!pressed && L.state == "guardstun") { Ctl(L).Taps.parry++; pressed = true; }
                    sim.events.Clear(); sim.Step();
                    ev = sim.events.FirstOrDefault(e => e.type == "parry");
                }
                Ok(ev != null && ev.move == "light2", "blocking the 1st hit then pressing U should parry the 2nd (got " + (ev != null ? ev.move : "none") + ")");
            }
            // E. 绝技不可格开；重击可格开
            {
                var sim = Stage("关羽", "吕布", 1.6); var L = sim.f[0]; var Rf = sim.f[1];
                Rf.rage = 100; Ctl(Rf).Taps.special++;
                Until(sim, (s, ev) => s.freeze[0] <= 0 && Rf.state == "special", 1.5);
                Ctl(L).Taps.parry++;
                var e = Until(sim, (s, ev) => ev != null && (ev.type == "hit" || ev.type == "parry") && ev.att == 1, 1.2);
                Ok(e != null && e.type == "hit" && e.move == "special", "specials cannot be parried (got " + (e != null ? e.type : "null") + ")");
                var sim2 = Stage("关羽", "吕布", 1.6); var L2 = sim2.f[0]; var R2 = sim2.f[1];
                Ctl(R2).Taps.heavy++;
                var go = Until(sim2, (s, ev) => ev != null && ev.type == "heavyGo", 0.6);
                Ok(go != null, "releasing a heavy emits the telegraph event");
                Ctl(L2).Taps.parry++;
                var e2 = Until(sim2, (s, ev) => ev != null && (ev.type == "hit" || ev.type == "parry"), 0.5);
                Ok(e2 != null && e2.type == "parry" && e2.move == "heavy", "a heavy seen on its telegraph can be parried (got " + (e2 != null ? e2.type : "null") + ")");
                Ok(R2.state == "parried" && R2.stun > DuelSim.PARRY_STAGGER, "a parried heavy staggers a little longer");
            }
            // F. 电脑抓格开落空的破绽：武力高者多半抓到，武力低者少
            {
                // 只统计真正落空的格开（电脑恰好打进窗口的算格开成功，不计）：落空破绽中（或刚恢复的一瞬）挨打 = 抓到
                Func<string, string, int, double> punish = (ai, me, n) =>
                {
                    int got = 0, whiffs = 0;
                    for (int i = 0; i < n * 3 && whiffs < n; i++)
                    {
                        var sim = Stage(me, ai, 1.75); var L = sim.f[0]; var Rf = sim.f[1];
                        Rf.ctrl = new DuelAI(Rf);
                        Run(sim, 0.25 + 0.01 * (i % 7));
                        if (!sim.Actionable(L)) continue;
                        Ctl(L).Taps.parry++;
                        if (Until(sim, (s, ev) => L.state == "parry", 0.1) == null) continue;
                        if (Until(sim, (s, ev) => ev != null ? ev.type == "parry" || ev.type == "hit" : L.state == "parrywhiff", 0.5) != TrueEv) continue;
                        whiffs++;
                        var h = Until(sim, (s, ev) => ev != null ? ev.type == "hit" && ev.att == 1 : L.state != "parrywhiff", 1);
                        if (h != null && h.type == "hit") got++;
                    }
                    return whiffs != 0 ? 100.0 * got / whiffs : 0;
                };
                double strong = punish("吕布", "刘备", 200), weak = punish("孙乾", "刘备", 200);
                Console.WriteLine("  AI punishes a whiffed parry: 吕布 " + F(strong, 0) + "%, 孙乾 " + F(weak, 0) + "%");
                Ok(strong >= 50, "strong AI should usually punish a whiffed parry (got " + F(strong, 0) + "%)");
                Ok(strong > weak + 20, "strong AI should punish whiffed parries much more often than weak AI (" + F(strong, 0) + "% vs " + F(weak, 0) + "%)");
            }
        });

        // ---------------------------------------------------------------- (5) 胜率表 --
        Section("(5) parry bot / parry-spam bot vs AI across war gaps", () =>
        {
            int n = Math.Max(10, N >> 1);
            var PAIRS = new[] { new[] { "孙乾", "孙乾" }, new[] { "刘备", "刘备" }, new[] { "曹操", "曹操" }, new[] { "郭汜", "潘凤" }, new[] { "关羽", "关羽" }, new[] { "吕布", "吕布" }, new[] { "刘备", "吕布" }, new[] { "张飞", "刘备" } };
            var KINDS = new[] { "masher", "spam", "guard", "parry" };
            var T = new Dictionary<string, Dictionary<string, PlayRes>>();
            Console.WriteLine("  player vs AI            " + string.Join("", KINDS.Select(k => k.PadLeft(7))) + "   parry bot parries/tries, AI parries");
            foreach (var pa in PAIRS)
            {
                string p = pa[0], a = pa[1];
                var row = new Dictionary<string, PlayRes>();
                foreach (var k in KINDS) row[k] = Play(k, p, a, n);
                T[p + ">" + a] = row;
                var pr = row["parry"];
                Console.WriteLine("  " + (p + "(" + War(p) + ") vs " + a + "(" + War(a) + ")").PadRight(22, '　') + string.Join("", KINDS.Select(k => (F(row[k].pct, 0) + "%").PadLeft(7))) +
                    "   " + F(pr.parries, 1) + "/" + F(pr.tries, 1) + ", " + F(pr.aiParries, 1));
            }
            Func<string, string[], double> avg = (k, keys) => keys.Sum(x => T[x][k].pct) / keys.Length;
            var mid = new[] { "刘备>刘备", "曹操>曹操", "郭汜>潘凤" }; var strong = new[] { "关羽>关羽", "吕布>吕布" };
            double spamMid = avg("spam", mid), mashMid = avg("masher", mid), spamStrong = avg("spam", strong);
            double parryMid = avg("parry", mid), guardMid = avg("guard", mid), parryStrong = avg("parry", strong), guardStrong = avg("guard", strong);
            Console.WriteLine("  mid (72-76) avg: masher " + F(mashMid, 0) + "%, spam " + F(spamMid, 0) + "%, guard " + F(guardMid, 0) + "%, parry " + F(parryMid, 0) + "%;" +
                " strong (98-100) avg: spam " + F(spamStrong, 0) + "%, guard " + F(guardStrong, 0) + "%, parry " + F(parryStrong, 0) + "%");
            // 连按格开不占便宜
            Ok(spamMid <= 45, "parry-spam should not beat equal mid AIs (got " + F(spamMid, 0) + "%, want <= 45%)");
            Ok(spamMid <= mashMid + 15, "parry-spam should not be much better than plain mashing (" + F(spamMid, 0) + "% vs " + F(mashMid, 0) + "%)");
            Ok(spamStrong <= 30, "parry-spam vs war 98-100 should be hard (got " + F(spamStrong, 0) + "%)");
            Ok(T["孙乾>孙乾"]["spam"].pct >= 70, "weak generals stay easy even for the parry-spammer");
            // 格开对熟练玩家有用，但强将仍难、弱将仍易
            double parryUse = mid.Concat(strong).Sum(x => T[x]["parry"].parries) / 5;
            Ok(parryUse >= 0.5, "the parry bot should actually land parries (got " + F(parryUse, 2) + " per match)");
            Ok(parryMid >= guardMid - 5 && parryStrong >= guardStrong - 5, "parrying should help a skilled player (mid " + F(parryMid, 0) + "% vs " + F(guardMid, 0) + "%, strong " + F(parryStrong, 0) + "% vs " + F(guardStrong, 0) + "%)");
            Ok(parryStrong <= 90, "even a skilled parry player should not always beat war 98-100 mirrors (got " + F(parryStrong, 0) + "%)");
            Ok(T["刘备>吕布"]["parry"].pct <= 40, "刘备(76) should rarely beat 吕布(100) even with parries (got " + F(T["刘备>吕布"]["parry"].pct, 0) + "%)");
            Ok(T["孙乾>孙乾"]["parry"].pct >= 80 && T["张飞>刘备"]["parry"].pct >= 80, "a skilled player should beat weak / much weaker AIs");
            // 格开途径分类（parry 机器人 = 人的反应与节奏误差）：确实是按节奏 / 看准才格开的，而电脑看准格开重击有上限
            var by = new SortedDictionary<string, int>(StringComparer.Ordinal); int hvP = 0, hvPF = 0, hvA = 0, hvAF = 0;
            foreach (var x in mid.Concat(strong)) { var r = T[x]["parry"]; foreach (var kv in r.by) by[kv.Key] = (by.ContainsKey(kv.Key) ? by[kv.Key] : 0) + kv.Value; hvP += r.hP; hvPF += r.hPFull; hvA += r.hAI; hvAF += r.hAIFull; }
            Console.WriteLine("  parry bot, mid + strong mirrors, parries by route/move: " + string.Join(", ", by.Select(kv => kv.Key + " " + kv.Value)));
            Console.WriteLine("  heavies released: bot " + hvP + " (full " + hvPF + "), AI " + hvA + " (full " + hvAF + ")");
            Ok(by.Where(kv => kv.Key == "P hitstun/light3").Sum(kv => kv.Value) > 0, "the human-latency bot should land some timed 3rd-hit parries");
            // heavy 机器人（同 parry，试探多用重击，一半满蓄）对强将：电脑格开重击的次数有上限（满蓄至多约一半，轻点几乎没有）
            var hb = new SortedDictionary<string, int>(StringComparer.Ordinal); int hhP = 0, hhPF = 0; var hw = new List<string>();
            foreach (var pa in new[] { new[] { "关羽", "关羽" }, new[] { "吕布", "吕布" }, new[] { "张飞", "张辽" }, new[] { "刘备", "刘备" } })
            {
                var r = Play("heavy", pa[0], pa[1], n); hw.Add(pa[0] + ">" + pa[1] + " " + F(r.pct, 0) + "%");
                foreach (var kv in r.by) hb[kv.Key] = (hb.ContainsKey(kv.Key) ? hb[kv.Key] : 0) + kv.Value; hhP += r.hP; hhPF += r.hPFull;
            }
            int aiHv = hb.Where(kv => kv.Key.StartsWith("AI ") && kv.Key.EndsWith("/heavy")).Sum(kv => kv.Value);
            Console.WriteLine("  heavy bot: " + string.Join(", ", hw) + "; bot heavies " + hhP + " (full " + hhPF + "), AI parried " + aiHv + "; parries " + string.Join(", ", hb.Select(kv => kv.Key + " " + kv.Value)));
            Ok(hhPF > 0 && aiHv <= DuelSim.HEAVY_PARRY_CAP * hhPF + 0.05 * (hhP - hhPF) + 2, "AI heavy parries vs a human-latency player should stay bounded (" + aiHv + " of " + hhP + " heavies, " + hhPF + " full)");
        });

        // ---------------------------------------------------------------- (6) 重击与反应 --
        // 玩家（左，手动）从重击距离松开重击，右方为真实电脑：电脑看重击与看其他来招同样慢（react），轻点重击来不及格开，
        // 满蓄重击来得及但至多一半；人的反应（0.22 ± 0.04 秒）也能格开满蓄重击
        Section("(6) heavy release: reaction parries are fair for both sides", () =>
        {
            // 全体武将中出招最快 / 最慢者：轻点重击（蓄力 0.15）松手到打中、满蓄重击松手到打中（实际秒数）
            double sMin = 9, sMax = 0;
            foreach (var g in gensOrder) { double sp = new DuelSim(g, g, DuelLooks.LookOf(g), DuelLooks.LookOf(g)).f[0].spd; sMin = Math.Min(sMin, sp); sMax = Math.Max(sMax, sp); }
            Func<double, double, double> rt = (c, sp) => DuelSim.HeavySU(c, sp) / sp;
            Console.WriteLine("  heavy release → impact: tap " + F(rt(0.15, sMax), 3) + "–" + F(rt(0.15, sMin), 3) + "s, full charge " + F(rt(1, sMax), 3) + "–" + F(rt(1, sMin), 3) + "s");
            Ok(rt(0.15, sMin) < 0.17, "a tap heavy should be too fast to react to (" + F(rt(0.15, sMin), 3) + "s)");
            Ok(rt(1, sMax) >= 0.24, "a full-charge heavy should be reactable by a human (" + F(rt(1, sMax), 3) + "s >= 0.24s)");
            // 电脑（右）对玩家松开的重击：格开的比例按「电脑看清了这一记重击、来得及决定」的次数算（AI.hvSeq 记下这一记）
            Func<string, string, double, int, Dictionary<string, object>> vsAI = (p, ai, chg, n) =>
            {
                var cnt = new Dictionary<string, int> { { "parried", 0 }, { "hit", 0 }, { "guarded", 0 }, { "gbreak", 0 }, { "avoid", 0 } };
                var lat = new List<double>();
                int looked = 0, lookedParried = 0;
                double react = new DuelAI(new DuelSim(gens[ai], gens[ai], DuelLooks.LookOf(gens[ai]), DuelLooks.LookOf(gens[ai])).f[0]).react;
                for (int i = 0; i < n * 12 && looked < n; i++)
                {
                    var sim = Stage(p, ai, 2.05 + (i % 4) * 0.05); var L = sim.f[0]; var Rf = sim.f[1];
                    var A = new DuelAI(Rf); Rf.ctrl = A;
                    Run(sim, 0.3 + (i % 9) * 0.04);
                    if (!sim.Actionable(L)) continue;
                    Ctl(L).Taps.heavy++; Ctl(L).Held.heavy = true;
                    double rel = -1, press = -1; string res = null; int hSeq = -1; bool saw = false;
                    for (int k = 0; k < 150 && res == null; k++)
                    {
                        if (L.state == "charge" && L.charge >= chg) Ctl(L).Held.heavy = false;
                        int hv0 = A.hvSeq; double d0 = Math.Abs(L.x - Rf.x);   // 电脑在步首决定（此时的距离）
                        sim.events.Clear(); sim.Step();
                        if (L.state == "heavy" && hSeq < 0) hSeq = L.seq;
                        // 这一步电脑看清了这一记重击：当时在格开够得着的距离内、离打中还有时间 → 计入分母
                        if (hSeq >= 0 && hv0 != hSeq && A.hvSeq == hSeq && L.state == "heavy" && L.Phase == "startup")
                            saw = d0 <= L.reach * MVH.reachK + 0.2 + 0.6;
                        foreach (var e in sim.events)
                        {
                            if (e.type == "heavyGo" && e.who == 0) rel = sim.clock;
                            if (e.type == "parryStart" && e.who == 1 && rel >= 0 && press < 0) press = sim.clock;
                            if (e.type == "parry" && e.def == 1) res = "parried";
                            if (e.type == "hit" && e.att == 0) res = e.breaks ? "gbreak" : e.guarded ? "guarded" : "hit";
                            if (e.type == "hit" && e.att == 1) res = "skip";
                        }
                        if (rel >= 0 && res == null && L.state != "heavy" && L.state != "charge") res = "avoid";
                    }
                    if (res == null || res == "skip") continue;
                    cnt[res]++;
                    if (res == "parried") lat.Add(press - rel);
                    if (saw) { looked++; if (res == "parried") lookedParried++; }
                }
                int reached = cnt["parried"] + cnt["hit"] + cnt["guarded"] + cnt["gbreak"];
                return new Dictionary<string, object> {
                    { "out", cnt }, { "n", looked }, { "pct", looked != 0 ? 100.0 * lookedParried / looked : 0.0 }, { "reachedPct", reached != 0 ? 100.0 * cnt["parried"] / reached : 0.0 },
                    { "other", cnt["parried"] - lookedParried }, { "minLat", lat.Count > 0 ? (object)lat.Min() : null }, { "react", react } };
            };
            foreach (var pa in new[] { new[] { "关羽", "吕布" }, new[] { "关羽", "关羽" }, new[] { "张飞", "张辽" }, new[] { "刘备", "曹操" } })
            {
                string p = pa[0], ai = pa[1];
                var t = vsAI(p, ai, 0.15, 40); var f = vsAI(p, ai, 0.95, 200);   // 满蓄：电脑看清的 200 记
                var fo = (Dictionary<string, int>)f["out"]; var to = (Dictionary<string, int>)t["out"];
                double fReact = (double)f["react"]; object fMin = f["minLat"];
                Console.WriteLine("  " + p + " heavy vs AI " + ai + "(" + War(ai) + "): tap parried " + F((double)t["pct"], 0) + "% of " + t["n"] + " seen; full parried " + F((double)f["pct"], 0) + "% of " + f["n"] + " seen" +
                    " (" + F((double)f["reachedPct"], 0) + "% of those that reached it; " + f["other"] + " parried unseen) " + Json(fo["parried"], fo["hit"], fo["guarded"], fo["gbreak"], fo["avoid"]) +
                    (fMin != null ? "; AI press ≥ " + F((double)fMin * 1000, 0) + "ms after release (react " + F(fReact * 1000, 0) + "ms)" : ""));
                Ok((double)t["pct"] <= 5 && to["parried"] <= 2, "AI " + ai + " should not parry tap heavies on reaction (" + F((double)t["pct"], 0) + "%, " + to["parried"] + " parried)");
                Ok((double)f["pct"] <= 100 * DuelSim.HEAVY_PARRY_CAP + 12, "AI " + ai + " should parry at most about half of the full-charge heavies it sees (" + F((double)f["pct"], 0) + "%)");
                Ok((int)f["other"] == 0, "AI " + ai + " should parry full heavies only on reaction (" + f["other"] + " parried without seeing the release)");
                Ok(fMin == null || (double)fMin >= fReact - 1e-6, "AI " + ai + " must not react to a heavy faster than to other attacks");
            }
            // 人的反应：看到松手 0.22 ± 0.04 秒后按 U
            Func<string, string, double, int, double> human = (def, att, chg, n) =>
            {
                int par = 0, tot = 0;
                for (int i = 0; i < n; i++)
                {
                    var sim = Stage(def, att, 2.1); var L = sim.f[0]; var Rf = sim.f[1];
                    Ctl(Rf).Taps.heavy++; Ctl(Rf).Held.heavy = true;
                    double rtm = 0.22 + 0.04 * ((R() + R() + R() + R() - 2) * 1.732);
                    double rel = -1; string res = null; bool pressed = false;
                    for (int k = 0; k < 150 && res == null; k++)
                    {
                        if (Rf.state == "charge" && Rf.charge >= chg) Ctl(Rf).Held.heavy = false;
                        if (rel >= 0 && sim.clock >= rel + rtm && !pressed) { Ctl(L).Taps.parry++; pressed = true; }
                        sim.events.Clear(); sim.Step();
                        foreach (var e in sim.events)
                        {
                            if (e.type == "heavyGo" && e.who == 1) rel = sim.clock;
                            if (e.type == "parry" && e.def == 0) res = "parried";
                            if (e.type == "hit" && e.att == 1) res = "hit";
                        }
                    }
                    if (res != null) { tot++; if (res == "parried") par++; }
                }
                return tot != 0 ? 100.0 * par / tot : 0;
            };
            double hFull = human("刘备", "关羽", 0.95, 60), hTap = human("刘备", "关羽", 0.15, 60);
            Console.WriteLine("  human reaction (0.22 ± 0.04 s) parrying 关羽's heavies as 刘备: full charge " + F(hFull, 0) + "%, tap " + F(hTap, 0) + "%");
            Ok(hFull >= 50, "a human-latency player should parry most full-charge heavies on reaction (" + F(hFull, 0) + "%)");
            Ok(hTap <= 10, "nobody should parry tap heavies on reaction (" + F(hTap, 0) + "%)");
        });

        // ---------------------------------------------------------------- (7) 防守试探型打法 --
        Section("(7) guard-and-poke / cautious / turtle bots vs AI; war-gap ladder", () =>
        {
            int n = Math.Max(20, N >> 1);
            var MID8 = new[] { new[] { "刘备", "刘备" }, new[] { "曹操", "曹操" }, new[] { "郭汜", "潘凤" }, new[] { "王平", "王平" }, new[] { "李傕", "刘备" }, new[] { "张济", "曹操" }, new[] { "祖茂", "黄祖" }, new[] { "孟达", "潘凤" } };
            var res = new Dictionary<string, double[]>();
            foreach (var k in new[] { "guard", "parry", "cautious", "turtle", "turtleP", "backstepper" })
            {
                var rs = MID8.Select(pa => Play(k, pa[0], pa[1], n)).ToList();
                double avg = rs.Sum(r => r.pct) / rs.Count;
                double full = rs.Sum(r => (double)r.hAIFull) / (rs.Count * n), aiP = rs.Sum(r => r.aiParries) / rs.Count;
                res[k] = new[] { avg, full, aiP };
                Console.WriteLine("  " + k.PadRight(8) + " vs equal mid AI: " + F(avg, 0) + "%  [" + string.Join(" ", rs.Select(r => F(r.pct, 0))) + "]  AI full-charge heavies " + F(full, 2) + "/match, AI parries " + F(aiP, 2) + "/match");
            }
            Ok(res["guard"][0] <= 50, "guard-and-poke bot should not beat equal mid AIs (got " + F(res["guard"][0], 0) + "%, want <= 50%; v3 before the AI refine: 73-81%)");
            Ok(res["parry"][0] >= 25 && res["parry"][0] <= 70, "a skilled parry player should keep a fair chance against equal mid AIs (got " + F(res["parry"][0], 0) + "%, want 25-70%)");
            Ok(res["cautious"][0] >= 25 && res["cautious"][0] <= 65, "a cautious player (blocks / parries after his poke is blocked) should keep a fair chance (got " + F(res["cautious"][0], 0) + "%)");
            Ok(res["turtle"][0] <= 70, "pure turtling should not beat equal mid AIs (got " + F(res["turtle"][0], 0) + "%, want <= 70%)");
            Ok(res["turtle"][1] >= 1, "the AI should break a turtle with full-charge heavies from out of his reach (got " + F(res["turtle"][1], 2) + " per match)");
            Ok(res["turtleP"][0] <= 70, "turtling and parrying every charged heavy should not beat equal mid AIs (got " + F(res["turtleP"][0], 0) + "%, want <= 70%; before this fix about 92%)");
            Ok(res["backstepper"][0] <= 55, "poke-and-backstep should not beat equal mid AIs (got " + F(res["backstepper"][0], 0) + "%, want <= 55%; before this fix about 62-69%)");
            // 武力差阶梯：玩家 刘备(76) / 曹操(72) 对 电脑 简雍 30 · 刘表 50 · 徐庶 64 · 樊稠 78 · 董卓 86 · 吕布 100
            var Lad = new[] { "简雍", "刘表", "徐庶", "樊稠", "董卓", "吕布" };
            foreach (var k in new[] { "guard", "parry" })
            {
                var row = Lad.Select(a => (Play(k, "刘备", a, n).pct + Play(k, "曹操", a, n).pct) / 2).ToList();
                Console.WriteLine("  ladder " + k.PadRight(6) + " vs AI war " + string.Join("/", Lad.Select(War)) + ": " + string.Join(" ", row.Select(x => F(x, 0))));
                Ok(row.Select((x, i) => i == 0 || x <= row[i - 1] + 12).All(b => b), k + " win rate should fall as the AI's war rises (" + string.Join(" ", row.Select(x => F(x, 0))) + ")");
                Ok(row[0] >= 85 && row[Lad.Length - 1] <= 15, k + ": war 30 AI should be easy and war 100 AI hard for a war 72-76 player (" + F(row[0], 0) + "% / " + F(row[Lad.Length - 1], 0) + "%)");
            }
            // 熟练玩家（parry 机器人）在武力相近（±6）时仍有一搏，对武力低 6–12 的电脑多半能赢——弱将与名将的玩家都如此
            foreach (var grp in new[] { new[] { new[] { "刘表", "张鲁" }, new[] { "陆康" }, new[] { "糜芳" } }, new[] { new[] { "关羽", "马超" }, new[] { "董卓", "华雄" }, new[] { "张辽", "关羽" } } })
            {
                var ps = grp[0]; var below = grp[1]; var near6 = grp[2];
                Func<string[], double> avg = aS => aS.Sum(a => ps.Sum(p => Play("parry", p, a, n).pct) / ps.Length) / aS.Length;
                double lo = avg(below), hi = avg(near6);
                Console.WriteLine("  parry bot as " + string.Join("/", ps) + "(" + War(ps[0]) + "): vs AI " + string.Join("/", below.Select(a => a + War(a))) + " " + F(lo, 0) + "%, vs AI " + string.Join("/", near6.Select(a => a + War(a))) + " " + F(hi, 0) + "%");
                Ok(lo >= 65, "a skilled player should usually beat an AI 6-12 war below (" + ps[0] + ": " + F(lo, 0) + "%)");
                Ok(hi >= 25, "a skilled player should keep a fair chance within +6 war (" + ps[0] + ": " + F(hi, 0) + "%)");
            }
        });

        Console.WriteLine(checks + " checks");
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
}
