// Sound playback bookkeeping (Core/Util/SfxBank.cs, used by Game/Audio/Sfx.cs): variant grouping by base id,
// round-robin without immediate repeats, jitter ranges, the base-id rate limit / voice cap / frame budget, the slot
// choice of the 24-source pool, the scaled-time delay queue and the recorded-override merge.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsSfxPlayback
    {
        [Test]
        public static void BaseIdAndVariantNumberParse()
        {
            Assert(SfxIds.BaseOf("hit_blade#3") == "hit_blade", "variant suffix stripped");
            Assert(SfxIds.BaseOf("hit_blade") == "hit_blade", "plain id unchanged");
            Assert(SfxIds.BaseOf("") == "" && SfxIds.BaseOf(null) == "", "empty and null give empty");
            Assert(SfxIds.BaseOf("#2") == "", "a bare suffix has an empty base");
            Assert(SfxIds.VariantOf("hit_blade#3") == 3 && SfxIds.VariantOf("x#12") == 12, "variant numbers");
            Assert(SfxIds.VariantOf("hit_blade") == 0 && SfxIds.VariantOf("x#") == 0 && SfxIds.VariantOf("x#a") == 0 && SfxIds.VariantOf(null) == 0,
                "no or malformed suffix is variant 0");
            Assert(SfxIds.VariantOf("x#99999999999") == 0, "an absurd suffix does not overflow");
        }

        [Test]
        public static void GroupingKeysVariantsByBaseIdInNumericOrder()
        {
            var g = SfxIds.Group(new[] { "x#10", "y", "x#2", "z#1", "x#1", "", "#3", "x#2", "x" });
            Assert(g.Count == 3, "three base ids (empty keys and bare suffixes skipped): " + string.Join(",", g.Keys));
            Assert(g["x"].SequenceEqual(new[] { "x", "x#1", "x#2", "x#10" }), "plain first, then numeric order, duplicates once: " + string.Join(",", g["x"]));
            Assert(g["y"].SequenceEqual(new[] { "y" }), "a plain id is a bank of one");
            Assert(g["z"].SequenceEqual(new[] { "z#1" }), "a single variant");
            var g2 = SfxIds.Group(new[] { "x#1", "x", "x#10", "x#2" });
            Assert(g2["x"].SequenceEqual(g["x"]), "order does not depend on the input order");
            Assert(SfxIds.Group(null).Count == 0, "null input");
        }

        [Test]
        public static void RoundRobinNeverRepeatsAndCoversEveryVariantEachCycle()
        {
            foreach (int k in new[] { 2, 3, 4, 6 })
            {
                var pick = new SfxVariantPicker(k);
                var rng = new Random(1234 + k);
                int last = -1;
                var counts = new int[k];
                for (int cycle = 0; cycle < 250; cycle++)
                {
                    var seen = new HashSet<int>();
                    for (int i = 0; i < k; i++)
                    {
                        int v = pick.Next(rng);
                        Assert(v >= 0 && v < k, $"K={k}: index in range");
                        Assert(v != last, $"K={k}: never the same variant twice in a row (cycle {cycle})");
                        Assert(seen.Add(v), $"K={k}: every variant once per cycle");
                        counts[v]++;
                        last = v;
                    }
                }
                Assert(counts.All(c => c == 250), $"K={k}: even use over many cycles");
            }
            var one = new SfxVariantPicker(1);
            for (int i = 0; i < 5; i++) Assert(one.Next(new Random(i)) == 0, "K=1 always plays the only clip");
            Assert(new SfxVariantPicker(0).Count == 1, "a bank of zero is treated as one (no crash)");
        }

        [Test]
        public static void RoundRobinIsDeterministicForASeedAndNotAPlainCycle()
        {
            int[] Run(int seed) { var p = new SfxVariantPicker(4); var r = new Random(seed); return Enumerable.Range(0, 40).Select(_ => p.Next(r)).ToArray(); }
            Assert(Run(7).SequenceEqual(Run(7)), "same seed, same sequence");
            var a = Run(7);
            bool plainCycle = true;
            for (int i = 4; i < a.Length; i++) if (a[i] != a[i - 4]) plainCycle = false;
            Assert(!plainCycle, "the order is reshuffled between cycles (not 1-2-3-4-1-2-3-4)");
        }

        [Test]
        public static void JitterStaysWithinThreePercentAndOneAndAHalfDecibels()
        {
            var rng = new Random(99);
            float minP = 9, maxP = 0, minG = 9, maxG = 0;
            double sumP = 0;
            for (int i = 0; i < 20000; i++)
            {
                float p = SfxJitter.Pitch(rng), g = SfxJitter.Gain(rng);
                minP = Math.Min(minP, p); maxP = Math.Max(maxP, p); sumP += p;
                minG = Math.Min(minG, g); maxG = Math.Max(maxG, g);
            }
            Assert(minP >= 0.97f - 1e-5f && maxP <= 1.03f + 1e-5f, $"pitch in ±3 % ({minP}..{maxP})");
            Assert(maxP - minP > 0.05f, "pitch actually varies");
            float lo = (float)Math.Pow(10, -1.5 / 20), hi = (float)Math.Pow(10, 1.5 / 20);
            Assert(minG >= lo - 1e-5f && maxG <= hi + 1e-5f, $"gain in ±1.5 dB ({minG}..{maxG})");
            Assert(maxG - minG > 0.3f, "gain actually varies");
            AssertNear((float)(sumP / 20000), 1f, 0.002f, "pitch jitter is centred");
            AssertNear(SfxJitter.DbToGain(-6.0206f), 0.5f, 1e-3f, "dB conversion");
            Assert(SfxJitter.Pitch(null) == 1f && SfxJitter.Gain(null) == 1f, "no rng, no jitter");
        }

        [Test]
        public static void StableIdsAreNotJittered()
        {
            foreach (var id in new[] { "ui_click", "ui_open", "ui_close", "level_up", "quest", "quest_accept", "quest_turnin", "loot_epic", "loot_legendary", "raid_warning", "boss_pull", "secret_found", "set_complete" })
                Assert(!SfxJitter.IsJittered(id), id + " plays as authored");
            foreach (var id in new[] { "hit_blade", "mat_plate", "swing", "footstep_dirt", "hit_physical", "coin", "bow", "parry", "vo_beast_yelp" })
                Assert(SfxJitter.IsJittered(id), id + " gets jitter");
            Assert(!SfxJitter.IsJittered("") && !SfxJitter.IsJittered(null), "empty ids are not jittered");
        }

        [Test]
        public static void RateLimitIsKeyedOnTheBaseId()
        {
            var r = new SfxVoiceRules();
            Assert(r.TryStart("hit_blade", 1.000f, 1), "first start");
            Assert(!r.TryStart("hit_blade", 1.020f, 2), "the same base id within 30 ms is dropped");
            Assert(r.TryStart("mat_plate", 1.020f, 2), "another id is independent");
            Assert(r.TryStart("hit_blade", 1.031f, 3), "after 30 ms it plays again");
            Assert(!r.TryStart("", 2f, 4) && !r.TryStart(null, 2f, 4), "empty ids never start");
            // a clock that went backwards (new play session) does not block forever
            Assert(r.TryStart("hit_blade", 0.5f, 5), "an earlier clock is not a rate-limit hit");
            r.Clear();
            Assert(r.TryStart("hit_blade", 0.5f, 6), "Clear forgets the history");
        }

        [Test]
        public static void FrameBudgetCapsNewVoicesButNotUi()
        {
            var r = new SfxVoiceRules();
            int frame = 10;
            for (int i = 0; i < SfxVoiceRules.FrameBudget; i++)
                Assert(r.TryStart("id" + i, 5f, frame), "within the budget " + i);
            Assert(r.UsedThisFrame(frame) == SfxVoiceRules.FrameBudget, "budget used");
            Assert(!r.TryStart("one_more", 5f, frame), "the 7th new voice in a frame is dropped");
            Assert(r.TryStart("ui_click", 5f, frame), "UI ids are exempt from the budget");
            Assert(r.UsedThisFrame(frame) == SfxVoiceRules.FrameBudget, "UI does not use budget");
            Assert(r.TryStart("one_more", 5f, frame + 1), "a new frame has a fresh budget (and a dropped id was not rate-limited)");
            Assert(r.UsedThisFrame(frame + 1) == 1 && r.UsedThisFrame(frame) == 0, "UsedThisFrame per frame");
            // a rate-limited sound costs no budget
            var r2 = new SfxVoiceRules();
            Assert(r2.TryStart("a", 1f, 1), "a");
            for (int i = 0; i < 10; i++) Assert(!r2.TryStart("a", 1f, 1), "rate limited");
            Assert(r2.UsedThisFrame(1) == 1, "rate-limited attempts did not consume the budget");
        }

        [Test]
        public static void SlotChoiceCapsFourVoicesPerBaseIdAndStealsTheOldest()
        {
            int n = SfxVoiceRules.PoolSize;
            Assert(n == 24, "pool of 24");
            var playing = new bool[n];
            var ids = new string[n];
            var start = new float[n];
            int next = 0;
            // 4 voices of hit_blade at slots 0..3 (slot 2 oldest)
            for (int i = 0; i < 4; i++) { playing[i] = true; ids[i] = "hit_blade"; start[i] = 10f + i; }
            start[2] = 1f;
            next = 4;
            int s = SfxVoiceRules.ChooseSlot("hit_blade", playing, ids, start, ref next);
            Assert(s == 2, "the 5th voice of one id steals the oldest of that id (got " + s + ")");
            Assert(next == 3, "next advances past the chosen slot");
            // another id takes the next free slot, round robin from next
            next = 4;
            s = SfxVoiceRules.ChooseSlot("mat_plate", playing, ids, start, ref next);
            Assert(s == 4 && next == 5, "free slot after next");
            next = 23;
            s = SfxVoiceRules.ChooseSlot("mat_plate", playing, ids, start, ref next);
            Assert(s == 23 && next == 0, "wraps round");
            // pool full: the oldest overall
            for (int i = 0; i < n; i++) { playing[i] = true; ids[i] = "v" + (i % 6); start[i] = 100f + i; }
            start[17] = 3f;
            next = 0;
            s = SfxVoiceRules.ChooseSlot("new", playing, ids, start, ref next);
            Assert(s == 17, "a full pool steals the oldest voice (got " + s + ")");
            // fewer than 4 of the same id do not steal
            Array.Clear(playing, 0, n);
            for (int i = 0; i < 3; i++) { playing[i] = true; ids[i] = "x"; start[i] = i; }
            next = 0;
            s = SfxVoiceRules.ChooseSlot("x", playing, ids, start, ref next);
            Assert(s == 3, "three overlapping voices: a free slot is used");
            next = -5;
            s = SfxVoiceRules.ChooseSlot("y", playing, ids, start, ref next);
            Assert(s >= 3 && s < n, "a negative next is tolerated");
            Assert(SfxVoiceRules.ChooseSlot("y", new bool[0], new string[0], new float[0], ref next) == -1, "an empty pool has no slot");
        }

        [Test]
        public static void DelayQueueFollowsTheCallersClockAndKeepsOrder()
        {
            var q = new SfxDelayQueue<string>();
            var outp = new List<string>();
            q.Add(10f, 0.62f, "fall");
            q.Add(10f, 0.66f, "clatter");
            q.Add(10f, 0.62f, "fall2");
            q.Add(10f, 0f, "now");
            q.Add(10f, -1f, "negative");
            q.Add(10f, float.NaN, "nan");
            Assert(q.PopDue(10f, outp) == 3 && outp.SequenceEqual(new[] { "now", "negative", "nan" }), "non-positive delays are due at once, in order: " + string.Join(",", outp));
            outp.Clear();
            // paused: the scaled clock does not move, nothing plays
            for (int i = 0; i < 5; i++) Assert(q.PopDue(10f, outp) == 0, "paused clock: nothing due");
            Assert(q.PopDue(10.61f, outp) == 0, "not yet");
            Assert(q.PopDue(10.6201f, outp) == 2 && outp.SequenceEqual(new[] { "fall", "fall2" }), "due at the delay; same due keeps insertion order");
            outp.Clear();
            // at 8× presentation speed the scaled clock moves 8× faster: the same delay passes in an eighth of the real time
            Assert(q.PopDue(10.6601f, outp) == 1 && outp[0] == "clatter", "the later one");
            Assert(q.Count == 0, "queue empty");
            q.Add(0f, 1f, "a");
            q.Clear();
            Assert(q.Count == 0 && q.PopDue(100f, outp) == 0, "Clear drops everything");
            // a clock that restarts keeps the remaining delay
            q.Add(50f, 0.5f, "r");
            q.Rebase(50.2f, 0f);
            Assert(q.PopDue(0.29f, outp) == 0 && q.PopDue(0.3f, null) == 1, "Rebase keeps the remaining 0.3 s (null sink allowed)");
        }

        sealed class FakeClip { public string Name; public FakeClip(string n) { Name = n; } }

        [Test]
        public static void RecordedOverridesReplaceSynthesizedVariantsOnlyWhenPresent()
        {
            var bank = new Dictionary<string, FakeClip[]>
            {
                ["hit_blade"] = new[] { new FakeClip("syn1"), new FakeClip("syn2") },
                ["mat_plate"] = new[] { new FakeClip("synA") },
            };
            var files = new Dictionary<string, FakeClip[]>
            {
                ["hit_blade"] = new[] { new FakeClip("rec_a"), null, new FakeClip("rec_b") },
                ["mat_plate"] = new FakeClip[0],
                ["custom_new"] = new[] { new FakeClip("rec_c") },
                ["throws"] = null,
            };
            int calls = 0;
            var done = SfxOverrides.Apply(bank, new[] { "hit_blade", "hit_blade#2", "mat_plate", "custom_new", "missing", "throws", "" },
                id => { calls++; if (id == "throws") throw new InvalidOperationException("io"); return files.TryGetValue(id, out var f) ? f : null; });
            Assert(done.SequenceEqual(new[] { "hit_blade", "custom_new" }), "overridden ids: " + string.Join(",", done));
            Assert(bank["hit_blade"].Select(c => c.Name).SequenceEqual(new[] { "rec_a", "rec_b" }), "recorded files replace every synthesized variant (nulls skipped)");
            Assert(bank["mat_plate"][0].Name == "synA", "an empty folder changes nothing");
            Assert(bank["custom_new"][0].Name == "rec_c", "an id that was not synthesized can be added");
            Assert(!bank.ContainsKey("missing"), "absent folder = no entry");
            Assert(calls == 5, "each base id probed once (hit_blade#2 folds into hit_blade; empty skipped): " + calls);
            Assert(SfxOverrides.Apply<FakeClip>(null, new[] { "a" }, _ => null).Count == 0, "null bank tolerated");
        }
    }
}
