// What the world module (dialogue, quests, map state) needs from the game session. Pure C#.
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.World
{
    /// <summary>Snapshot of one party member, as seen by dialogue conditions and skill checks.</summary>
    [Serializable]
    public sealed class PartyMemberInfo
    {
        /// <summary>"player" (or any id) for the main character, otherwise the companion id.</summary>
        public string id = "";
        public string name = "";
        public ClassId classId;
        public int level = 1;
        public bool isMain;
        /// <summary>Current effective primary stats (base + gear + buffs).</summary>
        public PrimaryStats stats = new PrimaryStats();

        public PartyMemberInfo() { }

        public PartyMemberInfo(string id, string name, ClassId classId, int level, bool isMain, PrimaryStats stats = null)
        {
            this.id = id ?? "";
            this.name = name ?? "";
            this.classId = classId;
            this.level = level;
            this.isMain = isMain;
            this.stats = stats ?? new PrimaryStats();
        }
    }

    /// <summary>
    /// Implemented by the game session. Dialogue conditions/outcomes, quest objectives/rewards and skill checks go
    /// through this interface. Flags and the quest log are the world module's own classes, exposed here so every
    /// component sees the same state.
    /// </summary>
    public interface IDialogueContext
    {
        FlagStore Flags { get; }
        QuestLog Quests { get; }

        // ---------------------------------------------------------------- queries

        /// <summary>Main character's name ({player} token, "player" speaker).</summary>
        string PlayerName { get; }

        /// <summary>Active party; index 0 is the main character. Characters only (no pets/totems).</summary>
        IReadOnlyList<PartyMemberInfo> Party { get; }

        /// <summary>Party money in copper.</summary>
        int Gold { get; }

        /// <summary>How many of an item the party carries.</summary>
        int CountItem(string itemId);

        /// <summary>Companion approval (any scale; conditions compare against data `amount`).</summary>
        int GetApproval(string companionId);

        /// <summary>"dawn" | "day" | "dusk" | "night".</summary>
        string TimeOfDay { get; }

        /// <summary>Extra skill check bonus for a member (items, buffs). Usually 0.</summary>
        int SkillCheckBonus(string memberId, SkillCheck skill);

        // ---------------------------------------------------------------- actions

        void GiveItem(string itemId, int count);
        void TakeItem(string itemId, int count);
        void GiveGold(int copper);
        void TakeGold(int copper);
        /// <summary>Raw XP from data (dialogue outcome or quest reward); apply config.xpRate here if desired.</summary>
        void GiveXP(int amount);
        void Recruit(string companionId);
        void Dismiss(string companionId);
        void ChangeApproval(string companionId, int delta);
        /// <summary>Start the encounter with this id on the current map ("" = the dialogue owner's encounter).</summary>
        void StartCombat(string encounterId);
        void OpenVendor(string npcId);
        void OpenTrainer(string npcId);
        void OpenRespec(string npcId);
        /// <summary>Long rest (restore everything, advance time).</summary>
        void Rest();
        void HealParty();
        void Teleport(string mapId, string spawnId);
        /// <summary>Named code hook from data (`Special` outcome): key = special id, value/amount = arguments.</summary>
        void RunSpecial(string specialId, OutcomeDef outcome);
    }
}
