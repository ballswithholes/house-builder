// Top-right: the clock (day, time, phase with sun/moon) and the party's gold, and below it (exploration only)
// a quest tracker listing active quests from the session's journal with their objectives and progress.
// Quest events flash the updated quest; clicking a quest opens the journal; the header collapses the list.
// The menu buttons (MenuBarHud) sit between the clock and the tracker (HudLayout.MenuBar).
using System;
using System.Collections.Generic;
using System.Globalization;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class QuestTrackerHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderQuestTracker;
        public bool Visible => Hud.WorldHud;
        public bool Modal => false;

        public const float W = 336f, ClockH = 40f;
        const int MaxQuests = 4;

        static string flashId = "";
        static float flashTime = -100f;
        static bool dirty = true;

        /// <summary>Highlights a quest in the tracker for a moment (called by the toast layer on quest events).</summary>
        public static void Flash(string questId)
        {
            flashId = questId ?? "";
            flashTime = Time.unscaledTime;
            dirty = true;
        }

        sealed class Line
        {
            public string Text = "";
            public GUIStyle Style;
            public Color Color;
            public float Height;
            public bool Title;
            public bool Main;
            public bool Done;
            public string QuestId = "";
        }

        readonly List<Line> lines = new List<Line>(24);
        readonly GUIContent measure = new GUIContent();
        float refreshTimer;
        bool layoutDirty = true;
        float layoutWidth = -1f;
        int layoutStyles = -1;   // HudStyles.Version the heights were measured with
        int moreCount;
        readonly HudText.One moreText = new HudText.One("+", " more in the journal (J)");
        bool collapsed;
        bool prefsLoaded;
        GameSession lastSession;

        // camp (long rest) button: shown on rest-area maps while exploring; reason refreshed twice a second
        bool campShown;
        string campWhy;
        float campTimer;
        const string CampTip = "<b>Make camp</b>\nA long rest: health, mana and energy are restored, cooldowns and debuffs cleared, " +
                               "and a fallen hunter's pet returns. The party wakes at 08:00 the next morning.";
        string campTipWhy = "", campTipWhyFor;

        // clock & gold caches
        int clockMinute = -1, clockDay = -1;
        string clockText = "", phase = "", phaseLabel = "";
        int goldShown = int.MinValue;
        string goldText = "";

        public void Tick(float dt)
        {
            if (!prefsLoaded)
            {
                prefsLoaded = true;
                try { collapsed = PlayerPrefs.GetInt("lv.hud.questsCollapsed", 0) == 1; } catch (Exception) { }
            }
            var s = Hud.Session;
            if (!GameFlow.HasGame || s == null) { if (lines.Count > 0) lines.Clear(); return; }
            if (s != lastSession) { lastSession = s; dirty = true; }
            campTimer -= dt;
            if (campTimer <= 0f)
            {
                campTimer = 0.5f;
                campShown = s.Mode == SessionMode.Exploration && s.MapDef != null && s.MapDef.restArea;
                try { campWhy = campShown ? s.CannotRestReason() : null; }
                catch (Exception) { campWhy = null; }
            }
            refreshTimer -= dt;
            if (dirty || refreshTimer <= 0f)
            {
                dirty = false;
                refreshTimer = 2.5f;
                try { Rebuild(s); }
                catch (Exception e) { Hud.LogOnce("quests", "Quest tracker: " + e.Message); }
            }
        }

        void Rebuild(GameSession s)
        {
            lines.Clear();
            moreCount = 0;
            List<QuestJournalEntry> journal = s.Journal(false);
            int shown = 0;
            for (int i = 0; i < journal.Count; i++)
            {
                var q = journal[i];
                if (q == null || q.Status != QuestStatus.Active) continue;
                if (shown >= MaxQuests) { moreCount++; continue; }
                shown++;
                lines.Add(new Line { Text = q.Title, Title = true, Main = q.Main, QuestId = q.Id, Color = q.Main ? Hud.C("#ffd27a") : Hud.C("#f2e6c8") });
                bool any = false;
                if (q.Objectives != null)
                    for (int k = 0; k < q.Objectives.Count; k++)
                    {
                        var o = q.Objectives[k];
                        if (o == null) continue;
                        string txt = !string.IsNullOrEmpty(o.Display) ? o.Display : o.Text;
                        if (string.IsNullOrEmpty(txt)) continue;
                        any = true;
                        lines.Add(new Line { Text = "•  " + txt, QuestId = q.Id, Done = o.Complete, Color = o.Complete ? Hud.C("#8fd08a") : Ui.TextLight });
                    }
                if (!any && !string.IsNullOrEmpty(q.StageText))
                    lines.Add(new Line { Text = q.StageText, QuestId = q.Id, Color = Hud.Muted });
            }
            layoutDirty = true;
        }

        void Layout(float width)
        {
            HudStyles.Ensure();
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                l.Style = l.Title ? HudStyles.TitleLine : HudStyles.BodyWrap;
                measure.text = l.Text;
                float w = l.Title ? width - (l.Main ? 22f : 0f) : width - 12f;
                l.Height = Mathf.Ceil(l.Style.CalcHeight(measure, w)) + (l.Title ? 2f : 0f);
            }
            layoutDirty = false;
            layoutWidth = width;
            layoutStyles = HudStyles.Version;
        }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try { DrawInner(); }
            catch (Exception e) { Hud.LogOnce("tracker:" + e.GetType().Name, "Quest tracker: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        void DrawInner()
        {
            HudStyles.Ensure();
            var s = Hud.Session;
            if (s == null) return;
            float x = Ui.Width - HudLayout.Margin - W;
            DrawClock(new Rect(x, HudLayout.Margin, W, ClockH), s);
            if (campShown && Hud.ExploreHud) DrawCamp(new Rect(x - ClockH - 6f, HudLayout.Margin, ClockH, ClockH));
            if (Hud.CombatHud || lines.Count == 0) return;

            float inner = W - 28f;
            if (layoutDirty || Mathf.Abs(layoutWidth - inner) > 0.5f || layoutStyles != HudStyles.Version) Layout(inner);
            float y = HudLayout.MenuBar.yMax + 8f;   // below the menu buttons (MenuBarHud)
            float h = 34f;
            if (!collapsed)
            {
                for (int i = 0; i < lines.Count; i++) h += lines[i].Height + (lines[i].Title && i > 0 ? 8f : 1f);
                if (moreCount > 0) h += 22f;
                h += 8f;
            }
            var panel = new Rect(x, y, W, h);
            HudDraw.Frame(panel, 0.7f);
            Ui.Block(panel);

            // header
            var hr = new Rect(panel.x + 14f, panel.y + 6f, W - 28f, 24f);
            HudDraw.Text(hr, "Quests", HudStyles.Header, Ui.Gold);
            HudDraw.Arrow(new Rect(panel.xMax - 30f, panel.y + 12f, 14f, 11f), collapsed, new Color(1f, 1f, 1f, HudDraw.Hover(hr) ? 1f : 0.6f));
            if (HudDraw.Hover(hr)) Ui.TooltipFor(hr, collapsed ? "Show tracked quests" : "Hide tracked quests (J opens the journal)");
            if (HudDraw.Click(hr))
            {
                collapsed = !collapsed;
                try { PlayerPrefs.SetInt("lv.hud.questsCollapsed", collapsed ? 1 : 0); } catch (Exception) { }
                Ui.Sfx?.Invoke("ui_click");
            }
            if (collapsed) return;

            float ly = panel.y + 34f;
            float flashA = Time.unscaledTime - flashTime < 2.6f ? 1f - (Time.unscaledTime - flashTime) / 2.6f : 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.Title && i > 0) ly += 7f;
                float lx = panel.x + 14f;
                if (l.Title)
                {
                    var tr = new Rect(lx, ly, W - 28f, l.Height);
                    if (flashA > 0f && l.QuestId == flashId)
                        HudDraw.Glow(new Rect(tr.x - 20f, tr.y - 10f, tr.width + 40f, tr.height + 20f), new Color(1f, 0.85f, 0.4f, 0.55f * flashA));
                    if (l.Main) { HudDraw.Glyph(new Rect(lx, ly + 2f, 16f, 16f), "glyph_star", Ui.Gold); lx += 22f; }
                    var textR = new Rect(lx, ly, panel.xMax - 14f - lx, l.Height);
                    bool hov = HudDraw.Hover(tr);
                    HudDraw.Text(textR, l.Text, l.Style, hov ? Color.white : l.Color);
                    if (hov) Ui.TooltipFor(tr, "Click to open the journal (J).");
                    if (HudDraw.Click(tr)) Hud.Post(() => UiRoot.Open(UiPanels.Journal));
                }
                else
                {
                    var r = new Rect(lx + 12f, ly, panel.xMax - 14f - lx - 12f, l.Height);
                    HudDraw.Text(r, l.Text, l.Style, l.Done ? new Color(l.Color.r, l.Color.g, l.Color.b, 0.75f) : l.Color);
                }
                ly += l.Height + 1f;
            }
            if (moreCount > 0)
                HudDraw.Text(new Rect(panel.x + 14f, ly + 2f, W - 28f, 18f), moreText.Get(moreCount), HudStyles.Small, Hud.Muted);
        }

        /// <summary>Square "make camp" button left of the clock (GameFlow.TryRest; the Rested event plays the fade).</summary>
        void DrawCamp(Rect r)
        {
            bool can = string.IsNullOrEmpty(campWhy);
            bool hov = HudDraw.Hover(r);
            HudDraw.Frame(r, 0.72f, hov && can ? new Color(1f, 0.85f, 0.45f, 0.9f) : (Color?)null);
            Ui.Block(r);
            HudDraw.Glyph(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, r.height - 16f), "glyph_moon",
                can ? new Color(0.78f, 0.84f, 1f, hov ? 1f : 0.85f) : new Color(0.6f, 0.58f, 0.66f, 0.55f));
            if (hov)
            {
                if (!can && campWhy != campTipWhyFor) { campTipWhyFor = campWhy; campTipWhy = CampTip + "\n" + Ui.Rich(campWhy, Hud.C("#ff9a7a")); }
                Ui.TooltipFor(r, can ? CampTip : campTipWhy);
            }
            if (can && HudDraw.Click(r))
            {
                Ui.Sfx?.Invoke("ui_click");
                Hud.Post(() =>
                {
                    var f = Hud.Flow;
                    var why = f != null ? f.TryRest() : "There is no game running.";
                    if (!string.IsNullOrEmpty(why)) Hud.Error(why);
                    campTimer = 0f;
                });
            }
        }

        void DrawClock(Rect r, GameSession s)
        {
            HudDraw.Frame(r, 0.72f);
            Ui.Block(r);
            UpdateClock(s);
            Color pc = PhaseColor(phase);
            var gr = new Rect(r.x + 10f, r.y + 8f, 24f, 24f);
            HudDraw.Glyph(gr, phase == "night" ? "glyph_moon" : "glyph_sun", pc);
            HudDraw.Text(new Rect(r.x + 42f, r.y + 2f, 160f, 22f), clockText, HudStyles.NameSmall, Ui.TextLight);
            HudDraw.Text(new Rect(r.x + 42f, r.y + 20f, 160f, 16f), phaseLabel, HudStyles.Tiny, pc);
            UpdateGold(s);
            var gold = new Rect(r.x + 150f, r.y + 2f, r.width - 162f, r.height - 4f);
            var st = HudStyles.SmallRight;
            HudDraw.Rich(gold, goldText, st);
            if (HudDraw.Hover(r))
                Ui.TooltipFor(r, "<b>Day " + s.Day + "</b> · " + clockText + " (" + phaseLabel.ToLowerInvariant() + ")\nPurse: " + goldText +
                                 "\n" + Ui.Rich("The clock runs while you explore and pauses in conversations and combat.", Hud.Muted));
        }

        void UpdateClock(GameSession s)
        {
            float hour = s.GameHour;
            int minute = Mathf.Clamp(Mathf.FloorToInt(hour * 60f), 0, 24 * 60 - 1);
            minute -= minute % 5;   // a calm clock: five-minute steps
            int day = s.Day;
            if (minute == clockMinute && day == clockDay) return;
            clockMinute = minute;
            clockDay = day;
            clockText = "Day " + day.ToString(CultureInfo.InvariantCulture) + "  ·  " + (minute / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("00", CultureInfo.InvariantCulture);
            string p = "";
            try { p = s.TimeOfDay ?? ""; } catch (Exception) { }
            phase = p;
            phaseLabel = p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : "";
        }

        void UpdateGold(GameSession s)
        {
            int g = s.Gold;
            if (g == goldShown) return;
            goldShown = g;
            goldText = Ui.Money(g);
        }

        static Color PhaseColor(string p)
        {
            switch (p)
            {
                case "dawn": return Hud.C("#ffc6a8");
                case "day": return Hud.C("#ffe08a");
                case "dusk": return Hud.C("#ff9e8a");
                case "night": return Hud.C("#b9c8ff");
                default: return Ui.Gold;
            }
        }
    }
}
