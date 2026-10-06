// Save game DTOs: plain classes with public fields (Lanternvale.Json.JsonWriter / JsonMapper). Versioned by
// SessionSaveData.version (GameSession.SaveVersion). Dictionaries are written with sorted keys so that
// save -> load -> save is byte-identical, except the units' `abilities`, which keep the learn order (the default action
// bar order and the AI's tie-break order; the loader preserves the JSON order). No readonly fields (the mapper skips them).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    [Serializable]
    public sealed class SessionSaveData
    {
        public int version = GameSession.SaveVersion;

        // ---- header (also read by GameSession.ReadSaveHeader)
        public string playerName = "";
        public ClassId playerClass;
        public int playerLevel = 1;
        public string mapId = "";
        public string mapName = "";
        public int day = 1;
        public float gameHour;
        public float playSeconds;
        /// <summary>Active party member ids in order ("player" first).</summary>
        public List<string> party = new List<string>();

        // ---- state
        public string leader = "";
        /// <summary>Rng state (xorshift128+ s0/s1) as 16-digit hex.</summary>
        public string rngS0 = "", rngS1 = "";
        public int gold;
        public List<ItemSaveData> inventory = new List<ItemSaveData>();
        /// <summary>Main character first, then every recruited companion.</summary>
        public List<UnitSaveData> roster = new List<UnitSaveData>();
        public Dictionary<string, int> approval = new Dictionary<string, int>();
        public List<VendorSaveData> vendors = new List<VendorSaveData>();
        /// <summary>Open loot window (null when none).</summary>
        public LootSaveData loot;
        public SessionSettings settings = new SessionSettings();
        public WorldSaveData world = new WorldSaveData();
        /// <summary>Encounters of the current map that do not trigger until the party has moved away from them (left or
        /// practice fights, finished encounter dialogues), sorted. Null in saves made before it was saved.</summary>
        public List<string> suppressedEncounters;
        /// <summary>Mind Soothe: encounter id -> real seconds left of its reduced trigger radius (sorted keys; null = none).</summary>
        public Dictionary<string, float> soothedEncounters;
        /// <summary>Raid state (null when not in a raid, and in saves made before raids existed).</summary>
        public RaidSaveData raid;
    }

    /// <summary>The raid in progress and the normal party to restore when it ends (lists sorted, see Docs/Expansion.md §2.7).</summary>
    [Serializable]
    public sealed class RaidSaveData
    {
        public int size;
        /// <summary>Member ids of the party before the raid.</summary>
        public List<string> normalParty = new List<string>();
        public string normalLeader = "";
        /// <summary>Member ids that had auto-play on before the raid.</summary>
        public List<string> normalAutoPlay = new List<string>();
    }

    [Serializable]
    public sealed class ItemSaveData
    {
        /// <summary>Database item id (also set for generated items: their generated id).</summary>
        public string id = "";
        public int count = 1;
        public string suffix = "";
        public string suffixName = "";
        /// <summary>Rolled random suffix stats (null when none).</summary>
        public List<StatModDef> suffixStats;
        /// <summary>Full definition of a generated (non-database) item, null for database items.</summary>
        public ItemDef generated;
    }

    [Serializable]
    public sealed class EquippedItemSaveData
    {
        public EquipSlot slot;
        public ItemSaveData item;
    }

    [Serializable]
    public sealed class AuraSaveData
    {
        public string id = "";
        /// <summary>"" (none / not a party unit), "self", a member id ("player", companion id) or "pet:&lt;ownerId&gt;".</summary>
        public string caster = "";
        public float duration;
        public float remaining;
        public int stacks = 1;
        public int charges;
        /// <summary>Source ability id ("" when none).</summary>
        public string source = "";
        public int rank = 1;
        public int effLevel = 1;
        public int learnLevel = 1;
        public int comboPoints;
        public float effectMult = 1f;
        public float damageMult = 1f;
        public float healingMult = 1f;
        public float tickAccum;
        public float absorbLeft;
        public float damageTaken;
        public Dictionary<string, float> vars;
    }

    [Serializable]
    public sealed class HunterPetSaveData
    {
        public string templateId = "";
        public string name = "";
        public float healthFraction = 1f;
        public bool dead;
    }

    [Serializable]
    public sealed class PetSaveData
    {
        public string creature = "";
        public string name = "";
        public int level = 1;
        public float health, mana, rage, energy, focus;
        public Vec2 position;
        public Vec2 facing;
        public bool autoPlay;
        /// <summary>Known abilities (id -> rank) in learn order, not sorted (see GameSession.InLearnOrder).</summary>
        public Dictionary<string, int> abilities = new Dictionary<string, int>();
        public Dictionary<string, float> cooldowns = new Dictionary<string, float>();
        public Dictionary<string, float> procCooldowns = new Dictionary<string, float>();
        public List<AuraSaveData> auras = new List<AuraSaveData>();
    }

    [Serializable]
    public sealed class UnitSaveData
    {
        /// <summary>"player" or the companion id.</summary>
        public string id = "";
        public string name = "";
        public ClassId classId;
        /// <summary>Companion definition id ("" for the main character).</summary>
        public string companion = "";
        public int level = 1;
        public int xp;
        public string sprite = "", portrait = "";
        public float health, mana, rage, energy, focus;
        public Vec2 position;
        public Vec2 facing;
        public bool autoPlay;
        public UnitRole role;
        public int respecCount;
        public float secondsSinceManaSpent;
        public float secondsSinceCombat;
        /// <summary>Known abilities (id -> rank) in learn order, not sorted (see GameSession.InLearnOrder).</summary>
        public Dictionary<string, int> abilities = new Dictionary<string, int>();
        public Dictionary<string, int> talents = new Dictionary<string, int>();
        public Dictionary<string, float> cooldowns = new Dictionary<string, float>();
        public Dictionary<string, float> procCooldowns = new Dictionary<string, float>();
        /// <summary>School name → seconds.</summary>
        public Dictionary<string, float> lockouts = new Dictionary<string, float>();
        public List<EquippedItemSaveData> equipment = new List<EquippedItemSaveData>();
        /// <summary>Non-passive auras (passives are rebuilt from abilities/talents).</summary>
        public List<AuraSaveData> auras = new List<AuraSaveData>();
        public HunterPetSaveData hunterPet;
        public PetSaveData pet;
        /// <summary>Living totems, hunter traps and temporary guardians placed out of combat (totems by element, then
        /// summons in order); null when none (and in saves made before they were saved).</summary>
        public List<SummonSaveData> summons;
    }

    /// <summary>A totem, trap or temporary guardian (Lightwell, Inferno...) owned by a party member (Session.OwnedSummons).</summary>
    [Serializable]
    public sealed class SummonSaveData
    {
        public string creature = "";
        /// <summary>Totem (traps too) or Summon.</summary>
        public UnitKind kind = UnitKind.Summon;
        /// <summary>Owner's totem slot (totems: "Earth", "Trap"...).</summary>
        public string totemElement = "";
        public string name = "";
        public int level = 1;
        /// <summary>Seconds of lifetime left (−1 = no limit).</summary>
        public float lifetime = -1f;
        public float health, mana, rage, energy, focus;
        public float maxHealthMult = 1f;
        public Vec2 position;
        public Vec2 facing;
        public bool autoPlay;
        /// <summary>Known abilities (id -> rank) in learn order, not sorted (see GameSession.InLearnOrder).</summary>
        public Dictionary<string, int> abilities = new Dictionary<string, int>();
        public Dictionary<string, float> cooldowns = new Dictionary<string, float>();
        public Dictionary<string, float> procCooldowns = new Dictionary<string, float>();
        /// <summary>Unit values of special handlers (Lightwell charges and rank), sorted; null when none.</summary>
        public Dictionary<string, float> vars;
        public List<AuraSaveData> auras = new List<AuraSaveData>();
    }

    [Serializable]
    public sealed class VendorSaveData
    {
        public string npc = "";
        public Dictionary<string, int> stock = new Dictionary<string, int>();
        public List<ItemSaveData> buyback = new List<ItemSaveData>();
    }

    [Serializable]
    public sealed class LootSaveData
    {
        public string source = "";
        public string title = "";
        public int gold;
        public List<ItemSaveData> items = new List<ItemSaveData>();
    }
}
