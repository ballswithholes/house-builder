// Public value types of the game session: modes, events, options, settings and command results.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    /// <summary>What the game is doing right now (overlay windows like vendors/loot are separate state).</summary>
    public enum SessionMode { None, Exploration, Dialogue, Combat, GameOver }

    public enum CompanionStatus { NotRecruited, Active, Camp, Away }

    public enum SurpriseMode { None, EnemiesSurprised, PartySurprised }

    public enum CombatEndKind { None, Victory, Defeat, Left }

    public enum SessionEventKind
    {
        Toast,
        GameStarted, GameLoaded, GameOver,
        MapEntered, RegionEntered, TransitionLocked,
        DialogueStarted, DialogueEnded, SkillCheck,
        QuestStarted, QuestUpdated, QuestCompleted, QuestFailed, QuestRewardChoice,
        ItemReceived, ItemLost, GoldChanged, XpGained,
        LevelUp, AbilityLearned, TalentPointsAvailable,
        CompanionRecruited, CompanionDismissed, PartyChanged, LeaderChanged, ApprovalChanged, PetChanged,
        VendorOpened, VendorClosed, TrainerOpened, TrainerClosed, RespecOpened, RespecClosed,
        LootOpened, LootClosed, ChestOpened,
        CombatStarted, CombatEnded,
        Rested, PartyHealed,
        SpecialOutcome,
        TimeOfDayChanged,
    }

    /// <summary>A notification for the UI (toasts, window requests, state changes). See Docs/SessionAPI.md §2.</summary>
    public sealed class SessionEvent
    {
        public SessionEventKind Kind;
        /// <summary>Toast-ready text (may be empty).</summary>
        public string Text = "";
        /// <summary>Main id (quest, item, companion, npc, map, encounter, special…).</summary>
        public string Id = "";
        /// <summary>Secondary id (spawn id, dialogue owner…).</summary>
        public string Id2 = "";
        /// <summary>Count, delta, level… depending on the kind.</summary>
        public int Amount;
        public Unit Unit;
        public ItemInstance Item;
        public QuestEvent Quest;
        public LevelUpInfo LevelUp;
        public CheckResult Check;
        public Battle Battle;
        public LootWindow Loot;
        public CombatEndKind Outcome;

        public override string ToString() => $"{Kind}{(Id.Length > 0 ? " " + Id : "")}{(Text.Length > 0 ? ": " + Text : "")}";
    }

    /// <summary>Options of <see cref="GameSession.NewGame(NewGameOptions)"/>.</summary>
    public sealed class NewGameOptions
    {
        public string Name = "Traveller";
        public ClassId Class = ClassId.Warrior;
        /// <summary>1..maxLevel. Above 1: all trainable ranks learned, talents auto-allocated, optional gear and gold.</summary>
        public int StartLevel = 1;
        /// <summary>Art key overrides for the main character ("" = class art).</summary>
        public string Sprite = "", Portrait = "";
        /// <summary>Veteran starts (StartLevel &gt; 1): equip level-appropriate gear (also given to companions recruited later).</summary>
        public bool VeteranGear = true;
        /// <summary>Veteran starts: spend the talent points following ClassDef.defaultBuild.</summary>
        public bool AutoAllocateTalents = true;
        /// <summary>Start config.startDialogue right away.</summary>
        public bool PlayOpening = true;
    }

    /// <summary>Per-game settings (saved with the game).</summary>
    [Serializable]
    public sealed class SessionSettings
    {
        /// <summary>Default auto-play state of newly recruited companions (companion AI plays them in combat).</summary>
        public bool CompanionAutoPlay = false;           // BG3-style: the player controls every party member by default
        /// <summary>Companions learn every rank available at their new level for free when they level up.</summary>
        public bool CompanionAutoTrain = true;
        /// <summary>Companions spend new talent points on their preferred build automatically.</summary>
        public bool AutoAllocateCompanionTalents = true;
        /// <summary>Game hours that pass per real minute out of combat (1 = DayNight default).</summary>
        public float GameHoursPerRealMinute = 1f;
        /// <summary>Hour of day a new game starts at (the opening scene is at dusk).</summary>
        public float StartHour = 18.5f;
        /// <summary>Set by NewGame: recruits receive level-appropriate gear too.</summary>
        public bool VeteranGear;
    }

    /// <summary>Items and gold waiting to be picked up (after a battle or from a chest).</summary>
    public sealed class LootWindow
    {
        /// <summary>"battle" or the chest id.</summary>
        public string Source = "";
        public string Title = "";
        public readonly List<ItemInstance> Items = new List<ItemInstance>();
        /// <summary>Copper already added to the purse when the window opened.</summary>
        public int Gold;
        public bool IsEmpty => Items.Count == 0;
    }

    /// <summary>What <see cref="GameSession.FinishBattle"/> applied.</summary>
    public sealed class BattleSummary
    {
        public CombatEndKind Outcome;
        public string EncounterId = "";
        public int Rounds;
        /// <summary>XP gained by the party (rate applied).</summary>
        public int Xp;
        public readonly List<LevelUpInfo> LevelUps = new List<LevelUpInfo>();
        /// <summary>Creature ids killed.</summary>
        public readonly List<string> Killed = new List<string>();
        public LootWindow Loot;
    }

    public enum InteractKind { None, Dialogue, Loot, Locked, Travel, Text }

    public sealed class InteractResult
    {
        public bool Ok;
        public InteractKind Kind;
        public string Message = "";
        public string Id = "";
        public static InteractResult Fail(string why) => new InteractResult { Ok = false, Message = why ?? "" };
        public override string ToString() => Ok ? $"{Kind} {Id}" : "fail: " + Message;
    }

    public enum TriggerKind { None, Dialogue, Combat, Travel, Locked }

    /// <summary>Result of a position update: Stop = the UI should stop animating movement.</summary>
    public sealed class TriggerResult
    {
        public bool Stop;
        public TriggerKind Kind;
        public string Id = "";
        public static readonly TriggerResult Nothing = new TriggerResult();
        public override string ToString() => Stop ? $"{Kind} {Id}" : "none";
    }

    /// <summary>Result of the instant <see cref="GameSession.MoveLeader"/>.</summary>
    public sealed class MoveResult
    {
        public bool Moved;
        public Vec2 End;
        public float Distance;
        public TriggerResult Trigger = TriggerResult.Nothing;
        public string Reason = "";
    }

    public sealed class UnitMovePlan
    {
        public Unit Unit;
        public Vec2 Destination;
        public List<Vec2> Path = new List<Vec2>();
    }

    /// <summary>Paths for animating a party move (nothing is changed until positions are reported).</summary>
    public sealed class PartyMovePlan
    {
        public Unit Leader;
        public List<Vec2> LeaderPath = new List<Vec2>();
        public float Length;
        public bool Reachable;
        public readonly List<UnitMovePlan> Followers = new List<UnitMovePlan>();
    }

    /// <summary>Header of a save game (for save-slot lists).</summary>
    [Serializable]
    public sealed class SaveHeader
    {
        public int version;
        public string playerName = "";
        public ClassId playerClass;
        public int playerLevel;
        public string mapId = "";
        public string mapName = "";
        public int day;
        public float gameHour;
        public float playSeconds;
        public List<string> party = new List<string>();
    }
}
