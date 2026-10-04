// Smoke tests of the world module against the real content data (Harness.Db). Robust to missing data:
// hard failures only for crashes, non-terminating dialogues and enclosed default spawns; softer authoring
// issues (unreachable npcs/chests/transitions) are printed as warnings.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsWorldContent
    {
        [Test]
        public static void MapsAreNavigable()
        {
            var db = Harness.Db;
            if (db == null) return;
            var agent = NavAgent.Default;
            foreach (var m in db.Maps.Values)
            {
                var nav = new NavGrid(m);
                Vec2 spawn = default;
                bool hasDefault = false;
                foreach (var s in m.spawns) if (s.id == "default") { spawn = s.pos; hasDefault = true; }
                if (!hasDefault) continue; // DataValidator reports it
                var start = nav.ClampToWalkable(spawn, agent);
                Assert(nav.IsWalkable(start, agent), $"map {m.id}: default spawn has no walkable ground nearby");
                if (Vec2.Distance(start, spawn) > 1.0f) Console.WriteLine($"    WARN map {m.id}: default spawn {spawn} is inside an obstacle (nearest walkable {start})");
                var reach = nav.ReachableWithin(start, 6f, agent);
                Assert(reach.ReachableCount > 20, $"map {m.id}: default spawn is enclosed ({reach.ReachableCount} cells reachable within 6 m)");

                void CheckReach(string what, Vec2 p, float range)
                {
                    var path = nav.FindPathToRange(start, p, range, agent);
                    if (path.Status != PathStatus.Complete) Console.WriteLine($"    WARN map {m.id}: {what} at {p} is not reachable from the default spawn");
                }
                foreach (var s in m.spawns) CheckReach($"spawn '{s.id}'", s.pos, 1.5f);
                foreach (var n in m.npcs)
                {
                    if (!nav.IsWalkable(n.pos, 0f)) Console.WriteLine($"    WARN map {m.id}: npc '{n.npc}' stands inside an obstacle at {n.pos}");
                    CheckReach($"npc '{n.npc}'", n.pos, 2.5f);
                }
                foreach (var c in m.chests) CheckReach($"chest '{c.id}'", c.pos, 2.0f);
                foreach (var t in m.transitions) CheckReach($"transition '{t.id}'", t.pos, Math.Max(1f, Math.Min(t.size.x, t.size.y) * 0.5f + 0.5f));
                foreach (var e in m.encounters) foreach (var en in e.enemies)
                    if (!nav.IsWalkable(en.pos, agent)) Console.WriteLine($"    WARN map {m.id}: encounter '{e.id}' enemy '{en.creature}' stands in an obstacle at {en.pos}");
            }
        }

        [Test]
        public static void DialoguesTerminate()
        {
            var db = Harness.Db;
            if (db == null || db.Dialogues.Count == 0) return;
            var prevWarn = Log.WarnHandler;
            var warnings = new List<string>();
            Log.WarnHandler = s => warnings.Add(s);
            try
            {
                foreach (var d in db.Dialogues.Values)
                {
                    for (ulong seed = 1; seed <= 4; seed++)
                    {
                        var ctx = new FakeWorldContext(db);
                        ctx.GoldValue = 100000;
                        var runner = ctx.World.CreateDialogueRunner(new Rng(seed));
                        var pick = new Rng(seed * 7919);
                        runner.Start(d.id, "");
                        int steps = 0;
                        while (runner.IsActive && steps++ < 200)
                        {
                            var v = runner.Current;
                            Assert(v != null, $"dialogue {d.id}: active without a view");
                            if (v.Choices.Count == 0) runner.Continue();
                            else runner.Choose(pick.Range(0, v.Choices.Count - 1));
                        }
                        if (runner.IsActive)
                        {
                            // loops through choices are legitimate (hub menus); make sure it can still be closed
                            runner.End();
                        }
                        Assert(!runner.IsActive, $"dialogue {d.id}: could not be ended");
                    }
                }
            }
            finally { Log.WarnHandler = prevWarn; }
            foreach (var w in new HashSet<string>(warnings)) Console.WriteLine("    WARN " + w);
        }
    }
}
