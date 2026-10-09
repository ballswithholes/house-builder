// Regression tests for the class-audit engine follow-ups (Charge vs Bloodrage, Retaliation from behind, Heightened
// Senses, Bestial Discipline focus, Elemental Mastery / Clearcasting / Divine Favor next-spell bonuses, Greater
// Blessings, Amplify Curse, session item greying).
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsRulesFollowups
    {
        static (Battle b, Unit hero, Unit foe) Setup(ClassId c, int level, string mob = "cr_bandit_cutthroat", int mobLevel = 0, float dist = 2f, int seed = 5)
        {
            var b = NewBattle(seed, new Inventory());
            var hero = Hero(c, level).At(20f, 20f);
            hero.AutoPlay = false;
            var foe = Mob(mob, mobLevel > 0 ? mobLevel : Math.Max(1, level - 3)).At(20f + dist, 20f).Tough();
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
            u.Energy = u.MaxResource(ResourceType.Energy);
        }

        static float Cost(Unit u, string abilityId)
        {
            var a = Db.Ability(abilityId);
            return AbilityRules.ResourceCost(u, a, AbilityRules.UsedRank(u, a), AbilityMods.For(u, a));
        }

        static float Crit(Battle b, Unit caster, string abilityId, Unit target, bool heal)
        {
            var a = Db.Ability(abilityId);
            var c = b.NewCast(caster, a, AbilityRules.UsedRank(caster, a), target, null, null);
            c.CurrentEffect = a.effects.FirstOrDefault(e => e.type == (heal ? EffectType.Heal : EffectType.Damage));
            return b.CritChance(c, target, heal, a.school);
        }

        // ------------------------------------------------------------- 1. Charge vs Bloodrage

        [Test]
        public static void ChargeBlockedWhileBloodrageIsUp()
        {
            var b = NewBattle(4, new Inventory());
            var w = Hero(ClassId.Warrior, 20).At(10f, 20f);
            var foe = Mob("cr_wolf", 18).At(18f, 20f).Tough();
            w.AutoPlay = false;
            b.AddUnit(w); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, w);
            if (!w.HasAura("warrior_battle_stance_aura")) { Fresh(w); b.UseAbility(w, "warrior_battle_stance"); }
            Fresh(w);
            Assert(!w.Engaged, "warrior has not fought yet");
            var before = b.CanUse(w, "warrior_charge", foe);
            Assert(before.Ok, "charge usable before Bloodrage: " + before.Reason);
            var br = b.ApplyAura(w, w, Db.Aura("warrior_bloodrage"));
            Assert(br != null, "bloodrage applied");
            var during = b.CanUse(w, "warrior_charge", foe);
            Assert(!during.Ok && during.Reason.Contains("combat"), "Bloodrage puts the warrior in combat: " + during.Reason);
            b.RemoveAura(br, AuraRemoveReason.Expired);
            Assert(b.CanUse(w, "warrior_charge", foe).Ok, "charge usable again once Bloodrage has expired");
        }

        // ------------------------------------------------------------- 2. Retaliation from behind

        [Test]
        public static void RetaliationIgnoresAttackersBehind()
        {
            var (b, w, foe) = Setup(ClassId.Warrior, 40, mobLevel: 38);
            w.Tough();
            var ret = b.ApplyAura(w, w, Db.Aura("warrior_retaliation"));
            Assert(ret != null && ret.Charges == 30, "retaliation up with 30 charges");
            var hit = new DamageInfo { Kind = AttackKind.Melee, AutoAttack = true, IgnoreArmor = true, Name = "test" };

            // attacker in front: counterattack, one charge
            w.FaceTowards(foe.Position);
            Assert(!foe.IsBehind(w), "attacker in front");
            int ev0 = b.Events.Count(e => e.Source == w && e.Target == foe);
            b.DealDamage(foe, w, 10f, School.Physical, hit);
            Assert(ret.Charges == 29, $"frontal hit retaliated (charges {ret.Charges})");
            Assert(b.Events.Count(e => e.Source == w && e.Target == foe) > ev0, "a counterattack was made");

            // attacker behind (same test as requires.behindTarget, from the warrior's point of view): nothing
            w.Facing = new Vec2(-1f, 0f);
            Assert(foe.IsBehind(w), "attacker behind the warrior");
            int evBefore = b.Events.Count(e => e.Source == w && e.Target == foe);
            b.DealDamage(foe, w, 10f, School.Physical, hit);
            Assert(ret.Charges == 29, $"no charge used by a hit from behind (charges {ret.Charges})");
            Assert(b.Events.Count(e => e.Source == w && e.Target == foe) == evBefore, "no counterattack against an attacker behind");

            // facing again: retaliates again
            w.FaceTowards(foe.Position);
            b.DealDamage(foe, w, 10f, School.Physical, hit);
            Assert(ret.Charges == 28, $"frontal hit retaliated again (charges {ret.Charges})");
        }

        // ------------------------------------------------------------- 3. Heightened Senses

        /// <summary>Misses of <paramref name="n"/> rolls of the attacker's ability against the rogue (same seed per call).</summary>
        static int Misses(ClassId attackerClass, string abilityId, bool talent, int n = 3000)
        {
            var b = NewBattle(11, new Inventory());
            var r = Hero(ClassId.Rogue, 60).At(20f, 20f);
            r.Talents.Remove("rogue_subtlety_heightened_senses");
            if (talent) r.Talents["rogue_subtlety_heightened_senses"] = 2;
            r.InvalidateStats();
            var att = Hero(attackerClass, 60, talents: false, gear: false, name: "Attacker").At(30f, 20f);   // no hit bonuses
            att.Team = Team.Enemy;
            att.FaceTowards(r.Position);
            r.FaceTowards(att.Position);
            b.AddUnit(r); b.AddUnit(att);
            var a = Db.Ability(abilityId);
            var e = a.effects.First(x => x.type == EffectType.Damage || x.type == EffectType.WeaponDamage);
            int misses = 0;
            for (int i = 0; i < n; i++)
            {
                var c = b.NewCast(att, a, AbilityRules.UsedRank(att, a), r, null, null);
                var o = b.RollHit(c, r, e);
                if (o == HitOutcome.Miss || o == HitOutcome.Resist) misses++;
            }
            return misses;
        }

        [Test]
        public static void HeightenedSensesOnlySpellsAndRangedMissMore()
        {
            var r = Hero(ClassId.Rogue, 60);
            r.Talents["rogue_subtlety_heightened_senses"] = 2;
            r.InvalidateStats();
            AssertNear(r.Stats.ChanceToBeHit, 0f, 0.001f, "no generic ChanceToBeHit (melee unaffected)");
            AssertNear(Specials.IncomingMissChance(r, AttackKind.Spell), 4f, 0.001f, "spells +4% at 2/2");
            AssertNear(Specials.IncomingMissChance(r, AttackKind.Ranged), 4f, 0.001f, "ranged +4% at 2/2");
            AssertNear(Specials.IncomingMissChance(r, AttackKind.Melee), 0f, 0.001f, "melee +0%");
            r.Talents["rogue_subtlety_heightened_senses"] = 1;
            AssertNear(Specials.IncomingMissChance(r, AttackKind.Spell), 2f, 0.001f, "spells +2% at 1/2");

            const int n = 3000;
            Assert(AbilityRules.KindOf(Db.Ability("mage_frostbolt")) == AttackKind.Spell, "frostbolt is a spell");
            Assert(AbilityRules.KindOf(Db.Ability("warrior_shoot")) == AttackKind.Ranged, "warrior shoot is ranged");
            Assert(AbilityRules.KindOf(Db.Ability("warrior_mortal_strike")) == AttackKind.Melee, "mortal strike is melee");
            int spell0 = Misses(ClassId.Mage, "mage_frostbolt", false, n), spell1 = Misses(ClassId.Mage, "mage_frostbolt", true, n);
            int rng0 = Misses(ClassId.Warrior, "warrior_shoot", false, n), rng1 = Misses(ClassId.Warrior, "warrior_shoot", true, n);
            int mel0 = Misses(ClassId.Warrior, "warrior_mortal_strike", false, n), mel1 = Misses(ClassId.Warrior, "warrior_mortal_strike", true, n);
            float ds = (spell1 - spell0) * 100f / n, dr = (rng1 - rng0) * 100f / n;
            Assert(ds > 2.5f && ds < 5.5f, $"spell misses +4% (without {spell0}, with {spell1} of {n})");
            Assert(dr > 2.5f && dr < 5.5f, $"ranged misses +4% (without {rng0}, with {rng1} of {n})");
            Assert(mel0 == mel1, $"melee misses unchanged (without {mel0}, with {mel1} of {n})");
        }

        // ------------------------------------------------------------- 4. Bestial Discipline focus

        [Test]
        public static void BestialDisciplineScalesPetFocusRegen()
        {
            var (b, h, foe) = Setup(ClassId.Hunter, 60, dist: 14f);
            h.Talents["hunter_bm_bestial_discipline"] = 2;
            if (h.Pet == null) { Fresh(h); Assert(b.UseAbility(h, "hunter_call_pet", h).Ok, "call pet"); }
            var pet = h.Pet;
            Assert(pet != null && pet.PowerType == ResourceType.Focus, "focus pet out");
            pet.InvalidateStats();
            AssertNear(pet.Stats.EnergyRegen, 1.2f, 0.001f, "Bestial Discipline 2/2: +20% EnergyRegen on the pet");
            if (h.Pending != null) SkipTo(b, h);
            pet.Focus = 0f;
            b.EndTurn(h);
            Assert(b.ActiveUnit == pet, "the pet acts right after the hunter");
            AssertNear(pet.Focus, RulesConstants.FocusPerTurn * 1.2f, 0.01f, "in combat: 36 focus per turn × 1.2");

            var f = Battle.CreateField(Db, new Rng(3), new List<Unit> { h, pet }, new StraightLinePathfinder(80f, 60f), new Inventory());
            pet.Focus = 0f;
            f.TickOutOfCombat(1f);
            AssertNear(pet.Focus, RulesConstants.FocusPerSecondOoc * 1.2f, 0.01f, "out of combat: 6 focus per second × 1.2");
        }

        // ------------------------------------------------------------- 5. Elemental Mastery

        [Test]
        public static void ElementalMasteryOnlyForDamageSpells()
        {
            var (b, s, foe) = Setup(ClassId.Shaman, 60, dist: 10f);
            s.Tough();
            var em = b.ApplyAura(s, s, Db.Aura("shaman_elemental_mastery"));
            Assert(em != null, "elemental mastery up");
            Assert(Cost(s, "shaman_lightning_bolt") == 0f && Cost(s, "shaman_flame_shock") == 0f, "damage spells cost no mana");
            Assert(Cost(s, "shaman_healing_wave") > 0f && Cost(s, "shaman_chain_heal") > 0f, "heals pay full mana");
            Assert(Cost(s, "shaman_searing_totem") > 0f && Cost(s, "shaman_healing_stream_totem") > 0f, "totems pay full mana");
            AssertNear(Crit(b, s, "shaman_lightning_bolt", foe, false), 100f, 0.01f, "lightning bolt is a guaranteed crit");
            Assert(Crit(b, s, "shaman_healing_wave", s, true) < 99f, "healing wave gets no crit bonus");

            // a heal and a totem neither benefit nor consume it
            s.Health = s.MaxHealth * 0.5f;
            Fresh(s);
            float mana = s.Mana;
            var hw = b.UseAbility(s, "shaman_healing_wave", s);
            Assert(hw.Ok, "healing wave: " + hw.Reason);
            if (s.Pending != null) SkipTo(b, s);
            Assert(s.HasAura("shaman_elemental_mastery"), "healing wave does not consume Elemental Mastery");
            Assert(s.Mana < mana, "healing wave paid its mana");
            Fresh(s);
            mana = s.Mana;
            var st = b.UseAbility(s, "shaman_searing_totem", s);
            Assert(st.Ok, "searing totem: " + st.Reason);
            Assert(s.HasAura("shaman_elemental_mastery") && s.Mana < mana, "a totem pays and does not consume it");

            // the damage spell: free, crit, consumes it
            Fresh(s);
            mana = s.Mana;
            int from = b.Events.Count;
            var lb = b.UseAbility(s, "shaman_lightning_bolt", foe);
            Assert(lb.Ok, "lightning bolt: " + lb.Reason);
            if (s.Pending != null) SkipTo(b, s);
            AssertNear(s.Mana, mana, 0.01f, "lightning bolt cost no mana");
            Assert(!s.HasAura("shaman_elemental_mastery"), "lightning bolt consumed Elemental Mastery");
            var dmg = b.Events.Skip(from).Where(e => e.Type == CombatEventType.Damage && e.Source == s && e.AbilityId == "shaman_lightning_bolt").ToList();
            Assert(dmg.All(e => e.Crit), "the bolt critically hit");
        }

        // ------------------------------------------------------------- 6. Clearcasting (Elemental Focus)

        [Test]
        public static void ShamanClearcastingFreesOnlyDamageSpells()
        {
            var (b, s, foe) = Setup(ClassId.Shaman, 60, dist: 6f);
            s.Tough();
            s.Talents.Remove("shaman_ele_elemental_focus");   // no new Clearcasting procs in this test
            var cc = b.ApplyAura(s, s, Db.Aura("shaman_clearcasting"));
            Assert(cc != null, "clearcasting up");
            Assert(Cost(s, "shaman_lightning_bolt") == 0f && Cost(s, "shaman_frost_shock") == 0f, "damage spells are free");
            Assert(Cost(s, "shaman_healing_wave") > 0f, "Healing Wave (Nature) is not free while Clearcasting is up");
            Assert(Cost(s, "shaman_searing_totem") > 0f && Cost(s, "shaman_magma_totem") > 0f, "Fire totems are not free");
            Assert(Crit(b, s, "shaman_lightning_bolt", foe, false) < 99f, "clearcasting gives no crit");

            s.Health = s.MaxHealth * 0.5f;
            Fresh(s);
            float mana = s.Mana;
            Assert(b.UseAbility(s, "shaman_healing_wave", s).Ok, "healing wave");
            if (s.Pending != null) SkipTo(b, s);
            Assert(s.Mana < mana && s.HasAura("shaman_clearcasting"), "healing wave pays and keeps Clearcasting");

            Fresh(s);
            mana = s.Mana;
            var es = b.UseAbility(s, "shaman_earth_shock", foe);
            Assert(es.Ok, "earth shock: " + es.Reason);
            AssertNear(s.Mana, mana, 0.01f, "earth shock was free");
            Assert(!s.HasAura("shaman_clearcasting"), "earth shock consumed Clearcasting");
        }

        [Test]
        public static void MageClearcastingFreesOnlyDamageSpells()
        {
            var (b, m, foe) = Setup(ClassId.Mage, 60, dist: 6f);
            m.Tough();
            m.Talents.Remove("mage_arcane_arcane_concentration");   // no new Clearcasting procs in this test
            var cc = b.ApplyAura(m, m, Db.Aura("mage_clearcasting"));
            Assert(cc != null, "clearcasting up");
            Assert(Cost(m, "mage_frostbolt") == 0f && Cost(m, "mage_fire_blast") == 0f, "damage spells are free");
            Assert(Cost(m, "mage_polymorph") > 0f && Cost(m, "mage_arcane_intellect") > 0f, "utility spells are not free");
        }

        // ------------------------------------------------------------- 7. Greater Blessings, Divine Favor

        [Test]
        public static void GreaterBlessingReachesTheTargetsClass()
        {
            var b = NewBattle(6, new Inventory());
            var pal = Hero(ClassId.Paladin, 60, name: "Pal").At(20f, 20f);
            var w1 = Hero(ClassId.Warrior, 60, name: "W1").At(22f, 20f);
            var w2 = Hero(ClassId.Warrior, 60, name: "W2").At(60f, 50f);
            var mage = Hero(ClassId.Mage, 60, name: "Mage").At(21f, 22f);
            var foe = Mob("cr_bandit_cutthroat", 55).At(35f, 20f).Tough();
            foreach (var u in new[] { pal, w1, w2, mage }) { u.AutoPlay = false; b.AddUnit(u); }
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, pal);
            Fresh(pal);
            var r = b.UseAbility(pal, "paladin_greater_blessing_of_might", w1);
            Assert(r.Ok, "greater blessing of might: " + r.Reason);
            Assert(w1.HasAura("paladin_greater_blessing_of_might", pal), "target blessed");
            Assert(w2.HasAura("paladin_greater_blessing_of_might", pal), "the other warrior is blessed too");
            Assert(!mage.HasAura("paladin_greater_blessing_of_might") && !pal.HasAura("paladin_greater_blessing_of_might"),
                "other classes are not blessed");
            var a1 = w1.FindAura("paladin_greater_blessing_of_might");
            var a2 = w2.FindAura("paladin_greater_blessing_of_might");
            Assert(a2.Rank == a1.Rank && Math.Abs(a2.Remaining - a1.Remaining) < 0.01f && Math.Abs(a2.ModValues[0] - a1.ModValues[0]) < 0.01f,
                "same rank, duration and value");
        }

        [Test]
        public static void DivineFavorOnlyForHolyLightFlashAndShock()
        {
            var b = NewBattle(8, new Inventory());
            var pal = Hero(ClassId.Paladin, 60, name: "Pal").At(20f, 20f);
            var ally = Hero(ClassId.Warrior, 60, name: "Ally").At(22f, 20f).Tough();
            var foe = Mob("cr_bandit_cutthroat", 55).At(30f, 20f).Tough();
            pal.AutoPlay = false; ally.AutoPlay = false;
            b.AddUnit(pal); b.AddUnit(ally); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, pal);
            var df = b.ApplyAura(pal, pal, Db.Aura("paladin_divine_favor"));
            Assert(df != null, "divine favor up");
            AssertNear(Crit(b, pal, "paladin_holy_light", ally, true), 100f, 0.01f, "holy light crit guaranteed");
            AssertNear(Crit(b, pal, "paladin_flash_of_light", ally, true), 100f, 0.01f, "flash of light crit guaranteed");
            Assert(Crit(b, pal, "paladin_exorcism", foe, false) < 99f, "Exorcism (Holy) gets no crit bonus");
            Assert(Crit(b, pal, "paladin_holy_wrath", foe, false) < 99f, "Holy Wrath gets no crit bonus");
            Assert(Cost(pal, "paladin_holy_light") > 0f, "Divine Favor does not make the heal free");

            ally.Health = ally.MaxHealth * 0.3f;
            Fresh(pal);
            int from = b.Events.Count;
            var r = b.UseAbility(pal, "paladin_holy_light", ally);
            Assert(r.Ok, "holy light: " + r.Reason);
            if (pal.Pending != null) SkipTo(b, pal);
            var heal = b.Events.Skip(from).FirstOrDefault(e => e.Type == CombatEventType.Heal && e.Source == pal && e.AbilityId == "paladin_holy_light");
            Assert(heal != null && heal.Crit, "holy light critically healed");
            Assert(!pal.HasAura("paladin_divine_favor"), "and consumed Divine Favor");
        }

        // ------------------------------------------------------------- 8. Amplify Curse

        [Test]
        public static void AmplifiedCurseOfExhaustionIsFlatTwenty()
        {
            var (b, w, foe) = Setup(ClassId.Warlock, 60, dist: 10f);
            b.ApplyAura(w, w, Db.Aura("warlock_amplify_curse"));
            var coe = Db.Aura("warlock_curse_of_exhaustion");
            var mods = new AbilityModSet { EffectPct = 50f };   // a +50% Effect mod on the curse
            var inst = b.ApplyAura(w, foe, coe, new AuraApplyInfo { Source = Db.Ability("warlock_curse_of_exhaustion"), EffLevel = 60, LearnLevel = 30, Mods = mods });
            Assert(inst != null, "curse applied");
            AssertNear(inst.ModValues[0], -10f * 1.5f - 20f, 0.01f, "amplified: base × Effect, then 20 points more (flat)");
            Assert(!w.HasAura("warlock_amplify_curse"), "amplify curse consumed");
        }

        // ------------------------------------------------------------- 9. session: items the unit cannot use

        [Test]
        public static void SessionGreysOutRestrictedItems()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 1);
            s.Inventory.Add(Db.Item("potion_healing"), 1);
            s.Inventory.Add(Db.Item("mage_mana_agate"), 1);
            var potion = s.Inventory.Find("potion_healing");
            var agate = s.Inventory.Find("mage_mana_agate");
            var why = s.CannotUseItemReason(s.Main, potion, s.Main);
            Assert(why != null && why.Contains("level 12"), "level 12 potion greyed out at level 1: " + why);
            why = s.CannotUseItemReason(s.Main, potion);
            Assert(why != null && why.Contains("level 12"), "also without a target: " + why);
            why = s.CannotUseItemReason(s.Main, agate);
            Assert(why != null && why.Contains("Mage"), "mage-only item greyed out for a warrior: " + why);
            Assert(!s.UseItem(s.Main, potion, s.Main).Ok, "and using it fails");
        }
    }
}
