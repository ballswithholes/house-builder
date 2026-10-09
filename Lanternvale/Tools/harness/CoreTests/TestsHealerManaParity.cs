// Regression test: the shaman's heals got the same Lanternvale cost deviation as the priest's (classes/*.json
// `_costNote`). A healer casts every 6-second turn, so the five-second rule never lapses in combat; at WoW Classic costs
// Lesser Healing Wave healed 1.7 per mana against Flash Heal's 4.3, and Nanami (the expansion's shaman healer) ran dry by
// round 5-7 of a dungeon boss the priests won.
using System;
using Lanternvale.Data;
using Lanternvale.Rules;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsHealerManaParity
    {
        /// <summary>Average healing per mana of the highest rank trainable at <paramref name="level"/> (no talents or gear:
        /// the same rank-level scaling as AbilityRules.ResourceCost and the Heal effect).</summary>
        static float HealPerMana(string abilityId, int level)
        {
            var a = Db.Ability(abilityId);
            Assert(a != null && a.cost != null && a.cost.type == ResourceType.Mana, abilityId + " is a mana spell");
            int rank = AbilityRules.MaxRankAtLevel(a, level);
            Assert(rank > 0, $"{abilityId} is trainable at L{level}");
            int dl = Math.Max(0, AbilityRules.RankLevel(a, rank) - a.learnLevel);
            var e = a.effects.Find(x => x.type == EffectType.Heal);
            Assert(e != null, abilityId + " heals");
            float heal = (e.min + Math.Max(e.min, e.max)) / 2f + e.perLevel * dl;
            float cost = a.cost.amount + a.cost.perLevel * dl;
            return heal / Math.Max(1f, cost);
        }

        [Test]
        public static void ShamanHealsCostLikeThePriests()
        {
            for (int level = 20; level <= 33; level++)
            {
                float fast = HealPerMana("shaman_lesser_healing_wave", level), flash = HealPerMana("priest_flash_heal", level);
                Assert(fast >= 0.6f * flash, $"L{level}: Lesser Healing Wave heals {fast:0.00}/mana, Flash Heal {flash:0.00}");
                float big = HealPerMana("shaman_healing_wave", level), heal = HealPerMana("priest_heal", level);
                Assert(big >= 0.6f * heal, $"L{level}: Healing Wave heals {big:0.00}/mana, Heal {heal:0.00}");
            }
        }
    }
}
