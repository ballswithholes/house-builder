// Warlock specials: soul shards, demons, curses and talents.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterWarlock()
        {
            Register(new WarlockConsumeSoulShard());
            Register(new WarlockDrainSoulShard());
            Register(new WarlockShadowburnShard());
            Register(new WarlockLifeTap());
            Register(new WarlockCurseOfAgonyRamp());
            Register(new WarlockSearingPainThreat());
            Register(new OneTargetPerCaster("WarlockOneTargetCC"));
            Register(new WarlockEnslaveDemon());
            Register(new WarlockSoulstoneResurrection());
            Register(new WarlockAmplifyCurse());
            Register(new NextCastInstant("WarlockShadowTrance", a => a.id == "warlock_shadow_bolt"));
            Register(new WarlockFelDomination());
            Register(new WarlockDemonicSacrifice());
            Register(new WarlockSoulLink());
            Register(new WarlockMasterDemonologist());
            Register(new WarlockImprovedDrainMana());
            Register(new WarlockImprovedDrainSoul());
            Register(new WarlockPushbackResist());
        }
    }

    static class SoulShards
    {
        internal static string Item => RulesConstants.SoulShardItem;
        internal static bool Has(Battle b, Unit u) => HasReagent(b, u, Item);
        internal static void Take(Battle b, Unit u) => ConsumeReagent(b, u, Item);
        internal static void Give(Battle b, Unit u)
        {
            if (!UsesReagents(b, u) || b.Inventory.Count(Item) >= RulesConstants.MaxSoulShards) return;
            GiveItem(b, u, Item);
        }
    }

    /// <summary>Abilities that need and consume one Soul Shard when they resolve (pending casts: on completion).</summary>
    sealed class WarlockConsumeSoulShard : SpecialHandler
    {
        public WarlockConsumeSoulShard() : base("WarlockConsumeSoulShard") { }
        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) => SoulShards.Has(b, u) ? null : "Requires a Soul Shard.";
        public override void Resolve(AbilityCast c)
        {
            if (c.Depth > 0 || c.Periodic) return;
            if (!SoulShards.Has(c.Battle, c.Caster)) { c.SkipEffects = true; c.Battle.Log($"{c.Caster.Name} has no Soul Shard.", c.Caster); return; }
            SoulShards.Take(c.Battle, c.Caster);
        }
    }

    /// <summary>Drain Soul: a target dying while drained (and worth experience) yields a Soul Shard.</summary>
    sealed class WarlockDrainSoulShard : SpecialHandler
    {
        const string AbilityId = "warlock_drain_soul";
        public WarlockDrainSoulShard() : base("WarlockDrainSoulShard") { }

        public override void OnAnyUnitFell(Battle b, Unit victim)
        {
            foreach (var w in b.Units)
            {
                var p = w.Pending;
                if (p == null || !p.Channel || p.Ability.id != AbilityId || p.Target != victim || !w.IsAlive) continue;
                Grant(b, w, victim);
                b.FireProcs(ProcTrigger.OnKill, w, victim, new ProcInfo { Ability = p.Ability, School = p.Ability.school });
            }
        }

        public override void OnKill(Battle b, Unit killer, Unit victim, DamageInfo info, int rank)
        {
            if (info?.Cast?.Ability == null || info.Cast.Ability.id != AbilityId || killer == null) return;
            Grant(b, killer, victim);
        }

        static void Grant(Battle b, Unit w, Unit victim)
        {
            if (victim.GetVar("ds_shard") > 0f || !GrantsXp(victim, w)) return;
            victim.SetVar("ds_shard", 1f);
            SoulShards.Give(b, w);
        }
    }

    /// <summary>Shadowburn's hidden debuff: if the bearer dies while it lasts (and is worth experience) its caster gains a shard.</summary>
    sealed class WarlockShadowburnShard : SpecialHandler
    {
        public WarlockShadowburnShard() : base("WarlockShadowburnShard") { }
        public override void OnHolderDowned(Battle b, AuraInstance a)
        {
            var w = a.Caster;
            if (w == null || !GrantsXp(a.Bearer, w) || a.Bearer.GetVar("sb_shard") > 0f) return;
            a.Bearer.SetVar("sb_shard", 1f);
            SoulShards.Give(b, w);
        }
    }

    /// <summary>Life Tap: lose X health (not damage), gain X × (1 + Improved Life Tap) mana. Needs more than X health.</summary>
    sealed class WarlockLifeTap : SpecialHandler
    {
        public WarlockLifeTap() : base("WarlockLifeTap") { }

        static float Amount(Unit u, AbilityDef a, int rank, EffectDef e)
        {
            int eff = AbilityRules.EffLevel(u, a, rank);
            return e.min + e.perLevel * Math.Max(0, eff - a.learnLevel) + e.coef * u.Stats.SpellDamage(School.Shadow);
        }

        static EffectDef Effect(AbilityDef a)
        {
            foreach (var e in a.effects) if (e.type == EffectType.Special && e.special == "WarlockLifeTap") return e;
            return null;
        }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            var e = Effect(a);
            if (e == null) return null;
            return u.Health > Amount(u, a, AbilityRules.UsedRank(u, a), e) ? null : "Not enough health.";
        }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            float x = c.Ability != null ? Amount(u, c.Ability, c.Rank, e) : e.min;
            float loss = Math.Min(x, Math.Max(0f, u.Health - 1f));
            if (loss <= 0f) return;
            u.Health -= loss;
            c.Battle.Emit(new CombatEvent { Type = CombatEventType.ResourceChange, Source = u, Target = u, Amount = -loss, AbilityId = c.AbilityId, Name = c.Name, Reason = "health" });
            c.Battle.ChangeResource(u, ResourceType.Mana, x * c.Mods.EffectMult, u);
        }
    }

    /// <summary>Curse of Agony: ticks 1-4 deal ×0.5, 5-8 ×1, 9-12 ×1.5; refreshing restarts the ramp.</summary>
    sealed class WarlockCurseOfAgonyRamp : SpecialHandler
    {
        public WarlockCurseOfAgonyRamp() : base("WarlockCurseOfAgonyRamp") { }

        public override float ModifyDamage(AbilityCast c, EffectDef e, Unit t, float v)
        {
            var a = c.SourceAura;
            if (a == null || !c.Periodic || a.Def.special != Name) return v;
            int n = (int)a.GetVar("tick") + 1;
            a.SetVar("tick", n);
            return v * (n <= 4 ? 0.5f : n <= 8 ? 1f : 1.5f);
        }

        public override void OnAuraRefreshed(Battle b, AuraInstance a) => a.SetVar("tick", 0f);
    }

    /// <summary>Searing Pain: double threat from its damage.</summary>
    sealed class WarlockSearingPainThreat : SpecialHandler
    {
        public WarlockSearingPainThreat() : base("WarlockSearingPainThreat") { }
        public override float ThreatMultiplier(AbilityCast c) => 2f;
    }

    /// <summary>
    /// Enslave Demon: a demon of level ≤ 45/55/60 becomes the warlock's pet (one Soul Shard) while the aura lasts; each of
    /// its turns it may break free (1% per turn enslaved) and then returns hostile, angry at the warlock.
    /// </summary>
    sealed class WarlockEnslaveDemon : SpecialHandler
    {
        static readonly int[] MaxLevel = { 45, 55, 60 };
        public WarlockEnslaveDemon() : base("WarlockEnslaveDemon") { }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            if (!SoulShards.Has(b, u)) return "Requires a Soul Shard.";
            if (target == null || target == u) return null;
            int r = AbilityRules.UsedRank(u, a);
            int max = MaxLevel[MathUtil.Clamp(r, 1, MaxLevel.Length) - 1];
            if (target.Level > max) return $"Target is too powerful (level {max} or lower).";
            if (target.Kind != UnitKind.Creature || target.Owner != null) return "Cannot enslave that target.";
            return null;
        }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            var w = a.Caster;
            var demon = a.Bearer;
            if (w == null || demon == w || !demon.IsAlive) return;
            SoulShards.Take(b, w);
            b.ChangeSide(demon, w.Team, w, true);
            a.SetVar("turns", 0f);
        }

        public override void OnBearerTurnStart(Battle b, AuraInstance a)
        {
            float n = a.GetVar("turns") + 1f;
            a.SetVar("turns", n);
            if (b.Rng.Chance(n)) b.RemoveAura(a, AuraRemoveReason.Broken);
        }

        public override void OnAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason)
        {
            var demon = a.Bearer;
            if (!demon.OriginalTeam.HasValue) return;
            if (b.IsOver || !b.InCombat)
            {
                // released when the fight ends: it wanders off
                if (demon.Owner != null && demon.Owner.Pet == demon) demon.Owner.Pet = null;
                if (b.Units.Contains(demon)) b.RemoveUnit(demon, "released");
                return;
            }
            if (demon.IsAlive) b.RestoreSide(demon, a.Caster);
        }

        public override void OnBattleFinished(Battle b)
        {
            foreach (var u in new List<Unit>(b.Units))
            {
                if (!u.OriginalTeam.HasValue) continue;
                foreach (var a in new List<AuraInstance>(u.Auras))
                    if (a.Def.special == Name) b.RemoveAura(a, AuraRemoveReason.Cancelled);
            }
        }
    }

    /// <summary>Soulstone: when the holder falls, the stone is used up and the holder may rise at its next turn.</summary>
    sealed class WarlockSoulstoneResurrection : SpecialHandler
    {
        public WarlockSoulstoneResurrection() : base("WarlockSoulstoneResurrection") { }

        static void Values(string id, out float h, out float m)
        {
            if (id.EndsWith("_major")) { h = 2200; m = 2800; }
            else if (id.EndsWith("_greater")) { h = 1600; m = 2200; }
            else if (id.EndsWith("_lesser")) { h = 750; m = 1200; }
            else if (id.EndsWith("_minor")) { h = 400; m = 700; }
            else { h = 1100; m = 1700; }
        }

        public override void OnHolderDowned(Battle b, AuraInstance a)
        {
            var u = a.Bearer;
            Values(a.Def.id, out var h, out var m);
            b.RemoveAura(a, AuraRemoveReason.Consumed);
            if (!b.InCombat) return;
            u.SelfRes = new SelfResOffer { Source = a.Def.id, Name = "Soulstone", Health = Math.Min(h, u.MaxHealth), Mana = Math.Min(m, u.MaxMana) };
        }
    }

    /// <summary>Amplify Curse: the next Curse of Agony/Weakness is 50% stronger, or Curse of Exhaustion slows 20 points more.</summary>
    sealed class WarlockAmplifyCurse : SpecialHandler
    {
        public WarlockAmplifyCurse() : base("WarlockAmplifyCurse") { }
        public override void OnAuraAppliedByMe(Battle b, Unit caster, int rank, AuraInstance inst)
        {
            string id = inst.Def.id;
            if (id != "warlock_curse_of_agony" && id != "warlock_curse_of_weakness" && id != "warlock_curse_of_exhaustion") return;
            AuraInstance amp = null;
            foreach (var a in caster.Auras) if (a.Def.special == Name) { amp = a; break; }
            if (amp == null) return;
            if (id == "warlock_curse_of_agony") inst.DamageMult *= 1.5f;
            else if (id == "warlock_curse_of_weakness") { for (int i = 0; i < inst.ModValues.Length; i++) inst.ModValues[i] *= 1.5f; }
            else if (inst.ModValues.Length > 0) inst.ModValues[0] -= 20f * inst.EffectMult;
            inst.Bearer.InvalidateStats();
            b.RemoveAura(amp, AuraRemoveReason.Consumed);
        }
    }

    /// <summary>Fel Domination: the next demon summon is 5.5 s faster and costs 50% less mana.</summary>
    sealed class WarlockFelDomination : SpecialHandler
    {
        public WarlockFelDomination() : base("WarlockFelDomination") { }

        static bool IsSummon(AbilityDef a) =>
            a != null && (a.id == "warlock_summon_imp" || a.id == "warlock_summon_voidwalker" || a.id == "warlock_summon_succubus" || a.id == "warlock_summon_felhunter");

        public override float ModifyCastTime(Unit u, int rank, AbilityDef a, float t) => IsSummon(a) ? Math.Max(0f, t - 5.5f) : t;
        public override float ModifyCost(Unit u, int rank, AbilityDef a, float cost) => IsSummon(a) ? cost * 0.5f : cost;

        public override void OnAbilityStart(Battle b, Unit u, int rank, AbilityCast c, float castTime)
        {
            if (!IsSummon(c.Ability)) return;
            c.CostMult = 0.5f; // the cost is paid when the (possibly pending) cast completes
            foreach (var a in new List<AuraInstance>(u.Auras))
                if (a.Def.special == Name) { b.RemoveAura(a, AuraRemoveReason.Consumed); break; }
        }
    }

    /// <summary>Demonic Sacrifice: sacrifices the summoned demon for a buff that depends on its family.</summary>
    sealed class WarlockDemonicSacrifice : SpecialHandler
    {
        public WarlockDemonicSacrifice() : base("WarlockDemonicSacrifice") { }

        static string AuraFor(Unit pet)
        {
            if (pet == null || pet.Creature == null || pet.OriginalTeam.HasValue) return null;
            switch (pet.Creature.family)
            {
                case "Imp": return "warlock_demonic_sacrifice_imp";
                case "Voidwalker": return "warlock_demonic_sacrifice_voidwalker";
                case "Succubus": return "warlock_demonic_sacrifice_succubus";
                case "Felhunter": return "warlock_demonic_sacrifice_felhunter";
                default: return null;
            }
        }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) =>
            u.Pet != null && u.Pet.IsAlive && AuraFor(u.Pet) == null ? "That demon cannot be sacrificed." : null;

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            var u = c.Caster;
            var pet = u.Pet;
            var id = AuraFor(pet);
            if (id == null || !pet.IsAlive) return;
            var def = b.Db.Aura(id);
            b.Emit(new CombatEvent { Type = CombatEventType.Log, Source = u, Target = pet, Text = $"{u.Name} sacrifices {pet.Name}." });
            b.DismissPet(u);
            pet.Dead = true;
            if (def != null) b.ApplyAura(u, u, def, new AuraApplyInfo { EffLevel = u.Level, LearnLevel = 1, Duration = def.duration, Mods = c.Mods });
        }
    }

    /// <summary>Soul Link: 30% of the warlock's damage taken is dealt to the demon instead; ends when the demon is gone.</summary>
    sealed class WarlockSoulLink : SpecialHandler
    {
        public WarlockSoulLink() : base("WarlockSoulLink") { }

        public override float ModifyIncomingDamage(Battle b, AuraInstance a, Unit src, float amount, School school, DamageInfo info)
        {
            var w = a.Bearer;
            var pet = w.Pet;
            if (pet == null || !pet.IsAlive || !b.Units.Contains(pet) || amount <= 0f) return amount;
            float share = amount * 0.3f;
            b.DealDamage(src, pet, share, school, new DamageInfo { Name = a.Def.name, IgnoreModifiers = true, Redirected = true, NoThreat = true, NoProcs = true });
            return amount - share;
        }

        public override void OnPetChanged(Battle b, Unit owner, int rank)
        {
            var pet = owner.Pet;
            if (pet != null && pet.IsAlive && b.Units.Contains(pet)) return;
            RemoveWhere(b, owner, x => x.Def.id == "warlock_soul_link");
            if (pet != null) RemoveWhere(b, pet, x => x.Def.id == "warlock_soul_link_pet");
        }
    }

    /// <summary>Master Demonologist: a family-specific aura on both warlock and demon, scaled by talent rank.</summary>
    sealed class WarlockMasterDemonologist : SpecialHandler
    {
        const string Group = "warlock_master_demonologist";
        public WarlockMasterDemonologist() : base("WarlockMasterDemonologist") { }

        public override void OnPetChanged(Battle b, Unit owner, int rank)
        {
            float r = PV(rank);
            RemoveWhere(b, owner, x => x.Def.exclusiveGroup == Group);
            var pet = owner.Pet;
            if (pet == null || !pet.IsAlive || pet.Creature == null || !b.Units.Contains(pet)) return;
            RemoveWhere(b, pet, x => x.Def.exclusiveGroup == Group);
            var def = b.Db.Aura(Group + "_" + (pet.Creature.family ?? "").ToLowerInvariant());
            if (def == null || pet.Creature.type != CreatureType.Demon) return;
            foreach (var u in new[] { owner, pet })
            {
                var inst = b.ApplyAura(owner, u, def, new AuraApplyInfo { EffLevel = owner.Level, LearnLevel = 1, Duration = -1f });
                if (inst == null) continue;
                Scale(inst, owner, r);
                u.InvalidateStats();
            }
        }

        static void Scale(AuraInstance inst, Unit warlock, float r)
        {
            bool perLevel = inst.Def.id.EndsWith("_felhunter");
            for (int i = 0; i < inst.ModValues.Length; i++) inst.ModValues[i] *= r * (perLevel ? warlock.Level : 1f);
        }

        public override void OnModValuesRefreshed(AuraInstance a)
        {
            if (a.Def.exclusiveGroup != Group) return;
            var warlock = a.Caster ?? (a.Bearer?.Owner ?? a.Bearer);
            if (warlock == null) return;
            float r = TalentValue(warlock, Name);
            if (r > 0f) Scale(a, warlock, r);
        }
    }

    /// <summary>Improved Drain Mana: each Drain Mana tick deals Shadow damage equal to values[rank]% of the mana drained.</summary>
    sealed class WarlockImprovedDrainMana : SpecialHandler
    {
        public WarlockImprovedDrainMana() : base("WarlockImprovedDrainMana") { }
        public override void OnChannelTick(AbilityCast c, int rank)
        {
            float pct = PV(rank);
            if (c.Ability == null || c.Ability.id != "warlock_drain_mana") return;
            if (!c.Vars.TryGetValue("drained", out var drained)) return;
            c.Vars.Remove("drained");
            var t = c.Target;
            if (drained <= 0f || pct <= 0f || t == null || !t.IsAlive) return;
            c.Battle.DealDamage(c.Caster, t, drained * pct / 100f, School.Shadow, new DamageInfo { Ability = c.Ability, Name = "Improved Drain Mana", Kind = AttackKind.Spell, Periodic = true, Cast = c });
        }
    }

    /// <summary>Improved Drain Soul buff: Spirit-based mana regeneration doubled.</summary>
    sealed class WarlockImprovedDrainSoul : SpecialHandler
    {
        public WarlockImprovedDrainSoul() : base("WarlockImprovedDrainSoul") { }
        public override void AdjustStats(Unit u, int rank, UnitStats s) => s.SpiritRegenPerTick *= 2f;
    }

    /// <summary>Fel Concentration (drains) and Intensity (Destruction spells): chance to ignore casting pushback.</summary>
    sealed class WarlockPushbackResist : SpecialHandler
    {
        public WarlockPushbackResist() : base("WarlockPushbackResist") { }
        public override float PushbackResistChance(Unit u, int rank, AbilityDef pending)
        {
            string src = Specials.CurrentSource ?? "";
            float v = PV(rank);
            if (src.Contains("fel_concentration"))
                return pending.id == "warlock_drain_life" || pending.id == "warlock_drain_mana" || pending.id == "warlock_drain_soul" ? v : 0f;
            return AbilityMods.HasTag(pending, "Destruction") ? v : 0f;
        }
    }
}
