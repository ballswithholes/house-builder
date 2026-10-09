// Quest progression: states, stages, objectives, rewards, journal view model and toast events. Pure C#.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    public enum QuestStatus { NotStarted, Active, Completed, Failed }

    public enum QuestEventKind { Started, ObjectiveProgress, ObjectiveCompleted, StageAdvanced, Completed, Failed, RewardChoicePending }

    /// <summary>Quest change notification for UI toasts.</summary>
    public sealed class QuestEvent
    {
        public QuestEventKind Kind;
        public string QuestId = "";
        public string QuestName = "";
        public string StageId = "";
        /// <summary>Objective index in the current stage (-1 when not about an objective).</summary>
        public int ObjectiveIndex = -1;
        public int Progress;
        public int Count;
        /// <summary>Toast-ready text: "Wolves slain 3/6", stage description, quest name...</summary>
        public string Text = "";

        public override string ToString() => $"{Kind} {QuestId}{(StageId.Length > 0 ? "/" + StageId : "")}: {Text}";
    }

    [Serializable]
    public sealed class ObjectiveView
    {
        public ObjectiveType Type;
        public string Target = "";
        public string Text = "";
        public int Progress;
        public int Count = 1;
        public bool Complete;
        /// <summary>"Wolves slain 3/6"; objectives with count 1 show only the text.</summary>
        public string Display = "";
    }

    [Serializable]
    public sealed class QuestJournalEntry
    {
        public string Id = "";
        public string Title = "";
        public string Summary = "";
        public string GiverId = "";
        public string GiverName = "";
        public int Level = 1;
        public bool Main;
        public QuestStatus Status;
        public string StageId = "";
        /// <summary>Current stage description (last stage for finished quests).</summary>
        public string StageText = "";
        public List<ObjectiveView> Objectives = new List<ObjectiveView>();
        /// <summary>Descriptions of completed stages, oldest first.</summary>
        public List<string> History = new List<string>();
        public QuestRewardDef Rewards;
        public bool RewardChoicePending;
    }

    [Serializable]
    public sealed class QuestRecordState
    {
        public string id = "";
        public QuestStatus status;
        public string stage = "";
        public int[] progress = new int[0];
        public List<string> history = new List<string>();
        public int order;
    }

    [Serializable]
    public sealed class QuestLogState
    {
        public List<QuestRecordState> quests = new List<QuestRecordState>();
        public List<string> pendingRewardChoices = new List<string>();
        public int nextOrder;
    }

    public sealed class QuestLog
    {
        sealed class Record
        {
            public string id;
            public QuestDef def;
            public QuestStatus status;
            public int stageIndex;
            public int[] progress = new int[0];
            public readonly List<string> history = new List<string>();
            public int order;
            public bool leaving; // running the current stage's onComplete

            public QuestStageDef Stage => def != null && stageIndex >= 0 && stageIndex < def.stages.Count ? def.stages[stageIndex] : null;
        }

        public GameDatabase Db { get; set; }
        public IDialogueContext Context { get; set; }

        /// <summary>Raised for every quest change (toasts, journal refresh).</summary>
        public event Action<QuestEvent> Changed;

        /// <summary>The quest whose stage outcomes or rewards are being applied right now (null otherwise): the session
        /// pays their GiveXP at the quest's level (Progression.QuestLevel), not the receiver's.</summary>
        public QuestDef Paying { get; private set; }

        readonly Dictionary<string, Record> records = new Dictionary<string, Record>(StringComparer.Ordinal);
        readonly List<Record> ordered = new List<Record>();
        readonly List<string> pendingChoices = new List<string>();
        int nextOrder;
        int depth;
        const int MaxDepth = 24;

        public QuestLog(GameDatabase db, IDialogueContext ctx = null)
        {
            Db = db;
            Context = ctx;
        }

        QuestDef Def(string id) => Db != null && id != null && Db.Quests.TryGetValue(id, out var q) ? q : null;

        Record Rec(string id) => id != null && records.TryGetValue(id, out var r) ? r : null;

        // ------------------------------------------------------------------ queries

        public QuestStatus GetStatus(string questId) => Rec(questId)?.status ?? QuestStatus.NotStarted;
        public bool IsActive(string questId) => GetStatus(questId) == QuestStatus.Active;
        public bool IsCompleted(string questId) => GetStatus(questId) == QuestStatus.Completed;

        /// <summary>Current stage id ("" when not started).</summary>
        public string GetStage(string questId) => Rec(questId)?.Stage?.id ?? "";

        public int GetProgress(string questId, int objectiveIndex)
        {
            var r = Rec(questId);
            return r != null && objectiveIndex >= 0 && objectiveIndex < r.progress.Length ? r.progress[objectiveIndex] : 0;
        }

        /// <summary>
        /// How many of an item the quests still want in total (loot entries with <c>whileQuestNeeds</c>): the largest
        /// Collect count for the item among the stages of unfinished quests that are still ahead, i.e. the current stage
        /// and later ones of an active quest, or any stage of a quest not started yet (encounters never respawn, so
        /// creatures killed before the quest is picked up must still supply it). 0 once every such quest has passed
        /// its collecting stage, completed or failed. The party's own count is not subtracted.
        /// </summary>
        public int CollectNeed(string itemId)
        {
            if (Db == null || string.IsNullOrEmpty(itemId)) return 0;
            int need = 0;
            foreach (var q in Db.Quests.Values)
            {
                if (q?.stages == null) continue;
                var r = Rec(q.id);
                int from = 0;
                if (r != null)
                {
                    if (r.status == QuestStatus.Completed || r.status == QuestStatus.Failed) continue;
                    if (r.status == QuestStatus.Active) from = r.stageIndex;
                }
                for (int s = Math.Max(0, from); s < q.stages.Count; s++)
                {
                    var objectives = q.stages[s]?.objectives;
                    if (objectives == null) continue;
                    foreach (var o in objectives)
                        if (o != null && o.type == ObjectiveType.Collect && string.Equals(o.target, itemId, StringComparison.Ordinal))
                            need = Math.Max(need, Math.Max(1, o.count));
                }
            }
            return need;
        }

        /// <summary>Ids of all started quests (any status), in start order.</summary>
        public IEnumerable<string> KnownQuests()
        {
            foreach (var r in ordered) yield return r.id;
        }

        /// <summary>Quest ids whose choiceItems reward awaits a pick (see ClaimRewardChoice).</summary>
        public IReadOnlyList<string> PendingRewardChoices => pendingChoices;

        // ------------------------------------------------------------------ state changes

        /// <summary>Starts a quest at its first stage. False if unknown or already started.</summary>
        public bool Start(string questId)
        {
            var def = Def(questId);
            if (def == null) { Log.Warn($"QuestLog: unknown quest '{questId}'"); return false; }
            if (records.ContainsKey(questId)) return false;
            var r = CreateRecord(def, QuestStatus.Active);
            Emit(QuestEventKind.Started, r, -1, def.name);
            if (def.stages.Count == 0) { FinishQuest(r); return true; }
            EnterStage(r, 0, false);
            return true;
        }

        /// <summary>
        /// Moves an active quest to the given stage (starting it if needed). Moving forward runs the current
        /// stage's onComplete outcomes first. False if unknown quest/stage or the quest is finished.
        /// </summary>
        public bool SetStage(string questId, string stageId)
        {
            var def = Def(questId);
            if (def == null) { Log.Warn($"QuestLog: unknown quest '{questId}'"); return false; }
            int idx = StageIndex(def, stageId);
            if (idx < 0) { Log.Warn($"QuestLog: quest '{questId}' has no stage '{stageId}'"); return false; }
            var r = Rec(questId);
            if (r == null)
            {
                r = CreateRecord(def, QuestStatus.Active);
                Emit(QuestEventKind.Started, r, -1, def.name);
                EnterStage(r, idx, idx > 0);
                return true;
            }
            if (r.status != QuestStatus.Active) return false;
            if (r.stageIndex == idx) return true;
            if (idx > r.stageIndex && !r.leaving)
            {
                int before = r.stageIndex;
                LeaveStage(r);
                if (r.status != QuestStatus.Active || r.stageIndex != before) return true; // outcomes moved it
            }
            EnterStage(r, idx, true);
            return true;
        }

        /// <summary>Completes a quest (running the current stage's onComplete) and grants rewards.</summary>
        public bool Complete(string questId)
        {
            var def = Def(questId);
            if (def == null) { Log.Warn($"QuestLog: unknown quest '{questId}'"); return false; }
            var r = Rec(questId);
            if (r == null)
            {
                r = CreateRecord(def, QuestStatus.Active);
                Emit(QuestEventKind.Started, r, -1, def.name);
            }
            if (r.status != QuestStatus.Active) return false;
            if (!r.leaving)
            {
                int before = r.stageIndex;
                LeaveStage(r);
                if (r.status != QuestStatus.Active || r.stageIndex != before) return true;
            }
            FinishQuest(r);
            return true;
        }

        /// <summary>Fails an active (or not yet started) quest.</summary>
        public bool Fail(string questId)
        {
            var def = Def(questId);
            if (def == null) { Log.Warn($"QuestLog: unknown quest '{questId}'"); return false; }
            var r = Rec(questId) ?? CreateRecord(def, QuestStatus.Active);
            if (r.status != QuestStatus.Active) return false;
            r.status = QuestStatus.Failed;
            Emit(QuestEventKind.Failed, r, -1, def.name);
            return true;
        }

        /// <summary>Gives the picked choiceItems reward. False if nothing is pending or the item is not offered.</summary>
        public bool ClaimRewardChoice(string questId, string itemId)
        {
            if (!pendingChoices.Contains(questId)) return false;
            var def = Def(questId);
            if (def?.rewards?.choiceItems == null || Array.IndexOf(def.rewards.choiceItems, itemId) < 0) return false;
            pendingChoices.Remove(questId);
            Context?.GiveItem(itemId, 1);
            return true;
        }

        Record CreateRecord(QuestDef def, QuestStatus status)
        {
            var r = new Record { id = def.id, def = def, status = status, order = nextOrder++ };
            records[def.id] = r;
            ordered.Add(r);
            return r;
        }

        static int StageIndex(QuestDef def, string stageId)
        {
            if (def?.stages == null) return -1;
            for (int i = 0; i < def.stages.Count; i++)
                if (def.stages[i] != null && def.stages[i].id == stageId) return i;
            return -1;
        }

        void EnterStage(Record r, int idx, bool emitAdvance)
        {
            r.stageIndex = idx;
            var st = r.Stage;
            r.progress = new int[st?.objectives?.Count ?? 0];
            if (emitAdvance) Emit(QuestEventKind.StageAdvanced, r, -1, st?.description ?? "");
            Pull(r, false);
            TryCompleteStage(r);
        }

        // Runs the current stage's onComplete once and records it in the history.
        void LeaveStage(Record r)
        {
            var st = r.Stage;
            if (st == null) return;
            if (r.history.Count == 0 || r.history[r.history.Count - 1] != st.id) r.history.Add(st.id);
            if (st.onComplete == null || st.onComplete.Count == 0) return;
            if (depth >= MaxDepth) { Log.Warn($"QuestLog: recursion limit reached in '{r.id}'"); return; }
            depth++;
            r.leaving = true;
            var paying = Paying;
            Paying = r.def;
            try { WorldRules.ExecuteAll(st.onComplete, Context); }
            finally
            {
                Paying = paying;
                r.leaving = false;
                depth--;
            }
        }

        void TryCompleteStage(Record r)
        {
            if (r.status != QuestStatus.Active || r.leaving) return;
            var st = r.Stage;
            if (st?.objectives == null || st.objectives.Count == 0) return;
            for (int i = 0; i < st.objectives.Count; i++)
                if (r.progress[i] < Math.Max(1, st.objectives[i].count)) return;
            if (depth >= MaxDepth) { Log.Warn($"QuestLog: recursion limit reached in '{r.id}'"); return; }
            int before = r.stageIndex;
            LeaveStage(r);
            if (r.status != QuestStatus.Active || r.stageIndex != before) return; // outcomes changed the quest
            if (string.IsNullOrEmpty(st.next))
            {
                FinishQuest(r);
                return;
            }
            int next = StageIndex(r.def, st.next);
            if (next < 0)
            {
                Log.Warn($"QuestLog: quest '{r.id}' stage '{st.id}' -> missing stage '{st.next}'");
                FinishQuest(r);
                return;
            }
            depth++;
            try { EnterStage(r, next, true); }
            finally { depth--; }
        }

        void FinishQuest(Record r)
        {
            r.status = QuestStatus.Completed;
            var st = r.Stage;
            if (st != null && (r.history.Count == 0 || r.history[r.history.Count - 1] != st.id)) r.history.Add(st.id);
            Emit(QuestEventKind.Completed, r, -1, r.def.name);
            var rw = r.def.rewards;
            if (rw == null || Context == null) return;
            if (rw.xp > 0)
            {
                var paying = Paying;
                Paying = r.def;
                try { Context.GiveXP(rw.xp); }
                finally { Paying = paying; }
            }
            if (rw.gold > 0) Context.GiveGold(rw.gold);
            if (rw.items != null) foreach (var it in rw.items) if (!string.IsNullOrEmpty(it)) Context.GiveItem(it, 1);
            if (rw.choiceItems != null && rw.choiceItems.Length > 0 && !pendingChoices.Contains(r.id))
            {
                pendingChoices.Add(r.id);
                Emit(QuestEventKind.RewardChoicePending, r, -1, r.def.name);
            }
        }

        // ------------------------------------------------------------------ notifications

        public void OnKill(string creatureId, int count = 1)
        {
            if (string.IsNullOrEmpty(creatureId) || count <= 0) return;
            ForEachObjective(ObjectiveType.Kill, creatureId, (r, i, need) => Math.Min(need, r.progress[i] + count));
        }

        /// <summary>Absolute inventory count of an item (call whenever it changes).</summary>
        public void OnItemCount(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            ForEachObjective(ObjectiveType.Collect, itemId, (r, i, need) => MathUtil.Clamp(count, 0, need));
        }

        public void OnTalk(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            ForEachObjective(ObjectiveType.Talk, npcId, (r, i, need) => need);
        }

        /// <summary>Flag changed: updates Flag objectives (progress = flag value) and Defeat objectives (enc_&lt;id&gt;).</summary>
        public void OnFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return;
            var flags = Context?.Flags;
            int v = flags?.Get(flag) ?? 0;
            ForEachObjective(ObjectiveType.Flag, flag, (r, i, need) => MathUtil.Clamp(v, 0, need));
            if (v != 0 && flag.StartsWith(WorldRules.EncounterFlagPrefix, StringComparison.Ordinal))
                ForEachObjective(ObjectiveType.Defeat, flag.Substring(WorldRules.EncounterFlagPrefix.Length), (r, i, need) => need);
        }

        /// <summary>Party is inside a region (region id) or on a map (map id).</summary>
        public void OnReach(string regionOrMapId)
        {
            if (string.IsNullOrEmpty(regionOrMapId)) return;
            ForEachObjective(ObjectiveType.Reach, regionOrMapId, (r, i, need) => need);
        }

        public void OnDefeat(string encounterId)
        {
            if (string.IsNullOrEmpty(encounterId)) return;
            ForEachObjective(ObjectiveType.Defeat, encounterId, (r, i, need) => need);
        }

        /// <summary>Re-pulls Collect/Flag/Defeat objectives from the context (after loading or bulk inventory changes).</summary>
        public void Refresh()
        {
            for (int k = 0; k < ordered.Count; k++)
            {
                var r = ordered[k];
                if (r.status != QuestStatus.Active) continue;
                Pull(r, true);
                TryCompleteStage(r);
            }
        }

        void ForEachObjective(ObjectiveType type, string target, Func<Record, int, int, int> newProgress)
        {
            for (int k = 0; k < ordered.Count; k++)
            {
                var r = ordered[k];
                if (r.status != QuestStatus.Active) continue;
                var st = r.Stage;
                if (st?.objectives == null) continue;
                bool changed = false;
                int stageAt = r.stageIndex;
                for (int i = 0; i < st.objectives.Count && i < r.progress.Length; i++)
                {
                    var o = st.objectives[i];
                    if (o.type != type || !string.Equals(o.target, target, StringComparison.Ordinal)) continue;
                    int need = Math.Max(1, o.count);
                    int np = newProgress(r, i, need);
                    if (SetProgress(r, i, np, true)) changed = true;
                    if (r.status != QuestStatus.Active || r.stageIndex != stageAt) break;
                }
                if (changed && r.status == QuestStatus.Active && r.stageIndex == stageAt) TryCompleteStage(r);
            }
        }

        bool SetProgress(Record r, int i, int value, bool emit)
        {
            var o = r.Stage.objectives[i];
            int need = Math.Max(1, o.count);
            value = MathUtil.Clamp(value, 0, need);
            int old = r.progress[i];
            if (old == value) return false;
            r.progress[i] = value;
            if (emit)
            {
                var text = ObjectiveDisplay(o, value);
                Emit(value >= need ? QuestEventKind.ObjectiveCompleted : QuestEventKind.ObjectiveProgress, r, i, text);
            }
            return true;
        }

        void Pull(Record r, bool emit)
        {
            var st = r.Stage;
            if (st?.objectives == null || Context == null) return;
            for (int i = 0; i < st.objectives.Count && i < r.progress.Length; i++)
            {
                var o = st.objectives[i];
                int need = Math.Max(1, o.count);
                switch (o.type)
                {
                    case ObjectiveType.Collect:
                        SetProgress(r, i, Context.CountItem(o.target), emit);
                        break;
                    case ObjectiveType.Flag:
                        if (Context.Flags != null) SetProgress(r, i, Context.Flags.Get(o.target), emit);
                        break;
                    case ObjectiveType.Defeat:
                        if (Context.Flags != null && Context.Flags.IsSet(WorldRules.EncounterDoneFlag(o.target))) SetProgress(r, i, need, emit);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ journal

        /// <summary>Default objective text when the data gives none.</summary>
        public string ObjectiveText(ObjectiveDef o)
        {
            if (!string.IsNullOrEmpty(o.text)) return o.text;
            string t = o.target ?? "";
            switch (o.type)
            {
                case ObjectiveType.Kill:
                {
                    var c = Db?.Creature(t);
                    return $"Slay {(c != null && c.name.Length > 0 ? c.name : t)}";
                }
                case ObjectiveType.Collect:
                {
                    var it = Db?.Item(t);
                    return $"Collect {(it != null && it.name.Length > 0 ? it.name : t)}";
                }
                case ObjectiveType.Talk:
                {
                    string n = t;
                    if (Db != null && Db.Npcs.TryGetValue(t, out var npc) && npc.name.Length > 0) n = npc.name;
                    else if (Db != null && Db.Companions.TryGetValue(t, out var comp) && comp.name.Length > 0) n = comp.name;
                    return $"Speak with {n}";
                }
                case ObjectiveType.Reach:
                {
                    string n = t;
                    if (Db != null && Db.Maps.TryGetValue(t, out var m) && m.name.Length > 0) n = m.name;
                    return $"Reach {n}";
                }
                case ObjectiveType.Defeat: return $"Defeat {t}";
                default: return t;
            }
        }

        public string ObjectiveDisplay(ObjectiveDef o, int progress)
        {
            var text = ObjectiveText(o);
            int need = Math.Max(1, o.count);
            return need > 1 ? $"{text} {Math.Min(progress, need)}/{need}" : text;
        }

        public QuestJournalEntry GetEntry(string questId)
        {
            var def = Def(questId);
            if (def == null) return null;
            var r = Rec(questId);
            var e = new QuestJournalEntry
            {
                Id = def.id,
                Title = def.name,
                Summary = def.summary,
                GiverId = def.giver,
                GiverName = GiverName(def.giver),
                Level = def.level,
                Main = def.main,
                Status = r?.status ?? QuestStatus.NotStarted,
                Rewards = def.rewards,
                RewardChoicePending = pendingChoices.Contains(def.id),
            };
            if (r == null) return e;
            var st = r.Stage;
            e.StageId = st?.id ?? "";
            e.StageText = st?.description ?? "";
            foreach (var h in r.history)
            {
                if (r.status == QuestStatus.Active && st != null && h == st.id) continue;
                int hi = StageIndex(def, h);
                if (hi >= 0 && !string.IsNullOrEmpty(def.stages[hi].description)) e.History.Add(def.stages[hi].description);
            }
            if (r.status == QuestStatus.Active && st?.objectives != null)
            {
                for (int i = 0; i < st.objectives.Count; i++)
                {
                    var o = st.objectives[i];
                    int p = i < r.progress.Length ? r.progress[i] : 0;
                    int need = Math.Max(1, o.count);
                    e.Objectives.Add(new ObjectiveView
                    {
                        Type = o.type,
                        Target = o.target,
                        Text = ObjectiveText(o),
                        Progress = p,
                        Count = need,
                        Complete = p >= need,
                        Display = ObjectiveDisplay(o, p),
                    });
                }
            }
            return e;
        }

        string GiverName(string giver)
        {
            if (string.IsNullOrEmpty(giver) || Db == null) return giver ?? "";
            if (Db.Npcs.TryGetValue(giver, out var n) && n.name.Length > 0) return n.name;
            if (Db.Companions.TryGetValue(giver, out var c) && c.name.Length > 0) return c.name;
            return giver;
        }

        /// <summary>Journal entries: active (main quests first, then by start order), then completed and failed (newest first).</summary>
        public List<QuestJournalEntry> GetJournal(bool includeFinished = true)
        {
            var active = new List<Record>();
            var finished = new List<Record>();
            foreach (var r in ordered)
            {
                if (r.status == QuestStatus.Active) active.Add(r);
                else if (includeFinished) finished.Add(r);
            }
            active.Sort((a, b) => a.def.main != b.def.main ? (a.def.main ? -1 : 1) : a.order.CompareTo(b.order));
            finished.Sort((a, b) => a.status != b.status ? (a.status == QuestStatus.Completed ? -1 : 1) : b.order.CompareTo(a.order));
            var list = new List<QuestJournalEntry>(active.Count + finished.Count);
            foreach (var r in active) list.Add(GetEntry(r.id));
            foreach (var r in finished) list.Add(GetEntry(r.id));
            return list;
        }

        void Emit(QuestEventKind kind, Record r, int objectiveIndex, string text)
        {
            var h = Changed;
            if (h == null) return;
            var ev = new QuestEvent
            {
                Kind = kind,
                QuestId = r.id,
                QuestName = r.def?.name ?? r.id,
                StageId = r.Stage?.id ?? "",
                ObjectiveIndex = objectiveIndex,
                Text = text ?? "",
            };
            if (objectiveIndex >= 0 && r.Stage != null && objectiveIndex < r.progress.Length)
            {
                ev.Progress = r.progress[objectiveIndex];
                ev.Count = Math.Max(1, r.Stage.objectives[objectiveIndex].count);
            }
            h(ev);
        }

        // ------------------------------------------------------------------ save / load

        public QuestLogState Save()
        {
            var s = new QuestLogState { nextOrder = nextOrder };
            foreach (var r in ordered)
            {
                s.quests.Add(new QuestRecordState
                {
                    id = r.id,
                    status = r.status,
                    stage = r.Stage?.id ?? "",
                    progress = (int[])r.progress.Clone(),
                    history = new List<string>(r.history),
                    order = r.order,
                });
            }
            s.pendingRewardChoices.AddRange(pendingChoices);
            return s;
        }

        /// <summary>Replaces the log with the snapshot (unknown quests are dropped with a warning). No events.</summary>
        public void Load(QuestLogState state)
        {
            records.Clear();
            ordered.Clear();
            pendingChoices.Clear();
            nextOrder = 0;
            if (state == null) return;
            if (state.quests != null)
            {
                var list = new List<QuestRecordState>(state.quests);
                list.Sort((a, b) => a.order.CompareTo(b.order));
                foreach (var q in list)
                {
                    var def = Def(q?.id);
                    if (def == null) { Log.Warn($"QuestLog: save references unknown quest '{q?.id}'"); continue; }
                    var r = new Record { id = def.id, def = def, status = q.status, order = q.order };
                    int idx = StageIndex(def, q.stage);
                    if (idx < 0 && def.stages.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(q.stage)) Log.Warn($"QuestLog: quest '{def.id}' has no stage '{q.stage}' (save)");
                        idx = 0;
                    }
                    r.stageIndex = idx;
                    int n = r.Stage?.objectives?.Count ?? 0;
                    r.progress = new int[n];
                    if (q.progress != null) for (int i = 0; i < n && i < q.progress.Length; i++) r.progress[i] = q.progress[i];
                    if (q.history != null) r.history.AddRange(q.history);
                    records[def.id] = r;
                    ordered.Add(r);
                    nextOrder = Math.Max(nextOrder, r.order + 1);
                }
            }
            nextOrder = Math.Max(nextOrder, state.nextOrder);
            if (state.pendingRewardChoices != null)
                foreach (var p in state.pendingRewardChoices) if (records.ContainsKey(p) && !pendingChoices.Contains(p)) pendingChoices.Add(p);
        }
    }
}
