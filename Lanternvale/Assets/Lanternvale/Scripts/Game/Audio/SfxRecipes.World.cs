// World recipes: footsteps per surface (heel then toe, 30–50 ms apart), the armour jingle that rides on every second
// step of a mail or plate wearer, and the expansion's hooks — loot fanfares by quality, set completion, secrets,
// the stone door, boss pull, raid warning, quest accept and turn-in, and the portal.
namespace Lanternvale.Game
{
    internal static partial class SfxSynth
    {
        /// <summary>Heel + toe pattern; draw(t, weight) adds one contact at time t with weight 1 (heel) or ~0.6 (toe).</summary>
        static void Step(SynthBuffer b, System.Action<float, float> draw)
        {
            draw(0.002f, 1f);
            draw(0.002f + b.U(0.03f, 0.05f), b.U(0.5f, 0.7f));
        }

        static void AddWorld(Registry r)
        {
            // ---------------------------------------------------------------- footsteps (K = 4)

            r.Add("footstep_grass", 1, 4, 0.3f, 0.2f, (b, v) =>
            {
                Step(b, (t, w) =>
                {
                    b.Band(t, 0.06f, 260f, 150f, 1f, 0.35f * w, 0.003f, 0.018f);                 // soft thud
                    b.Band(t, 0.08f, b.Jit(1800f, 0.15f), 900f, 0.7f, 0.35f * w, 0.35f, 0f);    // swish
                    b.Grains(t, 0.06f, 16, GrainKind.Noise, 3000f, 7000f, 0.0008f, 0.003f, 0.12f * w, 0f);
                });
                b.CutLows(80f);
            });
            r.Add("footstep_dirt", 1, 4, 0.32f, 0.2f, (b, v) =>
            {
                Step(b, (t, w) =>
                {
                    b.Band(t, 0.06f, 280f, 160f, 1f, 0.45f * w, 0.002f, 0.018f);
                    b.Thump(t, 130f, 90f, 0.02f, 0.08f * w);
                    b.Grains(t, 0.05f, 12, GrainKind.Noise, 1500f, 4000f, 0.001f, 0.004f, 0.2f * w, 0.02f); // grit
                });
                b.CutLows(70f);
            });
            r.Add("footstep_leaves", 1, 4, 0.32f, 0.3f, (b, v) =>
            {
                Step(b, (t, w) =>
                {
                    b.Band(t, 0.05f, 240f, 150f, 1f, 0.3f * w, 0.003f, 0.016f);
                    b.Grains(t, 0.1f, b.UInt(30, 50), GrainKind.Noise, 2000f, 8000f, 0.0008f, 0.003f, 0.25f * w, 0.03f); // crunch
                    b.Grains(t, 0.05f, b.UInt(1, 3), GrainKind.Click, 1500f, 4000f, 0.002f, 0.005f, 0.2f * w, 0.02f); // twig
                });
                b.CutLows(90f);
            });
            r.Add("footstep_stone", 1, 4, 0.32f, 0.18f, (b, v) =>
            {
                Step(b, (t, w) =>
                {
                    b.Click(t, 0.6f, 2000f, 0.5f * w);                                             // hard heel
                    b.Strike(t, b.U(1200f, 2500f), Modes.Stone, 0.005f, 0.9f, 0.3f * w, 0.2f, b.U(0.2f, 0.4f));
                    b.Band(t, 0.04f, 300f, 200f, 1f, 0.3f * w, 0.002f, 0.012f);
                    b.Grains(t, 0.03f, 5, GrainKind.Noise, 2000f, 5000f, 0.001f, 0.003f, 0.12f * w, 0f); // grit under the sole
                });
                SmallRoom(b, 0.06f, 0.25f);
                b.CutLows(90f);
            });
            r.Add("footstep_snow", 1, 4, 0.3f, 0.32f, (b, v) =>
            {
                Step(b, (t, w) =>
                {
                    // compression crunch: dense, uniform crackle of breaking crystal bonds, then a faint squeak
                    b.Grains(t, b.Jit(0.1f, 0.15f), 60, GrainKind.Noise, 800f, 4000f, 0.001f, 0.004f, 0.2f * w, 0f, 0.2f);
                    b.Band(t + 0.02f, 0.06f, 1300f, 1500f, 6f, 0.05f * w, 0.4f, 0f);
                    b.Band(t, 0.06f, 220f, 140f, 1f, 0.2f * w, 0.004f, 0.02f);
                });
                b.Filter(Biquad.Kind.LowPass, 5000f, 0.7f);
                b.CutLows(90f);
            });
            r.Add("footstep_mud", 1, 4, 0.32f, 0.3f, (b, v) =>
            {
                Step(b, (t, w) =>
                {
                    b.Band(t, 0.06f, 260f, 150f, 1f, 0.4f * w, 0.003f, 0.02f);                    // sink in
                    b.Band(t + 0.04f, 0.12f, 400f, 900f, 3f, 0.25f * w, 0.25f, 0f);                 // suction release
                    b.Grains(t + 0.03f, 0.1f, b.UInt(3, 6), GrainKind.Bubble, 300f, 900f, 0.006f, 0.012f, 0.25f * w, 0.04f);
                });
                b.CutLows(80f);
            });
            r.Add("armor_jingle", 1, 4, 0.25f, 0.2f, (b, v) =>
            {
                b.Grains(0.002f, 0.06f, b.UInt(6, 12), GrainKind.Metal, 3000f, 7000f, 0.004f, 0.012f, 0.3f, 0.02f);
                b.Mode(0.002f + b.U(0f, 0.02f), b.U(1000f, 2000f), 0.05f, 0.08f, 0.15f);       // a plate tink
                b.CutLows(300f);
            });

            // ---------------------------------------------------------------- hooks (K = 2)

            r.Add("loot_rare", 2, 2, 0.5f, 1.3f, (b, v) =>
            {
                int[] notes = { 88, 92, 95 };                                                     // E6 G#6 B6
                for (int i = 0; i < notes.Length; i++) b.Bell(i * 0.07f, Midi(notes[i]), 0.35f, 0.18f);
                b.Grains(0.05f, 0.6f, 14, GrainKind.Metal, 5000f, 10000f, 0.01f, 0.03f, 0.08f, 0.2f); // sparkle
                SmallRoom(b, 0.15f, 0.6f);
                b.CutLows(200f);
            });
            r.Add("loot_epic", 2, 2, 0.52f, 2f, (b, v) =>
            {
                int[] notes = { 74, 78, 81, 85, 88 };                                             // D lydian rising
                for (int i = 0; i < notes.Length; i++)
                {
                    b.Bell(i * 0.08f, Midi(notes[i]), 0.3f, 0.22f);
                    b.Pluck(i * 0.08f, Midi(notes[i] - 12), 0.14f, 0.997f, 0.45f);
                }
                b.Fm(0.25f, 1.4f, Midi(62), 2.01f, 0.4f, 1.6f, 0.08f, 0.4f, 0.8f);               // shimmering pad
                b.Swell(0f, 1.2f, new[] { Midi(50), Midi(57) }, 0.12f, 0.5f);                     // low swell
                b.Grains(0.3f, 1f, 20, GrainKind.Metal, 5000f, 10000f, 0.01f, 0.03f, 0.07f, 0.4f);
                b.Reverb(1.2f, 0.18f, 0.7f, 0.3f, 15f);
                b.CutLows(80f);
            });
            r.Add("loot_legendary", 2, 2, 0.55f, 3.6f, (b, v) =>
            {
                // a choir "ah" chord blooming under a bell cluster, a low boom swell and a long sparkle
                int[] chord = { 60, 67, 72, 76 };
                foreach (int m in chord)
                {
                    float f = Midi(m);
                    b.Voice(0.1f, 2f, f * 1.003f, f * 1.003f, VowelA, FormantGains, 0.08f, 0.5f, 0.7f, 0.05f, 0.003f, 0f, 5f, 0.006f);
                    b.Voice(0.12f, 2f, f * 0.997f, f * 0.997f, VowelO, FormantGains, 0.07f, 0.5f, 0.7f, 0.05f, 0.003f, 0f, 5.4f, 0.006f);
                }
                b.Strike(0f, Midi(84), Modes.Bell, 0.8f, 0.4f, 0.25f, 0.3f, 0.3f, 0.002f);
                int[] run = { 84, 88, 91, 96 };
                for (int i = 0; i < run.Length; i++) b.Bell(0.12f + i * 0.09f, Midi(run[i]), 0.22f, 0.25f);
                b.Thump(0f, 70f, 50f, 0.5f, 0.3f);                                                // low boom
                b.Swell(0f, 1.6f, new[] { Midi(36), Midi(43) }, 0.15f, 0.35f);
                b.Grains(0.2f, 2f, 40, GrainKind.Metal, 5000f, 11000f, 0.01f, 0.04f, 0.07f, 0.8f);
                b.Reverb(1.6f, 0.2f, 0.85f, 0.3f, 20f);
                b.CutLows(40f);
            });
            r.Add("set_complete", 2, 2, 0.52f, 1.8f, (b, v) =>
            {
                for (int i = 0; i < 3; i++)                                                       // three pieces lock into place
                {
                    float t = 0.002f + i * 0.12f;
                    b.Click(t, 0.6f, 2500f, 0.4f);
                    b.Strike(t, b.U(700f, 900f) * (1f + i * 0.12f), Modes.Plate, 0.1f, 0.7f, 0.25f, 0.2f, 0.3f);
                }
                int[] chord = { 67, 71, 74, 79, 83 };                                            // G major bloom
                for (int i = 0; i < chord.Length; i++) b.Bell(0.38f + i * 0.03f, Midi(chord[i]), 0.25f, 0.3f);
                b.Swell(0.36f, 1.2f, new[] { Midi(55), Midi(62) }, 0.1f, 0.25f);
                b.Reverb(1f, 0.16f, 0.7f, 0.3f, 12f);
                b.CutLows(80f);
            });
            r.Add("secret_found", 2, 2, 0.5f, 1.8f, (b, v) =>
            {
                int[] notes = { 79, 83, 87, 90 };                                                 // G B D# F#: rising augmented, mysterious
                for (int i = 0; i < notes.Length; i++)
                {
                    b.Bell(i * 0.11f, Midi(notes[i]), 0.32f, 0.25f);
                    b.Pluck(i * 0.11f, Midi(notes[i] - 24), 0.12f, 0.996f, 0.4f);
                }
                b.Swell(0.3f, 1.2f, new[] { Midi(91), Midi(95), Midi(98) }, 0.08f, 0.3f, 6f);      // shimmer
                b.Reverb(1.3f, 0.2f, 0.8f, 0.3f, 15f);
                b.CutLows(100f);
            });
            r.Add("door_stone", 2, 2, 0.6f, 2f, (b, v) =>
            {
                float d = b.Jit(1.4f, 0.08f);
                b.StickSlip(0.002f, d, 60f, 40f, 0.4f, b.U(110f, 140f), Modes.Stone, 0.01f, 0.5f);   // grinding
                b.Whoosh(0.002f, d, 300f, 800f, 0.8f, 0.35f, 0.5f, 1.5f, 8f, 0.6f);                  // grit, scraping at 8 Hz
                b.Band(0.002f, d, 90f, 120f, 0.7f, 0.25f, 0.5f, 0f);                                 // rumble
                b.Thump(d, 90f, 55f, 0.12f, 0.45f);                                                  // the slab settles
                b.Grains(d, 0.25f, 24, GrainKind.Noise, 1200f, 4000f, 0.003f, 0.008f, 0.25f, 0.08f);
                b.Reverb(0.9f, 0.2f, 0.7f, 0.4f, 10f);
                b.CutLows(45f);
            });
            r.Add("boss_pull", 2, 2, 0.65f, 1.8f, (b, v) =>
            {
                b.Strike(0.002f, b.U(68f, 74f), Modes.Membrane, 0.35f, 0.8f, 0.7f, 3f, 0.3f);      // big frame drum
                b.Click(0.002f, 2f, 1500f, 0.35f, 5000f);
                b.Horn(0.06f, 0.9f, Midi(50), 0.45f, 0.08f, 0.3f, 3f);                            // D3 horn call
                b.Horn(0.08f, 0.88f, Midi(57), 0.2f, 0.1f, 0.3f, 3f, 5.6f);
                b.Reverb(1.1f, 0.22f, 0.8f, 0.4f, 18f);
                b.CutLows(45f);
            });
            r.Add("raid_warning", 2, 2, 0.62f, 2.2f, (b, v) =>
            {
                b.Horn(0f, 0.5f, Midi(57), 0.45f, 0.06f, 0.12f, 2f);                             // two-tone call: A3 …
                b.Horn(0.5f, 0.7f, Midi(64), 0.45f, 0.06f, 0.25f, 2f);                           // … E4
                for (int i = 0; i < 8; i++)                                                      // drum roll building
                    b.Strike(0.002f + i * 0.09f, b.U(95f, 105f), Modes.Membrane, 0.12f, 0.8f, 0.2f + i * 0.04f, 2f, b.U(0.2f, 0.4f));
                b.Reverb(1f, 0.2f, 0.75f, 0.4f, 15f);
                b.CutLows(50f);
            });
            r.Add("quest_accept", 2, 2, 0.48f, 1.4f, (b, v) =>
            {
                b.Whoosh(0f, 0.16f, 3000f, 5000f, 1.2f, 0.18f, 0.5f, 1.5f, 24f, 0.8f);           // quill scribble
                b.Bell(0.12f, Midi(81), 0.38f, 0.16f);
                b.Bell(0.22f, Midi(88), 0.4f, 0.2f);
                b.Pluck(0.22f, Midi(64), 0.18f, 0.996f, 0.45f);
                SmallRoom(b, 0.12f, 0.5f);
                b.CutLows(150f);
            });
            r.Add("quest_turnin", 2, 2, 0.5f, 2f, (b, v) =>
            {
                int[] cadence = { 79, 83, 86, 91 };                                               // G B D G: a resolved cadence
                for (int i = 0; i < cadence.Length; i++) b.Bell(i * 0.1f, Midi(cadence[i]), 0.34f, 0.22f + i * 0.04f);
                b.Pluck(0.3f, Midi(55), 0.22f, 0.997f, 0.45f);
                b.Pluck(0.3f, Midi(62), 0.18f, 0.997f, 0.45f);
                b.Grains(0.3f, 0.35f, 10, GrainKind.Metal, 3500f, 7500f, 0.01f, 0.03f, 0.1f, 0.12f); // coins
                SmallRoom(b, 0.14f, 0.6f);
                b.CutLows(80f);
            });
            r.Add("portal_whoosh", 2, 2, 0.55f, 1.8f, (b, v) =>
            {
                b.Whoosh(0f, 1.4f, 300f, 2500f, 0.9f, 0.5f, 0.6f, 2f, 6f, 0.6f);                 // swirl, rising
                b.Fm(0.1f, 1.2f, b.U(420f, 480f), 1.5f, 0.5f, 2.5f, 0.15f, 0.6f, 0.6f, 900f);
                b.Swell(0.2f, 1.2f, new[] { 1760f, 2637f, 3520f }, 0.08f, 0.6f, 9f, 0.1f);
                b.Reverb(1.2f, 0.25f, 0.8f, 0.3f, 15f);
                b.CutLows(80f);
            });
        }
    }
}
