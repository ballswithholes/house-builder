// NavGrid tests: A*, smoothing, blocked goals, range stops, truncation, units, reach flood, map build, perf.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsWorldNav
    {
        static void AssertPathValid(NavGrid nav, NavPath p, NavAgent agent, string what)
        {
            Assert(p.Points.Count >= 1, what + ": path has points");
            for (int i = 1; i < p.Points.Count; i++)
            {
                Assert(nav.IsWalkable(p.Points[i], agent.Radius), $"{what}: point {i} {p.Points[i]} walkable");
                Assert(nav.LineWalkable(p.Points[i - 1], p.Points[i], agent), $"{what}: segment {i - 1}->{i} {p.Points[i - 1]}->{p.Points[i]} clear");
            }
            float len = 0;
            for (int i = 1; i < p.Points.Count; i++) len += Vec2.Distance(p.Points[i - 1], p.Points[i]);
            AssertNear(p.Length, len, 1e-3f, what + ": length matches points");
        }

        [Test]
        public static void OpenFieldStraightLine()
        {
            var nav = new NavGrid(40, 20);
            var agent = NavAgent.Default;
            var p = nav.FindPath(new Vec2(2, 2), new Vec2(38, 18), agent);
            Assert(p.Status == PathStatus.Complete && p.ReachedGoal, "complete");
            Assert(p.Points.Count == 2, $"smoothed to a straight line (got {p.Points.Count} points)");
            AssertNear(p.Length, Vec2.Distance(new Vec2(2, 2), new Vec2(38, 18)), 1e-3f, "length = euclid");
            Assert(p.End == new Vec2(38, 18), "ends exactly at the goal");
            AssertPathValid(nav, p, agent, "open");
        }

        [Test]
        public static void PathAroundWall()
        {
            var nav = new NavGrid(40, 20);
            nav.AddObstacleRect(new Vec2(19.5f, 0), new Vec2(20.5f, 15)); // wall with a gap at the back (y > 15)
            nav.Rebuild();
            var agent = NavAgent.Default;
            var a = new Vec2(10, 5);
            var b = new Vec2(30, 5);
            Assert(!nav.LineWalkable(a, b, agent), "straight line blocked");
            var p = nav.FindPath(a, b, agent);
            Assert(p.Status == PathStatus.Complete, "complete");
            Assert(p.Length > Vec2.Distance(a, b) + 8, $"detour is longer ({p.Length})");
            bool passesGap = false;
            foreach (var pt in p.Points) if (pt.y > 15) passesGap = true;
            Assert(passesGap, "goes through the gap");
            Assert(p.Points.Count <= 6, $"smoothed ({p.Points.Count} points)");
            AssertPathValid(nav, p, agent, "wall");
            // an agent too fat for the gap cannot pass: gap is 5 m minus margin, so radius 3 fails
            var fat = new NavAgent(3f);
            var pf = nav.FindPath(new Vec2(10, 8), new Vec2(30, 8), fat);
            Assert(pf.Status == PathStatus.Partial, "fat agent cannot pass");
        }

        [Test]
        public static void NoCornerCutting()
        {
            var nav = new NavGrid(10, 10, new NavGridOptions { EdgeMargin = 0 });
            // two blocks touching at a corner: (4..5, 4..5) and (5..6, 5..6) leave a diagonal pinch at (5,5)
            nav.AddObstacleRect(new Vec2(4.01f, 4.01f), new Vec2(4.99f, 4.99f));
            nav.AddObstacleRect(new Vec2(5.01f, 5.01f), new Vec2(5.99f, 5.99f));
            nav.Rebuild();
            var agent = new NavAgent(0f);
            Assert(!nav.LineWalkable(new Vec2(4.25f, 5.75f), new Vec2(5.75f, 4.25f), agent), "cannot squeeze through the touching corners");
            var p = nav.FindPath(new Vec2(4.25f, 5.75f), new Vec2(5.75f, 4.25f), agent);
            Assert(p.Status == PathStatus.Complete, "complete");
            Assert(p.Length > 2.5f, $"goes around the blocks ({p.Length})");
            AssertPathValid(nav, p, agent, "pinch");
        }

        [Test]
        public static void BlockedGoalGivesClosestPoint()
        {
            var nav = new NavGrid(40, 20);
            nav.AddObstacleEllipse(new Vec2(30, 10), 6, 4);
            nav.Rebuild();
            var agent = NavAgent.Default;
            var p = nav.FindPath(new Vec2(5, 10), new Vec2(30, 10), agent);
            Assert(p.Status == PathStatus.Partial, "partial");
            Assert(nav.IsWalkable(p.End, agent), "end walkable");
            float d = Vec2.Distance(p.End, new Vec2(30, 10));
            Assert(d < 4.0f && d > 2.5f, $"end next to the obstacle edge (d={d})");

            // enclosed pocket: unreachable goal inside a closed box
            var nav2 = new NavGrid(40, 20);
            nav2.AddObstacleRect(new Vec2(25, 5), new Vec2(35, 6));
            nav2.AddObstacleRect(new Vec2(25, 14), new Vec2(35, 15));
            nav2.AddObstacleRect(new Vec2(25, 5), new Vec2(26, 15));
            nav2.AddObstacleRect(new Vec2(34, 5), new Vec2(35, 15));
            nav2.Rebuild();
            var goal = new Vec2(30, 10);
            Assert(nav2.IsWalkable(goal, agent), "pocket interior is walkable");
            var p2 = nav2.FindPath(new Vec2(5, 10), goal, agent);
            Assert(p2.Status == PathStatus.Partial, "unreachable → partial");
            Assert(p2.End.x < 26 && p2.End.x > 23, $"stops at the outer wall ({p2.End})");
            AssertPathValid(nav2, p2, agent, "pocket");

            // start blocked too (inside an obstacle): still produces a path out
            var p3 = nav.FindPath(new Vec2(30, 10), new Vec2(5, 10), agent);
            Assert(p3.Status == PathStatus.Complete, "path out of an obstacle");
            Assert(p3.Points[0] == new Vec2(30, 10), "starts at the given start");
        }

        [Test]
        public static void RangeStop()
        {
            var nav = new NavGrid(40, 20);
            var agent = NavAgent.Default;
            var target = new Vec2(30, 10);
            var p = nav.FindPathToRange(new Vec2(5, 10), target, 2.2f, agent);
            Assert(p.Status == PathStatus.Complete, "complete");
            float d = Vec2.Distance(p.End, target);
            Assert(d <= 2.2f, $"within range (d={d})");
            Assert(d >= 2.2f - 0.1f, $"stops as soon as in range (d={d})");
            AssertNear(p.Length, 25f - d, 0.05f, "straight approach");

            // already in range
            var p2 = nav.FindPathToRange(new Vec2(29, 10), target, 2.2f, agent);
            Assert(p2.Status == PathStatus.Complete && p2.Points.Count == 1 && p2.Length == 0, "no movement when in range");

            // target unit registered: approach respects its circle, with or without ignoring it
            nav.SetUnit(7, target, 0.5f);
            var p3 = nav.FindPathToRange(new Vec2(5, 3), target, 2.2f, agent);
            Assert(p3.Status == PathStatus.Complete && Vec2.Distance(p3.End, target) <= 2.2f, "melee approach to a unit");
            var p4 = nav.FindPathToRange(new Vec2(5, 3), target, 2.2f, agent.Ignoring(7));
            Assert(p4.Status == PathStatus.Complete, "melee approach ignoring target");

            // truncated approach (movement budget)
            var p5 = nav.FindPathToRange(new Vec2(5, 10), target, 2.2f, agent, maxLength: 9f);
            Assert(p5.Truncated && Math.Abs(p5.Length - 9f) < 1e-3f, $"truncated to 9 m ({p5.Length})");
            Assert(p5.FullLength > 20f, "full length kept");
            Assert(!p5.ReachedGoal, "not reached when truncated");
        }

        [Test]
        public static void RangeWithLineOfSight()
        {
            var nav = new NavGrid(40, 20);
            nav.AddObstacleEllipse(new Vec2(20, 10), 4, 4);
            nav.AddLosBlocker(new Vec2(20, 10), 4, 4);
            nav.Rebuild();
            var agent = NavAgent.Default;
            var target = new Vec2(26, 10);
            var start = new Vec2(10, 10);
            Assert(!nav.HasLineOfSight(start, target), "blocked LOS");
            Assert(nav.HasLineOfSight(new Vec2(10, 1), new Vec2(10, 19)), "clear LOS elsewhere");
            var p = nav.FindPathToRange(start, target, 12f, agent, requireLineOfSight: true);
            Assert(p.Status == PathStatus.Complete, "complete");
            Assert(Vec2.Distance(p.End, target) <= 12f, "in range");
            Assert(nav.HasLineOfSight(p.End, target), "has LOS at the end");
            var pNoLos = nav.FindPathToRange(start, target, 12f, agent);
            AssertNear(pNoLos.Length, 4f, 0.1f, "without LOS: straight 4 m to get within 12 m");
            Assert(!nav.HasLineOfSight(pNoLos.End, target), "no LOS at the plain range stop");
            Assert(p.Length > pNoLos.Length + 0.5f, "LOS approach walks further");
        }

        [Test]
        public static void TruncateAndPointAt()
        {
            var p = new NavPath();
            p.Points.Add(new Vec2(0, 0));
            p.Points.Add(new Vec2(10, 0));
            p.Points.Add(new Vec2(10, 10));
            p.Status = PathStatus.Complete;
            p.RecomputeLength();
            AssertNear(p.Length, 20, 1e-4f, "length");
            Assert(p.PointAt(15) == new Vec2(10, 5), "point at 15");
            p.Truncate(12);
            Assert(p.Truncated && p.Points.Count == 3, "truncated");
            AssertNear(p.Length, 12, 1e-4f, "new length");
            Assert(p.End == new Vec2(10, 2), $"end {p.End}");
            Assert(!p.ReachedGoal, "not reached");
        }

        [Test]
        public static void UnitsAreAvoided()
        {
            var nav = new NavGrid(20, 10);
            // corridor: block everything except y in [4,6]
            nav.AddObstacleRect(new Vec2(5, 0), new Vec2(15, 4));
            nav.AddObstacleRect(new Vec2(5, 6), new Vec2(15, 10));
            nav.Rebuild();
            var agent = new NavAgent(0.3f, selfId: 1);
            var a = new Vec2(2, 5);
            var b = new Vec2(18, 5);
            var p = nav.FindPath(a, b, agent);
            Assert(p.Status == PathStatus.Complete, "corridor open");
            nav.SetUnit(1, a, 0.4f); // self: ignored
            nav.SetUnit(2, new Vec2(10, 5), 0.6f); // blocks the corridor
            Assert(nav.UnitCount == 2, "two units");
            var p2 = nav.FindPath(a, b, agent);
            Assert(p2.Status == PathStatus.Partial, "corridor blocked by unit 2");
            Assert(p2.End.x < 10, "stops before the blocker");
            var p3 = nav.FindPath(a, b, agent.Ignoring(2));
            Assert(p3.Status == PathStatus.Complete, "ignoring the blocker");
            Assert(nav.FindPath(a, b, NavAgent.ForUnit(0.3f, 1, 2)).Status == PathStatus.Complete, "ForUnit single ignore id");
            Assert(nav.FindPath(a, b, NavAgent.ForUnit(0.3f, 1, -1)).Status == PathStatus.Partial, "ForUnit -1 = no ignore");
            var p4 = nav.FindPath(a, b, agent.IgnoringAllUnits());
            Assert(p4.Status == PathStatus.Complete, "ignoring all units");
            Assert(!nav.IsWalkable(new Vec2(10, 5), agent), "unit position not walkable");
            Assert(nav.IsWalkable(new Vec2(10, 5), agent.Ignoring(2)), "walkable when ignored");
            Assert(nav.UnitAt(new Vec2(10.2f, 5), agent) == 2, "UnitAt");
            nav.SetUnit(2, new Vec2(3, 2), 0.6f); // move it out of the corridor
            Assert(nav.FindPath(a, b, agent).Status == PathStatus.Complete, "moved unit no longer blocks");
            Assert(nav.RemoveUnit(2) && !nav.RemoveUnit(2), "remove");
            nav.SetUnit(9, new Vec2(10, 5), 0.5f);
            nav.SetUnit(9, new Vec2(10, 5), 0f);
            Assert(!nav.TryGetUnit(9, out _, out _), "radius 0 removes the unit");
            Assert(nav.TryGetUnit(1, out var pos1, out var r1) && pos1 == a && Math.Abs(r1 - 0.4f) < 1e-6f, "TryGetUnit");
            nav.ClearUnits();
            Assert(nav.UnitCount == 0, "cleared");

            // open field: a unit in the way is walked around, not through
            var open = new NavGrid(20, 10);
            open.SetUnit(5, new Vec2(10, 5), 0.5f);
            var po = open.FindPath(new Vec2(2, 5), new Vec2(18, 5), NavAgent.Default);
            Assert(po.Status == PathStatus.Complete && po.Points.Count >= 3, "detour around the unit");
            foreach (var pt in po.Points) Assert(Vec2.Distance(pt, new Vec2(10, 5)) >= 0.5f + 0.35f - 0.05f, "vertices keep clear of the unit");
        }

        [Test]
        public static void ReachableFlood()
        {
            var nav = new NavGrid(40, 30);
            var agent = NavAgent.Default;
            var c = new Vec2(20, 15);
            var reach = nav.ReachableWithin(c, 9f, agent);
            Assert(reach.ReachableCount > 800, $"round area ({reach.ReachableCount} cells)");
            for (int deg = 0; deg < 360; deg += 15)
            {
                float a = deg * (float)Math.PI / 180f;
                var dir = new Vec2((float)Math.Cos(a), (float)Math.Sin(a));
                Assert(reach.CanReach(c + dir * 8.6f), $"8.6 m at {deg}° reachable");
                Assert(!reach.CanReach(c + dir * 9.5f), $"9.5 m at {deg}° not reachable");
                AssertNear(reach.DistanceTo(c + dir * 6f), 6f, 0.05f, $"near-euclidean distance at {deg}°");
            }
            var outline = new List<Vec2>();
            reach.GetOutline(outline);
            Assert(outline.Count > 0 && outline.Count % 2 == 0, "outline segment pairs");
            var border = new List<Vec2>();
            reach.GetBorderCells(border);
            Assert(border.Count > 20, "border cells");
            var cells = new List<Vec2>();
            reach.GetReachableCells(cells);
            Assert(cells.Count == reach.ReachableCount, "reachable cells listed");

            // path from the flood tree
            var target = c + new Vec2(5, 5);
            var pth = reach.PathTo(target);
            Assert(pth.Status == PathStatus.Complete && pth.Length <= 9f && pth.End == target, "PathTo");
            AssertNear(pth.Length, Vec2.Distance(c, target), 0.1f, "PathTo straight");

            // a wall forces a detour: a point 4 m away behind the wall is out of a 9 m budget
            var nav2 = new NavGrid(40, 30);
            nav2.AddObstacleRect(new Vec2(21, 5), new Vec2(22, 25));
            nav2.Rebuild();
            var reach2 = nav2.ReachableWithin(c, 9f, agent);
            Assert(!reach2.CanReach(new Vec2(24, 15)), "behind the wall is too far");
            Assert(reach2.CanReach(new Vec2(20, 22)), "same side reachable");
            var full = nav2.FindPath(c, new Vec2(24, 15), agent);
            Assert(full.Length > 9f, "detour longer than budget");
            var pathOk = reach2.PathTo(new Vec2(20, 22));
            AssertPathValid(nav2, pathOk, agent, "flood path");

            // reuse buffers
            var again = nav2.ReachableWithin(new Vec2(5, 5), 4f, agent, reach2);
            Assert(ReferenceEquals(again, reach2) && again.CanReach(new Vec2(7, 7)) && !again.CanReach(c), "reused ReachMap");

            // units block the flood
            var nav3 = new NavGrid(40, 30);
            nav3.SetUnit(3, c + new Vec2(2, 0), 0.5f);
            var reach3 = nav3.ReachableWithin(c, 9f, agent);
            Assert(!reach3.CanReach(c + new Vec2(2, 0)), "unit cell not reachable");
            Assert(reach3.CanReach(c + new Vec2(4, 0)), "behind the unit reachable via detour");
        }

        [Test]
        public static void BuildFromMapDef()
        {
            var m = WorldFixtures.Map();
            m.walkable.AddRange(new[] { new Vec2(0, 0), new Vec2(40, 0), new Vec2(40, 14), new Vec2(0, 14) });
            var nav = new NavGrid(m);
            Assert(nav.Width == 80 && nav.Height == 32, $"cells {nav.Width}x{nav.Height}");
            Assert(!nav.IsWalkable(new Vec2(10, 8), 0f), "tree trunk blocked");
            Assert(!nav.IsWalkable(new Vec2(11.6f, 8), 0f), "tree ellipse scaled (2.4*1.5/2 = 1.8)");
            Assert(nav.IsWalkable(new Vec2(12.2f, 8), 0f), "outside scaled tree ellipse");
            Assert(!nav.IsWalkable(new Vec2(20, 4), 0f), "tiny lamp collider still blocks its cell");
            Assert(nav.IsWalkable(new Vec2(5, 5), 0f), "grass has no collider");
            Assert(!nav.IsWalkable(new Vec2(29, 10), 0f), "flipped rock offset mirrored to x=29");
            Assert(nav.IsWalkable(new Vec2(31, 10), 0f), "unflipped offset position free");
            Assert(!nav.IsWalkable(new Vec2(25, 12), 0f), "chest footprint");
            Assert(!nav.IsWalkable(new Vec2(20, 15), 0f), "outside walkable polygon");
            Assert(!nav.IsWalkable(new Vec2(0.3f, 8), NavAgent.DefaultRadius), "agents keep their radius inside the ground edge");
            Assert(nav.IsWalkable(new Vec2(1.1f, 8), NavAgent.DefaultRadius), "near the edge is fine");
            Assert(!nav.HasLineOfSight(new Vec2(6, 8), new Vec2(14, 8)), "big tree (3.6 m) blocks LOS");
            Assert(nav.HasLineOfSight(new Vec2(26, 10), new Vec2(34, 10)), "small rock does not block LOS");
            var noLos = new NavGrid(m, new NavGridOptions { LineOfSight = false });
            Assert(noLos.HasLineOfSight(new Vec2(6, 8), new Vec2(14, 8)), "LOS disabled");
            var p = nav.FindPath(new Vec2(3, 8), new Vec2(37, 8), NavAgent.Default);
            Assert(p.Status == PathStatus.Complete, "cross the map");
            AssertPathValid(nav, p, NavAgent.Default, "map");
            Assert(nav.Clearance(0, 0) <= 0.25f + 1e-4f && nav.Clearance(40, 16) > 1f, $"clearance field ({nav.Clearance(0, 0)}, {nav.Clearance(40, 16)})");
        }

        [Test]
        public static void PointHelpers()
        {
            var nav = new NavGrid(30, 20);
            nav.AddObstacleEllipse(new Vec2(15, 10), 6, 6);
            nav.Rebuild();
            var agent = NavAgent.Default;
            var cl = nav.ClampToWalkable(new Vec2(15, 10), agent);
            Assert(nav.IsWalkable(cl, agent), "clamped point walkable");
            Assert(Vec2.Distance(cl, new Vec2(15, 10)) < 4f, "clamped near the obstacle");
            Assert(nav.ClampToWalkable(new Vec2(3, 3), agent) == new Vec2(3, 3), "walkable point unchanged");
            var outside = nav.ClampToWalkable(new Vec2(-5, 50), agent);
            Assert(nav.IsWalkable(outside, agent), "out of bounds clamped in");

            var rng = new Rng(42);
            for (int i = 0; i < 20; i++)
            {
                Assert(nav.RandomWalkablePointNear(new Vec2(5, 5), 4f, rng, agent, out var q), "random point found");
                Assert(nav.IsWalkable(q, agent) && Vec2.Distance(q, new Vec2(5, 5)) <= 4f + 1e-3f, "random point valid");
            }
            Assert(nav.FleePoint(new Vec2(8, 10), new Vec2(10, 10), 5f, rng, agent, out var f), "flee point");
            Assert(f.x < 8f && nav.IsWalkable(f, agent), $"flees away ({f})");
            // fleeing into a wall turns aside
            Assert(nav.FleePoint(new Vec2(1, 10), new Vec2(3, 10), 5f, rng, agent, out var f2) && nav.IsWalkable(f2, agent), "flee along the wall");

            var spots = new List<Vec2>();
            int n = nav.FindStandingSpots(new Vec2(5, 5), 4, 1.2f, agent, spots);
            Assert(n == 4 && spots.Count == 4, "four spots");
            Assert(spots[0] == new Vec2(5, 5), "first spot is the centre");
            for (int i = 0; i < spots.Count; i++)
            {
                Assert(nav.IsWalkable(spots[i], agent), "spot walkable");
                for (int j = i + 1; j < spots.Count; j++) Assert(Vec2.Distance(spots[i], spots[j]) >= 1.2f - 1e-3f, "spots spaced");
            }
        }

        [Test]
        public static void Performance()
        {
            // 200 x 60 m map (400 x 120 cells) with 300 random obstacles
            var rng = new Rng(7);
            var m = new MapDef { id = "perf", width = 200, depth = 60 };
            for (int i = 0; i < 300; i++)
                m.props.Add(new PropDef { pos = new Vec2(rng.Range(5f, 195f), rng.Range(3f, 57f)), collider = new ColliderDef { w = rng.Range(0.5f, 4f), h = rng.Range(0.4f, 2.5f) } });
            var sw = Stopwatch.StartNew();
            var nav = new NavGrid(m);
            double buildMs = sw.Elapsed.TotalMilliseconds;
            var agent = NavAgent.Default;
            for (int i = 0; i < 12; i++) nav.SetUnit(100 + i, new Vec2(rng.Range(10f, 190f), rng.Range(5f, 55f)), 0.45f);
            var path = new NavPath();
            // warm up
            nav.FindPath(new Vec2(2, 30), new Vec2(198, 30), agent, result: path);
            int runs = 60, complete = 0;
            double worst = 0, total = 0;
            int maxExpanded = 0;
            for (int i = 0; i < runs; i++)
            {
                var a = nav.ClampToWalkable(new Vec2(rng.Range(1f, 20f), rng.Range(1f, 59f)), agent);
                var b = nav.ClampToWalkable(new Vec2(rng.Range(180f, 199f), rng.Range(1f, 59f)), agent);
                var t0 = sw.Elapsed.TotalMilliseconds;
                nav.FindPath(a, b, agent, result: path);
                var dt = sw.Elapsed.TotalMilliseconds - t0;
                total += dt;
                worst = Math.Max(worst, dt);
                maxExpanded = Math.Max(maxExpanded, nav.LastExpandedNodes);
                if (path.Status == PathStatus.Complete) complete++;
            }
            // unreachable goal (enclosed) = full flood of the component
            var nav2 = new NavGrid(200, 60);
            nav2.AddObstacleRect(new Vec2(150, 20), new Vec2(170, 21));
            nav2.AddObstacleRect(new Vec2(150, 40), new Vec2(170, 41));
            nav2.AddObstacleRect(new Vec2(150, 20), new Vec2(151, 41));
            nav2.AddObstacleRect(new Vec2(169, 20), new Vec2(170, 41));
            nav2.Rebuild();
            nav2.FindPath(new Vec2(2, 2), new Vec2(160, 30), agent, result: path);
            var t1 = sw.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 10; i++) nav2.FindPath(new Vec2(2, 2 + i), new Vec2(160, 30), agent, result: path);
            double floodMs = (sw.Elapsed.TotalMilliseconds - t1) / 10;
            Assert(path.Status == PathStatus.Partial, "enclosed goal partial");
            Assert(path.End.x < 151 && path.End.x > 148, $"stops outside the enclosure ({path.End})");
            var reach = nav.ReachableWithin(new Vec2(100, 30), 9f, agent);
            var t2 = sw.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 20; i++) nav.ReachableWithin(new Vec2(60 + i * 3, 30), 9f, agent, reach);
            double reachMs = (sw.Elapsed.TotalMilliseconds - t2) / 20;
            Console.WriteLine($"    nav perf: build {buildMs:0.0} ms, path avg {total / runs:0.00} ms, worst {worst:0.00} ms " +
                              $"(max {maxExpanded} nodes, {complete}/{runs} complete), unreachable flood {floodMs:0.00} ms, reach 9 m {reachMs:0.00} ms ({reach.ReachableCount} cells)");
            Assert(complete >= runs * 0.9, "most random cross-map paths complete");
            Assert(total / runs < 10, "average path time well under budget");
            Assert(floodMs < 20 && reachMs < 20, "unreachable goal and reach flood stay cheap");
        }
    }
}
