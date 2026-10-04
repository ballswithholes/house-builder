// Movement queries used by the battle and the AI. NavGridPathfinder adapts Lanternvale.World.NavGrid;
// StraightLinePathfinder is a trivial open-field implementation for tests and arenas.
using System;
using System.Collections.Generic;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed class PathResult
    {
        public readonly List<Vec2> Points = new List<Vec2>();
        /// <summary>Path length in metres (after truncation).</summary>
        public float Length;
        /// <summary>Length before truncation to a budget.</summary>
        public float FullLength;
        public bool Found;
        public bool Truncated;
        public Vec2 End => Points.Count > 0 ? Points[Points.Count - 1] : default;
        public bool ReachedGoal => Found && !Truncated;
    }

    public interface IPathfinder
    {
        /// <summary>Path from <paramref name="from"/> to <paramref name="to"/> for a unit of the given radius,
        /// avoiding other units (except <paramref name="selfId"/> and <paramref name="ignoreId"/>), truncated to <paramref name="maxLength"/>.</summary>
        PathResult FindPath(Vec2 from, Vec2 to, float radius, int selfId, int ignoreId, float maxLength);
        /// <summary>Path that stops as soon as the unit is within <paramref name="range"/> metres of <paramref name="target"/>.</summary>
        PathResult FindPathToRange(Vec2 from, Vec2 target, float range, float radius, int selfId, int ignoreId, float maxLength);
        bool IsWalkable(Vec2 p, float radius, int selfId);
        bool HasLineOfSight(Vec2 a, Vec2 b);
        /// <summary>Nearest walkable point to <paramref name="p"/>.</summary>
        Vec2 ClampToWalkable(Vec2 p, float radius, int selfId);
        /// <summary>Updates the dynamic occupancy of a unit (combat); radius ≤ 0 removes it.</summary>
        void SetUnit(int id, Vec2 pos, float radius);
        void ClearUnits();
    }

    /// <summary>Open field (optionally bounded) with circular unit obstacles: paths are straight lines.</summary>
    public sealed class StraightLinePathfinder : IPathfinder
    {
        readonly Dictionary<int, KeyValuePair<Vec2, float>> units = new Dictionary<int, KeyValuePair<Vec2, float>>();
        public float MinX = -1000, MinY = -1000, MaxX = 1000, MaxY = 1000;
        /// <summary>When true, other units block the destination point (not the straight line).</summary>
        public bool UnitsBlock = true;

        public StraightLinePathfinder() { }
        public StraightLinePathfinder(float width, float depth) { MinX = 0; MinY = 0; MaxX = width; MaxY = depth; }

        Vec2 Clamp(Vec2 p) => new Vec2(MathUtil.Clamp(p.x, MinX, MaxX), MathUtil.Clamp(p.y, MinY, MaxY));

        public PathResult FindPath(Vec2 from, Vec2 to, float radius, int selfId, int ignoreId, float maxLength)
        {
            var r = new PathResult { Found = true };
            to = Clamp(to);
            if (UnitsBlock && !IsFree(to, radius, selfId, ignoreId)) to = NearestFree(from, to, radius, selfId, ignoreId);
            float len = Vec2.Distance(from, to);
            r.FullLength = len;
            r.Points.Add(from);
            if (len > maxLength + 1e-4f)
            {
                var end = Vec2.MoveTowards(from, to, maxLength);
                if (UnitsBlock && !IsFree(end, radius, selfId, ignoreId)) end = BackOff(from, end, radius, selfId, ignoreId);
                r.Points.Add(end);
                r.Length = Vec2.Distance(from, end);
                r.Truncated = true;
            }
            else
            {
                if (len > 1e-4f) r.Points.Add(to);
                r.Length = len;
            }
            return r;
        }

        public PathResult FindPathToRange(Vec2 from, Vec2 target, float range, float radius, int selfId, int ignoreId, float maxLength)
        {
            float d = Vec2.Distance(from, target);
            if (d <= range)
            {
                var r0 = new PathResult { Found = true };
                r0.Points.Add(from);
                return r0;
            }
            var goal = Vec2.MoveTowards(from, target, d - Math.Max(0f, range - 0.02f));
            if (UnitsBlock && !IsFree(goal, radius, selfId, ignoreId))
            {
                // try points around the target on the range circle
                var best = goal; float bestD = float.MaxValue; bool found = false;
                var dir = (from - target).Normalized;
                for (int i = 0; i < 16; i++)
                {
                    double ang = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * Math.PI / 8.0;
                    var rd = new Vec2((float)(dir.x * Math.Cos(ang) - dir.y * Math.Sin(ang)), (float)(dir.x * Math.Sin(ang) + dir.y * Math.Cos(ang)));
                    var p = Clamp(target + rd * Math.Max(0.1f, range - 0.05f));
                    if (!IsFree(p, radius, selfId, ignoreId)) continue;
                    float pd = Vec2.Distance(from, p);
                    if (pd < bestD) { bestD = pd; best = p; found = true; }
                }
                if (found) goal = best;
            }
            return FindPath(from, goal, radius, selfId, ignoreId, maxLength);
        }

        bool IsFree(Vec2 p, float radius, int selfId, int ignoreId)
        {
            foreach (var kv in units)
            {
                if (kv.Key == selfId || kv.Key == ignoreId) continue;
                if (Vec2.Distance(p, kv.Value.Key) < radius + kv.Value.Value - 0.05f) return false;
            }
            return true;
        }

        Vec2 NearestFree(Vec2 from, Vec2 to, float radius, int selfId, int ignoreId)
        {
            // walk back along the line towards the start until free
            float len = Vec2.Distance(from, to);
            for (float t = 0.1f; t <= len; t += 0.1f)
            {
                var p = Vec2.MoveTowards(to, from, t);
                if (IsFree(p, radius, selfId, ignoreId)) return p;
            }
            return from;
        }

        Vec2 BackOff(Vec2 from, Vec2 end, float radius, int selfId, int ignoreId) => NearestFree(from, end, radius, selfId, ignoreId);

        public bool IsWalkable(Vec2 p, float radius, int selfId)
        {
            if (p.x < MinX || p.x > MaxX || p.y < MinY || p.y > MaxY) return false;
            return !UnitsBlock || IsFree(p, radius, selfId, -1);
        }

        public bool HasLineOfSight(Vec2 a, Vec2 b) => true;
        public Vec2 ClampToWalkable(Vec2 p, float radius, int selfId) => Clamp(p);

        public void SetUnit(int id, Vec2 pos, float radius)
        {
            if (radius <= 0) units.Remove(id);
            else units[id] = new KeyValuePair<Vec2, float>(pos, radius);
        }

        public void ClearUnits() => units.Clear();
    }
}
