// Warrior specials and the generic specials shared by warriors and rogues
// (RankDurations, SweepingHits, OffHandDamage, PhysicalRangedShot, ImmunityFromTags).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static partial void RegisterClassSpecials()
        {
            RegisterWarrior();
            RegisterRogue();
            RegisterMage();
            RegisterPriest();
            RegisterPaladin();
            RegisterHunter();
            RegisterShaman();
            RegisterWarlock();
            RegisterContent();
        }

        static void RegisterWarrior()
        {
            Register(new WarriorStanceSwap());
            Register(new WarriorCharge());
            Register(new WarriorOverpower());
            Register(new WarriorExecute());
            Register(new WarriorDefiance());
            Register(new WarriorImprovedCleave());
            Register(new WarriorIronWill());
            Register(new WarriorBerserkerRage());
            Register(new ImmunityFromTags());
            Register(new RankDurations());
            Register(new SweepingHits());
            Register(new OffHandDamage());
            Register(new PhysicalRangedShot());
        }
    }

    // ============================================================== warrior

    /// <summary>Stance change: unusable in the current stance; rage is cut to Tactical Mastery's 5 × rank.</summary>
    sealed class WarriorStanceSwap : SpecialHandler
    {
        public WarriorStanceSwap() : base("WarriorStanceSwap") { }

        static string StanceAura(AbilityDef a)
        {
            foreach (var e in a.effects) if (e.type == EffectType.ApplyAura && e.target == EffectTarget.Self) return e.aura;
            return null;
        }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            var aura = StanceAura(a);
            return aura != null && u.HasAura(aura) ? "You are already in that stance." : null;
        }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            float keep = TalentValue(u, Name);
            if (u.Rage > keep) c.Battle.ChangeResource(u, ResourceType.Rage, keep - u.Rage, u);
        }
    }

    /// <summary>Charge only while the warrior has not yet fought in this battle (or out of combat as an opener).</summary>
    sealed class WarriorCharge : SpecialHandler
    {
        public WarriorCharge() : base("WarriorCharge") { }
        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) =>
            b.InCombat && b.Started && u.Engaged ? "You are already in combat." : null;
    }

    /// <summary>Overpower cannot be dodged, parried or blocked.</summary>
    sealed class WarriorOverpower : SpecialHandler
    {
        public WarriorOverpower() : base("WarriorOverpower") { }
        public override bool Unavoidable(AbilityCast c, EffectDef e) => true;
    }

    /// <summary>Execute: every extra rage point drained by consumeAll adds 3 × rank damage.</summary>
    sealed class WarriorExecute : SpecialHandler
    {
        public WarriorExecute() : base("WarriorExecute") { }

        public override float ModifyDamage(AbilityCast c, EffectDef e, Unit t, float v)
        {
            if (e == null || e.type != EffectType.Damage || c.Periodic || c.ExtraResource <= 0f) return v;
            return v + c.ExtraResource * 3f * c.Rank * c.MagnitudeScale * c.Mods.DamageMult;
        }
    }

    /// <summary>Defiance: +3% threat per rank while in Defensive Stance.</summary>
    sealed class WarriorDefiance : SpecialHandler
    {
        public WarriorDefiance() : base("WarriorDefiance") { }
        public override void ContributeStats(Unit u, int rank, StatBlock block)
        {
            float v = PV(rank);
            if (v != 0f && u.HasAura("warrior_defensive_stance_aura")) block.Add(StatId.ThreatGenerated, v, true);
        }
    }

    /// <summary>Improved Cleave: +40% per rank to Cleave's flat bonus damage.</summary>
    sealed class WarriorImprovedCleave : SpecialHandler
    {
        public WarriorImprovedCleave() : base("WarriorImprovedCleave") { }
        public override float WeaponFlatBonusMult(AbilityCast c, int rank, EffectDef e)
        {
            float v = PV(rank);
            return c.Ability != null && c.Ability.id == "warrior_cleave" ? 1f + v / 100f : 1f;
        }
    }

    /// <summary>Iron Will: 3% per rank to resist stuns and charms.</summary>
    sealed class WarriorIronWill : SpecialHandler
    {
        public WarriorIronWill() : base("WarriorIronWill") { }
        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming)
        {
            if (holder != null) return null;
            float chance = PV(rank);
            bool charm = HasTag(incoming, "MindControl") || HasTag(incoming, "Charm");
            if (!(Imposes(incoming, UnitState.Stun) || charm)) return null;
            return b.Rng.Chance(chance) ? "Resist" : null;
        }
    }

    /// <summary>
    /// Immunity to the unit states named by the aura's tags (ImmuneFear, ImmuneIncapacitate (+Sleep), ImmuneStun,
    /// ImmuneRoot): existing such auras are removed on application, new ones are not applied (Immune).
    /// </summary>
    class ImmunityFromTags : SpecialHandler
    {
        public ImmunityFromTags() : base("ImmunityFromTags") { }
        protected ImmunityFromTags(string name) : base(name) { }

        protected virtual List<UnitState> States(AuraInstance a)
        {
            var l = new List<UnitState>();
            foreach (var t in a.Def.tags)
            {
                switch (t)
                {
                    case "ImmuneFear": l.Add(UnitState.Fear); break;
                    case "ImmuneIncapacitate": l.Add(UnitState.Incapacitate); l.Add(UnitState.Sleep); break;
                    case "ImmuneStun": l.Add(UnitState.Stun); break;
                    case "ImmuneRoot": l.Add(UnitState.Root); break;
                }
            }
            return l;
        }

        static bool Blocks(List<UnitState> states, AuraDef d)
        {
            foreach (var s in states) if (Imposes(d, s)) return true;
            return false;
        }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            var states = States(a);
            if (states.Count == 0) return;
            RemoveWhere(b, a.Bearer, x => x != a && IsHarmful(x) && Blocks(states, x.Def));
        }

        public override void OnAuraRefreshed(Battle b, AuraInstance a) => OnAuraApplied(b, a);

        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming)
        {
            if (holder == null) return null;
            return Blocks(States(holder), incoming) ? "Immune" : null;
        }
    }

    /// <summary>Berserker Rage: immune to (and freed from) Fear, Incapacitate and Sleep; double rage from damage taken.</summary>
    sealed class WarriorBerserkerRage : ImmunityFromTags
    {
        static readonly List<UnitState> states = new List<UnitState> { UnitState.Fear, UnitState.Incapacitate, UnitState.Sleep };
        public WarriorBerserkerRage() : base("WarriorBerserkerRage") { }
        protected override List<UnitState> States(AuraInstance a) => states;
        public override float RageFromDamageTakenMult(Unit u, int rank) => 2f;
    }

    // ============================================================== generic

    /// <summary>Aura duration per rank of the applying ability, from the aura tag "Durations:a/b/c".</summary>
    sealed class RankDurations : SpecialHandler
    {
        public RankDurations() : base("RankDurations") { }

        static float[] Durations(AuraDef d)
        {
            foreach (var t in d.tags)
            {
                if (!t.StartsWith("Durations:", StringComparison.Ordinal)) continue;
                var parts = t.Substring(10).Split('/');
                var list = new List<float>();
                foreach (var p in parts)
                    if (float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) list.Add(v);
                return list.ToArray();
            }
            return null;
        }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            var list = Durations(a.Def);
            if (list == null || list.Length == 0 || a.Duration <= 0f) return;
            float baseRank = list[MathUtil.Clamp(a.Rank, 1, list.Length) - 1];
            float delta = baseRank - a.Def.duration;
            if (Math.Abs(delta) < 1e-4f) return;
            // the applied duration already holds (base + perCombo + mods.Duration) × (1 + DurationPct): swap the base
            float pct = 1f;
            if (a.SourceAbility != null && a.Caster != null) pct = 1f + AbilityMods.For(a.Caster, a.SourceAbility).DurationPct / 100f;
            float d = Math.Max(0.1f, a.Duration + delta * pct);
            float elapsed = a.Duration - a.Remaining;
            a.Duration = d;
            a.Remaining = Math.Max(0.1f, d - elapsed);
        }

        public override void OnAuraRefreshed(Battle b, AuraInstance a) => OnAuraApplied(b, a);
    }

    /// <summary>
    /// Sweeping Strikes / Blade Flurry: each single-target melee hit of the bearer is copied (same final damage, no
    /// roll, no procs) to the nearest other enemy within 8 yards of the target. Charges are consumed per copy.
    /// </summary>
    sealed class SweepingHits : SpecialHandler
    {
        public SweepingHits() : base("SweepingHits") { }

        public override void OnDamageDealt(Battle b, Unit src, int rank, Unit tgt, float amount, DamageInfo info, bool viaMinion)
        {
            if (viaMinion || amount <= 0f || info == null || info.Redirected || info.Periodic) return;
            if (info.Kind != AttackKind.Melee || info.Ranged || info.School != School.Physical) return;
            var c = info.Cast;
            if (c != null)
            {
                if (c.AreaUnits != null || c.SourceAura != null || c.Periodic) return;
                if (c.SourceProc != null && !info.ExtraAttack) return;
            }
            AuraInstance aura = null;
            foreach (var a in src.Auras) if (a.Def.special == Name) { aura = a; break; }
            if (aura == null) return;
            var other = NearestEnemy(b, src, tgt.Position, MathUtil.Yd(8f), tgt);
            if (other == null) return;
            b.DealDamage(src, other, amount, School.Physical, new DamageInfo
            {
                Ability = info.Ability, Name = aura.Def.name, Kind = AttackKind.Melee, IgnoreModifiers = true, NoProcs = true, Redirected = true,
            });
            if (aura.Charges > 0 && src.Auras.Contains(aura)) b.ConsumeCharge(aura);
        }
    }

    /// <summary>Dual Wield Specialization: off-hand damage × (1 + value × rank %).</summary>
    sealed class OffHandDamage : SpecialHandler
    {
        public OffHandDamage() : base("OffHandDamage") { }
        public override float OffHandMultiplier(Unit u, int rank) => 1f + PV(rank) / 100f;
    }

    /// <summary>Warrior/rogue Shoot/Throw: Time = ranged weapon speed; needs the right ranged weapon; never starts Auto Shot.</summary>
    sealed class PhysicalRangedShot : SpecialHandler
    {
        public PhysicalRangedShot() : base("PhysicalRangedShot") { }

        public override float? TimeCost(Unit u, AbilityDef a)
        {
            var w = StatCalculator.GetWeapon(u, WeaponSlot.Ranged);
            return w.Valid ? w.Speed / Math.Max(0.1f, u.Stats.RangedHaste) : 2f;
        }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            if (u.Class == null) return null;
            var r = u.Equipment.Ranged;
            var wt = r != null && r.IsWeapon ? r.Def.weaponType : WeaponType.None;
            if (AbilityMods.HasTag(a, "Thrown") && wt != WeaponType.Thrown) return "Requires a thrown weapon.";
            if (AbilityMods.HasTag(a, "Shoot") && wt != WeaponType.Bow && wt != WeaponType.Gun && wt != WeaponType.Crossbow)
                return "Requires a bow, gun or crossbow.";
            return null;
        }
    }
}
