// Level curves for creatures, pets, totems and summons (documented in Docs/CoreAPI.md §Creatures).
using System;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    /// <summary>
    /// WoW-Classic-like creature curves (normal rank, before CreatureDef multipliers):
    /// <list type="bullet">
    /// <item>Health(L) = 42 + 16.5·(L−1) + 0.75·(L−1)²  (L1 42, L10 251, L20 626, L30 1151, L40 1826, L60 3626)</item>
    /// <item>Melee DPS(L) = (1 + L + 0.03·L²) / 2; one swing = DPS × attackSpeed (L10 7 dps, L30 29, L60 85)</item>
    /// <item>Armor(L) = 20·L + 0.5·L²  (L10 250, L30 1050, L60 3000)</item>
    /// <item>Mana(L) = 60 + 25·L + 0.4·L²  (only creatures whose resource is Mana)</item>
    /// <item>Primary stats (initiative, skill checks) = 15 + 1.5·L</item>
    /// </list>
    /// Rank multipliers (health / damage / armor): Normal 1/1/1, Elite 2.5/1.4/1.2, Rare 1.6/1.2/1.1,
    /// Boss 7/1.8/1.3, Minion 0.5/0.6/0.8, Pet 1/0.75/1, Totem: health 5 + 3·L, damage 1, Critter 0.2/0.1/0.5.
    /// </summary>
    public static class CreatureScaling
    {
        public static float BaseHealth(int level)
        {
            float l = Math.Max(0, level - 1);
            return 42f + 16.5f * l + 0.75f * l * l;
        }

        public static float MeleeDps(int level) => (1f + level + 0.03f * level * level) / 2f;
        public static float BaseArmor(int level) => 20f * level + 0.5f * level * level;
        public static float BaseMana(int level) => 60f + 25f * level + 0.4f * level * level;
        public static float PrimaryStat(int level) => 15f + 1.5f * level;

        public static float HealthRankMult(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Elite: return 2.5f;
                case CreatureRank.Rare: return 1.6f;
                case CreatureRank.Boss: return 7f;
                case CreatureRank.Minion: return 0.5f;
                case CreatureRank.Critter: return 0.2f;
                default: return 1f;
            }
        }

        public static float DamageRankMult(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Elite: return 1.4f;
                case CreatureRank.Rare: return 1.2f;
                case CreatureRank.Boss: return 1.8f;
                case CreatureRank.Minion: return 0.6f;
                case CreatureRank.Pet: return 0.75f;
                case CreatureRank.Critter: return 0.1f;
                default: return 1f;
            }
        }

        public static float ArmorRankMult(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Elite: return 1.2f;
                case CreatureRank.Rare: return 1.1f;
                case CreatureRank.Boss: return 1.3f;
                case CreatureRank.Minion: return 0.8f;
                case CreatureRank.Critter: return 0.5f;
                default: return 1f;
            }
        }

        public static float Health(CreatureDef c, int level)
        {
            if (c.rank == CreatureRank.Totem) return (5f + 3f * level) * c.healthMult;
            return BaseHealth(level) * HealthRankMult(c.rank) * c.healthMult;
        }

        public static float Armor(CreatureDef c, int level) => BaseArmor(level) * ArmorRankMult(c.rank) * c.armorMult;

        public static float Mana(CreatureDef c, int level) => c.resource == ResourceType.Mana ? BaseMana(level) * c.manaMult : 0f;

        /// <summary>Damage multiplier applied to everything the creature deals (rank × damageMult).</summary>
        public static float DamageMult(CreatureDef c) => DamageRankMult(c.rank) * c.damageMult;

        /// <summary>Average damage of one basic attack at the creature's attack speed (before damageMult).</summary>
        public static float SwingAverage(CreatureDef c, int level) => MeleeDps(level) * Math.Max(0.5f, c.attackSpeed);

        /// <summary>Creatures that can parry frontal melee attacks (beasts, critters, elementals, totems cannot).</summary>
        public static bool CanParry(CreatureDef c)
        {
            switch (c.type)
            {
                case CreatureType.Beast:
                case CreatureType.Critter:
                case CreatureType.Totem:
                case CreatureType.Elemental:
                case CreatureType.Mechanical:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>Unit radius in metres from the creature's sprite height.</summary>
        public static float Radius(CreatureDef c) => Math.Max(0.3f, Math.Min(1.6f, c.size * 0.22f));
    }
}
