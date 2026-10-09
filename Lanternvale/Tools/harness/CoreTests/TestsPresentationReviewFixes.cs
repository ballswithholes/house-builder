// Presentation review fixes (expansion): NpcDef.scale, the size of an NPC's model (a gnoll pup on the adult gnoll's
// model, the old frost drake and her granddaughter, a sheepdog on the wolf). The game's unit view and preview3d multiply
// the model's natural height by it; the validator keeps it in [0.3, 3].
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsPresentationReviewFixes
    {
        [Test]
        public static void NpcScale_DefaultsToOneAndSizesThePupsAndDrakes()
        {
            var want = new Dictionary<string, float>
            {
                ["am_nib"] = 0.6f, ["am_nib_farm"] = 0.6f, ["am_biscuit"] = 0.7f,
                ["sr_glimmerwing"] = 1.6f, ["sr_icicle"] = 0.8f,
            };
            foreach (var kv in want)
            {
                Assert(Db.Npcs.TryGetValue(kv.Key, out var n), "npc " + kv.Key + " exists");
                Assert(Math.Abs(n.scale - kv.Value) < 1e-4f, $"{kv.Key}: scale {n.scale}, want {kv.Value}");
            }
            // an NPC without the key keeps its model's natural size
            Assert(Db.Npcs.TryGetValue("elder_maru", out var maru) && maru.scale == 1f, "elder_maru: scale defaults to 1");
            Assert(new NpcDef().scale == 1f, "NpcDef.scale defaults to 1");
            foreach (var n in Db.Npcs.Values)
                Assert(n.scale >= DataValidator.NpcScaleMin && n.scale <= DataValidator.NpcScaleMax, $"npc {n.id}: scale {n.scale} in range");
        }

        const string BadScales = @"{
  ""npcs"": [
    {""id"": ""xt_tiny"", ""name"": ""X"", ""sprite"": ""cr_gnoll"", ""scale"": 0},
    {""id"": ""xt_huge"", ""name"": ""X"", ""sprite"": ""cr_gnoll"", ""scale"": 5},
    {""id"": ""xt_fine"", ""name"": ""X"", ""sprite"": ""cr_gnoll"", ""scale"": 2.5}
  ]
}";

        [Test]
        public static void NpcScale_ValidatorRejectsOutOfRange()
        {
            var files = ReadDataFiles(DataDir).ToList();
            files.Add(new KeyValuePair<string, string>("zz_presentation_bad_scales.json", BadScales));
            var db = GameDatabase.Load(files);
            Assert(db.Problems.Count == 0, "the bundle parses (scale is a known key): " + string.Join("; ", db.Problems));
            var p = DataValidator.Validate(db);
            Assert(p.Any(x => x.StartsWith("npc xt_tiny", StringComparison.Ordinal) && x.Contains("scale")), "scale 0 is rejected:\n  " + string.Join("\n  ", p));
            Assert(p.Any(x => x.StartsWith("npc xt_huge", StringComparison.Ordinal) && x.Contains("scale")), "scale 5 is rejected");
            Assert(!p.Any(x => x.StartsWith("npc xt_fine", StringComparison.Ordinal)), "scale 2.5 passes");
            Assert(!p.Any(x => !x.Contains("xt_")), "the real data stays clean: " + string.Join("; ", p.Where(x => !x.Contains("xt_"))));
        }
    }
}
