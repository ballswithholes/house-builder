// Regression tests of the flow/UI review: the Heart Lantern's relight (flag lanterns_rekindled) covers the valley only,
// so the expansion's own dark lanterns and flag-driven dark/lit pairs (Brightwater's memorial, Mirefen's rising
// lanterns, the Drowned Vault, the Hollow Heart's grove) stay dark until their own story moments.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;

namespace Lanternvale.Tests
{
    public static class TestsFlowUiReviewFixes
    {
        static readonly string[] StoryLanternMaps = { "brightwater", "mirefen", "dgn_drowned_vault", "raid_hollow_heart" };

        [Test]
        public static void Rekindle_IsValleyOnly()
        {
            var db = Harness.Db;
            foreach (var id in GameSession.RekindleMapIds)
                Harness.Assert(db.Maps.ContainsKey(id) && GameSession.IsRekindleMap(db.Maps[id]), "valley map " + id);
            foreach (var id in StoryLanternMaps)
            {
                Harness.Assert(db.Maps.TryGetValue(id, out var def), "map " + id);
                Harness.Assert(!GameSession.IsRekindleMap(def), id + " is not relit by the Heart Lantern");
                Harness.Assert(def.props.Any(p => p.art == "prop_spirit_lantern_dark"), id + " keeps dark lanterns of its own");
            }
            Harness.Assert(!GameSession.IsRekindleMap(null), "no map, no relight");
        }

        [Test]
        public static void Rekindle_EventOnlyOnValleyMaps_EnterAndLoad()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 20, seed: 4411);
            s.Flags.Set(GameSession.LanternsFlag, 1);
            var seen = new List<SessionEvent>();
            Action<SessionEvent> watch = e => { if (e.Kind == SessionEventKind.SpecialOutcome && e.Id == GameSession.RekindleLanternsSpecial) seen.Add(e); };
            s.EventRaised += watch;
            try
            {
                foreach (var id in GameSession.RekindleMapIds.Concat(new[] { "brightwater", "mirefen", "dgn_drowned_vault", "lanternvale" }))
                {
                    seen.Clear();
                    s.EnterMap(id);
                    Harness.Assert(s.MapId == id, "entered " + id);
                    bool valley = GameSession.IsRekindleMap(s.MapDef);
                    Harness.Assert(s.LanternsLitHere == valley, id + ": LanternsLitHere " + s.LanternsLitHere);
                    Harness.Assert(seen.Count(e => e.Amount == 0) == (valley ? 1 : 0), id + ": silent relight raised " + seen.Count + "×, valley " + valley);
                }
                s.EnterMap("mirefen");
                string json = s.SaveGame();
                var s2 = new GameSession(Harness.Db, 4412);
                var seen2 = new List<SessionEvent>();
                s2.EventRaised += e => { if (e.Kind == SessionEventKind.SpecialOutcome && e.Id == GameSession.RekindleLanternsSpecial) seen2.Add(e); };
                Harness.Assert(s2.LoadGame(json, out var err), "load: " + err);
                Harness.Assert(s2.LanternsRekindled && !s2.LanternsLitHere && seen2.Count == 0, "loading in Mirefen does not relight its lanterns");
                s2.EnterMap("lanternvale");
                Harness.Assert(s2.LanternsLitHere && seen2.Count(e => e.Amount == 0) == 1, "back in the valley the lanterns are lit");
            }
            finally { s.EventRaised -= watch; }
        }
    }
}
