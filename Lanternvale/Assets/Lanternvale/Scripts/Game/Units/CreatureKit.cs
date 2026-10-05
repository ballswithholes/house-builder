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

        public void Body(Color back, Color belly, float rearK = 0.95f, float frontK = 1.05f, float hump = 0f)
        {
            M.Bone = QB.Hips; M.Color = back;
            var rear = Bind[QB.Hips] + new Vector3(0f, 0f, BodyLen * 0.08f);
            M.Sphere(rear, new Vector3(BodyR * rearK, BodyR * rearK * 0.95f, BodyLen * 0.48f), 10, 7, false);
            M.Bone = QB.Chest;
            var front = Bind[QB.Chest] + new Vector3(0f, hump * BodyR * 0.3f, -BodyLen * 0.12f);
            M.Sphere(front, new Vector3(BodyR * frontK, BodyR * frontK * (1f + hump * 0.25f), BodyLen * 0.52f), 10, 7, false);
            M.Color = belly;
            M.Sphere(front + new Vector3(0f, -BodyR * 0.35f, BodyLen * 0.12f), new Vector3(BodyR * frontK * 0.82f, BodyR * 0.7f, BodyLen * 0.38f), 8, 5, false);
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

        public void Ears(Color c, Color inner, float size = 1f, float spread = 0.55f, float back = 0.1f, bool round = false, bool tufts = false)
        {
            M.Bone = QB.Head;
            float r = HeadR;
            var c0 = HeadBase + new Vector3(0f, r * 0.2f, r * 0.35f);
            for (int s = -1; s <= 1; s += 2)
            {
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

        /// <summary>Tail in two segments (bushy = blob tuft at the end).</summary>
        public void Tail(Color c, float len, float r, Color? tip = null, bool bushy = false, float droop = 0.4f)
        {
            var a = Bind[QB.Tail1];
            var b = Bind[QB.Tail2];
            var dir2 = (b - a).normalized;
            M.Bone = QB.Tail1; M.Color = c;
            M.Segment(a, b, r, r * 0.9f, 6);
            M.Bone = QB.Tail2;
            var end = b + (dir2 + Vector3.down * droop).normalized * len;
            if (bushy)
            {
                M.Blob((b + end) * 0.5f, new Vector3(r * 1.5f, r * 1.5f, len * 0.6f), 1, 0.18f, 7);
                if (tip.HasValue) { M.Color = tip.Value; M.Blob(end, new Vector3(r * 0.9f, r * 0.9f, r * 1.3f), 0, 0.2f, 9); }
            }
            else
            {
                M.Segment(b, end, r * 0.9f, r * 0.3f, 6);
                if (tip.HasValue) { M.Color = tip.Value; M.Sphere(end, r * 0.55f, 5, 4); }
            }
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
            m.PickBones = new[] { QB.Head, QB.Chest, QB.Hips, QB.FLF, QB.FRF, QB.BLF, QB.BRF, QB.Tail2 };
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

        public SpiderKit(int seed, float bodyY, float bodyR, float span)
        {
            M = new MeshBuilder(seed) { Jitter = 0.06f };
            BodyY = bodyY; BodyR = bodyR; Span = span;
            Bind[SB.Body] = new Vector3(0f, bodyY, 0f);
            Bind[SB.Abdomen] = new Vector3(0f, bodyY + bodyR * 0.15f, -bodyR * 0.8f);
            Bind[SB.Fangs] = new Vector3(0f, bodyY - bodyR * 0.2f, bodyR * 0.85f);
            // leg i: 0..3 left (front → back), 4..7 right
            float[] ang = { 38f, 78f, 108f, 145f };
            for (int i = 0; i < 8; i++)
            {
                int side = i < 4 ? -1 : 1;
                float a = ang[i % 4] * Mathf.Deg2Rad;
                var outDir = new Vector3(side * Mathf.Sin(a), 0f, Mathf.Cos(a));
                var hip = Bind[SB.Body] + outDir * bodyR * 0.75f + Vector3.up * bodyR * 0.05f;
                var foot = new Vector3(0f, 0.02f, 0f) + new Vector3(outDir.x, 0f, outDir.z) * span;
                var knee = hip + outDir * (span - bodyR * 0.75f) * 0.45f + Vector3.up * bodyY * 0.85f;
                Bind[SB.Upper(i)] = hip;
                Bind[SB.Lower(i)] = knee;
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
            for (int i = 0; i < 8; i++)
            {
                M.Bone = SB.Upper(i); M.Color = legs;
                M.Segment(Bind[SB.Upper(i)], Bind[SB.Lower(i)], BodyR * 0.14f, BodyR * 0.11f, 5);
                M.Bone = SB.Lower(i);
                M.Color = band;
                M.Sphere(Bind[SB.Lower(i)], BodyR * 0.13f, 5, 3);
                M.Color = legs;
                var mid = Vector3.Lerp(Bind[SB.Lower(i)], feet[i], 0.5f);
                M.Segment(Bind[SB.Lower(i)], mid, BodyR * 0.11f, BodyR * 0.09f, 5);
                M.Color = band;
                M.Segment(mid, mid + (feet[i] - mid) * 0.15f, BodyR * 0.1f, BodyR * 0.09f, 5);
                M.Color = legs;
                M.Segment(mid + (feet[i] - mid) * 0.15f, feet[i], BodyR * 0.09f, BodyR * 0.03f, 5);
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
            m.Radius = Span * 0.7f;
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
                    A = (knee - hip).magnitude, B = (feet[i] - knee).magnitude,
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
