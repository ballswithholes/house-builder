// Quest markers (Docs/WorldAPI.md §5, Docs/Expansion.md §3): the WoW-style "!" / "?" over NPC heads and on the Map
// panel. Pure C#:
//   QuestMarker / QuestMarkerInfo   what one NPC shows (best quest first)
//   DialogueReach                   a read-only dry run of a dialogue graph against the live state: every outcome a
//                                   conversation could reach right now (no outcomes run, no dice, memory only read)
//   QuestMarkerIndex                a static index built once from the data: dialogue owners, who starts / sets the
//                                   stage of / completes each quest, who sets which flag, and each stage's hand-in NPC
//                                   with lookahead (the grey "?")
//   QuestMarkers.Evaluate           the classification of one NPC (ReadyToTurnIn > Available > InProgress > AvailableLater)
//   QuestMapHint                    a quest objective on the current map (Map panel hints; GameSession.QuestHintsOnMap)
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    /// <summary>
    /// What an NPC's quest marker shows. Higher values win when an NPC has several quests (WoW's priority):
    /// ReadyToTurnIn (yellow ?) &gt; Available (yellow !) &gt; InProgress (grey ?) &gt; AvailableLater (grey !).
    /// </summary>
    public enum QuestMarker { None, AvailableLater, InProgress, Available, ReadyToTurnIn }

    /// <summary>One quest marker of one NPC.</summary>
    [Serializable]
    public sealed class QuestMarkerInfo
    {
        public string NpcId = "";
        public QuestMarker Kind;
        public string QuestId = "";
        public string QuestName = "";
        /// <summary>A main-story quest (QuestDef.main): the same glyph, 15 % larger, with a thin gold ring.</summary>
        public bool Main;
        /// <summary>AvailableLater: the main character level the offer needs; otherwise the quest's recommended level.</summary>
        public int Level;

        /// <summary>Yellow glyph (Available, ReadyToTurnIn); grey otherwise.</summary>
        public bool Yellow => Kind == QuestMarker.Available || Kind == QuestMarker.ReadyToTurnIn;
        /// <summary>"?" (ReadyToTurnIn, InProgress); "!" otherwise.</summary>
        public bool Question => Kind == QuestMarker.ReadyToTurnIn || Kind == QuestMarker.InProgress;
        public bool IsNone => Kind == QuestMarker.None;

        /// <summary>"!" / "?" ("" for None).</summary>
        public string Glyph => Kind == QuestMarker.None ? "" : Question ? "?" : "!";

        /// <summary>Hover text: "Quest: Wolves at the Fold", "Turn in: …", "In progress: …", "Quest (level 14): …".</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case QuestMarker.ReadyToTurnIn: return "Turn in: " + QuestName;
                case QuestMarker.Available: return "Quest: " + QuestName;
                case QuestMarker.InProgress: return "In progress: " + QuestName;
                case QuestMarker.AvailableLater: return "Quest (level " + Level.ToString(System.Globalization.CultureInfo.InvariantCulture) + "): " + QuestName;
                default: return "";
            }
        }

        public QuestMarkerInfo Clone() => (QuestMarkerInfo)MemberwiseClone();

        public override string ToString() => Kind == QuestMarker.None ? $"{NpcId}: none" : $"{NpcId}: {Kind} {QuestId}{(Main ? " (main)" : "")}";
    }

    // ===================================================================================================== dry run

    public enum ReachMode
    {
        /// <summary>Every condition as the dialogue would test it now.</summary>
        Normal,
        /// <summary>Level conditions count as met (what the NPC will offer once the main character is high enough).</summary>
        IgnoreLevel,
    }

    /// <summary>An outcome a conversation can reach, with the highest Level condition on the path to it.</summary>
    public struct ReachedOutcome
    {
        public OutcomeDef Outcome;
        /// <summary>Max Level condition amount of the entered nodes and picked choices on the path (0 = none).</summary>
        public int LevelGate;
        public string NodeId;
    }

    /// <summary>
    /// Read-only dry run of a dialogue graph (brief_quests §6.3): starts at `start`, resolves node conditions and fallback
    /// chains exactly like DialogueRunner.EnterNode, follows every visible choice (conditions met, used `once` choices
    /// hidden), both branches of a skill check, and `next` links, and collects the node and choice outcomes on the way.
    /// It never runs an outcome, rolls a die or writes the dialogue memory. Outcomes earlier on a path are not applied
    /// to later conditions (in the data, quest outcomes sit at the end of offer and hand-in branches).
    /// </summary>
    public static class DialogueReach
    {
        /// <summary>Safety limit on entered nodes per walk.</summary>
        public const int MaxNodes = 1024;
        /// <summary>Fallback hops per node resolution (DialogueRunner.MaxAutoSteps).</summary>
        public const int MaxHops = 64;

        public static List<ReachedOutcome> Collect(DialogueDef d, IDialogueContext ctx, DialogueMemory memory, ReachMode mode = ReachMode.Normal,
                                                   List<ReachedOutcome> into = null)
        {
            var list = into ?? new List<ReachedOutcome>();
            if (d?.nodes == null || string.IsNullOrEmpty(d.start)) return list;
            var byId = new Dictionary<string, DialogueNodeDef>(StringComparer.Ordinal);
            foreach (var n in d.nodes) if (n != null && !string.IsNullOrEmpty(n.id) && !byId.ContainsKey(n.id)) byId[n.id] = n;
            var best = new Dictionary<string, int>(StringComparer.Ordinal);   // entered node → lowest level gate seen
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(d.start, 0));
            int entered = 0;
            while (stack.Count > 0 && entered < MaxNodes)
            {
                var top = stack.Pop();
                int gate = top.Value;
                var node = Resolve(byId, top.Key, ctx, mode);
                if (node == null) continue;
                gate = Math.Max(gate, LevelOf(node.conditions));
                if (best.TryGetValue(node.id, out int seen) && seen <= gate) continue;
                best[node.id] = gate;
                entered++;
                bool ends = Add(list, node.outcomes, gate, node.id);
                if (ends) continue;   // EndDialogue on the node: no choices, no next
                if (node.choices != null)
                    for (int i = 0; i < node.choices.Count; i++)
                    {
                        var c = node.choices[i];
                        if (c == null || !CheckAll(c.conditions, ctx, mode)) continue;
                        if (c.once && memory != null && memory.HasChosen(d.id, node.id, i)) continue;
                        int cg = Math.Max(gate, LevelOf(c.conditions));
                        if (Add(list, c.outcomes, cg, node.id)) continue;   // EndDialogue on the choice
                        if (c.check != null)
                        {
                            if (!string.IsNullOrEmpty(c.check.failure)) stack.Push(new KeyValuePair<string, int>(c.check.failure, cg));
                            if (!string.IsNullOrEmpty(c.check.success)) stack.Push(new KeyValuePair<string, int>(c.check.success, cg));
                        }
                        else if (!string.IsNullOrEmpty(c.next)) stack.Push(new KeyValuePair<string, int>(c.next, cg));
                    }
                if (!string.IsNullOrEmpty(node.next)) stack.Push(new KeyValuePair<string, int>(node.next, gate));
            }
            return list;
        }

        /// <summary>The node DialogueRunner would enter for `id` (fallback chain), or null when the dialogue would end.</summary>
        static DialogueNodeDef Resolve(Dictionary<string, DialogueNodeDef> byId, string id, IDialogueContext ctx, ReachMode mode)
        {
            for (int hop = 0; hop <= MaxHops && !string.IsNullOrEmpty(id); hop++)
            {
                if (!byId.TryGetValue(id, out var node)) return null;
                if (CheckAll(node.conditions, ctx, mode)) return node;
                id = node.fallback;
            }
            return null;
        }

        public static bool CheckAll(IList<ConditionDef> conditions, IDialogueContext ctx, ReachMode mode)
        {
            if (conditions == null) return true;
            for (int i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                if (c == null || (mode == ReachMode.IgnoreLevel && c.type == ConditionType.Level)) continue;
                if (!WorldRules.Check(c, ctx)) return false;
            }
            return true;
        }

        /// <summary>Highest Level condition amount in the list (0 = none).</summary>
        public static int LevelOf(IList<ConditionDef> conditions)
        {
            int lv = 0;
            if (conditions == null) return lv;
            for (int i = 0; i < conditions.Count; i++)
                if (conditions[i] != null && conditions[i].type == ConditionType.Level) lv = Math.Max(lv, conditions[i].amount);
            return lv;
        }

        // adds the outcomes; true when one of them is EndDialogue (the conversation stops after this node / choice)
        static bool Add(List<ReachedOutcome> list, List<OutcomeDef> outcomes, int gate, string nodeId)
        {
            if (outcomes == null) return false;
            bool ends = false;
            for (int i = 0; i < outcomes.Count; i++)
            {
                var o = outcomes[i];
                if (o == null) continue;
                if (o.type == OutcomeType.EndDialogue) { ends = true; continue; }
                list.Add(new ReachedOutcome { Outcome = o, LevelGate = gate, NodeId = nodeId });
            }
            return ends;
        }
    }

    // ===================================================================================================== static index

    /// <summary>
    /// Static facts about quests and the people who handle them, built once from the data (brief_quests §5):
    /// dialogue owners (NpcDef.dialogue, CompanionDef.recruitDialogue), who can start, set the stage of or complete each
    /// quest, who sets which flag, and the hand-in NPC of each stage. Hand-in rules, in order: the stage's turnIn; else
    /// the union of its Talk targets, the setters of its Flag targets, the quest's completers when it is the last stage,
    /// and the people who set exactly its `next` stage. A stage that resolves without anyone (Kill, Collect, Reach, a flag
    /// set by an encounter…) looks ahead along `next` (16 hops, cycle-safe).
    /// </summary>
    public sealed class QuestMarkerIndex
    {
        public const int MaxLookahead = 16;

        public readonly GameDatabase Db;

        readonly Dictionary<string, string> dialogueOf = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<string> people = new List<string>();
        readonly Dictionary<string, List<string>> starters = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> completers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> flagSetters = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, List<KeyValuePair<string, string>>> stageSetters = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        readonly HashSet<string> offerers = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, int> questOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> directCache = new Dictionary<string, string[]>(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> handInCache = new Dictionary<string, string[]>(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> touchingCache = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static readonly string[] None = new string[0];

        public QuestMarkerIndex(GameDatabase db)
        {
            Db = db ?? new GameDatabase();
            int order = 0;
            foreach (var q in Db.Quests.Values) if (q != null && !questOrder.ContainsKey(q.id)) questOrder[q.id] = order++;
            foreach (var n in Db.Npcs.Values)
                if (n != null && !string.IsNullOrEmpty(n.dialogue)) AddPerson(n.id, n.dialogue);
            foreach (var c in Db.Companions.Values)
                if (c != null && !string.IsNullOrEmpty(c.recruitDialogue) && !dialogueOf.ContainsKey(c.id)) AddPerson(c.id, c.recruitDialogue);
        }

        void AddPerson(string id, string dialogue)
        {
            if (string.IsNullOrEmpty(id) || dialogueOf.ContainsKey(id)) return;
            dialogueOf[id] = dialogue;
            people.Add(id);
            if (!Db.Dialogues.TryGetValue(dialogue, out var d) || d?.nodes == null) return;
            foreach (var n in d.nodes)
            {
                if (n == null) continue;
                Scan(id, n.outcomes);
                if (n.choices != null) foreach (var c in n.choices) if (c != null) Scan(id, c.outcomes);
            }
        }

        void Scan(string person, List<OutcomeDef> outcomes)
        {
            if (outcomes == null) return;
            foreach (var o in outcomes)
            {
                if (o == null || string.IsNullOrEmpty(o.key)) continue;
                switch (o.type)
                {
                    case OutcomeType.StartQuest:
                        AddTo(starters, o.key, person);
                        offerers.Add(person);
                        break;
                    case OutcomeType.SetQuestStage:
                        AddTo(starters, o.key, person);
                        offerers.Add(person);
                        if (!stageSetters.TryGetValue(o.key, out var l)) stageSetters[o.key] = l = new List<KeyValuePair<string, string>>();
                        l.Add(new KeyValuePair<string, string>(person, o.value ?? ""));
                        break;
                    case OutcomeType.CompleteQuest: AddTo(completers, o.key, person); break;
                    case OutcomeType.SetFlag: AddTo(flagSetters, o.key, person); break;
                }
            }
        }

        static void AddTo(Dictionary<string, List<string>> d, string key, string person)
        {
            if (!d.TryGetValue(key, out var l)) d[key] = l = new List<string>();
            if (!l.Contains(person)) l.Add(person);
        }

        static IReadOnlyList<string> Get(Dictionary<string, List<string>> d, string key) =>
            key != null && d.TryGetValue(key, out var l) ? (IReadOnlyList<string>)l : None;

        // ------------------------------------------------------------------ queries

        /// <summary>Every npc and companion that has a dialogue, in data order.</summary>
        public IReadOnlyList<string> People => people;

        /// <summary>Dialogue id of an npc (NpcDef.dialogue) or a companion (recruitDialogue); "" when none.</summary>
        public string DialogueIdOf(string personId) => personId != null && dialogueOf.TryGetValue(personId, out var d) ? d : "";

        public DialogueDef DialogueOf(string personId)
        {
            var id = DialogueIdOf(personId);
            return id.Length > 0 && Db.Dialogues.TryGetValue(id, out var d) ? d : null;
        }

        /// <summary>The person's dialogue holds some StartQuest or SetQuestStage outcome.</summary>
        public bool Offers(string personId) => personId != null && offerers.Contains(personId);

        /// <summary>People whose dialogue holds StartQuest or SetQuestStage for the quest.</summary>
        public IReadOnlyList<string> Starters(string questId) => Get(starters, questId);

        /// <summary>People whose dialogue holds CompleteQuest for the quest.</summary>
        public IReadOnlyList<string> Completers(string questId) => Get(completers, questId);

        /// <summary>People whose dialogue sets the flag.</summary>
        public IReadOnlyList<string> FlagSetters(string flag) => Get(flagSetters, flag);

        /// <summary>Data order of a quest (ties between markers of the same kind; main quests go first anyway).</summary>
        public int QuestOrder(string questId) => questId != null && questOrder.TryGetValue(questId, out int i) ? i : int.MaxValue;

        public QuestDef Quest(string questId) => questId != null && Db.Quests.TryGetValue(questId, out var q) ? q : null;

        public static int StageIndex(QuestDef q, string stageId)
        {
            if (q?.stages == null || string.IsNullOrEmpty(stageId)) return -1;
            for (int i = 0; i < q.stages.Count; i++) if (q.stages[i] != null && q.stages[i].id == stageId) return i;
            return -1;
        }

        /// <summary>The people a stage resolves at, without lookahead (empty when it resolves on its own).</summary>
        public IReadOnlyList<string> DirectHandIn(string questId, string stageId)
        {
            var q = Quest(questId);
            int si = StageIndex(q, stageId);
            if (si < 0) return None;
            string key = questId + "\n" + stageId;
            if (directCache.TryGetValue(key, out var hit)) return hit;
            var s = q.stages[si];
            var list = new List<string>();
            if (!string.IsNullOrEmpty(s.turnIn)) list.Add(s.turnIn);
            else
            {
                if (s.objectives != null)
                    foreach (var o in s.objectives)
                    {
                        if (o == null || string.IsNullOrEmpty(o.target)) continue;
                        if (o.type == ObjectiveType.Talk) AddUnique(list, o.target);
                        else if (o.type == ObjectiveType.Flag) foreach (var p in FlagSetters(o.target)) AddUnique(list, p);
                    }
                if (string.IsNullOrEmpty(s.next)) foreach (var p in Completers(questId)) AddUnique(list, p);
                else if (stageSetters.TryGetValue(questId, out var setters))
                    foreach (var kv in setters) if (kv.Value == s.next) AddUnique(list, kv.Key);
            }
            var arr = list.ToArray();
            directCache[key] = arr;
            return arr;
        }

        /// <summary>The next hand-in of a stage: its own (DirectHandIn), else the first later stage's along `next`.</summary>
        public IReadOnlyList<string> HandIn(string questId, string stageId)
        {
            var q = Quest(questId);
            if (StageIndex(q, stageId) < 0) return None;
            string key = questId + "\n" + stageId;
            if (handInCache.TryGetValue(key, out var hit)) return hit;
            IReadOnlyList<string> found = None;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string sid = stageId;
            for (int hop = 0; hop <= MaxLookahead && !string.IsNullOrEmpty(sid) && visited.Add(sid); hop++)
            {
                int si = StageIndex(q, sid);
                if (si < 0) break;
                var d = DirectHandIn(questId, sid);
                if (d.Count > 0) { found = d; break; }
                sid = q.stages[si].next;
            }
            var arr = new string[found.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = found[i];
            handInCache[key] = arr;
            return arr;
        }

        /// <summary>People who end the quest: CompleteQuest owners, then the last stage's Talk targets.</summary>
        public IReadOnlyList<string> Enders(string questId)
        {
            var list = new List<string>(Completers(questId));
            var q = Quest(questId);
            if (q?.stages != null && q.stages.Count > 0)
            {
                var last = q.stages[q.stages.Count - 1];
                if (last?.objectives != null)
                    foreach (var o in last.objectives)
                        if (o != null && o.type == ObjectiveType.Talk && !string.IsNullOrEmpty(o.target)) AddUnique(list, o.target);
            }
            return list;
        }

        /// <summary>The marker target of an active stage: HandIn (lookahead), else the first ender, else the giver ("" none).</summary>
        public string TurnInOf(string questId, string stageId)
        {
            var h = HandIn(questId, stageId);
            if (h.Count > 0) return h[0];
            var e = Enders(questId);
            if (e.Count > 0) return e[0];
            return Quest(questId)?.giver ?? "";
        }

        /// <summary>
        /// Everyone who could resolve something of the quest: its stages' turnIn and Talk targets, the setters of its Flag
        /// targets, and its starters and completers (searched for "where can I hand this in now").
        /// </summary>
        public IReadOnlyList<string> Touching(string questId)
        {
            if (questId == null) return None;
            if (touchingCache.TryGetValue(questId, out var hit)) return hit;
            var list = new List<string>();
            var q = Quest(questId);
            if (q?.stages != null)
                foreach (var s in q.stages)
                {
                    if (s == null) continue;
                    if (!string.IsNullOrEmpty(s.turnIn)) AddUnique(list, s.turnIn);
                    if (s.objectives == null) continue;
                    foreach (var o in s.objectives)
                    {
                        if (o == null || string.IsNullOrEmpty(o.target)) continue;
                        if (o.type == ObjectiveType.Talk) AddUnique(list, o.target);
                        else if (o.type == ObjectiveType.Flag) foreach (var p in FlagSetters(o.target)) AddUnique(list, p);
                    }
                }
            foreach (var p in Completers(questId)) AddUnique(list, p);
            foreach (var p in Starters(questId)) AddUnique(list, p);
            var arr = list.ToArray();
            touchingCache[questId] = arr;
            return arr;
        }

        static void AddUnique(List<string> list, string id)
        {
            if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id);
        }
    }

    // ===================================================================================================== classification

    public static class QuestMarkers
    {
        /// <summary>A grey "!" shows while the main character is at most this many levels short of the offer.</summary>
        public const int LaterLevelWindow = 3;

        /// <summary>Priority of a marker kind (higher wins).</summary>
        public static int Rank(QuestMarker k) => (int)k;

        /// <summary>
        /// Every quest marker of one NPC (or unrecruited companion), best first (kind, then main quests, then data order),
        /// at most one per quest. Rules (brief_quests §6.4):
        /// <list type="bullet">
        /// <item>ReadyToTurnIn: an active quest whose current stage the NPC can resolve now: every incomplete objective is a
        /// Talk objective on this NPC or a Flag objective its conversation can set (reachable SetFlag ≥ count), or the
        /// conversation reaches CompleteQuest or a SetQuestStage to a later stage.</item>
        /// <item>Available: a not-started quest the conversation can start now (StartQuest, or SetQuestStage, which starts
        /// it) whose QuestDef.minLevel the main character meets.</item>
        /// <item>InProgress: an active quest whose next hand-in (QuestMarkerIndex.TurnInOf) is this NPC.</item>
        /// <item>AvailableLater: an offer behind a level gate (QuestDef.minLevel or Level conditions on the path, found with
        /// ReachMode.IgnoreLevel) that the main character misses by at most <see cref="LaterLevelWindow"/> levels.</item>
        /// </list>
        /// </summary>
        public static List<QuestMarkerInfo> Evaluate(QuestMarkerIndex index, IDialogueContext ctx, DialogueMemory memory, string npcId,
                                                     List<QuestMarkerInfo> into = null)
        {
            var list = into ?? new List<QuestMarkerInfo>();
            if (index == null || ctx?.Quests == null || string.IsNullOrEmpty(npcId)) return list;
            int first = list.Count;
            var log = ctx.Quests;
            var dlg = index.DialogueOf(npcId);
            var reach = dlg != null ? DialogueReach.Collect(dlg, ctx, memory, ReachMode.Normal) : new List<ReachedOutcome>();
            var main = WorldRules.MainMember(ctx);
            int level = main != null ? main.level : 1;
            var byQuest = new Dictionary<string, QuestMarkerInfo>(StringComparer.Ordinal);

            // active quests: ready here, or waiting for this NPC later
            foreach (var qid in log.KnownQuests())
            {
                if (log.GetStatus(qid) != QuestStatus.Active) continue;
                var q = index.Quest(qid);
                string stageId = log.GetStage(qid);
                int si = QuestMarkerIndex.StageIndex(q, stageId);
                if (si < 0) continue;
                if (IsReady(q, si, npcId, dlg != null, reach, log))
                    Put(byQuest, q, npcId, QuestMarker.ReadyToTurnIn, q.level);
                else if (index.TurnInOf(qid, stageId) == npcId || Contains(index.HandIn(qid, stageId), npcId))
                    Put(byQuest, q, npcId, QuestMarker.InProgress, q.level);
            }

            // offers the conversation can make now
            foreach (var r in reach)
            {
                var o = r.Outcome;
                if (o.type != OutcomeType.StartQuest && o.type != OutcomeType.SetQuestStage) continue;
                var q = index.Quest(o.key);
                if (q == null || log.GetStatus(q.id) != QuestStatus.NotStarted || q.minLevel > level) continue;
                Put(byQuest, q, npcId, QuestMarker.Available, q.level);
            }

            // offers behind a level gate within reach
            if (dlg != null && index.Offers(npcId))
            {
                foreach (var r in DialogueReach.Collect(dlg, ctx, memory, ReachMode.IgnoreLevel))
                {
                    var o = r.Outcome;
                    if (o.type != OutcomeType.StartQuest && o.type != OutcomeType.SetQuestStage) continue;
                    var q = index.Quest(o.key);
                    if (q == null || log.GetStatus(q.id) != QuestStatus.NotStarted) continue;
                    int gate = Math.Max(q.minLevel, r.LevelGate);
                    if (gate <= level || gate - level > LaterLevelWindow) continue;
                    if (byQuest.TryGetValue(q.id, out var have) && (have.Kind != QuestMarker.AvailableLater || have.Level <= gate)) continue;
                    byQuest.Remove(q.id);
                    Put(byQuest, q, npcId, QuestMarker.AvailableLater, gate);
                }
            }

            list.AddRange(byQuest.Values);
            list.Sort(first, list.Count - first, Comparer(index));
            return list;
        }

        /// <summary>The best marker of a list built by Evaluate (a None marker for npcId when empty).</summary>
        public static QuestMarkerInfo Best(IReadOnlyList<QuestMarkerInfo> markers, string npcId = "") =>
            markers != null && markers.Count > 0 ? markers[0] : new QuestMarkerInfo { NpcId = npcId ?? "" };

        /// <summary>Sort order: kind (priority), then main quests, then quest data order.</summary>
        public static IComparer<QuestMarkerInfo> Comparer(QuestMarkerIndex index) =>
            Comparer<QuestMarkerInfo>.Create((a, b) =>
            {
                int c = Rank(b.Kind).CompareTo(Rank(a.Kind));
                if (c != 0) return c;
                if (a.Main != b.Main) return a.Main ? -1 : 1;
                c = (index?.QuestOrder(a.QuestId) ?? 0).CompareTo(index?.QuestOrder(b.QuestId) ?? 0);
                return c != 0 ? c : string.CompareOrdinal(a.QuestId, b.QuestId);
            });

        static void Put(Dictionary<string, QuestMarkerInfo> byQuest, QuestDef q, string npcId, QuestMarker kind, int level)
        {
            if (byQuest.TryGetValue(q.id, out var have) && Rank(have.Kind) >= Rank(kind)) return;
            byQuest[q.id] = new QuestMarkerInfo { NpcId = npcId, Kind = kind, QuestId = q.id, QuestName = q.name ?? q.id, Main = q.main, Level = level };
        }

        static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return true;
            return false;
        }

        /// <summary>Can the NPC's conversation resolve the quest's current stage now?</summary>
        static bool IsReady(QuestDef q, int si, string npcId, bool hasDialogue, List<ReachedOutcome> reach, QuestLog log)
        {
            var s = q.stages[si];
            foreach (var r in reach)
            {
                var o = r.Outcome;
                if (o.key != q.id) continue;
                if (o.type == OutcomeType.CompleteQuest) return true;
                if (o.type == OutcomeType.SetQuestStage && QuestMarkerIndex.StageIndex(q, o.value) > si) return true;
            }
            if (s.objectives == null || s.objectives.Count == 0) return false;
            bool handled = false;
            for (int i = 0; i < s.objectives.Count; i++)
            {
                var ob = s.objectives[i];
                if (ob == null) continue;
                int need = Math.Max(1, ob.count);
                if (log.GetProgress(q.id, i) >= need) continue;
                if (ob.type == ObjectiveType.Talk && hasDialogue && ob.target == npcId) { handled = true; continue; }   // OnTalk at dialogue end
                if (ob.type == ObjectiveType.Flag && SetsFlag(reach, ob.target, need)) { handled = true; continue; }
                return false;   // something else is still to do
            }
            return handled;
        }

        static bool SetsFlag(List<ReachedOutcome> reach, string flag, int need)
        {
            foreach (var r in reach)
            {
                var o = r.Outcome;
                if (o.type != OutcomeType.SetFlag || o.key != flag) continue;
                int v = o.amount;
                if (v == 0 && !(!string.IsNullOrEmpty(o.value) && int.TryParse(o.value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v))) v = 1;
                if (v >= need) return true;
            }
            return false;
        }
    }

    // ===================================================================================================== map hints

    public enum QuestMapHintKind
    {
        /// <summary>A visible encounter holding an active Kill target (or a Defeat target).</summary>
        Encounter,
        /// <summary>A region an active Reach objective targets.</summary>
        Region,
        /// <summary>An unopened chest holding an item an active Collect objective needs.</summary>
        Chest,
        /// <summary>A visible exit to a map an active Reach objective targets.</summary>
        Transition,
    }

    /// <summary>A quest objective on the current map (Map panel hint ring).</summary>
    [Serializable]
    public sealed class QuestMapHint
    {
        public QuestMapHintKind Kind;
        /// <summary>Encounter, region, chest or transition id.</summary>
        public string Id = "";
        public Vec2 Pos;
        /// <summary>Rect size for regions and transitions (zero otherwise).</summary>
        public Vec2 Size;
        public string QuestId = "";
        public string QuestName = "";
        public bool Main;
        /// <summary>The objective's display text ("Grey Wolves driven off 1/3").</summary>
        public string Text = "";

        public override string ToString() => $"{Kind} {Id} ({QuestId}: {Text})";
    }
}
