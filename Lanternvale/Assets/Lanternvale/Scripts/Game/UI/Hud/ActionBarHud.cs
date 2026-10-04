// Action bar (bottom centre) for GameFlow.Selected — in combat the active player unit. Abilities come from
// Session.GetAbilityBar (the battle in combat, the field context outside). Icons use the school colour, show a
// clockwise cooldown sweep, dim with the rules' reason when unusable, cost / cast-time text, hotkeys 1–0 - =,
// pages of 12 (wheel over the bar or the arrows), drag-to-rearrange (saved per character), and an "Active" glow
// for stances/aspects/auto attack/queued swings/targeting/armed openers. A consumables strip (potions, food,
// bandages from the party bags) sits to its left. Clicks/hotkeys → Combat.BeginAbility/BeginItem in combat,
// GameFlow.UseAbilityOutOfCombat/UseItemOutOfCombat in the field (ally spells first ask for a party member).
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
            public float CdMax;
            public string Tip;
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
        bool forceRefresh;

        readonly Dictionary<string, AbilityStatus> byId = new Dictionary<string, AbilityStatus>();
        readonly HashSet<string> used = new HashSet<string>();
        readonly Dictionary<string, float> cdMax = new Dictionary<string, float>();
        readonly HashSet<string> seenItems = new HashSet<string>();
        static readonly Dictionary<string, List<string>> savedOrders = new Dictionary<string, List<string>>();
        static readonly Dictionary<string, int> savedPages = new Dictionary<string, int>();

        // ---- mouse (Draw)
        int pressId = -1;
        Vector2 pressPos;
        bool dragging;

        public ActionBarHud()
        {
            Hud.HotkeyOf = HotkeyOf;
        }

        // ================================================================ tick

        public void Tick(float dt)
        {
            Hud.RollOccluders();
            Hud.RunCommands();
            try
            {
                UpdateUnit();
                Refresh(dt);
                HandleKeys();
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
            bool dirty = forceRefresh || unit != lastUnit || events != lastEvents || playerTurn != lastPlayerTurn || targeting != lastTargeting ||
                         opener != lastOpener || bagSig != lastBagSig || refreshTimer <= 0f;
            if (!dirty) return;
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
            try { bar = s.GetAbilityBar(unit); }
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
            if (saved != null)
                for (int i = 0; i < saved.Count; i++)
                    if (byId.TryGetValue(saved[i], out var st) && used.Add(saved[i])) slots.Add(MakeSlot(st));
            for (int i = 0; i < bar.Count; i++)
            {
                var st = bar[i];
                if (st == null || st.Ability == null || !used.Add(st.Ability.id)) continue;
                slots.Add(MakeSlot(st));
            }
            pageCount = Mathf.Max(1, (slots.Count + HudLayout.SlotsPerPage - 1) / HudLayout.SlotsPerPage);
            savedPages.TryGetValue(orderKey, out page);
            page = Mathf.Clamp(page, 0, pageCount - 1);

            RebuildItems(s);
        }

        Slot MakeSlot(AbilityStatus st)
        {
            var sl = new Slot { St = st, A = st.Ability };
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
                c?.BeginAbility(a.id);   // failures arrive through Combat.LastError (red error lane)
                return;
            }
            UseInFieldWithPick(a, null, a.target, selfCast);
        }

        void ActivateItem(ItemSlot it, bool selfCast)
        {
            if (it == null || it.Item == null || unit == null) return;
            if (inCombat)
            {
                if (inspect) { Hud.Error(Hud.NameOf(unit) + " must wait for their turn."); return; }
                Hud.Combat?.BeginItem(it.Item);
                return;
            }
            if (!string.IsNullOrEmpty(it.Reason)) { Hud.Error(it.Reason); return; }
            UseInFieldWithPick(null, it.Item, it.Use != null ? it.Use.target : TargetType.Self, selfCast);
        }

        void UseInFieldWithPick(AbilityDef a, ItemInstance item, TargetType target, bool selfCast)
        {
            var pick = Hud.FieldPick;
            bool samePick = pick != null && pick.Caster == unit && pick.Ability == a && pick.Item == item;
            if (samePick || selfCast)
            {
                Hud.FieldPick = null;
                if (target == TargetType.AllyOther) { Hud.Error("Choose another party member."); return; }
                Hud.UseInField(unit, a, item, target == TargetType.DeadAlly ? null : unit);
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
                    Hud.FieldPick = new Hud.FieldPickState { Caster = unit, Ability = a, Item = item, Target = target };
                    Ui.Sfx?.Invoke("ui_open");
                    return;
                }
            }
            Hud.FieldPick = null;
            Hud.UseInField(unit, a, item, null);
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
                    HudDraw.Text(new Rect(r.x + 4f, r.y + 2f, 20f, 14f), HudText.Hotkey(i), HudStyles.Tiny, new Color(1f, 1f, 1f, 0.25f), false);
                    continue;
                }
                var sl = slots[idx];
                bool glow = sl.St.Active || (targeting != null && targeting == sl.A) || (!string.IsNullOrEmpty(opener) && opener == sl.A.id) ||
                            (pick != null && pick.Ability == sl.A && pick.Item == null);
                if (!(dragging && pressId == i)) DrawSlot(r, sl, i, glow, alpha);
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
            if (dragging && pressId >= 0 && pressId < HudLayout.SlotsPerPage && start + pressId < slots.Count && HudDraw.IsRepaint)
            {
                var sl = slots[start + pressId];
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
            if (!usable && !inspect)
            {
                if (st.Code == UseFailure.Resource) HudDraw.Fill(r, new Color(0.15f, 0.25f, 0.75f, 0.35f), 8);
                else if (st.Code == UseFailure.Range || st.Code == UseFailure.TooClose) HudDraw.Fill(r, new Color(0.8f, 0.15f, 0.12f, 0.3f), 8);
            }
            float left = Hud.CooldownLeft(unit, a);
            if (left > 0.01f)
            {
                HudDraw.Sweep(r, Mathf.Clamp01(left / Mathf.Max(sl.CdMax, left)));
                HudDraw.Text(new Rect(r.x, r.y + 6f, r.width, r.height - 12f), HudText.Duration(left), Ui.NumberStyle(20), Ui.TextLight);
            }
            if (glow) HudDraw.Ring(r, new Color(1f, 0.88f, 0.5f, HudDraw.Pulse(4.5f, 0.6f, 1f) * alpha), 8, true);
            // hotkey, cost, cast time
            HudDraw.Text(new Rect(r.x + 4f, r.y + 2f, 22f, 14f), HudText.Hotkey(index), HudStyles.Tiny, new Color(1f, 1f, 1f, 0.85f * alpha));
            if (sl.Cost.Length > 0)
                HudDraw.Text(new Rect(r.x + 2f, r.yMax - 15f, r.width - 5f, 14f), sl.Cost, HudStyles.TinyRight, new Color(sl.CostColor.r, sl.CostColor.g, sl.CostColor.b, alpha));
            if (sl.Cast.Length > 0)
                HudDraw.Text(new Rect(r.x + 4f, r.yMax - 15f, r.width - 8f, 14f), sl.Cast, HudStyles.Tiny, new Color(Ui.Time.r, Ui.Time.g, Ui.Time.b, alpha));
        }

        void DrawPager()
        {
            var pr = HudLayout.Pager;
            HudDraw.Fill(pr, new Color(0.08f, 0.06f, 0.13f, 0.7f), 8);
            var up = new Rect(pr.x, pr.y, pr.width, 22f);
            var down = new Rect(pr.x, pr.yMax - 22f, pr.width, 22f);
            var mid = new Rect(pr.x, pr.y + 22f, pr.width, pr.height - 44f);
            bool hu = HudDraw.Hover(up), hd = HudDraw.Hover(down);
            HudDraw.Arrow(new Rect(up.x + 7f, up.y + 5f, 16f, 12f), true, hu ? Ui.Gold : Ui.TextLight);
            HudDraw.Arrow(new Rect(down.x + 7f, down.y + 5f, 16f, 12f), false, hd ? Ui.Gold : Ui.TextLight);
            HudDraw.Text(mid, HudText.Int(page + 1), HudStyles.TinyCenter, Ui.Gold);
            Ui.Block(pr);
            if (HudDraw.Hover(pr)) Ui.TooltipFor(pr, "Page " + (page + 1) + " / " + pageCount + "\n" + Ui.Rich("Mouse wheel over the bar also turns pages.", Hud.Muted));
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
                HudDraw.Icon(r, string.IsNullOrEmpty(d.icon) ? "glyph_vial" : d.icon, Hud.QualityCol(d.quality), !usable, alpha);
                float left = Hud.CooldownLeft(unit, it.Use);
                if (left > 0.01f)
                {
                    HudDraw.Sweep(r, Mathf.Clamp01(left / Mathf.Max(it.CdMax, left)));
                    HudDraw.Text(new Rect(r.x, r.y + 4f, r.width, r.height - 10f), HudText.Duration(left), Ui.NumberStyle(16), Ui.TextLight);
                }
                if (it.Count > 1) HudDraw.Text(new Rect(r.x, r.yMax - 15f, r.width - 4f, 14f), HudText.Int(it.Count), HudStyles.TinyRight, Ui.TextLight);
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
            string tip = UiText.Ability(unit, sl.A);
            var st = sl.St;
            if (!st.Usable && !string.IsNullOrEmpty(st.Reason)) tip += "\n" + Ui.Rich(st.Reason, Ui.Bad);
            if (inCombat)
            {
                string time = st.TimeCost > 0.01f ? "Costs " + HudText.Secs(st.TimeCost) + " s of this turn's Time" : "No Time cost (off the GCD)";
                if (st.CastTime > 0.01f) time += " · cast " + HudText.Secs(st.CastTime) + " s (becomes pending if it does not fit)";
                tip += "\n" + Ui.Rich(time, Ui.Time);
            }
            if (st.Active) tip += "\n" + Ui.Rich("Active", Ui.Gold);
            tip += "\n" + Ui.Rich("Hotkey " + HudText.Hotkey(index) + " · drag to rearrange · Shift+right-click resets the order", Hud.Muted);
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
                        pressId = s >= 0 ? s : it >= 0 ? 100 + it : -1;
                        pressPos = m;
                        dragging = false;
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
                    if (pressId >= 0 && pressId < 100 && !dragging && (m - pressPos).sqrMagnitude > 64f && start + pressId < slots.Count) dragging = true;
                    break;
                case EventType.MouseUp:
                {
                    if (e.button != 0) break;
                    int id = pressId;
                    bool wasDragging = dragging;
                    pressId = -1;
                    dragging = false;
                    if (id < 0) break;
                    if (id >= 100)
                    {
                        int it = ItemAt(m);
                        if (it == id - 100 && it < items.Count)
                        {
                            var item = items[it];
                            Hud.Post(() => ActivateItem(item, false));
                            Ui.Sfx?.Invoke("ui_click");
                        }
                        break;
                    }
                    int over = SlotAt(m);
                    if (wasDragging)
                    {
                        if (over >= 0 && over != id) Swap(start + id, start + over);
                    }
                    else if (over == id && start + id < slots.Count)
                    {
                        var sl = slots[start + id];
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
