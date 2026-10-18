// 第三版成长测试（tests/sim.js 第 (7) 节除 (7d) 外的 C# 版；(7d) 计策见 HarnessTactics.cs）：
//   (7a) 规则单元：等级表、按武力 / 智力的格数与等阶、升级候选（确定、至多 3 个、两类都出现、每类含最高阶）、习得与拒绝、
//        电脑 / 玩家自动习得（playerAutoLearn）、Pending、Ensure 幂等、自动结算的攻城战给经验且不重复计；
//   (7b) 存档 v3 往返与第二版存档迁移（PlayerPrefs：只有第二版键时可读，存档写入第三版键并删除第二版键）；
//   (7c) 可习得必杀：数据（等阶、能力、唯一性）与按等阶的参考伤害倍率、暗杀的期望值、每人至多一招暗杀；
//   (7e) 成长后的压力测试：随机等级与习得，电脑完整打完，每一手后检查状态；
//   (7f) 多年全电脑模拟：成长开 / 关（自动结算不看等级，城池归属应完全相同）与等级分布；
//   (7g) 战术战斗平衡：全电脑模拟开局 / 第 6、12 个月 / 第 3、8 年的局面，成长开 / 关各打一遍；
//   (7h) 成长的分量：第 8 年局面里攻方 / 守方打回 1 级的胜率与战损比变化。
//   (exp-ref) 经验结算：Progress.BattleExp / AutoBattle 对 ref/progress-ref.txt 中的探针输入（导出工具用 SG.Progress 算出）逐行与 JS 相同；
//        经验来源的权重在 JS 里是 battleExp 的字面常数（C# 照抄），这一节让 JS 的任何改动都能被发现。
// 输出格式与 JS 相同，便于逐行对照（同一随机序列；数值逐字相同；run.sh 的 JS 对照自动检查）。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;
using R = UnityEngine.Random;

class PG
{
    static int fails;
    static readonly string[] DMG_KINDS = { "smite", "cleave", "charge", "rampage", "volley", "drain" };
    static readonly string[][] pairs = Sim.Pairs;
    static string F(double v, int n) { return Sim.F(v, n); }
    static double Median(List<double> a) { return Sim.Median(a); }
    static double Median(List<int> a) { return Sim.Median(a.Select(x => (double)x).ToList()); }

    static int Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Progress.enabled = true;
        BattleModel.specialsEnabled = true;
        var t0 = DateTime.Now;
        Section("(7a) progression rules", A);
        Section("(7b) save v3", B);
        Section("(7c) learnable specials", C);
        Section("(7e) leveled battles", E);
        Section("(7f) campaign", Fc);
        Section("(7g) tactical balance", Gb);
        Section("(7h) progression weight", H);
        Section("(exp-ref) experience probes", ExpRef);   // C# 独有（JS 端的结果在导出工具生成的 ref/progress-ref.txt 中）；放最后，不扰动前面各节
        Console.WriteLine($"total {F((DateTime.Now - t0).TotalSeconds, 1)}s");
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
    static void Section(string name, Action a)
    {
        try { a(); }
        catch (Exception e)
        {
            fails++; Progress.enabled = true; Progress.playerAutoLearn = true; R.ValueOverride = null;
            Console.WriteLine(name + " FAILED: " + e);
        }
    }

    // ------------------------------------------------------------ 共用 --
    sealed class Stats
    {
        public int n, w, days, oldTac, newTac, sig, pool, assnKill; public double dmg;
        public List<double> atk = new List<double>(), assn = new List<double>();
        public SortedDictionary<string, int> byTac = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public SortedDictionary<int, int> poolTier = new SortedDictionary<int, int>();
        public SortedDictionary<int, List<double>> poolRatio = new SortedDictionary<int, List<double>>();
        public List<string> outcomes = new List<string>();
    }
    // 电脑对电脑打完一场（与 Sim.RunBattle 同样的行动顺序）；st 收集统计，check = true 时每一手后检查状态
    static void FightAI(BattleModel M, Stats st, bool check)
    {
        int guard = 0;
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
                    var act = M.PlanAction(u);
                    if (act.kind == "attack") { var r = M.Attack(u, act.target); st.atk.Add(r.dmg); st.dmg += r.dmg + r.counter; }
                    else if (act.kind == "tactic")
                    {
                        if (act.tactic.IsNew)
                        {
                            var r = M.UseNewTactic(u, act.tactic, act.target);
                            st.dmg += r.dmg;
                            st.newTac++; int c; st.byTac[act.tactic.Id] = st.byTac.TryGetValue(act.tactic.Id, out c) ? c + 1 : 1;
                            if (check) Sim.CheckSpecialResult(r, "tactic " + act.tactic.Name);
                        }
                        else { var r = M.UseTactic(u, act.tactic, act.target); st.dmg += r.Value; st.oldTac++; }
                    }
                    else if (act.kind == "duel") M.Duel(u, act.target);
                    else if (act.kind == "special")
                    {
                        var sp = M.SpecialOf(u);
                        if (act.special != null && sp != act.special) throw new Exception("planned special " + act.special.name + " but default resolves to " + sp.name);
                        var tg = act.target;
                        double rf = tg != null && tg.side != u.side ? Sim.RefAction(M, u, tg, sp) : 0;
                        var r = M.UseSpecial(u, act.target);
                        if (r.sp == null || !u.spUsed.Contains(sp.id)) throw new Exception("planned special not used: " + u.gen.name + " " + r.why);
                        st.dmg += r.dmg;
                        if (sp.pool)
                        {
                            st.pool++;
                            int c; st.poolTier[sp.tier] = st.poolTier.TryGetValue(sp.tier, out c) ? c + 1 : 1;
                            if (rf > 0 && r.dmg > 0 && DMG_KINDS.Contains(sp.kind)) { if (!st.poolRatio.ContainsKey(sp.tier)) st.poolRatio[sp.tier] = new List<double>(); st.poolRatio[sp.tier].Add(r.dmg / rf); }
                            if (rf > 0 && sp.kind == "assassinate") { st.assn.Add(r.dmg / rf); if (r.killed != null) st.assnKill++; }
                        }
                        else st.sig++;
                        if (check) Sim.CheckSpecialResult(r, "special " + sp.name);
                    }
                    if (check) Sim.CheckBattleState(M, "fightAI " + u.gen.name);
                    M.Spend(u);
                }
            }
            if (M.result == 0) M.EndSide();
        }
        if (M.result == 0) throw new Exception("battle never ended");
    }
    static string LevelHist(IEnumerable<General> gens)
    {
        var h = new SortedDictionary<int, int>();
        foreach (var x in gens) { int c; h[x.level] = h.TryGetValue(x.level, out c) ? c + 1 : 1; }
        return string.Join(" ", h.Select(kv => "L" + kv.Key + ":" + kv.Value));
    }
    // 习得的招式不超出格数与等阶上限
    static void CheckCaps(General x, string where)
    {
        var ss = Progress.SpecialSlots(x); var ts = Progress.TacticSlots(x);
        if (x.specials.Count > ss.max) throw new Exception(where + ": " + x.name + " knows " + x.specials.Count + " learned specials, slots " + ss.max);
        for (int k = 0; k < x.specials.Count; k++)
        {
            var sp = Specials.ById(x.specials[k]);
            if (sp == null || sp.tier > ss.statTier || sp.tier > k + 1) throw new Exception(where + ": " + x.name + " (war " + x.war + ") has " + x.specials[k] + " beyond tier cap (slot " + (k + 1) + ")");
        }
        if (x.specials.Count(id => Specials.ById(id).kind == "assassinate") > 1) throw new Exception(where + ": " + x.name + " knows more than one pool assassinate");
        var lt = Progress.LearnedTactics(x);
        if (lt.Count > ts.max) throw new Exception(where + ": " + x.name + " knows " + lt.Count + " learned tactics, slots " + ts.max);
        for (int k = 0; k < lt.Count; k++)
        {
            var t = Tactics.ById(lt[k]);
            if (t == null || t.Tier > ts.statTier || t.Tier > k + 1 || x.intel < t.ReqInt) throw new Exception(where + ": " + x.name + " (intel " + x.intel + ") has tactic " + lt[k] + " beyond cap (slot " + (k + 1) + ")");
        }
        if (!(x.level >= 1 && x.level <= Progress.MaxLevel) || x.level != Progress.LevelFor(x.exp)) throw new Exception(where + ": bad level " + x.name + " " + x.level + " exp " + x.exp);
    }
    static string Dump(General x)
    {
        return string.Join("|", new[] { x.name, x.exp.ToString(), x.level.ToString(), string.Join(",", x.specials), x.tactics == null ? "null" : string.Join(",", x.tactics),
            x.record == null ? "null" : x.record.battles + "/" + x.record.wins + "/" + x.record.routs + "/" + x.record.duels });
    }
    static int n99;
    static General Mk(int war, int intel, int lv, int faction = -1)
    {
        var x = new General { id = 99000 + n99, name = "测试" + (n99++), war = war, intel = intel, pol = 50, faction = faction, city = 0, troops = 3000, training = 60, loyalty = 80 };
        Progress.Ensure(x); x.exp = Progress.Exp[Math.Max(1, lv)]; Progress.Ensure(x);
        return x;
    }

    // ---- (exp-ref) 经验结算对照 ref/progress-ref.txt ----
    static void ExpRef()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var none = StringSplitOptions.RemoveEmptyEntries;
        int n = 0, na = 0, bad = 0;
        foreach (var line in System.IO.File.ReadAllLines("ref/progress-ref.txt"))
        {
            if (line.StartsWith("#") || line.Trim().Length == 0) continue;
            var p = line.Split('\t');
            string got, want;
            if (p[0] == "exp")
            {
                var v = p[1].Split(' ').Select(x => double.Parse(x, inv)).ToArray();
                got = "=> " + Progress.BattleExp(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9] != 0, v[10] != 0);
                want = p[2]; n++;
            }
            else if (p[0] == "auto")
            {
                // 武将 = 编号:武力:兵力:训练:战后兵力（同导出工具的探针）
                Func<string, List<int[]>> gens = f => f.Substring(4).Split(new[] { ' ' }, none).Select(t => t.Split(':').Select(int.Parse).ToArray()).ToList();
                var A = gens(p[2]); var D = gens(p[3]);
                Func<int[], General> mk = q => { var x = new General { id = 70000 + q[0], name = "探针" + q[0], war = q[1], intel = 50, pol = 50, faction = -1, city = 0, troops = q[2], training = q[3], loyalty = 80 }; Progress.Ensure(x); return x; };
                var s = new BattleSetup { atk = A.Select(mk).ToList(), def = D.Select(mk).ToList(), attackerWon = p[5] == "attackerWon 1" };
                foreach (var id in p[4].Substring(7).Split(new[] { ',' }, none)) s.routed.Add(70000 + int.Parse(id));
                var snap = Progress.Snapshot(s);
                for (int i = 0; i < A.Count; i++) s.atk[i].troops = A[i][4];
                for (int i = 0; i < D.Count; i++) s.def[i].troops = D[i][4];
                var res = Progress.AutoBattle(s, snap);
                got = "=> " + string.Join(" ", res.Select(x => $"{x.gen.id - 70000}:{x.side}:{(x.won ? 1 : 0)}:{x.exp}:{x.from}>{x.to}")) + "\t" +
                      string.Join(" ", s.atk.Concat(s.def).Select(x => $"{x.id - 70000}:{x.record.battles}/{x.record.wins}/{x.record.routs}/{x.record.duels}"));
                want = p[6] + "\t" + p[7]; na++;
            }
            else continue;
            if (got != want) { bad++; if (bad <= 6) Console.WriteLine($"  MISMATCH {p[0]} {p[1]}:\n    got  {got}\n    want {want}"); }
        }
        if (n < 50 || na < 5) throw new Exception($"ref/progress-ref.txt too short ({n} exp probes, {na} auto cases)");
        if (bad > 0) throw new Exception(bad + " experience probes differ from JS");
        Console.WriteLine($"(exp-ref) experience OK: Progress.BattleExp {n} probes, AutoBattle {na} cases identical to JS (ref/progress-ref.txt)");
    }

    // ---- (7a) 规则单元：等级表、格数与等阶、候选、习得、电脑自动习得 ----
    static void A()
    {
        R.InitState(3);
        var g = GameState.Current = GameState.NewGame("cao");
        for (int L = 2; L <= Progress.MaxLevel; L++)
        {
            if (!(Progress.Exp[L] > Progress.Exp[L - 1])) throw new Exception("EXP table not increasing at " + L);
            if (Progress.LevelFor(Progress.Exp[L]) != L || Progress.LevelFor(Progress.Exp[L] - 1) != L - 1) throw new Exception("levelFor wrong at " + L);
        }
        n99 = 0;
        var innate = new[] { new KeyValuePair<int, string>(39, ""), new KeyValuePair<int, string>(40, "inspire"), new KeyValuePair<int, string>(55, "fire,inspire"),
            new KeyValuePair<int, string>(65, "fire,rockfall,inspire"), new KeyValuePair<int, string>(75, "fire,rockfall,confuse,inspire") };
        foreach (var kv in innate) if (string.Join(",", Mk(50, kv.Key, 1).tactics) != kv.Value) throw new Exception("innate tactics for intel " + kv.Key + ": " + string.Join(",", Mk(50, kv.Key, 1).tactics));
        // [能力, 等级, 格数, 能力等阶上限, 等级等阶上限, 已解锁格数]；第 k 格最高 k 阶，故未学时 tier = min(…, 1)
        var slotCase = new[] { new[] { 59, 10, 0, 0, 4, 0 }, new[] { 60, 10, 1, 1, 4, 1 }, new[] { 75, 10, 2, 2, 4, 2 }, new[] { 85, 10, 3, 3, 4, 3 }, new[] { 91, 10, 4, 3, 4, 4 },
            new[] { 92, 10, 4, 4, 4, 4 }, new[] { 100, 1, 4, 4, 0, 0 }, new[] { 100, 2, 4, 4, 1, 1 }, new[] { 100, 5, 4, 4, 2, 2 }, new[] { 100, 8, 4, 4, 4, 4 } };
        foreach (var c in slotCase)
        {
            var s = Progress.SpecialSlots(Mk(c[0], 30, c[1]));
            if (s.max != c[2] || s.statTier != c[3] || s.levelTier != c[4] || s.unlocked != c[5] || s.tier != Math.Min(c[3], Math.Min(c[4], 1))) throw new Exception($"special slots war {c[0]} L{c[1]}");
        }
        var tacCase = new[] { new[] { 49, 10, 0, 0, 4, 0 }, new[] { 50, 10, 1, 1, 4, 1 }, new[] { 64, 10, 2, 1, 4, 2 }, new[] { 65, 10, 2, 2, 4, 2 }, new[] { 80, 10, 4, 3, 4, 4 },
            new[] { 95, 10, 5, 4, 4, 5 }, new[] { 95, 2, 5, 4, 0, 0 }, new[] { 95, 3, 5, 4, 1, 1 }, new[] { 95, 9, 5, 4, 4, 4 } };
        foreach (var c in tacCase)
        {
            var s = Progress.TacticSlots(Mk(30, c[0], c[1]));
            if (s.max != c[2] || s.statTier != c[3] || s.levelTier != c[4] || s.unlocked != c[5] || s.tier != Math.Min(c[3], Math.Min(c[4], 1))) throw new Exception($"tactic slots intel {c[0]} L{c[1]}");
        }
        // 候选：确定、至多 3 个、全部合格；两类都有空格时两类都出现
        var rnd = new SeededRandom(77);
        int mixed = 0, total = 0;
        for (int i = 0; i < 300; i++)
        {
            int w0 = 40 + rnd.Next(61), i0 = 40 + rnd.Next(61), l0 = 1 + rnd.Next(10);
            var x = Mk(w0, i0, l0);
            for (int step = 0; step < 6; step++)
            {
                var c1 = Progress.Candidates(x); var c2 = Progress.Candidates(x);
                if (string.Join(",", c1.Select(c => c.id)) != string.Join(",", c2.Select(c => c.id))) throw new Exception("candidates not deterministic");
                var op = Progress.OpeningsOf(x);
                if (c1.Count > 3) throw new Exception("more than 3 candidates");
                if (op.total > 0 && c1.Count == 0 && (Progress.Eligible(x, "special").Count > 0 || Progress.Eligible(x, "tactic").Count > 0)) throw new Exception("openings but no candidates");
                foreach (var c in c1)
                {
                    int cap = c.type == "special" ? Progress.SpecialSlots(x).tier : Progress.TacticSlots(x).tier;
                    if (c.tier > cap) throw new Exception("candidate above tier cap: " + c.name);
                    if (!Progress.Eligible(x, c.type).Any(e => e.id == c.id)) throw new Exception("ineligible candidate " + c.id);
                    if (string.IsNullOrEmpty(c.name) || string.IsNullOrEmpty(c.desc) || string.IsNullOrEmpty(c.rules) || !System.Text.RegularExpressions.Regex.IsMatch(c.color ?? "", "^#[0-9a-fA-F]{6}$")) throw new Exception("candidate lacks display fields: " + c.id);
                }
                foreach (var type in new[] { "special", "tactic" })
                {
                    int cap = type == "special" ? Progress.SpecialSlots(x).tier : Progress.TacticSlots(x).tier;
                    if (Progress.Eligible(x, type).Any(e => e.tier == cap) && !c1.Any(c => c.type == type && c.tier == cap)) throw new Exception("offer lacks a top-tier " + type + " (tier " + cap + ") for " + x.name);
                }
                if (op.special > 0 && op.tactic > 0 && Progress.Eligible(x, "special").Count > 0 && Progress.Eligible(x, "tactic").Count > 0)
                {
                    total++;
                    if (c1.Any(c => c.type == "special") && c1.Any(c => c.type == "tactic")) mixed++;
                }
                if (c1.Count == 0) break;
                var pick = c1[rnd.Next(c1.Count)];
                if (!Progress.Learn(x, pick)) throw new Exception("learn failed for an offered candidate");
                if (Progress.Learn(x, pick)) throw new Exception("learned the same entry twice");
            }
            CheckCaps(x, "7a");
        }
        if (total > 0 && mixed != total) throw new Exception($"mixed openings offered both kinds only {mixed}/{total}");
        // 不合格的习得一律拒绝
        var low = Mk(65, 55, 10);
        var t4 = Specials.Pool().First(sp => sp.tier == 4); var tt4 = Tactics.Learnable().First(t => t.Tier == 4);
        if (Progress.Learn(low, "special", t4.id) || Progress.Learn(low, "tactic", tt4.Id) || Progress.Learn(low, "tactic", "fire") || Progress.Learn(low, "special", "lp:nope")) throw new Exception("learn() accepted an ineligible entry");
        // 电脑自动习得；玩家武将：playerAutoLearn = false（升级对话框上线后）保留待选，缺省 true 时与电脑一样自动习得
        if (Progress.playerAutoLearn != true) throw new Exception("playerAutoLearn should default to true until the level-up dialog ships");
        var ai = Mk(95, 95, 1, 3); var me = Mk(95, 95, 1, g.player); var me2 = Mk(95, 95, 1, g.player);
        Progress.playerAutoLearn = false;
        ProgressResult ra, rm, rm2;
        try { ra = Progress.Gain(ai, Progress.Exp[10]); rm = Progress.Gain(me, Progress.Exp[10]); } finally { Progress.playerAutoLearn = true; }
        rm2 = Progress.Gain(me2, Progress.Exp[10]);
        if (ai.level != 10 || me.level != 10 || ai.exp != Progress.Exp[10]) throw new Exception("gain did not reach level 10");
        if (Progress.OpeningsOf(ai).total != 0 || ra.learned.Count != 9 || ai.specials.Count != 4 || Progress.LearnedTactics(ai).Count != 5) throw new Exception("AI auto-learn should fill 4 + 5 slots: learned " + ra.learned.Count);
        if (rm.learned.Count != 0 || Progress.OpeningsOf(me).total != 9) throw new Exception("player general should keep openings for the level-up dialog (playerAutoLearn false)");
        if (rm2.learned.Count != 9 || Progress.OpeningsOf(me2).total != 0) throw new Exception("player general should auto-learn while playerAutoLearn is on: " + rm2.learned.Count);
        g.generals.Add(me); g.generals.Add(me2);
        if (!Progress.Pending(g.player).Contains(me) || Progress.Pending(g.player).Contains(me2)) throw new Exception("pending() wrong for the player generals");
        g.generals.RemoveAt(g.generals.Count - 1); g.generals.RemoveAt(g.generals.Count - 1);
        // 计策格（Unity 版的计策目录总是载入；网页版另测未载入 tactics-data.js 时 openings 不计计策格）
        {
            var lone = Mk(40, 95, 1, g.player);
            lone.exp = Progress.Exp[10]; Progress.Ensure(lone);
            if (Progress.OpeningsOf(lone).tactic != 5 || Progress.Candidates(lone).Count == 0) throw new Exception("openings with the tactics catalogue: " + Progress.OpeningsOf(lone).tactic);
        }
        var tiers = string.Concat(ai.specials.Select(id => Specials.ById(id).tier.ToString()));
        if (tiers != "1234") throw new Exception("slot k should take tier k for a war-95 general: " + tiers);
        // Ensure 幂等
        var before = Dump(ai); Progress.Ensure(ai); if (Dump(ai) != before) throw new Exception("ensure() not idempotent");
        // 自动结算（Conquest.AutoResolve 内含经验结算）：双方都得经验，结果在 s.progress，不重复计
        {
            var src = g.CityByKey("chenliu"); var tgt = g.CityByKey("luoyang");
            var s = Conquest.Prepare(src.owner, src, tgt, g.OfficersIn(src).Where(x => x.troops > 0).Take(5).ToList(), 3000, 0);
            var both = s.atk.Concat(s.def).ToList();
            var exp0 = both.Select(x => x.exp).ToList();
            R.NextDouble(); R.InitState(3);
            Conquest.AutoResolve(s);
            var got = both.Select((x, i) => x.exp - exp0[i]).ToList();
            if (!s.progressDone || s.progress == null || s.progress.Count != s.atk.Count + s.def.Count) throw new Exception("autoResolve did not award experience: " + (s.progress != null ? s.progress.Count : -1));
            if (got.Any(v => !(v >= 10))) throw new Exception("every participant should gain exp: " + string.Join(",", got));
            var exp1 = both.Select(x => x.exp).ToList();
            Progress.AutoBattle(s, Progress.Snapshot(s));
            if (both.Where((x, i) => x.exp != exp1[i]).Any()) throw new Exception("autoBattle counted the same battle twice");
        }
        Console.WriteLine($"(7a) rules OK: EXP {string.Join("/", Progress.Exp.Skip(1))}; AI L10 (95/95) learned specials tiers {tiers} + {string.Join("、", Progress.LearnedTactics(ai).Select(id => Tactics.ById(id).Name))}; mixed offers {mixed}/{total}");
    }

    // ---- (7b) 存档 v3 与第二版存档迁移 ----
    static void B()
    {
        R.InitState(17);
        var g = GameState.Current = GameState.NewGame("cao");
        for (int i = 0; i < 30; i++) { StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth(); }
        var grown = g.generals.Where(x => x.level > 1).ToList();
        if (grown.Count < 10) throw new Exception("only " + grown.Count + " generals leveled after 30 AI months");
        var text = g.ToJson();
        if (!text.StartsWith("{\"version\":3,")) throw new Exception("save version should be 3");
        var back = GameState.FromJson(text);
        if (back.ToJson() != text) throw new Exception("v3 JSON round trip mismatch");
        if (back.migratedFrom != 0) throw new Exception("v3 save flagged as migrated");
        // 第二版存档：去掉成长字段、version 2
        var v2text = System.Text.RegularExpressions.Regex.Replace(text, ",\"exp\":-?\\d+,\"level\":-?\\d+,\"specials\":(\\[[^\\]]*\\]|null),\"tactics\":(\\[[^\\]]*\\]|null),\"record\":(\\{[^}]*\\}|null)", "");
        v2text = "{\"version\":2," + v2text.Substring("{\"version\":3,".Length);
        if (v2text.IndexOf("\"exp\"") >= 0 || v2text.IndexOf("\"record\"") >= 0) throw new Exception("v2 text still has progression fields");
        var m = GameState.FromJson(v2text);
        if (m.migratedFrom != 2) throw new Exception("v2 save not flagged as migrated");
        foreach (var x in m.generals)
        {
            if (x.level != 1 || x.exp != 0 || x.specials.Count != 0 || string.Join(",", x.tactics) != string.Join(",", Progress.InnateTactics(x)) || x.record.battles != 0) throw new Exception("v2 migration wrong for " + x.name + ": " + Dump(x));
            if (Specials.Known(x).Count != 1) throw new Exception("migrated general should only have the signature special");
        }
        if (GameState.FromJson(m.ToJson()).ToJson() != m.ToJson()) throw new Exception("migrated game round trip mismatch");
        foreach (var bad in new[] { "1", "4", "null" })
        {
            string msg = "";
            try { GameState.FromJson("{\"version\":" + bad + "," + text.Substring("{\"version\":3,".Length)); } catch (Exception e) { msg = e.Message; }
            if (msg.IndexOf("旧存档") < 0) throw new Exception("version " + bad + " should be refused, got " + msg);
        }
        // 存储（PlayerPrefs）：只有第二版键时能读（迁移），存档写入第三版键并清掉第二版键
        UnityEngine.PlayerPrefs.DeleteAll();
        UnityEngine.PlayerPrefs.SetString(GameState.V2SaveKey, v2text);
        if (!GameState.HasSave || GameState.HasLegacySave) throw new Exception("v2 save should count as a save");
        var info = GameState.GetSaveInfo();
        if (info == null || info.version != 2 || info.year != g.year) throw new Exception("saveInfo for v2 save");
        var L = GameState.Load();
        if (L == null || L.migratedFrom != 2 || L.generals.Any(x => x.level != 1)) throw new Exception("load() of a v2 save failed: " + GameState.LoadError);
        GameState.Current = L;
        if (L.Save() != true || !UnityEngine.PlayerPrefs.HasKey(GameState.SaveKey) || UnityEngine.PlayerPrefs.HasKey(GameState.V2SaveKey)) throw new Exception("save() should write the v3 key and drop the v2 key");
        if (GameState.GetSaveInfo().version != 3) throw new Exception("saveInfo after save should be v3");
        UnityEngine.PlayerPrefs.SetString(GameState.LegacySaveKey, "{}");
        if (GameState.HasLegacySave) throw new Exception("legacy flag with a v3 save present");
        GameState.DeleteSave();
        if (GameState.HasSave || !GameState.HasLegacySave) throw new Exception("deleteSave() should leave only the v1 save");
        UnityEngine.PlayerPrefs.DeleteAll();
        Console.WriteLine($"(7b) save v3 OK: {text.Length} bytes, {grown.Count} generals leveled after 30 months (max L{g.generals.Max(x => x.level)}); v2 save migrated (level 1, exp 0, signature only, innate tactics)");
    }

    // ---- (7c) 可习得必杀：数据与按等阶的参考伤害倍率 ----
    static void C()
    {
        var pool = Specials.Pool();
        var byTier = new SortedDictionary<int, List<Special>>();
        foreach (var sp in pool) { if (!byTier.ContainsKey(sp.tier)) byTier[sp.tier] = new List<Special>(); byTier[sp.tier].Add(sp); }
        for (int t = 1; t <= 4; t++) if (!(byTier.ContainsKey(t) && byTier[t].Count >= 6)) throw new Exception("pool tier " + t + " has too few entries");
        foreach (var sp in pool)
        {
            if (!sp.id.StartsWith("lp:") || !sp.pool || string.IsNullOrEmpty(sp.name) || string.IsNullOrEmpty(sp.desc) || Array.IndexOf(SpecialsData.FxStyles, sp.fx) < 0) throw new Exception("bad pool entry " + sp.id);
            if (sp.stat != "war") throw new Exception("pool special should scale with 武力: " + sp.name + " (" + sp.stat + ")");
            if (Specials.ById(sp.id) != sp) throw new Exception("byId mismatch " + sp.id);
            if (sp.problems.Count > 0) throw new Exception("pool problems: " + string.Join("; ", sp.problems));
        }
        var rep = Specials.Check();
        if (!rep.ok || rep.poolTotal != pool.Count) throw new Exception("specials check with pool failed: dup names " + rep.dupNames.Count + ", dup sigs " + rep.dupSigs.Count + ", problems " + rep.problems.Count);
        // 参考局面（同 (5)）：武力 85 / 智力 60 的施展者在平原，四面敌军 3500 兵
        R.InitState(779);
        var g = GameState.Current = GameState.NewGame("cao");
        Func<int, General> dummy = i => new General { id = 9000 + i, name = "靶" + i, war = 70, intel = 60, pol = 50, faction = -1, troops = 3500, training = 60, loyalty = 80 };
        var ratios = new SortedDictionary<int, List<KeyValuePair<string, double>>>();
        var RANGE = new Dictionary<int, double[]> { { 1, new[] { 1.2, 1.9 } }, { 2, new[] { 1.45, 2.25 } }, { 3, new[] { 1.75, 2.6 } }, { 4, new[] { 2.0, 2.9 } } };
        var evs = new List<Tuple<string, int, double>>();     // 暗杀：期望值 = 成功率 × 目标兵力 + (1 − 成功率) × 失手伤害
        foreach (var sp in pool)
        {
            bool assn = sp.kind == "assassinate";
            if (!DMG_KINDS.Contains(sp.kind) && !assn) continue;
            var gen = new General { id = 9700, name = "参考", war = 85, intel = 60, pol = 50, faction = -1, troops = 0, training = 70, loyalty = 90 };
            gen.troops = gen.MaxTroops;
            var s = new BattleSetup { attacker = 0, defender = 1, src = g.cities[0], target = g.cities[11], atk = new List<General> { gen }, def = new List<General> { dummy(1), dummy(2), dummy(3), dummy(4) }, atkFood = 3000, atkGold = 0 };
            var M = new BattleModel(s);
            for (int x = 1; x <= 7; x++) for (int y = 1; y <= 7; y++) M.map[x, y] = Terrain.Plain;
            var u = M.units[0]; var es = M.units.Skip(1).Take(4).ToList();
            u.x = 4; u.y = 4; u.morale = 80;
            var P = new[] { new[] { 5, 4 }, new[] { 5, 5 }, new[] { 4, 6 }, new[] { 4, 2 } };
            for (int k = 0; k < 4; k++) { es[k].x = P[k][0]; es[k].y = P[k][1]; es[k].morale = 70; }
            M.side = 0;
            if (assn) foreach (var x in M.units) x.commander = false;     // 参考目标都不是主将（主将成功率减半）
            double normal = Sim.RefAction(M, u, es[0], sp);
            double best = 0;
            foreach (var t in M.TargetsOf(u, sp))
            {
                var pl = M.SpPlan(u, sp, t);
                if (assn)
                {
                    double p = pl.kill != null ? pl.kill.p : 0, miss = 0;
                    foreach (var h in pl.dmg) miss += Math.Min(h.unit.Troops, h.amt);
                    best = Math.Max(best, p * (pl.kill != null ? pl.kill.unit.Troops : 0) + (1 - p) * miss);
                    continue;
                }
                var per = new Dictionary<BUnit, double>(); var order = new List<BUnit>();
                foreach (var h in pl.dmg) { double c; if (!per.TryGetValue(h.unit, out c)) { c = 0; order.Add(h.unit); } per[h.unit] = Math.Min(h.unit.Troops, c + h.amt); }
                double dmg = order.Select(x => per[x]).OrderByDescending(v => v).Take(sp.kind == "rampage" ? 99 : 2).Aggregate(0.0, (a, b) => a + b);
                if (pl.drain > 0) foreach (var h in pl.dmg) if (h.primary) dmg += Math.Min(h.unit.Troops, h.amt) * pl.drain * 0.5;
                best = Math.Max(best, dmg);
            }
            if (assn) { evs.Add(Tuple.Create(sp.name, sp.tier, best / Math.Max(1, normal))); continue; }
            if (!ratios.ContainsKey(sp.tier)) ratios[sp.tier] = new List<KeyValuePair<string, double>>();
            ratios[sp.tier].Add(new KeyValuePair<string, double>(sp.name, best / Math.Max(1, normal)));
        }
        // 暗杀的期望值须落在同阶伤害型的范围内，且逐阶递增
        foreach (var o in evs)
        {
            double lo = 0.7, hi = RANGE[o.Item2][1];
            if (!(o.Item3 >= lo && o.Item3 <= hi)) throw new Exception($"pool assassinate {o.Item1} (tier {o.Item2}) expected value / normal attack {F(o.Item3, 2)} outside {lo}-{hi}");
        }
        evs = evs.OrderBy(o => o.Item2).ToList();
        for (int k = 1; k < evs.Count; k++) if (evs[k].Item2 > evs[k - 1].Item2 && !(evs[k].Item3 > evs[k - 1].Item3)) throw new Exception("pool assassinate expected value should rise with tier");
        // 每位武将至多一招可习得的暗杀
        {
            var killer = new General { id = 9701, name = "刺客", war = 95, intel = 60, pol = 50, faction = -1, troops = 3000, training = 70, loyalty = 90 };
            Progress.Ensure(killer); killer.exp = Progress.Exp[10]; Progress.Ensure(killer);
            var assnIds = pool.Where(sp => sp.kind == "assassinate").Select(sp => sp.id).ToList();
            killer.specials = new List<string> { assnIds[0] };
            if (Progress.EligibleSpecials(killer).Any(sp => sp.kind == "assassinate")) throw new Exception("a general should learn at most one pool assassinate");
            if (Progress.Learn(killer, "special", assnIds[1])) throw new Exception("learn() accepted a second pool assassinate");
        }
        var meds = new List<double>(); var line = new List<string>();
        for (int t = 1; t <= 4; t++)
        {
            var rs = ratios[t].Select(o => o.Value).ToList();
            meds.Add(Median(rs));
            line.Add($"T{t} median {F(Median(rs), 2)} [{F(rs.Min(), 2)}-{F(rs.Max(), 2)}] (n{rs.Count})");
            foreach (var o in ratios[t]) if (!(o.Value >= RANGE[t][0] && o.Value <= RANGE[t][1])) throw new Exception($"pool {o.Key} (tier {t}) reference ratio {F(o.Value, 2)} outside {RANGE[t][0]}-{RANGE[t][1]}");
        }
        for (int t = 1; t < 4; t++) if (!(meds[t] > meds[t - 1])) throw new Exception("pool tier medians should increase");
        Console.WriteLine($"(7c) learnable specials OK: {pool.Count} ({string.Join(" ", Enumerable.Range(1, 4).Select(t => "T" + t + ":" + byTier[t].Count))}), names & (kind, params) unique vs {rep.total} signature specials; reference damage / normal attack (war 85): {string.Join("; ", line)}; assassinate expected value {string.Join(" ", evs.Select(o => o.Item1 + " " + F(o.Item3, 2)))} (at most one per general)");
    }

    // ---- (7e) 成长后的压力测试：随机等级与习得，电脑完整打完，每一手后检查状态 ----
    static void E()
    {
        int battles = 0, newTac = 0, pool = 0;
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int seed = 1; seed <= 24; seed++)
        {
            R.InitState(seed * 11);
            var g = GameState.Current = GameState.NewGame("cao");
            g.player = -1;
            var r = new SeededRandom(seed);
            foreach (var x in g.generals) { Progress.Gain(x, r.Next(Progress.Exp[10] + 1)); CheckCaps(x, "7e"); }
            var pr = pairs[seed % pairs.Length];
            var src = g.CityByKey(pr[0]); var tgt = g.CityByKey(pr[1]);
            var s = Conquest.Prepare(src.owner, src, tgt, g.OfficersIn(src).Where(x => x.troops > 0).Take(5).ToList(), 3000, 0);
            var M = new BattleModel(s);
            var st = new Stats();
            FightAI(M, st, true);
            battles++; newTac += st.newTac; pool += st.pool;
            foreach (var kv in st.byTac) { int c; kinds[kv.Key] = kinds.TryGetValue(kv.Key, out c) ? c + kv.Value : kv.Value; }
            if (M.progress == null || M.progress.Count != M.units.Count || s.progress != M.progress || !s.progressDone) throw new Exception("battle did not settle experience");
            foreach (var p in M.progress) if (!(p.exp >= 10 && p.exp <= Progress.BattleCap)) throw new Exception("battle exp out of range " + p.exp);
        }
        Console.WriteLine($"(7e) leveled AI battles OK: {battles} battles, learned specials used {pool}, new tactics used {newTac} ({string.Join(" ", kinds.Select(kv => Tactics.ById(kv.Key).Name + "×" + kv.Value))})");
    }

    // ---- (7f) 多年全电脑模拟：成长开 / 关（自动结算不看等级，城池归属应完全相同）与等级分布 ----
    static readonly SortedDictionary<int, List<string>> snaps = new SortedDictionary<int, List<string>>();
    static void Fc()
    {
        var lines = new List<string>();
        try
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var owners = new List<string>();
                foreach (var on in new[] { true, false })
                {
                    Progress.enabled = on;
                    R.InitState(seed);
                    var g = GameState.Current = GameState.NewGame("liubei");
                    g.player = -1;
                    if (on) { if (!snaps.ContainsKey(0)) snaps[0] = new List<string>(); snaps[0].Add(g.ToJson()); }      // 开局（全员 1 级）
                    for (int m = 0; m < 12 * 20; m++)
                    {
                        StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth();
                        if (on && (m == 5 || m == 11 || m == 35 || m == 95)) { if (!snaps.ContainsKey(m)) snaps[m] = new List<string>(); snaps[m].Add(g.ToJson()); }
                        if (on && (m == 59 || m == 119 || m == 239))
                        {
                            var act = g.generals.Where(x => !x.dead && x.record.battles > 0).ToList();
                            foreach (var x in g.generals) CheckCaps(x, "7f");
                            var lv = act.Select(x => x.level).ToList(); var bt = act.Select(x => x.record.battles).ToList();
                            if (m == 119)
                            {
                                if (!(Median(lv) >= 2 && Median(lv) <= 6)) throw new Exception("median level of active generals at year 10 = " + Median(lv));
                                if (!act.Any(x => x.level >= 6)) throw new Exception("nobody reached level 6 by year 10");
                            }
                            lines.Add($"  seed {seed} year {(m + 1) / 12}: active {act.Count}, battles median {Median(bt)} max {bt.Max()}, {LevelHist(act)}; learned specials {act.Sum(x => x.specials.Count)}, tactics {act.Sum(x => Progress.LearnedTactics(x).Count)}");
                        }
                    }
                    owners.Add(string.Join(",", g.cities.Select(c => c.owner)));
                    if (!on && g.generals.Any(x => x.exp != 0 || x.level != 1)) throw new Exception("progression off but generals gained exp");
                }
                if (owners[0] != owners[1]) throw new Exception("seed " + seed + ": campaign outcome differs with progression on/off (autoResolve must not use levels or randomness)");
            }
        }
        finally { Progress.enabled = true; }
        Console.WriteLine("(7f) 4 seeds x 20 years, progression ON (city ownership identical with OFF):\n" + string.Join("\n", lines));
    }

    // 局面里一场进攻（同 JS：有兵的城打相邻的敌城；第 pick 个候选）
    static BattleSetup PickBattle(GameState g, Func<int, int> pick)
    {
        var cand = new List<City[]>();
        foreach (var c in g.cities)
        {
            if (c.owner < 0) continue;
            foreach (var j in c.links) { var t = g.cities[j]; if (t.owner >= 0 && t.owner != c.owner && g.OfficersIn(c).Any(x => x.troops > 500) && g.OfficersIn(t).Any()) cand.Add(new[] { c, t }); }
        }
        if (cand.Count == 0) return null;
        var ct = cand[pick(cand.Count)];
        var atk = g.OfficersIn(ct[0]).Where(x => x.troops > 0).OrderByDescending(x => x.troops).Take(5).ToList();
        return Conquest.Prepare(ct[0].owner, ct[0], ct[1], atk, 3000, 0);
    }

    // ---- (7g) 战术战斗平衡：取全电脑模拟开局 / 第 6、12 个月 / 第 3 年 / 第 8 年的局面，成长开 / 关各打一遍 ----
    static void Gb()
    {
        var rows = new List<string>();
        var results = new Dictionary<string, Stats>();
        try
        {
            foreach (var m in new[] { 0, 5, 11, 35, 95 })
            {
                foreach (var on in new[] { false, true })
                {
                    var st = new Stats();
                    var list = snaps.ContainsKey(m) ? snaps[m] : new List<string>();
                    for (int si = 0; si < list.Count; si++)
                    {
                        for (int k = 0; k < 40; k++)
                        {
                            var g = GameState.Current = GameState.FromJson(list[si]);
                            g.player = -1;
                            R.InitState(500 + si * 100 + k);
                            int kk = k, ss = si;
                            var s = PickBattle(g, n => (kk * 7 + ss) % n);
                            if (s == null) continue;
                            Progress.enabled = on;
                            try
                            {
                                var M = new BattleModel(s);
                                FightAI(M, st, false);
                                st.n++; st.days += M.day; if (M.result == 1) st.w++;
                                st.outcomes.Add(M.result + ":" + M.day + ":" + string.Join("/", M.units.Select(u => u.Troops)));
                            }
                            finally { Progress.enabled = true; }
                        }
                    }
                    results[m + ":" + (on ? "true" : "false")] = st;
                }
                var off = results[m + ":false"]; var onS = results[m + ":true"];
                Func<Stats, string> pr = o => string.Join(" ", o.poolRatio.Select(kv => "T" + kv.Key + " " + F(Median(kv.Value), 2)));
                // 局面中武力 ≥ 60 的在世武将有几位已学到必杀
                int lrnA = 0, lrnB = 0;
                foreach (var text in snaps.ContainsKey(m) ? snaps[m] : new List<string>())
                {
                    var d = (Dictionary<string, object>)MiniJson.Parse(text);
                    foreach (Dictionary<string, object> x in (List<object>)d["generals"])
                    {
                        if ((bool)x["dead"] || (double)x["war"] < 60 || (double)x["faction"] < 0) continue;
                        lrnB++;
                        var spl = x.ContainsKey("specials") ? x["specials"] as List<object> : null;
                        if (spl != null && spl.Count > 0) lrnA++;
                    }
                }
                string label = m == 0 ? "start" : m < 12 ? "month " + (m + 1) : "year " + (m + 1) / 12;
                rows.Add($"  {label} (war≥60 with a learned special {lrnA}/{lrnB}): OFF atk win {F(100.0 * off.w / off.n, 1)}% days {F(off.days / (double)off.n, 1)} dmg/battle {(int)(off.dmg / off.n)} | ON atk win {F(100.0 * onS.w / onS.n, 1)}% days {F(onS.days / (double)onS.n, 1)} dmg/battle {(int)(onS.dmg / onS.n)}; " +
                    $"ON per battle: signature {F(onS.sig / (double)onS.n, 2)}, learned specials {F(onS.pool / (double)onS.n, 2)}, new tactics {F(onS.newTac / (double)onS.n, 2)}, old tactics {F(onS.oldTac / (double)onS.n, 2)} (OFF {F(off.oldTac / (double)off.n, 2)})" +
                    (onS.pool > 0 ? "; learned special dmg / normal attack by tier: " + pr(onS) : ""));
                if (m == 0 && string.Join(",", onS.outcomes) != string.Join(",", off.outcomes)) throw new Exception("opening battles (all level 1) must be identical with progression on/off");
                double dw = Math.Abs(onS.w / (double)onS.n - off.w / (double)off.n);
                if (dw > 0.12) throw new Exception("attacker win rate shifted " + F(dw * 100, 1) + " points with progression at month " + m);
                if (Math.Abs(onS.days / (double)onS.n - off.days / (double)off.n) > 3) throw new Exception("battle length changed too much with progression");
                if (onS.dmg / onS.n > 1.35 * off.dmg / off.n) throw new Exception("damage per battle grew too much with progression");
            }
            // 学到的必杀：各等阶的实战伤害倍率（合并第 3 年与第 8 年）
            var all = new SortedDictionary<int, List<double>>();
            foreach (var key in new[] { "35:true", "95:true" }) foreach (var kv in results[key].poolRatio) { if (!all.ContainsKey(kv.Key)) all[kv.Key] = new List<double>(); all[kv.Key].AddRange(kv.Value); }
            var tierLine = string.Join(", ", all.Select(kv => $"T{kv.Key} {F(Median(kv.Value), 2)} (n{kv.Value.Count})"));
            var assn = new List<double>(); int assnKill = 0;
            foreach (var key in new[] { 0, 5, 11, 35, 95 }.SelectMany(mm => new[] { mm + ":false", mm + ":true" })) { assn.AddRange(results[key].assn); assnKill += results[key].assnKill; }
            var assnLine = assn.Count > 0 ? $"; learned assassinate realised dmg / normal attack mean {F(assn.Sum() / assn.Count, 2)} (n{assn.Count}, {assnKill} kills)" : "";
            if (assn.Count >= 10 && !(assn.Sum() / assn.Count <= 2.2)) throw new Exception("learned assassinate too strong in battle: " + assnLine);
            var LO = new[] { 0, 1.1, 1.3, 1.6, 1.8 }; var HI = new[] { 0, 2.0, 2.3, 2.7, 3.1 };
            foreach (var kv in all) if (kv.Value.Count >= 10 && !(Median(kv.Value) >= LO[kv.Key] && Median(kv.Value) <= HI[kv.Key])) throw new Exception("in-battle learned special ratio for tier " + kv.Key + " = " + F(Median(kv.Value), 2));
            Console.WriteLine("(7g) tactical balance, 160 battles per row (same seeds and armies, progression OFF vs ON):\n" + string.Join("\n", rows) + "\n  learned special damage / normal attack in battle: " + tierLine + assnLine);
        }
        finally { Progress.enabled = true; }
    }

    // ---- (7h) 成长的分量：第 8 年局面里，攻方保留成长、守方打回 1 级（及反过来）的胜率与战损比变化 ----
    static void H()
    {
        Action<General> strip = x => { x.exp = 0; x.level = 1; x.specials = new List<string>(); x.tactics = Progress.InnateTactics(x); };
        var outD = new Dictionary<string, double[]>();
        foreach (var mode in new[] { "both", "atkOnly", "defOnly" })
        {
            int n = 0, w = 0; double lossA = 0, lossD = 0;
            var list = snaps.ContainsKey(95) ? snaps[95] : new List<string>();
            for (int si = 0; si < list.Count; si++)
            {
                for (int k = 0; k < 80; k++)
                {
                    var g = GameState.Current = GameState.FromJson(list[si]);
                    g.player = -1;
                    R.InitState(900 + si * 100 + k);
                    int kk = k, ss = si;
                    var s = PickBattle(g, c => (kk * 5 + ss) % c);
                    if (s == null) continue;
                    if (mode == "atkOnly") foreach (var x in s.def) strip(x);
                    if (mode == "defOnly") foreach (var x in s.atk) strip(x);
                    var M = new BattleModel(s);
                    var t0s = M.units.Select(u => u.Troops).ToList();
                    FightAI(M, new Stats(), false);
                    n++; if (M.result == 1) w++;
                    for (int i = 0; i < M.units.Count; i++) { var u = M.units[i]; double l = Math.Max(0, t0s[i] - Math.Max(0, u.Troops)); if (u.side == 0) lossA += l; else lossD += l; }
                }
            }
            outD[mode] = new[] { n, w / (double)Math.Max(1, n), lossD / Math.Max(1, lossA) };
        }
        double shiftA = outD["atkOnly"][1] - outD["both"][1], shiftD = outD["both"][1] - outD["defOnly"][1], swing = outD["atkOnly"][1] - outD["defOnly"][1];
        Func<double, string> pct = v => F(100 * v, 1) + "%";
        Console.WriteLine($"(7h) year-8 armies ({outD["both"][0]} battles each): attacker win {pct(outD["both"][1])} both progressed, {pct(outD["atkOnly"][1])} defenders reset to L1, {pct(outD["defOnly"][1])} attackers reset to L1 " +
            $"(swing {F(100 * swing, 1)} pp); defender / attacker losses {F(outD["both"][2], 2)} / {F(outD["atkOnly"][2], 2)} / {F(outD["defOnly"][2], 2)}");
        if (!(shiftA >= -0.03 && shiftD >= -0.03)) throw new Exception("progression should not hurt the progressed side");
        if (shiftA > 0.35 || shiftD > 0.35) throw new Exception("progression decides battles too much: shifts " + F(shiftA, 2) + " / " + F(shiftD, 2));
        // 成长须看得出来：只有攻方成长 vs 只有守方成长，攻方胜率差 ≥ 5 个百分点；成长一方的战损比各好 ≥ 8%
        if (!(swing >= 0.05)) throw new Exception("progression barely matters: attacker win swing " + F(100 * swing, 1) + " pp");
        if (!(outD["atkOnly"][2] >= 1.08 * outD["both"][2] && outD["both"][2] >= 1.08 * outD["defOnly"][2])) throw new Exception("progressed side should trade troops clearly better");
        if (outD["atkOnly"][2] > 1.8 * outD["both"][2] || outD["both"][2] > 1.8 * outD["defOnly"][2]) throw new Exception("progression trades troops too much better");
    }
}
