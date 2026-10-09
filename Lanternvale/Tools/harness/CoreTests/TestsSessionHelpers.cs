// Helpers for the GameSession tests (TestsSession*.cs): new games, dialogue navigation, battles, walking.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class SessionTest
    {
        public static GameSession NewGame(ClassId c, int level = 1, ulong seed = 7, bool opening = false, bool veteranGear = true, string name = "Tester")
        {
            var s = new GameSession(Harness.Db, seed);
            s.NewGame(new NewGameOptions { Name = name, Class = c, StartLevel = level, PlayOpening = opening, VeteranGear = veteranGear });
            return s;
        }

        /// <summary>Index of the first visible choice whose text contains <paramref name="text"/> (case-insensitive), or -1.</summary>
        public static int ChoiceIndex(DialogueView v, string text)
        {
            if (v == null) return -1;
            foreach (var c in v.Choices)
                if (c.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return c.Index;
            return -1;
        }

        /// <summary>Continues through text-only nodes until a node with choices (or the end).</summary>
        public static void SkipText(GameSession s, int max = 50)
        {
            for (int i = 0; i < max && s.Dialogue.IsActive && s.Dialogue.Current != null && s.Dialogue.Current.CanContinue; i++)
                s.ContinueDialogue();
        }

        /// <summary>Skips text nodes, then picks the choice containing <paramref name="text"/> (asserts it exists).</summary>
        public static void Pick(GameSession s, string text)
        {
            SkipText(s);
            Harness.Assert(s.Dialogue.IsActive, $"dialogue still active to pick '{text}'");
            int i = ChoiceIndex(s.Dialogue.Current, text);
            Harness.Assert(i >= 0, $"choice '{text}' visible at node {s.Dialogue.Current?.NodeId} (choices: {Describe(s.Dialogue.Current)})");
            s.ChooseDialogue(i);
        }

        /// <summary>Finishes the dialogue: continues text and picks the choice containing <paramref name="preferred"/> (else the last choice).</summary>
        public static void Finish(GameSession s, string preferred = "Goodbye", int max = 100)
        {
            for (int i = 0; i < max && s.Dialogue.IsActive; i++)
            {
                var v = s.Dialogue.Current;
                if (v == null) break;
                if (v.CanContinue) { s.ContinueDialogue(); continue; }
                int idx = ChoiceIndex(v, preferred);
                if (idx < 0) idx = v.Choices.Count - 1;
                s.ChooseDialogue(idx);
            }
            Harness.Assert(!s.Dialogue.IsActive, "dialogue finished");
        }

        public static string Describe(DialogueView v)
        {
            if (v == null) return "(none)";
            var parts = new List<string>();
            foreach (var c in v.Choices) parts.Add($"[{c.Index}] {c.Text}");
            return string.Join(" | ", parts);
        }

        /// <summary>AI plays the current battle to the end; asserts victory; applies the result and takes the loot.</summary>
        public static BattleSummary WinBattle(GameSession s, int maxRounds = 80)
        {
            Harness.Assert(s.Battle != null, "a battle is running");
            var outcome = s.AutoResolve(maxRounds);
            Harness.Assert(outcome == BattleOutcome.Victory, $"battle won (outcome {outcome}, round {s.Battle.Round}, party HP {PartyHp(s)})");
            var sum = s.FinishBattle();
            Harness.Assert(sum != null && sum.Outcome == CombatEndKind.Victory, "victory applied");
            if (s.PendingLoot != null) s.TakeAllLoot();
            return sum;
        }

        public static string PartyHp(GameSession s)
        {
            var parts = new List<string>();
            foreach (var u in s.Party) parts.Add($"{u.Name}:{u.Health:0}/{u.MaxHealth:0}");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Walks the leader to <paramref name="dest"/>, winning every fight that triggers on the way (encounter
        /// dialogues are answered with <paramref name="dialogueChoice"/> or finished). Returns the last trigger.
        /// </summary>
        public static TriggerResult WalkTo(GameSession s, Vec2 dest, string dialogueChoice = null, int maxLegs = 20)
        {
            TriggerResult last = TriggerResult.Nothing;
            for (int leg = 0; leg < maxLegs; leg++)
            {
                if (s.Mode == SessionMode.Combat) { WinBattle(s); continue; }
                if (s.Mode == SessionMode.Dialogue)
                {
                    if (dialogueChoice != null) { SkipText(s); int i = ChoiceIndex(s.Dialogue.Current, dialogueChoice); if (i >= 0) s.ChooseDialogue(i); }
                    Finish(s);
                    continue;
                }
                var r = s.MoveLeader(dest);
                last = r.Trigger;
                if (!r.Trigger.Stop) return last;
                if (r.Trigger.Kind == TriggerKind.Travel || r.Trigger.Kind == TriggerKind.Locked) return last;
            }
            return last;
        }

        /// <summary>Puts the party at full health/mana (between test fights).</summary>
        public static void Refresh(GameSession s)
        {
            foreach (var u in s.PartyUnits()) u.RestoreFull();
        }

        public static int CountEvents(List<SessionEvent> events, SessionEventKind kind)
        {
            int n = 0;
            foreach (var e in events) if (e.Kind == kind) n++;
            return n;
        }

        public static SessionEvent FindEvent(List<SessionEvent> events, SessionEventKind kind, string id = null)
        {
            foreach (var e in events) if (e.Kind == kind && (id == null || e.Id == id)) return e;
            return null;
        }
    }
}
