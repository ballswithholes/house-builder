// Amberfield Downs (highlands): standing stones and cairns on golden grass, a scarecrow, skeps, wheat, the old
// watchtower, the Duskmane gnoll camp (hide tent, totem, bone pile), golden autumn trees, the stone farmhouse and the
// Mudpaw tunnelers' quarry cart. Colliders: Docs/ArtKeys.md (props-wild block).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterWildHighlands(Dictionary<string, Recipe> r)
        {
            r["prop_standing_stone"] = (art, seed) => Simple(art, seed, BuildStandingStone, 0.5f);
            r["prop_cairn"] = (art, seed) => Swaying(art, seed, BuildCairn, 0.5f);
            r["prop_scarecrow"] = (art, seed) => Swaying(art, seed, BuildScarecrow, 0.4f);
            r["prop_beehive"] = (art, seed) => Simple(art, seed, BuildBeehive, 0.7f);
            r["prop_wheat"] = (art, seed) => Swaying(art, seed, BuildWheat, 1.7f);
            r["prop_tree_golden"] = GoldenTree;
            r["prop_farmhouse"] = Farmhouse;
            r["prop_watchtower_ruin"] = (art, seed) => Simple(art, seed, BuildWatchtowerRuin, 1.7f);
            r["prop_gnoll_tent"] = GnollTent;
            r["prop_gnoll_totem"] = (art, seed) => Swaying(art, seed, BuildGnollTotem, 0.4f);
            r["prop_bonepile"] = (art, seed) => Simple(art, seed, BuildBonePile, 0.7f);
            r["prop_quarry_cart"] = (art, seed) => Simple(art, seed, BuildQuarryCart, 0.9f);
        }

        static readonly Color HlGrass = Paint.Hex("#D3B862"), HlGrassDark = Paint.Hex("#B39444"), HlLichen = Paint.Hex("#D3C06A");
        static readonly Color[] HlFlowers = { Paint.Hex("#FFFFFF"), Paint.Hex("#F6D04D"), Paint.Hex("#B48CE0"), Paint.Hex("#E9536A") };

        // ------------------------------------------------------------------ standing stone (≈ 2.8 m; collider 1.0 × 0.7)

        static MeshBuilder BuildStandingStone(int b)
        {
            var mb = Builder(VariantSeed("prop_standing_stone", b), 0.07f, 0.35f, 0.8f);
            var stone = Paint.Hsv(Paint.Hex("#ABA498"), (b - 1.5f) * 6f, 0.85f);
            float H = 2.65f + b * 0.17f;
            float[] ys = { -0.2f, 0.3f, 1.0f, 1.8f, H - 0.45f, H - 0.08f };
            float[] rx = { 0.52f, 0.5f, 0.45f, 0.4f, 0.33f, 0.2f };
            float[] rz = { 0.34f, 0.32f, 0.29f, 0.26f, 0.22f, 0.13f };
            const int N = 8;
            int sd = VariantSeed("standing", b);
            var rings = new List<Vector3[]>();
            var frontZ = new float[ys.Length];
            for (int i = 0; i < ys.Length; i++)
            {
                var ring = new Vector3[N];
                float kf = 1f + WildHash(i, 0, sd) * 0.05f;
                for (int j = 0; j < N; j++)
                {
                    // vertices at −67.5° + 45°·j: the front face (between j = N−1 and 0) is flat and centred on −Z
                    float a = (-67.5f + j * 45f) * Mathf.Deg2Rad;
                    float k = (j == 0 || j == N - 1) ? kf : 1f + WildHash(i, j, sd) * 0.1f;
                    float x = Mathf.Cos(a) * rx[i] * k, z = Mathf.Sin(a) * rz[i] * k;
                    float y = ys[i];
                    if (i == ys.Length - 1) y += x * 0.45f + z * 0.25f;   // a slanted, weathered crown
                    ring[j] = new Vector3(x, y, z);
                }
                rings.Add(ring);
                frontZ[i] = Mathf.Sin(-67.5f * Mathf.Deg2Rad) * rz[i] * kf;
            }
            float FrontZ(float y)
            {
                for (int i = 0; i < ys.Length - 1; i++)
                    if (y <= ys[i + 1]) return Mathf.Lerp(frontZ[i], frontZ[i + 1], Mathf.InverseLerp(ys[i], ys[i + 1], y));
                return frontZ[ys.Length - 1];
            }
            var dark = Paint.Shade(stone, 0.78f);
            var top = Color.Lerp(stone, HlLichen, 0.45f);
            mb.Push().Rotate((b - 1.5f) * 2f, 0f, (b % 2 == 0 ? 3.5f : -3f));
            Loft(mb, rings, false, true, n => n.y > 0.5f ? top : n.y < -0.2f ? dark : stone);
            // lichen rosettes on the side faces
            for (int s = 0; s < 6; s++)
            {
                int i = 1 + s % 3, j = (s * 3 + b) % (N - 1);
                var r0 = rings[i]; var r1 = rings[i + 1];
                var c = (r0[j] + r0[j + 1] + r1[j] + r1[j + 1]) * 0.25f;
                var n = Vector3.Cross(r1[j] - r0[j], r0[j + 1] - r0[j]).normalized;
                if (Vector3.Dot(n, new Vector3(c.x, 0f, c.z)) < 0f) n = -n;
                var col = s % 2 == 0 ? HlLichen : Paint.Hex("#B9C3A0");
                Ngon(mb, c + n * 0.006f + Vector3.up * WildHash(s, 3, sd) * 0.15f, 0.07f + 0.04f * (s % 3), 6, n, Vector3.up, s * 17f, col);
            }
            // an old carved rune on the front face: a ring crossed by a stave, with two marks; glows faintly on odd seeds
            bool glow = b % 2 == 1;
            mb.Color = glow ? Paint.Hex("#F6E6A6") : Paint.Shade(stone, 0.5f);
            mb.Emission = glow ? 0.6f : 0f;
            Vector3 On(float x, float y) => new Vector3(x, y, FrontZ(y) - 0.012f);
            const float cy = 1.3f, cr = 0.1f;
            for (int k = 0; k < 8; k++)
            {
                float a0 = k * Mathf.PI / 4f, a1 = (k + 1) * Mathf.PI / 4f;
                mb.Segment(On(Mathf.Cos(a0) * cr, cy + Mathf.Sin(a0) * cr), On(Mathf.Cos(a1) * cr, cy + Mathf.Sin(a1) * cr), 0.016f, 0.016f, 3, false, true);
            }
            mb.Segment(On(0f, cy - 0.24f), On(0f, cy + 0.26f), 0.016f, 0.016f, 3, false, true);
            mb.Segment(On(-0.07f, cy + 0.34f), On(0.07f, cy + 0.34f), 0.014f, 0.014f, 3, false, true);
            mb.Segment(On(-0.05f, cy - 0.32f), On(0.05f, cy - 0.38f), 0.014f, 0.014f, 3, false, true);
            mb.Emission = 0f;
            mb.Pop();
            // fallen chips, golden grass and a few wildflowers at the foot
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + b * 33f + 20f) * Mathf.Deg2Rad;
                FacetBlob(mb, new Vector3(Mathf.Cos(a) * 0.62f, 0.05f, Mathf.Sin(a) * 0.42f), new Vector3(0.14f, 0.09f, 0.11f), 0, 0.2f, sd + i, 0.3f,
                          Mossy(stone, HlLichen, 0.7f));
            }
            WildTuft(mb, new Vector3(-0.42f, 0f, -0.3f), 0.42f, 6, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(0.48f, 0f, 0.05f), 0.36f, 5, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(0.1f, 0f, 0.38f), 0.3f, 4, HlGrass, HlGrassDark);
            WildFlowers(mb, new Vector3(0.05f, 0f, -0.45f), 0.3f, 4, HlFlowers);
            return mb;
        }

        // ------------------------------------------------------------------ cairn (≈ 1.4 m; collider 1.1 × 0.8)

        static MeshBuilder BuildCairn(int b)
        {
            var mb = Builder(VariantSeed("prop_cairn", b), 0.08f, 0.35f, 0.5f);
            Color[] greys = { Paint.Hex("#ACA79F"), Paint.Hex("#C2B79F"), Paint.Hex("#9E9A97"), Paint.Hex("#B8AE9E") };
            (float r, float t)[] st = { (0.5f, 0.34f), (0.42f, 0.31f), (0.35f, 0.29f), (0.27f, 0.25f), (0.19f, 0.21f) };
            int count = 4 + (b % 2);
            float y = -0.06f;
            int sd = VariantSeed("cairn", b);
            var topAt = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var (r, t) = st[i];
                float ox = WildHash(i, 1, sd) * 0.08f, oz = WildHash(i, 2, sd) * 0.05f;
                var c = new Vector3(ox, y + t * 0.5f, oz);
                var g = greys[(i + b) % 4];
                FacetBlob(mb, c, new Vector3(r, t * 0.58f, r * 0.85f), i < 3 ? 1 : 0, 0.2f, sd + i * 7, 0.2f,
                          Mossy(g, Color.Lerp(Paint.Shade(g, 1.08f), HlLichen, 0.35f), 0.86f, Paint.Shade(g, 0.76f)));
                y += t * 0.82f;
                topAt = c + Vector3.up * t * 0.4f;
            }
            // an upright finger stone on top, with a hazel wand and a faded ribbon (sways)
            FacetBlob(mb, topAt + new Vector3(0.02f, 0.14f, 0f), new Vector3(0.08f, 0.17f, 0.07f), 0, 0.15f, sd + 99, 0f, Mossy(greys[b], HlLichen, 0.8f));
            mb.Color = Pal.Wood;
            var w0 = topAt + new Vector3(-0.1f, 0.02f, 0.02f);
            var w1 = w0 + new Vector3(-0.12f, 0.55f, 0.02f);
            mb.Segment(w0, w1, 0.016f, 0.01f, 4);
            mb.WindGradient = true; mb.WindY0 = w1.y - 0.05f; mb.WindY1 = w1.y - 0.5f; mb.Wind = 1.6f;
            mb.Color = new[] { Pal.Vermilion, Paint.Hex("#3E78B8"), Paint.Hex("#E3B34C"), Pal.Vermilion }[b];
            var rb = w1 + new Vector3(0.005f, -0.03f, -0.015f);
            Slab(mb, new[] { rb, rb + new Vector3(0.05f, -0.01f, 0f), rb + new Vector3(0.16f, -0.36f, 0f), rb + new Vector3(0.1f, -0.38f, 0f) }, Vector3.back, 0.008f);
            mb.Wind = 0f; mb.WindGradient = false;
            // a ring of pebbles and an offering posy
            for (int i = 0; i < 7; i++)
            {
                float a = (i * 51f + b * 20f) * Mathf.Deg2Rad;
                mb.Color = greys[(i + 1) % 4];
                Gem(mb, new Vector3(Mathf.Cos(a) * 0.62f, 0.03f, Mathf.Sin(a) * 0.46f), new Vector3(0.08f, 0.05f, 0.07f));
            }
            WildTuft(mb, new Vector3(0.5f, 0f, -0.2f), 0.34f, 5, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(-0.52f, 0f, 0.1f), 0.3f, 4, HlGrass, HlGrassDark);
            WildFlowers(mb, new Vector3(-0.15f, 0f, -0.52f), 0.14f, 3, new[] { Paint.Hex("#FFFFFF"), Paint.Hex("#F6D04D") }, 0.045f);
            return mb;
        }

        // ------------------------------------------------------------------ scarecrow (≈ 2.3 m; collider 0.6 × 0.4)

        static MeshBuilder BuildScarecrow(int b)
        {
            var mb = Builder(VariantSeed("prop_scarecrow", b), 0.06f, 0.3f, 0.5f);
            var coat = new[] { Paint.Hex("#6F8A5A"), Paint.Hex("#8A5A44"), Paint.Hex("#5A6F8A"), Paint.Hex("#7C6A9A") }[b];
            var straw = Pal.ThatchLight;
            var burlap = Pal.Burlap;
            // post and crossbar
            mb.Color = Pal.Timber;
            mb.Segment(new Vector3(0f, -0.05f, 0.04f), new Vector3(0f, 1.9f, 0.04f), 0.06f, 0.05f, 6);
            mb.Segment(new Vector3(-0.8f, 1.52f, 0.04f), new Vector3(0.8f, 1.57f, 0.04f), 0.045f, 0.04f, 5);
            // everything above the hips sways a little
            mb.WindGradient = true; mb.WindY0 = 0.9f; mb.WindY1 = 2.3f; mb.Wind = 0.55f;
            // coat (flattened), rope belt, patches
            mb.Push().Translate(0f, 0f, 0.02f).Scale(new Vector3(1f, 1f, 0.72f));
            mb.Color = coat;
            mb.Lathe(new[] { new Vector2(0.34f, 0.82f), new Vector2(0.37f, 1.0f), new Vector2(0.33f, 1.3f), new Vector2(0.3f, 1.48f), new Vector2(0.16f, 1.64f) }, 9, false, true, true,
                     new[] { Paint.Shade(coat, 0.85f), coat, coat, Paint.Shade(coat, 1.06f), coat });
            mb.Color = Pal.RopeStraw;
            mb.Torus(new Vector3(0f, 1.03f, 0f), 0.37f, 0.025f, 10, 3);
            mb.Pop();
            float fz = -0.37f * 0.72f + 0.02f;
            mb.Color = Paint.Hsv(coat, 160f, 0.6f, 1.15f);
            Slab(mb, new[] { new Vector3(0.06f, 1.18f, fz - 0.01f), new Vector3(0.2f, 1.17f, fz - 0.005f), new Vector3(0.21f, 1.31f, fz + 0.01f), new Vector3(0.07f, 1.32f, fz + 0.005f) },
                 new Vector3(0.15f, 0f, -1f), 0.01f);
            mb.Color = Paint.Hex("#D9C189");
            Slab(mb, new[] { new Vector3(-0.22f, 0.88f, fz + 0.005f), new Vector3(-0.1f, 0.88f, fz - 0.005f), new Vector3(-0.1f, 0.98f, fz - 0.005f), new Vector3(-0.22f, 0.98f, fz + 0.005f) },
                 new Vector3(-0.15f, 0f, -1f), 0.01f);
            // straw spilling from the hem
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + b * 11f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a) * 0.3f, 0.85f, Mathf.Sin(a) * 0.2f + 0.02f);
                mb.Color = i % 2 == 0 ? straw : Pal.Thatch;
                mb.Segment(p, p + new Vector3(Mathf.Cos(a) * 0.06f, -0.2f - 0.06f * (i % 3), Mathf.Sin(a) * 0.04f), 0.022f, 0.004f, 3, false, false);
            }
            // sleeves along the crossbar, straw hands
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = coat;
                mb.Segment(new Vector3(s * 0.18f, 1.53f, 0.04f), new Vector3(s * 0.66f, 1.56f, 0.04f), 0.11f, 0.095f, 6);
                mb.Color = Paint.Shade(coat, 0.85f);
                mb.Segment(new Vector3(s * 0.62f, 1.555f, 0.04f), new Vector3(s * 0.68f, 1.56f, 0.04f), 0.105f, 0.105f, 6);
                for (int k = 0; k < 5; k++)
                {
                    float a = (k - 2) * 0.45f;
                    mb.Color = k % 2 == 0 ? straw : Pal.Thatch;
                    var p = new Vector3(s * 0.68f, 1.56f, 0.04f);
                    mb.Segment(p, p + new Vector3(s * 0.2f, Mathf.Sin(a) * 0.12f - 0.03f, Mathf.Cos(a) * 0.04f), 0.02f, 0.004f, 3, false, false);
                }
            }
            // sack head with button eyes and a stitched grin, tied at the neck
            mb.Color = burlap;
            mb.Sphere(new Vector3(0f, 1.87f, 0.02f), new Vector3(0.2f, 0.22f, 0.19f), 9, 6, false);
            mb.Color = Pal.RopeStraw;
            mb.Cylinder(new Vector3(0f, 1.64f, 0.02f), 0.09f, 0.085f, 0.06f, 6);
            mb.Color = Pal.Ink;
            Ngon(mb, new Vector3(-0.075f, 1.92f, -0.165f), 0.035f, 6, new Vector3(-0.3f, 0.1f, -1f), Vector3.up, 0f, Pal.Ink);
            Ngon(mb, new Vector3(0.075f, 1.92f, -0.165f), 0.035f, 6, new Vector3(0.3f, 0.1f, -1f), Vector3.up, 0f, Pal.Ink);
            Vector3 prev = Vector3.zero;
            for (int k = 0; k <= 6; k++)
            {
                float t = k / 6f, x = Mathf.Lerp(-0.1f, 0.1f, t), y = 1.8f - Mathf.Sin(t * Mathf.PI) * 0.035f;
                float z = 0.02f - 0.19f * Mathf.Sqrt(Mathf.Max(0f, 1f - (x / 0.2f) * (x / 0.2f) - ((y - 1.87f) / 0.22f) * ((y - 1.87f) / 0.22f))) - 0.008f;
                var p = new Vector3(x, y, z);
                if (k > 0) mb.Segment(prev, p, 0.008f, 0.008f, 3, false, false);
                if (k % 2 == 1) mb.Segment(p + new Vector3(0f, 0.025f, 0f), p - new Vector3(0f, 0.025f, 0f), 0.006f, 0.006f, 3, false, false);
                prev = p;
            }
            // floppy straw hat with a band
            mb.Push().Translate(0f, 2.02f, 0.02f).Rotate(-6f, 0f, 9f - b * 4f);
            mb.Color = straw;
            mb.Lathe(new[] { new Vector2(0.4f, -0.03f), new Vector2(0.4f, 0f), new Vector2(0.2f, 0.03f), new Vector2(0.17f, 0.2f), new Vector2(0.06f, 0.24f) }, 10, false, true, true,
                     new[] { Pal.ThatchDark, straw, straw, Pal.Thatch, straw });
            mb.Color = new[] { Pal.Vermilion, Paint.Hex("#3E78B8"), Pal.Vermilion, Paint.Hex("#E3B34C") }[b];
            mb.Cylinder(new Vector3(0f, 0.03f, 0f), 0.2f, 0.19f, 0.06f, 10);
            mb.Pop();
            // a crow perched on the left arm
            var cr = new Vector3(-0.52f, 1.68f, 0.04f);
            mb.Color = Paint.Hex("#2F3040");
            mb.Sphere(cr, new Vector3(0.07f, 0.075f, 0.12f), 7, 5, false);
            mb.Sphere(cr + new Vector3(0f, 0.08f, -0.09f), new Vector3(0.055f, 0.055f, 0.055f), 6, 4, false);
            mb.Segment(cr + new Vector3(0f, 0f, 0.08f), cr + new Vector3(0f, -0.05f, 0.2f), 0.04f, 0.01f, 4);
            mb.Color = Paint.Hex("#D9A33A");
            mb.Segment(cr + new Vector3(0f, 0.08f, -0.14f), cr + new Vector3(0f, 0.07f, -0.21f), 0.02f, 0.003f, 4);
            mb.Color = Pal.Paper;
            Gem(mb, cr + new Vector3(-0.04f, 0.1f, -0.12f), 0.012f);
            mb.Wind = 0f; mb.WindGradient = false;
            WildTuft(mb, new Vector3(0.12f, 0f, -0.14f), 0.36f, 6, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(-0.16f, 0f, 0.12f), 0.3f, 4, HlGrass, HlGrassDark);
            return mb;
        }

        // ------------------------------------------------------------------ bee skeps on a bench (≈ 1.2 m; collider 2.0 × 0.9)

        static void Skep(MeshBuilder mb, Vector3 at, float s, bool hackle)
        {
            var keepC = mb.Color;
            var light = Pal.ThatchLight; var mid = Pal.Thatch; var dark = Pal.ThatchDark;
            var prof = new List<Vector2>();
            var cols = new List<Color>();
            float[] ys = { 0f, 0.1f, 0.2f, 0.3f, 0.38f, 0.46f, 0.52f };
            float[] rs = { 0.27f, 0.29f, 0.29f, 0.26f, 0.21f, 0.14f, 0.05f };
            for (int i = 0; i < ys.Length; i++)
            {
                // a groove between coils: duplicated rings in a darker straw
                prof.Add(new Vector2(rs[i] * s * 0.95f, ys[i] * s)); cols.Add(dark);
                prof.Add(new Vector2(rs[i] * s, ys[i] * s + 0.012f * s)); cols.Add(i % 2 == 0 ? light : mid);
            }
            prof.Add(new Vector2(0f, 0.56f * s)); cols.Add(mid);
            mb.Push().Translate(at);
            mb.Lathe(prof, 10, false, true, false, cols, 9f);
            if (hackle)
            {
                mb.Color = Pal.ThatchDark;
                mb.Lathe(new[] { new Vector2(0.24f * s, 0.36f * s), new Vector2(0.27f * s, 0.34f * s), new Vector2(0f, 0.7f * s) }, 9, false, true, false);
            }
            // entrance
            Ngon(mb, new Vector3(0f, 0.06f * s, -0.285f * s), 0.05f * s, 6, Vector3.back, Vector3.up, 0f, Pal.Ink);
            mb.Color = Pal.WoodLight;
            mb.Box(new Vector3(0f, 0.012f * s, -0.3f * s), new Vector3(0.14f * s, 0.024f * s, 0.06f * s));
            mb.Pop();
            mb.Color = keepC;
        }

        static MeshBuilder BuildBeehive(int b)
        {
            var mb = Builder(VariantSeed("prop_beehive", b), 0.06f, 0.3f, 0.5f);
            // rustic bench
            mb.Color = Pal.WoodGrey;
            mb.Box(new Vector3(0f, 0.43f, 0f), new Vector3(1.36f, 0.06f, 0.48f));
            mb.Color = Pal.Wood;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Beam(mb, new Vector3(sx * 0.56f, 0f, sz * 0.17f), new Vector3(sx * 0.6f, 0.41f, sz * 0.17f), 0.07f, 0.07f);
            mb.Box(new Vector3(0f, 0.16f, 0f), new Vector3(1.16f, 0.05f, 0.05f));
            Skep(mb, new Vector3(-0.34f, 0.46f, 0f), 1f, b % 2 == 0);
            Skep(mb, new Vector3(0.36f, 0.46f, 0.02f), 0.92f, b % 2 == 1);
            // a small skep on a stone at the side
            FacetBlob(mb, new Vector3(0.88f, 0.05f, -0.08f), new Vector3(0.24f, 0.08f, 0.2f), 0, 0.2f, b + 3, 0.3f, Mossy(Pal.Stone, Pal.Moss, 0.7f));
            Skep(mb, new Vector3(0.88f, 0.11f, -0.08f), 0.7f, false);
            // honey jar with a wooden dipper
            mb.Color = Paint.Hex("#C9785A");
            mb.Push().Translate(-0.86f, 0f, -0.12f);
            mb.Lathe(new[] { new Vector2(0.09f, 0f), new Vector2(0.12f, 0.1f), new Vector2(0.1f, 0.2f), new Vector2(0.07f, 0.23f), new Vector2(0.08f, 0.26f) }, 8, false, true, false);
            mb.Color = Pal.Honey;
            mb.Emission = 0.15f;
            mb.Disc(new Vector3(0f, 0.25f, 0f), 0.07f, 8);
            mb.Emission = 0f;
            mb.Pop();
            mb.Color = Pal.WoodLight;
            mb.Segment(new Vector3(-0.86f, 0.18f, -0.12f), new Vector3(-0.8f, 0.42f, -0.16f), 0.012f, 0.012f, 4);
            // bees
            mb.Color = Paint.Hex("#F2C23A");
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.3f + b;
                Gem(mb, new Vector3(Mathf.Cos(a) * 0.5f, 0.75f + (i % 3) * 0.18f, -0.3f + Mathf.Sin(a) * 0.15f), new Vector3(0.028f, 0.022f, 0.022f));
            }
            // lavender tufts
            for (int k = 0; k < 2; k++)
            {
                var at = new Vector3(k == 0 ? -0.6f : 0.62f, 0f, -0.32f);
                WildTuft(mb, at, 0.4f, 6, Paint.Hex("#8DA36A"), Paint.Hex("#A8B888"));
                mb.Color = Paint.Hex("#9C7FD0");
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 1.26f + k;
                    Gem(mb, at + new Vector3(Mathf.Cos(a) * 0.1f, 0.36f + (i % 2) * 0.06f, Mathf.Sin(a) * 0.07f), new Vector3(0.025f, 0.05f, 0.025f));
                }
            }
            return mb;
        }

        // ------------------------------------------------------------------ wheat patch (3.6 × 2.0 m, ≈ 1 m; collider 3.4 × 1.8)

        static MeshBuilder BuildWheat(int b)
        {
            var mb = Builder(VariantSeed("prop_wheat", b), 0.05f, 0.45f, 0.75f);
            const float HX = 1.7f, HZ = 0.9f;
            const int Rows = 5, PerRow = 11;
            var gold = Paint.Hsv(Paint.Hex("#E4C060"), (b - 1.5f) * 3f);
            var goldDark = Paint.Hsv(Paint.Hex("#C49A46"), (b - 1.5f) * 3f);
            var light = Paint.Hex("#F6DE92");
            var stalk = Paint.Hex("#98894A");
            int sd = VariantSeed("wheat", b);
            // furrow soil under the crop
            QuadC(mb, new Vector3(-HX - 0.1f, 0.012f, -HZ - 0.08f), new Vector3(-HX - 0.1f, 0.012f, HZ + 0.08f), new Vector3(HX + 0.1f, 0.012f, HZ + 0.08f),
                  new Vector3(HX + 0.1f, 0.012f, -HZ - 0.08f), Vector3.up, Paint.Hex("#7E6248"));
            mb.WindGradient = true; mb.WindY0 = 0.15f; mb.WindY1 = 1.0f; mb.Wind = 1.3f;
            // the crop: rows of sheaf-shaped bunches (narrow at the foot, a full, domed head of ears), so the field has a
            // soft bristling top, shadowed gaps between the stalks and ragged edges
            float rowW = 2f * HZ / Rows;
            var tops = new List<Vector3>();
            for (int row = 0; row < Rows; row++)
                for (int i = 0; i < PerRow; i++)
                {
                    int k = row * PerRow + i;
                    float x = Mathf.Lerp(-HX + 0.12f, HX - 0.12f, (i + 0.5f * (row % 2)) / (PerRow - 0.5f)) + WildHash(k, 1, sd) * 0.06f;
                    float z = -HZ + rowW * (row + 0.5f) + WildHash(k, 2, sd) * 0.07f;
                    bool edge = row == 0 || row == Rows - 1 || i == 0 || i == PerRow - 1;
                    float h = (0.82f + 0.1f * WildHash(k, 3, sd) + 0.05f * Mathf.Sin(x * 2.6f + row)) * (edge ? 0.9f : 1f);
                    float rt = 0.2f + 0.03f * WildHash(k, 4, sd);
                    var lean = new Vector3(WildHash(k, 5, sd) * 0.08f + (i == 0 ? -0.06f : i == PerRow - 1 ? 0.06f : 0f), 0f, row == 0 ? -0.07f : row == Rows - 1 ? 0.07f : WildHash(k, 6, sd) * 0.05f);
                    mb.Push().Translate(x, 0f, z).Rotate(lean.z * 120f, k * 37f, -lean.x * 120f);
                    mb.Lathe(new[] { new Vector2(0.05f, 0f), new Vector2(0.09f, h * 0.45f), new Vector2(rt * 0.9f, h * 0.8f), new Vector2(rt, h * 0.92f), new Vector2(rt * 0.6f, h * 1.02f), new Vector2(0f, h * 1.06f) },
                             6, false, false, true, new[] { stalk, Color.Lerp(stalk, goldDark, 0.6f), goldDark, gold, Color.Lerp(gold, light, 0.4f), light }, k * 13f);
                    mb.Pop();
                    tops.Add(new Vector3(x, h, z) + new Vector3(-lean.x, 0f, lean.z) * h * 2.1f);
                }
            // bristly ears poking out of the heads
            for (int i = 0; i < tops.Count; i++)
                for (int e = 0; e < 2; e++)
                {
                    var tp = tops[i];
                    float a = (i * 47f + e * 160f) * Mathf.Deg2Rad;
                    var at = tp + new Vector3(Mathf.Cos(a) * 0.1f, 0.02f, Mathf.Sin(a) * 0.08f);
                    mb.Color = (i + e) % 3 == 0 ? light : Color.Lerp(gold, light, 0.55f);
                    mb.Push().Translate(at).Rotate(Mathf.Sin(a) * 30f, 0f, -Mathf.Cos(a) * 30f);
                    Gem(mb, new Vector3(0f, 0.07f, 0f), new Vector3(0.028f, 0.09f, 0.028f));
                    mb.Pop();
                }
            // poppies and cornflowers among the stalks
            for (int i = 0; i < 7; i++)
            {
                var tp = tops[(i * 13 + b * 5) % tops.Count];
                var petal = i % 3 == 2 ? Paint.Hex("#4F7FD8") : Paint.Hex("#E2453A");
                Bloom(mb, tp + new Vector3(0.12f, 0.06f, -0.12f), 0.075f, new Vector3(0f, 1f, -0.45f), petal, Pal.Ink, i * 40f);
            }
            mb.Wind = 0f; mb.WindGradient = false;
            return mb;
        }

        // ------------------------------------------------------------------ golden autumn tree (≈ 8.5 m; trunk collider 1.2 × 0.8)

        static PropModel GoldenTree(string art, int seed)
        {
            int b = Bucket(seed);
            var rig = new Rig(art);
            rig.Body(Cached(art + "#" + b, () => BuildGoldenTree(b)));
            var model = rig.Done(0.6f);
            model.Sways = true;
            WildGroundPart(rig, "Leaves", Cached(art + "#leaves" + b, () => BuildFallenLeaves(b)), Skin.Plain);
            return model;
        }

        static void GoldenPalette(int b, out Color top, out Color side, out Color bottom)
        {
            side = new[] { Paint.Hex("#E3A23A"), Paint.Hex("#DE8236"), Paint.Hex("#E6BA46"), Paint.Hex("#CF6C3A") }[b];
            top = new[] { Paint.Hex("#F8D774"), Paint.Hex("#F6C460"), Paint.Hex("#FAE38A"), Paint.Hex("#EFB25C") }[b];
            bottom = new[] { Paint.Hex("#A65A34"), Paint.Hex("#9A4830"), Paint.Hex("#AE7636"), Paint.Hex("#8C3E30") }[b];
        }

        /// <summary>Autumn canopy ramp: plum-shadowed rust underneath → gold → pale sunlit gold on top (no teal, unlike CanopyRamp).</summary>
        static System.Func<float, Color> GoldenRamp(Color top, Color side, Color bottom, float y0, float y1)
        {
            var cool = Color.Lerp(bottom, Paint.Hex("#6A4A62"), 0.22f);
            var warm = Color.Lerp(top, Paint.Hex("#FFF2B8"), 0.25f);
            return y =>
            {
                float t = Mathf.Clamp01((y - y0) / Mathf.Max(0.01f, y1 - y0));
                if (t < 0.4f) return Color.Lerp(cool, side, Mathf.SmoothStep(0f, 1f, t / 0.4f));
                if (t < 0.78f) return Color.Lerp(side, top, Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.38f));
                return Color.Lerp(top, warm, (t - 0.78f) / 0.22f);
            };
        }

        static MeshBuilder BuildGoldenTree(int b)
        {
            var mb = Builder(VariantSeed("prop_tree_golden", b), 0.06f, 0.3f, 1.2f);
            mb.WindGradient = true; mb.WindY0 = 1.8f; mb.WindY1 = 8.5f; mb.Wind = 1.7f;
            var bark = Paint.Hex("#857565");
            Trunk(mb, new[] { new Vector2(0.46f, 0f), new Vector2(0.34f, 0.35f), new Vector2(0.3f, 1.4f), new Vector2(0.27f, 2.6f), new Vector2(0.21f, 3.5f) }, 7, bark, 0.8f);
            Roots(mb, 5, 0.6f, 0.21f, 0.6f, b * 29f, bark);
            mb.Color = bark;
            var fork = new Vector3(0f, 3.2f, 0f);
            Vector3[] tips = { new Vector3(-1.2f, 5.0f, 0.15f), new Vector3(1.3f, 5.2f, 0.25f), new Vector3(0.15f, 5.9f, -0.35f), new Vector3(-0.25f, 5.5f, 0.75f) };
            foreach (var t in tips) mb.Segment(fork, t, 0.19f, 0.07f, 5);
            mb.Segment(new Vector3(0f, 2.3f, 0f), new Vector3(1.0f, 3.4f, -0.4f), 0.11f, 0.05f, 4);
            GoldenPalette(b, out var top, out var side, out var bottom);
            var ramp = GoldenRamp(top, side, bottom, 3.6f, 8.8f);
            // a tall, rounded crown (beech-like): big soft masses and smaller lumps on the silhouette
            (Vector3 c, Vector3 r)[] blobs =
            {
                (new Vector3(0f, 6.0f, 0.2f), new Vector3(2.1f, 2.2f, 1.9f)),
                (new Vector3(-1.45f, 5.1f, 0.05f), new Vector3(1.35f, 1.35f, 1.3f)),
                (new Vector3(1.5f, 5.25f, 0.3f), new Vector3(1.35f, 1.4f, 1.3f)),
                (new Vector3(0.3f, 7.5f, 0.1f), new Vector3(1.45f, 1.3f, 1.35f)),
                (new Vector3(-0.7f, 4.7f, -0.95f), new Vector3(1.2f, 1.0f, 1.0f)),
                (new Vector3(0.6f, 4.9f, 1.3f), new Vector3(1.3f, 1.15f, 1.1f)),
            };
            (Vector3 c, Vector3 r)[] lumps =
            {
                (new Vector3(-2.45f, 5.6f, 0.3f), new Vector3(0.75f, 0.7f, 0.7f)), (new Vector3(2.55f, 5.8f, 0.4f), new Vector3(0.75f, 0.72f, 0.7f)),
                (new Vector3(-1.35f, 7.55f, 0.3f), new Vector3(0.75f, 0.7f, 0.7f)), (new Vector3(1.45f, 7.35f, 0.4f), new Vector3(0.8f, 0.72f, 0.75f)),
                (new Vector3(0.1f, 8.55f, 0.3f), new Vector3(0.8f, 0.6f, 0.75f)), (new Vector3(1.0f, 4.3f, -0.85f), new Vector3(0.75f, 0.58f, 0.65f)),
                (new Vector3(-1.85f, 4.35f, -0.5f), new Vector3(0.7f, 0.58f, 0.65f)),
            };
            for (int i = 0; i < blobs.Length; i++)
            {
                var c = blobs[i].c + new Vector3((mb.Random01() - 0.5f) * 0.25f, (mb.Random01() - 0.5f) * 0.25f, 0f);
                SoftLump(mb, c, blobs[i].r, 1, i * 41f + b * 17f, ramp, 0.13f);
            }
            for (int i = 0; i < lumps.Length; i++)
                SoftLump(mb, lumps[i].c, lumps[i].r, 1, i * 59f + b * 13f, ramp, 0.15f);
            // a few leaves drifting down, caught in the air below the crown
            mb.Wind = 2.2f;
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.1f + b;
                var at = new Vector3(Mathf.Cos(a) * (1.6f + (i % 2) * 0.6f), 2.0f + (i % 3) * 0.7f, Mathf.Sin(a) * 1.2f - 0.4f);
                Leaf(mb, at, new Vector3(Mathf.Sin(a), 0.3f, Mathf.Cos(a)), new Vector3(0.2f, 0.6f, -1f), 0.16f, i % 2 == 0 ? side : top);
                Leaf(mb, at, new Vector3(Mathf.Sin(a), 0.3f, Mathf.Cos(a)), new Vector3(-0.2f, -0.6f, 1f), 0.16f, Paint.Shade(side, 0.85f));
            }
            mb.Wind = 0f; mb.WindGradient = false;
            return mb;
        }

        /// <summary>The golden tree's fallen leaves: a loose ellipse of flat leaves on the ground (a ground part).</summary>
        static MeshBuilder BuildFallenLeaves(int b)
        {
            var mb = new MeshBuilder(VariantSeed("prop_tree_golden_leaves", b)) { Jitter = 0.08f };
            GoldenPalette(b, out var top, out var side, out var bottom);
            Color[] cols = { side, top, Color.Lerp(side, bottom, 0.5f), Paint.Hex("#C9653A"), Paint.Hex("#F0C860") };
            for (int i = 0; i < 70; i++)
            {
                float a = mb.Random01() * Mathf.PI * 2f;
                float d = Mathf.Lerp(0.55f, 1f, Mathf.Sqrt(mb.Random01()));
                var at = new Vector3(Mathf.Cos(a) * d * 2.6f, 0.012f + mb.Random01() * 0.012f, Mathf.Sin(a) * d * 1.9f + 0.2f);
                float yaw = mb.Random01() * Mathf.PI * 2f;
                Leaf(mb, at, new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), Vector3.up, 0.13f + 0.06f * mb.Random01(), cols[i % cols.Length]);
            }
            return mb;
        }

        // ------------------------------------------------------------------ farmhouse (≈ 6.4 × 3.6 m, 6.1 m; collider 8.4 × 5.2, centred)

        static PropModel Farmhouse(string art, int seed) =>
            LitBuilding(art, seed, BuildFarmhouse, 3.4f,
                        new Vector3(-2.4f, 1.65f, -1.95f), new Vector3(0.05f, 1.65f, -1.95f), new Vector3(-0.35f, 2.45f, -2.05f),
                        new Vector3(-3.35f, 1.7f, -0.45f), new Vector3(-2.3f, 1.65f, 1.95f));

        /// <summary>A straw roll lying along X on the ground at `at` (radius r).</summary>
        static void HayRoll(MeshBuilder mb, Vector3 at, float r, float len, float yaw)
        {
            var keep = mb.Color;
            mb.Push().Translate(at + Vector3.up * r).Rotate(0f, yaw, 0f).Rotate(0f, 0f, 90f).Translate(0f, -len * 0.5f, 0f);
            mb.Color = Pal.Thatch;
            mb.Lathe(new[] { new Vector2(0f, 0f), new Vector2(r * 0.8f, 0f), new Vector2(r, r * 0.12f), new Vector2(r, len - r * 0.12f), new Vector2(r * 0.8f, len), new Vector2(0f, len) },
                     8, false, false, false, new[] { Pal.ThatchLight, Pal.ThatchLight, Pal.Thatch, Pal.Thatch, Pal.ThatchLight, Pal.ThatchLight });
            mb.Color = Pal.RopeStraw;
            mb.Torus(new Vector3(0f, len * 0.3f, 0f), r + 0.008f, 0.02f, 8, 3);
            mb.Torus(new Vector3(0f, len * 0.7f, 0f), r + 0.008f, 0.02f, 8, 3);
            mb.Pop();
            mb.Color = keep;
        }

        static MeshBuilder BuildFarmhouse(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_farmhouse", b), 0.06f, 0.3f, 0.9f);
            const float X0 = -3.2f, X1 = 0.8f, Z0 = -1.8f, Z1 = 1.8f, YB = 0.4f, YE = 2.85f;
            const float xc = (X0 + X1) * 0.5f, W = X1 - X0;
            var wash = Color.Lerp(Pal.Cream, Paint.Hex("#F3E2C2"), b * 0.25f);
            var sand = Paint.Hex("#C9B08A");
            var timber = Pal.TimberDark;
            var shutter = new[] { Paint.Hex("#5E8C6A"), Paint.Hex("#6F8FC4"), Paint.Hex("#B8664E"), Paint.Hex("#7E9B5A") }[b];
            // stone footing and whitewashed walls
            mb.Color = sand;
            mb.BoxOn(new Vector3(xc, 0f, 0f), new Vector3(W + 0.14f, YB, Z1 - Z0 + 0.14f), Paint.Shade(sand, 1.1f));
            mb.Color = wash;
            mb.BoxOn(new Vector3(xc, YB, 0f), new Vector3(W, YE - YB + 0.1f, Z1 - Z0));
            // sandstone quoins on the corners
            mb.Color = sand;
            for (int k = 0; k < 4; k++)
            {
                float y = YB + 0.3f + k * 0.6f;
                bool wide = k % 2 == 0;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        if (sx > 0 && sz > 0) continue;   // hidden by the barn
                        float x = sx < 0 ? X0 : X1, z = sz < 0 ? Z0 : Z1;
                        mb.Box(new Vector3(x - sx * (wide ? 0.22f : 0.15f), y, z + sz * 0.02f), new Vector3(wide ? 0.46f : 0.32f, 0.5f, 0.06f));
                        mb.Box(new Vector3(x + sx * 0.02f, y, z - sz * (wide ? 0.15f : 0.22f)), new Vector3(0.06f, 0.5f, wide ? 0.32f : 0.46f));
                    }
            }
            // front: door with a little porch roof and lantern, two shuttered windows, a flower box
            var doorWood = Paint.Shade(shutter, 0.85f);
            Door(mb, -1.15f, YB, 0.95f, 2.0f, Z0, doorWood, timber, false, Pal.Brass);
            mb.Color = Pal.StoneLight;
            mb.BoxOn(new Vector3(-1.15f, 0f, Z0 - 0.22f), new Vector3(1.3f, 0.38f, 0.44f));
            mb.BoxOn(new Vector3(-1.15f, 0f, Z0 - 0.55f), new Vector3(1.1f, 0.18f, 0.3f));
            mb.Color = timber;
            OBox(mb, new Vector3(-1.15f, YB + 2.32f, Z0 - 0.36f), new Vector3(1.55f, 0.07f, 0.78f), Quaternion.Euler(-22f, 0f, 0f));
            mb.Color = Pal.Thatch;
            OBox(mb, new Vector3(-1.15f, YB + 2.37f, Z0 - 0.36f), new Vector3(1.6f, 0.06f, 0.8f), Quaternion.Euler(-22f, 0f, 0f));
            mb.Color = timber;
            for (int s = -1; s <= 1; s += 2)
                Beam(mb, new Vector3(-1.15f + s * 0.68f, YB + 1.75f, Z0 - 0.02f), new Vector3(-1.15f + s * 0.68f, YB + 2.15f, Z0 - 0.62f), 0.07f, 0.07f, Vector3.right);
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(-0.35f, 2.75f, Z0 - 0.02f), new Vector3(-0.35f, 2.75f, Z0 - 0.28f), 0.03f, 0.03f, Vector3.right);
            IronLantern(mb, new Vector3(-0.35f, 2.47f, Z0 - 0.28f), 1f, lit);
            Window(mb, -2.4f, 1.65f, 0.72f, 0.85f, Z0, lit, timber, shutter);
            FlowerBox(mb, -2.4f, 1.06f, 0.95f, Z0, Pal.Wood, b * 3 + 1);
            Window(mb, 0.05f, 1.65f, 0.72f, 0.85f, Z0, lit, timber, shutter);
            // left gable: a window (Rotate(0, 90, 0): local −Z faces −X, local x = −world z)
            mb.Push().Rotate(0f, 90f, 0f);
            Window(mb, 0.45f, 1.7f, 0.62f, 0.78f, X0, lit, timber, shutter);
            mb.Pop();
            // back: a window and the kitchen door (Rotate(0, 180, 0): local −Z faces +Z, local x = −world x)
            mb.Push().Rotate(0f, 180f, 0f);
            Window(mb, 2.3f, 1.65f, 0.7f, 0.8f, -Z1, lit, timber, shutter);
            Door(mb, 0.55f, YB, 0.85f, 1.9f, -Z1, Pal.Wood, timber, false);
            mb.Pop();
            mb.Color = Pal.StoneLight;
            mb.BoxOn(new Vector3(-0.55f, 0f, Z1 + 0.2f), new Vector3(1.1f, 0.36f, 0.4f));
            // thatch with a turf ridge and a sandstone chimney
            const float baseY = 2.75f, height = 2.1f, halfDepth = 2.25f, p = 1.35f;
            mb.Push().Translate(xc, 0f, 0f);
            mb.Color = Color.Lerp(Pal.Thatch, Pal.ThatchLight, 0.2f + b * 0.1f);
            mb.Jitter = 0.045f;
            ThatchRoof(mb, W * 0.5f + 0.45f, 0f, halfDepth, baseY, height, 0.32f, p, b);
            mb.Jitter = 0.06f;
            // a turf ridge: a mossy roll along the crest with a few flowers
            var ridge = new List<Vector3[]>();
            for (int i = 0; i <= 6; i++)
            {
                float x = Mathf.Lerp(-W * 0.5f - 0.05f, W * 0.5f + 0.05f, i / 6f);
                float y = baseY + height - 0.06f + Mathf.Sin(i * 1.7f + b) * 0.025f;
                ridge.Add(new[] { new Vector3(x, y - 0.08f, -0.34f), new Vector3(x, y + 0.1f, -0.2f), new Vector3(x, y + 0.14f, 0.02f), new Vector3(x, y + 0.1f, 0.22f), new Vector3(x, y - 0.08f, 0.34f) });
            }
            Loft(mb, ridge, true, true, n => n.y > 0.5f ? Pal.MossLight : Pal.Moss);
            for (int i = 0; i < 5; i++)
                Bloom(mb, new Vector3(-1.4f + i * 0.7f, baseY + height + 0.09f, -0.16f + (i % 2) * 0.08f), 0.06f, new Vector3(0f, 1f, -0.3f), HlFlowers[(i + b) % 4], Pal.Honey, i * 30f);
            mb.Pop();
            Chimney(mb, new Vector3(X0 + 0.75f, 3.7f, 0.5f), 0.62f, 0.58f, 2.0f, sand, 3f);
            // barn wing: stone plinth, plank walls, shingle roof, big double doors, a hayloft on the gable
            const float BX0 = X1, BX1 = 3.2f, BZ0 = -1.4f, BZ1 = Z1, BY = 0.3f, BYE = 2.75f;
            const float bxc = (BX0 + BX1) * 0.5f, bzc = (BZ0 + BZ1) * 0.5f, BW = BX1 - BX0, BD = BZ1 - BZ0;
            var barn = new[] { Paint.Hex("#9C5E46"), Paint.Hex("#8C6B4E"), Paint.Hex("#A0654A"), Paint.Hex("#7E6A58") }[b];
            mb.Color = Pal.Stone;
            mb.BoxOn(new Vector3(bxc, 0f, bzc), new Vector3(BW + 0.1f, BY, BD + 0.1f), Pal.StoneLight);
            PlankWall(mb, new Vector3(bxc, BY, bzc), BW, BYE - BY, BD, barn, 0.24f);
            mb.Color = Paint.Shade(barn, 0.7f);
            for (int i = 1; i < 13; i++)
            {
                mb.Box(new Vector3(BX1 + 0.008f, (BY + BYE) * 0.5f, BZ0 + BD * i / 13f), new Vector3(0.016f, BYE - BY, 0.022f));
                if (i < 10) mb.Box(new Vector3(BX0 + BW * i / 10f, (BY + BYE) * 0.5f, BZ1 + 0.008f), new Vector3(0.022f, BYE - BY, 0.016f));
            }
            var doorCol = Paint.Shade(barn, 1.12f);
            for (int s = -1; s <= 1; s += 2)
            {
                float dx = bxc + s * 0.43f;
                mb.Color = doorCol;
                mb.Box(new Vector3(dx, BY + 1.0f, BZ0 - 0.04f), new Vector3(0.82f, 2.0f, 0.06f));
                mb.Color = Pal.Cream;
                mb.Box(new Vector3(dx, BY + 0.12f, BZ0 - 0.08f), new Vector3(0.82f, 0.1f, 0.03f));
                mb.Box(new Vector3(dx, BY + 1.9f, BZ0 - 0.08f), new Vector3(0.82f, 0.1f, 0.03f));
                Beam(mb, new Vector3(dx - 0.36f, BY + 0.17f, BZ0 - 0.08f), new Vector3(dx + 0.36f, BY + 1.85f, BZ0 - 0.08f), 0.08f, 0.03f, Vector3.back);
                Beam(mb, new Vector3(dx + 0.36f, BY + 0.17f, BZ0 - 0.08f), new Vector3(dx - 0.36f, BY + 1.85f, BZ0 - 0.08f), 0.08f, 0.03f, Vector3.back);
            }
            mb.Color = timber;
            mb.Box(new Vector3(bxc, BY + 2.06f, BZ0 - 0.05f), new Vector3(1.9f, 0.12f, 0.1f));
            var shingle = new[] { Paint.Hex("#7A6A5A"), Paint.Hex("#8C5A44"), Paint.Hex("#6E7480"), Paint.Hex("#7E6450") }[b];
            const float brh = 1.45f;
            CourseRoof(mb, new Vector3(bxc - 0.15f, BYE, bzc), BW + 0.3f, BD, brh, 0.3f, 4, shingle, Paint.Shade(shingle, 0.88f), Paint.Shade(shingle, 0.72f));
            mb.Color = barn;
            GableWall(mb, BX1, BYE, BZ0, BZ1, brh - 0.1f, 1f);
            // hayloft door on the gable, hay spilling out, a hoist beam with rope and hook
            mb.Push().Rotate(0f, -90f, 0f);   // local −Z faces +X, local x = world z
            mb.Color = Pal.Ink;
            QuadC(mb, new Vector3(bzc - 0.42f, BYE + 0.05f, -BX1 - 0.01f), new Vector3(bzc - 0.42f, BYE + 0.8f, -BX1 - 0.01f),
                  new Vector3(bzc + 0.42f, BYE + 0.8f, -BX1 - 0.01f), new Vector3(bzc + 0.42f, BYE + 0.05f, -BX1 - 0.01f), Vector3.back, Paint.Hex("#3A2E28"));
            mb.Pop();
            FacetBlob(mb, new Vector3(BX1 + 0.05f, BYE + 0.12f, bzc), new Vector3(0.12f, 0.16f, 0.38f), 0, 0.3f, b + 41, 0f,
                      ByNormal(Pal.ThatchLight, Pal.Thatch, Pal.ThatchDark));
            mb.Color = timber;
            Beam(mb, new Vector3(BX1 - 0.3f, BYE + 1.2f, bzc), new Vector3(BX1 + 0.65f, BYE + 1.2f, bzc), 0.12f, 0.12f);
            mb.Color = Pal.RopeStraw;
            mb.Segment(new Vector3(BX1 + 0.55f, BYE + 1.14f, bzc), new Vector3(BX1 + 0.55f, BYE + 0.15f, bzc), 0.014f, 0.014f, 3);
            mb.Color = Pal.Iron;
            mb.Torus(new Vector3(BX1 + 0.55f, BYE + 0.1f, bzc), 0.05f, 0.014f, 6, 3);
            // yard dressing: hay rolls, a wheat sheaf by the door, a milk churn at the back, a cart wheel leaning on the barn
            HayRoll(mb, new Vector3(2.75f, 0f, -1.85f), 0.36f, 0.75f, 12f);
            HayRoll(mb, new Vector3(3.45f, 0f, -0.9f), 0.33f, 0.7f, 75f);
            mb.Color = Pal.Honey;
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.9f;
                var root = new Vector3(-1.95f + Mathf.Cos(a) * 0.06f, 0f, Z0 - 0.25f + Mathf.Sin(a) * 0.05f);
                var tip = root + new Vector3(Mathf.Cos(a) * 0.12f, 0.9f, Mathf.Sin(a) * 0.08f);
                mb.Color = Pal.ThatchLight;
                mb.Segment(root, tip, 0.03f, 0.02f, 3, false, false);
                mb.Color = Pal.Honey;
                Gem(mb, tip + Vector3.up * 0.06f, new Vector3(0.035f, 0.08f, 0.035f));
            }
            mb.Color = Pal.RopeStraw;
            mb.Torus(new Vector3(-1.95f, 0.5f, Z0 - 0.25f), 0.09f, 0.02f, 8, 3);
            mb.Color = Pal.StoneCool;
            mb.Push().Translate(-1.3f, 0f, Z1 + 0.35f);
            mb.Lathe(new[] { new Vector2(0.17f, 0f), new Vector2(0.19f, 0.45f), new Vector2(0.12f, 0.58f), new Vector2(0.1f, 0.68f), new Vector2(0.12f, 0.7f), new Vector2(0f, 0.72f) }, 8, false, true, false);
            mb.Pop();
            mb.Push().Translate(1.15f, 0.5f, BZ0 - 0.13f).Rotate(0f, 0f, 14f).Rotate(80f, 0f, 0f);
            mb.Color = timber;
            mb.Torus(Vector3.zero, 0.48f, 0.05f, 12, 4);
            mb.Cylinder(new Vector3(0f, -0.06f, 0f), 0.09f, 0.09f, 0.12f, 6);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                Beam(mb, new Vector3(Mathf.Cos(a) * 0.08f, 0f, Mathf.Sin(a) * 0.08f), new Vector3(Mathf.Cos(a) * 0.45f, 0f, Mathf.Sin(a) * 0.45f), 0.045f, 0.035f, Vector3.up);
            }
            mb.Pop();
            WildTuft(mb, new Vector3(X0 - 0.2f, 0f, Z0 - 0.15f), 0.4f, 6, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(BX1 + 0.25f, 0f, BZ1 - 0.2f), 0.4f, 6, HlGrass, HlGrassDark);
            return mb;
        }

        // ------------------------------------------------------------------ ruined watchtower (round, ≈ 8 m; collider 3.8 × 3.8)

        static MeshBuilder BuildWatchtowerRuin(int b)
        {
            var mb = Builder(VariantSeed("prop_watchtower_ruin", b), 0.07f, 0.35f, 1.0f);
            const int N = 16;
            const float R = 1.55f, T = 0.42f, Course = 0.5f, DoorTop = 2.1f;
            Color[] stones = { Paint.Hex("#C7B89C"), Paint.Hex("#B1A38B"), Paint.Hex("#D4C6A6"), Paint.Hex("#BCAE94") };
            int sd = VariantSeed("watchtower", b);
            float step = 360f / N;
            var heights = new float[N];
            for (int j = 0; j < N; j++)
            {
                float mid = (-90f - step + (j + 0.5f) * step) * Mathf.Deg2Rad;
                float back = Mathf.Cos(mid - (120f + b * 25f) * Mathf.Deg2Rad);
                float h = 5.0f + 1.6f * back + 0.55f * WildHash(j, 1, sd);
                heights[j] = Mathf.Max(2.6f, Mathf.Round(h / (Course * 0.5f)) * Course * 0.5f);
            }
            heights[0] = Mathf.Max(heights[0], 2.9f);
            heights[1] = Mathf.Max(heights[1], 2.9f);
            for (int j = 0; j < N; j++)
            {
                float a0 = (-90f - step + j * step) * Mathf.Deg2Rad, a1 = a0 + step * Mathf.Deg2Rad;
                var poly = new[]
                {
                    new Vector2(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R), new Vector2(Mathf.Cos(a1) * R, Mathf.Sin(a1) * R),
                    new Vector2(Mathf.Cos(a1) * (R - T), Mathf.Sin(a1) * (R - T)), new Vector2(Mathf.Cos(a0) * (R - T), Mathf.Sin(a0) * (R - T)),
                };
                bool door = j == 0 || j == 1;
                float y = door ? DoorTop : 0f;
                float first = j % 2 == 0 ? Course : Course * 0.5f;   // running bond
                int k = 0;
                while (y < heights[j] - 0.01f)
                {
                    float y1 = Mathf.Min(heights[j], (door && k == 0) ? y + Course : (k == 0 ? first : y + Course));
                    if (y1 - y < 0.08f) { y1 = heights[j]; }
                    mb.Color = stones[(int)((WildHash(j, k, sd) * 0.5f + 0.5f) * 3.99f)];
                    mb.Extrude(poly, y, y1, false, y1 >= heights[j] - 0.01f);
                    y = y1;
                    k++;
                }
                // moss on the tallest stubs
                if (heights[j] > 5.2f && j % 3 == 0)
                {
                    float am = (a0 + a1) * 0.5f;
                    FacetBlob(mb, new Vector3(Mathf.Cos(am) * (R - T * 0.5f), heights[j] + 0.02f, Mathf.Sin(am) * (R - T * 0.5f)), new Vector3(0.24f, 0.07f, 0.2f), 0, 0.25f, sd + j, 0f,
                              ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
                }
            }
            // doorway lintel, threshold, arrow slits
            mb.Color = Pal.TimberDark;
            mb.Box(new Vector3(0f, DoorTop - 0.08f, -R + T * 0.5f), new Vector3(1.35f, 0.16f, T + 0.06f));
            for (int s = 0; s < 3; s++)
            {
                int j = new[] { 4, 9, 13 }[s];
                if (heights[j] < 3.9f) continue;
                float am = (-90f - step + (j + 0.5f) * step) * Mathf.Deg2Rad;
                float d = R * Mathf.Cos(step * 0.5f * Mathf.Deg2Rad) + 0.006f;
                var c = new Vector3(Mathf.Cos(am) * d, 3.35f, Mathf.Sin(am) * d);
                var n = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                var side = Vector3.Cross(Vector3.up, n);
                QuadC(mb, c - side * 0.07f - Vector3.up * 0.38f, c - side * 0.07f + Vector3.up * 0.38f, c + side * 0.07f + Vector3.up * 0.38f, c + side * 0.07f - Vector3.up * 0.38f, n, Pal.Ink);
            }
            // inside: earth floor, rubble, the beams of a fallen floor
            mb.Color = Paint.Hex("#6B5A48");
            mb.Disc(new Vector3(0f, 0.02f, 0f), R - T + 0.02f, 12);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f + b;
                mb.Color = stones[i % 4];
                OBox(mb, new Vector3(Mathf.Cos(a) * 0.55f, 0.12f, Mathf.Sin(a) * 0.5f), new Vector3(0.36f, 0.22f, 0.26f), Quaternion.Euler(i * 13f, i * 47f, i * 9f));
            }
            mb.Color = Pal.TimberDark;
            Beam(mb, new Vector3(-R + 0.2f, 3.4f, 0.35f), new Vector3(R - 0.2f, 3.5f, 0.2f), 0.16f, 0.16f);
            Beam(mb, new Vector3(-0.6f, 0.1f, -0.2f), new Vector3(0.9f, 2.6f, 0.45f), 0.15f, 0.15f);
            // fallen blocks outside the collapsed side, ivy, a tattered pennant on the tallest stub
            for (int i = 0; i < 7; i++)
            {
                float a = (-40f + (i - 3) * 16f + b * 12f) * Mathf.Deg2Rad;
                float d = R + 0.25f + (i % 3) * 0.12f;
                mb.Color = stones[(i + 2) % 4];
                OBox(mb, new Vector3(Mathf.Cos(a) * d, 0.11f + (i % 2) * 0.04f, Mathf.Sin(a) * d), new Vector3(0.42f, 0.22f, 0.3f), Quaternion.Euler(i * 7f, i * 61f + 20f, (i % 2) * 14f));
            }
            for (int s = 0; s < 2; s++)
            {
                float am = (s == 0 ? 205f : 165f + b * 6f) * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                Ivy(mb, n * (R + 0.03f) + Vector3.up * 0.2f, n * (R + 0.03f) + Vector3.up * (2.4f + s * 0.8f), 12, 0.35f, n);
            }
            int tall = 0;
            for (int j = 1; j < N; j++) if (heights[j] > heights[tall]) tall = j;
            float at = (-90f - step + (tall + 0.5f) * step) * Mathf.Deg2Rad;
            var pole = new Vector3(Mathf.Cos(at) * (R - T * 0.5f), heights[tall], Mathf.Sin(at) * (R - T * 0.5f));
            mb.Color = Pal.TimberDark;
            mb.Segment(pole - Vector3.up * 0.3f, pole + Vector3.up * 1.5f, 0.04f, 0.03f, 5);
            mb.Color = Paint.Hex("#B8452E");
            Slab(mb, new[] { pole + new Vector3(0.03f, 1.45f, 0f), pole + new Vector3(0.75f, 1.38f, 0.05f), pole + new Vector3(0.55f, 1.18f, 0.05f), pole + new Vector3(0.7f, 1.0f, 0.05f), pole + new Vector3(0.03f, 1.05f, 0f) },
                 Vector3.back, 0.015f);
            mb.Color = Pal.Gold;
            Slab(mb, new[] { pole + new Vector3(0.03f, 1.33f, -0.01f), pole + new Vector3(0.4f, 1.29f, 0.015f), pole + new Vector3(0.4f, 1.22f, 0.015f), pole + new Vector3(0.03f, 1.24f, -0.01f) },
                 Vector3.back, 0.02f);
            WildTuft(mb, new Vector3(-R - 0.1f, 0f, -0.3f), 0.45f, 6, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(R * 0.6f, 0f, -R - 0.05f), 0.4f, 5, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(-R * 0.75f, 0f, R * 0.7f), 0.45f, 6, HlGrass, HlGrassDark);
            return mb;
        }

        // ------------------------------------------------------------------ gnoll hide tent (≈ 3.2 m; collider 2.8 × 2.6): glows inside when lit

        static PropModel GnollTent(string art, int seed) =>
            LitBuilding(art, seed, BuildGnollTent, 1.4f, new Vector3(0f, 0.75f, -1.25f));

        static MeshBuilder BuildGnollTent(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_gnoll_tent", b), 0.05f, 0.3f, 0.6f);
            Color[] hides = { Paint.Hex("#B98A5C"), Paint.Hex("#9C6E48"), Paint.Hex("#C9A574"), Paint.Hex("#86603F"), Paint.Hex("#AD8257") };
            const int N = 9;
            const float R = 1.45f, Rm = 0.98f, Hm = 1.25f, Apex = 2.6f;
            int sd = VariantSeed("gnolltent", b);
            var ground = new Vector3[N + 1];
            var mid = new Vector3[N + 1];
            for (int j = 0; j <= N; j++)
            {
                float a = (-110f + j * 40f) * Mathf.Deg2Rad;   // panel 0 (between vertices 0 and 1) is the doorway, centred on −Z
                float kr = 1f + WildHash(j % N, 0, sd) * 0.06f;
                ground[j] = new Vector3(Mathf.Cos(a) * R * kr, -0.02f, Mathf.Sin(a) * R * kr);
                mid[j] = new Vector3(Mathf.Cos(a) * Rm * kr, Hm + WildHash(j % N, 1, sd) * 0.07f, Mathf.Sin(a) * Rm * kr);
            }
            var apex = new Vector3(0.04f, Apex, 0.05f);
            var inside = lit ? Color.Lerp(Paint.Hex("#6A4030"), Pal.GlowDeep, 0.55f) : Paint.Hex("#3E2C24");
            for (int j = 0; j < N; j++)
            {
                var col = hides[(j * 2 + b) % hides.Length];
                var c = (mid[j] + mid[j + 1]) * 0.5f;
                var outward = new Vector3(c.x, 0.35f, c.z);
                if (j != 0)
                {
                    var sagC = (ground[j] + ground[j + 1] + mid[j] + mid[j + 1]) * 0.25f * 0.97f;
                    sagC.y = (ground[j].y + mid[j].y) * 0.5f;
                    // a hide panel, sagging a little between the poles (4 triangles around the sag point)
                    Tri(mb, ground[j], ground[j + 1], sagC, outward, col);
                    Tri(mb, ground[j + 1], mid[j + 1], sagC, outward, Paint.Shade(col, 1.04f));
                    Tri(mb, mid[j + 1], mid[j], sagC, outward, col);
                    Tri(mb, mid[j], ground[j], sagC, outward, Paint.Shade(col, 0.96f));
                    mb.Emission = lit ? 0.45f : 0f;
                    Tri(mb, ground[j], sagC, ground[j + 1], -outward, inside);
                    Tri(mb, ground[j + 1], sagC, mid[j + 1], -outward, inside);
                    Tri(mb, mid[j + 1], sagC, mid[j], -outward, inside);
                    Tri(mb, mid[j], sagC, ground[j], -outward, inside);
                    mb.Emission = 0f;
                }
                Tri(mb, mid[j], mid[j + 1], apex, outward + Vector3.up, Paint.Shade(col, 1.08f));
                mb.Emission = lit ? 0.3f : 0f;
                Tri(mb, mid[j], apex, mid[j + 1], -outward - Vector3.up, Paint.Shade(inside, 0.8f));
                mb.Emission = 0f;
                // hide seams / lacing down each pole line
                mb.Color = Paint.Hex("#5A4030");
                mb.Segment(ground[j] + (ground[j] - Vector3.zero).normalized * 0.01f, mid[j] + (mid[j] - Vector3.up * Hm).normalized * 0.01f, 0.025f, 0.022f, 3, false, false);
            }
            // the doorway: dark floor, flaps folded back
            mb.Color = Paint.Hex("#4A3A2C");
            mb.Disc(new Vector3(0f, 0.01f, 0f), Rm * 0.95f, 9);
            for (int s = 0; s < 2; s++)
            {
                var g = ground[s]; var m = mid[s];
                var fold = g + (s == 0 ? new Vector3(-0.38f, 0.42f, -0.22f) : new Vector3(0.38f, 0.42f, -0.22f));
                mb.Color = hides[(b + 3 + s) % hides.Length];
                Slab(mb, new[] { g, m, fold }, Vector3.Cross(m - g, fold - g).z > 0f ? -Vector3.Cross(m - g, fold - g) : Vector3.Cross(m - g, fold - g), 0.025f);
            }
            // painted red zigzags on two panels
            mb.Color = Paint.Hex("#B8452E");
            for (int pi = 0; pi < 2; pi++)
            {
                int j = pi == 0 ? 2 : 7;
                var g0 = Vector3.Lerp(ground[j], mid[j], 0.45f); var g1 = Vector3.Lerp(ground[j + 1], mid[j + 1], 0.45f);
                var n = new Vector3((g0 + g1).x, 0.4f, (g0 + g1).z).normalized * 0.012f;
                for (int k = 0; k < 4; k++)
                {
                    var p0 = Vector3.Lerp(g0, g1, 0.15f + k * 0.18f) + n * 2f + Vector3.up * (k % 2 == 0 ? -0.1f : 0.12f);
                    var p1 = Vector3.Lerp(g0, g1, 0.15f + (k + 1) * 0.18f) + n * 2f + Vector3.up * ((k + 1) % 2 == 0 ? -0.1f : 0.12f);
                    mb.Segment(p0, p1, 0.035f, 0.035f, 3, false, true);
                }
            }
            // crossed poles poking out of the smoke hole, with bones and a feather
            mb.Color = Pal.Timber;
            for (int k = 0; k < 5; k++)
            {
                int j = (k * 2 + 1) % N;
                var dir = (apex - ground[j]).normalized;
                mb.Segment(ground[j] + Vector3.up * 0.02f, apex + dir * (0.65f + 0.15f * (k % 2)), 0.045f, 0.035f, 5);
            }
            mb.Color = Paint.Hex("#2E2622");
            mb.Torus(apex + Vector3.down * 0.05f, 0.16f, 0.04f, 8, 3);
            WildBone(mb, apex + new Vector3(0.35f, 0.55f, 0.1f), apex + new Vector3(0.6f, 0.3f, 0.12f), 0.025f, WildBoneCol);
            mb.Color = Paint.Hex("#2F2B30");
            mb.Blade(apex + new Vector3(-0.3f, 0.55f, 0.1f), apex + new Vector3(-0.55f, 0.95f, 0.15f), 0.1f, Vector3.forward);
            // a horned skull over the doorway
            var top = (mid[0] + mid[1]) * 0.5f;
            WildSkull(mb, top + new Vector3(0f, 0.08f, -0.06f), 1.0f, 0f, -12f, WildBoneCol, 1.1f);
            // stakes and guy ropes
            for (int k = 0; k < 3; k++)
            {
                int j = 3 + k * 2;
                var stake = new Vector3(ground[j].x * 1.18f, 0f, ground[j].z * 1.18f);
                mb.Color = Pal.Timber;
                mb.Segment(stake, stake + new Vector3(0f, 0.28f, 0f), 0.03f, 0.02f, 4);
                mb.Color = Pal.RopeStraw;
                mb.Segment(stake + Vector3.up * 0.25f, mid[j], 0.012f, 0.012f, 3, false, false);
            }
            WildTuft(mb, new Vector3(-1.2f, 0f, -1.0f), 0.4f, 5, HlGrass, HlGrassDark);
            WildTuft(mb, new Vector3(1.3f, 0f, -0.7f), 0.35f, 5, HlGrass, HlGrassDark);
            return mb;
        }

        // ------------------------------------------------------------------ gnoll totem (≈ 3.6 m; collider 0.8 × 0.6)

        static void GnollHead(MeshBuilder mb, Vector3 at, float s, Color wood, Color paint, int seed)
        {
            var keep = mb.Color;
            mb.Push().Translate(at).Scale(s);
            mb.Color = wood;
            mb.Box(new Vector3(0f, 0.22f, 0f), new Vector3(0.36f, 0.44f, 0.34f));
            // muzzle
            mb.Push().Translate(0f, 0.14f, -0.12f).Rotate(-90f, 0f, 0f);
            mb.TaperedBox(Vector3.zero, new Vector3(0.22f, 0.22f, 0.18f), 0.3f, 0.2f);
            mb.Pop();
            mb.Color = Pal.Ink;
            mb.Box(new Vector3(0f, 0.2f, -0.34f), new Vector3(0.09f, 0.06f, 0.03f));
            // rounded hyena ears
            mb.Color = Paint.Shade(wood, 0.9f);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                mb.Push().Translate(sd * 0.15f, 0.48f, 0.02f).Rotate(0f, 0f, sd * -20f);
                mb.Sphere(new Vector3(0f, 0.06f, 0f), new Vector3(0.07f, 0.1f, 0.03f), 6, 4, false);
                mb.Pop();
            }
            // painted eyes, brow stripe, white fangs
            mb.Color = paint;
            mb.Box(new Vector3(0f, 0.36f, -0.172f), new Vector3(0.36f, 0.05f, 0.012f));
            Ngon(mb, new Vector3(-0.09f, 0.29f, -0.175f), 0.045f, 6, Vector3.back, Vector3.up, 0f, Pal.Paper);
            Ngon(mb, new Vector3(0.09f, 0.29f, -0.175f), 0.045f, 6, Vector3.back, Vector3.up, 0f, Pal.Paper);
            Ngon(mb, new Vector3(-0.09f, 0.29f, -0.178f), 0.02f, 4, Vector3.back, Vector3.up, 45f, Pal.Ink);
            Ngon(mb, new Vector3(0.09f, 0.29f, -0.178f), 0.02f, 4, Vector3.back, Vector3.up, 45f, Pal.Ink);
            mb.Color = Pal.Paper;
            for (int sd = -1; sd <= 1; sd += 2)
                mb.Segment(new Vector3(sd * 0.06f, 0.07f, -0.27f), new Vector3(sd * 0.055f, -0.0f, -0.28f), 0.018f, 0.003f, 3);
            mb.Pop();
            mb.Color = keep;
        }

        static MeshBuilder BuildGnollTotem(int b)
        {
            var mb = Builder(VariantSeed("prop_gnoll_totem", b), 0.06f, 0.3f, 0.5f);
            var wood = Paint.Hex("#8A5E3C");
            Color red = Paint.Hex("#B8452E"), ochre = Paint.Hex("#D19A3E"), chalk = Paint.Hex("#EDE3C9");
            // a heap of stones at the foot
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + b * 20f) * Mathf.Deg2Rad;
                FacetBlob(mb, new Vector3(Mathf.Cos(a) * 0.26f, 0.08f, Mathf.Sin(a) * 0.2f), new Vector3(0.17f, 0.12f, 0.14f), 0, 0.2f, b * 7 + i, 0.3f, Mossy(Pal.Stone, HlLichen, 0.75f));
            }
            mb.Color = Paint.Shade(wood, 0.85f);
            mb.Segment(new Vector3(0f, -0.05f, 0f), new Vector3(0f, 3.05f, 0f), 0.12f, 0.1f, 6);
            GnollHead(mb, new Vector3(0f, 0.55f, 0f), 1.15f, wood, red, b);
            GnollHead(mb, new Vector3(0f, 1.18f, 0f), 1.05f, Paint.Shade(wood, 1.12f), ochre, b + 1);
            GnollHead(mb, new Vector3(0f, 1.76f, 0f), 0.95f, wood, chalk, b + 2);
            // crossbar with dangling charms
            mb.Color = Pal.TimberDark;
            Beam(mb, new Vector3(-0.68f, 2.45f, 0f), new Vector3(0.68f, 2.5f, 0f), 0.08f, 0.08f, Vector3.forward);
            mb.Color = red;
            mb.Torus(new Vector3(0f, 2.47f, 0f), 0.12f, 0.03f, 8, 3);
            mb.WindGradient = true; mb.WindY0 = 2.45f; mb.WindY1 = 1.5f; mb.Wind = 1.4f;
            for (int s = -1; s <= 1; s += 2)
            {
                var hang = new Vector3(s * 0.6f, 2.48f, 0f);
                mb.Color = Pal.RopeStraw;
                mb.Segment(hang, hang + Vector3.down * 0.35f, 0.01f, 0.01f, 3, false, false);
                WildBone(mb, hang + new Vector3(-0.1f, -0.42f, 0f), hang + new Vector3(0.1f, -0.36f, 0f), 0.022f, WildBoneCol);
                mb.Color = s < 0 ? red : ochre;
                Slab(mb, new[] { hang + new Vector3(s * -0.12f, 0f, -0.02f), hang + new Vector3(s * -0.22f, 0f, -0.02f), hang + new Vector3(s * -0.26f, -0.7f, -0.02f), hang + new Vector3(s * -0.16f, -0.62f, -0.02f) },
                     Vector3.back, 0.012f);
                mb.Color = s < 0 ? Paint.Hex("#2F2B30") : chalk;
                mb.Blade(hang + new Vector3(s * 0.06f, -0.05f, 0.02f), hang + new Vector3(s * 0.12f, -0.38f, 0.04f), 0.08f, Vector3.forward);
            }
            mb.Wind = 0f; mb.WindGradient = false;
            // a great horned skull crowning the pole
            WildSkull(mb, new Vector3(0f, 2.95f, 0.02f), 1.35f, 0f, -8f, WildBoneCol, 1.6f, Paint.Hex("#4E3E34"));
            WildTuft(mb, new Vector3(0.35f, 0f, -0.25f), 0.38f, 5, HlGrass, HlGrassDark);
            return mb;
        }

        // ------------------------------------------------------------------ bone pile (≈ 0.9 m; collider 1.6 × 1.2)

        static MeshBuilder BuildBonePile(int b)
        {
            var mb = Builder(VariantSeed("prop_bonepile", b), 0.07f, 0.35f, 0.4f);
            int sd = VariantSeed("bonepile", b);
            FacetBlob(mb, new Vector3(0f, 0.02f, 0.02f), new Vector3(0.72f, 0.12f, 0.48f), 1, 0.15f, sd, 0.6f, ByNormal(Paint.Hex("#8C7458"), Paint.Hex("#7A644C"), Paint.Hex("#5E4C3A")));
            Color[] bones = { WildBoneCol, WildBoneDark, Paint.Hex("#E2D3B2") };
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + b * 17f) * Mathf.Deg2Rad;
                float y = 0.1f + (i % 3) * 0.08f + (i > 5 ? 0.1f : 0f);
                var c = new Vector3(WildHash(i, 1, sd) * 0.4f, y, WildHash(i, 2, sd) * 0.25f);
                var d = new Vector3(Mathf.Cos(a), WildHash(i, 3, sd) * 0.25f, Mathf.Sin(a) * 0.7f) * (0.24f + 0.08f * (i % 2));
                WildBone(mb, c - d, c + d, 0.03f, bones[i % 3]);
            }
            // a rib cage half sunk in the heap
            mb.Color = WildBoneCol;
            for (int k = 0; k < 4; k++)
            {
                float x = 0.22f + k * 0.1f;
                WildRib(mb, new[] { new Vector3(x, 0.08f, 0.15f), new Vector3(x - 0.02f, 0.32f, 0.05f), new Vector3(x - 0.03f, 0.38f, -0.12f), new Vector3(x - 0.02f, 0.26f, -0.26f) }, 0.022f, 0.012f, 4);
            }
            mb.Segment(new Vector3(0.18f, 0.08f, 0.16f), new Vector3(0.6f, 0.1f, 0.16f), 0.03f, 0.025f, 5);
            WildSkull(mb, new Vector3(-0.22f, 0.22f, -0.1f), 0.95f, 18f + b * 9f, -6f, WildBoneCol);
            WildSkull(mb, new Vector3(0.12f, 0.36f, 0.12f), 1.15f, -25f, 10f, Paint.Hex("#E2D3B2"), b % 2 == 0 ? 1.2f : 0f);
            WildSkull(mb, new Vector3(-0.55f, 0.08f, 0.2f), 0.7f, 60f, 4f, WildBoneDark);
            // a broken round shield
            mb.Push().Translate(0.52f, 0.2f, -0.18f).Rotate(-65f, 20f, 0f);
            mb.Color = Paint.Hex("#8A5E3C");
            mb.Cylinder(Vector3.zero, 0.24f, 0.24f, 0.04f, 9);
            mb.Color = Paint.Hex("#B8452E");
            mb.Cylinder(new Vector3(0f, 0.04f, 0f), 0.07f, 0.06f, 0.025f, 7);
            mb.Pop();
            for (int i = 0; i < 6; i++)
            {
                mb.Color = bones[i % 3];
                Gem(mb, new Vector3(WildHash(i, 7, sd) * 0.65f, 0.04f, -0.32f + WildHash(i, 8, sd) * 0.12f), new Vector3(0.04f, 0.025f, 0.03f));
            }
            WildTuft(mb, new Vector3(-0.68f, 0f, -0.15f), 0.35f, 5, HlGrass, HlGrassDark);
            return mb;
        }

        // ------------------------------------------------------------------ quarry cart on a stub of rails (≈ 1.3 m; collider 2.2 × 1.1)

        static MeshBuilder BuildQuarryCart(int b)
        {
            var mb = Builder(VariantSeed("prop_quarry_cart", b), 0.06f, 0.3f, 0.5f);
            var wood = b % 2 == 0 ? Pal.Wood : Pal.WoodGrey;
            // sleepers and rails along X
            mb.Color = Pal.Timber;
            for (int i = 0; i < 4; i++) mb.BoxOn(new Vector3(-0.84f + i * 0.56f, 0f, 0f), new Vector3(0.18f, 0.06f, 0.92f));
            mb.Color = Pal.Iron;
            for (int s = -1; s <= 1; s += 2) mb.BoxOn(new Vector3(0f, 0.06f, s * 0.31f), new Vector3(2.0f, 0.06f, 0.05f));
            // wheels resting on the rails
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    mb.Push().Translate(sx * 0.4f, 0.29f, sz * 0.31f).Rotate(90f, 0f, 0f);
                    mb.Color = Paint.Shade(Pal.Iron, 1.2f);
                    mb.Cylinder(new Vector3(0f, -0.035f, 0f), 0.17f, 0.17f, 0.07f, 8);
                    mb.Color = Pal.Iron;
                    mb.Cylinder(new Vector3(0f, -0.05f, 0f), 0.05f, 0.05f, 0.1f, 6);
                    mb.Pop();
                }
            // the tub: wider at the top, planked, with iron corner straps
            const float hx0 = 0.5f, hz0 = 0.3f, hx1 = 0.66f, hz1 = 0.42f, y0 = 0.3f, y1 = 0.92f;
            var bottom = new[] { new Vector3(-hx0, y0, -hz0), new Vector3(hx0, y0, -hz0), new Vector3(hx0, y0, hz0), new Vector3(-hx0, y0, hz0) };
            var top = new[] { new Vector3(-hx1, y1, -hz1), new Vector3(hx1, y1, -hz1), new Vector3(hx1, y1, hz1), new Vector3(-hx1, y1, hz1) };
            mb.Color = wood;
            Loft(mb, new List<Vector3[]> { bottom, top }, true, true);
            mb.Color = Paint.Shade(wood, 0.72f);
            for (int k = 1; k < 3; k++)
            {
                float t = k / 3f;
                var a = Vector3.Lerp(bottom[0], top[0], t); var c = Vector3.Lerp(bottom[1], top[1], t);
                Beam(mb, a + new Vector3(0f, 0f, -0.012f), c + new Vector3(0f, 0f, -0.012f), 0.022f, 0.012f, Vector3.back);
            }
            mb.Color = Pal.Iron;
            for (int i = 0; i < 4; i++) Beam(mb, bottom[i], top[i], 0.06f, 0.06f);
            for (int i = 0; i < 4; i++) Beam(mb, top[i], top[(i + 1) % 4], 0.05f, 0.05f);
            // a heap of cut sandstone blocks and rubble
            Color[] sandst = { Paint.Hex("#D8C29A"), Paint.Hex("#C9B08A"), Paint.Hex("#E3D0AA") };
            int sd = VariantSeed("quarrycart", b);
            for (int i = 0; i < 6; i++)
            {
                mb.Color = sandst[i % 3];
                var c = new Vector3(-0.38f + (i % 3) * 0.38f + WildHash(i, 1, sd) * 0.05f, y1 + 0.08f + (i / 3) * 0.14f, -0.12f + (i / 3) * 0.2f + WildHash(i, 2, sd) * 0.05f);
                OBox(mb, c, new Vector3(0.32f, 0.2f, 0.24f) * (i / 3 == 1 ? 0.85f : 1f), Quaternion.Euler(WildHash(i, 3, sd) * 12f, WildHash(i, 4, sd) * 30f, WildHash(i, 5, sd) * 12f));
            }
            for (int i = 0; i < 5; i++)
            {
                mb.Color = sandst[(i + 1) % 3];
                Gem(mb, new Vector3(-0.5f + i * 0.25f, y1 + 0.05f, 0.3f), new Vector3(0.09f, 0.06f, 0.08f));
            }
            // a pickaxe leaning on the end
            mb.Color = Pal.WoodLight;
            var hb = new Vector3(0.95f, 0f, -0.15f); var ht = new Vector3(0.72f, 0.95f, -0.1f);
            mb.Segment(hb, ht, 0.025f, 0.025f, 5);
            mb.Color = Paint.Shade(Pal.Iron, 1.25f);
            var dir = (ht - hb).normalized;
            var side = Vector3.Cross(dir, Vector3.forward).normalized;
            WildRib(mb, new[] { ht - side * 0.28f - dir * 0.08f, ht - side * 0.12f + dir * 0.02f, ht, ht + side * 0.12f + dir * 0.02f, ht + side * 0.3f - dir * 0.06f }, 0.03f, 0.012f, 4);
            WildTuft(mb, new Vector3(-1.0f, 0f, -0.45f), 0.3f, 4, HlGrass, HlGrassDark);
            return mb;
        }
    }
}
