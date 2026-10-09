// [Sim] raid scale (Tools/check.sh core --sim --filter RaidSim): a raid of 10 (main + 9 companions, every companion
// auto-played) against 12 bandits at level 22 on the Hollow Heart, resolved by GameSession.AutoResolve. Reports the outcome,
// rounds, turns (TurnStart events), turn-order length and CPU time per fight and per turn.
using System;
using System.Diagnostics;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsRaidSim
    {
        static void One(ClassId main, ulong seed, int enemies)
        {
            var s = RaidTest.Game(main, 22, seed);
            Assert(s.EnterRaid(RaidTest.Raid, "from_mirefen", RaidTest.Ids(10), true) == null, "enter: " + s.LastError);
            s.SetAutoPlay(s.Main, true);
            var at = RaidTest.OpenArea(s, 20f, 14f, s.Leader.Position);
            using (var enc = new RaidTest.TempEncounter(RaidTest.Raid, $"enc_sim_raid_{seed}_{enemies}", at + new Vec2(6f, 0f), RaidTest.Pack(enemies)))
            {
                s.SetPartyPositions(at - new Vec2(4f, 0f));
                var sw = Stopwatch.StartNew();
                var b = s.StartEncounter(enc.Def.id);
                Assert(b != null, "fight: " + s.LastError);
                double prep = sw.Elapsed.TotalMilliseconds;
                var outcome = s.AutoResolve(60);
                sw.Stop();
                int turns = b.Events.Count(e => e.Type == CombatEventType.TurnStart);
                int side = b.Units.Count(u => u.Team == b.PlayerTeam);
                Assert(b.IsOver && outcome != BattleOutcome.None, $"the raid fight resolves (outcome {outcome}, round {b.Round})");
                Console.WriteLine($"    raid {s.Main.Class.name} seed {seed}: 10 characters ({side} player-side units) v {enemies} at L22 → {outcome} in {b.Round} rounds, " +
                    $"turn order {b.TurnOrder.Count}, {turns} turns; {sw.Elapsed.TotalMilliseconds:0} ms (prep {prep:0.0} ms), " +
                    $"{sw.Elapsed.TotalMilliseconds / Math.Max(1, turns):0.00} ms/turn; party HP {SessionTest.PartyHp(s)}");
                s.FinishBattle();
            }
        }

        [Sim]
        public static void RaidSim_TenVersusTwelve_Level22()
        {
            One(ClassId.Paladin, 1, 12);   // includes JIT warm-up
            One(ClassId.Paladin, 2, 12);
            One(ClassId.Mage, 3, 12);
            One(ClassId.Warrior, 4, 12);
        }
    }
}
