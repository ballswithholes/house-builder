// Input that works in every project setup: the legacy Input Manager when it is enabled, otherwise
// IMGUI events (which Unity delivers regardless of the active input backend, so projects that only
// enable the new Input System package still work without any extra dependency).
//
// Poll from Update(). UI code registers screen rects that block world clicks via GameInput.BlockRect().
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class GameInput
    {
        // ---- state for the current frame (filled by GameInputDriver) ----
        internal static Vector2 mouse;
        internal static readonly bool[] down = new bool[3], held = new bool[3], up = new bool[3];
        internal static float scroll;
        internal static readonly HashSet<KeyCode> keysHeld = new HashSet<KeyCode>();
        internal static readonly HashSet<KeyCode> keysDown = new HashSet<KeyCode>();
        internal static readonly HashSet<KeyCode> keysUp = new HashSet<KeyCode>();
        internal static readonly List<Rect> blockers = new List<Rect>();      // screen space, origin bottom-left
        internal static readonly List<Rect> nextBlockers = new List<Rect>();
        internal static bool textFieldFocused;

        /// <summary>Mouse position in screen pixels (origin bottom-left, like Input.mousePosition).</summary>
        public static Vector2 MousePosition => mouse;

        /// <summary>True when the mouse is over a UI panel (world clicks are suppressed).</summary>
        public static bool PointerOverUi
        {
            get
            {
                for (int i = 0; i < blockers.Count; i++) if (blockers[i].Contains(mouse)) return true;
                return false;
            }
        }

        /// <summary>Button pressed this frame and not over UI. 0 = left, 1 = right, 2 = middle.</summary>
        public static bool WorldClick(int button) => down[button] && !PointerOverUi;
        public static bool MouseDown(int button) => down[button];
        public static bool MouseHeld(int button) => held[button];
        public static bool MouseUp(int button) => up[button];

        /// <summary>Scroll this frame (positive = wheel up / zoom in), 0 when over UI.</summary>
        public static float Scroll => PointerOverUi ? 0f : scroll;

        public static bool Key(KeyCode k) => !textFieldFocused && keysHeld.Contains(k);
        public static bool KeyDown(KeyCode k) => !textFieldFocused && keysDown.Contains(k);
        public static bool KeyUp(KeyCode k) => keysUp.Contains(k);
        public static bool AnyKeyDown => !textFieldFocused && keysDown.Count > 0;

        /// <summary>UI calls this (in GUI coordinates, origin top-left) for every panel drawn this frame.</summary>
        public static void BlockRectGui(Rect guiRect)
        {
            // only collect once per frame (OnGUI runs for Layout, Repaint and every input event)
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            // convert from IMGUI (top-left origin, current GUI.matrix) to screen (bottom-left origin)
            var m = GUI.matrix;
            var p0 = m.MultiplyPoint3x4(new Vector3(guiRect.xMin, guiRect.yMin, 0));
            var p1 = m.MultiplyPoint3x4(new Vector3(guiRect.xMax, guiRect.yMax, 0));
            nextBlockers.Add(Rect.MinMaxRect(p0.x, Screen.height - p1.y, p1.x, Screen.height - p0.y));
        }

        /// <summary>Set by text fields so typing doesn't trigger hotkeys.</summary>
        public static void SetTextFieldFocused(bool focused) { textFieldFocused = focused; }

        public static void EnsureDriver()
        {
            if (GameInputDriver.Instance != null) return;
            var go = new GameObject("Lanternvale Input");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameInputDriver>();
        }
    }

    [DefaultExecutionOrder(-1000)]
    public sealed class GameInputDriver : MonoBehaviour
    {
        public static GameInputDriver Instance { get; private set; }

        // events collected by OnGUI during the previous frame
        readonly bool[] pendingDown = new bool[3], pendingUp = new bool[3];
        readonly HashSet<KeyCode> pendingKeyDown = new HashSet<KeyCode>();
        readonly HashSet<KeyCode> pendingKeyUp = new HashSet<KeyCode>();
        float pendingScroll;
        Vector2 guiMouse;
        bool haveGuiMouse;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            useGUILayout = false;
        }

        void Update()
        {
            GameInput.blockers.Clear();
            GameInput.blockers.AddRange(GameInput.nextBlockers);
            GameInput.nextBlockers.Clear();
            GameInput.keysDown.Clear();
            GameInput.keysUp.Clear();

#if ENABLE_LEGACY_INPUT_MANAGER
            GameInput.mouse = Input.mousePosition;
            for (int b = 0; b < 3; b++)
            {
                GameInput.down[b] = Input.GetMouseButtonDown(b);
                GameInput.held[b] = Input.GetMouseButton(b);
                GameInput.up[b] = Input.GetMouseButtonUp(b);
            }
            GameInput.scroll = Input.mouseScrollDelta.y;
            if (Input.anyKey || Input.anyKeyDown || GameInput.keysHeld.Count > 0)
            {
                foreach (var k in WatchedKeys)
                {
                    if (Input.GetKeyDown(k)) GameInput.keysDown.Add(k);
                    if (Input.GetKeyUp(k)) GameInput.keysUp.Add(k);
                    if (Input.GetKey(k)) GameInput.keysHeld.Add(k); else GameInput.keysHeld.Remove(k);
                }
            }
#else
            if (haveGuiMouse) GameInput.mouse = new Vector2(guiMouse.x, Screen.height - guiMouse.y);
            for (int b = 0; b < 3; b++)
            {
                GameInput.down[b] = pendingDown[b];
                GameInput.up[b] = pendingUp[b];
                if (pendingDown[b]) GameInput.held[b] = true;
                if (pendingUp[b]) GameInput.held[b] = false;
                pendingDown[b] = pendingUp[b] = false;
            }
            GameInput.scroll = pendingScroll;
            pendingScroll = 0f;
            foreach (var k in pendingKeyDown) { if (!GameInput.keysHeld.Contains(k)) GameInput.keysDown.Add(k); GameInput.keysHeld.Add(k); }
            foreach (var k in pendingKeyUp) { GameInput.keysUp.Add(k); GameInput.keysHeld.Remove(k); }
            pendingKeyDown.Clear();
            pendingKeyUp.Clear();
#endif
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e == null) return;
            GUI.matrix = Matrix4x4.identity;
            // mousePosition is valid for mouse events and repaints
            if (e.isMouse || e.type == EventType.Repaint || e.type == EventType.Layout)
            {
                guiMouse = e.mousePosition;
                haveGuiMouse = true;
            }
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button < 3) pendingDown[e.button] = true;
                    break;
                case EventType.MouseUp:
                    if (e.button < 3) pendingUp[e.button] = true;
                    break;
                case EventType.ScrollWheel:
                    pendingScroll += -e.delta.y / 3f;
                    break;
                case EventType.KeyDown:
                    if (e.keyCode != KeyCode.None) pendingKeyDown.Add(e.keyCode);
                    break;
                case EventType.KeyUp:
                    if (e.keyCode != KeyCode.None) pendingKeyUp.Add(e.keyCode);
                    break;
            }
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus) return;
            GameInput.keysHeld.Clear();
            for (int b = 0; b < 3; b++) GameInput.held[b] = false;
        }

        /// <summary>Keys polled through the legacy manager (IMGUI mode receives every key).</summary>
        static readonly KeyCode[] WatchedKeys =
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F, KeyCode.G,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0, KeyCode.Minus, KeyCode.Equals,
            KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape, KeyCode.Tab, KeyCode.Backspace,
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.I, KeyCode.C, KeyCode.N, KeyCode.P, KeyCode.L, KeyCode.J, KeyCode.M, KeyCode.K, KeyCode.B, KeyCode.H,
            KeyCode.T, KeyCode.V, KeyCode.X, KeyCode.Z, KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5,
            KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12, KeyCode.BackQuote,
            // dialogue choices also accept the keypad (PanelKit.NumberKeyDown)
            KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
            KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
        };
    }
}
