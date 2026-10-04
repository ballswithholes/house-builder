// Combat log: a history that mirrors Combat.LogLines (kept across battles, separated per fight), the toggled
// panel (Id = UiPanels.CombatLog, hotkey L via UiRoot: scrollable, sticks to the newest line, draggable, Clear)
// and a compact always-visible 4-line log in combat (click it to open the panel).
using System;
using System.Collections.Generic;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>Combat log lines (rich text) mirrored from the combat controller, kept across battles.</summary>
    public static class CombatLogHistory
    {
        public const int Cap = 800;
        static readonly List<string> lines = new List<string>(Cap + 64);
        static CombatController tracked;
        static GameSession trackedSession;
        static string lastRef;
        static int frame = -1;
        static int battles;

        public static IReadOnlyList<string> Lines => lines;
        /// <summary>Increments on every change.</summary>
        public static int Version { get; private set; }
        /// <summary>Total lines dropped from the front since the last Clear (keeps cached layouts aligned).</summary>
        public static int Dropped { get; private set; }
        /// <summary>Index (in Lines) of the first line of the current battle, or -1.</summary>
        public static int BattleStart { get; private set; } = -1;

        /// <summary>Pulls new lines from the active combat controller (once per frame; called from Tick).</summary>
        public static void Update()
        {
            int f = Time.frameCount;
            if (f == frame) return;
            frame = f;
            var s = Hud.Session;
            if (s != trackedSession) { trackedSession = s; Clear(); battles = 0; }
            var c = Hud.Combat;
            if (c != tracked)
            {
                // lines the old controller presented in its last frame (it is disposed inside CombatEnded)
                if (tracked != null) Pull(tracked);
                tracked = c;
                lastRef = null;
                if (c != null)
                {
                    battles++;
                    Add("<color=#b98a3e>—  Battle " + battles + "  —</color>");
                    BattleStart = lines.Count - 1;
                }
            }
            if (c != null) Pull(c);
        }

        static void Pull(CombatController c)
        {
            IReadOnlyList<string> src;
            try { src = c.LogLines; }
            catch (Exception) { return; }
            if (src == null) return;
            int n = src.Count;
            if (n == 0) return;
            if (lastRef != null && ReferenceEquals(src[n - 1], lastRef)) return;
            int start = 0;
            if (lastRef != null)
                for (int i = n - 1; i >= 0; i--)
                    if (ReferenceEquals(src[i], lastRef)) { start = i + 1; break; }
            for (int i = start; i < n; i++) Add(src[i]);
            lastRef = src[n - 1];
        }

        static void Add(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            lines.Add(s);
            if (lines.Count > Cap + 50)
            {
                int k = lines.Count - Cap;
                lines.RemoveRange(0, k);
                Dropped += k;
                if (BattleStart >= 0) BattleStart = Mathf.Max(0, BattleStart - k);
            }
            Version++;
        }

        public static void Clear()
        {
            lines.Clear();
            Dropped = 0;
            BattleStart = tracked != null ? 0 : -1;   // cleared mid-fight: the compact log keeps following this fight
            Version++;
        }
    }

    /// <summary>
    /// The full combat log panel (L). Sits at the bottom of the panel band (Hud.OrderCombatLogPanel) so every other window
    /// covers it, and hides while a modal screen (dialogue, game over, menus) is up — it stays toggled open.
    /// </summary>
    public sealed class CombatLogPanel : IUiScreen
    {
        public string Id => UiPanels.CombatLog;
        public int Order => Hud.OrderCombatLogPanel;
        public bool Visible => UiRoot.IsOpen(Id) && GameFlow.HasGame && !UiRoot.IsOpen(UiPanels.Pause) && !UiRoot.ModalActive;
        public bool Modal => false;

        const float DefaultW = 600f, DefaultH = 392f, HeaderH = 40f;

        readonly List<float> heights = new List<float>(CombatLogHistory.Cap + 64);
        readonly List<float> offsets = new List<float>(CombatLogHistory.Cap + 64);
        readonly GUIContent measure = new GUIContent();
        int laidVersion = -1, laidDropped, laidStyles = -1;
        float laidWidth = -1f, contentH;
        Vector2 scroll;
        bool stick = true;
        Vector2 offset;           // user drag offset
        bool draggingPanel, draggingThumb;
        float thumbGrab;
        Vector2 dragStart, offsetStart;

        public void Tick(float dt) => CombatLogHistory.Update();

        Rect PanelRect()
        {
            float w = Mathf.Min(DefaultW, Ui.Width - 2f * HudLayout.Margin);
            var log = HudLayout.CompactLog;
            var r = new Rect(HudLayout.Margin, log.yMax - DefaultH, w, DefaultH);
            r.x = Mathf.Clamp(r.x + offset.x, 0f, Ui.Width - r.width);
            r.y = Mathf.Clamp(r.y + offset.y, 0f, Ui.Height - r.height);
            return r;
        }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try { DrawInner(layer); }
            catch (Exception e) { Hud.LogOnce("log:" + e.GetType().Name, "Combat log panel: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        void DrawInner(HudDraw.Layer layer)
        {
            HudStyles.Ensure();
            var r = PanelRect();
            Ui.Panel(r, Ui.InkPanel, true);
            Hud.Occlude(r, Order);
            var e = Event.current;
            // drags continue while the cursor crosses a window above this panel: they follow the REAL mouse (the layer
            // hides it from clicks/hover only); new drags still start only where the panel is really under the mouse
            var real = layer.RealMouse;

            // header (drag to move)
            var hr = new Rect(r.x + 16f, r.y + 8f, r.width - 150f, 26f);
            HudDraw.Text(hr, "Combat Log", HudStyles.Header, Ui.Gold);
            var clear = new Rect(r.xMax - 128f, r.y + 8f, 72f, 26f);
            var close = new Rect(r.xMax - 48f, r.y + 8f, 32f, 26f);
            if (Ui.Btn(clear, "Clear", HudStyles.Button)) Hud.Post(CombatLogHistory.Clear);
            if (Ui.Btn(close, "×", HudStyles.Button, true, "Close (L)")) Hud.Post(() => UiRoot.Close(UiPanels.CombatLog));
            var dragZone = new Rect(r.x, r.y, r.width - 140f, HeaderH);
            if (e.type == EventType.MouseDown && e.button == 0 && dragZone.Contains(e.mousePosition))
            {
                draggingPanel = true;
                dragStart = e.mousePosition;
                offsetStart = offset;
            }
            else if (e.type == EventType.MouseDrag && draggingPanel)
            {
                offset = offsetStart + (real - dragStart);
            }
            else if (e.type == EventType.MouseUp && e.button == 0) draggingPanel = false;

            // body
            var view = new Rect(r.x + 12f, r.y + HeaderH + 4f, r.width - 24f, r.height - HeaderH - 16f);
            HudDraw.Fill(view, new Color(0.03f, 0.02f, 0.07f, 0.45f), 8);
            float textW = view.width - 30f;
            Relayout(textW);
            var lines = CombatLogHistory.Lines;
            float maxScroll = Mathf.Max(0f, contentH + 8f - view.height);
            if (stick) scroll.y = maxScroll;
            scroll.y = Mathf.Clamp(scroll.y, 0f, maxScroll);

            // custom thin scrollbar (drag the thumb)
            var track = new Rect(view.xMax - 10f, view.y + 4f, 6f, view.height - 8f);
            float thumbH = maxScroll > 0f ? Mathf.Max(28f, track.height * view.height / (contentH + 8f)) : track.height;
            float thumbY = maxScroll > 0f ? track.y + (track.height - thumbH) * (scroll.y / maxScroll) : track.y;
            var thumb = new Rect(track.x - 2f, thumbY, 10f, thumbH);
            if (e.type == EventType.MouseDown && e.button == 0 && maxScroll > 0f && new Rect(track.x - 6f, track.y, 18f, track.height).Contains(e.mousePosition))
            {
                draggingThumb = true;
                thumbGrab = e.mousePosition.y - thumbY;
                if (!thumb.Contains(e.mousePosition)) thumbGrab = thumbH * 0.5f;
            }
            else if (e.type == EventType.MouseUp && e.button == 0) draggingThumb = false;
            if (draggingThumb && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && maxScroll > 0f)
            {
                float t = Mathf.Clamp01((real.y - thumbGrab - track.y) / Mathf.Max(1f, track.height - thumbH));
                scroll.y = t * maxScroll;
                stick = scroll.y >= maxScroll - 2f;
            }

            // the wheel scrolls only when the panel itself is under the mouse (the layer's hide is respected): handled
            // here instead of by GUI.EndScrollView, which tests the real cursor after its clip pop
            if (e.type == EventType.ScrollWheel && maxScroll > 0f && view.Contains(e.mousePosition))
            {
                scroll.y = Mathf.Clamp(scroll.y + e.delta.y * 20f, 0f, maxScroll);
                stick = scroll.y >= maxScroll - 2f;
                e.Use();
            }

            var content = new Rect(0f, 0f, view.width - 16f, Mathf.Max(contentH + 8f, view.height));
            scroll = GUI.BeginScrollView(view, scroll, content, false, false, GUIStyle.none, GUIStyle.none);
            HudDraw.Rehide();   // the clip push recomputed the mouse from the real cursor
            if (lines.Count == 0)
            {
                HudDraw.Text(new Rect(8f, 8f, textW, 22f), "Nothing has happened yet.", HudStyles.Small, Hud.Muted);
            }
            else if (e.type == EventType.Repaint)
            {
                float top = scroll.y, bottom = scroll.y + view.height;
                int first = FirstVisible(top);
                var st = HudStyles.LogWrap;
                for (int i = first; i < lines.Count && i < offsets.Count; i++)
                {
                    float y = offsets[i];
                    if (y > bottom) break;
                    GUI.Label(new Rect(8f, y + 4f, textW, heights[i]), lines[i], st);
                }
            }
            GUI.EndScrollView(false);
            HudDraw.Rehide();   // ...and so did the clip pop: windows above keep their clicks (Jump to newest, header)
            if (e.type != EventType.Layout && e.type != EventType.Repaint && !draggingThumb) stick = scroll.y >= maxScroll - 2f;

            if (maxScroll > 0f)
            {
                HudDraw.Fill(track, new Color(1f, 1f, 1f, 0.08f), 3);
                float ty = track.y + (track.height - thumbH) * (scroll.y / maxScroll);
                HudDraw.Fill(new Rect(track.x - 1f, ty, 8f, thumbH), new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, draggingThumb ? 0.9f : 0.55f), 3);
            }
            if (!stick)
            {
                var jr = new Rect(view.xMax - 150f, view.yMax - 30f, 132f, 24f);
                if (Ui.Btn(jr, "Jump to newest", HudStyles.Button)) stick = true;
            }
        }

        void Relayout(float width)
        {
            var lines = CombatLogHistory.Lines;
            bool restyled = laidStyles != HudStyles.Version;   // fonts rebuilt (screen scale changed): measure again
            if (!restyled && laidVersion == CombatLogHistory.Version && Mathf.Abs(width - laidWidth) < 0.5f) return;
            laidStyles = HudStyles.Version;
            var st = HudStyles.LogWrap;
            bool full = restyled || Mathf.Abs(width - laidWidth) >= 0.5f || CombatLogHistory.Dropped < laidDropped || heights.Count > lines.Count + (CombatLogHistory.Dropped - laidDropped);
            if (!full)
            {
                int drop = CombatLogHistory.Dropped - laidDropped;
                if (drop > 0) heights.RemoveRange(0, Mathf.Min(drop, heights.Count));
                if (heights.Count > lines.Count) full = true;
            }
            if (full) heights.Clear();
            for (int i = heights.Count; i < lines.Count; i++)
            {
                measure.text = lines[i];
                heights.Add(Mathf.Ceil(st.CalcHeight(measure, width)) + 1f);
            }
            offsets.Clear();
            float y = 0f;
            for (int i = 0; i < heights.Count; i++) { offsets.Add(y); y += heights[i]; }
            contentH = y;
            laidVersion = CombatLogHistory.Version;
            laidDropped = CombatLogHistory.Dropped;
            laidWidth = width;
        }

        int FirstVisible(float top)
        {
            int lo = 0, hi = offsets.Count - 1, ans = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (offsets[mid] <= top) { ans = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return ans;
        }
    }

    /// <summary>The always-visible last four log lines during combat (bottom left).</summary>
    public sealed class CompactCombatLogHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderCompactLog;
        public bool Visible => Hud.CombatHud && !UiRoot.IsOpen(UiPanels.CombatLog);
        public bool Modal => false;

        const int Shown = 4;

        public void Tick(float dt) => CombatLogHistory.Update();

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try
            {
                HudStyles.Ensure();
                var r = HudLayout.CompactLog;
                var lines = CombatLogHistory.Lines;
                int start = CombatLogHistory.BattleStart;
                if (start < 0) return;
                HudDraw.Fill(r, new Color(0.08f, 0.06f, 0.14f, 0.55f), 8);
                HudDraw.Ring(r, new Color(1f, 1f, 1f, 0.12f), 8);
                Ui.Block(r);
                int from = Mathf.Max(start, lines.Count - Shown);
                float lh = (r.height - 12f) / Shown;
                float y = r.y + 6f + (Shown - (lines.Count - from)) * lh;
                if (HudDraw.IsRepaint)
                {
                    var st = HudStyles.LogLine;
                    var old = GUI.color;
                    for (int i = from; i < lines.Count; i++)
                    {
                        int age = lines.Count - 1 - i;
                        GUI.color = new Color(1f, 1f, 1f, age == 0 ? 1f : age == 1 ? 0.88f : age == 2 ? 0.74f : 0.6f);
                        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, lh), lines[i], st);
                        y += lh;
                    }
                    GUI.color = old;
                }
                if (HudDraw.Hover(r)) Ui.TooltipFor(r, "Combat log — click or press L for the full log.");
                if (HudDraw.Click(r)) Hud.Post(() => UiRoot.Open(UiPanels.CombatLog));
            }
            catch (Exception e) { Hud.LogOnce("minilog:" + e.GetType().Name, "Compact log: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }
    }
}
