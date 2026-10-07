// Brightwater, the river trade town (Docs/Expansion.md §1, §8 builder "brightwater"): the map (every spawn, exit, NPC and
// chest reachable; the river only crossed at the bridge, the Heron Ford, and by ferry), the services (an inn that rests,
// four trainers covering all eight classes, vendors with level 12-30 stock), the town quests scripted to completion at the
// band's top level (and their fallbacks when every skill check fails), the Archivist's hints that reveal the hidden
// dungeons of Amberfield, Mirefen and Skyreach, the passive region checks, and the dialogue rules the walkers rely on.
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

namespace Lanternvale.Tests
{
    public static class TestsContentBw
    {
        const string MapId = "brightwater";
        static readonly string[] Quests = { "bw_q_parcels", "bw_q_lost_heron", "bw_q_smugglers", "bw_q_forty_names", "bw_q_fen_road", "bw_q_high_road" };
        static readonly string[] LeaveWords = { "Goodbye", "(Continue.)", "Let's keep moving", "(Leave", "Rest", "Just passing by", "Never mind" };

        static GameSession Town(int level, ClassId c = ClassId.Mage, int checkBonus = 0, ulong seed = 4401)
        {
            var s = SessionTest.NewGame(c, level, seed);
            if (checkBonus != 0) s.ExtraSkillCheckBonus = (id, skill) => checkBonus;
            s.EnterMap(MapId, "default");
            Assert(s.MapId == MapId, "in Brightwater");
            return s;
        }

        static Vec2 NpcPos(GameSession s, string npc)
        {
            var n = s.VisibleNpcs().FirstOrDefault(x => x.npc == npc);
            Assert(n != null, $"{npc} stands in {s.MapId}");
            return n.pos;
        }

        /// <summary>Walks next to a point (fighting nothing: the town has no hostile encounters) and asserts arrival.</summary>
        static void Walk(GameSession s, Vec2 p, float near = 2.6f)
        {
            for (int i = 0; i < 6 && Vec2.Distance(s.Leader.Position, p) > near; i++)
            {
                SessionTest.WalkTo(s, p);
                if (s.Mode == SessionMode.Dialogue) SessionTest.Finish(s);
                Assert(s.Mode != SessionMode.Combat, "no fight in Brightwater on the way to " + p);
                Assert(s.MapId == MapId, $"still in Brightwater walking to {p} (now {s.MapId})");
            }
            Assert(Vec2.Distance(s.Leader.Position, p) <= near + 0.5f, $"reached {p} (at {s.Leader.Position})");
        }

        /// <summary>Walks to an NPC, talks, and picks the scripted choices in order (text fragments); then leaves.</summary>
        static void Talk(GameSession s, string npc, params string[] picks)
        {
            Walk(s, NpcPos(s, npc));
            var r = s.TalkTo(npc);
            Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npc}: {r.Message}");
            foreach (var p in picks) SessionTest.Pick(s, p);
            SessionTest.Finish(s);
        }

        static void UseProp(GameSession s, string interact, params string[] picks)
        {
            var prop = s.Map.Def.props.First(p => p.interact == interact);
            Walk(s, prop.pos, 2.4f);
            var r = s.InteractProp(interact);
            Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"{interact} starts its dialogue ({r.Message})");
            foreach (var p in picks) SessionTest.Pick(s, p);
            SessionTest.Finish(s);
        }

        static void Claim(GameSession s, string q)
        {
            Assert(s.Quests.IsCompleted(q), q + " completed");
            if (!s.PendingQuestRewards.Contains(q)) return;
            var choices = s.QuestRewardChoices(q);
            Assert(choices.Count == 3, q + ": three reward choices");
            Assert(s.ClaimQuestReward(q, choices[0].id) == null, q + ": reward claimed");
        }

        static void Stage(GameSession s, string q, string stage) =>
            Assert(s.Quests.IsActive(q) && s.Quests.GetStage(q) == stage, $"{q} at '{stage}' (is {(s.Quests.IsActive(q) ? s.Quests.GetStage(q) : "not active")})");

        // ------------------------------------------------------------------ the map

        [Test]
        public static void Map_ContractLinks_AndEverythingReachable()
        {
            var m = Db.Maps[MapId];
            Assert(m.width == 84 && m.depth == 44 && m.biome == "village" && m.restArea && m.levelMin == 12 && m.levelMax == 30, "the §1 row");
            Assert(m.fill >= 0.3f && m.fill <= 0.5f && m.paths.Count >= 8 && m.water.Count >= 1, "fill, paths and a river");
            foreach (var (id, target, spawn, x, y) in new[] { ("to_amberfield", "amberfield", "from_brightwater", 83.3f, 22f),
                                                             ("to_mirefen", "mirefen", "from_brightwater", 0.7f, 12f),
                                                             ("to_skyreach", "skyreach", "from_brightwater", 0.7f, 34f) })
            {
                var t = m.transitions.FirstOrDefault(tr => tr.id == id);
                Assert(t != null && t.targetMap == target && t.targetSpawn == spawn && Math.Abs(t.pos.x - x) < 0.01f && Math.Abs(t.pos.y - y) < 0.01f, id + " as in §1");
            }
            foreach (var sp in new[] { "default", "from_amberfield", "from_mirefen", "from_skyreach", "bw_ferry_west", "bw_ferry_east" })
                Assert(m.spawns.Any(x => x.id == sp), "spawn " + sp);
            var problems = TestsMapsReachable.Unreachable(m);
            Assert(problems.Count == 0, "everything reachable: " + string.Join("; ", problems));
            Assert(m.encounters.All(e => e.enemies.All(en => en.creature == "cr_training_dummy")), "no hostile encounters in town (only the straw knight)");

            // the river: a stone bridge with a crossing, the Heron Ford, and docks that never reach the far bank
            var river = m.water[0];
            Assert(m.props.Any(p => p.art == "prop_bridge_stone" && river.crossings.Any(c => Math.Abs(c.pos.x - p.pos.x) < 1.5f && Math.Abs(c.pos.y - p.pos.y) < 1.5f)), "the bridge has its crossing");
            var nav = new NavGrid(m, null, TestsMapsReachable.AllFlagsSet);
            var agent = NavAgent.Default;
            var east = nav.ClampToWalkable(new Vec2(40, 22), agent);
            var west = nav.ClampToWalkable(new Vec2(22, 22), agent);
            Assert(nav.FindPath(east, west, agent).Status == PathStatus.Complete, "east and west banks joined");
            var copy = JsonMapper.FromJson<MapDef>(JsonWriter.Serialize(m, false));
            copy.water[0].crossings.RemoveAll(c => Math.Abs(c.pos.y - 22) < 1.5f || Math.Abs(c.pos.y - 38.6f) < 2f);
            var cut = new NavGrid(copy, null, TestsMapsReachable.AllFlagsSet);
            Assert(cut.FindPath(cut.ClampToWalkable(new Vec2(40, 22), agent), cut.ClampToWalkable(new Vec2(22, 22), agent), agent).Status != PathStatus.Complete,
                "without the bridge and the ford the river blocks the way (the docks stop mid-stream)");
        }

        [Test]
        public static void Enter_FromEveryNeighbour_AndTheFerry()
        {
            var s = Town(16);
            foreach (var sp in new[] { "from_amberfield", "from_mirefen", "from_skyreach" })
            {
                s.EnterMap(MapId, sp);
                Assert(s.MapId == MapId && s.Nav.IsWalkable(s.Leader.Position, NavAgent.Default), "arrive walkable at " + sp);
            }
            s.EnterMap(MapId, "default");
            int gold = s.Gold;
            Talk(s, "bw_ferryman_gideon", "Take me across");
            Assert(s.MapId == MapId && s.Leader.Position.x < 28f && s.Gold == gold - 5, $"the ferry crossed to the west bank for 5 copper (at {s.Leader.Position})");
            UseProp(s, "bw_ferry_bell", "Ride back");
            Assert(s.Leader.Position.x > 34f && s.Gold == gold - 10, "and the bell brings it back to the harbour");
        }

        // ------------------------------------------------------------------ services

        [Test]
        public static void Services_TrainersInnAndVendors()
        {
            var covered = new HashSet<ClassId>();
            var m = Db.Maps[MapId];
            foreach (var n in m.npcs)
                if (Db.Npcs.TryGetValue(n.npc, out var def) && def.trains != null) foreach (var c in def.trains) covered.Add(c);
            Assert(covered.Count == 8, "the town's four trainers cover all eight classes: " + string.Join(",", covered));
            Assert(m.npcs.Count(n => Db.Npcs.TryGetValue(n.npc, out var d) && d.trains != null && d.trains.Length == 2) == 4, "four trainers, two classes each");
            Assert(m.npcs.Count(n => Db.Npcs.TryGetValue(n.npc, out var d) && d.innkeeper) == 1, "one innkeeper");

            int Req(string vendor, Func<ItemDef, bool> what) => Db.Npcs[vendor].vendor.Select(v => Db.Item(v.item)).Where(what).Select(d => d.requiredLevel).DefaultIfEmpty(0).Max();
            Assert(Req("bw_weaponsmith_dunstan", d => d.equip != EquipType.None) >= 25, "weapons up to level 25+");
            Assert(Req("bw_armourer_dagny", d => d.armorType == ArmorType.Mail) >= 25, "mail up to level 25+");
            foreach (var v in new[] { "bw_weaponsmith_dunstan", "bw_armourer_dagny" })
                foreach (var it in Db.Npcs[v].vendor.Select(x => Db.Item(x.item)))
                {
                    Assert(it.quality == Quality.Common || it.quality == Quality.Uncommon, $"{v}: {it.id} Common/Uncommon");
                    Assert(it.requiredLevel >= 12 && it.requiredLevel <= 30, $"{v}: {it.id} for levels 12-30 ({it.requiredLevel})");
                    Assert(it.armorType != ArmorType.Plate, $"{it.id}: wearable below 40");
                    Assert(it.quality == Quality.Common || it.stats.Count > 0, $"{it.id}: every green has stats");
                    Assert(string.IsNullOrEmpty(it.use), $"{it.id}: no use on equipables");
                }
            var potions = Db.Npcs["bw_apothecary_ottoline"].vendor.Select(x => Db.Item(x.item)).ToList();
            Assert(potions.Count(p => p.use.Length > 0 && Db.Ability(p.use) != null) >= 12 && potions.Max(p => p.requiredLevel) >= 28, "a dozen consumables up to level 28+");

            // the inn rests, the trainers train, vendors open
            var s = Town(20, ClassId.Warrior);
            foreach (var u in s.Party) u.Health = 1;
            Talk(s, "bw_innkeeper_marigold", "room for the night");
            Assert(s.Party.All(u => u.Health >= u.MaxHealth - 0.5f), "a night at the Lantern & Heron heals everyone");
            Walk(s, NpcPos(s, "bw_trainer_bertram"));
            Assert(s.TalkTo("bw_trainer_bertram").Ok, "Bertram talks");
            SessionTest.Pick(s, "Teach me to hit harder");
            SessionTest.Finish(s);
            Assert(SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.TrainerOpened) != null, "Bertram trains warriors");
        }

        // ------------------------------------------------------------------ dialogue rules

        [Test]
        public static void Dialogues_ExitWordsOnlyOnExits_AndEveryHubCanBeLeft()
        {
            var mine = Db.Dialogues.Values.Where(d => d.id.StartsWith("dlg_bw_", StringComparison.Ordinal)).ToList();
            Assert(mine.Count >= 20, "Brightwater has its people's dialogues: " + mine.Count);
            foreach (var d in mine)
                foreach (var n in d.nodes)
                {
                    foreach (var c in n.choices)
                        foreach (var w in LeaveWords)
                            Assert(c.text.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0 || string.IsNullOrEmpty(c.next),
                                $"{d.id}.{n.id}: '{c.text}' uses the exit word '{w}' but does not end the conversation");
                    if (n.choices.Count >= 3)
                        Assert(n.choices.Any(c => string.IsNullOrEmpty(c.next) && c.conditions.Count == 0 && c.check == null), $"{d.id}.{n.id}: an unconditional way out");
                }
            foreach (var q in Quests)
            {
                var def = Db.Quests[q];
                Assert(def.zone == MapId && Db.Npcs.ContainsKey(def.giver), q + ": zone and giver");
                var last = def.stages.Last();
                Assert(!string.IsNullOrEmpty(last.turnIn) && last.objectives.All(o => o.type == ObjectiveType.Flag), q + ": hand-in is a flag set at the turn-in NPC");
            }
        }

        // ------------------------------------------------------------------ the quest chain

        [Test]
        public static void TownQuests_ScriptedToCompletion_AtTheBandsTop()
        {
            var s = Town(30, ClassId.Mage, checkBonus: 100);
            Assert(s.QuestMarkerOf("bw_dockmaster_hobb").Kind == QuestMarker.Available, "Hobb offers work (yellow !)");

            // Parcels on the Tide
            Talk(s, "bw_dockmaster_hobb", "Need a hand on the docks", "I'll carry them");
            Stage(s, "bw_q_parcels", "wicks");
            Assert(s.QuestMarkerOf("bw_mother_wren").Kind == QuestMarker.ReadyToTurnIn, "Mother Wren awaits her wicks (yellow ?): " + s.QuestMarkerOf("bw_mother_wren") + " | hobb " + s.QuestMarkerOf("bw_dockmaster_hobb"));
            Talk(s, "bw_mother_wren", "parcel of lamp wicks");
            Talk(s, "bw_trainer_vell", "clock springs");
            Talk(s, "bw_captain_rowan", "sealed letter");
            Stage(s, "bw_q_parcels", "return");
            Talk(s, "bw_dockmaster_hobb", "All three parcels");
            Claim(s, "bw_q_parcels");

            // Admiral Puddles Is Missing
            Talk(s, "bw_child_tamsin", "Lost something", "find your Admiral");
            Stage(s, "bw_q_lost_heron", "ask");
            Talk(s, "bw_dockhand_nettie");
            Stage(s, "bw_q_lost_heron", "find");
            Assert(s.CountItem("bw_minnows") == 1, "Nettie's minnows");
            UseProp(s, "bw_heron_reeds", "bucket of minnows");
            Assert(s.CountItem("bw_admiral_puddles") == 1, "the Admiral is in the basket");
            Talk(s, "bw_child_tamsin", "Here he is", "Dame Ysolde");
            Claim(s, "bw_q_lost_heron");
            Assert(s.Flags.IsSet("bw_puddles_heronguard") && !s.Flags.IsSet("bw_puddles_home"), "the Admiral goes to Ysolde for training");

            // Something Fishy (passive Perception at the warehouse, Investigation at the crates, evidence at the stall)
            Talk(s, "bw_dockhand_bo", "Something wrong with those crates", "take a look");
            UseProp(s, "bw_herring_crates", "[INVESTIGATION]");
            Assert(s.Flags.IsSet("bw_oily_footprints"), "the warehouse yard's passive check found the oily footprints");
            Stage(s, "bw_q_smugglers", "confront");
            Assert(s.CountItem("bw_oil_jar") == 1, "a jar of stolen oil as evidence");
            Talk(s, "bw_fishmonger_silas", "Your boots are oily", "coming with me");
            Assert(!s.VisibleNpcs().Any(n => n.npc == "bw_fishmonger_silas"), "Silas has left his stall");
            Talk(s, "bw_captain_rowan", "He's confessed");
            Claim(s, "bw_q_smugglers");
            Assert(s.Flags.IsSet("bw_dragonsworn_lead"), "the buyer points up the high road");

            // Forty Names
            Talk(s, "bw_trainer_bertram", "Is something troubling you", "gather the lanterns");
            Stage(s, "bw_q_forty_names", "gideon");
            Assert(s.QuestMarkerOf("bw_ferryman_gideon").Kind == QuestMarker.ReadyToTurnIn, "Gideon has his lantern ready (yellow ?)");
            Talk(s, "bw_ferryman_gideon", "gathering the lanterns");
            Assert(s.QuestMarkerOf("bw_captain_rowan").Kind == QuestMarker.ReadyToTurnIn, "then Rowan (yellow ?)");
            Talk(s, "bw_captain_rowan", "gathering the lanterns");
            Talk(s, "bw_dockhand_nettie", "gathering the lanterns", "[PERSUASION]");
            Stage(s, "bw_q_forty_names", "light");
            Assert(s.CountItem("bw_memorial_lantern") == 3, "three lanterns");
            var lit = s.Map.Def.props.First(p => p.art == "prop_spirit_lantern" && p.requireFlag == "bw_memorial_lit");
            Assert(!s.Map.IsPropVisible(lit), "the memorial lanterns are dark");
            UseProp(s, "bw_heronguard_memorial", "Light the three lanterns");
            Assert(s.Map.IsPropVisible(lit) && s.CountItem("bw_memorial_lantern") == 0, "lit at the stone");
            Talk(s, "bw_trainer_bertram", "The lanterns are lit");
            Claim(s, "bw_q_forty_names");

            // The Fen Road and The High Road: out of the west gate, back, report
            Talk(s, "bw_captain_rowan", "work for the Watch", "walk the fen road");
            Walk(s, new Vec2(3.6f, 12f));
            SessionTest.WalkTo(s, new Vec2(0.5f, 12f));
            Assert(s.MapId == "mirefen", "the fen road leads to Mirefen");
            Stage(s, "bw_q_fen_road", "report");
            s.EnterMap(MapId, "from_mirefen");
            Talk(s, "bw_captain_rowan", "walked the fen road");
            Claim(s, "bw_q_fen_road");
            Talk(s, "bw_captain_rowan", "Anything else the Watch needs", "climb to Skyreach");
            Walk(s, new Vec2(3.6f, 34f));
            SessionTest.WalkTo(s, new Vec2(0.5f, 34f));
            Assert(s.MapId == "skyreach", "the high road leads to Skyreach");
            s.EnterMap(MapId, "from_skyreach");
            Talk(s, "bw_captain_rowan", "up the high road");
            Claim(s, "bw_q_high_road");

            foreach (var q in Quests) Assert(s.Quests.IsCompleted(q), q + " done");
        }

        [Test]
        public static void TownQuests_FallbacksWhenEveryCheckFails()
        {
            var s = Town(24, ClassId.Priest, checkBonus: -100, seed: 4402);
            // the heron: no minnows, every check fails, patience works
            Talk(s, "bw_child_tamsin", "Lost something", "find your Admiral");
            Talk(s, "bw_dockhand_nettie");
            s.TakeItem("bw_minnows", 1);
            Assert(s.CountItem("bw_minnows") == 0, "drop the minnows");
            UseProp(s, "bw_heron_reeds", "[NATURE]", "[ATHLETICS]", "wait as long as it takes");
            Assert(s.Flags.IsSet("bw_puddles_found"), "patience finds the Admiral");
            Talk(s, "bw_child_tamsin", "Here he is", "crate with a roof");
            Assert(s.Quests.IsCompleted("bw_q_lost_heron") && s.CountItem("bw_tamsins_lucky_button") == 1, "kept at home, with Tamsin's lucky button");

            // the smugglers: nothing spotted, checks fail, smash the crate; Silas will not talk; the bribe
            Talk(s, "bw_dockhand_bo", "Something wrong with those crates", "take a look");
            UseProp(s, "bw_herring_crates", "[INVESTIGATION]", "[PERCEPTION]", "Smash a crate");
            Assert(s.Flags.IsSet("bw_crates_inspected") && s.Flags.IsSet("bw_smuggler_alerted") && !s.Flags.IsSet("bw_oily_footprints"), "smashed open, Silas alerted, no footprints");
            int gold = s.Gold;
            Talk(s, "bw_fishmonger_silas", "[INTIMIDATION]", "midnight boat");
            Assert(s.Flags.IsSet("bw_silas_silent"), "no name, but caught");
            Talk(s, "bw_captain_rowan", "won't name his buyer");
            Assert(s.Quests.IsCompleted("bw_q_smugglers") && !s.Flags.IsSet("bw_dragonsworn_lead"), "done, without the lead");

            // Nettie: the persuasion fails, the honest answer still works
            Talk(s, "bw_trainer_bertram", "Is something troubling you", "gather the lanterns");
            Talk(s, "bw_dockhand_nettie", "gathering the lanterns", "[PERSUASION]", "carry it for your mother");
            Assert(s.Flags.IsSet("bw_lantern_nettie"), "Nettie's lantern");
        }

        [Test]
        public static void Smugglers_TheBribeHasAPrice()
        {
            var s = Town(20, ClassId.Rogue, checkBonus: 100, seed: 4403);
            Talk(s, "bw_dockhand_bo", "Something wrong with those crates", "take a look");
            UseProp(s, "bw_herring_crates", "[ROGUE]");
            Assert(s.Flags.IsSet("bw_crates_inspected") && !s.Flags.IsSet("bw_smuggler_alerted"), "a rogue opens the crates without a sound");
            int gold = s.Gold;
            Talk(s, "bw_fishmonger_silas", "[PERSUASION] Talk to me", "How heavy", "Deal");
            Assert(s.Gold == gold + 2500 && s.Flags.IsSet("bw_silas_bribed") && !s.Flags.IsSet("bw_silas_confessed"), "twenty-five silver, and no name");
            Assert(!s.VisibleNpcs().Any(n => n.npc == "bw_fishmonger_silas"), "Silas is gone downriver");
            Talk(s, "bw_captain_rowan", "resolved, Captain");
            Assert(s.Quests.IsCompleted("bw_q_smugglers") && s.Flags.IsSet("bw_rowan_disappointed") && !s.Flags.IsSet("bw_dragonsworn_lead"), "the Captain knows, and the lead is lost");
            Walk(s, NpcPos(s, "bw_dockhand_bo"));
            Assert(s.TalkTo("bw_dockhand_bo").Ok && s.Dialogue.Current.NodeId == "bo_bribed", "Bo knows what happened");
            SessionTest.Finish(s);
        }

        // ------------------------------------------------------------------ Penhallow: lore and the hidden dungeons

        [Test]
        public static void Penhallow_HintsRevealTheHiddenDungeons()
        {
            var s = Town(16);
            bool Offers(string text)
            {
                Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
                SessionTest.SkipText(s);
                bool seen = SessionTest.ChoiceIndex(s.Dialogue.Current, text) >= 0;
                s.Dialogue.End();
                return seen;
            }
            Assert(!Offers("hidden places"), "no hints below level 17");
            Assert(s.QuestMarkerOf("bw_archivist_penhallow").Kind != QuestMarker.Available, "no new offers of his own");
            var reveals = new[] { ("found_barrow", "amberfield", "to_dgn_barrow", "barrow of King Aldwin", 17),
                                  ("found_drowned_vault", "mirefen", "to_dgn_drowned_vault", "Drowned places", 23),
                                  ("found_frozen_sanctum", "skyreach", "to_dgn_frozen_sanctum", "Cold places", 29) };
            foreach (var (flag, map, trans, choice, level) in reveals)
            {
                while (s.Main.Level < level) s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
                Assert(!s.Flags.IsSet(flag), flag + " not yet set");
                Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk");
                SessionTest.Pick(s, "hidden places");
                SessionTest.Pick(s, choice);
                SessionTest.Finish(s);
                Assert(s.Flags.IsSet(flag), $"Penhallow's hint sets {flag} at level {level}");
                var t = Db.Maps[map].transitions.FirstOrDefault(x => x.id == trans);
                Assert(t != null && t.hidden && t.revealFlag == flag, $"{map}/{trans} is revealed by {flag}");
                Assert(!Offers(choice), "and the hint is not offered twice");
            }
            // lore and rumours keep him a hub; the stub's offers are untouched
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk");
            SessionTest.Pick(s, "Heronguard");
            SessionTest.Pick(s, "Lord-Commander Aubric");
            SessionTest.Pick(s, "Ask about something else");
            SessionTest.Pick(s, "Any news");
            SessionTest.Finish(s);
        }

        // ------------------------------------------------------------------ secrets

        [Test]
        public static void RiverSteps_PerceptionRevealsTheCache()
        {
            var s = Town(18, ClassId.Hunter, checkBonus: 100);
            var cache = s.Map.FindChest("chest_bw_river_cache");
            Assert(cache != null && !s.Map.IsChestAvailable(cache), "the cache is hidden");
            Walk(s, new Vec2(26.6f, 17.4f), 0.8f);
            Assert(s.Map.State.checkedRegions.Contains("reg_bw_river_steps") && s.Flags.IsSet("bw_river_cache_found"), "spotted on the river steps");
            Assert(s.Map.IsChestAvailable(cache), "and now it can be opened");
            Walk(s, cache.pos, 2.0f);
            var r = s.OpenChest(cache.id);
            Assert(r.Ok, "open the cache: " + r.Message);

            var t = Town(18, ClassId.Hunter, checkBonus: -100, seed: 4404);
            Walk(t, new Vec2(26.6f, 17.4f), 0.8f);
            Assert(t.Map.State.checkedRegions.Contains("reg_bw_river_steps") && !t.Flags.IsSet("bw_river_cache_found"), "a failed look, once per save");
        }
    }
}
