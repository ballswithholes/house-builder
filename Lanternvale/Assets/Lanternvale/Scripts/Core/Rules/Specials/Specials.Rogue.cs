// Rogue specials.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterRogue()
        {
            Register(new RoguePerComboByRank());
            Register(new RogueExposeArmor());
            Register(new RogueKidneyShot());
            Register(new RogueVanish());
            Register(new RogueBlind());
            Register(new RogueDistracted());
            Register(new RoguePickLock());
            Register(new RogueRelentlessStrikes());
            Register(new RogueMurder());
            Register(new RogueImprovedPoisons());
            Register(new RogueVigor());
            Register(new RogueCamouflage());
            Register(new RogueMasterOfDeception());
            Register(new RogueRemoveSnares());
            Register(new RogueSleightOfHand());
            Register(new RogueHeightenedSenses());
            Register(new RogueSerratedBlades());
            UndetectableAuras.Add("rogue_vanish_aura");
        }
    }

    /// <summary>Finishers gain amount × (effLevel − learnLevel) per combo point on every Damage effect (incl. Rupture ticks).
    /// Eviscerate's attack-power part per point is the generic EffectDef.apCoefPerCombo (Battle.EffectDamage).</summary>
    sealed class RoguePerComboByRank : SpecialHandler
    {
        public RoguePerComboByRank() : base("RoguePerComboByRank") { }

        public override float ModifyDamage(AbilityCast c, EffectDef e, Unit t, float v)
        {
            if (e == null || e.type != EffectType.Damage || e.amount == 0f || c.ComboPoints <= 0) return v;
            float extra = e.amount * Math.Max(0, c.EffLevel - c.LearnLevel) * c.ComboPoints * c.MagnitudeScale;
            if (c.Periodic && c.SourceAura != null) extra *= c.SourceAura.DamageMult * c.SourceAura.EffectMult;
            else extra *= c.Mods.DamageMult;
            return v + extra;
        }
    }

    /// <summary>Expose Armor: armor reduction × combo points spent; does not stack with Sunder Armor (the stronger stays).</summary>
    sealed class RogueExposeArmor : SpecialHandler
    {
        const string Sunder = "warrior_sunder_armor";
        public RogueExposeArmor() : base("RogueExposeArmor") { }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            if (a.ModValues.Length > 0) a.ModValues[0] *= Math.Max(1, a.ComboPoints);
            a.Bearer.InvalidateStats();
            Resolve(b, a.Bearer);
        }

        public override void OnAuraRefreshed(Battle b, AuraInstance a) => OnAuraApplied(b, a);

        public override void OnModValuesRefreshed(AuraInstance a)
        {
            if (a.Def.special == Name && a.ModValues.Length > 0) a.ModValues[0] *= Math.Max(1, a.ComboPoints);
        }

        public override void OnAnyAuraApplied(Battle b, AuraInstance a)
        {
            if (a.Def.id == Sunder) Resolve(b, a.Bearer);
        }

        static void Resolve(Battle b, Unit u)
        {
            var expose = u.FindAura("rogue_expose_armor");
            var sunder = u.FindAura(Sunder);
            if (expose == null || sunder == null) return;
            float e = expose.ModValues.Length > 0 ? expose.ModValues[0] : 0f;
            float s = sunder.ModValues.Length > 0 ? sunder.ModValues[0] * Math.Max(1, sunder.Stacks) : 0f;
            // values are negative armor: keep the larger reduction
            b.RemoveAura(e <= s ? sunder : expose, AuraRemoveReason.Replaced);
        }
    }

    /// <summary>Kidney Shot: rank 1 lasts 1 s less; Improved Kidney Shot makes the target take +3% per rank damage.</summary>
    sealed class RogueKidneyShot : SpecialHandler
    {
        public RogueKidneyShot() : base("RogueKidneyShot") { }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            if (a.Rank <= 1 && a.Duration > 1f)
            {
                a.Duration = Math.Max(0.5f, a.Duration - 1f);
                a.Remaining = Math.Min(a.Remaining, a.Duration);
            }
            float pct = a.Caster != null ? TalentValue(a.Caster, Name) : 0f;
            if (a.ModValues.Length > 0) a.ModValues[0] = pct;
            a.Bearer.InvalidateStats();
        }

        public override void OnAuraRefreshed(Battle b, AuraInstance a) => OnAuraApplied(b, a);
    }

    /// <summary>Vanish: Flash Powder; frees from roots/snares, drops all threat and re-enters (undetectable) stealth.</summary>
    sealed class RogueVanish : SpecialHandler
    {
        const string Powder = "rogue_flash_powder";
        public RogueVanish() : base("RogueVanish") { }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) =>
            HasReagent(b, u, Powder) ? null : "Requires Flash Powder.";

        public override void Resolve(AbilityCast c)
        {
            var b = c.Battle;
            var u = c.Caster;
            ConsumeReagent(b, u, Powder);
            RemoveWhere(b, u, a => IsHarmful(a) && IsRootOrSnare(a.Def));
            foreach (var o in b.Units)
            {
                if (o.Threat.ContainsKey(u)) o.Threat[u] = 0f;
                if (o.AggroTarget == u) o.AggroTarget = null;
                if (o.AttackTarget == u && o.IsHostileTo(u)) o.AttackTarget = null;
                if (o.TauntedBy == u) o.TauntedBy = null;
            }
            b.StopAutoAttack(u);
            b.CancelQueuedSwing(u);
            u.ComboPoints = 0;
            u.ComboTarget = null;
        }
    }

    /// <summary>Blind consumes Blinding Powder (even when resisted).</summary>
    sealed class RogueBlind : SpecialHandler
    {
        const string Powder = "rogue_blinding_powder";
        public RogueBlind() : base("RogueBlind") { }
        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) =>
            HasReagent(b, u, Powder) ? null : "Requires Blinding Powder.";
        public override void Resolve(AbilityCast c) => ConsumeReagent(c.Battle, c.Caster, Powder);
    }

    /// <summary>
    /// Distract: the bearer faces the distraction point (FacingLock): it cannot detect stealth and everyone not
    /// between it and the point counts as behind it. The point is the Distract cast's target point.
    /// </summary>
    sealed class RogueDistracted : SpecialHandler
    {
        const string Ability = "rogue_distract";
        public RogueDistracted() : base("RogueDistracted") { }

        public override void OnAnyAbilityStart(Battle b, Unit u, AbilityCast c)
        {
            if (c.Ability == null || c.Ability.id != Ability) return;
            u.SetVar("distract_x", c.Point.x);
            u.SetVar("distract_y", c.Point.y);
        }

        public override void OnAuraApplied(Battle b, AuraInstance a)
        {
            var u = a.Bearer;
            var c = a.Caster;
            Vec2 p = c != null && c.Vars.ContainsKey("distract_x") ? new Vec2(c.GetVar("distract_x"), c.GetVar("distract_y")) : (c != null ? c.Position : u.Position + u.Facing);
            u.FacingLock = null;
            u.FaceTowards(p);
            u.FacingLock = p;
        }

        public override void OnAuraRefreshed(Battle b, AuraInstance a) => OnAuraApplied(b, a);

        public override void OnAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason)
        {
            if (!a.Bearer.HasAura(a.Def.id)) a.Bearer.FacingLock = null;
        }
    }

    /// <summary>
    /// Pick Lock: the exploration UI casts it on a locked chest/door; when the cast completes the session resolves
    /// the lock with <see cref="Roll"/> (raised as <see cref="Specials.FieldEvent"/> "RoguePickLock").
    /// </summary>
    public sealed class RoguePickLock : SpecialHandler
    {
        public RoguePickLock() : base("RoguePickLock") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            if (c.Battle.InCombat) return;
            Specials.RaiseFieldEvent(c.Battle, c.Caster, Name, null);
        }

        /// <summary>Lockpicking roll: d20 + SleightOfHand modifier + floor(level / 5) ≥ DC; a natural 20 always succeeds.</summary>
        public static bool Roll(Unit rogue, int dc, int skillModifier, Rng rng, out int total)
        {
            int d20 = rng.Range(1, 20);
            total = d20 + skillModifier + rogue.Level / 5;
            return d20 == 20 || total >= dc;
        }
    }

    /// <summary>Relentless Strikes: finishers have 20% per combo point to restore 25 energy.</summary>
    sealed class RogueRelentlessStrikes : SpecialHandler
    {
        public RogueRelentlessStrikes() : base("RogueRelentlessStrikes") { }
        public override void OnProc(Battle b, ProcTrigger trigger, Unit owner, Unit other, ProcInfo info, int rank)
        {
            if (trigger != ProcTrigger.OnFinisher || info == null || info.ComboPoints <= 0) return;
            if (owner.Owner != null) return; // the rogue's own finishers only
            if (b.Rng.Chance(20f * info.ComboPoints)) b.ChangeResource(owner, ResourceType.Energy, 25f, owner);
        }
    }

    /// <summary>Murder: +1% per rank damage to Humanoids, Giants, Beasts and Dragonkin.</summary>
    sealed class RogueMurder : SpecialHandler
    {
        public RogueMurder() : base("RogueMurder") { }
        public override float ModifyOutgoingDamage(Battle b, Unit u, int rank, AbilityCast c, EffectDef e, Unit t, float v)
        {
            float pct = PV(rank);
            if (t == null || u.Owner != null) return v;
            var ty = TypeOf(t);
            return ty == CreatureType.Humanoid || ty == CreatureType.Giant || ty == CreatureType.Beast || ty == CreatureType.Dragonkin ? v * (1f + pct / 100f) : v;
        }
    }

    /// <summary>Improved Poisons: +2 points per rank to weapon coating proc chances.</summary>
    sealed class RogueImprovedPoisons : SpecialHandler
    {
        public RogueImprovedPoisons() : base("RogueImprovedPoisons") { }
        public override float ProcChanceBonus(Unit u, int rank, AuraInstance aura, ProcDef p)
        {
            float v = PV(rank);
            return aura != null && aura.HasTag("WeaponCoating") ? v : 0f;
        }
    }

    /// <summary>Vigor: +10 maximum energy.</summary>
    sealed class RogueVigor : SpecialHandler
    {
        public RogueVigor() : base("RogueVigor") { }
        public override void AdjustStats(Unit u, int rank, UnitStats s) => s.ExtraMaxEnergy += PV(rank);
    }

    /// <summary>Camouflage: +3% per rank movement speed while stealthed.</summary>
    sealed class RogueCamouflage : SpecialHandler
    {
        public RogueCamouflage() : base("RogueCamouflage") { }
        public override void ContributeStats(Unit u, int rank, StatBlock block)
        {
            float v = PV(rank);
            if (u.HasStateAura(UnitState.Stealth)) block.Add(StatId.MoveSpeed, v, true);
        }
    }

    /// <summary>Master of Deception: enemies' detection radius against the rogue −10% per rank.</summary>
    sealed class RogueMasterOfDeception : SpecialHandler
    {
        public RogueMasterOfDeception() : base("RogueMasterOfDeception") { }
        public override float DetectionRadiusMult(Unit stealthed, int rank) => Math.Max(0f, 1f - 0.1f * rank);
    }

    /// <summary>Removes root and snare debuffs from the effect target (Improved Sprint).</summary>
    sealed class RogueRemoveSnares : SpecialHandler
    {
        public RogueRemoveSnares() : base("RogueRemoveSnares") { }
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = t ?? c.Caster;
            RemoveWhere(c.Battle, u, a => IsHarmful(a) && IsRootOrSnare(a.Def));
        }
    }

    /// <summary>Sleight of Hand: melee/ranged attacks against the rogue −1% crit per rank.</summary>
    sealed class RogueSleightOfHand : SpecialHandler
    {
        public RogueSleightOfHand() : base("RogueSleightOfHand") { }
        public override float ModifyIncomingCritChance(Unit target, int rank, AbilityCast c, float chance)
        {
            float v = PV(rank);
            var k = c.Kind;
            return k == AttackKind.Melee || k == AttackKind.Ranged ? Math.Max(0f, chance - v) : chance;
        }
    }

    /// <summary>Heightened Senses: spells (and wands) and ranged attacks miss the rogue value × rank (2/4) percentage points more often; melee is unaffected.</summary>
    sealed class RogueHeightenedSenses : SpecialHandler
    {
        public RogueHeightenedSenses() : base("RogueHeightenedSenses") { }
        public override float IncomingMissChance(Unit target, int rank, AttackKind kind) =>
            kind == AttackKind.Spell || kind == AttackKind.Wand || kind == AttackKind.Ranged ? PV(rank) : 0f;
    }

    /// <summary>Serrated Blades: physical attacks ignore values[rank] × level armor.</summary>
    sealed class RogueSerratedBlades : SpecialHandler
    {
        public RogueSerratedBlades() : base("RogueSerratedBlades") { }
        public override void AdjustStats(Unit u, int rank, UnitStats s) => s.ArmorPenetration += PV(rank) * u.Level;
    }
}
