// "Off Hand" weapons (content): one-hand melee weapons with equip OffHand (off-hand slot only; they need dual wield
// like a One-Hand weapon in the off hand). Every level band has one a rogue can get (vendor or loot), every authored
// one follows the gear budget (one-hand DPS, the OffHand slot's stat budget), every one can be obtained, the weapon
// types give rogues, hunters and warriors a choice, and a level-1 rogue can buy the slice's off-hand dagger from
// Garrow and wield it in the off hand.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsContentOffHandWeapons
    {
        static readonly WeaponType[] OneHandMelee =
            { WeaponType.Dagger, WeaponType.FistWeapon, WeaponType.OneHandSword, WeaponType.OneHandMace, WeaponType.OneHandAxe };

        /// <summary>An "Off Hand" weapon: a one-hand melee weapon type that goes only in the off hand (the rule's own
        /// <see cref="EquipmentRules.IsOffHandWeapon"/>, authored as a weapon).</summary>
        public static bool IsOffHandWeapon(ItemDef d) => d != null && d.kind == ItemKind.Weapon && EquipmentRules.IsOffHandWeapon(d);

        static List<ItemDef> OffHandWeapons() =>
            Db.Items.Values.Where(IsOffHandWeapon).OrderBy(d => d.id, StringComparer.Ordinal).ToList();

        /// <summary>Item ids sold by a vendor, and item ids some loot table can drop (named entries and pools).</summary>
        static (HashSet<string> sold, HashSet<string> dropped) Sources()
        {
            var sold = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in Db.Npcs.Values)
                if (n.vendor != null) foreach (var v in n.vendor) if (!string.IsNullOrEmpty(v.item)) sold.Add(v.item);
            var dropped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in Db.LootTables.Values)
                foreach (var e in t.entries)
                {
                    if (e.random || e.chance <= 0f) continue;
                    if (!string.IsNullOrEmpty(e.item)) dropped.Add(e.item);
                    if (e.pool != null) foreach (var id in e.pool) if (!string.IsNullOrEmpty(id)) dropped.Add(id);
                }
            return (sold, dropped);
        }

        static bool ClassUses(ClassId c, WeaponType w) => Db.Classes[c].weaponTypes.Contains(w);

        /// <summary>Class starting gear (e.g. the rogue's Worn Parrying Dagger): given at character creation, not sold or looted.</summary>
        static bool IsStartingItem(ItemDef d) => Db.Classes.Values.Any(c => c.startingItems.Contains(d.id));

        [Test]
        public static void OffHandWeapons_EveryBandHasOneForARogue_AndChoicesForEveryDualWielder()
        {
            var all = OffHandWeapons();
            var (sold, dropped) = Sources();
            Assert(all.Count >= 20, "the game has 20+ Off Hand weapons: " + all.Count);
            foreach (var d in all) Assert(sold.Contains(d.id) || dropped.Contains(d.id) || IsStartingItem(d), d.id + ": sold by a vendor, dropped by a loot table or class starting gear");

            // [lo, hi): required levels of the slice, the three expansion zones, and the two raids (required 20 / 30)
            var bands = new (string name, int lo, int hi)[]
            {
                ("the slice 1-12", 1, 12), ("Amberfield 12-18", 12, 18), ("Mirefen 18-24", 18, 24), ("Skyreach 24-30", 24, 30),
            };
            foreach (var (name, lo, hi) in bands)
            {
                var band = all.Where(d => d.requiredLevel >= lo && d.requiredLevel < hi).ToList();
                Assert(band.Any(d => ClassUses(ClassId.Rogue, d.weaponType) && (sold.Contains(d.id) || dropped.Contains(d.id))),
                    name + ": an obtainable Off Hand weapon a rogue can use");
            }
            // the raids' epic pools each hold one (rogue-usable)
            foreach (var raid in new[] { "r1_", "r2_" })
            {
                var epics = all.Where(d => d.id.StartsWith(raid, StringComparison.Ordinal) && d.quality == Quality.Epic).ToList();
                Assert(epics.Count >= 1 && epics.All(d => dropped.Contains(d.id)) && epics.Any(d => ClassUses(ClassId.Rogue, d.weaponType)),
                    raid + "*: an Epic Off Hand weapon in a boss pool, usable by a rogue");
                var bossPool = Db.LootTables.Values.Where(t => t.id.StartsWith("lt_" + raid, StringComparison.Ordinal) && !t.id.EndsWith("_trash", StringComparison.Ordinal))
                    .SelectMany(t => t.entries).Where(e => e.pool != null && e.partyUsable && e.perMembers > 0);
                Assert(epics.All(d => bossPool.Any(e => e.pool.Contains(d.id))), raid + "*: the Off Hand epic is in a boss's epic pool");
            }
            // the slice: a cheap one at Garrow's from level 1 (dagger) and by level 3 (sword)
            var garrow = Db.Npcs["smith_garrow"].vendor.Select(v => Db.Item(v.item)).Where(IsOffHandWeapon).ToList();
            Assert(garrow.Any(d => d.weaponType == WeaponType.Dagger && d.requiredLevel == 1 && d.quality <= Quality.Uncommon && d.price <= 100),
                "Garrow sells a cheap level-1 off-hand dagger");
            Assert(garrow.Any(d => d.weaponType == WeaponType.OneHandSword && d.requiredLevel <= 3 && d.quality <= Quality.Uncommon && d.price <= 150),
                "Garrow sells a cheap off-hand sword by level 3");
            // Brightwater's weaponsmith stocks some for the expansion bands
            var dunstan = Db.Npcs["bw_weaponsmith_dunstan"].vendor.Select(v => Db.Item(v.item)).Where(IsOffHandWeapon).ToList();
            Assert(dunstan.Count >= 2 && dunstan.Select(d => d.weaponType).Distinct().Count() >= 2, "Dunstan sells 2+ kinds of Off Hand weapon");
            // a Rare in hidden-dungeon boss pools
            int dungeonRares = all.Count(d => d.quality == Quality.Rare && d.id.StartsWith("dg", StringComparison.Ordinal) && dropped.Contains(d.id));
            Assert(dungeonRares >= 3, "hidden-dungeon bosses drop Rare Off Hand weapons: " + dungeonRares);

            // weapon types: every dual wielder has a real choice (rogue: dagger, fist, mace, sword; hunter: dagger, fist,
            // axe, sword; warrior: all five)
            foreach (var c in new[] { ClassId.Rogue, ClassId.Hunter, ClassId.Warrior })
            {
                var types = all.Select(d => d.weaponType).Where(w => ClassUses(c, w)).Distinct().ToList();
                int want = OneHandMelee.Count(w => ClassUses(c, w));
                Assert(types.Count == want, $"{c}: Off Hand weapons of every one-hand type it uses ({types.Count} of {want}: {string.Join(", ", types)})");
                // from the level they learn to dual wield, every band has one they can use
                int from = Math.Max(1, Db.Classes[c].dualWieldLevel);
                foreach (var (name, lo, hi) in bands.Where(b => b.hi > from))
                    Assert(all.Any(d => d.requiredLevel >= lo && d.requiredLevel < hi && ClassUses(c, d.weaponType)), $"{c}: {name} has an Off Hand weapon it can use");
            }
        }

        static float Cost(StatId stat, float value)
        {
            switch (stat)
            {
                case StatId.AttackPower: case StatId.RangedAttackPower: return 0.5f * value;
                case StatId.SpellDamage: return 0.86f * value;
                case StatId.HealingPower: return 0.45f * value;
                case StatId.MeleeCrit: case StatId.SpellCrit: case StatId.RangedCrit: case StatId.MeleeHit: case StatId.SpellHit: case StatId.Dodge: case StatId.Parry: return 14f * value;
                default: return value;
            }
        }

        [Test]
        public static void OffHandWeapons_FollowTheGearBudget()
        {
            var qa = new Dictionary<Quality, float> { [Quality.Uncommon] = 1.1f, [Quality.Rare] = 1.6f, [Quality.Epic] = 2.1f };
            var dpsRatio = new Dictionary<Quality, float> { [Quality.Uncommon] = 0.72f, [Quality.Rare] = 0.79f, [Quality.Epic] = 0.86f };
            foreach (var d in OffHandWeapons())
            {
                Assert(!string.IsNullOrEmpty(d.name) && !string.IsNullOrEmpty(d.description) && !string.IsNullOrEmpty(d.icon), d.id + ": name, flavour and icon");
                bool starter = IsStartingItem(d);
                Assert(d.price > 0 && d.requiredLevel >= (starter ? 0 : 1) && d.requiredLevel <= d.itemLevel, d.id + ": price and levels");
                Assert(string.IsNullOrEmpty(d.use) && d.equipEffects.All(e => e.type != "GrantAbility"), d.id + ": no use on equipables");
                Assert(d.minDamage > 0 && d.maxDamage > d.minDamage && d.speed >= 1.2f && d.speed <= 2.8f, d.id + ": weapon damage and a one-hander's speed");
                Assert(d.armorType == ArmorType.None && d.armor == 0 && d.block == 0, d.id + ": a weapon, not a shield");
                Assert(EquipmentRules.SlotsFor(d).SequenceEqual(new[] { EquipSlot.OffHand }), d.id + ": goes only in the off hand");
                if (starter) continue; // class starting gear follows the starter rules (TestsOffHandWeapons), not the loot budget

                // DPS: the one-hand WeaponDps rule for its item level and quality
                float dps = (d.minDamage + d.maxDamage) * 0.5f / d.speed;
                float ratio = dps / ItemGenerator.WeaponDps(d.weaponType, d.itemLevel, d.quality);
                if (d.quality == Quality.Common)
                {
                    Assert(ratio >= 0.5f && ratio <= 0.72f, $"{d.id}: Common DPS {dps:0.00} = {ratio:0.00} x WeaponDps (0.50-0.72)");
                    continue;
                }
                Assert(dpsRatio.ContainsKey(d.quality), d.id + ": Common to Epic");
                Assert(Math.Abs(ratio - dpsRatio[d.quality]) < 0.03f, $"{d.id}: DPS {dps:0.00} = {ratio:0.00} x WeaponDps (want {dpsRatio[d.quality]})");

                // stats: 0.55 x ilvl x Qa x OffHand slot (0.56), a little richer than a One-Hand weapon (0.45)
                float target = 0.55f * d.itemLevel * qa[d.quality] * 0.56f;
                float cost = d.stats.Sum(m => Cost(m.stat, m.value)) + d.equipEffects.Where(e => e.type == "Stat").Sum(e => Cost(e.stat, e.value));
                Assert(d.stats.Count > 0, d.id + ": every green has stats");
                Assert(Math.Abs(cost - target) <= Math.Max(0.15f * target, 0.6f), $"{d.id}: stats cost {cost:0.0} vs the OffHand budget {target:0.0}");
                float oneHand = 0.55f * d.itemLevel * qa[d.quality] * 0.45f;
                Assert(cost > oneHand * 0.95f, $"{d.id}: richer than a One-Hand weapon's {oneHand:0.0}");
                // the item engine's guardrail ([0.8, 3.2] x StatBudget) holds too
                float guard = cost / ItemGenerator.StatBudget(d.itemLevel, d.quality, d.equip);
                Assert(guard >= 0.8f && guard <= 3.2f, $"{d.id}: guardrail ratio {guard:0.00}");
            }
        }

        [Test]
        public static void OffHandWeapons_ALevelOneRogueBuysTheSliceDaggerAndWieldsItInTheOffHand()
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 1, seed: 913);
            var rogue = s.Main;
            Assert(rogue.Level == 1 && rogue.Equipment.MainHand != null, "a level-1 rogue with a main-hand weapon");
            // rogues start dual wielding with the Worn Parrying Dagger (or, in an older setup, an empty off hand)
            var starterOffHand = rogue.Equipment.OffHand;
            Assert(starterOffHand == null || IsOffHandWeapon(starterOffHand.Def), "and an off-hand starter dagger or an empty off hand");

            s.OpenVendor("smith_garrow");
            Assert(s.ActiveVendor != null, "Garrow's shop opens");
            var offer = s.ActiveVendor.Offers().FirstOrDefault(o => IsOffHandWeapon(o.Item) && o.Item.weaponType == WeaponType.Dagger && o.Item.requiredLevel == 1);
            Assert(offer != null, "Garrow offers a level-1 off-hand dagger");
            Assert(offer.Price <= s.Gold, $"a new rogue can afford it ({offer.Price} of {s.Gold} copper)");
            int gold = s.Gold;
            Assert(s.Buy(offer.Item.id) == null, "bought");
            Assert(s.Gold == gold - offer.Price, "paid");
            s.CloseVendor();

            var knife = s.Inventory.Items.Last(i => i.Def.id == offer.Item.id);
            Assert(EquipmentRules.CannotUseReason(rogue, knife.Def) == null, "the bags show it as usable");
            Assert(s.CanEquip(rogue, knife, EquipSlot.MainHand) != null, "an Off Hand weapon does not go in the main hand");
            Assert(EquipmentRules.ChooseSlot(rogue, knife.Def) == EquipSlot.OffHand, "it goes to the off hand");
            var mainHand = rogue.Equipment.MainHand;
            Assert(s.Equip(rogue, knife) == null, "the rogue equips it: " + s.CanEquip(rogue, knife));
            Assert(rogue.Equipment.OffHand?.Def.id == offer.Item.id && rogue.Equipment.MainHand == mainHand, "in the off hand, the main hand kept");
            Assert(rogue.Equipment.IsDualWielding, "the rogue is dual wielding");
            Assert(!s.Inventory.Items.Contains(knife), "out of the bags");
            if (starterOffHand != null) Assert(s.Inventory.Items.Contains(starterOffHand), "the starter off-hand dagger goes back to the bags");
        }
    }
}
