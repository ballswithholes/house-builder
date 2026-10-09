// Quest items carried by creatures (embers, wicks, pelts, bells, fen-glass, wisp-glows...) are guaranteed drops so a
// quest never fails on luck, but with `whileQuestNeeds` they only drop while an unfinished quest still collects them
// (capped to the shortfall), so they no longer pile up in the bags once the quest is done (DataSchema.md, loot entries).
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
    public static class TestsQuestItemLoot
    {
        const string Ember = "spirit_ember", Quest = "mq_lanterns";

        static int Rolled(GameSession s, string table, string item, int times)
        {
            var ctx = s.BuildLootContext();   // one context = one battle: drops of earlier creatures count
            var rng = new Rng(77);
            int n = 0;
            for (int i = 0; i < times; i++)
                foreach (var it in LootGenerator.Roll(Db, table, 10, rng, ctx).Items)
                    if (it.Def.id == item) n += it.Count;
            return n;
        }

        [Test]
        public static void QuestItems_DropOnlyWhileAQuestNeedsThem()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 10, seed: 311);
            Assert(!s.Quests.IsCompleted(Quest), "the main quest is not done yet");
            Assert(s.Inventory.Count(Ember) == 0, "no embers to start with");
            Assert(s.Quests.CollectNeed(Ember) == 3, "the main quest wants three embers: " + s.Quests.CollectNeed(Ember));

            // before the collecting stage (or before the quest is even picked up): encounters never respawn, so the
            // wisps still drop embers, but never more than the quest wants within one fight
            Assert(Rolled(s, "lt_hollow_wisp", Ember, 6) == 3, "six wisps of one fight give exactly the three embers needed");

            s.Inventory.Add(Db.Item(Ember), 2);
            Assert(Rolled(s, "lt_hollow_wisp", Ember, 6) == 1, "two in the bags: one more ember");

            s.Inventory.Add(Db.Item(Ember), 1);
            Assert(Rolled(s, "lt_hollow_wisp", Ember, 4) == 0, "three in the bags: no more embers");
            s.Inventory.Remove(Ember, 3);

            // at the collecting stage
            if (s.Quests.GetStatus(Quest) == QuestStatus.NotStarted) Assert(s.Quests.Start(Quest), "the main quest starts");
            Assert(s.Quests.SetStage(Quest, "embers"), "the main quest is at the embers stage");
            Assert(Rolled(s, "lt_hollow_wisp", Ember, 2) == 2, "two wisps at the embers stage: two embers");

            // past the collecting stage, and done: nothing more piles up in the bags
            Assert(s.Quests.SetStage(Quest, "embers_return"), "past the embers stage");
            Assert(s.Quests.CollectNeed(Ember) == 0, "nothing wants embers any more");
            Assert(Rolled(s, "lt_hollow_wisp", Ember, 6) == 0, "past the stage: no embers");
            s.Quests.Complete(Quest);
            Assert(Rolled(s, "lt_hollow_wisp", Ember, 6) == 0, "quest done: no embers");
            // the other drops of the table are untouched
            var ctx = s.BuildLootContext();
            var rng = new Rng(5);
            bool ash = false;
            for (int i = 0; i < 40 && !ash; i++) ash = LootGenerator.Roll(Db, "lt_hollow_wisp", 10, rng, ctx).Items.Any(it => it.Def.id == "junk_grey_ash");
            Assert(ash, "the wisps still drop their ash");

            // a stack drop is capped to the shortfall too (Old Gnasher's 2-3 tails, Hettie wants 4)
            var s2 = SessionTest.NewGame(ClassId.Warrior, 20, seed: 312);
            s2.Inventory.Add(Db.Item("mf_croc_tail"), 3);
            Assert(Rolled(s2, "lt_mf_old_gnasher", "mf_croc_tail", 1) == 1, "three tails held: Old Gnasher gives the fourth only");

            // through a real fight: the wisps of the grove leave only the missing ember in the loot window
            var s3 = SessionTest.NewGame(ClassId.Warrior, 20, seed: 141);
            s3.Inventory.Add(Db.Item(Ember), 2);
            s3.EnterMap("whisperwood", "from_village");
            var b = s3.StartEncounter("enc_wisps_grove");
            Assert(b != null, "the wisp fight starts: " + s3.LastError);
            int wisps = b.Units.Count(u => u.Team != b.PlayerTeam && u.Creature != null && u.Creature.lootTable == "lt_hollow_wisp");
            Assert(wisps >= 2, "several wisps: " + wisps);
            foreach (var u in b.Units) if (u.Team != b.PlayerTeam && u.IsAlive) u.Health = 1f;
            Assert(s3.AutoResolve(80) == BattleOutcome.Victory, "the wisps are beaten");
            s3.FinishBattle();
            int inWindow = s3.PendingLoot == null ? 0 : s3.PendingLoot.Items.Where(it => it.Def.id == Ember).Sum(it => it.Count);
            Assert(inWindow == 1, $"{wisps} wisps, two embers held: one ember in the window ({inWindow})");
        }

        [Test]
        public static void QuestNeed_KeepsTheRngStream()
        {
            // a capped entry rolls exactly like an uncapped one: the rest of the fight's loot does not change
            var s = SessionTest.NewGame(ClassId.Warrior, 10, seed: 313);
            s.Inventory.Add(Db.Item(Ember), 3);
            var capped = s.BuildLootContext();
            var plain = new LootContext();
            Assert(plain.QuestNeed == null && plain.QuestCap(Ember, 2) == 2, "no quest knowledge: entries drop as plain items");
            var r1 = new Rng(901);
            var r2 = new Rng(901);
            for (int i = 0; i < 12; i++)
            {
                var a = LootGenerator.Roll(Db, "lt_hollow_wisp", 10, r1, capped).Items.Where(it => it.Def.id != Ember).Select(it => it.Def.id + "x" + it.Count);
                var b = LootGenerator.Roll(Db, "lt_hollow_wisp", 10, r2, plain).Items.Where(it => it.Def.id != Ember).Select(it => it.Def.id + "x" + it.Count);
                Assert(a.SequenceEqual(b), $"roll {i}: same other drops ({string.Join(",", a)} vs {string.Join(",", b)})");
            }
            Assert(r1.NextULong() == r2.NextULong(), "same RNG use");
        }

        [Test]
        public static void EveryQuestItemEntry_UsesWhileQuestNeeds()
        {
            var collected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var q in Db.Quests.Values)
                foreach (var st in q.stages)
                    foreach (var o in st.objectives)
                        if (o.type == ObjectiveType.Collect) collected.Add(o.target);
            int n = 0;
            foreach (var t in Db.LootTables.Values)
                foreach (var e in t.entries)
                {
                    if (string.IsNullOrEmpty(e.item) || !collected.Contains(e.item)) continue;
                    n++;
                    Assert(e.whileQuestNeeds, $"{t.id}: quest item '{e.item}' needs whileQuestNeeds");
                }
            Assert(n >= 16, "the quest-item entries were found: " + n);
        }

        const string BadBundle = @"{
  ""lootTables"": [
    {""id"": ""lt_xqi"", ""entries"": [
      {""item"": ""junk_grey_ash"", ""whileQuestNeeds"": true},
      {""random"": true, ""whileQuestNeeds"": true},
      {""pool"": [""spirit_ember""], ""whileQuestNeeds"": true},
      {""item"": ""spirit_ember"", ""whileQuestNeeds"": true}
    ]}
  ]
}";

        [Test]
        public static void Validator_ChecksWhileQuestNeeds()
        {
            var files = ReadDataFiles(DataDir).ToList();
            files.Add(new KeyValuePair<string, string>("zz_quest_item_loot_bad.json", BadBundle));
            var db = GameDatabase.Load(files);
            Assert(db.Problems.Count == 0, "the bundle parses: " + string.Join("; ", db.Problems));
            var p = DataValidator.Validate(db).Where(x => x.StartsWith("loot lt_xqi", StringComparison.Ordinal)).ToList();
            Assert(p.Count(x => x.Contains("no quest collects 'junk_grey_ash'")) == 1, "an item no quest collects: " + string.Join("; ", p));
            Assert(p.Count(x => x.Contains("whileQuestNeeds needs a plain item entry")) == 2, "random and pooled entries: " + string.Join("; ", p));
            Assert(p.Count == 3, "the collected ember passes: " + string.Join("; ", p));
        }
    }
}
