// Rules-engine support for the HUD: WoW downranking (cost, magnitudes, aura values, tooltips, pending casts, queued
// swings, rank cast times, AI healer downranking), hit-table previews that match the rolls, cast-bar totals, range
// tinting, cached cooldown-group keys, tooltip-free action bars, area-aura child events and allocation-free event reads.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsRulesUiSupport
    {
        static (Battle b, Unit hero, Unit foe) Setup(ClassId c, int level, bool talents = true, string mob = "cr_bandit_cutthroat", int mobLevel = 0, float dist = 2f, int seed = 5)
        {
            var b = NewBattle(seed, new Inventory());
            var hero = Hero(c, level, talents).At(20f, 20f);
            hero.AutoPlay = false;
            var foe = Mob(mob, mobLevel > 0 ? mobLevel : level).At(20f + dist, 20f).Tough();
            hero.FaceTowards(foe.Position);
            foe.FaceTowards(hero.Position);
            b.AddUnit(hero);
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, hero);
            return (b, hero, foe);
        }

        static void Fresh(Unit u)
        {
            u.TimeLeft = 6f;
            u.TimeDebt = 0f;
            u.Cooldowns.Clear();
            u.Mana = u.MaxMana;
            u.Rage = 100f;
            u.Energy = u.MaxResource(ResourceType.Energy);
        }

        static float Cost(Unit u, string id, int rank)
        {
            var a = Db.Ability(id);
            return AbilityRules.ResourceCost(u, a, AbilityRules.UsedRank(u, a, rank), AbilityMods.For(u, a));
        }

        // ================================================================== downranking

        [Test]
        public static void Downrank_CostMagnitudeStatusAndTooltip()
        {
            var (b, mage, foe) = Setup(ClassId.Mage, 60, talents: false, dist: 10f);
            var fb = Db.Ability("mage_fireball");
            int known = mage.RankOf(fb.id);
            Assert(known == AbilityRules.RankCount(fb) && known > 1, $"level 60 mage knows every fireball rank ({known})");

            // status / picker
            var top = b.GetStatus(mage, fb);
            var r1 = b.GetStatus(mage, fb, false, 1);
            Assert(top.Rank == known && top.KnownRanks == known && top.CanDownrank, $"top status rank {top.Rank}, known {top.KnownRanks}");
            Assert(r1.Rank == 1 && r1.Usable && r1.KnownRanks == known, $"rank 1 status ({r1})");
            AssertNear(r1.Cost, Cost(mage, fb.id, 1), 0.01f, "rank 1 cost in the status");
            Assert(r1.Cost < top.Cost * 0.25f, $"rank 1 is much cheaper ({r1.Cost} vs {top.Cost})");
            Assert(r1.Tooltip != top.Tooltip && r1.Tooltip.Length > 0, "rank 1 tooltip differs: " + r1.Tooltip);
            Assert(r1.Tooltip == Tooltip.Ability(mage, fb, 1), "status tooltip = Tooltip.Ability(u, a, 1)");
            Assert(Tooltip.AbilityFull(mage, fb, 1).Contains("(Rank 1)"), "full tooltip names the rank");
            Assert(Tooltip.AbilityFull(mage, fb).Contains($"(Rank {known})"), "full tooltip defaults to the known rank");

            // unknown ranks are refused
            var bad = b.CanUse(mage, fb.id, foe, null, false, known + 1);
            Assert(!bad.Ok && bad.Code == UseFailure.Unknown && bad.Reason.Contains("Rank"), "unknown rank refused: " + bad);
            Assert(!b.UseAbility(mage, fb.id, foe, null, known + 1).Ok, "UseAbility refuses an unknown rank");
            Assert(!b.CanUseIgnoringTarget(mage, fb, false, known + 1).Ok, "CanUseIgnoringTarget refuses an unknown rank");

            // cast rank 1: rank 1 mana, rank 1 damage, rank 1 DoT
            Fresh(mage);
            float mana = mage.Mana;
            int cursor = b.Events.Count;
            var r = b.UseAbility(mage, fb.id, foe, null, 1);
            Assert(r.Ok, "rank 1 fireball: " + r.Reason);
            AssertNear(mana - mage.Mana, Cost(mage, fb.id, 1), 0.6f, "paid the rank 1 cost");
            var hits = b.Events.Skip(cursor).Where(e => e.Type == CombatEventType.Damage && e.AbilityId == fb.id && !e.Periodic).ToList();
            var resisted = b.Events.Skip(cursor).Any(e => e.Type == CombatEventType.Resist && e.AbilityId == fb.id);
            Assert(hits.Count == 1 || resisted, "the fireball resolved");
            float sp = mage.Stats.SpellDamage(School.Fire);
            if (hits.Count == 1) Assert(hits[0].Amount <= (22f + sp) * 1.5f * 1.3f + 1f, $"rank 1 damage ({hits[0].Amount}, spell power {sp})");
            var dot = foe.FindAura("mage_fireball_dot", mage);
            if (!resisted) Assert(dot != null && dot.Rank == 1, $"DoT carries rank 1 (rank {dot?.Rank})");

            // top rank for comparison
            Fresh(mage);
            cursor = b.Events.Count;
            Assert(b.UseAbility(mage, fb.id, foe).Ok, "top rank fireball");
            var top2 = b.Events.Skip(cursor).Where(e => e.Type == CombatEventType.Damage && e.AbilityId == fb.id && !e.Periodic).ToList();
            if (top2.Count == 1) Assert(top2[0].Amount > 400f, $"top rank damage ({top2[0].Amount})");
        }

        [Test]
        public static void Downrank_AuraValuesPendingCastsAndQueuedSwings()
        {
            // aura mod values of the rank (Power Word: Fortitude rank 1 = +3 Stamina, rank 6 = +54), out of combat
            var priest = Hero(ClassId.Priest, 60, talents: false).At(10f, 10f);
            var ally = Hero(ClassId.Warrior, 60, talents: false).At(12f, 10f);
            var f = Battle.CreateField(Db, new Rng(3), new List<Unit> { priest, ally }, new StraightLinePathfinder(80f, 60f), new Inventory());
            Assert(f.UseAbility(priest, "priest_power_word_fortitude", ally, null, 1).Ok, "rank 1 fortitude");
            var pwf = ally.FindAura("priest_power_word_fortitude");
            Assert(pwf != null && pwf.Rank == 1, "aura rank 1");
            AssertNear(pwf.ModValues[0], 3f, 0.01f, "rank 1 stamina");
            priest.Cooldowns.Clear();
            Assert(f.UseAbility(priest, "priest_power_word_fortitude", ally).Ok, "top rank fortitude");
            pwf = ally.FindAura("priest_power_word_fortitude");
            AssertNear(pwf.ModValues[0], 54f, 0.01f, "rank 6 stamina");

            // pending cast keeps its rank and reports its total time
            var (b, p2, foe) = Setup(ClassId.Priest, 40, talents: false, dist: 6f);
            var ally2 = Hero(ClassId.Warrior, 40, talents: false).At(22f, 22f);
            b.AddUnit(ally2);
            ally2.Health = ally2.MaxHealth * 0.5f;
            Fresh(p2);
            p2.TimeLeft = 1f;
            var heal = Db.Ability("priest_heal");
            Assert(b.UseAbility(p2, heal.id, ally2, null, 1).Ok, "rank 1 heal started");
            Assert(p2.Pending != null && p2.Pending.Rank == 1, "pending cast keeps rank 1");
            AssertNear(p2.Pending.TotalTime, AbilityRules.CastTime(p2, heal, AbilityMods.For(p2, heal), 1), 0.01f, "pending total time");

            // queued swing keeps its rank
            var (b3, w, foe3) = Setup(ClassId.Warrior, 60, talents: false);
            Fresh(w);
            Assert(b3.UseAbility(w, "warrior_heroic_strike", foe3, null, 1).Ok, "rank 1 heroic strike queued");
            Assert(w.QueuedSwing == "warrior_heroic_strike" && w.QueuedSwingRank == 1, "queued rank 1");
            int c3 = b3.Events.Count;
            w.SwingMain = 10f;
            b3.EndTurn(w);
            Assert(b3.Events.Skip(c3).Any(e => e.Type == CombatEventType.AbilityUsed && e.AbilityId == "warrior_heroic_strike"), "the queued swing fired");
            Assert(w.QueuedSwingRank == 0 && w.QueuedSwing == "", "queue cleared");
        }

        [Test]
        public static void Downrank_RankCastTimes()
        {
            var def = new AbilityDef { id = "test_bolt", name = "Test Bolt", castTime = 3f, learnLevel = 1, rankLevels = new[] { 1, 6, 12 }, rankCastTimes = new[] { 1.5f, 2f, 3f }, school = School.Fire };
            AssertNear(AbilityRules.BaseCastTime(def, 1), 1.5f, 1e-4f, "rank 1 cast time");
            AssertNear(AbilityRules.BaseCastTime(def, 2), 2f, 1e-4f, "rank 2 cast time");
            AssertNear(AbilityRules.BaseCastTime(def, 0), 3f, 1e-4f, "rank 0 = top rank");
            var mage = Hero(ClassId.Mage, 20, talents: false, gear: false);
            mage.Abilities[def.id] = 3;
            AssertNear(AbilityRules.CastTime(mage, def, AbilityModSet.Empty), 3f, 1e-3f, "known rank 3 casts in 3 s");
            AssertNear(AbilityRules.CastTime(mage, def, AbilityModSet.Empty, 1), 1.5f, 1e-3f, "downranked cast time");
            AssertNear(AbilityRules.TimeCost(mage, def, AbilityModSet.Empty, 1), 1.5f, 1e-3f, "downranked Time cost");
            var plain = Db.Ability("mage_fireball");
            AssertNear(AbilityRules.BaseCastTime(plain, 1), plain.castTime, 1e-4f, "no rankCastTimes: castTime for every rank");
            mage.Abilities.Remove(def.id);
        }

        [Test]
        public static void Downrank_CompanionHealerDownranksWhenManaIsLow()
        {
            var b = NewBattle(8, new Inventory());
            var priest = Hero(ClassId.Priest, 40, talents: false).At(20f, 20f);
            var tank = Hero(ClassId.Warrior, 40, talents: false).At(22f, 20f);
            var foe = Mob("cr_bandit_cutthroat", 40).At(30f, 20f).Tough();
            foreach (var id in priest.Abilities.Keys.ToList()) if (id != "priest_heal") priest.Abilities.Remove(id);
            priest.RoleOverride = UnitRole.Healer;
            tank.AutoPlay = false;
            b.AddUnit(priest); b.AddUnit(tank); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, priest);
            var heal = Db.Ability("priest_heal");
            int known = priest.RankOf(heal.id);
            Assert(known >= 3, "priest knows several Heal ranks");

            // a deficit the AI heals (≥ half the top rank) that a lower rank covers
            float top = AI.ExpectedHeal(priest, heal, 0);
            float deficit = Math.Max(top * 0.55f, AI.ExpectedHeal(priest, heal, 1) * 0.9f);
            int expectedRank = 0;
            for (int r0 = 1; r0 < known; r0++) if (AI.ExpectedHeal(priest, heal, r0) >= deficit) { expectedRank = r0; break; }
            Assert(expectedRank >= 1, $"a lower rank covers {deficit:0} (top {top:0})");
            Assert(AI.ExpectedHeal(priest, heal, 1) < AI.ExpectedHeal(priest, heal, known), "lower ranks heal less");
            tank.Health = tank.MaxHealth - deficit;
            Assert(tank.HealthPct < 90f && tank.HealthPct > 50f, $"tank at {tank.HealthPct:0}%");
            priest.Mana = priest.MaxMana;
            Assert(AI.DownrankFor(priest, heal, deficit) == 0, "full mana: top rank");
            priest.Mana = priest.MaxMana * 0.3f;
            int rank = AI.DownrankFor(priest, heal, deficit);
            Assert(rank == expectedRank, $"low mana: the smallest covering rank ({rank}, expected {expectedRank})");
            Assert(AI.DownrankFor(priest, heal, 1e6f) == 0, "nothing covers a huge deficit: top rank");

            var step = AI.NextStep(b, priest);
            Assert(step.Kind == AIStepKind.UseAbility && step.AbilityId == heal.id && step.Rank == rank, $"AI step heals at rank {rank}: {step} (rank {step.Rank})");
            float mana = priest.Mana;
            var r = AI.Execute(b, step);
            Assert(r.Ok, "heal executed: " + r.Reason);
            if (priest.Pending == null) AssertNear(mana - priest.Mana, Cost(priest, heal.id, rank), 0.6f, "paid the downranked cost");
            else Assert(priest.Pending.Rank == rank, "pending heal keeps the rank");
        }

        // ================================================================== hit table previews

        [Test]
        public static void HitChance_AbilityTableMatchesRolls()
        {
            // melee ability against a frontal humanoid (miss, dodge, parry, block)
            var (b, w, foe) = Setup(ClassId.Warrior, 30, talents: false);
            b.RecordEvents = false;
            var a = Db.Ability("warrior_hamstring");
            var h = b.HitChance(w, a, foe);
            Assert(h.Kind == AttackKind.Melee && h.Rolls && !h.SingleRoll && !h.Immune, "melee ability table: " + h);
            AssertNear(h.Hit + h.Miss + h.Dodge + h.Parry + h.Resist, 100f, 0.01f, "outcomes add up");
            Assert(h.Parry > 0f && h.Dodge > 0f && h.Miss > 0f, "frontal humanoid can miss/dodge/parry: " + h);
            var e = a.effects.First(x => x.type == EffectType.ApplyAura || x.type == EffectType.WeaponDamage || x.type == EffectType.Damage);
            CheckEmpirical(b, w, a, foe, e, h, 6000);
            Console.WriteLine($"    hamstring L30 vs L30 humanoid: {h}");

            // from behind: no parry or block
            foe.FaceTowards(foe.Position + (foe.Position - w.Position));
            var hb = b.HitChance(w, a, foe);
            Assert(hb.Parry == 0f && hb.Block == 0f && hb.Dodge > 0f, "from behind: dodge only: " + hb);

            // spells: the miss is a full resist; crit on hit from the same CritChance the engine uses
            var (b2, mage, foe2) = Setup(ClassId.Mage, 30, talents: false, mobLevel: 33, dist: 10f);
            b2.RecordEvents = false;
            var fb = Db.Ability("mage_fireball");
            var s = b2.HitChance(mage, fb, foe2);
            Assert(s.Kind == AttackKind.Spell && s.Miss == 0f && s.Dodge == 0f && s.Parry == 0f, "spell table: " + s);
            AssertNear(s.Resist, Math.Max(1f, Formulas.SpellMissChance(30, 33) - mage.Stats.SpellHit(School.Fire)), 0.01f, "+3 levels: 15% resist minus spell hit");
            var c = b2.NewCast(mage, fb, AbilityRules.UsedRank(mage, fb), foe2, null, null);
            c.CurrentEffect = fb.effects[0];
            AssertNear(s.CritOnHit, b2.CritChance(c, foe2, false, School.Fire), 0.001f, "crit on hit = engine crit chance");
            AssertNear(s.Crit, s.Hit * s.CritOnHit / 100f, 0.001f, "absolute crit");
            CheckEmpirical(b2, mage, fb, foe2, fb.effects[0], s, 6000);
            Console.WriteLine($"    fireball L30 vs L33: {s}");

            // helpful: never misses
            var priest = Hero(ClassId.Priest, 30, talents: false).At(25f, 25f);
            b2.AddUnit(priest);
            var hh = b2.HitChance(priest, Db.Ability("priest_lesser_heal"), mage);
            Assert(hh.Hit == 100f && !hh.Rolls && hh.CanCrit && hh.CritOnHit > 0f, "heal: always lands, can crit: " + hh);

            // invulnerable target
            b2.ApplyAura(foe2, foe2, Db.Aura("paladin_divine_shield"));
            var inv = b2.HitChance(mage, fb, foe2);
            Assert(inv.Immune && inv.Hit == 0f, "divine shield: immune: " + inv);
        }

        static void CheckEmpirical(Battle b, Unit caster, AbilityDef a, Unit target, EffectDef e, HitChanceInfo h, int n)
        {
            var counts = new Dictionary<HitOutcome, int>();
            int rank = AbilityRules.UsedRank(caster, a);
            for (int i = 0; i < n; i++)
            {
                var cast = b.NewCast(caster, a, rank, target, null, null);
                var o = b.RollHit(cast, target, e);
                counts.TryGetValue(o, out var k);
                counts[o] = k + 1;
                caster.Reactive.Clear();
                target.Reactive.Clear();
            }
            void Check(HitOutcome o, float expected)
            {
                counts.TryGetValue(o, out var k);
                float got = k * 100f / n;
                float p = expected / 100f;
                float tol = Math.Max(1.2f, 4.5f * (float)Math.Sqrt(p * (1 - p) / n) * 100f);
                AssertNear(got, expected, tol, $"{a.name} {o} rate");
            }
            Check(HitOutcome.Miss, h.Miss);
            Check(HitOutcome.Resist, h.Resist);
            Check(HitOutcome.Dodge, h.Dodge);
            Check(HitOutcome.Parry, h.Parry);
            Check(HitOutcome.Block, h.Block);
            Check(HitOutcome.Hit, h.Hit - h.Block);
        }

        [Test]
        public static void HitChance_SwingTableMatchesRolls()
        {
            var (b, rogue, foe) = Setup(ClassId.Rogue, 30, talents: false);
            b.RecordEvents = true;
            Assert(rogue.Equipment.IsDualWielding, "veteran rogue dual wields");
            var h = b.HitChance(rogue, null, foe);
            var hm = b.SwingHitChance(rogue, foe, WeaponSlot.MainHand);
            Assert(h.SingleRoll && h.Kind == AttackKind.Melee, "null ability = white swing: " + h);
            AssertNear(h.Hit, hm.Hit, 1e-4f, "HitChance(null) = main-hand SwingHitChance");
            AssertNear(b.HitChance(rogue, Db.Ability("attack"), foe).Hit, hm.Hit, 1e-4f, "Attack previews the white swing");
            Assert(hm.Miss > 20f, $"dual wield white swings miss a lot ({hm.Miss})");
            AssertNear(hm.Miss + hm.Dodge + hm.Parry + hm.Hit, 100f, 0.01f, "single roll adds up");

            var swing = typeof(Battle).GetMethod("WhiteSwing", BindingFlags.NonPublic | BindingFlags.Instance);
            int n = 6000, miss = 0, dodge = 0, parry = 0, block = 0, crit = 0, hit = 0;
            for (int i = 0; i < n; i++)
            {
                int c = b.Events.Count;
                swing.Invoke(b, new object[] { rogue, foe, WeaponSlot.MainHand, 0f });
                for (int j = c; j < b.Events.Count; j++)
                {
                    var ev = b.Events[j];
                    if (ev.Source != rogue || !ev.AutoAttack) continue;
                    if (ev.Type == CombatEventType.Miss) miss++;
                    else if (ev.Type == CombatEventType.Dodge) dodge++;
                    else if (ev.Type == CombatEventType.Parry) parry++;
                    else if (ev.Type == CombatEventType.Damage) { hit++; if (ev.Blocked > 0) block++; if (ev.Crit) crit++; }
                }
                foe.Health = foe.MaxHealth;
                rogue.Reactive.Clear(); foe.Reactive.Clear();
                if (b.Events.Count > 50000) b.Events.Clear();
            }
            void Check(int k, float expected, string what)
            {
                float got = k * 100f / n, p = expected / 100f;
                AssertNear(got, expected, Math.Max(1.2f, 4.5f * (float)Math.Sqrt(p * (1 - p) / n) * 100f), "white swing " + what);
            }
            Check(miss, hm.Miss, "miss");
            Check(dodge, hm.Dodge, "dodge");
            Check(parry, hm.Parry, "parry");
            Check(hit, hm.Hit, "hit");
            Check(crit, hm.Crit, "crit");
            Check(block, hm.Block, "block");
            Console.WriteLine($"    dual-wield white swing L30 vs L30 humanoid: {hm}; rolled miss {miss} dodge {dodge} parry {parry} hit {hit} crit {crit} of {n}");
        }

        // ================================================================== cast bars, range, bar options

        [Test]
        public static void PendingCast_TotalTimeFollowsPushback()
        {
            var (b, mage, foe) = Setup(ClassId.Mage, 20, talents: false, dist: 10f);
            var fb = Db.Ability("mage_fireball");
            Fresh(mage);
            mage.TimeLeft = 1f;
            float cast = AbilityRules.CastTime(mage, fb, AbilityMods.For(mage, fb));
            Assert(b.UseAbility(mage, fb.id, foe).Ok, "fireball started");
            var p = mage.Pending;
            Assert(p != null && !p.Channel, "pending cast");
            AssertNear(p.TotalTime, cast, 0.01f, "total = hasted cast time");
            AssertNear(p.RemainingTime, cast - 1f, 0.01f, "remaining");
            AssertNear(p.Progress, 1f / cast, 0.01f, "progress");
            b.DealDamage(foe, mage, 5f, School.Physical, new DamageInfo { Kind = AttackKind.Melee });
            AssertNear(p.TotalTime, cast + 0.5f, 0.01f, "pushback lengthens the cast");
            AssertNear(p.RemainingTime, cast - 0.5f, 0.01f, "and the remaining time");

            // channels: pushback cuts the channel short
            var (b2, mage2, foe2) = Setup(ClassId.Mage, 20, talents: false, dist: 10f);
            var am = Db.Ability("mage_arcane_missiles");
            Fresh(mage2);
            mage2.TimeLeft = 2f;
            float ch = AbilityRules.CastTime(mage2, am, AbilityMods.For(mage2, am));
            Assert(b2.UseAbility(mage2, am.id, foe2).Ok, "arcane missiles started");
            var q = mage2.Pending;
            Assert(q != null && q.Channel, "pending channel");
            AssertNear(q.TotalTime, ch, 0.01f, "channel total");
            AssertNear(q.RemainingTime, ch - 2f, 0.01f, "channel remaining");
            b2.DealDamage(foe2, mage2, 5f, School.Physical, new DamageInfo { Kind = AttackKind.Melee });
            AssertNear(q.TotalTime, ch - 0.5f, 0.01f, "pushback shortens the channel");
            AssertNear(q.RemainingTime, ch - 2.5f, 0.01f, "remaining channel");
        }

        [Test]
        public static void ActionBar_RangeTintingTooltipsAndGroupCooldowns()
        {
            var (b, mage, foe) = Setup(ClassId.Mage, 20, talents: false, dist: 10f);
            Fresh(mage);
            mage.AttackTarget = null;
            var bar = b.GetAbilityBar(mage);
            Assert(bar.All(s => s.InRangeOfAttackTarget == null), "no attack target: null");
            mage.AttackTarget = foe;
            bar = b.GetAbilityBar(mage);
            var fbS = bar.First(s => s.Ability.id == "mage_fireball");
            Assert(fbS.InRangeOfAttackTarget == true, "fireball in range at 10 m");
            var armor = bar.First(s => s.Ability.target == TargetType.Self);
            Assert(armor.InRangeOfAttackTarget == null, "self abilities: null");
            var ally = bar.FirstOrDefault(s => s.Ability.target == TargetType.Ally);
            if (ally != null) Assert(ally.InRangeOfAttackTarget == null, "ally abilities: null");
            foe.Position = new Vec2(60f, 20f);
            Assert(b.GetStatus(mage, Db.Ability("mage_fireball")).InRangeOfAttackTarget == false, "fireball out of range at 40 m");
            Assert(!b.IsInRange(mage, Db.Ability("mage_fireball"), foe) && b.IsInRange(mage, Db.Ability("mage_fireball"), mage), "IsInRange");
            foe.Position = new Vec2(30f, 20f);

            // tooltip-free bars (frequent HUD refreshes)
            var withTips = b.GetAbilityBar(mage);
            var noTips = b.GetAbilityBar(mage, includeTooltips: false);
            Assert(withTips.Count == noTips.Count && withTips.Any(s => s.Tooltip.Length > 0), "same entries, tooltips by default");
            Assert(noTips.All(s => s.Tooltip == ""), "no tooltip text when skipped");
            for (int i = 0; i < noTips.Count; i++)
                Assert(noTips[i].Ability == withTips[i].Ability && noTips[i].Cost == withTips[i].Cost && noTips[i].Usable == withTips[i].Usable, "other fields identical");
            Assert(b.GetAbilityBar(mage, true).Count >= withTips.Count, "GetAbilityBar(u, true) still means includeHidden");

            // cached "grp:" keys: shared shock cooldown
            var (b2, sham, foe2) = Setup(ClassId.Shaman, 20, talents: false, dist: 6f);
            Fresh(sham);
            var es = Db.Ability("shaman_earth_shock");
            var fs = Db.Ability("shaman_flame_shock");
            string k1 = Unit.CooldownGroupKey(es), k2 = Unit.CooldownGroupKey(es);
            Assert(k1 == "grp:" + es.cooldownGroup && ReferenceEquals(k1, k2), "group key cached per definition");
            Assert(Unit.CooldownGroupKey(Db.Ability("mage_fireball")) == null, "no group, no key");
            Assert(b2.UseAbility(sham, es.id, foe2).Ok, "earth shock");
            Assert(sham.CooldownLeft(fs) > 0f && sham.Cooldowns.ContainsKey(k1), "flame shock shares the shock cooldown");
            Assert(!b2.CanUse(sham, fs, foe2).Ok, "flame shock on cooldown");
        }

        // ================================================================== events

        [Test]
        public static void Events_AreaAuraChildFlagAndTakeEventsInto()
        {
            var pal = Hero(ClassId.Paladin, 20, talents: false).At(10f, 10f);
            var ally = Hero(ClassId.Warrior, 20, talents: false).At(12f, 10f);
            var f = Battle.CreateField(Db, new Rng(5), new List<Unit> { pal, ally }, new StraightLinePathfinder(80f, 60f), new Inventory());
            f.RecordEvents = true;
            int cursor = 0;
            var into = new List<CombatEvent>(256);
            f.TakeEvents(ref cursor, into);
            into.Clear();
            Assert(f.UseAbility(pal, "paladin_devotion_aura").Ok, "devotion aura");
            var src = f.Events.First(e => e.Type == CombatEventType.AuraApplied && e.AuraId == "paladin_devotion_aura");
            Assert(!src.AreaAuraChild, "the source aura is not a child");
            var kids = f.Events.Where(e => e.Type == CombatEventType.AuraApplied && e.AuraId == "paladin_devotion_aura_effect").ToList();
            Assert(kids.Count == 2 && kids.All(e => e.AreaAuraChild), $"children on paladin and ally flagged ({kids.Count})");
            f.Teleport(ally, new Vec2(60f, 10f));
            var gone = f.Events.LastOrDefault(e => e.Type == CombatEventType.AuraRemoved && e.AuraId == "paladin_devotion_aura_effect" && e.Target == ally);
            Assert(gone != null && gone.AreaAuraChild && gone.Reason == "OutOfRange", "leaving the radius: flagged removal");

            // allocation-free incremental reads
            int n = f.TakeEvents(ref cursor, into);
            Assert(n == into.Count && n > 0 && cursor == f.Events.Count, $"took {n} events");
            Assert(into[0] == f.Events[f.Events.Count - n], "in order");
            Assert(f.TakeEvents(ref cursor, into) == 0 && into.Count == n, "nothing new: nothing appended");
            var list = f.TakeEvents(ref cursor);
            Assert(list.Count == 0, "the list overload still works");
        }
    }
}
