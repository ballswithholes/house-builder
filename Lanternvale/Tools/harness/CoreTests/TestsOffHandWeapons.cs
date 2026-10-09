// Rogue dual wield from level 1 and Off Hand weapons (WoW "Off Hand" daggers, swords, fist weapons...):
//  - every rogue dual wields from level 1 with no training: new games, veteran starts, recruited rogue companions and
//    characters loaded from saves made before the change (which do not know the Dual Wield passive);
//  - warriors and hunters keep level 20 + the trainer; casters never dual wield;
//  - an Off Hand weapon (equip OffHand + a one-hand melee weapon type) goes only in the off hand and needs dual wield
//    exactly like a One-Hand weapon there; shields and held-in-off-hand items keep their rules;
//  - off-hand swings happen in battle at the off-hand damage factor; generated Off Hand weapons have one-hand DPS and
//    the OffHand stat budget; the validator checks Off Hand weapons.
// Uses test-local ItemDefs so it does not depend on the Off Hand weapons of the content data.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsOffHandWeapons
    {
        // ------------------------------------------------------------------------------------------ test items

        static ItemDef Weapon(string id, EquipType equip, WeaponType type, float dmg = 20f, float speed = 2f) => new ItemDef
        {
            id = id, name = id, icon = "dagger", description = "test weapon", kind = ItemKind.Weapon, quality = Quality.Common,
            itemLevel = 2, requiredLevel = 1, equip = equip, weaponType = type, minDamage = dmg, maxDamage = dmg, speed = speed, price = 1,
        };

        static readonly ItemDef OneHandDagger = Weapon("t_oh_one_hand_dagger", EquipType.OneHand, WeaponType.Dagger);
        static readonly ItemDef OffHandDagger = Weapon("t_oh_off_hand_dagger", EquipType.OffHand, WeaponType.Dagger);
        static readonly ItemDef OffHandSword = Weapon("t_oh_off_hand_sword", EquipType.OffHand, WeaponType.OneHandSword);
        static readonly ItemDef OffHandMace = Weapon("t_oh_off_hand_mace", EquipType.OffHand, WeaponType.OneHandMace);
        static readonly ItemDef OneHandSword = Weapon("t_oh_one_hand_sword", EquipType.OneHand, WeaponType.OneHandSword);
        static readonly ItemDef TwoHandSword = Weapon("t_oh_two_hand_sword", EquipType.TwoHand, WeaponType.TwoHandSword, 40f, 3.5f);
        static readonly ItemDef Shield = new ItemDef
        {
            id = "t_oh_shield", name = "Test Shield", icon = "shield", kind = ItemKind.Armor, quality = Quality.Common, itemLevel = 2,
            requiredLevel = 1, equip = EquipType.OffHand, weaponType = WeaponType.Shield, armor = 40f, block = 4f,
        };
        static readonly ItemDef Orb = new ItemDef
        {
            id = "t_oh_orb", name = "Test Orb", icon = "arcane_orb", kind = ItemKind.Weapon, quality = Quality.Common, itemLevel = 2,
            requiredLevel = 1, equip = EquipType.OffHand, weaponType = WeaponType.HeldInOffhand,
        };

        /// <summary>Puts a test item in the bags and equips it through the session (asserting success).</summary>
        static ItemInstance EquipVia(GameSession s, Unit u, ItemDef def, EquipSlot? slot = null)
        {
            var it = new ItemInstance(def);
            s.Inventory.Add(it);
            Assert(s.CanEquip(u, it, slot) == null, $"{u.Name} can equip {def.id}{(slot.HasValue ? " in " + slot : "")}: {s.CanEquip(u, it, slot)}");
            Assert(s.Equip(u, it, slot) == null, $"{u.Name} equips {def.id}");
            return it;
        }

        static void AssertDualWields(GameSession s, Unit u, string tag)
        {
            Assert(EquipmentRules.CanDualWield(u), $"{tag}: can dual wield");
            Assert(EquipmentRules.CannotDualWieldReason(u) == null, $"{tag}: no reason against dual wield");
            Assert(u.Knows("rogue_dual_wield"), $"{tag}: Dual Wield in the spellbook");
            // a One-Hand weapon in each hand, then an Off Hand weapon in the off hand: no trainer, no level gate
            EquipVia(s, u, OneHandDagger, EquipSlot.MainHand);
            EquipVia(s, u, OneHandSword, EquipSlot.OffHand);
            Assert(u.Equipment.IsDualWielding, $"{tag}: two One-Hand weapons");
            var ohw = EquipVia(s, u, OffHandSword);   // ChooseSlot: the off hand
            Assert(u.Equipment.OffHand == ohw && u.Equipment.MainHand.Def == OneHandDagger && u.Equipment.IsDualWielding,
                $"{tag}: One-Hand dagger + Off Hand sword");
            Assert(s.Inventory.Items.Any(i => i.Def == OneHandSword), $"{tag}: the displaced off-hand sword went back to the bags");
        }

        // ------------------------------------------------------------------------------------------ rogues from level 1

        [Test]
        public static void Rogue_DualWieldsFromLevelOne_NewGame()
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 1, seed: 1101);
            var r = s.Main;
            Assert(r.Level == 1 && r.Class.dualWieldLevel == 1, "level 1 rogue, dualWieldLevel 1");
            // the starter kit: a One-Hand dagger and a real Off Hand dagger, both worn from the start
            Assert(r.Equipment.MainHand?.Def.id == "rogue_starter_dagger", "starter dagger in the main hand: " + r.Equipment.MainHand?.Def.id);
            Assert(r.Equipment.OffHand?.Def.id == "rogue_starter_dagger_offhand" && EquipmentRules.IsOffHandWeapon(r.Equipment.OffHand.Def),
                "starter Off Hand dagger in the off hand: " + r.Equipment.OffHand?.Def.id);
            Assert(r.Equipment.IsDualWielding, "dual wielding at level 1");
            // Dual Wield is a starting passive: known, never offered by the trainer, and it gates nothing
            Assert(EquipmentRules.ProficiencyPassive(r, EquipmentRules.Proficiency.DualWield) == null, "no trainer gate for rogues");
            Assert(!Progression.TrainerOffers(r, null).Any(o => o.Ability.id == "rogue_dual_wield"), "the trainer does not sell Dual Wield");
            var dw = Db.Ability("rogue_dual_wield");
            Assert(dw.passive && dw.learnLevel == 1 && dw.trainCost == 0 && Array.IndexOf(r.Class.startingAbilities, dw.id) >= 0, "a free level-1 starting passive");
            AssertDualWields(s, r, "new level-1 rogue");
        }

        [Test]
        public static void Rogue_DualWieldsFromLevelOne_VeteranStart()
        {
            foreach (int level in new[] { 2, 5, 12 })
            {
                var s = SessionTest.NewGame(ClassId.Rogue, level, seed: 1102);
                var r = s.Main;
                string tag = $"veteran L{level} rogue";
                Assert(r.Equipment.IsDualWielding && r.Equipment.OffHand.IsWeapon, $"{tag}: starts dual wielding ({r.Equipment.OffHand?.Def.id})");
                Assert(EquipmentRules.CannotEquipReason(r, r.Equipment.OffHand.Def, EquipSlot.OffHand) == null, $"{tag}: a legal off hand");
                AssertDualWields(s, r, tag);
            }
            // the veteran gear itself: the rogue's second weapon is an Off Hand weapon
            var u = UnitFactory.CreateCharacter(Db, ClassId.Rogue, "V", 12, learnAll: true);
            var gear = ItemGenerator.VeteranGear(Db, u, new Rng(5));
            var oh = gear.Select(i => i.Def).Where(EquipmentRules.IsOffHandWeapon).ToList();
            Assert(oh.Count == 1, "veteran rogue gear has one Off Hand weapon: " + string.Join(", ", gear.Select(i => i.Def.equip + " " + i.Def.weaponType)));
            Assert(gear.Count(i => i.Def.equip == EquipType.OneHand) == 1, "and one One-Hand weapon");
        }

        [Test]
        public static void Rogue_RecruitedCompanionDualWields()
        {
            // a recruited rogue companion in a level-1 party
            var s = SessionTest.NewGame(ClassId.Warrior, 1, seed: 1103);
            s.Recruit("pip");
            var pip = s.Roster.First(u => u.Companion != null && u.Companion.id == "pip");
            Assert(pip.ClassId == ClassId.Rogue, "Pip is a rogue");
            Assert(pip.Equipment.IsDualWielding, $"Pip dual wields on arrival (L{pip.Level}): {pip.Equipment.MainHand?.Def.id} + {pip.Equipment.OffHand?.Def.id}");
            AssertDualWields(s, pip, $"recruited Pip L{pip.Level}");

            // a level-1 rogue companion straight from the factory: dual wields with no training either
            var c = UnitFactory.CreateCompanion(Db, Db.Companions["pip"], 1);
            Assert(c.Level == 1 && EquipmentRules.CanDualWield(c) && c.Equipment.IsDualWielding,
                $"level-1 Pip dual wields: {c.Equipment.MainHand?.Def.id} + {c.Equipment.OffHand?.Def.id}");
        }

        [Test]
        public static void Rogue_OldSaveDualWieldsWithNoAction()
        {
            // a level-4 rogue saved before the change: no Dual Wield passive, the (then One-Hand) starter off-hand dagger in
            // the main hand and nothing in the off hand
            var s = SessionTest.NewGame(ClassId.Rogue, 4, seed: 1104, veteranGear: false);
            var r = s.Main;
            EquipmentRules.Unequip(r, EquipSlot.MainHand);
            EquipmentRules.Unequip(r, EquipSlot.OffHand);
            r.Abilities.Remove("rogue_dual_wield");
            r.Equipment[EquipSlot.MainHand] = new ItemInstance(Db.Item("rogue_starter_dagger_offhand"));
            string json = s.SaveGame();
            Assert(!json.Contains("rogue_dual_wield"), "the old-format save does not know Dual Wield");

            // even before any load step, not knowing the starting passive gates nothing
            Assert(EquipmentRules.CanDualWield(r), "an unknown starting passive does not gate dual wield");

            var s2 = new GameSession(Db, 1105);
            Assert(s2.LoadGame(json, out var err), "load: " + err);
            var r2 = s2.Main;
            Assert(r2.Knows("rogue_dual_wield"), "the starting passive is learned on load");
            Assert(r2.Equipment.MainHand == null, "the Off Hand dagger left the main hand on load");
            var dagger = s2.Inventory.Items.FirstOrDefault(i => i.Def.id == "rogue_starter_dagger_offhand");
            Assert(dagger != null, "... into the bags");
            Assert(s2.CanEquip(r2, dagger, EquipSlot.MainHand) != null, "it cannot go back into the main hand");
            Assert(s2.Equip(r2, dagger) == null && r2.Equipment.OffHand == dagger, "equipped: into the off hand");
            AssertDualWields(s2, r2, "loaded old-save rogue");
            Assert(s2.SaveGame().Contains("rogue_dual_wield"), "saved again with Dual Wield");
        }

        // ------------------------------------------------------------------------------------------ other classes

        [Test]
        public static void Warrior_DualWieldNeedsLevelTwentyAndTheTrainer()
        {
            var w19 = UnitFactory.CreateCharacter(Db, ClassId.Warrior, "W19", 19);
            var why = EquipmentRules.CannotUseReason(w19, OffHandSword);
            Assert(why != null && why.Contains("level 20"), "L19 warrior: an Off Hand weapon needs level 20: " + why);
            why = EquipmentRules.CannotEquipReason(w19, OneHandSword, EquipSlot.OffHand);
            Assert(why != null && why.Contains("level 20"), "L19 warrior: a One-Hand weapon in the off hand needs level 20: " + why);
            Assert(EquipmentRules.ChooseSlot(w19, OffHandSword) == null, "no slot for an Off Hand weapon");
            Assert(!LootContext.CanUse(w19, OffHandSword) && LootContext.CanUse(w19, OneHandSword), "loot: the Off Hand sword is not usable yet, the One-Hand one is");
            Assert(EquipmentRules.CannotEquipReason(w19, OneHandSword, EquipSlot.MainHand) == null, "a One-Hand weapon still goes in the main hand");

            var w = UnitFactory.CreateCharacter(Db, ClassId.Warrior, "W20", 20);
            why = EquipmentRules.CannotUseReason(w, OffHandSword);
            Assert(why != null && why.Contains("Dual Wield") && why.Contains("trainer"), "L20 untrained warrior: needs Dual Wield from the trainer: " + why);
            Assert(Progression.TrainerOffers(w, null).Any(o => o.Ability.id == "warrior_dual_wield" && o.CanTrain), "the trainer offers Dual Wield at 20");
            Assert(Progression.Train(w, Db.Ability("warrior_dual_wield"), 1, new Inventory { Gold = 100000 }) == null, "train Dual Wield");
            Assert(EquipmentRules.CanDualWield(w) && EquipmentRules.CannotUseReason(w, OffHandSword) == null, "trained: Off Hand weapons usable");
            Assert(EquipmentRules.ChooseSlot(w, OffHandSword) == EquipSlot.OffHand && LootContext.CanUse(w, OffHandSword), "slot and loot after training");

            // the same through a veteran start at 20 (learns everything) and the session's equip API
            var s = SessionTest.NewGame(ClassId.Warrior, 20, seed: 1106);
            Assert(EquipmentRules.CanDualWield(s.Main), "veteran L20 warrior dual wields");
            EquipVia(s, s.Main, OneHandSword, EquipSlot.MainHand);
            EquipVia(s, s.Main, OffHandSword);
            Assert(s.Main.Equipment.IsDualWielding && s.Main.Equipment.OffHand.Def == OffHandSword, "warrior: One-Hand + Off Hand sword");

            // hunters keep the same rule
            var h = UnitFactory.CreateCharacter(Db, ClassId.Hunter, "H", 19);
            Assert(!EquipmentRules.CanDualWield(h) && EquipmentRules.ProficiencyPassive(h, EquipmentRules.Proficiency.DualWield)?.id == "hunter_dual_wield",
                "hunter: level 20 and its trainer passive");
        }

        [Test]
        public static void Casters_CannotUseOffHandWeapons()
        {
            var mage = UnitFactory.CreateCharacter(Db, ClassId.Mage, "M", 30, learnAll: true);
            var priest = UnitFactory.CreateCharacter(Db, ClassId.Priest, "P", 30, learnAll: true);
            var why = EquipmentRules.CannotUseReason(mage, OffHandDagger);
            Assert(why != null && why.Contains("cannot dual wield"), "mage: an Off Hand dagger needs dual wield: " + why);
            why = EquipmentRules.CannotUseReason(priest, OffHandMace);
            Assert(why != null && why.Contains("cannot dual wield"), "priest: an Off Hand mace needs dual wield: " + why);
            Assert(EquipmentRules.CannotUseReason(mage, OffHandMace) != null, "mage cannot use maces at all");
            Assert(!LootContext.CanUse(mage, OffHandDagger) && !LootContext.CanUse(priest, OffHandMace), "loot: not usable by casters");
            Assert(EquipmentRules.ChooseSlot(mage, OffHandDagger) == null, "no slot for the mage");
            // their One-Hand daggers and held items still work
            Assert(EquipmentRules.CannotEquipReason(mage, OneHandDagger, EquipSlot.MainHand) == null, "mage: One-Hand dagger in the main hand");
            Assert(EquipmentRules.CannotEquipReason(mage, OneHandDagger, EquipSlot.OffHand) != null, "mage: not in the off hand");
            Assert(EquipmentRules.CannotEquipReason(mage, Orb, EquipSlot.OffHand) == null, "mage: a held item in the off hand");

            // the bags mark it unusable (the inventory panel asks CannotUseReason) and the session refuses it
            var s = SessionTest.NewGame(ClassId.Mage, 1, seed: 1107);
            var it = new ItemInstance(OffHandDagger);
            s.Inventory.Add(it);
            Assert(s.CanEquip(s.Main, it) != null && s.Equip(s.Main, it) != null && s.Main.Equipment.OffHand != it, "session: a mage cannot equip it");
        }

        // ------------------------------------------------------------------------------------------ slots

        [Test]
        public static void OffHandWeapons_NeverGoInTheMainHand()
        {
            Assert(EquipmentRules.IsOffHandWeapon(OffHandDagger) && EquipmentRules.IsOffHandWeapon(OffHandSword), "Off Hand weapons");
            Assert(!EquipmentRules.IsOffHandWeapon(OneHandDagger) && !EquipmentRules.IsOffHandWeapon(Shield) && !EquipmentRules.IsOffHandWeapon(Orb),
                "One-Hand weapons, shields and held items are not Off Hand weapons");
            var slots = EquipmentRules.SlotsFor(OffHandDagger);
            Assert(slots.Length == 1 && slots[0] == EquipSlot.OffHand, "SlotsFor: the off hand only");

            var r = UnitFactory.CreateCharacter(Db, ClassId.Rogue, "R", 1);
            EquipmentRules.Unequip(r, EquipSlot.MainHand);
            EquipmentRules.Unequip(r, EquipSlot.OffHand);
            var why = EquipmentRules.CannotEquipReason(r, OffHandDagger, EquipSlot.MainHand);
            Assert(why != null && why.Contains("off hand"), "not in the main hand: " + why);
            Assert(EquipmentRules.ChooseSlot(r, OffHandDagger) == EquipSlot.OffHand, "ChooseSlot: the off hand even with an empty main hand");
            Assert(EquipmentRules.ChooseSlot(r, OneHandDagger) == EquipSlot.MainHand, "a One-Hand weapon still prefers the empty main hand");

            var s = SessionTest.NewGame(ClassId.Rogue, 1, seed: 1108);
            var it = new ItemInstance(OffHandDagger);
            s.Inventory.Add(it);
            Assert(s.CanEquip(s.Main, it, EquipSlot.MainHand) != null && s.Equip(s.Main, it, EquipSlot.MainHand) != null, "session: refused in the main hand");
            Assert(s.Main.Equipment.MainHand.Def.id == "rogue_starter_dagger", "the main hand is untouched");
            Assert(s.Equip(s.Main, it) == null && s.Main.Equipment.OffHand == it, "session: Equip without a slot puts it in the off hand");
            Assert(s.Inventory.Items.Any(i => i.Def.id == "rogue_starter_dagger_offhand"), "the old off-hand dagger is back in the bags");

            // an Off Hand weapon next to a two-hander takes the two-hander off (like any off-hand item)
            var w = UnitFactory.CreateCharacter(Db, ClassId.Warrior, "W", 20, learnAll: true);
            EquipmentRules.Equip(w, new ItemInstance(TwoHandSword), EquipSlot.MainHand);
            var displaced = EquipmentRules.Equip(w, new ItemInstance(OffHandSword), EquipSlot.OffHand);
            Assert(displaced.Any(d => d != null && d.Def == TwoHandSword) && w.Equipment.MainHand == null, "the two-hander is displaced");
            // and RemoveIllegal takes it off a character that can no longer dual wield
            w.Level = 19;
            var removed = EquipmentRules.RemoveIllegal(w);
            Assert(removed.Any(d => d.Def == OffHandSword) && w.Equipment.OffHand == null, "RemoveIllegal: an Off Hand weapon without dual wield");
        }

        [Test]
        public static void ShieldsAndHeldItems_KeepTheirRules()
        {
            Assert(!EquipmentRules.NeedsDualWield(Shield, EquipSlot.OffHand) && !EquipmentRules.NeedsDualWield(Orb, EquipSlot.OffHand), "no dual wield for shields and held items");
            Assert(EquipmentRules.NeedsDualWield(OneHandDagger, EquipSlot.OffHand) && !EquipmentRules.NeedsDualWield(OneHandDagger, EquipSlot.MainHand), "a One-Hand weapon only in the off hand");
            var w1 = UnitFactory.CreateCharacter(Db, ClassId.Warrior, "W1", 1);
            Assert(!EquipmentRules.CanDualWield(w1) && EquipmentRules.CannotEquipReason(w1, Shield, EquipSlot.OffHand) == null, "L1 warrior: a shield, no dual wield");
            var p1 = UnitFactory.CreateCharacter(Db, ClassId.Priest, "P1", 1);
            Assert(EquipmentRules.CannotEquipReason(p1, Orb, EquipSlot.OffHand) == null, "L1 priest: a held item");
            var r1 = UnitFactory.CreateCharacter(Db, ClassId.Rogue, "R1", 1);
            var why = EquipmentRules.CannotUseReason(r1, Shield);
            Assert(why != null && why.Contains("shields"), "rogues still cannot use shields: " + why);
            Assert(EquipmentRules.CannotEquipReason(r1, Orb, EquipSlot.OffHand) == null, "a rogue may hold an off-hand item");
            r1.Equipment[EquipSlot.OffHand] = new ItemInstance(Orb);
            Assert(!r1.Equipment.IsDualWielding && !r1.Equipment.OffHand.IsWeapon, "a held item is not dual wielding");
        }

        // ------------------------------------------------------------------------------------------ battle

        [Test]
        public static void OffHandWeapon_SwingsInBattleAtTheOffHandFactor()
        {
            var b = RulesTestUtil.NewBattle(1109);
            var r = UnitFactory.CreateCharacter(Db, ClassId.Rogue, "R", 1);
            EquipmentRules.Unequip(r, EquipSlot.MainHand);
            EquipmentRules.Unequip(r, EquipSlot.OffHand);
            // the same damage in both hands (large, so the rounding of hits does not blur the ratio)
            var mainDagger = Weapon("t_oh_battle_main", EquipType.OneHand, WeaponType.Dagger, 200f);
            var offDagger = Weapon("t_oh_battle_off", EquipType.OffHand, WeaponType.Dagger, 200f);
            Assert(EquipmentRules.CannotEquipReason(r, mainDagger, EquipSlot.MainHand) == null && EquipmentRules.CannotEquipReason(r, offDagger, EquipSlot.OffHand) == null,
                "a level-1 rogue wears both test daggers");
            EquipmentRules.Equip(r, new ItemInstance(mainDagger), EquipSlot.MainHand);
            EquipmentRules.Equip(r, new ItemInstance(offDagger), EquipSlot.OffHand);
            r.InvalidateStats();
            r.RestoreFull();
            Assert(r.Equipment.IsDualWielding, "dual wielding");
            var off = StatCalculator.GetWeapon(r, WeaponSlot.OffHand);
            Assert(off.Valid && off.Item.Def == offDagger && off.Speed == 2f, "the off-hand weapon swings on its own timer");
            float factor = StatCalculator.OffHandDamageFactor(r);
            AssertNear(factor, RulesConstants.OffHandDamageFactor, 1e-4f, "level 1, no talents: the plain off-hand factor");
            // off-hand swings sound like the weapon held there
            Assert(CombatSounds.WeaponTypeOf(r, true, false) == WeaponType.Dagger && CombatSounds.WeaponLayerOf(WeaponType.Dagger) == "hit_dagger",
                "an Off Hand dagger sounds like a dagger");

            var foe = RulesTestUtil.Mob("cr_wolf", 1).At(10f, 10f).Tough();
            r.At(11.2f, 10f);
            r.FaceTowards(foe.Position);
            foe.FaceTowards(r.Position);
            b.AddUnits(new[] { r, foe });
            b.Begin();
            // dual-wield white swings miss more (the +19% penalty applies with an Off Hand weapon too)
            Assert(b.SwingHitChance(r, foe, WeaponSlot.MainHand).Miss > 19f, "dual-wield miss penalty: " + b.SwingHitChance(r, foe, WeaponSlot.MainHand));
            b.StartAutoAttack(r, foe, null, false);
            for (int i = 0; i < 30 && !b.IsOver; i++)
            {
                RulesTestUtil.SkipTo(b, r);
                if (b.IsOver || b.ActiveUnit != r) break;
                b.EndTurn(r);
            }
            var hits = b.Events.Where(e => e.Type == CombatEventType.Damage && e.Source == r && e.AutoAttack && !e.Crit && e.Blocked <= 0f).ToList();
            var mainHits = hits.Where(e => !e.OffHand).Select(e => e.Amount + e.Absorbed).ToList();
            var offHits = hits.Where(e => e.OffHand).Select(e => e.Amount + e.Absorbed).ToList();
            Assert(mainHits.Count >= 5 && offHits.Count >= 5, $"both hands hit in battle (main {mainHits.Count}, off {offHits.Count})");
            float ratio = offHits.Average() / mainHits.Average();
            Console.WriteLine($"    dual wield L1 rogue vs wolf: {mainHits.Count} main-hand hits (avg {mainHits.Average():0.0}), {offHits.Count} off-hand hits (avg {offHits.Average():0.0}), ratio {ratio:0.000}");
            AssertNear(ratio, factor, 0.02f, "off-hand white hits deal the off-hand factor of a main-hand hit");
            Assert(b.Events.Any(e => e.Type == CombatEventType.Miss && e.Source == r && e.OffHand), "off-hand swings can miss too");
        }

        // ------------------------------------------------------------------------------------------ generation and data

        [Test]
        public static void GeneratedOffHandWeapons_OneHandDpsAndOffHandBudget()
        {
            foreach (var q in new[] { Quality.Uncommon, Quality.Rare })
                foreach (var wt in new[] { WeaponType.Dagger, WeaponType.OneHandSword, WeaponType.FistWeapon })
                {
                    var oh = ItemGenerator.MakeDef("Test", EquipType.OffHand, ArmorType.None, wt, 20, q);
                    var one = ItemGenerator.MakeDef("Test", EquipType.OneHand, ArmorType.None, wt, 20, q);
                    string tag = $"{q} {wt}";
                    Assert(oh.kind == ItemKind.Weapon && EquipmentRules.IsOffHandWeapon(oh), tag + ": an Off Hand weapon");
                    float dps = (oh.minDamage + oh.maxDamage) * 0.5f / oh.speed;
                    AssertNear(dps, ItemGenerator.WeaponDps(wt, 20, q), 0.6f, tag + ": one-hand DPS");
                    Assert(oh.minDamage == one.minDamage && oh.maxDamage == one.maxDamage && oh.speed == one.speed, tag + ": the same damage as the One-Hand version");
                    float ohBudget = ItemGenerator.StatBudget(20, q, EquipType.OffHand), oneBudget = ItemGenerator.StatBudget(20, q, EquipType.OneHand);
                    AssertNear(ohBudget / oneBudget, 0.56f / 0.45f, 1e-3f, tag + ": OffHand stat budget 0.56 vs One-Hand 0.45");
                }
            // generated shields and held items are unchanged (no weapon damage, not Off Hand weapons)
            var sh = ItemGenerator.MakeDef("", EquipType.OffHand, ArmorType.None, WeaponType.Shield, 15, Quality.Uncommon);
            var orb = ItemGenerator.MakeDef("", EquipType.OffHand, ArmorType.None, WeaponType.HeldInOffhand, 15, Quality.Uncommon);
            Assert(!EquipmentRules.IsOffHandWeapon(sh) && sh.block > 0 && sh.maxDamage == 0f, "generated shield");
            Assert(!EquipmentRules.IsOffHandWeapon(orb) && orb.maxDamage == 0f, "generated held item");
            // an Uncommon random suffix on an Off Hand weapon follows the OffHand budget
            var inst = new ItemInstance(ItemGenerator.MakeDef("", EquipType.OffHand, ArmorType.None, WeaponType.Dagger, 20, Quality.Uncommon));
            ItemGenerator.ApplyRandomSuffix(Db, inst, new Rng(3));
            Assert(inst.SuffixStats.Count > 0, "suffix stats on a generated Off Hand weapon");
        }

        const string BadBundle = @"{
  ""items"": [
    {""id"": ""zz_oh_good_dagger"", ""name"": ""Good Off Hand Dagger"", ""icon"": ""dagger"", ""kind"": ""Weapon"", ""quality"": ""Uncommon"", ""itemLevel"": 14,
     ""requiredLevel"": 9, ""equip"": ""OffHand"", ""weaponType"": ""Dagger"", ""minDamage"": 8, ""maxDamage"": 15, ""speed"": 1.7, ""price"": 300,
     ""stats"": [{""stat"": ""Agility"", ""value"": 3}]},
    {""id"": ""zz_oh_bad_bow"", ""name"": ""Off Hand Bow"", ""icon"": ""bow"", ""kind"": ""Weapon"", ""quality"": ""Common"", ""itemLevel"": 5,
     ""equip"": ""OffHand"", ""weaponType"": ""Bow"", ""minDamage"": 3, ""maxDamage"": 6, ""speed"": 2.8, ""price"": 10},
    {""id"": ""zz_oh_bad_nodamage"", ""name"": ""Blunt Off Hand Sword"", ""icon"": ""sword"", ""kind"": ""Weapon"", ""quality"": ""Common"", ""itemLevel"": 5,
     ""equip"": ""OffHand"", ""weaponType"": ""OneHandSword"", ""price"": 10},
    {""id"": ""zz_oh_bad_kind"", ""name"": ""Armour Fist"", ""icon"": ""fist"", ""kind"": ""Armor"", ""quality"": ""Common"", ""itemLevel"": 5,
     ""equip"": ""OffHand"", ""weaponType"": ""FistWeapon"", ""minDamage"": 3, ""maxDamage"": 6, ""speed"": 2.5, ""price"": 10}
  ]
}";

        [Test]
        public static void Validator_ChecksOffHandWeapons()
        {
            var files = ReadDataFiles(DataDir).ToList();
            files.Add(new KeyValuePair<string, string>("zz_off_hand_weapons.json", BadBundle));
            var db = GameDatabase.Load(files);
            Assert(db.Problems.Count == 0, "the bundle parses: " + string.Join("; ", db.Problems));
            var problems = DataValidator.Validate(db).Where(p => p.Contains("zz_oh_")).ToList();
            void Expect(string id, string text) =>
                Assert(problems.Any(p => p.Contains(id) && p.Contains(text)), $"expected '{id}: …{text}…', got:\n  " + string.Join("\n  ", problems));
            Assert(!problems.Any(p => p.Contains("zz_oh_good_dagger")), "a proper Off Hand dagger validates: " + string.Join("; ", problems));
            Expect("zz_oh_bad_bow", "one-hand melee type");
            Expect("zz_oh_bad_nodamage", "damage");
            Expect("zz_oh_bad_kind", "kind Weapon");
        }
    }
}
