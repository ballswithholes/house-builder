// Hidden things (Docs/Expansion.md §2.2, §7 world-core): passive region checks (once per save, best party member,
// requireFlag), SecretFound once per hidden passage whatever reveals it, hidden transitions that only work once revealed
// (and never whisk the party away from under its feet), props that start dialogues, flag-gated props and chests in the
// navigation grid (same instance, Version++, deferred during combat) and blocking water with fords.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsWorldHiddenThings
    {
        /// <summary>Test maps loaded on top of the real data: a vale with a hidden cellar, a ford map and a dialogue.</summary>
        const string Bundle = @"{
  ""maps"": [
    {""id"": ""xw_vale"", ""name"": ""Test Vale"", ""width"": 40, ""depth"": 30,
     ""spawns"": [{""id"": ""default"", ""pos"": [4, 4]}, {""id"": ""from_xw_cellar"", ""pos"": [30, 21.5]}],
     ""transitions"": [
       {""id"": ""to_xw_cellar"", ""pos"": [30, 25], ""size"": [2, 2], ""targetMap"": ""xw_cellar"", ""targetSpawn"": ""from_xw_vale"",
        ""label"": ""The Old Cellar"", ""hidden"": true, ""revealFlag"": ""xw_found_cellar"", ""marker"": ""door""},
       {""id"": ""to_xw_attic"", ""pos"": [36, 4], ""size"": [2, 2], ""targetMap"": ""xw_cellar"", ""targetSpawn"": ""from_xw_vale"",
        ""hidden"": true, ""revealFlag"": ""xw_found_attic""}
     ],
     ""regions"": [
       {""id"": ""reg_xw_hollow"", ""pos"": [30, 19], ""size"": [10, 7], ""check"": {""skill"": ""Perception"", ""dc"": 12},
        ""checkFlag"": ""xw_found_cellar"", ""successText"": ""A cold draught rises from the brambles."", ""failText"": ""Only brambles here.""},
       {""id"": ""reg_xw_gated"", ""pos"": [10, 24], ""size"": [6, 6], ""check"": {""skill"": ""Investigation"", ""dc"": 10},
        ""checkFlag"": ""xw_gated_found"", ""requireFlag"": ""xw_permit"", ""successText"": ""Tracks!"", ""failText"": ""No tracks.""}
     ],
     ""props"": [
       {""art"": ""prop_rock_large"", ""pos"": [10, 10], ""collider"": {""w"": 2, ""h"": 1.2}, ""interact"": ""xw_stone"",
        ""dialogue"": ""dlg_xw_stone"", ""text"": ""An old carved stone.""},
       {""art"": ""prop_signpost"", ""pos"": [6, 7], ""interact"": ""xw_sign"", ""text"": ""West: the Vale.""},
       {""art"": ""prop_rock_large"", ""pos"": [20, 5], ""collider"": {""w"": 3, ""h"": 3}, ""hideFlag"": ""xw_boulder_moved"",
        ""interact"": ""xw_boulder"", ""text"": ""A boulder.""},
       {""art"": ""prop_crate"", ""pos"": [15, 15], ""collider"": {""w"": 1.5, ""h"": 1.5}, ""requireFlag"": ""xw_crates"",
        ""interact"": ""xw_crate"", ""text"": ""Crates of lamp oil.""},
       {""art"": ""prop_rock_large"", ""pos"": [3, 26], ""scale"": 3, ""collider"": {""w"": 2, ""h"": 2}}
     ],
     ""chests"": [{""id"": ""chest_xw_cache"", ""pos"": [25, 5], ""gold"": 5, ""requireFlag"": ""xw_cache_seen""}],
     ""encounters"": [{""id"": ""enc_xw_pup"", ""pos"": [36, 14], ""radius"": 1.5,
                      ""enemies"": [{""creature"": ""cr_wolf"", ""pos"": [36.5, 14], ""level"": 1}]}]
    },
    {""id"": ""xw_cellar"", ""name"": ""Test Cellar"", ""width"": 20, ""depth"": 16, ""environment"": ""cave"", ""dungeon"": true,
     ""spawns"": [{""id"": ""default"", ""pos"": [3.5, 8]}, {""id"": ""from_xw_vale"", ""pos"": [3.5, 8]}],
     ""transitions"": [{""id"": ""to_xw_vale"", ""pos"": [0.7, 8], ""size"": [1.4, 6], ""targetMap"": ""xw_vale"", ""targetSpawn"": ""from_xw_cellar""}]
    },
    {""id"": ""xw_ford"", ""name"": ""Test Ford"", ""width"": 30, ""depth"": 20,
     ""spawns"": [{""id"": ""default"", ""pos"": [5, 4]}],
     ""water"": [
       {""points"": [[15, -2], [15, 22]], ""halfWidth"": 1.5, ""crossings"": [{""pos"": [15, 10], ""size"": [5, 3]}]},
       {""points"": [[22, 14], [27, 14], [27, 18], [22, 18]], ""closed"": true},
       {""points"": [[2, 16], [10, 16]], ""halfWidth"": 1, ""blocksMovement"": false}
     ]
    }
  ],
  ""dialogues"": [
    {""id"": ""dlg_xw_stone"", ""start"": ""a"", ""nodes"": [
      {""id"": ""a"", ""speaker"": ""narrator"", ""text"": ""Runes spiral across the stone. One is worn smooth by thumbs."", ""choices"": [
        {""text"": ""Press the worn rune."", ""outcomes"": [{""type"": ""SetFlag"", ""key"": ""xw_found_cellar""}]},
        {""text"": ""Leave it be.""}
      ]}
    ]}
  ]
}";

        static GameDatabase db;

        static GameDatabase TestDb()
        {
            if (db != null) return db;
            var files = ReadDataFiles(DataDir).ToList();
            files.Add(new KeyValuePair<string, string>("zz_world_hidden_things.json", Bundle));
            db = GameDatabase.Load(files);
            Assert(db.Problems.Count == 0, "the test bundle parses: " + string.Join("; ", db.Problems));
            var problems = DataValidator.Validate(db).Where(p => p.Contains("xw")).ToList();
            Assert(problems.Count == 0, "the test bundle is valid: " + string.Join("; ", problems));
            return db;
        }

        /// <summary>A level-12 rogue party on the test vale. bonus is added to every skill check (±100 = only natural 1/20 matter).</summary>
        static GameSession NewVale(ulong seed, int bonus = 0)
        {
            var s = new GameSession(TestDb(), seed);
            s.NewGame(new NewGameOptions { Name = "Tester", Class = ClassId.Rogue, StartLevel = 12, PlayOpening = false });
            if (bonus != 0) s.ExtraSkillCheckBonus = (id, skill) => bonus;
            s.EnterMap("xw_vale", "default");
            Assert(s.MapId == "xw_vale" && s.IsExploring, "on the test vale");
            s.TakeEvents();
            return s;
        }

        static readonly Vec2 HollowSpot = new Vec2(30f, 19f);    // inside reg_xw_hollow, outside the cellar door
        static readonly Vec2 OutsideHollow = new Vec2(18f, 19f);

        /// <summary>Walks into reg_xw_hollow on fresh vales (seeds 1, 2, …) until the region's roll comes out as wanted.</summary>
        static GameSession RollHollow(bool wantSuccess, out CheckResult roll, out List<SessionEvent> events)
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var s = NewVale(seed, wantSuccess ? 100 : -100);
                s.MoveLeader(HollowSpot);
                events = s.TakeEvents();
                var ev = SessionTest.FindEvent(events, SessionEventKind.SkillCheck, "reg_xw_hollow");
                Assert(ev?.Check != null, "entering the hollow rolls its Perception check");
                if (ev.Check.Success != wantSuccess) continue;
                roll = ev.Check;
                return s;
            }
            throw new Exception("no seed gave the wanted roll");
        }

        // ------------------------------------------------------------------ region checks

        [Test]
        public static void RegionCheck_Success_RevealsThePassageOnce()
        {
            var s = RollHollow(true, out var roll, out var events);
            Assert(roll.Skill == SkillCheck.Perception && roll.Dc == 12, "the region's skill and DC");
            Assert(s.Party.Any(u => s.MemberId(u) == roll.RollerId), "rolled by a party member");
            Assert(s.Flags.IsSet("xw_found_cellar"), "success sets the checkFlag");
            Assert(s.Map.State.checkedRegions.Contains("reg_xw_hollow"), "marked checked");
            Assert(events.Any(e => e.Kind == SessionEventKind.Toast && e.Text == "A cold draught rises from the brambles."), "the success text is toasted");
            Assert(!events.Any(e => e.Kind == SessionEventKind.Toast && e.Text == "Only brambles here."), "not the failure text");
            var found = events.Where(e => e.Kind == SessionEventKind.SecretFound).ToList();
            Assert(found.Count == 1, $"one SecretFound ({found.Count})");
            Assert(found[0].Id == "xw_found_cellar" && found[0].Id2 == "to_xw_cellar" && found[0].Text == "You discovered a hidden passage: The Old Cellar",
                $"SecretFound: Id = the flag, Id2 = the transition, banner text ('{found[0].Text}')");
            int iCheck = events.FindIndex(e => e.Kind == SessionEventKind.SkillCheck);
            int iSecret = events.FindIndex(e => e.Kind == SessionEventKind.SecretFound);
            Assert(iCheck >= 0 && iCheck < iSecret, "the roll is shown before the discovery banner");

            // once per save: leaving and coming back neither rolls nor announces again
            s.MoveLeader(OutsideHollow);
            s.MoveLeader(HollowSpot);
            var again = s.TakeEvents();
            Assert(SessionTest.CountEvents(again, SessionEventKind.SkillCheck) == 0, "no second roll");
            Assert(SessionTest.CountEvents(again, SessionEventKind.SecretFound) == 0, "no second banner");

            // the revealed passage works: TransitionAt sees it, walking in travels, and coming back does not re-announce
            var door = s.Map.FindTransition("to_xw_cellar");
            Assert(s.Map.TransitionAt(door.pos) == door, "TransitionAt finds the revealed door");
            var r = s.MoveLeader(door.pos);
            Assert(r.Trigger.Kind == TriggerKind.Travel && s.MapId == "xw_cellar", $"walking into it travels ({r.Trigger})");
            var u = s.UseTransition("to_xw_vale");
            Assert(u.Ok && s.MapId == "xw_vale" && Vec2.Distance(s.Leader.Position, new Vec2(30f, 21.5f)) < 3f, "back beside the door");
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.SecretFound) == 0, "travel does not re-announce");
            Assert(s.UseTransition("to_xw_cellar").Ok && s.MapId == "xw_cellar", "UseTransition works once revealed");
        }

        [Test]
        public static void RegionCheck_Failure_IsFinal_ButOtherWaysStillReveal()
        {
            var s = RollHollow(false, out var roll, out var events);
            Assert(!s.Flags.IsSet("xw_found_cellar") && s.Map.State.checkedRegions.Contains("reg_xw_hollow"), "failure: no flag, but checked");
            Assert(events.Any(e => e.Kind == SessionEventKind.Toast && e.Text == "Only brambles here."), "the failure text is toasted");
            Assert(SessionTest.CountEvents(events, SessionEventKind.SecretFound) == 0, "nothing found");
            var door = s.Map.FindTransition("to_xw_cellar");
            Assert(s.Map.TransitionAt(door.pos) == null, "the door stays hidden");
            var r = s.MoveLeader(door.pos);
            Assert(s.MapId == "xw_vale" && r.Trigger.Kind != TriggerKind.Travel, "walking over the hidden door does nothing");
            Assert(!s.UseTransition("to_xw_cellar").Ok && s.MapId == "xw_vale", "UseTransition refuses a hidden door");
            s.MoveLeader(OutsideHollow);
            s.MoveLeader(HollowSpot);
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.SkillCheck) == 0, "no re-roll after a failure");

            // an NPC hint (any SetFlag) still reveals it, announced once
            s.Flags.Set("xw_found_cellar", 1);
            var ev = s.TakeEvents();
            Assert(SessionTest.CountEvents(ev, SessionEventKind.SecretFound) == 1, "the hint is announced");
            Assert(s.Map.TransitionAt(door.pos) == door && s.UseTransition("to_xw_cellar").Ok && s.MapId == "xw_cellar", "and the door works");
        }

        [Test]
        public static void RegionCheck_WaitsForRequireFlag_AndSkipsWhenAlreadyFound()
        {
            var s = NewVale(3, 100);
            var gated = new Vec2(10f, 24f);
            s.MoveLeader(gated);
            var ev = s.TakeEvents();
            Assert(SessionTest.FindEvent(ev, SessionEventKind.SkillCheck, "reg_xw_gated") == null, "no roll while requireFlag fails");
            Assert(!s.Map.State.checkedRegions.Contains("reg_xw_gated"), "not marked checked either");
            Assert(s.Map.HasEnteredRegion("reg_xw_gated"), "the region itself was entered");

            // the flag turns true while the leader stands inside: the next position update rolls
            s.Flags.Set("xw_permit", 1);
            s.MoveLeader(gated + new Vec2(0.5f, 0f));
            ev = s.TakeEvents();
            var check = SessionTest.FindEvent(ev, SessionEventKind.SkillCheck, "reg_xw_gated");
            Assert(check?.Check != null && check.Check.Skill == SkillCheck.Investigation && check.Check.Dc == 10, "rolled once the requireFlag holds");
            Assert(s.Map.State.checkedRegions.Contains("reg_xw_gated"), "checked");
            Assert(s.Flags.IsSet("xw_gated_found") == check.Check.Success, "the checkFlag follows the roll");

            // found another way first (a dialogue hint): entering marks the region checked without a roll
            var t = NewVale(4, 100);
            t.Flags.Set("xw_found_cellar", 1);
            t.TakeEvents();
            t.MoveLeader(HollowSpot);
            ev = t.TakeEvents();
            Assert(SessionTest.CountEvents(ev, SessionEventKind.SkillCheck) == 0, "no pointless roll");
            Assert(t.Map.State.checkedRegions.Contains("reg_xw_hollow"), "marked checked");
            Assert(!ev.Any(e => e.Kind == SessionEventKind.Toast && (e.Text == "A cold draught rises from the brambles." || e.Text == "Only brambles here.")), "no check text");
        }

        [Test]
        public static void RegionCheck_SurvivesSaveAndLoad()
        {
            // a save made before the roll: the check is still due after loading
            var s = NewVale(5, 100);
            var before = s.SaveGame();
            var a = new GameSession(TestDb(), 99);
            Assert(a.LoadGame(before, out var err), "load: " + err);
            a.ExtraSkillCheckBonus = (id, skill) => 100;
            a.TakeEvents();
            a.MoveLeader(HollowSpot);
            Assert(SessionTest.FindEvent(a.TakeEvents(), SessionEventKind.SkillCheck, "reg_xw_hollow") != null, "rolled after loading an earlier save");

            // a save made after the roll keeps it rolled, and loading does not re-announce the found passage
            var won = RollHollow(true, out _, out _);
            var after = won.SaveGame();
            Assert(after.Contains("reg_xw_hollow"), "checkedRegions is saved");
            var b = new GameSession(TestDb(), 98);
            Assert(b.LoadGame(after, out err), "load: " + err);
            var loadEvents = b.TakeEvents();
            Assert(SessionTest.CountEvents(loadEvents, SessionEventKind.SecretFound) == 0, "loading does not re-announce");
            Assert(b.SaveGame() == after, "re-saved identically");
            Assert(b.Map.State.checkedRegions.Contains("reg_xw_hollow") && b.Flags.IsSet("xw_found_cellar"), "restored");
            Assert(b.Map.TransitionAt(b.Map.FindTransition("to_xw_cellar").pos) != null, "the door is still revealed");
            b.MoveLeader(OutsideHollow);
            b.MoveLeader(HollowSpot);
            var ev = b.TakeEvents();
            Assert(SessionTest.CountEvents(ev, SessionEventKind.SkillCheck) == 0 && SessionTest.CountEvents(ev, SessionEventKind.SecretFound) == 0,
                "no roll and no banner after loading");
        }

        // ------------------------------------------------------------------ SecretFound from any source

        [Test]
        public static void SecretFound_OncePerPassage_FromAnySource()
        {
            // a flag set anywhere (here: on another map) announces the passage with its label
            var s = NewVale(6);
            s.EnterMap("lanternvale", "default");
            s.TakeEvents();
            s.Flags.Set("xw_found_cellar", 1);
            var ev = s.TakeEvents();
            Assert(ev.Count(e => e.Kind == SessionEventKind.SecretFound && e.Id2 == "to_xw_cellar") == 1, "announced while elsewhere");
            // un-set and set again: still only once
            s.Flags.Clear("xw_found_cellar");
            s.Flags.Set("xw_found_cellar", 1);
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.SecretFound) == 0, "never twice");
            // a passage without a label is named after its target map
            s.Flags.Set("xw_found_attic", 1);
            var attic = SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.SecretFound, "xw_found_attic");
            Assert(attic != null && attic.Text == "You discovered a hidden passage: Test Cellar", "label falls back to the target map's name");
            Assert(s.IsPassageRevealed("xw_vale", "to_xw_attic") && !s.IsPassageRevealed("xw_vale", "to_xw_vale_nope"), "IsPassageRevealed");

            // the real data: Lanternvale's Root Hollows
            var lv = SessionTest.NewGame(ClassId.Hunter, 12, seed: 8);
            lv.TakeEvents();
            lv.Flags.Set("found_root_hollows", 1);
            var rh = SessionTest.FindEvent(lv.TakeEvents(), SessionEventKind.SecretFound, "found_root_hollows");
            var t = Db.Maps["lanternvale"].transitions.First(x => x.id == "to_dgn_root_hollows");
            string label = !string.IsNullOrEmpty(t.label) ? t.label : Db.Maps[t.targetMap].name;
            Assert(rh != null && rh.Text == GameSession.SecretFoundPrefix + label, $"Root Hollows announced ('{rh?.Text}')");

            // a new game starts over: the same passage is news again
            lv.NewGame(new NewGameOptions { Name = "Again", Class = ClassId.Hunter, StartLevel = 12, PlayOpening = false });
            lv.TakeEvents();
            lv.Flags.Set("found_root_hollows", 1);
            Assert(SessionTest.CountEvents(lv.TakeEvents(), SessionEventKind.SecretFound) == 1, "a new game announces again");

            // a dialogue SetFlag (the stone's rune) announces too
            var d = NewVale(7);
            Assert(d.InteractProp("xw_stone").Kind == InteractKind.Dialogue, "the stone talks");
            SessionTest.Pick(d, "Press the worn rune");
            SessionTest.Finish(d);
            ev = d.TakeEvents();
            Assert(ev.Count(e => e.Kind == SessionEventKind.SecretFound && e.Id2 == "to_xw_cellar") == 1, "the dialogue's SetFlag is announced once");
        }

        [Test]
        public static void RevealedUnderFoot_DoesNotTravelUntilSteppedOutAndBack()
        {
            var s = NewVale(9);
            var door = s.Map.FindTransition("to_xw_cellar");
            s.Map.MarkRegionChecked("reg_xw_hollow");    // walk through the hollow without its roll revealing the door early
            var r = s.MoveLeader(door.pos);
            Assert(s.MapId == "xw_vale" && MapRuntime.RectContains(door.pos, door.size, s.Leader.Position), "standing on the hidden door");
            s.Flags.Set("xw_found_cellar", 1);    // revealed by a hint while standing on it
            r = s.MoveLeader(door.pos + new Vec2(0.4f, 0f));
            Assert(s.MapId == "xw_vale" && r.Trigger.Kind != TriggerKind.Travel, "no surprise travel");
            s.MoveLeader(door.pos + new Vec2(0f, -3.5f));
            r = s.MoveLeader(door.pos);
            Assert(r.Trigger.Kind == TriggerKind.Travel && s.MapId == "xw_cellar", "stepping out and back in travels");
        }

        // ------------------------------------------------------------------ props

        [Test]
        public static void Props_DialogueTextAndFlags()
        {
            var s = NewVale(10);
            // a dialogue prop starts its dialogue with the prop as owner
            var r = s.InteractProp("xw_stone");
            Assert(r.Ok && r.Kind == InteractKind.Dialogue && r.Id == "dlg_xw_stone", $"dialogue prop ({r})");
            Assert(s.Dialogue.IsActive && s.Dialogue.OwnerId == "xw_stone", "owner = the interact id");
            Assert(!s.TakeEvents().Any(e => e.Kind == SessionEventKind.Toast && e.Text == "An old carved stone."), "no text toast for a dialogue prop");
            SessionTest.Finish(s, "Leave it be");
            Assert(!s.Flags.IsSet("xw_found_cellar"), "leaving it sets nothing");
            Assert(s.InspectProp("xw_stone") == "" && s.Dialogue.IsActive, "InspectProp starts the dialogue too (returns \"\")");
            SessionTest.Finish(s, "Leave it be");

            // a text prop toasts its text
            s.TakeEvents();
            Assert(s.InspectProp("xw_sign") == "West: the Vale.", "text prop");
            Assert(s.TakeEvents().Any(e => e.Kind == SessionEventKind.Toast && e.Text == "West: the Vale."), "toasted");
            r = s.InteractProp("xw_sign");
            Assert(r.Ok && r.Kind == InteractKind.Text && r.Message == "West: the Vale.", "InteractProp: Text");

            // requireFlag / hideFlag: not there until (or after) the flag
            Assert(!s.InteractProp("xw_crate").Ok && s.InspectProp("xw_crate") == "", "requireFlag unmet: no crate");
            s.Flags.Set("xw_crates", 1);
            Assert(s.InspectProp("xw_crate") == "Crates of lamp oil.", "requireFlag met: crate");
            Assert(s.InspectProp("xw_boulder") == "A boulder.", "boulder present");
            s.Flags.Set("xw_boulder_moved", 1);
            Assert(!s.InteractProp("xw_boulder").Ok, "hideFlag holds: boulder gone");
            Assert(!s.InteractProp("xw_nothing").Ok && s.InspectProp("") == "", "unknown ids");

            // not during combat
            s.Flags.Clear("xw_boulder_moved");
            Assert(s.StartEncounter("enc_xw_pup") != null, "a fight starts");
            r = s.InteractProp("xw_stone");
            Assert(!r.Ok && !s.Dialogue.IsActive, "a dialogue prop does nothing in combat");
        }

        // ------------------------------------------------------------------ navigation

        [Test]
        public static void Nav_FlagGatedObstacles_RebuildInPlace()
        {
            var s = NewVale(11);
            var nav = s.Nav;
            int v0 = nav.Version;
            var boulder = new Vec2(20f, 5f);
            var chest = new Vec2(25f, 5f);
            var crate = new Vec2(15f, 15f);
            Assert(!nav.IsWalkable(boulder, 0f), "the boulder blocks");
            Assert(nav.IsWalkable(chest, 0f), "a flag-hidden chest does not block");
            Assert(nav.IsWalkable(crate, 0f), "a flag-hidden prop does not block");
            Assert(!nav.IsWalkable(new Vec2(3f, 26f), 0f), "a plain scaled prop blocks");

            s.Flags.Set("xw_unrelated", 1);
            Assert(ReferenceEquals(s.Nav, nav) && nav.Version == v0, "an unrelated flag rebuilds nothing");
            s.Flags.Set("xw_boulder_moved", 1);
            Assert(ReferenceEquals(s.Nav, nav) && nav.Version == v0 + 1, $"same grid, Version+1 ({nav.Version - v0})");
            Assert(nav.IsWalkable(boulder, 0f), "the moved boulder no longer blocks");
            var path = s.FindPath(new Vec2(17f, 5f), new Vec2(23f, 5f));
            Assert(path.Count >= 2 && path.Length < 6.5f, $"the straight way is open ({path.Length:0.0} m)");
            s.Flags.Set("xw_cache_seen", 1);
            Assert(!nav.IsWalkable(chest, 0f) && nav.Version == v0 + 2, "a revealed chest blocks");
            Assert(s.Map.IsChestAvailable(s.Map.FindChest("chest_xw_cache")), "and is there");

            // during combat the ground stays as it is; the change lands when the fight ends
            Assert(s.StartEncounter("enc_xw_pup") != null, "a fight starts");
            int vFight = nav.Version;
            s.Flags.Set("xw_crates", 1);
            Assert(nav.Version == vFight && nav.IsWalkable(crate, 0f), "no rebuild during combat");
            SessionTest.WinBattle(s);
            Assert(ReferenceEquals(s.Nav, nav) && nav.Version == vFight + 1 && !nav.IsWalkable(crate, 0f), "rebuilt once the fight is over");

            // the plain constructor keeps the old rule: every prop and chest blocks
            var plain = new NavGrid(TestDb().Maps["xw_vale"]);
            Assert(!plain.IsWalkable(boulder, 0f) && !plain.IsWalkable(chest, 0f) && !plain.IsWalkable(crate, 0f), "no flag test: everything blocks");
            Assert(!plain.RefreshFlags(), "nothing to refresh without a flag test");

            // a grid that was cleared stays cleared
            var cleared = new NavGrid(TestDb().Maps["xw_vale"], null, s.Flags.Test);
            cleared.ClearObstacles();
            cleared.Rebuild();
            s.Flags.Clear("xw_boulder_moved");
            Assert(!cleared.RefreshFlags() && cleared.IsWalkable(boulder, 0f), "ClearObstacles drops the map's obstacles for good");
        }

        [Test]
        public static void Nav_WaterBlocks_ExceptAtCrossings()
        {
            var map = TestDb().Maps["xw_ford"];
            var nav = new NavGrid(map, null, f => true);
            Assert(!nav.IsWalkable(new Vec2(15f, 4f), 0f) && nav.IsWater(new Vec2(15f, 4f)), "the stream blocks");
            Assert(!nav.IsWalkable(new Vec2(16.2f, 4f), 0f), "within halfWidth of the centreline");
            Assert(nav.IsWalkable(new Vec2(16.9f, 4f), 0f) && !nav.IsWater(new Vec2(16.9f, 4f)), "beyond halfWidth is dry");
            Assert(nav.IsWalkable(new Vec2(15f, 10f), 0f) && !nav.IsWater(new Vec2(15f, 10f)), "the ford is walkable");
            Assert(!nav.IsWalkable(new Vec2(24.5f, 16f), 0f) && nav.IsWater(new Vec2(24.5f, 16f)), "inside the pond");
            Assert(nav.IsWalkable(new Vec2(24.5f, 12.5f), 0f), "beside the pond");
            Assert(nav.IsWalkable(new Vec2(6f, 16f), 0f) && !nav.IsWater(new Vec2(6f, 16f)), "blocksMovement false: wade through");

            var agent = NavAgent.Default;
            var path = nav.FindPath(new Vec2(5f, 4f), new Vec2(25f, 4f), agent);
            Assert(path.Status == PathStatus.Complete, "across by the ford");
            Assert(path.Length > 21.5f && path.Length < 25f, $"the way detours over the ford ({path.Length:0.0} m; straight would be 20)");
            foreach (var p in Sample(path.Points, 0.2f))
                Assert(!nav.IsWater(p), $"the path never wades ({p})");

            // without crossings the stream splits the map
            var closed = new MapDef { id = "xw_wall", width = 30, depth = 20 };
            closed.water.Add(new WaterDef { points = new List<Vec2> { new Vec2(15, -2), new Vec2(15, 22) } });
            var split = new NavGrid(closed);
            Assert(split.FindPath(new Vec2(5f, 4f), new Vec2(25f, 4f), agent).Status != PathStatus.Complete, "no ford, no way across");

            // a game session pathfinds around water too
            var s = NewVale(12);
            s.EnterMap("xw_ford", "default");
            var walk = s.MoveLeader(new Vec2(25f, 4f));
            Assert(Vec2.Distance(s.Leader.Position, new Vec2(25f, 4f)) < 0.6f, $"the party crosses at the ford ({s.Leader.Position})");
            Assert(walk.Distance > 21.5f, $"by the ford, not straight through the water ({walk.Distance:0.0} m)");
        }

        static IEnumerable<Vec2> Sample(List<Vec2> pts, float step)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                float len = Vec2.Distance(pts[i - 1], pts[i]);
                for (float d = 0f; d < len; d += step) yield return Vec2.MoveTowards(pts[i - 1], pts[i], d);
            }
            if (pts.Count > 0) yield return pts[pts.Count - 1];
        }
    }
}
