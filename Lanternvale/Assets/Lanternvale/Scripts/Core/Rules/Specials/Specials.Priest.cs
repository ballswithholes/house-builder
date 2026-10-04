// Priest specials.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using static Lanternvale.Rules.SpecialUtil;

namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterPriest()
        {
            Register(new PriestWeakenedSoulCheck());
            Register(new PriestFade());
            Register(new PriestManaBurn());
            Register(new PriestDispelRank());
            Register(new PriestMindSoothe());
            Register(new PriestMindControl());
            Register(new PriestLightwell());
            Register(new PriestSpiritOfRedemption());
            Register(new PriestVampiricEmbrace());
            Register(new PriestFearWard());
            Register(new PriestFocusedCasting());
            Register(new PriestPushbackResist());
            Register(new PriestUnbreakableWill());
            Register(new PriestSpiritualGuidance());
            Register(new PriestBlessedRecovery());
            PushbackImmuneAuras.Add("priest_power_word_shield");
        }
    }

    /// <summary>Power Word: Shield cannot target a unit with Weakened Soul (from any priest).</summary>
    sealed class PriestWeakenedSoulCheck : SpecialHandler
    {
        public PriestWeakenedSoulCheck() : base("PriestWeakenedSoulCheck") { }
        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) =>
            target != null && target.HasAura("priest_weakened_soul") ? "Target has Weakened Soul." : null;
    }

    /// <summary>Fade: temporarily removes F threat from every enemy table; restored when priest_fade ends.</summary>
    sealed class PriestFade : SpecialHandler
    {
        const string AuraId = "priest_fade";
        public PriestFade() : base("PriestFade") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            var b = c.Battle;
            var aura = u.FindAura(AuraId);
            float f = Magnitude(c, e);
            if (f <= 0f) return;
            foreach (var o in b.Units)
            {
                if (!o.IsAlive || !o.Threat.TryGetValue(u, out var cur)) continue;
                float removed = Math.Min(cur, f);
                if (removed <= 0f) continue;
                o.Threat[u] = cur - removed;
                if (aura != null) aura.SetVar("fade:" + o.Id, aura.GetVar("fade:" + o.Id) + removed);
                b.Emit(new CombatEvent { Type = CombatEventType.Threat, Source = u, Target = o, Amount = -removed, AbilityId = c.AbilityId, Name = c.Name });
            }
        }

        public override void OnAnyAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason)
        {
            if (a.Def.id != AuraId || a.Vars == null) return;
            var u = a.Bearer;
            foreach (var o in b.Units)
            {
                if (!o.IsAlive || !a.Vars.TryGetValue("fade:" + o.Id, out var v) || v <= 0f) continue;
                if (!u.IsAlive) continue;
                o.Threat.TryGetValue(u, out var cur);
                o.Threat[u] = cur + v;
            }
        }
    }

    /// <summary>Mana Burn / Feedback: burn B mana (capped), then deal B × amount% Shadow damage (no crit, no spell power).</summary>
    sealed class PriestManaBurn : SpecialHandler
    {
        public PriestManaBurn() : base("PriestManaBurn") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            if (t == null || !t.IsAlive || t == c.Caster) return;
            if (c.SourceProc == null && t.IsHostileTo(c.Caster) && !c.Periodic)
            {
                var o = b.RollHit(c, t, e);
                if (o != HitOutcome.Hit) return;
            }
            float burn = Math.Min(t.Mana, Math.Max(0f, Magnitude(c, e) * c.Mods.EffectMult));
            if (burn <= 0f || t.MaxMana <= 0f) return;
            b.ChangeResource(t, ResourceType.Mana, -burn, c.Caster);
            float dmg = burn * (e.amount > 0 ? e.amount : 100f) / 100f;
            b.DealDamage(c.Caster, t, dmg, School.Shadow, new DamageInfo { Ability = c.Ability, Name = c.Ability != null ? c.Ability.name : "Feedback", Kind = AttackKind.Spell, Cast = c });
        }
    }

    /// <summary>Dispel Magic removes as many effects as the rank used (1 at rank 1, 2 at rank 2).</summary>
    sealed class PriestDispelRank : SpecialHandler
    {
        public PriestDispelRank() : base("PriestDispelRank") { }
        public override bool ReplaceEffect(AbilityCast c, EffectDef e, Unit t)
        {
            var copy = new EffectDef { type = EffectType.Dispel, target = e.target, dispelType = e.dispelType, dispelCount = Math.Max(1, c.Rank), chance = 100f };
            c.Battle.ApplyEffect(c, copy, t, 1f);
            return true;
        }
    }

    /// <summary>
    /// Mind Soothe: out of combat the session shrinks the trigger radius of the encounter the target belongs to (FieldEvent
    /// "PriestMindSoothe", cast through GameSession.UseAbilityOnEncounter); in combat only the debuff remains.
    /// </summary>
    sealed class PriestMindSoothe : SpecialHandler
    {
        public PriestMindSoothe() : base("PriestMindSoothe") { }
        public override bool FieldTargetsEncounter => true;
        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            if (!c.Battle.InCombat) Specials.RaiseFieldEvent(c.Battle, c.Caster, Name, t);
        }
    }

    /// <summary>
    /// Mind Control: the humanoid (level ≤ 33/45/57) fights for the priest while priest_mind_control lasts. The priest
    /// channels: acting, moving, being controlled or dying ends it; the creature then returns with full threat on the priest.
    /// </summary>
    sealed class PriestMindControl : SpecialHandler
    {
        const string AuraId = "priest_mind_control";
        static readonly int[] MaxLevel = { 33, 45, 57 };
        public PriestMindControl() : base("PriestMindControl") { }

        public override string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            if (target == null || target == u) return null;
            int r = AbilityRules.UsedRank(u, a);
            int max = MaxLevel[MathUtil.Clamp(r, 1, MaxLevel.Length) - 1];
            if (target.Level > max) return $"Target is too powerful (level {max} or lower).";
            if (target.Kind != UnitKind.Creature || target.Owner != null) return "Cannot control that target.";
            return null;
        }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var b = c.Battle;
            var u = c.Caster;
            if (t == null || !t.IsAlive || t.FindAura(AuraId, u) == null) return;
            End(b, u);
            b.ChangeSide(t, u.Team, u, false);
            u.SetVar("mc_target", t.Id);
        }

        static Unit Controlled(Battle b, Unit priest)
        {
            if (!priest.Vars.TryGetValue("mc_target", out var id)) return null;
            foreach (var o in b.Units) if (o.Id == (int)id) return o;
            return null;
        }

        static void End(Battle b, Unit priest)
        {
            var t = Controlled(b, priest);
            priest.Vars.Remove("mc_target");
            if (t == null) return;
            var aura = t.FindAura(AuraId, priest);
            if (aura != null) b.RemoveAura(aura, AuraRemoveReason.Cancelled);
            else if (t.OriginalTeam.HasValue) b.RestoreSide(t, priest);
        }

        static bool IsController(Unit u) => u != null && u.Vars.ContainsKey("mc_target");

        public override void OnAnyAbilityStart(Battle b, Unit u, AbilityCast c)
        {
            if (IsController(u) && b.InCombat) End(b, u);
        }

        public override void OnAnyUnitMoved(Battle b, Unit u)
        {
            if (IsController(u) && b.InCombat) End(b, u);
        }

        public override void OnAnyAuraApplied(Battle b, AuraInstance a)
        {
            if (!IsController(a.Bearer) || !IsHarmful(a)) return;
            if (ImposesAny(a.Def, UnitState.Stun, UnitState.Silence, UnitState.Fear, UnitState.Incapacitate, UnitState.Sleep,
                    UnitState.Polymorph, UnitState.Confuse, UnitState.Banish))
                End(b, a.Bearer);
        }

        public override void OnAnyAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason)
        {
            if (a.Def.id != AuraId) return;
            var t = a.Bearer;
            if (a.Caster != null && a.Caster.GetVar("mc_target", -1f) == t.Id) a.Caster.Vars.Remove("mc_target");
            if (t.OriginalTeam.HasValue && reason != AuraRemoveReason.Death) b.RestoreSide(t, a.Caster);
        }

        public override void OnAnyUnitFell(Battle b, Unit u)
        {
            if (IsController(u)) End(b, u);
            // a controlled creature that dies still counts as a slain enemy
            if (u.OriginalTeam.HasValue && u.Kind == UnitKind.Creature && u.FindAura(AuraId) == null && u.Team == b.PlayerTeam)
            {
                u.Team = u.OriginalTeam.Value;
                u.Owner = u.OriginalOwner;
                u.OriginalTeam = null;
                if (u.Creature != null) b.KilledCreatures.Add(u.Creature.id);
            }
        }

        public override void OnBattleFinished(Battle b)
        {
            foreach (var u in new List<Unit>(b.Units))
            {
                var a = u.FindAura(AuraId);
                if (a != null) b.RemoveAura(a, AuraRemoveReason.Cancelled);
                u.Vars.Remove("mc_target");
            }
        }
    }

    /// <summary>
    /// Lightwell: 5 charges; party members within 5 yards may use priest_lightwell_renew (contextual action) to get the
    /// HoT at the priest's Lightwell rank and healing power. One Lightwell per priest.
    /// </summary>
    sealed class PriestLightwell : SpecialHandler
    {
        const string WellId = "priest_lightwell", RenewId = "priest_lightwell_renew";
        public PriestLightwell() : base("PriestLightwell") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var u = c.Caster;
            Unit newest = null;
            foreach (var s in u.Summons) if (s.IsAlive && s.Creature != null && s.Creature.id == WellId) newest = s;
            if (newest == null) return;
            foreach (var s in new List<Unit>(u.Summons))
                if (s != newest && s.Creature != null && s.Creature.id == WellId && s.IsAlive) c.Battle.Despawn(s, "replaced");
            newest.SetVar("charges", 5f);
            newest.SetVar("rank", c.Rank);
        }

        static Unit WellNear(Battle b, Unit u)
        {
            foreach (var w in b.Units)
            {
                if (!w.IsAlive || w.Creature == null || w.Creature.id != WellId || w.Team != u.Team) continue;
                if (w.GetVar("charges") <= 0f || w.Owner == null) continue;
                if (u.DistanceTo(w) <= MathUtil.Yd(5f) + u.Radius + w.Radius) return w;
            }
            return null;
        }

        public override void ContextualAbilities(Battle b, Unit u, List<string> into)
        {
            if (!u.IsAlive || u.IsTotem || u.Kind == UnitKind.Summon || u.HasAura(RenewId)) return;
            if (WellNear(b, u) != null && !into.Contains(RenewId)) into.Add(RenewId);
        }

        public override void OnAnyAbilityStart(Battle b, Unit u, AbilityCast c)
        {
            if (c.Ability == null || c.Ability.id != RenewId) return;
            var w = WellNear(b, u);
            if (w == null) return;
            c.SkipEffects = true;
            var priest = w.Owner;
            var def = b.Db.Aura(RenewId);
            if (def == null) return;
            int rank = Math.Max(1, (int)w.GetVar("rank", 1f));
            b.ApplyAura(priest, u, def, new AuraApplyInfo
            {
                Source = c.Ability, Rank = rank, EffLevel = AbilityRules.EffLevel(priest, c.Ability, rank), LearnLevel = c.Ability.learnLevel,
                Mods = AbilityMods.For(priest, c.Ability),
            });
            float left = w.GetVar("charges") - 1f;
            w.SetVar("charges", left);
            if (left <= 0f) b.Despawn(w, "depleted");
        }
    }

    /// <summary>
    /// Spirit of Redemption: instead of being downed the priest becomes a spirit for 10 s (only healing allowed), then dies.
    /// </summary>
    sealed class PriestSpiritOfRedemption : SpecialHandler
    {
        const string FormId = "priest_spirit_of_redemption";
        public PriestSpiritOfRedemption() : base("PriestSpiritOfRedemption") { }

        public override bool PreventDeath(Battle b, Unit u, Unit killer, int rank)
        {
            if (u.HasAura(FormId) || u.GetVar("sor_dying") > 0f || !b.InCombat || b.IsOver) return false;
            var form = b.Db.Aura(FormId);
            if (form == null) return false;
            u.Health = 1f;
            b.CancelPending(u, "spirit of redemption", null, 0f);
            b.StopAutoAttack(u);
            RemoveWhere(b, u, a => IsHarmful(a), AuraRemoveReason.Dispelled);
            b.ApplyAura(u, u, form, new AuraApplyInfo { EffLevel = u.Level, LearnLevel = 1, Duration = form.duration });
            return true;
        }

        public override string CannotUse(Unit u, int rank, AbilityDef a, Unit target)
        {
            if (!u.HasAura(FormId)) return null;
            bool heal = AbilityMods.HasTag(a, "Heal") || a.aiHint == "Heal";
            if (!heal) foreach (var e in a.effects) if (e.type == EffectType.Heal) { heal = true; break; }
            return heal && !Specials.UsesSpecial(a, "HelpUp") ? null : "Only healing spells can be cast in spirit form.";
        }

        public override void OnAnyAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason)
        {
            if (a.Def.id != FormId || reason == AuraRemoveReason.Death || !b.InCombat || b.IsOver) return;
            var u = a.Bearer;
            if (!u.IsAlive) return;
            u.SetVar("sor_dying", 1f);
            try { b.KillUnit(u, null, false); }
            finally { u.Vars.Remove("sor_dying"); }
        }

        public override void OnBattleFinished(Battle b)
        {
            foreach (var u in b.Units)
            {
                var f = u.FindAura(FormId);
                if (f != null) b.RemoveAura(f, AuraRemoveReason.Cancelled);
            }
        }
    }

    /// <summary>Vampiric Embrace: the caster's Shadow damage to the bearer heals the caster's allies within 30 yd for 20%.</summary>
    sealed class PriestVampiricEmbrace : SpecialHandler
    {
        public PriestVampiricEmbrace() : base("PriestVampiricEmbrace") { }
        public override void OnBearerDamaged(Battle b, AuraInstance a, Unit src, float amount, School s, DamageInfo info)
        {
            var caster = a.Caster;
            if (caster == null || src != caster || s != School.Shadow || amount <= 0f) return;
            float heal = amount * 0.2f * a.EffectMult;
            if (heal <= 0f) return;
            float r = MathUtil.Yd(30f);
            foreach (var ally in new List<Unit>(b.Units))
            {
                if (!ally.IsAlive || ally.IsTotem || ally.Team != caster.Team || caster.DistanceTo(ally) > r + ally.Radius) continue;
                b.HealUnit(caster, ally, heal, new HealInfo { Name = a.Def.name, NoThreat = true, Periodic = true });
            }
        }
    }

    /// <summary>Fear Ward: the next Fear effect is not applied (Immune) and the ward is consumed.</summary>
    sealed class PriestFearWard : SpecialHandler
    {
        public PriestFearWard() : base("PriestFearWard") { }
        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming)
        {
            if (holder == null || !Imposes(incoming, UnitState.Fear)) return null;
            b.RemoveAura(holder, AuraRemoveReason.Consumed);
            return "Immune";
        }
    }

    /// <summary>Focused Casting (Martyrdom): pending casts ignore pushback; +10% per Martyrdom rank to resist interrupts.</summary>
    sealed class PriestFocusedCasting : SpecialHandler
    {
        public PriestFocusedCasting() : base("PriestFocusedCasting") { }
        public override float PushbackResistChance(Unit u, int rank, AbilityDef pending) => 100f;
        public override float InterruptResistChance(Unit u, int rank)
        {
            var a = u.FindAura("priest_focused_casting");
            return a != null ? 10f * Math.Max(1, a.Rank) : 0f;
        }
    }

    /// <summary>Healing Focus: values[rank]% chance for pending heals to ignore pushback.</summary>
    sealed class PriestPushbackResist : SpecialHandler
    {
        public PriestPushbackResist() : base("PriestPushbackResist") { }
        public override float PushbackResistChance(Unit u, int rank, AbilityDef pending)
        {
            float v = PV(rank);
            return IsHeal(pending) ? v : 0f;
        }

        internal static bool IsHeal(AbilityDef a)
        {
            if (AbilityMods.HasTag(a, "Heal") || a.aiHint == "Heal") return true;
            foreach (var e in a.effects) if (e.type == EffectType.Heal) return true;
            return false;
        }
    }

    /// <summary>Unbreakable Will: values[rank]% to resist stun, fear and silence effects.</summary>
    sealed class PriestUnbreakableWill : SpecialHandler
    {
        public PriestUnbreakableWill() : base("PriestUnbreakableWill") { }
        public override string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming)
        {
            if (holder != null) return null;
            float v = PV(rank);
            if (!ImposesAny(incoming, UnitState.Stun, UnitState.Fear, UnitState.Silence)) return null;
            return b.Rng.Chance(v) ? "Resist" : null;
        }
    }

    /// <summary>Spiritual Guidance: spell damage and healing power + values[rank]% of Spirit.</summary>
    sealed class PriestSpiritualGuidance : SpecialHandler
    {
        public PriestSpiritualGuidance() : base("PriestSpiritualGuidance") { }
        public override void AdjustStats(Unit u, int rank, UnitStats s)
        {
            float bonus = s.Spirit * PV(rank) / 100f;
            s.ExtraSpellDamage += bonus;
            s.HealingPower += bonus;
        }
    }

    /// <summary>
    /// Blessed Recovery: a physical crit taken stores 8/16/25% of its damage in a pool on priest_blessed_recovery; the
    /// aura's three ticks heal pool / remaining ticks (no coefficient, no crit).
    /// </summary>
    sealed class PriestBlessedRecovery : SpecialHandler
    {
        static readonly float[] Pct = { 8f, 16f, 25f };
        const string AuraId = "priest_blessed_recovery";
        public PriestBlessedRecovery() : base("PriestBlessedRecovery") { }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var info = c.ProcInfo;
            var u = c.Caster;
            if (info == null || info.Damage <= 0f || info.School != School.Physical) return;
            var def = c.Battle.Db.Aura(AuraId);
            if (def == null) return;
            float add = info.Damage * Pct[MathUtil.Clamp(c.Rank, 1, Pct.Length) - 1] / 100f;
            var inst = c.Battle.ApplyAura(u, u, def, new AuraApplyInfo { EffLevel = u.Level, LearnLevel = 1, Duration = def.duration });
            if (inst == null) return;
            inst.SetVar("pool", inst.GetVar("pool") + add);
            inst.SetVar("ticks", Math.Max(1f, (float)Math.Round(def.duration / Math.Max(0.1f, def.tickInterval))));
        }

        public override bool ReplaceEffect(AbilityCast c, EffectDef e, Unit t)
        {
            var a = c.SourceAura;
            if (a == null || a.Def.id != AuraId) return true;
            float ticks = Math.Max(1f, a.GetVar("ticks", 1f));
            float pool = a.GetVar("pool");
            float heal = pool / ticks;
            a.SetVar("pool", pool - heal);
            a.SetVar("ticks", Math.Max(1f, ticks - 1f));
            if (heal > 0f) c.Battle.HealUnit(a.Bearer, a.Bearer, heal, new HealInfo { Name = a.Def.name, Periodic = true });
            return true;
        }
    }
}
