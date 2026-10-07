// Physically informed DSP building blocks for the procedural sound effects (SfxSynth).
//
// Every impact is built from layers: a contact transient (the "click" of first contact), a body (the resonant modes
// of the struck object, excited by a contact pulse whose length sets the hardness), and a tail (debris grains, a small
// baked room). The primitives here are pure maths on float arrays: they use System.Math only (no Unity API, no shared
// state), so a SynthBuffer can be rendered on any worker thread, deterministically from its seed.
//
// C# 9 / net472: no MathF, no Span, no records.
using System;

namespace Lanternvale.Game
{
    /// <summary>RBJ "Audio EQ Cookbook" biquad (transposed direct form II). A struct: copy = independent state.</summary>
    internal struct Biquad
    {
        public enum Kind { LowPass, HighPass, BandPass, Peak, Notch, LowShelf, HighShelf }

        float b0, b1, b2, a1, a2, z1, z2;

        public static Biquad Make(Kind kind, float hz, float q, float dbGain = 0f)
        {
            var f = new Biquad();
            f.Set(kind, hz, q, dbGain);
            return f;
        }

        /// <summary>Recomputes the coefficients and keeps the state (safe for sweeps).</summary>
        public void Set(Kind kind, float hz, float q, float dbGain = 0f)
        {
            double sr = SynthBuffer.SampleRate;
            double f = Math.Max(10.0, Math.Min(sr * 0.49, hz));
            double w = 2.0 * Math.PI * f / sr, cw = Math.Cos(w), sw = Math.Sin(w);
            double alpha = sw / (2.0 * Math.Max(0.05, q));
            double A = Math.Pow(10.0, dbGain / 40.0);
            double nb0, nb1, nb2, na0, na1, na2;
            switch (kind)
            {
                case Kind.HighPass:
                    nb0 = (1 + cw) / 2; nb1 = -(1 + cw); nb2 = (1 + cw) / 2; na0 = 1 + alpha; na1 = -2 * cw; na2 = 1 - alpha; break;
                case Kind.BandPass: // constant 0 dB peak gain
                    nb0 = alpha; nb1 = 0; nb2 = -alpha; na0 = 1 + alpha; na1 = -2 * cw; na2 = 1 - alpha; break;
                case Kind.Peak:
                    nb0 = 1 + alpha * A; nb1 = -2 * cw; nb2 = 1 - alpha * A; na0 = 1 + alpha / A; na1 = -2 * cw; na2 = 1 - alpha / A; break;
                case Kind.Notch:
                    nb0 = 1; nb1 = -2 * cw; nb2 = 1; na0 = 1 + alpha; na1 = -2 * cw; na2 = 1 - alpha; break;
                case Kind.LowShelf:
                {
                    double s = 2 * Math.Sqrt(A) * alpha;
                    nb0 = A * ((A + 1) - (A - 1) * cw + s); nb1 = 2 * A * ((A - 1) - (A + 1) * cw); nb2 = A * ((A + 1) - (A - 1) * cw - s);
                    na0 = (A + 1) + (A - 1) * cw + s; na1 = -2 * ((A - 1) + (A + 1) * cw); na2 = (A + 1) + (A - 1) * cw - s;
                    break;
                }
                case Kind.HighShelf:
                {
                    double s = 2 * Math.Sqrt(A) * alpha;
                    nb0 = A * ((A + 1) + (A - 1) * cw + s); nb1 = -2 * A * ((A - 1) + (A + 1) * cw); nb2 = A * ((A + 1) + (A - 1) * cw - s);
                    na0 = (A + 1) - (A - 1) * cw + s; na1 = 2 * ((A - 1) - (A + 1) * cw); na2 = (A + 1) - (A - 1) * cw - s;
                    break;
                }
                default: // LowPass
                    nb0 = (1 - cw) / 2; nb1 = 1 - cw; nb2 = (1 - cw) / 2; na0 = 1 + alpha; na1 = -2 * cw; na2 = 1 - alpha; break;
            }
            b0 = (float)(nb0 / na0); b1 = (float)(nb1 / na0); b2 = (float)(nb2 / na0);
            a1 = (float)(na1 / na0); a2 = (float)(na2 / na0);
        }

        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            // flush denormals in long decays
            if (z1 > -1e-25f && z1 < 1e-25f) z1 = 0f;
            if (z2 > -1e-25f && z2 < 1e-25f) z2 = 0f;
            return y;
        }

        public void Run(float[] buf, int start, int count)
        {
            int end = Math.Min(buf.Length, start + count);
            for (int i = Math.Max(0, start); i < end; i++) buf[i] = Process(buf[i]);
        }
    }

    /// <summary>
    /// Textbook modal frequency ratios (f_k / f_1) used for struck objects. Decay per mode follows
    /// τ_k = τ0 · (f_1 / f_k)^α (higher modes die faster; α ≈ 0.5 metal … 1.2 wood).
    /// </summary>
    internal static class Modes
    {
        /// <summary>Free-free Euler-Bernoulli bar (sword blade, metal rod, bone): (2k+1)² π²/4 ratios.</summary>
        public static readonly float[] FreeBar = { 1f, 2.756f, 5.404f, 8.933f, 13.344f };
        /// <summary>Clamped-free bar (blade held at the hilt, latch spring, fletching).</summary>
        public static readonly float[] ClampedBar = { 1f, 6.267f, 17.55f, 34.39f };
        /// <summary>Ideal circular membrane, Bessel zeros j_mn / j_01 (drum hide, leather, flesh slap body).</summary>
        public static readonly float[] Membrane = { 1f, 1.594f, 2.136f, 2.296f, 2.653f, 2.918f, 3.156f, 3.501f };
        /// <summary>Thin circular plate, free edge (shield boss, breastplate; Rossing's measured cluster, approximate).</summary>
        public static readonly float[] Plate = { 1f, 1.73f, 2.33f, 3.91f, 4.11f, 5.32f, 6.17f, 7.43f };
        /// <summary>Tuned church bell partials relative to the prime: hum, prime, minor tierce, quint, nominal, …</summary>
        public static readonly float[] Bell = { 0.5f, 1f, 1.2f, 1.5f, 2f, 2.5f, 2.67f, 3f };
        /// <summary>Glass / ice shell (the old impact_frost ratios).</summary>
        public static readonly float[] Glass = { 1f, 2.41f, 3.93f, 5.6f };
        /// <summary>Bone or xylophone-like bar with an undercut (1 : 3.9 : 9.2).</summary>
        public static readonly float[] Bone = { 1f, 3.9f, 9.2f };
        /// <summary>Wooden plank / board (orthotropic: dense low cluster, few modes).</summary>
        public static readonly float[] Wood = { 1f, 1.58f, 2.57f, 3.32f, 4.9f };
        /// <summary>Stone slab or rock chunk: stiff, inharmonic, heavily damped.</summary>
        public static readonly float[] Stone = { 1f, 1.47f, 2.09f, 2.72f, 3.61f, 4.4f };
    }

    /// <summary>Small Freeverb-style room (4 damped combs + 2 allpasses, mono). Own state per instance.</summary>
    internal sealed class Room
    {
        static readonly int[] CombLens = { 1116, 1188, 1277, 1356 };
        static readonly int[] ApLens = { 556, 441 };
        readonly float[][] comb;
        readonly float[] combStore, combFb;
        readonly int[] combPos;
        readonly float[][] ap;
        readonly int[] apPos;
        readonly float damp;
        readonly float[] pre;
        int prePos;

        /// <param name="rt60">seconds for the tail to fall 60 dB</param>
        /// <param name="size">0.2 (closet) … 1 (hall): scales the delay lengths</param>
        /// <param name="damp">0 bright … 0.7 dark (high frequencies die faster)</param>
        public Room(float rt60, float size, float damp, float predelayMs)
        {
            int sr = SynthBuffer.SampleRate;
            float k = Math.Max(0.15f, Math.Min(1.2f, size));
            comb = new float[CombLens.Length][];
            combStore = new float[CombLens.Length];
            combFb = new float[CombLens.Length];
            combPos = new int[CombLens.Length];
            for (int i = 0; i < CombLens.Length; i++)
            {
                int len = Math.Max(8, (int)(CombLens[i] * k));
                comb[i] = new float[len];
                // feedback for a 60 dB fall in rt60: g = 10^(-3·D / (rt60·sr))
                combFb[i] = (float)Math.Pow(10.0, -3.0 * len / (Math.Max(0.05, rt60) * sr));
            }
            ap = new float[ApLens.Length][];
            apPos = new int[ApLens.Length];
            for (int i = 0; i < ApLens.Length; i++) ap[i] = new float[Math.Max(4, (int)(ApLens[i] * k))];
            this.damp = Math.Max(0f, Math.Min(0.9f, damp));
            pre = new float[Math.Max(1, (int)(predelayMs * 0.001f * sr))];
        }

        public float Process(float x)
        {
            float d = pre[prePos];
            pre[prePos] = x;
            if (++prePos >= pre.Length) prePos = 0;
            float o = 0f;
            for (int i = 0; i < comb.Length; i++)
            {
                var c = comb[i];
                int p = combPos[i];
                float y = c[p];
                combStore[i] = y * (1f - damp) + combStore[i] * damp;
                c[p] = d + combStore[i] * combFb[i];
                if (++p >= c.Length) p = 0;
                combPos[i] = p;
                o += y;
            }
            o *= 0.25f;
            for (int i = 0; i < ap.Length; i++)
            {
                var a = ap[i];
                int p = apPos[i];
                float b = a[p];
                float y = -o + b;
                a[p] = o + b * 0.5f;
                if (++p >= a.Length) p = 0;
                apPos[i] = p;
                o = y;
            }
            return o;
        }
    }

    /// <summary>Grain flavours for <see cref="SynthBuffer.Grains"/>.</summary>
    internal enum GrainKind
    {
        /// <summary>Tiny ringing metal (mail rings, coins, armour rattle): two inharmonic sine modes.</summary>
        Metal,
        /// <summary>Band-passed noise tick (grit, debris, leaf crunch, snow).</summary>
        Noise,
        /// <summary>Hard click: one strongly damped mode (bone, chitin, scale plates, wood splinters).</summary>
        Click,
        /// <summary>Air bubble (Minnaert resonance with rising pitch): wet, mud, splash.</summary>
        Bubble,
    }

    internal sealed partial class SynthBuffer
    {
        /// <summary>A layer that shares this buffer's length and random stream (draw into it, process it, Mix it back).</summary>
        SynthBuffer(int length, System.Random sharedRng)
        {
            Data = new float[Math.Max(1, length)];
            rng = sharedRng;
        }

        public int Length => Data.Length;
        public float Seconds => Data.Length / (float)SampleRate;

        static int Smp(float seconds) => (int)Math.Round(seconds * SampleRate);

        // ------------------------------------------------------------------ randomness (per-buffer, deterministic)

        /// <summary>Uniform in [a, b).</summary>
        public float U(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        /// <summary>x varied by ±pct (0.1 = ±10 %).</summary>
        public float Jit(float x, float pct) => x * (1f + U(-pct, pct));
        /// <summary>Uniform integer in [a, b].</summary>
        public int UInt(int a, int b) => a + (int)Math.Min(b - a, Math.Floor(rng.NextDouble() * (b - a + 1)));
        /// <summary>Signed white noise sample.</summary>
        public float White() => (float)rng.NextDouble() * 2f - 1f;

        // ------------------------------------------------------------------ layers and whole-buffer processing

        public SynthBuffer Layer() => new SynthBuffer(Data.Length, rng);

        public void Mix(SynthBuffer layer, float gain)
        {
            var src = layer.Data;
            int n = Math.Min(src.Length, Data.Length);
            for (int i = 0; i < n; i++) Data[i] += src[i] * gain;
        }

        public void Gain(float g) { for (int i = 0; i < Data.Length; i++) Data[i] *= g; }

        /// <summary>Runs a biquad over the whole buffer (or from start for dur seconds).</summary>
        public void Filter(Biquad.Kind kind, float hz, float q = 0.707f, float dbGain = 0f, float start = 0f, float dur = -1f)
        {
            var f = Biquad.Make(kind, hz, q, dbGain);
            int s0 = Smp(start);
            f.Run(Data, s0, dur < 0f ? Data.Length - s0 : Smp(dur));
        }

        /// <summary>Removes DC and sub-rumble: 2nd-order high-pass, twice (24 dB/oct).</summary>
        public void CutLows(float hz)
        {
            Filter(Biquad.Kind.HighPass, hz, 0.707f);
            Filter(Biquad.Kind.HighPass, hz, 0.707f);
        }

        /// <summary>Soft saturation tanh(x·d)/tanh(d) on the peak-normalised buffer; keeps the original peak.</summary>
        public void Saturate(float drive)
        {
            float peak = 1e-9f;
            for (int i = 0; i < Data.Length; i++) peak = Math.Max(peak, Math.Abs(Data[i]));
            double d = Math.Max(0.01, drive), norm = Math.Tanh(d);
            for (int i = 0; i < Data.Length; i++) Data[i] = (float)(Math.Tanh(Data[i] / peak * d) / norm) * peak;
        }

        /// <summary>Adds a baked room tail: wet = reverb level relative to the dry peak scale.</summary>
        public void Reverb(float rt60, float wet, float size = 0.45f, float damp = 0.35f, float predelayMs = 6f, float hpHz = 180f, float lpHz = 7000f)
        {
            var room = new Room(rt60, size, damp, predelayMs);
            var hp = Biquad.Make(Biquad.Kind.HighPass, hpHz, 0.707f);
            var lp = Biquad.Make(Biquad.Kind.LowPass, lpHz, 0.707f);
            for (int i = 0; i < Data.Length; i++)
            {
                float x = lp.Process(hp.Process(Data[i]));
                Data[i] += room.Process(x) * wet;
            }
        }

        /// <summary>Linear fade-in over the first seconds (removes onset clicks of non-zero starts).</summary>
        public void FadeIn(float seconds)
        {
            int n = Math.Min(Data.Length, Math.Max(1, Smp(seconds)));
            for (int i = 0; i < n; i++) Data[i] *= i / (float)n;
        }

        /// <summary>
        /// Normalises to the peak, trims the inaudible tail (below floorDb of the peak) and returns the samples. The
        /// end gets a raised-cosine fade: 8 ms after a trimmed (already silent) tail, 40 ms when the tail still sounds
        /// at the end of the buffer, so nothing clicks. Used by the layered recipes (the original ones keep Finish).
        /// </summary>
        public float[] FinishTrim(float peak, float floorDb = -62f)
        {
            float max = 1e-9f;
            for (int i = 0; i < Data.Length; i++) { float a = Math.Abs(Data[i]); if (a > max) max = a; }
            float k = peak / max;
            float floor = max * (float)Math.Pow(10.0, floorDb / 20.0);
            int last = Data.Length - 1;
            while (last > 0 && Math.Abs(Data[last]) < floor) last--;
            int pad = Smp(0.015f);                       // keep 15 ms of near-silence after the last audible sample
            bool trimmed = last + 1 + pad < Data.Length;
            int len = Math.Min(Data.Length, last + 1 + pad);
            len = Math.Max(len, Math.Min(Data.Length, Smp(0.02f)));
            var o = new float[len];
            int fade = Math.Min(len, Smp(trimmed ? 0.008f : 0.04f));
            for (int i = 0; i < len; i++)
            {
                float v = Data[i] * k;
                int fromEnd = len - 1 - i;
                if (fromEnd < fade) v *= 0.5f - 0.5f * (float)Math.Cos(Math.PI * fromEnd / fade);
                o[i] = v;
            }
            return o;
        }

        // ------------------------------------------------------------------ envelopes

        /// <summary>Attack (linear) then exponential decay with time constant tau.</summary>
        public static float EnvAD(float t, float attack, float tau)
            => t < attack ? t / Math.Max(1e-5f, attack) : (float)Math.Exp(-(t - attack) / Math.Max(1e-4f, tau));

        /// <summary>Asymmetric bell 0→1→0 over u ∈ [0,1] with the peak at p (smooth, sin² flanks).</summary>
        public static float EnvBell(float u, float p)
        {
            if (u <= 0f || u >= 1f) return 0f;
            p = Math.Max(0.02f, Math.Min(0.98f, p));
            float x = u < p ? u / p * 0.5f : 0.5f + (u - p) / (1f - p) * 0.5f;
            float s = (float)Math.Sin(Math.PI * x);
            return s * s;
        }

        /// <summary>Equal-power crossfade gains (a, b) for x ∈ [0,1].</summary>
        public static void EqualPower(float x, out float a, out float b)
        {
            x = Math.Max(0f, Math.Min(1f, x));
            a = (float)Math.Cos(x * Math.PI * 0.5);
            b = (float)Math.Sin(x * Math.PI * 0.5);
        }

        // ------------------------------------------------------------------ transients and noise

        /// <summary>
        /// Contact transient: a high-passed noise burst of a few milliseconds with a near-instant attack (the "tick"
        /// the ear uses to localise and judge hardness).
        /// </summary>
        public void Click(float start, float durMs, float hpHz, float amp, float lpHz = 16000f)
        {
            int s0 = Smp(start), n = Math.Max(4, Smp(durMs * 0.001f));
            var hp = Biquad.Make(Biquad.Kind.HighPass, hpHz, 0.6f);
            var lp = Biquad.Make(Biquad.Kind.LowPass, lpHz, 0.6f);
            float tau = n / 3f;
            for (int i = 0; i < n * 2 && s0 + i < Data.Length; i++)
            {
                if (s0 + i < 0) continue;
                float env = (i < 3 ? i / 3f : 1f) * (float)Math.Exp(-i / tau);
                Data[s0 + i] += lp.Process(hp.Process(White() * env)) * amp;
            }
        }

        /// <summary>
        /// Band-passed noise with the centre swept fc0→fc1 (exponentially), attack + exponential decay
        /// (or a bell shape when tau ≤ 0, peaking at <paramref name="attack"/> as a fraction of dur).
        /// </summary>
        public void Band(float start, float dur, float fc0, float fc1, float q, float amp, float attack, float tau)
        {
            int s0 = Smp(start), n = Smp(dur);
            if (n <= 0) return;
            var bp = Biquad.Make(Biquad.Kind.BandPass, fc0, q);
            float ratio = fc1 / Math.Max(1f, fc0);
            int rel = Math.Min(n, Smp(0.006f));
            float gain = (float)Math.Sqrt(Math.Max(0.3f, q)) * 1.6f; // keeps narrow bands audible
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                if ((i & 7) == 0) bp.Set(Biquad.Kind.BandPass, fc0 * (float)Math.Pow(ratio, i / (float)n), q);
                float t = i / (float)SampleRate;
                float env = tau > 0f ? EnvAD(t, attack, tau) : EnvBell(i / (float)n, attack);
                if (i > n - rel) env *= (n - i) / (float)rel;
                if (s0 + i >= 0) Data[s0 + i] += bp.Process(White()) * env * amp * gain;
            }
        }

        // ------------------------------------------------------------------ modal synthesis

        /// <summary>
        /// One resonant mode (2-pole resonator, impulse response r^n·sin(ωn)) driven by a raised-cosine contact pulse
        /// of <paramref name="contactMs"/> with unit area: a short pulse excites the high modes (hard contact), a long one
        /// only the low modes (soft contact) — the physical reason a mallet sounds duller than a hammer.
        /// </summary>
        public void Mode(float start, float freq, float tau, float amp, float contactMs = 0.3f)
        {
            if (freq <= 20f || freq >= SampleRate * 0.45f || amp == 0f) return;
            int s0 = Smp(start);
            int lc = Math.Max(1, Smp(contactMs * 0.001f));
            double w = 2.0 * Math.PI * freq / SampleRate;
            double r = Math.Exp(-1.0 / (Math.Max(0.0005, tau) * SampleRate));
            float c = (float)(2.0 * r * Math.Cos(w)), r2 = (float)(r * r);
            float g = (float)Math.Sin(w) * amp;
            float y1 = 0f, y2 = 0f;
            int n = lc + (int)(tau * 7f * SampleRate) + 2;
            float area = 0f;
            for (int i = 0; i < lc; i++) area += 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * (i + 0.5) / lc);
            float inv = 1f / Math.Max(1e-6f, area);
            for (int i = 0; i < n; i++)
            {
                int k = s0 + i;
                if (k >= Data.Length) break;
                float x = i < lc ? (0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * (i + 0.5) / lc)) * inv : 0f;
                float y = g * x + c * y1 - r2 * y2;
                y2 = y1; y1 = y;
                if (k >= 0) Data[k] += y;
            }
        }

        /// <summary>
        /// Struck object: a bank of modes at f0·ratios[k] with decays τ0·(1/ratio)^α, strike-position weighting
        /// |sin(π(k+1)x)| (so each variant, struck at a different point, has its own timbre) and per-mode detune.
        /// </summary>
        public void Strike(float start, float f0, float[] ratios, float tau0, float alpha, float amp, float contactMs,
                           float strikePos = 0.3f, float detune = 0.01f, float[] amps = null, float rolloff = 0.75f)
        {
            for (int k = 0; k < ratios.Length; k++)
            {
                float ratio = ratios[k] * (1f + U(-detune, detune));
                float f = f0 * ratio;
                float tau = tau0 * (float)Math.Pow(1.0 / Math.Max(0.1f, ratios[k]), alpha);
                float w = amps != null ? amps[Math.Min(k, amps.Length - 1)] : (float)Math.Pow(rolloff, k);
                float pos = 0.2f + 0.8f * Math.Abs((float)Math.Sin(Math.PI * (k + 1) * strikePos));
                Mode(start, f, tau, amp * w * pos, contactMs);
            }
        }

        // ------------------------------------------------------------------ grains

        /// <summary>
        /// Scatters <paramref name="count"/> micro-impacts over [start, start+span] with exponentially falling density
        /// (time constant densityTau; ≤ 0 = uniform) and falling amplitude: mail jingles, debris, leaf crunch, bubbles.
        /// </summary>
        public void Grains(float start, float span, int count, GrainKind kind, float fLo, float fHi, float tauLo, float tauHi,
                           float amp, float densityTau = 0f, float ampFall = 0.6f)
        {
            for (int g = 0; g < count; g++)
            {
                float u;
                if (densityTau > 0f)
                {
                    // inverse CDF of a truncated exponential on [0, span]
                    double m = 1.0 - Math.Exp(-span / densityTau);
                    u = (float)(-densityTau * Math.Log(1.0 - rng.NextDouble() * m));
                }
                else u = U(0f, span);
                float t0 = start + u;
                float a = amp * U(0.35f, 1f) * (1f - ampFall * (u / Math.Max(1e-4f, span)));
                float f = fLo * (float)Math.Pow(fHi / Math.Max(1f, fLo), rng.NextDouble());
                float tau = U(tauLo, tauHi);
                switch (kind)
                {
                    case GrainKind.Metal:
                        GrainSine(t0, f, tau, a);
                        GrainSine(t0, f * U(1.45f, 2.3f), tau * 0.6f, a * 0.45f);
                        break;
                    case GrainKind.Click:
                        Mode(t0, f, tau, a * 1.6f, 0.08f);
                        break;
                    case GrainKind.Bubble:
                        Bubble(t0, f, tau, U(0.08f, 0.25f), a);
                        break;
                    default:
                        GrainNoise(t0, f, tau, a);
                        break;
                }
            }
        }

        void GrainSine(float t0, float f, float tau, float a)
        {
            if (f >= SampleRate * 0.45f) return;
            int s0 = Smp(t0), n = (int)(tau * 6f * SampleRate);
            double w = 2.0 * Math.PI * f / SampleRate, ph = rng.NextDouble() * Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                int k = s0 + i;
                if (k >= Data.Length) break;
                if (k < 0) continue;
                float env = (i < 8 ? i / 8f : 1f) * (float)Math.Exp(-i / (tau * SampleRate));
                Data[k] += (float)Math.Sin(ph + w * i) * env * a;
            }
        }

        void GrainNoise(float t0, float f, float tau, float a)
        {
            int s0 = Smp(t0), n = (int)(tau * 5f * SampleRate) + 4;
            var bp = Biquad.Make(Biquad.Kind.BandPass, f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                int k = s0 + i;
                if (k >= Data.Length) break;
                float env = (i < 4 ? i / 4f : 1f) * (float)Math.Exp(-i / (tau * SampleRate));
                float y = bp.Process(White()) * env * a * 2.4f;
                if (k >= 0) Data[k] += y;
            }
        }

        /// <summary>
        /// Air bubble: a decaying sinusoid at the Minnaert frequency whose pitch rises as the bubble nears the surface
        /// (van den Doel's model): f(t) = f0·(1 + rise·t/τ).
        /// </summary>
        public void Bubble(float start, float f0, float tau, float rise, float amp)
        {
            int s0 = Smp(start), n = (int)(tau * 6f * SampleRate);
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                int k = s0 + i;
                if (k >= Data.Length) break;
                float t = i / (float)SampleRate;
                float f = f0 * (1f + rise * t / Math.Max(1e-4f, tau));
                if (f >= SampleRate * 0.45f) break;
                ph += 2.0 * Math.PI * f / SampleRate;
                float env = (i < 6 ? i / 6f : 1f) * (float)Math.Exp(-t / tau);
                if (k >= 0) Data[k] += (float)Math.Sin(ph) * env * amp;
            }
        }

        // ------------------------------------------------------------------ air: swings, flights, flutter

        /// <summary>
        /// Swing whoosh from vortex shedding: the band centre follows the blade speed (Strouhal: f ∝ v) and the level
        /// follows v^velPow (aeroacoustic dipoles radiate ∝ v^6; a gentler power keeps it cosy). The speed profile is a
        /// bell peaking at <paramref name="peakAt"/> (fraction of dur). Optional flutter (fletching, cloth flap).
        /// </summary>
        public void Whoosh(float start, float dur, float fLo, float fHi, float q, float amp, float peakAt = 0.55f,
                           float velPow = 2.5f, float flutterHz = 0f, float flutterDepth = 0f)
        {
            int s0 = Smp(start), n = Smp(dur);
            var bp = Biquad.Make(Biquad.Kind.BandPass, fLo, q);
            var bp2 = Biquad.Make(Biquad.Kind.BandPass, fLo * 1.9f, q * 1.4f);
            double fl = 0;
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float u = i / (float)n;
                float v = EnvBell(u, peakAt);
                if ((i & 7) == 0)
                {
                    float fc = fLo + (fHi - fLo) * v;
                    bp.Set(Biquad.Kind.BandPass, fc, q);
                    bp2.Set(Biquad.Kind.BandPass, fc * 1.9f, q * 1.4f);
                }
                float x = White();
                float y = bp.Process(x) + 0.35f * bp2.Process(x);
                float env = (float)Math.Pow(v, velPow / 2f);
                if (flutterHz > 0f)
                {
                    fl += 2.0 * Math.PI * flutterHz / SampleRate;
                    env *= 1f - flutterDepth * 0.5f * (1f + (float)Math.Sin(fl));
                }
                if (s0 + i >= 0) Data[s0 + i] += y * env * amp * 2.2f;
            }
        }

        /// <summary>
        /// A noisy source flying past the listener (arrow, bolt, thrown axe): Doppler factor c/(c + v_r) on the band
        /// centre and 1/r spherical spreading, closest approach d metres at tPass seconds, speed v m/s.
        /// </summary>
        public void Doppler(float start, float dur, float fSrc, float q, float speed, float closest, float tPass, float amp,
                            float flutterHz = 0f, float flutterDepth = 0f)
        {
            const float c = 343f;
            int s0 = Smp(start), n = Smp(dur);
            var bp = Biquad.Make(Biquad.Kind.BandPass, fSrc, q);
            double fl = 0;
            int rel = Math.Min(n, Smp(0.02f));
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float x = speed * (t - tPass);                  // position along the path, 0 at closest approach
                float r = (float)Math.Sqrt(x * x + closest * closest);
                float vr = speed * x / r;                       // radial velocity (+ = receding)
                float f = fSrc * c / (c + vr);
                if ((i & 7) == 0) bp.Set(Biquad.Kind.BandPass, f, q);
                float env = closest / r;
                if (i < Smp(0.01f)) env *= i / (float)Smp(0.01f);
                if (i > n - rel) env *= (n - i) / (float)rel;
                if (flutterHz > 0f)
                {
                    fl += 2.0 * Math.PI * flutterHz / SampleRate;
                    env *= 1f - flutterDepth * 0.5f * (1f + (float)Math.Sin(fl));
                }
                if (s0 + i >= 0) Data[s0 + i] += bp.Process(White()) * env * amp * 2.2f;
            }
        }

        // ------------------------------------------------------------------ voiced sources

        /// <summary>Band-limited sawtooth sample (PolyBLEP) for phase ph ∈ [0,1) and increment dt.</summary>
        static float SawBlep(double ph, double dt)
        {
            float y = (float)(2.0 * ph - 1.0);
            if (ph < dt) { double t = ph / dt; y -= (float)(t + t - t * t - 1.0); }
            else if (ph > 1.0 - dt) { double t = (ph - 1.0) / dt; y -= (float)(t * t + t + t + 1.0); }
            return y;
        }

        /// <summary>
        /// Source-filter voice (creature vocals, choir, growls): a band-limited saw with pitch glide f0→f1 (shape
        /// <paramref name="curve"/>: 1 = exponential sweep, &lt;1 = falls early), random-walk jitter, optional vibrato and
        /// roughness (subharmonic AM), plus breath noise, through parallel band-pass formants that can morph to
        /// <paramref name="formantsEnd"/>.
        /// </summary>
        public void Voice(float start, float dur, float f0, float f1, float[] formants, float[] gains, float amp,
                          float attack, float release, float breath = 0.1f, float jitter = 0.01f, float rough = 0f,
                          float vibHz = 0f, float vibDepth = 0f, float[] formantsEnd = null, float curve = 1f, float formantQ = 6f)
        {
            int s0 = Smp(start), n = Smp(dur);
            if (n <= 0) return;
            int nf = formants.Length;
            var bp = new Biquad[nf];
            for (int k = 0; k < nf; k++) bp[k] = Biquad.Make(Biquad.Kind.BandPass, formants[k], formantQ);
            double ph = 0, sub = 0;
            float walk = 0f;
            int rel = Math.Max(1, Smp(release));
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float u = i / (float)n, t = i / (float)SampleRate;
                if ((i & 31) == 0)
                {
                    walk = walk * 0.9f + White() * jitter;
                    if (formantsEnd != null)
                        for (int k = 0; k < nf; k++)
                        {
                            float fk = formants[k] + (formantsEnd[Math.Min(k, formantsEnd.Length - 1)] - formants[k]) * u;
                            bp[k].Set(Biquad.Kind.BandPass, fk, formantQ);
                        }
                }
                float f = f0 * (float)Math.Pow(f1 / Math.Max(1f, f0), Math.Pow(u, curve));
                if (vibHz > 0f) f *= 1f + vibDepth * (float)Math.Sin(2.0 * Math.PI * vibHz * t) * Math.Min(1f, t / 0.15f);
                f *= 1f + walk;
                double dt = Math.Min(0.45, f / SampleRate);
                ph += dt; if (ph >= 1.0) ph -= 1.0;
                sub += dt * 0.5; if (sub >= 1.0) sub -= 1.0;
                float src = SawBlep(ph, dt);
                if (rough > 0f) src *= 1f - rough * 0.5f * (1f + (float)Math.Sin(2.0 * Math.PI * sub));
                src += White() * breath;
                float y = 0f;
                for (int k = 0; k < nf; k++) y += bp[k].Process(src) * gains[Math.Min(k, gains.Length - 1)];
                float env = t < attack ? t / Math.Max(1e-4f, attack) : 1f;
                if (i > n - rel) env *= (n - i) / (float)rel;
                if (s0 + i >= 0) Data[s0 + i] += y * env * amp * 2.5f;
            }
        }

        /// <summary>
        /// Brass horn: band-limited saw with a pitch scoop up from -<paramref name="scoopSemis"/>, vibrato after the
        /// attack, and a low-pass whose cutoff follows the envelope (brass gets brighter as it gets louder), then lip /
        /// bell resonances as peaking EQs at 500 Hz and 1.5 kHz.
        /// </summary>
        public void Horn(float start, float dur, float freq, float amp, float attack, float release, float scoopSemis = 3f,
                         float vibHz = 5.2f, float vibDepth = 0.008f, float bright = 1f, float endFreq = -1f)
        {
            int s0 = Smp(start), n = Smp(dur);
            var lp = Biquad.Make(Biquad.Kind.LowPass, 800f, 0.8f);
            var p1 = Biquad.Make(Biquad.Kind.Peak, 500f, 1.4f, 6f);
            var p2 = Biquad.Make(Biquad.Kind.Peak, 1500f, 1.8f, 5f);
            double ph = 0, ph2 = 0;
            if (endFreq <= 0f) endFreq = freq;
            int rel = Math.Max(1, Smp(release));
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float t = i / (float)SampleRate, u = i / (float)n;
                float env = t < attack ? (float)Math.Pow(t / attack, 1.6) : 1f;
                if (i > n - rel) env *= (float)Math.Pow((n - i) / (float)rel, 1.5);
                float scoop = (float)Math.Pow(2.0, -scoopSemis / 12.0 * Math.Exp(-t / 0.05));
                float vib = 1f + vibDepth * (float)Math.Sin(2.0 * Math.PI * vibHz * t) * Math.Min(1f, Math.Max(0f, (t - 0.15f) / 0.2f));
                float f = (freq + (endFreq - freq) * u) * scoop * vib;
                double dt = f / SampleRate;
                ph += dt; if (ph >= 1.0) ph -= 1.0;
                ph2 += dt * 1.004; if (ph2 >= 1.0) ph2 -= 1.0;
                float src = (SawBlep(ph, dt) + 0.6f * SawBlep(ph2, dt)) * 0.6f + White() * 0.04f * env;
                if ((i & 15) == 0) lp.Set(Biquad.Kind.LowPass, 250f + 3200f * bright * (float)Math.Pow(env, 1.5), 0.9f);
                float y = p2.Process(p1.Process(lp.Process(src)));
                if (s0 + i >= 0) Data[s0 + i] += y * env * amp;
            }
        }

        /// <summary>FM tone (carrier fc, modulator fc·ratio, index swept i0→i1), attack + exponential decay.</summary>
        public void Fm(float start, float dur, float fc, float ratio, float i0, float i1, float amp, float attack, float tau, float fcEnd = -1f)
        {
            int s0 = Smp(start), n = Smp(dur);
            if (fcEnd <= 0f) fcEnd = fc;
            double pc = 0, pm = 0;
            int rel = Math.Min(n, Smp(0.01f));
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float t = i / (float)SampleRate, u = i / (float)n;
                float f = fc * (float)Math.Pow(fcEnd / fc, u);
                pc += 2.0 * Math.PI * f / SampleRate;
                pm += 2.0 * Math.PI * f * ratio / SampleRate;
                float idx = i0 + (i1 - i0) * u;
                float env = EnvAD(t, attack, tau);
                if (i > n - rel) env *= (n - i) / (float)rel;
                if (s0 + i >= 0) Data[s0 + i] += (float)Math.Sin(pc + idx * Math.Sin(pm)) * env * amp;
            }
        }

        /// <summary>
        /// Stick-slip friction (creaking wood, grinding stone, a drawn bowstring): an irregular impulse train whose rate
        /// glides rate0→rate1 drives a bank of body modes. Each slip is one impulse; the body rings between them.
        /// </summary>
        public void StickSlip(float start, float dur, float rate0, float rate1, float irregular, float f0, float[] ratios, float tau, float amp)
        {
            int s0 = Smp(start), n = Smp(dur);
            if (n <= 0) return;
            var exc = new float[n];
            float next = 0f;
            while (true)
            {
                int i = (int)(next * SampleRate);
                if (i >= n) break;
                float u = i / (float)n;
                exc[i] += (0.6f + 0.4f * U(0f, 1f)) * EnvBell(u, 0.35f);
                float rate = rate0 + (rate1 - rate0) * u;
                next += (1f / Math.Max(1f, rate)) * (1f + U(-irregular, irregular));
            }
            for (int k = 0; k < ratios.Length; k++)
            {
                float f = f0 * ratios[k] * (1f + U(-0.01f, 0.01f));
                if (f >= SampleRate * 0.45f) continue;
                double w = 2.0 * Math.PI * f / SampleRate;
                double r = Math.Exp(-1.0 / (Math.Max(0.0005, tau / (1f + k * 0.6f)) * SampleRate));
                float c = (float)(2.0 * r * Math.Cos(w)), r2 = (float)(r * r), g = (float)Math.Sin(w) * amp * (float)Math.Pow(0.7, k);
                float y1 = 0f, y2 = 0f;
                for (int i = 0; i < n + (int)(tau * 5 * SampleRate); i++)
                {
                    int idx = s0 + i;
                    if (idx >= Data.Length) break;
                    float y = g * (i < n ? exc[i] : 0f) + c * y1 - r2 * y2;
                    y2 = y1; y1 = y;
                    if (idx >= 0) Data[idx] += y;
                }
            }
        }

        /// <summary>
        /// Sustained sine partials under a bell envelope that peaks at <paramref name="peakAt"/> (a late peak = a
        /// reversed swell): magic shimmer, spirit fades, chord blooms. Optional tremolo; each partial starts at a
        /// random phase and drifts by <paramref name="drift"/> (relative) over the duration.
        /// </summary>
        public void Swell(float start, float dur, float[] freqs, float amp, float peakAt, float tremHz = 0f, float drift = 0f)
        {
            int s0 = Smp(start), n = Smp(dur);
            for (int k = 0; k < freqs.Length; k++)
            {
                float f0 = freqs[k];
                if (f0 <= 20f || f0 >= SampleRate * 0.45f) continue;
                double ph = rng.NextDouble() * Math.PI * 2.0, tph = rng.NextDouble() * Math.PI * 2.0;
                float a = amp / (float)Math.Sqrt(freqs.Length) * (k == 0 ? 1f : 0.8f);
                for (int i = 0; i < n && s0 + i < Data.Length; i++)
                {
                    float u = i / (float)n;
                    ph += 2.0 * Math.PI * f0 * (1f + drift * u) / SampleRate;
                    float env = EnvBell(u, peakAt);
                    if (tremHz > 0f) { tph += 2.0 * Math.PI * tremHz / SampleRate; env *= 0.75f + 0.25f * (float)Math.Sin(tph); }
                    if (s0 + i >= 0) Data[s0 + i] += (float)Math.Sin(ph) * env * a;
                }
            }
        }

        /// <summary>Pitch-dropping sine thump (the low "weight" of a blow), with exponential decay.</summary>
        public void Thump(float start, float f0, float f1, float tau, float amp, float dur = -1f)
        {
            if (dur < 0f) dur = tau * 6f;
            int s0 = Smp(start), n = Smp(dur);
            double ph = 0;
            for (int i = 0; i < n && s0 + i < Data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = f1 + (f0 - f1) * (float)Math.Exp(-t / (tau * 0.6f));
                ph += 2.0 * Math.PI * f / SampleRate;
                float env = (i < 24 ? i / 24f : 1f) * (float)Math.Exp(-t / tau);
                if (s0 + i >= 0) Data[s0 + i] += (float)Math.Sin(ph) * env * amp;
            }
        }
    }
}
