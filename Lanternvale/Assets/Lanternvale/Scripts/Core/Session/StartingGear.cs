// Level-appropriate ("veteran start") gear: per slot, the best class-usable database item for the unit's level,
// or the generated veteran piece (ItemGenerator.VeteranGear) when the database has nothing as good.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;

namespace Lanternvale.Session
{
    public static class StartingGear
    {
        /// <summary>Extra gold for a veteran start: 25 × level² copper (level 60: 9g).</summary>
        public static int VeteranGold(int level) => level <= 1 ? 0 : 25 * level * level;

        /// <summary>Item "power" for gear comparisons: item level × quality multiplier.</summary>
        public static float Score(ItemDef d) => d == null ? 0f : Math.Max(1, d.itemLevel) * ItemGenerator.QualityMult(d.quality);

        /// <summary>
        /// Equips the best level-appropriate piece per slot (replacing only worse items) and returns every displaced
        /// item (put them in the bags). Only common/uncommon database items that are not quest rewards, companion
        /// signature items, guaranteed (named) loot drops, unique or quest items are considered.
        /// </summary>
        public static List<ItemInstance> EquipLevelGear(GameDatabase db, Unit u, Rng rng)
        {
            var displaced = new List<ItemInstance>();
            if (u == null || u.Class == null) return displaced;
            var reserved = ReservedItems(db);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var chosen = new List<ItemInstance>();
            foreach (var gen in ItemGenerator.VeteranGear(db, u, rng))
            {
                var best = BestDatabaseItem(db, u, gen.Def, reserved, used);
                if (best != null && Score(best) >= Score(gen.Def))
                {
                    used.Add(best.id);
                    chosen.Add(new ItemInstance(best));
                }
                else chosen.Add(gen);
            }
            // armour, accessories and ranged: slot by slot, replacing only worse items
            var placed = new HashSet<EquipSlot>();
            var hands = new List<ItemInstance>();
            foreach (var it in chosen)
            {
                if (IsHandItem(it.Def)) { hands.Add(it); continue; }
                EquipSlot? slot = null;
                float worst = float.MaxValue;
                foreach (var s in EquipmentRules.SlotsFor(it.Def))
                {
                    if (placed.Contains(s)) continue;
                    if (EquipmentRules.CannotEquipReason(u, it.Def, s) != null) continue;
                    var cur = u.Equipment[s];
                    float sc = cur == null ? -1f : Score(cur.Def);
                    if (sc < worst) { worst = sc; slot = s; }
                }
                if (slot == null) continue;
                placed.Add(slot.Value);
                if (worst >= Score(it.Def)) continue;   // keep the better item already worn
                displaced.AddRange(EquipmentRules.Equip(u, it, slot.Value));
            }

            // main hand + off hand as one set (two-hander vs one-hander + shield/off-hand): replace when better overall
            if (hands.Count > 0)
            {
                float cur = Score(u.Equipment.MainHand?.Def) + Score(u.Equipment.OffHand?.Def);
                float next = 0f;
                foreach (var it in hands) next += Score(it.Def);
                if (next > cur)
                {
                    var mh = EquipmentRules.Unequip(u, EquipSlot.MainHand);
                    var oh = EquipmentRules.Unequip(u, EquipSlot.OffHand);
                    if (mh != null) displaced.Add(mh);
                    if (oh != null) displaced.Add(oh);
                    foreach (var it in hands)
                    {
                        EquipSlot slot;
                        if (it.Def.equip == EquipType.OffHand) slot = EquipSlot.OffHand;
                        else if (it.Def.equip == EquipType.OneHand && u.Equipment.MainHand != null) slot = EquipSlot.OffHand;
                        else slot = EquipSlot.MainHand;
                        if (u.Equipment[slot] != null || EquipmentRules.CannotEquipReason(u, it.Def, slot) != null) continue;
                        displaced.AddRange(EquipmentRules.Equip(u, it, slot));
                    }
                }
            }
            displaced.RemoveAll(i => i == null);
            u.InvalidateStats();
            return displaced;
        }

        static bool IsHandItem(ItemDef d) =>
            d.equip == EquipType.OneHand || d.equip == EquipType.MainHand || d.equip == EquipType.TwoHand || d.equip == EquipType.OffHand;

        /// <summary>Items that must not be handed out as starting gear: quest rewards, companion signature items, named drops.</summary>
        static HashSet<string> ReservedItems(GameDatabase db)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var q in db.Quests.Values)
            {
                if (q.rewards == null) continue;
                if (q.rewards.items != null) foreach (var i in q.rewards.items) set.Add(i);
                if (q.rewards.choiceItems != null) foreach (var i in q.rewards.choiceItems) set.Add(i);
            }
            foreach (var c in db.Companions.Values)
                if (c.startingItems != null) foreach (var i in c.startingItems) set.Add(i);
            // named drops: guaranteed loot-table entries (boss and story loot)
            foreach (var t in db.LootTables.Values)
                foreach (var e in t.entries)
                    if (!e.random && !string.IsNullOrEmpty(e.item) && e.chance >= 100f) set.Add(e.item);
            return set;
        }

        /// <summary>Best usable database item that can replace the generated piece (same slot type, armour type / weapon type).</summary>
        static ItemDef BestDatabaseItem(GameDatabase db, Unit u, ItemDef gen, HashSet<string> reserved, HashSet<string> used)
        {
            ItemDef best = null;
            float bestScore = 0f;
            var maxArmor = EquipmentRules.MaxArmor(u);
            bool weapon = gen.weaponType != WeaponType.None;
            foreach (var d in db.Items.Values)
            {
                if (d.equip == EquipType.None || d.equip != gen.equip) continue;
                if (d.unique || d.kind == ItemKind.Quest || !string.IsNullOrEmpty(d.quest)) continue;
                if (d.quality > Quality.Uncommon || reserved.Contains(d.id) || used.Contains(d.id)) continue;   // rare+ = named items
                if (d.requiredLevel > u.Level) continue;
                if (weapon) { if (d.weaponType != gen.weaponType) continue; }
                else if (d.weaponType != WeaponType.None) continue;
                else if (gen.armorType != ArmorType.None && d.armorType != maxArmor && gen.equip != EquipType.Back) continue;
                if (EquipmentRules.CannotUseReason(u, d) != null) continue;
                float sc = Score(d);
                if (best == null || sc > bestScore || (sc == bestScore && string.CompareOrdinal(d.id, best.id) < 0))
                {
                    best = d;
                    bestScore = sc;
                }
            }
            return best;
        }
    }
}
