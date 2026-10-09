// UiRoot: the single IMGUI host. Every screen (HUD layers, panels, modals) implements IUiScreen and is
// discovered automatically (any non-abstract class in this assembly with a parameterless constructor).
// UiRoot draws visible screens in Order, handles panel toggles/hotkeys and blocks world input under modals.
//
// Order bands: HUD 0–99 · panels 100–199 · modal windows 200–299 · full-screen (menus) 300–399 · overlays 400+.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Lanternvale.Game
{
    public interface IUiScreen
    {
        /// <summary>Panel id for UiRoot.Open/Toggle (see UiPanels), or "" for state-driven screens (HUD, dialogue).</summary>
        string Id { get; }
        /// <summary>Draw order (lower first). See the bands above.</summary>
        int Order { get; }
        /// <summary>Whether to draw this frame (state-driven screens check the session; panels usually return UiRoot.IsOpen(Id)).</summary>
        bool Visible { get; }
        /// <summary>Modal screens block world input everywhere and hide the hotbar's hotkeys.</summary>
        bool Modal { get; }
        /// <summary>Called from OnGUI after Ui.BeginFrame() when Visible.</summary>
        void Draw();
        /// <summary>Called every frame from Update (even when hidden) — timers, hotkeys local to the screen.</summary>
        void Tick(float dt);
    }

    /// <summary>Convenience base class for panels toggled by id.</summary>
    public abstract class UiPanel : IUiScreen
    {
        public abstract string Id { get; }
        public virtual int Order => 100;
        public virtual bool Visible => UiRoot.IsOpen(Id) && GameFlow.HasGame;
        public virtual bool Modal => false;
        public abstract void Draw();
        public virtual void Tick(float dt) { }
        public void Close() => UiRoot.Close(Id);
    }

    /// <summary>Panel ids (and their default hotkeys, handled by UiRoot).</summary>
    public static class UiPanels
    {
        public const string Character = "character";   // C
        public const string Inventory = "inventory";   // I / B
        public const string Spellbook = "spellbook";   // P
        public const string Talents = "talents";       // N
        public const string Journal = "journal";       // J
        public const string CombatLog = "log";         // L
        public const string Party = "party";           // (camp / roster)
        public const string Settings = "settings";
        public const string Pause = "pause";           // Esc
        public const string SaveLoad = "saveload";
        public const string Help = "help";             // F1
        public const string Map = "map";               // M
    }

    [DefaultExecutionOrder(100)]
    public sealed class UiRoot : MonoBehaviour
    {
        public static UiRoot Instance { get; private set; }

        static readonly HashSet<string> open = new HashSet<string>();
        readonly List<IUiScreen> screens = new List<IUiScreen>();
        static readonly List<string> openOrder = new List<string>();

        /// <summary>Set by any screen that wants to suppress global hotkeys this frame (e.g. a focused text field).</summary>
        public static bool HotkeysSuppressed;

        public static UiRoot Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Lanternvale UI");
            DontDestroyOnLoad(go);
            return go.AddComponent<UiRoot>();
        }

        void Awake()
        {
            Instance = this;
            // no screen uses GUILayout/GUI.Window: skip the extra Layout event (plain GUI + Rects only)
            useGUILayout = false;
            DiscoverScreens();
        }

        void DiscoverScreens()
        {
            var iface = typeof(IUiScreen);
            foreach (var t in iface.Assembly.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface || !iface.IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                try { screens.Add((IUiScreen)Activator.CreateInstance(t)); }
                catch (Exception e) { Debug.LogError($"[Lanternvale] UI screen {t.Name} failed to construct: {e}"); }
            }
            screens.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public static T Get<T>() where T : class, IUiScreen
        {
            if (Instance == null) return null;
            foreach (var s in Instance.screens) if (s is T t) return t;
            return null;
        }

        // ------------------------------------------------------------ panels
        public static bool IsOpen(string id) => !string.IsNullOrEmpty(id) && open.Contains(id);

        public static void Open(string id)
        {
            if (string.IsNullOrEmpty(id) || open.Contains(id)) return;
            open.Add(id);
            openOrder.Remove(id);
            openOrder.Add(id);
            Ui.Sfx?.Invoke("ui_open");
        }

        public static void Close(string id)
        {
            if (!open.Remove(id)) return;
            openOrder.Remove(id);
            Ui.Sfx?.Invoke("ui_close");
        }

        public static void Toggle(string id)
        {
            if (IsOpen(id)) Close(id); else Open(id);
        }

        public static void CloseAll()
        {
            open.Clear();
            openOrder.Clear();
        }

        /// <summary>Closes the most recently opened panel. Returns false if none was open.</summary>
        public static bool CloseTop()
        {
            if (openOrder.Count == 0) return false;
            Close(openOrder[openOrder.Count - 1]);
            return true;
        }

        /// <summary>True while any visible screen is modal (world input is blocked).</summary>
        public static bool ModalActive
        {
            get
            {
                if (Instance == null) return false;
                foreach (var s in Instance.screens)
                    if (s.Modal && SafeVisible(s)) return true;
                return false;
            }
        }

        static bool SafeVisible(IUiScreen s)
        {
            try { return s.Visible; }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------ loop
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var s in screens)
            {
                try { s.Tick(dt); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (!HotkeysSuppressed) HandleHotkeys();
            HotkeysSuppressed = false;
        }

        void HandleHotkeys()
        {
            if (!GameFlow.HasGame) return;
            if (GameInput.KeyDown(KeyCode.Escape))
            {
                if (!CloseTop() && !ModalActive) Open(UiPanels.Pause);
                return;
            }
            if (ModalActive) return;
            if (GameInput.KeyDown(KeyCode.C)) Toggle(UiPanels.Character);
            if (GameInput.KeyDown(KeyCode.I) || GameInput.KeyDown(KeyCode.B)) Toggle(UiPanels.Inventory);
            if (GameInput.KeyDown(KeyCode.P)) Toggle(UiPanels.Spellbook);
            if (GameInput.KeyDown(KeyCode.N)) Toggle(UiPanels.Talents);
            if (GameInput.KeyDown(KeyCode.J)) Toggle(UiPanels.Journal);
            if (GameInput.KeyDown(KeyCode.L)) Toggle(UiPanels.CombatLog);
            if (GameInput.KeyDown(KeyCode.M)) Toggle(UiPanels.Map);
            if (GameInput.KeyDown(KeyCode.F1)) Toggle(UiPanels.Help);
        }

        void OnGUI()
        {
            GUI.depth = 0;
            Ui.BeginFrame();
            bool modal = false;
            foreach (var s in screens)
            {
                if (!SafeVisible(s)) continue;
                if (s.Modal) modal = true;
                try { s.Draw(); }
                catch (ExitGUIException) { throw; }
                catch (Exception e) { Debug.LogException(e); }
            }
            // a modal screen blocks clicks everywhere in the world
            if (modal) Ui.Block(new Rect(0, 0, Ui.Width, Ui.Height));
            Ui.EndFrame();
        }
    }
}
