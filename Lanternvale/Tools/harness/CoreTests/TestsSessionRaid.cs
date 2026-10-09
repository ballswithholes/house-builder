// Raids (Docs/Expansion.md §6, Docs/SessionAPI.md §3 "Raids"): the raid gate (walking, clicking, teleporting), forming a
// raid party, leaving it (party order, leader, auto-play; dismissed companions stay away), saves in a raid, the raid
// wipe, clamping loaded positions to walkable ground, the encounter health scale, and the raid-scale companion AI
// (nearest-tank assist, no two healers on one target in a round).
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Json;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    /// <summary>Shared helpers of the raid tests (TestsSessionRaid, TestsSessionBattleFormation, TestsRaidSim).</summary>
    public static class RaidTest
    {
        public const string Raid = "raid_hollow_heart";

        /// <summary>Every companion, in a fixed order: the first four join the party of 5, the rest wait at camp.</summary>
        public static readonly string[] Companions = { "kael", "aldric", "pip", "rook", "seren", "lys", "torvan", "morwen", "bruna", "ysolde", "liora", "nanami" };

        /// <summary>A game with every companion recruited, standing at the Hollow Heart's door in Mirefen.</summary>
        public static GameSession Game(ClassId c = ClassId.Paladin, int level = 22, ulong seed = 61)
        {
            var s = SessionTest.NewGame(c, level, seed);
            foreach (var id in Companions) s.Recruit(id);
            var home = Db.Maps[Raid];
            s.EnterMap(home.raidReturnMap, home.raidReturnSpawn);
            Assert(s.MapId == home.raidReturnMap, "at the raid's return map");
            s.TakeEvents();
            return s;
        }

        /// <summary>Main first, then the first <paramref name="n"/> − 1 companions of <see cref="Companions"/>.</summary>
        public static List<string> Ids(int n)
        {
            var ids = new List<string> { GameSession.MainId };
            for (int i = 0; ids.Count < n && i < Companions.Length; i++) ids.Add(Companions[i]);
            return ids;
        }

        public static List<string> PartyIds(GameSession s) => s.Party.Select(s.MemberId).ToList();

        /// <summary>The raid's exit to its return map.</summary>
        public static TransitionDef ExitOf(string raidId)
        {
            var m = Db.Maps[raidId];
            return m.transitions.First(t => t.targetMap == m.raidReturnMap);
        }

        /// <summary>The current map's transition into <paramref name="targetMap"/> (unlocked and revealed for the test).</summary>
        public static TransitionDef DoorTo(GameSession s, string targetMap)
        {
            var t = s.MapDef.transitions.First(x => x.targetMap == targetMap);
            if (!string.IsNullOrEmpty(t.requireFlag) && !t.requireFlag.StartsWith("!")) s.Flags.Set(t.requireFlag);
            if (t.hidden && !string.IsNullOrEmpty(t.revealFlag)) s.Flags.Set(t.revealFlag);
            return t;
        }

        /// <summary>Marks every encounter of the current map done, so walking around triggers no fight.</summary>
        public static void Pacify(GameSession s)
        {
            foreach (var e in s.MapDef.encounters) s.Map.MarkEncounterDone(e.id);
        }

        /// <summary>A runtime encounter added to a map's data for one test (removed on Dispose).</summary>
        public sealed class TempEncounter : IDisposable
        {
            readonly MapDef map;
            public readonly EncounterDef Def;
            public TempEncounter(string mapId, string id, Vec2 pos, IList<string> creatures, float spacing = 1.6f)
            {
                map = Db.Maps[mapId];
                Def = new EncounterDef { id = id, pos = pos, radius = 3f };
                int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(creatures.Count)));
                for (int i = 0; i < creatures.Count; i++)
                {
                    float dx = (i % cols - (cols - 1) * 0.5f) * spacing, dy = (i / cols - (creatures.Count - 1) / cols * 0.5f) * spacing;
                    Def.enemies.Add(new EncounterEnemyDef { creature = creatures[i], pos = pos + new Vec2(dx, dy) });
                }
                map.encounters.Add(Def);
            }
            public void Dispose() => map.encounters.Remove(Def);
        }

        public static readonly string[] Bandits = { "cr_bandit_cutthroat", "cr_bandit_archer", "cr_bandit_hexer", "cr_bandit_cutthroat", "cr_bandit_chief" };

        public static List<string> Pack(int n) => Enumerable.Range(0, n).Select(i => Bandits[i % Bandits.Length]).ToList();

        /// <summary>
        /// The centre of a w × h box of the current map where every 0.5 m sample is walkable for a default unit, nearest to
        /// <paramref name="near"/> (null: the map centre). Asserts one exists.
        /// </summary>
        public static Vec2 OpenArea(GameSession s, float w, float h, Vec2? near = null)
        {
            var nav = s.Nav;
            var agent = NavAgent.Default.IgnoringAllUnits();
            var target = near ?? new Vec2(s.MapDef.width * 0.5f, s.MapDef.depth * 0.5f);
            var centres = new List<Vec2>();
            for (float cy = h * 0.5f + 0.5f; cy <= s.MapDef.depth - h * 0.5f - 0.5f; cy += 1f)
                for (float cx = w * 0.5f + 0.5f; cx <= s.MapDef.width - w * 0.5f - 0.5f; cx += 1f)
                    centres.Add(new Vec2(cx, cy));
            foreach (var c in centres.OrderBy(c => Vec2.Distance(c, target)))
            {
                bool open = true;
                for (float y = -h * 0.5f; open && y <= h * 0.5f; y += 0.5f)
                    for (float x = -w * 0.5f; open && x <= w * 0.5f; x += 0.5f)
                        if (!nav.IsWalkable(c + new Vec2(x, y), agent)) open = false;
                if (open) return c;
            }
            Assert(false, $"an open {w}×{h} m area on {s.MapId}");
            return target;
        }

        /// <summary>Kills every party character outright: the battle is lost.</summary>
        public static void Wipe(Battle b)
        {
            foreach (var u in b.Units.ToList())
                if (u.Team == b.PlayerTeam && u.IsCharacter && u.IsAlive) b.KillUnit(u, null, true);
        }
    }

    public static class TestsSessionRaid
    {
        static GameSession Game(ulong seed = 61) => RaidTest.Game(ClassId.Paladin, 22, seed);

        // ------------------------------------------------------------------ candidates and refusals

        [Test]
        public static void RaidCandidates_AreMain_TheParty_ThenCamp_NeverAway()
        {
            var s = Game();
            s.Dismiss("morwen");
            var c = s.RaidCandidates();
            Assert(c.Count == s.Roster.Count - 1 && c[0] == s.Main, "Main first, every recruited companion but the away one: " + c.Count);
            for (int i = 0; i < s.Party.Count; i++) Assert(c[i] == s.Party[i], "then the active party in its order");
            var camp = s.Camp();
            Assert(c.Skip(s.Party.Count).SequenceEqual(camp), "then the camp in roster order");
            Assert(!c.Contains(s.FindMember("morwen")), "an away companion is not a candidate");
            Assert(s.MaxPartySizeOn(RaidTest.Raid) == 10 && s.MaxPartySizeOn("mirefen") == Db.Config.partySize && s.MaxPartySizeOn("nowhere") == Db.Config.partySize,
                "MaxPartySizeOn: the raid's size on raid maps, else config.partySize");
            Assert(new GameSession(Db, 1).RaidCandidates().Count == 0, "no game, no candidates");
        }

        [Test]
        public static void CannotEnterRaid_ExplainsEveryRefusal_AndChangesNothing()
        {
            var s = Game();
            s.Dismiss("nanami");
            var before = RaidTest.PartyIds(s);
            string Why(string map, IReadOnlyList<string> ids) => s.CannotEnterRaidReason(map, ids);
            var ok = RaidTest.Ids(10);
            Assert(Why(RaidTest.Raid, ok) == null, "ten heroes may enter: " + Why(RaidTest.Raid, ok));
            Assert(Why(RaidTest.Raid, RaidTest.Ids(1)) == null, "the main character alone may enter");
            Assert(Why("mirefen", ok).Contains("not a raid"), "an ordinary map: " + Why("mirefen", ok));
            Assert(Why("nowhere", ok).Contains("Unknown map"), "an unknown map");
            Assert(Why(RaidTest.Raid, null).Contains("Choose"), "nobody chosen");
            Assert(Why(RaidTest.Raid, new string[0]).Contains("Choose"), "nobody chosen (empty)");
            Assert(Why(RaidTest.Raid, new[] { "kael", "seren" }).Contains("must come along"), "the main character must come");
            Assert(Why(RaidTest.Raid, new[] { "player", "kael", "kael" }).Contains("twice"), "a duplicate");
            Assert(Why(RaidTest.Raid, new[] { "player", "nobody" }).Contains("Unknown party member"), "an unknown id");
            Assert(Why(RaidTest.Raid, new[] { "player", "nanami" }).Contains("not with you"), "an away companion");
            var eleven = RaidTest.Ids(11);
            Assert(eleven.Count == 11 && Why(RaidTest.Raid, eleven).Contains("at most 10"), "eleven are too many");

            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "a conversation");
            Assert(Why(RaidTest.Raid, ok).Contains("conversation"), "not while talking");
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", ok, true) != null && s.MapId == "mirefen", "EnterRaid refuses while talking");
            s.EndDialogue();

            s.EnterMap("lanternvale", "default");
            Assert(s.StartEncounter("enc_training_dummy") != null, "a practice fight");
            Assert(Why(RaidTest.Raid, ok).Contains("combat"), "not in combat");
            Assert(s.LeaveCombat() != null, "leave the dummy");

            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", eleven, true).Contains("at most"), "EnterRaid returns the reason");
            Assert(!s.InRaid && s.MapId == "lanternvale" && RaidTest.PartyIds(s).SequenceEqual(before), "a refused raid changes nothing");
            Assert(s.LastError.Contains("at most"), "LastError holds the reason");
        }

        // ------------------------------------------------------------------ forming the raid

        [Test]
        public static void EnterRaid_FormsTheRaidParty_MainFirst_InTheChosenOrder()
        {
            var s = Game();
            Assert(s.SetLeader(s.FindMember("kael")) == null, "Kael leads");
            s.TakeEvents();
            var ids = new List<string> { "seren", "kael", GameSession.MainId, "liora", "bruna", "lys", "torvan", "pip", "nanami", "ysolde" };
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", ids, true) == null, "enter: " + s.LastError);
            var ev = s.TakeEvents();
            Assert(s.MapId == RaidTest.Raid && s.InRaid && s.RaidSize == 10 && s.PartySize == 10, "in the raid, size 10");
            var want = new List<string> { GameSession.MainId }.Concat(ids.Where(x => x != GameSession.MainId)).ToList();
            Assert(RaidTest.PartyIds(s).SequenceEqual(want), "Main first, then the chosen order: " + string.Join(",", RaidTest.PartyIds(s)));
            Assert(s.Leader == s.FindMember("kael"), "Kael still leads (he came along)");
            foreach (var u in s.Party) Assert(u.AutoPlay == (u != s.Main), $"{u.Name}: companions auto-play, Main does not");
            Assert(s.FindMember("aldric").AutoPlay == false && s.CompanionStatusOf("aldric") == CompanionStatus.Camp, "Aldric (left behind) waits at camp, untouched");
            var entered = SessionTest.FindEvent(ev, SessionEventKind.MapEntered, RaidTest.Raid);
            var started = SessionTest.FindEvent(ev, SessionEventKind.RaidStarted, RaidTest.Raid);
            Assert(entered != null && entered.Id2 == "from_mirefen" && started != null && started.Amount == 10, "MapEntered, then RaidStarted (Amount 10)");
            Assert(ev.IndexOf(entered) < ev.IndexOf(started), "RaidStarted after MapEntered");
            Assert(SessionTest.FindEvent(ev, SessionEventKind.PartyChanged) != null && SessionTest.FindEvent(ev, SessionEventKind.RaidPartyRequested) == null,
                "PartyChanged; no picker request");
            var spawn = s.Map.SpawnPosition("from_mirefen");
            foreach (var u in s.PartyUnits())
            {
                Assert(s.Nav.IsWalkable(u.Position, NavAgent.Default.IgnoringAllUnits()), $"{u.Name} stands on walkable ground");
                Assert(Vec2.Distance(u.Position, spawn) < 8f, $"{u.Name} stands by the spawn");
            }
            Assert(s.Field != null && ids.All(id => s.Field.Units.Contains(s.FindMember(id))), "the exploration context holds the raid");

            // the raid's own size caps the active party now
            Assert(s.SetPartyMemberActive("aldric", true).Contains("full (10)"), "the raid is full at 10");
            Assert(s.SetPartyMemberActive("pip", false) == null && s.SetPartyMemberActive("aldric", true) == null && s.Party.Count == 10, "swap at the raid");
            Assert(s.CannotEnterRaidReason(RaidTest.Raid, RaidTest.Ids(3)).Contains("already"), "one raid at a time");
        }

        [Test]
        public static void RaidGate_WalkingClickingTeleportingAndEnterMap_AskForAParty()
        {
            var s = Game();
            RaidTest.Pacify(s);
            var before = RaidTest.PartyIds(s);
            var door = RaidTest.DoorTo(s, RaidTest.Raid);

            // walking into the door: the walk stops at a RaidGate, nobody travels
            var outside = s.Leader.Position;
            Assert(s.Map.TransitionAt(outside) == null, "the spawn is outside the door");
            Assert(!s.CheckTriggers().Stop, "standing outside arms the door");
            var r = s.UpdatePartyPositions(door.pos);
            Assert(r.Stop && r.Kind == TriggerKind.RaidGate && r.Id == RaidTest.Raid, $"walking in stops at the raid gate ({r.Kind})");
            var ev = s.TakeEvents();
            var req = SessionTest.FindEvent(ev, SessionEventKind.RaidPartyRequested);
            Assert(req != null && req.Id == RaidTest.Raid && req.Id2 == door.targetSpawn && req.Amount == 10, "RaidPartyRequested (map, spawn, size)");
            Assert(s.MapId == "mirefen" && !s.InRaid && RaidTest.PartyIds(s).SequenceEqual(before), "no travel, the party unchanged");
            Assert(!s.UpdatePartyPositions(door.pos + new Vec2(0.2f, 0f)).Stop && SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidPartyRequested) == null,
                "standing in the door asks once (step out and in again)");
            s.UpdatePartyPositions(outside);
            Assert(s.UpdatePartyPositions(door.pos).Kind == TriggerKind.RaidGate, "stepping out and back in asks again");
            s.TakeEvents();

            // clicking the door
            s.SetPartyPositions(outside);
            var ir = s.UseTransition(door.id);
            Assert(ir.Ok && ir.Kind == InteractKind.None && ir.Id == RaidTest.Raid && s.MapId == "mirefen", $"clicking: no travel ({ir.Kind})");
            Assert(SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidPartyRequested, RaidTest.Raid) != null, "clicking asks for the party");

            // travel by code
            s.EnterMap(RaidTest.Raid, "from_mirefen");
            Assert(s.MapId == "mirefen" && SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidPartyRequested, RaidTest.Raid) != null, "EnterMap asks too");

            // a dialogue Teleport: deferred until the conversation ends, then the same request
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk");
            s.Teleport(RaidTest.Raid, "");
            Assert(SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidPartyRequested) == null, "nothing while talking");
            s.EndDialogue();
            var tp = SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidPartyRequested, RaidTest.Raid);
            Assert(tp != null && tp.Id2 == "default" && s.MapId == "mirefen" && !s.InRaid, "after the conversation: the picker, no travel");

            // the picker answers with EnterRaid: then the door is just a door
            Assert(s.EnterRaid(tp.Id, tp.Id2, RaidTest.Ids(6), true) == null && s.MapId == RaidTest.Raid, "the chosen party enters");
            Assert(s.Party.Count == 6, "six heroes");
        }

        // ------------------------------------------------------------------ leaving

        [Test]
        public static void LeavingTheRaid_RestoresOrderLeaderAndAutoPlay_DismissedStayAway()
        {
            var s = Game();
            // the normal party: Main, Kael, Aldric, Pip, Rook — Kael leads, Pip and Seren (camp) auto-play
            var normal = RaidTest.PartyIds(s);
            Assert(normal.SequenceEqual(new[] { "player", "kael", "aldric", "pip", "rook" }), "the normal party: " + string.Join(",", normal));
            s.SetLeader(s.FindMember("kael"));
            s.SetAutoPlay(s.FindMember("pip"), true);
            s.SetAutoPlay(s.FindMember("seren"), true);
            var autoBefore = s.Roster.ToDictionary(s.MemberId, u => u.AutoPlay);

            var ids = new List<string> { "player", "seren", "liora", "kael", "aldric", "bruna", "lys", "morwen", "nanami", "torvan" };
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", ids, true) == null, "enter: " + s.LastError);
            // during the raid: a new leader, auto-play for Main, Aldric dismissed (away)
            Assert(s.SetLeader(s.FindMember("seren")) == null, "Seren leads the raid");
            s.SetAutoPlay(s.Main, true);
            s.Dismiss("aldric");
            Assert(s.CompanionStatusOf("aldric") == CompanionStatus.Away && s.Party.Count == 9, "Aldric leaves (away)");
            s.TakeEvents();

            var exit = RaidTest.ExitOf(RaidTest.Raid);
            s.SetPartyPositions(s.Map.SpawnPosition("from_mirefen"));
            var r = s.UseTransition(exit.id);
            Assert(r.Ok && r.Kind == InteractKind.Travel && s.MapId == "mirefen", "out of the raid");
            var ev = s.TakeEvents();
            Assert(!s.InRaid && s.RaidSize == 0 && s.PartySize == Db.Config.partySize, "the raid is over; the party size is config's again");
            Assert(RaidTest.PartyIds(s).SequenceEqual(new[] { "player", "kael", "pip", "rook" }), "the normal party, its order, without the dismissed Aldric: " + string.Join(",", RaidTest.PartyIds(s)));
            Assert(s.Leader == s.FindMember("kael"), "Kael leads again");
            foreach (var u in s.Roster)
                Assert(u.AutoPlay == autoBefore[s.MemberId(u)], $"{u.Name}: auto-play as before the raid ({u.AutoPlay})");
            Assert(s.CompanionStatusOf("seren") == CompanionStatus.Camp && s.CompanionStatusOf("aldric") == CompanionStatus.Away, "raid-only members go back to camp; Aldric stays away");
            var entered = SessionTest.FindEvent(ev, SessionEventKind.MapEntered, "mirefen");
            var ended = SessionTest.FindEvent(ev, SessionEventKind.RaidEnded, RaidTest.Raid);
            Assert(entered != null && ended != null && ended.Amount == 0 && ev.IndexOf(entered) < ev.IndexOf(ended), "MapEntered, then RaidEnded (Amount 0)");
            var spawn = s.Map.SpawnPosition(exit.targetSpawn);
            foreach (var u in s.PartyUnits()) Assert(Vec2.Distance(u.Position, spawn) < 8f, $"{u.Name} arrives at {exit.targetSpawn}");
            Assert(s.Field.Units.Count(u => u.IsCharacter) == 4, "the exploration context holds the normal party only");

            // the leader dismissed during the raid: Main leads afterwards
            var s2 = Game(62);
            s2.SetLeader(s2.FindMember("pip"));
            Assert(s2.EnterRaid(RaidTest.Raid, "", RaidTest.Ids(10), false) == null, "enter");
            Assert(s2.Party.All(u => !u.AutoPlay), "without auto-play nobody's flag changes");
            s2.Dismiss("pip");
            s2.EnterMap("mirefen", "from_raid_hollow_heart");
            Assert(!s2.InRaid && s2.Leader == s2.Main && RaidTest.PartyIds(s2).SequenceEqual(new[] { "player", "kael", "aldric", "rook" }), "Pip away: Main leads");
        }

        [Test]
        public static void RecruitInARaid_JoinsTheRaid_ThenWaitsAtCamp()
        {
            var s = SessionTest.NewGame(ClassId.Mage, 22, 63);
            foreach (var id in new[] { "kael", "seren", "pip" }) s.Recruit(id);
            s.EnterMap("mirefen", "from_raid_hollow_heart");
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", new[] { "player", "kael", "seren", "pip" }, true) == null, "a small raid");
            s.Settings.CompanionAutoPlay = true;
            s.Recruit("lys");
            Assert(s.CompanionStatusOf("lys") == CompanionStatus.Active && s.Party.Count == 5, "a recruit joins the raid (room up to 10)");
            s.Recruit("rook");
            Assert(s.Party.Count == 6 && s.PartySize == 10, "the raid holds 6 of 10");
            s.EnterMap("mirefen", "from_raid_hollow_heart");
            Assert(RaidTest.PartyIds(s).SequenceEqual(new[] { "player", "kael", "seren", "pip" }), "afterwards the normal party; the recruits wait at camp");
            Assert(s.CompanionStatusOf("lys") == CompanionStatus.Camp && s.FindMember("lys").AutoPlay && s.FindMember("rook").AutoPlay,
                "recruits keep the auto-play they were recruited with");
            Assert(!s.FindMember("kael").AutoPlay, "members get their old auto-play back");
        }

        // ------------------------------------------------------------------ saves

        [Test]
        public static void SaveInARaid_LoadsTheRaid_ResavesIdentically_AndStillRestores()
        {
            var s = Game();
            s.SetLeader(s.FindMember("aldric"));
            s.SetAutoPlay(s.FindMember("rook"), true);
            var normal = RaidTest.PartyIds(s);
            var ids = new List<string> { "player", "torvan", "kael", "seren", "liora", "lys", "morwen", "bruna", "nanami", "ysolde" };
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", ids, true) == null, "enter");
            var json = s.SaveGame();
            var d = s.BuildSaveData();
            Assert(d.raid != null && d.raid.size == 10 && d.raid.normalParty.SequenceEqual(normal) && d.raid.normalLeader == "aldric", "the raid is saved (normal party in order)");
            Assert(d.raid.normalAutoPlay.SequenceEqual(new[] { "rook" }), "auto-play before the raid: " + string.Join(",", d.raid.normalAutoPlay));
            Assert(d.party.SequenceEqual(ids), "the raid party is saved in order");

            var s2 = new GameSession(Db, 1);
            Assert(s2.LoadGame(json, out var err), "load: " + err);
            Assert(s2.InRaid && s2.RaidSize == 10 && s2.PartySize == 10 && s2.MapId == RaidTest.Raid, "in the raid after loading");
            Assert(RaidTest.PartyIds(s2).SequenceEqual(ids), "the raid party in order");
            Assert(s2.SaveGame() == json, "save -> load -> save is byte-identical");
            s2.EnterMap("mirefen", "from_raid_hollow_heart");
            Assert(!s2.InRaid && RaidTest.PartyIds(s2).SequenceEqual(normal) && s2.Leader == s2.FindMember("aldric"), "leaving after the load restores the normal party and leader");
            Assert(s2.FindMember("rook").AutoPlay && !s2.FindMember("kael").AutoPlay && !s2.FindMember("seren").AutoPlay, "and auto-play");

            // a save outside raids has no raid
            var plain = s2.SaveGame();
            Assert(s2.BuildSaveData().raid == null && !plain.Contains("\"raid\""), "no raid: nothing is written");

            // a raid saved on a map that is no longer a raid (the data changed): the normal party comes back at once
            d.mapId = "mirefen";
            var s3 = new GameSession(Db, 1);
            Assert(s3.LoadGame(JsonWriter.Serialize(d, true), out err), "load the odd save: " + err);
            Assert(!s3.InRaid && RaidTest.PartyIds(s3).SequenceEqual(normal) && s3.Leader == s3.FindMember("aldric"), "the raid ended on load");
            foreach (var u in s3.PartyUnits()) Assert(s3.Nav.IsWalkable(u.Position, NavAgent.Default.IgnoringAllUnits()), $"{u.Name} on walkable ground");
        }

        [Test]
        public static void Load_ClampsPartyPositionsToWalkableGround()
        {
            var s = SessionTest.NewGame(ClassId.Hunter, 12, seed: 64);
            s.Recruit("kael");
            Assert(s.UseAbility(s.Main, "hunter_call_pet").Ok && s.Main.Pet != null, "a pet");
            s.EnterMap("whisperwood", "from_village");
            var agent = NavAgent.Default.IgnoringAllUnits();
            Vec2? blocked = null;
            for (float y = 1f; y < s.MapDef.depth - 1f && blocked == null; y += 0.5f)
                for (float x = 1f; x < s.MapDef.width - 1f; x += 0.5f)
                    if (!s.Nav.IsWalkable(new Vec2(x, y), agent)) { blocked = new Vec2(x, y); break; }
            Assert(blocked.HasValue, "whisperwood has unwalkable ground (trees, rocks)");
            var d = s.BuildSaveData();
            d.roster[0].position = blocked.Value;
            d.roster[0].pet.position = new Vec2(-6f, -3f);
            d.roster[1].position = new Vec2(s.MapDef.width + 5f, 4f);
            var s2 = new GameSession(Db, 1);
            Assert(s2.LoadGame(JsonWriter.Serialize(d, true), out var err), "load: " + err);
            foreach (var u in s2.PartyUnits())
                Assert(s2.Nav.IsWalkable(u.Position, agent) && s2.Nav.InBounds(u.Position), $"{u.Name} stands on walkable ground: {u.Position}");
            Assert(Vec2.Distance(s2.Main.Position, blocked.Value) < 3f, "the nearest walkable spot");
            var kept = s.SaveGame();
            var s3 = new GameSession(Db, 1);
            Assert(s3.LoadGame(kept, out err) && s3.SaveGame() == kept, "walkable positions are kept exactly");
        }

        // ------------------------------------------------------------------ wipe

        [Test]
        public static void RaidWipe_SendsThePartyHomeHealed_NoGameOver_NoLockout()
        {
            var s = Game();
            var normal = RaidTest.PartyIds(s);
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), true) == null, "enter");
            var at = RaidTest.OpenArea(s, 16f, 10f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter(RaidTest.Raid, "enc_test_raid_wipe", at + new Vec2(4f, 0f), RaidTest.Pack(6)))
            {
                s.SetPartyPositions(at - new Vec2(4f, 0f));
                s.TakeEvents();
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "a raid fight: " + s.LastError);
                RaidTest.Wipe(b);
                Assert(b.IsOver && b.Outcome == BattleOutcome.Defeat, "the raid wipes");
                var sum = s.FinishBattle();
                var ev = s.TakeEvents();
                Assert(sum != null && sum.Outcome == CombatEndKind.Defeat && sum.Xp == 0, "a defeat, no XP");
                Assert(!s.IsGameOver && s.Mode == SessionMode.Exploration && s.CannotSaveReason() == null, "no game over");
                var home = Db.Maps[RaidTest.Raid];
                Assert(s.MapId == home.raidReturnMap && !s.InRaid, "back at the raid's return map; the raid is over");
                Assert(RaidTest.PartyIds(s).SequenceEqual(normal), "the normal party");
                var spawn = s.Map.SpawnPosition(home.raidReturnSpawn);
                Assert(Vec2.Distance(s.Leader.Position, spawn) < 6f, "at the return spawn");
                foreach (var u in s.Roster)
                {
                    Assert(!u.Dead && !u.Downed && u.Health == u.MaxHealth && u.Mana == u.MaxMana, $"{u.Name} healed ({u.Health:0}/{u.MaxHealth:0})");
                    Assert(!u.Auras.Any(a => a.IsDebuff && !a.IsPassive), $"{u.Name}: no debuffs left");
                }
                Assert(SessionTest.FindEvent(ev, SessionEventKind.GameOver) == null, "no GameOver event");
                var ended = SessionTest.FindEvent(ev, SessionEventKind.CombatEnded);
                var raidEnded = SessionTest.FindEvent(ev, SessionEventKind.RaidEnded, RaidTest.Raid);
                var healed = SessionTest.FindEvent(ev, SessionEventKind.PartyHealed);
                Assert(ended != null && ended.Outcome == CombatEndKind.Defeat, "CombatEnded (Defeat)");
                Assert(raidEnded != null && raidEnded.Amount == 1, "RaidEnded, Amount 1 = a wipe");
                Assert(healed != null && healed.Text.Contains("wiped"), "the notice: " + healed?.Text);
                Assert(ev.IndexOf(ended) < ev.IndexOf(raidEnded) && ev.IndexOf(raidEnded) < ev.IndexOf(healed), "CombatEnded, RaidEnded, PartyHealed");
                var rt = s.World.GetMap(RaidTest.Raid);
                Assert(!rt.IsEncounterDone(enc.Def) && rt.IsEncounterAvailable(enc.Def), "the encounter waits for the next attempt");

                // no lockout: the raid can be entered again at once, and the fight is there
                Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), true) == null && s.InRaid, "enter again");
                Assert(s.StartEncounter(enc.Def.id) != null, "fight again");
            }
        }

        [Test]
        public static void DefeatOutsideRaids_IsStillGameOver()
        {
            var s = Game();
            var at = RaidTest.OpenArea(s, 12f, 8f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter("mirefen", "enc_test_mirefen_defeat", at + new Vec2(3f, 0f), RaidTest.Pack(2)))
            {
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "a fight: " + s.LastError);
                RaidTest.Wipe(b);
                var sum = s.FinishBattle();
                Assert(sum.Outcome == CombatEndKind.Defeat && s.IsGameOver && s.MapId == "mirefen", "game over off raid maps");
            }
        }

        // ------------------------------------------------------------------ encounter scaling

        [Test]
        public static void EncounterHealthScale_FivePlusAreTougher_RaidsAreTunedInData()
        {
            Assert(GameSession.EncounterHealthScale(1, false) == 1f && GameSession.EncounterHealthScale(4, false) == 1f, "up to 4: unchanged");
            Assert(Math.Abs(GameSession.EncounterHealthScale(5, false) - 1.2f) < 1e-5f && Math.Abs(GameSession.EncounterHealthScale(10, false) - 2.2f) < 1e-5f, "1 + 0.2·(n − 4)");
            Assert(GameSession.EncounterHealthScale(10, true) == 1f, "raid maps: 1");

            float Scale(GameSession s, string map)
            {
                var at = RaidTest.OpenArea(s, 12f, 8f, s.Leader.Position);
                using (var enc = new RaidTest.TempEncounter(map, "enc_test_scale_" + map + "_" + s.Party.Count, at + new Vec2(3f, 0f), RaidTest.Pack(3)))
                {
                    var b = s.StartEncounter(enc.Def.id);
                    Assert(b != null, "fight: " + s.LastError);
                    float k = 0f;
                    foreach (var e in b.Units.Where(u => u.Team == Team.Enemy))
                    {
                        var plain = UnitFactory.CreateCreature(Db, e.Creature, e.Level, Team.Enemy);
                        k = e.MaxHealth / plain.MaxHealth;
                        Assert(e.Health == e.MaxHealth, "enemies start at full (scaled) health");
                    }
                    b.Finish(BattleOutcome.Fled);
                    s.FinishBattle();
                    return k;
                }
            }
            var g = Game();
            Assert(g.Party.Count == 5 && Math.Abs(Scale(g, "mirefen") - 1.2f) < 0.01f, "a party of 5: ×1.2");
            g.SetPartyMemberActive("rook", false);
            Assert(Math.Abs(Scale(g, "mirefen") - 1f) < 0.01f, "a party of 4: ×1");
            Assert(g.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), true) == null, "raid");
            Assert(Math.Abs(Scale(g, RaidTest.Raid) - 1f) < 0.01f, "a raid of 10 on a raid map: ×1");
        }

        // ------------------------------------------------------------------ AI at raid scale

        [Test]
        public static void CompanionDps_AssistTheNearestTank()
        {
            var b = NewBattle(81, new Inventory());
            var t1 = Hero(ClassId.Warrior, 30, name: "Tank One").At(20f, 20f);
            var t2 = Hero(ClassId.Warrior, 30, name: "Tank Two").At(40f, 20f);
            var dps = Hero(ClassId.Rogue, 30, name: "Stabby").At(38f, 21f);
            var a = Mob("cr_bandit_chief", 30).At(22f, 20f).Tough();
            var c = Mob("cr_bandit_chief", 30).At(42f, 20f).Tough();
            t1.RoleOverride = UnitRole.Tank;
            t2.RoleOverride = UnitRole.Tank;
            dps.RoleOverride = UnitRole.MeleeDps;
            foreach (var u in new[] { t1, t2, dps, a, c }) b.AddUnit(u);
            b.Begin();
            t1.AttackTarget = a;
            t2.AttackTarget = c;
            var enemies = new List<Unit> { a, c };
            Assert(AI.AssistTank(b, dps) == t2, "the tank next to the rogue");
            Assert(AI.FocusTarget(b, dps, enemies, UnitRole.MeleeDps) == c, "assists Tank Two's target");
            dps.Position = new Vec2(23f, 21f);
            Assert(AI.AssistTank(b, dps) == t1 && AI.FocusTarget(b, dps, enemies, UnitRole.MeleeDps) == a, "next to Tank One: his target");
            Assert(AI.AssistTank(b, t1) == t2, "a tank's assist tank is the other one");
            b.KillUnit(t1, a, true);
            Assert(AI.AssistTank(b, dps) == t2 && AI.FocusTarget(b, dps, enemies, UnitRole.MeleeDps) == c, "a dead tank is not assisted");
        }

        [Test]
        public static void TwoHealers_NeverHealTheSameTargetInOneRound()
        {
            var b = NewBattle(82, new Inventory());
            var h1 = Hero(ClassId.Priest, 30, talents: false, name: "Healer A").At(20f, 22f);
            var h2 = Hero(ClassId.Priest, 30, talents: false, name: "Healer B").At(21f, 22f);
            var tank = Hero(ClassId.Warrior, 30, name: "Tank").At(24f, 20f);
            var mage = Hero(ClassId.Mage, 30, name: "Mage").At(21f, 18f);
            var foe = Mob("cr_bandit_chief", 30).At(40f, 20f).Tough();
            foreach (var h in new[] { h1, h2 })
            {
                h.RoleOverride = UnitRole.Healer;
                foreach (var id in h.Abilities.Keys.ToList()) if (id != "priest_flash_heal") h.Abilities.Remove(id);
            }
            tank.RoleOverride = UnitRole.Tank;
            foreach (var u in new[] { h1, h2, tank, mage, foe }) b.AddUnit(u);
            b.Begin();
            // the healer acting first this round is "first"
            var first = b.TurnOrder.IndexOf(h1) < b.TurnOrder.IndexOf(h2) ? h1 : h2;
            var second = first == h1 ? h2 : h1;
            SkipTo(b, first);
            int round = b.Round;
            tank.Health = tank.MaxHealth * 0.3f;
            mage.Health = mage.MaxHealth * 0.55f;
            var s1 = AI.NextStep(b, first);
            Assert(s1.Kind == AIStepKind.UseAbility && s1.AbilityId == "priest_flash_heal" && s1.Target == tank, "the first healer heals the tank: " + s1);
            Assert(AI.Execute(b, s1).Ok, "healed");
            Assert(AI.HealClaimedByOther(b, second, tank) && !AI.HealClaimedByOther(b, first, tank), "the tank is the first healer's this round");
            Assert(!AI.HealClaimedByOther(b, second, mage), "the mage is free");
            if (b.ActiveUnit == first) b.EndTurn(first);
            SkipTo(b, second);
            Assert(b.ActiveUnit == second && b.Round == round, "the second healer acts in the same round");
            tank.Health = tank.MaxHealth * 0.3f;   // the tank took a beating again meanwhile
            var s2 = AI.NextStep(b, second);
            Assert(!(s2.Kind == AIStepKind.UseAbility && s2.Target == tank), "the second healer does not heal the tank too: " + s2);
            Assert(s2.Kind == AIStepKind.UseAbility && s2.AbilityId == "priest_flash_heal" && s2.Target == mage, "it heals the mage instead: " + s2);

            // a new round frees every target
            if (b.ActiveUnit == second) b.EndTurn(second);
            SkipTo(b, first);
            Assert(b.Round == round + 1 && !AI.HealClaimedByOther(b, second, tank), "next round: no claims");

            // a heal that could not be cast claims nothing
            var b2 = NewBattle(83, new Inventory());
            var x1 = Hero(ClassId.Priest, 30, talents: false, name: "X1").At(20f, 22f);
            var x2 = Hero(ClassId.Priest, 30, talents: false, name: "X2").At(21f, 22f);
            var t = Hero(ClassId.Warrior, 30, name: "T").At(24f, 20f);
            var f2 = Mob("cr_bandit_chief", 30).At(40f, 20f).Tough();
            foreach (var u in new[] { x1, x2, t, f2 }) b2.AddUnit(u);
            b2.Begin();
            AI.ClaimHeal(b2, x1, t, Db.Ability("priest_flash_heal"));
            Assert(!AI.HealClaimedByOther(b2, x2, t), "a claim without the heal cast (AITurnMemory) does not count");
        }

        // ------------------------------------------------------------------ a whole raid fight

        [Test]
        public static void RaidFight_TenVersusTen_ResolvesAndPays()
        {
            var s = Game(65);
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), true) == null, "enter");
            var at = RaidTest.OpenArea(s, 18f, 12f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter(RaidTest.Raid, "enc_test_raid_fight", at + new Vec2(5f, 0f), RaidTest.Pack(10)))
            {
                s.SetPartyPositions(at - new Vec2(4f, 0f));
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "fight: " + s.LastError);
                Assert(b.Units.Count(u => u.Team == b.PlayerTeam && u.IsCharacter) == 10 && b.Units.Count(u => u.Team == Team.Enemy) == 10, "10 v 10");
                var sum = SessionTest.WinBattle(s, 60);
                Assert(sum.Xp > 0 && s.InRaid && s.MapId == RaidTest.Raid, "victory pays XP; the raid goes on");
                Assert(s.World.GetMap(RaidTest.Raid).IsEncounterDone(enc.Def), "the encounter is done");
            }
        }
    }
}
