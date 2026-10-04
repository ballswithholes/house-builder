// Movement-range flood (any-angle Dijkstra, Theta*-style parent shortcuts) and its queryable result.
using System;
using System.Collections.Generic;
using Lanternvale.Util;

namespace Lanternvale.World
{
    public sealed partial class NavGrid
    {
        /// <summary>
        /// Every cell reachable from start within maxMetres of walking (any-angle distances, so the area is round in
        /// open ground). Pass a previous ReachMap as `into` to reuse its buffers.
        /// </summary>
        public ReachMap ReachableWithin(Vec2 start, float maxMetres, NavAgent agent, ReachMap into = null)
        {
            var map = into ?? new ReachMap();
            map.Reset(this, agent, start, Math.Max(0f, maxMetres));
            PrepareUnits(agent);
            float r = agent.Radius;
            int s = StartCell(start, r, out int startExact);
            map.startExact = startExact;
            if (s < 0) return map;

            int W = Width, H = Height;
            int K = (int)Math.Ceiling(map.MaxMetres * invCs) + 2;
            int scx = s % W, scy = s / W;
            int x0 = Math.Max(0, scx - K), y0 = Math.Max(0, scy - K);
            int x1 = Math.Min(W - 1, scx + K), y1 = Math.Min(H - 1, scy + K);
            map.Allocate(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            int ww = map.w;

            int sl = (scy - y0) * ww + (scx - x0);
            float d0 = Vec2.Distance(start, CellCenter(s));
            if (d0 > map.MaxMetres + 1e-4f) return map;
            map.dist[sl] = d0;
            map.parent[sl] = ReachMap.ParentStart;
            heapCount = 0;
            HeapPush(sl, d0);
            float cs = CellSize, diagCost = cs * Sqrt2, limit = map.MaxMetres + 1e-4f;

            // Lazy Theta*: relaxations optimistically inherit the expanded cell's parent (any-angle shortcut);
            // the straight walk is verified once when a cell is expanded, falling back to its best closed neighbour.
            while (heapCount > 0)
            {
                int cur = HeapPop();
                if (map.closed[cur] != 0) continue;
                int lx = cur % ww, ly = cur / ww;
                int cx = lx + x0, cy = ly + y0;
                var cc = new Vec2((cx + 0.5f) * cs, (cy + 0.5f) * cs);
                int par = map.parent[cur];
                if (cur != sl)
                {
                    Vec2 pp = par == ReachMap.ParentStart ? start : map.LocalCenter(par);
                    if (!LineClear(pp, cc, r, par == ReachMap.ParentStart ? startExact : -1, -1))
                    {
                        float best = float.PositiveInfinity;
                        int bp = -1;
                        for (int k = 0; k < 8; k++)
                        {
                            int nx = cx + DX[k], ny = cy + DY[k];
                            if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                            int nl = (ny - y0) * ww + (nx - x0);
                            if (map.closed[nl] == 0) continue;
                            bool diag = k >= 4;
                            if (diag && (!Open(cy * W + nx, r) || !Open(ny * W + cx, r))) continue;
                            float g = map.dist[nl] + (diag ? diagCost : cs);
                            if (g < best) { best = g; bp = nl; }
                        }
                        if (bp < 0) { map.closed[cur] = 1; map.dist[cur] = float.PositiveInfinity; continue; }
                        map.dist[cur] = best;
                        map.parent[cur] = bp;
                        par = bp;
                    }
                }
                map.closed[cur] = 1;
                float gc = map.dist[cur];
                if (gc > limit) continue;
                map.count++;
                Vec2 parPos = par == ReachMap.ParentStart ? start : map.LocalCenter(par);
                float parG = par == ReachMap.ParentStart ? 0f : map.dist[par];
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                    int nl = (ny - y0) * ww + (nx - x0);
                    if (map.closed[nl] != 0) continue;
                    if (!Open(ny * W + nx, r)) continue;
                    bool diag = k >= 4;
                    if (diag && (!Open(cy * W + nx, r) || !Open(ny * W + cx, r))) continue;
                    var nc = new Vec2((nx + 0.5f) * cs, (ny + 0.5f) * cs);
                    float cand;
                    int candParent;
                    if (cur == sl && par == ReachMap.ParentStart && startExact != s)
                    {
                        // start was relocated to the nearest open cell: walk from that cell
                        cand = gc + (diag ? diagCost : cs);
                        candParent = cur;
                    }
                    else
                    {
                        cand = parG + Vec2.Distance(parPos, nc);
                        candParent = par;
                    }
                    if (cand > limit || cand >= map.dist[nl]) continue;
                    map.dist[nl] = cand;
                    map.parent[nl] = candParent;
                    HeapPush(nl, cand);
                }
            }
            return map;
        }

        internal bool PrepareAndLineClear(in NavAgent agent, Vec2 a, Vec2 b, int skipCell)
        {
            PrepareUnits(agent);
            return LineClear(a, b, agent.Radius, skipCell, -1);
        }
    }

    /// <summary>Result of <see cref="NavGrid.ReachableWithin"/>: movement range for the UI and for move validation.</summary>
    public sealed class ReachMap
    {
        internal const int ParentStart = -2;

        NavGrid nav;
        NavAgent agent;
        internal int x0, y0, w, h;
        internal float[] dist = new float[0];
        internal int[] parent = new int[0];
        internal byte[] closed = new byte[0];
        internal int count;
        internal int startExact = -1;

        public Vec2 Start { get; private set; }
        public float MaxMetres { get; private set; }
        public float CellSize => nav?.CellSize ?? 0.5f;
        /// <summary>Number of reachable cells (within MaxMetres).</summary>
        public int ReachableCount => count;
        public NavGrid Grid => nav;

        internal void Reset(NavGrid grid, NavAgent a, Vec2 start, float maxMetres)
        {
            nav = grid;
            agent = a;
            Start = start;
            MaxMetres = maxMetres;
            x0 = y0 = w = h = 0;
            count = 0;
            startExact = -1;
        }

        internal void Allocate(int ox, int oy, int width, int height)
        {
            x0 = ox; y0 = oy; w = width; h = height;
            int n = w * h;
            if (dist.Length < n)
            {
                dist = new float[n];
                parent = new int[n];
                closed = new byte[n];
            }
            for (int i = 0; i < n; i++)
            {
                dist[i] = float.PositiveInfinity;
                parent[i] = -1;
                closed[i] = 0;
            }
        }

        internal Vec2 LocalCenter(int local) => nav.CellCenter(local % w + x0, local / w + y0);

        int Local(int cx, int cy)
        {
            int lx = cx - x0, ly = cy - y0;
            if (lx < 0 || ly < 0 || lx >= w || ly >= h) return -1;
            return ly * w + lx;
        }

        bool Reached(int local) => local >= 0 && closed[local] != 0 && dist[local] <= MaxMetres + 1e-4f;

        public bool IsCellReachable(int cx, int cy) => Reached(Local(cx, cy));

        /// <summary>Walking distance to the cell centre, +inf when unreachable.</summary>
        public float CellDistance(int cx, int cy)
        {
            int l = Local(cx, cy);
            return Reached(l) ? dist[l] : float.PositiveInfinity;
        }

        /// <summary>Walking distance from Start to point (any-angle), +inf when unreachable.</summary>
        public float DistanceTo(Vec2 point)
        {
            if (nav == null || !nav.WorldToCell(point, out int cx, out int cy)) return float.PositiveInfinity;
            int l = Local(cx, cy);
            if (!Reached(l)) return float.PositiveInfinity;
            float best = dist[l] + Vec2.Distance(LocalCenter(l), point);
            int par = parent[l];
            if (par != -1)
            {
                Vec2 pp = par == ParentStart ? Start : LocalCenter(par);
                float pg = par == ParentStart ? 0f : dist[par];
                float via = pg + Vec2.Distance(pp, point);
                if (via < best && nav.PrepareAndLineClear(agent, pp, point, par == ParentStart ? startExact : -1)) best = via;
            }
            return best;
        }

        /// <summary>True when the point is walkable for the agent and within MaxMetres of walking.</summary>
        public bool CanReach(Vec2 point)
        {
            if (nav == null) return false;
            if (!nav.IsWalkable(point, agent)) return false;
            return DistanceTo(point) <= MaxMetres + 1e-4f;
        }

        /// <summary>Path from Start to point using the flood tree (no new search). NoPath when unreachable.</summary>
        public NavPath PathTo(Vec2 point, NavPath result = null)
        {
            result ??= new NavPath();
            result.Clear();
            result.Points.Add(Start);
            if (nav == null || !nav.WorldToCell(point, out int cx, out int cy)) return result;
            int l = Local(cx, cy);
            if (!Reached(l)) return result;
            float viaCell = dist[l] + Vec2.Distance(LocalCenter(l), point);
            int par = parent[l];
            int chainFrom = l;
            if (par != -1)
            {
                Vec2 pp = par == ParentStart ? Start : LocalCenter(par);
                float pg = par == ParentStart ? 0f : dist[par];
                float via = pg + Vec2.Distance(pp, point);
                if (via <= viaCell && nav.PrepareAndLineClear(agent, pp, point, par == ParentStart ? startExact : -1)) chainFrom = par;
            }
            // collect: point, chain of cell centres back to the start
            var pts = result.Points;
            pts.Clear();
            pts.Add(point);
            int c = chainFrom;
            int guard = w * h + 2;
            while (c != ParentStart && c != -1 && guard-- > 0)
            {
                pts.Add(LocalCenter(c));
                c = parent[c];
            }
            pts.Add(Start);
            pts.Reverse();
            for (int i = pts.Count - 1; i >= 1; i--)
                if ((pts[i] - pts[i - 1]).SqrLength < 1e-8f) pts.RemoveAt(i);
            result.Status = PathStatus.Complete;
            result.RecomputeLength();
            result.FullLength = result.Length;
            return result;
        }

        /// <summary>Appends the centres of all reachable cells.</summary>
        public void GetReachableCells(List<Vec2> into)
        {
            for (int l = 0; l < w * h; l++)
                if (Reached(l)) into.Add(LocalCenter(l));
        }

        /// <summary>Appends the centres of reachable cells that touch an unreachable cell (4-neighbourhood).</summary>
        public void GetBorderCells(List<Vec2> into)
        {
            for (int ly = 0; ly < h; ly++)
            {
                for (int lx = 0; lx < w; lx++)
                {
                    int l = ly * w + lx;
                    if (!Reached(l)) continue;
                    bool border = !ReachedXY(lx + 1, ly) || !ReachedXY(lx - 1, ly) || !ReachedXY(lx, ly + 1) || !ReachedXY(lx, ly - 1);
                    if (border) into.Add(LocalCenter(l));
                }
            }
        }

        /// <summary>
        /// Appends outline segments as consecutive point pairs (a0, b0, a1, b1, ...): every cell edge between a reachable
        /// and an unreachable cell, in world metres.
        /// </summary>
        public void GetOutline(List<Vec2> segmentPairs)
        {
            float cs = CellSize;
            for (int ly = 0; ly < h; ly++)
            {
                for (int lx = 0; lx < w; lx++)
                {
                    if (!Reached(ly * w + lx)) continue;
                    float minX = (lx + x0) * cs, minY = (ly + y0) * cs, maxX = minX + cs, maxY = minY + cs;
                    if (!ReachedXY(lx + 1, ly)) { segmentPairs.Add(new Vec2(maxX, minY)); segmentPairs.Add(new Vec2(maxX, maxY)); }
                    if (!ReachedXY(lx - 1, ly)) { segmentPairs.Add(new Vec2(minX, minY)); segmentPairs.Add(new Vec2(minX, maxY)); }
                    if (!ReachedXY(lx, ly + 1)) { segmentPairs.Add(new Vec2(minX, maxY)); segmentPairs.Add(new Vec2(maxX, maxY)); }
                    if (!ReachedXY(lx, ly - 1)) { segmentPairs.Add(new Vec2(minX, minY)); segmentPairs.Add(new Vec2(maxX, minY)); }
                }
            }
        }

        bool ReachedXY(int lx, int ly) => lx >= 0 && ly >= 0 && lx < w && ly < h && Reached(ly * w + lx);
    }
}
