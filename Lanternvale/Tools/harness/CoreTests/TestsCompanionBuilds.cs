// Companion talent builds (CompanionDef.preferredTalents): legal in order, and the auto-allocated build keeps each
// companion in its role at every level — Kael tanks, Seren heals — with the default session settings.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsCompanionBuilds
    {
        /// <summary>The role each companion's build is meant to give the companion AI (RoleInference from the dominant tree).</summary>
        static readonly Dictionary<string, UnitRole> ExpectedRoles = new Dictionary<string, UnitRole>
        {
            // the Ember Road companions (Docs/Expansion.md §8): two tanks and two healers for raids
            { "bruna", UnitRole.Tank },       // Warrior: sword-and-board Protection (Shield Slam)
            { "ysolde", UnitRole.Tank },      // Paladin: Protection (Holy Shield); a tree role needs 5 talent points: from level 14
            { "liora", UnitRole.Healer },     // Priest: Discipline (Power Infusion), then Holy
            { "nanami", UnitRole.Healer },    // Shaman: deep Restoration (Mana Tide), from level 14
            { "kael", UnitRole.Tank },        // Warrior: Protection
            { "seren", UnitRole.Healer },     // Priest: Holy, then Discipline
            { "aldric", UnitRole.MeleeDps },  // Paladin: sword-and-board Retribution
            { "lys", UnitRole.RangedDps },
            { "rook", UnitRole.RangedDps },
            { "pip", UnitRole.MeleeDps },
            { "torvan", UnitRole.MeleeDps },
            { "morwen", UnitRole.RangedDps },
        };

        /// <summary>Lowest level at which a companion's expected role is asserted. RoleInference reads the dominant talent
        /// tree only from 5 points (level 14); below that a Paladin or Shaman falls back to its class role (melee).</summary>
        static readonly Dictionary<string, int> RoleFromLevel = new Dictionary<string, int>
        {
            { "ysolde", 14 },
            { "nanami", 14 },
        };

        /// <summary>Talent ranks after spending the first <paramref name="points"/> entries of a build.</summary>
        static Dictionary<string, int> Prefix(string[] build, int points)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < Math.Min(points, build.Length); i++)
            {
                d.TryGetValue(build[i], out var n);
                d[build[i]] = n + 1;
            }
            return d;
        }

        static string Describe(Unit u)
        {
            var per = new Dictionary<string, int>();
            foreach (var kv in u.Talents)
                if (u.Db.TreeOfTalent.TryGetValue(kv.Key, out var t)) { per.TryGetValue(t.name, out var n); per[t.name] = n + kv.Value; }
            var parts = new List<string>();
            foreach (var kv in per) parts.Add(kv.Key + " " + kv.Value);
            return string.Join(", ", parts);
        }

        static bool SameTalents(Unit u, Dictionary<string, int> want)
        {
            if (u.Talents.Count != want.Count) return false;
            foreach (var kv in want) if (u.TalentRank(kv.Key) != kv.Value) return false;
            return true;
        }

        [Test]
        public static void PreferredTalents_AreLegalInOrder()
        {
            var db = Harness.Db;
            int max = Math.Max(1, db.Config.maxLevel);
            foreach (var comp in db.Companions.Values)
            {
                var build = comp.preferredTalents;
                if (build == null || build.Length == 0) continue;
                string tag = comp.id;
                Harness.Assert(build.Length <= Progression.TalentPointsTotal(max), $"{tag}: build has {build.Length} points (max {Progression.TalentPointsTotal(max)})");
                var u = UnitFactory.CreateCompanion(db, comp, max);
                Progression.ResetTalents(u);
                for (int i = 0; i < build.Length; i++)
                {
                    var id = build[i];
                    Harness.Assert(db.TreeOfTalent.TryGetValue(id, out var tree) && tree.classId == comp.classId, $"{tag}: entry {i} '{id}' is a {comp.classId} talent");
                    var why = Progression.CannotLearnTalent(u, id);
                    Harness.Assert(why == null, $"{tag}: entry {i} '{id}' is legal after the earlier entries ({why})");
                    if (why == null) Progression.LearnTalent(u, id);
                }
                Harness.Assert(Progression.TalentPointsSpent(u) == build.Length, $"{tag}: every entry spent");
            }
        }

        [Test]
        public static void Companions_KeepTheirRoles_VeteranRecruits()
        {
            var db = Harness.Db;
            foreach (int level in new[] { 10, 14, 20, 30, 40, 50, 60 })
            {
                var s = SessionTest.NewGame(ClassId.Mage, level, seed: (ulong)(500 + level));
                Harness.Assert(s.Settings.AutoAllocateCompanionTalents, "companions auto-allocate talents by default");
                foreach (var kv in ExpectedRoles)
                {
                    s.Recruit(kv.Key);
                    var u = s.FindMember(kv.Key);
                    string tag = $"{kv.Key} L{level}";
                    Harness.Assert(u != null && u.Level == level, $"{tag}: recruited at party level");
                    Harness.Assert(u.RoleOverride == UnitRole.Auto, $"{tag}: no role override");
                    Harness.Assert(Progression.TalentPointsAvailable(u) == 0, $"{tag}: all talent points spent");
                    var build = u.Companion.preferredTalents.Length > 0 ? u.Companion.preferredTalents : u.Class.defaultBuild;
                    Harness.Assert(SameTalents(u, Prefix(build, Progression.TalentPointsTotal(level))), $"{tag}: talents follow the build in order ({Describe(u)})");
                    if (RoleFromLevel.TryGetValue(kv.Key, out var from) && level < from) continue;
                    Harness.Assert(u.Role == kv.Value, $"{tag}: role {u.Role}, expected {kv.Value} ({Describe(u)})");
                }
            }
        }

        [Test]
        public static void KaelTanks_SerenHeals_LevellingOneToSixty()
        {
            var db = Harness.Db;
            var s = SessionTest.NewGame(ClassId.Rogue, 1, seed: 61);
            s.Recruit("kael");
            s.Recruit("seren");
            s.Recruit("aldric");
            var kael = s.FindMember("kael");
            var seren = s.FindMember("seren");
            var aldric = s.FindMember("aldric");
            int max = Math.Max(1, db.Config.maxLevel);
            while (s.Main.Level < max)
            {
                int before = s.Main.Level;
                s.GivePartyXp(Progression.XpToNextLevel(db, s.Main.Level) - s.Main.Xp);
                Harness.Assert(s.Main.Level == before + 1, $"levelled to {before + 1} (got {s.Main.Level})");
                int level = s.Main.Level;
                foreach (var u in new[] { kael, seren, aldric })
                {
                    Harness.Assert(u.Level == level, $"{u.Name} synced to {level}");
                    Harness.Assert(Progression.TalentPointsAvailable(u) == 0, $"{u.Name} L{level}: points auto-spent");
                    Harness.Assert(SameTalents(u, Prefix(u.Companion.preferredTalents, Progression.TalentPointsTotal(level))),
                        $"{u.Name} L{level}: talents follow preferredTalents in order ({Describe(u)})");
                }
                Harness.Assert(kael.Role == UnitRole.Tank, $"Kael L{level} is the tank (got {kael.Role}: {Describe(kael)})");
                Harness.Assert(seren.Role == UnitRole.Healer, $"Seren L{level} is the healer (got {seren.Role}: {Describe(seren)})");
                Harness.Assert(aldric.Role == UnitRole.MeleeDps, $"Aldric L{level} fights in melee (got {aldric.Role}: {Describe(aldric)})");
            }
            Harness.Assert(Progression.PointsInTree(kael, "tree_warrior_protection") >= 31, "Kael ends deep Protection");
            Harness.Assert(Progression.PointsInTree(seren, "tree_priest_holy") >= 31, "Seren ends deep Holy");
            Harness.Assert(kael.Knows("warrior_last_stand") && kael.Knows("warrior_concussion_blow"), "Kael has the Protection talent abilities");
            Harness.Assert(seren.Knows("priest_inner_focus") && seren.Knows("priest_lightwell"), "Seren has the healing talent abilities");
        }
    }
}
