// QuestLog tests: stages, objectives (kill/collect/talk/flag/reach/defeat), rewards, journal, events.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.WorldFixtures;

namespace Lanternvale.Tests
{
    public static class TestsWorldQuests
    {
        [Test]
        public static void WolfQuestProgression()
        {
            var ctx = new FakeWorldContext(Db());
            var q = ctx.Quests;
            var events = new List<QuestEvent>();
            q.Changed += e => events.Add(e);

            q.OnKill("wolf"); // before the quest: does not count
            ctx.Items["wolf_pelt"] = 1; // already carrying one pelt
            Assert(q.Start("q_wolves") && !q.Start("q_wolves"), "start once");
            Assert(q.GetStatus("q_wolves") == QuestStatus.Active && q.GetStage("q_wolves") == "hunt", "first stage");
            Assert(events.Count == 1 && events[0].Kind == QuestEventKind.Started && events[0].QuestName == "Wolves at the Door", "started event");
            Assert(q.GetProgress("q_wolves", 0) == 0, "earlier kill not counted");

            q.OnKill("wolf");
            q.OnKill("boar");
            q.OnKill("wolf");
            Assert(q.GetProgress("q_wolves", 0) == 2, "two wolves");
            var last = events[events.Count - 1];
            Assert(last.Kind == QuestEventKind.ObjectiveProgress && last.Text == "Wolves slain 2/3" && last.Progress == 2 && last.Count == 3, $"progress toast ({last})");
            var entry = q.GetEntry("q_wolves");
            Assert(entry.StageText == "Slay the wolves in Whisperwood." && entry.Objectives.Count == 1 && entry.Objectives[0].Display == "Wolves slain 2/3" && !entry.Objectives[0].Complete, "journal objective");
            Assert(entry.GiverName == "Brann" && entry.Level == 3 && entry.Summary.Length > 0, "journal header");

            q.OnKill("wolf", 5);
            Assert(ctx.Flags.IsSet("wolves_thinned"), "stage onComplete outcome ran");
            Assert(q.GetStage("q_wolves") == "pelts", "advanced to pelts");
            Assert(q.GetProgress("q_wolves", 0) == 1, "pelt already carried counts (pulled on stage entry)");
            Assert(events.Exists(e => e.Kind == QuestEventKind.ObjectiveCompleted && e.Text == "Wolves slain 3/3"), "objective completed event");
            Assert(events.Exists(e => e.Kind == QuestEventKind.StageAdvanced && e.StageId == "pelts" && e.Text == "Bring Brann two wolf pelts."), "stage advanced event");

            ctx.GiveItem("wolf_pelt", 1); // context reports the new count → OnItemCount
            Assert(q.GetStage("q_wolves") == "return", "pelts collected");
            entry = q.GetEntry("q_wolves");
            Assert(entry.Objectives[0].Display == "Speak with Brann", $"default talk objective text ({entry.Objectives[0].Display})");
            Assert(entry.History.Count == 2 && entry.History[0] == "Slay the wolves in Whisperwood.", "history");

            q.OnTalk("elder_maren");
            Assert(q.IsActive("q_wolves"), "wrong npc");
            q.OnTalk("hunter_brann");
            Assert(q.IsCompleted("q_wolves"), "completed");
            Assert(ctx.Xp == 450 && ctx.GoldValue == 250 && ctx.CountItem("lantern_oil") == 1, "rewards");
            Assert(events.Exists(e => e.Kind == QuestEventKind.Completed), "completed event");
            Assert(q.PendingRewardChoices.Count == 1 && events.Exists(e => e.Kind == QuestEventKind.RewardChoicePending), "choice pending");
            Assert(!q.ClaimRewardChoice("q_wolves", "sword_of_nope"), "item not offered");
            Assert(q.ClaimRewardChoice("q_wolves", "wolf_pelt") && ctx.CountItem("wolf_pelt") == 3, "claimed");
            Assert(q.PendingRewardChoices.Count == 0 && !q.ClaimRewardChoice("q_wolves", "wolf_pelt"), "claimed once");

            entry = q.GetEntry("q_wolves");
            Assert(entry.Status == QuestStatus.Completed && entry.History.Count == 3 && entry.Objectives.Count == 0, "finished journal entry");
            Assert(!q.Complete("q_wolves") && !q.Fail("q_wolves") && !q.SetStage("q_wolves", "hunt"), "finished quests are final");
            q.OnKill("wolf");
            Assert(q.IsCompleted("q_wolves"), "notifications ignore finished quests");
        }

        [Test]
        public static void CollectProgressCanDrop()
        {
            var ctx = new FakeWorldContext(Db());
            var q = ctx.Quests;
            q.SetStage("q_wolves", "pelts");
            Assert(q.IsActive("q_wolves") && q.GetStage("q_wolves") == "pelts", "SetStage starts the quest");
            ctx.GiveItem("wolf_pelt", 1);
            Assert(q.GetProgress("q_wolves", 0) == 1, "1/2");
            ctx.TakeItem("wolf_pelt", 1);
            Assert(q.GetProgress("q_wolves", 0) == 0, "dropped back to 0/2");
            ctx.Items["wolf_pelt"] = 5;
            q.Refresh();
            Assert(q.GetStage("q_wolves") == "return", "Refresh pulls inventory");
        }

        [Test]
        public static void FlagReachDefeatObjectives()
        {
            var db = Db();
            var map = Map();
            db.Maps[map.id] = map;
            var ctx = new FakeWorldContext(db);
            var q = ctx.Quests;
            var rt = ctx.World.GetMap("whisperwood");
            q.Start("q_lanterns");
            ctx.Flags.Set("lanterns_lit", 1); // WorldState wires FlagStore.Changed → OnFlag
            Assert(q.GetProgress("q_lanterns", 0) == 1, "flag counter 1/3");
            Assert(q.GetEntry("q_lanterns").Objectives[0].Display == "Lanterns lit 1/3", "display");
            ctx.Flags.Add("lanterns_lit", 2);
            Assert(q.GetStage("q_lanterns") == "shrine", "three lanterns lit");

            rt.UpdatePartyPosition(new Vec2(10, 3));
            Assert(q.GetProgress("q_lanterns", 0) == 0, "not in region yet");
            var first = rt.UpdatePartyPosition(new Vec2(36, 8));
            Assert(first.Count == 1 && first[0].id == "shrine_gate", "first entry toast");
            Assert(q.GetProgress("q_lanterns", 0) == 1, "reach objective done");
            Assert(ctx.Flags.IsSet("saw_shrine"), "region enterFlag");

            ctx.Flags.Set("shrine_open");
            rt.MarkEncounterDone("warden");
            Assert(ctx.Flags.IsSet("warden_dead"), "custom done flag");
            Assert(q.IsCompleted("q_lanterns"), "defeat objective completes the quest");

            // Defeat objective pulled from enc_<id> when the encounter was already won before the stage began
            var db2 = Db();
            var quest = new QuestDef { id = "q_pack", name = "The Pack" };
            quest.stages.Add(new QuestStageDef { id = "s", objectives = { Obj(ObjectiveType.Defeat, "wolves1") } });
            db2.Quests[quest.id] = quest;
            var ctx2 = new FakeWorldContext(db2);
            ctx2.Flags.Set("enc_wolves1");
            ctx2.Quests.Start("q_pack");
            Assert(ctx2.Quests.IsCompleted("q_pack"), "already-defeated encounter counts");
        }

        [Test]
        public static void StageJumpsAndFailure()
        {
            var db = Db();
            var q3 = new QuestDef { id = "q_chain", name = "Chain" };
            var s1 = new QuestStageDef { id = "one", description = "One", next = "two" };
            s1.onComplete.Add(Out(OutcomeType.SetFlag, "one_done"));
            s1.onComplete.Add(Out(OutcomeType.StartQuest, "q_wolves"));
            var s2 = new QuestStageDef { id = "two", description = "Two", next = "three" };
            s2.onComplete.Add(Out(OutcomeType.GiveXP)); // amount 0: no-op
            q3.stages.Add(s1);
            q3.stages.Add(s2);
            q3.stages.Add(new QuestStageDef { id = "three", description = "Three" });
            db.Quests[q3.id] = q3;
            var ctx = new FakeWorldContext(db);
            var q = ctx.Quests;

            Assert(q.Start("q_chain") && q.GetStage("q_chain") == "one", "stage without objectives waits");
            Assert(q.SetStage("q_chain", "two"), "jump forward");
            Assert(ctx.Flags.IsSet("one_done") && q.IsActive("q_wolves"), "forward jump ran onComplete (flag + chained StartQuest)");
            Assert(q.SetStage("q_chain", "one") && q.GetStage("q_chain") == "one", "jump back");
            ctx.Flags.Clear("one_done");
            Assert(q.SetStage("q_chain", "one") && !ctx.Flags.IsSet("one_done"), "same stage is a no-op");
            var prev = Log.WarnHandler;
            Log.WarnHandler = _ => { };
            try { Assert(!q.SetStage("q_chain", "nope") && !q.Start("q_unknown"), "unknown stage/quest"); }
            finally { Log.WarnHandler = prev; }
            Assert(q.Fail("q_chain") && q.GetStatus("q_chain") == QuestStatus.Failed, "failed");
            Assert(!q.Start("q_chain"), "failed quests do not restart");

            // journal ordering: active main quests first, then active side, then completed, then failed
            q.Start("q_lanterns");
            q.Complete("q_wolves");
            var j = q.GetJournal();
            Assert(j.Count == 3 && j[0].Id == "q_lanterns" && j[1].Id == "q_wolves" && j[2].Id == "q_chain", string.Join(",", j.ConvertAll(e => e.Id)));
            Assert(q.GetJournal(false).Count == 1, "active only");
            Assert(q.GetEntry("q_lanterns").Main, "main flag");
        }
    }
}
