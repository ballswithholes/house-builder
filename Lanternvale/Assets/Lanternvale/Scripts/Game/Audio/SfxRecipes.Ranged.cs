// Ranged recipes: releases (bow, crossbow, a cosy "pop" gun, throws, wand) and the arrow's fly-by.
// Releases are played at the moment the projectile leaves (UnitView.ShootReleaseTime), not at draw start.
namespace Lanternvale.Game
{
    internal static partial class SfxSynth
    {
        static void AddRanged(Registry r)
        {
            // keeps the old id: string twang (Karplus-Strong, fast damping) + limb thump + string slap + the arrow's "thwip"
            r.Add("bow", 0, 3, 0.6f, 0.5f, (b, v) =>
            {
                b.Click(0.001f, 0.8f, 2500f, 0.45f);                                              // string slap on the bracer
                b.Pluck(0.001f, b.U(90f, 140f), 0.6f, b.U(0.91f, 0.93f), 0.8f);                    // ~ -70 dB/s: a short twang
                b.Strike(0.001f, b.U(230f, 280f), Modes.Wood, 0.03f, 1f, 0.35f, 0.8f, b.U(0.2f, 0.4f)); // limbs
                b.Whoosh(0.005f, 0.09f, 2500f, 5000f, 1.5f, 0.25f, 0.3f, 2f);                     // the arrow leaves
                b.CutLows(70f);
            });
            r.Add("xbow_release", 1, 2, 0.65f, 0.4f, (b, v) =>
            {
                // dry and mechanical: two latch clicks, a ringing spring, a stiff short twang and a knock of the stock
                float gap = b.U(0.007f, 0.009f);
                b.Click(0f, 0.4f, 3000f, 0.8f);                                                   // trigger sear
                b.Strike(0f, b.U(2800f, 3400f), Modes.ClampedBar, 0.04f, 0.5f, 0.3f, 0.05f, 0.25f); // latch spring ring
                b.Click(gap, 0.4f, 2500f, 0.7f);                                                  // nut releases
                b.Mode(gap, b.U(1800f, 2200f), 0.02f, 0.25f, 0.1f);
                b.Strike(gap, b.U(350f, 450f), Modes.Wood, 0.03f, 1f, 0.3f, 0.5f, b.U(0.2f, 0.4f)); // stock knock
                b.Pluck(gap, b.U(150f, 190f), 0.4f, 0.9f, 0.85f);                                  // short, stiff twang
                b.Whoosh(gap + 0.004f, 0.08f, 1800f, 3800f, 1.4f, 0.2f, 0.3f, 2f);
                b.CutLows(120f);
            });
            r.Add("gun_fire", 1, 2, 0.62f, 1.2f, (b, v) =>
            {
                // a cosy "pop", not a gunshot: pan flash, crack, a round boom and a long, soft room tail
                b.Band(0f, 0.02f, 3000f, 2000f, 0.8f, 0.22f, 0.001f, 0.006f);                    // flintlock pan flash
                const float t = 0.02f;
                b.Click(t, 0.5f, 3000f, 0.85f);                                                   // crack
                b.Band(t, 0.15f, 200f, 80f, 0.8f, 0.6f, 0.001f, 0.06f);                          // boom
                b.Band(t, 0.25f, 3500f, 900f, 0.5f, 0.5f, 0.001f, b.Jit(0.08f, 0.15f));          // muzzle blast
                b.Thump(t, 140f, 60f, 0.06f, 0.35f);
                b.Saturate(1.4f);
                b.Reverb(0.9f, 0.22f, 0.7f, 0.4f, 12f);
                b.CutLows(55f);
            });
            r.Add("throw_release", 1, 2, 0.5f, 0.25f, (b, v) =>
            {
                b.Whoosh(0f, b.Jit(0.18f, 0.1f), 900f, 2500f, 1f, 0.5f, 0.5f, 2.5f, b.U(15f, 25f), 0.7f); // spinning
                b.CutLows(150f);
            });
            r.Add("arrow_flight", 1, 2, 0.45f, 0.6f, (b, v) =>
            {
                // 0.55 s pass-by; play with pitch = 0.55 / flightSeconds to fit a flight
                b.Doppler(0f, 0.55f, b.U(1800f, 2400f), 1.6f, b.U(55f, 70f), b.U(1.5f, 2.5f), 0.3f, 0.6f, b.U(40f, 60f), 0.4f);
                b.CutLows(300f);
            });
            r.Add("wand_zap", 1, 2, 0.5f, 0.35f, (b, v) =>
            {
                float f = b.U(1300f, 1600f);
                b.Fm(0f, 0.16f, f, 1.41f, 3f, 0.3f, 0.5f, 0.002f, 0.05f, f * 1.6f);               // a bright rising blip
                b.Grains(0.01f, 0.14f, 14, GrainKind.Metal, 6000f, 10000f, 0.004f, 0.012f, 0.25f, 0.05f); // sparkle
                b.Click(0f, 0.5f, 3000f, 0.25f);
                SmallRoom(b, 0.12f, 0.4f);
                b.CutLows(200f);
            });
        }
    }
}
