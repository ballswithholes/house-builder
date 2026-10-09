// Shaman specials.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterShaman()
        {
            Register(new ShamanWindfuryAttack());
            Register(new ShamanFlatPhysicalReduction("ShamanStoneskin", ranged: false));
            Register(new ShamanFlatPhysicalReduction("ShamanWindwall", ranged: true));
            Register(new ShamanEarthsGrasp());
            Register(new ShamanGroundingTotem());
            Register(new ShamanReincarnation());
            Register(new NextCastInstant("ShamanNaturesSwiftness", a => a.school == School.Nature));
            Register(new ShamanTwoHandedWeapons());
            Register(new ShamanPushbackResist());
            Register(new NextSpellBonus("ShamanNextDamageSpell", free: true, crit: true));   // Elemental Mastery
            Register(new NextSpellBonus("ShamanClearcasting", free: true, crit: false));     // Elemental Focus' Clearcasting
        }
    }

    /// <summary>
    /// Windfury (weapon: 2 swings, totem: 1): immediate extra main-hand swings at the unit that was hit, with bonus
    /// attack power (source rank × the aura's EffectMult). Extra swings never trigger Windfury again; one proc per hit.
    /// </summary>
    sealed class ShamanWindfuryAttack : SpecialHandler
    {
        bool active;
        object lastTrigger;
        public ShamanWindfuryAttack() : base("ShamanWindfuryAttack") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            if (active) return;
            var trigger = (object)c.ProcInfo ?? c;
            if (ReferenceEquals(trigger, lastTrigger)) return;
            lastTrigger = trigger;
            var u = c.Caster;
            var target = c.ProcOther;
            if (target == null || !target.IsAlive || !u.IsAlive) return;
            float ap = Magnitude(c, e) * (c.ProcAura != null ? c.ProcAura.EffectMult : 1f);
            int n = Math.Max(1, e.count);
            active = true;
            try
            {
                for (int i = 0; i < n && target.IsAlive && u.IsAlive; i++)
                    c.Battle.ExtraSwing(u, target, ap, c.SourceProc);
            }
            finally { active = false; }
        }
    }

    /// <summary>
    /// Stoneskin (melee) / Windwall (ranged): not a depleting shield — every incoming physical hit of that kind is
    /// reduced by the aura's absorb amount (scaled by level and EffectMult), never below 0.
    /// </summary>
    sealed class ShamanFlatPhysicalReduction : SpecialHandler
    {
        readonly bool ranged;
        public ShamanFlatPhysicalReduction(string name, bool ranged) : base(name) { this.ranged = ranged; }

        public override bool SkipAbsorbPool(AuraInstance a) => true;

        public override float ModifyIncomingDamage(Battle b, AuraInstance a, Unit src, float amount, School school, DamageInfo info)
        {
            if (school != School.Physical || info == null || info.Periodic || a.Def.absorb == null) return amount;
            bool isRanged = info.Ranged || info.Kind == AttackKind.Ranged || info.Kind == AttackKind.Wand;
            bool isMelee = info.Kind == AttackKind.Melee && !info.Ranged;
            if (ranged ? !isRanged : !isMelee) return amount;
            var ab = a.Def.absorb;
            float r = (ab.amount + ab.perLevel * Math.Max(0, a.EffLevel - a.LearnLevel)) * a.EffectMult;
            return Math.Max(0f, amount - r);
        }
    }

    /// <summary>Earth's Grasp: Stoneclaw Totems get +values[rank]% maximum health.</summary>
    sealed class ShamanEarthsGrasp : SpecialHandler
    {
        public ShamanEarthsGrasp() : base("ShamanEarthsGrasp") { }
        public override void OnOwnerSummoned(Battle b, Unit owner, int rank, Unit summoned)
        {
            float pct = PV(rank);
            if (summoned?.Creature == null || summoned.Creature.id != "shaman_totem_stoneclaw" || pct <= 0f) return;
            summoned.MaxHealthMult = 1f + pct / 100f;
            summoned.InvalidateStats();
            summoned.Health = summoned.MaxHealth;
        }
    }

    /// <summary>
    /// Grounding Totem: redirects the first hostile single-target spell aimed at a party member within 20 yards to the
    /// totem; then inactive for 10 s (two rounds).
    /// </summary>
    sealed class ShamanGroundingTotem : SpecialHandler
    {
        public ShamanGroundingTotem() : base("ShamanGroundingTotem") { }

        public override Unit RedirectSpell(Battle b, AuraInstance a, AbilityCast c)
        {
            var totem = a.Bearer;
            var t = c.Target;
            if (totem == null || !totem.IsAlive || t == null || t == totem || t.IsTotem) return null;
            if (!c.Caster.IsHostileTo(totem) || t.Team != totem.Team) return null;
            if (totem.DistanceTo(t) > MathUtil.Yd(20f) + t.Radius) return null;
            if (a.Vars != null && a.Vars.ContainsKey("ready") && b.Round < (int)a.GetVar("ready")) return null;
            a.SetVar("ready", b.Round + 2);
            return totem;
        }
    }

    /// <summary>
    /// Reincarnation: when downed with the ability off cooldown and an Ankh in the bags, the shaman is offered to rise
    /// (20% × Improved Reincarnation of health and mana) at her next turn slot; accepting uses the Ankh and the cooldown.
    /// </summary>
    sealed class ShamanReincarnation : SpecialHandler
    {
        const string AbilityId = "shaman_reincarnation", Ankh = "shaman_ankh";
        public ShamanReincarnation() : base("ShamanReincarnation") { }

        public override void OnHolderDowned(Battle b, AuraInstance a)
        {
            var u = a.Bearer;
            if (!b.InCombat || u.SelfRes != null) return;
            var ab = b.Db.Ability(AbilityId);
            if (ab == null || u.CooldownLeft(ab) > 0f || !HasReagent(b, u, Ankh)) return;
            var mods = AbilityMods.For(u, ab);
            float pct = 0.2f * mods.EffectMult;
            u.SelfRes = new SelfResOffer
            {
                Source = AbilityId, Name = ab.name, Health = u.MaxHealth * pct, Mana = u.MaxMana * pct,
                OnAccept = (battle, unit) =>
                {
                    ConsumeReagent(battle, unit, Ankh);
                    battle.StartCooldown(unit, ab, AbilityMods.For(unit, ab));
                },
            };
        }
    }

    /// <summary>Two-Handed Axes and Maces: proficiency with two-handed axes and maces.</summary>
    sealed class ShamanTwoHandedWeapons : SpecialHandler
    {
        public ShamanTwoHandedWeapons() : base("ShamanTwoHandedWeapons") { }
        public override bool GrantsWeapon(Unit u, int rank, WeaponType w) => w == WeaponType.TwoHandAxe || w == WeaponType.TwoHandMace;
    }

    /// <summary>Healing Focus (talent: heals ignore pushback values[rank]%) and Focused Casting (aura: all casts ignore pushback).</summary>
    sealed class ShamanPushbackResist : SpecialHandler
    {
        public ShamanPushbackResist() : base("ShamanPushbackResist") { }
        public override float PushbackResistChance(Unit u, int rank, AbilityDef pending)
        {
            var p = Specials.CurrentPassive;
            if (p == null) return 100f; // shaman_focused_casting aura
            float v = Specials.RankValue(p, rank);
            return PriestPushbackResist.IsHeal(pending) ? v : 0f;
        }
    }

    /// <summary>
    /// "Your next X spell" buffs (Elemental Mastery, shaman Clearcasting, Divine Favor) whose bonus applies only to the
    /// spells that consume them. The consuming spells are the abilities selected by the aura's own consumeCharge
    /// OnSpellCast procs (their abilities/tags/schools filters), so the bonus and the charge always go together: a
    /// matching spell costs no mana (<c>free</c>) and/or every non-periodic effect of it is a guaranteed critical
    /// (<c>crit</c>, +100 points); other spells (heals, totems, Exorcism...) neither benefit nor consume the charge.
    /// The bonus lasts while the aura is up, i.e. it also covers a cast that went pending; the data proc removes the
    /// aura once the consuming cast has resolved (after its cost and crit rolls).
    /// </summary>
    sealed class NextSpellBonus : SpecialHandler
    {
        readonly bool free, crit;
        public NextSpellBonus(string name, bool free, bool crit) : base(name) { this.free = free; this.crit = crit; }

        /// <summary>The aura instance serving the running passive hook (aura passives publish their aura id as CurrentSource).</summary>
        static AuraInstance Serving(Unit u)
        {
            var id = Specials.CurrentSource;
            return u != null && id != null ? u.FindAura(id) : null;
        }

        /// <summary>True when a use of <paramref name="a"/> consumes the aura (one of its consumeCharge OnSpellCast procs matches).</summary>
        internal static bool Consumes(AuraDef d, AbilityDef a)
        {
            if (d == null || a == null || !AbilityRules.IsSpell(a) || a.special == "Shoot") return false;
            foreach (var p in d.procs)
                if (p.consumeCharge && p.trigger == ProcTrigger.OnSpellCast && Battle.ProcMatchesAbility(p, a)) return true;
            return false;
        }

        public override float ModifyCost(Unit u, int rank, AbilityDef a, float cost)
        {
            if (!free || cost <= 0f || a?.cost == null || a.cost.type != ResourceType.Mana) return cost;
            var aura = Serving(u);
            return aura != null && Consumes(aura.Def, a) ? 0f : cost;
        }

        public override float ModifyCritChance(Battle b, Unit u, int rank, AbilityCast c, Unit t, School s, float chance)
        {
            if (!crit || c == null || c.Periodic || c.SourceAura != null || c.SourceProc != null || c.Depth > 0) return chance;
            var aura = Serving(u);
            return aura != null && Consumes(aura.Def, c.Ability) ? chance + 100f : chance;
        }
    }
}
