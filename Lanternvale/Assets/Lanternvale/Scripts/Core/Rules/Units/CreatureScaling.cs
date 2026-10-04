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
    /// <item>Spell/heal magnitudes of unowned creatures above level 20: the level-20 value × MeleeDps(L) / MeleeDps(20)
    /// (<see cref="SpellMagnitudeLevel"/>)</item>
    /// </list>
    /// Rank multipliers (the content's healthMult/damageMult/armorMult apply on top):
    /// health Elite/Rare ×3, Boss ×7, Normal ×2, Minion ×1, Critter ×0.2 (Pet ×1); damage Elite/Rare ×1.5, Boss ×2, Minion ×0.6,
    /// Pet ×0.75, Critter ×0.1; armor Critter ×0.5. (The Hollow Warden, Boss healthMult 1.15, at party level 12
    /// → level 14 → 383 × 7 × 1.15 ≈ 3,080 health.) Totems have 5 + 3·L health.
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
                case CreatureRank.Elite: case CreatureRank.Rare: return 3f;
                case CreatureRank.Boss: return 7f;
                // the base curve is WoW's one-on-one health; encounters here are 2-3 normals against a 3-4 member party,
                // which at ×1 ended in 1-2 rounds with the back of the initiative order never acting
                case CreatureRank.Normal: return 2f;
                case CreatureRank.Minion: return 1f;
                case CreatureRank.Critter: return 0.2f;
                default: return 1f;
            }
        }

        public static float DamageRankMult(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Elite: case CreatureRank.Rare: return 1.5f;
                case CreatureRank.Boss: return 2f;
                case CreatureRank.Minion: return 0.6f;
                case CreatureRank.Pet: return 0.75f;
                case CreatureRank.Critter: return 0.1f;
                default: return 1f;
            }
        }

        public static float ArmorRankMult(CreatureRank r) => r == CreatureRank.Critter ? 0.5f : 1f;

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

        /// <summary>Level up to which creature spell and heal magnitudes follow their data (min..max + perLevel).</summary>
        public const int SpellCurveLevel = 20;

        /// <summary>
        /// Level at which a damage/heal effect's min..max + perLevel magnitude is read for this caster, and the multiplier on
        /// top (<paramref name="mult"/>). The data's perLevel is linear and tuned for the early levels, while creature health
        /// and melee grow quadratically: above level 20 (or the ability's learn level, when higher) an unowned creature's
        /// spells and heals (Grey Bolt, Lantern Requiem, Dark Mend, their DoT ticks...) take their level-20 value scaled by
        /// the melee DPS curve, so a caster's spells keep pace with its swings and a 6 s telegraph stays worth interrupting at
        /// any level. Class characters, and the pets, totems and summons of a character, keep the linear values (as does
        /// everything at or below level 20). A creature's own summons (the Warden's wisps) count as creatures.
        /// </summary>
        public static int SpellMagnitudeLevel(Unit caster, int effLevel, int learnLevel, out float mult)
        {
            mult = 1f;
            if (!UsesCreatureSpellCurve(caster)) return effLevel;
            int anchor = Math.Max(SpellCurveLevel, learnLevel);
            if (effLevel <= anchor) return effLevel;
            mult = MeleeDps(effLevel) / MeleeDps(anchor);
            return anchor;
        }

        /// <summary>A creature (no class) that is not, directly or through its summoner, a class character's pet, totem or summon.</summary>
        public static bool UsesCreatureSpellCurve(Unit u)
        {
            if (u == null || u.Class != null || u.Creature == null) return false;
            var root = u;
            for (int guard = 0; root.Owner != null && guard < 8; guard++) root = root.Owner;
            return root.Class == null;
        }

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
