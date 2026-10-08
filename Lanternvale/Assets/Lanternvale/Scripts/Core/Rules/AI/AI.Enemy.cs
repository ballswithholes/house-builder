// Enemy AI (AIProfile, creature ability priorities/conditions, threat targeting) and pet AI.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public static partial class AI
    {
        static bool IsRangedProfile(Unit u)
        {
            var cr = u.Creature;
            if (cr == null) return false;
            return cr.ai == AIProfile.Ranged || cr.ai == AIProfile.Caster || cr.ai == AIProfile.Healer || cr.ranged;
        }

        static AIStep EnemyStep(Battle b, Unit u)
        {
            var cr = u.Creature;
            var profile = cr != null ? cr.ai : AIProfile.Melee;
            if (profile == AIProfile.Passive || profile == AIProfile.Totem || u.IsTotem) return AIStep.End(u, "passive");
            var enemies = VisibleEnemies(b, u);
            if (enemies.Count == 0) return AIStep.End(u, "no enemies");
            bool ranged = IsRangedProfile(u);

            // coward: flee at low health
            if (profile == AIProfile.Coward && u.HealthPct < 20f && !Specials.CannotFlee(u))
            {
                var near = b.NearestHostile(u);
                if (near != null)
                {
                    var flee = MoveAway(b, u, near, 12f, null, 0f, "fleeing");
                    if (flee != null) return flee;
                }
            }

            var target = b.SelectThreatTarget(u, ranged);
            if (target == null) return AIStep.End(u, "no target");

            // skirmishers ignore threat stickiness and go for the weakest reachable
            if (profile == AIProfile.Skirmisher && u.TauntedBy == null)
            {
                Unit weak = null; float hp = float.MaxValue;
                foreach (var e in enemies) if (!e.IsTotem && e.Health < hp && u.DistanceTo(e) <= u.MoveLeft + 3f) { hp = e.Health; weak = e; }
                if (weak != null) target = weak;
            }

            // creature abilities by priority (long casts last so they telegraph with the leftover time)
            var step = CreatureAbilityStep(b, u, target, profile);
            if (step != null) return step;

            // basic attack
            var basic = BasicAttackFor(b, u, target, ranged);
            if (basic != null && (!u.AutoAttacking || u.AttackTarget != target || u.AutoAttackAbility != basic.id) && u.AIMemory.UsedCount(basic.id) == 0)
            {
                var chk = b.CanUse(u, basic, target);
                if (chk.Ok) return Use(u, basic, target, null, "auto attack");
            }

            // movement
            if (ranged && cr != null && profile != AIProfile.Melee)
            {
                float range = MathUtil.Yd(cr.ranged ? cr.rangedRange : 30f);
                if (u.DistanceTo(target) > range + target.Radius - 0.2f)
                {
                    var mv = MoveToRange(b, u, target, range, "closing to range");
                    if (mv != null) return mv;
                }
                else if (profile == AIProfile.Ranged || profile == AIProfile.Caster)
                {
                    // step away from melee attackers
                    var near = b.NearestHostile(u);
                    if (near != null && b.InMeleeRange(u, near) && u.AIMemory.Moves == 0 && u.HealthPct < 90f)
                    {
                        var away = MoveAway(b, u, near, 5f, target, range, "keeping distance");
                        if (away != null) return away;
                    }
                }
            }
            else if (!b.InMeleeRange(u, target))
            {
                var mv = MoveToRange(b, u, target, b.MeleeReachOf(u, target), "closing in");
                if (mv != null)
                {
                    // re-toggle auto attack after moving
                    return mv;
                }
            }
            return AIStep.End(u, "done");
        }

        static AbilityDef BasicAttackFor(Battle b, Unit u, Unit target, bool ranged)
        {
            if (u.Class == null && u.Creature != null && u.Creature.ranged && !b.InMeleeRange(u, target))
                return b.Db.Ability("auto_shot") ?? b.Db.Ability("attack");
            return b.Db.Ability("attack");
        }

        static AIStep CreatureAbilityStep(Battle b, Unit u, Unit target, AIProfile profile)
        {
            var cr = u.Creature;
            if (cr == null || cr.abilities.Count == 0) return null;
            var list = new List<CreatureAbilityDef>(cr.abilities);
            list.Sort((x, y) => y.priority.CompareTo(x.priority));
            AIStep moveFor = null;
            AIStep longCast = null;
            foreach (var ca in list)
            {
                var a = b.Db.Ability(ca.ability);
                if (a == null || a.passive) continue;
                if (ca.chance < 100f)
                {
                    if (!u.AIMemory.ChanceRolls.TryGetValue(ca.ability, out var rolled))
                        u.AIMemory.ChanceRolls[ca.ability] = rolled = b.Rng.Chance(ca.chance);
                    if (!rolled) continue;
                }
                if (u.AIMemory.UsedCount(a.id) > 0 && a.time == TimeCost.OffGcd && a.castTime <= 0) continue;
                var t = PickCreatureAbilityTarget(b, u, a, ca, target, profile);
                if (t == null && a.target != TargetType.Point && a.target != TargetType.Self) continue;
                if (Failed(u, a, t)) continue;
                if (!ConditionMet(b, u, ca.condition, t ?? target)) continue;
                // do not waste buffs/debuffs that are already up
                if (a.aiHint == "Buff" || a.aiHint == "Debuff" || a.aiHint == "CC")
                {
                    bool already = false;
                    foreach (var id in AppliedAuras(a))
                        if ((t ?? u).HasAura(id, a.aiHint == "Buff" ? null : u)) { already = true; break; }
                    if (already) continue;
                }
                Vec2? point = a.target == TargetType.Point ? (t ?? target).Position : (Vec2?)null;
                var chk = b.CanUse(u, a, t, point);
                if (chk.Ok)
                {
                    var mods = AbilityMods.For(u, a);
                    float cast = AbilityRules.CastTime(u, a, mods);
                    // big casts go last: prefer other instants first, then start the cast with the remaining time
                    if (cast >= 2.5f && longCast == null && u.TimeLeft >= cast)
                    {
                        longCast = Use(u, a, t, point, "big cast");
                        continue;
                    }
                    return Use(u, a, t, point, "priority " + ca.priority);
                }
                if (moveFor == null && IsMovableFailure(chk.Code) && t != null && (a.target == TargetType.Enemy || a.target == TargetType.Ally || a.target == TargetType.AllyOther))
                {
                    var mods = AbilityMods.For(u, a);
                    float range = AbilityRules.RangeMetres(u, a, t, mods, b.Config) - t.Radius;
                    if (!float.IsInfinity(range)) moveFor = MoveToRange(b, u, t, range, "moving for " + a.name);
                }
            }
            if (longCast != null) return longCast;
            return moveFor;
        }

        static Unit PickCreatureAbilityTarget(Battle b, Unit u, AbilityDef a, CreatureAbilityDef ca, Unit target, AIProfile profile)
        {
            switch (a.target)
            {
                case TargetType.Self: return u;
                case TargetType.Pet: return u.Pet;
                case TargetType.Ally:
                case TargetType.AllyOther:
                {
                    Unit best = null; float worst = 101f;
                    foreach (var o in b.AlliesOf(u, a.target == TargetType.Ally))
                    {
                        if (o.HealthPct < worst) { worst = o.HealthPct; best = o; }
                    }
                    if (a.aiHint == "Heal" && best != null && best.HealthPct > 80f) return null;
                    return best;
                }
                case TargetType.DeadAlly:
                    foreach (var o in b.Units) if (o.Team == u.Team && o.IsDeadOrDowned) return o;
                    return null;
                default:
                    if (ca.condition != null && ca.condition.Contains("targetCasting"))
                    {
                        foreach (var e in VisibleEnemies(b, u)) if (Battle.HasInterruptibleCast(e)) return e;
                        return null;
                    }
                    if (a.aiHint == "CC" && target != null)
                    {
                        // crowd-control someone other than the main target if possible
                        foreach (var e in VisibleEnemies(b, u)) if (e != target && !e.IsTotem && !e.IsControlled) return e;
                    }
                    return target;
            }
        }

        // ============================================================== pets

        static AIStep PetStep(Battle b, Unit u)
        {
            var owner = u.Owner;
            var enemies = VisibleEnemies(b, u);
            if (enemies.Count == 0) return AIStep.End(u, "no enemies");
            Unit target = null;
            if (owner != null)
            {
                if (owner.AttackTarget != null && owner.AttackTarget.IsAlive && owner.AttackTarget.IsHostileTo(u) && b.CanSee(u, owner.AttackTarget)) target = owner.AttackTarget;
                // tank pets (Voidwalker) pick up enemies hitting their owner or other party members
                if (u.Role == UnitRole.Tank)
                {
                    foreach (var e in enemies)
                        if (e.AggroTarget != null && e.AggroTarget != u && e.AggroTarget.Team == u.Team && e.AggroTarget.Role != UnitRole.Tank) { target = e; break; }
                }
            }
            if (target == null)
            {
                foreach (var e in enemies) if (e.AggroTarget == owner) { target = e; break; }
            }
            target ??= b.NearestHostile(u);
            if (target == null) return AIStep.End(u, "no target");

            // pet abilities: the creature's list (priority, chance per turn, condition), then anything else it knows by aiPriority
            var entries = new List<PetAbility>();
            if (u.Creature != null)
                foreach (var ca in u.Creature.abilities)
                {
                    var a = b.Db.Ability(ca.ability);
                    if (a != null && !a.passive && !a.autoAttack && u.Knows(a.id)) entries.Add(new PetAbility { A = a, C = ca, Priority = ca.priority });
                }
            foreach (var kv in u.Abilities)
            {
                var a = b.Db.Ability(kv.Key);
                if (a == null || a.passive || a.autoAttack || entries.Exists(x => x.A == a)) continue;
                entries.Add(new PetAbility { A = a, Priority = a.aiPriority });
            }
            entries.Sort((x, y) => y.Priority.CompareTo(x.Priority));
            foreach (var en in entries)
            {
                var a = en.A;
                var ca = en.C;
                if (ca != null && ca.chance < 100f)
                {
                    if (ca.chance <= 0f) continue;
                    if (!u.AIMemory.ChanceRolls.TryGetValue(ca.ability, out var rolled))
                        u.AIMemory.ChanceRolls[ca.ability] = rolled = b.Rng.Chance(ca.chance);
                    if (!rolled) continue;
                }
                if (u.AIMemory.UsedCount(a.id) > 0 && a.time == TimeCost.OffGcd) continue;
                Unit t = a.target == TargetType.Self ? u : a.target == TargetType.Ally || a.target == TargetType.AllyOther ? owner : target;
                if (t == null || Failed(u, a, t)) continue;
                if (ca != null && !string.IsNullOrEmpty(ca.condition) && !ConditionMet(b, u, ca.condition, t)) continue;
                string hint = HintOf(a);
                if (hint == "Taunt" && (u.Role != UnitRole.Tank || t.AggroTarget == u)) continue;
                if (hint == "Defensive" && u.HealthPct > 40f) continue;
                if (hint == "Interrupt" && !Battle.HasInterruptibleCast(t)) continue;
                if (hint == "Heal" && (t == null || t.HealthPct > 60f)) continue;
                if ((hint == "Buff" || hint == "Debuff") && AlreadyHasAny(a, t, u)) continue;
                if ((hint == "CC" || hint == "Utility") && ca == null) continue;
                Vec2? point = a.target == TargetType.Point ? target.Position : (Vec2?)null;
                if (b.CanUse(u, a, t, point).Ok) return Use(u, a, t, point, "pet " + hint);
            }
            var basic = BasicAttackFor(b, u, target, u.Creature != null && u.Creature.ranged);
            if (basic != null && (!u.AutoAttacking || u.AttackTarget != target) && u.AIMemory.UsedCount(basic.id) == 0 && b.CanUse(u, basic, target).Ok)
                return Use(u, basic, target, null, "pet attack");
            bool petRanged = u.Creature != null && (u.Creature.ranged || u.Creature.ai == AIProfile.Caster || u.Creature.ai == AIProfile.Ranged);
            if (petRanged)
            {
                float range = MathUtil.Yd(u.Creature.ranged ? u.Creature.rangedRange : 30f);
                if (u.DistanceTo(target) > range)
                {
                    var mv = MoveToRange(b, u, target, range, "pet closing to range");
                    if (mv != null) return mv;
                }
            }
            else if (!b.InMeleeRange(u, target))
            {
                var mv = MoveToRange(b, u, target, b.MeleeReachOf(u, target), "pet closing in", behind: u.Role != UnitRole.Tank);
                if (mv != null) return mv;
            }
            return AIStep.End(u, "pet done");
        }

        struct PetAbility { public AbilityDef A; public CreatureAbilityDef C; public int Priority; }

        /// <summary>
        /// One of the ability's auras is already up on the target and will still be up after the bearer's next turn start
        /// (where it loses <see cref="RulesConstants.TurnSeconds"/>): a fully stacked or non-stacking aura with more than a
        /// turn left. Stacking debuffs (Sunder Armor) are one shared instance whoever applied it last (Battle.ApplyAura), so
        /// they are looked up from any caster; non-stacking debuffs are per caster. Auras that last a turn or less count as
        /// up while present (recasting them in the same turn would only refresh them). A pure DoT/HoT (ticks and nothing
        /// else that lapses with it) counts as up while it still has a tick to deliver: the bearer's turn start ticks it for
        /// what is left before it expires (Battle.ElapseAuraList), so refreshing it a turn early would only throw those
        /// ticks away.
        /// </summary>
        static bool AlreadyHasAny(AbilityDef a, Unit t, Unit caster)
        {
            foreach (var id in AppliedAuras(a))
            {
                var def = caster.Db.Aura(id);
                if (def == null) continue;
                var ex = t.FindAura(id, def.kind == AuraKind.Debuff && def.maxStacks <= 1 ? caster : null);
                if (ex == null || (def.maxStacks > 1 && ex.Stacks < def.maxStacks)) continue;
                if (ex.IsPermanent || ex.Duration <= RulesConstants.TurnSeconds + 1e-3f || ex.Remaining > RulesConstants.TurnSeconds + 1e-3f) return true;
                if (PurePeriodic(def) && ex.TickAccum + ex.Remaining >= def.tickInterval - 1e-4f) return true;
            }
            return false;
        }

        /// <summary>A DoT/HoT whose only lasting effect is its ticks: no states, stat mods, absorb or procs that would lapse
        /// at the bearer's turn start along with its last ticks.</summary>
        static bool PurePeriodic(AuraDef def) =>
            def.tickInterval > 0f && (def.tickEffects.Count > 0 || !string.IsNullOrEmpty(def.special))
            && def.states.Length == 0 && def.mods.Count == 0 && def.absorb == null && def.procs.Count == 0;

        internal static string HintOf(AbilityDef a)
        {
            if (!string.IsNullOrEmpty(a.aiHint)) return a.aiHint;
            foreach (var e in a.effects)
            {
                switch (e.type)
                {
                    case EffectType.Heal: return "Heal";
                    case EffectType.Taunt: return "Taunt";
                    case EffectType.Interrupt: return "Interrupt";
                    case EffectType.Summon: return "Summon";
                    case EffectType.SummonTotem: return "Buff";
                    case EffectType.Damage: case EffectType.WeaponDamage: return a.area.shape != AreaShape.None ? "AoE" : "Damage";
                }
            }
            foreach (var e in a.effects)
                if (e.type == EffectType.ApplyAura) return a.target == TargetType.Enemy ? "Debuff" : "Buff";
            return "Utility";
        }
    }
}
