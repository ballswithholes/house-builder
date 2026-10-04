// Loot window (Session.PendingLoot, Order 150): items with quality colours and comparison tooltips, click to take one,
// Take All (Space / E / Esc), coins already added to the purse. Closing with items left asks first (they are lost).
// Quest reward choice (Order 205, modal): QuestRewardChoice → pick one of QuestRewardChoices → ClaimQuestReward;
// "Decide later" hides it until the next reward event or the Journal's "Choose reward" button.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class LootScreen : IUiScreen
    {
        public const int ScreenOrder = 150;
        public string Id => "";
        public int Order => ScreenOrder;
        public bool Modal => false;

        public bool Visible
        {
            get
            {
                var s = PanelKit.Sess;
                return s != null && GameFlow.HasGame && s.PendingLoot != null && s.Mode == SessionMode.Exploration;
            }
        }

        static LootScreen instance;
        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        readonly List<ItemInstance> items = new List<ItemInstance>();
        LootWindow lastWindow;
        float openedAt;
        int lockFrame;

        public LootScreen() { instance = this; }

        static Rect windowRect;
        static int windowFrame = -1;

        /// <summary>The loot window's rect while it is drawn this frame (screens above it step aside), else false.</summary>
        internal static bool TryGetWindowRect(out Rect r)
        {
            r = windowRect;
            return windowFrame == Time.frameCount && instance != null && instance.Visible;
        }

        static LootScreen()
        {
            EscRouter.Register(400, () =>
            {
                var l = instance;
                if (l == null || !l.Visible) return false;
                l.TakeAll();
                return true;
            }, ScreenOrder);
        }

        public void Tick(float dt)
        {
            if (!Visible) { lastWindow = null; return; }
            var w = PanelKit.Sess.PendingLoot;
            if (w != lastWindow)
            {
                lastWindow = w;
                openedAt = Time.unscaledTime;
                // a chest clicked in range opened in this frame's Update (legacy input): its MouseDown still reaches
                // this frame's OnGUI over the new window, where it must not take an item or arm a button
                lockFrame = Mathf.Max(lockFrame, Time.frameCount + 1);
                scroll.Reset();
                Ui.Sfx?.Invoke("ui_open");
            }
            if (ConfirmScreen.IsOpen || UiRoot.ModalActive || Time.frameCount <= lockFrame) return;
            if (GameInput.KeyDown(KeyCode.Space) || GameInput.KeyDown(KeyCode.E)) TakeAll();
        }

        void TakeAll()
        {
            lockFrame = Time.frameCount + 2;
            Ui.Sfx?.Invoke("coin");
            PanelKit.Do(() => { var s = PanelKit.Sess; if (s != null && s.PendingLoot != null) s.TakeAllLoot(); });
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var s = PanelKit.Sess;
            var w = s.PendingLoot;
            if (w == null) return;
            items.Clear();
            items.AddRange(w.Items);
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                const float rowH = 66f;
                float listH = Mathf.Min(6, Mathf.Max(1, items.Count)) * rowH;
                float h = 74f + (w.Gold > 0 ? 44f : 0f) + listH + 92f;
                float x = Mathf.Min(Ui.Width * 0.5f + 140f, Ui.Width - 500f);
                float pop = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.2f);
                var r = PanelKit.Fit(new Rect(x, Ui.Height * 0.2f + (1f - pop) * 16f, 470f, h));
                windowRect = r;
                windowFrame = Time.frameCount;
                // locked: no click on the window counts, and none arms Take All / Close (they fire on the release)
                if (Time.frameCount <= lockFrame) PanelKit.SwallowMouseDown(r);
                string title = string.IsNullOrEmpty(w.Title) ? "Spoils" : w.Title;
                var c = PanelKit.Window(r, title, Order, out bool close);
                float y = c.y;
                if (w.Gold > 0)
                {
                    PanelKit.Label(new Rect(c.x, y + 6f, 200f, 30f), "Coins (in your purse)", PanelKit.TextMuted);
                    PanelKit.MoneyPlate(new Rect(c.x, y + 4f, c.width, 32f), w.Gold);
                    y += 44f;
                }
                var member = PanelKit.Member;
                var listR = new Rect(c.x, y, c.width, listH);
                float cw = PanelKit.BeginScroll(listR, scroll, items.Count * rowH);
                try
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        var row = new Rect(0f, i * rowH, cw, rowH - 6f);
                        if (!scroll.IsVisible(row) || it == null || it.Def == null) continue;
                        bool hover = PanelKit.Hover(row);
                        PanelKit.Rounded(row, hover ? PanelKit.RowHover : PanelKit.RowShade);
                        PanelKit.ItemIcon(new Rect(row.x + 6f, row.y + 5f, 50f, 50f), it, false, hover);
                        PanelKit.Label(new Rect(row.x + 68f, row.y + 6f, row.width - 76f, 26f), it.Name, PanelKit.RowText, PanelKit.QualityInk(it.Def.quality));
                        PanelKit.Label(new Rect(row.x + 68f, row.y + 32f, row.width - 76f, 22f), ItemKindText(it.Def), PanelKit.RowTextSmall);
                        if (hover) Ui.TooltipFor(row, PanelKit.ItemTip(it, member, true, Ui.Rich("Click to take", Ui.Good)));
                        if (PanelKit.LeftClick(row) && Time.frameCount > lockFrame)
                        {
                            var take = it;
                            lockFrame = Time.frameCount + 1;
                            Ui.Sfx?.Invoke("ui_click");
                            PanelKit.Do(() => PanelKit.Try(() => PanelKit.Sess != null ? PanelKit.Sess.TakeLoot(take) : null));
                        }
                    }
                }
                finally { PanelKit.EndScroll(scroll); }
                if (items.Count == 0) PanelKit.Label(listR, "Nothing else here.", PanelKit.TextCenter);

                float by = r.yMax - 66f;
                if (Ui.Btn(new Rect(c.x, by, 220f, 48f), "Take All", Ui.ButtonGold, items.Count > 0, "Space / E")) TakeAll();
                if (Ui.Btn(new Rect(c.xMax - 150f, by, 150f, 48f), "Close") || close) CloseWindow(items.Count);
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }

        static void CloseWindow(int left)
        {
            if (left <= 0) { PanelKit.Do(() => PanelKit.Sess?.CloseLoot(false)); return; }
            ConfirmScreen.Ask("Leave the loot?", left == 1 ? "One item is left behind. It will be lost." : $"{left} items are left behind. They will be lost.",
                "Leave them", () => PanelKit.Sess?.CloseLoot(false), "Take all", () => PanelKit.Sess?.TakeAllLoot());
        }

        static readonly Dictionary<ItemDef, string> kindTexts = new Dictionary<ItemDef, string>();

        /// <summary>"Two-Hand · Sword", "Consumable", "Quest item"… (cached per item definition).</summary>
        public static string ItemKindText(ItemDef d)
        {
            if (d == null) return "";
            if (kindTexts.TryGetValue(d, out var t)) return t;
            if (kindTexts.Count > 512) kindTexts.Clear();
            t = BuildKindText(d);
            kindTexts[d] = t;
            return t;
        }

        static string BuildKindText(ItemDef d)
        {
            if (d.equip != EquipType.None)
            {
                string slot = UiText.SlotName(d.equip);
                string type = d.weaponType != WeaponType.None ? UiText.Spaced(d.weaponType.ToString()) : d.armorType != ArmorType.None ? d.armorType.ToString() : "";
                return type.Length > 0 ? slot + " · " + type : slot;
            }
            if (PanelKit.IsQuestItem(d)) return "Quest item";
            switch (d.kind)
            {
                case ItemKind.Food: return "Food";
                case ItemKind.Drink: return "Drink";
                case ItemKind.Consumable: return "Consumable";
                case ItemKind.Reagent: return "Reagent";
                case ItemKind.Junk: return d.quality == Quality.Poor ? "Junk · sell it" : "Trade goods";
                default: return d.kind.ToString();
            }
        }
    }

    // ==================================================================================== quest reward

    public sealed class QuestRewardScreen : IUiScreen
    {
        public string Id => "";
        public int Order => 205;
        public bool Modal => Visible;

        static readonly HashSet<string> deferred = new HashSet<string>();
        string questId = "";
        string picked = "";
        readonly List<ItemDef> choices = new List<ItemDef>();
        readonly Dictionary<ItemDef, ItemInstance> samples = new Dictionary<ItemDef, ItemInstance>();
        float shownAt;

        public QuestRewardScreen()
        {
            PanelKit.Events += e =>
            {
                if (e.Kind == SessionEventKind.QuestRewardChoice && !string.IsNullOrEmpty(e.Id)) deferred.Remove(e.Id);
                if (e.Kind == SessionEventKind.GameStarted || e.Kind == SessionEventKind.GameLoaded) deferred.Clear();
            };
        }

        /// <summary>Shows the pick-one dialog for a quest whose reward choice is pending (Journal button).</summary>
        public static void Show(string quest)
        {
            if (!string.IsNullOrEmpty(quest)) deferred.Remove(quest);
        }

        public bool Visible
        {
            get
            {
                var s = PanelKit.Sess;
                if (s == null || !GameFlow.HasGame || s.Mode != SessionMode.Exploration) return false;
                var pending = s.PendingQuestRewards;
                if (pending == null) return false;
                for (int i = 0; i < pending.Count; i++) if (!deferred.Contains(pending[i])) return true;
                return false;
            }
        }

        public void Tick(float dt)
        {
            if (!Visible) { questId = ""; return; }
            var s = PanelKit.Sess;
            string q = "";
            foreach (var id in s.PendingQuestRewards) if (!deferred.Contains(id)) { q = id; break; }
            if (q != questId)
            {
                questId = q;
                picked = "";
                shownAt = Time.unscaledTime;
                choices.Clear();
                try { choices.AddRange(s.QuestRewardChoices(q)); }
                catch (Exception e) { Debug.LogException(e); }
                Ui.Sfx?.Invoke("quest");
            }
            if (!ConfirmScreen.IsOpen && picked.Length > 0 && PanelKit.ConfirmKeyDown()) Claim();
        }

        ItemInstance Sample(ItemDef d)
        {
            if (!samples.TryGetValue(d, out var it)) { it = new ItemInstance(d); samples[d] = it; }
            return it;
        }

        void Claim()
        {
            if (picked.Length == 0 || questId.Length == 0) return;
            string q = questId, item = picked;
            picked = "";
            PanelKit.Do(() =>
            {
                var s = PanelKit.Sess;
                if (s == null) return;
                if (PanelKit.Try(() => s.ClaimQuestReward(q, item))) Ui.Sfx?.Invoke("quest");
            });
        }

        public void Draw()
        {
            if (questId.Length == 0) return;
            PanelKit.EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                PanelKit.OccludeAll(Order);
                Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
                PanelKit.Rect(new Rect(0f, 0f, Ui.Width, Ui.Height), new Color(0.06f, 0.04f, 0.10f, 0.35f));
                var db = PanelKit.Db;
                var qd = db != null && db.Quests.TryGetValue(questId, out var qq) ? qq : null;
                int n = Mathf.Max(1, choices.Count);
                const float card = 210f, gap = 18f;
                float w = Mathf.Max(560f, n * card + (n - 1) * gap + 70f);
                float pop = Mathf.Clamp01((Time.unscaledTime - shownAt) / 0.25f);
                var r = PanelKit.Centered(w, 470f, (1f - pop) * 20f);
                var c = PanelKit.Frame(r, "Choose your reward", Order);
                PanelKit.Label(new Rect(c.x, c.y, c.width, 28f), qd != null ? "Quest complete: <b>" + qd.name + "</b>" : "Quest complete", PanelKit.TextCenter);
                float x0 = r.center.x - (n * card + (n - 1) * gap) * 0.5f;
                var member = PanelKit.Member;
                for (int i = 0; i < choices.Count; i++)
                {
                    var d = choices[i];
                    var cr = new Rect(x0 + i * (card + gap), c.y + 44f, card, 250f);
                    bool sel = picked == d.id;
                    bool hover = PanelKit.Hover(cr);
                    if (sel) PanelKit.Tex(new Rect(cr.x - 16f, cr.y - 16f, cr.width + 32f, cr.height + 32f), ProceduralArt.Glow, new Color(1f, 0.8f, 0.4f, 0.8f));
                    PanelKit.Rounded(cr, sel ? PanelKit.RowSelected : hover ? PanelKit.RowHover : PanelKit.RowShade);
                    if (sel) PanelKit.Outline(cr, Ui.GoldDeep);
                    var it = Sample(d);
                    PanelKit.ItemIcon(new Rect(cr.center.x - 40f, cr.y + 22f, 80f, 80f), it, false, hover || sel);
                    PanelKit.Label(new Rect(cr.x + 10f, cr.y + 114f, cr.width - 20f, 56f), d.name, PanelKit.TextSmallCenter, PanelKit.QualityInk(d.quality));
                    PanelKit.Label(new Rect(cr.x + 10f, cr.y + 172f, cr.width - 20f, 44f), LootScreen.ItemKindText(d), PanelKit.TextSmallCenter, Ui.InkSoft);
                    if (hover) Ui.TooltipFor(cr, PanelKit.ItemTip(it, member, true));   // built only for the hovered card
                    if (PanelKit.LeftClick(cr))
                    {
                        picked = d.id;
                        Ui.Sfx?.Invoke("ui_click");
                    }
                }
                if (choices.Count == 0) PanelKit.Label(new Rect(c.x, c.y + 120f, c.width, 40f), "There is nothing to choose.", PanelKit.TextCenter);
                float by = r.yMax - 68f;
                if (Ui.Btn(new Rect(r.center.x - 230f, by, 220f, 50f), "Claim reward", Ui.ButtonGold, picked.Length > 0, picked.Length > 0 ? "Enter" : "Pick one first")) Claim();
                if (Ui.Btn(new Rect(r.center.x + 10f, by, 220f, 50f), "Decide later", Ui.Button, true, "You can choose later from the Journal (J)."))
                {
                    deferred.Add(questId);
                    questId = "";
                }
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }
    }
}
