// Humanoid modelling kit: proportions + body, face, hair, garments, armour and held items for the biped rig.
//
// FFX-inspired stylised proportions (≈ 6 heads, slightly large heads and hands), built in the bind pose: standing,
// arms hanging straight down, facing +Z, Y up, feet on y = 0. Each part is bound rigidly to one bone (M.Bone).
// Held items are authored along the HAND's +Z ("out of the front of the fist"); the animator rotates the hand to hold
// them (staff upright, sword forward-down, bow vertical when drawn …).
using UnityEngine;

namespace Lanternvale.Game
{
    public enum EyeStyle { Round, Narrow, Closed, Glow, Scar, None }

    public sealed class BipedKit
    {
        public readonly MeshBuilder M;
        public readonly UnitModel Model = new UnitModel();
        public readonly Vector3[] Bind = new Vector3[BB.Count];

        // proportions (metres)
        public readonly float H, U, R;
        public readonly float HeadCY, HeadY, NeckY, ShoulderY, ShoulderX, ChestY, SpineY, PelvisY, HipY, HipX, KneeY, AnkleY, ElbowY, WristY;
        public readonly float Bulk;
        public readonly bool Female;
        public float WaistR, ChestR, ShoulderR, HipR, DepthK = 0.74f;
        public float ArmR, ForeR, ThighR, ShinR, HandK = 1.12f, FootLen, FootW;
        public Color Skin = Paint.Hex("#f2cfae");

        public BipedKit(int seed, float height, float headR, float legFrac = 0.49f, float bulk = 1f, bool female = false,
                        float shoulderK = 1f, float armK = 1f)
        {
            M = new MeshBuilder(seed) { Jitter = 0.05f };
            H = height; U = height / 1.75f; R = headR; Bulk = bulk; Female = female;
            HeadCY = H - R * 1.03f;
            HeadY = HeadCY - R * 0.9f;
            NeckY = HeadY - 0.045f * U;
            ShoulderY = NeckY - 0.04f * U;
            HipY = H * legFrac;
            AnkleY = 0.075f * U;
            KneeY = AnkleY + (HipY - AnkleY) * 0.5f + 0.005f * U;
            PelvisY = HipY + 0.055f * U;
            float torso = ShoulderY - HipY;
            SpineY = HipY + torso * 0.32f;
            ChestY = HipY + torso * 0.6f;
            float arm = torso * armK;
            ElbowY = ShoulderY - arm * 0.53f;
            WristY = ShoulderY - arm;
            float w = female ? 0.168f : 0.198f;
            ShoulderX = w * U * shoulderK * Mathf.Lerp(1f, bulk, 0.6f);
            HipX = (female ? 0.094f : 0.088f) * U * Mathf.Lerp(1f, bulk, 0.5f);

            float b = U * bulk;
            WaistR = (female ? 0.112f : 0.128f) * b;
            ChestR = (female ? 0.142f : 0.163f) * U * Mathf.Lerp(1f, bulk, 0.8f) * shoulderK;
            ShoulderR = (female ? 0.138f : 0.168f) * U * Mathf.Lerp(1f, bulk, 0.8f) * shoulderK;
            HipR = (female ? 0.146f : 0.132f) * b;
            ArmR = (female ? 0.045f : 0.052f) * b;
            ForeR = (female ? 0.037f : 0.043f) * b;
            ThighR = (female ? 0.074f : 0.078f) * b;
            ShinR = (female ? 0.05f : 0.055f) * b;
            FootLen = 0.25f * U * Mathf.Lerp(1f, bulk, 0.4f);
            FootW = 0.1f * U * Mathf.Lerp(1f, bulk, 0.5f);

            Bind[BB.Hips] = new Vector3(0f, PelvisY, 0f);
            Bind[BB.Spine] = new Vector3(0f, SpineY, 0f);
            Bind[BB.Chest] = new Vector3(0f, ChestY, 0f);
            Bind[BB.Neck] = new Vector3(0f, NeckY, 0f);
            Bind[BB.Head] = new Vector3(0f, HeadY, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                Bind[BB.ArmU(s)] = new Vector3(s * ShoulderX, ShoulderY, 0f);
                Bind[BB.ArmL(s)] = new Vector3(s * ShoulderX, ElbowY, 0f);
                Bind[BB.Hand(s)] = new Vector3(s * ShoulderX, WristY, 0f);
                Bind[BB.LegU(s)] = new Vector3(s * HipX, HipY, 0f);
                Bind[BB.LegL(s)] = new Vector3(s * HipX, KneeY, 0f);
                Bind[BB.Foot(s)] = new Vector3(s * HipX, AnkleY, 0f);
            }
            Bind[BB.SkirtL] = new Vector3(-HipX, HipY + 0.03f * U, 0.01f * U);
            Bind[BB.SkirtR] = new Vector3(HipX, HipY + 0.03f * U, 0.01f * U);
            Bind[BB.SkirtB] = new Vector3(0f, HipY + 0.07f * U, -0.06f * U);
            Bind[BB.SkirtF] = new Vector3(0f, HipY + 0.07f * U, 0.07f * U);
            Bind[BB.Cape] = new Vector3(0f, ShoulderY + 0.01f * U, -ChestR * DepthK - 0.01f * U);
            Bind[BB.HairB] = new Vector3(0f, HeadCY + 0.15f * R, -0.82f * R);
            Bind[BB.Tail] = new Vector3(0f, PelvisY - 0.05f * U, -HipR * DepthK);
            Bind[BB.WingL] = new Vector3(-0.07f * U, ShoulderY - 0.07f * U, -ChestR * DepthK);
            Bind[BB.WingR] = new Vector3(0.07f * U, ShoulderY - 0.07f * U, -ChestR * DepthK);
        }

        public Vector3 Grip(int side) => new Vector3(side * ShoulderX, WristY - 0.05f * U * HandK, 0.012f * U);

        // ================================================================== body

        public void Torso(Color top, Color pelvis, Color? belly = null, float bellyK = 0f)
        {
            float u = U;
            M.Bone = BB.Hips; M.Color = pelvis;
            M.Body(Vector3.zero, new[]
            {
                new Vector2(HipR * 0.82f, HipY - 0.085f * u), new Vector2(HipR, HipY - 0.02f * u),
                new Vector2(HipR * 0.98f, PelvisY + 0.02f * u), new Vector2(WaistR * 0.98f, PelvisY + 0.06f * u),
            }, 8, DepthK * 1.06f, true, false);
            M.Bone = BB.Spine; M.Color = belly ?? top;
            float bk = 1f + bellyK;
            M.Body(Vector3.zero, new[]
            {
                new Vector2(WaistR * 1.0f * bk, PelvisY - 0.01f * u), new Vector2(WaistR * 1.04f * bk, (PelvisY + SpineY) * 0.5f + 0.02f * u),
                new Vector2(WaistR * 1.02f * Mathf.Lerp(1f, bk, 0.6f), SpineY + 0.06f * u),
            }, 8, DepthK * (1f + bellyK * 0.6f), false, false);
            M.Bone = BB.Chest; M.Color = top;
            M.Body(Vector3.zero, new[]
            {
                new Vector2(WaistR * 1.0f, SpineY), new Vector2(ChestR, ChestY), new Vector2(ChestR * 1.02f, ShoulderY - 0.07f * u),
                new Vector2(ShoulderR, ShoulderY - 0.012f * u), new Vector2(0.075f * u, NeckY + 0.005f * u),
            }, 8, DepthK, false, true);
        }

        public void Neck(Color c)
        {
            M.Bone = BB.Neck; M.Color = c;
            M.Segment(new Vector3(0f, ShoulderY - 0.02f * U, -0.005f * U), new Vector3(0f, HeadY + 0.05f * U, 0f), 0.05f * U * Mathf.Lerp(1f, Bulk, 0.7f), 0.045f * U, 7);
        }

        public void Arm(int side, Color upper, Color lower, Color hand, bool shoulderBall = true)
        {
            float x = side * ShoulderX;
            M.Bone = BB.ArmU(side); M.Color = upper;
            if (shoulderBall) M.Sphere(new Vector3(x, ShoulderY - 0.012f * U, 0f), ArmR * 1.15f, 7, 5);
            M.Segment(new Vector3(x, ShoulderY, 0f), new Vector3(x, ElbowY, 0f), ArmR, ArmR * 0.86f, 7);
            M.Bone = BB.ArmL(side); M.Color = lower;
            M.Sphere(new Vector3(x, ElbowY, 0f), ForeR * 1.06f, 6, 4);
            M.Segment(new Vector3(x, ElbowY, 0f), new Vector3(x, WristY + 0.008f * U, 0f), ForeR, ForeR * 0.82f, 7);
            Fist(side, hand);
        }

        public void Fist(int side, Color hand)
        {
            float x = side * ShoulderX;
            M.Bone = BB.Hand(side); M.Color = hand;
            float k = U * HandK * Mathf.Lerp(1f, Bulk, 0.5f);
            M.Sphere(new Vector3(x, WristY - 0.045f * k, 0.008f * k), new Vector3(0.037f * k, 0.05f * k, 0.044f * k), 7, 5);
            M.Sphere(new Vector3(x - side * 0.026f * k, WristY - 0.035f * k, 0.03f * k), new Vector3(0.014f * k, 0.022f * k, 0.014f * k), 4, 3);
        }

        /// <summary>A cuff/bracer ring on the forearm (gloves, bracers, sleeve ends).</summary>
        public void Cuff(int side, Color c, float from = 0.45f, float to = 0.95f, float k = 1.18f)
        {
            float x = side * ShoulderX;
            M.Bone = BB.ArmL(side); M.Color = c;
            float y0 = Mathf.Lerp(ElbowY, WristY, from), y1 = Mathf.Lerp(ElbowY, WristY, to);
            float r0 = Mathf.Lerp(ForeR, ForeR * 0.82f, from) * k, r1 = Mathf.Lerp(ForeR, ForeR * 0.82f, to) * k;
            M.Segment(new Vector3(x, y0, 0f), new Vector3(x, y1, 0f), r0, r1, 7);
        }

        /// <summary>Upper-arm sleeve end / armband.</summary>
        public void ArmBand(int side, Color c, float at = 0.7f, float k = 1.2f, float len = 0.05f)
        {
            float x = side * ShoulderX;
            M.Bone = BB.ArmU(side); M.Color = c;
            float y = Mathf.Lerp(ShoulderY, ElbowY, at);
            float r = Mathf.Lerp(ArmR, ArmR * 0.86f, at) * k;
            M.Segment(new Vector3(x, y + len * 0.5f * U, 0f), new Vector3(x, y - len * 0.5f * U, 0f), r, r, 7);
        }

        public void Leg(int side, Color thigh, Color shin, Color boot, float bootTop = 0.55f, Color? kneePad = null, Color? sole = null)
        {
            float x = side * HipX;
            M.Bone = BB.LegU(side); M.Color = thigh;
            M.Segment(new Vector3(x, HipY + 0.03f * U, 0f), new Vector3(x, KneeY, 0f), ThighR, ThighR * 0.76f, 8);
            M.Bone = BB.LegL(side); M.Color = kneePad ?? thigh;
            M.Sphere(new Vector3(x, KneeY, 0.004f * U), ThighR * 0.78f, 6, 4);
            M.Color = shin;
            M.Segment(new Vector3(x, KneeY, 0f), new Vector3(x, AnkleY + 0.02f * U, 0f), ShinR, ShinR * 0.78f, 7);
            if (bootTop > 0f)
            {
                float top = AnkleY + (KneeY - AnkleY) * bootTop;
                M.Color = boot;
                M.Segment(new Vector3(x, top, 0f), new Vector3(x, AnkleY - 0.005f * U, 0f), ShinR * 1.16f, ShinR * 1.0f, 7);
                M.Band(new Vector3(x, 0f, 0f), ShinR * 1.24f, ShinR * 1.22f, top - 0.025f * U, top + 0.008f * U, 7);
            }
            M.Bone = BB.Foot(side);
            M.Shoe(new Vector3(x, AnkleY, 0f), FootLen, FootW, boot, sole ?? Paint.Shade(boot, 0.6f));
        }

        /// <summary>Shorts/trouser cuff ring on the thigh (or shin) — baggy shorts, rolled trousers.</summary>
        public void LegCuff(int side, Color c, float at, float k = 1.25f, bool onShin = false)
        {
            float x = side * HipX;
            M.Bone = onShin ? BB.LegL(side) : BB.LegU(side); M.Color = c;
            float y0 = onShin ? KneeY : HipY + 0.03f * U, y1 = onShin ? AnkleY : KneeY;
            float y = Mathf.Lerp(y0, y1, at);
            float r = (onShin ? ShinR : ThighR) * Mathf.Lerp(1f, 0.78f, at) * k;
            M.Segment(new Vector3(x, y + 0.04f * U, 0f), new Vector3(x, y - 0.03f * U, 0f), r * 0.92f, r, 8);
        }

        // ================================================================== head & face

        /// <summary>Head (cranium + chin), ears, eyes and brows.</summary>
        public void Head(Color skin, Color iris, Color brow, EyeStyle eyes = EyeStyle.Round, float browTilt = 0f, bool ears = true, float chin = 1f)
        {
            M.Bone = BB.Head; M.Color = skin;
            float r = R;
            M.Sphere(new Vector3(0f, HeadCY + 0.03f * r, -0.02f * r), new Vector3(r, r * 1.0f, r * 0.96f), 12, 9);
            M.Sphere(new Vector3(0f, HeadCY - 0.42f * r, 0.1f * r), new Vector3(0.72f * r * chin, 0.62f * r, 0.78f * r), 9, 6);
            if (ears)
                for (int s = -1; s <= 1; s += 2)
                    M.Sphere(new Vector3(s * 0.95f * r, HeadCY - 0.12f * r, -0.04f * r), new Vector3(0.12f * r, 0.2f * r, 0.1f * r), 6, 4);
            Eyes(iris, brow, eyes, browTilt);
        }

        public void Eyes(Color iris, Color brow, EyeStyle style, float browTilt = 0f, float y = -0.12f, float spread = 0.36f, float size = 1f)
        {
            if (style == EyeStyle.None) return;
            float r = R;
            M.Bone = BB.Head;
            var keepE = M.Emission;
            var keepJ = M.Jitter;
            M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                float ex = s * spread * r, ey = HeadCY + y * r;
                float zs = 0.96f * r * Mathf.Sqrt(Mathf.Max(0.05f, 1f - spread * spread - y * y));
                M.Push().Translate(ex, ey, zs - 0.05f * r).Rotate(0f, s * 20f, 0f);
                bool closed = style == EyeStyle.Closed || (style == EyeStyle.Scar && s < 0);
                if (closed)
                {
                    M.Color = Paint.Shade(brow, 0.7f);
                    M.Box(new Vector3(0f, -0.02f * r, 0.035f * r), new Vector3(0.24f * r, 0.035f * r, 0.05f * r));
                }
                else
                {
                    float h = style == EyeStyle.Narrow ? 0.13f : 0.2f;
                    if (style == EyeStyle.Glow)
                    {
                        M.Emission = 1f;
                        M.Color = iris;
                        M.Sphere(Vector3.zero, new Vector3(0.14f * r * size, h * r * size, 0.065f * r), 7, 5);
                    }
                    else
                    {
                        M.Color = Paint.Shade(iris, 0.42f);
                        M.Sphere(Vector3.zero, new Vector3(0.13f * r * size, h * r * size, 0.065f * r), 7, 5);
                        M.Color = iris;
                        M.Sphere(new Vector3(0f, -h * 0.35f * r * size, 0.012f * r), new Vector3(0.1f * r * size, h * 0.45f * r * size, 0.06f * r), 6, 3);
                        M.Emission = 0.55f;
                        M.Color = new Color(1f, 1f, 0.97f);
                        M.Sphere(new Vector3(s * 0.035f * r, h * 0.38f * r * size, 0.05f * r), 0.04f * r * size, 4, 2);
                    }
                    M.Emission = keepE;
                }
                M.Pop();
                if (style == EyeStyle.Scar && s < 0)
                {
                    M.Color = Paint.Shade(Skin, 0.78f);
                    M.Push().Translate(ex, ey, zs + 0.0f * r).Rotate(0f, s * 20f, 12f);
                    M.Box(Vector3.zero, new Vector3(0.035f * r, 0.5f * r, 0.035f * r));
                    M.Pop();
                }
                // brow
                if (brow.a > 0f)
                {
                    float by = ey + (style == EyeStyle.Narrow ? 0.2f : 0.27f) * r;
                    float bz = 0.96f * r * Mathf.Sqrt(Mathf.Max(0.05f, 1f - spread * spread - (y + 0.27f) * (y + 0.27f)));
                    M.Color = brow;
                    M.Push().Translate(ex, by, bz - 0.01f * r).Rotate(0f, s * 20f, s * browTilt);
                    M.Box(Vector3.zero, new Vector3(0.26f * r, 0.05f * r, 0.05f * r));
                    M.Pop();
                }
            }
            M.Jitter = keepJ;
            M.Emission = keepE;
        }

        /// <summary>Blush / face paint marks (two small flattened discs on the cheeks).</summary>
        public void Cheeks(Color c, float k = 1f)
        {
            M.Bone = BB.Head; M.Color = c;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.5f * R, y = HeadCY - 0.36f * R;
                float z = 0.96f * R * Mathf.Sqrt(1f - 0.25f - 0.13f) - 0.012f * R;
                M.Push().Translate(x, y, z).Rotate(0f, s * 30f, 0f);
                M.Sphere(Vector3.zero, new Vector3(0.11f * R * k, 0.06f * R * k, 0.03f * R), 6, 3);
                M.Pop();
            }
        }

        /// <summary>Beard: chin wedge + sideburns; long = a flowing beard cone (elder).</summary>
        public void Beard(Color c, float length = 0.4f, float width = 0.8f)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            M.Shell(new Vector3(0f, HeadCY - 0.25f * r, 0.0f), new Vector3(1.0f * r, 0.85f * r, 0.98f * r), -75f, 75f, 8, 95f, 150f, 3);
            if (length > 0.2f)
            {
                M.Aim(new Vector3(0f, HeadCY - 0.8f * r, 0.62f * r), new Vector3(0f, -1f, 0.25f));
                M.Lathe(new[] { new Vector2(0.42f * r * width, 0f), new Vector2(0.38f * r * width, length * r * 0.5f), new Vector2(0f, length * r * 1.6f) }, 6, false, true, false);
                M.Pop();
            }
        }

        public void Moustache(Color c)
        {
            M.Bone = BB.Head; M.Color = c;
            for (int s = -1; s <= 1; s += 2)
                M.Segment(new Vector3(s * 0.04f * R, HeadCY - 0.42f * R, 0.86f * R), new Vector3(s * 0.38f * R, HeadCY - 0.55f * R, 0.66f * R), 0.07f * R, 0.03f * R, 5);
        }

        // ================================================================== hair

        /// <summary>Hair cap over the cranium: hairline at frontPhi (forehead), sidePhi (over the ears), backPhi (nape).</summary>
        public void HairCap(Color c, float frontPhi = 58f, float sidePhi = 100f, float backPhi = 122f, float puff = 1.08f)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            var center = new Vector3(0f, HeadCY + 0.05f * r, -0.03f * r);
            var rad = new Vector3(r * puff, r * puff * 1.0f, r * puff * 0.98f);
            M.Shell(center, rad, -180f, 180f, 14, 0f, th =>
            {
                float a = Mathf.Abs(th);
                if (a < 55f) return Mathf.Lerp(frontPhi, frontPhi + 10f, a / 55f);
                if (a < 110f) return Mathf.Lerp(frontPhi + 10f, sidePhi, (a - 55f) / 55f);
                return Mathf.Lerp(sidePhi, backPhi, (a - 110f) / 70f);
            }, 4);
        }

        /// <summary>Fringe locks over the forehead (cones pointing down/forward).</summary>
        public void Bangs(Color c, int count = 5, float length = 0.42f, float spread = 70f, float tilt = 0.4f)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0.5f;
                float th = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t) * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Sin(th) * 0.84f * r, HeadCY + 0.48f * r, Mathf.Cos(th) * 0.84f * r);
                var dir = new Vector3(Mathf.Sin(th) * 0.35f, -1f, tilt + 0.1f);
                float len = length * r * (0.8f + 0.4f * Mathf.Sin(t * 9.1f + 1.3f) * 0.5f + 0.2f);
                M.Aim(at, dir);
                M.Lathe(new[] { new Vector2(0.13f * r, 0f), new Vector2(0.11f * r, len * 0.4f), new Vector2(0f, len) }, 4, false, true, false);
                M.Pop();
            }
        }

        /// <summary>Spiky hair: cones radiating from the head (up / back / sides).</summary>
        public void Spikes(Color c, int count = 12, float length = 0.55f, float upBias = 0.5f, float backBias = 0.5f, int seed = 3, float radius = 0.2f)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            var rng = new MeshBuilder(seed);
            for (int i = 0; i < count; i++)
            {
                float th = Mathf.Lerp(-150f, 150f, (i + 0.5f) / count) + rng.Range(-12f, 12f);
                float ph = rng.Range(25f, 85f);
                if (Mathf.Abs(th) < 60f) ph = rng.Range(15f, 45f);
                var dir = new Vector3(Mathf.Sin(ph * Mathf.Deg2Rad) * Mathf.Sin(th * Mathf.Deg2Rad), Mathf.Cos(ph * Mathf.Deg2Rad), Mathf.Sin(ph * Mathf.Deg2Rad) * Mathf.Cos(th * Mathf.Deg2Rad));
                dir += Vector3.up * upBias * 0.6f + Vector3.back * backBias * 0.6f;
                var at = new Vector3(0f, HeadCY + 0.1f * r, -0.05f * r) + dir.normalized * 0.78f * r;
                float len = length * r * rng.Range(0.75f, 1.2f);
                M.Spike(at, dir, radius * r * rng.Range(0.85f, 1.15f), len, 5);
            }
        }

        /// <summary>Long hair falling down the back (bound to HairB so it sways), to y = endY.</summary>
        public void LongBack(Color c, float endY, float width = 1f, float flare = 1.15f)
        {
            M.Bone = BB.HairB; M.Color = c;
            float r = R;
            var top = new Vector3(0f, HeadCY + 0.1f * r, -0.1f * r);
            M.Panel(top, 95f, 265f, 7, new[]
            {
                new Vector2(1.04f * r * width, 0.1f * r), new Vector2(1.0f * r * width, -0.6f * r),
                new Vector2(0.85f * r * width * flare, (endY - top.y) * 0.55f), new Vector2(0.7f * r * width * flare, endY - top.y),
            }, 1f, c, Paint.Shade(c, 0.7f), 0.012f);
        }

        /// <summary>Side locks framing the face, from the temples to y = endY.</summary>
        public void SideLocks(Color c, float endY, float width = 0.2f)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(s * 0.82f * r, HeadCY + 0.15f * r, 0.35f * r);
                var b = new Vector3(s * 0.9f * r, endY, 0.32f * r);
                M.Segment(a, b, width * r, width * 0.45f * r, 5);
            }
        }

        /// <summary>Ponytail from a tie at the back of the head (HairB), hanging to y = endY.</summary>
        public void Ponytail(Color c, float endY, float thick = 0.32f, float high = 0.45f, Color? tie = null)
        {
            float r = R;
            var tieP = new Vector3(0f, HeadCY + high * r, -0.95f * r);
            M.Bone = BB.Head; M.Color = tie ?? Paint.Shade(c, 0.6f);
            M.Sphere(tieP, 0.16f * r, 6, 4);
            M.Bone = BB.HairB; M.Color = c;
            var mid = new Vector3(0f, (tieP.y + endY) * 0.5f, -1.35f * r);
            M.Segment(tieP, mid, thick * r, thick * 0.95f * r, 6);
            M.Segment(mid, new Vector3(0f, endY, -1.25f * r), thick * 0.95f * r, 0.02f * r, 6);
        }

        public void Bun(Color c, Vector3 at, float size = 0.4f)
        {
            M.Bone = BB.Head; M.Color = c;
            M.Sphere(at, new Vector3(size * R, size * R * 0.9f, size * R), 8, 6);
        }

        public Vector3 HeadPoint(float th, float ph, float k = 1f)
        {
            float t = th * Mathf.Deg2Rad, p = ph * Mathf.Deg2Rad;
            return new Vector3(0f, HeadCY, 0f) + new Vector3(Mathf.Sin(p) * Mathf.Sin(t), Mathf.Cos(p), Mathf.Sin(p) * Mathf.Cos(t)) * R * k;
        }

        public void Braid(Color c, Vector3 from, Vector3 to, int beads, float r0, Color? tip = null, int bone = BB.Head)
        {
            M.Bone = bone;
            M.Beads(from, to, beads, r0 * R, r0 * 0.7f * R, c, Paint.Shade(c, 0.85f));
            if (tip.HasValue) { M.Color = tip.Value; M.Sphere(to + (to - from).normalized * 0.06f * R, 0.12f * R, 5, 4); }
        }

        // ================================================================== headwear

        /// <summary>Hood up: a shell around the head open at the face, a point at the back, a cowl on the shoulders.</summary>
        public void Hood(Color c, Color lining, float openHalf = 58f, float point = 0.5f, bool cowl = true)
        {
            float r = R * 1.3f;
            var center = new Vector3(0f, HeadCY + 0.06f * R, -0.08f * R);
            float oh = openHalf;
            System.Func<float, float> edge = th =>
            {
                float a = Mathf.Abs(Mathf.DeltaAngle(0f, th));
                float t = Mathf.Clamp01((a - oh * 0.5f) / (oh + 50f - oh * 0.5f));
                return 50f + 100f * t * t * (3f - 2f * t);
            };
            M.Bone = BB.Head; M.Color = c;
            M.Shell(center, new Vector3(r, r * 1.02f, r), -180f, 180f, 14, 0f, edge, 4);
            M.Color = lining;
            M.Shell(center, new Vector3(r * 0.96f, r * 0.98f, r * 0.96f), -180f, 180f, 14, 0f, edge, 4, true);
            if (point > 0f)
            {
                M.Color = c;
                M.Spike(center + new Vector3(0f, 0.45f * r, -0.72f * r), new Vector3(0f, 0.25f, -1f), 0.32f * r, point * r, 5);
            }
            if (cowl)
            {
                M.Bone = BB.Chest; M.Color = c;
                M.Panel(new Vector3(0f, 0f, 0f), 0f, 360f, 10, new[]
                {
                    new Vector2(0.09f * U, NeckY + 0.06f * U), new Vector2(ShoulderR * 1.05f, ShoulderY - 0.01f * U),
                    new Vector2(ShoulderR * 1.18f, ShoulderY - 0.1f * U),
                }, DepthK * 1.1f, c, new Color(0, 0, 0, 0));
            }
        }

        /// <summary>Wide-brim hat with a (bent) pointed crown — mage / merchant / straw hat (crown 0 = flat cap).</summary>
        public void BrimHat(Color brim, Color crown, float brimR = 2.6f, float crownH = 2.4f, float bend = 0.5f, Color? band = null, float tilt = -6f)
        {
            M.Bone = BB.Head;
            float r = R;
            var baseP = new Vector3(0f, HeadCY + 0.5f * r, -0.04f * r);
            M.Push().Translate(baseP).Rotate(tilt, 0f, 0f);
            M.Color = brim;
            M.Cylinder(Vector3.zero, brimR * r, brimR * r * 0.96f, 0.05f * r, 14);
            M.Color = crown;
            if (crownH > 0.8f)
            {
                M.Lathe(new[] { new Vector2(1.05f * r, 0f), new Vector2(0.75f * r, crownH * 0.45f * r), new Vector2(0.42f * r, crownH * 0.72f * r) }, 9, false, false, false);
                M.Curve(new Vector3(0f, crownH * 0.72f * r, 0f), new Vector3(0f, crownH * 0.95f * r, -0.1f * r), new Vector3(0f, crownH * 1.0f * r, -bend * r * 1.2f), 0.42f * r, 0.03f * r, 3, 7);
            }
            else if (crownH > 0f)
            {
                M.Lathe(new[] { new Vector2(1.04f * r, 0f), new Vector2(0.95f * r, crownH * 0.7f * r), new Vector2(0.55f * r, crownH * r), new Vector2(0f, crownH * r * 1.04f) }, 10, false, false, false);
            }
            if (band.HasValue)
            {
                M.Color = band.Value;
                M.Band(Vector3.zero, 1.07f * r, 1.0f * r, 0.04f * r, 0.22f * r, 10);
            }
            M.Pop();
        }

        /// <summary>Headscarf (shell over the hair) with a knot at the back.</summary>
        public void Headscarf(Color c, Color knot)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            M.Shell(new Vector3(0f, HeadCY + 0.06f * r, -0.05f * r), new Vector3(1.14f * r, 1.1f * r, 1.12f * r), -180f, 180f, 12, 0f, th =>
            {
                float a = Mathf.Abs(th);
                return a < 70f ? Mathf.Lerp(50f, 70f, a / 70f) : Mathf.Lerp(70f, 112f, (a - 70f) / 110f);
            }, 3);
            M.Color = knot;
            M.Sphere(new Vector3(0f, HeadCY - 0.25f * r, -1.08f * r), 0.18f * r, 6, 4);
            M.Bone = BB.HairB;
            M.Blade(new Vector3(-0.05f * r, HeadCY - 0.3f * r, -1.1f * r), new Vector3(-0.25f * r, HeadCY - 1.0f * r, -1.2f * r), 0.22f * r);
            M.Blade(new Vector3(0.05f * r, HeadCY - 0.3f * r, -1.1f * r), new Vector3(0.22f * r, HeadCY - 0.9f * r, -1.25f * r), 0.22f * r);
        }

        /// <summary>Metal dome helmet (kettle hat with a brim when brim &gt; 0).</summary>
        public void Helmet(Color c, float brim = 0f, Color? trim = null, bool noseGuard = false)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            var center = new Vector3(0f, HeadCY + 0.08f * r, -0.03f * r);
            M.Shell(center, new Vector3(1.14f * r, 1.1f * r, 1.12f * r), -180f, 180f, 12, 0f, th =>
            {
                float a = Mathf.Abs(th);
                return a < 60f ? 66f : Mathf.Lerp(66f, 100f, (a - 60f) / 120f);
            }, 3);
            if (brim > 0f)
            {
                M.Color = trim ?? c;
                M.Cylinder(new Vector3(0f, HeadCY + 0.42f * r, -0.03f * r), brim * r, brim * r * 0.92f, 0.06f * r, 12);
            }
            else if (trim.HasValue)
            {
                M.Color = trim.Value;
                M.Band(new Vector3(0f, 0f, -0.03f * r), 1.16f * r, 1.15f * r, HeadCY + 0.4f * r, HeadCY + 0.52f * r, 12);
            }
            if (noseGuard)
            {
                M.Color = trim ?? c;
                M.Box(new Vector3(0f, HeadCY + 0.15f * r, 1.04f * r), new Vector3(0.12f * r, 0.5f * r, 0.06f * r));
            }
        }

        /// <summary>Curved horns from the temples (ram curl when curl &gt; 0.6).</summary>
        public void Horns(Color c, float length = 0.9f, float curl = 0.3f, float up = 0.6f, float r0 = 0.16f, bool ram = false)
        {
            M.Bone = BB.Head; M.Color = c;
            float r = R;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(s * 0.55f * r, HeadCY + 0.62f * r, -0.1f * r);
                if (ram)
                {
                    var p1 = a + new Vector3(s * 0.55f * r, 0.25f * r, -0.45f * r) * length;
                    var p2 = a + new Vector3(s * 0.95f * r, -0.35f * r, -0.25f * r) * length;
                    var p3 = a + new Vector3(s * 0.85f * r, -0.75f * r, 0.35f * r) * length;
                    M.Curve(a, a + new Vector3(s * 0.3f * r, 0.45f * r, -0.25f * r) * length, p1, r0 * r, r0 * 0.85f * r, 3, 6);
                    M.Curve(p1, p1 + new Vector3(s * 0.35f * r, -0.1f * r, -0.1f * r) * length, p2, r0 * 0.85f * r, r0 * 0.65f * r, 3, 6);
                    M.Curve(p2, p2 + new Vector3(0f, -0.3f * r, 0.15f * r) * length, p3, r0 * 0.65f * r, r0 * 0.3f * r, 3, 6);
                }
                else
                {
                    var tip = a + new Vector3(s * (0.55f + curl * 0.3f) * r, up * 1.2f * r, -0.35f * r) * length;
                    var ctrl = a + new Vector3(s * 0.7f * r, up * 0.2f * r, 0.0f) * length;
                    M.Curve(a, ctrl, tip, r0 * r, 0.015f * r, 4, 6);
                }
            }
        }

        // ================================================================== garments

        /// <summary>Skirt/coat around the hips from the waist to hemY. Open front = coat tails; full = robe/dress.
        /// The front halves follow the thighs (SkirtL/SkirtR), the back hangs from SkirtB.</summary>
        public void Skirt(Color c, Color lining, float hemY, float flare = 1.35f, float openFront = 0f, Color? hem = null, float topY = -1f, float backOnly = 0f)
        {
            float top = topY > 0f ? topY : HipY + 0.08f * U;
            float r0 = HipR * 1.06f;
            float r1 = HipR * flare;
            float rMid = Mathf.Lerp(r0, r1, 0.45f);
            var prof = new[] { new Vector2(r0, top), new Vector2(rMid * 1.02f, Mathf.Lerp(top, hemY, 0.45f)), new Vector2(r1, hemY) };
            float dk = DepthK * 1.08f;
            float side = 92f;   // front/back split angle
            float o = openFront;
            if (backOnly <= 0f)
            {
                M.Bone = BB.SkirtR;
                M.Panel(Vector3.zero, o, side, 4, prof, dk, c, lining);
                M.Bone = BB.SkirtL;
                M.Panel(Vector3.zero, -side, -o, 4, prof, dk, c, lining);
            }
            M.Bone = BB.SkirtB;
            M.Panel(Vector3.zero, side - 4f, 360f - side + 4f, 7, prof, dk, c, lining);
            if (hem.HasValue)
            {
                var hp = new[] { new Vector2(r1 * 1.01f + 0.004f, hemY + 0.05f * U), new Vector2(r1 * 1.02f + 0.004f, hemY - 0.005f) };
                if (backOnly <= 0f)
                {
                    M.Bone = BB.SkirtR; M.Panel(Vector3.zero, o, side, 4, hp, dk, hem.Value, new Color(0, 0, 0, 0));
                    M.Bone = BB.SkirtL; M.Panel(Vector3.zero, -side, -o, 4, hp, dk, hem.Value, new Color(0, 0, 0, 0));
                }
                M.Bone = BB.SkirtB; M.Panel(Vector3.zero, side - 4f, 360f - side + 4f, 7, hp, dk, hem.Value, new Color(0, 0, 0, 0));
            }
        }

        /// <summary>Front flap (tabard, apron, loin panel) hanging from the waist to hemY (SkirtF).</summary>
        public void FrontFlap(Color c, float hemY, float width = 0.2f, Color? trim = null, float topY = -1f, float zOff = 0f)
        {
            float top = topY > 0f ? topY : HipY + 0.1f * U;
            float z = HipR * DepthK * 1.12f + 0.012f * U + zOff;
            float w = width * U;
            M.Bone = BB.SkirtF; M.Color = c;
            var a = new Vector3(-w * 0.5f, top, z); var b = new Vector3(w * 0.5f, top, z);
            var cc = new Vector3(w * 0.55f, hemY, z + 0.05f * U); var d = new Vector3(-w * 0.55f, hemY, z + 0.05f * U);
            M.Quad(a, b, cc, d, Vector3.forward);
            M.Color = Paint.Shade(c, 0.7f);
            M.Quad(a + Vector3.back * 0.008f, b + Vector3.back * 0.008f, cc + Vector3.back * 0.008f, d + Vector3.back * 0.008f, Vector3.back);
            if (trim.HasValue)
            {
                M.Color = trim.Value;
                var t = Vector3.forward * 0.004f;
                M.Quad(d + Vector3.up * 0.045f * U + t, cc + Vector3.up * 0.045f * U + t, cc + t, d + t, Vector3.forward);
            }
        }

        /// <summary>Back flap (tabard back, loin cloth) — SkirtB.</summary>
        public void BackFlap(Color c, float hemY, float width = 0.22f, Color? trim = null)
        {
            float top = HipY + 0.1f * U;
            float z = -HipR * DepthK * 1.12f - 0.012f * U;
            float w = width * U;
            M.Bone = BB.SkirtB; M.Color = c;
            var a = new Vector3(-w * 0.5f, top, z); var b = new Vector3(w * 0.5f, top, z);
            var cc = new Vector3(w * 0.55f, hemY, z - 0.05f * U); var d = new Vector3(-w * 0.55f, hemY, z - 0.05f * U);
            M.Quad(a, b, cc, d, Vector3.back);
            M.Color = Paint.Shade(c, 0.7f);
            M.Quad(a + Vector3.forward * 0.008f, b + Vector3.forward * 0.008f, cc + Vector3.forward * 0.008f, d + Vector3.forward * 0.008f, Vector3.forward);
            if (trim.HasValue)
            {
                M.Color = trim.Value;
                var t = Vector3.back * 0.004f;
                M.Quad(d + Vector3.up * 0.045f * U + t, cc + Vector3.up * 0.045f * U + t, cc + t, d + t, Vector3.back);
            }
        }

        /// <summary>Tabard over the chest (front + back plates on the torso) — pair with FrontFlap/BackFlap.</summary>
        public void ChestPanel(Color c, float width = 0.2f, bool back = true, Color? emblem = null, float topY = -1f)
        {
            M.Bone = BB.Chest; M.Color = c;
            float top = topY > 0f ? topY : ShoulderY - 0.03f * U;
            float bottom = SpineY - 0.04f * U;
            float w = width * U;
            float zf = ChestR * DepthK + 0.012f * U;
            for (int k = back ? -1 : 1; k <= 1; k += 2)
            {
                float z = k * zf;
                var a = new Vector3(-w * 0.5f, top, z * 0.93f); var b = new Vector3(w * 0.5f, top, z * 0.93f);
                var cc = new Vector3(w * 0.5f, bottom, z * 0.97f); var d = new Vector3(-w * 0.5f, bottom, z * 0.97f);
                M.Quad(a, b, cc, d, new Vector3(0f, 0f, k));
            }
            if (emblem.HasValue)
            {
                M.Color = emblem.Value;
                var e = new Vector3(0f, (top + bottom) * 0.5f, zf * 0.98f + 0.006f);
                M.Push().Translate(e);
                M.Flat(new[]
                {
                    new Vector2(0f, 0.07f * U), new Vector2(0.05f * U, 0.05f * U), new Vector2(0.07f * U, 0f), new Vector2(0.05f * U, -0.05f * U),
                    new Vector2(0f, -0.07f * U), new Vector2(-0.05f * U, -0.05f * U), new Vector2(-0.07f * U, 0f), new Vector2(-0.05f * U, 0.05f * U),
                }, 0.01f * U);
                M.Pop();
            }
        }

        /// <summary>Belt around the waist with a buckle.</summary>
        public void Belt(Color c, Color buckle, float y = -1f, float k = 1.08f, float h = 0.05f)
        {
            float yy = y > 0f ? y : HipY + 0.09f * U;
            M.Bone = BB.Hips; M.Color = c;
            float r = Mathf.Max(HipR, WaistR) * k;
            M.Band(Vector3.zero, r, r * 0.98f, yy - h * 0.5f * U, yy + h * 0.5f * U, 10, DepthK * 1.08f);
            M.Color = buckle;
            M.Box(new Vector3(0f, yy, r * DepthK * 1.08f + 0.004f), new Vector3(0.06f * U, h * 1.3f * U, 0.02f * U));
        }

        /// <summary>Wide sash/obi around the waist (Spine) with an optional bow at the back.</summary>
        public void Sash(Color c, float y, float h = 0.1f, bool bow = false, Color? knot = null)
        {
            M.Bone = BB.Spine; M.Color = c;
            float r = Mathf.Max(WaistR, HipR * 0.9f) * 1.1f;
            M.Band(Vector3.zero, r, r * 1.02f, y - h * 0.5f * U, y + h * 0.5f * U, 10, DepthK * 1.1f);
            if (bow)
            {
                M.Color = knot ?? c;
                float z = -r * DepthK * 1.1f - 0.03f * U;
                M.Sphere(new Vector3(0f, y, z), new Vector3(0.05f * U, 0.05f * U, 0.04f * U), 6, 4);
                for (int s = -1; s <= 1; s += 2)
                {
                    M.Push().Translate(s * 0.09f * U, y + 0.02f * U, z - 0.01f * U).Rotate(0f, 0f, s * 20f);
                    M.Sphere(Vector3.zero, new Vector3(0.1f * U, 0.065f * U, 0.035f * U), 6, 4);
                    M.Pop();
                }
                M.Bone = BB.SkirtB;
                M.Blade(new Vector3(-0.03f * U, y - 0.02f * U, z - 0.02f * U), new Vector3(-0.07f * U, y - 0.28f * U, z - 0.06f * U), 0.07f * U);
                M.Blade(new Vector3(0.03f * U, y - 0.02f * U, z - 0.02f * U), new Vector3(0.08f * U, y - 0.25f * U, z - 0.06f * U), 0.07f * U);
            }
        }

        /// <summary>Diagonal strap/baldric across the chest (left shoulder → right hip by default).</summary>
        public void Strap(Color c, int side = -1, float w = 0.045f)
        {
            M.Bone = BB.Chest; M.Color = c;
            var dirs = new Vector3[3];
            for (int k = -1; k <= 1; k += 2)
            {
                // shoulder → chest centre → opposite hip, each point on the torso's elliptic cross-section
                dirs[0] = OnTorso(side * ShoulderX * 0.62f, ShoulderY - 0.03f * U, ShoulderR, k);
                dirs[1] = OnTorso(-side * ShoulderX * 0.05f, ChestY, ChestR * 1.02f, k);
                dirs[2] = OnTorso(-side * ShoulderX * 0.5f, SpineY, WaistR * 1.06f, k);
                var n = new Vector3(0f, 0f, k);
                for (int i = 0; i < 2; i++)
                {
                    var a = dirs[i]; var b = dirs[i + 1];
                    var d = Vector3.Cross(b - a, n).normalized * w * U * 0.5f;
                    M.Quad(a - d, a + d, b + d, b - d, n);
                }
            }
        }

        Vector3 OnTorso(float x, float y, float r, int k)
        {
            float q = Mathf.Clamp01(Mathf.Abs(x) / Mathf.Max(0.01f, r));
            return new Vector3(x, y, k * (r * DepthK * Mathf.Sqrt(Mathf.Max(0.05f, 1f - q * q * 0.92f)) + 0.012f * U));
        }

        /// <summary>Pauldron on a shoulder (Arm bone). size ≈ 1 for normal, 1.5 for big plate.</summary>
        public void Pauldron(int side, Color c, float size = 1f, Color? trim = null, int layers = 2)
        {
            M.Bone = BB.ArmU(side); M.Color = c;
            float x = side * ShoulderX;
            float r = ArmR * 1.9f * size;
            for (int i = 0; i < layers; i++)
            {
                float k = 1f - i * 0.18f;
                var center = new Vector3(x + side * 0.012f * U, ShoulderY + 0.01f * U - i * 0.05f * U * size, 0f);
                M.Color = i == 0 ? c : Paint.Shade(c, 0.88f);
                M.Shell(center, new Vector3(r * k, r * 0.8f * k, r * 0.95f * k), -180f, 180f, 8, 0f, 78f, 2);
                M.Shell(center, new Vector3(r * k * 0.96f, r * 0.77f * k, r * 0.92f * k), -180f, 180f, 8, 0f, 78f, 2, true);
            }
            if (trim.HasValue)
            {
                M.Color = trim.Value;
                var center = new Vector3(x + side * 0.012f * U, ShoulderY + 0.01f * U, 0f);
                M.Shell(center, new Vector3(r * 1.02f, r * 0.82f, r * 0.97f), -180f, 180f, 8, 70f, 80f, 1);
            }
        }

        /// <summary>High stiff collar around the neck, open at the front (coat collars).</summary>
        public void Collar(Color c, Color lining, float height = 0.14f, float open = 40f, float flare = 1.35f)
        {
            M.Bone = BB.Chest;
            M.Panel(Vector3.zero, open, 360f - open, 10, new[]
            {
                new Vector2(0.1f * U * flare, NeckY + height * U), new Vector2(0.085f * U, NeckY - 0.0f * U), new Vector2(ShoulderR * 0.85f, ShoulderY - 0.02f * U),
            }, DepthK * 1.15f, c, lining, 0.01f);
        }

        /// <summary>Fur/mantle ring around the shoulders (fluffy blob ring).</summary>
        public void Mantle(Color c, float size = 1f, float drop = 0.06f, int seed = 5)
        {
            M.Bone = BB.Chest; M.Color = c;
            int n = 9;
            for (int i = 0; i < n; i++)
            {
                float th = (i / (float)n) * Mathf.PI * 2f;
                float rr = ShoulderR * 0.98f;
                var p = new Vector3(Mathf.Sin(th) * rr * 1.08f, ShoulderY - drop * U, Mathf.Cos(th) * rr * DepthK * 1.15f);
                M.Blob(p, new Vector3(0.085f, 0.07f, 0.085f) * U * size, 0, 0.2f, seed + i);
            }
        }

        /// <summary>Cape from the shoulders down to hemY (Cape bone, sways with motion).</summary>
        public void Cape(Color c, Color lining, float hemY, float width = 1f, Color? trim = null)
        {
            M.Bone = BB.Cape;
            float top = ShoulderY + 0.01f * U;
            var center = new Vector3(0f, 0f, 0.02f * U);
            M.Panel(center, 118f, 242f, 6, new[]
            {
                new Vector2(ShoulderR * 1.08f * width, top), new Vector2(ShoulderR * 1.22f * width, Mathf.Lerp(top, hemY, 0.3f)),
                new Vector2(ShoulderR * 1.45f * width, hemY),
            }, DepthK * 1.25f, c, lining, 0.01f);
            if (trim.HasValue)
            {
                M.Panel(center, 118f, 242f, 6, new[]
                {
                    new Vector2(ShoulderR * 1.46f * width + 0.004f, hemY + 0.05f * U), new Vector2(ShoulderR * 1.46f * width + 0.004f, hemY - 0.004f),
                }, DepthK * 1.25f, trim.Value, new Color(0, 0, 0, 0));
            }
        }

        /// <summary>Scarf: a roll around the neck and a tail fluttering behind (Cape bone).</summary>
        public void Scarf(Color c, float tail = 0.45f, bool mask = false)
        {
            M.Bone = BB.Chest; M.Color = c;
            M.Torus(new Vector3(0f, NeckY - 0.0f * U, -0.005f * U), 0.085f * U, 0.035f * U, 10, 5);
            if (mask)
            {
                M.Bone = BB.Head;
                float r = R;
                M.Shell(new Vector3(0f, HeadCY - 0.05f * r, 0.0f), new Vector3(1.06f * r, 1.04f * r, 1.04f * r), -95f, 95f, 8, 100f, 150f, 2);
            }
            if (tail > 0f)
            {
                M.Bone = BB.Cape; M.Color = c;
                var a = new Vector3(0.04f * U, NeckY - 0.02f * U, -0.08f * U);
                M.Blade(a, a + new Vector3(0.05f * U, -tail * U, -0.12f * U), 0.08f * U, Vector3.right);
                M.Blade(a + new Vector3(-0.05f * U, 0f, 0f), a + new Vector3(-0.02f * U, -tail * 0.8f * U, -0.16f * U), 0.075f * U, Vector3.right);
            }
        }

        /// <summary>Wide hanging kimono sleeve on the forearm (ArmL).</summary>
        public void WideSleeve(int side, Color c, Color lining, float length = 0.32f, float width = 2.4f, Color? hem = null)
        {
            M.Bone = BB.ArmL(side);
            float x = side * ShoulderX;
            var center = new Vector3(x, 0f, 0f);
            M.Panel(center, 0f, 360f, 9, new[]
            {
                new Vector2(ForeR * 1.25f, ElbowY + 0.05f * U), new Vector2(ForeR * width, ElbowY - length * 0.35f * U),
                new Vector2(ForeR * width * 1.05f, ElbowY - length * U),
            }, 1f, c, lining, 0.008f);
            if (hem.HasValue)
                M.Panel(center, 0f, 360f, 9, new[]
                {
                    new Vector2(ForeR * width * 1.07f + 0.003f, ElbowY - (length - 0.05f) * U), new Vector2(ForeR * width * 1.07f + 0.003f, ElbowY - length * U - 0.003f),
                }, 1f, hem.Value, new Color(0, 0, 0, 0));
        }

        /// <summary>Puffed upper sleeve (ArmU).</summary>
        public void PuffSleeve(int side, Color c, float k = 1.4f)
        {
            M.Bone = BB.ArmU(side); M.Color = c;
            float x = side * ShoulderX;
            M.Sphere(new Vector3(x, Mathf.Lerp(ShoulderY, ElbowY, 0.3f), 0f), new Vector3(ArmR * k, (ShoulderY - ElbowY) * 0.42f, ArmR * k), 8, 5);
        }

        public void Backpack(Color c, Color strap, Color? roll = null, Color? pot = null)
        {
            M.Bone = BB.Chest; M.Color = c;
            float z = -ChestR * DepthK - 0.12f * U;
            M.Box(new Vector3(0f, ChestY + 0.02f * U, z), new Vector3(0.3f * U, 0.42f * U, 0.2f * U));
            M.Color = Paint.Shade(c, 0.85f);
            M.Box(new Vector3(0f, ChestY - 0.1f * U, z - 0.11f * U), new Vector3(0.22f * U, 0.16f * U, 0.05f * U));
            if (roll.HasValue)
            {
                M.Color = roll.Value;
                M.Aim(new Vector3(-0.2f * U, ChestY + 0.28f * U, z), Vector3.right);
                M.Cylinder(Vector3.zero, 0.07f * U, 0.07f * U, 0.4f * U, 8);
                M.Pop();
            }
            if (pot.HasValue)
            {
                M.Color = pot.Value;
                M.Sphere(new Vector3(0.12f * U, ChestY - 0.18f * U, z - 0.12f * U), new Vector3(0.08f, 0.07f, 0.08f) * U, 7, 5);
            }
            Strap(strap, -1, 0.04f);
            Strap(strap, 1, 0.04f);
        }

        public void Quiver(Color c, Color fletch, int side = 1)
        {
            M.Bone = BB.Chest; M.Color = c;
            float z = -ChestR * DepthK - 0.07f * U;
            var bottom = new Vector3(-side * 0.1f * U, SpineY - 0.04f * U, z);
            var top = new Vector3(side * 0.12f * U, ShoulderY + 0.1f * U, z - 0.02f * U);
            M.Segment(bottom, top, 0.06f * U, 0.07f * U, 7);
            M.Color = fletch;
            var dir = (top - bottom).normalized;
            for (int i = 0; i < 4; i++)
            {
                var off = new Vector3((i - 1.5f) * 0.03f * U, 0f, (i % 2) * 0.03f * U);
                M.Blade(top + off, top + off + dir * 0.12f * U, 0.035f * U);
            }
            Strap(Paint.Shade(c, 0.7f), side, 0.035f);
        }

        /// <summary>A sheathed blade or club hanging at the hip (SkirtL/R so it swings a bit).</summary>
        public void HipItem(int side, Color c, float len = 0.35f, float r = 0.03f, Color? cap = null)
        {
            M.Bone = side < 0 ? BB.SkirtL : BB.SkirtR; M.Color = c;
            var a = new Vector3(side * (HipR * 1.1f), HipY + 0.06f * U, 0.02f * U);
            M.Segment(a, a + new Vector3(side * 0.03f * U, -len * U, -0.1f * U), r * U, r * 0.8f * U, 6);
            if (cap.HasValue) { M.Color = cap.Value; M.Sphere(a, r * 1.3f * U, 6, 4); }
        }

        public void Pouch(int side, Color c, float zFront = 0.5f, float size = 1f)
        {
            M.Bone = BB.Hips; M.Color = c;
            float th = side * Mathf.Lerp(80f, 30f, zFront) * Mathf.Deg2Rad;
            float rr = HipR * 1.12f;
            var p = new Vector3(Mathf.Sin(th) * rr, HipY + 0.03f * U, Mathf.Cos(th) * rr * DepthK * 1.1f);
            M.Box(p, new Vector3(0.07f, 0.08f, 0.05f) * U * size);
        }

        // ================================================================== held items (authored along +Z of the hand)

        /// <summary>Starts the hand frame: origin at the fist, +Y of the weapon along the hand's +Z. Pop afterwards.</summary>
        public void BeginHand(int side, float along = 0f)
        {
            M.Bone = BB.Hand(side);
            M.Push().Translate(Grip(side)).Rotate(90f, 0f, 0f).Translate(0f, along, 0f);
        }

        public void End() => M.Pop();

        public void Sword(int side, float len, float width, Color blade, Color hilt, Color guard, bool broad = false)
        {
            BeginHand(side);
            M.Color = hilt;
            M.Cylinder(new Vector3(0f, -0.11f * U, 0f), 0.017f * U, 0.017f * U, 0.13f * U, 6);
            M.Color = guard;
            M.Sphere(new Vector3(0f, -0.12f * U, 0f), 0.028f * U, 6, 4);
            M.Box(new Vector3(0f, 0.025f * U, 0f), new Vector3(width * 1.9f * U, 0.028f * U, 0.04f * U));
            M.Color = blade;
            M.Push().Translate(0f, 0.04f * U, 0f).Scale(new Vector3(width * 0.5f * U, 1f, (broad ? 0.024f : 0.017f) * U));
            M.Lathe(new[] { new Vector2(1f, 0f), new Vector2(1f, len * 0.86f * U), new Vector2(0f, len * U) }, 4, false, true, false);
            M.Pop();
            End();
            Model.CastBone = BB.Hand(side);
        }

        public void Greatsword(int side, float len, Color blade, Color hilt, Color guard)
        {
            BeginHand(side);
            M.Color = hilt;
            M.Cylinder(new Vector3(0f, -0.24f * U, 0f), 0.02f * U, 0.02f * U, 0.26f * U, 6);
            M.Color = guard;
            M.Sphere(new Vector3(0f, -0.25f * U, 0f), 0.035f * U, 6, 4);
            M.Box(new Vector3(0f, 0.03f * U, 0f), new Vector3(0.32f * U, 0.04f * U, 0.06f * U));
            M.Color = blade;
            M.Push().Translate(0f, 0.05f * U, 0f).Scale(new Vector3(0.085f * U, 1f, 0.026f * U));
            M.Lathe(new[] { new Vector2(1f, 0f), new Vector2(0.95f, len * 0.9f * U), new Vector2(0.15f, len * U) }, 4, false, true, true);
            M.Pop();
            M.Color = Paint.Shade(blade, 0.7f);
            M.Box(new Vector3(0.075f * U, len * 0.55f * U, 0f), new Vector3(0.03f * U, 0.05f * U, 0.03f * U)); // notch shadow
            End();
        }

        public void Dagger(int side, Color blade, Color hilt, float len = 0.3f, bool cog = false, Color? cogCol = null)
        {
            BeginHand(side);
            M.Color = hilt;
            M.Cylinder(new Vector3(0f, -0.07f * U, 0f), 0.015f * U, 0.016f * U, 0.09f * U, 6);
            M.Box(new Vector3(0f, 0.02f * U, 0f), new Vector3(0.09f * U, 0.02f * U, 0.03f * U));
            M.Color = blade;
            M.Push().Translate(0f, 0.03f * U, 0f).Scale(new Vector3(0.028f * U, 1f, 0.012f * U));
            M.Lathe(new[] { new Vector2(1f, 0f), new Vector2(0.9f, len * 0.65f * U), new Vector2(0f, len * U) }, 4, false, true, false);
            M.Pop();
            if (cog)
            {
                M.Color = cogCol ?? new Color(0.8f, 0.65f, 0.3f);
                M.Push().Translate(0f, 0.04f * U, 0f).Rotate(0f, 0f, 90f);
                M.Torus(Vector3.zero, 0.04f * U, 0.012f * U, 8, 4);
                M.Pop();
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    M.Box(new Vector3(Mathf.Cos(a) * 0.055f * U, 0.04f * U + Mathf.Sin(a) * 0.055f * U, 0f), new Vector3(0.018f, 0.018f, 0.02f) * U);
                }
            }
            End();
        }

        public void Hammer(int side, Color head, Color shaft, float len = 0.75f, float headK = 1f, Color? band = null)
        {
            BeginHand(side, -0.18f * U);
            M.Color = shaft;
            M.Cylinder(Vector3.zero, 0.02f * U, 0.02f * U, len * U, 6);
            M.Color = head;
            M.Box(new Vector3(0f, len * U, 0f), new Vector3(0.12f, 0.13f, 0.28f) * U * headK);
            M.Box(new Vector3(0f, len * U, 0.15f * U * headK), new Vector3(0.15f, 0.15f, 0.04f) * U * headK);
            M.Box(new Vector3(0f, len * U, -0.15f * U * headK), new Vector3(0.15f, 0.15f, 0.04f) * U * headK);
            if (band.HasValue) { M.Color = band.Value; M.Box(new Vector3(0f, len * U, 0f), new Vector3(0.135f, 0.05f, 0.29f) * U * headK); }
            End();
            Model.CastBone = BB.Hand(side);
            Model.CastOffset = new Vector3(0f, 0f, (len - 0.18f) * U) + new Vector3(0f, -0.05f * U * HandK, 0.012f * U);
        }

        public void Mace(int side, Color head, Color shaft, float len = 0.6f)
        {
            BeginHand(side, -0.12f * U);
            M.Color = shaft;
            M.Cylinder(Vector3.zero, 0.02f * U, 0.022f * U, len * U, 6);
            M.Color = head;
            M.Sphere(new Vector3(0f, len * U, 0f), 0.075f * U, 8, 6, false);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                M.Box(new Vector3(Mathf.Cos(a) * 0.07f * U, len * U, Mathf.Sin(a) * 0.07f * U), new Vector3(0.03f, 0.13f, 0.03f) * U);
            }
            End();
        }

        public void Axe(int side, Color head, Color shaft, float len = 0.85f, float size = 1f)
        {
            BeginHand(side, -0.2f * U);
            M.Color = shaft;
            M.Cylinder(Vector3.zero, 0.022f * U, 0.022f * U, len * U, 6);
            M.Color = head;
            float s = size * U;
            // blade in the weapon YZ plane, edge towards +Z (leads an overhead chop)
            M.Push().Translate(0f, len * U - 0.08f * s, 0f).Rotate(0f, -90f, 0f);
            M.Flat(new[] { new Vector2(0.02f * s, 0.08f * s), new Vector2(0.2f * s, 0.16f * s), new Vector2(0.26f * s, 0.0f), new Vector2(0.2f * s, -0.16f * s), new Vector2(0.02f * s, -0.08f * s) }, 0.03f * s);
            M.Pop();
            M.Box(new Vector3(0f, len * U - 0.08f * s, -0.04f * s), new Vector3(0.05f, 0.12f, 0.08f) * s);
            End();
        }

        public enum StaffTop { Orb, Ring, Skull, Totem, Crook, Leaf, Bead, Lantern, Plain }

        /// <summary>Staff gripped at 40 % of its length; the top ornament is the cast anchor.</summary>
        public void Staff(int side, float len, Color wood, StaffTop top, Color accent, Color glow, float orbR = 0.07f)
        {
            float below = len * 0.42f * U, above = len * 0.58f * U;
            BeginHand(side);
            M.Color = wood;
            M.Cylinder(new Vector3(0f, -below, 0f), 0.019f * U, 0.022f * U, below + above, 6);
            var tip = new Vector3(0f, above, 0f);
            Vector3 anchor = tip;
            switch (top)
            {
                case StaffTop.Orb:
                    M.Color = accent;
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI * 2f / 3f;
                        M.Curve(tip - new Vector3(0f, 0.04f * U, 0f), tip + new Vector3(Mathf.Cos(a) * 0.09f, 0.02f, Mathf.Sin(a) * 0.09f) * U,
                                tip + new Vector3(Mathf.Cos(a) * 0.05f, 0.13f, Mathf.Sin(a) * 0.05f) * U, 0.014f * U, 0.006f * U, 3, 4);
                    }
                    M.Emission = 1f; M.Color = glow;
                    anchor = tip + new Vector3(0f, 0.08f * U, 0f);
                    M.Sphere(anchor, orbR * U, 8, 6);
                    M.Emission = 0f;
                    break;
                case StaffTop.Ring:
                    M.Color = accent;
                    anchor = tip + new Vector3(0f, 0.13f * U, 0f);
                    M.Push().Translate(anchor).Rotate(0f, 0f, 90f);
                    M.Torus(Vector3.zero, 0.11f * U, 0.012f * U, 12, 4);
                    M.Pop();
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (i - 1.5f) * 0.5f;
                        M.Push().Translate(anchor + new Vector3(Mathf.Sin(a) * 0.11f * U, -Mathf.Cos(a) * 0.11f * U, 0f)).Rotate(0f, 0f, 90f);
                        M.Torus(Vector3.zero, 0.025f * U, 0.007f * U, 6, 3);
                        M.Pop();
                    }
                    M.Emission = 0.8f; M.Color = glow;
                    M.Sphere(anchor, 0.035f * U, 6, 4);
                    M.Emission = 0f;
                    break;
                case StaffTop.Skull:
                    M.Color = accent;
                    anchor = tip + new Vector3(0f, 0.08f * U, 0f);
                    M.Sphere(anchor, new Vector3(0.075f, 0.07f, 0.08f) * U, 8, 6);
                    M.Box(anchor + new Vector3(0f, -0.06f * U, 0.02f * U), new Vector3(0.08f, 0.04f, 0.07f) * U);
                    M.Curve(anchor + new Vector3(-0.05f, 0.04f, -0.02f) * U, anchor + new Vector3(-0.13f, 0.06f, -0.04f) * U, anchor + new Vector3(-0.12f, 0.16f, 0f) * U, 0.02f * U, 0.004f * U, 3, 4);
                    M.Curve(anchor + new Vector3(0.05f, 0.04f, -0.02f) * U, anchor + new Vector3(0.13f, 0.06f, -0.04f) * U, anchor + new Vector3(0.12f, 0.16f, 0f) * U, 0.02f * U, 0.004f * U, 3, 4);
                    M.Emission = 1f; M.Color = glow;
                    M.Sphere(anchor + new Vector3(-0.028f, 0.005f, 0.068f) * U, 0.018f * U, 5, 3);
                    M.Sphere(anchor + new Vector3(0.028f, 0.005f, 0.068f) * U, 0.018f * U, 5, 3);
                    M.Emission = 0f;
                    break;
                case StaffTop.Totem:
                    M.Color = accent;
                    anchor = tip + new Vector3(0f, 0.06f * U, 0f);
                    M.Box(anchor, new Vector3(0.1f, 0.14f, 0.1f) * U);
                    M.Color = Paint.Shade(accent, 0.6f);
                    M.Box(anchor + new Vector3(0f, 0.01f, 0.05f) * U, new Vector3(0.07f, 0.03f, 0.01f) * U);
                    M.Color = new Color(0.95f, 0.92f, 0.85f);
                    M.Blade(anchor + new Vector3(0.05f, -0.04f, 0f) * U, anchor + new Vector3(0.14f, -0.22f, 0f) * U, 0.05f * U);
                    M.Color = new Color(0.85f, 0.35f, 0.25f);
                    M.Blade(anchor + new Vector3(-0.05f, -0.04f, 0f) * U, anchor + new Vector3(-0.13f, -0.24f, 0.02f) * U, 0.05f * U);
                    M.Emission = 0.9f; M.Color = glow;
                    M.Sphere(anchor + new Vector3(0f, 0.11f * U, 0f), 0.04f * U, 6, 4);
                    M.Emission = 0f;
                    break;
                case StaffTop.Crook:
                    M.Color = wood;
                    M.Curve(tip, tip + new Vector3(0f, 0.18f, 0.0f) * U, tip + new Vector3(0f, 0.12f, 0.12f) * U, 0.02f * U, 0.016f * U, 4, 5);
                    anchor = tip + new Vector3(0f, 0.12f * U, 0.06f * U);
                    break;
                case StaffTop.Leaf:
                    M.Color = accent;
                    M.Push().Translate(tip).Scale(new Vector3(0.05f * U, 1f, 0.016f * U));
                    M.Lathe(new[] { new Vector2(0.3f, 0f), new Vector2(1f, 0.08f * U), new Vector2(0.9f, 0.2f * U), new Vector2(0f, 0.34f * U) }, 4, false, true, false);
                    M.Pop();
                    M.Color = new Color(0.78f, 0.22f, 0.2f);
                    M.Blade(tip + new Vector3(0f, -0.02f * U, 0f), tip + new Vector3(0.02f * U, -0.2f * U, -0.04f * U), 0.05f * U);
                    anchor = tip + new Vector3(0f, 0.2f * U, 0f);
                    break;
                case StaffTop.Bead:
                    M.Emission = 1f; M.Color = glow;
                    anchor = tip + new Vector3(0f, 0.04f * U, 0f);
                    M.Sphere(anchor, 0.045f * U, 6, 4);
                    M.Emission = 0f;
                    M.Color = accent;
                    M.Blade(tip, tip + new Vector3(0.06f, 0.12f, 0f) * U, 0.04f * U);
                    M.Blade(tip, tip + new Vector3(-0.07f, 0.1f, 0.02f) * U, 0.04f * U);
                    break;
                case StaffTop.Lantern:
                    M.Color = accent;
                    M.Curve(tip, tip + new Vector3(0f, 0.12f, 0f) * U, tip + new Vector3(0f, 0.1f, 0.14f) * U, 0.016f * U, 0.012f * U, 3, 5);
                    anchor = tip + new Vector3(0f, 0.0f, 0.15f) * U;
                    M.Box(anchor, new Vector3(0.08f, 0.1f, 0.08f) * U);
                    M.Emission = 1f; M.Color = glow;
                    M.Box(anchor + new Vector3(0f, -0.005f, 0f) * U, new Vector3(0.085f, 0.07f, 0.085f) * U);
                    M.Emission = 0f;
                    break;
            }
            End();
            Model.CastBone = BB.Hand(side);
            // hand frame: weapon +Y → hand +Z, weapon +Z → hand −Y
            Model.CastOffset = Grip(side) - Bind[BB.Hand(side)] + new Vector3(anchor.x, -anchor.z, anchor.y);
        }

        public void Spear(int side, float len, Color wood, Color blade, Color tassel)
        {
            Staff(side, len, wood, StaffTop.Plain, blade, blade);
            float above = len * 0.58f * U;
            BeginHand(side);
            M.Color = blade;
            var tip = new Vector3(0f, above, 0f);
            M.Push().Translate(tip).Scale(new Vector3(0.045f * U, 1f, 0.015f * U));
            M.Lathe(new[] { new Vector2(0.4f, 0f), new Vector2(1f, 0.07f * U), new Vector2(0f, 0.28f * U) }, 4, false, true, false);
            M.Pop();
            M.Color = tassel;
            M.Blade(tip, tip + new Vector3(0.03f * U, -0.18f * U, -0.05f * U), 0.06f * U);
            M.Blade(tip, tip + new Vector3(-0.03f * U, -0.16f * U, 0.04f * U), 0.06f * U);
            End();
        }

        /// <summary>Recurve bow held in the hand (limbs along the hand's ±Z, belly towards the target).</summary>
        public void Bow(int side, float len, Color wood, Color grip, Color str)
        {
            BeginHand(side);
            float h = len * 0.5f * U;
            M.Color = grip;
            M.Cylinder(new Vector3(0f, -0.06f * U, 0.02f * U), 0.024f * U, 0.024f * U, 0.12f * U, 6);
            M.Color = wood;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(0f, s * 0.05f * U, 0.025f * U);
                var b = new Vector3(0f, s * h * 0.6f, 0.07f * U);
                var c = new Vector3(0f, s * h * 0.95f, 0.02f * U);
                var tip = new Vector3(0f, s * h, 0.055f * U);
                M.Segment(a, b, 0.02f * U, 0.016f * U, 5);
                M.Segment(b, c, 0.016f * U, 0.011f * U, 5);
                M.Segment(c, tip, 0.011f * U, 0.008f * U, 5);
            }
            M.Color = str;
            M.Box(new Vector3(0f, 0f, 0.028f * U), new Vector3(0.006f * U, len * 0.95f * U, 0.006f * U));
            End();
        }

        public void Shield(Color face, Color rim, Color emblem, bool round = false, float size = 1f)
        {
            M.Bone = BB.ArmLL;
            float x = -ShoulderX - ForeR - 0.035f * U;
            float y = Mathf.Lerp(ElbowY, WristY, 0.45f);
            M.Push().Translate(x, y, 0.02f * U).Rotate(0f, 90f, 0f);
            float s = size * U;
            if (round)
            {
                M.Color = face;
                M.Push().Rotate(90f, 0f, 0f);
                M.Cylinder(new Vector3(0f, -0.02f * s, 0f), 0.27f * s, 0.27f * s, 0.04f * s, 12);
                M.Color = rim;
                M.Torus(new Vector3(0f, 0.0f, 0f), 0.265f * s, 0.022f * s, 12, 4);
                M.Color = emblem;
                M.Sphere(new Vector3(0f, -0.03f * s, 0f), new Vector3(0.07f * s, 0.035f * s, 0.07f * s), 7, 4);
                M.Pop();
            }
            else
            {
                M.Color = rim;
                M.Flat(new[] { new Vector2(-0.24f * s, 0.27f * s), new Vector2(0.24f * s, 0.27f * s), new Vector2(0.25f * s, 0.02f * s), new Vector2(0f, -0.38f * s), new Vector2(-0.25f * s, 0.02f * s) }, 0.05f * s);
                M.Color = face;
                M.Push().Translate(0f, 0f, -0.012f * s);
                M.Flat(new[] { new Vector2(-0.2f * s, 0.235f * s), new Vector2(0.2f * s, 0.235f * s), new Vector2(0.21f * s, 0.02f * s), new Vector2(0f, -0.32f * s), new Vector2(-0.21f * s, 0.02f * s) }, 0.04f * s);
                M.Pop();
                M.Color = emblem;
                M.Push().Translate(0f, 0.03f * s, -0.035f * s);
                M.Flat(new[]
                {
                    new Vector2(0f, 0.1f * s), new Vector2(0.07f * s, 0.07f * s), new Vector2(0.1f * s, 0f), new Vector2(0.07f * s, -0.07f * s),
                    new Vector2(0f, -0.1f * s), new Vector2(-0.07f * s, -0.07f * s), new Vector2(-0.1f * s, 0f), new Vector2(-0.07f * s, 0.07f * s),
                }, 0.012f * s);
                M.Pop();
            }
            M.Pop();
            Model.Shield = true;
        }

        public void Book(int side, Color cover, Color pages, bool glow = false, Color? glowCol = null)
        {
            BeginHand(side, 0.02f * U);
            M.Color = cover;
            M.Box(new Vector3(0f, 0.06f * U, -0.02f * U), new Vector3(0.18f, 0.2f, 0.035f) * U);
            M.Color = pages;
            M.Box(new Vector3(0.006f * U, 0.06f * U, -0.02f * U), new Vector3(0.17f, 0.18f, 0.045f) * U);
            if (glow)
            {
                M.Emission = 1f; M.Color = glowCol ?? new Color(0.75f, 0.5f, 1f);
                M.Box(new Vector3(0.0f, 0.06f * U, -0.045f * U), new Vector3(0.1f, 0.1f, 0.012f) * U);
                M.Emission = 0f;
            }
            End();
        }

        /// <summary>Emissive flame/fireball in the hand (warlock fel flame, imp fireball). Sets the cast anchor.</summary>
        public void HandFlame(int side, Color core, Color outer, float size = 1f)
        {
            // authored along the hand's +Z so it points up when the forearm is held forward (Carry hold)
            M.Bone = BB.Hand(side);
            var p = Grip(side) + new Vector3(0f, -0.07f * U * size, 0.01f * U);
            M.Emission = 1f;
            M.Color = outer;
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                M.Blade(p + new Vector3(Mathf.Cos(a) * 0.03f, Mathf.Sin(a) * 0.03f, -0.02f) * U * size, p + new Vector3(Mathf.Cos(a) * 0.02f, Mathf.Sin(a) * 0.02f, 0.17f) * U * size, 0.06f * U * size);
            }
            M.Color = core;
            M.Sphere(p, 0.045f * U * size, 6, 4);
            M.Emission = 0f;
            Model.CastBone = BB.Hand(side);
            Model.CastOffset = p - Bind[BB.Hand(side)];
        }

        // ================================================================== finish

        /// <summary>Default anchors / picking for a humanoid built with this kit.</summary>
        public void Finish(string key, UnitGait gait = UnitGait.Humanoid)
        {
            var m = Model;
            m.Key = key;
            m.Rig = UnitRigKind.Biped;
            m.Gait = gait;
            m.Bind = Bind;
            m.Parent = BB.Parent;
            m.Names = BB.Names;
            m.Height = H;
            m.Radius = Mathf.Max(0.26f, ShoulderX + 0.1f * U) * 1.1f;
            m.HipY = PelvisY;
            m.LegLength = HipY;
            m.LieHeight = HipR * DepthK + 0.03f * U;
            m.HeadBone = BB.Head;
            m.HeadTop = new Vector3(0f, H - HeadY, 0f);
            m.CenterBone = BB.Chest;
            m.CenterOffset = new Vector3(0f, (ShoulderY - ChestY) * 0.2f, 0f);
            if (m.CastBone < 0)
            {
                m.CastBone = BB.HandR;
                m.CastOffset = new Vector3(0f, -0.06f * U, 0.03f * U);
            }
            m.PickBones = new[] { BB.Head, BB.Chest, BB.Hips, BB.HandL, BB.HandR, BB.FootL, BB.FootR, BB.LegLL, BB.LegLR };
            m.PickPad = 0.12f * U;
            m.Legs = new UnitLeg[2];
            for (int i = 0; i < 2; i++)
            {
                int s = i == 0 ? -1 : 1;
                m.Legs[i] = new UnitLeg
                {
                    Upper = BB.LegU(s), Lower = BB.LegL(s), Foot = BB.Foot(s), Root = BB.Hips, Side = s,
                    A = HipY - KneeY, B = KneeY - AnkleY,
                    Rest = new Vector3(s * HipX * 0.95f, AnkleY, 0f),
                    Pole = new Vector3(s * 0.12f, 0f, 1f).normalized,
                    Phase = i == 0 ? 0f : 0.5f,
                    HeelBack = FootLen * 0.26f, BallFwd = FootLen * 0.5f,
                };
            }
        }
    }
}
