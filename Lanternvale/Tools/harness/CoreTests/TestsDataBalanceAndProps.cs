// Data guards for content tuning and prop placement:
//   * an enemy's instant damage ability carries a turn cooldown (the AI rolls its chance once per turn and then uses
//     a ready ability on every GCD, so a cooldown-free instant was used up to four times a turn);
//   * a lone Elite in an encounter carries at least the health of a three-Normal pack at its level;
//   * class pets that cast damage spells keep most of the spells' own WoW numbers (the creature damage multiplier
//     scales spells as well as melee);
//   * prop colliders and light offsets are unscaled sprite units (MapView/NavGrid multiply them by prop.scale), so a
//     scaled prop uses the same values as the unscaled props of its art, and a spirit lantern's light sits at its lamp.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsDataBalanceAndProps
    {
        [Test]
        public static void CreatureInstantDamageHasATurnCooldown()
        {
            int checkedAbilities = 0;
            foreach (var c in Db.Creatures.Values.Where(x => x.rank != CreatureRank.Totem && x.rank != CreatureRank.Pet).OrderBy(x => x.id, StringComparer.Ordinal))
                foreach (var entry in c.abilities)
                {
                    var a = Db.Ability(entry.ability);
                    if (a == null || entry.chance <= 0f || a.channeled || AbilityRules.BaseCastTime(a, 0) > 0f) continue;
                    if (!a.effects.Any(e => e.type == EffectType.Damage || e.type == EffectType.WeaponDamage)) continue;
                    checkedAbilities++;
                    Assert(a.cooldown >= RulesConstants.TurnSeconds - 1e-3f,
                        c.id + ": instant " + a.id + " deals damage with cooldown " + a.cooldown + " s (the AI would use it on every GCD of its turn)");
                }
            Assert(checkedAbilities > 0, "no instant creature damage abilities found");
        }

        [Test]
        public static void LoneElitesOutlastAPackOfNormals()
        {
            const int level = 12;
            float pack = 3f * CreatureScaling.BaseHealth(level) * CreatureScaling.HealthRankMult(CreatureRank.Normal);
            int lone = 0;
            foreach (var map in Db.Maps.Values.OrderBy(m => m.id, StringComparer.Ordinal))
                foreach (var enc in map.encounters)
                {
                    if (enc.enemies.Count != 1) continue;
                    var c = Db.Creature(enc.enemies[0].creature);
                    if (c == null || (c.rank != CreatureRank.Elite && c.rank != CreatureRank.Rare)) continue;
                    lone++;
                    float hp = CreatureScaling.Health(c, level);
                    Assert(hp >= pack - 0.5f, $"{enc.id}: lone {c.rank} {c.id} has {hp:0} health at L{level}, less than a three-Normal pack ({pack:0})");
                }
            Assert(lone > 0, "no lone-elite encounter found");
        }

        [Test]
        public static void PetDamageSpellsKeepMostOfTheirNumbers()
        {
            int pets = 0;
            foreach (var c in Db.Creatures.Values.Where(x => x.rank == CreatureRank.Pet).OrderBy(x => x.id, StringComparer.Ordinal))
            {
                var spells = c.abilities.Select(e => Db.Ability(e.ability)).Where(a => a != null && a.effects.Any(e => e.type == EffectType.Damage)).ToList();
                if (spells.Count == 0) continue;
                pets++;
                float mult = CreatureScaling.DamageMult(c);
                Assert(mult >= 0.6f - 1e-4f, $"{c.id} scales {string.Join(", ", spells.Select(a => a.id))} by {mult:0.###} (rank × damageMult)");
            }
            Assert(pets > 0, "no pet casts a damage spell");
        }

        static bool Unscaled(PropDef p) => Math.Abs((p.scale > 0f ? p.scale : 1f) - 1f) < 1e-3f;

        [Test]
        public static void ScaledPropsUseUnscaledCollidersAndLights()
        {
            var props = Db.Maps.Values.OrderBy(m => m.id, StringComparer.Ordinal).SelectMany(m => m.props.Select(p => (map: m.id, p))).ToList();
            int compared = 0;
            foreach (var group in props.GroupBy(x => x.p.art))
            {
                var colliders = group.Where(x => Unscaled(x.p) && x.p.collider != null && x.p.collider.w > 0f).Select(x => (x.p.collider.w, x.p.collider.h)).Distinct().ToList();
                var lights = group.Where(x => Unscaled(x.p) && x.p.light != null).Select(x => (x.p.light.offset.x, x.p.light.offset.y)).Distinct().ToList();
                foreach (var (map, p) in group.Where(x => !Unscaled(x.p)))
                {
                    if (colliders.Count > 0 && p.collider != null && p.collider.w > 0f)
                    {
                        compared++;
                        Assert(colliders.Any(c => Math.Abs(c.w - p.collider.w) < 0.02f && Math.Abs(c.h - p.collider.h) < 0.02f),
                            $"{map}: {p.art} at {p.pos} (scale {p.scale}) has collider {p.collider.w}×{p.collider.h}; unscaled props of this art use " +
                            string.Join(" / ", colliders.Select(c => c.w + "×" + c.h)) + " (colliders are multiplied by scale)");
                    }
                    if (lights.Count > 0 && p.light != null)
                    {
                        compared++;
                        Assert(lights.Any(l => Math.Abs(l.x - p.light.offset.x) < 0.02f && Math.Abs(l.y - p.light.offset.y) < 0.02f),
                            $"{map}: {p.art} at {p.pos} (scale {p.scale}) has light offset {p.light.offset}; unscaled props of this art use " +
                            string.Join(" / ", lights.Select(l => "(" + l.x + ", " + l.y + ")")) + " (light offsets are multiplied by scale)");
                    }
                }
            }
            Assert(compared > 0, "no scaled prop with a collider or light to compare");
        }

        [Test]
        public static void SpiritLanternLightsSitAtTheLamp()
        {
            // the lamp is ≈ 1.5 m above the pivot on the unscaled spirit-lantern art (MapView.LanternLampFraction);
            // the light, the halo and the rekindle burst all use offset.y × scale
            int lanterns = 0;
            foreach (var map in Db.Maps.Values.OrderBy(m => m.id, StringComparer.Ordinal))
                foreach (var p in map.props.Where(x => x.art.StartsWith("prop_spirit_lantern", StringComparison.Ordinal) && x.light != null))
                {
                    lanterns++;
                    Assert(p.light.offset.y >= 1.2f && p.light.offset.y <= 1.8f,
                        $"{map.id}: {p.art} at {p.pos} (scale {p.scale}) puts its light {p.light.offset.y} unscaled units above the pivot (lamp ≈ 1.5)");
                }
            Assert(lanterns > 0, "no lit spirit lantern found");
        }
    }
}
