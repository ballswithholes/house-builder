// Generative ambient score, synthesized on the audio thread (OnAudioFilterRead).
//
// A Composer walks a mood's chord progression in 8th-note steps and triggers voices: warm pads
// (wavetable, detuned pair, low-passed), Karplus-Strong harp plucks, music-box bells, a soft
// sine bass, an airy flute (forest) and, for combat, a gentle kick + shaker pulse. Melodies are
// pentatonic random walks organised in 2-bar phrases (A A' B rest) so they feel composed.
// A shared ping-pong delay and a small Schroeder reverb glue everything together.
//
// Threading: Composers are built on the main thread and handed over atomically; the audio thread
// owns voices/effects. No allocations and no Unity API calls happen on the audio thread.
using System;
using System.Threading;

namespace Lanternvale.Game
{
    internal sealed class MoodSpec
    {
        public string name = "";
        public float bpm = 72f;
        public int stepsPerBar = 8;          // 8th notes per bar (6 = 3/4 waltz)
        public int root = 53;                // midi of the tonic
        public int[] mode = { 0, 2, 4, 5, 7, 9, 11 };
        public int[] progression = { 0, 4, 5, 3 };
        public int barsPerChord = 2;
        public int[] melodyDegrees = { 0, 1, 2, 4, 5 };
        public int melodyBase = 14;          // scale degrees above the root where melodies live
        public float melodyDensity = 0.4f;
        public bool fluteMelody;
        public float pad = 0.08f, pluck = 0.2f, bell = 0.15f, bass = 0.18f, flute = 0.12f, kick, shaker;
        public int[] arp = { -1, -1, -1, -1, -1, -1, -1, -1 };   // chord tone index per step (0 root,1 3rd,2 5th,3 8ve,4 9th)
        public float arpProb = 1f;
        public int[] bassSteps = { 0 };
        public int[] kickSteps = new int[0];
        public bool add9;
        public float reverb = 0.3f, delay = 0.15f;
    }

    internal static class MoodLibrary
    {
        /// <summary>
        /// Normalises a mood or map music id ("music_whisperwood", "forest", "boss_battle"…) to one of
        /// village, forest, shrine, combat, menu.
        /// </summary>
        public static string Normalize(string mood)
        {
            var m = (mood ?? "").ToLowerInvariant();
            if (m.Contains("combat") || m.Contains("battle") || m.Contains("boss") || m.Contains("fight")) return "combat";
            if (m.Contains("menu") || m.Contains("title")) return "menu";
            if (m.Contains("shrine") || m.Contains("dungeon") || m.Contains("crypt") || m.Contains("sad") || m.Contains("hollow")) return "shrine";
            if (m.Contains("forest") || m.Contains("wood") || m.Contains("wild") || m.Contains("glade")) return "forest";
            return "village";
        }

        public static MoodSpec Get(string mood)
        {
            switch (Normalize(mood))
            {
                case "forest": case "whisperwood": case "wilds":
                    return new MoodSpec
                    {
                        name = "forest", bpm = 66f, stepsPerBar = 8, root = 50, mode = new[] { 0, 2, 3, 5, 7, 9, 10 },
                        progression = new[] { 0, 3, 0, 6 }, barsPerChord = 2, melodyDegrees = new[] { 0, 1, 3, 4, 6 }, melodyBase = 14,
                        melodyDensity = 0.3f, fluteMelody = true, pad = 0.085f, pluck = 0.16f, bell = 0.09f, bass = 0.14f, flute = 0.13f,
                        arp = new[] { -1, -1, 2, -1, 3, -1, 4, -1 }, arpProb = 0.4f, bassSteps = new[] { 0 }, add9 = true, reverb = 0.42f, delay = 0.22f,
                    };
                case "shrine": case "dungeon": case "sad":
                    return new MoodSpec
                    {
                        name = "shrine", bpm = 56f, stepsPerBar = 8, root = 45, mode = new[] { 0, 2, 3, 5, 7, 8, 10 },
                        progression = new[] { 0, 5, 2, 6 }, barsPerChord = 2, melodyDegrees = new[] { 0, 2, 3, 4, 6 }, melodyBase = 14,
                        melodyDensity = 0.22f, pad = 0.1f, pluck = 0.13f, bell = 0.14f, bass = 0.16f,
                        arp = new[] { 0, -1, -1, 2, -1, -1, 1, -1 }, arpProb = 0.55f, bassSteps = new[] { 0 }, reverb = 0.52f, delay = 0.28f,
                    };
                case "combat": case "battle":
                    return new MoodSpec
                    {
                        name = "combat", bpm = 108f, stepsPerBar = 8, root = 50, mode = new[] { 0, 2, 3, 5, 7, 8, 10 },
                        progression = new[] { 0, 5, 6, 0 }, barsPerChord = 1, melodyDegrees = new[] { 0, 2, 3, 4, 6 }, melodyBase = 14,
                        melodyDensity = 0.26f, pad = 0.055f, pluck = 0.19f, bell = 0.1f, bass = 0.2f, kick = 0.32f, shaker = 0.045f,
                        arp = new[] { 0, 2, 3, 2, 0, 2, 4, 2 }, arpProb = 1f, bassSteps = new[] { 0, 3, 4, 6 }, kickSteps = new[] { 0, 4 },
                        reverb = 0.22f, delay = 0.1f,
                    };
                case "menu": case "title":
                    return new MoodSpec
                    {
                        name = "menu", bpm = 60f, stepsPerBar = 8, root = 48, mode = new[] { 0, 2, 4, 6, 7, 9, 11 },
                        progression = new[] { 0, 1, 5, 4 }, barsPerChord = 2, melodyDegrees = new[] { 0, 1, 2, 4, 5 }, melodyBase = 14,
                        melodyDensity = 0.36f, pad = 0.1f, pluck = 0.17f, bell = 0.15f, bass = 0.15f,
                        arp = new[] { 0, 1, 2, 3, 4, 3, 2, 1 }, arpProb = 0.55f, bassSteps = new[] { 0 }, add9 = true, reverb = 0.46f, delay = 0.25f,
                    };
                default: // village: warm major waltz
                    return new MoodSpec
                    {
                        name = "village", bpm = 92f, stepsPerBar = 6, root = 53, mode = new[] { 0, 2, 4, 5, 7, 9, 11 },
                        progression = new[] { 0, 4, 5, 3 }, barsPerChord = 2, melodyDegrees = new[] { 0, 1, 2, 4, 5 }, melodyBase = 14,
                        melodyDensity = 0.45f, pad = 0.065f, pluck = 0.21f, bell = 0.16f, bass = 0.19f,
                        arp = new[] { -1, -1, 2, -1, 3, -1 }, arpProb = 0.95f, bassSteps = new[] { 0 }, add9 = true, reverb = 0.3f, delay = 0.14f,
                    };
            }
        }
    }

    internal sealed class Composer
    {
        public readonly MoodSpec spec;
        public float gain, gainPrev, targetGain, gainStep;
        public int samplesToStep;
        public readonly int samplesPerStep;
        int step, bar, phraseIndex;
        uint rng;
        readonly int[] phrase, previousPhrase;
        readonly int phraseLen;
        const int Rest = int.MinValue;
        int lastMelody = Rest;
        public bool Released;   // fading out for good

        public Composer(MoodSpec spec, int sampleRate, float fadeSeconds, uint seed)
        {
            this.spec = spec;
            samplesPerStep = Math.Max(1, (int)(sampleRate * 60f / spec.bpm / 2f));
            samplesToStep = 1;
            phraseLen = spec.stepsPerBar * 2;
            phrase = new int[phraseLen];
            previousPhrase = new int[phraseLen];
            for (int i = 0; i < phraseLen; i++) phrase[i] = previousPhrase[i] = Rest;
            rng = seed | 1u;
            targetGain = 1f;
            gain = 0f;
            gainStep = 1f / Math.Max(1f, fadeSeconds * sampleRate);
        }

        public void FadeOut(float seconds, int sampleRate)
        {
            targetGain = 0f;
            Released = true;
            gainStep = Math.Max(gainStep, 1f / Math.Max(1f, seconds * sampleRate));
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
        int RandInt(int n) => Math.Min(n - 1, (int)(Rand() * n));

        public int Midi(int degree)
        {
            int oct = degree >= 0 ? degree / 7 : -((-degree + 6) / 7);
            int idx = degree - oct * 7;
            return spec.root + oct * 12 + spec.mode[idx];
        }

        int ChordAt(int barIndex)
        {
            int c = (barIndex / Math.Max(1, spec.barsPerChord)) % spec.progression.Length;
            return spec.progression[c];
        }

        static int Wrap(int midi, int lo, int hi)
        {
            while (midi > hi) midi -= 12;
            while (midi < lo) midi += 12;
            return midi;
        }

        public void Step(MusicEngine e)
        {
            int chord = ChordAt(bar);
            int barInPhrase = bar % 2;

            if (step == 0)
            {
                if (bar % Math.Max(1, spec.barsPerChord) == 0)
                {
                    e.ReleasePads(this);
                    int holdSteps = spec.barsPerChord * spec.stepsPerBar;
                    int lo = spec.root + 9, hi = spec.root + 24;
                    e.Pad(this, Wrap(Midi(chord), lo, hi), holdSteps, -0.35f);
                    e.Pad(this, Wrap(Midi(chord + 2), lo, hi), holdSteps, 0.3f);
                    e.Pad(this, Wrap(Midi(chord + 4), lo, hi), holdSteps, -0.1f);
                    if (spec.add9) e.Pad(this, Wrap(Midi(chord + 8), lo + 7, hi + 7), holdSteps, 0.45f, 0.6f);
                }
                if (barInPhrase == 0) NewPhrase();
            }

            // melody
            int m = phrase[barInPhrase * spec.stepsPerBar + step];
            if (m != Rest)
            {
                int midi = Midi(m);
                if (spec.fluteMelody)
                {
                    int hold = 1;
                    int idx = barInPhrase * spec.stepsPerBar + step;
                    while (idx + hold < phraseLen && phrase[idx + hold] == Rest && hold < 4) hold++;
                    e.Flute(this, midi, hold);
                    if (Rand() < 0.25f) e.Bell(this, midi + 12, spec.bell * 0.5f);
                }
                else e.Bell(this, midi, spec.bell * (0.8f + 0.3f * Rand()));
            }

            // arpeggio / harp
            int a = spec.arp[step % spec.arp.Length];
            if (a >= 0 && Rand() < spec.arpProb)
            {
                int deg = chord + (a == 1 ? 2 : a == 2 ? 4 : a == 3 ? 7 : a == 4 ? 8 : 0);
                int midi = Wrap(Midi(deg), spec.root + 12, spec.root + 27);
                e.Pluck(this, midi, spec.pluck * (0.75f + 0.35f * Rand()));
            }

            // bass
            for (int i = 0; i < spec.bassSteps.Length; i++)
                if (spec.bassSteps[i] == step)
                {
                    int deg = chord;
                    if (spec.name == "combat" && step != 0 && Rand() < 0.4f) deg += 4;
                    e.Bass(this, Wrap(Midi(deg), spec.root - 12, spec.root - 1), spec.bass);
                }

            // pulse
            for (int i = 0; i < spec.kickSteps.Length; i++)
                if (spec.kickSteps[i] == step) e.Kick(this, spec.kick);
            if (spec.kick > 0f && step == 6 && Rand() < 0.3f) e.Kick(this, spec.kick * 0.6f);
            if (spec.shaker > 0f && (step & 1) == 1) e.Shaker(this, spec.shaker * (0.7f + 0.3f * Rand()));

            step++;
            if (step >= spec.stepsPerBar) { step = 0; bar++; }
        }

        void NewPhrase()
        {
            int kind = phraseIndex % 4;
            phraseIndex++;
            Array.Copy(phrase, previousPhrase, phraseLen);
            if (kind == 1)
            {
                // A': repeat with a varied ending
                for (int i = 0; i < phraseLen; i++) phrase[i] = previousPhrase[i];
                for (int i = phraseLen - 1; i >= phraseLen / 2; i--)
                    if (phrase[i] != Rest) { phrase[i] = StepFrom(phrase[i], Rand() < 0.5f ? 1 : -1); break; }
                return;
            }
            for (int i = 0; i < phraseLen; i++) phrase[i] = Rest;
            if (kind == 3 && Rand() < 0.6f) return; // breathing space
            int prev = lastMelody == Rest ? spec.melodyBase + spec.melodyDegrees[RandInt(spec.melodyDegrees.Length)] : lastMelody;
            for (int i = 0; i < phraseLen - 2; i++)
            {
                int s = i % spec.stepsPerBar;
                bool strong = s == 0 || (spec.stepsPerBar == 8 && s == 4) || (spec.stepsPerBar == 6 && s == 3);
                float p = spec.melodyDensity * (strong ? 1.35f : (s & 1) == 0 ? 0.8f : 0.45f);
                if (Rand() >= p) continue;
                int n;
                if (s == 0)
                {
                    // chord tone on the downbeat
                    int chord = ChordAt(bar + i / spec.stepsPerBar);
                    int[] tones = { chord, chord + 2, chord + 4 };
                    n = NearestAllowed(spec.melodyBase + tones[RandInt(3)] % 7, prev);
                }
                else n = StepFrom(prev, Rand() < 0.5f ? (Rand() < 0.7f ? 1 : 2) : (Rand() < 0.7f ? -1 : -2));
                phrase[i] = n;
                prev = n;
            }
            lastMelody = prev;
        }

        bool Allowed(int degree)
        {
            int d = ((degree % 7) + 7) % 7;
            for (int i = 0; i < spec.melodyDegrees.Length; i++) if (spec.melodyDegrees[i] == d) return true;
            return false;
        }

        int StepFrom(int degree, int dir)
        {
            int lo = spec.melodyBase - 2, hi = spec.melodyBase + 9;
            int moves = Math.Abs(dir), d = degree, sign = Math.Sign(dir);
            if (sign == 0) return degree;
            while (moves > 0)
            {
                d += sign;
                if (d > hi) { d = hi; sign = -1; }
                if (d < lo) { d = lo; sign = 1; }
                if (Allowed(d)) moves--;
            }
            return d;
        }

        int NearestAllowed(int target, int near)
        {
            // pick target's octave closest to the previous note
            int best = target, bestDist = int.MaxValue;
            for (int o = -14; o <= 14; o += 7)
            {
                int c = target + o;
                if (c < spec.melodyBase - 2 || c > spec.melodyBase + 9) continue;
                int dist = Math.Abs(c - near);
                if (dist < bestDist) { bestDist = dist; best = c; }
            }
            if (!Allowed(best)) best = StepFrom(best, 1);
            return best;
        }
    }

    internal sealed class MusicEngine
    {
        const int TableSize = 4096;
        const int MaxVoices = 40;
        static float[] sineTable, padTable;

        enum Kind { Off, Pad, Pluck, Bell, Bass, Kick, Shaker, Flute }

        sealed class Voice
        {
            public Kind kind;
            public Composer owner;
            public float ph1, ph2, ph3, inc1, inc2, inc3;
            public float env, e2, e3, d1, d2, d3, attackInc, releaseMul, amp, lp, lpA;
            public bool releasing;
            public int hold, age, delay;
            public float panL, panR;
            public readonly float[] ks = new float[2400];
            public int ksLen, ksPos;
            public float ksDamp;
            public float vibPh, vibInc;
            public float pitchEnv, pitchDecay;
            public uint noise = 2463534242u;
        }

        readonly int sr;
        readonly Voice[] voices = new Voice[MaxVoices];
        Composer current, fading;
        Composer pending;
        int pendingStop;              // 1 = fade current out with no replacement
        float pendingStopSeconds = 2f;
        public volatile float TargetGain = 0.6f;
        float gain;
        float reverbSend = 0.3f, delaySend = 0.15f;
        uint rng = 0x9E3779B9u;

        // effects
        readonly float[] dlyL, dlyR;
        int dlyPos;
        readonly int dlyLen;
        float dlyLpL, dlyLpR;
        readonly Comb[] combL, combR;
        readonly AllPass[] apL, apR;
        float[] mixL = new float[4096], mixR = new float[4096];

        sealed class Comb
        {
            readonly float[] buf; int pos; float store;
            public Comb(int len) { buf = new float[Math.Max(1, len)]; }
            public float Process(float x, float feedback, float damp)
            {
                float y = buf[pos];
                store = y * (1f - damp) + store * damp;
                buf[pos] = x + store * feedback;
                if (++pos >= buf.Length) pos = 0;
                return y;
            }
        }

        sealed class AllPass
        {
            readonly float[] buf; int pos;
            public AllPass(int len) { buf = new float[Math.Max(1, len)]; }
            public float Process(float x)
            {
                float b = buf[pos];
                float y = -x + b;
                buf[pos] = x + b * 0.5f;
                if (++pos >= buf.Length) pos = 0;
                return y;
            }
        }

        public MusicEngine(int sampleRate)
        {
            sr = Math.Max(8000, sampleRate);
            BuildTables();
            for (int i = 0; i < MaxVoices; i++) voices[i] = new Voice();
            dlyLen = (int)(sr * 0.36f);
            dlyL = new float[dlyLen + 1];
            dlyR = new float[dlyLen + 1];
            float k = sr / 44100f;
            int[] combs = { 1116, 1188, 1277, 1356 };
            combL = new Comb[combs.Length]; combR = new Comb[combs.Length];
            for (int i = 0; i < combs.Length; i++) { combL[i] = new Comb((int)(combs[i] * k)); combR[i] = new Comb((int)((combs[i] + 23) * k)); }
            int[] aps = { 556, 441 };
            apL = new AllPass[aps.Length]; apR = new AllPass[aps.Length];
            for (int i = 0; i < aps.Length; i++) { apL[i] = new AllPass((int)(aps[i] * k)); apR[i] = new AllPass((int)((aps[i] + 23) * k)); }
        }

        public int SampleRate => sr;

        /// <summary>Number of sounding voices (diagnostics).</summary>
        public int ActiveVoices { get { int n = 0; for (int i = 0; i < MaxVoices; i++) if (voices[i].kind != Kind.Off) n++; return n; } }

        static void BuildTables()
        {
            if (sineTable != null) return;
            var s = new float[TableSize + 1];
            var p = new float[TableSize + 1];
            float max = 0f;
            for (int i = 0; i <= TableSize; i++)
            {
                double ph = (double)i / TableSize * Math.PI * 2.0;
                s[i] = (float)Math.Sin(ph);
                p[i] = (float)(Math.Sin(ph) + 0.42 * Math.Sin(2 * ph) + 0.16 * Math.Sin(3 * ph) + 0.07 * Math.Sin(4 * ph) + 0.03 * Math.Sin(5 * ph));
                max = Math.Max(max, Math.Abs(p[i]));
            }
            for (int i = 0; i <= TableSize; i++) p[i] /= max;
            padTable = p;
            sineTable = s;
        }

        static float Lookup(float[] t, float ph)
        {
            float x = ph * TableSize;
            int i = (int)x;
            if (i >= TableSize) i = TableSize - 1;
            float f = x - i;
            return t[i] + (t[i + 1] - t[i]) * f;
        }

        static float Freq(int midi) => 440f * (float)Math.Pow(2.0, (midi - 69) / 12.0);

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        // ------------------------------------------------------------------ main-thread handoff

        public void Submit(Composer c) { Interlocked.Exchange(ref pending, c); Interlocked.Exchange(ref pendingStop, 0); }

        public void Stop(float seconds)
        {
            pendingStopSeconds = Math.Max(0.05f, seconds);
            Interlocked.Exchange(ref pendingStop, 1);
        }

        // ------------------------------------------------------------------ voices

        Voice Alloc(Composer owner, Kind kind)
        {
            Voice best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < MaxVoices; i++)
            {
                var v = voices[i];
                if (v.kind == Kind.Off) { best = v; break; }
                float score = v.env * (v.kind == Kind.Pad ? 4f : 1f) + (v.releasing ? 0f : 0.5f);
                if (score < bestScore) { bestScore = score; best = v; }
            }
            best.kind = kind;
            best.owner = owner;
            best.ph1 = best.ph2 = best.ph3 = 0f;
            best.env = best.e2 = best.e3 = 0f;
            best.releasing = false;
            best.age = 0;
            best.lp = 0f;
            best.delay = (int)(Rand() * sr * 0.012f);
            float pan = (Rand() - 0.5f) * 0.6f;
            SetPan(best, pan);
            return best;
        }

        static void SetPan(Voice v, float pan)
        {
            float a = (pan + 1f) * 0.25f * (float)Math.PI;
            v.panL = (float)Math.Cos(a);
            v.panR = (float)Math.Sin(a);
        }

        public void ReleasePads(Composer owner)
        {
            for (int i = 0; i < MaxVoices; i++)
            {
                var v = voices[i];
                if ((v.kind == Kind.Pad) && v.owner == owner) v.releasing = true;
            }
        }

        public void Pad(Composer o, int midi, int holdSteps, float pan, float level = 1f)
        {
            var v = Alloc(o, Kind.Pad);
            float f = Freq(midi);
            v.inc1 = f * 1.0021f / sr;
            v.inc2 = f * 0.9979f / sr;
            v.ph2 = Rand();
            v.amp = o.spec.pad * level;
            v.attackInc = 1f / (sr * 1.4f);
            v.releaseMul = (float)Math.Exp(-1.0 / (sr * 0.9));
            v.hold = holdSteps * o.samplesPerStep + (int)(sr * 0.2f);
            v.lpA = 1f - (float)Math.Exp(-2.0 * Math.PI * 1500.0 / sr);
            SetPan(v, pan);
        }

        public void Pluck(Composer o, int midi, float amp)
        {
            var v = Alloc(o, Kind.Pluck);
            float f = Math.Min(1500f, Freq(midi));
            v.ksLen = Math.Min(v.ks.Length - 1, Math.Max(2, (int)(sr / f)));
            float lp = 0f;
            for (int i = 0; i < v.ksLen; i++) { lp += ((Rand() * 2f - 1f) - lp) * 0.45f; v.ks[i] = lp; }
            v.ksPos = 0;
            v.ksDamp = 0.9965f;
            v.amp = amp * 1.7f;
            v.hold = Math.Max(sr, Math.Min((int)(sr * (f < 200f ? 3.2f : 2.4f)), o.samplesPerStep * 6));
        }

        public void Bell(Composer o, int midi, float amp)
        {
            var v = Alloc(o, Kind.Bell);
            float f = Freq(midi);
            v.inc1 = f / sr; v.inc2 = f * 2f / sr; v.inc3 = f * 4.07f / sr;
            v.env = 0f; v.e2 = 1f; v.e3 = 1f;
            v.d1 = (float)Math.Exp(-1.0 / (sr * 1.15));
            v.d2 = (float)Math.Exp(-1.0 / (sr * 0.35));
            v.d3 = (float)Math.Exp(-1.0 / (sr * 0.12));
            v.attackInc = 1f / (sr * 0.003f);
            v.amp = amp;
            v.hold = sr * 6;
        }

        public void Bass(Composer o, int midi, float amp)
        {
            // monophonic: the previous bass note of this composer fades out quickly
            float quick = (float)Math.Exp(-1.0 / (sr * 0.04));
            for (int i = 0; i < MaxVoices; i++)
                if (voices[i].kind == Kind.Bass && voices[i].owner == o && !voices[i].releasing) { voices[i].releasing = true; voices[i].releaseMul = quick; }
            var v = Alloc(o, Kind.Bass);
            v.inc1 = Freq(midi) / sr;
            v.attackInc = 1f / (sr * 0.02f);
            v.d1 = (float)Math.Exp(-1.0 / (sr * 1.1));
            v.amp = amp;
            v.hold = sr * 5;
            SetPan(v, 0f);
        }

        public void Kick(Composer o, float amp)
        {
            var v = Alloc(o, Kind.Kick);
            v.pitchEnv = 1f;
            v.pitchDecay = (float)Math.Exp(-1.0 / (sr * 0.03));
            v.d1 = (float)Math.Exp(-1.0 / (sr * 0.16));
            v.env = 1f;
            v.amp = amp;
            v.hold = sr;
            v.delay = 0;
            SetPan(v, 0f);
        }

        public void Shaker(Composer o, float amp)
        {
            var v = Alloc(o, Kind.Shaker);
            v.env = 1f;
            v.d1 = (float)Math.Exp(-1.0 / (sr * 0.035));
            v.amp = amp;
            v.hold = sr / 4;
            v.lpA = 1f - (float)Math.Exp(-2.0 * Math.PI * 3500.0 / sr);
        }

        public void Flute(Composer o, int midi, int holdSteps)
        {
            var v = Alloc(o, Kind.Flute);
            v.inc1 = Freq(midi) / sr;
            v.attackInc = 1f / (sr * 0.12f);
            v.releaseMul = (float)Math.Exp(-1.0 / (sr * 0.18));
            v.hold = holdSteps * o.samplesPerStep;
            v.amp = o.spec.flute;
            v.vibPh = 0f;
            v.vibInc = 5.2f / sr;
            v.lpA = 1f - (float)Math.Exp(-2.0 * Math.PI * 1200.0 / sr);
        }

        void RenderVoice(Voice v, int n)
        {
            float g = v.owner != null ? v.owner.gainPrev : 0f;
            float dg = v.owner != null ? (v.owner.gain - v.owner.gainPrev) / n : 0f;
            for (int i = 0; i < n; i++)
            {
                g += dg;
                if (v.delay > 0) { v.delay--; continue; }
                float s = 0f;
                v.age++;
                switch (v.kind)
                {
                    case Kind.Pad:
                        if (!v.releasing) { v.env = Math.Min(1f, v.env + v.attackInc); if (v.age > v.hold) v.releasing = true; }
                        else { v.env *= v.releaseMul; if (v.env < 0.002f) { v.kind = Kind.Off; return; } }
                        v.ph1 += v.inc1; if (v.ph1 >= 1f) v.ph1 -= 1f;
                        v.ph2 += v.inc2; if (v.ph2 >= 1f) v.ph2 -= 1f;
                        s = (Lookup(padTable, v.ph1) + Lookup(padTable, v.ph2)) * 0.5f;
                        v.lp += (s - v.lp) * v.lpA;
                        s = v.lp * v.env;
                        break;
                    case Kind.Pluck:
                    {
                        int nxt = v.ksPos + 1; if (nxt >= v.ksLen) nxt = 0;
                        float y = v.ks[v.ksPos];
                        v.ks[v.ksPos] = (y + v.ks[nxt]) * 0.5f * v.ksDamp;
                        v.ksPos = nxt;
                        int left = v.hold - v.age;
                        if (left <= 0) { v.kind = Kind.Off; return; }
                        s = left < 16384 ? y * (left / 16384f) : y;
                        break;
                    }
                    case Kind.Bell:
                        v.env = v.age < 200 ? Math.Min(1f, v.env + v.attackInc) : v.env * v.d1;
                        v.e2 *= v.d2; v.e3 *= v.d3;
                        if (v.age > 200 && v.env < 0.003f) { v.kind = Kind.Off; return; }
                        v.ph1 += v.inc1; if (v.ph1 >= 1f) v.ph1 -= 1f;
                        v.ph2 += v.inc2; if (v.ph2 >= 1f) v.ph2 -= 1f;
                        v.ph3 += v.inc3; if (v.ph3 >= 1f) v.ph3 -= 1f;
                        s = Lookup(sineTable, v.ph1) * v.env + Lookup(sineTable, v.ph2) * v.e2 * 0.3f * v.env + Lookup(sineTable, v.ph3) * v.e3 * 0.14f * Math.Min(1f, v.env * 4f);
                        break;
                    case Kind.Bass:
                        v.env = v.releasing ? v.env * v.releaseMul : v.age < sr / 50 ? Math.Min(1f, v.env + v.attackInc) : v.env * v.d1;
                        if (v.age > sr / 50 && v.env < 0.003f) { v.kind = Kind.Off; return; }
                        v.ph1 += v.inc1; if (v.ph1 >= 1f) v.ph1 -= 1f;
                        { float p2 = v.ph1 * 2f; if (p2 >= 1f) p2 -= 1f; s = (Lookup(sineTable, v.ph1) + 0.22f * Lookup(sineTable, p2)) * v.env; }
                        break;
                    case Kind.Kick:
                        v.pitchEnv *= v.pitchDecay;
                        v.env *= v.d1;
                        if (v.env < 0.002f) { v.kind = Kind.Off; return; }
                        v.ph1 += (46f + 80f * v.pitchEnv) / sr; if (v.ph1 >= 1f) v.ph1 -= 1f;
                        s = Lookup(sineTable, v.ph1) * v.env;
                        break;
                    case Kind.Shaker:
                    {
                        v.env *= v.d1;
                        if (v.env < 0.002f) { v.kind = Kind.Off; return; }
                        v.noise ^= v.noise << 13; v.noise ^= v.noise >> 17; v.noise ^= v.noise << 5;
                        float x = (v.noise & 0xFFFF) / 32768f - 1f;
                        v.lp += (x - v.lp) * v.lpA;
                        s = (x - v.lp) * v.env;
                        break;
                    }
                    case Kind.Flute:
                    {
                        if (!v.releasing) { v.env = Math.Min(1f, v.env + v.attackInc); if (v.age > v.hold) v.releasing = true; }
                        else { v.env *= v.releaseMul; if (v.env < 0.002f) { v.kind = Kind.Off; return; } }
                        v.vibPh += v.vibInc; if (v.vibPh >= 1f) v.vibPh -= 1f;
                        float vib = v.age > sr / 4 ? Lookup(sineTable, v.vibPh) * 0.004f : 0f;
                        v.ph1 += v.inc1 * (1f + vib); if (v.ph1 >= 1f) v.ph1 -= 1f;
                        float p2 = v.ph1 * 2f; if (p2 >= 1f) p2 -= 1f;
                        v.noise ^= v.noise << 13; v.noise ^= v.noise >> 17; v.noise ^= v.noise << 5;
                        float breath = ((v.noise & 0xFFFF) / 32768f - 1f) * 0.05f;
                        v.lp += (breath - v.lp) * v.lpA;
                        s = (Lookup(sineTable, v.ph1) + 0.12f * Lookup(sineTable, p2) + v.lp) * v.env;
                        break;
                    }
                    default:
                        return;
                }
                s *= v.amp * g;
                mixL[i] += s * v.panL;
                mixR[i] += s * v.panR;
            }
        }

        // ------------------------------------------------------------------ audio thread

        public void Render(float[] data, int channels)
        {
            if (channels <= 0) return;
            int frames = data.Length / channels;
            int offset = 0;
            while (offset < frames)
            {
                int n = Math.Min(frames - offset, mixL.Length);
                RenderBlock(data, channels, offset, n);
                offset += n;
            }
        }

        void RenderBlock(float[] data, int channels, int frameOffset, int frames)
        {
            // take over a newly submitted composer
            var p = Interlocked.Exchange(ref pending, null);
            if (p != null)
            {
                if (fading != null) KillVoices(fading);
                fading = current;
                if (fading != null) fading.FadeOut(Math.Max(0.3f, 1f / (p.gainStep * sr)), sr);
                current = p;
            }
            if (Interlocked.Exchange(ref pendingStop, 0) == 1 && current != null)
            {
                if (fading != null) KillVoices(fading);
                current.FadeOut(pendingStopSeconds, sr);
                fading = current;
                current = null;
            }

            int done = 0;
            while (done < frames)
            {
                int n = frames - done;
                if (current != null) n = Math.Min(n, current.samplesToStep);
                if (fading != null) n = Math.Min(n, fading.samplesToStep);
                n = Math.Max(1, n);

                Array.Clear(mixL, 0, n);
                Array.Clear(mixR, 0, n);
                RampGain(current, n);
                RampGain(fading, n);
                for (int i = 0; i < MaxVoices; i++)
                {
                    var v = voices[i];
                    if (v.kind == Kind.Off) continue;
                    RenderVoice(v, n);
                }
                Mix(data, channels, frameOffset + done, n);

                AdvanceComposer(current, n);
                AdvanceComposer(fading, n);
                if (fading != null && fading.gain <= 0f && fading.targetGain <= 0f)
                {
                    KillVoices(fading);
                    fading = null;
                }
                done += n;
            }
        }

        static void RampGain(Composer c, int n)
        {
            if (c == null) return;
            // voices interpolate gainPrev → gain per sample over this sub-block
            c.gainPrev = c.gain;
            float delta = c.gainStep * n;
            if (c.gain < c.targetGain) c.gain = Math.Min(c.targetGain, c.gain + delta);
            else if (c.gain > c.targetGain) c.gain = Math.Max(c.targetGain, c.gain - delta);
        }

        void AdvanceComposer(Composer c, int n)
        {
            if (c == null) return;
            c.samplesToStep -= n;
            if (c.samplesToStep <= 0)
            {
                c.samplesToStep += c.samplesPerStep;
                if (!c.Released) c.Step(this);
            }
        }

        void KillVoices(Composer c)
        {
            for (int i = 0; i < MaxVoices; i++)
                if (voices[i].owner == c) { voices[i].kind = Kind.Off; voices[i].owner = null; }
        }

        void Mix(float[] data, int channels, int frameOffset, int n)
        {
            float target = TargetGain;
            float rs = current != null ? current.spec.reverb : reverbSend;
            float ds = current != null ? current.spec.delay : delaySend;
            const float fb = 0.86f, damp = 0.32f;
            float dlyLpA = 0.35f;
            for (int i = 0; i < n; i++)
            {
                gain += (target - gain) * 0.0005f;
                reverbSend += (rs - reverbSend) * 0.00002f;
                delaySend += (ds - delaySend) * 0.00002f;
                float l = mixL[i], r = mixR[i];
                float mono = (l + r) * 0.5f;

                // ping-pong delay with a soft low-pass in the loop
                float rl = dlyL[dlyPos], rr = dlyR[dlyPos];
                dlyLpL += (rr - dlyLpL) * dlyLpA;
                dlyLpR += (rl - dlyLpR) * dlyLpA;
                dlyL[dlyPos] = mono * delaySend + dlyLpL * 0.38f;
                dlyR[dlyPos] = dlyLpR * 0.38f;
                if (++dlyPos >= dlyLen) dlyPos = 0;
                l += rl; r += rr;

                // reverb
                float vin = mono * reverbSend * 0.35f;
                float wl = 0f, wr = 0f;
                for (int k = 0; k < combL.Length; k++) { wl += combL[k].Process(vin, fb, damp); wr += combR[k].Process(vin, fb, damp); }
                for (int k = 0; k < apL.Length; k++) { wl = apL[k].Process(wl); wr = apR[k].Process(wr); }
                l += wl; r += wr;

                l *= gain * 0.9f; r *= gain * 0.9f;
                l = l / (1f + Math.Abs(l));
                r = r / (1f + Math.Abs(r));

                int idx = (frameOffset + i) * channels;
                if (channels == 1) data[idx] += (l + r) * 0.5f;
                else
                {
                    data[idx] += l;
                    data[idx + 1] += r;
                }
            }
        }
    }
}
