// Quest markers (Docs/SessionAPI.md "Quest markers", Docs/Expansion.md §3): the "!" / "?" of every NPC, the quest
// objectives on the current map and the hand-in NPC of each active quest, cached per QuestMarkersVersion.
//
// QuestMarkersVersion moves whenever a marker input may have changed: quest progress (QuestLog.Changed), story flags,
// bags and gold, dialogue choices (`once` memory), and the session events for level, party, approval, time of day,
// map, dialogue end, new game and load (QuestLog.Load and FlagStore.Load raise nothing, so GameLoaded must count).
// The hooks are installed lazily on first use. FlagsVersion is never touched: it would rebuild the whole world view.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        QuestMarkerIndex markerIndex;
        bool markerHooks;
        int questMarkersVersion = 1;
        int markerCacheVersion = -1;
        readonly Dictionary<string, List<QuestMarkerInfo>> markerCache = new Dictionary<string, List<QuestMarkerInfo>>(StringComparer.Ordinal);
        readonly Dictionary<string, QuestMarkerInfo> turnInCache = new Dictionary<string, QuestMarkerInfo>(StringComparer.Ordinal);
        readonly List<QuestMarkerInfo> mapMarkers = new List<QuestMarkerInfo>();
        readonly List<QuestMapHint> mapHints = new List<QuestMapHint>();
        string mapMarkersFor = "", mapHintsFor = "";
        int mapMarkersVersion = -1, mapHintsVersion = -1;
        static readonly List<QuestMarkerInfo> NoMarkers = new List<QuestMarkerInfo>();

        /// <summary>Static quest index of this database (dialogue owners, hand-in NPC of each stage…), built on first use.</summary>
        public QuestMarkerIndex QuestMarkerIndex => markerIndex ??= new QuestMarkerIndex(Db);

        /// <summary>
        /// Moves whenever a quest marker may have changed (quests, flags, bags, gold, level, party, approval, time of day,
        /// map, dialogue, new game, load). Poll it each frame and rebuild marker views when it differs.
        /// </summary>
        public int QuestMarkersVersion
        {
            get
            {
                EnsureMarkerHooks();
                return questMarkersVersion;
            }
        }

        void EnsureMarkerHooks()
        {
            if (markerHooks) return;
            markerHooks = true;
            World.Quests.Changed += _ => BumpQuestMarkers();
            World.Flags.Changed += (k, o, n) => BumpQuestMarkers();
            Inventory.Changed += (id, n) => BumpQuestMarkers();
            Inventory.GoldChanged += _ => BumpQuestMarkers();
            Dialogue.ChoiceMade += _ => BumpQuestMarkers();
            EventRaised += OnMarkerEvent;
        }

        void OnMarkerEvent(SessionEvent e)
        {
            switch (e.Kind)
            {
                case SessionEventKind.GameStarted:
                case SessionEventKind.GameLoaded:
                case SessionEventKind.MapEntered:
                case SessionEventKind.DialogueEnded:
                case SessionEventKind.LevelUp:
                case SessionEventKind.PartyChanged:
                case SessionEventKind.LeaderChanged:
                case SessionEventKind.CompanionRecruited:
                case SessionEventKind.CompanionDismissed:
                case SessionEventKind.ApprovalChanged:
                case SessionEventKind.TimeOfDayChanged:
                case SessionEventKind.GoldChanged:
                case SessionEventKind.RaidStarted:
                case SessionEventKind.RaidEnded:
                    BumpQuestMarkers();
                    break;
            }
        }

        void BumpQuestMarkers() => questMarkersVersion++;

        void SyncMarkerCache()
        {
            EnsureMarkerHooks();
            if (markerCacheVersion == questMarkersVersion) return;
            markerCacheVersion = questMarkersVersion;
            markerCache.Clear();
            turnInCache.Clear();
        }

        /// <summary>
        /// Every quest marker of an NPC or unrecruited companion (any map), best first, at most one per quest. Empty for
        /// people without quests, for recruited companions and when no game runs. Do not modify the list.
        /// </summary>
        public IReadOnlyList<QuestMarkerInfo> QuestMarkersOf(string npcId)
        {
            if (!hasGame || string.IsNullOrEmpty(npcId)) return NoMarkers;
            SyncMarkerCache();
            if (markerCache.TryGetValue(npcId, out var hit)) return hit;
            List<QuestMarkerInfo> list;
            if (Db.Companions.ContainsKey(npcId) && CompanionStatusOf(npcId) != CompanionStatus.NotRecruited) list = NoMarkers;
            else list = QuestMarkers.Evaluate(QuestMarkerIndex, this, World.DialogueMemory, npcId);
            markerCache[npcId] = list;
            return list;
        }

        /// <summary>The marker an NPC shows (its best quest; Kind None when it has nothing).</summary>
        public QuestMarkerInfo QuestMarkerOf(string npcId) => QuestMarkers.Best(QuestMarkersOf(npcId), npcId);

        /// <summary>The non-None markers of the current map's visible NPCs (VisibleNpcs order), cached per version and map.</summary>
        public IReadOnlyList<QuestMarkerInfo> QuestMarkersOnMap()
        {
            SyncMarkerCache();
            if (mapMarkersVersion == questMarkersVersion && mapMarkersFor == MapId) return mapMarkers;
            mapMarkersVersion = questMarkersVersion;
            mapMarkersFor = MapId;
            mapMarkers.Clear();
            if (!hasGame || Map == null) return mapMarkers;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in Map.VisibleNpcs())
            {
                if (n == null || string.IsNullOrEmpty(n.npc) || !seen.Add(n.npc)) continue;
                var m = QuestMarkerOf(n.npc);
                if (!m.IsNone) mapMarkers.Add(m);
            }
            return mapMarkers;
        }

        /// <summary>
        /// Where an active quest goes next: NpcId = the person who resolves its current stage. Kind ReadyToTurnIn when
        /// someone can resolve it now ("→ Return to …"), InProgress when its hand-in NPC waits for later steps
        /// (QuestMarkerIndex.TurnInOf, with lookahead), None when the quest is not active or nobody is known.
        /// </summary>
        public QuestMarkerInfo QuestTurnInOf(string questId)
        {
            var none = new QuestMarkerInfo { QuestId = questId ?? "" };
            if (!hasGame || string.IsNullOrEmpty(questId) || World.Quests.GetStatus(questId) != QuestStatus.Active) return none;
            SyncMarkerCache();
            if (turnInCache.TryGetValue(questId, out var hit)) return hit;
            var idx = QuestMarkerIndex;
            var q = idx.Quest(questId);
            QuestMarkerInfo result = none;
            if (q != null)
            {
                // ready somewhere? the stage's own hand-in first, then anyone the data ties to the quest
                var ready = FindReady(idx.DirectHandIn(questId, World.Quests.GetStage(questId)), questId) ?? FindReady(idx.Touching(questId), questId);
                if (ready != null) result = ready.Clone();
                else
                {
                    string who = idx.TurnInOf(questId, World.Quests.GetStage(questId));
                    if (!string.IsNullOrEmpty(who))
                        result = new QuestMarkerInfo { NpcId = who, Kind = QuestMarker.InProgress, QuestId = q.id, QuestName = q.name ?? q.id, Main = q.main, Level = q.level };
                }
            }
            turnInCache[questId] = result;
            return result;
        }

        QuestMarkerInfo FindReady(IReadOnlyList<string> people, string questId)
        {
            for (int i = 0; i < people.Count; i++)
                foreach (var m in QuestMarkersOf(people[i]))
                    if (m.QuestId == questId && m.Kind == QuestMarker.ReadyToTurnIn) return m;
            return null;
        }

        /// <summary>
        /// Active objectives on the current map: visible encounters holding an incomplete Kill target (or a Defeat target,
        /// or whose done flag is a Flag target),
        /// regions and exits to maps that Reach objectives target, and unopened chests holding a needed Collect item.
        /// Cached per QuestMarkersVersion and map; do not modify the list.
        /// </summary>
        public IReadOnlyList<QuestMapHint> QuestHintsOnMap()
        {
            SyncMarkerCache();
            if (mapHintsVersion == questMarkersVersion && mapHintsFor == MapId) return mapHints;
            mapHintsVersion = questMarkersVersion;
            mapHintsFor = MapId;
            mapHints.Clear();
            if (!hasGame || Map?.Def == null) return mapHints;
            var def = Map.Def;
            var log = World.Quests;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var qid in log.KnownQuests())
            {
                if (log.GetStatus(qid) != QuestStatus.Active || !Db.Quests.TryGetValue(qid, out var q)) continue;
                int si = QuestMarkerIndex.StageIndex(q, log.GetStage(qid));
                if (si < 0 || q.stages[si].objectives == null) continue;
                var objectives = q.stages[si].objectives;
                for (int i = 0; i < objectives.Count; i++)
                {
                    var o = objectives[i];
                    if (o == null || string.IsNullOrEmpty(o.target) || log.GetProgress(qid, i) >= Math.Max(1, o.count)) continue;
                    string text = log.ObjectiveDisplay(o, log.GetProgress(qid, i));
                    void Hint(QuestMapHintKind kind, string id, Vec2 pos, Vec2 size)
                    {
                        if (!seen.Add(kind + "/" + id)) return;
                        mapHints.Add(new QuestMapHint { Kind = kind, Id = id, Pos = pos, Size = size, QuestId = q.id, QuestName = q.name ?? q.id, Main = q.main, Text = text });
                    }
                    switch (o.type)
                    {
                        case ObjectiveType.Kill:
                        case ObjectiveType.Defeat:
                            foreach (var e in def.encounters)
                            {
                                if (e == null || !Map.IsEncounterVisible(e)) continue;
                                bool hit = o.type == ObjectiveType.Defeat ? e.id == o.target : HasEnemy(e, o.target);
                                if (hit) Hint(QuestMapHintKind.Encounter, e.id, e.pos, default);
                            }
                            break;
                        case ObjectiveType.Flag:
                            // a boss objective written as Flag(<encounter's done flag>) rings that encounter too
                            foreach (var e in def.encounters)
                                if (e != null && Map.IsEncounterVisible(e) && MapRuntime.DoneFlag(e) == o.target) Hint(QuestMapHintKind.Encounter, e.id, e.pos, default);
                            break;
                        case ObjectiveType.Reach:
                            foreach (var r in def.regions)
                                if (r != null && r.id == o.target) Hint(QuestMapHintKind.Region, r.id, r.pos, r.size);
                            foreach (var t in def.transitions)
                                if (t != null && t.targetMap == o.target && Map.IsTransitionVisible(t)) Hint(QuestMapHintKind.Transition, t.id, t.pos, MapRuntime.TransitionSize(t));
                            break;
                        case ObjectiveType.Collect:
                            foreach (var c in def.chests)
                                if (c != null && Map.IsChestAvailable(c) && !Map.IsChestOpened(c.id) && ChestHolds(c, o.target)) Hint(QuestMapHintKind.Chest, c.id, c.pos, default);
                            break;
                    }
                }
            }
            return mapHints;
        }

        static bool HasEnemy(EncounterDef e, string creatureId)
        {
            if (e.enemies == null) return false;
            foreach (var en in e.enemies) if (en != null && en.creature == creatureId) return true;
            return false;
        }

        bool ChestHolds(ChestDef c, string itemId)
        {
            if (c.items != null && Array.IndexOf(c.items, itemId) >= 0) return true;
            if (string.IsNullOrEmpty(c.lootTable) || !Db.LootTables.TryGetValue(c.lootTable, out var lt) || lt.entries == null) return false;
            foreach (var en in lt.entries)
                if (en != null && (en.item == itemId || (en.pool != null && Array.IndexOf(en.pool, itemId) >= 0))) return true;
            return false;
        }
    }
}
