// Data guards for combat pacing: enemy creature heals land at most once per turn, and the low-rank bolts and heals that
// WoW casts faster at Rank 1 carry rankCastTimes (Rank 1 fast, every later rank at castTime).
using System;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsDataCombatPacing
    {
        [Test]
        public static void CreatureHealsHaveATurnCooldown()
        {
            int checkedHeals = 0;
            // enemy creatures only: totem pulses fire once per turn by design, and class pets follow their class data
            foreach (var c in Db.Creatures.Values.Where(x => x.rank != CreatureRank.Totem && x.rank != CreatureRank.Pet).OrderBy(x => x.id, StringComparer.Ordinal))
                foreach (var entry in c.abilities)
                {
                    var a = Db.Ability(entry.ability);
                    if (a == null || !a.effects.Any(e => e.type == EffectType.Heal)) continue;
                    checkedHeals++;
                    Assert(a.cooldown >= RulesConstants.TurnSeconds - 1e-3f,
                        c.id + ": " + a.id + " heals with cooldown " + a.cooldown + " s (a healer would cast it every step of its turn)");
                }
            Assert(checkedHeals > 0, "no creature heals found");
        }

        static readonly (string id, float rank1)[] FastRankOne =
        {
            ("mage_fireball", 1.5f), ("mage_frostbolt", 1.5f), ("warlock_shadow_bolt", 1.7f), ("priest_smite", 1.5f),
            ("priest_lesser_heal", 1.5f), ("shaman_lightning_bolt", 1.5f), ("shaman_healing_wave", 1.5f),
        };

        [Test]
        public static void LowRankBoltsAndHealsCastFasterAtRankOne()
        {
            foreach (var (id, rank1) in FastRankOne)
            {
                var a = Db.Ability(id);
                Assert(a != null, "no ability " + id);
                int ranks = Math.Max(1, a.rankLevels.Length);
                AssertNear(AbilityRules.BaseCastTime(a, 1), rank1, 1e-4f, id + " Rank 1 cast time");
                for (int r = 2; r <= ranks; r++)
                    AssertNear(AbilityRules.BaseCastTime(a, r), a.castTime, 1e-4f, id + " Rank " + r + " cast time");
                AssertNear(AbilityRules.BaseCastTime(a, 0), a.castTime, 1e-4f, id + " top rank cast time");
            }
        }
    }
}
