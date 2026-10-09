// `sfxpreview check`: the audio test suite. Every assertion adds a line to the report; any failure makes the
// command exit non-zero. Run it after every change to Game/Audio (README.md lists what each check guards).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lanternvale.Game;

namespace Lanternvale.SfxPreview
{
    sealed class CheckOptions
    {
        /// <summary>Wall-clock budget for a cold GeneratePcm (all tiers, default threads), ms.</summary>
        public double BudgetMs = 4000;
        /// <summary>Budget for tier 0 alone (UI + core combat), ms.</summary>
        public double Tier0BudgetMs = 2000;
        public bool Verbose;
        public string Root;
    }

    sealed class Checks
    {
        readonly CheckOptions opt;
        int passed, failed;
        readonly List<string> failures = new List<string>();

        public Checks(CheckOptions o) { opt = o; }

        void Ok(string what) { passed++; if (opt.Verbose) Console.WriteLine("  ok    " + what); }
        void Fail(string what) { failed++; failures.Add(what); Console.WriteLine("  FAIL  " + what); }
        void Expect(bool cond, string what) { if (cond) Ok(what); else Fail(what); }

        public int Run()
        {
            Console.WriteLine("== sfxpreview check");

            // ---- timing (cold: includes JIT, like the game's first synthesis) and the published key set
            var errors = new List<string>();
            var sw = Stopwatch.StartNew();
            var pcm = SfxSynth.GeneratePcm(errors);
            double coldMs = sw.Elapsed.TotalMilliseconds;
            var tierMs = (double[])SfxSynth.LastTierMs.Clone();
            sw.Restart();
            var seq = new Dictionary<string, float[]>(StringComparer.Ordinal);
            for (int t = 0; t < SfxSynth.TierCount; t++)
                foreach (var kv in SfxSynth.GeneratePcmTier(t, null, 1)) seq[kv.Key] = kv.Value;
            double seqMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var pcm2 = SfxSynth.GeneratePcm(null);
            double warmMs = sw.Elapsed.TotalMilliseconds;
            var warmTierMs = (double[])SfxSynth.LastTierMs.Clone();

            long samples = 0;
            int clips = 0;
            foreach (var kv in pcm) if (kv.Key.IndexOf('#') >= 0) { samples += kv.Value.Length; clips++; }
            Console.WriteLine($"  synthesis: {SfxSynth.Recipes.Count} ids, {clips} clips, {samples / 44100.0:0.0} s of audio, " +
                              $"{samples * 4 / 1048576.0:0.0} MB float");
            Console.WriteLine($"  time: cold {coldMs:0} ms (tiers {string.Join(" / ", tierMs.Select(x => x.ToString("0")))}), " +
                              $"warm {warmMs:0} ms (tiers {string.Join(" / ", warmTierMs.Select(x => x.ToString("0")))}), " +
                              $"single thread {seqMs:0} ms, {Environment.ProcessorCount} cores");

            Expect(errors.Count == 0, "no synthesis errors" + (errors.Count > 0 ? ": " + string.Join("; ", errors) : ""));
            Expect(coldMs <= opt.BudgetMs, $"cold synthesis {coldMs:0} ms within the {opt.BudgetMs:0} ms budget");
            Expect(tierMs[0] <= opt.Tier0BudgetMs, $"tier 0 (UI + core combat) {tierMs[0]:0} ms within {opt.Tier0BudgetMs:0} ms");

            // ---- determinism: parallel vs single thread vs a second parallel run, bit for bit
            int diff = 0;
            var diffKeys = new List<string>();
            foreach (var kv in pcm)
            {
                bool same = seq.TryGetValue(kv.Key, out var s) && pcm2.TryGetValue(kv.Key, out var p2)
                            && Analysis.Hash(s) == Analysis.Hash(kv.Value) && Analysis.Hash(p2) == Analysis.Hash(kv.Value);
                if (!same) { diff++; if (diffKeys.Count < 8) diffKeys.Add(kv.Key); }
            }
            Expect(diff == 0 && seq.Count == pcm.Count && pcm2.Count == pcm.Count,
                   $"deterministic: {pcm.Count} keys identical across parallel / single-thread / repeated runs" +
                   (diff > 0 ? " (differs: " + string.Join(", ", diffKeys) + ")" : ""));

            // ---- published keys: every id as id#1..K plus the plain id (= the same array as #1)
            foreach (var r in SfxSynth.Recipes)
            {
                int need = Spec.MinVariants(r.Id);
                bool keys = pcm.ContainsKey(r.Id);
                for (int v = 1; v <= r.Variants; v++) keys &= pcm.ContainsKey(SfxSynth.VariantKey(r.Id, v));
                keys &= !pcm.ContainsKey(SfxSynth.VariantKey(r.Id, r.Variants + 1));
                Expect(keys, $"{r.Id}: keys {r.Id}, {r.Id}#1..#{r.Variants}");
                Expect(r.Variants >= need, $"{r.Id}: {r.Variants} variants (need ≥ {need})");
                if (keys) Expect(ReferenceEquals(pcm[r.Id], pcm[SfxSynth.VariantKey(r.Id, 1)]), $"{r.Id}: plain id is variant 1");
                Expect(r.Peak <= 0.9f, $"{r.Id}: declared peak {r.Peak} ≤ 0.9");
                if (r.Variants > 1 && keys)
                {
                    var hashes = new HashSet<ulong>();
                    for (int v = 1; v <= r.Variants; v++) hashes.Add(Analysis.Hash(pcm[SfxSynth.VariantKey(r.Id, v)]));
                    Expect(hashes.Count == r.Variants, $"{r.Id}: all {r.Variants} variants differ");
                    if (Spec.MustVary(r.Id))
                    {
                        float worst = 0f;
                        for (int v = 1; v <= r.Variants; v++)
                            for (int w = v + 1; w <= r.Variants; w++)
                                worst = Math.Max(worst, Analysis.MaxXcorr(pcm[SfxSynth.VariantKey(r.Id, v)], pcm[SfxSynth.VariantKey(r.Id, w)]));
                        Expect(worst < Spec.MaxVariantXcorr,
                               $"{r.Id}: variants sound different (max cross-correlation {worst:0.000} < {Spec.MaxVariantXcorr})");
                    }
                }
            }
            foreach (var key in pcm.Keys)
                Expect(SfxSynth.Recipe(SfxSynth.BaseId(key)) != null, $"{key}: belongs to a recipe");
            foreach (var id in Spec.AllIds()) Expect(SfxSynth.Recipe(id) != null, $"{id}: synthesized (Expansion.md §4 / original id)");

            // ---- the original UI and reward clips are byte-identical to the pre-expansion recipes
            foreach (var kv in Spec.LegacyHashes)
                Expect(pcm.TryGetValue(kv.Key, out var x) && Analysis.Hash(x) == kv.Value, $"{kv.Key}: byte-identical to the original clip");

            // ---- every clip: finite, peak, DC, clean tail, length; the spectral guard on hit_* / mat_*
            var metrics = new Dictionary<string, Metrics>(StringComparer.Ordinal);
            foreach (var kv in pcm.Where(k => k.Key.IndexOf('#') >= 0).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var r = SfxSynth.Recipe(kv.Key);
                var m = Analysis.Measure(kv.Value);
                metrics[kv.Key] = m;
                string k = kv.Key;
                Expect(m.Finite, $"{k}: no NaN/Inf");
                Expect(m.Peak <= 0.9f + 1e-4f, $"{k}: peak {m.Peak:0.000} ≤ 0.9");
                Expect(Math.Abs(m.Peak - r.Peak) <= r.Peak * 0.05f, $"{k}: peak {m.Peak:0.000} within 5 % of {r.Peak}");
                if (r.Legacy)
                {
                    // the original clips are pinned byte-identical (above); report, don't fail, their old DC / tails
                    if (Math.Abs(m.Dc) >= 0.01f || m.TailDb >= -40f)
                        Console.WriteLine($"  note  {k}: original clip kept as is (|DC| {Math.Abs(m.Dc):0.0000}, tail {m.TailDb:0} dBFS)");
                }
                else
                {
                    Expect(Math.Abs(m.Dc) < 0.01f, $"{k}: |DC| {Math.Abs(m.Dc):0.0000} < 0.01");
                    Expect(m.TailDb < -40f, $"{k}: last 10 ms at {m.TailDb:0} dBFS < -40 (no click at the end)");
                }
                Expect(m.Seconds >= 0.02f && m.Seconds <= r.Seconds + 1e-3f, $"{k}: length {m.Seconds:0.000} s in [0.02, {r.Seconds}]");
                if (Spec.Guarded(r.Id) && m.Finite)
                {
                    Expect(m.Low <= 0.5f, $"{k}: {m.Low * 100:0.0} % of the energy below 200 Hz (≤ 50 %)");
                    Expect(m.Mid >= 0.25f, $"{k}: {m.Mid * 100:0.0} % of the energy in 1–5 kHz (≥ 25 %)");
                }
            }

            // ---- distinctness: each variant is nearer its own id's centroid than any other id's in its group
            foreach (var g in Spec.Groups.Where(g => g.Distinct)) CheckDistinct(g, metrics);

            // ---- ids referenced by game code exist
            CheckCallSites(pcm);

            // ---- music
            CheckMusic();

            Console.WriteLine();
            Console.WriteLine($"sfxpreview check: {passed} passed, {failed} failed");
            if (failed > 0)
            {
                Console.WriteLine("failures:");
                foreach (var f in failures.Take(60)) Console.WriteLine("  " + f);
            }
            return failed == 0 ? 0 : 1;
        }

        void CheckDistinct(Spec.Group g, Dictionary<string, Metrics> metrics)
        {
            var feats = new Dictionary<string, List<float[]>>();
            foreach (var id in g.Ids)
            {
                var r = SfxSynth.Recipe(id);
                if (r == null) continue;
                var l = new List<float[]>();
                for (int v = 1; v <= r.Variants; v++)
                    if (metrics.TryGetValue(SfxSynth.VariantKey(id, v), out var m) && m.Bands != null) l.Add(Analysis.Features(m));
                if (l.Count > 0) feats[id] = l;
            }
            var cents = feats.ToDictionary(kv => kv.Key, kv => Analysis.Mean(kv.Value));
            int wrong = 0;
            float minPair = float.MaxValue;
            string minA = "", minB = "";
            foreach (var a in feats)
            {
                // leave-one-out nearest centroid
                foreach (var f in a.Value)
                {
                    float[] own;
                    if (a.Value.Count > 1)
                    {
                        var rest = a.Value.Where(x => !ReferenceEquals(x, f)).ToList();
                        own = Analysis.Mean(rest);
                    }
                    else own = cents[a.Key];
                    float dOwn = Analysis.Distance(f, own);
                    foreach (var b in cents)
                    {
                        if (b.Key == a.Key) continue;
                        if (Analysis.Distance(f, b.Value) <= dOwn)
                        {
                            wrong++;
                            Console.WriteLine($"        {a.Key}: a variant sits nearer {b.Key} ({Analysis.Distance(f, b.Value):0.0} vs own {dOwn:0.0})");
                            break;
                        }
                    }
                }
                foreach (var b in cents)
                {
                    if (string.CompareOrdinal(a.Key, b.Key) >= 0) continue;
                    float d = Analysis.Distance(cents[a.Key], b.Value);
                    if (d < minPair) { minPair = d; minA = a.Key; minB = b.Key; }
                }
            }
            Expect(wrong == 0, $"{g.Name}: every variant is nearest its own id (leave-one-out, {feats.Count} ids)");
            Expect(minPair >= 3f, $"{g.Name}: closest pair {minA} / {minB} differs by {minPair:0.0} dB (≥ 3)");
        }

        void CheckCallSites(Dictionary<string, float[]> pcm)
        {
            string scripts = Path.Combine(opt.Root, "Assets", "Lanternvale", "Scripts");
            if (!Directory.Exists(scripts)) { Fail("call sites: Scripts folder not found at " + scripts); return; }
            var call = new Regex(@"(?:\bSfx\.Play\w*|\bUi\.Sfx\?\.Invoke|\bSfx\.Has|\bSfx\.Clip)\s*\(([^;]*)", RegexOptions.Compiled);
            var lit = new Regex("\"([a-z][a-z0-9_]*)\"", RegexOptions.Compiled);
            var aliases = new HashSet<string>(Spec.Aliases);
            int sites = 0, bad = 0;
            foreach (var file in Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in call.Matches(lines[i]))
                    {
                        var args = m.Groups[1].Value;
                        // only the first argument's string literals (the id), e.g. Sfx.Play(crit ? "a" : "b", pos)
                        int depth = 0, end = args.Length;
                        for (int c = 0; c < args.Length; c++)
                        {
                            if (args[c] == '(') depth++;
                            else if (args[c] == ')') { if (depth == 0) { end = c; break; } depth--; }
                            else if (args[c] == ',' && depth == 0) { end = c; break; }
                        }
                        foreach (Match l in lit.Matches(args.Substring(0, end)))
                        {
                            sites++;
                            string id = l.Groups[1].Value;
                            if (!pcm.ContainsKey(id) && !aliases.Contains(id))
                            {
                                bad++;
                                Fail($"call site {Path.GetRelativePath(opt.Root, file)}:{i + 1} plays unknown sfx id \"{id}\"");
                            }
                        }
                    }
                }
            }
            if (bad == 0) Ok($"call sites: {sites} sfx id literals in game code all exist");
            Console.WriteLine($"  call sites: {sites} id literals checked");
        }

        void CheckMusic()
        {
            // explicit keys and keyword fallbacks
            var cases = new (string input, string mood)[]
            {
                ("music_lanternvale lanternvale", "village"), ("music_whisperwood whisperwood", "forest"), ("music_shrine shrine", "shrine"),
                ("combat", "combat"), ("boss_battle", "combat"), ("menu", "menu"), ("", "village"), (null, "village"),
                ("music_highlands amberfield", "highlands"), ("music_town brightwater", "town"), ("music_fen mirefen", "fen"),
                ("music_peaks skyreach", "peaks"), ("music_dungeon dgn_barrow", "dungeon"), ("music_dungeon dgn_root_hollows", "dungeon"),
                ("music_raid raid_hollow_heart", "raid"), ("music_raid raid_ashwyrm_roost", "raid"),
                ("raid_hollow_heart", "raid"), ("amberfield", "highlands"), ("brightwater", "town"), ("mirefen", "fen"),
                ("skyreach", "peaks"), ("dgn_frozen_sanctum", "dungeon"), ("hollow", "shrine"), ("crypt", "dungeon"),
                ("MUSIC_FEN", "fen"), ("music_shrine raid_hollow_heart", "shrine"), ("music_lanternvale hollow_place", "shrine"),
            };
            foreach (var c in cases)
                Expect(MoodLibrary.Normalize(c.input) == c.mood, $"music: Normalize(\"{c.input}\") = {c.mood} (got {MoodLibrary.Normalize(c.input)})");
            foreach (var mood in MoodLibrary.All)
            {
                Expect(MoodLibrary.Normalize(mood) == mood && MoodLibrary.Get(mood).name == mood, $"music: mood {mood} maps to itself");
                Expect(MoodLibrary.Normalize("music_" + mood) == mood, $"music: music_{mood} → {mood}");
            }

            // every mood is musically distinct (tempo, metre, key and mode)
            var sig = new Dictionary<string, string>();
            foreach (var mood in MoodLibrary.All)
            {
                var s = MoodLibrary.Get(mood);
                string k = $"{s.bpm}/{s.stepsPerBar}/{s.root}/{string.Join(",", s.mode)}";
                Expect(!sig.ContainsKey(k), $"music: {mood} has its own tempo/metre/key/mode" + (sig.ContainsKey(k) ? " (same as " + sig[k] + ")" : ""));
                sig[k] = mood;
            }

            // render each mood offline: finite, below full scale, audible, and allocation-free voices keep running
            foreach (var mood in MoodLibrary.All)
            {
                var m = Program.RenderMusic(mood, 16f, out var lr);
                Expect(m.Finite, $"music {mood}: no NaN/Inf");
                Expect(m.Peak < 1f, $"music {mood}: peak {m.Peak:0.000} < 1");
                Expect(m.Rms > 0.01f && m.Rms < 0.4f, $"music {mood}: rms {m.Rms:0.000} in (0.01, 0.4)");
                if (opt.Verbose) Console.WriteLine($"        music {mood}: peak {m.Peak:0.000} rms {m.Rms:0.000} centroid {m.CentroidHz:0} Hz");
            }

            // the maps' music keys resolve to the intended moods
            string data = Path.Combine(opt.Root, "Assets", "Lanternvale", "Resources", "Data");
            int maps = 0;
            foreach (var file in Directory.EnumerateFiles(data, "*.json", SearchOption.AllDirectories))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                foreach (var (id, music) in MapsWithMusic(doc.RootElement))
                {
                    maps++;
                    string got = MoodLibrary.Normalize(music + " " + id);
                    string want = ExpectedMood(id);
                    if (want != null) Expect(got == want, $"music: map {id} ({music}) plays {want} (got {got})");
                    else Console.WriteLine($"        map {id} ({music}) plays {got}");
                }
            }
            Expect(maps >= 3, $"music: found {maps} maps with a music key in the data");
        }

        static string ExpectedMood(string mapId)
        {
            if (mapId.StartsWith("raid_")) return "raid";
            if (mapId.StartsWith("dgn_")) return "dungeon";
            switch (mapId)
            {
                case "lanternvale": return "village";
                case "whisperwood": return "forest";
                case "shrine": return "shrine";
                case "amberfield": return "highlands";
                case "brightwater": return "town";
                case "mirefen": return "fen";
                case "skyreach": return "peaks";
                default: return null;
            }
        }

        static IEnumerable<(string, string)> MapsWithMusic(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && e.TryGetProperty("music", out var mu) && mu.ValueKind == JsonValueKind.String)
                    yield return (id.GetString(), mu.GetString());
                foreach (var p in e.EnumerateObject())
                    foreach (var x in MapsWithMusic(p.Value)) yield return x;
            }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var a in e.EnumerateArray())
                    foreach (var x in MapsWithMusic(a)) yield return x;
        }
    }
}
