// System windows: pause menu (Esc; the game is paused while it is open), save / load slots, settings and the controls
// cheat-sheet (F1).
using System;
using System.Collections.Generic;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class PauseMenuPanel : PanelWindow
    {
        public override string Id => UiPanels.Pause;
        public override int Order => 250;
        protected override bool ShowInDialogue => true;
        public override bool Modal => true;

        static readonly string[] Labels = { "Resume", "Save Game", "Load Game", "Settings", "Help", "Main Menu", "Quit" };
        string info = "";
        float infoAt;

        public override void Tick(float dt)
        {
            if (!Visible || Time.unscaledTime < infoAt) return;
            infoAt = Time.unscaledTime + 1f;
            var s = PanelKit.Sess;
            var def = s.MapDef;
            info = $"{(def != null ? def.name : "")}  ·  Day {Mathf.Max(1, s.Day)}, {PanelKit.Clock(s.GameHour)}  ·  played {PanelKit.PlayTime(s.PlaySeconds)}";
        }

        protected override void DrawPanel()
        {
            PanelKit.OccludeAll(Order);
            Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
            PanelKit.Rect(new Rect(0f, 0f, Ui.Width, Ui.Height), new Color(0.06f, 0.04f, 0.10f, 0.45f));
            var r = PanelKit.Centered(460f, 640f);
            var c = PanelKit.Frame(r, "Paused", Order);
            PanelKit.Label(new Rect(c.x, c.y, c.width, 24f), info, PanelKit.TextSmallCenter, Ui.InkSoft);
            var s = PanelKit.Sess;
            string saveWhy = null;
            try { saveWhy = s.CannotSaveReason(); } catch (Exception) { }
            float y = c.y + 38f;
            for (int i = 0; i < Labels.Length; i++)
            {
                bool enabled = i != 1 || saveWhy == null;
                var b = new Rect(c.x + 30f, y, c.width - 60f, 54f);
                if (Ui.Btn(b, Labels[i], i == 0 ? Ui.ButtonGold : Ui.Button, enabled, i == 1 ? saveWhy : null)) Activate(i);
                y += 64f;
            }
        }

        void Activate(int i)
        {
            switch (i)
            {
                case 0: Close(); break;
                case 1: SaveLoadPanel.OpenFor(true); break;
                case 2: SaveLoadPanel.OpenFor(false); break;
                case 3: UiRoot.Open(UiPanels.Settings); break;
                case 4: UiRoot.Open(UiPanels.Help); break;
                case 5:
                    ConfirmScreen.Ask("Return to the main menu?", "Progress since your last save will be lost.", "Main Menu", () =>
                    {
                        UiRoot.CloseAll();
                        PanelKit.Flow?.ReturnToMainMenu();
                    });
                    break;
                case 6:
                    ConfirmScreen.Ask("Quit Lanternvale?", "Progress since your last save will be lost.", "Quit", () => PanelKit.Flow?.QuitGame(), "Stay", null, true);
                    break;
            }
        }
    }

    public sealed class SaveLoadPanel : PanelWindow
    {
        public override string Id => UiPanels.SaveLoad;
        public override int Order => 255;
        protected override bool ShowInDialogue => true;
        public override bool Modal => true;

        static bool saveMode;
        static readonly string[] Tabs = { "Save", "Load" };
        readonly SaveSlotsView view = new SaveSlotsView();

        public static void OpenFor(bool save)
        {
            saveMode = save;
            UiRoot.Open(UiPanels.SaveLoad);
        }

        protected override void DrawPanel()
        {
            PanelKit.OccludeAll(Order);
            Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
            var r = PanelKit.Centered(Mathf.Min(1000f, Ui.Width - 40f), Mathf.Min(820f, Ui.Height - 80f));
            var c = Chrome(r, saveMode ? "Save Game" : "Load Game");
            int t = PanelKit.Tabs(new Rect(c.x, c.y, 300f, 40f), Tabs, saveMode ? 0 : 1);
            if ((t == 0) != saveMode) { saveMode = t == 0; view.Invalidate(); }
            view.SaveMode = saveMode;
            if (saveMode)
            {
                string why = null;
                try { why = PanelKit.Sess.CannotSaveReason(); } catch (Exception) { }
                if (why != null) PanelKit.Label(new Rect(c.x + 320f, c.y + 6f, c.width - 320f, 30f), why, PanelKit.TextSmall, PanelKit.BadDark);
                else PanelKit.Label(new Rect(c.x + 320f, c.y + 6f, c.width - 320f, 30f), "F5 quick saves, F9 quick loads.", PanelKit.TextMutedSmall);
            }
            view.Draw(new Rect(c.x, c.y + 54f, c.width, c.height - 54f));
        }
    }

    public sealed class SettingsPanel : PanelWindow
    {
        public override string Id => UiPanels.Settings;
        public override int Order => 256;
        protected override bool ShowInDialogue => true;
        public override bool Modal => true;

        readonly SettingsView view = new SettingsView();

        public override void Tick(float dt) => view.Tick();

        protected override void DrawPanel()
        {
            PanelKit.OccludeAll(Order);
            Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
            var r = PanelKit.Centered(840f, view.Height + 110f);
            var c = Chrome(r, "Settings");
            view.Draw(new Rect(c.x + 10f, c.y, c.width - 20f, c.height));
        }
    }

    public sealed class HelpPanel : PanelWindow
    {
        public override string Id => UiPanels.Help;
        public override int Order => 257;
        protected override bool ShowInDialogue => true;

        static readonly string[][] Columns =
        {
            new[]
            {
                "Exploring",
                "Left click|walk there (hold to keep walking)",
                "Click a person|talk",
                "Click a chest, sign or exit|open, read, travel",
                "Click an enemy|attack first (from stealth: a surprise round)",
                "Ability from the bar|buffs and heals; attacks arm an opener",
                "Right click|stop, cancel an opener",
                "Tab / Shift+Tab|next / previous party member",
                "Mouse wheel|zoom",
                "Q / E or middle drag|rotate the camera",
                "WASD / arrows|pan (Shift+middle drag too)",
                "Middle click|recentre the camera",
                "F5 / F9|quick save / quick load",
            },
            new[]
            {
                "Combat — six-second turns",
                "Each turn|6 s of Time for abilities + metres to move",
                "Click ground|move (the path shows what it costs)",
                "Click an enemy|attack (walks into reach first)",
                "1 – 0 or click|use an ability, then pick a target",
                "Right click / Esc|cancel targeting",
                "Space / Enter|end the turn (auto attacks swing now)",
                "Hold Shift|fast-forward the animations",
                "Long casts|carry over to the next turn; can be interrupted",
            },
            new[]
            {
                "Windows",
                "C|character sheet",
                "I or B|bags",
                "P|spellbook (Shift/right click: pick a rank)",
                "N|talents",
                "J|journal",
                "K|party & camp (swap companions)",
                "M|map",
                "L|combat log",
                "Esc|close the last window / pause",
                "F1|this help",
            },
        };

        static readonly string[] Tips =
        {
            "Mana users regenerate faster when they have not cast for five seconds — pace your spells.",
            "Warriors gain rage by hitting and being hit; rogues spend energy and finish with combo points.",
            "Tanks hold threat with taunts and stances; healers draw threat too.",
            "Skill checks in conversation roll a d20 plus the best party member's modifier against the DC.",
            "Trainers in the village teach new ranks; talents unlock at level 10.",
        };

        static string[][] keys, whats;

        static void Split()
        {
            if (keys != null) return;
            keys = new string[Columns.Length][];
            whats = new string[Columns.Length][];
            for (int i = 0; i < Columns.Length; i++)
            {
                keys[i] = new string[Columns[i].Length];
                whats[i] = new string[Columns[i].Length];
                for (int k = 1; k < Columns[i].Length; k++)
                {
                    var line = Columns[i][k];
                    int bar = line.IndexOf('|');
                    keys[i][k] = bar > 0 ? line.Substring(0, bar) : line;
                    whats[i][k] = bar > 0 ? line.Substring(bar + 1) : "";
                }
            }
        }

        const float RowStep = 46f, ColumnHead = 42f;
        // "Good to know": divider 10 above, heading 38, two tips per 36 px row, 4 px bottom margin (150 for 5 tips)
        static float TipsHeight => 42f + (Tips.Length + 1) / 2 * 36f;

        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();

        protected override void DrawPanel()
        {
            Split();
            var r = PanelKit.Centered(Mathf.Min(1500f, Ui.Width - 40f), Mathf.Min(820f, Ui.Height - 60f));
            var c = Chrome(r, "How to play");
            int rows = 0;
            foreach (var col in Columns) rows = Mathf.Max(rows, col.Length - 1);
            float columnsH = ColumnHead + rows * RowStep;
            // the tips go below the tallest column (12 px clear of it, plus the 10 px to their divider); when the window
            // is too short for both (the 150% interface size: a 720 high canvas) the body scrolls instead of overlapping
            float contentH = columnsH + 22f + TipsHeight;
            if (contentH <= c.height + 0.5f)
            {
                DrawBody(c, columnsH);
                return;
            }
            float cw = PanelKit.BeginScroll(c, scroll, contentH);
            try { DrawBody(new Rect(0f, 0f, cw, contentH), columnsH); }
            finally { PanelKit.EndScroll(scroll); }
        }

        void DrawBody(Rect c, float columnsH)
        {
            float colW = (c.width - 40f) / 3f;
            for (int i = 0; i < Columns.Length; i++)
            {
                var col = Columns[i];
                float x = c.x + i * (colW + 20f);
                float y = c.y;
                PanelKit.Label(new Rect(x, y, colW, 34f), col[0], PanelKit.Heading);
                y += ColumnHead;
                for (int k = 1; k < col.Length; k++)
                {
                    string key = keys[i][k];
                    string what = whats[i][k];
                    var kr = new Rect(x, y, colW * 0.42f, 44f);
                    PanelKit.Rounded(new Rect(kr.x, kr.y + 4f, kr.width - 8f, 34f), new Color(0.17f, 0.13f, 0.22f, 0.08f));
                    PanelKit.Label(new Rect(kr.x + 10f, kr.y + 4f, kr.width - 20f, 34f), key, PanelKit.TextBoldSmall);
                    PanelKit.Label(new Rect(x + colW * 0.42f + 4f, y + 4f, colW * 0.58f - 4f, 44f), what, PanelKit.TextSmall);
                    y += RowStep;
                }
            }
            float ty = Mathf.Max(c.yMax - TipsHeight, c.y + columnsH + 22f);
            PanelKit.HLine(c.x, ty - 10f, c.width);
            PanelKit.Label(new Rect(c.x, ty, c.width, 30f), "Good to know", PanelKit.Heading);
            for (int i = 0; i < Tips.Length; i++)
            {
                int col = i % 2;
                var tr = new Rect(c.x + col * (c.width * 0.5f), ty + 38f + (i / 2) * 36f, c.width * 0.5f - 10f, 36f);
                PanelKit.Tex(new Rect(tr.x, tr.y + 9f, 10f, 10f), PanelArt.Diamond, Ui.GoldDeep);
                PanelKit.Label(new Rect(tr.x + 18f, tr.y, tr.width - 18f, tr.height), Tips[i], PanelKit.TextSmall);
            }
        }
    }
}
