// WoW Classic formulas used by the rules engine (Design.md §2-§3). Pure functions, no state.
using System;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    /// <summary>Engine-wide constants (Design.md §2). Values are WoW Classic numbers adapted to 6 s turns.</summary>
    public static class RulesConstants
    {
        public const float TurnSeconds = 6f;          // one round / one turn of Time
        public const float EnergyPerTurn = 60f;       // +20 per 2 s
        public const float EnergyPerSecondOoc = 10f;
        public const float FocusPerTurn = 36f;        // +24 per 4 s
        public const float FocusPerSecondOoc = 6f;
        public const float MaxEnergy = 100f, MaxRage = 100f, MaxFocus = 100f, MaxComboPoints = 5f;
        public const float RageDecayPerSecondOoc = 1f;
        public const float RageTurnCompression = 1.75f;
        public const float DefaultGcd = 1.5f;
        public const float DefaultMoveMetres = 9f;
        public const float DefaultMeleeReach = 2.2f;
        public const float DefaultUnitRadius = 0.4f;
        public const float StealthDetectMetres = 3f;  // stealthed units are seen within this distance
        public const float HelpUpTime = 1.5f;
        public const float FiveSecondRule = 5f;
        public const int MaxSoulShards = 32;
        public const string SoulShardItem = "soul_shard";
        public const float DualWieldMissPenalty = 19f;
        public const float OffHandDamageFactor = 0.5f;
        public const float AvoidedRageRefund = 0.8f;   // dodged/parried/missed rage abilities refund 80%
        public const float MaxHitChance = 99f;
        public const float BehindDot = -0.1f;          // attacker is "behind" when dot(facing, toAttacker) < this
        public const int MaxProcDepth = 3;
        /// <summary>Out of combat, game clock seconds per real second (time of day).</summary>
        public const float GameClockScale = 30f;
    }

    public static class Formulas
    {
        /// <summary>Linear interpolation of a level-1 / level-60 value pair.</summary>
        public static float LevelLerp(float atLevel1, float atLevel60, int level)
        {
            if (level <= 1) return atLevel1;
            return atLevel1 + (atLevel60 - atLevel1) * (Math.Min(level, 60) - 1) / 59f + (level > 60 ? (atLevel60 - atLevel1) * (level - 60) / 59f : 0f);
        }

        public static float HealthFromStamina(float sta) => Math.Min(20f, sta) + Math.Max(0f, sta - 20f) * 10f;
        public static float ManaFromIntellect(float intel) => Math.Min(20f, intel) + Math.Max(0f, intel - 20f) * 15f;

        /// <summary>Melee attack power from the class formula (Design.md §3).</summary>
        public static float MeleeAttackPower(ApFormula f, float str, float agi, int level)
        {
            switch (f)
            {
                case ApFormula.StrengthTimesTwo: return str * 2f + level * 3f - 20f;
                case ApFormula.StrengthPlusAgility: return str + agi + level * 2f - 20f;
                default: return str - 10f;
            }
        }

        /// <summary>Ranged attack power. Hunter: Agi×2 + L×2 − 10; Warrior/Rogue: Agi + L − 10; casters: 0.</summary>
        public static float RangedAttackPower(ClassDef c, float agi, int level)
        {
            if (c == null) return 0f;
            if (c.rangedApAgilityTimesTwo || c.id == ClassId.Hunter) return agi * 2f + level * 2f - 10f;
            if (c.id == ClassId.Warrior || c.id == ClassId.Rogue || c.rangedAp != ApFormula.Strength) return agi + level - 10f;
            return 0f;
        }

        static float Ratio(float perPctAt60, int level) => Math.Max(1f, perPctAt60 * level / 60f);

        public static float MeleeCritFromAgility(ClassDef c, float agi, int level) => c == null ? 5f : c.baseMeleeCrit + agi / Ratio(c.agilityPerMeleeCritAt60, level);
        public static float SpellCritFromIntellect(ClassDef c, float intel, int level) => c == null ? 5f : c.baseSpellCrit + intel / Ratio(c.intellectPerSpellCritAt60, level);
        public static float DodgeFromAgility(ClassDef c, float agi, int level) => c == null ? 5f : c.baseDodge + agi / Ratio(c.agilityPerDodgeAt60, level);

        /// <summary>Armor damage reduction (0..0.75) against an attacker of the given level.</summary>
        public static float ArmorReduction(float armor, int attackerLevel)
        {
            if (armor <= 0) return 0f;
            var r = armor / (armor + 400f + 85f * attackerLevel);
            return MathUtil.Clamp(r, 0f, 0.75f);
        }

        /// <summary>Average resistance mitigation (0..0.75).</summary>
        public static float ResistReduction(float resist, int attackerLevel)
        {
            if (resist <= 0) return 0f;
            return MathUtil.Clamp(resist / (5f * Math.Max(1, attackerLevel)) * 0.75f, 0f, 0.75f);
        }

        /// <summary>Rage conversion value c(L) = 0.0091107836 L² + 3.225598133 L + 4.2652911.</summary>
        public static float RageConversion(int level) => 0.0091107836f * level * level + 3.225598133f * level + 4.2652911f;

        public static float RageFromDamageDealt(float damage, int level) => 7.5f * damage / RageConversion(level) * RulesConstants.RageTurnCompression;
        public static float RageFromDamageTaken(float damage, int level) => 2.5f * damage / RageConversion(level);

        /// <summary>Base melee miss chance against a target (before hit bonuses).</summary>
        public static float MeleeMissChance(int attackerLevel, int targetLevel, bool dualWieldWhite)
        {
            int diff = targetLevel - attackerLevel;
            float miss = diff > 0 ? 5f + diff : Math.Max(0f, 5f + diff * 0.5f);
            if (dualWieldWhite) miss += RulesConstants.DualWieldMissPenalty;
            return miss;
        }

        /// <summary>Base spell miss chance: 4% (+1/+2/+11 for targets 1/2/3 levels higher, +11 per extra level).</summary>
        public static float SpellMissChance(int attackerLevel, int targetLevel)
        {
            int diff = targetLevel - attackerLevel;
            if (diff <= 0) return Math.Max(1f, 4f + diff);
            if (diff == 1) return 5f;
            if (diff == 2) return 6f;
            return 15f + 11f * (diff - 3);
        }

        /// <summary>Initiative roll: d20 + (Agi − 20) / 10.</summary>
        public static float Initiative(int d20, float agility) => d20 + (agility - 20f) / 10f;

        // ------------------------------------------------------------ experience

        /// <summary>WoW Classic grey level: mobs at or below this level give no XP.</summary>
        public static int GreyLevel(int playerLevel)
        {
            if (playerLevel <= 5) return 0;
            if (playerLevel <= 39) return playerLevel - 5 - playerLevel / 10;
            if (playerLevel <= 59) return playerLevel - 1 - playerLevel / 5;
            return playerLevel - 9;
        }

        /// <summary>WoW Classic "zero difference" value used for lower-level mob XP.</summary>
        public static int ZeroDifference(int level)
        {
            if (level < 8) return 5;
            if (level < 10) return 6;
            if (level < 12) return 7;
            if (level < 16) return 8;
            if (level < 20) return 9;
            if (level < 30) return 10;
            if (level < 40) return 11;
            if (level < 45) return 12;
            if (level < 50) return 13;
            if (level < 55) return 14;
            if (level < 60) return 15;
            return 16;
        }

        /// <summary>WoW Classic kill XP for one character (before rate/elite multipliers).</summary>
        public static float MobXp(int playerLevel, int mobLevel)
        {
            float baseXp = playerLevel * 5f + 45f;
            if (mobLevel >= playerLevel)
            {
                int d = Math.Min(4, mobLevel - playerLevel);
                return baseXp * (1f + 0.05f * d);
            }
            if (mobLevel <= GreyLevel(playerLevel)) return 0f;
            float zd = ZeroDifference(playerLevel);
            return Math.Max(0f, baseXp * (1f - (playerLevel - mobLevel) / zd));
        }
    }
}
