// The expansion contract (Docs/Expansion.md §2, §13): new data fields keep today's behaviour, the new validator rules
// catch the errors they target, and the small Core logic of the contract commit works (XP rate by level, creature level
// floor/cap, Perception, hidden transitions and flagged props, the mq2 story stubs).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsExpansionContract
    {
        // ------------------------------------------------------------------ validator rules (negative tests)

        /// <summary>One extra data file with one mistake per rule, loaded on top of the real data.</summary>
        const string BadBundle = @"{
  ""items"": [
    {""id"": ""xt_legend"", ""name"": ""X"", ""kind"": ""Weapon"", ""quality"": ""Legendary"", ""equip"": ""MainHand"",
     ""weaponType"": ""OneHandSword"", ""minDamage"": 1, ""maxDamage"": 2, ""speed"": 2},
    {""id"": ""xt_badfx"", ""name"": ""X"", ""kind"": ""Armor"", ""equip"": ""Head"", ""armorType"": ""Cloth"", ""equipEffects"": [{""type"": ""Bogus""}]},
    {""id"": ""xt_a"", ""name"": ""X"", ""kind"": ""Armor"", ""equip"": ""Head"", ""armorType"": ""Cloth""},
    {""id"": ""xt_b"", ""name"": ""X"", ""kind"": ""Armor"", ""equip"": ""Chest"", ""armorType"": ""Cloth""},
    {""id"": ""xt_c"", ""name"": ""X"", ""kind"": ""Armor"", ""equip"": ""Legs"", ""armorType"": ""Cloth""},
    {""id"": ""xt_d"", ""name"": ""X"", ""kind"": ""Armor"", ""equip"": ""Feet"", ""armorType"": ""Cloth""}
  ],
  ""itemSets"": [
    {""id"": ""set_x1"", ""name"": ""Set One"", ""items"": [""xt_a"", ""xt_b""], ""bonuses"": [{""pieces"": 2}]},
    {""id"": ""set_x2"", ""name"": """", ""items"": [""xt_a"", ""xt_c"", ""potion_minor_healing""],
     ""bonuses"": [{""pieces"": 3}, {""pieces"": 2}, {""pieces"": 5, ""equipEffects"": [{""type"": ""GrantAbility"", ""ability"": ""attack""}]}]},
    {""id"": ""set_x3"", ""name"": ""Set Three"", ""items"": [""xt_d"", ""potion_minor_healing""]}
  ],
  ""lootTables"": [
    {""id"": ""lt_xt"", ""entries"": [
      {""random"": true, ""quality"": ""Epic""},
      {""item"": ""xt_a"", ""pool"": [""xt_b""]},
      {""pool"": [""xt_b"", ""xt_nope""], ""weights"": [1]},
      {""item"": ""xt_b"", ""perMembers"": -1}
    ]}
  ],
  ""creatures"": [
    {""id"": ""cr_xt"", ""name"": ""X"", ""scaleToParty"": true, ""levelFloor"": 20, ""levelCap"": 15, ""material"": ""jelly"", ""voice"": ""kazoo""}
  ],
  ""quests"": [
    {""id"": ""xq_talk"", ""name"": ""X"", ""giver"": ""elder_maru"",
     ""stages"": [{""id"": ""a"", ""objectives"": [{""type"": ""Talk"", ""target"": ""puddlecap""}]}]},
    {""id"": ""xq_flag"", ""name"": ""X"", ""giver"": ""xt_nobody"", ""minLevel"": 20, ""zone"": ""xt_nowhere"",
     ""stages"": [
       {""id"": ""a"", ""objectives"": [{""type"": ""Flag"", ""target"": ""xt_never_set""}], ""turnIn"": ""xt_ghost"", ""next"": ""b""},
       {""id"": ""b"", ""objectives"": [{""type"": ""Flag"", ""target"": ""xt_never_set""}]}
     ]}
  ],
  ""dialogues"": [
    {""id"": ""dlg_xt"", ""start"": ""n"", ""nodes"": [
      {""id"": ""n"", ""text"": ""x"", ""choices"": [
        {""text"": ""xt ungated"", ""outcomes"": [{""type"": ""StartQuest"", ""key"": ""xq_flag""}]},
        {""text"": ""xt gated"", ""conditions"": [{""type"": ""Level"", ""amount"": 20}], ""outcomes"": [{""type"": ""StartQuest"", ""key"": ""xq_flag""}]}
      ]}
    ]}
  ],
  ""maps"": [
    {""id"": ""xt_map_a"", ""width"": 20, ""depth"": 20, ""biome"": ""moon"", ""environment"": ""space"", ""raidSize"": 12,
     ""spawns"": [{""id"": ""default"", ""pos"": [2, 2]}],
     ""transitions"": [
       {""id"": ""xt_hidden"", ""pos"": [10, 10], ""targetMap"": ""xt_map_a"", ""targetSpawn"": ""default"", ""hidden"": true, ""marker"": ""trapdoor""},
       {""id"": ""xt_dup"", ""pos"": [5, 5], ""targetMap"": ""xt_map_a"", ""targetSpawn"": ""default""}
     ],
     ""regions"": [{""id"": ""xt_dup"", ""pos"": [5, 5], ""size"": [2, 2], ""check"": {""skill"": ""Perception"", ""dc"": 12}}],
     ""props"": [{""art"": ""prop_rock"", ""pos"": [3, 3], ""interact"": ""xt_p1"", ""dialogue"": ""xt_dlg_missing""}],
     ""paths"": [{""points"": [[1, 1]]}],
     ""water"": [{""points"": [[1, 1], [2, 2]], ""closed"": true}, {""points"": [[1, 1]]}],
     ""encounters"": [{""id"": ""enc_training_dummy"", ""pos"": [8, 8]}]
    },
    {""id"": ""xt_raid"", ""width"": 20, ""depth"": 20, ""raidSize"": 10, ""raidReturnMap"": ""lanternvale"", ""raidReturnSpawn"": ""xt_nowhere"",
     ""spawns"": [{""id"": ""default"", ""pos"": [2, 2]}]},
    {""id"": ""xt_raid2"", ""width"": 20, ""depth"": 20, ""raidSize"": 10, ""raidReturnMap"": ""xt_nowhere"", ""raidReturnSpawn"": ""default"",
     ""spawns"": [{""id"": ""default"", ""pos"": [2, 2]}]}
  ]
}";

        static List<string> ValidateWithBadBundle()
        {
            var files = ReadDataFiles(DataDir).ToList();
            files.Add(new KeyValuePair<string, string>("zz_expansion_contract_bad.json", BadBundle));
            var db = GameDatabase.Load(files);
            Assert(db.Problems.Count == 0, "the bad bundle parses (no unknown keys): " + string.Join("; ", db.Problems));
            return DataValidator.Validate(db);
        }

        static void Expect(List<string> problems, string where, string text)
        {
            Assert(problems.Any(p => p.StartsWith(where, StringComparison.Ordinal) && p.Contains(text)),
                $"expected a problem '{where}: …{text}…'\n  got:\n    " + string.Join("\n    ", problems));
        }

        [Test]
        public static void Validator_CatchesEveryExpansionMistake()
        {
            var p = ValidateWithBadBundle();
            // every problem comes from the bad bundle: today's data plus the contract stubs stay clean
            foreach (var x in p) Assert(x.Contains("xt") || x.Contains("xq") || x.Contains("set_x"), "unexpected problem in real data: " + x);

            // quests
            Expect(p, "quest xq_flag", "giver 'xt_nobody' is not a known npc or companion");
            Expect(p, "quest xq_talk", "talk target 'puddlecap' has no dialogue");
            Expect(p, "quest xq_talk", "nothing starts it");
            Expect(p, "quest xq_flag", "stage b: nothing sets flag 'xt_never_set'");
            Expect(p, "quest xq_flag", "stage a: turnIn 'xt_ghost' is not a known npc or companion");
            Expect(p, "quest xq_flag", "zone 'xt_nowhere' is not a known map");
            Assert(!p.Any(x => x.StartsWith("quest xq_flag") && x.Contains("stage a: nothing sets")), "a turnIn excuses a Flag stage without a setter");
            Assert(!p.Any(x => x.StartsWith("quest xq_flag") && x.Contains("nothing starts it")), "dlg_xt starts xq_flag");
            Expect(p, "dialogue dlg_xt.n choice 'xt ungated'", "StartQuest xq_flag needs a Level >= 20 condition");
            Assert(!p.Any(x => x.Contains("choice 'xt gated'")), "a Level-gated StartQuest choice passes");

            // maps
            Expect(p, "map xt_map_a", "transition xt_hidden: hidden needs a revealFlag");
            Expect(p, "map xt_map_a", "transition xt_hidden: unknown marker 'trapdoor'");
            Expect(p, "map xt_map_a", "unknown biome 'moon'");
            Expect(p, "map xt_map_a", "unknown environment 'space'");
            Expect(p, "map xt_map_a", "raidSize 12 must be in [0, 10]");
            Expect(p, "map xt_raid", "raidReturnMap 'lanternvale' has no spawn 'xt_nowhere'");
            Expect(p, "map xt_map_a", "paths[0] needs at least 2 points");
            Expect(p, "map xt_map_a", "water[0] needs at least 3 points (closed)");
            Expect(p, "map xt_map_a", "water[1] needs at least 2 points");
            Expect(p, "map xt_raid2", "raid needs a valid raidReturnMap (got 'xt_nowhere')");
            Assert(!p.Any(x => x.StartsWith("map xt_raid:") && x.Contains("raid needs a valid raidReturnMap")), "a known raidReturnMap passes the map check");
            Expect(p, "map xt_map_a", "region xt_dup: a check needs a checkFlag");
            Expect(p, "map xt_map_a", "unknown dialogue 'xt_dlg_missing'");
            Expect(p, "map xt_map_a", "id 'xt_dup' is used by a transition and a region");
            Expect(p, "map xt_map_a", "encounter id 'enc_training_dummy' is also used on map 'lanternvale'");

            // creatures and loot
            Expect(p, "creature cr_xt", "levelFloor 20 > levelCap 15");
            Expect(p, "creature cr_xt", "unknown material 'jelly'");
            Expect(p, "creature cr_xt", "unknown voice 'kazoo'");
            Expect(p, "loot lt_xt", "random Epic drops are not allowed");
            Expect(p, "loot lt_xt", "both item 'xt_a' and a pool");
            Expect(p, "loot lt_xt", "pool: unknown item 'xt_nope'");
            Expect(p, "loot lt_xt", "weights has 1 entries but the pool has 2");
            Expect(p, "loot lt_xt", "perMembers must be >= 0");

            // items and sets
            Expect(p, "item xt_legend", "a Legendary item must be unique");
            Expect(p, "item xt_badfx", "unknown equipEffects type 'Bogus'");
            Expect(p, "item set set_x2", "item 'xt_a' already belongs to set 'set_x1'");
            Expect(p, "item set set_x2", "missing name");
            Expect(p, "item set set_x2", "item 'potion_minor_healing' is not equipable");
            Expect(p, "item set set_x2.bonuses[1]", "pieces must be strictly increasing");
            Expect(p, "item set set_x2.bonuses[2]", "pieces 5 must be in [1, 3]");
            Expect(p, "item set set_x2.bonuses[2]", "effect type 'GrantAbility' is not allowed in a set bonus");
            Expect(p, "item set set_x3", "needs at least 2 equipable items");
            Expect(p, "item set set_x3", "item 'potion_minor_healing' is not equipable");
            Assert(!p.Any(x => x.StartsWith("item set set_x2") && x.Contains("needs at least 2 equipable")), "a set with 2 equipable items meets the minimum");
            Assert(!p.Any(x => x.StartsWith("item set set_x1")), "a valid set passes");
        }

        // ------------------------------------------------------------------ defaults keep today's behaviour

        [Test]
        public static void NewFields_DefaultToTodaysBehaviour()
        {
            var m = new MapDef();
            Assert(m.biome == "" && m.environment == "" && m.fill == 0f && m.raidSize == 0 && !m.dungeon && m.paths.Count == 0 && m.water.Count == 0,
                "MapDef defaults: keyword biome, outdoor, no fill, not a raid or dungeon");
            var t = new TransitionDef();
            Assert(!t.hidden && t.revealFlag == "" && t.marker == "", "TransitionDef defaults: visible, auto marker");
            var c = new CreatureDef();
            Assert(c.levelFloor == 0 && c.levelCap == 0 && c.material == "" && c.voice == "", "CreatureDef defaults");
            var l = new LootEntryDef();
            Assert(l.pool.Length == 0 && l.weights.Length == 0 && !l.skipOwned && l.perMembers == 0 && !l.partyUsable, "LootEntryDef defaults");
            var q = new QuestDef();
            Assert(q.minLevel == 0 && q.zone == "" && new QuestStageDef().turnIn == "", "QuestDef defaults");
            var cfg = new GameConfigDef();
            Assert(cfg.maxRaidSize == 10 && cfg.xpRateByLevel.Count == 0, "GameConfigDef defaults");
            Assert(new WaterDef().blocksMovement && new WaterDef().halfWidth == 1.5f && new PathDef().art == "decal_path_dirt" && new PathDef().width == 2.2f, "path/water defaults");
            Assert(new SetBonusDef().pieces == 2 && new RegionDef().check == null, "set bonus / region defaults");

            // the shipped config of the expansion
            Assert(Db.Config.partySize == 5 && Db.Config.maxRaidSize == 10, "config: party of 5, raids of 10");
            Assert(Db.Config.xpRateByLevel.Count == 6, "config: xpRateByLevel");
        }

        // ------------------------------------------------------------------ XP rate by level

        [Test]
        public static void XpRateByLevel_InterpolatesLinearly()
        {
            var db = new GameDatabase();
            db.Config.xpRate = 4f;
            Assert(Progression.XpRate(db, 1) == 4f && Progression.XpRate(db, 40) == 4f, "empty list: config.xpRate (old behaviour)");
            Assert(Progression.QuestXp(db, 100) == 400 && Progression.QuestXp(db, 100, 30) == 400, "empty list: quest XP uses xpRate");

            db.Config.xpRateByLevel = new List<XpRatePoint>
            {
                new XpRatePoint { level = 1, rate = 4 }, new XpRatePoint { level = 12, rate = 4 }, new XpRatePoint { level = 18, rate = 6 },
                new XpRatePoint { level = 24, rate = 8 }, new XpRatePoint { level = 30, rate = 9 }, new XpRatePoint { level = 60, rate = 9 },
            };
            AssertNear(Progression.XpRate(db, 1), 4f, 1e-4f, "level 1");
            AssertNear(Progression.XpRate(db, 7), 4f, 1e-4f, "level 7 (1-12 unchanged)");
            AssertNear(Progression.XpRate(db, 12), 4f, 1e-4f, "level 12");
            AssertNear(Progression.XpRate(db, 15), 5f, 1e-4f, "level 15 (between 12 and 18)");
            AssertNear(Progression.XpRate(db, 18), 6f, 1e-4f, "level 18");
            AssertNear(Progression.XpRate(db, 21), 7f, 1e-4f, "level 21");
            AssertNear(Progression.XpRate(db, 27), 8.5f, 1e-4f, "level 27");
            AssertNear(Progression.XpRate(db, 45), 9f, 1e-4f, "level 45");
            AssertNear(Progression.XpRate(db, 0), 4f, 1e-4f, "below the first point: clamped");
            AssertNear(Progression.XpRate(db, 70), 9f, 1e-4f, "above the last point: clamped");
            Assert(Progression.QuestXp(db, 100, 18) == 600 && Progression.QuestXp(db, 100, 5) == 400, "quest XP by level");

            // unsorted points interpolate the same way
            db.Config.xpRateByLevel.Reverse();
            AssertNear(Progression.XpRate(db, 15), 5f, 1e-4f, "unsorted list");

            // the real data: the 1-12 slice is unchanged, kill XP scales with the receiving character's level
            Assert(Progression.XpRate(Db, 1) == Db.Config.xpRate && Progression.XpRate(Db, 12) == Db.Config.xpRate, "1-12 slice unchanged");
            var mob = UnitFactory.CreateCreature(Db, Db.Creature("cr_wolf"), 18);
            int k18 = Progression.KillXp(Db, 18, mob);
            int expected = (int)Math.Round(Formulas.MobXp(18, 18) * Progression.XpRankMult(mob.Creature.rank) * mob.Creature.xpMult * 6f);
            Assert(k18 == expected, $"kill XP at level 18 uses rate 6 ({k18} vs {expected})");

            // the session's dialogue/quest XP uses the main character's level
            var s = SessionTest.NewGame(ClassId.Mage, 18, seed: 41);
            int before = s.Main.Xp;
            s.GiveXP(50);
            Assert(s.Main.Xp == before + 300, $"GiveXP(50) at level 18 = 300 ({s.Main.Xp - before})");
        }

        // ------------------------------------------------------------------ creature level floor and cap

        [Test]
        public static void CreatureLevel_ClampsToFloorAndCap()
        {
            var c = new CreatureDef { scaleToParty = true, levelOffset = 1, levelFloor = 12, levelCap = 18 };
            Assert(UnitFactory.CreatureLevel(c, 0, 5, null) == 12, "below the band: floor");
            Assert(UnitFactory.CreatureLevel(c, 0, 15, null) == 16, "inside the band: party level + offset");
            Assert(UnitFactory.CreatureLevel(c, 0, 30, null) == 18, "above the band: cap");
            Assert(UnitFactory.CreatureLevel(c, 40, 5, null) == 40, "an explicit encounter level wins");
            var open = new CreatureDef { scaleToParty = true, levelOffset = -3 };
            Assert(UnitFactory.CreatureLevel(open, 0, 2, null) == 1 && UnitFactory.CreatureLevel(open, 0, 70, null) == 63, "no floor/cap: [1, 63] as before");
            var floorOnly = new CreatureDef { scaleToParty = true, levelFloor = 20 };
            Assert(UnitFactory.CreatureLevel(floorOnly, 0, 10, null) == 20 && UnitFactory.CreatureLevel(floorOnly, 0, 40, null) == 40, "floor only");
            var fixedDef = new CreatureDef { levelMin = 7, levelMax = 7, levelFloor = 30 };
            Assert(UnitFactory.CreatureLevel(fixedDef, 0, 2, null) == 7, "floor/cap only apply to scaleToParty");
            // every shipped creature keeps its old level (no floor/cap yet in the 1-12 data)
            foreach (var cr in Db.Creatures.Values.Where(x => x.scaleToParty && x.levelFloor == 0 && x.levelCap == 0))
                Assert(UnitFactory.CreatureLevel(cr, 0, 10, null) == MathUtil.Clamp(10 + cr.levelOffset, 1, 63), cr.id);
        }

        // ------------------------------------------------------------------ Perception

        [Test]
        public static void Perception_IsSpiritAndHunterRogueProficient()
        {
            Assert(SkillChecks.StatFor(SkillCheck.Perception) == StatKind.Spirit, "Perception uses Spirit");
            foreach (ClassId c in Enum.GetValues(typeof(ClassId)))
            {
                bool want = c == ClassId.Hunter || c == ClassId.Rogue;
                Assert(SkillChecks.IsProficient(c, SkillCheck.Perception) == want, $"{c} Perception proficiency {want}");
            }
            // older skills keep their proficiencies
            Assert(SkillChecks.IsProficient(ClassId.Hunter, SkillCheck.Survival) && SkillChecks.IsProficient(ClassId.Rogue, SkillCheck.Stealth), "existing proficiencies");
        }

        // ------------------------------------------------------------------ hidden transitions and flagged props

        [Test]
        public static void HiddenTransitions_StayInvisibleUntilRevealed()
        {
            var flags = new FlagStore();
            var lv = new MapRuntime(Db.Maps["lanternvale"], flags);
            var hidden = lv.Def.transitions.Find(t => t.id == "to_dgn_root_hollows");
            Assert(hidden != null && hidden.hidden && hidden.revealFlag == "found_root_hollows", "LV has the hidden Root Hollows entrance");
            Assert(!lv.IsTransitionVisible(hidden) && lv.TransitionAt(hidden.pos) == null, "TransitionAt skips the hidden transition");
            var west = lv.Def.transitions.Find(t => t.id == "to_amberfield");
            Assert(lv.IsTransitionVisible(west) && lv.TransitionAt(west.pos) == west, "plain transitions are visible");
            flags.Set("found_root_hollows", 1);
            Assert(lv.IsTransitionVisible(hidden) && lv.TransitionAt(hidden.pos) == hidden, "revealed by its flag");
            Assert(!lv.IsTransitionVisible(new TransitionDef { hidden = true }), "hidden without a revealFlag never shows");

            var prop = new PropDef { requireFlag = "xt_shown", hideFlag = "xt_gone" };
            Assert(!lv.IsPropVisible(prop), "requireFlag unmet: hidden");
            flags.Set("xt_shown", 1);
            Assert(lv.IsPropVisible(prop), "requireFlag met: visible");
            flags.Set("xt_gone", 1);
            Assert(!lv.IsPropVisible(prop) && lv.IsPropVisible(new PropDef()), "hideFlag hides; plain props are visible");

            // the session: a hidden exit cannot be used until revealed, then leads into the dungeon and back
            var s = SessionTest.NewGame(ClassId.Rogue, 12, seed: 77);
            var r = s.UseTransition("to_dgn_root_hollows");
            Assert(!r.Ok && s.MapId == "lanternvale", "hidden: 'There is no way through here.'");
            s.Flags.Set("found_root_hollows", 1);
            r = s.UseTransition("to_dgn_root_hollows");
            Assert(r.Ok && s.MapId == "dgn_root_hollows", "revealed: travel into the Root Hollows");
            r = s.UseTransition("to_lanternvale");
            // the spawn beside the entrance (the lv builder placed both among Kusu's northern roots; look them up)
            var besideEntrance = Db.Maps["lanternvale"].spawns.First(x => x.id == "from_dgn_root_hollows").pos;
            Assert(Vec2.Distance(besideEntrance, hidden.pos) < 4f, "from_dgn_root_hollows stands beside the entrance");
            Assert(r.Ok && s.MapId == "lanternvale" && Vec2.Distance(s.Leader.Position, besideEntrance) < 3f, "back out beside the entrance");
        }

        // ------------------------------------------------------------------ topology of §1

        [Test]
        public static void Topology_EveryLinkAndSpawnExists()
        {
            string[] maps = { "lanternvale", "whisperwood", "shrine", "dgn_root_hollows", "dgn_mossdeep", "dgn_lantern_catacombs", "amberfield",
                              "dgn_barrow", "brightwater", "mirefen", "dgn_drowned_vault", "raid_hollow_heart", "skyreach", "dgn_frozen_sanctum", "raid_ashwyrm_roost" };
            foreach (var id in maps) Assert(Db.Maps.ContainsKey(id), "map " + id);
            Assert(Db.Maps["lanternvale"].depth == 44 && Db.Maps["whisperwood"].depth == 46 && Db.Maps["shrine"].depth == 40, "the deepened maps");
            var slice = new HashSet<string> { "lanternvale", "whisperwood", "shrine" };
            foreach (var m in Db.Maps.Values)
                foreach (var t in m.transitions)
                {
                    // every link has its way back; expansion links arrive at from_<source map> (the slice keeps its old names)
                    var target = Db.Maps[t.targetMap];
                    Assert(target.transitions.Any(b => b.targetMap == m.id), $"{t.targetMap} links back to {m.id}");
                    if (slice.Contains(m.id) && slice.Contains(t.targetMap)) continue;
                    Assert(t.id == "to_" + t.targetMap && t.targetSpawn == "from_" + m.id, $"{m.id}.{t.id} → {t.targetMap}/{t.targetSpawn} follows to_<map> → from_<map>");
                }
            Assert(Db.Maps["raid_hollow_heart"].raidSize == 10 && Db.Maps["raid_ashwyrm_roost"].raidSize == 10, "raids of 10");
            foreach (var id in new[] { "bruna", "ysolde", "liora", "nanami" })
                Assert(Db.Maps.Values.Any(m => m.npcs.Any(n => n.npc == id && n.hideFlag == "recruited_" + id)), $"{id} is placed (hidden once recruited)");
        }

        // ------------------------------------------------------------------ the mq2 story stubs

        [Test]
        public static void EmberRoad_OfferedOnlyAfterTheLanterns_AndChainsToPenhallow()
        {
            const string offer = "Ash, drifting down from the north";
            var s = SessionTest.NewGame(ClassId.Paladin, 12, seed: 91);
            bool Offered()
            {
                Assert(s.StartDialogue("dlg_elder_maru", "elder_maru"), "talk to the Elder");
                SessionTest.SkipText(s);
                bool seen = SessionTest.ChoiceIndex(s.Dialogue.Current, offer) >= 0;
                s.Dialogue.End();
                return seen;
            }
            Assert(!Offered(), "not offered before The Lanterns Go Dark starts");
            s.Quests.Start("mq_lanterns");
            Assert(!Offered(), "not offered while it is active");
            s.Quests.Complete("mq_lanterns");
            Assert(s.StartDialogue("dlg_elder_maru", "elder_maru"), "talk to the Elder");
            SessionTest.Pick(s, offer);
            SessionTest.Finish(s, "west road");
            Assert(s.Quests.IsActive("mq2_ember_road") && s.Quests.GetStage("mq2_ember_road") == "road", "The Ember Road starts at 'road'");
            Assert(!Offered(), "offered once");

            s.EnterMap("amberfield", "from_lanternvale");
            // the amberfield builder adds the Amberfield stages between 'road' and 'archivist' (TestsContentAm plays them)
            Assert(s.Quests.IsActive("mq2_ember_road") && s.Quests.GetStage("mq2_ember_road") != "road", "reaching Amberfield advances past 'road'");
            Assert(s.Quests.SetStage("mq2_ember_road", "archivist"), "skip to the last stage, 'archivist'");
            s.EnterMap("brightwater", "from_amberfield");
            Assert(s.VisibleNpcs().Any(n => n.npc == "bw_archivist_penhallow"), "Penhallow stands in Brightwater");
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow");
            SessionTest.Pick(s, "Elder Maru sent me");
            SessionTest.Finish(s);
            Assert(s.Quests.IsCompleted("mq2_ember_road") && s.Flags.IsSet("mq2_ember_road_done"), "complete; sets mq2_ember_road_done");

            // the Mirefen offer waits for level 18
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow again");
            Assert(SessionTest.ChoiceIndex(s.Dialogue.Current, "about the fens") < 0, "no Mirefen offer below level 18");
            s.Dialogue.End();
            while (s.Main.Level < 18) s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
            Assert(s.StartDialogue("dlg_bw_penhallow", "bw_archivist_penhallow"), "talk to Penhallow at 18");
            SessionTest.Pick(s, "about the fens");
            SessionTest.Finish(s);
            Assert(s.Quests.IsActive("mq2_drowned_lanterns"), "The Drowned Lanterns starts at 18");
        }

        // ------------------------------------------------------------------ saves stay compatible

        [Test]
        public static void OldSaves_WithoutTheNewFields_Load()
        {
            var s = SessionTest.NewGame(ClassId.Hunter, 12, seed: 5);
            var json = s.SaveGame();
            Assert(json.Contains("\"checkedRegions\""), "the new map state is saved");
            // a save written before the expansion: no checkedRegions, no raid
            var old = Regex.Replace(json, "\"checkedRegions\"\\s*:\\s*\\[[^\\]]*\\]\\s*,?", "");
            old = Regex.Replace(old, "\"raid\"\\s*:\\s*null\\s*,?", "");
            old = Regex.Replace(old, ",(\\s*[}\\]])", "$1");
            Assert(!old.Contains("checkedRegions"), "stripped");
            var s2 = new GameSession(Db, 1);
            Assert(s2.LoadGame(old, out var err), "an old save loads: " + err);
            Assert(s2.Map.State.checkedRegions != null && s2.Map.State.checkedRegions.Count == 0, "checkedRegions defaults to empty");
            Assert(!s2.InRaid && s2.RaidSize == 0 && s2.MaxPartySizeOn("lanternvale") == Db.Config.partySize, "not in a raid");
            Assert(s2.SaveGame() == json, "re-saved identically");
        }
    }
}
