// Content tests of the lv builder (Docs/Expansion.md §8 row lv): Lanternvale's northern band (prefix lv2) and the Root
// Hollows (prefix dg1). Both maps are entered, every spawn and exit is walkable, the hidden dungeon is revealed each of
// its three ways (the region's Perception check, the listening root, Nell's hint), the four quest chains are scripted to
// completion at the band's top level (TalkTo / walk / fight via AutoResolve), and the Rootwarden is a real fight: a
// party of five wins it, a lone hero does not.
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
    public static class TestsContentLv
    {
        const string Lv = "lanternvale", Hollows = "dgn_root_hollows";
        static readonly string[] Companions = { "kael", "aldric", "seren", "rook" };

        static GameSession Game(ClassId c, int level, ulong seed, bool party = true)
        {
            var s = SessionTest.NewGame(c, level, seed);
            if (party) foreach (var id in Companions) s.Recruit(id);
            Assert(!party || s.Party.Count == 5, "a party of five");
            s.TakeEvents();
            return s;
        }

        /// <summary>Walks the leader to a point, winning every fight on the way (the party is patched up between fights)
        /// and answering encounter dialogues with <paramref name="choice"/>.</summary>
        static void Walk(GameSession s, Vec2 dest, string choice = null, float near = 1.6f)
        {
            string map = s.MapId;
            for (int leg = 0; leg < 12; leg++)
            {
                if (s.Mode == SessionMode.Combat) { SessionTest.WinBattle(s); SessionTest.Refresh(s); continue; }
                if (s.Mode == SessionMode.Dialogue)
                {
                    if (choice != null) { SessionTest.SkipText(s); int i = SessionTest.ChoiceIndex(s.Dialogue.Current, choice); if (i >= 0) s.ChooseDialogue(i); }
                    if (s.Dialogue.IsActive) SessionTest.Finish(s);
                    continue;
                }
                if (s.MapId != map || Vec2.Distance(s.Leader.Position, dest) <= near) break;
                var r = s.MoveLeader(dest);
                if (r.Trigger.Kind == TriggerKind.Travel) break;
                if (!r.Trigger.Stop && !r.Moved && Vec2.Distance(s.Leader.Position, dest) > near) break;
            }
            if (s.MapId == map) Assert(Vec2.Distance(s.Leader.Position, dest) <= near + 1.2f, $"walked to {dest} on {map} (at {s.Leader.Position})");
        }

        /// <summary>Walks up to a map NPC, talks to it and picks the scripted choices in order, then finishes.</summary>
        static void Talk(GameSession s, string npc, params string[] script)
        {
            var n = s.VisibleNpcs().FirstOrDefault(x => x.npc == npc);
            Assert(n != null, $"{npc} stands on {s.MapId}");
            Walk(s, n.pos + new Vec2(-1.2f, -0.8f), near: 1.2f);
            Assert(s.InInteractionRange(n.pos) || Vec2.Distance(s.Leader.Position, n.pos) <= GameSession.InteractionRange + 1f, $"next to {npc}");
            var r = s.TalkTo(npc);
            Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npc}: {r.Message}");
            foreach (var t in script) SessionTest.Pick(s, t);
            if (s.Dialogue.IsActive) SessionTest.Finish(s);
        }

        static void Inspect(GameSession s, string interact, params string[] script)
        {
            var p = s.MapDef.props.First(x => x.interact == interact);
            Walk(s, p.pos + new Vec2(0f, -1.4f), near: 1.4f);
            var r = s.InteractProp(interact);
            Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"{interact} starts its dialogue: {r.Message}");
            foreach (var t in script) SessionTest.Pick(s, t);
            if (s.Dialogue.IsActive) SessionTest.Finish(s);
        }

        static void Claim(GameSession s, string quest)
        {
            if (!s.PendingQuestRewards.Contains(quest)) return;
            var pick = s.QuestRewardChoices(quest).First();
            Assert(s.ClaimQuestReward(quest, pick.id) == null, $"{quest}: claim {pick.id}");
        }

        static string Stage(GameSession s, string q) => s.Quests.GetStage(q);

        // ------------------------------------------------------------------ maps

        [Test]
        public static void Maps_EnterAndEverythingReachable()
        {
            foreach (var id in new[] { Lv, Hollows })
            {
                var m = Db.Maps[id];
                var problems = TestsMapsReachable.Unreachable(m);
                Assert(problems.Count == 0, $"{id}: everything reachable from default: {string.Join("; ", problems)}");
                var s = Game(ClassId.Warrior, 12, 11, party: false);
                foreach (var sp in m.spawns)
                {
                    s.EnterMap(id, sp.id);
                    Assert(s.MapId == id && s.Nav.IsWalkable(s.Leader.Position, NavAgent.Default), $"{id}/{sp.id}: standing on walkable ground");
                }
            }
            var lv = Db.Maps[Lv];
            Assert(lv.depth == 44 && lv.paths.Count >= 6 && lv.water.Count >= 1 && lv.fill >= 0.3f && lv.fill <= 0.5f, "LV: deepened, with paths, water and fill");
            var hollows = Db.Maps[Hollows];
            Assert(hollows.dungeon && !hollows.restArea && hollows.environment == "cave" && hollows.fill >= 0.4f && hollows.fill <= 0.6f, "the Root Hollows: a cave dungeon");
            Assert(hollows.encounters.Count >= 4 && hollows.encounters.Count <= 7 && hollows.chests.Count >= 2 && hollows.chests.Count <= 3, "4-7 encounters, 2-3 chests");
            int elitePacks = hollows.encounters.Count(e => e.enemies.Count >= 2 && e.enemies.Count(x => Db.Creatures[x.creature].rank == CreatureRank.Elite) >= 2);
            Assert(elitePacks >= 2, "at least two elite packs");
            foreach (var e in hollows.encounters)
                foreach (var x in e.enemies)
                {
                    var c = Db.Creatures[x.creature];
                    int off = c.rank == CreatureRank.Elite || c.rank == CreatureRank.Boss ? 2 : 1;
                    // the Rootwarden is capped at the band's top (13): at 14 he wiped the slice's party in ~40 % of pulls
                    int cap = c.rank == CreatureRank.Boss ? 13 : 12 + off;
                    Assert(c.scaleToParty && c.levelOffset == off && c.levelCap == cap && c.levelFloor == 10 + off, $"{c.id}: +{off} levels, floor/cap of the band");
                }

            // the playthrough's southern band is untouched: no new encounter, region check or hidden/locked exit below y 15
            var baseLv = new HashSet<string> { "enc_training_dummy", "enc_pasture_wolves" };
            foreach (var e in lv.encounters.Where(e => !baseLv.Contains(e.id))) Assert(e.pos.y - e.radius > 15f, $"{e.id} stays in the northern band");
            foreach (var t in lv.transitions.Where(t => t.hidden || !string.IsNullOrEmpty(t.requireFlag))) Assert(t.pos.y > 20f, $"{t.id} stays in the north");
            foreach (var r in lv.regions.Where(r => r.check != null)) Assert(r.pos.y - r.size.y * 0.5f > 15f, $"{r.id} check stays in the north");
        }

        [Test]
        public static void Hollows_EntranceIsAmongKususNorthernRoots_AndTravelsBothWays()
        {
            var lv = Db.Maps[Lv];
            var door = lv.transitions.First(t => t.id == "to_dgn_root_hollows");
            var back = lv.spawns.First(x => x.id == "from_dgn_root_hollows").pos;
            Assert(door.hidden && door.revealFlag == "found_root_hollows" && door.marker == "cave", "a hidden cave mouth");
            Assert(door.pos.y > 30f && Math.Abs(door.pos.x - 25f) < 6f, "north of Old Kusu, clear of its canopy");
            Assert(Vec2.Distance(back, door.pos) is > 2f and < 3.5f && back.y < door.pos.y, "the spawn stands ≈ 2.5 m in front of the mouth");
            Assert(lv.props.Count(p => p.art == "prop_root_column" || p.art == "prop_root_arch") >= 3, "Kusu's roots dress the way");

            var s = Game(ClassId.Rogue, 12, 21, party: false);
            s.Map.MarkRegionChecked("reg_lv2_kusu_roots");
            s.Flags.Set("found_root_hollows", 1);
            Walk(s, back);
            Walk(s, door.pos, near: 0.1f);
            Assert(s.MapId == Hollows, "walked into the Root Hollows");
            var exit = Db.Maps[Hollows].transitions.First(t => t.id == "to_lanternvale");
            Assert(exit.pos.x < 1f && Math.Abs(exit.pos.y - 20f) < 0.01f, "the way out is on the west edge at (0.7, D/2)");
            s.MoveLeader(s.Leader.Position + new Vec2(2f, 0f));
            Walk(s, exit.pos, near: 0.1f);
            Assert(s.MapId == Lv && Vec2.Distance(s.Leader.Position, back) < 3f, "back out beside the mouth");
        }

        // ------------------------------------------------------------------ the reveal

        [Test]
        public static void Reveal_RegionCheck_RollsOnceAndRevealsOnSuccess()
        {
            var region = Db.Maps[Lv].regions.First(r => r.id == "reg_lv2_kusu_roots");
            Assert(region.check != null && region.check.skill == SkillCheck.Perception && region.check.dc >= 12 && region.check.dc <= 16 && region.checkFlag == "found_root_hollows",
                "a Perception check (DC 12-16) near the roots reveals the Hollows");
            int found = 0, missed = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var s = Game(ClassId.Hunter, 12, 400 + seed, party: false);
                Walk(s, region.pos);
                var ev = s.TakeEvents().FirstOrDefault(e => e.Kind == SessionEventKind.SkillCheck && e.Id == region.id);
                Assert(ev != null && s.Map.IsRegionChecked(region.id), $"seed {seed}: the check rolled on entry");
                Assert(ev.Check.Success == s.Flags.IsSet("found_root_hollows"), $"seed {seed}: the passage is revealed exactly on success");
                Assert(s.Map.IsTransitionVisible(s.MapDef.transitions.First(t => t.id == "to_dgn_root_hollows")) == ev.Check.Success, "visible iff found");
                if (ev.Check.Success) found++; else missed++;
                s.MoveLeader(region.pos + new Vec2(0f, -8f));
                s.TakeEvents();
                Walk(s, region.pos);
                Assert(!s.TakeEvents().Any(e => e.Kind == SessionEventKind.SkillCheck && e.Id == region.id), "rolls once per save");
            }
            Assert(found > 0 && missed > 0, $"a real check: {found} found, {missed} missed");
        }

        [Test]
        public static void Reveal_ListeningRoot_ClassOptionAndSkillChecks()
        {
            // a Hunter feels the draught: no roll needed
            var s = Game(ClassId.Hunter, 12, 31, party: false);
            s.Map.MarkRegionChecked("reg_lv2_kusu_roots");
            Inspect(s, "lv2_listening_root", "Feel for the draught");
            Assert(s.Flags.IsSet("found_root_hollows") && s.Flags.IsSet("lv2_roots_listened"), "the Hunter finds the mouth");
            var ev = s.TakeEvents();
            Assert(ev.Any(e => e.Kind == SessionEventKind.SecretFound && e.Id == "found_root_hollows"), "SecretFound is raised");
            // revealed: the root now says where the hum comes from
            Inspect(s, "lv2_listening_root");
            Assert(s.Dialogue.IsActive == false, "the short version ends");

            // a Warrior must roll (Perception, Nature or Religion); 'Just listen' never reveals
            var w = Game(ClassId.Warrior, 12, 32, party: false);
            w.Map.MarkRegionChecked("reg_lv2_kusu_roots");
            Inspect(w, "lv2_listening_root", "Just listen.");
            Assert(w.Flags.IsSet("lv2_roots_listened") && !w.Flags.IsSet("found_root_hollows"), "listening alone does not find the door");
            Assert(w.StartDialogue("dlg_lv2_listening_root", "lv2_listening_root"), "listen again");
            SessionTest.SkipText(w);
            var v = w.Dialogue.Current;
            Assert(SessionTest.ChoiceIndex(v, "Listen for where") >= 0 && SessionTest.ChoiceIndex(v, "Read the root") >= 0 && SessionTest.ChoiceIndex(v, "Feel for the draught") < 0,
                "skill checks offered, no Hunter option for a Warrior: " + SessionTest.Describe(v));
            w.Dialogue.End();
        }

        // ------------------------------------------------------------------ the quest chains (band top: 12)

        [Test]
        public static void SingingRoots_NellsHintRevealsTheHollows()
        {
            var s = Game(ClassId.Warrior, 12, 41);
            s.Map.MarkRegionChecked("reg_lv2_kusu_roots");     // nobody notices the draught on the way
            // offered only after Moppet is home
            Assert(s.StartDialogue("dlg_child_nell", "child_nell"), "Nell");
            SessionTest.SkipText(s);
            Assert(SessionTest.ChoiceIndex(s.Dialogue.Current, "Moppet looks worried") < 0, "not before Moppet is found");
            s.Dialogue.End();
            s.Quests.Start("sq_spirit_friend");
            s.Quests.Complete("sq_spirit_friend");

            Talk(s, "child_nell", "Moppet looks worried", "I'll go and listen.");
            Assert(Stage(s, "lv2_singing_roots") == "listen", "listen at the roots");
            Inspect(s, "lv2_listening_root", "Just listen.");
            Assert(Stage(s, "lv2_singing_roots") == "tell" && !s.Flags.IsSet("found_root_hollows"), "heard, but not found");
            Talk(s, "child_nell", "I listened to the roots", "I heard it, but");
            Assert(s.Quests.IsCompleted("lv2_singing_roots") && s.Flags.IsSet("found_root_hollows"), "Moppet tells where the door is");
            Assert(s.CountItem("lv2_moppets_spare_bell") == 1, "Moppet's spare bell");
            Assert(s.UseTransition("to_dgn_root_hollows").Ok && s.MapId == Hollows, "the Hollows are open");
        }

        [Test]
        public static void Orchard_ThievesCaught_AndTheRootlingsStay()
        {
            var s = Game(ClassId.Shaman, 12, 51);
            var enc = s.MapDef.encounters.First(e => e.id == "enc_lv2_orchard_rootlings");
            Talk(s, "lv2_hana_pipp", "Somebody else's?", "I'll find your thief.");
            Assert(s.Quests.IsActive("lv2_orchard_thieves") && !s.Map.IsEncounterAvailable(enc), "accepted; the thieves are not there yet");
            Inspect(s, "lv2_spilled_crate", "Wait among the trees");
            Assert(Stage(s, "lv2_orchard_thieves") == "rootlings" && s.Map.IsEncounterAvailable(enc), "tracked: the thieves appear");
            Walk(s, enc.pos);
            Assert(s.Map.IsEncounterDone(enc) && Stage(s, "lv2_orchard_thieves") == "report", "the thieves are dealt with");
            Talk(s, "lv2_hana_pipp", "Rootlings", "The earth spirits ask you to share");
            Assert(s.Quests.IsCompleted("lv2_orchard_thieves") && s.Flags.IsSet("lv2_rootling_friend"), "a row for the rootlings");
            Claim(s, "lv2_orchard_thieves");
            Assert(s.VisibleNpcs().Any(n => n.npc == "lv2_sprout"), "Sprout now lives in the orchard");

            // the other way: pests are pests
            var w = Game(ClassId.Warrior, 12, 52);
            Talk(w, "lv2_hana_pipp", "Somebody else's?", "I'll find your thief.");
            Inspect(w, "lv2_spilled_crate", "Wait among the trees");
            Walk(w, enc.pos);
            Talk(w, "lv2_hana_pipp", "Some root-creatures");
            Assert(w.Quests.IsCompleted("lv2_orchard_thieves") && !w.Flags.IsSet("lv2_rootling_friend") && !w.VisibleNpcs().Any(n => n.npc == "lv2_sprout"), "no rootling row");
        }

        [Test]
        public static void LanternOil_FoughtOrTalkedDown()
        {
            foreach (var (cls, choice) in new[] { (ClassId.Warrior, "Put them down"), (ClassId.Hunter, "smoked boar haunch") })
            {
                var s = Game(cls, 12, 61);
                s.Quests.Start("sq_wicks");
                s.Quests.Complete("sq_wicks");
                Talk(s, "lamplighter_tobben", "Is anything else going missing", "I'll find your oil.");
                Assert(Stage(s, "lv2_lantern_oil") == "tracks", $"{cls}: Tobben's oil");
                Walk(s, s.MapDef.regions.First(r => r.id == "reg_lv2_oil_tracks").pos);
                Assert(Stage(s, "lv2_lantern_oil") == "scouts", "the trail on the west road");
                var enc = s.MapDef.encounters.First(e => e.id == "enc_lv2_duskmane_scouts");
                s.TakeEvents();
                bool fought = false;
                s.EventRaised += e => { if (e.Kind == SessionEventKind.CombatStarted) fought = true; };
                Walk(s, enc.pos, choice);
                Assert(s.Flags.IsSet("lv2_scouts_dealt") && s.Map.IsEncounterDone(enc) && Stage(s, "lv2_lantern_oil") == "return", $"{cls}: the scouts are dealt with");
                Assert(fought == (cls != ClassId.Hunter), $"{cls}: {(cls == ClassId.Hunter ? "talked down" : "fought")}");
                Talk(s, "lamplighter_tobben", "About your lamp oil", "won't be back");
                Assert(s.Quests.IsCompleted("lv2_lantern_oil"), $"{cls}: the oil is home");
                Claim(s, "lv2_lantern_oil");
            }
        }

        [Test]
        public static void HeartUnderTheRoots_FullChain()
        {
            var s = Game(ClassId.Paladin, 13, 71);
            s.Quests.Start("mq_lanterns");
            s.Quests.Complete("mq_lanterns");
            s.Flags.Set("found_root_hollows", 1);
            s.Map.MarkRegionChecked("reg_lv2_kusu_roots");
            Walk(s, Db.Maps[Lv].transitions.First(t => t.id == "to_dgn_root_hollows").pos, near: 0.1f);
            Assert(s.MapId == Hollows, "into the Root Hollows");

            Talk(s, "dg1_hotaru", "Before what?", "I'll free him.");
            Assert(Stage(s, "dg1_heart_of_kusu") == "warden", "Hotaru's quest");
            var boss = s.MapDef.encounters.First(e => e.id == "enc_dg1_rootwarden");
            Walk(s, boss.pos);
            Assert(s.Map.IsEncounterDone(boss) && Stage(s, "dg1_heart_of_kusu") == "hotaru", "the Rootwarden is freed");
            Assert(s.CountItem("dg1_ash_seed") == 1, "the Ash-Seed dropped");
            var lantern = s.MapDef.chests.First(c => c.id == "chest_dg1_root_lantern");
            Assert(s.Map.IsChestAvailable(lantern), "the Root Lantern's cache appears");

            Talk(s, "dg1_hotaru", "Here it is", "I'll take it to Elder Maru");
            Assert(Stage(s, "dg1_heart_of_kusu") == "elder", "to the Elder");
            Walk(s, s.MapDef.transitions.First(t => t.id == "to_lanternvale").pos, near: 0.1f);
            Assert(s.MapId == Lv, "back in Lanternvale");
            Talk(s, "elder_maru", "I've brought something up from under Old Kusu's roots");
            Assert(s.Quests.IsCompleted("dg1_heart_of_kusu") && s.CountItem("dg1_ash_seed") == 0, "the seed is sealed in Kusu's shrine box");
            Claim(s, "dg1_heart_of_kusu");
        }

        [Test]
        public static void ElderOffersTheEmberRoad_WithLore()
        {
            var s = Game(ClassId.Priest, 12, 81, party: false);
            s.Quests.Start("mq_lanterns");
            s.Quests.Complete("mq_lanterns");
            Assert(s.StartDialogue("dlg_elder_maru", "elder_maru"), "Elder");
            SessionTest.Pick(s, "Ash, drifting down from the north");
            SessionTest.Pick(s, "What lies north");
            SessionTest.Pick(s, "I'll take the west road.");
            SessionTest.Finish(s);
            Assert(s.Quests.IsActive("mq2_ember_road") && s.CountItem("potion_healing") >= 2, "the Ember Road starts, with provisions");
            var q = Db.Quests["mq2_ember_road"];
            var low = Game(ClassId.Priest, 11, 82, party: false);
            low.Quests.Start("mq_lanterns");
            low.Quests.Complete("mq_lanterns");
            Assert(low.StartDialogue("dlg_elder_maru", "elder_maru"), "Elder at 11");
            SessionTest.SkipText(low);
            Assert(SessionTest.ChoiceIndex(low.Dialogue.Current, "Ash, drifting") < 0, "not offered below level 12");
            low.Dialogue.End();
        }

        // ------------------------------------------------------------------ the Rootwarden

        static (BattleOutcome outcome, int rounds, float hpLeft) BossFight(ClassId cls, int level, ulong seed, bool party)
        {
            var s = Game(cls, level, seed, party);
            s.EnterMap(Hollows, "default");
            foreach (var e in s.MapDef.encounters.Where(e => e.id != "enc_dg1_rootwarden")) s.Map.MarkEncounterDone(e.id);
            var boss = s.MapDef.encounters.First(e => e.id == "enc_dg1_rootwarden");
            for (int i = 0; i < 6 && s.Mode != SessionMode.Combat; i++) s.MoveLeader(boss.pos);
            Assert(s.Mode == SessionMode.Combat && s.Battle != null, "the Rootwarden fight starts");
            var outcome = s.AutoResolve(80);
            float max = 0f, hp = 0f;
            foreach (var u in s.Party) { max += u.MaxHealth; hp += Math.Max(0f, u.Health); }
            return (outcome, s.Battle.Round, hp / Math.Max(1f, max));
        }

        [Test]
        public static void Rootwarden_WinnableByAPartyOfFive_NotTrivial()
        {
            var boss = Db.Creatures["cr_dg1_rootwarden"];
            Assert(boss.rank == CreatureRank.Boss && boss.sprite == "cr_dg1_rootwarden" && Math.Abs(boss.size - 3.4f) < 0.01f, "the Rootwarden: Boss, its own model at 3.4 m");
            var lt = Db.LootTables[boss.lootTable];
            Assert(lt.entries.Any(e => e.pool != null && e.pool.Length > 0 && e.chance >= 100 && e.pool.All(id => Db.Item(id).quality == Quality.Rare)), "a guaranteed Rare");
            Assert(lt.entries.Any(e => e.pool != null && e.pool.Length > 0 && e.chance < 100 && e.pool.All(id => Db.Item(id).quality == Quality.Epic)), "an Epic chance");

            int wins = 0, rounds = 0;
            float hpLeft = 0f;
            var classes = new[] { ClassId.Paladin, ClassId.Mage, ClassId.Rogue };
            for (int i = 0; i < classes.Length; i++)
            {
                var r = BossFight(classes[i], 13, (ulong)(900 + i), party: true);
                Console.WriteLine($"    Rootwarden vs {classes[i]} + 4 at L13: {r.outcome} after {r.rounds} rounds, party HP left {r.hpLeft:P0}");
                if (r.outcome == BattleOutcome.Victory) wins++;
                rounds += r.rounds;
                hpLeft += r.hpLeft;
            }
            Assert(wins >= 2, $"a fitting party wins ({wins}/3)");
            Assert(rounds >= 3 * 4, $"not a walkover: {rounds / 3f:0.#} rounds on average");
            Assert(hpLeft / 3f < 0.85f, $"the party is hurt: {hpLeft / 3f:P0} HP left on average");

            var solo = BossFight(ClassId.Warrior, 12, 950, party: false);
            Console.WriteLine($"    Rootwarden vs a lone Warrior at L12: {solo.outcome} after {solo.rounds} rounds");
            Assert(solo.outcome != BattleOutcome.Victory, "a lone hero cannot solo it");
        }

        /// <summary>The Duskmane Lookout shoots the bow it is so proud of: a bowstring twang, an arrow in flight and an arrow's
        /// thud (review: its fx_bolt projectile played the crossbow latch and a bolt thump).</summary>
        [Test]
        public static void DuskmaneLookout_SoundsLikeABow()
        {
            var lookout = RulesTestUtil.Mob("cr_lv2_duskmane_lookout", 12);
            Assert(CombatSounds.ReleaseOf(lookout, School.Physical) == "bow", "the bow's release: " + CombatSounds.ReleaseOf(lookout, School.Physical));
            Assert(CombatSounds.AttackLayerOf(lookout, false, true) == "hit_arrow", "an arrow lands: " + CombatSounds.AttackLayerOf(lookout, false, true));
            Assert(CombatSounds.FlightWhooshOf(lookout, School.Physical), "the arrow whooshes through the air");
        }

        /// <summary>XP pacing of the deepened first slice (Docs/Expansion.md §1/§8): the slice ends at 12; each north band
        /// with its hidden dungeon (bands 11-13, 12-14, 13-15) adds about one level, so a party that fights every north
        /// encounter and dungeon and hands in their quests reaches Amberfield (12-18) around 15. Review: they paid ~81k XP
        /// (L12 -> L18, nearly the whole Amberfield band) before the kill and quest XP were scaled down.</summary>
        [Test]
        public static void NorthBandsAndEarlyDungeons_AboutALevelEach()
        {
            int level = 12; long xp = 0;
            void Give(float amount)
            {
                xp += (long)Math.Round(amount);
                while (xp >= Progression.XpToNextLevel(Db, level)) { xp -= Progression.XpToNextLevel(Db, level); level++; }
            }
            var plan = new[] { ("lanternvale", "dgn_root_hollows", new[] { "lv2_", "dg1_" }), ("whisperwood", "dgn_mossdeep", new[] { "ww2_" }), ("shrine", "dgn_lantern_catacombs", new[] { "sh2_" }) };
            var log = new List<string>();
            foreach (var (map, dungeon, prefixes) in plan)
            {
                int l0 = level;
                var fights = Db.Maps[map].encounters.Where(e => e.pos.y > 15f).Concat(Db.Maps[dungeon].encounters).ToList();
                Assert(fights.Count >= 7, $"{map}: the north band's and {dungeon}'s fights");
                foreach (var e in fights)
                {
                    float sum = 0f;
                    foreach (var x in e.enemies)
                    {
                        var c = Db.Creatures[x.creature];
                        int ml = UnitFactory.CreatureLevel(c, x.level, level, null);
                        sum += Formulas.MobXp(level, ml) * Progression.XpRankMult(c.rank) * c.xpMult * Progression.XpRate(Db, level);
                        if (map == "shrine" || map == "whisperwood" || map == "lanternvale")
                            Assert(!c.scaleToParty || c.levelCap > 0, $"{e.id}: {c.id} is held to its band (levelCap)");
                    }
                    Give(sum);
                }
                foreach (var q in Db.Quests.Values.Where(q => prefixes.Any(p => q.id.StartsWith(p, StringComparison.Ordinal))))
                {
                    foreach (var st in q.stages)
                        if (st.onComplete != null)
                            foreach (var o in st.onComplete.Where(o => o.type == OutcomeType.GiveXP)) Give(Progression.QuestXp(Db, o.amount, level));
                    if (q.rewards != null) Give(Progression.QuestXp(Db, q.rewards.xp, level));
                }
                log.Add($"{map}+{dungeon}: L{l0} -> L{level}");
                Assert(level - l0 <= 2, $"{map} north band + {dungeon} add about a level (L{l0} -> L{level})");
            }
            Console.WriteLine($"    {string.Join("; ", log)} (+{xp} XP into L{level})");
            Assert(level >= 14 && level <= 15, $"the north bands and their dungeons take the slice's L12 to about 15, not past Amberfield's band (L{level})");
        }

        /// <summary>The party the slice really gives (TestsSessionFullPlaythrough's run: quest rewards and drops, about 20
        /// of 45 armour/weapon slots empty, item level ~8), raised to the top of the dungeon's band (13) and walked
        /// straight to the Rootwarden: veteran gear hides how hard he hits such a party, and a wipe here is a game over.
        /// Review: at levelCap 14 / damageMult 1.45 this party won 5 of these 16 pulls.</summary>
        [Test]
        public static void Rootwarden_TheSlicePartyWinsAtTheBand()
        {
            var t = typeof(TestsSessionFullPlaythrough).GetNestedType("Playthrough", System.Reflection.BindingFlags.NonPublic);
            Assert(t != null, "TestsSessionFullPlaythrough.Playthrough");
            int wins = 0, pulls = 0, rounds = 0;
            var parts = new List<string>();
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Paladin, ClassId.Hunter, ClassId.Rogue, ClassId.Priest, ClassId.Shaman, ClassId.Mage, ClassId.Warlock })
            {
                var p = Activator.CreateInstance(t, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                                                 null, new object[] { c, 4100 + (ulong)c }, null);
                try
                {
                    t.GetMethod("Play").Invoke(p, new object[] { TestsSessionFullPlaythrough.BridgeRoute.Fight, TestsSessionFullPlaythrough.WicksRoute.Fight, TestsSessionFullPlaythrough.KeeperRoute.Letter });
                }
                catch (System.Reflection.TargetInvocationException e) { throw e.InnerException ?? e; }
                var save = ((GameSession)t.GetField("S").GetValue(p)).SaveGame();
                var res = new List<string>();
                for (ulong k = 0; k < 2; k++)
                {
                    var s = new GameSession(Db, 77 + k);
                    Assert(s.LoadGame(save, out var err), err);
                    while (s.Main.Level < 13) s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
                    Progression.LearnAllAvailable(s.Main);
                    s.Rng.s0 = 0x9E3779B97F4A7C15UL * (k + 1); s.Rng.s1 = 0xBF58476D1CE4E5B9UL ^ k;
                    s.Settings.CompanionAutoPlay = true;
                    foreach (var u in s.Roster) s.SetAutoPlay(u, true);
                    s.EnterMap(Hollows, "default");
                    foreach (var e in s.MapDef.encounters.Where(e => e.id != "enc_dg1_rootwarden")) s.Map.MarkEncounterDone(e.id);
                    foreach (var u in s.PartyUnits()) u.RestoreFull();
                    var be = s.Map.FindEncounter("enc_dg1_rootwarden");
                    s.SetPartyPositions(be.pos + new Vec2(-be.radius - 3f, -1.5f));
                    var b = s.StartEncounter("enc_dg1_rootwarden");
                    Assert(b != null, "the Rootwarden fight starts: " + s.LastError);
                    var o = s.AutoResolve(100);
                    pulls++; rounds += b.Round;
                    if (o == BattleOutcome.Victory) wins++;
                    res.Add($"{(o == BattleOutcome.Victory ? "won" : "lost")} in {b.Round}");
                }
                parts.Add($"{c} {string.Join(", ", res)}");
            }
            Console.WriteLine($"    Rootwarden vs the slice's party at L13: {wins}/{pulls} won ({string.Join("; ", parts)})");
            Assert(wins >= 11, $"the slice's party usually wins at the band ({wins}/{pulls})");
            Assert(rounds >= pulls * 8, $"still a long fight: {rounds / (float)pulls:0.#} rounds on average");
        }
    }
}
