// Shared helpers for the rules engine tests (TestsRules*.cs).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class RulesTestUtil
    {
        public static Battle NewBattle(int seed = 1, Inventory inv = null, bool inCombat = true) =>
            new Battle(Db, new Rng((ulong)seed), new StraightLinePathfinder(80f, 60f), inv, inCombat);

        /// <summary>A character of the class at the level: every ability, auto talents, veteran gear.</summary>
        public static Unit Hero(ClassId cls, int level, bool talents = true, bool gear = true, int seed = 7, string name = null)
        {
            var u = UnitFactory.CreateCharacter(Db, cls, name ?? cls.ToString(), level, learnAll: true);
            if (talents)
            {
                Progression.AutoAllocateTalents(u);
                Progression.LearnAllAvailable(u);
            }
            if (gear) ItemGenerator.EquipVeteranGear(Db, u, new Rng((ulong)seed));
            UnitFactory.AttachPassives(u);
            u.InvalidateStats();
            u.RestoreFull();
            u.AutoPlay = true;
            return u;
        }

        public static Unit Mob(string id, int level, Team team = Team.Enemy)
        {
            var def = Db.Creature(id) ?? throw new Exception("no creature " + id);
            return UnitFactory.CreateCreature(Db, def, level, team);
        }

        /// <summary>Makes a unit very hard to kill (max health × 200) for long tests.</summary>
        public static Unit Tough(this Unit u, float mult = 200f)
        {
            u.MaxHealthMult = mult;
            u.InvalidateStats();
            u.Health = u.MaxHealth;
            return u;
        }

        public static Unit At(this Unit u, float x, float y)
        {
            u.Position = new Vec2(x, y);
            return u;
        }

        public static Unit Facing(this Unit u, Unit other)
        {
            u.FaceTowards(other.Position);
            return u;
        }

        /// <summary>Lets the AI play every unit until the battle ends or <paramref name="maxRounds"/> pass.</summary>
        public static void Run(Battle b, int maxRounds, List<string> warnings = null)
        {
            var old = Log.WarnHandler;
            if (warnings != null) Log.WarnHandler = s => warnings.Add(s);
            try
            {
                if (!b.Started) b.Begin();
                int guard = 0;
                while (!b.IsOver && b.Round <= maxRounds && guard++ < 20000)
                {
                    var u = b.ActiveUnit;
                    if (u == null) break;
                    if (b.PendingSelfResurrection(u) != null) { b.AcceptSelfResurrection(u); continue; }
                    AI.RunTurn(b, u);
                }
            }
            finally { Log.WarnHandler = old; }
        }

        /// <summary>Ends turns until it is <paramref name="u"/>'s turn (others do nothing).</summary>
        public static void SkipTo(Battle b, Unit u, int guard = 50)
        {
            if (!b.Started) b.Begin();
            while (!b.IsOver && b.ActiveUnit != u && guard-- > 0)
            {
                if (b.ActiveUnit == null) break;
                b.EndTurn(b.ActiveUnit);
            }
        }

        public static float DamageBy(Battle b, Unit u) => b.Meters.TryGetValue(u, out var m) ? m.Damage : 0f;

        public static int Count(Battle b, CombatEventType t, Unit src = null, string ability = null)
        {
            int n = 0;
            foreach (var e in b.Events)
                if (e.Type == t && (src == null || e.Source == src) && (ability == null || e.AbilityId == ability)) n++;
            return n;
        }
    }
}
