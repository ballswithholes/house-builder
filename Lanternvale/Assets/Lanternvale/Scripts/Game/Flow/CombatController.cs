// CombatController — drives one Battle in the Unity layer: animates CombatEvents in order (UnitView, Fx,
// FloatingText, Sfx), steps AI units with readable pacing, and turns player input into Battle actions
// (targeting single/point/area abilities, movement with range preview, items, end turn).
//
// CONTRACT FILE: the public members below are used by the UI (hotbar, turn order, target frame, log).
// The combat engineer implements the bodies and may add private members/partials, but must not remove or
// rename public members.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;

namespace Lanternvale.Game
{
    public sealed partial class CombatController
    {
        /// <summary>Created by GameFlow when the session raises CombatStarted.</summary>
        public CombatController(GameFlow flow, Battle battle) { throw new NotImplementedException(); }

        /// <summary>Driven by GameFlow every frame while in combat (unscaled delta time × AnimationSpeed is applied inside).</summary>
        public void Update(float dt) { throw new NotImplementedException(); }

        /// <summary>
        /// True once the battle is over, every event has been presented and Session.FinishBattle() (or LeaveCombat)
        /// has been called by this controller; GameFlow then disposes it when the session raises CombatEnded.
        /// </summary>
        public bool Finished { get; private set; }

        /// <summary>Clears previews/overlays and releases presentation state (views are owned by GameFlow).</summary>
        public void Dispose() { throw new NotImplementedException(); }

        /// <summary>The battle being presented.</summary>
        public Battle Battle { get; private set; }

        /// <summary>Unit whose turn it is (may be AI-controlled).</summary>
        public Unit ActiveUnit => Battle?.ActiveUnit;

        /// <summary>True when the active unit waits for player input and nothing is animating.</summary>
        public bool IsPlayerTurn { get { throw new NotImplementedException(); } }

        /// <summary>True while events are being animated or an AI unit is acting.</summary>
        public bool IsBusy { get { throw new NotImplementedException(); } }

        // ------------------------------------------------------------ targeting

        /// <summary>Ability being targeted (null when not targeting).</summary>
        public AbilityDef TargetingAbility { get; private set; }
        /// <summary>Item being targeted (null when not targeting an item).</summary>
        public ItemInstance TargetingItem { get; private set; }
        public bool IsTargeting => TargetingAbility != null || TargetingItem != null;

        /// <summary>
        /// Hotbar entry point. Self/no-target abilities and toggles (stances, aspects, auto attack) execute
        /// immediately; abilities needing a unit or point enter targeting mode (left click confirms, right
        /// click/Esc cancels). Returns null or the reason it cannot be used.
        /// </summary>
        public string BeginAbility(string abilityId) { throw new NotImplementedException(); }

        /// <summary>Same as BeginAbility for a usable item (potion, bandage, scroll).</summary>
        public string BeginItem(ItemInstance item) { throw new NotImplementedException(); }

        public void CancelTargeting() { throw new NotImplementedException(); }

        /// <summary>
        /// Text describing what a click would do right now (hover preview): e.g. "Fireball → Grey Wolf · 14–22 Fire ·
        /// 96% hit · 3.5 s (pending)" or "Move 6.2 m (2.8 m left)" or the reason it is not possible.
        /// </summary>
        public string HoverPreview { get; private set; } = "";

        // ------------------------------------------------------------ commands

        /// <summary>Ends the active player unit's turn (Space/Enter).</summary>
        public void EndTurn() { throw new NotImplementedException(); }

        /// <summary>Leave a practice fight (all hostiles passive). Returns null or the reason.</summary>
        public string Disengage() { throw new NotImplementedException(); }

        /// <summary>Toggle companion auto-play for a party unit (AI plays its turns).</summary>
        public void SetAutoPlay(Unit u, bool on) { throw new NotImplementedException(); }

        /// <summary>Accept/decline a pending self-resurrection (Soulstone, Reincarnation) for the active unit.</summary>
        public void AnswerSelfResurrection(bool accept) { throw new NotImplementedException(); }

        /// <summary>Show the movement-range overlay for the active player unit.</summary>
        public bool ShowMoveRange { get; set; } = true;

        /// <summary>Recently presented combat log lines (newest last), already formatted.</summary>
        public IReadOnlyList<string> LogLines { get { throw new NotImplementedException(); } }
    }
}
