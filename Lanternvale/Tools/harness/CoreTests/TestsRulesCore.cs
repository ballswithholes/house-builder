// Rules engine unit tests: formulas, stats, turns and time, casting, resources, auto attacks, auras, threat,
// equipment, progression, tooltips, out-of-combat field operations.
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
    public static class TestsRulesCore
    {
        // ------------------------------------------------------------ data / registry

        [Test]
        public static void EveryDocumentedSpecialIsImplemented()
        {
            var missing = Db.Specials.Keys.Where(k => !Specials.IsImplemented(k)).ToList();
            Assert(missing.Count == 0, "unimplemented specials: " + string.Join(", ", missing));
        }

        [Test]
        public static void FormulasAreSane()
        {
            float a60 = Formulas.ArmorReduction(3000f, 60);
            Assert(a60 > 0.3f && a60 < 0.75f, $"armor 3000 vs L60 reduction {a60}");
            Assert(Formulas.ArmorReduction(1e7f, 60) <= 0.75f + 1e-4f, "armor cap 75%");
            Assert(Formulas.SpellMissChance(60, 60) < Formulas.SpellMissChance(60, 63), "spell miss grows with level difference");
            Assert(Formulas.GreyLevel(60) < 60 && Formulas.GreyLevel(10) < 10, "grey level below own level");
        }

        [Test]
        public static void CreatureCurves()
        {
            var plain = new CreatureDef { id = "t", type = CreatureType.Beast, rank = CreatureRank.Normal };
            AssertNear(CreatureScaling.Health(plain, 10), 251f, 1f, "L10 health");
            AssertNear(CreatureScaling.Health(plain, 60), 3626f, 2f, "L60 health");
            // the Warden at party level 12 should be around 3,100 health (content multipliers on top)
            var warden = Db.Creature("cr_hollow_warden");
            if (warden != null)
            {
                var w = UnitFactory.CreateCreature(Db, warden, UnitFactory.CreatureLevel(warden, 0, 12, new Rng(1)));
                Assert(w.MaxHealth > 2700f && w.MaxHealth < 3500f, $"Warden at party level 12 has {w.MaxHealth} health (≈3,100 expected)");
            }
        }

        [Test]
        public static void CharacterStatsScaleWithLevel()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Mage, ClassId.Rogue, ClassId.Hunter })
            {
                var lo = Hero(c, 1, talents: false, gear: false);
                var hi = Hero(c, 60, talents: false, gear: false);
                Assert(hi.MaxHealth > lo.MaxHealth * 5f, $"{c}: L60 health {hi.MaxHealth} vs L1 {lo.MaxHealth}");
                if (c == ClassId.Mage) Assert(hi.MaxMana > lo.MaxMana * 4f, "mage mana grows");
                if (c == ClassId.Rogue) Assert(hi.MaxResource(ResourceType.Energy) >= 100f, "rogue energy");
            }
        }

        // ------------------------------------------------------------ turns / time / casting

        static (Battle b, Unit hero, Unit foe) Duel(ClassId c, int level, string mob = "cr_wolf", int mobLevel = 0, float dist = 2f, int seed = 5, bool gear = true)
        {
            var b = NewBattle(seed, new Inventory());
            var hero = Hero(c, level, gear: gear).At(20f, 20f);
            var foe = Mob(mob, mobLevel > 0 ? mobLevel : Math.Max(1, level - 2)).At(20f + dist, 20f);
            hero.AutoPlay = false;
            hero.FaceTowards(foe.Position);
            foe.FaceTowards(hero.Position);
            b.AddUnit(hero);
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, hero);
            return (b, hero, foe);
        }

        [Test]
        public static void InstantSpellCostsGcdAndCastsUseTime()
        {
            var (b, mage, foe) = Duel(ClassId.Mage, 30, dist: 7f);
            Assert(b.ActiveUnit == mage, "mage's turn");
            float t0 = mage.TimeLeft;
            var r = b.UseAbility(mage, "mage_fire_blast", foe);
            Assert(r.Ok, "fire blast: " + r.Reason);
            AssertNear(t0 - mage.TimeLeft, mage.Class.gcd, 0.01f, "instant costs the GCD");
            float t1 = mage.TimeLeft;
            var fb = Db.Ability("mage_frostbolt");
            float cast = AbilityRules.CastTime(mage, fb, AbilityMods.For(mage, fb));
            r = b.UseAbility(mage, "mage_frostbolt", foe);
            Assert(r.Ok, "frostbolt: " + r.Reason);
            if (cast <= t1) AssertNear(t1 - mage.TimeLeft, Math.Max(cast, mage.Class.gcd), 0.01f, "cast spends its cast time");
        }

        [Test]
        public static void LongCastBecomesPendingAndResolvesNextTurn()
        {
            var (b, wl, foe) = Duel(ClassId.Warlock, 30, dist: 15f);
            b.Inventory.Add(Db.Item("soul_shard"), 3);
            wl.Mana = wl.MaxMana;
            var r = b.UseAbility(wl, "warlock_summon_voidwalker");
            Assert(r.Ok, "summon voidwalker: " + r.Reason);
            Assert(wl.Pending != null, "10 s cast is pending");
            Assert(b.ActiveUnit != wl, "turn ended");
            int shards = b.Inventory.Count("soul_shard");
            Assert(shards == 3, "shard not consumed before completion");
            int guard = 0;
            while (wl.Pending != null && guard++ < 10) { SkipTo(b, wl); if (wl.Pending != null) b.EndTurn(wl); }
            Assert(wl.Pet != null && wl.Pet.Creature.id == "warlock_demon_voidwalker", "voidwalker summoned on completion");
            Assert(b.Inventory.Count("soul_shard") == 2, "one shard consumed on completion");
        }

        [Test]
        public static void InterruptCancelsPendingAndLocksSchool()
        {
            var (b, rogue, foe) = Duel(ClassId.Rogue, 30);
            var caster = Mob("cr_bandit_hexer", 28).At(21.5f, 21f);
            b.AddUnit(caster);
            var spell = Db.Ability("warlock_shadow_bolt");
            caster.Pending = new PendingCast { Ability = spell, Rank = 1, Target = rogue, RemainingTime = 2f };
            rogue.Energy = 100f;
            var r = b.UseAbility(rogue, "rogue_kick", caster);
            Assert(r.Ok, "kick: " + r.Reason);
            bool interrupted = b.Events.Any(e => e.Type == CombatEventType.CastInterrupted && e.Target == caster);
            bool missed = b.Events.Any(e => (e.Type == CombatEventType.Miss || e.Type == CombatEventType.Dodge || e.Type == CombatEventType.Parry) && e.Target == caster);
            Assert(interrupted || missed, "kick interrupts (or is avoided)");
            if (interrupted)
            {
                Assert(caster.Pending == null, "pending cleared");
                Assert(caster.IsSchoolLocked(School.Shadow), "shadow school locked out");
            }
        }

        [Test]
        public static void ComboPointsAndFinishers()
        {
            var (b, rogue, foe) = Duel(ClassId.Rogue, 30, mobLevel: 20);
            foe.Tough();
            int hits = 0;
            for (int i = 0; i < 8 && hits < 2; i++)
            {
                rogue.Energy = 100f;
                rogue.TimeLeft = 6f;
                var r = b.UseAbility(rogue, "rogue_sinister_strike", foe);
                Assert(r.Ok, "sinister strike: " + r.Reason);
                hits = rogue.ComboPoints;
            }
            Assert(rogue.ComboPoints >= 2 && rogue.ComboTarget == foe, $"combo points built ({rogue.ComboPoints})");
            rogue.TimeLeft = 6f;
            rogue.Energy = 100f;
            int cp = rogue.ComboPoints;
            var ev = b.UseAbility(rogue, "rogue_eviscerate", foe);
            Assert(ev.Ok, "eviscerate: " + ev.Reason);
            Assert(rogue.ComboPoints == 0 || b.Events.Last(e => e.Target == foe).Type != CombatEventType.Damage, "finisher spends combo points on hit");
        }

        [Test]
        public static void AutoAttacksFollowSwingTimers()
        {
            var (b, war, foe) = Duel(ClassId.Warrior, 20);
            foe.Tough();
            var w = StatCalculator.GetWeapon(war, WeaponSlot.MainHand);
            b.StartAutoAttack(war, foe, Db.Ability("attack"), false);
            int before = b.Events.Count(e => e.Source == war && e.AutoAttack);
            b.EndTurn(war);
            int swings = b.Events.Count(e => e.Source == war && e.AutoAttack && (e.Type == CombatEventType.Damage || e.Type == CombatEventType.Miss || e.Type == CombatEventType.Dodge || e.Type == CombatEventType.Parry || e.Type == CombatEventType.Block)) - before;
            int expected = (int)Math.Floor(6f * war.Stats.MeleeHaste / w.Speed + 1e-3f);
            bool offhand = StatCalculator.GetWeapon(war, WeaponSlot.OffHand).Valid;
            Assert(swings >= expected && (offhand || swings <= expected + 1), $"swings {swings}, expected about {expected} (speed {w.Speed})");
            Assert(war.Rage > 0f || swings == 0, "white hits generate rage");
        }

        [Test]
        public static void PeriodicDamageTicksAtBearerTurnStart()
        {
            var (b, pr, foe) = Duel(ClassId.Priest, 30, dist: 12f, mobLevel: 20);
            foe.Tough();
            Assert(b.UseAbility(pr, "priest_shadow_word_pain", foe).Ok, "SW:P");
            var aura = foe.FindAura("priest_shadow_word_pain");
            if (aura == null) return; // resisted (rare): nothing more to check
            float hp = foe.Health;
            SkipTo(b, foe);
            Assert(foe.Health < hp, "SW:P ticked at the start of the bearer's turn");
            Assert(b.Events.Any(e => e.Type == CombatEventType.Damage && e.Periodic && e.Target == foe), "periodic damage event");
        }

        [Test]
        public static void ShieldsAbsorbAndWeakenedSoulBlocksRecast()
        {
            var (b, pr, foe) = Duel(ClassId.Priest, 30, dist: 4f);
            Assert(b.UseAbility(pr, "priest_power_word_shield", pr).Ok, "PW:S on self");
            var sh = pr.FindAura("priest_power_word_shield");
            Assert(sh != null && sh.AbsorbLeft > 0f, "shield absorb pool");
            float hp = pr.Health;
            b.DealDamage(foe, pr, 50f, School.Physical, new DamageInfo { Kind = AttackKind.Melee, IgnoreArmor = true });
            Assert(pr.Health >= hp - 0.5f, "absorbed");
            pr.Cooldowns.Clear();
            pr.TimeLeft = 6f;
            var chk = b.CanUse(pr, "priest_power_word_shield", pr);
            Assert(!chk.Ok && chk.Reason.Contains("Weakened Soul"), "Weakened Soul blocks recast: " + chk.Reason);
        }

        [Test]
        public static void FiveSecondRuleSlowsManaRegen()
        {
            var a = Hero(ClassId.Priest, 40);
            var c = Hero(ClassId.Priest, 40);
            var b = NewBattle(3);
            b.AddUnit(a.At(10, 10)); b.AddUnit(c.At(12, 10));
            var dummy = Mob("cr_training_dummy", 40).At(30, 10);
            b.AddUnit(dummy);
            b.Begin();
            a.Mana = c.Mana = a.MaxMana * 0.3f;
            a.ManaSpentTurn = -10; a.SecondsSinceManaSpent = 100f;
            c.ManaSpentTurn = c.TurnsTaken; c.SecondsSinceManaSpent = 0f;
            SkipTo(b, a); float ga = a.Mana;
            SkipTo(b, c); float gc = c.Mana;
            Assert(ga > c.MaxMana * 0.3f, "regen outside the five-second rule");
            Assert(gc <= ga + 0.5f, $"regen inside FSR ({gc}) not above outside ({ga})");
        }

        [Test]
        public static void ThreatTargetingAndTaunt()
        {
            var b = NewBattle(9);
            var tank = Hero(ClassId.Warrior, 30, name: "Tank").At(20, 20);
            var dps = Hero(ClassId.Mage, 30, name: "Mage").At(14, 20);
            var foe = Mob("cr_bandit_cutthroat", 28).At(22, 20);
            tank.AutoPlay = dps.AutoPlay = false;
            b.AddUnit(tank); b.AddUnit(dps); b.AddUnit(foe);
            b.Begin();
            b.AddThreat(foe, dps, 500f, true);
            b.AddThreat(foe, tank, 100f, true);
            Assert(b.SelectThreatTarget(foe, false) == dps, "top threat is targeted");
            b.Taunt(tank, foe);
            Assert(b.SelectThreatTarget(foe, false) == tank, "taunt forces the target");
            Assert(b.ThreatOf(foe, tank) >= b.ThreatOf(foe, dps) - 0.5f, "taunt matches the top threat");
        }

        [Test]
        public static void HealingSplitsThreatAndNoThreatTag()
        {
            var (b, pr, foe) = Duel(ClassId.Priest, 30, dist: 4f);
            pr.Health = pr.MaxHealth * 0.5f;
            b.AddThreat(foe, pr, 0f, true);
            float before = b.ThreatOf(foe, pr);
            b.HealUnit(pr, pr, 200f, new HealInfo { Name = "test" });
            Assert(b.ThreatOf(foe, pr) > before, "healing makes threat");
            float mid = b.ThreatOf(foe, pr);
            pr.Health = pr.MaxHealth * 0.5f;
            b.HealUnit(pr, pr, 200f, new HealInfo { Name = "test", NoThreat = true });
            AssertNear(b.ThreatOf(foe, pr), mid, 0.01f, "NoThreat heal");
        }

        // ------------------------------------------------------------ equipment / items

        [Test]
        public static void EquipmentRulesWork()
        {
            var mage = Hero(ClassId.Mage, 20, gear: false);
            var plate = Db.Items.Values.FirstOrDefault(i => i.equip == EquipType.Chest && i.armorType == ArmorType.Plate);
            if (plate != null) Assert(EquipmentRules.CannotEquipReason(mage, plate, EquipSlot.Chest) != null, "mage cannot wear plate");
            var war = Hero(ClassId.Warrior, 20, gear: false);
            var twoH = Db.Items.Values.FirstOrDefault(i => i.equip == EquipType.TwoHand && i.requiredLevel <= 20 && EquipmentRules.CannotEquipReason(war, i, EquipSlot.MainHand) == null);
            var oneH = Db.Items.Values.FirstOrDefault(i => i.equip == EquipType.OneHand && i.requiredLevel <= 20 && EquipmentRules.CannotEquipReason(war, i, EquipSlot.MainHand) == null);
            if (twoH != null && oneH != null)
            {
                EquipmentRules.Equip(war, new ItemInstance(oneH), EquipSlot.MainHand);
                var displaced = EquipmentRules.Equip(war, new ItemInstance(twoH), EquipSlot.MainHand);
                Assert(war.Equipment.MainHand.Def == twoH && displaced.Count >= 1, "two-hander replaces the main hand");
                Assert(war.Equipment.OffHand == null, "two-hander leaves the off hand empty");
            }
            // shaman two-handed axes/maces need the talent
            var sham = Hero(ClassId.Shaman, 40, talents: false, gear: false);
            Assert(!EquipmentRules.CanUseWeapon(sham, WeaponType.TwoHandAxe) || sham.Class.weaponTypes.Contains(WeaponType.TwoHandAxe), "no 2H axes without talent");
            sham.Talents["shaman_enh_two_handed_axes_and_maces"] = 1;
            Assert(EquipmentRules.CanUseWeapon(sham, WeaponType.TwoHandAxe), "talent grants 2H axes");
        }

        [Test]
        public static void InventoryStacksAndSoulShardCap()
        {
            var inv = new Inventory();
            inv.Add(Db.Item("soul_shard"), 40);
            Assert(inv.Count("soul_shard") == RulesConstants.MaxSoulShards, $"soul shards capped at {RulesConstants.MaxSoulShards} (got {inv.Count("soul_shard")})");
            Assert(inv.Remove("soul_shard", 2) && inv.Count("soul_shard") == RulesConstants.MaxSoulShards - 2, "remove");
        }

        // ------------------------------------------------------------ progression

        [Test]
        public static void ExperienceAndLevels()
        {
            var u = Hero(ClassId.Paladin, 1, talents: false, gear: false);
            int need = Progression.XpToNextLevel(Db, 1);
            var ups = Progression.GiveXp(u, need + 5);
            Assert(u.Level == 2 && ups.Count == 1, $"level up to 2 (level {u.Level})");
            var grey = Mob("cr_wolf", 1);
            Assert(Progression.KillXp(Db, 30, grey) == 0, "grey mobs give no XP");
            var even = Mob("cr_wolf", 10);
            Assert(Progression.KillXp(Db, 10, even) > 0, "even-level mob gives XP");
        }

        [Test]
        public static void TalentsGateAndRespec()
        {
            var u = Hero(ClassId.Warrior, 20, talents: false, gear: false);
            Assert(Progression.TalentPointsAvailable(u) == 11, $"11 points at level 20 (got {Progression.TalentPointsAvailable(u)})");
            Progression.AutoAllocateTalents(u);
            Assert(Progression.TalentPointsAvailable(u) == 0, "auto allocation spends all points");
            int cost = Progression.RespecCost(u);
            Assert(cost > 0, "respec costs gold");
            Progression.ResetTalents(u, new Inventory());
            Assert(Progression.TalentPointsAvailable(u) == 11 && u.Talents.Count == 0, "respec refunds points");
        }

        [Test]
        public static void TooltipsResolveTokens()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Rogue, ClassId.Mage, ClassId.Priest, ClassId.Paladin, ClassId.Hunter, ClassId.Shaman, ClassId.Warlock })
            {
                var u = Hero(c, 60, gear: false);
                foreach (var kv in u.Abilities)
                {
                    var a = Db.Ability(kv.Key);
                    if (a == null) continue;
                    var tip = Tooltip.Ability(u, a);
                    Assert(!System.Text.RegularExpressions.Regex.IsMatch(tip, @"\{d?\d+\}"), $"{a.id}: unresolved token in '{tip}'");
                }
            }
        }

        // ------------------------------------------------------------ out of combat

        [Test]
        public static void FieldBuffsTicksAndFood()
        {
            var pr = Hero(ClassId.Priest, 30, name: "Priest");
            var war = Hero(ClassId.Warrior, 30, name: "Warrior");
            var inv = new Inventory();
            var field = Battle.CreateField(Db, new Rng(4), new[] { pr, war }, new StraightLinePathfinder(), inv);
            var r = field.UseAbility(pr, "priest_power_word_fortitude", war);
            Assert(r.Ok, "out-of-combat buff: " + r.Reason);
            Assert(war.HasAura("priest_power_word_fortitude"), "buff applied");
            war.Health = war.MaxHealth * 0.5f;
            pr.Mana = pr.MaxMana * 0.2f;
            float h = war.Health, m = pr.Mana;
            field.TickOutOfCombat(10f);
            Assert(war.Health > h && pr.Mana > m, "out-of-combat regeneration");
            field.TickOutOfCombat(2000f);
            Assert(!war.HasAura("priest_power_word_fortitude"), "buff expires in real time");
            // food/drink: notInCombat items work in the field, not in battle
            var food = Db.Items.Values.FirstOrDefault(i => !string.IsNullOrEmpty(i.use) && Db.Ability(i.use)?.requires != null && Db.Ability(i.use).requires.notInCombat);
            if (food != null)
            {
                inv.Add(food, 1);
                var item = inv.Find(food.id);
                var ok = field.UseItem(war, item, war);
                Assert(ok.Ok, "food out of combat: " + ok.Reason);
                var b = NewBattle(2, inv);
                b.AddUnit(war.At(10, 10)); b.AddUnit(Mob("cr_wolf", 28).At(14, 10));
                b.Begin(); SkipTo(b, war);
                inv.Add(food, 1);
                Assert(!b.UseItem(war, inv.Find(food.id), war).Ok, "food is not usable in combat");
            }
        }

        [Test]
        public static void SelfResurrectionWithSoulstone()
        {
            var b = NewBattle(12, new Inventory());
            var pal = Hero(ClassId.Paladin, 40, name: "Pal").At(20, 20);
            var other = Hero(ClassId.Warrior, 40, name: "War").At(18, 20);
            var foe = Mob("cr_bandit_chief", 40).At(22, 20);
            pal.AutoPlay = false;
            b.AddUnit(pal); b.AddUnit(other); b.AddUnit(foe);
            b.Begin();
            var ss = Db.Aura("warlock_soulstone_minor");
            b.ApplyAura(other, pal, ss);
            b.DealDamage(foe, pal, pal.Health + 50f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(pal.Downed, "downed");
            Assert(pal.SelfRes != null && !pal.HasAura(ss.id), "soulstone consumed into an offer");
            int guard = 0;
            while (b.ActiveUnit != pal && guard++ < 20 && !b.IsOver) b.EndTurn(b.ActiveUnit);
            Assert(b.PendingSelfResurrection(pal) != null, "offer at the unit's turn slot");
            Assert(b.AcceptSelfResurrection(pal).Ok, "accept");
            Assert(pal.IsAlive && pal.Health >= 399f && b.ActiveUnit == pal, "risen and taking the turn");
        }

        [Test]
        public static void DisengageFromPassiveEncounter()
        {
            var b = NewBattle(3);
            var u = Hero(ClassId.Mage, 10).At(10, 10);
            var d = Mob("cr_training_dummy", 10).At(14, 10);
            b.AddUnit(u); b.AddUnit(d);
            b.Begin();
            Assert(b.CanDisengage, "a dummy can be left");
            Assert(b.Disengage().Ok && b.Outcome == BattleOutcome.Fled, "disengaged");
        }
    }
}
