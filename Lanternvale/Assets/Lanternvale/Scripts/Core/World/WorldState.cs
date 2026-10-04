// Aggregate of the world module's persistent state: flags, quest log, dialogue memory and per-map runtimes.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    /// <summary>Everything the world module saves. Serialize with Lanternvale.Json.JsonWriter / JsonMapper.</summary>
    [Serializable]
    public sealed class WorldSaveData
    {
        public int version = 1;
        public FlagStoreState flags = new FlagStoreState();
        public QuestLogState quests = new QuestLogState();
        public DialogueMemoryState dialogue = new DialogueMemoryState();
        public List<MapRuntimeState> maps = new List<MapRuntimeState>();
    }

    public sealed class WorldState
    {
        public readonly GameDatabase Db;
        public readonly FlagStore Flags = new FlagStore();
        public readonly QuestLog Quests;
        public readonly DialogueMemory DialogueMemory = new DialogueMemory();

        readonly Dictionary<string, MapRuntime> maps = new Dictionary<string, MapRuntime>(StringComparer.Ordinal);
        readonly Dictionary<string, MapRuntimeState> savedMaps = new Dictionary<string, MapRuntimeState>(StringComparer.Ordinal);

        /// <summary>Wires FlagStore.Changed → QuestLog.OnFlag. ctx may be null and assigned later via Context.</summary>
        public WorldState(GameDatabase db, IDialogueContext ctx = null)
        {
            Db = db ?? new GameDatabase();
            Quests = new QuestLog(Db, ctx);
            Flags.Changed += (key, oldValue, newValue) => Quests.OnFlag(key);
        }

        public IDialogueContext Context
        {
            get => Quests.Context;
            set => Quests.Context = value;
        }

        /// <summary>Runtime of a map (cached, state persisted in saves). Null when the map id is unknown.</summary>
        public MapRuntime GetMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return null;
            if (maps.TryGetValue(mapId, out var rt)) return rt;
            if (!Db.Maps.TryGetValue(mapId, out var def))
            {
                Log.Warn($"WorldState: unknown map '{mapId}'");
                return null;
            }
            return GetMap(def);
        }

        /// <summary>Runtime for a map definition (registers ad-hoc maps not in the database).</summary>
        public MapRuntime GetMap(MapDef def)
        {
            if (def == null) return null;
            if (maps.TryGetValue(def.id, out var rt)) return rt;
            savedMaps.TryGetValue(def.id, out var st);
            rt = new MapRuntime(def, Flags, Quests, st);
            maps[def.id] = rt;
            return rt;
        }

        /// <summary>Map runtime states that exist (visited maps or loaded from a save).</summary>
        public IEnumerable<MapRuntime> LoadedMaps => maps.Values;

        public DialogueRunner CreateDialogueRunner(Rng rng = null) => new DialogueRunner(Db, Context, DialogueMemory, rng);

        public WorldSaveData Save()
        {
            var d = new WorldSaveData
            {
                flags = Flags.Save(),
                quests = Quests.Save(),
                dialogue = DialogueMemory.Save(),
            };
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var k in savedMaps.Keys) ids.Add(k);
            foreach (var k in maps.Keys) ids.Add(k);
            foreach (var id in ids)
            {
                if (maps.TryGetValue(id, out var rt)) d.maps.Add(rt.State);
                else d.maps.Add(savedMaps[id]);
            }
            return d;
        }

        /// <summary>Restores a snapshot. Existing MapRuntime objects are updated in place (references stay valid).</summary>
        public void Load(WorldSaveData data)
        {
            data ??= new WorldSaveData();
            Flags.Load(data.flags);
            Quests.Load(data.quests);
            DialogueMemory.Load(data.dialogue);
            savedMaps.Clear();
            if (data.maps != null)
                foreach (var m in data.maps)
                    if (m != null && !string.IsNullOrEmpty(m.mapId)) savedMaps[m.mapId] = m;
            foreach (var kv in maps)
            {
                savedMaps.TryGetValue(kv.Key, out var st);
                kv.Value.SetState(st);
            }
        }
    }
}
