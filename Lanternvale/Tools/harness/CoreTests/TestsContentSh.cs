// The deepened Old Lantern Shrine (prefix sh2) and the Lantern Catacombs (prefix dg3), Docs/Expansion.md §8 row sh:
// every spawn and exit of both maps reachable, the north band kept off the 1-12 playthrough's southern routes, the
// catacombs revealed by the High Terrace's Perception check, by the cracked plinth's inspect dialogue and by Aiko's
// reading of Ishiro's ledger; the side quest "The Keepers' Rest" played through the real GameSession at the band's top
// level (walking, talking, the chests, the bound lanterns, the chapel guard's parley, the boss fought by AutoResolve and
// both endings of the lantern choice); Hazama, the Lantern Lich, winnable by a fitting party of 5 but not trivial; the
// XP the dungeon is worth.
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
    public static class TestsContentSh
    {
        const string Shrine = "shrine", Cat = "dgn_lantern_catacombs", Q = "sh2_keepers_rest", Aiko = "sh2_novice_aiko";
        const string Secret = "found_lantern_catacombs", Entrance = "to_dgn_lantern_catacombs";

        /// <summary>A tank, a healer and three damage dealers: a Warrior main with Aldric, Seren, Lys and Rook.</summary>
        static readonly string[] Party = { "aldric", "seren", "lys", "rook" };

        static GameSession Game(int level = 15, ClassId main = ClassId.Warrior, ulong seed = 31, bool party = true, bool lit = true)
        {
            var s = SessionTest.NewGame(main, level, seed);
            if (party) foreach (var id in Party) s.Recruit(id);
            if (lit)
            {
                s.Flags.Set("warden_defeated", 1);
                s.Flags.Set("heart_lantern_lit", 1);
            }
            s.EnterMap(Shrine, "default");
            s.TakeEvents();
            return s;
        }

        /// <summary>Every encounter of the map already dealt with (their done flags set).</summary>
        static void Calm(GameSession s, string mapId)
        {
            foreach (var e in Db.Maps[mapId].encounters)
                s.Flags.Set(string.IsNullOrEmpty(e.doneFlag) ? WorldRules.EncounterDoneFlag(e.id) : e.doneFlag, 1);
        }

        // ================================================================== a scripted walker (as TestsSessionFullPlaythrough)

        sealed class Run
        {
            public readonly GameSession S;
            public int Fights;
            /// <summary>Encounter dialogue scripts by owner (the encounter id).</summary>
            public readonly Dictionary<string, string[]> Scripts = new Dictionary<string, string[]>();
            public readonly List<SessionEvent> Events = new List<SessionEvent>();

            public Run(GameSession s) { S = s; }

            void Collect() => Events.AddRange(S.TakeEvents());

            public void Go(Vec2 dest, float near = 0.8f)
            {
                for (int leg = 0; leg < 40; leg++)
                {
                    Assert(!S.IsGameOver, "game over on " + S.MapId);
                    Collect();
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue)
                    {
                        var owner = S.Dialogue.OwnerId ?? "";
                        var script = Scripts.TryGetValue(owner, out var sc) ? sc : new string[0];
                        Scripts[owner] = new string[0];
                        Converse(script);
                        continue;
                    }
                    Loot();
                    if (Vec2.Distance(S.Leader.Position, dest) <= near) return;
                    var r = S.MoveLeader(dest);
                    Collect();
                    if (r.Trigger.Stop)
                    {
                        Assert(r.Trigger.Kind != TriggerKind.Locked, "a locked transition on the way: " + r.Trigger.Id);
                        if (r.Trigger.Kind == TriggerKind.Travel) return;
                        continue;
                    }
                    if (Vec2.Distance(S.Leader.Position, dest) <= near + 0.3f) return;
                    if (!r.Moved) break;
                }
                Assert(Vec2.Distance(S.Leader.Position, dest) <= near + 1.5f, $"reached {dest} on {S.MapId} (at {S.Leader.Position})");
            }

            public void Fight()
            {
                Fights++;
                SessionTest.WinBattle(S, 80);
                Collect();
                Loot();
                SessionTest.Refresh(S);   // a rest between fights
            }

            public void Loot() { if (S.PendingLoot != null) S.TakeAllLoot(); }

            /// <summary>Plays the running dialogue: the script's choices in order (an item may list "a|b"), else an exit.</summary>
            public void Converse(params string[] script)
            {
                var todo = new List<string>(script);
                for (int guard = 0; guard < 200 && S.Dialogue.IsActive; guard++)
                {
                    var v = S.Dialogue.Current;
                    if (v == null) break;
                    if (v.CanContinue) { S.ContinueDialogue(); continue; }
                    int pick = -1;
                    for (int k = 0; k < todo.Count && pick < 0; k++)
                    {
                        foreach (var alt in todo[k].Split('|'))
                        {
                            pick = SessionTest.ChoiceIndex(v, alt);
                            if (pick >= 0) break;
                        }
                        if (pick >= 0) todo.RemoveAt(k);
                    }
                    if (pick < 0)
                        foreach (var leave in new[] { "Goodbye", "(Continue.)", "(Step back.)", "(Leave", "Never mind" })
                            if ((pick = SessionTest.ChoiceIndex(v, leave)) >= 0) break;
                    if (pick < 0 && todo.Count == 0 && v.Choices.Count > 0) pick = v.Choices[v.Choices.Count - 1].Index;   // the closing line
                    Assert(pick >= 0, $"no choice to pick at {v.DialogueId}/{v.NodeId}: {SessionTest.Describe(v)} (wanted: {string.Join(" | ", todo)})");
                    S.ChooseDialogue(pick);
                }
                Assert(!S.Dialogue.IsActive, "dialogue finished");
                Assert(todo.Count == 0, $"every scripted choice was offered (left: {string.Join(" | ", todo)})");
                Collect();
                if (S.Mode == SessionMode.Combat) Fight();
                Loot();
            }

            public void TalkTo(string npcId, params string[] script)
            {
                var npc = S.VisibleNpcs().FirstOrDefault(n => n.npc == npcId);
                Assert(npc != null, $"{npcId} is on {S.MapId}");
                Go(npc.pos + new Vec2(-1.2f, -0.8f), 1.2f);
                var r = S.TalkTo(npcId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npcId}: {r.Message}");
                Converse(script);
            }

            public void Inspect(string interactId, params string[] script)
            {
                var p = S.Map.Def.props.FirstOrDefault(x => x.interact == interactId);
                Assert(p != null, $"prop {interactId} on {S.MapId}");
                Go(p.pos + new Vec2(0f, -1.4f), 1.2f);
                var r = S.InteractProp(interactId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"inspect {interactId}: {r.Message}");
                Converse(script);
            }

            public void Chest(string id)
            {
                var c = S.Map.Def.chests.First(x => x.id == id);
                Go(c.pos + new Vec2(-1.1f, 0f), 1.2f);
                var r = S.OpenChest(id);
                Assert(r.Ok && r.Kind == InteractKind.Loot, $"open {id}: {r.Message}");
                Loot();
            }

            public void WalkInto(string encId)
            {
                var enc = S.Map.FindEncounter(encId);
                Assert(enc != null && S.Map.IsEncounterAvailable(enc), $"{encId} is on {S.MapId} and available");
                SessionTest.Refresh(S);
                for (int leg = 0; leg < 12 && !S.Map.IsEncounterDone(enc); leg++) Go(enc.pos, 0.5f);
                Assert(S.Map.IsEncounterDone(enc), $"{encId} resolved");
            }

            public void Travel(string transitionId, string expectMap)
            {
                var t = S.Map.Def.transitions.First(x => x.id == transitionId);
                Assert(S.Map.IsTransitionVisible(t), $"{transitionId} is visible");
                Go(t.pos, 0.1f);
                if (S.MapId != expectMap)
                {
                    S.MoveLeader(t.pos + new Vec2(0f, -3f));
                    Go(t.pos, 0.1f);
                }
                Assert(S.MapId == expectMap, $"travelled to {expectMap} (on {S.MapId})");
                Collect();
            }
        }

        // ================================================================== maps

        [Test]
        public static void Maps_EverySpawnEntered_EverythingReachable()
        {
            foreach (var id in new[] { Shrine, Cat })
            {
                var m = Db.Maps[id];
                var problems = TestsMapsReachable.Unreachable(m);
                Assert(problems.Count == 0, $"{id}: everything reachable with every flag set:\n    " + string.Join("\n    ", problems));
                var s = SessionTest.NewGame(ClassId.Mage, 14, 5);
                foreach (var sp in m.spawns)
                {
                    s.EnterMap(id, sp.id);
                    Assert(s.MapId == id && s.Nav.IsWalkable(s.Leader.Position, 0.3f), $"{id}/{sp.id}: on walkable ground at {s.Leader.Position}");
                }
                foreach (var t in m.transitions)
                    Assert(Db.Maps.ContainsKey(t.targetMap) && Db.Maps[t.targetMap].spawns.Any(x => x.id == t.targetSpawn), $"{id}/{t.id} leads somewhere");
            }
            var shrine = Db.Maps[Shrine];
            Assert(shrine.depth == 40 && shrine.paths.Count >= 3 && shrine.water.Count >= 1 && shrine.fill >= 0.3f && shrine.fill <= 0.5f,
                "the shrine: 40 m deep, a switchback of paths, the Moon Pool, outdoor fill");
            var cat = Db.Maps[Cat];
            Assert(cat.width == 56 && cat.depth == 44 && cat.dungeon && !cat.restArea && cat.environment == "crypt" && cat.fill >= 0.4f && cat.fill <= 0.6f,
                "the catacombs: 56 × 44 crypt, a hidden dungeon without rest, indoor fill");
            var approach = shrine.regions.First(r => r.id == "reg_shrine_approach");
            Assert(MapRuntime.RectContains(approach.pos, approach.size, shrine.spawns.First(x => x.id == "default").pos), "reg_shrine_approach still covers the arrival spawn");
        }

        [Test]
        public static void ShrineNorthBand_StaysOffTheSouthernRoutes()
        {
            var m = Db.Maps[Shrine];
            foreach (var e in m.encounters.Where(e => e.id.StartsWith("enc_sh2_", StringComparison.Ordinal)))
            {
                Assert(e.pos.y - e.radius >= 15f, $"{e.id}: in the north band, clear of the southern routes");
                foreach (var en in e.enemies) Assert(en.pos.y >= 15f, $"{e.id}: enemies stand north of y 15");
            }
            var t = m.transitions.First(x => x.id == Entrance);
            Assert(t.hidden && t.revealFlag == Secret && t.marker == "stairs" && t.pos.y >= 15f + MapRuntime.TransitionSize(t).y, "the hidden stair is in the north band");
            var spawn = m.spawns.First(x => x.id == "from_" + Cat);
            Assert(Vec2.Distance(spawn.pos, t.pos) >= 2f && Vec2.Distance(spawn.pos, t.pos) <= 3.2f && spawn.pos.y < t.pos.y, "the return spawn stands about 2.5 m in front of the stair");
            foreach (var p in m.props.Where(p => p.pos.y < 15f && !string.IsNullOrEmpty(p.interact)))
                Assert(!p.interact.StartsWith("sh2_", StringComparison.Ordinal), $"{p.interact}: north band only");
        }

        // ================================================================== the reveal

        [Test]
        public static void Reveal_HighTerracePerceptionCheck_OncePerSave_AfterTheHeartIsLit()
        {
            // before the Heart is lit the check is not armed
            var dark = Game(13, ClassId.Rogue, 3, party: false, lit: false);
            var region = dark.Map.Def.regions.First(r => r.id == "reg_sh2_high_terrace");
            Assert(region.check.skill == SkillCheck.Perception && region.check.dc >= 12 && region.check.dc <= 16 && region.checkFlag == Secret, "Perception DC 12-16 reveals it");
            Assert(!dark.Map.IsRegionCheckDue(region, region.pos), "no roll while the lanterns are dark");

            int found = 0, missed = 0;
            for (ulong seed = 1; seed <= 24 && (found == 0 || missed == 0); seed++)
            {
                var s = Game(13, ClassId.Rogue, seed, party: false);
                var t = s.Map.Def.transitions.First(x => x.id == Entrance);
                Assert(!s.Map.IsTransitionVisible(t) && !s.UseTransition(Entrance).Ok, "the stair is hidden at first");
                // arrive on the High Terrace from the Lookout side (the walker is the region test, not the route)
                s.EnterMap(Shrine, "from_" + Cat);
                var ev = s.TakeEvents();
                var check = ev.FirstOrDefault(e => e.Kind == SessionEventKind.SkillCheck && e.Id == region.id);
                Assert(check != null, $"seed {seed}: the High Terrace rolls Perception on arrival");
                if (s.Flags.IsSet(Secret))
                {
                    found++;
                    Assert(ev.Any(e => e.Kind == SessionEventKind.SecretFound && e.Id2 == Entrance), "SecretFound for the stair");
                    Assert(s.Map.IsTransitionVisible(t), "the stair shows");
                }
                else missed++;
                s.EnterMap(Shrine, "default");
                s.EnterMap(Shrine, "from_" + Cat);
                Assert(!s.TakeEvents().Any(e => e.Kind == SessionEventKind.SkillCheck && e.Id == region.id), "the check rolls once per save");
            }
            Assert(found > 0 && missed > 0, $"the roll can go either way (found {found}, missed {missed})");
        }

        [Test]
        public static void Reveal_CrackedPlinth_InvestigationReligionOrAClassTouch()
        {
            var plinth = Db.Maps[Shrine].props.First(p => p.interact == "sh2_cracked_plinth");
            var dlg = Db.Dialogues[plinth.dialogue];
            var checks = dlg.nodes.SelectMany(n => n.choices).Where(c => c.check != null).Select(c => c.check.skill).ToList();
            Assert(checks.Contains(SkillCheck.Investigation) && checks.Contains(SkillCheck.Religion), "Investigation and Religion checks at the plinth");

            // a rogue feels the latch
            // (before the Heart is lit: no passive roll on the High Terrace gets there first; the party fights its way up)
            var s = Game(13, ClassId.Rogue, 9, lit: false);
            var run = new Run(s);
            run.Inspect("sh2_cracked_plinth", "(Run your fingers along the crack");
            Assert(s.Flags.IsSet(Secret) && run.Events.Any(e => e.Kind == SessionEventKind.SecretFound && e.Id2 == Entrance), "the rogue opens the stair");

            // anyone carrying Keeper Ishiro's prayer beads can count the marks
            s = Game(13, ClassId.Mage, 9, lit: false);
            Calm(s, Shrine);
            s.GiveItem("ishiros_prayer_beads", 1);
            run = new Run(s);
            run.Inspect("sh2_cracked_plinth", "Count the marks on Ishiro's prayer beads");
            Assert(s.Flags.IsSet(Secret), "the beads open the stair");

            // once open, the plinth only describes the stair
            run.Inspect("sh2_cracked_plinth");
            run.Travel(Entrance, Cat);
            Assert(s.Leader.Position.x < 6f, "arrived at the catacombs' west stair");
        }

        // ================================================================== the side quest

        [Test]
        public static void KeepersRest_PlaysThrough_FreeingTheKeepers()
        {
            var s = Game(15, ClassId.Warrior, 31);
            Assert(s.Party.Count == 5, "a party of 5");
            var run = new Run(s);
            Assert(s.QuestMarkerOf(Aiko).Kind == QuestMarker.Available && s.QuestMarkerOf(Aiko).QuestId == Q, "Aiko offers the quest (yellow !)");

            run.TalkTo(Aiko, "I'll fetch the ledger");
            Assert(s.Quests.GetStage(Q) == "ledger", "stage ledger: " + s.Quests.GetStage(Q));
            run.Chest("chest_sh2_keepers_lodge");
            Assert(s.Quests.GetStage(Q) == "give" && s.CountItem("sh2_ishiros_ledger") == 1, "the ledger found: " + s.Quests.GetStage(Q));
            Assert(s.QuestMarkerOf(Aiko).Kind == QuestMarker.ReadyToTurnIn, "Aiko waits for the ledger (yellow ?)");
            Assert(s.Map.IsEncounterDone("enc_sh2_lodge_pilgrims"), "the pilgrims on the way to the lodge were fought");

            run.TalkTo(Aiko, "Here. Keeper Ishiro's ledger");
            Assert(s.Quests.GetStage(Q) == "descend" && s.Flags.IsSet(Secret) && s.CountItem("sh2_ishiros_ledger") == 0, "Aiko reads the ledger and opens the stair");

            run.Travel(Entrance, Cat);
            Assert(s.Quests.GetStage(Q) == "keepers", "in the catacombs: " + s.Quests.GetStage(Q));

            // the Hall of Keepers: the gallery's bones and the hall's knights, then the three lanterns
            if (!s.Map.IsEncounterDone("enc_dg3_gallery_bones")) run.WalkInto("enc_dg3_gallery_bones");
            run.WalkInto("enc_dg3_keepers_hall");
            run.Inspect("dg3_lantern_hana", "(Open the lantern's little door");
            run.Inspect("dg3_lantern_tomo", "(Tell Keeper Tomo");
            run.Inspect("dg3_lantern_rin", "(Count aloud");
            Assert(s.Quests.GetStage(Q) == "lich", "the keepers are free: " + s.Quests.GetStage(Q));
            // the ossuary's chest lies past the drowned dead (a hidden ambush) on the far shore
            run.Chest("chest_dg3_ossuary");
            Assert(s.Map.IsEncounterDone("enc_dg3_drowned_ambush"), "the drowned dead rose on the way to the ossuary chest");

            // the chapel: the guard stands aside for the keepers' sake; Hazama does not
            run.Scripts["enc_dg3_chapel_guard"] = new[] { "Your keepers are free" };
            run.WalkInto("enc_dg3_chapel_guard");
            Assert(s.Flags.IsSet("dg3_chapel_guard_done") && s.Battle == null, "the chapel guard kneels");
            run.Scripts["enc_dg3_lantern_lich"] = new[] { "They wanted to go home" };
            int fights = run.Fights;
            run.WalkInto("enc_dg3_lantern_lich");
            Assert(run.Fights == fights + 1 && s.Flags.IsSet("dg3_lich_defeated") && s.Flags.IsSet("dg3_hazama_remembers"), "Hazama fought and laid to rest");
            Assert(s.Quests.GetStage(Q) == "lantern", "stage lantern: " + s.Quests.GetStage(Q));
            var dg3Rares = Db.LootTables["lt_dg3_lantern_lich"].entries[0].pool;
            Assert(dg3Rares.Any(id => s.CountItem(id) > 0 || s.PartyUnits().Any(u => u.Equipment.Contains(id))), "the guaranteed Rare dropped");

            run.Inspect("dg3_first_lantern", "(Open the cage");
            Assert(s.Flags.IsSet("sh2_keepers_freed") && s.CountItem("sh2_keepers_last_light") == 1 && s.Quests.GetStage(Q) == "return", "the keepers go home");
            run.Chest("chest_dg3_first_keepers_hoard");

            run.Travel("to_shrine", Shrine);
            Assert(s.QuestMarkerOf(Aiko).Kind == QuestMarker.ReadyToTurnIn, "Aiko: turn in (yellow ?)");
            run.TalkTo(Aiko, "We opened the First Keeper's lantern");
            Assert(s.Quests.IsCompleted(Q), "The Keepers' Rest complete");
            Assert(s.PendingQuestRewards.Contains(Q) && s.ClaimQuestReward(Q, "sh2_keepers_broom") == null && s.CountItem("sh2_keepers_broom") == 1, "a reward picked");
            Assert(s.VisibleNpcs().Any(n => n.npc == "sh2_ishiro_spirit"), "Keeper Ishiro is back on his bench");
            run.TalkTo("sh2_ishiro_spirit", "The keepers below");
            Assert(s.QuestMarkerOf(Aiko).Kind == QuestMarker.None, "no marker left on Aiko");
        }

        [Test]
        public static void KeepersRest_KeepingTheLantern_ChangesTheEnding()
        {
            var s = Game(15, ClassId.Mage, 47, party: false);
            Assert(s.Quests.Start(Q) && s.Quests.SetStage(Q, "lantern"), "quest at the lantern");
            s.Flags.Set(Secret, 1);
            Calm(s, Cat);
            Calm(s, Shrine);
            s.EnterMap(Cat, "default");
            var run = new Run(s);
            run.Inspect("dg3_first_lantern", "(Read the inscription", "(Keep the lantern");
            Assert(s.Flags.IsSet("sh2_lantern_kept") && !s.Flags.IsSet("sh2_keepers_freed") && s.CountItem("sh2_everburning_lantern") == 1,
                "the lantern kept");
            Assert(!s.Map.Def.props.Where(p => p.interact == "dg3_first_lantern").Any(p => s.Map.IsPropVisible(p)), "carried away");
            s.EnterMap(Shrine, "from_" + Cat);
            run.TalkTo(Aiko, "Hazama is at peace. I kept his lantern");
            Assert(s.Quests.IsCompleted(Q) && !s.VisibleNpcs().Any(n => n.npc == "sh2_ishiro_spirit"), "complete; Ishiro does not come back");
            var v = s.StartDialogue("dlg_sh2_aiko", Aiko);
            Assert(v && s.Dialogue.Current.NodeId == "a_done_kept", "Aiko remembers the choice");
            SessionTest.Finish(s);
        }

        [Test]
        public static void KeepersRest_OfferGatedByLevelAndTheHeart()
        {
            var q = Db.Quests[Q];
            Assert(q.giver == Aiko && q.minLevel == 12 && q.zone == Shrine && q.stages.Last().objectives.Single().type == ObjectiveType.Talk, "giver, minLevel, zone, a Talk hand-in");
            var low = Game(10, ClassId.Priest, 3, party: false);
            Assert(low.QuestMarkerOf(Aiko).Kind == QuestMarker.AvailableLater, "grey ! two levels early");
            Assert(low.StartDialogue("dlg_sh2_aiko", Aiko) && low.Dialogue.Current.NodeId == "a_low", "Aiko asks a tired party to come back later");
            SessionTest.Finish(low);
            var dark = Game(14, ClassId.Priest, 3, party: false, lit: false);
            Assert(!dark.VisibleNpcs().Any(n => n.npc == Aiko), "Aiko comes home once the Heart is lit");
        }

        // ================================================================== the boss

        static (BattleOutcome outcome, int rounds, float lowest, int lanterns) FightLich(GameSession s)
        {
            s.Flags.Set("dg3_chapel_guard_done", 1);
            s.EnterMap(Cat, "default");
            foreach (var u in s.PartyUnits()) u.RestoreFull();
            var b = s.StartEncounter("enc_dg3_lantern_lich");
            Assert(b != null, "the lich fight starts");
            float max = s.Party.Sum(u => u.MaxHealth), lowest = 1f;
            int lanterns = 0;
            while (!b.IsOver && b.Round <= 60)
            {
                s.AutoResolve(b.Round);
                lowest = Math.Min(lowest, s.Party.Sum(u => Math.Max(0f, u.Health)) / max);
                lanterns = Math.Max(lanterns, b.Units.Count(u => u.Creature != null && u.Creature.id == "cr_dg3_soulfire_lantern"));
            }
            return (b.Outcome, b.Round, lowest, lanterns);
        }

        [Test]
        public static void LanternLich_WinnableByAFittingParty_NotTrivial()
        {
            var lich = Db.Creatures["cr_dg3_lantern_lich"];
            Assert(lich.rank == CreatureRank.Boss && lich.levelFloor == 13 && lich.levelCap == 16 && lich.sprite == "cr_lantern_lich", "boss rank, levels 13-16");
            var lt = Db.LootTables[lich.lootTable];
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance >= 100 && e.pool.All(id => Db.Item(id).quality == Quality.Rare)), "a guaranteed Rare");
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance > 0 && e.chance < 100 && e.pool.All(id => Db.Item(id).quality == Quality.Epic)), "an Epic chance");

            int wins = 0;
            float hardest = 1f;
            foreach (ulong seed in new ulong[] { 11, 23, 37 })
            {
                var s = Game(15, ClassId.Warrior, seed);
                var (outcome, rounds, lowest, lanterns) = FightLich(s);
                Console.WriteLine($"    lich seed {seed}: {outcome} in {rounds} rounds, party low {lowest:P0}, lanterns {lanterns}");
                if (outcome == BattleOutcome.Victory) wins++;
                hardest = Math.Min(hardest, lowest);
                Assert(rounds >= 4, $"seed {seed}: the fight lasts ({rounds} rounds)");
                Assert(lanterns > 0, $"seed {seed}: Hazama rekindles his lanterns");
            }
            Assert(wins >= 2, $"a level-15 party of 5 beats Hazama ({wins}/3)");
            Assert(hardest <= 0.75f, $"and feels it: the party dropped to {hardest:P0} of its health");

            // alone, a level-13 hero does not
            var solo = Game(13, ClassId.Warrior, 5, party: false);
            Assert(FightLich(solo).outcome == BattleOutcome.Defeat, "Hazama is no pushover for one hero");
        }

        // ================================================================== balance

        [Test]
        public static void Catacombs_WorthAboutALevel_AndBuiltToTheRules()
        {
            var m = Db.Maps[Cat];
            int elitePacks = 0;
            float xp = 0f;
            const int party = 13;
            foreach (var e in m.encounters)
            {
                if (e.enemies.Count(x => Db.Creatures[x.creature].rank == CreatureRank.Elite) >= 2) elitePacks++;
                if (e.enemies.Count == 1 && Db.Creatures[e.enemies[0].creature].rank == CreatureRank.Elite)
                    Assert(Db.Creatures[e.enemies[0].creature].healthMult >= 2f, $"{e.id}: a lone elite has healthMult >= 2");
                foreach (var en in e.enemies)
                {
                    var c = Db.Creatures[en.creature];
                    int lvl = Math.Min(c.levelCap > 0 ? c.levelCap : 63, Math.Max(Math.Max(1, c.levelFloor), party + c.levelOffset));
                    xp += Formulas.MobXp(party, lvl) * Progression.XpRankMult(c.rank) * c.xpMult * Progression.XpRate(Db, party);
                }
            }
            Assert(m.encounters.Count >= 4 && m.encounters.Count <= 7 && elitePacks >= 2 && m.chests.Count >= 2 && m.chests.Count <= 3, "4-7 encounters, 2+ elite packs, 2-3 chests");
            float level = Progression.XpToNextLevel(Db, party);
            Assert(xp >= 0.8f * level && xp <= 1.6f * level, $"the catacombs' fights are worth about a level at {party}: {xp:0} XP vs {level}");
            foreach (var c in Db.Creatures.Values.Where(c => c.id.StartsWith("cr_dg3_", StringComparison.Ordinal) || c.id.StartsWith("cr_sh2_", StringComparison.Ordinal)))
            {
                Assert(c.scaleToParty && c.levelFloor > 0 && c.levelCap >= c.levelFloor, $"{c.id}: scales within a floor and cap");
                Assert(c.material.Length > 0 && c.voice.Length > 0, $"{c.id}: sound material and voice");
            }
        }
    }
}
