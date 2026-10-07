// Measurements on rendered clips: spectrum (FFT), band energy shares, envelope decay, tail level, feature vectors for
// the distinctness check, plus WAV writing and hashing.
using System;
using System.IO;

namespace Lanternvale.SfxPreview
{
    sealed class Metrics
    {
        public float Seconds, Peak, Rms, Dc, TailDb, CentroidHz, T20Ms, T40Ms;
        /// <summary>Energy shares (0–1) below 200 Hz, 200 Hz–1 kHz, 1–5 kHz and above 5 kHz.</summary>
        public float Low, LowMid, Mid, High;
        public bool Finite = true;
        /// <summary>Third-octave band levels in dB relative to the total (100 Hz … 12.5 kHz).</summary>
        public float[] Bands;
    }

    static class Analysis
    {
        public const int SampleRate = 44100;
        /// <summary>Third-octave centres 100 Hz … 12.5 kHz (22 bands).</summary>
        public static readonly float[] BandCentres = BuildCentres();

        static float[] BuildCentres()
        {
            var c = new float[22];
            for (int i = 0; i < c.Length; i++) c[i] = 100f * (float)Math.Pow(2.0, i / 3.0);
            return c;
        }

        public static Metrics Measure(float[] x)
        {
            var m = new Metrics { Seconds = x.Length / (float)SampleRate };
            double sum = 0, sq = 0;
            for (int i = 0; i < x.Length; i++)
            {
                float v = x[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) { m.Finite = false; continue; }
                m.Peak = Math.Max(m.Peak, Math.Abs(v));
                sum += v; sq += v * v;
            }
            m.Dc = (float)(sum / Math.Max(1, x.Length));
            m.Rms = (float)Math.Sqrt(sq / Math.Max(1, x.Length));
            int tail = Math.Min(x.Length, SampleRate / 100);
            float tp = 0f;
            for (int i = x.Length - tail; i < x.Length; i++) tp = Math.Max(tp, Math.Abs(x[i]));
            m.TailDb = Db(tp);
            if (!m.Finite) return m;

            // envelope decay: time after the envelope peak until it stays below -20 / -40 dB (5 ms RMS windows)
            int win = SampleRate / 200;
            int nw = Math.Max(1, x.Length / win);
            var env = new float[nw];
            int peakW = 0;
            for (int w = 0; w < nw; w++)
            {
                double e = 0;
                for (int i = w * win; i < Math.Min(x.Length, (w + 1) * win); i++) e += x[i] * x[i];
                env[w] = (float)Math.Sqrt(e / win);
                if (env[w] > env[peakW]) peakW = w;
            }
            m.T20Ms = DecayMs(env, peakW, 0.1f, win);
            m.T40Ms = DecayMs(env, peakW, 0.01f, win);

            // spectrum of the whole clip
            int n = 1;
            while (n < x.Length) n <<= 1;
            n = Math.Max(n, 4096);
            var re = new double[n];
            var im = new double[n];
            for (int i = 0; i < x.Length; i++) re[i] = x[i];
            Fft(re, im);
            double total = 0, low = 0, lowMid = 0, mid = 0, high = 0, cen = 0;
            var bands = new double[BandCentres.Length];
            double binHz = (double)SampleRate / n;
            double edge = Math.Pow(2.0, 1.0 / 6.0);
            for (int k = 1; k < n / 2; k++)
            {
                double p = re[k] * re[k] + im[k] * im[k];
                double f = k * binHz;
                total += p;
                cen += p * f;
                if (f < 200) low += p; else if (f < 1000) lowMid += p; else if (f < 5000) mid += p; else high += p;
                for (int b = 0; b < bands.Length; b++)
                    if (f >= BandCentres[b] / edge && f < BandCentres[b] * edge) { bands[b] += p; break; }
            }
            total = Math.Max(total, 1e-30);
            m.Low = (float)(low / total); m.LowMid = (float)(lowMid / total); m.Mid = (float)(mid / total); m.High = (float)(high / total);
            m.CentroidHz = (float)(cen / total);
            m.Bands = new float[bands.Length];
            for (int b = 0; b < bands.Length; b++) m.Bands[b] = (float)(10.0 * Math.Log10(Math.Max(bands[b] / total, 1e-9)));
            return m;
        }

        static float DecayMs(float[] env, int peakW, float ratio, int win)
        {
            float thr = env[peakW] * ratio;
            int last = peakW;
            for (int w = peakW; w < env.Length; w++) if (env[w] >= thr) last = w;
            return (last - peakW + 1) * win * 1000f / SampleRate;
        }

        public static float Db(float a) => a <= 1e-9f ? -180f : 20f * (float)Math.Log10(a);

        /// <summary>In-place iterative radix-2 FFT (n a power of two).</summary>
        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                    }
                }
            }
        }

        /// <summary>
        /// Feature vector for distinctness: the 22 third-octave levels (dB re total, floored at -50) plus the
        /// envelope's -20 dB decay time on a log scale (10 dB per factor 10 in time, weighted ×2).
        /// </summary>
        public static float[] Features(Metrics m)
        {
            var f = new float[m.Bands.Length + 1];
            for (int i = 0; i < m.Bands.Length; i++) f[i] = Math.Max(-50f, m.Bands[i]);
            f[m.Bands.Length] = 20f * (float)Math.Log10(Math.Max(5f, m.T20Ms));
            return f;
        }

        /// <summary>RMS difference of two feature vectors (≈ dB).</summary>
        public static float Distance(float[] a, float[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++) { double d = a[i] - b[i]; s += d * d; }
            return (float)Math.Sqrt(s / a.Length);
        }

        public static float[] Mean(System.Collections.Generic.List<float[]> v)
        {
            var m = new float[v[0].Length];
            foreach (var x in v) for (int i = 0; i < m.Length; i++) m[i] += x[i] / v.Count;
            return m;
        }

        public static ulong Hash(float[] x)
        {
            ulong h = 14695981039346656037UL;
            foreach (var v in x)
            {
                uint u = BitConverter.SingleToUInt32Bits(v);
                for (int i = 0; i < 4; i++) { h ^= (u >> (8 * i)) & 0xFF; h *= 1099511628211UL; }
            }
            return h;
        }

        /// <summary>16-bit PCM mono WAV.</summary>
        public static void WriteWav(string path, float[] d, int sr = SampleRate)
        {
            using var w = new BinaryWriter(File.Create(path));
            int n = d.Length;
            w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVEfmt "u8.ToArray());
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(n * 2);
            foreach (var x in d) w.Write((short)Math.Clamp((int)Math.Round(x * 32767f), -32768, 32767));
        }

        /// <summary>16-bit PCM stereo WAV from interleaved samples.</summary>
        public static void WriteWavStereo(string path, float[] lr, int sr)
        {
            using var w = new BinaryWriter(File.Create(path));
            int n = lr.Length;
            w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVEfmt "u8.ToArray());
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(sr); w.Write(sr * 4); w.Write((short)4); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(n * 2);
            foreach (var x in lr) w.Write((short)Math.Clamp((int)Math.Round(x * 32767f), -32768, 32767));
        }

        /// <summary>File-safe name for a key ("hit_blade#2" → "hit_blade__v2").</summary>
        public static string FileName(string key) => key.Replace("#", "__v");
    }
}
