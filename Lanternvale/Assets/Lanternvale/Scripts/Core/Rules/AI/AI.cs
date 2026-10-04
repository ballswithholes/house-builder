// Step-wise AI for enemies (AIProfile + CreatureAbilityDef priorities/conditions + threat), pets and
// auto-played party members (role-aware scoring with aiHint/aiPriority). Each call returns ONE step so the
// presentation layer can animate between steps: Move(path), UseAbility(id, target/point) or EndTurn.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum AIStepKind { Move, UseAbility, UseItem, EndTurn }

    public sealed class AIStep
    {
        public AIStepKind Kind;
        public Unit Unit;
        public List<Vec2> Path;
        public string AbilityId = "";
        /// <summary>Rank to use (0 = the highest known; companion healers may downrank when mana runs low).</summary>
        public int Rank;
        public Unit Target;
        public Vec2? Point;
        public ItemInstance Item;
        /// <summary>Why the AI chose this (debug / combat log tooltip).</summary>
        public string Reason = "";

        public static AIStep End(Unit u, string why) => new AIStep { Kind = AIStepKind.EndTurn, Unit = u, Reason = why };
        public override string ToString() => Kind == AIStepKind.UseAbility ? $"{Unit?.Name}: {AbilityId} -> {Target?.Name ?? (Point.HasValue ? Point.Value.ToString() : "")} ({Reason})"
            : Kind == AIStepKind.Move ? $"{Unit?.Name}: move {Path?.Count} pts ({Reason})" : $"{Unit?.Name}: {Kind} ({Reason})";
    }

    public static partial class AI
    {
        public const int MaxStepsPerTurn = 24;

        /// <summary>The next step for the active unit (enemy, pet or auto-played party member).</summary>
        public static AIStep NextStep(Battle b, Unit u)
        {
            if (b == null || u == null || b.IsOver || b.ActiveUnit != u) return AIStep.End(u, "not active");
            var mem = u.AIMemory;
            if (++mem.Steps > MaxStepsPerTurn) return AIStep.End(u, "step limit");
            if (!u.IsAlive || u.Pending != null || u.IsControlled) return AIStep.End(u, "cannot act");
            try
            {
                if (u.Team != b.PlayerTeam || (u.Class == null && u.Owner == null)) return EnemyStep(b, u);
                if (u.Class == null) return PetStep(b, u);
                return CompanionStep(b, u);
            }
            catch (Exception ex)
            {
                Log.Warn($"AI error for {u.Name}: {ex}");
                return AIStep.End(u, "error");
            }
        }

        /// <summary>Executes a step on the battle. Failed ability steps are remembered so the AI tries something else.</summary>
        public static ActionResult Execute(Battle b, AIStep step)
        {
            if (step == null || step.Unit == null) return ActionResult.Fail("No step.");
            var u = step.Unit;
            ActionResult r;
            switch (step.Kind)
            {
                case AIStepKind.Move:
                    r = b.MoveAlong(u, step.Path);
                    if (!r.Ok) u.AIMemory.Moves += 10; // stop trying to move this turn
                    else u.AIMemory.Moves++;
                    return r;
                case AIStepKind.UseAbility:
                    r = b.UseAbility(u, step.AbilityId, step.Target, step.Point, step.Rank);
                    if (!r.Ok) u.AIMemory.Failed.Add(step.AbilityId + "@" + (step.Target?.Id ?? 0));
                    else u.AIMemory.Used(step.AbilityId);
                    return r;
                case AIStepKind.UseItem:
                    r = b.UseItem(u, step.Item, step.Target, step.Point);
                    if (!r.Ok) u.AIMemory.Failed.Add("item:" + step.Item?.Id);
                    return r;
                default:
                    return b.InCombat && b.ActiveUnit == u ? b.EndTurn(u) : ActionResult.Success;
            }
        }

        /// <summary>Runs the active AI-controlled unit's whole turn (tests, fast-forward). Returns steps taken.</summary>
        public static int RunTurn(Battle b, Unit u, List<AIStep> log = null)
        {
            int n = 0;
            while (!b.IsOver && b.ActiveUnit == u && n < MaxStepsPerTurn + 5)
            {
                var s = NextStep(b, u);
                log?.Add(s);
                n++;
                var r = Execute(b, s);
                if (s.Kind == AIStepKind.EndTurn) break;
                if (!r.Ok && s.Kind == AIStepKind.Move && u.AIMemory.Moves > 20 && b.ActiveUnit == u) { b.EndTurn(u); break; }
            }
            if (!b.IsOver && b.ActiveUnit == u) b.EndTurn(u);
            return n;
        }

        // ===================================================== shared helpers

        internal static List<Unit> VisibleEnemies(Battle b, Unit u)
        {
            var list = new List<Unit>();
            foreach (var o in b.Units)
                if (o.IsAlive && o.IsHostileTo(u) && !o.IsUntargetable && b.CanSee(u, o) && !o.IsFeigningDeathFor(u)) list.Add(o);
            return list;
        }

        internal static AIStep MoveToRange(Battle b, Unit u, Unit target, float range, string why, bool behind = false)
        {
            if (b.CannotMoveReason(u) != null || u.AIMemory.Moves >= 3) return null;
            Vec2 goal = target.Position;
            PathResult p;
            if (behind)
            {
                var back = target.Position - target.Facing.Normalized * Math.Max(0.6f, range * 0.8f);
                p = b.Pathfinder.FindPath(u.Position, back, u.Radius, u.Id, target.Id, u.MoveLeft);
                if (!p.Found || !p.ReachedGoal) p = b.Pathfinder.FindPathToRange(u.Position, goal, Math.Max(0.1f, range - 0.15f), u.Radius, u.Id, target.Id, u.MoveLeft);
            }
            else p = b.Pathfinder.FindPathToRange(u.Position, goal, Math.Max(0.1f, range - 0.15f), u.Radius, u.Id, target.Id, u.MoveLeft);
            if (!p.Found || p.Points.Count < 2 || p.Length < 0.1f) return null;
            return new AIStep { Kind = AIStepKind.Move, Unit = u, Path = new List<Vec2>(p.Points), Target = target, Reason = why };
        }

        internal static AIStep MoveAway(Battle b, Unit u, Unit from, float wantDistance, Unit stayInRangeOf, float maxRange, string why)
        {
            if (b.CannotMoveReason(u) != null || u.AIMemory.Moves >= 2) return null;
            var dir = (u.Position - from.Position);
            if (dir.SqrLength < 1e-4f) dir = u.Facing * -1f;
            dir = dir.Normalized;
            Vec2 best = u.Position; float bestScore = float.MinValue;
            for (int i = -3; i <= 3; i++)
            {
                double ang = i * Math.PI / 8;
                var d = new Vec2((float)(dir.x * Math.Cos(ang) - dir.y * Math.Sin(ang)), (float)(dir.x * Math.Sin(ang) + dir.y * Math.Cos(ang)));
                float need = Math.Max(0.5f, wantDistance - u.DistanceTo(from) + 0.3f);
                var p = from.Position + d * wantDistance;
                var target = Vec2.MoveTowards(u.Position, p, Math.Min(u.MoveLeft, need + 0.5f));
                if (!b.Pathfinder.IsWalkable(target, u.Radius, u.Id)) continue;
                float score = Vec2.Distance(target, from.Position);
                if (stayInRangeOf != null && Vec2.Distance(target, stayInRangeOf.Position) > maxRange) score -= 100f;
                if (score > bestScore) { bestScore = score; best = target; }
            }
            if (Vec2.Distance(best, u.Position) < 0.3f) return null;
            var path = b.Pathfinder.FindPath(u.Position, best, u.Radius, u.Id, -1, u.MoveLeft);
            if (!path.Found || path.Points.Count < 2 || path.Length < 0.2f) return null;
            return new AIStep { Kind = AIStepKind.Move, Unit = u, Path = new List<Vec2>(path.Points), Reason = why };
        }

        internal static AIStep Use(Unit u, AbilityDef a, Unit target, Vec2? point, string why) =>
            new AIStep { Kind = AIStepKind.UseAbility, Unit = u, AbilityId = a.id, Target = target, Point = point, Reason = why };

        internal static bool Failed(Unit u, AbilityDef a, Unit t) => u.AIMemory.Failed.Contains(a.id + "@" + (t?.Id ?? 0));

        internal static bool IsMovableFailure(UseFailure c) => c == UseFailure.Range || c == UseFailure.LineOfSight;

        /// <summary>The aura ids the ability applies (ApplyAura effects).</summary>
        internal static List<string> AppliedAuras(AbilityDef a)
        {
            var l = new List<string>();
            foreach (var e in a.effects) if (e.type == EffectType.ApplyAura && !string.IsNullOrEmpty(e.aura)) l.Add(e.aura);
            return l;
        }

        // ===================================================== conditions

        /// <summary>
        /// CreatureAbilityDef conditions: "", "selfHpBelow:N", "selfHpAbove:N", "allyHpBelow:N", "targetHpBelow:N",
        /// "targetCasting", "targetNoAura:id", "selfNoAura:id", "enemiesInRange:N", "notOnCooldown", "hasPet", "noPet".
        /// </summary>
        public static bool ConditionMet(Battle b, Unit u, string condition, Unit target)
        {
            if (string.IsNullOrEmpty(condition)) return true;
            foreach (var part in condition.Split('&', ';'))
            {
                var c = part.Trim();
                if (c.Length == 0) continue;
                string key = c, arg = "";
                int colon = c.IndexOf(':');
                if (colon >= 0) { key = c.Substring(0, colon).Trim(); arg = c.Substring(colon + 1).Trim(); }
                float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num);
                bool ok;
                switch (key)
                {
                    case "selfHpBelow": ok = u.HealthPct < num; break;
                    case "selfHpAbove": ok = u.HealthPct > num; break;
                    case "allyHpBelow":
                        ok = false;
                        foreach (var a in b.AlliesOf(u)) if (a.HealthPct < num) { ok = true; break; }
                        break;
                    case "targetHpBelow": ok = target != null && target.HealthPct < num; break;
                    case "targetCasting": ok = target != null && target.Pending != null; break;
                    case "targetNoAura": ok = target == null || !target.HasAura(arg); break;
                    case "selfNoAura": ok = !u.HasAura(arg); break;
                    case "selfAura": ok = u.HasAura(arg); break;
                    case "enemiesInRange":
                    {
                        int n = 0;
                        foreach (var e in b.Units) if (e.IsAlive && e.IsHostileTo(u) && !e.IsTotem && u.DistanceTo(e) <= MathUtil.Yd(10f) + e.Radius) n++;
                        ok = n >= Math.Max(1, (int)num);
                        break;
                    }
                    case "notOnCooldown": ok = true; break;
                    case "hasPet": ok = u.Pet != null && u.Pet.IsAlive; break;
                    case "noPet": ok = u.Pet == null || !u.Pet.IsAlive; break;
                    case "roundAtLeast": ok = b.Round >= num; break;
                    default: ok = true; break;
                }
                if (!ok) return false;
            }
            return true;
        }
    }

    public static class AITurnMemoryExtensions
    {
        public static void Used(this AITurnMemory m, string abilityId)
        {
            m.UsedCounts.TryGetValue(abilityId, out var n);
            m.UsedCounts[abilityId] = n + 1;
        }
        public static int UsedCount(this AITurnMemory m, string abilityId) => m.UsedCounts.TryGetValue(abilityId, out var n) ? n : 0;
    }
}
