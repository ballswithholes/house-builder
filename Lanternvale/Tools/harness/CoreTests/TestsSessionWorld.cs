// GameSession: dialogue through the World runner (Elder Maru), vendors, trainers, respec, rest, chests/locks,
// travel, food/drink and the game clock.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsSessionWorld
    {
        [Test]
        public static void ElderMaru_Dialogue_AdvancesMainQuest()
        {
            var s = SessionTest.NewGame(ClassId.Mage, 1, seed: 201, opening: true);
            SessionTest.Pick(s, "Grey? Like ash?");
            SessionTest.Pick(s, "I'll help");
            SessionTest.Finish(s);
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "elder", "quest at stage 'elder'");
            var r = s.TalkTo("elder_maru");
            Harness.Assert(r.Ok && r.Kind == InteractKind.Dialogue && s.Dialogue.Dialogue.id == "dlg_elder_maru", "talking to the elder");
            Harness.Assert(s.Dialogue.Current.SpeakerName == "Elder Maru", "speaker resolved");
            // the History check is a mage proficiency: preview uses the party's stats
            SessionTest.SkipText(s);
            var hist = s.Dialogue.Current.Choices.Find(c => c.Check != null && c.Check.Skill == SkillCheck.History);
            Harness.Assert(hist != null && hist.Check.RollerId == "player" && hist.Check.Modifier >= SkillChecks.ProficiencyBonus - 1, "History check preview for the mage");
            s.TakeEvents();
            s.ChooseDialogue(hist.Index);
            var ev = s.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.SkillCheck) != null, "SkillCheck event");
            SessionTest.SkipText(s);
            SessionTest.Pick(s, "keep it safe");
            SessionTest.Finish(s);
            Harness.Assert(s.CountItem("kindling_taper") == 1, "kindling taper received");
            Harness.Assert(s.Flags.IsSet("met_elder"), "met_elder");
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "wayshrine", "quest advanced to 'wayshrine'");
            var ev2 = s.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(ev2, SessionEventKind.ItemReceived, "kindling_taper") != null, "ItemReceived toast");
            Harness.Assert(SessionTest.FindEvent(ev2, SessionEventKind.QuestUpdated, "mq_lanterns") != null, "QuestUpdated toast");
            // talking again: progress branch
            s.TalkTo("elder_maru");
            SessionTest.SkipText(s);
            Harness.Assert(SessionTest.ChoiceIndex(s.Dialogue.Current, "Remind me where") >= 0, "hint for the wayshrine stage");
            SessionTest.Finish(s);
        }

        [Test]
        public static void QuestReward_ChoiceAndJournal()
        {
            var s = SessionTest.NewGame(ClassId.Hunter, 5, seed: 205);
            s.Quests.Start("sq_shepherd");
            var j = s.Journal();
            Harness.Assert(j.Exists(e => e.Id == "sq_shepherd" && e.Status == QuestStatus.Active), "journal lists the quest");
            int gold = s.Gold, xp = s.Main.Xp, lvl = s.Main.Level;
            s.TakeEvents();
            s.Quests.Complete("sq_shepherd");
            var ev = s.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.QuestCompleted, "sq_shepherd") != null, "completed toast");
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.QuestRewardChoice, "sq_shepherd") != null, "reward choice request");
            Harness.Assert(s.Gold > gold && (s.Main.Xp > xp || s.Main.Level > lvl), "gold and xp rewards");
            Harness.Assert(s.PendingQuestRewards.Count == 1, "pending choice");
            var choices = s.QuestRewardChoices("sq_shepherd");
            Harness.Assert(choices.Count == 4, "four options");
            Harness.Assert(s.ClaimQuestReward("sq_shepherd", "potion_minor_healing") != null, "not an option");
            Harness.Assert(s.ClaimQuestReward("sq_shepherd", choices[1].id) == null, "claim");
            Harness.Assert(s.CountItem(choices[1].id) == 1 && s.PendingQuestRewards.Count == 0, "reward in the bags");
            Harness.Assert(s.ClaimQuestReward("sq_shepherd", choices[0].id) != null, "only once");
        }

        [Test]
        public static void Vendor_Buy_Sell_Buyback()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 1, seed: 211);
            s.TalkTo("merchant_tilly");
            SessionTest.Finish(s);   // whatever the greeting, no vendor without asking
            s.OpenVendor("merchant_tilly");
            Harness.Assert(s.ActiveVendor != null && s.ActiveVendor.Npc.id == "merchant_tilly", "vendor open");
            var offers = s.ActiveVendor.Offers();
            Harness.Assert(offers.Count > 0, "offers");
            var offer = offers.Find(o => o.Item.id == "potion_minor_healing") ?? offers[0];
            int gold = s.Gold, have = s.CountItem(offer.Item.id);
            Harness.Assert(s.Buy(offer.Item.id, 2) == null, "buy two");
            Harness.Assert(s.Gold == gold - offer.Price * 2 && s.CountItem(offer.Item.id) == have + 2, "paid and received");
            Harness.Assert(s.Buy(offer.Item.id, 100000) != null, "cannot afford 100000");
            var junk = s.Inventory.Find("food_rice_ball");
            int g2 = s.Gold, n = s.CountItem("food_rice_ball");
            Harness.Assert(s.Sell(junk, 1) == null, "sell one rice ball");
            Harness.Assert(s.Gold == g2 + junk.SellPrice && s.CountItem("food_rice_ball") == n - 1, "sold for a quarter");
            var bb = s.ActiveVendor.Buyback[0];
            Harness.Assert(s.BuyBack(bb) == null && s.CountItem("food_rice_ball") == n && s.Gold == g2, "buyback at the sell price");
            s.CloseVendor();
            Harness.Assert(s.ActiveVendor == null, "closed");
            Harness.Assert(s.Buy(offer.Item.id) != null, "no buying without a vendor");
            // dialogue route: "trade" choice opens the vendor after the dialogue
            s.TalkTo("merchant_tilly");
            SessionTest.SkipText(s);
            int i = -1;
            foreach (var c in s.Dialogue.Current.Choices)
                foreach (var o in c.Def.outcomes) if (o.type == OutcomeType.OpenVendor) i = c.Index;
            if (i >= 0)
            {
                s.ChooseDialogue(i);
                SessionTest.Finish(s);
                Harness.Assert(s.ActiveVendor != null, "OpenVendor outcome opens the shop after the dialogue");
            }
        }

        [Test]
        public static void Trainer_Learn_And_Respec()
        {
            var db = Harness.Db;
            var s = SessionTest.NewGame(ClassId.Mage, 1, seed: 221);
            int need = 0;
            for (int l = 1; l < 12; l++) need += Progression.XpToNextLevel(db, l);
            s.GivePartyXp(need);
            Harness.Assert(s.Main.Level == 12, "level 12");
            Harness.Assert(s.Train(s.Main, "mage_fireball") != null, "no trainer open");
            s.OpenTrainer("trainer_odo");
            Harness.Assert(!s.CanTrainHere(s.Main), "Odo does not teach mages");
            s.CloseTrainer();
            s.OpenTrainer("trainer_quillon");
            Harness.Assert(s.CanTrainHere(s.Main), "Quillon teaches mages");
            var offers = s.TrainerOffers(s.Main);
            var fireball = offers.Find(o => o.Ability.id == "mage_fireball" && o.CanTrain);
            Harness.Assert(fireball != null, "a fireball rank to train");
            int gold = s.Gold, rank = s.Main.RankOf("mage_fireball");
            Harness.Assert(s.Train(s.Main, "mage_fireball") == null, "train fireball");
            Harness.Assert(s.Main.RankOf("mage_fireball") == AbilityRules.MaxRankAtLevel(db.Ability("mage_fireball"), 12) && s.Main.RankOf("mage_fireball") > rank, "highest rank learned");
            Harness.Assert(s.Gold < gold, "paid");
            s.Inventory.Gold += 100000;
            int trained = s.TrainAll(s.Main);
            Harness.Assert(trained > 0, "train everything else");
            foreach (var o in s.TrainerOffers(s.Main)) Harness.Assert(!o.CanTrain, $"nothing left to train ({o})");

            // talents → respec
            Harness.Assert(s.TalentPointsAvailable(s.Main) == 3, "3 points at level 12");
            Harness.Assert(s.AutoAllocateTalents(s.Main) == 3, "auto-allocate");
            Harness.Assert(s.Respec(s.Main) != null, "respec needs a trainer");
            s.OpenRespec("trainer_quillon");
            int g = s.Gold, cost = s.RespecCost(s.Main);
            Harness.Assert(s.Respec(s.Main) == null, "respec");
            Harness.Assert(s.Gold == g - cost && s.TalentPointsAvailable(s.Main) == 3 && s.Main.Talents.Count == 0, "talents reset for gold");
            Harness.Assert(s.RespecCost(s.Main) > cost, "next respec costs more");
            var tree = db.TalentTrees[s.Main.Class.talentTrees[0]];
            var t1 = tree.talents.Find(t => t.tier == 1);
            Harness.Assert(s.LearnTalent(s.Main, t1.id) == null && s.Main.TalentRank(t1.id) == 1, "manual talent point");
            s.CloseRespec();
        }

        [Test]
        public static void Rest_Heal_Clock()
        {
            var s = SessionTest.NewGame(ClassId.Priest, 5, seed: 231);
            s.Recruit("kael");
            Harness.Assert(s.CannotRestReason() != null, "the village square is not a rest area");
            Harness.Assert(!s.LongRest(), "LongRest refused");
            foreach (var u in s.Party) { u.Health = 1; u.Mana = 0; }
            s.Main.Cooldowns["mage_x"] = 30f;
            float h = s.GameHour;
            s.Rest();   // innkeeper outcome
            foreach (var u in s.Party) Harness.Assert(u.Health == u.MaxHealth && u.Mana == u.MaxMana, $"{u.Name} restored");
            Harness.Assert(s.Main.Cooldowns.Count == 0, "cooldowns cleared");
            Harness.Assert(Math.Abs(s.GameHour - 8f) < 0.01f && s.Day == 2, $"morning of day 2 (hour {s.GameHour}, day {s.Day})");
            Harness.Assert(s.TimeOfDay == "day", "day time");
            Harness.Assert(SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.Rested) != null, "Rested event");
            // camp rest on a rest-area map
            s.EnterMap("whisperwood", "from_village");
            Harness.Assert(s.CannotRestReason() == null && s.LongRest(), "camp in Whisperwood");
            // clock: 1 game hour per real minute by default
            s.SetTime(16.5f);
            s.TakeEvents();
            s.Tick(60f);
            Harness.Assert(Math.Abs(s.GameHour - 17.5f) < 0.01f, "one hour later");
            Harness.Assert(s.TimeOfDay == "dusk" && SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.TimeOfDayChanged) != null, "dusk + event");
            // regeneration out of combat
            s.Main.Health = 1;
            s.Tick(20f);
            Harness.Assert(s.Main.Health > 1, "regenerates out of combat");
            // heal outcome
            s.Main.Health = 1;
            s.HealParty();
            Harness.Assert(s.Main.Health == s.Main.MaxHealth, "HealParty");
            // the shrine has a fixed dusk
            s.EnterMap("shrine", "from_whisperwood");
            s.SetTime(12f);
            Harness.Assert(s.TimeOfDay == "dusk", "fixed time of day on the shrine map");
        }

        [Test]
        public static void FoodAndDrink_OutOfCombat()
        {
            var s = SessionTest.NewGame(ClassId.Mage, 1, seed: 241);
            var u = s.Main;
            u.Health = 5f;
            u.Mana = 1f;
            var food = s.Inventory.Find("food_rice_ball");
            int n = s.CountItem("food_rice_ball");
            var r = s.UseItem(u, food);
            Harness.Assert(r.Ok, "eat: " + r.Reason);
            Harness.Assert(s.CountItem("food_rice_ball") == n - 1, "consumed");
            Harness.Assert(u.HasAuraWithTag("Food"), "food aura");
            int foodTicks = 0;
            s.CombatEventRaised += e => { if (e.Type == CombatEventType.Heal && e.Periodic && e.Target == u && e.Reason != "regen") foodTicks++; };
            float before = u.Health;
            s.Tick(3.01f);
            Harness.Assert(u.Health > before, "health restores over time");
            Harness.Assert(foodTicks >= 1, $"food ticked ({foodTicks})");
            // moving cancels eating
            s.UpdatePartyPositions(u.Position + new Vec2(0.5f, 0));
            Harness.Assert(!u.HasAuraWithTag("Food"), "moving cancels food");
            var drink = s.Inventory.Find("drink_spring_water");
            Harness.Assert(s.UseItem(u, drink).Ok, "drink");
            float m = u.Mana;
            s.Tick(6.01f);
            Harness.Assert(u.Mana > m, "mana back");
        }

        [Test]
        public static void Chests_Locks_Travel()
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 20, seed: 251);
            // village chest: loot window
            var r = s.OpenChest("chest_village_oak");
            Harness.Assert(r.Ok && r.Kind == InteractKind.Loot, "open chest");
            Harness.Assert(s.PendingLoot != null && s.PendingLoot.Items.Count > 0, "loot window");
            int n = s.CountItem("potion_minor_healing");
            s.TakeAllLoot();
            Harness.Assert(s.PendingLoot == null && s.CountItem("potion_minor_healing") > n, "looted");
            Harness.Assert(!s.OpenChest("chest_village_oak").Ok, "empty now");
            // locked chest in Whisperwood
            var tr = s.UseTransition("to_whisperwood");
            Harness.Assert(tr.Ok && s.MapId == "whisperwood", "travel to Whisperwood");
            var lr = s.OpenChest("chest_bandit_strongbox");
            Harness.Assert(!lr.Ok && lr.Kind == InteractKind.Locked, "locked");
            Harness.Assert(s.Main.Knows(GameSession.PickLockAbility), "a level-20 rogue knows Pick Lock");
            CheckResult res = null;
            for (int i = 0; i < 30 && (res == null || !res.Success); i++) res = s.PickLock("chest_bandit_strongbox");
            Harness.Assert(res != null && res.Success && res.Bonus >= 4, "picked the lock (bonus includes level/5)");
            Harness.Assert(s.Map.IsChestOpened("chest_bandit_strongbox") && s.PendingLoot != null, "opened after the lock");
            s.CloseLoot(true);
            // a locked transition
            var locked = s.UseTransition("to_shrine");
            Harness.Assert(!locked.Ok && locked.Kind == InteractKind.Locked && locked.Message.Length > 0, "shrine stair blocked");
            // walking into the transition rectangle
            var back = s.Map.Def.transitions.Find(t => t.id == "to_lanternvale");
            var w = s.WalkTo_(back.pos);
            Harness.Assert(s.MapId == "lanternvale", $"walked back to the village ({w})");
            Harness.Assert(Vec2.Distance(s.Leader.Position, s.Map.SpawnPosition("from_whisperwood")) < 4f, "arrived at the spawn");
        }

        static TriggerResult WalkTo_(this GameSession s, Vec2 p) => SessionTest.WalkTo(s, p);
    }
}
