// WoW threat tables: damage threat, split healing/resource threat, taunt, target selection (110%/130%).
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    public sealed partial class Battle
    {
        /// <summary>Puts every hostile unit on the AI unit's threat table with 0 threat.</summary>
        void EnsureThreatEntries(Unit ai)
        {
            foreach (var o in Units)
                if (o.IsAlive && o.IsHostileTo(ai) && o.Kind != UnitKind.Totem && !ai.Threat.ContainsKey(o)) ai.Threat[o] = 0f;
        }

        /// <summary>Adds threat from <paramref name="source"/> on <paramref name="table"/>'s threat table. Totems credit their owner.</summary>
        public void AddThreat(Unit table, Unit source, float amount, bool engage)
        {
            if (table == null || source == null || !InCombat) return;
            if (source.IsTotem && source.Owner != null) source = source.Owner;
            if (table.Team == PlayerTeam || !table.IsAlive || table.Team == source.Team) return;
            if (source.IsFeigningDeath) return;
            table.Threat.TryGetValue(source, out var cur);
            float nv = Math.Max(0f, cur + amount);
            table.Threat[source] = nv;
            if (amount != 0f)
            {
                MetersOf(source).ThreatGenerated += amount;
                if (Math.Abs(amount) >= 1f) Emit(new CombatEvent { Type = CombatEventType.Threat, Source = source, Target = table, Amount = amount, Count = (int)nv });
            }
        }

        /// <summary>Healing / resource threat: split evenly among enemies engaged with the source's side.</summary>
        void SplitThreat(Unit source, float amount, Unit credit)
        {
            if (amount <= 0f || !InCombat) return;
            if (credit != null && credit.IsTotem && credit.Owner != null) credit = credit.Owner;
            var tables = new List<Unit>();
            foreach (var u in Units)
                if (u.IsAlive && u.Team != PlayerTeam && u.IsHostileTo(credit ?? source) && u.Threat.Count > 0) tables.Add(u);
            if (tables.Count == 0) return;
            float each = amount / tables.Count;
            foreach (var t in tables) AddThreat(t, credit ?? source, each, true);
        }

        public float ThreatOf(Unit table, Unit source) => table != null && source != null && table.Threat.TryGetValue(source, out var v) ? v : 0f;

        /// <summary>Highest-threat unit on the table (any reachability), or null.</summary>
        public Unit TopThreat(Unit table)
        {
            Unit best = null; float bv = -1f;
            foreach (var kv in table.Threat)
            {
                if (!kv.Key.IsAlive || kv.Key.IsFeigningDeathFor(table)) continue;
                if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
            }
            return best;
        }

        /// <summary>Taunt: the target attacks the taunter during its next turn and the taunter's threat becomes the highest.</summary>
        public void Taunt(Unit taunter, Unit target)
        {
            if (target == null || taunter == null || target.Team == taunter.Team) return;
            if (target.Team != PlayerTeam)
            {
                float top = 0f;
                foreach (var kv in target.Threat) if (kv.Value > top) top = kv.Value;
                target.Threat.TryGetValue(taunter, out var cur);
                if (top > cur) target.Threat[taunter] = top;
                target.AggroTarget = taunter;
            }
            target.TauntedBy = taunter;
            target.TauntUntilTurn = target.TurnsTaken + 1;
            Emit(new CombatEvent { Type = CombatEventType.Taunt, Source = taunter, Target = target });
        }

        /// <summary>
        /// Threat-based target for an AI unit: taunt first; otherwise keep the current target unless another
        /// reachable unit exceeds 110% (melee) / 130% (ranged) of its threat.
        /// </summary>
        public Unit SelectThreatTarget(Unit ai, bool ranged, Func<Unit, bool> reachable = null)
        {
            if (ai.TauntedBy != null && ai.TauntedBy.IsAlive && CanSee(ai, ai.TauntedBy)) return SetAggro(ai, ai.TauntedBy);
            EnsureThreatEntries(ai);
            Unit cur = ai.AggroTarget;
            bool curOk = cur != null && cur.IsAlive && cur.IsHostileTo(ai) && CanSee(ai, cur) && !cur.IsFeigningDeathFor(ai) && !cur.IsUntargetable && (reachable == null || reachable(cur));
            Unit best = null; float bv = -1f;
            foreach (var kv in ai.Threat)
            {
                var u = kv.Key;
                if (!u.IsAlive || !CanSee(ai, u) || u.IsFeigningDeathFor(ai) || u.IsUntargetable || u.IsTotem) continue;
                if (reachable != null && !reachable(u)) continue;
                float v = kv.Value;
                if (v > bv || (Math.Abs(v - bv) < 1e-3f && best != null && ai.DistanceTo(u) < ai.DistanceTo(best))) { bv = v; best = u; }
            }
            if (!curOk) return SetAggro(ai, best ?? NearestHostile(ai));
            if (best == null || best == cur) return SetAggro(ai, cur);
            float curThreat = ThreatOf(ai, cur);
            float factor = ranged ? 1.3f : 1.1f;
            if (bv > curThreat * factor && bv > 0f) return SetAggro(ai, best);
            return SetAggro(ai, cur);
        }

        Unit SetAggro(Unit ai, Unit t)
        {
            if (t != ai.AggroTarget && t != null)
                Emit(new CombatEvent { Type = CombatEventType.TargetChanged, Source = ai, Target = t });
            ai.AggroTarget = t;
            return t;
        }

        public Unit NearestHostile(Unit u, bool includeTotems = false)
        {
            Unit best = null; float bd = float.MaxValue;
            foreach (var o in Units)
            {
                if (!o.IsAlive || !o.IsHostileTo(u) || (!includeTotems && o.IsTotem) || !CanSee(u, o) || o.IsUntargetable || o.IsFeigningDeathFor(u)) continue;
                float d = u.DistanceTo(o);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }
    }
}
