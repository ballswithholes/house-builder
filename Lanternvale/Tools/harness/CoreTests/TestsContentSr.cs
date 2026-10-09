// Skyreach Peaks and the Frozen Sanctum (Docs/Expansion.md §8, builder "skyreach", prefixes sr / dg6): both maps can be
// entered and everything on them reached; the sanctum's hidden door is revealed by the frozen falls' Perception check,
// the ice spire beside it (Investigation) or Odran's hint; the main story Ash on the Wind and every side quest are scripted
// to completion at the band's top level through the real GameSession (talk, walk, props, fights won by the party AI); the
// choices change the world (Gorrum talked down: the ledge camps go, Gorrum trades); the quest markers follow the level and
// quest gates; Vanguard-Marshal Kaedric and the Rimeheart are winnable by a fitting party but not by two.
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
    public static class TestsContentSr
    {
        const string Peaks = "skyreach", Sanctum = "dgn_frozen_sanctum", MQ = "mq2_ash_on_the_wind";

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

            /// <summary>Full health and mana between fights (the party camps; Skyreach is a rest area).</summary>
            public void Refresh()
            {
                foreach (var u in S.PartyUnits()) u.RestoreFull();
            }

            public string Stage(string q) => S.Quests.GetStage(q);

            public void TopUp(string item, int n)
            {
                if (S.CountItem(item) < n) S.GiveItem(item, n - S.CountItem(item));
            }
        }

        static Player FittingParty(int level, ulong seed, string fifth = "torvan") =>
            new Player(ClassId.Warrior, level, seed, "seren", "rook", "lys", fifth);

        // ===================================================================================================== maps

        [Test]
        public static void Maps_EnterAndReachEverything()
        {
            var s = SessionTest.NewGame(ClassId.Hunter, 28, 12);
            foreach (var id in new[] { Peaks, Sanctum })
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
            var peaks = Db.Maps[Peaks];
            Assert(peaks.biome == "peaks" && peaks.environment == "outdoor" && peaks.fill >= 0.3f && peaks.fill <= 0.5f && peaks.levelMin == 24 &&
                   peaks.levelMax == 30 && peaks.restArea && peaks.ambient.snow, "Skyreach: peaks biome, outdoor fill 0.3-0.5, band 24-30, a rest area, snow");
            Assert(peaks.paths.Count >= 8 && peaks.water.Any(w => w.closed) && peaks.water.Any(w => !w.closed && w.crossings.Count >= 2),
                "switchback paths, the tarn and the Tarnrun with a bridge and a ford");
            Assert(peaks.props.Count(p => p.art == "prop_pine_snow") >= 40 && peaks.props.Count(p => p.art == "prop_mountain_hut") >= 3 &&
                   peaks.props.Count(p => p.art == "prop_prayer_flags") >= 4 && peaks.props.Any(p => p.art == "prop_ruined_tower") &&
                   peaks.props.Any(p => p.art == "prop_dragon_bones") && peaks.props.Count(p => p.art == "prop_ice_spire") >= 10,
                "pinewoods, the hut village, prayer flags, the Heronguard tower, dragon bones and ice spires");
            Assert(peaks.props.Count(p => p.light != null) >= 20, "lights for the night: " + peaks.props.Count(p => p.light != null));
            var portal = peaks.transitions.First(t => t.id == "to_raid_ashwyrm_roost");
            Assert(portal.pos.x == 20f && portal.pos.y == 56f && portal.marker == "portal", "the Roost gate stays at (20, 56)");
            var v = Db.Maps[Sanctum];
            Assert(v.dungeon && !v.restArea && v.environment == "cave" && v.biome == "ice_cave" && v.fill >= 0.4f && v.fill <= 0.6f && v.levelMin == 30 && v.levelMax == 32,
                "the sanctum: an ice cave dungeon, no rest, band 30-32");
            Assert(v.encounters.Count >= 4 && v.encounters.Count <= 7 && v.chests.Count >= 2 && v.chests.Count <= 3, "4-7 encounters, 2-3 chests");
            int elitePacks = v.encounters.Count(e => e.enemies.Count >= 2 && e.enemies.Count(x => Db.Creatures[x.creature].rank == CreatureRank.Elite) >= 1);
            Assert(elitePacks >= 2, "at least two elite packs: " + elitePacks);
            Assert(v.props.Count(p => p.art == "prop_frozen_statue") >= 8 && v.props.Count(p => p.art == "prop_ice_pillar") >= 8, "frozen statues and ice pillars");
            foreach (var c in Db.Creatures.Values.Where(c => c.id.StartsWith("cr_sr_") || c.id.StartsWith("cr_dg6_")))
            {
                Assert(c.scaleToParty && c.levelFloor > 0 && c.levelCap >= c.levelFloor && c.levelOffset >= -1 && c.levelOffset <= 2, $"{c.id}: scaled, floored and capped");
                Assert(!string.IsNullOrEmpty(c.material) && !string.IsNullOrEmpty(c.voice), $"{c.id}: material and voice");
                if (c.id.StartsWith("cr_sr_")) Assert(c.levelFloor == 24 && c.levelCap == 30, $"{c.id}: in the zone band");
                else Assert(c.levelFloor == 30 && c.levelCap == 32, $"{c.id}: in the sanctum band");
            }
            var boss = Db.Creatures["cr_dg6_rimeheart"];
            Assert(boss.rank == CreatureRank.Boss && boss.sprite == "cr_rimeheart", "the Rimeheart: a boss with its model");
            // the Rimeheart: an Epic chance and a guaranteed Rare
            var lt = Db.LootTables["lt_dg6_rimeheart"];
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance < 100 && e.pool.All(i => Db.Items[i].quality == Quality.Epic)), "an Epic chance");
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance >= 100 && e.pool.All(i => Db.Items[i].quality == Quality.Rare)), "a guaranteed Rare");
        }

        // ===================================================================================================== the hidden door

        [Test]
        public static void Sanctum_RevealedByTheFallsCheck_TheIce_OrOdran()
        {
            var reg = Db.Maps[Peaks].regions.First(r => r.id == "reg_sr_frozen_falls");
            Assert(reg.check != null && reg.check.skill == SkillCheck.Perception && reg.check.dc >= 12 && reg.check.dc <= 16 && reg.checkFlag == "found_frozen_sanctum",
                "a Perception DC 12-16 check at the frozen falls reveals the sanctum");
            var door = Db.Maps[Peaks].transitions.First(t => t.id == "to_dgn_frozen_sanctum");
            Assert(door.hidden && door.revealFlag == "found_frozen_sanctum" && door.marker == "cave", "the door is hidden behind found_frozen_sanctum");
            Assert(Vec2.Distance(door.pos, reg.pos) < 5f, "the check stands at the door");

            int found = 0, missed = 0;
            for (ulong seed = 1; seed <= 24 && (found == 0 || missed == 0); seed++)
            {
                var s = SessionTest.NewGame(ClassId.Warrior, 28, 400 + seed);
                s.EnterMap(Peaks, "cairnhollow");
                RaidTest.Pacify(s);
                Assert(!s.IsPassageRevealed(Peaks, door.id) && s.Map.TransitionAt(door.pos) == null, "hidden at first");
                s.TakeEvents();
                var mouth = s.Map.SpawnPosition("from_dgn_frozen_sanctum");
                SessionTest.WalkTo(s, mouth);
                var ev = s.TakeEvents();
                var roll = ev.FirstOrDefault(e => e.Kind == SessionEventKind.SkillCheck && e.Id == reg.id);
                Assert(roll != null, "walking up to the falls rolls the check");
                bool ok = roll.Check.Success;
                Assert(s.Flags.IsSet("found_frozen_sanctum") == ok, "the flag follows the roll");
                if (ok)
                {
                    found++;
                    Assert(ev.Any(e => e.Kind == SessionEventKind.SecretFound && e.Id2 == door.id), "SecretFound for the door");
                    var r = s.UseTransition(door.id);
                    Assert(r.Ok && s.MapId == Sanctum, "through the door into the sanctum");
                    s.UseTransition("to_skyreach");
                    Assert(s.MapId == Peaks && Vec2.Distance(s.Leader.Position, s.Map.SpawnPosition("from_dgn_frozen_sanctum")) < 3f, "and back out beside it");
                }
                else
                {
                    missed++;
                    // the check rolls once per save: stepping out and in again does not roll again
                    SessionTest.WalkTo(s, mouth + new Vec2(0f, -9f));
                    SessionTest.WalkTo(s, mouth);
                    Assert(!s.TakeEvents().Any(e => e.Kind == SessionEventKind.SkillCheck && e.Id == reg.id), "no second roll");
                    // Odran's hint (at level 28, or with Aubric's journal)
                    var p = new Player(ClassId.Mage, 28, 900 + seed);
                    p.Enter(Peaks, "cairnhollow");
                    RaidTest.Pacify(p.S);
                    p.TalkTo("sr_guide_odran", "Pleased to meet you", "Any old stories about Glasswater Falls");
                    Assert(p.S.Flags.IsSet("found_frozen_sanctum"), "Odran's story reveals the door");
                    Assert(p.S.UseTransition(door.id).Ok && p.S.MapId == Sanctum, "and it leads in");
                }
            }
            Assert(found > 0 && missed > 0, $"the Perception check can pass and fail ({found} found, {missed} missed)");
            var ice = Db.Dialogues["dlg_sr_frozen_falls"];
            Assert(ice.nodes.Any(n => n.choices.Any(c => c.check != null && c.check.skill == SkillCheck.Investigation)) &&
                   ice.nodes.Any(n => n.outcomes.Any(o => o.type == OutcomeType.SetFlag && o.key == "found_frozen_sanctum")), "the ice spire's Investigation check is another way in");
            // the shaman simply listens to the water behind the ice
            var sh = new Player(ClassId.Shaman, 28, 77);
            sh.Enter(Peaks, "cairnhollow");
            RaidTest.Pacify(sh.S);
            sh.Use("sr_frozen_falls_ice", "(Listen to the water");
            Assert(sh.S.Flags.IsSet("found_frozen_sanctum") && sh.S.IsPassageRevealed(Peaks, door.id), "the shaman hears the door behind the falls");
        }

        // ===================================================================================================== the main story

        [Test]
        public static void AshOnTheWind_FromPenhallowToTheRoostGate_AtTheBandsTop()
        {
            var p = FittingParty(30, 2424);
            var s = p.S;
            p.Enter("brightwater", "default");
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
            Assert(SessionTest.ChoiceIndex(s.Dialogue.Current, "Where does the ash come from") < 0, "no Skyreach offer before The Drowned Lanterns is done");
            s.Dialogue.End();
            s.Flags.Set("mq2_drowned_lanterns_done");
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
            p.Converse("Where does the ash come from");
            Assert(s.Quests.IsActive(MQ) && p.Stage(MQ) == "peaks", "Ash on the Wind starts at 'peaks'");

            p.Enter(Peaks, "from_brightwater");
            Assert(p.Stage(MQ) == "guide", "reaching Skyreach: find a guide");
            Assert(s.QuestMarkerOf("sr_guide_odran").Kind == QuestMarker.ReadyToTurnIn, "Odran's marker: " + s.QuestMarkerOf("sr_guide_odran"));
            p.TalkTo("sr_guide_odran", "Pleased to meet you", "Archivist Penhallow sent me", "Who are the Dragonsworn", "I'll head for the Bone Field");
            Assert(p.Stage(MQ) == "bones", "then the Bone Field: " + p.Stage(MQ));

            p.TalkTo("sr_ottoline", "Penhallow sent me to read the ash", "What does that mean", "I'll find Ivo");
            Assert(s.Flags.IsSet("sr_bones_read") && p.Stage(MQ) == "scout", "the ash is read; find the scout: " + p.Stage(MQ));

            p.TalkTo("sr_scout_ivo", "Ottoline sent me", "I'll get those orders");
            Assert(p.Stage(MQ) == "marshal" && s.Flags.IsSet("sr_ivo_met"), "then the Marshal");

            p.Refresh();
            p.WalkInto("enc_sr_marshal");
            var k = p.Fights["enc_sr_marshal"];
            Console.WriteLine($"    skyreach: Vanguard-Marshal Kaedric vs a party of 5 at 30: {k.rounds} rounds, lowest party health {k.minHp:P0}, {k.deaths} down");
            Assert(k.rounds >= 3 && (k.minHp < 0.9f || k.deaths > 0), $"Kaedric is a real fight ({k.rounds} rounds, lowest {k.minHp:P0}, {k.deaths} down)");
            Assert(s.CountItem("sr_varkas_orders") == 1 && p.Stage(MQ) == "gate", "Highlord Varkas's orders; now the gate: " + p.Stage(MQ));

            p.Go(s.Map.SpawnPosition("from_raid_ashwyrm_roost"));
            Assert(p.Stage(MQ) == "report", "the Roost Gate found: " + p.Stage(MQ));
            Assert(s.MapId == Peaks, "the gate does not pull the party in by itself");

            p.Enter("brightwater", "from_skyreach");
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
            p.Converse("I've been to Skyreach");
            Assert(s.Quests.IsCompleted(MQ) && s.Flags.IsSet("mq2_ash_on_the_wind_done"), "complete; mq2_ash_on_the_wind_done is set");
        }

        // ===================================================================================================== the side quests

        [Test]
        public static void SideQuests_Cairnhollow_EveryOneScriptedToTheEnd()
        {
            var p = FittingParty(30, 3131);
            var s = p.S;
            p.Enter(Peaks, "default");

            p.TalkTo("sr_pema", "I'll find your goats");
            p.TalkTo("sr_guide_odran", "Pleased to meet you", "Is the road west safe", "I'll deal with Gorrum");
            p.TalkTo("sr_guide_odran", "You keep looking north-east", "I'll find him");
            p.TalkTo("sr_brannoc", "Why does the village need coats", "Six pelts");
            p.TalkTo("sr_hilde", "Why does the line outside have no bells", "I'll bring your bells back");
            foreach (var q in new[] { "sr_woolly_business", "sr_ogre_toll", "sr_lamplighters_climb", "sr_warm_coats", "sr_whiteout_bells" })
                Assert(s.Quests.IsActive(q), q + " started");
            Assert(s.Flags.IsSet("sr_pell_sought"), "Pell's lantern shows in the snow");

            // Warm Coats: the wolves, Old Rimefang (pelts are luck: topped up)
            foreach (var e in new[] { "enc_sr_wolves_road", "enc_sr_wolf_den", "enc_sr_rimefang" }) { p.Refresh(); p.WalkInto(e); }
            p.TopUp("sr_frost_wolf_pelt", 6);
            Assert(p.Stage("sr_warm_coats") == "report", "six pelts");
            p.TalkTo("sr_brannoc", "Six pelts, as promised");
            Assert(s.Quests.IsCompleted("sr_warm_coats"), "Warm Coats complete");

            // The Lamplighter's Climb: find Pell, walk him out of the wolves' hollow
            p.Refresh();
            p.TalkTo("sr_pell", "Can you walk", "Lean on me");
            Assert(s.Map.IsEncounterDone(s.Map.FindEncounter("enc_sr_pell_ambush")) && p.Stage("sr_lamplighters_climb") == "report", "the wolves driven off: " + p.Stage("sr_lamplighters_climb"));
            Assert(s.VisibleNpcs().Count(n => n.npc == "sr_pell") == 1, "Pell has gone down to the Kettle Hut");
            p.TalkTo("sr_guide_odran", "Brother Pell is safe");
            Assert(s.Quests.IsCompleted("sr_lamplighters_climb"), "The Lamplighter's Climb complete");
            p.TalkTo("sr_pell", "What was the lantern for");

            // The Ogre Toll, the hard way
            p.Refresh();
            p.WalkInto("enc_sr_ogre_toll", "Then we do this the hard way");
            var g = p.Fights["enc_sr_ogre_toll"];
            Console.WriteLine($"    skyreach: Gorrum's toll: {g.rounds} rounds, lowest party health {g.minHp:P0}");
            Assert(s.Flags.IsSet("sr_ogre_road_clear") && p.Stage("sr_ogre_toll") == "report", "the road is clear");
            p.TalkTo("sr_guide_odran", "Gorrum won't be charging anyone");
            Assert(s.Quests.IsCompleted("sr_ogre_toll"), "The Ogre Toll complete");

            // Woolly Business: Bramble waits, Clover's yeti is fed, Turnip is liberated from the cook-pot
            p.TalkTo("sr_goat_bramble", "(Wait for her to come down");
            Assert(s.Flags.IsSet("sr_goat_bramble"), "Bramble goes home");
            s.GiveItem("sr_food_yak_dumplings", 1);
            p.Refresh();
            p.WalkInto("enc_sr_whitebrow", "(Offer it a parcel");
            Assert(s.Flags.IsSet("sr_whitebrow_calmed") && !p.Fights.ContainsKey("enc_sr_whitebrow"), "Old Whitebrow takes the dumplings and wanders off");
            p.TalkTo("sr_goat_clover", "(Wake her gently");
            p.Refresh();
            p.TalkTo("sr_goat_turnip", "(Charge the camp|(Take Turnip home");   // (the camp may already have met the party on its way in)
            Assert(s.Map.IsEncounterDone(s.Map.FindEncounter("enc_sr_ogre_camp")), "the ogre camp broken");
            if (!s.Flags.IsSet("sr_goat_turnip")) p.TalkTo("sr_goat_turnip", "(Take Turnip home");
            Assert(s.Flags.IsSet("sr_goat_clover") && s.Flags.IsSet("sr_goat_turnip") && p.Stage("sr_woolly_business") == "return", "three goats home");
            Assert(s.VisibleNpcs().Count(n => n.npc.StartsWith("sr_goat_") && n.pos.x > 95f) == 3, "the goats stand in Pema's pen");
            p.TalkTo("sr_pema", "Every last goat");
            Assert(s.Quests.IsCompleted("sr_woolly_business"), "Woolly Business complete");

            // Bells in the Whiteout: the crag harpies and the tower's matriarch
            foreach (var e in new[] { "enc_sr_harpies_a", "enc_sr_harpies_b", "enc_sr_tower_harpies" }) { p.Refresh(); p.WalkInto(e); }
            var sh = p.Fights["enc_sr_tower_harpies"];
            Console.WriteLine($"    skyreach: Shrikeclaw: {sh.rounds} rounds, lowest party health {sh.minHp:P0}");
            p.TopUp("sr_hut_bell", 5);
            p.TalkTo("sr_hilde", "Five bells");
            Assert(s.Quests.IsCompleted("sr_whiteout_bells"), "Bells in the Whiteout complete");
        }

        [Test]
        public static void SideQuests_TheHighSlopes_EveryOneScriptedToTheEnd()
        {
            var p = FittingParty(30, 4646, "ysolde");
            var s = p.S;
            p.Enter(Peaks, "cairnhollow");

            // The Twenty-Nine: Lumi, the tower, Shrikeclaw, the journal (Ysolde knows where), back to Lumi
            p.TalkTo("sr_lumi", "Who are the flags for", "Is there anything I can do", "I'll go to the tower");
            Assert(s.Quests.IsActive("sr_twenty_nine"), "The Twenty-Nine started");
            p.Go(new Vec2(40.2f, 44.6f));
            Assert(p.Stage("sr_twenty_nine") == "nest" || p.Stage("sr_twenty_nine") == "journal", "at the tower: " + p.Stage("sr_twenty_nine"));
            p.Refresh();
            p.WalkInto("enc_sr_tower_harpies");
            Assert(p.Stage("sr_twenty_nine") == "journal", "Shrikeclaw driven off");
            p.Use("sr_heron_tower", "Ysolde, where would he have kept it");
            Assert(s.CountItem("sr_aubric_journal") == 1 && p.Stage("sr_twenty_nine") == "lumi", "Aubric's journal");
            p.TalkTo("sr_lumi", "(Read the last page aloud", "Ysolde is right here");
            Assert(s.Quests.IsCompleted("sr_twenty_nine") && s.Flags.IsSet("sr_knows_temple") && s.Flags.IsSet("sr_journal_ysolde"),
                "The Twenty-Nine complete: Ysolde has the journal, and the temple is known");

            // Ice and Ember: the brood, the pen, Icicle's collar, Glimmerwing
            p.TalkTo("sr_glimmerwing", "Hello", "What troubles you", "I'll thin the whelps");
            Assert(s.Quests.IsActive("sr_ice_and_ember") && s.Flags.IsSet("sr_brood_hunt"), "Ice and Ember started; the brood comes down");
            foreach (var e in new[] { "enc_sr_brood_shelf", "enc_sr_brood_huts" }) { p.Refresh(); p.WalkInto(e); }
            Assert(p.Stage("sr_ice_and_ember") == "pen", "six whelps: " + p.Stage("sr_ice_and_ember"));
            p.Refresh();
            p.WalkInto("enc_sr_drake_pen");
            Assert(p.Stage("sr_ice_and_ember") == "free" && s.CountItem("sr_whelpmaster_key") == 1, "the pen broken, the key taken");
            Assert(s.QuestMarkerOf("sr_icicle").Kind == QuestMarker.ReadyToTurnIn, "Icicle's marker: " + s.QuestMarkerOf("sr_icicle"));
            p.TalkTo("sr_icicle", "(Unlock the collar");
            Assert(s.Flags.IsSet("sr_drake_freed") && p.Stage("sr_ice_and_ember") == "report", "Icicle is free");
            Assert(s.VisibleNpcs().Any(n => n.npc == "sr_icicle" && n.pos.y > 50f), "and home on Glimmerwing's shelf");
            p.TalkTo("sr_glimmerwing", "She's safe now");
            Assert(s.Quests.IsCompleted("sr_ice_and_ember"), "Ice and Ember complete");

            // The Singing Bones: four rime cores, three stones (struck hard: the snow stands up), Ottoline
            s.Flags.Set("sr_bones_read");
            p.TalkTo("sr_ottoline", "What are you measuring", "I'll get your cores");
            foreach (var e in new[] { "enc_sr_bone_ice", "enc_sr_rime_drift", "enc_sr_falls_ice" }) { p.Refresh(); p.WalkInto(e); }
            Assert(s.CountItem("sr_rime_core") >= 4 && p.Stage("sr_singing_bones") == "stones", "four rime cores: " + s.CountItem("sr_rime_core"));
            foreach (var side in new[] { "w", "n", "e" })
            {
                p.Refresh();
                p.Use("sr_stone_" + side, "(Strike the stone hard");
                Assert(s.Flags.IsSet("sr_stone_" + side) && s.Map.IsEncounterDone(s.Map.FindEncounter("enc_sr_stone_guard_" + side)), "the " + side + " stone rings true, and its guard is beaten");
            }
            Assert(p.Stage("sr_singing_bones") == "report", "the stones are tuned");
            p.TalkTo("sr_ottoline", "I heard it");
            Assert(s.Quests.IsCompleted("sr_singing_bones"), "The Singing Bones complete");

            // Strike the Colours: four banners, four zealots
            s.Flags.Set("sr_ivo_met");
            p.TalkTo("sr_scout_ivo", "Anything else I can do", "Four banners");
            for (int i = 1; i <= 4; i++) { p.Refresh(); p.Use("sr_banner_" + i, "(Tear it down"); }
            foreach (var e in new[] { "enc_sr_vanguard_pickets", "enc_sr_vanguard_west", "enc_sr_vanguard_altar" }) { p.Refresh(); p.WalkInto(e); }
            Assert(p.Stage("sr_strike_the_colours") == "report", "banners struck, zealots broken: " + p.Stage("sr_strike_the_colours"));
            p.TalkTo("sr_scout_ivo", "The mountain belongs to itself");
            Assert(s.Quests.IsCompleted("sr_strike_the_colours"), "Strike the Colours complete");
        }

        [Test]
        public static void Ivo_OffersHisBanners_EvenWhenTheScoutTalkSkipsOttolinesBranch()
        {
            // the 'scout' stage is a Talk objective: any conversation with Ivo ends it, so meeting him at all must open his offer
            var p = FittingParty(30, 2425);
            var s = p.S;
            p.Enter("brightwater", "default");
            s.Flags.Set("mq2_drowned_lanterns_done");
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
            p.Converse("Where does the ash come from");
            p.Enter(Peaks, "from_brightwater");
            p.TalkTo("sr_guide_odran", "Pleased to meet you", "Archivist Penhallow sent me", "Who are the Dragonsworn", "I'll head for the Bone Field");
            p.TalkTo("sr_ottoline", "Penhallow sent me to read the ash", "What does that mean", "I'll find Ivo");
            Assert(p.Stage(MQ) == "scout", "find the scout: " + p.Stage(MQ));

            p.TalkTo("sr_scout_ivo", "What news from the camp");
            Assert(p.Stage(MQ) == "marshal", "the talk with Ivo ends the scout stage: " + p.Stage(MQ));
            Assert(s.Flags.IsSet("sr_ivo_met"), "meeting Ivo at all counts as having met him");
            var m = s.QuestMarkerOf("sr_scout_ivo");
            Assert(m.Kind == QuestMarker.Available && m.QuestId == "sr_strike_the_colours", "Ivo's ! for Strike the Colours: " + m);
            p.TalkTo("sr_scout_ivo", "Anything else I can do", "Four banners");
            Assert(s.Quests.IsActive("sr_strike_the_colours"), "Strike the Colours is offered and taken");
        }

        [Test]
        public static void Gorrum_TalkedDown_ChangesTheLedge()
        {
            var p = new Player(ClassId.Shaman, 27, 5858, "seren", "rook", "kael");
            var s = p.S;
            p.Enter(Peaks, "default");
            p.TalkTo("sr_guide_odran", "Pleased to meet you", "Is the road west safe", "I'll deal with Gorrum");
            p.TalkTo("sr_pema", "I'll find your goats");
            Assert(s.Map.IsEncounterAvailable(s.Map.FindEncounter("enc_sr_ogre_camp")), "the ogre camp sits on the ledge");
            p.WalkInto("enc_sr_ogre_toll", "The mountain is restless", "Let the road go free");
            Assert(s.Flags.IsSet("sr_ogres_pacified") && s.Flags.IsSet("sr_ogre_road_clear") && !p.Fights.ContainsKey("enc_sr_ogre_toll"), "Gorrum makes peace without a fight");
            Assert(!s.Map.IsEncounterAvailable(s.Map.FindEncounter("enc_sr_ogre_camp")) && !s.Map.IsEncounterAvailable(s.Map.FindEncounter("enc_sr_ogre_patrol")),
                "the consequence: the ogre camp and patrol stand down");
            Assert(s.VisibleNpcs().Any(n => n.npc == "sr_gorrum"), "Gorrum sits by his cook-pot, a cheese man now");
            p.TalkTo("sr_goat_turnip", "(Take Turnip home");
            Assert(s.Flags.IsSet("sr_goat_turnip"), "and gives Turnip back");
            p.TalkTo("sr_guide_odran", "Gorrum's made peace");
            Assert(s.Quests.IsCompleted("sr_ogre_toll"), "The Ogre Toll complete, peacefully");
        }

        // ===================================================================================================== the Frozen Sanctum

        [Test]
        public static void LongWatch_TheSanctumClearedAndTheRimeheartBeaten()
        {
            var p = FittingParty(31, 6363, "ysolde");
            var s = p.S;
            p.Enter(Peaks, "cairnhollow");
            RaidTest.Pacify(s);
            s.Quests.Start("sr_twenty_nine");
            s.Quests.Complete("sr_twenty_nine");
            s.Flags.Set("sr_knows_temple");
            p.TalkTo("sr_lumi", "The knights who went into the temple", "I'll go into the Frozen Sanctum");
            Assert(s.Quests.IsActive("dg6_the_long_watch"), "The Long Watch starts");
            p.TalkTo("sr_guide_odran", "Pleased to meet you", "Aubric's journal says");
            Assert(s.Flags.IsSet("found_frozen_sanctum"), "Odran shows the door");
            var door = s.MapDef.transitions.First(t => t.id == "to_dgn_frozen_sanctum");
            p.Go(door.pos, 0.2f);
            Assert(s.MapId == Sanctum && p.Stage("dg6_the_long_watch") == "rimeheart", "into the sanctum");
            foreach (var e in new[] { "enc_dg6_statue_ambush", "enc_dg6_hymn_hall", "enc_dg6_hoarfang", "enc_dg6_colossus" })
            {
                p.Refresh();
                p.WalkInto(e);
            }
            p.Refresh();
            p.WalkInto("enc_dg6_vael", "Then we will end the Heart");
            Assert(p.Fights.ContainsKey("enc_dg6_vael"), "Hierophant Vael fought");
            Console.WriteLine("    skyreach: the sanctum: " + string.Join(", ", p.Fights.Where(kv => kv.Key.StartsWith("enc_dg6_")).Select(kv => $"{kv.Key} {kv.Value.rounds}r {kv.Value.minHp:P0}")));
            p.Refresh();
            p.WalkInto("enc_dg6_rimeheart");
            var rh = p.Fights["enc_dg6_rimeheart"];
            Console.WriteLine($"    skyreach: the Rimeheart vs a party of 5 at 31: {rh.rounds} rounds, lowest party health {rh.minHp:P0}, {rh.deaths} down");
            Assert(rh.rounds >= 4 && (rh.minHp < 0.85f || rh.deaths > 0), $"the Rimeheart is a real fight ({rh.rounds} rounds, lowest {rh.minHp:P0}, {rh.deaths} down)");
            Assert(p.Stage("dg6_the_long_watch") == "aubric", "Aubric rises: " + p.Stage("dg6_the_long_watch"));
            Assert(s.QuestMarkerOf("dg6_aubric").Kind == QuestMarker.ReadyToTurnIn, "Aubric's marker: " + s.QuestMarkerOf("dg6_aubric"));
            p.TalkTo("dg6_aubric", "Ysolde is here");
            Assert(s.Flags.IsSet("dg6_knights_freed") && s.Flags.IsSet("sr_ysolde_absolved") && p.Stage("dg6_the_long_watch") == "lumi", "the knights are free; Ysolde is answered");
            Assert(!s.VisibleNpcs().Any(n => n.npc == "dg6_aubric"), "Aubric fades");
            var heart = s.MapDef.chests.First(c => c.id == "chest_dg6_heart");
            Assert(s.Map.IsChestAvailable(heart), "the heart's hoard");
            p.Go(heart.pos, 1.5f);
            Assert(s.OpenChest(heart.id).Ok, "open the hoard");
            p.TakeLoot();
            p.Go(s.MapDef.transitions.First(t => t.id == "to_skyreach").pos, 0.2f);
            Assert(s.MapId == Peaks, "back out under the falls");
            RaidTest.Pacify(s);
            p.TalkTo("sr_lumi", "They're free, Lumi");
            Assert(s.Quests.IsCompleted("dg6_the_long_watch"), "The Long Watch complete");
        }

        [Test]
        public static void Vael_CanBeLaidToRest()
        {
            var p = new Player(ClassId.Priest, 31, 7171, "kael", "rook");
            p.Enter(Sanctum, "default");
            RaidTest.Pacify(p.S);
            p.S.Map.ResetEncounter("enc_dg6_vael");
            p.WalkInto("enc_dg6_vael", "(Lay a hand on his shoulder");
            Assert(p.S.Flags.IsSet("dg6_vael_rested") && !p.Fights.ContainsKey("enc_dg6_vael"), "the priest prays with Vael; no fight");
        }

        // ===================================================================================================== bosses and markers

        [Test]
        public static void Bosses_TooMuchForTwo_AFittingPartyWins()
        {
            // a fitting party of five at the band's top beats Kaedric and the Rimeheart (the main story and The Long Watch above);
            // two characters do not
            var p = new Player(ClassId.Mage, 29, 8181, "lys");
            p.Enter(Peaks, "from_raid_ashwyrm_roost");
            RaidTest.Pacify(p.S);
            p.S.Map.ResetEncounter("enc_sr_marshal");
            Assert(p.S.StartEncounter("enc_sr_marshal") != null, "fight Kaedric: " + p.S.LastError);
            var outcome = p.S.AutoResolve(80);
            Assert(outcome != BattleOutcome.Victory, "two mages do not beat Kaedric: " + outcome);

            var q = new Player(ClassId.Warrior, 31, 8282, "seren");
            q.Enter(Sanctum, "default");
            RaidTest.Pacify(q.S);
            q.S.Map.ResetEncounter("enc_dg6_rimeheart");
            Assert(q.S.StartEncounter("enc_dg6_rimeheart") != null, "fight the Rimeheart: " + q.S.LastError);
            outcome = q.S.AutoResolve(80);
            Assert(outcome != BattleOutcome.Victory, "a warrior and a priest do not beat the Rimeheart: " + outcome);
        }

        [Test]
        public static void Markers_FollowTheLevelAndQuestGates()
        {
            var s = SessionTest.NewGame(ClassId.Priest, 24, 91);
            s.EnterMap(Peaks, "default");
            Assert(s.QuestMarkerOf("sr_pema").Kind == QuestMarker.Available, "Pema offers at 24");
            Assert(s.QuestMarkerOf("sr_brannoc").Kind == QuestMarker.Available, "Brannoc offers at 24");
            var hilde = s.QuestMarkerOf("sr_hilde");
            Assert(hilde.Kind == QuestMarker.AvailableLater && hilde.Level == 25, "Hilde: grey ! for level 25: " + hilde);
            var glim = s.QuestMarkerOf("sr_glimmerwing");
            Assert(glim.Kind == QuestMarker.AvailableLater && glim.Level == 26, "Glimmerwing: grey ! for 26: " + glim);
            Assert(s.QuestMarkerOf("sr_scout_ivo").Kind != QuestMarker.Available, "Ivo offers nothing before he has met you");
            Assert(s.QuestMarkerOf("sr_ottoline").Kind != QuestMarker.Available, "Ottoline's tuning waits for the ash to be read");
            s.Flags.Set("sr_bones_read");
            s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
            s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
            Assert(s.Main.Level == 26 && s.QuestMarkerOf("sr_ottoline").Kind == QuestMarker.Available, "then Ottoline offers at 26");
            // every Skyreach quest has an offer gated by its level, and a CompleteQuest or final Talk at its turn-in
            foreach (var q in Db.Quests.Values.Where(q => q.zone == Peaks || q.zone == Sanctum))
            {
                Assert(q.minLevel >= 24 && q.minLevel <= 30 && Db.Npcs.ContainsKey(q.giver), $"{q.id}: level gate and giver");
                Assert(s.QuestMarkerIndex.Starters(q.id).Contains(q.giver), $"{q.id}: {q.giver} offers it");
                Assert(s.QuestMarkerIndex.Enders(q.id).Count > 0, $"{q.id}: has a turn-in");
            }
            Assert(Db.Quests.Values.Count(q => q.zone == Peaks) >= 10, "ten Skyreach quests");
        }
    }
}
