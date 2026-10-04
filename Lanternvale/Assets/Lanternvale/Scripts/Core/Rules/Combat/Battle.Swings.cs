// End-of-turn auto attacks (WoW swing timers, off hand, nextSwing replacement) and totem actions.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed partial class Unit
    {
        /// <summary>Seconds of this turn the unit was in control (swing timers advance by this × haste).</summary>
        public float SwingTimeThisTurn = RulesConstants.TurnSeconds;
    }

    public sealed partial class Battle
    {
        Unit actingOutOfTurn;

        bool IsRangedAutoMode(Unit u, Unit target)
        {
            if (u.Class == null)
                return u.Creature != null && u.Creature.ranged && (target == null || !InMeleeReach(u, target));
            var a = Db.Ability(u.AutoAttackAbility);
            return a != null && AbilityRules.IsRangedWeaponAbility(a);
        }

        /// <summary>Auto attacks at the end of the unit's turn (Design.md §2): the swing timers advance by the
        /// controlled part of 6 s × haste; one swing per full weapon speed while the target is in reach.</summary>
        void AutoAttacks(Unit u, bool canSwing)
        {
            float t = u.SwingTimeThisTurn;
            var target = u.AttackTarget;
            bool on = canSwing && u.AutoAttacking && target != null && target.IsAlive && target.IsHostileTo(u) && CanSee(u, target)
                      && !u.HasState(UnitState.Pacify) && Units.Contains(target);
            bool ranged = IsRangedAutoMode(u, target);
            var st = u.Stats;
            if (ranged)
            {
                var w = StatCalculator.GetWeapon(u, WeaponSlot.Ranged);
                if (!w.Valid) return;
                u.SwingRanged += t * st.RangedHaste;
                bool inReach = on && InRangedReach(u, target);
                if (!inReach) { u.SwingRanged = Math.Min(u.SwingRanged, w.Speed); return; }
                u.FaceTowards(target.Position);
                int guard = 0;
                while (u.SwingRanged >= w.Speed - 1e-4f && guard++ < 20 && target.IsAlive && u.IsAlive)
                {
                    u.SwingRanged -= w.Speed;
                    WhiteSwing(u, target, WeaponSlot.Ranged);
                }
                if (!target.IsAlive) u.SwingRanged = Math.Min(u.SwingRanged, w.Speed);
                return;
            }
            var main = StatCalculator.GetWeapon(u, WeaponSlot.MainHand);
            if (!main.Valid) return;
            bool reach = on && InMeleeReach(u, target);
            u.SwingMain += t * st.MeleeHaste;
            var off = StatCalculator.GetWeapon(u, WeaponSlot.OffHand);
            if (off.Valid) u.SwingOff += t * st.MeleeHaste;
            if (!reach)
            {
                u.SwingMain = Math.Min(u.SwingMain, main.Speed);
                if (off.Valid) u.SwingOff = Math.Min(u.SwingOff, off.Speed);
                return;
            }
            u.FaceTowards(target.Position);
            int g = 0;
            while (u.SwingMain >= main.Speed - 1e-4f && g++ < 20 && target.IsAlive && u.IsAlive)
            {
                u.SwingMain -= main.Speed;
                MainHandSwing(u, target);
            }
            if (off.Valid)
            {
                g = 0;
                while (u.SwingOff >= off.Speed - 1e-4f && g++ < 20 && target.IsAlive && u.IsAlive)
                {
                    u.SwingOff -= off.Speed;
                    WhiteSwing(u, target, WeaponSlot.OffHand);
                }
            }
            // stored extra attacks (Reckoning): one extra main-hand swing each, outside the swing timer
            while (u.ExtraAttacks > 0 && target.IsAlive && u.IsAlive)
            {
                u.ExtraAttacks--;
                WhiteSwing(u, target, WeaponSlot.MainHand);
            }
            if (!target.IsAlive)
            {
                u.SwingMain = Math.Min(u.SwingMain, main.Speed);
                if (off.Valid) u.SwingOff = Math.Min(u.SwingOff, off.Speed);
            }
        }

        bool InRangedReach(Unit u, Unit target)
        {
            float d = u.DistanceTo(target);
            if (u.Class == null)
                return u.Creature != null && d <= MathUtil.Yd(u.Creature.rangedRange) + target.Radius;
            var a = Db.Ability(u.AutoAttackAbility);
            float max = a != null && a.range > 0 ? MathUtil.Yd(a.range) + target.Radius : MathUtil.Yd(35f);
            float min = a != null ? AbilityRules.MinRangeMetres(a) : 0f;
            return d <= max && d >= min;
        }

        void MainHandSwing(Unit u, Unit target)
        {
            if (!string.IsNullOrEmpty(u.QueuedSwing))
            {
                var a = Db.Ability(u.QueuedSwing);
                u.QueuedSwing = "";
                if (a != null)
                {
                    var mods = AbilityMods.For(u, a);
                    int rank = AbilityRules.UsedRank(u, a);
                    float cost = AbilityRules.ResourceCost(u, a, rank, mods);
                    bool afford = cost <= 0 || u.GetResource(a.cost.type) + 1e-3f >= cost;
                    var req = CheckRequirements(u, a, target, true);
                    if (afford && req.Ok && u.CooldownLeft(a) <= 1e-3f)
                    {
                        var cast = NewCast(u, a, rank, target, null, mods);
                        BreakOnAction(u);
                        Emit(new CombatEvent { Type = CombatEventType.AbilityUsed, Source = u, Target = target, AbilityId = a.id, Name = a.name, Reason = "next swing" });
                        ResolveCast(cast);
                        return;
                    }
                }
            }
            WhiteSwing(u, target, WeaponSlot.MainHand);
        }

        /// <summary>One white (auto attack) swing: single-roll attack table miss/dodge/parry/block/crit/hit.</summary>
        void WhiteSwing(Unit u, Unit target, WeaponSlot slot, float apBonus = 0f)
        {
            var w = StatCalculator.GetWeapon(u, slot);
            if (!w.Valid) return;
            castSerial++;
            BreakOnAction(u);
            bool ranged = slot == WeaponSlot.Ranged;
            var st = u.Stats;
            var tst = target.Stats;
            var basic = Db.Ability(ranged ? (u.Class != null ? u.AutoAttackAbility : "auto_shot") : "attack");
            bool dw = !ranged && u.Equipment.IsDualWielding;
            float miss = Formulas.MeleeMissChance(u.Level, target.Level, dw) - (ranged ? st.RangedHit : st.MeleeHit) - tst.ChanceToBeHit + tst.Defense * 0.04f
                         + Specials.IncomingMissChance(target, ranged ? AttackKind.Ranged : AttackKind.Melee);
            miss = MathUtil.Clamp(miss, 100f - RulesConstants.MaxHitChance, 100f);
            bool canAvoid = !target.IsControlled;
            bool frontal = !u.IsBehind(target);
            float dodge = canAvoid ? Math.Max(0f, tst.Dodge - st.DodgeChanceAgainstMe) : 0f;
            float parry = canAvoid && !ranged && frontal && tst.CanParry ? tst.Parry : 0f;
            float block = canAvoid && frontal && tst.CanBlock ? tst.BlockChance : 0f;
            float crit = (ranged ? st.RangedCrit : st.MeleeCrit) - tst.Defense * 0.04f;
            crit += WeaponTalents.StatBonus(u, w.Type, ranged ? StatId.RangedCrit : StatId.MeleeCrit);
            crit = Math.Max(0f, Specials.CritChanceBonus(new AbilityCast { Battle = this, Caster = u, Ability = Db.Ability(ranged ? "auto_shot" : "attack"), Target = target, School = w.School }, target, w.School, crit));
            float roll = Rng.Value * 100f;
            var evInfo = new CombatEvent { Source = u, Target = target, AbilityId = basic != null ? basic.id : "attack", Name = basic != null ? basic.name : "Attack", AutoAttack = true, OffHand = slot == WeaponSlot.OffHand, Ranged = ranged };
            if (roll < miss)
            {
                evInfo.Type = CombatEventType.Miss; Emit(evInfo);
                MetersOf(u.Master).Misses++;
                return;
            }
            roll -= miss;
            if (roll < dodge)
            {
                evInfo.Type = CombatEventType.Dodge; Emit(evInfo);
                OnAvoided(u, target, HitOutcome.Dodge, null, true);
                return;
            }
            roll -= dodge;
            if (roll < parry)
            {
                evInfo.Type = CombatEventType.Parry; Emit(evInfo);
                OnAvoided(u, target, HitOutcome.Parry, null, true);
                return;
            }
            roll -= parry;
            bool blocked = false, isCrit = false;
            if (roll < block) { blocked = true; OnAvoided(u, target, HitOutcome.Block, null, true); }
            else
            {
                roll -= block;
                if (roll < crit) isCrit = true;
            }
            float ap = (ranged ? st.RangedAttackPower + Specials.IncomingRangedApBonus(u, target) : st.AttackPower) + apBonus;
            float dmg = Rng.Range(w.Min, w.Max) + ap / 14f * w.Speed;
            if (slot == WeaponSlot.OffHand) dmg *= RulesConstants.OffHandDamageFactor * Specials.OffHandMultiplier(u);
            dmg *= 1f + WeaponTalents.StatBonus(u, w.Type, StatId.DamageDone) / 100f;
            dmg *= CreatureDamageMult(u);
            var mods = basic != null ? AbilityMods.For(u, basic) : AbilityModSet.Empty;
            dmg *= mods.DamageMult;
            if (isCrit)
            {
                float bonus = 1f * (1f + mods.CritBonusPct / 100f) + st.CritDamageBonus(w.School) / 100f + Specials.CritBonusAdd(u, target) / 100f;
                dmg *= 1f + bonus;
            }
            dmg = Specials.ModifyDamage(new AbilityCast { Battle = this, Caster = u, Ability = basic, Target = target, School = w.School, Mods = mods }, null, target, dmg);
            var info = new DamageInfo
            {
                Ability = basic, Name = basic != null ? basic.name : "Attack", Crit = isCrit, AutoAttack = true, OffHand = slot == WeaponSlot.OffHand,
                Ranged = ranged, Kind = ranged ? AttackKind.Ranged : AttackKind.Melee, Blocked = blocked, Weapon = w, Mods = mods,
            };
            DealDamage(u, target, dmg, w.School, info);
        }

        // ================================================================ totems

        /// <summary>Totems act at the end of their owner's turn: aura ticks/lifetime, creature abilities, attacks.</summary>
        void TotemActions(Unit owner)
        {
            if (owner.Totems.Count == 0) return;
            foreach (var totem in new List<Unit>(owner.Totems.Values))
            {
                if (IsOver) return;
                if (totem == null || !totem.IsAlive || !Units.Contains(totem)) { owner.Totems.Remove(totem?.TotemElement ?? ""); continue; }
                totem.TurnsTaken++;
                ElapseAuras(totem, RulesConstants.TurnSeconds);
                ElapseTimers(totem, RulesConstants.TurnSeconds);
                if (!totem.IsAlive) continue;
                if (totem.Lifetime > 0)
                {
                    totem.Lifetime -= RulesConstants.TurnSeconds;
                    if (totem.Lifetime <= 1e-3f) { Despawn(totem, "expired"); continue; }
                }
                var prev = actingOutOfTurn;
                actingOutOfTurn = totem;
                totem.InOwnTurn = true;
                totem.TimeLeft = RulesConstants.TurnSeconds;
                totem.SwingTimeThisTurn = RulesConstants.TurnSeconds;
                try
                {
                    TotemUseAbilities(totem);
                    if (totem.IsAlive && totem.Creature != null && totem.Creature.ranged)
                    {
                        var t = NearestHostileInRange(totem, MathUtil.Yd(totem.Creature.rangedRange));
                        if (t != null)
                        {
                            totem.AttackTarget = t;
                            totem.AutoAttacking = true;
                            AutoAttacks(totem, true);
                        }
                    }
                }
                finally
                {
                    totem.InOwnTurn = false;
                    actingOutOfTurn = prev;
                }
            }
        }

        Unit NearestHostileInRange(Unit u, float range)
        {
            Unit best = null; float bd = float.MaxValue;
            foreach (var o in Units)
            {
                if (!o.IsAlive || !o.IsHostileTo(u) || o.IsTotem || !CanSee(u, o) || o.IsUntargetable) continue;
                float d = u.DistanceTo(o);
                if (d <= range + o.Radius && d < bd) { bd = d; best = o; }
            }
            return best;
        }

        void TotemUseAbilities(Unit totem)
        {
            var cr = totem.Creature;
            if (cr == null || cr.abilities.Count == 0) return;
            var list = new List<CreatureAbilityDef>(cr.abilities);
            list.Sort((a, b) => b.priority.CompareTo(a.priority));
            foreach (var ca in list)
            {
                if (!totem.IsAlive || totem.TimeLeft <= 0) break;
                var a = Db.Ability(ca.ability);
                if (a == null) continue;
                if (ca.chance < 100f && !Rng.Chance(ca.chance)) continue;
                Unit target = null;
                switch (a.target)
                {
                    case TargetType.Enemy:
                    {
                        var mods = AbilityMods.For(totem, a);
                        float range = AbilityRules.RangeMetres(totem, a, null, mods, Config);
                        target = NearestHostileInRange(totem, float.IsInfinity(range) ? 1000f : range);
                        break;
                    }
                    case TargetType.Ally:
                    case TargetType.AllyOther:
                    {
                        float worst = 101f;
                        foreach (var o in AlliesOf(totem, a.target == TargetType.Ally))
                            if (o.HealthPct < worst && CanUse(totem, a, o).Ok) { worst = o.HealthPct; target = o; }
                        break;
                    }
                    default:
                        target = totem;
                        break;
                }
                if (target == null) continue;
                if (!AI.ConditionMet(this, totem, ca.condition, target)) continue;
                if (CanUse(totem, a, target).Ok) UseAbility(totem, a, target, target.Position, false, null);
            }
        }
    }
}
