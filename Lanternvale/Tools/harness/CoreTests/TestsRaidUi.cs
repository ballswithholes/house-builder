// Raid UI support (Docs/Expansion.md §6 raid-ui; S/Core/Session/RaidPlanning.cs): the raid picker's suggested party,
// role summary and level warning; the picker's round trip (RaidPartyRequested → EnterRaid → leave); the auto-play
// snapshot behind the combat HUD's "Auto: all companions" / "Auto-battle" toggles; the "big battle" threshold of the
// compact turn strip and the AI pacing; and the raid-wipe notice the toasts merge into one banner.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsRaidUi
    {
        static GameSession Game(ulong seed = 71) => RaidTest.Game(ClassId.Paladin, 22, seed);

        /// <summary>Rook (in the party) calls his pet.</summary>
        static Unit CallRooksPet(GameSession s)
        {
            var rook = s.FindMember("rook");
            Assert(s.IsInParty(rook) && s.UseAbility(rook, "hunter_call_pet").Ok && rook.Pet != null, "Rook calls his pet");
            return rook;
        }

        // ------------------------------------------------------------------ roles

        [Test]
        public static void RoleSummary_ReadsLikeARaidRoster()
        {
            var ten = RaidPlanning.CountRoles(new[]
            {
                UnitRole.Tank, UnitRole.Tank, UnitRole.Healer, UnitRole.Healer, UnitRole.MeleeDps, UnitRole.MeleeDps,
                UnitRole.MeleeDps, UnitRole.RangedDps, UnitRole.RangedDps, UnitRole.RangedDps,
            });
            Assert(ten.Tanks == 2 && ten.Healers == 2 && ten.Melee == 3 && ten.Ranged == 3 && ten.Dps == 6 && ten.Total == 10, "counts");
            Assert(RaidPlanning.RoleSummary(ten) == "2 tanks · 2 healers · 6 dps", RaidPlanning.RoleSummary(ten));
            var one = RaidPlanning.CountRoles(new[] { UnitRole.Tank, UnitRole.Healer, UnitRole.RangedDps });
            Assert(RaidPlanning.RoleSummary(one) == "1 tank · 1 healer · 1 dps", "singular: " + RaidPlanning.RoleSummary(one));
            var none = RaidPlanning.CountRoles((IEnumerable<UnitRole>)null);
            Assert(none.Total == 0 && RaidPlanning.RoleSummary(none) == "0 tanks · 0 healers · 0 dps", "nobody: " + RaidPlanning.RoleSummary(none));
            Assert(RaidPlanning.CountRoles(new[] { UnitRole.Auto }).Melee == 1, "an unresolved role counts as damage");

            Assert(RaidPlanning.RoleAdvice(ten) == null, "a balanced raid needs no advice");
            Assert(RaidPlanning.RoleAdvice(RaidPlanning.CountRoles(new[] { UnitRole.Tank, UnitRole.MeleeDps })) == null, "two heroes: too few to judge");
            Assert(RaidPlanning.RoleAdvice(RaidPlanning.CountRoles(new[] { UnitRole.Healer, UnitRole.MeleeDps, UnitRole.MeleeDps })).Contains("No tank"), "no tank");
            Assert(RaidPlanning.RoleAdvice(RaidPlanning.CountRoles(new[] { UnitRole.Tank, UnitRole.MeleeDps, UnitRole.MeleeDps })).Contains("No healer"), "no healer");
            Assert(RaidPlanning.RoleAdvice(RaidPlanning.CountRoles(new[] { UnitRole.RangedDps, UnitRole.MeleeDps, UnitRole.MeleeDps })).Contains("no healer"), "neither");

            Assert(RaidPlanning.WantedTanks(1) == 0 && RaidPlanning.WantedHealers(1) == 0, "alone: no roles wanted");
            Assert(RaidPlanning.WantedTanks(5) == 1 && RaidPlanning.WantedHealers(5) == 1, "five: one of each");
            Assert(RaidPlanning.WantedTanks(10) == 2 && RaidPlanning.WantedHealers(10) == 2, "ten: two of each");
            Assert(RaidPlanning.WantedTanks(3) == 1 && RaidPlanning.WantedHealers(8) == 2, "small raids: at least one");
        }

        [Test]
        public static void CountRoles_OfUnits_CountsCharactersOnly()
        {
            var s = Game();
            CallRooksPet(s);
            var units = s.PartyUnits();
            Assert(units.Count > s.Party.Count, "the party brings a pet (Rook's Tide): " + units.Count);
            var c = RaidPlanning.CountRoles(units);
            Assert(c.Total == s.Party.Count, $"pets are not counted ({c.Total} of {s.Party.Count})");
            Assert(c.Tanks == s.Party.Count(u => u.Role == UnitRole.Tank) && c.Healers == s.Party.Count(u => u.Role == UnitRole.Healer), "by Unit.Role");
            Assert(RaidPlanning.CountRoles((IEnumerable<Unit>)null).Total == 0, "null: nobody");
        }

        // ------------------------------------------------------------------ the picker's suggestion

        /// <summary>The suggestion as specified, computed independently of RaidPlanning.</summary>
        static List<string> Expected(GameSession s, int size)
        {
            var cands = s.RaidCandidates();
            var picked = new List<Unit> { s.Main };
            foreach (var u in s.Party) if (u != s.Main && picked.Count < size) picked.Add(u);
            int tanks = picked.Count(u => u.Role == UnitRole.Tank), healers = picked.Count(u => u.Role == UnitRole.Healer);
            var camp = cands.Where(u => !s.IsInParty(u)).ToList();
            foreach (var u in camp.Where(u => u.Role == UnitRole.Tank).ToList())
                if (picked.Count < size && tanks < RaidPlanning.WantedTanks(size)) { picked.Add(u); tanks++; }
            foreach (var u in camp.Where(u => u.Role == UnitRole.Healer).ToList())
                if (picked.Count < size && healers < RaidPlanning.WantedHealers(size)) { picked.Add(u); healers++; }
            foreach (var u in camp) if (picked.Count < size && !picked.Contains(u)) picked.Add(u);
            return picked.Select(s.MemberId).ToList();
        }

        [Test]
        public static void DefaultSelection_KeepsTheParty_ThenTanksAndHealersFromCamp_ThenTheRest()
        {
            var s = Game();
            var normal = RaidTest.PartyIds(s);
            var cands = s.RaidCandidates();
            var ids = RaidPlanning.DefaultSelection(s, cands, 10);
            Assert(ids.SequenceEqual(Expected(s, 10)), "the suggestion: " + string.Join(",", ids) + " / expected " + string.Join(",", Expected(s, 10)));
            Assert(ids.Count == 10 && ids.Distinct().Count() == 10 && ids[0] == GameSession.MainId, "ten distinct heroes, Main first");
            Assert(ids.Take(normal.Count).SequenceEqual(normal), "the party you travel with comes first, in its order");
            var roles = RaidPlanning.CountRoles(ids.Select(s.FindMember));
            Assert(roles.Tanks >= 2 && roles.Healers >= 2, "two tanks and two healers at least: " + RaidPlanning.RoleSummary(roles));
            // with the data's roles: Kael tanks in the party; Bruna (the first tank at camp) and Seren + Liora (the first
            // healers at camp) come before the damage dealers waiting there
            Assert(ids.Contains("bruna") && ids.Contains("seren") && ids.Contains("liora"), "camp tanks and healers first: " + string.Join(",", ids));
            Assert(ids.IndexOf("bruna") < ids.IndexOf("lys") && ids.IndexOf("liora") < ids.IndexOf("lys"), "before Lys (damage), though she waits earlier in the roster");
            Assert(s.CannotEnterRaidReason(RaidTest.Raid, ids) == null, "the suggestion can enter: " + s.CannotEnterRaidReason(RaidTest.Raid, ids));

            Assert(RaidPlanning.DefaultSelection(s, cands, 5).SequenceEqual(normal), "a raid of five: the party as it is");
            Assert(RaidPlanning.DefaultSelection(s, cands, 3).SequenceEqual(normal.Take(3)), "smaller than the party: in party order");
            Assert(RaidPlanning.DefaultSelection(s, cands, 1).SequenceEqual(new[] { GameSession.MainId }), "a raid of one");
            Assert(RaidPlanning.DefaultSelection(s, cands, 0).SequenceEqual(new[] { GameSession.MainId }), "size 0 still takes Main");
            Assert(RaidPlanning.DefaultSelection(s, null, 10).SequenceEqual(new[] { GameSession.MainId }), "no candidates: Main alone");
            Assert(RaidPlanning.DefaultSelection(s, cands, 99).Count == cands.Count, "a bigger raid than the roster: everyone");
            Assert(RaidPlanning.DefaultSelection(new GameSession(Db, 1), cands, 10).Count == 0, "no game: nobody");
            Assert(RaidPlanning.DefaultSelection(null, cands, 10).Count == 0, "no session: nobody");

            // a role cache is used when given (the picker caches Unit.Role)
            int asked = 0;
            var cached = RaidPlanning.DefaultSelection(s, cands, 10, u => { asked++; return u.Role; });
            Assert(cached.SequenceEqual(ids) && asked > 0, "roleOf is consulted");
            var allTanks = RaidPlanning.DefaultSelection(s, cands, 10, u => UnitRole.Tank);
            Assert(allTanks.Take(normal.Count).SequenceEqual(normal) && allTanks.Count == 10, "roles only order the camp");
        }

        [Test]
        public static void DefaultSelection_SkipsAwayCompanions_AndFillsTheMissingRole()
        {
            var s = Game(72);
            s.Dismiss("bruna");
            s.Dismiss("ysolde");
            s.Dismiss("kael");   // no tank anywhere now
            var cands = s.RaidCandidates();
            var ids = RaidPlanning.DefaultSelection(s, cands, 10);
            Assert(!ids.Contains("bruna") && !ids.Contains("ysolde") && !ids.Contains("kael"), "away companions are never suggested");
            Assert(ids.SequenceEqual(Expected(s, 10)), "the suggestion: " + string.Join(",", ids));
            Assert(ids.Count == Math.Min(10, cands.Count), "as many as there are, up to the size");
            Assert(s.CannotEnterRaidReason(RaidTest.Raid, ids) == null, "and they can enter");
        }

        [Test]
        public static void LevelWarning_AndBand_ComeFromTheMap()
        {
            var raid = Db.Maps[RaidTest.Raid];
            Assert(raid.levelMin > 1 && raid.levelMax >= raid.levelMin, "the raid has a band");
            Assert(RaidPlanning.LevelWarning(raid.levelMin - 3, raid.levelMin) == $"Level {raid.levelMin - 3} · the raid asks for {raid.levelMin}", "below the band");
            Assert(RaidPlanning.LevelWarning(raid.levelMin, raid.levelMin) == null && RaidPlanning.LevelWarning(raid.levelMin + 5, raid.levelMin) == null, "at or above: no warning");
            Assert(RaidPlanning.LevelWarning(1, 0) == null, "a map without a band never warns");
            Assert(RaidPlanning.BandText(raid) == $"levels {raid.levelMin}–{raid.levelMax}", RaidPlanning.BandText(raid));
            Assert(RaidPlanning.BandText(new MapDef { levelMin = 30, levelMax = 30 }) == "level 30+", "a one-level band");
            Assert(RaidPlanning.BandText(new MapDef()) == "" && RaidPlanning.BandText(null) == "", "no band");
        }

        // ------------------------------------------------------------------ the picker's round trip

        [Test]
        public static void PickerRoundTrip_GateRequest_EnterWithTheSuggestion_LeaveRestores()
        {
            var s = Game(73);
            RaidTest.Pacify(s);
            var normal = RaidTest.PartyIds(s);
            var autoBefore = s.Roster.ToDictionary(s.MemberId, u => u.AutoPlay);
            var door = RaidTest.DoorTo(s, RaidTest.Raid);
            Assert(!s.CheckTriggers().Stop, "arm the door");
            var r = s.UpdatePartyPositions(door.pos);
            Assert(r.Stop && r.Kind == TriggerKind.RaidGate, "the walk stops at the gate");
            var req = SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidPartyRequested, RaidTest.Raid);
            Assert(req != null && req.Amount == Db.Maps[RaidTest.Raid].raidSize && !string.IsNullOrEmpty(req.Id2) && !string.IsNullOrEmpty(req.Text), "the picker's request");

            // what the picker does: suggest, the player unticks one, "Companions auto-play in this raid" stays on
            var ids = RaidPlanning.DefaultSelection(s, s.RaidCandidates(), req.Amount);
            ids.Remove(ids[ids.Count - 1]);
            Assert(s.CannotEnterRaidReason(req.Id, ids) == null, "the choice is valid");
            Assert(s.EnterRaid(req.Id, req.Id2, ids, true) == null, "enter: " + s.LastError);
            var ev = s.TakeEvents();
            var started = SessionTest.FindEvent(ev, SessionEventKind.RaidStarted, RaidTest.Raid);
            Assert(started != null && started.Text.Length > 0 && started.Amount == req.Amount, "RaidStarted, with its toast text");
            Assert(s.InRaid && s.MapId == RaidTest.Raid && RaidTest.PartyIds(s).SequenceEqual(ids), "the raid party as chosen");
            Assert(s.Party.Where(u => u != s.Main).All(u => u.AutoPlay) && !s.Main.AutoPlay, "companions auto-play; you keep your own character");
            Assert(RaidPlanning.CountRoles(s.Party).Total == ids.Count, "the frames' role count matches");

            var exit = RaidTest.ExitOf(RaidTest.Raid);
            s.SetPartyPositions(s.Map.SpawnPosition(req.Id2));
            Assert(s.UseTransition(exit.id).Ok && !s.InRaid, "out again");
            var ended = SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidEnded, RaidTest.Raid);
            Assert(ended != null && ended.Amount == 0 && ended.Text.Length > 0, "RaidEnded (not a wipe) with its toast text");
            Assert(RaidTest.PartyIds(s).SequenceEqual(normal), "the party you came with");
            foreach (var u in s.Roster) Assert(u.AutoPlay == autoBefore[s.MemberId(u)], $"{u.Name}: auto-play as before");
        }

        // ------------------------------------------------------------------ auto-play toggles

        [Test]
        public static void AutoPlaySnapshot_PutsBackEveryFlag_PetsToo()
        {
            var s = Game(74);
            var rook = CallRooksPet(s);
            s.SetAutoPlay(s.FindMember("pip"), true);
            rook.Pet.AutoPlay = true;   // the pet plays itself while Rook is yours (the pet frame's AUTO)
            var companions = s.Party.Where(u => u != s.Main).ToList();
            var before = s.PartyUnits().ToDictionary(u => u, u => u.AutoPlay);

            var snap = AutoPlaySnapshot.Capture(companions);
            Assert(snap.Count == companions.Count + 1 && snap.Contains(rook.Pet) && !snap.Contains(s.Main), "the companions and the pet");
            Assert(snap.FlagOf(s.FindMember("pip")) && !snap.FlagOf(rook) && snap.FlagOf(rook.Pet) && !snap.FlagOf(s.Main), "their flags");
            Assert(snap.Matches(), "nothing changed yet");
            foreach (var u in companions) s.SetAutoPlay(u, true);   // "Auto: all companions"
            Assert(companions.All(u => u.AutoPlay) && rook.Pet.AutoPlay && !snap.Matches(), "all on");
            snap.Restore(s.SetAutoPlay);
            Assert(snap.Matches(), "restored");
            foreach (var kv in before) Assert(kv.Key.AutoPlay == kv.Value, $"{kv.Key.Name}: {kv.Value} again");

            // "Auto-battle" on top: the whole party, then back
            var all = AutoPlaySnapshot.Capture(s.Party);
            foreach (var u in s.Party) s.SetAutoPlay(u, true);
            Assert(s.Party.All(u => u.AutoPlay), "everyone");
            all.Restore(s.SetAutoPlay);
            foreach (var kv in before) Assert(kv.Key.AutoPlay == kv.Value, $"{kv.Key.Name}: {kv.Value} after auto-battle");

            // a member who was not captured (joined since, or the snapshot is from another game) gets the fallback
            var seren = s.FindMember("seren");
            s.SetAutoPlay(seren, true);
            snap.Restore(s.SetAutoPlay, companions.Concat(new[] { seren }), false);
            Assert(!seren.AutoPlay && snap.Matches(), "the newcomer is handed back");
            snap.Restore(null);   // no setter: nothing happens
            Assert(AutoPlaySnapshot.Capture(null).Count == 0 && AutoPlaySnapshot.Capture(new Unit[] { null, rook, rook }).Count == 2, "null and duplicates");
            Assert(!snap.FlagOf(null) && !snap.Contains(null), "null is not captured");
        }

        [Test]
        public static void AutoBattle_InARaidFight_HandsEveryTurnToTheAI_ThenBack()
        {
            var s = Game(75);
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), false) == null, "enter without auto-play");
            var at = RaidTest.OpenArea(s, 18f, 12f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter(RaidTest.Raid, "enc_test_raid_ui_auto", at + new Vec2(5f, 0f), RaidTest.Pack(8)))
            {
                s.SetPartyPositions(at - new Vec2(4f, 0f));
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "fight: " + s.LastError);
                Assert(RaidPlanning.IsBigBattle(b), $"a raid fight is big ({b.Units.Count} units)");
                var party = b.Units.Where(u => u.Team == b.PlayerTeam && u.IsCharacter).ToList();
                Assert(party.All(u => !b.IsAIControlled(u)), "every hero is yours");

                var snap = AutoPlaySnapshot.Capture(s.Party);
                foreach (var u in s.Party) s.SetAutoPlay(u, true);
                Assert(b.Units.Where(u => u.Team == b.PlayerTeam).All(b.IsAIControlled), "auto-battle: every party unit (pets too) is the AI's");
                int guard = 0;
                while (!b.IsOver && b.NeedsPlayerInput == false && guard++ < 40) s.RunAITurn();
                Assert(!b.NeedsPlayerInput, "the AI plays every turn: nothing waits for you");

                snap.Restore(s.SetAutoPlay);
                Assert(party.All(u => !u.AutoPlay) && b.Units.Where(u => u.Team == b.PlayerTeam && u.Kind == UnitKind.Pet).All(p => !p.AutoPlay), "auto-battle off: every flag as before");
                // (a hero under an enemy's fear or charm stays the AI's until it wears off: Unit.IsControlled)
                if (!b.IsOver) Assert(party.Where(u => !u.IsControlled).All(u => !b.IsAIControlled(u)), "every hero is yours again");
                s.AutoResolve();
            }
        }

        // ------------------------------------------------------------------ big battles

        [Test]
        public static void BigBattle_ThresholdIsFourteenUnits()
        {
            Assert(RaidPlanning.BigBattleUnits == 14, "the contract: halved AI pacing and a compact strip above 14 units");
            Assert(!RaidPlanning.IsBigBattle(14) && RaidPlanning.IsBigBattle(15) && !RaidPlanning.IsBigBattle(0), "strictly above 14");
            Assert(!RaidPlanning.IsBigBattle((Battle)null), "no battle: not big");

            var s = Game(76);
            var at = RaidTest.OpenArea(s, 14f, 8f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter("mirefen", "enc_test_raid_ui_small", at + new Vec2(4f, 0f), RaidTest.Pack(2)))
            {
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "an ordinary fight: " + s.LastError);
                Assert(b.Units.Count <= RaidPlanning.BigBattleUnits && !RaidPlanning.IsBigBattle(b), $"a party of five against two is not big ({b.Units.Count})");
                s.AutoResolve();
            }
        }

        // ------------------------------------------------------------------ the raid wipe notice

        [Test]
        public static void RaidWipe_OneNotice_TheEventsTheToastsMerge()
        {
            var raid = Db.Maps[RaidTest.Raid];
            Assert(RaidPlanning.WipeSendsHome(Db, raid), "a defeat in the raid sends the party home");
            Assert(!RaidPlanning.WipeSendsHome(Db, Db.Maps["mirefen"]), "an ordinary map: game over");
            Assert(!RaidPlanning.WipeSendsHome(Db, null) && !RaidPlanning.WipeSendsHome(null, raid), "nothing to go on");
            Assert(!RaidPlanning.WipeSendsHome(Db, new MapDef { id = "x", raidSize = 10 }), "no return map");
            Assert(!RaidPlanning.WipeSendsHome(Db, new MapDef { id = "x", raidSize = 10, raidReturnMap = "nowhere" }), "an unknown return map");
            Assert(!RaidPlanning.WipeSendsHome(Db, new MapDef { id = "x", raidSize = 10, raidReturnMap = "raid_ashwyrm_roost" }), "a raid returning into a raid");
            Assert(RaidPlanning.WipeDetail("Rested.") == "Rested." && RaidPlanning.WipeDetail(null) == "" && RaidPlanning.WipeDetail("") == "", "other texts as they are");

            var s = Game(77);
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), true) == null, "enter");
            Assert(RaidPlanning.WipeSendsHome(s.Db, s.MapDef), "the HUD's check on the current map");
            var at = RaidTest.OpenArea(s, 16f, 10f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter(RaidTest.Raid, "enc_test_raid_ui_wipe", at + new Vec2(4f, 0f), RaidTest.Pack(6)))
            {
                s.SetPartyPositions(at - new Vec2(4f, 0f));
                s.TakeEvents();
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "a raid fight: " + s.LastError);
                RaidTest.Wipe(b);
                s.FinishBattle();
                var ev = s.TakeEvents();
                var ended = SessionTest.FindEvent(ev, SessionEventKind.CombatEnded);
                var entered = SessionTest.FindEvent(ev, SessionEventKind.MapEntered, raid.raidReturnMap);
                var raidEnded = SessionTest.FindEvent(ev, SessionEventKind.RaidEnded, RaidTest.Raid);
                var healed = SessionTest.FindEvent(ev, SessionEventKind.PartyHealed);
                Assert(ended != null && ended.Outcome == CombatEndKind.Defeat && entered != null && raidEnded != null && raidEnded.Amount == 1 && healed != null,
                    "CombatEnded (Defeat), MapEntered, RaidEnded (wipe), PartyHealed");
                Assert(ev.IndexOf(ended) < ev.IndexOf(entered) && ev.IndexOf(entered) < ev.IndexOf(raidEnded) && ev.IndexOf(raidEnded) < ev.IndexOf(healed),
                    "in that order: the banner shows first, the rest fills it in");
                Assert(SessionTest.FindEvent(ev, SessionEventKind.GameOver) == null, "no game over");
                Assert(healed.Text.StartsWith(RaidPlanning.WipeNotice, StringComparison.Ordinal), "PartyHealed opens with the wipe notice: " + healed.Text);
                string detail = RaidPlanning.WipeDetail(healed.Text);
                Assert(detail.Length > 0 && !detail.StartsWith(" ") && detail.Contains(Db.Maps[raid.raidReturnMap].name), "the banner's second line: " + detail);
                Assert(!RaidPlanning.WipeSendsHome(s.Db, s.MapDef), "home again: a defeat here would be a game over");
            }
        }
    }
}
