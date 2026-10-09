// Ashwyrm's Roost (Docs/Expansion.md §8, builder "r2", prefix r2; raid of 10, band 31-33): the raid map is entered
// through the raid gate (EnterMap asks for a raid party, EnterRaid forms it) and everything on it can be reached; the
// Last Stand's cache is revealed by its Perception check or Sir Hamon's hint; the six raid quests are scripted to the
// epilogue at the band's top level through the real GameSession (talk, walk, props, fights won by the raid AI); the four
// bosses are won by a fitting raid of 10 at level 32 and lost by 5 of the same level; the quest markers follow the gates.
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
    public static class TestsContentR2
    {
        const string Roost = "raid_ashwyrm_roost", Gate = "mq2_ash_on_the_wind_done";

        /// <summary>A fitting raid of 10: two tanks beside the main, three healers, four damage dealers.</summary>
        static readonly string[] Ten = { "bruna", "ysolde", "liora", "seren", "nanami", "rook", "pip", "lys", "morwen" };
        /// <summary>A party of 5: one tank, one healer, two damage dealers and the main.</summary>
        static readonly string[] Five = { "bruna", "liora", "rook", "lys" };

        static readonly string[] Bosses = { "enc_r2_frostclaw", "enc_r2_cinder_drakes", "enc_r2_varkas", "enc_r2_vyrmathra" };

        /// <summary>A scripted player in the raid: walks, talks, pokes props and lets the raid AI fight.</summary>
        sealed class Player
        {
            public readonly GameSession S;
            public readonly List<string> Log = new List<string>();
            public readonly Dictionary<string, string[]> EncounterScripts = new Dictionary<string, string[]>(StringComparer.Ordinal);
            public readonly Dictionary<string, (float minHp, int rounds, int deaths)> Fights = new Dictionary<string, (float, int, int)>(StringComparer.Ordinal);

            /// <summary>Every companion recruited, standing at the roost's gate in Skyreach; then the raid of 1 + companions enters.</summary>
            public Player(ClassId main, int level, ulong seed, string[] companions, bool gate = true)
            {
                S = SessionTest.NewGame(main, level, seed);
                S.Settings.CompanionAutoPlay = true;
                foreach (var id in RaidTest.Companions) S.Recruit(id);
                var home = Db.Maps[Roost];
                S.EnterMap(home.raidReturnMap, home.raidReturnSpawn);
                Assert(S.MapId == home.raidReturnMap, "at the roost's gate in " + home.raidReturnMap);
                if (gate) S.Flags.Set(Gate);
                var ids = new List<string> { GameSession.MainId };
                ids.AddRange(companions);
                Assert(S.EnterRaid(Roost, "from_skyreach", ids, true) == null, "enter the raid: " + S.LastError);
                Assert(S.InRaid && S.MapId == Roost && S.Party.Count == ids.Count, "the raid party is formed: " + S.Party.Count);
                S.SetAutoPlay(S.Main, true);
                S.TakeEvents();
            }

            void Note(string s) => Log.Add(s);
            string Trace => string.Join("\n      ", Log.Skip(Math.Max(0, Log.Count - 25)));

            public void Converse(params string[] script)
            {
                var todo = new List<string>(script);
                for (int guard = 0; guard < 200 && S.Dialogue.IsActive; guard++)
                {
                    var v = S.Dialogue.Current;
                    if (v == null) break;
                    if (v.CanContinue) { S.ContinueDialogue(); continue; }
                    Note($"  [{v.DialogueId}/{v.NodeId}] " + string.Join(" / ", v.Choices.Select(c => c.Text)));
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
                        foreach (var leave in new[] { "Goodbye", "(Leave", "(Step back", "(Back away", "Not right now", "Not yet", "(Fight.)" })
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
                var p = S.MapDef.props.FirstOrDefault(x => x.interact == interactId && S.Map.IsPropVisible(x));
                Assert(p != null, $"prop {interactId} is there");
                Go(p.pos, GameSession.InteractionRange - 0.3f);
                var r = S.InteractProp(interactId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"use {interactId}: {r.Message}");
                Note("use " + interactId);
                Converse(script);
            }

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
                while (!b.IsOver && steps++ < 120000 && b.Round <= 100)
                {
                    var step = S.RunAIStep();
                    Assert(step != null || b.IsOver, $"AI step in {id}");
                    min = Math.Min(min, party.Sum(u => Math.Max(0f, u.Health)) / max);
                }
                int deaths = party.Count(u => !u.IsAlive);
                var sum = S.FinishBattle();
                Assert(sum != null && sum.Outcome == CombatEndKind.Victory, $"{id}: the raid wins (outcome {sum?.Outcome}, round {b.Round}, level {S.Main.Level})\n      {Trace}");
                Fights[id] = (min, sum.Rounds, deaths);
                Note($"   {id}: won in {sum.Rounds} rounds, lowest raid health {min:P0}, {deaths} down, +{sum.Xp} xp");
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

            /// <summary>Full health and mana between pulls (the raid eats, drinks and binds its wounds).</summary>
            public void Refresh()
            {
                foreach (var u in S.PartyUnits()) u.RestoreFull();
            }

            public string Stage(string q) => S.Quests.GetStage(q);
        }

        // ===================================================================================================== the map

        [Test]
        public static void Roost_EnterThroughTheGate_AndReachEverything()
        {
            var m = Db.Maps[Roost];
            Assert(m.raidSize == 10 && m.raidReturnMap == "skyreach" && m.raidReturnSpawn == "from_raid_ashwyrm_roost", "a raid of 10 returning to Skyreach");
            Assert(m.biome == "roost" && m.environment == "outdoor" && m.fill >= 0.3f && m.fill <= 0.5f && m.levelMin == 31 && m.levelMax == 33 && !m.restArea,
                "roost biome, outdoor fill 0.3-0.5, band 31-33, no rest");
            Assert(m.ambient.ash, "falling ash");
            Assert(m.paths.Count >= 6 && m.water.Count >= 1, "paths and the meltwater tarn");
            Assert(m.props.Count(p => p.light != null) >= 20, "lights: " + m.props.Count(p => p.light != null));
            foreach (var art in new[] { "prop_roost_nest", "prop_dragon_skull", "prop_treasure_pile", "prop_ash_banner", "prop_brazier", "prop_altar", "prop_ruined_tower", "prop_dragon_bones" })
                Assert(m.props.Any(p => p.art == art), "the roost has " + art);
            var exit = m.transitions.First(t => t.id == "to_skyreach");
            Assert(Math.Abs(exit.pos.x - 0.7f) < 0.01f && Math.Abs(exit.pos.y - 32f) < 0.01f && exit.targetMap == "skyreach" && exit.targetSpawn == "from_raid_ashwyrm_roost",
                "the §1 exit at (0.7, 32)");

            var bad = TestsMapsReachable.Unreachable(m);
            Assert(bad.Count == 0, "everything reachable from default: " + string.Join("; ", bad));

            // the gate: Skyreach's portal asks for a raid party; EnterRaid forms it; every spawn stands on walkable ground
            var s = SessionTest.NewGame(ClassId.Warrior, 32, 3);
            foreach (var id in RaidTest.Companions) s.Recruit(id);
            s.EnterMap(m.raidReturnMap, m.raidReturnSpawn);
            Assert(s.MapId == "skyreach", "at the gate");
            s.TakeEvents();
            s.EnterMap(Roost, "from_skyreach");
            var req = SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.RaidPartyRequested, Roost);
            Assert(s.MapId == "skyreach" && req != null && req.Amount == 10, "EnterMap asks for a raid party of 10");
            foreach (var sp in m.spawns)
            {
                Assert(s.EnterRaid(Roost, sp.id, RaidTest.Ids(10), true) == null, $"enter at {sp.id}: {s.LastError}");
                Assert(s.MapId == Roost && s.Party.Count == 10 && s.Nav.IsWalkable(s.Leader.Position, NavAgent.Default.IgnoringAllUnits()),
                    $"{sp.id}: the raid stands on walkable ground");
                Assert(s.UseTransition("to_skyreach").Ok && s.MapId == "skyreach" && !s.InRaid, "and walks out to Skyreach");
            }
        }

        [Test]
        public static void Creatures_LevelsLootAndMechanics()
        {
            var mine = Db.Creatures.Values.Where(c => c.id.StartsWith("cr_r2_")).ToList();
            Assert(mine.Count >= 10, "the roost's creatures: " + mine.Count);
            foreach (var c in mine)
            {
                Assert(c.scaleToParty && c.levelFloor == 31 && c.levelCap == 34 && c.levelOffset >= 1 && c.levelOffset <= 2, $"{c.id}: floor 31, cap 34");
                Assert(!string.IsNullOrEmpty(c.material) && !string.IsNullOrEmpty(c.voice), $"{c.id}: material and voice");
            }
            var m = Db.Maps[Roost];
            foreach (var e in m.encounters)
                foreach (var x in e.enemies)
                {
                    var c = Db.Creatures[x.creature];
                    if (Bosses.Contains(e.id) && c.rank == CreatureRank.Boss) continue;
                    Assert(c.rank == CreatureRank.Elite && c.lootTable == "lt_r2_trash", $"{e.id}: {c.id} is Elite raid trash with lt_r2_trash");
                }
            Assert(m.encounters.Count(e => !Bosses.Contains(e.id)) >= 5, "five or more trash packs");
            Assert(Db.Creatures["cr_r2_frostclaw"].lootTable == "lt_r2_frostclaw" && Db.Creatures["cr_r2_varkas"].lootTable == "lt_r2_varkas" &&
                   Db.Creatures["cr_r2_vyrmathra"].lootTable == "lt_r2_vyrmathra", "boss tables");
            var drakes = new[] { Db.Creatures["cr_r2_emberjaw"], Db.Creatures["cr_r2_ashtongue"] };
            Assert(drakes.Count(d => d.lootTable == "lt_r2_cinder_drakes") == 1 && drakes.All(d => d.lootTable == "lt_r2_cinder_drakes" || string.IsNullOrEmpty(d.lootTable)),
                "only one of the Cinder Drakes carries lt_r2_cinder_drakes");
            var pair = m.encounters.First(e => e.id == "enc_r2_cinder_drakes");
            Assert(pair.enemies.Select(x => x.creature).OrderBy(x => x).SequenceEqual(new[] { "cr_r2_ashtongue", "cr_r2_emberjaw" }), "both drakes in one encounter");
            Assert(Math.Abs(Db.Creatures["cr_r2_vyrmathra"].size - 7.5f) < 0.01f && Math.Abs(Db.Creatures["cr_r2_frostclaw"].size - 5f) < 0.01f &&
                   drakes.All(d => Math.Abs(d.size - 4.2f) < 0.01f) && Math.Abs(Db.Creatures["cr_r2_varkas"].size - 5.2f) < 0.01f, "boss sizes per ArtKeys");

            // every boss: adds, a telegraphed big cast, an enrage below a health threshold, and raid-wide damage for the healers
            foreach (var id in new[] { "cr_r2_frostclaw", "cr_r2_varkas", "cr_r2_vyrmathra", "cr_r2_ashtongue" })
            {
                var c = Db.Creatures[id];
                var abs = c.abilities.Select(a => (def: Db.Ability(a.ability), a.condition)).ToList();
                Assert(abs.Any(a => a.def.tags.Contains("Telegraph") && a.def.castTime >= 2.5f), $"{id}: a telegraphed big cast");
                Assert(abs.Any(a => a.def.area.radius >= 30f && a.def.area.shape != AreaShape.None), $"{id}: raid-wide damage (the healer check)");
            }
            foreach (var id in new[] { "cr_r2_frostclaw", "cr_r2_varkas", "cr_r2_vyrmathra", "cr_r2_emberjaw", "cr_r2_ashtongue" })
                Assert(Db.Creatures[id].abilities.Any(a => a.condition.Contains("selfHpBelow:") && Db.Ability(a.ability).effects.Any(e => e.type == EffectType.ApplyAura && e.target == EffectTarget.Self)),
                    id + ": an enrage below a health threshold");
            foreach (var id in new[] { "cr_r2_frostclaw", "cr_r2_varkas", "cr_r2_vyrmathra" })
                Assert(Db.Creatures[id].abilities.Count(a => Db.Ability(a.ability).effects.Any(e => e.type == EffectType.Summon)) >= (id == "cr_r2_varkas" ? 1 : 2),
                    id + ": summons adds in phases");
            Assert(Db.Creatures["cr_r2_ashtongue"].abilities.Any(a => a.condition.StartsWith("allyHpBelow") && Db.Ability(a.ability).effects.Any(e => e.type == EffectType.Heal)),
                "Ashtongue mends her brother (interrupt her)");
        }

        // ===================================================================================================== the secret

        [Test]
        public static void HeronCache_RevealedByPerception_OrSirHamon()
        {
            var reg = Db.Maps[Roost].regions.First(r => r.id == "reg_r2_last_stand");
            Assert(reg.check != null && reg.check.skill == SkillCheck.Perception && reg.check.dc >= 12 && reg.check.dc <= 16 && reg.checkFlag == "r2_cache_found",
                "a Perception check at the Last Stand reveals the cache");
            var chest = Db.Maps[Roost].chests.First(c => c.id == "chest_r2_heron_cache");
            Assert(chest.requireFlag == "r2_cache_found", "the cache is hidden until found");
            int found = 0, missed = 0;
            for (ulong seed = 1; seed <= 24 && (found == 0 || missed == 0); seed++)
            {
                var p = new Player(ClassId.Rogue, 32, 500 + seed, new[] { "pip" });
                var s = p.S;
                foreach (var e in s.MapDef.encounters) if (!Bosses.Contains(e.id)) s.Map.MarkEncounterDone(e.id);
                Assert(!s.Map.IsChestAvailable(chest), "hidden at first");
                s.TakeEvents();
                p.Go(reg.pos, 2f);
                var ev = s.TakeEvents();
                var roll = ev.FirstOrDefault(e => e.Kind == SessionEventKind.SkillCheck && e.Id == reg.id);
                Assert(roll != null, "reaching the Last Stand rolls the check");
                if (roll.Check.Success)
                {
                    found++;
                    Assert(s.Flags.IsSet("r2_cache_found") && s.Map.IsChestAvailable(chest), "found: the cache is there");
                    p.Go(chest.pos, 1.5f);
                    Assert(s.OpenChest(chest.id).Ok, "it opens");
                    p.TakeLoot();
                }
                else
                {
                    missed++;
                    Assert(!s.Map.IsChestAvailable(chest), "missed: still hidden");
                    p.TalkTo("r2_sir_hamon", "Did the Heronguard leave anything");
                    Assert(s.Flags.IsSet("r2_cache_found") && s.Map.IsChestAvailable(chest), "Sir Hamon's hint reveals it");
                }
            }
            Assert(found > 0 && missed > 0, $"the check can pass and fail ({found} found, {missed} missed)");
        }

        // ===================================================================================================== the quests

        [Test]
        public static void RaidQuests_FromTheCampToTheEpilogue_AtTheBandsTop()
        {
            var p = new Player(ClassId.Warrior, 33, 3131, Ten);
            var s = p.S;
            Assert(s.QuestMarkerOf("r2_quartermaster").Kind == QuestMarker.Available, "Marta offers: " + s.QuestMarkerOf("r2_quartermaster"));

            p.TalkTo("r2_quartermaster", "Where do we start", "I'll put her to rest");
            p.TalkTo("r2_sir_hamon", "anything I can do for them", "I'll find his standard");
            p.TalkTo("r2_kesta", "lost something", "I'll get your stores back");
            foreach (var q in new[] { "r2_matriarch", "r2_twenty_nine", "r2_small_fires" }) Assert(s.Quests.IsActive(q), q + " started");

            // the Heron Road, and the Last Stand: Aubric's standard and his letter (Ysolde is with us)
            p.WalkInto("enc_r2_road_patrol");
            p.Go(Db.Maps[Roost].regions.First(r => r.id == "reg_r2_last_stand").pos, 3f);
            Assert(p.Stage("r2_twenty_nine") == "standard", "at the Last Stand: " + p.Stage("r2_twenty_nine"));
            p.Use("r2_aubric_standard", "(Search every heap");
            p.Use("r2_aubric_letter", "(Read the page");
            Assert(s.Flags.IsSet("r2_aubric_letter_read") && s.Quests.GetStage("r2_twenty_nine") == "return", "the standard found, the letter read");

            // A Cold Welcome: Frostclaw, then the cubs
            p.WalkInto("enc_r2_vent_elementals");
            p.WalkInto("enc_r2_frostclaw");
            Assert(p.Stage("r2_matriarch") == "cubs", "Frostclaw at rest");
            p.Use("r2_cub_den", "(Hack through the chains");
            Assert(s.Flags.IsSet("r2_cubs_freed") && p.Stage("r2_matriarch") == "return", "the cubs go free");

            // back at camp: Hamon (plant the standard at the summit; Ysolde and Hamon), Marta (Twin Fires)
            p.TalkTo("r2_sir_hamon", "Plant it at the summit");
            Assert(s.Quests.IsCompleted("r2_twenty_nine") && s.Flags.IsSet("r2_standard_to_summit") && s.CountItem("r2_q_heronguard_signet") == 1, "The Twenty-Nine");
            p.TalkTo("r2_sir_hamon", "you know Ysolde", "show him the letter");
            Assert(s.Flags.IsSet("r2_hamon_ysolde"), "Hamon and Ysolde");
            p.TalkTo("r2_quartermaster", "Frostclaw is at rest", "What's next on the list", "Two drakes and a gland");
            Assert(s.Quests.IsCompleted("r2_matriarch") && s.Quests.IsActive("r2_twin_fires"), "A Cold Welcome done; Twin Fires");

            // the terraces: the whelps and the stores, the Drake Pit and the gland
            p.WalkInto("enc_r2_whelp_clutch");
            p.Use("r2_stash_1", "(Gather up");
            p.Use("r2_stash_2", "(Gather up");
            p.WalkInto("enc_r2_cinder_drakes");
            Assert(p.Stage("r2_twin_fires") == "gland", "the Cinder Drakes are down");
            p.Use("r2_drake_remains", "(Cut it out the hard way");
            p.WalkInto("enc_r2_terrace");
            p.Use("r2_stash_3", "(Gather up");
            Assert(p.Stage("r2_small_fires") == "return", "six whelps and three stashes: " + p.Stage("r2_small_fires"));
            p.TalkTo("r2_kesta", "Six whelps seen off");
            Assert(s.Quests.IsCompleted("r2_small_fires"), "Small Fires");
            p.TalkTo("r2_quartermaster", "One fire-gland", "There's always bad news", "Varkas, then the horn");
            Assert(s.Quests.IsCompleted("r2_twin_fires") && s.CountItem("r2_emberward_draught") == 5 && s.Quests.IsActive("r2_highlord"), "Twin Fires done; five draughts; the Highlord's Horn");

            // the Highlord's Court
            p.WalkInto("enc_r2_court_gate");
            p.WalkInto("enc_r2_varkas", "We're not here to kneel");
            Assert(p.Stage("r2_highlord") == "horn", "Varkas is down");
            p.Use("r2_varkas_horn", "(Take the Horn");
            p.TalkTo("r2_quartermaster", "Varkas is dead", "Then I'll blow the horn");
            Assert(s.Quests.IsCompleted("r2_highlord") && s.Quests.IsActive("r2_last_ember") && s.CountItem("r2_horn_of_calling") == 1, "The Last Ember begins");

            // the Wyrm Stair and the summit: the horn, the Ashwyrm, the nest
            p.WalkInto("enc_r2_stair_guard");
            p.Go(Db.Maps[Roost].regions.First(r => r.id == "reg_r2_summit").pos + new Vec2(0f, -8f), 2f);
            Assert(p.Stage("r2_last_ember") == "call", "on the summit: " + p.Stage("r2_last_ember"));
            p.Refresh();
            p.Use("r2_calling_stone", "(Blow the Horn", "Remember them", "(Fight.)");
            Assert(s.Flags.IsSet("r2_vyrmathra_defeated") && p.Stage("r2_last_ember") == "nest", "Vyrmathra is dead: " + p.Stage("r2_last_ember"));
            var v = p.Fights["enc_r2_vyrmathra"];
            Console.WriteLine($"    roost: Vyrmathra vs a raid of 10 at 33: {v.rounds} rounds, lowest raid health {v.minHp:P0}, {v.deaths} down");
            Assert(s.Map.IsChestAvailable(s.MapDef.chests.First(c => c.id == "chest_r2_hoard")), "the hoard is there");
            Assert(s.VisibleNpcs().Any(n => n.npc == "r2_sir_hamon_summit") && !s.VisibleNpcs().Any(n => n.npc == "r2_sir_hamon"), "Sir Hamon plants the standard on the summit");
            p.Use("r2_nest", "(Take the warm egg");
            Assert(s.Flags.IsSet("r2_egg_spared") && s.CountItem("r2_ember_egg") == 1 && p.Stage("r2_last_ember") == "return", "the warm egg goes down the mountain");

            // the epilogue
            p.TalkTo("r2_quartermaster", "Vyrmathra is dead", "One egg was still warm", "It took all of us");
            Assert(s.Quests.IsCompleted("r2_last_ember") && s.Flags.IsSet("r2_roost_epilogue") && s.CountItem("r2_ember_egg") == 0, "the epilogue; Marta takes the egg");
            foreach (var q in Db.Quests.Values.Where(q => q.zone == Roost)) Assert(s.Quests.IsCompleted(q.id), q.id + " complete");
            Console.WriteLine("    roost fights: " + string.Join(", ", p.Fights.Select(kv => $"{kv.Key} {kv.Value.rounds}r {kv.Value.minHp:P0}")));
        }

        [Test]
        public static void Horn_WaitsAtTheCallingStone_UntilMartaSendsYouUp()
        {
            // the Wyrm Stair is open from Varkas's court: the stone must not take the horn before Marta's hand-in
            var p = new Player(ClassId.Priest, 31, 3141, new[] { "seren" });
            var s = p.S;
            s.Quests.Start("r2_highlord");
            s.Flags.Set("r2_varkas_defeated");
            s.GiveItem("r2_horn_of_calling", 1);
            Assert(p.Stage("r2_highlord") == "return" && s.CountItem("r2_horn_of_calling") == 1, "Varkas is down, the horn in hand: " + p.Stage("r2_highlord"));

            Assert(s.StartDialogue("dlg_r2_calling_stone", "r2_calling_stone"), "at the calling stone: " + s.LastError);
            SessionTest.SkipText(s);
            var v = s.Dialogue.Current;
            Assert(v.NodeId == "c2_wait" && !v.Choices.Any(c => c.Text.Contains("Blow")), "the horn waits for Marta: " + SessionTest.Describe(v));
            s.Dialogue.End();
            Assert(s.CountItem("r2_horn_of_calling") == 1 && !s.Flags.IsSet("r2_horn_blown"), "the horn is kept");

            p.TalkTo("r2_quartermaster", "Varkas is dead", "Then I'll blow the horn");
            Assert(s.Quests.IsCompleted("r2_highlord") && s.Quests.IsActive("r2_last_ember"), "Marta takes the news and sends you up");

            Assert(s.StartDialogue("dlg_r2_calling_stone", "r2_calling_stone"), "back at the calling stone: " + s.LastError);
            SessionTest.SkipText(s);
            v = s.Dialogue.Current;
            Assert(v.NodeId == "c2" && v.Choices.Any(c => c.Text.Contains("(Blow the Horn")), "now the horn can be blown: " + SessionTest.Describe(v));
            s.Dialogue.End();
        }

        [Test]
        public static void HamonAndKesta_OfferAndTakeTheirQuests_AfterVyrmathraFalls()
        {
            // the summit can be won before the camp's side quests are taken or handed in
            var p = new Player(ClassId.Priest, 31, 3142, new[] { "seren" });
            var s = p.S;
            s.Flags.Set("r2_vyrmathra_defeated");
            Assert(s.QuestMarkerOf("r2_sir_hamon").Kind == QuestMarker.Available && s.QuestMarkerOf("r2_kesta").Kind == QuestMarker.Available,
                $"Hamon and Kesta still offer: {s.QuestMarkerOf("r2_sir_hamon")} / {s.QuestMarkerOf("r2_kesta")}");
            p.TalkTo("r2_sir_hamon", "anything I can do for them", "I'll find his standard");
            p.TalkTo("r2_kesta", "lost something", "I'll get your stores back");
            Assert(s.Quests.IsActive("r2_twenty_nine") && s.Quests.IsActive("r2_small_fires"), "both quests taken after the kill");

            s.GiveItem("r2_aubric_standard", 1);
            s.Quests.SetStage("r2_twenty_nine", "return");
            s.GiveItem("r2_stolen_stores", 3);
            s.Quests.SetStage("r2_small_fires", "return");
            Assert(s.QuestMarkerOf("r2_sir_hamon").Kind == QuestMarker.ReadyToTurnIn && s.QuestMarkerOf("r2_kesta").Kind == QuestMarker.ReadyToTurnIn,
                $"yellow ? on both: {s.QuestMarkerOf("r2_sir_hamon")} / {s.QuestMarkerOf("r2_kesta")}");

            p.TalkTo("r2_sir_hamon", "Plant it at the summit");
            Assert(s.Quests.IsCompleted("r2_twenty_nine") && s.CountItem("r2_aubric_standard") == 0, "The Twenty-Nine handed in after the kill");
            Assert(s.VisibleNpcs().Any(n => n.npc == "r2_sir_hamon_summit") && !s.VisibleNpcs().Any(n => n.npc == "r2_sir_hamon"), "Sir Hamon goes up to plant it");
            p.TalkTo("r2_kesta", "Six whelps seen off");
            Assert(s.Quests.IsCompleted("r2_small_fires") && s.CountItem("r2_stolen_stores") == 0, "Small Fires handed in after the kill");
            Assert(s.QuestMarkerOf("r2_kesta").Kind == QuestMarker.None, "no marker left on Kesta: " + s.QuestMarkerOf("r2_kesta"));
        }

        // ===================================================================================================== the bosses

        static (BattleOutcome outcome, int rounds, float minHp, int deaths, float minOne, HashSet<string> landed) Pull(string[] companions, string encId, ulong seed, int level = 32)
        {
            var p = new Player(ClassId.Warrior, level, seed, companions);
            var s = p.S;
            RaidTest.Pacify(s);
            if (encId == "enc_r2_vyrmathra") s.Flags.Set("r2_horn_blown");
            s.Map.ResetEncounter(encId);
            var b = s.StartEncounter(encId);
            Assert(b != null, "fight " + encId + ": " + s.LastError);
            var party = b.Units.Where(u => u.Team == b.PlayerTeam && u.IsCharacter).ToList();
            float max = party.Sum(u => u.MaxHealth), min = 1f, one = 1f;
            int guard = 0;
            while (!b.IsOver && b.Round <= 80 && guard++ < 200000)
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                AI.RunTurn(b, u);
                if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
                min = Math.Min(min, party.Sum(x => Math.Max(0f, x.Health)) / max);
                foreach (var x in party) one = Math.Min(one, Math.Max(0f, x.Health) / x.MaxHealth);
            }
            // the boss side's abilities that landed (CastComplete, AbilityUsed): a cast that was started and interrupted does not count
            var landed = new HashSet<string>(b.Events.Where(e => (e.Type == CombatEventType.CastComplete || e.Type == CombatEventType.AbilityUsed) && e.Source != null && e.Source.Team != b.PlayerTeam).Select(e => e.AbilityId));
            return (b.Outcome, b.Round, min, party.Count(u => !u.IsAlive), one, landed);
        }

        [Test]
        public static void Bosses_ARaidOfTenWins_FiveOfTheSameLevelLose()
        {
            // three pulls each (different seeds: gear rolls and the fight's dice): the raid of 10 wins at least two and is
            // pushed in at least one (someone drops below 40 % or goes down), and the raid-wide casts land (they cannot be
            // interrupted: the healer checks); five of the same level win none
            var mechanics = new Dictionary<string, string[]>
            {
                ["enc_r2_frostclaw"] = new[] { "cr_r2_bitter_cold", "cr_r2_avalanche" },
                ["enc_r2_cinder_drakes"] = new[] { "cr_r2_ash_breath", "cr_r2_choking_cinders" },
                ["enc_r2_varkas"] = new[] { "cr_r2_brand_of_the_wyrm", "cr_r2_wyrmfire_slam" },
                ["enc_r2_vyrmathra"] = new[] { "cr_r2_fire_breath", "cr_r2_rain_of_cinders", "cr_r2_hollowfire" },
            };
            ulong seed = 7100;
            var bad = new List<string>();
            foreach (var enc in Bosses)
            {
                int tenWins = 0, fiveWins = 0, minRounds = 999;
                bool pushed = false;
                var landed = new HashSet<string>();
                for (int k = 0; k < 3; k++)
                {
                    var ten = Pull(Ten, enc, seed++);
                    Console.WriteLine($"    roost: {enc} vs 10 at 32: {ten.outcome} in {ten.rounds} rounds, lowest raid health {ten.minHp:P0}, lowest member {ten.minOne:P0}, {ten.deaths} down");
                    if (ten.outcome == BattleOutcome.Victory) { tenWins++; minRounds = Math.Min(minRounds, ten.rounds); landed.UnionWith(ten.landed); }
                    if (ten.minOne < 0.4f || ten.deaths > 0) pushed = true;
                    var five = Pull(Five, enc, seed++);
                    Console.WriteLine($"    roost: {enc} vs 5 at 32: {five.outcome} in {five.rounds} rounds, {five.deaths} down");
                    if (five.outcome == BattleOutcome.Victory) fiveWins++;
                }
                if (tenWins < 2) bad.Add($"{enc}: a raid of 10 at 32 wins ({tenWins}/3)");
                if (!pushed || minRounds < 6) bad.Add($"{enc}: not trivial (pushed {pushed}, shortest win {minRounds} rounds)");
                if (enc == "enc_r2_vyrmathra" && minRounds < 15) bad.Add("the finale lasts many rounds: " + minRounds);
                if (fiveWins > 0) bad.Add($"{enc}: five at 32 do not win ({fiveWins}/3)");
                foreach (var a in mechanics[enc]) if (!landed.Contains(a)) bad.Add($"{enc}: {a} lands in a won fight");
            }
            Assert(bad.Count == 0, string.Join("\n    ", bad));
        }

        /// <summary>[Sim] win rates over seeds (Tools/check.sh core --sim --filter TestsContentR2.Sim).</summary>
        [Sim]
        public static void Sim_BossWinRates()
        {
            foreach (var enc in Bosses)
                foreach (var (name, comp) in new[] { ("10", Ten), ("5", Five) })
                {
                    int wins = 0, n = 12, rounds = 0;
                    float low = 0f;
                    for (ulong k = 0; k < (ulong)n; k++)
                    {
                        var r = Pull(comp, enc, 9000 + k * 13);
                        if (r.outcome == BattleOutcome.Victory) wins++;
                        else if (name == "10") Console.WriteLine($"      loss {enc} seed {9000 + k * 13}: {r.rounds} rounds, {r.deaths} down");
                        rounds += r.rounds;
                        low += r.minHp;
                    }
                    Console.WriteLine($"    sim {enc} vs {name}: {wins}/{n} wins, avg {rounds / n} rounds, avg lowest raid health {low / n:P0}");
                }
        }

        // ===================================================================================================== markers

        [Test]
        public static void Markers_FollowTheGateLevelAndQuestOrder()
        {
            var lo = new Player(ClassId.Priest, 28, 41, new[] { "seren" });
            var m = lo.S.QuestMarkerOf("r2_quartermaster");
            Assert(m.Kind == QuestMarker.AvailableLater && m.Level == 30, "Marta at 28: grey ! for level 30: " + m);

            var ungated = new Player(ClassId.Priest, 31, 42, new[] { "seren" }, gate: false);
            Assert(ungated.S.QuestMarkerOf("r2_quartermaster").Kind != QuestMarker.Available, "no offer before Ash on the Wind: " + ungated.S.QuestMarkerOf("r2_quartermaster"));

            var p = new Player(ClassId.Priest, 31, 43, new[] { "seren" });
            var s = p.S;
            Assert(s.QuestMarkerOf("r2_quartermaster").Kind == QuestMarker.Available && s.QuestMarkerOf("r2_quartermaster").QuestId == "r2_matriarch", "Marta offers A Cold Welcome first");
            Assert(s.QuestMarkerOf("r2_kesta").Kind == QuestMarker.Available && s.QuestMarkerOf("r2_sir_hamon").Kind == QuestMarker.Available, "Kesta and Sir Hamon offer");
            s.Quests.Start("r2_matriarch");
            s.Flags.Set("r2_frostclaw_defeated");
            s.Flags.Set("r2_cubs_freed");
            Assert(p.Stage("r2_matriarch") == "return" && s.QuestMarkerOf("r2_quartermaster").Kind == QuestMarker.ReadyToTurnIn, "the cubs freed: Marta's yellow ?");
            foreach (var q in Db.Quests.Values.Where(q => q.zone == Roost))
            {
                Assert(q.minLevel == 30 && Db.Npcs.ContainsKey(q.giver), $"{q.id}: level gate and giver");
                Assert(s.QuestMarkerIndex.Starters(q.id).Contains(q.giver), $"{q.id}: {q.giver} offers it");
                Assert(s.QuestMarkerIndex.Enders(q.id).Count > 0, $"{q.id}: has a turn-in");
            }
        }

        // ===================================================================================================== XP

        [Test]
        public static void Xp_TheRaidCarriesTheBand()
        {
            // kill XP for a character at 31 (each pack once, bosses at +2) plus quest XP, against 31 → 33
            int lvl = 31;
            float kills = 0f;
            foreach (var e in Db.Maps[Roost].encounters)
                foreach (var x in e.enemies)
                {
                    var c = Db.Creatures[x.creature];
                    int ml = UnitFactory.CreatureLevel(c, 0, lvl, null);
                    kills += Formulas.MobXp(lvl, ml) * Progression.XpRankMult(c.rank) * c.xpMult * Progression.XpRate(Db, lvl);
                }
            int quests = Db.Quests.Values.Where(q => q.zone == Roost).Sum(q => Progression.QuestXp(Db, q.rewards.xp, lvl));
            int need = Progression.XpToNextLevel(Db, 31) + Progression.XpToNextLevel(Db, 32);
            float total = kills + quests;
            Console.WriteLine($"    roost XP at 31: kills {kills:0}, quests {quests}, total {total:0} vs 31 → 33 {need} ({total / need:P0})");
            Assert(total >= need * 0.85f && total <= need * 1.3f, $"the raid carries about two levels: {total:0} vs {need}");
        }
    }
}
