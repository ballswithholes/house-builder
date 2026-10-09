// Spell recipes: per-school wind-ups (cast_<school>; cast_start stays as the fallback), upgraded school impacts,
// lightning, the warrior's horn-call shout and the Thunder Clap stomp. Wind-ups swell towards the release
// (UnitView.CastReleaseTime ≈ 0.45 s) and peak just before it (≈ 0.38–0.42 s; CombatSfx speeds them up with the
// presentation); impacts carry a transient so they read on small speakers.
namespace Lanternvale.Game
{
    internal static partial class SfxSynth
    {
        // vowel formants (F1, F2, F3), adult averages
        static readonly float[] VowelA = { 800f, 1150f, 2900f };   // "ah"
        static readonly float[] VowelO = { 450f, 800f, 2830f };    // "oh"
        static readonly float[] VowelU = { 350f, 600f, 2700f };    // "oo"
        static readonly float[] VowelE = { 400f, 2000f, 2550f };   // "eh/ee"
        static readonly float[] VowelUh = { 600f, 1170f, 2400f };  // "uh"
        static readonly float[] FormantGains = { 1f, 0.55f, 0.25f };

        static void AddSpells(Registry r)
        {
            // ---------------------------------------------------------------- wind-ups (K = 2)

            r.Add("cast_fire", 1, 2, 0.45f, 0.8f, (b, v) =>
            {
                b.Band(0f, 0.65f, 400f, b.Jit(2600f, 0.1f), 0.8f, 0.45f, 0.6f, 0f);               // inhale: rising, swelling
                b.Band(0.05f, 0.6f, 160f, 320f, 0.9f, 0.3f, 0.6f, 0f);                             // low roar
                b.Crackle(0.15f, 0.55f, 45f, 0.25f);
                b.Grains(0.2f, 0.45f, 12, GrainKind.Click, 1500f, 4000f, 0.002f, 0.006f, 0.12f, 0f); // embers pop
                b.CutLows(90f);
            });
            r.Add("cast_frost", 1, 2, 0.42f, 0.85f, (b, v) =>
            {
                float f = b.U(2400f, 2800f);
                b.Swell(0f, 0.7f, new[] { f, f * 1.34f, f * 1.68f, f * 2.41f }, 0.35f, 0.55f, 7f); // glassy shimmer
                b.Grains(0.1f, 0.55f, 24, GrainKind.Metal, 4000f, 9000f, 0.003f, 0.01f, 0.12f, 0f); // crystals forming
                b.Band(0f, 0.7f, 6000f, 8000f, 0.7f, 0.12f, 0.55f, 0f);                            // cold air
                SmallRoom(b, 0.15f, 0.5f);
                b.CutLows(400f);
            });
            r.Add("cast_arcane", 1, 2, 0.42f, 1.1f, (b, v) =>
            {
                float f = b.U(560f, 640f);
                b.Fm(0f, 0.65f, f, 1.41f, 0.2f, 3.2f, 0.32f, 0.38f, 0.4f, f * 1.12f);             // FM shimmer, index rising
                b.Swell(0f, 0.65f, new[] { f * 2f, f * 3f, f * 4.24f }, 0.18f, 0.6f, 11f);          // reverse swell
                b.Grains(0.25f, 0.4f, 10, GrainKind.Metal, 3000f, 7000f, 0.005f, 0.015f, 0.1f, 0f);
                SmallRoom(b, 0.18f, 0.6f);
                b.CutLows(150f);
            });
            r.Add("cast_shadow", 1, 2, 0.45f, 1.2f, (b, v) =>
            {
                b.Voice(0f, 0.7f, b.U(75f, 95f), b.U(100f, 115f), VowelU, FormantGains, 0.3f, 0.36f, 0.12f, 0.35f, 0.02f, 0.5f); // low growl
                b.Whoosh(0f, 0.7f, 1800f, 3500f, 1.1f, 0.22f, 0.58f, 2f, 7f, 0.6f);               // whisper
                b.Band(0f, 0.7f, 300f, 600f, 1.2f, 0.25f, 0.6f, 0f);                               // reversed swell
                b.Reverb(0.8f, 0.2f, 0.6f, 0.5f, 12f);
                b.CutLows(70f);
            });
            r.Add("cast_holy", 1, 2, 0.42f, 1.5f, (b, v) =>
            {
                // a soft choir "ah" on a major triad (C5 E5 G5), two detuned singers per note, plus bell partials
                int[] notes = { 72, 76, 79 };
                foreach (int m in notes)
                {
                    float f = Midi(m);
                    b.Voice(0f, 0.8f, f * 1.003f, f * 1.003f, VowelA, FormantGains, 0.12f, 0.3f, 0.25f, 0.06f, 0.004f, 0f, 5f, 0.006f);
                    b.Voice(0.02f, 0.78f, f * 0.997f, f * 0.997f, VowelA, FormantGains, 0.1f, 0.3f, 0.25f, 0.06f, 0.004f, 0f, 5.5f, 0.006f);
                }
                b.Strike(0.35f, Midi(84), Modes.Bell, 0.5f, 0.4f, 0.12f, 0.3f, 0.3f, 0.002f);
                b.Band(0f, 0.8f, 9000f, 11000f, 0.7f, 0.06f, 0.6f, 0f);
                b.Reverb(1f, 0.2f, 0.7f, 0.3f, 15f);
                b.CutLows(200f);
            });
            r.Add("cast_nature", 1, 2, 0.45f, 1.2f, (b, v) =>
            {
                b.Grains(0f, 0.6f, 40, GrainKind.Noise, 2000f, 6000f, 0.001f, 0.004f, 0.18f, 0f, 0f); // leaf rustle
                b.StickSlip(0.05f, 0.5f, 22f, 40f, 0.3f, b.U(260f, 320f), Modes.Wood, 0.02f, 0.28f); // wood creak
                int[] pent = { 79, 81, 84, 86, 88 };
                for (int i = 0; i < 3; i++) b.Bell(0.3f + i * 0.09f, Midi(pent[b.UInt(0, pent.Length - 1)]), 0.12f, 0.12f);
                SmallRoom(b, 0.12f, 0.4f);
                b.CutLows(120f);
            });
            r.Add("cast_lightning", 1, 2, 0.45f, 0.8f, (b, v) =>
            {
                // electric hum: a square at ~120 Hz through a band-pass, getting louder, plus a rising crackle
                b.Tone(0f, 0.7f, b.U(115f, 125f), 122f, 0.25f, 0.38f, 0.6f, SynthBuffer.Wave.Square, 0f, 0f, 1400f);
                b.Whoosh(0f, 0.7f, 2500f, 6000f, 0.8f, 0.2f, 0.6f, 2f, 50f, 0.7f);
                for (int i = 0; i < 18; i++)
                {
                    float t = 0.45f * (float)System.Math.Sqrt(b.U(0f, 1f));                    // denser towards the release
                    b.Click(t, b.U(0.3f, 1.2f), 3000f, b.U(0.15f, 0.4f) * (0.3f + t * 1.55f));
                }
                b.CutLows(90f);
            });

            // ---------------------------------------------------------------- school impacts (keep the ids, K = 2)

            r.Add("impact_fire", 0, 2, 0.75f, 0.85f, (b, v) =>
            {
                b.Click(0.002f, 2f, 1500f, 0.6f, 6000f);
                b.Band(0f, 0.22f, 400f, 900f, 0.9f, 0.6f, 0.01f, 0.08f);                          // whoosh body
                b.Noise(0f, 0.7f, 0.5f, 0.01f, 0.2f, 3500f, 400f);                               // the old roar
                b.Thump(0f, 110f, 60f, 0.08f, 0.25f);
                b.Crackle(0.04f, 0.6f, 70f, 0.35f);
                SmallRoom(b, 0.12f, 0.4f);
                b.CutLows(70f);
            });
            r.Add("impact_frost", 0, 2, 0.62f, 0.8f, (b, v) =>
            {
                b.Click(0.002f, 0.4f, 4000f, 0.7f);
                b.Click(0.002f + b.U(0.008f, 0.015f), 0.4f, 4000f, 0.4f);                         // crack
                b.Strike(0.002f, b.U(1500f, 2500f), Modes.Glass, 0.12f, 0.6f, 0.4f, 0.1f, b.U(0.1f, 0.4f));
                b.Grains(0.01f, 0.15f, b.UInt(30, 60), GrainKind.Metal, 3000f, 9000f, 0.003f, 0.01f, 0.2f, 0.05f); // shatter
                b.Noise(0f, 0.3f, 0.3f, 0.001f, 0.07f, 9000f, 6000f, 2500f);
                SmallRoom(b, 0.12f, 0.45f);
                b.CutLows(200f);
            });
            r.Add("impact_arcane", 0, 2, 0.6f, 0.8f, (b, v) =>
            {
                float f0 = v == 1 ? 880f : 784f, vib = b.U(7f, 11f);                             // A5 / G5: two distinct strikes
                b.Tone(0f, 0.65f, f0, f0 * 0.375f, 0.4f, 0.005f, 0.22f, SynthBuffer.Wave.Sine, vib, 0.03f);
                b.Tone(0f, 0.65f, f0 * 1.5f, f0 * 0.5625f, 0.18f, 0.005f, 0.2f, SynthBuffer.Wave.Sine, vib, 0.03f);
                b.Thump(0f, 200f, 120f, 0.06f, 0.3f);                                            // soft thoom
                b.Click(0.001f, 1f, 2000f, 0.3f);
                b.Bell(0.05f, Midi(v == 1 ? 96 : 95), 0.12f, 0.06f);
                b.Bell(0.12f, Midi(v == 1 ? 100 : 98), 0.1f, 0.06f);
                SmallRoom(b, 0.14f, 0.5f);
                b.CutLows(90f);
            });
            r.Add("impact_shadow", 0, 2, 0.65f, 0.9f, (b, v) =>
            {
                b.Band(0f, 0.25f, 600f, 200f, 1f, 0.6f, 0.01f, 0.09f);                           // whump in 200-600 Hz
                b.Thump(0f, 120f, 85f, 0.08f, 0.25f);
                b.Whoosh(0.05f, 0.7f, 1000f, 3000f, 1.2f, 0.25f, 0.3f, 2f, 9f, 0.6f);             // dark whisper tail
                b.Click(0.001f, 1.5f, 1200f, 0.3f, 4000f);
                b.Reverb(0.7f, 0.18f, 0.6f, 0.5f, 10f);
                b.CutLows(70f);
            });
            r.Add("impact_holy", 0, 2, 0.58f, 1.6f, (b, v) =>
            {
                b.Strike(0f, Midi(84), Modes.Bell, 0.45f, 0.4f, 0.3f, 0.25f, b.U(0.2f, 0.35f), 0.002f); // bright bell cluster
                b.Bell(0.02f, Midi(88), 0.3f, 0.2f);
                b.Bell(0.04f, Midi(91), 0.28f, 0.2f);
                b.Thump(0f, 180f, 110f, 0.05f, 0.25f);                                           // soft thud
                b.Noise(0f, 0.8f, 0.06f, 0f, 0f, 12000f, 12000f, 6000f, true);
                b.Reverb(0.9f, 0.15f, 0.6f, 0.3f, 12f);
                b.CutLows(100f);
            });
            r.Add("impact_nature", 0, 2, 0.85f, 0.7f, (b, v) =>
            {
                b.Strike(0.002f, b.U(200f, 300f), Modes.Wood, 0.05f, 1.1f, 0.5f, 0.7f, b.U(0.2f, 0.4f)); // wood thud
                b.Grains(0.005f, 0.25f, 30, GrainKind.Noise, 2000f, 6000f, 0.001f, 0.004f, 0.2f, 0.08f); // rustle
                b.Band(0.002f, 0.04f, 1600f, 1100f, 1.2f, 0.35f, 0.001f, 0.012f);                 // splatter
                b.Grains(0.002f, 0.06f, 6, GrainKind.Click, 1800f, 3500f, 0.002f, 0.006f, 0.15f, 0.02f); // thorns
                SmallRoom(b, 0.1f);
                b.CutLows(90f);
            });
            r.Add("impact_lightning", 1, 2, 0.8f, 1.1f, (b, v) =>
            {
                b.Click(0.001f, 4f, 2000f, 1f);                                                   // the crack
                float t = 0.008f;
                int bursts = b.UInt(3, 6);
                for (int i = 0; i < bursts; i++)
                {
                    float a = 0.7f * (float)System.Math.Pow(0.72, i);                             // stepwise decay
                    b.Band(t, b.U(0.02f, 0.04f), b.U(4000f, 6000f), 3500f, 0.5f, a, 0.0005f, 0.012f);
                    b.Tone(t, 0.03f, b.U(90f, 140f), 80f, a * 0.4f, 0.0005f, 0.01f, SynthBuffer.Wave.Saw, 0f, 0f, 3000f); // zzt
                    t += b.U(0.02f, 0.05f);
                }
                b.Thump(0.005f, 90f, 50f, 0.3f, 0.16f);                                           // rumble
                b.Band(0.01f, 0.8f, 120f, 60f, 0.7f, 0.14f, 0.05f, 0.25f);
                b.Saturate(3f);
                b.Reverb(0.9f, 0.2f, 0.7f, 0.4f, 15f);
                b.CutLows(45f);
            });

            // ---------------------------------------------------------------- shout and stomp (K = 2)

            r.Add("shout_horn", 1, 2, 0.6f, 1.3f, (b, v) =>
            {
                float f = Midi(v == 1 ? 55 : 57);                                                  // G3 or A3: one each
                b.Horn(0f, 0.7f, f, 0.5f, 0.06f, 0.2f, 3f);
                b.Horn(0.04f, 0.66f, f * 1.5f, 0.22f, 0.08f, 0.2f, 3f, 5.6f);                     // a fifth above
                b.Reverb(0.9f, 0.25f, 0.7f, 0.35f, 18f);
                b.CutLows(80f);
            });
            r.Add("stomp", 1, 2, 0.82f, 0.9f, (b, v) =>
            {
                b.Click(0.002f, 3f, 1200f, 0.6f, 5000f);
                b.Thump(0.002f, b.U(80f, 95f), 50f, 0.15f, 0.42f);
                b.Band(0.002f, 0.2f, 220f, 90f, 0.8f, 0.4f, 0.003f, 0.07f);
                b.Whoosh(0f, 0.3f, 400f, 1800f, 0.8f, 0.4f, 0.12f, 2f);                           // air burst
                b.Grains(0.01f, 0.3f, 40, GrainKind.Noise, 1200f, 4000f, 0.003f, 0.01f, 0.3f, 0.1f); // dust and debris
                b.Reverb(0.6f, 0.16f, 0.6f, 0.4f, 8f);
                b.CutLows(48f);
            });
        }
    }
}
