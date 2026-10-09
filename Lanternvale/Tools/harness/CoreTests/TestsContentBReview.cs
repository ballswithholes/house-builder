// Content fixes from the expansion review for the new zones (Amberfield, Brightwater, Mirefen, Skyreach), their dungeons
// (the Barrow, the Drowned Vault, the Frozen Sanctum) and Ashwyrm's Roost: one-shot packs counted with Defeat objectives
// (caught up when the stage starts, so no order of play softlocks), hand-ins through a Flag set by the hand-in choice (so
// leaving the conversation never completes a quest), quest items that always drop, Aubric's stage caught up after an
// early visit, the Roost's portal revealed by Ash on the Wind, hard enrages in the Roost, and smaller data checks.
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
    public static class TestsContentBReview
    {
        /// <summary>The quests of this area (the zones, their dungeons and the Roost; the main story's own stubs aside).</summary>
        static bool Mine(string questId) =>
            new[] { "am_", "bw_", "mf_", "sr_", "dg4_", "dg5_", "dg6_", "r2_" }.Any(p => questId.StartsWith(p, StringComparison.Ordinal));

        static IEnumerable<(MapDef map, EncounterDef enc)> Encounters() =>
            Db.Maps.Values.SelectMany(m => m.encounters.Select(e => (m, e)));

        // ===================================================================================================== kills

        /// <summary>
        /// A Kill objective only counts while its stage is active and packs never come back, so a quest may only count
        /// creatures that cannot be used up before it starts: enough of them must sit in packs gated on a flag (the
        /// offer's). Everything else is counted per pack with Defeat objectives, which QuestLog.Pull catches up; those
        /// packs keep the default done flag, the one Pull reads.
        /// </summary>
        [Test]
        public static void KillObjectives_CannotBeUsedUpBeforeTheQuest()
        {
            var bad = new List<string>();
            foreach (var q in Db.Quests.Values.Where(q => Mine(q.id)))
                foreach (var st in q.stages)
                    foreach (var o in st.objectives)
                    {
                        if (o.type == ObjectiveType.Kill)
                        {
                            int gated = Encounters().Where(x => !string.IsNullOrEmpty(x.enc.requireFlag) && !x.enc.requireFlag.StartsWith("!"))
                                                    .Sum(x => x.enc.enemies.Count(u => u.creature == o.target));
                            if (gated < Math.Max(1, o.count)) bad.Add($"{q.id}/{st.id}: Kill {o.target} x{o.count}, only {gated} cannot be met before the quest");
                        }
                        if (o.type == ObjectiveType.Defeat)
                        {
                            var e = Encounters().FirstOrDefault(x => x.enc.id == o.target).enc;
                            if (e == null) bad.Add($"{q.id}/{st.id}: Defeat {o.target}: no such encounter");
                            else if (!string.IsNullOrEmpty(e.doneFlag) && e.doneFlag != WorldRules.EncounterDoneFlag(e.id))
                                bad.Add($"{q.id}/{st.id}: Defeat {o.target} has done flag {e.doneFlag}, which a late start does not catch up (use a Flag objective)");
                        }
                    }
            Assert(bad.Count == 0, string.Join("\n    ", bad));
        }

        [Test]
        public static void PacksFoughtBeforeTheQuest_StillCount()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 30, 811);
            void Done(params string[] encs) { foreach (var e in encs) s.Flags.Set(WorldRules.EncounterDoneFlag(e)); }
            void Expect(string q, string stage)
            {
                Assert(s.Quests.Start(q), "start " + q);
                Assert(s.Quests.GetStage(q) == stage || (stage == "" && s.Quests.IsCompleted(q)), $"{q}: at '{stage}' (is '{s.Quests.GetStage(q)}')");
            }

            // every pack of the quest was already fought on the way (the main quest's road, the camp, the raid's terraces)
            Done("enc_am_hawks_south", "enc_am_hawks_ring", "enc_am_hawks_lookout");
            Expect("am_q_hawks", "return");
            Done("enc_mf_shallows_crocs", "enc_mf_shallows_crocs_east");
            Expect("mf_bo_nets", "gnasher");
            Done("enc_mf_mere_ghouls", "enc_mf_barrow_ghouls", "enc_mf_barrow_ghouls_north");
            Expect("mf_corwin_rest", "bell");
            Done("enc_mf_coven_watch", "enc_mf_statue_hags", "enc_mf_gate_road_hag");
            s.Flags.Set("mf_gall_defeated");   // Auntie Gall's own done flag (the main story's fight)
            Expect("mf_coven_bounty", "return");
            for (int i = 1; i <= 4; i++) s.Flags.Set($"sr_banner_{i}_struck");
            Done("enc_sr_vanguard_pickets", "enc_sr_vanguard_west", "enc_sr_vanguard_altar");
            Expect("sr_strike_the_colours", "report");
            Done("enc_r2_whelp_clutch", "enc_r2_terrace");
            s.GiveItem("r2_stolen_stores", 3);
            Expect("r2_small_fires", "return");
        }

        // ===================================================================================================== The Long Watch

        [Test]
        public static void LongWatch_AubricMetBeforeTheQuest_StillCompletes()
        {
            var s = SessionTest.NewGame(ClassId.Paladin, 30, 822);
            s.Flags.Set("found_frozen_sanctum");
            s.Flags.Set(WorldRules.EncounterDoneFlag("enc_dg6_rimeheart"));   // the Sanctum cleared without the quest
            s.EnterMap("dgn_frozen_sanctum", "default");
            Assert(s.MapId == "dgn_frozen_sanctum", "into the sanctum: " + s.LastError);
            Assert(s.VisibleNpcs().Any(n => n.npc == "dg6_aubric"), "Aubric rises from the ice");
            Assert(s.StartDialogue(Db.Npcs["dg6_aubric"].dialogue, "dg6_aubric"), "talk to Aubric");
            SessionTest.Pick(s, "The Rimeheart is gone");
            SessionTest.Finish(s, "(Continue.)");
            Assert(s.Flags.IsSet("dg6_knights_freed") && !s.VisibleNpcs().Any(n => n.npc == "dg6_aubric"), "the knights are free and Aubric fades");

            // later, Lumi asks: the stages already done are caught up, and her hand-in is what is left
            s.EnterMap("skyreach", "default");
            Assert(s.Quests.Start("dg6_the_long_watch"), "The Long Watch starts");
            s.EnterMap("dgn_frozen_sanctum", "default");
            Assert(s.Quests.GetStage("dg6_the_long_watch") == "lumi", "back in the sanctum: nothing left to do there (" + s.Quests.GetStage("dg6_the_long_watch") + ")");
            s.EnterMap("skyreach", "default");
            Assert(s.QuestMarkerOf("sr_lumi").Kind == QuestMarker.ReadyToTurnIn, "Lumi's yellow ?: " + s.QuestMarkerOf("sr_lumi"));
            Assert(s.StartDialogue("dlg_sr_lumi", "sr_lumi"), "talk to Lumi");
            SessionTest.Pick(s, "They're free, Lumi");
            SessionTest.Finish(s);
            Assert(s.Quests.IsCompleted("dg6_the_long_watch"), "The Long Watch complete");
        }

        // ===================================================================================================== hand-ins

        /// <summary>A conversation can end in many ways (Goodbye, "not yet", the shop, Esc); only the hand-in choice
        /// may complete a quest, so the last stage of every quest here is a Flag that choice sets, with its turnIn.</summary>
        [Test]
        public static void HandIns_AreFlagsSetByTheHandInChoice()
        {
            var bad = new List<string>();
            foreach (var q in Db.Quests.Values.Where(q => Mine(q.id)))
            {
                var last = q.stages.Last();
                if (last.objectives.Any(o => o.type == ObjectiveType.Talk) || string.IsNullOrEmpty(last.turnIn))
                    bad.Add($"{q.id}/{last.id}: ends on {string.Join(", ", last.objectives.Select(o => o.type + " " + o.target))}, turnIn '{last.turnIn}'");
            }
            Assert(bad.Count == 0, string.Join("\n    ", bad));
        }

        [Test]
        public static void HandIns_LeavingTheConversation_KeepsTheQuestOpen()
        {
            var s = SessionTest.NewGame(ClassId.Priest, 33, 833);

            // Twin Fires: "I'll be back with the gland" leaves; the hand-in takes the gland and gives the draughts
            s.Quests.Start("r2_twin_fires");
            Assert(s.Quests.SetStage("r2_twin_fires", "return"), "Twin Fires at its hand-in");
            s.GiveItem("r2_fire_gland", 1);
            Assert(s.QuestMarkerOf("r2_quartermaster").Kind == QuestMarker.ReadyToTurnIn, "Marta's yellow ?: " + s.QuestMarkerOf("r2_quartermaster"));
            Assert(s.StartDialogue("dlg_r2_marta", "r2_quartermaster"), "talk to Marta");
            SessionTest.Pick(s, "I'll be back with the gland");
            SessionTest.Finish(s);
            Assert(s.Quests.IsActive("r2_twin_fires") && s.CountItem("r2_fire_gland") == 1, "leaving keeps the quest open and the gland in the bags");
            Assert(s.StartDialogue("dlg_r2_marta", "r2_quartermaster"), "talk to Marta again");
            SessionTest.Pick(s, "One fire-gland");
            SessionTest.Finish(s);
            Assert(s.Quests.IsCompleted("r2_twin_fires") && s.CountItem("r2_fire_gland") == 0 && s.CountItem("r2_emberward_draught") == 5,
                "handed in: the gland taken, five draughts given");

            // The Undertow: Goodbye at Bloop's hub leaves the pearl where it is
            s.Quests.Start("dg5_undertow");
            Assert(s.Quests.SetStage("dg5_undertow", "return"), "The Undertow at its hand-in");
            s.GiveItem("mf_tidewitch_pearl", 1);
            Assert(s.StartDialogue("dlg_mf_bloop", "mf_oracle_bloop"), "talk to Bloop");
            SessionTest.Finish(s);
            Assert(s.Quests.IsActive("dg5_undertow") && s.CountItem("mf_tidewitch_pearl") == 1, "Goodbye keeps the quest open and the pearl in the bags");
            Assert(s.StartDialogue("dlg_mf_bloop", "mf_oracle_bloop"), "talk to Bloop again");
            SessionTest.Pick(s, "Here is her pearl");
            SessionTest.Finish(s);
            Assert(s.Quests.IsCompleted("dg5_undertow") && s.CountItem("mf_tidewitch_pearl") == 0, "handed in: the pearl taken");
        }

        // ===================================================================================================== drops

        /// <summary>Quest items that only creatures carry drop every time, so a game that fights each pack once (or
        /// makes peace at the Croaking Stones) can always finish the quest.</summary>
        [Test]
        public static void QuestItemsFromCreatures_AlwaysDrop()
        {
            var collect = Db.Quests.Values.Where(q => Mine(q.id)).SelectMany(q => q.stages).SelectMany(st => st.objectives)
                            .Where(o => o.type == ObjectiveType.Collect).Select(o => o.target).ToHashSet();
            var bad = new List<string>();
            foreach (var t in Db.LootTables.Values)
                foreach (var e in t.entries)
                    if (collect.Contains(e.item) && e.chance < 100f) bad.Add($"{t.id}: {e.item} at {e.chance}%");
            Assert(bad.Count == 0, string.Join(", ", bad));

            // the peaceful route: the scouts' and the stewpot camp's mirelings alone carry Obi's five lumps of glass
            int glass = Encounters().Where(x => x.enc.id == "enc_mf_mireling_scouts" || x.enc.id == "enc_mf_stewpot_camp")
                                    .SelectMany(x => x.enc.enemies).Count(u => Db.Creatures[u.creature].lootTable is string lt &&
                                        Db.LootTables[lt].entries.Any(e => e.item == "mf_fen_glass"));
            Assert(glass >= 5, "fen-glass on the peaceful route: " + glass);
        }

        // ===================================================================================================== Mirefen

        /// <summary>The Croaking Stones offer is not gated on a flag the Reeve's own greeting sets (the marker's dry run
        /// does not apply outcomes on the way), so her yellow ! shows before the party has met her.</summary>
        [Test]
        public static void ReeveTamsin_ShowsTheCroakingStonesBeforeSheIsMet()
        {
            var s = SessionTest.NewGame(ClassId.Shaman, 20, 877);
            s.EnterMap("mirefen", "default");
            Assert(!s.Flags.IsSet("mf_met_reeve"), "the Reeve not met yet");
            var m = s.QuestMarkersOf("mf_reeve_tamsin").FirstOrDefault(x => x.QuestId == "mf_reeve_chief");
            Assert(m != null && m.Kind == QuestMarker.Available, "the Croaking Stones: yellow ! over the Reeve: " + (m?.ToString() ?? "no marker"));
        }

        // ===================================================================================================== Skyreach

        [Test]
        public static void RoostPortal_RevealedByAshOnTheWind()
        {
            var s = SessionTest.NewGame(ClassId.Hunter, 26, 844);
            s.EnterMap("skyreach", "default");
            var portal = s.MapDef.transitions.First(t => t.id == "to_raid_ashwyrm_roost");
            Assert(portal.hidden && !s.Map.IsTransitionVisible(portal), "the Roost's portal is not there at the start of Skyreach");
            s.Quests.Start("mq2_ash_on_the_wind");
            Assert(s.Quests.SetStage("mq2_ash_on_the_wind", "gate"), "Ash on the Wind: find the gate");
            var gate = s.MapDef.regions.First(r => r.id == "reg_sr_roost_gate");
            RaidTest.Pacify(s);
            SessionTest.WalkTo(s, gate.pos);
            Assert(s.Quests.GetStage("mq2_ash_on_the_wind") == "report" && s.Map.IsTransitionVisible(portal), "the gate is found and the portal opens");
        }

        [Test]
        public static void TwentyNine_TheJournalReachesYsolde()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 28, 855);
            s.Recruit("ysolde");   // she joins the party (the hand-over needs her there)
            s.Flags.Set("sr_journal_ysolde");   // "I'll carry it to her in Brightwater myself."
            s.GiveItem("sr_aubric_journal", 1);
            s.EnterMap("brightwater", "default");
            Assert(s.StartDialogue("dlg_bw_memorial", "bw_heronguard_memorial"), "at the memorial");
            SessionTest.Pick(s, "Aubric's field journal");
            SessionTest.Finish(s);
            Assert(s.CountItem("sr_aubric_journal") == 0, "Ysolde has the journal");
        }

        [Test]
        public static void SmallDataFixes()
        {
            var lash = Db.Ability("cr_sr_lightning_lash");
            Assert(CombatSounds.IsLightning(lash), "Lightning Lash sounds like lightning");
            var rime = Db.LootTables["lt_dg6_rimeheart"];
            Assert(rime.entries.Where(e => e.pool.Length > 0).All(e => e.partyUsable), "the Rimeheart's pools drop what the party can use");
            var tags = new List<string>();
            foreach (var d in Db.Dialogues.Values.Where(d => d.id.StartsWith("dlg_bw_")))
                foreach (var n in d.nodes)
                    foreach (var c in n.choices ?? new List<ChoiceDef>())
                        if (!string.IsNullOrEmpty(c.tag) && c.text.StartsWith("[")) tags.Add(d.id + "/" + n.id + ": " + c.text);
            Assert(tags.Count == 0, "the UI adds the tag label itself: " + string.Join("; ", tags));
            Assert(Db.Npcs["mf_corwin"].name != "Old Corwin", "Mirefen's old knight is not the fallen Sir Corwin");
        }

        // ===================================================================================================== Ashwyrm's Roost

        /// <summary>Every Roost boss has a hard enrage: past its round the boss gains the Ashwyrm's Fury and burns the
        /// whole summit every turn, so a party that only survives cannot outlast it.</summary>
        [Test]
        public static void RoostBosses_HardEnrage_WipesAPartyThatOnlySurvives()
        {
            foreach (var (enc, bosses) in new[] { ("enc_r2_cinder_drakes", new[] { "cr_r2_emberjaw", "cr_r2_ashtongue" }), ("enc_r2_varkas", new[] { "cr_r2_varkas" }),
                                                  ("enc_r2_vyrmathra", new[] { "cr_r2_vyrmathra" }) })
            {
                int round = 0;
                foreach (var id in bosses)
                {
                    var a = Db.Creatures[id].abilities.FirstOrDefault(x => x.ability == "cr_r2_ashwyrm_fury");
                    Assert(a != null && a.condition.StartsWith("roundAtLeast:"), id + ": the Ashwyrm's Fury on a round");
                    round = int.Parse(a.condition.Substring("roundAtLeast:".Length));
                    Assert(round >= 35, $"{id}: the fury comes after a fitting raid's win ({round})");
                }
                var s = SessionTest.NewGame(ClassId.Warrior, 32, 866);
                s.Settings.CompanionAutoPlay = true;
                foreach (var c in new[] { "bruna", "liora", "rook", "lys" }) s.Recruit(c);
                var home = Db.Maps["raid_ashwyrm_roost"];
                s.EnterMap(home.raidReturnMap, home.raidReturnSpawn);
                s.Flags.Set("mq2_ash_on_the_wind_done");
                Assert(s.EnterRaid("raid_ashwyrm_roost", "from_skyreach", new List<string> { GameSession.MainId, "bruna", "liora", "rook", "lys" }, true) == null,
                    "into the Roost: " + s.LastError);
                s.SetAutoPlay(s.Main, true);
                RaidTest.Pacify(s);
                if (enc == "enc_r2_vyrmathra") s.Flags.Set("r2_horn_blown");
                s.Map.ResetEncounter(enc);
                var b = s.StartEncounter(enc);
                Assert(b != null, enc + ": " + s.LastError);
                b.Round = round;   // the fight has gone on that long
                int guard = 0;
                while (!b.IsOver && b.Round <= round + 12 && guard++ < 100000)
                {
                    var u = b.ActiveUnit;
                    if (u == null) break;
                    AI.RunTurn(b, u);
                    if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
                }
                var foes = b.Units.Where(u => u.Team != b.PlayerTeam && u.Creature != null && bosses.Contains(u.Creature.id)).ToList();
                Assert(foes.Any(u => u.HasAura("cr_r2_ashwyrm_fury")), enc + ": the fury comes");
                Assert(b.Events.Any(e => e.Type == CombatEventType.AbilityUsed && e.AbilityId == "cr_r2_firestorm" || e.Type == CombatEventType.CastComplete && e.AbilityId == "cr_r2_firestorm"),
                    enc + ": the firestorm sweeps the summit");
                Assert(b.IsOver && b.Outcome == BattleOutcome.Defeat, $"{enc}: five who only survive are wiped (outcome {b.Outcome}, round {b.Round})");
            }
        }
    }
}
