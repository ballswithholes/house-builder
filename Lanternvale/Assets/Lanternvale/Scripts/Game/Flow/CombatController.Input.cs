// CombatController — the player's turn: movement range overlay (ReachMap of the battle NavGrid), path preview
// with remaining metres, hover preview text, valid-target / AoE highlights, targeting mode for abilities and items,
// BG3-style smart clicks (attack an enemy: start the basic attack, walking into reach first when movement allows;
// help a downed ally up), click-to-move, Space/Enter to end the turn, right click/Esc to cancel targeting.
//
// All world input goes through GameInput. Clicks over UI panels (Ui.Panel/Ui.Btn/Ui.Block) are suppressed by
// GameInput.WorldClick; modal screens and GameFlow.WorldInputEnabled = false block world input entirely.
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class CombatController
    {
        // FxSystem preview ids owned by the combat controller
        const string MoveRangeId = "combat_move_range";
        const string PathId = "combat_path";
        const string AoeId = "combat_aoe";
        const string RangeId = "combat_range";
        const string AiPathId = "combat_ai_path";

        static readonly Color MoveRangeColor = new Color(0.68f, 0.9f, 1f, 0.95f);
        static readonly Color PathColor = new Color(1f, 0.86f, 0.5f, 0.95f);
        static readonly Color PathFarColor = new Color(1f, 0.62f, 0.45f, 0.9f);
        static readonly Color HostileColor = new Color(1f, 0.45f, 0.38f, 1f);
        static readonly Color FriendlyColor = new Color(0.5f, 0.95f, 0.55f, 1f);
        static readonly Color InvalidColor = new Color(0.7f, 0.66f, 0.75f, 0.6f);
        static readonly Color RangeRingColor = new Color(1f, 1f, 1f, 0.28f);

        // ---------------------------------------------------------------- state
        Unit turnUnit;
        bool playerVisualsShown, moveRangeShown, moveRangeDirty;
        bool hoverDirty = true;
        HoverKey lastKey;
        AbilityModSet targetingMods;

        ReachMap reach;
        bool reachValid;
        Unit reachUnit;
        int reachStamp = -1, reachGridVersion = -1;
        Vec2 reachPos;
        float reachMove = -1f;

        readonly NavPath navTmp = new NavPath();
        readonly NavPath approachTmp = new NavPath();
        readonly List<Vec2> planPoints = new List<Vec2>(32);
        float planLength;
        bool planReached, planTruncated;
        readonly List<Vec2> approachPoints = new List<Vec2>(32);
        UseFailure lastCheckCode;

        int validStamp = -1;
        AbilityDef validFor;
        Unit validUnit;
        int validRank;
        readonly Dictionary<Unit, bool> validTargets = new Dictionary<Unit, bool>();

        AbilityDef rangeRingFor;
        int rangeRingStamp = -1;

        readonly Dictionary<UnitView, Color> wantHl = new Dictionary<UnitView, Color>();
        readonly Dictionary<UnitView, Color> haveHl = new Dictionary<UnitView, Color>();
        readonly List<UnitView> hlTmp = new List<UnitView>();
        readonly StringBuilder sb = new StringBuilder(128);

        struct HoverKey : IEquatable<HoverKey>
        {
            public int Unit, Hover, Cx, Cy, Stamp, Rank;
            public AbilityDef Ability;
            public ItemInstance Item;
            public bool OverUi;

            public bool Equals(HoverKey o) =>
                Unit == o.Unit && Hover == o.Hover && Cx == o.Cx && Cy == o.Cy && Stamp == o.Stamp && Rank == o.Rank &&
                ReferenceEquals(Ability, o.Ability) && ReferenceEquals(Item, o.Item) && OverUi == o.OverUi;

            public override bool Equals(object obj) => obj is HoverKey k && Equals(k);
            public override int GetHashCode() => Unit ^ (Hover << 8) ^ (Cx << 16) ^ Cy ^ Stamp;
        }

        struct SmartPlan
        {
            public AbilityDef Ability;
            public bool Approach;
            public bool Behind;          // the approach goes behind the target (Backstab…)
            public float ApproachLength;
            public string Error;
            public bool AlreadyAttacking;
        }

        // ================================================================ per frame

        void UpdatePlayerTurn()
        {
            var u = Battle.ActiveUnit;
            if (u == null) return;
            if (u != turnUnit)
            {
                CancelTargeting();
                turnUnit = u;
                hoverDirty = true;
            }
            playerVisualsShown = true;
            bool blocked = UiRoot.ModalActive || (flow != null && !flow.WorldInputEnabled);

            if (!blocked)
            {
                // Esc cancels targeting instead of opening the pause menu (while a modal window is up — a confirm
                // prompt — Esc belongs to that window, so hotkeys are left alone then)
                if (IsTargeting) UiRoot.HotkeysSuppressed = true;
                if (IsTargeting && (GameInput.KeyDown(KeyCode.Escape) || (GameInput.MouseDown(1) && !GameInput.PointerOverUi)))
                {
                    CancelTargeting();
                    UiRoot.HotkeysSuppressed = true;
                    return;
                }
                if (GameInput.KeyDown(KeyCode.Space) || GameInput.KeyDown(KeyCode.Return) || GameInput.KeyDown(KeyCode.KeypadEnter))
                {
                    EndTurn();
                    return;
                }
            }

            // a downed unit offered a self-resurrection: the UI shows the prompt (AnswerSelfResurrection)
            var offer = Battle.PendingSelfResurrection(u);
            if (offer != null)
            {
                HideTurnPreviews();
                ClearHighlights();
                HoveredTarget = null;
                if (hoverDirty || lastKey.Unit != -u.Id)
                {
                    lastKey = new HoverKey { Unit = -u.Id };
                    hoverDirty = false;
                    HoverPreview = (string.IsNullOrEmpty(offer.Name) ? "Resurrection" : offer.Name) + ": rise now, or wait (Space).";
                }
                return;
            }

            var rig = CameraRig.Instance;
            if (rig == null) return;
            bool overUi = blocked || GameInput.PointerOverUi;
            var mouse = rig.MouseWorld;
            var hover = overUi ? null : PickUnit(GameInput.MousePosition);
            HoveredTarget = hover;

            UpdateMoveRange(u);
            UpdateFacing(u);

            var key = MakeKey(u, hover, mouse, overUi);
            if (hoverDirty || !key.Equals(lastKey))
            {
                lastKey = key;
                hoverDirty = false;
                RecomputeHover(u, hover, mouse, overUi);
            }

            if (!blocked && GameInput.WorldClick(0))
            {
                try { OnLeftClick(u, hover, mouse); }
                catch (Exception e) { LogOnce("click", "Combat click failed: " + e); }
                hoverDirty = true;
            }
        }

        HoverKey MakeKey(Unit u, Unit hover, Vector2 mouse, bool overUi)
        {
            var a = TargetingAbility;
            float q = a != null && (a.target == TargetType.Point || IsAimed(a)) ? 0.1f : 0.25f;
            return new HoverKey
            {
                Unit = u.Id, Hover = hover != null ? hover.Id : 0, Cx = Mathf.FloorToInt(mouse.x / q), Cy = Mathf.FloorToInt(mouse.y / q),
                Stamp = Battle.Events.Count, Ability = a, Item = TargetingItem, OverUi = overUi, Rank = TargetingRank,
            };
        }

        void HidePlayerTurnVisuals()
        {
            if (!playerVisualsShown) return;
            playerVisualsShown = false;
            HideTurnPreviews();
            ClearHighlights();
            HoverPreview = "";
            HoveredTarget = null;
            hoverDirty = true;
        }

        void HideTurnPreviews()
        {
            if (moveRangeShown) { FxSystem.Hide(MoveRangeId); moveRangeShown = false; }
            FxSystem.Hide(PathId);
            FxSystem.Hide(AoeId);
            FxSystem.Hide(RangeId);
            rangeRingFor = null;
            HideFacingIndicators();
        }

        /// <summary>
        /// Front-most battle unit whose body is under the screen position (pixels, bottom-left origin). Views that are not
        /// part of this fight (map NPCs, other encounters' enemies) are skipped instead of hiding the battle unit behind
        /// them. Dead bodies count only while targeting an ability for dead allies (resurrection).
        /// </summary>
        Unit PickUnit(Vector2 screen)
        {
            var ta = TargetingAbility;
            bool includeDead = ta != null && ta.target == TargetType.DeadAlly;
            var all = UnitView.All;
            UnitView best = null;
            Unit bestUnit = null;
            float bestDepth = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null || !v.Visible || (!includeDead && v.IsDead)) continue;
                if (!v.HitTestScreen(screen, out float depth)) continue;
                if (best != null && depth >= bestDepth) continue;
                var u = UnitOfView(v);
                if (u == null || !Battle.Units.Contains(u)) continue;
                if (v.IsDead && (Battle.ActiveUnit == null || !u.IsFriendlyTo(Battle.ActiveUnit))) continue;   // enemy corpses
                best = v;
                bestUnit = u;
                bestDepth = depth;
            }
            return bestUnit;
        }

        // ================================================================ facing

        // Sprites only flip left/right, but the rules' facing is a 2D vector (Unit.IsBehind). While the active unit
        // knows an ability that needs it behind its target (Backstab, Ambush, Garrote, Ravage) every visible enemy
        // shows its "behind" zone (the arc where those abilities work, as far as melee reach) — brighter while such
        // an ability is being targeted. Views are also turned to match the rules' facing whenever the screen is idle.
        const string FacingIdPrefix = "combat_facing:";
        const float BehindArcDeg = 160f;   // the rules' behind arc is ±84° around the back (BehindDot −0.1)
        static readonly Color BehindZoneColor = new Color(1f, 0.84f, 0.42f, 0.32f);
        static readonly Color BehindZoneTargetingColor = new Color(1f, 0.86f, 0.4f, 0.7f);

        readonly Dictionary<int, string> facingIds = new Dictionary<int, string>();
        readonly List<Unit> facingShown = new List<Unit>(8);
        readonly List<Unit> facingNext = new List<Unit>(8);
        bool facingValid;
        int facingStamp = -1;
        Unit facingFor;
        bool facingStrong;
        Unit behindKnowsUnit;
        int behindKnowsCount = -1;
        bool behindKnows;
        int flipStamp = -1;

        void UpdateFacing(Unit u)
        {
            int stamp = Battle.Events.Count;
            if (stamp != flipStamp)
            {
                flipStamp = stamp;
                SyncViewFacings();
            }
            var ta = TargetingAbility;
            bool strong = ta != null && ta.requires != null && ta.requires.behindTarget;
            if (!strong && !KnowsBehindAbility(u)) { HideFacingIndicators(); return; }
            if (facingValid && stamp == facingStamp && u == facingFor && strong == facingStrong) return;
            facingValid = true;
            facingStamp = stamp;
            facingFor = u;
            facingStrong = strong;
            facingNext.Clear();
            var col = strong ? BehindZoneTargetingColor : BehindZoneColor;
            foreach (var o in Battle.Units)
            {
                if (o == null || !o.IsAlive || o.IsTotem || !o.IsHostileTo(u)) continue;
                bool seen;
                try { seen = Battle.CanSee(u, o); } catch (Exception) { seen = true; }
                if (!seen) continue;
                var v = V(o);
                if (v == null || !v.Visible || v.IsDead) continue;
                var f = o.Facing.Normalized;
                if (f.SqrLength < 1e-6f) continue;
                float r = Mathf.Max(0.8f, Battle.MeleeReachOf(u, o));
                FxSystem.ShowCone(FacingId(o), v.FeetPosition, new Vector2(-f.x, -f.y), r, BehindArcDeg, col);
                facingNext.Add(o);
            }
            for (int i = 0; i < facingShown.Count; i++)
                if (!facingNext.Contains(facingShown[i])) FxSystem.Hide(FacingId(facingShown[i]));
            facingShown.Clear();
            facingShown.AddRange(facingNext);
        }

        void HideFacingIndicators()
        {
            facingValid = false;
            for (int i = 0; i < facingShown.Count; i++) FxSystem.Hide(FacingId(facingShown[i]));
            facingShown.Clear();
        }

        string FacingId(Unit o)
        {
            if (!facingIds.TryGetValue(o.Id, out var id)) facingIds[o.Id] = id = FacingIdPrefix + o.Id;
            return id;
        }

        bool KnowsBehindAbility(Unit u)
        {
            if (u == behindKnowsUnit && u.Abilities.Count == behindKnowsCount) return behindKnows;
            behindKnowsUnit = u;
            behindKnowsCount = u.Abilities.Count;
            behindKnows = false;
            var db = Db;
            if (db == null) return false;
            foreach (var kv in u.Abilities)
            {
                var a = db.Ability(kv.Key);
                if (a != null && !a.passive && a.requires != null && a.requires.behindTarget) { behindKnows = true; break; }
            }
            return behindKnows;
        }

        /// <summary>Turns idle views left/right to match the rules' facing (it changes without a visible event, e.g. an
        /// enemy turning to its target at the end of its turn).</summary>
        void SyncViewFacings()
        {
            foreach (var o in Battle.Units)
            {
                if (o == null || !o.IsAlive) continue;
                float fx = o.Facing.x;
                if (Mathf.Abs(fx) <= 0.1f) continue;
                var v = V(o);
                if (v == null || v.IsMoving || v.IsDead || v.IsDowned) continue;
                int dir = fx >= 0f ? 1 : -1;
                if (v.Facing != dir) v.SetFacing(dir);
            }
        }

        // ================================================================ movement range & paths

        NavGrid Grid
        {
            get
            {
                if (Battle != null && Battle.Pathfinder is Lanternvale.Session.NavGridPathfinder p) return p.Grid;
                var s = flow != null ? flow.Session : null;
                return s != null ? s.Nav : null;
            }
        }

        ReachMap EnsureReach(Unit u)
        {
            var grid = Grid;
            if (grid == null || u == null) return null;
            int stamp = Battle.Events.Count;
            if (reachUnit == u && reachStamp == stamp && reachGridVersion == grid.Version && reachPos == u.Position &&
                Mathf.Abs(reachMove - u.MoveLeft) < 1e-4f)
                return reachValid ? reach : null;
            reachUnit = u;
            reachStamp = stamp;
            reachGridVersion = grid.Version;
            reachPos = u.Position;
            reachMove = u.MoveLeft;
            reachValid = false;
            moveRangeDirty = true;
            if (u.MoveLeft <= 0.05f || Battle.CannotMoveReason(u) != null) return null;
            try
            {
                reach = grid.ReachableWithin(u.Position, u.MoveLeft, NavAgent.ForUnit(u.Radius, u.Id), reach);
                reachValid = reach != null;
            }
            catch (Exception e) { LogOnce("reach", "Movement range failed: " + e.Message); }
            return reachValid ? reach : null;
        }

        void UpdateMoveRange(Unit u)
        {
            var r = EnsureReach(u);
            bool want = ShowMoveRange && r != null && !IsTargeting;
            if (!want)
            {
                if (moveRangeShown) { FxSystem.Hide(MoveRangeId); moveRangeShown = false; }
                return;
            }
            if (!moveRangeShown || moveRangeDirty)
            {
                FxSystem.ShowMoveRange(MoveRangeId, r, MoveRangeColor);
                moveRangeShown = true;
                moveRangeDirty = false;
            }
        }

        /// <summary>Path the active unit would walk to goal this turn (fills planPoints/planLength).</summary>
        bool PlanMove(Unit u, Vec2 goal)
        {
            planPoints.Clear();
            planLength = 0f;
            planReached = planTruncated = false;
            var grid = Grid;
            if (grid != null)
            {
                NavPath p = null;
                var r = EnsureReach(u);
                if (r != null && r.CanReach(goal))
                {
                    p = r.PathTo(goal, navTmp);
                    planReached = p.Status != PathStatus.NoPath && !p.IsEmpty;
                }
                if (!planReached)
                {
                    p = grid.FindPath(u.Position, goal, NavAgent.ForUnit(u.Radius, u.Id), Mathf.Max(0f, u.MoveLeft), navTmp);
                    planReached = p.ReachedGoal;
                    planTruncated = p.Truncated;
                }
                if (p == null || p.Status == PathStatus.NoPath || p.IsEmpty) return false;
                planPoints.AddRange(p.Points);
                planLength = p.Length;
                return planLength > 0.05f;
            }
            var pr = Battle.PreviewMove(u, goal);
            if (pr == null || !pr.Found || pr.Points.Count < 2) return false;
            planPoints.AddRange(pr.Points);
            planLength = pr.Length;
            planReached = !pr.Truncated;
            planTruncated = pr.Truncated;
            return planLength > 0.05f;
        }

        /// <summary>Path that brings the unit into range of a target unit or point this turn (fills approachPoints).</summary>
        bool TryApproach(Unit u, AbilityDef a, Unit target, Vec2? point, out float length, out string why)
        {
            approachPoints.Clear();
            length = 0f;
            why = null;
            var grid = Grid;
            if (grid == null || a == null || (target == null && !point.HasValue)) return false;
            var cannot = Battle.CannotMoveReason(u);
            if (cannot != null) { why = "Out of range (" + cannot.TrimEnd('.').ToLowerInvariant() + ")."; return false; }
            AbilityModSet mods;
            try { mods = AbilityMods.For(u, a); } catch (Exception) { mods = AbilityModSet.Empty; }
            float range;
            if (a.target == TargetType.Point || target == null)
                range = a.range > 0f ? MathUtil.Yd(a.range * (1f + mods.RangePct / 100f)) : float.PositiveInfinity;
            else
            {
                range = AbilityRules.RangeMetres(u, a, target, mods, Battle.Config);
                if (a.autoAttack && u.Class == null && u.Creature != null && AbilityRules.IsRangedWeaponAbility(a))
                    range = MathUtil.Yd(u.Creature.rangedRange) + target.Radius;
            }
            if (float.IsInfinity(range) || range <= 0f) return false;
            var goal = target != null ? target.Position : point.Value;
            bool los = !AbilityRules.UsesMeleeReach(a);
            var agent = NavAgent.ForUnit(u.Radius, u.Id, target != null ? target.Id : NavAgent.NoId);
            var p = grid.FindPathToRange(u.Position, goal, Mathf.Max(0.1f, range - 0.15f), agent, Mathf.Max(0f, u.MoveLeft), los, approachTmp);
            if (p.Status == PathStatus.NoPath) { why = "No path to the target."; return false; }
            if (p.Truncated || p.Status != PathStatus.Complete)
            {
                float more = Mathf.Max(0.1f, p.FullLength - u.MoveLeft);
                why = p.Truncated ? "Out of range (needs " + more.ToString("0.0") + " m more movement)." : "Cannot get into range from here.";
                return false;
            }
            if (p.IsEmpty) return false;
            approachPoints.AddRange(p.Points);
            length = p.Length;
            return true;
        }

        // ================================================================ hover preview

        void RecomputeHover(Unit u, Unit hover, Vector2 mouse, bool overUi)
        {
            wantHl.Clear();
            string text;
            try
            {
                if (IsTargeting) text = TargetingPreview(u, hover, mouse, overUi);
                else if (overUi) { FxSystem.Hide(PathId); FxSystem.Hide(AoeId); text = ""; }
                else if (hover != null && hover != u) text = UnitPreview(u, hover);
                else if (hover == u) text = SelfPreview(u);
                else text = GroundPreview(u, mouse);
            }
            catch (Exception e)
            {
                LogOnce("hover", "Hover preview failed: " + e);
                text = "";
            }
            HoverPreview = text ?? "";
            CommitHighlights();
        }

        string GroundPreview(Unit u, Vector2 mouse)
        {
            FxSystem.Hide(AoeId);
            var why = Battle.CannotMoveReason(u);
            if (why != null)
            {
                FxSystem.Hide(PathId);
                return u.MoveLeft <= 0.05f && u.CanMoveNow ? "No movement left. Use an ability or press Space to end the turn." : why + WaitHint(u);
            }
            if (!PlanMove(u, ToVec(mouse)))
            {
                FxSystem.Hide(PathId);
                return "Cannot move there.";
            }
            FxSystem.ShowPath(PathId, planPoints, planReached ? PathColor : PathFarColor, true);
            float left = Mathf.Max(0f, u.MoveLeft - planLength);
            sb.Length = 0;
            sb.Append("Move ").Append(planLength.ToString("0.0")).Append(" m (").Append(left.ToString("0.0")).Append(" m left)");
            if (!planReached) sb.Append(planTruncated ? " · too far to reach this turn" : " · as close as possible");
            return sb.ToString();
        }

        string SelfPreview(Unit u)
        {
            FxSystem.Hide(PathId);
            FxSystem.Hide(AoeId);
            sb.Length = 0;
            sb.Append(u.Name).Append(" · ").Append(Mathf.Max(0f, u.TimeLeft).ToString("0.0")).Append(" s and ")
              .Append(Mathf.Max(0f, u.MoveLeft).ToString("0.0")).Append(" m left this turn");
            // a silence/root/lockout left from the start of the turn runs out as Time is spent: clicking yourself waits
            float free = FreeIn(u, out string what);
            if (free > 0.01f) sb.Append(" · click to wait ").Append(free.ToString("0.0")).Append(" s until the ").Append(what).Append(" wears off");
            sb.Append(" · Space ends the turn");
            return sb.ToString();
        }

        /// <summary>
        /// Seconds of Time until every window left from the start of the turn has run out: silence, pacify and root (only
        /// when no aura still applies them) and school lockouts. what = the longest one ("silence", "Fire lockout").
        /// </summary>
        float FreeIn(Unit u, out string what)
        {
            what = "";
            if (u == null || !u.InOwnTurn || u.TimeLeft <= 0.01f) return 0f;
            float clock = u.TurnClock, best = 0f;
            try
            {
                CheckWindow(u, UnitState.Silence, "silence", clock, ref best, ref what);
                CheckWindow(u, UnitState.Pacify, "pacify", clock, ref best, ref what);
                if (u.MoveLeft > 0.05f) CheckWindow(u, UnitState.Root, "root", clock, ref best, ref what);
                foreach (var kv in u.LockoutWindow)
                {
                    float left = kv.Value - clock;
                    if (left > best + 1e-4f) { best = left; what = LockoutName(kv.Key); }
                }
            }
            catch (Exception) { return 0f; }
            return Mathf.Min(best, Mathf.Max(0f, u.TimeLeft));
        }

        static readonly Dictionary<School, string> lockoutNames = new Dictionary<School, string>();

        static string LockoutName(School s)
        {
            if (!lockoutNames.TryGetValue(s, out var n)) lockoutNames[s] = n = s + " lockout";
            return n;
        }

        static void CheckWindow(Unit u, UnitState s, string name, float clock, ref float best, ref string what)
        {
            int i = (int)s;
            if (i < 0 || i >= u.StateWindow.Length || u.HasStateAura(s)) return;
            float left = u.StateWindow[i] - clock;
            if (left > best + 1e-4f) { best = left; what = name; }
        }

        /// <summary>" Click yourself to wait 2.0 s until the root wears off." or "".</summary>
        string WaitHint(Unit u)
        {
            float free = FreeIn(u, out string what);
            return free > 0.01f ? " Click yourself to wait " + free.ToString("0.0") + " s until the " + what + " wears off." : "";
        }

        /// <summary>Caches keyed on the event count (waiting changes the turn clock without an event).</summary>
        void InvalidateTurnCaches()
        {
            reachUnit = null;
            validStamp = -1;
            rangeRingFor = null;
            facingValid = false;
            hoverDirty = true;
        }

        string UnitPreview(Unit u, Unit target)
        {
            FxSystem.Hide(AoeId);
            if (target.IsHostileTo(u))
            {
                if (!target.IsAlive) { FxSystem.Hide(PathId); return target.Name; }
                var plan = PlanSmartAttack(u, target);
                Want(target, plan.Error == null ? HostileColor : InvalidColor);
                ShowApproach(plan);
                if (plan.Error != null)
                    return plan.Ability != null ? plan.Ability.name + " → " + target.Name + ": " + plan.Error : target.Name + ": " + plan.Error;
                if (plan.AlreadyAttacking)
                    return "Attacking " + target.Name + " · auto attacks swing at the end of your turn (Space).";
                return Line(u, plan.Ability, target, null, null, plan);
            }
            if (target.IsFriendlyTo(u) && target.Downed)
            {
                var help = Db?.Ability("help_up");
                if (help != null)
                {
                    var plan = PlanUse(u, help, target, null, false);
                    Want(target, plan.Error == null ? FriendlyColor : InvalidColor);
                    ShowApproach(plan);
                    if (plan.Error != null) return help.name + " → " + target.Name + ": " + plan.Error;
                    return Line(u, help, target, null, null, plan);
                }
            }
            FxSystem.Hide(PathId);
            sb.Length = 0;
            sb.Append(target.Name).Append(" · ").Append(Mathf.CeilToInt(Mathf.Max(0f, target.Health))).Append('/')
              .Append(Mathf.CeilToInt(target.MaxHealth)).Append(" HP");
            return sb.ToString();
        }

        void ShowApproach(SmartPlan plan)
        {
            if (plan.Error == null && plan.Approach && approachPoints.Count >= 2) FxSystem.ShowPath(PathId, approachPoints, PathColor, true);
            else FxSystem.Hide(PathId);
        }

        string TargetingPreview(Unit u, Unit hover, Vector2 mouse, bool overUi)
        {
            var a = TargetingAbility;
            if (a == null) return "";
            bool fromItem = TargetingItem != null;
            int rank = fromItem ? 0 : TargetingRank;
            var mods = targetingMods;
            if (mods == null)
            {
                try { mods = AbilityMods.For(u, a); } catch (Exception) { mods = AbilityModSet.Empty; }
                targetingMods = mods;
            }
            ShowRangeRing(u, a, mods);
            bool aimed = IsAimed(a);
            bool pointed = a.target == TargetType.Point || aimed;
            if (!pointed) WantValidTargets(u, a, fromItem, rank);

            string label = fromItem ? TargetingItem.Name : RankedName(u, a, rank);
            if (overUi && pointed)
            {
                FxSystem.Hide(AoeId);
                FxSystem.Hide(PathId);
                return label + ": choose a location (right click to cancel).";
            }
            Unit tgt = pointed ? null : hover;
            Vec2? point = pointed ? AimPoint(u, a, hover, mouse) : null;

            // area preview + affected units
            int enemies = -1, allies = -1;
            if (a.area.shape != AreaShape.None && (tgt != null || point.HasValue))
            {
                var shape = Targeting.AreaOf(u, a, tgt, point, mods);
                ShowShape(shape, a);
                var hit = Targeting.AreaUnits(Battle, u, a, tgt, point, mods);
                enemies = allies = 0;
                for (int i = 0; i < hit.Count; i++)
                {
                    bool hostile = hit[i].IsHostileTo(u);
                    if (hostile) enemies++; else allies++;
                    Want(hit[i], hostile ? HostileColor : FriendlyColor);
                }
            }
            else FxSystem.Hide(AoeId);

            if (!pointed && tgt == null)
            {
                FxSystem.Hide(PathId);
                sb.Length = 0;
                // targeting entered for a rage strike without the rage (Begin): say what a click on an enemy does
                if (!fromItem && RageStrike(u, a))
                {
                    var chk = Battle.CanUseIgnoringTarget(u, a, false, rank);
                    if (!chk.Ok && chk.Code == UseFailure.Resource)
                    {
                        sb.Append(label).Append(": ").Append(chk.Reason.TrimEnd('.').Split('.')[0])
                          .Append(" — click an enemy to attack it first: your swings build rage (right click to cancel)");
                        return sb.ToString();
                    }
                }
                sb.Append(label).Append(": ").Append(a.target == TargetType.Enemy ? "choose an enemy" : "choose a target")
                  .Append(" · ").Append(TimeText(u, a, mods, rank)).Append(" (right click to cancel)");
                return sb.ToString();
            }
            var plan = PlanUse(u, a, tgt, point, fromItem, rank, true);
            if (plan.Error != null && RageFallback(u, a, TargetingItem, tgt)) return RageFallbackPreview(u, a, tgt, label, plan.Error);
            if (tgt != null) Want(tgt, plan.Error != null ? InvalidColor : tgt.IsHostileTo(u) ? HostileColor : FriendlyColor);
            ShowApproach(plan);
            if (plan.Error != null) return tgt != null ? label + " → " + tgt.Name + ": " + plan.Error : label + ": " + plan.Error;
            return Line(u, a, tgt, mods, null, plan, enemies, allies, label, rank);
        }

        void ShowShape(AreaShapeInfo shape, AbilityDef a)
        {
            var col = Color.Lerp(Ui.SchoolColor(a.school), Color.white, 0.15f);
            col.a = 0.95f;
            var c = ToV(shape.Center);
            switch (shape.Shape)
            {
                case AreaShape.Circle:
                    FxSystem.ShowCircle(AoeId, c, Mathf.Max(0.2f, shape.Radius), col);
                    break;
                case AreaShape.Cone:
                    FxSystem.ShowCone(AoeId, c, ToV(shape.Direction), Mathf.Max(0.2f, shape.Radius), shape.Angle, col);
                    break;
                case AreaShape.Line:
                    FxSystem.ShowLine(AoeId, c, ToV(shape.Center + shape.Direction * shape.Radius), Mathf.Max(0.2f, shape.Width), col);
                    break;
                default:
                    FxSystem.Hide(AoeId);
                    break;
            }
        }

        void ShowRangeRing(Unit u, AbilityDef a, AbilityModSet mods)
        {
            int stamp = Battle.Events.Count;
            if (rangeRingFor == a && rangeRingStamp == stamp) return;
            rangeRingFor = a;
            rangeRingStamp = stamp;
            float range = 0f;
            if (!AbilityRules.UsesMeleeReach(a) && a.range > 0f) range = MathUtil.Yd(a.range * (1f + mods.RangePct / 100f));
            if (range <= 0.5f || range > 45f || IsAimed(a)) { FxSystem.Hide(RangeId); return; }
            FxSystem.ShowCircle(RangeId, Feet(u, V(u)), range, RangeRingColor);
        }

        void WantValidTargets(Unit u, AbilityDef a, bool fromItem, int rank)
        {
            int stamp = Battle.Events.Count;
            if (validStamp != stamp || validFor != a || validUnit != u || validRank != rank)
            {
                validStamp = stamp;
                validFor = a;
                validUnit = u;
                validRank = rank;
                validTargets.Clear();
                foreach (var o in Battle.Units)
                {
                    // downed/dead allies are checked too (Help, resurrections): CanUse decides whether they are valid
                    if (o == null || (!o.IsAlive && (o.IsHostileTo(u) || !o.IsDeadOrDowned))) continue;
                    UseCheck c;
                    try { c = Battle.CanUse(u, a, o, null, fromItem, rank); }
                    catch (Exception) { continue; }
                    if (c.Ok) validTargets[o] = true;
                    else if (c.Code == UseFailure.Range || c.Code == UseFailure.LineOfSight) validTargets[o] = false;
                    // too close for a minimum range (Charge, hunter shots): a click steps back first
                    else if (c.Code == UseFailure.TooClose && o != u && o.IsHostileTo(u)) validTargets[o] = false;
                    // a rage strike without the rage: a click attacks that enemy first (RageFallback)
                    else if (c.Code == UseFailure.Resource && !fromItem && o.IsAlive && o.IsHostileTo(u) && RageStrike(u, a) && AttackingBuildsRageNow(u)) validTargets[o] = false;
                    // Backstab & co. on an enemy the unit is not behind yet (a click walks behind it)
                    else if (c.Code == UseFailure.Requirement && a.requires != null && a.requires.behindTarget && o != u && !u.IsBehind(o)) validTargets[o] = false;
                }
            }
            foreach (var kv in validTargets)
            {
                var col = kv.Key.IsHostileTo(u) ? HostileColor : FriendlyColor;
                col.a = kv.Value ? 0.6f : 0.3f;
                Want(kv.Key, col);
            }
        }

        // ================================================================ plans

        /// <summary>
        /// What a click with the ability on the target/point does: use it, walk into range (or behind) first, or why not.
        /// <paramref name="stepBack"/> (explicit targeting only, never the smart click): a target inside the ability's minimum
        /// range (Charge, hunter shots) makes the unit step back out of it first when movement allows.
        /// </summary>
        SmartPlan PlanUse(Unit u, AbilityDef a, Unit target, Vec2? point, bool fromItem, int rank = 0, bool stepBack = false)
        {
            var plan = new SmartPlan { Ability = a };
            approachPoints.Clear();
            lastCheckCode = UseFailure.None;
            if (a == null) { plan.Error = "Nothing to use."; return plan; }
            var chk = Battle.CanUse(u, a, target, point, fromItem, rank);
            lastCheckCode = chk.Code;
            if (chk.Ok) return plan;
            // Backstab, Ambush, Garrote, Ravage: the requirement is checked before the range, so both failures lead here
            bool behind = a.requires != null && a.requires.behindTarget && target != null && target != u &&
                          (chk.Code == UseFailure.Range || (chk.Code == UseFailure.Requirement && !u.IsBehind(target)));
            if (behind)
            {
                if (TryApproachBehind(u, a, target, chk, fromItem, rank, out float blen, out string bwhy))
                {
                    plan.Approach = true;
                    plan.Behind = true;
                    plan.ApproachLength = blen;
                    return plan;
                }
                plan.Error = bwhy ?? chk.Reason;
                return plan;
            }
            // out of range, or out of sight with a spot in sight reachable this turn: walk there first
            bool sight = chk.Code == UseFailure.LineOfSight && !AbilityRules.UsesMeleeReach(a);
            if (chk.Code == UseFailure.Range || sight)
            {
                if (TryApproach(u, a, target, point, out float len, out string why))
                {
                    plan.Approach = true;
                    plan.ApproachLength = len;
                    return plan;
                }
                plan.Error = sight ? chk.Reason : why ?? chk.Reason;
                return plan;
            }
            if (stepBack && chk.Code == UseFailure.TooClose && target != null && target != u)
            {
                if (TryStepBack(u, a, target, fromItem, rank, out float slen, out string swhy))
                {
                    plan.Approach = true;
                    plan.ApproachLength = slen;
                    return plan;
                }
                plan.Error = swhy ?? chk.Reason;
                return plan;
            }
            plan.Error = chk.Reason;
            return plan;
        }

        /// <summary>
        /// Path to a spot outside the ability's minimum range of the target (and inside its maximum range, in sight)
        /// reachable this turn: a warrior standing next to the enemies at the start of a fight steps back to Charge, a hunter
        /// in the dead zone steps out to shoot (fills approachPoints). The ability is checked as if the unit stood there.
        /// </summary>
        bool TryStepBack(Unit u, AbilityDef a, Unit target, bool fromItem, int rank, out float length, out string why)
        {
            approachPoints.Clear();
            length = 0f;
            why = null;
            var grid = Grid;
            if (grid == null) return false;
            var cannot = Battle.CannotMoveReason(u);
            if (cannot != null) { why = "Target is too close (" + cannot.TrimEnd('.').ToLowerInvariant() + ")."; return false; }
            float min = AbilityRules.MinRangeMetres(a);
            if (a.requires != null && a.requires.outOfMeleeRange) min = Mathf.Max(min, MathUtil.Yd(8f) + target.Radius);
            if (min <= 0f) return false;
            var away = u.Position - target.Position;
            if (away.SqrLength < 1e-6f) away = u.Facing * -1f;
            if (away.SqrLength < 1e-6f) away = Vec2.Right;
            away = away.Normalized;
            var r = EnsureReach(u);
            if (r == null) return false;
            var agent = NavAgent.ForUnit(u.Radius, u.Id);
            Vec2 best = default;
            float bestLen = float.PositiveInfinity;
            string blocked = null;
            bool any = false;
            foreach (float extra in StepBackExtra)
            {
                float dist = min + extra;
                for (int i = 0; i < StepBackAngles.Length; i++)
                {
                    float ang = StepBackAngles[i] * Mathf.Deg2Rad;
                    float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    var dir = new Vec2(away.x * cs - away.y * sn, away.x * sn + away.y * cs);
                    var goal = target.Position + dir * dist;
                    if (!grid.IsWalkable(goal, agent)) continue;
                    any = true;
                    if (!r.CanReach(goal)) continue;
                    float walk = r.DistanceTo(goal);
                    if (walk >= bestLen || walk > u.MoveLeft + 1e-3f) continue;
                    var sim = CheckFrom(u, a, target, goal, fromItem, rank);
                    if (!sim.Ok) { blocked ??= sim.Reason; continue; }
                    bestLen = walk;
                    best = goal;
                }
                if (!float.IsInfinity(bestLen)) break;   // the nearest ring that works
            }
            if (!float.IsInfinity(bestLen))
            {
                var p = r.PathTo(best, approachTmp);
                if (p != null && p.Status != PathStatus.NoPath && !p.IsEmpty && p.Points.Count >= 2)
                {
                    approachPoints.AddRange(p.Points);
                    length = p.Length;
                    return true;
                }
            }
            why = blocked ?? (any
                ? "Target is too close (needs " + (min / MathUtil.Yd(1f)).ToString("0") + " yd; no room to step back that far this turn)."
                : "Target is too close, and there is no room to step back.");
            return false;
        }

        static readonly float[] StepBackExtra = { 0.3f, 0.8f, 1.5f };
        static readonly float[] StepBackAngles = { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 115f, -115f };

        /// <summary>
        /// Path to a spot behind the target (outside its frontal arc, within melee reach) reachable this turn, for abilities
        /// that require standing behind it (fills approachPoints). The ability is checked as if the unit stood there.
        /// </summary>
        bool TryApproachBehind(Unit u, AbilityDef a, Unit target, UseCheck chk, bool fromItem, int rank, out float length, out string why)
        {
            approachPoints.Clear();
            length = 0f;
            why = null;
            var grid = Grid;
            if (grid == null) return false;
            var cannot = Battle.CannotMoveReason(u);
            if (cannot != null)
            {
                why = chk.Code == UseFailure.Range ? "Out of range (" + cannot.TrimEnd('.').ToLowerInvariant() + ")." : chk.Reason;
                return false;
            }
            var face = target.Facing.Normalized;
            if (face.SqrLength < 1e-6f) face = Vec2.Right;
            float reachM = Battle.MeleeReachOf(u, target);
            float touch = u.Radius + target.Radius + 0.05f;
            float dist = Mathf.Clamp(reachM - 0.25f, Mathf.Min(touch, reachM - 0.05f), reachM - 0.05f);
            var r = EnsureReach(u);

            // candidates straight behind and up to 70° to either side (the behind arc is ±84° around the back)
            Vec2 best = default, nearest = default;
            float bestLen = float.PositiveInfinity, nearestD = float.PositiveInfinity;
            bool found = false;
            string blocked = null;
            for (int i = 0; i < BehindAngles.Length; i++)
            {
                float ang = BehindAngles[i] * Mathf.Deg2Rad;
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                var dir = new Vec2(-(face.x * cs - face.y * sn), -(face.x * sn + face.y * cs));
                var goal = target.Position + dir * dist;
                float straight = Vec2.Distance(u.Position, goal);
                if (straight < nearestD && grid.IsWalkable(goal, NavAgent.ForUnit(u.Radius, u.Id)))
                {
                    nearestD = straight;
                    nearest = goal;
                }
                if (r == null || !r.CanReach(goal)) continue;
                float walk = r.DistanceTo(goal);
                if (walk >= bestLen) continue;
                var sim = CheckFrom(u, a, target, goal, fromItem, rank);
                if (!sim.Ok) { blocked ??= sim.Reason; continue; }
                bestLen = walk;
                best = goal;
                found = true;
            }
            if (found)
            {
                var p = r.PathTo(best, approachTmp);
                if (p != null && p.Status != PathStatus.NoPath && !p.IsEmpty && p.Points.Count >= 2)
                {
                    approachPoints.AddRange(p.Points);
                    length = p.Length;
                    return true;
                }
            }
            // reachable but something else fails there (Garrote on an immune target…): that reason
            if (blocked != null) { why = blocked; return false; }
            if (float.IsInfinity(nearestD)) { why = "There is no room behind " + target.Name + "."; return false; }
            // tell how far it is (one full path to the nearest free spot behind)
            var full = grid.FindPath(u.Position, nearest, NavAgent.ForUnit(u.Radius, u.Id), float.PositiveInfinity, approachTmp);
            if (full.Status == PathStatus.Complete && full.FullLength > u.MoveLeft)
                why = "Out of reach (needs " + Mathf.Max(0.1f, full.FullLength - u.MoveLeft).ToString("0.0") + " m more movement to get behind " + target.Name + ").";
            else if (full.Status != PathStatus.Complete) why = "Cannot get behind " + target.Name + " from here.";
            return false;
        }

        static readonly float[] BehindAngles = { 0f, 35f, -35f, 70f, -70f };

        /// <summary>The ability's checks as if the unit stood at p (its Position is restored before returning; CanUse has no side effects).</summary>
        UseCheck CheckFrom(Unit u, AbilityDef a, Unit target, Vec2 p, bool fromItem, int rank)
        {
            var old = u.Position;
            try
            {
                u.Position = p;
                return Battle.CanUse(u, a, target, null, fromItem, rank);
            }
            catch (Exception) { return UseCheck.Fail(UseFailure.Unknown, "That cannot be used right now."); }
            finally { u.Position = old; }
        }

        /// <summary>BG3-style click on an enemy: the unit's basic attack (Auto Shot for hunters, a wand out of melee for casters, else melee Attack).</summary>
        SmartPlan PlanSmartAttack(Unit u, Unit target)
        {
            var db = Db;
            var melee = db?.Ability("attack");
            var basic = u.Class != null ? db?.Ability(u.Class.basicAttack) : melee;
            if (basic == null) basic = melee;
            if (basic != null && basic != melee && basic.autoAttack)
            {
                var p = PlanUse(u, basic, target, null, false);
                if (p.Error == null) return MarkAttacking(u, target, p);
                if (lastCheckCode != UseFailure.TooClose && lastCheckCode != UseFailure.Requirement && lastCheckCode != UseFailure.LineOfSight) return p;
            }
            // casters with a wand shoot when out of melee reach, walking into wand range (or sight) first when needed
            var shoot = db?.Ability("shoot");
            if (shoot != null && basic == melee && u.Knows("shoot") && !Battle.InMeleeRange(u, target))
            {
                var sp = PlanUse(u, shoot, target, null, false);
                if (sp.Error == null) return sp;
                // no wand (Requirement) or too close: melee. Otherwise melee only when it works now (e.g. no Time left for
                // the wand but walking into melee and starting the auto attack is free); else the wand's reason
                if (melee == null || lastCheckCode == UseFailure.Requirement || lastCheckCode == UseFailure.TooClose)
                    return melee == null ? sp : MarkAttacking(u, target, PlanUse(u, melee, target, null, false));
                var mp = MarkAttacking(u, target, PlanUse(u, melee, target, null, false));
                if (mp.Error == null) return mp;
                approachPoints.Clear();
                return sp;
            }
            if (melee == null) return new SmartPlan { Error = "You cannot attack." };
            return MarkAttacking(u, target, PlanUse(u, melee, target, null, false));
        }

        static SmartPlan MarkAttacking(Unit u, Unit target, SmartPlan p)
        {
            if (p.Error == null && !p.Approach && p.Ability != null && p.Ability.autoAttack &&
                u.AutoAttacking && u.AttackTarget == target && u.AutoAttackAbility == p.Ability.id)
                p.AlreadyAttacking = true;
            return p;
        }

        // ================================================================ clicks & commands

        void OnLeftClick(Unit u, Unit hover, Vector2 mouse)
        {
            if (IsTargeting) { ConfirmTargeting(u, hover, mouse); return; }
            if (hover != null && hover != u)
            {
                if (hover.IsHostileTo(u))
                {
                    if (hover.IsAlive) ExecutePlan(u, PlanSmartAttack(u, hover), hover, null, null);
                    return;
                }
                if (hover.IsFriendlyTo(u) && hover.Downed)
                {
                    var help = Db?.Ability("help_up");
                    if (help != null) ExecutePlan(u, PlanUse(u, help, hover, null, false), hover, null, null);
                }
                return;
            }
            if (hover == u)
            {
                // clicking yourself waits out a silence/root/lockout from the start of the turn (see SelfPreview)
                float free = FreeIn(u, out _);
                if (free > 0.01f) Wait(free);
                return;
            }
            var why = Battle.CannotMoveReason(u);
            if (why != null) { Fail(why + WaitHint(u)); return; }
            if (!PlanMove(u, ToVec(mouse))) { Fail("Cannot move there."); return; }
            ActionResult r;
            try { r = Battle.MoveAlong(u, planPoints); }
            catch (Exception e) { LogOnce("move", "Move failed: " + e); r = ActionResult.Fail("Cannot move there."); }
            PullEvents(new Intent { Kind = IntentKind.Move, Actor = u });
            if (!r.Ok) Fail(r.Reason);
        }

        void ConfirmTargeting(Unit u, Unit hover, Vector2 mouse)
        {
            var a = TargetingAbility;
            var item = TargetingItem;
            if (a == null) return;
            Unit tgt = null;
            Vec2? point = null;
            if (a.target == TargetType.Point || IsAimed(a)) point = AimPoint(u, a, hover, mouse);
            else
            {
                if (hover == null) { Fail(a.target == TargetType.Enemy ? "Select an enemy." : "Select a target."); return; }
                tgt = hover;
            }
            int rank = item != null ? 0 : TargetingRank;
            Confirm(u, a, item, tgt, point, rank);
        }

        string ExecutePlan(Unit u, SmartPlan plan, Unit target, Vec2? point, ItemInstance item, int rank = 0)
        {
            if (plan.Error != null) return Fail(plan.Error);
            if (plan.AlreadyAttacking || plan.Ability == null) return null;
            if (plan.Approach)
            {
                if (approachPoints.Count < 2) return Fail("Cannot move there.");
                ActionResult m;
                try { m = Battle.MoveAlong(u, approachPoints); }
                catch (Exception e) { LogOnce("approach", "Approach move failed: " + e); m = ActionResult.Fail("Cannot move there."); }
                PullEvents(new Intent { Kind = IntentKind.Move, Actor = u });
                if (!m.Ok) return Fail(m.Reason);
                var chk = Battle.CanUse(u, plan.Ability, target, point, item != null, rank);
                if (!chk.Ok) return Fail(chk.Reason);
            }
            // never toggle a running auto attack off by "attacking" the same target again
            if (plan.Ability.autoAttack && item == null && u.AutoAttacking && u.AttackTarget == target && u.AutoAttackAbility == plan.Ability.id)
            {
                CancelTargeting();
                return null;
            }
            return Execute(u, plan.Ability, item, target, point, rank);
        }

        /// <summary>Uses an ability/item on the battle and queues its events with the command as intent. rank: 0 = highest
        /// known, else that rank (downranking; ignored for items).</summary>
        string Execute(Unit u, AbilityDef a, ItemInstance item, Unit target, Vec2? point, int rank = 0)
        {
            var intent = new Intent { Kind = IntentKind.Ability, Actor = u, Ability = a, Item = item, Target = target, Point = point };
            ActionResult r;
            try { r = item != null ? Battle.UseItem(u, item, target, point) : Battle.UseAbility(u, a.id, target, point, rank); }
            catch (Exception e)
            {
                LogOnce("use:" + a.id, "Using " + a.id + " failed: " + e);
                r = ActionResult.Fail("That cannot be used right now.");
            }
            PullEvents(intent);
            if (!r.Ok) return Fail(r.Reason);
            CancelTargeting();
            hoverDirty = true;
            return null;
        }

        string BeginAbilityInternal(string abilityId, int rank)
        {
            if (disposed || Battle == null || Finished) return Fail("Not in combat.");
            if (!IsPlayerTurn) return Fail(Battle.NeedsPlayerInput ? "Wait for the action to finish." : "It is not your turn.");
            var a = Db?.Ability(abilityId);
            if (a == null) return Fail("Unknown ability.");
            var u = Battle.ActiveUnit;
            // a pinned rank the unit does not know (any more) falls back to the highest known rank
            if (rank > 0 && u != null && rank > AbilityRules.KnownRanks(u, a)) rank = 0;
            return Begin(u, a, null, rank);
        }

        string BeginItemInternal(ItemInstance item)
        {
            if (disposed || Battle == null || Finished) return Fail("Not in combat.");
            if (item == null || item.Def == null) return Fail("No item.");
            if (!IsPlayerTurn) return Fail(Battle.NeedsPlayerInput ? "Wait for the action to finish." : "It is not your turn.");
            var u = Battle.ActiveUnit;
            var a = Db?.Ability(item.Def.use);
            if (a == null) return Fail(item.Name + " cannot be used.");
            var s = flow != null ? flow.Session : null;
            if (s != null)
            {
                string why;
                try { why = s.CannotUseItemReason(u, item); }
                catch (Exception) { why = null; }
                if (why != null) return Fail(why);
            }
            return Begin(u, a, item, 0);
        }

        string Begin(Unit u, AbilityDef a, ItemInstance item, int rank)
        {
            CancelTargeting();
            if (item != null) rank = 0;

            // pressing a queued "next swing" ability again (Heroic Strike, Cleave, Raptor Strike) un-queues it, keeping
            // the rage/mana (WoW). The engine emits no event for this, so bump QueueVersion for the hotbar's dirty check.
            if (item == null && a.nextSwing && u != null && u.QueuedSwing == a.id)
            {
                Battle.CancelQueuedSwing(u);
                QueueVersion++;
                InvalidateTurnCaches();
                FloatingText.Spawn(Head(u, V(u)) + World3D.Up * 0.3f, a.name + " cancelled", MutedText, 0.75f);
                return null;
            }

            var chk = Battle.CanUseIgnoringTarget(u, a, item != null, rank);
            if (!chk.Ok)
            {
                // a rage strike without the rage (every warrior's first turn): instead of a dead end, targeting mode offers
                // to attack the enemy first — the opening swing builds the rage, then the strike follows when it can
                if (item == null && chk.Code == UseFailure.Resource && RageStrike(u, a) && AttackingBuildsRageNow(u))
                {
                    StartTargeting(a, null, rank);
                    return null;
                }
                bool waitable = chk.Code == UseFailure.Silenced || chk.Code == UseFailure.Pacified || chk.Code == UseFailure.Locked;
                return Fail(waitable ? chk.Reason + WaitHint(u) : chk.Reason);
            }

            // no target needed: execute now (cones and lines are aimed with the mouse first)
            bool aimed = IsAimed(a);
            if (a.target == TargetType.Self && !aimed) return Execute(u, a, item, u, null, rank);
            if (a.target == TargetType.Pet) return Execute(u, a, item, u.Pet, null, rank);
            if (a.target == TargetType.Point && a.area.centeredOnCaster && !aimed) return Execute(u, a, item, null, u.Position, rank);

            // toggles on the current target: auto attack (on/off), "next swing" abilities (Heroic Strike…). Only the running
            // auto attack toggles off unchecked; switching (Attack while Auto Shot runs) needs the new one to be usable on
            // the target, otherwise targeting mode offers the approach (walk into melee, then attack)
            var cur = u.AttackTarget;
            bool curOk = cur != null && cur.IsAlive && cur.IsHostileTo(u) && Battle.Units.Contains(cur);
            if (item == null && a.autoAttack && curOk &&
                ((u.AutoAttacking && u.AutoAttackAbility == a.id) || Battle.CanUse(u, a, cur).Ok))
                return Execute(u, a, null, cur, null);
            if (item == null && a.nextSwing && curOk && Battle.CanUse(u, a, cur, null, false, rank).Ok) return Execute(u, a, null, cur, null, rank);

            StartTargeting(a, item, rank);
            return null;
        }

        void StartTargeting(AbilityDef a, ItemInstance item, int rank)
        {
            TargetingAbility = a;
            TargetingItem = item;
            TargetingRank = rank;
            targetingMods = null;
            validStamp = -1;
            rangeRingFor = null;
            hoverDirty = true;
            if (moveRangeShown) { FxSystem.Hide(MoveRangeId); moveRangeShown = false; }
        }

        // ================================================================ rage: attack first

        /// <summary>An enemy-targeted ability paid with rage (Rend, Heroic Strike, Hamstring, Sunder Armor...).</summary>
        static bool RageStrike(Unit u, AbilityDef a) =>
            u != null && a != null && a.target == TargetType.Enemy && a.cost != null && a.cost.type == ResourceType.Rage;

        /// <summary>
        /// Attacking an enemy now would build rage this turn: the unit is not yet swinging at a living enemy in reach (the
        /// click starts the auto attack, walking in first), or its opening swing — the first swing of the battle, which
        /// lands at once (Battle.OpeningSwing) — is still to come.
        /// </summary>
        bool AttackingBuildsRageNow(Unit u)
        {
            if (u == null || u.PowerType != ResourceType.Rage) return false;
            var t = u.AttackTarget;
            bool swinging = u.AutoAttacking && t != null && t.IsAlive && t.IsHostileTo(u) && Battle.Units.Contains(t) && Battle.InMeleeRange(u, t);
            return !swinging || Battle.HasOpeningSwing(u);
        }

        /// <summary>The rage fallback applies to this click: a rage strike refused for rage on a living enemy.</summary>
        bool RageFallback(Unit u, AbilityDef a, ItemInstance item, Unit target) =>
            item == null && target != null && target != u && target.IsAlive && target.IsHostileTo(u) && RageStrike(u, a) &&
            lastCheckCode == UseFailure.Resource && AttackingBuildsRageNow(u);

        /// <summary>
        /// Confirms the targeted ability on a unit (left click in the world, TargetUnit from a frame). A rage strike the unit
        /// cannot pay yet attacks the enemy instead (walking in first): the opening swing builds rage, and the strike is
        /// used right after when it is affordable then; otherwise the reason says the swings build more at the end of the turn.
        /// </summary>
        string Confirm(Unit u, AbilityDef a, ItemInstance item, Unit tgt, Vec2? point, int rank)
        {
            var plan = PlanUse(u, a, tgt, point, item != null, rank, true);
            if (plan.Error == null || !RageFallback(u, a, item, tgt)) return ExecutePlan(u, plan, tgt, point, item, rank);

            var attack = PlanSmartAttack(u, tgt);
            if (attack.Error != null) return Fail(a.name + ": " + plan.Error + " " + tgt.Name + ": " + attack.Error);
            if (!attack.AlreadyAttacking)
            {
                var why = ExecutePlan(u, attack, tgt, null, null);
                if (why != null) return why;
            }
            CancelTargeting();
            if (Battle.IsOver || Battle.ActiveUnit != u || !tgt.IsAlive) return null;
            var again = PlanUse(u, a, tgt, null, false, rank, true);
            if (again.Error == null) return ExecutePlan(u, again, tgt, null, null, rank);
            return Fail(a.name + ": " + again.Error);
        }

        /// <summary>Hover text for a rage strike refused for rage: what the click does instead (attack, walking in first).</summary>
        string RageFallbackPreview(Unit u, AbilityDef a, Unit tgt, string label, string reason)
        {
            var attack = PlanSmartAttack(u, tgt);
            Want(tgt, attack.Error == null ? HostileColor : InvalidColor);
            ShowApproach(attack);
            if (attack.Error != null) return label + " → " + tgt.Name + ": " + reason;
            sb.Length = 0;
            sb.Append(label).Append(": ").Append(reason.TrimEnd('.').Split('.')[0]).Append(" — click to ");
            if (attack.Approach) sb.Append("move ").Append(attack.ApproachLength.ToString("0.0")).Append(" m and ");
            sb.Append("attack ").Append(tgt.Name);
            if (!Battle.HasOpeningSwing(u)) sb.Append(": your swings build rage at the end of the turn");
            else if (!string.IsNullOrEmpty(u.QueuedSwing)) sb.Append(": your queued ").Append(Db?.Ability(u.QueuedSwing)?.name ?? "strike").Append(" lands at once");
            else sb.Append(": your first swing lands at once and builds rage, then ").Append(a.name).Append(" follows if it can");
            return sb.ToString();
        }

        /// <summary>Cones and lines from the caster (Cone of Cold, breath attacks) are aimed with the mouse.</summary>
        static bool IsAimed(AbilityDef a) =>
            a != null && (a.area.shape == AreaShape.Cone || a.area.shape == AreaShape.Line) &&
            (a.target == TargetType.Self || (a.target == TargetType.Point && a.area.centeredOnCaster));

        /// <summary>Ground point for point abilities (snaps to a hovered unit); for aimed cones/lines a point 1 m along the aim.</summary>
        static Vec2 AimPoint(Unit u, AbilityDef a, Unit hover, Vector2 mouse)
        {
            if (IsAimed(a))
            {
                var d = (hover != null ? hover.Position : ToVec(mouse)) - u.Position;
                if (d.SqrLength < 1e-4f) d = u.Facing;
                return u.Position + d.Normalized * 1f;
            }
            return hover != null ? hover.Position : ToVec(mouse);
        }

        // ================================================================ highlights

        void Want(Unit u, Color c)
        {
            var v = V(u);
            if (v != null) wantHl[v] = c;
        }

        void CommitHighlights()
        {
            hlTmp.Clear();
            foreach (var kv in haveHl)
                if (kv.Key == null || !wantHl.ContainsKey(kv.Key)) hlTmp.Add(kv.Key);
            for (int i = 0; i < hlTmp.Count; i++)
            {
                var v = hlTmp[i];
                if (v != null) v.SetTargetable(null);
                haveHl.Remove(v);
            }
            foreach (var kv in wantHl)
            {
                if (kv.Key == null) continue;
                if (haveHl.TryGetValue(kv.Key, out var c) && c == kv.Value) continue;
                kv.Key.SetTargetable(kv.Value);
            }
            haveHl.Clear();
            foreach (var kv in wantHl) if (kv.Key != null) haveHl[kv.Key] = kv.Value;
        }

        void ClearHighlights()
        {
            wantHl.Clear();
            CommitHighlights();
        }

        // ================================================================ hover text pieces

        string Line(Unit u, AbilityDef a, Unit target, AbilityModSet mods, Vec2? point, SmartPlan plan,
                    int enemies = -1, int allies = -1, string label = null, int rank = 0)
        {
            if (a == null) return "";
            if (mods == null)
            {
                try { mods = AbilityMods.For(u, a); } catch (Exception) { mods = AbilityModSet.Empty; }
            }
            int used = UsedRank(u, a, rank);
            sb.Length = 0;
            if (plan.Approach) sb.Append("Move ").Append(plan.ApproachLength.ToString("0.0")).Append(plan.Behind ? " m behind, then " : " m, then ");
            sb.Append(label ?? a.name);
            // finishers: the points this cast would spend (Battle.ComboPointsOn — they stay with the rogue across targets)
            int cp = -1;
            if (a.cost != null && a.cost.consumesComboPoints)
            {
                try { cp = Battle != null ? Battle.ComboPointsOn(u, target) : u.ComboPoints; } catch (Exception) { cp = u.ComboPoints; }
                if (cp > 0) sb.Append(" (").Append(cp).Append(cp == 1 ? " combo point)" : " combo points)");
            }
            if (target != null && target != u)
            {
                sb.Append(" → ").Append(target.Name);
                var p = target.Pending;
                if (p != null && p.Ability != null && target.IsHostileTo(u))
                {
                    bool locked = Battle.IsUninterruptible(p.Ability);
                    sb.Append(" (casting ").Append(p.Ability.name).Append(locked ? ", uninterruptible, " : ", ").Append(p.RemainingTime.ToString("0.#")).Append(" s left)");
                    // the interrupt would only spend its cooldown: say so before the click (CombatLog: "… is uninterruptible")
                    if (locked && a.effects != null && a.effects.Exists(e => e != null && e.type == EffectType.Interrupt)) sb.Append(" · the interrupt has no effect");
                }
            }
            var mag = MagnitudeText(u, a, mods, used, cp);
            if (!string.IsNullOrEmpty(mag)) sb.Append(" · ").Append(mag);
            // a finisher spent early is weak (WoW: build to 3-5); say so before the click without blocking it (plain text:
            // TurnHud draws the preview with a shadow pass, which colour tags would tint)
            if ((cp == 1 || cp == 2) && Tooltip.IsComboDamageFinisher(a))
                sb.Append(" · weak at ").Append(cp).Append(cp == 1 ? " point" : " points").Append(", 3–5 hit much harder");
            if (enemies >= 0)
            {
                sb.Append(" · ").Append(enemies).Append(enemies == 1 ? " enemy" : " enemies");
                if (allies > 0) sb.Append(", ").Append(allies).Append(allies == 1 ? " ally" : " allies");
            }
            if (target != null && target != u && target.IsHostileTo(u)) AppendHitChance(u, a, target);
            float cost = 0f;
            try { cost = AbilityRules.ResourceCost(u, a, used, mods); } catch (Exception) { }
            if (cost > 0f && a.cost != null && a.cost.type != ResourceType.None)
                sb.Append(" · ").Append(cost.ToString("0")).Append(' ').Append(a.cost.type.ToString());
            sb.Append(" · ").Append(TimeText(u, a, mods, rank));
            return sb.ToString();
        }

        /// <summary>The rank the engine will use (0 = the highest known; basic/contextual abilities stay 0).</summary>
        static int UsedRank(Unit u, AbilityDef a, int rank)
        {
            try { return AbilityRules.UsedRank(u, a, rank); }
            catch (Exception) { return rank; }
        }

        /// <summary>"Fireball (Rank 3)" while a lower rank than the highest known is used, else the plain name.</summary>
        static string RankedName(Unit u, AbilityDef a, int rank)
        {
            if (a == null) return "";
            if (rank <= 0 || u == null) return a.name;
            int known;
            try { known = AbilityRules.KnownRanks(u, a); } catch (Exception) { known = 0; }
            return rank < known ? a.name + " (Rank " + rank + ")" : a.name;
        }

        /// <summary>"77–92 damage": the first damage/heal effect at the rank; a finisher's at <paramref name="comboPoints"/>
        /// (the points it would spend; &lt; 1 shows the 1-point value and the per-point step).</summary>
        string MagnitudeText(Unit u, AbilityDef a, AbilityModSet mods, int rank, int comboPoints = -1)
        {
            try
            {
                int eff = AbilityRules.EffLevel(u, a, rank);
                foreach (var e in a.effects)
                {
                    if (e == null) continue;
                    if (e.type != EffectType.Damage && e.type != EffectType.WeaponDamage && e.type != EffectType.Heal) continue;
                    var m = Tooltip.Magnitude(u, a, e, eff, mods, rank, comboPoints);
                    if (string.IsNullOrEmpty(m)) continue;
                    m = m.Replace(" to ", "–");
                    if (e.type == EffectType.Heal) return m + " healing";
                    var school = e.school ?? a.school;
                    return school == School.Physical ? m + " damage" : m + " " + school;
                }
            }
            catch (Exception) { }
            return "";
        }

        /// <summary>
        /// " · 92% hit · 18% crit" from the engine's own attack table (Battle.HitChance — the very numbers the rolls use;
        /// basic attacks are previewed as white swings), " · immune" against an invulnerable target, nothing for
        /// abilities that cannot miss.
        /// </summary>
        void AppendHitChance(Unit u, AbilityDef a, Unit target)
        {
            HitChanceInfo h;
            try { h = Battle.HitChance(u, a, target); }
            catch (Exception) { return; }
            if (h.Immune) { sb.Append(" · immune"); return; }
            if (!h.Rolls && !h.SingleRoll) return;
            sb.Append(" · ").Append(Mathf.Clamp(h.Hit, 0f, 100f).ToString("0")).Append("% hit");
            if (h.CanCrit && h.Crit >= 0.5f) sb.Append(" · ").Append(h.Crit.ToString("0")).Append("% crit");
        }

        string TimeText(Unit u, AbilityDef a, AbilityModSet mods, int rank = 0)
        {
            // the opening swing (Battle.OpeningSwing): the first melee swing of the battle lands as the unit engages
            bool opening = Battle.HasOpeningSwing(u) && !AbilityRules.IsRangedWeaponAbility(a);
            if (a.autoAttack) return opening ? "first swing at once, then auto attacks at the end of your turn" : "auto attack at the end of your turn";
            if (a.nextSwing) return opening ? "replaces your first swing (lands at once in reach)" : "replaces your next swing";
            float tc, ct;
            int used = UsedRank(u, a, rank);
            try
            {
                tc = AbilityRules.TimeCost(u, a, mods, used);
                ct = AbilityRules.CastTime(u, a, mods, used);
            }
            catch (Exception) { return ""; }
            float left = Mathf.Max(0f, u.TimeLeft);
            if (ct > 0f && a.channeled)
                return ct > left + 1e-3f ? ct.ToString("0.#") + " s channel (continues next turn)" : ct.ToString("0.#") + " s channel";
            if (ct > 0f && Battle.InCombat && Battle.IsTelegraphed(a))
                return ct.ToString("0.#") + " s cast (telegraphed: resolves next turn, can be interrupted)";
            if (ct > 0f)
                return ct > left + 1e-3f ? ct.ToString("0.#") + " s cast (pending: resolves next turn, can be interrupted)" : ct.ToString("0.#") + " s cast";
            if (tc > 0f)
                return tc > left + 1e-3f ? tc.ToString("0.#") + " s (+" + (tc - left).ToString("0.#") + " s time debt)" : tc.ToString("0.#") + " s";
            return "free action";
        }
    }
}
