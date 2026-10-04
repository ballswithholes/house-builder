// Exploration input (BG3-style): hover (units, then map objects), click to move the party in formation,
// click NPCs/companions to talk, chests to open (locks: rogue Pick Lock or a Sleight of Hand check),
// transition markers to travel, enemies to attack first (optionally with an opener ability armed from the
// hotbar: Charge, Cheap Shot, Ambush, Pyroblast…), props to inspect. Tab cycles the selected leader,
// right click stops the party / cancels an armed opener, F5/F9 quick save/load.
//
// The session is authoritative for positions: views walk along the session's plan and report their
// positions back (Session.UpdatePartyPositions) every ~0.2 m, which runs region/encounter/transition
// triggers. A Stop result (dialogue, combat, travel, locked transition) halts the walk.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameFlow
    {
        /// <summary>Walking speed range (m/s): short hops stroll, long trips jog.</summary>
        const float WalkSpeed = 3.4f, JogSpeed = 4.8f;
        const float ReportStep = 0.2f;
        const float EngageRange = 9f;
        const float ReachSlack = 1.6f;
        const float HoldMoveDelay = 0.28f, HoldReplanInterval = 0.22f;

        enum PendingKind { None, Talk, Chest, Transition, Prop, Engage }

        // movement
        bool partyMoving;
        Vector2 lastReportPos;
        float footTimer;
        readonly List<Vec2> otherPositions = new List<Vec2>();

        // click-and-hold to keep walking towards the cursor
        bool groundHold;
        float groundHoldTime, holdReplanTimer;
        Vector2 lastMoveDest;

        // pending interaction (walk into range first)
        PendingKind pendingKind;
        string pendingId = "";
        Vector2 pendingPos;
        UnitView pendingView;      // moving targets (NPCs) are tracked through their view
        Unit pendingActor;         // whose distance counts (null = leader)
        float pendingRange;
        EncounterDef pendingEncounter;
        string engageOpener = "";
        int engageTargetIndex;

        // opener armed from the hotbar (UseAbilityOutOfCombat with an enemy-target ability)
        Unit openerCaster;

        /// <summary>Ability id armed to open the next fight ("" when none): the next click on an enemy engages
        /// its encounter with this ability (GameSession.EngageEncounter with an opener). Right click cancels.</summary>
        public string PendingOpener { get; private set; } = "";

        /// <summary>Who will use PendingOpener (null when none).</summary>
        public Unit PendingOpenerCaster => string.IsNullOrEmpty(PendingOpener) ? null : openerCaster;

        /// <summary>Disarms PendingOpener.</summary>
        public void CancelOpener()
        {
            if (string.IsNullOrEmpty(PendingOpener) && openerCaster == null) return;
            PendingOpener = "";
            openerCaster = null;
            for (int i = 0; i < enemyEntries.Count; i++)
                if (enemyEntries[i].View != null) enemyEntries[i].View.SetTargetable(null);
        }

        void ArmOpener(Unit caster, AbilityDef a)
        {
            CancelOpener();
            PendingOpener = a.id;
            openerCaster = caster;
            var col = new Color(1f, 0.45f, 0.38f);
            for (int i = 0; i < enemyEntries.Count; i++)
                if (enemyEntries[i].View != null) enemyEntries[i].View.SetTargetable(col);
            Toast($"{a.name}: choose an enemy to open the fight with (right click to cancel).");
        }

        // hover
        UnitView hoveredView;
        MapObject hoveredObject;
        string hoveredObjectLabel = "";

        // ------------------------------------------------------------ per frame

        void UpdateExploration(float dt)
        {
            var s = Session;
            int gen = worldGeneration;
            UpdatePartyMovement(dt);
            if (gen != worldGeneration || Session != s || s.Mode != SessionMode.Exploration) return;

            bool input = WorldInputAllowed();
            if (input) HandleExplorationClicks(dt);
            else groundHold = false;
            if (gen != worldGeneration || Session != s || s.Mode != SessionMode.Exploration) return;

            if (!UiRoot.ModalActive && !UiRoot.HotkeysSuppressed && !pausedByMenu)
            {
                if (GameInput.KeyDown(KeyCode.Tab)) CycleSelection(GameInput.Key(KeyCode.LeftShift) || GameInput.Key(KeyCode.RightShift) ? -1 : 1);
                if (GameInput.KeyDown(KeyCode.Escape) && !string.IsNullOrEmpty(PendingOpener))
                {
                    // Esc disarms the opener instead of opening the pause menu this frame
                    CancelOpener();
                    UiRoot.HotkeysSuppressed = true;
                }
            }
        }

        bool WorldInputAllowed() =>
            WorldInputEnabled && !UiRoot.ModalActive && !GameInput.PointerOverUi && !pausedByMenu && !FadeBlocksInput;

        /// <summary>F5 / F9 (any mode while a game runs). Ignored while a yes/no prompt or a context menu is open:
        /// their closures are bound to the current session and its objects ("Destroy Wolf Pelt?", "Dismiss Rook?",
        /// "Overwrite save?"), and a quick load underneath would leave them acting on the freshly loaded game.</summary>
        void UpdateGlobalHotkeys()
        {
            if (UiRoot.HotkeysSuppressed) return;
            if (SessionBoundPromptOpen()) return;
            if (GameInput.KeyDown(KeyCode.F5))
            {
                var err = QuickSave();
                Toast(err ?? "Quick saved.");
                if (err == null) Sfx.Play("ui_open");
            }
            else if (GameInput.KeyDown(KeyCode.F9))
            {
                var err = QuickLoad();
                if (err != null) Toast(err);
            }
        }

        static bool SessionBoundPromptOpen()
        {
            try { return Panels.ConfirmScreen.IsOpen || Panels.ContextMenuScreen.IsOpen; }
            catch (Exception) { return false; }
        }

        void HandleExplorationClicks(float dt)
        {
            var rig = CameraRig.Instance;
            if (rig == null) return;
            var mouse = rig.MouseWorld;

            if (GameInput.MouseDown(1))
            {
                // right click: disarm an opener, or stop where we are
                if (!string.IsNullOrEmpty(PendingOpener)) CancelOpener();
                else if (partyMoving || pendingKind != PendingKind.None)
                {
                    StopPartyMove(true);
                    ResetPending();
                }
                groundHold = false;
                return;
            }

            if (GameInput.MouseDown(0))
            {
                groundHold = false;
                if (hoveredView != null && enemyByView.ContainsKey(hoveredView)) { ClickView(hoveredView); return; }
                CancelOpener();   // anything but an enemy disarms the opener
                if (hoveredView != null) { ClickView(hoveredView); return; }
                if (hoveredObject != null) { ClickObject(hoveredObject); return; }
                ResetPending();
                if (MoveTo(mouse, true))
                {
                    groundHold = true;
                    groundHoldTime = 0f;
                    holdReplanTimer = 0f;
                }
                return;
            }

            // keep walking towards the cursor while the button is held on the ground
            if (groundHold)
            {
                if (!GameInput.MouseHeld(0)) { groundHold = false; return; }
                groundHoldTime += dt;
                holdReplanTimer -= dt;
                if (groundHoldTime >= HoldMoveDelay && holdReplanTimer <= 0f && (mouse - lastMoveDest).sqrMagnitude > 0.8f * 0.8f)
                {
                    holdReplanTimer = HoldReplanInterval;
                    MoveTo(mouse, false);
                }
            }
        }

        // ------------------------------------------------------------ clicks

        void ClickView(UnitView v)
        {
            var s = Session;
            var u = UnitOf(v);
            if (u != null)
            {
                if (s.IsInParty(u)) { Select(u); Sfx.Play("ui_click", null, 0.6f, 1.1f); }
                else if (u.Owner != null && s.IsInParty(u.Owner)) SetSelectedInternal(u);   // pets/totems: inspect only
                return;
            }
            if (npcByView.TryGetValue(v, out var npc))
            {
                BeginInteraction(PendingKind.Talk, npc.Id, v.FeetPosition, v, null, GameSession.InteractionRange - 0.35f);
                return;
            }
            if (enemyByView.TryGetValue(v, out var enemy) && enemy.Encounter != null)
                ClickEnemy(enemy);
        }

        void ClickEnemy(EnemyEntry enemy)
        {
            var s = Session;
            var enc = enemy.Encounter;
            string opener = PendingOpener;
            var caster = PendingOpenerCaster;
            var a = !string.IsNullOrEmpty(opener) && Db != null ? Db.Ability(opener) : null;
            if (a == null || caster == null || !s.IsInParty(caster) || !caster.IsAlive)
            {
                CancelOpener();
                // plain attack: the party strikes first (surprise when the leader is stealthed)
                engageOpener = "";
                engageTargetIndex = 0;
                BeginInteraction(PendingKind.Engage, enc.id, enemy.Home, null, null, EngageRange, enc);
                return;
            }
            // with an opener: walk until the caster is in the ability's range of the enemy's starting spot
            float range;
            try { range = AbilityRules.RangeMetres(caster, a, null, AbilityMods.For(caster, a), Db.Config); }
            catch (Exception) { range = EngageRange; }
            if (float.IsInfinity(range) || float.IsNaN(range)) range = EngageRange;
            float minRange = AbilityRules.MinRangeMetres(a);
            range = Mathf.Clamp(range - 0.45f, Mathf.Max(1.2f, minRange + 0.6f), Mathf.Max(EngageRange, minRange + 1f));
            engageOpener = opener;
            engageTargetIndex = Mathf.Max(0, enc.enemies.IndexOf(enemy.Def));
            CancelOpener();
            BeginInteraction(PendingKind.Engage, enc.id, enemy.Home, null, caster, range, enc);
        }

        void ClickObject(MapObject o)
        {
            switch (o.Kind)
            {
                case MapObjectKind.Chest:
                    BeginInteraction(PendingKind.Chest, o.Id, o.Position, null, null, GameSession.InteractionRange - 0.2f);
                    break;
                case MapObjectKind.Transition:
                    BeginInteraction(PendingKind.Transition, o.Id, o.Position, null, null, 1.2f);
                    break;
                case MapObjectKind.Prop:
                    BeginInteraction(PendingKind.Prop, o.Id, o.Position, null, null, GameSession.InteractionRange + 0.5f);
                    break;
            }
        }

        void CycleSelection(int dir)
        {
            var s = Session;
            var party = s.Party;
            if (party.Count <= 1) return;
            int i = -1;
            for (int k = 0; k < party.Count; k++) if (party[k] == Selected) { i = k; break; }
            if (i < 0) for (int k = 0; k < party.Count; k++) if (party[k] == s.Leader) { i = k; break; }
            for (int step = 1; step <= party.Count; step++)
            {
                var next = party[((i + dir * step) % party.Count + party.Count) % party.Count];
                if (next == null || next.Dead) continue;
                Select(next);
                return;
            }
        }

        // ------------------------------------------------------------ interactions

        void BeginInteraction(PendingKind kind, string id, Vector2 pos, UnitView view, Unit actor, float range, EncounterDef enc = null)
        {
            var s = Session;
            if (string.IsNullOrEmpty(id) || ViewOf(s.Leader) == null) return;
            pendingKind = kind;
            pendingId = id;
            pendingPos = pos;
            pendingView = view;
            pendingActor = actor;
            pendingEncounter = enc;
            pendingRange = Mathf.Max(0.5f, range);
            Sfx.Play("ui_click", null, 0.5f, 0.95f);
            var av = ActorView();
            if (av != null && (av.FeetPosition - pos).sqrMagnitude <= pendingRange * pendingRange)
            {
                StopPartyMove(!SilentApproach(av));
                if (pendingKind != PendingKind.None) ExecutePending();
                return;
            }
            if (!MoveTo(pos, false))
            {
                // nothing walkable gets us closer: try anyway when nearly there
                if (av != null && (av.FeetPosition - pos).sqrMagnitude <= (pendingRange + ReachSlack) * (pendingRange + ReachSlack)) ExecutePending();
                else { ResetPending(); Toast("You can't reach that."); }
            }
        }

        UnitView ActorView()
        {
            var s = Session;
            if (s == null) return null;
            var v = pendingActor != null ? ViewOf(pendingActor) : null;
            return v ?? ViewOf(s.Leader);
        }

        Vector2 PendingTarget() => pendingView != null ? pendingView.FeetPosition : pendingPos;

        void ResetPending()
        {
            pendingKind = PendingKind.None;
            pendingId = "";
            pendingView = null;
            pendingActor = null;
            pendingEncounter = null;
        }

        void ExecutePending()
        {
            var s = Session;
            var kind = pendingKind;
            var id = pendingId;
            var target = PendingTarget();
            var targetView = pendingView;
            var actor = pendingActor;
            ResetPending();
            if (kind == PendingKind.None || s == null || s.Mode != SessionMode.Exploration) return;
            var lv = ViewOf(actor ?? s.Leader);
            if (lv != null) lv.FaceTowards(target);
            try
            {
                switch (kind)
                {
                    case PendingKind.Talk:
                    {
                        if (targetView != null && lv != null) targetView.FaceTowards(lv.FeetPosition);
                        var r = s.TalkTo(id);
                        if (r != null && r.Ok && r.Kind == InteractKind.Text && targetView != null) ShowBark(targetView, r.Message);
                        else if (r != null && !r.Ok) Toast(r.Message);
                        break;
                    }
                    case PendingKind.Chest:
                        OpenChestInteraction(id);
                        break;
                    case PendingKind.Transition:
                    {
                        var r = s.UseTransition(id);
                        if (r != null && !r.Ok && r.Kind != InteractKind.Locked) Toast(r.Message);
                        break;
                    }
                    case PendingKind.Prop:
                    {
                        var text = s.InspectProp(id);
                        var map = MapView.Current;
                        var o = map != null ? map.Find(id) : null;
                        if (o != null && o.IsLantern) FxSystem.Sparkles(o.LabelPosition, new Color(1f, 0.9f, 0.6f), 8);
                        if (string.IsNullOrEmpty(text)) Toast("Nothing of note.");
                        break;
                    }
                    case PendingKind.Engage:
                    {
                        string opener = engageOpener;
                        engageOpener = "";
                        var b = string.IsNullOrEmpty(opener)
                            ? s.EngageEncounter(id)
                            : s.EngageEncounter(id, actor, opener, engageTargetIndex);
                        if (b == null && !string.IsNullOrEmpty(s.LastError)) Toast(s.LastError);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OpenChestInteraction(string chestId)
        {
            var s = Session;
            var r = s.OpenChest(chestId);
            if (r == null || r.Ok) return;
            if (r.Kind != InteractKind.Locked) { Toast(r.Message); return; }
            // locked: a rogue who knows Pick Lock tries first, otherwise the nimblest hands roll Sleight of Hand
            Unit rogue = null;
            foreach (var u in s.Party)
                if (u != null && u.IsAlive && u.Knows(GameSession.PickLockAbility)) { rogue = u; break; }
            var rv = ViewOf(rogue ?? s.Leader);
            var chest = MapView.Current != null ? MapView.Current.Find(chestId) : null;
            if (rv != null && chest != null) rv.PlayShoot(chest.Position);   // a little fiddle with the lock
            var check = rogue != null ? s.PickLock(chestId, rogue) : s.TryUnlockChest(chestId);
            if (check == null) Toast(r.Message);
            else if (!check.Success) Toast("The lock holds. You can try again.");
        }

        // ------------------------------------------------------------ party movement

        /// <summary>Plans a party move to a world point and starts animating it. False when unreachable.</summary>
        bool MoveTo(Vector2 dest, bool marker)
        {
            var s = Session;
            if (s == null || s.Mode != SessionMode.Exploration) return false;
            var leader = s.Leader;
            var lv = ViewOf(leader);
            if (lv == null || lv.IsDowned) return false;

            // the plan starts from where the views are now (the session may lag by up to ReportStep)
            SyncUnitPositionsFromViews();
            var plan = s.PlanPartyMove(ToVec2(dest));
            if (plan == null || !plan.Reachable || plan.LeaderPath.Count < 2 || plan.Length < 0.05f)
            {
                if (marker) FxSystem.GroundRing(dest, 0.3f, new Color(1f, 0.55f, 0.5f, 0.8f), 0.45f);
                return false;
            }
            lastMoveDest = dest;
            float speed = Mathf.Lerp(WalkSpeed, JogSpeed, Mathf.Clamp01((plan.Length - 6f) / 14f));
            lv.MoveAlong(plan.LeaderPath, speed, null);
            float leaderLen = Mathf.Max(0.5f, plan.Length);
            foreach (var f in plan.Followers)
            {
                var fv = ViewOf(f.Unit);
                if (fv == null || f.Path.Count == 0 || fv.IsDowned) continue;
                float len = PathLength(f.Path);
                float fs = speed * Mathf.Clamp(len / leaderLen, 0.7f, 1.45f);
                fv.MoveAlong(f.Path, Mathf.Max(1.5f, fs), null);
            }
            partyMoving = true;
            lastReportPos = lv.FeetPosition;
            if (marker)
            {
                var end = plan.LeaderPath[plan.LeaderPath.Count - 1];
                FxSystem.GroundRing(ToUnity(end), 0.35f, new Color(1f, 0.93f, 0.7f, 0.85f), 0.5f);
            }
            return true;
        }

        static float PathLength(List<Vec2> pts)
        {
            float d = 0f;
            for (int i = 1; i < pts.Count; i++) d += Vec2.Distance(pts[i - 1], pts[i]);
            return d;
        }

        void UpdatePartyMovement(float dt)
        {
            if (!partyMoving) return;
            var s = Session;
            var lv = ViewOf(s.Leader);
            if (lv == null) { partyMoving = false; ResetPending(); return; }

            bool anyMoving = lv.IsMoving;
            if (!anyMoving)
                foreach (var kv in views)
                    if (kv.Value != null && kv.Value.IsMoving && s.IsInParty(kv.Key.Owner ?? kv.Key)) { anyMoving = true; break; }

            // footsteps
            if (lv.IsMoving)
            {
                footTimer -= dt;
                if (footTimer <= 0f)
                {
                    footTimer = 0.34f;
                    Sfx.Play("footstep_grass", lv.FeetPosition, 0.32f, 1f);
                }
            }
            else footTimer = 0f;

            // arrived next to what we wanted to use?
            var av = pendingKind != PendingKind.None ? ActorView() : null;
            if (av != null)
            {
                var t = PendingTarget();
                if ((av.FeetPosition - t).sqrMagnitude <= pendingRange * pendingRange)
                {
                    int gen = worldGeneration;
                    StopPartyMove(!SilentApproach(av));
                    if (gen == worldGeneration && Session == s && s.Mode == SessionMode.Exploration && pendingKind != PendingKind.None) ExecutePending();
                    else ResetPending();
                    return;
                }
            }

            if ((lv.FeetPosition - lastReportPos).sqrMagnitude >= ReportStep * ReportStep || !anyMoving)
            {
                if (av != null && SilentApproach(av))
                {
                    // closing in on the encounter we are about to attack: do not let it trigger on its own
                    SyncUnitPositionsFromViews();
                    lastReportPos = lv.FeetPosition;
                }
                else if (!ReportPositions()) return;   // stopped by a trigger (or the world was rebuilt)
            }

            if (!anyMoving)
            {
                partyMoving = false;
                if (pendingKind != PendingKind.None)
                {
                    var t = PendingTarget();
                    av = ActorView();
                    float slack = pendingRange + ReachSlack;
                    if (av != null && (av.FeetPosition - t).sqrMagnitude <= slack * slack) ExecutePending();
                    else { ResetPending(); Toast("You can't reach that."); }
                }
            }
        }

        /// <summary>
        /// True while walking up to an encounter the player chose to attack and the actor is already inside its
        /// detection radius: positions are synced silently so the encounter does not start before the opener.
        /// </summary>
        bool SilentApproach(UnitView actorView)
        {
            if (pendingKind != PendingKind.Engage || pendingEncounter == null || actorView == null || Session == null) return false;
            var center = ToUnity(pendingEncounter.pos);
            // any active member close enough to be noticed (stealthed ones only within 3 m, like the session's trigger)
            foreach (var u in Session.Party)
            {
                if (u == null || !u.IsAlive) continue;
                var v = ViewOf(u);
                if (v == null) continue;
                float r = pendingEncounter.radius;
                if (u.IsStealthed) r = Mathf.Min(r, Lanternvale.World.MapRuntime.StealthDetectRadius);
                r += 0.75f;
                if ((v.FeetPosition - center).sqrMagnitude <= r * r) return true;
            }
            return false;
        }

        /// <summary>Reports view positions to the session (runs triggers). False when movement was stopped.</summary>
        bool ReportPositions()
        {
            var s = Session;
            if (s == null || s.Mode != SessionMode.Exploration) return false;
            var leader = s.Leader;
            var lv = ViewOf(leader);
            if (lv == null) return false;
            otherPositions.Clear();
            foreach (var u in s.PartyUnits())
            {
                if (u == leader) continue;
                var v = ViewOf(u);
                otherPositions.Add(v != null ? ToVec2(v.FeetPosition) : u.Position);
            }
            int gen = worldGeneration;
            var tr = s.UpdatePartyPositions(ToVec2(lv.FeetPosition), otherPositions);
            if (gen != worldGeneration || Session != s) return false;   // travelled / loaded: views were rebuilt
            lastReportPos = lv.FeetPosition;
            if (tr != null && tr.Stop)
            {
                StopPartyMove(false);
                ResetPending();
                return false;
            }
            return true;
        }

        /// <summary>Copies view positions into the units without running triggers (right before planning, silent approach).</summary>
        void SyncUnitPositionsFromViews()
        {
            var s = Session;
            // never in combat: battle positions are authoritative there
            if (s == null || (s.Mode != SessionMode.Exploration && s.Mode != SessionMode.Dialogue)) return;
            foreach (var u in s.PartyUnits())
            {
                var v = ViewOf(u);
                if (v != null && !v.IsDowned) u.Position = ToVec2(v.FeetPosition);
            }
        }

        /// <summary>Stops every party view; report = tell the session the final positions (may fire triggers).</summary>
        void StopPartyMove(bool report)
        {
            var s = Session;
            bool was = partyMoving;
            partyMoving = false;
            groundHold = false;
            footTimer = 0f;
            if (s == null) return;
            foreach (var kv in views)
            {
                var v = kv.Value;
                if (v == null || !v.IsMoving) continue;
                if (s.IsInParty(kv.Key.Owner ?? kv.Key)) v.StopMoving();
            }
            if (!was) return;
            if (report && s.Mode == SessionMode.Exploration) ReportPositions();
            else SyncUnitPositionsFromViews();
        }

        void ResetExplorationState()
        {
            partyMoving = false;
            groundHold = false;
            footTimer = 0f;
            engageOpener = "";
            ResetPending();
            CancelOpener();
        }

        // ------------------------------------------------------------ hover

        void UpdateHover(SessionMode mode)
        {
            var rig = CameraRig.Instance;
            bool allow = rig != null && (mode == SessionMode.Exploration || mode == SessionMode.Combat)
                         && !UiRoot.ModalActive && !GameInput.PointerOverUi && !pausedByMenu;
            UnitView uv = null;
            MapObject mo = null;
            var map = MapView.Current;
            if (allow)
            {
                var mw = rig.MouseWorld;
                uv = UnitView.Pick(mw);
                if (uv != null && IsBeingRemoved(uv)) uv = null;
                if (uv == null && mode == SessionMode.Exploration && map != null) mo = map.Pick(mw);
            }

            if (!ReferenceEquals(uv, hoveredView))
            {
                if (hoveredView != null) hoveredView.SetHovered(false);
                hoveredView = uv;
                if (uv != null)
                {
                    uv.SetHovered(true);
                    OnUnitHoverStart(uv);
                }
            }
            if (!ReferenceEquals(mo, hoveredObject))
            {
                if (hoveredObject != null && map != null) map.SetHighlighted(hoveredObject.Id, false);
                hoveredObject = mo;
                hoveredObjectLabel = mo != null ? ObjectLabel(mo) : "";
                if (mo != null && map != null) map.SetHighlighted(mo.Id, true);
            }

            if (hoveredView != null)
            {
                var u = UnitOf(hoveredView);
                HoveredLabelWorld = hoveredView.NameplatePosition;
                if (u != null)
                {
                    HoveredUnit = u;
                    HoveredLabel = "";
                    HoveredKind = IsPlayerSide(u) ? HoverKind.PartyMember : u.Team == Team.Enemy ? HoverKind.Enemy : HoverKind.Npc;
                }
                else if (enemyByView.TryGetValue(hoveredView, out var enemy))
                {
                    HoveredUnit = null;
                    HoveredLabel = EnemyHoverLabel(hoveredView, enemy);
                    HoveredKind = HoverKind.Enemy;
                }
                else
                {
                    HoveredUnit = null;
                    HoveredLabel = hoveredView.DisplayName ?? "";
                    HoveredKind = HoverKind.Npc;
                }
            }
            else if (hoveredObject != null)
            {
                HoveredUnit = null;
                HoveredLabel = hoveredObjectLabel;
                HoveredLabelWorld = hoveredObject.LabelPosition;
                HoveredKind = HoverKind.Object;
            }
            else
            {
                HoveredUnit = null;
                HoveredLabel = "";
                HoveredKind = HoverKind.None;
            }
        }

        bool IsBeingRemoved(UnitView v)
        {
            for (int i = 0; i < delayedRemovals.Count; i++) if (ReferenceEquals(delayedRemovals[i].View, v)) return true;
            return false;
        }

        void OnUnitHoverStart(UnitView v)
        {
            if (Session == null || Session.Mode != SessionMode.Exploration) return;
            if (!npcByView.TryGetValue(v, out var e)) return;
            string bark = e.Npc != null ? e.Npc.bark : "";
            if (string.IsNullOrEmpty(bark) || idleClock < e.BarkReady) return;
            e.BarkReady = idleClock + 16f;
            ShowBark(v, bark);
        }

        // ------------------------------------------------------------ encounter hover label

        // cached label of the hovered encounter enemy (rebuilt when the hovered view/entry or the party level changes)
        UnitView enemyLabelView;
        EnemyEntry enemyLabelEntry;
        int enemyLabelPartyLevel = -1;
        string enemyLabel = "";
        readonly System.Text.StringBuilder enemyLabelBuilder = new System.Text.StringBuilder(96);

        /// <summary>
        /// What the player needs before committing to a fight: "Mossling  Lv 3-4  (x3)", "Hollow Warden  Lv 8 Boss",
        /// "Mossling  Lv 5  (group of 3, Boss)". The level is coloured WoW-style by its difference to the party level
        /// (grey/green/yellow/orange/red, as on the target frame); rich-text colour tag, the nameplate styles render it.
        /// </summary>
        string EnemyHoverLabel(UnitView v, EnemyEntry e)
        {
            int party = Session != null ? Session.PartyLevel : 1;
            if (ReferenceEquals(v, enemyLabelView) && ReferenceEquals(e, enemyLabelEntry) && party == enemyLabelPartyLevel) return enemyLabel;
            enemyLabelView = v;
            enemyLabelEntry = e;
            enemyLabelPartyLevel = party;
            try { enemyLabel = BuildEnemyLabel(v, e, party); }
            catch (Exception ex) { LogStageOnce("enemy-label", ex); enemyLabel = v != null ? v.DisplayName ?? "" : ""; }
            return enemyLabel;
        }

        string BuildEnemyLabel(UnitView v, EnemyEntry e, int partyLevel)
        {
            var cdef = e.Creature;
            string name = cdef != null && !string.IsNullOrEmpty(cdef.name) ? cdef.name : (v != null ? v.DisplayName ?? "" : "");
            if (cdef == null) return name;
            var sb = enemyLabelBuilder;
            sb.Length = 0;
            sb.Append(name);

            // level (range) of this creature as the session will roll it (UnitFactory.CreatureLevel)
            CreatureLevelRange(cdef, e.Def != null ? e.Def.level : 0, partyLevel, out int lo, out int hi);
            sb.Append("  <color=").Append(LevelColorHex(hi - partyLevel)).Append(">Lv ").Append(lo);
            if (hi != lo) sb.Append('-').Append(hi);
            sb.Append("</color>");
            string rank = RankWord(cdef.rank);
            if (rank.Length > 0) sb.Append(' ').Append(rank);

            // the size of the fight (and a tougher member when the hovered one is not the leader of the pack)
            var enc = e.Encounter;
            var db = Db;
            if (enc != null && enc.enemies != null && db != null)
            {
                int count = 0;
                bool same = true;
                CreatureRank top = CreatureRank.Normal;
                foreach (var ed in enc.enemies)
                {
                    var c = ed != null ? db.Creature(ed.creature) : null;
                    if (c == null) continue;
                    count++;
                    if (c != cdef) same = false;
                    if (RankDanger(c.rank) > RankDanger(top)) top = c.rank;
                }
                if (count > 1)
                {
                    if (same) sb.Append("  (x").Append(count).Append(')');
                    else
                    {
                        sb.Append("  (group of ").Append(count);
                        if (RankDanger(top) > RankDanger(cdef.rank)) sb.Append(", ").Append(RankWord(top));
                        sb.Append(')');
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Mirror of UnitFactory.CreatureLevel without the roll: the range the level can land in.</summary>
        static void CreatureLevelRange(CreatureDef def, int explicitLevel, int partyLevel, out int lo, out int hi)
        {
            if (explicitLevel > 0) { lo = hi = explicitLevel; return; }
            if (def.scaleToParty) { lo = hi = Mathf.Clamp(partyLevel + def.levelOffset, 1, 63); return; }
            lo = def.levelMin;
            hi = Mathf.Max(def.levelMin, def.levelMax);
        }

        /// <summary>WoW level colours (same thresholds as the target frame): red ≥ +5, orange ≥ +3, yellow ≥ -2, green ≥ -7, grey.</summary>
        static string LevelColorHex(int diff)
        {
            if (diff >= 5) return "#ff4a3a";
            if (diff >= 3) return "#ff8a3a";
            if (diff >= -2) return "#ffe14a";
            if (diff >= -7) return "#5ee05e";
            return "#9d9d9d";
        }

        static string RankWord(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Elite: return "Elite";
                case CreatureRank.Rare: return "Rare";
                case CreatureRank.Boss: return "Boss";
                default: return "";
            }
        }

        static int RankDanger(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Boss: return 3;
                case CreatureRank.Elite: return 2;
                case CreatureRank.Rare: return 1;
                default: return 0;
            }
        }

        void ClearHoverState()
        {
            enemyLabelView = null;
            enemyLabelEntry = null;
            enemyLabelPartyLevel = -1;
            if (hoveredView != null) hoveredView.SetHovered(false);
            hoveredView = null;
            if (hoveredObject != null && MapView.Current != null) MapView.Current.SetHighlighted(hoveredObject.Id, false);
            hoveredObject = null;
            hoveredObjectLabel = "";
            HoveredUnit = null;
            HoveredLabel = "";
            HoveredKind = HoverKind.None;
        }

        /// <summary>Short label for a map object ("Chest", "Locked chest", "To Whisperwood", "Spirit Lantern").</summary>
        string ObjectLabel(MapObject o)
        {
            var s = Session;
            switch (o.Kind)
            {
                case MapObjectKind.Chest:
                {
                    if (s != null && s.Map != null && o.Chest != null)
                    {
                        if (s.Map.IsChestOpened(o.Chest.id)) return "Empty chest";
                        if (s.Map.IsChestLocked(o.Chest)) return "Locked chest";
                    }
                    return o.Opened ? "Empty chest" : "Chest";
                }
                case MapObjectKind.Transition:
                {
                    string name = !string.IsNullOrEmpty(o.Label) ? o.Label : "";
                    if (string.IsNullOrEmpty(name) && o.Transition != null && Db != null && Db.Maps.TryGetValue(o.Transition.targetMap ?? "", out var m)) name = m.name;
                    if (string.IsNullOrEmpty(name)) return "Path";
                    return name.StartsWith("To ", StringComparison.OrdinalIgnoreCase) ? name : "To " + name;
                }
                case MapObjectKind.Prop:
                    return PropName(o.Prop != null ? o.Prop.art : "");
                default:
                    return o.Label ?? "";
            }
        }

        static readonly Dictionary<string, string> PropNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "prop_spirit_lantern", "Spirit Lantern" },
            { "prop_noticeboard", "Notice Board" },
            { "prop_signpost", "Signpost" },
            { "prop_spirit_statue", "Spirit Statue" },
            { "prop_shrine_gate", "Shrine Gate" },
            { "prop_tree_great", "Old Camphor Tree" },
            { "prop_lamp_post", "Lamp Post" },
            { "prop_blight_crystal", "Blight Crystal" },
            { "prop_campfire", "Campfire" },
        };

        static string PropName(string art)
        {
            if (string.IsNullOrEmpty(art)) return "Inspect";
            if (PropNames.TryGetValue(art, out var cached)) return cached;
            string key = art.EndsWith("_dark", StringComparison.Ordinal) ? art.Substring(0, art.Length - 5) : art;
            if (PropNames.TryGetValue(key, out var n)) { PropNames[art] = n; return n; }
            if (key.StartsWith("prop_", StringComparison.Ordinal)) key = key.Substring(5);
            var parts = key.Split('_');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p.Length == 0) continue;
                if (p.Length == 1 && i == parts.Length - 1 && parts.Length > 1) continue;   // "_a" / "_b" art variants
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(p[0])).Append(p, 1, p.Length - 1);
            }
            var r = sb.Length > 0 ? sb.ToString() : "Inspect";
            PropNames[art] = r;
            return r;
        }
    }
}
