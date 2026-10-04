// Persistent dialogue history: chosen choices (for `once` and greying) and how often each dialogue was started.
using System;
using System.Collections.Generic;

namespace Lanternvale.World
{
    [Serializable]
    public sealed class DialogueMemoryState
    {
        /// <summary>"dialogueId/nodeId/choiceIndex" keys of choices picked at least once.</summary>
        public List<string> chosen = new List<string>();
        /// <summary>dialogueId → times started.</summary>
        public Dictionary<string, int> started = new Dictionary<string, int>();
    }

    public sealed class DialogueMemory
    {
        readonly HashSet<string> chosen = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, int> started = new Dictionary<string, int>(StringComparer.Ordinal);

        public static string ChoiceKey(string dialogueId, string nodeId, int choiceIndex) => $"{dialogueId}/{nodeId}/{choiceIndex}";

        public bool HasChosen(string dialogueId, string nodeId, int choiceIndex) => chosen.Contains(ChoiceKey(dialogueId, nodeId, choiceIndex));

        public void MarkChosen(string dialogueId, string nodeId, int choiceIndex) => chosen.Add(ChoiceKey(dialogueId, nodeId, choiceIndex));

        public int TimesStarted(string dialogueId) => dialogueId != null && started.TryGetValue(dialogueId, out int n) ? n : 0;

        public void MarkStarted(string dialogueId)
        {
            if (string.IsNullOrEmpty(dialogueId)) return;
            started[dialogueId] = TimesStarted(dialogueId) + 1;
        }

        public int ChosenCount => chosen.Count;

        public void Clear()
        {
            chosen.Clear();
            started.Clear();
        }

        public DialogueMemoryState Save()
        {
            var s = new DialogueMemoryState();
            s.chosen.AddRange(chosen);
            s.chosen.Sort(StringComparer.Ordinal);
            var keys = new List<string>(started.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) s.started[k] = started[k];
            return s;
        }

        public void Load(DialogueMemoryState state)
        {
            Clear();
            if (state == null) return;
            if (state.chosen != null) foreach (var c in state.chosen) if (!string.IsNullOrEmpty(c)) chosen.Add(c);
            if (state.started != null) foreach (var kv in state.started) if (!string.IsNullOrEmpty(kv.Key)) started[kv.Key] = kv.Value;
        }
    }
}
