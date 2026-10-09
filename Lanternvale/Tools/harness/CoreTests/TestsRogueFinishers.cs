// Rogue finishers as the player plays them ("rogue eviscerate does so little damage"): a new-game rogue with level gear
// builds combo points with Sinister Strike and spends them with Eviscerate through the same calls CombatController makes
// (Battle.CanUse, then Battle.UseAbility). Eviscerate gains 7% attack power per combo point (apCoefPerCombo), rank 2 comes
// at level 6, combo points stay with the rogue for the whole battle, and the UI text (tooltip, hover magnitude, the
// "requires combo points" reason) follows the rules.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsRogueFinishers
    {
        const string SS = "rogue_sinister_strike", Evis = "rogue_eviscerate", Foe = "cr_bandit_cutthroat";

        /// <summary>A new game's rogue at the level (veteran gear, every rank, default talents) against a level-matched,
        /// very tough Bridge Cutthroat in melee, on the rogue's turn.</summary>
        static (Battle b, Unit r, Unit foe, GameSession s) Setup(int level, int seed)
        {
            var s = SessionTest.NewGame(ClassId.Rogue, level, (ulong)seed);
            var r = s.Main;
            r.AutoPlay = false;
            r.RestoreFull();
            var b = NewBattle(seed, new Inventory());
            r.At(20f, 20f);
            var foe = Mob(Foe, level).At(22f, 20f).Tough(1000f);
            r.FaceTowards(foe.Position);
            foe.FaceTowards(r.Position);
            b.AddUnit(r);
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, r);
            return (b, r, foe, s);
        }

        /// <summary>The player's click: CanUse (the bar/hover check), then UseAbility. Returns the direct damage the cast
        /// dealt to the target (0 when avoided) and whether it landed.</summary>
        static float PlayerUse(Battle b, Unit r, string id, Unit target, out bool landed, out bool crit)
        {
            var a = Db.Ability(id);
            var chk = b.CanUse(r, a, target, null, false, 0);
            Assert(chk.Ok, $"{a.name} usable from the player path: {chk.Reason}");
            int start = b.Events.Count;
            var res = b.UseAbility(r, id, target, null, 0);
            Assert(res.Ok, $"{a.name}: {res.Reason}");
            float dmg = 0f;
            landed = crit = false;
            for (int i = start; i < b.Events.Count; i++)
            {
                var e = b.Events[i];
                if (e.Source != r || e.Target != target || e.AbilityId != id || e.Type != CombatEventType.Damage || e.Periodic || e.AutoAttack) continue;
                dmg += e.Amount + e.Overkill;
                landed = true;
                crit |= e.Crit;
            }
            return dmg;
        }

        static void Refill(Battle b, Unit r, Unit foe)
        {
            r.Energy = r.MaxResource(ResourceType.Energy);
            r.TimeLeft = 6f;
            foe.Health = foe.MaxHealth;
        }

        sealed class Run
        {
            public readonly List<float> Ss = new List<float>();
            public readonly Dictionary<int, List<float>> Evis = new Dictionary<int, List<float>>();
            public float Avg(int cp) => Evis.TryGetValue(cp, out var l) && l.Count > 0 ? l.Average() : 0f;
            public float SsAvg => Ss.Count > 0 ? Ss.Average() : 0f;
        }

        /// <summary>
        /// Builds to <paramref name="target"/> combo points with Sinister Strike (player path), then Eviscerates (player
        /// path) until one lands; records every landed Sinister Strike and the landed Eviscerate at that count.
        /// </summary>
        static void BuildAndSpend(Battle b, Unit r, Unit foe, int target, Run run)
        {
            int guard = 0;
            while (b.ComboPointsOn(r, foe) < target && guard++ < 40)
            {
                Refill(b, r, foe);
                float d = PlayerUse(b, r, SS, foe, out bool hit, out _);
                if (hit) run.Ss.Add(d);
            }
            Assert(b.ComboPointsOn(r, foe) >= target, $"built {target} combo points (have {r.ComboPoints})");
            int cp = b.ComboPointsOn(r, foe);
            for (int k = 0; k < 20; k++)
            {
                Refill(b, r, foe);
                float d = PlayerUse(b, r, Evis, foe, out bool hit, out _);
                if (!hit)
                {
                    Assert(r.ComboPoints == cp, "an avoided Eviscerate keeps its combo points");
                    continue;
                }
                Assert(r.ComboPoints == 0, "a landed Eviscerate spends every combo point");
                if (!run.Evis.TryGetValue(cp, out var l)) run.Evis[cp] = l = new List<float>();
                l.Add(d);
                return;
            }
            Assert(false, "Eviscerate never landed in 20 tries");
        }

        static Run Measure(int level, int trials, params int[] cps)
        {
            var (b, r, foe, _) = Setup(level, 11 + level);
            var run = new Run();
            for (int t = 0; t < trials; t++)
                foreach (int cp in cps) BuildAndSpend(b, r, foe, cp, run);
            return run;
        }

        [Test]
        public static void Eviscerate_PlayerPath_ThreeToFivePointsClearlyBeatTheBuilders()
        {
            foreach (int level in new[] { 1, 12, 30 })
            {
                var run = Measure(level, 120, 3, 5);
                float ss = run.SsAvg, e3 = run.Avg(3), e5 = run.Avg(5);
                Console.WriteLine($"    L{level}: Sinister Strike {ss:0.0} | Eviscerate 3 cp {e3:0.0} ({e3 / ss:0.00}x), 5 cp {e5:0.0} ({e5 / ss:0.00}x)");
                Assert(ss > 0f && e3 > 0f && e5 > 0f, $"L{level}: measured");
                Assert(e3 >= 1.8f * ss, $"L{level}: Eviscerate at 3 combo points ({e3:0.0}) is at least 1.8x a Sinister Strike ({ss:0.0})");
                Assert(e5 >= 2.8f * ss, $"L{level}: Eviscerate at 5 combo points ({e5:0.0}) is at least 2.8x a Sinister Strike ({ss:0.0})");
                Assert(e5 >= 1.4f * e3, $"L{level}: 5 points ({e5:0.0}) hit clearly harder than 3 ({e3:0.0})");
                // per energy: 35 energy of Eviscerate at 5 points against the 45 of one builder
                Assert(e5 / 35f >= 3.5f * (ss / 45f), $"L{level}: Eviscerate's damage per energy dwarfs the builder's");
            }
        }

        [Test]
        public static void Eviscerate_OnePointIsNoLongerFarBelowABuilder_FromRankTwo()
        {
            foreach (int level in new[] { 6, 12, 30 })
            {
                var run = Measure(level, 150, 1);
                float ss = run.SsAvg, e1 = run.Avg(1);
                Console.WriteLine($"    L{level}: Sinister Strike {ss:0.0} | Eviscerate 1 cp {e1:0.0} ({e1 / ss:0.00}x)");
                Assert(e1 >= 0.75f * ss, $"L{level}: a 1-point Eviscerate ({e1:0.0}) is at least 0.75x a Sinister Strike ({ss:0.0})");
            }
        }

        [Test]
        public static void Eviscerate_RankOne_IsWoWsNumbersPlusSevenPercentApPerPoint()
        {
            var (b, r, foe, _) = Setup(1, 3);
            var ev = Db.Ability(Evis);
            var e = ev.effects[0];
            Assert(r.RankOf(Evis) == 1 && Math.Abs(e.apCoefPerCombo - 0.07f) < 1e-4f, "level 1: rank 1, 0.07 AP per point");
            Assert(ev.rankLevels.Length == 8 && ev.rankLevels[1] == 6, "rank 2 at level 6 (with Sinister Strike rank 2)");
            float ap = r.Stats.AttackPower;
            var mods = AbilityMods.For(r, ev);
            float red = Formulas.ArmorReduction(Math.Max(0f, foe.Stats.Armor - r.Stats.ArmorPenetration), r.Level);
            for (int cp = 1; cp <= 5; cp++)
            {
                // WoW 1.12 rank 1: 1 point 6-10 ... 5 points 26-30, plus 7% AP per point (Lanternvale)
                float lo = (6f + 5f * (cp - 1) + 0.07f * ap * cp) * mods.DamageMult, hi = (10f + 5f * (cp - 1) + 0.07f * ap * cp) * mods.DamageMult;
                var text = Tooltip.Magnitude(r, ev, e, AbilityRules.EffLevel(r, ev, 1), mods, 1, cp);
                Assert(text.Contains(((int)Math.Round(lo)).ToString()) && text.Contains(((int)Math.Round(hi)).ToString()), $"{cp} cp tooltip '{text}' = {lo:0.0}..{hi:0.0}");
                for (int k = 0; k < 30; k++)
                {
                    Refill(b, r, foe);
                    r.ComboPoints = cp;
                    r.ComboTarget = foe;
                    float d = PlayerUse(b, r, Evis, foe, out bool hit, out bool crit);
                    if (!hit || crit) continue;
                    Assert(d <= hi + 1f && d >= lo * (1f - red) - 1f, $"{cp} cp non-crit {d:0.0} within {lo * (1f - red):0.0}..{hi:0.0}");
                }
            }
        }

        [Test]
        public static void ApPerComboPoint_IsDirectFinisherDamageOnly()
        {
            var b = NewBattle(5);
            var r = Hero(ClassId.Rogue, 40).At(20f, 20f);
            var foe = Mob(Foe, 40).At(22f, 20f).Tough(1000f);
            b.AddUnit(r);
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, r);
            var ev = Db.Ability(Evis);
            var e = ev.effects[0];
            // Rupture's ticks (and every other effect) have no AP-per-point part; Eviscerate's tooltip includes it
            foreach (var a in Db.Abilities.Values)
                foreach (var x in a.effects)
                    if (x.apCoefPerCombo > 0f) Assert(a.id == Evis, $"{a.id}: only Eviscerate scales with AP per combo point");
            foreach (var t in Db.Aura("rogue_rupture").tickEffects) Assert(t.apCoefPerCombo == 0f, "Rupture ticks keep their own apCoef only");
            int eff = AbilityRules.EffLevel(r, ev, r.RankOf(Evis));
            float step = Tooltip.PerComboPoint(r, ev, e, eff);
            float want = e.perCombo + e.amount * (eff - ev.learnLevel) + 0.07f * r.Stats.AttackPower;
            AssertNear(step, want, 0.01f, "per point = perCombo + amount × (eff − learn) + 0.07 × AP");
            // the hover/tooltip text: an explicit count gives the total; no count gives the 1-point value and the step
            var mods = AbilityMods.For(r, ev);
            string a5 = Tooltip.Magnitude(r, ev, e, eff, mods, 0, 5);
            Assert(!a5.Contains("per extra point"), $"an explicit count shows the total: '{a5}'");
            string def = Tooltip.Magnitude(r, ev, e, eff, mods, 0);
            Assert(def.Contains("at 1 combo point") && def.Contains("per extra point"), $"no count: 1-point value and the step: '{def}'");
        }

        [Test]
        public static void ComboPoints_StayWithTheRogue_WhenACompanionKillsTheTarget()
        {
            var b = NewBattle(9, new Inventory());
            var r = Hero(ClassId.Rogue, 30).At(20f, 20f);
            r.AutoPlay = false;
            var kael = Hero(ClassId.Warrior, 30, name: "Kael").At(18f, 20f);
            var a = Mob(Foe, 30).At(22f, 20f).Tough(1000f);
            var bb = Mob(Foe, 30).At(20.4f, 21.95f).Tough(1000f);
            var c = Mob(Foe, 30).At(30f, 30f).Tough(1000f);
            foreach (var u in new[] { r, kael, a, bb, c }) b.AddUnit(u);
            b.Inventory.Add(Db.Item("rogue_flash_powder"), 2);
            b.Begin();
            SkipTo(b, r);
            int guard = 0;
            while (r.ComboPoints < 3 && guard++ < 30) { Refill(b, r, a); PlayerUse(b, r, SS, a, out _, out _); }
            Assert(r.ComboPoints == 3 && r.ComboTarget == a, $"3 points built on A ({r.ComboPoints})");
            // a companion takes the kill: the points are still the rogue's, for any enemy
            b.DealDamage(kael, a, a.Health + 50f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(!a.IsAlive, "A died");
            Assert(b.ComboPointsOn(r, bb) == 3 && b.ComboPointsOn(r, null) == 3, "the points carry over to another enemy");
            var bar = b.GetStatus(r, Db.Ability(Evis), false, 0, false);
            Assert(bar.Usable, "the action bar keeps Eviscerate lit: " + bar.Reason);
            // switching target with a builder keeps the count and moves the points
            Refill(b, r, bb);
            PlayerUse(b, r, SS, bb, out bool hit, out _);
            Assert(r.ComboPoints == (hit ? 4 : 3) && (!hit || r.ComboTarget == bb), $"a builder on B adds to the count ({r.ComboPoints})");
            int held = r.ComboPoints;
            // Eviscerate on B spends them all and hits for that many points
            float d = 0f;
            for (int k = 0; k < 20 && r.ComboPoints > 0; k++)
            {
                Refill(b, r, bb);
                d = PlayerUse(b, r, Evis, bb, out hit, out _);
            }
            Assert(r.ComboPoints == 0 && d > 0f, "Eviscerate on B spent the carried points");
            Assert(b.Events.Any(e => e.Type == CombatEventType.ComboPoints && e.Source == r && e.Count == -held), $"spent {held} points");
            // with none left the reason names what to press
            var chk = b.CanUse(r, Db.Ability(Evis), bb);
            Assert(!chk.Ok && chk.Code == UseFailure.ComboPoints && chk.Reason.Contains("Sinister Strike"), "reason: " + chk.Reason);
            // Vanish clears them
            while (r.ComboPoints < 2 && guard++ < 60) { Refill(b, r, bb); PlayerUse(b, r, SS, bb, out _, out _); }
            Refill(b, r, bb);
            var v = b.UseAbility(r, "rogue_vanish", r);
            Assert(v.Ok, "vanish: " + v.Reason);
            Assert(r.ComboPoints == 0 && b.ComboPointsOn(r, bb) == 0, "Vanish clears combo points");
        }

        [Test]
        public static void ComboPoints_ClearAtBattleEnd()
        {
            var b = NewBattle(4, new Inventory());
            var r = Hero(ClassId.Rogue, 12).At(20f, 20f);
            r.AutoPlay = false;
            var foe = Mob(Foe, 12).At(22f, 20f);
            b.AddUnit(r);
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, r);
            r.ComboPoints = 4;
            r.ComboTarget = foe;
            b.DealDamage(r, foe, foe.Health + 50f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(b.IsOver, "battle over");
            Assert(r.ComboPoints == 0 && r.ComboTarget == null, "combo points do not leave the battle");
        }

        [Test]
        public static void Eviscerate_TooltipShowsTheRogue_sOwnRank()
        {
            var ev = Db.Ability(Evis);
            foreach (int level in new[] { 1, 8, 12, 30 })
            {
                var s = SessionTest.NewGame(ClassId.Rogue, level, 21);
                var r = s.Main;
                string tip = Tooltip.Ability(r, ev);
                Assert(tip.IndexOf('{') < 0 && tip.Contains("5 points"), $"L{level}: tokens resolved: '{tip}'");
                int rank = r.RankOf(Evis);
                int eff = AbilityRules.EffLevel(r, ev, rank);
                var mods = AbilityMods.For(r, ev);
                string five = Tooltip.Magnitude(r, ev, ev.effects[0], eff, mods, rank, 5);
                Assert(tip.Contains("5 points " + five), $"L{level}: 5-point total '{five}' in '{tip}'");
                Console.WriteLine($"    L{level} (rank {rank}): {tip}");
            }
            // rank 2 at level 8: +23-ish per point became +17.9 (rank level 6) plus 7% AP
            var r8 = SessionTest.NewGame(ClassId.Rogue, 8, 21).Main;
            Assert(r8.RankOf(Evis) == 2, "level 8 knows rank 2");
            float step = Tooltip.PerComboPoint(r8, ev, ev.effects[0], AbilityRules.EffLevel(r8, ev, 2));
            AssertNear(step, 5f + 2.582f * 5f + 0.07f * r8.Stats.AttackPower, 0.05f, "rank 2 per point");
            // no unit (data browsing): rank 1 reads exactly like WoW's tooltip
            string plain = Tooltip.Ability(null, ev);
            Assert(plain.Contains("1 point 6 to 10") && plain.Contains("5 points 26 to 30"), plain);
        }

        [Test]
        public static void LevelUp_NamesTheNewRanks()
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 5, 13);
            s.TakeEvents();
            int need = Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp + 1;
            s.GivePartyXp(need);
            Assert(s.Main.Level == 6, "reached 6");
            var text = string.Join(" | ", s.TakeEvents().Where(e => e.Kind == SessionEventKind.LevelUp && e.Unit == s.Main).Select(e => e.Text));
            Assert(text.Contains("Eviscerate (Rank 2)") && text.Contains("Sinister Strike (Rank 2)"), "level 6 names the new ranks: " + text);
        }

        /// <summary>Report: per-level Sinister Strike vs Eviscerate at 1/3/5 points (player path), level gear.</summary>
        [Sim]
        public static void RogueFinishers_Table()
        {
            var sb = new StringBuilder();
            foreach (int level in new[] { 1, 3, 4, 5, 6, 7, 8, 10, 12, 16, 20, 25, 30 })
            {
                var run = Measure(level, 200, 1, 3, 5);
                var (_, r, _, _) = Setup(level, 11 + level);
                float ss = run.SsAvg;
                sb.AppendLine($"L{level,2} evis r{r.RankOf(Evis)} ss r{r.RankOf(SS)} AP {r.Stats.AttackPower:0}: SS {ss:0.0} | 1cp {run.Avg(1):0.0} ({run.Avg(1) / ss:0.00}x) 3cp {run.Avg(3):0.0} ({run.Avg(3) / ss:0.00}x) 5cp {run.Avg(5):0.0} ({run.Avg(5) / ss:0.00}x)");
            }
            Console.WriteLine(sb.ToString());
        }
    }
}
