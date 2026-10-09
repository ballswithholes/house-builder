// Accumulates stat modifiers from every source (base, items, auras, talents) and answers stat queries.
using System;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    /// <summary>How a stat combines its modifiers.</summary>
    public enum StatCombine
    {
        /// <summary>(base + flat) × Π(1 + pct/100). Primary stats, health, mana, armor, AP, spell power, resistances, regen.</summary>
        Base,
        /// <summary>Sum of all values (percent points): crit, hit, dodge, parry, block, defense, penetration...</summary>
        Additive,
        /// <summary>Percent stat combined multiplicatively: Π(1 + v/100). Haste, damage done/taken, threat, mana cost...</summary>
        Multiplicative,
        /// <summary>MoveSpeed: positive bonuses add, the strongest snare applies.</summary>
        Speed,
    }

    /// <summary>
    /// Raw modifier totals per stat and school. Slot 0 is "any school", slots 1..7 are School+1.
    /// A school query adds the generic and school-specific values (and multiplies multipliers).
    /// </summary>
    public sealed class StatBlock
    {
        public static readonly int StatCount = Enum.GetValues(typeof(StatId)).Length;
        const int Slots = 8;

        readonly float[] flat = new float[StatCount * Slots];
        readonly float[] mult = new float[StatCount * Slots];
        readonly float[] snare = new float[Slots];

        public StatBlock() { Clear(); }

        public void Clear()
        {
            Array.Clear(flat, 0, flat.Length);
            for (int i = 0; i < mult.Length; i++) mult[i] = 1f;
            Array.Clear(snare, 0, snare.Length);
        }

        public static StatCombine CombineOf(StatId s)
        {
            switch (s)
            {
                case StatId.MeleeCrit: case StatId.RangedCrit: case StatId.SpellCrit:
                case StatId.MeleeHit: case StatId.RangedHit: case StatId.SpellHit:
                case StatId.Dodge: case StatId.Parry: case StatId.Block: case StatId.Defense:
                case StatId.SpiritRegenWhileCasting: case StatId.CritDamage:
                case StatId.ArmorPenetration: case StatId.SpellPenetration:
                case StatId.DodgeChanceAgainstMe: case StatId.ChanceToBeHit: case StatId.StealthDetection:
                    return StatCombine.Additive;
                case StatId.MeleeHaste: case StatId.RangedHaste: case StatId.CastSpeed:
                case StatId.DamageDone: case StatId.DamageTaken: case StatId.HealingDone: case StatId.HealingTaken:
                case StatId.ThreatGenerated: case StatId.EnergyRegen: case StatId.RageGenerated: case StatId.ManaCost:
                    return StatCombine.Multiplicative;
                case StatId.MoveSpeed:
                    return StatCombine.Speed;
                default:
                    return StatCombine.Base;
            }
        }

        static int Slot(School? school) => school.HasValue ? 1 + (int)school.Value : 0;
        static int Idx(StatId s, School? school) => (int)s * Slots + Slot(school);

        /// <summary>Adds one modifier. <paramref name="pct"/> only matters for Base stats.</summary>
        public void Add(StatId stat, float value, bool pct, School? school = null)
        {
            if (value == 0f) return;
            if (stat == StatId.AllStats)
            {
                Add(StatId.Strength, value, pct, school); Add(StatId.Agility, value, pct, school);
                Add(StatId.Stamina, value, pct, school); Add(StatId.Intellect, value, pct, school);
                Add(StatId.Spirit, value, pct, school);
                return;
            }
            int i = Idx(stat, school);
            switch (CombineOf(stat))
            {
                case StatCombine.Base:
                    if (pct) mult[i] *= 1f + value / 100f; else flat[i] += value;
                    break;
                case StatCombine.Additive:
                    flat[i] += value;
                    break;
                case StatCombine.Multiplicative:
                    mult[i] *= Math.Max(0f, 1f + value / 100f);
                    break;
                case StatCombine.Speed:
                    if (value > 0) flat[i] += value;
                    else snare[Slot(school)] = Math.Min(snare[Slot(school)], value);
                    break;
            }
        }

        public float Flat(StatId s, School? school = null)
        {
            float v = flat[Idx(s, null)];
            if (school.HasValue) v += flat[Idx(s, school)];
            return v;
        }

        public float Mult(StatId s, School? school = null)
        {
            float v = mult[Idx(s, null)];
            if (school.HasValue) v *= mult[Idx(s, school)];
            return v;
        }

        /// <summary>Final value of a Base stat given its base value.</summary>
        public float Base(StatId s, float baseValue, School? school = null) => (baseValue + Flat(s, school)) * Mult(s, school);

        /// <summary>Sum for Additive stats (percent points).</summary>
        public float Sum(StatId s, School? school = null) => Flat(s, school);

        /// <summary>Multiplier for Multiplicative stats (1.0 = unchanged).</summary>
        public float Multiplier(StatId s, School? school = null) => Mult(s, school);

        /// <summary>Movement speed percent: positive bonuses add, strongest snare applies (−50 = half speed).</summary>
        public float MoveSpeedPct => Flat(StatId.MoveSpeed) + snare[0];
    }
}
