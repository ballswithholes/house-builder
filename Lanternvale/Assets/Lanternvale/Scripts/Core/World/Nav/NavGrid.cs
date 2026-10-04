// Navigation grid for exploration and combat movement. Pure C# (no UnityEngine).
//
// World metres: x in [0, width], y in [0, depth] (y = 0 nearest the camera). The grid covers the map's ground
// rectangle with square cells (default 0.5 m). Static walkability comes from the ground rect (minus a margin),
// the optional walkable polygon, prop collider ellipses and chest footprints. A clearance field (exact Euclidean
// distance transform) lets one grid serve agents of any radius. Dynamic unit circles are stamped per query.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    public sealed partial class NavGrid
    {
        /// <summary>Range queries stop this far inside the requested range so `distance &lt;= range` checks pass.</summary>
        public const float RangeEpsilon = 0.02f;
        /// <summary>Slack used when comparing unit circles (touching units do not block each other).</summary>
        public const float UnitEpsilon = 0.01f;

        const float ClearanceEpsilon = 1e-4f;
        const double EdtInf = 1e20;

        public readonly NavGridOptions Options;
        /// <summary>Source map (null for grids built from a plain rectangle).</summary>
        public readonly MapDef Map;

        /// <summary>Grid width in cells.</summary>
        public int Width { get; private set; }
        /// <summary>Grid height (depth) in cells.</summary>
        public int Height { get; private set; }
        public float CellSize { get; }
        public float WorldWidth { get; }
        public float WorldDepth { get; }
        /// <summary>Incremented whenever static walkability is rebuilt.</summary>
        public int Version { get; private set; }

        readonly float invCs;
        bool[] blocked;
        float[] clearance;

        struct Ellipse
        {
            public Vec2 c;
            public float rx, ry;
        }

        struct Rect
        {
            public Vec2 min, max;
        }

        readonly List<Ellipse> obstacleEllipses = new List<Ellipse>();
        readonly List<Rect> obstacleRects = new List<Rect>();
        readonly List<Ellipse> losBlockers = new List<Ellipse>();
        readonly List<Vec2> polygon = new List<Vec2>();

        // ------------------------------------------------------------------ construction

        /// <summary>Empty rectangular field (tests, arenas). Call <see cref="Rebuild"/> after adding obstacles.</summary>
        public NavGrid(float width, float depth, NavGridOptions options = null) : this(width, depth, options, true) { }

        NavGrid(float width, float depth, NavGridOptions options, bool build)
        {
            Options = options ?? new NavGridOptions();
            CellSize = Options.CellSize > 0.05f ? Options.CellSize : 0.5f;
            invCs = 1f / CellSize;
            WorldWidth = Math.Max(CellSize, width);
            WorldDepth = Math.Max(CellSize, depth);
            Width = Math.Max(1, (int)Math.Ceiling(WorldWidth * invCs - 1e-4f));
            Height = Math.Max(1, (int)Math.Ceiling(WorldDepth * invCs - 1e-4f));
            AllocateSearchBuffers();
            if (build) Rebuild();
        }

        /// <summary>Builds the grid from a map's ground, walkable polygon, prop colliders and chests.</summary>
        public NavGrid(MapDef map, NavGridOptions options = null) : this(map?.width ?? 60f, map?.depth ?? 20f, options, false)
        {
            Map = map;
            if (map == null) { Rebuild(); return; }
            if (map.walkable != null && map.walkable.Count >= 3) polygon.AddRange(map.walkable);
            if (map.props != null)
            {
                foreach (var prop in map.props)
                {
                    if (prop?.collider == null) continue;
                    float w = prop.collider.w, h = prop.collider.h;
                    if (w <= 0 && h <= 0) continue;
                    if (h <= 0) h = w * 0.5f;
                    if (w <= 0) w = h * 2f;
                    float scale = prop.scale > 0 ? prop.scale : 1f;
                    var off = prop.collider.offset * scale;
                    if (prop.flip) off.x = -off.x;
                    var c = prop.pos + off;
                    AddObstacleEllipseNoRebuild(c, w * scale, h * scale);
                    if (Options.LineOfSight && Math.Max(w, h) * scale >= Options.LosBlockerMinSize)
                        AddLosBlocker(c, w * scale, h * scale);
                }
            }
            if (Options.IncludeChests && map.chests != null)
                foreach (var chest in map.chests)
                    if (chest != null) AddObstacleEllipseNoRebuild(chest.pos, Options.ChestFootprint.x, Options.ChestFootprint.y);
            Rebuild();
        }

        /// <summary>Adds an elliptical static obstacle (full width w × depth h). Call <see cref="Rebuild"/> afterwards.</summary>
        public void AddObstacleEllipse(Vec2 center, float w, float h) => AddObstacleEllipseNoRebuild(center, w, h);

        /// <summary>Adds a rectangular static obstacle. Call <see cref="Rebuild"/> afterwards.</summary>
        public void AddObstacleRect(Vec2 min, Vec2 max)
        {
            obstacleRects.Add(new Rect
            {
                min = new Vec2(Math.Min(min.x, max.x), Math.Min(min.y, max.y)),
                max = new Vec2(Math.Max(min.x, max.x), Math.Max(min.y, max.y)),
            });
        }

        /// <summary>Adds an ellipse (full width × depth) that blocks line of sight (not movement).</summary>
        public void AddLosBlocker(Vec2 center, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            losBlockers.Add(new Ellipse { c = center, rx = w * 0.5f, ry = h * 0.5f });
        }

        /// <summary>Replaces the walkable polygon (empty = whole ground rect). Call <see cref="Rebuild"/> afterwards.</summary>
        public void SetWalkablePolygon(IList<Vec2> points)
        {
            polygon.Clear();
            if (points != null && points.Count >= 3) polygon.AddRange(points);
        }

        /// <summary>Removes all static obstacles, LOS blockers and the polygon. Call <see cref="Rebuild"/> afterwards.</summary>
        public void ClearObstacles()
        {
            obstacleEllipses.Clear();
            obstacleRects.Clear();
            losBlockers.Clear();
            polygon.Clear();
        }

        void AddObstacleEllipseNoRebuild(Vec2 center, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            obstacleEllipses.Add(new Ellipse { c = center, rx = w * 0.5f, ry = h * 0.5f });
        }

        /// <summary>Recomputes static walkability and the clearance field.</summary>
        public void Rebuild()
        {
            int n = Width * Height;
            if (blocked == null || blocked.Length != n) blocked = new bool[n];
            if (clearance == null || clearance.Length != n) clearance = new float[n];
            float margin = Math.Max(0f, Options.EdgeMargin);
            bool usePoly = polygon.Count >= 3;
            for (int cy = 0; cy < Height; cy++)
            {
                float y = (cy + 0.5f) * CellSize;
                for (int cx = 0; cx < Width; cx++)
                {
                    float x = (cx + 0.5f) * CellSize;
                    bool b = x < margin - 1e-4f || y < margin - 1e-4f ||
                             x > WorldWidth - margin + 1e-4f || y > WorldDepth - margin + 1e-4f;
                    if (!b && usePoly && !PointInPolygon(polygon, x, y)) b = true;
                    blocked[cy * Width + cx] = b;
                }
            }
            foreach (var e in obstacleEllipses) RasterEllipse(e);
            foreach (var r in obstacleRects) RasterRect(r);
            ComputeClearance();
            componentCache.Clear();
            Version++;
        }

        void RasterEllipse(Ellipse e)
        {
            int x0 = Math.Max(0, (int)Math.Floor((e.c.x - e.rx) * invCs));
            int x1 = Math.Min(Width - 1, (int)Math.Floor((e.c.x + e.rx) * invCs));
            int y0 = Math.Max(0, (int)Math.Floor((e.c.y - e.ry) * invCs));
            int y1 = Math.Min(Height - 1, (int)Math.Floor((e.c.y + e.ry) * invCs));
            for (int cy = y0; cy <= y1; cy++)
            {
                float dy = ((cy + 0.5f) * CellSize - e.c.y) / e.ry;
                for (int cx = x0; cx <= x1; cx++)
                {
                    float dx = ((cx + 0.5f) * CellSize - e.c.x) / e.rx;
                    if (dx * dx + dy * dy <= 1f) blocked[cy * Width + cx] = true;
                }
            }
            if (WorldToCell(e.c, out int ccx, out int ccy)) blocked[ccy * Width + ccx] = true;
        }

        void RasterRect(Rect r)
        {
            int x0 = Math.Max(0, (int)Math.Floor(r.min.x * invCs));
            int x1 = Math.Min(Width - 1, (int)Math.Floor(r.max.x * invCs));
            int y0 = Math.Max(0, (int)Math.Floor(r.min.y * invCs));
            int y1 = Math.Min(Height - 1, (int)Math.Floor(r.max.y * invCs));
            for (int cy = y0; cy <= y1; cy++)
            {
                float y = (cy + 0.5f) * CellSize;
                if (y < r.min.y || y > r.max.y) continue;
                for (int cx = x0; cx <= x1; cx++)
                {
                    float x = (cx + 0.5f) * CellSize;
                    if (x >= r.min.x && x <= r.max.x) blocked[cy * Width + cx] = true;
                }
            }
            if (WorldToCell((r.min + r.max) * 0.5f, out int ccx, out int ccy)) blocked[ccy * Width + ccx] = true;
        }

        static bool PointInPolygon(List<Vec2> poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // Exact Euclidean distance transform (Felzenszwalb & Huttenlocher) over a grid padded with a blocked border.
        void ComputeClearance()
        {
            int pw = Width + 2, ph = Height + 2;
            var grid = new double[pw * ph];
            for (int py = 0; py < ph; py++)
            {
                for (int px = 0; px < pw; px++)
                {
                    bool inside = px >= 1 && px <= Width && py >= 1 && py <= Height;
                    grid[py * pw + px] = !inside || blocked[(py - 1) * Width + (px - 1)] ? 0 : EdtInf;
                }
            }
            int m = Math.Max(pw, ph);
            var f = new double[m];
            var d = new double[m];
            var v = new int[m];
            var z = new double[m + 1];
            for (int px = 0; px < pw; px++)
            {
                for (int py = 0; py < ph; py++) f[py] = grid[py * pw + px];
                Edt1D(f, ph, d, v, z);
                for (int py = 0; py < ph; py++) grid[py * pw + px] = d[py];
            }
            for (int py = 0; py < ph; py++)
            {
                for (int px = 0; px < pw; px++) f[px] = grid[py * pw + px];
                Edt1D(f, pw, d, v, z);
                for (int px = 0; px < pw; px++) grid[py * pw + px] = d[px];
            }
            for (int cy = 0; cy < Height; cy++)
            {
                for (int cx = 0; cx < Width; cx++)
                {
                    int i = cy * Width + cx;
                    if (blocked[i]) { clearance[i] = 0f; continue; }
                    double dist = Math.Sqrt(grid[(cy + 1) * pw + cx + 1]) * CellSize - CellSize * 0.5;
                    clearance[i] = (float)Math.Max(0.0, dist);
                }
            }
        }

        static void Edt1D(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = -1;
            for (int q = 0; q < n; q++)
            {
                if (f[q] >= EdtInf) continue;
                if (k < 0)
                {
                    k = 0; v[0] = q; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
                    continue;
                }
                double s;
                while (true)
                {
                    int vk = v[k];
                    s = ((f[q] + (double)q * q) - (f[vk] + (double)vk * vk)) / (2.0 * q - 2.0 * vk);
                    if (s <= z[k]) k--;
                    else break;
                }
                k++;
                v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
            }
            if (k < 0)
            {
                for (int q = 0; q < n; q++) d[q] = EdtInf;
                return;
            }
            int j = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[j + 1] < q) j++;
                double dq = q - v[j];
                d[q] = dq * dq + f[v[j]];
            }
        }

        // ------------------------------------------------------------------ cells

        public bool InBounds(int cx, int cy) => cx >= 0 && cy >= 0 && cx < Width && cy < Height;

        public bool InBounds(Vec2 p) => p.x >= 0 && p.y >= 0 && p.x < Width * CellSize && p.y < Height * CellSize;

        /// <summary>Cell containing p; false when p is outside the grid.</summary>
        public bool WorldToCell(Vec2 p, out int cx, out int cy)
        {
            cx = (int)Math.Floor(p.x * invCs);
            cy = (int)Math.Floor(p.y * invCs);
            return InBounds(cx, cy);
        }

        public Vec2 CellCenter(int cx, int cy) => new Vec2((cx + 0.5f) * CellSize, (cy + 0.5f) * CellSize);

        Vec2 CellCenter(int index) => CellCenter(index % Width, index / Width);

        int CellIndexClamped(Vec2 p)
        {
            int cx = MathUtil.Clamp((int)Math.Floor(p.x * invCs), 0, Width - 1);
            int cy = MathUtil.Clamp((int)Math.Floor(p.y * invCs), 0, Height - 1);
            return cy * Width + cx;
        }

        int CellIndexOrNeg(Vec2 p) => WorldToCell(p, out int cx, out int cy) ? cy * Width + cx : -1;

        /// <summary>Clamps p into the grid rectangle.</summary>
        public Vec2 ClampToBounds(Vec2 p)
        {
            float maxX = Width * CellSize - 1e-3f, maxY = Height * CellSize - 1e-3f;
            return new Vec2(MathUtil.Clamp(p.x, 0f, maxX), MathUtil.Clamp(p.y, 0f, maxY));
        }

        /// <summary>True when the cell is free of static obstacles and has at least `radius` clearance.</summary>
        public bool IsCellWalkable(int cx, int cy, float radius = 0f)
        {
            if (!InBounds(cx, cy)) return false;
            int i = cy * Width + cx;
            return !blocked[i] && clearance[i] >= radius - ClearanceEpsilon;
        }

        /// <summary>True when a static obstacle covers the cell.</summary>
        public bool IsCellBlocked(int cx, int cy) => !InBounds(cx, cy) || blocked[cy * Width + cx];

        /// <summary>Distance in metres from the cell centre to the nearest static obstacle edge (0 when blocked).</summary>
        public float Clearance(int cx, int cy) => InBounds(cx, cy) ? clearance[cy * Width + cx] : 0f;

        bool StaticOpen(int i, float r) => !blocked[i] && clearance[i] >= r - ClearanceEpsilon;

        // ------------------------------------------------------------------ dynamic units

        struct Unit
        {
            public int id;
            public Vec2 pos;
            public float radius;
        }

        readonly List<Unit> units = new List<Unit>();
        readonly Dictionary<int, int> unitIndex = new Dictionary<int, int>();

        public int UnitCount => units.Count;

        /// <summary>Registers or moves a dynamic unit circle. A radius ≤ 0 removes the unit (dead/despawned).</summary>
        public void SetUnit(int id, Vec2 pos, float radius)
        {
            if (radius <= 0f)
            {
                RemoveUnit(id);
                return;
            }
            var u = new Unit { id = id, pos = pos, radius = radius };
            if (unitIndex.TryGetValue(id, out int idx)) units[idx] = u;
            else
            {
                unitIndex[id] = units.Count;
                units.Add(u);
            }
        }

        public bool RemoveUnit(int id)
        {
            if (!unitIndex.TryGetValue(id, out int idx)) return false;
            int last = units.Count - 1;
            if (idx != last)
            {
                units[idx] = units[last];
                unitIndex[units[idx].id] = idx;
            }
            units.RemoveAt(last);
            unitIndex.Remove(id);
            return true;
        }

        public void ClearUnits()
        {
            units.Clear();
            unitIndex.Clear();
        }

        public bool TryGetUnit(int id, out Vec2 pos, out float radius)
        {
            if (unitIndex.TryGetValue(id, out int idx))
            {
                pos = units[idx].pos;
                radius = units[idx].radius;
                return true;
            }
            pos = default;
            radius = 0;
            return false;
        }

        /// <summary>Ids of all registered units (into is cleared first).</summary>
        public void GetUnitIds(List<int> into)
        {
            into.Clear();
            foreach (var u in units) into.Add(u.id);
        }

        /// <summary>True when a circle of the agent's radius at p overlaps a non-ignored unit.</summary>
        public bool IsOccupied(Vec2 p, NavAgent agent) => UnitBlocks(p, agent);

        /// <summary>Id of the first non-ignored unit overlapping the agent at p, or NavAgent.NoId.</summary>
        public int UnitAt(Vec2 p, NavAgent agent)
        {
            if (agent.IgnoreUnits) return NavAgent.NoId;
            foreach (var u in units)
            {
                if (agent.IsIgnored(u.id)) continue;
                float rr = u.radius + agent.Radius - UnitEpsilon;
                if (rr > 0 && (p - u.pos).SqrLength < rr * rr) return u.id;
            }
            return NavAgent.NoId;
        }

        bool UnitBlocks(Vec2 p, in NavAgent agent) => UnitAt(p, agent) != NavAgent.NoId;

        // per-query stamping of unit discs (inflated by the agent radius)
        int[] dynStamp;
        int dynGen;

        void PrepareUnits(in NavAgent agent)
        {
            dynGen++;
            if (dynGen == int.MaxValue)
            {
                Array.Clear(dynStamp, 0, dynStamp.Length);
                dynGen = 1;
            }
            if (agent.IgnoreUnits || units.Count == 0) return;
            foreach (var u in units)
            {
                if (agent.IsIgnored(u.id)) continue;
                float rr = u.radius + agent.Radius - UnitEpsilon;
                if (rr <= 0) continue;
                int x0 = Math.Max(0, (int)Math.Floor((u.pos.x - rr) * invCs));
                int x1 = Math.Min(Width - 1, (int)Math.Floor((u.pos.x + rr) * invCs));
                int y0 = Math.Max(0, (int)Math.Floor((u.pos.y - rr) * invCs));
                int y1 = Math.Min(Height - 1, (int)Math.Floor((u.pos.y + rr) * invCs));
                float rr2 = rr * rr;
                for (int cy = y0; cy <= y1; cy++)
                {
                    float dy = (cy + 0.5f) * CellSize - u.pos.y;
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        float dx = (cx + 0.5f) * CellSize - u.pos.x;
                        if (dx * dx + dy * dy < rr2) dynStamp[cy * Width + cx] = dynGen;
                    }
                }
                if (WorldToCell(u.pos, out int ux, out int uy)) dynStamp[uy * Width + ux] = dynGen;
            }
        }

        /// <summary>Static clearance and (stamped) unit test for a cell. PrepareUnits must have run.</summary>
        bool Open(int i, float r) => !blocked[i] && clearance[i] >= r - ClearanceEpsilon && dynStamp[i] != dynGen;

        // ------------------------------------------------------------------ point queries

        /// <summary>Static walkability for a circle of the given radius (units ignored).</summary>
        public bool IsWalkable(Vec2 p, float radius)
        {
            if (!WorldToCell(p, out int cx, out int cy)) return false;
            return StaticOpen(cy * Width + cx, radius);
        }

        /// <summary>Walkable for the agent: static clearance plus no overlap with non-ignored units.</summary>
        public bool IsWalkable(Vec2 p, NavAgent agent)
        {
            if (!IsWalkable(p, agent.Radius)) return false;
            return !UnitBlocks(p, agent);
        }

        /// <summary>True when the agent can walk the straight segment a→b (static obstacles and units).</summary>
        public bool LineWalkable(Vec2 a, Vec2 b, NavAgent agent)
        {
            PrepareUnits(agent);
            return LineClear(a, b, agent.Radius, -1, -1);
        }

        /// <summary>Static-only straight-walk test for a circle of the given radius.</summary>
        public bool LineWalkable(Vec2 a, Vec2 b, float radius)
        {
            PrepareUnits(new NavAgent(radius, NavAgent.NoId, null, true));
            return LineClear(a, b, radius, -1, -1);
        }

        // Grid traversal (Amanatides & Woo). skipCell/allowCell are exempt from the test.
        bool LineClear(Vec2 a, Vec2 b, float r, int skipCell, int allowCell)
        {
            if (!WorldToCell(a, out int cx, out int cy) || !WorldToCell(b, out int ex, out int ey)) return false;
            float dx = b.x - a.x, dy = b.y - a.y;
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            float tMaxX = float.PositiveInfinity, tMaxY = float.PositiveInfinity, tDeltaX = 0, tDeltaY = 0;
            if (stepX != 0)
            {
                float bx = (cx + (stepX > 0 ? 1 : 0)) * CellSize;
                tMaxX = (bx - a.x) / dx;
                tDeltaX = CellSize / Math.Abs(dx);
            }
            if (stepY != 0)
            {
                float by = (cy + (stepY > 0 ? 1 : 0)) * CellSize;
                tMaxY = (by - a.y) / dy;
                tDeltaY = CellSize / Math.Abs(dy);
            }
            int remaining = Math.Abs(ex - cx) + Math.Abs(ey - cy);
            int W = Width;
            while (true)
            {
                int i = cy * W + cx;
                if (i != skipCell && i != allowCell && !Open(i, r)) return false;
                if ((cx == ex && cy == ey) || remaining <= 0) return true;
                bool moveX, moveY;
                if (cx == ex) { moveX = false; moveY = true; }
                else if (cy == ey) { moveX = true; moveY = false; }
                else if (Math.Abs(tMaxX - tMaxY) < 1e-6f) { moveX = true; moveY = true; }
                else { moveX = tMaxX < tMaxY; moveY = !moveX; }
                if (moveX && moveY)
                {
                    // passing exactly through a corner: both side cells must be open (no corner cutting)
                    int s1 = cy * W + cx + stepX, s2 = (cy + stepY) * W + cx;
                    if ((s1 != allowCell && !Open(s1, r)) || (s2 != allowCell && !Open(s2, r))) return false;
                    cx += stepX; cy += stepY; tMaxX += tDeltaX; tMaxY += tDeltaY;
                    remaining -= 2;
                }
                else if (moveX) { cx += stepX; tMaxX += tDeltaX; remaining--; }
                else { cy += stepY; tMaxY += tDeltaY; remaining--; }
                if (!InBounds(cx, cy)) return false;
            }
        }

        /// <summary>
        /// Line of sight: blocked only by big props (collider ≥ LosBlockerMinSize) and AddLosBlocker ellipses,
        /// each shrunk to 80% so units standing next to them can still see past the edge.
        /// </summary>
        public bool HasLineOfSight(Vec2 a, Vec2 b)
        {
            if (!Options.LineOfSight || losBlockers.Count == 0) return true;
            foreach (var e in losBlockers)
            {
                float rx = e.rx * 0.8f, ry = e.ry * 0.8f;
                if (rx <= 0 || ry <= 0) continue;
                float ax = (a.x - e.c.x) / rx, ay = (a.y - e.c.y) / ry;
                float bx = (b.x - e.c.x) / rx, by = (b.y - e.c.y) / ry;
                float ddx = bx - ax, ddy = by - ay;
                float len2 = ddx * ddx + ddy * ddy;
                float t = len2 > 1e-12f ? MathUtil.Clamp(-(ax * ddx + ay * ddy) / len2, 0f, 1f) : 0f;
                float px = ax + ddx * t, py = ay + ddy * t;
                if (px * px + py * py < 1f) return false;
            }
            return true;
        }

        /// <summary>The point itself when walkable for the agent, otherwise the nearest walkable cell centre.</summary>
        public Vec2 ClampToWalkable(Vec2 point, NavAgent agent)
        {
            var p = ClampToBounds(point);
            if (IsWalkable(p, agent)) return p;
            PrepareUnits(agent);
            int idx = NearestOpenCell(CellIndexClamped(p), p, agent.Radius, Math.Max(Width, Height));
            return idx >= 0 ? CellCenter(idx) : p;
        }

        // Ring search for the open cell nearest to refPoint (optionally restricted to one connectivity label).
        // With a tie point, candidates are scored distance(refPoint) + 5% of distance(tie) so that near-equal
        // candidates prefer the side of the tie point (the path start). PrepareUnits must have run.
        int NearestOpenCell(int fromIndex, Vec2 refPoint, float r, int maxRing, int[] labels = null, int label = 0, Vec2? tie = null)
        {
            int fx = fromIndex % Width, fy = fromIndex / Width;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int ring = 0; ring <= maxRing; ring++)
            {
                if (best >= 0 && (ring - 1) * CellSize > bestScore) break;
                int x0 = fx - ring, x1 = fx + ring, y0 = fy - ring, y1 = fy + ring;
                if (x0 < 0 && y0 < 0 && x1 >= Width && y1 >= Height) break;
                for (int cy = y0; cy <= y1; cy++)
                {
                    if (cy < 0 || cy >= Height) continue;
                    bool edgeRow = cy == y0 || cy == y1;
                    for (int cx = x0; cx <= x1; cx += edgeRow ? 1 : Math.Max(1, x1 - x0))
                    {
                        if (cx < 0 || cx >= Width) continue;
                        int i = cy * Width + cx;
                        if (labels != null && labels[i] != label) continue;
                        if (!Open(i, r)) continue;
                        var c = CellCenter(cx, cy);
                        float score = (c - refPoint).Length;
                        if (tie.HasValue) score += 0.05f * (c - tie.Value).Length;
                        if (score < bestScore) { bestScore = score; best = i; }
                    }
                }
            }
            return best;
        }

        /// <summary>Random walkable point within maxDist of center that can be walked to in a straight line (wander).</summary>
        public bool RandomWalkablePointNear(Vec2 center, float maxDist, Rng rng, NavAgent agent, out Vec2 point)
        {
            PrepareUnits(agent);
            int skip = CellIndexOrNeg(center);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float ang = rng.Range(0f, (float)(Math.PI * 2));
                float d = maxDist * (float)Math.Sqrt(rng.Value);
                var q = center + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * d;
                if (!IsWalkable(q, agent.Radius) || UnitBlocks(q, agent)) continue;
                if (!LineClear(center, q, agent.Radius, skip, -1)) continue;
                point = q;
                return true;
            }
            point = ClampToWalkable(center, agent);
            return false;
        }

        /// <summary>
        /// Point roughly `distance` metres away from `awayFrom`, reachable from `from` in a straight line (fear).
        /// Tries the direct flee direction first, then angles up to ±120°, then shorter distances.
        /// </summary>
        public bool FleePoint(Vec2 from, Vec2 awayFrom, float distance, Rng rng, NavAgent agent, out Vec2 point)
        {
            PrepareUnits(agent);
            var dir = (from - awayFrom).Normalized;
            if (dir.SqrLength < 1e-6f)
            {
                float a0 = rng.Range(0f, (float)(Math.PI * 2));
                dir = new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0));
            }
            float baseAng = (float)Math.Atan2(dir.y, dir.x);
            float jitter = rng.Range(-0.2f, 0.2f);
            int skip = CellIndexOrNeg(from);
            float[] fractions = { 1f, 0.66f, 0.33f };
            float[] offsets = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f };
            foreach (var frac in fractions)
            {
                foreach (var offDeg in offsets)
                {
                    float ang = baseAng + jitter + offDeg * (float)(Math.PI / 180.0);
                    var q = from + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * (distance * frac);
                    if (!IsWalkable(q, agent.Radius) || UnitBlocks(q, agent)) continue;
                    if (!LineClear(from, q, agent.Radius, skip, -1)) continue;
                    point = q;
                    return true;
                }
            }
            return RandomWalkablePointNear(from, distance, rng, agent, out point);
        }
    }
}
