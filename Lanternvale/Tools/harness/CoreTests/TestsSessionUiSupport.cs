// GameSession support for the presentation layer: trigger-free party placement, owned summons, flag versioning and the
// FlagsChanged event, encounter previews for nameplates, downranked ability use and tooltip-free action bars.
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
    public static class TestsSessionUiSupport
    {
        [Test]
        public static void SetPartyPositions_PlacesWithoutTriggers()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 5, seed: 11);
            s.Recruit("kael");
            s.TakeEvents();
            var units = s.PartyUnits();
            Assert(units.Count == 2, "leader + Kael");

            // into the training dummy's radius: no combat until the next movement update
            var dummy = s.Map.FindEncounter("enc_training_dummy");
            var spot = dummy.pos + new Vec2(0f, 0.5f);
            var follower = new List<Vec2> { spot + new Vec2(-1.5f, 0.5f) };
            Assert(s.SetPartyPositions(spot, follower) == null, "placed");
            Assert(s.Mode == SessionMode.Exploration && s.Battle == null, "no encounter fired");
            Assert(Vec2.Distance(s.Leader.Position, spot) < 1e-4f && Vec2.Distance(units[1].Position, follower[0]) < 1e-4f, "positions applied");
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.RegionEntered) == 0, "no region event either");
            var tr = s.UpdatePartyPositions(spot, follower);
            Assert(tr.Stop && tr.Kind == TriggerKind.Combat && s.Battle != null, "the next movement update triggers it: " + tr);
            Assert(s.SetPartyPositions(spot) != null, "refused during combat");
            Assert(s.LeaveCombat() != null, "leave the dummy");

            // into a transition: no travel now, and none until the leader has stepped out of it
            var east = s.Map.Def.transitions.Find(t => t.id == "to_whisperwood");
            Assert(s.SetPartyPositions(east.pos) == null, "placed on the transition");
            Assert(s.MapId == "lanternvale", "no travel");
            s.UpdatePartyPositions(east.pos);
            Assert(s.MapId == "lanternvale", "still disarmed while standing in it");
            s.UpdatePartyPositions(east.pos + new Vec2(-4f, 0f));
            s.UpdatePartyPositions(east.pos);
            Assert(s.MapId == "whisperwood", "walking back in travels");

            // null followers snap to formation slots
            var p = s.Leader.Position + new Vec2(3f, 0f);
            Assert(s.SetPartyPositions(p, null) == null, "placed with formation");
            Assert(Vec2.Distance(s.PartyUnits()[1].Position, p) < 6f && Vec2.Distance(s.PartyUnits()[1].Position, p) > 0.3f, "follower in a formation slot");
        }

        [Test]
        public static void AfterBattle_LeaderInsideATransitionDoesNotTravelByStepping()
        {
            // regression (full playthrough): a rogue that finished Rotheart standing behind it — inside the stair transition —
            // travelled to the shrine on its next step. The field now resumes like a map entry: step out first.
            var s = SessionTest.NewGame(ClassId.Rogue, 5, seed: 21);
            var east = s.Map.Def.transitions.Find(t => t.id == "to_whisperwood");
            Assert(s.StartEncounter("enc_training_dummy") != null, "fight the dummy: " + s.LastError);
            s.Leader.Position = east.pos;   // combat movement ended inside the transition
            Assert(s.LeaveCombat() != null, "leave the practice fight: " + s.LastError);
            s.UpdatePartyPositions(east.pos + new Vec2(0f, 0.2f));
            Assert(s.MapId == "lanternvale", "no surprise travel on the first step after the battle");
            s.UpdatePartyPositions(east.pos + new Vec2(-4f, 0f));
            s.UpdatePartyPositions(east.pos);
            Assert(s.MapId == "whisperwood", "walking back in travels as usual");
        }

        [Test]
        public static void OwnedSummons_IsPublic_TotemsJoinTheNextBattle()
        {
            var s = SessionTest.NewGame(ClassId.Shaman, 10, seed: 12);
            Assert(s.OwnedSummons().Count == 0, "nothing placed");
            var r = s.UseAbility(s.Main, "shaman_stoneskin_totem");
            Assert(r.Ok, "stoneskin totem: " + r.Reason);
            var owned = s.OwnedSummons();
            Assert(owned.Count == 1 && owned[0].IsTotem && owned[0].Owner == s.Main, "the totem is listed");
            Assert(!s.PartyUnits().Contains(owned[0]), "totems are not party units");
            s.EnterMap("whisperwood", "from_village");
            Assert(s.OwnedSummons().Count == 0, "cleared on map change");
        }

        [Test]
        public static void FlagsVersion_And_FlagsChanged_OncePerStep()
        {
            var s = SessionTest.NewGame(ClassId.Paladin, 1, seed: 13, opening: true);
            var ev = s.TakeEvents();
            Assert(SessionTest.CountEvents(ev, SessionEventKind.FlagsChanged) == 1, "new game: one FlagsChanged");
            int v0 = s.FlagsVersion;

            // a dialogue step that sets several flags (opening: StartQuest + opening_seen) → one event, version per change
            SessionTest.Pick(s, "Grey? Like ash?");
            s.TakeEvents();
            SessionTest.SkipText(s);
            int before = s.FlagsVersion;
            int i = SessionTest.ChoiceIndex(s.Dialogue.Current, "I'll help");
            Assert(i >= 0, "help choice");
            s.ChooseDialogue(i);
            SessionTest.SkipText(s);
            ev = s.TakeEvents();
            Assert(s.Flags.IsSet("opening_seen") && s.FlagsVersion > before, $"flags changed ({before} → {s.FlagsVersion})");
            Assert(SessionTest.CountEvents(ev, SessionEventKind.FlagsChanged) == 1, $"one FlagsChanged for the step ({SessionTest.CountEvents(ev, SessionEventKind.FlagsChanged)})");
            Assert(SessionTest.FindEvent(ev, SessionEventKind.FlagsChanged).Amount == s.FlagsVersion, "Amount = FlagsVersion");
            SessionTest.Finish(s);
            s.TakeEvents();

            // several changes between ticks → one event on the next Tick; nothing when nothing changed
            int v1 = s.FlagsVersion;
            s.Flags.Set("test_a", 1);
            s.Flags.Set("test_b", 2);
            s.Flags.Clear("test_a");
            Assert(s.FlagsVersion == v1 + 3, "one version per change");
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.FlagsChanged) == 0, "not raised before the tick");
            s.Tick(0.1f);
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.FlagsChanged) == 1, "one event on the tick");
            s.Tick(0.1f);
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.FlagsChanged) == 0, "quiet tick");

            // load replaces the flags: version moves on, one event
            var json = s.SaveGame();
            int v2 = s.FlagsVersion;
            Assert(s.LoadGame(json, out var err), "load: " + err);
            Assert(s.FlagsVersion > v2 && s.Flags.IsSet("test_b"), "version bumped by the load");
            Assert(SessionTest.CountEvents(s.TakeEvents(), SessionEventKind.FlagsChanged) == 1, "one FlagsChanged after load");
            Assert(v0 > 0, "versions start above 0 after a new game");
        }

        [Test]
        public static void PreviewEncounter_NameplatesWithoutSideEffects()
        {
            var s = SessionTest.NewGame(ClassId.Mage, 7, seed: 14);
            ulong s0 = s.Rng.s0, s1 = s.Rng.s1;
            var wolves = s.PreviewEncounter("enc_pasture_wolves");
            Assert(wolves.Count == 3 && wolves.All(w => w.CreatureId == "cr_wolf" && w.Name == Db.Creature("cr_wolf").name), "three wolves");
            Assert(wolves.All(w => w.Level == 6 && w.MinLevel == 6 && w.MaxLevel == 6 && w.Rank == CreatureRank.Normal), "party level − 1, normal");
            var def = s.Map.FindEncounter("enc_pasture_wolves");
            for (int i = 0; i < 3; i++) Assert(Vec2.Distance(wolves[i].Position, def.enemies[i].pos) < 1e-4f, "positions from the encounter");

            var rot = s.PreviewEncounter("enc_rotheart");   // another map
            Assert(rot.Count == 1 && rot[0].Rank == CreatureRank.Elite && rot[0].Level == 9, $"Rotheart: elite, party level + 2 ({rot.FirstOrDefault()})");
            var dummy = s.PreviewEncounter("enc_training_dummy");
            Assert(dummy.Count == 1 && dummy[0].Passive, "the dummy is passive");
            Assert(s.PreviewEncounter("nope").Count == 0 && s.PreviewEncounter(null).Count == 0, "unknown: empty");
            Assert(s.Rng.s0 == s0 && s.Rng.s1 == s1, "no random numbers drawn");

            // the battle uses the previewed levels
            s.Flags.Set("shepherd_quest", 1);
            var b = s.StartEncounter("enc_pasture_wolves");
            Assert(b != null, "fight: " + s.LastError);
            foreach (var e in b.Units.Where(u => u.Team == Team.Enemy)) Assert(e.Level == 6, "battle level matches the preview");
        }

        [Test]
        public static void Session_DownrankedUse_And_TooltipFreeBar()
        {
            var s = SessionTest.NewGame(ClassId.Priest, 24, seed: 15);
            var me = s.Main;
            var r = s.UseAbility(me, "priest_power_word_fortitude", me, null, 1);
            Assert(r.Ok, "rank 1 fortitude: " + r.Reason);
            var aura = me.FindAura("priest_power_word_fortitude");
            Assert(aura != null && aura.Rank == 1, "rank 1 aura");
            Assert(!s.UseAbility(me, "priest_power_word_fortitude", me, null, 9).Ok, "unknown rank refused");

            var bar = s.GetAbilityBar(me);
            var lean = s.GetAbilityBar(me, includeTooltips: false);
            Assert(bar.Count == lean.Count && bar.Any(x => x.Tooltip.Length > 0) && lean.All(x => x.Tooltip == ""), "tooltip-free bar");
            var fort = lean.First(x => x.Ability.id == "priest_power_word_fortitude");
            Assert(fort.KnownRanks == me.RankOf("priest_power_word_fortitude") && fort.CanDownrank, "rank picker data");
            Assert(fort.InRangeOfAttackTarget == null, "no attack target out of combat");
        }
    }
}
