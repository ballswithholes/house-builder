// Procedural skeletal animation of one unit body (no Animator/AnimationClip: everything is computed per frame).
//
// Locomotion is driven by the ground actually covered: the gait phase advances by distance / stride, and planted
// feet move backwards at exactly the body speed in model space, so feet never skate. Stride length and duty factor
// follow the Froude-scaled speed (v / √(g·legLength)), so a child, a wolf and the 4.5 m warden all move plausibly at
// any speed. Feet are placed on a trajectory (heel strike → flat → heel lift around the ball → swing arc) and the legs
// are solved with analytic two-bone IK; hips bob twice per stride, sway over the stance foot, roll and counter-rotate
// against the shoulders; arms counter-swing; idle breathes, shifts weight and glances around.
//
// One-shots (attack, shoot, cast, hit, dodge, death, downed, revive) are layered on top with weight envelopes;
// lying poses blend the whole skeleton. Everything is allocation-free per frame.
//
// Model space: Y up, +Z forward, +X right (the Body transform stands it up, see World3D). Angles in degrees.
using UnityEngine;

namespace Lanternvale.Game
{
    public enum UnitAction { None, Attack, Shoot, Cast, Dodge, Knockback, Fall, Revive, Death }

    /// <summary>Per-frame input from UnitView.</summary>
    public struct UnitAnimInput
    {
        public float Dt, Time;
        /// <summary>Ground velocity from path movement (model space, model units/s).</summary>
        public Vector3 Velocity;
        public float MoveDist;       // model units covered along the path this frame
        public float YawRate;        // degrees per second
        public UnitAction Action;
        public float ActionT, ActionDur;
        public Vector3 ActionDir;    // model space, unit, towards the target
        public int DodgeSide;
        public float HitT;           // seconds since PlayHit (< 0: none)
        public bool Lying, Dead;
        public bool Casting;
        public float CastProgress;
        public float SpawnT;
    }

    public sealed partial class UnitAnimator
    {
        readonly UnitModel m;
        readonly Transform[] t;
        readonly int n;
        readonly float seed;

        // ---- outputs
        /// <summary>Body offset in model space (lunges, hops, recoil) — UnitView applies it to the Body transform.</summary>
        public Vector3 BodyOffset;
        /// <summary>A foot touched down this frame (model-space ankle position) — footstep dust.</summary>
        public bool FootDown;
        public Vector3 FootDownPos;
        /// <summary>0 standing … 1 lying (for anchors/shadow).</summary>
        public float LieAmount;
        /// <summary>Extra uniform scale of the body (spawn pop).</summary>
        public float ScalePop = 1f;

        // ---- gait state
        float phase, vs, liftAmt, walkW;
        Vector3 moveDir = Vector3.forward;
        readonly bool[] wasStance = new bool[8];
        int stepCount;

        // ---- idle life
        float glanceYaw, glanceTarget, glanceTimer, glancePitchTarget, glancePitch;

        // ---- secondary motion (springs)
        float capeA, capeV, hairA, hairV, tailA, tailV, tailYaw, tailYawV, wobX, wobXV, wobZ, wobZV;
        float lastBobY;

        // ---- biped working pose
        Vector3 hipsPos, hipsE, spineE, chestE, neckE, headE;
        readonly float[] armP = new float[2], armI = new float[2], armRl = new float[2], elb = new float[2];
        readonly Vector3[] handE = new Vector3[2];
        readonly bool[] handLock = new bool[2];
        readonly Vector3[] handDir = new Vector3[2];
        readonly Vector3[] footT = new Vector3[8];
        readonly float[] footPitch = new float[8];
        readonly float[] legPitch = new float[2];
        float footRaise;

        public UnitAnimator(UnitModel model, Transform[] bones)
        {
            m = model;
            t = bones;
            n = bones.Length;
            seed = Random.value * 100f;
            phase = Random.value;
            glanceTimer = 2f + Random.value * 4f;
            for (int i = 0; i < wasStance.Length; i++) wasStance[i] = true;
        }

        // ================================================================== helpers

        static float S01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static float Win(float x, float a, float b) => S01((x - a) / (b - a));
        static float Bump(float x, float a, float b, float c, float d) => Win(x, a, b) * (1f - Win(x, c, d));
        static Quaternion E(float x, float y, float z) => Quaternion.Euler(x, y, z);
        static Quaternion E(Vector3 v) => Quaternion.Euler(v.x, v.y, v.z);
        static float Frac(float x) => x - Mathf.Floor(x);

        static float SoftMin(float a, float b, float k)
        {
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / Mathf.Max(1e-5f, k);
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        static void Spring(ref float x, ref float v, float target, float stiffness, float damping, float dt)
        {
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);   // frame hitches must not blow the integration up
            float a = (target - x) * stiffness - v * damping;
            v += a * dt;
            x += v * dt;
        }

        /// <summary>Rotation whose local −Y points along `down` and whose +Z leans towards `fwd`.</summary>
        static Quaternion BoneRot(Vector3 down, Vector3 fwd)
        {
            var f = fwd - down * Vector3.Dot(fwd, down);
            if (f.sqrMagnitude < 1e-8f)
            {
                f = Vector3.Cross(down, Vector3.right);
                if (f.sqrMagnitude < 1e-8f) f = Vector3.forward;
            }
            return Quaternion.LookRotation(f.normalized, -down);
        }

        /// <summary>Two-bone IK in model space. Returns the model rotations of both segments.</summary>
        static void SolveIK(Vector3 hip, Vector3 target, float a, float b, Vector3 pole, out Quaternion upper, out Quaternion lower)
        {
            var d = target - hip;
            float dist = d.magnitude;
            var dir = dist > 1e-5f ? d / dist : Vector3.down;
            dist = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-3f, (a + b) * 0.9995f);
            var p = pole - dir * Vector3.Dot(pole, dir);
            if (p.sqrMagnitude < 1e-8f) p = Vector3.Cross(dir, Vector3.right);
            p.Normalize();
            float cosA = Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            var knee = hip + (dir * cosA + p * sinA) * a;
            var ankle = hip + dir * dist;
            var u1 = (knee - hip) / a;
            var u2 = ankle - knee;
            u2 = u2.sqrMagnitude > 1e-10f ? u2.normalized : dir;
            upper = BoneRot(u1, p);
            lower = BoneRot(u2, p);
        }

        Vector3 Rest(int bone) => m.LocalOffset(bone);

        void SetRot(int bone, Quaternion q) { if (bone < n) t[bone].localRotation = q; }
        void SetPos(int bone, Vector3 p) { if (bone < n) t[bone].localPosition = p; }

        // ================================================================== entry

        // lying amount when the current action started (death of a downed unit continues from the ground)
        float lieStart;
        UnitAction lieAction;
        float lieActionT;

        public void Tick(in UnitAnimInput inp)
        {
            if (inp.Action != lieAction || inp.ActionT < lieActionT - 1e-4f) lieStart = LieAmount;
            lieAction = inp.Action;
            lieActionT = inp.ActionT;
            FootDown = false;
            BodyOffset = Vector3.zero;
            ScalePop = 1f;
            if (m.SpawnPop && inp.SpawnT < 0.5f)
            {
                float u = Mathf.Clamp01(inp.SpawnT / 0.45f);
                ScalePop = Mathf.Max(0.01f, 1f + Mathf.Sin(u * Mathf.PI * 1.5f) * (1f - u) * 0.35f - (1f - S01(u * 2.2f)));
            }
            switch (m.Rig)
            {
                case UnitRigKind.Biped: TickBiped(inp); break;
                case UnitRigKind.Quad: TickQuad(inp); break;
                case UnitRigKind.Spider: TickSpider(inp); break;
                default: TickStatic(inp); break;
            }
        }

        // ================================================================== common gait maths

        struct Gait
        {
            public float S, Beta, Sweep, Lift, Run, Walk, Vn;
        }

        Gait GaitParams(float speed, float legLen, float chain, float maxSweepK, float strideK, float maxCadence)
        {
            var g = new Gait();
            float L = Mathf.Max(0.05f, legLen);
            g.Vn = speed / Mathf.Sqrt(9.81f * L);
            g.Run = Win(g.Vn, 1.0f, 1.3f);
            g.Walk = Mathf.Clamp01(speed / Mathf.Max(0.05f, 0.35f * Mathf.Sqrt(9.81f * L)));
            float S = L * 2.3f * Mathf.Pow(Mathf.Max(g.Vn, 0.001f), 0.6f) * strideK;
            S = Mathf.Max(S, 0.3f * L);
            if (speed / S > maxCadence) S = speed / maxCadence;
            g.S = S;
            float beta = Mathf.Lerp(0.62f, 0.36f, Win(g.Vn, 0.45f, 1.25f));
            float maxSweep = maxSweepK * chain;
            if (beta * S > maxSweep) beta = maxSweep / S;
            g.Beta = Mathf.Clamp(beta, 0.2f, 0.7f);
            g.Sweep = g.Beta * S;
            g.Lift = L * (0.08f + 0.1f * g.Run);
            return g;
        }

        static float WalkWeight(float speed, float legLen) => Mathf.Clamp01(speed / Mathf.Max(0.05f, 0.35f * Mathf.Sqrt(9.81f * Mathf.Max(0.05f, legLen))));

        /// <summary>Foot trajectory for leg phase p. Returns the ankle (dir-axis offset, height) and the foot pitch.</summary>
        static void FootPath(float p, in Gait g, float lift, float ankleY, float heel, float ball, bool heelToe,
                             out float along, out float fwdFix, out float y, out float pitch, out bool stance)
        {
            float onP = heelToe ? -(12f + 4f * g.Run) * (1f - g.Run * 0.7f) : 0f;
            float offP = heelToe ? 28f + 18f * g.Run : 12f;
            if (p < g.Beta)
            {
                stance = true;
                float u = p / g.Beta;
                along = g.Sweep * (0.5f - u);
                pitch = u < 0.15f ? Mathf.Lerp(onP, 0f, S01(u / 0.15f)) : (u > 0.6f ? Mathf.Lerp(0f, offP, S01((u - 0.6f) / 0.4f)) : 0f);
                Ankle(pitch, ankleY, heel, ball, out y, out fwdFix);
            }
            else
            {
                stance = false;
                float u = (p - g.Beta) / (1f - g.Beta);
                Ankle(offP, ankleY, heel, ball, out float y0, out float f0);
                Ankle(onP, ankleY, heel, ball, out float y1, out float f1);
                float s = u * u * u * (u * (u * 6f - 15f) + 10f);
                along = Mathf.Lerp(-g.Sweep * 0.5f, g.Sweep * 0.5f, s);
                fwdFix = Mathf.Lerp(f0, f1, s);
                y = Mathf.Lerp(y0, y1, s) + lift * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.8f));
                pitch = Mathf.Lerp(offP, onP, S01(u * 1.2f)) + (heelToe ? -10f * Mathf.Sin(Mathf.PI * u) : 0f);
            }
        }

        /// <summary>Ankle height and forward offset of a foot pitched by `pitch` around its heel (pitch &lt; 0) or ball (&gt; 0).</summary>
        static void Ankle(float pitch, float a, float heel, float ball, out float y, out float fwd)
        {
            float th = pitch * Mathf.Deg2Rad;
            float c = Mathf.Cos(th), s = Mathf.Sin(th);
            if (pitch >= 0f)
            {
                y = a * c + ball * s;
                fwd = ball + (a * s - ball * c);
            }
            else
            {
                y = a * c - heel * s;
                fwd = -heel + (a * s + heel * c);
            }
        }
    }
}
