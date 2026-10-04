// Loads and indexes every DataBundle. Pure C#: the Unity layer feeds it TextAsset contents,
// the test harness feeds it files from disk.
using System;
using System.Collections.Generic;
using Lanternvale.Json;
using Lanternvale.Util;

namespace Lanternvale.Data
{
    public sealed class GameDatabase
    {
        public readonly Dictionary<ClassId, ClassDef> Classes = new Dictionary<ClassId, ClassDef>();
        public readonly Dictionary<string, AbilityDef> Abilities = new Dictionary<string, AbilityDef>();
        public readonly Dictionary<string, AuraDef> Auras = new Dictionary<string, AuraDef>();
        public readonly Dictionary<string, TalentTreeDef> TalentTrees = new Dictionary<string, TalentTreeDef>();
        public readonly Dictionary<string, TalentDef> Talents = new Dictionary<string, TalentDef>();
        public readonly Dictionary<string, TalentTreeDef> TreeOfTalent = new Dictionary<string, TalentTreeDef>();
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>();
        public readonly Dictionary<string, ItemSuffixDef> ItemSuffixes = new Dictionary<string, ItemSuffixDef>();
        public readonly Dictionary<string, LootTableDef> LootTables = new Dictionary<string, LootTableDef>();
        public readonly Dictionary<string, CreatureDef> Creatures = new Dictionary<string, CreatureDef>();
        public readonly Dictionary<string, NpcDef> Npcs = new Dictionary<string, NpcDef>();
        public readonly Dictionary<string, CompanionDef> Companions = new Dictionary<string, CompanionDef>();
        public readonly Dictionary<string, DialogueDef> Dialogues = new Dictionary<string, DialogueDef>();
        public readonly Dictionary<string, QuestDef> Quests = new Dictionary<string, QuestDef>();
        public readonly Dictionary<string, MapDef> Maps = new Dictionary<string, MapDef>();
        public readonly Dictionary<string, SpecialDoc> Specials = new Dictionary<string, SpecialDoc>();
        public GameConfigDef Config = new GameConfigDef();

        /// <summary>Problems found while parsing/mapping (unknown keys, bad enums, duplicate ids).</summary>
        public readonly List<string> Problems = new List<string>();

        public static GameDatabase Load(IEnumerable<KeyValuePair<string, string>> files)
        {
            var db = new GameDatabase();
            foreach (var f in files) db.AddFile(f.Key, f.Value);
            db.Finish();
            return db;
        }

        public void AddFile(string name, string text)
        {
            DataBundle bundle;
            var ctx = new JsonMapContext { Source = name };
            try
            {
                bundle = JsonMapper.FromJson<DataBundle>(text, ctx, name);
            }
            catch (Exception e)
            {
                Problems.Add($"{name}: parse error: {e.Message}");
                return;
            }
            Problems.AddRange(ctx.Problems);
            if (bundle == null) return;

            foreach (var c in bundle.classes) AddUnique(Classes, c.id, c, name, "class");
            foreach (var a in bundle.abilities) AddUnique(Abilities, a.id, a, name, "ability");
            foreach (var a in bundle.auras) AddUnique(Auras, a.id, a, name, "aura");
            foreach (var t in bundle.talentTrees)
            {
                AddUnique(TalentTrees, t.id, t, name, "talent tree");
                foreach (var tal in t.talents)
                {
                    AddUnique(Talents, tal.id, tal, name, "talent");
                    TreeOfTalent[tal.id] = t;
                }
            }
            foreach (var i in bundle.items) AddUnique(Items, i.id, i, name, "item");
            foreach (var s in bundle.itemSuffixes) AddUnique(ItemSuffixes, s.id, s, name, "item suffix");
            foreach (var l in bundle.lootTables) AddUnique(LootTables, l.id, l, name, "loot table");
            foreach (var c in bundle.creatures) AddUnique(Creatures, c.id, c, name, "creature");
            foreach (var n in bundle.npcs) AddUnique(Npcs, n.id, n, name, "npc");
            foreach (var c in bundle.companions) AddUnique(Companions, c.id, c, name, "companion");
            foreach (var d in bundle.dialogues) AddUnique(Dialogues, d.id, d, name, "dialogue");
            foreach (var q in bundle.quests) AddUnique(Quests, q.id, q, name, "quest");
            foreach (var m in bundle.maps) AddUnique(Maps, m.id, m, name, "map");
            foreach (var s in bundle.specials) AddUnique(Specials, s.id, s, name, "special");
            if (bundle.config != null) Config = bundle.config;
        }

        void AddUnique<TKey, TVal>(Dictionary<TKey, TVal> dict, TKey key, TVal val, string file, string kind)
        {
            if (key == null || (key is string s && s.Length == 0))
            {
                Problems.Add($"{file}: {kind} with empty id");
                return;
            }
            if (dict.ContainsKey(key)) Problems.Add($"{file}: duplicate {kind} id '{key}'");
            dict[key] = val;
        }

        void Finish()
        {
            // Fill defaults that depend on other fields.
            foreach (var a in Abilities.Values)
                foreach (var e in a.effects)
                    if (e.max < e.min) e.max = e.min;
            foreach (var a in Auras.Values)
            {
                foreach (var e in a.tickEffects) if (e.max < e.min) e.max = e.min;
                foreach (var e in a.onApply) if (e.max < e.min) e.max = e.min;
                foreach (var p in a.procs) foreach (var e in p.effects) if (e.max < e.min) e.max = e.min;
            }
            if (Config.xpToLevel == null || Config.xpToLevel.Length == 0) Config.xpToLevel = DefaultXpTable;
        }

        public AbilityDef Ability(string id) => id != null && Abilities.TryGetValue(id, out var a) ? a : null;
        public AuraDef Aura(string id) => id != null && Auras.TryGetValue(id, out var a) ? a : null;
        public ItemDef Item(string id) => id != null && Items.TryGetValue(id, out var a) ? a : null;
        public CreatureDef Creature(string id) => id != null && Creatures.TryGetValue(id, out var a) ? a : null;
        public ClassDef Class(ClassId id) => Classes.TryGetValue(id, out var c) ? c : null;
        public TalentDef Talent(string id) => id != null && Talents.TryGetValue(id, out var t) ? t : null;

        /// <summary>WoW Classic experience needed to go from level (i+1) to (i+2).</summary>
        public static readonly int[] DefaultXpTable =
        {
            400, 900, 1400, 2100, 2800, 3600, 4500, 5400, 6500, 7600, 8800, 10100, 11400, 12900, 14400, 16000,
            17700, 19400, 21300, 23200, 25200, 27300, 29400, 31700, 34000, 36400, 38900, 41400, 44300, 47400,
            50800, 54500, 58600, 62800, 67100, 71600, 76100, 80800, 85700, 90700, 95800, 101000, 106300, 111800,
            117500, 123200, 129100, 135100, 141200, 147500, 153900, 160400, 167100, 173900, 180800, 187900,
            195000, 202300, 209800,
        };
    }
}
