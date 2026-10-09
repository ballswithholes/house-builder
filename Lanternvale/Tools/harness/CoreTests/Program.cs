// Headless harness for the Lanternvale rules core.
//   dotnet CoreTests.dll --data <Resources/Data dir> [--no-tests] [--filter name] [--sim] [--allow-problems] [--quiet]
// Any static method marked [Test] in this assembly is run (in Tests*.cs files).
// Any static method marked [Sim] runs only with --sim (balance reports, long simulations).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Lanternvale.Data;

namespace Lanternvale.Tests
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SimAttribute : Attribute { }

    public static class Harness
    {
        public static GameDatabase Db;
        public static string DataDir;

        public static void Assert(bool cond, string msg)
        {
            if (!cond) throw new Exception("Assertion failed: " + msg);
        }

        public static void AssertNear(float a, float b, float eps, string msg)
        {
            if (Math.Abs(a - b) > eps) throw new Exception($"Assertion failed: {msg} (got {a}, expected {b} ±{eps})");
        }

        public static IEnumerable<KeyValuePair<string, string>> ReadDataFiles(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                yield return new KeyValuePair<string, string>(Path.GetRelativePath(dir, f), File.ReadAllText(f));
        }

        public static int Main(string[] args)
        {
            string filter = null, grep = null;
            bool runTests = true, runSims = false, allowProblems = false, quiet = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--data": DataDir = args[++i]; break;
                    case "--filter": filter = args[++i]; break;
                    case "--grep": grep = args[++i]; break;
                    case "--no-tests": runTests = false; break;
                    case "--sim": runSims = true; break;
                    case "--allow-problems": allowProblems = true; break;
                    case "--quiet": quiet = true; break;
                }
            }
            if (DataDir == null) DataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Assets/Lanternvale/Resources/Data"));
            if (quiet) Lanternvale.Util.Log.InfoHandler = _ => { };

            int failures = 0;

            // 1. data
            Db = GameDatabase.Load(ReadDataFiles(DataDir));
            HookRules();
            var problems = DataValidator.Validate(Db);
            if (grep != null) problems = problems.Where(x => x.IndexOf(grep, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Console.WriteLine($"Data: {Db.Classes.Count} classes, {Db.Abilities.Count} abilities, {Db.Auras.Count} auras, " +
                              $"{Db.TalentTrees.Count} talent trees ({Db.Talents.Count} talents), {Db.Items.Count} items, " +
                              $"{Db.Creatures.Count} creatures, {Db.Npcs.Count} npcs, {Db.Companions.Count} companions, " +
                              $"{Db.Dialogues.Count} dialogues, {Db.Quests.Count} quests, {Db.Maps.Count} maps");
            if (problems.Count > 0)
            {
                Console.WriteLine($"Data problems ({problems.Count}):");
                foreach (var p in problems.Take(400)) Console.WriteLine("  - " + p);
                if (problems.Count > 400) Console.WriteLine($"  ... and {problems.Count - 400} more");
                if (!allowProblems) failures += problems.Count;
            }
            else Console.WriteLine("Data validation: OK");

            // 2. tests / sims
            var methods = typeof(Harness).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(m => (runTests && m.IsDefined(typeof(TestAttribute))) || (runSims && m.IsDefined(typeof(SimAttribute))))
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();
            int passed = 0;
            foreach (var m in methods)
            {
                var name = m.DeclaringType.Name + "." + m.Name;
                try
                {
                    m.Invoke(null, null);
                    passed++;
                    Console.WriteLine($"  PASS {name}");
                }
                catch (TargetInvocationException e)
                {
                    failures++;
                    Console.WriteLine($"  FAIL {name}: {e.InnerException?.Message}\n{e.InnerException?.StackTrace}");
                }
            }
            if (methods.Count > 0) Console.WriteLine($"Tests: {passed}/{methods.Count} passed");

            return failures == 0 ? 0 : 1;
        }

        /// <summary>Connects the validator to the rules engine's special-handler registry if one exists.</summary>
        static void HookRules()
        {
            // The rules engine exposes Lanternvale.Rules.Specials.IsImplemented(string) once written.
            var t = typeof(GameDatabase).Assembly.GetType("Lanternvale.Rules.Specials");
            var m = t?.GetMethod("IsImplemented", BindingFlags.Public | BindingFlags.Static);
            if (m != null) DataValidator.IsSpecialImplemented = s => (bool)m.Invoke(null, new object[] { s });
        }
    }
}
