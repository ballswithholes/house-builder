// Mirefen (fen): reeds and cattails, mangroves and weeping willows, lily pads on the water, a stilt hut, boardwalk
// segments, the drowned lanterns' fen lanterns, Mother Mire's coven totems, a sunken statue, giant glowing mushrooms and
// a fishing rack. Water sits 0.1 m below the ground plane (MapTerrain.WaterLevel): lily pads, boardwalk posts and the
// hut's stilts reach down to it. Colliders: Docs/ArtKeys.md (props-wild block).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterWildFen(Dictionary<string, Recipe> r)
        {
            r["prop_reeds"] = (art, seed) => WildPlainSwaying(art, seed, BuildReeds, 0.5f);
            r["prop_cattails"] = Cattails;
            r["prop_mangrove"] = (art, seed) => Swaying(art, seed, BuildMangrove, 1.0f);
            r["prop_willow"] = (art, seed) => Swaying(art, seed, BuildWillow, 0.6f);
            r["prop_lilypads"] = LilyPads;
            r["prop_stilt_hut"] = StiltHut;
            r["prop_boardwalk"] = (art, seed) => Boardwalk(art, seed, false);
            r["prop_boardwalk_y"] = (art, seed) => Boardwalk(art, seed, true);
            r["prop_fen_lantern"] = FenLantern;
            r["prop_mire_totem"] = MireTotem;
            r["prop_sunken_statue"] = (art, seed) => Simple(art, seed, BuildSunkenStatue, 1.2f);
            r["prop_mushroom_giant"] = MushroomGiant;
            r["prop_fishing_rack"] = (art, seed) => Swaying(art, seed, BuildFishingRack, 1.2f);
        }

        static readonly Color FenReed = Paint.Hex("#7E9150"), FenReedTip = Paint.Hex("#C2B56E"), FenMud = Paint.Hex("#5E5040"), FenWood = Paint.Hex("#7A6A58");
        static readonly Color FenWoodWet = Paint.Hex("#55493E"), FenMoss = Paint.Hex("#7E9A5A");

        /// <summary>A plain-skinned (no ink: thin blades) swaying prop.</summary>
        static PropModel WildPlainSwaying(string art, int seed, System.Func<int, MeshBuilder> build, float radius)
        {
            var m = Simple(art, seed, build, radius, Skin.Plain);
            m.Sways = true;
            return m;
        }

        // ------------------------------------------------------------------ reeds (≈ 1.8 m; walk-through, no collider)

        static MeshBuilder BuildReeds(int b)
        {
            var mb = new MeshBuilder(VariantSeed("prop_reeds", b)) { Jitter = 0.07f, AOStrength = 0.35f, AOHeight = 0.6f, WindGradient = true, WindY0 = 0f, WindY1 = 1.9f, Wind = 1.8f };
            int sd = VariantSeed("reeds", b);
            int n = 20 + b * 2;
            for (int i = 0; i < n; i++)
            {
                float a = (i * 137.5f + b * 31f) * Mathf.Deg2Rad;
                float d = 0.08f + 0.3f * Mathf.Sqrt((i + 0.5f) / n);
                var root = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d * 0.8f);
                float h = 1.1f + 0.75f * (WildHash(i, 1, sd) * 0.5f + 0.5f) - d * 0.6f;
                var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.8f) * (0.18f + 0.25f * d) + new Vector3(WildHash(i, 2, sd) * 0.08f, 0f, 0f);
                var tip = root + Vector3.up * h + lean * h;
                var mid = Vector3.Lerp(root, tip, 0.5f) - lean * 0.12f * h;
                mb.Color = Color.Lerp(FenReed, Paint.Hex("#5E7448"), WildHash(i, 3, sd) * 0.5f + 0.5f);
                var side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                mb.Blade(root, mid, 0.07f, side);
                mb.Color = Color.Lerp(FenReed, FenReedTip, 0.4f + 0.4f * (WildHash(i, 4, sd) * 0.5f + 0.5f));
                mb.Blade(mid, tip, 0.05f, side);
                // feathery seed plumes on the tallest stems
                if (i % 4 == 0)
                {
                    mb.Color = Paint.Hex("#B89A72");
                    for (int k = 0; k < 3; k++)
                    {
                        var d2 = (tip - mid).normalized;
                        var off = Quaternion.AngleAxis(k * 120f, d2) * side * 0.06f;
                        mb.Blade(tip - d2 * 0.05f, tip + d2 * 0.18f + off + Vector3.down * 0.05f, 0.06f, off.sqrMagnitude > 0f ? off : side);
                    }
                }
            }
            return mb;
        }

        // ------------------------------------------------------------------ cattails (≈ 1.7 m; no collider): plain leaves, inked heads

        static PropModel Cattails(string art, int seed)
        {
            int b = Bucket(seed);
            var rig = new Rig(art);
            rig.Body(Cached(art + "#" + b, () => BuildCattailLeaves(b)), Skin.Plain);
            rig.Part("Heads", Cached(art + "#heads" + b, () => BuildCattailHeads(b)), Vector3.zero, Quaternion.identity);
            var m = rig.Done(0.5f);
            m.Sways = true;
            return m;
        }

        static Vector3 CattailTip(int i, int b, out Vector3 root)
        {
            float a = (i * 97f + b * 41f) * Mathf.Deg2Rad;
            float d = 0.06f + 0.22f * ((i * 0.37f + b * 0.11f) % 1f);
            root = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d * 0.8f);
            float h = 1.25f + 0.45f * ((i * 0.61f + b * 0.23f) % 1f);
            return root + new Vector3(Mathf.Cos(a) * 0.12f, h, Mathf.Sin(a) * 0.1f);
        }

        const int CattailStems = 7;

        static MeshBuilder BuildCattailLeaves(int b)
        {
            var mb = new MeshBuilder(VariantSeed("prop_cattails", b)) { Jitter = 0.07f, AOStrength = 0.35f, AOHeight = 0.5f, WindGradient = true, WindY0 = 0f, WindY1 = 1.7f, Wind = 1.6f };
            int sd = VariantSeed("cattails", b);
            // long arching leaves
            for (int i = 0; i < 16; i++)
            {
                float a = (i * 137.5f + b * 23f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.8f);
                var root = dir * 0.12f;
                float h = 0.9f + 0.5f * (WildHash(i, 1, sd) * 0.5f + 0.5f);
                var mid = root + dir * 0.18f + Vector3.up * h * 0.6f;
                var tip = root + dir * (0.45f + 0.2f * (i % 3)) + Vector3.up * h * (i % 3 == 0 ? 0.75f : 1f);
                var side = new Vector3(-dir.z, 0f, dir.x);
                mb.Color = Color.Lerp(Paint.Hex("#6E8A48"), Paint.Hex("#4F6E40"), WildHash(i, 2, sd) * 0.5f + 0.5f);
                mb.Blade(root, mid, 0.09f, side);
                mb.Color = Color.Lerp(Paint.Hex("#86A058"), FenReedTip, 0.3f * (WildHash(i, 3, sd) * 0.5f + 0.5f));
                mb.Blade(mid, tip, 0.07f, side);
            }
            // stems
            mb.Color = Paint.Hex("#7E9454");
            for (int i = 0; i < CattailStems; i++)
            {
                var tip = CattailTip(i, b, out var root);
                mb.Blade(root, tip, 0.03f, Vector3.right);
                mb.Blade(root, tip, 0.03f, Vector3.forward);
            }
            return mb;
        }

        static MeshBuilder BuildCattailHeads(int b)
        {
            var mb = new MeshBuilder(VariantSeed("prop_cattails_heads", b)) { Jitter = 0.06f, WindGradient = true, WindY0 = 0f, WindY1 = 1.7f, Wind = 1.6f };
            for (int i = 0; i < CattailStems; i++)
            {
                var tip = CattailTip(i, b, out var root);
                var d = (tip - root).normalized;
                mb.Color = Paint.Hex("#7A4A2E");
                mb.Segment(tip - d * 0.3f, tip - d * 0.06f, 0.045f, 0.042f, 6, false, true);
                mb.Color = Paint.Hex("#A89A6A");
                mb.Segment(tip - d * 0.06f, tip + d * 0.12f, 0.01f, 0.004f, 3, false, true);
            }
            return mb;
        }

        // ------------------------------------------------------------------ mangrove (≈ 6 m on arching prop roots; collider 2.2 × 1.8)

        static MeshBuilder BuildMangrove(int b)
        {
            var mb = Builder(VariantSeed("prop_mangrove", b), 0.07f, 0.35f, 0.8f);
            var bark = Paint.Hex("#6E5E4E");
            int sd = VariantSeed("mangrove", b);
            // arching stilt roots from the trunk's foot down into the mud
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f + b * 17f + WildHash(i, 1, sd) * 12f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.72f);
                float reach = 0.95f + 0.25f * (WildHash(i, 2, sd) * 0.5f + 0.5f);
                var pts = new[]
                {
                    dir * 0.12f + Vector3.up * 1.45f, dir * (reach * 0.45f) + Vector3.up * 1.42f, dir * (reach * 0.82f) + Vector3.up * 0.95f,
                    dir * reach + Vector3.up * 0.35f, dir * (reach * 1.04f) + Vector3.down * 0.08f,
                };
                mb.Color = Color.Lerp(bark, FenWoodWet, 0.3f);
                WildRib(mb, pts, 0.12f, 0.06f, 5);
            }
            // trunk and spreading limbs
            Trunk(mb, new[] { new Vector2(0.34f, 1.2f), new Vector2(0.3f, 1.7f), new Vector2(0.26f, 2.8f), new Vector2(0.2f, 3.6f) }, 6, bark, 0.85f);
            mb.Color = bark;
            Vector3[] limbs = { new Vector3(-1.7f, 4.4f, 0.2f), new Vector3(1.8f, 4.6f, 0.1f), new Vector3(0.2f, 4.9f, 0.9f), new Vector3(-0.3f, 4.6f, -0.8f) };
            foreach (var l in limbs) mb.Segment(new Vector3(0f, 3.3f, 0f), l, 0.15f, 0.06f, 5);
            // a few aerial roots dropping from the limbs
            mb.Color = Paint.Shade(bark, 0.9f);
            for (int i = 0; i < 3; i++)
            {
                var top = Vector3.Lerp(new Vector3(0f, 3.3f, 0f), limbs[i], 0.75f);
                mb.Segment(top, new Vector3(top.x * 1.1f, -0.05f, top.z * 0.6f), 0.04f, 0.03f, 4);
            }
            // a broad, low, glossy canopy
            Foliage(b, Paint.Hex("#4F7E4C"), out var ctop, out var cside, out var cbottom, 6f);
            var ramp = CanopyRamp(ctop, cside, cbottom, 3.7f, 6.4f);
            (Vector3 c, Vector3 r)[] blobs =
            {
                (new Vector3(0f, 5.0f, 0.2f), new Vector3(2.0f, 1.1f, 1.7f)), (new Vector3(-1.75f, 4.6f, 0.15f), new Vector3(1.25f, 0.85f, 1.2f)),
                (new Vector3(1.85f, 4.75f, 0.1f), new Vector3(1.3f, 0.9f, 1.2f)), (new Vector3(0.35f, 5.7f, 0.4f), new Vector3(1.35f, 0.8f, 1.2f)),
                (new Vector3(-0.4f, 4.4f, -1.0f), new Vector3(1.1f, 0.7f, 0.9f)), (new Vector3(0.6f, 4.5f, 1.35f), new Vector3(1.2f, 0.75f, 1.0f)),
                (new Vector3(-2.7f, 4.4f, 0.4f), new Vector3(0.7f, 0.55f, 0.65f)), (new Vector3(2.8f, 4.55f, 0.3f), new Vector3(0.7f, 0.55f, 0.65f)),
            };
            for (int i = 0; i < blobs.Length; i++) SoftLump(mb, blobs[i].c, blobs[i].r, 1, i * 47f + b * 11f, ramp, 0.14f);
            // grey-green moss beards hanging under the canopy
            var beard = Paint.Hex("#A3AE8E");
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + b * 20f) * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(a) * 1.5f, 3.75f, Mathf.Sin(a) * 1.1f);
                SoftLump(mb, c, new Vector3(0.14f, 0.5f, 0.12f), 0, i * 33f, y => Color.Lerp(Paint.Shade(beard, 0.8f), beard, Mathf.Clamp01((y - 3.2f) / 0.9f)), 0.2f);
            }
            // a mossy hummock between the roots
            FacetBlob(mb, new Vector3(0f, 0.02f, 0f), new Vector3(0.75f, 0.2f, 0.55f), 1, 0.2f, sd, 0.5f, ByNormal(FenMoss, Paint.Shade(FenMud, 1.1f), FenMud));
            return mb;
        }

        // ------------------------------------------------------------------ weeping willow (≈ 7.5 m; trunk collider 1.2 × 0.8)

        static MeshBuilder BuildWillow(int b)
        {
            var mb = Builder(VariantSeed("prop_willow", b), 0.06f, 0.3f, 1.2f);
            var bark = Paint.Hex("#7C6C5E");
            int sd = VariantSeed("willow", b);
            mb.Push().Rotate(0f, b * 35f, (b - 1.5f) * 2.5f);
            Trunk(mb, new[] { new Vector2(0.5f, 0f), new Vector2(0.38f, 0.4f), new Vector2(0.34f, 1.6f), new Vector2(0.31f, 2.6f), new Vector2(0.24f, 3.3f) }, 7, bark, 0.8f);
            Roots(mb, 5, 0.7f, 0.22f, 0.6f, b * 19f, bark);
            mb.Color = bark;
            Vector3[] limbs = { new Vector3(-1.6f, 5.3f, 0.3f), new Vector3(1.7f, 5.5f, 0.1f), new Vector3(0.1f, 6.3f, 0.6f), new Vector3(0.4f, 5.4f, -0.9f), new Vector3(-0.6f, 5.6f, 1.1f) };
            foreach (var l in limbs) mb.Segment(new Vector3(0f, 3.1f, 0f), l, 0.17f, 0.06f, 5);
            mb.Pop();
            Foliage(b, Paint.Hex("#6F9848"), out var top, out var side, out var bottom, 8f);
            var ramp = CanopyRamp(top, side, bottom, 1.4f, 7.6f);
            // the crown: a soft dome
            mb.WindGradient = true; mb.WindY0 = 3.5f; mb.WindY1 = 7.5f; mb.Wind = 1.4f;
            (Vector3 c, Vector3 r)[] crown =
            {
                (new Vector3(0f, 6.2f, 0.2f), new Vector3(2.3f, 1.2f, 1.9f)), (new Vector3(-1.4f, 5.7f, 0.1f), new Vector3(1.35f, 0.95f, 1.3f)),
                (new Vector3(1.5f, 5.85f, 0.25f), new Vector3(1.35f, 0.95f, 1.3f)), (new Vector3(0.2f, 7.0f, 0.2f), new Vector3(1.4f, 0.75f, 1.2f)),
            };
            for (int i = 0; i < crown.Length; i++) SoftLump(mb, crown[i].c, crown[i].r, 1, i * 53f + b * 7f, ramp, 0.12f);
            // weeping curtains: long hanging fronds (flat, tapering strands) around the rim, swaying most at their free ends
            mb.WindGradient = true; mb.WindY0 = 6.0f; mb.WindY1 = 1.5f; mb.Wind = 2.3f;
            const int Drapes = 28;
            for (int i = 0; i < Drapes; i++)
            {
                float a = (i * 360f / Drapes + WildHash(i, 1, sd) * 6f) * Mathf.Deg2Rad;
                bool inner = i % 3 == 2;
                float rr = (inner ? 1.55f : 2.2f) + 0.4f * (WildHash(i, 2, sd) * 0.5f + 0.5f);
                float len = (inner ? 2.2f : 2.7f) + 0.9f * (WildHash(i, 3, sd) * 0.5f + 0.5f);
                // front fronds hang shorter so the trunk and anyone beneath stay readable
                if (Mathf.Sin(a) < -0.45f) len *= 0.68f;
                float topY = (inner ? 5.4f : 5.85f) + 0.3f * WildHash(i, 4, sd);
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.85f);
                var tang = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)).normalized;
                var off = new Vector3(0f, 0f, 0.15f);
                var pts = new[]
                {
                    off + radial * rr + Vector3.up * topY, off + radial * (rr + 0.28f) + Vector3.up * (topY - len * 0.32f),
                    off + radial * (rr + 0.32f) + Vector3.up * (topY - len * 0.68f), off + radial * (rr + 0.24f) + Vector3.up * (topY - len),
                };
                WildStrand(mb, pts, new[] { 0.36f, 0.3f, 0.2f, 0.03f }, new[] { 0.12f, 0.1f, 0.07f, 0.015f }, tang, radial.normalized, ramp);
            }
            mb.Wind = 0f; mb.WindGradient = false;
            return mb;
        }

        /// <summary>
        /// A hanging frond: a flat diamond cross-section (width along `tang`, thickness along `radial`) lofted through pts,
        /// each quad coloured by the canopy ramp at its height.
        /// </summary>
        static void WildStrand(MeshBuilder mb, Vector3[] pts, float[] w, float[] t, Vector3 tang, Vector3 radial, System.Func<float, Color> ramp)
        {
            var rings = new Vector3[pts.Length][];
            for (int i = 0; i < pts.Length; i++)
                rings[i] = new[] { pts[i] + tang * w[i], pts[i] + radial * t[i], pts[i] - tang * w[i], pts[i] - radial * t[i] };
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var c = (pts[i] + pts[i + 1]) * 0.5f;
                for (int j = 0; j < 4; j++)
                {
                    int k = (j + 1) % 4;
                    var mid = (rings[i][j] + rings[i][k] + rings[i + 1][j] + rings[i + 1][k]) * 0.25f;
                    var col = ramp(mid.y);
                    float jk = 1f + (mb.Random01() * 2f - 1f) * mb.Jitter;
                    QuadC(mb, rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j], mid - c, new Color(col.r * jk, col.g * jk, col.b * jk, 1f));
                }
            }
            Fan(mb, rings[0], pts[0], pts[0] - pts[1], n => ramp(pts[0].y));
        }

        // ------------------------------------------------------------------ lily pads (on data water; walk-through, no collider)

        static PropModel LilyPads(string art, int seed)
        {
            var m = Simple(art, seed, BuildLilyPads, 0.9f);
            return m;
        }

        static MeshBuilder BuildLilyPads(int b)
        {
            var mb = Builder(VariantSeed("prop_lilypads", b), 0.06f, 0f, 1f);
            int sd = VariantSeed("lilypads", b);
            Color[] greens = { Paint.Hex("#6E9A4E"), Paint.Hex("#5E8A48"), Paint.Hex("#86AE5A"), Paint.Hex("#4F7A44") };
            int n = 9 + b;
            for (int i = 0; i < n; i++)
            {
                float a = (i * 137.5f + b * 50f) * Mathf.Deg2Rad;
                float d = 0.15f + 0.85f * Mathf.Sqrt((i + 0.5f) / n);
                var c = new Vector3(Mathf.Cos(a) * d, 0.012f + (i % 3) * 0.004f, Mathf.Sin(a) * d * 0.75f);
                float r = 0.16f + 0.14f * (WildHash(i, 1, sd) * 0.5f + 0.5f);
                // a round pad with a notch cut towards its centre
                float notch = WildHash(i, 2, sd) * 180f;
                var pts = NgonPoints(c, r, r, 9, Vector3.right, Vector3.forward, notch + 20f, 0f, 320f);
                var poly = new List<Vector3>(pts) { c };
                mb.Color = greens[i % greens.Length];
                Slab(mb, poly, Vector3.up, 0.02f);
                mb.Color = Paint.Shade(greens[i % greens.Length], 0.8f);
                mb.Segment(c + Vector3.up * 0.002f, c + Vector3.up * 0.002f + new Vector3(Mathf.Cos((notch + 200f) * Mathf.Deg2Rad), 0f, Mathf.Sin((notch + 200f) * Mathf.Deg2Rad)) * r * 0.8f, 0.006f, 0.006f, 3, false, false);
            }
            // two lotus blooms and a bud
            Color pink = Paint.Hex("#F2A0B8"), pinkDeep = Paint.Hex("#E07A9A");
            for (int k = 0; k < 2; k++)
            {
                float a = (k * 160f + b * 40f + 30f) * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(a) * 0.45f, 0.04f, Mathf.Sin(a) * 0.3f);
                Bloom(mb, c, 0.16f, new Vector3(0.1f, 1f, -0.2f), pinkDeep, Paint.Hex("#F6D04D"), k * 36f);
                Bloom(mb, c + Vector3.up * 0.05f, 0.11f, new Vector3(-0.1f, 1f, -0.2f), pink, Paint.Hex("#F6D04D"), k * 36f + 30f);
            }
            mb.Color = pink;
            var bud = new Vector3(-0.35f, 0.02f, 0.35f);
            mb.Color = Paint.Hex("#6E9A4E");
            mb.Segment(bud, bud + new Vector3(0.02f, 0.18f, 0f), 0.012f, 0.012f, 3);
            mb.Color = pinkDeep;
            Gem(mb, bud + new Vector3(0.02f, 0.25f, 0f), new Vector3(0.045f, 0.08f, 0.045f));
            return mb;
        }

        // ------------------------------------------------------------------ stilt hut (≈ 5.1 m; deck at 1.3 m; collider 4.8 × 4.6, centred)

        static PropModel StiltHut(string art, int seed) =>
            LitBuilding(art, seed, BuildStiltHut, 2.0f,
                        new Vector3(-0.75f, 2.35f, -0.75f), new Vector3(1.45f, 2.35f, -1.62f), new Vector3(1.55f, 2.35f, 0.5f), new Vector3(-0.2f, 2.35f, 1.75f));

        static MeshBuilder BuildStiltHut(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_stilt_hut", b), 0.07f, 0.3f, 1.2f);
            int sd = VariantSeed("stilthut", b);
            const float DY = 1.3f, DX = 1.75f, DZ0 = -1.55f, DZ1 = 1.7f;       // deck
            const float WX = 1.4f, WZ0 = -0.62f, WZ1 = 1.6f, WY = 3.3f;        // hut walls
            float wzc = (WZ0 + WZ1) * 0.5f;
            var wall = new[] { Paint.Hex("#8A7E68"), Paint.Hex("#7E8A7A"), Paint.Hex("#9A8466"), Paint.Hex("#86806E") }[b];
            var trim = FenWoodWet;
            // stilts (crooked), cross braces
            float[] sx = { -1.6f, 0f, 1.6f }, sz = { -1.4f, 0.1f, 1.55f };
            mb.Color = FenWood;
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 3; k++)
                {
                    var top = new Vector3(sx[i], DY, sz[k]);
                    var foot = top + new Vector3(WildHash(i, k, sd) * 0.12f, -DY - 0.45f, WildHash(k, i, sd) * 0.1f);
                    mb.Color = FenWood;
                    mb.Segment(foot, Vector3.Lerp(foot, top, 0.3f), 0.1f, 0.095f, 6);
                    mb.Color = Color.Lerp(FenWood, Pal.WoodLight, 0.15f);
                    mb.Segment(Vector3.Lerp(foot, top, 0.3f), top, 0.095f, 0.09f, 6);
                    mb.Color = FenWoodWet;
                    mb.Torus(Vector3.Lerp(foot, top, 0.3f), 0.1f, 0.025f, 6, 3);
                }
            mb.Color = FenWood;
            for (int s = -1; s <= 1; s += 2)
            {
                Beam(mb, new Vector3(s * 1.6f, 0.15f, sz[0]), new Vector3(s * 1.6f, DY - 0.15f, sz[2]), 0.07f, 0.07f, Vector3.right);
                Beam(mb, new Vector3(sx[0], 0.15f, s < 0 ? sz[0] : sz[2]), new Vector3(sx[2], DY - 0.15f, s < 0 ? sz[0] : sz[2]), 0.07f, 0.07f, Vector3.forward);
            }
            // deck: joists + planks running along X, slightly uneven
            mb.Color = trim;
            for (int k = 0; k < 3; k++) mb.Box(new Vector3(0f, DY - 0.09f, sz[k]), new Vector3(DX * 2f + 0.2f, 0.14f, 0.16f));
            const int planks = 13;
            for (int i = 0; i < planks; i++)
            {
                float z = Mathf.Lerp(DZ0, DZ1, (i + 0.5f) / planks);
                mb.Color = i % 3 == 0 ? Pal.WoodGrey : (i % 3 == 1 ? FenWood : Paint.Shade(Pal.WoodLight, 0.85f));
                float ext = WildHash(i, 7, sd) * 0.08f;
                mb.Box(new Vector3(ext, DY + 0.03f, z), new Vector3(DX * 2f + 0.06f - Mathf.Abs(ext), 0.06f, (DZ1 - DZ0) / planks - 0.02f));
            }
            // walls: horizontal weatherboards with corner posts
            mb.Color = wall;
            mb.BoxOn(new Vector3(0f, DY + 0.06f, wzc), new Vector3(WX * 2f, WY - DY, WZ1 - WZ0));
            mb.Color = Paint.Shade(wall, 0.75f);
            for (int k = 1; k < 7; k++)
            {
                float y = DY + 0.06f + (WY - DY) * k / 7f;
                mb.Box(new Vector3(0f, y, WZ0 - 0.01f), new Vector3(WX * 2f, 0.03f, 0.02f));
                mb.Box(new Vector3(0f, y, WZ1 + 0.01f), new Vector3(WX * 2f, 0.03f, 0.02f));
                mb.Box(new Vector3(-WX - 0.01f, y, wzc), new Vector3(0.02f, 0.03f, WZ1 - WZ0));
                mb.Box(new Vector3(WX + 0.01f, y, wzc), new Vector3(0.02f, 0.03f, WZ1 - WZ0));
            }
            mb.Color = trim;
            for (int i = -1; i <= 1; i += 2)
                for (int k = -1; k <= 1; k += 2)
                    mb.BoxOn(new Vector3(i * (WX + 0.02f), DY + 0.06f, k < 0 ? WZ0 - 0.02f : WZ1 + 0.02f), new Vector3(0.14f, WY - DY + 0.05f, 0.14f));
            // door, round window, side and back windows
            Door(mb, 0.4f, DY + 0.06f, 0.78f, 1.75f, WZ0, Paint.Hex("#6E8A7A"), trim, true, Pal.Brass);
            RoundWindow(mb, -0.75f, 2.35f, 0.32f, WZ0, lit, trim);
            mb.Push().Rotate(0f, -90f, 0f);   // local −Z faces +X, local x = world z
            Window(mb, 0.5f, 2.35f, 0.5f, 0.55f, -WX, lit, trim, null, true, false);
            mb.Pop();
            mb.Push().Rotate(0f, 180f, 0f);
            Window(mb, 0.2f, 2.35f, 0.55f, 0.55f, -WZ1, lit, trim, null, true, false);
            mb.Pop();
            mb.Push().Rotate(0f, 90f, 0f);    // local −Z faces −X, local x = −world z
            Window(mb, -0.6f, 2.35f, 0.45f, 0.5f, -WX, lit, trim, null, false, false);
            mb.Pop();
            // shaggy reed thatch with crossed ridge sticks
            mb.Color = Color.Lerp(Paint.Hex("#B39A62"), Paint.Hex("#A8A06A"), b * 0.25f);
            mb.Jitter = 0.05f;
            const float RH = 1.55f, RL = 0.42f;
            float rhd = (WZ1 - WZ0) * 0.5f + 0.62f;
            ThatchRoof(mb, WX + 0.55f, wzc, rhd, WY - 0.05f, RH, RL, 1.15f, b);
            // a ragged reed fringe along both eaves
            var fringe = Paint.Shade(mb.Color, 0.8f);
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                float z = wzc + s2 * (rhd - 0.14f), y = WY - 0.05f - RL;
                for (int k = 0; k < 14; k++)
                {
                    float x0 = -WX - 0.35f + (2f * WX + 0.7f) * k / 14f, x1 = -WX - 0.35f + (2f * WX + 0.7f) * (k + 1) / 14f;
                    float drop = 0.14f + 0.08f * (WildHash(k, s2, sd) * 0.5f + 0.5f);
                    var tip = new Vector3((x0 + x1) * 0.5f, y - drop, z + s2 * 0.03f);
                    Tri(mb, new Vector3(x0, y + 0.02f, z), new Vector3(x1, y + 0.02f, z), tip, new Vector3(0f, -0.2f, s2), fringe);
                    Tri(mb, new Vector3(x0, y + 0.02f, z), tip, new Vector3(x1, y + 0.02f, z), new Vector3(0f, 0.2f, -s2), Paint.Shade(fringe, 0.7f));
                }
            }
            mb.Jitter = 0.07f;
            mb.Color = trim;
            float ridgeY = WY - 0.05f + RH;
            for (int s = -1; s <= 1; s += 2)
            {
                var at = new Vector3(s * (WX + 0.25f), ridgeY - 0.25f, wzc);
                mb.Segment(at + new Vector3(-0.05f * s, -0.1f, -0.35f), at + new Vector3(0.05f * s, 0.55f, 0.3f), 0.04f, 0.03f, 4);
                mb.Segment(at + new Vector3(-0.05f * s, -0.1f, 0.35f), at + new Vector3(0.05f * s, 0.55f, -0.3f), 0.04f, 0.03f, 4);
            }
            // porch rail, lantern post, ladder
            mb.Color = FenWood;
            foreach (float x in new[] { -DX + 0.08f, DX - 0.08f })
                mb.BoxOn(new Vector3(x, DY + 0.06f, DZ0 + 0.08f), new Vector3(0.09f, 0.95f, 0.09f));
            mb.BoxOn(new Vector3(DX - 0.08f, DY + 0.06f, -0.25f), new Vector3(0.09f, 0.95f, 0.09f));
            mb.Color = Pal.RopeStraw;
            Rope(mb, new Vector3(0.0f, DY + 0.95f, DZ0 + 0.08f), new Vector3(DX - 0.08f, DY + 0.95f, DZ0 + 0.08f), 0.1f, 0.018f, 6, 3);
            Rope(mb, new Vector3(DX - 0.08f, DY + 0.95f, DZ0 + 0.08f), new Vector3(DX - 0.08f, DY + 0.95f, -0.25f), 0.08f, 0.018f, 5, 3);
            mb.Color = FenWood;
            mb.BoxOn(new Vector3(DX - 0.08f, DY + 1.0f, DZ0 + 0.08f), new Vector3(0.07f, 0.9f, 0.07f));
            Beam(mb, new Vector3(DX - 0.08f, DY + 1.82f, DZ0 + 0.08f), new Vector3(DX - 0.35f, DY + 1.82f, DZ0 + 0.08f), 0.05f, 0.05f, Vector3.forward);
            PaperLantern(mb, new Vector3(DX - 0.33f, DY + 1.5f, DZ0 + 0.08f), 0.13f, 0.28f, Paint.Hex("#F3DAA6"), lit, trim);
            const float lx = -0.85f;
            mb.Color = Pal.WoodGrey;
            for (int s = -1; s <= 1; s += 2)
                Beam(mb, new Vector3(lx + s * 0.24f, -0.02f, DZ0 - 0.55f), new Vector3(lx + s * 0.24f, DY + 0.75f, DZ0 + 0.02f), 0.07f, 0.06f, Vector3.right);
            for (int k = 0; k < 5; k++)
            {
                float t = (k + 0.7f) / 6f;
                var p = Vector3.Lerp(new Vector3(lx, -0.02f, DZ0 - 0.55f), new Vector3(lx, DY + 0.75f, DZ0 + 0.02f), t);
                mb.Box(p, new Vector3(0.5f, 0.05f, 0.07f));
            }
            // porch clutter: a basket of fish, a coiled rope, a hanging net with corks
            Basket(mb, new Vector3(-1.45f, DY + 0.06f, DZ0 + 0.35f), 0.2f, Paint.Hex("#9AA6A0"), b);
            mb.Color = Pal.RopeStraw;
            mb.Torus(new Vector3(1.1f, DY + 0.1f, DZ0 + 0.4f), 0.16f, 0.04f, 8, 3);
            mb.Torus(new Vector3(1.1f, DY + 0.15f, DZ0 + 0.4f), 0.12f, 0.04f, 8, 3);
            mb.Color = Paint.Hex("#6E7A6A");
            Slab(mb, new[] { new Vector3(WX + 0.03f, DY + 1.75f, 0.0f), new Vector3(WX + 0.03f, DY + 1.75f, 1.2f), new Vector3(WX + 0.05f, DY + 0.55f, 1.05f), new Vector3(WX + 0.05f, DY + 0.75f, 0.1f) },
                 Vector3.right, 0.01f);
            mb.Color = Paint.Hex("#C9A06A");
            for (int k = 0; k < 4; k++) Gem(mb, new Vector3(WX + 0.08f, DY + 1.72f, 0.1f + k * 0.33f), 0.045f);
            // reeds at the stilts' feet
            for (int k = 0; k < 4; k++)
            {
                var at = new Vector3(sx[k % 3] + 0.25f, 0f, sz[k / 2 == 0 ? 0 : 2] - 0.2f);
                WildTuft(mb, at, 0.65f, 5, FenReed, FenReedTip);
            }
            return mb;
        }

        // ------------------------------------------------------------------ boardwalk segment (3.0 × 1.5 m; walkable: no collider)

        /// <summary>
        /// A 3 m plank segment, deck top 0.08 m above the ground: chains seamlessly every 3.0 m along X (prop_boardwalk) or
        /// along the map's depth y (prop_boardwalk_y). Posts reach down into the water (0.1 m under the ground plane).
        /// </summary>
        static PropModel Boardwalk(string art, int seed, bool alongY)
        {
            int b = Bucket(seed);
            var rig = new Rig(art);
            rig.Body(Cached(art + "#" + b, () => BuildBoardwalk(b, alongY)));
            return rig.Done(1.5f);
        }

        static MeshBuilder BuildBoardwalk(int b, bool alongY)
        {
            var mb = Builder(VariantSeed("prop_boardwalk", b), 0.07f, 0f, 1f);
            int sd = VariantSeed("boardwalk", b);
            const float L = 1.5f, Wd = 0.75f, top = 0.08f;
            if (alongY) mb.Push().Rotate(0f, 90f, 0f);
            // stringers and cross beams on piles
            mb.Color = FenWoodWet;
            for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(0f, top - 0.12f, s * (Wd - 0.12f)), new Vector3(L * 2f, 0.12f, 0.12f));
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -L + 0.3f : L - 0.3f;
                for (int s = -1; s <= 1; s += 2)
                {
                    var p = new Vector3(x, 0f, s * (Wd + 0.03f));
                    mb.Color = FenWood;
                    mb.Segment(p + Vector3.down * 0.55f, p + Vector3.up * (0.28f + 0.06f * ((i + s + b) % 2)), 0.075f, 0.07f, 6);
                    mb.Color = FenMoss;
                    mb.Cone(p + Vector3.up * (0.28f + 0.06f * ((i + s + b) % 2)), 0.08f, 0.05f, 6);
                    mb.Color = FenWoodWet;
                    mb.Torus(p + Vector3.up * 0.0f, 0.08f, 0.022f, 6, 3);
                }
            }
            // planks across the walk (along Z), a little uneven, a few greyer
            const int planks = 12;
            for (int i = 0; i < planks; i++)
            {
                float x = -L + 2f * L * (i + 0.5f) / planks;
                float w = 2f * L / planks - 0.03f;
                mb.Color = (i * 7 + b) % 5 == 0 ? Pal.WoodGrey : (i % 2 == 0 ? FenWood : Color.Lerp(FenWood, Pal.WoodLight, 0.3f));
                float twist = WildHash(i, 1, sd) * 2.5f, len = Wd * 2f + 0.08f + WildHash(i, 2, sd) * 0.1f;
                OBox(mb, new Vector3(x, top - 0.025f, WildHash(i, 3, sd) * 0.04f), new Vector3(w, 0.05f, len), Quaternion.Euler(0f, twist, 0f));
            }
            // a tuft of moss on a plank end and some nail heads
            FacetBlob(mb, new Vector3(-0.6f, top + 0.01f, Wd - 0.05f), new Vector3(0.14f, 0.04f, 0.1f), 0, 0.2f, sd, 0f, ByNormal(FenMoss, Paint.Shade(FenMoss, 0.85f), FenMoss));
            if (alongY) mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ fen lantern (≈ 2.4 m; collider 0.4 × 0.4): a jar lantern on a crooked pole

        const float FenLanternX = 0.55f, FenLanternY = 1.78f;

        static PropModel FenLantern(string art, int seed) =>
            LitBuilding(art, seed, BuildFenLantern, 0.25f, new Vector3(FenLanternX, FenLanternY, 0f));

        static MeshBuilder BuildFenLantern(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_fen_lantern", b), 0.07f, 0.3f, 0.4f);
            int sd = VariantSeed("fenlantern", b);
            // mossy footing stones and a few mushrooms
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + b * 25f) * Mathf.Deg2Rad;
                FacetBlob(mb, new Vector3(Mathf.Cos(a) * 0.17f, 0.05f, Mathf.Sin(a) * 0.14f), new Vector3(0.12f, 0.09f, 0.1f), 0, 0.2f, sd + i, 0.3f, Mossy(Pal.StoneDark, FenMoss, 0.5f));
            }
            LittleMushroom(mb, new Vector3(-0.2f, 0f, -0.14f), 0.16f, 0.07f, Paint.Hex("#58D2B8"), 8f, 30f, 0.5f);
            LittleMushroom(mb, new Vector3(-0.12f, 0f, -0.2f), 0.1f, 0.05f, Paint.Hex("#58D2B8"), -10f, 70f, 0.5f);
            // a crooked driftwood pole with a hook arm
            mb.Color = Paint.Hex("#8A7C6C");
            var pole = new[] { new Vector3(0f, -0.05f, 0f), new Vector3(0.04f, 0.8f, 0.02f), new Vector3(-0.03f, 1.6f, -0.02f), new Vector3(0.02f, 2.25f, 0f) };
            WildRib(mb, pole, 0.075f, 0.055f, 6);
            WildRib(mb, new[] { new Vector3(0.02f, 2.15f, 0f), new Vector3(0.3f, 2.3f, 0f), new Vector3(FenLanternX, 2.22f, 0f), new Vector3(FenLanternX + 0.06f, 2.1f, 0f) }, 0.04f, 0.03f, 5);
            mb.Color = Pal.RopeStraw;
            mb.Torus(new Vector3(0.02f, 2.0f, 0f), 0.07f, 0.02f, 6, 3);
            mb.Color = Pal.Iron;
            mb.Segment(new Vector3(FenLanternX + 0.03f, 2.12f, 0f), new Vector3(FenLanternX, FenLanternY + 0.2f, 0f), 0.01f, 0.01f, 3);
            // the jar: bulbous glass with an iron cap and cage bands; glows pale gold-green when lit
            var glass = lit ? Paint.Hex("#F4EBA4") : Paint.Hex("#6E7E74");
            mb.Push().Translate(FenLanternX, FenLanternY, 0f);
            mb.Color = glass;
            mb.Emission = lit ? 1f : 0.05f;
            mb.Lathe(new[] { new Vector2(0.07f, -0.15f), new Vector2(0.13f, -0.08f), new Vector2(0.14f, 0.02f), new Vector2(0.11f, 0.11f), new Vector2(0.07f, 0.14f) }, 8, false, true, true);
            mb.Emission = 0f;
            mb.Color = Pal.Iron;
            mb.Cylinder(new Vector3(0f, 0.13f, 0f), 0.085f, 0.075f, 0.06f, 8);
            mb.Cone(new Vector3(0f, 0.19f, 0f), 0.08f, 0.06f, 8);
            mb.Cylinder(new Vector3(0f, -0.17f, 0f), 0.07f, 0.08f, 0.04f, 8);
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 90f + 45f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                WildRib(mb, new[] { d * 0.075f + Vector3.up * -0.15f, d * 0.145f + Vector3.up * -0.03f, d * 0.12f + Vector3.up * 0.11f }, 0.008f, 0.008f, 3);
            }
            mb.Pop();
            // paper charms and a reed bundle tied to the pole
            mb.WindGradient = true; mb.WindY0 = 2.2f; mb.WindY1 = 1.4f; mb.Wind = 1.6f;
            mb.Color = Pal.Vermilion;
            Slab(mb, new[] { new Vector3(0.22f, 2.24f, -0.02f), new Vector3(0.3f, 2.25f, -0.02f), new Vector3(0.31f, 1.92f, -0.03f), new Vector3(0.23f, 1.93f, -0.03f) }, Vector3.back, 0.008f);
            mb.Color = Pal.Paper;
            Slab(mb, new[] { new Vector3(0.36f, 2.26f, -0.02f), new Vector3(0.42f, 2.25f, -0.02f), new Vector3(0.43f, 2.0f, -0.03f), new Vector3(0.37f, 2.0f, -0.03f) }, Vector3.back, 0.008f);
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = FenReedTip;
            for (int k = 0; k < 5; k++)
                mb.Segment(new Vector3(-0.08f + k * 0.012f, 0.9f, -0.07f), new Vector3(-0.12f + k * 0.03f, 1.5f, -0.1f), 0.012f, 0.008f, 3, false, false);
            mb.Color = Pal.RopeStraw;
            mb.Torus(new Vector3(-0.07f, 1.15f, -0.04f), 0.075f, 0.016f, 6, 3);
            WildTuft(mb, new Vector3(0.18f, 0f, 0.12f), 0.5f, 5, FenReed, FenReedTip);
            return mb;
        }

        // ------------------------------------------------------------------ mire totem (≈ 2.9 m; collider 0.8 × 0.6): Mother Mire's coven

        static PropModel MireTotem(string art, int seed)
        {
            var m = Swaying(art, seed, BuildMireTotem, 0.4f);
            m.LightAnchors.Add(new Vector3(0f, 2.45f, -0.25f));
            return m;
        }

        static MeshBuilder BuildMireTotem(int b)
        {
            var mb = Builder(VariantSeed("prop_mire_totem", b), 0.07f, 0.3f, 0.5f);
            int sd = VariantSeed("miretotem", b);
            var drift = Paint.Hex("#6E6458");
            var witch = Paint.Hex("#7CF0A0");
            // a mud mound with mushrooms and reeds
            FacetBlob(mb, new Vector3(0f, 0.02f, 0f), new Vector3(0.5f, 0.2f, 0.4f), 1, 0.2f, sd, 0.5f, ByNormal(FenMoss, FenMud, Paint.Shade(FenMud, 0.8f)));
            LittleMushroom(mb, new Vector3(0.28f, 0.08f, -0.2f), 0.18f, 0.08f, Paint.Hex("#C9B26A"), -6f, 20f);
            LittleMushroom(mb, new Vector3(-0.3f, 0.06f, -0.14f), 0.12f, 0.06f, Paint.Hex("#C9B26A"), 12f, 60f);
            // a crooked, leaning driftwood post
            mb.Color = drift;
            var post = new[] { new Vector3(0f, 0f, 0.02f), new Vector3(0.06f, 0.9f, 0f), new Vector3(-0.04f, 1.7f, 0.03f), new Vector3(0.03f, 2.25f, 0f) };
            WildRib(mb, post, 0.12f, 0.085f, 6);
            mb.Color = Paint.Shade(drift, 0.8f);
            WildRib(mb, new[] { new Vector3(0.05f, 1.2f, 0f), new Vector3(0.32f, 1.45f, -0.02f), new Vector3(0.45f, 1.42f, -0.05f) }, 0.04f, 0.02f, 4);
            // mossy wraps and a tattered green rag
            mb.Color = FenMoss;
            mb.Torus(new Vector3(0.05f, 0.6f, 0.01f), 0.13f, 0.035f, 7, 3);
            mb.Torus(new Vector3(-0.02f, 1.55f, 0.02f), 0.1f, 0.03f, 7, 3);
            mb.WindGradient = true; mb.WindY0 = 1.9f; mb.WindY1 = 0.9f; mb.Wind = 1.5f;
            mb.Color = Paint.Hex("#3E5A44");
            Slab(mb, new[] { new Vector3(-0.08f, 1.85f, -0.1f), new Vector3(0.12f, 1.85f, -0.1f), new Vector3(0.16f, 1.2f, -0.13f), new Vector3(0.05f, 1.35f, -0.12f), new Vector3(-0.06f, 1.1f, -0.12f) },
                 Vector3.back, 0.012f);
            // herb bundles, a bone charm and glowing bottles hanging from the side branch
            for (int k = 0; k < 3; k++)
            {
                var hang = new Vector3(0.15f + k * 0.13f, 1.42f + (k == 1 ? 0.04f : 0f), -0.03f);
                mb.Color = Pal.RopeStraw;
                mb.Segment(hang, hang + Vector3.down * (0.18f + k * 0.05f), 0.008f, 0.008f, 3, false, false);
                var at = hang + Vector3.down * (0.2f + k * 0.05f);
                if (k == 0)
                {
                    mb.Color = Paint.Hex("#6E7A48");
                    for (int j = 0; j < 4; j++) mb.Segment(at, at + new Vector3((j - 1.5f) * 0.03f, -0.22f, 0f), 0.022f, 0.006f, 3, false, false);
                }
                else if (k == 1)
                {
                    WildBone(mb, at + new Vector3(-0.06f, -0.02f, 0f), at + new Vector3(0.06f, -0.06f, 0f), 0.016f, WildBoneCol);
                }
                else
                {
                    mb.Color = witch;
                    mb.Emission = 0.7f;
                    mb.Push().Translate(at + Vector3.down * 0.06f);
                    mb.Lathe(new[] { new Vector2(0.04f, -0.06f), new Vector2(0.05f, 0f), new Vector2(0.03f, 0.04f), new Vector2(0.015f, 0.07f) }, 6, false, true, true);
                    mb.Pop();
                    mb.Emission = 0f;
                }
            }
            mb.Color = witch;
            mb.Emission = 0.7f;
            mb.Push().Translate(-0.16f, 1.0f, -0.08f);
            mb.Lathe(new[] { new Vector2(0.035f, -0.05f), new Vector2(0.045f, 0f), new Vector2(0.025f, 0.035f), new Vector2(0.012f, 0.06f) }, 6, false, true, true);
            mb.Pop();
            mb.Emission = 0f;
            mb.Color = Pal.RopeStraw;
            mb.Segment(new Vector3(-0.08f, 1.12f, -0.06f), new Vector3(-0.16f, 1.07f, -0.08f), 0.008f, 0.008f, 3, false, false);
            mb.Wind = 0f; mb.WindGradient = false;
            // an antlered deer skull crowning the post, its sockets glowing witch-green
            var head = new Vector3(0.03f, 2.28f, -0.02f);
            WildSkull(mb, head, 1.1f, 0f, -14f, WildBoneCol);
            mb.Color = witch;
            mb.Emission = 0.9f;
            Ngon(mb, head + new Vector3(-0.071f, 0.18f, -0.1f), 0.028f, 6, new Vector3(-0.35f, 0.4f, -1f), Vector3.up, 0f, witch);
            Ngon(mb, head + new Vector3(0.071f, 0.18f, -0.1f), 0.028f, 6, new Vector3(0.35f, 0.4f, -1f), Vector3.up, 0f, witch);
            mb.Emission = 0f;
            mb.Color = Paint.Hex("#C9B89A");
            for (int s = -1; s <= 1; s += 2)
            {
                var a0 = head + new Vector3(s * 0.1f, 0.28f, 0.06f);
                var a1 = a0 + new Vector3(s * 0.18f, 0.2f, 0.06f);
                var a2 = a1 + new Vector3(s * 0.12f, 0.25f, 0.02f);
                var a3 = a2 + new Vector3(s * 0.02f, 0.18f, -0.04f);
                WildRib(mb, new[] { a0, a1, a2, a3 }, 0.035f, 0.012f, 4);
                WildRib(mb, new[] { a1, a1 + new Vector3(s * 0.02f, 0.18f, -0.08f) }, 0.022f, 0.008f, 4);
                WildRib(mb, new[] { a2, a2 + new Vector3(s * 0.14f, 0.08f, 0.02f) }, 0.018f, 0.007f, 4);
            }
            WildTuft(mb, new Vector3(-0.3f, 0f, 0.15f), 0.55f, 5, FenReed, FenReedTip);
            return mb;
        }

        // ------------------------------------------------------------------ sunken statue (≈ 1.9 m; collider 2.8 × 1.7)

        static MeshBuilder BuildSunkenStatue(int b)
        {
            var mb = Builder(VariantSeed("prop_sunken_statue", b), 0.07f, 0.4f, 0.6f);
            int sd = VariantSeed("sunken", b);
            var stone = Paint.Hsv(Paint.Hex("#9EA5A0"), (b - 1.5f) * 5f);
            var moss = FenMoss;
            var mossy = Mossy(stone, moss, 0.5f, Paint.Shade(stone, 0.75f));
            // wet mud ring
            FacetBlob(mb, new Vector3(0.1f, -0.06f, 0.05f), new Vector3(1.3f, 0.13f, 0.85f), 1, 0.15f, sd, 0.7f,
                      ByNormal(Color.Lerp(FenMud, FenMoss, 0.45f), Paint.Shade(FenMud, 0.9f), FenMud, 0.5f));
            WildTuft(mb, new Vector3(-1.05f, 0f, -0.35f), 0.5f, 6, FenReed, FenReedTip);
            WildTuft(mb, new Vector3(1.2f, 0f, -0.2f), 0.42f, 5, FenReed, FenReedTip);
            // a great shoulder sinking into the mire
            FacetBlob(mb, new Vector3(0.6f, 0.15f, 0.25f), new Vector3(0.75f, 0.55f, 0.6f), 1, 0.12f, sd + 1, 0.3f, mossy);
            // the serene head, tilted, half sunk
            mb.Push().Translate(-0.15f, 0.62f, 0.1f).Rotate(-8f, 12f, 16f);
            mb.Color = stone;
            mb.Lathe(new[]
            {
                new Vector2(0.42f, -0.5f), new Vector2(0.5f, -0.2f), new Vector2(0.54f, 0.15f), new Vector2(0.55f, 0.5f), new Vector2(0.5f, 0.8f),
                new Vector2(0.36f, 1.02f), new Vector2(0.14f, 1.12f),
            }, 10, false, true, true, new[] { Paint.Shade(stone, 0.8f), stone, stone, stone, Paint.Shade(stone, 1.05f), Color.Lerp(stone, moss, 0.6f), moss }, 18f);
            // topknot
            mb.Color = Color.Lerp(stone, moss, 0.5f);
            mb.Sphere(new Vector3(0f, 1.16f, 0.04f), new Vector3(0.2f, 0.16f, 0.2f), 8, 5, false);
            float fz = -0.53f;
            // brow, nose, closed eyes, lips
            mb.Color = Paint.Shade(stone, 1.06f);
            mb.Box(new Vector3(0f, 0.55f, fz - 0.02f), new Vector3(0.62f, 0.06f, 0.08f));
            mb.Push().Translate(0f, 0.53f, fz - 0.02f).Rotate(-90f, 0f, 0f).Rotate(0f, 0f, 0f);
            mb.Pop();
            Slab(mb, new[] { new Vector3(-0.07f, 0.24f, fz - 0.1f), new Vector3(0.07f, 0.24f, fz - 0.1f), new Vector3(0.025f, 0.52f, fz - 0.03f), new Vector3(-0.025f, 0.52f, fz - 0.03f) },
                 new Vector3(0f, 0.3f, -1f), 0.09f);
            mb.Color = Paint.Shade(stone, 0.55f);
            for (int s = -1; s <= 1; s += 2)
            {
                var c = new Vector3(s * 0.17f, 0.44f, fz - 0.025f);
                mb.Segment(c + new Vector3(-0.09f, 0.015f, 0f), c, 0.014f, 0.014f, 3, false, true);
                mb.Segment(c, c + new Vector3(0.09f, 0.015f, 0f), 0.014f, 0.014f, 3, false, true);
            }
            mb.Color = Paint.Shade(stone, 0.85f);
            mb.Box(new Vector3(0f, 0.12f, fz - 0.02f), new Vector3(0.18f, 0.05f, 0.05f));
            // long ear lobes
            mb.Color = stone;
            for (int s = -1; s <= 1; s += 2)
                mb.Sphere(new Vector3(s * 0.54f, 0.3f, -0.05f), new Vector3(0.08f, 0.32f, 0.14f), 6, 4, false);
            // moss caps
            FacetBlob(mb, new Vector3(0.05f, 0.98f, -0.05f), new Vector3(0.38f, 0.12f, 0.36f), 0, 0.2f, sd + 3, 0f, ByNormal(Pal.MossLight, moss, moss));
            mb.Pop();
            // a stone hand rising from the water, an old lantern hanging from its fingers
            var wrist = new Vector3(-1.05f, 1.05f, -0.1f);
            mb.Color = stone;
            mb.Segment(new Vector3(-1.0f, -0.1f, 0.0f), wrist, 0.17f, 0.14f, 7);
            FacetBlob(mb, wrist + new Vector3(0f, 0.16f, -0.02f), new Vector3(0.17f, 0.2f, 0.14f), 1, 0.1f, sd + 5, 0f, mossy);
            for (int k = 0; k < 4; k++)
            {
                var f0 = wrist + new Vector3(-0.09f + k * 0.06f, 0.3f, -0.08f);
                mb.Segment(f0, f0 + new Vector3(0.01f, 0.08f, -0.08f), 0.035f, 0.03f, 4);
            }
            mb.Color = Pal.Iron;
            var hook = wrist + new Vector3(0.0f, 0.33f, -0.17f);
            mb.Segment(hook, hook + Vector3.down * 0.22f, 0.01f, 0.01f, 3);
            mb.Push().Translate(hook + Vector3.down * 0.42f);
            mb.Color = Pal.Iron;
            mb.Cone(new Vector3(0f, 0.15f, 0f), 0.13f, 0.08f, 6);
            mb.Box(new Vector3(0f, -0.15f, 0f), new Vector3(0.18f, 0.04f, 0.18f));
            mb.Color = Paint.Hex("#5E6E66");
            mb.Emission = 0.15f;
            mb.Box(Vector3.zero, new Vector3(0.14f, 0.26f, 0.14f));
            mb.Emission = 0f;
            mb.Pop();
            // reeds and a few lily-like weeds
            WildTuft(mb, new Vector3(-1.3f, 0f, -0.35f), 0.7f, 6, FenReed, FenReedTip);
            WildTuft(mb, new Vector3(1.1f, 0f, -0.3f), 0.55f, 5, FenReed, FenReedTip);
            return mb;
        }

        // ------------------------------------------------------------------ giant mushroom (≈ 4.2 m, a half-size one beside it; collider 1.9 × 1.1): glowing gills

        static PropModel MushroomGiant(string art, int seed)
        {
            var m = Simple(art, seed, BuildMushroomGiant, 0.6f);
            m.LightAnchors.Add(new Vector3(0.2f, 2.45f, -0.4f));
            return m;
        }

        static MeshBuilder BuildMushroomGiant(int b)
        {
            var mb = Builder(VariantSeed("prop_mushroom_giant", b), 0.05f, 0.3f, 0.8f);
            Color cap = new[] { Paint.Hex("#D2584A"), Paint.Hex("#4E8FA8"), Paint.Hex("#E0913E"), Paint.Hex("#8E6BC0") }[b];
            Color glow = new[] { Paint.Hex("#86F2D8"), Paint.Hex("#9EEBFF"), Paint.Hex("#FFD87A"), Paint.Hex("#F2A8E8") }[b];
            var stemCol = Paint.Hex("#EFE4CC");
            // a younger one leaning out from the foot (half size), then the great one
            mb.Push().Translate(-0.85f, 0f, -0.1f).Rotate(4f, 140f + b * 20f, 16f).Scale(0.48f);
            GiantShroom(mb, b, cap, glow, stemCol);
            mb.Pop();
            GiantShroom(mb, b, cap, glow, stemCol);
            // little ones at the foot
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 85f + b * 30f + 200f) * Mathf.Deg2Rad;
                LittleMushroom(mb, new Vector3(Mathf.Cos(a) * 0.62f, 0f, Mathf.Sin(a) * 0.48f), 0.22f + 0.1f * (k % 2), 0.11f + 0.04f * (k % 2), cap, (k - 1.5f) * 9f, k * 50f, 0.35f);
            }
            WildTuft(mb, new Vector3(0.5f, 0f, -0.45f), 0.4f, 5, FenReed, FenReedTip);
            return mb;
        }

        static void GiantShroom(MeshBuilder mb, int b, Color cap, Color glow, Color stemCol)
        {
            // a gently curving stem with a flared foot and a skirt
            var s0 = new Vector3(0f, 0f, 0f); var s1 = new Vector3(0.08f, 1.0f, 0.02f); var s2 = new Vector3(0.18f, 2.0f, 0f); var s3 = new Vector3(0.22f, 2.75f, -0.02f);
            mb.Color = Paint.Shade(stemCol, 0.92f);
            mb.Segment(s0 + Vector3.down * 0.05f, s0 + Vector3.up * 0.35f, 0.52f, 0.32f, 9, true, true);
            mb.Color = stemCol;
            mb.Segment(s0 + Vector3.up * 0.3f, s1, 0.33f, 0.28f, 9, true, false);
            mb.Segment(s1, s2, 0.28f, 0.25f, 9, true, false);
            mb.Segment(s2, s3, 0.25f, 0.27f, 9, true, false);
            mb.Push().Translate(Vector3.Lerp(s2, s3, 0.25f));
            mb.Color = Paint.Shade(stemCol, 1.03f);
            mb.Lathe(new[] { new Vector2(0.27f, 0f), new Vector2(0.44f, -0.16f), new Vector2(0.4f, -0.2f), new Vector2(0.25f, -0.06f) }, 9, true, false, false);
            mb.Pop();
            // the cap (tilted a little), glowing gills underneath
            mb.Push().Translate(s3 + Vector3.up * 0.08f).Rotate(-6f, 0f, -7f);
            mb.Emission = 0.75f;
            mb.Color = glow;
            mb.Lathe(new[] { new Vector2(0.24f, 0f), new Vector2(1.82f, 0.3f) }, 14, false, false, false);
            mb.Emission = 0.4f;
            mb.Color = Color.Lerp(glow, cap, 0.5f);
            for (int k = 0; k < 14; k++)
            {
                float a = (k + 0.5f) * Mathf.PI * 2f / 14f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mb.Segment(d * 0.32f + Vector3.up * 0.01f, d * 1.72f + Vector3.up * 0.27f, 0.018f, 0.018f, 3, false, false);
            }
            mb.Emission = 0f;
            mb.Color = cap;
            var capTop = Paint.Shade(cap, 1.12f);
            mb.Lathe(new[]
            {
                new Vector2(1.82f, 0.3f), new Vector2(1.92f, 0.42f), new Vector2(1.78f, 0.78f), new Vector2(1.3f, 1.08f), new Vector2(0.66f, 1.24f), new Vector2(0f, 1.28f),
            }, 14, true, false, false, new[] { Paint.Shade(cap, 0.85f), cap, cap, capTop, capTop, Paint.Shade(capTop, 1.04f) });
            // pale spots
            mb.Color = Paint.Hex("#F6EEDC");
            mb.Emission = 0.12f;
            for (int k = 0; k < 9; k++)
            {
                float a = (k * 61f + b * 23f) * Mathf.Deg2Rad, rr = k < 5 ? 1.35f : 0.75f, y = k < 5 ? 0.98f : 1.2f;
                var n = new Vector3(Mathf.Cos(a) * (k < 5 ? 0.6f : 0.3f), 1f, Mathf.Sin(a) * (k < 5 ? 0.6f : 0.3f)).normalized;
                Ngon(mb, new Vector3(Mathf.Cos(a) * rr, y + 0.025f, Mathf.Sin(a) * rr), k < 5 ? 0.17f : 0.12f, 6, n, Vector3.forward, k * 20f);
            }
            mb.Emission = 0f;
            mb.Pop();
        }

        // ------------------------------------------------------------------ fishing rack (≈ 2.0 m; collider 2.4 × 0.7)

        static MeshBuilder BuildFishingRack(int b)
        {
            var mb = Builder(VariantSeed("prop_fishing_rack", b), 0.07f, 0.3f, 0.5f);
            int sd = VariantSeed("fishrack", b);
            var pole = Paint.Hex("#8A7A66");
            // two A-frame trestles and a ridge pole
            for (int s = -1; s <= 1; s += 2)
            {
                var top = new Vector3(s * 1.05f, 1.8f, 0f);
                mb.Color = pole;
                mb.Segment(new Vector3(s * 1.0f, -0.05f, -0.32f), top + new Vector3(0f, 0.15f, 0.08f), 0.045f, 0.035f, 5);
                mb.Segment(new Vector3(s * 1.1f, -0.05f, 0.32f), top + new Vector3(0f, 0.15f, -0.08f), 0.045f, 0.035f, 5);
                mb.Color = Pal.RopeStraw;
                mb.Torus(top, 0.06f, 0.018f, 6, 3);
            }
            mb.Color = Paint.Shade(pole, 1.08f);
            mb.Segment(new Vector3(-1.3f, 1.84f, 0f), new Vector3(1.3f, 1.8f, 0f), 0.04f, 0.04f, 5);
            mb.Color = pole;
            mb.Segment(new Vector3(-1.05f, 0.75f, -0.18f), new Vector3(1.05f, 0.75f, -0.18f), 0.03f, 0.03f, 5);
            // fish hanging by their tails (sway a little)
            mb.WindGradient = true; mb.WindY0 = 1.8f; mb.WindY1 = 1.0f; mb.Wind = 0.8f;
            Color[] fish = { Paint.Hex("#9AA6A0"), Paint.Hex("#B5AE8E"), Paint.Hex("#8E9E8A") };
            for (int i = 0; i < 8; i++)
            {
                float x = -0.85f + i * 0.243f + WildHash(i, 1, sd) * 0.03f;
                float len = 0.34f + 0.1f * (WildHash(i, 2, sd) * 0.5f + 0.5f);
                var hang = new Vector3(x, 1.8f, 0f);
                mb.Color = Pal.RopeStraw;
                mb.Segment(hang, hang + Vector3.down * 0.1f, 0.006f, 0.006f, 3, false, false);
                var tail = hang + Vector3.down * 0.1f;
                mb.Color = fish[i % 3];
                Tri(mb, tail + new Vector3(-0.06f, 0f, 0f), tail + new Vector3(0.06f, 0f, 0f), tail + Vector3.down * 0.08f, Vector3.back, Paint.Shade(fish[i % 3], 0.85f));
                Tri(mb, tail + new Vector3(-0.06f, 0f, 0f), tail + Vector3.down * 0.08f, tail + new Vector3(0.06f, 0f, 0f), Vector3.forward, Paint.Shade(fish[i % 3], 0.85f));
                mb.Sphere(tail + Vector3.down * (0.08f + len * 0.5f), new Vector3(0.055f, len * 0.5f, 0.03f), 6, 5, false);
                mb.Color = Paint.Shade(fish[i % 3], 0.6f);
                mb.Box(tail + Vector3.down * (0.08f + len * 0.5f) + new Vector3(0f, 0f, 0.02f), new Vector3(0.02f, len * 0.8f, 0.02f));
            }
            mb.Wind = 0f; mb.WindGradient = false;
            // a net draped over the left trestle with cork floats
            mb.Color = Paint.Hex("#6E7A6A");
            Slab(mb, new[] { new Vector3(-1.32f, 1.78f, -0.05f), new Vector3(-0.9f, 1.82f, -0.05f), new Vector3(-0.92f, 0.65f, -0.32f), new Vector3(-1.3f, 0.5f, -0.3f) },
                 new Vector3(0f, 0.3f, -1f), 0.012f);
            mb.Color = Paint.Shade(Paint.Hex("#6E7A6A"), 0.7f);
            for (int k = 0; k < 4; k++)
            {
                float t = (k + 0.5f) / 4f;
                mb.Segment(Vector3.Lerp(new Vector3(-1.32f, 1.78f, -0.07f), new Vector3(-1.3f, 0.5f, -0.32f), t), Vector3.Lerp(new Vector3(-0.9f, 1.82f, -0.07f), new Vector3(-0.92f, 0.65f, -0.34f), t), 0.008f, 0.008f, 3, false, false);
            }
            mb.Color = Paint.Hex("#C9A06A");
            for (int k = 0; k < 3; k++) Gem(mb, new Vector3(-1.25f + k * 0.15f, 0.56f + k * 0.04f, -0.34f), 0.045f);
            // a creel basket and a stool
            Basket(mb, new Vector3(0.55f, 0f, -0.42f), 0.22f, Paint.Hex("#9AA6A0"), b);
            mb.Color = Pal.WoodLight;
            mb.Cylinder(new Vector3(-0.35f, 0.38f, -0.4f), 0.18f, 0.18f, 0.06f, 8);
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2f / 3f;
                Beam(mb, new Vector3(-0.35f + Mathf.Cos(a) * 0.12f, 0.38f, -0.4f + Mathf.Sin(a) * 0.12f), new Vector3(-0.35f + Mathf.Cos(a) * 0.18f, 0f, -0.4f + Mathf.Sin(a) * 0.18f), 0.04f, 0.04f);
            }
            WildTuft(mb, new Vector3(1.15f, 0f, -0.2f), 0.5f, 5, FenReed, FenReedTip);
            return mb;
        }
    }
}
