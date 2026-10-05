// Action bar (bottom centre) for GameFlow.Selected — in combat the active player unit. Abilities come from
// Session.GetAbilityBar (the battle in combat, the field context outside; pets, demons and controlled creatures also list
// their data-"hidden" abilities, see FetchBar). Icons use the school colour, show a
// clockwise cooldown sweep, dim with the rules' reason when unusable, cost / cast-time text, hotkeys 1–0 - =,
// pages of 12 (wheel over the bar or the arrows), drag-to-rearrange across pages (wheel, or rest on a pager arrow while
// dragging; saved per character), and an "Active" glow
// for stances/aspects/auto attack/queued swings/targeting/armed openers. A consumables strip (potions, food,
// bandages from the party bags) sits to its left. Clicks/hotkeys → Combat.BeginAbility/BeginItem in combat,
// GameFlow.UseAbilityOutOfCombat/UseItemOutOfCombat in the field (ally spells first ask for a party member).
// Statuses are fetched without tooltips (GetAbilityBar(.., includeTooltips: false)); a slot's tooltip is built on hover.
// Ranks pinned in the Spellbook (RankPins, WoW downranking) are cast at that rank and show an "R3" badge; enemy abilities
// out of range of the unit's attack target get a red tint (AbilityStatus.InRangeOfAttackTarget).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class ActionBarHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderActionBar;
        public bool Visible => unit != null && Hud.WorldHud;
        public bool Modal => false;

        static readonly KeyCode[] Keys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0, KeyCode.Minus, KeyCode.Equals,
        };

        sealed class Slot
        {
            public AbilityStatus St;
            public AbilityDef A;
            public string Cost = "", Cast = "";
            public Color CostColor;
            public bool Shard;          // consumes a Soul Shard (bag count shown in the corner)
            public float CdMax;
            public string Tip;
            public int Rank;            // pinned rank cast by this slot (0 = the highest known rank)
            public string RankLabel = "";
        }

        sealed class ItemSlot
        {
            public ItemInstance Item;
            public AbilityDef Use;
            public int Count;
            public string Reason;
            public float CdMax;
            public string Tip;
        }

        // ---- snapshot (Tick)
        Unit unit;
        bool inCombat, inspect, busy;
        readonly List<Slot> slots = new List<Slot>(48);
        readonly List<ItemSlot> items = new List<ItemSlot>(12);
        string orderKey = "";
        int page, pageCount = 1;
        float refreshTimer;
        int lastEvents = -1;
        Unit lastUnit;
        bool lastPlayerTurn;
        AbilityDef lastTargeting;
        string lastOpener = "";
        int lastBagSig;
        int lastPins = -1, lastQueue = -1;
        bool forceRefresh;

        readonly Dictionary<string, AbilityStatus> byId = new Dictionary<string, AbilityStatus>();
        readonly HashSet<string> used = new HashSet<string>();
        readonly Dictionary<string, float> cdMax = new Dictionary<string, float>();
        readonly HashSet<string> seenItems = new HashSet<string>();
        static readonly Dictionary<string, List<string>> savedOrders = new Dictionary<string, List<string>>();
        static readonly Dictionary<string, int> savedPages = new Dictionary<string, int>();

        // ---- mouse (Draw)
        // A left press remembers the ABSOLUTE slot index (across pages) and its ability id, so a drag survives page turns
        // (wheel or hovering a pager arrow) and drops onto whatever page is showing.
        int pressSlot = -1;
        string pressAbility;
        int pressItem = -1;
        Vector2 pressPos;
        bool dragging;
        int pagerHoverDir;          // -1 = up arrow, +1 = down arrow, 0 = none (only while dragging)
        float pagerHoverSince;
        const float DragPageDelay = 0.4f;
        string pagerTip;
        int pagerTipPage = -1, pagerTipCount;

        public ActionBarHud()
        {
            Hud.HotkeyOf = HotkeyOf;
        }

        // ================================================================ tick

        public void Tick(float dt)
        {
            Hud.RollOccluders();
            HudPresented.Update();
            Hud.RunCommands();
            try
            {
                UpdateUnit();
                if (unit != lastUnit) ClearPress();
                Refresh(dt);
                HandleKeys();
                // a release the bar never saw (another panel used the event, focus lost) must not leave a drag stuck
                if ((pressSlot >= 0 || pressItem >= 0) && !GameInput.MouseHeld(0) && !GameInput.MouseDown(0) && !GameInput.MouseUp(0)) ClearPress();
            }
            catch (Exception e) { Hud.LogOnce("bar-tick:" + e.GetType().Name, "Action bar: " + e); }
        }

        void UpdateUnit()
        {
            unit = null;
            inCombat = inspect = busy = false;
            if (!GameFlow.HasGame) { Hud.FieldPick = null; return; }
            var s = Hud.Session;
            var f = Hud.Flow;
            if (s.Mode == SessionMode.Combat)
            {
                var c = Hud.Combat;
                if (c == null || c.Battle == null) return;
                inCombat = true;
                Hud.FieldPick = null;
                var ap = Hud.ActivePlayerUnit;
                if (ap != null)
                {
                    unit = ap;
                    busy = !c.IsPlayerTurn;
                }
                else
                {
                    unit = f.Selected;
                    if (unit == null || !c.Battle.Units.Contains(unit)) unit = s.Leader;
                    inspect = true;
                }
            }
            else if (s.Mode == SessionMode.Exploration)
            {
                unit = f.Selected ?? s.Leader;
                var p = Hud.FieldPick;
                if (p != null && (p.Caster != unit || p.Caster == null || p.Caster.Dead)) Hud.FieldPick = null;
            }
            else Hud.FieldPick = null;
        }

        void Refresh(float dt)
        {
            if (unit == null) { if (slots.Count > 0) { slots.Clear(); items.Clear(); Hud.BarStatuses.Clear(); Hud.BarUnit = null; } return; }
            refreshTimer -= dt;
            var c = Hud.Combat;
            int events = c != null && c.Battle != null ? c.Battle.Events.Count : -1;
            bool playerTurn = c != null && c.IsPlayerTurn;
            var targeting = c != null ? c.TargetingAbility : null;
            string opener = Hud.Flow != null ? Hud.Flow.PendingOpener ?? "" : "";
            int bagSig = BagSignature();
            int pins = RankPins.Version, queueV = c != null ? c.QueueVersion : -1;
            bool dirty = forceRefresh || unit != lastUnit || events != lastEvents || playerTurn != lastPlayerTurn || targeting != lastTargeting ||
                         opener != lastOpener || bagSig != lastBagSig || pins != lastPins || queueV != lastQueue || refreshTimer <= 0f;
            if (!dirty) return;
            lastPins = pins;
            lastQueue = queueV;
            if (unit != lastUnit) cdMax.Clear();
            forceRefresh = false;
            lastUnit = unit;
            lastEvents = events;
            lastPlayerTurn = playerTurn;
            lastTargeting = targeting;
            lastOpener = opener;
            lastBagSig = bagSig;
            refreshTimer = inCombat ? 0.6f : 0.3f;
            Rebuild();
        }

        int BagSignature()
        {
            var s = Hud.Session;
            if (s == null) return 0;
            var list = s.Inventory.Items;
            int sig = list.Count * 397;
            for (int i = 0; i < list.Count; i++) sig = sig * 31 + list[i].Count;
            return sig;
        }

        void Rebuild()
        {
            var s = Hud.Session;
            List<AbilityStatus> bar;
            try { bar = FetchBar(s, unit); }
            catch (Exception e) { Hud.LogOnce("bar", "GetAbilityBar failed: " + e.Message); bar = new List<AbilityStatus>(); }

            Hud.BarStatuses.Clear();
            Hud.BarStatuses.AddRange(bar);
            Hud.BarUnit = unit;

            orderKey = OrderKey(unit);
            var saved = LoadOrder(orderKey);
            byId.Clear();
            used.Clear();
            for (int i = 0; i < bar.Count; i++)
                if (bar[i] != null && bar[i].Ability != null && !byId.ContainsKey(bar[i].Ability.id)) byId[bar[i].Ability.id] = bar[i];

            slots.Clear();
            var ctx = ContextOf(s, unit);
            if (saved != null)
                for (int i = 0; i < saved.Count; i++)
                    if (byId.TryGetValue(saved[i], out var st) && used.Add(saved[i])) slots.Add(MakeSlot(Ranked(ctx, st)));
            for (int i = 0; i < bar.Count; i++)
            {
                var st = bar[i];
                if (st == null || st.Ability == null || !used.Add(st.Ability.id)) continue;
                slots.Add(MakeSlot(Ranked(ctx, st)));
            }
            pageCount = Mathf.Max(1, (slots.Count + HudLayout.SlotsPerPage - 1) / HudLayout.SlotsPerPage);
            savedPages.TryGetValue(orderKey, out page);
            page = Mathf.Clamp(page, 0, pageCount - 1);

            RebuildItems(s);

            // a rebuild mid-press keeps pointing at the pressed ability (its absolute index can shift if the bar changed)
            if (pressSlot >= 0)
            {
                pressSlot = IndexOfAbility(pressAbility);
                if (pressSlot < 0) dragging = false;
            }
        }

        /// <summary>
        /// The unit's bar statuses. Pets, demons and controlled creatures (no class) only know abilities that the data flags
        /// "hidden" (Bite, Growl, Firebolt, Torment, Seduction, Spell Lock…); Session.GetAbilityBar drops those, so for such units
        /// the bar comes straight from their context (the battle in combat, the field outside) with hidden abilities included,
        /// for units on the player's team. Totems are excluded: their hidden "pulse" abilities are internal, never player-chosen.
        /// </summary>
        static List<AbilityStatus> FetchBar(GameSession s, Unit u)
        {
            // refreshed often: no description text (Tooltip = ""), the slot builds its tooltip on hover
            if (u == null || u.Class != null || u.Kind == UnitKind.Totem) return s.GetAbilityBar(u, false);
            Battle ctx = s.Battle;
            if (ctx == null)
            {
                if (s.Field == null)
                {
                    var plain = s.GetAbilityBar(u, false);   // lets the session build its field context first
                    if (s.Field == null) return plain;
                }
                ctx = s.Field;
            }
            // only units on our side (an inspected enemy keeps the plain bar, so its kit is not spelled out)
            if (ctx == null || !ctx.Units.Contains(u) || u.Team != ctx.PlayerTeam) return s.GetAbilityBar(u, false);
            return ctx.GetAbilityBar(u, true, false);
        }

        /// <summary>The rules context the unit's bar comes from (the battle in combat, the field outside), or null.</summary>
        static Battle ContextOf(GameSession s, Unit u)
        {
            if (s == null || u == null) return null;
            var b = s.Battle;
            if (b != null) return b.Units.Contains(u) ? b : null;
            var f = s.Field;
            return f != null && f.Units.Contains(u) ? f : null;
        }

        /// <summary>The status at the unit's pinned rank (RankPins), or the highest-rank status the bar returned.</summary>
        AbilityStatus Ranked(Battle ctx, AbilityStatus st)
        {
            int rank = RankPins.RankFor(unit, st.Ability);
            if (rank <= 0 || ctx == null) return st;
            try { return ctx.GetStatus(unit, st.Ability, false, rank, false) ?? st; }
            catch (Exception e) { Hud.LogOnce("bar-rank", "GetStatus at a pinned rank failed: " + e.Message); return st; }
        }

        int IndexOfAbility(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].A != null && slots[i].A.id == id) return i;
            return -1;
        }

        Slot MakeSlot(AbilityStatus st)
        {
            var sl = new Slot { St = st, A = st.Ability, Shard = Hud.UsesSoulShard(st.Ability) };
            // a status built at a lower rank than the highest known one = a pinned rank (Ranked above)
            if (st.Rank > 0 && st.KnownRanks > 1 && st.Rank < st.KnownRanks)
            {
                sl.Rank = st.Rank;
                sl.RankLabel = RankBadge(st.Rank);
            }
            if (st.Cost > 0.5f && st.CostType != ResourceType.None)
            {
                sl.Cost = HudText.Int(Mathf.RoundToInt(st.Cost));
                sl.CostColor = Color.Lerp(Ui.ResourceColor(st.CostType), Color.white, 0.35f);
            }
            if (st.CastTime > 0.05f) sl.Cast = HudText.Secs(st.CastTime) + "s";
            float left = st.CooldownLeft;
            if (left > 0.01f)
            {
                cdMax.TryGetValue(st.Ability.id, out var prev);
                float m = Mathf.Max(st.Cooldown, Mathf.Max(prev, left));
                cdMax[st.Ability.id] = m;
                sl.CdMax = m;
            }
            else
            {
                cdMax.Remove(st.Ability.id);
                sl.CdMax = Mathf.Max(0.01f, st.Cooldown);
            }
            return sl;
        }

        void RebuildItems(GameSession s)
        {
            items.Clear();
            seenItems.Clear();
            var db = Hud.Db;
            if (db == null) return;
            var list = s.Inventory.Items;
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null || it.Def == null) continue;
                var d = it.Def;
                if (string.IsNullOrEmpty(d.use) || !string.IsNullOrEmpty(d.quest) || d.kind == ItemKind.Quest) continue;
                if (!(d.consumable || d.kind == ItemKind.Consumable || d.kind == ItemKind.Food || d.kind == ItemKind.Drink)) continue;
                var use = db.Ability(d.use);
                if (use == null) continue;
                if (inCombat && use.requires != null && use.requires.notInCombat) continue;   // food & drink wait for peace
                if (!seenItems.Add(d.id)) continue;
                string why = null;
                try { why = s.CannotUseItemReason(unit, it); }
                catch (Exception) { why = null; }
                float left = Hud.CooldownLeft(unit, use);
                cdMax.TryGetValue("item:" + d.id, out var prev);
                float m = left > 0.01f ? Mathf.Max(prev, left) : 0.01f;
                if (left > 0.01f) cdMax["item:" + d.id] = m; else cdMax.Remove("item:" + d.id);
                items.Add(new ItemSlot { Item = it, Use = use, Count = s.Inventory.Count(d.id), Reason = why, CdMax = m });
            }
            items.Sort((a, b) =>
            {
                int ka = KindRank(a.Item.Def.kind), kb = KindRank(b.Item.Def.kind);
                if (ka != kb) return ka.CompareTo(kb);
                return string.CompareOrdinal(a.Item.Def.name, b.Item.Def.name);
            });
        }

        static int KindRank(ItemKind k) => k == ItemKind.Consumable ? 0 : k == ItemKind.Food ? 1 : k == ItemKind.Drink ? 2 : 3;

        // ================================================================ keys & activation

        void HandleKeys()
        {
            if (unit == null) return;
            var pick = Hud.FieldPick;
            if (pick != null && (GameInput.KeyDown(KeyCode.Escape) || GameInput.MouseDown(1) || GameInput.WorldClick(0)))
            {
                Hud.FieldPick = null;
                UiRoot.HotkeysSuppressed = true;   // Esc cancels the choice instead of opening the pause menu
                Ui.Sfx?.Invoke("ui_close");
            }
            if (!Hud.WorldHud || UiRoot.ModalActive) return;
            for (int i = 0; i < Keys.Length; i++)
            {
                if (!GameInput.KeyDown(Keys[i])) continue;
                int idx = page * HudLayout.SlotsPerPage + i;
                if (idx < slots.Count) Activate(slots[idx], false);
            }
        }

        void Activate(Slot sl, bool selfCast)
        {
            if (sl == null || sl.A == null || unit == null) return;
            var a = sl.A;
            if (inCombat)
            {
                if (inspect) { Hud.Error(Hud.NameOf(unit) + " must wait for their turn."); return; }
                var c = Hud.Combat;
                if (c == null) return;
                var why = c.BeginAbility(a.id, sl.Rank);   // failures arrive through Combat.LastError (red error lane)
                // right-click = self-cast (WoW): a friendly spell that entered targeting mode is confirmed on the caster
                if (selfCast && why == null && c.IsTargeting && c.TargetingItem == null && c.TargetingAbility != null &&
                    c.TargetingAbility.id == a.id && SelfCastable(a.target))
                    c.TargetUnit(unit);
                return;
            }
            UseInFieldWithPick(a, null, a.target, selfCast, sl.Rank);
        }

        void ActivateItem(ItemSlot it, bool selfCast)
        {
            if (it == null || it.Item == null || unit == null) return;
            if (inCombat)
            {
                if (inspect) { Hud.Error(Hud.NameOf(unit) + " must wait for their turn."); return; }
                var c = Hud.Combat;
                if (c == null) return;
                var why = c.BeginItem(it.Item);
                if (selfCast && why == null && c.IsTargeting && c.TargetingItem == it.Item && it.Use != null && SelfCastable(it.Use.target))
                    c.TargetUnit(unit);
                return;
            }
            if (!string.IsNullOrEmpty(it.Reason)) { Hud.Error(it.Reason); return; }
            UseInFieldWithPick(null, it.Item, it.Use != null ? it.Use.target : TargetType.Self, selfCast);
        }

        /// <summary>Target types a right-click may confirm on the caster (friendly spells; hostile ones keep targeting).</summary>
        static bool SelfCastable(TargetType t) => t == TargetType.Ally || t == TargetType.Any;

        void UseInFieldWithPick(AbilityDef a, ItemInstance item, TargetType target, bool selfCast, int rank = 0)
        {
            var pick = Hud.FieldPick;
            bool samePick = pick != null && pick.Caster == unit && pick.Ability == a && pick.Item == item;
            if (samePick || selfCast)
            {
                Hud.FieldPick = null;
                if (target == TargetType.AllyOther) { Hud.Error("Choose another party member."); return; }
                Hud.UseInField(unit, a, item, target == TargetType.DeadAlly ? null : unit, rank);
                return;
            }
            bool allyish = target == TargetType.Ally || target == TargetType.AllyOther || target == TargetType.DeadAlly || target == TargetType.Any;
            if (allyish)
            {
                int candidates = CountCandidates(target);
                if (target == TargetType.DeadAlly && candidates == 0) { Hud.Error("Nobody in the party needs to be brought back."); return; }
                if (target == TargetType.AllyOther && candidates == 0) { Hud.Error("There is nobody else to target."); return; }
                if (candidates > 1 || target == TargetType.DeadAlly || target == TargetType.AllyOther)
                {
                    Hud.FieldPick = new Hud.FieldPickState { Caster = unit, Ability = a, Item = item, Target = target, Rank = rank };
                    Ui.Sfx?.Invoke("ui_open");
                    return;
                }
            }
            Hud.FieldPick = null;
            Hud.UseInField(unit, a, item, null, rank);
        }

        int CountCandidates(TargetType t)
        {
            var s = Hud.Session;
            if (s == null) return 0;
            int n = 0;
            var units = Hud.Flow != null ? Hud.Flow.PartyUnits : s.Party;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null) continue;
                switch (t)
                {
                    case TargetType.DeadAlly: if (u.Dead || u.Downed) n++; break;
                    case TargetType.AllyOther: if (u != unit && !u.Dead) n++; break;
                    default: if (!u.Dead) n++; break;
                }
            }
            return n;
        }

        string HotkeyOf(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) return "";
            int start = page * HudLayout.SlotsPerPage;
            for (int i = 0; i < HudLayout.SlotsPerPage; i++)
            {
                int idx = start + i;
                if (idx >= slots.Count) break;
                if (slots[idx].A != null && slots[idx].A.id == abilityId) return HudText.Hotkey(i);
            }
            return "";
        }

        // ================================================================ order persistence

        static string OrderKey(Unit u)
        {
            var s = Hud.Session;
            string mid = "";
            try { mid = s != null ? s.MemberId(u) : ""; } catch (Exception) { }
            if (!string.IsNullOrEmpty(mid)) return mid + "." + u.ClassId;
            if (u.Owner != null)
            {
                string oid = "";
                try { oid = s != null ? s.MemberId(u.Owner) : ""; } catch (Exception) { }
                return (oid.Length > 0 ? oid : "unit") + ".pet." + (u.Creature != null ? u.Creature.id : u.Name);
            }
            return "unit." + (u.Creature != null ? u.Creature.id : u.ClassId.ToString());
        }

        static List<string> LoadOrder(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (savedOrders.TryGetValue(key, out var list)) return list;
            list = new List<string>();
            try
            {
                var raw = PlayerPrefs.GetString("lv.hud.bar." + key, "");
                if (!string.IsNullOrEmpty(raw))
                    foreach (var id in raw.Split(',')) if (id.Length > 0) list.Add(id);
            }
            catch (Exception) { }
            savedOrders[key] = list;
            return list;
        }

        void SaveOrder()
        {
            var ids = new List<string>(slots.Count);
            for (int i = 0; i < slots.Count; i++) if (slots[i].A != null) ids.Add(slots[i].A.id);
            savedOrders[orderKey] = ids;
            try
            {
                PlayerPrefs.SetString("lv.hud.bar." + orderKey, string.Join(",", ids));
                PlayerPrefs.Save();
            }
            catch (Exception e) { Hud.LogOnce("prefs", "Saving the bar order failed: " + e.Message); }
        }

        void ResetOrder()
        {
            savedOrders[orderKey] = new List<string>();
            try { PlayerPrefs.DeleteKey("lv.hud.bar." + orderKey); PlayerPrefs.Save(); } catch (Exception) { }
            forceRefresh = true;
            Hud.Error("Action bar order reset.");
        }

        void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= slots.Count || b >= slots.Count || a == b) return;
            var t = slots[a];
            slots[a] = slots[b];
            slots[b] = t;
            SaveOrder();
            Ui.Sfx?.Invoke("ui_click");
        }

        void ClearPress()
        {
            pressSlot = -1;
            pressItem = -1;
            pressAbility = null;
            dragging = false;
            pagerHoverDir = 0;
        }

        void SetPage(int p)
        {
            int np = ((p % pageCount) + pageCount) % pageCount;
            if (np == page) return;
            page = np;
            savedPages[orderKey] = page;
            Ui.Sfx?.Invoke("ui_click");
        }

        // ================================================================ draw

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try { DrawInner(); }
            catch (Exception e) { Hud.LogOnce("bar-draw:" + e.GetType().Name, "Action bar draw: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        void DrawInner()
        {
            HudStyles.Ensure();
            var u = unit;
            if (u == null) return;
            var e = Event.current;
            var panel = HudLayout.BarPanel;
            float alpha = inspect ? 0.62f : busy ? 0.8f : 1f;

            Ui.Panel(panel, Ui.InkPanelSoft, true);

            // header (exploration): whose bar is this
            if (!inCombat)
            {
                var hr = new Rect(panel.x + 6f, panel.y - 22f, 360f, 20f);
                HudDraw.Text(hr, Hud.NameOf(u), HudStyles.NameSmall, Hud.UnitColor(u));
            }

            var c = Hud.Combat;
            var targeting = c != null ? c.TargetingAbility : null;
            string opener = Hud.Flow != null ? Hud.Flow.PendingOpener : "";
            var pick = Hud.FieldPick;

            int start = page * HudLayout.SlotsPerPage;
            int hovered = -1;
            for (int i = 0; i < HudLayout.SlotsPerPage; i++)
            {
                var r = HudLayout.SlotRect(i);
                int idx = start + i;
                if (idx >= slots.Count)
                {
                    HudDraw.Fill(r, new Color(0.03f, 0.02f, 0.07f, 0.45f), 8);
                    HudDraw.Ring(r, new Color(1f, 1f, 1f, 0.08f), 8);
                    HudDraw.Text(new Rect(r.x + 4f, r.y + 1f, 24f, 18f), HudText.Hotkey(i), HudStyles.Tiny, new Color(1f, 1f, 1f, 0.25f), false);
                    continue;
                }
                var sl = slots[idx];
                bool glow = sl.St.Active || (targeting != null && targeting == sl.A) || (!string.IsNullOrEmpty(opener) && opener == sl.A.id) ||
                            (pick != null && pick.Ability == sl.A && pick.Item == null);
                if (!(dragging && pressSlot == idx)) DrawSlot(r, sl, i, glow, alpha);
                else HudDraw.Fill(r, new Color(0.03f, 0.02f, 0.07f, 0.6f), 8);
                if (HudDraw.Hover(r)) hovered = i;
            }

            // pager
            if (pageCount > 1) DrawPager();

            // consumables
            DrawItems(alpha);

            // tooltips & hover hand-off
            if (hovered >= 0 && !dragging)
            {
                var sl = slots[start + hovered];
                var r = HudLayout.SlotRect(hovered);
                Hud.HoveredSlot = sl.St;
                if (sl.Tip == null) sl.Tip = BuildTip(sl, hovered);
                Ui.TooltipFor(r, sl.Tip);
            }

            // field pick hint
            if (pick != null && !inCombat) DrawPickHint(pick);

            HandleMouse(e, start);

            // drag ghost
            if (dragging && pressSlot >= 0 && pressSlot < slots.Count && HudDraw.IsRepaint)
            {
                var sl = slots[pressSlot];
                var m = e.mousePosition;
                HudDraw.Icon(new Rect(m.x - 26f, m.y - 26f, 52f, 52f), sl.A.icon, Hud.SchoolCol(sl.A.school), false, 0.85f);
            }
        }

        void DrawSlot(Rect r, Slot sl, int index, bool glow, float alpha)
        {
            var a = sl.A;
            var st = sl.St;
            var school = Hud.SchoolCol(a.school);
            bool usable = st.Usable && !inspect;
            if (glow)
            {
                float p = HudDraw.Pulse(4.5f, 0.45f, 0.95f);
                HudDraw.Glow(new Rect(r.x - 14f, r.y - 14f, r.width + 28f, r.height + 28f), new Color(1f, 0.86f, 0.45f, 0.55f * p * alpha));
            }
            HudDraw.Icon(r, string.IsNullOrEmpty(a.icon) ? "glyph_star" : a.icon, school, !usable, alpha);
            if (!inspect)
            {
                if (!usable && st.Code == UseFailure.Resource) HudDraw.Fill(r, new Color(0.15f, 0.25f, 0.75f, 0.35f), 8);
                // WoW's red icon: an enemy ability out of range (or inside the dead zone / out of sight) of the attack target
                else if (st.InRangeOfAttackTarget == false || (!usable && (st.Code == UseFailure.Range || st.Code == UseFailure.TooClose)))
                    HudDraw.Fill(r, new Color(0.8f, 0.15f, 0.12f, usable ? 0.36f : 0.3f), 8);
            }
            float left = Hud.CooldownLeft(unit, a);
            if (left > 0.01f)
            {
                HudDraw.Sweep(r, Mathf.Clamp01(left / Mathf.Max(sl.CdMax, left)));
                HudDraw.Text(new Rect(r.x, r.y + 6f, r.width, r.height - 12f), HudText.Duration(left), Ui.NumberStyle(20), Ui.TextLight);
            }
            if (glow) HudDraw.Ring(r, new Color(1f, 0.88f, 0.5f, HudDraw.Pulse(4.5f, 0.6f, 1f) * alpha), 8, true);
            // corners: hotkey (top left), cast time (top right), cost (bottom right) — or, for Soul Shard spells, the shards
            // left in the bags (like WoW's reagent count; the mana cost stays in the tooltip and the blue "no mana" tint)
            HudDraw.Text(new Rect(r.x + 4f, r.y + 1f, 24f, 18f), HudText.Hotkey(index), HudStyles.Tiny, new Color(1f, 1f, 1f, 0.85f * alpha));
            if (sl.Cast.Length > 0)
                HudDraw.Text(new Rect(r.x + 2f, r.y + 1f, r.width - 5f, 18f), sl.Cast, HudStyles.TinyRight, new Color(Ui.Time.r, Ui.Time.g, Ui.Time.b, alpha));
            if (sl.Shard)
            {
                int shards = Hud.SoulShards;
                var sc = shards > 0 ? ShardColor : Ui.Bad;
                HudDraw.Glyph(new Rect(r.x + 3f, r.yMax - 17f, 14f, 14f), "glyph_soul_shard", new Color(sc.r, sc.g, sc.b, alpha));
                HudDraw.Text(new Rect(r.x + 18f, r.yMax - 18f, r.width - 21f, 17f), HudText.Int(shards), HudStyles.Tiny, new Color(sc.r, sc.g, sc.b, alpha));
            }
            else if (sl.Cost.Length > 0)
                HudDraw.Text(new Rect(r.x + 2f, r.yMax - 18f, r.width - 5f, 17f), sl.Cost, HudStyles.TinyRight, new Color(sl.CostColor.r, sl.CostColor.g, sl.CostColor.b, alpha));
            // pinned lower rank: a small "R3" plate (bottom left; top centre on Soul Shard spells, whose corner shows shards)
            if (sl.RankLabel.Length > 0)
            {
                var br = sl.Shard ? new Rect(r.center.x - 13f, r.y + 2f, 26f, 15f) : new Rect(r.x + 3f, r.yMax - 17f, 26f, 15f);
                HudDraw.Fill(br, new Color(0.08f, 0.06f, 0.13f, 0.85f * alpha), 4);
                HudDraw.Ring(br, new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.7f * alpha), 4);
                HudDraw.Text(br, sl.RankLabel, HudStyles.TinyCenter, new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, alpha), false);
            }
        }

        static readonly string[] rankBadges = new string[32];

        static string RankBadge(int rank)
        {
            if (rank < 0 || rank >= rankBadges.Length) return "R" + rank;
            return rankBadges[rank] ??= "R" + rank;
        }

        static readonly Color ShardColor = Ui.Hex("#c9a6ff");

        void DrawPager()
        {
            var pr = HudLayout.Pager;
            HudDraw.Fill(pr, new Color(0.08f, 0.06f, 0.13f, 0.7f), 8);
            var up = new Rect(pr.x, pr.y, pr.width, 22f);
            var down = new Rect(pr.x, pr.yMax - 22f, pr.width, 22f);
            var mid = new Rect(pr.x, pr.y + 22f, pr.width, pr.height - 44f);
            bool hu = HudDraw.Hover(up), hd = HudDraw.Hover(down);
            // while dragging an ability, resting on an arrow for DragPageDelay turns the page (and keeps turning)
            if (dragging && HudDraw.IsRepaint)
            {
                int dir = hu ? -1 : hd ? 1 : 0;
                float now = Time.unscaledTime;
                if (dir == 0) pagerHoverDir = 0;
                else if (dir != pagerHoverDir) { pagerHoverDir = dir; pagerHoverSince = now; }
                else if (now - pagerHoverSince >= DragPageDelay) { SetPage(page + dir); pagerHoverSince = now; }
                if (pagerHoverDir != 0)
                {
                    var ar = pagerHoverDir < 0 ? up : down;
                    float k = Mathf.Clamp01((now - pagerHoverSince) / DragPageDelay);
                    HudDraw.Fill(new Rect(ar.x, ar.y, ar.width * k, ar.height), new Color(1f, 0.86f, 0.45f, 0.35f), 6);
                }
            }
            else if (!dragging) pagerHoverDir = 0;
            HudDraw.Arrow(new Rect(up.x + 7f, up.y + 5f, 16f, 12f), true, hu ? Ui.Gold : Ui.TextLight);
            HudDraw.Arrow(new Rect(down.x + 7f, down.y + 5f, 16f, 12f), false, hd ? Ui.Gold : Ui.TextLight);
            HudDraw.Text(mid, HudText.Int(page + 1), HudStyles.TinyCenter, Ui.Gold);
            Ui.Block(pr);
            if (HudDraw.Hover(pr) && !dragging)
            {
                if (pagerTip == null || pagerTipPage != page || pagerTipCount != pageCount)
                {
                    pagerTipPage = page;
                    pagerTipCount = pageCount;
                    pagerTip = "Page " + (page + 1) + " / " + pageCount + "\n" +
                               Ui.Rich("Mouse wheel over the bar also turns pages. While dragging an ability, rest on an arrow to turn the page.", Hud.Muted);
                }
                Ui.TooltipFor(pr, pagerTip);
            }
            if (HudDraw.Click(up)) SetPage(page - 1);
            else if (HudDraw.Click(down)) SetPage(page + 1);
        }

        void DrawItems(float alpha)
        {
            if (items.Count == 0) return;
            float right = HudLayout.ConsumablesRight;
            int fit = Mathf.Max(0, (int)((right - HudLayout.Margin - 16f) / (HudLayout.ItemSlot + HudLayout.ItemGap)));
            int n = Mathf.Min(items.Count, Mathf.Min(fit, 8));
            if (n <= 0) return;
            var bar = HudLayout.BarPanel;
            float w = n * HudLayout.ItemSlot + (n - 1) * HudLayout.ItemGap + 16f;
            var panel = new Rect(right - w, bar.y + 8f, w, bar.height - 16f);
            Ui.Panel(panel, Ui.InkPanelSoft, false);
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                var r = new Rect(panel.x + 8f + i * (HudLayout.ItemSlot + HudLayout.ItemGap), panel.y + (panel.height - HudLayout.ItemSlot) * 0.5f, HudLayout.ItemSlot, HudLayout.ItemSlot);
                var d = it.Item.Def;
                bool usable = it.Reason == null && !inspect;
                ItemArt.Draw(r, d, !usable, alpha);
                float left = Hud.CooldownLeft(unit, it.Use);
                if (left > 0.01f)
                {
                    HudDraw.Sweep(r, Mathf.Clamp01(left / Mathf.Max(it.CdMax, left)));
                    HudDraw.Text(new Rect(r.x, r.y + 4f, r.width, r.height - 10f), HudText.Duration(left), Ui.NumberStyle(16), Ui.TextLight);
                }
                if (it.Count > 1) HudDraw.Text(new Rect(r.x, r.yMax - 18f, r.width - 4f, 17f), HudText.Int(it.Count), HudStyles.TinyRight, Ui.TextLight);
                if (HudDraw.Hover(r))
                {
                    if (it.Tip == null)
                    {
                        it.Tip = UiText.Item(it.Item, unit);
                        if (!string.IsNullOrEmpty(it.Reason)) it.Tip += "\n" + Ui.Rich(it.Reason, Ui.Bad);
                        it.Tip += "\n" + Ui.Rich(inCombat ? "Click to use (costs Time like an ability)." : "Click to use.", Hud.Muted);
                    }
                    Ui.TooltipFor(r, it.Tip);
                }
            }
        }

        void DrawPickHint(Hud.FieldPickState pick)
        {
            var r = HudLayout.StatusPill;
            HudDraw.Frame(r, 0.85f, new Color(0.5f, 1f, 0.55f, 0.8f));
            HudDraw.Text(r, pick.Hint, HudStyles.BodyCenter, Ui.TextLight);
            Ui.Block(r);
        }

        string BuildTip(Slot sl, int index)
        {
            // the bar is fetched without tooltips: the description is built here, on hover, at the slot's rank
            string tip = UiText.Ability(unit, sl.A, sl.Rank);
            var st = sl.St;
            if (sl.Rank > 0)
                tip += "\n" + Ui.Rich("Pinned to Rank " + sl.Rank + " of " + st.KnownRanks + " (downranked: cheaper, weaker). Change it in the Spellbook (P).", Ui.Gold);
            if (!st.Usable && !string.IsNullOrEmpty(st.Reason)) tip += "\n" + Ui.Rich(st.Reason, Ui.Bad);
            else if (st.InRangeOfAttackTarget == false && unit != null && unit.AttackTarget != null)
                tip += "\n" + Ui.Rich("Out of range of " + Hud.NameOf(unit.AttackTarget) + ".", Ui.Bad);
            if (inCombat)
            {
                string time = st.TimeCost > 0.01f ? "Costs " + HudText.Secs(st.TimeCost) + " s of this turn's Time" : "No Time cost (off the GCD)";
                if (st.CastTime > 0.01f) time += " · cast " + HudText.Secs(st.CastTime) + " s (becomes pending if it does not fit)";
                tip += "\n" + Ui.Rich(time, Ui.Time);
            }
            if (sl.Shard)
            {
                int shards = Hud.SoulShards;
                tip += "\n" + Ui.Rich("Consumes a Soul Shard — " + (shards == 1 ? "1 shard" : HudText.Int(shards) + " shards") + " in your bags.", shards > 0 ? ShardColor : Ui.Bad);
            }
            if (st.Active) tip += "\n" + Ui.Rich("Active", Ui.Gold);
            string self = SelfCastable(sl.A.target) ? " · right-click casts on yourself" : "";
            tip += "\n" + Ui.Rich("Hotkey " + HudText.Hotkey(index) + self + " · drag to rearrange · Shift+right-click resets the order", Hud.Muted);
            return tip;
        }

        void HandleMouse(Event e, int start)
        {
            if (e == null) return;
            var m = e.mousePosition;
            var panel = HudLayout.BarPanel;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (pageCount > 1 && (panel.Contains(m) || HudLayout.Pager.Contains(m))) SetPage(page + (e.delta.y > 0 ? 1 : -1));
                    break;
                case EventType.MouseDown:
                {
                    int s = SlotAt(m);
                    int it = ItemAt(m);
                    if (e.button == 0)
                    {
                        ClearPress();
                        if (s >= 0 && start + s < slots.Count && slots[start + s].A != null)
                        {
                            pressSlot = start + s;
                            pressAbility = slots[pressSlot].A.id;
                        }
                        else if (s < 0 && it >= 0) pressItem = it;
                        pressPos = m;
                    }
                    else if (e.button == 1)
                    {
                        bool shift = e.shift;
                        if (s >= 0 && start + s < slots.Count)
                        {
                            if (shift) { Hud.Post(ResetOrder); break; }
                            var sl = slots[start + s];
                            Hud.Post(() => Activate(sl, true));   // right click = cast on yourself (WoW)
                        }
                        else if (it >= 0)
                        {
                            var item = items[it];
                            Hud.Post(() => ActivateItem(item, true));
                        }
                    }
                    break;
                }
                case EventType.MouseDrag:
                    if (pressSlot >= 0 && pressSlot < slots.Count && !dragging && (m - pressPos).sqrMagnitude > 64f) dragging = true;
                    break;
                case EventType.MouseUp:
                {
                    if (e.button != 0) break;
                    int src = pressSlot, pressedItem = pressItem;
                    bool wasDragging = dragging;
                    ClearPress();
                    if (pressedItem >= 0)
                    {
                        int it = ItemAt(m);
                        if (it == pressedItem && it < items.Count)
                        {
                            var item = items[it];
                            Hud.Post(() => ActivateItem(item, false));
                            Ui.Sfx?.Invoke("ui_click");
                        }
                        break;
                    }
                    if (src < 0 || src >= slots.Count) break;
                    int over = SlotAt(m);
                    if (over < 0) break;
                    int dst = start + over;   // the page showing now (the drag may have turned it)
                    if (wasDragging) Swap(src, dst);
                    else if (dst == src)
                    {
                        var sl = slots[src];
                        Hud.Post(() => Activate(sl, false));
                        Ui.Sfx?.Invoke("ui_click");
                    }
                    break;
                }
            }
        }

        int SlotAt(Vector2 m)
        {
            for (int i = 0; i < HudLayout.SlotsPerPage; i++)
                if (HudLayout.SlotRect(i).Contains(m)) return i;
            return -1;
        }

        int ItemAt(Vector2 m)
        {
            if (items.Count == 0) return -1;
            float right = HudLayout.ConsumablesRight;
            int fit = Mathf.Max(0, (int)((right - HudLayout.Margin - 16f) / (HudLayout.ItemSlot + HudLayout.ItemGap)));
            int n = Mathf.Min(items.Count, Mathf.Min(fit, 8));
            if (n <= 0) return -1;
            var bar = HudLayout.BarPanel;
            float w = n * HudLayout.ItemSlot + (n - 1) * HudLayout.ItemGap + 16f;
            var panel = new Rect(right - w, bar.y + 8f, w, bar.height - 16f);
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(panel.x + 8f + i * (HudLayout.ItemSlot + HudLayout.ItemGap), panel.y + (panel.height - HudLayout.ItemSlot) * 0.5f, HudLayout.ItemSlot, HudLayout.ItemSlot);
                if (r.Contains(m)) return i;
            }
            return -1;
        }
    }
}
