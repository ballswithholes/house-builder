// Rules engine simulations ([Sim], run with --sim): every class vs a training dummy at levels 10/30/60 and a
// four-character party against a group of enemies. They report numbers and fail on engine/AI errors.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using static Lanternvale.Tests.RulesTestUtil;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsRulesSim
    {
        static readonly ClassId[] Classes =
            { ClassId.Warrior, ClassId.Rogue, ClassId.Mage, ClassId.Priest, ClassId.Paladin, ClassId.Hunter, ClassId.Shaman, ClassId.Warlock };

        /// <summary>One class alone against a training dummy for 10 rounds: damage per round, abilities used, no errors.</summary>
        public static (float dpr, int casts, List<string> warnings, Dictionary<string, int> used) DummyRun(ClassId cls, int level, int rounds = 10, int seed = 3)
        {
            var b = NewBattle(seed, new Inventory());
            b.Inventory.Add(Db.Item("soul_shard"), 5);
            if (Db.Item("shaman_ankh") != null) b.Inventory.Add(Db.Item("shaman_ankh"), 2);
            if (Db.Item("rogue_flash_powder") != null) b.Inventory.Add(Db.Item("rogue_flash_powder"), 5);
            if (Db.Item("rogue_blinding_powder") != null) b.Inventory.Add(Db.Item("rogue_blinding_powder"), 5);
            var hero = Hero(cls, level).At(20f, 20f);
            var dummy = Mob("cr_training_dummy", level).At(30f, 20f);
            hero.FaceTowards(dummy.Position);
            b.AddUnit(hero);
            b.AddUnit(dummy);
            var warnings = new List<string>();
            Run(b, rounds, warnings);
            var used = new Dictionary<string, int>();
            foreach (var e in b.Events)
                if ((e.Type == CombatEventType.AbilityUsed || e.Type == CombatEventType.CastStart) && e.Source != null && e.Source.Master == hero && !string.IsNullOrEmpty(e.AbilityId))
                {
                    used.TryGetValue(e.AbilityId, out var n);
                    used[e.AbilityId] = n + 1;
                }
            float dmg = DamageBy(b, hero);
            return (dmg / Math.Max(1, rounds), b.Meters.TryGetValue(hero, out var m) ? m.Casts : 0, warnings, used);
        }

        [Sim]
        public static void ClassesVsDummy()
        {
            var problems = new List<string>();
            Console.WriteLine("    class      L10 dmg/round   L30 dmg/round   L60 dmg/round");
            foreach (var cls in Classes)
            {
                var line = $"    {cls,-9}";
                foreach (var lvl in new[] { 10, 30, 60 })
                {
                    var r = DummyRun(cls, lvl);
                    line += $" {r.dpr,14:0.0} ";
                    foreach (var w in r.warnings) problems.Add($"{cls} L{lvl}: {w}");
                    if (r.dpr <= 0f) problems.Add($"{cls} L{lvl}: no damage dealt");
                }
                Console.WriteLine(line);
            }
            foreach (var p in problems.Take(20)) Console.WriteLine("    ! " + p);
            Harness.Assert(problems.Count == 0, $"{problems.Count} problems in class sims (first: {problems.FirstOrDefault()})");
        }

        [Sim]
        public static void ClassAbilityUsage()
        {
            foreach (var cls in Classes)
            {
                var r = DummyRun(cls, 60, 12, 11);
                Console.WriteLine($"    {cls} L60 used: " + string.Join(", ", r.used.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}")));
            }
        }

        static List<Unit> Party(int level, int seed)
        {
            return new List<Unit>
            {
                Hero(ClassId.Warrior, level, seed: seed, name: "Tank"),
                Hero(ClassId.Priest, level, seed: seed + 1, name: "Healer"),
                Hero(ClassId.Mage, level, seed: seed + 2, name: "Mage"),
                Hero(ClassId.Rogue, level, seed: seed + 3, name: "Rogue"),
            };
        }

        /// <summary>A party of four (warrior, priest, mage, rogue) against a mixed group at level 12 and 30.</summary>
        [Sim]
        public static void PartyVsGroup()
        {
            var problems = new List<string>();
            int wins = 0, total = 0;
            foreach (var level in new[] { 12, 30 })
                for (int seed = 1; seed <= 5; seed++)
                {
                    var b = NewBattle(seed * 31 + level, new Inventory());
                    var party = Party(level, seed);
                    party[0].RoleOverride = UnitRole.Tank;
                    party[1].RoleOverride = UnitRole.Healer;
                    for (int i = 0; i < party.Count; i++) b.AddUnit(party[i].At(15f + (i % 2) * 2f, 18f + i * 2f));
                    var foes = new List<Unit>
                    {
                        Mob("cr_bandit_cutthroat", level).At(32f, 18f), Mob("cr_bandit_cutthroat", level).At(33f, 22f),
                        Mob("cr_bandit_archer", level).At(38f, 20f), Mob("cr_bandit_hexer", level).At(39f, 24f),
                    };
                    foreach (var f in foes) { b.AddUnit(f); f.FaceTowards(party[0].Position); }
                    foreach (var p in party) p.FaceTowards(foes[0].Position);
                    var warnings = new List<string>();
                    Run(b, 30, warnings);
                    total++;
                    if (b.Outcome == BattleOutcome.Victory) wins++;
                    foreach (var w in warnings) problems.Add($"L{level} seed {seed}: {w}");
                    Console.WriteLine($"    L{level} seed {seed}: {b.Outcome} in {b.Round} rounds; " +
                        string.Join(", ", party.Select(p => $"{p.Name} {(p.IsAlive ? p.HealthPct.ToString("0") + "%" : "down")} dmg {DamageBy(b, p):0} heal {(b.Meters.TryGetValue(p, out var m) ? m.Healing : 0):0}")));
                }
            Console.WriteLine($"    party wins {wins}/{total}");
            foreach (var p in problems.Take(20)) Console.WriteLine("    ! " + p);
            Harness.Assert(problems.Count == 0, $"{problems.Count} problems in party sims (first: {problems.FirstOrDefault()})");
        }

        /// <summary>Party of four at level 12 against the Hollow Warden (boss): difficulty sanity check.</summary>
        [Sim]
        public static void PartyVsWarden()
        {
            var problems = new List<string>();
            int wins = 0;
            for (int seed = 1; seed <= 5; seed++)
            {
                var b = NewBattle(seed * 7 + 1, new Inventory());
                var party = Party(12, seed);
                party[0].RoleOverride = UnitRole.Tank;
                party[1].RoleOverride = UnitRole.Healer;
                for (int i = 0; i < party.Count; i++) b.AddUnit(party[i].At(15f + (i % 2) * 2f, 18f + i * 2f));
                var wd = Db.Creature("cr_hollow_warden");
                var boss = UnitFactory.CreateCreature(Db, wd, UnitFactory.CreatureLevel(wd, 0, 12, b.Rng)).At(30f, 21f);
                b.AddUnit(boss);
                boss.FaceTowards(party[0].Position);
                foreach (var p in party) p.FaceTowards(boss.Position);
                var warnings = new List<string>();
                Run(b, 40, warnings);
                if (b.Outcome == BattleOutcome.Victory) wins++;
                foreach (var w in warnings) problems.Add($"seed {seed}: {w}");
                Console.WriteLine($"    seed {seed}: {b.Outcome} in {b.Round} rounds; Warden {boss.Health:0}/{boss.MaxHealth:0}; " +
                    string.Join(", ", party.Select(p => $"{p.Name} {(p.IsAlive ? p.HealthPct.ToString("0") + "%" : "down")}")));
            }
            Console.WriteLine($"    party wins {wins}/5");
            foreach (var p in problems.Take(10)) Console.WriteLine("    ! " + p);
            Assert(problems.Count == 0, $"{problems.Count} problems (first: {problems.FirstOrDefault()})");
        }
    }
}
