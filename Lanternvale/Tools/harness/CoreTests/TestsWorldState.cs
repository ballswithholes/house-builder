// MapRuntime bookkeeping and save/load round-trips through Lanternvale.Json.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Json;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.WorldFixtures;

namespace Lanternvale.Tests
{
    public static class TestsWorldState
    {
        [Test]
        public static void MapRuntimeEncountersNpcsChests()
        {
            var db = Db();
            var map = Map();
            db.Maps[map.id] = map;
            var ctx = new FakeWorldContext(db);
            var rt = ctx.World.GetMap("whisperwood");
            Assert(ReferenceEquals(rt, ctx.World.GetMap("whisperwood")), "cached");
            var prev = Log.WarnHandler;
            Log.WarnHandler = _ => { };
            try { Assert(ctx.World.GetMap("nowhere") == null, "unknown map"); }
            finally { Log.WarnHandler = prev; }

            // encounters
            var wolves = rt.FindEncounter("wolves1");
            var warden = rt.FindEncounter("warden");
            Assert(MapRuntime.DoneFlag(wolves) == "enc_wolves1" && MapRuntime.DoneFlag(warden) == "warden_dead", "done flags");
            Assert(rt.IsEncounterAvailable(wolves) && !rt.IsEncounterAvailable(warden), "warden needs shrine_open");
            Assert(rt.FindTriggeredEncounter(new Vec2(10, 8)) == null, "far away");
            Assert(rt.FindTriggeredEncounter(new Vec2(26, 6)) == wolves, "within 6 m");
            Assert(rt.FindTriggeredEncounter(new Vec2(26, 6), stealthed: true) == null, "stealthed: only within 3 m");
            Assert(rt.FindTriggeredEncounter(new Vec2(28, 6), stealthed: true) == wolves, "stealthed within 3 m");
            var party = new List<Vec2> { new Vec2(10, 8), new Vec2(25.5f, 6) };
            Assert(rt.FindTriggeredEncounter(party, new List<bool> { false, false }) == wolves, "any member triggers");
            Assert(rt.FindTriggeredEncounter(party, new List<bool> { false, true }) == null, "stealthed member out of 3 m");
            ctx.Flags.Set("shrine_open");
            Assert(rt.IsEncounterAvailable(warden) && !rt.IsEncounterVisible(warden), "hidden ambush invisible");
            rt.MarkEncounterTriggered("warden");
            Assert(rt.IsEncounterVisible(warden) && rt.IsEncounterTriggered("warden"), "revealed once triggered");
            rt.MarkEncounterDone("wolves1");
            Assert(rt.IsEncounterDone("wolves1") && ctx.Flags.IsSet("enc_wolves1") && rt.FindTriggeredEncounter(new Vec2(30, 6)) == null, "done");
            int avail = 0;
            foreach (var _ in rt.AvailableEncounters()) avail++;
            Assert(avail == 1, "only warden left");
            rt.ResetEncounter("wolves1");
            Assert(!rt.IsEncounterDone("wolves1"), "reset");

            // npcs
            var visible = new List<string>();
            foreach (var n in rt.VisibleNpcs()) visible.Add(n.npc);
            Assert(string.Join(",", visible) == "hunter_brann,lyra", string.Join(",", visible));
            ctx.Flags.Set("met_elder");
            WorldRules.Execute(Out(OutcomeType.Recruit, "lyra"), ctx);
            visible.Clear();
            foreach (var n in rt.VisibleNpcs()) visible.Add(n.npc);
            Assert(string.Join(",", visible) == "hunter_brann,elder_maren", "elder appears, recruited lyra hides: " + string.Join(",", visible));
            ctx.Flags.Set("elder_left");
            Assert(!rt.IsNpcVisible(map.npcs[1]), "hideFlag");

            // chests
            var chestA = rt.FindChest("chest_a");
            var chestB = rt.FindChest("chest_b");
            Assert(rt.IsChestAvailable(chestA) && !rt.IsChestAvailable(chestB), "chest_b needs a flag");
            Assert(rt.IsChestLocked(chestA) && !rt.IsChestLocked(chestB), "lock");
            var rng = new Rng(11);
            CheckResult res = null;
            int tries = 0;
            while (rt.IsChestLocked(chestA) && tries++ < 50)
            {
                res = rt.TryUnlockChest(chestA, ctx, rng);
                Assert(res != null && res.Skill == SkillCheck.SleightOfHand && res.Dc == 12, "lock check rolled");
            }
            Assert(!rt.IsChestLocked(chestA) && res.Success, "eventually unlocked");
            Assert(rt.TryUnlockChest(chestA, ctx, rng) == null, "no roll once unlocked");
            Assert(!rt.IsChestOpened("chest_a"), "not opened yet");
            rt.MarkChestOpened("chest_a");
            Assert(rt.IsChestOpened("chest_a"), "opened");

            // transitions, regions, spawns
            var toShrine = rt.TransitionAt(new Vec2(39.5f, 9));
            Assert(toShrine != null && toShrine.id == "to_shrine" && rt.IsTransitionUnlocked(toShrine), "shrine open now");
            ctx.Flags.Clear("shrine_open");
            Assert(!rt.IsTransitionUnlocked(toShrine), "locked without the flag");
            Assert(rt.TransitionAt(new Vec2(1.5f, 8.5f)).id == "to_village" && rt.TransitionAt(new Vec2(20, 8)) == null, "default 2x2 size centred on pos");
            Assert(rt.UpdatePartyPosition(new Vec2(36, 8)).Count == 1, "first entry");
            Assert(rt.UpdatePartyPosition(new Vec2(36.5f, 8)).Count == 0, "still inside: no toast");
            rt.UpdatePartyPosition(new Vec2(20, 8));
            Assert(rt.UpdatePartyPosition(new Vec2(36, 8)).Count == 0 && rt.HasEnteredRegion("shrine_gate"), "re-entry: no toast");
            Assert(rt.SpawnPosition("east") == new Vec2(37, 8) && rt.SpawnPosition("missing") == new Vec2(3, 8), "spawns");
            rt.OnEnterMap();
            Assert(rt.State.visited, "visited");
        }

        static string RoundTrip(WorldState w, out WorldSaveData parsed)
        {
            var json = JsonWriter.Serialize(w.Save());
            var mapCtx = new JsonMapContext { Source = "save" };
            parsed = JsonMapper.FromJson<WorldSaveData>(json, mapCtx, "save");
            Assert(mapCtx.Problems.Count == 0, "no mapping problems: " + string.Join("; ", mapCtx.Problems));
            return json;
        }

        [Test]
        public static void SaveLoadRoundTrip()
        {
            var db = Db();
            var map = Map();
            db.Maps[map.id] = map;
            var d = new DialogueDef { id = "d", start = "n" };
            var n = Node("n", "Hi");
            var once = Choice("Once");
            once.once = true;
            n.choices.Add(once);
            n.choices.Add(Choice("Bye"));
            d.nodes.Add(n);
            db.Dialogues[d.id] = d;

            var ctx = new FakeWorldContext(db);
            var w = ctx.World;
            ctx.Flags.Set("met_elder");
            ctx.Flags.Set("lanterns_lit", 2);
            ctx.Quests.Start("q_wolves");
            ctx.Quests.OnKill("wolf", 2);
            ctx.Quests.Start("q_lanterns");
            var runner = w.CreateDialogueRunner(new Rng(1));
            runner.Start("d");
            runner.Choose(0);
            var rt = w.GetMap("whisperwood");
            rt.MarkChestOpened("chest_a");
            rt.MarkChestUnlocked("chest_a");
            rt.UpdatePartyPosition(new Vec2(36, 8));
            rt.MarkEncounterTriggered("warden");
            rt.OnEnterMap();

            var json = RoundTrip(w, out var parsed);
            Assert(json.Contains("\"lanterns_lit\": 2") && json.Contains("\"status\": \"Active\""), "readable json");

            // load into a fresh world
            var ctx2 = new FakeWorldContext(db);
            var rtBefore = ctx2.World.GetMap("whisperwood"); // existing runtime is updated in place
            ctx2.World.Load(parsed);
            Assert(ctx2.Flags.Get("lanterns_lit") == 2 && ctx2.Flags.IsSet("met_elder") && ctx2.Flags.IsSet("saw_shrine"), "flags");
            Assert(ctx2.Quests.GetStage("q_wolves") == "hunt" && ctx2.Quests.GetProgress("q_wolves", 0) == 2, "quest progress");
            Assert(ctx2.Quests.GetProgress("q_lanterns", 0) == 2, "flag objective progress");
            var j = ctx2.Quests.GetJournal();
            Assert(j.Count == 2 && j[0].Id == "q_lanterns", "journal order (main first)");
            Assert(ctx2.World.DialogueMemory.HasChosen("d", "n", 0) && ctx2.World.DialogueMemory.TimesStarted("d") == 1, "dialogue memory");
            var runner2 = ctx2.World.CreateDialogueRunner(new Rng(1));
            runner2.Start("d");
            Assert(runner2.Current.Choices.Count == 1 && runner2.Current.Choices[0].Text == "Bye", "once choice stays hidden after load");
            var rt2 = ctx2.World.GetMap("whisperwood");
            Assert(ReferenceEquals(rt2, rtBefore), "runtime reference kept");
            Assert(rt2.IsChestOpened("chest_a") && !rt2.IsChestLocked(rt2.FindChest("chest_a")) && rt2.HasEnteredRegion("shrine_gate") && rt2.IsEncounterTriggered("warden") && rt2.State.visited, "map runtime");

            // progress continues after load
            ctx2.Quests.OnKill("wolf");
            Assert(ctx2.Quests.GetStage("q_wolves") == "pelts", "continues");

            // stable serialization: save(load(save)) == save
            var ctx3 = new FakeWorldContext(db);
            ctx3.World.Load(parsed);
            var json2 = JsonWriter.Serialize(ctx3.World.Save());
            Assert(json2 == json, "byte-identical re-save");

            // individual components
            var fs = new FlagStore();
            fs.Load(JsonMapper.FromJson<FlagStoreState>(JsonWriter.Serialize(ctx.Flags.Save())));
            Assert(fs.Get("lanterns_lit") == 2, "FlagStoreState");
            var ql = new QuestLog(db, ctx);
            ql.Load(JsonMapper.FromJson<QuestLogState>(JsonWriter.Serialize(ctx.Quests.Save())));
            Assert(ql.GetProgress("q_wolves", 0) == 2, "QuestLogState");
            var mem = new DialogueMemory();
            mem.Load(JsonMapper.FromJson<DialogueMemoryState>(JsonWriter.Serialize(w.DialogueMemory.Save())));
            Assert(mem.ChosenCount == 1, "DialogueMemoryState");
            var ms = JsonMapper.FromJson<MapRuntimeState>(JsonWriter.Serialize(rt.State));
            Assert(ms.mapId == "whisperwood" && ms.openedChests.Count == 1 && ms.enteredRegions.Count == 1, "MapRuntimeState");

            // empty / null loads are safe
            var ctx4 = new FakeWorldContext(db);
            ctx4.World.Load(null);
            ctx4.World.Load(JsonMapper.FromJson<WorldSaveData>("{}"));
            Assert(ctx4.Flags.Count == 0 && ctx4.Quests.GetJournal().Count == 0, "empty load");
        }

        [Test]
        public static void PendingRewardChoiceSurvivesSave()
        {
            var db = Db();
            var ctx = new FakeWorldContext(db);
            ctx.Quests.Complete("q_wolves");
            Assert(ctx.Quests.PendingRewardChoices.Count == 1, "pending");
            var json = JsonWriter.Serialize(ctx.World.Save());
            var ctx2 = new FakeWorldContext(db);
            ctx2.World.Load(JsonMapper.FromJson<WorldSaveData>(json));
            Assert(ctx2.Quests.PendingRewardChoices.Count == 1 && ctx2.Quests.ClaimRewardChoice("q_wolves", "lantern_oil"), "claim after load");
            Assert(ctx2.Quests.GetEntry("q_wolves").Status == QuestStatus.Completed, "completed after load");
        }
    }
}
