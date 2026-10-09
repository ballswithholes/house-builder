// Expansion dungeon props, caves and water/ice (props-dungeon): the cave mouth (outcrop with a dark opening; also the
// "cave" transition marker), stalagmites, glowing crystal clusters (tintable), glow mushrooms, great-tree roots coming
// down through a cave, rock wall pieces, ice pillars, the frozen knight, the barnacled drowned arch and the treasure pile.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterDungeonCave(Dictionary<string, Recipe> r)
        {
            r["prop_cave_mouth"] = CaveMouth;
            r["prop_cave_mouth_snow"] = CaveMouth;   // Skyreach: snow caps and icicles instead of moss and ivy
            r["prop_stalagmite"] = (art, seed) => Simple(art, seed, BuildStalagmite, 0.5f);
            r["prop_crystal_cluster"] = (art, seed) => DgGlow(art, seed, BuildCrystalCluster, 0.6f, new Vector3(0f, 0.9f, 0f));
            r["prop_glow_mushroom"] = (art, seed) => DgGlow(art, seed, BuildGlowMushroom, 0.6f, new Vector3(0f, 0.95f, 0f));
            r["prop_root_column"] = (art, seed) => Simple(art, seed, BuildRootColumn, 0.8f);
            r["prop_cave_wall"] = (art, seed) => Simple(art, seed, BuildCaveWall, 2.0f);
            r["prop_ice_pillar"] = (art, seed) => DgGlow(art, seed, BuildIcePillar, 0.5f, new Vector3(0f, 1.6f, 0f));
            r["prop_frozen_statue"] = (art, seed) => Simple(art, seed, BuildFrozenStatue, 0.5f);
            r["prop_drowned_arch"] = (art, seed) => Simple(art, seed, BuildDrownedArch, 2.2f);
            r["prop_treasure_pile"] = (art, seed) => DgGlow(art, seed, BuildTreasurePile, 0.8f, new Vector3(0f, 0.6f, 0f));
        }

        // ------------------------------------------------------------------ cave mouth (prop collider 6.0 × 1.8; as a marker none)

        const float CaveHalfW = 1.08f, CaveSpring = 1.3f, CaveZ0 = 0.0f, CaveZ1 = 1.7f;

        static PropModel CaveMouth(string art, int seed)
        {
            int b = Bucket(seed);
            var rig = new Rig(art);
            bool snow = art.EndsWith("_snow");
            rig.Body(Cached(art + "#" + b, () => BuildCaveMouth(b, snow)));
            rig.Part("Opening", Cached("prop_cave_mouth#in", () =>
            {
                var mb = Builder(VariantSeed("prop_cave_mouth_in", 0), 0.03f, 0f, 0.1f);
                DgOpening(mb, CaveHalfW, CaveSpring, CaveZ0, CaveZ1, Paint.Shade(DgPal.RockDark, 0.6f), DgPal.Void, 8, 4);
                return mb;
            }), Vector3.zero, Quaternion.identity, Skin.Plain);
            return rig.Done(2.4f);
        }

        static MeshBuilder BuildCaveMouth(int b, bool snow)
        {
            var mb = Builder(VariantSeed(snow ? "prop_cave_mouth_snow" : "prop_cave_mouth", b), 0.07f, 0.35f, 0.7f);
            var rock = Paint.Hsv(Color.Lerp(DgPal.Rock, Pal.Stone, 0.6f), (b - 1.5f) * 5f);
            var rockB = Paint.Shade(rock, 0.92f);
            var rockL = Color.Lerp(rock, Pal.StoneLight, 0.45f);
            var moss = snow ? DgPal.Snow : Color.Lerp(Pal.Moss, Pal.MossLight, b * 0.12f);
            if (snow) { rock = Color.Lerp(rock, DgPal.IceDeep, 0.12f); rockB = Paint.Shade(rock, 0.92f); }
            var mossy = Mossy(rock, moss, snow ? 0.42f : 0.5f);
            var mossyB = Mossy(rockB, Paint.Shade(moss, 0.95f), snow ? 0.45f : 0.55f);
            var grass = snow ? Paint.Hex("#C9B48A") : Pal.Sage;
            var grassL = snow ? Paint.Hex("#DCC9A0") : Pal.MossLight;
            // the outcrop: two shouldering boulders, smaller ones at their feet, a brow boulder over the mouth and the hill
            // behind (all set back so the front stays inside the collider)
            FacetBlob(mb, new Vector3(-1.95f, 1.05f, 0.5f), new Vector3(1.05f, 1.15f, 0.95f), 1, 0.18f, b * 7 + 1, 0.25f, mossy);
            FacetBlob(mb, new Vector3(2.0f, 0.9f, 0.55f), new Vector3(0.98f, 1.0f, 0.92f), 1, 0.18f, b * 7 + 2, 0.25f, mossyB);
            FacetBlob(mb, new Vector3(-2.55f, 0.42f, 0.3f), new Vector3(0.5f, 0.46f, 0.48f), 1, 0.2f, b * 7 + 3, 0.3f, mossyB);
            FacetBlob(mb, new Vector3(2.55f, 0.36f, 0.42f), new Vector3(0.42f, 0.38f, 0.42f), 1, 0.2f, b * 7 + 4, 0.3f, mossy);
            FacetBlob(mb, new Vector3(0.1f, 2.95f, 0.75f), new Vector3(2.05f, 0.85f, 1.15f), 1, 0.15f, b * 7 + 5, 0.3f, mossy);
            FacetBlob(mb, new Vector3(-0.2f, 1.9f, 2.7f), new Vector3(3.0f, 2.1f, 1.55f), 1, 0.12f, b * 7 + 6, 0.4f, mossyB);
            FacetBlob(mb, new Vector3(1.1f, 3.75f, 1.55f), new Vector3(1.0f, 0.55f, 0.85f), 1, 0.18f, b * 7 + 7, 0.3f, mossy);
            // a rough ring of paler rocks framing the opening, keystone on top
            const int ring = 9;
            for (int i = 0; i < ring; i++)
            {
                float t = (float)i / (ring - 1);
                Vector3 c;
                if (t < 0.18f) c = new Vector3(-CaveHalfW - 0.24f, Mathf.Lerp(0.3f, CaveSpring, t / 0.18f), -0.12f);
                else if (t > 0.82f) c = new Vector3(CaveHalfW + 0.24f, Mathf.Lerp(CaveSpring, 0.3f, (t - 0.82f) / 0.18f), -0.12f);
                else
                {
                    float a = Mathf.PI * (1f - (t - 0.18f) / 0.64f);
                    c = new Vector3(Mathf.Cos(a) * (CaveHalfW + 0.24f), CaveSpring + Mathf.Sin(a) * (CaveHalfW + 0.24f), -0.14f);
                }
                bool key = i == ring / 2;
                float s = key ? 0.36f : 0.27f + ((i * 3 + b) % 3) * 0.03f;
                FacetBlob(mb, c, new Vector3(s, s * 1.05f, 0.34f), 0, 0.2f, b * 13 + i, 0.1f, Mossy(i % 2 == 0 ? rockL : Paint.Shade(rockL, 0.94f), moss, 0.72f));
            }
            // grass, flowers and a little sapling on the top
            for (int i = 0; i < 8; i++)
            {
                float x = -1.9f + i * 0.55f + (mb.Random01() - 0.5f) * 0.2f;
                Tuft(mb, new Vector3(x, 3.72f - Mathf.Abs(x) * 0.18f, 0.35f + (i % 2) * 0.25f), 0.3f + mb.Random01() * 0.15f, 5, i % 3 == 0 ? grass : grassL);
            }
            for (int i = 0; i < (snow ? 0 : 4); i++)
            {
                mb.Color = Pal.Flowers[(b + i * 3) % Pal.Flowers.Length];
                Gem(mb, new Vector3(-1.5f + i * 0.95f, 3.8f - Mathf.Abs(-1.5f + i * 0.95f) * 0.18f, 0.3f + (i % 2) * 0.3f), 0.06f);
            }
            if (b % 2 == 1 && !snow)
            {
                mb.Color = Pal.Timber;
                mb.Segment(new Vector3(1.15f, 4.15f, 1.4f), new Vector3(1.25f, 4.9f, 1.45f), 0.05f, 0.03f, 5);
                Foliage(b, Pal.Leaf, out var ft, out var fs, out var fb);
                SoftLump(mb, new Vector3(1.25f, 5.05f, 1.45f), new Vector3(0.45f, 0.38f, 0.42f), 1, b * 30f, CanopyRamp(ft, fs, fb, 4.7f, 5.4f));
            }
            // pebbles and grass at the foot
            DgRock(mb, new Vector3(-1.6f, 0.08f, -0.62f), new Vector3(0.2f, 0.13f, 0.16f), b + 42, rock, rockL, 0.4f, 0);
            DgRock(mb, new Vector3(1.55f, 0.07f, -0.55f), new Vector3(0.16f, 0.1f, 0.14f), b + 43, rockB, rockL, 0.4f, 0);
            foreach (var p in new[] { new Vector3(-2.45f, 0f, -0.3f), new Vector3(-1.3f, 0f, -0.48f), new Vector3(1.3f, 0f, -0.42f), new Vector3(2.3f, 0f, -0.15f), new Vector3(3.0f, 0f, 0.5f) })
                Tuft(mb, p, 0.36f + mb.Random01() * 0.15f, 6, grass);
            // ivy and roots hanging over the mouth from the brow
            mb.WindGradient = true; mb.WindY0 = 2.7f; mb.WindY1 = 1.5f; mb.Wind = 0.5f;
            float[] strands = { -0.85f, -0.42f, 0.4f, 0.88f };
            for (int s = 0; s < strands.Length; s++)
            {
                float x = strands[s] + (b - 1.5f) * 0.05f;
                float top = CaveSpring + Mathf.Sqrt(Mathf.Max(0f, (CaveHalfW + 0.1f) * (CaveHalfW + 0.1f) - x * x)) + 0.05f;
                if (snow)
                {
                    // icicles along the brow instead of ivy
                    mb.Emission = 0.3f;
                    mb.Color = (s % 2 == 0) ? DgPal.Ice : DgPal.IceMid;
                    mb.Push().Translate(x, top + 0.04f, -0.5f).Rotate(180f, s * 40f, 0f);
                    mb.Lathe(new[] { new Vector2(0.07f, 0f), new Vector2(0.035f, 0.28f + (s + b) % 3 * 0.14f), new Vector2(0f, 0.5f + (s + b) % 3 * 0.18f) }, 5, false, true, false);
                    mb.Pop();
                    mb.Emission = 0f;
                    continue;
                }
                int leaves = 3 + (s + b) % 3;
                for (int i = 0; i < leaves; i++)
                {
                    mb.Color = (i + s) % 2 == 0 ? Pal.Leaf : Pal.LeafDark;
                    Gem(mb, new Vector3(x + (i % 2 == 0 ? 0.04f : -0.04f), top - i * 0.17f, -0.5f), new Vector3(0.09f, 0.08f, 0.06f));
                }
                mb.Color = DgPal.Root;
                if (s % 2 == 1) mb.Segment(new Vector3(x - 0.12f, top + 0.05f, -0.45f), new Vector3(x - 0.1f, top - 0.7f - b * 0.08f, -0.48f), 0.025f, 0.012f, 4);
            }
            mb.Wind = 0f; mb.WindGradient = false;
            // a sacred straw rope across the mouth (buckets 0 and 2): someone remembers this place
            if (b % 2 == 0)
            {
                var ra = new Vector3(-CaveHalfW - 0.35f, CaveSpring + 0.75f, -0.52f);
                var rb = new Vector3(CaveHalfW + 0.35f, CaveSpring + 0.72f, -0.52f);
                mb.Color = Pal.RopeStraw;
                Rope(mb, ra, rb, 0.16f, 0.05f, 9, 5);
                for (int i = 0; i < 3; i++)
                    Shide(mb, SagPoint(ra, rb, 0.16f, 0.28f + i * 0.22f) + Vector3.down * 0.04f + Vector3.back * 0.04f, Vector3.right, Vector3.back, 0.3f);
            }
            return mb;
        }

        // ------------------------------------------------------------------ stalagmite (collider 1.2 × 0.8)

        static void DgSpire(MeshBuilder mb, Vector3 at, float h, float r, float leanX, float leanZ, Color a, Color c, int sides = 7)
        {
            mb.Push().Translate(at).Rotate(leanX, 0f, leanZ);
            // a rounded drip cone with flowstone lips (bulging rings) and a blunt, wet tip
            var prof = new[]
            {
                new Vector2(r * 1.12f, -0.08f), new Vector2(r, h * 0.1f), new Vector2(r * 0.86f, h * 0.22f), new Vector2(r * 0.9f, h * 0.26f),
                new Vector2(r * 0.7f, h * 0.42f), new Vector2(r * 0.74f, h * 0.46f), new Vector2(r * 0.52f, h * 0.64f), new Vector2(r * 0.55f, h * 0.68f),
                new Vector2(r * 0.34f, h * 0.84f), new Vector2(r * 0.2f, h * 0.95f), new Vector2(0f, h),
            };
            var rings = new[] { Paint.Shade(a, 0.78f), a, a, c, a, c, a, c, Paint.Shade(c, 1.05f), Paint.Shade(c, 1.1f), Paint.Hex("#E2E4E6") };
            mb.Lathe(prof, sides, false, false, false, rings, at.x * 40f);
            mb.Pop();
        }

        static MeshBuilder BuildStalagmite(int b)
        {
            var mb = Builder(VariantSeed("prop_stalagmite", b), 0.07f, 0.35f, 0.5f);
            var rock = Paint.Hsv(DgPal.Rock, (b - 1.5f) * 6f);
            var band = Color.Lerp(DgPal.RockLight, DgPal.RockTop, 0.4f);
            DgRock(mb, new Vector3(0f, 0.1f, 0.05f), new Vector3(0.48f, 0.2f, 0.3f), b + 1, Paint.Shade(rock, 0.95f), DgPal.RockTop, 0.5f);
            float h = 1.9f + b * 0.16f;
            DgSpire(mb, new Vector3(0.02f, 0.05f, 0.08f), h, 0.42f, -3f, 4f + b, rock, band);
            DgSpire(mb, new Vector3(-0.32f, 0.02f, 0.02f), h * 0.52f, 0.25f, 4f, 12f, Paint.Shade(rock, 0.95f), band, 6);
            DgSpire(mb, new Vector3(0.34f, 0.02f, 0.14f), h * (0.32f + (b % 2) * 0.12f), 0.2f, -6f, -14f, rock, band, 6);
            if (b != 1) DgSpire(mb, new Vector3(-0.08f, 0.0f, -0.2f), 0.38f, 0.13f, -12f, 6f, rock, band, 5);
            // wet sheen on the tip and a few glow-moss specks at the foot (cosy light in the dark)
            mb.Emission = 0.85f;
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 70f + b * 31f) * Mathf.Deg2Rad;
                mb.Color = i % 2 == 0 ? DgPal.Spirit : Paint.Hex("#C9F7B0");
                Gem(mb, new Vector3(Mathf.Cos(a) * 0.42f, 0.06f + (i % 2) * 0.05f, Mathf.Sin(a) * 0.24f + 0.04f), 0.035f);
            }
            mb.Emission = 0f;
            return mb;
        }

        // ------------------------------------------------------------------ crystal cluster (collider 1.4 × 0.9; data tint colours it)

        static void DgCrystal(MeshBuilder mb, Vector3 at, float h, float r, float tiltX, float tiltZ, float yaw, Color body, Color tip, float glow)
        {
            mb.Push().Translate(at).Rotate(tiltX, yaw, tiltZ);
            mb.Emission = glow * 0.62f;
            var rings = new[] { Paint.Shade(body, 0.72f), body };
            mb.Lathe(new[] { new Vector2(r * 0.82f, -0.12f), new Vector2(r, h * 0.7f) }, 6, false, true, false, rings, yaw);
            mb.Emission = glow;
            mb.Color = tip;
            mb.Lathe(new[] { new Vector2(r, h * 0.7f), new Vector2(0f, h) }, 6, false, false, false, null, yaw);
            mb.Emission = 0f;
            mb.Pop();
        }

        static MeshBuilder BuildCrystalCluster(int b)
        {
            var mb = Builder(VariantSeed("prop_crystal_cluster", b), 0.07f, 0.25f, 0.4f);
            DgRock(mb, new Vector3(0f, 0.12f, 0.08f), new Vector3(0.58f, 0.24f, 0.34f), b + 9, DgPal.RockDark, DgPal.Rock, 0.5f);
            // pale, nearly white crystals so a data tint colours them (white = soft aqua spirit-light)
            var body = Paint.Hex("#BCEBEA");
            var tip = Paint.Hex("#F1FFFC");
            (Vector3 p, float h, float r, float tx, float tz)[] cs =
            {
                (new Vector3(0.02f, 0.1f, 0.12f), 1.75f, 0.2f, -4f, 5f), (new Vector3(-0.3f, 0.08f, 0.05f), 1.2f, 0.16f, 9f, 24f),
                (new Vector3(0.3f, 0.08f, 0.16f), 1.35f, 0.17f, -6f, -21f), (new Vector3(0.12f, 0.06f, -0.14f), 0.8f, 0.13f, -24f, -12f),
                (new Vector3(-0.14f, 0.06f, 0.32f), 0.95f, 0.13f, 16f, 8f), (new Vector3(0.46f, 0.04f, -0.02f), 0.58f, 0.1f, -10f, -38f),
                (new Vector3(-0.47f, 0.04f, -0.06f), 0.5f, 0.1f, -14f, 38f), (new Vector3(-0.12f, 0.05f, -0.2f), 0.45f, 0.09f, -30f, 14f),
            };
            for (int i = 0; i < cs.Length; i++)
            {
                mb.Color = i % 2 == 0 ? body : Paint.Shade(body, 0.9f);
                DgCrystal(mb, cs[i].p, cs[i].h * (1f + (b - 1.5f) * 0.06f), cs[i].r, cs[i].tx, cs[i].tz, i * 23f + b * 9f, mb.Color, tip, 0.85f);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 95f + 20f + b * 25f) * Mathf.Deg2Rad;
                mb.Color = body;
                DgCrystal(mb, new Vector3(Mathf.Cos(a) * 0.55f, 0f, Mathf.Sin(a) * 0.24f + 0.1f), 0.3f, 0.055f, Mathf.Sin(a) * 50f, -Mathf.Cos(a) * 50f, i * 40f, body, tip, 0.8f);
            }
            return mb;
        }

        // ------------------------------------------------------------------ glow mushrooms (collider 1.4 × 0.9; tintable)

        static void DgShroom(MeshBuilder mb, Vector3 at, float h, float capR, float tiltDeg, float yawDeg, Color cap, Color gill, float glow)
        {
            mb.Push().Translate(at).Rotate(0f, yawDeg, tiltDeg);
            mb.Color = Paint.Hex("#EFE6D2");
            var mid = new Vector3(capR * 0.12f, h * 0.5f, 0f);
            mb.Segment(Vector3.zero, mid, capR * 0.3f, capR * 0.22f, 6);
            mb.Segment(mid, new Vector3(0f, h, 0f), capR * 0.22f, capR * 0.2f, 6);
            mb.Color = Paint.Hex("#E2D6BC");
            mb.Torus(new Vector3(0f, h * 0.72f, 0f), capR * 0.24f, capR * 0.05f, 7, 3);
            // glowing gills under the cap, then the cap dome with pale spots
            mb.Emission = glow;
            mb.Color = gill;
            mb.Disc(new Vector3(0f, h * 0.96f, 0f), capR * 0.97f, 9, true);
            mb.Emission = glow * 0.7f;
            mb.Color = cap;
            mb.Lathe(new[] { new Vector2(capR, h * 0.95f), new Vector2(capR * 0.95f, h * 0.95f + capR * 0.28f), new Vector2(capR * 0.72f, h * 0.95f + capR * 0.56f),
                             new Vector2(capR * 0.36f, h * 0.95f + capR * 0.74f), new Vector2(0f, h * 0.95f + capR * 0.78f) }, 9, false, false, false,
                     new[] { Paint.Shade(cap, 0.85f), cap, Paint.Shade(cap, 1.05f), Paint.Shade(cap, 1.1f), Paint.Shade(cap, 1.12f) });
            mb.Emission = glow;
            mb.Color = Paint.Hex("#F4FFF9");
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + yawDeg) * Mathf.Deg2Rad;
                float rr = capR * (i % 2 == 0 ? 0.55f : 0.78f);
                float y = h * 0.95f + capR * (i % 2 == 0 ? 0.62f : 0.38f);
                Gem(mb, new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr), new Vector3(capR * 0.11f, capR * 0.05f, capR * 0.11f));
            }
            mb.Emission = 0f;
            mb.Pop();
        }

        static MeshBuilder BuildGlowMushroom(int b)
        {
            var mb = Builder(VariantSeed("prop_glow_mushroom", b), 0.06f, 0.3f, 0.3f);
            var cap = Paint.Hsv(Paint.Hex("#7EDCCB"), (b - 1.5f) * 8f);
            var gill = Paint.Hex("#C8FFF1");
            var moss = Paint.Hex("#5E8466");
            FacetBlob(mb, new Vector3(0f, 0.05f, 0.08f), new Vector3(0.52f, 0.1f, 0.32f), 0, 0.25f, b + 3, 0.5f, ByNormal(Paint.Shade(moss, 1.15f), moss, moss));
            DgRock(mb, new Vector3(0.36f, 0.1f, 0.18f), new Vector3(0.18f, 0.14f, 0.16f), b + 4, DgPal.Rock, Paint.Shade(moss, 1.1f), 0.4f, 0);
            DgShroom(mb, new Vector3(-0.06f, 0f, 0.12f), 1.05f + b * 0.06f, 0.5f, -6f, b * 20f, cap, gill, 0.85f);
            DgShroom(mb, new Vector3(0.3f, 0f, -0.02f), 0.62f, 0.32f, -14f, 40f + b * 10f, Paint.Shade(cap, 0.94f), gill, 0.85f);
            DgShroom(mb, new Vector3(-0.38f, 0f, -0.04f), 0.42f, 0.24f, 16f, 80f, cap, gill, 0.85f);
            DgShroom(mb, new Vector3(0.1f, 0f, -0.2f), 0.24f, 0.14f, -20f, 10f, cap, gill, 0.9f);
            if (b % 2 == 1) DgShroom(mb, new Vector3(-0.22f, 0f, 0.32f), 0.5f, 0.22f, 8f, 120f, cap, gill, 0.85f);
            Tuft(mb, new Vector3(0.42f, 0f, -0.1f), 0.22f, 4, Paint.Hex("#7FA07A"));
            return mb;
        }

        // ------------------------------------------------------------------ root column: a stone pillar strangled by Old Kusu's roots (collider 2.0 × 1.3)

        // the pillar's radius by height: a stalagmite foot, a waist, and a stalactite flaring into the broken-off crown
        static readonly Vector2[] RootColProfile =
        {
            new Vector2(0.86f, -0.1f), new Vector2(0.74f, 0.3f), new Vector2(0.57f, 1.0f), new Vector2(0.47f, 1.8f), new Vector2(0.42f, 2.5f),
            new Vector2(0.45f, 3.1f), new Vector2(0.58f, 3.65f), new Vector2(0.86f, 4.05f), new Vector2(1.02f, 4.3f),
        };

        static float RootColR(float y)
        {
            var p = RootColProfile;
            if (y <= p[0].y) return p[0].x;
            for (int i = 1; i < p.Length; i++)
                if (y <= p[i].y) return Mathf.Lerp(p[i - 1].x, p[i].x, (y - p[i - 1].y) / (p[i].y - p[i - 1].y));
            return p[p.Length - 1].x;
        }

        static MeshBuilder BuildRootColumn(int b)
        {
            var mb = Builder(VariantSeed("prop_root_column", b), 0.07f, 0.35f, 0.9f);
            var rock = Paint.Hsv(Color.Lerp(DgPal.Rock, Pal.Stone, 0.45f), (b - 1.5f) * 5f);
            var rockL = Color.Lerp(rock, DgPal.RockTop, 0.45f);
            var rockD = Paint.Shade(rock, 0.8f);
            var bark = Paint.Hsv(DgPal.Root, (b - 1.5f) * 4f);
            var barkL = DgPal.RootLight;
            var moss = Pal.Moss;
            const float zc = 0.08f, depth = 0.78f;   // the pillar is a little shallower than wide, to fit the ellipse
            // the stone pillar (flowstone bands) and its broken crown, mossy on top
            var rings = new[] { rockD, rock, rockL, rock, rockL, rock, rockL, rock, rockD };
            mb.Push().Translate(0f, 0f, zc).Scale(new Vector3(1f, 1f, depth));
            mb.Lathe(RootColProfile, 8, false, false, false, rings, b * 11f);
            mb.Pop();
            var crown = DgMossRock(rock, rockL, Color.Lerp(moss, Pal.MossLight, 0.3f), 0.6f);
            FacetBlob(mb, new Vector3(0.05f, 4.55f, zc + 0.05f), new Vector3(1.22f, 0.5f, 0.95f), 1, 0.16f, b + 70, 0.1f, crown);
            FacetBlob(mb, new Vector3(-0.55f + b * 0.2f, 5.0f, zc + 0.25f), new Vector3(0.55f, 0.32f, 0.45f), 0, 0.2f, b + 71, 0.2f, crown);
            for (int i = 0; i < 5; i++)
                Tuft(mb, new Vector3(-0.8f + i * 0.4f, 4.95f + (i % 2) * 0.08f, zc - 0.25f + (i % 3) * 0.2f), 0.28f, 5, i % 2 == 0 ? Pal.Sage : Pal.MossLight);
            // a foot of rubble and dust
            var dust = Color.Lerp(DgPal.RockTop, rockD, 0.35f);
            FacetBlob(mb, new Vector3(0f, 0.02f, zc), new Vector3(0.95f, 0.12f, 0.6f), 1, 0.2f, b + 72, 0.6f, ByNormal(dust, rockD, rockD));
            // four great roots: rooted into the crown, over its lip, spiralling down the pillar and splaying across the floor
            for (int s = 0; s < 4; s++)
            {
                float dir = s % 2 == 0 ? 1f : -1f;
                float a0 = (s * 90f + 30f + b * 20f) * Mathf.Deg2Rad;
                var pts = new List<Vector3>();
                pts.Add(new Vector3(Mathf.Cos(a0) * 0.75f, 4.55f, zc + Mathf.Sin(a0) * 0.6f));
                pts.Add(new Vector3(Mathf.Cos(a0) * 1.08f, 4.2f, zc + Mathf.Sin(a0) * 0.84f));
                const int down = 14;
                for (int i = 1; i <= down; i++)
                {
                    float t = (float)i / down;
                    float y = Mathf.Lerp(4.0f, 0.35f, t);
                    float a = a0 + dir * (t * 70f + Mathf.Sin(t * 9f + s) * 6f) * Mathf.Deg2Rad;
                    float rr = RootColR(y) + 0.08f;
                    pts.Add(new Vector3(Mathf.Cos(a) * rr, y, zc + Mathf.Sin(a) * rr * depth));
                }
                // across the floor (inside the collider ellipse) and into the ground
                float af = a0 + dir * 85f * Mathf.Deg2Rad;
                var fd = new Vector3(Mathf.Cos(af), 0f, Mathf.Sin(af) * 0.62f);
                pts.Add(fd * 0.85f + new Vector3(0f, 0.12f, zc));
                pts.Add(fd * 1.05f + new Vector3(0f, -0.06f, zc));
                for (int i = 1; i < pts.Count; i++)
                {
                    float t = (float)i / (pts.Count - 1);
                    float r0 = Mathf.Lerp(0.19f, 0.11f, (float)(i - 1) / (pts.Count - 1)), r1 = Mathf.Lerp(0.19f, 0.11f, t);
                    if (i >= pts.Count - 2) { r0 *= i == pts.Count - 1 ? 0.75f : 1f; r1 *= i == pts.Count - 1 ? 0.35f : 0.75f; }
                    mb.Color = (i + s) % 3 == 0 ? Paint.Shade(bark, 1.08f) : (s % 2 == 0 ? bark : Paint.Shade(bark, 0.9f));
                    mb.Segment(pts[i - 1], pts[i], r0, r1, 7, false, true);
                    if (i == 6 || i == 11)
                    {
                        mb.Color = Paint.Shade(bark, 0.82f);
                        Gem(mb, pts[i], r1 * 1.25f);
                    }
                }
            }
            // thinner rootlets threading round the waist
            for (int s = 0; s < 2; s++)
            {
                mb.Color = barkL;
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= 8; i++)
                {
                    float t = i / 8f;
                    float y = Mathf.Lerp(3.5f, 1.2f, t);
                    float a = (s * 180f + 70f + b * 25f + t * 260f * (s == 0 ? 1f : -1f)) * Mathf.Deg2Rad;
                    float rr = RootColR(y) + 0.05f;
                    var p = new Vector3(Mathf.Cos(a) * rr, y, zc + Mathf.Sin(a) * rr * depth);
                    if (i > 0) mb.Segment(prev, p, 0.055f, 0.05f, 5, false, true);
                    prev = p;
                }
            }
            // rootlets and moss beards hanging from the crown's lip, swaying in the cave draught
            mb.WindGradient = true; mb.WindY0 = 4.4f; mb.WindY1 = 2.8f; mb.Wind = 0.35f;
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + 10f + b * 13f) * Mathf.Deg2Rad;
                var top = new Vector3(Mathf.Cos(a) * 1.12f, 4.18f, zc + Mathf.Sin(a) * 0.86f);
                float len = 0.7f + ((i * 5 + b) % 4) * 0.28f;
                var mid = top + new Vector3(Mathf.Cos(a) * 0.06f, -len * 0.55f, Mathf.Sin(a) * 0.04f);
                if (i % 3 == 1)
                {
                    mb.Color = Color.Lerp(moss, Pal.Sage, 0.4f);
                    mb.Blade(top, top + new Vector3(0f, -len * 0.8f, 0f), 0.16f, new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
                    continue;
                }
                mb.Color = barkL;
                mb.Segment(top, mid, 0.035f, 0.028f, 4);
                mb.Segment(mid, mid + new Vector3(0.04f, -len * 0.45f, 0f), 0.028f, 0.006f, 4);
            }
            mb.Wind = 0f; mb.WindGradient = false;
            // moss on the roots' shoulders
            var mossCol = ByNormal(Paint.Shade(moss, 1.15f), moss, Paint.Shade(moss, 0.8f));
            FacetBlob(mb, new Vector3(0.35f, 2.2f, zc - 0.4f), new Vector3(0.24f, 0.32f, 0.12f), 0, 0.3f, b + 3, 0f, mossCol);
            FacetBlob(mb, new Vector3(-0.4f, 3.3f, zc - 0.32f), new Vector3(0.2f, 0.28f, 0.12f), 0, 0.3f, b + 4, 0f, mossCol);
            // Old Kusu's straw rope round the pillar, paper charms in front (not in bucket 3: forgotten down here)
            if (b != 3)
            {
                const float ry = 1.55f;
                float rr = RootColR(ry) + 0.2f;
                mb.Color = Pal.RopeStraw;
                mb.Push().Translate(0f, ry, zc).Scale(new Vector3(1f, 1f, depth));
                mb.Torus(Vector3.zero, rr, 0.065f, 14, 5);
                mb.Pop();
                for (int i = 0; i < 3; i++)
                {
                    float x = -0.36f + i * 0.36f;
                    float z = zc - depth * Mathf.Sqrt(Mathf.Max(0f, rr * rr - x * x)) - 0.03f;
                    Shide(mb, new Vector3(x, ry - 0.04f, z), Vector3.right, Vector3.back, 0.34f);
                }
            }
            // glowing mushrooms and glow-moss in the root crooks: the cosy light of the Hollows
            LittleMushroom(mb, new Vector3(-0.7f, 0.06f, zc - 0.2f), 0.24f, 0.13f, DgPal.Spirit, 12f, 30f, 0.85f);
            LittleMushroom(mb, new Vector3(-0.56f, 0.04f, zc - 0.36f), 0.15f, 0.09f, DgPal.Spirit, -10f, 80f, 0.85f);
            LittleMushroom(mb, new Vector3(0.66f, 0.06f, zc - 0.18f), 0.18f, 0.1f, DgPal.Spirit, -14f, 10f, 0.85f);
            LittleMushroom(mb, new Vector3(0.42f, 0.95f, zc - 0.48f), 0.13f, 0.08f, DgPal.Spirit, -40f, 0f, 0.85f);
            mb.Emission = 0.85f;
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 67f + b * 29f + 200f) * Mathf.Deg2Rad;
                mb.Color = i % 2 == 0 ? DgPal.Spirit : Paint.Hex("#C9F7B0");
                float y = 0.5f + i * 0.62f;
                float rr = RootColR(y) + 0.02f;
                Gem(mb, new Vector3(Mathf.Cos(a) * rr, y, zc + Mathf.Sin(a) * rr * depth), 0.04f);
            }
            mb.Emission = 0f;
            return mb;
        }

        // ------------------------------------------------------------------ cave wall piece: a free-standing rock rib (collider 4.4 × 1.3)

        static MeshBuilder BuildCaveWall(int b)
        {
            var mb = Builder(VariantSeed("prop_cave_wall", b), 0.07f, 0.4f, 0.8f);
            var rock = Paint.Hsv(Color.Lerp(DgPal.Rock, Pal.Stone, 0.45f), (b - 1.5f) * 5f);
            var rockB = Paint.Shade(rock, 0.9f);
            var rockL = Color.Lerp(rock, DgPal.RockTop, 0.5f);
            var moss = Color.Lerp(Pal.Moss, DgPal.Rock, 0.25f);
            var top = DgMossRock(rock, rockL, moss, 0.82f);
            var topB = DgMossRock(rockB, rockL, moss, 0.86f);
            const float a = 2.2f, bz = 0.65f;
            // a row of big weathered boulders shouldering each other (the ends lower so neighbouring pieces overlap),
            // each standing just inside the collider front, mossy where the drips never reach
            (float x, float rx, float ry)[] stones = { (-1.58f, 0.55f, 0.95f), (-0.78f, 0.8f, 1.45f), (0.2f, 0.88f, 1.7f), (1.15f, 0.76f, 1.35f), (1.72f, 0.44f, 0.8f) };
            for (int i = 0; i < stones.Length; i++)
            {
                var st = stones[i];
                float x = st.x + (mb.Random01() - 0.5f) * 0.1f;
                float ry = st.ry * (0.92f + mb.Random01() * 0.16f);
                const float rz = 0.62f;
                float z = EllipseFrontZ(a, bz, Mathf.Clamp(x, -2.0f, 2.0f) * 0.92f) + rz + 0.06f;
                FacetBlob(mb, new Vector3(x, ry * 0.92f, z), new Vector3(st.rx, ry, rz), 1, 0.2f, b * 31 + i, 0.35f, Mossy(i % 2 == 0 ? rock : rockB, moss, 0.55f));
            }
            // the solid back so no gaps show from any yaw, capped by a mossy crest
            FacetBlob(mb, new Vector3(0f, 1.35f, 1.3f), new Vector3(2.0f, 1.45f, 0.72f), 1, 0.15f, b + 90, 0.3f, Mossy(rockB, moss, 0.5f));
            FacetBlob(mb, new Vector3(-0.35f, 2.75f, 1.2f), new Vector3(1.15f, 0.6f, 0.62f), 1, 0.2f, b + 91, 0.2f, Mossy(rock, moss, 0.5f));
            // tufts on the ledges and the crest
            for (int i = 0; i < 6; i++)
            {
                float x = -1.5f + i * 0.6f;
                Tuft(mb, new Vector3(x, 3.1f - Mathf.Abs(x) * 0.6f + (i % 2) * 0.1f, 0.95f + (i % 2) * 0.2f), 0.26f, 5, i % 2 == 0 ? Pal.Sage : Color.Lerp(Pal.MossLight, rock, 0.3f));
            }
            // rubble at the foot
            for (int i = 0; i < 5; i++)
            {
                float x = -1.7f + i * 0.85f + (mb.Random01() - 0.5f) * 0.3f;
                DgRock(mb, new Vector3(x, 0.08f, EllipseFrontZ(a, bz, x) + 0.18f), new Vector3(0.18f + mb.Random01() * 0.1f, 0.13f, 0.15f), b * 5 + i + 60, rockB, rockL, 0.4f, 0);
            }
            // glow-moss specks in the joints (cosy points of light in the dark)
            mb.Emission = 0.85f;
            for (int i = 0; i < 5; i++)
            {
                float x = -1.25f + i * 0.62f + b * 0.06f;
                mb.Color = i % 2 == 0 ? DgPal.Spirit : Paint.Hex("#C9F7B0");
                Gem(mb, new Vector3(x, 0.55f + (i % 3) * 0.55f, EllipseFrontZ(a, bz, x) + 0.1f), 0.045f);
            }
            mb.Emission = 0f;
            return mb;
        }

        // ------------------------------------------------------------------ ice pillar (collider 1.8 × 1.2)

        static MeshBuilder BuildIcePillar(int b)
        {
            var mb = Builder(VariantSeed("prop_ice_pillar", b), 0.06f, 0.2f, 0.4f);
            var ice = DgPal.Ice;
            var mid = DgPal.IceMid;
            var deep = DgPal.IceDeep;
            // a tall hexagonal ice crystal with frost bands, a faceted point and leaning sister crystals
            float H = 4.3f + b * 0.15f;
            mb.Emission = 0.25f;
            mb.Push().Translate(0f, 0f, 0.05f).Rotate(2f, b * 17f, -2f);
            mb.Lathe(new[] { new Vector2(0.5f, -0.1f), new Vector2(0.47f, H * 0.3f), new Vector2(0.48f, H * 0.33f), new Vector2(0.43f, H * 0.6f), new Vector2(0.45f, H * 0.63f), new Vector2(0.4f, H * 0.82f) },
                     6, false, false, false, new[] { deep, mid, Paint.Hex("#E3F7FD"), mid, Paint.Hex("#E3F7FD"), ice });
            mb.Emission = 0.45f;
            mb.Color = DgPal.Frost;
            mb.Lathe(new[] { new Vector2(0.4f, H * 0.82f), new Vector2(0f, H) }, 6, false, false, false);
            mb.Pop();
            mb.Color = mid;
            DgCrystal(mb, new Vector3(-0.42f, 0f, 0.12f), 2.4f + b * 0.1f, 0.25f, 6f, 16f, 20f, mid, DgPal.Frost, 0.4f);
            mb.Color = ice;
            DgCrystal(mb, new Vector3(0.4f, 0f, 0.0f), 1.6f, 0.2f, -8f, -20f, 50f, ice, DgPal.Frost, 0.4f);
            mb.Color = deep;
            DgCrystal(mb, new Vector3(0.12f, 0f, 0.42f), 1.9f, 0.2f, 18f, -6f, 10f, Color.Lerp(deep, mid, 0.5f), DgPal.Frost, 0.4f);
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 15f + b * 22f) * Mathf.Deg2Rad;
                mb.Color = i % 2 == 0 ? mid : ice;
                DgCrystal(mb, new Vector3(Mathf.Cos(a) * 0.62f, 0f, Mathf.Sin(a) * 0.32f + 0.05f), 0.35f + (i % 3) * 0.15f, 0.08f, Mathf.Sin(a) * 35f, -Mathf.Cos(a) * 35f, i * 30f, mb.Color, DgPal.Frost, 0.4f);
            }
            // a snow drift round the foot and frost caught on the bands
            FacetBlob(mb, new Vector3(-0.05f, 0.04f, 0.06f), new Vector3(0.72f, 0.14f, 0.38f), 1, 0.2f, b + 4, 0.5f, ByNormal(DgPal.Snow, DgPal.Frost, Paint.Hex("#C6DDEA")));
            return mb;
        }

        // ------------------------------------------------------------------ frozen statue: a knight caught in the ice (collider 1.4 × 1.0)

        static MeshBuilder BuildFrozenStatue(int b)
        {
            var mb = Builder(VariantSeed("prop_frozen_statue", b), 0.06f, 0.3f, 0.5f);
            var stone = Paint.Hex("#9FB4C2");
            var stoneL = Paint.Hex("#C3D3DD");
            var stoneD = Paint.Shade(stone, 0.82f);
            // pedestal
            mb.Color = stoneD;
            mb.BoxOn(new Vector3(0f, 0f, 0.1f), new Vector3(0.92f, 0.24f, 0.72f), stone);
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0.24f, 0.1f), new Vector3(0.78f, 0.12f, 0.6f), stoneL);
            mb.Push().Translate(0f, 0.36f, 0.12f);
            // legs, armoured skirt and a cape behind
            mb.Color = stone;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Segment(new Vector3(s * 0.12f, 0f, 0f), new Vector3(s * 0.11f, 0.62f, 0f), 0.09f, 0.1f, 6);
                mb.Box(new Vector3(s * 0.12f, 0.06f, -0.04f), new Vector3(0.13f, 0.12f, 0.24f));
            }
            mb.Color = stoneD;
            mb.Lathe(new[] { new Vector2(0.26f, 0.5f), new Vector2(0.21f, 0.82f) }, 8, false, true, false);
            mb.Color = Paint.Shade(stone, 0.92f);
            Slab(mb, new[] { new Vector3(-0.25f, 1.42f, 0.13f), new Vector3(0.25f, 1.42f, 0.13f), new Vector3(0.34f, 0.12f, 0.24f), new Vector3(-0.34f, 0.12f, 0.24f) }, new Vector3(0f, 0.1f, 1f), 0.05f);
            // chest, pauldrons, crested helm
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.2f, 0.8f), new Vector2(0.26f, 1.05f), new Vector2(0.25f, 1.3f), new Vector2(0.15f, 1.42f) }, 8, false, false, true);
            mb.Color = stoneL;
            for (int s = -1; s <= 1; s += 2) mb.Sphere(new Vector3(s * 0.27f, 1.34f, 0f), new Vector3(0.12f, 0.09f, 0.12f), 7, 4, false);
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.06f, 1.42f), new Vector2(0.14f, 1.5f), new Vector2(0.14f, 1.68f), new Vector2(0.1f, 1.76f), new Vector2(0f, 1.79f) }, 8, false, true, true);
            mb.Color = stoneD;
            mb.Box(new Vector3(0f, 1.6f, -0.13f), new Vector3(0.16f, 0.025f, 0.02f));
            mb.Color = stoneL;
            Slab(mb, new[] { new Vector3(0f, 1.74f, -0.1f), new Vector3(0f, 1.98f, 0.0f), new Vector3(0f, 1.9f, 0.22f), new Vector3(0f, 1.7f, 0.14f) }, Vector3.right, 0.04f);
            // both hands on the pommel of a great sword, point down in front
            mb.Color = stone;
            for (int s = -1; s <= 1; s += 2) mb.Segment(new Vector3(s * 0.27f, 1.28f, 0f), new Vector3(s * 0.05f, 1.02f, -0.2f), 0.06f, 0.055f, 5);
            mb.Color = stoneL;
            mb.Box(new Vector3(0f, 0.55f, -0.22f), new Vector3(0.07f, 0.85f, 0.025f));
            mb.Box(new Vector3(0f, 0.98f, -0.22f), new Vector3(0.3f, 0.05f, 0.05f));
            mb.Color = stone;
            mb.Box(new Vector3(0f, 1.06f, -0.22f), new Vector3(0.05f, 0.14f, 0.05f));
            mb.Pop();
            // the ice that caught him: shards up the legs and over one shoulder, frost on the helm
            var ice = DgPal.IceMid;
            (Vector3 p, float h, float r, float tx, float tz)[] shards =
            {
                (new Vector3(-0.28f, 0.3f, -0.1f), 0.9f, 0.13f, -10f, 14f), (new Vector3(0.3f, 0.3f, -0.06f), 1.15f, 0.15f, -8f, -12f),
                (new Vector3(0.05f, 0.3f, -0.3f), 0.6f, 0.12f, -24f, 4f), (new Vector3(-0.12f, 0.3f, 0.3f), 1.0f, 0.14f, 14f, 8f),
                (new Vector3(0.36f, 1.4f, 0.1f), 0.55f, 0.11f, 4f, -40f), (new Vector3(0.38f, 0.3f, 0.3f), 0.7f, 0.11f, 16f, -20f),
                (new Vector3(-0.4f, 0.3f, 0.22f), 0.55f, 0.1f, 10f, 26f),
            };
            for (int i = 0; i < shards.Length; i++)
            {
                mb.Color = i % 2 == 0 ? ice : DgPal.Ice;
                DgCrystal(mb, shards[i].p, shards[i].h * (1f + (b - 1.5f) * 0.05f), shards[i].r, shards[i].tx, shards[i].tz, i * 31f + b * 7f, mb.Color, DgPal.Frost, 0.35f);
            }
            var snow = ByNormal(DgPal.Snow, DgPal.Frost, DgPal.Frost);
            FacetBlob(mb, new Vector3(0f, 2.14f, 0.12f), new Vector3(0.12f, 0.04f, 0.12f), 0, 0.2f, b + 1, 0f, snow);
            FacetBlob(mb, new Vector3(-0.27f, 1.79f, 0.12f), new Vector3(0.12f, 0.035f, 0.11f), 0, 0.2f, b + 2, 0f, snow);
            FacetBlob(mb, new Vector3(0.18f, 0.4f, 0.0f), new Vector3(0.3f, 0.06f, 0.24f), 0, 0.2f, b + 3, 0f, snow);
            FacetBlob(mb, new Vector3(-0.3f, 0.05f, -0.1f), new Vector3(0.24f, 0.08f, 0.14f), 0, 0.2f, b + 4, 0.5f, snow);
            return mb;
        }

        // ------------------------------------------------------------------ drowned arch (walk-through: no collider; the feet stand at x = ±1.75)

        static MeshBuilder BuildDrownedArch(int b)
        {
            var mb = Builder(VariantSeed("prop_drowned_arch", b), 0.08f, 0.35f, 0.9f);
            var stone = Paint.Hsv(DgPal.WetStone, (b - 1.5f) * 5f);
            var stoneL = DgPal.WetStoneLight;
            var algae = DgPal.Algae;
            const float px = 1.75f, spring = 2.7f, R = 1.75f;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = Paint.Shade(stone, 0.85f);
                mb.BoxOn(new Vector3(s * px, 0f, 0f), new Vector3(0.86f, 0.32f, 0.8f), algae);
                const int blocks = 5;
                for (int k = 0; k < blocks; k++)
                {
                    float y0 = 0.32f + (spring - 0.32f) * k / blocks, bh = (spring - 0.32f) / blocks;
                    // algae-green and wet near the waterline, paler and dry higher up
                    mb.Color = Color.Lerp(Color.Lerp(algae, stone, 0.35f), k % 2 == 0 ? stoneL : stone, Mathf.Clamp01(k / 2f));
                    OBox(mb, new Vector3(s * px + (mb.Random01() - 0.5f) * 0.05f, y0 + bh * 0.5f, 0f), new Vector3(0.64f, bh - 0.03f, 0.64f), Quaternion.Euler(0f, (mb.Random01() - 0.5f) * 6f, 0f));
                }
                mb.Color = stoneL;
                mb.Box(new Vector3(s * px, spring + 0.07f, 0f), new Vector3(0.8f, 0.14f, 0.76f));
            }
            const int count = 11;
            for (int i = 0; i < count; i++)
            {
                float a0 = 180f - i * 180f / count, a1 = a0 - 180f / count;
                float am = (a0 + a1) * 0.5f * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(am) * (R + 0.27f) * 0.985f, spring + 0.14f + Mathf.Sin(am) * (R + 0.27f), 0f);
                bool key = i == count / 2;
                mb.Color = key ? Paint.Shade(stoneL, 1.05f) : (i % 2 == 0 ? stone : stoneL);
                OBox(mb, c, new Vector3(0.52f, key ? 0.66f : 0.54f, key ? 0.7f : 0.62f), Quaternion.Euler(0f, 0f, am * Mathf.Rad2Deg - 90f));
            }
            // barnacle clusters and shells
            for (int i = 0; i < 26; i++)
            {
                bool onArch = i >= 14;
                Vector3 p;
                Vector3 n;
                if (!onArch)
                {
                    int sd = i % 2 == 0 ? -1 : 1;
                    float y = 0.35f + (i / 2) * 0.16f;
                    float ang = (i * 53f + b * 21f) * Mathf.Deg2Rad;
                    n = new Vector3(Mathf.Cos(ang), 0f, -Mathf.Abs(Mathf.Sin(ang)) - 0.3f).normalized;
                    p = new Vector3(sd * px, y, 0f) + new Vector3(n.x * 0.34f, 0f, n.z * 0.34f);
                }
                else
                {
                    float am = (165f - (i - 14) * 12f) * Mathf.Deg2Rad;
                    n = Vector3.back;
                    p = new Vector3(Mathf.Cos(am) * (R + 0.27f), spring + 0.14f + Mathf.Sin(am) * (R + 0.27f), -0.33f);
                }
                mb.Color = i % 5 == 0 ? DgPal.Shell : DgPal.Barnacle;
                mb.Push().Translate(p).Rotate(Quaternion.FromToRotation(Vector3.up, n));
                mb.Cylinder(Vector3.zero, 0.055f, 0.025f, 0.05f, 5);
                mb.Color = Paint.Shade(DgPal.Barnacle, 0.5f);
                mb.Disc(new Vector3(0f, 0.051f, 0f), 0.022f, 5);
                mb.Pop();
            }
            // kelp and weed hanging from the arch, swaying
            mb.WindGradient = true; mb.WindY0 = spring + R; mb.WindY1 = spring; mb.Wind = 0.6f;
            for (int i = 0; i < 7; i++)
            {
                float am = (150f - i * 20f + b * 3f) * Mathf.Deg2Rad;
                var top = new Vector3(Mathf.Cos(am) * R, spring + 0.1f + Mathf.Sin(am) * R, -0.18f + (i % 2) * 0.3f);
                float len = 0.6f + ((i * 3 + b) % 4) * 0.25f;
                mb.Color = i % 2 == 0 ? DgPal.Kelp : DgPal.Algae;
                mb.Blade(top, top + new Vector3(0.06f * ((i % 3) - 1), -len, 0f), 0.13f, Vector3.right);
                mb.Blade(top + new Vector3(0.05f, 0f, 0f), top + new Vector3(0.12f, -len * 0.7f, 0.02f), 0.1f, Vector3.right);
            }
            mb.Wind = 0f; mb.WindGradient = false;
            // drips: little glassy stalactites and droplets under the soffit
            mb.Emission = 0.45f;
            for (int i = 0; i < 6; i++)
            {
                float am = (160f - i * 28f + b * 5f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(am) * (R - 0.02f), spring + 0.12f + Mathf.Sin(am) * (R - 0.02f), 0.05f - (i % 2) * 0.2f);
                mb.Color = DgPal.Ice;
                mb.Push().Translate(p);
                mb.Lathe(new[] { new Vector2(0.04f, 0f), new Vector2(0f, -0.18f) }, 4, false, false, false);
                mb.Pop();
                mb.Color = DgPal.IceMid;
                Gem(mb, p + new Vector3(0f, -0.42f - (i % 3) * 0.25f, 0f), new Vector3(0.025f, 0.04f, 0.025f));
            }
            mb.Emission = 0f;
            // a pool of wet stone and fallen blocks at the feet
            mb.Color = stone;
            OBox(mb, new Vector3(px + 0.62f, 0.15f, -0.25f), new Vector3(0.42f, 0.3f, 0.48f), Quaternion.Euler(10f, 25f, 6f));
            OBox(mb, new Vector3(-px - 0.55f, 0.12f, 0.3f), new Vector3(0.36f, 0.24f, 0.4f), Quaternion.Euler(-8f, -30f, 0f));
            FacetBlob(mb, new Vector3(-px, spring + 0.17f, 0.05f), new Vector3(0.3f, 0.05f, 0.28f), 0, 0.25f, b + 2, 0f, ByNormal(Paint.Shade(algae, 1.2f), algae, algae));
            return mb;
        }

        // ------------------------------------------------------------------ treasure pile (collider 2.0 × 1.1; gold glints)

        static MeshBuilder BuildTreasurePile(int b)
        {
            var mb = Builder(VariantSeed("prop_treasure_pile", b), 0.08f, 0.25f, 0.3f);
            var gold = DgPal.Gold;
            var deep = DgPal.GoldDeep;
            mb.Emission = 0.12f;
            FacetBlob(mb, new Vector3(0f, 0.2f, 0.15f), new Vector3(0.72f, 0.58f, 0.42f), 1, 0.12f, b + 2, 0.6f, ByNormal(DgPal.GoldLight, gold, deep, 0.4f));
            FacetBlob(mb, new Vector3(0.55f, 0.05f, 0.0f), new Vector3(0.36f, 0.16f, 0.26f), 0, 0.2f, b + 3, 0.6f, ByNormal(DgPal.GoldLight, gold, deep, 0.4f));
            FacetBlob(mb, new Vector3(-0.5f, 0.04f, 0.18f), new Vector3(0.32f, 0.14f, 0.24f), 0, 0.2f, b + 4, 0.6f, ByNormal(DgPal.GoldLight, gold, deep, 0.4f));
            mb.Emission = 0f;
            // coins lying on the heap's own surface (rays cast down onto the main blob), so it reads as coin, not lump
            for (int i = 0; i < 34; i++)
            {
                float a = (i * 137.5f + b * 23f) * Mathf.Deg2Rad;
                float rr = Mathf.Sqrt((i + 0.5f) / 34f) * 0.62f;
                var o = new Vector3(Mathf.Cos(a) * rr, 3f, 0.15f + Mathf.Sin(a) * rr * 0.6f);
                if (!BlobRayHit(new Vector3(0f, 0.2f, 0.15f), new Vector3(0.72f, 0.58f, 0.42f), 1, 0.12f, b + 2, 0.6f, o, Vector3.down, out var hit, out var n)) continue;
                mb.Color = i % 2 == 0 ? DgPal.GoldLight : Color.Lerp(gold, DgPal.GoldLight, 0.4f);
                mb.Push().Translate(hit + n * 0.008f).Rotate(Quaternion.FromToRotation(Vector3.up, n)).Rotate(0f, i * 40f, (i % 4) * 10f - 15f);
                mb.Cylinder(Vector3.zero, 0.065f, 0.065f, 0.016f, 7);
                mb.Pop();
            }
            // loose coins tumbling down the slope
            for (int i = 0; i < 22; i++)
            {
                float a = (i * 137.5f + b * 40f) * Mathf.Deg2Rad;
                float rr = 0.25f + (i % 7) * 0.1f;
                var p = new Vector3(Mathf.Cos(a) * rr * 1.15f, 0f, Mathf.Sin(a) * rr * 0.55f + 0.12f);
                float y = Mathf.Max(0.015f, 0.62f * (1f - rr * rr / 0.62f));
                mb.Color = i % 3 == 0 ? DgPal.GoldLight : gold;
                mb.Push().Translate(p.x, y, p.z).Rotate((i % 5) * 14f - 20f, i * 33f, (i % 3) * 18f - 12f);
                mb.Cylinder(Vector3.zero, 0.06f, 0.06f, 0.018f, 7);
                mb.Pop();
            }
            // an open casket spilling coins behind the heap
            mb.Push().Translate(0.42f, 0f, 0.5f).Rotate(0f, -18f, 0f);
            mb.Color = Pal.TimberDark;
            mb.BoxOn(Vector3.zero, new Vector3(0.62f, 0.36f, 0.4f));
            mb.Color = Pal.Brass;
            mb.Box(new Vector3(-0.22f, 0.18f, 0f), new Vector3(0.05f, 0.38f, 0.42f));
            mb.Box(new Vector3(0.22f, 0.18f, 0f), new Vector3(0.05f, 0.38f, 0.42f));
            mb.Emission = 0.12f;
            FacetBlob(mb, new Vector3(0f, 0.36f, 0f), new Vector3(0.28f, 0.1f, 0.17f), 0, 0.2f, b + 8, 0f, ByNormal(DgPal.GoldLight, gold, deep, 0.4f));
            mb.Emission = 0f;
            mb.Color = Pal.TimberDark;
            OBox(mb, new Vector3(0f, 0.5f, 0.27f), new Vector3(0.64f, 0.08f, 0.42f), Quaternion.Euler(-70f, 0f, 0f));
            mb.Pop();
            // a goblet, a crown, gems and a sword thrust into the heap
            mb.Color = gold;
            mb.Push().Translate(-0.32f, 0.42f, -0.06f).Rotate(0f, 0f, 16f);
            mb.Lathe(new[] { new Vector2(0.08f, 0f), new Vector2(0.03f, 0.03f), new Vector2(0.025f, 0.14f), new Vector2(0.09f, 0.2f), new Vector2(0.1f, 0.3f) }, 7, false, true, false);
            mb.Pop();
            mb.Push().Translate(0.2f, 0.6f, -0.02f).Rotate(-16f, 20f, -10f);
            mb.Color = DgPal.GoldLight;
            mb.Lathe(new[] { new Vector2(0.15f, 0f), new Vector2(0.15f, 0.07f) }, 8, false, false, false);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                mb.Color = DgPal.GoldLight;
                mb.Cone(new Vector3(Mathf.Cos(a) * 0.14f, 0.06f, Mathf.Sin(a) * 0.14f), 0.035f, 0.1f, 4);
                if (i % 2 == 0) { mb.Color = i % 4 == 0 ? DgPal.Ruby : DgPal.Sapphire; Gem(mb, new Vector3(Mathf.Cos(a) * 0.155f, 0.035f, Mathf.Sin(a) * 0.155f), 0.025f); }
            }
            mb.Pop();
            mb.Push().Translate(-0.05f, 0.62f, 0.25f).Rotate(-14f, 0f, -18f);
            mb.Color = Paint.Hex("#C9D2DC");
            mb.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.07f, 0.4f, 0.02f));
            mb.Color = DgPal.Gold;
            mb.Box(new Vector3(0f, 0.32f, 0f), new Vector3(0.3f, 0.045f, 0.06f));
            mb.Color = Pal.TimberDark;
            mb.Box(new Vector3(0f, 0.44f, 0f), new Vector3(0.05f, 0.2f, 0.05f));
            mb.Color = DgPal.Gold;
            Gem(mb, new Vector3(0f, 0.57f, 0f), 0.045f);
            mb.Pop();
            mb.Emission = 0.5f;
            (Vector3 p, Color c, float s)[] gems =
            {
                (new Vector3(-0.08f, 0.66f, -0.18f), DgPal.Ruby, 0.06f), (new Vector3(0.45f, 0.3f, -0.12f), DgPal.Emerald, 0.05f),
                (new Vector3(-0.55f, 0.24f, 0.0f), DgPal.Sapphire, 0.055f), (new Vector3(0.12f, 0.3f, -0.32f), DgPal.Ruby, 0.045f),
                (new Vector3(-0.3f, 0.16f, -0.3f), DgPal.Emerald, 0.04f),
            };
            foreach (var g in gems) { mb.Color = g.c; Gem(mb, g.p, g.s); }
            // two soft glints catching the light (slim diamonds, not crosses)
            mb.Emission = 1f;
            mb.Color = Paint.Hex("#FFF6D0");
            Vector3[] glints = { new Vector3(0.12f, 0.8f, -0.12f), new Vector3(-0.42f, 0.44f, -0.18f) };
            for (int i = 0; i < glints.Length; i++)
            {
                Gem(mb, glints[i], new Vector3(0.022f, 0.07f, 0.022f));
                Gem(mb, glints[i], new Vector3(0.05f, 0.018f, 0.018f));
            }
            mb.Emission = 0f;
            return mb;
        }
    }
}
