// Vendor window (Session.ActiveVendor, Order 142, left side; the bags open beside it on VendorOpened). It is drawn above the
// sheets (Character, Spellbook, Journal, Map, Party, Talents) so a sheet left open never hides the shop; Esc closes it first.
// Buy: offers with prices (red when unaffordable) and stock, click Buy (Shift: a whole stack). Sell: the party bags with
// sell prices, "Sell junk" in one click; right-click in the bags also sells while a vendor is open. Buy back: the last
// items sold, at the price they fetched. Esc / × closes (Session.CloseVendor).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class VendorScreen : IUiScreen
    {
        public const int ScreenOrder = 142;
        public string Id => "";
        public int Order => ScreenOrder;
        public bool Modal => false;

        public bool Visible
        {
            get
            {
                var s = PanelKit.Sess;
                return s != null && GameFlow.HasGame && s.ActiveVendor != null && s.Mode == SessionMode.Exploration;
            }
        }

        static VendorScreen instance;
        static readonly string[] TabLabels = { "Buy", "Sell", "Buy back" };
        int tab;
        VendorShop lastShop;
        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        readonly List<VendorShop.Offer> offers = new List<VendorShop.Offer>();
        readonly List<string> offerSubs = new List<string>();
        Unit subsFor;
        readonly List<ItemInstance> sellable = new List<ItemInstance>();
        readonly List<ItemInstance> buyback = new List<ItemInstance>();
        readonly Dictionary<ItemDef, ItemInstance> samples = new Dictionary<ItemDef, ItemInstance>();
        float refreshAt;
        int junkValue, junkCount;
        string[] tabLabels = TabLabels;
        string junkTip = "", junkLabel = "";
        int tabCount;

        public VendorScreen()
        {
            instance = this;
            PanelKit.Events += e =>
            {
                if (e.Kind == SessionEventKind.VendorOpened) { tab = 0; scroll.Reset(); UiRoot.Open(UiPanels.Inventory); refreshAt = 0f; }
                else if (e.Kind == SessionEventKind.ItemReceived || e.Kind == SessionEventKind.ItemLost || e.Kind == SessionEventKind.GoldChanged) refreshAt = 0f;
            };
        }

        static VendorScreen()
        {
            EscRouter.Register(290, () =>
            {
                var v = instance;
                if (v == null || !v.Visible) return false;
                PanelKit.Do(() => PanelKit.Sess?.CloseVendor());
                return true;
            }, ScreenOrder);
        }

        public void Tick(float dt)
        {
            if (!Visible) { lastShop = null; return; }
            var shop = PanelKit.Sess.ActiveVendor;
            if (shop != lastShop) { lastShop = shop; tab = 0; scroll.Reset(); refreshAt = 0f; }
            if (PanelKit.Member != subsFor) refreshAt = 0f;
            if (Time.unscaledTime >= refreshAt) Refresh(shop);
        }

        void Refresh(VendorShop shop)
        {
            refreshAt = Time.unscaledTime + 0.5f;
            offers.Clear();
            try { offers.AddRange(shop.Offers()); }
            catch (Exception e) { Debug.LogException(e); }
            var member = PanelKit.Member;
            subsFor = member;
            offerSubs.Clear();
            foreach (var o in offers)
            {
                if (o == null || o.Item == null) { offerSubs.Add(""); continue; }
                string sub = LootScreen.ItemKindText(o.Item);
                if (o.Stock > 0) sub += " · " + o.Stock + " left";
                else if (o.Stock == 0) sub = "Sold out";
                if (member != null && PanelKit.IsEquipment(o.Item))
                {
                    string why = null;
                    try { why = EquipmentRules.CannotUseReason(member, o.Item); } catch (Exception) { }
                    if (why != null) sub = Ui.Rich("Unusable by " + PanelKit.NameOf(member), PanelKit.BadDark);
                }
                offerSubs.Add(sub);
            }
            buyback.Clear();
            if (shop.Buyback != null) buyback.AddRange(shop.Buyback);
            if (buyback.Count != tabCount)
            {
                tabCount = buyback.Count;
                tabLabels = tabCount > 0 ? new[] { "Buy", "Sell", "Buy back (" + tabCount + ")" } : TabLabels;
            }
            sellable.Clear();
            junkValue = junkCount = 0;
            var s = PanelKit.Sess;
            foreach (var it in s.Inventory.Items)
            {
                if (it == null || it.Def == null || it.Def.price <= 0 || PanelKit.IsQuestItem(it.Def)) continue;
                sellable.Add(it);
                if (IsJunk(it.Def)) { junkValue += it.SellPrice * Mathf.Max(1, it.Count); junkCount++; }
            }
            junkTip = "Sells every grey item for " + Inventory.FormatMoney(junkValue);
            junkLabel = junkCount == 1 ? "1 piece of junk" : junkCount + " pieces of junk";
            sellable.Sort((a, b) =>
            {
                bool ja = IsJunk(a.Def), jb = IsJunk(b.Def);
                if (ja != jb) return ja ? -1 : 1;
                return (b.SellPrice * b.Count).CompareTo(a.SellPrice * a.Count);
            });
        }

        /// <summary>Grey (Poor) items the merchant buys — what "Sell junk" sells (trade goods that quests may ask for are kept).</summary>
        public static bool IsJunk(ItemDef d) => d != null && d.quality == Quality.Poor && d.price > 0 && !PanelKit.IsQuestItem(d);

        ItemInstance Sample(ItemDef d)
        {
            if (!samples.TryGetValue(d, out var it)) { it = new ItemInstance(d); samples[d] = it; }
            return it;
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var s = PanelKit.Sess;
            var shop = s.ActiveVendor;
            if (shop == null) return;
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                var r = PanelKit.Fit(new Rect(40f, 70f, 620f, Mathf.Min(880f, Ui.Height - 200f)));
                var npc = shop.Npc;
                var c = PanelKit.Window(r, npc != null ? npc.name : "Merchant", Order, out bool close, npc != null ? npc.title : null);
                if (close) PanelKit.Do(() => PanelKit.Sess?.CloseVendor());
                int nt = PanelKit.Tabs(new Rect(c.x, c.y, c.width, 40f), tabLabels, tab);
                if (nt != tab) { tab = nt; scroll.Reset(); }
                var list = new Rect(c.x, c.y + 54f, c.width, c.height - 54f - 60f);
                if (tab == 1 && junkCount > 0) list.yMin += 50f;
                switch (tab)
                {
                    case 0: DrawBuy(list, s); break;
                    case 1: DrawSell(new Rect(c.x, c.y + 54f, c.width, 44f), list, s); break;
                    default: DrawBuyback(list, s); break;
                }
                // footer: purse
                PanelKit.HLine(c.x, c.yMax - 52f, c.width);
                PanelKit.Label(new Rect(c.x, c.yMax - 42f, 200f, 36f), "Your purse", PanelKit.TextMuted);
                PanelKit.MoneyPlate(new Rect(c.x, c.yMax - 42f, c.width, 36f), s.Gold);
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }

        void DrawBuy(Rect list, GameSession s)
        {
            const float rowH = 66f;
            var member = PanelKit.Member;
            float cw = PanelKit.BeginScroll(list, scroll, offers.Count * rowH);
            try
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    var o = offers[i];
                    var row = new Rect(0f, i * rowH, cw, rowH - 6f);
                    if (!scroll.IsVisible(row) || o == null || o.Item == null) continue;
                    bool soldOut = o.Stock == 0;
                    bool afford = s.Gold >= o.Price;
                    bool hover = PanelKit.Hover(row);
                    PanelKit.Rounded(row, hover ? PanelKit.RowHover : PanelKit.RowShade);
                    var it = Sample(o.Item);
                    PanelKit.QualityRowMarks(row, it.Def != null ? it.Def.quality : Quality.Common, row.y + 9f, false);
                    PanelKit.ItemIcon(new Rect(row.x + 6f, row.y + 5f, 50f, 50f), it, soldOut || !afford, hover);
                    float nameW = row.width - 68f - 250f;
                    PanelKit.Label(new Rect(row.x + 68f, row.y + 6f, nameW, 26f), o.Item.name, PanelKit.RowText, PanelKit.QualityInk(o.Item.quality));
                    string sub = i < offerSubs.Count ? offerSubs[i] : "";
                    PanelKit.Label(new Rect(row.x + 68f, row.y + 32f, nameW, 22f), sub, PanelKit.RowTextSmall);
                    var price = new Rect(row.xMax - 250f, row.y + 14f, 150f, 32f);
                    if (!afford) PanelKit.Rounded(new Rect(price.xMax - 4f, price.y + 4f, 4f, price.height - 8f), PanelKit.BadDark);
                    PanelKit.MoneyPlate(price, o.Price);
                    bool canBuy = !soldOut && afford;
                    string tip = soldOut ? "Sold out." : !afford ? "Not enough money." : (it.MaxStack > 1 ? "Shift-click: buy a stack" : null);
                    if (PanelKit.Btn(new Rect(row.xMax - 90f, row.y + 10f, 84f, 40f), "Buy", PanelKit.SmallButtonGold, canBuy, tip))
                        Buy(o, it.MaxStack > 1 && Event.current.shift);
                    if (hover) Ui.TooltipFor(new Rect(row.x, row.y, row.width - 260f, row.height), PanelKit.ItemTip(it, member, true));
                }
            }
            finally { PanelKit.EndScroll(scroll); }
            if (offers.Count == 0) PanelKit.Label(list, "Nothing for sale today.", PanelKit.TextCenter);
        }

        void Buy(VendorShop.Offer o, bool stack)
        {
            var s = PanelKit.Sess;
            int count = 1;
            if (stack)
            {
                count = Mathf.Max(1, o.Item.stack);
                if (o.Price > 0) count = Mathf.Min(count, s.Gold / o.Price);
                if (o.Stock >= 0) count = Mathf.Min(count, o.Stock);
                count = Mathf.Max(1, count);
            }
            string id = o.Item.id;
            int n = count;
            PanelKit.Do(() =>
            {
                if (PanelKit.Try(() => PanelKit.Sess != null ? PanelKit.Sess.Buy(id, n) : "No merchant.")) Ui.Sfx?.Invoke("coin");
                refreshAt = 0f;
            });
        }

        void DrawSell(Rect header, Rect list, GameSession s)
        {
            if (junkCount > 0)
            {
                PanelKit.Label(new Rect(header.x, header.y + 6f, header.width - 200f, 30f), junkLabel, PanelKit.TextMuted);
                if (Ui.Btn(new Rect(header.xMax - 190f, header.y, 190f, 40f), "Sell junk", PanelKit.SmallButtonGold, true, junkTip))
                    PanelKit.Do(SellJunk);
            }
            const float rowH = 66f;
            var member = PanelKit.Member;
            float cw = PanelKit.BeginScroll(list, scroll, sellable.Count * rowH);
            try
            {
                for (int i = 0; i < sellable.Count; i++)
                {
                    var it = sellable[i];
                    var row = new Rect(0f, i * rowH, cw, rowH - 6f);
                    if (!scroll.IsVisible(row) || it == null || it.Def == null) continue;
                    bool hover = PanelKit.Hover(row);
                    PanelKit.Rounded(row, hover ? PanelKit.RowHover : PanelKit.RowShade);
                    PanelKit.QualityRowMarks(row, it.Def != null ? it.Def.quality : Quality.Common, row.y + 9f, false);
                    PanelKit.ItemIcon(new Rect(row.x + 6f, row.y + 5f, 50f, 50f), it, false, hover);
                    float nameW = row.width - 68f - 250f;
                    PanelKit.Label(new Rect(row.x + 68f, row.y + 6f, nameW, 26f), it.Name, PanelKit.RowText, PanelKit.QualityInk(it.Def.quality));
                    PanelKit.Label(new Rect(row.x + 68f, row.y + 32f, nameW, 22f), LootScreen.ItemKindText(it.Def), PanelKit.RowTextSmall);
                    PanelKit.MoneyPlate(new Rect(row.xMax - 250f, row.y + 14f, 150f, 32f), (long)it.SellPrice * Mathf.Max(1, it.Count));
                    string tip = it.Count > 1 ? "Sells the whole stack (Shift-click: one)" : null;
                    if (PanelKit.Btn(new Rect(row.xMax - 90f, row.y + 10f, 84f, 40f), "Sell", PanelKit.SmallButton, true, tip))
                    {
                        var sell = it;
                        int n = it.Count > 1 && Event.current.shift ? 1 : it.Count;
                        if (sell.Def.quality >= Quality.Rare)
                            ConfirmScreen.Ask("Sell " + sell.Name + "?", "This is a " + sell.Def.quality.ToString().ToLowerInvariant() + " item. You can buy it back for a while.", "Sell", () => SellItem(sell, n));
                        else PanelKit.Do(() => SellItem(sell, n));
                    }
                    if (hover) Ui.TooltipFor(new Rect(row.x, row.y, row.width - 260f, row.height), PanelKit.ItemTip(it, member, true));
                }
            }
            finally { PanelKit.EndScroll(scroll); }
            if (sellable.Count == 0) PanelKit.Label(list, "You have nothing the merchant wants.", PanelKit.TextCenter);
        }

        public static void SellItem(ItemInstance it, int count)
        {
            var s = PanelKit.Sess;
            if (s == null || it == null) return;
            if (PanelKit.Try(() => s.Sell(it, Mathf.Max(1, count)))) Ui.Sfx?.Invoke("coin");
            if (instance != null) instance.refreshAt = 0f;
        }

        void SellJunk()
        {
            var s = PanelKit.Sess;
            if (s == null || s.ActiveVendor == null) return;
            var junk = new List<ItemInstance>();
            foreach (var it in s.Inventory.Items) if (it != null && IsJunk(it.Def)) junk.Add(it);
            int sold = 0;
            foreach (var it in junk) if (s.Sell(it, it.Count) == null) sold++;
            if (sold > 0) { Ui.Sfx?.Invoke("coin"); PanelKit.Notice(sold == 1 ? "Sold 1 piece of junk." : $"Sold {sold} pieces of junk.", false); }
            refreshAt = 0f;
        }

        void DrawBuyback(Rect list, GameSession s)
        {
            const float rowH = 66f;
            var member = PanelKit.Member;
            float cw = PanelKit.BeginScroll(list, scroll, buyback.Count * rowH);
            try
            {
                for (int i = 0; i < buyback.Count; i++)
                {
                    var it = buyback[i];
                    var row = new Rect(0f, i * rowH, cw, rowH - 6f);
                    if (!scroll.IsVisible(row) || it == null || it.Def == null) continue;
                    long cost = (long)it.SellPrice * Mathf.Max(1, it.Count);
                    bool afford = s.Gold >= cost;
                    bool hover = PanelKit.Hover(row);
                    PanelKit.Rounded(row, hover ? PanelKit.RowHover : PanelKit.RowShade);
                    PanelKit.QualityRowMarks(row, it.Def != null ? it.Def.quality : Quality.Common, row.y + 9f, false);
                    PanelKit.ItemIcon(new Rect(row.x + 6f, row.y + 5f, 50f, 50f), it, !afford, hover);
                    float nameW = row.width - 68f - 250f;
                    PanelKit.Label(new Rect(row.x + 68f, row.y + 6f, nameW, 26f), it.Name, PanelKit.RowText, PanelKit.QualityInk(it.Def.quality));
                    PanelKit.Label(new Rect(row.x + 68f, row.y + 32f, nameW, 22f), "Sold earlier", PanelKit.RowTextSmall);
                    PanelKit.MoneyPlate(new Rect(row.xMax - 250f, row.y + 14f, 150f, 32f), cost);
                    if (PanelKit.Btn(new Rect(row.xMax - 90f, row.y + 10f, 84f, 40f), "Buy", PanelKit.SmallButtonGold, afford, afford ? null : "Not enough money."))
                    {
                        var back = it;
                        PanelKit.Do(() => { if (PanelKit.Try(() => PanelKit.Sess != null ? PanelKit.Sess.BuyBack(back) : "No merchant.")) Ui.Sfx?.Invoke("coin"); refreshAt = 0f; });
                    }
                    if (hover) Ui.TooltipFor(new Rect(row.x, row.y, row.width - 260f, row.height), PanelKit.ItemTip(it, member, true));
                }
            }
            finally { PanelKit.EndScroll(scroll); }
            if (buyback.Count == 0) PanelKit.Label(list, "Items you sell can be bought back here for a while.", PanelKit.TextCenter);
        }
    }
}
