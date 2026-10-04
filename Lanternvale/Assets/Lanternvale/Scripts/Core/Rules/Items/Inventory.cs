// Shared party inventory (bags) and gold.
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    public sealed class Inventory
    {
        public readonly List<ItemInstance> Items = new List<ItemInstance>();
        /// <summary>Copper.</summary>
        public int Gold;
        /// <summary>Per-item caps (soul_shard: 32).</summary>
        public readonly Dictionary<string, int> MaxCount = new Dictionary<string, int> { { RulesConstants.SoulShardItem, RulesConstants.MaxSoulShards } };

        /// <summary>Raised with (itemId, new total count) whenever a stack changes.</summary>
        public event Action<string, int> Changed;
        public event Action<int> GoldChanged;

        public int Count(string itemId)
        {
            int n = 0;
            foreach (var it in Items) if (it.Def.id == itemId) n += it.Count;
            return n;
        }

        public bool Has(string itemId, int count = 1) => Count(itemId) >= count;

        public ItemInstance Find(string itemId)
        {
            foreach (var it in Items) if (it.Def.id == itemId) return it;
            return null;
        }

        public ItemInstance FindByUid(long uid)
        {
            foreach (var it in Items) if (it.Uid == uid) return it;
            return null;
        }

        /// <summary>Adds count copies of a database item (stacking). Returns the number actually added (caps).</summary>
        public int Add(ItemDef def, int count = 1)
        {
            if (def == null || count <= 0) return 0;
            int cap = MaxCount.TryGetValue(def.id, out var c) ? c : int.MaxValue;
            int have = Count(def.id);
            int add = Math.Min(count, Math.Max(0, cap - have));
            int left = add;
            int maxStack = Math.Max(1, def.stack);
            foreach (var it in Items)
            {
                if (left <= 0) break;
                if (it.Def.id != def.id || it.Generated || it.SuffixId != "" || it.Count >= maxStack) continue;
                int put = Math.Min(left, maxStack - it.Count);
                it.Count += put; left -= put;
            }
            while (left > 0)
            {
                int put = Math.Min(left, maxStack);
                Items.Add(new ItemInstance(def, put));
                left -= put;
            }
            if (add > 0) Changed?.Invoke(def.id, Count(def.id));
            return add;
        }

        /// <summary>Adds an item instance (merging into stacks when possible).</summary>
        public void Add(ItemInstance inst)
        {
            if (inst == null) return;
            if (!inst.Generated && inst.SuffixId == "" && inst.MaxStack > 1)
            {
                Add(inst.Def, inst.Count);
                return;
            }
            Items.Add(inst);
            Changed?.Invoke(inst.Def.id, Count(inst.Def.id));
        }

        /// <summary>Removes count items by id. Returns false (and removes nothing) when there are not enough.</summary>
        public bool Remove(string itemId, int count = 1)
        {
            if (count <= 0) return true;
            if (Count(itemId) < count) return false;
            int left = count;
            for (int i = Items.Count - 1; i >= 0 && left > 0; i--)
            {
                var it = Items[i];
                if (it.Def.id != itemId) continue;
                int take = Math.Min(left, it.Count);
                it.Count -= take; left -= take;
                if (it.Count <= 0) Items.RemoveAt(i);
            }
            Changed?.Invoke(itemId, Count(itemId));
            return true;
        }

        /// <summary>Removes count from a specific instance (whole instance when count ≥ its stack).</summary>
        public bool Remove(ItemInstance inst, int count = 1)
        {
            int idx = Items.IndexOf(inst);
            if (idx < 0 || count <= 0 || inst.Count < count) return false;
            inst.Count -= count;
            if (inst.Count <= 0) Items.RemoveAt(idx);
            Changed?.Invoke(inst.Def.id, Count(inst.Def.id));
            return true;
        }

        /// <summary>Takes an instance out of the bags entirely (for equipping).</summary>
        public bool Take(ItemInstance inst)
        {
            if (!Items.Remove(inst)) return false;
            Changed?.Invoke(inst.Def.id, Count(inst.Def.id));
            return true;
        }

        public void AddGold(int copper)
        {
            if (copper == 0) return;
            Gold = Math.Max(0, Gold + copper);
            GoldChanged?.Invoke(Gold);
        }

        public bool SpendGold(int copper)
        {
            if (copper < 0 || Gold < copper) return false;
            Gold -= copper;
            GoldChanged?.Invoke(Gold);
            return true;
        }

        public static string FormatMoney(int copper)
        {
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            var parts = new List<string>();
            if (g > 0) parts.Add(g + "g");
            if (s > 0) parts.Add(s + "s");
            if (c > 0 || parts.Count == 0) parts.Add(c + "c");
            return string.Join(" ", parts);
        }
    }
}
