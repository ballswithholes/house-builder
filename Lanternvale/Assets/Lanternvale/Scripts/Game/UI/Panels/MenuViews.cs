// Reusable bodies shared by the main menu, the pause menu panels and the game-over screen:
//   SaveSlotsView  — save/load slot list (SaveSlotInfo headers, overwrite/delete confirmations)
//   SettingsView   — audio volumes, animation and text speed, companion defaults
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class SaveSlotsView
    {
        public bool SaveMode;
        /// <summary>Called (deferred) after a successful load.</summary>
        public Action AfterLoad;

        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        readonly List<SaveSlotInfo> slots = new List<SaveSlotInfo>();
        readonly Dictionary<string, string[]> lines = new Dictionary<string, string[]>();
        float refreshAt;
        string selected = "";

        static readonly string[] ManualSlots = { "slot1", "slot2", "slot3", "slot4", "slot5", "slot6", "slot7", "slot8", "slot9" };

        public void Invalidate() => refreshAt = 0f;

        void Refresh()
        {
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + 1f;
            slots.Clear();
            lines.Clear();
            var f = PanelKit.Flow;
            List<SaveSlotInfo> all = null;
            try { all = f != null ? f.ListSaves() : null; }
            catch (Exception e) { Debug.LogException(e); }
            if (SaveMode)
            {
                foreach (var id in ManualSlots)
                {
                    SaveSlotInfo found = null;
                    if (all != null) foreach (var s in all) if (s != null && s.Slot == id) { found = s; break; }
                    slots.Add(found ?? new SaveSlotInfo { Slot = id });
                }
                SaveSlotInfo quick = null;
                if (all != null) foreach (var s in all) if (s != null && s.Slot == "quick") { quick = s; break; }
                slots.Insert(0, quick ?? new SaveSlotInfo { Slot = "quick" });
            }
            else if (all != null)
            {
                foreach (var s in all) if (s != null) slots.Add(s);
            }
        }

        static bool Exists(SaveSlotInfo s) => s != null && (s.Header != null || !string.IsNullOrEmpty(s.Error)) && s.Modified != default;

        string[] LinesOf(SaveSlotInfo s)
        {
            if (lines.TryGetValue(s.Slot, out var l)) return l;
            l = new string[5];   // title, line 1, line 2, date, portrait key
            l[0] = PanelKit.SlotDisplay(s.Slot);
            l[4] = s.Header != null ? "portrait_" + s.Header.playerClass.ToString().ToLowerInvariant() : "";
            if (!Exists(s))
            {
                l[1] = "Empty slot";
                l[2] = "";
                l[3] = "";
            }
            else if (s.Header == null)
            {
                l[1] = Ui.Rich("Unreadable save", PanelKit.BadDark);
                l[2] = string.IsNullOrEmpty(s.Error) ? "" : s.Error;
                l[3] = s.Modified.ToString("MMM d, HH:mm");
            }
            else
            {
                var h = s.Header;
                var db = PanelKit.Db;
                var cls = db != null ? db.Class(h.playerClass) : null;
                string clsName = cls != null ? cls.name : h.playerClass.ToString();
                l[1] = $"<b>{h.playerName}</b>  ·  Level {h.playerLevel} {Ui.Rich(clsName, PanelKit.InkColorOf(h.playerClass))}";
                string map = string.IsNullOrEmpty(h.mapName) ? h.mapId : h.mapName;
                l[2] = $"{map}  ·  Day {Mathf.Max(1, h.day)}, {PanelKit.Clock(h.gameHour)}  ·  played {PanelKit.PlayTime(h.playSeconds)}";
                l[3] = s.Modified.ToString("MMM d, HH:mm");
            }
            lines[s.Slot] = l;
            return l;
        }

        public void Draw(Rect r)
        {
            Refresh();
            const float rowH = 96f;
            if (slots.Count == 0)
            {
                PanelKit.Label(new Rect(r.x, r.y + 30f, r.width, 60f), "No saved games yet.\nYour adventure is saved automatically after the opening, when travelling and after victories.", PanelKit.TextCenter);
                return;
            }
            float cw = PanelKit.BeginScroll(r, scroll, slots.Count * rowH);
            try
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    var row = new Rect(0f, i * rowH, cw, rowH - 8f);
                    if (!scroll.IsVisible(row)) continue;
                    DrawRow(row, s);
                }
            }
            finally { PanelKit.EndScroll(scroll); }
        }

        void DrawRow(Rect row, SaveSlotInfo s)
        {
            bool exists = Exists(s);
            bool hover = PanelKit.Hover(row);
            bool sel = selected == s.Slot;
            PanelKit.Rounded(row, sel ? PanelKit.RowSelected : hover ? PanelKit.RowHover : PanelKit.RowShade);
            var l = LinesOf(s);
            var pr = new Rect(row.x + 10f, row.y + 9f, 70f, 70f);
            if (exists && s.Header != null) Ui.Portrait(pr, l[4], Ui.ClassColor(s.Header.playerClass));
            else PanelKit.Tex(pr, Ui.GlyphTexture("book") ?? PanelArt.Diamond, new Color(0.17f, 0.13f, 0.22f, 0.18f), ScaleMode.ScaleToFit);
            float x = pr.xMax + 14f;
            float bw = SaveMode ? 230f : 230f;
            float tw = row.width - (x - row.x) - bw;
            PanelKit.Label(new Rect(x, row.y + 6f, tw, 26f), l[0], PanelKit.TextBold);
            PanelKit.Label(new Rect(x + tw - 140f, row.y + 8f, 136f, 24f), l[3], PanelKit.TextSmallRight, Ui.InkSoft);
            PanelKit.Label(new Rect(x, row.y + 33f, tw, 26f), l[1], exists ? PanelKit.Text : PanelKit.TextMuted);
            PanelKit.Label(new Rect(x, row.y + 58f, tw, 24f), l[2], PanelKit.TextMutedSmall);
            if (PanelKit.LeftClick(new Rect(row.x, row.y, row.width - bw, row.height))) selected = s.Slot;

            float bx = row.xMax - bw + 6f, by = row.y + (row.height - 40f) * 0.5f;
            var f = PanelKit.Flow;
            if (SaveMode)
            {
                string why = null;
                var sess = PanelKit.Sess;
                if (sess != null) { try { why = sess.CannotSaveReason(); } catch (Exception) { } }
                if (PanelKit.Btn(new Rect(bx, by, 120f, 40f), exists ? "Overwrite" : "Save", PanelKit.SmallButtonGold, why == null, why))
                {
                    string slot = s.Slot;
                    if (exists)
                        ConfirmScreen.Ask("Overwrite save?", $"Replace the game in {PanelKit.SlotDisplay(slot)}?\n{StripTags(l[1])}", "Overwrite", () => DoSave(slot));
                    else PanelKit.Do(() => DoSave(slot));
                }
            }
            else
            {
                bool ok = exists && s.Header != null;
                if (PanelKit.Btn(new Rect(bx, by, 120f, 40f), "Load", PanelKit.SmallButtonGold, ok, ok ? null : "This save cannot be loaded."))
                {
                    string slot = s.Slot;
                    if (GameFlow.HasGame)
                        ConfirmScreen.Ask("Load game?", $"Load {PanelKit.SlotDisplay(slot)}? Progress since your last save will be lost.", "Load", () => DoLoad(slot));
                    else PanelKit.Do(() => DoLoad(slot));
                }
            }
            if (exists && f != null && PanelKit.Btn(new Rect(bx + 126f, by, 92f, 40f), "Delete", PanelKit.SmallButton))
            {
                string slot = s.Slot;
                ConfirmScreen.Ask("Delete save?", $"Delete {PanelKit.SlotDisplay(slot)} for good?", "Delete", () =>
                {
                    if (PanelKit.Try(() => PanelKit.Flow.DeleteSave(slot), "Save deleted.")) Invalidate();
                }, "Keep", null, true);
            }
        }

        void DoSave(string slot)
        {
            var f = PanelKit.Flow;
            if (f == null) return;
            if (PanelKit.Try(() => f.SaveToSlot(slot), "Saved to " + PanelKit.SlotDisplay(slot) + "."))
            {
                Ui.Sfx?.Invoke("ui_open");
                Invalidate();
            }
        }

        void DoLoad(string slot)
        {
            var f = PanelKit.Flow;
            if (f == null) return;
            if (PanelKit.Try(() => f.LoadFromSlot(slot)))
            {
                Invalidate();
                if (AfterLoad != null) AfterLoad();
            }
        }

        static string StripTags(string t)
        {
            if (string.IsNullOrEmpty(t) || t.IndexOf('<') < 0) return t;
            var sb = new System.Text.StringBuilder(t.Length);
            bool tag = false;
            foreach (var ch in t)
            {
                if (ch == '<') { tag = true; continue; }
                if (ch == '>') { tag = false; continue; }
                if (!tag) sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    // ======================================================================================== settings

    public sealed class SettingsView
    {
        static int idSeed;
        int dragId;
        int dragging = -1;
        bool audioDirty;

        static readonly float[] AnimSpeeds = { 0.5f, 1f, 1.5f, 2f, 3f };
        static readonly string[] AnimLabels = { "0.5×", "1×", "1.5×", "2×", "3×" };
        static readonly float[] TextSpeeds = { 0.5f, 1f, 2f, 0f };
        static readonly string[] TextLabels = { "Slow", "Normal", "Fast", "Instant" };

        public void Tick()
        {
            if (audioDirty && dragging < 0)
            {
                audioDirty = false;
                GameAudio.SavePrefs();
            }
        }

        public float Height => GameFlow.HasGame ? 720f : 550f;

        // interface size slider: the value follows the drag, the scale is applied on release (the layout would otherwise
        // move under the cursor while dragging)
        float uiScalePending = -1f;

        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();

        /// <summary>Draws the settings into r; scrolls when r is shorter than Height (large interface sizes).</summary>
        public void Draw(Rect r)
        {
            if (r.height + 0.5f >= Height) { DrawContent(r); return; }
            float cw = PanelKit.BeginScroll(r, scroll, Height);
            try { DrawContent(new Rect(0f, 0f, cw, Height)); }
            finally { PanelKit.EndScroll(scroll); }
        }

        void DrawContent(Rect r)
        {
            float y = r.y;
            Section(r.x, ref y, r.width, "Sound");
            GameAudio.MasterVolume = SliderRow(r.x, ref y, r.width, "Master volume", GameAudio.MasterVolume, 0);
            GameAudio.MusicVolume = SliderRow(r.x, ref y, r.width, "Music", GameAudio.MusicVolume, 1);
            GameAudio.SfxVolume = SliderRow(r.x, ref y, r.width, "Effects", GameAudio.SfxVolume, 2);
            bool muted = Toggle(new Rect(r.x, y, r.width, 34f), "Mute all sound", GameAudio.Muted);
            if (muted != GameAudio.Muted) { GameAudio.Muted = muted; audioDirty = true; }
            y += 44f;

            Section(r.x, ref y, r.width, "Interface");
            UiScaleRow(r.x, ref y, r.width);

            Section(r.x, ref y, r.width, "Pace");
            var f = PanelKit.Flow;
            float anim = f != null ? f.AnimationSpeed : 1f;
            int ai = Nearest(AnimSpeeds, anim);
            PanelKit.Label(new Rect(r.x, y, 230f, 36f), "Animation speed", PanelKit.Text);
            int na = Segments(new Rect(r.x + 240f, y, r.width - 240f, 36f), AnimLabels, ai, "How fast combat and actions play out (hold Shift in combat to fast-forward).");
            if (na != ai && f != null)
            {
                f.AnimationSpeed = AnimSpeeds[na];
                PanelPrefs.SaveAnimationSpeed(AnimSpeeds[na]);
            }
            y += 46f;
            int ti = Nearest(TextSpeeds, PanelPrefs.TextSpeed);
            PanelKit.Label(new Rect(r.x, y, 230f, 36f), "Dialogue text", PanelKit.Text);
            int nt = Segments(new Rect(r.x + 240f, y, r.width - 240f, 36f), TextLabels, ti, "How quickly dialogue lines are written out (click to finish a line).");
            if (nt != ti) PanelPrefs.TextSpeed = TextSpeeds[nt];
            y += 46f;
            // remembered across fights (PlayerPrefs, see CombatController.ShowMoveRangeSetting); a running fight follows it
            bool showRange = CombatController.ShowMoveRangeSetting;
            bool mr = Toggle(new Rect(r.x, y, r.width, 34f), "Show the movement range in combat", showRange,
                "Shades the ground the active character can still reach this turn.");
            if (mr != showRange) CombatController.ShowMoveRangeSetting = mr;
            y += 46f;

            var s = PanelKit.Sess;
            if (GameFlow.HasGame && s != null && s.Settings != null)
            {
                Section(r.x, ref y, r.width, "Companions (this game)");
                var st = s.Settings;
                bool v = Toggle(new Rect(r.x, y, r.width, 34f), "New companions play themselves in combat (auto-play)", st.CompanionAutoPlay,
                    "Default for companions who join from now on. Toggle each member in the Party window.");
                if (v != st.CompanionAutoPlay) { bool nv = v; PanelKit.Do(() => st.CompanionAutoPlay = nv); }
                y += 40f;
                v = Toggle(new Rect(r.x, y, r.width, 34f), "Companions learn new ranks for free when they level up", st.CompanionAutoTrain);
                if (v != st.CompanionAutoTrain) { bool nv = v; PanelKit.Do(() => st.CompanionAutoTrain = nv); }
                y += 40f;
                v = Toggle(new Rect(r.x, y, r.width, 34f), "Companions spend their talent points automatically", st.AutoAllocateCompanionTalents);
                if (v != st.AutoAllocateCompanionTalents) { bool nv = v; PanelKit.Do(() => st.AutoAllocateCompanionTalents = nv); }
                y += 40f;
            }
        }

        void UiScaleRow(float x, ref float y, float w)
        {
            PanelKit.Label(new Rect(x, y, 230f, 34f), "Interface size", PanelKit.Text);
            var track = new Rect(x + 240f, y + 8f, w - 240f - 70f, 18f);
            float range = Ui.MaxUserScale - Ui.MinUserScale;
            float cur = uiScalePending >= 0f ? uiScalePending : Ui.UserScale;
            float v01 = Slider(track, (cur - Ui.MinUserScale) / range, 3);
            // snap to 5 % steps
            float nv = Mathf.Round((Ui.MinUserScale + v01 * range) * 20f) / 20f;
            if (dragging == 3) uiScalePending = nv;
            else if (uiScalePending >= 0f)
            {
                Ui.UserScale = uiScalePending;   // released (or clicked): apply and remember
                uiScalePending = -1f;
            }
            PanelKit.Label(new Rect(track.xMax + 10f, y, 60f, 34f), PanelKit.PercentText(Mathf.RoundToInt(nv * 100f)), PanelKit.TextSmallRight);
            Ui.TooltipFor(new Rect(x, y, w, 34f), "Scales every window, the HUD and the nameplates (75–150 %). Applied when you let go of the slider.");
            y += 42f;
        }

        static int Nearest(float[] values, float v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i;
            return best;
        }

        static void Section(float x, ref float y, float w, string title)
        {
            PanelKit.Label(new Rect(x, y, w, 32f), title, PanelKit.Heading);
            y += 36f;
        }

        float SliderRow(float x, ref float y, float w, string label, float value, int index)
        {
            PanelKit.Label(new Rect(x, y, 230f, 34f), label, PanelKit.Text);
            var track = new Rect(x + 240f, y + 8f, w - 240f - 70f, 18f);
            float nv = Slider(track, value, index);
            PanelKit.Label(new Rect(track.xMax + 10f, y, 60f, 34f), PanelKit.PercentText(Mathf.RoundToInt(nv * 100f)), PanelKit.TextSmallRight);
            if (Mathf.Abs(nv - value) > 0.0001f) audioDirty = true;
            y += 42f;
            return nv;
        }

        float Slider(Rect track, float value, int index)
        {
            var e = Event.current;
            if (dragId == 0) dragId = 0x2A6D0000 + (++idSeed) * 16;
            int id = dragId + index;
            value = Mathf.Clamp01(value);
            var hit = new Rect(track.x - 10f, track.y - 10f, track.width + 20f, track.height + 20f);
            if (e != null)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
                {
                    dragging = index;
                    GUIUtility.hotControl = id;
                    value = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && dragging == index && GUIUtility.hotControl == id)
                {
                    // the real cursor: the settings scroll view (large interface sizes) and covering windows hide the
                    // mouse outside the viewport, which would snap the value to 0 when the drag overshoots
                    value = Mathf.Clamp01((PanelKit.RealMouse.x - track.x) / track.width);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && dragging == index)
                {
                    dragging = -1;
                    if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                    e.Use();
                }
            }
            if (PanelKit.IsRepaint)
            {
                PanelKit.Rounded(track, new Color(0.17f, 0.13f, 0.22f, 0.18f));
                PanelKit.Rounded(new Rect(track.x, track.y, Mathf.Max(track.height, track.width * value), track.height), new Color(0.91f, 0.70f, 0.36f, 0.95f));
                var knob = new Rect(track.x + track.width * value - 13f, track.center.y - 13f, 26f, 26f);
                PanelKit.Tex(knob, PanelArt.Disc, Ui.Ink);
                PanelKit.Tex(new Rect(knob.x + 3f, knob.y + 3f, 20f, 20f), PanelArt.Disc, dragging == index || PanelKit.Hover(hit) ? Ui.Hex("#ffe9b8") : Ui.Paper);
            }
            GameInput.BlockRectGui(hit);
            return value;
        }

        /// <summary>Checkbox row. Returns the new value.</summary>
        public static bool Toggle(Rect r, string label, bool value, string tip = null, bool block = true)
        {
            var box = new Rect(r.x, r.y + (r.height - 26f) * 0.5f, 26f, 26f);
            bool hover = PanelKit.Hover(r);
            PanelKit.Rounded(box, hover ? Ui.Hex("#ffe9b8") : Color.white);
            PanelKit.Outline(box, Ui.Ink);
            if (value) PanelKit.Tex(new Rect(box.x + 3f, box.y + 3f, 20f, 20f), PanelArt.Check, PanelKit.GoodDark);
            PanelKit.Label(new Rect(box.xMax + 12f, r.y, r.width - 40f, r.height), label, PanelKit.Text);
            if (tip != null) Ui.TooltipFor(r, tip);
            if (block) GameInput.BlockRectGui(r);
            if (PanelKit.LeftClick(r))
            {
                Ui.Sfx?.Invoke("ui_click");
                return !value;
            }
            return value;
        }

        static int Segments(Rect r, string[] labels, int selected, string tip)
        {
            float w = (r.width - (labels.Length - 1) * 6f) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var b = new Rect(r.x + i * (w + 6f), r.y, w, r.height);
                if (Ui.Btn(b, labels[i], i == selected ? PanelKit.TabOn : PanelKit.TabOff, true, tip)) selected = i;
            }
            return selected;
        }
    }
}
