// Damage and healing pipeline: modifiers, armor, resistance, block, absorbs (incl. Mana Shield), rage,
// threat, break-on-damage, procs, death and downed state.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed class DamageInfo
    {
        public AbilityDef Ability;
        public string Name = "";
        public bool Crit, Periodic, AutoAttack, OffHand, Ranged;
        public AttackKind Kind = AttackKind.Spell;
        /// <summary>The attack was blocked (damage reduced by block value).</summary>
        public bool Blocked;
        public bool IgnoreArmor, IgnoreAbsorb, IgnoreModifiers, NoThreat, NoProcs;
        /// <summary>Extra weapon attack granted by a proc (Sword Specialization, Windfury): fires on-hit procs like a swing.</summary>
        public bool ExtraAttack;
        /// <summary>Redirected damage (Soul Link, Blessing of Sacrifice): never redirected again.</summary>
        public bool Redirected;
        public float BonusThreat;
        public AbilityModSet Mods;
        public AuraInstance SourceAura;
        public WeaponInfo Weapon;
        public AbilityCast Cast;
    }

    public sealed class HealInfo
    {
        public AbilityDef Ability;
        public string Name = "";
        public bool Crit, Periodic, Silent, NoThreat;
        public AbilityModSet Mods;
        public AbilityCast Cast;
    }

    public sealed partial class Battle
    {
        /// <summary>Deals damage after all modifiers. Returns the damage actually done to health (absorbed damage excluded).</summary>
        public float DealDamage(Unit src, Unit tgt, float amount, School school, DamageInfo info)
        {
            if (tgt == null || !tgt.IsAlive || amount <= 0f) return 0f;
            info ??= new DamageInfo();
            string abilityId = info.Ability != null ? info.Ability.id : "";
            // immunities
            if (!info.IgnoreModifiers)
            {
                if (tgt.IsInvulnerable || (school == School.Physical ? tgt.HasStateAura(UnitState.ImmunePhysical) : tgt.HasStateAura(UnitState.ImmuneMagic)))
                {
                    Emit(new CombatEvent { Type = CombatEventType.Immune, Source = src, Target = tgt, AbilityId = abilityId, Name = info.Name, School = school, Periodic = info.Periodic, AutoAttack = info.AutoAttack });
                    return 0f;
                }
            }
            float v = amount;
            float resisted = 0f, blocked = 0f;
            if (!info.IgnoreModifiers)
            {
                if (src != null) v *= src.Stats.DamageDone(school);
                v *= tgt.Stats.DamageTaken(school);
                int atkLevel = src != null ? src.Level : tgt.Level;
                if (school == School.Physical && !info.IgnoreArmor)
                {
                    float armor = Math.Max(0f, tgt.Stats.Armor - (src != null ? src.Stats.ArmorPenetration : 0f));
                    v *= 1f - Formulas.ArmorReduction(armor, atkLevel);
                }
                else if (school != School.Physical)
                {
                    float res = Math.Max(0f, tgt.Stats.Resistance(school) - (src != null ? src.Stats.SpellPenetration(school) : 0f));
                    resisted = v * Formulas.ResistReduction(res, atkLevel);
                    v -= resisted;
                }
                if (info.Blocked)
                {
                    blocked = Math.Min(v, tgt.Stats.BlockValue);
                    v -= blocked;
                }
            }
            v = (float)Math.Round(Math.Max(0f, v));
            resisted = (float)Math.Round(resisted);
            blocked = (float)Math.Round(blocked);

            // absorbs
            float absorbed = 0f;
            if (!info.IgnoreAbsorb && v > 0) absorbed = Absorb(tgt, v, school);
            float hp = v - absorbed;
            float overkill = Math.Max(0f, hp - tgt.Health);
            tgt.Health -= hp;
            if (tgt.Health < 0) tgt.Health = 0;

            Emit(new CombatEvent
            {
                Type = CombatEventType.Damage, Source = src, Target = tgt, AbilityId = abilityId, Name = info.Name, School = school,
                Amount = hp, Absorbed = absorbed, Resisted = resisted, Blocked = blocked, Overkill = overkill, Crit = info.Crit,
                Periodic = info.Periodic, AutoAttack = info.AutoAttack, OffHand = info.OffHand, Ranged = info.Ranged,
            });
            if (info.Blocked && blocked > 0 && hp <= 0)
                Emit(new CombatEvent { Type = CombatEventType.Block, Source = src, Target = tgt, AbilityId = abilityId, Name = info.Name, Blocked = blocked });

            // meters
            if (src != null)
            {
                var m = MetersOf(src.Master);
                m.Damage += hp + absorbed;
                m.Hits++;
                if (info.Crit) m.Crits++;
            }
            MetersOf(tgt).DamageTaken += hp + absorbed;
            tgt.SecondsSinceCombat = 0f;
            if (src != null) src.SecondsSinceCombat = 0f;

            float total = hp + absorbed;
            // rage
            if (tgt.PowerType == ResourceType.Rage && total > 0 && InCombat)
                ChangeResource(tgt, ResourceType.Rage, Formulas.RageFromDamageTaken(total, tgt.Level) * tgt.Stats.RageGenerated, src, true);
            if (src != null && info.AutoAttack && src.PowerType == ResourceType.Rage && total > 0)
                ChangeResource(src, ResourceType.Rage, Formulas.RageFromDamageDealt(total, src.Level) * src.Stats.RageGenerated, src, true);

            // threat
            if (src != null && !info.NoThreat && tgt.IsHostileTo(src))
                AddThreat(tgt, src, (total + info.BonusThreat) * ThreatMult(src, school, info.Mods), true);

            // breaking CC
            if (total > 0) BreakOnDamage(tgt, total);

            // procs
            if (src != null && !info.NoProcs && total >= 0)
            {
                var pi = new ProcInfo { Ability = info.Ability, School = school, Crit = info.Crit, Periodic = info.Periodic, AutoAttack = info.AutoAttack, OffHand = info.OffHand, Damage = total, WeaponSpeed = info.Weapon.Valid ? info.Weapon.Speed : 2f };
                if (info.Periodic)
                {
                    FireProcs(ProcTrigger.OnPeriodicDamage, src, tgt, pi);
                }
                else
                {
                    bool melee = info.Kind == AttackKind.Melee;
                    bool ranged = info.Kind == AttackKind.Ranged;
                    if (melee) FireProcs(ProcTrigger.OnMeleeHit, src, tgt, pi);
                    if (info.AutoAttack) FireProcs(ProcTrigger.OnAutoAttackHit, src, tgt, pi);
                    if (ranged) FireProcs(ProcTrigger.OnRangedHit, src, tgt, pi);
                    if (info.Kind == AttackKind.Spell || info.Kind == AttackKind.Wand) FireProcs(ProcTrigger.OnSpellHit, src, tgt, pi);
                    if (info.Crit)
                    {
                        FireProcs(ProcTrigger.OnCrit, src, tgt, pi);
                        if (melee) FireProcs(ProcTrigger.OnMeleeCrit, src, tgt, pi);
                        if (info.Kind == AttackKind.Spell) FireProcs(ProcTrigger.OnSpellCrit, src, tgt, pi);
                        OpenReactive(src, "SelfCrit");
                    }
                    if (melee || ranged) FireProcs(ProcTrigger.OnStruck, tgt, src, pi);
                    if (info.Crit) FireProcs(ProcTrigger.OnCritTaken, tgt, src, pi);
                }
                FireProcs(ProcTrigger.OnDamaged, tgt, src, pi);
            }

            if (tgt.Health <= 0f && tgt.IsAlive)
            {
                if (info.Cast != null) info.Cast.KilledTarget = true;
                OnZeroHealth(tgt, src, info);
            }
            return hp;
        }

        float Absorb(Unit tgt, float amount, School school)
        {
            float absorbed = 0f;
            foreach (var a in new List<AuraInstance>(tgt.Auras))
            {
                if (amount <= 0) break;
                var ab = a.Def.absorb;
                if (ab == null || a.AbsorbLeft <= 0) continue;
                if (ab.schools.Length > 0 && Array.IndexOf(ab.schools, school) < 0) continue;
                float take = Math.Min(amount, a.AbsorbLeft);
                if (ab.manaPerDamage > 0)
                {
                    float affordable = tgt.Mana / ab.manaPerDamage;
                    take = Math.Min(take, affordable);
                    if (take <= 0) continue;
                    float manaCost = take * ab.manaPerDamage;
                    tgt.Mana -= manaCost;
                    Emit(new CombatEvent { Type = CombatEventType.ResourceChange, Source = tgt, Target = tgt, Resource = ResourceType.Mana, Amount = -manaCost, Reason = a.Def.name });
                }
                take = (float)Math.Floor(take);
                if (take <= 0) continue;
                a.AbsorbLeft -= take;
                amount -= take;
                absorbed += take;
                Emit(new CombatEvent { Type = CombatEventType.Absorb, Source = a.Caster, Target = tgt, AuraId = a.Def.id, Name = a.Def.name, Amount = take, School = school });
                MetersOf(tgt).Absorbed += take;
                if (a.AbsorbLeft <= 0.5f || (ab.manaPerDamage > 0 && tgt.Mana < ab.manaPerDamage)) RemoveAura(a, AuraRemoveReason.Consumed);
            }
            return absorbed;
        }

        /// <summary>Heals a unit (HealingTaken applied). Returns effective healing. Generates healing threat (0.5 per point, split).</summary>
        public float HealUnit(Unit src, Unit tgt, float amount, HealInfo info)
        {
            if (tgt == null || !tgt.IsAlive || amount <= 0f) return 0f;
            info ??= new HealInfo();
            float v = (float)Math.Round(amount * tgt.Stats.HealingTaken);
            float missing = Math.Max(0f, tgt.MaxHealth - tgt.Health);
            float eff = Math.Min(v, missing);
            float over = v - eff;
            tgt.Health += eff;
            if (!info.Silent || eff > 0)
                Emit(new CombatEvent
                {
                    Type = CombatEventType.Heal, Source = src, Target = tgt, AbilityId = info.Ability != null ? info.Ability.id : "", Name = info.Name,
                    Amount = eff, Overheal = over, Crit = info.Crit, Periodic = info.Periodic, Reason = info.Silent ? "regen" : "",
                });
            if (src != null)
            {
                var m = MetersOf(src.Master);
                m.Healing += eff;
                m.Overheal += over;
            }
            if (src != null && !info.NoThreat && eff > 0 && InCombat)
                SplitThreat(src, eff * 0.5f * ThreatMult(src, info.Ability != null ? info.Ability.school : School.Holy, info.Mods), src);
            return eff;
        }

        /// <summary>Health reached 0: party characters are downed, everything else dies.</summary>
        void OnZeroHealth(Unit tgt, Unit killer, DamageInfo info)
        {
            if (Specials.PreventDeath(this, tgt, killer, info)) return;
            tgt.Health = 0f;
            CancelPending(tgt, "died", null, 0f);
            tgt.AutoAttacking = false;
            tgt.QueuedSwing = "";
            bool downed = tgt.Team == PlayerTeam && tgt.IsCharacter;
            if (downed)
            {
                tgt.Downed = true;
                RemoveAllAuras(tgt, AuraRemoveReason.Death, keepPassive: true);
                Emit(new CombatEvent { Type = CombatEventType.Downed, Source = killer, Target = tgt, Name = tgt.Name });
            }
            else
            {
                tgt.Dead = true;
                RemoveAllAuras(tgt, AuraRemoveReason.Death, keepPassive: true);
                Emit(new CombatEvent { Type = CombatEventType.Death, Source = killer, Target = tgt, Name = tgt.Name });
                if (tgt.Creature != null && tgt.Team != PlayerTeam && tgt.Kind == UnitKind.Creature) KilledCreatures.Add(tgt.Creature.id);
                // owned totems and temporary summons vanish with their owner
                foreach (var t in new List<Unit>(tgt.Totems.Values)) Despawn(t, "owner died");
                foreach (var s in new List<Unit>(tgt.Summons)) if (s.Kind == UnitKind.Summon) Despawn(s, "owner died");
                if (tgt.Owner != null && tgt.Kind == UnitKind.Totem && tgt.Owner.Totems.TryGetValue(tgt.TotemElement, out var tt) && tt == tgt)
                    tgt.Owner.Totems.Remove(tgt.TotemElement);
            }
            Pathfinder?.SetUnit(tgt.Id, tgt.Position, 0f);
            CleanupAreaChildrenOf(tgt);
            foreach (var u in Units)
            {
                if (u.AttackTarget == tgt) { u.AttackTarget = null; }
                if (u.AggroTarget == tgt) u.AggroTarget = null;
                u.Threat.Remove(tgt);
                if (u.ComboTarget == tgt && u.Team != tgt.Team) { /* combo points fade with the target */ }
            }
            if (killer != null && killer != tgt)
            {
                FireProcs(ProcTrigger.OnKill, killer, tgt, new ProcInfo { Ability = info?.Ability, Damage = 0 });
                if (killer.Owner != null) FireProcs(ProcTrigger.OnKill, killer.Owner, tgt, new ProcInfo { Ability = info?.Ability });
                Specials.OnKill(this, killer, tgt, info);
            }
            CheckBattleEnd();
        }

        /// <summary>Kills a unit outright (scripted).</summary>
        public void KillUnit(Unit tgt, Unit killer = null)
        {
            if (tgt == null || !tgt.IsAlive) return;
            tgt.Health = 0f;
            OnZeroHealth(tgt, killer, new DamageInfo());
        }
    }
}
