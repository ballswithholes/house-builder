// Whisperwood's old forest (prefix ww2) and Mossdeep Grotto (prefix dg2), Docs/Expansion.md §8 builder "ww":
// the deepened map (brook as blocking water with the Old Bridge and two fords; every spawn, exit, NPC, chest and
// encounter reachable), the hidden grotto's reveals (a Perception region check, Komorebi's and Sprig's hints, the
// Kingstone's Investigation check), both north-band quest chains scripted to completion through the real GameSession
// (talk, escort-like flag chain, ambush, item hand-in, choices; the dungeon fought by the party AI), and the Moss King:
// a fitting party of five wins, a lone hero does not.
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
    public static class TestsContentWw2
    {
        const string WW = "whisperwood", DG = "dgn_mossdeep";
        const string Watch = "ww2_last_watch", Glow = "ww2_kings_glow";

        /// <summary>A level-N Paladin with Kael (tank), Seren (healer), Lys and Rook: a fitting party of five.</summary>
        static GameSession Party(int level, ulong seed = 31, ClassId main = ClassId.Paladin)
        {
            var s = SessionTest.NewGame(main, level, seed);
            foreach (var id in new[] { "kael", "seren", "lys", "rook" }) s.Recruit(id);
            Assert(s.Party.Count == Math.Min(5, s.PartySize), $"a party of five ({s.Party.Count})");
            return s;
        }

        /// <summary>Marks every encounter of the current map done except the named ones, so walks trigger only those.</summary>
        static void PacifyExcept(GameSession s, params string[] keep)
        {
            foreach (var e in s.MapDef.encounters)
                if (!keep.Contains(e.id)) s.Map.MarkEncounterDone(e.id);
        }

        static MapNpcDef VisibleNpc(GameSession s, string id) => s.VisibleNpcs().FirstOrDefault(n => n.npc == id);

        static void Talk(GameSession s, string npc)
        {
            Assert(VisibleNpc(s, npc) != null, $"{npc} is visible on {s.MapId}");
            var r = s.TalkTo(npc);
            Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npc}: {r.Message}");
        }

        static long TotalXp(GameSession s)
        {
            long t = s.Main.Xp;
            for (int l = 1; l < s.Main.Level; l++) t += Progression.XpToNextLevel(Db, l);
            return t;
        }

        /// <summary>Walks to an encounter and wins whatever triggers (encounter dialogues answered with choice).</summary>
        static void Clear(GameSession s, string encId, string choice = null)
        {
            var e = s.Map.FindEncounter(encId);
            Assert(e != null, $"{encId} on {s.MapId}");
            SessionTest.Refresh(s);
            for (int i = 0; i < 6 && !s.Map.IsEncounterDone(e); i++)
            {
                SessionTest.WalkTo(s, e.pos, choice);
                if (s.Mode == SessionMode.Combat) SessionTest.WinBattle(s);
            }
            Assert(s.Map.IsEncounterDone(e), $"{encId} cleared");
            if (s.PendingLoot != null) s.TakeAllLoot();
        }

        // ------------------------------------------------------------------ maps

        [Test]
        public static void Maps_EnterEverySpawn_EverythingReachable()
        {
            foreach (var id in new[] { WW, DG })
            {
                var m = Db.Maps[id];
                var problems = TestsMapsReachable.Unreachable(m);
                Assert(problems.Count == 0, $"{id}: everything reachable from default: {string.Join("; ", problems)}");
                foreach (var sp in m.spawns)
                {
                    var s = SessionTest.NewGame(ClassId.Warrior, 12);
                    s.EnterMap(id, sp.id);
                    Assert(s.MapId == id && s.Nav.IsWalkable(s.Leader.Position, NavAgent.Default), $"{id}/{sp.id}: entered on walkable ground");
                }
            }
            var dg = Db.Maps[DG];
            Assert(dg.dungeon && !dg.restArea && dg.environment == "cave" && dg.width == 60 && dg.depth == 40, "Mossdeep: a 60×40 hidden cave dungeon, no resting");
            Assert(dg.encounters.Count >= 4 && dg.encounters.Count <= 7 && dg.chests.Count >= 2 && dg.chests.Count <= 3, "4-7 encounters, 2-3 chests");
            int elitePacks = dg.encounters.Count(e => e.enemies.Count >= 2 && e.enemies.Count(x => Db.Creature(x.creature).rank == CreatureRank.Elite) >= 1);
            Assert(elitePacks >= 2, $"at least two elite packs ({elitePacks})");
            foreach (var e in dg.encounters)
                foreach (var en in e.enemies)
                {
                    var c = Db.Creature(en.creature);
                    Assert(c.scaleToParty && c.levelFloor == 12 && c.levelCap == 15, $"{c.id}: band floor 12, cap 15");
                }
        }

        [Test]
        public static void Brook_BlocksMovement_ButTheBridgeAndFordsCross()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 12);
            s.EnterMap(WW, "default");
            var nav = s.Nav;
            var agent = NavAgent.Default;
            var m = Db.Maps[WW];
            Assert(m.water.Count >= 1 && m.water[0].blocksMovement && m.water[0].crossings.Count >= 3, "the brook is blocking data water with crossings");
            Assert(!nav.IsWalkable(new Vec2(60.3f, 16f), agent) && !nav.IsWalkable(new Vec2(60.2f, 19.5f), agent), "the brook itself blocks");
            Assert(nav.IsWalkable(new Vec2(59f, 6.2f), agent), "the Old Bridge's deck is walkable");
            Assert(nav.IsWalkable(new Vec2(62f, 26.8f), agent) && nav.IsWalkable(new Vec2(59.2f, 36.8f), agent), "the Heron Ford and the Mossy Ford are walkable");
            var bridge = m.props.First(p => p.art == "prop_bridge");
            Assert(bridge.collider == null || bridge.collider.w <= 0f, "no collider on the bridge");
            // the playthrough's southern band: the wayshrine, the spiders' hollow and the toll fight's ground stay walkable
            var wayshrine = m.regions.First(r => r.id == "reg_wayshrine").pos;
            foreach (var p in new[] { wayshrine, s.Map.FindEncounter("enc_spiders").pos, new Vec2(62.6f, 7.2f), new Vec2(65f, 6.4f), new Vec2(56f, 5.4f) })
                Assert(nav.IsWalkable(nav.ClampToWalkable(p, agent), agent) && Vec2.Distance(nav.ClampToWalkable(p, agent), p) < 1.2f, $"{p} walkable");
            // west bank → east bank in the north crosses at a ford, not through the water
            var path = nav.FindPathToRange(new Vec2(52f, 30f), new Vec2(70f, 30f), 1f, agent);
            Assert(path.Status == PathStatus.Complete, "the north band's banks are linked by the fords");
        }

        // ------------------------------------------------------------------ the hidden grotto

        static GameSession AtTheHollow(bool success, out bool found)
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var s = SessionTest.NewGame(ClassId.Hunter, 12, seed);
                s.ExtraSkillCheckBonus = (id, skill) => success ? 100 : -100;
                s.EnterMap(WW, "default");
                var reg = s.MapDef.regions.First(r => r.id == "reg_ww2_mossy_hollow");
                Assert(reg.check != null && reg.check.skill == SkillCheck.Perception && reg.check.dc >= 12 && reg.check.dc <= 16, "Perception DC 12-16");
                PacifyExcept(s);
                SessionTest.WalkTo(s, reg.pos + new Vec2(0f, -1.5f));
                var ev = SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.SkillCheck, reg.id);
                if (ev?.Check == null) continue;
                if (ev.Check.Success != success) continue;
                found = s.Flags.IsSet("found_mossdeep");
                return s;
            }
            throw new Exception("no seed gave the wanted roll");
        }

        [Test]
        public static void Mossdeep_RevealedByThePerceptionCheck()
        {
            var s = AtTheHollow(true, out bool found);
            Assert(found, "a good Perception roll finds the grotto");
            var t = s.MapDef.transitions.First(x => x.id == "to_dgn_mossdeep");
            Assert(s.Map.IsTransitionVisible(t), "the cave mouth is revealed");
            var r = s.UseTransition(t.id);
            Assert(r.Ok && s.MapId == DG, "into Mossdeep Grotto");
            var back = s.UseTransition("to_whisperwood");
            Assert(back.Ok && s.MapId == WW && Vec2.Distance(s.Leader.Position, t.pos) < 4f, "and back out beside the entrance");

            var f = AtTheHollow(false, out bool found2);
            Assert(!found2 && !f.Map.IsTransitionVisible(t), "a poor roll walks past it");
            Assert(!f.UseTransition(t.id).Ok, "an unrevealed passage cannot be used");
        }

        [Test]
        public static void Mossdeep_RevealedByHints()
        {
            // Komorebi, once the lanterns are lit
            var s = SessionTest.NewGame(ClassId.Mage, 12);
            s.EnterMap(WW, "default");
            s.Flags.Set("lanterns_rekindled");
            Talk(s, "komorebi");
            SessionTest.Pick(s, "stirring in the old wood");
            SessionTest.Finish(s);
            Assert(s.Flags.IsSet("found_mossdeep"), "Komorebi's hint reveals the grotto");

            // the Kingstone: an Investigation check on an inspect prop
            var k = SessionTest.NewGame(ClassId.Rogue, 12, 5);
            k.ExtraSkillCheckBonus = (id, skill) => 100;
            k.EnterMap(WW, "default");
            var r = k.InteractProp("ww2_kingstone");
            Assert(r.Ok && r.Kind == InteractKind.Dialogue, "the Kingstone starts its dialogue");
            SessionTest.Pick(k, "Follow where the carving points");
            SessionTest.Finish(k);
            Assert(k.Flags.IsSet("found_mossdeep") || k.Dialogue.IsActive == false, "the carving leads to the cave");
            if (!k.Flags.IsSet("found_mossdeep"))
            {
                // a natural 1: the Nature reading still works
                k.InteractProp("ww2_kingstone");
                SessionTest.Pick(k, "Feel which way the moss grows");
                SessionTest.Finish(k);
            }
            Assert(k.Flags.IsSet("found_mossdeep"), "the Kingstone reveals the grotto");
        }

        // ------------------------------------------------------------------ The Last Watch

        [Test]
        public static void LastWatch_PatrolAmbushDispatchAndRelease()
        {
            var s = Party(12);
            s.EnterMap(WW, "default");
            PacifyExcept(s, "enc_ww2_kestrel_ambush");
            Assert(s.QuestMarkerOf("ww2_pell").Kind == QuestMarker.Available, "Pell offers his patrol (yellow !)");
            var low = SessionTest.NewGame(ClassId.Paladin, 8);
            low.EnterMap(WW, "default");
            Assert(low.QuestMarkerOf("ww2_pell").Kind == QuestMarker.AvailableLater || low.QuestMarkerOf("ww2_pell").Kind == QuestMarker.None, "not offered below level 10");

            Talk(s, "ww2_pell");
            SessionTest.Pick(s, "What company?");
            SessionTest.Pick(s, "Lead the way");
            SessionTest.Finish(s);
            Assert(s.World.Quests.GetStage(Watch) == "ford", "the patrol starts at the ford");
            var atFord = VisibleNpc(s, "ww2_pell");
            Assert(atFord != null && Vec2.Distance(atFord.pos, new Vec2(62f, 26.8f)) < 5f, "Pell waits at the Heron Ford");

            Talk(s, "ww2_pell");
            SessionTest.Pick(s, "Tell me about Sir Corwin");
            SessionTest.Pick(s, "On to the Kestrel Stone");
            SessionTest.Finish(s);
            Assert(s.World.Quests.GetStage(Watch) == "stone", "on to the Kestrel Stone");
            var atStone = VisibleNpc(s, "ww2_pell");
            Assert(atStone != null && atStone.pos.y > 36f && atStone.pos.x > 85f, "Pell has gone ahead to the stone");

            // walking up to him springs the ambush
            Clear(s, "enc_ww2_kestrel_ambush");
            Assert(s.World.Quests.GetStage(Watch) == "dispatch", "ambush beaten: search the cairn");

            s.ExtraSkillCheckBonus = (id, skill) => 100;
            var r = s.InteractProp("ww2_kestrel_cairn");
            Assert(r.Ok && r.Kind == InteractKind.Dialogue, "the dispatch cairn");
            SessionTest.Pick(s, "Search the whole cairn");
            SessionTest.Pick(s, "Take the dispatch");
            SessionTest.Finish(s);
            Assert(s.CountItem("ww2_aubrics_dispatch") == 1 && s.World.Quests.GetStage(Watch) == "home", "the dispatch, back to the Watch");
            var home = VisibleNpc(s, "ww2_pell");
            Assert(home != null && Vec2.Distance(home.pos, new Vec2(79f, 33f)) < 3f, "Pell is back at the cold beacon");
            Assert(s.QuestMarkerOf("ww2_pell").Kind == QuestMarker.ReadyToTurnIn, "hand in at Pell (yellow ?)");

            long xp0 = TotalXp(s);
            Talk(s, "ww2_pell");
            if (s.CountItem("ww2_charcoal_heron") > 0)
            {
                SessionTest.Pick(s, "Show him the charcoal heron");
                Assert(s.CountItem("ww2_charcoal_heron") == 0, "the drawing is handed over");
            }
            SessionTest.Pick(s, "Read him every word");
            SessionTest.Pick(s, "light it together");
            SessionTest.Finish(s);
            Assert(s.Quests.IsCompleted(Watch), "The Last Watch complete");
            Assert(s.Flags.IsSet("ww2_beacon_lit") && VisibleNpc(s, "ww2_pell") == null, "the beacon burns, and Pell is at peace");
            Assert(TotalXp(s) > xp0, "quest XP");
            Assert(s.PendingQuestRewards.Contains(Watch), "a reward to choose");
        }

        [Test]
        public static void LastWatch_AKindLie_CanBeTakenBack()
        {
            var s = Party(12, 7);
            s.EnterMap(WW, "default");
            s.World.Quests.Start(Watch);
            s.World.Quests.SetStage(Watch, "home");
            s.GiveItem("ww2_aubrics_dispatch", 1);
            Talk(s, "ww2_pell");
            SessionTest.Pick(s, "They're coming home");
            SessionTest.Finish(s);
            Assert(s.Quests.IsCompleted(Watch) && s.Flags.IsSet("ww2_pell_lied"), "the lie also ends the quest");
            Assert(VisibleNpc(s, "ww2_pell") != null, "and Pell keeps waiting by his cold fire");
            Talk(s, "ww2_pell");
            SessionTest.Pick(s, "I lied to you");
            SessionTest.Pick(s, "Of course");
            SessionTest.Finish(s);
            Assert(s.Flags.IsSet("ww2_pell_rest") && !s.Flags.IsSet("ww2_pell_lied") && VisibleNpc(s, "ww2_pell") == null, "the truth at last lets him go");
        }

        // ------------------------------------------------------------------ The Glow Under the Moss (and Mossdeep)

        [Test]
        public static void KingsGlow_GladeGrottoAndTheMossKing()
        {
            var s = Party(14, 11);
            s.EnterMap(WW, "default");
            PacifyExcept(s, "enc_ww2_glade_webs");
            long xp0 = TotalXp(s);
            Assert(s.QuestMarkerOf("ww2_sprig").Kind == QuestMarker.Available, "Sprig has a quest (yellow !)");
            Talk(s, "ww2_sprig");
            SessionTest.Pick(s, "What spiders?");
            SessionTest.Pick(s, "I'll clear the glade");
            SessionTest.Finish(s);
            Assert(s.World.Quests.GetStage(Glow) == "webs", "clear the glade");
            Clear(s, "enc_ww2_glade_webs");
            Assert(s.World.Quests.GetStage(Glow) == "way", "then ask Sprig the way");
            Assert(s.QuestMarkerOf("ww2_sprig").Kind == QuestMarker.ReadyToTurnIn, "Sprig shows the way (yellow ?)");
            Talk(s, "ww2_sprig");
            SessionTest.Pick(s, "Show me the way under the moss");
            SessionTest.Finish(s);
            Assert(s.Flags.IsSet("found_mossdeep") && s.World.Quests.GetStage(Glow) == "king", "Sprig reveals Mossdeep");

            Assert(s.UseTransition("to_dgn_mossdeep").Ok && s.MapId == DG, "down into the grotto");
            foreach (var enc in new[] { "enc_dg2_glowlings", "enc_dg2_web_gallery", "enc_dg2_pool_tenders", "enc_dg2_silk_hall", "enc_dg2_kings_guard" })
                Clear(s, enc);
            foreach (var c in new[] { "chest_dg2_cocoon", "chest_dg2_pool_cache" })
            {
                s.ExtraSkillCheckBonus = (id, skill) => 100;
                var r = s.OpenChest(c);
                if (r.Kind == InteractKind.Locked) s.TryUnlockChest(c);
                if (s.PendingLoot != null) s.TakeAllLoot();
                Assert(s.Map.IsChestOpened(c), $"{c} opened");
            }
            // the King: talk to him first, through Sprig's song
            Clear(s, "enc_dg2_mossking", "Sprig sent us");
            Assert(s.Flags.IsSet("dg2_king_defeated") && s.Flags.IsSet("dg2_king_soothed"), "the King is freed (and heard us)");
            Assert(s.World.Quests.GetStage(Glow) == "return", "back to Sprig");
            Assert(VisibleNpc(s, "dg2_umbercap") != null, "Umbercap, small again, on his throne");
            var hoard = s.OpenChest("chest_dg2_kings_hoard");
            Assert(hoard.Ok, "the King's hoard opens once he is freed");
            if (s.PendingLoot != null) s.TakeAllLoot();
            Talk(s, "dg2_umbercap");
            SessionTest.Pick(s, "What was that grey ember");
            SessionTest.Pick(s, "Go home safe");
            SessionTest.Finish(s);
            Assert(s.CountItem("dg2_ashen_seed") == 1 && s.CountItem("dg2_glowcap_lantern") == 1, "the Ashen Seed, and the King's thanks");

            Assert(s.UseTransition("to_whisperwood").Ok && s.MapId == WW, "back to the old forest");
            Talk(s, "ww2_sprig");
            SessionTest.Pick(s, "He talked to us");
            SessionTest.Finish(s);
            Assert(s.Quests.IsCompleted(Glow), "The Glow Under the Moss complete");
            long gained = TotalXp(s) - xp0;
            Assert(gained >= Progression.XpToNextLevel(Db, 14) * 6 / 10, $"the chain and the dungeon are worth most of a level at 14 ({gained} XP)");
        }

        /// <summary>Walks up to the throne and answers the King's challenge with "(Attack.)": the fight starts (not resolved).</summary>
        static void PullTheKing(GameSession s, EncounterDef enc)
        {
            for (int leg = 0; leg < 12 && s.Battle == null; leg++)
            {
                if (s.Mode == SessionMode.Dialogue) { SessionTest.Pick(s, "(Attack.)"); SessionTest.Finish(s); continue; }
                s.MoveLeader(enc.pos);
            }
        }

        [Test]
        public static void MossKing_FittingPartyWins_LoneHeroDoesNot()
        {
            int wins = 0, close = 0;
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var s = Party(14, 100 + seed);
                s.EnterMap(DG, "default");
                PacifyExcept(s, "enc_dg2_mossking");
                var enc = s.Map.FindEncounter("enc_dg2_mossking");
                PullTheKing(s, enc);
                Assert(s.Battle != null, "the King's fight starts");
                // round by round: how low the party's health goes
                float worstParty = 1f, worstMember = 1f;
                bool someoneFell = false;
                var outcome = BattleOutcome.None;
                for (int round = 1; round <= 120 && !s.Battle.IsOver; round++)
                {
                    outcome = s.AutoResolve(round);
                    float hp = s.Party.Sum(u => Math.Max(0f, u.Health)), max = s.Party.Sum(u => u.MaxHealth);
                    worstParty = Math.Min(worstParty, max > 0 ? hp / max : 1f);
                    worstMember = Math.Min(worstMember, s.Party.Min(u => u.MaxHealth > 0 ? Math.Max(0f, u.Health) / u.MaxHealth : 1f));
                    someoneFell |= s.Party.Any(u => !u.IsAlive);
                }
                outcome = s.Battle.Outcome;
                Console.WriteLine($"    Moss King seed {seed}: {outcome} in {s.Battle.Round} rounds, party health low {worstParty:P0}, lowest member {worstMember:P0}, someone fell {someoneFell}");
                if (outcome == BattleOutcome.Victory) wins++;
                if (outcome != BattleOutcome.Victory || worstParty < 0.8f || worstMember < 0.55f || someoneFell) close++;
            }
            Assert(wins >= 2, $"a fitting party of five beats the Moss King ({wins}/3)");
            Assert(close >= 2, $"but it is not trivial ({close}/3 hard fights)");

            var solo = SessionTest.NewGame(ClassId.Paladin, 14, 77);
            solo.EnterMap(DG, "default");
            PacifyExcept(solo, "enc_dg2_mossking");
            var e2 = solo.Map.FindEncounter("enc_dg2_mossking");
            PullTheKing(solo, e2);
            Assert(solo.Battle != null, "the solo fight starts");
            Assert(solo.AutoResolve(120) != BattleOutcome.Victory, "a lone hero does not beat the Moss King");
        }

        // ------------------------------------------------------------------ data rules

        [Test]
        public static void Data_BossLoot_CreatureRules_AndMarkers()
        {
            var king = Db.Creature("cr_dg2_mossking");
            Assert(king.rank == CreatureRank.Boss && king.sprite == "cr_mossling_king" && king.lootTable == "lt_dg2_mossking", "the Moss King");
            var lt = Db.LootTables["lt_dg2_mossking"];
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance >= 100f && e.pool.All(id => Db.Item(id).quality == Quality.Rare)), "a guaranteed Rare");
            Assert(lt.entries.Any(e => e.pool.Length > 0 && e.chance < 100f && e.pool.All(id => Db.Item(id).quality == Quality.Epic)), "an Epic chance");
            foreach (var q in new[] { Watch, Glow })
            {
                var def = Db.Quests[q];
                Assert(def.minLevel >= 10 && def.zone == WW && Db.Npcs.ContainsKey(def.giver), $"{q}: gated, zoned, real giver");
            }
            foreach (var c in Db.Creatures.Values.Where(c => c.id.StartsWith("cr_ww2_") || c.id.StartsWith("cr_dg2_")))
            {
                Assert(c.scaleToParty && c.levelFloor > 0 && c.levelCap >= c.levelFloor, $"{c.id}: floor and cap");
                Assert(!string.IsNullOrEmpty(c.material) && !string.IsNullOrEmpty(c.voice), $"{c.id}: material and voice");
            }
        }
    }
}
