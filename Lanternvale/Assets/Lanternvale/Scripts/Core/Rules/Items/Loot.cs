// Loot tables (named items, random items, pools with party context), battle results (XP + loot) and vendors (buy, sell
// at 1/4, buyback).
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

    /// <summary>
    /// What a loot roll knows about the party (Docs/Expansion.md §2.4, §5). Only the new loot-entry keys read it:
    /// <c>perMembers</c> (Members), <c>partyUsable</c> (Party), <c>skipOwned</c> (Owned, Dropped) and pools (Dropped).
    /// One context spans one battle (every defeated creature's table) or one chest, so two bosses of one fight do not
    /// drop the same pool item twice. The session builds it (GameSession.BuildLootContext); without one, a roll sees a
    /// party of one that owns nothing.
    /// </summary>
    public sealed class LootContext
    {
        /// <summary>Party characters in the fight (an entry with perMembers rolls ceil(Members / perMembers) times).</summary>
        public int Members = 1;
        /// <summary>The party's characters (partyUsable keeps pool ids one of them can use; empty = no filter).</summary>
        public readonly List<Unit> Party = new List<Unit>();
        /// <summary>True when the party already owns an item id: in the bags or equipped by any roster member (camp too).</summary>
        public Func<string, bool> Owned;
        /// <summary>Database item ids dropped so far by rolls with this context.</summary>
        public readonly HashSet<string> Dropped = new HashSet<string>(StringComparer.Ordinal);

        public bool IsOwned(string itemId) => Owned != null && !string.IsNullOrEmpty(itemId) && Owned(itemId);

        /// <summary>Some party character can use the item (see <see cref="CanUse"/>); true when <see cref="Party"/> is empty.</summary>
        public bool UsableBySomeone(ItemDef d)
        {
            if (Party.Count == 0) return true;
            foreach (var u in Party) if (CanUse(u, d)) return true;
            return false;
        }

        /// <summary>
        /// The character could equip the item apart from its required level: an equipable item of its class (or of no
        /// class), an armour type it wears now (Back always) and a weapon type it can use. Gear a few levels ahead
        /// counts as usable; armour the class only wears from a later level (Warrior plate at 40) does not.
        /// </summary>
        public static bool CanUse(Unit u, ItemDef d)
        {
            if (u == null || d == null || d.equip == EquipType.None || !u.IsCharacter) return false;
            if (d.classes != null && d.classes.Length > 0 && Array.IndexOf(d.classes, u.ClassId) < 0) return false;
            if (d.equip != EquipType.Back && !EquipmentRules.CanWearArmor(u, d.armorType)) return false;
            if (d.weaponType != WeaponType.None && !EquipmentRules.CanUseWeapon(u, d.weaponType)) return false;
            return true;
        }

        /// <summary>
        /// A context for a fight's party: <paramref name="party"/>'s characters (pets and summons are skipped) are the
        /// members; owned = in <paramref name="bags"/> or equipped by a party character or anyone in <paramref name="roster"/>.
        /// </summary>
        public static LootContext For(IEnumerable<Unit> party, Inventory bags, IEnumerable<Unit> roster = null)
        {
            var ctx = new LootContext();
            var owners = new List<Unit>();
            if (party != null)
                foreach (var u in party)
                    if (u != null && u.IsCharacter && !ctx.Party.Contains(u)) { ctx.Party.Add(u); owners.Add(u); }
            if (roster != null)
                foreach (var u in roster)
                    if (u != null && !owners.Contains(u)) owners.Add(u);
            ctx.Members = Math.Max(1, ctx.Party.Count);
            ctx.Owned = id =>
            {
                if (bags != null && bags.Has(id)) return true;
                foreach (var u in owners) if (u.Equipment.Contains(id)) return true;
                return false;
            };
            return ctx;
        }

        /// <summary>The battle's own context when the session set none: its player-team characters and inventory.</summary>
        public static LootContext ForBattle(Battle b)
        {
            var party = new List<Unit>();
            foreach (var u in b.Units) if (u.Team == b.PlayerTeam && u.IsCharacter) party.Add(u);
            return For(party, b.Inventory);
        }
    }

    public sealed partial class Battle
    {
        /// <summary>Loot context of this fight (set by the session at battle creation; null = built from the battle's party).</summary>
        public LootContext LootContext;
    }

    public static class LootGenerator
    {
        /// <summary>
        /// Rolls a loot table: gold in [goldMin, goldMax], then `rolls` passes over the entries. Each entry rolls once
        /// (ceil(members / perMembers) times with perMembers) by its chance, then gives `min..max` of its item, of a
        /// random item, or of one id picked from its pool (see <see cref="PickFromPool"/>). Tables without the new keys
        /// consume the RNG exactly as before, with or without a context.
        /// </summary>
        public static LootDrop Roll(GameDatabase db, string tableId, int level, Rng rng, LootContext ctx = null)
        {
            var drop = new LootDrop();
            if (string.IsNullOrEmpty(tableId) || !db.LootTables.TryGetValue(tableId, out var t)) return drop;
            ctx = ctx ?? new LootContext();
            drop.Gold = rng.Range(Math.Max(0, t.goldMin), Math.Max(t.goldMin, t.goldMax));
            int rolls = Math.Max(1, t.rolls);
            int members = Math.Max(1, ctx.Members);
            for (int r = 0; r < rolls; r++)
            {
                foreach (var e in t.entries)
                {
                    int times = e.perMembers > 0 ? (members + e.perMembers - 1) / e.perMembers : 1;
                    for (int k = 0; k < times; k++)
                    {
                        if (!rng.Chance(e.chance)) continue;
                        int n = rng.Range(Math.Max(1, e.min), Math.Max(e.min, e.max));
                        if (e.random)
                        {
                            for (int i = 0; i < n; i++) drop.Items.Add(ItemGenerator.RandomItem(db, rng, Math.Max(1, level + e.itemLevelOffset), e.quality));
                            continue;
                        }
                        var def = e.pool != null && e.pool.Length > 0 ? PickFromPool(db, e, rng, ctx) : db.Item(e.item);
                        if (def == null) continue;
                        drop.Items.Add(new ItemInstance(def, n));
                        ctx.Dropped.Add(def.id);
                    }
                }
            }
            return drop;
        }

        /// <summary>
        /// One item of a pool entry: the pool's database items, kept to those a party member can use (partyUsable) and,
        /// with skipOwned, to those the party neither owns nor has seen drop with this context (null when none is left).
        /// Without skipOwned the ids that have not dropped yet are preferred, so a 10-raid's two rolls (and two bosses
        /// of one fight) give different pieces while the pool allows. Picks uniformly, or by <c>weights</c> (one per
        /// pool id; ids weighted 0 never drop).
        /// </summary>
        public static ItemDef PickFromPool(GameDatabase db, LootEntryDef e, Rng rng, LootContext ctx = null)
        {
            if (e?.pool == null || e.pool.Length == 0) return null;
            ctx = ctx ?? new LootContext();
            bool weighted = e.weights != null && e.weights.Length == e.pool.Length;
            var cands = new List<ItemDef>();
            var weights = new List<float>();
            for (int i = 0; i < e.pool.Length; i++)
            {
                var def = db.Item(e.pool[i]);
                if (def == null || cands.Contains(def)) continue;
                float w = weighted ? e.weights[i] : 1f;
                if (w <= 0f) continue;
                if (e.partyUsable && !ctx.UsableBySomeone(def)) continue;
                if (e.skipOwned && (ctx.IsOwned(def.id) || ctx.Dropped.Contains(def.id))) continue;
                cands.Add(def);
                weights.Add(w);
            }
            if (!e.skipOwned && cands.Exists(c => !ctx.Dropped.Contains(c.id)))
            {
                for (int i = cands.Count - 1; i >= 0; i--)
                {
                    if (!ctx.Dropped.Contains(cands[i].id)) continue;
                    cands.RemoveAt(i);
                    weights.RemoveAt(i);
                }
            }
            if (cands.Count == 0) return null;
            if (cands.Count == 1) return cands[0];
            if (!weighted) return cands[rng.Range(0, cands.Count - 1)];
            float total = 0f;
            foreach (var w in weights) total += w;
            float x = rng.Value * total;
            for (int i = 0; i < cands.Count; i++)
            {
                x -= weights[i];
                if (x < 0f) return cands[i];
            }
            return cands[cands.Count - 1];
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
            var ctx = b.LootContext ?? LootContext.ForBattle(b);
            foreach (var d in r.Defeated)
                if (d.Creature != null && !string.IsNullOrEmpty(d.Creature.lootTable))
                    r.Loot.Merge(LootGenerator.Roll(b.Db, d.Creature.lootTable, d.Level, b.Rng, ctx));
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
