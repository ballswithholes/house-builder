// Journal (J): quests on the left (active — main quests first —, completed, failed; collapsible), the selected quest on
// the right: giver, level and zone band, summary, the current stage, objectives with progress and ticks, "» Return to
// <NPC>" when the step can be handed in now (QuestTrackerHud.ReturnLine), earlier stages, rewards (XP at the party
// level, coins, items with tooltips, pick-one choices) and "Choose your reward" when a choice is pending.
// A button opens the Party & camp window.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class JournalPanel : PanelWindow
    {
        public override string Id => UiPanels.Journal;
        public override int Order => 130;

        List<QuestJournalEntry> entries = new List<QuestJournalEntry>();
        float refreshAt;
        string selectedId = "";
        bool showCompleted = true, showFailed;
        int xpShown = -1;
        string xpText = "";
        readonly PanelKit.ScrollState listScroll = new PanelKit.ScrollState();
        readonly PanelKit.ScrollState detailScroll = new PanelKit.ScrollState();
        readonly Dictionary<string, ItemInstance> samples = new Dictionary<string, ItemInstance>();
        // height of the detail column as the last draw of that quest laid it out (wrapped reward rows, the choice
        // button): the scroll range comes from it, so the bottom of a long quest stays reachable. EstimateDetail
        // sizes only the first draw after a selection.
        string detailMeasuredFor = "";
        float detailMeasuredH;

        // list rows (rebuilt with the entries)
        struct ListRow
        {
            public int Kind;   // 0 header, 1 quest
            public string Text;
            public QuestJournalEntry Entry;
            public int Section;
        }

        readonly List<ListRow> listRows = new List<ListRow>();

        sealed class EntryText
        {
            public string Sub = "", Level = "", Meta = "", Return = "";
        }

        readonly Dictionary<QuestJournalEntry, EntryText> texts = new Dictionary<QuestJournalEntry, EntryText>();

        EntryText TextOf(QuestJournalEntry q)
        {
            if (texts.TryGetValue(q, out var t)) return t;
            t = new EntryText
            {
                Level = "Lv " + q.Level,
                Sub = q.Status == QuestStatus.Active
                    ? (q.RewardChoicePending ? Ui.Rich("Reward waiting!", PanelKit.GoodDark) : Short(q.StageText))
                    : q.Status == QuestStatus.Completed ? (q.RewardChoicePending ? Ui.Rich("Choose your reward", PanelKit.GoodDark) : "Completed") : "Failed",
                Meta = $"Level {q.Level}" + (string.IsNullOrEmpty(q.GiverName) ? "" : "  ·  from " + q.GiverName) + (q.Main ? "  ·  main story" : "") +
                       ZoneText(q) +
                       (q.Status == QuestStatus.Completed ? "  ·  " + Ui.Rich("completed", PanelKit.GoodDark) : q.Status == QuestStatus.Failed ? "  ·  " + Ui.Rich("failed", PanelKit.BadDark) : ""),
                Return = QuestTrackerHud.ReturnLine(PanelKit.Sess, q),
            };
            texts[q] = t;
            return t;
        }

        /// <summary>"  ·  Amberfield Downs (12–18)" for a quest with a zone (QuestDef.zone, MapDef.levelMin/levelMax).</summary>
        static string ZoneText(QuestJournalEntry q)
        {
            var db = PanelKit.Db;
            if (db == null || !db.Quests.TryGetValue(q.Id ?? "", out var def) || string.IsNullOrEmpty(def.zone) || !db.Maps.TryGetValue(def.zone, out var m)) return "";
            string band = m.levelMin > 0 && m.levelMax > 0 ? " (" + m.levelMin + "–" + m.levelMax + ")" : m.levelMin > 0 ? " (" + m.levelMin + "+)" : "";
            return "  ·  " + (string.IsNullOrEmpty(m.name) ? m.id : m.name) + band;
        }

        public JournalPanel()
        {
            PanelKit.Events += e =>
            {
                switch (e.Kind)
                {
                    case SessionEventKind.QuestStarted:
                    case SessionEventKind.QuestUpdated:
                    case SessionEventKind.QuestCompleted:
                    case SessionEventKind.QuestFailed:
                    case SessionEventKind.QuestRewardChoice:
                    case SessionEventKind.ItemReceived:
                    case SessionEventKind.ItemLost:
                        refreshAt = 0f;
                        if (e.Kind == SessionEventKind.QuestStarted && !string.IsNullOrEmpty(e.Id)) selectedId = e.Id;
                        break;
                    case SessionEventKind.GameStarted:
                    case SessionEventKind.GameLoaded:
                        refreshAt = 0f;
                        selectedId = "";
                        detailMeasuredFor = "";
                        break;
                }
            };
        }

        public override void Tick(float dt)
        {
            if (!Visible || Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + 1.5f;
            var s = PanelKit.Sess;
            try { entries = s.Journal(true) ?? new List<QuestJournalEntry>(); }
            catch (Exception e) { Debug.LogException(e); entries = new List<QuestJournalEntry>(); }
            BuildRows();
        }

        void BuildRows()
        {
            listRows.Clear();
            texts.Clear();
            int active = 0, done = 0, failed = 0;
            foreach (var q in entries)
            {
                if (q == null) continue;
                if (q.Status == QuestStatus.Active) active++;
                else if (q.Status == QuestStatus.Completed) done++;
                else if (q.Status == QuestStatus.Failed) failed++;
            }
            listRows.Add(new ListRow { Kind = 0, Text = $"Active ({active})", Section = 0 });
            foreach (var q in entries) if (q != null && q.Status == QuestStatus.Active) listRows.Add(new ListRow { Kind = 1, Entry = q, Section = 0 });
            if (done > 0)
            {
                listRows.Add(new ListRow { Kind = 0, Text = $"Completed ({done})" + (showCompleted ? "" : "  · show"), Section = 1 });
                if (showCompleted) foreach (var q in entries) if (q != null && q.Status == QuestStatus.Completed) listRows.Add(new ListRow { Kind = 1, Entry = q, Section = 1 });
            }
            if (failed > 0)
            {
                listRows.Add(new ListRow { Kind = 0, Text = $"Failed ({failed})" + (showFailed ? "" : "  · show"), Section = 2 });
                if (showFailed) foreach (var q in entries) if (q != null && q.Status == QuestStatus.Failed) listRows.Add(new ListRow { Kind = 1, Entry = q, Section = 2 });
            }
            bool found = false;
            foreach (var q in entries) if (q != null && q.Id == selectedId) { found = true; break; }
            if (!found)
            {
                selectedId = "";
                foreach (var q in entries) if (q != null && q.Status == QuestStatus.Active) { selectedId = q.Id; break; }
                if (selectedId.Length == 0 && entries.Count > 0 && entries[0] != null) selectedId = entries[0].Id;
            }
        }

        protected override void DrawPanel()
        {
            var r = PanelKit.Centered(Mathf.Min(1160f, Ui.Width - 40f), Mathf.Min(860f, Ui.Height - 120f), -10f);
            var c = Chrome(r, "Journal");
            // the roster is centred where the journal is: close the journal so it is not drawn on top of it
            if (Ui.Btn(new Rect(r.xMax - 260f, r.y + 16f, 190f, 38f), "Party & camp", PanelKit.SmallButton, true, "Party & camp (K)"))
            {
                Close();
                UiRoot.Open(UiPanels.Party);
            }
            var listR = new Rect(c.x, c.y, 380f, c.height);
            var detailR = new Rect(listR.xMax + 24f, c.y, c.xMax - listR.xMax - 24f, c.height);
            PanelKit.Rounded(listR, new Color(0.55f, 0.42f, 0.25f, 0.07f));
            DrawList(new Rect(listR.x + 8f, listR.y + 8f, listR.width - 16f, listR.height - 16f));
            QuestJournalEntry sel = null;
            foreach (var q in entries) if (q != null && q.Id == selectedId) { sel = q; break; }
            if (sel == null)
            {
                PanelKit.Label(detailR, entries.Count == 0 ? "No quests yet. Talk to the villagers — the valley needs help." : "Choose a quest on the left.", PanelKit.TextCenter);
                return;
            }
            DrawDetail(detailR, sel);
        }

        void DrawList(Rect r)
        {
            const float rowH = 56f, headH = 40f;
            float content = 0f;
            foreach (var row in listRows) content += row.Kind == 0 ? headH : rowH;
            float cw = PanelKit.BeginScroll(r, listScroll, content);
            int toggle = -1;
            try
            {
                float y = 0f;
                foreach (var row in listRows)
                {
                    if (row.Kind == 0)
                    {
                        var hr = new Rect(0f, y, cw, headH);
                        PanelKit.Label(new Rect(4f, y + 8f, cw - 8f, 30f), row.Text, PanelKit.Heading);
                        if (row.Section > 0 && PanelKit.LeftClick(hr)) toggle = row.Section;
                        y += headH;
                        continue;
                    }
                    var q = row.Entry;
                    var rr = new Rect(0f, y, cw, rowH - 4f);
                    y += rowH;
                    if (!listScroll.IsVisible(rr)) continue;
                    bool sel = q.Id == selectedId;
                    bool hover = PanelKit.Hover(rr);
                    PanelKit.Rounded(rr, sel ? PanelKit.RowSelected : hover ? PanelKit.RowHover : new Color(0f, 0f, 0f, 0f));
                    var g = Ui.GlyphTexture(q.Main ? "crown" : q.Status == QuestStatus.Completed ? "star" : "feather");
                    var gc = q.Main ? Ui.GoldDeep : q.Status == QuestStatus.Failed ? PanelKit.BadDark : Ui.InkSoft;
                    if (g != null) PanelKit.Tex(new Rect(rr.x + 8f, rr.y + 12f, 28f, 28f), g, gc, ScaleMode.ScaleToFit);
                    var tc = q.Status == QuestStatus.Active ? Ui.Ink : Ui.InkSoft;
                    PanelKit.Label(new Rect(rr.x + 44f, rr.y + 4f, rr.width - 96f, 26f), q.Title, PanelKit.RowText, tc);
                    var tx = TextOf(q);
                    PanelKit.Label(new Rect(rr.x + 44f, rr.y + 28f, rr.width - 96f, 22f), tx.Sub, PanelKit.RowTextSmall);
                    PanelKit.Label(new Rect(rr.xMax - 54f, rr.y + 6f, 48f, 24f), tx.Level, PanelKit.TextSmallRight, Ui.InkSoft);
                    if (PanelKit.LeftClick(rr)) { selectedId = q.Id; detailScroll.Reset(); Ui.Sfx?.Invoke("ui_click"); }
                }
            }
            finally { PanelKit.EndScroll(listScroll); }
            if (toggle == 1) { showCompleted = !showCompleted; BuildRows(); }
            else if (toggle == 2) { showFailed = !showFailed; BuildRows(); }
        }

        static readonly Dictionary<string, string> shortCache = new Dictionary<string, string>();

        static string Short(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            if (shortCache.TryGetValue(t, out var s)) return s;
            if (shortCache.Count > 200) shortCache.Clear();
            s = t.Length > 46 ? t.Substring(0, 44).TrimEnd() + "…" : t;
            shortCache[t] = s;
            return s;
        }

        ItemInstance Sample(string id)
        {
            if (samples.TryGetValue(id, out var it)) return it;
            var d = PanelKit.Db != null ? PanelKit.Db.Item(id) : null;
            it = d != null ? new ItemInstance(d) : null;
            samples[id] = it;
            return it;
        }

        void DrawDetail(Rect r, QuestJournalEntry q)
        {
            float w = r.width - 20f;
            float hSummary = PanelKit.TextHeight(q.Summary, PanelKit.TextItalic, w);
            float hStage = PanelKit.TextHeight(q.StageText, PanelKit.Text, w);
            float hObj = q.Objectives != null ? q.Objectives.Count * 32f : 0f;
            float hHist = 0f;
            if (q.History != null) foreach (var h in q.History) hHist += PanelKit.TextHeight(h, PanelKit.TextMutedSmall, w - 24f) + 6f;
            string qid = q.Id ?? "";
            float content = detailMeasuredFor == qid && detailMeasuredH > 0f
                ? detailMeasuredH
                : EstimateDetail(q, r.width, hSummary, hStage, hObj, hHist);
            float cw = PanelKit.BeginScroll(r, detailScroll, content);
            bool chooseNow = false;
            try
            {
                float y = 0f;
                PanelKit.Label(new Rect(0f, y, cw, 44f), q.Title, PanelKit.TitleDark, q.Main ? PanelKit.GoldInk : Ui.Ink);
                y += 46f;
                PanelKit.Label(new Rect(0f, y, cw, 26f), TextOf(q).Meta, PanelKit.TextMuted);
                y += 30f;
                if (hSummary > 0f) { PanelKit.Label(new Rect(0f, y, cw, hSummary + 4f), q.Summary, PanelKit.TextItalic); y += hSummary + 20f; }
                if (q.Status == QuestStatus.Active)
                {
                    PanelKit.Label(new Rect(0f, y, cw, 32f), "Now", PanelKit.Heading);
                    y += 38f;
                    if (hStage > 0f) { PanelKit.Label(new Rect(0f, y, cw, hStage + 4f), q.StageText, PanelKit.Text); y += hStage + 12f; }
                    if (q.Objectives != null)
                        foreach (var o in q.Objectives)
                        {
                            if (o == null) continue;
                            var box = new Rect(4f, y + 4f, 22f, 22f);
                            PanelKit.Rounded(box, o.Complete ? new Color(0.47f, 0.8f, 0.45f, 0.5f) : Color.white);
                            PanelKit.Outline(box, Ui.InkSoft);
                            if (o.Complete) PanelKit.Tex(new Rect(box.x + 2f, box.y + 2f, 18f, 18f), PanelArt.Check, PanelKit.GoodDark);
                            PanelKit.Label(new Rect(36f, y + 2f, cw - 36f, 28f), o.Display, PanelKit.Text, o.Complete ? Ui.InkSoft : Ui.Ink);
                            y += 32f;
                        }
                    string back = TextOf(q).Return;
                    if (back.Length > 0)
                    {
                        PanelKit.Label(new Rect(36f, y + 2f, cw - 36f, 28f), back, PanelKit.TextBold, PanelKit.GoldInk);
                        y += 32f;
                    }
                    y += 16f;
                }
                if (hHist > 0f)
                {
                    PanelKit.Label(new Rect(0f, y, cw, 32f), "So far", PanelKit.Heading);
                    y += 38f;
                    foreach (var h in q.History)
                    {
                        float hh = PanelKit.TextHeight(h, PanelKit.TextMutedSmall, w - 24f);
                        PanelKit.Tex(new Rect(4f, y + 6f, 10f, 10f), PanelArt.Diamond, Ui.InkSoft);
                        PanelKit.Label(new Rect(24f, y, cw - 24f, hh + 4f), h, PanelKit.TextMutedSmall);
                        y += hh + 6f;
                    }
                    y += 10f;
                }
                // rewards
                var rw = q.Rewards;
                if (HasRewards(rw))
                {
                    PanelKit.Label(new Rect(0f, y, cw, 32f), "Rewards", PanelKit.Heading);
                    y += 40f;
                    float x = 0f;
                    if (rw.xp > 0)
                    {
                        int xp = rw.xp;
                        var db = PanelKit.Db;
                        // as the session grants it: config.xpRateByLevel at the party's level
                        var sess = PanelKit.Sess;
                        if (db != null) try { xp = Progression.QuestXp(db, rw.xp, sess != null ? sess.PartyLevel : 1); } catch (Exception) { }
                        var plate = new Rect(x, y + 4f, 150f, 34f);
                        PanelKit.Rounded(plate, new Color(0.69f, 0.48f, 0.88f, 0.3f));
                        if (xp != xpShown) { xpShown = xp; xpText = "<b>" + xp + "</b> XP"; }
                        PanelKit.Label(plate, xpText, PanelKit.TextCenter);
                        x += 160f;
                    }
                    if (rw.gold > 0) { PanelKit.MoneyPlate(new Rect(x, y + 4f, 200f, 34f), rw.gold, false); x += 210f; }
                    y += 48f;
                    if (rw.items != null && rw.items.Length > 0)
                    {
                        y = DrawRewardItems(y, cw, rw.items, null);
                    }
                    if (rw.choiceItems != null && rw.choiceItems.Length > 0)
                    {
                        PanelKit.Label(new Rect(0f, y, cw, 26f), q.RewardChoicePending ? "Choose one of:" : "One of:", PanelKit.TextBold);
                        y += 30f;
                        y = DrawRewardItems(y, cw, rw.choiceItems, null);
                    }
                    if (q.RewardChoicePending)
                    {
                        if (PanelKit.Btn(new Rect(0f, y + 4f, 260f, 46f), "Choose your reward", Ui.ButtonGold)) chooseNow = true;
                        y += 56f;
                    }
                }
                detailMeasuredFor = qid;
                detailMeasuredH = y + DetailBottomPad;
            }
            finally { PanelKit.EndScroll(detailScroll); }
            if (chooseNow) { QuestRewardScreen.Show(q.Id); Close(); }
        }

        const float DetailBottomPad = 12f;

        static bool HasRewards(QuestRewardDef rw) =>
            rw != null && (rw.xp > 0 || rw.gold > 0 || (rw.items != null && rw.items.Length > 0) || (rw.choiceItems != null && rw.choiceItems.Length > 0));

        /// <summary>Content height of DrawDetail before it has been drawn for this quest: mirrors its layout (the "Now"
        /// section only for active quests; reward rows wrapped at the narrower width a scrollbar leaves).</summary>
        float EstimateDetail(QuestJournalEntry q, float width, float hSummary, float hStage, float hObj, float hHist)
        {
            float h = 46f + 30f + (hSummary > 0f ? hSummary + 20f : 0f);
            if (q.Status == QuestStatus.Active) h += 38f + (hStage > 0f ? hStage + 12f : 0f) + hObj + 16f;
            if (hHist > 0f) h += 38f + hHist + 10f;
            var rw = q.Rewards;
            if (HasRewards(rw))
            {
                float cw = width - 16f;
                h += 40f + 48f;
                if (rw.items != null && rw.items.Length > 0) h += RewardItemsHeight(rw.items, cw);
                if (rw.choiceItems != null && rw.choiceItems.Length > 0) h += 30f + RewardItemsHeight(rw.choiceItems, cw);
                if (q.RewardChoicePending) h += 56f;
            }
            return h + DetailBottomPad;
        }

        /// <summary>Height DrawRewardItems adds for these items at content width cw (same wrapping).</summary>
        float RewardItemsHeight(string[] ids, float cw)
        {
            float x = 0f, y = 0f;
            foreach (var id in ids)
            {
                if (Sample(id) == null) continue;
                if (x + 250f > cw) { x = 0f; y += 62f; }
                x += 250f;
            }
            return y + 64f;
        }

        float DrawRewardItems(float y, float cw, string[] ids, string hint)
        {
            float x = 0f;
            var member = PanelKit.Member;
            foreach (var id in ids)
            {
                var it = Sample(id);
                if (it == null) continue;
                if (x + 250f > cw) { x = 0f; y += 62f; }
                var rr = new Rect(x, y, 240f, 56f);
                bool hover = PanelKit.Hover(rr);
                PanelKit.Rounded(rr, hover ? PanelKit.RowHover : PanelKit.RowShade);
                PanelKit.ItemIcon(new Rect(rr.x + 4f, rr.y + 4f, 48f, 48f), it);
                PanelKit.Label(new Rect(rr.x + 60f, rr.y + 4f, rr.width - 64f, 48f), it.Name, PanelKit.TextSmall, PanelKit.QualityInk(it.Def.quality));
                if (hover) Ui.TooltipFor(rr, PanelKit.ItemTip(it, member, true, hint));   // built only for the hovered item
                x += 250f;
            }
            return y + 64f;
        }
    }
}
