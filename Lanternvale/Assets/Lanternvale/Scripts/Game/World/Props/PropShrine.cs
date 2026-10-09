// The Old Lantern Shrine and the wild: spirit lanterns (stone tōrō; SetLit rekindles them), the vermilion shrine gate,
// the kitsune statue, blight crystals, ruins, and the camp (campfire with flickering flames, travel tent).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterShrine(Dictionary<string, Recipe> r)
        {
            r["prop_spirit_lantern"] = SpiritLantern;
            r["prop_spirit_lantern_dark"] = SpiritLantern;
            r["prop_shrine_gate"] = (art, seed) => Simple(art, seed, BuildShrineGate, 1.9f);
            r["prop_spirit_statue"] = (art, seed) => Simple(art, seed, BuildSpiritStatue, 0.4f);
            r["prop_blight_crystal"] = BlightCrystal;
            r["prop_ruin_pillar"] = (art, seed) => Simple(art, seed, BuildRuinPillar, 0.5f);
            r["prop_ruin_arch"] = (art, seed) => Simple(art, seed, BuildRuinArch, 2.0f);
            r["prop_campfire"] = Campfire;
            r["prop_tent"] = Tent;
        }

        // ------------------------------------------------------------------ spirit lantern (tōrō; collider 0.9 × 0.5)

        const float ToroZ = 0.06f, ToroBoxY0 = 1.24f, ToroBoxY1 = 1.82f;

        /// <summary>prop_spirit_lantern (lit) and prop_spirit_lantern_dark (dark, blighted): one model, SetLit swaps the mesh.</summary>
        static PropModel SpiritLantern(string art, int seed)
        {
            int b = Bucket(seed);
            bool startLit = !art.EndsWith("_dark", System.StringComparison.Ordinal);
            string key = "prop_spirit_lantern#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, startLit, lit => BuildToro(b, lit)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => BuildToro(b, lit));
            rig.Light(new Vector3(0f, (ToroBoxY0 + ToroBoxY1) * 0.5f, ToroZ));
            return rig.Done(0.4f);
        }

        static MeshBuilder BuildToro(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_spirit_lantern", b), 0.07f, 0.3f, 0.4f);
            var stone = lit ? Color.Lerp(Pal.Stone, Pal.StoneLight, 0.25f) : Color.Lerp(Pal.StoneCool, Pal.Blight, 0.45f);
            var stoneDark = Paint.Shade(stone, 0.85f);
            var moss = lit ? Pal.Moss : Color.Lerp(Pal.Moss, Pal.Blight, 0.5f);
            mb.Push().Translate(0f, 0f, ToroZ);
            // kiso (hexagonal base), sao (pillar), chudai (platform)
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.33f, 0f), new Vector2(0.33f, 0.14f), new Vector2(0.27f, 0.2f), new Vector2(0.19f, 0.26f) }, 6, false, false, true,
                     new[] { stoneDark, stone, stone, Paint.Shade(stone, 1.05f) });
            mb.Lathe(new[] { new Vector2(0.13f, 0.26f), new Vector2(0.12f, 0.62f), new Vector2(0.12f, 0.62f), new Vector2(0.14f, 0.66f), new Vector2(0.14f, 0.7f), new Vector2(0.12f, 0.74f), new Vector2(0.12f, 0.74f), new Vector2(0.11f, 1.05f) },
                     6, false, false, false, new[] { stone, stone, stoneDark, stoneDark, stoneDark, stoneDark, stone, stone });
            mb.Lathe(new[] { new Vector2(0.11f, 1.04f), new Vector2(0.29f, 1.12f), new Vector2(0.29f, 1.19f), new Vector2(0.21f, ToroBoxY0) }, 6, false, true, true,
                     new[] { stoneDark, stone, stone, Paint.Shade(stone, 1.05f) });
            // hibukuro (fire box): stone frame, paper panels glowing honey-gold when lit
            mb.Lathe(new[] { new Vector2(0.21f, ToroBoxY0), new Vector2(0.21f, ToroBoxY1) }, 6, false, false, false);
            float ap = 0.21f * Mathf.Cos(30f * Mathf.Deg2Rad);
            for (int f = 0; f < 6; f++)
            {
                float a = (270f + f * 60f) * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var t = new Vector3(-n.z, 0f, n.x);
                var c = n * (ap + 0.004f) + Vector3.up * ((ToroBoxY0 + ToroBoxY1) * 0.5f);
                float hw = 0.075f, hh = 0.2f;
                bool window = f == 0 || f == 1 || f == 5 || f == 3;
                Color col = lit ? (f == 0 ? Pal.Glow : Color.Lerp(Pal.Glow, Pal.GlowDeep, 0.4f)) : (window ? Paint.Hex("#4A4258") : stoneDark);
                mb.Emission = lit && window ? 1f : 0f;
                if (window || lit) QuadC(mb, c - t * hw - Vector3.up * hh, c - t * hw + Vector3.up * hh, c + t * hw + Vector3.up * hh, c + t * hw - Vector3.up * hh, n, col);
                if (f == 0)
                {
                    // round "moon" cut-out on the front panel; a faint violet ember when dark
                    mb.Emission = lit ? 1f : 0.7f;
                    Ngon(mb, c + n * 0.004f + Vector3.up * 0.06f, lit ? 0.06f : 0.04f, 8, n, Vector3.up, 0f, lit ? Paint.Hex("#FFF2C4") : Pal.VioletDeep);
                }
                mb.Emission = 0f;
            }
            // kasa (curled hexagonal roof) with upturned corners and an onion finial
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.22f, ToroBoxY1), new Vector2(0.46f, ToroBoxY1 + 0.05f), new Vector2(0.48f, ToroBoxY1 + 0.1f), new Vector2(0.38f, ToroBoxY1 + 0.17f), new Vector2(0.17f, ToroBoxY1 + 0.29f), new Vector2(0.08f, ToroBoxY1 + 0.32f) },
                     6, false, true, true, new[] { stoneDark, stoneDark, stone, stone, Paint.Shade(stone, 1.06f), Paint.Shade(stone, 1.06f) }, 0f);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                var corner = new Vector3(Mathf.Cos(a) * 0.47f, ToroBoxY1 + 0.08f, Mathf.Sin(a) * 0.47f);
                mb.Segment(corner, corner + new Vector3(Mathf.Cos(a) * 0.07f, 0.1f, Mathf.Sin(a) * 0.07f), 0.035f, 0.015f, 4);
            }
            mb.Lathe(new[] { new Vector2(0.06f, ToroBoxY1 + 0.31f), new Vector2(0.07f, ToroBoxY1 + 0.35f), new Vector2(0.1f, ToroBoxY1 + 0.42f), new Vector2(0.075f, ToroBoxY1 + 0.5f), new Vector2(0f, ToroBoxY1 + 0.6f) },
                     6, false, false, false);
            // moss
            FacetBlob(mb, new Vector3(0.12f, ToroBoxY1 + 0.2f, 0.1f), new Vector3(0.18f, 0.05f, 0.14f), 0, 0.25f, b + 1, 0f, ByNormal(Paint.Shade(moss, 1.15f), moss, moss));
            FacetBlob(mb, new Vector3(-0.2f, 0.2f, 0.12f), new Vector3(0.14f, 0.06f, 0.12f), 0, 0.25f, b + 2, 0f, ByNormal(Paint.Shade(moss, 1.15f), moss, moss));
            if (!lit)
            {
                // blighted: cracks and small violet crystals at the foot
                mb.Color = Pal.BlightDark;
                Beam(mb, new Vector3(-0.03f, 0.5f, -0.123f), new Vector3(0.04f, 0.78f, -0.122f), 0.018f, 0.01f, Vector3.back);
                Beam(mb, new Vector3(0.04f, 0.78f, -0.122f), new Vector3(0f, 0.95f, -0.118f), 0.015f, 0.01f, Vector3.back);
                mb.Emission = 0.55f;
                for (int i = 0; i < 3; i++)
                {
                    float a = (200f + i * 65f + b * 20f) * Mathf.Deg2Rad;
                    mb.Color = i == 1 ? Pal.Violet : Paint.Hsv(Pal.Violet, -10f, 1.1f, 0.9f);
                    mb.Push().Translate(Mathf.Cos(a) * 0.27f, 0.12f, Mathf.Sin(a) * 0.16f + 0.04f).Rotate(Mathf.Sin(a) * 25f, i * 40f, -Mathf.Cos(a) * 25f);
                    float h = 0.16f + i % 2 * 0.08f;
                    mb.Lathe(new[] { new Vector2(0.04f, 0f), new Vector2(0.045f, h * 0.7f), new Vector2(0f, h) }, 5, false, true, false);
                    mb.Pop();
                }
                mb.Emission = 0f;
            }
            mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ shrine gate (torii, vermilion; no collider)

        static MeshBuilder BuildShrineGate(int b)
        {
            var mb = Builder(VariantSeed("prop_shrine_gate", b), 0.05f, 0.3f, 0.6f);
            var red = Paint.Hsv(Pal.Vermilion, (b - 1.5f) * 3f);
            var black = Paint.Hex("#2E2A30");
            const float px = 1.65f, top = 5.05f;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push().Translate(s * px, 0f, 0f).Rotate(0f, 0f, s * 1.6f);
                mb.Color = red;
                mb.Lathe(new[] { new Vector2(0.2f, 0f), new Vector2(0.175f, top) }, 10, false, false, true);
                mb.Color = black;
                mb.Lathe(new[] { new Vector2(0.25f, 0f), new Vector2(0.24f, 0.5f), new Vector2(0.21f, 0.56f) }, 10, false, false, true);
                mb.Lathe(new[] { new Vector2(0.2f, top - 0.18f), new Vector2(0.2f, top - 0.02f) }, 10, false, true, false);
                mb.Pop();
                FacetBlob(mb, new Vector3(s * px - 0.12f, 0.04f, -0.15f), new Vector3(0.22f, 0.08f, 0.14f), 0, 0.25f, b + s + 3, 0.3f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
                Tuft(mb, new Vector3(s * (px + 0.3f), 0f, -0.1f), 0.3f, 5, Pal.Sage);
            }
            // nuki (tie beam), gakuzuka (strut) with the plaque, shimaki
            mb.Color = red;
            mb.Box(new Vector3(0f, 4.15f, 0f), new Vector3(4.6f, 0.24f, 0.17f));
            mb.Box(new Vector3(0f, 4.62f, 0f), new Vector3(0.18f, 0.72f, 0.13f));
            mb.Box(new Vector3(0f, 5.16f, 0f), new Vector3(5.1f, 0.26f, 0.3f));
            mb.Color = black;
            Slab(mb, new[] { new Vector3(-0.24f, 4.3f, -0.08f), new Vector3(-0.24f, 4.92f, -0.08f), new Vector3(0.24f, 4.92f, -0.08f), new Vector3(0.24f, 4.3f, -0.08f) }, Vector3.back, 0.05f);
            mb.Color = Pal.Gold;
            Slab(mb, new[] { new Vector3(-0.2f, 4.34f, -0.085f), new Vector3(-0.2f, 4.88f, -0.085f), new Vector3(0.2f, 4.88f, -0.085f), new Vector3(0.2f, 4.34f, -0.085f) }, Vector3.back, 0.004f);
            mb.Color = black;
            Slab(mb, new[] { new Vector3(-0.16f, 4.38f, -0.09f), new Vector3(-0.16f, 4.84f, -0.09f), new Vector3(0.16f, 4.84f, -0.09f), new Vector3(0.16f, 4.38f, -0.09f) }, Vector3.back, 0.004f);
            mb.Color = Pal.Gold;
            mb.Box(new Vector3(0f, 4.72f, -0.095f), new Vector3(0.05f, 0.18f, 0.005f));
            mb.Box(new Vector3(0f, 4.5f, -0.095f), new Vector3(0.18f, 0.04f, 0.005f));
            // kasagi: the curved top beam with upturned ends (vermilion under a black cap)
            const int seg = 12;
            const float half = 2.95f;
            var lower = new List<Vector3[]>();
            var cap = new List<Vector3[]>();
            for (int i = 0; i <= seg; i++)
            {
                float x = -half + 2f * half * i / seg;
                float t = Mathf.Abs(x) / half;
                float y = 5.32f + 0.26f * Mathf.Pow(t, 2.6f);
                float lift = Mathf.Pow(t, 3f) * 0.08f;
                lower.Add(new[] { new Vector3(x, y, -0.2f), new Vector3(x, y + 0.2f + lift, -0.2f), new Vector3(x, y + 0.2f + lift, 0.2f), new Vector3(x, y, 0.2f) });
                cap.Add(new[] { new Vector3(x, y + 0.2f + lift, -0.24f), new Vector3(x, y + 0.32f + lift * 1.5f, -0.24f), new Vector3(x, y + 0.32f + lift * 1.5f, 0.24f), new Vector3(x, y + 0.2f + lift, 0.24f) });
            }
            mb.Color = red;
            Loft(mb, lower);
            mb.Color = black;
            Loft(mb, cap);
            // shimenawa between the pillars with shide and tassels
            var ra = new Vector3(-px + 0.05f, 3.8f, -0.18f);
            var rb = new Vector3(px - 0.05f, 3.8f, -0.18f);
            mb.Color = Pal.RopeStraw;
            Rope(mb, ra, rb, 0.3f, 0.08f, 10, 6);
            for (int i = 0; i < 3; i++)
            {
                float t = 0.3f + i * 0.2f;
                Shide(mb, SagPoint(ra, rb, 0.3f, t) + Vector3.down * 0.06f + Vector3.back * 0.06f, Vector3.right, Vector3.back, 0.48f);
            }
            mb.Color = Pal.RopeStraw;
            foreach (float t in new[] { 0.2f, 0.8f })
            {
                var p = SagPoint(ra, rb, 0.3f, t);
                mb.Cylinder(p + Vector3.down * 0.38f, 0.07f, 0.04f, 0.32f, 5);
            }
            return mb;
        }

        // ------------------------------------------------------------------ kitsune spirit statue (collider 0.8 × 0.5)

        static MeshBuilder BuildSpiritStatue(int b)
        {
            var mb = Builder(VariantSeed("prop_spirit_statue", b), 0.08f, 0.3f, 0.5f);
            var stone = Paint.Hsv(Pal.StoneLight, (b - 1.5f) * 4f, 0.85f);
            var stoneDark = Paint.Shade(stone, 0.86f);
            // pedestal
            mb.Color = stoneDark;
            mb.BoxOn(new Vector3(0f, 0f, 0.15f), new Vector3(0.62f, 0.2f, 0.58f), stone);
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0.2f, 0.17f), new Vector3(0.5f, 0.5f, 0.48f));
            mb.Color = stoneDark;
            mb.BoxOn(new Vector3(0f, 0.7f, 0.17f), new Vector3(0.56f, 0.05f, 0.54f));
            FacetBlob(mb, new Vector3(-0.18f, 0.74f, 0.3f), new Vector3(0.14f, 0.04f, 0.14f), 0, 0.25f, b + 2, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            FacetBlob(mb, new Vector3(0.22f, 0.21f, -0.05f), new Vector3(0.12f, 0.05f, 0.1f), 0, 0.25f, b + 4, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            // the fox, sitting, facing the camera
            mb.Color = stone;
            mb.Push().Translate(0f, 0.75f, 0.24f).Rotate(-8f, 0f, 0f);
            mb.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.19f, 0.03f), new Vector2(0.22f, 0.18f), new Vector2(0.18f, 0.38f), new Vector2(0.11f, 0.53f), new Vector2(0f, 0.58f) }, 8, false, false, false);
            mb.Pop();
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Segment(new Vector3(s * 0.08f, 1.08f, 0.08f), new Vector3(s * 0.07f, 0.76f, 0.02f), 0.05f, 0.04f, 5);
                mb.Sphere(new Vector3(s * 0.07f, 0.77f, 0.0f), new Vector3(0.05f, 0.03f, 0.06f), 5, 3, false);
                // ears
                mb.Push().Translate(s * 0.075f, 1.46f, 0.12f).Rotate(0f, 0f, -s * 14f);
                mb.Color = stone;
                mb.Cone(Vector3.zero, 0.055f, 0.19f, 4);
                mb.Pop();
            }
            mb.Segment(new Vector3(0f, 1.05f, 0.14f), new Vector3(0f, 1.3f, 0.1f), 0.1f, 0.08f, 6);
            mb.Sphere(new Vector3(0f, 1.38f, 0.09f), new Vector3(0.13f, 0.12f, 0.13f), 7, 5, false);
            mb.Push().Translate(0f, 1.34f, 0.0f).Rotate(-90f, 0f, 0f);
            mb.Cone(Vector3.zero, 0.07f, 0.19f, 6);
            mb.Pop();
            mb.Color = Pal.Ink;
            Gem(mb, new Vector3(0f, 1.34f, -0.19f), 0.022f);
            for (int s = -1; s <= 1; s += 2)
                OBox(mb, new Vector3(s * 0.055f, 1.41f, -0.025f), new Vector3(0.05f, 0.016f, 0.02f), Quaternion.Euler(0f, 0f, s * 18f));
            // bushy tail curling up the side, pale tip
            Vector3[] tail = { new Vector3(0.13f, 0.8f, 0.42f), new Vector3(0.27f, 0.93f, 0.46f), new Vector3(0.31f, 1.16f, 0.38f), new Vector3(0.25f, 1.38f, 0.3f), new Vector3(0.17f, 1.52f, 0.25f) };
            float[] tr = { 0.08f, 0.12f, 0.13f, 0.1f, 0.04f };
            for (int i = 0; i < tail.Length - 1; i++)
            {
                mb.Color = i == tail.Length - 2 ? Paint.Hex("#ECE7DC") : stone;
                mb.Segment(tail[i], tail[i + 1], tr[i], tr[i + 1], 6, false, true);
            }
            // red cloth bib (yodarekake)
            mb.Color = Paint.Hex("#C8402E");
            Slab(mb, new[] { new Vector3(-0.15f, 1.23f, -0.05f), new Vector3(0.15f, 1.23f, -0.05f), new Vector3(0.1f, 1.08f, -0.11f), new Vector3(0f, 0.98f, -0.13f), new Vector3(-0.1f, 1.08f, -0.11f) },
                 new Vector3(0f, -0.35f, -1f), 0.02f);
            mb.Color = Pal.Gold;
            Gem(mb, new Vector3(0f, 1.2f, -0.08f), 0.025f);
            return mb;
        }

        // ------------------------------------------------------------------ blight crystals (violet glow; collider 1.2 × 0.7)

        static PropModel BlightCrystal(string art, int seed)
        {
            var m = Simple(art, seed, BuildBlightCrystal, 0.6f);
            m.LightAnchors.Add(new Vector3(0f, 1.0f, 0.1f));
            return m;
        }

        static void Crystal(MeshBuilder mb, Vector3 at, float h, float r, float tiltX, float tiltZ, float yaw, Color body, Color tip)
        {
            mb.Push().Translate(at).Rotate(tiltX, yaw, tiltZ);
            mb.Emission = 0.3f;
            mb.Color = body;
            mb.Lathe(new[] { new Vector2(r * 0.85f, -0.1f), new Vector2(r, h * 0.68f) }, 6, false, true, false, new[] { Paint.Shade(body, 0.75f), body }, yaw);
            mb.Emission = 0.6f;
            mb.Color = tip;
            mb.Lathe(new[] { new Vector2(r, h * 0.68f), new Vector2(0f, h) }, 6, false, false, false, null, yaw);
            mb.Emission = 0f;
            mb.Pop();
        }

        static MeshBuilder BuildBlightCrystal(int b)
        {
            var mb = Builder(VariantSeed("prop_blight_crystal", b), 0.08f, 0.25f, 0.4f);
            FacetBlob(mb, new Vector3(0f, 0.12f, 0.1f), new Vector3(0.55f, 0.24f, 0.32f), 1, 0.2f, b + 9, 0.5f, ByNormal(Paint.Shade(Pal.Blight, 1.05f), Pal.BlightDark, Pal.BlightDark));
            var body = Color.Lerp(Pal.Blight, Pal.Violet, 0.4f);
            var tip = Paint.Hsv(Pal.Violet, (b - 1.5f) * 5f);
            (Vector3 p, float h, float r, float tx, float tz)[] cs =
            {
                (new Vector3(0f, 0.1f, 0.12f), 1.9f, 0.2f, -5f, 4f), (new Vector3(-0.28f, 0.08f, 0.06f), 1.25f, 0.16f, 8f, 22f), (new Vector3(0.3f, 0.08f, 0.14f), 1.4f, 0.17f, -6f, -20f),
                (new Vector3(0.1f, 0.06f, -0.12f), 0.85f, 0.13f, -22f, -10f), (new Vector3(-0.14f, 0.06f, 0.32f), 1.0f, 0.13f, 16f, 8f), (new Vector3(0.44f, 0.04f, -0.02f), 0.6f, 0.1f, -10f, -35f),
                (new Vector3(-0.45f, 0.04f, -0.06f), 0.55f, 0.1f, -14f, 35f),
            };
            for (int i = 0; i < cs.Length; i++)
                Crystal(mb, cs[i].p, cs[i].h * (1f + (b - 1.5f) * 0.05f), cs[i].r, cs[i].tx, cs[i].tz, i * 23f + b * 9f, i % 2 == 0 ? body : Paint.Shade(body, 0.9f), tip);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 110f + 30f + b * 20f) * Mathf.Deg2Rad;
                Crystal(mb, new Vector3(Mathf.Cos(a) * 0.5f, 0.0f, Mathf.Sin(a) * 0.22f + 0.1f), 0.32f, 0.06f, Mathf.Sin(a) * 50f, -Mathf.Cos(a) * 50f, i * 40f, body, tip);
            }
            return mb;
        }

        // ------------------------------------------------------------------ ruins

        /// <summary>Fluted column from y0 with a jagged broken top (heights per facet).</summary>
        static void BrokenColumn(MeshBuilder mb, Vector3 c, float r, int sides, float y0, float[] tops)
        {
            int n = sides * 2;
            Vector3 P(int j, float y)
            {
                float a = j * Mathf.PI * 2f / n;
                float rr = j % 2 == 0 ? r : r * 0.88f;
                return c + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
            }
            float avg = 0f;
            foreach (float t in tops) avg += t;
            avg /= tops.Length;
            var center = c + new Vector3(0f, avg - 0.08f, 0f);
            for (int j = 0; j < n; j++)
            {
                int k = (j + 1) % n;
                float ta = tops[j % tops.Length], tb = tops[k % tops.Length];
                var a0 = P(j, y0); var b0 = P(k, y0); var b1 = P(k, tb); var a1 = P(j, ta);
                var mid = (a0 + b0) * 0.5f - c;
                mb.Quad(a0, b0, b1, a1, new Vector3(mid.x, 0f, mid.z));
                var col = FaceCol(mb);
                Tri(mb, center, a1, b1, Vector3.up, Paint.Shade(col, 1.08f));
            }
        }

        static MeshBuilder BuildRuinPillar(int b)
        {
            var mb = Builder(VariantSeed("prop_ruin_pillar", b), 0.08f, 0.3f, 0.5f);
            var stone = Color.Lerp(Pal.StoneCool, Pal.StoneLight, 0.35f + b * 0.08f);
            mb.Color = Paint.Shade(stone, 0.88f);
            mb.BoxOn(new Vector3(0f, 0f, 0.2f), new Vector3(0.84f, 0.3f, 0.76f), stone);
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0.3f, 0.2f), new Vector3(0.7f, 0.1f, 0.64f));
            var tops = new float[8];
            for (int i = 0; i < tops.Length; i++) tops[i] = 2.6f + mb.Random01() * 0.55f + (i == (b * 3) % 8 ? 0.45f : 0f);
            BrokenColumn(mb, new Vector3(0f, 0f, 0.2f), 0.29f, 8, 0.4f, tops);
            // ivy spiralling up, moss on the top and plinth, rubble behind
            for (int i = 0; i < 16; i++)
            {
                float t = i / 15f;
                float a = (200f + t * 300f + b * 40f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a) * 0.31f, 0.45f + t * 2.0f, 0.2f + Mathf.Sin(a) * 0.31f);
                mb.Color = i % 3 == 0 ? Pal.LeafDark : Pal.Leaf;
                Gem(mb, p, new Vector3(0.11f, 0.08f, 0.11f) * (1.1f - t * 0.4f));
            }
            FacetBlob(mb, new Vector3(0.05f, 2.75f, 0.2f), new Vector3(0.24f, 0.08f, 0.22f), 0, 0.25f, b + 3, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            FacetBlob(mb, new Vector3(-0.28f, 0.31f, 0.0f), new Vector3(0.14f, 0.05f, 0.12f), 0, 0.25f, b + 5, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            mb.Color = stone;
            OBox(mb, new Vector3(0.38f, 0.12f, 0.55f), new Vector3(0.28f, 0.22f, 0.3f), Quaternion.Euler(0f, 25f, 8f));
            OBox(mb, new Vector3(-0.4f, 0.09f, 0.5f), new Vector3(0.22f, 0.17f, 0.24f), Quaternion.Euler(10f, -30f, 0f));
            return mb;
        }

        static MeshBuilder BuildRuinArch(int b)
        {
            var mb = Builder(VariantSeed("prop_ruin_arch", b), 0.08f, 0.3f, 0.5f);
            var stone = Color.Lerp(Pal.StoneCool, Pal.StoneLight, 0.35f + b * 0.06f);
            const float px = 1.7f, spring = 3.1f, R = 1.7f;
            // pillars: left whole, right broken lower
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = Paint.Shade(stone, 0.88f);
                mb.BoxOn(new Vector3(s * px, 0f, 0f), new Vector3(0.8f, 0.3f, 0.75f), stone);
                mb.Color = stone;
                float h = s < 0 ? spring : 2.25f;
                int blocks = s < 0 ? 5 : 4;
                for (int k = 0; k < blocks; k++)
                {
                    float y0 = 0.3f + (h - 0.3f) * k / blocks, bh = (h - 0.3f) / blocks;
                    mb.Color = k % 2 == 0 ? stone : Paint.Shade(stone, 0.94f);
                    OBox(mb, new Vector3(s * px + (mb.Random01() - 0.5f) * 0.04f, y0 + bh * 0.5f, 0f), new Vector3(0.6f, bh - 0.02f, 0.6f), Quaternion.Euler(0f, (mb.Random01() - 0.5f) * 4f, 0f));
                }
                if (s < 0)
                {
                    mb.Color = Paint.Shade(stone, 1.05f);
                    mb.Box(new Vector3(s * px, spring + 0.08f, 0f), new Vector3(0.76f, 0.16f, 0.72f));
                }
                else
                {
                    mb.Color = stone;
                    OBox(mb, new Vector3(px + 0.05f, 2.4f, 0.02f), new Vector3(0.5f, 0.28f, 0.55f), Quaternion.Euler(6f, 10f, -12f));
                }
            }
            // voussoirs from the left springing, broken off past the keystone
            const int count = 9;
            for (int i = 0; i < count; i++)
            {
                float a0 = 180f - i * 15f, a1 = a0 - 15f;
                float am = (a0 + a1) * 0.5f * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(am) * (R + 0.25f) * 0.98f, spring + 0.16f + Mathf.Sin(am) * (R + 0.25f), 0f);
                bool key = i == 6;
                float sag = i >= 7 ? (i - 6) * 4f : 0f;
                mb.Color = key ? Paint.Shade(stone, 1.06f) : (i % 2 == 0 ? stone : Paint.Shade(stone, 0.93f));
                OBox(mb, c + (i >= 7 ? Vector3.down * 0.05f * (i - 6) : Vector3.zero), new Vector3(0.56f, key ? 0.62f : 0.5f, key ? 0.62f : 0.56f), Quaternion.Euler(0f, 0f, am * Mathf.Rad2Deg - 90f - sag));
            }
            // fallen blocks, moss, hanging ivy
            mb.Color = stone;
            OBox(mb, new Vector3(1.2f, 0.2f, -0.55f), new Vector3(0.5f, 0.4f, 0.55f), Quaternion.Euler(0f, 30f, 12f));
            OBox(mb, new Vector3(2.35f, 0.15f, 0.45f), new Vector3(0.42f, 0.3f, 0.5f), Quaternion.Euler(14f, -20f, 0f));
            OBox(mb, new Vector3(0.7f, 0.11f, 0.35f), new Vector3(0.3f, 0.22f, 0.32f), Quaternion.Euler(0f, 50f, -8f));
            var mossCol = ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss);
            FacetBlob(mb, new Vector3(-px, spring + 0.18f, 0.05f), new Vector3(0.3f, 0.06f, 0.28f), 0, 0.25f, b + 1, 0f, mossCol);
            FacetBlob(mb, new Vector3(-0.6f, spring + R + 0.45f, 0.02f), new Vector3(0.35f, 0.08f, 0.26f), 0, 0.25f, b + 2, 0f, mossCol);
            FacetBlob(mb, new Vector3(px, 2.58f, 0.02f), new Vector3(0.25f, 0.06f, 0.22f), 0, 0.25f, b + 4, 0f, mossCol);
            for (int strand = 0; strand < 3; strand++)
            {
                float am = (150f - strand * 25f) * Mathf.Deg2Rad;
                var start = new Vector3(Mathf.Cos(am) * R, spring + 0.1f + Mathf.Sin(am) * R, -0.3f);
                int leaves = 5 - strand;
                for (int i = 0; i < leaves; i++)
                {
                    mb.Color = (i + strand) % 2 == 0 ? Pal.Leaf : Pal.LeafDark;
                    Gem(mb, start + new Vector3((i % 2 == 0 ? 0.04f : -0.04f), -i * 0.17f, 0f), new Vector3(0.09f, 0.08f, 0.07f));
                }
            }
            Tuft(mb, new Vector3(-px - 0.45f, 0f, -0.2f), 0.4f, 5, Pal.Sage);
            Tuft(mb, new Vector3(px + 0.4f, 0f, -0.3f), 0.35f, 5, Pal.Sage);
            return mb;
        }

        // ------------------------------------------------------------------ campfire (collider 1.0 × 0.6): flames child flickers, SetLit

        static PropModel Campfire(string art, int seed)
        {
            int b = Bucket(seed);
            string key = "prop_campfire#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, true, lit => BuildCampfire(b, lit)));
            var flames = rig.Part("Flames", Cached("prop_campfire#flames" + b, () => BuildFlames(b)), new Vector3(0f, 0.12f, 0.02f), Quaternion.identity, Skin.Plain);
            var motion = flames.gameObject.AddComponent<PropMotion>();
            motion.Flicker = 0.12f;
            motion.FlickerSpeed = 7.5f + b;
            rig.Model.SetLit = MeshSwitch(mf, key, lit => BuildCampfire(b, lit));
            rig.Model.SetLit += ActiveSwitch(flames.gameObject);
            rig.Light(new Vector3(0f, 0.5f, 0.02f));
            return rig.Done(0.5f);
        }

        static MeshBuilder BuildCampfire(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_campfire", b), 0.07f, 0.3f, 0.3f);
            const float zc = 0.02f;
            mb.Color = Pal.Charcoal;
            mb.Disc(new Vector3(0f, 0.012f, zc), 0.34f, 9);
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + b * 13f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a) * 0.36f, 0.07f, zc + Mathf.Sin(a) * 0.22f);
                float s = 0.1f + mb.Random01() * 0.04f;
                FacetBlob(mb, p, new Vector3(s * 1.2f, s * 0.8f, s), 0, 0.25f, b * 11 + i, 0.4f, Mossy(Pal.Stone, Pal.StoneLight, 0.7f));
            }
            // crossed logs (teepee), charred tips
            var apex = new Vector3(0f, 0.42f, zc);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f + b * 10f) * Mathf.Deg2Rad;
                var foot = new Vector3(Mathf.Cos(a) * 0.3f, 0.03f, zc + Mathf.Sin(a) * 0.18f);
                mb.Color = Pal.Wood;
                mb.Segment(foot, Vector3.Lerp(foot, apex, 0.7f), 0.055f, 0.045f, 6);
                mb.Color = Pal.Charcoal;
                mb.Segment(Vector3.Lerp(foot, apex, 0.7f), apex + (foot - apex) * 0.05f, 0.045f, 0.03f, 6);
            }
            mb.Color = Pal.Wood;
            Beam(mb, new Vector3(-0.32f, 0.06f, zc - 0.06f), new Vector3(0.3f, 0.07f, zc + 0.1f), 0.1f, 0.1f);
            // embers
            mb.Emission = lit ? 1f : 0f;
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.9f + b;
                mb.Color = lit ? Color.Lerp(Pal.Ember, Pal.FireCore, (i % 3) * 0.35f) : Paint.Shade(Pal.Charcoal, 1.3f);
                Gem(mb, new Vector3(Mathf.Cos(a) * 0.16f, 0.05f, zc + Mathf.Sin(a) * 0.1f), new Vector3(0.05f, 0.03f, 0.05f));
            }
            mb.Emission = 0f;
            return mb;
        }

        static MeshBuilder BuildFlames(int b)
        {
            var mb = new MeshBuilder(VariantSeed("prop_campfire_flames", b)) { Emission = 1f, WindGradient = true, WindY0 = 0.1f, WindY1 = 0.8f, Wind = 0.8f };
            for (int layer = 0; layer < 3; layer++)
            {
                int count = layer == 0 ? 6 : (layer == 1 ? 5 : 3);
                float h = layer == 0 ? 0.68f : (layer == 1 ? 0.48f : 0.3f);
                float rr = layer == 0 ? 0.15f : (layer == 1 ? 0.1f : 0.05f);
                mb.Color = layer == 0 ? Pal.Fire : (layer == 1 ? Paint.Hex("#FFC24A") : Pal.FireCore);
                for (int i = 0; i < count; i++)
                {
                    float a = (i * 360f / count + layer * 30f + b * 17f) * Mathf.Deg2Rad;
                    var basePt = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr * 0.8f);
                    var tip = new Vector3(Mathf.Cos(a) * rr * 0.3f, h * (0.75f + 0.25f * ((i * 7 + layer) % 3) / 2f), Mathf.Sin(a) * rr * 0.25f);
                    var side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    mb.Blade(basePt, tip, 0.2f - layer * 0.04f, side);
                    mb.Blade(basePt, tip, 0.2f - layer * 0.04f, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                }
            }
            mb.Color = Pal.FireCore;
            for (int i = 0; i < 3; i++) Gem(mb, new Vector3((i - 1) * 0.1f, 0.85f + i % 2 * 0.15f, (i % 2) * 0.05f), 0.022f);
            return mb;
        }

        // ------------------------------------------------------------------ travel tent (collider 2.4 × 1.2): warm glow inside when lit

        static PropModel Tent(string art, int seed) =>
            LitBuilding(art, seed, BuildTent, 1.2f, new Vector3(0.2f, 0.6f, 0.6f));

        static MeshBuilder BuildTent(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_tent", b), 0.05f, 0.25f, 0.4f);
            var canvas = new[] { Paint.Hex("#E9DDC2"), Paint.Hex("#A8C2B8"), Paint.Hex("#C9CFA0"), Paint.Hex("#E3BFAE") }[b];
            const float zf = -0.32f, zb = 2.1f, hw = 1.05f, ridge = 1.75f;
            var top0 = new Vector3(0f, ridge, zf);
            var top1 = new Vector3(0f, ridge, zb);
            // canvas sides and back
            mb.Color = canvas;
            Slab(mb, new[] { top0, top1, new Vector3(-hw, 0f, zb), new Vector3(-hw, 0f, zf) }, new Vector3(-ridge, hw, 0f), 0.03f);
            mb.Color = Paint.Shade(canvas, 0.97f);
            Slab(mb, new[] { top0, top1, new Vector3(hw, 0f, zb), new Vector3(hw, 0f, zf) }, new Vector3(ridge, hw, 0f), 0.03f);
            mb.Color = canvas;
            Slab(mb, new[] { new Vector3(-hw + 0.04f, 0f, zb - 0.02f), new Vector3(0f, ridge - 0.04f, zb - 0.02f), new Vector3(hw - 0.04f, 0f, zb - 0.02f) }, Vector3.forward, 0.03f);
            // front: left flap closed, right flap rolled back
            mb.Color = Paint.Shade(canvas, 1.04f);
            Slab(mb, new[] { new Vector3(-0.02f, ridge - 0.04f, zf - 0.01f), new Vector3(-hw + 0.04f, 0f, zf - 0.01f), new Vector3(-0.08f, 0f, zf - 0.01f) }, Vector3.back, 0.025f);
            mb.Color = Paint.Shade(canvas, 0.85f);
            mb.Segment(new Vector3(0.06f, ridge - 0.15f, zf - 0.06f), new Vector3(0.82f, 0.32f, zf - 0.06f), 0.075f, 0.06f, 6);
            mb.Color = Pal.Vermilion;
            mb.Segment(new Vector3(0.5f, 0.95f, zf - 0.12f), new Vector3(0.62f, 0.88f, zf - 0.02f), 0.015f, 0.015f, 3);
            // warm lining and floor seen through the opening
            var warm = lit ? Paint.Hex("#FFC47A") : Paint.Shade(canvas, 0.6f);
            mb.Emission = lit ? 0.55f : 0f;
            var inR = new Vector3(-ridge, -hw, 0f).normalized;   // the right panel's inner side faces left-down
            QuadC(mb, new Vector3(0f, ridge - 0.08f, zf + 0.05f), new Vector3(0f, ridge - 0.08f, zb - 0.06f), new Vector3(hw - 0.08f, 0.04f, zb - 0.06f), new Vector3(hw - 0.08f, 0.04f, zf + 0.05f), inR, warm);
            var inL = new Vector3(ridge, -hw, 0f).normalized;
            QuadC(mb, new Vector3(0f, ridge - 0.08f, zf + 0.05f), new Vector3(0f, ridge - 0.08f, zb - 0.06f), new Vector3(-hw + 0.08f, 0.04f, zb - 0.06f), new Vector3(-hw + 0.08f, 0.04f, zf + 0.05f), inL, Paint.Shade(warm, 0.9f));
            Tri(mb, new Vector3(-hw + 0.1f, 0.04f, zb - 0.07f), new Vector3(0f, ridge - 0.1f, zb - 0.07f), new Vector3(hw - 0.1f, 0.04f, zb - 0.07f), Vector3.back, Paint.Shade(warm, 0.95f));
            QuadC(mb, new Vector3(-hw + 0.1f, 0.03f, zf + 0.05f), new Vector3(-hw + 0.1f, 0.03f, zb - 0.08f), new Vector3(hw - 0.1f, 0.03f, zb - 0.08f), new Vector3(hw - 0.1f, 0.03f, zf + 0.05f), Vector3.up,
                  lit ? Paint.Hex("#E89A5C") : Paint.Hex("#6B5A4A"));
            mb.Emission = 0f;
            // bedroll and a little lantern inside
            mb.Color = Paint.Hex("#8A5A6A");
            mb.Segment(new Vector3(-0.55f, 0.12f, 0.9f), new Vector3(-0.2f, 0.12f, 1.6f), 0.12f, 0.12f, 7);
            IronLantern(mb, new Vector3(0.3f, 0.14f, 0.55f), 0.9f, lit);
            // poles, pennant, pegs and a back guy rope
            mb.Color = Pal.TimberDark;
            mb.Segment(new Vector3(0f, 0f, zf - 0.04f), new Vector3(0f, 2.2f, zf - 0.04f), 0.035f, 0.03f, 5);
            mb.Segment(new Vector3(0f, 0f, zb + 0.04f), new Vector3(0f, 1.9f, zb + 0.04f), 0.035f, 0.03f, 5);
            mb.Segment(new Vector3(0f, ridge + 0.02f, zf - 0.04f), new Vector3(0f, ridge + 0.02f, zb + 0.04f), 0.03f, 0.03f, 4);
            mb.WindGradient = true; mb.WindY0 = 1.95f; mb.WindY1 = 2.2f; mb.Wind = 1.5f;
            mb.Color = Pal.Vermilion;
            Slab(mb, new[] { new Vector3(0.03f, 2.18f, zf - 0.04f), new Vector3(0.03f, 1.98f, zf - 0.04f), new Vector3(0.45f, 2.06f, zf - 0.04f) }, Vector3.back, 0.015f);
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Pal.Wood;
            foreach (var p in new[] { new Vector3(-hw + 0.02f, 0f, zf + 0.12f), new Vector3(hw - 0.02f, 0f, zf + 0.12f), new Vector3(-hw - 0.05f, 0f, zb), new Vector3(hw + 0.05f, 0f, zb) })
                mb.BoxOn(p, new Vector3(0.05f, 0.14f, 0.05f));
            mb.Color = Pal.RopeStraw;
            mb.Segment(new Vector3(0f, 1.88f, zb + 0.05f), new Vector3(0f, 0.02f, zb + 0.85f), 0.012f, 0.012f, 3);
            mb.Color = Pal.Wood;
            mb.BoxOn(new Vector3(0f, 0f, zb + 0.86f), new Vector3(0.05f, 0.12f, 0.05f));
            return mb;
        }
    }
}
