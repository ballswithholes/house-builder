// Modelling kits for creatures: quadrupeds (wolf, boar, cat, bear, felhunter, sheep, the warden stag), the spider
// and static models (totems, dummy, traps, lightwell). Bind pose: standing, facing +Z, Y up, feet on y = 0.
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class QuadKit
    {
        public readonly MeshBuilder M;
        public readonly UnitModel Model = new UnitModel();
        public readonly Vector3[] Bind = new Vector3[QB.Count];

        public readonly float HipY, ChestY, BodyLen, BodyR, LegX, LegR, JointF, JointB, PawY, NeckUp, NeckLen, HeadR;
        public Vector3 NeckBase, HeadBase;

        /// <summary>
        /// hipY/chestY: body centre heights (rear/front); bodyLen: rear→front body centre distance; bodyR: body radius;
        /// legX: lateral leg offset; legR: leg radius; jointF/jointB: shoulder/hip joint heights; neckUp/neckLen: neck
        /// rise and length; headR: skull radius.
        /// </summary>
        public QuadKit(int seed, float hipY, float chestY, float bodyLen, float bodyR, float legX, float legR,
                       float jointF, float jointB, float neckUp, float neckLen, float headR, float pawY = 0.035f)
        {
            M = new MeshBuilder(seed) { Jitter = 0.055f };
            HipY = hipY; ChestY = chestY; BodyLen = bodyLen; BodyR = bodyR; LegX = legX; LegR = legR;
            JointF = jointF; JointB = jointB; PawY = pawY; NeckUp = neckUp; NeckLen = neckLen; HeadR = headR;
            float zh = -bodyLen * 0.5f, zc = bodyLen * 0.5f;
            Bind[QB.Hips] = new Vector3(0f, hipY, zh);
            Bind[QB.Chest] = new Vector3(0f, chestY, zc);
            NeckBase = new Vector3(0f, chestY + bodyR * 0.45f, zc + bodyR * 0.55f);
            Bind[QB.Neck] = NeckBase;
            HeadBase = NeckBase + new Vector3(0f, neckUp, Mathf.Sqrt(Mathf.Max(0.0001f, neckLen * neckLen - neckUp * neckUp)));
            Bind[QB.Head] = HeadBase;
            Bind[QB.Jaw] = HeadBase + new Vector3(0f, -headR * 0.45f, headR * 0.45f);
            // legs: slightly bent in the bind pose (middle joint behind), paws under the joints
            SetLeg(QB.FLU, QB.FLL, QB.FLF, -1, jointF, zc + bodyR * 0.25f, 0.5f, 0.06f);
            SetLeg(QB.FRU, QB.FRL, QB.FRF, 1, jointF, zc + bodyR * 0.25f, 0.5f, 0.06f);
            SetLeg(QB.BLU, QB.BLL, QB.BLF, -1, jointB, zh - bodyR * 0.2f, 0.36f, 0.1f);
            SetLeg(QB.BRU, QB.BRL, QB.BRF, 1, jointB, zh - bodyR * 0.2f, 0.36f, 0.1f);
            Bind[QB.Tail1] = new Vector3(0f, hipY + bodyR * 0.45f, zh - bodyR * 0.9f);
            Bind[QB.Tail2] = Bind[QB.Tail1] + new Vector3(0f, -bodyR * 0.3f, -bodyR * 0.9f);
            Bind[QB.Back] = new Vector3(0f, Mathf.Lerp(hipY, chestY, 0.5f) + bodyR * 0.85f, 0f);
            // wing and tail-tip bones (QB.WingL/R, WingL2/R2, Tail3) carry nothing unless Wings / LongTail author them
            Bind[QB.WingL] = Bind[QB.WingR] = Bind[QB.WingL2] = Bind[QB.WingR2] = Bind[QB.Chest];
            Bind[QB.Tail3] = Bind[QB.Tail2];
        }

        void SetLeg(int u, int l, int f, int side, float jointY, float z, float midFrac, float bend)
        {
            float x = side * LegX;
            float midY = PawY + (jointY - PawY) * midFrac;
            float back = (jointY - PawY) * bend;
            Bind[u] = new Vector3(x, jointY, z);
            Bind[l] = new Vector3(x, midY, z - back);
            Bind[f] = new Vector3(x, PawY, z);
        }

        // the torso's shape (set by Body): rump end and chest front (body depth z), the build factors
        float tz0, tz1, tRearK = 0.95f, tFrontK = 1.05f, tHump, tWaist = 1f;
        bool torsoSet;

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The torso's section at body depth z: centre height, half width, height above and depth below the centre.</summary>
        void Section(float z, out float cy, out float w, out float top, out float bottom)
        {
            float s = Mathf.Clamp01((z - tz0) / (tz1 - tz0));
            float u = s * 2f - 1f;
            // rounded rump and chest: a superellipse along the spine (flatter sides than an ellipse, a bean not a ball)
            float e = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(u), 2.6f)), 1f / 2.6f);
            float front = Smooth(0.42f, 0.78f, s) * (1f - Smooth(0.9f, 1f, s) * 0.4f);
            float tuck = (1f - tWaist) * Mathf.Exp(-((s - 0.48f) / 0.2f) * ((s - 0.48f) / 0.2f));
            float wb = BodyR * Mathf.Lerp(tRearK, tFrontK, Smooth(0.3f, 0.7f, s));
            cy = Mathf.Lerp(HipY, ChestY, Smooth(0.2f, 0.8f, s)) + tHump * BodyR * 0.3f * front;
            w = wb * 1.05f * (1f - tuck * 0.5f) * e;
            top = wb * (1f + tHump * 0.25f * front) * (1f - tuck * 0.3f) * e;
            // lean builds (a waist tuck) get a deep chest over the tucked belly: the canine/feline line
            bottom = wb * 0.98f * (1f - tuck * 1.1f) * (1f + (1f - tWaist) * 1.5f * front) * e;
        }

        /// <summary>
        /// The torso as ONE continuous faceted bean from the rump to the chest, tucked at the waist (`waist` &lt; 1: wolves,
        /// cats; ≈ 1: a solid barrel for boars and bears) and raised at the shoulders by `hump`. Rigidly skinned, it is two
        /// pieces sharing the middle ring: the rear half on the Hips, the front half on the Chest, each running on past
        /// the middle while shrinking inside the other half, so the chest can turn without opening a gap — and at rest
        /// the outline is one smooth body (overlapping ellipsoids read as beads with creases between them). Belly faces
        /// under the chest take `belly`, an optional darker `saddle` runs along the top.
        /// </summary>
        public void Body(Color back, Color belly, float rearK = 0.95f, float frontK = 1.05f, float hump = 0f, float waist = 0.97f, Color? saddle = null)
        {
            tz0 = Bind[QB.Hips].z - BodyLen * 0.4f;
            tz1 = Bind[QB.Chest].z + BodyLen * 0.4f;
            tRearK = rearK; tFrontK = frontK; tHump = hump; tWaist = waist;
            torsoSet = true;
            const int n = 14, over = 2;
            var zs = new float[n + 1];
            for (int i = 0; i <= n; i++) zs[i] = Mathf.Lerp(tz0, tz1, 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / n));   // denser at the ends
            int mid = n / 2;
            TorsoPiece(QB.Hips, zs, 0, mid + over, mid, back, belly, saddle);
            TorsoPiece(QB.Chest, zs, mid - over, n, mid, back, belly, saddle);
        }

        void TorsoPiece(int bone, float[] zs, int from, int to, int mid, Color back, Color belly, Color? saddle)
        {
            const int sides = 10, over = 2;
            int rings = to - from + 1;
            var pts = new Vector3[rings, sides];
            var centre = new Vector3[rings];
            for (int r = 0; r < rings; r++)
            {
                int i = from + r;
                Section(zs[i], out float cy, out float w, out float top, out float bottom);
                // past the shared middle ring this piece shrinks inside the other one (same slope at the middle: no kink)
                float k = 1f;
                if (bone == QB.Hips && i > mid) k = 1f - 0.3f * Smooth(0f, 1f, (i - mid) / (float)over);
                if (bone == QB.Chest && i < mid) k = 1f - 0.3f * Smooth(0f, 1f, (mid - i) / (float)over);
                centre[r] = new Vector3(0f, cy, zs[i]);
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    float c = Mathf.Cos(a);
                    pts[r, j] = new Vector3(Mathf.Sin(a) * w * k, cy + c * (c >= 0f ? top : bottom) * k, zs[i]);
                }
            }
            M.Bone = bone;
            // big regular facets: a softer per-face jitter than the small parts, or the pelt reads as a checkerboard
            float keepJ = M.Jitter;
            M.Jitter = keepJ * 0.5f;
            float zMid = zs[mid], len = tz1 - tz0;
            for (int r = 0; r + 1 < rings; r++)
            {
                float zc = (zs[from + r] + zs[from + r + 1]) * 0.5f;
                for (int j = 0; j < sides; j++)
                {
                    int j1 = (j + 1) % sides;
                    float am = (j + 0.5f) * Mathf.PI * 2f / sides, ca = Mathf.Cos(am);
                    bool underChest = ca < -0.45f && zc > zMid - 0.12f * len;
                    bool onTop = ca > 0.55f && zc > tz0 + 0.18f * len && zc < tz1 - 0.12f * len;
                    M.Color = underChest ? belly : onTop && saddle.HasValue ? saddle.Value : back;
                    var a0 = pts[r, j]; var a1 = pts[r, j1]; var b0 = pts[r + 1, j]; var b1 = pts[r + 1, j1];
                    var outward = (a0 + a1 + b0 + b1) * 0.25f - Vector3.Lerp(centre[r], centre[r + 1], 0.5f);
                    if ((a0 - a1).sqrMagnitude < 1e-10f) M.TriangleFacing(a0, b0, b1, outward);
                    else if ((b0 - b1).sqrMagnitude < 1e-10f) M.TriangleFacing(a0, b0, a1, outward);
                    else M.Quad(a0, a1, b1, b0, outward);
                }
            }
            // close the end that runs on inside the other half (it shows only while the chest turns)
            int capR = bone == QB.Hips ? rings - 1 : 0;
            M.Color = back;
            var dir = new Vector3(0f, 0f, bone == QB.Hips ? 1f : -1f);
            for (int j = 0; j < sides; j++)
                M.TriangleFacing(centre[capR], pts[capR, j], pts[capR, (j + 1) % sides], dir);
            M.Jitter = keepJ;
        }

        /// <summary>A point on the torso (after Body) at body depth z and angle aDeg round it (0 = the top, 90 = the +x side), `lift` metres out.</summary>
        public Vector3 TorsoPoint(float z, float aDeg, float lift = 0f)
        {
            Section(Mathf.Clamp(z, tz0, tz1), out float cy, out float w, out float top, out float bottom);
            float a = aDeg * Mathf.Deg2Rad, c = Mathf.Cos(a);
            return new Vector3(Mathf.Sin(a) * (w + lift), cy + c * ((c >= 0f ? top : bottom) + lift), z);
        }

        /// <summary>
        /// A cloth (saddle blanket) draped over the torso from depth z0 to z1, halfAngle degrees down each flank,
        /// following the body (a cylinder section around it sank into the hump), with a lining underneath and an
        /// optional hem band along its lower edges. Bound to the current M.Bone.
        /// </summary>
        public void Drape(Color c, Color lining, float z0, float z1, float halfAngle, float lift, Color? hem = null)
        {
            const int nz = 6, na = 8;
            float keepJ = M.Jitter;
            M.Jitter = keepJ * 0.5f;
            for (int layer = 0; layer < 2; layer++)
            {
                float l = layer == 0 ? lift : lift * 0.5f;
                for (int i = 0; i < nz; i++)
                    for (int j = 0; j < na; j++)
                    {
                        float za = Mathf.Lerp(z0, z1, (float)i / nz), zb = Mathf.Lerp(z0, z1, (float)(i + 1) / nz);
                        float aa = Mathf.Lerp(-halfAngle, halfAngle, (float)j / na), ab = Mathf.Lerp(-halfAngle, halfAngle, (float)(j + 1) / na);
                        var p00 = TorsoPoint(za, aa, l); var p01 = TorsoPoint(za, ab, l);
                        var p11 = TorsoPoint(zb, ab, l); var p10 = TorsoPoint(zb, aa, l);
                        Section((za + zb) * 0.5f, out float cy, out _, out _, out _);
                        var outward = (p00 + p01 + p11 + p10) * 0.25f - new Vector3(0f, cy, (za + zb) * 0.5f);
                        M.Color = layer == 0 ? c : lining;
                        M.Quad(p00, p01, p11, p10, layer == 0 ? outward : -outward);
                    }
            }
            if (hem.HasValue)
            {
                M.Color = hem.Value;
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < nz; i++)
                    {
                        float za = Mathf.Lerp(z0, z1, (float)i / nz), zb = Mathf.Lerp(z0, z1, (float)(i + 1) / nz);
                        var a = TorsoPoint(za, s * halfAngle, lift * 1.4f); var b = TorsoPoint(zb, s * halfAngle, lift * 1.4f);
                        var a2 = TorsoPoint(za, s * (halfAngle - 9f), lift * 1.4f); var b2 = TorsoPoint(zb, s * (halfAngle - 9f), lift * 1.4f);
                        Section((za + zb) * 0.5f, out float cy, out _, out _, out _);
                        M.Quad(a, b, b2, a2, (a + b + a2 + b2) * 0.25f - new Vector3(0f, cy, (za + zb) * 0.5f));
                    }
            }
            M.Jitter = keepJ;
        }

        /// <summary>Height of the back line (the top of the torso) at body depth z (after Body()).</summary>
        public float TopY(float z)
        {
            if (!torsoSet) return HipY + BodyR;
            Section(Mathf.Clamp(z, tz0, tz1), out float cy, out _, out float top, out _);
            return cy + top;
        }

        public void Neck(Color c, float r0 = 1f, float r1 = 0.8f)
        {
            M.Bone = QB.Neck; M.Color = c;
            M.Segment(NeckBase - new Vector3(0f, BodyR * 0.3f, BodyR * 0.25f), HeadBase, BodyR * 0.62f * r0, HeadR * 0.75f * r1, 8);
        }

        /// <summary>Skull + snout + nose + eyes; jaw on the Jaw bone. Returns the snout tip.</summary>
        public Vector3 Head(Color skull, Color snout, Color nose, Color eyes, float snoutLen, float snoutR = 0.5f, bool glowEyes = false, float eyeSize = 1f)
        {
            float r = HeadR;
            var c = HeadBase + new Vector3(0f, r * 0.2f, r * 0.35f);
            M.Bone = QB.Head; M.Color = skull;
            M.Sphere(c, new Vector3(r, r * 0.92f, r * 1.05f), 10, 7);
            M.Color = snout;
            var s0 = c + new Vector3(0f, -r * 0.2f, r * 0.6f);
            var s1 = s0 + new Vector3(0f, -r * 0.12f, snoutLen);
            M.Segment(s0, s1, r * snoutR * 1.1f, r * snoutR * 0.7f, 7);
            M.Color = nose;
            M.Sphere(s1 + new Vector3(0f, r * 0.08f, r * 0.02f), r * snoutR * 0.42f, 6, 4);
            // eyes
            var keepE = M.Emission; var keepJ = M.Jitter;
            M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = c + new Vector3(s * r * 0.55f, r * 0.15f, r * 0.72f);
                M.Push().Translate(e).Rotate(0f, s * 35f, 0f);
                if (glowEyes) { M.Emission = 1f; M.Color = eyes; M.Sphere(Vector3.zero, new Vector3(r * 0.2f, r * 0.15f, r * 0.12f) * eyeSize, 6, 4); }
                else
                {
                    M.Color = Paint.Shade(eyes, 0.45f);
                    M.Sphere(Vector3.zero, new Vector3(r * 0.17f, r * 0.17f, r * 0.12f) * eyeSize, 6, 4);
                    M.Color = eyes;
                    M.Sphere(new Vector3(0f, -r * 0.03f, r * 0.02f), new Vector3(r * 0.12f, r * 0.11f, r * 0.11f) * eyeSize, 6, 3);
                    M.Emission = 0.5f; M.Color = Color.white;
                    M.Sphere(new Vector3(s * r * 0.03f, r * 0.06f, r * 0.08f) * eyeSize, r * 0.04f * eyeSize, 4, 3);
                }
                M.Emission = keepE;
                M.Pop();
            }
            M.Jitter = keepJ;
            Model.HeadBone = QB.Head;
            Model.HeadTop = c + new Vector3(0f, r * 1.1f, 0f) - HeadBase;
            Model.CastBone = QB.Head;
            Model.CastOffset = s1 - HeadBase;
            return s1;
        }

        public void Jaw(Color c, float len, Color? teeth = null, float r = 0.35f)
        {
            M.Bone = QB.Jaw; M.Color = c;
            var j = Bind[QB.Jaw];
            M.Segment(j, j + new Vector3(0f, -HeadR * 0.1f, len), HeadR * r, HeadR * r * 0.6f, 6);
            if (teeth.HasValue)
            {
                M.Color = teeth.Value;
                for (int s = -1; s <= 1; s += 2)
                    M.Spike(j + new Vector3(s * HeadR * 0.16f, HeadR * 0.12f, len * 0.85f), Vector3.up, HeadR * 0.05f, HeadR * 0.16f, 4);
            }
        }

        /// <summary>Ears; leftK &lt; 1 shortens the left one (a torn ear).</summary>
        public void Ears(Color c, Color inner, float size = 1f, float spread = 0.55f, float back = 0.1f, bool round = false, bool tufts = false, float leftK = 1f)
        {
            M.Bone = QB.Head;
            float r = HeadR;
            var c0 = HeadBase + new Vector3(0f, r * 0.2f, r * 0.35f);
            float size0 = size;
            for (int s = -1; s <= 1; s += 2)
            {
                size = s < 0 ? size0 * leftK : size0;
                var at = c0 + new Vector3(s * r * spread, r * 0.72f, -r * back);
                M.Color = c;
                if (round) M.Sphere(at + Vector3.up * r * 0.1f * size, new Vector3(r * 0.28f, r * 0.28f, r * 0.14f) * size, 6, 4);
                else
                {
                    M.Aim(at, new Vector3(s * 0.25f, 1f, -0.15f));
                    M.Push().Scale(new Vector3(1f, 1f, 0.55f));
                    M.Cone(Vector3.zero, r * 0.26f * size, r * 0.62f * size, 4);
                    M.Pop();
                    M.Pop();
                    if (tufts)
                    {
                        M.Color = Paint.Shade(c, 0.4f);
                        M.Spike(at + new Vector3(s * r * 0.08f, r * 0.55f * size, -r * 0.05f), new Vector3(s * 0.2f, 1f, 0f), r * 0.04f, r * 0.25f, 3);
                    }
                }
            }
        }

        public void Leg(int u, int l, int f, Color upper, Color lower, Color paw, float rk = 1f, bool hoof = false)
        {
            float r = LegR * rk;
            bool front = u == QB.FLU || u == QB.FRU;
            M.Bone = u; M.Color = upper;
            M.Segment(Bind[u] + Vector3.up * r * 0.6f, Bind[l], r * (front ? 1.25f : 1.55f), r * 0.85f, 7);
            M.Bone = l; M.Color = lower;
            M.Sphere(Bind[l], r * 0.85f, 6, 4);
            M.Segment(Bind[l], Bind[f] + Vector3.up * r * 0.3f, r * 0.82f, r * 0.62f, 6);
            M.Bone = f; M.Color = paw;
            if (hoof) M.Cylinder(new Vector3(Bind[f].x, 0f, Bind[f].z + r * 0.15f), r * 0.75f, r * 0.62f, PawY + r * 0.5f, 6);
            else M.Paw(Bind[f], r * 1.05f);
        }

        public void Legs(Color upper, Color lower, Color paw, float rk = 1f, bool hoof = false, float backK = 1f)
        {
            Leg(QB.FLU, QB.FLL, QB.FLF, upper, lower, paw, rk, hoof);
            Leg(QB.FRU, QB.FRL, QB.FRF, upper, lower, paw, rk, hoof);
            Leg(QB.BLU, QB.BLL, QB.BLF, upper, lower, paw, rk * backK, hoof);
            Leg(QB.BRU, QB.BRL, QB.BRF, upper, lower, paw, rk * backK, hoof);
        }

        /// <summary>
        /// Tail rooted in the rump: a root on Tail1 starting inside the body, then the tail on Tail2. Bushy: the root
        /// thickens straight into one tapered brush curving down (a thin stub with a separate lozenge on it read as a
        /// detached piece on a stick), the last part in `tip`.
        /// </summary>
        public void Tail(Color c, float len, float r, Color? tip = null, bool bushy = false, float droop = 0.4f)
        {
            // the root segment is never longer than the tail itself (a bear's stub must not become a stick)
            var a = Bind[QB.Tail1];
            var dir2 = (Bind[QB.Tail2] - a).normalized;
            Bind[QB.Tail2] = a + dir2 * Mathf.Min(0.9f * BodyR, len);
            var b = Bind[QB.Tail2];
            var end = b + (dir2 + Vector3.down * droop).normalized * len;
            M.Bone = QB.Tail1; M.Color = c;
            if (bushy)
            {
                M.Segment(a - dir2 * r * 1.3f, b, r * 0.95f, r * 1.4f, 7);
                M.Bone = QB.Tail2;
                // the brush along a gentle curve: thickest a third of the way, tapering to the tip
                var ctrl = b + dir2 * len * 0.45f;
                Vector3 P(float t) => (1f - t) * (1f - t) * b + 2f * (1f - t) * t * ctrl + t * t * end;
                float[] ts = { 0f, 0.35f, 0.72f, 1f };
                float[] rs = { 1.4f, 1.65f, 1.3f, 0.3f };
                for (int i = 0; i + 1 < ts.Length; i++)
                {
                    if (i == ts.Length - 2 && tip.HasValue) M.Color = tip.Value;
                    M.Segment(P(ts[i]), P(ts[i + 1]), r * rs[i], r * rs[i + 1], 7);
                }
            }
            else
            {
                M.Segment(a - dir2 * r * 0.8f, b, r, r * 0.9f, 6);
                M.Bone = QB.Tail2;
                M.Segment(b, end, r * 0.9f, r * 0.3f, 6);
                if (tip.HasValue) { M.Color = tip.Value; M.Sphere(end, r * 0.55f, 5, 4); }
            }
        }

        /// <summary>
        /// A long tapered tail that sweeps back, rises in an S and curls over at the tip (cats): a short root on
        /// Tail1, the curve on Tail2 so it still wags and drags.
        /// </summary>
        public void CurlTail(Color c, float len, float r, Color? tip = null)
        {
            var a = Bind[QB.Tail1];
            var back = Vector3.back;
            Bind[QB.Tail2] = a + (back * 0.8f + Vector3.down * 0.25f).normalized * Mathf.Min(0.6f * BodyR, len * 0.18f);
            var b = Bind[QB.Tail2];
            M.Bone = QB.Tail1; M.Color = c;
            M.Segment(a, b, r, r * 0.95f, 6);
            M.Bone = QB.Tail2;
            // low arc back, then up, then the tip curling forward over the back
            var p1 = b + back * len * 0.38f + Vector3.up * len * 0.05f;
            var p2 = p1 + back * len * 0.1f + Vector3.up * len * 0.36f;
            var p3 = p2 + Vector3.up * len * 0.1f + Vector3.forward * len * 0.12f;
            M.Curve(b, b + back * len * 0.24f + Vector3.down * len * 0.06f, p1, r * 0.95f, r * 0.8f, 3, 6);
            M.Curve(p1, p1 + back * len * 0.16f + Vector3.up * len * 0.08f, p2, r * 0.8f, r * 0.62f, 3, 6);
            M.Curve(p2, p2 + Vector3.up * len * 0.12f, p3, r * 0.62f, r * 0.42f, 2, 6);
            if (tip.HasValue) { M.Color = tip.Value; M.Sphere(p3, r * 0.55f, 6, 4); }
        }

        /// <summary>
        /// Dragon wings on QB.WingL/R (the arm from the shoulder to the wrist and the membrane from it down to the flank)
        /// and QB.WingL2/R2 (the fingers fanning from the wrist and the scalloped membrane between them), authored spread:
        /// `span` metres from the shoulder to the leading finger tip, `chord` stretches the wing towards the tail. Both
        /// faces of the membrane are drawn with a little thickness (top `membrane`, underside `under`) so the ink outline
        /// keeps it one shape edge-on; `tatter` (0 … 1) notches the trailing edge (old, battle-torn wings). The animator
        /// folds them at rest (UnitModel.WingFold), spreads them in roars and rearing attacks and beats them when the
        /// model hovers (FloatHeight &gt; 0). Call after Body (the shoulders sit on the torso).
        /// </summary>
        public void Wings(float span, Color arm, Color membrane, Color under, Color claw, int fingers = 4, float chord = 1f,
                          float tatter = 0f, float armR = 0.035f, Color? edge = null)
        {
            fingers = Mathf.Clamp(fingers, 2, 5);
            float zc = Bind[QB.Chest].z - BodyR * 0.1f;
            float zr = Bind[QB.Hips].z + BodyR * 0.15f;
            float keepJ = M.Jitter;
            for (int s = -1; s <= 1; s += 2)
            {
                int wb = QB.Wing(s), wo = QB.Wing2(s);
                var w0 = new Vector3(s * BodyR * 0.42f, TopY(zc) - BodyR * 0.12f, zc);
                Vector3 P(float x, float y, float z) => w0 + new Vector3(s * x * span, y * span, z * span);
                var elbow = P(0.3f, 0.17f, 0.1f);
                var wrist = P(0.56f, 0.26f, 0.06f);
                Bind[wb] = w0;
                Bind[wo] = wrist;
                var tips = new Vector3[fingers];
                for (int i = 0; i < fingers; i++)
                {
                    float t = (float)i / (fingers - 1);
                    tips[i] = P(Mathf.Lerp(1.0f, 0.42f, t * t * 0.6f + t * 0.4f), Mathf.Lerp(0.3f, -0.07f, Mathf.Sqrt(t)), -Mathf.Lerp(0.04f, 0.82f, t) * chord);
                }
                var root = new Vector3(s * BodyR * 0.62f, TopY(zr) - BodyR * 0.3f, zr);
                var last = Vector3.Lerp(wrist, tips[fingers - 1], 0.58f);

                // membrane (two faces with a little thickness, plus the rim)
                M.Jitter = keepJ * 0.4f;
                float th = span * 0.012f;
                void Tri(Vector3 a, Vector3 b, Vector3 c)
                {
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 1e-12f) return;
                    n.Normalize();
                    if (n.y < 0f) n = -n;
                    var h = n * th;
                    M.Color = membrane; M.TriangleFacing(a + h, b + h, c + h, n);
                    M.Color = under; M.TriangleFacing(a - h, b - h, c - h, -n);
                }
                void Rim(Vector3 a, Vector3 b, Vector3 inside)
                {
                    var n = Vector3.Cross(b - a, inside - a);
                    if (n.sqrMagnitude < 1e-12f) return;
                    n.Normalize();
                    if (n.y < 0f) n = -n;
                    var h = n * th;
                    var o = Vector3.Cross(b - a, n);
                    if (Vector3.Dot(o, inside - a) > 0f) o = -o;
                    M.Color = edge ?? under;
                    M.Quad(a + h, b + h, b - h, a - h, o);
                }
                Vector3 Scallop(Vector3 a, Vector3 b, Vector3 towards, float k) => Vector3.Lerp(Vector3.Lerp(a, b, 0.5f), towards, k);

                // inner membrane: shoulder → elbow → wrist → along the last finger → scalloped edge → flank
                M.Bone = wb;
                var midIn = Scallop(last, root, elbow, 0.22f);
                var midIn2 = Scallop(midIn, root, elbow, 0.12f);
                Tri(w0, elbow, wrist);
                Tri(w0, wrist, last);
                Tri(w0, last, midIn);
                Tri(w0, midIn, midIn2);
                Tri(w0, midIn2, root);
                Rim(last, midIn, w0); Rim(midIn, midIn2, w0); Rim(midIn2, root, w0);
                // arm: humerus, forearm, the wrist knuckle and its thumb claw
                M.Jitter = keepJ;
                M.Color = arm;
                float r = armR * span;
                M.Segment(w0 - (elbow - w0).normalized * r, elbow, r * 1.25f, r, 6);
                M.Sphere(elbow, r * 1.05f, 6, 4);
                M.Segment(elbow, wrist, r, r * 0.8f, 6);
                M.Color = claw;
                M.Spike(wrist, (wrist - elbow).normalized + Vector3.up * 0.6f + Vector3.forward * 0.4f, r * 0.7f, r * 3.2f, 5);

                // outer membrane: a fan between the fingers, each gap scalloped (and torn when tattered)
                M.Bone = wo;
                M.Jitter = keepJ * 0.4f;
                for (int i = 0; i + 1 < fingers; i++)
                {
                    float k = 0.3f + (tatter > 0f && (i & 1) == 1 ? 0.18f * tatter : 0f);
                    var mid = Scallop(tips[i], tips[i + 1], wrist, k);
                    Tri(wrist, tips[i], mid);
                    Tri(wrist, mid, tips[i + 1]);
                    Rim(tips[i], mid, wrist); Rim(mid, tips[i + 1], wrist);
                    if (tatter > 0f && (i & 1) == 0)
                    {
                        // a torn notch: a small dark triangle on the edge reads as a hole from above
                        M.Color = Paint.Shade(under, 0.55f);
                        var n0 = Vector3.Lerp(tips[i], mid, 0.45f);
                        var n1 = Vector3.Lerp(n0, wrist, 0.12f * tatter);
                        var n2 = Vector3.Lerp(tips[i], mid, 0.7f);
                        var nn = Vector3.Cross(n1 - n0, n2 - n0).normalized;
                        if (nn.y < 0f) nn = -nn;
                        M.TriangleFacing(n0 + nn * th * 1.6f, n1 + nn * th * 1.6f, n2 + nn * th * 1.6f, nn);
                    }
                }
                M.Jitter = keepJ;
                M.Color = arm;
                M.Sphere(wrist, r * 0.95f, 6, 4);
                for (int i = 0; i < fingers; i++)
                {
                    var bend = Vector3.Lerp(wrist, tips[i], 0.5f) + Vector3.up * span * 0.03f;
                    M.Curve(wrist, bend, tips[i], r * (i == 0 ? 0.85f : 0.62f), r * 0.16f, 3, 5);
                }
                M.Color = claw;
                M.Spike(tips[0], (tips[0] - wrist).normalized, r * 0.4f, r * 2.2f, 4);
            }
            M.Jitter = keepJ;
            Model.Wings = true;
        }

        /// <summary>
        /// A long tail on three bones (Tail1 root in the rump, Tail2, Tail3 tip) sweeping back and down towards the ground
        /// and lifting a little at the tip; returns the tip point. `r` is the root radius; `flat` &gt; 1 widens it sideways
        /// (crocolisks). Optional `ridge` scutes/spines run along its top, `under` is a paler belly strip.
        /// </summary>
        public Vector3 LongTail(Color c, float len, float r, float flat = 1f, float droop = 0.5f, Color? ridge = null, float ridgeH = 0f, Color? under = null)
        {
            var a = Bind[QB.Tail1];
            var dir = new Vector3(0f, -Mathf.Clamp01(droop) * 0.55f, -1f).normalized;
            var b = a + dir * len * 0.34f;
            var dir2 = new Vector3(0f, -Mathf.Clamp01(droop) * 0.35f, -1f).normalized;
            var cpt = b + dir2 * len * 0.33f;
            cpt.y = Mathf.Max(cpt.y, r * 0.55f);
            var tip = cpt + new Vector3(0f, r * 0.4f, -len * 0.33f);
            tip.y = Mathf.Max(tip.y, r * 0.45f);
            Bind[QB.Tail2] = b;
            Bind[QB.Tail3] = cpt;
            Model.LongTail = true;
            float[] rs = { r, r * 0.72f, r * 0.45f, r * 0.08f };
            Vector3[] ps = { a - dir * r * 0.9f, b, cpt, tip };
            int[] bones = { QB.Tail1, QB.Tail2, QB.Tail3 };
            for (int i = 0; i < 3; i++)
            {
                M.Bone = bones[i];
                M.Color = c;
                var mid = Vector3.Lerp(ps[i], ps[i + 1], 0.5f);
                M.Push().Translate(mid).Scale(new Vector3(flat, 1f, 1f)).Translate(-mid);
                M.Segment(ps[i], ps[i + 1], rs[i], rs[i + 1], 7);
                if (i < 2) M.Sphere(ps[i + 1], rs[i + 1], 7, 4);
                M.Pop();
                if (under.HasValue)
                {
                    M.Color = under.Value;
                    M.Segment(ps[i] + Vector3.down * rs[i] * 0.35f, ps[i + 1] + Vector3.down * rs[i + 1] * 0.35f, rs[i] * 0.78f * flat, rs[i + 1] * 0.78f * flat, 6);
                }
                if (ridge.HasValue && ridgeH > 0f)
                {
                    M.Color = ridge.Value;
                    for (int j = 0; j < 3; j++)
                    {
                        float t = (j + 0.5f) / 3f;
                        var p = Vector3.Lerp(ps[i], ps[i + 1], t);
                        float rr = Mathf.Lerp(rs[i], rs[i + 1], t);
                        M.Aim(p + Vector3.up * rr * 0.8f, new Vector3(0f, 1f, -0.6f));
                        M.Push().Scale(new Vector3(0.45f, 1f, 1.3f));
                        M.Cone(Vector3.zero, rr * 0.5f + ridgeH * 0.2f, ridgeH * Mathf.Lerp(1f, 0.4f, (i + t) / 3f), 4);
                        M.Pop();
                        M.Pop();
                    }
                }
            }
            return tip;
        }

        public void Finish(string key, float height, UnitStrike strike = UnitStrike.Bite, UnitRanged ranged = UnitRanged.Howl)
        {
            var m = Model;
            m.Key = key;
            m.Rig = UnitRigKind.Quad;
            m.Gait = UnitGait.Quad;
            m.Bind = Bind;
            m.Parent = QB.Parent;
            m.Names = QB.Names;
            m.Height = height;
            m.Radius = Mathf.Max(0.22f, BodyR * 1.15f + LegX * 0.3f);
            m.HalfLength = BodyLen * 0.5f + BodyR * 0.3f;
            m.HipY = HipY;
            m.LegLength = JointB;
            m.LieHeight = BodyR * 0.85f;
            m.Strike = strike;
            m.Ranged = ranged;
            m.TurnRate = 540f;
            m.MaxCadence = 3.4f;
            m.CenterBone = QB.Chest;
            m.CenterOffset = new Vector3(0f, 0f, -BodyLen * 0.35f);
            m.PickBones = m.Wings
                ? new[] { QB.Head, QB.Chest, QB.Hips, QB.FLF, QB.FRF, QB.BLF, QB.BRF, QB.Tail2, QB.WingL2, QB.WingR2 }
                : new[] { QB.Head, QB.Chest, QB.Hips, QB.FLF, QB.FRF, QB.BLF, QB.BRF, QB.Tail2 };
            m.PickPad = BodyR * 0.6f;
            int[,] legs = { { QB.FLU, QB.FLL, QB.FLF, QB.Chest, -1 }, { QB.FRU, QB.FRL, QB.FRF, QB.Chest, 1 }, { QB.BLU, QB.BLL, QB.BLF, QB.Hips, -1 }, { QB.BRU, QB.BRL, QB.BRF, QB.Hips, 1 } };
            m.Legs = new UnitLeg[4];
            for (int i = 0; i < 4; i++)
            {
                int u = legs[i, 0], l = legs[i, 1], f = legs[i, 2];
                m.Legs[i] = new UnitLeg
                {
                    Upper = u, Lower = l, Foot = f, Root = legs[i, 3], Side = legs[i, 4],
                    A = (Bind[l] - Bind[u]).magnitude, B = (Bind[f] - Bind[l]).magnitude,
                    Rest = Bind[f],
                    Pole = Vector3.back,
                    // diagonal pairs: front-left + back-right, front-right + back-left
                    Phase = (i == 0 || i == 3) ? 0f : 0.5f,
                };
            }
        }
    }

    public sealed class SpiderKit
    {
        public readonly MeshBuilder M;
        public readonly UnitModel Model = new UnitModel();
        public readonly Vector3[] Bind = new Vector3[SB.Count];
        public readonly float BodyY, BodyR, Span;
        readonly Vector3[] feet = new Vector3[8];
        readonly float[] legA = new float[8], legB = new float[8];

        public SpiderKit(int seed, float bodyY, float bodyR, float span)
        {
            M = new MeshBuilder(seed) { Jitter = 0.06f };
            BodyY = bodyY; BodyR = bodyR; Span = span;
            Bind[SB.Body] = new Vector3(0f, bodyY, 0f);
            Bind[SB.Abdomen] = new Vector3(0f, bodyY + bodyR * 0.15f, -bodyR * 0.8f);
            Bind[SB.Fangs] = new Vector3(0f, bodyY - bodyR * 0.2f, bodyR * 0.85f);
            // leg i: 0..3 left (front → back), 4..7 right. Splayed wide with the knees arched high above the body, so
            // the eight legs stand out of the silhouette at game zoom (not two balls on twigs). The arch is what the
            // leg IK solves to (pole: out + up); the leg segments themselves are authored hanging straight down from
            // their joints (−Y), the bone convention UnitAnimator.SolveIK/BoneRot rotates from — authoring them along
            // the arch instead turned them inside out (folded under the body).
            float[] ang = { 34f, 72f, 110f, 150f };
            for (int i = 0; i < 8; i++)
            {
                int side = i < 4 ? -1 : 1;
                float a = ang[i % 4] * Mathf.Deg2Rad;
                var outDir = new Vector3(side * Mathf.Sin(a), 0f, Mathf.Cos(a));
                var hip = Bind[SB.Body] + outDir * bodyR * 0.75f + Vector3.up * bodyR * 0.05f;
                var foot = new Vector3(0f, 0.02f, 0f) + new Vector3(outDir.x, 0f, outDir.z) * span;
                var knee = hip + outDir * (span - bodyR * 0.75f) * 0.4f + Vector3.up * bodyY * 1.2f;
                legA[i] = (knee - hip).magnitude;
                legB[i] = (foot - knee).magnitude;
                Bind[SB.Upper(i)] = hip;
                Bind[SB.Lower(i)] = hip + Vector3.down * legA[i];
                feet[i] = foot;
            }
        }

        public void Build(Color body, Color abdomen, Color pattern, Color legs, Color band, Color eyes, Color moss)
        {
            M.Bone = SB.Body; M.Color = body;
            M.Sphere(Bind[SB.Body] + new Vector3(0f, 0f, BodyR * 0.2f), new Vector3(BodyR * 0.8f, BodyR * 0.62f, BodyR * 0.9f), 9, 6, false);
            // eyes cluster
            var keepE = M.Emission;
            M.Emission = 0.85f; M.Color = eyes;
            var front = Bind[SB.Body] + new Vector3(0f, BodyR * 0.25f, BodyR * 0.95f);
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 3 - 1) * BodyR * 0.18f, y = (i / 3) * BodyR * 0.14f;
                M.Sphere(front + new Vector3(x, y, -Mathf.Abs(x) * 0.5f), BodyR * (i == 1 ? 0.11f : 0.07f), 5, 3);
            }
            M.Emission = keepE;
            M.Bone = SB.Fangs; M.Color = Paint.Shade(body, 0.6f);
            for (int s = -1; s <= 1; s += 2)
                M.Curve(Bind[SB.Fangs] + new Vector3(s * BodyR * 0.15f, 0f, 0f), Bind[SB.Fangs] + new Vector3(s * BodyR * 0.2f, -BodyR * 0.25f, BodyR * 0.2f),
                        Bind[SB.Fangs] + new Vector3(s * BodyR * 0.08f, -BodyR * 0.45f, BodyR * 0.1f), BodyR * 0.08f, BodyR * 0.02f, 3, 4);
            M.Bone = SB.Abdomen; M.Color = abdomen;
            var ab = Bind[SB.Abdomen] + new Vector3(0f, BodyR * 0.25f, -BodyR * 0.75f);
            M.Sphere(ab, new Vector3(BodyR * 1.05f, BodyR * 0.95f, BodyR * 1.3f), 10, 7, false);
            M.Color = pattern;
            for (int i = 0; i < 3; i++)
                M.Sphere(ab + new Vector3(0f, BodyR * 0.82f, BodyR * (0.5f - i * 0.45f)), new Vector3(BodyR * 0.32f, BodyR * 0.12f, BodyR * 0.2f), 6, 3);
            M.Color = moss;
            M.Blob(ab + new Vector3(BodyR * 0.25f, BodyR * 0.85f, -BodyR * 0.3f), new Vector3(0.45f, 0.18f, 0.5f) * BodyR, 1, 0.3f, 4);
            // legs: thick enough to read at game zoom (1.6× the old radii), light bands at the knees and shins
            const float lk = 1.6f;
            var bandL = Color.Lerp(band, Color.white, 0.25f);
            for (int i = 0; i < 8; i++)
            {
                // authored hanging down (see the constructor); the animator's IK swings them into the arch
                var knee = Bind[SB.Lower(i)];
                var foot = knee + Vector3.down * legB[i];
                M.Bone = SB.Upper(i); M.Color = legs;
                M.Segment(Bind[SB.Upper(i)], knee, BodyR * 0.15f * lk, BodyR * 0.11f * lk, 6);
                M.Bone = SB.Lower(i);
                M.Color = bandL;
                M.Sphere(knee, BodyR * 0.135f * lk, 6, 4);
                M.Color = legs;
                var mid = Vector3.Lerp(knee, foot, 0.5f);
                M.Segment(knee, mid, BodyR * 0.11f * lk, BodyR * 0.09f * lk, 6);
                M.Color = bandL;
                M.Segment(mid, mid + (foot - mid) * 0.18f, BodyR * 0.1f * lk, BodyR * 0.09f * lk, 6);
                M.Color = legs;
                M.Segment(mid + (foot - mid) * 0.18f, foot, BodyR * 0.09f * lk, BodyR * 0.035f * lk, 5);
            }
        }

        public void Finish(string key, float height)
        {
            var m = Model;
            m.Key = key;
            m.Rig = UnitRigKind.Spider;
            m.Gait = UnitGait.Spider;
            m.Bind = Bind;
            m.Parent = SB.Parent;
            m.Names = SB.Names;
            m.Height = height;
            m.Radius = Span * 0.6f;
            m.HalfLength = BodyR * 0.4f;
            m.HipY = BodyY;
            m.LegLength = BodyY * 1.6f;
            m.LieHeight = BodyR * 0.55f;
            m.Strike = UnitStrike.Fangs;
            m.Ranged = UnitRanged.Pulse;
            m.MaxCadence = 4.2f;
            m.TurnRate = 600f;
            m.HeadBone = SB.Body;
            m.HeadTop = new Vector3(0f, BodyR * 1.2f, -BodyR * 0.3f);
            m.CenterBone = SB.Body;
            m.CenterOffset = Vector3.zero;
            m.CastBone = SB.Fangs;
            m.CastOffset = Vector3.zero;
            m.PickBones = new[] { SB.Body, SB.Abdomen, SB.Lower(0), SB.Lower(3), SB.Lower(4), SB.Lower(7) };
            m.PickPad = BodyR * 0.5f;
            m.Legs = new UnitLeg[8];
            for (int i = 0; i < 8; i++)
            {
                int side = i < 4 ? -1 : 1;
                int k = i % 4;
                var hip = Bind[SB.Upper(i)];
                var knee = Bind[SB.Lower(i)];
                var outDir = new Vector3(hip.x, 0f, hip.z - Bind[SB.Body].z).normalized;
                // alternating tetrapod: L0 R1 L2 R3 | R0 L1 R2 L3
                bool groupA = side < 0 ? (k % 2 == 0) : (k % 2 == 1);
                m.Legs[i] = new UnitLeg
                {
                    Upper = SB.Upper(i), Lower = SB.Lower(i), Foot = -1, Root = SB.Body, Side = side,
                    A = legA[i], B = legB[i],
                    Rest = feet[i],
                    Pole = (outDir * 0.5f + Vector3.up).normalized,
                    Phase = groupA ? 0f : 0.5f,
                };
            }
            // put the front pair first (L0, R0): they rear up in attacks
            var r0 = m.Legs[4];
            m.Legs[4] = m.Legs[1];
            m.Legs[1] = r0;
        }
    }

    public sealed class StaticKit
    {
        public readonly MeshBuilder M;
        public readonly UnitModel Model = new UnitModel();
        public readonly Vector3[] Bind = new Vector3[TB.Count];

        public StaticKit(int seed, float topY)
        {
            M = new MeshBuilder(seed) { Jitter = 0.06f, AOStrength = 0.25f, AOHeight = 0.4f };
            Bind[TB.Base] = Vector3.zero;
            Bind[TB.Top] = new Vector3(0f, topY, 0f);
        }

        public void Finish(string key, float height, float radius, bool pop)
        {
            var m = Model;
            m.Key = key;
            m.Rig = UnitRigKind.Static;
            m.Gait = UnitGait.Static;
            m.Bind = Bind;
            m.Parent = TB.Parent;
            m.Names = TB.Names;
            m.Height = height;
            m.Radius = radius;
            m.HipY = 0f;
            m.LegLength = height * 0.5f;
            m.Static = true;
            m.SpawnPop = pop;
            m.Dust = false;
            m.TurnRate = 0f;
            m.HeadBone = TB.Top;
            m.HeadTop = new Vector3(0f, height - Bind[TB.Top].y, 0f);
            m.CenterBone = TB.Top;
            m.CenterOffset = new Vector3(0f, (height - Bind[TB.Top].y) * 0.4f, 0f);
            m.CastBone = TB.Top;
            m.CastOffset = new Vector3(0f, height - Bind[TB.Top].y, 0f);
            m.PickBones = new[] { TB.Base, TB.Top };
            m.PickPad = radius;
        }
    }
}
