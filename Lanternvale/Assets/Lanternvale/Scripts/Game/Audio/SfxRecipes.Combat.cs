// Combat recipes: weapon contact layers (hit_*), target material layers (mat_*), defence outcomes and swings.
//
// A hit is two voices mixed at runtime: hit_<weapon> (what struck) + mat_<material> (what was struck, a few dB
// under). Each is transient + body + tail: the transient is a high-passed click of 0.4–3 ms, the body the struck
// object's modes (Modes.*) excited by a contact pulse (short = hard, long = soft), and the tail debris grains and a
// small baked room. The ear judges material in 300 Hz – 5 kHz, so that is where the energy goes; the low "weight"
// is a short, quiet layer and every clip is high-passed (CutLows), so no hit is a "kick drum" again (sfxpreview
// check: ≤ 50 % of the energy below 200 Hz, ≥ 25 % in 1–5 kHz).
//
// Variants: every recipe draws its numbers from the buffer's random stream, so variant n (its own seed) is struck
// at a different point, with detuned modes and ±10–20 % decays and corners.
namespace Lanternvale.Game
{
    internal static partial class SfxSynth
    {
        const float T0 = 0.003f; // a few samples of pre-roll so the first transient is not cut

        static void SmallRoom(SynthBuffer b, float wet = 0.1f, float rt60 = 0.3f) => b.Reverb(rt60, wet, 0.32f, 0.45f, 4f);

        static void AddCombat(Registry r)
        {
            // ---------------------------------------------------------------- weapon layers (K = 4)

            r.Add("hit_blade", 0, 4, 0.8f, 0.45f, (b, v) =>
            {
                b.Click(T0, b.U(1f, 2f), 3500f, 0.9f);                                          // edge contact
                b.Band(T0, 0.07f, b.Jit(5500f, 0.1f), b.Jit(2400f, 0.12f), 1f, 0.6f, 0.001f, b.Jit(0.022f, 0.15f)); // slice
                b.Strike(T0, b.U(1100f, 1700f), Modes.FreeBar, b.Jit(0.12f, 0.15f), 0.6f, 0.14f, 0.15f, b.U(0.15f, 0.45f), 0.015f); // blade ring, -18 dB
                b.Band(T0, 0.05f, 300f, 220f, 1.2f, 0.2f, 0.002f, 0.018f);                       // push into the target
                b.Thump(T0, 190f, 120f, 0.022f, 0.15f);
                SmallRoom(b);
                b.CutLows(80f);
            });
            r.Add("hit_axe", 0, 4, 0.82f, 0.45f, (b, v) =>
            {
                b.Click(T0, b.U(1.5f, 3f), 2500f, 1f);
                b.Band(T0, 0.05f, b.Jit(4500f, 0.1f), b.Jit(1800f, 0.1f), 0.9f, 0.7f, 0.001f, 0.018f); // the edge bites
                b.Band(T0, 0.04f, b.Jit(2600f, 0.1f), 1500f, 1.2f, 0.4f, 0.001f, 0.01f);         // fibres crack
                b.Band(T0, 0.08f, 420f, 250f, 1.1f, 0.42f, 0.002f, b.Jit(0.035f, 0.15f));        // chop thump
                b.Strike(T0, b.U(450f, 600f), Modes.Wood, 0.03f, 1.1f, 0.25f, 0.4f, b.U(0.2f, 0.4f)); // haft "chock"
                b.Strike(T0, b.U(800f, 1200f), Modes.FreeBar, b.Jit(0.08f, 0.15f), 0.7f, 0.18f, 0.2f, b.U(0.2f, 0.45f)); // heavy blade
                b.Thump(T0, 150f, 85f, 0.035f, 0.15f);
                SmallRoom(b);
                b.CutLows(70f);
            });
            r.Add("hit_blunt", 0, 4, 0.82f, 0.45f, (b, v) =>
            {
                b.Click(T0, b.U(2f, 3f), 1000f, 1f, 5000f);                                      // 1-3 kHz contact
                b.Band(T0, 0.05f, b.Jit(2000f, 0.1f), 1200f, 1.2f, 0.68f, 0.001f, 0.015f);      // crunch of the dent
                b.Band(T0, 0.1f, b.Jit(300f, 0.1f), 160f, 1f, 0.55f, 0.002f, b.Jit(0.04f, 0.15f)); // dull thump
                b.Thump(T0, 110f, 70f, 0.04f, 0.18f);                                            // weight
                b.Strike(T0, b.U(600f, 900f), Modes.Wood, 0.025f, 1f, 0.4f, 0.5f, b.U(0.2f, 0.45f)); // head knock
                b.Grains(T0, 0.05f, 14, GrainKind.Noise, 1200f, 4000f, 0.002f, 0.006f, 0.3f, 0.015f); // crunch grains
                SmallRoom(b, 0.09f);
                b.CutLows(70f);
            });
            r.Add("hit_dagger", 0, 4, 0.75f, 0.3f, (b, v) =>
            {
                b.Click(T0, b.U(0.6f, 1f), 4000f, 0.85f);                                        // point pricks
                b.Band(T0, 0.035f, b.Jit(4500f, 0.1f), b.Jit(2800f, 0.1f), 1.6f, 0.5f, 0.0008f, 0.01f); // short stab, no long slice
                b.Strike(T0, b.U(2200f, 3000f), Modes.FreeBar, 0.06f, 0.5f, 0.22f, 0.1f, b.U(0.15f, 0.4f)); // little blade ring
                b.Band(T0 + 0.003f, 0.03f, 700f, 450f, 1f, 0.3f, 0.002f, 0.012f);                // hilt meets target
                SmallRoom(b, 0.07f, 0.22f);
                b.CutLows(100f);
            });
            r.Add("hit_fist", 0, 4, 0.75f, 0.3f, (b, v) =>
            {
                b.Click(T0, 1.5f, 2000f, 0.6f);                                                  // knuckles
                b.Band(T0, 0.03f, b.Jit(2200f, 0.12f), 1400f, 1.2f, 0.85f, 0.0008f, 0.012f);    // the smack
                b.Strike(T0, b.U(250f, 350f), Modes.Membrane, 0.025f, 0.8f, 0.35f, 1.2f, b.U(0.2f, 0.45f)); // leather/skin body
                b.Thump(T0, 140f, 95f, 0.025f, 0.1f);
                SmallRoom(b, 0.08f, 0.22f);
                b.CutLows(100f);
            });
            r.Add("hit_bite", 0, 4, 0.75f, 0.35f, (b, v) =>
            {
                float gap = b.U(0.012f, 0.018f);
                b.Click(T0, 0.6f, 3000f, 0.6f);                                                  // upper teeth
                b.Mode(T0, b.U(2300f, 2900f), 0.01f, 0.22f, 0.05f);
                b.Click(T0 + gap, 0.6f, 3000f, 0.5f);                                            // lower teeth
                b.Mode(T0 + gap, b.U(2300f, 2900f), 0.01f, 0.18f, 0.05f);
                b.Band(T0 + 0.005f, 0.06f, 2000f, 1000f, 1.4f, 0.45f, 0.002f, 0.02f);            // wet
                b.Voice(T0, 0.13f, b.U(160f, 240f), b.U(130f, 190f), new[] { 400f, 1000f, 2400f }, new[] { 1f, 0.6f, 0.25f },
                        0.22f, 0.01f, 0.04f, 0.3f, 0.03f, 0.6f);                                 // growl grain
                SmallRoom(b, 0.08f);
                b.CutLows(80f);
            });
            r.Add("hit_claw", 0, 4, 0.75f, 0.32f, (b, v) =>
            {
                float t = T0;
                for (int k = 0; k < 3; k++)
                {
                    b.Band(t, 0.022f, b.Jit(5000f, 0.1f), b.Jit(2600f, 0.1f), 2.2f, 0.6f * (1f - 0.15f * k), 0.0008f, 0.008f); // scratch
                    b.Click(t, 0.5f, 4000f, 0.22f);
                    t += b.Jit(0.028f, 0.15f);
                }
                b.Band(T0, 0.06f, 300f, 200f, 1f, 0.22f, 0.003f, 0.02f);                         // light thump
                SmallRoom(b, 0.07f);
                b.CutLows(90f);
            });
            r.Add("hit_slam", 0, 4, 0.85f, 0.8f, (b, v) =>
            {
                b.Click(T0, 3f, 1500f, 0.8f, 6000f);                                             // crack
                b.Thump(T0, b.U(85f, 95f), 55f, 0.14f, 0.26f);                                   // big low thud
                b.Band(T0, 0.2f, 220f, 100f, 0.8f, 0.3f, 0.003f, 0.07f);
                b.Grains(T0 + 0.005f, 0.25f, 56, GrainKind.Noise, 1200f, 4500f, 0.003f, 0.012f, 0.5f, 0.08f); // debris
                b.Grains(T0 + 0.01f, 0.2f, 16, GrainKind.Click, 900f, 2500f, 0.004f, 0.01f, 0.25f, 0.06f);   // chunks
                b.Reverb(0.5f, 0.14f, 0.5f, 0.4f, 8f);
                b.CutLows(50f);
            });
            r.Add("hit_arrow", 0, 4, 0.75f, 0.4f, (b, v) =>
            {
                b.Click(T0, 0.7f, 3500f, 0.8f);                                                  // point
                b.Band(T0, 0.03f, b.Jit(3200f, 0.1f), 2000f, 1.3f, 0.45f, 0.0006f, 0.008f);      // tip bites in
                b.Strike(T0, b.U(1000f, 1500f), Modes.FreeBar, 0.02f, 0.8f, 0.45f, 0.25f, b.U(0.15f, 0.4f)); // shaft knock
                b.Thump(T0, 220f, 140f, 0.02f, 0.15f);
                b.Whoosh(T0 + 0.004f, 0.12f, 900f, 1500f, 2f, 0.16f, 0.12f, 1f, b.U(55f, 75f), 0.9f); // shaft quiver
                SmallRoom(b, 0.08f);
                b.CutLows(100f);
            });
            r.Add("hit_bolt", 0, 4, 0.8f, 0.42f, (b, v) =>
            {
                b.Click(T0, 1f, 2500f, 0.9f);
                b.Band(T0, 0.04f, b.Jit(2500f, 0.1f), 1400f, 1.1f, 0.55f, 0.001f, 0.012f);       // heavier bite
                b.Strike(T0, b.U(800f, 1200f), Modes.FreeBar, 0.03f, 0.8f, 0.45f, 0.3f, b.U(0.15f, 0.4f)); // short, thick shaft
                b.Thump(T0, 180f, 100f, 0.035f, 0.22f);
                b.Grains(T0 + 0.003f, 0.05f, 10, GrainKind.Click, 2000f, 5000f, 0.001f, 0.004f, 0.18f, 0.02f); // splinters
                SmallRoom(b, 0.09f);
                b.CutLows(80f);
            });
            r.Add("hit_bullet", 0, 4, 0.78f, 0.35f, (b, v) =>
            {
                b.Click(T0, 0.4f, 5000f, 0.95f);                                                 // impact crack
                b.Band(T0, 0.03f, b.Jit(2600f, 0.1f), 1300f, 0.9f, 0.7f, 0.0005f, 0.01f);        // the "thwack"
                b.Thump(T0, 160f, 90f, 0.03f, 0.1f);                                             // a soft "thup"
                b.Grains(T0, 0.06f, 16, GrainKind.Noise, 2000f, 6000f, 0.001f, 0.004f, 0.35f, 0.02f); // grit spray
                SmallRoom(b, 0.08f);
                b.CutLows(90f);
            });
            // the old generic ids, now layered: a generic armed blow and a heavy crit (also the crit sweetener)
            r.Add("hit_physical", 0, 4, 0.8f, 0.45f, (b, v) =>
            {
                b.Click(T0, b.U(1f, 2f), 2500f, 0.9f);
                b.Band(T0, 0.06f, b.Jit(4500f, 0.12f), b.Jit(2000f, 0.12f), 1f, 0.5f, 0.001f, 0.02f);
                b.Band(T0, 0.04f, b.Jit(2500f, 0.1f), 1500f, 1.2f, 0.4f, 0.001f, 0.012f);
                b.Band(T0, 0.08f, 320f, 180f, 1f, 0.4f, 0.002f, 0.03f);
                b.Strike(T0, b.U(600f, 900f), Modes.Membrane, 0.03f, 0.8f, 0.3f, 0.6f, b.U(0.2f, 0.4f));
                b.Thump(T0, 140f, 85f, 0.03f, 0.18f);
                SmallRoom(b);
                b.CutLows(70f);
            });
            r.Add("hit_crit", 0, 4, 0.88f, 0.75f, (b, v) =>
            {
                b.Click(T0, 2f, 2000f, 1f);
                b.Band(T0, 0.09f, b.Jit(5000f, 0.1f), 1500f, 0.9f, 0.6f, 0.001f, 0.03f);
                b.Band(T0, 0.12f, 320f, 150f, 1f, 0.6f, 0.002f, 0.045f);
                b.Thump(T0, 150f, 70f, 0.05f, 0.3f);
                b.Strike(T0 + 0.002f, b.U(1150f, 1450f), Modes.FreeBar, 0.18f, 0.55f, 0.22f, 0.15f, b.U(0.15f, 0.35f)); // ring
                b.Grains(T0, 0.08f, 14, GrainKind.Noise, 1500f, 5000f, 0.002f, 0.006f, 0.22f, 0.03f);
                b.Saturate(1.8f);                                                                 // density
                b.Reverb(0.4f, 0.13f, 0.45f, 0.4f, 6f);
                b.CutLows(60f);
            });

            // ---------------------------------------------------------------- target materials (K = 4)

            r.Add("mat_plate", 0, 4, 0.56f, 0.5f, (b, v) =>
            {
                b.Click(T0, 0.8f, 3000f, 0.6f);
                b.Strike(T0, b.U(420f, 540f), Modes.Plate, b.Jit(0.16f, 0.1f), 0.6f, 0.5f, 0.2f, b.U(0.2f, 0.35f), 0.015f, null, 0.92f);
                b.Band(T0, 0.06f, 380f, 220f, 1f, 0.35f, 0.002f, 0.02f);                         // the body behind the plate
                b.Strike(T0, b.U(2000f, 3500f), Modes.FreeBar, 0.1f, 0.5f, 0.3f, 0.12f, b.U(0.1f, 0.4f)); // bright edge ring
                SmallRoom(b, 0.1f);
                b.CutLows(150f);
            });
            r.Add("mat_mail", 0, 4, 0.7f, 0.45f, (b, v) =>
            {
                b.Grains(T0, b.U(0.04f, 0.09f), b.UInt(25, 45), GrainKind.Metal, 3000f, 8000f, 0.004f, 0.015f, 0.3f, 0.03f);
                b.Band(T0, 0.05f, 420f, 250f, 1f, 0.32f, 0.002f, 0.02f);                         // light thud
                b.Click(T0, 0.6f, 3000f, 0.25f);
                SmallRoom(b, 0.1f);
                b.CutLows(110f);
            });
            r.Add("mat_leather", 0, 4, 0.7f, 0.35f, (b, v) =>
            {
                // a broadband slap with no ringing modes (that is what tells it from wood), a snap and a creak
                b.Click(T0, 1f, 2500f, 0.4f);
                b.Band(T0, 0.04f, b.U(1300f, 1800f), 1000f, 1f, 0.85f, 0.001f, 0.014f);          // slap of the hide
                b.Band(T0 + 0.005f, 0.03f, 2800f, 2100f, 1.2f, 0.55f, 0.001f, 0.006f);           // snap
                b.Strike(T0, b.U(220f, 320f), Modes.Membrane, 0.02f, 0.8f, 0.22f, 1.2f, b.U(0.2f, 0.45f)); // padded body
                b.Band(T0 + 0.012f, 0.08f, b.Jit(420f, 0.1f), 470f, 8f, 0.12f, 0.01f, 0.03f);    // creak of the straps
                b.Saturate(3f);                                                                  // denser body, crest ~16 dB like its peers
                SmallRoom(b, 0.08f);
                b.CutLows(100f);
            });
            r.Add("mat_cloth", 0, 4, 0.65f, 0.3f, (b, v) =>
            {
                b.Band(T0, 0.07f, 300f, 140f, 0.9f, 0.55f, 0.003f, 0.025f);                      // soft thud
                b.Whoosh(T0, b.Jit(0.08f, 0.15f), 1200f, 2600f, 0.8f, 0.45f, 0.25f, 1.5f, b.U(35f, 50f), 0.75f); // fabric flap
                b.Grains(T0, 0.05f, 14, GrainKind.Noise, 2000f, 5000f, 0.001f, 0.003f, 0.1f, 0f); // fibres
                b.Filter(Biquad.Kind.LowPass, 6000f, 0.7f);
                SmallRoom(b, 0.07f, 0.22f);
                b.CutLows(80f);
            });
            r.Add("mat_flesh", 0, 4, 0.68f, 0.32f, (b, v) =>
            {
                b.Band(T0, 0.08f, 260f, 140f, 1f, 0.45f, 0.002f, 0.028f);                        // thud
                b.Thump(T0, 120f, 80f, 0.03f, 0.1f);
                b.Band(T0, 0.025f, b.Jit(2200f, 0.12f), 1300f, 1.1f, 0.7f, 0.0006f, 0.006f);     // slap
                b.Band(T0 + 0.01f, 0.07f, 900f, 450f, 3f, 0.13f, 0.01f, 0.025f);                 // squish
                b.Saturate(3f);                                                                  // denser body, crest ~16 dB like its peers
                SmallRoom(b, 0.08f);
                b.CutLows(80f);
            });
            r.Add("mat_fur", 0, 4, 0.8f, 0.42f, (b, v) =>
            {
                b.Band(T0, 0.1f, 320f, 160f, 1f, 0.7f, 0.003f, b.Jit(0.035f, 0.15f));          // the body under the pelt gives
                b.Thump(T0, 150f, 95f, 0.03f, 0.18f);
                b.Band(T0, 0.04f, b.Jit(1500f, 0.12f), 900f, 0.9f, 0.8f, 0.002f, 0.012f);       // muffled slap through the fur
                b.Grains(T0, b.Jit(0.06f, 0.15f), 30, GrainKind.Noise, 1500f, 4500f, 0.0008f, 0.003f, 0.1f, 0.03f); // hairs, short
                b.Saturate(2.5f);
                SmallRoom(b, 0.07f);
                b.Filter(Biquad.Kind.LowPass, 5000f, 0.7f);
                b.CutLows(80f);
            });
            r.Add("mat_chitin", 0, 4, 0.85f, 0.3f, (b, v) =>
            {
                int clicks = b.UInt(2, 3);
                for (int k = 0; k < clicks; k++)
                {
                    float t = T0 + k * b.U(0.003f, 0.006f);
                    b.Click(t, 0.4f, 2500f, 0.55f, 6000f);
                    b.Mode(t, b.U(2000f, 4000f), 0.006f, 0.3f, 0.05f);
                }
                b.Strike(T0, b.U(600f, 900f), Modes.Membrane, 0.025f, 0.9f, 0.45f, 0.3f, b.U(0.2f, 0.45f)); // hollow shell
                SmallRoom(b, 0.08f);
                b.CutLows(120f);
            });
            r.Add("mat_bone", 0, 4, 0.7f, 0.35f, (b, v) =>
            {
                b.Click(T0, 0.6f, 2500f, 0.4f);
                b.Strike(T0, b.U(500f, 900f), Modes.Bone, b.Jit(0.04f, 0.15f), 0.6f, 0.55f, 0.2f, b.U(0.1f, 0.4f));
                b.Grains(T0 + 0.01f, 0.1f, 10, GrainKind.Click, 1500f, 3200f, 0.003f, 0.008f, 0.22f, 0.04f); // rattle
                SmallRoom(b, 0.09f);
                b.CutLows(110f);
            });
            r.Add("mat_wood", 0, 4, 0.72f, 0.45f, (b, v) =>
            {
                b.Click(T0, 1f, 2000f, 0.5f);
                b.Band(T0, 0.03f, b.Jit(2500f, 0.1f), 1500f, 1.2f, 0.4f, 0.0008f, 0.006f);       // fibres crack
                b.Strike(T0, b.U(900f, 1400f), Modes.Wood, 0.015f, 1f, 0.5f, 0.3f, b.U(0.15f, 0.45f)); // local knock (stiff spot)
                b.Strike(T0, b.U(220f, 380f), Modes.Wood, b.Jit(0.05f, 0.15f), 1.1f, 0.45f, 0.6f, b.U(0.15f, 0.45f)); // trunk body
                b.Grains(T0 + 0.004f, 0.06f, 14, GrainKind.Click, 2000f, 6000f, 0.001f, 0.004f, 0.25f, 0.02f); // splinters
                SmallRoom(b, 0.09f);
                b.CutLows(100f);
            });
            r.Add("mat_stone", 0, 4, 0.72f, 0.4f, (b, v) =>
            {
                b.Click(T0, 0.6f, 2500f, 0.7f);
                b.Strike(T0, b.U(1200f, 2000f), Modes.Stone, 0.01f, 0.9f, 0.4f, 0.15f, b.U(0.15f, 0.45f)); // clack
                b.Grains(T0, 0.12f, 30, GrainKind.Noise, 1500f, 4000f, 0.002f, 0.008f, 0.28f, 0.04f);     // grit
                b.Thump(T0, 95f, 75f, 0.03f, 0.22f);
                SmallRoom(b, 0.1f);
                b.CutLows(70f);
            });
            r.Add("mat_ether", 0, 4, 0.48f, 0.7f, (b, v) =>
            {
                b.Thump(T0, 230f, 110f, 0.06f, 0.28f);                                           // soft whoomp
                b.Band(T0, 0.25f, 1500f, 600f, 1.2f, 0.3f, 0.02f, 0.08f);                        // breath
                float f = b.U(2200f, 2600f);
                b.Swell(T0, b.Jit(0.3f, 0.1f), new[] { f, f * 1.5f, f * 2.02f }, 0.2f, 0.8f, 9f); // reverse-swell shimmer
                b.Reverb(0.6f, 0.2f, 0.6f, 0.3f, 10f);
                b.CutLows(90f);
            });
            r.Add("mat_ice", 0, 4, 0.5f, 0.6f, (b, v) =>
            {
                b.Click(T0, 0.4f, 4000f, 0.6f);
                b.Click(T0 + b.U(0.006f, 0.014f), 0.4f, 4000f, 0.35f);
                b.Strike(T0, b.U(1500f, 2500f), Modes.Glass, b.Jit(0.09f, 0.15f), 0.6f, 0.4f, 0.1f, b.U(0.1f, 0.4f));
                b.Grains(T0 + 0.008f, 0.15f, b.UInt(30, 60), GrainKind.Metal, 3000f, 9000f, 0.002f, 0.008f, 0.2f, 0.05f); // shatter
                SmallRoom(b, 0.1f);
                b.CutLows(150f);
            });
            r.Add("mat_scale", 0, 4, 0.7f, 0.35f, (b, v) =>
            {
                b.Click(T0, 0.8f, 2000f, 0.35f);
                b.Grains(T0, 0.06f, 14, GrainKind.Click, 1500f, 3800f, 0.003f, 0.008f, 0.42f, 0.02f); // hard scales clacking
                b.Band(T0, 0.06f, 350f, 220f, 1f, 0.42f, 0.002f, 0.022f);                         // hide beneath
                b.Strike(T0, b.U(900f, 1300f), Modes.Membrane, 0.015f, 0.9f, 0.22f, 0.3f, b.U(0.2f, 0.4f));
                SmallRoom(b, 0.08f);
                b.CutLows(90f);
            });
            r.Add("mat_wet", 0, 4, 0.68f, 0.45f, (b, v) =>
            {
                b.Band(T0, 0.1f, 1800f, 500f, 1f, 0.5f, 0.002f, 0.03f);                          // splash body
                b.Band(T0, 0.05f, 300f, 180f, 1f, 0.28f, 0.002f, 0.02f);
                b.Grains(T0 + 0.005f, 0.2f, b.UInt(10, 18), GrainKind.Bubble, 500f, 2200f, 0.004f, 0.012f, 0.25f, 0.06f);
                b.Grains(T0, 0.12f, 25, GrainKind.Noise, 3000f, 8000f, 0.001f, 0.003f, 0.1f, 0.05f); // droplets
                SmallRoom(b, 0.08f);
                b.CutLows(90f);
            });

            // ---------------------------------------------------------------- defence outcomes (K = 2)

            r.Add("parry", 0, 2, 0.75f, 1.8f, (b, v) =>
            {
                float pos = b.U(0.1f, 0.3f);
                b.Click(T0, 0.5f, 3500f, 0.6f);
                b.Strike(T0, b.Jit(1100f, 0.06f), Modes.FreeBar, 0.5f, 0.55f, 0.42f, 0.12f, pos);            // blade a
                b.Strike(T0 + b.U(0.005f, 0.01f), b.Jit(1450f, 0.06f), Modes.FreeBar, 0.45f, 0.55f, 0.36f, 0.12f, pos + 0.1f); // blade b
                b.Band(T0 + 0.004f, 0.09f, 4000f, 2000f, 2.5f, 0.25f, 0.003f, 0.035f);                   // scrape
                b.Reverb(0.5f, 0.14f, 0.5f, 0.35f, 6f);
                b.CutLows(200f);
            });
            r.Add("block_wood", 0, 2, 0.78f, 0.5f, (b, v) =>
            {
                b.Click(T0, 1.2f, 1500f, 0.35f);
                b.Strike(T0, b.U(140f, 220f), Modes.Wood, b.Jit(0.07f, 0.12f), 1f, 0.6f, 0.8f, b.U(0.2f, 0.45f));
                b.Mode(T0, b.Jit(450f, 0.06f), 0.04f, 0.32f, 0.5f);                               // plank
                b.Mode(T0, b.Jit(2500f, 0.05f), 0.04f, 0.1f, 0.1f);                               // iron rim tick, -12 dB
                b.Thump(T0, 120f, 80f, 0.04f, 0.22f);
                SmallRoom(b, 0.1f);
                b.CutLows(60f);
            });
            r.Add("block_metal", 0, 2, 0.78f, 1.2f, (b, v) =>
            {
                b.Click(T0, 1f, 2500f, 0.5f);
                b.Strike(T0, b.U(220f, 350f), Modes.Plate, b.Jit(0.32f, 0.1f), 0.7f, 0.7f, 0.3f, b.U(0.1f, 0.4f), 0.02f, null, 0.85f);
                b.Strike(T0, b.U(1600f, 2000f), Modes.FreeBar, 0.2f, 0.5f, 0.15f, 0.15f, b.U(0.1f, 0.4f)); // rim
                b.Thump(T0, 120f, 80f, 0.05f, 0.3f);
                b.Reverb(0.4f, 0.12f, 0.4f, 0.4f, 5f);
                b.CutLows(60f);
            });
            r.Add("dodge", 0, 2, 0.6f, 0.3f, (b, v) =>
            {
                b.Whoosh(0f, b.Jit(0.18f, 0.1f), 600f, 1800f, 1f, 0.5f, 0.45f, 2.5f);            // the body sways away
                b.Whoosh(0.02f, 0.14f, 300f, 900f, 0.8f, 0.4f, 0.4f, 2f, b.U(16f, 22f), 0.85f);   // cloth flap
                b.Band(0.12f, 0.05f, 2500f, 1200f, 1.2f, 0.35f, 0.002f, 0.014f);                 // foot scuff
                b.Grains(0.12f, 0.04f, 8, GrainKind.Noise, 1500f, 4000f, 0.001f, 0.003f, 0.15f, 0.01f);
                b.CutLows(150f);
            });
            r.Add("miss", 0, 2, 0.5f, 0.25f, (b, v) =>
            {
                b.Whoosh(0f, b.Jit(0.17f, 0.1f), 1500f, 4000f, 1.6f, 0.5f, 0.45f, 3f);           // a thin whiff past the ear
                b.CutLows(400f);
            });
            r.Add("resist", 0, 2, 0.55f, 0.45f, (b, v) =>
            {
                b.Whoosh(0f, 0.3f, 3500f, 5500f, 0.6f, 0.4f, 0.15f, 1.5f, 40f, 0.9f);              // fizzle (40 Hz AM)
                b.Tone(0f, 0.25f, b.Jit(1200f, 0.05f), 600f, 0.22f, 0.005f, 0.08f);              // magic drains away
                b.CutLows(200f);
            });
            r.Add("immune", 0, 2, 0.55f, 0.5f, (b, v) =>
            {
                float f = b.Jit(1800f, 0.03f);
                b.Mode(T0, f, 0.06f, 0.5f, 0.4f);                                                // dull tink
                b.Mode(T0, f * 2.41f, 0.03f, 0.15f, 0.4f);
                b.Swell(T0, 0.25f, new[] { f * 0.5f, f * 0.75f }, 0.08f, 0.2f);                   // warded dome
                b.Filter(Biquad.Kind.LowPass, 4000f, 0.7f);
                SmallRoom(b, 0.12f, 0.4f);
                b.CutLows(200f);
            });
            r.Add("absorb", 0, 2, 0.6f, 1f, (b, v) =>
            {
                b.Strike(T0, b.U(1600f, 2000f), Modes.Glass, 0.2f, 0.5f, 0.32f, 0.3f, b.U(0.15f, 0.4f)); // glassy shield
                b.Whoosh(0f, 0.25f, 900f, 2400f, 0.9f, 0.18f, 0.3f, 2f);
                b.Thump(T0, 160f, 100f, 0.05f, 0.25f);
                b.Reverb(0.6f, 0.15f, 0.5f, 0.3f, 8f);
                b.CutLows(80f);
            });

            // ---------------------------------------------------------------- swings (K = 4)
            // the whoosh peaks a little before UnitView.AttackHitTime (0.22 s)

            r.Add("swing_light", 0, 4, 0.5f, 0.2f, (b, v) =>
            {
                b.Whoosh(0f, b.Jit(0.15f, 0.1f), b.Jit(1500f, 0.1f), b.Jit(4000f, 0.1f), 1.1f, 0.6f, b.U(0.55f, 0.65f), 3f);
                b.CutLows(300f);
            });
            r.Add("swing", 0, 4, 0.55f, 0.3f, (b, v) =>
            {
                b.Whoosh(0f, b.Jit(0.22f, 0.08f), b.Jit(700f, 0.1f), b.Jit(3000f, 0.1f), 1f, 0.6f, b.U(0.55f, 0.65f), 3f);
                b.CutLows(200f);
            });
            r.Add("swing_heavy", 0, 4, 0.6f, 0.4f, (b, v) =>
            {
                float d = b.Jit(0.32f, 0.08f), p = b.U(0.58f, 0.66f);
                b.Whoosh(0f, d, b.Jit(300f, 0.1f), b.Jit(1600f, 0.1f), 0.9f, 0.6f, p, 3f);
                b.Whoosh(0f, d, 70f, 110f, 0.7f, 0.35f, p, 3f);                                  // low air rumble
                b.CutLows(55f);
            });
        }
    }
}
