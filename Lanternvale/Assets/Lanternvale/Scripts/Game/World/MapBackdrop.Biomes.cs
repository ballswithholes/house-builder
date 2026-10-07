// MapBackdrop: the default layers of a map without authored ones, and the scenery of the expansion's outdoor biomes
// (MapDef.biome), built behind and beside the play area like the rest of the backdrop (Y-up local space under the
// backdrop root: local x = world x, local y = height, local z = world depth y; everything sits on terrain.Height).
//
//   highlands  rolling golden downs swelling up to the mountains, windmills turning on the crests, rings of standing
//              stones and cairns on the hilltops, gold-leaved clumps and haystacks
//   fen        rows of bare, crooked dead trees hung with moss, reed banks with cattails along the water table, low
//              grey-green woods far off in the mist
//   peaks      a near ridge of snowy peaks towering over the pass, snow-laden pines on the slopes and at the sides
//   roost      the summit's crown of jagged dark spires behind and around the plateau (ember-lit cracks at their feet),
//              a sea of cloud far below and other summits rising out of it
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    internal sealed partial class MapBackdrop
    {
        // ================================================================== defaults

        static ParallaxLayerDef Layer(string art, string tint = "", float scroll = 0f) =>
            new ParallaxLayerDef { art = art, tint = tint ?? "", scrollSpeed = scroll };

        /// <summary>The backdrop layers a map gets when it authors none: clouds and far mountains in its biome's mood.</summary>
        internal static List<ParallaxLayerDef> DefaultLayers(string biome)
        {
            var l = new List<ParallaxLayerDef>();
            switch (biome ?? "")
            {
                case "highlands":
                    l.Add(Layer("bg_clouds", "", 0.12f));
                    l.Add(Layer("bg_mountains_far", "#e6d4c2"));
                    l.Add(Layer("bg_hills_far"));
                    break;
                case "fen":
                    l.Add(Layer("bg_clouds", "#c6d0c8", 0.05f));
                    l.Add(Layer("bg_mountains_far", "#94a4a6"));
                    break;
                case "peaks":
                    l.Add(Layer("bg_clouds", "#eef2fa", 0.1f));
                    l.Add(Layer("bg_mountains_far", "#dfe6f4"));
                    break;
                case "roost":
                    l.Add(Layer("bg_clouds", "#f0c4b0", 0.05f));
                    break;
                case "village":
                    l.Add(Layer("bg_clouds", "", 0.15f));
                    l.Add(Layer("bg_mountains_far"));
                    l.Add(Layer("bg_hills_far"));
                    l.Add(Layer("bg_village_far"));
                    break;
                case "forest":
                    l.Add(Layer("bg_clouds", "", 0.1f));
                    l.Add(Layer("bg_mountains_far"));
                    l.Add(Layer("bg_forest_far"));
                    break;
                case "meadow":
                case "shrine":
                case "":
                    l.Add(Layer("bg_clouds", "", 0.12f));
                    l.Add(Layer("bg_mountains_far"));
                    break;
                default:
                    // indoor biomes: nothing (Build returns early anyway)
                    break;
            }
            return l;
        }

        /// <summary>The biome's own scenery (new outdoor biomes only; the first slice's maps are untouched).</summary>
        void BuildBiome()
        {
            switch (def.biome ?? "")
            {
                case "highlands": BuildHighlands(); break;
                case "fen": BuildFen(); break;
                case "peaks": BuildPeaks(); break;
                case "roost": BuildRoost(); break;
            }
        }

        /// <summary>Calls build for points spread along rows behind the map (z = D + rows[i]) and, with sides, down both
        /// flanks (|x| beyond the map by sideMin…sideMax, z from front to D), skipping the exits' roads.</summary>
        void Scatter(float[] rows, float spanX, float step, float jitterZ, float clear, System.Action<float, float, int> place,
                     float sideMin = 0f, float sideMax = 0f, float sideStep = 0f, float front = -24f)
        {
            for (int ri = 0; ri < rows.Length; ri++)
            {
                float z0 = D + rows[ri];
                for (float x = -spanX + Range(0f, step); x < W + spanX; x += step * Range(0.7f, 1.3f))
                {
                    float z = z0 + Range(-jitterZ, jitterZ);
                    if (terrain.Reserved(x, z, clear)) continue;
                    place(x, z, ri);
                }
            }
            if (sideStep <= 0f) return;
            for (int s = -1; s <= 1; s += 2)
                for (float z = front + Range(0f, sideStep); z < D + Mathf.Max(0f, rows.Length > 0 ? rows[0] : 0f); z += sideStep * Range(0.7f, 1.3f))
                {
                    float o = Range(sideMin, sideMax);
                    float x = s < 0 ? -o : W + o;
                    if (terrain.Reserved(x, z, clear + 1f)) continue;
                    place(x, z, -1);
                }
        }

        // ================================================================== highlands

        static readonly Color[] DownsGold = { Paint.Hex("#cdb462"), Paint.Hex("#bfae5c"), Paint.Hex("#d8be6c"), Paint.Hex("#aaa35a"), Paint.Hex("#c3a85a") };
        static readonly Color[] AutumnLeaf = { Paint.Hex("#c9a24a"), Paint.Hex("#b8913f"), Paint.Hex("#9fa552"), Paint.Hex("#d6b25a") };
        static readonly Color MenhirStone = Paint.Hex("#a7a197"), MenhirDark = Paint.Hex("#8a847c"), Lichen = Paint.Hex("#a5b06a");

        void BuildHighlands()
        {
            // far downs: long low swells in rows between the terrain's hills and the mountains, gold fading to sage
            var downs = new MeshBuilder(rng.Next()) { Jitter = 0.04f };
            float[] downRows = { 95f, 135f, 180f };
            for (int row = 0; row < downRows.Length; row++)
            {
                for (float x = -280f + Range(0f, 40f); x < W + 280f; x += Range(55f, 85f))
                {
                    float z = D + downRows[row] + Range(-12f, 12f);
                    float r = Range(42f, 70f) * (1f + row * 0.25f);
                    float h = Range(9f, 17f) * (1f + row * 0.45f);
                    var col = Color.Lerp(DownsGold[rng.Next(DownsGold.Length)], Paint.Hex("#a9b58a"), row * 0.25f);
                    downs.Color = Paint.Shade(col, Range(0.92f, 1.06f));
                    downs.Blob(Ground(x, z, -h * 0.25f), new Vector3(r, h, r * Range(0.45f, 0.65f)), 1, 0.08f, rng.Next(1000), 0.85f);
                }
            }
            Emit(downs, "Highland Downs", 0.8f);

            // windmills on the crests behind the map (and one off each side), their sails turning
            var mills = new MeshBuilder(rng.Next()) { Jitter = 0.05f };
            int millCount = Mathf.Clamp(Mathf.RoundToInt(W / 40f), 2, 4);
            for (int i = 0; i < millCount; i++)
            {
                float x = Mathf.Lerp(W * 0.12f, W * 0.88f, millCount > 1 ? i / (float)(millCount - 1) : 0.5f) + Range(-9f, 9f);
                float z = D + Range(26f, 58f);
                if (terrain.Reserved(x, z, 4f)) continue;
                HighlandMill(mills, new Vector3(x, terrain.Height(x, z) - 0.3f, z), Range(0.75f, 1f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s < 0 ? -Range(30f, 48f) : W + Range(30f, 48f), z = Range(D * 0.3f, D + 10f);
                if (!terrain.Reserved(x, z, 4f)) HighlandMill(mills, new Vector3(x, terrain.Height(x, z) - 0.3f, z), Range(0.8f, 1f));
            }
            Emit(mills, "Highland Mills", 0.85f);

            // standing stones: rings on the nearer hilltops, lone stones and cairns between them
            var stones = new MeshBuilder(rng.Next()) { Jitter = 0.07f, AOStrength = 0.3f, AOHeight = 1.2f };
            int rings = Mathf.Clamp(Mathf.RoundToInt(W / 55f), 1, 3);
            for (int i = 0; i < rings; i++)
            {
                float x = Mathf.Lerp(W * 0.2f, W * 0.8f, rings > 1 ? i / (float)(rings - 1) : 0.5f) + Range(-12f, 12f);
                float z = D + Range(13f, 24f);
                if (terrain.Reserved(x, z, 6f)) continue;
                StoneRing(stones, x, z, Range(3.6f, 5.2f), 7 + rng.Next(3));
            }
            Scatter(new[] { 9f, 18f, 30f }, 70f, 26f, 3f, 2f, (x, z, row) =>
            {
                if (R() < 0.55f) Menhir(stones, Ground(x, z, -0.2f), Range(1.6f, 2.8f), Range(-8f, 8f));
                else Cairn(stones, Ground(x, z, -0.1f), Range(0.8f, 1.3f));
            }, 12f, 40f, 16f);
            Emit(stones, "Standing Stones", 1f);

            // gold-leaved clumps and haystacks dotted over the near slopes
            var clumps = new MeshBuilder(rng.Next()) { Jitter = 0.06f };
            Scatter(new[] { 7f, 15f, 26f, 40f }, 80f, 14f, 3f, 2.5f, (x, z, row) =>
            {
                if (R() < 0.35f) return;
                if (R() < 0.3f) Haystack(clumps, Ground(x, z, -0.15f), Range(0.9f, 1.3f));
                else
                {
                    float h = Range(4.5f, 7f);
                    MapTerrain.RoundTree(clumps, rng, Ground(x, z, -0.3f), h, AutumnLeaf[rng.Next(AutumnLeaf.Length)]);
                }
            }, 14f, 46f, 9f);
            Emit(clumps, "Highland Clumps", 1f);
        }

        void HighlandMill(MeshBuilder mb, Vector3 b, float s)
        {
            // a stone tower mill with a thatched cap, door and window, the sails a separate turning part
            mb.Push().Translate(b).Scale(s);
            mb.Color = Paint.Hex("#ddd2bd");
            mb.Lathe(new[] { new Vector2(2.3f, 0f), new Vector2(1.6f, 7.6f) }, 8, false, false, true);
            mb.Color = Paint.Hex("#b7924f");
            mb.Cone(new Vector3(0f, 7.5f, 0f), 2.1f, 2.4f, 8);
            mb.Color = Paint.Hex("#5a4234");
            mb.BoxOn(new Vector3(0f, 0f, -2.18f), new Vector3(0.9f, 1.8f, 0.2f));
            mb.Box(new Vector3(0.4f, 4.4f, -1.95f), new Vector3(0.5f, 0.6f, 0.2f));
            mb.Pop();
            var smb = new MeshBuilder(rng.Next()) { Jitter = 0.04f, Color = Paint.Hex("#efe4cc") };
            for (int i = 0; i < 4; i++)
            {
                smb.Push().Rotate(0f, 0f, i * 90f);
                smb.Box(new Vector3(0f, 3.5f, 0f), new Vector3(1.3f, 5.8f, 0.08f));
                smb.Color = Paint.Hex("#8a6a4e");
                smb.Box(new Vector3(0f, 3.5f, -0.06f), new Vector3(0.12f, 6f, 0.06f));
                smb.Color = Paint.Hex("#efe4cc");
                smb.Pop();
            }
            smb.Color = Paint.Hex("#6b5040");
            smb.Box(Vector3.zero, new Vector3(0.6f, 0.6f, 0.45f));
            var go = Emit(smb, "Highland Mill Sails " + sails.Count, 0.85f);
            if (go == null) return;
            go.transform.localPosition = b + new Vector3(0f, 7.7f * s, -2f * s);
            go.transform.localScale = new Vector3(s, s, s);
            sails.Add(go.transform);
        }

        void StoneRing(MeshBuilder mb, float cx, float cz, float radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + Range(-0.15f, 0.15f)) * Mathf.PI * 2f / count;
                float x = cx + Mathf.Cos(a) * radius, z = cz + Mathf.Sin(a) * radius * 0.8f;
                if (R() < 0.12f) { Cairn(mb, Ground(x, z, -0.1f), Range(0.6f, 0.9f)); continue; }   // a fallen one
                Menhir(mb, Ground(x, z, -0.25f), Range(1.9f, 3f), Range(-7f, 7f));
            }
            // the tall stone at the heart, lichen on its crown
            Menhir(mb, Ground(cx, cz, -0.3f), Range(3.2f, 3.8f), Range(-3f, 3f));
        }

        void Menhir(MeshBuilder mb, Vector3 b, float h, float lean)
        {
            mb.Push().Translate(b).Rotate(Range(-4f, 4f), Range(0f, 180f), lean);
            mb.Color = Paint.Shade(MenhirStone, Range(0.88f, 1.06f));
            mb.Lathe(new[] { new Vector2(h * 0.2f, 0f), new Vector2(h * 0.19f, h * 0.55f), new Vector2(h * 0.13f, h * 0.92f), new Vector2(h * 0.05f, h) },
                     5, false, false, true, new[] { Paint.Shade(MenhirDark, 0.95f), MenhirStone, MenhirStone, Lichen }, Range(0f, 60f));
            mb.Pop();
        }

        void Cairn(MeshBuilder mb, Vector3 b, float s)
        {
            float y = 0f;
            for (int i = 0; i < 4; i++)
            {
                float r = s * (0.55f - i * 0.11f);
                mb.Color = Paint.Shade(MenhirStone, Range(0.85f, 1.05f));
                mb.Blob(b + new Vector3(Range(-0.08f, 0.08f) * s, y + r * 0.55f, Range(-0.08f, 0.08f) * s), new Vector3(r, r * 0.6f, r * 0.9f), 0, 0.2f, rng.Next(1000), 0.4f);
                y += r * 0.95f;
            }
        }

        void Haystack(MeshBuilder mb, Vector3 b, float s)
        {
            mb.Color = Paint.Hex("#d9b866");
            mb.Lathe(new[] { new Vector2(1.1f * s, 0f), new Vector2(1.05f * s, 0.8f * s), new Vector2(0.7f * s, 1.5f * s), new Vector2(0f, 1.9f * s) }, 8, false, false, true,
                     new[] { Paint.Hex("#b99a52"), Paint.Hex("#d9b866"), Paint.Hex("#e6c878"), Paint.Hex("#c9a85c") }, Range(0f, 40f));
        }

        // ================================================================== fen

        static readonly Color Bark = Paint.Hex("#6c6253"), BarkDark = Paint.Hex("#544b40"), HangingMoss = Paint.Hex("#9aa676");
        static readonly Color[] ReedCols = { Paint.Hex("#a3a15c"), Paint.Hex("#8f9a52"), Paint.Hex("#b3a866"), Paint.Hex("#7d8c4c") };
        static readonly Color[] FenWoods = { Paint.Hex("#5d6d58"), Paint.Hex("#66745e"), Paint.Hex("#556550") };

        void BuildFen()
        {
            // dead trees: a ragged line along the back, thinning out, and down both flanks
            var trees = new MeshBuilder(rng.Next()) { Jitter = 0.06f };
            Scatter(new[] { 5f, 9f, 14f, 21f, 30f }, 70f, 6.5f, 2f, 2f, (x, z, row) =>
            {
                if (row >= 3 && R() < 0.4f) return;
                DeadTree(trees, Ground(x, z, -0.2f), Range(4.5f, 8.5f) * (row < 0 ? 1.05f : 1f));
            }, 9f, 34f, 6f);
            Emit(trees, "Fen Dead Trees", 1f);

            // reed banks: dense along the near back and at the sides, clumps further out
            var reeds = new MeshBuilder(rng.Next()) { Jitter = 0.05f };
            Scatter(new[] { 2.8f, 6f, 11f }, 60f, 2.6f, 1.2f, 1.2f, (x, z, row) =>
            {
                if (row == 2 && R() < 0.5f) return;
                ReedClump(reeds, Ground(x, z, -0.05f), Range(0.8f, 1.3f), 7 + rng.Next(6));
            }, 4.5f, 16f, 2.4f, -12f);
            Emit(reeds, "Fen Reeds", 1f);

            // low misty woods far off: rounded grey-green crowns in rows
            var woods = new MeshBuilder(rng.Next()) { Jitter = 0.06f };
            float[] rows = { 46f, 58f, 74f, 92f };
            for (int ri = 0; ri < rows.Length; ri++)
                for (float x = -180f + Range(0f, 8f); x < W + 180f; x += Range(5f, 9f))
                {
                    float z = D + rows[ri] + Range(-3f, 3f);
                    if (terrain.Reserved(x, z, 2f)) continue;
                    var b = Ground(x, z, -0.5f);
                    float h = Range(6f, 10f);
                    if (R() < 0.3f) { DeadTree(woods, b, h); continue; }
                    woods.Color = FenWoods[rng.Next(FenWoods.Length)];
                    woods.Blob(b + new Vector3(0f, h * 0.55f, 0f), new Vector3(h * 0.42f, h * 0.5f, h * 0.38f), 1, 0.12f, rng.Next(1000), 0.4f);
                }
            Emit(woods, "Fen Woods", 0.8f);
        }

        void DeadTree(MeshBuilder mb, Vector3 b, float h)
        {
            mb.Wind = 0f; mb.WindGradient = false;
            float lean = Range(-9f, 9f), turn = Range(0f, 360f);
            mb.Push().Translate(b).Rotate(0f, turn, lean);
            mb.Color = Paint.Shade(Bark, Range(0.9f, 1.08f));
            // a crooked trunk in two pieces, flared at the foot
            var k1 = new Vector3(Range(-0.25f, 0.25f), h * 0.45f, Range(-0.2f, 0.2f));
            var top = k1 + new Vector3(Range(-0.4f, 0.4f), h * 0.45f, Range(-0.3f, 0.3f));
            mb.Cylinder(Vector3.zero, h * 0.07f, h * 0.05f, 0.3f, 6);
            mb.Segment(new Vector3(0f, 0.2f, 0f), k1, h * 0.05f, h * 0.035f, 5);
            mb.Segment(k1, top, h * 0.035f, h * 0.012f, 5);
            // bare branches reaching up and out, a twig or two on each
            int branches = 3 + rng.Next(3);
            mb.Color = BarkDark;
            for (int i = 0; i < branches; i++)
            {
                float t = Range(0.35f, 0.9f);
                var from = Vector3.Lerp(Vector3.zero, k1, Mathf.Min(1f, t * 2f)) + (t > 0.5f ? (top - k1) * (t - 0.5f) * 2f : Vector3.zero);
                float a = Range(0f, Mathf.PI * 2f);
                var to = from + new Vector3(Mathf.Cos(a) * h * Range(0.18f, 0.32f), h * Range(0.1f, 0.26f), Mathf.Sin(a) * h * Range(0.12f, 0.22f));
                mb.Segment(from, to, h * 0.022f, h * 0.006f, 4);
                var twig = to + new Vector3(Mathf.Cos(a + 0.8f) * h * 0.08f, h * 0.08f, Mathf.Sin(a + 0.8f) * h * 0.05f);
                mb.Segment(Vector3.Lerp(from, to, 0.65f), twig, h * 0.01f, h * 0.004f, 3);
                // grey-green moss hanging from the branch
                if (R() < 0.6f)
                {
                    mb.Color = HangingMoss;
                    var m = Vector3.Lerp(from, to, Range(0.5f, 0.9f));
                    mb.Segment(m, m - new Vector3(0f, h * Range(0.1f, 0.2f), 0f), h * 0.018f, h * 0.004f, 3);
                    mb.Color = BarkDark;
                }
            }
            mb.Pop();
        }

        void ReedClump(MeshBuilder mb, Vector3 b, float s, int blades)
        {
            mb.Wind = 0.3f; mb.WindGradient = true; mb.WindY0 = b.y; mb.WindY1 = b.y + 1.8f * s;
            for (int i = 0; i < blades; i++)
            {
                float a = Range(0f, Mathf.PI * 2f), r = Range(0f, 0.45f) * s;
                var p = b + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.7f);
                float h = Range(1f, 1.8f) * s;
                var tip = p + new Vector3(Mathf.Cos(a) * h * 0.18f, h, Mathf.Sin(a) * h * 0.12f);
                mb.Color = ReedCols[rng.Next(ReedCols.Length)];
                mb.Blade(p, tip, Range(0.07f, 0.11f) * s);
                if (R() < 0.22f)
                {
                    // a cattail head
                    mb.Color = Paint.Hex("#6e4c34");
                    mb.Segment(Vector3.Lerp(p, tip, 0.62f), Vector3.Lerp(p, tip, 0.8f), 0.045f * s, 0.04f * s, 5);
                }
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        // ================================================================== peaks

        static readonly Color PeakRock = Paint.Hex("#7d8494"), PeakRockFar = Paint.Hex("#8f97ab"), PeakSnow = Paint.Hex("#f4f7fc");
        static readonly Color[] SnowPineLeaf = { Paint.Hex("#3d5c54"), Paint.Hex("#46645a"), Paint.Hex("#36514a"), Paint.Hex("#4f6c62") };

        void BuildPeaks()
        {
            // a near ridge of snowy peaks towering over the pass, a second behind it
            var ridge = new MeshBuilder(rng.Next()) { Jitter = 0.07f };
            float cx = W * 0.5f;
            Ridge(ridge, cx, D + 150f, 46f, 70f, 125f, PeakRockFar, PeakSnow, 0.42f);
            Ridge(ridge, cx, D + 92f, 34f, 38f, 74f, PeakRock, PeakSnow, 0.5f);
            Emit(ridge, "Peaks Ridges", 0.72f);

            // snow-laden pines on the slopes behind and down the sides
            var pines = new MeshBuilder(rng.Next()) { Jitter = 0.06f };
            Scatter(new[] { 7f, 11f, 16f, 23f, 32f, 44f }, 80f, 4.2f, 1.8f, 1.6f, (x, z, row) =>
            {
                if (row >= 4 && R() < 0.35f) return;
                if (R() < 0.12f) { Boulder(pines, Ground(x, z, -0.2f), Range(0.8f, 1.6f)); return; }
                SnowPine(pines, Ground(x, z, -0.3f), Range(6f, 10.5f) * (row < 0 ? 1.1f : 1f));
            }, 10f, 40f, 4.5f);
            Emit(pines, "Peaks Pines", 1f);
        }

        void SnowPine(MeshBuilder mb, Vector3 b, float h)
        {
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Paint.Hex("#5a4434");
            mb.Cylinder(b, h * 0.04f, h * 0.025f, h * 0.28f, 6);
            var leaf = SnowPineLeaf[rng.Next(SnowPineLeaf.Length)];
            int tiers = 4;
            for (int i = 0; i < tiers; i++)
            {
                float y0 = h * (0.16f + i * 0.18f);
                float rr = h * 0.24f * (1f - i * 0.2f);
                float th = h * (0.36f - i * 0.04f);
                mb.Color = Paint.Shade(leaf, 0.86f + 0.07f * i);
                mb.Cylinder(b + new Vector3(0f, y0, 0f), rr, 0f, th, 7, false, true, false);
                // the snow lying on the tier: a white cone a little smaller and higher, leaving a green fringe
                mb.Color = PeakSnow;
                mb.Cylinder(b + new Vector3(0f, y0 + th * 0.3f, 0f), rr * 0.74f, 0f, th * 0.72f, 7, false, true, false);
            }
            // a drift at the foot
            mb.Color = PeakSnow;
            mb.Blob(b + new Vector3(0f, 0.15f, 0f), new Vector3(h * 0.16f, h * 0.04f, h * 0.14f), 0, 0.2f, rng.Next(1000), 0.9f);
        }

        void Boulder(MeshBuilder mb, Vector3 b, float s)
        {
            mb.Color = Paint.Shade(PeakRock, Range(0.9f, 1.08f));
            mb.Blob(b + new Vector3(0f, s * 0.4f, 0f), new Vector3(s, s * 0.7f, s * 0.85f), 0, 0.2f, rng.Next(1000), 0.5f);
            mb.Color = PeakSnow;
            mb.Blob(b + new Vector3(0f, s * 0.95f, 0f), new Vector3(s * 0.75f, s * 0.2f, s * 0.6f), 0, 0.15f, rng.Next(1000), 0.8f);
        }

        // ================================================================== roost

        static readonly Color Basalt = Paint.Hex("#625c66"), BasaltLight = Paint.Hex("#8c8590"), AshTop = Paint.Hex("#d2cac4");
        static readonly Color EmberGlow = new Color(1f, 0.5f, 0.22f);

        void BuildRoost()
        {
            // the crown of jagged spires: two staggered rows behind, a ring down the flanks
            var spires = new MeshBuilder(rng.Next()) { Jitter = 0.07f, AOStrength = 0.35f, AOHeight = 4f };
            var embers = new MeshBuilder(rng.Next()) { Emission = 1f, Jitter = 0.05f };
            // clustered, with gaps between the clusters where the cloud sea and the far summits show
            Scatter(new[] { 5f, 11f, 19f }, 55f, 7.5f, 2.2f, 2.5f, (x, z, row) =>
            {
                float n = Mathf.PerlinNoise(x * 0.045f + 2.1f, row * 0.9f + 0.4f);
                if (row >= 0 && n < 0.38f) return;
                float h = row < 0 ? Range(8f, 20f) : Mathf.Lerp(10f, 32f, n) * Range(0.8f, 1.2f);
                if (row >= 0 && R() < 0.15f) h *= 1.3f;
                Spire(spires, embers, Ground(x, z, -1.2f), h, Range(1.5f, 2.6f) * (row < 0 ? 0.9f : 1f));
            }, 8f, 26f, 7f, -18f);
            // sharp fins of rock among the columns, leaning outwards: the crown's jagged edge
            Scatter(new[] { 3.5f, 8f }, 50f, 6f, 1.5f, 2f, (x, z, row) =>
            {
                if (R() < 0.45f) return;
                float fh = Range(4f, 11f);
                spires.Push().Translate(Ground(x, z, -0.6f)).Rotate(Range(4f, 14f), Range(0f, 360f), Range(-10f, 10f));
                spires.Lathe(new[] { new Vector2(Range(0.9f, 1.5f), 0f), new Vector2(0.55f, fh * 0.6f), new Vector2(0f, fh) }, 3 + rng.Next(2), false, false, true,
                             new[] { Paint.Shade(Basalt, 0.85f), BasaltLight, AshTop }, Range(0f, 90f));
                spires.Pop();
            }, 6f, 16f, 6f, -14f);
            Emit(spires, "Roost Spires", 0.9f);
            Emit(embers, "Roost Embers", 0.6f);

            // a sea of cloud far below and around the summit, other summits rising out of it
            var sea = new MeshBuilder(rng.Next()) { Jitter = 0.04f };
            var top = Paint.Hex("#fbe9df");
            var under = Paint.Hex("#d9b8b6");
            for (int ring = 0; ring < 3; ring++)
            {
                float z0 = D + 70f + ring * 70f;
                for (float x = -320f + Range(0f, 30f); x < W + 320f; x += Range(32f, 52f))
                {
                    float z = z0 + Range(-20f, 20f);
                    float r = Range(26f, 44f) * (1f + ring * 0.3f);
                    sea.Color = Color.Lerp(under, top, Range(0.35f, 1f));
                    sea.Blob(new Vector3(x, Range(-6f, 6f) + ring * 6f, z), new Vector3(r, r * Range(0.22f, 0.32f), r * 0.6f), 1, 0.12f, rng.Next(1000), 0.6f);
                }
            }
            Emit(sea, "Roost Cloud Sea", 0.45f);
            var far = new MeshBuilder(rng.Next()) { Jitter = 0.07f };
            Ridge(far, W * 0.5f, D + 250f, 70f, 60f, 130f, Paint.Hex("#8a7f8c"), Paint.Hex("#f6eef0"), 0.55f);
            Emit(far, "Roost Far Summits", 0.6f);
        }

        void Spire(MeshBuilder mb, MeshBuilder embers, Vector3 b, float h, float r)
        {
            // a column of stacked basalt drums, each narrower, turned and knocked a little off the one below, ash lying
            // pale on every ledge, broken off into a crooked tip; a lesser shard leaning beside it
            mb.Push().Translate(b).Rotate(Range(-5f, 5f), Range(0f, 360f), Range(-7f, 7f));
            Column(mb, Vector3.zero, h, r, 5 + rng.Next(3));
            if (R() < 0.6f)
            {
                float sh = h * Range(0.3f, 0.55f);
                mb.Push().Translate(r * Range(1.1f, 1.5f), 0f, Range(-0.6f, 0.6f) * r).Rotate(Range(-6f, 6f), 0f, Range(-14f, -5f));
                Column(mb, Vector3.zero, sh, r * Range(0.45f, 0.6f), 5);
                mb.Pop();
            }
            mb.Pop();
            // an ember-lit crack at the foot (a few of them)
            if (R() < 0.35f)
            {
                embers.Color = Color.Lerp(EmberGlow, new Color(1f, 0.8f, 0.4f), R() * 0.5f);
                float a = Range(0f, Mathf.PI * 2f);
                var c = b + new Vector3(Mathf.Cos(a) * r * 0.9f, 0.6f, -Mathf.Abs(Mathf.Sin(a)) * r * 0.9f);
                embers.Push().Translate(c).Rotate(0f, Range(0f, 180f), Range(60f, 85f));
                embers.Box(Vector3.zero, new Vector3(0.12f, Range(1.2f, 2.4f), 0.12f));
                embers.Pop();
            }
        }

        void Column(MeshBuilder mb, Vector3 b, float h, float r, int sides)
        {
            // craggy drums: overlapping rough lumps, each narrower and knocked a little off the one below, ash pale on
            // the shoulders, a crooked broken tip
            int drums = Mathf.Clamp(Mathf.RoundToInt(h / 4.5f), 2, 7);
            float y = 0f, rr = r;
            var off = Vector3.zero;
            float dh = h * 0.9f / drums;
            for (int i = 0; i < drums; i++)
            {
                float t = drums > 1 ? i / (float)(drums - 1) : 0f;
                mb.Color = Color.Lerp(Paint.Shade(Basalt, Range(0.82f, 0.95f)), BasaltLight, t * 0.55f + Range(-0.05f, 0.08f));
                mb.Blob(b + off + new Vector3(0f, y + dh * 0.5f, 0f), new Vector3(rr, dh * Range(0.88f, 1f), rr * Range(0.8f, 0.95f)), 1, 0.26f, rng.Next(1000), i == 0 ? 0.5f : 0f);
                // ash on the shoulder
                mb.Color = AshTop;
                if (R() < 0.6f) mb.Blob(b + off + new Vector3(Range(-0.25f, 0.25f) * rr, y + dh * 0.95f, Range(-0.3f, 0f) * rr), new Vector3(rr * 0.62f, dh * 0.12f, rr * 0.55f), 0, 0.2f, rng.Next(1000), 0.6f);
                y += dh;
                rr *= Range(0.84f, 0.94f);
                off += new Vector3(Range(-0.18f, 0.18f) * rr, 0f, Range(-0.18f, 0.18f) * rr);
            }
            mb.Push().Translate(b + off + new Vector3(0f, y, 0f)).Rotate(Range(-14f, 14f), Range(0f, 90f), Range(-14f, 14f));
            mb.Color = BasaltLight;
            mb.Lathe(new[] { new Vector2(rr * 0.85f, -0.4f), new Vector2(rr * 0.3f, h * 0.12f), new Vector2(0f, h * 0.17f) }, Mathf.Max(4, sides - 1), false, false, true,
                     new[] { BasaltLight, Color.Lerp(BasaltLight, AshTop, 0.5f), AshTop });
            mb.Pop();
        }
    }
}
