// Generated items: random "of the Bear" suffixes with WoW-like stat budgets, random loot pieces and
// level-appropriate "veteran" gear sets for characters created above level 1.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public static class ItemGenerator
    {
        static int genCounter;

        /// <summary>The counter used for generated item ids ("gen_&lt;slot&gt;_&lt;ilvl&gt;_&lt;n&gt;").</summary>
        public static int GeneratedCounter => genCounter;

        /// <summary>Makes the next generated id use a number above <paramref name="n"/> (call after loading a save).</summary>
        public static void EnsureCounterAbove(int n)
        {
            if (genCounter < n) genCounter = n;
        }

        /// <summary>Raises the counter above every generated id in <paramref name="ids"/> ("gen_*_N"); other ids are ignored.</summary>
        public static void EnsureCounterAbove(IEnumerable<string> ids)
        {
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id) || !id.StartsWith("gen_", StringComparison.Ordinal)) continue;
                int us = id.LastIndexOf('_');
                if (us >= 0 && int.TryParse(id.Substring(us + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
                    EnsureCounterAbove(n);
            }
        }

        public static float QualityMult(Quality q)
        {
            switch (q)
            {
                case Quality.Poor: return 0.3f;
                case Quality.Common: return 0.5f;
                case Quality.Uncommon: return 1f;
                case Quality.Rare: return 1.3f;
                case Quality.Epic: return 1.6f;
                case Quality.Legendary: return 2f;
                default: return 1f;
            }
        }

        static float SlotBudgetMult(EquipType e)
        {
            switch (e)
            {
                case EquipType.Chest: case EquipType.Legs: case EquipType.Head: case EquipType.TwoHand: return 1f;
                case EquipType.Shoulder: case EquipType.Hands: case EquipType.Feet: case EquipType.Waist: return 0.75f;
                case EquipType.Wrist: case EquipType.Neck: case EquipType.Back: case EquipType.Finger: case EquipType.Trinket: case EquipType.OffHand: return 0.56f;
                case EquipType.OneHand: case EquipType.MainHand: return 0.45f;
                case EquipType.Ranged: return 0.35f;
                default: return 0.5f;
            }
        }

        /// <summary>Total stat points for an item: 0.55 × itemLevel × quality × slot (≈ WoW greens: ilvl 40 chest ≈ 22 points).</summary>
        public static float StatBudget(int itemLevel, Quality q, EquipType slot) => Math.Max(1f, 0.55f * itemLevel * QualityMult(q) * SlotBudgetMult(slot));

        static float ArmorTypeFactor(ArmorType t)
        {
            switch (t)
            {
                case ArmorType.Cloth: return 1.4f;
                case ArmorType.Leather: return 2.8f;
                case ArmorType.Mail: return 5.9f;
                case ArmorType.Plate: return 10f;
                default: return 0f;
            }
        }

        static float ArmorSlotFactor(EquipType e)
        {
            switch (e)
            {
                case EquipType.Chest: case EquipType.Legs: return 1f;
                case EquipType.Head: return 0.81f;
                case EquipType.Shoulder: return 0.75f;
                case EquipType.Feet: return 0.69f;
                case EquipType.Hands: return 0.62f;
                case EquipType.Waist: return 0.56f;
                case EquipType.Wrist: return 0.44f;
                case EquipType.Back: return 0.5f;
                default: return 0f;
            }
        }

        /// <summary>Armor value of a piece (chest: cloth 1.4, leather 2.8, mail 5.9, plate 10 × (ilvl + 5)).</summary>
        public static float ArmorValue(ArmorType t, EquipType slot, int itemLevel, Quality q)
        {
            float qm = q >= Quality.Rare ? 1.1f + 0.1f * (q - Quality.Rare) : (q == Quality.Uncommon ? 1f : 0.9f);
            if (slot == EquipType.Back) t = ArmorType.Cloth;
            return (float)Math.Round(ArmorTypeFactor(t) * ArmorSlotFactor(slot) * (itemLevel + 5) * qm);
        }

        /// <summary>Weapon DPS: (0.62 × ilvl + 2) × quality, ×1.3 for two-handers, ×0.95 ranged, ×1.25 wands.</summary>
        public static float WeaponDps(WeaponType w, int itemLevel, Quality q)
        {
            float qm = q >= Quality.Rare ? 1.1f + 0.12f * (q - Quality.Rare) : (q == Quality.Uncommon ? 1f : 0.9f);
            float dps = (0.62f * itemLevel + 2f) * qm;
            if (IsTwoHand(w)) dps *= 1.3f;
            else if (w == WeaponType.Bow || w == WeaponType.Gun || w == WeaponType.Crossbow || w == WeaponType.Thrown) dps *= 0.95f;
            else if (w == WeaponType.Wand) dps *= 1.25f;
            return dps;
        }

        public static bool IsTwoHand(WeaponType w) => w == WeaponType.TwoHandAxe || w == WeaponType.TwoHandMace || w == WeaponType.TwoHandSword || w == WeaponType.Polearm || w == WeaponType.Staff;
        public static bool IsRangedType(WeaponType w) => w == WeaponType.Bow || w == WeaponType.Gun || w == WeaponType.Crossbow || w == WeaponType.Thrown || w == WeaponType.Wand;

        public static float WeaponSpeed(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Dagger: return 1.7f;
                case WeaponType.FistWeapon: return 2.5f;
                case WeaponType.OneHandAxe: case WeaponType.OneHandMace: case WeaponType.OneHandSword: return 2.6f;
                case WeaponType.Staff: return 3.0f;
                case WeaponType.Polearm: case WeaponType.TwoHandAxe: case WeaponType.TwoHandMace: case WeaponType.TwoHandSword: return 3.5f;
                case WeaponType.Bow: case WeaponType.Gun: return 2.8f;
                case WeaponType.Crossbow: return 3.0f;
                case WeaponType.Wand: return 1.8f;
                case WeaponType.Thrown: return 2.0f;
                default: return 2f;
            }
        }

        static string Noun(EquipType e, ArmorType t, WeaponType w)
        {
            switch (e)
            {
                case EquipType.Head: return t == ArmorType.Cloth ? "Hood" : t == ArmorType.Plate ? "Helm" : "Cap";
                case EquipType.Neck: return "Pendant";
                case EquipType.Shoulder: return t == ArmorType.Cloth ? "Mantle" : "Pauldrons";
                case EquipType.Back: return "Cloak";
                case EquipType.Chest: return t == ArmorType.Cloth ? "Robe" : t == ArmorType.Plate ? "Breastplate" : t == ArmorType.Mail ? "Hauberk" : "Tunic";
                case EquipType.Wrist: return "Bracers";
                case EquipType.Hands: return t == ArmorType.Plate ? "Gauntlets" : "Gloves";
                case EquipType.Waist: return t == ArmorType.Cloth ? "Sash" : "Belt";
                case EquipType.Legs: return t == ArmorType.Cloth ? "Leggings" : t == ArmorType.Plate ? "Legplates" : "Legguards";
                case EquipType.Feet: return t == ArmorType.Cloth ? "Slippers" : "Boots";
                case EquipType.Finger: return "Ring";
                case EquipType.Trinket: return "Charm";
            }
            switch (w)
            {
                case WeaponType.Dagger: return "Dirk";
                case WeaponType.FistWeapon: return "Claw";
                case WeaponType.OneHandAxe: return "Hatchet";
                case WeaponType.OneHandMace: return "Mace";
                case WeaponType.OneHandSword: return "Blade";
                case WeaponType.Polearm: return "Halberd";
                case WeaponType.Staff: return "Staff";
                case WeaponType.TwoHandAxe: return "Greataxe";
                case WeaponType.TwoHandMace: return "Maul";
                case WeaponType.TwoHandSword: return "Greatsword";
                case WeaponType.Bow: return "Longbow";
                case WeaponType.Crossbow: return "Crossbow";
                case WeaponType.Gun: return "Rifle";
                case WeaponType.Thrown: return "Throwing Knives";
                case WeaponType.Wand: return "Wand";
                case WeaponType.Shield: return "Shield";
                case WeaponType.HeldInOffhand: return "Orb";
            }
            return "Trinket";
        }

        static string Icon(EquipType e, WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Dagger: return "dagger";
                case WeaponType.OneHandAxe: case WeaponType.TwoHandAxe: return "axe";
                case WeaponType.OneHandMace: case WeaponType.TwoHandMace: return "mace";
                case WeaponType.OneHandSword: case WeaponType.TwoHandSword: return "sword";
                case WeaponType.Polearm: return "spear";
                case WeaponType.Staff: return "staff";
                case WeaponType.Bow: case WeaponType.Crossbow: return "bow";
                case WeaponType.Gun: return "gun";
                case WeaponType.Wand: return "staff";
                case WeaponType.Shield: return "shield";
                case WeaponType.FistWeapon: return "fist";
                case WeaponType.Thrown: return "dagger";
                case WeaponType.HeldInOffhand: return "arcane_orb";
            }
            switch (e)
            {
                case EquipType.Finger: case EquipType.Neck: return "star";
                case EquipType.Trinket: return "sparkle";
                case EquipType.Feet: return "boot";
                default: return "armor";
            }
        }

        /// <summary>Creates a generated (non-database) equipable item definition.</summary>
        public static ItemDef MakeDef(string prefix, EquipType equip, ArmorType armor, WeaponType weapon, int itemLevel, Quality q)
        {
            genCounter++;
            var def = new ItemDef
            {
                id = $"gen_{equip.ToString().ToLowerInvariant()}_{itemLevel}_{genCounter}",
                name = $"{prefix} {Noun(equip, armor, weapon)}".Trim(),
                icon = Icon(equip, weapon), quality = q, itemLevel = itemLevel, requiredLevel = Math.Max(1, itemLevel - 5),
                equip = equip, armorType = armor, weaponType = weapon,
                kind = weapon != WeaponType.None ? ItemKind.Weapon : (equip == EquipType.Neck || equip == EquipType.Finger || equip == EquipType.Trinket ? ItemKind.Accessory : ItemKind.Armor),
                stack = 1,
            };
            if (armor != ArmorType.None || equip == EquipType.Back) def.armor = ArmorValue(armor, equip, itemLevel, q);
            if (weapon == WeaponType.Shield)
            {
                def.armor = (float)Math.Round(30f * itemLevel * (q >= Quality.Rare ? 1.1f : 1f));
                def.block = (float)Math.Round(0.6f * itemLevel + 3f);
                def.kind = ItemKind.Armor;
            }
            else if (weapon != WeaponType.None && weapon != WeaponType.HeldInOffhand)
            {
                float speed = WeaponSpeed(weapon);
                float dps = WeaponDps(weapon, itemLevel, q);
                def.speed = speed;
                def.minDamage = (float)Math.Round(dps * speed * 0.75f);
                def.maxDamage = (float)Math.Round(dps * speed * 1.25f);
                if (weapon == WeaponType.Wand) def.damageSchool = School.Arcane;
            }
            def.price = (int)Math.Round(itemLevel * itemLevel * 4f * QualityMult(q) + 10);
            return def;
        }

        /// <summary>Splits a stat budget over stats by weights (rounded, at least 1 each).</summary>
        public static List<StatModDef> SplitBudget(float budget, StatId[] stats, float[] weights)
        {
            var list = new List<StatModDef>();
            if (stats == null || stats.Length == 0) return list;
            float sum = 0f;
            for (int i = 0; i < stats.Length; i++) sum += weights != null && i < weights.Length && weights[i] > 0 ? weights[i] : 1f;
            for (int i = 0; i < stats.Length; i++)
            {
                float w = weights != null && i < weights.Length && weights[i] > 0 ? weights[i] : 1f;
                float cost = StatCost(stats[i]);
                int v = Math.Max(1, (int)Math.Round(budget * w / sum / cost));
                list.Add(new StatModDef { stat = stats[i], value = v });
            }
            return list;
        }

        /// <summary>Budget cost per point (spell damage/healing/AP are cheaper per point, crit/hit dearer).</summary>
        static float StatCost(StatId s)
        {
            switch (s)
            {
                case StatId.AttackPower: case StatId.RangedAttackPower: return 0.5f;
                case StatId.SpellDamage: return 0.86f;
                case StatId.HealingPower: return 0.45f;
                case StatId.MeleeCrit: case StatId.SpellCrit: case StatId.RangedCrit: case StatId.MeleeHit: case StatId.SpellHit: case StatId.Dodge: case StatId.Parry: return 14f;
                case StatId.Armor: return 0.1f;
                case StatId.ManaRegen: case StatId.HealthRegen: return 2.5f;
                case StatId.Resistance: return 1f;
                default: return 1f;
            }
        }

        /// <summary>Gives an instance a random suffix from the database ("of the Bear") with a stat budget.</summary>
        public static void ApplyRandomSuffix(GameDatabase db, ItemInstance inst, Rng rng, ItemSuffixDef forced = null)
        {
            var suffixes = new List<ItemSuffixDef>(db.ItemSuffixes.Values);
            var suf = forced ?? (suffixes.Count > 0 ? rng.Pick(suffixes) : null);
            if (suf == null) return;
            float budget = StatBudget(inst.Def.itemLevel, inst.Def.quality, inst.Def.equip);
            inst.SuffixId = suf.id;
            inst.SuffixName = suf.name;
            inst.SuffixStats.Clear();
            inst.SuffixStats.AddRange(SplitBudget(budget, suf.stats, suf.weights));
        }

        /// <summary>A random equipable item of the given level/quality: a matching database item when one exists, otherwise generated.
        /// Uncommon+ items roll a random suffix.</summary>
        public static ItemInstance RandomItem(GameDatabase db, Rng rng, int itemLevel, Quality q)
        {
            itemLevel = Math.Max(1, itemLevel);
            var candidates = new List<ItemDef>();
            foreach (var d in db.Items.Values)
            {
                if (d.equip == EquipType.None || d.unique || !string.IsNullOrEmpty(d.quest) || d.quality != q) continue;
                if (Math.Abs(d.itemLevel - itemLevel) > 4) continue;
                if (d.stats.Count > 0 || d.equipEffects.Count > 0) continue; // only plain bases receive suffixes
                candidates.Add(d);
            }
            ItemInstance inst;
            if (candidates.Count > 0)
            {
                inst = new ItemInstance(rng.Pick(candidates));
            }
            else
            {
                var slots = new[] { EquipType.Head, EquipType.Shoulder, EquipType.Chest, EquipType.Wrist, EquipType.Hands, EquipType.Waist, EquipType.Legs, EquipType.Feet, EquipType.Back, EquipType.Finger, EquipType.Neck, EquipType.OneHand, EquipType.TwoHand, EquipType.Ranged, EquipType.OffHand };
                var slot = slots[rng.Range(0, slots.Length - 1)];
                ArmorType armor = ArmorType.None;
                WeaponType weapon = WeaponType.None;
                if (slot == EquipType.OneHand) weapon = new[] { WeaponType.Dagger, WeaponType.OneHandSword, WeaponType.OneHandMace, WeaponType.OneHandAxe }[rng.Range(0, 3)];
                else if (slot == EquipType.TwoHand) weapon = new[] { WeaponType.Staff, WeaponType.TwoHandSword, WeaponType.TwoHandMace, WeaponType.TwoHandAxe, WeaponType.Polearm }[rng.Range(0, 4)];
                else if (slot == EquipType.Ranged) weapon = new[] { WeaponType.Bow, WeaponType.Gun, WeaponType.Wand, WeaponType.Crossbow }[rng.Range(0, 3)];
                else if (slot == EquipType.OffHand) weapon = rng.Chance(50) ? WeaponType.Shield : WeaponType.HeldInOffhand;
                else if (slot != EquipType.Finger && slot != EquipType.Neck && slot != EquipType.Back)
                {
                    int maxType = itemLevel >= 40 ? 4 : 3;
                    armor = (ArmorType)rng.Range(1, maxType);
                }
                inst = new ItemInstance(MakeDef("", slot, armor, weapon, itemLevel, q)) { Generated = true };
                inst.Def.name = inst.Def.name.Trim();
            }
            if (q >= Quality.Uncommon) ApplyRandomSuffix(db, inst, rng);
            return inst;
        }

        // ======================================================== veteran gear

        static StatId[] PreferredStats(Unit u)
        {
            var role = u.Role;
            switch (u.ClassId)
            {
                case ClassId.Warrior: return role == UnitRole.Tank ? new[] { StatId.Stamina, StatId.Strength, StatId.Agility } : new[] { StatId.Strength, StatId.Agility, StatId.Stamina };
                case ClassId.Paladin: return role == UnitRole.Healer ? new[] { StatId.Intellect, StatId.Stamina, StatId.Spirit } : new[] { StatId.Strength, StatId.Stamina, StatId.Intellect };
                case ClassId.Hunter: return new[] { StatId.Agility, StatId.Stamina, StatId.Intellect };
                case ClassId.Rogue: return new[] { StatId.Agility, StatId.Stamina, StatId.Strength };
                case ClassId.Shaman: return role == UnitRole.MeleeDps ? new[] { StatId.Strength, StatId.Agility, StatId.Stamina, StatId.Intellect } : new[] { StatId.Intellect, StatId.Stamina, StatId.Spirit };
                default: return new[] { StatId.Intellect, StatId.Stamina, StatId.Spirit };
            }
        }

        static float[] PreferredWeights(int n) => n == 4 ? new[] { 3f, 2f, 2f, 1f } : new[] { 3f, 2f, 1f };

        static WeaponType FirstAllowed(Unit u, params WeaponType[] options)
        {
            foreach (var w in options) if (EquipmentRules.CanUseWeapon(u, w)) return w;
            return WeaponType.None;
        }

        /// <summary>
        /// A full level-appropriate set for the unit (armour at its best armour type, class weapons by role,
        /// stats by role). Quality: Rare at level ≥ 50, else Uncommon. Items are not equipped.
        /// </summary>
        public static List<ItemInstance> VeteranGear(GameDatabase db, Unit u, Rng rng)
        {
            var list = new List<ItemInstance>();
            if (u.Class == null) return list;
            int ilvl = Math.Max(1, u.Level);
            var q = u.Level >= 50 ? Quality.Rare : Quality.Uncommon;
            if (u.Level < 10) q = Quality.Common;
            var armor = EquipmentRules.MaxArmor(u);
            var stats = PreferredStats(u);
            var weights = PreferredWeights(stats.Length);
            var prefix = u.Level >= 50 ? "Champion's" : u.Level >= 30 ? "Veteran's" : u.Level >= 10 ? "Journeyman's" : "Recruit's";
            void Add(EquipType e, ArmorType at, WeaponType wt, float budgetScale = 1f)
            {
                bool weaponSlot = e == EquipType.OneHand || e == EquipType.TwoHand || e == EquipType.MainHand || e == EquipType.OffHand || e == EquipType.Ranged;
                if (weaponSlot && wt == WeaponType.None) return;
                if (wt != WeaponType.None && wt != WeaponType.Shield && wt != WeaponType.HeldInOffhand && !IsRangedType(wt))
                    e = IsTwoHand(wt) ? EquipType.TwoHand : EquipType.OneHand;
                var def = MakeDef(prefix, e, at, wt, ilvl, q);
                def.requiredLevel = Math.Min(def.requiredLevel, u.Level);
                if (q >= Quality.Uncommon) def.stats.AddRange(SplitBudget(StatBudget(ilvl, q, e) * budgetScale, stats, weights));
                list.Add(new ItemInstance(def) { Generated = true });
            }
            foreach (var e in new[] { EquipType.Head, EquipType.Shoulder, EquipType.Chest, EquipType.Wrist, EquipType.Hands, EquipType.Waist, EquipType.Legs, EquipType.Feet })
                Add(e, armor, WeaponType.None);
            Add(EquipType.Back, ArmorType.Cloth, WeaponType.None);
            if (u.Level >= 10) { Add(EquipType.Neck, ArmorType.None, WeaponType.None); Add(EquipType.Finger, ArmorType.None, WeaponType.None); Add(EquipType.Finger, ArmorType.None, WeaponType.None); }

            var role = u.Role;
            switch (u.ClassId)
            {
                case ClassId.Warrior:
                    if (role == UnitRole.Tank) { Add(EquipType.OneHand, ArmorType.None, FirstAllowed(u, WeaponType.OneHandSword, WeaponType.OneHandAxe, WeaponType.OneHandMace)); Add(EquipType.OffHand, ArmorType.None, WeaponType.Shield); }
                    else Add(EquipType.TwoHand, ArmorType.None, FirstAllowed(u, WeaponType.TwoHandSword, WeaponType.TwoHandAxe, WeaponType.TwoHandMace, WeaponType.Polearm));
                    AddRanged(u, Add, WeaponType.Bow, WeaponType.Gun, WeaponType.Crossbow, WeaponType.Thrown);
                    break;
                case ClassId.Paladin:
                    if (role == UnitRole.MeleeDps) Add(EquipType.TwoHand, ArmorType.None, FirstAllowed(u, WeaponType.TwoHandMace, WeaponType.TwoHandSword, WeaponType.TwoHandAxe, WeaponType.Polearm));
                    else { Add(EquipType.OneHand, ArmorType.None, FirstAllowed(u, WeaponType.OneHandMace, WeaponType.OneHandSword)); Add(EquipType.OffHand, ArmorType.None, WeaponType.Shield); }
                    break;
                case ClassId.Hunter:
                    Add(EquipType.TwoHand, ArmorType.None, FirstAllowed(u, WeaponType.Polearm, WeaponType.TwoHandAxe, WeaponType.Staff, WeaponType.TwoHandSword), 0.6f);
                    AddRanged(u, Add, WeaponType.Bow, WeaponType.Gun, WeaponType.Crossbow);
                    break;
                case ClassId.Rogue:
                    Add(EquipType.OneHand, ArmorType.None, FirstAllowed(u, WeaponType.Dagger, WeaponType.OneHandSword));
                    if (EquipmentRules.CanDualWield(u)) Add(EquipType.OneHand, ArmorType.None, FirstAllowed(u, WeaponType.OneHandSword, WeaponType.Dagger, WeaponType.FistWeapon, WeaponType.OneHandMace));
                    AddRanged(u, Add, WeaponType.Thrown, WeaponType.Bow, WeaponType.Gun, WeaponType.Crossbow);
                    break;
                case ClassId.Shaman:
                    if (role == UnitRole.MeleeDps) Add(EquipType.TwoHand, ArmorType.None, FirstAllowed(u, WeaponType.TwoHandMace, WeaponType.TwoHandAxe, WeaponType.Staff));
                    else { Add(EquipType.OneHand, ArmorType.None, FirstAllowed(u, WeaponType.OneHandMace, WeaponType.OneHandAxe, WeaponType.Dagger)); Add(EquipType.OffHand, ArmorType.None, EquipmentRules.CanUseWeapon(u, WeaponType.Shield) ? WeaponType.Shield : WeaponType.HeldInOffhand); }
                    break;
                default: // Mage, Priest, Warlock
                    Add(EquipType.TwoHand, ArmorType.None, FirstAllowed(u, WeaponType.Staff, WeaponType.OneHandMace, WeaponType.Dagger, WeaponType.OneHandSword));
                    if (EquipmentRules.CanUseWeapon(u, WeaponType.Wand)) Add(EquipType.Ranged, ArmorType.None, WeaponType.Wand);
                    break;
            }
            // drop pieces the unit could not use (e.g. a class without the expected proficiency)
            list.RemoveAll(i => EquipmentRules.CannotUseReason(u, i.Def) != null);
            return list;
        }

        static void AddRanged(Unit u, Action<EquipType, ArmorType, WeaponType, float> add, params WeaponType[] options)
        {
            var w = FirstAllowed(u, options);
            if (w != WeaponType.None) add(EquipType.Ranged, ArmorType.None, w, 1f);
        }

        /// <summary>Equips veteran gear on the unit (replacing anything worse). Returns the displaced items.</summary>
        public static List<ItemInstance> EquipVeteranGear(GameDatabase db, Unit u, Rng rng)
        {
            var displaced = new List<ItemInstance>();
            foreach (var it in VeteranGear(db, u, rng))
            {
                EquipSlot? slot = null;
                foreach (var s in EquipmentRules.SlotsFor(it.Def))
                {
                    if (EquipmentRules.CannotEquipReason(u, it.Def, s) != null) continue;
                    if (u.Equipment[s] == null || u.Equipment[s].Generated == false) { slot = s; if (u.Equipment[s] == null) break; }
                }
                if (slot == null) continue;
                displaced.AddRange(EquipmentRules.Equip(u, it, slot.Value));
            }
            displaced.RemoveAll(i => i == null);
            return displaced;
        }
    }
}
