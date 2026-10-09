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
            internal set
            {
                slots[(int)s] = value;
                Version++;
            }
        }

        /// <summary>
        /// Bumped by every change of a slot (equip, unequip, displacement, load) and by <see cref="Clear"/>. UI caches
        /// (item tooltips, set-bonus toasts) and <see cref="ItemSets.Active"/> compare it to see that the paper doll changed.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>Active set bonuses cached by <see cref="ItemSets.Active"/> for one <see cref="Version"/>.</summary>
        internal object SetCache;
        internal int SetCacheVersion = -1, SetCacheIndex = -1;

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

        public void Clear()
        {
            Array.Clear(slots, 0, slots.Length);
            Version++;
        }
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

        /// <summary>
        /// Highest armour type the unit may wear: armorTypes, plus armorUpgrade from its level once the class's armour
        /// passive (Plate Mail, Mail) is trained (see <see cref="ProficiencyPassive"/>).
        /// </summary>
        public static ArmorType MaxArmor(Unit u)
        {
            var c = u.Class;
            if (c == null) return ArmorType.Plate;
            var max = ArmorType.Cloth;
            foreach (var t in c.armorTypes) if (t > max) max = t;
            if (c.armorUpgrade != null && c.armorUpgrade.type != ArmorType.None && u.Level >= c.armorUpgrade.level && c.armorUpgrade.type > max
                && HasProficiency(u, Proficiency.ArmorUpgrade))
                max = c.armorUpgrade.type;
            return max;
        }

        public static bool CanWearArmor(Unit u, ArmorType t) => t == ArmorType.None || t <= MaxArmor(u);

        /// <summary>Class proficiencies unlocked by a trained passive.</summary>
        public enum Proficiency { ArmorUpgrade, DualWield, Parry }

        static readonly Dictionary<ClassDef, string>[] ProficiencyIds =
        {
            new Dictionary<ClassDef, string>(), new Dictionary<ClassDef, string>(), new Dictionary<ClassDef, string>(),
        };

        /// <summary>
        /// The class passive a trainer sells for a proficiency, by data convention: &lt;class&gt;_plate_mail / &lt;class&gt;_mail
        /// for the armorUpgrade type, &lt;class&gt;_dual_wield, &lt;class&gt;_parry (Shaman: granted by the Enhancement talent).
        /// Null when the class data has no such passive, or when the passive is one of the class's startingAbilities (Rogue
        /// Dual Wield, known from level 1): the level/class rule alone applies then.
        /// </summary>
        public static AbilityDef ProficiencyPassive(Unit u, Proficiency p)
        {
            var c = u?.Class;
            if (c == null || u.Db == null) return null;
            var ids = ProficiencyIds[(int)p];
            string id;
            lock (ids)
            {
                if (!ids.TryGetValue(c, out id))
                {
                    string prefix = c.id.ToString().ToLowerInvariant();
                    switch (p)
                    {
                        case Proficiency.DualWield: id = prefix + "_dual_wield"; break;
                        case Proficiency.Parry: id = prefix + "_parry"; break;
                        default:
                            var type = c.armorUpgrade != null ? c.armorUpgrade.type : ArmorType.None;
                            id = type == ArmorType.Plate ? prefix + "_plate_mail" : type == ArmorType.Mail ? prefix + "_mail" : "";
                            break;
                    }
                    // a starting passive (Rogue Dual Wield) is known from level 1 and never sold: it gates nothing, so
                    // characters that do not list it (saves from before it became one) are not locked out
                    if (c.startingAbilities != null && Array.IndexOf(c.startingAbilities, id) >= 0) id = "";
                    ids[c] = id;
                }
            }
            if (string.IsNullOrEmpty(id)) return null;
            var a = u.Db.Ability(id);
            return a != null && a.passive && a.classId == c.id ? a : null;
        }

        /// <summary>The unit knows the proficiency's passive (true when its class data has none).</summary>
        public static bool HasProficiency(Unit u, Proficiency p)
        {
            var a = ProficiencyPassive(u, p);
            return a == null || u.Knows(a.id);
        }

        /// <summary>
        /// The character can parry (with a melee weapon): its class parry passive is trained (Warrior/Rogue/Paladin/Hunter
        /// Parry from the trainer, Shaman Parry from the Enhancement talent); classes without one parry when ClassDef.canParry.
        /// </summary>
        public static bool HasParry(Unit u)
        {
            if (u?.Class == null) return false;
            var a = ProficiencyPassive(u, Proficiency.Parry);
            return a != null ? u.Knows(a.id) : u.Class.canParry;
        }

        public static bool CanUseWeapon(Unit u, WeaponType t)
        {
            if (t == WeaponType.None || t == WeaponType.HeldInOffhand) return true;
            var c = u.Class;
            if (c == null) return true;
            foreach (var w in c.weaponTypes) if (w == t) return true;
            if (t == WeaponType.Shield && c.canBlock) return true;
            return Specials.GrantsWeapon(u, t); // talents granting proficiency (Two-Handed Axes and Maces)
        }

        /// <summary>
        /// Weapons in the off hand (One-Hand and Off Hand weapons): the class dual wields from dualWieldLevel once its Dual
        /// Wield passive is known. Rogue: level 1, Dual Wield is a starting passive (never gated, see <see cref="ProficiencyPassive"/>);
        /// Warrior and Hunter: level 20 and the trainer.
        /// </summary>
        public static bool CanDualWield(Unit u) =>
            u.Class != null && u.Class.dualWieldLevel > 0 && u.Level >= u.Class.dualWieldLevel && HasProficiency(u, Proficiency.DualWield);

        /// <summary>Why the unit cannot dual wield (level, the trainer's Dual Wield, or its class), or null when it can.</summary>
        public static string CannotDualWieldReason(Unit u)
        {
            if (CanDualWield(u)) return null;
            if (u.Class != null && u.Class.dualWieldLevel > 0)
            {
                if (u.Level < u.Class.dualWieldLevel) return $"Dual Wield requires level {u.Class.dualWieldLevel}.";
                var pa = ProficiencyPassive(u, Proficiency.DualWield);
                if (pa != null && !u.Knows(pa.id)) return $"Requires {pa.name} (class trainer).";
            }
            return $"{u.Class?.name ?? "This class"} cannot dual wield.";
        }

        /// <summary>The one-hand melee weapon types: those of One-Hand weapons and of Off Hand weapons.</summary>
        public static bool IsOneHandMeleeType(WeaponType t) =>
            t == WeaponType.Dagger || t == WeaponType.OneHandSword || t == WeaponType.FistWeapon || t == WeaponType.OneHandMace || t == WeaponType.OneHandAxe;

        /// <summary>
        /// An Off Hand weapon (WoW "Off Hand" daggers, swords, fist weapons...): equip OffHand with a one-hand melee weapon
        /// type. It goes only in the off hand and needs dual wield there, exactly like a One-Hand weapon in the off hand.
        /// Shields and held-in-off-hand items (also equip OffHand) are not: they need no dual wield.
        /// </summary>
        public static bool IsOffHandWeapon(ItemDef d) => d != null && d.equip == EquipType.OffHand && IsOneHandMeleeType(d.weaponType);

        /// <summary>Putting the item into the slot is dual wielding: a One-Hand or an Off Hand weapon in the off hand.</summary>
        public static bool NeedsDualWield(ItemDef d, EquipSlot slot) =>
            d != null && slot == EquipSlot.OffHand && (d.equip == EquipType.OneHand || IsOffHandWeapon(d));

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
                {
                    if (u.Level < u.Class.armorUpgrade.level) return $"{def.armorType} armour requires level {u.Class.armorUpgrade.level}.";
                    var pa = ProficiencyPassive(u, Proficiency.ArmorUpgrade);
                    if (pa != null && !u.Knows(pa.id)) return $"{def.armorType} armour requires {pa.name} (class trainer).";
                }
                return $"{u.Class?.name ?? "This class"} cannot wear {def.armorType} armour.";
            }
            if (def.weaponType != WeaponType.None && !CanUseWeapon(u, def.weaponType))
                return $"{u.Class?.name ?? "This class"} cannot use {Pretty(def.weaponType)}.";
            if (IsOffHandWeapon(def)) return CannotDualWieldReason(u);   // its only slot is the off hand: it needs dual wield
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
            if (Array.IndexOf(SlotsFor(def), slot) < 0)
                return IsOffHandWeapon(def) ? $"{def.name} is an off-hand weapon: it goes only in the off hand." : $"{def.name} does not go in the {slot} slot.";
            if (NeedsDualWield(def, slot))
            {
                var dw = CannotDualWieldReason(u);
                if (dw != null) return dw;
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
        /// The item <see cref="Equip"/> also takes out of the OTHER hand when <paramref name="def"/> goes into
        /// <paramref name="slot"/>: the off-hand item for a two-hander, the main-hand two-hander for anything put in the off
        /// hand (an Off Hand weapon, a shield, a held item, a One-Hand weapon); else null. For comparisons and warnings.
        /// </summary>
        public static ItemInstance OtherHandDisplaced(Unit u, ItemDef def, EquipSlot slot)
        {
            if (u == null || def == null) return null;
            var eq = u.Equipment;
            if (def.equip == EquipType.TwoHand) return slot == EquipSlot.OffHand ? null : eq.OffHand;
            if (slot == EquipSlot.OffHand && eq.HasTwoHander) return eq.MainHand;
            return null;
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
            var other = OtherHandDisplaced(u, item.Def, slot);
            if (other != null)
            {
                displaced.Add(other);
                eq[item.Def.equip == EquipType.TwoHand ? EquipSlot.OffHand : EquipSlot.MainHand] = null;
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
