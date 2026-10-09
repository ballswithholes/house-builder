// Skyreach Peaks (peaks): snow-laden pines, snow-capped boulders, ice spires, wind-carved drifts, the herders' log hut,
// prayer flags on the passes, the broken Heronguard tower and the bones of an old dragon. Snow is pillowy and white with
// cool blue shade (soft lumps where it lies deep, white faces where it caps stone); ice is pale and faintly luminous.
// Colliders: Docs/ArtKeys.md (props-wild block).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterWildPeaks(Dictionary<string, Recipe> r)
        {
            r["prop_pine_snow"] = (art, seed) => Swaying(art, seed, BuildPineSnow, 0.5f);
            r["prop_rock_snow"] = (art, seed) => Simple(art, seed, BuildRockSnow, 1.0f);
            r["prop_ice_spire"] = IceSpire;
            r["prop_snowdrift"] = (art, seed) => Simple(art, seed, BuildSnowdrift, 1.2f);
            r["prop_mountain_hut"] = MountainHut;
            r["prop_prayer_flags"] = (art, seed) => Swaying(art, seed, BuildPrayerFlags, 2.0f);
            r["prop_ruined_tower"] = (art, seed) => Swaying(art, seed, BuildRuinedTower, 1.9f);
            r["prop_dragon_bones"] = (art, seed) => Simple(art, seed, BuildDragonBones, 3.2f);
        }

        static readonly Color PkSnow = Paint.Hex("#F4F7FB"), PkSnowShade = Paint.Hex("#D5DFEC"), PkSnowDeep = Paint.Hex("#C6D3E5");
        static readonly Color PkIce = Paint.Hex("#A6DCF2"), PkIceDeep = Paint.Hex("#5E9FD0"), PkIceTip = Paint.Hex("#E8FBFF");
        static readonly Color PkStone = Paint.Hex("#8F8E9C"), PkStoneLight = Paint.Hex("#AAA9B6"), PkStoneDark = Paint.Hex("#686676");
        static readonly Color PkFrostGrass = Paint.Hex("#C8C9A4"), PkFrostGrassDark = Paint.Hex("#98A07E");

        /// <summary>Soft snow colour by height: blue shade low down, white on top.</summary>
        static System.Func<float, Color> PkSnowRamp(float y0, float y1) =>
            y => Color.Lerp(PkSnowDeep, PkSnow, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(y0, y1, y)));

        /// <summary>Faceted stone whose up-facing faces carry snow.</summary>
        static System.Func<Vector3, Color> PkSnowy(Color stone, float threshold = 0.45f) => n =>
        {
            if (n.y > threshold) return Color.Lerp(PkSnowShade, PkSnow, Mathf.Clamp01((n.y - threshold) / (1f - threshold) * 1.6f));
            if (n.y < -0.2f) return Paint.Shade(stone, 0.78f);
            return stone;
        };

        /// <summary>A row of icicles hanging from a to b (count of them, length up to len; faint glow).</summary>
        static void PkIcicles(MeshBuilder mb, Vector3 a, Vector3 b, int count, float len, int seed)
        {
            var keepC = mb.Color;
            float keepE = mb.Emission;
            mb.Emission = 0.15f;
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count + WildHash(i, 7, seed) * 0.25f / count;
                var p = Vector3.Lerp(a, b, t);
                float l = len * (0.35f + 0.65f * Mathf.Abs(WildHash(i, 8, seed)));
                mb.Color = i % 3 == 0 ? PkIce : PkIceTip;
                mb.Segment(p + Vector3.up * 0.02f, p + Vector3.down * l, 0.035f + l * 0.06f, 0.004f, 4, false, true);
            }
            mb.Color = keepC;
            mb.Emission = keepE;
        }

        // ------------------------------------------------------------------ snowy pine (≈ 10 m; trunk collider 1.0 × 0.6)

        static MeshBuilder BuildPineSnow(int b)
        {
            var mb = Builder(VariantSeed("prop_pine_snow", b), 0.05f, 0.3f, 1.0f);
            mb.WindGradient = true; mb.WindY0 = 1.5f; mb.WindY1 = 11f; mb.Wind = 1.1f;
            var bark = Paint.Hex("#5E4636");
            float k = 0.9f + b * 0.06f;   // per-bucket height
            Trunk(mb, new[] { new Vector2(0.32f, 0f), new Vector2(0.23f, 0.4f), new Vector2(0.18f, 4f * k), new Vector2(0.1f, 9f * k), new Vector2(0.04f, 10.6f * k) }, 6, bark, 0.85f);
            Roots(mb, 4, 0.48f, 0.16f, 0.55f, 40f + b * 17f, bark);
            Foliage(b, Paint.Hex("#2F5A4E"), out var top, out var side, out var bottom, 5f);
            const int tiers = 6;
            var ramp = CanopyRamp(Color.Lerp(top, side, 0.4f), side, bottom, 2.2f * k, 11.2f * k);
            for (int i = 0; i < tiers; i++)
            {
                float y0 = (2.55f + i * 1.36f) * k, r = (2.3f - i * 0.33f + (i % 2) * 0.08f) * (0.92f + 0.08f * k), h = (2.45f - i * 0.15f) * k;
                mb.Push().Translate(0f, y0, 0f).Rotate((WildHash(i, 1, b) * 3f), 0f, (WildHash(i, 2, b) * 3f));
                var under = Paint.Shade(ramp(y0 - 0.3f), 0.78f);
                mb.Color = side;
                mb.Lathe(new[] { new Vector2(r * 0.8f, -0.42f), new Vector2(r * 0.97f, -0.24f), new Vector2(r, -0.12f), new Vector2(r * 0.55f, h * 0.36f), new Vector2(0f, h) },
                         9, true, true, false,
                         new[] { under, Color.Lerp(under, ramp(y0), 0.6f), ramp(y0), ramp(y0 + h * 0.36f), ramp(y0 + h) }, i * 19f + b * 11f);
                // the snow cap: a white shell over the tier's shoulders whose edge droops in soft tongues, the green
                // skirt showing beneath it
                float R(float y) => y <= h * 0.36f ? Mathf.Lerp(r, r * 0.55f, Mathf.InverseLerp(-0.12f, h * 0.36f, y)) : Mathf.Lerp(r * 0.55f, 0f, Mathf.InverseLerp(h * 0.36f, h, y));
                float ye = h * (0.2f + 0.035f * i);
                const int CV = 14;
                var rings = new List<Vector3[]>();
                float[] ry = { ye, ye + 0.16f, h * 0.62f, h * 0.9f };
                float[] grow = { 0.07f, 0.08f, 0.06f, 0.04f };
                for (int q = 0; q < ry.Length; q++)
                {
                    var ring = new Vector3[CV];
                    for (int v = 0; v < CV; v++)
                    {
                        float a = (v * 360f / CV + i * 19f + b * 11f) * Mathf.Deg2Rad;
                        float y = ry[q], rr = R(y) + grow[q];
                        if (q == 0)
                        {
                            float tongue = Mathf.Abs(WildHash(v, i, b + 3));
                            if (v % 2 == 1) { y -= 0.1f + 0.12f * tongue; rr = R(y) + 0.09f; }
                            else y += 0.03f * tongue;
                        }
                        ring[v] = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
                    }
                    rings.Add(ring);
                }
                Loft(mb, rings, true, true, n => n.y > 0.55f ? PkSnow : Color.Lerp(PkSnowShade, PkSnow, Mathf.Clamp01(n.y / 0.55f)));
                mb.Pop();
            }
            mb.Color = PkSnow;
            mb.Cone(new Vector3(0f, (2.55f + tiers * 1.36f) * k - 0.45f, 0f), 0.36f, 1.3f * k, 6);
            mb.WindGradient = false; mb.Wind = 0f;
            // a little drift around the foot
            SoftLump(mb, new Vector3(0.1f, -0.06f, 0.05f), new Vector3(0.95f, 0.22f, 0.7f), 1, b * 40f, PkSnowRamp(-0.05f, 0.18f), 0.12f);
            return mb;
        }

        // ------------------------------------------------------------------ snow-capped boulder (≈ 1.5 m; collider 2.0 × 1.2)

        static MeshBuilder BuildRockSnow(int b)
        {
            var mb = Builder(VariantSeed("prop_rock_snow", b), 0.07f, 0.3f, 0.6f);
            int sd = VariantSeed("rocksnow", b);
            var stone = Paint.Hsv(PkStone, (b - 1.5f) * 5f);
            float s = 0.92f + 0.06f * b;
            FacetBlob(mb, new Vector3(0f, 0.55f * s, 0.05f), new Vector3(0.98f, 0.8f, 0.6f) * s, 1, 0.22f, sd, 0.35f, PkSnowy(stone, 0.4f));
            FacetBlob(mb, new Vector3(0.78f, 0.28f, 0.22f), new Vector3(0.48f, 0.38f, 0.38f) * s, 1, 0.25f, sd + 5, 0.3f, PkSnowy(Paint.Shade(stone, 1.06f), 0.38f));
            FacetBlob(mb, new Vector3(-0.82f, 0.14f, -0.12f), new Vector3(0.3f, 0.22f, 0.26f), 0, 0.2f, sd + 9, 0.3f, PkSnowy(stone, 0.5f));
            // deep snow banked against the front and the lee side
            SoftLump(mb, new Vector3(-0.25f, -0.02f, -0.5f), new Vector3(0.95f, 0.26f, 0.32f), 1, 4f + b * 9f, PkSnowRamp(-0.05f, 0.24f), 0.12f);
            SoftLump(mb, new Vector3(0.35f, -0.02f, 0.55f), new Vector3(0.9f, 0.32f, 0.36f), 1, -8f + b * 7f, PkSnowRamp(-0.05f, 0.3f), 0.12f);
            // frost-bitten grass and two dark pebbles poking through
            WildTuft(mb, new Vector3(0.95f, 0f, -0.32f), 0.38f, 6, PkFrostGrass, PkFrostGrassDark);
            WildTuft(mb, new Vector3(-0.95f, 0f, 0.25f), 0.3f, 5, PkFrostGrass, PkFrostGrassDark);
            mb.Color = PkStoneDark;
            Gem(mb, new Vector3(0.55f, 0.03f, -0.62f), new Vector3(0.09f, 0.06f, 0.07f));
            Gem(mb, new Vector3(-0.62f, 0.03f, -0.58f), new Vector3(0.07f, 0.05f, 0.06f));
            return mb;
        }

        // ------------------------------------------------------------------ ice spire (≈ 3.4 m; collider 1.4 × 1.0)

        static PropModel IceSpire(string art, int seed)
        {
            var m = Simple(art, seed, BuildIceSpire, 0.7f);
            m.LightAnchors.Add(new Vector3(0f, 1.5f, -0.35f));
            return m;
        }

        static MeshBuilder BuildIceSpire(int b)
        {
            var mb = Builder(VariantSeed("prop_ice_spire", b), 0.06f, 0.2f, 0.4f);
            int sd = VariantSeed("icespire", b);
            FacetBlob(mb, new Vector3(0f, 0.12f, 0.05f), new Vector3(0.68f, 0.3f, 0.48f), 1, 0.22f, sd, 0.5f, PkSnowy(PkStone, 0.35f));
            var body = Paint.Hsv(PkIce, (b - 1.5f) * 4f);
            var deep = Color.Lerp(PkIceDeep, body, 0.25f);
            (Vector3 p, float h, float r, float tx, float tz)[] cs =
            {
                (new Vector3(-0.05f, 0.15f, 0.08f), 3.3f, 0.3f, -4f, 5f), (new Vector3(0.36f, 0.12f, 0.02f), 2.1f, 0.22f, -6f, -16f),
                (new Vector3(-0.38f, 0.1f, 0.12f), 1.8f, 0.2f, 8f, 18f), (new Vector3(0.12f, 0.08f, -0.26f), 1.1f, 0.15f, -24f, -8f),
                (new Vector3(-0.2f, 0.08f, 0.36f), 1.35f, 0.16f, 18f, 6f), (new Vector3(0.58f, 0.05f, 0.18f), 0.7f, 0.11f, 4f, -38f),
                (new Vector3(-0.6f, 0.05f, -0.1f), 0.6f, 0.1f, -12f, 36f),
            };
            for (int i = 0; i < cs.Length; i++)
            {
                var c = cs[i];
                float h = c.h * (1f + (b - 1.5f) * 0.06f);
                mb.Push().Translate(c.p).Rotate(c.tx, i * 27f + b * 13f, c.tz);
                mb.Emission = 0.22f;
                mb.Lathe(new[] { new Vector2(c.r * 0.82f, -0.12f), new Vector2(c.r, h * 0.45f), new Vector2(c.r * 0.96f, h * 0.72f) }, 6, false, true, false,
                         new[] { deep, Color.Lerp(deep, body, 0.7f), body }, 0f);
                mb.Emission = 0.45f;
                mb.Color = PkIceTip;
                mb.Lathe(new[] { new Vector2(c.r * 0.96f, h * 0.72f), new Vector2(0f, h) }, 6, false, false, false, null, 0f);
                // a pale inner core line on the face towards the camera
                mb.Emission = 0.5f;
                mb.Color = PkIceTip;
                mb.Segment(new Vector3(0f, 0.05f, -c.r * 0.86f), new Vector3(0f, h * 0.68f, -c.r * 0.9f), 0.025f, 0.012f, 3, false, true);
                mb.Emission = 0f;
                mb.Pop();
            }
            // frost sparkles and a skirt of snow
            mb.Emission = 0.8f;
            mb.Color = PkIceTip;
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 61f + b * 17f) * Mathf.Deg2Rad;
                Gem(mb, new Vector3(Mathf.Cos(a) * 0.55f, 0.32f + (i % 3) * 0.2f, Mathf.Sin(a) * 0.4f), 0.035f);
            }
            mb.Emission = 0f;
            SoftLump(mb, new Vector3(0.05f, -0.03f, -0.28f), new Vector3(0.8f, 0.2f, 0.32f), 1, b * 11f, PkSnowRamp(-0.05f, 0.18f), 0.12f);
            return mb;
        }

        // ------------------------------------------------------------------ snowdrift (≈ 0.6 m; collider 2.4 × 0.9, or none)

        static MeshBuilder BuildSnowdrift(int b)
        {
            var mb = Builder(VariantSeed("prop_snowdrift", b), 0.04f, 0.25f, 0.5f);
            var ramp = PkSnowRamp(-0.05f, 0.55f);
            float yaw = (b - 1.5f) * 6f;
            SoftLump(mb, new Vector3(0f, 0f, 0f), new Vector3(1.3f, 0.5f, 0.55f), 1, yaw, ramp, 0.08f);
            SoftLump(mb, new Vector3(0.95f, 0f, 0.15f), new Vector3(0.62f, 0.36f, 0.42f), 1, yaw + 20f, ramp, 0.1f);
            SoftLump(mb, new Vector3(-1.05f, 0f, -0.08f), new Vector3(0.56f, 0.28f, 0.4f), 1, yaw - 15f, ramp, 0.1f);
            // the wind-carved cornice: a crest leaning over the lee (back) side
            SoftLump(mb, new Vector3(0.1f, 0.34f, 0.12f), new Vector3(0.95f, 0.17f, 0.38f), 1, yaw + 4f, ramp, 0.06f);
            if (b % 2 == 0)
            {
                // an old fence post buried to the waist, a snow cap on top
                mb.Color = Paint.Hex("#6E5A4A");
                mb.Segment(new Vector3(-0.55f, 0.1f, -0.18f), new Vector3(-0.6f, 0.95f, -0.2f), 0.07f, 0.06f, 5);
                mb.Color = PkSnow;
                mb.Sphere(new Vector3(-0.6f, 0.99f, -0.2f), new Vector3(0.1f, 0.06f, 0.1f), 6, 4, true);
                mb.Color = Pal.RopeStraw;
                mb.Segment(new Vector3(-0.6f, 0.78f, -0.2f), new Vector3(-0.2f, 0.52f, -0.3f), 0.012f, 0.012f, 3);
            }
            else
            {
                // a bare, frosted shrub
                mb.Color = Paint.Hex("#6B5648");
                var root = new Vector3(0.62f, 0.25f, -0.15f);
                for (int i = 0; i < 6; i++)
                {
                    float a = (i * 60f + 15f) * Mathf.Deg2Rad;
                    var tip = root + new Vector3(Mathf.Cos(a) * 0.32f, 0.45f + 0.15f * (i % 2), Mathf.Sin(a) * 0.2f);
                    mb.Segment(root, tip, 0.025f, 0.008f, 3);
                    mb.Color = PkSnowShade;
                    Gem(mb, tip, 0.03f);
                    mb.Color = Paint.Hex("#6B5648");
                }
                mb.Color = Pal.Apple;
                Gem(mb, root + new Vector3(0.1f, 0.3f, -0.12f), 0.03f);
                Gem(mb, root + new Vector3(-0.08f, 0.36f, -0.1f), 0.028f);
            }
            mb.Color = PkStoneDark;
            Gem(mb, new Vector3(1.15f, 0.04f, -0.42f), new Vector3(0.12f, 0.07f, 0.09f));
            Gem(mb, new Vector3(-0.3f, 0.03f, -0.55f), new Vector3(0.08f, 0.05f, 0.06f));
            return mb;
        }

        // ------------------------------------------------------------------ mountain hut (≈ 4.8 × 3.8 m, 5.6 m; collider 7.0 × 5.2, centred)

        const float HutX = 2.4f, HutZ = 1.9f, HutPlinth = 0.5f, HutEave = 2.9f, HutRidge = 1.9f, HutOver = 0.55f;

        static PropModel MountainHut(string art, int seed) =>
            LitBuilding(art, seed, BuildMountainHut, 3.0f,
                        new Vector3(0.95f, 1.6f, -HutZ - 0.1f), new Vector3(0.2f, 2.1f, -HutZ - 0.35f), new Vector3(0f, 3.65f, -HutZ - 0.1f),
                        new Vector3(-HutX - 0.1f, 1.6f, 0.5f), new Vector3(HutX + 0.1f, 1.6f, -0.5f), new Vector3(0.6f, 1.6f, HutZ + 0.1f));

        /// <summary>A course of round logs along X from x0 to x1 at height y (front face z), with darker chinking under it.</summary>
        static void HutLog(MeshBuilder mb, float x0, float x1, float y, float z, float r, Color log)
        {
            mb.Color = log;
            mb.Push().Translate(x0, y, z).Rotate(0f, 0f, -90f);
            mb.Cylinder(Vector3.zero, r, r * 0.97f, x1 - x0, 7, false);
            mb.Pop();
        }

        static MeshBuilder BuildMountainHut(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_mountain_hut", b), 0.05f, 0.3f, 0.8f);
            int sd = VariantSeed("hut", b);
            var log = new[] { Paint.Hex("#8A5F42"), Paint.Hex("#7C5A44"), Paint.Hex("#93664A"), Paint.Hex("#6F5240") }[b];
            var logLight = Paint.Shade(log, 1.12f);
            var endGrain = Paint.Hex("#D9B98C");
            var shutter = new[] { Paint.Hex("#B8463A"), Paint.Hex("#3F6E9E"), Paint.Hex("#4E7E5A"), Paint.Hex("#B8463A") }[b];
            var timber = Pal.TimberDark;
            var stone = PkStone;
            // fieldstone plinth
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, -0.05f, 0f), new Vector3(HutX * 2f + 0.16f, HutPlinth + 0.05f, HutZ * 2f + 0.16f), PkStoneLight);
            for (int i = 0; i < 18; i++)
            {
                // a few proud stones in the plinth (front and sides)
                float t = (i + 0.5f) / 18f;
                Vector3 p; Vector3 n;
                if (i < 8) { p = new Vector3(Mathf.Lerp(-HutX, HutX, (i + 0.5f) / 8f), 0.2f + 0.14f * (i % 2), -HutZ - 0.09f); n = Vector3.back; }
                else if (i < 13) { p = new Vector3(-HutX - 0.09f, 0.2f + 0.14f * (i % 2), Mathf.Lerp(-HutZ, HutZ, (i - 7.5f) / 5f)); n = Vector3.left; }
                else { p = new Vector3(HutX + 0.09f, 0.2f + 0.14f * (i % 2), Mathf.Lerp(-HutZ, HutZ, (i - 12.5f) / 5f)); n = Vector3.right; }
                mb.Color = i % 3 == 0 ? PkStoneLight : (i % 3 == 1 ? stone : PkStoneDark);
                Ngon(mb, p, 0.12f + 0.04f * (i % 2), 6, n, Vector3.up, i * 13f + t);
            }
            // log walls: courses alternate front/back and side logs; the corners cross with round end grain
            const float lr = 0.15f;
            int courses = Mathf.RoundToInt((HutEave - HutPlinth) / (lr * 2f));
            for (int k = 0; k < courses; k++)
            {
                float y = HutPlinth + lr + k * lr * 2f;
                bool along = k % 2 == 0;
                var c = k % 2 == 0 ? log : Paint.Shade(log, 0.94f);
                float ext = 0.22f;
                // front and back logs (along X), lifted by half a log on odd courses so the corners interlock
                float yx = along ? y : y - 0.02f;
                HutLog(mb, -HutX - ext, HutX + ext, yx, -HutZ + lr * 0.4f, lr, c);
                HutLog(mb, -HutX - ext, HutX + ext, yx, HutZ - lr * 0.4f, lr, c);
                // side logs (along Z)
                mb.Push().Rotate(0f, 90f, 0f);
                HutLog(mb, -HutZ - ext, HutZ + ext, y + 0.01f, -HutX + lr * 0.4f, lr, Paint.Shade(c, 0.97f));
                HutLog(mb, -HutZ - ext, HutZ + ext, y + 0.01f, HutX - lr * 0.4f, lr, Paint.Shade(c, 0.97f));
                mb.Pop();
                // end grain discs on the four crossing log ends (front and back faces of the X logs, side faces of the Z logs)
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Ngon(mb, new Vector3(sx * (HutX + ext + 0.005f), yx, sz * (HutZ - lr * 0.4f)), lr * 0.85f, 7, new Vector3(sx, 0f, 0f), Vector3.up, 0f, endGrain);
                        Ngon(mb, new Vector3(sx * (HutX - lr * 0.4f), y + 0.01f, sz * (HutZ + ext + 0.005f)), lr * 0.85f, 7, new Vector3(0f, 0f, sz), Vector3.up, 0f, endGrain);
                    }
            }
            // gables (front and back): vertical boards in a triangle, a round window front, a square one back
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (HutZ - 0.02f);
                var gc = Paint.Shade(log, 1.05f);
                Tri(mb, new Vector3(-HutX - 0.05f, HutEave, z), new Vector3(0f, HutEave + HutRidge - 0.05f, z), new Vector3(HutX + 0.05f, HutEave, z), new Vector3(0f, 0f, s), gc);
                mb.Color = Paint.Shade(gc, 0.75f);
                for (int i = -5; i <= 5; i++)
                {
                    float x = i * 0.4f;
                    float top = HutEave + (HutRidge - 0.08f) * (1f - Mathf.Abs(x) / (HutX + 0.05f));
                    if (top - HutEave < 0.12f) continue;
                    mb.Box(new Vector3(x, (HutEave + top) * 0.5f, z + s * 0.008f), new Vector3(0.025f, top - HutEave, 0.012f));
                }
            }
            RoundWindow(mb, 0f, 3.65f, 0.3f, -HutZ - 0.02f, lit, timber);
            mb.Push().Rotate(0f, 180f, 0f);
            Window(mb, 0f, 3.55f, 0.5f, 0.5f, -HutZ - 0.02f, lit, timber, null, false, true);
            mb.Pop();
            // front: door, a shuttered window with a flower box of evergreens and berries, the lantern, a wreath, steps
            float wallF = -HutZ - 0.02f;
            Door(mb, -0.85f, HutPlinth, 0.92f, 1.95f, wallF, Paint.Shade(shutter, 0.8f), timber, false, Pal.Brass);
            mb.Color = PkStoneLight;
            mb.BoxOn(new Vector3(-0.85f, 0f, -HutZ - 0.3f), new Vector3(1.25f, HutPlinth - 0.02f, 0.5f), PkSnowShade);
            mb.BoxOn(new Vector3(-0.85f, 0f, -HutZ - 0.62f), new Vector3(1.05f, 0.25f, 0.3f), PkSnowShade);
            mb.Color = Paint.Hex("#3F6B4E");
            mb.Push().Translate(-0.85f, HutPlinth + 1.6f, wallF - 0.09f).Rotate(90f, 0f, 0f);
            mb.Torus(Vector3.zero, 0.2f, 0.06f, 10, 4);
            mb.Pop();
            mb.Color = Pal.Vermilion;
            Gem(mb, new Vector3(-0.85f, HutPlinth + 1.42f, wallF - 0.14f), new Vector3(0.06f, 0.05f, 0.03f));
            Gem(mb, new Vector3(-0.9f, HutPlinth + 1.34f, wallF - 0.14f), new Vector3(0.025f, 0.07f, 0.02f));
            Gem(mb, new Vector3(-0.8f, HutPlinth + 1.34f, wallF - 0.14f), new Vector3(0.025f, 0.07f, 0.02f));
            Window(mb, 0.95f, 1.6f, 0.78f, 0.82f, wallF - 0.06f, lit, timber, shutter);
            HutBox(mb, 0.95f, 1.05f, wallF - 0.06f, b);
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(0.2f, 2.42f, wallF - 0.04f), new Vector3(0.2f, 2.42f, wallF - 0.34f), 0.03f, 0.03f, Vector3.right);
            IronLantern(mb, new Vector3(0.2f, 2.15f, wallF - 0.34f), 1f, lit);
            // the front balcony under the gable: a plank floor on brackets, a railing of cut boards
            float by = HutEave + 0.02f, bz0 = -HutZ - 0.62f, bz1 = -HutZ + 0.05f;
            mb.Color = timber;
            mb.Box(new Vector3(0f, by, (bz0 + bz1) * 0.5f), new Vector3(3.3f, 0.08f, bz1 - bz0));
            for (int s = -1; s <= 1; s += 2)
                Beam(mb, new Vector3(s * 1.3f, by - 0.55f, -HutZ - 0.02f), new Vector3(s * 1.3f, by - 0.05f, bz0 + 0.06f), 0.09f, 0.09f, Vector3.right);
            mb.Color = Paint.Shade(log, 1.18f);
            for (int i = 0; i < 15; i++)
            {
                float x = -1.6f + i * (3.2f / 14f);
                mb.Box(new Vector3(x, by + 0.44f, bz0 + 0.03f), new Vector3(0.15f, 0.8f, 0.04f));
                mb.Color = Paint.Hex("#3A2E28");
                if (i < 14) Ngon(mb, new Vector3(x + 3.2f / 28f, by + 0.5f, bz0 + 0.008f), 0.035f, 6, Vector3.back, Vector3.up, 0f, Paint.Hex("#3A2E28"));
                mb.Color = Paint.Shade(log, 1.18f);
            }
            mb.Color = timber;
            mb.Box(new Vector3(0f, by + 0.86f, bz0 + 0.03f), new Vector3(3.3f, 0.07f, 0.1f));
            for (int s = -1; s <= 1; s += 2)
                mb.Box(new Vector3(s * 1.62f, by + 0.44f, (bz0 + bz1) * 0.5f), new Vector3(0.06f, 0.86f, bz1 - bz0));
            mb.Color = PkSnow;
            mb.Box(new Vector3(0f, by + 0.93f, bz0 + 0.03f), new Vector3(3.2f, 0.06f, 0.13f));
            // a red-and-white blanket airing over the rail
            mb.Color = shutter;
            Slab(mb, new[] { new Vector3(0.45f, by + 0.88f, bz0 - 0.04f), new Vector3(1.15f, by + 0.88f, bz0 - 0.04f), new Vector3(1.12f, by + 0.3f, bz0 - 0.06f), new Vector3(0.48f, by + 0.3f, bz0 - 0.06f) }, Vector3.back, 0.02f);
            mb.Color = Pal.Cream;
            for (int i = 0; i < 3; i++)
                mb.Box(new Vector3(0.8f, by + 0.75f - i * 0.2f, bz0 - 0.07f), new Vector3(0.66f, 0.04f, 0.01f));
            // sides: windows (left two, right one), the woodpile under the left eave, skis, sled and shovel on the right
            mb.Push().Rotate(0f, 90f, 0f);   // local −Z faces −X (local x = −world z)
            Window(mb, -0.75f, 1.6f, 0.62f, 0.72f, -HutX - 0.04f, lit, timber, shutter);
            Window(mb, 0.85f, 1.6f, 0.62f, 0.72f, -HutX - 0.04f, lit, timber, shutter);
            mb.Pop();
            mb.Push().Rotate(0f, -90f, 0f);  // local −Z faces +X (local x = world z)
            Window(mb, -0.5f, 1.6f, 0.62f, 0.72f, -HutX - 0.04f, lit, timber, shutter);
            mb.Pop();
            // back: a window, the back door with a little snow-roofed porch
            mb.Push().Rotate(0f, 180f, 0f);   // local −Z faces +Z (local x = −world x)
            Window(mb, -0.6f, 1.6f, 0.7f, 0.76f, -HutZ - 0.04f, lit, timber, shutter);
            Door(mb, 1.1f, HutPlinth, 0.8f, 1.8f, -HutZ - 0.02f, Paint.Shade(log, 0.85f), timber, false);
            mb.Pop();
            mb.Color = PkStoneLight;
            mb.BoxOn(new Vector3(-1.1f, 0f, HutZ + 0.28f), new Vector3(1.0f, HutPlinth - 0.04f, 0.42f), PkSnowShade);
            // woodpile (left): split logs stacked along the wall with end grain out, a snow blanket on top
            for (int row = 0; row < 4; row++)
                for (int i = 0; i < 9 - row; i++)
                {
                    float z = -1.2f + (i + row * 0.5f) * 0.29f;
                    float y = 0.14f + row * 0.25f;
                    mb.Color = (i + row) % 3 == 0 ? Paint.Shade(log, 1.1f) : log;
                    mb.Push().Translate(-HutX - 0.42f, y, z).Rotate(0f, 0f, 90f);
                    mb.Cylinder(new Vector3(0f, -0.35f, 0f), 0.13f, 0.13f, 0.7f, 6);
                    mb.Pop();
                    Ngon(mb, new Vector3(-HutX - 0.775f, y, z), 0.115f, 6, Vector3.left, Vector3.up, 0f, endGrain);
                }
            SoftLump(mb, new Vector3(-HutX - 0.42f, 1.08f, -0.05f), new Vector3(0.42f, 0.13f, 1.3f), 1, 0f, PkSnowRamp(1.0f, 1.2f), 0.08f);
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(-HutX - 0.1f, 0.95f, -1.45f), new Vector3(-HutX - 0.55f, 1.2f, -1.55f), 0.05f, 0.05f);
            mb.Color = Paint.Hex("#B8B8C4");
            Slab(mb, new[] { new Vector3(-HutX - 0.55f, 1.32f, -1.52f), new Vector3(-HutX - 0.72f, 1.18f, -1.6f), new Vector3(-HutX - 0.58f, 1.0f, -1.58f) }, Vector3.back, 0.03f);
            // right side: skis and poles leaning, a little sled, a shovel
            for (int i = 0; i < 2; i++)
            {
                mb.Color = i == 0 ? shutter : Paint.Hex("#C9A24A");
                var f0 = new Vector3(HutX + 0.45f, 0.02f, -1.35f + i * 0.16f);
                OBox(mb, f0 + new Vector3(-0.2f, 0.85f, 0f), new Vector3(0.05f, 1.75f, 0.09f), Quaternion.Euler(0f, 0f, 13f));
            }
            mb.Color = timber;
            mb.Segment(new Vector3(HutX + 0.5f, 0f, -0.95f), new Vector3(HutX + 0.12f, 1.35f, -0.9f), 0.02f, 0.02f, 4);
            mb.Segment(new Vector3(HutX + 0.55f, 0f, -0.82f), new Vector3(HutX + 0.12f, 1.32f, -0.8f), 0.02f, 0.02f, 4);
            // sled
            mb.Push().Translate(HutX + 0.62f, 0f, 0.55f).Rotate(0f, 90f, 0f);
            mb.Color = Paint.Shade(log, 1.15f);
            mb.Box(new Vector3(0f, 0.3f, 0f), new Vector3(1.1f, 0.05f, 0.42f));
            mb.Color = timber;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(-0.05f, 0.05f, s * 0.18f), new Vector3(1.15f, 0.04f, 0.05f));
                Beam(mb, new Vector3(0.58f, 0.06f, s * 0.18f), new Vector3(0.68f, 0.3f, s * 0.18f), 0.05f, 0.04f, Vector3.forward);
                for (int i = 0; i < 3; i++) mb.Box(new Vector3(-0.4f + i * 0.4f, 0.17f, s * 0.18f), new Vector3(0.04f, 0.24f, 0.04f));
            }
            mb.Pop();
            mb.Color = PkSnow;
            mb.Box(new Vector3(HutX + 0.62f, 0.34f, 0.55f), new Vector3(0.36f, 0.04f, 0.9f));
            // roof: a broad gable (ridge front-to-back) with carved bargeboards, buried under a thick, pillowy snow quilt
            float drop = HutRidge * HutOver / HutX;
            mb.Push().Translate(0f, HutEave, 0f).Rotate(0f, 90f, 0f);
            mb.Color = Paint.Shade(log, 0.7f);
            mb.Roof(Vector3.zero, HutZ * 2f + 0.1f, HutX * 2f, HutRidge, HutOver, 0.14f, false);
            mb.Pop();
            float ex = HutX + HutOver, ey = HutEave - drop, ez = HutZ + HutOver + 0.05f;
            var nL = new Vector3(-HutRidge, ex, 0f).normalized;   // left slope normal
            var nR = new Vector3(HutRidge, ex, 0f).normalized;
            mb.Color = PkSnow;
            const float snowT = 0.22f;
            Slab(mb, new[] { new Vector3(-ex + 0.04f, ey + 0.02f, -ez + 0.03f) + nL * snowT, new Vector3(0f, HutEave + HutRidge, -ez + 0.03f) + nL * snowT,
                             new Vector3(0f, HutEave + HutRidge, ez - 0.03f) + nL * snowT, new Vector3(-ex + 0.04f, ey + 0.02f, ez - 0.03f) + nL * snowT }, nL, snowT);
            mb.Color = PkSnow;
            Slab(mb, new[] { new Vector3(ex - 0.04f, ey + 0.02f, -ez + 0.03f) + nR * snowT, new Vector3(ex - 0.04f, ey + 0.02f, ez - 0.03f) + nR * snowT,
                             new Vector3(0f, HutEave + HutRidge, ez - 0.03f) + nR * snowT, new Vector3(0f, HutEave + HutRidge, -ez + 0.03f) + nR * snowT }, nR, snowT);
            // a rounded snow ridge and soft rolls along both eaves
            var ridgeRamp = PkSnowRamp(ey, HutEave + HutRidge + 0.3f);
            var crest = new List<Vector3[]>();
            for (int i = 0; i <= 8; i++)
            {
                float z = Mathf.Lerp(-ez + 0.02f, ez - 0.02f, i / 8f);
                float y = HutEave + HutRidge + 0.2f + Mathf.Sin(i * 1.9f + b) * 0.025f - (i == 0 || i == 8 ? 0.05f : 0f);
                float w = i == 0 || i == 8 ? 0.26f : 0.34f;
                crest.Add(new[] { new Vector3(-w, y - 0.12f, z), new Vector3(-w * 0.6f, y + 0.06f, z), new Vector3(0f, y + 0.11f, z), new Vector3(w * 0.6f, y + 0.06f, z), new Vector3(w, y - 0.12f, z) });
            }
            Loft(mb, crest, true, true, n => n.y > 0.5f ? PkSnow : PkSnowShade);
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-ez + 0.5f, ez - 0.5f, i / 3f);
                for (int s = -1; s <= 1; s += 2)
                    SoftLump(mb, new Vector3(s * (ex - 0.12f), ey + 0.22f, z + s * 0.1f), new Vector3(0.2f, 0.14f, 0.75f), 1, 0f, ridgeRamp, 0.06f);
            }
            for (int s = -1; s <= 1; s += 2)
                PkIcicles(mb, new Vector3(s * (ex + 0.02f), ey - 0.04f, -ez + 0.2f), new Vector3(s * (ex + 0.02f), ey - 0.04f, ez - 0.2f), 11, 0.45f, sd + s);
            // bargeboards on both gables, with crossed finials
            mb.Color = shutter;
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (ez - 0.02f);
                Beam(mb, new Vector3(-ex - 0.02f, ey - 0.05f, z), new Vector3(0f, HutEave + HutRidge + 0.05f, z), 0.08f, 0.24f, new Vector3(0f, 0f, 1f));
                Beam(mb, new Vector3(ex + 0.02f, ey - 0.05f, z), new Vector3(0f, HutEave + HutRidge + 0.05f, z), 0.08f, 0.24f, new Vector3(0f, 0f, 1f));
                mb.Color = timber;
                Beam(mb, new Vector3(-0.32f, HutEave + HutRidge - 0.2f, z), new Vector3(0.26f, HutEave + HutRidge + 0.45f, z), 0.08f, 0.08f, new Vector3(0f, 0f, 1f));
                Beam(mb, new Vector3(0.32f, HutEave + HutRidge - 0.2f, z), new Vector3(-0.26f, HutEave + HutRidge + 0.45f, z), 0.08f, 0.08f, new Vector3(0f, 0f, 1f));
                mb.Color = shutter;
                PkIcicles(mb, new Vector3(-ex * 0.7f, ey + HutRidge * 0.3f - 0.1f, z), new Vector3(-0.3f, HutEave + HutRidge - 0.35f, z), 4, 0.25f, sd + 10 + s);
                PkIcicles(mb, new Vector3(0.3f, HutEave + HutRidge - 0.35f, z), new Vector3(ex * 0.7f, ey + HutRidge * 0.3f - 0.1f, z), 4, 0.25f, sd + 20 + s);
                mb.Color = shutter;
            }
            // stone chimney through the right slope, snow on its cap
            Chimney(mb, new Vector3(1.05f, HutEave + HutRidge * 0.45f, 0.95f), 0.62f, 0.6f, 1.5f, PkStoneLight, -2f);
            mb.Color = PkSnow;
            mb.Box(new Vector3(1.03f, HutEave + HutRidge * 0.45f + 1.66f, 0.95f), new Vector3(0.72f, 0.08f, 0.7f));
            // snow banked against the walls and the plinth corners
            var bank = PkSnowRamp(-0.05f, 0.45f);
            SoftLump(mb, new Vector3(1.6f, 0f, -HutZ - 0.2f), new Vector3(0.75f, 0.32f, 0.3f), 1, 0f, bank, 0.1f);
            SoftLump(mb, new Vector3(-HutX - 0.15f, 0f, 1.45f), new Vector3(0.35f, 0.38f, 0.6f), 1, 0f, bank, 0.1f);
            SoftLump(mb, new Vector3(HutX + 0.2f, 0f, 1.5f), new Vector3(0.38f, 0.3f, 0.55f), 1, 0f, bank, 0.1f);
            SoftLump(mb, new Vector3(0.6f, 0f, HutZ + 0.2f), new Vector3(0.7f, 0.28f, 0.3f), 1, 0f, bank, 0.1f);
            return mb;
        }

        /// <summary>Hut window box: evergreen sprigs with red berries.</summary>
        static void HutBox(MeshBuilder mb, float x, float y, float wallZ, int b)
        {
            mb.Color = Pal.Wood;
            mb.Box(new Vector3(x, y, wallZ - 0.14f), new Vector3(0.9f, 0.18f, 0.24f));
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(x - 0.36f + i * 0.145f, y + 0.1f, wallZ - 0.14f + ((i % 2) - 0.5f) * 0.06f);
                mb.Color = i % 2 == 0 ? Paint.Hex("#3F6B4E") : Paint.Hex("#527E5A");
                mb.Cone(p, 0.08f, 0.24f + 0.05f * ((i + b) % 3), 5);
                mb.Color = Pal.Apple;
                Gem(mb, p + new Vector3(0.04f, 0.1f, -0.06f), 0.026f);
            }
            mb.Color = PkSnow;
            mb.Box(new Vector3(x, y + 0.1f, wallZ - 0.14f), new Vector3(0.92f, 0.03f, 0.25f));
        }

        // ------------------------------------------------------------------ prayer flags (poles 3.4 / 2.8 m, 4 m apart; no collider)

        static readonly Color[] PkFlagCols = { Paint.Hex("#4A7FC1"), Paint.Hex("#F3EFE4"), Paint.Hex("#D2543E"), Paint.Hex("#5E9E58"), Paint.Hex("#F0C64A") };

        static MeshBuilder BuildPrayerFlags(int b)
        {
            var mb = Builder(VariantSeed("prop_prayer_flags", b), 0.06f, 0.25f, 0.5f);
            int sd = VariantSeed("flags", b);
            var wood = Paint.Hex("#8A7360");
            var poleL = new Vector3(-2.0f, 0f, 0.05f);
            var poleR = new Vector3(2.05f, 0f, -0.05f);
            var topL = poleL + new Vector3(-0.08f, 3.4f + b * 0.08f, 0f);
            var topR = poleR + new Vector3(0.1f, 2.8f, 0.02f);
            foreach (var (foot, top) in new[] { (poleL, topL), (poleR, topR) })
            {
                mb.Color = wood;
                mb.Segment(foot + Vector3.down * 0.1f, top, 0.075f, 0.05f, 6);
                mb.Color = Pal.Brass;
                mb.Sphere(top + Vector3.up * 0.06f, new Vector3(0.07f, 0.07f, 0.07f), 6, 4, false);
                mb.Cone(top + Vector3.up * 0.1f, 0.035f, 0.16f, 5);
                // a white scarf tied below the top, its tails lifting in the wind
                mb.Color = Pal.Paper;
                var k = top + Vector3.down * 0.35f;
                mb.Torus(k, 0.08f, 0.025f, 8, 3);
                mb.Wind = 1.6f;
                Slab(mb, new[] { k + new Vector3(0.06f, -0.02f, -0.06f), k + new Vector3(0.12f, -0.02f, -0.07f), k + new Vector3(0.34f, -0.42f, -0.12f), k + new Vector3(0.26f, -0.46f, -0.11f) }, new Vector3(0.2f, 0f, -1f), 0.008f);
                mb.Wind = 0f;
                // a cairn of stones about the foot, snow in the gaps
                for (int i = 0; i < 6; i++)
                {
                    float a = (i * 60f + b * 25f) * Mathf.Deg2Rad;
                    float rr = i < 4 ? 0.3f : 0.14f;
                    var c = foot + new Vector3(Mathf.Cos(a) * rr, i < 4 ? 0.1f : 0.32f, Mathf.Sin(a) * rr * 0.85f);
                    FacetBlob(mb, c, new Vector3(0.18f, 0.12f, 0.15f) * (i < 4 ? 1f : 0.8f), 0, 0.2f, sd + i * 3 + (int)(foot.x * 10f), 0.3f, PkSnowy(i % 2 == 0 ? PkStone : PkStoneLight, 0.6f));
                }
                SoftLump(mb, foot + new Vector3(0f, -0.04f, -0.05f), new Vector3(0.48f, 0.13f, 0.4f), 1, foot.x * 30f, PkSnowRamp(-0.05f, 0.12f), 0.1f);
            }
            // a carved mani stone leaning on the left cairn
            mb.Color = PkStoneLight;
            OBox(mb, poleL + new Vector3(0.45f, 0.24f, -0.25f), new Vector3(0.5f, 0.42f, 0.09f), Quaternion.Euler(-14f, 18f, 0f));
            mb.Color = Paint.Hex("#E9D9A6");
            for (int i = 0; i < 3; i++)
                OBox(mb, poleL + new Vector3(0.45f + (i - 1) * 0.12f, 0.27f, -0.302f) + new Vector3(0f, 0f, -0.012f), new Vector3(0.07f, 0.16f, 0.01f), Quaternion.Euler(-14f, 18f, 0f));
            // three strings of square flags: top to top, a lower one, and a long one down to a stake
            mb.Wind = 1.5f;
            WildPennants(mb, topL + new Vector3(0.05f, -0.15f, 0f), topR + new Vector3(-0.05f, -0.1f, 0f), 0.42f, 12, PkFlagCols, 0.22f, true, Pal.RopeStraw);
            var cols2 = new[] { PkFlagCols[2], PkFlagCols[3], PkFlagCols[4], PkFlagCols[0], PkFlagCols[1] };
            WildPennants(mb, poleL + new Vector3(-0.02f, 2.45f, 0f), poleR + new Vector3(0.05f, 2.05f, 0f), 0.3f, 11, cols2, 0.2f, true, Pal.RopeStraw);
            var stake = new Vector3(3.3f, 0.32f, 0.65f);
            WildPennants(mb, topR + new Vector3(0.02f, -0.12f, 0.02f), stake, 0.25f, 6, PkFlagCols, 0.18f, true, Pal.RopeStraw);
            mb.Wind = 0f;
            mb.Color = wood;
            mb.Segment(stake + Vector3.down * 0.4f, stake + Vector3.up * 0.06f, 0.035f, 0.03f, 4);
            return mb;
        }

        // ------------------------------------------------------------------ ruined tower (≈ 8 m, Ø 3.5 m; collider 4.0 × 4.0)

        static MeshBuilder BuildRuinedTower(int b)
        {
            var mb = Builder(VariantSeed("prop_ruined_tower", b), 0.05f, 0.35f, 1.4f);
            int sd = VariantSeed("rtower", b);
            const int N = 10;
            const float R = 1.75f, T = 0.5f, CH = 0.55f;
            float[] tops = { 7.4f, 8.2f, 7.6f, 6.0f, 4.6f, 3.9f, 5.1f, 6.3f, 7.0f, 6.6f };
            var stone = Paint.Hsv(PkStone, (b - 1.5f) * 4f);
            var light = Paint.Hsv(PkStoneLight, (b - 1.5f) * 4f);
            var scorch = Paint.Hex("#4A4448");
            // per side: centre angle, top height and the open spans (door front, breach right)
            const int door = 7;                                  // centred on −Z
            int breach = 3 + (b % 2);                            // a collapsed hole on the back right
            float Top(int j) => tops[(j + b * 3) % N] + (j == door ? 0.4f : 0f);
            bool Open(int j, float y0, float y1)
            {
                if (j == door && y0 < 2.15f) return true;
                if (j == breach && y1 > 2.9f && y0 < 4.6f) return true;
                if (j == breach + 1 && y1 > 3.4f && y0 < 4.3f) return true;
                return false;
            }
            for (int j = 0; j < N; j++)
            {
                float ang = (j + 0.5f) * 360f / N;
                float rad = ang * Mathf.Deg2Rad;
                var outN = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
                float chord = 2f * R * Mathf.Sin(Mathf.PI / N) + 0.06f;
                float top = Top(j);
                var rot = Quaternion.LookRotation(outN, Vector3.up);
                int nc = Mathf.CeilToInt(top / CH);
                for (int k = 0; k < nc; k++)
                {
                    float y0 = k * CH, y1 = Mathf.Min(top, y0 + CH);
                    if (Open(j, y0, y1)) continue;
                    bool last = k == nc - 1;
                    // ragged top courses: shortened and shifted along the wall
                    float w = chord, shift = 0f;
                    if (last || k == nc - 2)
                    {
                        float h = WildHash(j, k, sd);
                        if (last) { w = chord * (0.55f + 0.3f * Mathf.Abs(h)); shift = h * chord * 0.2f; }
                    }
                    float proud = WildHash(j, k + 50, sd) * 0.025f;
                    bool burnt = y0 > 4.2f && (j == 4 || j == 5 || j == 6) || (y0 > 5.5f && j == 3);
                    mb.Color = burnt ? Color.Lerp(scorch, stone, Mathf.Abs(WildHash(j, k, sd + 1)) * 0.5f) : ((j + k) % 3 == 0 ? light : (WildHash(j, k, sd + 2) > 0.3f ? Paint.Shade(stone, 0.93f) : stone));
                    var right = rot * Vector3.right;
                    var c = outN * (R - T * 0.5f + proud) + right * shift + Vector3.up * ((y0 + y1) * 0.5f);
                    mb.Push().Translate(c).Rotate(rot);
                    mb.Box(Vector3.zero, new Vector3(w, (y1 - y0) - 0.03f, T), last ? PkSnow : (Color?)null);
                    mb.Pop();
                }
                // the inside face in shade
                var p0 = new Vector3(Mathf.Cos((ang - 18f) * Mathf.Deg2Rad), 0f, Mathf.Sin((ang - 18f) * Mathf.Deg2Rad)) * (R - T - 0.02f);
                var p1 = new Vector3(Mathf.Cos((ang + 18f) * Mathf.Deg2Rad), 0f, Mathf.Sin((ang + 18f) * Mathf.Deg2Rad)) * (R - T - 0.02f);
                var inner = Paint.Hex("#5B5866");
                for (int k = 0; k < nc; k++)
                {
                    float y0 = k * CH, y1 = Mathf.Min(top - 0.05f, y0 + CH);
                    if (y1 <= y0 || Open(j, y0, y0 + CH)) continue;
                    QuadC(mb, p0 + Vector3.up * y0, p1 + Vector3.up * y0, p1 + Vector3.up * y1, p0 + Vector3.up * y1, -outN, Color.Lerp(inner, Paint.Shade(inner, 1.25f), y1 / 8f));
                }
                // arrow slits
                if (j != door && j != breach && (j % 3 == 1))
                    foreach (float sy in new[] { 3.3f, 5.6f })
                        if (sy + 0.5f < top && !Open(j, sy - 0.4f, sy + 0.4f))
                            QuadC(mb, outN * (R + 0.012f) + rot * Vector3.right * -0.07f + Vector3.up * (sy - 0.4f), outN * (R + 0.012f) + rot * Vector3.right * 0.07f + Vector3.up * (sy - 0.4f),
                                  outN * (R + 0.012f) + rot * Vector3.right * 0.07f + Vector3.up * (sy + 0.4f), outN * (R + 0.012f) + rot * Vector3.right * -0.07f + Vector3.up * (sy + 0.4f), outN, Paint.Hex("#2E2A32"));
            }
            // a string course with snow on its ledge
            mb.Push().Translate(0f, 2.62f, 0f);
            mb.Lathe(new[] { new Vector2(R + 0.02f, 0f), new Vector2(R + 0.13f, 0.06f), new Vector2(R + 0.13f, 0.16f), new Vector2(R + 0.02f, 0.2f) }, N, false, false, false,
                     new[] { Paint.Shade(stone, 0.85f), light, PkSnow, PkSnow }, 0f);
            mb.Pop();
            // the doorway: a lintel stone and a broken, half-open door
            {
                float rad = (door + 0.5f) * 360f / N * Mathf.Deg2Rad;
                var outN = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
                var rot = Quaternion.LookRotation(outN, Vector3.up);
                mb.Color = light;
                mb.Push().Translate(outN * (R - T * 0.5f + 0.04f) + Vector3.up * 2.32f).Rotate(rot);
                mb.Box(Vector3.zero, new Vector3(1.35f, 0.34f, T + 0.08f), PkSnow);
                mb.Pop();
                mb.Color = Paint.Hex("#6B5242");
                var hinge = outN * (R - 0.05f) + rot * Vector3.right * -0.45f;
                mb.Push().Translate(hinge).Rotate(rot * Quaternion.Euler(0f, -52f, 0f));
                mb.Box(new Vector3(0.36f, 0.85f, -0.05f), new Vector3(0.7f, 1.7f, 0.07f));
                mb.Color = Pal.Iron;
                mb.Box(new Vector3(0.36f, 0.45f, -0.1f), new Vector3(0.66f, 0.06f, 0.02f));
                mb.Box(new Vector3(0.36f, 1.35f, -0.1f), new Vector3(0.66f, 0.06f, 0.02f));
                mb.Pop();
                mb.Color = PkStoneLight;
                mb.BoxOn(outN * (R + 0.25f), new Vector3(1.2f, 0.16f, 0.5f), PkSnowShade);
                // dark floor of the interior
                mb.Color = Paint.Hex("#4A4650");
                mb.Disc(new Vector3(0f, 0.03f, 0f), R - T, N);
                // the burnt floor joists of the upper storey: stubs in their sockets, one beam fallen askew
                mb.Color = Paint.Hex("#3E3434");
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * 0.55f;
                    float half = Mathf.Sqrt(Mathf.Max(0f, (R - 0.3f) * (R - 0.3f) - x * x));
                    float y = 4.75f + i * 0.03f;
                    if (i == 1)
                        Beam(mb, new Vector3(x, y, half), new Vector3(x + 0.2f, y - 1.6f, -half + 0.4f), 0.18f, 0.16f);
                    else
                    {
                        Beam(mb, new Vector3(x, y, half), new Vector3(x, y + 0.04f, half - 0.75f), 0.18f, 0.16f);
                        Beam(mb, new Vector3(x, y, -half), new Vector3(x, y - 0.05f, -half + 0.6f + 0.3f * i), 0.18f, 0.16f);
                    }
                    mb.Color = PkSnow;
                    mb.Box(new Vector3(x, y + 0.1f, half - 0.3f), new Vector3(0.17f, 0.04f, 0.5f));
                    mb.Color = Paint.Hex("#3E3434");
                }
            }
            // a tattered Heronguard banner hanging from a pole high on the front left
            {
                var anchor = new Vector3(-1.05f, 6.9f, -1.35f);
                mb.Color = Paint.Hex("#5C4A3C");
                mb.Segment(anchor + new Vector3(0.25f, 0f, 0.35f), anchor + new Vector3(-0.3f, 0.05f, -0.45f), 0.05f, 0.04f, 5);
                mb.Wind = 1.3f;
                var blue = Paint.Hex("#3E6FA8");
                var a0 = anchor + new Vector3(0.12f, -0.02f, 0.18f);
                var a1 = anchor + new Vector3(-0.22f, 0.02f, -0.32f);
                var down = Vector3.down;
                var nrm = Vector3.Cross(a1 - a0, down).normalized;
                if (nrm.z > 0f) nrm = -nrm;
                float[] lens = { 1.7f, 1.35f, 1.55f, 1.0f, 1.45f };
                for (int i = 0; i < lens.Length; i++)
                {
                    var p = Vector3.Lerp(a0, a1, i / (float)lens.Length);
                    var q = Vector3.Lerp(a0, a1, (i + 1) / (float)lens.Length);
                    mb.Color = i % 2 == 0 ? blue : Paint.Shade(blue, 0.92f);
                    float l = lens[(i + b) % lens.Length];
                    Slab(mb, new[] { p, q, q + down * (l - 0.1f), p + down * l }, nrm, 0.012f);
                }
                // the heron's white silhouette and the gold edge
                mb.Color = Pal.Paper;
                var mid = Vector3.Lerp(a0, a1, 0.5f) + down * 0.55f + nrm * 0.015f;
                Slab(mb, new[] { mid + new Vector3(0f, 0.22f, 0f), mid + new Vector3(0.08f, 0.02f, -0.04f), mid + new Vector3(0f, -0.24f, 0f), mid + new Vector3(-0.08f, 0.02f, 0.04f) }, nrm, 0.01f);
                mb.Color = Pal.Gold;
                Beam(mb, a0 + down * 0.05f + nrm * 0.015f, a1 + down * 0.05f + nrm * 0.015f, 0.04f, 0.04f);
                mb.Wind = 0f;
            }
            // fallen blocks under the breach and by the door, snow on them; icicles under the string course
            float br = (breach + 0.5f) * 360f / N * Mathf.Deg2Rad;
            var bn = new Vector3(Mathf.Cos(br), 0f, Mathf.Sin(br));
            var bt = new Vector3(-bn.z, 0f, bn.x);
            for (int i = 0; i < 7; i++)
            {
                var p = bn * (R + 0.25f + 0.15f * (i % 3)) + bt * ((i - 3) * 0.32f) + Vector3.up * (0.13f + (i == 3 ? 0.22f : 0f));
                mb.Color = i % 2 == 0 ? stone : light;
                OBox(mb, p, new Vector3(0.48f, 0.3f, 0.36f), Quaternion.Euler(WildHash(i, 1, sd) * 18f, i * 37f, WildHash(i, 2, sd) * 14f), PkSnow);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = (door + 0.5f) * 360f / N * Mathf.Deg2Rad + (i - 1) * 0.55f + 0.9f;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R + 0.4f) + Vector3.up * 0.12f;
                mb.Color = light;
                OBox(mb, p, new Vector3(0.38f, 0.24f, 0.3f), Quaternion.Euler(0f, i * 50f + 10f, 6f), PkSnow);
            }
            for (int j = 0; j < N; j++)
            {
                if (j == breach) continue;
                float a0 = j * 360f / N * Mathf.Deg2Rad, a1 = (j + 1) * 360f / N * Mathf.Deg2Rad;
                PkIcicles(mb, new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (R + 0.1f) + Vector3.up * 2.6f,
                          new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (R + 0.1f) + Vector3.up * 2.6f, 3, 0.3f, sd + j);
            }
            var bank = PkSnowRamp(-0.05f, 0.4f);
            SoftLump(mb, bn * (R + 0.35f), new Vector3(0.9f, 0.3f, 0.9f), 1, 0f, bank, 0.12f);
            SoftLump(mb, new Vector3(1.3f, 0f, -1.3f), new Vector3(0.6f, 0.28f, 0.45f), 1, 40f, bank, 0.12f);
            SoftLump(mb, new Vector3(-1.5f, 0f, 0.9f), new Vector3(0.55f, 0.32f, 0.5f), 1, -30f, bank, 0.12f);
            WildTuft(mb, new Vector3(-1.75f, 0f, -0.75f), 0.36f, 5, PkFrostGrass, PkFrostGrassDark);
            return mb;
        }

        // ------------------------------------------------------------------ dragon bones (≈ 7.8 m long, ribs 3 m; collider 8.0 × 4.2)

        static MeshBuilder BuildDragonBones(int b)
        {
            var mb = Builder(VariantSeed("prop_dragon_bones", b), 0.05f, 0.3f, 0.8f);
            int sd = VariantSeed("dragonbones", b);
            var bone = Paint.Hsv(WildBoneCol, (b - 1.5f) * 3f);
            var boneOld = Color.Lerp(WildBoneDark, bone, 0.4f);
            mb.Push().Translate(0.45f, 0f, 0f);   // centre the skeleton (skull tip to tail) on the pivot
            // the spine: vertebrae along a gentle S from the neck (left) to a curled tail (right), half sunk in snow
            var spine = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                float x = Mathf.Lerp(-2.0f, 3.2f, t);
                float z = 0.18f * Mathf.Sin(x * 0.9f + b) + (t > 0.8f ? (t - 0.8f) * 4.5f : 0f);
                float y = 0.28f - 0.12f * t + 0.08f * Mathf.Sin(t * 5f);
                if (t > 0.85f) x -= (t - 0.85f) * 2.2f;
                spine.Add(new Vector3(x, y, z));
            }
            for (int i = 0; i < spine.Count; i++)
            {
                float t = i / (float)(spine.Count - 1);
                float r = Mathf.Lerp(0.24f, 0.07f, t);
                var p = spine[i];
                var d = (i < spine.Count - 1 ? spine[i + 1] - p : p - spine[i - 1]).normalized;
                mb.Color = i % 2 == 0 ? bone : boneOld;
                mb.Segment(p - d * r * 0.55f, p + d * r * 0.55f, r, r * 0.9f, 7);
                mb.Color = bone;
                var spike = p + Vector3.up * (r + Mathf.Lerp(0.42f, 0.12f, t)) + d * 0.08f;
                mb.Segment(p + Vector3.up * r * 0.6f, spike, r * 0.45f, 0.01f, 4);
                var side = Vector3.Cross(d, Vector3.up).normalized;
                mb.Segment(p - side * r * 0.5f, p - side * (r + 0.18f) + Vector3.up * 0.04f, r * 0.25f, 0.02f, 4);
                mb.Segment(p + side * r * 0.5f, p + side * (r + 0.18f) + Vector3.up * 0.04f, r * 0.25f, 0.02f, 4);
            }
            // the neck down to the skull, which rests on its jaw facing the camera's left
            var neck0 = spine[0];
            var skullAt = new Vector3(-2.75f, 0.0f, -0.35f);
            for (int i = 1; i <= 4; i++)
            {
                var p = Vector3.Lerp(neck0, skullAt + new Vector3(0.45f, 0.45f, 0.2f), i / 4.5f) + Vector3.up * Mathf.Sin(i / 4.5f * Mathf.PI) * 0.25f;
                mb.Color = i % 2 == 0 ? bone : boneOld;
                mb.Sphere(p, new Vector3(0.2f, 0.17f, 0.17f), 7, 5, false);
                mb.Color = bone;
                mb.Segment(p + Vector3.up * 0.12f, p + new Vector3(0.05f, 0.42f, 0f), 0.08f, 0.01f, 4);
            }
            // a long reptilian head: the beast skull stretched along its snout, horns swept back from the crown, cheek spikes
            mb.Push().Translate(skullAt).Rotate(-6f, 62f + (b - 1.5f) * 5f, 0f).Scale(new Vector3(1f, 0.92f, 1.45f));
            WildSkull(mb, Vector3.zero, 4.6f, 0f, 0f, bone, 0f);
            mb.Push().Scale(4.6f);
            var hornCol = Paint.Hex("#6E5E54");
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = hornCol;
                WildRib(mb, new[] { new Vector3(s * 0.08f, 0.25f, 0.1f), new Vector3(s * 0.12f, 0.31f, 0.24f), new Vector3(s * 0.15f, 0.33f, 0.4f), new Vector3(s * 0.13f, 0.28f, 0.55f) }, 0.04f, 0.004f, 6);
                WildRib(mb, new[] { new Vector3(s * 0.13f, 0.18f, 0.12f), new Vector3(s * 0.2f, 0.2f, 0.24f), new Vector3(s * 0.22f, 0.17f, 0.34f) }, 0.022f, 0.003f, 5);
                mb.Color = bone;
                mb.Segment(new Vector3(s * 0.1f, 0.24f, -0.06f), new Vector3(s * 0.11f, 0.29f, 0.04f), 0.018f, 0.004f, 4);
            }
            mb.Pop();
            mb.Pop();
            // the ribs: arches rising out of the snow on both sides, tips curling in; one broken, one fallen
            float[] rx = { -1.4f, -0.75f, -0.1f, 0.55f, 1.2f, 1.8f };
            float[] rh = { 2.35f, 2.85f, 3.05f, 2.8f, 2.25f, 1.55f };
            int broken = 1 + b % 3;
            for (int i = 0; i < rx.Length; i++)
            {
                int si = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(-2.0f, 3.2f, rx[i]) * 16f), 0, 16);
                var root = spine[si];
                for (int s = -1; s <= 1; s += 2)
                {
                    float H = rh[i] * (s > 0 ? 0.94f : 1f);
                    // each rib sweeps back towards the tail as it rises (a curved tusk in profile) and curls in at the tip
                    float rake = 0.55f + i * 0.1f;
                    var pts = new List<Vector3>
                    {
                        root + new Vector3(0f, 0.05f, s * 0.2f),
                        root + new Vector3(-rake * 0.12f, H * 0.32f, s * 1.0f),
                        root + new Vector3(rake * 0.12f, H * 0.66f, s * 1.22f),
                        root + new Vector3(rake * 0.6f, H * 0.9f, s * 1.0f),
                        root + new Vector3(rake, H, s * 0.55f),
                    };
                    if (i == broken && s < 0) pts.RemoveRange(3, 2);
                    mb.Color = (i + (s > 0 ? 1 : 0)) % 2 == 0 ? bone : Color.Lerp(bone, boneOld, 0.5f);
                    WildRib(mb, pts, 0.16f - i * 0.012f, i == broken && s < 0 ? 0.08f : 0.025f, 6);
                    // snow caught on the outer curve
                    mb.Color = PkSnow;
                    mb.Sphere(pts[1] + new Vector3(0f, 0.1f, s * 0.05f), new Vector3(0.14f, 0.07f, 0.12f), 6, 3, true);
                }
            }
            // a fallen rib on the ground in front
            mb.Color = boneOld;
            WildRib(mb, new[] { new Vector3(0.9f, 0.08f, -1.75f), new Vector3(1.6f, 0.14f, -1.55f), new Vector3(2.3f, 0.1f, -1.6f), new Vector3(2.75f, 0.06f, -1.35f) }, 0.11f, 0.03f, 6);
            // a folded wing on the far side: arm bones and long fingers fanned on the snow
            mb.Color = bone;
            var shoulder = spine[5] + new Vector3(0f, 0.1f, 0.55f);
            var elbow = shoulder + new Vector3(0.9f, 0.05f, 0.75f);
            var wrist = elbow + new Vector3(1.0f, -0.05f, -0.15f);
            WildBone(mb, shoulder, elbow, 0.1f, bone);
            WildBone(mb, elbow, wrist, 0.08f, boneOld);
            for (int f = 0; f < 3; f++)
            {
                var tip = wrist + new Vector3(0.55f + f * 0.25f, -0.12f, 0.75f - f * 0.42f);
                mb.Color = f % 2 == 0 ? bone : boneOld;
                WildRib(mb, new[] { wrist, Vector3.Lerp(wrist, tip, 0.5f) + Vector3.up * 0.12f, tip }, 0.06f, 0.015f, 5);
            }
            // a broken spear lodged between the ribs, a scrap of pennant on it
            var sp0 = spine[7] + new Vector3(0.1f, 0.1f, -0.6f);
            var sp1 = sp0 + new Vector3(-0.55f, 1.8f, -0.55f);
            mb.Color = Paint.Hex("#6B5242");
            mb.Segment(sp0, sp1, 0.035f, 0.03f, 5);
            mb.Color = Pal.Iron;
            mb.Segment(sp1, sp1 + (sp1 - sp0).normalized * 0.25f, 0.05f, 0.005f, 4);
            mb.Wind = 1.3f;
            mb.Color = Paint.Hex("#3E6FA8");
            var pa = sp1 + (sp0 - sp1).normalized * 0.1f;
            Slab(mb, new[] { pa, pa + (sp0 - sp1).normalized * 0.26f, pa + new Vector3(0.38f, -0.2f, -0.05f) }, new Vector3(0.3f, 0f, -1f), 0.01f);
            mb.Wind = 0f;
            // snow drifts piled against the spine and under the skull
            var bank = PkSnowRamp(-0.05f, 0.38f);
            SoftLump(mb, new Vector3(-0.4f, 0f, -0.35f), new Vector3(1.4f, 0.3f, 0.42f), 1, 6f, bank, 0.1f);
            SoftLump(mb, new Vector3(1.4f, 0f, 0.45f), new Vector3(1.3f, 0.32f, 0.5f), 1, -8f, bank, 0.1f);
            SoftLump(mb, new Vector3(2.7f, 0f, 0.6f), new Vector3(0.7f, 0.26f, 0.45f), 1, 20f, bank, 0.1f);
            SoftLump(mb, skullAt + new Vector3(0.3f, -0.05f, 0.25f), new Vector3(0.75f, 0.24f, 0.55f), 1, 30f, bank, 0.1f);
            SoftLump(mb, new Vector3(-1.2f, 0f, 0.55f), new Vector3(0.8f, 0.26f, 0.4f), 1, 0f, bank, 0.1f);
            mb.Color = PkStoneDark;
            Gem(mb, new Vector3(-1.9f, 0.04f, -1.2f), new Vector3(0.14f, 0.08f, 0.1f));
            Gem(mb, new Vector3(3.0f, 0.04f, -0.75f), new Vector3(0.11f, 0.07f, 0.09f));
            WildTuft(mb, new Vector3(-0.4f, 0f, -1.45f), 0.34f, 5, PkFrostGrass, PkFrostGrassDark);
            mb.Pop();
            return mb;
        }
    }
}
