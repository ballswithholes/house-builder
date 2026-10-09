// Death and vocal recipes: a short creature vocal at the moment of death (vo_*, by CreatureDef.voice) and the body
// landing a little later (body_fall_*, plus armor_clatter for mail and plate wearers). `death` stays as the party
// defeat cue and the fallback. Vocals are source-filter voices (SynthBuffer.Voice), friction (StickSlip) or
// granular, kept rounded and short so they read as cosy, not gory.
namespace Lanternvale.Game
{
    internal static partial class SfxSynth
    {
        static void AddCreatures(Registry r)
        {
            r.Add("body_fall_light", 1, 2, 0.65f, 0.55f, (b, v) =>
            {
                b.Band(0.002f, 0.2f, 230f, 100f, 0.9f, 0.55f, 0.003f, b.Jit(0.08f, 0.15f));      // body meets ground
                b.Thump(0.002f, 140f, 80f, 0.09f, 0.3f);
                b.Band(0f, 0.12f, 3500f, 2200f, 0.7f, 0.25f, 0.3f, 0f);                            // cloth rustle
                b.Grains(0.005f, 0.12f, 14, GrainKind.Noise, 1500f, 4000f, 0.002f, 0.006f, 0.18f, 0.05f); // dirt
                b.Band(b.U(0.07f, 0.1f), 0.08f, 260f, 140f, 1f, 0.2f, 0.003f, 0.03f);             // a limb settles
                SmallRoom(b, 0.08f);
                b.CutLows(50f);
            });
            r.Add("body_fall_heavy", 1, 2, 0.75f, 0.8f, (b, v) =>
            {
                b.Thump(0.002f, 110f, 55f, b.Jit(0.17f, 0.12f), 0.45f);
                b.Band(0.002f, 0.3f, 170f, 70f, 0.8f, 0.5f, 0.003f, 0.12f);
                b.Click(0.002f, 2.5f, 1200f, 0.3f, 4000f);
                float bounce = b.U(0.07f, 0.1f);
                b.Thump(bounce, 100f, 60f, 0.07f, 0.2f);                                          // secondary bounce
                b.Band(bounce, 0.1f, 200f, 100f, 1f, 0.22f, 0.003f, 0.04f);
                b.Grains(0.005f, 0.3f, 34, GrainKind.Noise, 1200f, 4000f, 0.003f, 0.01f, 0.25f, 0.1f); // debris and dust
                b.Band(0f, 0.18f, 3000f, 1800f, 0.7f, 0.2f, 0.3f, 0f);
                b.Reverb(0.45f, 0.12f, 0.45f, 0.4f, 6f);
                b.CutLows(40f);
            });
            r.Add("armor_clatter", 1, 2, 0.6f, 1f, (b, v) =>
            {
                // plates and buckles hitting the ground one after another (short, dry rings), mail rings jingling
                float t = 0.002f;
                int pieces = b.UInt(5, 9);
                for (int i = 0; i < pieces; i++)
                {
                    float a = 0.5f * (float)System.Math.Pow(0.8, i);
                    b.Strike(t, b.U(800f, 2500f), Modes.Plate, b.U(0.05f, 0.09f), 0.7f, a, 0.15f, b.U(0.1f, 0.45f), 0.03f, null, 0.85f);
                    b.Click(t, 0.5f, 3000f, a);
                    t += b.U(0.02f, 0.07f) * (1f + i * 0.25f);
                }
                b.Grains(0.002f, 0.3f, 50, GrainKind.Metal, 3000f, 8000f, 0.004f, 0.012f, 0.32f, 0.1f); // mail
                SmallRoom(b, 0.08f);
                b.CutLows(200f);
            });

            r.Add("vo_beast_yelp", 1, 2, 0.6f, 0.45f, (b, v) =>
            {
                b.Voice(0.002f, b.Jit(0.25f, 0.1f), b.U(650f, 750f), b.U(280f, 320f), new[] { 800f, 1200f, 2600f }, FormantGains,
                        0.55f, 0.01f, 0.08f, 0.08f, 0.01f, 0.1f, 0f, 0f, VowelU, 0.7f, 5f);           // "aa" → "oo", falling
                SmallRoom(b, 0.08f);
                b.CutLows(150f);
            });
            r.Add("vo_humanoid_grunt", 1, 2, 0.6f, 0.4f, (b, v) =>
            {
                b.Voice(0.002f, b.Jit(0.2f, 0.1f), b.U(150f, 175f), b.U(115f, 135f), VowelUh, FormantGains,
                        0.55f, 0.015f, 0.07f, 0.25f, 0.02f, 0.2f, 0f, 0f, VowelO, 0.6f, 5f);
                b.Band(0.12f, 0.15f, 1500f, 800f, 0.9f, 0.12f, 0.2f, 0f);                         // exhale
                SmallRoom(b, 0.08f);
                b.CutLows(90f);
            });
            r.Add("vo_spirit_fade", 1, 2, 0.5f, 1.4f, (b, v) =>
            {
                float f = b.U(1200f, 1500f);
                b.Swell(0f, 0.6f, new[] { f, f * 1.25f, f * 1.5f, f * 2f }, 0.3f, 0.75f, 6f, 0.15f); // reverse shimmer, rising
                b.Band(0.35f, 0.6f, 1500f, 500f, 1.1f, 0.3f, 0.2f, 0f);                            // exhale
                b.Reverb(1.2f, 0.3f, 0.8f, 0.3f, 20f);
                b.CutLows(150f);
            });
            r.Add("vo_wood_creak", 1, 2, 0.6f, 0.9f, (b, v) =>
            {
                b.StickSlip(0.002f, b.Jit(0.6f, 0.1f), b.U(22f, 30f), b.U(45f, 60f), 0.25f, b.U(280f, 340f), Modes.Wood, 0.02f, 0.5f);
                b.Voice(0.1f, 0.5f, 70f, 55f, new[] { 300f, 700f, 1600f }, FormantGains, 0.12f, 0.15f, 0.2f, 0.4f, 0.03f, 0.6f); // trunk groan
                SmallRoom(b, 0.1f, 0.4f);
                b.CutLows(70f);
            });
            r.Add("vo_stone_crumble", 1, 2, 0.65f, 1f, (b, v) =>
            {
                b.Grains(0.002f, 0.55f, 60, GrainKind.Noise, 1000f, 4000f, 0.003f, 0.01f, 0.3f, 0.2f); // grit pouring
                b.Grains(0.002f, 0.45f, 15, GrainKind.Click, 600f, 1800f, 0.005f, 0.012f, 0.3f, 0.15f); // chunks
                b.Thump(0.002f, 80f, 60f, 0.2f, 0.28f);
                b.Band(0f, 0.5f, 200f, 100f, 0.8f, 0.25f, 0.02f, 0.18f);                          // rumble
                b.Reverb(0.6f, 0.16f, 0.6f, 0.4f, 8f);
                b.CutLows(50f);
            });
            r.Add("vo_dragon_roar", 1, 2, 0.7f, 1.9f, (b, v) =>
            {
                // a big, warm roar: a low rough voice opening from "oh" to "ah", a growling octave above, a breathy blast
                float f = b.U(85f, 100f);
                b.Voice(0.002f, 1.3f, f, f * 0.75f, VowelO, new[] { 1f, 0.7f, 0.3f }, 0.6f, 0.12f, 0.4f, 0.5f, 0.04f, 0.7f, 6f, 0.03f, VowelA, 0.5f, 4f);
                b.Voice(0.03f, 1.2f, f * 2f, f * 1.4f, VowelO, FormantGains, 0.25f, 0.15f, 0.4f, 0.4f, 0.03f, 0.5f, 6.5f, 0.02f, VowelA, 0.5f, 4f);
                b.Band(0.002f, 1.2f, 400f, 1500f, 0.6f, 0.3f, 0.25f, 0f);                          // breath blast
                b.Reverb(1.2f, 0.25f, 0.9f, 0.4f, 25f);
                b.CutLows(55f);
            });
            r.Add("vo_frog_croak", 1, 2, 0.6f, 0.5f, (b, v) =>
            {
                // "rib-bit": two pulse-train bursts (vocal sac pulses ~80/s) through the throat resonances
                float f = b.U(400f, 500f);
                b.StickSlip(0.002f, 0.12f, 80f, 85f, 0.05f, f, new[] { 1f, 2.6f }, 0.008f, 0.9f);
                b.StickSlip(0.18f, 0.1f, 90f, 95f, 0.05f, f * 1.12f, new[] { 1f, 2.6f }, 0.008f, 0.8f);
                SmallRoom(b, 0.08f);
                b.CutLows(120f);
            });
            r.Add("vo_gnoll_yip", 1, 2, 0.6f, 0.75f, (b, v) =>
            {
                // hyena-like giggle: a run of rising-falling yips, each a little lower
                int yips = b.UInt(4, 6);
                float t = 0.002f, f = b.U(650f, 800f);
                for (int i = 0; i < yips; i++)
                {
                    b.Voice(t, b.U(0.06f, 0.08f), f, f * 1.35f, VowelE, FormantGains, 0.5f, 0.008f, 0.03f, 0.1f, 0.02f, 0.15f, 0f, 0f, VowelA, 0.5f, 5f);
                    t += b.U(0.085f, 0.1f);
                    f *= b.U(0.9f, 0.95f);
                }
                SmallRoom(b, 0.08f);
                b.CutLows(200f);
            });
        }
    }
}
