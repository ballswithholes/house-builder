// Mirefen and the Drowned Vault (Docs/Expansion.md §8, builder "mirefen", prefixes mf / dg5): both maps can be entered and
// everything on them reached; the vault's hidden stair is revealed by the sunken statue's Perception check, the statue
// itself (Investigation) or Old Bloop's hint; the main story The Drowned Lanterns and every side quest are scripted to
// completion at the band's top level through the real GameSession (talk, walk, props, fights won by the party AI); the
// quest markers follow the level and quest gates; the zone's bosses are winnable by a fitting party but not trivial.
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
    public static class TestsContentMf
    {
        const string Fen = "mirefen", Vault = "dgn_drowned_vault", MQ = "mq2_drowned_lanterns";

        /// <summary>A scripted player: walks, talks, pokes props and lets the party AI fight (TestsSessionFullPlaythrough style).</summary>
        sealed class Player
        {
            public readonly GameSession S;
            public readonly List<string> Log = new List<string>();
            public readonly Dictionary<string, string[]> EncounterScripts = new Dictionary<string, string[]>(StringComparer.Ordinal);
            /// <summary>Per encounter: lowest share of the party's total health during the fight, rounds, deaths.</summary>
            public readonly Dictionary<string, (float minHp, int rounds, int deaths)> Fights = new Dictionary<string, (float, int, int)>(StringComparer.Ordinal);

            public Player(ClassId main, int level, ulong seed, params string[] companions)
            {
                S = SessionTest.NewGame(main, level, seed);
                S.Settings.CompanionAutoPlay = true;   // the party AI plays everyone
                foreach (var c in companions) S.Recruit(c);
                S.SetAutoPlay(S.Main, true);
                Assert(S.Party.Count == Math.Min(1 + companions.Length, S.PartySize), "the party: " + S.Party.Count);
            }

            void Note(string s) => Log.Add(s);
            string Trace => string.Join("\n      ", Log.Skip(Math.Max(0, Log.Count - 25)));

            public void Enter(string map, string spawn)
            {
                S.EnterMap(map, spawn);
                Assert(S.MapId == map, $"entered {map} ({S.LastError})");
                Note("== " + map);
            }

            /// <summary>Plays the running conversation: picks the first scripted choice offered (alternatives "a|b"), else an exit.</summary>
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
                        if (pick >= 0) { Note("  > " + todo[k]); todo.RemoveAt(k); }
                    }
                    if (pick < 0)
                        foreach (var leave in new[] { "Goodbye", "(Leave", "(Step back", "(Back away", "(Continue.)", "Not right now", "Not yet" })
                        {
                            var c = v.Choices.FirstOrDefault(x => x.Text.IndexOf(leave, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (c != null) { pick = c.Index; break; }
                        }
                    if (pick < 0 && v.Choices.Count > 0) pick = v.Choices[v.Choices.Count - 1].Index;
                    Assert(pick >= 0, $"no choice at {v.DialogueId}/{v.NodeId}: {SessionTest.Describe(v)} (wanted {string.Join(" | ", todo)})\n      {Trace}");
                    S.ChooseDialogue(pick);
                }
                Assert(!S.Dialogue.IsActive, "dialogue finished");
                Assert(todo.Count == 0, $"every scripted choice was offered (left: {string.Join(" | ", todo)})\n      {Trace}");
                if (S.Mode == SessionMode.Combat) Fight();
                TakeLoot();
            }

            public void TalkTo(string npcId, params string[] script)
            {
                var npc = S.VisibleNpcs().FirstOrDefault(n => n.npc == npcId);
                Assert(npc != null, $"{npcId} stands on {S.MapId}");
                Go(npc.pos, GameSession.InteractionRange - 0.4f);
                var r = S.TalkTo(npcId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npcId}: {r.Message}");
                Note("talk " + npcId);
                Converse(script);
            }

            public void Use(string interactId, params string[] script)
            {
                var p = S.MapDef.props.FirstOrDefault(x => x.interact == interactId);
                Assert(p != null && S.Map.IsPropVisible(p), $"prop {interactId} is there");
                Go(p.pos, GameSession.InteractionRange - 0.3f);
                var r = S.InteractProp(interactId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"use {interactId}: {r.Message}");
                Note("use " + interactId);
                Converse(script);
            }

            /// <summary>Walks to within <paramref name="near"/> of a point, winning every fight and answering every encounter on the way.</summary>
            public void Go(Vec2 dest, float near = 0.8f, int maxLegs = 40)
            {
                for (int leg = 0; leg < maxLegs; leg++)
                {
                    Assert(!S.IsGameOver, "game over\n      " + Trace);
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    TakeLoot();
                    if (Vec2.Distance(S.Leader.Position, dest) <= near) return;
                    var r = S.MoveLeader(dest);
                    if (r.Trigger.Stop)
                    {
                        Assert(r.Trigger.Kind != TriggerKind.Locked, "a locked way: " + r.Trigger.Id);
                        if (r.Trigger.Kind == TriggerKind.Travel || r.Trigger.Kind == TriggerKind.RaidGate) return;
                        continue;
                    }
                    if (Vec2.Distance(S.Leader.Position, dest) <= near + 0.3f) return;
                    if (!r.Moved) break;
                }
                Assert(Vec2.Distance(S.Leader.Position, dest) <= near + 1.5f, $"reached {dest} on {S.MapId} (at {S.Leader.Position})\n      {Trace}");
            }

            string[] EncounterScript()
            {
                var owner = S.Dialogue.OwnerId ?? "";
                if (EncounterScripts.TryGetValue(owner, out var script) && script != null)
                {
                    EncounterScripts[owner] = new string[0];
                    return script;
                }
                return new string[0];
            }

            public void WalkInto(string encId, params string[] script)
            {
                var enc = S.Map.FindEncounter(encId);
                Assert(enc != null, $"{encId} on {S.MapId}");
                if (script.Length > 0) EncounterScripts[encId] = script;
                if (S.Map.IsEncounterDone(enc)) { Note(encId + " already done (met on the way)"); return; }
                Assert(S.Map.IsEncounterAvailable(enc), $"{encId} is there to meet");
                Refresh();
                for (int leg = 0; leg < 16 && !S.Map.IsEncounterDone(enc); leg++)
                {
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    var r = S.MoveLeader(enc.pos);
                    if (r.Trigger.Stop) continue;
                    if (S.Mode == SessionMode.Exploration && !S.Map.IsEncounterDone(enc) && Vec2.Distance(S.Leader.Position, enc.pos) < enc.radius)
                        S.MoveLeader(enc.pos + new Vec2(enc.radius + 2.5f, 0f));
                }
                Assert(S.Map.IsEncounterDone(enc), $"{encId} resolved\n      {Trace}");
            }

            public void Fight()
            {
                var b = S.Battle;
                Assert(b != null, "a battle");
                var id = S.BattleEncounter?.id ?? "?";
                var party = b.Units.Where(u => u.Team == b.PlayerTeam && u.IsCharacter).ToList();
                float max = party.Sum(u => u.MaxHealth), min = 1f;
                int steps = 0;
                while (!b.IsOver && steps++ < 60000 && b.Round <= 100)
                {
                    var step = S.RunAIStep();
                    Assert(step != null || b.IsOver, $"AI step in {id}");
                    min = Math.Min(min, party.Sum(u => Math.Max(0f, u.Health)) / max);
                }
                int deaths = party.Count(u => !u.IsAlive);
                var sum = S.FinishBattle();
                Assert(sum != null && sum.Outcome == CombatEndKind.Victory, $"{id}: the party wins (outcome {sum?.Outcome}, round {b.Round}, level {S.Main.Level})\n      {Trace}");
                Fights[id] = (min, sum.Rounds, deaths);
                Note($"   {id}: won in {sum.Rounds} rounds, lowest party health {min:P0}, {deaths} down, +{sum.Xp} xp");
                TakeLoot();
            }

            public void TakeLoot()
            {
                if (S.PendingLoot != null) S.TakeAllLoot();
                foreach (var q in S.Quests.PendingRewardChoices.ToList())
                {
                    var choice = Db.Quests[q].rewards.choiceItems;
                    Assert(S.ClaimQuestReward(q, choice[0]) == null, "claim " + q);
                }
            }

            /// <summary>Full health and mana between fights (the party camps; Mirefen is a rest area).</summary>
            public void Refresh()
            {
                foreach (var u in S.PartyUnits()) u.RestoreFull();
            }

            public string Stage(string q) => S.Quests.GetStage(q);
        }

        static Player FittingParty(int level, ulong seed) =>
            new Player(ClassId.Warrior, level, seed, "seren", "rook", "lys", "torvan");

        // ===================================================================================================== maps

        [Test]
        public static void Maps_EnterAndReachEverything()
        {
            var s = SessionTest.NewGame(ClassId.Hunter, 22, 11);
            foreach (var id in new[] { Fen, Vault })
            {
                var m = Db.Maps[id];
                foreach (var sp in m.spawns)
                {
                    s.EnterMap(id, sp.id);
                    Assert(s.MapId == id && s.Nav.IsWalkable(s.Leader.Position, NavAgent.Default.IgnoringAllUnits()), $"{id}/{sp.id}: enter on walkable ground");
                }
                var bad = TestsMapsReachable.Unreachable(m);
                Assert(bad.Count == 0, $"{id}: everything reachable from default: {string.Join("; ", bad)}");
            }
            var fen = Db.Maps[Fen];
            Assert(fen.biome == "fen" && fen.environment == "outdoor" && fen.fill >= 0.3f && fen.fill <= 0.5f && fen.levelMin == 18 && fen.levelMax == 24 && fen.restArea,
                "Mirefen: fen biome, outdoor fill 0.3-0.5, band 18-24, a rest area");
            Assert(fen.paths.Count >= 8 && fen.water.Count >= 6 && fen.water.Any(w => !w.closed && w.crossings.Count >= 3), "paths, ponds and a channel with crossings");
            Assert(fen.props.Count(p => p.art == "prop_stilt_hut") >= 5 && fen.props.Count(p => p.art.StartsWith("prop_boardwalk")) >= 12, "stilt huts and boardwalks");
            Assert(fen.props.Count(p => p.light != null) >= 30, "lights for the night");
            var v = Db.Maps[Vault];
            Assert(v.dungeon && !v.restArea && v.environment == "cave" && v.fill >= 0.4f && v.fill <= 0.6f && v.levelMin == 24 && v.levelMax == 26, "the vault: a cave dungeon, no rest, band 24-26");
            Assert(v.encounters.Count >= 4 && v.encounters.Count <= 7 && v.chests.Count >= 2 && v.chests.Count <= 3, "4-7 encounters, 2-3 chests");
            int elitePacks = v.encounters.Count(e => e.enemies.Count >= 2 && e.enemies.Count(x => Db.Creatures[x.creature].rank == CreatureRank.Elite) >= 1);
            Assert(elitePacks >= 2, "at least two elite packs: " + elitePacks);
            foreach (var c in Db.Creatures.Values.Where(c => c.id.StartsWith("cr_mf_") || c.id.StartsWith("cr_dg5_")))
            {
                Assert(c.scaleToParty && c.levelFloor > 0 && c.levelCap >= c.levelFloor && c.levelOffset >= -1 && c.levelOffset <= 2, $"{c.id}: scaled, floored and capped");
                Assert(!string.IsNullOrEmpty(c.material) && !string.IsNullOrEmpty(c.voice), $"{c.id}: material and voice");
                if (c.id.StartsWith("cr_mf_")) Assert(c.levelFloor >= 18 && c.levelCap <= 25, $"{c.id}: in the zone band");
                else Assert(c.levelFloor == 24 && c.levelCap == 26, $"{c.id}: in the vault band");
            }
            // the Tidewitch: an Epic chance and a guaranteed Rare
            var lt = Db.LootTables["lt_dg5_tidewitch"];
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance < 100 && e.pool.All(i => Db.Items[i].quality == Quality.Epic)), "an Epic chance");
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance >= 100 && e.pool.All(i => Db.Items[i].quality == Quality.Rare)), "a guaranteed Rare");
        }

        // ===================================================================================================== the hidden stair

        [Test]
        public static void Vault_RevealedByTheStatuesCheck_TheStatue_OrBloop()
        {
            var reg = Db.Maps[Fen].regions.First(r => r.id == "reg_mf_sunken_statue");
            Assert(reg.check != null && reg.check.skill == SkillCheck.Perception && reg.check.dc >= 12 && reg.check.dc <= 16 && reg.checkFlag == "found_drowned_vault",
                "a Perception DC 12-16 check reveals the vault");
            var door = Db.Maps[Fen].transitions.First(t => t.id == "to_dgn_drowned_vault");
            Assert(door.hidden && door.revealFlag == "found_drowned_vault" && door.marker == "stairs", "the stair is hidden behind found_drowned_vault");
            Assert(Vec2.Distance(door.pos, reg.pos) < 4f, "the check stands at the stair");

            int found = 0, missed = 0;
            for (ulong seed = 1; seed <= 24 && (found == 0 || missed == 0); seed++)
            {
                var s = SessionTest.NewGame(ClassId.Hunter, 22, 300 + seed);
                s.EnterMap(Fen, "from_raid_hollow_heart");
                RaidTest.Pacify(s);
                Assert(!s.IsPassageRevealed(Fen, door.id) && s.Map.TransitionAt(door.pos) == null, "hidden at first");
                s.TakeEvents();
                var mouth = s.Map.SpawnPosition("from_dgn_drowned_vault");
                SessionTest.WalkTo(s, mouth);
                var ev = s.TakeEvents();
                var roll = ev.FirstOrDefault(e => e.Kind == SessionEventKind.SkillCheck && e.Id == reg.id);
                Assert(roll != null, "walking up to the statue rolls the check");
                bool ok = roll.Check.Success;
                Assert(s.Flags.IsSet("found_drowned_vault") == ok, "the flag follows the roll");
                if (ok)
                {
                    found++;
                    Assert(ev.Any(e => e.Kind == SessionEventKind.SecretFound && e.Id2 == door.id), "SecretFound for the stair");
                    var r = s.UseTransition(door.id);
                    Assert(r.Ok && s.MapId == Vault, "down the stair into the vault");
                    s.UseTransition("to_mirefen");
                    Assert(s.MapId == Fen && Vec2.Distance(s.Leader.Position, s.Map.SpawnPosition("from_dgn_drowned_vault")) < 3f, "and back up beside it");
                }
                else
                {
                    missed++;
                    // the check rolls once per save: stepping out and in again does not roll again
                    SessionTest.WalkTo(s, mouth + new Vec2(0f, -9f));
                    SessionTest.WalkTo(s, mouth);
                    Assert(!s.TakeEvents().Any(e => e.Kind == SessionEventKind.SkillCheck && e.Id == reg.id), "no second roll");
                    // the statue itself (Investigation), or Old Bloop's hint
                    var p = new Player(ClassId.Mage, 22, 900 + seed);
                    p.Enter(Fen, "from_raid_hollow_heart");
                    RaidTest.Pacify(p.S);
                    p.S.Quests.SetStage("mf_oracle_totems", "return");
                    p.TalkTo("mf_oracle_bloop", "The four totems are broken");
                    Assert(p.S.Flags.IsSet("found_drowned_vault") && p.S.Quests.IsCompleted("mf_oracle_totems"), "Bloop's hint reveals the stair");
                    Assert(p.S.UseTransition(door.id).Ok && p.S.MapId == Vault, "and it leads down");
                }
            }
            Assert(found > 0 && missed > 0, $"the Perception check can pass and fail ({found} found, {missed} missed)");
            var st = Db.Dialogues["dlg_mf_sunken_statue"];
            Assert(st.nodes.Any(n => n.choices.Any(c => c.check != null && c.check.skill == SkillCheck.Investigation)) &&
                   st.nodes.Any(n => n.outcomes.Any(o => o.type == OutcomeType.SetFlag && o.key == "found_drowned_vault")), "the statue's Investigation check is another way in");
        }

        // ===================================================================================================== the main story

        [Test]
        public static void DrownedLanterns_FromPenhallowToTheSending_AtTheBandsTop()
        {
            var p = FittingParty(24, 4141);
            var s = p.S;
            s.Flags.Set("mq2_ember_road_done");
            p.Enter("brightwater", "default");
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
            p.Converse("about the fens");
            Assert(s.Quests.IsActive(MQ) && p.Stage(MQ) == "fen", "The Drowned Lanterns starts at 'fen'");

            p.Enter(Fen, "from_brightwater");
            Assert(p.Stage(MQ) == "reeve", "reaching Mirefen: find the Reeve");
            Assert(s.QuestMarkerOf("mf_reeve_tamsin").Kind == QuestMarker.ReadyToTurnIn, "the Reeve's marker: " + s.QuestMarkerOf("mf_reeve_tamsin"));
            p.TalkTo("mf_reeve_tamsin", "Penhallow of Brightwater sent me", "Then I'll go and stare", "I'll go now");
            Assert(p.Stage(MQ) == "cords", "then the Mere: " + p.Stage(MQ));

            for (int i = 1; i <= 3; i++) p.Use("mf_drowned_lantern_" + i, "(Cut the cord");
            Assert(p.Stage(MQ) == "coven" && s.CountItem("mf_drowning_cord") == 3, "three drowning cords");
            p.TalkTo("mf_reeve_tamsin", "I cut these from the drowned lanterns", "Then I'll go and have a word", "I'll be careful");
            Assert(p.Stage(MQ) == "gall" && s.Flags.IsSet("mf_coven_known") && s.CountItem("mf_drowning_cord") == 0, "the coven is named; Auntie Gall waits at the moon-gate");

            p.Refresh();
            p.WalkInto("enc_mf_auntie_gall", "It ends now");
            Assert(p.Stage(MQ) == "sen" && s.Flags.IsSet("mf_gall_defeated"), "Auntie Gall is gone");
            var gall = p.Fights["enc_mf_auntie_gall"];
            Console.WriteLine($"    mirefen: Auntie Gall vs a party of 5 at 24: {gall.rounds} rounds, lowest party health {gall.minHp:P0}, {gall.deaths} down");
            Assert(gall.rounds >= 3 && (gall.minHp < 0.9f || gall.deaths > 0), $"Auntie Gall is a real fight ({gall.rounds} rounds, lowest {gall.minHp:P0}, {gall.deaths} down)");

            var dark = s.MapDef.props.First(x => x.interact == "mf_drowned_lantern_1");
            Assert(s.Map.IsPropVisible(dark), "the drowned lanterns are still dark");
            p.TalkTo("mf_granny_sen", "I'll sing it");
            Assert(p.Stage(MQ) == "report" && s.Flags.IsSet("mf_lanterns_rise"), "the sending is sung");
            int lit = s.MapDef.props.Count(x => x.art == "prop_spirit_lantern" && s.Map.IsPropVisible(x));
            Assert(!s.Map.IsPropVisible(dark), "the drowned lanterns are dark no longer");
            Assert(lit >= 8, $"the lanterns float again: {lit} of {s.MapDef.props.Count(x => x.art == "prop_spirit_lantern")} on {s.MapId}, flag {s.Flags.IsSet("mf_lanterns_rise")} test {s.Flags.Test("mf_lanterns_rise")}");
            Assert(!s.VisibleNpcs().Any(n => n.npc == "mf_granny_sen"), "Granny Sen has gone on");

            p.Enter("brightwater", "from_mirefen");
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
            p.Converse("I've been to Mirefen");
            Assert(s.Quests.IsCompleted(MQ) && s.Flags.IsSet("mq2_drowned_lanterns_done"), "complete; mq2_drowned_lanterns_done is set");
        }

        // ===================================================================================================== the side quests

        [Test]
        public static void SideQuests_EveryOneScriptedToTheEnd()
        {
            var p = FittingParty(24, 5151);
            var s = p.S;
            p.Enter(Fen, "default");
            s.Flags.Set("mf_coven_known");   // the main story's coven stage (the bounty's gate)

            // Lowlantern first: the bounty, the Duke, the nets, the glass
            p.TalkTo("mf_reeve_tamsin", "Is anyone paying for coven hags", "Consider it done");
            p.TalkTo("mf_lily", "Who are you looking for", "I'll find your Duke");
            p.TalkTo("mf_bo_puddlefoot", "Rough day", "I'll deal with your crocolisks");
            p.TalkTo("mf_obi_wick", "Lowlantern looks short of lanterns", "I'll find you some");
            p.TalkTo("mf_hettie_brine", "That chowder smells", "I'll fetch them");
            foreach (var q in new[] { "mf_coven_bounty", "mf_lily_duke", "mf_bo_nets", "mf_obi_glass", "mf_hettie_soup" })
                Assert(s.Quests.IsActive(q), q + " started");

            // Teeth in the Nets: crocolisks, then Old Gnasher (a lone elite)
            foreach (var e in new[] { "enc_mf_shallows_crocs", "enc_mf_shallows_crocs_east", "enc_mf_channel_crocs" }) { p.Refresh(); p.WalkInto(e); }
            Assert(p.Stage("mf_bo_nets") != "crocs", "five crocolisks: " + p.Stage("mf_bo_nets"));
            p.Refresh();
            if (!s.Map.IsEncounterDone(s.Map.FindEncounter("enc_mf_old_gnasher"))) p.WalkInto("enc_mf_old_gnasher");   // (the west road passes his bank)
            Assert(p.Stage("mf_bo_nets") == "return", "Old Gnasher defeated");
            var gn = p.Fights["enc_mf_old_gnasher"];
            Console.WriteLine($"    mirefen: Old Gnasher: {gn.rounds} rounds, lowest party health {gn.minHp:P0}; the Bellringer and the rest: " +
                string.Join(", ", p.Fights.Where(kv => kv.Value.minHp < 0.8f).Select(kv => $"{kv.Key} {kv.Value.minHp:P0}")));
            Assert(gn.rounds >= 2, $"Old Gnasher takes a while ({gn.rounds} rounds)");
            p.TalkTo("mf_bo_puddlefoot", "Old Gnasher's done");
            Assert(s.Quests.IsCompleted("mf_bo_nets"), "Teeth in the Nets complete");

            // Soup of the Mire: every crocolisk drops a tail (six crocolisks and Old Gnasher so far), and three truffles
            Assert(s.CountItem("mf_croc_tail") >= 4, "the crocolisks' tails: " + s.CountItem("mf_croc_tail"));
            for (int i = 1; i <= 3; i++) p.Use("mf_truffle_" + i, "(Dig gently");
            p.TalkTo("mf_hettie_brine", "Four crocolisk tails");
            Assert(s.Quests.IsCompleted("mf_hettie_soup"), "Soup of the Mire complete");

            // The Duke of Puddles: the ring, the willow, the stewpot (set free)
            p.Use("mf_duke_ring", "(Look for a trail");
            p.Use("mf_duke_willow", "(Look for the trail again");
            p.Refresh();
            p.WalkInto("enc_mf_stewpot_camp");
            p.Use("mf_duke_pot", "(Watch the two frogs", "(Lift them both");
            Assert(s.Flags.IsSet("mf_duke_free") && p.Stage("mf_lily_duke") == "return", "the Duke goes free with Lady Bubbles");
            p.TalkTo("mf_lily", "He's safe");
            Assert(s.Quests.IsCompleted("mf_lily_duke"), "The Duke of Puddles complete");

            // Rest for the Drowned: six bog ghouls, then the Low Bell and the Bellringer
            p.TalkTo("mf_corwin", "What keeps you out here", "I'll help you lay them down");
            foreach (var e in new[] { "enc_mf_mere_ghouls", "enc_mf_barrow_ghouls", "enc_mf_barrow_ghouls_north" }) { p.Refresh(); p.WalkInto(e); }
            Assert(p.Stage("mf_corwin_rest") == "bell", "six ghouls at rest: " + p.Stage("mf_corwin_rest"));
            p.Refresh();
            p.Use("mf_low_bell", "(Ring the Low Bell");
            Assert(s.Flags.IsSet("mf_bell_rung"), "the bell rings");
            p.WalkInto("enc_mf_low_bell");
            Assert(p.Stage("mf_corwin_rest") == "return", "the Bellringer laid to rest");
            p.TalkTo("mf_corwin", "The barrows are quiet");
            Assert(s.Quests.IsCompleted("mf_corwin_rest") && s.Flags.IsSet("mf_heard_heart_hungry"), "Rest for the Drowned complete");

            // Fen-Glass and Wisp-Light, then Light the Way (the last post wakes an ambush)
            // (no top-ups: every mireling carries fen-glass and every wisp a glow, so the stewpot camp, the scouts and two
            // wisp flocks are enough, even on the peaceful route where the Croaker camp and the Stones are never fought)
            foreach (var e in new[] { "enc_mf_mireling_scouts", "enc_mf_mere_wisps", "enc_mf_willow_wisps" }) { p.Refresh(); p.WalkInto(e); }
            Assert(s.CountItem("mf_fen_glass") >= 5 && s.CountItem("mf_wisp_glow") >= 4, $"glass {s.CountItem("mf_fen_glass")}, glow {s.CountItem("mf_wisp_glow")}");
            p.TalkTo("mf_obi_wick", "Five lumps of fen-glass");
            Assert(s.Quests.IsCompleted("mf_obi_glass"), "Fen-Glass and Wisp-Light complete");
            p.TalkTo("mf_obi_wick", "What will you make", "I'll hang them");
            Assert(s.CountItem("mf_new_lantern") == 4, "four post-lamps");
            for (int i = 1; i <= 4; i++) { p.Refresh(); p.Use("mf_post_" + i, "(Hang one"); }
            Assert(p.Stage("mf_obi_posts") == "return", "the road is lit");
            p.Refresh();
            p.WalkInto("enc_mf_post_ambush");
            p.TalkTo("mf_obi_wick", "The Fen Road is lit");
            Assert(s.Quests.IsCompleted("mf_obi_posts"), "Light the Way complete");

            // Unbinding the Totems (any route that works)
            p.Go(new Vec2(31f, 10f));
            p.TalkTo("mf_oracle_bloop", "What do the coven totems do", "I'll break them");
            // (Bloop's hollow lies past the Croaking Stones: go round the ring, Gubbagulp is for later)
            foreach (int i in new[] { 2, 4, 3, 1 })
                for (int tries = 0; tries < 12 && !s.Flags.IsSet($"mf_totem_{i}_broken"); tries++)
                    p.Use("mf_totem_" + i, "(Smash the skull|(Unpick the binding");
            Assert(p.Stage("mf_oracle_totems") == "return", "four totems broken");
            p.Go(new Vec2(31f, 10f));
            p.TalkTo("mf_oracle_bloop", "The four totems are broken");
            Assert(s.Quests.IsCompleted("mf_oracle_totems") && s.Flags.IsSet("found_drowned_vault"), "Unbinding the Totems complete; the stair is revealed");

            // The Croaking Stones: peace, now that the totems are silent
            p.Go(new Vec2(31f, 10f));
            Assert(!s.Flags.IsSet("mf_chief_resolved"), "Gubbagulp not met yet");
            p.TalkTo("mf_reeve_tamsin", "frog problem", "I'll go and talk to him");
            p.WalkInto("enc_mf_croaking_stones", "The coven's totems are broken");
            Assert(s.Flags.IsSet("mf_chief_peace") && s.CountItem("mf_gubbagulps_pearl") == 1, "Gubbagulp makes peace and sends his pearl");
            Assert(s.VisibleNpcs().Any(n => n.npc == "mf_plip") && !s.Map.IsEncounterAvailable(s.Map.FindEncounter("enc_mf_croaker_camp")),
                "the consequence: Plip trades in Lowlantern, the Croaker camp stands down");
            p.TalkTo("mf_reeve_tamsin", "Gubbagulp won't be cutting");
            Assert(s.Quests.IsCompleted("mf_reeve_chief"), "The Croaking Stones complete");

            // Bounty: the coven's hags, Auntie Gall and her daughters among them
            // (round the vault's stair, which the totems revealed: the gate road, the watch, Gall's ring, then the statue)
            foreach (var e in new[] { "enc_mf_gate_road_hag", "enc_mf_coven_watch" }) { p.Refresh(); p.WalkInto(e); }
            p.Refresh();
            p.WalkInto("enc_mf_auntie_gall", "It ends now");
            Assert(p.Stage("mf_coven_bounty") == "hags", "the statue's hags still stand: " + p.Stage("mf_coven_bounty"));
            p.Refresh();
            p.WalkInto("enc_mf_statue_hags");
            Assert(p.Stage("mf_coven_bounty") == "return", "the hags and Gall: " + p.Stage("mf_coven_bounty"));
            p.TalkTo("mf_reeve_tamsin", "The coven is five hags fewer");
            Assert(s.Quests.IsCompleted("mf_coven_bounty"), "the bounty paid");
        }

        // ===================================================================================================== the Drowned Vault

        [Test]
        public static void Undertow_TheVaultClearedAndTheTidewitchBeaten()
        {
            var p = FittingParty(25, 6262);
            var s = p.S;
            p.Enter(Fen, "from_raid_hollow_heart");
            RaidTest.Pacify(s);
            s.Quests.Complete("mf_oracle_totems");
            p.TalkTo("mf_oracle_bloop", "Where does the undertow go", "I'll go down and stop her");
            Assert(s.Quests.IsActive("dg5_undertow") && s.Flags.IsSet("found_drowned_vault"), "The Undertow starts; the stair is shown");
            var door = s.MapDef.transitions.First(t => t.id == "to_dgn_drowned_vault");
            p.Go(door.pos, 0.2f);
            Assert(s.MapId == Vault, "down the stair");
            foreach (var e in new[] { "enc_dg5_entry_thralls", "enc_dg5_alcove_thralls", "enc_dg5_causeway", "enc_dg5_sentinels", "enc_dg5_snapjaws", "enc_dg5_captain" })
            {
                p.Refresh();
                p.WalkInto(e);
            }
            p.Refresh();
            p.WalkInto("enc_dg5_tidewitch", "Give the lights back");
            var tw = p.Fights["enc_dg5_tidewitch"];
            Console.WriteLine($"    mirefen: the Tidewitch vs a party of 5 at 25: {tw.rounds} rounds, lowest party health {tw.minHp:P0}, {tw.deaths} down");
            Assert(tw.rounds >= 3 && (tw.minHp < 0.9f || tw.deaths > 0), $"the Tidewitch is a real fight ({tw.rounds} rounds, lowest {tw.minHp:P0}, {tw.deaths} down)");
            Assert(s.Flags.IsSet("dg5_tidewitch_defeated") && p.Stage("dg5_undertow") == "return" && s.CountItem("mf_tidewitch_pearl") == 1, "her pearl");
            Assert(s.MapDef.props.Count(x => x.art == "prop_spirit_lantern" && s.Map.IsPropVisible(x)) >= 6, "the drowned lanterns at the Heart-Well light up");
            var hoard = s.MapDef.chests.First(c => c.id == "chest_dg5_hoard");
            Assert(s.Map.IsChestAvailable(hoard), "her hoard opens");
            p.Go(hoard.pos, 1.5f);
            var r = s.OpenChest(hoard.id);
            Assert(r.Ok, "open the hoard: " + r.Message);
            p.TakeLoot();
            p.Go(s.MapDef.transitions.First(t => t.id == "to_mirefen").pos, 0.2f);
            Assert(s.MapId == Fen, "back up into the fen");
            RaidTest.Pacify(s);
            p.TalkTo("mf_oracle_bloop", "Here is her pearl");
            Assert(s.Quests.IsCompleted("dg5_undertow"), "The Undertow complete");
        }

        // ===================================================================================================== bosses and markers

        [Test]
        public static void Bosses_TooMuchForTwo_AFittingPartyWins()
        {
            // a fitting party of five at the band's top beats Auntie Gall (TestsContentMf main story);
            // two characters a level under the band do not
            var p = new Player(ClassId.Mage, 23, 7373, "lys");
            p.Enter(Fen, "from_raid_hollow_heart");
            RaidTest.Pacify(p.S);
            p.S.Flags.Set("mf_coven_known");
            p.S.Map.ResetEncounter("enc_mf_auntie_gall");
            var b = p.S.StartEncounter("enc_mf_auntie_gall");
            Assert(b != null, "fight Auntie Gall: " + p.S.LastError);
            var outcome = p.S.AutoResolve(80);
            Assert(outcome != BattleOutcome.Victory, "two mages do not beat Auntie Gall: " + outcome);

            // nor the Tidewitch, for two at the vault's band
            var q = new Player(ClassId.Warrior, 25, 7474, "seren");
            q.Enter(Vault, "default");
            RaidTest.Pacify(q.S);
            q.S.Map.ResetEncounter("enc_dg5_tidewitch");
            Assert(q.S.StartEncounter("enc_dg5_tidewitch") != null, "fight the Tidewitch: " + q.S.LastError);
            outcome = q.S.AutoResolve(80);
            Assert(outcome != BattleOutcome.Victory, "a warrior and a priest do not beat the Tidewitch: " + outcome);
        }

        [Test]
        public static void Markers_FollowTheLevelAndQuestGates()
        {
            var s = SessionTest.NewGame(ClassId.Priest, 18, 81);
            s.EnterMap(Fen, "default");
            Assert(s.QuestMarkerOf("mf_bo_puddlefoot").Kind == QuestMarker.Available, "Bo offers at 18");
            Assert(s.QuestMarkerOf("mf_lily").Kind == QuestMarker.Available, "Lily offers at 18");
            var cor = s.QuestMarkerOf("mf_corwin");
            Assert(cor.Kind == QuestMarker.AvailableLater && cor.Level == 19, "Corwin: grey ! for level 19: " + cor);
            var hettie = s.QuestMarkerOf("mf_hettie_brine");
            Assert(hettie.Kind == QuestMarker.AvailableLater && hettie.Level == 21, "Hettie: grey ! for 21: " + hettie);
            Assert(s.QuestMarkerOf("mf_obi_wick").QuestId == "mf_obi_glass", "Obi offers the glass first");
            s.Quests.Start("mf_obi_glass");
            s.Quests.Complete("mf_obi_glass");
            Assert(s.QuestMarkerOf("mf_obi_wick").Kind == QuestMarker.AvailableLater, "Light the Way waits for 19 and the glass");
            s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
            Assert(s.Main.Level == 19 && s.QuestMarkerOf("mf_obi_wick").Kind == QuestMarker.Available, "then it is offered");
            // every Mirefen quest has an offer gated by its level, and a CompleteQuest or final Talk at its turn-in
            foreach (var q in Db.Quests.Values.Where(q => q.zone == Fen))
            {
                Assert(q.minLevel >= 18 && q.minLevel <= 24 && Db.Npcs.ContainsKey(q.giver), $"{q.id}: level gate and giver");
                Assert(s.QuestMarkerIndex.Starters(q.id).Contains(q.giver), $"{q.id}: {q.giver} offers it");
                Assert(s.QuestMarkerIndex.Enders(q.id).Count > 0, $"{q.id}: has a turn-in");
            }
        }
    }
}
