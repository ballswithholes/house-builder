// Hunter specials: the persistent pet (call, dismiss, revive, tame), feign death, traps, marks and talents.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterHunter()
        {
            Register(new HunterCallPet());
            Register(new HunterDismissPet());
            Register(new HunterRevivePet());
            Register(new HunterTameBeast());
            Register(new HunterFeignDeath());
            Register(new HunterTrap());
            Register(new HunterMark());
            Register(new HunterBestialWrath());
            Register(new HunterImprovedMendPet());
            Register(new HunterSpiritBond());
            Register(new HunterCreatureSlaying());
            Register(new HunterImprovedScorpidSting());
            Register(new HunterSurefooted());
            Register(new HunterEntrapment());
            Register(new NoOpSpecial("HunterImprovedEyesOfTheBeast"));
        }
    }

    /// <summary>A documented special with no rules effect in Lanternvale (kept for talent-tree parity).</summary>
    sealed class NoOpSpecial : SpecialHandler
    {
        public NoOpSpecial(string name) : base(name) { }
    }

    /// <summary>Shared helpers for the hunter's persistent pet.</summary>
    static class HunterPets
    {
        internal const string DefaultTemplate = "hunter_pet_wolf";

        internal static HunterPetState State(Unit hunter, string template = null)
        {
            if (hunter.HunterPet == null)
            {
                var t = string.IsNullOrEmpty(template) ? DefaultTemplate : template;
                var def = hunter.Db.Creature(t);
                hunter.HunterPet = new HunterPetState { TemplateId = t, Name = def != null ? def.name : "Wolf", HealthFraction = 1f };
            }
            return hunter.HunterPet;
        }

        internal static bool PetPresent(Battle b, Unit hunter) => hunter.Pet != null && hunter.Pet.IsAlive && b.Units.Contains(hunter.Pet);

        /// <summary>Summons the active pet next to <paramref name="at"/> with a health fraction and focus.</summary>
        internal static Unit Summon(Battle b, Unit hunter, Vec2 at, float healthFraction, float focus)
        {
            var st = State(hunter);
            var def = b.Db.Creature(st.TemplateId) ?? b.Db.Creature(DefaultTemplate);
            if (def == null) return null;
            var pet = b.SummonUnit(hunter, def, UnitKind.Pet, -1f, at, string.IsNullOrEmpty(st.Name) ? null : st.Name);
            pet.Health = Math.Max(1f, pet.MaxHealth * MathUtil.Clamp(healthFraction, 0f, 1f));
            pet.Focus = Math.Min(pet.MaxResource(ResourceType.Focus), focus);
            st.Dead = false;
            st.HealthFraction = pet.Health / Math.Max(1f, pet.MaxHealth);
            return pet;
        }
    }

    /// <summary>Call Pet: summons the hunter's active pet (template + name stored on the hunter) at its remembered health.</summary>
    sealed class HunterCallPet : SpecialHandler
    {
        public HunterCallPet() : base("HunterCallPet") { }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            if (u.HunterPet != null && u.HunterPet.Dead) return "Your pet is dead.";
            if (HunterPets.PetPresent(b, u)) return "You already have a pet.";
            return null;
        }

        public override void Resolve(AbilityCast c)
        {
            c.SkipEffects = true;
            var u = c.Caster;
            string template = null;
            foreach (var e in c.Ability.effects) if (e.type == EffectType.Summon) { template = e.summon; break; }
            var st = HunterPets.State(u, template);
            if (st.Dead) return;
            HunterPets.Summon(c.Battle, u, u.Position + u.Facing * 1.2f, st.HealthFraction > 0f ? st.HealthFraction : 1f, 100f);
        }
    }

    /// <summary>Dismiss Pet: removes the living pet without killing it; its health is remembered.</summary>
    sealed class HunterDismissPet : SpecialHandler
    {
        public HunterDismissPet() : base("HunterDismissPet") { }
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            var pet = u.Pet;
            if (pet == null || !pet.IsAlive) return;
            HunterPets.State(u, pet.Creature?.id).HealthFraction = pet.Health / Math.Max(1f, pet.MaxHealth);
            c.Battle.DismissPet(u);
        }
    }

    /// <summary>Revive Pet: a dead pet returns with 15% (× Improved Revive Pet) health and 0 focus; a dismissed one at its remembered health.</summary>
    sealed class HunterRevivePet : SpecialHandler
    {
        public HunterRevivePet() : base("HunterRevivePet") { }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            if (HunterPets.PetPresent(b, u)) return "Your pet is already here.";
            return null;
        }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            var u = c.Caster;
            if (HunterPets.PetPresent(b, u)) return;
            var st = HunterPets.State(u);
            var at = u.Pet != null && b.Units.Contains(u.Pet) ? u.Pet.Position : u.Position + u.Facing * 1.2f;
            if (st.Dead || (u.Pet != null && u.Pet.Dead))
            {
                float pct = (e.pctOfMax > 0f ? e.pctOfMax : 15f) * c.Mods.EffectMult;
                HunterPets.Summon(b, u, at, pct / 100f, 0f);
            }
            else HunterPets.Summon(b, u, at, st.HealthFraction > 0f ? st.HealthFraction : 1f, 100f);
        }
    }

    /// <summary>Tame Beast: when the channel completes, the beast leaves its side and becomes the hunter's active pet.</summary>
    sealed class HunterTameBeast : SpecialHandler
    {
        public HunterTameBeast() : base("HunterTameBeast") { }

        static string Why(Battle b, Unit u, Unit t)
        {
            if (t == null) return null;
            var cr = t.Creature;
            if (cr == null || t.Class != null || t.Kind != UnitKind.Creature || t.Owner != null) return "That cannot be tamed.";
            if (!cr.tameable || cr.type != CreatureType.Beast) return "That creature is not tameable.";
            if (cr.rank == CreatureRank.Boss || cr.rank == CreatureRank.Elite) return "That beast is too powerful to tame.";
            if (t.Level > u.Level) return "That beast is too high level.";
            if (HunterPets.PetPresent(b, u)) return "You already have a pet.";
            return null;
        }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) => target != null && target.IsHostileTo(u) ? Why(b, u, target) : null;

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            var u = c.Caster;
            if (t == null || !t.IsAlive || Why(b, u, t) != null) return;
            string template = "hunter_pet_" + (t.Creature.family ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(t.Creature.family) || b.Db.Creature(template) == null) template = HunterPets.DefaultTemplate;
            float frac = t.Health / Math.Max(1f, t.MaxHealth);
            var at = t.Position;
            b.RemoveUnit(t, "tamed");
            u.HunterPet = new HunterPetState { TemplateId = template, Name = t.Name, HealthFraction = frac };
            HunterPets.Summon(b, u, at, frac, 100f);
            b.Emit(new CombatEvent { Type = CombatEventType.Log, Source = u, Target = u.Pet, Text = $"{u.Name} tames {t.Name}." });
            b.CheckBattleEnd();
        }
    }

    /// <summary>
    /// Feign Death: enemies that resist (spell hit roll, Improved Feign Death) keep their threat and keep attacking;
    /// the hunter stops attacking, loses her pending cast and the rest of her turn.
    /// </summary>
    sealed class HunterFeignDeath : SpecialHandler
    {
        public HunterFeignDeath() : base("HunterFeignDeath") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            var u = c.Caster;
            foreach (var k in new List<string>(u.Vars.Keys)) if (k.StartsWith("fd_resist:", StringComparison.Ordinal)) u.Vars.Remove(k);
            foreach (var o in b.Units)
            {
                if (!o.IsAlive || !o.IsHostileTo(u) || !o.Threat.TryGetValue(u, out var threat)) continue;
                float miss = Formulas.SpellMissChance(u.Level, o.Level) - c.Mods.HitChance;
                if (b.Rng.Chance(miss)) u.SetVar("fd_resist:" + o.Id, Math.Max(0.01f, threat));
            }
        }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            var u = a.Bearer;
            foreach (var o in b.Units)
            {
                if (!u.Vars.TryGetValue("fd_resist:" + o.Id, out var threat)) continue;
                o.Threat[u] = threat;
                o.AggroTarget = u;
                b.Emit(new CombatEvent { Type = CombatEventType.Resist, Source = u, Target = o, AuraId = a.Def.id, Name = a.Def.name, Reason = "sees through Feign Death" });
            }
            b.StopAutoAttack(u);
            b.CancelQueuedSwing(u);
            b.CancelPending(u, "feign death", null, 0f);
            if (b.InCombat && b.ActiveUnit == u) u.TimeLeft = 0f;
        }

        public override void OnAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason)
        {
            var u = a.Bearer;
            foreach (var k in new List<string>(u.Vars.Keys)) if (k.StartsWith("fd_resist:", StringComparison.Ordinal)) u.Vars.Remove(k);
        }
    }

    /// <summary>
    /// Armed hunter trap: untargetable and immune; a hostile unit moving within 5 yards stops there and the trap fires
    /// its first creature ability on it immediately (otherwise it acts like a totem at the end of the owner's turn).
    /// </summary>
    sealed class HunterTrap : SpecialHandler
    {
        public HunterTrap() : base("HunterTrap") { }

        public override void OnAuraApplied(Battle b, AuraInstance a) => b.Pathfinder?.SetUnit(a.Bearer.Id, a.Bearer.Position, 0f);

        public override float MovementTriggerRadius(AuraInstance a)
        {
            var ab = FirstAbility(a.Bearer);
            return ab != null && a.Bearer.CooldownLeft(ab) <= 0f ? MathUtil.Yd(5f) : 0f;
        }

        static AbilityDef FirstAbility(Unit trap)
        {
            var cr = trap.Creature;
            if (cr == null || cr.abilities.Count == 0) return null;
            return trap.Db.Ability(cr.abilities[0].ability);
        }

        public override void OnMovementTrigger(Battle b, AuraInstance a, Unit mover)
        {
            var trap = a.Bearer;
            var ab = FirstAbility(trap);
            if (ab == null || !trap.IsAlive || !mover.IsAlive || trap.CooldownLeft(ab) > 0f) return;
            b.Emit(new CombatEvent { Type = CombatEventType.Log, Source = trap, Target = mover, AbilityId = ab.id, Name = ab.name, Text = $"{mover.Name} triggers {trap.Name}!" });
            b.StartCooldown(trap, ab, AbilityMods.For(trap, ab));
            b.TriggerAbility(trap, ab, mover, mover.Position, 1);
        }

        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming) =>
            holder != null && caster != null && caster.IsHostileTo(target) ? "Immune" : null;

        public override float ModifyIncomingDamage(Battle b, AuraInstance a, Unit src, float amount, School school, DamageInfo info) =>
            src != null && src.IsHostileTo(a.Bearer) ? 0f : amount;
    }

    /// <summary>Hunter's Mark: ranged attacks against the bearer gain +RAP (by rank × Improved Hunter's Mark); it cannot hide.</summary>
    sealed class HunterMark : SpecialHandler
    {
        static readonly float[] Ap = { 20, 45, 75, 110 };
        public HunterMark() : base("HunterMark") { }
        public override float IncomingRangedApBonus(AuraInstance a, Unit attacker) => Ap[MathUtil.Clamp(a.Rank, 1, Ap.Length) - 1] * a.EffectMult;
        public override bool RevealsTo(AuraInstance a, Unit observer) => a.Caster != null && observer != null && observer.Team == a.Caster.Team;
    }

    /// <summary>Bestial Wrath: the pet is immune to and freed from fear, stun, root, incapacitate, sleep, polymorph, confuse, daze and snares.</summary>
    sealed class HunterBestialWrath : SpecialHandler
    {
        static readonly UnitState[] States = { UnitState.Fear, UnitState.Stun, UnitState.Root, UnitState.Incapacitate, UnitState.Sleep, UnitState.Polymorph, UnitState.Confuse, UnitState.Daze };
        public HunterBestialWrath() : base("HunterBestialWrath") { }

        static bool Blocked(AuraDef d) => ImposesAny(d, States) || IsSnare(d);

        public override void OnAuraApplied(Battle b, AuraInstance a) => RemoveWhere(b, a.Bearer, x => x != a && IsHarmful(x) && Blocked(x.Def));
        public override void OnAuraRefreshed(Battle b, AuraInstance a) => OnAuraApplied(b, a);
        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming) =>
            holder != null && Blocked(incoming) ? "Immune" : null;
    }

    /// <summary>Improved Mend Pet: each Mend Pet tick has values[rank]% to cleanse one Curse/Disease/Magic/Poison debuff from the pet.</summary>
    sealed class HunterImprovedMendPet : SpecialHandler
    {
        public HunterImprovedMendPet() : base("HunterImprovedMendPet") { }
        public override void OnChannelTick(AbilityCast c, int rank)
        {
            float ch = PV(rank);
            var pet = c.Caster.Pet;
            if (c.Ability == null || c.Ability.id != "hunter_mend_pet" || pet == null || !pet.IsAlive) return;
            if (!c.Battle.Rng.Chance(ch)) return;
            var list = new List<AuraInstance>();
            foreach (var a in pet.Auras) if (!a.IsPassive && a.Def.kind == AuraKind.Debuff && a.Def.dispel != DispelType.None) list.Add(a);
            if (list.Count == 0) return;
            var pick = list[c.Battle.Rng.Range(0, list.Count - 1)];
            c.Battle.Emit(new CombatEvent { Type = CombatEventType.Dispel, Source = c.Caster, Target = pet, AuraId = pick.Def.id, Name = pick.Def.name, AbilityId = c.AbilityId });
            c.Battle.RemoveAura(pick, AuraRemoveReason.Dispelled);
        }
    }

    /// <summary>Spirit Bond: while the pet is out, hunter and pet heal values[rank] × 0.6% of max health at the start of their turns.</summary>
    sealed class HunterSpiritBond : SpecialHandler
    {
        public HunterSpiritBond() : base("HunterSpiritBond") { }

        static float Pct(Unit hunter) => TalentValue(hunter, "HunterSpiritBond");

        public override void OnAnyTurnStart(Battle b, Unit u)
        {
            Unit hunter = u.Kind == UnitKind.Pet && u.Owner != null ? u.Owner : u;
            if (hunter.Class == null || hunter.Pet == null || !hunter.Pet.IsAlive || !b.Units.Contains(hunter.Pet)) return;
            if (u != hunter && u != hunter.Pet) return;
            float pct = Pct(hunter);
            if (pct <= 0f) return;
            b.HealUnit(u, u, u.MaxHealth * pct * 0.6f / 100f, new HealInfo { Name = "Spirit Bond", NoThreat = true, Periodic = true });
        }

        public override void OnOutOfCombatTick(Battle b, Unit u, float seconds)
        {
            Unit hunter = u.Kind == UnitKind.Pet && u.Owner != null ? u.Owner : u;
            if (hunter.Class == null || hunter.Pet == null || !hunter.Pet.IsAlive) return;
            if (u != hunter && u != hunter.Pet) return;
            float pct = Pct(hunter);
            if (pct <= 0f || u.Health >= u.MaxHealth) return;
            b.HealUnit(u, u, u.MaxHealth * pct / 100f * seconds / 10f, new HealInfo { Name = "Spirit Bond", NoThreat = true, Periodic = true, Silent = true });
        }
    }

    /// <summary>Monster Slaying (Beast/Giant/Dragonkin) and Humanoid Slaying: +values[rank]% damage and crit damage bonus.</summary>
    sealed class HunterCreatureSlaying : SpecialHandler
    {
        public HunterCreatureSlaying() : base("HunterCreatureSlaying") { }

        static bool Applies(string source, Unit t)
        {
            if (t == null) return false;
            var ty = TypeOf(t);
            if (source != null && source.Contains("humanoid")) return ty == CreatureType.Humanoid;
            return ty == CreatureType.Beast || ty == CreatureType.Giant || ty == CreatureType.Dragonkin;
        }

        public override float ModifyOutgoingDamage(Battle b, Unit u, int rank, AbilityCast c, EffectDef e, Unit t, float v)
        {
            string src = Specials.CurrentSource;
            float pct = PV(rank);
            return u.Owner == null && Applies(src, t) ? v * (1f + pct / 100f) : v;
        }

        public override float CritBonusAdd(Unit u, int rank, Unit t)
        {
            string src = Specials.CurrentSource;
            float pct = PV(rank);
            return u.Owner == null && Applies(src, t) ? pct : 0f;
        }
    }

    /// <summary>Improved Scorpid Sting: the sting also lowers Stamina by values[rank]% of its Strength reduction.</summary>
    sealed class HunterImprovedScorpidSting : SpecialHandler
    {
        public HunterImprovedScorpidSting() : base("HunterImprovedScorpidSting") { }
        public override void OnAuraAppliedByMe(Battle b, Unit caster, int rank, AuraInstance inst)
        {
            float pct = PV(rank);
            if (inst.Def.id != "hunter_scorpid_sting" || inst.ModValues.Length == 0 || pct <= 0f) return;
            float str = 0f;
            for (int i = 0; i < inst.Def.mods.Count; i++) if (inst.Def.mods[i].stat == StatId.Strength) { str = inst.ModValues[i]; break; }
            if (str >= 0f) return;
            inst.ExtraMods = new List<StatModDef> { new StatModDef { stat = StatId.Stamina, value = str * pct / 100f } };
            inst.Bearer.InvalidateStats();
        }
    }

    /// <summary>Surefooted: values[rank]% to resist hostile roots and snares.</summary>
    sealed class HunterSurefooted : SpecialHandler
    {
        public HunterSurefooted() : base("HunterSurefooted") { }
        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming)
        {
            if (holder != null) return null;
            float v = PV(rank);
            return IsRootOrSnare(incoming) && caster != null && caster.IsHostileTo(target) && b.Rng.Chance(v) ? "Resist" : null;
        }
    }

    /// <summary>Entrapment: units hit by Immolation/Explosive Trap, or first slowed by Frost Trap, may be rooted (hunter_entrapment).</summary>
    sealed class HunterEntrapment : SpecialHandler
    {
        const string RootAura = "hunter_entrapment";
        public HunterEntrapment() : base("HunterEntrapment") { }

        static void Roll(Battle b, Unit hunter, Unit t)
        {
            if (hunter == null || t == null || !t.IsAlive) return;
            float ch = TalentValue(hunter, "HunterEntrapment");
            if (ch <= 0f || !b.Rng.Chance(ch)) return;
            var def = b.Db.Aura(RootAura);
            if (def != null) b.ApplyAura(hunter, t, def, new AuraApplyInfo { EffLevel = hunter.Level, LearnLevel = 1, Duration = def.duration });
        }

        public override void OnDamageDealt(Battle b, Unit src, int rank, Unit tgt, float amount, DamageInfo info, bool viaMinion)
        {
            if (!viaMinion || info?.Ability == null || src.Owner == null) return;
            string id = info.Ability.id;
            if (id == "hunter_explosive_trap_effect" && !info.Periodic) Roll(b, src.Owner, tgt);
            else if (id == "hunter_immolation_trap_effect" && info.Periodic && info.SourceAura != null && info.SourceAura.GetVar("entrapment") == 0f)
            {
                info.SourceAura.SetVar("entrapment", 1f);
                Roll(b, src.Owner, tgt);
            }
        }

        public override void OnAnyAuraApplied(Battle b, AuraInstance a)
        {
            if (a.Def.id != "hunter_frost_trap" || a.AreaSource == null) return;
            var trap = a.AreaSource.Bearer;
            if (trap == null || trap.Owner == null || a.GetVar("entrapment") > 0f) return;
            a.SetVar("entrapment", 1f);
            Roll(b, trap.Owner, a.Bearer);
        }
    }
}
