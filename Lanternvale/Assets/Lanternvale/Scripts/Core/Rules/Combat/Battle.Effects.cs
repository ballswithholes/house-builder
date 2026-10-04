// Ability resolution: casts, channels, pending casts, interrupts, and every EffectType.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum HitOutcome { Hit, Miss, Dodge, Parry, Block, Resist, Immune, Evade }

    /// <summary>
    /// One resolution of an ability (or of aura tick/onApply/onExpire effects, or of proc effects).
    /// Special handlers receive it and may read/modify anything.
    /// </summary>
    public sealed class AbilityCast
    {
        public Battle Battle;
        public Unit Caster;
        /// <summary>The ability (null for aura ticks without a source ability and for talent/item procs).</summary>
        public AbilityDef Ability;
        public int Rank = 1;
        public int EffLevel = 1, LearnLevel = 1;
        /// <summary>Primary target (aura context: the aura bearer; proc context: the other unit of the event).</summary>
        public Unit Target;
        public Vec2 Point;
        public bool HasPoint;
        public AbilityModSet Mods = AbilityModSet.Empty;
        /// <summary>Combo points spent by a finisher (or stored on the aura for ticks).</summary>
        public int ComboPoints;
        /// <summary>Resource drained by cost.consumeAll (Execute).</summary>
        public float ExtraResource;
        /// <summary>No cost, Time or cooldown (TriggerAbility, procs).</summary>
        public bool Free;
        public ItemInstance SourceItem;
        public AuraInstance SourceAura;
        public bool Periodic;
        public ProcDef SourceProc;
        /// <summary>The aura whose proc runs this cast (aura procs), or null.</summary>
        public AuraInstance ProcAura;
        public Unit ProcOther;
        public int Depth;
        public School School;
        /// <summary>Multiplier on magnitudes (channel ticks, chain falloff).</summary>
        public float MagnitudeScale = 1f;
        public List<Unit> AreaUnits;
        public readonly Dictionary<Unit, HitOutcome> Outcomes = new Dictionary<Unit, HitOutcome>();
        readonly HashSet<Unit> blockPending = new HashSet<Unit>();
        public readonly List<Unit> HitTargets = new List<Unit>();
        public bool AnyCrit;
        public float TotalDamage, TotalHealing;
        /// <summary>The primary target avoided the ability (miss/dodge/parry/resist/immune).</summary>
        public bool PrimaryAvoided;
        public bool KilledTarget;
        /// <summary>Free-form values for special handlers.</summary>
        public readonly Dictionary<string, float> Vars = new Dictionary<string, float>();
        /// <summary>Set by a special handler to skip the default effects.</summary>
        public bool SkipEffects;
        /// <summary>The spell was reflected back at its caster.</summary>
        public bool Reflected;

        /// <summary>Hostile targets hit during the current effect pass (hit procs fire once per target after the pass).</summary>
        internal readonly List<Unit> PassHits = new List<Unit>();
        internal readonly Dictionary<Unit, bool> PassCrits = new Dictionary<Unit, bool>();
        internal readonly Dictionary<Unit, float> PassDamage = new Dictionary<Unit, float>();
        /// <summary>Effect currently being applied (for specials).</summary>
        public EffectDef CurrentEffect;

        internal bool ConsumeBlock(Unit t) => blockPending.Remove(t);
        internal void MarkBlock(Unit t) => blockPending.Add(t);

        /// <summary>Centre for *InRadius effect targets (aura bearer for aura effects, else the caster).</summary>
        public Vec2 Center => SourceAura != null && SourceAura.Bearer != null ? SourceAura.Bearer.Position : Caster.Position;
        public bool IsSpell => Ability != null ? AbilityRules.IsSpell(Ability) && Ability.special != "Shoot" : School != School.Physical;
        public AttackKind Kind => Ability != null ? AbilityRules.KindOf(Ability) : (School == School.Physical ? AttackKind.Melee : AttackKind.Spell);
        public string Name => Ability != null ? Ability.name : SourceAura != null ? SourceAura.Def.name : "";
        public string AbilityId => Ability != null ? Ability.id : "";
    }

    public sealed partial class Battle
    {
        internal AbilityCast NewCast(Unit caster, AbilityDef a, int rank, Unit target, Vec2? point, AbilityModSet mods)
        {
            var c = new AbilityCast
            {
                Battle = this, Caster = caster, Ability = a, Rank = Math.Max(1, rank), Target = target,
                Point = point ?? (target != null ? target.Position : caster.Position), HasPoint = point.HasValue,
                Mods = mods ?? AbilityMods.For(caster, a), School = a != null ? a.school : School.Physical,
            };
            if (a != null)
            {
                c.EffLevel = AbilityRules.EffLevel(caster, a, c.Rank);
                c.LearnLevel = a.learnLevel;
            }
            else { c.EffLevel = caster.Level; c.LearnLevel = 1; }
            return c;
        }

        /// <summary>Resolves a cast: cost, cooldown, item, area, effects (or special), combo points, procs.</summary>
        internal void ResolveCast(AbilityCast cast)
        {
            var a = cast.Ability;
            var u = cast.Caster;
            if (!cast.Free)
            {
                PayCost(cast);
                StartCooldown(u, a, cast.Mods);
                ConsumeItem(cast);
            }
            if (a.area.shape != AreaShape.None)
                cast.AreaUnits = Targeting.AreaUnits(this, u, a, cast.Target, cast.HasPoint ? cast.Point : (Vec2?)null, cast.Mods);
            else RedirectOrReflect(cast);
            Specials.ResolveAbility(cast);
            if (!cast.SkipEffects) ExecuteEffects(cast, a.effects);
            Specials.AfterAbility(cast);
            AfterCast(cast);
        }

        /// <summary>Hostile single-target spells may be grounded (Grounding Totem) or reflected (ward talents).</summary>
        void RedirectOrReflect(AbilityCast cast)
        {
            var a = cast.Ability;
            var t = cast.Target;
            if (a == null || t == null || cast.Periodic || cast.Depth > 0 || a.target != TargetType.Enemy) return;
            if (!AbilityRules.IsSpell(a) || a.special == "Shoot" || !t.IsHostileTo(cast.Caster)) return;
            foreach (var e in a.effects) if (e.chainTargets > 0) return;
            var g = Specials.RedirectSpell(this, cast);
            if (g != null && g != t)
            {
                Log($"{g.Name} redirects {a.name}.", g);
                cast.Target = g;
                return;
            }
            float refl = Specials.ReflectChance(t, cast);
            if (refl > 0 && Rng.Chance(refl))
            {
                Log($"{t.Name} reflects {a.name}!", t);
                cast.Target = cast.Caster;
                cast.Reflected = true;
            }
        }

        void ResolveChannelTick(AbilityCast cast, int index, int total)
        {
            if (!cast.Caster.IsAlive) return;
            var a = cast.Ability;
            if (cast.Target != null && !cast.Target.IsAlive && a.target != TargetType.Self && a.target != TargetType.Point) return;
            cast.Outcomes.Clear();
            if (a.area.shape != AreaShape.None)
                cast.AreaUnits = Targeting.AreaUnits(this, cast.Caster, a, cast.Target, cast.HasPoint ? cast.Point : (Vec2?)null, cast.Mods);
            Emit(new CombatEvent { Type = CombatEventType.ChannelTick, Source = cast.Caster, Target = cast.Target, AbilityId = a.id, Name = a.name, Count = index + 1, Amount = total });
            cast.Vars["tick"] = index + 1;
            cast.Vars["ticks"] = total;
            cast.SkipEffects = false;
            Specials.ResolveAbility(cast);
            if (!cast.SkipEffects) ExecuteEffects(cast, a.effects);
            Specials.AfterAbility(cast);
            Specials.OnChannelTick(cast);
        }

        void ResolvePending(Unit u)
        {
            var p = u.Pending;
            u.Pending = null;
            pendingItems.TryGetValue(u, out var item);
            pendingItems.Remove(u);
            if (p == null) return;
            var a = p.Ability;
            var mods = AbilityMods.For(u, a);
            if (p.Target != null && a.target != TargetType.Self && a.target != TargetType.Point)
            {
                bool deadOk = a.target == TargetType.DeadAlly || (a.target == TargetType.Pet && HasEffect(a, EffectType.Resurrect));
                if ((!deadOk && !p.Target.IsAlive) || !Units.Contains(p.Target))
                {
                    Emit(new CombatEvent { Type = CombatEventType.CastFailed, Source = u, Target = p.Target, AbilityId = a.id, Name = a.name, Reason = "Target is no longer valid." });
                    return;
                }
                float max = AbilityRules.RangeMetres(u, a, p.Target, mods, Config);
                if (u.DistanceTo(p.Target) > max + 0.5f)
                {
                    Emit(new CombatEvent { Type = CombatEventType.CastFailed, Source = u, Target = p.Target, AbilityId = a.id, Name = a.name, Reason = "Out of range." });
                    return;
                }
            }
            var cast = NewCast(u, a, p.Rank, p.Target, p.HasPoint ? p.Point : (Vec2?)null, mods);
            cast.ComboPoints = p.ComboPoints;
            cast.SourceItem = item;
            if (p.Channel)
            {
                cast.Free = true;
                for (int i = p.TicksTotal - p.TicksLeft; i < p.TicksTotal && u.IsAlive; i++) ResolveChannelTick(cast, i, p.TicksTotal);
                Emit(new CombatEvent { Type = CombatEventType.CastComplete, Source = u, Target = p.Target, AbilityId = a.id, Name = a.name });
                return;
            }
            float cost = AbilityRules.ResourceCost(u, a, p.Rank, mods);
            if (cost > 0 && u.GetResource(a.cost.type) + 1e-3f < cost)
            {
                Emit(new CombatEvent { Type = CombatEventType.CastFailed, Source = u, Target = p.Target, AbilityId = a.id, Name = a.name, Reason = $"Not enough {a.cost.type.ToString().ToLowerInvariant()}." });
                return;
            }
            if (a.cost != null && a.cost.consumesComboPoints) cast.ComboPoints = ComboPointsOn(u, p.Target);
            ResolveCast(cast);
            Emit(new CombatEvent { Type = CombatEventType.CastComplete, Source = u, Target = p.Target, AbilityId = a.id, Name = a.name });
        }

        /// <summary>Cancels a pending cast/channel. Lockout seconds lock the cast's school (interrupts).</summary>
        public void CancelPending(Unit u, string reason, Unit by, float lockout)
        {
            var p = u.Pending;
            if (p == null) return;
            u.Pending = null;
            pendingItems.Remove(u);
            Emit(new CombatEvent { Type = CombatEventType.CastInterrupted, Source = by ?? u, Target = u, AbilityId = p.Ability.id, Name = p.Ability.name, School = p.Ability.school, Seconds = lockout, Reason = reason });
            if (lockout > 0f)
            {
                var s = p.Ability.school;
                u.Lockouts.TryGetValue(s, out var cur);
                u.Lockouts[s] = Math.Max(cur, lockout);
            }
        }

        void PayCost(AbilityCast cast)
        {
            var u = cast.Caster;
            var a = cast.Ability;
            if (a.cost == null) return;
            float cost = AbilityRules.ResourceCost(u, a, cast.Rank, cast.Mods);
            if (cost > 0 && a.cost.type != ResourceType.None)
            {
                ChangeResource(u, a.cost.type, -cost);
                if (a.cost.type == ResourceType.Mana) { u.ManaSpentTurn = u.TurnsTaken; u.SecondsSinceManaSpent = 0f; }
                cast.Vars["cost"] = cost;
            }
            if (a.cost.consumeAll && a.cost.type != ResourceType.None)
            {
                float extra = u.GetResource(a.cost.type);
                if (extra > 0)
                {
                    cast.ExtraResource = extra;
                    ChangeResource(u, a.cost.type, -extra);
                }
            }
            if (a.cost.health > 0)
            {
                float h = Math.Min(a.cost.health, Math.Max(0f, u.Health - 1f));
                u.Health -= h;
                Emit(new CombatEvent { Type = CombatEventType.ResourceChange, Source = u, Target = u, Amount = -h, Reason = "health cost" });
            }
        }

        void ConsumeItem(AbilityCast cast)
        {
            var it = cast.SourceItem;
            if (it == null || !it.Def.consumable || Inventory == null) return;
            Inventory.Remove(it, 1);
            Emit(new CombatEvent { Type = CombatEventType.ItemConsumed, Source = cast.Caster, Name = it.Name, AbilityId = cast.AbilityId });
        }

        void AfterCast(AbilityCast cast)
        {
            var u = cast.Caster;
            var a = cast.Ability;
            if (a == null) return;
            // combo point builders: +1 (+ComboPoints mods) on hit
            if (a.generatesComboPoint && cast.Target != null && cast.Target.IsHostileTo(u) && cast.HitTargets.Contains(cast.Target))
                AddComboPoints(u, cast.Target, 1 + (int)Math.Round(cast.Mods.ComboPoints));
            bool finisher = a.cost != null && a.cost.consumesComboPoints;
            if (finisher && !cast.PrimaryAvoided && cast.ComboPoints > 0)
            {
                u.ComboPoints = 0;
                Emit(new CombatEvent { Type = CombatEventType.ComboPoints, Source = u, Target = u.ComboTarget, Amount = 0, Count = -cast.ComboPoints });
            }
            // rage refund for avoided single-target abilities
            if (cast.PrimaryAvoided && a.cost != null && a.cost.type == ResourceType.Rage && cast.Vars.TryGetValue("cost", out var paid) && a.area.shape == AreaShape.None)
                ChangeResource(u, ResourceType.Rage, paid * RulesConstants.AvoidedRageRefund);
            if (cast.Depth == 0 && !cast.Periodic)
            {
                var info = new ProcInfo { Ability = a, School = a.school, ComboPoints = cast.ComboPoints, Crit = cast.AnyCrit };
                FireProcs(ProcTrigger.OnAbilityUsed, u, cast.Target, info);
                if (AbilityRules.IsSpell(a) && a.special != "Shoot") FireProcs(ProcTrigger.OnSpellCast, u, cast.Target, info);
                if (IsHealAbility(a)) FireProcs(ProcTrigger.OnHealCast, u, cast.Target, info);
                if (finisher && !cast.PrimaryAvoided) FireProcs(ProcTrigger.OnFinisher, u, cast.Target, info);
                ConsumeChargesOnUse(u, a, cast);
            }
            RefreshAreaAuras();
            CheckBattleEnd();
        }

        static bool IsHealAbility(AbilityDef a)
        {
            if (a.aiHint == "Heal" || AbilityMods.HasTag(a, "Heal")) return true;
            foreach (var e in a.effects) if (e.type == EffectType.Heal) return true;
            return false;
        }

        // ============================================================ combo points

        /// <summary>Combo points the unit has on the target (0 when they are on another target).</summary>
        public int ComboPointsOn(Unit u, Unit target)
        {
            if (u.ComboPoints <= 0) return 0;
            if (target != null && u.ComboTarget != null && target != u.ComboTarget) return 0;
            if (u.ComboTarget != null && !u.ComboTarget.IsAlive) return 0;
            return u.ComboPoints;
        }

        /// <summary>Adds combo points on a target (switching target loses the old points). Max 5.</summary>
        public void AddComboPoints(Unit u, Unit target, int n)
        {
            if (n == 0 || target == null) return;
            if (u.ComboTarget != target) { u.ComboPoints = 0; u.ComboTarget = target; }
            int before = u.ComboPoints;
            u.ComboPoints = MathUtil.Clamp(u.ComboPoints + n, 0, (int)RulesConstants.MaxComboPoints);
            if (u.ComboPoints != before)
                Emit(new CombatEvent { Type = CombatEventType.ComboPoints, Source = u, Target = target, Amount = u.ComboPoints, Count = u.ComboPoints - before });
        }

        // ================================================================= effects

        /// <summary>Executes a list of effects for a cast (ability effects, aura tick/apply/expire effects, proc effects).</summary>
        public void ExecuteEffects(AbilityCast cast, IList<EffectDef> effects)
        {
            if (effects == null) return;
            bool hitProcs = cast.Depth == 0 && !cast.Periodic && cast.SourceAura == null && cast.SourceProc == null && cast.Ability != null;
            if (hitProcs) { cast.PassHits.Clear(); cast.PassCrits.Clear(); cast.PassDamage.Clear(); }
            ExecuteEffectList(cast, effects);
            if (hitProcs) FirePassHitProcs(cast);
        }

        void FirePassHitProcs(AbilityCast cast)
        {
            if (cast.PassHits.Count == 0) return;
            var kind = cast.Kind;
            var w = StatCalculator.GetWeapon(cast.Caster, kind == AttackKind.Ranged ? WeaponSlot.Ranged : WeaponSlot.MainHand);
            foreach (var t in new List<Unit>(cast.PassHits))
            {
                cast.PassCrits.TryGetValue(t, out var crit);
                cast.PassDamage.TryGetValue(t, out var dmg);
                var info = new ProcInfo { Ability = cast.Ability, School = cast.School, Crit = crit, Damage = dmg, WeaponSpeed = w.Valid ? w.Speed : 2f, Ranged = kind == AttackKind.Ranged };
                if (kind == AttackKind.Melee) FireProcs(ProcTrigger.OnMeleeHit, cast.Caster, t, info);
                else if (kind == AttackKind.Ranged || kind == AttackKind.Wand) FireProcs(ProcTrigger.OnRangedHit, cast.Caster, t, info);
                else FireProcs(ProcTrigger.OnSpellHit, cast.Caster, t, info);
            }
            cast.PassHits.Clear();
        }

        void ExecuteEffectList(AbilityCast cast, IList<EffectDef> effects)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (!cast.Caster.IsAlive && cast.SourceAura == null && cast.Ability != null && !cast.Free) break;
                var e = effects[i];
                cast.Vars["effectIndex"] = i;
                if (IsCasterScoped(e.type))
                {
                    if (e.chance < 100f && !Rng.Chance(e.chance)) continue;
                    ApplyEffect(cast, e, cast.Caster, 1f);
                    continue;
                }
                var targets = EffectTargets(cast, e);
                if (e.chainTargets > 0 && targets.Count == 1 && targets[0] != null)
                {
                    var chain = Targeting.Chain(this, cast.Caster, targets[0], e.chainTargets, e.chainRange);
                    float scale = 1f;
                    foreach (var t in chain)
                    {
                        ApplyEffectChecked(cast, e, t, scale);
                        scale *= Math.Max(0f, 1f - e.chainFalloffPct / 100f);
                    }
                    continue;
                }
                foreach (var t in targets) ApplyEffectChecked(cast, e, t, 1f);
            }
        }

        static bool IsCasterScoped(EffectType t) =>
            t == EffectType.Summon || t == EffectType.SummonTotem || t == EffectType.CreateItem || t == EffectType.ResetCooldowns;

        List<Unit> EffectTargets(AbilityCast cast, EffectDef e)
        {
            var list = new List<Unit>();
            var c = cast.Caster;
            switch (e.target)
            {
                case EffectTarget.Target:
                    if (cast.AreaUnits != null && cast.SourceAura == null && cast.SourceProc == null) list.AddRange(cast.AreaUnits);
                    else if (cast.Target != null) list.Add(cast.Target);
                    else if (cast.Ability != null && cast.Ability.target == TargetType.Self) list.Add(c);
                    break;
                case EffectTarget.Self:
                    list.Add(c);
                    break;
                case EffectTarget.Area:
                    if (cast.AreaUnits != null) list.AddRange(cast.AreaUnits);
                    else if (cast.Target != null) list.Add(cast.Target);
                    break;
                case EffectTarget.Pet:
                    if (c.Pet != null && (c.Pet.IsAlive || e.type == EffectType.Resurrect)) list.Add(c.Pet);
                    break;
                case EffectTarget.Owner:
                    if (c.Owner != null) list.Add(c.Owner);
                    break;
                case EffectTarget.AlliesInRadius:
                case EffectTarget.EnemiesInRadius:
                {
                    float r = e.radius > 0 ? e.radius : (cast.Ability != null && cast.Ability.area.radius > 0 ? cast.Ability.area.radius : 30f);
                    float rm = MathUtil.Yd(r * (1f + cast.Mods.RadiusPct / 100f));
                    var src = cast.SourceAura != null && cast.SourceAura.Caster != null ? cast.SourceAura.Caster : c;
                    list.AddRange(Targeting.InRadius(this, src, cast.Center, rm, e.target == EffectTarget.AlliesInRadius));
                    break;
                }
                case EffectTarget.Party:
                    foreach (var u in Units) if (u.IsAlive && u.Team == c.Team && !u.IsTotem) list.Add(u);
                    break;
                case EffectTarget.Attacker:
                    if (cast.ProcOther != null) list.Add(cast.ProcOther);
                    else if (cast.Target != null) list.Add(cast.Target);
                    break;
            }
            return list;
        }

        void ApplyEffectChecked(AbilityCast cast, EffectDef e, Unit t, float chainScale)
        {
            if (t == null) return;
            if (e.type != EffectType.Resurrect && !t.IsAlive && !(e.type == EffectType.Special)) return;
            if (e.chance < 100f && !Rng.Chance(e.chance)) return;
            AuraInstance required = null;
            if (!string.IsNullOrEmpty(e.requireTargetAura))
            {
                required = t.FindAura(e.requireTargetAura, cast.Caster) ?? t.FindAura(e.requireTargetAura);
                if (required == null) return;
            }
            bool hostile = t.IsHostileTo(cast.Caster);
            if (hostile && !cast.Periodic && !e.cannotMiss && NeedsHitRoll(e.type))
            {
                var o = GetOutcome(cast, t, e);
                if (o != HitOutcome.Hit && o != HitOutcome.Block) return;
            }
            if (hostile && !cast.Periodic && !cast.PassHits.Contains(t) && e.type != EffectType.Threat) cast.PassHits.Add(t);
            ApplyEffect(cast, e, t, chainScale);
            if (required != null && e.consumeTargetAura && t.Auras.Contains(required)) RemoveAura(required, AuraRemoveReason.Consumed);
        }

        static bool NeedsHitRoll(EffectType t)
        {
            switch (t)
            {
                case EffectType.Damage: case EffectType.WeaponDamage: case EffectType.ApplyAura: case EffectType.DrainResource:
                case EffectType.Interrupt: case EffectType.Taunt: case EffectType.Knockback: case EffectType.Dispel:
                case EffectType.RemoveAura: case EffectType.Kill:
                    return true;
                default: return false;
            }
        }

        /// <summary>Hit/avoid outcome of the cast on a hostile target (rolled once per cast and target).</summary>
        HitOutcome GetOutcome(AbilityCast cast, Unit t, EffectDef e = null)
        {
            if (cast.Outcomes.TryGetValue(t, out var o)) return o;
            o = RollOutcome(cast, t, e != null && Specials.Unavoidable(cast, e));
            cast.Outcomes[t] = o;
            if (o == HitOutcome.Hit || o == HitOutcome.Block)
            {
                if (!cast.HitTargets.Contains(t)) cast.HitTargets.Add(t);
                if (o == HitOutcome.Block) cast.MarkBlock(t);
            }
            else if (t == cast.Target) cast.PrimaryAvoided = true;
            return o;
        }

        /// <summary>The hit outcome already rolled for a target in this cast, or null when none was rolled.</summary>
        public HitOutcome? OutcomeOf(AbilityCast cast, Unit t) => cast.Outcomes.TryGetValue(t, out var o) ? o : (HitOutcome?)null;

        HitOutcome RollOutcome(AbilityCast cast, Unit t, bool unavoidable)
        {
            var c = cast.Caster;
            var kind = cast.Kind;
            if (t.IsInvulnerable)
            {
                EmitAvoid(CombatEventType.Immune, cast, t);
                return HitOutcome.Immune;
            }
            float hitBonus = cast.Mods.HitChance;
            float roll = Rng.Value * 100f;
            if (kind == AttackKind.Spell || kind == AttackKind.Wand)
            {
                float miss = Formulas.SpellMissChance(c.Level, t.Level) - c.Stats.SpellHit(cast.School) - hitBonus - t.Stats.ChanceToBeHit;
                miss = MathUtil.Clamp(miss, 100f - RulesConstants.MaxHitChance, 100f);
                if (roll < miss)
                {
                    EmitAvoid(kind == AttackKind.Wand ? CombatEventType.Miss : CombatEventType.Resist, cast, t);
                    MetersOf(c).Misses++;
                    if (kind == AttackKind.Spell) Specials.OnSpellResisted(this, t, c, cast);
                    return kind == AttackKind.Wand ? HitOutcome.Miss : HitOutcome.Resist;
                }
                return HitOutcome.Hit;
            }
            bool ranged = kind == AttackKind.Ranged;
            float hit = ranged ? c.Stats.RangedHit : c.Stats.MeleeHit;
            float m = Formulas.MeleeMissChance(c.Level, t.Level, false) - hit - hitBonus - t.Stats.ChanceToBeHit + t.Stats.Defense * 0.04f;
            m = MathUtil.Clamp(m, 100f - RulesConstants.MaxHitChance, 100f);
            bool canAvoid = !t.IsControlled && !unavoidable;
            bool frontal = !c.IsBehind(t);
            float dodge = canAvoid ? Math.Max(0f, t.Stats.Dodge - c.Stats.DodgeChanceAgainstMe) : 0f;
            float parry = canAvoid && !ranged && frontal && t.Stats.CanParry ? t.Stats.Parry : 0f;
            float block = canAvoid && frontal && t.Stats.CanBlock ? t.Stats.BlockChance : 0f;
            if (roll < m) { EmitAvoid(CombatEventType.Miss, cast, t); MetersOf(c).Misses++; return HitOutcome.Miss; }
            roll -= m;
            if (roll < dodge) { OnAvoided(c, t, HitOutcome.Dodge, cast); return HitOutcome.Dodge; }
            roll -= dodge;
            if (roll < parry) { OnAvoided(c, t, HitOutcome.Parry, cast); return HitOutcome.Parry; }
            roll -= parry;
            if (roll < block) { OnAvoided(c, t, HitOutcome.Block, cast); return HitOutcome.Block; }
            return HitOutcome.Hit;
        }

        void EmitAvoid(CombatEventType type, AbilityCast cast, Unit t)
        {
            Emit(new CombatEvent { Type = type, Source = cast.Caster, Target = t, AbilityId = cast.AbilityId, Name = cast.Name, School = cast.School });
        }

        /// <summary>Dodge/parry/block bookkeeping: events, reactive windows, procs.</summary>
        internal void OnAvoided(Unit attacker, Unit target, HitOutcome o, AbilityCast cast, bool autoAttack = false)
        {
            if (o == HitOutcome.Dodge)
            {
                if (cast != null) EmitAvoid(CombatEventType.Dodge, cast, target);
                OpenReactive(attacker, "TargetDodged");
                OpenReactive(target, "SelfDodged");
                MetersOf(attacker).Misses++;
                FireProcs(ProcTrigger.OnDodge, target, attacker, new ProcInfo { Ability = cast?.Ability, AutoAttack = autoAttack });
                FireProcs(ProcTrigger.OnTargetDodged, attacker, target, new ProcInfo { Ability = cast?.Ability, AutoAttack = autoAttack });
            }
            else if (o == HitOutcome.Parry)
            {
                if (cast != null) EmitAvoid(CombatEventType.Parry, cast, target);
                OpenReactive(attacker, "TargetParried");
                OpenReactive(target, "SelfParried");
                MetersOf(attacker).Misses++;
                FireProcs(ProcTrigger.OnParry, target, attacker, new ProcInfo { Ability = cast?.Ability, AutoAttack = autoAttack });
                FireProcs(ProcTrigger.OnTargetParried, attacker, target, new ProcInfo { Ability = cast?.Ability, AutoAttack = autoAttack });
            }
            else if (o == HitOutcome.Block)
            {
                OpenReactive(target, "SelfBlocked");
                FireProcs(ProcTrigger.OnBlock, target, attacker, new ProcInfo { Ability = cast?.Ability, AutoAttack = autoAttack });
            }
        }

        /// <summary>Crit chance (percent) of the cast's caster against the target for this effect.</summary>
        internal float CritChance(AbilityCast cast, Unit t, bool heal, School school)
        {
            var c = cast.Caster;
            float ch;
            var kind = cast.Kind;
            if (heal || kind == AttackKind.Spell || kind == AttackKind.Wand) ch = c.Stats.SpellCrit(school);
            else if (kind == AttackKind.Ranged) ch = c.Stats.RangedCrit;
            else ch = c.Stats.MeleeCrit;
            ch += cast.Mods.CritChance;
            if (!heal && (kind == AttackKind.Melee || kind == AttackKind.Ranged))
            {
                var e = cast.CurrentEffect;
                bool off = e != null && e.offHand;
                var wt = WeaponTalents.AttackWeapon(c, off, kind == AttackKind.Ranged);
                ch += WeaponTalents.StatBonus(c, wt, kind == AttackKind.Ranged ? StatId.RangedCrit : StatId.MeleeCrit);
                if (t != null) ch -= t.Stats.Defense * 0.04f;
            }
            ch = Specials.CritChanceBonus(cast, t, school, ch);
            return MathUtil.Clamp(ch, 0f, 100f);
        }

        internal float CritMultiplier(AbilityCast cast, School school, bool heal)
        {
            var kind = cast.Kind;
            float baseMult = heal || kind == AttackKind.Spell || kind == AttackKind.Wand ? 1.5f : 2f;
            float bonus = (baseMult - 1f) * (1f + cast.Mods.CritBonusPct / 100f) + cast.Caster.Stats.CritDamageBonus(school) / 100f;
            bonus += Specials.CritBonusAdd(cast.Caster, cast.Target) / 100f;
            return 1f + Math.Max(0f, bonus);
        }

        float CreatureDamageMult(Unit c) => c.Class == null && c.Creature != null ? CreatureScaling.DamageMult(c.Creature) : 1f;

        // ================================================================ apply

        /// <summary>Applies one effect to one target (hit roll already done).</summary>
        internal void ApplyEffect(AbilityCast cast, EffectDef e, Unit t, float chainScale)
        {
            var c = cast.Caster;
            var a = cast.Ability;
            bool hostile = t != null && t.IsHostileTo(c);
            cast.CurrentEffect = e;
            if (e.type != EffectType.Special && !string.IsNullOrEmpty(e.special) && Specials.ReplaceEffect(cast, e, t)) return;
            switch (e.type)
            {
                case EffectType.Damage: EffectDamage(cast, e, t, chainScale); break;
                case EffectType.WeaponDamage: EffectWeaponDamage(cast, e, t, chainScale); break;
                case EffectType.Heal: EffectHeal(cast, e, t, chainScale); break;
                case EffectType.ApplyAura:
                {
                    var def = Db.Aura(e.aura);
                    if (def == null) break;
                    float dur;
                    if (!string.IsNullOrEmpty(e.special) && Specials.Get(e.special) != null)
                    {
                        dur = Specials.ModifyAuraDuration(cast, e, t, def, e.duration > 0 ? e.duration : def.duration);
                        if (dur > 0 && !cast.Periodic) dur = (dur + cast.Mods.Duration) * (1f + cast.Mods.DurationPct / 100f);
                    }
                    else
                    {
                        dur = AbilityRules.AuraDuration(def, e, cast.Periodic ? null : cast.Mods, cast.ComboPoints);
                        // ApplyAura `duration` + `perLevel`: extra seconds per level above learnLevel (Hammer of Justice 3-6 s)
                        if (e.duration > 0 && e.perLevel != 0) dur += e.perLevel * Math.Max(0, cast.EffLevel - cast.LearnLevel);
                    }
                    var info = new AuraApplyInfo
                    {
                        Source = a ?? cast.SourceAura?.SourceAbility, Rank = cast.Rank, EffLevel = cast.EffLevel, LearnLevel = cast.LearnLevel,
                        Stacks = Math.Max(1, e.stacks), Duration = dur, ComboPoints = cast.ComboPoints, Mods = cast.Mods,
                    };
                    var inst = ApplyAura(c, t, def, info);
                    if (inst != null && !string.IsNullOrEmpty(e.special)) Specials.AfterAuraEffect(cast, e, t, inst);
                    if (hostile) AddThreat(t, c, e.threat * ThreatMult(c, def.school, cast.Mods), true);
                    break;
                }
                case EffectType.RemoveAura:
                {
                    foreach (var au in new List<AuraInstance>(t.Auras))
                    {
                        if (au.IsPassive) continue;
                        bool match = !string.IsNullOrEmpty(e.aura) ? au.Def.id == e.aura : (!string.IsNullOrEmpty(e.auraTag) && AuraMatchesTag(au, e.auraTag));
                        if (match) RemoveAura(au, AuraRemoveReason.Cancelled);
                    }
                    break;
                }
                case EffectType.Dispel: EffectDispel(cast, e, t); break;
                case EffectType.Interrupt:
                    if (t.Pending != null) CancelPending(t, "interrupted", c, e.lockout);
                    if (hostile && e.threat > 0) AddThreat(t, c, e.threat * ThreatMult(c, School.Physical, cast.Mods), true);
                    break;
                case EffectType.Taunt: Taunt(c, t); break;
                case EffectType.Threat: EffectThreat(cast, e, t); break;
                case EffectType.GainResource: EffectGainResource(cast, e, t); break;
                case EffectType.DrainResource: EffectDrainResource(cast, e, t); break;
                case EffectType.Teleport: EffectTeleport(cast, e, t); break;
                case EffectType.Charge:
                {
                    if (t == c) break;
                    var dir = (c.Position - t.Position);
                    if (dir.SqrLength < 1e-6f) dir = c.Facing * -1f;
                    float stop = Math.Max(0.5f, MeleeReach(c, t) * 0.8f);
                    var dest = Targeting.FreeSpotNear(this, t.Position + dir.Normalized * stop, c.Radius, c.Id, dir);
                    var from = c.Position;
                    SetPosition(c, dest);
                    c.FaceTowards(t.Position);
                    Emit(new CombatEvent { Type = CombatEventType.Charge, Source = c, Target = t, From = from, To = dest, AbilityId = cast.AbilityId, Name = cast.Name });
                    BreakOnMove(c);
                    break;
                }
                case EffectType.Knockback:
                {
                    if (t == c || t.IsTotem || t.Rank == CreatureRank.Boss) break;
                    var dir = t.Position - c.Position;
                    if (dir.SqrLength < 1e-6f) dir = c.Facing;
                    var from = t.Position;
                    var dest = Targeting.WalkLine(this, t, t.Position, dir, MathUtil.Yd(e.distance > 0 ? e.distance : 5f));
                    SetPosition(t, dest);
                    Emit(new CombatEvent { Type = CombatEventType.Knockback, Source = c, Target = t, From = from, To = dest, AbilityId = cast.AbilityId, Name = cast.Name });
                    break;
                }
                case EffectType.Summon: EffectSummon(cast, e); break;
                case EffectType.SummonTotem: EffectSummonTotem(cast, e); break;
                case EffectType.Resurrect: EffectResurrect(cast, e, t); break;
                case EffectType.CreateItem:
                {
                    var def = Db.Item(e.item);
                    if (def == null || Inventory == null || c.Master.Team != PlayerTeam) break;
                    int n = Inventory.Add(def, Math.Max(1, e.count));
                    if (n > 0) Emit(new CombatEvent { Type = CombatEventType.ItemCreated, Source = c, Target = c, Name = def.name, AbilityId = def.id, Count = n });
                    break;
                }
                case EffectType.ResetCooldowns: EffectResetCooldowns(cast, e); break;
                case EffectType.TriggerAbility:
                {
                    var trig = Db.Ability(e.ability);
                    if (trig == null || cast.Depth > RulesConstants.MaxProcDepth) break;
                    TriggerAbility(c, trig, t, cast.HasPoint ? cast.Point : (Vec2?)null, cast.Depth + 1, cast.Rank, cast.ComboPoints);
                    break;
                }
                case EffectType.Kill:
                    if (t.IsInvulnerable) { EmitAvoid(CombatEventType.Immune, cast, t); break; }
                    DealDamage(c, t, t.Health + 1f, School.Physical, new DamageInfo { Ability = a, Name = cast.Name, IgnoreArmor = true, IgnoreAbsorb = true, IgnoreModifiers = true });
                    break;
                case EffectType.Special:
                    Specials.ExecuteEffect(e.special, cast, e, t);
                    break;
            }
            if (hostile && e.threat > 0 && e.type != EffectType.ApplyAura && e.type != EffectType.Interrupt &&
                e.type != EffectType.Damage && e.type != EffectType.WeaponDamage && e.type != EffectType.Threat)
                AddThreat(t, c, e.threat * ThreatMult(c, cast.School, cast.Mods), true);
        }

        internal float ThreatMult(Unit c, School s, AbilityModSet mods) => c.Stats.Threat(s) * (mods != null ? mods.ThreatMult : 1f);

        /// <summary>A tag matches an aura tag, or a UnitState name the aura imposes (Fear, Sleep, Stealth...).</summary>
        public static bool AuraMatchesTag(AuraInstance au, string tag)
        {
            if (au.HasTag(tag)) return true;
            return Enum.TryParse<UnitState>(tag, true, out var st) && au.HasState(st);
        }

        /// <summary>Casts another ability for free (no cost/time/cooldown) on a target.</summary>
        public AbilityCast TriggerAbility(Unit caster, AbilityDef a, Unit target, Vec2? point, int depth = 1, int rank = 0, int comboPoints = 0)
        {
            if (a.target == TargetType.Self) target = caster;
            else if (a.target == TargetType.Pet) target = caster.Pet;
            int r = caster.RankOf(a.id) > 0 ? caster.RankOf(a.id) : (rank > 0 ? Math.Min(rank, AbilityRules.RankCount(a)) : 1);
            var cast = NewCast(caster, a, r, target, point, null);
            cast.Free = true;
            cast.Depth = depth;
            cast.ComboPoints = comboPoints;
            if (a.area.shape != AreaShape.None)
                cast.AreaUnits = Targeting.AreaUnits(this, caster, a, target, point, cast.Mods);
            Specials.ResolveAbility(cast);
            if (!cast.SkipEffects) ExecuteEffects(cast, a.effects);
            Specials.AfterAbility(cast);
            if (a.generatesComboPoint && target != null && target.IsHostileTo(caster) && cast.HitTargets.Contains(target))
                AddComboPoints(caster, target, 1 + (int)Math.Round(cast.Mods.ComboPoints));
            return cast;
        }

        // ---------------------------------------------------------------- damage

        void EffectDamage(AbilityCast cast, EffectDef e, Unit t, float chainScale)
        {
            var c = cast.Caster;
            var school = e.school ?? cast.School;
            float v = AbilityRules.BaseMagnitude(e, cast.EffLevel, cast.LearnLevel, cast.ComboPoints, Rng);
            if (e.coef > 0) v += e.coef * c.Stats.SpellDamage(school);
            if (e.apCoef > 0)
            {
                bool rangedAp = cast.Ability != null && AbilityRules.IsRangedWeaponAbility(cast.Ability);
                v += e.apCoef * (rangedAp ? c.Stats.RangedAttackPower : c.Stats.AttackPower);
            }
            v *= cast.MagnitudeScale * chainScale;
            if (cast.Periodic && cast.SourceAura != null)
                v *= cast.SourceAura.DamageMult * cast.SourceAura.EffectMult * Math.Max(1, cast.SourceAura.Stacks);
            else v *= cast.Mods.DamageMult;
            v *= CreatureDamageMult(c);
            v += Specials.IncomingFlatDamageBonus(cast, e, t, school, false);
            v = Specials.ModifyDamage(cast, e, t, v);
            if (v <= 0f) return;
            bool crit = false;
            if (!cast.Periodic && !e.cannotCrit && Rng.Chance(CritChance(cast, t, false, school)))
            {
                crit = true;
                v *= CritMultiplier(cast, school, false);
            }
            var info = new DamageInfo
            {
                Ability = cast.Ability, Name = cast.Name, Crit = crit, Periodic = cast.Periodic, Kind = cast.Kind,
                Blocked = cast.ConsumeBlock(t), BonusThreat = e.threat, Mods = cast.Mods, SourceAura = cast.SourceAura, Cast = cast,
            };
            float dealt = DealDamage(c, t, v, school, info);
            NoteHit(cast, t, crit, dealt);
            if (crit) { cast.AnyCrit = true; Specials.OnEffectCrit(cast, e, t, dealt, false); }
            cast.TotalDamage += dealt;
            if (e.pctOfDamage > 0 && dealt > 0 && c.IsAlive)
                HealUnit(c, c, dealt * e.pctOfDamage / 100f, new HealInfo { Ability = cast.Ability, Name = cast.Name, Periodic = cast.Periodic });
        }

        void EffectWeaponDamage(AbilityCast cast, EffectDef e, Unit t, float chainScale)
        {
            var c = cast.Caster;
            var slot = e.ranged ? WeaponSlot.Ranged : (e.offHand ? WeaponSlot.OffHand : WeaponSlot.MainHand);
            var w = StatCalculator.GetWeapon(c, slot);
            if (!w.Valid) return;
            bool wand = w.Type == WeaponType.Wand || (cast.Ability != null && cast.Ability.special == "Shoot");
            float ap = wand ? 0f : (slot == WeaponSlot.Ranged ? c.Stats.RangedAttackPower + Specials.IncomingRangedApBonus(c, t) : c.Stats.AttackPower);
            ap += cast.Vars.TryGetValue("apBonus", out var apb) ? apb : 0f;
            float roll = Rng.Range(w.Min, w.Max) + ap / 14f * w.Speed;
            float v = roll * e.weaponPct / 100f;
            if (e.min > 0 || e.max > 0 || e.perLevel != 0 || e.perCombo != 0)
                v += AbilityRules.BaseMagnitude(e, cast.EffLevel, cast.LearnLevel, cast.ComboPoints, Rng) * Specials.WeaponFlatBonusMult(cast, e);
            if (e.apCoef > 0) v += e.apCoef * ap;
            if (slot == WeaponSlot.OffHand) v *= RulesConstants.OffHandDamageFactor * Specials.OffHandMultiplier(c);
            if (!wand) v *= 1f + WeaponTalents.StatBonus(c, w.Type, StatId.DamageDone) / 100f;
            v *= cast.MagnitudeScale * chainScale;
            if (cast.Periodic && cast.SourceAura != null) v *= cast.SourceAura.DamageMult * cast.SourceAura.EffectMult;
            else v *= cast.Mods.DamageMult;
            v *= CreatureDamageMult(c);
            var school = e.school ?? (cast.School != School.Physical ? cast.School : w.School);
            v += Specials.IncomingFlatDamageBonus(cast, e, t, school, true);
            v = Specials.ModifyDamage(cast, e, t, v);
            if (v <= 0f) return;
            bool crit = false;
            if (!cast.Periodic && !e.cannotCrit && Rng.Chance(CritChance(cast, t, false, school)))
            {
                crit = true;
                v *= CritMultiplier(cast, school, false);
            }
            var info = new DamageInfo
            {
                Ability = cast.Ability, Name = cast.Name, Crit = crit, Periodic = cast.Periodic, Kind = cast.Kind,
                Blocked = cast.ConsumeBlock(t), BonusThreat = e.threat, Mods = cast.Mods, OffHand = slot == WeaponSlot.OffHand,
                Ranged = slot == WeaponSlot.Ranged, Weapon = w, Cast = cast,
                ExtraAttack = cast.SourceProc != null && !cast.Periodic && slot != WeaponSlot.Ranged,
            };
            float dealt = DealDamage(c, t, v, school, info);
            NoteHit(cast, t, crit, dealt);
            if (crit) { cast.AnyCrit = true; Specials.OnEffectCrit(cast, e, t, dealt, false); }
            cast.TotalDamage += dealt;
        }

        void EffectHeal(AbilityCast cast, EffectDef e, Unit t, float chainScale)
        {
            var c = cast.Caster;
            var school = e.school ?? cast.School;
            float v;
            if (e.pctOfMax > 0) v = t.MaxHealth * e.pctOfMax / 100f;
            else
            {
                v = AbilityRules.BaseMagnitude(e, cast.EffLevel, cast.LearnLevel, cast.ComboPoints, Rng);
                if (e.coef > 0) v += e.coef * c.Stats.HealingPower;
                if (e.apCoef > 0) v += e.apCoef * c.Stats.AttackPower;
            }
            v *= cast.MagnitudeScale * chainScale;
            if (cast.Periodic && cast.SourceAura != null)
                v *= cast.SourceAura.HealingMult * cast.SourceAura.EffectMult * Math.Max(1, cast.SourceAura.Stacks);
            else v *= cast.Mods.HealingMult;
            v *= c.Stats.HealingDone;
            v = Specials.ModifyHealing(cast, e, t, v);
            if (v <= 0f) return;
            bool crit = false;
            if (!cast.Periodic && !e.cannotCrit && e.pctOfMax <= 0 && Rng.Chance(CritChance(cast, t, true, school)))
            {
                crit = true;
                v *= CritMultiplier(cast, school, true);
            }
            if (crit) cast.AnyCrit = true;
            float healed = HealUnit(c, t, v, new HealInfo { Ability = cast.Ability, Name = cast.Name, Crit = crit, Periodic = cast.Periodic, Mods = cast.Mods, Cast = cast });
            cast.TotalHealing += healed;
            if (!cast.HitTargets.Contains(t)) cast.HitTargets.Add(t);
            if (crit)
            {
                var pi = new ProcInfo { Ability = cast.Ability, School = school, Crit = true, Damage = healed };
                FireProcs(ProcTrigger.OnCrit, c, t, pi);
                FireProcs(ProcTrigger.OnSpellCrit, c, t, pi);
                Specials.OnEffectCrit(cast, e, t, healed, true);
            }
        }

        void NoteHit(AbilityCast cast, Unit t, bool crit, float dealt)
        {
            if (crit) cast.PassCrits[t] = true;
            cast.PassDamage.TryGetValue(t, out var d);
            cast.PassDamage[t] = d + dealt;
        }

        // ------------------------------------------------------------------ misc

        void EffectDispel(AbilityCast cast, EffectDef e, Unit t)
        {
            bool hostile = t.IsHostileTo(cast.Caster);
            int n = Math.Max(1, e.dispelCount);
            for (int i = t.Auras.Count - 1; i >= 0 && n > 0; i--)
            {
                if (i >= t.Auras.Count) continue;
                var au = t.Auras[i];
                if (au.IsPassive || au.IsAreaChild || au.Def.dispel == DispelType.None) continue;
                if (hostile ? au.Def.kind != AuraKind.Buff : au.Def.kind != AuraKind.Debuff) continue;
                if (e.dispelType != DispelType.None && au.Def.dispel != e.dispelType) continue;
                Emit(new CombatEvent { Type = CombatEventType.Dispel, Source = cast.Caster, Target = t, AuraId = au.Def.id, Name = au.Def.name, AbilityId = cast.AbilityId });
                RemoveAura(au, AuraRemoveReason.Dispelled);
                n--;
            }
        }

        void EffectThreat(AbilityCast cast, EffectDef e, Unit t)
        {
            var c = cast.Caster;
            var tables = new List<Unit>();
            if (e.target == EffectTarget.Self || t == c)
            {
                foreach (var u in Units) if (u.Threat.ContainsKey(c)) tables.Add(u);
            }
            else tables.Add(t);
            float flat = (e.threat != 0 ? e.threat : e.amount);
            if (flat != 0 || e.perLevel != 0) flat = (flat + e.perLevel * (cast.EffLevel - cast.LearnLevel)) * cast.Mods.EffectMult;
            foreach (var tab in tables)
            {
                if (tab.Team == c.Team) continue;
                if (e.threatPct != 0f)
                {
                    tab.Threat.TryGetValue(c, out var cur);
                    float nv = Math.Max(0f, cur * (1f + e.threatPct / 100f));
                    tab.Threat[c] = nv;
                    Emit(new CombatEvent { Type = CombatEventType.Threat, Source = c, Target = tab, Amount = nv - cur, AbilityId = cast.AbilityId });
                    if (nv <= 0f && tab.AggroTarget == c) tab.AggroTarget = null;
                }
                if (flat != 0f) AddThreat(tab, c, flat * ThreatMult(c, cast.School, cast.Mods), true);
            }
        }

        static bool TryParseResource(string s, out ResourceType r, out bool health, out bool combo)
        {
            health = string.Equals(s, "Health", StringComparison.OrdinalIgnoreCase);
            combo = string.Equals(s, "ComboPoints", StringComparison.OrdinalIgnoreCase);
            r = ResourceType.None;
            if (health || combo) return true;
            return Enum.TryParse(s, true, out r);
        }

        void EffectGainResource(AbilityCast cast, EffectDef e, Unit t)
        {
            var c = cast.Caster;
            if (!TryParseResource(e.resource, out var r, out var health, out var combo)) return;
            float amt = e.pctOfMax > 0
                ? (health ? t.MaxHealth : t.MaxResource(r)) * e.pctOfMax / 100f
                : ((e.amount != 0 ? e.amount : e.min) + e.perLevel * (cast.EffLevel - cast.LearnLevel) + e.perCombo * cast.ComboPoints) * cast.Mods.EffectMult;
            amt *= cast.MagnitudeScale;
            amt = Specials.ModifyResourceGain(cast, e, t, amt);
            if (combo)
            {
                var on = cast.Target != null && cast.Target.IsHostileTo(c) ? cast.Target : (c.ComboTarget ?? cast.ProcOther);
                if (on != null && on.IsHostileTo(c)) AddComboPoints(c, on, (int)Math.Round(amt));
                return;
            }
            if (health)
            {
                if (amt >= 0) HealUnit(c, t, amt, new HealInfo { Ability = cast.Ability, Name = cast.Name, Periodic = cast.Periodic, Mods = cast.Mods });
                else
                {
                    float loss = Math.Min(-amt, Math.Max(0f, t.Health - 1f));
                    t.Health -= loss;
                    Emit(new CombatEvent { Type = CombatEventType.Damage, Source = c, Target = t, Amount = loss, AbilityId = cast.AbilityId, Name = cast.Name, Reason = "self" });
                }
                return;
            }
            if (r == ResourceType.Rage) amt *= t.Stats.RageGenerated;
            float gained = ChangeResource(t, r, amt, c);
            if (gained > 0 && (r == ResourceType.Mana || r == ResourceType.Rage) && InCombat && !cast.Periodic)
                SplitThreat(t, gained * (r == ResourceType.Rage ? 5f : 0.5f), c);
        }

        void EffectDrainResource(AbilityCast cast, EffectDef e, Unit t)
        {
            var c = cast.Caster;
            if (!TryParseResource(e.resource, out var r, out var health, out var combo) || combo) return;
            float amt = ((e.amount != 0 ? e.amount : e.min) + e.perLevel * (cast.EffLevel - cast.LearnLevel)) * cast.Mods.EffectMult * cast.MagnitudeScale;
            if (cast.Periodic && cast.SourceAura != null) amt *= cast.SourceAura.EffectMult;
            if (health && t == c)
            {
                float loss = Math.Min(amt, Math.Max(0f, c.Health - 1f));
                c.Health -= loss;
                Emit(new CombatEvent { Type = CombatEventType.Damage, Source = c, Target = c, Amount = loss, AbilityId = cast.AbilityId, Name = cast.Name, Reason = "self", Periodic = cast.Periodic });
                cast.Vars["drained"] = loss;
                return;
            }
            if (health)
            {
                float dealt = DealDamage(c, t, amt, e.school ?? cast.School, new DamageInfo { Ability = cast.Ability, Name = cast.Name, Periodic = cast.Periodic, Mods = cast.Mods, Kind = AttackKind.Spell, Cast = cast });
                if (e.pctToCaster > 0 && dealt > 0) HealUnit(c, c, dealt * e.pctToCaster / 100f, new HealInfo { Ability = cast.Ability, Name = cast.Name, Periodic = cast.Periodic });
                return;
            }
            float have = t.GetResource(r);
            float take = Math.Min(have, Math.Max(0f, amt));
            if (take <= 0) return;
            ChangeResource(t, r, -take, c);
            if (t.IsHostileTo(c)) AddThreat(t, c, 0f, true);
            if (e.pctToCaster > 0) ChangeResource(c, r, take * e.pctToCaster / 100f, c);
            cast.Vars["drained"] = take;
        }

        void EffectTeleport(AbilityCast cast, EffectDef e, Unit t)
        {
            var c = cast.Caster;
            var from = c.Position;
            Vec2 dest;
            if (cast.HasPoint && cast.Ability != null && cast.Ability.target == TargetType.Point && e.distance <= 0)
                dest = Pathfinder.ClampToWalkable(cast.Point, c.Radius, c.Id);
            else
            {
                var dir = cast.HasPoint ? cast.Point - c.Position : c.Facing;
                if (dir.SqrLength < 1e-6f) dir = c.Facing;
                dest = Targeting.WalkLine(this, c, c.Position, dir, MathUtil.Yd(e.distance > 0 ? e.distance : 20f));
            }
            SetPosition(c, dest);
            if ((dest - from).SqrLength > 1e-4f) c.FaceTowards(dest + (dest - from));
            Emit(new CombatEvent { Type = CombatEventType.Teleport, Source = c, Target = c, From = from, To = dest, AbilityId = cast.AbilityId, Name = cast.Name });
            BreakOnMove(c);
        }

        void EffectResetCooldowns(AbilityCast cast, EffectDef e)
        {
            var c = cast.Caster;
            bool all = e.abilities.Length == 0 && e.tags.Length == 0 && e.schools.Length == 0;
            var remove = new List<string>();
            foreach (var key in c.Cooldowns.Keys)
            {
                if (key.StartsWith("grp:")) continue;
                if (cast.Ability != null && key == cast.Ability.id) continue;
                var ad = Db.Ability(key);
                if (ad == null) continue;
                bool match = all;
                if (!match) foreach (var id in e.abilities) if (id == key) { match = true; break; }
                if (!match) foreach (var tg in e.tags) if (AbilityMods.HasTag(ad, tg)) { match = true; break; }
                if (!match) foreach (var s in e.schools) if (ad.school == s) { match = true; break; }
                if (!match) continue;
                remove.Add(key);
                if (!string.IsNullOrEmpty(ad.cooldownGroup)) remove.Add("grp:" + ad.cooldownGroup);
            }
            foreach (var k in remove) c.Cooldowns.Remove(k);
            if (remove.Count > 0) Emit(new CombatEvent { Type = CombatEventType.CooldownReset, Source = c, Target = c, AbilityId = cast.AbilityId, Name = cast.Name, Count = remove.Count });
        }

        void EffectResurrect(AbilityCast cast, EffectDef e, Unit t)
        {
            if (t == null || !t.IsDeadOrDowned || !t.IsFriendlyTo(cast.Caster)) return;
            float pct = e.pctOfMax > 0 ? e.pctOfMax : 35f;
            Revive(t, Math.Max(1f, t.MaxHealth * pct / 100f), t.MaxMana * pct / 100f, cast.Caster, cast.Name);
        }

        /// <summary>Revives a downed party member or dead pet with the given health/mana.</summary>
        public void Revive(Unit t, float health, float mana, Unit by, string name = "")
        {
            t.Downed = false;
            t.Dead = false;
            t.Health = Math.Min(t.MaxHealth, Math.Max(1f, health));
            if (mana > 0) t.Mana = Math.Min(t.MaxMana, mana);
            if (!Units.Contains(t)) AddUnit(t);
            else
            {
                Pathfinder?.SetUnit(t.Id, t.Position, t.Radius);
                if (Started && InCombat && !TurnOrder.Contains(t) && !t.IsTotem) InsertIntoTurnOrder(t);
            }
            foreach (var e in Units) if (e.IsAlive && e.Team != PlayerTeam && e.Team != t.Team && !e.Threat.ContainsKey(t)) e.Threat[t] = 0f;
            Emit(new CombatEvent { Type = CombatEventType.Revive, Source = by ?? t, Target = t, Amount = t.Health, Name = name });
        }

        // ---------------------------------------------------------------- summons

        void EffectSummon(AbilityCast cast, EffectDef e)
        {
            var c = cast.Caster;
            var def = Db.Creature(e.summon);
            if (def == null) return;
            bool isPet = def.rank == CreatureRank.Pet && e.lifetime < 0;
            int n = Math.Max(1, e.count);
            for (int i = 0; i < n; i++)
            {
                if (isPet && c.Pet != null)
                {
                    var old = c.Pet;
                    c.Pet = null;
                    if (Units.Contains(old)) RemoveUnit(old, "dismissed");
                }
                var u = UnitFactory.CreateSummon(Db, def, c, isPet ? UnitKind.Pet : UnitKind.Summon, e.lifetime);
                u.Position = Targeting.FreeSpotNear(this, c.Position + c.Facing * 1.2f, u.Radius, u.Id, c.Facing);
                u.Facing = c.Facing;
                if (isPet) c.Pet = u; else c.Summons.Add(u);
                AddUnit(u);
                Emit(new CombatEvent { Type = CombatEventType.Summon, Source = c, Target = u, Name = u.Name, AbilityId = cast.AbilityId, To = u.Position });
                ApplyPassives(u);
                Specials.OnSummoned(cast, u);
            }
        }

        void EffectSummonTotem(AbilityCast cast, EffectDef e)
        {
            var c = cast.Caster;
            var def = Db.Creature(e.summon);
            if (def == null) return;
            string element = !string.IsNullOrEmpty(e.totemElement) ? e.totemElement : def.totemElement;
            if (string.IsNullOrEmpty(element)) element = def.id;
            if (c.Totems.TryGetValue(element, out var old) && old != null) Despawn(old, "replaced");
            var u = UnitFactory.CreateSummon(Db, def, c, UnitKind.Totem, e.lifetime);
            u.TotemElement = element;
            Vec2 at = cast.HasPoint && cast.Ability != null && cast.Ability.target == TargetType.Point ? cast.Point : c.Position + c.Facing * 1.0f + SideOffset(c, element);
            u.Position = Targeting.FreeSpotNear(this, at, u.Radius, u.Id, c.Facing);
            u.Facing = c.Facing;
            c.Totems[element] = u;
            AddUnit(u);
            Emit(new CombatEvent { Type = CombatEventType.Summon, Source = c, Target = u, Name = u.Name, AbilityId = cast.AbilityId, To = u.Position, Reason = "totem" });
            ApplyPassives(u);
            Specials.OnSummoned(cast, u);
            RefreshAreaAuras();
        }

        static Vec2 SideOffset(Unit c, string element)
        {
            var perp = new Vec2(-c.Facing.y, c.Facing.x);
            switch ((element ?? "").ToLowerInvariant())
            {
                case "earth": return perp * 0.8f;
                case "fire": return perp * -0.8f;
                case "water": return perp * 1.6f;
                case "air": return perp * -1.6f;
                default: return Vec2.Zero;
            }
        }

        /// <summary>Applies creature passives, passive abilities and class passives to a unit entering the battle.</summary>
        public void ApplyPassives(Unit u)
        {
            if (u.Creature != null && u.Class == null)
                foreach (var id in u.Creature.passives)
                {
                    var def = Db.Aura(id);
                    if (def != null && !u.HasAura(id)) ApplyAura(u, u, def, new AuraApplyInfo { Passive = true, EffLevel = u.Level, LearnLevel = 1 });
                }
            foreach (var kv in new List<KeyValuePair<string, int>>(u.Abilities))
            {
                var a = Db.Ability(kv.Key);
                if (a == null || !a.passive || string.IsNullOrEmpty(a.passiveAura)) continue;
                var def = Db.Aura(a.passiveAura);
                if (def != null && !u.HasAura(def.id))
                    ApplyAura(u, u, def, new AuraApplyInfo { Passive = true, Source = a, Rank = kv.Value, EffLevel = AbilityRules.EffLevel(u, a, kv.Value), LearnLevel = a.learnLevel });
            }
        }
    }
}
