// CombatController — drives one Battle in the Unity layer: animates CombatEvents in order (UnitView, Fx,
// FloatingText, Sfx), steps AI units with readable pacing, and turns player input into Battle actions
// (targeting single/point/area abilities, movement with range preview, items, end turn).
//
// CONTRACT FILE: the public members below are used by the UI (hotbar, turn order, target frame, log).
// The combat engineer implements the bodies and may add private members/partials, but must not remove or
// rename public members.
//
// Implementation is split into partial files (all in Scripts/Game/Flow):
//   CombatController.cs          lifecycle, per-frame loop, speed, commands (contract surface)
//   CombatController.Queue.cs    event intake, grouping into "beats", the sequential presentation runner
//   CombatController.Present.cs  beat presenters and the CombatEvent → visual mapping, state visuals, log lines
//   CombatController.Input.cs    player turn: move range, path/AoE previews, hover text, targeting, smart attack
//   CombatController.AI.cs       AI pacing (one step at a time, path previews, loop guards)
// See Docs/CombatFlow.md for the input rules and the presentation timing.
using System;
using System.Collections.Generic;
using System.Reflection;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class CombatController
    {
        readonly GameFlow flow;
        bool disposed;
        bool endingQueued, endingDone;
        bool disengageRequested, disengageQueued, disengageDone;
        bool timeScaleOwned;

        /// <summary>Created by GameFlow when the session raises CombatStarted.</summary>
        public CombatController(GameFlow flow, Battle battle)
        {
            this.flow = flow;
            Battle = battle;
            if (battle == null) { Finished = true; return; }
            try
            {
                SeedPresentedAuras();
                PrepareViews();
            }
            catch (Exception e) { LogOnce("ctor", "CombatController setup failed: " + e); }
        }

        /// <summary>Driven by GameFlow every frame while in combat (unscaled delta time × AnimationSpeed is applied inside).</summary>
        public void Update(float dt)
        {
            if (disposed || Finished || Battle == null) return;
            try { Tick(dt); }
            catch (Exception e) { LogOnce("tick:" + e.GetType().Name + ":" + e.Message, "CombatController.Update: " + e); }
        }

        /// <summary>
        /// True once the battle is over, every event has been presented and Session.FinishBattle() (or LeaveCombat)
        /// has been called by this controller; GameFlow then disposes it when the session raises CombatEnded.
        /// </summary>
        public bool Finished { get; private set; }

        /// <summary>Clears previews/overlays and releases presentation state (views are owned by GameFlow).</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { Teardown(); }
            catch (Exception e) { LogOnce("dispose", "CombatController.Dispose: " + e); }
        }

        /// <summary>The battle being presented.</summary>
        public Battle Battle { get; private set; }

        /// <summary>Unit whose turn it is (may be AI-controlled).</summary>
        public Unit ActiveUnit => Battle?.ActiveUnit;

        /// <summary>True when the active unit waits for player input and nothing is animating.</summary>
        public bool IsPlayerTurn =>
            !disposed && !Finished && Battle != null && !disengageRequested && QueueIdle && Battle.NeedsPlayerInput;

        /// <summary>True while events are being animated or an AI unit is acting.</summary>
        public bool IsBusy
        {
            get
            {
                if (disposed || Battle == null) return false;
                if (!QueueIdle || disengageRequested) return true;
                if (Battle.IsOver) return !Finished;
                var u = Battle.ActiveUnit;
                return u != null && Battle.IsAIControlled(u);
            }
        }

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
        public string BeginAbility(string abilityId)
        {
            try { return BeginAbilityInternal(abilityId); }
            catch (Exception e) { LogOnce("begin:" + abilityId, "BeginAbility(" + abilityId + "): " + e); return Fail("That cannot be used right now."); }
        }

        /// <summary>Same as BeginAbility for a usable item (potion, bandage, scroll).</summary>
        public string BeginItem(ItemInstance item)
        {
            try { return BeginItemInternal(item); }
            catch (Exception e) { LogOnce("item", "BeginItem: " + e); return Fail("That cannot be used right now."); }
        }

        public void CancelTargeting()
        {
            bool was = IsTargeting;
            TargetingAbility = null;
            TargetingItem = null;
            targetingMods = null;
            validStamp = -1;
            if (was)
            {
                FxSystem.Hide(AoeId);
                FxSystem.Hide(RangeId);
                FxSystem.Hide(PathId);
                hoverDirty = true;
            }
        }

        /// <summary>
        /// Text describing what a click would do right now (hover preview): e.g. "Fireball → Grey Wolf · 14–22 Fire ·
        /// 96% hit · 3.5 s (pending)" or "Move 6.2 m (2.8 m left)" or the reason it is not possible.
        /// </summary>
        public string HoverPreview { get; private set; } = "";

        // ------------------------------------------------------------ commands

        /// <summary>Ends the active player unit's turn (Space/Enter).</summary>
        public void EndTurn()
        {
            if (!IsPlayerTurn) return;
            var u = Battle.ActiveUnit;
            CancelTargeting();
            ActionResult r;
            try { r = Battle.EndTurn(u); }
            catch (Exception e) { LogOnce("endturn", "EndTurn: " + e); r = ActionResult.Fail("Could not end the turn."); }
            PullEvents(null);
            if (!r.Ok) Fail(r.Reason);
        }

        /// <summary>Leave a practice fight (all hostiles passive). Returns null or the reason.</summary>
        public string Disengage()
        {
            if (disposed || Finished || Battle == null) return "Not in combat.";
            if (disengageRequested) return null;
            var s = flow != null ? flow.Session : null;
            if (s == null) return Fail("Not in combat.");
            string why;
            try { why = s.CannotLeaveCombatReason(); }
            catch (Exception e) { LogOnce("leave", "CannotLeaveCombatReason: " + e); why = "You cannot leave this fight."; }
            if (why != null) return Fail(why);
            CancelTargeting();
            disengageRequested = true;
            return null;
        }

        /// <summary>Toggle companion auto-play for a party unit (AI plays its turns).</summary>
        public void SetAutoPlay(Unit u, bool on)
        {
            if (u == null) return;
            var s = flow != null ? flow.Session : null;
            try
            {
                if (s != null) s.SetAutoPlay(u, on);
                else { u.AutoPlay = on; if (u.Pet != null) u.Pet.AutoPlay = on; }
            }
            catch (Exception e) { LogOnce("autoplay", "SetAutoPlay: " + e); }
            if (on && Battle != null && (u == Battle.ActiveUnit || u.Pet == Battle.ActiveUnit)) CancelTargeting();
            // a step the AI previewed (path shown, not walked yet) is stale now: the AI re-plans from scratch
            DropAIPreview();
            aiUnit = null;
            hoverDirty = true;
        }

        /// <summary>Accept/decline a pending self-resurrection (Soulstone, Reincarnation) for the active unit.</summary>
        public void AnswerSelfResurrection(bool accept)
        {
            if (disposed || Battle == null) return;
            var u = Battle.ActiveUnit;
            if (u == null || Battle.PendingSelfResurrection(u) == null) return;
            ActionResult r;
            try { r = accept ? Battle.AcceptSelfResurrection(u) : Battle.DeclineSelfResurrection(u); }
            catch (Exception e) { LogOnce("selfres", "AnswerSelfResurrection: " + e); r = ActionResult.Fail("Something went wrong."); }
            PullEvents(null);
            if (!r.Ok) Fail(r.Reason);
        }

        /// <summary>Show the movement-range overlay for the active player unit (a setting remembered across fights,
        /// see ShowMoveRangeSetting).</summary>
        public bool ShowMoveRange
        {
            get => ShowMoveRangeSetting;
            set => ShowMoveRangeSetting = value;
        }

        /// <summary>Recently presented combat log lines (newest last), already formatted.</summary>
        public IReadOnlyList<string> LogLines => logLines;

        // ------------------------------------------------------------ additions (beyond the original contract)

        /// <summary>Speeds the presentation up (×2.5, like holding Shift). Persisted by the UI if desired.</summary>
        public bool FastForward { get; set; }

        /// <summary>Current presentation speed (AnimationSpeed × fast-forward), applied to Time.timeScale during combat.</summary>
        public float PresentationSpeed { get; private set; } = 1f;

        /// <summary>Last command failure reason ("Not enough rage (15).") for a toast; "" when none.</summary>
        public string LastError { get; private set; } = "";
        /// <summary>Time.unscaledTime when LastError was set (toast fade-out).</summary>
        public float LastErrorTime { get; private set; } = -100f;
        /// <summary>Raised with the reason whenever a command fails (toasts).</summary>
        public event Action<string> ErrorRaised;

        /// <summary>The self-resurrection offer waiting for AnswerSelfResurrection (UI shows the prompt), or null.</summary>
        public SelfResOffer PendingSelfResurrection
        {
            get
            {
                if (disposed || Battle == null || !QueueIdle) return null;
                var u = Battle.ActiveUnit;
                return u != null && !Battle.IsAIControlled(u) ? Battle.PendingSelfResurrection(u) : null;
            }
        }

        /// <summary>Battle unit under the mouse during the player's turn (target frame preview), or null.</summary>
        public Unit HoveredTarget { get; private set; }

        /// <summary>
        /// Confirms the ability/item being targeted on a unit picked from the UI (party frames, turn order) instead of
        /// the world, exactly like a left click on that unit (walks into range first when needed). Returns null on
        /// success (or when nothing is being targeted), else the reason.
        /// </summary>
        public string TargetUnit(Unit target)
        {
            if (!IsTargeting || target == null) return null;
            try
            {
                if (!IsPlayerTurn) return Fail(Battle != null && Battle.NeedsPlayerInput ? "Wait for the action to finish." : "It is not your turn.");
                var u = Battle.ActiveUnit;
                var a = TargetingAbility;
                var item = TargetingItem;
                if (u == null || a == null) return null;
                if (!Battle.Units.Contains(target)) return Fail("That is not part of this fight.");
                Unit tgt = null;
                Vec2? point = null;
                if (a.target == TargetType.Point || IsAimed(a)) point = AimPoint(u, a, target, ToV(target.Position));
                else tgt = target;
                var why = ExecutePlan(u, PlanUse(u, a, tgt, point, item != null), tgt, point, item);
                hoverDirty = true;
                return why;
            }
            catch (Exception e)
            {
                LogOnce("targetunit", "TargetUnit: " + e);
                return Fail("That cannot be used right now.");
            }
        }

        /// <summary>
        /// The "show the movement range in combat" setting without a running fight (settings screens). Every battle gets a
        /// new controller, so the value lives here and in PlayerPrefs; ShowMoveRange reads and writes the same value.
        /// </summary>
        public static bool ShowMoveRangeSetting
        {
            get
            {
                if (showMoveRangePref < 0)
                {
                    try { showMoveRangePref = PlayerPrefs.GetInt(ShowMoveRangeKey, 1) == 0 ? 0 : 1; }
                    catch (Exception) { showMoveRangePref = 1; }
                }
                return showMoveRangePref == 1;
            }
            set
            {
                int v = value ? 1 : 0;
                if (v == showMoveRangePref) return;
                showMoveRangePref = v;
                try { PlayerPrefs.SetInt(ShowMoveRangeKey, v); } catch (Exception) { }
            }
        }

        const string ShowMoveRangeKey = "lv.combat.showMoveRange";
        static int showMoveRangePref = -1;   // -1 = not loaded yet

        /// <summary>
        /// Spends seconds of the active player unit's Time without acting, so that windows from the start of the turn
        /// (silence, pacify, root, school lockouts) run out (see WaitUntilFree). Only during IsPlayerTurn. Returns null on
        /// success, else the reason.
        /// </summary>
        public string Wait(float seconds)
        {
            if (disposed || Battle == null || Finished) return Fail("Not in combat.");
            if (!IsPlayerTurn) return Fail(Battle.NeedsPlayerInput ? "Wait for the action to finish." : "It is not your turn.");
            var u = Battle.ActiveUnit;
            if (u == null) return Fail("It is not your turn.");
            if (float.IsNaN(seconds) || seconds <= 0.001f) return Fail("Nothing to wait for.");
            seconds = Mathf.Min(seconds, Mathf.Max(0f, u.TimeLeft));
            ActionResult r;
            try { r = Battle.Wait(u, seconds); }
            catch (Exception e) { LogOnce("wait", "Wait: " + e); r = ActionResult.Fail("You cannot wait right now."); }
            PullEvents(null);
            // waiting emits no event: drop every cache keyed on the event count (move range, valid targets, hover)
            InvalidateTurnCaches();
            if (!r.Ok) return Fail(r.Reason);
            FloatingText.Spawn(Head(u, V(u)) + new Vector2(0f, 0.3f), "Waits " + seconds.ToString("0.#") + " s", MutedText, 0.75f);
            return null;
        }

        /// <summary>
        /// Seconds of Time the active unit has to wait until every window left from the start of its turn (silence,
        /// pacify, root, school lockouts) has run out; 0 when nothing can be waited out this turn. For a "Wait" button.
        /// </summary>
        public float WaitUntilFree => Battle != null && Battle.ActiveUnit != null ? FreeIn(Battle.ActiveUnit, out _) : 0f;

        /// <summary>Why the battle cannot continue (an AI turn that cannot be ended, a battle that cannot be finished) for a
        /// banner; "" while everything works. The player is then told to load a save or return to the main menu (Esc).</summary>
        public string StuckReason { get; private set; } = "";

        // ================================================================ per frame

        const float FastForwardFactor = 2.5f;

        void Tick(float rawDt)
        {
            float speed = ApplySpeed();
            bool paused = Time.timeScale <= 0f;
            // GameFlow passes unscaled time; guard against a scaled delta (we drive Time.timeScale ourselves)
            float real = Mathf.Min(Mathf.Max(0f, rawDt), Time.unscaledDeltaTime + 0.0001f);
            float dt = paused ? 0f : Mathf.Min(real, 0.1f) * speed;
            FrameDt = dt;

            PullEvents(null);
            RunDeferred(dt);
            RunQueue(dt);
            if (disposed) return;
            UpdatePersistentVisuals();

            bool playerTurnShown = false, aiDriven = false;
            if (QueueIdle && !Finished)
            {
                if (disengageRequested) HandleDisengage();
                else if (Battle.IsOver) HandleEnding();
                else
                {
                    var u = Battle.ActiveUnit;
                    if (u != null && Battle.IsAIControlled(u))
                    {
                        if (IsTargeting) CancelTargeting();
                        DriveAI(dt);
                        aiDriven = true;
                    }
                    else if (Battle.NeedsPlayerInput)
                    {
                        // the AI does not drive this turn (any more): a previewed step is stale and it re-plans next time
                        aiUnit = null;
                        UpdatePlayerTurn();
                        playerTurnShown = true;
                    }
                }
            }
            if (disposed) return;
            if (!aiDriven) DropAIPreview();
            if (!playerTurnShown)
            {
                HidePlayerTurnVisuals();
                // targeting can stay active while a beat plays (an approach walk whose cast then failed): right click/Esc
                // still cancel it, and Esc must not open the pause menu meanwhile
                if (IsTargeting && !Finished) UpdateBusyTargetingInput();
            }
        }

        void UpdateBusyTargetingInput()
        {
            if (UiRoot.ModalActive || (flow != null && !flow.WorldInputEnabled)) return;
            UiRoot.HotkeysSuppressed = true;
            if (GameInput.KeyDown(KeyCode.Escape) || (GameInput.MouseDown(1) && !GameInput.PointerOverUi)) CancelTargeting();
        }

        /// <summary>Scaled frame delta of the presentation clock (used by per-frame presenter loops).</summary>
        float FrameDt;

        float ApplySpeed()
        {
            float s = flow != null ? flow.AnimationSpeed : 1f;
            if (float.IsNaN(s) || s <= 0f) s = 1f;
            s = Mathf.Clamp(s, 0.25f, 4f);
            if (FastForward || GameInput.Key(KeyCode.LeftShift) || GameInput.Key(KeyCode.RightShift)) s *= FastForwardFactor;
            s = Mathf.Min(s, 8f);
            PresentationSpeed = s;
            // UnitView/Fx/FloatingText animate with scaled time: drive Time.timeScale so every animation keeps
            // its timing relative to the queue. Never override a pause (timeScale 0) set by someone else.
            if (Time.timeScale > 0f && Mathf.Abs(Time.timeScale - s) > 0.001f)
            {
                Time.timeScale = s;
                timeScaleOwned = true;
            }
            return s;
        }

        void RestoreTimeScale()
        {
            if (timeScaleOwned && Time.timeScale > 0f) Time.timeScale = 1f;
            timeScaleOwned = false;
        }

        // ================================================================ ending / disengage

        void HandleEnding()
        {
            if (!endingQueued)
            {
                endingQueued = true;
                Enqueue(new Beat { Kind = BeatKind.Ending });
                return;
            }
            if (!endingDone) return;
            FinishNow(false);
        }

        void HandleDisengage()
        {
            if (Battle.IsOver) { disengageRequested = false; HandleEnding(); return; }
            if (!disengageQueued)
            {
                disengageQueued = true;
                Enqueue(new Beat { Kind = BeatKind.Disengage });
                return;
            }
            if (!disengageDone) return;
            FinishNow(true);
        }

        void FinishNow(bool leave)
        {
            if (Finished) return;
            var s = flow != null ? flow.Session : null;
            HidePlayerTurnVisuals();
            RestoreTimeScale();
            Finished = true;   // set first: the session raises CombatEnded synchronously and GameFlow disposes us
            if (s == null) return;
            try
            {
                if (leave)
                {
                    if (s.LeaveCombat() == null)
                    {
                        // not allowed any more (or already over): finish normally if the battle ended
                        if (Battle.IsOver && s.Battle == Battle) s.FinishBattle();
                        else
                        {
                            Finished = false;
                            disengageRequested = disengageQueued = disengageDone = false;
                            Fail(string.IsNullOrEmpty(s.LastError) ? "You cannot leave this fight." : s.LastError);
                        }
                    }
                }
                else if (s.Battle == Battle)
                {
                    if (s.FinishBattle() == null) LogOnce("finish", "FinishBattle failed: " + s.LastError);
                }
            }
            catch (Exception e) { LogOnce("finish-ex", "Finishing the battle failed: " + e); }
            if (disposed || !Finished) return;   // the normal path: CombatEnded was raised and GameFlow disposed us

            // FinishBattle/LeaveCombat clear Session.Battle first and raise CombatEnded last. When something in between
            // threw (quest kills, XP, level-ups, encounter state), the session is out of the fight but GameFlow never
            // heard of it and would keep this controller (no world sync, no field events, no autosave).
            bool sessionLeft;
            try { sessionLeft = s.Battle != Battle; } catch (Exception) { sessionLeft = true; }
            if (sessionLeft) RaiseMissingCombatEnded(s, leave);
            else
            {
                StuckReason = "The battle could not be finished. Open the menu (Esc) to load a save or return to the main menu.";
                Fail(StuckReason);
                if (flow != null) flow.Toast(StuckReason);
            }
        }

        static MethodInfo flowEventHandler;
        static bool flowEventHandlerLooked;

        /// <summary>
        /// Delivers the CombatEnded event the session failed to raise to GameFlow's session-event handler, so the flow runs
        /// its usual teardown (dispose this controller, re-sync the world views, music, victory autosave) and relays it to
        /// the UI. GameFlow has no public hook for this, hence the lookup of its handler.
        /// </summary>
        void RaiseMissingCombatEnded(GameSession s, bool leave)
        {
            var outcome = Battle.Outcome == BattleOutcome.Defeat ? CombatEndKind.Defeat
                : leave || Battle.Outcome == BattleOutcome.Fled ? CombatEndKind.Left : CombatEndKind.Victory;
            var ev = new SessionEvent
            {
                Kind = SessionEventKind.CombatEnded, Outcome = outcome, Battle = Battle,
                Text = outcome == CombatEndKind.Victory ? "Victory!" : outcome == CombatEndKind.Left ? "You step away from the fight." : "Defeat...",
            };
            Debug.LogError("[Lanternvale] The session left the battle without raising CombatEnded; ending the fight in the flow.");
            bool delivered = false;
            try
            {
                if (!flowEventHandlerLooked)
                {
                    flowEventHandlerLooked = true;
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    flowEventHandler = typeof(GameFlow).GetMethod("OnSessionEventRaised", flags, null, new[] { typeof(SessionEvent) }, null)
                                       ?? typeof(GameFlow).GetMethod("OnCombatEnded", flags, null, new[] { typeof(SessionEvent) }, null);
                }
                if (flowEventHandler != null && flow != null && flow.Combat == this)
                {
                    flowEventHandler.Invoke(flow, new object[] { ev });
                    delivered = true;
                }
            }
            catch (Exception e) { LogOnce("finish-recover", "Ending the fight in the flow failed: " + e); }
            if (delivered || disposed) return;
            StuckReason = "The battle could not be finished. Open the menu (Esc) to load a save or return to the main menu.";
            Fail(StuckReason);
            if (flow != null) flow.Toast(StuckReason);
        }

        // ================================================================ teardown

        void Teardown()
        {
            CancelTargeting();
            FxSystem.Hide(MoveRangeId);
            FxSystem.Hide(PathId);
            FxSystem.Hide(AoeId);
            FxSystem.Hide(RangeId);
            FxSystem.Hide(AiPathId);
            aiPathShown = false;
            aiPreview = null;
            HideFacingIndicators();
            ClearHighlights();
            if (activeRingView != null) { activeRingView.SetActiveTurn(false); activeRingView = null; }
            foreach (var kv in casting)
            {
                if (kv.Value.Beam >= 0) FxSystem.StopBeam(kv.Value.Beam);
                var v = V(kv.Key);
                if (v != null) v.StopCasting();
            }
            casting.Clear();
            if (curChannelBeam >= 0) { FxSystem.StopBeam(curChannelBeam); curChannelBeam = -1; }

            // whatever was not presented (e.g. after Disengage) — sync the visuals with the final rules state
            if (Battle != null)
            {
                foreach (var u in Battle.Units)
                {
                    var v = V(u);
                    if (v == null) continue;
                    v.SetActiveTurn(false);
                    v.SetTargetable(null);
                    v.StopCasting();
                    ApplyRealStateVisuals(u, v);
                }
            }
            // dead units whose fade-out was still running
            for (int i = 0; i < deferred.Count; i++)
            {
                var d = deferred[i];
                if (d.Unit != null && (d.Kind == DeferredKind.RemoveDespawned || !d.Unit.IsAlive)) RemoveViewNow(d.Unit);
            }
            deferred.Clear();

            RestoreTimeScale();
            var rig = CameraRig.Instance;
            if (rig != null) rig.Focus(null);
            if (Battle != null && Battle.Outcome != BattleOutcome.Defeat && startedCombatMusic)
            {
                var map = flow != null ? flow.Map : null;
                if (map != null) Music.Play(Music.MoodForMap(map.Def));
            }
            queue.Clear();
            curBeat = null;
            curRoutine = null;
            HoveredTarget = null;
            HoverPreview = "";
        }

        // ================================================================ helpers

        string Fail(string reason)
        {
            if (string.IsNullOrEmpty(reason)) reason = "That is not possible right now.";
            LastError = reason;
            LastErrorTime = Time.unscaledTime;
            try { ErrorRaised?.Invoke(reason); }
            catch (Exception e) { LogOnce("errhandler", "ErrorRaised handler: " + e); }
            return reason;
        }

        GameDatabase Db => Battle != null ? Battle.Db : flow?.Db;

        readonly HashSet<string> warned = new HashSet<string>();

        void LogOnce(string key, string message)
        {
            if (!warned.Add(key ?? "")) return;
            Debug.LogWarning("[Lanternvale] " + message);
        }

        bool viewOfFailed, unitOfFailed;

        /// <summary>The unit's view (GameFlow registry, falling back to a scan of UnitView.All by Tag/UnitId), or null.</summary>
        UnitView V(Unit u)
        {
            if (u == null) return null;
            UnitView v = null;
            if (flow != null && !viewOfFailed)
            {
                try { v = flow.ViewOf(u); }
                catch (Exception e) { viewOfFailed = true; LogOnce("viewof", "GameFlow.ViewOf failed (falling back to UnitView.All): " + e.Message); }
            }
            if (v != null) return v;
            var all = UnitView.All;
            for (int i = 0; i < all.Count; i++)
            {
                var w = all[i];
                if (w == null) continue;
                if (ReferenceEquals(w.Tag, u) || w.UnitId == u.Id) return w;
            }
            return null;
        }

        Unit UnitOfView(UnitView v)
        {
            if (v == null) return null;
            Unit u = null;
            if (flow != null && !unitOfFailed)
            {
                try { u = flow.UnitOf(v); }
                catch (Exception e) { unitOfFailed = true; LogOnce("unitof", "GameFlow.UnitOf failed (falling back to Tag/UnitId): " + e.Message); }
            }
            if (u != null) return u;
            if (v.Tag is Unit t) return t;
            return Battle != null && v.UnitId != int.MinValue ? Battle.FindUnit(v.UnitId) : null;
        }

        UnitView Ensure(Unit u)
        {
            if (u == null) return null;
            UnitView v = null;
            if (flow != null)
            {
                try { v = flow.EnsureView(u); }
                catch (Exception e) { LogOnce("ensure", "GameFlow.EnsureView failed: " + e.Message); }
            }
            return v ?? V(u);
        }

        void RemoveViewNow(Unit u)
        {
            if (u == null || removedViews.Contains(u)) return;
            removedViews.Add(u);
            if (flow == null) return;
            try { flow.RemoveView(u); }
            catch (Exception e) { LogOnce("remove", "GameFlow.RemoveView failed: " + e.Message); }
        }

        /// <summary>Creates/places views for every unit of the battle at the start.</summary>
        void PrepareViews()
        {
            foreach (var u in Battle.Units)
            {
                if (u == null || u.Dead) continue;
                var v = Ensure(u);
                if (v == null) continue;
                var p = ToV(u.Position);
                float d = Vector2.Distance(v.FeetPosition, p);
                if (d > 2.5f) v.Teleport(p);
                else if (d > 0.05f) v.MoveAlong(new List<Vector2>(2) { v.FeetPosition, p }, 4.5f);
                if (Mathf.Abs(u.Facing.x) > 0.1f) v.SetFacing(u.Facing.x >= 0f ? 1 : -1);
                if (u.Downed && !v.IsDowned) v.PlayDowned();
                RefreshStateVisuals(u);
            }
        }

        static Vector2 ToV(Vec2 v) => new Vector2(v.x, v.y);
        static Vec2 ToVec(Vector2 v) => new Vec2(v.x, v.y);
    }
}
