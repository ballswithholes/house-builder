// `sfxpreview sheet`: a montage of spectrograms. Each cell is a true log-frequency STFT image (Spectrogram.cs:
// 50 Hz bottom … 16 kHz top, guide lines at 200 Hz / 1 kHz / 5 kHz, a fixed time window so lengths compare), labelled
// and tiled by ffmpeg (/usr/bin/ffmpeg: drawtext + tile).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Lanternvale.SfxPreview
{
    static class Sheet
    {
        public const string Ffmpeg = "/usr/bin/ffmpeg";
        static readonly string[] Fonts =
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/freefont/FreeMono.ttf",
        };

        /// <summary>One cell per entry (pcm null = blank); layout cols × rows (row-major). Returns 0 on success.</summary>
        public static int Make(string outPng, List<(string key, float[] pcm, string label)> cells, int cols, float seconds, int w = 320, int h = 150)
        {
            string ffmpeg = File.Exists(Ffmpeg) ? Ffmpeg : "ffmpeg";
            string font = null;
            foreach (var f in Fonts) if (File.Exists(f)) { font = f; break; }
            string tmp = Path.Combine(Path.GetTempPath(), "sfxsheet_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                int rows = (cells.Count + cols - 1) / cols;
                int total = rows * cols;
                int failed = 0;
                Parallel.For(0, total, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) }, i =>
                {
                    string png = Path.Combine(tmp, i.ToString("D4") + ".png");
                    string ppm = Path.Combine(tmp, i.ToString("D4") + ".ppm");
                    var a = new List<string> { "-v", "error", "-y" };
                    if (i < cells.Count && cells[i].pcm != null)
                    {
                        Spectrogram.WritePpm(ppm, cells[i].pcm, seconds, w, h);
                        a.AddRange(new[] { "-i", ppm });
                        if (font != null)
                        {
                            string txt = Path.Combine(tmp, i.ToString("D4") + ".txt");
                            File.WriteAllText(txt, cells[i].label ?? cells[i].key);
                            a.AddRange(new[] { "-vf", $"drawtext=fontfile={font}:textfile={txt}:expansion=none:x=4:y=3:fontsize=12:fontcolor=white:box=1:boxcolor=black@0.55:boxborderw=2" });
                        }
                    }
                    else a.AddRange(new[] { "-f", "lavfi", "-i", $"color=c=0x202020:s={w}x{h}" });
                    a.AddRange(new[] { "-frames:v", "1", png });
                    if (Run(ffmpeg, a) != 0) System.Threading.Interlocked.Increment(ref failed);
                });
                if (failed > 0) { Console.Error.WriteLine($"sheet: {failed} cell(s) failed (ffmpeg)"); return 1; }
                int rc = Run(ffmpeg, new List<string> { "-v", "error", "-y", "-framerate", "1", "-start_number", "0", "-i", Path.Combine(tmp, "%04d.png"),
                    "-vf", $"tile={cols}x{rows}:padding=3:color=0x303030", "-frames:v", "1", outPng });
                if (rc != 0) { Console.Error.WriteLine("sheet: montage failed (ffmpeg)"); return 1; }
                Console.WriteLine($"sheet: {outPng} ({cells.Count} cells, {cols}x{rows}, {seconds:0.##} s window, log frequency 50 Hz-16 kHz, guides at 200 Hz / 1 kHz / 5 kHz)");
                return 0;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        static int Run(string exe, List<string> args)
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var x in args) psi.ArgumentList.Add(x);
            using var p = Process.Start(psi);
            string err = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 && err.Length > 0) Console.Error.WriteLine(err.Trim());
            return p.ExitCode;
        }
    }
}
