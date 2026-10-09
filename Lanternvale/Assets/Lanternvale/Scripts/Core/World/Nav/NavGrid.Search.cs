// NavGrid path search: A* (8-directional, no corner cutting, octile heuristic, binary heap) with line-of-walk
// smoothing, closest-point fallback, range stops and truncation. All big buffers are allocated once per grid.
using System;
using System.Collections.Generic;
using Lanternvale.Util;

namespace Lanternvale.World
{
    public sealed partial class NavGrid
    {
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        const float Sqrt2 = 1.41421356f;
        const float HeuristicTieBreak = 1.001f;

        float[] gScore;
        int[] parentCell;
        int[] seenGen;
        int[] closedGen;
        int searchGen;

        int[] heapNode = new int[1024];
        float[] heapKey = new float[1024];
        int heapCount;

        readonly List<int> cellPath = new List<int>(256);
        readonly List<Vec2> rawPoints = new List<Vec2>(256);

        /// <summary>Number of nodes expanded by the last A* query (diagnostics).</summary>
        public int LastExpandedNodes { get; private set; }

        void AllocateSearchBuffers()
        {
            int n = Width * Height;
            gScore = new float[n];
            parentCell = new int[n];
            seenGen = new int[n];
            closedGen = new int[n];
            dynStamp = new int[n];
            searchGen = 0;
            dynGen = 0;
        }

        void NextSearchGen()
        {
            searchGen++;
            if (searchGen == int.MaxValue)
            {
                Array.Clear(seenGen, 0, seenGen.Length);
                Array.Clear(closedGen, 0, closedGen.Length);
                searchGen = 1;
            }
        }

        // ------------------------------------------------------------------ binary heap (lazy deletion)

        void HeapPush(int node, float key)
        {
            if (heapCount == heapNode.Length)
            {
                Array.Resize(ref heapNode, heapCount * 2);
                Array.Resize(ref heapKey, heapCount * 2);
            }
            int i = heapCount++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heapKey[p] <= key) break;
                heapNode[i] = heapNode[p];
                heapKey[i] = heapKey[p];
                i = p;
            }
            heapNode[i] = node;
            heapKey[i] = key;
        }

        int HeapPop()
        {
            int top = heapNode[0];
            int lastNode = heapNode[--heapCount];
            float lastKey = heapKey[heapCount];
            int i = 0;
            int half = heapCount >> 1;
            while (i < half)
            {
                int c = 2 * i + 1;
                if (c + 1 < heapCount && heapKey[c + 1] < heapKey[c]) c++;
                if (heapKey[c] >= lastKey) break;
                heapNode[i] = heapNode[c];
                heapKey[i] = heapKey[c];
                i = c;
            }
            if (heapCount > 0)
            {
                heapNode[i] = lastNode;
                heapKey[i] = lastKey;
            }
            return top;
        }

        // ------------------------------------------------------------------ public path queries

        /// <summary>
        /// Path from start to goal. When the goal is blocked or unreachable the path leads to the closest
        /// reachable point (Status = Partial). maxLength truncates the result (combat movement budget).
        /// </summary>
        public NavPath FindPath(Vec2 start, Vec2 goal, NavAgent agent, float maxLength = float.PositiveInfinity, NavPath result = null)
        {
            result ??= new NavPath();
            result.Clear();
            result.Points.Add(start);
            PrepareUnits(agent);
            float r = agent.Radius;
            int s = StartCell(start, r, out int startExact);
            if (s < 0) return result;
            PathToPoint(start, s, startExact, goal, agent, result);
            if (maxLength < result.Length) result.Truncate(maxLength);
            return result;
        }

        /// <summary>
        /// Path that stops as soon as the agent is within `range` metres of `target` (melee approach, spell range).
        /// With requireLineOfSight the stop point must also see the target. Unreachable → closest point (Partial).
        /// </summary>
        public NavPath FindPathToRange(Vec2 start, Vec2 target, float range, NavAgent agent, float maxLength = float.PositiveInfinity,
                                       bool requireLineOfSight = false, NavPath result = null)
        {
            result ??= new NavPath();
            result.Clear();
            result.Points.Add(start);
            if (range < 0) range = 0;
            if (Vec2.Distance(start, target) <= range && (!requireLineOfSight || HasLineOfSight(start, target)))
            {
                result.Status = PathStatus.Complete;
                return result;
            }
            PrepareUnits(agent);
            float r = agent.Radius;
            int s = StartCell(start, r, out int startExact);
            if (s < 0) return result;
            float stopR = Math.Max(0f, range - RangeEpsilon);
            var labels = Components(r);
            int label = labels[s];

            if (stopR < CellSize || !AnyCellInRange(target, stopR, labels, label))
            {
                // Tiny range (may contain no cell centre) or no reachable cell in range: walk towards the target point.
                PathToPoint(start, s, startExact, target, agent, result);
                bool inRange = CutAtRange(result.Points, target, stopR, requireLineOfSight);
                result.Status = inRange ? PathStatus.Complete : PathStatus.Partial;
            }
            else
            {
                int found = AStar(s, -1, target, -1, r, true, target, stopR, requireLineOfSight, out bool reached);
                BuildRawPoints(start, s, startExact, found, null);
                Smooth(rawPoints, result.Points, r, startExact, -1);
                if (reached) CutAtRange(result.Points, target, stopR, requireLineOfSight);
                result.Status = reached ? PathStatus.Complete : PathStatus.Partial;
            }
            result.RecomputeLength();
            result.FullLength = result.Length;
            if (maxLength < result.Length) result.Truncate(maxLength);
            return result;
        }

        // Point search shared by FindPath and tiny-range queries. PrepareUnits must have run; s = StartCell(...).
        void PathToPoint(Vec2 start, int s, int startExact, Vec2 goal, in NavAgent agent, NavPath result)
        {
            float r = agent.Radius;
            bool goalOk = IsWalkable(goal, r) && !UnitBlocks(goal, agent);
            int goalCell = goalOk ? CellIndexOrNeg(goal) : NearestOpenCell(CellIndexClamped(goal), ClampToBounds(goal), r, Math.Max(Width, Height), null, 0, start);
            var labels = Components(r);
            int label = labels[s];
            if (goalCell < 0 || labels[goalCell] != label)
            {
                // statically unreachable: aim for the closest cell of the start's component (avoids flooding the map)
                goalOk = false;
                goalCell = NearestOpenCell(CellIndexClamped(goal), ClampToBounds(goal), r, Math.Max(Width, Height), labels, label, start);
            }
            int allow = goalOk ? goalCell : -1;
            int found = AStar(s, goalCell, goal, allow, r, false, default, 0f, false, out bool reached);
            bool complete = reached && goalOk;
            BuildRawPoints(start, s, startExact, found, complete ? goal : (Vec2?)null);
            Smooth(rawPoints, result.Points, r, startExact, allow);
            result.Status = complete ? PathStatus.Complete : PathStatus.Partial;
            result.RecomputeLength();
            result.FullLength = result.Length;
        }

        bool AnyCellInRange(Vec2 target, float R, int[] labels, int label)
        {
            int x0 = Math.Max(0, (int)Math.Floor((target.x - R) * invCs));
            int x1 = Math.Min(Width - 1, (int)Math.Floor((target.x + R) * invCs));
            int y0 = Math.Max(0, (int)Math.Floor((target.y - R) * invCs));
            int y1 = Math.Min(Height - 1, (int)Math.Floor((target.y + R) * invCs));
            float r2 = R * R;
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    int i = cy * Width + cx;
                    if (labels[i] != label) continue;
                    if ((CellCenter(cx, cy) - target).SqrLength <= r2) return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ static connectivity

        readonly Dictionary<int, int[]> componentCache = new Dictionary<int, int[]>();
        int[] bfsQueue;

        /// <summary>Connected-component labels of statically walkable cells for an agent radius (0 = blocked). Cached.</summary>
        int[] Components(float r)
        {
            int key = (int)Math.Round(r * 1000f);
            if (componentCache.TryGetValue(key, out var labels)) return labels;
            if (componentCache.Count >= 8) componentCache.Clear();
            int n = Width * Height, W = Width, H = Height;
            labels = new int[n];
            if (bfsQueue == null || bfsQueue.Length < n) bfsQueue = new int[n];
            var q = bfsQueue;
            int next = 0;
            for (int i = 0; i < n; i++)
            {
                if (labels[i] != 0 || !StaticOpen(i, r)) continue;
                next++;
                labels[i] = next;
                int head = 0, tail = 0;
                q[tail++] = i;
                while (head < tail)
                {
                    int cur = q[head++];
                    int cx = cur % W, cy = cur / W;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = cx + DX[k], ny = cy + DY[k];
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int ni = ny * W + nx;
                        if (labels[ni] != 0 || !StaticOpen(ni, r)) continue;
                        if (k >= 4 && (!StaticOpen(cy * W + nx, r) || !StaticOpen(ny * W + cx, r))) continue;
                        labels[ni] = next;
                        q[tail++] = ni;
                    }
                }
            }
            componentCache[key] = labels;
            return labels;
        }

        /// <summary>True when b can be reached from a ignoring units (static connectivity only; O(1) after the first call per radius).</summary>
        public bool AreConnected(Vec2 a, Vec2 b, float radius)
        {
            int ia = CellIndexOrNeg(a), ib = CellIndexOrNeg(b);
            if (ia < 0 || ib < 0) return false;
            var labels = Components(radius);
            return labels[ia] != 0 && labels[ia] == labels[ib];
        }

        // ------------------------------------------------------------------ internals

        // Cell to start searching from: the start's cell when usable, else the nearest usable cell.
        int StartCell(Vec2 start, float r, out int startExact)
        {
            startExact = CellIndexOrNeg(start);
            if (startExact >= 0 && Open(startExact, r)) return startExact;
            var p = ClampToBounds(start);
            return NearestOpenCell(CellIndexClamped(p), p, r, Math.Max(Width, Height));
        }

        int AStar(int s, int goalCell, Vec2 goalPos, int allowCell, float r, bool rangeMode, Vec2 target, float stopR, bool los, out bool reached)
        {
            NextSearchGen();
            heapCount = 0;
            int gen = searchGen;
            int W = Width, H = Height;
            float cs = CellSize, diagCost = cs * Sqrt2;
            int gx = goalCell >= 0 ? goalCell % W : 0, gy = goalCell >= 0 ? goalCell / W : 0;
            Vec2 aim = rangeMode ? target : goalPos;

            gScore[s] = 0;
            parentCell[s] = -1;
            seenGen[s] = gen;
            HeapPush(s, Heuristic(s % W, s / W));

            int best = s;
            float bestD = float.MaxValue;
            int expanded = 0, maxNodes = Math.Max(1000, Options.MaxSearchNodes);
            reached = false;
            int result = -1;

            while (heapCount > 0)
            {
                int cur = HeapPop();
                if (closedGen[cur] == gen) continue;
                closedGen[cur] = gen;
                int cx = cur % W, cy = cur / W;
                var cc = new Vec2((cx + 0.5f) * cs, (cy + 0.5f) * cs);
                float d = (cc - aim).Length;
                if (rangeMode)
                {
                    if (d <= stopR && (!los || HasLineOfSight(cc, target))) { reached = true; result = cur; break; }
                }
                else if (cur == goalCell) { reached = true; result = cur; break; }
                if (d < bestD) { bestD = d; best = cur; }
                if (++expanded > maxNodes) break;

                float gc = gScore[cur];
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int ni = ny * W + nx;
                    if (closedGen[ni] == gen) continue;
                    if (ni != allowCell && !Open(ni, r)) continue;
                    bool diag = k >= 4;
                    if (diag)
                    {
                        int s1 = cy * W + nx, s2 = ny * W + cx;
                        if ((s1 != allowCell && !Open(s1, r)) || (s2 != allowCell && !Open(s2, r))) continue;
                    }
                    float ng = gc + (diag ? diagCost : cs);
                    if (seenGen[ni] != gen || ng < gScore[ni])
                    {
                        seenGen[ni] = gen;
                        gScore[ni] = ng;
                        parentCell[ni] = cur;
                        HeapPush(ni, ng + Heuristic(nx, ny) * HeuristicTieBreak);
                    }
                }
            }
            LastExpandedNodes = expanded;
            return reached ? result : best;

            float Heuristic(int x, int y)
            {
                if (rangeMode)
                {
                    float ddx = (x + 0.5f) * cs - target.x, ddy = (y + 0.5f) * cs - target.y;
                    float e = (float)Math.Sqrt(ddx * ddx + ddy * ddy) - stopR;
                    return e > 0 ? e : 0f;
                }
                if (goalCell < 0)
                {
                    float ddx = (x + 0.5f) * cs - goalPos.x, ddy = (y + 0.5f) * cs - goalPos.y;
                    return (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                }
                int ax = Math.Abs(x - gx), ay = Math.Abs(y - gy);
                int mn = ax < ay ? ax : ay, mx = ax < ay ? ay : ax;
                return (mx - mn) * cs + mn * diagCost;
            }
        }

        // rawPoints = start, centres of the cell path (skipping the start's own cell), optional exact end.
        void BuildRawPoints(Vec2 start, int s, int startExact, int end, Vec2? exactEnd)
        {
            cellPath.Clear();
            for (int c = end; c >= 0; c = parentCell[c])
            {
                cellPath.Add(c);
                if (c == s) break;
            }
            cellPath.Reverse();
            rawPoints.Clear();
            rawPoints.Add(start);
            for (int j = 0; j < cellPath.Count; j++)
            {
                if (j == 0 && cellPath[j] == startExact) continue;
                rawPoints.Add(CellCenter(cellPath[j]));
            }
            if (exactEnd.HasValue)
            {
                if (rawPoints.Count > 1) rawPoints[rawPoints.Count - 1] = exactEnd.Value;
                else rawPoints.Add(exactEnd.Value);
            }
            // drop zero-length steps
            for (int i = rawPoints.Count - 1; i >= 1; i--)
                if ((rawPoints[i] - rawPoints[i - 1]).SqrLength < 1e-8f) rawPoints.RemoveAt(i);
        }

        // Greedy line-of-walk smoothing (string pulling).
        void Smooth(List<Vec2> raw, List<Vec2> output, float r, int startSkipCell, int allowCell)
        {
            output.Clear();
            int n = raw.Count;
            if (n == 0) return;
            output.Add(raw[0]);
            if (n == 1) return;
            int anchor = 0;
            for (int i = 1; i < n - 1; i++)
            {
                if (!LineClear(raw[anchor], raw[i + 1], r, anchor == 0 ? startSkipCell : -1, allowCell))
                {
                    output.Add(raw[i]);
                    anchor = i;
                }
            }
            output.Add(raw[n - 1]);
        }

        // Cuts the polyline at the first point within R of target (and with LOS when required).
        bool CutAtRange(List<Vec2> pts, Vec2 target, float R, bool los)
        {
            if (pts.Count == 0) return false;
            if (pts.Count == 1) return (pts[0] - target).SqrLength <= R * R && (!los || HasLineOfSight(pts[0], target));
            float r2 = R * R;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                float t = -1f;
                if (!los)
                {
                    var d = b - a;
                    var f = a - target;
                    float A = Vec2.Dot(d, d);
                    float C = Vec2.Dot(f, f) - r2;
                    if (C <= 0) t = 0;
                    else if (A > 1e-12f)
                    {
                        float B = 2 * Vec2.Dot(f, d);
                        float disc = B * B - 4 * A * C;
                        if (disc >= 0)
                        {
                            float root = (-B - (float)Math.Sqrt(disc)) / (2 * A);
                            if (root >= 0 && root <= 1) t = root;
                        }
                    }
                }
                else
                {
                    float segLen = Vec2.Distance(a, b);
                    int steps = Math.Max(1, (int)Math.Ceiling(segLen / 0.1f));
                    for (int k = 0; k <= steps; k++)
                    {
                        float tt = (float)k / steps;
                        var q = Vec2.Lerp(a, b, tt);
                        if ((q - target).SqrLength <= r2 && HasLineOfSight(q, target)) { t = tt; break; }
                    }
                }
                if (t < 0) continue;
                var cut = Vec2.Lerp(a, b, t);
                pts.RemoveRange(i, pts.Count - i);
                if ((cut - a).SqrLength > 1e-10f) pts.Add(cut);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Finds up to `count` walkable spots near center, at least `spacing` apart from each other and from
        /// non-ignored units, all reachable from center (party placement at spawns, formations, combat start).
        /// Spots are appended to `into` nearest first; returns the number added.
        /// </summary>
        public int FindStandingSpots(Vec2 center, int count, float spacing, NavAgent agent, List<Vec2> into)
        {
            if (count <= 0 || into == null) return 0;
            PrepareUnits(agent);
            float r = agent.Radius;
            int s = StartCell(center, r, out _);
            if (s < 0) return 0;
            int firstNew = into.Count;
            int added = 0;
            float sp2 = spacing * spacing;
            NextSearchGen();
            int gen = searchGen;
            heapCount = 0;
            gScore[s] = 0;
            seenGen[s] = gen;
            HeapPush(s, 0);
            int W = Width, H = Height;
            float cs = CellSize, diagCost = cs * Sqrt2;
            int expanded = 0;
            bool centerOk = IsWalkable(center, agent);
            while (heapCount > 0 && added < count && expanded < 20000)
            {
                int cur = HeapPop();
                if (closedGen[cur] == gen) continue;
                closedGen[cur] = gen;
                expanded++;
                var pos = (expanded == 1 && centerOk) ? center : CellCenter(cur);
                bool ok = !UnitBlocks(pos, agent);
                for (int j = firstNew; ok && j < into.Count; j++)
                    if ((into[j] - pos).SqrLength < sp2) ok = false;
                if (ok) { into.Add(pos); added++; }
                int cx = cur % W, cy = cur / W;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int ni = ny * W + nx;
                    if (closedGen[ni] == gen || !StaticOpen(ni, r)) continue;
                    bool diag = k >= 4;
                    if (diag && (!StaticOpen(cy * W + nx, r) || !StaticOpen(ny * W + cx, r))) continue;
                    float ng = gScore[cur] + (diag ? diagCost : cs);
                    if (seenGen[ni] != gen || ng < gScore[ni])
                    {
                        seenGen[ni] = gen;
                        gScore[ni] = ng;
                        HeapPush(ni, ng);
                    }
                }
            }
            return added;
        }
    }
}
