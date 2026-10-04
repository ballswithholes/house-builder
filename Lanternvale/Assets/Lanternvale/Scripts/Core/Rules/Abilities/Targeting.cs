// Area shapes (circle, cone, line), area target selection and chain jumps.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    /// <summary>Geometry of an ability's area for UI previews (metres, degrees).</summary>
    public struct AreaShapeInfo
    {
        public AreaShape Shape;
        public Vec2 Center;      // circle centre, cone apex or line start
        public Vec2 Direction;   // cone/line direction (unit vector)
        public float Radius;     // circle radius, cone radius, line length
        public float Angle;      // cone angle
        public float Width;      // line width
        public bool Contains(Vec2 p, float pad = 0f)
        {
            switch (Shape)
            {
                case AreaShape.Circle: return Vec2.Distance(Center, p) <= Radius + pad;
                case AreaShape.Cone:
                {
                    var d = p - Center;
                    float dist = d.Length;
                    if (dist > Radius + pad) return false;
                    if (dist < 0.3f + pad) return true;
                    return Vec2.Angle(Direction, d) <= Angle * 0.5f + (pad > 0 ? (float)(Math.Atan2(pad, dist) * 180 / Math.PI) : 0f);
                }
                case AreaShape.Line:
                {
                    var d = p - Center;
                    float along = Vec2.Dot(d, Direction);
                    if (along < -pad || along > Radius + pad) return false;
                    var perp = d - Direction * along;
                    return perp.Length <= Width * 0.5f + pad;
                }
                default: return false;
            }
        }
    }

    public static class Targeting
    {
        /// <summary>Area geometry for an ability used on a target/point.</summary>
        public static AreaShapeInfo AreaOf(Unit caster, AbilityDef a, Unit target, Vec2? point, AbilityModSet mods)
        {
            var info = new AreaShapeInfo { Shape = a.area.shape, Angle = a.area.angle, Width = MathUtil.Yd(a.area.width) };
            info.Radius = AbilityRules.RadiusMetres(a, mods ?? AbilityModSet.Empty);
            Vec2 aim = point ?? (target != null ? target.Position : caster.Position + caster.Facing);
            if (a.area.shape == AreaShape.Circle)
            {
                info.Center = a.area.centeredOnCaster || a.target == TargetType.Self ? caster.Position : aim;
                info.Direction = caster.Facing;
            }
            else
            {
                info.Center = caster.Position;
                var dir = aim - caster.Position;
                info.Direction = dir.SqrLength > 1e-6f && !(a.target == TargetType.Self && point == null && target == caster) ? dir.Normalized : caster.Facing.Normalized;
                if (info.Direction.SqrLength < 1e-6f) info.Direction = Vec2.Right;
            }
            return info;
        }

        static bool Affects(AreaAffects affects, Unit caster, Unit u)
        {
            switch (affects)
            {
                case AreaAffects.Enemies: return u.IsHostileTo(caster);
                case AreaAffects.Allies: return u.IsFriendlyTo(caster);
                default: return true;
            }
        }

        /// <summary>Units inside the ability's area (closest to the centre first, maxTargets applied).</summary>
        public static List<Unit> AreaUnits(Battle b, Unit caster, AbilityDef a, Unit target, Vec2? point, AbilityModSet mods)
        {
            var shape = AreaOf(caster, a, target, point, mods);
            var list = new List<Unit>();
            foreach (var u in b.Units)
            {
                if (!u.IsAlive || u.IsUntargetable) continue;
                if (!Affects(a.area.affects, caster, u)) continue;
                if (u.IsHostileTo(caster) && u.HasStateAura(UnitState.Invisible)) continue;
                if (u == caster && a.area.affects == AreaAffects.Enemies) continue;
                if (u.IsHostileTo(caster) && a.requires != null && a.requires.targetCreatureTypes.Length > 0 &&
                    Array.IndexOf(a.requires.targetCreatureTypes, u.Creature != null && u.Class == null ? u.Creature.type : CreatureType.Humanoid) < 0) continue;
                if (!shape.Contains(u.Position, u.Radius)) continue;
                list.Add(u);
            }
            var c = shape.Center;
            list.Sort((x, y) =>
            {
                // the primary target always first
                if (x == target && y != target) return -1;
                if (y == target && x != target) return 1;
                return Vec2.Distance(x.Position, c).CompareTo(Vec2.Distance(y.Position, c));
            });
            if (a.area.maxTargets > 0 && list.Count > a.area.maxTargets) list.RemoveRange(a.area.maxTargets, list.Count - a.area.maxTargets);
            return list;
        }

        /// <summary>
        /// Chain targets after <paramref name="first"/>: up to <paramref name="jumps"/> extra units of the same side
        /// within chainRange yards of the previous one. Friendly chains prefer the most injured, hostile the nearest.
        /// </summary>
        public static List<Unit> Chain(Battle b, Unit caster, Unit first, int jumps, float chainRangeYards)
        {
            var list = new List<Unit> { first };
            if (first == null || jumps <= 0) return list;
            bool friendly = first.IsFriendlyTo(caster);
            float range = MathUtil.Yd(chainRangeYards > 0 ? chainRangeYards : 12f);
            var cur = first;
            for (int j = 0; j < jumps; j++)
            {
                Unit best = null;
                float bestScore = float.MaxValue;
                foreach (var u in b.Units)
                {
                    if (!u.IsAlive || list.Contains(u) || u.IsUntargetable || u.IsTotem) continue;
                    if (u.IsFriendlyTo(caster) != friendly) continue;
                    if (!friendly && !b.CanSee(caster, u)) continue;
                    float d = Vec2.Distance(cur.Position, u.Position);
                    if (d > range + u.Radius) continue;
                    float score = friendly ? u.HealthPct * 1000f + d : d;
                    if (friendly && u.Health >= u.MaxHealth) score += 100000f;
                    if (score < bestScore) { bestScore = score; best = u; }
                }
                if (best == null) break;
                list.Add(best);
                cur = best;
            }
            return list;
        }

        /// <summary>Units within <paramref name="radiusMetres"/> of <paramref name="center"/> on the given side of <paramref name="caster"/>.</summary>
        public static List<Unit> InRadius(Battle b, Unit caster, Vec2 center, float radiusMetres, bool allies, bool includeTotems = false)
        {
            var list = new List<Unit>();
            foreach (var u in b.Units)
            {
                if (!u.IsAlive) continue;
                if (u.IsTotem && !includeTotems) continue;
                if (allies ? !u.IsFriendlyTo(caster) : !u.IsHostileTo(caster)) continue;
                if (!allies && (u.IsUntargetable || u.HasStateAura(UnitState.Invisible))) continue;
                if (Vec2.Distance(center, u.Position) <= radiusMetres + u.Radius) list.Add(u);
            }
            return list;
        }

        /// <summary>A free standing spot near <paramref name="center"/> (summons, totems, party placement).</summary>
        public static Vec2 FreeSpotNear(Battle b, Vec2 center, float radius, int selfId, Vec2 preferDir)
        {
            var pf = b.Pathfinder;
            if (pf.IsWalkable(center, radius, selfId) && !Occupied(b, center, radius, selfId)) return center;
            var dir = preferDir.SqrLength > 1e-6f ? preferDir.Normalized : Vec2.Right;
            for (int ring = 1; ring <= 6; ring++)
            {
                float dist = ring * 0.8f;
                for (int i = 0; i < 12; i++)
                {
                    double ang = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * Math.PI / 6.0;
                    var d = new Vec2((float)(dir.x * Math.Cos(ang) - dir.y * Math.Sin(ang)), (float)(dir.x * Math.Sin(ang) + dir.y * Math.Cos(ang)));
                    var p = center + d * dist;
                    if (pf.IsWalkable(p, radius, selfId) && !Occupied(b, p, radius, selfId)) return p;
                }
            }
            return pf.ClampToWalkable(center, radius, selfId);
        }

        static bool Occupied(Battle b, Vec2 p, float radius, int selfId)
        {
            foreach (var u in b.Units)
                if (u.Id != selfId && u.IsAlive && Vec2.Distance(u.Position, p) < radius + u.Radius - 0.05f) return true;
            return false;
        }

        /// <summary>Walks from <paramref name="from"/> along <paramref name="dir"/> up to <paramref name="dist"/> metres while walkable.</summary>
        public static Vec2 WalkLine(Battle b, Unit u, Vec2 from, Vec2 dir, float dist)
        {
            var d = dir.Normalized;
            if (d.SqrLength < 1e-6f) return from;
            var last = from;
            const float step = 0.25f;
            for (float t = step; t <= dist + 1e-4f; t += step)
            {
                var p = from + d * Math.Min(t, dist);
                if (!b.Pathfinder.IsWalkable(p, u.Radius, u.Id)) break;
                last = p;
            }
            return last;
        }
    }
}
