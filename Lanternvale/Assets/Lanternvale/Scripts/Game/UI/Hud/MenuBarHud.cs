// Menu bar (top right, under the clock/gold pill): one button per window so everything reachable by hotkey is also
// reachable with the mouse — Character (C), Bags (I), Spellbook (P), Talents (N, glows with unspent points),
// Journal (J), Party, Map (M) and Help (F1). Each toggles its panel through UiRoot (posted, run from Tick); the hotkey
// sits in the button's corner and its tooltip. The first time the exploration HUD appears (after the opening
// conversation) a one-time toast points at the menus and F1.
using System;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class MenuBarHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderMenuBar;
        public bool Visible => Hud.WorldHud;
        public bool Modal => false;

        sealed class Entry
        {
            public string Panel, Glyph, Key, Tip;
        }

        static readonly Entry[] entries =
        {
            new Entry { Panel = UiPanels.Character, Glyph = "glyph_armor", Key = "C", Tip = "<b>Character</b>  (C)\nEquipment, stats and resistances." },
            new Entry { Panel = UiPanels.Inventory, Glyph = "glyph_coin", Key = "I", Tip = "<b>Bags</b>  (I or B)\nThe party's items — equip, use and sell." },
            new Entry { Panel = UiPanels.Spellbook, Glyph = "glyph_sparkle", Key = "P", Tip = "<b>Spellbook</b>  (P)\nEvery ability and its ranks." },
            new Entry { Panel = UiPanels.Talents, Glyph = "glyph_star", Key = "N", Tip = "<b>Talents</b>  (N)\nThree trees per class; a point every level from 10." },
            new Entry { Panel = UiPanels.Journal, Glyph = "glyph_feather", Key = "J", Tip = "<b>Journal</b>  (J)\nQuests, objectives and rewards." },
            new Entry { Panel = UiPanels.Party, Glyph = "glyph_banner", Key = "", Tip = "<b>Party</b>\nYour companions, the party order and the leader." },
            new Entry { Panel = UiPanels.Map, Glyph = "glyph_eagle", Key = "M", Tip = "<b>Map</b>  (M)\nWhere you are and where you can travel." },
            new Entry { Panel = UiPanels.Help, Glyph = "", Key = "F1", Tip = "<b>Help</b>  (F1)\nControls and how combat works." },
        };

        const int TalentsIndex = 3, HelpIndex = 7;
        const float Gap = 4f;
        const string HintKey = "lv.hud.menuHint";

        bool talentPoints;
        float talentTimer;
        int hintState = -1;          // -1 not loaded, 0 not shown yet, 1 shown
        float hintGlowUntil = -1f;
        string talentTip = "";

        public void Tick(float dt)
        {
            try
            {
                talentTimer -= dt;
                if (talentTimer <= 0f)
                {
                    talentTimer = 0.5f;
                    talentPoints = AnyTalentPoints();
                }
                if (hintState < 0)
                {
                    try { hintState = PlayerPrefs.GetInt(HintKey, 0) == 1 ? 1 : 0; } catch (Exception) { hintState = 1; }
                }
                if (hintState == 0 && Hud.ExploreHud && Hud.Flow != null)
                {
                    hintState = 1;
                    hintGlowUntil = Time.unscaledTime + 25f;
                    try { PlayerPrefs.SetInt(HintKey, 1); PlayerPrefs.Save(); } catch (Exception) { }
                    Hud.Flow.Toast("Menus are at the top right (Character C · Bags I · Spellbook P · Talents N · Journal J · Map M) — F1 shows every control.");
                }
            }
            catch (Exception e) { Hud.LogOnce("menubar-tick", "Menu bar: " + e.Message); }
        }

        static bool AnyTalentPoints()
        {
            var s = Hud.Session;
            if (s == null || !GameFlow.HasGame) return false;
            var party = s.Party;
            for (int i = 0; i < party.Count; i++)
            {
                var u = party[i];
                if (u == null || !u.IsCharacter) continue;
                try { if (s.TalentPointsAvailable(u) > 0) return true; }
                catch (Exception) { return false; }
            }
            return false;
        }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try { DrawInner(); }
            catch (Exception e) { Hud.LogOnce("menubar:" + e.GetType().Name, "Menu bar: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        void DrawInner()
        {
            HudStyles.Ensure();
            var bar = HudLayout.MenuBar;
            int n = entries.Length;
            float size = Mathf.Floor((bar.width - (n - 1) * Gap) / n);
            float x0 = bar.xMax - (n * size + (n - 1) * Gap);
            Ui.Block(new Rect(x0, bar.y, bar.xMax - x0, size));
            for (int i = 0; i < n; i++)
            {
                var en = entries[i];
                var r = new Rect(x0 + i * (size + Gap), bar.y, size, size);
                bool open = UiRoot.IsOpen(en.Panel);
                bool hov = HudDraw.Hover(r);
                bool glow = (i == TalentsIndex && talentPoints) || (i == HelpIndex && Time.unscaledTime < hintGlowUntil);
                if (glow)
                    HudDraw.Glow(new Rect(r.x - 12f, r.y - 12f, r.width + 24f, r.height + 24f), new Color(1f, 0.85f, 0.42f, 0.55f * HudDraw.Pulse(4f, 0.4f, 1f)));
                Color edge = open ? new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.95f)
                           : hov ? new Color(1f, 0.92f, 0.7f, 0.8f)
                           : glow ? new Color(1f, 0.85f, 0.45f, HudDraw.Pulse(4f, 0.5f, 1f)) : new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.35f);
                HudDraw.Frame(r, open ? 0.9f : 0.72f, edge);
                var ic = open ? Ui.Gold : hov ? Color.white : new Color(Ui.TextLight.r, Ui.TextLight.g, Ui.TextLight.b, 0.85f);
                var gr = new Rect(r.x + 7f, r.y + 6f, r.width - 14f, r.height - 14f);
                if (string.IsNullOrEmpty(en.Glyph)) HudDraw.Text(new Rect(r.x, r.y - 2f, r.width, r.height), "?", Ui.NumberStyle(24), ic);
                else HudDraw.Glyph(gr, en.Glyph, ic);
                if (en.Key.Length > 0)
                    HudDraw.Text(new Rect(r.x, r.yMax - 16f, r.width - 3f, 16f), en.Key, HudStyles.TinyRight, new Color(1f, 1f, 1f, hov ? 0.95f : 0.7f));
                if (hov)
                {
                    string tip = en.Tip;
                    if (i == TalentsIndex && talentPoints)
                    {
                        if (talentTip.Length == 0) talentTip = en.Tip + "\n" + Ui.Rich("Unspent talent points!", Ui.Gold);
                        tip = talentTip;
                    }
                    Ui.TooltipFor(r, tip);
                }
                if (HudDraw.Click(r))
                {
                    string id = en.Panel;
                    Hud.Post(() => UiRoot.Toggle(id));   // UiRoot plays the open/close sound
                }
            }
        }
    }
}
