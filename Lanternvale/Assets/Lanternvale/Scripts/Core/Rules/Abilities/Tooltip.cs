// Tooltip text: ability description tokens ({0} {1} {d0}) and talent per-rank values ({a/b/c}).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public static class Tooltip
    {
        /// <summary>Markup around the current rank's value in talent descriptions (IMGUI rich text by default).</summary>
        public static string HighlightOpen = "<color=#ffd100><b>";
        public static string HighlightClose = "</b></color>";

        static string N(float v)
        {
            if (Math.Abs(v - Math.Round(v)) < 0.05f) return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
            return v.ToString(Math.Abs(v) < 10 ? "0.#" : "0", CultureInfo.InvariantCulture);
        }

        static string Range(float lo, float hi) => Math.Abs(hi - lo) < 0.5f ? N((lo + hi) / 2f) : $"{N(lo)} to {N(hi)}";

        /// <summary>Ability description with {N}/{dN} tokens replaced using the unit's rank and stats (unit may be null).</summary>
        public static string Ability(Unit u, AbilityDef a, int rankOverride = 0)
        {
            if (a == null) return "";
            var text = a.description ?? "";
            if (text.IndexOf('{') < 0) return text;
            int rank = rankOverride > 0 ? rankOverride : (u != null ? AbilityRules.UsedRank(u, a) : 1);
            var mods = u != null ? AbilityMods.For(u, a) : AbilityModSet.Empty;
            int eff = u != null ? AbilityRules.EffLevel(u, a, rank) : AbilityRules.RankLevel(a, rank);
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        var tok = text.Substring(i + 1, close - i - 1);
                        var rep = Token(u, a, tok, eff, mods);
                        if (rep != null) { sb.Append(rep); i = close + 1; continue; }
                    }
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        static string Token(Unit u, AbilityDef a, string tok, int eff, AbilityModSet mods)
        {
            bool dur = tok.StartsWith("d", StringComparison.Ordinal);
            if (!int.TryParse(dur ? tok.Substring(1) : tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)) return null;
            if (idx < 0 || idx >= a.effects.Count) return "?";
            var e = a.effects[idx];
            var db = u?.Db;
            if (dur)
            {
                var aura = db?.Aura(e.aura);
                if (aura == null) return e.duration > 0 ? N(e.duration) : "?";
                return N(AbilityRules.AuraDuration(aura, e, mods, 0));
            }
            return Magnitude(u, a, e, eff, mods);
        }

        /// <summary>Displayed magnitude of an ability effect at an effective level for a unit (null unit = no stats).</summary>
        public static string Magnitude(Unit u, AbilityDef a, EffectDef e, int eff, AbilityModSet mods)
        {
            float delta = eff - a.learnLevel;
            var school = e.school ?? a.school;
            string combo = e.perCombo > 0 ? $" (+{N(e.perCombo)} per combo point)" : "";
            switch (e.type)
            {
                case EffectType.Damage:
                {
                    float bonus = 0f;
                    if (u != null && e.coef > 0) bonus += e.coef * u.Stats.SpellDamage(school);
                    if (u != null && e.apCoef > 0) bonus += e.apCoef * (AbilityRules.IsRangedWeaponAbility(a) ? u.Stats.RangedAttackPower : u.Stats.AttackPower);
                    float m = mods.DamageMult * (u != null && u.Class == null && u.Creature != null ? CreatureScaling.DamageMult(u.Creature) : 1f);
                    return Range((e.min + e.perLevel * delta + bonus) * m, (Math.Max(e.min, e.max) + e.perLevel * delta + bonus) * m) + combo;
                }
                case EffectType.Heal:
                {
                    if (e.pctOfMax > 0) return N(e.pctOfMax) + "%";
                    float bonus = u != null && e.coef > 0 ? e.coef * u.Stats.HealingPower : 0f;
                    float m = mods.HealingMult;
                    return Range((e.min + e.perLevel * delta + bonus) * m, (Math.Max(e.min, e.max) + e.perLevel * delta + bonus) * m);
                }
                case EffectType.WeaponDamage:
                {
                    if (u == null) return N(e.weaponPct) + "% weapon damage";
                    var w = StatCalculator.GetWeapon(u, e.ranged ? WeaponSlot.Ranged : e.offHand ? WeaponSlot.OffHand : WeaponSlot.MainHand);
                    if (!w.Valid) return N(e.weaponPct) + "% weapon damage";
                    float ap = w.Type == WeaponType.Wand ? 0f : (e.ranged ? u.Stats.RangedAttackPower : u.Stats.AttackPower);
                    float flatLo = e.min + e.perLevel * delta, flatHi = Math.Max(e.min, e.max) + e.perLevel * delta;
                    float lo = (w.Min + ap / 14f * w.Speed) * e.weaponPct / 100f + flatLo;
                    float hi = (w.Max + ap / 14f * w.Speed) * e.weaponPct / 100f + flatHi;
                    if (e.offHand) { lo *= 0.5f; hi *= 0.5f; }
                    return Range(lo * mods.DamageMult, hi * mods.DamageMult) + combo;
                }
                case EffectType.ApplyAura:
                {
                    var aura = u?.Db?.Aura(e.aura);
                    if (aura == null) return e.stacks > 1 ? N(e.stacks) : "";
                    float d = AbilityRules.AuraDuration(aura, e, mods, 0);
                    foreach (var te in aura.tickEffects)
                    {
                        if (te.type != EffectType.Damage && te.type != EffectType.Heal) continue;
                        int ticks = aura.tickInterval > 0 && d > 0 ? (int)Math.Floor(d / aura.tickInterval + 1e-3f) : 1;
                        float per = te.min + te.perLevel * delta;
                        float perHi = Math.Max(te.min, te.max) + te.perLevel * delta;
                        if (u != null && te.coef > 0) { float sp = te.type == EffectType.Heal ? u.Stats.HealingPower : u.Stats.SpellDamage(te.school ?? aura.school); per += te.coef * sp; perHi += te.coef * sp; }
                        float m = (te.type == EffectType.Heal ? mods.HealingMult : mods.DamageMult) * mods.EffectMult;
                        string extra = te.perCombo > 0 ? $" (+{N(te.perCombo * ticks)} per combo point)" : "";
                        return Range(per * ticks * m, perHi * ticks * m) + extra;
                    }
                    if (aura.absorb != null)
                    {
                        float v = aura.absorb.amount + aura.absorb.perLevel * delta;
                        if (u != null && aura.absorb.coef > 0) v += aura.absorb.coef * Math.Max(u.Stats.HealingPower, u.Stats.SpellDamage(aura.school));
                        return N(v * mods.EffectMult);
                    }
                    if (aura.mods.Count > 0)
                    {
                        int rank = u != null ? AbilityRules.UsedRank(u, a) : 1;
                        float v = StatCalculator.AuraModValue(aura.mods[0], rank, eff, a.learnLevel, mods.EffectMult);
                        return N(Math.Abs(v));
                    }
                    return N(d);
                }
                case EffectType.GainResource:
                case EffectType.DrainResource:
                    if (e.pctOfMax > 0) return N(e.pctOfMax) + "%";
                    return N(((e.amount != 0 ? e.amount : e.min) + e.perLevel * delta) * mods.EffectMult) + combo;
                case EffectType.Teleport:
                case EffectType.Knockback:
                    return N(e.distance);
                case EffectType.Interrupt:
                    return N(e.lockout);
                case EffectType.Threat:
                    return N(e.threat != 0 ? e.threat : e.amount);
                case EffectType.Summon:
                case EffectType.SummonTotem:
                    return e.lifetime > 0 ? N(e.lifetime) : "";
                case EffectType.Resurrect:
                    return N(e.pctOfMax) + "%";
                case EffectType.CreateItem:
                    return N(e.count);
                default:
                    return e.min > 0 ? Range(e.min + e.perLevel * delta, Math.Max(e.min, e.max) + e.perLevel * delta) : "";
            }
        }

        /// <summary>
        /// Talent description with {a/b/c} per-rank groups. The value of the current rank is highlighted
        /// (all values kept, WoW talent-pane style); with <paramref name="onlyCurrent"/> only the current
        /// (or first, at rank 0) value is shown.
        /// </summary>
        public static string Talent(TalentDef t, int rank, bool onlyCurrent = false, bool rich = true)
        {
            if (t == null) return "";
            var text = t.description ?? "";
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        var inner = text.Substring(i + 1, close - i - 1);
                        if (inner.IndexOf('/') >= 0)
                        {
                            var parts = inner.Split('/');
                            if (onlyCurrent)
                            {
                                int k = MathUtil.Clamp(rank, 1, parts.Length) - 1;
                                sb.Append(rich && rank > 0 ? HighlightOpen + parts[k] + HighlightClose : parts[k]);
                            }
                            else
                            {
                                for (int p = 0; p < parts.Length; p++)
                                {
                                    if (p > 0) sb.Append('/');
                                    if (rich && rank == p + 1) sb.Append(HighlightOpen).Append(parts[p]).Append(HighlightClose);
                                    else sb.Append(parts[p]);
                                }
                            }
                            i = close + 1;
                            continue;
                        }
                    }
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>Multi-line tooltip: name, rank, cost, range, cast time, cooldown, requirements and description.</summary>
        public static string AbilityFull(Unit u, AbilityDef a)
        {
            var sb = new StringBuilder();
            sb.Append(a.name);
            int rank = u != null ? u.RankOf(a.id) : 0;
            if (AbilityRules.RankCount(a) > 1 && rank > 0) sb.Append($"  (Rank {rank})");
            sb.Append('\n');
            var mods = u != null ? AbilityMods.For(u, a) : AbilityModSet.Empty;
            if (a.passive) sb.Append("Passive\n");
            else
            {
                if (u != null && a.cost != null && a.cost.type != ResourceType.None)
                {
                    float cost = AbilityRules.ResourceCost(u, a, AbilityRules.UsedRank(u, a), mods);
                    if (cost > 0) sb.Append($"{N(cost)} {a.cost.type}");
                    if (a.cost.consumesComboPoints) sb.Append(cost > 0 ? ", finisher" : "Finisher");
                    sb.Append("   ");
                }
                if (a.melee) sb.Append("Melee range");
                else if (a.range > 0) sb.Append($"{N(a.range)} yd range");
                sb.Append('\n');
                float cast = u != null ? AbilityRules.CastTime(u, a, mods) : a.castTime;
                sb.Append(a.channeled ? $"Channeled ({N(cast)} sec)" : cast > 0 ? $"{N(cast)} sec cast" : "Instant");
                float cd = Math.Max(0f, a.cooldown + mods.Cooldown);
                if (cd > 0) sb.Append($"   {N(cd)} sec cooldown");
                sb.Append('\n');
            }
            var desc = Ability(u, a);
            if (desc.Length > 0) sb.Append(desc);
            return sb.ToString();
        }
    }
}
