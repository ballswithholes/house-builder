// Out-of-combat use of the rules engine ("field" context): real-time ticking (regen, cooldowns, buff expiry,
// HoT/food ticks), ability and item use without turns, opening actions that start a battle, disengaging.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed partial class Battle
    {
        /// <summary>
        /// Creates an exploration context (InCombat = false) for the party: abilities and items resolve instantly
        /// (no turns, no Time), cooldowns/auras run in real time via <see cref="TickOutOfCombat"/>.
        /// </summary>
        public static Battle CreateField(GameDatabase db, Rng rng, IEnumerable<Unit> party, IPathfinder pathfinder = null, Inventory inventory = null)
        {
            var b = new Battle(db, rng, pathfinder, inventory, inCombat: false);
            if (party != null)
                foreach (var u in party)
                {
                    b.AddUnit(u);
                    if (u.Pet != null && !u.Pet.Dead) b.AddUnit(u.Pet);
                }
            return b;
        }

        /// <summary>
        /// Advances out-of-combat time for every unit in this (field) context: cooldowns, lockouts, aura durations and
        /// periodic ticks (food, drink, HoTs), summon lifetimes and resource regeneration.
        /// </summary>
        public void TickOutOfCombat(float seconds)
        {
            if (seconds <= 0f || InCombat) return;
            foreach (var u in new List<Unit>(Units))
            {
                if (!Units.Contains(u)) continue;
                TickUnitOutOfCombat(u, seconds);
            }
        }

        /// <summary>Real-time update of one unit (see <see cref="TickOutOfCombat"/>).</summary>
        public void TickUnitOutOfCombat(Unit u, float seconds)
        {
            if (u.Dead) return;
            ElapseAuras(u, seconds);
            ElapseTimers(u, seconds);
            u.SecondsSinceManaSpent += seconds;
            u.SecondsSinceCombat += seconds;
            if (u.Lifetime > 0)
            {
                u.Lifetime -= seconds;
                if (u.Lifetime <= 1e-3f) { Despawn(u, "expired"); return; }
            }
            if (u.Downed) return;
            RegenOutOfCombat(u, seconds);
            Specials.OnOutOfCombatTick(this, u, seconds);
        }

        /// <summary>
        /// Out-of-combat regeneration per second: health 2% of max + 0.25 × Spirit (+HP5); mana: Spirit regen
        /// (per 2 s tick / 2) + 1% of max outside the five-second rule (SpiritRegenWhileCasting share inside it) + MP5;
        /// energy +10/s, focus +6/s, rage −1/s.
        /// </summary>
        void RegenOutOfCombat(Unit u, float dt)
        {
            var st = u.Stats;
            if (u.Health < u.MaxHealth)
                u.Health = Math.Min(u.MaxHealth, u.Health + (u.MaxHealth * 0.02f + st.Spirit * 0.25f + st.HealthRegen / 5f) * dt);
            if (u.MaxMana > 0 && u.Mana < u.MaxMana)
            {
                bool fsr = u.SecondsSinceManaSpent < RulesConstants.FiveSecondRule;
                float spirit = u.Class != null ? st.SpiritRegenPerTick / 2f : u.MaxMana * 0.01f;
                float perSec = fsr ? spirit * st.SpiritRegenWhileCasting / 100f : spirit + u.MaxMana * 0.01f;
                u.Mana = Math.Min(u.MaxMana, u.Mana + (perSec + st.ManaRegen / 5f) * dt);
            }
            if (u.PowerType == ResourceType.Energy) u.Energy = Math.Min(u.MaxResource(ResourceType.Energy), u.Energy + RulesConstants.EnergyPerSecondOoc * st.EnergyRegen * dt);
            if (u.PowerType == ResourceType.Focus) u.Focus = Math.Min(RulesConstants.MaxFocus, u.Focus + RulesConstants.FocusPerSecondOoc * dt);
            if (u.Rage > 0) u.Rage = Math.Max(0f, u.Rage - RulesConstants.RageDecayPerSecondOoc * dt);
        }

        /// <summary>
        /// Uses an ability before the battle starts (Charge, Ambush, Cheap Shot, Pyroblast...), then begins combat.
        /// Opening from stealth with an Opener-tagged ability surprises the enemies (they lose their first turn).
        /// The battle must contain all units and not be started yet.
        /// </summary>
        public ActionResult BeginWithOpener(Unit attacker, string abilityId, Unit target = null, Vec2? point = null)
        {
            if (Started) return ActionResult.Fail("The battle has already started.");
            var a = Db.Ability(abilityId);
            if (a == null) return ActionResult.Fail($"Unknown ability '{abilityId}'.");
            bool stealthed = attacker.IsStealthed;
            bool opener = AbilityMods.HasTag(a, "Opener") || a.aiHint == "Opener";
            var r = UseAbility(attacker, a, target, point, false, null);
            if (!r.Ok) return r;
            attacker.Engaged = false; // Charge counts as the opening move
            Begin(stealthed && opener ? (Team?)(attacker.Team == Team.Player ? Team.Enemy : Team.Player) : null);
            return ActionResult.Success;
        }

        /// <summary>True when every remaining hostile is passive (training dummies): the party may simply walk away.</summary>
        public bool CanDisengage
        {
            get
            {
                if (!InCombat || IsOver) return false;
                foreach (var u in Units)
                {
                    if (!u.IsAlive || u.Team == PlayerTeam || u.IsTotem) continue;
                    if (u.Creature == null || u.Creature.ai != AIProfile.Passive) return false;
                }
                return true;
            }
        }

        /// <summary>Leaves a battle against passive enemies (outcome Fled, no XP or loot).</summary>
        public ActionResult Disengage()
        {
            if (!CanDisengage) return ActionResult.Fail("You cannot leave combat now.");
            Finish(BattleOutcome.Fled);
            return ActionResult.Success;
        }
    }
}
