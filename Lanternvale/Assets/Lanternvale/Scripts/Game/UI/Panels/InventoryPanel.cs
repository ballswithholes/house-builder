// Bags (I / B): the shared party inventory as a grid with quality borders and stack counts; filters (all / gear /
// consumables / quest / junk) and sorting; tooltips compare with the selected member's equipped item. Left click equips
// on the selected member (or uses a consumable); right click opens a menu: use, equip on any member, sell (vendor open),
// destroy (with confirmation). Gold total at the bottom.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class InventoryPanel : PanelWindow
    {
        public override string Id => UiPanels.Inventory;
        public override int Order => 126;

        static readonly string[] FilterLabels = { "All", "Gear", "Consumables", "Quest", "Junk" };
        static readonly string[] SortLabels = { "Sort: Bag order", "Sort: Type", "Sort: Quality", "Sort: Name" };

        int filter, sortMode = 1;
        readonly List<ItemInstance> view = new List<ItemInstance>();
        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        int stamp = -1;
        int lockFrame;
        Unit nameFor;
        string nameText = "", countText = "";
        int countFor = -1;

        void Rebuild(GameSession s)
        {
            int st = filter * 7 + sortMode * 131 + s.Inventory.Items.Count * 1009;
            foreach (var it in s.Inventory.Items) if (it != null) st = st * 31 + it.Count + (int)(it.Uid & 0xffff);
            if (st == stamp) return;
            stamp = st;
            view.Clear();
            foreach (var it in s.Inventory.Items)
                if (it != null && it.Def != null && Matches(it.Def)) view.Add(it);
            switch (sortMode)
            {
                case 1: view.Sort(CompareType); break;
                case 2: view.Sort((a, b) => a.Def.quality != b.Def.quality ? b.Def.quality.CompareTo(a.Def.quality) : CompareType(a, b)); break;
                case 3: view.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); break;
            }
        }

        bool Matches(ItemDef d)
        {
            switch (filter)
            {
                case 1: return PanelKit.IsEquipment(d);
                case 2: return !PanelKit.IsEquipment(d) && (PanelKit.IsUsable(d) || d.kind == ItemKind.Consumable || d.kind == ItemKind.Food || d.kind == ItemKind.Drink || d.kind == ItemKind.Reagent || d.kind == ItemKind.Ammo);
                case 3: return PanelKit.IsQuestItem(d);
                case 4: return VendorScreen.IsJunk(d) || d.kind == ItemKind.Junk;
                default: return true;
            }
        }

        static int TypeRank(ItemDef d)
        {
            if (PanelKit.IsQuestItem(d)) return 0;
            if (PanelKit.IsEquipment(d)) return 1;
            if (PanelKit.IsUsable(d) || d.kind == ItemKind.Consumable || d.kind == ItemKind.Food || d.kind == ItemKind.Drink) return 2;
            if (d.kind == ItemKind.Reagent || d.kind == ItemKind.Ammo) return 3;
            if (d.quality == Quality.Poor || d.kind == ItemKind.Junk) return 5;
            return 4;
        }

        static int CompareType(ItemInstance a, ItemInstance b)
        {
            int ta = TypeRank(a.Def), tb = TypeRank(b.Def);
            if (ta != tb) return ta.CompareTo(tb);
            if (a.Def.equip != b.Def.equip) return a.Def.equip.CompareTo(b.Def.equip);
            if (a.Def.quality != b.Def.quality) return b.Def.quality.CompareTo(a.Def.quality);
            return string.CompareOrdinal(a.Name, b.Name);
        }

        protected override void DrawPanel()
        {
            var s = PanelKit.Sess;
            Rebuild(s);
            var u = PanelKit.Member;
            float w = 620f;
            var r = PanelKit.Fit(new Rect(Ui.Width - w - 40f, 66f, w, Mathf.Min(900f, Ui.Height - 160f)));
            var c = Chrome(r, "Bags");
            // who equips
            PanelKit.Label(new Rect(c.x, c.y + 8f, 110f, 30f), "Equip on", PanelKit.TextMuted);
            var nm = PanelKit.MemberTabs(new Rect(c.x + 96f, c.y - 2f, c.width - 96f, 44f), u, PanelKit.Characters, 42f);
            if (nm != u) { PanelKit.Member = nm; u = nm; }
            if (u != null)
            {
                if (nameFor != u) { nameFor = u; nameText = "<b>" + PanelKit.NameOf(u) + "</b>"; }
                PanelKit.Label(new Rect(c.x + 96f + PanelKit.Characters.Count * 52f + 6f, c.y + 8f, 220f, 30f), nameText, PanelKit.Text);
            }
            float y = c.y + 52f;
            int nf = PanelKit.Tabs(new Rect(c.x, y, c.width, 38f), FilterLabels, filter, 70f);
            if (nf != filter) { filter = nf; scroll.Reset(); stamp = -1; }
            y += 48f;
            if (PanelKit.SmallBtn(new Rect(c.xMax - 190f, c.yMax - 46f, 190f, 40f), SortLabels[sortMode], true, "Change the sort order"))
            {
                sortMode = (sortMode + 1) % SortLabels.Length;
                stamp = -1;
            }
            // grid
            const float slot = 60f, gap = 8f;
            var grid = new Rect(c.x, y, c.width, c.yMax - y - 60f);
            int cols = Mathf.Max(1, Mathf.FloorToInt((grid.width - 16f + gap) / (slot + gap)));
            int shown = Mathf.Max(view.Count + (filter == 0 ? cols : 0), cols * 5);
            shown = Mathf.CeilToInt(shown / (float)cols) * cols;
            int rows = shown / cols;
            float cw = PanelKit.BeginScroll(grid, scroll, rows * (slot + gap));
            Vector2 menuAt = Vector2.zero;
            ItemInstance menuItem = null;
            try
            {
                float ox = Mathf.Max(0f, (cw - (cols * (slot + gap) - gap)) * 0.5f);
                for (int i = 0; i < shown; i++)
                {
                    var sr = new Rect(ox + (i % cols) * (slot + gap), (i / cols) * (slot + gap), slot, slot);
                    if (!scroll.IsVisible(sr)) continue;
                    if (i >= view.Count) { PanelKit.EmptySlot(sr, null); continue; }
                    var it = view[i];
                    bool hover = PanelKit.Hover(sr);
                    bool unusable = u != null && PanelKit.IsEquipment(it.Def) && Unusable(u, it.Def);
                    PanelKit.ItemIcon(sr, it, unusable, hover);
                    if (unusable && PanelKit.IsRepaint) PanelKit.Rect(new Rect(sr.x + 4f, sr.y + 4f, 8f, 8f), PanelKit.BadDark);
                    if (hover) Ui.TooltipFor(sr, PanelKit.ItemTip(it, u, true, HintOf(it, u, s)));
                    if (PanelKit.Click(sr, out int button) && Time.frameCount > lockFrame)
                    {
                        lockFrame = Time.frameCount + 1;
                        if (button == 1) { menuItem = it; menuAt = Event.current.mousePosition - scroll.Pos + new Vector2(grid.x, grid.y); }
                        else if (button == 0) Primary(it, u);
                    }
                }
            }
            finally { PanelKit.EndScroll(scroll); }
            if (menuItem != null) OpenMenu(menuItem, u, s, menuAt);
            // footer
            PanelKit.HLine(c.x, c.yMax - 54f, c.width - 200f);
            if (countFor != view.Count) { countFor = view.Count; countText = view.Count == 1 ? "1 item" : view.Count + " items"; }
            PanelKit.Label(new Rect(c.x, c.yMax - 46f, 120f, 40f), countText, PanelKit.TextMuted);
            PanelKit.MoneyPlate(new Rect(c.x, c.yMax - 44f, c.width - 210f, 36f), s.Gold);
        }

        static bool Unusable(Unit u, ItemDef d)
        {
            try { return EquipmentRules.CannotUseReason(u, d) != null; }
            catch (Exception) { return false; }
        }

        static string HintOf(ItemInstance it, Unit u, GameSession s)
        {
            var d = it.Def;
            string sell = s.ActiveVendor != null && d.price > 0 && !PanelKit.IsQuestItem(d) ? "  ·  Right-click: sell / more" : "  ·  Right-click: more";
            if (PanelKit.IsEquipment(d) && u != null) return Ui.Rich("Click: equip on " + PanelKit.NameOf(u) + sell, Ui.TextMuted);
            if (PanelKit.IsUsable(d)) return Ui.Rich("Click: use" + sell, Ui.TextMuted);
            return Ui.Rich(sell.Substring(5), Ui.TextMuted);
        }

        /// <summary>Left click: equip on the selected member, or use.</summary>
        void Primary(ItemInstance it, Unit u)
        {
            var d = it.Def;
            if (PanelKit.IsEquipment(d))
            {
                if (u == null) return;
                PanelKit.Do(() => Equip(it, u));
            }
            else if (PanelKit.IsUsable(d)) PanelKit.Do(() => Use(it, u));
            else if (PanelKit.IsQuestItem(d)) PanelKit.Notice("A quest item. Keep it safe.", false);
        }

        static void Equip(ItemInstance it, Unit u)
        {
            var s = PanelKit.Sess;
            if (s == null || u == null) return;
            if (PanelKit.Try(() => s.Equip(u, it))) Ui.Sfx?.Invoke("ui_open");
        }

        static void Use(ItemInstance it, Unit u)
        {
            var f = PanelKit.Flow;
            var s = PanelKit.Sess;
            if (f == null || s == null) return;
            if (s.Mode == SessionMode.Combat)
            {
                var c = f.Combat;
                if (c == null) { PanelKit.Notice("Not now."); return; }
                PanelKit.Try(() => c.BeginItem(it));
                return;
            }
            var user = u ?? s.Leader;
            PanelKit.Try(() => f.UseItemOutOfCombat(user, it));
        }

        void OpenMenu(ItemInstance it, Unit u, GameSession s, Vector2 at)
        {
            var d = it.Def;
            var items = new List<ContextMenuScreen.Item>();
            if (PanelKit.IsUsable(d))
                items.Add(new ContextMenuScreen.Item { Label = "Use", Enabled = true, Action = () => Use(it, u) });
            if (PanelKit.IsEquipment(d))
                foreach (var m in s.Party)
                {
                    if (m == null || m.Class == null) continue;
                    string why = null;
                    try { why = s.CanEquip(m, it); } catch (Exception e) { why = e.Message; }
                    var who = m;
                    items.Add(new ContextMenuScreen.Item { Label = "Equip on " + PanelKit.NameOf(m), Enabled = why == null, Tip = why, Action = () => Equip(it, who) });
                }
            if (s.ActiveVendor != null)
            {
                bool can = d.price > 0 && !PanelKit.IsQuestItem(d);
                string price = can ? " (" + Inventory.FormatMoney(it.SellPrice * Mathf.Max(1, it.Count)) + ")" : "";
                items.Add(new ContextMenuScreen.Item { Label = "Sell" + price, Enabled = can, Tip = can ? null : "The merchant does not want that.", Action = () => VendorScreen.SellItem(it, it.Count) });
                if (can && it.Count > 1)
                    items.Add(new ContextMenuScreen.Item { Label = "Sell one", Enabled = true, Action = () => VendorScreen.SellItem(it, 1) });
            }
            bool quest = PanelKit.IsQuestItem(d);
            items.Add(new ContextMenuScreen.Item { Separator = true });
            items.Add(new ContextMenuScreen.Item
            {
                Label = "Destroy…", Enabled = !quest, Tip = quest ? "Quest items cannot be destroyed." : null,
                Action = () => ConfirmScreen.Ask("Destroy " + it.Name + "?", it.Count > 1 ? $"All {it.Count} will be gone for good." : "It will be gone for good.", "Destroy", () =>
                {
                    var ss = PanelKit.Sess;
                    if (ss != null) PanelKit.Try(() => ss.DestroyItem(it, it.Count), "Destroyed " + it.Name + ".");
                }, "Keep it", null, true),
            });
            items.Add(new ContextMenuScreen.Item { Label = "Cancel", Enabled = true, Action = null });
            ContextMenuScreen.Open(at, it.Name, items);
        }
    }
}
