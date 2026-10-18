// 第三版计策测试（tests/sim.js (7d) 的 C# 版）与参考结果比对：
//   (ref) Tactics.All() 的说明（Explain / Desc）与摘要（Rules）对 ref/tactics-ref.txt 逐字与 JS 相同；
//   (7d) 计策目录（18 种、编号 / 名称唯一、原四计不变、等阶与需求智力）与新计策规则：水计须临河并灭火、冷却「N 日」的可用间隔、
//        结束战斗的火计仍给施计者经验、无兵上阵者不计经验、伏兵须靠近林地 / 山丘、补给耗粮且回复不超过出阵兵力、
//        挑拨 / 伪报的削弱持续日数、连环波及周围、奇门遁甲机动 +2、落雷不受城防减免；原四计与第一版逐项相同（80 例）；
//   (menu) 控制层辅助（Tactics.MenuItems / AutoTarget / Perform / FxTargets、Specials.MenuInfo / PickList / UsedIn）与规则层一致。
// 「必定成功」用替身的 Random.ValueOverride = () => 0（= 网页版 SG.Random.value = () => 0）。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;
using R = UnityEngine.Random;

class PT2
{
    static int fails;
    static int Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Progress.enabled = true;
        BattleModel.specialsEnabled = true;
        try { RefCheck(); } catch (Exception e) { fails++; Console.WriteLine("(ref) tactics FAILED: " + e); }
        try { D(); } catch (Exception e) { fails++; R.ValueOverride = null; Console.WriteLine("(7d) tactics FAILED: " + e); }
        try { Menus(); } catch (Exception e) { fails++; R.ValueOverride = null; Console.WriteLine("(menu) helpers FAILED: " + e); }
        try { ToFixedCheck(); } catch (Exception e) { fails++; Console.WriteLine("(fmt) toFixed FAILED: " + e); }
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }

    // ---- (fmt) 游戏代码里的 JS toFixed（Specials.JsToFixed，计策说明用）：对 node 实测的易错值逐字相同，并与测试用的 BigInteger 版 Sim.F 交叉比对 ----
    static void ToFixedCheck()
    {
        var node = new[] {   // (v).toFixed(n) 在 node 下的结果
            new object[] { 10.85, 1, "10.8" }, new object[] { 14.95, 1, "14.9" }, new object[] { 1.005, 2, "1.00" }, new object[] { 1.45, 1, "1.4" }, new object[] { 2.5, 0, "3" },
            new object[] { -2.5, 0, "-3" }, new object[] { 0.5, 0, "1" }, new object[] { -0.5, 0, "-1" }, new object[] { -0.001, 2, "-0.00" }, new object[] { -0.0, 2, "0.00" },
            new object[] { 0.03 * 100, 2, "3.00" }, new object[] { 0.1 * 100, 2, "10.00" }, new object[] { 9.995, 2, "9.99" }, new object[] { 99.995, 2, "100.00" },
            new object[] { 123456789.125, 2, "123456789.13" }, new object[] { 0.045, 2, "0.04" }, new object[] { 1.255, 2, "1.25" }, new object[] { 8.345, 2, "8.35" },
            new object[] { 1e20, 2, "100000000000000000000.00" }, new object[] { 5e-324, 3, "0.000" }, new object[] { 0.000001, 7, "0.0000010" }, new object[] { 1e-7, 6, "0.000000" },
            new object[] { -1e-7, 6, "-0.000000" }, new object[] { 2.675, 2, "2.67" }, new object[] { 0.615, 2, "0.61" }, new object[] { 1.0000000000000002, 20, "1.00000000000000022204" } };
        int bad = 0;
        foreach (var c in node)
        {
            var got = Specials.JsToFixed((double)c[0], (int)c[1]);
            if (got != (string)c[2]) { bad++; Console.WriteLine($"  MISMATCH toFixed({((double)c[0]).ToString("R")}, {c[1]}): got {got}, node {c[2]}"); }
        }
        var rnd = new System.Random(20261018); int n = 0;
        for (int i = 0; i < 20000; i++)
        {
            double v = (rnd.NextDouble() - 0.5) * 2 * Math.Pow(10, rnd.Next(14) - 6);
            if (i % 3 == 0) v = Math.Round(v * 1000) / 1000 + (i % 2 == 1 ? 0.0005 : 0.005);   // 靠近舍入中点
            int d = i % 7; n++;
            var a = Specials.JsToFixed(v, d); var b = Sim.F(v, d);
            if (a != b) { bad++; if (bad <= 8) Console.WriteLine($"  MISMATCH toFixed({v.ToString("R")}, {d}): JsToFixed {a}, Sim.F {b}"); }
        }
        if (bad > 0) throw new Exception(bad + " toFixed mismatches");
        Console.WriteLine($"(fmt) Specials.JsToFixed OK: {node.Length} node cases, {n} random values identical to Sim.F (BigInteger)");
    }

    static void RefCheck()
    {
        var want = System.IO.File.ReadAllLines("ref/tactics-ref.txt").Where(l => !l.StartsWith("#") && l.Trim().Length > 0).ToList();
        var all = Tactics.All();
        int bad = 0;
        Func<string, string> clean = s => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');
        for (int i = 0; i < Math.Max(want.Count, all.Count); i++)
        {
            var t = i < all.Count ? all[i] : null;
            var got = t == null ? "(none)" : string.Join("\t", new[] { t.Id, t.Name, t.Tier.ToString(), t.ReqInt.ToString(), t.Desc, Tactics.Rules(t) }.Select(clean));
            var w = i < want.Count ? want[i] : "(none)";
            if (got != w) { bad++; if (bad <= 4) Console.WriteLine($"  MISMATCH tactics line {i + 1}:\n    got  {got}\n    want {w}"); }
            if (t != null && t.IsNew && Tactics.Explain(t) != t.Desc) { bad++; Console.WriteLine("  Desc != Explain for " + t.Id); }
        }
        Console.WriteLine($"(ref) {all.Count} tactics compared with JS (desc and rules), {bad} mismatches");
        if (bad > 0) throw new Exception("tactics differ from the JS reference");
    }

    // 控制层辅助（Tactics.MenuItems / AutoTarget / Perform、Specials.MenuInfo / PickList / UsedIn）与规则层一致
    static void Menus()
    {
        R.InitState(4321);
        g = GameState.Current = GameState.NewGame("cao");
        var f = TField(2, 1); var M = f.M; var u = f.u;
        var rows = Tactics.MenuItems(M, u);
        if (rows.Count != M.KnownTactics(u).Count || rows.Count != 18) throw new Exception("menu rows " + rows.Count);   // 智力 96、会全部新计策
        foreach (var r in rows)
        {
            string w; bool ok = M.TacticUsable(u, r.tactic, out w) && M.TacticTargets(u, r.tactic).Count > 0;
            if (r.ok != ok || (!ok && string.IsNullOrEmpty(r.why))) throw new Exception("menu row disagrees with the rules: " + r.tactic.Id);
            if (r.tactic.IsNew && (r.tierLabel != Tactics.TierNameOf(r.tactic.Tier) || r.summary.Length == 0 || r.summaryShort.Split(new[] { " · " }, StringSplitOptions.None).Length > 3)) throw new Exception("menu row text " + r.tactic.Id);
        }
        // 落石须立于山丘：菜单灰显并注明原因
        var rock = rows.First(r => r.tactic.Id == "rockfall");
        if (rock.ok || rock.why != "须立于山丘") throw new Exception("rockfall row: " + rock.why);
        // 自身为中心的计策不必选目标；单一目标直接选中
        if (Tactics.AutoTarget(M, u, Tactics.ById("bazhen")) != u || Tactics.AutoTarget(M, u, Tactics.ById("inspire")) != u) throw new Exception("AutoTarget self");
        var res = Tactics.Perform(M, u, Tactics.ById("inspire"), u);
        if (!res.success || res.tactic.Id != "inspire" || res.kind != "inspire") throw new Exception("Perform(inspire)");
        var fire = R.ValueOverride; R.ValueOverride = () => 0;
        try { res = Tactics.Perform(M, u, Tactics.ById("fire"), f.foes[0]); } finally { R.ValueOverride = fire; }
        if (!res.success || res.hits.Count != 1 || res.hits[0].dmg != res.dmg || Tactics.FxTargets(res, u)[0] != f.foes[0]) throw new Exception("Perform(fire) result shape");
        if (Tactics.CurseWord("rumor") != "动摇" || Tactics.BuffWord("bazhen") != "八阵护体" || Tactics.CurseWord("fire") != null) throw new Exception("float words");
        // 必杀：会多招时菜单项与选招菜单
        var gen = u.gen;
        gen.war = 95; gen.exp = Progress.Exp[10]; gen.specials = new List<string>(); Progress.Ensure(gen);
        gen.specials = Specials.Pool().Where(sp => sp.kind == "smite").Take(2).Select(sp => sp.id).ToList();
        u.x = f.foes[0].x - 1; u.y = f.foes[0].y;
        var info = Specials.MenuInfo(M, u);
        var pick = Specials.PickList(M, u);
        if (info.list.Count != 3 || pick.Count != 3 || pick[0].tierLabel != "专属" || pick[1].tierLabel != Specials.TierNameOf(pick[1].sp.tier) || info.usable != pick.Count(r => r.ok)) throw new Exception("special menu: " + info.list.Count + "/" + info.usable);
        var learned = pick[1].sp;
        u.spPick = learned.id;
        if (M.SpecialOf(u) != learned) throw new Exception("spPick should choose the special");
        var r2 = M.UseSpecial(u, null);
        if (r2.sp != learned || !Specials.UsedIn(u, learned) || Specials.UsedIn(u, pick[0].sp) || u.specialUsed || u.spPick != null) throw new Exception("using a learned special should only mark that one");
        string why; if (M.SpecialUsable(u, learned.id, out why) || why != "本战已施展") throw new Exception("learned special usable twice: " + why);
        Console.WriteLine($"(menu) helpers OK: {rows.Count} tactic rows ({rows.Count(r => r.ok)} usable), {info.list.Count} specials ({info.usable} usable) for a level-10 general");
    }

    sealed class Field { public BattleModel M; public BUnit u; public List<BUnit> foes, friends; }
    static GameState g;
    static Field TField(int nFoes, int nFriends = 0)
    {
        var caster = new General { id = 9800, name = "施计", war = 60, intel = 96, pol = 60, faction = -1, troops = 4000, training = 70, loyalty = 90 };
        Progress.Ensure(caster);
        caster.tactics = Progress.InnateTactics(caster).Concat(Tactics.Learnable().Select(t => t.Id)).ToList();
        var mine = new List<General> { caster };
        for (int i = 0; i < nFriends; i++) mine.Add(new General { id = 9810 + i, name = "友" + i, war = 60, intel = 40, pol = 40, faction = -1, troops = 2500, training = 60, loyalty = 80 });
        var theirs = new List<General>();
        for (int i = 0; i < nFoes; i++) theirs.Add(new General { id = 9820 + i, name = "敌" + i, war = 60, intel = 40, pol = 40, faction = -1, troops = 3500, training = 60, loyalty = 80 });
        var s = new BattleSetup { attacker = 0, defender = 1, src = g.cities[0], target = g.cities[11], atk = mine, def = theirs, atkFood = 9000, atkGold = 0 };
        var M = new BattleModel(s);
        for (int x = 0; x < M.W; x++) for (int y = 0; y < M.H; y++) M.map[x, y] = Terrain.Plain;
        M.castle = new UnityEngine.Vector2Int(M.W - 1, M.H - 1);
        var u = M.units[0];
        var foes = M.units.Where(x => x.side == 1).ToList(); var friends = M.units.Where(x => x.side == 0 && x != u).ToList();
        u.x = 4; u.y = 5;
        for (int k = 0; k < foes.Count; k++) { foes[k].x = 6 + (k % 2); foes[k].y = 4 + (k >> 1); foes[k].commander = false; }
        for (int k = 0; k < friends.Count; k++) { friends[k].x = 4; friends[k].y = 6 + k; friends[k].commander = false; }
        foreach (var x in M.units) { x.morale = 80; x.commander = false; }
        M.StartSide(0);
        return new Field { M = M, u = u, foes = foes, friends = friends };
    }
    static T Sure<T>(Func<T> f) { R.ValueOverride = () => 0; try { return f(); } finally { R.ValueOverride = null; } }
    static bool Usable(BattleModel M, BUnit u, TacticDef t) { string w; return M.TacticUsable(u, t, out w); }
    static string Why(BattleModel M, BUnit u, TacticDef t) { string w; M.TacticUsable(u, t, out w); return w ?? ""; }

    // 第一版的原四计（规则原样；与 BattleModel.UseTactic 比对）
    static KeyValuePair<bool, int> UseTacticV1(BattleModel M, BUnit u, TacticDef t, BUnit target)
    {
        if (t.Kind == TacticKind.Inspire)
        {
            foreach (var o in M.units) if (o.alive && o.side == u.side && BattleModel.Dist(o, u) <= 1) o.morale = Math.Min(100, o.morale + 12 + u.gen.intel / 10);
            return new KeyValuePair<bool, int>(true, 0);
        }
        double p = Sim.Clamp(0.5 + (u.gen.intel - target.gen.intel) / 110.0, 0.12, 0.92);
        if (R.value > p) return new KeyValuePair<bool, int>(false, 0);
        int dmg = 0;
        if (t.Kind == TacticKind.Confuse) { target.confused = 2; target.acted = true; }
        else
        {
            double baseD = (t.Kind == TacticKind.Fire ? 260 : 340) + u.gen.intel * (t.Kind == TacticKind.Fire ? 11 : 13);
            var tt = M.map[target.x, target.y];
            if (t.Kind == TacticKind.Fire) { if (tt == Terrain.Forest) baseD *= 1.8; if (tt == Terrain.Castle || tt == Terrain.Gate) baseD *= 0.6; M.burning[target.x, target.y] = 2; }
            baseD *= Sim.Clamp(target.Troops / 2500.0, 0.4, 1.4);
            baseD /= M.ModK(target, "def");
            dmg = Math.Min(target.Troops, (int)Math.Round(baseD * R.Range(0.85f, 1.15f)));
            target.Troops -= dmg; target.morale = Math.Max(0, target.morale - 10);
            M.GetType().GetMethod("CheckRout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(M, new object[] { target });
        }
        M.CheckEnd();
        return new KeyValuePair<bool, int>(true, dmg);
    }

    static void D()
    {
        var all = Tactics.All();
        if (all.Count < 16) throw new Exception("tactics catalogue too small: " + all.Count);
        if (all.Select(t => t.Id).Distinct().Count() != all.Count || all.Select(t => t.Name).Distinct().Count() != all.Count) throw new Exception("tactic ids / names not unique");
        var ids = new[] { "fire", "rockfall", "confuse", "inspire" };
        for (int i = 0; i < 4; i++)
        {
            var t = Tactics.ById(ids[i]); var d = Defs.Tactics[i];
            if (t == null || t.Kind != d.Kind || t.Name != d.Name || t.ReqInt != d.ReqInt || t.Power != d.Power || t.IsNew) throw new Exception("original tactic changed: " + ids[i]);
        }
        var learn = Tactics.Learnable();
        for (int tier = 1; tier <= 4; tier++) if (learn.Count(x => x.Tier == tier) < 3) throw new Exception("too few learnable tactics in tier " + tier);
        foreach (var t in learn)
        {
            if (!t.IsNew || Array.IndexOf(SpecialsData.FxStyles, t.Fx) < 0 || !System.Text.RegularExpressions.Regex.IsMatch(t.Color ?? "", "^#[0-9a-fA-F]{6}$") || string.IsNullOrEmpty(t.Desc) || string.IsNullOrEmpty(Tactics.Rules(t))) throw new Exception("bad tactic entry " + t.Id);
            if (t.ReqInt != new[] { 0, 50, 65, 80, 92 }[t.Tier]) throw new Exception("tactic " + t.Id + " reqInt should match its tier threshold");
        }
        // 小战场：施计者（智力 96，会全部新计策）对若干敌军
        R.InitState(4321);
        g = GameState.Current = GameState.NewGame("cao");
        Func<string, TacticDef> T = id => Tactics.ById(id);
        var notes = new List<string>();
        // 水计：须临河
        {
            var f = TField(2); var M = f.M; var u = f.u; var foes = f.foes;
            if (Usable(M, u, T("water")) || Why(M, u, T("water")).IndexOf("临河") < 0) throw new Exception("water should need a river");
            M.map[foes[0].x, foes[0].y + 1] = Terrain.River;
            M.burning[foes[0].x, foes[0].y] = 2;
            if (!Usable(M, u, T("water"))) throw new Exception("water should be usable next to a river");
            var r = Sure(() => M.UseNewTactic(u, T("water"), foes[0]));
            if (!r.success || r.hits.Count < 2 || r.doused.Count < 1) throw new Exception("water: success " + r.success + " hits " + r.hits.Count + " doused " + r.doused.Count);
            // 冷却：「冷却 N 日」= 用后的 N 个本方回合不能再用（第 d 日用，第 d+N+1 日起可用）
            int cdW = T("water").Cd;
            if (!(cdW >= 1)) throw new Exception("water should have a cooldown");
            if (Usable(M, u, T("water"))) throw new Exception("water should be on cooldown the same day");
            for (int k = 1; k <= cdW; k++)
            {
                M.EndSide(); M.EndSide();
                if (Usable(M, u, T("water"))) throw new Exception("water (冷却" + cdW + "日) usable again after only " + k + " day(s)");
            }
            M.EndSide(); M.EndSide();
            if (!Usable(M, u, T("water"))) throw new Exception("water cooldown (" + cdW + " day) should be over after " + (cdW + 1) + " days: " + Why(M, u, T("water")));
            notes.Add($"水计 hits {r.hits.Count} dmg {r.dmg}, 冷却{cdW}日 = next use day +{cdW + 1}");
        }
        // 每种有冷却的计策：每个本方回合都尝试施展（必定成功），可用的日子间隔 = 冷却 + 1
        {
            var pat = new List<string>();
            foreach (var t in learn)
            {
                if (!(t.Cd > 0)) continue;
                var f = TField(2, 1); var M = f.M; var u = f.u; var foes = f.foes;
                foes[0].x = 5; foes[0].y = 5; foes[1].x = 6; foes[1].y = 5;
                if (t.Need == "river") M.map[foes[0].x, foes[0].y + 1] = Terrain.River;
                if (t.Need == "cover") M.map[foes[0].x, foes[0].y + 1] = Terrain.Forest;
                var line = "";
                for (int d = 0; d < 7; d++)
                {
                    if (M.result != 0) throw new Exception("cooldown probe battle ended early (" + t.Name + ")");
                    foreach (var x in M.units) { x.Troops = x.side == 1 ? 3500 : x == u ? 4000 : 1500; x.morale = 80; x.confused = 0; }
                    bool ok = Usable(M, u, t);
                    line += ok ? "Y" : "-";
                    if (ok) { var tg = M.TacticTargets(u, t)[0]; Sure(() => M.UseNewTactic(u, t, tg)); }
                    M.EndSide(); M.EndSide();
                }
                var want = string.Concat(Enumerable.Range(0, 7).Select(d => d % (t.Cd + 1) == 0 ? "Y" : "-"));
                if (line != want) throw new Exception($"{t.Name} 冷却{t.Cd}日: usable pattern {line}, want {want}");
                if (Tactics.Rules(t).IndexOf("冷却" + t.Cd + "日") < 0) throw new Exception(t.Name + " rules text should state 冷却" + t.Cd + "日");
                pat.Add(t.Name + t.Cd);
            }
            notes.Add("cooldowns " + string.Join(" ", pat));
        }
        // 火计击溃敌主将而结束战斗：施计者的计策 / 伤害 / 击溃经验都要计入战后结算（结算在记经验之后）
        {
            var f = TField(3); var M = f.M; var u = f.u; var foes = f.foes;
            var e = foes[0];
            e.Troops = 300; e.commander = true;
            foes[2].Troops = 0; foes[2].troops0 = 0;     // 无兵上阵的守将：战后不计经验、不计战绩
            var r = Sure(() => M.UseTactic(u, T("fire"), e));
            if (!r.Key || e.alive || M.result != 1) throw new Exception("fire should rout the 300-troop commander and end the battle");
            var pr = (M.progress ?? new List<ProgressResult>()).FirstOrDefault(p => p.gen == u.gen);
            int want = Progress.BattleExp(300, 0, 1, 1, 0, 0, 0, 1, 0, true, false);
            if (pr == null || pr.xp.tactics != 1 || pr.xp.routs != 1 || pr.xp.cmdRouts != 1 || pr.xp.dmg != 300 || pr.exp != want) throw new Exception("battle-ending fire lost the caster exp");
            if (M.progress.Any(p => p.gen == foes[2].gen) || !M.progress.Any(p => p.gen == foes[1].gen) || (foes[2].gen.record != null ? foes[2].gen.record.battles : 0) != 0) throw new Exception("a 0-troop unit should get no battle exp / record");
            notes.Add($"battle-ending 火计 credits caster {pr.exp} exp; 0-troop unit skipped");
        }
        // 伏兵：须靠近林地 / 山丘
        {
            var f = TField(1); var M = f.M; var u = f.u; var foes = f.foes;
            foes[0].x = 5; foes[0].y = 5;
            if (Usable(M, u, T("ambush"))) throw new Exception("ambush should need cover");
            M.map[5, 6] = Terrain.Forest;
            if (!Usable(M, u, T("ambush")) || M.TacticTargets(u, T("ambush"))[0] != foes[0]) throw new Exception("ambush should work next to a forest");
        }
        // 补给：耗粮、回复不超过出阵兵力
        {
            var f = TField(1, 1); var M = f.M; var u = f.u;
            var a = f.friends[0];
            if (M.TacticTargets(u, T("supply")).Count > 0) throw new Exception("supply targets unhurt units");
            a.Troops -= 900;
            int food0 = M.food[0];
            var r = M.UseNewTactic(u, T("supply"), a);
            if (!r.success || r.heal <= 0 || a.Troops > a.troops0 || !(M.food[0] < food0)) throw new Exception("supply: heal " + r.heal + " troops " + a.Troops + "/" + a.troops0 + " food " + food0 + "→" + M.food[0]);
            notes.Add($"补给 +{r.heal} food −{food0 - M.food[0]}");
        }
        // 挑拨：敌军之后两次本方行动期间防御下降（我方的攻击更痛）；伪报：敌军下一次本方行动攻击下降
        {
            var f = TField(2); var M = f.M; var u = f.u;
            var e = f.foes[0]; var e2 = f.foes[1];
            Sure(() => { M.UseNewTactic(u, T("provoke"), e); M.UseNewTactic(u, T("rumor"), e2); return 0; });
            if (!(M.ModK(e, "def") < 1) || !(M.AtkExpect(u, e) > M.AtkExpect(u, e2))) throw new Exception("provoke did not lower defence");
            int phases = 0, atkLow = 0;
            for (int k = 0; k < 4; k++)
            {
                do { M.EndSide(); } while (M.side != 1);
                if (M.ModK(e, "def") < 1) phases++;
                if (M.ModK(e2, "atk") < 1) atkLow++;
            }
            if (phases != (int)T("provoke").Mod.Days || atkLow != (int)T("rumor").Mod.Days) throw new Exception($"debuff durations: provoke {phases}, rumor {atkLow}");
            notes.Add($"挑拨 {phases} enemy phases, 伪报 {atkLow}");
        }
        // 连环：周围一格的敌军一起混乱；奇门遁甲：友军机动 +2；落雷不受城防减免
        {
            var f = TField(3, 1); var M = f.M; var u = f.u; var foes = f.foes;
            var r = Sure(() => M.UseNewTactic(u, T("chain"), foes[0]));
            int near = foes.Count(e => BattleModel.Dist(e, foes[0]) <= 1);
            if (r.confused.Count != near || near < 2) throw new Exception("chain confused " + r.confused.Count + " of " + near);
            int m0 = f.friends[0].Mobility;
            M.UseNewTactic(u, T("qimen"), u);
            if (f.friends[0].Mobility != m0 + 2) throw new Exception("qimen should add 2 mobility");
            var f2 = TField(1); var M2 = f2.M; var u2 = f2.u; var e1 = f2.foes[0];
            double plain = M2.TacticPlan(u2, T("thunder"), e1).dmg[0].amt, firePlain = M2.TacticPlan(u2, T("weather"), e1).dmg[0].amt;
            M2.map[e1.x, e1.y] = Terrain.Gate;
            if (Math.Abs(M2.TacticPlan(u2, T("thunder"), e1).dmg[0].amt - plain) > 1e-6) throw new Exception("thunder should ignore gate defence");
            if (!(M2.TacticPlan(u2, T("weather"), e1).dmg[0].amt < firePlain * 0.7)) throw new Exception("weather should be reduced on a gate");
            notes.Add($"连环 confused {r.confused.Count}");
        }
        // 原四计不变：与第一版 UseTactic 同随机数下结果完全一致
        {
            for (int k = 0; k < 80; k++)
            {
                var snap = new List<string>();
                for (int v = 0; v <= 1; v++)
                {
                    R.InitState(600 + k);
                    var f = TField(2); var M = f.M; var u = f.u;
                    u.gen.intel = 40 + (k % 60);
                    var t = Defs.Tactics[k % 4];
                    var tg = t.Kind == TacticKind.Inspire ? u : f.foes[k % 2];
                    if (k % 3 == 0) M.map[tg.x, tg.y] = Terrain.Forest;
                    var r = v == 0 ? UseTacticV1(M, u, t, tg) : M.UseTactic(u, t, tg);
                    snap.Add(r.Key + "/" + r.Value + "|" + R.NextDouble() + "|" + string.Join(";", M.units.Select(x => x.Troops + "," + x.morale + "," + x.confused + "," + x.alive)));
                }
                if (snap[0] != snap[1]) throw new Exception("original tactic differs from v1 at case " + k + "\n" + snap[0] + "\n" + snap[1]);
            }
            notes.Add("原四计 80 cases identical to v1");
        }
        Console.WriteLine($"(7d) tactics OK: {all.Count} tactics ({string.Join(" ", Enumerable.Range(1, 4).Select(t => "T" + t + ":" + all.Count(x => x.Tier == t)))}; learnable {learn.Count}); {string.Join("; ", notes)}");
    }
}
