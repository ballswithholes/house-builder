// Item sets (Docs/Expansion.md §2.5, §5): a set bonus is active while at least `pieces` DISTINCT items of its set are
// equipped (two copies of a set ring count once). Membership comes only from the set's items list
// (GameDatabase.SetOf). The active bonuses are cached per Equipment.Version; their consumers are StatCalculator
// (stats and Stat effects), Battle.FireProcs (Proc effects, cooldown key ProcKey), Specials (Special effects, Source
// "set:<id>") and AbilityMods.For (AbilityMod effects). Equipment cannot change in combat, so bonuses never flip mid-fight.
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    /// <summary>One active bonus of a worn set.</summary>
    public struct ActiveSetBonus
    {
        public ItemSetDef Set;
        public SetBonusDef Bonus;
        /// <summary>Index of <see cref="Bonus"/> in <c>Set.bonuses</c>.</summary>
        public int Index;
    }

    /// <summary>How much of a set a unit wears (tooltips, the character sheet, set-bonus toasts).</summary>
    public sealed class SetProgress
    {
        public ItemSetDef Set;
        /// <summary>Distinct set items equipped.</summary>
        public int Equipped;
        /// <summary>Per <c>Set.items</c>: that item is equipped.</summary>
        public bool[] PieceEquipped = new bool[0];
        /// <summary>Per <c>Set.bonuses</c>: that bonus is active.</summary>
        public bool[] BonusActive = new bool[0];

        /// <summary>Number of items in the set.</summary>
        public int Total => Set != null ? Set.items.Length : 0;

        /// <summary>How many bonuses are active.</summary>
        public int ActiveCount
        {
            get { int n = 0; foreach (var b in BonusActive) if (b) n++; return n; }
        }

        /// <summary>Pieces of the highest active bonus (0 = none active).</summary>
        public int ActiveTier
        {
            get
            {
                int t = 0;
                for (int i = 0; i < BonusActive.Length; i++) if (BonusActive[i]) t = Math.Max(t, Set.bonuses[i].pieces);
                return t;
            }
        }
    }

    public static class ItemSets
    {
        static readonly ActiveSetBonus[] NoBonuses = new ActiveSetBonus[0];

        /// <summary>Proc cooldown key of a set-bonus proc (saved in <see cref="Unit.ProcCooldowns"/>): "s:&lt;set&gt;:&lt;bonus&gt;:&lt;effect&gt;".</summary>
        public static string ProcKey(ActiveSetBonus b, int effectIndex) => "s:" + b.Set.id + ":" + b.Index + ":" + effectIndex;

        /// <summary>Source id of a set-bonus Special passive (<see cref="Specials.CurrentSource"/>): "set:&lt;set id&gt;".</summary>
        public static string SourceId(ItemSetDef set) => "set:" + (set != null ? set.id : "");

        /// <summary>True when the bonus is active at that many distinct equipped pieces.</summary>
        public static bool IsActive(SetBonusDef b, int equippedPieces) => b != null && equippedPieces >= Math.Max(1, b.pieces);

        /// <summary>Distinct items of the set the unit has equipped (0 for a null unit or set).</summary>
        public static int EquippedPieces(Unit u, ItemSetDef set) => u == null ? 0 : EquippedPieces(EquippedIds(u), set);

        /// <summary>Distinct items of the set among the given item ids (duplicates count once).</summary>
        public static int EquippedPieces(IEnumerable<string> itemIds, ItemSetDef set)
        {
            if (set == null || set.items == null || itemIds == null) return 0;
            int n = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in itemIds)
                if (id != null && seen.Add(id) && Array.IndexOf(set.items, id) >= 0) n++;
            return n;
        }

        /// <summary>
        /// The unit's active set bonuses, ordered by set id then bonus index (deterministic proc order). Empty for units
        /// without a database or that are not characters. Cached per <see cref="Equipment.Version"/>: do not modify it.
        /// </summary>
        public static IReadOnlyList<ActiveSetBonus> Active(Unit u)
        {
            if (u == null || u.Db == null || !u.IsCharacter || u.Db.ItemSets.Count == 0) return NoBonuses;
            var eq = u.Equipment;
            if (eq.SetCache is List<ActiveSetBonus> cached && eq.SetCacheVersion == eq.Version && eq.SetCacheIndex == u.Db.SetIndexVersion)
                return cached;
            var list = ActiveFor(u.Db, EquippedIds(u));
            eq.SetCache = list;
            eq.SetCacheVersion = eq.Version;
            eq.SetCacheIndex = u.Db.SetIndexVersion;
            return list;
        }

        /// <summary>
        /// The set bonuses that would be active with exactly these items equipped (duplicates count once). Used for
        /// "what if I swap this piece" comparisons; <see cref="Active"/> is the cached form for a unit.
        /// </summary>
        public static List<ActiveSetBonus> ActiveFor(GameDatabase db, IEnumerable<string> equippedIds)
        {
            var list = new List<ActiveSetBonus>();
            if (db == null || equippedIds == null) return list;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in equippedIds) if (!string.IsNullOrEmpty(id)) ids.Add(id);
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var sets = new Dictionary<string, ItemSetDef>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var set = db.SetOf(id);
                if (set == null) continue;
                counts.TryGetValue(set.id, out var n);
                counts[set.id] = n + 1;
                sets[set.id] = set;
            }
            foreach (var kv in counts)
            {
                var set = sets[kv.Key];
                for (int i = 0; i < set.bonuses.Count; i++)
                    if (IsActive(set.bonuses[i], kv.Value)) list.Add(new ActiveSetBonus { Set = set, Bonus = set.bonuses[i], Index = i });
            }
            return list;
        }

        /// <summary>
        /// Which pieces and bonuses of the set the unit has (a null unit: nothing equipped, for tooltips without a
        /// member). Null when the set is null.
        /// </summary>
        public static SetProgress Progress(Unit u, ItemSetDef set)
        {
            if (set == null) return null;
            var items = set.items ?? new string[0];
            var p = new SetProgress { Set = set, PieceEquipped = new bool[items.Length], BonusActive = new bool[set.bonuses.Count] };
            if (u != null && u.IsCharacter)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < items.Length; i++)
                {
                    if (string.IsNullOrEmpty(items[i]) || !u.Equipment.Contains(items[i])) continue;
                    p.PieceEquipped[i] = true;
                    if (seen.Add(items[i])) p.Equipped++;
                }
            }
            for (int i = 0; i < set.bonuses.Count; i++) p.BonusActive[i] = IsActive(set.bonuses[i], p.Equipped);
            return p;
        }

        /// <summary>Every set the unit wears at least one piece of, by set id (the character sheet's set block).</summary>
        public static List<SetProgress> Worn(Unit u)
        {
            var list = new List<SetProgress>();
            if (u == null || u.Db == null || !u.IsCharacter || u.Db.ItemSets.Count == 0) return list;
            var sets = new SortedDictionary<string, ItemSetDef>(StringComparer.Ordinal);
            foreach (var kv in u.Equipment.Equipped)
            {
                var set = u.Db.SetOf(kv.Value.Def.id);
                if (set != null) sets[set.id] = set;
            }
            foreach (var set in sets.Values) list.Add(Progress(u, set));
            return list;
        }

        /// <summary>Ids of the unit's equipped items (one per occupied slot).</summary>
        public static List<string> EquippedIds(Unit u)
        {
            var ids = new List<string>();
            if (u == null) return ids;
            foreach (var kv in u.Equipment.Equipped) ids.Add(kv.Value.Def.id);
            return ids;
        }
    }
}
