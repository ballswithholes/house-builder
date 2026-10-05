// Foreground clumps along the front edge of a map: ferns, grass, mossy stones, wildflowers. Lush, ~2 m wide, no ink
// outline (Skin.Plain); blades are double-sided and carry wind weights that grow towards their tips.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterForeground(Dictionary<string, Recipe> r)
        {
            r["fg_ferns"] = (art, seed) => Foreground(art, seed, BuildFerns, 1.1f);
            r["fg_grass_a"] = (art, seed) => Foreground(art, seed, b => BuildGrass(b, false), 1.1f);
            r["fg_grass_b"] = (art, seed) => Foreground(art, seed, b => BuildGrass(b, true), 1.1f);
            r["fg_stones_a"] = (art, seed) => Foreground(art, seed, b => BuildFgStones(b, false), 0.9f);
            r["fg_stones_b"] = (art, seed) => Foreground(art, seed, b => BuildFgStones(b, true), 1.0f);
            r["fg_flowers_a"] = (art, seed) => Foreground(art, seed, b => BuildFlowers(b, false), 1.1f);
            r["fg_flowers_b"] = (art, seed) => Foreground(art, seed, b => BuildFlowers(b, true), 1.1f);
        }

        static PropModel Foreground(string art, int seed, System.Func<int, MeshBuilder> build, float radius)
        {
            var m = Simple(art, seed, build, radius, Skin.Plain);
            m.Sways = true;
            return m;
        }

        static MeshBuilder FgBuilder(string art, int b, float height) =>
            new MeshBuilder(VariantSeed(art, b)) { Jitter = 0.07f, AOStrength = 0.32f, AOHeight = height * 0.35f, WindGradient = true, WindY0 = 0f, WindY1 = height, Wind = 1.6f };

        /// <summary>A grass tuft: blades fanning out from `at`, leaning towards `lean` (≤ 60 tris for 10 blades).</summary>
        static void GrassTuft(MeshBuilder mb, Vector3 at, float height, int blades, Color baseCol, Color tipCol, Vector3 lean)
        {
            var keepC = mb.Color;
            for (int i = 0; i < blades; i++)
            {
                float a = (i + mb.Random01() * 0.6f) * Mathf.PI * 2f / blades;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.7f);
                float h = height * (0.55f + 0.45f * mb.Random01());
                var tip = at + dir * (h * 0.38f) + lean * h + Vector3.up * h;
                mb.Color = Color.Lerp(baseCol, tipCol, 0.35f + 0.65f * mb.Random01());
                mb.Blade(at + dir * 0.04f, tip, 0.09f + 0.05f * mb.Random01(), new Vector3(-dir.z, 0f, dir.x));
            }
            mb.Color = keepC;
        }

        // ------------------------------------------------------------------ grass (a: soft tufts, b: taller wilder clumps leaning)

        static MeshBuilder BuildGrass(int b, bool wild)
        {
            float height = wild ? 1.15f : 0.85f;
            var mb = FgBuilder(wild ? "fg_grass_b" : "fg_grass_a", b, height);
            var baseCol = Paint.Hsv(Pal.Leaf, (b - 1.5f) * 5f);
            var tipCol = Paint.Hsv(wild ? Paint.Hex("#C4D27C") : Paint.Hex("#B4DA78"), (b - 1.5f) * 5f);
            int tufts = wild ? 9 : 11;
            var lean = wild ? new Vector3(0.22f, 0f, -0.05f) : Vector3.zero;
            for (int i = 0; i < tufts; i++)
            {
                float x = -1.05f + 2.1f * (i + 0.5f) / tufts + (mb.Random01() - 0.5f) * 0.18f;
                float z = (mb.Random01() - 0.5f) * 0.55f;
                float h = height * (0.6f + 0.4f * Mathf.Sin((i + b) * 1.7f) * Mathf.Sin((i + b) * 1.7f));
                GrassTuft(mb, new Vector3(x, 0f, z), h, 9, baseCol, tipCol, lean * (0.6f + mb.Random01() * 0.6f));
            }
            if (wild)
            {
                // seed heads on thin stalks
                for (int i = 0; i < 5; i++)
                {
                    var foot = new Vector3(-0.9f + i * 0.45f, 0f, (mb.Random01() - 0.5f) * 0.3f);
                    var top = foot + new Vector3(0.25f, height * (0.95f + mb.Random01() * 0.2f), 0f);
                    mb.Color = Paint.Hex("#C9B878");
                    mb.Blade(foot, top, 0.025f, Vector3.right);
                    mb.Color = Paint.Hex("#E2D49A");
                    Gem(mb, top + new Vector3(0.02f, 0.04f, 0f), new Vector3(0.035f, 0.08f, 0.035f));
                }
            }
            return mb;
        }

        // ------------------------------------------------------------------ ferns (lush arching fronds, 1.3 m)

        static MeshBuilder BuildFerns(int b)
        {
            const float height = 1.3f;
            var mb = FgBuilder("fg_ferns", b, height);
            var dark = Paint.Hsv(Paint.Hex("#4C8A4E"), (b - 1.5f) * 5f);
            var light = Paint.Hsv(Paint.Hex("#9CCB66"), (b - 1.5f) * 5f);
            Vector3[] centres = { new Vector3(-0.42f, 0f, 0.05f), new Vector3(0.45f, 0f, -0.05f) };
            for (int c = 0; c < centres.Length; c++)
            {
                int fronds = 9;
                for (int f = 0; f < fronds; f++)
                {
                    float a = (f * 360f / fronds + c * 20f + b * 11f) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.7f).normalized;
                    float len = 0.75f + 0.3f * Mathf.Abs(Mathf.Sin(f * 2.1f + c));
                    float rise = height * (0.85f + 0.25f * Mathf.Abs(Mathf.Cos(f * 1.3f + b)));
                    Frond(mb, centres[c], dir, len, rise, dark, light);
                }
            }
            return mb;
        }

        /// <summary>Arching fern frond: a curved midrib with leaflet pairs shrinking towards the drooping tip.</summary>
        static void Frond(MeshBuilder mb, Vector3 root, Vector3 dir, float len, float rise, Color dark, Color light)
        {
            const int n = 7;
            Vector3 P(float t) => root + dir * (len * t) + Vector3.up * (rise * (2.2f * t - 1.5f * t * t));
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            for (int i = 0; i < n; i++)
            {
                float t0 = (float)i / n, t1 = (float)(i + 1) / n;
                var p0 = P(t0);
                var p1 = P(t1);
                mb.Color = Color.Lerp(dark, light, t0);
                mb.Blade(p0, p1, 0.05f * (1f - t0 * 0.6f), side);
                if (i == 0) continue;
                float leaf = 0.3f * (1f - t0 * 0.7f);
                var along = (p1 - p0).normalized;
                for (int s = -1; s <= 1; s += 2)
                {
                    var tip = p0 + side * (s * leaf) + along * (leaf * 0.55f) + Vector3.down * (leaf * 0.2f);
                    mb.Color = Color.Lerp(dark, light, t0 * 0.7f + 0.3f * mb.Random01());
                    mb.Blade(p0, tip, leaf * 0.85f, along);
                }
            }
        }

        // ------------------------------------------------------------------ mossy stones with grass

        static MeshBuilder BuildFgStones(int b, bool boulder)
        {
            var mb = FgBuilder(boulder ? "fg_stones_b" : "fg_stones_a", b, 0.6f);
            var stone = Paint.Hsv(Pal.Stone, (b - 1.5f) * 6f, 0.9f);
            int seed = VariantSeed(boulder ? "fgsb" : "fgsa", b);
            float keepW = mb.Wind;
            mb.Wind = 0f;
            if (boulder)
            {
                FacetBlob(mb, new Vector3(0f, 0.42f, 0.05f), new Vector3(0.62f, 0.55f, 0.5f), 1, 0.2f, seed, 0.3f, Mossy(stone, Pal.Moss, 0.45f));
                FacetBlob(mb, new Vector3(-0.72f, 0.18f, 0.0f), new Vector3(0.3f, 0.24f, 0.28f), 1, 0.2f, seed + 3, 0.3f, Mossy(Paint.Shade(stone, 1.05f), Pal.MossLight, 0.5f));
                FacetBlob(mb, new Vector3(0.7f, 0.15f, -0.05f), new Vector3(0.26f, 0.2f, 0.24f), 0, 0.2f, seed + 7, 0.3f, Mossy(Paint.Shade(stone, 0.95f), Pal.Moss, 0.5f));
            }
            else
            {
                (Vector3 c, Vector3 r)[] rocks =
                {
                    (new Vector3(-0.55f, 0.22f, 0.05f), new Vector3(0.36f, 0.3f, 0.32f)), (new Vector3(0.05f, 0.3f, -0.05f), new Vector3(0.42f, 0.38f, 0.36f)),
                    (new Vector3(0.6f, 0.16f, 0.1f), new Vector3(0.28f, 0.22f, 0.26f)), (new Vector3(-0.15f, 0.1f, -0.38f), new Vector3(0.18f, 0.13f, 0.16f)),
                };
                for (int i = 0; i < rocks.Length; i++)
                    FacetBlob(mb, rocks[i].c, rocks[i].r, i < 3 ? 1 : 0, 0.2f, seed + i * 5, 0.3f, Mossy(Paint.Shade(stone, 0.95f + 0.05f * i), i % 2 == 0 ? Pal.Moss : Pal.MossLight, 0.5f));
            }
            mb.Wind = keepW;
            var baseCol = Pal.LeafDark;
            var tipCol = Paint.Hex("#A6CC6E");
            float[] xs = boulder ? new[] { -0.95f, -0.4f, 0.35f, 0.95f } : new[] { -0.9f, -0.3f, 0.35f, 0.85f };
            foreach (float x in xs)
                GrassTuft(mb, new Vector3(x, 0f, -0.25f + mb.Random01() * 0.15f), 0.42f + mb.Random01() * 0.2f, 7, baseCol, tipCol, Vector3.zero);
            return mb;
        }

        // ------------------------------------------------------------------ wildflowers among grass

        static MeshBuilder BuildFlowers(int b, bool second)
        {
            const float height = 0.9f;
            var mb = FgBuilder(second ? "fg_flowers_b" : "fg_flowers_a", b, height);
            var baseCol = Paint.Hsv(Pal.Leaf, (b - 1.5f) * 4f);
            var tipCol = Paint.Hex("#B4DA78");
            for (int i = 0; i < 8; i++)
            {
                float x = -1.0f + i * 0.29f + (mb.Random01() - 0.5f) * 0.12f;
                GrassTuft(mb, new Vector3(x, 0f, (mb.Random01() - 0.5f) * 0.45f), 0.5f + mb.Random01() * 0.2f, 8, baseCol, tipCol, Vector3.zero);
            }
            Color[] petalsA = { Color.white, Paint.Hex("#FFD84E"), Paint.Hex("#F59AB8") };
            Color[] petalsB = { Paint.Hex("#9C7BE0"), Paint.Hex("#E2443A"), Paint.Hex("#FFC93E") };
            var petals = second ? petalsB : petalsA;
            for (int i = 0; i < 18; i++)
            {
                float x = -1.0f + 2.0f * (i + 0.5f) / 18f + (mb.Random01() - 0.5f) * 0.1f;
                float z = (mb.Random01() - 0.5f) * 0.55f;
                float h = 0.4f + mb.Random01() * 0.45f;
                var foot = new Vector3(x, 0f, z);
                var head = foot + new Vector3((mb.Random01() - 0.5f) * 0.12f, h, 0f);
                mb.Color = Pal.Leaf;
                mb.Blade(foot, head, 0.025f, Vector3.right);
                var kind = i % 3;
                var col = petals[kind];
                if (second && kind == 0)
                {
                    // bellflower: a little violet bell hanging from the stalk tip
                    mb.Color = col;
                    mb.Push().Translate(head + new Vector3(0.04f, -0.02f, 0f)).Rotate(0f, 0f, 160f);
                    mb.Lathe(new[] { new Vector2(0.02f, 0f), new Vector2(0.065f, 0.08f), new Vector2(0.08f, 0.14f) }, 5, false, true, false);
                    mb.Pop();
                    continue;
                }
                mb.Color = col;
                Gem(mb, head, new Vector3(0.1f, 0.03f, 0.1f));
                mb.Color = second && kind == 1 ? Paint.Hex("#2E2430") : Paint.Hex("#F2B02E");
                Gem(mb, head + Vector3.up * 0.025f, 0.035f);
            }
            return mb;
        }
    }
}
