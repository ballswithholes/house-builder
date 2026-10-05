// Biped animation: gait + IK legs, FK arm swing with carry styles, keyframed one-shots solved with arm IK, hit,
// dodge, knockback, kneel/lie/die/revive, floaters (no legs) and fliers (the owl). See UnitAnimator.cs.
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class UnitAnimator
    {
        // ================================================================== action keyframes
        // Hand targets are relative to the CHEST bone position in a body-aligned frame (x right, y up, z forward),
        // in metres for a 1.75 m human (scaled by arm length); Dir = where the item in the hand points (hand +Z);
        // Pole = where the elbow points.

        // Lunge / Crouch / Step move the hips and the front foot and are scaled by the LEG length (a 2.6 m treant on
        // short root legs must not fold into the ground); Leap / Hop move the whole body, feet included (a jump), and
        // are scaled by the body height.
        struct AKey
        {
            public float T;
            public Vector3 R, RDir, RPole, L, LDir, LPole;
            public float ChestYaw, ChestPitch, Spine, Head, Lunge, Crouch, Step, Leap, Hop;
        }

        static AKey K(float t, Vector3 r, Vector3 rd, Vector3 rp, Vector3 l, Vector3 ld, Vector3 lp,
                      float cy = 0f, float cp = 0f, float sp = 0f, float hd = 0f, float lunge = 0f, float crouch = 0f, float step = 0f,
                      float leap = 0f, float hop = 0f) =>
            new AKey { T = t, R = r, RDir = rd, RPole = rp, L = l, LDir = ld, LPole = lp, ChestYaw = cy, ChestPitch = cp, Spine = sp, Head = hd,
                       Lunge = lunge, Crouch = crouch, Step = step, Leap = leap, Hop = hop };

        /// <summary>Leg (hip joint) height of the 1.75 m reference human the action keys are authored for.</summary>
        const float RefLeg = 1.75f * 0.49f;

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static readonly Vector3 PoleR = V(1f, -1f, -0.2f), PoleL = V(-1f, -1f, -0.2f);
        static readonly Vector3 ShieldL = V(-0.24f, -0.06f, 0.24f), ShieldDir = V(0f, 1f, 0.3f);

        static readonly AKey[] SwordKeys =
        {
            K(0.00f, V(0.26f, -0.18f, 0.18f), V(0f, -0.4f, 1f), PoleR, ShieldL, ShieldDir, PoleL),
            K(0.12f, V(0.30f, 0.48f, -0.12f), V(0.1f, 0.25f, -1f), V(1f, 0.3f, -0.6f), V(-0.22f, 0.05f, 0.3f), ShieldDir, PoleL, 25f, -6f, -2f, 0f, -0.04f, 0.03f, 0f),
            K(0.22f, V(-0.08f, -0.12f, 0.55f), V(-0.45f, -0.55f, 0.7f), V(1f, -0.5f, 0f), V(-0.3f, -0.12f, 0.15f), V(0f, 1f, 0f), PoleL, -25f, 12f, 6f, 0f, 0.36f, 0.07f, 0.28f),
            K(0.32f, V(-0.18f, -0.22f, 0.42f), V(-0.75f, -0.55f, 0.35f), V(1f, -0.5f, 0f), V(-0.3f, -0.12f, 0.15f), V(0f, 1f, 0f), PoleL, -30f, 10f, 5f, 0f, 0.34f, 0.06f, 0.28f),
        };

        static readonly AKey[] GreatKeys =
        {
            K(0.00f, V(0.15f, 0.08f, 0.28f), V(0.1f, 0.35f, -0.95f), PoleR, V(0.05f, 0.04f, 0.3f), V(0.1f, 0.35f, -0.95f), PoleL),
            K(0.13f, V(0.42f, 0.15f, -0.08f), V(0.6f, 0.2f, -0.8f), V(1f, -1f, -0.5f), V(0.3f, 0.12f, 0.0f), V(0.6f, 0.2f, -0.8f), V(-0.5f, -1f, 0f), 50f, -4f, 0f, 0f, -0.05f, 0.05f, 0f),
            K(0.22f, V(-0.15f, 0.0f, 0.55f), V(-0.95f, 0.0f, 0.3f), V(1f, -1f, 0f), V(-0.02f, -0.02f, 0.5f), V(-0.95f, 0.0f, 0.3f), V(-1f, -1f, 0f), -40f, 8f, 4f, 0f, 0.36f, 0.08f, 0.3f),
            K(0.32f, V(-0.35f, -0.05f, 0.35f), V(-0.75f, -0.1f, -0.6f), V(1f, -1f, 0f), V(-0.25f, -0.06f, 0.4f), V(-0.75f, -0.1f, -0.6f), V(-1f, -1f, 0f), -55f, 6f, 3f, 0f, 0.34f, 0.06f, 0.3f),
        };

        static readonly AKey[] DaggerKeys =
        {
            K(0.00f, V(0.22f, -0.15f, 0.2f), V(0f, 0f, 1f), PoleR, V(-0.22f, -0.12f, 0.22f), V(0f, 0f, 1f), PoleL),
            K(0.10f, V(0.2f, -0.02f, -0.05f), V(0f, 0.2f, 1f), V(1f, -0.5f, -1f), V(-0.2f, -0.05f, 0.2f), V(0f, 0f, 1f), PoleL, 15f, 4f, 0f, 0f, -0.03f, 0.05f, 0f),
            K(0.20f, V(0.02f, 0.0f, 0.62f), V(0f, -0.05f, 1f), V(1f, -1f, 0f), V(-0.2f, -0.05f, 0.12f), V(0f, 0f, 1f), PoleL, -18f, 10f, 4f, 0f, 0.34f, 0.08f, 0.25f),
            K(0.28f, V(0.2f, -0.05f, 0.15f), V(0f, 0f, 1f), PoleR, V(-0.18f, -0.02f, 0.0f), V(0f, 0.2f, 1f), V(-1f, -0.5f, -1f), -5f, 6f, 2f, 0f, 0.32f, 0.07f, 0.25f),
            K(0.36f, V(0.22f, -0.1f, 0.12f), V(0f, 0f, 1f), PoleR, V(-0.02f, 0.02f, 0.62f), V(0f, -0.05f, 1f), V(-1f, -1f, 0f), 18f, 10f, 4f, 0f, 0.38f, 0.08f, 0.25f),
            K(0.45f, V(0.22f, -0.12f, 0.15f), V(0f, 0f, 1f), PoleR, V(-0.2f, -0.1f, 0.2f), V(0f, 0f, 1f), PoleL, 0f, 4f, 0f, 0f, 0.3f, 0.04f, 0.25f),
        };

        static readonly AKey[] MaceKeys =
        {
            K(0.00f, V(0.24f, -0.18f, 0.18f), V(0f, 0.6f, 0.8f), PoleR, ShieldL, ShieldDir, PoleL),
            K(0.14f, V(0.12f, 0.6f, -0.06f), V(0f, 0.3f, -1f), V(1f, 0.2f, -0.3f), V(-0.22f, 0.1f, 0.28f), ShieldDir, PoleL, 10f, -10f, -4f, -6f, -0.03f, 0f, 0f),
            K(0.22f, V(0.05f, -0.1f, 0.55f), V(0f, -0.85f, 0.5f), V(1f, -0.3f, 0f), V(-0.28f, -0.1f, 0.15f), ShieldDir, PoleL, -8f, 20f, 10f, 6f, 0.32f, 0.12f, 0.25f),
            K(0.32f, V(0.05f, -0.16f, 0.5f), V(0f, -0.95f, 0.3f), V(1f, -0.3f, 0f), V(-0.28f, -0.1f, 0.15f), ShieldDir, PoleL, -8f, 18f, 9f, 4f, 0.3f, 0.1f, 0.25f),
        };

        static readonly AKey[] StaffKeys =
        {
            K(0.00f, V(0.22f, -0.1f, 0.18f), V(0f, 1f, 0.1f), PoleR, V(-0.22f, -0.2f, 0.05f), V(0f, 0f, 1f), PoleL),
            K(0.13f, V(0.2f, 0.45f, 0.0f), V(0f, 0.2f, -1f), V(1f, 0f, -0.5f), V(-0.22f, -0.2f, 0.05f), V(0f, 0f, 1f), PoleL, 12f, -6f, 0f, 0f, -0.03f, 0.02f, 0f),
            K(0.22f, V(0.05f, 0.0f, 0.5f), V(0f, -0.5f, 1f), V(1f, -1f, 0f), V(-0.22f, -0.2f, 0.05f), V(0f, 0f, 1f), PoleL, -12f, 12f, 4f, 0f, 0.28f, 0.06f, 0.22f),
            K(0.32f, V(0.05f, -0.05f, 0.48f), V(0f, -0.6f, 1f), V(1f, -1f, 0f), V(-0.22f, -0.2f, 0.05f), V(0f, 0f, 1f), PoleL, -12f, 10f, 3f, 0f, 0.26f, 0.05f, 0.22f),
        };

        static readonly AKey[] SpearKeys =
        {
            K(0.00f, V(0.2f, -0.1f, 0.15f), V(0f, 1f, 0.15f), PoleR, V(-0.15f, -0.05f, 0.25f), V(0f, 0.3f, 1f), PoleL),
            K(0.12f, V(0.22f, 0.05f, -0.18f), V(0f, 0.08f, 1f), V(1f, -1f, -0.3f), V(-0.05f, 0.02f, 0.22f), V(0f, 0.08f, 1f), PoleL, 20f, 0f, 0f, 0f, -0.05f, 0.06f, 0f),
            K(0.22f, V(0.08f, 0.05f, 0.42f), V(0f, 0f, 1f), V(1f, -1f, 0f), V(-0.06f, 0.05f, 0.65f), V(0f, 0f, 1f), PoleL, -10f, 8f, 3f, 0f, 0.42f, 0.08f, 0.32f),
            K(0.32f, V(0.08f, 0.03f, 0.4f), V(0f, -0.05f, 1f), V(1f, -1f, 0f), V(-0.06f, 0.03f, 0.62f), V(0f, -0.05f, 1f), PoleL, -10f, 8f, 3f, 0f, 0.4f, 0.07f, 0.32f),
        };

        static readonly AKey[] FistKeys =
        {
            K(0.00f, V(0.2f, 0.0f, 0.2f), V(0f, 1f, 0f), V(1f, -1f, -0.3f), V(-0.2f, 0.02f, 0.22f), V(0f, 1f, 0f), V(-1f, -1f, -0.3f)),
            K(0.10f, V(0.22f, 0.05f, -0.05f), V(0f, 1f, 0f), V(1f, -1f, -0.5f), V(-0.2f, 0.04f, 0.24f), V(0f, 1f, 0f), V(-1f, -1f, -0.3f), 20f, 0f, 0f, 0f, -0.02f, 0.04f, 0f),
            K(0.20f, V(0.02f, 0.1f, 0.62f), V(0f, 0.2f, 1f), V(1f, -0.5f, 0f), V(-0.2f, 0.02f, 0.18f), V(0f, 1f, 0f), V(-1f, -1f, -0.3f), -25f, 6f, 3f, 0f, 0.3f, 0.06f, 0.2f),
            K(0.30f, V(0.05f, 0.08f, 0.5f), V(0f, 0.2f, 1f), V(1f, -0.5f, 0f), V(-0.2f, 0.02f, 0.2f), V(0f, 1f, 0f), V(-1f, -1f, -0.3f), -20f, 5f, 2f, 0f, 0.28f, 0.05f, 0.2f),
        };

        static readonly AKey[] ClawKeys =
        {
            K(0.00f, V(0.25f, 0f, 0.2f), V(0f, 0.3f, 1f), PoleR, V(-0.25f, 0f, 0.2f), V(0f, 0.3f, 1f), PoleL),
            K(0.12f, V(0.45f, 0.4f, -0.05f), V(0.3f, 0.8f, -0.2f), V(1f, 0f, -1f), V(-0.3f, 0.05f, 0.25f), V(0f, 0.3f, 1f), PoleL, 25f, -5f, 0f, 0f, -0.02f, 0.04f, 0f),
            K(0.22f, V(-0.15f, -0.15f, 0.5f), V(-0.5f, -0.6f, 0.6f), V(1f, -0.5f, 0f), V(-0.3f, 0.0f, 0.15f), V(0f, 0.3f, 1f), PoleL, -25f, 12f, 4f, 0f, 0.32f, 0.08f, 0.2f),
            K(0.32f, V(-0.2f, -0.2f, 0.4f), V(-0.6f, -0.6f, 0.4f), V(1f, -0.5f, 0f), V(-0.3f, 0.0f, 0.15f), V(0f, 0.3f, 1f), PoleL, -28f, 10f, 4f, 0f, 0.3f, 0.07f, 0.2f),
        };

        static readonly AKey[] SlamKeys =
        {
            K(0.00f, V(0.3f, 0f, 0.2f), V(0f, 0f, 1f), PoleR, V(-0.3f, 0f, 0.2f), V(0f, 0f, 1f), PoleL),
            K(0.15f, V(0.2f, 0.6f, 0.0f), V(0f, 1f, -0.3f), V(1f, 0f, -0.5f), V(-0.2f, 0.6f, 0.0f), V(0f, 1f, -0.3f), V(-1f, 0f, -0.5f), 0f, -14f, -6f, -6f, -0.05f, 0f, 0f),
            K(0.22f, V(0.12f, -0.3f, 0.55f), V(0f, -1f, 0.3f), V(1f, 0f, 0f), V(-0.12f, -0.3f, 0.55f), V(0f, -1f, 0.3f), V(-1f, 0f, 0f), 0f, 28f, 12f, 8f, 0.25f, 0.16f, 0.15f),
            K(0.34f, V(0.12f, -0.32f, 0.52f), V(0f, -1f, 0.3f), V(1f, 0f, 0f), V(-0.12f, -0.32f, 0.52f), V(0f, -1f, 0.3f), V(-1f, 0f, 0f), 0f, 26f, 11f, 8f, 0.24f, 0.15f, 0.15f),
        };

        // small round creatures (mosslings): rock back, then hop forward and butt with the head, arms flung back
        static readonly AKey[] HeadbuttKeys =
        {
            K(0.00f, V(0.24f, -0.12f, 0.12f), V(0f, 0f, 1f), PoleR, V(-0.24f, -0.12f, 0.12f), V(0f, 0f, 1f), PoleL),
            K(0.12f, V(0.3f, 0.12f, 0.2f), V(0f, 1f, 0.3f), V(1f, -0.5f, -0.5f), V(-0.3f, 0.12f, 0.2f), V(0f, 1f, 0.3f), V(-1f, -0.5f, -0.5f),
              0f, -16f, -10f, -12f, 0f, 0.06f, 0f, -0.08f, 0f),
            K(0.22f, V(0.3f, 0.05f, -0.3f), V(0f, -0.3f, -1f), V(1f, -1f, 0f), V(-0.3f, 0.05f, -0.3f), V(0f, -0.3f, -1f), V(-1f, -1f, 0f),
              0f, 18f, 10f, 6f, 0f, 0f, 0f, 0.7f, 0.16f),
            K(0.34f, V(0.3f, -0.02f, -0.22f), V(0f, -0.5f, -1f), V(1f, -1f, 0f), V(-0.3f, -0.02f, -0.22f), V(0f, -0.5f, -1f), V(-1f, -1f, 0f),
              0f, 16f, 8f, 8f, 0f, 0.03f, 0f, 0.66f, 0.02f),
        };

        static readonly AKey[] BowKeys =
        {
            K(0.00f, V(0.22f, -0.1f, 0.1f), V(0f, 0f, 1f), PoleR, V(-0.22f, -0.1f, 0.15f), V(0f, 1f, 0.1f), PoleL),
            K(0.16f, V(0.1f, 0.3f, 0.05f), V(0f, 0f, 1f), V(1f, 0f, -0.6f), V(-0.02f, 0.22f, 0.6f), V(0f, 1f, 0.05f), V(-1f, -1f, 0f), 50f, 0f, 0f, 0f),
            K(0.20f, V(0.11f, 0.3f, 0.04f), V(0f, 0f, 1f), V(1f, 0f, -0.6f), V(-0.02f, 0.22f, 0.6f), V(0f, 1f, 0.05f), V(-1f, -1f, 0f), 50f, 0f, 0f, 0f),
            K(0.25f, V(0.3f, 0.38f, -0.12f), V(0.3f, 0.2f, 1f), V(1f, 0f, -0.5f), V(-0.02f, 0.2f, 0.62f), V(0f, 1f, -0.05f), V(-1f, -1f, 0f), 45f, -3f, 0f, 0f, -0.03f),
            K(0.45f, V(0.3f, 0.3f, -0.1f), V(0.3f, 0.2f, 1f), V(1f, 0f, -0.5f), V(-0.02f, 0.18f, 0.6f), V(0f, 1f, 0f), V(-1f, -1f, 0f), 45f, 0f, 0f, 0f),
        };

        static readonly AKey[] ThrowKeys =
        {
            K(0.00f, V(0.22f, -0.1f, 0.15f), V(0f, 0f, 1f), PoleR, V(-0.22f, -0.15f, 0.1f), V(0f, 0f, 1f), PoleL),
            K(0.12f, V(0.3f, 0.45f, -0.2f), V(0f, 0.5f, -1f), V(1f, 0f, -0.5f), V(-0.25f, 0.0f, 0.3f), V(0f, 0.5f, 1f), PoleL, 25f, -8f, -2f, 0f, -0.02f, 0.03f, 0f),
            K(0.20f, V(0.05f, 0.2f, 0.6f), V(0f, 0.2f, 1f), V(1f, -0.5f, 0f), V(-0.25f, -0.15f, 0.05f), V(0f, 0f, 1f), PoleL, -20f, 12f, 4f, 0f, 0.12f, 0.03f, 0.12f),
            K(0.28f, V(-0.05f, -0.1f, 0.5f), V(0f, -0.5f, 1f), V(1f, -0.5f, 0f), V(-0.25f, -0.15f, 0.05f), V(0f, 0f, 1f), PoleL, -25f, 14f, 5f, 0f, 0.1f, 0.03f, 0.12f),
        };

        static readonly AKey[] PointKeys =
        {
            K(0.00f, V(0.22f, -0.1f, 0.18f), V(0f, 1f, 0.1f), PoleR, V(-0.22f, -0.15f, 0.1f), V(0f, 0f, 1f), PoleL),
            K(0.14f, V(0.25f, 0.25f, 0.15f), V(0f, 1f, 0.2f), PoleR, V(-0.25f, 0.0f, 0.3f), V(0f, 0.5f, 1f), PoleL, 8f, -4f, 0f, 0f),
            K(0.20f, V(0.12f, 0.2f, 0.55f), V(0f, 0.15f, 1f), V(1f, -1f, 0f), V(-0.25f, -0.05f, 0.25f), V(0f, 0.3f, 1f), PoleL, -8f, 8f, 2f, 0f, 0.1f, 0.02f, 0.1f),
            K(0.30f, V(0.12f, 0.18f, 0.52f), V(0f, 0.1f, 1f), V(1f, -1f, 0f), V(-0.25f, -0.05f, 0.25f), V(0f, 0.3f, 1f), PoleL, -8f, 7f, 2f, 0f, 0.09f, 0.02f, 0.1f),
        };

        static readonly AKey[] CastKeys =
        {
            K(0.00f, V(0.22f, -0.12f, 0.2f), V(0f, 1f, 0.2f), PoleR, V(-0.22f, -0.12f, 0.2f), V(0f, 1f, 0.2f), PoleL),
            K(0.40f, V(0.3f, 0.42f, 0.3f), V(0f, 1f, 0.25f), V(1f, -0.5f, -0.3f), V(-0.28f, 0.25f, 0.32f), V(0f, 1f, 0.5f), V(-1f, -0.6f, -0.3f), 0f, -8f, -3f, -10f),
            K(0.47f, V(0.16f, 0.2f, 0.62f), V(0f, 0.3f, 1f), V(1f, -1f, 0f), V(-0.16f, 0.15f, 0.6f), V(0f, 0.3f, 1f), V(-1f, -1f, 0f), 0f, 10f, 3f, 0f, 0.08f, 0.02f, 0f),
            K(0.60f, V(0.16f, 0.18f, 0.6f), V(0f, 0.3f, 1f), V(1f, -1f, 0f), V(-0.16f, 0.13f, 0.58f), V(0f, 0.3f, 1f), V(-1f, -1f, 0f), 0f, 8f, 3f, 0f, 0.07f, 0.02f, 0f),
        };

        static AKey LerpKey(in AKey a, in AKey b, float u)
        {
            return new AKey
            {
                T = Mathf.Lerp(a.T, b.T, u),
                R = Vector3.Lerp(a.R, b.R, u), RDir = Vector3.Lerp(a.RDir, b.RDir, u), RPole = Vector3.Lerp(a.RPole, b.RPole, u),
                L = Vector3.Lerp(a.L, b.L, u), LDir = Vector3.Lerp(a.LDir, b.LDir, u), LPole = Vector3.Lerp(a.LPole, b.LPole, u),
                ChestYaw = Mathf.Lerp(a.ChestYaw, b.ChestYaw, u), ChestPitch = Mathf.Lerp(a.ChestPitch, b.ChestPitch, u),
                Spine = Mathf.Lerp(a.Spine, b.Spine, u), Head = Mathf.Lerp(a.Head, b.Head, u),
                Lunge = Mathf.Lerp(a.Lunge, b.Lunge, u), Crouch = Mathf.Lerp(a.Crouch, b.Crouch, u), Step = Mathf.Lerp(a.Step, b.Step, u),
                Leap = Mathf.Lerp(a.Leap, b.Leap, u), Hop = Mathf.Lerp(a.Hop, b.Hop, u),
            };
        }

        static void Sample(AKey[] keys, float time, out AKey o)
        {
            if (time <= keys[0].T) { o = keys[0]; return; }
            for (int i = 0; i < keys.Length - 1; i++)
            {
                if (time < keys[i + 1].T)
                {
                    float u = S01((time - keys[i].T) / (keys[i + 1].T - keys[i].T));
                    o = LerpKey(keys[i], keys[i + 1], u);
                    return;
                }
            }
            o = keys[keys.Length - 1];
        }

        AKey[] StrikeKeys(out bool useL)
        {
            useL = true;
            switch (m.Strike)
            {
                case UnitStrike.Sword: return SwordKeys;
                case UnitStrike.Greatsword: return GreatKeys;
                case UnitStrike.Daggers: return DaggerKeys;
                case UnitStrike.Mace: useL = m.Shield || m.TwoHanded; return MaceKeys;
                case UnitStrike.Staff: useL = false; return StaffKeys;
                case UnitStrike.Spear: return SpearKeys;
                case UnitStrike.Claw: case UnitStrike.Whip: return ClawKeys;
                case UnitStrike.Slam: return SlamKeys;
                case UnitStrike.Headbutt: return HeadbuttKeys;
                default: return FistKeys;
            }
        }

        AKey[] RangedKeys(out bool useL)
        {
            useL = false;
            switch (m.Ranged)
            {
                case UnitRanged.Bow: useL = true; return BowKeys;
                case UnitRanged.Throw: return ThrowKeys;
                default: return PointKeys;
            }
        }

        // ================================================================== biped tick

        // per-frame action state (IK arms)
        float actW, actWL;
        AKey act;
        readonly Quaternion[] ikU = new Quaternion[2], ikL = new Quaternion[2], ikH = new Quaternion[2];

        void TickBiped(in UnitAnimInput inp)
        {
            float dt = inp.Dt;
            float time = inp.Time + seed;
            float U = m.Height / 1.75f;
            bool hasLegs = m.Legs.Length == 2;
            bool floater = m.FloatHeight > 0f;
            bool flier = m.Gait == UnitGait.Flier;
            float heavy = m.Heavy;
            float small = m.Gait == UnitGait.Small ? 1f : 0f;
            float L = m.LegLength;

            // ---- speed, direction
            var v = inp.Velocity; v.y = 0f;
            float speed = v.magnitude;
            vs = Mathf.Lerp(vs, speed, 1f - Mathf.Exp(-dt / 0.1f));
            if (speed > 0.05f) moveDir = Vector3.Slerp(moveDir, v / speed, 1f - Mathf.Exp(-dt / 0.06f));
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude < 1e-6f) moveDir = Vector3.forward;
            moveDir.Normalize();
            float turn = Mathf.Abs(inp.YawRate) * Mathf.Deg2Rad;

            // ---- gait
            UnitLeg l0 = hasLegs ? m.Legs[0] : null;
            float chain = hasLegs ? l0.A + l0.B : L;
            // stride/cadence from the actual ground speed (right from the first frame of a move); amplitude (g.Walk)
            // ramps with the smoothed speed so starts and stops blend without pops
            // long robes and dresses take shorter, quicker steps
            var g = GaitParams(Mathf.Max(vs, speed), L, chain, 1.0f, m.StrideK * (1f - 0.15f * m.SkirtClosed), m.MaxCadence * (1f + 0.15f * m.SkirtClosed));
            g.Walk = WalkWeight(vs, L);
            g.Run = Win(g.Vn, 1.08f, 1.4f) * (1f - heavy);
            float turnW = Mathf.Clamp01(turn / 2.2f) * (1f - g.Walk);
            float w = Mathf.Max(g.Walk, turnW);
            walkW = Mathf.Lerp(walkW, g.Walk, 1f - Mathf.Exp(-dt / 0.12f));
            float run = g.Run;
            phase += inp.MoveDist / g.S + Mathf.Min(1.6f, turn * 0.35f) * dt * (1f - g.Walk);
            phase = Frac(phase);
            float liftK = Mathf.Max(g.Walk, turnW * 0.6f) * (1f + small * 0.3f + heavy * 0.25f);

            // signal: +1 when the left foot is forward (scaled by the walk weight)
            float gL = 0f;

            // ---- feet
            if (hasLegs)
            {
                float zL = 0f, zR = 0f;
                for (int i = 0; i < 2; i++)
                {
                    var leg = m.Legs[i];
                    float p = Frac(phase + leg.Phase);
                    FootPath(p, g, g.Lift * liftK, leg.Rest.y, leg.HeelBack, leg.BallFwd, true, out float along, out float fix, out float y, out float pitch, out bool stance);
                    float narrow = 1f - 0.18f * run;
                    var gaitPos = new Vector3(leg.Rest.x * narrow, y, 0f) + moveDir * (along * w) + Vector3.forward * (fix * w);
                    var idlePos = new Vector3(leg.Rest.x * 1.08f, leg.Rest.y, (i == 0 ? 0.035f : -0.03f) * U);
                    float wi = Mathf.Clamp01(w * 1.5f);
                    footT[i] = Vector3.Lerp(idlePos, gaitPos, wi);
                    footPitch[i] = pitch * wi;
                    if (stance && !wasStance[i] && vs > 0.5f)
                    {
                        FootDown = true;
                        FootDownPos = footT[i];
                        stepCount++;
                    }
                    wasStance[i] = stance;
                    if (i == 0) zL = Vector3.Dot(gaitPos, moveDir); else zR = Vector3.Dot(gaitPos, moveDir);
                }
                gL = g.Sweep > 1e-4f ? Mathf.Clamp((zL - zR) / g.Sweep, -1f, 1f) * w : 0f;
            }
            else
            {
                // floaters / fliers: a gentle swing signal from time while moving
                gL = Mathf.Sin(time * 4.2f) * 0.35f * walkW;
            }

            // ---- hips
            float bobPh = (phase - g.Beta * 0.5f) * Mathf.PI * 4f;
            float swPh = (phase - g.Beta * 0.5f) * Mathf.PI * 2f;
            float crouch = L * (0.012f + (0.022f + 0.05f * run) * w + heavy * 0.05f * w);
            float bob = L * (0.016f * (1f - run) * (1f + heavy) - 0.034f * run) * w * Mathf.Cos(bobPh);
            hipsPos = new Vector3(0f, m.HipY - crouch + bob, 0f);
            float sway = L * ((0.024f * (1f - 0.6f * run) + heavy * 0.05f + small * 0.05f) * w);
            hipsPos.x = -sway * Mathf.Cos(swPh);
            // idle weight shift
            float idleW = 1f - Mathf.Clamp01(w * 1.5f);
            hipsPos.x += Mathf.Sin(time * 0.37f) * 0.012f * L * idleW;
            hipsPos.y -= (0.5f + 0.5f * Mathf.Sin(time * 0.37f * 2f)) * 0.004f * L * idleW;
            float hipYaw = (6f + 4f * run + heavy * 5f) * gL;
            float hipRoll = -(3.5f + heavy * 5f + small * 6f) * w * Mathf.Cos(swPh) + Mathf.Sin(time * 0.37f) * 2f * idleW;
            float hipPitch = (2f + 5f * run) * w;
            hipsE = new Vector3(hipPitch, hipYaw, hipRoll);

            // ---- spine / chest / head
            float accel = Mathf.Clamp((speed - vs) * 4f, -6f, 6f);
            float lean = (3f + 9f * run) * w + accel + heavy * 8f * w;
            float breathe = Mathf.Sin(time * 1.65f) * 1.3f * m.Breath * (1f - w * 0.6f);
            spineE = new Vector3(lean * 0.35f - hipPitch * 0.4f, -hipYaw * 0.6f, -hipRoll * 0.5f);
            chestE = new Vector3(lean * 0.5f + breathe, -hipYaw * 1.0f, -hipRoll * 0.35f);
            if (heavy > 0f) chestE.z += Mathf.Sin(swPh) * 4f * heavy * w;

            // glance around while idle
            glanceTimer -= dt;
            if (glanceTimer <= 0f)
            {
                glanceTimer = 2.5f + Random.value * 5f;
                bool look = Random.value < 0.6f && idleW > 0.5f;
                glanceTarget = look ? Random.Range(-38f, 38f) : 0f;
                glancePitchTarget = look ? Random.Range(-6f, 8f) : 0f;
            }
            if (idleW < 0.5f) { glanceTarget = 0f; glancePitchTarget = 0f; }
            glanceYaw = Mathf.Lerp(glanceYaw, glanceTarget, 1f - Mathf.Exp(-dt * 3.5f));
            glancePitch = Mathf.Lerp(glancePitch, glancePitchTarget, 1f - Mathf.Exp(-dt * 3.5f));
            float chestModelYaw = hipYaw + spineE.y + chestE.y;
            float totalPitch = hipPitch + spineE.x + chestE.x;
            neckE = new Vector3(-totalPitch * 0.35f + glancePitch * 0.4f, -chestModelYaw * 0.4f + glanceYaw * 0.4f, -(hipRoll + spineE.z + chestE.z) * 0.4f);
            headE = new Vector3(-totalPitch * 0.4f + glancePitch * 0.6f - breathe * 0.4f, -chestModelYaw * 0.5f + glanceYaw * 0.6f, -(hipRoll + spineE.z + chestE.z) * 0.4f);

            // ---- arms (FK carry styles)
            for (int si = 0; si < 2; si++) HoldPose(si, si == 0 ? m.HoldL : m.HoldR, w, run, gL, heavy, small, floater, v);

            // ---- floating
            if (floater)
            {
                float fh = m.FloatHeight * (1f + Mathf.Sin(time * 1.7f) * 0.12f);
                hipsPos.y = m.HipY + fh;
                hipsPos.x = Mathf.Sin(time * 0.9f) * 0.03f * m.Height;
                var lv = v / Mathf.Max(0.01f, m.Height);
                hipsE = new Vector3(Mathf.Clamp(lv.z * 14f, -12f, 18f) + Mathf.Sin(time * 1.3f) * 2f, 0f, Mathf.Clamp(-lv.x * 14f, -14f, 14f) + Mathf.Sin(time * 1.1f) * 3f);
                spineE = new Vector3(Mathf.Sin(time * 1.7f + 0.6f) * 2f, Mathf.Sin(time * 0.8f) * 4f, 0f);
                chestE = new Vector3(breathe, 0f, Mathf.Sin(time * 1.2f) * 2f);
            }

            // ---- one-shot actions (IK arms)
            BipedActions(inp, U);
            if (m.DrawOnAttack) UpdateDrawn(inp);

            // ---- hit recoil (additive)
            if (inp.HitT >= 0f && inp.HitT < 0.35f)
            {
                float k = Win(inp.HitT, 0f, 0.04f) * (1f - Win(inp.HitT, 0.06f, 0.33f));
                spineE.x -= 9f * k; chestE.x -= 9f * k; headE.x -= 14f * k; neckE.x -= 6f * k;
                chestE.y += 8f * k * (seed > 50f ? 1f : -1f);
                for (int si = 0; si < 2; si++) { armRl[si] += 16f * k; elb[si] += 18f * k; armP[si] += 8f * k; }
                hipsPos.y -= 0.02f * L * k;
                BodyOffset.z -= 0.07f * U * k;
            }

            // ---- lying / dying / getting up
            float lie, kneel;
            LieWeights(inp, out lie, out kneel);
            LieAmount = lie;
            if (kneel > 0f || lie > 0f) ApplyLie(lie, kneel, U, floater);

            SolveBiped(U, floater, flier, time, v, w, run, dt);
        }

        // ---------------------------------------------------------------- carry styles

        void HoldPose(int si, UnitHold hold, float w, float run, float gL, float heavy, float small, bool floater, Vector3 vel)
        {
            int s = si == 0 ? -1 : 1;
            float amp = (16f + 24f * run) * (1f + heavy * 0.6f + small * 0.4f);
            float swing = -amp * gL * s;
            float p = -4f - 8f * run * w;
            float inw = 10f * run * w;
            float roll = 7f + 2f * run + small * 14f + heavy * 6f;
            float e = 10f + 10f * w + 68f * run * w;
            float swingK = 1f;
            handE[si] = Vector3.zero;
            handLock[si] = false;
            handDir[si] = Vector3.up;
            if (si == 0 && m.Shield && hold == UnitHold.Relaxed) { e += 22f; p -= 6f; swingK = 0.7f; }
            switch (hold)
            {
                case UnitHold.OneHand: p -= 6f; e += 16f; handE[si] = new Vector3(76f, 0f, 0f); swingK = 0.8f; break;   // blade forward-down
                case UnitHold.Daggers: p -= 8f; e += 24f; inw += 6f; handE[si] = new Vector3(56f, 0f, 0f); swingK = 0.85f; break;   // blades ~level
                case UnitHold.Staff:
                    p = -12f - 6f * run; roll = 10f; e = 36f + 20f * run; swingK = 0.45f;
                    handLock[si] = true; handDir[si] = new Vector3(s * 0.04f, 1f, 0.06f + 0.1f * run);
                    break;
                case UnitHold.Spear:
                    p = -10f; roll = 9f; e = 30f + 20f * run; swingK = 0.4f;
                    handLock[si] = true; handDir[si] = new Vector3(0f, 1f, 0.05f + 0.25f * run);
                    break;
                case UnitHold.Bow:
                    handLock[si] = true; handDir[si] = new Vector3(0f, 1f, 0.05f); e += 6f; swingK = 0.9f;
                    break;
                case UnitHold.Shoulder:
                    p = -32f; inw = 4f; roll = 14f; e = 138f; handE[si] = new Vector3(5f, -s * 16f, 0f); swingK = 0.12f;
                    break;
                case UnitHold.Book:
                    p = -14f; inw = 22f; roll = 6f; e = 84f; swingK = 0.15f;
                    handLock[si] = true; handDir[si] = new Vector3(-s * 0.15f, 0.85f, 0.5f);
                    break;
                case UnitHold.Cane:
                    p = -22f; roll = 8f; e = 20f; swingK = 0.5f;
                    handLock[si] = true; handDir[si] = new Vector3(s * 0.05f, -1f, 0.32f);
                    break;
                case UnitHold.Carry:
                    p = -18f; inw = 10f; roll = 8f; e = 82f; swingK = 0.25f;
                    handLock[si] = true; handDir[si] = new Vector3(0f, 1f, 0.05f);
                    break;
                case UnitHold.Claws:
                    p = -16f; e += 30f; roll += 6f; handE[si] = new Vector3(-20f, 0f, 0f); swingK = 0.9f;
                    break;
            }
            if (floater)
            {
                // arms trail the motion and drift
                float drag = Mathf.Clamp(vel.z / Mathf.Max(0.3f, m.Height) * 18f, -10f, 30f);
                p += drag + Mathf.Sin(seed + si * 1.7f + Time.time * 1.3f) * 4f;
                roll += 6f + Mathf.Sin(seed * 0.3f + si + Time.time * 0.9f) * 4f;
                e += 8f;
                swingK = 0.3f;
            }
            float sw = swing * swingK;
            armP[si] = p + sw;
            armI[si] = inw;
            armRl[si] = roll;
            elb[si] = e + Mathf.Max(0f, -sw) * 0.35f;
        }

        // ---------------------------------------------------------------- actions

        // cross-fade when an action interrupts another (death during an attack, a cast right after a shot…)
        AKey carryKey;
        float carryW, carryWL, carryT = 99f;
        UnitAction lastAction;
        float lastActionT;

        void BlendCarry(float cw)
        {
            float c = carryW * cw;
            if (c <= 0.001f) return;
            act = LerpKey(carryKey, act, actW / (actW + c));
            actW = Mathf.Max(actW, c);
            actWL = Mathf.Max(actWL, carryWL * cw);
        }

        void BipedActions(in UnitAnimInput inp, float U)
        {
            // keep last frame's arm pose when the action changes, and fade it out over 0.15 s
            if (inp.Action != lastAction || inp.ActionT < lastActionT - 1e-4f)
            {
                if (actW > 0.02f) { carryKey = act; carryW = actW; carryWL = actWL; carryT = 0f; }
            }
            lastAction = inp.Action;
            lastActionT = inp.ActionT;
            carryT += inp.Dt;
            float cw = carryT < 0.15f ? 1f - S01(carryT / 0.15f) : 0f;
            actW = 0f; actWL = 0f;
            BodyOffset = Vector3.zero;
            footRaise = 0f;
            float t = inp.ActionT, dur = Mathf.Max(0.05f, inp.ActionDur);
            actDir = inp.ActionDir.sqrMagnitude > 1e-4f ? inp.ActionDir.normalized : Vector3.forward;
            actDir.y = 0f;
            if (actDir.sqrMagnitude < 1e-4f) actDir = Vector3.forward;
            actDir.Normalize();
            actStepT = t;
            AKey[] keys = null;
            bool useL = false;
            float envOut = 0.16f;
            switch (inp.Action)
            {
                case UnitAction.Attack: keys = StrikeKeys(out useL); break;
                case UnitAction.Shoot: keys = RangedKeys(out useL); break;
                case UnitAction.Cast: keys = CastKeys; useL = true; envOut = 0.2f; break;
            }
            if (keys == null && inp.Casting && inp.Action == UnitAction.None)
            {
                // channelling: arms half raised, slow sway
                Sample(CastKeys, 0.3f + Mathf.Sin(inp.Time * 2.2f) * 0.05f, out act);
                actW = 0.55f + 0.25f * inp.CastProgress;
                actWL = m.HoldL == UnitHold.Bow || m.Shield ? 0f : actW;
                BlendCarry(cw);
                ApplyActKey(U, actW, false);
                return;
            }
            if (keys != null)
            {
                Sample(keys, t, out act);
                float wA = Win(t, 0f, 0.07f) * (1f - Win(t, dur - envOut, dur));
                actW = wA;
                actWL = useL ? wA : 0f;
                BlendCarry(cw);
                ApplyActKey(U, actW, true);
                return;
            }
            if (cw > 0f && carryW > 0f)
            {
                act = carryKey;
                actW = carryW * cw;
                actWL = carryWL * cw;
                ApplyActKey(U, actW, true);
            }
            switch (inp.Action)
            {
                case UnitAction.Dodge:
                {
                    float u = Mathf.Clamp01(t / dur);
                    float k = Mathf.Sin(Mathf.PI * u);
                    float side = inp.DodgeSide >= 0 ? 1f : -1f;
                    BodyOffset += new Vector3(side * 0.55f * U * S01(u * 1.6f) * (1f - Win(u, 0.55f, 1f)), 0.16f * U * k, -0.08f * U * k);
                    hipsE.z -= side * 16f * k;
                    spineE.z -= side * 6f * k;
                    hipsE.x -= 6f * k;
                    footRaise = 0.14f * U * k;
                    for (int si = 0; si < 2; si++) { armRl[si] += 25f * k; elb[si] += 25f * k; }
                    break;
                }
                case UnitAction.Knockback:
                {
                    float u = Mathf.Clamp01(t / dur);
                    float k = Mathf.Sin(Mathf.PI * u);
                    BodyOffset.y += 0.2f * U * k;
                    hipsE.x -= 14f * k; spineE.x -= 8f * k; headE.x -= 12f * k;
                    footRaise = 0.12f * U * k;
                    for (int si = 0; si < 2; si++) { armRl[si] += 30f * k; armP[si] -= 20f * k; elb[si] += 20f * k; }
                    break;
                }
            }
        }

        void ApplyActKey(float U, float wA, bool body)
        {
            if (wA <= 0f) return;
            spineE.x += act.Spine * wA;
            chestE.x += act.ChestPitch * wA;
            chestE.y += act.ChestYaw * wA;
            headE.x += act.Head * wA;
            if (!body) return;
            // crouch / lunge / step by the leg length: the legs have to reach the planted feet
            float lk = m.LegLength / RefLeg;
            // heavies (treant, infernal) bend at the waist rather than squatting on their stumpy legs
            float stiff = 1f - 0.6f * Mathf.Clamp01(m.Heavy);
            float lunge = act.Lunge * wA * stiff;
            float crouchA = act.Crouch * wA * stiff;
            hipsPos.y -= crouchA * lk;
            var dir = actDir;
            BodyOffset += dir * (lunge * lk);
            if (m.Legs.Length == 2)
            {
                // back (left) foot stays planted in the world, front (right) foot steps forward
                footT[0] -= dir * (lunge * lk);
                footT[1] += dir * ((act.Step * wA - lunge) * lk);
                if (act.Step > 0.01f)
                    footT[1].y += 0.06f * lk * wA * Mathf.Sin(Mathf.PI * Mathf.Clamp01((actStepT - 0.08f) / 0.16f));
            }
            // a leap carries the whole body, feet and all
            BodyOffset += dir * (act.Leap * wA * U) + Vector3.up * (act.Hop * wA * U);
        }

        Vector3 actDir = Vector3.forward;
        float actStepT;
        readonly Quaternion[] skirtQ = new Quaternion[2];

        /// <summary>
        /// Bow users' knife (UnitModel.DrawOnAttack): in the hand only during a melee attack, otherwise its hilt shows in
        /// the hip sheath. The hidden copy is scaled (almost) to nothing (rigid skinning; no degenerate normals).
        /// </summary>
        void UpdateDrawn(in UnitAnimInput inp)
        {
            float drawn = 0f;
            if (inp.Action == UnitAction.Attack && !inp.Dead && !inp.Lying)
            {
                float dur = Mathf.Max(0.1f, inp.ActionDur);
                drawn = Win(inp.ActionT, 0f, 0.05f) * (1f - Win(inp.ActionT, dur - 0.07f, dur));
            }
            SetScale(BB.DrawnR, Mathf.Max(0.001f, drawn));
            SetScale(BB.SheathR, Mathf.Max(0.001f, 1f - drawn));
        }

        void SetScale(int bone, float s) { if (bone < n) t[bone].localScale = new Vector3(s, s, s); }

        // ---------------------------------------------------------------- lying

        void LieWeights(in UnitAnimInput inp, out float lie, out float kneel)
        {
            float t = inp.ActionT;
            float ls = Mathf.Clamp01(lieStart);
            switch (inp.Action)
            {
                case UnitAction.Death:
                    lie = Mathf.Lerp(Win(t, 0.36f, 0.72f), 1f, ls);
                    kneel = Win(t, 0.1f, 0.38f) * (1f - Win(t, 0.42f, 0.72f)) * (1f - ls);
                    return;
                case UnitAction.Fall:
                    lie = Mathf.Lerp(Win(t, 0.18f, 0.56f), 1f, ls);
                    kneel = Win(t, 0.0f, 0.22f) * (1f - Win(t, 0.28f, 0.56f)) * (1f - ls);
                    return;
                case UnitAction.Revive:
                    lie = (1f - Win(t, 0.08f, 0.42f)) * ls;
                    kneel = Win(t, 0.05f, 0.3f) * (1f - Win(t, 0.48f, 0.8f)) * ls;
                    return;
            }
            lie = inp.Lying || inp.Dead ? 1f : 0f;
            kneel = 0f;
        }

        void ApplyLie(float lie, float kneel, float U, bool floater)
        {
            float L = m.LegLength;
            if (floater)
            {
                // sink to the ground, slump
                float k = Mathf.Max(lie, kneel * 0.5f);
                hipsPos.y = Mathf.Lerp(hipsPos.y, m.HipY * 0.55f, k);
                hipsE.x = Mathf.Lerp(hipsE.x, 25f, k);
                spineE.x = Mathf.Lerp(spineE.x, 20f, k);
                headE.x = Mathf.Lerp(headE.x, 30f, k);
                for (int si = 0; si < 2; si++) { armP[si] = Mathf.Lerp(armP[si], -10f, k); armRl[si] = Mathf.Lerp(armRl[si], 25f, k); }
                actW *= 1f - k; actWL *= 1f - k;
                return;
            }
            // kneel: hips drop, torso slumps forward, feet stay planted
            if (kneel > 0f)
            {
                hipsPos.y = Mathf.Lerp(hipsPos.y, m.HipY * 0.52f, kneel);
                hipsPos.z = Mathf.Lerp(hipsPos.z, -0.08f * L, kneel);
                hipsE.x += 14f * kneel;
                spineE.x += 18f * kneel;
                chestE.x += 10f * kneel;
                headE.x += 22f * kneel;
                for (int si = 0; si < 2; si++)
                {
                    armP[si] = Mathf.Lerp(armP[si], -18f, kneel);
                    elb[si] = Mathf.Lerp(elb[si], 30f, kneel);
                    footT[si] = Vector3.Lerp(footT[si], new Vector3(m.Legs.Length > si ? m.Legs[si].Rest.x * 1.2f : 0f, m.Legs.Length > si ? m.Legs[si].Rest.y : 0f, (si == 0 ? 0.12f : -0.05f) * L), kneel);
                    footPitch[si] = Mathf.Lerp(footPitch[si], si == 0 ? 0f : 35f, kneel);
                }
            }
            if (lie <= 0f) return;
            // lying on the back, head behind (−Z), legs forward
            float le = S01(lie);
            hipsPos = Vector3.Lerp(hipsPos, new Vector3(0f, m.LieHeight, 0.0f), le);
            hipsE = Vector3.Lerp(hipsE, new Vector3(-86f, 0f, 6f), le);
            spineE = Vector3.Lerp(spineE, new Vector3(-3f, 0f, 0f), le);
            chestE = Vector3.Lerp(chestE, new Vector3(-2f + Mathf.Sin(Time.time * 1.6f + seed) * 1.5f, 0f, 0f), le);
            neckE = Vector3.Lerp(neckE, new Vector3(4f, 15f, 0f), le);
            headE = Vector3.Lerp(headE, new Vector3(6f, 28f, 0f), le);
            for (int si = 0; si < 2; si++)
            {
                armP[si] = Mathf.Lerp(armP[si], 6f, le);
                armI[si] = Mathf.Lerp(armI[si], 0f, le);
                armRl[si] = Mathf.Lerp(armRl[si], 52f + si * 8f, le);
                elb[si] = Mathf.Lerp(elb[si], 28f, le);
                if (m.Legs.Length == 2)
                {
                    var leg = m.Legs[si];
                    var lying = new Vector3(leg.Rest.x * 1.5f, leg.Rest.y * 0.9f, (leg.A + leg.B) * 0.9f + 0.05f * L);
                    footT[si] = Vector3.Lerp(footT[si], lying, le);
                    footPitch[si] = Mathf.Lerp(footPitch[si], -70f, le);
                }
            }
            actW *= 1f - le; actWL *= 1f - le;
            footRaise *= 1f - le;
        }

        // ---------------------------------------------------------------- solve & write

        void SolveBiped(float U, bool floater, bool flier, float time, Vector3 vel, float w, float run, float dt)
        {
            var hipsQ = E(hipsE);
            SetPos(BB.Hips, hipsPos);
            SetRot(BB.Hips, hipsQ);
            var spineQ = E(spineE);
            var chestQ = E(chestE);
            SetRot(BB.Spine, spineQ);
            SetRot(BB.Chest, chestQ);
            SetRot(BB.Neck, E(neckE));
            SetRot(BB.Head, E(headE));

            // model-space chest frame
            var spineModel = hipsQ * spineQ;
            var chestModel = spineModel * chestQ;
            var spinePos = hipsPos + hipsQ * Rest(BB.Spine);
            var chestPos = spinePos + spineModel * Rest(BB.Chest);

            // ---- arms: FK carry pose, blended with IK action pose
            float armLen = Rest(BB.ArmLL).magnitude + Rest(BB.HandL).magnitude;
            float ak = armLen / 0.545f;
            for (int si = 0; si < 2; si++)
            {
                int s = si == 0 ? -1 : 1;
                int bu = BB.ArmU(s), bl = BB.ArmL(s), bh = BB.Hand(s);
                var uQ = E(armP[si], -s * armI[si], s * armRl[si]);
                var lQ = E(-elb[si], 0f, 0f);
                Quaternion hQ;
                var lowerModel = chestModel * uQ * lQ;
                if (handLock[si])
                {
                    var forearm = lowerModel * Vector3.down;
                    var want = Quaternion.LookRotation(handDir[si].normalized, -forearm);
                    hQ = Quaternion.Inverse(lowerModel) * want;
                }
                else hQ = E(handE[si]);

                float wa = si == 0 ? actWL : actW;
                if (Rest(bl).sqrMagnitude < 1e-8f || Rest(bh).sqrMagnitude < 1e-8f) wa = 0f;   // no arm (wisps)
                if (wa > 0.001f)
                {
                    Vector3 target, dir, pole;
                    if (si == 1) { target = act.R; dir = act.RDir; pole = act.RPole; }
                    else
                    {
                        target = act.L; dir = act.LDir; pole = act.LPole;
                        if (m.TwoHanded && (m.Strike == UnitStrike.Mace || m.Strike == UnitStrike.Greatsword))
                        {
                            target = act.R - act.RDir.normalized * 0.14f;
                            dir = act.RDir;
                            pole = V(-1f, -0.5f, 0f);
                        }
                    }
                    var shoulder = chestPos + chestModel * Rest(bu);
                    var tgt = chestPos + new Vector3(target.x * ak, target.y * ak, target.z * ak);
                    float a = Rest(bl).magnitude, b = Rest(bh).magnitude + 0.04f * U;
                    SolveIK(shoulder, tgt, a, b, pole, out var wu, out var wl);
                    var fore = wl * Vector3.down;
                    var dd = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
                    var wh = Quaternion.LookRotation(dd, -fore);
                    var iu = Quaternion.Inverse(chestModel) * wu;
                    var il = Quaternion.Inverse(wu) * wl;
                    var ih = Quaternion.Inverse(wl) * wh;
                    uQ = Quaternion.Slerp(uQ, iu, wa);
                    lQ = Quaternion.Slerp(lQ, il, wa);
                    hQ = Quaternion.Slerp(hQ, ih, wa);
                }
                SetRot(bu, uQ);
                SetRot(bl, lQ);
                SetRot(bh, hQ);
            }

            // ---- legs (IK)
            if (m.Legs.Length == 2)
            {
                // reach constraint: lower the hips if a planted foot cannot be reached
                float ymax = float.MaxValue;
                for (int i = 0; i < 2; i++)
                {
                    var leg = m.Legs[i];
                    var hj = hipsPos + hipsQ * Rest(leg.Upper);
                    var f = footT[i];
                    // every foot counts (also the rear foot rolling off its toes, the most extended one)
                    float R = (leg.A + leg.B) * 0.99f;
                    float dx = f.x - hj.x, dz = f.z - hj.z;
                    float rem = R * R - dx * dx - dz * dz;
                    float allowed = (rem > 0f ? f.y + Mathf.Sqrt(rem) : f.y + R * 0.3f) - (hj.y - hipsPos.y);
                    ymax = Mathf.Min(ymax, allowed);
                }
                if (ymax < float.MaxValue && LieAmount < 0.5f)
                {
                    float ny = SoftMin(hipsPos.y, ymax, 0.03f * m.LegLength);
                    if (Mathf.Abs(ny - hipsPos.y) > 1e-5f)
                    {
                        hipsPos.y = ny;
                        SetPos(BB.Hips, hipsPos);
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    var leg = m.Legs[i];
                    var hj = hipsPos + hipsQ * Rest(leg.Upper);
                    var target = footT[i] + Vector3.up * footRaise;
                    if (footRaise > 0f) target += Vector3.forward * (footRaise * 0.6f);
                    var pole = hipsQ * leg.Pole;
                    SolveIK(hj, target, leg.A, leg.B, pole, out var wu, out var wl);
                    // foot: pitch around its own X, turned out a little, following the hips' yaw
                    var footStand = E(footPitch[i], hipsE.y * 0.5f + leg.Side * 4f, 0f);
                    var footLie = hipsQ * E(25f, 0f, 0f);
                    var wf = Quaternion.Slerp(footStand, footLie, LieAmount);
                    var lu = Quaternion.Inverse(hipsQ) * wu;
                    SetRot(leg.Upper, lu);
                    SetRot(leg.Lower, Quaternion.Inverse(wu) * wl);
                    SetRot(leg.Foot, Quaternion.Inverse(wl) * wf);
                    var dn = lu * Vector3.down;
                    legPitch[i] = -Mathf.Atan2(dn.z, -dn.y) * Mathf.Rad2Deg;
                    skirtQ[i] = Quaternion.Slerp(Quaternion.identity, lu, 0.72f);
                }
                if (m.SkirtClosed > 0f)
                {
                    // long robe: both front halves swing forward together with the leading leg (and a little sideways
                    // with each leg) so the front stays closed; the back panel follows the trailing leg (below)
                    float fwd = Mathf.Min(0f, Mathf.Min(legPitch[0], legPitch[1])) * 0.62f;
                    for (int i = 0; i < 2; i++)
                    {
                        var dn = skirtQ[i] * Vector3.down;
                        float rollDeg = Mathf.Clamp(Mathf.Atan2(dn.x, -dn.y) * Mathf.Rad2Deg * 0.5f, -10f, 10f);
                        var closedQ = E(fwd, 0f, rollDeg);
                        skirtQ[i] = Quaternion.Slerp(skirtQ[i], closedQ, m.SkirtClosed);
                    }
                }
                SetRot(BB.SkirtL, skirtQ[0]);
                SetRot(BB.SkirtR, skirtQ[1]);
            }
            else
            {
                legPitch[0] = legPitch[1] = 0f;
                if (floater)
                {
                    // robe/smoke hem panels drift
                    SetRot(BB.SkirtL, E(Mathf.Sin(time * 1.9f) * 6f, 0f, -4f));
                    SetRot(BB.SkirtR, E(Mathf.Sin(time * 1.9f + 1.4f) * 6f, 0f, 4f));
                }
            }

            // ---- garments & appendages (springs)
            float speedK = Mathf.Clamp01(vs / Mathf.Max(0.5f, 1.6f * m.LegLength * 2f));
            float drag = speedK * (10f + 18f * run);
            float frontFlap = Mathf.Min(legPitch[0], legPitch[1]) * 0.6f;
            float backFlap = Mathf.Max(legPitch[0], legPitch[1]) * Mathf.Lerp(0.6f, 0.8f, m.SkirtClosed);   // a long robe's back covers the trailing leg
            SetRot(BB.SkirtF, E(Mathf.Min(0f, frontFlap) * 1.0f + drag * 0.4f, 0f, 0f));
            SetRot(BB.SkirtB, E(Mathf.Max(0f, backFlap) + drag * 0.8f + (floater ? Mathf.Sin(time * 2.1f) * 5f : 0f), 0f, 0f));

            float chestPitchModel = hipsE.x + spineE.x + chestE.x;
            float bobV = dt > 0f ? (hipsPos.y - lastBobY) / dt : 0f;
            lastBobY = hipsPos.y;
            float capeTarget = drag * 1.6f + Mathf.Sin(time * 7f) * 4f * speedK - chestPitchModel * (1f - LieAmount) + 4f;
            Spring(ref capeA, ref capeV, capeTarget, 60f, 9f, dt);
            capeV += -bobV * 30f * dt;
            SetRot(BB.Cape, E(Mathf.Clamp(capeA, -10f, 75f), 0f, Mathf.Sin(time * 3.1f) * 2f * speedK));

            float headPitchModel = chestPitchModel + neckE.x + headE.x;
            Spring(ref hairA, ref hairV, -headPitchModel * 0.7f * (1f - LieAmount) + drag * 0.8f, 70f, 8f, dt);
            hairV += -bobV * 40f * dt;
            SetRot(BB.HairB, E(Mathf.Clamp(hairA, -40f, 60f), 0f, 0f));

            float wag = Mathf.Sin(time * (2f + 3f * speedK)) * (10f + 8f * speedK);
            Spring(ref tailA, ref tailV, 20f + drag, 40f, 7f, dt);
            SetRot(BB.Tail, E(Mathf.Clamp(tailA, -20f, 70f), wag, 0f));

            if (m.Wings)
            {
                float flap;
                if (m.WingsFlap || flier) flap = Mathf.Sin(time * (flier ? 11f : 8f)) * (flier ? 55f : 30f) + (flier ? 10f : 0f);
                else flap = Mathf.Sin(time * 2.3f) * 6f + Mathf.Max(0f, Mathf.Sin(time * 0.7f)) * 10f;
                float fold = flier ? 0f : 20f;   // folded wings sweep back (left: −yaw, right: +yaw)
                SetRot(BB.WingL, E(0f, -fold, -flap));
                SetRot(BB.WingR, E(0f, fold, flap));
            }

            if (flier)
            {
                // owl: body leans with motion, head swivels independently
                var hq = E(Mathf.Clamp(vel.z * 10f, -10f, 25f), 0f, Mathf.Clamp(-vel.x * 10f, -15f, 15f));
                SetRot(BB.Hips, hipsQ * hq);
                SetRot(BB.Head, E(headE.x * 0.5f, glanceYaw * 2.2f, 0f));
            }
        }
    }
}
