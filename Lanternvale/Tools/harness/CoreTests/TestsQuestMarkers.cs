// Quest markers (Docs/Expansion.md §3, brief_quests §6): the dry-run walk, the static hand-in index, the marker of every
// NPC in the brief's §6.5 scenarios on the real data, the session's QuestMarkersVersion (kill, item, flag, level, load,
// never FlagsVersion), map hints and turn-ins, and the Map panel's label layout on every map.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsQuestMarkers
    {
        // ------------------------------------------------------------------ helpers

        static FakeWorldContext Ctx(int level = 5)
        {
            var c = new FakeWorldContext(Db);
            c.Members[0].level = level;
            return c;
        }

        static QuestMarkerIndex index;
        static QuestMarkerIndex Index => index ??= new QuestMarkerIndex(Db);

        static QuestMarkerInfo M(FakeWorldContext c, string npc) =>
            QuestMarkers.Best(QuestMarkers.Evaluate(Index, c, c.World.DialogueMemory, npc), npc);

        static void Expect(FakeWorldContext c, string npc, QuestMarker kind, string quest = null, string scenario = "")
        {
            var m = M(c, npc);
            Assert(m.Kind == kind && (quest == null || m.QuestId == quest),
                $"{scenario}: {npc} shows {kind}{(quest != null ? " " + quest : "")}, got {m}");
        }

        static readonly string[] VillageQuestPeople = { "elder_maru", "shepherd_bram", "child_nell", "lamplighter_tobben", "postman_fennick", "guard_holt" };

        static void Give(FakeWorldContext c, string item, int n) => c.GiveItem(item, n);

        // ------------------------------------------------------------------ dry run

        [Test]
        public static void DryRunIsReadOnly()
        {
            var c = Ctx();
            var memory = c.World.DialogueMemory;
            int flags = c.Flags.Save().flags.Count, chosen = memory.ChosenCount;
            foreach (var p in Index.People)
            {
                var d = Index.DialogueOf(p);
                if (d == null) continue;
                DialogueReach.Collect(d, c, memory, ReachMode.Normal);
                DialogueReach.Collect(d, c, memory, ReachMode.IgnoreLevel);
            }
            Assert(c.Log.Count == 0, "the walk runs no outcome: " + string.Join(", ", c.Log));
            Assert(c.Flags.Save().flags.Count == flags && memory.ChosenCount == chosen && memory.TimesStarted("dlg_elder_maru") == 0, "no flag or memory written");
            Assert(!c.Quests.KnownQuests().Any(), "no quest started");
        }

        [Test]
        public static void DryRunFollowsConditionsChecksOnceAndEndDialogue()
        {
            var d = new DialogueDef
            {
                id = "dlg_t", start = "a",
                nodes = new List<DialogueNodeDef>
                {
                    new DialogueNodeDef { id = "a", conditions = { WorldFixtures.Cond(ConditionType.Flag, "nope") }, fallback = "b" },
                    new DialogueNodeDef
                    {
                        id = "b", text = "hub",
                        choices =
                        {
                            new ChoiceDef { text = "lvl", next = "c", conditions = { WorldFixtures.Cond(ConditionType.Level, amount: 9) } },
                            new ChoiceDef { text = "roll", check = new CheckDef { skill = SkillCheck.Persuasion, dc = 10, success = "s", failure = "f" } },
                            new ChoiceDef { text = "once", once = true, outcomes = { WorldFixtures.Out(OutcomeType.SetFlag, "o") } },
                            new ChoiceDef { text = "end", next = "z", outcomes = { WorldFixtures.Out(OutcomeType.EndDialogue), WorldFixtures.Out(OutcomeType.SetFlag, "e") } },
                        },
                    },
                    new DialogueNodeDef { id = "c", text = "x", outcomes = { WorldFixtures.Out(OutcomeType.StartQuest, "q_lvl") } },
                    new DialogueNodeDef { id = "s", text = "x", outcomes = { WorldFixtures.Out(OutcomeType.SetFlag, "won") } },
                    new DialogueNodeDef { id = "f", text = "x", outcomes = { WorldFixtures.Out(OutcomeType.SetFlag, "lost"), WorldFixtures.Out(OutcomeType.EndDialogue) }, next = "z" },
                    new DialogueNodeDef { id = "z", text = "x", outcomes = { WorldFixtures.Out(OutcomeType.SetFlag, "never") } },
                },
            };
            var c = Ctx(5);
            var mem = new DialogueMemory();
            List<string> Keys(ReachMode mode) => DialogueReach.Collect(d, c, mem, mode).Select(r => r.Outcome.type + ":" + r.Outcome.key).ToList();
            var normal = Keys(ReachMode.Normal);
            Assert(normal.Contains("SetFlag:won") && normal.Contains("SetFlag:lost"), "both check branches: " + string.Join(",", normal));
            Assert(normal.Contains("SetFlag:o") && normal.Contains("SetFlag:e"), "choice outcomes collected");
            Assert(!normal.Contains("SetFlag:never"), "EndDialogue stops the path (choice and node)");
            Assert(!normal.Contains("StartQuest:q_lvl"), "a Level choice above the main level is not reachable");
            var later = DialogueReach.Collect(d, c, mem, ReachMode.IgnoreLevel).Where(r => r.Outcome.key == "q_lvl").ToList();
            Assert(later.Count == 1 && later[0].LevelGate == 9, "IgnoreLevel reaches it with its gate");
            mem.MarkChosen("dlg_t", "b", 2);
            Assert(!Keys(ReachMode.Normal).Contains("SetFlag:o"), "a used once choice is hidden");
            c.Members[0].level = 9;
            Assert(Keys(ReachMode.Normal).Contains("StartQuest:q_lvl"), "reachable at the level");
        }

        // ------------------------------------------------------------------ static index

        [Test]
        public static void StaticHandInTable()
        {
            var idx = Index;
            void H(string q, string stage, string expect)
            {
                var got = string.Join("|", idx.HandIn(q, stage));
                Assert(got == expect, $"HandIn {q}/{stage} = '{expect}', got '{got}'");
            }
            H("mq_lanterns", "elder", "elder_maru");
            H("mq_lanterns", "wayshrine", "komorebi");
            H("mq_lanterns", "komorebi", "komorebi");
            H("mq_lanterns", "embers", "komorebi");
            H("mq_lanterns", "embers_return", "komorebi");
            H("mq_lanterns", "rotheart", "warden_spirit");
            H("mq_lanterns", "shrine", "warden_spirit");
            H("mq_lanterns", "warden", "warden_spirit");
            H("mq_lanterns", "rekindle", "warden_spirit");
            H("mq_lanterns", "home", "elder_maru");
            H("sq_shepherd", "wolves", "shepherd_bram");
            H("sq_shepherd", "report", "shepherd_bram");
            H("sq_shepherd", "greymane", "shepherd_bram");
            H("sq_shepherd", "return", "shepherd_bram");
            H("sq_spirit_friend", "find", "moppet");
            H("sq_spirit_friend", "return", "child_nell");
            H("sq_wicks", "gather", "lamplighter_tobben");
            H("sq_wicks", "return", "lamplighter_tobben");
            H("sq_satchel", "find", "postman_fennick");
            H("sq_satchel", "return", "postman_fennick");
            H("sq_satchel", "deliver", "elder_maru");
            H("sq_bridge", "bandits", "guard_holt");
            H("sq_bridge", "report", "guard_holt");
            Assert(idx.DirectHandIn("sq_shepherd", "wolves").Count == 0, "a Kill stage resolves on its own (lookahead only)");
            Assert(string.Join("|", idx.DirectHandIn("sq_bridge", "bandits")) == "guard_holt",
                "the encounter dialogue's flag is nobody's, but Holt sets exactly the next stage (h_greet shortcut)");
            Assert(idx.DirectHandIn("sq_wicks", "gather").Count == 0, "a Collect stage resolves on its own (lookahead only)");
            Assert(idx.TurnInOf("sq_bridge", "bandits") == "guard_holt", "TurnInOf follows the lookahead");
            Assert(idx.HandIn("sq_shepherd", "nope").Count == 0 && idx.HandIn("nope", "x").Count == 0, "unknown quest/stage → nobody");
            Assert(idx.Starters("sq_satchel").Contains("postman_fennick") && idx.Completers("sq_satchel").Contains("elder_maru"), "starters and completers indexed");
            Assert(idx.DialogueIdOf("rook") == Db.Companions["rook"].recruitDialogue, "companions own their recruit dialogue");
        }

        [Test]
        public static void TurnInFieldOverridesInference()
        {
            var db = GameDatabase.Load(ReadDataFiles(DataDir).Concat(new[] { new KeyValuePair<string, string>("zz_marker_test.json", @"{
  ""quests"": [{""id"": ""xq_turnin"", ""name"": ""X"", ""giver"": ""elder_maru"",
    ""stages"": [{""id"": ""a"", ""objectives"": [{""type"": ""Kill"", ""target"": ""cr_wolf""}], ""turnIn"": ""child_toby"", ""next"": ""b""},
                 {""id"": ""b"", ""objectives"": [{""type"": ""Talk"", ""target"": ""shepherd_bram""}]}]}]
}") }));
            var idx = new QuestMarkerIndex(db);
            Assert(string.Join("|", idx.HandIn("xq_turnin", "a")) == "child_toby", "turnIn wins over lookahead");
            Assert(string.Join("|", idx.HandIn("xq_turnin", "b")) == "shepherd_bram", "a Talk stage hands in at its target");
        }

        // ------------------------------------------------------------------ §6.5 scenarios on the real data

        [Test]
        public static void ScenarioFreshGame()
        {
            var c = Ctx();
            string s = "fresh game, opening declined";
            Expect(c, "elder_maru", QuestMarker.Available, "mq_lanterns", s);
            Assert(M(c, "elder_maru").Main, "the main quest marker is flagged main");
            Expect(c, "shepherd_bram", QuestMarker.Available, "sq_shepherd", s);
            Expect(c, "child_nell", QuestMarker.Available, "sq_spirit_friend", s);
            Expect(c, "lamplighter_tobben", QuestMarker.Available, "sq_wicks", s);
            Expect(c, "postman_fennick", QuestMarker.Available, "sq_satchel", s);
            Expect(c, "guard_holt", QuestMarker.Available, "sq_bridge", s);
            foreach (var other in new[] { "villager_june", "child_toby", "merchant_tilly", "innkeeper_dorrit", "trainer_odo", "komorebi", "moppet", "rusk", "aldric" })
                Expect(c, other, QuestMarker.None, null, s);
        }

        [Test]
        public static void ScenarioOpeningAccepted()
        {
            var c = Ctx();
            c.Quests.Start("mq_lanterns");
            Expect(c, "elder_maru", QuestMarker.ReadyToTurnIn, "mq_lanterns", "mq at elder");
            Expect(c, "shepherd_bram", QuestMarker.Available, "sq_shepherd", "mq at elder");
        }

        [Test]
        public static void ScenarioAllAcceptedAtWayshrine()
        {
            var c = Ctx();
            foreach (var q in new[] { "mq_lanterns", "sq_shepherd", "sq_spirit_friend", "sq_wicks", "sq_satchel", "sq_bridge" }) c.Quests.Start(q);
            c.Flags.Set("met_elder", 1);
            Assert(c.Quests.GetStage("mq_lanterns") == "wayshrine", "mq at wayshrine");
            string s = "all accepted, mq at wayshrine";
            Expect(c, "moppet", QuestMarker.ReadyToTurnIn, "sq_spirit_friend", s);
            Expect(c, "shepherd_bram", QuestMarker.InProgress, "sq_shepherd", s);
            Expect(c, "lamplighter_tobben", QuestMarker.InProgress, "sq_wicks", s);
            Expect(c, "postman_fennick", QuestMarker.InProgress, "sq_satchel", s);
            Expect(c, "guard_holt", QuestMarker.InProgress, "sq_bridge", s);
            Expect(c, "komorebi", QuestMarker.InProgress, "mq_lanterns", s);
            Expect(c, "child_nell", QuestMarker.None, null, s + " (Moppet is the next hand-in)");
            Expect(c, "elder_maru", QuestMarker.None, null, s);
        }

        [Test]
        public static void ScenarioShepherd()
        {
            var c = Ctx();
            c.Quests.Start("sq_shepherd");
            Expect(c, "shepherd_bram", QuestMarker.InProgress, "sq_shepherd", "wolves");
            c.Quests.OnKill("cr_wolf", 3);
            Assert(c.Quests.GetStage("sq_shepherd") == "report", "wolves done → report");
            Expect(c, "shepherd_bram", QuestMarker.ReadyToTurnIn, "sq_shepherd", "report");
            c.Quests.SetStage("sq_shepherd", "greymane");
            Expect(c, "shepherd_bram", QuestMarker.InProgress, "sq_shepherd", "greymane");
            c.Quests.OnKill("cr_greymane");
            Expect(c, "shepherd_bram", QuestMarker.ReadyToTurnIn, "sq_shepherd", "return");
            c.Quests.Complete("sq_shepherd");
            Expect(c, "shepherd_bram", QuestMarker.None, null, "done");
        }

        [Test]
        public static void ScenarioSpiritFriend()
        {
            var c = Ctx();
            c.Quests.Start("sq_spirit_friend");
            c.Flags.Set("moppet_found", 1);
            Assert(c.Quests.GetStage("sq_spirit_friend") == "return", "moppet found → return");
            Expect(c, "child_nell", QuestMarker.ReadyToTurnIn, "sq_spirit_friend", "moppet found");
            Expect(c, "moppet", QuestMarker.None, null, "moppet found");
        }

        [Test]
        public static void ScenarioWicks()
        {
            var c = Ctx();
            c.Quests.Start("sq_wicks");
            Give(c, "lantern_wick", 5);
            Expect(c, "lamplighter_tobben", QuestMarker.InProgress, "sq_wicks", "5 wicks");
            Give(c, "lantern_wick", 1);
            Assert(c.Quests.GetStage("sq_wicks") == "return", "6 wicks → return");
            Expect(c, "lamplighter_tobben", QuestMarker.ReadyToTurnIn, "sq_wicks", "6 wicks");
            c.TakeItem("lantern_wick", 1);
            Expect(c, "lamplighter_tobben", QuestMarker.InProgress, "sq_wicks", "wicks collected, then 1 sold");
        }

        [Test]
        public static void ScenarioSatchel()
        {
            var c = Ctx();
            Give(c, "fennicks_satchel", 1);
            Expect(c, "postman_fennick", QuestMarker.Available, "sq_satchel", "satchel found before the quest (f_early)");
            c.Quests.Start("sq_satchel");
            Assert(c.Quests.GetStage("sq_satchel") == "return", "satchel already in the bags → return");
            Expect(c, "postman_fennick", QuestMarker.ReadyToTurnIn, "sq_satchel", "satchel found");
            c.Quests.SetStage("sq_satchel", "deliver");
            Give(c, "ishiro_letter", 1);
            Expect(c, "elder_maru", QuestMarker.ReadyToTurnIn, "sq_satchel", "letter to deliver (the turn-in is not the giver)");
            Expect(c, "postman_fennick", QuestMarker.None, null, "letter to deliver");
        }

        [Test]
        public static void ScenarioBridge()
        {
            var c = Ctx(9);
            c.Flags.Set("bandits_dealt_with", 1);
            Expect(c, "guard_holt", QuestMarker.Available, "sq_bridge", "bandits dealt with before the quest");
            c.Quests.Start("sq_bridge");
            Assert(c.Quests.GetStage("sq_bridge") == "report", "flag already set → report");
            Expect(c, "guard_holt", QuestMarker.ReadyToTurnIn, "sq_bridge", "bridge report");
        }

        [Test]
        public static void ScenarioMainQuestSteps()
        {
            var c = Ctx(9);
            c.Quests.SetStage("mq_lanterns", "komorebi");
            c.Flags.Set("met_elder", 1);   // the Elder's briefing came first (Komorebi's k_first needs it)
            Expect(c, "komorebi", QuestMarker.ReadyToTurnIn, "mq_lanterns", "mq komorebi");
            c.Quests.SetStage("mq_lanterns", "embers_return");
            Give(c, "spirit_ember", 3);
            Expect(c, "komorebi", QuestMarker.ReadyToTurnIn, "mq_lanterns", "mq embers_return with 3 embers");
            c.Quests.SetStage("mq_lanterns", "rekindle");
            c.Flags.Set("warden_defeated", 1);
            Expect(c, "warden_spirit", QuestMarker.ReadyToTurnIn, "mq_lanterns", "mq rekindle");
            c.Quests.SetStage("mq_lanterns", "home");
            Expect(c, "elder_maru", QuestMarker.ReadyToTurnIn, "mq_lanterns", "mq home");
            Expect(c, "komorebi", QuestMarker.None, null, "mq home");
        }

        [Test]
        public static void GreyExclamationWithinThreeLevels()
        {
            var c = Ctx(14);
            c.Flags.Set("mq2_ember_road_done", 1);
            Expect(c, "bw_archivist_penhallow", QuestMarker.None, null, "4 levels short");
            c.Members[0].level = 15;
            var m = M(c, "bw_archivist_penhallow");
            Assert(m.Kind == QuestMarker.AvailableLater && m.QuestId == "mq2_drowned_lanterns" && m.Level == 18 && m.Main && !m.Yellow && m.Glyph == "!",
                "3 levels short → grey ! (needs 18): " + m);
            c.Members[0].level = 18;
            Expect(c, "bw_archivist_penhallow", QuestMarker.Available, "mq2_drowned_lanterns", "at the level");
            // a minLevel without a Level condition still gates (data error caught by the validator, markers stay honest)
            var q = Db.Quests["mq2_drowned_lanterns"];
            Assert(q.minLevel == 18, "data: minLevel 18");
        }

        [Test]
        public static void PriorityReadyOverAvailableOverInProgress()
        {
            var c = Ctx();
            // the Elder can take the letter (ready) and still offers the main quest (available)
            c.Quests.SetStage("sq_satchel", "deliver");
            Give(c, "ishiro_letter", 1);
            var all = QuestMarkers.Evaluate(Index, c, c.World.DialogueMemory, "elder_maru");
            Assert(all.Count == 2 && all[0].Kind == QuestMarker.ReadyToTurnIn && all[1].Kind == QuestMarker.Available && all[1].QuestId == "mq_lanterns",
                "ready first, then available: " + string.Join("; ", all));
            Assert(all[0].Describe().StartsWith("Turn in: ") && all[1].Describe() == "Quest: " + Db.Quests["mq_lanterns"].name, "hover texts");
        }

        // ------------------------------------------------------------------ session: version, caching, map, hints

        [Test]
        public static void SessionVersionBumps()
        {
            var s = SessionTest.NewGame(ClassId.Warrior);
            int v = s.QuestMarkersVersion;
            int fv = s.FlagsVersion;
            Assert(s.QuestMarkerOf("shepherd_bram").Kind == QuestMarker.Available, "Bram offers at the start");
            Assert(s.QuestMarkersVersion == v, "reading markers changes nothing");

            s.World.Quests.Start("sq_shepherd");
            Assert(s.QuestMarkersVersion > v, "quest start bumps");
            Assert(s.QuestMarkerOf("shepherd_bram").Kind == QuestMarker.InProgress, "cache refreshed after the bump");
            v = s.QuestMarkersVersion;
            s.World.Quests.OnKill("cr_wolf");
            Assert(s.QuestMarkersVersion > v, "a kill bumps");
            Assert(s.FlagsVersion == fv, "quest progress never bumps FlagsVersion");

            v = s.QuestMarkersVersion;
            ((IDialogueContext)s).GiveItem("lantern_wick", 1);
            Assert(s.QuestMarkersVersion > v, "an item bumps");
            Assert(s.FlagsVersion == fv, "items never bump FlagsVersion");

            v = s.QuestMarkersVersion;
            s.Flags.Set("shepherd_quest", 1);
            Assert(s.QuestMarkersVersion > v, "a flag bumps");

            v = s.QuestMarkersVersion;
            fv = s.FlagsVersion;
            int lvl = s.PartyLevel;
            s.GiveXP(50000);
            Assert(s.PartyLevel > lvl && s.QuestMarkersVersion > v, "a level up bumps");
            Assert(s.FlagsVersion == fv, "levels never bump FlagsVersion");
        }

        [Test]
        public static void SessionMarkersRefreshOnLoad()
        {
            var s = SessionTest.NewGame(ClassId.Mage);
            string json = s.SaveGame();
            s.World.Quests.Start("sq_shepherd");
            Assert(s.QuestMarkerOf("shepherd_bram").Kind == QuestMarker.InProgress, "after accepting");
            int v = s.QuestMarkersVersion;
            Assert(s.LoadGame(json, out var err), "load: " + err);
            Assert(s.QuestMarkersVersion > v, "GameLoaded bumps (QuestLog.Load raises nothing)");
            Assert(s.QuestMarkerOf("shepherd_bram").Kind == QuestMarker.Available, "markers read the loaded state");
        }

        [Test]
        public static void SessionMarkersOnMapAndCompanions()
        {
            var s = SessionTest.NewGame(ClassId.Priest);
            var ids = s.QuestMarkersOnMap().Select(m => m.NpcId).ToList();
            foreach (var p in VillageQuestPeople) Assert(ids.Contains(p), $"{p} has a marker on the village map: {string.Join(",", ids)}");
            Assert(!ids.Contains("moppet"), "hidden NPCs (requireFlag) have no map marker");
            Assert(s.QuestMarkersOnMap().All(m => !m.IsNone), "only real markers");
            // a recruited companion shows nothing
            s.World.Flags.Set(WorldRules.RecruitedFlag("aldric"), 1);
            ((IDialogueContext)s).Recruit("aldric");
            Assert(s.QuestMarkersOf("aldric").Count == 0, "recruited companions have no marker");
            s.EnterMap("whisperwood");
            Assert(s.QuestMarkersOnMap().All(m => s.VisibleNpcs().Any(n => n.npc == m.NpcId)), "map markers follow the current map");
        }

        [Test]
        public static void SessionTurnInAndHints()
        {
            var s = SessionTest.NewGame(ClassId.Hunter);
            s.World.Quests.Start("sq_shepherd");
            s.Flags.Set("shepherd_quest", 1);
            var t = s.QuestTurnInOf("sq_shepherd");
            Assert(t.NpcId == "shepherd_bram" && t.Kind == QuestMarker.InProgress, "wolves: Bram waits (grey): " + t);
            var hints = s.QuestHintsOnMap();
            Assert(hints.Any(h => h.Kind == QuestMapHintKind.Encounter && h.Id == "enc_pasture_wolves" && h.QuestId == "sq_shepherd"),
                "the pasture wolves are a kill hint: " + string.Join("; ", hints));
            s.World.Quests.OnKill("cr_wolf", 3);
            t = s.QuestTurnInOf("sq_shepherd");
            Assert(t.NpcId == "shepherd_bram" && t.Kind == QuestMarker.ReadyToTurnIn, "report: return to Bram: " + t);
            Assert(!s.QuestHintsOnMap().Any(h => h.QuestId == "sq_shepherd"), "no hint once the kills are done");
            Assert(s.QuestTurnInOf("sq_wicks").Kind == QuestMarker.None, "not active → none");

            // Reach a map: the exit towards it; Collect: the chest holding the item
            s.World.Quests.Start("mq2_ember_road");
            Assert(s.QuestHintsOnMap().Any(h => h.Kind == QuestMapHintKind.Transition && h.Id == "to_amberfield"), "the road west is hinted");
            s.World.Quests.Start("sq_satchel");
            s.EnterMap("whisperwood");
            Assert(s.QuestHintsOnMap().Any(h => h.Kind == QuestMapHintKind.Chest && h.Id == "chest_satchel"), "the satchel chest is hinted");
        }

        // ------------------------------------------------------------------ labels

        [Test]
        public static void LabelLayoutBasics()
        {
            var lay = new LabelLayout(new LabelRect(0f, 0f, 400f, 300f));
            var items = new List<LabelLayout.Item>();
            for (int i = 0; i < 6; i++)
                items.Add(new LabelLayout.Item { Id = "n" + i, AnchorX = 200f, AnchorY = 150f, Width = 80f, Height = 20f, Priority = i, Important = i == 5 });
            lay.PlaceAll(items);
            Assert(LabelLayout.NoOverlaps(items, out var conflict), conflict);
            Assert(items.All(it => it.Placed), "six labels round one point all fit");
            Assert(items[0].Ring == 0 && items[0].Direction == 0 && !items[0].Leader, "the first goes below, touching");
            Assert(items.Any(it => it.Leader), "later ones need leader lines");
            Assert(items.All(it => lay.Bounds.Contains(it.Rect)), "inside the bounds");
            var edge = new LabelLayout.Item { Id = "edge", AnchorX = 2f, AnchorY = 2f, Width = 60f, Height = 20f };
            Assert(lay.TryPlace(edge) && lay.Bounds.Contains(edge.Rect), "an anchor in a corner finds a side inside");
            lay.Obstacles.Add(new LabelRect(0f, 0f, 400f, 300f));
            var blocked = new LabelLayout.Item { Id = "blocked", AnchorX = 100f, AnchorY = 100f, Width = 10f, Height = 10f };
            Assert(!lay.TryPlace(blocked) && !blocked.Placed, "no place → left out");
        }

        [Test]
        public static void MapViewportZoomPan()
        {
            var v = new MapViewport(90f, 44f, new LabelRect(100f, 50f, 1500f, 733f));
            v.ToScreen(0f, 0f, out float sx, out float sy);
            AssertNear(sx, 100f, 0.01f, "x0 left");
            AssertNear(sy, 783f, 0.01f, "y0 at the bottom");
            v.ToScreen(45f, 22f, out float mx, out float my);
            v.ToWorld(mx, my, out float bx, out float by);
            AssertNear(bx, 45f, 0.01f, "round trip x");
            AssertNear(by, 22f, 0.01f, "round trip y");
            var v2 = new MapViewport(90f, 44f, new LabelRect(100f, 50f, 1500f, 733f));
            v2.ToWorld(mx + 300f, my - 100f, out float ex, out float ey);
            v2.ZoomAt(2.5f, mx + 300f, my - 100f);
            v2.ToWorld(mx + 300f, my - 100f, out float zx, out float zy);
            AssertNear(zx, ex, 0.01f, "zoom keeps the point under the cursor (x)");
            AssertNear(zy, ey, 0.01f, "zoom keeps the point under the cursor (y)");
            v2.ZoomAt(9f, 0f, 0f);
            Assert(v2.Zoom == MapViewport.MaxZoom, "zoom clamps at 3x");
            v2.PanBy(-100000f, 100000f);
            Assert(v2.X0 >= -1e-3f && v2.X0 + v2.ViewWidth <= 90f + 1e-3f && v2.Y0 >= -1e-3f && v2.Y0 + v2.ViewDepth <= 44f + 1e-3f, "pan stays inside the map");
            AssertNear(v2.PixelsPerMetre, 1500f / 30f, 0.01f, "3x zoom on a 90 m map shows 30 m");
        }

        [Test]
        public static void ShortNames()
        {
            Assert(MapLabels.Shorten("Magister Quillon Ashby") == "Quillon Ashby", "honorific stripped");
            Assert(MapLabels.Shorten("Dorrit Applewhistle") == "Dorrit", "long names → first word");
            Assert(MapLabels.Shorten("Elder Maru") == "Elder Maru" && MapLabels.Shorten("Old Tobben") == "Old Tobben", "short names stay");
            Assert(MapLabels.Shorten("Sergeant Holt") == "Holt", "Sergeant Holt → Holt");
            Assert(MapLabels.ShortName(Db, "bw_archivist_penhallow") == "Penhallow", "NpcDef.shortName wins");
            Assert(MapLabels.RegionName(new RegionDef { id = "reg_training_yard" }) == "Training Yard", "region id humanized");
            Assert(MapLabels.RegionName(new RegionDef { id = "bw_reg_market_square" }) == "Market Square", "prefix dropped");
            Assert(MapLabels.RegionName(new RegionDef { id = "reg_x", name = "Trainers' Hall" }) == "Trainers' Hall", "RegionDef.name wins");
        }

        /// <summary>Text width close to the Map panel's bold 14 px font (a little wider, to be safe).</summary>
        static float Width(string t) => (t?.Length ?? 0) * 8.2f;

        [Test]
        public static void MapLabelsNeverOverlapAndQuestNpcsAreLabelled()
        {
            // worst case: every NPC of the map shown (flags ignored) and everyone the quest data names wears a marker
            var questPeople = new HashSet<string>();
            foreach (var q in Db.Quests.Keys) foreach (var p in Index.Touching(q)) questPeople.Add(p);
            int maps = 0;
            foreach (var map in Db.Maps.Values)
                foreach (var ui in new[] { (w: 1920f, h: 1080f, tag: "1080p"), (w: 1280f, h: 720f, tag: "1080p at UserScale 1.5") })
                {
                    maps++;
                    MapLabels.MapSize(ui.w, ui.h, map.width, map.depth, out float mw, out float mh);
                    var screen = new LabelRect((ui.w - mw) * 0.5f, 120f, mw, mh);
                    var input = new MapLabelInput
                    {
                        Db = Db, Map = map, Npcs = map.npcs, View = new MapViewport(map.width, map.depth, screen), TextWidth = Width,
                        MarkerOf = id => questPeople.Contains(id)
                            ? new QuestMarkerInfo { NpcId = id, Kind = QuestMarker.Available, QuestId = "q", Main = id == "elder_maru" }
                            : new QuestMarkerInfo { NpcId = id },
                    };
                    foreach (var ch in map.chests)
                    {
                        input.View.ToScreen(ch.pos.x, ch.pos.y, out float cx, out float cy);
                        input.Obstacles.Add(LabelRect.Around(cx, cy, 16f, 16f));
                    }
                    var plan = MapLabels.Plan(input);
                    string where = $"{map.id} ({ui.tag})";
                    var items = plan.Labels.Select(l => l.Item).ToList();
                    Assert(LabelLayout.NoOverlaps(items, out var conflict), where + ": " + conflict);
                    foreach (var l in plan.Labels.Where(l => l.Placed))
                    {
                        Assert(screen.Contains(l.Rect), $"{where}: {l.Id} label inside the map");
                        for (int i = 0; i < plan.Layout.Obstacles.Count; i++)
                            Assert(i == l.Item.OwnObstacle || !l.Rect.Overlaps(plan.Layout.Obstacles[i]),
                                $"{where}: {l.Id} label {l.Rect} covers a dot or glyph {plan.Layout.Obstacles[i]}");
                    }
                    foreach (var n in map.npcs.Where(n => questPeople.Contains(n.npc)))
                    {
                        var l = plan.LabelOf(n.npc);
                        Assert(l != null && l.Kind == MapLabelKind.Npc && l.Placed, $"{where}: quest NPC {n.npc} has its own label");
                    }
                    foreach (var l in plan.Labels.Where(l => l.Kind == MapLabelKind.Cluster))
                        Assert(l.Members.Count >= 2 && l.Members.All(id => !questPeople.Contains(id)), $"{where}: crowds never swallow quest NPCs");
                }
            Assert(maps >= 6, "maps checked");
        }

        [Test]
        public static void VillageServiceCrowdUsesRegionName()
        {
            var map = Db.Maps["lanternvale"];
            MapLabels.MapSize(1280f, 720f, map.width, map.depth, out float mw, out float mh);
            var screen = new LabelRect(0f, 0f, mw, mh);
            // a narrow strip: the trainers cannot all be named, so the yard gets one label
            var input = new MapLabelInput { Db = Db, Map = map, Npcs = map.npcs, View = new MapViewport(map.width, map.depth, new LabelRect(0f, mh - 60f, mw, 60f)), TextWidth = Width };
            var plan = MapLabels.Plan(input);
            var yard = plan.LabelOf("trainer_quillon");
            Assert(yard != null && yard.Kind == MapLabelKind.Cluster && yard.Text == "Training Yard" && yard.Members.Count == 4,
                "the trainers share one 'Training Yard' label: " + (yard == null ? "none" : yard.Kind + " " + yard.Text));
            // zoomed in, there is room for every name
            var view = new MapViewport(map.width, map.depth, screen);
            view.ToScreen(68f, 8f, out float yx, out float yy);
            view.ZoomAt(3f, yx, yy);
            plan = MapLabels.Plan(new MapLabelInput { Db = Db, Map = map, Npcs = map.npcs, View = view, TextWidth = Width });
            Assert(plan.LabelOf("trainer_quillon")?.Kind == MapLabelKind.Npc && plan.LabelOf("trainer_quillon").Placed, "3x zoom: the trainers are named");
            Assert(plan.LabelOf("villager_june") == null, "NPCs outside the zoomed view get no label");
        }
    }
}
