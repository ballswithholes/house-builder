// Offline synthesis of the game's sound effects into AudioClips (generated once at startup).
// Soft, rounded timbres to match the cosy art: plucked harps (Karplus-Strong), music-box bells,
// filtered noise for whooshes and impacts. Deterministic (fixed seeds).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    internal sealed class SynthBuffer
    {
        public enum Wave { Sine, Triangle, Square, Saw }

        public const int SampleRate = 44100;
        public readonly float[] Data;
        readonly System.Random rng;

        public SynthBuffer(float seconds, int seed)
        {
            Data = new float[Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate))];
            rng = new System.Random(seed);
        }

        float Rand() => (float)rng.NextDouble() * 2f - 1f;

        static float Osc(Wave w, float ph)
        {
            switch (w)
            {
                case Wave.Triangle: return 1f - 4f * Mathf.Abs(ph - 0.5f);
                case Wave.Square: return ph < 0.5f ? 0.7f : -0.7f;
                case Wave.Saw: return ph * 2f - 1f;
                default: return Mathf.Sin(ph * Mathf.PI * 2f);
            }
        }

        static float Lp(float hz) => hz <= 0f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * hz / SampleRate);

        /// <summary>Tone with exponential pitch sweep f0→f1, linear attack, exponential decay (time constant), optional vibrato and low-pass.</summary>
        public void Tone(float start, float dur, float f0, float f1, float amp, float attack, float decay, Wave wave = Wave.Sine,
                         float vibHz = 0f, float vibDepth = 0f, float lowpassHz = 0f)
        {
            int s0 = (int)(start * SampleRate), n = (int)(dur * SampleRate);
            float ph = 0f, lp = 0f, a = Lp(lowpassHz);
            float ratio = f1 / Mathf.Max(1f, f0);
            int rel = Mathf.Min(n, (int)(0.006f * SampleRate));
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = f0 * Mathf.Pow(ratio, i / (float)n);
                if (vibHz > 0f) f *= 1f + vibDepth * Mathf.Sin(2f * Mathf.PI * vibHz * t);
                ph += f / SampleRate;
                ph -= Mathf.Floor(ph);
                float env = t < attack ? t / Mathf.Max(1e-5f, attack) : Mathf.Exp(-(t - attack) / Mathf.Max(1e-4f, decay));
                if (i > n - rel) env *= (n - i) / (float)rel;
                float s = Osc(wave, ph);
                if (lowpassHz > 0f) { lp += (s - lp) * a; s = lp; }
                Data[s0 + i] += s * env * amp;
            }
        }

        /// <summary>Noise burst with a low-pass sweeping fc0→fc1 and optional high-pass.</summary>
        public void Noise(float start, float dur, float amp, float attack, float decay, float fc0 = 8000f, float fc1 = -1f, float highpassHz = 0f, bool bellShape = false)
        {
            int s0 = (int)(start * SampleRate), n = (int)(dur * SampleRate);
            if (fc1 < 0f) fc1 = fc0;
            float lp = 0f, lp2 = 0f, hp = 0f, ah = Lp(highpassHz);
            int rel = Mathf.Min(n, (int)(0.008f * SampleRate));
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)n;
                float a = Lp(Mathf.Lerp(fc0, fc1, u));
                float x = Rand();
                lp += (x - lp) * a;
                lp2 += (lp - lp2) * a;
                float s = lp2;
                if (highpassHz > 0f) { hp += (s - hp) * ah; s -= hp; }
                float env = bellShape ? Mathf.Pow(Mathf.Sin(Mathf.PI * u), 2f)
                    : (t < attack ? t / Mathf.Max(1e-5f, attack) : Mathf.Exp(-(t - attack) / Mathf.Max(1e-4f, decay)));
                if (i > n - rel) env *= (n - i) / (float)rel;
                Data[s0 + i] += s * env * amp * 2.2f;
            }
        }

        /// <summary>Karplus-Strong pluck (harp / bow string).</summary>
        public void Pluck(float start, float freq, float amp, float damp = 0.996f, float brightness = 0.5f)
        {
            int s0 = (int)(start * SampleRate);
            int len = Mathf.Max(2, Mathf.RoundToInt(SampleRate / Mathf.Max(20f, freq)));
            var buf = new float[len];
            float lp = 0f, a = Mathf.Lerp(0.15f, 0.9f, brightness);
            for (int i = 0; i < len; i++) { lp += (Rand() - lp) * a; buf[i] = lp; }
            int pos = 0;
            int n = Data.Length - s0;
            for (int i = 0; i < n; i++)
            {
                int nxt = pos + 1 == len ? 0 : pos + 1;
                float y = buf[pos];
                buf[pos] = (buf[pos] + buf[nxt]) * 0.5f * damp;
                pos = nxt;
                float env = i < 40 ? i / 40f : 1f;
                Data[s0 + i] += y * amp * env * 1.6f;
            }
        }

        /// <summary>Music-box / bell: decaying partials at the given frequency ratios.</summary>
        public void Bell(float start, float freq, float amp, float decay, float[] ratios = null, float[] amps = null)
        {
            ratios = ratios ?? new[] { 1f, 2f, 4.07f };
            amps = amps ?? new[] { 1f, 0.28f, 0.12f };
            int s0 = (int)(start * SampleRate);
            int n = Mathf.Min(Data.Length - s0, (int)((decay * 7f + 0.02f) * SampleRate));
            for (int k = 0; k < ratios.Length; k++)
            {
                float f = freq * ratios[k];
                if (f > SampleRate * 0.45f) continue;
                float d = decay / (1f + k * 1.3f);
                float inc = f / SampleRate, ph = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = (t < 0.002f ? t / 0.002f : 1f) * Mathf.Exp(-t / d);
                    ph += inc; ph -= Mathf.Floor(ph);
                    Data[s0 + i] += Mathf.Sin(ph * Mathf.PI * 2f) * env * amp * amps[Mathf.Min(k, amps.Length - 1)];
                }
            }
        }

        /// <summary>Random crackles (fire).</summary>
        public void Crackle(float start, float dur, float perSecond, float amp)
        {
            int count = Mathf.RoundToInt(dur * perSecond);
            for (int c = 0; c < count; c++)
            {
                float t = start + (float)rng.NextDouble() * dur;
                int s0 = (int)(t * SampleRate);
                int n = (int)(0.004f * SampleRate);
                float a = amp * (0.4f + 0.6f * (float)rng.NextDouble()) * (1f - (t - start) / dur);
                for (int i = 0; i < n && s0 + i < Data.Length; i++)
                    Data[s0 + i] += Rand() * a * (1f - i / (float)n);
            }
        }

        /// <summary>Normalises to the peak and fades the tail; returns the samples (thread-safe, no Unity API).</summary>
        public float[] Finish(float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < Data.Length; i++) max = Mathf.Max(max, Mathf.Abs(Data[i]));
            float k = peak / max;
            int fade = Mathf.Min(Data.Length, (int)(0.012f * SampleRate));
            for (int i = 0; i < Data.Length; i++)
            {
                float v = Data[i] * k;
                int fromEnd = Data.Length - 1 - i;
                if (fromEnd < fade) v *= fromEnd / (float)fade;
                Data[i] = v;
            }
            return Data;
        }

        /// <summary>Creates an AudioClip from finished samples (main thread only).</summary>
        public static AudioClip ToClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    internal static class SfxSynth
    {
        static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

        /// <summary>
        /// Synthesizes every effect as mono 44.1 kHz samples. Pure maths (no Unity API), so it can run
        /// on a worker thread; ~0.1–0.3 s of CPU in total.
        /// </summary>
        public static Dictionary<string, float[]> GeneratePcm(List<string> errors = null)
        {
            var d = new Dictionary<string, float[]>(StringComparer.Ordinal);
            void Add(string id, float peak, float seconds, int seed, Action<SynthBuffer> make)
            {
                try
                {
                    var b = new SynthBuffer(seconds, seed);
                    make(b);
                    d[id] = b.Finish(peak);
                }
                catch (Exception e) { errors?.Add(id + ": " + e.Message); }
            }
            var W = SynthBuffer.Wave.Sine;

            Add("ui_click", 0.32f, 0.07f, 1, b =>
            {
                b.Tone(0f, 0.05f, 2200f, 1700f, 0.6f, 0.0008f, 0.012f);
                b.Noise(0f, 0.012f, 0.3f, 0.0005f, 0.004f, 6000f);
            });
            Add("ui_open", 0.42f, 0.6f, 2, b =>
            {
                b.Bell(0f, Midi(79), 0.5f, 0.12f);
                b.Bell(0.07f, Midi(86), 0.45f, 0.14f);
                b.Noise(0f, 0.25f, 0.05f, 0.08f, 0.1f, 9000f, 9000f, 4000f);
            });
            Add("ui_close", 0.36f, 0.45f, 3, b =>
            {
                b.Bell(0f, Midi(84), 0.4f, 0.1f);
                b.Bell(0.06f, Midi(79), 0.4f, 0.12f);
            });
            Add("hit_physical", 0.8f, 0.28f, 4, b =>
            {
                b.Tone(0f, 0.22f, 150f, 55f, 0.9f, 0.002f, 0.06f);
                b.Noise(0f, 0.14f, 0.7f, 0.001f, 0.035f, 2200f, 900f);
                b.Noise(0f, 0.03f, 0.35f, 0.0005f, 0.01f, 6000f);
            });
            Add("hit_crit", 0.9f, 0.6f, 5, b =>
            {
                b.Tone(0f, 0.3f, 180f, 50f, 1f, 0.002f, 0.08f);
                b.Noise(0f, 0.2f, 0.9f, 0.001f, 0.05f, 3000f, 800f);
                b.Bell(0f, 1320f, 0.35f, 0.09f, new[] { 1f, 2.76f, 5.4f }, new[] { 1f, 0.5f, 0.25f });
                b.Tone(0.01f, 0.25f, 600f, 150f, 0.2f, 0.003f, 0.08f, SynthBuffer.Wave.Triangle);
            });
            Add("swing", 0.55f, 0.3f, 6, b => b.Noise(0f, 0.28f, 0.8f, 0f, 0f, 700f, 3200f, 300f, true));
            Add("bow", 0.6f, 0.45f, 7, b =>
            {
                b.Pluck(0f, 196f, 0.7f, 0.985f, 0.7f);
                b.Noise(0f, 0.03f, 0.4f, 0.001f, 0.01f, 3000f);
                b.Noise(0.02f, 0.2f, 0.25f, 0f, 0f, 1500f, 4000f, 800f, true);
            });
            Add("cast_start", 0.45f, 0.9f, 8, b =>
            {
                int[] steps = { 0, 2, 4, 7, 9, 12 };
                for (int i = 0; i < steps.Length; i++) b.Bell(i * 0.075f, Midi(72 + steps[i]), 0.25f, 0.12f);
                b.Noise(0f, 0.7f, 0.12f, 0f, 0f, 9000f, 9000f, 4000f, true);
            });
            Add("impact_fire", 0.75f, 0.8f, 9, b =>
            {
                b.Noise(0f, 0.7f, 0.8f, 0.01f, 0.22f, 3500f, 400f);
                b.Tone(0f, 0.45f, 95f, 50f, 0.55f, 0.005f, 0.15f);
                b.Crackle(0.04f, 0.6f, 60f, 0.35f);
            });
            Add("impact_frost", 0.6f, 0.8f, 10, b =>
            {
                var r = new[] { 1f, 2.41f, 3.93f };
                var a = new[] { 1f, 0.4f, 0.2f };
                b.Bell(0f, 2093f, 0.32f, 0.12f, r, a);
                b.Bell(0.025f, 2637f, 0.26f, 0.1f, r, a);
                b.Bell(0.05f, 3136f, 0.2f, 0.09f, r, a);
                b.Noise(0f, 0.3f, 0.45f, 0.001f, 0.07f, 9000f, 6000f, 2500f);
            });
            Add("impact_arcane", 0.6f, 0.8f, 11, b =>
            {
                b.Tone(0f, 0.65f, 880f, 330f, 0.45f, 0.005f, 0.22f, W, 9f, 0.03f);
                b.Tone(0f, 0.65f, 1320f, 495f, 0.2f, 0.005f, 0.2f, W, 9f, 0.03f);
                b.Bell(0.05f, Midi(96), 0.12f, 0.06f);
                b.Bell(0.12f, Midi(100), 0.1f, 0.06f);
                b.Bell(0.2f, Midi(103), 0.08f, 0.06f);
            });
            Add("impact_shadow", 0.65f, 0.9f, 12, b =>
            {
                b.Tone(0f, 0.8f, 82f, 68f, 0.6f, 0.06f, 0.3f);
                b.Tone(0f, 0.8f, 85.5f, 70f, 0.5f, 0.06f, 0.3f);
                b.Noise(0f, 0.65f, 0.5f, 0f, 0f, 300f, 1400f, 0f, true);
            });
            Add("impact_holy", 0.55f, 1.1f, 13, b =>
            {
                b.Bell(0f, Midi(84), 0.4f, 0.2f);
                b.Bell(0.02f, Midi(88), 0.35f, 0.2f);
                b.Bell(0.04f, Midi(91), 0.32f, 0.2f);
                b.Bell(0.06f, Midi(96), 0.18f, 0.16f);
                b.Noise(0f, 0.8f, 0.06f, 0f, 0f, 12000f, 12000f, 6000f, true);
            });
            Add("impact_nature", 0.6f, 0.7f, 14, b =>
            {
                b.Tone(0f, 0.12f, 320f, 170f, 0.6f, 0.002f, 0.04f);
                b.Noise(0.02f, 0.45f, 0.35f, 0f, 0f, 2600f, 1500f, 600f, true);
                b.Pluck(0f, 330f, 0.25f, 0.975f, 0.4f);
            });
            Add("heal", 0.5f, 1.4f, 15, b =>
            {
                int[] notes = { 72, 76, 79, 84, 88 };
                for (int i = 0; i < notes.Length; i++) b.Pluck(i * 0.075f, Midi(notes[i]), 0.45f - i * 0.04f, 0.997f, 0.45f);
                b.Noise(0f, 1f, 0.05f, 0f, 0f, 11000f, 11000f, 5000f, true);
            });
            Add("buff", 0.45f, 0.75f, 16, b =>
            {
                b.Bell(0f, Midi(76), 0.4f, 0.14f);
                b.Bell(0.09f, Midi(83), 0.4f, 0.17f);
                b.Noise(0f, 0.5f, 0.06f, 0f, 0f, 10000f, 10000f, 5000f, true);
            });
            Add("debuff", 0.45f, 0.7f, 17, b =>
            {
                b.Tone(0f, 0.5f, 440f, 415f, 0.35f, 0.01f, 0.22f, SynthBuffer.Wave.Triangle, 6f, 0.02f, 2500f);
                b.Tone(0.09f, 0.5f, 415f, 370f, 0.3f, 0.01f, 0.22f, SynthBuffer.Wave.Triangle, 6f, 0.02f, 2500f);
            });
            Add("death", 0.5f, 1.2f, 18, b =>
            {
                b.Tone(0f, 1f, 330f, 150f, 0.45f, 0.02f, 0.4f, SynthBuffer.Wave.Triangle, 4f, 0.01f, 1800f);
                b.Noise(0f, 0.9f, 0.18f, 0.05f, 0.35f, 1200f, 500f);
            });
            Add("level_up", 0.55f, 2f, 19, b =>
            {
                int[] notes = { 72, 76, 79, 84, 88 };
                for (int i = 0; i < notes.Length; i++) b.Bell(i * 0.1f, Midi(notes[i]), 0.35f, 0.22f);
                b.Pluck(0.5f, Midi(60), 0.3f, 0.998f, 0.4f);
                b.Pluck(0.5f, Midi(64), 0.25f, 0.998f, 0.4f);
                b.Pluck(0.5f, Midi(67), 0.25f, 0.998f, 0.4f);
                b.Bell(0.5f, Midi(96), 0.12f, 0.3f);
                b.Noise(0.45f, 1.2f, 0.06f, 0f, 0f, 12000f, 12000f, 6000f, true);
            });
            Add("quest", 0.5f, 1.4f, 20, b =>
            {
                b.Bell(0f, Midi(79), 0.42f, 0.22f);
                b.Bell(0.14f, Midi(81), 0.42f, 0.22f);
                b.Bell(0.28f, Midi(86), 0.45f, 0.3f);
                b.Pluck(0.28f, Midi(62), 0.25f, 0.997f, 0.4f);
            });
            Add("coin", 0.42f, 0.45f, 21, b =>
            {
                var r = new[] { 1f, 2.7f };
                var a = new[] { 1f, 0.35f };
                b.Bell(0f, Midi(95), 0.4f, 0.05f, r, a);
                b.Bell(0.07f, Midi(100), 0.45f, 0.08f, r, a);
            });
            Add("footstep_grass", 0.3f, 0.13f, 22, b =>
            {
                b.Noise(0f, 0.11f, 0.6f, 0.004f, 0.03f, 1800f, 800f);
                b.Noise(0f, 0.05f, 0.2f, 0.002f, 0.012f, 7000f, 7000f, 3000f);
            });
            Add("chest_open", 0.55f, 1.1f, 23, b =>
            {
                b.Tone(0f, 0.32f, 170f, 230f, 0.3f, 0.02f, 0.25f, SynthBuffer.Wave.Saw, 26f, 0.06f, 900f);
                b.Tone(0.3f, 0.16f, 120f, 70f, 0.6f, 0.002f, 0.05f);
                b.Noise(0.3f, 0.06f, 0.35f, 0.001f, 0.02f, 900f);
                b.Bell(0.42f, Midi(91), 0.3f, 0.16f);
                b.Bell(0.5f, Midi(96), 0.28f, 0.16f);
                b.Bell(0.58f, Midi(100), 0.25f, 0.2f);
            });
            Add("door", 0.55f, 0.8f, 24, b =>
            {
                b.Tone(0f, 0.42f, 150f, 118f, 0.3f, 0.03f, 0.3f, SynthBuffer.Wave.Triangle, 21f, 0.08f, 1200f);
                b.Tone(0.42f, 0.2f, 110f, 58f, 0.7f, 0.002f, 0.06f);
                b.Noise(0.42f, 0.1f, 0.45f, 0.001f, 0.03f, 800f);
            });
            return d;
        }
    }
}
