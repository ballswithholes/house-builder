// Mage specials.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterMage()
        {
            Register(new MageBlinkFreedom());
            Register(new NextSpellBonus("MageClearcasting", free: true, crit: false));   // Arcane Concentration's Clearcasting
            Register(new OneTargetPerCaster("MageOnePolymorph"));
            Register(new MageEvocation());
            Register(new MageFlatMagicModifier());
            Register(new NextCastInstant("MagePresenceOfMind", a => a.classId == ClassId.Mage));
            Register(new MageIgnite());
            Register(new MageShatter());
            Register(new MageWintersChill());
            Register(new MageMasterOfElements());
            Register(new MageWardReflect());
            Register(new MageMagicAbsorption());
            Register(new MageArcaneResilience());
            Register(new MageImprovedManaShield());
            Register(new MagePushbackResist());
        }
    }

    /// <summary>Blink frees the caster from stuns and roots (not banish/invulnerability effects).</summary>
    sealed class MageBlinkFreedom : SpecialHandler
    {
        public MageBlinkFreedom() : base("MageBlinkFreedom") { }
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            RemoveWhere(c.Battle, c.Caster, a => !a.IsPassive && ImposesAny(a.Def, UnitState.Stun, UnitState.Root)
                && !ImposesAny(a.Def, UnitState.Invulnerable, UnitState.Banish));
        }
    }

    /// <summary>Only one target per caster: applying the aura removes the same aura the caster put on another unit.</summary>
    sealed class OneTargetPerCaster : SpecialHandler
    {
        public OneTargetPerCaster(string name) : base(name) { }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            if (a.Caster == null) return;
            foreach (var u in new List<Unit>(b.Units))
            {
                if (u == a.Bearer) continue;
                var other = u.FindAura(a.Def.id, a.Caster);
                if (other != null) b.RemoveAura(other, AuraRemoveReason.Replaced);
            }
        }
    }

    /// <summary>Evocation tick: 16 × the class Spirit regeneration per tick, ignoring the five-second rule.</summary>
    sealed class MageEvocation : SpecialHandler
    {
        public MageEvocation() : base("MageEvocation") { }
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            var cls = u.Class;
            float baseRegen = cls != null ? cls.manaRegenBase + u.Stats.Spirit * cls.manaRegenPerSpirit : 15f + u.Stats.Spirit * 0.2f;
            c.Battle.ChangeResource(u, ResourceType.Mana, 16f * baseRegen, u);
        }
    }

    /// <summary>Dampen/Amplify Magic: flat change to incoming non-physical damage and to incoming heals.</summary>
    sealed class MageFlatMagicModifier : SpecialHandler
    {
        static readonly float[] DampenD = { 10, 20, 40, 60, 90 }, DampenH = { 11, 22, 44, 66, 99 };
        static readonly float[] AmplifyD = { 15, 30, 50, 75 }, AmplifyH = { 16, 33, 55, 83 };

        public MageFlatMagicModifier() : base("MageFlatMagicModifier") { }

        static float Pick(float[] v, int rank) => v[MathUtil.Clamp(rank, 1, v.Length) - 1];
        static bool Amplify(AuraInstance a) => a.Def.id.Contains("amplify");

        public override float ModifyIncomingDamage(Battle b, AuraInstance a, Unit src, float amount, School school, DamageInfo info)
        {
            if (school == School.Physical) return amount;
            float d = Pick(Amplify(a) ? AmplifyD : DampenD, a.Rank) * a.EffectMult;
            return Math.Max(0f, Amplify(a) ? amount + d : amount - d);
        }

        public override float ModifyIncomingHeal(Battle b, AuraInstance a, Unit src, float amount, HealInfo info)
        {
            if (info != null && info.Silent) return amount; // regeneration
            float h = Pick(Amplify(a) ? AmplifyH : DampenH, a.Rank) * a.EffectMult;
            return Math.Max(0f, Amplify(a) ? amount + h : amount - h);
        }
    }

    /// <summary>
    /// "Next cast is instant" buffs (Presence of Mind, Nature's Swiftness, Shadow Trance): the next matching
    /// non-channelled ability with 0 &lt; cast time &lt; 10 s has no cast time; the aura is consumed when that cast starts.
    /// </summary>
    sealed class NextCastInstant : SpecialHandler
    {
        readonly Func<AbilityDef, bool> filter;
        public NextCastInstant(string name, Func<AbilityDef, bool> filter) : base(name) { this.filter = filter; }

        bool Matches(Unit u, AbilityDef a, AbilityModSet mods)
        {
            if (a == null || a.channeled || a.castTime <= 0f || !filter(a)) return false;
            float raw = Math.Max(0f, a.castTime + (mods != null ? mods.CastTime : 0f));
            return raw > 0f && raw < 10f;
        }

        public override float ModifyCastTime(Unit u, int rank, AbilityDef a, float t) => Matches(u, a, null) && t > 0f ? 0f : t;

        public override void OnAbilityStart(Battle b, Unit u, int rank, AbilityCast c, float castTime)
        {
            if (!Matches(u, c.Ability, c.Mods)) return;
            foreach (var a in new List<AuraInstance>(u.Auras))
                if (a.Def.special == Name) { b.RemoveAura(a, AuraRemoveReason.Consumed); break; }
        }
    }

    /// <summary>
    /// Ignite: Fire crits add P% of the crit's damage to a pool on the target (rolling, max 5 stacks); each of the
    /// aura's ticks deals pool / remaining ticks as Fire damage (no crit, no spell power), credited to the mage.
    /// </summary>
    sealed class MageIgnite : SpecialHandler
    {
        const string AuraId = "mage_ignite";
        public MageIgnite() : base("MageIgnite") { }

        public override void OnEffectCrit(AbilityCast c, EffectDef e, Unit t, float amount, bool heal, int rank)
        {
            float pct = PV(rank);
            if (heal || pct <= 0f || t == null || !t.IsAlive || c.Periodic || c.SourceAura != null || c.Ability == null) return;
            if (e == null || e.type != EffectType.Damage || (e.school ?? c.School) != School.Fire) return;
            if (c.Caster.Owner != null) return;
            var b = c.Battle;
            var def = b.Db.Aura(AuraId);
            if (def == null) return;
            var inst = b.ApplyAura(c.Caster, t, def, new AuraApplyInfo { EffLevel = c.Caster.Level, LearnLevel = 1, Duration = def.duration });
            if (inst == null) return;
            inst.SetVar("pool", inst.GetVar("pool") + amount * pct / 100f);
            inst.SetVar("ticks", Math.Max(1f, (float)Math.Round(def.duration / Math.Max(0.1f, def.tickInterval))));
        }

        public override bool ReplaceEffect(AbilityCast c, EffectDef e, Unit t)
        {
            var a = c.SourceAura;
            if (a == null || a.Def.id != AuraId || t == null) return true;
            float ticks = Math.Max(1f, a.GetVar("ticks", 1f));
            float pool = a.GetVar("pool");
            float dmg = pool / ticks;
            a.SetVar("pool", pool - dmg);
            a.SetVar("ticks", Math.Max(1f, ticks - 1f));
            if (dmg >= 0.5f)
                c.Battle.DealDamage(c.Caster, t, dmg, School.Fire, new DamageInfo { Name = a.Def.name, Periodic = true, Kind = AttackKind.Spell, SourceAura = a, Cast = c });
            return true;
        }
    }

    /// <summary>Shatter: +values[rank]% crit for damage effects against Frozen targets (checked before the hit breaks the freeze).</summary>
    sealed class MageShatter : SpecialHandler
    {
        public MageShatter() : base("MageShatter") { }
        public override float ModifyCritChance(Battle b, Unit u, int rank, AbilityCast c, Unit t, School s, float chance)
        {
            float v = PV(rank);
            var e = c.CurrentEffect;
            if (t == null || e == null || e.type != EffectType.Damage || c.Periodic) return chance;
            return t.HasAuraWithTag("Frozen") ? chance + v : chance;
        }
    }

    /// <summary>Winter's Chill: +2% crit per stack for Frost damage against the bearer.</summary>
    sealed class MageWintersChill : SpecialHandler
    {
        public MageWintersChill() : base("MageWintersChill") { }
        public override float IncomingCritBonus(AuraInstance a, AbilityCast c, School s)
        {
            var e = c.CurrentEffect;
            return s == School.Frost && e != null && e.type == EffectType.Damage ? 2f * Math.Max(1, a.Stacks) : 0f;
        }
    }

    /// <summary>Master of Elements: Fire/Frost crits refund values[rank]% of the ability's base mana cost (once per cast).</summary>
    sealed class MageMasterOfElements : SpecialHandler
    {
        public MageMasterOfElements() : base("MageMasterOfElements") { }
        public override void OnEffectCrit(AbilityCast c, EffectDef e, Unit t, float amount, bool heal, int rank)
        {
            float pct = PV(rank);
            if (heal || c.Ability == null || c.Periodic || c.SourceAura != null || c.Vars.ContainsKey("moe")) return;
            var school = e != null && e.school.HasValue ? e.school.Value : c.School;
            if (school != School.Fire && school != School.Frost) return;
            if (c.Ability.cost == null || c.Ability.cost.type != ResourceType.Mana) return;
            c.Vars["moe"] = 1f;
            float gain = BaseCost(c.Caster, c.Ability, c.Rank) * pct / 100f;
            if (gain > 0f) c.Battle.ChangeResource(c.Caster, ResourceType.Mana, gain, c.Caster);
        }
    }

    /// <summary>Improved Fire Ward / Frost Warding: chance to reflect hostile Fire/Frost spells while the ward is up.</summary>
    sealed class MageWardReflect : SpecialHandler
    {
        public MageWardReflect() : base("MageWardReflect") { }
        public override float ReflectChance(Unit u, int rank, AbilityCast incoming)
        {
            string src = Specials.CurrentSource;
            float v = PV(rank);
            bool frost = src != null && src.Contains("frost");
            string ward = frost ? "mage_frost_ward" : "mage_fire_ward";
            var school = frost ? School.Frost : School.Fire;
            return incoming.School == school && u.HasAura(ward) ? v : 0f;
        }
    }

    /// <summary>Magic Absorption: fully resisted hostile spells restore values[rank]% of maximum mana (1 s ICD).</summary>
    sealed class MageMagicAbsorption : SpecialHandler
    {
        const string Key = "sp:MageMagicAbsorption";
        public MageMagicAbsorption() : base("MageMagicAbsorption") { }
        public override void OnSpellResisted(Battle b, Unit victim, int rank, Unit attacker, AbilityCast c)
        {
            float pct = PV(rank);
            if (pct <= 0f || c == null || c.School == School.Physical || attacker == null || !attacker.IsHostileTo(victim)) return;
            if (victim.ProcCooldowns.TryGetValue(Key, out var cd) && cd > 0f) return;
            victim.ProcCooldowns[Key] = 1f;
            b.ChangeResource(victim, ResourceType.Mana, victim.MaxMana * pct / 100f, victim);
        }
    }

    /// <summary>Arcane Resilience: Armor + 50% of Intellect.</summary>
    sealed class MageArcaneResilience : SpecialHandler
    {
        public MageArcaneResilience() : base("MageArcaneResilience") { }
        public override void AdjustStats(Unit u, int rank, UnitStats s) => s.Armor += 0.5f * s.Intellect;
    }

    /// <summary>Improved Mana Shield: mana per absorbed point × (1 − values[rank]%).</summary>
    sealed class MageImprovedManaShield : SpecialHandler
    {
        public MageImprovedManaShield() : base("MageImprovedManaShield") { }
        public override float ManaPerDamageMult(Unit u, int rank, AuraInstance shield)
        {
            float v = PV(rank);
            return shield != null && shield.Def.id == "mage_mana_shield" ? Math.Max(0f, 1f - v / 100f) : 1f;
        }
    }

    /// <summary>Improved Arcane Missiles (Arcane Missiles) / Burning Soul (Fire spells): chance to ignore pushback.</summary>
    sealed class MagePushbackResist : SpecialHandler
    {
        public MagePushbackResist() : base("MagePushbackResist") { }
        public override float PushbackResistChance(Unit u, int rank, AbilityDef pending)
        {
            string src = Specials.CurrentSource ?? "";
            float v = PV(rank);
            if (src.Contains("arcane_missiles")) return pending.id == "mage_arcane_missiles" ? v : 0f;
            return pending.school == School.Fire ? v : 0f;
        }
    }
}
