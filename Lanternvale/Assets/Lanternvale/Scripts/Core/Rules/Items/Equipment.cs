// Paper doll (17 slots) and WoW Classic equip rules (armour/weapon proficiencies, dual wield, two-handers, shields).
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    public sealed class Equipment
    {
        public static readonly EquipSlot[] AllSlots = (EquipSlot[])Enum.GetValues(typeof(EquipSlot));
        readonly ItemInstance[] slots = new ItemInstance[AllSlots.Length];

        public ItemInstance this[EquipSlot s]
        {
            get => slots[(int)s];
            internal set => slots[(int)s] = value;
        }

        public ItemInstance MainHand => this[EquipSlot.MainHand];
        public ItemInstance OffHand => this[EquipSlot.OffHand];
        public ItemInstance Ranged => this[EquipSlot.Ranged];

        public bool HasShield => OffHand != null && OffHand.Def.weaponType == WeaponType.Shield;
        public bool HasTwoHander => MainHand != null && MainHand.Def.equip == EquipType.TwoHand;
        public bool IsDualWielding => MainHand != null && OffHand != null && OffHand.IsWeapon;
        public bool HasRangedWeapon => Ranged != null && Ranged.IsWeapon;
        public bool HasWand => Ranged != null && Ranged.Def.weaponType == WeaponType.Wand;
        public bool HasMeleeWeapon => MainHand != null && MainHand.IsWeapon;

        public IEnumerable<KeyValuePair<EquipSlot, ItemInstance>> Equipped
        {
            get
            {
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null) yield return new KeyValuePair<EquipSlot, ItemInstance>((EquipSlot)i, slots[i]);
            }
        }

        public int Count { get { int n = 0; foreach (var s in slots) if (s != null) n++; return n; } }

        public bool Contains(string itemId)
        {
            foreach (var s in slots) if (s != null && s.Def.id == itemId) return true;
            return false;
        }

        public void Clear() { Array.Clear(slots, 0, slots.Length); }
    }

    public static class EquipmentRules
    {
        /// <summary>Paper-doll slots an item may go into.</summary>
        public static EquipSlot[] SlotsFor(ItemDef def)
        {
            switch (def.equip)
            {
                case EquipType.Head: return new[] { EquipSlot.Head };
                case EquipType.Neck: return new[] { EquipSlot.Neck };
                case EquipType.Shoulder: return new[] { EquipSlot.Shoulder };
                case EquipType.Back: return new[] { EquipSlot.Back };
                case EquipType.Chest: return new[] { EquipSlot.Chest };
                case EquipType.Wrist: return new[] { EquipSlot.Wrist };
                case EquipType.Hands: return new[] { EquipSlot.Hands };
                case EquipType.Waist: return new[] { EquipSlot.Waist };
                case EquipType.Legs: return new[] { EquipSlot.Legs };
                case EquipType.Feet: return new[] { EquipSlot.Feet };
                case EquipType.Finger: return new[] { EquipSlot.Finger1, EquipSlot.Finger2 };
                case EquipType.Trinket: return new[] { EquipSlot.Trinket1, EquipSlot.Trinket2 };
                case EquipType.OneHand: return new[] { EquipSlot.MainHand, EquipSlot.OffHand };
                case EquipType.MainHand: return new[] { EquipSlot.MainHand };
                case EquipType.TwoHand: return new[] { EquipSlot.MainHand };
                case EquipType.OffHand: return new[] { EquipSlot.OffHand };
                case EquipType.Ranged: return new[] { EquipSlot.Ranged };
                default: return new EquipSlot[0];
            }
        }

        /// <summary>Highest armour type the unit may wear (armorTypes + armorUpgrade at its level).</summary>
        public static ArmorType MaxArmor(Unit u)
        {
            var c = u.Class;
            if (c == null) return ArmorType.Plate;
            var max = ArmorType.Cloth;
            foreach (var t in c.armorTypes) if (t > max) max = t;
            if (c.armorUpgrade != null && c.armorUpgrade.type != ArmorType.None && u.Level >= c.armorUpgrade.level && c.armorUpgrade.type > max)
                max = c.armorUpgrade.type;
            return max;
        }

        public static bool CanWearArmor(Unit u, ArmorType t) => t == ArmorType.None || t <= MaxArmor(u);

        public static bool CanUseWeapon(Unit u, WeaponType t)
        {
            if (t == WeaponType.None || t == WeaponType.HeldInOffhand) return true;
            var c = u.Class;
            if (c == null) return true;
            foreach (var w in c.weaponTypes) if (w == t) return true;
            if (t == WeaponType.Shield && c.canBlock) return true;
            return Specials.GrantsWeapon(u, t); // talents granting proficiency (Two-Handed Axes and Maces)
        }

        public static bool CanDualWield(Unit u) => u.Class != null && u.Class.dualWieldLevel > 0 && u.Level >= u.Class.dualWieldLevel;

        /// <summary>Why the unit cannot use the item at all (ignoring slot), or null.</summary>
        public static string CannotUseReason(Unit u, ItemDef def)
        {
            if (def.equip == EquipType.None) return "That item cannot be equipped.";
            if (!u.IsCharacter) return "Only characters can equip items.";
            if (def.requiredLevel > u.Level) return $"Requires level {def.requiredLevel}.";
            if (def.classes != null && def.classes.Length > 0 && Array.IndexOf(def.classes, u.ClassId) < 0)
                return $"Requires class: {string.Join(", ", def.classes)}.";
            if (def.equip != EquipType.Back && !CanWearArmor(u, def.armorType))
            {
                if (u.Class != null && u.Class.armorUpgrade != null && u.Class.armorUpgrade.type == def.armorType)
                    return $"{def.armorType} armour requires level {u.Class.armorUpgrade.level}.";
                return $"{u.Class?.name ?? "This class"} cannot wear {def.armorType} armour.";
            }
            if (def.weaponType != WeaponType.None && !CanUseWeapon(u, def.weaponType))
                return $"{u.Class?.name ?? "This class"} cannot use {Pretty(def.weaponType)}.";
            return null;
        }

        static string Pretty(WeaponType t)
        {
            switch (t)
            {
                case WeaponType.OneHandAxe: return "one-handed axes";
                case WeaponType.OneHandMace: return "one-handed maces";
                case WeaponType.OneHandSword: return "one-handed swords";
                case WeaponType.TwoHandAxe: return "two-handed axes";
                case WeaponType.TwoHandMace: return "two-handed maces";
                case WeaponType.TwoHandSword: return "two-handed swords";
                case WeaponType.FistWeapon: return "fist weapons";
                case WeaponType.Shield: return "shields";
                case WeaponType.Staff: return "staves";
                default: return t.ToString().ToLowerInvariant() + "s";
            }
        }

        /// <summary>Why the item cannot go into this slot, or null when it can.</summary>
        public static string CannotEquipReason(Unit u, ItemDef def, EquipSlot slot)
        {
            var why = CannotUseReason(u, def);
            if (why != null) return why;
            if (Array.IndexOf(SlotsFor(def), slot) < 0) return $"{def.name} does not go in the {slot} slot.";
            if (slot == EquipSlot.OffHand && def.equip == EquipType.OneHand && !CanDualWield(u))
            {
                if (u.Class != null && u.Class.dualWieldLevel > 0) return $"Dual Wield requires level {u.Class.dualWieldLevel}.";
                return $"{u.Class?.name ?? "This class"} cannot dual wield.";
            }
            if (def.unique)
            {
                foreach (var kv in u.Equipment.Equipped)
                    if (kv.Key != slot && kv.Value.Def.id == def.id) return "Unique: you already have one equipped.";
            }
            return null;
        }

        /// <summary>Best slot for an item: an empty compatible slot, else the first compatible one. Null if none legal.</summary>
        public static EquipSlot? ChooseSlot(Unit u, ItemDef def)
        {
            EquipSlot? first = null;
            foreach (var s in SlotsFor(def))
            {
                if (CannotEquipReason(u, def, s) != null) continue;
                if (first == null) first = s;
                if (u.Equipment[s] == null)
                {
                    // a one-hander goes to the main hand first if it is empty or holds a two-hander
                    return s;
                }
            }
            return first;
        }

        /// <summary>
        /// Puts the item into the slot and returns every item displaced (to be returned to the inventory).
        /// Caller must have checked <see cref="CannotEquipReason"/>.
        /// </summary>
        public static List<ItemInstance> Equip(Unit u, ItemInstance item, EquipSlot slot)
        {
            var displaced = new List<ItemInstance>();
            var eq = u.Equipment;
            if (eq[slot] != null) displaced.Add(eq[slot]);
            if (item.Def.equip == EquipType.TwoHand && eq.OffHand != null)
            {
                displaced.Add(eq.OffHand);
                eq[EquipSlot.OffHand] = null;
            }
            if (slot == EquipSlot.OffHand && eq.HasTwoHander)
            {
                displaced.Add(eq.MainHand);
                eq[EquipSlot.MainHand] = null;
            }
            eq[slot] = item;
            u.InvalidateStats();
            u.ClampResources();
            return displaced;
        }

        public static ItemInstance Unequip(Unit u, EquipSlot slot)
        {
            var it = u.Equipment[slot];
            if (it == null) return null;
            u.Equipment[slot] = null;
            u.InvalidateStats();
            u.ClampResources();
            return it;
        }

        /// <summary>Removes items the unit can no longer use (e.g. after a class/level change). Returns them.</summary>
        public static List<ItemInstance> RemoveIllegal(Unit u)
        {
            var list = new List<ItemInstance>();
            foreach (var s in Equipment.AllSlots)
            {
                var it = u.Equipment[s];
                if (it != null && CannotEquipReason(u, it.Def, s) != null) list.Add(Unequip(u, s));
            }
            return list;
        }
    }
}
