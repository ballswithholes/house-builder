// Player/AI actions: usability checks (with human-readable reasons), ability use, movement, items, waiting.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum UseFailure
    {
        None, Unknown, Dead, NotYourTurn, Casting, Passive, Controlled, Shapeshifted, Silenced, Pacified, Locked, Forbidden,
        Disarmed, Cooldown, NoTime, Resource, ComboPoints, Requirement, NoTarget, InvalidTarget, Range, TooClose, LineOfSight,
        AlreadyQueued, Special,
    }

    /// <summary>Result of a usability check: Ok, or a failure code and a sentence for the UI.</summary>
    public struct UseCheck
    {
        public bool Ok;
        public UseFailure Code;
        public string Reason;
        public static readonly UseCheck Pass = new UseCheck { Ok = true, Code = UseFailure.None, Reason = "" };
        public static UseCheck Fail(UseFailure code, string reason) => new UseCheck { Ok = false, Code = code, Reason = reason };
        public override string ToString() => Ok ? "OK" : $"{Code}: {Reason}";
    }

    public struct ActionResult
    {
        public bool Ok;
        public string Reason;
        public static readonly ActionResult Success = new ActionResult { Ok = true, Reason = "" };
        public static ActionResult Fail(string reason) => new ActionResult { Ok = false, Reason = reason };
        public override string ToString() => Ok ? "OK" : Reason;
    }

    /// <summary>One entry of a unit's action bar / spellbook with its current state.</summary>
    public sealed class AbilityStatus
    {
        public AbilityDef Ability;
        public int Rank, MaxRank;
        /// <summary>Usable now (ignoring target and range).</summary>
        public bool Usable;
        public UseFailure Code;
        public string Reason = "";
        public float Cost;
        public ResourceType CostType;
        public float TimeCost, CastTime, Cooldown, CooldownLeft;
        public bool NeedsTarget, NeedsPoint;
        /// <summary>Stance/aura/seal from this ability is active, auto attack running, or swing queued.</summary>
        public bool Active;
        public string Tooltip = "";
        public override string ToString() => $"{Ability.name} r{Rank} {(Usable ? "ready" : Reason)}";
    }

    public sealed partial class Battle
    {
        // ===================================================== usability checks

        /// <summary>Checks everything except target and range (for the action bar).</summary>
        public UseCheck CanUseIgnoringTarget(Unit u, AbilityDef a, bool fromItem = false) => CheckUse(u, a, null, null, fromItem, false);

        /// <summary>Full usability check for an ability on a target unit and/or ground point.</summary>
        public UseCheck CanUse(Unit u, string abilityId, Unit target = null, Vec2? point = null, bool fromItem = false)
        {
            var a = Db.Ability(abilityId);
            if (a == null) return UseCheck.Fail(UseFailure.Unknown, $"Unknown ability '{abilityId}'.");
            return CheckUse(u, a, target, point, fromItem, true);
        }

        public UseCheck CanUse(Unit u, AbilityDef a, Unit target = null, Vec2? point = null, bool fromItem = false) => CheckUse(u, a, target, point, fromItem, true);

        UseCheck CheckUse(Unit u, AbilityDef a, Unit target, Vec2? point, bool fromItem, bool checkTarget)
        {
            if (u == null || a == null) return UseCheck.Fail(UseFailure.Unknown, "Nothing to use.");
            if (u.Dead) return UseCheck.Fail(UseFailure.Dead, "You are dead.");
            if (u.Downed) return UseCheck.Fail(UseFailure.Dead, "You are downed.");
            if (InCombat && Started)
            {
                if (IsOver) return UseCheck.Fail(UseFailure.NotYourTurn, "The battle is over.");
                if (ActiveUnit != u && actingOutOfTurn != u) return UseCheck.Fail(UseFailure.NotYourTurn, "It is not your turn.");
            }
            if (u.Pending != null) return UseCheck.Fail(UseFailure.Casting, $"Already casting {u.Pending.Ability.name}.");
            if (!fromItem && !KnowsForUse(u, a)) return UseCheck.Fail(UseFailure.Unknown, $"You do not know {a.name}.");
            if (a.passive) return UseCheck.Fail(UseFailure.Passive, $"{a.name} is passive.");

            // control states
            if (u.HasState(UnitState.Stun)) return UseCheck.Fail(UseFailure.Controlled, "You are stunned.");
            if (u.HasState(UnitState.Polymorph)) return UseCheck.Fail(UseFailure.Controlled, "You are polymorphed.");
            if (u.HasState(UnitState.Incapacitate)) return UseCheck.Fail(UseFailure.Controlled, "You are incapacitated.");
            if (u.HasState(UnitState.Sleep)) return UseCheck.Fail(UseFailure.Controlled, "You are asleep.");
            if (u.HasState(UnitState.Banish)) return UseCheck.Fail(UseFailure.Controlled, "You are banished.");
            if (u.HasState(UnitState.Fear)) return UseCheck.Fail(UseFailure.Controlled, "You are feared.");
            if (u.HasState(UnitState.Confuse)) return UseCheck.Fail(UseFailure.Controlled, "You are confused.");
            if (u.HasStateAura(UnitState.Shapeshift) && !IsShapeshiftAbility(a))
                return UseCheck.Fail(UseFailure.Shapeshifted, "Cannot do that while shapeshifted.");
            bool spell = AbilityRules.IsSpell(a) && a.special != "Shoot" && !a.autoAttack;
            if (spell && u.HasState(UnitState.Silence)) return UseCheck.Fail(UseFailure.Silenced, "You are silenced.");
            if (!spell && !fromItem && !a.autoAttack && a.school == School.Physical && a.special != "HelpUp" && u.HasState(UnitState.Pacify))
                return UseCheck.Fail(UseFailure.Pacified, "You are pacified.");
            if (a.school != School.Physical && u.IsSchoolLocked(a.school))
                return UseCheck.Fail(UseFailure.Locked, $"{a.school} spells are locked out.");
            if (u.IsSchoolForbidden(a.school)) return UseCheck.Fail(UseFailure.Forbidden, $"Cannot use {a.school} abilities in this form.");
            if (u.HasStateAura(UnitState.Disarm) && (AbilityRules.HasWeaponEffect(a) && !AbilityRules.IsRangedWeaponAbility(a)) && !a.autoAttack)
                return UseCheck.Fail(UseFailure.Disarmed, "You are disarmed.");

            // cooldown / time
            float cd = u.CooldownLeft(a);
            if (cd > 1e-3f) return UseCheck.Fail(UseFailure.Cooldown, $"{a.name} is not ready ({cd:0.#} s).");
            var mods = AbilityMods.For(u, a);
            int rank = AbilityRules.UsedRank(u, a);
            if (InCombat && Started)
            {
                float tc = AbilityRules.TimeCost(u, a, mods);
                if (tc > 0f && u.TimeLeft <= 1e-3f) return UseCheck.Fail(UseFailure.NoTime, "No time left this turn.");
            }
            if (a.nextSwing && u.QueuedSwing == a.id) return UseCheck.Fail(UseFailure.AlreadyQueued, $"{a.name} is already queued.");

            // cost
            float cost = AbilityRules.ResourceCost(u, a, rank, mods);
            if (cost > 0f)
            {
                var r = a.cost.type;
                if (u.GetResource(r) + 1e-3f < cost) return UseCheck.Fail(UseFailure.Resource, $"Not enough {r.ToString().ToLowerInvariant()} ({cost:0}).");
            }
            if (a.cost != null && a.cost.health > 0 && u.Health <= a.cost.health) return UseCheck.Fail(UseFailure.Resource, "Not enough health.");

            // requirements
            var req = CheckRequirements(u, a, target, checkTarget);
            if (!req.Ok) return req;

            // specials
            var sp = Specials.CheckUse(this, u, a, target) ?? Specials.CannotUse(u, a, target);
            if (sp != null) return UseCheck.Fail(UseFailure.Special, sp);

            if (!checkTarget) return UseCheck.Pass;
            return CheckTarget(u, a, target, point, mods);
        }

        bool KnowsForUse(Unit u, AbilityDef a)
        {
            if (u.Knows(a.id)) return true;
            if (a.id == "attack" || a.id == "help_up") return true;
            if (u.Class != null && a.id == u.Class.basicAttack) return true;
            return Specials.ContextualAbilities(this, u).Contains(a.id);
        }

        static bool IsShapeshiftAbility(AbilityDef a) =>
            AbilityMods.HasTag(a, "Shapeshift") || AbilityMods.HasTag(a, "Form") || AbilityMods.HasTag(a, "Stance");

        UseCheck CheckRequirements(Unit u, AbilityDef a, Unit target, bool checkTarget)
        {
            var r = a.requires;
            if (r == null) return UseCheck.Pass;
            if (r.casterAuras.Length > 0)
            {
                bool ok = false;
                foreach (var id in r.casterAuras) if (u.HasAura(id)) { ok = true; break; }
                if (!ok) return UseCheck.Fail(UseFailure.Requirement, "Requires " + NamesOfAuras(r.casterAuras) + ".");
            }
            if (r.casterAuraTags.Length > 0)
            {
                bool ok = false;
                foreach (var t in r.casterAuraTags) if (u.HasAuraWithTag(t)) { ok = true; break; }
                if (!ok) return UseCheck.Fail(UseFailure.Requirement, $"Requires an active {string.Join(" or ", r.casterAuraTags)}.");
            }
            if (r.casterStates.Length > 0)
            {
                bool ok = false;
                foreach (var s in r.casterStates)
                    if (Enum.TryParse<UnitState>(s, true, out var st) && u.HasStateAura(st)) { ok = true; break; }
                if (!ok) return UseCheck.Fail(UseFailure.Requirement, $"Requires {string.Join(" or ", r.casterStates)}.");
            }
            if (r.notInCombat && InCombat) return UseCheck.Fail(UseFailure.Requirement, "Cannot be used in combat.");
            if (r.inCombat && !InCombat) return UseCheck.Fail(UseFailure.Requirement, "Can only be used in combat.");
            if (r.mainHand.Length > 0)
            {
                var mh = u.Equipment.MainHand;
                bool ok = false;
                if (mh != null && mh.IsWeapon)
                    foreach (var w in r.mainHand)
                        if (string.Equals(w, mh.Def.weaponType.ToString(), StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                if (u.Class == null) ok = true;
                if (!ok) return UseCheck.Fail(UseFailure.Requirement, $"Requires a {string.Join(" or ", r.mainHand)} in your main hand.");
            }
            if (r.shield && u.Class != null && !u.Equipment.HasShield) return UseCheck.Fail(UseFailure.Requirement, "Requires a shield.");
            if (r.rangedWeapon)
            {
                bool ok = u.Class != null ? u.Equipment.HasRangedWeapon : (u.Creature != null && u.Creature.ranged);
                if (!ok) return UseCheck.Fail(UseFailure.Requirement, a.special == "Shoot" ? "Requires a wand." : "Requires a ranged weapon.");
                if (a.special == "Shoot" && u.Class != null && !u.Equipment.HasWand) return UseCheck.Fail(UseFailure.Requirement, "Requires a wand.");
            }
            if (r.meleeWeapon && u.Class != null && !u.Equipment.HasMeleeWeapon) return UseCheck.Fail(UseFailure.Requirement, "Requires a melee weapon.");
            if (r.dualWield && u.Class != null && !u.Equipment.IsDualWielding) return UseCheck.Fail(UseFailure.Requirement, "Requires dual wielding.");
            if (r.hasPet && (u.Pet == null || !u.Pet.IsAlive)) return UseCheck.Fail(UseFailure.Requirement, "Requires an active pet.");
            if (r.noPet && u.Pet != null && !u.Pet.Dead) return UseCheck.Fail(UseFailure.Requirement, "You already have a pet.");
            if (r.minHealthPct > 0 && u.HealthPct < r.minHealthPct) return UseCheck.Fail(UseFailure.Requirement, $"Requires at least {r.minHealthPct:0}% health.");
            if (!string.IsNullOrEmpty(r.reactive))
            {
                if (!HasReactive(u, r.reactive)) return UseCheck.Fail(UseFailure.Requirement, ReactiveText(r.reactive));
            }
            int needCp = Math.Max(r.minComboPoints, a.cost != null && a.cost.consumesComboPoints ? 1 : 0);
            if (needCp > 0)
            {
                int cp = target != null && u.ComboTarget != null && target != u.ComboTarget ? 0 : u.ComboPoints;
                if (u.ComboTarget != null && !u.ComboTarget.IsAlive) cp = 0;
                if (cp < needCp) return UseCheck.Fail(UseFailure.ComboPoints, needCp == 1 ? "Requires combo points." : $"Requires {needCp} combo points.");
            }
            if (!checkTarget || target == null) return UseCheck.Pass;

            if (r.targetNotSelf && target == u) return UseCheck.Fail(UseFailure.InvalidTarget, "Cannot target yourself.");
            if (r.targetAuras.Length > 0)
            {
                bool ok = false;
                foreach (var id in r.targetAuras) if (target.HasAura(id, r.targetAuraFromSelf ? u : null)) { ok = true; break; }
                if (!ok) return UseCheck.Fail(UseFailure.Requirement, "Target must be affected by " + NamesOfAuras(r.targetAuras) + ".");
            }
            if (r.targetAuraTags.Length > 0)
            {
                bool ok = false;
                foreach (var t in r.targetAuraTags) if (target.HasAuraWithTag(t, r.targetAuraFromSelf ? u : null)) { ok = true; break; }
                if (!ok) return UseCheck.Fail(UseFailure.Requirement, $"Target must be affected by your {string.Join(" or ", r.targetAuraTags)}.");
            }
            if (r.behindTarget && !u.IsBehind(target)) return UseCheck.Fail(UseFailure.Requirement, "You must be behind your target.");
            if (r.targetHealthBelowPct > 0 && target.HealthPct >= r.targetHealthBelowPct)
                return UseCheck.Fail(UseFailure.Requirement, $"Target must be below {r.targetHealthBelowPct:0}% health.");
            if (r.targetCreatureTypes.Length > 0 && target != u && Array.IndexOf(r.targetCreatureTypes, TypeOf(target)) < 0)
                return UseCheck.Fail(UseFailure.InvalidTarget, $"Only works on {string.Join(", ", r.targetCreatureTypes)}.");
            // Sap-like openers (stealth + incapacitate, no damage) only work on enemies not yet engaged in the fight
            if (InCombat && Started && target.IsHostileTo(u) && target.Engaged && IsStealthIncapacitate(a))
                return UseCheck.Fail(UseFailure.InvalidTarget, "Target is already in combat.");
            if (r.outOfMeleeRange && u.DistanceTo(target) < MathUtil.Yd(8f) + target.Radius)
                return UseCheck.Fail(UseFailure.TooClose, "Target is too close.");
            return UseCheck.Pass;
        }

        bool IsStealthIncapacitate(AbilityDef a)
        {
            if (a.requires == null || Array.IndexOf(a.requires.casterStates, "Stealth") < 0) return false;
            bool incap = false;
            foreach (var e in a.effects)
            {
                if (e.type == EffectType.Damage || e.type == EffectType.WeaponDamage) return false;
                if (e.type == EffectType.ApplyAura)
                {
                    var d = Db.Aura(e.aura);
                    if (d != null && Array.IndexOf(d.states, UnitState.Incapacitate) >= 0) incap = true;
                }
            }
            return incap;
        }

        static CreatureType TypeOf(Unit t) => t.Creature != null && t.Class == null ? t.Creature.type : CreatureType.Humanoid;

        string NamesOfAuras(string[] ids)
        {
            var names = new List<string>();
            foreach (var id in ids) names.Add(Db.Aura(id)?.name ?? id);
            return string.Join(" or ", names);
        }

        static string ReactiveText(string r)
        {
            switch (r)
            {
                case "TargetDodged": return "Usable only after the target dodges.";
                case "TargetParried": return "Usable only after the target parries.";
                case "SelfDodged": return "Usable only after you dodge.";
                case "SelfParried": return "Usable only after you parry.";
                case "SelfBlocked": return "Usable only after you block.";
                case "SelfDodgedParriedBlocked": return "Usable only after you dodge, parry or block.";
                case "SelfCrit": return "Usable only after you score a critical strike.";
                default: return $"Usable only after {r}.";
            }
        }

        bool HasReactive(Unit u, string name)
        {
            if (u.Reactive.ContainsKey(name)) return true;
            if (name == "SelfDodgedParriedBlocked")
                return u.Reactive.ContainsKey("SelfDodged") || u.Reactive.ContainsKey("SelfParried") || u.Reactive.ContainsKey("SelfBlocked");
            return false;
        }

        internal void OpenReactive(Unit u, string name)
        {
            if (u == null || !u.IsAlive) return;
            u.Reactive[name] = u.TurnsTaken + 1;
            if (name == "SelfDodged" || name == "SelfParried" || name == "SelfBlocked") u.Reactive["SelfDodgedParriedBlocked"] = u.TurnsTaken + 1;
        }

        void ConsumeReactive(Unit u, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (name == "SelfDodgedParriedBlocked")
            {
                u.Reactive.Remove("SelfDodged"); u.Reactive.Remove("SelfParried"); u.Reactive.Remove("SelfBlocked");
            }
            u.Reactive.Remove(name);
        }

        /// <summary>Target validity, visibility and range for the ability's target type.</summary>
        UseCheck CheckTarget(Unit u, AbilityDef a, Unit target, Vec2? point, AbilityModSet mods)
        {
            var sp = Specials.ValidateTarget(this, u, a, target, point);
            if (sp.HasValue) return sp.Value;
            switch (a.target)
            {
                case TargetType.Self:
                    return UseCheck.Pass;
                case TargetType.Pet:
                    if (u.Pet == null) return UseCheck.Fail(UseFailure.NoTarget, "You have no pet.");
                    if (u.Pet.Dead && !HasEffect(a, EffectType.Resurrect)) return UseCheck.Fail(UseFailure.InvalidTarget, "Your pet is dead.");
                    return CheckRange(u, a, u.Pet, mods);
                case TargetType.Point:
                {
                    Vec2 p = point ?? (target != null ? target.Position : u.Position);
                    if (point == null && target == null && !a.area.centeredOnCaster) return UseCheck.Fail(UseFailure.NoTarget, "Choose a location.");
                    float range = a.range > 0 ? MathUtil.Yd(a.range * (1f + mods.RangePct / 100f)) : float.PositiveInfinity;
                    if (u.DistanceTo(p) > range + 0.05f) return UseCheck.Fail(UseFailure.Range, "Out of range.");
                    if (!Pathfinder.HasLineOfSight(u.Position, p)) return UseCheck.Fail(UseFailure.LineOfSight, "Target not in line of sight.");
                    return UseCheck.Pass;
                }
            }
            if (target == null) return UseCheck.Fail(UseFailure.NoTarget, a.target == TargetType.Enemy ? "Select an enemy." : "Select a target.");
            switch (a.target)
            {
                case TargetType.Enemy:
                    if (!target.IsHostileTo(u)) return UseCheck.Fail(UseFailure.InvalidTarget, "Invalid target: not an enemy.");
                    if (!target.IsAlive) return UseCheck.Fail(UseFailure.InvalidTarget, "Target is dead.");
                    if (target.IsUntargetable) return UseCheck.Fail(UseFailure.InvalidTarget, "Invalid target.");
                    if (!CanSee(u, target)) return UseCheck.Fail(UseFailure.InvalidTarget, "You cannot see that target.");
                    break;
                case TargetType.Ally:
                    if (!target.IsFriendlyTo(u)) return UseCheck.Fail(UseFailure.InvalidTarget, "Invalid target: not an ally.");
                    if (!target.IsAlive) return UseCheck.Fail(UseFailure.InvalidTarget, target.Downed ? "Target is downed." : "Target is dead.");
                    break;
                case TargetType.AllyOther:
                    if (!target.IsFriendlyTo(u) || target == u) return UseCheck.Fail(UseFailure.InvalidTarget, "Select another ally.");
                    if (!target.IsAlive) return UseCheck.Fail(UseFailure.InvalidTarget, target.Downed ? "Target is downed." : "Target is dead.");
                    break;
                case TargetType.Any:
                    if (!target.IsAlive) return UseCheck.Fail(UseFailure.InvalidTarget, "Target is dead.");
                    if (target.IsHostileTo(u) && (target.IsUntargetable || !CanSee(u, target))) return UseCheck.Fail(UseFailure.InvalidTarget, "Invalid target.");
                    break;
                case TargetType.DeadAlly:
                    if (!target.IsFriendlyTo(u)) return UseCheck.Fail(UseFailure.InvalidTarget, "Invalid target.");
                    if (!target.IsDeadOrDowned) return UseCheck.Fail(UseFailure.InvalidTarget, "Target is not dead.");
                    break;
            }
            return CheckRange(u, a, target, mods);
        }

        UseCheck CheckRange(Unit u, AbilityDef a, Unit target, AbilityModSet mods)
        {
            if (target == u) return UseCheck.Pass;
            float d = u.DistanceTo(target);
            float max = AbilityRules.RangeMetres(u, a, target, mods, Config);
            // creature ranged basic attacks use the creature's range, no dead zone
            if (a.autoAttack && u.Class == null && u.Creature != null && AbilityRules.IsRangedWeaponAbility(a))
                max = MathUtil.Yd(u.Creature.rangedRange) + target.Radius;
            if (d > max + 1e-3f) return UseCheck.Fail(UseFailure.Range, AbilityRules.UsesMeleeReach(a) ? "Target is not in melee range." : "Out of range.");
            float min = AbilityRules.MinRangeMetres(a);
            if (u.Class == null && a.autoAttack) min = 0f;
            if (min > 0 && d < min) return UseCheck.Fail(UseFailure.TooClose, "Target is too close.");
            if (!AbilityRules.UsesMeleeReach(a) && !Pathfinder.HasLineOfSight(u.Position, target.Position))
                return UseCheck.Fail(UseFailure.LineOfSight, "Target not in line of sight.");
            return UseCheck.Pass;
        }

        static bool HasEffect(AbilityDef a, EffectType t)
        {
            foreach (var e in a.effects) if (e.type == t) return true;
            return false;
        }

        // ======================================================== action bar

        /// <summary>Known, non-passive, non-hidden abilities with usability/cost/time/cooldown (target-independent).</summary>
        public List<AbilityStatus> GetAbilityBar(Unit u, bool includeHidden = false)
        {
            var list = new List<AbilityStatus>();
            var seen = new HashSet<string>();
            void Add(AbilityDef a)
            {
                if (a == null || !seen.Add(a.id)) return;
                if (a.passive || (a.hidden && !includeHidden)) return;
                list.Add(GetStatus(u, a));
            }
            if (u.Class != null) Add(Db.Ability(u.Class.basicAttack));
            foreach (var kv in u.Abilities) Add(Db.Ability(kv.Key));
            foreach (var id in Specials.ContextualAbilities(this, u))
            {
                var a = Db.Ability(id);
                if (a != null && seen.Add(a.id)) list.Add(GetStatus(u, a));
            }
            return list;
        }

        public AbilityStatus GetStatus(Unit u, AbilityDef a, bool fromItem = false)
        {
            var mods = AbilityMods.For(u, a);
            int rank = AbilityRules.UsedRank(u, a);
            var chk = CheckUse(u, a, null, null, fromItem, false);
            var st = new AbilityStatus
            {
                Ability = a,
                Rank = u.RankOf(a.id),
                MaxRank = AbilityRules.RankCount(a),
                Usable = chk.Ok,
                Code = chk.Code,
                Reason = chk.Reason ?? "",
                Cost = AbilityRules.ResourceCost(u, a, rank, mods),
                CostType = a.cost != null ? a.cost.type : ResourceType.None,
                TimeCost = AbilityRules.TimeCost(u, a, mods),
                CastTime = AbilityRules.CastTime(u, a, mods),
                Cooldown = AbilityRules.Cooldown(u, a, mods),
                CooldownLeft = u.CooldownLeft(a),
                NeedsTarget = a.target != TargetType.Self && a.target != TargetType.Point && a.target != TargetType.Pet,
                NeedsPoint = a.target == TargetType.Point,
            };
            st.Active = IsAbilityActive(u, a);
            st.Tooltip = Tooltip.Ability(u, a);
            return st;
        }

        /// <summary>True when the ability's toggle state is on (auto attack, queued swing, its aura active on the caster).</summary>
        public bool IsAbilityActive(Unit u, AbilityDef a)
        {
            if (a.autoAttack) return u.AutoAttacking && u.AutoAttackAbility == a.id;
            if (a.nextSwing) return u.QueuedSwing == a.id;
            foreach (var e in a.effects)
                if (e.type == EffectType.ApplyAura && e.target == EffectTarget.Self && u.HasAura(e.aura)) return true;
            if (a.target == TargetType.Self)
                foreach (var e in a.effects)
                    if (e.type == EffectType.ApplyAura && e.target == EffectTarget.Target && u.HasAura(e.aura, u)) return true;
            return false;
        }

        // ======================================================== ability use

        /// <summary>Uses an ability (by id) on a target unit and/or point. Returns why it failed, if it did.</summary>
        public ActionResult UseAbility(Unit u, string abilityId, Unit target = null, Vec2? point = null)
        {
            var a = Db.Ability(abilityId);
            if (a == null) return ActionResult.Fail($"Unknown ability '{abilityId}'.");
            return UseAbility(u, a, target, point, false, null);
        }

        /// <summary>Uses an item from the shared inventory (its `use` ability); consumes one when consumable.</summary>
        public ActionResult UseItem(Unit u, ItemInstance item, Unit target = null, Vec2? point = null)
        {
            if (item == null) return ActionResult.Fail("No item.");
            var a = Db.Ability(item.Def.use);
            if (a == null) return ActionResult.Fail($"{item.Name} cannot be used.");
            if (Inventory != null && !Inventory.Items.Contains(item)) return ActionResult.Fail("The item is not in your bags.");
            return UseAbility(u, a, target, point, true, item);
        }

        internal ActionResult UseAbility(Unit u, AbilityDef a, Unit target, Vec2? point, bool fromItem, ItemInstance item)
        {
            if (a.target == TargetType.Self) target = u;
            else if (a.target == TargetType.Pet) target = u.Pet;
            else if (a.target == TargetType.Point && point == null && target != null) point = target.Position;
            else if (a.target == TargetType.Point && point == null && a.area.centeredOnCaster) point = u.Position;
            var chk = CheckUse(u, a, target, point, fromItem, true);
            if (!chk.Ok) return ActionResult.Fail(chk.Reason);

            // toggles
            if (a.autoAttack)
            {
                StartAutoAttack(u, target, a, true);
                return ActionResult.Success;
            }
            if (a.nextSwing)
            {
                u.QueuedSwing = a.id;
                if (target != null && target.IsHostileTo(u)) StartAutoAttack(u, target, Db.Ability("attack"), false);
                Emit(new CombatEvent { Type = CombatEventType.SwingQueued, Source = u, Target = target, AbilityId = a.id, Name = a.name });
                return ActionResult.Success;
            }

            castSerial++;
            var mods = AbilityMods.For(u, a);
            int rank = AbilityRules.UsedRank(u, a);
            float timeCost = AbilityRules.TimeCost(u, a, mods);
            float castTime = AbilityRules.CastTime(u, a, mods);
            if (target != null && target.IsHostileTo(u)) u.Engaged = true;

            if (!string.IsNullOrEmpty(a.exclusiveGroup))
                foreach (var au in new List<AuraInstance>(u.Auras))
                    if (au.Def.exclusiveGroup == a.exclusiveGroup && au.Caster == u) RemoveAura(au, AuraRemoveReason.Replaced);
            if (a.breaksStealth) BreakOnAction(u);
            if (target != null && target != u) u.FaceTowards(target.Position);
            else if (point.HasValue) u.FaceTowards(point.Value);

            if (target != null && target.IsHostileTo(u))
            {
                if (AbilityRules.StartsMeleeAutoAttack(a)) StartAutoAttack(u, target, Db.Ability("attack"), false);
                else if (AbilityRules.StartsAutoShot(a) && u.Knows("auto_shot")) StartAutoAttack(u, target, Db.Ability("auto_shot"), false);
            }
            ConsumeReactive(u, a.requires?.reactive);

            var cast = NewCast(u, a, rank, target, point, mods);
            cast.SourceItem = item;
            if (a.cost != null && a.cost.consumesComboPoints) cast.ComboPoints = ComboPointsOn(u, target);
            Specials.OnBeforeUse(cast);
            Specials.OnAbilityStart(this, u, cast, castTime);
            Specials.OnAnyAbilityStart(this, u, cast);
            if (!u.IsAlive) return ActionResult.Success;
            MetersOf(u).Casts++;

            bool timed = InCombat && Started && u != actingOutOfTurn;
            if (a.channeled && a.channelTicks > 0)
            {
                PayCost(cast);
                StartCooldown(u, a, mods);
                ConsumeItem(cast);
                int ticks = a.channelTicks;
                int now = ticks;
                float channel = Math.Max(0.01f, castTime);
                Emit(new CombatEvent { Type = CombatEventType.CastStart, Source = u, Target = target, AbilityId = a.id, Name = a.name, Seconds = channel, Reason = "channel" });
                if (timed && u.TimeLeft + 1e-3f < channel)
                {
                    now = (int)Math.Floor(ticks * u.TimeLeft / channel + 1e-4f);
                    u.Pending = new PendingCast
                    {
                        Ability = a, Rank = rank, Target = target, Point = point ?? default, HasPoint = point.HasValue,
                        RemainingTime = channel - u.TimeLeft, Channel = true, TicksLeft = ticks - now, TicksTotal = ticks,
                        ComboPoints = cast.ComboPoints, StartRound = Round, ChannelDuration = channel,
                    };
                    u.TimeLeft = 0f;
                }
                else if (timed) SpendTime(u, timeCost);
                for (int i = 0; i < now && u.IsAlive; i++) ResolveChannelTick(cast, i, ticks);
                AfterCast(cast);
                if (u.Pending != null) { EndTurnInternal(u, false); return ActionResult.Success; }
                Emit(new CombatEvent { Type = CombatEventType.CastComplete, Source = u, Target = target, AbilityId = a.id, Name = a.name });
                return ActionResult.Success;
            }

            if (castTime > 0f && timed)
            {
                Emit(new CombatEvent { Type = CombatEventType.CastStart, Source = u, Target = target, AbilityId = a.id, Name = a.name, Seconds = castTime });
                if (u.TimeLeft + 1e-3f < castTime)
                {
                    u.Pending = new PendingCast
                    {
                        Ability = a, Rank = rank, Target = target, Point = point ?? default, HasPoint = point.HasValue,
                        RemainingTime = castTime - u.TimeLeft, ComboPoints = cast.ComboPoints, StartRound = Round, CostMult = cast.CostMult,
                    };
                    u.TimeLeft = 0f;
                    pendingItems[u] = item;
                    EndTurnInternal(u, false);
                    return ActionResult.Success;
                }
                SpendTime(u, timeCost);
            }
            else if (timed) SpendTime(u, timeCost);
            else if (castTime > 0f) Emit(new CombatEvent { Type = CombatEventType.CastStart, Source = u, Target = target, AbilityId = a.id, Name = a.name, Seconds = castTime });

            ResolveCast(cast);
            if (castTime > 0f) Emit(new CombatEvent { Type = CombatEventType.CastComplete, Source = u, Target = target, AbilityId = a.id, Name = a.name });
            return ActionResult.Success;
        }

        readonly Dictionary<Unit, ItemInstance> pendingItems = new Dictionary<Unit, ItemInstance>();

        void SpendTime(Unit u, float t)
        {
            if (t <= 0f) return;
            if (t > u.TimeLeft)
            {
                u.TimeDebt += t - Math.Max(0f, u.TimeLeft);
                u.TimeLeft = 0f;
            }
            else u.TimeLeft -= t;
        }

        /// <summary>Spends Time without acting (lets silences/lockouts run out, or simply passes).</summary>
        public ActionResult Wait(Unit u, float seconds)
        {
            if (!InCombat || ActiveUnit != u) return ActionResult.Fail("It is not your turn.");
            if (seconds <= 0f || u.TimeLeft <= 0f) return ActionResult.Fail("No time left this turn.");
            u.TimeLeft = Math.Max(0f, u.TimeLeft - seconds);
            return ActionResult.Success;
        }

        internal void StartCooldown(Unit u, AbilityDef a, AbilityModSet mods)
        {
            float cd = AbilityRules.Cooldown(u, a, mods);
            if (cd > 0f) u.Cooldowns[a.id] = cd;
            if (!string.IsNullOrEmpty(a.cooldownGroup))
            {
                // shared group cooldown: the group lock lasts the ability's cooldown (at least its GCD-free minimum)
                float g = Math.Max(cd, 0f);
                if (g > 0f) u.Cooldowns["grp:" + a.cooldownGroup] = g;
            }
        }

        /// <summary>Turns auto attack on against the target (or toggles it off when <paramref name="toggle"/> and already on).</summary>
        public void StartAutoAttack(Unit u, Unit target, AbilityDef basic, bool toggle)
        {
            if (basic == null) basic = Db.Ability("attack");
            string id = basic != null ? basic.id : "attack";
            if (toggle && u.AutoAttacking && u.AttackTarget == target && u.AutoAttackAbility == id)
            {
                u.AutoAttacking = false;
                Emit(new CombatEvent { Type = CombatEventType.AutoAttackToggled, Source = u, Target = target, AbilityId = id, Amount = 0 });
                return;
            }
            bool changed = !u.AutoAttacking || u.AttackTarget != target || u.AutoAttackAbility != id;
            u.AutoAttacking = true;
            u.AttackTarget = target;
            u.AutoAttackAbility = id;
            if (changed) Emit(new CombatEvent { Type = CombatEventType.AutoAttackToggled, Source = u, Target = target, AbilityId = id, Amount = 1 });
        }

        public void StopAutoAttack(Unit u)
        {
            if (!u.AutoAttacking) return;
            u.AutoAttacking = false;
            Emit(new CombatEvent { Type = CombatEventType.AutoAttackToggled, Source = u, Target = u.AttackTarget, AbilityId = u.AutoAttackAbility, Amount = 0 });
        }

        /// <summary>Removes a queued nextSwing ability.</summary>
        public void CancelQueuedSwing(Unit u) { u.QueuedSwing = ""; }

        /// <summary>Cancels one of the unit's own buffs (right-click a buff). Debuffs cannot be cancelled.</summary>
        public ActionResult CancelAura(Unit u, AuraInstance a)
        {
            if (a == null || a.Bearer != u || !u.Auras.Contains(a)) return ActionResult.Fail("No such aura.");
            if (a.IsDebuff || a.IsPassive || a.IsAreaChild || a.Def.hidden) return ActionResult.Fail("Cannot cancel that.");
            RemoveAura(a, AuraRemoveReason.Cancelled);
            return ActionResult.Success;
        }

        // ============================================================ movement

        /// <summary>Path the unit would take to <paramref name="dest"/>, truncated to its remaining movement (combat) — for UI previews.</summary>
        public PathResult PreviewMove(Unit u, Vec2 dest)
        {
            float budget = InCombat ? Math.Max(0f, u.MoveLeft) : float.PositiveInfinity;
            return Pathfinder.FindPath(u.Position, dest, u.Radius, u.Id, -1, budget);
        }

        /// <summary>Why the unit cannot move now, or null.</summary>
        public string CannotMoveReason(Unit u)
        {
            if (!u.IsAlive) return u.Downed ? "You are downed." : "You are dead.";
            if (InCombat && Started && ActiveUnit != u) return "It is not your turn.";
            if (u.Pending != null) return "You are casting.";
            if (u.IsTotem) return "Totems cannot move.";
            if (u.IsControlled) return "You cannot move while controlled.";
            if (u.IsRooted) return "You are rooted.";
            if (InCombat && u.MoveLeft <= 0.05f) return "No movement left this turn.";
            return null;
        }

        /// <summary>Moves towards <paramref name="dest"/> along a path, as far as the movement budget allows.</summary>
        public ActionResult Move(Unit u, Vec2 dest)
        {
            var why = CannotMoveReason(u);
            if (why != null) return ActionResult.Fail(why);
            var p = PreviewMove(u, dest);
            if (!p.Found || p.Points.Count < 2 || p.Length < 0.01f) return ActionResult.Fail("Cannot move there.");
            DoMove(u, p.Points, p.Length, CombatEventType.Move, "");
            return ActionResult.Success;
        }

        /// <summary>Moves along an explicit path (from the UI or AI). The path is truncated to the remaining movement.</summary>
        public ActionResult MoveAlong(Unit u, IList<Vec2> path)
        {
            var why = CannotMoveReason(u);
            if (why != null) return ActionResult.Fail(why);
            if (path == null || path.Count == 0) return ActionResult.Fail("Empty path.");
            var pts = new List<Vec2> { u.Position };
            int start = Vec2.Distance(path[0], u.Position) < 0.05f ? 1 : 0;
            for (int i = start; i < path.Count; i++) pts.Add(path[i]);
            float budget = InCombat ? u.MoveLeft : float.PositiveInfinity;
            float len = 0f;
            var outPts = new List<Vec2> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vec2.Distance(pts[i - 1], pts[i]);
                if (len + seg > budget + 1e-3f)
                {
                    outPts.Add(Vec2.MoveTowards(pts[i - 1], pts[i], budget - len));
                    len = budget;
                    break;
                }
                len += seg;
                outPts.Add(pts[i]);
            }
            if (len < 0.01f) return ActionResult.Fail("Cannot move there.");
            DoMove(u, outPts, len, CombatEventType.Move, "");
            return ActionResult.Success;
        }

        internal void DoMove(Unit u, List<Vec2> points, float length, CombatEventType type, string reason)
        {
            AuraInstance trigger = null;
            if (InCombat && points.Count >= 2) TruncateAtTrigger(u, points, ref length, out trigger);
            var from = u.Position;
            var end = points[points.Count - 1];
            if (points.Count >= 2) u.FaceTowards(end);
            SetPosition(u, end);
            if (InCombat) u.MoveLeft = Math.Max(0f, u.MoveLeft - length);
            Emit(new CombatEvent { Type = type, Source = u, Target = u, From = from, To = end, Path = new List<Vec2>(points), Amount = length, Reason = reason });
            BreakOnMove(u);
            Specials.OnAnyUnitMoved(this, u);
            RefreshAreaAuras();
            if (trigger != null && trigger.Bearer != null && trigger.Bearer.IsAlive) Specials.OnMovementTrigger(this, trigger, u);
        }

        /// <summary>Stops a path where it first enters a hostile movement trigger (traps). Returns the trigger aura.</summary>
        void TruncateAtTrigger(Unit u, List<Vec2> points, ref float length, out AuraInstance trigger)
        {
            trigger = null;
            float walked = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                float seg = Vec2.Distance(a, b);
                for (float t = 0f; t <= seg + 1e-4f; t += 0.25f)
                {
                    var p = Vec2.MoveTowards(a, b, Math.Min(t, seg));
                    var hit = Specials.MovementTriggerAt(this, u, p);
                    if (hit == null) continue;
                    if (i == 1 && t < 0.01f) { trigger = null; break; } // already inside at the start: no new trigger
                    trigger = hit;
                    points.RemoveRange(i, points.Count - i);
                    points.Add(p);
                    length = walked + Math.Min(t, seg);
                    return;
                }
                walked += seg;
            }
        }

        /// <summary>Places a unit instantly (out of combat / scripted) without spending movement.</summary>
        public void Teleport(Unit u, Vec2 p)
        {
            var from = u.Position;
            SetPosition(u, p);
            Emit(new CombatEvent { Type = CombatEventType.Teleport, Source = u, Target = u, From = from, To = p });
            RefreshAreaAuras();
        }
    }
}
