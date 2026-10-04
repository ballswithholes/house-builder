// Shared kit for every window in Scripts/Game/UI/Panels (menus, sheets, dialogue, trade…).
//
// * Access: Flow / Sess / Db shortcuts (UI talks only to GameFlow, its Session and Combat).
// * Layers: IMGUI gives a click to the FIRST control drawn under the mouse, i.e. to the screen BELOW. Every panel
//   screen wraps its Draw in BeginLayer(Order)/EndLayer and registers its rects with Occlude(rect, Order); while the
//   mouse is over a rect of a higher screen (last frame's rects) the lower screen sees the mouse "nowhere", so it gets
//   no clicks, hovers, wheel or tooltips. Modal screens occlude the whole screen.
// * Deferred commands: buttons never mutate session state inside OnGUI; they queue a command with Do(...) that the
//   PanelHost runs in the next Update (no "collection modified" surprises, no layout changes mid-event).
// * Notices (error/feedback lines → GameFlow.Toast, the single toast lane), confirm dialogs and context menus (PanelHost.cs).
// * Styles, item/portrait/money helpers, windows, tabs, scroll views with a painted scrollbar, member tabs.
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public static class PanelKit
    {
        // ================================================================ access

        public static GameFlow Flow => GameFlow.Instance;
        public static GameSession Sess { get { var f = GameFlow.Instance; return f != null ? f.Session : null; } }
        public static GameDatabase Db => GameRoot.Instance != null ? GameRoot.Instance.Db : null;
        public static bool InGame => GameFlow.HasGame;
        public static SessionMode Mode { get { var s = Sess; return s != null && GameFlow.HasGame ? s.Mode : SessionMode.None; } }
        public static bool Exploring => Mode == SessionMode.Exploration;
        public static bool InCombat => Mode == SessionMode.Combat;

        /// <summary>Every session event relayed by GameFlow (subscribed once by the PanelHost).</summary>
        public static event Action<SessionEvent> Events;

        internal static void RaiseEvent(SessionEvent e)
        {
            var h = Events;
            if (h == null || e == null) return;
            foreach (Action<SessionEvent> d in h.GetInvocationList())
            {
                try { d(e); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        // ================================================================ error logging

        static readonly HashSet<string> logged = new HashSet<string>();

        /// <summary>Logs a drawing/tick exception once per screen and message (no per-frame log spam).</summary>
        public static void LogOnce(object owner, Exception e)
        {
            if (e == null) return;
            string key = (owner != null ? owner.GetType().Name : "?") + ":" + e.GetType().Name + ":" + e.Message;
            if (logged.Count > 256) logged.Clear();
            if (!logged.Add(key)) return;
            Debug.LogError("[Lanternvale UI] " + key + "\n" + e);
        }

        // ================================================================ deferred commands

        static readonly List<Action> deferred = new List<Action>();
        static readonly List<Action> running = new List<Action>();

        /// <summary>Queues a command for the next Update (never mutate the session inside OnGUI).</summary>
        public static void Do(Action a)
        {
            if (a != null) deferred.Add(a);
        }

        internal static void RunDeferred()
        {
            if (deferred.Count == 0) return;
            running.AddRange(deferred);
            deferred.Clear();
            for (int i = 0; i < running.Count; i++)
            {
                try { running[i](); }
                catch (Exception e) { Debug.LogException(e); Notice("Something went wrong."); }
            }
            running.Clear();
        }

        // ================================================================ notices

        static readonly Color NoticeError = Ui.Hex("#ff8a7a");
        static readonly Color NoticeGood = Ui.Hex("#ffe08a");

        /// <summary>
        /// Shows a short feedback line — errors in red, confirmations in gold — in the game's single toast lane
        /// (GameFlow.Toast(text, colour) → the HUD's ToastsHud/ToastLaneHud, drawn above windows and menus).
        /// </summary>
        public static void Notice(string text, bool error = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            var f = GameFlow.Instance;
            if (f != null) f.Toast(text, error ? NoticeError : NoticeGood);
            else Debug.Log("[Lanternvale] " + text);
            if (error) Ui.Sfx?.Invoke("ui_close");
        }

        /// <summary>Runs a command that returns null or a reason; the reason becomes a notice. Returns true on success.</summary>
        public static bool Try(Func<string> command, string success = null)
        {
            string why;
            try { why = command(); }
            catch (Exception e) { Debug.LogException(e); why = "Something went wrong."; }
            if (why != null) { Notice(why); return false; }
            if (!string.IsNullOrEmpty(success)) Notice(success, false);
            return true;
        }

        // ================================================================ layers / occlusion

        struct Occ
        {
            public int Order;
            public Rect Rect;
        }

        static List<Occ> occNow = new List<Occ>(32), occLast = new List<Occ>(32);
        static readonly Vector2 Nowhere = new Vector2(-100000f, -100000f);

        /// <summary>Called once per frame (Update) by the host: rects drawn last frame become the hit-test set.</summary>
        internal static void RollOccluders()
        {
            var t = occLast;
            occLast = occNow;
            occNow = t;
            occNow.Clear();
        }

        /// <summary>Registers a top-level rect drawn by the screen with this order (call every frame while drawn).</summary>
        public static void Occlude(Rect r, int order)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            occNow.Add(new Occ { Order = order, Rect = r });
        }

        /// <summary>Full-screen occluder (modal screens).</summary>
        public static void OccludeAll(int order) => Occlude(new Rect(-10f, -10f, Ui.Width + 20f, Ui.Height + 20f), order);

        /// <summary>True when the GUI point is covered by a screen above `order` (last frame).</summary>
        public static bool CoveredAbove(int order, Vector2 guiPoint)
        {
            for (int i = 0; i < occLast.Count; i++)
                if (occLast[i].Order > order && occLast[i].Rect.Contains(guiPoint)) return true;
            return false;
        }

        public struct MouseHide
        {
            internal bool Hidden;
            internal Vector2 Saved;
            internal bool Layer;
            internal Color Color;
            internal bool Enabled;
            internal Matrix4x4 Matrix;
        }

        /// <summary>
        /// Hides the mouse from this screen when a higher screen covers it, and remembers GUI.color / enabled / matrix so
        /// EndLayer restores them even when the screen's drawing failed half-way. Pair with EndLayer (in a finally).
        /// </summary>
        public static MouseHide BeginLayer(int order)
        {
            var h = new MouseHide { Layer = true, Color = GUI.color, Enabled = GUI.enabled, Matrix = GUI.matrix };
            var e = Event.current;
            if (e == null) return h;
            var mp = e.mousePosition;
            if (CoveredAbove(order, mp)) HideMouse(ref h);
            return h;
        }

        public static void EndLayer(MouseHide h)
        {
            if (h.Layer)
            {
                GUI.color = h.Color;
                GUI.enabled = h.Enabled;
                if (GUI.matrix != h.Matrix) { GUI.matrix = h.Matrix; Rehide(); }
            }
            RestoreMouse(h);
        }

        // Hidden scopes nest (a covered screen with a scroll view whose rows are out of sight). The real position is kept
        // in screen space so it can be restored under any clip/matrix; IMGUI may recompute Event.mousePosition when a clip
        // is pushed/popped or GUI.matrix changes, so code that does either inside a layer calls Rehide() afterwards.
        static int hideDepth;
        static Vector2 hiddenScreenPos;
        static int hideFrame = -1;

        static void HideMouse(ref MouseHide h)
        {
            var e = Event.current;
            if (e == null || h.Hidden) return;
            if (hideFrame != Time.frameCount) { hideFrame = Time.frameCount; hideDepth = 0; }   // safety: never leak across frames
            if (hideDepth == 0) hiddenScreenPos = GUIUtility.GUIToScreenPoint(e.mousePosition);
            hideDepth++;
            h.Hidden = true;
            e.mousePosition = Nowhere;
        }

        static void RestoreMouse(MouseHide h)
        {
            if (!h.Hidden) return;
            var e = Event.current;
            hideDepth = Mathf.Max(0, hideDepth - 1);
            if (e == null) return;
            e.mousePosition = hideDepth > 0 ? Nowhere : GUIUtility.ScreenToGUIPoint(hiddenScreenPos);
        }

        /// <summary>Re-applies an active mouse hide after a clip push/pop or a GUI.matrix change.</summary>
        public static void Rehide()
        {
            var e = Event.current;
            if (e != null && hideDepth > 0 && hideFrame == Time.frameCount) e.mousePosition = Nowhere;
        }

        public static Vector2 Mouse => Event.current != null ? Event.current.mousePosition : Nowhere;

        /// <summary>
        /// The real cursor in the current GUI space, also while a covering screen (BeginLayer) or a scroll viewport
        /// (BeginScroll) hides it. For drags that already own the mouse (slider knobs, scrollbar thumbs) and must keep
        /// following it when it leaves the viewport or crosses another window; never use it to start a click or a hover.
        /// </summary>
        public static Vector2 RealMouse
        {
            get
            {
                var e = Event.current;
                if (e == null) return Nowhere;
                if (hideDepth > 0 && hideFrame == Time.frameCount) return GUIUtility.ScreenToGUIPoint(hiddenScreenPos);
                return e.mousePosition;
            }
        }

        public static bool Hover(Rect r) => Event.current != null && r.Contains(Event.current.mousePosition);
        public static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;

        /// <summary>Frame of the last click handled by a panel (context menus close on any later click).</summary>
        public static int LastClickFrame { get; private set; } = -1;

        /// <summary>
        /// Mouse-down click inside r (any button). Consumes the MouseDown (never the MouseUp, so the input driver keeps
        /// its button state). button: 0 left, 1 right.
        /// </summary>
        public static bool Click(Rect r, out int button)
        {
            button = -1;
            var e = Event.current;
            if (e == null || e.type != EventType.MouseDown || !r.Contains(e.mousePosition)) return false;
            button = e.button;
            LastClickFrame = Time.frameCount;
            e.Use();
            return true;
        }

        public static bool LeftClick(Rect r)
        {
            if (!Click(r, out var b)) return false;
            return b == 0;
        }

        // ================================================================ styles

        public static GUIStyle Text, TextSmall, TextTiny, TextBold, TextBoldSmall, TextMuted, TextMutedSmall, TextCenter,
            TextSmallCenter, TextRight, TextSmallRight, TextItalic, Heading, HeadingCenter, TitleDark, TitleCenterDark,
            LText, LTextSmall, LTextBold, LTextCenter, LTextSmallCenter, LMuted, LTitle, LHeading, CloseButton, TabOn,
            TabOff, SmallButton, SmallButtonGold, SmallButtonDark, RowText, RowTextSmall, ChoiceText;
        static bool built;
        static Font builtFor;

        /// <summary>Builds the kit's styles (after Ui.BeginFrame built the toolkit). Safe to call every frame.</summary>
        public static void EnsureStyles()
        {
            if (built && Text != null && builtFor == Ui.BodyFont && Ui.Label != null && SmallButton != null && SmallButton.normal.background != null) return;
            if (Ui.Label == null) return;
            built = true;
            builtFor = Ui.BodyFont;
            var ink = Ui.Ink;
            Text = Make(Ui.LabelDark, 19, ink);
            TextSmall = Make(Ui.LabelDark, 16, ink);
            TextTiny = Make(Ui.LabelDark, 13, ink);
            TextBold = Make(Ui.LabelDark, 19, ink, Ui.BoldFont);
            TextBoldSmall = Make(Ui.LabelDark, 16, ink, Ui.BoldFont);
            TextMuted = Make(Ui.LabelDark, 18, Ui.InkSoft);
            TextMutedSmall = Make(Ui.LabelDark, 15, Ui.InkSoft);
            TextCenter = Make(Ui.LabelDark, 19, ink, null, TextAnchor.MiddleCenter);
            TextSmallCenter = Make(Ui.LabelDark, 16, ink, null, TextAnchor.MiddleCenter);
            TextRight = Make(Ui.LabelDark, 19, ink, null, TextAnchor.MiddleRight, false);
            TextSmallRight = Make(Ui.LabelDark, 16, ink, null, TextAnchor.MiddleRight, false);
            TextItalic = Make(Ui.LabelDark, 18, Ui.InkSoft);
            TextItalic.fontStyle = FontStyle.Italic;
            Heading = Make(Ui.HeaderDark, 26, ink, Ui.TitleFont);
            Heading.wordWrap = false;
            HeadingCenter = Make(Heading, 26, ink, Ui.TitleFont, TextAnchor.MiddleCenter, false);
            TitleDark = Make(Ui.HeaderDark, 34, ink, Ui.TitleFont, TextAnchor.MiddleLeft, false);
            TitleCenterDark = Make(Ui.HeaderDark, 34, ink, Ui.TitleFont, TextAnchor.MiddleCenter, false);

            LText = Make(Ui.Label, 19, Ui.TextLight);
            LTextSmall = Make(Ui.Label, 16, Ui.TextLight);
            LTextBold = Make(Ui.Label, 19, Ui.TextLight, Ui.BoldFont);
            LTextCenter = Make(Ui.Label, 19, Ui.TextLight, null, TextAnchor.MiddleCenter);
            LTextSmallCenter = Make(Ui.Label, 16, Ui.TextLight, null, TextAnchor.MiddleCenter);
            LMuted = Make(Ui.Label, 16, Ui.TextMuted);
            LTitle = Make(Ui.Title, 44, Ui.TextLight, Ui.TitleFont, TextAnchor.MiddleCenter, false);
            LHeading = Make(Ui.Header, 26, Ui.Gold, Ui.TitleFont, TextAnchor.MiddleLeft, false);

            RowText = Make(Ui.LabelDark, 18, ink, Ui.BoldFont, TextAnchor.MiddleLeft, false);
            RowText.clipping = TextClipping.Clip;
            RowTextSmall = Make(Ui.LabelDark, 15, Ui.InkSoft, null, TextAnchor.MiddleLeft, false);
            RowTextSmall.clipping = TextClipping.Clip;
            ChoiceText = Make(Ui.LabelDark, 19, ink, Ui.BodyFont, TextAnchor.UpperLeft, true);

            CloseButton = new GUIStyle(Ui.Button) { fontSize = 22, padding = new RectOffset(0, 0, 0, 3) };
            SmallButton = new GUIStyle(Ui.Button) { fontSize = 16, padding = new RectOffset(10, 10, 4, 6) };
            SmallButtonGold = new GUIStyle(Ui.ButtonGold) { fontSize = 16, padding = new RectOffset(10, 10, 4, 6) };
            SmallButtonDark = new GUIStyle(Ui.ButtonDark) { fontSize = 16, padding = new RectOffset(10, 10, 4, 6) };
            TabOn = new GUIStyle(Ui.ButtonGold) { fontSize = 17, padding = new RectOffset(12, 12, 4, 6) };
            TabOff = new GUIStyle(Ui.Button) { fontSize = 17, padding = new RectOffset(12, 12, 4, 6) };
        }

        static GUIStyle Make(GUIStyle from, int size, Color color, Font font = null, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = true)
        {
            var s = new GUIStyle(from) { fontSize = size, alignment = anchor, wordWrap = wrap, richText = true };
            if (font != null) s.font = font;
            s.normal.textColor = color;
            s.hover.textColor = color;
            s.active.textColor = color;
            s.padding = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }

        // ================================================================ colours

        public static readonly Color RowHover = new Color(1f, 0.86f, 0.55f, 0.32f);
        public static readonly Color RowSelected = new Color(1f, 0.80f, 0.40f, 0.45f);
        public static readonly Color RowShade = new Color(0.55f, 0.42f, 0.25f, 0.08f);
        public static readonly Color Divider = new Color(0.17f, 0.13f, 0.22f, 0.22f);
        public static readonly Color GoodDark = Ui.Hex("#2f8a3a");
        public static readonly Color BadDark = Ui.Hex("#c0392b");
        public static readonly Color GoldInk = Ui.Hex("#9a6b1c");

        // ================================================================ drawing primitives

        static Texture2D fillTex, ringTex, fillSmallTex;

        public static Texture2D Fill => fillTex != null ? fillTex : (fillTex = ProceduralArt.RoundedRect(48, 10, Color.white, Color.white, 0));
        public static Texture2D FillSmall => fillSmallTex != null ? fillSmallTex : (fillSmallTex = ProceduralArt.RoundedRect(32, 6, Color.white, Color.white, 0));
        public static Texture2D Ring => ringTex != null ? ringTex : (ringTex = ProceduralArt.RoundedRect(48, 10, new Color(1f, 1f, 1f, 0f), Color.white, 3));

        /// <summary>Draws a tinted texture (Repaint only).</summary>
        public static void Tex(Rect r, Texture t, Color c, ScaleMode mode = ScaleMode.StretchToFill)
        {
            if (t == null || !IsRepaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t, mode);
            GUI.color = old;
        }

        public static void Rect(Rect r, Color c) => Tex(r, ProceduralArt.White, c);
        public static void Rounded(Rect r, Color c) => Tex(r, r.height < 30f ? FillSmall : Fill, c);
        public static void Outline(Rect r, Color c) => Tex(r, Ring, c);

        public static void HLine(float x, float y, float w, Color? c = null) => Rect(new Rect(x, y, w, 1.5f), c ?? Divider);

        /// <summary>A thin divider with a small gold diamond in the middle (window title ornament).</summary>
        public static void Ornament(float x, float y, float w)
        {
            if (!IsRepaint) return;
            HLine(x, y, w * 0.5f - 12f, new Color(0.17f, 0.13f, 0.22f, 0.28f));
            HLine(x + w * 0.5f + 12f, y, w * 0.5f - 12f, new Color(0.17f, 0.13f, 0.22f, 0.28f));
            Tex(new Rect(x + w * 0.5f - 7f, y - 6f, 14f, 14f), PanelArt.Diamond, Ui.GoldDeep);
        }

        public static void Label(Rect r, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUI.Label(r, text, style);
        }

        public static void Label(Rect r, string text, GUIStyle style, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            var old = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        static readonly GUIContent tmpContent = new GUIContent();

        public static float TextHeight(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || style == null) return 0f;
            tmpContent.text = text;
            return style.CalcHeight(tmpContent, width);
        }

        public static float TextWidth(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text) || style == null) return 0f;
            tmpContent.text = text;
            return style.CalcSize(tmpContent).x;
        }

        // ================================================================ windows

        /// <summary>
        /// Paper window with a title, an ornament and a close button. Registers the occluder and the world-click
        /// blocker. Returns the content rect.
        /// </summary>
        public static Rect Window(Rect r, string title, int order, out bool close, string subtitle = null)
        {
            Ui.Panel(r);
            Occlude(r, order);
            float x = r.x + 26f;
            Label(new Rect(x, r.y + 14f, r.width - 110f, 40f), title, TitleDark);
            if (!string.IsNullOrEmpty(subtitle))
            {
                float tw = TextWidth(title, TitleDark);
                Label(new Rect(x + tw + 14f, r.y + 24f, r.width - tw - 140f, 26f), subtitle, TextMuted);
            }
            close = Ui.Btn(new Rect(r.xMax - 54f, r.y + 14f, 38f, 36f), "×", CloseButton, true, "Close (Esc)");
            Ornament(r.x + 22f, r.y + 60f, r.width - 44f);
            return new Rect(r.x + 24f, r.y + 74f, r.width - 48f, r.height - 92f);
        }

        /// <summary>Window without a close button (modal prompts).</summary>
        public static Rect Frame(Rect r, string title, int order, GUIStyle titleStyle = null)
        {
            Ui.Panel(r);
            Occlude(r, order);
            if (!string.IsNullOrEmpty(title))
            {
                Label(new Rect(r.x + 20f, r.y + 14f, r.width - 40f, 40f), title, titleStyle ?? TitleCenterDark);
                Ornament(r.x + 22f, r.y + 60f, r.width - 44f);
                return new Rect(r.x + 24f, r.y + 74f, r.width - 48f, r.height - 92f);
            }
            return new Rect(r.x + 24f, r.y + 20f, r.width - 48f, r.height - 40f);
        }

        /// <summary>Clamps a window rect into the screen (8 px margin).</summary>
        public static Rect Fit(Rect r)
        {
            float w = Ui.Width, h = Ui.Height;
            r.width = Mathf.Min(r.width, w - 16f);
            r.height = Mathf.Min(r.height, h - 16f);
            r.x = Mathf.Clamp(r.x, 8f, Mathf.Max(8f, w - r.width - 8f));
            r.y = Mathf.Clamp(r.y, 8f, Mathf.Max(8f, h - r.height - 8f));
            return r;
        }

        public static Rect Centered(float w, float h, float dy = 0f) => Fit(new Rect((Ui.Width - w) * 0.5f, (Ui.Height - h) * 0.5f + dy, w, h));

        /// <summary>
        /// Full-screen layouts are designed on a fixed canvas (e.g. 1920×1080) and scaled down to fit narrower screens
        /// (centred when wider). Returns the previous GUI.matrix for EndCanvas. Mouse positions follow the matrix.
        /// </summary>
        public static Matrix4x4 BeginCanvas(float designW, float designH, out Rect canvas)
        {
            var old = GUI.matrix;
            float s = Mathf.Min(1f, Mathf.Min(Ui.Width / designW, Ui.Height / designH));
            float ox = (Ui.Width - designW * s) * 0.5f, oy = (Ui.Height - designH * s) * 0.5f;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(s, s, 1f));
            Rehide();
            canvas = new Rect(0f, 0f, designW, designH);
            return old;
        }

        public static void EndCanvas(Matrix4x4 old)
        {
            GUI.matrix = old;
            Rehide();
        }

        /// <summary>Sets GUI.matrix and keeps an active mouse hide.</summary>
        public static void SetMatrix(Matrix4x4 m)
        {
            GUI.matrix = m;
            Rehide();
        }

        // ================================================================ tabs & buttons

        /// <summary>Row of tab buttons. Returns the selected index.</summary>
        public static int Tabs(Rect r, string[] labels, int selected, float minWidth = 90f)
        {
            if (labels == null || labels.Length == 0) return selected;
            float x = r.x;
            for (int i = 0; i < labels.Length; i++)
            {
                float w = Mathf.Max(minWidth, TextWidth(labels[i], TabOff) + 30f);
                if (Ui.Btn(new Rect(x, r.y, w, r.height), labels[i], i == selected ? TabOn : TabOff) && i != selected) selected = i;
                x += w + 6f;
            }
            return selected;
        }

        /// <summary>
        /// Button (also inside scroll views): Ui.Btn. GameInput.BlockRectGui converts through GUIUtility.GUIToScreenPoint
        /// and is cut to the scroll viewport (BeginScroll registers it as a block clip), so the blocker lands where the
        /// button is on screen and rows scrolled out of sight block nothing.
        /// </summary>
        public static bool Btn(Rect r, string text, GUIStyle style = null, bool enabled = true, string tip = null) =>
            Ui.Btn(r, text, style, enabled, tip);

        /// <summary>Small inline button with a tooltip and a disabled reason.</summary>
        public static bool SmallBtn(Rect r, string text, bool enabled = true, string tip = null, GUIStyle style = null) =>
            Ui.Btn(r, text, style ?? SmallButton, enabled, tip);

        // ================================================================ scroll views

        /// <summary>Scroll state owned by a screen (one per list).</summary>
        public sealed class ScrollState
        {
            public Vector2 Pos;
            public float ContentHeight;
            internal Rect Viewport;
            internal MouseHide Hide;
            internal int DragId;
            internal float DragOffset;
            internal bool Dragging;

            public void ScrollTo(float y) => Pos.y = Mathf.Max(0f, y);
            public void Reset() => Pos = Vector2.zero;

            /// <summary>Visible content rect (content coordinates) during the scroll.</summary>
            public Rect Visible => new Rect(Pos.x, Pos.y, Viewport.width, Viewport.height);

            /// <summary>True when a content-space rect intersects the viewport (skip drawing rows outside).</summary>
            public bool IsVisible(Rect contentRect) => contentRect.yMax >= Pos.y - 2f && contentRect.y <= Pos.y + Viewport.height + 2f;
        }

        const float ScrollbarWidth = 10f;
        static int dragIds;

        /// <summary>
        /// Begins a scroll view with a painted scrollbar. The mouse is hidden from the content while it is outside the
        /// viewport (no clicks on rows scrolled out of sight); the wheel scrolls only when the mouse is over the viewport
        /// and the window is not covered there (see EndScroll). Returns the content width.
        /// </summary>
        public static float BeginScroll(Rect r, ScrollState st, float contentHeight)
        {
            st.ContentHeight = Mathf.Max(0f, contentHeight);
            st.Viewport = r;
            float maxY = Mathf.Max(0f, st.ContentHeight - r.height);
            st.Pos.y = Mathf.Clamp(st.Pos.y, 0f, maxY);
            bool bar = st.ContentHeight > r.height + 0.5f;
            float w = r.width - (bar ? ScrollbarWidth + 6f : 0f);
            st.Hide = new MouseHide();
            var e = Event.current;
            bool outside = e != null && !r.Contains(e.mousePosition) && !st.Dragging;
            GameInput.BeginBlockClip(r);   // world-click blockers of the rows are cut to the viewport
            st.Pos = GUI.BeginScrollView(r, st.Pos, new Rect(0f, 0f, w, Mathf.Max(st.ContentHeight, r.height)), false, false, GUIStyle.none, GUIStyle.none);
            Rehide();
            if (outside) HideMouse(ref st.Hide);
            return w;
        }

        public static void EndScroll(ScrollState st)
        {
            // Unity's own wheel handling (EndScrollView(true)) runs right after the clip pop, which recomputes
            // Event.mousePosition from the real cursor: a list in a window covered by another one would scroll and eat
            // the wheel meant for the window on top. So the wheel is handled here, after the layer's hide is re-applied.
            GUI.EndScrollView(false);
            GameInput.EndBlockClip();
            Rehide();
            RestoreMouse(st.Hide);
            st.Hide = new MouseHide();
            var r = st.Viewport;
            var ev = Event.current;
            if (ev != null && ev.type == EventType.ScrollWheel && r.Contains(ev.mousePosition))
            {
                float maxScroll = Mathf.Max(0f, st.ContentHeight - r.height);
                if (maxScroll > 0.5f)
                {
                    st.Pos.y = Mathf.Clamp(st.Pos.y + ev.delta.y * 20f, 0f, maxScroll);
                    ev.Use();
                }
            }
            if (st.ContentHeight <= r.height + 0.5f) { st.Dragging = false; return; }
            // painted scrollbar: track + thumb, draggable
            var track = new Rect(r.xMax - ScrollbarWidth, r.y + 2f, ScrollbarWidth, r.height - 4f);
            float thumbH = Mathf.Max(28f, track.height * r.height / st.ContentHeight);
            float maxY = st.ContentHeight - r.height;
            float t = maxY > 0f ? st.Pos.y / maxY : 0f;
            var thumb = new Rect(track.x, track.y + (track.height - thumbH) * t, track.width, thumbH);
            if (IsRepaint)
            {
                Rounded(track, new Color(0.17f, 0.13f, 0.22f, 0.10f));
                Rounded(thumb, st.Dragging || Hover(thumb) ? new Color(0.36f, 0.30f, 0.45f, 0.85f) : new Color(0.36f, 0.30f, 0.45f, 0.55f));
            }
            var e = Event.current;
            if (e == null) return;
            if (st.DragId == 0) st.DragId = 0x2A6C0000 + (++dragIds);
            if (st.Dragging && e.rawType == EventType.MouseUp && e.type == EventType.Used)
            {
                // released over another control that used the event: still end the drag
                st.Dragging = false;
                if (GUIUtility.hotControl == st.DragId) GUIUtility.hotControl = 0;
                return;
            }
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && new Rect(track.x - 4f, track.y, track.width + 8f, track.height).Contains(e.mousePosition))
                    {
                        st.Dragging = true;
                        st.DragOffset = thumb.Contains(e.mousePosition) ? e.mousePosition.y - thumb.y : thumbH * 0.5f;
                        GUIUtility.hotControl = st.DragId;
                        DragTo(st, track, thumbH, e.mousePosition.y);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (st.Dragging && GUIUtility.hotControl == st.DragId)
                    {
                        // the real cursor: a window above this one hides the mouse from the layer (Nowhere would jump
                        // the list to the top while the drag crosses that window)
                        DragTo(st, track, thumbH, RealMouse.y);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (st.Dragging)
                    {
                        st.Dragging = false;
                        if (GUIUtility.hotControl == st.DragId) GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        static void DragTo(ScrollState st, Rect track, float thumbH, float mouseY)
        {
            float range = Mathf.Max(1f, track.height - thumbH);
            float t = Mathf.Clamp01((mouseY - st.DragOffset - track.y) / range);
            st.Pos.y = t * Mathf.Max(0f, st.ContentHeight - st.Viewport.height);
        }

        // ================================================================ units & portraits

        public static string PortraitOf(Unit u)
        {
            if (u == null) return "";
            if (!string.IsNullOrEmpty(u.Portrait)) return u.Portrait;
            if (u.Companion != null && !string.IsNullOrEmpty(u.Companion.portrait)) return u.Companion.portrait;
            if (u.Class != null && !string.IsNullOrEmpty(u.Class.portrait)) return u.Class.portrait;
            if (u.Creature != null) return !string.IsNullOrEmpty(u.Creature.portrait) ? u.Creature.portrait : u.Creature.sprite;
            return u.Class != null ? "portrait_" + u.Class.id.ToString().ToLowerInvariant() : "";
        }

        public static string SpriteOf(Unit u)
        {
            if (u == null) return "";
            if (!string.IsNullOrEmpty(u.Sprite)) return u.Sprite;
            if (u.Companion != null && !string.IsNullOrEmpty(u.Companion.sprite)) return u.Companion.sprite;
            if (u.Class != null && !string.IsNullOrEmpty(u.Class.sprite)) return u.Class.sprite;
            if (u.Creature != null) return u.Creature.sprite;
            return "";
        }

        public static Color ColorOf(Unit u)
        {
            if (u == null) return Ui.Gold;
            if (u.Class != null) return Ui.ClassColor(u.Class.id);
            if (u.Owner != null && u.Owner.Class != null) return Color.Lerp(Ui.ClassColor(u.Owner.Class.id), Color.white, 0.3f);
            return Ui.Hex("#d6c7a8");
        }

        /// <summary>Class colour readable on paper (very light class colours are darkened).</summary>
        public static Color InkColorOf(ClassId c)
        {
            var col = Ui.ClassColor(c);
            float lum = col.r * 0.3f + col.g * 0.59f + col.b * 0.11f;
            return lum > 0.62f ? Color.Lerp(col, Ui.Ink, 0.45f) : Color.Lerp(col, Ui.Ink, 0.12f);
        }

        public static string NameOf(Unit u)
        {
            if (u == null) return "";
            var f = Flow;
            if (f != null) { try { var n = f.NameOf(u); if (!string.IsNullOrEmpty(n)) return n; } catch (Exception) { } }
            return u.Name ?? "";
        }

        /// <summary>Active party characters (no pets), main character first.</summary>
        public static IReadOnlyList<Unit> Characters
        {
            get
            {
                var s = Sess;
                return s != null ? s.Party : (IReadOnlyList<Unit>)Array.Empty<Unit>();
            }
        }

        static Unit member;
        static Unit lastFlowSelected;

        /// <summary>The party member the character sheet, bags, spellbook and talents show (follows GameFlow.Selected).</summary>
        public static Unit Member
        {
            get
            {
                var s = Sess;
                if (s == null) { member = null; return null; }
                var f = Flow;
                var sel = f != null ? f.Selected : null;
                if (sel != lastFlowSelected)
                {
                    lastFlowSelected = sel;
                    var c = sel != null && sel.Class == null && sel.Owner != null ? sel.Owner : sel;
                    if (c != null && c.Class != null && Contains(s.Roster, c)) member = c;
                }
                if (member == null || !Contains(s.Roster, member)) member = s.Leader != null && s.Leader.Class != null ? s.Leader : s.Main;
                return member;
            }
            set { if (value != null && value.Class != null) member = value; }
        }

        static bool Contains(IReadOnlyList<Unit> list, Unit u)
        {
            if (list == null || u == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] == u) return true;
            return false;
        }

        /// <summary>Row of portrait tabs for the party characters. Returns the (possibly new) selected unit.</summary>
        public static Unit MemberTabs(Rect r, Unit selected, IReadOnlyList<Unit> members = null, float size = 56f)
        {
            members = members ?? Characters;
            float x = r.x;
            for (int i = 0; i < members.Count; i++)
            {
                var u = members[i];
                if (u == null) continue;
                var pr = new Rect(x, r.y, size, size);
                bool sel = u == selected;
                if (sel) Tex(new Rect(pr.x - 8f, pr.y - 8f, pr.width + 16f, pr.height + 16f), ProceduralArt.Glow, new Color(1f, 0.82f, 0.45f, 0.9f));
                Ui.Portrait(pr, PortraitOf(u), sel ? Ui.Gold : ColorOf(u), !sel && !Hover(pr));
                if (sel) Outline(pr, Ui.GoldDeep);
                Ui.TooltipFor(pr, MemberTip(u));
                GameInput.BlockRectGui(pr);
                if (LeftClick(pr) && u != selected)
                {
                    selected = u;
                    Ui.Sfx?.Invoke("ui_click");
                }
                x += size + 10f;
            }
            return selected;
        }

        static readonly Dictionary<Unit, string> memberTips = new Dictionary<Unit, string>();
        static readonly Dictionary<Unit, int> memberTipLevel = new Dictionary<Unit, int>();

        static string MemberTip(Unit u)
        {
            if (memberTips.TryGetValue(u, out var t) && memberTipLevel.TryGetValue(u, out var l) && l == u.Level) return t;
            if (memberTips.Count > 32) { memberTips.Clear(); memberTipLevel.Clear(); }
            t = $"<b>{NameOf(u)}</b>\nLevel {u.Level} {(u.Class != null ? u.Class.name : "")}";
            memberTips[u] = t;
            memberTipLevel[u] = u.Level;
            return t;
        }

        // ================================================================ items

        public static string ItemGlyph(ItemDef d)
        {
            if (d == null) return "";
            if (!string.IsNullOrEmpty(d.icon)) return d.icon;
            switch (d.weaponType)
            {
                case WeaponType.Dagger: return "dagger";
                case WeaponType.FistWeapon: return "fist";
                case WeaponType.OneHandAxe: case WeaponType.TwoHandAxe: return "axe";
                case WeaponType.OneHandMace: case WeaponType.TwoHandMace: return "mace";
                case WeaponType.OneHandSword: case WeaponType.TwoHandSword: return "sword";
                case WeaponType.Polearm: return "spear";
                case WeaponType.Staff: return "staff";
                case WeaponType.Bow: case WeaponType.Crossbow: return "bow";
                case WeaponType.Gun: return "gun";
                case WeaponType.Wand: return "sparkle";
                case WeaponType.Shield: return "shield";
                case WeaponType.HeldInOffhand: return "star";
            }
            switch (d.kind)
            {
                case ItemKind.Food: return "food";
                case ItemKind.Drink: return "drink";
                case ItemKind.Consumable: return "vial";
                case ItemKind.Quest: return "key";
                case ItemKind.Junk: return "coin";
                case ItemKind.Reagent: return "leaf";
                case ItemKind.Accessory: return d.equip == EquipType.Finger ? "halo" : "star";
            }
            return d.equip == EquipType.Feet ? "boot" : d.equip == EquipType.Hands ? "hand" : "armor";
        }

        static readonly string[] countStrings = new string[201];
        static readonly string[] percentStrings = new string[101];

        /// <summary>Cached "0%".."100%".</summary>
        public static string PercentText(int n)
        {
            n = Mathf.Clamp(n, 0, 100);
            return percentStrings[n] ?? (percentStrings[n] = n + "%");
        }

        public static string CountText(int n)
        {
            if (n >= 0 && n < countStrings.Length) return countStrings[n] ?? (countStrings[n] = n.ToString());
            return n.ToString();
        }

        /// <summary>Item icon in a dark slot with a quality frame and a stack count.</summary>
        public static void ItemIcon(Rect r, ItemInstance it, bool dim = false, bool highlight = false)
        {
            if (it == null || it.Def == null) { EmptySlot(r, null); return; }
            var q = Ui.QualityColor(it.Def.quality);
            if (highlight) Tex(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f), ProceduralArt.Glow, new Color(q.r, q.g, q.b, 0.85f));
            Ui.Icon(r, ItemGlyph(it.Def), it.Def.quality <= Quality.Common ? Ui.Hex("#a99b86") : q, 0f, dim);
            if (it.Count > 1)
                Ui.Shadowed(new Rect(r.x + 2f, r.y + r.height * 0.5f, r.width - 6f, r.height * 0.5f - 2f), CountText(it.Count),
                    Ui.NumberStyle(Mathf.Max(12, (int)(r.height * 0.3f)), TextAnchor.LowerRight));
        }

        public static void ItemIcon(Rect r, ItemDef d, int count = 1, bool dim = false)
        {
            if (d == null) { EmptySlot(r, null); return; }
            var q = Ui.QualityColor(d.quality);
            Ui.Icon(r, ItemGlyph(d), d.quality <= Quality.Common ? Ui.Hex("#a99b86") : q, 0f, dim);
            if (count > 1)
                Ui.Shadowed(new Rect(r.x + 2f, r.y + r.height * 0.5f, r.width - 6f, r.height * 0.5f - 2f), CountText(count),
                    Ui.NumberStyle(Mathf.Max(12, (int)(r.height * 0.3f)), TextAnchor.LowerRight));
        }

        public static void EmptySlot(Rect r, string label)
        {
            if (!IsRepaint) return;
            GUI.Box(r, GUIContent.none, Ui.Slot);
            if (!string.IsNullOrEmpty(label))
                Label(r, label, Ui.NumberStyle(Mathf.Clamp((int)(r.height * 0.2f), 10, 14)), new Color(1f, 1f, 1f, 0.38f));
        }

        public static Color QualityInk(Quality q)
        {
            switch (q)
            {
                case Quality.Poor: return Ui.Hex("#7d7d7d");
                case Quality.Common: return Ui.Ink;
                case Quality.Uncommon: return Ui.Hex("#1f9a12");
                case Quality.Rare: return Ui.Hex("#1f6fd0");
                case Quality.Epic: return Ui.Hex("#8a35c9");
                case Quality.Legendary: return Ui.Hex("#d06a00");
                default: return Ui.Ink;
            }
        }

        public static bool IsEquipment(ItemDef d) => d != null && d.equip != EquipType.None;
        public static bool IsUsable(ItemDef d) => d != null && !string.IsNullOrEmpty(d.use);
        public static bool IsQuestItem(ItemDef d) => d != null && (d.kind == ItemKind.Quest || !string.IsNullOrEmpty(d.quest));

        /// <summary>The item currently in the slot the unit would equip `def` into (for comparisons), or null.</summary>
        public static ItemInstance EquippedFor(Unit u, ItemDef def)
        {
            if (u == null || def == null || def.equip == EquipType.None) return null;
            try
            {
                var slots = EquipmentRules.SlotsFor(def);
                if (slots == null || slots.Length == 0) return null;
                // prefer an occupied slot so there is something to compare with
                foreach (var s in slots) { var cur = u.Equipment[s]; if (cur != null) return cur; }
            }
            catch (Exception) { }
            return null;
        }

        static ItemInstance tipItem;
        static Unit tipUnit;
        static string tipText;
        static int tipFrame = -1;
        static int tipStamp;

        /// <summary>Item tooltip with a comparison to the member's equipped item (cached while hovered).</summary>
        public static string ItemTip(ItemInstance it, Unit u, bool compare = true, string extra = null)
        {
            if (it == null || it.Def == null) return null;
            int stamp = (u != null ? u.Level * 131 + u.Equipment.Count : 0) + (extra != null ? extra.Length * 7 : 0) + it.Count * 17;
            if (it == tipItem && u == tipUnit && stamp == tipStamp && Time.frameCount - tipFrame < 30 && tipText != null)
            {
                tipFrame = Time.frameCount;
                return tipText;
            }
            tipItem = it;
            tipUnit = u;
            tipStamp = stamp;
            tipFrame = Time.frameCount;
            var eq = compare ? EquippedFor(u, it.Def) : null;
            if (eq == it) eq = null;
            var sb = new StringBuilder();
            sb.Append(UiText.Item(it, u, null));
            if (u != null && IsEquipment(it.Def))
            {
                string why = null;
                try { why = EquipmentRules.CannotUseReason(u, it.Def); } catch (Exception) { }
                if (why != null) sb.Append('\n').Append(Ui.Rich(why, Ui.Bad));
                else if (eq != null)
                {
                    var diff = CompareText(it, eq);
                    if (diff.Length > 0) sb.Append("\n\n").Append(Ui.Rich("If you replace " + eq.Name + ":", Ui.TextMuted)).Append('\n').Append(diff);
                    sb.Append("\n\n").Append(Ui.Rich("Currently equipped", Ui.TextMuted)).Append('\n').Append(UiText.Item(eq, u, null));
                }
            }
            if (!string.IsNullOrEmpty(extra)) sb.Append("\n\n").Append(extra);
            tipText = sb.ToString();
            return tipText;
        }

        static readonly Dictionary<string, float> diffA = new Dictionary<string, float>(), diffB = new Dictionary<string, float>();
        static readonly List<string> diffKeys = new List<string>();

        /// <summary>"+3 Stamina, −2 Agility, +12 Armor, +1.4 DPS" (green/red lines).</summary>
        public static string CompareText(ItemInstance next, ItemInstance cur)
        {
            diffA.Clear(); diffB.Clear(); diffKeys.Clear();
            Accumulate(next, diffA);
            Accumulate(cur, diffB);
            foreach (var k in diffA.Keys) if (!diffKeys.Contains(k)) diffKeys.Add(k);
            foreach (var k in diffB.Keys) if (!diffKeys.Contains(k)) diffKeys.Add(k);
            var sb = new StringBuilder();
            foreach (var k in diffKeys)
            {
                diffA.TryGetValue(k, out var a);
                diffB.TryGetValue(k, out var b);
                float d = a - b;
                if (Mathf.Abs(d) < 0.05f) continue;
                bool pct = k.EndsWith("%");
                string name = pct ? k.Substring(0, k.Length - 1) : k;
                string num = Mathf.Abs(d - Mathf.Round(d)) < 0.05f ? Mathf.Round(d).ToString("+0;-0") : d.ToString("+0.0;-0.0");
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Ui.Rich(num + (pct ? "% " : " ") + name, d > 0 ? Ui.Good : Ui.Bad));
            }
            return sb.ToString();
        }

        static void Accumulate(ItemInstance it, Dictionary<string, float> into)
        {
            if (it == null || it.Def == null) return;
            var d = it.Def;
            if (d.armor > 0f) Add(into, "Armor", d.armor);
            if (d.block > 0f) Add(into, "Block", d.block);
            if (d.maxDamage > 0f && d.speed > 0f) Add(into, "DPS", (d.minDamage + d.maxDamage) * 0.5f / d.speed);
            foreach (var s in it.Stats)
            {
                if (s == null) continue;
                var name = UiText.StatName(s.stat, s.school);
                Add(into, s.pct ? name + "%" : name, s.value);
            }
        }

        static void Add(Dictionary<string, float> d, string k, float v)
        {
            d.TryGetValue(k, out var cur);
            d[k] = cur + v;
        }

        // ================================================================ money

        static readonly Dictionary<long, string> moneyCache = new Dictionary<long, string>();

        /// <summary>Cached "1g 23s 45c" rich text.</summary>
        public static string Money(long copper)
        {
            if (moneyCache.TryGetValue(copper, out var s)) return s;
            if (moneyCache.Count > 512) moneyCache.Clear();
            s = Ui.Money(Math.Max(0, copper));
            moneyCache[copper] = s;
            return s;
        }

        /// <summary>Money on paper: the light gold/silver/copper colours are too pale, so a dark plate sits behind it.</summary>
        public static void MoneyPlate(Rect r, long copper, bool alignRight = true)
        {
            var text = Money(copper);
            var st = Ui.NumberStyle(17, alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            float w = TextWidth(text, st) + 24f;
            var plate = alignRight ? new Rect(r.xMax - w, r.y, w, r.height) : new Rect(r.x, r.y, w, r.height);
            Rounded(plate, new Color(0.13f, 0.10f, 0.19f, 0.82f));
            GUI.Label(new Rect(plate.x + 10f, plate.y, plate.width - 20f, plate.height), text, st);
        }

        // ================================================================ formatting

        public static string Pct(float v) => v.ToString(Mathf.Abs(v) >= 100f ? "0" : "0.0") + "%";

        public static string Clock(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            int h = Mathf.FloorToInt(hour);
            int m = Mathf.FloorToInt((hour - h) * 60f);
            return h.ToString("00") + ":" + m.ToString("00");
        }

        public static string PlayTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds / 60f));
            int h = total / 60, m = total % 60;
            return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
        }

        public static string SlotDisplay(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return "Save";
            if (slot == "quick") return "Quicksave";
            if (slot == "auto") return "Autosave";
            if (slot.StartsWith("slot") && slot.Length > 4) return "Slot " + slot.Substring(4);
            return char.ToUpperInvariant(slot[0]) + slot.Substring(1);
        }

        public static string SkillName(SkillCheck s)
        {
            switch (s)
            {
                case SkillCheck.SleightOfHand: return "Sleight of Hand";
                default: return s.ToString();
            }
        }

        /// <summary>Colour of a [TAG] in dialogue choices: class tags in the class colour, skills in gold.</summary>
        public static Color TagColor(string tag, out bool isClass)
        {
            isClass = false;
            if (string.IsNullOrEmpty(tag)) return Ui.InkSoft;
            if (Enum.TryParse(tag, true, out ClassId c) && c != ClassId.None)
            {
                isClass = true;
                return InkColorOf(c);
            }
            if (Enum.TryParse(tag, true, out SkillCheck _)) return GoldInk;
            return Ui.InkSoft;
        }

        public static string ResourceName(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Mana: return "Mana";
                case ResourceType.Rage: return "Rage";
                case ResourceType.Energy: return "Energy";
                case ResourceType.Focus: return "Focus";
                default: return "";
            }
        }

        public static string SlotLabel(EquipSlot s)
        {
            switch (s)
            {
                case EquipSlot.Finger1: case EquipSlot.Finger2: return "Ring";
                case EquipSlot.Trinket1: case EquipSlot.Trinket2: return "Trinket";
                case EquipSlot.MainHand: return "Main";
                case EquipSlot.OffHand: return "Off";
                case EquipSlot.Shoulder: return "Shoulders";
                default: return s.ToString();
            }
        }

        // ================================================================ keyboard helpers

        public static bool KeyDown(KeyCode k) => GameInput.KeyDown(k);

        /// <summary>Number key 1..9 pressed this frame (top row or keypad), else 0.</summary>
        public static int NumberKeyDown()
        {
            for (int i = 1; i <= 9; i++)
                if (GameInput.KeyDown(KeyCode.Alpha0 + i) || GameInput.KeyDown(KeyCode.Keypad0 + i)) return i;
            return 0;
        }

        public static bool ConfirmKeyDown() => GameInput.KeyDown(KeyCode.Return) || GameInput.KeyDown(KeyCode.KeypadEnter);
    }
}
