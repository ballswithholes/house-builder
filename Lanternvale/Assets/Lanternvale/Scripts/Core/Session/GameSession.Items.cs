// Inventory, equipment, item and ability use, loot windows, vendors, trainers, talents and respec.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        readonly Dictionary<string, VendorShop> vendors = new Dictionary<string, VendorShop>(StringComparer.Ordinal);

        /// <summary>Open vendor window (null when closed).</summary>
        public VendorShop ActiveVendor { get; private set; }
        /// <summary>Open trainer window (null when closed).</summary>
        public NpcDef ActiveTrainer { get; private set; }
        /// <summary>Npc id of the open respec window ("" when closed).</summary>
        public string ActiveRespecNpc { get; private set; } = "";
        /// <summary>Loot waiting to be picked up (null when none).</summary>
        public LootWindow PendingLoot { get; private set; }

        // ================================================================= bags & gold

        public int Gold => Inventory.Gold;

        /// <summary>How many of an item the party carries in its bags.</summary>
        public int CountItem(string itemId) => string.IsNullOrEmpty(itemId) ? 0 : Inventory.Count(itemId);

        /// <summary>Adds database items to the bags (dialogue outcome, quest reward). Raises ItemReceived.</summary>
        public void GiveItem(string itemId, int count)
        {
            if (resetting || count <= 0) return;
            var def = Db.Item(itemId);
            if (def == null)
            {
                Log.Warn($"GameSession: unknown item '{itemId}'");
                return;
            }
            int added = Inventory.Add(def, count);
            if (added > 0)
                Raise(new SessionEvent { Kind = SessionEventKind.ItemReceived, Id = itemId, Amount = added, Item = Inventory.Find(itemId), Text = ReceivedText(def.name, added) });
            if (added < count) Toast($"You cannot carry more {def.name}.");
        }

        public void TakeItem(string itemId, int count)
        {
            if (resetting || count <= 0) return;
            int n = Math.Min(count, Inventory.Count(itemId));
            if (n <= 0) return;
            Inventory.Remove(itemId, n);
            Raise(new SessionEvent { Kind = SessionEventKind.ItemLost, Id = itemId, Amount = n, Text = n > 1 ? $"Lost: {ItemName(itemId)} x{n}" : $"Lost: {ItemName(itemId)}" });
        }

        public void GiveGold(int copper)
        {
            if (resetting || copper == 0) return;
            if (copper < 0) { TakeGold(-copper); return; }
            Inventory.AddGold(copper);
            Raise(new SessionEvent { Kind = SessionEventKind.GoldChanged, Amount = copper, Text = "+" + Money(copper) });
        }

        public void TakeGold(int copper)
        {
            if (resetting || copper <= 0) return;
            int n = Math.Min(copper, Inventory.Gold);
            if (n <= 0) return;
            Inventory.AddGold(-n);
            Raise(new SessionEvent { Kind = SessionEventKind.GoldChanged, Amount = -n, Text = "-" + Money(n) });
        }

        static string ReceivedText(string name, int count) => count > 1 ? $"Received: {name} x{count}" : $"Received: {name}";

        /// <summary>Adds an item instance to the bags with a toast.</summary>
        void ReceiveInstance(ItemInstance it)
        {
            if (it == null) return;
            int n = it.Count;
            Inventory.Add(it);
            Raise(new SessionEvent { Kind = SessionEventKind.ItemReceived, Id = it.Id, Amount = n, Item = it, Text = ReceivedText(it.Name, n) });
        }

        void RaiseGoldDelta(int before, string why = "")
        {
            int d = Inventory.Gold - before;
            if (d == 0) return;
            Raise(new SessionEvent { Kind = SessionEventKind.GoldChanged, Amount = d, Text = (d > 0 ? "+" : "-") + Money(d) + (why.Length > 0 ? " " + why : "") });
        }

        /// <summary>Destroys (throws away) items from the bags. Quest items cannot be destroyed.</summary>
        public string DestroyItem(ItemInstance item, int count = 1)
        {
            if (item == null || !Inventory.Items.Contains(item)) return "No such item.";
            if (!string.IsNullOrEmpty(item.Def.quest) || item.Def.kind == ItemKind.Quest) return "Quest items cannot be destroyed.";
            count = Math.Min(Math.Max(1, count), item.Count);
            Inventory.Remove(item, count);
            Raise(new SessionEvent { Kind = SessionEventKind.ItemLost, Id = item.Id, Amount = count, Item = item, Text = $"Destroyed: {item.Name}" });
            return null;
        }

        // ================================================================= equipment

        /// <summary>Why the item cannot be equipped (in that slot, or anywhere), or null.</summary>
        public string CanEquip(Unit u, ItemInstance item, EquipSlot? slot = null)
        {
            if (u == null || !roster.Contains(u)) return "Only party members can equip items.";
            if (item == null || !Inventory.Items.Contains(item)) return "The item is not in your bags.";
            if (Battle != null) return "Cannot change equipment during combat.";
            if (slot.HasValue) return EquipmentRules.CannotEquipReason(u, item.Def, slot.Value);
            var why = EquipmentRules.CannotUseReason(u, item.Def);
            if (why != null) return why;
            return EquipmentRules.ChooseSlot(u, item.Def).HasValue ? null : "You cannot equip that.";
        }

        /// <summary>Equips an item from the bags (displaced items go back to the bags).</summary>
        public string Equip(Unit u, ItemInstance item, EquipSlot? slot = null)
        {
            var why = CanEquip(u, item, slot);
            if (why != null) return why;
            var s = slot ?? EquipmentRules.ChooseSlot(u, item.Def).Value;
            ItemInstance inst;
            if (item.Count > 1)
            {
                inst = item.CloneOne();
                Inventory.Remove(item, 1);
            }
            else
            {
                inst = item;
                Inventory.Take(item);
            }
            var displaced = EquipmentRules.Equip(u, inst, s);
            foreach (var d in displaced) if (d != null) Inventory.Add(d);
            u.ClampResources();
            if (Field != null) Field.RefreshAreaAuras();
            return null;
        }

        public string Unequip(Unit u, EquipSlot slot)
        {
            if (u == null || !roster.Contains(u)) return "Not a party member.";
            if (Battle != null) return "Cannot change equipment during combat.";
            var it = EquipmentRules.Unequip(u, slot);
            if (it == null) return "Nothing equipped there.";
            Inventory.Add(it);
            return null;
        }

        // ================================================================= abilities & items

        /// <summary>The action bar of a unit (battle or field context).</summary>
        public List<AbilityStatus> GetAbilityBar(Unit u)
        {
            var ctx = ContextFor(u);
            return ctx != null ? ctx.GetAbilityBar(u) : new List<AbilityStatus>();
        }

        /// <summary>The battle when fighting, else the field (exploration) context, if the unit is in it.</summary>
        Battle ContextFor(Unit u)
        {
            if (u == null) return null;
            if (Battle != null) return Battle.Units.Contains(u) ? Battle : null;
            if (Field == null) RebuildField();
            return Field != null && Field.Units.Contains(u) ? Field : null;
        }

        /// <summary>Uses an ability in combat (active unit) or out of combat (buffs, summons, Call Pet…).</summary>
        public ActionResult UseAbility(Unit u, string abilityId, Unit target = null, Vec2? point = null)
        {
            if (!hasGame || gameOver) return ActionResult.Fail("No game.");
            if (Battle == null && Dialogue.IsActive) return ActionResult.Fail("Not during a conversation.");
            var ctx = ContextFor(u);
            if (ctx == null) return ActionResult.Fail("That unit is not in the party.");
            var r = ctx.UseAbility(u, abilityId, target, point);
            if (r.Ok && ctx == Field) AfterFieldAction();
            return r;
        }

        public string CannotUseItemReason(Unit user, ItemInstance item, Unit target = null)
        {
            if (item == null || !Inventory.Items.Contains(item)) return "The item is not in your bags.";
            if (string.IsNullOrEmpty(item.Def.use) || Db.Ability(item.Def.use) == null) return $"{item.Name} cannot be used.";
            var ctx = ContextFor(user);
            if (ctx == null) return "That unit is not in the party.";
            var a = Db.Ability(item.Def.use);
            if (a.target == TargetType.Self) target = user;
            var chk = target != null ? ctx.CanUse(user, a, target, null, true) : ctx.CanUseIgnoringTarget(user, a, true);
            return chk.Ok ? null : chk.Reason;
        }

        /// <summary>Uses a bag item (potion, food, bandage…). Food/drink only out of combat.</summary>
        public ActionResult UseItem(Unit user, ItemInstance item, Unit target = null, Vec2? point = null)
        {
            if (!hasGame || gameOver) return ActionResult.Fail("No game.");
            if (Battle == null && Dialogue.IsActive) return ActionResult.Fail("Not during a conversation.");
            if (item == null || !Inventory.Items.Contains(item)) return ActionResult.Fail("The item is not in your bags.");
            var ctx = ContextFor(user);
            if (ctx == null) return ActionResult.Fail("That unit is not in the party.");
            var r = ctx.UseItem(user, item, target, point);
            if (r.Ok && ctx == Field) AfterFieldAction();
            return r;
        }

        void AfterFieldAction()
        {
            if (Field == null) return;
            Field.RefreshAreaAuras();
        }

        // ================================================================= loot

        void OpenLoot(LootWindow w)
        {
            if (w == null) return;
            if (w.Gold > 0)
            {
                Inventory.AddGold(w.Gold);
                Raise(new SessionEvent { Kind = SessionEventKind.GoldChanged, Amount = w.Gold, Text = "+" + Money(w.Gold) });
            }
            if (w.Items.Count == 0) return;
            if (PendingLoot != null && PendingLoot != w)
            {
                PendingLoot.Items.AddRange(w.Items);
                PendingLoot.Gold += w.Gold;
            }
            else PendingLoot = w;
            Raise(new SessionEvent { Kind = SessionEventKind.LootOpened, Loot = PendingLoot, Id = PendingLoot.Source, Text = PendingLoot.Title });
        }

        /// <summary>Moves one item from the loot window into the bags.</summary>
        public string TakeLoot(ItemInstance item)
        {
            if (PendingLoot == null || item == null || !PendingLoot.Items.Remove(item)) return "Nothing to take.";
            ReceiveInstance(item);
            if (PendingLoot.Items.Count == 0) CloseLoot();
            return null;
        }

        public void TakeAllLoot()
        {
            if (PendingLoot == null) return;
            foreach (var it in new List<ItemInstance>(PendingLoot.Items)) ReceiveInstance(it);
            PendingLoot.Items.Clear();
            CloseLoot();
        }

        /// <summary>Closes the loot window. Items not taken are lost unless <paramref name="takeAll"/>.</summary>
        public void CloseLoot(bool takeAll = false)
        {
            if (PendingLoot == null) return;
            var w = PendingLoot;
            if (takeAll)
            {
                foreach (var it in new List<ItemInstance>(w.Items)) ReceiveInstance(it);
                w.Items.Clear();
            }
            PendingLoot = null;
            Raise(new SessionEvent { Kind = SessionEventKind.LootClosed, Loot = w, Id = w.Source });
        }

        // ================================================================= vendors

        /// <summary>The persistent shop of a vendor npc (null when the npc sells nothing).</summary>
        public VendorShop GetVendor(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            if (vendors.TryGetValue(npcId, out var s)) return s;
            if (!Db.Npcs.TryGetValue(npcId, out var npc) || npc.vendor == null || npc.vendor.Count == 0) return null;
            s = new VendorShop(Db, npc, null, null);
            vendors[npcId] = s;
            return s;
        }

        /// <summary>Opens a vendor window (dialogue outcome; deferred until the dialogue ends).</summary>
        public void OpenVendor(string npcId)
        {
            if (resetting) return;
            if (Dialogue.IsActive) { afterDialogue.Add(() => OpenVendor(npcId)); return; }
            var s = GetVendor(npcId);
            if (s == null) { Log.Warn($"GameSession: '{npcId}' is not a vendor"); return; }
            ActiveVendor = s;
            Raise(new SessionEvent { Kind = SessionEventKind.VendorOpened, Id = npcId, Text = s.Npc.name });
        }

        public void CloseVendor()
        {
            if (ActiveVendor == null) return;
            var id = ActiveVendor.Npc.id;
            ActiveVendor = null;
            Raise(new SessionEvent { Kind = SessionEventKind.VendorClosed, Id = id });
        }

        public string Buy(string itemId, int count = 1)
        {
            if (ActiveVendor == null) return "No merchant.";
            int gold = Inventory.Gold;
            var why = ActiveVendor.Buy(Inventory, itemId, count);
            if (why != null) return why;
            RaiseGoldDelta(gold);
            Raise(new SessionEvent { Kind = SessionEventKind.ItemReceived, Id = itemId, Amount = count, Item = Inventory.Find(itemId), Text = ReceivedText(ItemName(itemId), count) });
            return null;
        }

        public string Sell(ItemInstance item, int count = 1)
        {
            if (ActiveVendor == null) return "No merchant.";
            int gold = Inventory.Gold;
            var why = ActiveVendor.Sell(Inventory, item, count);
            if (why != null) return why;
            RaiseGoldDelta(gold);
            return null;
        }

        public string BuyBack(ItemInstance item)
        {
            if (ActiveVendor == null) return "No merchant.";
            int gold = Inventory.Gold;
            var why = ActiveVendor.BuyBack(Inventory, item);
            if (why != null) return why;
            RaiseGoldDelta(gold);
            Raise(new SessionEvent { Kind = SessionEventKind.ItemReceived, Id = item.Id, Amount = item.Count, Item = item, Text = ReceivedText(item.Name, item.Count) });
            return null;
        }

        // ================================================================= trainers

        public void OpenTrainer(string npcId)
        {
            if (resetting) return;
            if (Dialogue.IsActive) { afterDialogue.Add(() => OpenTrainer(npcId)); return; }
            if (!Db.Npcs.TryGetValue(npcId ?? "", out var npc) || npc.trains == null || npc.trains.Length == 0)
            {
                Log.Warn($"GameSession: '{npcId}' is not a trainer");
                return;
            }
            ActiveTrainer = npc;
            Raise(new SessionEvent { Kind = SessionEventKind.TrainerOpened, Id = npc.id, Text = npc.name });
        }

        public void CloseTrainer()
        {
            if (ActiveTrainer == null) return;
            var id = ActiveTrainer.id;
            ActiveTrainer = null;
            Raise(new SessionEvent { Kind = SessionEventKind.TrainerClosed, Id = id });
        }

        /// <summary>The open trainer teaches this unit's class.</summary>
        public bool CanTrainHere(Unit u) =>
            ActiveTrainer != null && u != null && u.Class != null && Array.IndexOf(ActiveTrainer.trains, u.ClassId) >= 0;

        /// <summary>Ranks the unit can learn (and the next upcoming ones), gold checked.</summary>
        public List<TrainerOffer> TrainerOffers(Unit u) =>
            u == null ? new List<TrainerOffer>() : Progression.TrainerOffers(u, Inventory);

        /// <summary>Trains an ability rank at the open trainer (rank 0 = highest available now; lower ranks are paid too).</summary>
        public string Train(Unit u, string abilityId, int rank = 0)
        {
            if (ActiveTrainer == null) return "Find a trainer first.";
            if (u == null || !roster.Contains(u)) return "Not a party member.";
            if (!CanTrainHere(u)) return $"{ActiveTrainer.name} cannot teach a {u.Class?.name ?? "creature"}.";
            var a = Db.Ability(abilityId);
            if (a == null) return "Unknown ability.";
            if (rank <= 0) rank = AbilityRules.MaxRankAtLevel(a, u.Level);
            if (rank <= 0) return $"Requires level {AbilityRules.RankLevel(a, 1)}.";
            int gold = Inventory.Gold;
            var before = new Dictionary<string, int>(u.Abilities);
            var why = Progression.Train(u, a, rank, Inventory);
            if (why != null) return why;
            RaiseGoldDelta(gold);
            RaiseNewAbilities(u, before);
            if (Field != null) Field.RefreshAreaAuras();
            return null;
        }

        /// <summary>Trains every affordable rank (cheapest first). Returns how many abilities were trained.</summary>
        public int TrainAll(Unit u)
        {
            if (!CanTrainHere(u)) return 0;
            int n = 0;
            foreach (var o in TrainerOffers(u))
            {
                if (!o.CanTrain || u.RankOf(o.Ability.id) >= o.Rank) continue;
                if (Train(u, o.Ability.id, o.Rank) == null) n++;
            }
            return n;
        }

        // ================================================================= talents & respec

        public int TalentPointsAvailable(Unit u) => u == null ? 0 : Progression.TalentPointsAvailable(u);

        /// <summary>Spends one talent point. Returns the reason when it cannot.</summary>
        public string LearnTalent(Unit u, string talentId)
        {
            if (u == null || !roster.Contains(u)) return "Not a party member.";
            if (Battle != null) return "Cannot change talents during combat.";
            var before = new Dictionary<string, int>(u.Abilities);
            var why = Progression.LearnTalent(u, talentId);
            if (why != null) return why;
            RaiseNewAbilities(u, before);
            if (Field != null) Field.RefreshAreaAuras();
            return null;
        }

        /// <summary>Spends all free points on the unit's preferred build. Returns points spent.</summary>
        public int AutoAllocateTalents(Unit u)
        {
            if (u == null || !roster.Contains(u) || Battle != null) return 0;
            var before = new Dictionary<string, int>(u.Abilities);
            int n = Progression.AutoAllocateTalents(u);
            RaiseNewAbilities(u, before);
            return n;
        }

        public void OpenRespec(string npcId)
        {
            if (resetting) return;
            if (Dialogue.IsActive) { afterDialogue.Add(() => OpenRespec(npcId)); return; }
            ActiveRespecNpc = npcId ?? "";
            Raise(new SessionEvent { Kind = SessionEventKind.RespecOpened, Id = ActiveRespecNpc, Text = NpcName(ActiveRespecNpc) });
        }

        public void CloseRespec()
        {
            if (ActiveRespecNpc.Length == 0) return;
            var id = ActiveRespecNpc;
            ActiveRespecNpc = "";
            Raise(new SessionEvent { Kind = SessionEventKind.RespecClosed, Id = id });
        }

        public int RespecCost(Unit u) => u == null ? 0 : Progression.RespecCost(u);

        /// <summary>Unlearns every talent for gold (respec window must be open).</summary>
        public string Respec(Unit u)
        {
            if (u == null || !roster.Contains(u)) return "Not a party member.";
            if (Battle != null) return "Cannot change talents during combat.";
            if (ActiveRespecNpc.Length == 0) return "Ask a trainer to help you unlearn your talents.";
            if (Progression.TalentPointsSpent(u) == 0) return "There is nothing to unlearn.";
            int cost = Progression.RespecCost(u);
            int gold = Inventory.Gold;
            if (!Inventory.SpendGold(cost)) return "Not enough money.";
            Progression.ResetTalents(u);
            if (Field != null) Field.RefreshAreaAuras();
            RaiseGoldDelta(gold);
            int pts = Progression.TalentPointsAvailable(u);
            Raise(new SessionEvent { Kind = SessionEventKind.TalentPointsAvailable, Unit = u, Amount = pts, Text = $"{u.Name}'s talents are reset ({pts} points to spend)." });
            return null;
        }
    }
}
