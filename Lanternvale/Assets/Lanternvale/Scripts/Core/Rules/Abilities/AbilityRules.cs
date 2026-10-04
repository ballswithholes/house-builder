// Rank, cost, time, cooldown, range and attack-table classification of abilities.
using System;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    /// <summary>Which hit table an ability uses.</summary>
    public enum AttackKind { None, Melee, Ranged, Spell, Wand }

    public static class AbilityRules
    {
        /// <summary>Number of ranks of an ability (1 when rankLevels is empty).</summary>
        public static int RankCount(AbilityDef a) => a.rankLevels != null && a.rankLevels.Length > 0 ? a.rankLevels.Length : 1;

        /// <summary>Level at which a rank becomes trainable.</summary>
        public static int RankLevel(AbilityDef a, int rank)
        {
            if (a.rankLevels == null || a.rankLevels.Length == 0) return a.learnLevel;
            return a.rankLevels[MathUtil.Clamp(rank, 1, a.rankLevels.Length) - 1];
        }

        /// <summary>Highest rank available at a level (0 when the ability is not yet learnable).</summary>
        public static int MaxRankAtLevel(AbilityDef a, int level)
        {
            if (level < a.learnLevel) return 0;
            if (a.rankLevels == null || a.rankLevels.Length == 0) return 1;
            int r = 0;
            for (int i = 0; i < a.rankLevels.Length; i++) if (a.rankLevels[i] <= level) r = i + 1;
            return Math.Max(1, r);
        }

        /// <summary>
        /// Effective level for perLevel scaling: the level of the known rank, or the caster level when the ability
        /// has scaleWithLevel or the caster is a creature/pet/totem.
        /// </summary>
        public static int EffLevel(Unit u, AbilityDef a, int rank)
        {
            if (a.scaleWithLevel || u == null || u.Class == null) return u != null ? u.Level : a.learnLevel;
            return RankLevel(a, Math.Max(1, rank));
        }

        /// <summary>Rank the unit uses (known rank, 1 for creatures/unknown).</summary>
        public static int UsedRank(Unit u, AbilityDef a)
        {
            int r = u.RankOf(a.id);
            return r > 0 ? r : 1;
        }

        public static bool IsSpell(AbilityDef a) => a.school != School.Physical;

        /// <summary>The ability attacks with the ranged weapon (uses RAP and the ranged hit table).</summary>
        public static bool IsRangedWeaponAbility(AbilityDef a)
        {
            if (a.requires != null && a.requires.rangedWeapon) return true;
            foreach (var e in a.effects) if (e.type == EffectType.WeaponDamage && e.ranged) return true;
            return false;
        }

        public static bool HasWeaponEffect(AbilityDef a)
        {
            foreach (var e in a.effects) if (e.type == EffectType.WeaponDamage) return true;
            return false;
        }

        /// <summary>Hit table: wand (Shoot), ranged weapon abilities, melee/physical abilities, or spells.</summary>
        public static AttackKind KindOf(AbilityDef a)
        {
            if (a == null) return AttackKind.Melee;
            if (a.special == "Shoot") return AttackKind.Wand;
            if (IsRangedWeaponAbility(a)) return AttackKind.Ranged;
            if (a.melee || HasWeaponEffect(a)) return AttackKind.Melee;
            if (a.school == School.Physical) return a.range > 8f && !a.area.centeredOnCaster ? AttackKind.Ranged : AttackKind.Melee;
            return AttackKind.Spell;
        }

        public static float ResourceCost(Unit u, AbilityDef a, int rank, AbilityModSet mods)
        {
            var c = a.cost;
            if (c == null || c.type == ResourceType.None) return 0f;
            float v;
            if (c.pctBaseMana > 0)
            {
                float baseMana = u.Stats.BaseMana > 0 ? u.Stats.BaseMana : u.MaxMana;
                v = c.pctBaseMana / 100f * baseMana;
            }
            else v = c.amount + c.perLevel * Math.Max(0, EffLevel(u, a, rank) - a.learnLevel);
            v = v * (1f + mods.CostPct / 100f) + mods.CostFlat;
            if (c.type == ResourceType.Mana) v *= u.Stats.ManaCostMult(a.school);
            v = Specials.ModifyCost(u, a, v);
            return Math.Max(0f, (float)Math.Round(v));
        }

        /// <summary>Global cooldown for the unit and ability (class GCD, gcdOverride, Gcd mods).</summary>
        public static float Gcd(Unit u, AbilityDef a, AbilityModSet mods)
        {
            float g = a.gcdOverride >= 0 ? a.gcdOverride : (u.Class != null ? u.Class.gcd : RulesConstants.DefaultGcd);
            return Math.Max(0f, g + mods.Gcd);
        }

        /// <summary>Cast time after CastTime mods and haste (channel duration for channels).</summary>
        public static float CastTime(Unit u, AbilityDef a, AbilityModSet mods)
        {
            float t = a.castTime;
            if (t <= 0f) return 0f;
            t = Math.Max(0f, t + mods.CastTime);
            if (!a.channeled)
            {
                float haste = KindOf(a) == AttackKind.Ranged ? u.Stats.RangedHaste : u.Stats.CastSpeed;
                if (haste > 0f) t /= haste;
            }
            return Specials.ModifyCastTime(u, a, t);
        }

        /// <summary>Seconds of turn Time the ability costs (Design.md §2).</summary>
        public static float TimeCost(Unit u, AbilityDef a, AbilityModSet mods)
        {
            if (a.autoAttack || a.nextSwing || a.passive) return 0f;
            var special = Specials.TimeCost(u, a);
            if (special.HasValue) return special.Value;
            float cast = CastTime(u, a, mods);
            if (a.time == Lanternvale.Data.TimeCost.OffGcd) return cast;
            return Math.Max(cast, Gcd(u, a, mods));
        }

        public static float Cooldown(Unit u, AbilityDef a, AbilityModSet mods) => Math.Max(0f, a.cooldown + mods.Cooldown);

        /// <summary>Centre-to-centre melee reach between two units (metres).</summary>
        public static float MeleeReach(Unit a, Unit b, GameConfigDef cfg)
        {
            float reach = cfg != null && cfg.meleeReachMetres > 0 ? cfg.meleeReachMetres : RulesConstants.DefaultMeleeReach;
            if (a != null) reach += Math.Max(0f, a.Radius - RulesConstants.DefaultUnitRadius);
            if (b != null) reach += Math.Max(0f, b.Radius - RulesConstants.DefaultUnitRadius);
            return reach;
        }

        /// <summary>True when range checks use melee reach.</summary>
        public static bool UsesMeleeReach(AbilityDef a)
        {
            if (a.melee) return true;
            if (a.range > 0f) return false;
            switch (a.target)
            {
                case TargetType.Enemy: case TargetType.Ally: case TargetType.AllyOther: case TargetType.Any: case TargetType.DeadAlly:
                    return true;
                default: return false;
            }
        }

        /// <summary>Maximum range in metres (melee reach for melee abilities; +inf for unlimited).</summary>
        public static float RangeMetres(Unit caster, AbilityDef a, Unit target, AbilityModSet mods, GameConfigDef cfg)
        {
            if (UsesMeleeReach(a)) return MeleeReach(caster, target, cfg);
            if (a.range <= 0f) return float.PositiveInfinity;
            float r = MathUtil.Yd(a.range * (1f + mods.RangePct / 100f));
            if (target != null) r += target.Radius;
            return r;
        }

        public static float MinRangeMetres(AbilityDef a) => a.minRange > 0 ? MathUtil.Yd(a.minRange) : 0f;

        public static float RadiusMetres(AbilityDef a, AbilityModSet mods) => MathUtil.Yd(a.area.radius * (1f + mods.RadiusPct / 100f));

        /// <summary>Aura duration after Duration/DurationPct mods and combo-point scaling.</summary>
        public static float AuraDuration(AuraDef aura, EffectDef e, AbilityModSet mods, int comboPoints)
        {
            float d = e != null && e.duration > 0 ? e.duration : aura.duration;
            if (d <= 0f) return d;
            if (e != null && e.durationPerCombo > 0) d += e.durationPerCombo * comboPoints;
            if (mods != null) d = (d + mods.Duration) * (1f + mods.DurationPct / 100f);
            return Math.Max(0.1f, d);
        }

        /// <summary>Base magnitude (before spell power, crits and multipliers) of an effect.</summary>
        public static float BaseMagnitude(EffectDef e, int effLevel, int learnLevel, int comboPoints, Rng rng)
        {
            float lo = e.min, hi = Math.Max(e.min, e.max);
            float v = rng != null ? rng.Range(lo, hi) : (lo + hi) * 0.5f;
            v += e.perLevel * (effLevel - learnLevel);
            v += e.perCombo * comboPoints;
            return v;
        }

        /// <summary>The ability starts melee auto attack when used on an enemy (WoW behaviour for melee abilities).</summary>
        public static bool StartsMeleeAutoAttack(AbilityDef a) => !a.autoAttack && a.target == TargetType.Enemy && (a.melee || (HasWeaponEffect(a) && !IsRangedWeaponAbility(a)));
        public static bool StartsAutoShot(AbilityDef a) => !a.autoAttack && a.target == TargetType.Enemy && IsRangedWeaponAbility(a) && a.special != "Shoot";

        public static bool IsHarmful(AbilityDef a)
        {
            if (a.target == TargetType.Enemy) return true;
            foreach (var e in a.effects)
                if (e.type == EffectType.Damage || e.type == EffectType.WeaponDamage || e.type == EffectType.Interrupt || e.type == EffectType.Taunt)
                    return true;
            return a.area.shape != AreaShape.None && a.area.affects == AreaAffects.Enemies;
        }
    }
}
