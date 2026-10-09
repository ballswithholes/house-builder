// A true log-frequency spectrogram image (STFT, Hann window) written as a binary PPM for ffmpeg to label and tile.
// y: 50 Hz (bottom) … 16 kHz (top), log scale, with faint guide lines at 200 Hz, 1 kHz and 5 kHz (the spectral-guard
// band edges); x: a fixed time window, so clip lengths compare across cells; colour: 90 dB range under the cell's peak.
using System;
using System.IO;

namespace Lanternvale.SfxPreview
{
    static class Spectrogram
    {
        const float FLo = 50f, FHi = 16000f;

        public static void WritePpm(string path, float[] x, float seconds, int w, int h)
        {
            const int n = 2048;
            int sr = Analysis.SampleRate;
            var win = new double[n];
            for (int i = 0; i < n; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            var mag = new float[w, h];
            float max = 1e-12f;
            var re = new double[n];
            var im = new double[n];
            for (int col = 0; col < w; col++)
            {
                double centre = (col + 0.5) / w * seconds * sr;
                int start = (int)centre - n / 2;
                for (int i = 0; i < n; i++)
                {
                    int k = start + i;
                    re[i] = k >= 0 && k < x.Length ? x[k] * win[i] : 0;
                    im[i] = 0;
                }
                Analysis.Fft(re, im);
                for (int row = 0; row < h; row++)
                {
                    double f = FLo * Math.Pow(FHi / FLo, (h - 1 - row) / (double)(h - 1));
                    double bin = f * n / sr;
                    int b0 = (int)bin;
                    double t = bin - b0;
                    double p0 = re[b0] * re[b0] + im[b0] * im[b0];
                    double p1 = re[b0 + 1] * re[b0 + 1] + im[b0 + 1] * im[b0 + 1];
                    float p = (float)(p0 + (p1 - p0) * t);
                    mag[col, row] = p;
                    if (p > max) max = p;
                }
            }
            var guide = new bool[h];
            foreach (float g in new[] { 200f, 1000f, 5000f })
            {
                int row = (int)Math.Round((h - 1) - Math.Log(g / FLo) / Math.Log(FHi / FLo) * (h - 1));
                if (row >= 0 && row < h) guide[row] = true;
            }
            using var fs = File.Create(path);
            var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
            fs.Write(header, 0, header.Length);
            var px = new byte[w * h * 3];
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    double db = 10 * Math.Log10(Math.Max(mag[col, row] / max, 1e-12));
                    float v = (float)Math.Clamp((db + 90.0) / 90.0, 0.0, 1.0);
                    Colour(v, out byte r, out byte g, out byte b);
                    if (guide[row] && v < 0.5f) { r = (byte)Math.Max(r, (byte)70); g = (byte)Math.Max(g, (byte)70); b = (byte)Math.Max(b, (byte)90); }
                    int o = (row * w + col) * 3;
                    px[o] = r; px[o + 1] = g; px[o + 2] = b;
                }
            fs.Write(px, 0, px.Length);
        }

        /// <summary>Black → purple → red → orange → pale yellow ("inferno"-like).</summary>
        static void Colour(float v, out byte r, out byte g, out byte b)
        {
            float[,] stops = { { 0f, 0f, 0f }, { 0.25f, 0.05f, 0.4f }, { 0.65f, 0.1f, 0.45f }, { 0.95f, 0.45f, 0.1f }, { 1f, 0.95f, 0.6f } };
            float s = v * 4f;
            int i = Math.Min(3, (int)s);
            float t = s - i;
            r = (byte)(255 * (stops[i, 0] + (stops[i + 1, 0] - stops[i, 0]) * t));
            g = (byte)(255 * (stops[i, 1] + (stops[i + 1, 1] - stops[i, 1]) * t));
            b = (byte)(255 * (stops[i, 2] + (stops[i + 1, 2] - stops[i, 2]) * t));
        }
    }
}
