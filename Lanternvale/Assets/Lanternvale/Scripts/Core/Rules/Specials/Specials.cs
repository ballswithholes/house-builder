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
    /// <summary>What a content special (dialogue/encounter outcome) may do to the game state; implemented by the session.</summary>
    public interface IContentContext
    {
        bool GetFlag(string flag);
        void SetFlag(string flag, bool value);
        /// <summary>Raises a named session event (UI/presentation reacts, e.g. relight every lantern on the map).</summary>
        void RaiseEvent(string name, string arg = "");
    }

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
        /// <summary>Multiplier on the threat caused by this ability's damage.</summary>
        public virtual float ThreatMultiplier(AbilityCast c) => 1f;

        // ---- effect-level (EffectType.Special, or `special` set on any other effect as a modifier)
        public virtual void Execute(AbilityCast c, EffectDef e, Unit t) { }
        /// <summary>Modifier specials: return true when the effect was fully handled (default skipped).</summary>
        public virtual bool ReplaceEffect(AbilityCast c, EffectDef e, Unit t) => false;
        /// <summary>Modifier specials on ApplyAura: duration of the aura to apply.</summary>
        public virtual float ModifyAuraDuration(AbilityCast c, EffectDef e, Unit t, AuraDef aura, float duration) => duration;
        /// <summary>Modifier specials on ApplyAura: called after the aura was applied/refreshed.</summary>
        public virtual void AfterAuraEffect(AbilityCast c, EffectDef e, Unit t, AuraInstance inst) { }
        /// <summary>Modifier specials: the attack cannot be dodged, parried or blocked.</summary>
        public virtual bool Unavoidable(AbilityCast c, EffectDef e) => false;

        // ---- aura-level (AuraDef.special, called for the aura instance)
        public virtual void OnAuraApplied(Battle b, AuraInstance a) { }
        /// <summary>An existing instance was refreshed/stacked by a new application (mod values and duration were reset).</summary>
        public virtual void OnAuraRefreshed(Battle b, AuraInstance a) { }
        public virtual void OnAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason) { }
        /// <summary>Periodic tick; return true to replace the default tickEffects.</summary>
        public virtual bool OnAuraTick(Battle b, AuraInstance a, AbilityCast c) => false;
        /// <summary>The bearer's turn starts.</summary>
        public virtual void OnBearerTurnStart(Battle b, AuraInstance a) { }
        /// <summary>Incoming damage on the bearer after armor/resistance/block, before absorbs.</summary>
        public virtual float ModifyIncomingDamage(Battle b, AuraInstance a, Unit src, float amount, School school, DamageInfo info) => amount;
        public virtual float ModifyIncomingHeal(Battle b, AuraInstance a, Unit src, float amount, HealInfo info) => amount;
        /// <summary>Extra crit chance (points) for effects of school <paramref name="s"/> against the bearer.</summary>
        public virtual float IncomingCritBonus(AuraInstance a, AbilityCast c, School s) => 0f;
        /// <summary>Flat damage bonus added to damage effects against the bearer (before crit).</summary>
        public virtual float IncomingFlatDamageBonus(AuraInstance a, AbilityCast c, EffectDef e, School s, bool weapon) => 0f;
        /// <summary>Flat healing added to heal effects on the bearer (before multipliers and crit).</summary>
        public virtual float IncomingFlatHealBonus(AuraInstance a, AbilityCast c, EffectDef e) => 0f;
        /// <summary>Extra ranged attack power for ranged attacks against the bearer.</summary>
        public virtual float IncomingRangedApBonus(AuraInstance a, Unit attacker) => 0f;
        /// <summary>True: the aura's absorb is not a depleting shield (handled by ModifyIncomingDamage).</summary>
        public virtual bool SkipAbsorbPool(AuraInstance a) => false;
        /// <summary>The bearer cannot hide from <paramref name="observer"/>'s team.</summary>
        public virtual bool RevealsTo(AuraInstance a, Unit observer) => false;
        /// <summary>Redirect a hostile single-target spell (Grounding Totem). Return the new target or null.</summary>
        public virtual Unit RedirectSpell(Battle b, AuraInstance a, AbilityCast c) => null;
        /// <summary>The bearer was downed or killed (self-resurrection offers).</summary>
        public virtual void OnHolderDowned(Battle b, AuraInstance a) { }
        /// <summary>Called when a unit moves within range of the bearer: return >0 trigger radius (metres) for movement triggers.</summary>
        public virtual float MovementTriggerRadius(AuraInstance a) => 0f;
        public virtual void OnMovementTrigger(Battle b, AuraInstance a, Unit mover) { }
        /// <summary>
        /// One of the aura's own data procs is about to fire (before its chance roll and charge use). Return false to
        /// suppress it for this event (it does not fire and consumes no charge). <paramref name="other"/> is the other unit
        /// of the event (the attacker for OnStruck).
        /// </summary>
        public virtual bool AllowsAuraProc(Battle b, AuraInstance a, ProcDef p, ProcTrigger trigger, Unit other, ProcInfo info) => true;

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
        public virtual float ModifyCritChance(Battle b, Unit u, int rank, AbilityCast c, Unit t, School s, float chance) => chance;
        /// <summary>Passive of the target: modify the crit chance of an attack against <paramref name="target"/>.</summary>
        public virtual float ModifyIncomingCritChance(Unit target, int rank, AbilityCast c, float chance) => chance;
        /// <summary>Passive of the target: extra percentage points for an attack of hit table <paramref name="kind"/> to miss <paramref name="target"/>.</summary>
        public virtual float IncomingMissChance(Unit target, int rank, AttackKind kind) => 0f;
        /// <summary>Extra percentage points for a proc of an aura on the unit (Improved Poisons).</summary>
        public virtual float ProcChanceBonus(Unit u, int rank, AuraInstance aura, ProcDef p) => 0f;
        /// <summary>Extra crit damage bonus (fraction, 0.01 = +1% of the base bonus... added to the bonus) against the target.</summary>
        public virtual float CritBonusAdd(Unit u, int rank, Unit t) => 0f;
        /// <summary>Resist an incoming hostile aura: return "Immune"/"Resist" or null. <paramref name="holder"/> is the aura granting this (null for talents).</summary>
        public virtual string ResistIncomingAura(Battle b, Unit target, int rank, AuraInstance holder, Unit caster, AuraDef incoming) => null;
        public virtual float RageFromDamageTakenMult(Unit u, int rank) => 1f;
        public virtual bool IgnoresState(Unit u, int rank, UnitState s) => false;
        public virtual bool GrantsWeapon(Unit u, int rank, WeaponType w) => false;
        /// <summary>The unit starts using an ability (cast time already computed). Consume "next cast" charges here.</summary>
        public virtual void OnAbilityStart(Battle b, Unit u, int rank, AbilityCast c, float castTime) { }
        public virtual void OnChannelTick(AbilityCast c, int rank) { }
        public virtual void OnEffectCrit(AbilityCast c, EffectDef e, Unit t, float amount, bool heal, int rank) { }
        public virtual void OnAuraAppliedByMe(Battle b, Unit caster, int rank, AuraInstance inst) { }
        public virtual void OnDamageDealt(Battle b, Unit src, int rank, Unit tgt, float amount, DamageInfo info, bool viaMinion) { }
        public virtual void OnSpellResisted(Battle b, Unit victim, int rank, Unit attacker, AbilityCast c) { }
        /// <summary>Percent chance to ignore one casting pushback for the pending ability.</summary>
        public virtual float PushbackResistChance(Unit u, int rank, AbilityDef pending) => 0f;
        /// <summary>Fraction by which pushback is reduced (0.35 = 35% shorter).</summary>
        public virtual float PushbackReduction(Unit u, int rank) => 0f;
        /// <summary>Percent chance to reflect a hostile single-target spell back to its caster.</summary>
        public virtual float ReflectChance(Unit u, int rank, AbilityCast incoming) => 0f;
        public virtual float ManaPerDamageMult(Unit u, int rank, AuraInstance shield) => 1f;
        /// <summary>Multiplier on the flat (non weapon roll) bonus of a WeaponDamage effect.</summary>
        public virtual float WeaponFlatBonusMult(AbilityCast c, int rank, EffectDef e) => 1f;
        /// <summary>The unit's pet changed (summoned, dismissed, died).</summary>
        public virtual void OnPetChanged(Battle b, Unit owner, int rank) { }

        public virtual float InterruptResistChance(Unit u, int rank) => 0f;
        /// <summary>Extra usability rule from a passive (e.g. Spirit of Redemption form): return a reason to block.</summary>
        public virtual string CannotUse(Unit u, int rank, AbilityDef a, Unit target) => null;
        /// <summary>Multiplier on enemies' stealth detection radius against this (stealthed) unit.</summary>
        public virtual float DetectionRadiusMult(Unit stealthed, int rank) => 1f;
        /// <summary>Aura-level: the bearer took damage (after it was applied).</summary>
        public virtual void OnBearerDamaged(Battle b, AuraInstance a, Unit src, float amount, School s, DamageInfo info) { }
        /// <summary>Aura-level: the bearer cannot flee (AI cowards stay, no voluntary retreat).</summary>
        public virtual bool PreventsFleeing(AuraInstance a) => false;

        // ---- content specials (dialogue/encounter outcomes run by the session layer)
        /// <summary>Runs a content special (dialogue outcome etc.). Return false when the handler is not a content special.</summary>
        public virtual bool RunContent(IContentContext ctx) => false;

        // ---- global hooks (called on every registered handler)
        public virtual void OnAnyTurnStart(Battle b, Unit u) { }
        public virtual void OnAnyUnitMoved(Battle b, Unit u) { }
        public virtual void OnAnyAuraApplied(Battle b, AuraInstance a) { }
        public virtual void OnAnyAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason reason) { }
        /// <summary>An aura's mod values were recomputed from data (save restore): re-apply special scaling.</summary>
        public virtual void OnModValuesRefreshed(AuraInstance a) { }
        public virtual void OnAnyUnitFell(Battle b, Unit u) { }
        public virtual void OnAnyAbilityStart(Battle b, Unit u, AbilityCast c) { }
        public virtual void OnBattleFinished(Battle b) { }
        /// <summary>Out-of-combat real-time tick of a unit (field context).</summary>
        public virtual void OnOutOfCombatTick(Battle b, Unit u, float seconds) { }
        /// <summary>Abilities the unit may use because of its surroundings (Lightwell).</summary>
        public virtual void ContextualAbilities(Battle b, Unit u, List<string> into) { }

        // ---- summons
        public virtual void OnSummoned(AbilityCast c, Unit summoned) { }
        /// <summary>Passive hook: a unit owned by <paramref name="owner"/> was summoned.</summary>
        public virtual void OnOwnerSummoned(Battle b, Unit owner, int rank, Unit summoned) { }
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

        /// <summary>
        /// A passive source of a handler. Reading <see cref="H"/> publishes the source as
        /// <see cref="CurrentPassive"/>/<see cref="CurrentSource"/> so a handler shared by several talents
        /// (e.g. Monster/Humanoid Slaying) knows which entry it is serving. Read them first thing in the hook.
        /// </summary>
        struct PassiveRef
        {
            public SpecialHandler Handler;
            public int Rank;
            public PassiveDef Def;
            public string Source;
            public SpecialHandler H { get { currentPassive = Def; currentSource = Source; return Handler; } }
        }

        static PassiveDef currentPassive;
        static string currentSource;

        /// <summary>The talent/item passive entry whose hook is running (null for aura/ability sources).</summary>
        public static PassiveDef CurrentPassive => currentPassive;
        /// <summary>Id of the talent, item or aura whose passive hook is running.</summary>
        public static string CurrentSource => currentSource;

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
                            if (h != null) list.Add(new PassiveRef { Handler = h, Rank = kv.Value, Def = p, Source = t.id });
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
                            if (h != null) list.Add(new PassiveRef { Handler = h, Rank = kv.Value, Def = p, Source = t.id });
                        }
                }
            foreach (var kv in u.Equipment.Equipped)
                foreach (var p in kv.Value.Def.equipEffects)
                    if (p.type == "Special")
                    {
                        var h = Get(p.special);
                        if (h != null) list.Add(new PassiveRef { Handler = h, Rank = 1, Def = p, Source = kv.Value.Def.id });
                    }
            foreach (var a in u.Auras)
            {
                if (string.IsNullOrEmpty(a.Def.special)) continue;
                var h = Get(a.Def.special);
                if (h != null) list.Add(new PassiveRef { Handler = h, Rank = Math.Max(1, a.Stacks), Source = a.Def.id });
            }
            return list;
        }

        /// <summary>Talent (own) and item Special passives only (no auras).</summary>
        static List<PassiveRef> TalentAndItemPassives(Unit u)
        {
            var list = new List<PassiveRef>();
            if (u?.Db == null) return list;
            foreach (var kv in u.Talents)
            {
                if (kv.Value <= 0) continue;
                var t = u.Db.Talent(kv.Key);
                if (t == null) continue;
                foreach (var p in t.effects)
                    if (p.type == "Special" && !string.Equals(p.target, "Pet", StringComparison.OrdinalIgnoreCase))
                    {
                        var h = Get(p.special);
                        if (h != null) list.Add(new PassiveRef { Handler = h, Rank = kv.Value, Def = p, Source = t.id });
                    }
            }
            foreach (var kv in u.Equipment.Equipped)
                foreach (var p in kv.Value.Def.equipEffects)
                    if (p.type == "Special")
                    {
                        var h = Get(p.special);
                        if (h != null) list.Add(new PassiveRef { Handler = h, Rank = 1, Def = p, Source = kv.Value.Def.id });
                    }
            return list;
        }

        // ================================================================ dispatch

        static SpecialHandler EffectHandler(EffectDef e) => e != null && !string.IsNullOrEmpty(e.special) ? Get(e.special) : null;

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

        /// <summary>Usability rule of the ability's special and of the specials of its effects (e.g. stance swap, Mind Control).</summary>
        internal static string CheckUse(Battle b, Unit u, AbilityDef a, Unit target)
        {
            var h = Get(a.special);
            var why = h?.CheckUse(b, u, a, target);
            if (why != null) return why;
            foreach (var e in a.effects)
            {
                if (string.IsNullOrEmpty(e.special)) continue;
                var eh = Get(e.special);
                if (eh == null || eh == h) continue;
                why = eh.CheckUse(b, u, a, target);
                if (why != null) return why;
            }
            return null;
        }

        /// <summary>Target rule of the ability's special, else of the specials of its effects (help_up carries HelpUp on its effect).</summary>
        internal static UseCheck? ValidateTarget(Battle b, Unit u, AbilityDef a, Unit target, Vec2? point)
        {
            var h = Get(a.special);
            var r = h?.ValidateTarget(b, u, a, target, point);
            if (r.HasValue) return r;
            foreach (var e in a.effects)
            {
                if (string.IsNullOrEmpty(e.special)) continue;
                var eh = Get(e.special);
                if (eh == null || eh == h) continue;
                r = eh.ValidateTarget(b, u, a, target, point);
                if (r.HasValue) return r;
            }
            return null;
        }

        /// <summary>True when the ability uses the named special, on the ability itself or on one of its effects.</summary>
        internal static bool UsesSpecial(AbilityDef a, string name)
        {
            if (a == null || string.IsNullOrEmpty(name)) return false;
            if (a.special == name) return true;
            foreach (var e in a.effects) if (e.special == name) return true;
            return false;
        }

        internal static void OnBeforeUse(AbilityCast c) => Get(c.Ability?.special)?.BeforeUse(c);

        internal static void ResolveAbility(AbilityCast c) => Get(c.Ability?.special)?.Resolve(c);

        internal static void AfterAbility(AbilityCast c) => Get(c.Ability?.special)?.After(c);

        internal static float ThreatMultiplier(AbilityCast c)
        {
            if (c?.Ability == null) return 1f;
            var h = Get(c.Ability.special);
            return h != null ? h.ThreatMultiplier(c) : 1f;
        }

        internal static float ModifyDamage(AbilityCast c, EffectDef e, Unit t, float v)
        {
            // the ability's special also sees the periodic ticks of auras it applied (tick casts carry Ability = source ability)
            var h = c.Ability != null && (c.SourceAura == null || c.SourceAura.SourceAbility == c.Ability) && c.SourceProc == null ? Get(c.Ability.special) : null;
            if (h != null) v = h.ModifyDamage(c, e, t, v);
            var eh = EffectHandler(e);
            if (eh != null && eh != h) v = eh.ModifyDamage(c, e, t, v);
            var ah = c.SourceAura != null ? Get(c.SourceAura.Def.special) : null;
            if (ah != null && ah != eh) v = ah.ModifyDamage(c, e, t, v);
            foreach (var p in PassivesOf(c.Caster)) v = p.H.ModifyOutgoingDamage(c.Battle, c.Caster, p.Rank, c, e, t, v);
            return v;
        }

        internal static float ModifyHealing(AbilityCast c, EffectDef e, Unit t, float v)
        {
            var h = c.Ability != null && (c.SourceAura == null || c.SourceAura.SourceAbility == c.Ability) && c.SourceProc == null ? Get(c.Ability.special) : null;
            if (h != null) v = h.ModifyHealing(c, e, t, v);
            var ah = c.SourceAura != null ? Get(c.SourceAura.Def.special) : null;
            if (ah != null && ah != h && ah != EffectHandler(e)) v = ah.ModifyHealing(c, e, t, v);
            var eh = EffectHandler(e);
            if (eh != null && eh != h) v = eh.ModifyHealing(c, e, t, v);
            return v;
        }

        internal static float ModifyResourceGain(AbilityCast c, EffectDef e, Unit t, float v)
        {
            var h = c.Ability != null && c.SourceAura == null ? Get(c.Ability.special) : null;
            if (h != null) v = h.ModifyResourceGain(c, e, t, v);
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

        internal static bool ReplaceEffect(AbilityCast c, EffectDef e, Unit t) => EffectHandler(e)?.ReplaceEffect(c, e, t) ?? false;

        internal static float ModifyAuraDuration(AbilityCast c, EffectDef e, Unit t, AuraDef aura, float d)
        {
            var h = EffectHandler(e);
            return h != null ? h.ModifyAuraDuration(c, e, t, aura, d) : d;
        }

        internal static void AfterAuraEffect(AbilityCast c, EffectDef e, Unit t, AuraInstance inst) => EffectHandler(e)?.AfterAuraEffect(c, e, t, inst);

        internal static bool Unavoidable(AbilityCast c, EffectDef e) =>
            (EffectHandler(e)?.Unavoidable(c, e) ?? false) || (c.Ability != null && c.SourceAura == null && (Get(c.Ability.special)?.Unavoidable(c, e) ?? false));

        internal static void OnAuraApplied(Battle b, AuraInstance a) => Get(a.Def.special)?.OnAuraApplied(b, a);
        internal static void OnAuraRefreshed(Battle b, AuraInstance a) => Get(a.Def.special)?.OnAuraRefreshed(b, a);

        internal static float ProcChanceBonus(Unit u, AuraInstance aura, ProcDef p)
        {
            float v = 0f;
            foreach (var r in PassivesOf(u)) v += r.H.ProcChanceBonus(u, r.Rank, aura, p);
            return v;
        }

        /// <summary>
        /// Raised by specials whose effect lives outside combat rules (Pick Lock completed, Mind Soothe on an encounter
        /// creature). Arguments: battle/field, acting unit, special name, target unit (may be null).
        /// </summary>
        public static event Action<Battle, Unit, string, Unit> FieldEvent;
        internal static void RaiseFieldEvent(Battle b, Unit u, string name, Unit target) => FieldEvent?.Invoke(b, u, name, target);
        internal static void OnAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason r) => Get(a.Def.special)?.OnAuraRemoved(b, a, r);
        internal static bool OnAuraTick(Battle b, AuraInstance a, AbilityCast c) => Get(a.Def.special)?.OnAuraTick(b, a, c) ?? false;

        // ---- aura-level queries on a unit's auras
        struct AuraRef { public SpecialHandler H; public AuraInstance A; }

        static List<AuraRef> SpecialAuras(Unit u)
        {
            var list = new List<AuraRef>();
            if (u == null) return list;
            foreach (var a in u.Auras)
            {
                if (string.IsNullOrEmpty(a.Def.special)) continue;
                var h = Get(a.Def.special);
                if (h != null) list.Add(new AuraRef { H = h, A = a });
            }
            return list;
        }

        internal static void OnBearerTurnStart(Battle b, Unit u)
        {
            foreach (var r in SpecialAuras(u)) if (u.Auras.Contains(r.A)) r.H.OnBearerTurnStart(b, r.A);
        }

        internal static float IncomingDamage(Battle b, Unit tgt, Unit src, float amount, School s, DamageInfo info)
        {
            foreach (var r in SpecialAuras(tgt))
            {
                if (amount <= 0) break;
                if (tgt.Auras.Contains(r.A)) amount = r.H.ModifyIncomingDamage(b, r.A, src, amount, s, info);
            }
            return Math.Max(0f, amount);
        }

        internal static float IncomingHeal(Battle b, Unit tgt, Unit src, float amount, HealInfo info)
        {
            foreach (var r in SpecialAuras(tgt)) amount = r.H.ModifyIncomingHeal(b, r.A, src, amount, info);
            return Math.Max(0f, amount);
        }

        internal static float CritChanceBonus(AbilityCast c, Unit t, School s, float chance)
        {
            if (t != null) foreach (var r in SpecialAuras(t)) chance += r.H.IncomingCritBonus(r.A, c, s);
            foreach (var p in PassivesOf(c.Caster)) chance = p.H.ModifyCritChance(c.Battle, c.Caster, p.Rank, c, t, s, chance);
            if (t != null) foreach (var p in PassivesOf(t)) chance = p.H.ModifyIncomingCritChance(t, p.Rank, c, chance);
            return chance;
        }

        /// <summary>
        /// Extra percentage points for an attack of hit table <paramref name="kind"/> to miss <paramref name="target"/>, from the
        /// target's passives (Heightened Senses: spells and ranged attacks). Added to the miss chance before the hit cap.
        /// </summary>
        public static float IncomingMissChance(Unit target, AttackKind kind)
        {
            if (target == null || handlers.Count == 0) return 0f;
            float v = 0f;
            foreach (var p in PassivesOf(target)) v += p.H.IncomingMissChance(target, p.Rank, kind);
            return v;
        }

        /// <summary>False when the special of the aura suppresses one of its data procs for this event (Retaliation from behind).</summary>
        internal static bool AuraProcAllowed(Battle b, AuraInstance a, ProcDef p, ProcTrigger trigger, Unit other, ProcInfo info)
        {
            var h = Get(a.Def.special);
            return h == null || h.AllowsAuraProc(b, a, p, trigger, other, info);
        }

        internal static float CritBonusAdd(Unit u, Unit t)
        {
            float v = 0f;
            foreach (var p in PassivesOf(u)) v += p.H.CritBonusAdd(u, p.Rank, t);
            return v;
        }

        internal static float IncomingFlatDamageBonus(AbilityCast c, EffectDef e, Unit t, School s, bool weapon)
        {
            float v = 0f;
            if (t != null) foreach (var r in SpecialAuras(t)) v += r.H.IncomingFlatDamageBonus(r.A, c, e, s, weapon);
            return v;
        }

        internal static float IncomingFlatHealBonus(AbilityCast c, EffectDef e, Unit t)
        {
            float v = 0f;
            if (t != null) foreach (var r in SpecialAuras(t)) v += r.H.IncomingFlatHealBonus(r.A, c, e);
            return v;
        }

        internal static float IncomingRangedApBonus(Unit attacker, Unit t)
        {
            float v = 0f;
            if (t != null) foreach (var r in SpecialAuras(t)) v += r.H.IncomingRangedApBonus(r.A, attacker);
            return v;
        }

        internal static bool SkipAbsorbPool(AuraInstance a) => Get(a.Def.special)?.SkipAbsorbPool(a) ?? false;

        internal static bool RevealedTo(Unit target, Unit observer)
        {
            foreach (var r in SpecialAuras(target)) if (r.H.RevealsTo(r.A, observer)) return true;
            return false;
        }

        internal static Unit RedirectSpell(Battle b, AbilityCast c)
        {
            foreach (var u in b.Units)
            {
                if (!u.IsAlive) continue;
                foreach (var r in SpecialAuras(u))
                {
                    var t = r.H.RedirectSpell(b, r.A, c);
                    if (t != null) return t;
                }
            }
            return null;
        }

        internal static void OnHolderDowned(Battle b, Unit u)
        {
            foreach (var r in SpecialAuras(u)) r.H.OnHolderDowned(b, r.A);
            foreach (var p in PassivesOf(u)) { }
        }

        /// <summary>Movement triggers (traps): returns the aura whose trigger radius the point enters, or null.</summary>
        internal static AuraInstance MovementTriggerAt(Battle b, Unit mover, Vec2 p)
        {
            foreach (var u in b.Units)
            {
                if (!u.IsAlive || !u.IsHostileTo(mover)) continue;
                foreach (var r in SpecialAuras(u))
                {
                    float rad = r.H.MovementTriggerRadius(r.A);
                    if (rad > 0 && Vec2.Distance(u.Position, p) <= rad + mover.Radius) return r.A;
                }
            }
            return null;
        }

        internal static void OnMovementTrigger(Battle b, AuraInstance a, Unit mover) => Get(a.Def.special)?.OnMovementTrigger(b, a, mover);

        // ---- passive queries
        internal static string ResistIncomingAura(Battle b, Unit target, Unit caster, AuraDef incoming)
        {
            foreach (var r in SpecialAuras(target))
            {
                var why = r.H.ResistIncomingAura(b, target, 1, r.A, caster, incoming);
                if (why != null) return why;
            }
            if (target.Db != null)
                foreach (var p in TalentAndItemPassives(target))
                {
                    var why = p.H.ResistIncomingAura(b, target, p.Rank, null, caster, incoming);
                    if (why != null) return why;
                }
            return null;
        }

        internal static float RageFromDamageTakenMult(Unit u)
        {
            float m = 1f;
            foreach (var p in PassivesOf(u)) m *= p.H.RageFromDamageTakenMult(u, p.Rank);
            return m;
        }

        internal static bool IgnoresState(Unit u, UnitState s)
        {
            foreach (var p in PassivesOf(u)) if (p.H.IgnoresState(u, p.Rank, s)) return true;
            return false;
        }

        internal static bool GrantsWeapon(Unit u, WeaponType w)
        {
            foreach (var p in TalentAndItemPassives(u)) if (p.H.GrantsWeapon(u, p.Rank, w)) return true;
            return false;
        }

        internal static void OnAbilityStart(Battle b, Unit u, AbilityCast c, float castTime)
        {
            foreach (var p in PassivesOf(u)) p.H.OnAbilityStart(b, u, p.Rank, c, castTime);
        }

        internal static void OnChannelTick(AbilityCast c)
        {
            foreach (var p in PassivesOf(c.Caster)) p.H.OnChannelTick(c, p.Rank);
        }

        internal static void OnEffectCrit(AbilityCast c, EffectDef e, Unit t, float amount, bool heal)
        {
            foreach (var p in PassivesOf(c.Caster)) p.H.OnEffectCrit(c, e, t, amount, heal, p.Rank);
        }

        internal static void OnAuraAppliedByMe(Battle b, Unit caster, AuraInstance inst)
        {
            if (caster == null) return;
            foreach (var p in PassivesOf(caster)) p.H.OnAuraAppliedByMe(b, caster, p.Rank, inst);
        }

        internal static void OnDamageDealt(Battle b, Unit src, Unit tgt, float amount, DamageInfo info)
        {
            if (src == null) return;
            foreach (var p in PassivesOf(src)) p.H.OnDamageDealt(b, src, p.Rank, tgt, amount, info, false);
            if (src.Owner != null && src.Owner != src)
                foreach (var p in TalentAndItemPassives(src.Owner)) p.H.OnDamageDealt(b, src, p.Rank, tgt, amount, info, true);
        }

        internal static void OnSpellResisted(Battle b, Unit victim, Unit attacker, AbilityCast c)
        {
            foreach (var p in PassivesOf(victim)) p.H.OnSpellResisted(b, victim, p.Rank, attacker, c);
        }

        internal static float PushbackResistChance(Unit u, AbilityDef pending)
        {
            float v = 0f;
            foreach (var p in PassivesOf(u)) v += p.H.PushbackResistChance(u, p.Rank, pending);
            return Math.Min(100f, v);
        }

        internal static float PushbackReduction(Unit u)
        {
            float v = 0f;
            foreach (var p in PassivesOf(u)) v = Math.Max(v, p.H.PushbackReduction(u, p.Rank));
            return MathUtil.Clamp(v, 0f, 1f);
        }

        internal static float ReflectChance(Unit u, AbilityCast incoming)
        {
            float v = 0f;
            foreach (var p in PassivesOf(u)) v += p.H.ReflectChance(u, p.Rank, incoming);
            return Math.Min(100f, v);
        }

        internal static float ManaPerDamageMult(Unit u, AuraInstance shield)
        {
            float m = 1f;
            foreach (var p in PassivesOf(u)) m *= p.H.ManaPerDamageMult(u, p.Rank, shield);
            return m;
        }

        internal static float WeaponFlatBonusMult(AbilityCast c, EffectDef e)
        {
            float m = 1f;
            foreach (var p in PassivesOf(c.Caster)) m *= p.H.WeaponFlatBonusMult(c, p.Rank, e);
            return m;
        }

        internal static void OnPetChanged(Battle b, Unit owner)
        {
            if (owner == null) return;
            foreach (var p in PassivesOf(owner)) p.H.OnPetChanged(b, owner, p.Rank);
        }

        internal static void OnProcTrigger(Battle b, ProcTrigger trigger, Unit owner, Unit other, ProcInfo info)
        {
            if (handlers.Count == 0) return;
            foreach (var p in PassivesOf(owner)) p.H.OnProc(b, trigger, owner, other, info, p.Rank);
        }

        internal static void OnKill(Battle b, Unit killer, Unit victim, DamageInfo info)
        {
            if (killer != null) foreach (var p in PassivesOf(killer)) p.H.OnKill(b, killer, victim, info, p.Rank);
            var ah = info?.Cast?.Ability != null ? Get(info.Cast.Ability.special) : null;
            ah?.OnKill(b, killer, victim, info, 1);
            var sh = info?.Cast?.SourceAura != null ? Get(info.Cast.SourceAura.Def.special) : null;
            if (sh != null && sh != ah) sh.OnKill(b, killer, victim, info, 1);
        }

        internal static bool PreventDeath(Battle b, Unit u, Unit killer, DamageInfo info)
        {
            foreach (var p in PassivesOf(u)) if (p.H.PreventDeath(b, u, killer, p.Rank)) return true;
            return false;
        }

        internal static void OnSummoned(AbilityCast c, Unit summoned)
        {
            Get(c.Ability?.special)?.OnSummoned(c, summoned);
            if (c.Caster != null) foreach (var p in PassivesOf(c.Caster)) p.H.OnOwnerSummoned(c.Battle, c.Caster, p.Rank, summoned);
        }

        internal static float InterruptResistChance(Unit u)
        {
            float v = 0f;
            foreach (var p in PassivesOf(u)) v += p.H.InterruptResistChance(u, p.Rank);
            return Math.Min(100f, v);
        }

        internal static string CannotUse(Unit u, AbilityDef a, Unit target)
        {
            foreach (var p in PassivesOf(u))
            {
                var why = p.H.CannotUse(u, p.Rank, a, target);
                if (why != null) return why;
            }
            return null;
        }

        /// <summary>Aura ids whose bearer's pending casts ignore casting pushback (Power Word: Shield).</summary>
        public static readonly HashSet<string> PushbackImmuneAuras = new HashSet<string>();

        /// <summary>Aura ids that make the bearer undetectable even within the stealth detection distance (Vanish).</summary>
        public static readonly HashSet<string> UndetectableAuras = new HashSet<string>();

        internal static float DetectionRadiusMult(Unit stealthed)
        {
            foreach (var a in stealthed.Auras) if (UndetectableAuras.Contains(a.Def.id)) return 0f;
            float m = 1f;
            foreach (var p in PassivesOf(stealthed)) m *= p.H.DetectionRadiusMult(stealthed, p.Rank);
            return Math.Max(0f, m);
        }

        internal static void OnBearerDamaged(Battle b, Unit tgt, Unit src, float amount, School s, DamageInfo info)
        {
            foreach (var r in SpecialAuras(tgt)) if (tgt.Auras.Contains(r.A)) r.H.OnBearerDamaged(b, r.A, src, amount, s, info);
        }

        static List<SpecialHandler> All()
        {
            var l = new List<SpecialHandler>(handlers.Values);
            return l;
        }

        internal static void OnAnyTurnStart(Battle b, Unit u) { foreach (var h in All()) h.OnAnyTurnStart(b, u); }
        internal static void OnAnyUnitMoved(Battle b, Unit u) { foreach (var h in All()) h.OnAnyUnitMoved(b, u); }
        internal static void OnAnyAuraApplied(Battle b, AuraInstance a) { foreach (var h in All()) h.OnAnyAuraApplied(b, a); }
        internal static void OnAnyAuraRemoved(Battle b, AuraInstance a, AuraRemoveReason r) { foreach (var h in All()) h.OnAnyAuraRemoved(b, a, r); }
        internal static void OnModValuesRefreshed(AuraInstance a) { if (handlers.Count > 0) foreach (var h in All()) h.OnModValuesRefreshed(a); }

        /// <summary>True when an aura on the unit forbids fleeing (Judgement of Justice).</summary>
        public static bool CannotFlee(Unit u)
        {
            foreach (var r in SpecialAuras(u)) if (r.H.PreventsFleeing(r.A)) return true;
            return false;
        }

        /// <summary>
        /// Runs a content special (dialogue/encounter outcome such as RekindleLanterns) against the session's context.
        /// Returns false when no content handler with that name exists.
        /// </summary>
        public static bool RunContentSpecial(string name, IContentContext ctx)
        {
            var h = Get(name);
            return h != null && ctx != null && h.RunContent(ctx);
        }
        internal static void OnAnyUnitFell(Battle b, Unit u) { foreach (var h in All()) h.OnAnyUnitFell(b, u); }
        internal static void OnAnyAbilityStart(Battle b, Unit u, AbilityCast c) { foreach (var h in All()) h.OnAnyAbilityStart(b, u, c); }
        internal static void OnBattleFinished(Battle b) { foreach (var h in All()) h.OnBattleFinished(b); }
        internal static void OnOutOfCombatTick(Battle b, Unit u, float s) { foreach (var h in All()) h.OnOutOfCombatTick(b, u, s); }

        /// <summary>Abilities usable by the unit because of its surroundings (e.g. Lightwell renew next to a Lightwell).</summary>
        public static List<string> ContextualAbilities(Battle b, Unit u)
        {
            var l = new List<string>();
            foreach (var h in All()) h.ContextualAbilities(b, u, l);
            return l;
        }

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

        /// <summary>The talent passive entry (and rank) of the unit that uses the named Special passive, or null.</summary>
        public static PassiveDef TalentPassive(Unit u, string special, out int rank, string talentId = null)
        {
            rank = 0;
            if (u?.Db == null) return null;
            foreach (var kv in u.Talents)
            {
                if (kv.Value <= 0 || (talentId != null && kv.Key != talentId)) continue;
                var t = u.Db.Talent(kv.Key);
                if (t == null) continue;
                foreach (var p in t.effects)
                    if (p.type == "Special" && p.special == special) { rank = kv.Value; return p; }
            }
            return null;
        }

        /// <summary>Per-rank value of a passive: values[rank-1] or value × rank.</summary>
        public static float RankValue(PassiveDef p, int rank) => p == null ? 0f : StatCalculator.RankValue(p.value, p.values, rank);

        // ================================================================ built-ins

        static void RegisterBuiltIns()
        {
            Register(new ShootSpecial());
            Register(new HelpUpSpecial());
            Register(new WeaponTypeTalentSpecial());
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
            if (!b.InMeleeRange(u, target)) return UseCheck.Fail(UseFailure.Range, "Move next to the downed ally.");
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
