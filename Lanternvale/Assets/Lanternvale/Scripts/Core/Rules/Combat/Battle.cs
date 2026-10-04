// The rules engine's combat (and exploration "field") context. Split into partial files:
//   Battle.cs          state, units, events, queries
//   Battle.Turns.cs    rounds, initiative, start/end of turn, regen, victory/defeat
//   Battle.Actions.cs  usability checks, ability use, movement, items, AI step execution
//   Battle.Effects.cs  ability resolution and every EffectType
//   Battle.Damage.cs   hit tables, damage/heal pipeline, absorbs, death
//   Battle.Auras.cs    aura application/removal/ticks, area auras, breaks, charges
//   Battle.Procs.cs    proc triggers
//   Battle.Threat.cs   threat tables and taunt
//   Battle.Swings.cs   auto attacks (swing timers), totem actions
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum BattleOutcome { None, Victory, Defeat }

    /// <summary>Per-unit damage/healing/threat counters (combat meters; used by sims and the end-of-battle summary).</summary>
    public sealed class UnitMeters
    {
        public float Damage, Healing, Overheal, DamageTaken, Absorbed, ThreatGenerated;
        public int Crits, Hits, Misses, Casts;
    }

    public sealed partial class Battle
    {
        public readonly GameDatabase Db;
        public readonly Rng Rng;
        public IPathfinder Pathfinder;
        /// <summary>Shared party inventory (soul shards, conjured items, consumables). May be null in tests.</summary>
        public Inventory Inventory;
        /// <summary>False for the exploration context (out-of-combat ability use: buffs, summons, food).</summary>
        public bool InCombat;
        public Team PlayerTeam = Team.Player;

        /// <summary>Every unit that took part (including dead and despawned ones).</summary>
        public readonly List<Unit> Units = new List<Unit>();
        /// <summary>Units that take turns, in initiative order (pets right after their owner).</summary>
        public readonly List<Unit> TurnOrder = new List<Unit>();
        public int TurnIndex = -1;
        public int Round;
        public Unit ActiveUnit;
        public BattleOutcome Outcome;
        public bool Started;
        public bool IsOver => Outcome != BattleOutcome.None;
        public BattleResult Result;
        /// <summary>Creature ids killed (for quest kill objectives), in order.</summary>
        public readonly List<string> KilledCreatures = new List<string>();
        public readonly Dictionary<Unit, UnitMeters> Meters = new Dictionary<Unit, UnitMeters>();

        public readonly List<CombatEvent> Events = new List<CombatEvent>();
        /// <summary>Raised for every event as it happens.</summary>
        public event Action<CombatEvent> EventRaised;
        /// <summary>When false, events are not stored in <see cref="Events"/> (long simulations).</summary>
        public bool RecordEvents = true;

        /// <summary>Raised when a unit joins mid-battle (summons, totems, pets).</summary>
        public event Action<Unit> UnitAdded;
        /// <summary>Raised when a unit leaves (despawned summons/totems, dismissed pets).</summary>
        public event Action<Unit> UnitRemoved;

        public GameConfigDef Config => Db.Config;

        public Battle(GameDatabase db, Rng rng, IPathfinder pathfinder = null, Inventory inventory = null, bool inCombat = true)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Rng = rng ?? new Rng(1);
            Pathfinder = pathfinder ?? new StraightLinePathfinder();
            Inventory = inventory;
            InCombat = inCombat;
        }

        // ================================================================== units

        /// <summary>Adds a unit (before Begin, or mid-battle for summons). Pets/summons are placed after their owner in the turn order.</summary>
        public void AddUnit(Unit u)
        {
            if (u == null || Units.Contains(u)) return;
            Units.Add(u);
            if (!Meters.ContainsKey(u)) Meters[u] = new UnitMeters();
            if (u.IsAlive) Pathfinder?.SetUnit(u.Id, u.Position, u.Radius);
            if (Started && InCombat)
            {
                if (u.Kind != UnitKind.Totem) InsertIntoTurnOrder(u);
                if (u.Team != PlayerTeam) EnsureThreatEntries(u);
                foreach (var e in Units)
                    if (e != u && e.IsAlive && e.Team != PlayerTeam && e.IsHostileTo(u) && !e.Threat.ContainsKey(u) && u.Kind != UnitKind.Totem)
                        e.Threat[u] = 0f;
            }
            UnitAdded?.Invoke(u);
        }

        public void AddUnits(IEnumerable<Unit> units) { foreach (var u in units) AddUnit(u); }

        /// <summary>Removes a unit from the battle without killing it (despawn/dismiss).</summary>
        public void RemoveUnit(Unit u, string reason = "despawn")
        {
            if (u == null || !Units.Contains(u)) return;
            Emit(new CombatEvent { Type = CombatEventType.Despawn, Source = u, Target = u, Reason = reason });
            RemoveAllAuras(u, AuraRemoveReason.Death, keepPassive: true);
            CleanupAreaChildrenOf(u);
            Pathfinder?.SetUnit(u.Id, u.Position, 0f);
            Units.Remove(u);
            int idx = TurnOrder.IndexOf(u);
            if (idx >= 0)
            {
                TurnOrder.RemoveAt(idx);
                if (idx <= TurnIndex) TurnIndex--;
            }
            foreach (var e in Units) { e.Threat.Remove(u); if (e.AttackTarget == u) e.AttackTarget = null; if (e.AggroTarget == u) e.AggroTarget = null; }
            if (u.Owner != null)
            {
                if (u.Owner.Pet == u) u.Owner.Pet = null;
                u.Owner.Summons.Remove(u);
                if (!string.IsNullOrEmpty(u.TotemElement) && u.Owner.Totems.TryGetValue(u.TotemElement, out var t) && t == u) u.Owner.Totems.Remove(u.TotemElement);
            }
            UnitRemoved?.Invoke(u);
        }

        void InsertIntoTurnOrder(Unit u)
        {
            if (TurnOrder.Contains(u)) return;
            int idx = -1;
            if (u.Owner != null)
            {
                idx = TurnOrder.IndexOf(u.Owner);
                if (idx >= 0)
                {
                    // after the owner and the owner's other pets
                    int j = idx + 1;
                    while (j < TurnOrder.Count && TurnOrder[j].Owner == u.Owner) j++;
                    TurnOrder.Insert(j, u);
                    if (j <= TurnIndex) TurnIndex++;
                    return;
                }
            }
            TurnOrder.Add(u);
        }

        public UnitMeters MetersOf(Unit u)
        {
            if (!Meters.TryGetValue(u, out var m)) Meters[u] = m = new UnitMeters();
            return m;
        }

        // ================================================================= events

        internal void Emit(CombatEvent e)
        {
            e.Round = Round;
            if (string.IsNullOrEmpty(e.Text)) e.Text = CombatLog.Format(e);
            if (RecordEvents) Events.Add(e);
            EventRaised?.Invoke(e);
        }

        /// <summary>Returns events added since <paramref name="cursor"/> and advances it.</summary>
        public List<CombatEvent> TakeEvents(ref int cursor)
        {
            var list = new List<CombatEvent>();
            for (int i = Math.Max(0, cursor); i < Events.Count; i++) list.Add(Events[i]);
            cursor = Events.Count;
            return list;
        }

        public void Log(string text, Unit source = null)
        {
            Emit(new CombatEvent { Type = CombatEventType.Log, Source = source, Text = text });
        }

        // ================================================================ queries

        public IEnumerable<Unit> LivingUnits()
        {
            foreach (var u in Units) if (u.IsAlive) yield return u;
        }

        public List<Unit> AlliesOf(Unit u, bool includeSelf = true, bool includeTotems = false)
        {
            var list = new List<Unit>();
            foreach (var o in Units)
                if (o.IsAlive && o.Team == u.Team && (includeSelf || o != u) && (includeTotems || !o.IsTotem)) list.Add(o);
            return list;
        }

        public List<Unit> EnemiesOf(Unit u, bool includeTotems = true)
        {
            var list = new List<Unit>();
            foreach (var o in Units)
                if (o.IsAlive && o.Team != u.Team && (includeTotems || !o.IsTotem)) list.Add(o);
            return list;
        }

        /// <summary>Party characters (player team, characters and companions), including downed ones.</summary>
        public List<Unit> PartyMembers()
        {
            var list = new List<Unit>();
            foreach (var o in Units) if (o.Team == PlayerTeam && o.IsCharacter && !o.Dead) list.Add(o);
            return list;
        }

        /// <summary>Can <paramref name="observer"/> see (target) <paramref name="target"/>? Stealth: within 3 m (+StealthDetection yards).</summary>
        public bool CanSee(Unit observer, Unit target)
        {
            if (observer == null || target == null) return true;
            if (observer.Team == target.Team) return true;
            if (target.HasStateAura(UnitState.Invisible)) return false;
            if (!target.HasStateAura(UnitState.Stealth)) return true;
            float detect = RulesConstants.StealthDetectMetres + MathUtil.Yd(Math.Max(0f, observer.Stats.StealthDetection));
            return observer.DistanceTo(target) <= detect + target.Radius;
        }

        /// <summary>True when the unit is controlled by AI this turn (enemies, auto-played party members, pets of auto-played owners).</summary>
        public bool IsAIControlled(Unit u)
        {
            if (u == null) return false;
            if (u.Team != PlayerTeam) return true;
            if (u.AutoPlay) return true;
            if (u.Kind == UnitKind.Summon || u.Kind == UnitKind.Totem) return true;
            if (u.Owner != null && u.Owner.AutoPlay) return true;
            if (u.IsControlled) return true;
            return false;
        }

        /// <summary>True when the active unit waits for a player decision.</summary>
        public bool NeedsPlayerInput => !IsOver && ActiveUnit != null && !IsAIControlled(ActiveUnit);

        public Unit FindUnit(int id)
        {
            foreach (var u in Units) if (u.Id == id) return u;
            return null;
        }

        internal void SetPosition(Unit u, Vec2 p)
        {
            u.Position = p;
            if (u.IsAlive) Pathfinder?.SetUnit(u.Id, p, u.Radius);
        }

        internal float MeleeReach(Unit a, Unit b) => AbilityRules.MeleeReach(a, b, Config);
        internal bool InMeleeReach(Unit a, Unit b) => a.DistanceTo(b) <= MeleeReach(a, b) + 1e-3f;
        /// <summary>Centre-to-centre melee reach between two units (metres).</summary>
        public float MeleeReachOf(Unit a, Unit b) => MeleeReach(a, b);
        /// <summary>True when the two units are within melee reach of each other.</summary>
        public bool InMeleeRange(Unit a, Unit b) => InMeleeReach(a, b);
    }
}
