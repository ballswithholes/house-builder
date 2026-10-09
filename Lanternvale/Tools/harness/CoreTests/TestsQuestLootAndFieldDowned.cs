// Regression tests: leaving a loot window never throws quest items away (the Spirit Embers of the main quest come
// only from four wisps), and nothing outside combat leaves a party character downed (Hellfire's self-damage while
// exploring soft-locked a solo warlock; a save of that state loaded as a 0-health "alive" character).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsQuestLootAndFieldDowned
    {
        static bool IsQuest(ItemInstance it) => it != null && (it.Def.kind == ItemKind.Quest || !string.IsNullOrEmpty(it.Def.quest));

        static int CountIn(List<ItemInstance> items, string id)
        {
            int n = 0;
            foreach (var it in items) if (it != null && it.Id == id) n += it.Count;
            return n;
        }

        static void Weaken(Battle b)
        {
            foreach (var u in b.Units) if (u.Team != b.PlayerTeam && u.IsAlive) u.Health = 1f;
        }

        // ------------------------------------------------------------------------------------------ loot window

        [Test]
        public static void CloseLoot_LeavingTheItems_KeepsQuestItems()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 20, seed: 141);
            s.EnterMap("whisperwood", "from_village");
            var b = s.StartEncounter("enc_wisps_grove");
            Assert(b != null, "the wisp fight starts: " + s.LastError);
            Weaken(b);   // the wisps scale to the party: a lone warrior only needs to win, not to win fairly
            var outcome = s.AutoResolve(80);
            Assert(outcome == BattleOutcome.Victory, $"the wisps are beaten ({outcome}, round {b.Round}, started {b.Started}, party {SessionTest.PartyHp(s)})");
            s.FinishBattle();
            Assert(s.PendingLoot != null, "the wisps leave loot");
            // a non-quest item in the window too: that one is still left behind
            var ash = Db.Item("junk_grey_ash");
            Assert(ash != null && !IsQuest(new ItemInstance(ash)), "junk_grey_ash is an ordinary item");
            s.PendingLoot.Items.Add(new ItemInstance(ash));

            int embersInWindow = CountIn(s.PendingLoot.Items, "spirit_ember");
            int ashInWindow = CountIn(s.PendingLoot.Items, "junk_grey_ash");
            Assert(embersInWindow >= 2, $"each wisp drops a spirit ember ({embersInWindow})");
            Assert(IsQuest(s.PendingLoot.Items.Find(it => it.Id == "spirit_ember")), "spirit_ember is a quest item");
            int embersBefore = s.CountItem("spirit_ember"), ashBefore = s.CountItem("junk_grey_ash");

            s.TakeEvents();
            s.CloseLoot(false);   // the loot window's Close → "Leave them"
            Assert(s.PendingLoot == null, "the window is closed");
            Assert(s.CountItem("spirit_ember") == embersBefore + embersInWindow,
                $"the embers went into the bags ({s.CountItem("spirit_ember")} of {embersBefore + embersInWindow})");
            Assert(s.CountItem("junk_grey_ash") == ashBefore, "ordinary items left behind are lost");
            Assert(ashInWindow > 0, "the junk was in the window");
            var events = s.TakeEvents();
            Assert(SessionTest.FindEvent(events, SessionEventKind.ItemReceived, "spirit_ember") != null, "the kept embers are announced");
            Assert(SessionTest.FindEvent(events, SessionEventKind.LootClosed) != null, "LootClosed raised");

            // taking everything still takes everything
            var s2 = SessionTest.NewGame(ClassId.Warrior, 20, seed: 141);
            s2.EnterMap("whisperwood", "from_village");
            var b2 = s2.StartEncounter("enc_wisps_grove");
            Assert(b2 != null, "second wisp fight starts: " + s2.LastError);
            Weaken(b2);
            Assert(s2.AutoResolve(80) == BattleOutcome.Victory, "second wisp fight won");
            s2.FinishBattle();
            Assert(s2.PendingLoot != null, "loot again");
            s2.PendingLoot.Items.Add(new ItemInstance(ash));
            int e2 = CountIn(s2.PendingLoot.Items, "spirit_ember"), a2 = CountIn(s2.PendingLoot.Items, "junk_grey_ash");
            int ashBefore2 = s2.CountItem("junk_grey_ash"), embersBefore2 = s2.CountItem("spirit_ember");
            s2.CloseLoot(true);
            Assert(s2.PendingLoot == null && s2.CountItem("spirit_ember") == embersBefore2 + e2 && s2.CountItem("junk_grey_ash") == ashBefore2 + a2,
                "CloseLoot(true) takes everything");
        }

        // ------------------------------------------------------------------------------------------ downed in the field

        [Test]
        public static void FieldSelfDamage_NeverDownsAPartyCharacter()
        {
            foreach (int level in new[] { 30, 45 })
            {
                var s = SessionTest.NewGame(ClassId.Warlock, level, seed: 151);
                var w = s.Main;
                Assert(w.Knows("warlock_hellfire"), $"L{level} warlock knows Hellfire");
                Assert(s.Mode == SessionMode.Exploration && s.Battle == null, "exploring");
                for (int i = 0; i < 4; i++)
                {
                    w.Mana = w.MaxMana;
                    var r = s.UseAbility(w, "warlock_hellfire");
                    Assert(r.Ok, $"L{level} Hellfire #{i + 1} cast while exploring: {r.Reason}");
                    Assert(!w.Downed && w.IsAlive && w.Health >= 1f, $"L{level} Hellfire #{i + 1}: still standing (hp {w.Health}, downed {w.Downed})");
                }
                Assert(w.Health < 2f, $"L{level}: the self-damage stops at 1 health (hp {w.Health})");
                Assert(s.Mode == SessionMode.Exploration, "no game over, still exploring");
                s.Tick(5f);
                Assert(w.IsAlive && w.Health > 1f, $"L{level}: health regenerates afterwards (hp {w.Health})");
            }
        }

        [Test]
        public static void DownedOutsideCombat_StandsBackUp_AndNeverLoadsAsZeroHealthAlive()
        {
            var s = SessionTest.NewGame(ClassId.Warlock, 30, seed: 152);
            var w = s.Main;
            Assert(s.Mode == SessionMode.Exploration, "exploring");

            // a character somehow downed outside combat (scripted kill, older code) gets back up and regenerates
            s.Tick(0.1f);   // the field context exists
            Assert(s.Field != null && s.Field.Units.Contains(w), "the warlock is in the field context");
            s.Field.KillUnit(w);
            Assert(w.Downed && !w.IsAlive && w.Health <= 0f, "downed by a scripted kill outside combat");
            s.Tick(1f);
            Assert(!w.Downed && w.IsAlive && w.Health >= 1f, $"stands back up out of combat (hp {w.Health}, downed {w.Downed})");
            float hp = w.Health;
            s.Tick(5f);
            Assert(w.Health > hp, "and regenerates");

            // a save taken at 0 health (downed or not) loads as a standing character with at least 1 health
            w.Health = 0f;
            w.Downed = true;
            Assert(s.CannotSaveReason() == null, "saving is allowed while exploring: " + s.CannotSaveReason());
            var json = s.SaveGame();
            var s2 = new GameSession(Db, 999);
            Assert(s2.LoadGame(json, out var err), "load: " + err);
            Assert(s2.Main.IsAlive && !s2.Main.Downed && s2.Main.Health >= 1f, $"loaded standing with health (hp {s2.Main.Health}, alive {s2.Main.IsAlive})");
            s2.Tick(2f);
            Assert(s2.Main.IsAlive && s2.Main.Health > 1f, $"and regenerates after the load (hp {s2.Main.Health})");
        }
    }
}
