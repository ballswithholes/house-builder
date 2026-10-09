// DialogueRunner, WorldRules (conditions/outcomes) and SkillChecks tests with in-memory fixtures.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.WorldFixtures;

namespace Lanternvale.Tests
{
    public static class TestsWorldDialogue
    {
        static DialogueDef ElderDialogue()
        {
            var d = new DialogueDef { id = "elder_intro", start = "greet_done" };
            var done = Node("greet_done", "Thank you, {player}.");
            done.conditions.Add(Cond(ConditionType.QuestComplete, "q_wolves"));
            done.fallback = "greet_met";
            d.nodes.Add(done);
            var met = Node("greet_met", "Welcome back, {class}.", next: "ask");
            met.conditions.Add(Cond(ConditionType.Flag, "met_elder"));
            met.fallback = "greet_first";
            d.nodes.Add(met);
            var first = Node("greet_first", "Ah, a traveller.", next: "ask", speaker: "elder_maren");
            first.outcomes.Add(Out(OutcomeType.SetFlag, "met_elder"));
            d.nodes.Add(first);

            var ask = Node("ask", "What brings you here?");
            var pal = Choice("The Light guides me.", "paladin");
            pal.tag = "Paladin";
            pal.conditions.Add(Cond(ConditionType.Class, "Paladin"));
            pal.outcomes.Add(Out(OutcomeType.Approval, "lyra", amount: 2));
            ask.choices.Add(pal);
            var mage = Choice("I study the arcane.", "lore");
            mage.tag = "[mage]";
            mage.conditions.Add(Cond(ConditionType.Class, "Mage"));
            ask.choices.Add(mage);
            var lore = Choice("Tell me about the lanterns.", "lore");
            lore.once = true;
            ask.choices.Add(lore);
            var persuade = Choice("Pay me upfront.");
            persuade.check = new CheckDef { skill = SkillCheck.Persuasion, dc = 12, success = "paid", failure = "refused" };
            persuade.once = true;
            ask.choices.Add(persuade);
            var shop = Choice("Show me your wares.");
            shop.outcomes.Add(Out(OutcomeType.OpenVendor));
            ask.choices.Add(shop);
            ask.choices.Add(Choice("Goodbye."));
            d.nodes.Add(ask);

            d.nodes.Add(Node("paladin", "A paladin! {companion:lyra} will like you.", next: "ask"));
            var loreNode = Node("lore", "They are going dark, one by one.", next: "ask");
            loreNode.outcomes.Add(Out(OutcomeType.StartQuest, "q_lanterns"));
            d.nodes.Add(loreNode);
            var paid = Node("paid", "Fine, take it.");
            paid.outcomes.Add(Out(OutcomeType.GiveGold, amount: 100));
            d.nodes.Add(paid);
            d.nodes.Add(Node("refused", "Certainly not."));
            return d;
        }

        static (FakeWorldContext ctx, DialogueRunner runner) Setup(ulong seed = 1)
        {
            var db = Db();
            var d = ElderDialogue();
            db.Dialogues[d.id] = d;
            var ctx = new FakeWorldContext(db);
            var runner = ctx.World.CreateDialogueRunner(new Rng(seed));
            return (ctx, runner);
        }

        static int FindChoice(DialogueView v, string textStart)
        {
            foreach (var c in v.Choices) if (c.Text.StartsWith(textStart)) return c.Index;
            return -1;
        }

        [Test]
        public static void FlowFallbacksChoicesOnce()
        {
            var (ctx, runner) = Setup();
            var entered = new List<string>();
            string ended = null;
            runner.NodeEntered += v => entered.Add(v.NodeId);
            runner.Ended += id => ended = id;

            var prevWarn = Log.WarnHandler;
            Log.WarnHandler = _ => { };
            try { Assert(!runner.Start("nope"), "unknown dialogue"); }
            finally { Log.WarnHandler = prevWarn; }
            Assert(runner.Start("elder_intro", "elder_maren"), "start");
            var v = runner.Current;
            Assert(v.NodeId == "greet_first", $"fallback chain lands on greet_first ({v.NodeId})");
            Assert(v.SpeakerName == "Elder Maren" && v.Portrait == "portrait_maren", "speaker from npc def");
            Assert(ctx.Flags.IsSet("met_elder"), "node outcome ran on enter");
            Assert(v.CanContinue && !v.IsLast, "continue");
            Assert(runner.Continue(), "continue to ask");

            v = runner.Current;
            Assert(v.NodeId == "ask" && v.SpeakerId == "elder_maren", "empty speaker = owner");
            Assert(FindChoice(v, "I study") < 0, "mage option hidden for a paladin");
            int pal = FindChoice(v, "The Light");
            Assert(pal >= 0 && v.Choices[pal].DisplayText == "[PALADIN] The Light guides me.", $"class tag ({v.Choices[pal].DisplayText})");
            int per = FindChoice(v, "Pay me");
            Assert(v.Choices[per].DisplayText == "[PERSUASION] Pay me upfront.", "check tag");
            Assert(v.Choices[per].Check != null && v.Choices[per].Check.Dc == 12 && v.Choices[per].Check.RollerId == "player", "check preview");
            AssertNear(v.Choices[per].Check.SuccessChance, 0.45f, 1e-4f, "spirit 25 → +0 vs DC 12 → 45%");
            Assert(v.Choices[FindChoice(v, "Goodbye")].Ends, "goodbye ends");
            Assert(!runner.Continue(), "cannot continue while choices are shown");
            Assert(!runner.Choose(99), "bad index");

            Assert(runner.Choose(FindChoice(v, "Tell me")), "choose lore");
            Assert(runner.Current.NodeId == "lore" && ctx.Quests.IsActive("q_lanterns"), "StartQuest outcome");
            runner.Continue();
            v = runner.Current;
            Assert(FindChoice(v, "Tell me") < 0, "once choice hidden after use");

            Assert(runner.Choose(FindChoice(v, "The Light")), "choose paladin");
            Assert(runner.Current.Text == "A paladin! Lyra will like you.", $"companion token ({runner.Current.Text})");
            Assert(ctx.Approval["lyra"] == 2, "approval outcome");
            runner.Continue();
            Assert(runner.Current.Choices[FindChoice(runner.Current, "The Light")].PreviouslyChosen, "previously chosen flag");

            Assert(runner.Choose(FindChoice(runner.Current, "Goodbye")), "goodbye");
            Assert(runner.IsFinished && runner.Current == null && ended == "elder_intro", "ended");

            // second visit: greet_met with {class}
            runner.Start("elder_intro", "elder_maren");
            Assert(runner.Current.NodeId == "greet_met" && runner.Current.Text == "Welcome back, Paladin.", $"second greeting ({runner.Current.Text})");
            Assert(runner.Memory.TimesStarted("elder_intro") == 2, "times started");
            runner.End();
            Assert(!runner.IsActive, "End()");
        }

        [Test]
        public static void SkillCheckBranches()
        {
            bool sawSuccess = false, sawFailure = false;
            for (ulong seed = 1; seed < 60 && !(sawSuccess && sawFailure); seed++)
            {
                var (ctx, runner) = Setup(seed);
                CheckResult rolled = null;
                runner.CheckRolled += r => rolled = r;
                runner.Start("elder_intro", "elder_maren");
                runner.Continue();
                runner.Choose(FindChoice(runner.Current, "Pay me"));
                var r = runner.LastCheck;
                Assert(r != null && ReferenceEquals(r, rolled), "check result exposed and evented");
                Assert(r.RollerId == "player" && r.RollerName == "Tidus" && r.Dc == 12 && r.Skill == SkillCheck.Persuasion, "check details");
                Assert(r.Modifier == 0 && r.Total == r.Roll, "modifier 0");
                Assert(r.Success == (r.Roll == 20 || (r.Roll != 1 && r.Total >= 12)), "success rule");
                if (r.Success)
                {
                    sawSuccess = true;
                    Assert(runner.Current.NodeId == "paid", "success node");
                    Assert(ctx.GoldValue == 100, "success outcome");
                }
                else
                {
                    sawFailure = true;
                    Assert(runner.Current.NodeId == "refused", "failure node");
                }
                Assert(runner.Current.IsLast, "terminal node");
                runner.Continue();
                Assert(runner.IsFinished, "ended");
                // once check: cannot retry
                runner.Start("elder_intro", "elder_maren");
                runner.Continue();
                Assert(FindChoice(runner.Current, "Pay me") < 0, "failed/used check cannot be retried");
            }
            Assert(sawSuccess && sawFailure, "both branches observed with seeded rolls");
        }

        [Test]
        public static void SkillCheckMath()
        {
            var db = Db();
            var ctx = new FakeWorldContext(db);
            var tidus = ctx.Members[0];
            Assert(SkillChecks.StatModifier(45) == 2 && SkillChecks.StatModifier(20) == 0 && SkillChecks.StatModifier(15) == -1 && SkillChecks.StatModifier(29.9f) == 0, "stat modifier floors");
            Assert(SkillChecks.Modifier(tidus, SkillCheck.Athletics) == 4, "str 45 → +2, paladin athletics +2");
            Assert(SkillChecks.Modifier(tidus, SkillCheck.Arcana) == -1, "int 18 → -1, not proficient");
            Assert(SkillChecks.IsProficient(ClassId.Rogue, SkillCheck.SleightOfHand) && SkillChecks.IsProficient(ClassId.Shaman, SkillCheck.Insight)
                   && SkillChecks.IsProficient(ClassId.Warlock, SkillCheck.Intimidation) && !SkillChecks.IsProficient(ClassId.Mage, SkillCheck.Nature), "proficiency table");
            Assert(SkillChecks.StatFor(SkillCheck.Survival) == StatKind.Spirit && SkillChecks.StatFor(SkillCheck.Endurance) == StatKind.Stamina, "stat table");
            Assert(SkillChecks.DisplayName(SkillCheck.SleightOfHand) == "SLEIGHT OF HAND", "display name");
            AssertNear(SkillChecks.SuccessChance(0, 12), 0.45f, 1e-5f, "45%");
            AssertNear(SkillChecks.SuccessChance(10, 5), 0.95f, 1e-5f, "nat 1 cap");
            AssertNear(SkillChecks.SuccessChance(-5, 30), 0.05f, 1e-5f, "nat 20 floor");

            // best roller: Lyra the mage (int 60 → +4, proficient → +6) beats Tidus at Arcana
            ctx.Members.Add(new PartyMemberInfo("lyra", "Lyra", ClassId.Mage, 5, false, new PrimaryStats { intellect = 60, spirit = 30 }));
            var pv = SkillChecks.Preview(ctx, SkillCheck.Arcana, 15);
            Assert(pv.RollerId == "lyra" && pv.Modifier == 6, "best party member rolls");
            ctx.Bonus = 1;
            var res = SkillChecks.Roll(ctx, SkillCheck.Arcana, 15, new Rng(3));
            Assert(res.RollerId == "lyra" && res.StatModifier == 4 && res.Proficiency == 2 && res.Bonus == 1 && res.Modifier == 7 && res.Total == res.Roll + 7, "breakdown");
            ctx.Bonus = 0;

            // natural 20 / natural 1
            ulong nat20 = 0, nat1 = 0;
            for (ulong s = 1; s < 2000 && (nat20 == 0 || nat1 == 0); s++)
            {
                int roll = new Rng(s).D20();
                if (roll == 20 && nat20 == 0) nat20 = s;
                if (roll == 1 && nat1 == 0) nat1 = s;
            }
            Assert(nat20 != 0 && nat1 != 0, "found seeds");
            var crit = SkillChecks.Roll(ctx, SkillCheck.Arcana, 40, new Rng(nat20));
            Assert(crit.Roll == 20 && crit.Critical && crit.Success, "natural 20 beats DC 40");
            var fumble = SkillChecks.Roll(ctx, SkillCheck.Arcana, -10, new Rng(nat1));
            Assert(fumble.Roll == 1 && fumble.Fumble && !fumble.Success, "natural 1 fails DC -10");
            Assert(crit.ToString().Contains("critical"), "ToString");

            // empty party: still rolls with +0
            ctx.Members.Clear();
            var bare = SkillChecks.Roll(ctx, SkillCheck.Stealth, 10, new Rng(5));
            Assert(bare.RollerId == "" && bare.Modifier == 0, "empty party");
        }

        [Test]
        public static void DeferredAndEndOutcomes()
        {
            var db = Db();
            var d = new DialogueDef { id = "ambush", start = "a" };
            var a = Node("a", "Hand over your gold!", speaker: "narrator");
            var fight = Choice("Never!", "b");
            fight.outcomes.Add(Out(OutcomeType.SetFlag, "defied_bandits"));
            a.choices.Add(fight);
            var pay = Choice("Fine. [Pay 50 copper]", "never_shown");
            pay.conditions.Add(Cond(ConditionType.Gold, amount: 50));
            pay.outcomes.Add(Out(OutcomeType.TakeGold, amount: 50));
            pay.outcomes.Add(Out(OutcomeType.EndDialogue));
            a.choices.Add(pay);
            d.nodes.Add(a);
            var b = Node("b", "Then we take it from your corpse!", next: "c");
            b.outcomes.Add(Out(OutcomeType.StartCombat, "bandits"));
            d.nodes.Add(b);
            var c = Node("c", "");
            c.outcomes.Add(Out(OutcomeType.Teleport, "whisperwood"));
            c.outcomes.Add(Out(OutcomeType.SetFlag, "router_ran", amount: 3));
            d.nodes.Add(c);
            db.Dialogues[d.id] = d;

            var ctx = new FakeWorldContext(db);
            var runner = ctx.World.CreateDialogueRunner(new Rng(1));
            runner.Start("ambush");
            Assert(runner.Current.SpeakerName == "" && runner.Current.SpeakerId == "narrator", "narrator");
            Assert(runner.Current.Choices.Count == 1, "pay option needs 50 copper");
            runner.Choose(0);
            Assert(ctx.Flags.IsSet("defied_bandits"), "choice outcome");
            Assert(!ctx.Log.Contains("StartCombat bandits"), "combat deferred");
            Assert(runner.PendingOutcomes.Count == 1, "one pending");
            Assert(runner.Current.NodeId == "b" && !runner.Current.IsLast, "node b shown");
            int combatIndex = -1;
            runner.Ended += _ => combatIndex = ctx.Log.IndexOf("StartCombat bandits");
            runner.Continue(); // → router node c (empty text) runs, then ends
            Assert(runner.IsFinished, "ended after router");
            Assert(ctx.Flags.Get("router_ran") == 3, "router outcomes ran");
            Assert(combatIndex >= 0 && ctx.Log.Contains("Teleport whisperwood default"), "deferred outcomes ran before Ended");

            // EndDialogue in a choice ends immediately (next ignored); immediate mode
            ctx.Log.Clear();
            ctx.GoldValue = 80;
            runner.DeferInterruptingOutcomes = false;
            runner.Start("ambush");
            Assert(runner.Current.Choices.Count == 2, "can pay now");
            runner.Choose(1);
            Assert(runner.IsFinished && ctx.GoldValue == 30, "paid and ended");

            // EndDialogue on a node: shows text, no choices, Continue ends
            var d2 = new DialogueDef { id = "bye", start = "n" };
            var n = Node("n", "Leave me.", next: "m");
            n.outcomes.Add(Out(OutcomeType.EndDialogue));
            n.choices.Add(Choice("But..."));
            d2.nodes.Add(n);
            d2.nodes.Add(Node("m", "never"));
            runner.Start(d2);
            Assert(runner.Current.Text == "Leave me." && runner.Current.Choices.Count == 0 && runner.Current.IsLast, "end node");
            runner.Continue();
            Assert(runner.IsFinished, "ended");

            // broken data: missing node / failed conditions without fallback / infinite router loop
            var d3 = new DialogueDef { id = "broken", start = "x" };
            d3.nodes.Add(Node("x", "", next: "y"));
            d3.nodes.Add(Node("y", "", next: "x"));
            var prevWarn = Log.WarnHandler;
            Log.WarnHandler = _ => { };
            try
            {
                runner.Start(d3);
                Assert(runner.IsFinished, "router loop terminates");
                runner.Start(new DialogueDef { id = "missing", start = "nope" });
                Assert(runner.IsFinished, "missing start node ends");
            }
            finally { Log.WarnHandler = prevWarn; }
        }

        [Test]
        public static void TalkObjectiveOnEnd()
        {
            var db = Db();
            var d = new DialogueDef { id = "brann", start = "hi" };
            d.nodes.Add(Node("hi", "Back already?"));
            db.Dialogues[d.id] = d;
            var ctx = new FakeWorldContext(db);
            ctx.Quests.SetStage("q_wolves", "return");
            Assert(ctx.Quests.GetStage("q_wolves") == "return", "on the talk stage");
            var runner = ctx.World.CreateDialogueRunner(new Rng(1));
            runner.Start("brann", "hunter_brann");
            Assert(ctx.Quests.IsActive("q_wolves"), "still active during the conversation");
            runner.Continue();
            Assert(ctx.Quests.IsCompleted("q_wolves"), "talk objective completed when the dialogue ended");
        }

        [Test]
        public static void ConditionsAndOutcomes()
        {
            var db = Db();
            var ctx = new FakeWorldContext(db);
            bool C(ConditionType t, string key = "", string value = "", int amount = 0) => WorldRules.Check(Cond(t, key, value, amount), ctx);

            ctx.Flags.Set("lanterns_lit", 2);
            Assert(C(ConditionType.Flag, "lanterns_lit") && C(ConditionType.Flag, "lanterns_lit", amount: 2) && !C(ConditionType.Flag, "lanterns_lit", amount: 3), "Flag amount");
            Assert(C(ConditionType.Flag, "lanterns_lit", "2") && !C(ConditionType.Flag, "lanterns_lit", "1"), "Flag exact value");
            Assert(C(ConditionType.NotFlag, "nope") && !C(ConditionType.NotFlag, "lanterns_lit"), "NotFlag");

            Assert(C(ConditionType.QuestNotStarted, "q_wolves") && C(ConditionType.QuestState, "q_wolves", "NotStarted"), "not started");
            ctx.Quests.Start("q_wolves");
            Assert(C(ConditionType.QuestActive, "q_wolves") && C(ConditionType.QuestActive, "q_wolves", "hunt") && !C(ConditionType.QuestActive, "q_wolves", "pelts"), "QuestActive + stage");
            Assert(C(ConditionType.QuestState, "q_wolves", "active") && C(ConditionType.QuestState, "q_wolves", "hunt") && C(ConditionType.QuestState, "q_wolves"), "QuestState status/stage");
            Assert(!C(ConditionType.QuestComplete, "q_wolves"), "not complete");
            ctx.Quests.Complete("q_wolves");
            Assert(C(ConditionType.QuestComplete, "q_wolves") && C(ConditionType.QuestState, "q_wolves", "Completed") && C(ConditionType.QuestState, "q_wolves", "done"), "complete");

            ctx.Items["wolf_pelt"] = 2;
            Assert(C(ConditionType.HasItem, "wolf_pelt") && C(ConditionType.HasItem, "wolf_pelt", amount: 2) && !C(ConditionType.HasItem, "wolf_pelt", amount: 3), "HasItem");
            Assert(C(ConditionType.NotHasItem, "lantern_key") && !C(ConditionType.NotHasItem, "wolf_pelt"), "NotHasItem");
            ctx.GoldValue = 500;
            Assert(C(ConditionType.Gold, amount: 500) && !C(ConditionType.Gold, amount: 501), "Gold");
            Assert(C(ConditionType.Class, "paladin") && !C(ConditionType.Class, "Mage") && C(ConditionType.NotClass, "Mage"), "Class (main)");
            Assert(!C(ConditionType.Class, "Mage", "party"), "no mage in party yet");
            ctx.Members.Add(new PartyMemberInfo("lyra", "Lyra", ClassId.Mage, 6, false));
            Assert(C(ConditionType.Class, "Mage", "party") && !C(ConditionType.Class, "Mage"), "Class party vs main");
            Assert(C(ConditionType.Level, amount: 5) && !C(ConditionType.Level, amount: 6), "Level uses the main character");
            Assert(C(ConditionType.InParty, "lyra") && C(ConditionType.NotInParty, "brann"), "InParty");
            ctx.Approval["lyra"] = 10;
            Assert(C(ConditionType.Companion, "lyra", amount: 10) && !C(ConditionType.Companion, "lyra", amount: 11), "Companion approval");
            ctx.Time = "dusk";
            Assert(C(ConditionType.TimeOfDay, "dusk") && C(ConditionType.TimeOfDay, value: "night,dusk") && !C(ConditionType.TimeOfDay, "day"), "TimeOfDay");
            Assert(WorldRules.CheckAll(null, ctx) && WorldRules.CheckAll(new List<ConditionDef>(), ctx), "empty list");

            void O(OutcomeType t, string key = "", string value = "", int amount = 0) => WorldRules.Execute(Out(t, key, value, amount), ctx, "elder_maren");
            O(OutcomeType.SetFlag, "a");
            O(OutcomeType.SetFlag, "b", amount: 4);
            O(OutcomeType.SetFlag, "c", "7");
            Assert(ctx.Flags.Get("a") == 1 && ctx.Flags.Get("b") == 4 && ctx.Flags.Get("c") == 7, "SetFlag variants");
            O(OutcomeType.ClearFlag, "a");
            Assert(!ctx.Flags.IsSet("a"), "ClearFlag");
            O(OutcomeType.GiveItem, "lantern_oil");
            O(OutcomeType.TakeItem, "wolf_pelt", amount: 2);
            Assert(ctx.CountItem("lantern_oil") >= 1 && ctx.CountItem("wolf_pelt") == 0, "items");
            int xp = ctx.Xp;
            O(OutcomeType.GiveXP, amount: 120);
            Assert(ctx.Xp == xp + 120, "xp");
            O(OutcomeType.Recruit, "lyra");
            Assert(ctx.Flags.IsSet("recruited_lyra") && ctx.Log.Contains("Recruit lyra"), "Recruit sets recruited_ flag");
            O(OutcomeType.OpenVendor);
            O(OutcomeType.OpenTrainer, "hunter_brann");
            Assert(ctx.Log.Contains("OpenVendor elder_maren") && ctx.Log.Contains("OpenTrainer hunter_brann"), "vendor/trainer npc");
            O(OutcomeType.Teleport, "shrine", "gate");
            O(OutcomeType.Special, "LightLantern", "north");
            O(OutcomeType.HealParty);
            O(OutcomeType.Rest);
            O(OutcomeType.Dismiss, "lyra");
            O(OutcomeType.OpenRespec);
            Assert(ctx.Log.Contains("Teleport shrine gate") && ctx.Log.Contains("Special LightLantern north") && ctx.Log.Contains("HealParty")
                   && ctx.Log.Contains("Rest") && ctx.Log.Contains("Dismiss lyra") && ctx.Log.Contains("OpenRespec elder_maren"), "misc outcomes");
            O(OutcomeType.FailQuest, "q_lanterns");
            Assert(ctx.Quests.GetStatus("q_lanterns") == QuestStatus.Failed, "FailQuest on a not-started quest");
        }

        [Test]
        public static void TextTokens()
        {
            var (ctx, runner) = Setup();
            Assert(runner.Substitute("Hello {player} the {class}.") == "Hello Tidus the Paladin.", "player/class");
            Assert(runner.Substitute("{companion:lyra} and {companion:ghost}") == "Lyra and ghost", "companion");
            Assert(runner.Substitute("{unknown} {open") == "{unknown} {open", "unknown tokens kept");
            Assert(runner.Substitute("") == "" && runner.Substitute(null) == "", "empty");
        }

        [Test]
        public static void FlagStoreBasics()
        {
            var f = new FlagStore();
            var changes = new List<string>();
            f.Changed += (k, o, n) => changes.Add($"{k}:{o}->{n}");
            f.Set("a");
            f.Set("a");
            f.Add("count", 2);
            f.Add("count", 1);
            f.Set("count", 0);
            Assert(f.IsSet("a") && !f.IsSet("count") && f.Count == 1, "values");
            Assert(string.Join(",", changes) == "a:0->1,count:0->2,count:2->3,count:3->0", string.Join(",", changes));
            Assert(f.Test("") && f.Test("a") && !f.Test("!a") && f.Test("!b") && f.Test("a & !b") && !f.Test("a&b"), "Test expressions");
            f.Set("", 5);
            Assert(f.Get("") == 0 && f.Get(null) == 0, "empty key ignored");
            f.ClearAll();
            Assert(f.Count == 0, "cleared");
        }
    }
}
