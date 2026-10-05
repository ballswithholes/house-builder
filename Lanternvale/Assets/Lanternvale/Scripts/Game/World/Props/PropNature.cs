// Nature: oak, pine, birch, blighted dead tree, bushes, rocks, stump, log, glowing mushrooms.
// Trees: the trunk fits the (trunk-sized) collider, canopies overhang; foliage carries wind weights that grow with height
// (Sways = true). Canopies are faceted blobs coloured by normal (sun-lit tops, cool undersides); seed shifts the hue.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterNature(Dictionary<string, Recipe> r)
        {
            r["prop_tree_oak"] = (art, seed) => Swaying(art, seed, BuildOak, 0.6f);
            r["prop_tree_pine"] = (art, seed) => Swaying(art, seed, BuildPine, 0.5f);
            r["prop_tree_birch"] = (art, seed) => Swaying(art, seed, BuildBirch, 0.35f);
            r["prop_tree_dead"] = (art, seed) => Simple(art, seed, BuildDeadTree, 0.5f);
            r["prop_bush_a"] = (art, seed) => Swaying(art, seed, b => BuildBush(b, false), 0.6f);
            r["prop_bush_b"] = (art, seed) => Swaying(art, seed, b => BuildBush(b, true), 0.6f);
            r["prop_rock_large"] = (art, seed) => Simple(art, seed, BuildRockLarge, 1.0f);
            r["prop_rock_small"] = (art, seed) => Simple(art, seed, BuildRockSmall, 0.5f);
            r["prop_stump"] = (art, seed) => Simple(art, seed, BuildStump, 0.45f);
            r["prop_log"] = (art, seed) => Simple(art, seed, BuildLog, 1.0f);
            r["prop_mushrooms"] = Mushrooms;
        }

        static PropModel Swaying(string art, int seed, System.Func<int, MeshBuilder> build, float radius)
        {
            var m = Simple(art, seed, build, radius);
            m.Sways = true;
            return m;
        }

        /// <summary>Foliage palette per seed bucket: (top, side, bottom).</summary>
        static void Foliage(int b, Color baseCol, out Color top, out Color side, out Color bottom, float hueStep = 7f)
        {
            side = Paint.Hsv(baseCol, (b - 1.5f) * hueStep, 1f, 1f);
            top = Paint.Hsv(Color.Lerp(side, Paint.Hex("#DCE07C"), 0.26f), 0f, 0.95f, 1.08f);
            bottom = Paint.Hsv(Color.Lerp(side, Paint.Hex("#3C5A5A"), 0.45f), 0f, 1f, 0.85f);
        }

        /// <summary>Tapered faceted trunk (Lathe) squashed in depth so it fits a trunk collider.</summary>
        static void Trunk(MeshBuilder mb, Vector2[] profile, int sides, Color bark, float depthScale, Color[] rings = null, float angle = 0f)
        {
            mb.Color = bark;
            mb.Push().Scale(new Vector3(1f, 1f, depthScale));
            mb.Lathe(profile, sides, false, false, true, rings, angle);
            mb.Pop();
        }

        static void Roots(MeshBuilder mb, int count, float reach, float radius, float depthScale, float startDeg, Color bark)
        {
            mb.Color = Paint.Shade(bark, 0.92f);
            for (int i = 0; i < count; i++)
            {
                float a = (startDeg + i * 360f / count) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * depthScale);
                mb.Segment(dir * radius * 0.4f + Vector3.up * radius * 0.9f, dir * reach + Vector3.up * 0.02f, radius * 0.5f, radius * 0.12f, 5, false, true);
            }
        }

        // ------------------------------------------------------------------ oak (~8 m; trunk collider 1.2 × 0.7)

        static MeshBuilder BuildOak(int b)
        {
            var mb = Builder(VariantSeed("prop_tree_oak", b), 0.06f, 0.3f, 1.2f);
            mb.WindGradient = true; mb.WindY0 = 1.6f; mb.WindY1 = 8f; mb.Wind = 1.8f;
            var bark = Paint.Hex("#7A5E4A");
            Trunk(mb, new[] { new Vector2(0.44f, 0f), new Vector2(0.33f, 0.3f), new Vector2(0.28f, 1.2f), new Vector2(0.26f, 2.4f), new Vector2(0.22f, 3.3f) }, 7, bark, 0.78f);
            Roots(mb, 5, 0.58f, 0.2f, 0.55f, b * 23f, bark);
            mb.Color = bark;
            var fork = new Vector3(0f, 3.0f, 0f);
            Vector3[] tips = { new Vector3(-1.4f, 4.5f, 0.1f), new Vector3(1.4f, 4.6f, 0.3f), new Vector3(0.2f, 5.2f, -0.3f), new Vector3(-0.2f, 5.0f, 0.8f) };
            foreach (var t in tips) mb.Segment(fork, t, 0.2f, 0.08f, 5);
            Foliage(b, Paint.Hex("#6E9A4E"), out var top, out var side, out var bottom);
            var col = ByNormal(top, side, bottom, 0.25f);
            int seed = VariantSeed("oak_canopy", b);
            (Vector3 c, Vector3 r)[] blobs =
            {
                (new Vector3(0f, 5.5f, 0.2f), new Vector3(2.3f, 1.9f, 2.0f)),
                (new Vector3(-1.75f, 4.7f, 0.1f), new Vector3(1.5f, 1.3f, 1.4f)),
                (new Vector3(1.85f, 4.85f, 0.35f), new Vector3(1.5f, 1.35f, 1.4f)),
                (new Vector3(0.55f, 6.7f, 0.15f), new Vector3(1.55f, 1.35f, 1.45f)),
                (new Vector3(-0.95f, 6.35f, 0.55f), new Vector3(1.35f, 1.2f, 1.3f)),
                (new Vector3(0.35f, 4.5f, -1.05f), new Vector3(1.35f, 1.15f, 1.1f)),
                (new Vector3(-0.3f, 5.1f, 1.6f), new Vector3(1.5f, 1.3f, 1.2f)),
            };
            for (int i = 0; i < blobs.Length; i++)
            {
                var c = blobs[i].c + new Vector3((mb.Random01() - 0.5f) * 0.3f, (mb.Random01() - 0.5f) * 0.3f, 0f);
                FacetBlob(mb, c, blobs[i].r, 1, 0.16f, seed + i * 31, 0.15f, col);
            }
            return mb;
        }

        // ------------------------------------------------------------------ pine (~11 m; trunk collider 1.0 × 0.6)

        static MeshBuilder BuildPine(int b)
        {
            var mb = Builder(VariantSeed("prop_tree_pine", b), 0.06f, 0.3f, 1.0f);
            mb.WindGradient = true; mb.WindY0 = 1.5f; mb.WindY1 = 11f; mb.Wind = 1.6f;
            var bark = Paint.Hex("#6B4C3B");
            Trunk(mb, new[] { new Vector2(0.32f, 0f), new Vector2(0.23f, 0.4f), new Vector2(0.18f, 4f), new Vector2(0.1f, 9f), new Vector2(0.04f, 10.6f) }, 6, bark, 0.85f);
            Roots(mb, 4, 0.48f, 0.16f, 0.55f, 40f + b * 17f, bark);
            Foliage(b, Paint.Hex("#3F6B4A"), out var top, out var side, out var bottom, 6f);
            const int tiers = 6;
            for (int i = 0; i < tiers; i++)
            {
                float y0 = 2.55f + i * 1.36f, r = 2.3f - i * 0.33f + (i % 2) * 0.08f, h = 2.45f - i * 0.15f;
                mb.Push().Translate(0f, y0, 0f).Rotate((mb.Random01() - 0.5f) * 6f, 0f, (mb.Random01() - 0.5f) * 6f);
                mb.Color = side;
                mb.Lathe(new[] { new Vector2(r * 0.86f, -0.42f), new Vector2(r, -0.18f), new Vector2(r * 0.52f, h * 0.38f), new Vector2(0f, h) }, 8, false, true, false,
                         new[] { bottom, Color.Lerp(side, bottom, 0.3f), side, top }, i * 19f + b * 11f);
                mb.Pop();
            }
            mb.Color = top;
            mb.Cone(new Vector3(0f, 2.55f + tiers * 1.36f - 0.45f, 0f), 0.35f, 1.3f, 6);
            return mb;
        }

        // ------------------------------------------------------------------ birch (~7.5 m; trunk collider 0.7 × 0.4)

        static MeshBuilder BuildBirch(int b)
        {
            var mb = Builder(VariantSeed("prop_tree_birch", b), 0.05f, 0.25f, 1.0f);
            mb.WindGradient = true; mb.WindY0 = 1.2f; mb.WindY1 = 7.5f; mb.Wind = 2.2f;
            var white = Paint.Hex("#EDE8DC");
            var mark = Paint.Hex("#3E3836");
            BirchTrunk(mb, Vector3.zero, 0f, 0.17f, 6.3f, white, mark);
            if (b % 2 == 1) BirchTrunk(mb, new Vector3(0.12f, 0f, 0.05f), -11f, 0.1f, 4.8f, white, mark);
            mb.Color = Paint.Hex("#6E6560");
            foreach (var (from, to) in new[] { (new Vector3(0f, 3.6f, 0f), new Vector3(-1.1f, 5.0f, 0.2f)), (new Vector3(0f, 4.2f, 0f), new Vector3(1.0f, 5.6f, -0.1f)), (new Vector3(0f, 5.0f, 0f), new Vector3(-0.4f, 6.4f, -0.4f)) })
                mb.Segment(from, to, 0.06f, 0.025f, 4);
            Foliage(b, Paint.Hex("#8CB25A"), out var top, out var side, out var bottom, 9f);
            var col = ByNormal(top, side, bottom, 0.2f);
            int seed = VariantSeed("birch_canopy", b);
            (Vector3 c, float r)[] blobs =
            {
                (new Vector3(-1.1f, 5.2f, 0.2f), 0.95f), (new Vector3(1.0f, 5.7f, -0.1f), 1.0f), (new Vector3(-0.35f, 6.6f, -0.3f), 1.0f),
                (new Vector3(0.4f, 7.0f, 0.4f), 0.85f), (new Vector3(0.05f, 4.4f, 0.6f), 0.8f), (new Vector3(0.6f, 4.6f, -0.7f), 0.7f),
                (new Vector3(-0.8f, 4.1f, -0.5f), 0.6f),
            };
            for (int i = 0; i < blobs.Length; i++)
                FacetBlob(mb, blobs[i].c, new Vector3(blobs[i].r * 1.1f, blobs[i].r * 0.85f, blobs[i].r), 1, 0.22f, seed + i * 13, 0.2f, col);
            return mb;
        }

        static void BirchTrunk(MeshBuilder mb, Vector3 at, float leanDeg, float r0, float height, Color white, Color mark)
        {
            var prof = new List<Vector2>();
            var cols = new List<Color>();
            void P(float r, float y, Color c) { prof.Add(new Vector2(r, y)); cols.Add(c); }
            float R(float y) => Mathf.Lerp(r0, r0 * 0.4f, y / height);
            P(r0 * 1.15f, 0f, mark);
            P(r0, 0.35f, white);
            float[] marks = { 0.9f, 1.6f, 2.5f, 3.2f, 4.1f, 5.0f };
            foreach (float m in marks)
            {
                if (m > height - 0.4f) break;
                float h = 0.05f + (m * 7.3f % 1f) * 0.06f;
                P(R(m), m, white); P(R(m), m, mark); P(R(m + h), m + h, mark); P(R(m + h), m + h, white);
            }
            P(R(height) * 0.6f, height, white);
            mb.Color = white;
            mb.Push().Translate(at).Rotate(0f, 0f, leanDeg).Scale(new Vector3(1f, 1f, 0.9f));
            mb.Lathe(prof, 6, false, false, true, cols);
            mb.Pop();
        }

        // ------------------------------------------------------------------ dead tree (blighted; collider 1.0 × 0.6)

        static MeshBuilder BuildDeadTree(int b)
        {
            var mb = Builder(VariantSeed("prop_tree_dead", b), 0.07f, 0.3f, 1.0f);
            var bark = Pal.Blight;
            mb.Push().Rotate(0f, b * 40f, (b - 1.5f) * 3f);
            Trunk(mb, new[] { new Vector2(0.4f, 0f), new Vector2(0.28f, 0.4f), new Vector2(0.22f, 2.2f), new Vector2(0.17f, 3.3f) }, 6, bark, 0.75f);
            Roots(mb, 5, 0.52f, 0.2f, 0.55f, 10f, bark);
            // gnarled limbs: three main branches, each forking into twigs
            mb.Color = bark;
            var trunkTop = new Vector3(0f, 3.2f, 0f);
            Vector3[] dirs = { new Vector3(-0.8f, 0.9f, 0.2f), new Vector3(0.9f, 0.8f, -0.1f), new Vector3(0.1f, 1f, 0.5f) };
            for (int i = 0; i < dirs.Length; i++)
            {
                var d = dirs[i].normalized;
                float len = 1.3f + (i == 2 ? 0.4f : 0f);
                var mid = trunkTop + d * len + new Vector3(0f, 0f, (mb.Random01() - 0.5f) * 0.3f);
                mb.Segment(trunkTop - d * 0.1f, mid, 0.15f, 0.08f, 5);
                for (int k = -1; k <= 1; k += 2)
                {
                    var tw = (d + new Vector3(k * 0.6f, 0.35f, (mb.Random01() - 0.5f) * 0.5f)).normalized;
                    var end = mid + tw * (0.8f + mb.Random01() * 0.5f);
                    mb.Segment(mid, end, 0.07f, 0.02f, 4);
                    var bend = end + (tw + new Vector3(-k * 0.5f, 0.5f, 0f)).normalized * 0.4f;
                    mb.Segment(end, bend, 0.025f, 0.008f, 3, false, false);
                }
            }
            // a low side limb
            mb.Segment(new Vector3(0f, 1.9f, 0f), new Vector3(0.95f, 2.5f, -0.2f), 0.1f, 0.03f, 4);
            // violet veins and root crystals (faint glow)
            mb.Color = Pal.Violet;
            mb.Emission = 0.7f;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 30f) * Mathf.Deg2Rad;
                var p0 = new Vector3(Mathf.Cos(a) * 0.3f, 0.25f, Mathf.Sin(a) * 0.22f);
                var p1 = new Vector3(Mathf.Cos(a + 0.3f) * 0.24f, 1.0f + i * 0.35f, Mathf.Sin(a + 0.3f) * 0.18f);
                mb.Segment(p0, p1, 0.025f, 0.012f, 3, false, true);
            }
            mb.Pop();
            mb.Emission = 0.55f;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 100f + b * 50f + 15f) * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(a) * 0.36f, 0f, Mathf.Sin(a) * 0.18f + 0.03f);
                mb.Color = i % 2 == 0 ? Pal.Violet : Paint.Hsv(Pal.Violet, -12f, 1.1f, 0.9f);
                mb.Push().Translate(at).Rotate(Mathf.Sin(a) * 20f, 0f, -Mathf.Cos(a) * 20f);
                float h = 0.28f + (i % 3) * 0.1f;
                mb.Lathe(new[] { new Vector2(0.07f, 0f), new Vector2(0.07f, h * 0.7f), new Vector2(0f, h) }, 5, false, true, false);
                mb.Pop();
            }
            mb.Emission = 0f;
            return mb;
        }

        // ------------------------------------------------------------------ bushes (collider 1.2 × 0.6)

        static MeshBuilder BuildBush(int b, bool flowering)
        {
            var mb = Builder(VariantSeed(flowering ? "prop_bush_b" : "prop_bush_a", b), 0.06f, 0.35f, 0.6f);
            mb.WindGradient = true; mb.WindY0 = 0.2f; mb.WindY1 = 1.4f; mb.Wind = 1.1f;
            Foliage(b, flowering ? Paint.Hex("#5F8F4A") : Paint.Hex("#729E4E"), out var top, out var side, out var bottom);
            var col = ByNormal(top, side, bottom, 0.25f);
            int seed = VariantSeed(flowering ? "bushb" : "busha", b);
            (Vector3 c, Vector3 r)[] blobs =
            {
                (new Vector3(0f, 0.62f, 0.25f), new Vector3(0.68f, 0.6f, 0.5f)),
                (new Vector3(-0.38f, 0.45f, 0.3f), new Vector3(0.38f, 0.4f, 0.36f)),
                (new Vector3(0.4f, 0.48f, 0.32f), new Vector3(0.38f, 0.42f, 0.36f)),
                (new Vector3(0.1f, 0.98f, 0.28f), new Vector3(0.42f, 0.38f, 0.38f)),
            };
            for (int i = 0; i < blobs.Length; i++)
                FacetBlob(mb, blobs[i].c, blobs[i].r, 1, 0.18f, seed + i * 7, 0.45f, col);
            if (flowering)
            {
                var petals = new[] { Paint.Hex("#F6A9C0"), Paint.Hex("#FFD3E0"), Paint.Hex("#F07FA0") };
                for (int i = 0; i < 16; i++)
                {
                    var bl = blobs[i % blobs.Length];
                    float a = mb.Random01() * Mathf.PI * 2f, e = Mathf.Lerp(0.15f, 1.2f, mb.Random01());
                    var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
                    if (dir.z > 0.5f) dir.z = -dir.z;
                    mb.Color = petals[i % petals.Length];
                    Gem(mb, bl.c + Vector3.Scale(dir, bl.r) * 1.02f, 0.07f);
                }
            }
            return mb;
        }

        // ------------------------------------------------------------------ rocks, stump, log

        static MeshBuilder BuildRockLarge(int b)
        {
            var mb = Builder(VariantSeed("prop_rock_large", b), 0.07f, 0.35f, 0.6f);
            var stone = Paint.Hsv(Pal.Stone, (b - 1.5f) * 6f, 0.9f);
            int seed = VariantSeed("rockL", b);
            FacetBlob(mb, new Vector3(0f, 0.92f, 0.22f), new Vector3(0.98f, 1.05f, 0.72f), 1, 0.2f, seed, 0.3f, Mossy(stone, Pal.Moss, 0.5f));
            FacetBlob(mb, new Vector3(0.55f, 0.36f, 0.12f), new Vector3(0.4f, 0.38f, 0.3f), 1, 0.2f, seed + 5, 0.35f, Mossy(Paint.Shade(stone, 1.05f), Pal.MossLight, 0.55f));
            mb.Color = Paint.Shade(stone, 0.6f);
            Beam(mb, new Vector3(-0.35f, 1.55f, -0.38f), new Vector3(-0.15f, 0.95f, -0.52f), 0.035f, 0.03f, Vector3.back);
            Beam(mb, new Vector3(-0.15f, 0.95f, -0.52f), new Vector3(-0.3f, 0.5f, -0.48f), 0.03f, 0.03f, Vector3.back);
            for (int i = 0; i < 4; i++)
            {
                mb.Color = mb.Random01() < 0.5f ? Pal.StoneLight : stone;
                Gem(mb, new Vector3(-0.85f + i * 0.5f + mb.Random01() * 0.2f, 0.04f, -0.22f + mb.Random01() * 0.2f), new Vector3(0.1f, 0.07f, 0.08f));
            }
            Tuft(mb, new Vector3(-0.7f, 0f, -0.2f), 0.35f, 5, Pal.Leaf);
            return mb;
        }

        static MeshBuilder BuildRockSmall(int b)
        {
            var mb = Builder(VariantSeed("prop_rock_small", b), 0.07f, 0.35f, 0.4f);
            var stone = Paint.Hsv(Pal.Stone, (b - 1.5f) * 6f, 0.9f);
            int seed = VariantSeed("rockS", b);
            FacetBlob(mb, new Vector3(0f, 0.32f, 0.05f), new Vector3(0.48f, 0.42f, 0.4f), 1, 0.2f, seed, 0.35f, Mossy(stone, Pal.Moss, 0.5f));
            FacetBlob(mb, new Vector3(0.52f, 0.18f, -0.05f), new Vector3(0.3f, 0.24f, 0.26f), 0, 0.2f, seed + 3, 0.3f, Mossy(Paint.Shade(stone, 1.06f), Pal.MossLight, 0.55f));
            FacetBlob(mb, new Vector3(-0.48f, 0.15f, 0.1f), new Vector3(0.26f, 0.2f, 0.24f), 0, 0.2f, seed + 9, 0.3f, Mossy(Paint.Shade(stone, 0.95f), Pal.Moss, 0.55f));
            Tuft(mb, new Vector3(0.25f, 0f, -0.3f), 0.28f, 4, Pal.Leaf);
            return mb;
        }

        static void LittleMushroom(MeshBuilder mb, Vector3 at, float h, float capR, Color cap, float tiltDeg, float yawDeg, float glow = 0f)
        {
            var keepC = mb.Color;
            float keepE = mb.Emission;
            mb.Push().Translate(at).Rotate(0f, yawDeg, tiltDeg);
            mb.Color = Pal.Cream;
            mb.Cylinder(Vector3.zero, capR * 0.32f, capR * 0.26f, h, 5);
            mb.Emission = glow;
            mb.Color = cap;
            mb.Lathe(new[] { new Vector2(capR, h * 0.85f), new Vector2(capR * 0.9f, h * 0.85f + capR * 0.35f), new Vector2(capR * 0.45f, h * 0.85f + capR * 0.7f), new Vector2(0f, h * 0.85f + capR * 0.78f) }, 7, false, true, false,
                     new[] { Paint.Shade(cap, 0.9f), cap, Paint.Shade(cap, 1.08f), Paint.Shade(cap, 1.12f) });
            mb.Pop();
            mb.Color = keepC;
            mb.Emission = keepE;
        }

        static MeshBuilder BuildStump(int b)
        {
            var mb = Builder(VariantSeed("prop_stump", b), 0.06f, 0.3f, 0.4f);
            var bark = Paint.Hex("#6E5442");
            var wood = Paint.Hex("#D9B98A");
            var ring = Paint.Hex("#B8925E");
            mb.Push().Translate(0f, 0f, 0.08f);
            mb.Color = bark;
            mb.Lathe(new[]
            {
                new Vector2(0.4f, 0f), new Vector2(0.33f, 0.16f), new Vector2(0.3f, 0.5f), new Vector2(0.31f, 0.6f), new Vector2(0.31f, 0.6f),
                new Vector2(0.24f, 0.6f), new Vector2(0.24f, 0.6f), new Vector2(0.15f, 0.6f), new Vector2(0.15f, 0.6f), new Vector2(0.07f, 0.6f), new Vector2(0.07f, 0.6f), new Vector2(0f, 0.6f),
            }, 8, false, false, false, new[] { bark, bark, bark, bark, wood, wood, ring, ring, wood, wood, ring, ring }, b * 20f);
            Roots(mb, 3, 0.48f, 0.14f, 0.5f, 30f + b * 15f, bark);
            mb.Pop();
            FacetBlob(mb, new Vector3(-0.22f, 0.3f, -0.12f), new Vector3(0.14f, 0.2f, 0.08f), 0, 0.2f, b + 2, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.LeafDark));
            FacetBlob(mb, new Vector3(0.05f, 0.62f, 0.15f), new Vector3(0.16f, 0.04f, 0.12f), 0, 0.2f, b + 4, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            var orange = Paint.Hex("#E8863A");
            LittleMushroom(mb, new Vector3(0.2f, 0.02f, -0.12f), 0.1f, 0.07f, orange, -12f, 0f);
            LittleMushroom(mb, new Vector3(0.28f, 0.02f, -0.02f), 0.14f, 0.09f, orange, -18f, 30f);
            LittleMushroom(mb, new Vector3(0.12f, 0.02f, -0.18f), 0.07f, 0.05f, orange, 8f, 60f);
            return mb;
        }

        static MeshBuilder BuildLog(int b)
        {
            var mb = Builder(VariantSeed("prop_log", b), 0.06f, 0.3f, 0.4f);
            var bark = Paint.Hex("#6E5442");
            var wood = Paint.Hex("#D9B98A");
            var ring = Paint.Hex("#B8925E");
            const float r = 0.27f;
            mb.Push().Translate(-1.0f, r, 0.02f).Rotate(0f, 0f, -90f);
            mb.Color = bark;
            mb.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.09f, 0f), new Vector2(0.09f, 0f), new Vector2(0.18f, 0f), new Vector2(0.18f, 0f), new Vector2(r - 0.02f, 0f), new Vector2(r - 0.02f, 0f),
                new Vector2(r, 0.03f), new Vector2(r * 0.97f, 1.0f), new Vector2(r * 0.85f, 1.85f), new Vector2(r * 0.6f, 2.02f), new Vector2(0f, 2.08f),
            }, 8, false, false, false, new[] { ring, ring, wood, wood, ring, ring, bark, bark, bark, bark, Paint.Shade(bark, 0.8f), Paint.Shade(wood, 0.8f) }, 22.5f);
            mb.Pop();
            // moss along the top, a knot branch, a fern-ish sprig and mushrooms
            for (int i = 0; i < 4; i++)
                FacetBlob(mb, new Vector3(-0.65f + i * 0.45f, r * 1.85f, 0.04f), new Vector3(0.26f, 0.06f, 0.16f), 0, 0.25f, b * 3 + i, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            mb.Color = bark;
            mb.Segment(new Vector3(0.3f, r * 1.4f, 0.1f), new Vector3(0.45f, r * 2.3f, 0.25f), 0.06f, 0.025f, 4);
            mb.Color = Pal.Leaf;
            for (int i = 0; i < 4; i++)
                Gem(mb, new Vector3(0.75f + i * 0.08f, 0.15f + i * 0.08f, -0.22f), new Vector3(0.1f, 0.03f, 0.06f));
            var cap = b % 2 == 0 ? Paint.Hex("#E8863A") : Paint.Hex("#E3C08A");
            LittleMushroom(mb, new Vector3(-0.3f, 0.02f, -0.24f), 0.12f, 0.08f, cap, 10f, 0f);
            LittleMushroom(mb, new Vector3(-0.42f, 0.02f, -0.2f), 0.08f, 0.06f, cap, -6f, 20f);
            return mb;
        }

        // ------------------------------------------------------------------ glowing mushrooms (Whisperwood; no collider)

        static PropModel Mushrooms(string art, int seed)
        {
            var m = Simple(art, seed, BuildMushrooms, 0.45f);
            m.LightAnchors.Add(new Vector3(0f, 0.3f, 0f));
            return m;
        }

        static MeshBuilder BuildMushrooms(int b)
        {
            var mb = Builder(VariantSeed("prop_mushrooms", b), 0.05f, 0.2f, 0.3f);
            var teal = Paint.Hsv(Paint.Hex("#58D2B8"), (b - 1.5f) * 8f);
            (Vector3 p, float h, float r, float tilt)[] caps =
            {
                (new Vector3(0f, 0f, 0.05f), 0.55f, 0.2f, 4f), (new Vector3(-0.26f, 0f, -0.05f), 0.38f, 0.15f, 14f), (new Vector3(0.22f, 0f, -0.1f), 0.32f, 0.13f, -12f),
                (new Vector3(0.12f, 0f, 0.25f), 0.24f, 0.1f, -8f), (new Vector3(-0.12f, 0f, -0.28f), 0.16f, 0.08f, 10f), (new Vector3(0.36f, 0f, 0.12f), 0.14f, 0.07f, -20f),
            };
            for (int i = 0; i < caps.Length; i++)
            {
                var c = caps[i];
                LittleMushroom(mb, c.p, c.h, c.r, Paint.Shade(teal, 0.95f + 0.1f * (i % 2)), c.tilt, i * 47f + b * 13f, 0.6f);
                // pale spots on the larger caps
                if (c.r < 0.12f) continue;
                mb.Emission = 0.35f;
                mb.Color = Paint.Hex("#E8FFF6");
                float yTop = c.h * 0.85f + c.r * 0.55f;
                for (int k = 0; k < 3; k++)
                {
                    float a = k * Mathf.PI * 0.667f + i;
                    var local = new Vector3(Mathf.Cos(a) * c.r * 0.55f, yTop, Mathf.Sin(a) * c.r * 0.55f);
                    var rot = Quaternion.Euler(0f, i * 47f + b * 13f, c.tilt);
                    Gem(mb, c.p + rot * local, new Vector3(0.025f, 0.012f, 0.025f) * (c.r / 0.15f));
                }
                mb.Emission = 0f;
            }
            Tuft(mb, new Vector3(-0.3f, 0f, 0.15f), 0.2f, 4, Pal.Moss);
            return mb;
        }
    }
}
