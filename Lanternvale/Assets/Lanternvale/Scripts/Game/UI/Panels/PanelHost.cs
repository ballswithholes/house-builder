// Infrastructure screens of the panel layer:
//   PanelHost      (Order 100, never drawn) — per-frame housekeeping: occluder roll, session-event relay, deferred
//                  commands, Esc routing for state-driven windows, persisted UI settings.
//   ContextMenuScreen (460)  right-click menus (inventory: use / equip on… / sell / destroy).
//   ConfirmScreen  (470, modal) yes/no prompts ("Return to the main menu?", "Destroy Wolf Pelt?").
//   NoticeScreen   (480)  short feedback lines ("Not enough money.", "Saved to Slot 2.").
using System;
using System.Collections.Generic;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    /// <summary>Esc routing for windows that are not UiRoot panels (vendors, loot, menus…).</summary>
    public static class EscRouter
    {
        struct Handler
        {
            public int Priority;
            public Func<bool> Handle;
        }

        static readonly List<Handler> handlers = new List<Handler>();

        /// <summary>Registers a handler (higher priority first). Priorities ≥ 800 run even while UiRoot panels are open.</summary>
        public static void Register(int priority, Func<bool> handle)
        {
            handlers.Add(new Handler { Priority = priority, Handle = handle });
            handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public const int AbovePanels = 800;

        internal static bool Route()
        {
            bool panelsOpen = AnyPanelOpen();
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (panelsOpen && h.Priority < AbovePanels) return false;   // let UiRoot close the top panel
                bool done;
                try { done = h.Handle(); }
                catch (Exception e) { Debug.LogException(e); done = false; }
                if (done) return true;
            }
            return false;
        }

        static readonly string[] PanelIds =
        {
            UiPanels.Character, UiPanels.Inventory, UiPanels.Spellbook, UiPanels.Talents, UiPanels.Journal,
            UiPanels.CombatLog, UiPanels.Party, UiPanels.Settings, UiPanels.Pause, UiPanels.SaveLoad, UiPanels.Help, UiPanels.Map,
        };

        public static bool AnyPanelOpen()
        {
            if (!GameFlow.HasGame) return false;
            for (int i = 0; i < PanelIds.Length; i++) if (UiRoot.IsOpen(PanelIds[i])) return true;
            return false;
        }
    }

    /// <summary>UI preferences of the panel layer (PlayerPrefs).</summary>
    public static class PanelPrefs
    {
        const string KAnim = "lv_anim_speed", KText = "lv_text_speed";
        static bool loaded;
        static float textSpeed = 1f;

        /// <summary>Dialogue typewriter speed multiplier (0 = instant).</summary>
        public static float TextSpeed
        {
            get { Load(); return textSpeed; }
            set { textSpeed = Mathf.Clamp(value, 0f, 4f); PlayerPrefs.SetFloat(KText, textSpeed); PlayerPrefs.Save(); }
        }

        public static void SaveAnimationSpeed(float v)
        {
            PlayerPrefs.SetFloat(KAnim, v);
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            textSpeed = PlayerPrefs.GetFloat(KText, 1f);
            var f = GameFlow.Instance;
            if (f != null && PlayerPrefs.HasKey(KAnim)) f.AnimationSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(KAnim, 1f), 0.25f, 4f);
        }

        internal static void Reset() { loaded = false; }
    }

    public sealed class PanelHost : IUiScreen
    {
        public string Id => "";
        public int Order => 100;
        public bool Visible => false;
        public bool Modal => false;
        public void Draw() { }

        GameFlow subscribed;
        readonly Action<SessionEvent> relay = PanelKit.RaiseEvent;

        public void Tick(float dt)
        {
            PanelKit.RollOccluders();
            var flow = GameFlow.Instance;
            if (flow != subscribed)
            {
                if (subscribed != null) subscribed.SessionEventRaised -= relay;
                subscribed = flow;
                if (flow != null)
                {
                    flow.SessionEventRaised += relay;
                    PanelPrefs.Reset();
                    PanelPrefs.Load();
                }
            }
            PanelKit.RunDeferred();
            if (!UiRoot.HotkeysSuppressed && GameInput.KeyDown(KeyCode.Escape) && EscRouter.Route())
                UiRoot.HotkeysSuppressed = true;
        }
    }

    // ==================================================================== context menu

    public sealed class ContextMenuScreen : IUiScreen
    {
        public struct Item
        {
            public string Label;
            public bool Enabled;
            public string Tip;
            public Action Action;
            public bool Separator;
        }

        static readonly List<Item> items = new List<Item>();
        static string title = "";
        static Vector2 anchor;
        static bool open;
        static int openedFrame;
        static Rect rect;

        public const int ScreenOrder = 460;
        public string Id => "";
        public int Order => ScreenOrder;
        public bool Visible => open;
        public bool Modal => false;

        static ContextMenuScreen()
        {
            EscRouter.Register(900, () => { if (!open) return false; Close(); return true; });
        }

        public static void Open(Vector2 guiPos, string heading, List<Item> entries)
        {
            items.Clear();
            if (entries != null) items.AddRange(entries);
            title = heading ?? "";
            anchor = guiPos;
            open = items.Count > 0;
            openedFrame = Time.frameCount;
            rect = new Rect(anchor.x, anchor.y, 0f, 0f);
        }

        public static void Close() => open = false;
        public static bool IsOpen => open;

        public void Tick(float dt)
        {
            if (!open) return;
            if (PanelKit.LastClickFrame > openedFrame) { open = false; return; }
            if (Time.frameCount <= openedFrame + 1) return;
            if (GameInput.MouseDown(0) || GameInput.MouseDown(1))
            {
                var mp = GameInput.MousePosition;
                var gui = new Vector2(mp.x / Ui.Scale, (Screen.height - mp.y) / Ui.Scale);
                if (!rect.Contains(gui)) open = false;
            }
            if (!GameFlow.HasGame) open = false;
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            const float rowH = 34f, w = 250f;
            float h = 16f + items.Count * rowH + (title.Length > 0 ? 34f : 0f);
            var r = PanelKit.Fit(new Rect(anchor.x + 4f, anchor.y + 4f, w, h));
            rect = r;
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                Ui.Panel(r, Ui.InkPanel);
                PanelKit.Occlude(r, Order);
                float y = r.y + 8f;
                if (title.Length > 0)
                {
                    GUI.Label(new Rect(r.x + 14f, y, r.width - 28f, 28f), title, PanelKit.LTextBold);
                    y += 34f;
                }
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    var row = new Rect(r.x + 8f, y, r.width - 16f, rowH - 4f);
                    if (it.Separator)
                    {
                        PanelKit.Rect(new Rect(row.x + 6f, row.center.y, row.width - 12f, 1f), new Color(1f, 1f, 1f, 0.2f));
                    }
                    else
                    {
                        bool hover = it.Enabled && PanelKit.Hover(row);
                        if (hover) PanelKit.Rounded(row, new Color(1f, 0.85f, 0.5f, 0.22f));
                        PanelKit.Label(new Rect(row.x + 10f, row.y, row.width - 20f, row.height), it.Label,
                            PanelKit.LText, it.Enabled ? Ui.TextLight : new Color(1f, 1f, 1f, 0.35f));
                        if (!string.IsNullOrEmpty(it.Tip)) Ui.TooltipFor(row, it.Tip);
                        if (it.Enabled && PanelKit.LeftClick(row))
                        {
                            open = false;
                            Ui.Sfx?.Invoke("ui_click");
                            PanelKit.Do(it.Action);
                        }
                    }
                    y += rowH;
                }
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }
    }

    // ==================================================================== confirm

    public sealed class ConfirmScreen : IUiScreen
    {
        static bool open;
        static string title = "", text = "", yes = "Yes", no = "Cancel";
        static Action onYes, onNo;
        static bool danger;
        static float openedAt;

        public const int ScreenOrder = 470;
        public string Id => "";
        public int Order => ScreenOrder;
        public bool Visible => open;
        public bool Modal => open;

        static ConfirmScreen()
        {
            EscRouter.Register(1000, () => { if (!open) return false; Answer(false); return true; });
        }

        /// <summary>Opens a yes/no prompt. onYes runs as a deferred command.</summary>
        public static void Ask(string heading, string body, string yesLabel, Action yesAction, string noLabel = "Cancel", Action noAction = null, bool dangerous = false)
        {
            title = heading ?? "";
            text = body ?? "";
            yes = string.IsNullOrEmpty(yesLabel) ? "Yes" : yesLabel;
            no = string.IsNullOrEmpty(noLabel) ? "Cancel" : noLabel;
            onYes = yesAction;
            onNo = noAction;
            danger = dangerous;
            open = true;
            openedAt = Time.unscaledTime;
            ContextMenuScreen.Close();
            Ui.Sfx?.Invoke("ui_open");
        }

        public static bool IsOpen => open;

        static void Answer(bool ok)
        {
            if (!open) return;
            open = false;
            var a = ok ? onYes : onNo;
            onYes = onNo = null;
            if (a != null) PanelKit.Do(a);
        }

        public void Tick(float dt)
        {
            if (!open) return;
            if (Time.unscaledTime - openedAt > 0.15f && PanelKit.ConfirmKeyDown()) Answer(true);
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                PanelKit.OccludeAll(Order);
                float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.18f);
                PanelKit.Rect(new Rect(0f, 0f, Ui.Width, Ui.Height), new Color(0.06f, 0.04f, 0.10f, 0.42f * t));
                const float w = 560f;
                float th = PanelKit.TextHeight(text, PanelKit.Text, w - 64f);
                float h = 74f + 20f + th + 96f;
                var r = PanelKit.Centered(w, h, (1f - t) * 14f);
                var c = PanelKit.Frame(r, title, Order);
                GUI.Label(new Rect(c.x + 8f, c.y + 6f, c.width - 16f, th + 4f), text, PanelKit.Text);
                float bw = 180f, by = r.yMax - 70f;
                if (Ui.Btn(new Rect(r.center.x - bw - 10f, by, bw, 48f), yes, danger ? Ui.ButtonDark : Ui.ButtonGold)) Answer(true);
                if (Ui.Btn(new Rect(r.center.x + 10f, by, bw, 48f), no, Ui.Button)) Answer(false);
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }
    }

    // ==================================================================== notices

    public sealed class NoticeScreen : IUiScreen
    {
        public string Id => "";
        public int Order => 480;
        public bool Visible => PanelKit.NoticeLines.Count > 0;
        public bool Modal => false;
        const float Life = 3.2f;
        GUIStyle style;

        public void Tick(float dt)
        {
            var lines = PanelKit.NoticeLines;
            for (int i = lines.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - lines[i].Born > Life) lines.RemoveAt(i);
        }

        public void Draw()
        {
            if (!PanelKit.IsRepaint) return;
            if (style == null && Ui.Number != null)
                style = new GUIStyle(Ui.Number) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
            if (style == null) return;
            var lines = PanelKit.NoticeLines;
            // between the world and the HUD's bottom block (HUD toasts/banners live at the top)
            float y = Ui.Height * 0.64f;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                float age = Time.unscaledTime - l.Born;
                float a = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01((Life - age) / 0.6f);
                float pop = 1f + 0.12f * Mathf.Clamp01(1f - age / 0.18f);
                var c = l.Color;
                c.a = a;
                var r = new Rect(0f, y + i * 34f, Ui.Width, 32f);
                var old = GUI.matrix;
                if (pop > 1.001f) GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), new Vector2(Ui.Width * 0.5f, r.center.y) * Ui.Scale);
                Ui.Shadowed(r, l.Text, style, c);
                GUI.matrix = old;
            }
        }
    }
}
