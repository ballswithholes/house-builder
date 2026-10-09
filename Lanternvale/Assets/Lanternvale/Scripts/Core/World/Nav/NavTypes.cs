// Navigation value types: options, agent description and path results. Pure C#.
using System;
using System.Collections.Generic;
using Lanternvale.Util;

namespace Lanternvale.World
{
    /// <summary>Build options for <see cref="NavGrid"/>.</summary>
    [Serializable]
    public sealed class NavGridOptions
    {
        /// <summary>Cell size in metres.</summary>
        public float CellSize = 0.5f;
        /// <summary>Cells whose centre is closer than this to the ground rectangle's edge are blocked.</summary>
        public float EdgeMargin = 0.25f;
        /// <summary>Ellipse footprint (full width, depth) blocked around each chest.</summary>
        public Vec2 ChestFootprint = new Vec2(1.0f, 0.6f);
        public bool IncludeChests = true;
        /// <summary>When false, <see cref="NavGrid.HasLineOfSight"/> always returns true.</summary>
        public bool LineOfSight = true;
        /// <summary>Props whose collider (larger axis × scale) is at least this big block line of sight.</summary>
        public float LosBlockerMinSize = 2.5f;
        /// <summary>Safety cap on A* expansions per query.</summary>
        public int MaxSearchNodes = 200000;
    }

    /// <summary>
    /// Who is moving: its radius and which dynamic units it may pass through. Cheap to copy.
    /// The agent's own unit (<see cref="SelfId"/>) is always ignored.
    /// </summary>
    public struct NavAgent
    {
        /// <summary>Value of <see cref="SelfId"/> meaning "no own unit".</summary>
        public const int NoId = int.MinValue;
        public const float DefaultRadius = 0.35f;

        public float Radius;
        public int SelfId;
        /// <summary>One extra unit id to ignore without allocating (e.g. the melee target). NoId = none.</summary>
        public int IgnoreId;
        /// <summary>Extra unit ids to ignore (allies, the target). Any collection (int[], List, HashSet).</summary>
        public ICollection<int> Ignore;
        /// <summary>Ignore every dynamic unit (exploration movement).</summary>
        public bool IgnoreUnits;

        public NavAgent(float radius, int selfId = NoId, ICollection<int> ignore = null, bool ignoreUnits = false)
        {
            Radius = radius < 0 ? 0 : radius;
            SelfId = selfId;
            IgnoreId = NoId;
            Ignore = ignore;
            IgnoreUnits = ignoreUnits;
        }

        /// <summary>Allocation-free agent: own unit selfId, plus one ignored unit (pass NoId or a negative id for none).</summary>
        public static NavAgent ForUnit(float radius, int selfId, int ignoreId = NoId)
        {
            var a = new NavAgent(radius, selfId);
            a.IgnoreId = ignoreId < 0 ? NoId : ignoreId;
            return a;
        }

        /// <summary>Radius 0.35 m, no own unit, respects all units.</summary>
        public static NavAgent Default => new NavAgent(DefaultRadius);

        /// <summary>Copy that also ignores the given unit ids.</summary>
        public NavAgent Ignoring(params int[] ids)
        {
            var copy = this;
            if (ids == null || ids.Length == 0) return copy;
            if (Ignore == null || Ignore.Count == 0) { copy.Ignore = ids; return copy; }
            var merged = new List<int>(Ignore);
            merged.AddRange(ids);
            copy.Ignore = merged;
            return copy;
        }

        /// <summary>Copy that ignores all dynamic units.</summary>
        public NavAgent IgnoringAllUnits()
        {
            var copy = this;
            copy.IgnoreUnits = true;
            return copy;
        }

        public bool IsIgnored(int id)
        {
            if (IgnoreUnits) return true;
            if (id == SelfId || id == IgnoreId) return true;
            return Ignore != null && Ignore.Count > 0 && Ignore.Contains(id);
        }
    }

    public enum PathStatus
    {
        /// <summary>No movement possible (start outside the grid or completely enclosed). Points = [start].</summary>
        NoPath,
        /// <summary>Goal blocked or unreachable: the path leads to the closest reachable point.</summary>
        Partial,
        /// <summary>Path reaches the goal (or gets within range for range queries).</summary>
        Complete,
    }

    /// <summary>Result of a path query. Reusable: pass it back into the query to avoid allocations.</summary>
    public sealed class NavPath
    {
        /// <summary>Smoothed polyline; Points[0] is the start position.</summary>
        public readonly List<Vec2> Points = new List<Vec2>();
        public PathStatus Status = PathStatus.NoPath;
        /// <summary>Length in metres along <see cref="Points"/> (after truncation).</summary>
        public float Length;
        /// <summary>Length before truncation to a movement budget.</summary>
        public float FullLength;
        /// <summary>True when the path was cut to a maximum length.</summary>
        public bool Truncated;

        public int Count => Points.Count;
        public Vec2 Start => Points.Count > 0 ? Points[0] : default;
        public Vec2 End => Points.Count > 0 ? Points[Points.Count - 1] : default;
        /// <summary>Complete and not truncated: walking it arrives at the goal / in range.</summary>
        public bool ReachedGoal => Status == PathStatus.Complete && !Truncated;
        /// <summary>True when there is nothing to walk (fewer than two points or zero length).</summary>
        public bool IsEmpty => Points.Count < 2 || Length <= 1e-5f;

        public void Clear()
        {
            Points.Clear();
            Status = PathStatus.NoPath;
            Length = FullLength = 0;
            Truncated = false;
        }

        public void CopyFrom(NavPath other)
        {
            Points.Clear();
            Points.AddRange(other.Points);
            Status = other.Status;
            Length = other.Length;
            FullLength = other.FullLength;
            Truncated = other.Truncated;
        }

        /// <summary>Recomputes <see cref="Length"/> from the points.</summary>
        public float RecomputeLength()
        {
            float len = 0;
            for (int i = 1; i < Points.Count; i++) len += Vec2.Distance(Points[i - 1], Points[i]);
            Length = len;
            return len;
        }

        /// <summary>Cuts the path in place so that its length is at most maxLength.</summary>
        public void Truncate(float maxLength)
        {
            if (Points.Count == 0) return;
            if (maxLength < 0) maxLength = 0;
            if (Length <= maxLength + 1e-5f) return;
            float acc = 0;
            for (int i = 1; i < Points.Count; i++)
            {
                float seg = Vec2.Distance(Points[i - 1], Points[i]);
                if (acc + seg >= maxLength)
                {
                    float t = seg > 1e-6f ? (maxLength - acc) / seg : 0f;
                    var cut = Vec2.Lerp(Points[i - 1], Points[i], t);
                    Points.RemoveRange(i, Points.Count - i);
                    if (Vec2.Distance(Points[i - 1], cut) > 1e-5f) Points.Add(cut);
                    Truncated = true;
                    Length = Math.Min(maxLength, acc + seg);
                    RecomputeLength();
                    return;
                }
                acc += seg;
            }
        }

        /// <summary>Point at the given distance along the path (clamped to the ends).</summary>
        public Vec2 PointAt(float distance)
        {
            if (Points.Count == 0) return default;
            if (distance <= 0) return Points[0];
            float acc = 0;
            for (int i = 1; i < Points.Count; i++)
            {
                float seg = Vec2.Distance(Points[i - 1], Points[i]);
                if (acc + seg >= distance)
                {
                    float t = seg > 1e-6f ? (distance - acc) / seg : 0f;
                    return Vec2.Lerp(Points[i - 1], Points[i], t);
                }
                acc += seg;
            }
            return Points[Points.Count - 1];
        }

        public override string ToString() => $"NavPath({Status}, {Points.Count} pts, {Length:0.##} m{(Truncated ? ", truncated" : "")})";
    }
}
