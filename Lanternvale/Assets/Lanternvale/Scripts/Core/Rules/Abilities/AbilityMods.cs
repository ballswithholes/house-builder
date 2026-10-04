// AbilityMod passives (talents, items, pet talents, specials) aggregated for one ability.
using System;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    /// <summary>Summed AbilityMod values for one unit + ability. Percent properties are additive.</summary>
    public sealed class AbilityModSet
    {
        public float DamagePct, HealingPct, EffectPct, CritChance, CritBonusPct, CostPct, CostFlat, Cooldown, CastTime,
            RangePct, RadiusPct, Duration, DurationPct, ThreatPct, HitChance, Charges, ComboPoints, Gcd;

        public float DamageMult => Math.Max(0f, 1f + DamagePct / 100f);
        public float HealingMult => Math.Max(0f, 1f + HealingPct / 100f);
        public float EffectMult => Math.Max(0f, 1f + EffectPct / 100f);
        public float ThreatMult => Math.Max(0f, 1f + ThreatPct / 100f);

        public static readonly AbilityModSet Empty = new AbilityModSet();

        public void Add(AbilityProperty p, float v)
        {
            switch (p)
            {
                case AbilityProperty.Damage: DamagePct += v; break;
                case AbilityProperty.Healing: HealingPct += v; break;
                case AbilityProperty.Effect: EffectPct += v; break;
                case AbilityProperty.CritChance: CritChance += v; break;
                case AbilityProperty.CritBonus: CritBonusPct += v; break;
                case AbilityProperty.Cost: CostPct += v; break;
                case AbilityProperty.CostFlat: CostFlat += v; break;
                case AbilityProperty.Cooldown: Cooldown += v; break;
                case AbilityProperty.CastTime: CastTime += v; break;
                case AbilityProperty.Range: RangePct += v; break;
                case AbilityProperty.Radius: RadiusPct += v; break;
                case AbilityProperty.Duration: Duration += v; break;
                case AbilityProperty.DurationPct: DurationPct += v; break;
                case AbilityProperty.Threat: ThreatPct += v; break;
                case AbilityProperty.HitChance: HitChance += v; break;
                case AbilityProperty.Charges: Charges += v; break;
                case AbilityProperty.ComboPoints: ComboPoints += v; break;
                case AbilityProperty.Gcd: Gcd += v; break;
            }
        }
    }

    public static class AbilityMods
    {
        /// <summary>True when the passive's filters (abilities, tags, schools — any match) select the ability. Empty filters match all.</summary>
        public static bool Matches(PassiveDef p, AbilityDef a)
        {
            if (a == null) return false;
            bool any = false;
            if (p.abilities != null && p.abilities.Length > 0)
            {
                any = true;
                foreach (var id in p.abilities) if (id == a.id) return true;
            }
            if (p.tags != null && p.tags.Length > 0)
            {
                any = true;
                foreach (var t in p.tags) if (HasTag(a, t)) return true;
            }
            if (p.schools != null && p.schools.Length > 0)
            {
                any = true;
                foreach (var s in p.schools) if (s == a.school) return true;
            }
            return !any;
        }

        public static bool HasTag(AbilityDef a, string tag)
        {
            if (a?.tags == null) return false;
            foreach (var t in a.tags) if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>All AbilityMods that apply to <paramref name="a"/> when used by <paramref name="u"/>.</summary>
        public static AbilityModSet For(Unit u, AbilityDef a)
        {
            var set = new AbilityModSet();
            if (u == null || a == null) return set;
            var db = u.Db;
            if (db != null)
            {
                foreach (var kv in u.Talents)
                {
                    if (kv.Value <= 0) continue;
                    var t = db.Talent(kv.Key);
                    if (t == null) continue;
                    foreach (var p in t.effects)
                        if (p.type == "AbilityMod" && !IsPet(p.target) && Matches(p, a))
                            set.Add(p.property, StatCalculator.RankValue(p.value, p.values, kv.Value));
                }
                // owner talents that modify pet abilities
                if (u.Owner != null)
                {
                    foreach (var kv in u.Owner.Talents)
                    {
                        if (kv.Value <= 0) continue;
                        var t = db.Talent(kv.Key);
                        if (t == null) continue;
                        foreach (var p in t.effects)
                        {
                            if (p.type != "AbilityMod") continue;
                            // "Pet" mods by any filter; the owner's own mods only when they name the pet ability explicitly
                            bool ok = IsPet(p.target) ? Matches(p, a) : (p.abilities != null && Array.IndexOf(p.abilities, a.id) >= 0);
                            if (ok) set.Add(p.property, StatCalculator.RankValue(p.value, p.values, kv.Value));
                        }
                    }
                }
            }
            foreach (var kv in u.Equipment.Equipped)
                foreach (var p in kv.Value.Def.equipEffects)
                    if (p.type == "AbilityMod" && Matches(p, a))
                        set.Add(p.property, StatCalculator.RankValue(p.value, p.values, 1));
            Specials.ContributeAbilityMods(u, a, set);
            return set;
        }

        static bool IsPet(string target) => string.Equals(target, "Pet", StringComparison.OrdinalIgnoreCase);
    }
}
