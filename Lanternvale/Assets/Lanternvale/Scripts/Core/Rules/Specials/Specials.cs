// Named code handlers for unique mechanics ("special" fields in abilities, effects, auras, talents, items).
// Every special must be documented in a data `specials` array; the validator asks IsImplemented(name).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    /// <summary>
    /// Base class of a special handler. Override only the hooks the mechanic needs. The same handler may be
    /// referenced from an ability (`special`), an effect (`type: Special`), an aura (`special`), a talent or an item
    /// passive (`type: Special`). Passive hooks receive the talent rank (1 for items/auras/abilities).
    /// </summary>
    public abstract class SpecialHandler
    {
        public readonly string Name;
        protected SpecialHandler(string name) { Name = name; }

        // ---- ability-level (AbilityDef.special)
        /// <summary>Extra usability check; return a reason to block the ability.</summary>
        public virtual string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) => null;
        /// <summary>Replace the default target validation (return null to keep it).</summary>
        public virtual UseCheck? ValidateTarget(Battle b, Unit u, AbilityDef a, Unit target, Vec2? point) => null;
        /// <summary>Override the Time cost of the ability.</summary>
        public virtual float? TimeCost(Unit u, AbilityDef a) => null;
        /// <summary>Before the cast starts (combo points captured, costs not yet paid).</summary>
        public virtual void BeforeUse(AbilityCast c) { }
        /// <summary>When the ability resolves, before its effects. Set c.SkipEffects to replace them.</summary>
        public virtual void Resolve(AbilityCast c) { }
        /// <summary>After the ability's effects.</summary>
        public virtual void After(AbilityCast c) { }
        public virtual float ModifyDamage(AbilityCast c, EffectDef e, Unit t, float v) => v;
        public virtual float ModifyHealing(AbilityCast c, EffectDef e, Unit t, float v) => v;
        public virtual float ModifyResourceGain(AbilityCast c, EffectDef e, Unit t, float v) => v;

        // ---- effect-level (EffectType.Special)
        public virtual void Execute(AbilityCast c, EffectDef e, Unit t) { }

        // ---- aura-level (AuraDef.special)
        public virtual void OnAuraApplied(Battle b, AuraInstance a) { }
        public virtual void OnAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason) { }
        /// <summary>Periodic tick; return true to replace the default tickEffects.</summary>
        public virtual bool OnAuraTick(Battle b, AuraInstance a, AbilityCast c) => false;

        // ---- passive hooks (talent Special passives, item Special passives, auras/abilities with this special)
        public virtual void ContributeStats(Unit u, int rank, StatBlock block) { }
        public virtual void AdjustStats(Unit u, int rank, UnitStats s) { }
        public virtual void ContributeAbilityMods(Unit u, int rank, AbilityDef a, AbilityModSet set) { }
        public virtual float ModifyCost(Unit u, int rank, AbilityDef a, float cost) => cost;
        public virtual float ModifyCastTime(Unit u, int rank, AbilityDef a, float t) => t;
        public virtual float OffHandMultiplier(Unit u, int rank) => 1f;
        public virtual void OnProc(Battle b, ProcTrigger trigger, Unit owner, Unit other, ProcInfo info, int rank) { }
        public virtual void OnKill(Battle b, Unit killer, Unit victim, DamageInfo info, int rank) { }
        public virtual bool PreventDeath(Battle b, Unit u, Unit killer, int rank) => false;
        public virtual float ModifyOutgoingDamage(Battle b, Unit u, int rank, AbilityCast c, EffectDef e, Unit t, float v) => v;

        // ---- summons
        public virtual void OnSummoned(AbilityCast c, Unit summoned) { }
    }

    /// <summary>Registry of special handlers. The harness/validator calls <see cref="IsImplemented"/> by reflection.</summary>
    public static partial class Specials
    {
        static readonly Dictionary<string, SpecialHandler> handlers = new Dictionary<string, SpecialHandler>(StringComparer.Ordinal);

        static Specials()
        {
            RegisterBuiltIns();
        }

        public static void Register(SpecialHandler h)
        {
            if (h == null || string.IsNullOrEmpty(h.Name)) return;
            handlers[h.Name] = h;
        }

        /// <summary>True when a handler with this name exists.</summary>
        public static bool IsImplemented(string name) => !string.IsNullOrEmpty(name) && handlers.ContainsKey(name);

        public static SpecialHandler Get(string name) => !string.IsNullOrEmpty(name) && handlers.TryGetValue(name, out var h) ? h : null;

        public static IEnumerable<string> Names => handlers.Keys;

        // ============================================================ passive sources

        struct PassiveRef { public SpecialHandler H; public int Rank; }

        static readonly List<PassiveRef> scratch = new List<PassiveRef>();

        /// <summary>Handlers acting passively on a unit: talent and item Special passives, auras and passive abilities with a special.</summary>
        static List<PassiveRef> PassivesOf(Unit u)
        {
            var list = new List<PassiveRef>();
            if (u == null) return list;
            var db = u.Db;
            if (db != null && u.Talents.Count > 0)
                foreach (var kv in u.Talents)
                {
                    if (kv.Value <= 0) continue;
                    var t = db.Talent(kv.Key);
                    if (t == null) continue;
                    foreach (var p in t.effects)
                        if (p.type == "Special" && !string.Equals(p.target, "Pet", StringComparison.OrdinalIgnoreCase))
                        {
                            var h = Get(p.special);
                            if (h != null) list.Add(new PassiveRef { H = h, Rank = kv.Value });
                        }
                }
            if (db != null && u.Owner != null && u.Owner.Talents.Count > 0)
                foreach (var kv in u.Owner.Talents)
                {
                    if (kv.Value <= 0) continue;
                    var t = db.Talent(kv.Key);
                    if (t == null) continue;
                    foreach (var p in t.effects)
                        if (p.type == "Special" && string.Equals(p.target, "Pet", StringComparison.OrdinalIgnoreCase))
                        {
                            var h = Get(p.special);
                            if (h != null) list.Add(new PassiveRef { H = h, Rank = kv.Value });
                        }
                }
            foreach (var kv in u.Equipment.Equipped)
                foreach (var p in kv.Value.Def.equipEffects)
                    if (p.type == "Special")
                    {
                        var h = Get(p.special);
                        if (h != null) list.Add(new PassiveRef { H = h, Rank = 1 });
                    }
            foreach (var a in u.Auras)
            {
                if (string.IsNullOrEmpty(a.Def.special)) continue;
                var h = Get(a.Def.special);
                if (h != null) list.Add(new PassiveRef { H = h, Rank = Math.Max(1, a.Stacks) });
            }
            return list;
        }

        // ================================================================ dispatch

        internal static void ContributeStats(Unit u, StatBlock b)
        {
            if (handlers.Count == 0) return;
            foreach (var p in PassivesOf(u)) p.H.ContributeStats(u, p.Rank, b);
        }

        internal static void AdjustStats(Unit u, UnitStats s)
        {
            if (handlers.Count == 0) return;
            foreach (var p in PassivesOf(u)) p.H.AdjustStats(u, p.Rank, s);
        }

        internal static void ContributeAbilityMods(Unit u, AbilityDef a, AbilityModSet set)
        {
            foreach (var p in PassivesOf(u)) p.H.ContributeAbilityMods(u, p.Rank, a, set);
        }

        internal static float ModifyCost(Unit u, AbilityDef a, float cost)
        {
            foreach (var p in PassivesOf(u)) cost = p.H.ModifyCost(u, p.Rank, a, cost);
            return cost;
        }

        internal static float ModifyCastTime(Unit u, AbilityDef a, float t)
        {
            foreach (var p in PassivesOf(u)) t = p.H.ModifyCastTime(u, p.Rank, a, t);
            var h = Get(a.special);
            if (h != null) t = h.ModifyCastTime(u, 1, a, t);
            return Math.Max(0f, t);
        }

        internal static float? TimeCost(Unit u, AbilityDef a) => Get(a.special)?.TimeCost(u, a);

        internal static float OffHandMultiplier(Unit u)
        {
            float m = 1f;
            foreach (var p in PassivesOf(u)) m *= p.H.OffHandMultiplier(u, p.Rank);
            return m;
        }

        internal static string CheckUse(Battle b, Unit u, AbilityDef a, Unit target) => Get(a.special)?.CheckUse(b, u, a, target);

        internal static UseCheck? ValidateTarget(Battle b, Unit u, AbilityDef a, Unit target, Vec2? point) => Get(a.special)?.ValidateTarget(b, u, a, target, point);

        internal static void OnBeforeUse(AbilityCast c) => Get(c.Ability?.special)?.BeforeUse(c);

        internal static void ResolveAbility(AbilityCast c) => Get(c.Ability?.special)?.Resolve(c);

        internal static void AfterAbility(AbilityCast c) => Get(c.Ability?.special)?.After(c);

        internal static float ModifyDamage(AbilityCast c, EffectDef e, Unit t, float v)
        {
            var h = c.Ability != null ? Get(c.Ability.special) : null;
            if (h != null && c.SourceAura == null) v = h.ModifyDamage(c, e, t, v);
            var ah = c.SourceAura != null ? Get(c.SourceAura.Def.special) : null;
            if (ah != null) v = ah.ModifyDamage(c, e, t, v);
            foreach (var p in PassivesOf(c.Caster)) v = p.H.ModifyOutgoingDamage(c.Battle, c.Caster, p.Rank, c, e, t, v);
            return v;
        }

        internal static float ModifyHealing(AbilityCast c, EffectDef e, Unit t, float v)
        {
            var h = c.Ability != null ? Get(c.Ability.special) : null;
            if (h != null && c.SourceAura == null) v = h.ModifyHealing(c, e, t, v);
            return v;
        }

        internal static float ModifyResourceGain(AbilityCast c, EffectDef e, Unit t, float v)
        {
            var h = c.Ability != null ? Get(c.Ability.special) : null;
            if (h != null && c.SourceAura == null) v = h.ModifyResourceGain(c, e, t, v);
            return v;
        }

        internal static void ExecuteEffect(string name, AbilityCast c, EffectDef e, Unit t)
        {
            var h = Get(name);
            if (h == null)
            {
                c.Battle.Log($"(special '{name}' is not implemented)", c.Caster);
                return;
            }
            h.Execute(c, e, t);
        }

        internal static void OnAuraApplied(Battle b, AuraInstance a) => Get(a.Def.special)?.OnAuraApplied(b, a);
        internal static void OnAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason r) => Get(a.Def.special)?.OnAuraRemoved(b, a, r);
        internal static bool OnAuraTick(Battle b, AuraInstance a, AbilityCast c) => Get(a.Def.special)?.OnAuraTick(b, a, c) ?? false;

        internal static void OnProcTrigger(Battle b, ProcTrigger trigger, Unit owner, Unit other, ProcInfo info)
        {
            if (handlers.Count == 0) return;
            foreach (var p in PassivesOf(owner)) p.H.OnProc(b, trigger, owner, other, info, p.Rank);
        }

        internal static void OnKill(Battle b, Unit killer, Unit victim, DamageInfo info)
        {
            foreach (var p in PassivesOf(killer)) p.H.OnKill(b, killer, victim, info, p.Rank);
            // auras on the victim placed by the killer's side (Drain Soul style) get a say as well
            if (info?.Cast?.Ability != null) Get(info.Cast.Ability.special)?.OnKill(b, killer, victim, info, 1);
            if (info?.Cast?.SourceAura != null) Get(info.Cast.SourceAura.Def.special)?.OnKill(b, killer, victim, info, 1);
        }

        internal static bool PreventDeath(Battle b, Unit u, Unit killer, DamageInfo info)
        {
            foreach (var p in PassivesOf(u)) if (p.H.PreventDeath(b, u, killer, p.Rank)) return true;
            return false;
        }

        internal static void OnSummoned(AbilityCast c, Unit summoned) => Get(c.Ability?.special)?.OnSummoned(c, summoned);

        /// <summary>Talent rank of the first talent of the unit that uses the named Special passive (0 if none).</summary>
        public static int TalentRankOfSpecial(Unit u, string special)
        {
            if (u?.Db == null) return 0;
            foreach (var kv in u.Talents)
            {
                var t = u.Db.Talent(kv.Key);
                if (t == null) continue;
                foreach (var p in t.effects) if (p.type == "Special" && p.special == special) return kv.Value;
            }
            return 0;
        }

        // ================================================================ built-ins

        static void RegisterBuiltIns()
        {
            Register(new ShootSpecial());
            Register(new HelpUpSpecial());
            RegisterClassSpecials();
        }

        static partial void RegisterClassSpecials();
    }

    /// <summary>Wand shot: Time = wand speed; wand damage and school; no AP; spell crit (×1.5).</summary>
    sealed class ShootSpecial : SpecialHandler
    {
        public ShootSpecial() : base("Shoot") { }

        public override float? TimeCost(Unit u, AbilityDef a)
        {
            var w = StatCalculator.GetWeapon(u, WeaponSlot.Ranged);
            return w.Valid ? w.Speed / Math.Max(0.1f, u.Stats.RangedHaste) : 1.5f;
        }

        public override void Resolve(AbilityCast c)
        {
            var w = StatCalculator.GetWeapon(c.Caster, WeaponSlot.Ranged);
            if (w.Valid) c.School = w.School == School.Physical ? School.Arcane : w.School;
        }
    }

    /// <summary>Help a downed ally within melee reach back up with 1 health.</summary>
    sealed class HelpUpSpecial : SpecialHandler
    {
        public HelpUpSpecial() : base("HelpUp") { }

        public override UseCheck? ValidateTarget(Battle b, Unit u, AbilityDef a, Unit target, Vec2? point)
        {
            if (target == null) return UseCheck.Fail(UseFailure.NoTarget, "Select a downed ally.");
            if (!target.IsFriendlyTo(u) || target == u) return UseCheck.Fail(UseFailure.InvalidTarget, "Select a downed ally.");
            if (!target.Downed) return UseCheck.Fail(UseFailure.InvalidTarget, "That ally is not downed.");
            if (!b.InMeleeReachPublic(u, target)) return UseCheck.Fail(UseFailure.Range, "Move next to the downed ally.");
            return UseCheck.Pass;
        }

        public override void Execute(AbilityCast c, EffectDef e, Unit t)
        {
            var target = t ?? c.Target;
            if (target == null || !target.Downed) return;
            c.Battle.Revive(target, 1f, 0f, c.Caster, "Help");
        }
    }
}
