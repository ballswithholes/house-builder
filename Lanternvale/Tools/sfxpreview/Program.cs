// sfxpreview: offline rendering, spectrogram sheets and checks for Lanternvale's procedural audio. See README.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lanternvale.Game;

namespace Lanternvale.SfxPreview
{
    static class Program
    {
        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0) return Usage();
            var pos = new List<string>();
            var flags = new Dictionary<string, string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string k = args[i].Substring(2);
                    bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--") && k != "variants" && k != "verbose" && k != "spec";
                    flags[k] = hasValue ? args[++i] : "true";
                }
                else pos.Add(args[i]);
            }
            if (flags.TryGetValue("threads", out var th)) SfxSynth.MaxThreads = int.Parse(th);
            string root = flags.TryGetValue("root", out var rr) ? rr : FindRoot();
            try
            {
                switch (args[0])
                {
                    case "check":
                    {
                        var o = new CheckOptions { Root = root, Verbose = flags.ContainsKey("verbose") };
                        if (flags.TryGetValue("budget-ms", out var b)) o.BudgetMs = double.Parse(b);
                        if (flags.TryGetValue("tier0-budget-ms", out var b0)) o.Tier0BudgetMs = double.Parse(b0);
                        return new Checks(o).Run();
                    }
                    case "render": return Render(pos, flags);
                    case "sheet": return SheetCmd(pos, flags);
                    case "stats": return Stats(pos);
                    case "list": return List();
                    case "music": return MusicCmd(pos, flags);
                    default: return Usage();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("sfxpreview: " + e);
                return 2;
            }
        }

        static int Usage()
        {
            Console.WriteLine(@"usage:
  sfxpreview.sh check [--budget-ms 4000] [--tier0-budget-ms 2000] [--threads N] [--verbose]
  sfxpreview.sh render <dir> [ids|groups|all] [--variants] [--spec]   WAVs (+ spectrogram PNGs) and metrics.tsv
  sfxpreview.sh sheet <out.png> [ids|groups|all] [--variants] [--cols N] [--dur S]
  sfxpreview.sh stats [ids|groups|all]                                metrics table (all variants)
  sfxpreview.sh list                                                  ids, variants, tier, peak, length
  sfxpreview.sh music <mood|all> <out.wav|dir> [--seconds 30]          render the generative score offline
groups: " + string.Join(" ", Spec.Groups.Select(g => g.Name)) + " original");
            return 2;
        }

        static string FindRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "Assets", "Lanternvale", "Scripts"))) return d.FullName;
                d = d.Parent;
            }
            d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "Assets", "Lanternvale", "Scripts"))) return d.FullName;
                d = d.Parent;
            }
            return Directory.GetCurrentDirectory();
        }

        /// <summary>Expands "all", group names and comma lists into base ids.</summary>
        static List<string> Select(string spec)
        {
            var l = new List<string>();
            if (string.IsNullOrEmpty(spec) || spec == "all")
            {
                foreach (var r in SfxSynth.Recipes) l.Add(r.Id);
                return l;
            }
            foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var g = Spec.Find(part);
                if (g != null) l.AddRange(g.Ids);
                else if (part == "original") l.AddRange(Spec.Original);
                else l.Add(SfxSynth.BaseId(part));
            }
            return l.Distinct().ToList();
        }

        static List<string> Keys(List<string> ids, bool variants)
        {
            var keys = new List<string>();
            foreach (var id in ids)
            {
                var r = SfxSynth.Recipe(id) ?? throw new ArgumentException("unknown sfx id: " + id);
                if (variants) for (int v = 1; v <= r.Variants; v++) keys.Add(SfxSynth.VariantKey(id, v));
                else keys.Add(SfxSynth.VariantKey(id, 1));
            }
            return keys;
        }

        static float[] RenderKey(string key)
        {
            var r = SfxSynth.Recipe(key);
            int i = key.IndexOf('#');
            int v = i < 0 ? 1 : int.Parse(key.Substring(i + 1));
            return SfxSynth.Render(r, v);
        }

        static string Row(string key, Metrics m) =>
            $"{key,-24} {m.Seconds,6:0.000} {m.Peak,6:0.000} {m.Rms,6:0.000} {m.Low * 100,6:0.0} {m.LowMid * 100,6:0.0} {m.Mid * 100,6:0.0} {m.High * 100,6:0.0} {m.CentroidHz,7:0} {m.T20Ms,6:0} {m.T40Ms,6:0} {m.TailDb,6:0}";

        const string Header = "key                       len s   peak    rms  <200%  .2-1k%  1-5k%   >5k%  centHz  t20ms  t40ms tailDb";

        static int Render(List<string> pos, Dictionary<string, string> flags)
        {
            if (pos.Count < 1) return Usage();
            string dir = pos[0];
            Directory.CreateDirectory(dir);
            var keys = Keys(Select(pos.Count > 1 ? pos[1] : "all"), flags.ContainsKey("variants"));
            using var tsv = new StreamWriter(Path.Combine(dir, "metrics.tsv"));
            tsv.WriteLine("key\tseconds\tpeak\trms\tlow200\tlowmid\tmid1to5k\thigh5k\tcentroid\tt20ms\tt40ms\ttaildb");
            Console.WriteLine(Header);
            foreach (var key in keys)
            {
                var pcm = RenderKey(key);
                var m = Analysis.Measure(pcm);
                string file = Path.Combine(dir, Analysis.FileName(key) + ".wav");
                Analysis.WriteWav(file, pcm);
                Console.WriteLine(Row(key, m));
                tsv.WriteLine($"{key}\t{m.Seconds:0.0000}\t{m.Peak:0.0000}\t{m.Rms:0.0000}\t{m.Low:0.0000}\t{m.LowMid:0.0000}\t{m.Mid:0.0000}\t{m.High:0.0000}\t{m.CentroidHz:0}\t{m.T20Ms:0}\t{m.T40Ms:0}\t{m.TailDb:0}");
                if (flags.ContainsKey("spec"))
                {
                    var cells = new List<(string, float[], string)> { (key, pcm, key) };
                    Sheet.Make(Path.Combine(dir, Analysis.FileName(key) + ".png"), cells, 1, Math.Max(0.3f, m.Seconds), 640, 320);
                }
            }
            Console.WriteLine($"render: {keys.Count} clips → {dir}");
            return 0;
        }

        static int SheetCmd(List<string> pos, Dictionary<string, string> flags)
        {
            if (pos.Count < 1) return Usage();
            string sel = pos.Count > 1 ? pos[1] : "all";
            bool variants = flags.ContainsKey("variants");
            if (sel == "all" && !flags.ContainsKey("dur"))
            {
                // one sheet per group: out.png → out_<group>.png
                string baseName = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pos[0])), Path.GetFileNameWithoutExtension(pos[0]));
                int rc = 0;
                foreach (var g in Spec.Groups) rc |= SheetFor(baseName + "_" + g.Name + ".png", g.Ids.ToList(), variants, flags, g.SheetSeconds);
                return rc;
            }
            var group = Spec.Find(sel);
            float dur = group != null ? group.SheetSeconds : 0.8f;
            return SheetFor(pos[0], Select(sel), variants, flags, dur);
        }

        static int SheetFor(string png, List<string> ids, bool variants, Dictionary<string, string> flags, float dur)
        {
            if (flags.TryGetValue("dur", out var d)) dur = float.Parse(d, CultureInfo.InvariantCulture);
            var keys = Keys(ids, variants);
            int cols = flags.TryGetValue("cols", out var c) ? int.Parse(c) : variants ? Math.Max(1, ids.Max(id => SfxSynth.Recipe(id).Variants)) : 4;
            var cells = new List<(string, float[], string)>();
            foreach (var id in ids)
            {
                var r = SfxSynth.Recipe(id);
                int n = variants ? r.Variants : 1;
                for (int v = 1; v <= n; v++)
                {
                    var key = SfxSynth.VariantKey(id, v);
                    var pcm = RenderKey(key);
                    var m = Analysis.Measure(pcm);
                    cells.Add((key, pcm, $"{key}  <200 {m.Low * 100:0}%  1-5k {m.Mid * 100:0}%  t20 {m.T20Ms:0}ms"));
                }
                if (variants) for (int v = n; v < cols; v++) cells.Add((null, null, null)); // blank cells finish the row
            }
            return Sheet.Make(png, cells, cols, dur);
        }

        static int Stats(List<string> pos)
        {
            var keys = Keys(Select(pos.Count > 0 ? pos[0] : "all"), true);
            Console.WriteLine(Header);
            foreach (var key in keys) Console.WriteLine(Row(key, Analysis.Measure(RenderKey(key))));
            return 0;
        }

        static int List()
        {
            Console.WriteLine($"{"id",-22} {"K",2} {"tier",4} {"peak",5} {"len s",6}  kind");
            foreach (var r in SfxSynth.Recipes)
                Console.WriteLine($"{r.Id,-22} {r.Variants,2} {r.Tier,4} {r.Peak,5:0.00} {r.Seconds,6:0.00}  {(r.Legacy ? "original (byte-identical)" : "layered")}");
            return 0;
        }

        static int MusicCmd(List<string> pos, Dictionary<string, string> flags)
        {
            if (pos.Count < 2) return Usage();
            float secs = flags.TryGetValue("seconds", out var s) ? float.Parse(s, CultureInfo.InvariantCulture) : 30f;
            var moods = pos[0] == "all" ? MoodLibrary.All.ToList() : new List<string> { pos[0] };
            foreach (var mood in moods)
            {
                var m = RenderMusic(mood, secs, out var lr);
                string file = moods.Count == 1 && pos[1].EndsWith(".wav") ? pos[1] : Path.Combine(pos[1], "music_" + mood + ".wav");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
                Analysis.WriteWavStereo(file, lr, 44100);
                var spec = MoodLibrary.Get(mood);
                Console.WriteLine($"{mood,-10} {spec.bpm,5:0} bpm {spec.stepsPerBar}/8 root {spec.root,3}  peak {m.Peak:0.000} rms {m.Rms:0.000} centroid {m.CentroidHz:0} Hz → {file}");
            }
            return 0;
        }

        /// <summary>Renders a mood through the real MusicEngine (as OnAudioFilterRead would), returns mono metrics.</summary>
        public static Metrics RenderMusic(string mood, float seconds, out float[] stereo)
        {
            const int sr = 44100;
            var engine = new MusicEngine(sr) { TargetGain = 0.6f };
            engine.Submit(new Composer(MoodLibrary.Get(mood), sr, 0.05f, 12345u));
            int frames = (int)(seconds * sr);
            stereo = new float[frames * 2];
            var block = new float[1024 * 2];
            for (int f = 0; f < frames; f += 1024)
            {
                int n = Math.Min(1024, frames - f);
                Array.Clear(block, 0, block.Length);
                var buf = n == 1024 ? block : new float[n * 2];
                engine.Render(buf, 2);
                Array.Copy(buf, 0, stereo, f * 2, n * 2);
            }
            var mono = new float[frames];
            for (int i = 0; i < frames; i++) mono[i] = 0.5f * (stereo[2 * i] + stereo[2 * i + 1]);
            return Analysis.Measure(mono);
        }
    }
}
