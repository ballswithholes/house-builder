// Buildings: cottages (thatched / tiled two-storey / timber-framed), the Sleepy Lantern inn, the smithy, the windmill.
// Front wall faces −Z and sits inside the collider ellipse; the bulk extends backwards (+Z). Windows glow when lit
// (SetLit swaps the lit/dark mesh; default lit), LightAnchors sit in front of the main lit window / lantern.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterBuildings(Dictionary<string, Recipe> r)
        {
            r["prop_cottage_a"] = CottageA;
            r["prop_cottage_b"] = CottageB;
            r["prop_cottage_c"] = CottageC;
            r["prop_inn"] = Inn;
            r["prop_smithy"] = Smithy;
            r["prop_windmill"] = Windmill;
        }

        /// <summary>Building with its windows baked into one mesh: SetLit swaps lit/dark variants (default lit).</summary>
        static PropModel LitBuilding(string art, int seed, Func<int, bool, MeshBuilder> build, float radius, params Vector3[] lights)
        {
            int b = Bucket(seed);
            string key = art + "#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, true, lit => build(b, lit)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => build(b, lit));
            foreach (var l in lights) rig.Light(l);
            return rig.Done(radius);
        }

        // ------------------------------------------------------------------ cottage A: thatched

        static PropModel CottageA(string art, int seed) =>
            LitBuilding(art, seed, BuildCottageA, 2.5f, new Vector3(1.2f, 1.95f, -0.5f),
                        new Vector3(-0.45f, 3.78f, ThatchFrontZ(4.05f, 1.5f, 2.35f, 3.0f, 2.75f, 1.45f) - 0.3f), new Vector3(2.25f, 2.0f, 1.6f));

        /// <summary>Rounded thatch over a wall box: superellipse cross-section lofted along X, hipped (shrinking) ends.</summary>
        static void ThatchRoof(MeshBuilder mb, float halfLen, float zc, float halfDepth, float baseY, float height, float lip, float p, int seed)
        {
            const int K = 10;
            float bottom = baseY - lip;
            Vector3[] Ring(float x, float s, float wobble)
            {
                var ring = new Vector3[K + 3];
                ring[0] = new Vector3(x, bottom, zc - (halfDepth - 0.14f) * s);
                for (int k = 0; k <= K; k++)
                {
                    float th = Mathf.PI * k / K;
                    float sn = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(th)), p);
                    float z = zc - halfDepth * Mathf.Cos(th);
                    float y = baseY + height * sn + wobble * Mathf.Sin(th * 3f + x);
                    ring[k + 1] = new Vector3(x, bottom + (y - bottom) * s, zc + (z - zc) * s);
                }
                ring[K + 2] = new Vector3(x, bottom, zc + (halfDepth - 0.14f) * s);
                return ring;
            }
            float[] xs = { -1f, -0.93f, -0.78f, -0.45f, 0f, 0.45f, 0.78f, 0.93f, 1f };
            float[] ss = { 0.32f, 0.62f, 0.9f, 0.99f, 1f, 0.99f, 0.9f, 0.62f, 0.32f };
            var rings = new List<Vector3[]>();
            for (int i = 0; i < xs.Length; i++) rings.Add(Ring(xs[i] * halfLen, ss[i], (i % 2 == 0 ? 0.04f : -0.03f)));
            var top = Paint.Shade(mb.Color, 1.1f);
            var side = mb.Color;
            var eave = Paint.Shade(Pal.ThatchDark, 0.95f);
            Loft(mb, rings, true, true, n => n.y < -0.3f ? Paint.Shade(eave, 0.75f) : n.y < 0.35f ? eave : Color.Lerp(side, top, (n.y - 0.35f) / 0.65f));
        }

        /// <summary>Depth (z) of a ThatchRoof's outer surface on the front slope at height y (for dormers / anchors).</summary>
        static float ThatchFrontZ(float y, float zc, float halfDepth, float baseY, float height, float p)
        {
            float sn = Mathf.Clamp01((y - baseY) / height);
            float th = Mathf.Asin(Mathf.Pow(sn, 1f / p));
            return zc - halfDepth * Mathf.Cos(th);
        }

        static MeshBuilder BuildCottageA(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_cottage_a", b), 0.06f, 0.3f, 0.9f);
            const float W = 4.3f, Z0 = -0.4f, Z1 = 3.4f, Y0 = 0.45f, YE = 3.2f;
            float zc = (Z0 + Z1) * 0.5f;
            var plaster = Color.Lerp(Pal.Cream, Pal.PlasterWarm, b * 0.18f);
            var timber = Pal.Timber;
            // stone footing with a few proud stones
            mb.Color = Pal.Stone;
            mb.BoxOn(new Vector3(0f, 0f, zc), new Vector3(W + 0.12f, Y0, Z1 - Z0 + 0.1f), Pal.StoneLight);
            for (int i = 0; i < 7; i++)
            {
                float x = -W * 0.5f + 0.3f + i * (W - 0.6f) / 6f + (mb.Random01() - 0.5f) * 0.2f;
                FacetBlob(mb, new Vector3(x, 0.2f + mb.Random01() * 0.08f, Z0 - 0.02f), new Vector3(0.2f, 0.13f, 0.08f), 0, 0.2f, b * 13 + i,
                          0f, Mossy(Pal.StoneLight, Pal.Moss, 0.7f));
            }
            // walls
            mb.Color = plaster;
            mb.BoxOn(new Vector3(0f, Y0, zc), new Vector3(W, YE - Y0 + 0.1f, Z1 - Z0));
            // timber frame on the front: corner posts, sill, top plate, braces left of the door
            mb.Color = timber;
            mb.Box(new Vector3(-W * 0.5f + 0.08f, (Y0 + YE) * 0.5f, Z0 - 0.02f), new Vector3(0.18f, YE - Y0, 0.08f));
            mb.Box(new Vector3(W * 0.5f - 0.08f, (Y0 + YE) * 0.5f, Z0 - 0.02f), new Vector3(0.18f, YE - Y0, 0.08f));
            mb.Box(new Vector3(0f, Y0 + 0.07f, Z0 - 0.02f), new Vector3(W, 0.14f, 0.08f));
            mb.Box(new Vector3(0f, YE - 0.12f, Z0 - 0.02f), new Vector3(W, 0.16f, 0.08f));
            Beam(mb, new Vector3(-W * 0.5f + 0.16f, Y0 + 0.2f, Z0 - 0.02f), new Vector3(-1.3f, YE - 0.3f, Z0 - 0.02f), 0.13f, 0.07f, Vector3.back);
            mb.Box(new Vector3(-1.22f, (Y0 + YE) * 0.5f, Z0 - 0.02f), new Vector3(0.14f, YE - Y0, 0.07f));
            // side corner posts at the back
            mb.Box(new Vector3(-W * 0.5f - 0.02f, (Y0 + YE) * 0.5f, Z1 - 0.1f), new Vector3(0.08f, YE - Y0, 0.18f));
            mb.Box(new Vector3(W * 0.5f + 0.02f, (Y0 + YE) * 0.5f, Z1 - 0.1f), new Vector3(0.08f, YE - Y0, 0.18f));
            // arched door + stone step
            Door(mb, -0.55f, Y0, 0.95f, 2.05f, Z0, Pal.Wood, timber, true);
            mb.Color = Pal.StoneLight;
            mb.BoxOn(new Vector3(-0.55f, 0f, Z0 - 0.22f), new Vector3(1.25f, 0.2f, 0.44f));
            mb.BoxOn(new Vector3(-0.55f, 0f, Z0 - 0.5f), new Vector3(1.0f, 0.1f, 0.3f));
            // big round window with a flower box
            RoundWindow(mb, 1.2f, 1.95f, 0.5f, Z0, lit, timber);
            FlowerBox(mb, 1.2f, 1.33f, 1.1f, Z0, Pal.Wood, b * 3);
            // side window (right wall)
            mb.Push().Rotate(0f, -90f, 0f);
            Window(mb, 1.6f, 2.0f, 0.65f, 0.8f, -W * 0.5f, lit, timber, null);
            mb.Pop();
            // ivy on the left corner
            Ivy(mb, new Vector3(-W * 0.5f + 0.3f, 0.5f, Z0 - 0.04f), new Vector3(-W * 0.5f + 0.45f, 2.9f, Z0 - 0.04f), 14, 0.35f, Vector3.back);
            // thatch
            const float halfDepth = 2.35f, baseY = 3.0f, height = 2.75f, p = 1.45f;
            mb.Color = Color.Lerp(Pal.Thatch, Pal.ThatchLight, b * 0.15f);
            mb.Jitter = 0.045f;
            ThatchRoof(mb, W * 0.5f + 0.5f, zc, halfDepth, baseY, height, 0.32f, p, b);
            // eyebrow dormer over the door: a little half-round window under a curved lip of thatch
            float dy = 4.05f, dz = ThatchFrontZ(dy, zc, halfDepth, baseY, height, p);
            mb.Jitter = 0.06f;
            mb.Color = timber;
            Slab(mb, NgonPoints(new Vector3(-0.45f, dy - 0.3f, dz - 0.2f), 0.52f, 0.5f, 6, Vector3.right, Vector3.up, 0f, 0f, 180f), Vector3.back, 0.5f);
            mb.Emission = lit ? 1f : 0f;
            var glass = NgonPoints(new Vector3(-0.45f, dy - 0.27f, dz - 0.215f), 0.4f, 0.37f, 6, Vector3.right, Vector3.up, 0f, 0f, 180f);
            for (int i = 0; i < glass.Length - 1; i++) Tri(mb, new Vector3(-0.45f, dy - 0.27f, dz - 0.215f), glass[i], glass[i + 1], Vector3.back, lit ? Pal.Glow : Pal.GlassDark);
            mb.Emission = 0f;
            mb.Box(new Vector3(-0.45f, dy - 0.12f, dz - 0.23f), new Vector3(0.04f, 0.3f, 0.03f));
            mb.Color = Pal.ThatchDark;
            var brow = new List<Vector3[]>();
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.PI * i / 6f;
                var c = new Vector3(-0.45f + Mathf.Cos(a) * 0.68f, dy - 0.3f + Mathf.Sin(a) * 0.62f, dz - 0.18f - Mathf.Sin(a) * 0.12f);
                var o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                brow.Add(new[] { c + o * 0.16f, c + Vector3.back * 0.14f, c - o * 0.08f, c + Vector3.forward * 0.25f });
            }
            Loft(mb, brow, true, true, n => n.y > 0.3f ? Pal.Thatch : Pal.ThatchDark);
            // crooked stone chimney on the front slope
            Chimney(mb, new Vector3(1.6f, 4.0f, 0.75f), 0.62f, 0.55f, 2.55f, Pal.Stone, -4f);
            return mb;
        }

        // ------------------------------------------------------------------ cottage B: tall, crooked, stone + rosy plaster, terracotta

        static PropModel CottageB(string art, int seed) =>
            LitBuilding(art, seed, BuildCottageB, 2.2f, new Vector3(-0.95f, 3.65f, -0.88f), new Vector3(0.95f, 3.65f, -0.88f),
                        new Vector3(-0.85f, 1.35f, -0.6f), new Vector3(0.7f, 5.5f, -0.77f));

        static MeshBuilder BuildCottageB(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_cottage_b", b), 0.07f, 0.3f, 0.9f);
            const float W1 = 3.5f, Z0 = -0.5f, Z1 = 3.0f, YF = 2.5f;     // stone ground floor
            const float W2 = 3.8f, Z0u = -0.78f, YT = 4.9f;               // jettied upper floor
            float zc = (Z0 + Z1) * 0.5f, zcu = (Z0u + Z1) * 0.5f;
            var plaster = Color.Lerp(Pal.PlasterRose, Pal.PlasterWarm, b * 0.22f);
            var shutters = Pal.Shutters[b];
            var timber = Pal.Timber;
            // stone ground floor with proud stones and quoins
            mb.Color = Pal.Stone;
            mb.Jitter = 0.1f;
            mb.BoxOn(new Vector3(0f, 0f, zc), new Vector3(W1, YF, Z1 - Z0));
            for (int i = 0; i < 9; i++)
            {
                float x = -1.35f + mb.Random01() * 2.7f, y = 0.25f + mb.Random01() * 2.0f;
                if (x > 0.15f && x < 1.35f && y < 2.2f) continue;                 // keep the door clear
                if (x > -1.45f && x < -0.2f && y > 0.75f && y < 1.85f) continue; // and the window
                mb.Color = mb.Random01() < 0.5f ? Pal.StoneLight : Pal.StoneDark;
                mb.Box(new Vector3(x, y, Z0 - 0.015f), new Vector3(0.3f + mb.Random01() * 0.2f, 0.17f + mb.Random01() * 0.08f, 0.05f));
            }
            mb.Color = Pal.StoneLight;
            for (int k = 0; k < 5; k++)
            {
                bool wide = k % 2 == 0;
                float y = 0.27f + k * 0.47f;
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Box(new Vector3(s * (W1 * 0.5f - (wide ? 0.22f : 0.15f)), y, Z0 - 0.02f), new Vector3(wide ? 0.46f : 0.32f, 0.4f, 0.06f));
                    mb.Box(new Vector3(s * (W1 * 0.5f + 0.02f), y, Z0 + (wide ? 0.15f : 0.22f)), new Vector3(0.06f, 0.4f, wide ? 0.3f : 0.44f));
                }
            }
            mb.Jitter = 0.07f;
            // door, step, small window with a flower box
            Door(mb, 0.75f, 0f, 0.9f, 2.05f, Z0, Pal.Wood, timber, true);
            mb.Color = Pal.StoneLight;
            mb.BoxOn(new Vector3(0.72f, 0f, Z0 - 0.15f), new Vector3(1.0f, 0.14f, 0.3f));
            Window(mb, -0.85f, 1.35f, 0.62f, 0.7f, Z0, lit, timber, shutters);
            FlowerBox(mb, -0.85f, 0.9f, 0.95f, Z0, Pal.Wood, b * 2 + 1);
            // jetty beam and corbels
            mb.Color = timber;
            mb.Box(new Vector3(0f, YF + 0.06f, (Z0u + Z0) * 0.5f), new Vector3(W2, 0.18f, Z0 - Z0u + 0.1f));
            foreach (float x in new[] { -1.55f, -0.05f, 1.55f })
                Beam(mb, new Vector3(x, YF - 0.5f, Z0 - 0.03f), new Vector3(x, YF, Z0u + 0.1f), 0.12f, 0.12f, Vector3.back);
            // upper floor, leaning a little
            mb.Push().Translate(0f, YF, zc).Rotate(0f, 0f, -2f - b * 0.5f).Translate(0f, -YF, -zc);
            mb.Color = plaster;
            mb.BoxOn(new Vector3(0f, YF + 0.15f, zcu), new Vector3(W2, YT - YF - 0.1f, Z1 - Z0u));
            mb.Color = timber;
            mb.Box(new Vector3(-W2 * 0.5f + 0.07f, (YF + YT) * 0.5f + 0.05f, Z0u - 0.02f), new Vector3(0.15f, YT - YF - 0.1f, 0.07f));
            mb.Box(new Vector3(W2 * 0.5f - 0.07f, (YF + YT) * 0.5f + 0.05f, Z0u - 0.02f), new Vector3(0.15f, YT - YF - 0.1f, 0.07f));
            mb.Box(new Vector3(0f, YT - 0.06f, Z0u - 0.02f), new Vector3(W2, 0.13f, 0.07f));
            Window(mb, -0.95f, 3.65f, 0.7f, 0.9f, Z0u, lit, timber, shutters);
            Window(mb, 0.95f, 3.65f, 0.7f, 0.9f, Z0u, lit, timber, shutters);
            FlowerBox(mb, -0.95f, 3.06f, 0.95f, Z0u, Pal.Wood, b * 2 + 3);
            FlowerBox(mb, 0.95f, 3.06f, 0.95f, Z0u, Pal.Wood, b * 2 + 5);
            // steep terracotta roof, leaning a bit more
            mb.Push().Translate(0f, YT, zcu).Rotate(0f, 0f, -1.2f).Translate(0f, -YT, -zcu);
            const float rh = 2.9f, ov = 0.35f;
            var tile = Paint.Hsv(Pal.Terracotta, b * 4f - 6f);
            CourseRoof(mb, new Vector3(0f, YT, zcu), W2, Z1 - Z0u, rh, ov, 5, tile, Paint.Shade(tile, 0.9f), Pal.TerracottaDark);
            mb.Color = plaster;
            GableWall(mb, -W2 * 0.5f, YT, Z0u, Z1, rh - 0.1f, -1f);
            GableWall(mb, W2 * 0.5f, YT, Z0u, Z1, rh - 0.1f, 1f);
            // front dormer with a round attic window
            float hz = (Z1 - Z0u) * 0.5f + ov, drop = rh * ov / ((Z1 - Z0u) * 0.5f);
            float SlopeZ(float y) => zcu - hz + (y - (YT - drop)) / (rh + drop) * hz;
            float dzf = SlopeZ(YT + 0.25f) - 0.05f, dzb = SlopeZ(YT + 1.3f) + 0.4f;
            mb.Color = plaster;
            mb.BoxOn(new Vector3(0.7f, YT + 0.1f, (dzf + dzb) * 0.5f), new Vector3(1.05f, 0.95f, dzb - dzf));
            mb.Push().Translate(0.7f, YT + 1.05f, (dzf + dzb) * 0.5f).Rotate(0f, 90f, 0f);
            mb.Color = tile;
            mb.Roof(Vector3.zero, dzb - dzf, 1.05f, 0.55f, 0.12f, 0.08f, false);
            mb.Pop();
            mb.Color = plaster;
            Tri(mb, new Vector3(0.7f - 0.525f, YT + 1.05f, dzf), new Vector3(0.7f, YT + 1.55f, dzf), new Vector3(0.7f + 0.525f, YT + 1.05f, dzf), Vector3.back, plaster);
            RoundWindow(mb, 0.7f, YT + 0.6f, 0.27f, dzf, lit, timber);
            mb.Pop();
            // very tall crooked chimney (two stacks)
            var stone = Pal.StoneDark;
            Chimney(mb, new Vector3(-1.3f, 5.5f, 1.55f), 0.58f, 0.52f, 2.1f, stone, 3f);
            var top = new Vector3(-1.3f - Mathf.Sin(3f * Mathf.Deg2Rad) * 2.1f, 5.5f + Mathf.Cos(3f * Mathf.Deg2Rad) * 2.1f + 0.1f, 1.55f);
            Chimney(mb, top, 0.48f, 0.44f, 1.2f, stone, -6f);
            mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ cottage C: single storey, timber-framed, blue scalloped tiles

        static PropModel CottageC(string art, int seed) =>
            LitBuilding(art, seed, BuildCottageC, 2.5f, new Vector3(0.72f, 2.2f, -0.78f), new Vector3(-1.36f, 1.72f, -0.52f),
                        new Vector3(1.5f, 1.72f, -0.52f), new Vector3(2.3f, 1.75f, 1.25f), new Vector3(-2.3f, 1.75f, 1.25f));

        /// <summary>Small iron lantern (glass box, pyramid hat) hanging with its centre at c.</summary>
        static void IronLantern(MeshBuilder mb, Vector3 c, float s, bool lit)
        {
            var keepC = mb.Color;
            float keepE = mb.Emission;
            mb.Emission = lit ? 1f : 0f;
            mb.Color = lit ? Pal.Glow : Pal.GlassDark;
            mb.Box(c, new Vector3(0.15f, 0.2f, 0.15f) * s);
            mb.Emission = 0f;
            mb.Color = Pal.Iron;
            mb.Box(c - new Vector3(0f, 0.11f * s, 0f), new Vector3(0.2f, 0.03f, 0.2f) * s);
            mb.Cone(c + new Vector3(0f, 0.1f * s, 0f), 0.15f * s, 0.12f * s, 4);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                mb.Box(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.1f * s, new Vector3(0.025f, 0.21f, 0.025f) * s);
            }
            mb.Torus(c + new Vector3(0f, 0.25f * s, 0f), 0.03f * s, 0.01f * s, 6, 3);
            mb.Color = keepC;
            mb.Emission = keepE;
        }

        static void Pot(MeshBuilder mb, Vector3 at, float r, float h, int flowerSeed)
        {
            var keepC = mb.Color;
            mb.Color = Pal.Terracotta;
            mb.Push().Translate(at);
            mb.Lathe(new[] { new Vector2(r * 0.7f, 0f), new Vector2(r, h * 0.85f), new Vector2(r * 1.12f, h * 0.88f), new Vector2(r * 1.12f, h), new Vector2(r * 0.9f, h) }, 7, false, true, true);
            mb.Pop();
            FacetBlob(mb, at + new Vector3(0f, h + r * 0.55f, 0f), new Vector3(r * 1.2f, r * 0.95f, r * 1.2f), 0, 0.25f, flowerSeed, 0.4f,
                      ByNormal(Pal.Leaf, Pal.LeafDark, Pal.Forest));
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + flowerSeed * 37f) * Mathf.Deg2Rad;
                mb.Color = Pal.Flowers[(flowerSeed + i) % Pal.Flowers.Length];
                Gem(mb, at + new Vector3(Mathf.Cos(a) * r * 0.7f, h + r * (1.1f + 0.3f * (i % 2)), Mathf.Sin(a) * r * 0.7f), r * 0.32f);
            }
            mb.Color = keepC;
        }

        static MeshBuilder BuildCottageC(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_cottage_c", b), 0.06f, 0.3f, 0.9f);
            const float W = 4.4f, Z0 = -0.42f, Z1 = 3.42f, YS = 0.55f, YE = 3.25f;
            float zc = (Z0 + Z1) * 0.5f;
            var timber = Pal.TimberDark;
            var blue = Paint.Hsv(Pal.TileBlue, b * 9f - 12f);
            var shutters = Paint.Hsv(Pal.DustyBlue, b * 9f - 12f, 1.1f, 0.85f);
            // plank skirt and plaster walls
            PlankWall(mb, new Vector3(0f, 0f, zc), W + 0.06f, YS, Z1 - Z0 + 0.06f, Pal.Wood, 0.2f);
            mb.Color = Pal.Cream;
            mb.BoxOn(new Vector3(0f, YS, zc), new Vector3(W, YE - YS + 0.08f, Z1 - Z0));
            // timber frame
            mb.Color = timber;
            float fz = Z0 - 0.025f;
            mb.Box(new Vector3(0f, YS + 0.06f, fz), new Vector3(W, 0.13f, 0.07f));
            mb.Box(new Vector3(0f, YE - 0.07f, fz), new Vector3(W, 0.14f, 0.07f));
            foreach (float x in new[] { -W * 0.5f + 0.07f, -0.62f, 0.72f, W * 0.5f - 0.07f })
                mb.Box(new Vector3(x, (YS + YE) * 0.5f, fz), new Vector3(0.13f, YE - YS, 0.07f));
            mb.Box(new Vector3(-1.4f, 2.62f, fz), new Vector3(1.5f, 0.1f, 0.06f));
            mb.Box(new Vector3(1.47f, 2.62f, fz), new Vector3(1.38f, 0.1f, 0.06f));
            for (int s = -1; s <= 1; s += 2)
            {
                // side walls: posts and braces
                float x = s * (W * 0.5f + 0.025f);
                mb.Box(new Vector3(x, (YS + YE) * 0.5f, Z1 - 0.08f), new Vector3(0.07f, YE - YS, 0.13f));
                mb.Box(new Vector3(x, YS + 0.06f, zc), new Vector3(0.07f, 0.13f, Z1 - Z0));
                mb.Box(new Vector3(x, YE - 0.07f, zc), new Vector3(0.07f, 0.14f, Z1 - Z0));
                Beam(mb, new Vector3(x, YS + 0.15f, Z1 - 0.2f), new Vector3(x, YE - 0.2f, 2.3f), 0.1f, 0.06f, new Vector3(s, 0f, 0f));
            }
            // door with steps, iron lantern
            Door(mb, 0.05f, YS, 0.85f, 1.95f, Z0, Pal.WoodLight, timber, false, Pal.Iron);
            mb.Color = Pal.Wood;
            mb.BoxOn(new Vector3(0.05f, 0f, Z0 - 0.2f), new Vector3(1.05f, 0.36f, 0.36f));
            mb.BoxOn(new Vector3(0.05f, 0f, Z0 - 0.48f), new Vector3(1.05f, 0.18f, 0.32f));
            mb.Color = Pal.Iron;
            mb.Box(new Vector3(0.72f, 2.44f, Z0 - 0.2f), new Vector3(0.04f, 0.04f, 0.36f));
            Beam(mb, new Vector3(0.72f, 2.15f, Z0 - 0.04f), new Vector3(0.72f, 2.44f, Z0 - 0.3f), 0.03f, 0.03f, Vector3.right);
            IronLantern(mb, new Vector3(0.72f, 2.2f, Z0 - 0.36f), 1f, lit);
            // windows
            Window(mb, -1.36f, 1.72f, 0.68f, 0.95f, Z0, lit, timber, shutters);
            Window(mb, 1.5f, 1.72f, 0.75f, 0.95f, Z0, lit, timber, null);
            FlowerBox(mb, 1.5f, 1.12f, 0.95f, Z0, Pal.WoodLight, b * 3 + 2);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push().Rotate(0f, -90f * s, 0f);
                Window(mb, 1.25f * s, 1.75f, 0.6f, 0.8f, -W * 0.5f, lit, timber, null);
                mb.Pop();
            }
            // potted plants by the door
            Pot(mb, new Vector3(-0.8f, 0f, Z0 - 0.3f), 0.17f, 0.34f, b + 1);
            Pot(mb, new Vector3(1.05f, 0f, Z0 - 0.28f), 0.14f, 0.28f, b + 4);
            // blue scalloped tile roof
            const float rh = 2.15f;
            CourseRoof(mb, new Vector3(0f, YE, zc), W, Z1 - Z0, rh, 0.32f, 5, blue, Paint.Shade(blue, 0.88f), Paint.Shade(blue, 0.7f), true);
            mb.Color = Pal.Cream;
            GableWall(mb, -W * 0.5f, YE, Z0, Z1, rh - 0.1f, -1f);
            GableWall(mb, W * 0.5f, YE, Z0, Z1, rh - 0.1f, 1f);
            Chimney(mb, new Vector3(-1.35f, 4.1f, 2.3f), 0.55f, 0.5f, 2.2f, Pal.StoneLight, 2f);
            return mb;
        }

        // ------------------------------------------------------------------ the Sleepy Lantern inn

        static PropModel Inn(string art, int seed) =>
            LitBuilding(art, seed, BuildInn, 3.3f,
                        new Vector3(-1.12f, 2.6f, -0.88f), new Vector3(1.12f, 2.6f, -0.88f), new Vector3(-2.37f, 1.85f, -0.6f), new Vector3(2.37f, 1.85f, -0.6f),
                        new Vector3(-1.3f, 4.35f, -0.85f), new Vector3(1.3f, 4.35f, -0.85f), new Vector3(-2.63f, 4.35f, -0.85f), new Vector3(2.63f, 4.35f, -0.85f),
                        new Vector3(0f, 4.12f, -0.85f), new Vector3(-1.75f, 6.08f, -0.65f), new Vector3(1.75f, 6.08f, -0.65f), new Vector3(-2.2f, 2.66f, -1.62f));

        static MeshBuilder BuildInn(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_inn", b), 0.06f, 0.3f, 0.9f);
            const float W1 = 6.4f, Z0 = -0.5f, Z1 = 4.5f, YF0 = 0.45f, YF = 3.1f;     // ground floor
            const float W2 = 6.7f, Z0u = -0.75f, YT = 5.55f;                         // jettied upper floor
            float zc = (Z0 + Z1) * 0.5f, zcu = (Z0u + Z1) * 0.5f;
            var timber = Pal.TimberDark;
            var upper = Color.Lerp(Pal.PlasterWarm, Pal.Cream, 0.35f + b * 0.1f);
            float fz = Z0 - 0.025f, fzu = Z0u - 0.025f;
            // stone footing + ground floor (timber-framed plaster)
            mb.Color = Pal.Stone;
            mb.BoxOn(new Vector3(0f, 0f, zc), new Vector3(W1, YF0, Z1 - Z0), Pal.StoneLight);
            mb.Color = Pal.Cream;
            mb.BoxOn(new Vector3(0f, YF0, zc - 0.02f), new Vector3(W1 - 0.06f, YF - YF0, Z1 - Z0 - 0.04f));
            mb.Color = timber;
            mb.Box(new Vector3(0f, YF0 + 0.07f, fz), new Vector3(W1, 0.14f, 0.07f));
            foreach (float x in new[] { -3.12f, -1.62f, 1.62f, 3.12f })
                mb.Box(new Vector3(x, (YF0 + YF) * 0.5f, fz), new Vector3(0.15f, YF - YF0, 0.07f));
            for (int s = -1; s <= 1; s += 2)
            {
                Beam(mb, new Vector3(s * 3.05f, YF0 + 0.15f, fz), new Vector3(s * 2.95f, 1.1f, fz), 0.11f, 0.06f, Vector3.back);
                mb.Box(new Vector3(s * 2.37f, 1.08f, fz), new Vector3(1.4f, 0.1f, 0.06f));
                Beam(mb, new Vector3(s * 1.7f, YF0 + 0.15f, fz), new Vector3(s * 1.8f, 1.08f, fz), 0.11f, 0.06f, Vector3.back);
            }
            // double door, lanterns, windows
            Door(mb, 0f, YF0, 1.36f, 2.4f, Z0, Pal.Wood, timber, true);
            mb.Color = Paint.Shade(Pal.Wood, 0.6f);
            mb.Box(new Vector3(0f, YF0 + 0.9f, Z0 - 0.075f), new Vector3(0.04f, 1.75f, 0.02f));
            mb.Color = Pal.StoneLight;
            mb.BoxOn(new Vector3(0f, 0f, Z0 - 0.25f), new Vector3(1.8f, 0.18f, 0.5f));
            mb.BoxOn(new Vector3(0f, 0f, Z0 - 0.58f), new Vector3(1.5f, 0.09f, 0.3f));
            for (int s = -1; s <= 1; s += 2)
            {
                Window(mb, s * 2.37f, 1.85f, 1.0f, 1.0f, Z0, lit, timber, null);
                mb.Color = Pal.Iron;
                Beam(mb, new Vector3(s * 1.12f, 2.95f, Z0), new Vector3(s * 1.12f, 2.95f, Z0 - 0.4f), 0.04f, 0.05f);
                Beam(mb, new Vector3(s * 1.12f, 2.6f, Z0), new Vector3(s * 1.12f, 2.95f, Z0 - 0.3f), 0.03f, 0.03f, Vector3.right);
                mb.Color = Pal.Ink;
                mb.Segment(new Vector3(s * 1.12f, 2.95f, Z0 - 0.38f), new Vector3(s * 1.12f, 2.84f, Z0 - 0.38f), 0.012f, 0.012f, 3);
                PaperLantern(mb, new Vector3(s * 1.12f, 2.6f, Z0 - 0.38f), 0.17f, 0.36f, Paint.Hex("#E86A4E"), lit, Pal.TimberDark);
            }
            // barrels and a crate by the door
            BarrelGeom(mb, new Vector3(1.85f, 0f, -0.8f), 0.3f, 0.82f, Pal.Wood, true);
            BarrelGeom(mb, new Vector3(2.45f, 0f, -0.55f), 0.26f, 0.72f, Pal.WoodLight, true);
            CrateGeom(mb, new Vector3(-1.95f, 0f, -0.78f), 0.55f, Pal.WoodLight, 8f, true);
            // jetty beam + upper floor
            mb.Color = timber;
            mb.Box(new Vector3(0f, YF + 0.07f, (Z0u + Z0) * 0.5f - 0.02f), new Vector3(W2, 0.2f, Z0 - Z0u + 0.12f));
            mb.Color = upper;
            mb.BoxOn(new Vector3(0f, YF + 0.17f, zcu), new Vector3(W2, YT - YF - 0.12f, Z1 - Z0u));
            mb.Color = timber;
            mb.Box(new Vector3(0f, YT - 0.07f, fzu), new Vector3(W2, 0.14f, 0.07f));
            mb.Box(new Vector3(0f, 3.62f, fzu), new Vector3(W2, 0.1f, 0.06f));
            foreach (float x in new[] { -3.28f, -1.98f, -0.62f, 0.62f, 1.98f, 3.28f })
                mb.Box(new Vector3(x, (YF + YT) * 0.5f + 0.08f, fzu), new Vector3(0.14f, YT - YF - 0.15f, 0.07f));
            for (int s = -1; s <= 1; s += 2)
            {
                Beam(mb, new Vector3(s * 3.2f, 3.3f, fzu), new Vector3(s * 2.1f, 3.62f, fzu), 0.1f, 0.06f, Vector3.back);
                Window(mb, s * 1.3f, 4.35f, 0.7f, 0.95f, Z0u, lit, timber, null, true, false);
                Window(mb, s * 2.63f, 4.35f, 0.7f, 0.95f, Z0u, lit, timber, null, true, false);
                FlowerBox(mb, s * 1.3f, 3.74f, 0.9f, Z0u, Pal.Wood, b + (s > 0 ? 2 : 5));
                FlowerBox(mb, s * 2.63f, 3.74f, 0.9f, Z0u, Pal.Wood, b + (s > 0 ? 4 : 1));
            }
            // balcony with a glazed door
            Window(mb, 0f, 4.12f, 0.8f, 1.5f, Z0u, lit, timber, null, false, true);
            mb.Color = Pal.Wood;
            mb.Box(new Vector3(0f, 3.1f, (Z0u - 1.5f) * 0.5f), new Vector3(2.2f, 0.12f, 1.5f + Z0u));
            for (int s = -1; s <= 1; s += 2)
                Beam(mb, new Vector3(s * 0.85f, 2.55f, Z0 - 0.03f), new Vector3(s * 0.85f, 3.04f, -1.35f), 0.1f, 0.1f, Vector3.right);
            mb.Color = Pal.WoodLight;
            for (int i = 0; i <= 6; i++)
                mb.BoxOn(new Vector3(-1.04f + i * 0.3467f, 3.16f, -1.46f), new Vector3(0.05f, 0.82f, 0.05f));
            for (int s = -1; s <= 1; s += 2)
                mb.BoxOn(new Vector3(s * 1.04f, 3.16f, -1.1f), new Vector3(0.05f, 0.82f, 0.05f));
            mb.Color = Pal.Wood;
            mb.Box(new Vector3(0f, 4.0f, -1.46f), new Vector3(2.2f, 0.08f, 0.1f));
            mb.Box(new Vector3(-1.04f, 4.0f, -1.1f), new Vector3(0.1f, 0.08f, 0.75f));
            mb.Box(new Vector3(1.04f, 4.0f, -1.1f), new Vector3(0.1f, 0.08f, 0.75f));
            // terracotta roof with two dormers, chimney
            const float rh = 2.45f, ov = 0.4f;
            var tile = Paint.Hsv(Pal.Terracotta, b * 3f - 4f, 0.95f);
            CourseRoof(mb, new Vector3(0f, YT, zcu), W2, Z1 - Z0u, rh, ov, 5, tile, Paint.Shade(tile, 0.9f), Pal.TerracottaDark, false, false);
            mb.Color = upper;
            GableWall(mb, -W2 * 0.5f, YT, Z0u, Z1, rh - 0.1f, -1f);
            GableWall(mb, W2 * 0.5f, YT, Z0u, Z1, rh - 0.1f, 1f);
            float hz = (Z1 - Z0u) * 0.5f + ov, drop = rh * ov / ((Z1 - Z0u) * 0.5f);
            float SlopeZ(float y) => zcu - hz + (y - (YT - drop)) / (rh + drop) * hz;
            Dormer(mb, -1.75f, 1.05f, YT + 0.1f, 0.85f, SlopeZ, upper, tile, timber, lit, false);
            Dormer(mb, 1.75f, 1.05f, YT + 0.1f, 0.85f, SlopeZ, upper, tile, timber, lit, false);
            Chimney(mb, new Vector3(2.55f, 6.2f, 3.2f), 0.66f, 0.58f, 2.55f, Pal.StoneDark, -2f);
            // hanging sign (crescent moon + lantern) and the sleepy lantern in its nightcap
            const float sx = -2.75f;
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(sx, 3.0f, Z0), new Vector3(sx, 3.0f, -1.7f), 0.06f, 0.07f);
            Beam(mb, new Vector3(sx, 2.55f, Z0), new Vector3(sx, 3.0f, -1.2f), 0.04f, 0.04f, Vector3.right);
            Beam(mb, new Vector3(sx, 3.0f, -1.62f), new Vector3(sx + 0.62f, 3.0f, -1.62f), 0.05f, 0.05f);
            foreach (float cz in new[] { -0.82f, -1.38f })
                mb.Segment(new Vector3(sx, 3.0f, cz), new Vector3(sx, 2.72f, cz), 0.014f, 0.014f, 3);
            mb.Color = Pal.WoodLight;
            var board = NgonPoints(new Vector3(sx, 2.42f, -1.1f), 0.5f, 0.32f, 8, Vector3.right, Vector3.up, 22.5f);
            Slab(mb, board, Vector3.back, 0.06f);
            mb.Color = Pal.TimberDark;
            var rim = NgonPoints(new Vector3(sx, 2.42f, -1.08f), 0.55f, 0.36f, 8, Vector3.right, Vector3.up, 22.5f);
            Slab(mb, rim, Vector3.back, 0.03f);
            mb.Color = Pal.Gold;
            Ngon(mb, new Vector3(sx - 0.2f, 2.44f, -1.112f), 0.17f, 10, Vector3.back, Vector3.up, 0f, Pal.Gold);
            Ngon(mb, new Vector3(sx - 0.13f, 2.5f, -1.116f), 0.15f, 10, Vector3.back, Vector3.up, 0f, Paint.Shade(Pal.WoodLight, 0.97f));
            mb.Emission = 0.6f;
            QuadC(mb, new Vector3(sx + 0.13f, 2.3f, -1.115f), new Vector3(sx + 0.13f, 2.5f, -1.115f), new Vector3(sx + 0.29f, 2.5f, -1.115f), new Vector3(sx + 0.29f, 2.3f, -1.115f), Vector3.back, Pal.Honey);
            mb.Emission = 0f;
            Tri(mb, new Vector3(sx + 0.1f, 2.5f, -1.115f), new Vector3(sx + 0.21f, 2.6f, -1.115f), new Vector3(sx + 0.32f, 2.5f, -1.115f), Vector3.back, Pal.TimberDark);
            SleepyLantern(mb, new Vector3(sx + 0.55f, 2.66f, -1.62f), lit);
            mb.Color = Pal.Ink;
            mb.Segment(new Vector3(sx + 0.55f, 3.0f, -1.62f), new Vector3(sx + 0.55f, 2.84f, -1.62f), 0.012f, 0.012f, 3);
            return mb;
        }

        /// <summary>The inn's sign lantern, wearing a striped nightcap with a pom-pom (centre of the glass at c).</summary>
        static void SleepyLantern(MeshBuilder mb, Vector3 c, bool lit)
        {
            var keepC = mb.Color;
            mb.Emission = lit ? 1f : 0f;
            mb.Color = lit ? Pal.Glow : Pal.GlassDark;
            mb.Box(c, new Vector3(0.2f, 0.24f, 0.2f));
            mb.Emission = 0f;
            mb.Color = Pal.Iron;
            mb.Box(c - new Vector3(0f, 0.135f, 0f), new Vector3(0.26f, 0.04f, 0.26f));
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                mb.Box(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.135f, new Vector3(0.03f, 0.25f, 0.03f));
            }
            // nightcap: brim, then a droopy striped cone ending in a pom-pom
            var b0 = c + new Vector3(0f, 0.13f, 0f);
            mb.Color = Pal.Paper;
            mb.Cylinder(b0, 0.17f, 0.16f, 0.06f, 8);
            var pts = new[] { b0 + new Vector3(0f, 0.05f, 0f), b0 + new Vector3(0.01f, 0.2f, 0f), b0 + new Vector3(0.07f, 0.3f, 0f), b0 + new Vector3(0.17f, 0.32f, 0f), b0 + new Vector3(0.25f, 0.24f, 0f), b0 + new Vector3(0.29f, 0.13f, 0f) };
            float[] radii = { 0.15f, 0.115f, 0.085f, 0.06f, 0.04f, 0.025f };
            for (int i = 0; i < pts.Length - 1; i++)
            {
                mb.Color = i % 2 == 0 ? Pal.DustyBlue : Pal.Paper;
                mb.Segment(pts[i], pts[i + 1], radii[i], radii[i + 1], 7, false, true);
            }
            mb.Color = Color.white;
            mb.Sphere(pts[pts.Length - 1] + new Vector3(0f, -0.04f, 0f), 0.06f, 6, 4, false);
            mb.Color = keepC;
        }

        // ------------------------------------------------------------------ smithy: open shed, forge glow

        static PropModel Smithy(string art, int seed) =>
            LitBuilding(art, seed, BuildSmithy, 2.5f, new Vector3(0.9f, 1.15f, 1.25f));

        static MeshBuilder BuildSmithy(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_smithy", b), 0.07f, 0.3f, 0.7f);
            const float XP = 2.15f, ZF = -0.3f, ZB = 2.55f, HF = 3.9f, HB = 3.05f;
            var timber = Pal.Timber;
            // floor slab
            mb.Color = Pal.StoneDark;
            mb.BoxOn(new Vector3(0f, 0f, (ZF - 0.12f + ZB + 0.2f) * 0.5f), new Vector3(4.6f, 0.1f, ZB - ZF + 0.32f), Paint.Shade(Pal.Stone, 0.9f));
            // posts and beams
            mb.Color = timber;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.BoxOn(new Vector3(s * XP, 0.1f, ZF), new Vector3(0.2f, HF - 0.1f, 0.2f));
                mb.BoxOn(new Vector3(s * XP, 0.1f, ZB), new Vector3(0.2f, HB - 0.1f, 0.2f));
                Beam(mb, new Vector3(s * XP, HF - 0.1f, ZF), new Vector3(s * XP, HB - 0.1f, ZB), 0.16f, 0.18f);
                Beam(mb, new Vector3(s * XP, HF - 0.75f, ZF + 0.05f), new Vector3(s * (XP - 0.6f), HF - 0.1f, ZF + 0.05f), 0.12f, 0.12f, Vector3.back);
            }
            mb.Box(new Vector3(0f, HF - 0.1f, ZF), new Vector3(XP * 2f + 0.25f, 0.2f, 0.2f));
            mb.Box(new Vector3(0f, HB - 0.1f, ZB), new Vector3(XP * 2f + 0.25f, 0.2f, 0.2f));
            // back plank wall with tools, low side wall on the left
            PlankWall(mb, new Vector3(0f, 0.1f, ZB + 0.06f), XP * 2f, HB - 0.25f, 0.1f, Pal.WoodGrey, 0.32f, false);
            mb.Push().Rotate(0f, 90f, 0f);
            PlankWall(mb, new Vector3(-1.75f, 0.1f, -XP - 0.02f), 1.6f, 1.35f, 0.08f, Pal.WoodGrey, 0.3f, false);
            mb.Pop();
            mb.Color = Pal.Iron;
            float wz = ZB - 0.02f;
            mb.Box(new Vector3(-1.5f, 2.3f, wz), new Vector3(0.05f, 0.6f, 0.04f));              // hammer handle
            mb.Box(new Vector3(-1.5f, 2.6f, wz - 0.03f), new Vector3(0.24f, 0.1f, 0.08f));       // hammer head
            Beam(mb, new Vector3(-1.05f, 2.7f, wz), new Vector3(-1.15f, 1.95f, wz), 0.035f, 0.03f, Vector3.back);  // tongs
            Beam(mb, new Vector3(-1.0f, 2.7f, wz), new Vector3(-0.88f, 1.95f, wz), 0.035f, 0.03f, Vector3.back);
            mb.Push().Translate(-0.35f, 2.45f, wz).Rotate(90f, 0f, 0f);
            mb.Torus(Vector3.zero, 0.13f, 0.03f, 8, 3);                                          // horseshoes
            mb.Torus(new Vector3(0.36f, 0f, 0.12f), 0.13f, 0.03f, 8, 3);
            mb.Pop();
            // forge: stone block, coal bed (glows), hood and chimney
            const float fx = 0.9f;
            mb.Color = Pal.Stone;
            mb.Jitter = 0.1f;
            mb.BoxOn(new Vector3(fx, 0.1f, 1.6f), new Vector3(1.5f, 0.78f, 1.6f), Pal.StoneLight);
            for (int i = 0; i < 6; i++)
            {
                mb.Color = mb.Random01() < 0.5f ? Pal.StoneLight : Pal.StoneDark;
                mb.Box(new Vector3(fx - 0.6f + mb.Random01() * 1.2f, 0.25f + mb.Random01() * 0.45f, 0.79f), new Vector3(0.3f, 0.16f, 0.04f));
            }
            mb.Jitter = 0.07f;
            mb.Color = Pal.Charcoal;
            mb.BoxOn(new Vector3(fx, 0.86f, 1.55f), new Vector3(1.1f, 0.06f, 1.1f));
            mb.Emission = lit ? 1f : 0f;
            for (int i = 0; i < 11; i++)
            {
                float t = mb.Random01();
                mb.Color = lit ? Color.Lerp(Pal.Ember, Pal.FireCore, t) : Color.Lerp(Pal.Charcoal, Pal.StoneDark, t * 0.5f);
                Gem(mb, new Vector3(fx - 0.42f + mb.Random01() * 0.84f, 0.95f, 1.15f + mb.Random01() * 0.8f), new Vector3(0.1f, 0.07f, 0.1f));
            }
            mb.Emission = 0f;
            mb.Color = Pal.StoneDark;
            mb.TaperedBox(new Vector3(fx, 1.85f, 1.75f), new Vector3(1.45f, 0.85f, 1.3f), 0.55f);
            mb.Color = Pal.Iron;
            mb.Box(new Vector3(fx - 0.62f, 1.36f, 1.25f), new Vector3(0.05f, 0.95f, 0.05f));
            mb.Box(new Vector3(fx + 0.62f, 1.36f, 1.25f), new Vector3(0.05f, 0.95f, 0.05f));
            Chimney(mb, new Vector3(fx, 2.65f, 1.85f), 0.62f, 0.56f, 2.75f, Pal.Stone, 0f);
            // bellows beside the forge
            mb.Color = Paint.Hex("#7A4E36");
            mb.Push().Translate(1.95f, 0.75f, 1.2f).Rotate(0f, 0f, 12f);
            mb.TaperedBox(Vector3.zero, new Vector3(0.38f, 0.22f, 0.6f), 0.0f, 0.75f);
            mb.Pop();
            mb.Color = timber;
            mb.BoxOn(new Vector3(1.95f, 0.1f, 1.2f), new Vector3(0.12f, 0.65f, 0.12f));
            // anvil on a stump
            mb.Color = Pal.Wood;
            mb.Cylinder(new Vector3(-0.6f, 0.1f, 0.55f), 0.3f, 0.27f, 0.42f, 8);
            mb.Color = Paint.Shade(Pal.WoodLight, 1.05f);
            mb.Disc(new Vector3(-0.6f, 0.521f, 0.55f), 0.24f, 8);
            mb.Color = Pal.Iron;
            mb.TaperedBox(new Vector3(-0.6f, 0.52f, 0.55f), new Vector3(0.36f, 0.12f, 0.26f), 0.3f);
            mb.BoxOn(new Vector3(-0.6f, 0.63f, 0.55f), new Vector3(0.16f, 0.14f, 0.14f));
            mb.BoxOn(new Vector3(-0.55f, 0.76f, 0.55f), new Vector3(0.5f, 0.12f, 0.2f));
            mb.Push().Translate(-0.8f, 0.82f, 0.55f).Rotate(0f, 0f, 90f);
            mb.Cone(Vector3.zero, 0.07f, 0.28f, 6);
            mb.Pop();
            // quench tub, sacks, firewood
            BarrelGeom(mb, new Vector3(-1.62f, 0.1f, 0.15f), 0.3f, 0.62f, Pal.Wood);
            mb.Color = Pal.Water;
            mb.Emission = 0.15f;
            mb.Disc(new Vector3(-1.62f, 0.66f, 0.15f), 0.24f, 10);
            mb.Emission = 0f;
            Sack(mb, new Vector3(-1.45f, 0.1f, 1.75f), 0.28f, b + 3);
            Sack(mb, new Vector3(-1.05f, 0.1f, 2.0f), 0.24f, b + 7);
            mb.Color = Pal.Wood;
            for (int i = 0; i < 6; i++)
            {
                float y = 0.25f + (i / 3) * 0.2f, z = 1.0f + (i % 3) * 0.21f + (i / 3) * 0.1f;
                mb.Push().Translate(-1.95f, y, z).Rotate(0f, 0f, 90f);
                mb.Cylinder(new Vector3(0f, -0.3f, 0f), 0.1f, 0.1f, 0.6f, 6);
                mb.Pop();
            }
            // shed roof of wooden shingles (front low, back high), mossy here and there
            var front = new Vector3(0f, HF + 0.15f, ZF - 0.45f);
            var back = new Vector3(0f, HB - 0.05f, ZB + 0.4f);
            var dir = (back - front).normalized;
            float len = (back - front).magnitude;
            var n = Vector3.Cross(dir, Vector3.right).normalized;
            if (n.y < 0f) n = -n;
            // courses overlap from the low (back) edge upwards: build them from the back
            dir = -dir;
            var tmp = front; front = back; back = tmp;
            mb.Color = Paint.Shade(Pal.Wood, 0.75f);
            OBox(mb, (front + back) * 0.5f, new Vector3(5.1f, 0.1f, len), Quaternion.LookRotation(dir, n));
            const int courses = 5;
            for (int k = 0; k < courses; k++)
            {
                mb.Color = k % 2 == 0 ? Paint.Hex("#8A6A4E") : Paint.Hex("#7A5D45");
                var c = front + dir * (len / courses * (k + 0.55f)) + n * 0.08f;
                OBox(mb, c, new Vector3(5.16f, 0.06f, len / courses * 1.12f), Quaternion.LookRotation(dir, n) * Quaternion.Euler(6f, 0f, 0f));
            }
            for (int i = 0; i < 3; i++)
            {
                var mp = front + dir * (len * (0.15f + mb.Random01() * 0.6f)) + n * 0.1f + Vector3.right * (mb.Random01() * 4f - 2f);
                mb.Push().Translate(mp).Rotate(Quaternion.LookRotation(dir, n));
                FacetBlob(mb, Vector3.zero, new Vector3(0.45f, 0.05f, 0.35f), 0, 0.25f, b * 5 + i, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
                mb.Pop();
            }
            // a lucky horseshoe over the front beam
            mb.Color = Pal.Iron;
            mb.Push().Translate(0f, HF - 0.38f, ZF - 0.12f).Rotate(90f, 0f, 0f);
            mb.Torus(Vector3.zero, 0.12f, 0.03f, 8, 3);
            mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ windmill: sails turn slowly (PropMotion on the "Sails" child)

        const float WindmillZ = 0.72f, WindmillHubY = 8.75f, WindmillHubZ = -1.32f, WindmillSail = 4.6f;

        static PropModel Windmill(string art, int seed)
        {
            int b = Bucket(seed);
            string key = art + "#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, true, lit => BuildWindmill(b, lit)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => BuildWindmill(b, lit));
            var hub = new Vector3(0f, WindmillHubY, WindmillHubZ);
            var sails = rig.Part("Sails", Cached(art + "#sails" + b, () => BuildSails(b)), hub, Quaternion.identity);
            rig.Light(new Vector3(0f, 4.3f, -0.35f));
            rig.Light(new Vector3(0f, 6.75f, -0.25f));
            var model = rig.Done(1.6f, -1f, new Bounds(hub, new Vector3(WindmillSail * 2f + 0.4f, WindmillSail * 2f + 0.4f, 0.4f)));
            // bounds above are taken with the sails axis-aligned (= their swept square); now give each windmill its own start angle
            sails.transform.localRotation = Quaternion.Euler(0f, 0f, b * 20f);
            var motion = sails.gameObject.AddComponent<PropMotion>();
            motion.SpinAxis = Vector3.forward;
            motion.SpinSpeed = -(14f + b * 2f);
            return model;
        }

        static MeshBuilder BuildSails(int b)
        {
            var mb = Builder(VariantSeed("prop_windmill_sails", b), 0.05f, 0f, 1f);
            const float R = WindmillSail;
            mb.Color = Pal.TimberDark;
            mb.Push().Rotate(90f, 0f, 0f);
            mb.Cylinder(new Vector3(0f, -0.18f, 0f), 0.3f, 0.24f, 0.32f, 8);
            mb.Pop();
            for (int i = 0; i < 4; i++)
            {
                mb.Push().Rotate(0f, 0f, i * 90f);
                mb.Color = Pal.Timber;
                mb.Box(new Vector3(0f, R * 0.5f + 0.1f, 0f), new Vector3(0.15f, R - 0.2f, 0.13f));
                mb.Color = Pal.WoodLight;
                mb.Box(new Vector3(0.88f, (0.9f + R - 0.1f) * 0.5f, 0.02f), new Vector3(0.06f, R - 1.0f, 0.06f));
                for (int k = 0; k < 7; k++)
                    mb.Box(new Vector3(0.46f, 0.95f + k * (R - 1.1f) / 6f, 0.02f), new Vector3(0.84f, 0.05f, 0.05f));
                mb.Color = (i + b) % 4 == 3 ? Paint.Shade(Pal.Cream, 0.92f) : Pal.Cream;
                mb.Box(new Vector3(0.48f, (1.15f + R - 0.25f) * 0.5f, 0.075f), new Vector3(0.7f, R - 1.45f, 0.03f));
                mb.Pop();
            }
            return mb;
        }

        static MeshBuilder BuildWindmill(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_windmill", b), 0.07f, 0.3f, 0.8f);
            const float ZC = WindmillZ;
            var timber = Pal.TimberDark;
            mb.Push().Translate(0f, 0f, ZC);
            // octagonal stone base (a flat face to the front), plaster tower, timber band
            mb.Color = Pal.Stone;
            mb.Jitter = 0.1f;
            mb.Lathe(new[] { new Vector2(1.24f, 0f), new Vector2(1.21f, 0.9f), new Vector2(1.18f, 1.8f), new Vector2(1.15f, 2.62f) }, 8, false, false, true,
                     new[] { Pal.StoneDark, Pal.Stone, Pal.StoneLight, Pal.Stone }, 22.5f);
            mb.Jitter = 0.06f;
            mb.Color = Color.Lerp(Pal.Cream, Pal.PlasterWarm, b * 0.15f);
            mb.Lathe(new[] { new Vector2(1.1f, 2.6f), new Vector2(0.98f, 5.4f), new Vector2(0.86f, 8.05f) }, 8, false, false, true, null, 22.5f);
            mb.Color = timber;
            mb.Lathe(new[] { new Vector2(1.0f, 5.3f), new Vector2(0.99f, 5.48f) }, 8, false, true, true, null, 22.5f);
            // gallery (reefing stage) with railing and brackets
            mb.Color = Pal.Wood;
            mb.Lathe(new[] { new Vector2(1.1f, 2.48f), new Vector2(1.72f, 2.48f), new Vector2(1.72f, 2.6f), new Vector2(1.1f, 2.6f) }, 12, false, false, false);
            const int posts = 12;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= posts; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / posts;
                var p = new Vector3(Mathf.Cos(a) * 1.66f, 2.6f, Mathf.Sin(a) * 1.66f);
                mb.Color = Pal.WoodLight;
                if (i < posts) mb.BoxOn(p, new Vector3(0.06f, 0.82f, 0.06f));
                var top = p + new Vector3(0f, 0.82f, 0f);
                if (i > 0) Beam(mb, prev, top, 0.07f, 0.06f);
                prev = top;
                if (i < posts && i % 2 == 0)
                {
                    mb.Color = timber;
                    Beam(mb, new Vector3(Mathf.Cos(a) * 1.17f, 1.9f, Mathf.Sin(a) * 1.17f), new Vector3(Mathf.Cos(a) * 1.6f, 2.46f, Mathf.Sin(a) * 1.6f), 0.08f, 0.08f);
                }
            }
            // thatched cap and finial
            mb.Color = Pal.Thatch;
            mb.Lathe(new[] { new Vector2(0.92f, 7.95f), new Vector2(1.2f, 8.2f), new Vector2(1.14f, 8.9f), new Vector2(0.86f, 9.6f), new Vector2(0.46f, 10.12f), new Vector2(0.12f, 10.42f), new Vector2(0f, 10.48f) },
                     10, false, true, false, new[] { Pal.ThatchDark, Pal.ThatchDark, Pal.Thatch, Pal.Thatch, Pal.ThatchLight, Pal.ThatchLight, Pal.ThatchLight }, 9f);
            mb.Color = Pal.Gold;
            Gem(mb, new Vector3(0f, 10.6f, 0f), 0.13f);
            mb.Color = timber;
            mb.Cylinder(new Vector3(0f, 10.42f, 0f), 0.04f, 0.03f, 0.1f, 4);
            mb.Pop();
            // windshaft to the hub
            mb.Color = timber;
            mb.Segment(new Vector3(0f, WindmillHubY, ZC - 0.6f), new Vector3(0f, WindmillHubY, WindmillHubZ + 0.1f), 0.13f, 0.12f, 6);
            // door on the front face of the octagon, windows up the tower
            float ap0 = 1.24f * Mathf.Cos(22.5f * Mathf.Deg2Rad);
            Door(mb, 0f, 0f, 0.85f, 2.0f, ZC - ap0 + 0.02f, Pal.Wood, timber, true);
            float RadiusAt(float y) => y < 5.4f ? Mathf.Lerp(1.1f, 0.98f, (y - 2.6f) / 2.8f) : Mathf.Lerp(0.98f, 0.86f, (y - 5.4f) / 2.65f);
            float ApZ(float y) => ZC - RadiusAt(y) * Mathf.Cos(22.5f * Mathf.Deg2Rad);
            Window(mb, 0f, 4.3f, 0.5f, 0.7f, ApZ(4.3f), lit, timber, Pal.Shutters[b]);
            Window(mb, 0f, 6.75f, 0.42f, 0.56f, ApZ(6.75f), lit, timber, null, false, false);
            // a pair of flour sacks by the door
            Sack(mb, new Vector3(-0.85f, 0f, 0.02f), 0.24f, b + 11);
            Sack(mb, new Vector3(-0.58f, 0f, 0.12f), 0.2f, b + 13);
            return mb;
        }
    }
}
