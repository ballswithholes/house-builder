// GameSession: encounters -> battles, training dummy (leave + reset), wolves (victory, XP, loot, quest kills),
// encounter triggers (stealth rule), encounter dialogues (peaceful / fight), defeat -> game over.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsSessionCombat
    {
        static GameSession StrongParty(ClassId main, int level, ulong seed)
        {
            var s = SessionTest.NewGame(main, level, seed);
            s.Recruit("kael");
            s.Recruit("seren");
            s.Recruit("rook");
            s.TakeEvents();
            return s;
        }

        [Test]
        public static void TrainingDummy_LeaveCombat_And_FightAgain()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 1, seed: 61);
            Harness.Assert(s.CannotLeaveCombatReason() != null, "not in combat");
            var b = s.StartEncounter("enc_training_dummy");
            Harness.Assert(b != null && s.Mode == SessionMode.Combat && s.BattleEncounter.id == "enc_training_dummy", "dummy fight started: " + s.LastError);
            Harness.Assert(s.CannotSaveReason() != null, "no saving in combat");
            Harness.Assert(s.IsPracticeEncounter(s.BattleEncounter), "practice encounter");
            // let the AI hit it for a few rounds
            for (int i = 0; i < 12 && !b.IsOver; i++)
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                AI.RunTurn(b, u);
                if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
            }
            Harness.Assert(!b.IsOver, "the dummy does not die quickly");
            Harness.Assert(s.CannotLeaveCombatReason() == null, "may leave: every hostile is passive");
            var sum = s.LeaveCombat();
            Harness.Assert(sum != null && sum.Outcome == CombatEndKind.Left, "left the fight");
            Harness.Assert(s.Mode == SessionMode.Exploration && s.Battle == null, "exploring again");
            Harness.Assert(!s.Map.IsEncounterDone("enc_training_dummy") && !s.Flags.IsSet("training_dummy_done"), "dummy not marked done");
            Harness.Assert(s.Main.IsAlive && s.Main.Health >= 1, "warrior fine");
            var ev = s.TakeEvents();
            var ce = SessionTest.FindEvent(ev, SessionEventKind.CombatEnded);
            Harness.Assert(ce != null && ce.Outcome == CombatEndKind.Left, "CombatEnded(Left) event");
            // again, via Sir Odo
            s.TalkTo("trainer_odo");
            Harness.Assert(s.Mode == SessionMode.Dialogue, "talking to Odo");
            SessionTest.Pick(s, "training dummy");
            SessionTest.Finish(s);
            Harness.Assert(s.Mode == SessionMode.Combat && s.BattleEncounter?.id == "enc_training_dummy", "Odo's StartCombat outcome starts the dummy fight after the dialogue");
            Harness.Assert(s.LeaveCombat() != null, "leave again");
            // a done dummy is reset by ResetEncounter
            s.Flags.Set("training_dummy_done");
            Harness.Assert(s.StartEncounter("enc_training_dummy") == null, "a done encounter never starts");
            s.StartCombat("enc_training_dummy");
            Harness.Assert(s.Battle == null, "StartCombat outcome ignores done encounters");
            s.ResetEncounter("enc_training_dummy");
            Harness.Assert(s.StartEncounter("enc_training_dummy") != null, "reset dummy can be fought again");
            s.LeaveCombat();
        }

        [Test]
        public static void Wolves_Victory_Xp_Loot_QuestKills()
        {
            var s = StrongParty(ClassId.Warrior, 8, 71);
            s.Flags.Set("shepherd_quest");
            s.Quests.Start("sq_shepherd");
            int xp0 = s.Main.Xp, lvl0 = s.Main.Level;
            var b = s.StartEncounter("enc_pasture_wolves");
            Harness.Assert(b != null, "wolves: " + s.LastError);
            Harness.Assert(b.Pathfinder is NavGridPathfinder, "battle uses the NavGrid pathfinder");
            int enemies = 0;
            foreach (var u in b.Units)
                if (u.Team == Team.Enemy)
                {
                    enemies++;
                    Harness.Assert(u.Level == Math.Max(1, s.PartyLevel - 1), $"wolf scaled to party level - 1 (got {u.Level})");
                }
            Harness.Assert(enemies == 3, "three wolves");
            var ev0 = s.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(ev0, SessionEventKind.CombatStarted, "enc_pasture_wolves") != null, "CombatStarted event");
            var sum = SessionTest.WinBattle(s);
            Harness.Assert(sum.Killed.Count == 3, "three kills");
            Harness.Assert(sum.Xp > 0 && (s.Main.Xp > xp0 || s.Main.Level > lvl0), "xp gained");
            Harness.Assert(s.Flags.IsSet("pasture_wolves_done") && s.Map.IsEncounterDone("enc_pasture_wolves"), "done flag set");
            Harness.Assert(s.Quests.GetStage("sq_shepherd") == "report", "kill objective advanced the quest");
            Harness.Assert(s.StartEncounter("enc_pasture_wolves") == null, "won encounter does not restart");
            foreach (var u in s.Party) Harness.Assert(u.IsAlive && u.Health >= 1f, $"{u.Name} standing after victory");
            foreach (var e in s.VisibleEncounters()) Harness.Assert(e.id != "enc_pasture_wolves", "won encounter no longer visible");
            Harness.Assert(s.Mode == SessionMode.Exploration && s.Field != null, "back to exploring");
        }

        [Test]
        public static void WalkingIntoEncounter_Triggers_StealthRule()
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 10, seed: 81);
            s.EnterMap("whisperwood", "from_village");
            var enc = s.Map.FindEncounter("enc_forest_wolves");
            var r = s.UseAbility(s.Main, "rogue_stealth");
            Harness.Assert(r.Ok && s.Main.IsStealthed, "stealthed: " + r.Reason);
            // stand 4 m away (inside the 3 m radius? no: radius 3 → normal party would trigger at 3 m; stealth at min(3,3))
            var near = enc.pos + new Vec2(0, 2.5f);
            near = s.Nav.ClampToWalkable(near, NavAgent.Default.IgnoringAllUnits());
            float d = Vec2.Distance(near, enc.pos);
            var tr = s.UpdatePartyPositions(near);
            if (d <= Math.Min(enc.radius, 3f)) Harness.Assert(tr.Stop && tr.Kind == TriggerKind.Combat, "inside 3 m triggers even when stealthed");
            else Harness.Assert(!tr.Stop, "stealthed outside 3 m does not trigger");
            if (s.Battle != null) { s.AutoResolve(); s.FinishBattle(); if (s.PendingLoot != null) s.CloseLoot(true); }

            // boars: radius 3; a non-stealthed party triggers at 2.9 m, a stealthed one does not at 3.5 m of a 4 m-radius encounter
            var s2 = SessionTest.NewGame(ClassId.Warrior, 10, seed: 82);
            s2.EnterMap("whisperwood", "from_village");
            var boars = s2.Map.FindEncounter("enc_boars_edge");
            var p = s2.Nav.ClampToWalkable(boars.pos + new Vec2(-boars.radius + 0.3f, 0f), NavAgent.Default.IgnoringAllUnits());
            Harness.Assert(Vec2.Distance(p, boars.pos) <= boars.radius, "test point inside the radius");
            var t2 = s2.UpdatePartyPositions(p);
            Harness.Assert(t2.Stop && t2.Kind == TriggerKind.Combat && s2.BattleEncounter.id == "enc_boars_edge", "walking in triggers combat");
        }

        [Test]
        public static void EncounterDialogue_PeacefulAndFight()
        {
            // peaceful: the lookouts' "let's see Rusk" branch sets the done flag, no combat
            var s = StrongParty(ClassId.Paladin, 10, 91);
            s.EnterMap("whisperwood", "from_village");
            var enc = s.Map.FindEncounter("enc_bandit_lookouts");
            var tr = s.UpdatePartyPositions(s.Nav.ClampToWalkable(enc.pos + new Vec2(-1.5f, 0), NavAgent.Default.IgnoringAllUnits()));
            Harness.Assert(tr.Stop && tr.Kind == TriggerKind.Dialogue && s.Dialogue.Dialogue.id == "dlg_bandit_lookouts", "encounter dialogue plays first");
            SessionTest.Pick(s, "see Rusk");
            SessionTest.Finish(s);
            Harness.Assert(s.Battle == null && s.Flags.IsSet("lookouts_done") && s.Map.IsEncounterDone("enc_bandit_lookouts"), "peaceful: done, no fight");
            Harness.Assert(!s.UpdatePartyPositions(s.Leader.Position + new Vec2(0.2f, 0)).Stop, "no retrigger");

            // fight: "Get out of our way." → StartCombat after the dialogue closes
            var s2 = StrongParty(ClassId.Paladin, 10, 92);
            s2.EnterMap("whisperwood", "from_village");
            var tr2 = s2.UpdatePartyPositions(s2.Nav.ClampToWalkable(enc.pos + new Vec2(-1.5f, 0), NavAgent.Default.IgnoringAllUnits()));
            Harness.Assert(tr2.Kind == TriggerKind.Dialogue, "dialogue first");
            SessionTest.Pick(s2, "Get out of our way");
            SessionTest.Finish(s2);
            Harness.Assert(s2.Mode == SessionMode.Combat && s2.BattleEncounter.id == "enc_bandit_lookouts", "fight branch starts the encounter");
            SessionTest.WinBattle(s2);
            Harness.Assert(s2.Flags.IsSet("lookouts_done"), "won: done flag");

            // walking away from an unresolved encounter dialogue: no immediate retrigger
            var s3 = StrongParty(ClassId.Paladin, 10, 93);
            s3.EnterMap("whisperwood", "from_village");
            var camp = s3.Map.FindEncounter("enc_mossling_camp");
            var at = s3.Nav.ClampToWalkable(camp.pos + new Vec2(-2f, 0), NavAgent.Default.IgnoringAllUnits());
            var t3 = s3.UpdatePartyPositions(at);
            Harness.Assert(t3.Kind == TriggerKind.Dialogue && s3.Dialogue.Dialogue.id == "dlg_puddlecap", "puddlecap talks first");
            s3.EndDialogue();
            if (s3.Battle == null && !s3.Map.IsEncounterDone(camp))
            {
                Harness.Assert(!s3.UpdatePartyPositions(at + new Vec2(0.1f, 0)).Stop, "suppressed while still inside");
                var away = s3.UpdatePartyPositions(camp.pos + new Vec2(-camp.radius - 8f, -2f));   // followers trail behind the leader
                Harness.Assert(!away.Stop, "nothing at the retreat point");
                var again = s3.UpdatePartyPositions(at);
                Harness.Assert(again.Stop, "re-triggers after leaving the radius");
            }
        }

        [Test]
        public static void Defeat_GameOver()
        {
            var s = SessionTest.NewGame(ClassId.Mage, 1, seed: 101);
            s.EnterMap("whisperwood", "from_village");
            var b = s.StartEncounter("enc_forest_wolves");
            Harness.Assert(b != null, "fight");
            s.Main.Health = 1f;
            var oc = s.AutoResolve(40);
            if (oc == BattleOutcome.Defeat)
            {
                var sum = s.FinishBattle();
                Harness.Assert(sum.Outcome == CombatEndKind.Defeat && s.Mode == SessionMode.GameOver && s.IsGameOver, "game over");
                Harness.Assert(s.CannotSaveReason() != null, "cannot save a lost game");
                Harness.Assert(SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.GameOver) != null, "GameOver event");
            }
            else Harness.Assert(oc == BattleOutcome.Victory, "a lone level-1 mage at 1 HP should lose to three wolves");
        }

        [Test]
        public static void Combat_StepApi_And_ItemRules()
        {
            var s = StrongParty(ClassId.Priest, 6, 111);
            var food = s.Inventory.Find("food_rice_ball");
            var potion = s.Inventory.Find("potion_minor_healing");
            Harness.Assert(food != null && potion != null, "starting consumables");
            var b = s.StartEncounter("enc_training_dummy");
            Harness.Assert(b != null, "battle");
            int guard = 0;
            while (!s.IsPlayerTurn && !b.IsOver && guard++ < 200) Harness.Assert(s.RunAIStep() != null, "AI step for AI units");
            Harness.Assert(s.IsPlayerTurn && s.ActiveUnit == s.Main, "the priest's turn waits for input");
            Harness.Assert(s.RunAIStep() == null, "no AI step on the player's turn");
            var fr = s.UseItem(s.Main, food);
            Harness.Assert(!fr.Ok, "food cannot be eaten in combat");
            s.Main.Health = s.Main.MaxHealth * 0.5f;
            var pr = s.UseItem(s.Main, potion);
            Harness.Assert(pr.Ok, "potion in combat: " + pr.Reason);
            Harness.Assert(s.Main.Health > s.Main.MaxHealth * 0.5f, "potion healed");
            var other = s.Inventory.Find("potion_minor_healing");
            if (other != null) Harness.Assert(!s.UseItem(s.Main, other).Ok, "potion cooldown group");
            Harness.Assert(s.Equip(s.Main, s.Inventory.Items[0]) != null, "no gear changes in combat");
            b.EndTurn(s.Main);
            Harness.Assert(s.LeaveCombat() != null, "leave");
            Harness.Assert(s.Main.CooldownLeft(Harness.Db.Ability("use_potion_minor_healing")) > 0, "potion cooldown carried out of combat");
            s.Tick(200f);
            Harness.Assert(s.Main.CooldownLeft(Harness.Db.Ability("use_potion_minor_healing")) <= 0f, "cooldown recovers in real time");
        }
    }
}
