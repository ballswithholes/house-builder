// Amberfield Downs and the Barrow of King Aldwin (Docs/Expansion.md §8, builder amberfield; ids am_* / dg4_*):
//   * both maps load, every spawn, exit, NPC, chest and encounter is reachable (TestsMapsReachable's rule, asserted here);
//   * the barrow is revealed by the King's Ring Perception check, by Old Ida's hint and by the King's Stone, and the door
//     then takes the party in and out;
//   * The Ember Road and every Amberfield side quest are played to completion at the top of the band (level 18) through
//     dialogues, walking, prop clicks, chests and fights won by AutoResolve — once by the fighting routes, once by the
//     peaceful ones (Mudpaw parley, a peace geode, Nib sent home);
//   * the zone boss and the barrow king are won by a fitting party and lost by an unfitting one;
//   * the zone's quest XP and fights carry a character through the 12-18 band.
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
    public static class TestsContentAm
    {
        static readonly string[] ZoneQuests =
        {
            "mq2_ember_road", "am_q_lamp_oil", "am_q_bell", "am_q_hawks", "am_q_queen", "am_q_ditchwater", "am_q_sinkholes",
            "am_q_peace_offering", "am_q_scarecrow", "am_q_corlan", "am_q_harvest", "am_q_barrow",
        };

        // ================================================================== a scripted run through the zone

        sealed class AmRun
        {
            public readonly GameSession S;
            public readonly List<string> Log = new List<string>();
            public int Fights;

            public AmRun(ClassId main, int level, ulong seed, params string[] companions)
            {
                S = SessionTest.NewGame(main, level, seed);
                foreach (var c in companions) S.Recruit(c);
                S.Settings.CompanionAutoPlay = true;
                S.SetAutoPlay(S.Main, true);
                S.TakeEvents();
            }

            public void Note(string s) => Log.Add(s);
            public string Tail(int n = 40) => string.Join("\n      ", Log.Skip(Math.Max(0, Log.Count - n)));

            void Check(bool cond, string msg)
            {
                if (!cond) Assert(false, msg + "\n      " + Tail());
            }

            /// <summary>Plays the running dialogue: picks the first visible choice matching the earliest unused script
            /// entry ("a|b" = either); otherwise continues, or leaves by an exit choice, or takes the last choice.</summary>
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
                            var c = v.Choices.FirstOrDefault(x => x.Text.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (c != null) { pick = c.Index; break; }
                        }
                        if (pick >= 0) { Note($"  > {todo[k]}"); todo.RemoveAt(k); }
                    }
                    if (pick < 0)
                        foreach (var leave in new[] { "(Continue.)", "Goodbye", "(Leave", "(Step back", "Let's keep moving" })
                        {
                            var c = v.Choices.FirstOrDefault(x => x.Text.IndexOf(leave, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (c != null) { pick = c.Index; break; }
                        }
                    if (pick < 0 && todo.Count == 0 && v.Choices.Count > 0) pick = v.Choices[v.Choices.Count - 1].Index;   // nothing scripted left: the last choice
                    Check(pick >= 0, $"no choice to pick at {v.DialogueId}/{v.NodeId}: {SessionTest.Describe(v)} (wanted {string.Join(" | ", todo)})");
                    S.ChooseDialogue(pick);
                }
                Check(!S.Dialogue.IsActive, "dialogue finished");
                Check(todo.Count == 0, $"every scripted choice was offered (left: {string.Join(" | ", todo)})");
                After();
            }

            void After()
            {
                if (S.Mode == SessionMode.Combat) Fight();
                if (S.PendingLoot != null) S.TakeAllLoot();
            }

            public void Fight()
            {
                Check(S.Battle != null, "a battle");
                Fights++;
                var enc = S.BattleEncounter?.id;
                var outcome = S.AutoResolve(150);
                var sum = S.FinishBattle();
                Note($"   fight {enc}: {outcome} in {sum?.Rounds} rounds, +{sum?.Xp} xp, level {S.Main.Level}");
                Check(outcome == BattleOutcome.Victory && sum != null && sum.Outcome == CombatEndKind.Victory,
                      $"{enc}: the party wins (outcome {outcome}, party {SessionTest.PartyHp(S)})");
                if (S.PendingLoot != null) S.TakeAllLoot();
                foreach (var u in S.Roster)
                    if (S.TalentPointsAvailable(u) > 0) S.AutoAllocateTalents(u);
                SessionTest.Refresh(S);
            }

            /// <summary>Walks the leader to a point, answering encounter dialogues with <paramref name="script"/> and
            /// winning every fight on the way.</summary>
            public void Go(Vec2 dest, float near = 0.8f, params string[] script)
            {
                for (int leg = 0; leg < 40; leg++)
                {
                    Check(!S.IsGameOver, "game over");
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(script); script = new string[0]; continue; }
                    if (S.PendingLoot != null) S.TakeAllLoot();
                    if (Vec2.Distance(S.Leader.Position, dest) <= near) return;
                    var r = S.MoveLeader(dest);
                    if (r.Trigger.Stop)
                    {
                        Note($"  trigger {r.Trigger}");
                        Check(r.Trigger.Kind != TriggerKind.Locked, "a locked transition on the way: " + r.Trigger.Id);
                        if (r.Trigger.Kind == TriggerKind.Travel) return;
                        continue;
                    }
                    if (Vec2.Distance(S.Leader.Position, dest) <= near + 0.3f) return;
                    if (!r.Moved) break;
                }
                Check(Vec2.Distance(S.Leader.Position, dest) <= near + 1.5f, $"reached {dest} on {S.MapId} (at {S.Leader.Position})");
            }

            public void Talk(string npcId, params string[] script)
            {
                var npc = S.VisibleNpcs().FirstOrDefault(n => n.npc == npcId);
                Check(npc != null, $"{npcId} is on {S.MapId}");
                Go(npc.pos + new Vec2(-1.2f, -0.9f), 1.0f);
                var r = S.TalkTo(npcId);
                Check(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npcId}: {r}");
                Note($"talk {npcId}");
                Converse(script);
            }

            public void Prop(string interactId, params string[] script)
            {
                var p = S.Map.Def.props.First(x => x.interact == interactId);
                Go(p.pos + new Vec2(0f, -1.6f), 1.2f);
                var r = S.InteractProp(interactId);
                Check(r.Ok && r.Kind == InteractKind.Dialogue, $"click {interactId}: {r}");
                Note($"prop {interactId}");
                Converse(script);
            }

            public void Encounter(string encId, params string[] script)
            {
                var enc = S.Map.FindEncounter(encId);
                Check(enc != null, $"{encId} on {S.MapId}");
                if (S.Map.IsEncounterDone(enc)) { Note($"{encId} already done"); return; }
                Check(S.Map.IsEncounterAvailable(enc), $"{encId} is available");
                Note($"-> {encId}");
                for (int leg = 0; leg < 12 && !S.Map.IsEncounterDone(enc); leg++)
                {
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(script); script = new string[0]; continue; }
                    var r = S.MoveLeader(enc.pos);
                    if (r.Trigger.Stop) continue;
                    if (S.Mode == SessionMode.Exploration && !S.Map.IsEncounterDone(enc) && Vec2.Distance(S.Leader.Position, enc.pos) < enc.radius)
                        S.MoveLeader(enc.pos + new Vec2(enc.radius + 2f, 0f));
                }
                Check(S.Map.IsEncounterDone(enc), $"{encId} resolved");
            }

            public void Chest(string chestId)
            {
                var c = S.Map.FindChest(chestId);
                Check(c != null && S.Map.IsChestAvailable(c), $"chest {chestId} is there");
                Go(c.pos + new Vec2(-1f, -0.8f), 1.2f);
                for (int i = 0; i < 40 && S.Map.IsChestLocked(c); i++) S.TryUnlockChest(chestId);
                if (!S.Map.IsChestOpened(chestId))
                {
                    var r = S.OpenChest(chestId);
                    Check(r.Ok, $"open {chestId}: {r}");
                }
                if (S.PendingLoot != null) S.TakeAllLoot();
                Note($"chest {chestId}");
            }

            public void Travel(string transitionId, string expectMap)
            {
                var t = S.Map.Def.transitions.First(x => x.id == transitionId);
                Check(S.Map.IsTransitionVisible(t), $"{transitionId} is visible");
                Go(t.pos, 0.1f);
                if (S.MapId != expectMap)
                {
                    var r = S.UseTransition(transitionId);
                    Check(r.Ok, $"use {transitionId}: {r}");
                }
                Check(S.MapId == expectMap, $"travelled to {expectMap} (on {S.MapId})");
                Note($"== {expectMap}");
            }

            public void Stage(string q, string stage) =>
                Check(S.Quests.IsActive(q) && S.Quests.GetStage(q) == stage, $"{q} at '{stage}' (is {S.Quests.GetStatus(q)} '{S.Quests.GetStage(q)}')");

            public void Done(string q) => Check(S.Quests.IsCompleted(q), $"{q} completed (is {S.Quests.GetStatus(q)} '{S.Quests.GetStage(q)}')");

            public Vec2 At(string encOrNpc)
            {
                var e = S.Map.FindEncounter(encOrNpc);
                if (e != null) return e.pos;
                return S.VisibleNpcs().First(n => n.npc == encOrNpc).pos;
            }
        }

        // ================================================================== maps

        [Test]
        public static void Maps_EnterAndEverythingReachable()
        {
            foreach (var id in new[] { "amberfield", "dgn_barrow" })
            {
                Assert(Db.Maps.ContainsKey(id), id + " exists");
                var m = Db.Maps[id];
                var problems = TestsMapsReachable.Unreachable(m);
                Assert(problems.Count == 0, $"{id}: " + string.Join("; ", problems));
                var s = SessionTest.NewGame(ClassId.Hunter, 18, 3);
                foreach (var sp in m.spawns)
                {
                    s.EnterMap(id, sp.id);
                    Assert(s.MapId == id && s.Nav.IsWalkable(s.Leader.Position, NavAgent.DefaultRadius), $"{id}/{sp.id}: standing on walkable ground");
                }
            }
            var a = Db.Maps["amberfield"];
            Assert(a.levelMin == 12 && a.levelMax == 18 && a.biome == "highlands" && a.restArea && a.fill >= 0.3f && a.fill <= 0.5f, "amberfield band and look");
            var b = Db.Maps["dgn_barrow"];
            Assert(b.dungeon && !b.restArea && b.environment == "crypt" && b.fill >= 0.4f && b.fill <= 0.6f, "the barrow is a hidden crypt dungeon");
            Assert(b.encounters.Count >= 4 && b.encounters.Count <= 7 && b.chests.Count >= 2 && b.chests.Count <= 3, "4-7 encounters, 2-3 chests");
            int elitePacks = b.encounters.Count(e => e.enemies.Count > 1 && e.enemies.Any(x => Db.Creatures[x.creature].rank == CreatureRank.Elite));
            Assert(elitePacks >= 2, "at least two elite packs: " + elitePacks);
            // the creatures sit in their bands
            foreach (var c in Db.Creatures.Values.Where(x => x.id.StartsWith("cr_am_")))
                Assert(c.scaleToParty && c.levelFloor == 12 && c.levelCap == 18 && c.levelOffset >= -2 && c.levelOffset <= 2, c.id + " in 12-18");
            foreach (var c in Db.Creatures.Values.Where(x => x.id.StartsWith("cr_dg4_")))
                Assert(c.scaleToParty && c.levelFloor == 18 && c.levelCap == 20, c.id + " in 18-20");
        }

        // ================================================================== the barrow reveal

        [Test]
        public static void Barrow_RevealedByTheRing_ByIda_AndByTheKingsStone()
        {
            // (1) the Perception check at the King's Ring: rolled once, on entry; success reveals the door
            int found = 0, missed = 0;
            for (ulong seed = 1; seed <= 24; seed++)
            {
                var s = SessionTest.NewGame(ClassId.Hunter, 15, seed);
                Assert(!s.Flags.IsSet("found_barrow"), "the barrow door is hidden at first");
                s.EnterMap("amberfield", "from_dgn_barrow");                      // stands in the Ring, north of the stones
                var t = s.MapDef.transitions.First(x => x.id == "to_dgn_barrow");
                var ring = s.MapDef.regions.First(r => r.id == "reg_am_kings_ring");
                SessionTest.WalkTo(s, ring.pos + new Vec2(0f, -3.4f));
                var ev = s.TakeEvents();
                bool rolled = ev.Any(e => e.Kind == SessionEventKind.SkillCheck && e.Id == "reg_am_kings_ring");
                Assert(rolled, $"seed {seed}: the region check rolled on entry");
                if (s.Flags.IsSet("found_barrow"))
                {
                    found++;
                    Assert(s.Map.IsTransitionVisible(t), "revealed");
                    Assert(ev.Any(e => e.Kind == SessionEventKind.SecretFound && e.Id2 == "to_dgn_barrow"), "SecretFound announced");
                }
                else missed++;
            }
            Assert(found > 0 && missed > 0, $"a real roll (found {found}, missed {missed} of 24)");

            // (2) Old Ida's hint, no roll
            var run = new AmRun(ClassId.Warrior, 16, 5, "seren", "lys", "rook", "bruna");
            run.S.EnterMap("amberfield", "default");
            run.Talk("am_ida_crook", "Tell me about the King's Ring", "Is there a way into the hill");
            Assert(run.S.Flags.IsSet("found_barrow"), "Ida's hint reveals the door");
            run.Travel("to_dgn_barrow", "dgn_barrow");
            Assert(run.S.Leader != null && run.S.MapId == "dgn_barrow", "in the barrow");
            run.Travel("to_amberfield", "amberfield");
            var back = run.S.MapDef.spawns.First(x => x.id == "from_dgn_barrow").pos;
            Assert(Vec2.Distance(run.S.Leader.Position, back) < 3f, "back out at the barrow door");

            // (3) the King's Stone: an Investigation check (once)
            int stoneFound = 0;
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var s = SessionTest.NewGame(ClassId.Mage, 15, seed);
                s.EnterMap("amberfield", "default");
                var r = s.InteractProp("am_kings_stone");
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, "the King's Stone has a dialogue");
                SessionTest.Pick(s, "Follow the oil stains");
                SessionTest.Finish(s, "(Continue.)");
                if (s.Flags.IsSet("found_barrow")) stoneFound++;
            }
            Assert(stoneFound > 0, "the King's Stone can reveal the door");
        }

        // ================================================================== the whole zone, fighting

        [Test]
        public static void EmberRoad_AndEveryAmberfieldQuest_FightingRoutes()
        {
            var r = new AmRun(ClassId.Warrior, 18, 4242, "seren", "lys", "rook", "bruna");
            var S = r.S;
            Assert(S.Party.Count == 5, "a party of five");
            S.Quests.Start("mq2_ember_road");
            S.EnterMap("amberfield", "from_lanternvale");
            r.Stage("mq2_ember_road", "reeve");

            // the Reeve and the givers of the hamlet and the farm
            r.Talk("am_reeve_hester", "Who's stealing it?", "Then I'll follow the carts", "North of the Amber Bridge");
            r.Stage("mq2_ember_road", "caravan");
            r.Talk("am_miller_pell", "You're out of oil?", "I'll find your casks");
            r.Talk("am_hedda_haybright", "You look like you've lost something", "I'll get Biscuit's bell back");
            r.Talk("am_tam_haybright", "Suspicious? The scarecrow?", "I'll take a look at your scarecrow");
            r.Talk("am_ser_corlan", "There's a heron carved over your door", "Then I'll get it back for you");
            r.Talk("am_hob_waxley", "You look worried, Hob", "I'll bring your queen home");
            r.Talk("am_ida_crook", "Are you missing lambs", "Six hawks");
            foreach (var q in new[] { "am_q_lamp_oil", "am_q_bell", "am_q_scarecrow", "am_q_corlan", "am_q_queen", "am_q_hawks" })
                Assert(S.Quests.IsActive(q), q + " offered and accepted");

            // the scarecrow that walked
            r.Prop("am_scarecrow", "(Give the scarecrow a good firm shake.)");
            r.Stage("am_q_scarecrow", "decide");
            r.Talk("am_nib", "What happens to you now, Nib?|Why did you run away", "What happens to you now, Nib?", "Hedda Haybright could use a farmhand");
            r.Stage("am_q_scarecrow", "return");
            r.Talk("am_tam_haybright", "It was a gnoll pup called Nib");
            r.Done("am_q_scarecrow");
            Assert(S.VisibleNpcs().Any(n => n.npc == "am_nib_farm"), "Nib works at Haybright now");

            // hawks, Redwing, and Hob's queen
            r.Encounter("enc_am_hawks_south");
            r.Encounter("enc_am_hawks_ring");
            r.Encounter("enc_am_hawks_lookout");
            r.Stage("am_q_hawks", "return");
            r.Prop("am_swarm", "(Puff Old Hob's smoker");
            r.Stage("am_q_queen", "return");
            r.Talk("am_hob_waxley", "Your queen is back");
            r.Done("am_q_queen");
            r.Talk("am_ida_crook", "Six hawks won't take");
            r.Done("am_q_hawks");

            // the Ditchwater Gang (the hard way) and Biscuit's bell from their strongbox
            r.Talk("am_constable_pip", "The noticeboard says", "Consider them dealt with");
            r.Encounter("enc_am_ditchwater", "(Attack.)");
            r.Chest("chest_am_strongbox");
            r.Stage("am_q_bell", "return");
            r.Talk("am_constable_pip", "The Ditchwater Gang won't trouble the road again");
            r.Done("am_q_ditchwater");
            r.Talk("am_hedda_haybright", "Biscuit's bell, back where it belongs");
            r.Done("am_q_bell");

            // the Mudpaw (the hard way)
            r.Talk("am_wat_turnbull", "That's a very round hole", "I'll go to the quarry");
            r.Encounter("enc_am_quarry_parley", "(Attack.)");
            r.Encounter("enc_am_quarry_deep");
            r.Stage("am_q_sinkholes", "return");
            r.Talk("am_wat_turnbull", "The Mudpaw diggers won't undermine");
            r.Done("am_q_sinkholes");
            Assert(!S.VisibleNpcs().Any(n => n.npc == "am_mother_delve"), "no Mother Delve after a war");

            // the oil caravan, Ser Corlan reads the tally-stick
            r.Encounter("enc_am_oil_caravan");
            r.Stage("mq2_ember_road", "corlan");
            Assert(S.CountItem("am_duskmane_tally") == 1, "the caravan boss carried the tally-stick");
            r.Talk("am_ser_corlan", "It came off the caravan boss", "Skyreach?", "Skarra's warcamp");
            r.Stage("mq2_ember_road", "warchief");
            Assert(S.CountItem("am_duskmane_tally") == 0, "Corlan keeps the stick");

            // Pell's casks at the north ford and on the king's hill
            r.Encounter("enc_am_gnoll_scouts");
            r.Prop("am_cask_2", "(Chalk Pell's mark");
            r.Encounter("enc_am_grave_robbers");
            r.Prop("am_cask_3", "(Chalk Pell's mark");

            // the warcamp, the last cask, the warchief and her trophies
            r.Encounter("enc_am_warcamp_gate");
            r.Encounter("enc_am_warcamp_fire");
            r.Encounter("enc_am_warcamp_archers");
            r.Prop("am_cask_1", "(Chalk Pell's mark");
            r.Encounter("enc_am_warchief", "Bruna, anything to add?");
            Assert(S.Flags.IsSet("am_skarra_defeated"), "Skarra defeated");
            r.Stage("mq2_ember_road", "reeve_report");
            Assert(S.CountItem("am_ember_letter") == 1, "Skarra carried the scorched letter");
            r.Chest("chest_am_trophies");
            r.Stage("am_q_corlan", "return");

            // hand-ins in the hamlet
            r.Stage("am_q_lamp_oil", "return");
            r.Talk("am_miller_pell", "All three casks");
            r.Done("am_q_lamp_oil");
            r.Talk("am_ser_corlan", "Hang it over your door again");
            r.Done("am_q_corlan");
            Assert(S.Flags.IsSet("am_corlan_banner_home") && S.CountItem("am_corlans_riding_cloak") + (S.Roster.Any(u => u.Equipment.Contains("am_corlans_riding_cloak")) ? 1 : 0) >= 1,
                   "the banner is home and the cloak given");
            r.Talk("am_reeve_hester", "Skarra Duskmane is dead", "Who would know", "I'll take the road to Brightwater");
            r.Stage("mq2_ember_road", "archivist");

            // Light the Lanes
            r.Talk("am_reeve_hester", "The warchief is dead and the oil is coming home", "I'll light the lanes");
            for (int i = 1; i <= 4; i++) r.Prop("am_lane_lamp_" + i, "(Fill the lantern");
            r.Stage("am_q_harvest", "return");
            r.Talk("am_reeve_hester", "All four lane lanterns are lit");
            r.Done("am_q_harvest");
            Assert(S.Flags.IsSet("am_harvest_home"), "Harvest-Home");

            // The King Under the Hill: Ida's quest and hint, the barrow, the king
            r.Talk("am_ida_crook", "Something's wrong under the king's hill", "I'll go down into the barrow");
            if (!S.Flags.IsSet("found_barrow"))                  // the Ring's Perception check may already have found the door
            {
                r.Stage("am_q_barrow", "find");
                r.Talk("am_ida_crook", "Tell me about the King's Ring", "Is there a way into the hill");
            }
            r.Stage("am_q_barrow", "king");
            PlayBarrow(r);
            r.Stage("am_q_barrow", "return");
            r.Talk("am_ida_crook", "King Aldwin sleeps again");
            r.Done("am_q_barrow");

            // on to Brightwater and Penhallow
            r.Travel("to_brightwater", "brightwater");
            r.Talk("bw_archivist_penhallow", "Elder Maru sent me");
            r.Done("mq2_ember_road");
            Assert(S.Flags.IsSet("mq2_ember_road_done"), "mq2_ember_road_done is set");

            foreach (var q in ZoneQuests.Where(x => x != "am_q_peace_offering")) r.Done(q);
            Assert(S.Main.Level >= 18, "still at the top of the band or above: " + S.Main.Level);
            Console.WriteLine($"    amberfield fighting run: {r.Fights} fights, level {S.Main.Level}");
        }

        /// <summary>The barrow, door to king: every encounter fought (the trap only if nobody spotted the wire), the chests,
        /// the ember smothered, the king's shade heard, back out.</summary>
        static void PlayBarrow(AmRun r)
        {
            var S = r.S;
            r.Travel("to_dgn_barrow", "dgn_barrow");
            r.Encounter("enc_dg4_robbers");
            r.Go(new Vec2(23.5f, 22f), 0.8f);                    // through the trap corridor
            r.Encounter("enc_dg4_wight_guard");
            r.Encounter("enc_dg4_ossuary");
            r.Chest("chest_dg4_ossuary");
            r.Encounter("enc_dg4_ember_hearth");
            r.Prop("dg4_ember", "(Smother the coal");
            Assert(S.Flags.IsSet("dg4_ember_smothered"), "the ember is out");
            r.Chest("chest_dg4_hearth");
            r.Encounter("enc_dg4_huscarls");
            r.Encounter("enc_dg4_king_aldwin", "(Draw your weapon.)");
            Assert(S.Flags.IsSet("dg4_aldwin_rests"), "King Aldwin rests");
            r.Chest("chest_dg4_hoard");
            r.Talk("dg4_aldwin_shade", "What was the fire under your hill?", "Then I'll follow him north");
            Assert(S.Flags.IsSet("dg4_shade_spoke"), "the shade's tale");
            r.Travel("to_amberfield", "amberfield");
        }

        // ================================================================== the peaceful routes

        [Test]
        public static void Amberfield_PeacefulRoutes_MudpawPeace_PeaceGeode_NibGoesHome()
        {
            var r = new AmRun(ClassId.Hunter, 18, 777, "kael", "seren", "lys", "pip");
            var S = r.S;
            S.EnterMap("amberfield", "default");
            r.Talk("am_wat_turnbull", "That's a very round hole", "I'll go to the quarry");
            r.Encounter("enc_am_quarry_parley", "Every track out of this quarry points away", "Deal. I'll tell Wat.");
            Assert(S.Flags.IsSet("am_mudpaw_peace") && S.Flags.IsSet("am_quarry_settled") && r.Fights == 0, "the Hunter's parley makes peace, no fight");
            var deep = S.Map.FindEncounter("enc_am_quarry_deep");
            Assert(!S.Map.IsEncounterAvailable(deep), "the deep diggers stand down");
            Assert(S.VisibleNpcs().Any(n => n.npc == "am_mother_delve"), "Mother Delve comes up");
            r.Talk("am_mother_delve", "Is there anything I can do for the Mudpaw?", "I'll take it to Wat");
            r.Stage("am_q_peace_offering", "deliver");
            r.Talk("am_wat_turnbull", "A gift from Mother Delve");
            r.Done("am_q_peace_offering");
            r.Talk("am_wat_turnbull", "The Mudpaw aren't raiding");
            r.Done("am_q_sinkholes");
            Assert(S.CountItem("am_lucky_turnip") + (S.Roster.Any(u => u.Equipment.Contains("am_lucky_turnip")) ? 1 : 0) == 1, "Wat's lucky turnip");
            r.Talk("am_mother_delve", "What did you dig into, under the hill?");
            Assert(S.Flags.IsSet("found_barrow"), "Mother Delve shows the barrow door");

            r.Talk("am_tam_haybright", "Suspicious? The scarecrow?", "I'll take a look at your scarecrow");
            r.Prop("am_scarecrow", "(Give the scarecrow a good firm shake.)");
            r.Talk("am_nib", "What's the Hungry Fire?", "What happens to you now, Nib?", "Go home to your mum");
            Assert(S.Flags.IsSet("am_nib_home") && !S.VisibleNpcs().Any(n => n.npc == "am_nib" || n.npc == "am_nib_farm"), "Nib went home");
            r.Talk("am_tam_haybright", "I sent him home");
            r.Done("am_q_scarecrow");
        }

        // ================================================================== bosses: winnable, not trivial

        static (BattleOutcome outcome, int rounds, float hpLost) Boss(string map, string enc, int level, ulong seed, ClassId main, params string[] comps)
        {
            var s = SessionTest.NewGame(main, level, seed);
            foreach (var c in comps) s.Recruit(c);
            s.Settings.CompanionAutoPlay = true;
            s.SetAutoPlay(s.Main, true);
            s.EnterMap(map, "default");
            var b = s.StartEncounter(enc);
            Assert(b != null, $"{enc} starts: {s.LastError}");
            // the AI plays both sides; hpLost is the deepest dip of the party's total health during the fight
            var party = s.Party.ToList();
            // hpLost = all the damage the party took (health lost between turns, healing ignored) over its total health
            float max = party.Sum(u => u.MaxHealth), taken = 0f;
            var last = party.Select(x => x.Health).ToList();
            for (int guard = 0; !b.IsOver && b.Round <= 150 && guard < 100000; guard++)   // GameSession.AutoResolve, turn by turn
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                AI.RunTurn(b, u);
                if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
                for (int i = 0; i < party.Count; i++)
                {
                    float h = Math.Max(0f, party[i].Health);
                    if (h < last[i]) taken += last[i] - h;
                    last[i] = h;
                }
            }
            return (b.Outcome, b.Round, taken / Math.Max(1f, max));
        }

        [Test]
        public static void Bosses_SkarraAndAldwin_WinnableByAFittingParty_NotTrivial()
        {
            foreach (ulong seed in new ulong[] { 11, 12 })
            {
                var w = Boss("amberfield", "enc_am_warchief", 18, seed, ClassId.Warrior, "seren", "lys", "rook", "bruna");
                Assert(w.outcome == BattleOutcome.Victory, $"Skarra (seed {seed}): a party of five at 18 wins ({w.outcome})");
                Assert(w.rounds >= 5 && w.hpLost > 0.25f, $"Skarra (seed {seed}): not trivial ({w.rounds} rounds, {w.hpLost:P0} health lost)");
                var k = Boss("dgn_barrow", "enc_dg4_king_aldwin", 20, seed, ClassId.Warrior, "seren", "lys", "rook", "torvan");
                Console.WriteLine($"    seed {seed}: Skarra {w.outcome} {w.rounds} rounds, damage taken {w.hpLost:P0} of the party health; Aldwin {k.outcome} {k.rounds} rounds, damage taken {k.hpLost:P0}");
                Assert(k.outcome == BattleOutcome.Victory, $"Aldwin (seed {seed}): a party of five at 20 wins ({k.outcome})");
                Assert(k.rounds >= 6 && k.hpLost > 0.3f, $"Aldwin (seed {seed}): not trivial ({k.rounds} rounds, {k.hpLost:P0} health lost)");
            }
            var solo = Boss("amberfield", "enc_am_warchief", 18, 13, ClassId.Mage);
            Assert(solo.outcome != BattleOutcome.Victory, "Skarra's pack beats a lone level-18 mage");
            var duo = Boss("dgn_barrow", "enc_dg4_king_aldwin", 18, 13, ClassId.Rogue, "lys");
            Assert(duo.outcome != BattleOutcome.Victory, "King Aldwin beats two level-18 damage dealers");
        }

        // ================================================================== data rules for the zone

        [Test]
        public static void Zone_Quests_Markers_Xp_AndGear()
        {
            // quests: zone tag, givers, a Level-gated offer, hand-ins at people
            foreach (var id in ZoneQuests)
            {
                Assert(Db.Quests.TryGetValue(id, out var q), id + " exists");
                Assert(q.zone == "amberfield", id + " in the Amberfield journal");
                Assert(Db.Npcs.ContainsKey(q.giver), id + " giver");
                var last = q.stages[q.stages.Count - 1];
                Assert(last.objectives.Any(o => o.type == ObjectiveType.Talk) || !string.IsNullOrEmpty(last.turnIn), id + ": ends at a person");
            }
            Assert(Db.Quests["mq2_ember_road"].stages[0].id == "road" && Db.Quests["mq2_ember_road"].stages.Last().id == "archivist", "the Ember Road keeps its first and last stages");

            // XP: the zone's quests at the levels they are done, plus its fights, take a character through 12-18
            float questXp = 0f;
            var lv = new Dictionary<string, int>
            {
                ["mq2_ember_road"] = 18, ["am_q_lamp_oil"] = 17, ["am_q_bell"] = 13, ["am_q_hawks"] = 13, ["am_q_queen"] = 13,
                ["am_q_ditchwater"] = 14, ["am_q_sinkholes"] = 15, ["am_q_scarecrow"] = 15, ["am_q_corlan"] = 17, ["am_q_harvest"] = 17,
            };
            foreach (var kv in lv) questXp += Progression.QuestXp(Db, Db.Quests[kv.Key].rewards.xp, kv.Value);
            foreach (var st in Db.Quests["mq2_ember_road"].stages)
                foreach (var o in st.onComplete) if (o.type == OutcomeType.GiveXP) questXp += Progression.QuestXp(Db, o.amount, 15);
            float band = 0f;
            for (int l = 12; l < 18; l++) band += Progression.XpToNextLevel(Db, l);
            Assert(questXp > 0.5f * band && questXp < 0.85f * band, $"quests carry 50-85% of the band ({questXp:0} of {band:0}); fights do the rest");

            // gear: every authored equipable is wearable at its level and within the stat budget guardrail
            foreach (var it in Db.Items.Values.Where(x => (x.id.StartsWith("am_") || x.id.StartsWith("dg4_")) && x.equip != EquipType.None))
            {
                Assert(it.stats.Count > 0, it.id + " has stats");
                Assert(it.armorType != ArmorType.Plate, it.id + ": no plate below 40");
                Assert(it.requiredLevel >= 12 && it.requiredLevel <= 20 && it.itemLevel >= it.requiredLevel, it.id + " level");
                Assert(string.IsNullOrEmpty(it.use), it.id + ": no use on equipables");
            }
            // the king's table: a guaranteed Rare and a chance at an Epic
            var lt = Db.LootTables["lt_dg4_king_aldwin"];
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance >= 100f && e.pool.All(p => Db.Items[p].quality == Quality.Rare)), "a guaranteed Rare");
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance < 100f && e.pool.All(p => Db.Items[p].quality == Quality.Epic)), "an Epic chance");
        }
    }
}
