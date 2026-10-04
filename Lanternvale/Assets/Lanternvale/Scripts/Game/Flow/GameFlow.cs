// GameFlow — the Unity-side hub between GameSession (rules/world state) and the presentation (MapView,
// UnitView, Fx, audio) and UI. UI screens talk ONLY to GameFlow, GameFlow.Combat and GameFlow.Session.
//
// CONTRACT FILE: the public members below are the agreed surface used by the UI. The flow engineer
// implements the bodies (and may add private members/partials) but must not remove or rename public members.
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

    public sealed partial class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        /// <summary>True while a game is running (not in the main menu).</summary>
        public static bool HasGame => Instance != null && Instance.Session != null && Instance.Session.Mode != SessionMode.None;

        /// <summary>The running session (null in the main menu before the first game).</summary>
        public GameSession Session { get; private set; }

        public GameDatabase Db => GameRoot.Instance != null ? GameRoot.Instance.Db : null;

        /// <summary>The current diorama (null in the main menu).</summary>
        public MapView Map => MapView.Current;

        /// <summary>Non-null while Session.Mode == Combat.</summary>
        public CombatController Combat { get; private set; }

        /// <summary>Every session event, relayed after the flow has reacted to it (toasts, level ups, loot…).</summary>
        public event Action<SessionEvent> SessionEventRaised;

        /// <summary>Raised when the combat presenter starts animating a CombatEvent (combat log, HUD flashes).</summary>
        public event Action<CombatEvent> CombatEventPresented;

        // ------------------------------------------------------------ party & selection

        /// <summary>The party member the player is controlling/inspecting (exploration: leader by default; combat: the active unit).</summary>
        public Unit Selected { get; private set; }

        public void Select(Unit u) { throw new NotImplementedException(); }

        /// <summary>Active party (main character + companions + pets) in display order.</summary>
        public IReadOnlyList<Unit> PartyUnits => Session != null ? (IReadOnlyList<Unit>)Session.PartyUnits() : Array.Empty<Unit>();

        public UnitView ViewOf(Unit u) { throw new NotImplementedException(); }
        public Unit UnitOf(UnitView v) { throw new NotImplementedException(); }

        /// <summary>Returns the view for a unit, creating it (sprite/size/ring colour from its class/creature/companion art) if needed.
        /// The combat presenter calls this for summons, pets and battle units.</summary>
        public UnitView EnsureView(Unit u) { throw new NotImplementedException(); }

        /// <summary>Destroys a unit's view (despawned summons, removed NPCs).</summary>
        public void RemoveView(Unit u) { throw new NotImplementedException(); }

        /// <summary>Called by the CombatController when it starts presenting an event (raises CombatEventPresented).</summary>
        internal void NotifyCombatEventPresented(CombatEvent e) => CombatEventPresented?.Invoke(e);

        // ------------------------------------------------------------ hover (for nameplates/tooltips)

        /// <summary>Unit under the mouse (party, NPC or enemy) or null.</summary>
        public Unit HoveredUnit { get; private set; }
        /// <summary>Label for the hovered non-unit object or NPC ("Elder Maru", "Chest", "To Whisperwood"), "" if none.</summary>
        public string HoveredLabel { get; private set; } = "";
        /// <summary>World position to anchor HoveredLabel.</summary>
        public Vector2 HoveredLabelWorld { get; private set; }

        /// <summary>Display name for any unit or NPC id.</summary>
        public string NameOf(Unit u) { throw new NotImplementedException(); }

        // ------------------------------------------------------------ lifecycle

        /// <summary>Starts a new game (creates a fresh session, builds the start map, plays the opening).</summary>
        public void StartNewGame(NewGameOptions options) { throw new NotImplementedException(); }

        /// <summary>Tears down the map/views and shows the main menu.</summary>
        public void ReturnToMainMenu() { throw new NotImplementedException(); }

        public void QuitGame() { throw new NotImplementedException(); }

        // ------------------------------------------------------------ saves (Application.persistentDataPath/saves)

        public List<SaveSlotInfo> ListSaves() { throw new NotImplementedException(); }
        public bool HasAnySave { get { throw new NotImplementedException(); } }
        /// <summary>Returns null on success or a reason ("Cannot save during combat.").</summary>
        public string SaveToSlot(string slot) { throw new NotImplementedException(); }
        /// <summary>Returns null on success or an error. Works from the main menu too.</summary>
        public string LoadFromSlot(string slot) { throw new NotImplementedException(); }
        public string QuickSave() => SaveToSlot("quick");
        public string QuickLoad() => LoadFromSlot("quick");
        /// <summary>Most recent save of any slot (for "Continue"), or null.</summary>
        public SaveSlotInfo LatestSave() { throw new NotImplementedException(); }

        // ------------------------------------------------------------ exploration commands used by the UI

        /// <summary>False while the UI wants the world to ignore clicks (e.g. dragging an item). Modal screens are handled by UiRoot.</summary>
        public bool WorldInputEnabled { get; set; } = true;

        /// <summary>Use an ability outside combat (field buffs, heals, Conjure, Call Pet…). Returns null or a reason.</summary>
        public string UseAbilityOutOfCombat(Unit caster, string abilityId, Unit target = null) { throw new NotImplementedException(); }

        /// <summary>Use an item outside combat (food, potions, scrolls). Returns null or a reason.</summary>
        public string UseItemOutOfCombat(Unit user, ItemInstance item, Unit target = null) { throw new NotImplementedException(); }

        /// <summary>Long rest (camp/inn). Returns null or a reason.</summary>
        public string TryRest() { throw new NotImplementedException(); }

        /// <summary>Game speed for animations (settings): 1 = normal.</summary>
        public float AnimationSpeed { get; set; } = 1f;
    }
}
