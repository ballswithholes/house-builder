// Loot tables, battle results (XP + loot) and vendors (buy, sell at 1/4, buyback).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed class LootDrop
    {
        public readonly List<ItemInstance> Items = new List<ItemInstance>();
        public int Gold;
        public void Merge(LootDrop o) { Items.AddRange(o.Items); Gold += o.Gold; }
    }

    public static class LootGenerator
    {
        /// <summary>Rolls a loot table: gold in [goldMin, goldMax], then `rolls` passes over the entries (each by its chance).</summary>
        public static LootDrop Roll(GameDatabase db, string tableId, int level, Rng rng)
        {
            var drop = new LootDrop();
            if (string.IsNullOrEmpty(tableId) || !db.LootTables.TryGetValue(tableId, out var t)) return drop;
            drop.Gold = rng.Range(Math.Max(0, t.goldMin), Math.Max(t.goldMin, t.goldMax));
            int rolls = Math.Max(1, t.rolls);
            for (int r = 0; r < rolls; r++)
            {
                foreach (var e in t.entries)
                {
                    if (!rng.Chance(e.chance)) continue;
                    int n = rng.Range(Math.Max(1, e.min), Math.Max(e.min, e.max));
                    if (e.random)
                    {
                        for (int i = 0; i < n; i++) drop.Items.Add(ItemGenerator.RandomItem(db, rng, Math.Max(1, level + e.itemLevelOffset), e.quality));
                    }
                    else
                    {
                        var def = db.Item(e.item);
                        if (def == null) continue;
                        drop.Items.Add(new ItemInstance(def, n));
                    }
                }
            }
            return drop;
        }
    }

    /// <summary>Outcome of a finished battle: XP per party member, kills and loot (not yet applied).</summary>
    public sealed class BattleResult
    {
        public BattleOutcome Outcome;
        public int Rounds;
        public readonly List<Unit> Defeated = new List<Unit>();
        public readonly Dictionary<Unit, int> Xp = new Dictionary<Unit, int>();
        public readonly LootDrop Loot = new LootDrop();
        public bool Applied;

        public static BattleResult Compute(Battle b)
        {
            var r = new BattleResult { Outcome = b.Outcome, Rounds = b.Round };
            if (b.Outcome != BattleOutcome.Victory) return r;
            foreach (var u in b.Units)
                if (u.Team != b.PlayerTeam && u.Dead && u.Kind == UnitKind.Creature) r.Defeated.Add(u);
            foreach (var m in b.Units)
            {
                if (m.Team != b.PlayerTeam || !m.IsCharacter || m.Dead) continue;
                int xp = 0;
                foreach (var d in r.Defeated) xp += Progression.KillXp(b.Db, m.Level, d);
                r.Xp[m] = xp;
            }
            foreach (var d in r.Defeated)
                if (d.Creature != null && !string.IsNullOrEmpty(d.Creature.lootTable))
                    r.Loot.Merge(LootGenerator.Roll(b.Db, d.Creature.lootTable, d.Level, b.Rng));
            return r;
        }
    }

    /// <summary>A vendor's shop: stock, buying, selling at 1/4 price, buyback of the last sold items.</summary>
    public sealed class VendorShop
    {
        public readonly NpcDef Npc;
        readonly GameDatabase db;
        /// <summary>Remaining stock per item id (-1 unlimited). Persisted in saves by the session.</summary>
        public readonly Dictionary<string, int> Stock;
        public readonly List<ItemInstance> Buyback;

        public VendorShop(GameDatabase db, NpcDef npc, Dictionary<string, int> stock, List<ItemInstance> buyback)
        {
            this.db = db;
            Npc = npc;
            Stock = stock ?? new Dictionary<string, int>();
            Buyback = buyback ?? new List<ItemInstance>();
            foreach (var v in npc.vendor) if (!Stock.ContainsKey(v.item)) Stock[v.item] = v.stock;
        }

        public sealed class Offer
        {
            public ItemDef Item;
            public int Price;
            public int Stock;   // -1 unlimited
            public override string ToString() => $"{Item.name} {Inventory.FormatMoney(Price)}{(Stock >= 0 ? $" ({Stock} left)" : "")}";
        }

        public List<Offer> Offers()
        {
            var list = new List<Offer>();
            foreach (var v in Npc.vendor)
            {
                var def = db.Item(v.item);
                if (def == null) continue;
                Stock.TryGetValue(v.item, out var s);
                list.Add(new Offer { Item = def, Price = v.priceOverride > 0 ? v.priceOverride : def.price, Stock = s });
            }
            return list;
        }

        public string Buy(Inventory inv, string itemId, int count = 1)
        {
            if (count <= 0) return "Nothing to buy.";
            Offer o = null;
            foreach (var x in Offers()) if (x.Item.id == itemId) { o = x; break; }
            if (o == null) return "Not sold here.";
            if (o.Stock >= 0 && o.Stock < count) return "Out of stock.";
            int cost = o.Price * count;
            if (!inv.SpendGold(cost)) return "Not enough money.";
            inv.Add(o.Item, count);
            if (o.Stock >= 0) Stock[itemId] = o.Stock - count;
            return null;
        }

        /// <summary>Sells count of an inventory item for 1/4 of its price (quest items cannot be sold).</summary>
        public string Sell(Inventory inv, ItemInstance item, int count = 1)
        {
            if (item == null || !inv.Items.Contains(item)) return "No such item.";
            if (!string.IsNullOrEmpty(item.Def.quest) || item.Def.kind == ItemKind.Quest) return "Quest items cannot be sold.";
            if (item.Def.price <= 0) return "The merchant does not want that.";
            count = Math.Min(count, item.Count);
            var sold = item.CloneOne();
            sold.Count = count;
            if (!inv.Remove(item, count)) return "Cannot sell that.";
            inv.AddGold(item.SellPrice * count);
            Buyback.Insert(0, sold);
            if (Buyback.Count > 12) Buyback.RemoveAt(Buyback.Count - 1);
            return null;
        }

        /// <summary>Buys back a sold item at the price it was sold for.</summary>
        public string BuyBack(Inventory inv, ItemInstance item)
        {
            if (!Buyback.Contains(item)) return "Not available.";
            int cost = item.SellPrice * item.Count;
            if (!inv.SpendGold(cost)) return "Not enough money.";
            Buyback.Remove(item);
            inv.Add(item);
            return null;
        }
    }
}
