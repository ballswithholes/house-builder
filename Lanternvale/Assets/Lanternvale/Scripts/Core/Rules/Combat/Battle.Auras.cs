// Auras: application (stacking, refresh, exclusive groups, immunities), removal, periodic ticks with
// fractional carry, area auras (radius/radiusAura), break on damage/action/move, charges.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum AuraRemoveReason { Expired, Dispelled, Broken, Cancelled, Replaced, Consumed, Death, OutOfRange, SourceGone }

    public sealed class AuraApplyInfo
    {
        public AbilityDef Source;
        public int Rank = 1;
        public int EffLevel = 1, LearnLevel = 1;
        public int Stacks = 1;
        /// <summary>Seconds; 0 = use the aura's own duration; &lt; 0 = permanent.</summary>
        public float Duration;
        public int ComboPoints;
        public AbilityModSet Mods;
        public AuraInstance AreaSource;
        public bool Passive;
    }

    public sealed partial class Battle
    {
        static readonly UnitState[] CancelCastStates =
        {
            UnitState.Stun, UnitState.Fear, UnitState.Polymorph, UnitState.Incapacitate, UnitState.Sleep, UnitState.Banish, UnitState.Confuse,
        };

        /// <summary>Applies an aura (or refreshes/stacks an existing one). Returns the instance, or null when resisted/immune.</summary>
        public AuraInstance ApplyAura(Unit caster, Unit target, AuraDef def, AuraApplyInfo info = null)
        {
            if (target == null || def == null) return null;
            info ??= new AuraApplyInfo { EffLevel = caster != null ? caster.Level : 1 };
            if (!target.IsAlive && !def.persistThroughDeath) return null;
            bool harmful = def.kind == AuraKind.Debuff || (caster != null && caster.IsHostileTo(target));

            // immunities and resist effects (talents, Berserker Rage, Bestial Wrath, Freedom...)
            if (harmful && caster != target)
            {
                var resist = Specials.ResistIncomingAura(this, target, caster, def);
                if (resist != null)
                {
                    Emit(new CombatEvent { Type = resist == "Resist" ? CombatEventType.Resist : CombatEventType.Immune, Source = caster, Target = target, AuraId = def.id, Name = def.name, Reason = resist });
                    return null;
                }
                string immune = null;
                if (target.IsInvulnerable) immune = "invulnerable";
                else if (def.school != School.Physical && target.HasStateAura(UnitState.ImmuneMagic)) immune = "magic";
                else if (def.school == School.Physical && target.HasStateAura(UnitState.ImmunePhysical)) immune = "physical";
                else if (target.Creature != null && target.Class == null && target.Creature.immune.Length > 0)
                    foreach (var s in def.states) if (Array.IndexOf(target.Creature.immune, s) >= 0) { immune = s.ToString(); break; }
                if (immune != null)
                {
                    Emit(new CombatEvent { Type = CombatEventType.Immune, Source = caster, Target = target, AuraId = def.id, Name = def.name, Reason = immune });
                    return null;
                }
            }

            var mods = info.Mods ?? AbilityModSet.Empty;
            float duration = info.Duration != 0 ? info.Duration : def.duration;
            if (info.Passive) duration = 0f;

            // same aura already present?
            bool shared = def.kind == AuraKind.Buff || def.maxStacks > 1 || caster == null;
            var existing = shared ? target.FindAura(def.id) : target.FindAura(def.id, caster);
            if (existing != null && !existing.IsAreaChild && info.AreaSource == null)
            {
                int before = existing.Stacks;
                existing.Caster = caster ?? existing.Caster;
                existing.Duration = duration;
                existing.Remaining = duration;
                existing.Stacks = Math.Min(Math.Max(1, def.maxStacks), existing.Stacks + Math.Max(1, info.Stacks));
                existing.Charges = def.charges > 0 ? def.charges + (int)Math.Round(mods.Charges) : 0;
                FillScaling(existing, info, mods);
                ResolveModValues(existing);
                if (def.absorb != null) existing.AbsorbLeft = AbsorbAmount(existing);
                existing.CastSerial = castSerial;
                target.InvalidateStats();
                target.ClampResources();
                Emit(new CombatEvent
                {
                    Type = existing.Stacks != before ? CombatEventType.AuraStack : CombatEventType.AuraRefreshed, Source = caster, Target = target,
                    AuraId = def.id, Name = def.name, Count = existing.Stacks, Seconds = duration,
                });
                Specials.OnAuraAppliedByMe(this, caster, existing);
                return existing;
            }
            if (existing != null && existing.IsAreaChild && info.AreaSource != null)
            {
                existing.AreaSource = info.AreaSource;
                return existing;
            }

            // exclusive groups (stances, aspects, seals, armors, paladin auras, curses per caster...)
            if (!string.IsNullOrEmpty(def.exclusiveGroup))
            {
                foreach (var a in new List<AuraInstance>(target.Auras))
                {
                    if (a.Def.exclusiveGroup != def.exclusiveGroup || a.Def.id == def.id && a.Caster == caster) continue;
                    if (def.exclusivePerCaster && a.Caster != caster) continue;
                    if (a.IsAreaChild && info.AreaSource != null && a.AreaSource != null && a.Def.id == def.id) continue;
                    RemoveAura(a, AuraRemoveReason.Replaced);
                }
            }

            var inst = new AuraInstance
            {
                Def = def, Caster = caster, Bearer = target, Duration = duration, Remaining = duration,
                Stacks = Math.Min(Math.Max(1, def.maxStacks), Math.Max(1, info.Stacks)),
                Charges = def.charges > 0 ? def.charges + (int)Math.Round(mods.Charges) : 0,
                AreaSource = info.AreaSource, IsPassive = info.Passive,
                ProcCooldowns = new float[def.procs.Count], CastSerial = castSerial,
            };
            FillScaling(inst, info, mods);
            ResolveModValues(inst);
            if (def.absorb != null) inst.AbsorbLeft = AbsorbAmount(inst);
            target.Auras.Add(inst);
            target.InvalidateStats();
            target.ClampResources();
            Emit(new CombatEvent { Type = CombatEventType.AuraApplied, Source = caster, Target = target, AuraId = def.id, Name = def.name, Count = inst.Stacks, Seconds = duration, School = def.school });

            // state side effects
            if (target.Pending != null)
            {
                foreach (var s in CancelCastStates)
                    if (inst.HasState(s)) { CancelPending(target, s.ToString().ToLowerInvariant(), caster, 0f); break; }
                if (target.Pending != null && inst.HasState(UnitState.Silence) && AbilityRules.IsSpell(target.Pending.Ability))
                    CancelPending(target, "silenced", caster, 0f);
            }
            if (inst.HasState(UnitState.FeignDeath))
            {
                foreach (var u in Units)
                {
                    if (u.Threat.ContainsKey(target)) u.Threat[target] = 0f;
                    if (u.AggroTarget == target) u.AggroTarget = null;
                    if (u.AttackTarget == target && u.Team != PlayerTeam) u.AttackTarget = null;
                }
            }
            if (inst.HasState(UnitState.Stealth) || inst.HasState(UnitState.Invisible))
            {
                foreach (var u in Units)
                {
                    if (u.Team == target.Team) continue;
                    if (u.AttackTarget == target && !CanSee(u, target)) u.AttackTarget = null;
                    if (u.AggroTarget == target && !CanSee(u, target)) u.AggroTarget = null;
                }
            }
            if (harmful && caster != null && caster != target && target.IsHostileTo(caster)) AddThreat(target, caster, 0f, true);

            if (def.onApply.Count > 0) ExecuteAuraEffects(inst, def.onApply);
            Specials.OnAuraApplied(this, inst);
            if (target.Auras.Contains(inst)) Specials.OnAuraAppliedByMe(this, caster, inst);
            if (def.radius > 0) RefreshAreaAuras();
            return inst;
        }

        static void FillScaling(AuraInstance a, AuraApplyInfo info, AbilityModSet mods)
        {
            a.SourceAbility = info.Source ?? a.SourceAbility;
            a.Rank = Math.Max(1, info.Rank);
            a.EffLevel = info.EffLevel;
            a.LearnLevel = info.LearnLevel;
            a.ComboPoints = info.ComboPoints;
            a.EffectMult = mods.EffectMult;
            a.DamageMult = mods.DamageMult;
            a.HealingMult = mods.HealingMult;
            if (a.Caster != null && a.Caster.Class == null && a.Caster.Creature != null)
            {
                a.DamageMult *= CreatureScaling.DamageMult(a.Caster.Creature);
            }
        }

        static void ResolveModValues(AuraInstance a)
        {
            var mods = a.Def.mods;
            if (a.ModValues.Length != mods.Count) a.ModValues = new float[mods.Count];
            for (int i = 0; i < mods.Count; i++)
                a.ModValues[i] = StatCalculator.AuraModValue(mods[i], a.Rank, a.EffLevel, a.LearnLevel, a.EffectMult);
        }

        float AbsorbAmount(AuraInstance a)
        {
            var ab = a.Def.absorb;
            float v = ab.amount + ab.perLevel * Math.Max(0, a.EffLevel - a.LearnLevel);
            if (ab.coef > 0 && a.Caster != null)
            {
                bool healer = a.Def.school == School.Holy || a.Def.school == School.Nature;
                float sp = healer ? Math.Max(a.Caster.Stats.HealingPower, a.Caster.Stats.SpellDamage(a.Def.school)) : a.Caster.Stats.SpellDamage(a.Def.school);
                v += ab.coef * sp;
            }
            return Math.Max(0f, (float)Math.Round(v * a.EffectMult));
        }

        /// <summary>Removes an aura, running onExpire (natural expiry only) and onRemove effects.</summary>
        public void RemoveAura(AuraInstance a, AuraRemoveReason reason)
        {
            var u = a?.Bearer;
            if (u == null || !u.Auras.Remove(a)) return;
            u.InvalidateStats();
            u.ClampResources();
            Emit(new CombatEvent
            {
                Type = reason == AuraRemoveReason.Broken ? CombatEventType.AuraBroken : CombatEventType.AuraRemoved,
                Source = a.Caster, Target = u, AuraId = a.Def.id, Name = a.Def.name, Reason = reason.ToString(),
            });
            // a control effect broken/dispelled during the bearer's own turn stops applying immediately
            if (reason != AuraRemoveReason.Expired && u.InOwnTurn)
                foreach (var s in a.Def.states)
                    if (!u.HasStateAura(s)) u.StateWindow[(int)s] = 0f;
            if (reason == AuraRemoveReason.Expired && a.Def.onExpire.Count > 0 && reason != AuraRemoveReason.Death) ExecuteAuraEffects(a, a.Def.onExpire);
            if (a.Def.onRemove.Count > 0 && reason != AuraRemoveReason.Death) ExecuteAuraEffects(a, a.Def.onRemove);
            Specials.OnAuraRemoved(this, a, reason);
            if (a.Def.radius > 0) RemoveAreaChildren(a);
        }

        public void RemoveAllAuras(Unit u, AuraRemoveReason reason, bool keepPassive)
        {
            foreach (var a in new List<AuraInstance>(u.Auras))
            {
                if (keepPassive && (a.IsPassive || a.Def.persistThroughDeath)) continue;
                RemoveAura(a, reason);
            }
        }

        void ExecuteAuraEffects(AuraInstance a, List<EffectDef> effects)
        {
            var caster = a.Caster ?? a.Bearer;
            var cast = new AbilityCast
            {
                Battle = this, Caster = caster, Ability = a.SourceAbility, Rank = a.Rank, EffLevel = a.EffLevel, LearnLevel = a.LearnLevel,
                Target = a.Bearer, Point = a.Bearer.Position, SourceAura = a, ComboPoints = a.ComboPoints, Free = true,
                School = a.Def.school, Depth = 1,
                Mods = a.SourceAbility != null && caster != null ? AbilityMods.For(caster, a.SourceAbility) : AbilityModSet.Empty,
            };
            ExecuteEffects(cast, effects);
        }

        // =============================================================== elapse

        /// <summary>Elapses <paramref name="dt"/> seconds on the unit's auras: periodic ticks (fractional carry), expiry.</summary>
        public void ElapseAuras(Unit u, float dt)
        {
            foreach (var a in new List<AuraInstance>(u.Auras))
            {
                if (!u.Auras.Contains(a)) continue;
                for (int i = 0; i < a.ProcCooldowns.Length; i++) a.ProcCooldowns[i] = Math.Max(0f, a.ProcCooldowns[i] - dt);
                if (a.Def.tickInterval > 0 && (a.Def.tickEffects.Count > 0 || !string.IsNullOrEmpty(a.Def.special)))
                {
                    float span = a.IsPermanent ? dt : Math.Min(dt, Math.Max(0f, a.Remaining));
                    a.TickAccum += span;
                    int guard = 0;
                    while (a.TickAccum >= a.Def.tickInterval - 1e-4f && guard++ < 1000)
                    {
                        a.TickAccum -= a.Def.tickInterval;
                        TickAura(a);
                        if (!u.Auras.Contains(a) || !u.IsAlive) break;
                    }
                }
                if (!u.Auras.Contains(a)) continue;
                if (!a.IsPermanent && !a.IsAreaChild)
                {
                    a.Remaining -= dt;
                    if (a.Remaining <= 1e-3f) RemoveAura(a, AuraRemoveReason.Expired);
                }
                if (!u.IsAlive && !u.Downed) break;
            }
        }

        /// <summary>One periodic tick of an aura (tickEffects, magnitudes per tick).</summary>
        public void TickAura(AuraInstance a)
        {
            var caster = a.Caster ?? a.Bearer;
            var cast = new AbilityCast
            {
                Battle = this, Caster = caster, Ability = a.SourceAbility, Rank = a.Rank, EffLevel = a.EffLevel, LearnLevel = a.LearnLevel,
                Target = a.Bearer, Point = a.Bearer.Position, SourceAura = a, Periodic = true, ComboPoints = a.ComboPoints, Free = true,
                School = a.Def.school, Depth = 1, Mods = AbilityModSet.Empty,
            };
            if (Specials.OnAuraTick(this, a, cast)) return;
            ExecuteEffects(cast, a.Def.tickEffects);
        }

        // ============================================================ area auras

        /// <summary>Applies/removes radiusAura children for every area aura according to current positions.</summary>
        public void RefreshAreaAuras()
        {
            // orphaned children
            foreach (var u in Units)
            {
                for (int i = u.Auras.Count - 1; i >= 0; i--)
                {
                    if (i >= u.Auras.Count) continue;
                    var c = u.Auras[i];
                    if (c.AreaSource == null) continue;
                    var src = c.AreaSource;
                    if (src.Bearer == null || !src.Bearer.Auras.Contains(src) || !src.Bearer.IsAlive || !Units.Contains(src.Bearer))
                        RemoveAura(c, AuraRemoveReason.SourceGone);
                }
            }
            foreach (var s in new List<Unit>(Units))
            {
                if (!s.IsAlive) continue;
                foreach (var a in new List<AuraInstance>(s.Auras))
                {
                    if (a.Def.radius <= 0 || !s.Auras.Contains(a)) continue;
                    var child = Db.Aura(a.Def.radiusAura);
                    if (child == null) continue;
                    float r = MathUtil.Yd(a.Def.radius);
                    foreach (var u in new List<Unit>(Units))
                    {
                        if (!u.IsAlive) continue;
                        bool side;
                        switch (a.Def.radiusAffects)
                        {
                            case AreaAffects.Enemies: side = u.IsHostileTo(s) && !u.IsTotem; break;
                            case AreaAffects.Allies: side = u.IsFriendlyTo(s) && !u.IsTotem; break;
                            default: side = !u.IsTotem || u == s; break;
                        }
                        bool inRange = side && u.DistanceTo(s) <= r + u.Radius;
                        AuraInstance existing = null;
                        foreach (var x in u.Auras) if (x.AreaSource == a) { existing = x; break; }
                        if (inRange && existing == null)
                        {
                            // another source already provides the same child aura: keep a single instance
                            var same = u.FindAura(child.id);
                            if (same != null && same.IsAreaChild) continue;
                            ApplyAura(a.Caster ?? s, u, child, new AuraApplyInfo
                            {
                                Source = a.SourceAbility, Rank = a.Rank, EffLevel = a.EffLevel, LearnLevel = a.LearnLevel,
                                Duration = -1f, AreaSource = a, Mods = new AbilityModSet { EffectPct = (a.EffectMult - 1f) * 100f },
                            });
                        }
                        else if (!inRange && existing != null) RemoveAura(existing, AuraRemoveReason.OutOfRange);
                    }
                }
            }
        }

        void RemoveAreaChildren(AuraInstance source)
        {
            foreach (var u in Units)
                for (int i = u.Auras.Count - 1; i >= 0; i--)
                    if (i < u.Auras.Count && u.Auras[i].AreaSource == source) RemoveAura(u.Auras[i], AuraRemoveReason.SourceGone);
        }

        void CleanupAreaChildrenOf(Unit s)
        {
            foreach (var u in Units)
                for (int i = u.Auras.Count - 1; i >= 0; i--)
                    if (i < u.Auras.Count && u.Auras[i].AreaSource != null && u.Auras[i].AreaSource.Bearer == s)
                        RemoveAura(u.Auras[i], AuraRemoveReason.SourceGone);
        }

        // ================================================================ breaks

        /// <summary>The unit acted: removes breakOnAction auras and stealth/invisibility.</summary>
        public void BreakOnAction(Unit u)
        {
            foreach (var a in new List<AuraInstance>(u.Auras))
            {
                if (a.IsPassive) continue;
                if (a.Def.breakOnAction || a.HasState(UnitState.Stealth) || a.HasState(UnitState.Invisible) || a.HasState(UnitState.FeignDeath))
                    RemoveAura(a, AuraRemoveReason.Broken);
            }
        }

        void BreakOnMove(Unit u)
        {
            foreach (var a in new List<AuraInstance>(u.Auras))
                if (a.Def.breakOnMove && !a.IsPassive) RemoveAura(a, AuraRemoveReason.Broken);
        }

        void BreakOnDamage(Unit u, float amount)
        {
            foreach (var a in new List<AuraInstance>(u.Auras))
            {
                if (!a.Def.breakOnDamage || a.IsPassive) continue;
                a.DamageTaken += amount;
                if (a.Def.breakDamageThreshold <= 0 || a.DamageTaken >= a.Def.breakDamageThreshold)
                    RemoveAura(a, AuraRemoveReason.Broken);
            }
        }

        /// <summary>Auras with charges and no procs lose a charge when the bearer uses an ability they affect.</summary>
        void ConsumeChargesOnUse(Unit u, AbilityDef ability, AbilityCast cast)
        {
            if (ability == null || ability.autoAttack || ability.passive) return;
            foreach (var a in new List<AuraInstance>(u.Auras))
            {
                if (a.Charges <= 0 || a.Def.procs.Count > 0 || a.Def.absorb != null) continue;
                if (a.SourceAbility == ability && cast.Caster == a.Caster) continue; // the aura was just applied by this ability
                if (a.Def.mods.Count > 0)
                {
                    bool affects = false;
                    foreach (var m in a.Def.mods)
                    {
                        if (m.school.HasValue && m.school.Value != ability.school) continue;
                        if (m.stat == StatId.ManaCost && (ability.cost == null || ability.cost.type != ResourceType.Mana)) continue;
                        if ((m.stat == StatId.SpellCrit || m.stat == StatId.SpellDamage || m.stat == StatId.CastSpeed) && !AbilityRules.IsSpell(ability)) continue;
                        affects = true;
                        break;
                    }
                    if (!affects) continue;
                }
                ConsumeCharge(a);
            }
        }

        /// <summary>Spends one charge; removes the aura at zero.</summary>
        public void ConsumeCharge(AuraInstance a)
        {
            if (a.Charges <= 0) return;
            a.Charges--;
            if (a.Charges <= 0) RemoveAura(a, AuraRemoveReason.Consumed);
            else Emit(new CombatEvent { Type = CombatEventType.AuraStack, Source = a.Caster, Target = a.Bearer, AuraId = a.Def.id, Name = a.Def.name, Count = a.Charges, Reason = "charges" });
        }
    }
}
