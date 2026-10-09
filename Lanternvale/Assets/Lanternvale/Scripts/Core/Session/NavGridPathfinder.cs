// Adapts the world module's NavGrid (A*, unit occupancy, line of sight) to the rules engine's IPathfinder.
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    public sealed class NavGridPathfinder : IPathfinder
    {
        public readonly NavGrid Grid;
        readonly NavPath tmp = new NavPath();

        /// <summary>When true, a partial path (blocked/unreachable goal) still counts as Found (walks to the closest point).</summary>
        public bool AcceptPartial;
        /// <summary>When false (exploration context), units are never registered on the grid and never block paths.</summary>
        public bool TrackUnits = true;

        public NavGridPathfinder(NavGrid grid) { Grid = grid; }

        NavAgent Agent(float radius, int selfId, int ignoreId)
        {
            var a = NavAgent.ForUnit(radius, selfId, ignoreId);
            if (!TrackUnits) a.IgnoreUnits = true;
            return a;
        }

        PathResult Convert(NavPath p)
        {
            // Found = Complete keeps ReachedGoal strict (range checks); Partial leads to the closest reachable point.
            var r = new PathResult
            {
                Length = p.Length, FullLength = p.FullLength, Truncated = p.Truncated,
                Found = p.Status == PathStatus.Complete || (AcceptPartial && p.Status == PathStatus.Partial && p.Count > 1),
            };
            r.Points.AddRange(p.Points);
            return r;
        }

        public PathResult FindPath(Vec2 from, Vec2 to, float radius, int selfId, int ignoreId, float maxLength) =>
            Convert(Grid.FindPath(from, to, Agent(radius, selfId, ignoreId), maxLength, tmp));

        public PathResult FindPathToRange(Vec2 from, Vec2 target, float range, float radius, int selfId, int ignoreId, float maxLength) =>
            Convert(Grid.FindPathToRange(from, target, range, Agent(radius, selfId, ignoreId), maxLength, false, tmp));

        public bool IsWalkable(Vec2 p, float radius, int selfId) => Grid.IsWalkable(p, Agent(radius, selfId, NavAgent.NoId));
        public bool HasLineOfSight(Vec2 a, Vec2 b) => Grid.HasLineOfSight(a, b);
        public Vec2 ClampToWalkable(Vec2 p, float radius, int selfId) => Grid.ClampToWalkable(p, Agent(radius, selfId, NavAgent.NoId));

        public void SetUnit(int id, Vec2 pos, float radius)
        {
            if (TrackUnits) Grid.SetUnit(id, pos, radius);
        }

        public void ClearUnits()
        {
            if (TrackUnits) Grid.ClearUnits();
        }
    }
}
