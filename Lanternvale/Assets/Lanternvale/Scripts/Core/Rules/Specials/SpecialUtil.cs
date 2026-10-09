// Small helpers shared by the class special handlers.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    static class SpecialUtil
    {
        /// <summary>Per-rank value of the talent/item passive whose hook is running (values[rank-1] or value × rank).</summary>
        internal static float PV(int rank) => Specials.RankValue(Specials.CurrentPassive, rank);

        /// <summary>Per-rank value of the named Special talent passive of a unit (0 when not learned).</summary>
        internal static float TalentValue(Unit u, string special, string talentId = null)
        {
            var p = Specials.TalentPassive(u, special, out int r, talentId);
            return p != null ? Specials.RankValue(p, r) : 0f;
        }

        internal static bool HasTag(AuraDef d, string tag)
        {
            if (d?.tags == null) return false;
            foreach (var t in d.tags) if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool Imposes(AuraDef d, UnitState s) => d != null && Array.IndexOf(d.states, s) >= 0;

        internal static bool ImposesAny(AuraDef d, params UnitState[] states)
        {
            if (d == null) return false;
            foreach (var s in states) if (Array.IndexOf(d.states, s) >= 0) return true;
            return false;
        }

        /// <summary>The aura slows movement (negative MoveSpeed mod or the Daze state).</summary>
        internal static bool IsSnare(AuraDef d)
        {
            if (d == null) return false;
            if (Imposes(d, UnitState.Daze)) return true;
            foreach (var m in d.mods)
            {
                if (m.stat != StatId.MoveSpeed) continue;
                if (m.value < 0) return true;
                if (m.values != null && m.values.Length > 0 && m.values[0] < 0) return true;
            }
            return false;
        }

        internal static bool IsRootOrSnare(AuraDef d) => Imposes(d, UnitState.Root) || IsSnare(d);

        /// <summary>A harmful aura instance (debuff, or applied by a hostile unit) that is not a passive.</summary>
        internal static bool IsHarmful(AuraInstance a) =>
            !a.IsPassive && (a.Def.kind == AuraKind.Debuff || (a.Caster != null && a.Bearer != null && a.Caster.IsHostileTo(a.Bearer)));

        /// <summary>Removes the unit's auras matching the predicate.</summary>
        internal static int RemoveWhere(Battle b, Unit u, Predicate<AuraInstance> pred, AuraRemoveReason reason = AuraRemoveReason.Cancelled)
        {
            int n = 0;
            foreach (var a in new List<AuraInstance>(u.Auras))
                if (u.Auras.Contains(a) && pred(a)) { b.RemoveAura(a, reason); n++; }
            return n;
        }

        internal static CreatureType TypeOf(Unit u) => u.Creature != null && u.Class == null ? u.Creature.type : CreatureType.Humanoid;

        /// <summary>Base resource cost of a rank before talents/auras (cost.amount + perLevel × (rankLevel − learnLevel)).</summary>
        internal static float BaseCost(Unit u, AbilityDef a, int rank)
        {
            var c = a?.cost;
            if (c == null || c.type == ResourceType.None) return 0f;
            if (c.pctBaseMana > 0) return c.pctBaseMana / 100f * (u.Stats.BaseMana > 0 ? u.Stats.BaseMana : u.MaxMana);
            return c.amount + c.perLevel * Math.Max(0, AbilityRules.EffLevel(u, a, rank) - a.learnLevel);
        }

        /// <summary>Reagents are only tracked for the player's party (enemy casters and inventory-less test battles ignore them).</summary>
        internal static bool UsesReagents(Battle b, Unit u) => b != null && b.Inventory != null && u != null && u.Master.Team == b.PlayerTeam;

        internal static bool HasReagent(Battle b, Unit u, string item, int n = 1) => !UsesReagents(b, u) || b.Inventory.Count(item) >= n;

        internal static void ConsumeReagent(Battle b, Unit u, string item, int n = 1)
        {
            if (!UsesReagents(b, u) || !b.Inventory.Remove(item, n)) return;
            var def = b.Db.Item(item);
            b.Emit(new CombatEvent { Type = CombatEventType.ItemConsumed, Source = u, Target = u, Name = def != null ? def.name : item, AbilityId = item, Count = n });
        }

        internal static void GiveItem(Battle b, Unit u, string item, int n = 1)
        {
            if (!UsesReagents(b, u)) return;
            var def = b.Db.Item(item);
            if (def == null) return;
            int added = b.Inventory.Add(def, n);
            if (added > 0) b.Emit(new CombatEvent { Type = CombatEventType.ItemCreated, Source = u, Target = u, Name = def.name, AbilityId = def.id, Count = added });
        }

        /// <summary>Would killing the unit grant experience (not a summon/totem/critter, not grey for the killer)?</summary>
        internal static bool GrantsXp(Unit victim, Unit killer)
        {
            if (victim == null || victim.Creature == null || victim.Class != null) return false;
            if (victim.Kind != UnitKind.Creature || victim.Owner != null) return false;
            if (victim.Creature.rank == CreatureRank.Critter || victim.Creature.rank == CreatureRank.Totem || victim.Creature.xpMult <= 0f) return false;
            if (killer != null && victim.Level <= Formulas.GreyLevel(killer.Level)) return false;
            return true;
        }

        /// <summary>Magnitude of an effect at the cast's levels (min..max + perLevel + perCombo).</summary>
        internal static float Magnitude(AbilityCast c, EffectDef e) =>
            AbilityRules.BaseMagnitude(e, c.EffLevel, c.LearnLevel, c.ComboPoints, c.Battle.Rng);

        /// <summary>Nearest living hostile (to <paramref name="side"/>) unit within <paramref name="metres"/> of a point.</summary>
        internal static Unit NearestEnemy(Battle b, Unit side, Vec2 p, float metres, Unit exclude)
        {
            Unit best = null;
            float bd = float.MaxValue;
            foreach (var o in b.Units)
            {
                if (o == exclude || !o.IsAlive || o.IsTotem || !o.IsHostileTo(side) || o.IsUntargetable) continue;
                float d = Vec2.Distance(o.Position, p) - o.Radius;
                if (d <= metres && d < bd) { bd = d; best = o; }
            }
            return best;
        }

        internal static bool InParty(Unit a, Unit b) => a != null && b != null && a.Team == b.Team;
    }
}
