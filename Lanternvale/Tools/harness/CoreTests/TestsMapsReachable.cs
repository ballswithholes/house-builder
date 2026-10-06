// Every map in the database, now and in the future (Docs/Expansion.md §7 world-core): with every flag set (hidden
// passages revealed, flag-gated props and chests in their final state), each spawn, transition, NPC, chest and encounter
// centre can be walked to from the "default" spawn. Hard failure for every map except the three maps of the original
// slice, which only warn (their layout predates this rule; TestsWorldContent covers them as before).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsMapsReachable
    {
        static readonly HashSet<string> LegacyMaps = new HashSet<string>(StringComparer.Ordinal) { "lanternvale", "whisperwood", "shrine" };

        /// <summary>"Every flag set": a positive term holds, a negated one ("!flag") does not ('&amp;' joins terms).</summary>
        public static bool AllFlagsSet(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return true;
            foreach (var part in expr.Split('&'))
            {
                var term = part.Trim();
                if (term.Length > 0 && term[0] == '!') return false;
            }
            return true;
        }

        /// <summary>What on the map cannot be reached from its default spawn (empty = all reachable).</summary>
        public static List<string> Unreachable(MapDef m)
        {
            var problems = new List<string>();
            var agent = NavAgent.Default;
            var nav = new NavGrid(m, null, AllFlagsSet);
            SpawnPointDef def = null;
            foreach (var s in m.spawns) if (s != null && s.id == "default") def = s;
            if (def == null) { problems.Add("no default spawn"); return problems; }
            var start = nav.ClampToWalkable(def.pos, agent);
            if (!nav.IsWalkable(start, agent) || Vec2.Distance(start, def.pos) > 1.0f)
            {
                problems.Add($"default spawn {def.pos} has no walkable ground within 1 m");
                return problems;
            }

            void Reach(string what, Vec2 p, float range)
            {
                var path = nav.FindPathToRange(start, p, range, agent);
                if (path.Status != PathStatus.Complete) problems.Add($"{what} at {p} cannot be reached from the default spawn");
            }

            foreach (var s in m.spawns)
                if (s != null) Reach($"spawn '{s.id}'", s.pos, 1.5f);
            foreach (var t in m.transitions)
                if (t != null) Reach($"transition '{t.id}'", t.pos, Math.Max(1f, Math.Min(MapRuntime.TransitionSize(t).x, MapRuntime.TransitionSize(t).y) * 0.5f + 0.5f));
            foreach (var n in m.npcs)
                if (n != null) Reach($"npc '{n.npc}'", n.pos, 2.5f);
            foreach (var c in m.chests)
                if (c != null) Reach($"chest '{c.id}'", c.pos, 2.0f);
            foreach (var e in m.encounters)
                if (e != null) Reach($"encounter '{e.id}'", e.pos, Math.Max(1.5f, Math.Min(e.radius, 3f)));
            return problems;
        }

        [Test]
        public static void EveryMap_EverythingReachableFromDefault()
        {
            var db = Harness.Db;
            if (db == null) return;
            var failures = new List<string>();
            foreach (var m in db.Maps.Values)
            {
                var problems = Unreachable(m);
                if (problems.Count == 0) continue;
                if (LegacyMaps.Contains(m.id))
                {
                    foreach (var p in problems) Console.WriteLine($"    WARN map {m.id}: {p}");
                    continue;
                }
                foreach (var p in problems) failures.Add($"map {m.id}: {p}");
            }
            Assert(failures.Count == 0, "unreachable map content:\n    " + string.Join("\n    ", failures));
        }

        [Test]
        public static void ReachabilityCheck_CatchesWalledOffContent()
        {
            // a map whose cellar spawn sits behind a stream with no crossing, and whose chest is boxed in by a flag-gated
            // wall that only appears once its flag is set (the "every flag set" world keeps it)
            var m = new MapDef { id = "xr_test", width = 30, depth = 20 };
            m.spawns.Add(new SpawnPointDef { id = "default", pos = new Vec2(4, 10) });
            m.spawns.Add(new SpawnPointDef { id = "from_beyond", pos = new Vec2(25, 10) });
            m.water.Add(new WaterDef { points = new List<Vec2> { new Vec2(15, -2), new Vec2(15, 22) } });
            var problems = Unreachable(m);
            Assert(problems.Count == 1 && problems[0].Contains("from_beyond"), "the spawn across the stream: " + string.Join("; ", problems));

            m.water[0].crossings.Add(new RectDef { pos = new Vec2(15, 10), size = new Vec2(4, 3) });
            Assert(Unreachable(m).Count == 0, "a ford makes it reachable");

            // a hidden passage counts as revealed; a hideFlag boulder in its doorway counts as moved
            m.props.Add(new PropDef { art = "prop_rock_large", pos = new Vec2(4, 15), collider = new ColliderDef { w = 30, h = 1 }, hideFlag = "boulders_moved" });
            m.transitions.Add(new TransitionDef { id = "to_secret", pos = new Vec2(4, 18), hidden = true, revealFlag = "found", targetMap = "xr_test", targetSpawn = "default" });
            Assert(Unreachable(m).Count == 0, "with every flag set the boulder is gone");
            m.props[0].hideFlag = "";
            m.props[0].requireFlag = "walls_up";
            var walled = Unreachable(m);
            Assert(walled.Count == 1 && walled[0].Contains("to_secret"), "a requireFlag wall stands in the every-flag world: " + string.Join("; ", walled));

            Assert(AllFlagsSet("") && AllFlagsSet("a") && AllFlagsSet("a&b") && !AllFlagsSet("!a") && !AllFlagsSet("a & !b"), "flag expressions");
        }
    }
}
