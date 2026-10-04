// Paladin specials.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterPaladin()
        {
            Register(new PaladinJudgement());
            Register(new PaladinDoubleVsStunned());
            Register(new PaladinJudgementOfTheCrusader());
            Register(new PaladinJudgementOfJustice());
            Register(new PaladinBlessingOfLight());
            Register(new PaladinBlessingOfSanctuary());
            Register(new PaladinBlessingOfFreedom());
            Register(new PaladinBlessingOfSacrifice());
            Register(new PaladinForbearanceCheck());
            Register(new PaladinLayOnHands());
            Register(new PaladinDivineIntervention());
            Register(new PaladinHolyShock());
            Register(new PaladinConcentrationAura());
            Register(new PaladinPushbackResist());
            Register(new PaladinUnyieldingFaith());
            Register(new PaladinIllumination());
            Register(new PaladinReckoning());
            Register(new PaladinWeaponSpecialization());
            Register(new PaladinEyeForAnEye());
        }
    }

    /// <summary>Judgement: consumes the active seal and casts its hidden judgement (seal rank) on the target.</summary>
    sealed class PaladinJudgement : SpecialHandler
    {
        public PaladinJudgement() : base("PaladinJudgement") { }

        static AuraInstance Seal(Unit u)
        {
            foreach (var a in u.Auras) if (a.Def.exclusiveGroup == "paladin_seal") return a;
            return null;
        }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) => Seal(u) == null ? "Requires an active Seal." : null;

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            var u = c.Caster;
            var seal = Seal(u);
            if (seal == null || t == null || !t.IsAlive) return;
            var judgement = b.Db.Ability(seal.Def.id.Replace("seal_of_", "judgement_of_"));
            int rank = Math.Max(1, seal.Rank);
            b.RemoveAura(seal, AuraRemoveReason.Consumed);
            if (judgement == null) return;
            b.TriggerAbility(u, judgement, t, null, c.Depth + 1, rank);
        }
    }

    /// <summary>Judgement of Command: double damage against stunned targets.</summary>
    sealed class PaladinDoubleVsStunned : SpecialHandler
    {
        public PaladinDoubleVsStunned() : base("PaladinDoubleVsStunned") { }
        public override float ModifyDamage(AbilityCast c, EffectDef e, Unit t, float v) =>
            t != null && t.HasStateAura(UnitState.Stun) ? v * 2f : v;
    }

    /// <summary>Judgement of the Crusader: Holy damage against the bearer gains X × coefficient (X by seal rank).</summary>
    sealed class PaladinJudgementOfTheCrusader : SpecialHandler
    {
        static readonly float[] X = { 23, 35, 58, 92, 127, 140 };
        public PaladinJudgementOfTheCrusader() : base("PaladinJudgementOfTheCrusader") { }

        public override float IncomingFlatDamageBonus(AuraInstance a, AbilityCast c, EffectDef e, School s, bool weapon)
        {
            if (s != School.Holy || e == null) return 0f;
            float coef = weapon ? 0.2f : (e.coef > 0f ? e.coef : 1f);
            return X[MathUtil.Clamp(a.Rank, 1, X.Length) - 1] * a.EffectMult * coef;
        }
    }

    /// <summary>Judgement of Justice: the bearer cannot flee and ignores movement speed bonuses.</summary>
    sealed class PaladinJudgementOfJustice : SpecialHandler
    {
        public PaladinJudgementOfJustice() : base("PaladinJudgementOfJustice") { }
        public override bool PreventsFleeing(AuraInstance a) => true;
        public override void AdjustStats(Unit u, int rank, UnitStats s)
        {
            float bonus = s.Block.Flat(StatId.MoveSpeed);
            if (bonus > 0f) s.MoveSpeedPct -= bonus;
        }
    }

    /// <summary>Blessing of Light: Holy Light +H and Flash of Light +F healing received (before crit).</summary>
    sealed class PaladinBlessingOfLight : SpecialHandler
    {
        static readonly float[] H = { 210, 300, 400 }, F = { 60, 85, 115 };
        public PaladinBlessingOfLight() : base("PaladinBlessingOfLight") { }

        public override float IncomingFlatHealBonus(AuraInstance a, AbilityCast c, EffectDef e)
        {
            if (c.Ability == null) return 0f;
            bool greater = a.Def.id.Contains("greater");
            int i = greater ? 2 : MathUtil.Clamp(a.Rank, 1, 3) - 1;
            if (c.Ability.id == "paladin_holy_light") return H[i] * a.EffectMult;
            if (c.Ability.id == "paladin_flash_of_light") return F[i] * a.EffectMult;
            return 0f;
        }
    }

    /// <summary>Blessing of Sanctuary: every damage instance on the bearer is reduced by a flat amount.</summary>
    sealed class PaladinBlessingOfSanctuary : SpecialHandler
    {
        static readonly float[] R = { 10, 14, 19, 24 };
        public PaladinBlessingOfSanctuary() : base("PaladinBlessingOfSanctuary") { }
        public override float ModifyIncomingDamage(Battle b, AuraInstance a, Unit src, float amount, School school, DamageInfo info)
        {
            float r = a.Def.id.Contains("greater") ? 24f : R[MathUtil.Clamp(a.Rank, 1, R.Length) - 1];
            return Math.Max(0f, amount - r);
        }
    }

    /// <summary>Blessing of Freedom: removes roots and snares; while active they have no movement effect.</summary>
    sealed class PaladinBlessingOfFreedom : SpecialHandler
    {
        public PaladinBlessingOfFreedom() : base("PaladinBlessingOfFreedom") { }
        public override void OnAuraApplied(Battle b, AuraInstance a) => RemoveWhere(b, a.Bearer, x => IsHarmful(x) && IsRootOrSnare(x.Def));
        public override void OnAuraRefreshed(Battle b, AuraInstance a) => OnAuraApplied(b, a);
        public override bool IgnoresState(Unit u, int rank, UnitState s) => s == UnitState.Root;
        public override void AdjustStats(Unit u, int rank, UnitStats s)
        {
            float bonus = Math.Max(0f, s.Block.Flat(StatId.MoveSpeed));
            if (s.MoveSpeedPct < bonus) s.MoveSpeedPct = bonus;
        }
    }

    /// <summary>Blessing of Sacrifice: up to T of each damage instance is taken by the paladin instead.</summary>
    sealed class PaladinBlessingOfSacrifice : SpecialHandler
    {
        static readonly float[] T = { 30, 40 };
        public PaladinBlessingOfSacrifice() : base("PaladinBlessingOfSacrifice") { }
        public override float ModifyIncomingDamage(Battle b, AuraInstance a, Unit src, float amount, School school, DamageInfo info)
        {
            var pal = a.Caster;
            if (pal == null || pal == a.Bearer || amount <= 0f) return amount;
            if (!pal.IsAlive || !b.Units.Contains(pal))
            {
                b.RemoveAura(a, AuraRemoveReason.SourceGone);
                return amount;
            }
            float take = Math.Min(amount, T[MathUtil.Clamp(a.Rank, 1, T.Length) - 1]);
            b.DealDamage(src, pal, take, school, new DamageInfo { Name = a.Def.name, IgnoreModifiers = true, Redirected = true, NoThreat = true, NoProcs = true });
            return amount - take;
        }
    }

    /// <summary>Blessing of Protection / Divine Protection / Divine Shield: not on a target with Forbearance.</summary>
    sealed class PaladinForbearanceCheck : SpecialHandler
    {
        public PaladinForbearanceCheck() : base("PaladinForbearanceCheck") { }
        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            var t = a.target == TargetType.Self ? u : target;
            return t != null && t.HasAura("paladin_forbearance") ? (t == u ? "You have Forbearance." : "Target has Forbearance.") : null;
        }
    }

    /// <summary>Lay on Hands: heals for the paladin's maximum health; higher ranks also restore mana. Needs some mana.</summary>
    sealed class PaladinLayOnHands : SpecialHandler
    {
        public PaladinLayOnHands() : base("PaladinLayOnHands") { }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) => u.Mana <= 0f ? "Requires mana." : null;

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            if (t == null || !t.IsAlive) return;
            c.Battle.HealUnit(u, t, u.MaxHealth, new HealInfo { Ability = c.Ability, Name = c.Name, Mods = c.Mods, Cast = c });
            float mana = c.Rank >= 3 ? 550f : c.Rank == 2 ? 250f : 0f;
            if (mana > 0f && t.MaxMana > 0f) c.Battle.ChangeResource(t, ResourceType.Mana, mana, u);
        }
    }

    /// <summary>Divine Intervention: the target is sealed away (aura), enemies forget it, and the paladin dies outright.</summary>
    sealed class PaladinDivineIntervention : SpecialHandler
    {
        const string AuraId = "paladin_divine_intervention";
        public PaladinDivineIntervention() : base("PaladinDivineIntervention") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            if (t == null || !t.HasAura(AuraId)) return;
            foreach (var o in b.Units)
            {
                o.Threat.Remove(t);
                if (o.AggroTarget == t) o.AggroTarget = null;
                if (o.AttackTarget == t && o.IsHostileTo(t)) o.AttackTarget = null;
            }
            if (b.InCombat) b.KillUnit(c.Caster, null, true);
        }

        public override void OnBattleFinished(Battle b)
        {
            foreach (var u in b.Units)
            {
                var a = u.FindAura(AuraId);
                if (a != null) b.RemoveAura(a, AuraRemoveReason.Cancelled);
            }
        }
    }

    /// <summary>Holy Shock: damage (effects[0]) on enemies, heal (effects[1]) on friends.</summary>
    sealed class PaladinHolyShock : SpecialHandler
    {
        public PaladinHolyShock() : base("PaladinHolyShock") { }
        public override void Resolve(AbilityCast c)
        {
            var a = c.Ability;
            if (a.effects.Count < 2 || c.Target == null) return;
            c.SkipEffects = true;
            bool hostile = c.Target.IsHostileTo(c.Caster);
            c.Battle.ExecuteEffects(c, new List<EffectDef> { a.effects[hostile ? 0 : 1] });
        }
    }

    /// <summary>Concentration Aura: pushback −35% (+Improved Concentration Aura), which also resists silence/interrupts.</summary>
    sealed class PaladinConcentrationAura : SpecialHandler
    {
        const string AuraId = "paladin_concentration_aura_effect";
        public PaladinConcentrationAura() : base("PaladinConcentrationAura") { }

        static float Improved(Unit u)
        {
            var a = u.FindAura(AuraId);
            return a != null ? Math.Max(0f, a.EffectMult - 1f) : 0f;
        }

        public override float PushbackReduction(Unit u, int rank) => Math.Min(1f, 0.35f + Improved(u));
        public override float InterruptResistChance(Unit u, int rank) => Improved(u) * 100f;
        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming)
        {
            if (holder == null || !Imposes(incoming, UnitState.Silence)) return null;
            float ch = Math.Max(0f, holder.EffectMult - 1f) * 100f;
            return ch > 0f && b.Rng.Chance(ch) ? "Resist" : null;
        }
    }

    /// <summary>Spiritual Focus: values[rank]% chance for Holy Light / Flash of Light to ignore pushback.</summary>
    sealed class PaladinPushbackResist : SpecialHandler
    {
        public PaladinPushbackResist() : base("PaladinPushbackResist") { }
        public override float PushbackResistChance(Unit u, int rank, AbilityDef pending)
        {
            float v = PV(rank);
            return pending.id == "paladin_holy_light" || pending.id == "paladin_flash_of_light" ? v : 0f;
        }
    }

    /// <summary>Unyielding Faith: values[rank]% to resist fear and disorient (confuse).</summary>
    sealed class PaladinUnyieldingFaith : SpecialHandler
    {
        public PaladinUnyieldingFaith() : base("PaladinUnyieldingFaith") { }
        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming)
        {
            if (holder != null) return null;
            float v = PV(rank);
            return ImposesAny(incoming, UnitState.Fear, UnitState.Confuse) && b.Rng.Chance(v) ? "Resist" : null;
        }
    }

    /// <summary>Illumination: a healing crit refunds the base mana cost of the rank used.</summary>
    sealed class PaladinIllumination : SpecialHandler
    {
        public PaladinIllumination() : base("PaladinIllumination") { }
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var info = c.ProcInfo;
            var u = c.Caster;
            var a = info?.Ability;
            if (a == null || a.cost == null || a.cost.type != ResourceType.Mana) return;
            float gain = BaseCost(u, a, AbilityRules.UsedRank(u, a));
            if (gain > 0f) c.Battle.ChangeResource(u, ResourceType.Mana, gain, u);
        }
    }

    /// <summary>Reckoning: stores an extra attack (max 4) used at the end of the paladin's next turn.</summary>
    sealed class PaladinReckoning : SpecialHandler
    {
        public PaladinReckoning() : base("PaladinReckoning") { }
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            u.ExtraAttacks = Math.Min(4, u.ExtraAttacks + 1);
        }
    }

    /// <summary>One-/Two-Handed Weapon Specialization: +values[rank]% damage with that kind of main-hand melee weapon.</summary>
    sealed class PaladinWeaponSpecialization : SpecialHandler
    {
        public PaladinWeaponSpecialization() : base("PaladinWeaponSpecialization") { }
        public override float ModifyOutgoingDamage(Battle b, Unit u, int rank, AbilityCast c, EffectDef e, Unit t, float v)
        {
            string src = Specials.CurrentSource ?? "";
            float pct = PV(rank);
            if (u.Owner != null) return v;
            var mh = u.Equipment.MainHand;
            if (mh == null || !mh.IsWeapon) return v;
            bool two = mh.Def.equip == EquipType.TwoHand;
            bool wantTwo = src.Contains("two_handed");
            return two == wantTwo ? v * (1f + pct / 100f) : v;
        }
    }

    /// <summary>Eye for an Eye: spell crits taken reflect 15% per rank of the damage as Holy (max 50% of max health).</summary>
    sealed class PaladinEyeForAnEye : SpecialHandler
    {
        public PaladinEyeForAnEye() : base("PaladinEyeForAnEye") { }
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var info = c.ProcInfo;
            var u = c.Caster;
            if (info == null || info.School == School.Physical || info.Damage <= 0f || t == null || t == u || !t.IsAlive) return;
            float dmg = Math.Min(info.Damage * 0.15f * Math.Max(1, c.Rank), u.MaxHealth * 0.5f);
            c.Battle.DealDamage(u, t, dmg, School.Holy, new DamageInfo { Name = "Eye for an Eye", Kind = AttackKind.Spell, IgnoreModifiers = true, NoProcs = true });
        }
    }
}
