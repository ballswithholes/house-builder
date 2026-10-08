// 无头测试的共用工具（每个 Harness*.cs 都与本文件、UnityStub.cs 和规则文件一起编译）。
using System;

// SeededRandom（= SG.SeededRandom）已移入规则代码 Data/Defs.cs（Sanguo.SeededRandom）：战场地形与必杀技兜底生成器也要用。

// 生成数据的校验摘要：与导出工具（sanguozhi2-web/tools/export-unity-data.js 的 class Hash）逐字节相同的 FNV-1a 32 位。
//   S(字符串) = UTF-8 字节 + 0x00（null = 0x01 0x00）；I(整数) = 十进制文本；N(浮点) = floor(v × 10⁴ + 0.5) 的十进制文本
public sealed class DataHash
{
    uint h = 2166136261u;
    public int Count;
    void Bytes(byte[] b) { unchecked { foreach (var x in b) { h ^= x; h *= 16777619u; } } }
    public void S(string s)
    {
        Count++;
        if (s == null) { Bytes(new byte[] { 1, 0 }); return; }
        Bytes(System.Text.Encoding.UTF8.GetBytes(s)); Bytes(new byte[] { 0 });
    }
    public void I(int v) { S(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    public void I(int? v) { if (v.HasValue) I(v.Value); else S(null); }
    public void N(float v) { S(((long)Math.Floor((double)v * 1e4 + 0.5)).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    public void NA(float[] a) { I(a.Length); foreach (var v in a) N(v); }
    public void SA(string[] a) { I(a.Length); foreach (var v in a) S(v); }
    public uint Value => h;
    public string Hex => "0x" + h.ToString("x8");
}

// 简单的检查计数
public static class Check
{
    public static int Fails;
    public static bool That(bool cond, string msg) { if (!cond) { Fails++; Console.WriteLine("  FAIL " + msg); } return cond; }
}

// ======================================================================= 规则测试共用 --
// tests/sim.js 中几组测试共用的检查与战斗驱动（Harness.cs、HarnessSpecials.cs 使用）。
public static class Sim
{
    // JS 的 toFixed(n)
    // （按双精度的精确值四舍五入：14.95 实为 14.9499… → "14.9"；Mono 的 "F1" 会给出 "15.0"）
    public static string F(double v, int n)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) >= 1e20) return v.ToString(inv);
        var d = decimal.Parse(v.ToString("G17", inv), System.Globalization.NumberStyles.Float, inv);
        return Math.Round(d, n, MidpointRounding.AwayFromZero).ToString("F" + n, inv);
    }
    public static double Median(System.Collections.Generic.List<double> a)
    {
        if (a.Count == 0) return 0;
        var b = new System.Collections.Generic.List<double>(a); b.Sort();
        return b[b.Count >> 1];
    }
    public static double Avg(System.Collections.Generic.List<double> a) { double s = 0; foreach (var x in a) s += x; return s / Math.Max(1, a.Count); }
    public static int Cmp(string a, string b) { return string.CompareOrdinal(a, b); }

    // 一致性检查（全电脑模拟与随机玩家模拟共用）
    public static void Consistency(Sanguo.GameState g)
    {
        foreach (var c in g.cities)
        {
            if (c.owner >= 0 && !g.factions[c.owner].alive) throw new Exception("dead faction owns city " + c.name);
            if (c.gold < 0 || c.food < 0) throw new Exception("negative res " + c.name + " gold=" + c.gold + " food=" + c.food);
        }
        foreach (var gen in g.generals)
        {
            if (gen.troops < 0) throw new Exception("neg troops " + gen.name);
            if (gen.faction >= 0 && !g.factions[gen.faction].alive) throw new Exception("gen in dead faction " + gen.name);
        }
        foreach (var f in g.factions)
        {
            if (!f.alive) continue;
            var r = g.generals[f.ruler];
            if (r.dead || r.faction != f.id) throw new Exception("bad ruler " + f.name + " -> " + r.name + " dead=" + r.dead + " fac=" + r.faction);
        }
    }

    public static readonly string[][] Pairs = { new[] { "chenliu", "luoyang" }, new[] { "pingyuan", "nanpi" }, new[] { "changsha", "jiangling" }, new[] { "beiping", "nanpi" }, new[] { "wan", "xiangyang" } };

    // 必杀技结算后的状态检查：无 NaN、无负兵力、士气 0..100、存活部队不越界不重叠、燃烧 / 加成合法
    public static void CheckBattleState(Sanguo.BattleModel M, string where)
    {
        var occ = new System.Collections.Generic.HashSet<int>();
        foreach (var u in M.units)
        {
            if (u.Troops < 0) throw new Exception(where + ": bad troops " + u.gen.name + " " + u.Troops);
            if (u.morale < 0 || u.morale > 100) throw new Exception(where + ": bad morale " + u.gen.name + " " + u.morale);
            if (u.confused < 0) throw new Exception(where + ": bad confused " + u.gen.name + " " + u.confused);
            if (u.alive)
            {
                if (!M.Passable(u.x, u.y)) throw new Exception(where + ": unit on impassable tile " + u.gen.name + " " + u.x + "," + u.y);
                if (!occ.Add(u.x * 100 + u.y)) throw new Exception(where + ": two units on " + u.x + "," + u.y);
                if (!(u.Troops >= 50)) throw new Exception(where + ": alive unit should have routed " + u.gen.name + " troops " + u.Troops + " morale " + u.morale);
            }
            foreach (var m in u.mods)
            {
                if (!(m.days >= 1)) throw new Exception(where + ": mod with days " + m.days);
                foreach (var v in new[] { m.atk, m.def, m.counter }) if (v.HasValue && !(!double.IsNaN(v.Value) && !double.IsInfinity(v.Value) && v.Value > 0)) throw new Exception(where + ": bad mod " + v);
                if (m.dot.HasValue && !(m.dot.Value >= 0 && m.dot.Value < 1)) throw new Exception(where + ": bad dot " + m.dot);
            }
            if (u.Mobility < 1) throw new Exception(where + ": bad mobility " + u.Mobility);
        }
        for (int x = 0; x < M.W; x++) for (int y = 0; y < M.H; y++) if (M.burning[x, y] < 0) throw new Exception(where + ": bad burning " + M.burning[x, y]);
        if (M.ap < 0) throw new Exception(where + ": bad ap " + M.ap);
    }
    public static void CheckSpecialResult(Sanguo.SpecialResult res, string where)
    {
        // 被必杀技波及的敌军：士气归零者必须已溃散
        string caster = res.sp != null ? res.sp.gen : "";
        foreach (var x in res.affected)
            if (x.alive && x.gen.name != caster && res.healed.TrueForAll(h => h.unit != x) && !res.buffed.Contains(x) && !res.refreshed.Contains(x) && !(x.morale > 0))
                throw new Exception(where + ": affected enemy alive with morale 0: " + x.gen.name);
        if (res.dmg < 0 || res.heal < 0 || res.gained < 0) throw new Exception(where + ": res dmg/heal/gained " + res.dmg + "/" + res.heal + "/" + res.gained);
        foreach (var h in res.hits) if (h.dmg < 0) throw new Exception(where + ": hit dmg " + h.dmg);
        foreach (var h in res.healed) if (h.amount < 0) throw new Exception(where + ": heal " + h.amount);
    }

    // 倍率的分母：武力系招式 = 对同一目标的一次普通攻击（期望）；智力 / 政治系 = 普通攻击与火计期望中较大者
    public static double RefAction(Sanguo.BattleModel M, Sanguo.BUnit u, Sanguo.BUnit t, Sanguo.Special sp)
    {
        double rf = M.AtkExpect(u, t);
        if (sp.stat != "war" && u.gen.intel >= 50)
        {
            var tt = M.map[t.x, t.y];
            double terr = tt == Sanguo.Terrain.Forest ? 1.8 : (tt == Sanguo.Terrain.Castle || tt == Sanguo.Terrain.Gate) ? 0.6 : 1;
            double fire = Clamp(0.5 + (u.gen.intel - t.gen.intel) / 110.0, 0.12, 0.92) * (260 + u.gen.intel * 11) * terr * Clamp(t.Troops / 2500.0, 0.4, 1.4) / M.ModK(t, "def");
            rf = Math.Max(rf, fire);
        }
        return Math.Min(rf, t.Troops);
    }
    public static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }

    public sealed class Stats
    {
        public int specials;
        public System.Collections.Generic.List<double> spDmg = new System.Collections.Generic.List<double>(), atkDmg = new System.Collections.Generic.List<double>(),
            ratio = new System.Collections.Generic.List<double>(), ratio1 = new System.Collections.Generic.List<double>(), assassin = new System.Collections.Generic.List<double>();
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<double>> byKind = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<double>>();
        public System.Collections.Generic.List<string> kindOrder = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.Dictionary<string, int> kinds = new System.Collections.Generic.Dictionary<string, int>();
    }
    public sealed class BattleRun { public Sanguo.GameState g; public Sanguo.BattleSetup s; public Sanguo.BattleModel M; }

    // 电脑对电脑完整打完一场战术战斗（与战斗控制器的电脑回合相同：按 PlanFor / PlanAction 行动；
    // 疾行令友军再动时，同一回合内再扫一遍可行动的部队）
    public static BattleRun RunBattle(int seed, string[] pr, Stats stats)
    {
        UnityEngine.Random.InitState(seed * 7);
        var g = Sanguo.GameState.Current = Sanguo.GameState.NewGame("cao");
        var src = g.CityByKey(pr[0]); var tgt = g.CityByKey(pr[1]);
        var atk = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Take(System.Linq.Enumerable.Where(g.OfficersIn(src), x => x.troops > 0), 5));
        var s = Sanguo.Conquest.Prepare(src.owner, src, tgt, atk, 3000, 0);
        var M = new Sanguo.BattleModel(s);
        int guard = 0;
        while (M.result == 0 && guard++ < 2000)
        {
            for (int pass = 0; pass < 4 && M.result == 0; pass++)
            {
                var order = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(M.Alive(M.side), M.CanAct));
                if (order.Count == 0) break;
                foreach (var u in order)
                {
                    if (!M.CanAct(u) || M.result != 0) continue;
                    var plan = M.PlanFor(u);
                    if (plan.move.HasValue)
                    {
                        var to = plan.move.Value;
                        var p = M.PathTo(u, to);
                        if (p.Count == 0 || p[0].x != u.x || p[0].y != u.y || p[p.Count - 1] != to) throw new Exception("bad path from " + u.x + "," + u.y);
                        for (int i = 1; i < p.Count; i++)
                        {
                            if (Math.Abs(p[i].x - p[i - 1].x) + Math.Abs(p[i].y - p[i - 1].y) != 1) throw new Exception("path not contiguous");
                            if (!M.Passable(p[i].x, p[i].y)) throw new Exception("path through impassable tile");
                        }
                        if (M.UnitAt(to.x, to.y) != null) throw new Exception("move onto unit");
                        M.Move(u, to);
                    }
                    if (M.result != 0) break;
                    var act = M.PlanAction(u);
                    if (act.kind == "attack") { var r = M.Attack(u, act.target); if (stats != null) stats.atkDmg.Add(r.dmg); }
                    else if (act.kind == "tactic") M.UseTactic(u, act.tactic, act.target);
                    else if (act.kind == "duel") M.Duel(u, act.target);
                    else if (act.kind == "special")
                    {
                        var sp = M.SpecialOf(u);
                        var tg = act.target;
                        double rf = 0;
                        if (tg != null && tg.side != u.side) rf = RefAction(M, u, tg, sp);
                        var holder = M.UnitAt(M.castle.x, M.castle.y);   // 本城上的守军（必杀技不得把他挪走）
                        var res = M.UseSpecial(u, act.target);
                        if (res.sp == null || !u.specialUsed) throw new Exception("planned special not used: " + u.gen.name + " " + res.why);
                        if (holder != null && holder.side == 1 && holder.alive && (holder.x != M.castle.x || holder.y != M.castle.y)) throw new Exception(sp.name + " moved " + holder.gen.name + " off the castle");
                        CheckSpecialResult(res, "battle " + seed + " " + u.gen.name);
                        CheckBattleState(M, "battle " + seed + " " + u.gen.name + " " + sp.name);
                        if (stats != null)
                        {
                            stats.specials++;
                            if (!stats.kinds.ContainsKey(sp.kind)) { stats.kinds[sp.kind] = 0; stats.kindOrder.Add(sp.kind); }
                            stats.kinds[sp.kind]++;
                            if (res.dmg > 0 && sp.kind != "assassinate" && sp.kind != "poison" && sp.kind != "roar" && sp.kind != "scheme")
                            {
                                stats.spDmg.Add(res.dmg);
                                if (rf > 0)
                                {
                                    var n = new System.Collections.Generic.HashSet<Sanguo.BUnit>(System.Linq.Enumerable.Select(res.hits, h => h.unit)).Count;
                                    stats.ratio.Add(res.dmg / rf); stats.ratio1.Add(res.dmg / (double)n / rf);
                                    if (!stats.byKind.ContainsKey(sp.kind)) stats.byKind[sp.kind] = new System.Collections.Generic.List<double>();
                                    stats.byKind[sp.kind].Add(res.dmg / rf);
                                }
                            }
                            if (sp.kind == "assassinate") stats.assassin.Add(res.killed != null ? 1 : 0);
                        }
                    }
                    M.Spend(u);
                }
            }
            if (M.result == 0) M.EndSide();
        }
        if (M.result == 0) throw new Exception("battle never ended");
        foreach (var u in M.units) if (u.Troops < 0) throw new Exception("negative unit troops");
        return new BattleRun { g = g, s = s, M = M };
    }
}
