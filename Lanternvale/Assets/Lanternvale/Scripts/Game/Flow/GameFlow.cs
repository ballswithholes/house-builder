// GameFlow — the Unity-side hub between GameSession (rules/world state) and the presentation (MapView,
// UnitView, Fx, audio) and UI. UI screens talk ONLY to GameFlow, GameFlow.Combat and GameFlow.Session.
//
// CONTRACT FILE: the public members below are the agreed surface used by the UI. The flow engineer
// implements the bodies (and may add private members/partials) but must not remove or rename public members.
//
// Implementation is split over partial files (see Docs/GameFlow.md):
//   GameFlow.cs              contract surface, selection, view registry entry points, small helpers
//   GameFlow.Lifecycle.cs    boot, main-menu backdrop, new game / load / main menu / quit, per-frame loop
//   GameFlow.Events.cs       reactions to every SessionEvent (map, party, chests, lanterns, combat start/end…)
//   GameFlow.Views.cs        UnitView registry: party, NPC and encounter views, sync, wandering, removal
//   GameFlow.Overlay.cs      IMGUI overlay owned by the flow: fades, NPC barks
//   Exploration.cs           exploration input: hover, click-to-move, interactions, hotkeys
//   FieldPresenter.cs        out-of-combat CombatEvents (field buffs, heals, summons…), rest
//   SaveFiles.cs             save slots on disk (atomic writes, header cache)
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>One save slot on disk.</summary>
    public sealed class SaveSlotInfo
    {
        public string Slot = "";          // "quick", "auto", "slot1".."slot9"
        public SaveHeader Header;         // name, class, level, map, day/hour, play time (null if unreadable)
        public DateTime Modified;
        public string Error = "";         // non-empty when the file could not be read
    }

    /// <summary>What is under the mouse (see GameFlow.HoveredKind).</summary>
    public enum HoverKind { None, PartyMember, Npc, Enemy, Object }

    [DefaultExecutionOrder(-50)]
    public sealed partial class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        /// <summary>True while a game is running (not in the main menu).</summary>
        public static bool HasGame => Instance != null && Instance.Session != null && Instance.Session.Mode != SessionMode.None;

        /// <summary>The running session (null in the main menu before the first game).</summary>
        public GameSession Session { get; private set; }

        public GameDatabase Db => GameRoot.Instance != null ? GameRoot.Instance.Db : null;

        /// <summary>The current diorama (null in the main menu — the title backdrop is not exposed).</summary>
        public MapView Map => HasGame ? MapView.Current : null;

        /// <summary>Non-null while Session.Mode == Combat.</summary>
        public CombatController Combat { get; private set; }

        /// <summary>Every session event, relayed after the flow has reacted to it (toasts, level ups, loot…).</summary>
        public event Action<SessionEvent> SessionEventRaised;

        /// <summary>Raised when the combat presenter starts animating a CombatEvent (combat log, HUD flashes).</summary>
        public event Action<CombatEvent> CombatEventPresented;

        /// <summary>Reason of the last failed StartNewGame / LoadFromSlot / SaveToSlot ("" when the last one succeeded).</summary>
        public string LastError { get; private set; } = "";

        // ------------------------------------------------------------ party & selection

        /// <summary>The party member the player is controlling/inspecting (exploration: leader by default; combat: the active unit).</summary>
        public Unit Selected { get; private set; }

        /// <summary>
        /// Selects a unit. In exploration a party character also becomes the leader (camera follows it, it is
        /// the one the player moves). In combat Selected follows the active player unit again on the next turn.
        /// </summary>
        public void Select(Unit u)
        {
            if (u == null || Session == null) return;
            try
            {
                if (Session.Mode == SessionMode.Exploration && Session.IsInParty(u) && Session.Leader != u)
                {
                    StopPartyMove(true);
                    var why = Session.SetLeader(u);
                    if (why != null) { Toast(why); return; }
                }
                SetSelectedInternal(u);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Active party (main character + companions + pets) in display order. Cached per frame and refreshed
        /// on every session event, so UI code may read it freely (do not keep the list across frames).</summary>
        public IReadOnlyList<Unit> PartyUnits
        {
            get
            {
                if (Session == null) return Array.Empty<Unit>();
                int frame = Time.frameCount;
                if (partyUnitsFrame != frame || partyUnitsSession != Session || partyUnitsCache == null)
                {
                    // a fresh list each time (never mutated afterwards): safe even while a caller iterates the old one
                    partyUnitsFrame = frame;
                    partyUnitsSession = Session;
                    partyUnitsCache = Session.PartyUnits();
                }
                return partyUnitsCache;
            }
        }

        List<Unit> partyUnitsCache;
        int partyUnitsFrame = -1;
        GameSession partyUnitsSession;

        public UnitView ViewOf(Unit u)
        {
            if (u == null) return null;
            if (views.TryGetValue(u, out var v))
            {
                if (v != null) return v;
                views.Remove(u);
            }
            return null;
        }

        public Unit UnitOf(UnitView v)
        {
            if (v == null) return null;
            return viewUnits.TryGetValue(v, out var u) ? u : null;
        }

        /// <summary>Returns the view for a unit, creating it (sprite/size/ring colour from its class/creature/companion art) if needed.
        /// The combat presenter calls this for summons, pets and battle units.</summary>
        public UnitView EnsureView(Unit u)
        {
            if (u == null) return null;
            var v = ViewOf(u);
            if (v != null) return v;
            try { return CreateUnitView(u); }
            catch (Exception e) { Debug.LogException(e); return null; }
        }

        /// <summary>Destroys a unit's view (despawned summons, removed NPCs).</summary>
        public void RemoveView(Unit u)
        {
            if (u == null) return;
            if (!views.TryGetValue(u, out var v)) return;
            DetachUnitView(u);
            DisposeView(v);
        }

        /// <summary>Called by the CombatController when it starts presenting an event (raises CombatEventPresented).</summary>
        internal void NotifyCombatEventPresented(CombatEvent e)
        {
            var h = CombatEventPresented;
            if (h == null) return;
            try { h(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // ------------------------------------------------------------ hover (for nameplates/tooltips)

        /// <summary>Unit under the mouse (party member or battle unit) or null. Exploration NPCs and encounter
        /// enemies have no rules Unit: they are reported through HoveredLabel/HoveredKind.</summary>
        public Unit HoveredUnit { get; private set; }
        /// <summary>Label for the hovered non-unit object or NPC ("Elder Maru", "Chest", "To Whisperwood"), "" if none.
        /// Exploration encounter enemies get their level (range), rank and group size, the level wrapped in a rich-text
        /// colour tag ("Mossling  &lt;color=#ffe14a&gt;Lv 3-4&lt;/color&gt;  (x3)"): draw it with a richText style.</summary>
        public string HoveredLabel { get; private set; } = "";
        /// <summary>World position to anchor HoveredLabel.</summary>
        public Vector2 HoveredLabelWorld { get; private set; }
        /// <summary>What kind of thing is hovered (party member, NPC, enemy — exploration encounter or battle unit —, map object).</summary>
        public HoverKind HoveredKind { get; private set; }

        /// <summary>Display name for any unit or NPC id.</summary>
        public string NameOf(Unit u)
        {
            if (u == null) return "";
            if (u.Companion != null && Session != null)
            {
                var n = Session.NpcName(u.Companion.id);
                if (!string.IsNullOrEmpty(n) && n != u.Companion.id) return n;
            }
            if (!string.IsNullOrEmpty(u.Name)) return u.Name;
            if (u.Companion != null && !string.IsNullOrEmpty(u.Companion.name)) return u.Companion.name;
            if (u.Creature != null && !string.IsNullOrEmpty(u.Creature.name)) return u.Creature.name;
            if (u.Class != null) return u.Class.name;
            return "";
        }

        // ------------------------------------------------------------ lifecycle

        /// <summary>Starts a new game (creates a fresh session, builds the start map, plays the opening).</summary>
        public void StartNewGame(NewGameOptions options) => StartNewGameInternal(options);

        /// <summary>Tears down the map/views and shows the main menu.</summary>
        public void ReturnToMainMenu() => ReturnToMainMenuInternal();

        public void QuitGame()
        {
            Debug.Log("[Lanternvale] Quit.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------ saves (Application.persistentDataPath/saves)

        public List<SaveSlotInfo> ListSaves() => SaveFiles.List();
        public bool HasAnySave => SaveFiles.HasAny();
        /// <summary>Returns null on success or a reason ("Cannot save during combat.").</summary>
        public string SaveToSlot(string slot) => SaveToSlotInternal(slot);
        /// <summary>Returns null on success or an error. Works from the main menu too.</summary>
        public string LoadFromSlot(string slot) => LoadFromSlotInternal(slot);
        public string QuickSave() => SaveToSlot("quick");
        public string QuickLoad() => LoadFromSlot("quick");
        /// <summary>Most recent save of any slot (for "Continue"), or null.</summary>
        public SaveSlotInfo LatestSave() => SaveFiles.Latest();
        /// <summary>Deletes a save slot from disk. Returns null on success or an error.</summary>
        public string DeleteSave(string slot) => SaveFiles.Delete(slot);

        // ------------------------------------------------------------ exploration commands used by the UI

        /// <summary>False while the UI wants the world to ignore clicks (e.g. dragging an item). Modal screens are handled by UiRoot.</summary>
        public bool WorldInputEnabled { get; set; } = true;

        /// <summary>Use an ability outside combat (field buffs, heals, Conjure, Call Pet…). Returns null or a reason.</summary>
        public string UseAbilityOutOfCombat(Unit caster, string abilityId, Unit target = null) => UseAbilityInField(caster, abilityId, target);

        /// <summary>Use an item outside combat (food, potions, scrolls). Returns null or a reason.</summary>
        public string UseItemOutOfCombat(Unit user, ItemInstance item, Unit target = null) => UseItemInField(user, item, target);

        /// <summary>Long rest (camp/inn). Returns null or a reason.</summary>
        public string TryRest() => TryRestInternal();

        /// <summary>Game speed for animations (settings): 1 = normal.</summary>
        public float AnimationSpeed { get; set; } = 1f;

        // ------------------------------------------------------------ helpers

        static Vector2 ToUnity(Lanternvale.Util.Vec2 v) => new Vector2(v.x, v.y);
        static Lanternvale.Util.Vec2 ToVec2(Vector2 v) => new Lanternvale.Util.Vec2(v.x, v.y);

        void SetSelectedInternal(Unit u)
        {
            if (Selected == u)
            {
                var same = ViewOf(u);
                if (same != null && !same.IsSelected) same.SetSelected(true);
                return;
            }
            var old = ViewOf(Selected);
            if (old != null) old.SetSelected(false);
            Selected = u;
            var v = ViewOf(u);
            if (v != null) v.SetSelected(true);
        }

        /// <summary>Raises a toast for the UI without going through the session (flow-local messages): relayed through
        /// SessionEventRaised as a SessionEventKind.Toast, so it lands in the HUD's toast lane like every other toast.</summary>
        public void Toast(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Relay(new SessionEvent { Kind = SessionEventKind.Toast, Text = text });
        }

        void Relay(SessionEvent e)
        {
            var h = SessionEventRaised;
            if (h == null || e == null) return;
            try { h(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
