// Village garden & yard dressing (the snug front band of Lanternvale): a low dry-stone wall of coursed field stones under a
// flat cap course (moss tufts in a few joints and over one end), a raised
// vegetable patch (cabbages, carrots, leeks, a pumpkin and a watering can) and a washing line with pegged laundry that
// flutters in the wind. Same conventions as the rest of the library: front faces −Z, the solid base fits the collider.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterGarden(Dictionary<string, Recipe> r)
        {
            r["prop_stone_wall"] = (art, seed) => Simple(art, seed, BuildStoneWall, 1.6f);
            r["prop_veg_patch"] = (art, seed) => Swaying(art, seed, BuildVegPatch, 1.4f);
            r["prop_washing_line"] = (art, seed) => Swaying(art, seed, BuildWashingLine, 1.7f);
        }

        // ------------------------------------------------------------------ dry-stone wall (collider 3.4 × 0.6: the ends fit)

        /// <summary>Warm sandstone and grey field stones (sRGB): the wall sits in a sunny meadow.</summary>
        static readonly Color[] WallStones =
        {
            Paint.Hex("#CDB898"), Paint.Hex("#D8C4A0"), Paint.Hex("#BCAE98"), Paint.Hex("#C9B08C"), Paint.Hex("#B4ACA0"), Paint.Hex("#D2BEA0"),
            Paint.Hex("#AFA699"),
        };

        /// <summary>
        /// One dressed field stone centred on c: an octagon in the front plane (a rectangle with clipped corners, so the face
        /// reads rounded) extruded through the wall's depth, its front face a touch proud of its back. Flat-shaded, one
        /// colour with a slightly darker underside band.
        /// </summary>
        static void CutStone(MeshBuilder mb, Vector3 c, Vector3 size, float chamfer, Color col, float tiltDeg = 0f)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            float cx = Mathf.Min(chamfer, hx * 0.45f), cy = Mathf.Min(chamfer, hy * 0.45f);
            var oct = new[]
            {
                new Vector2(-hx + cx, -hy), new Vector2(hx - cx, -hy), new Vector2(hx, -hy + cy), new Vector2(hx, hy - cy),
                new Vector2(hx - cx, hy), new Vector2(-hx + cx, hy), new Vector2(-hx, hy - cy), new Vector2(-hx, -hy + cy),
            };
            var rot = Quaternion.Euler(0f, 0f, tiltDeg);
            Vector3[] Ring(float z, float k)
            {
                var r = new Vector3[oct.Length];
                for (int i = 0; i < oct.Length; i++) r[i] = c + rot * new Vector3(oct[i].x * k, oct[i].y * k, z);
                return r;
            }
            var under = Paint.Shade(col, 0.86f);
            Loft(mb, new[] { Ring(hz, 0.94f), Ring(-hz, 1f) }, true, true, n => n.y < -0.4f ? under : col);
        }

        static MeshBuilder BuildStoneWall(int b)
        {
            var mb = Builder(VariantSeed("prop_stone_wall", b), 0.05f, 0.25f, 0.45f);
            const float L = 2.9f;
            Color Stone() => Paint.Shade(WallStones[(int)(mb.Random01() * WallStones.Length) % WallStones.Length], 0.95f + 0.09f * mb.Random01());
            // two courses of flat, wide stones (joints staggered, the face battered back a little), the ends of each course
            // a little shallower (cheek ends), then a flat cap course slightly overhanging
            float[] courseH = { 0.25f, 0.21f };
            float[] depth = { 0.33f, 0.3f };
            float y = 0f;
            for (int c = 0; c < courseH.Length; c++)
            {
                float h = courseH[c];
                float x = -L * 0.5f, right = L * 0.5f;
                float first = 0.32f + 0.18f * ((b + c) % 2);
                int guard = 0;
                while (x < right - 0.05f && guard++ < 16)
                {
                    float len = guard == 1 ? first : 0.36f + 0.26f * mb.Random01();
                    if (right - (x + len) < 0.22f) len = right - x;
                    float sh = h * (0.92f + 0.08f * mb.Random01());
                    bool end = x <= -L * 0.5f + 0.01f || x + len >= right - 0.01f;
                    float d = depth[c] * (end ? 0.88f : 1f) * (0.96f + 0.04f * mb.Random01());
                    var at = new Vector3(x + len * 0.5f, y + sh * 0.5f, (mb.Random01() - 0.5f) * 0.015f + c * 0.01f);
                    CutStone(mb, at, new Vector3(len - 0.025f, sh - 0.02f, d), 0.05f, Stone(), (mb.Random01() - 0.5f) * 2.5f);
                    x += len;
                }
                y += h;
            }
            // cap course
            {
                float x = -L * 0.5f - 0.02f, right = L * 0.5f + 0.02f;
                int guard = 0;
                while (x < right - 0.05f && guard++ < 12)
                {
                    float len = 0.42f + 0.22f * mb.Random01();
                    if (right - (x + len) < 0.25f) len = right - x;
                    bool end = guard == 1 || x + len >= right - 0.01f;
                    // weathered caps: a shade darker than the courses (their tops face the sun), uneven in thickness
                    var col = Paint.Shade(Stone(), 0.92f);
                    float ch = 0.08f + 0.03f * mb.Random01();
                    CutStone(mb, new Vector3(x + len * 0.5f, y + ch * 0.5f, 0.01f), new Vector3(len - 0.02f, ch, end ? 0.3f : 0.34f), 0.03f, col, (mb.Random01() - 0.5f) * 2.5f);
                    x += len;
                }
                y += 0.09f;
            }
            // small dark moss cushions in a few joints and over one end of the cap, a few blades at the foot (kept inside
            // the collider)
            var mossRamp = CanopyRamp(Paint.Hex("#86A35A"), Pal.Moss, Pal.LeafDark, 0f, y + 0.06f);
            float endX = (b % 2 == 0 ? 1f : -1f) * (L * 0.5f - 0.18f);
            SoftLump(mb, new Vector3(endX, y - 0.005f, 0.0f), new Vector3(0.2f, 0.05f, 0.15f), 0, b * 31f, mossRamp, 0.15f);
            SoftLump(mb, new Vector3(endX + Mathf.Sign(endX) * 0.05f, y - 0.12f, -0.12f), new Vector3(0.1f, 0.08f, 0.04f), 0, b * 17f, mossRamp, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                float mx = -0.85f + i * 0.85f + (mb.Random01() - 0.5f) * 0.3f;
                SoftLump(mb, new Vector3(mx, 0.25f + (i % 2) * 0.21f, -0.165f), new Vector3(0.07f, 0.035f, 0.025f), 0, i * 50f + b, mossRamp, 0.1f);
            }
            for (int i = 0; i < 4; i++)
                Tuft(mb, new Vector3(-1.05f + i * 0.7f + (mb.Random01() - 0.5f) * 0.15f, 0f, -0.11f), 0.18f + 0.07f * mb.Random01(), 4, i % 2 == 0 ? Pal.Leaf : Pal.Sage);
            Color[] wild = { Color.white, Paint.Hex("#FFD84E"), Paint.Hex("#B48CE0") };
            for (int i = 0; i < 2; i++)
            {
                var at = new Vector3(-0.6f + i * 1.15f + (mb.Random01() - 0.5f) * 0.2f, 0.13f + 0.05f * mb.Random01(), -0.19f);
                mb.Color = Pal.Leaf;
                mb.Segment(at + Vector3.down * at.y, at, 0.012f, 0.008f, 3, false, false);
                Bloom(mb, at, 0.055f, new Vector3(0f, 0.8f, -0.6f), wild[(i + b) % wild.Length], Paint.Hex("#F2B02E"), i * 23f);
            }
            return mb;
        }

        // ------------------------------------------------------------------ vegetable patch (collider 3.6 × 1.8: the bed's corners and the can fit)

        static MeshBuilder BuildVegPatch(int b)
        {
            var mb = Builder(VariantSeed("prop_veg_patch", b), 0.06f, 0.25f, 0.3f);
            const float W = 2.5f, Dp = 1.15f, H = 0.2f;
            // plank edging with corner posts, soil inside with three ridged rows
            var plank = Color.Lerp(Pal.WoodGrey, Pal.Wood, 0.45f);
            mb.Color = plank;
            mb.BoxOn(new Vector3(0f, 0f, -Dp * 0.5f), new Vector3(W, H, 0.06f));
            mb.BoxOn(new Vector3(0f, 0f, Dp * 0.5f), new Vector3(W, H, 0.06f));
            mb.BoxOn(new Vector3(-W * 0.5f, 0f, 0f), new Vector3(0.06f, H, Dp));
            mb.BoxOn(new Vector3(W * 0.5f, 0f, 0f), new Vector3(0.06f, H, Dp));
            mb.Color = Pal.Timber;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.BoxOn(new Vector3(sx * W * 0.5f, 0f, sz * Dp * 0.5f), new Vector3(0.09f, H + 0.07f, 0.09f));
            var soil = Paint.Hex("#6E4E3A");
            mb.Color = soil;
            mb.BoxOn(new Vector3(0f, 0f, 0f), new Vector3(W - 0.06f, H - 0.03f, Dp - 0.06f));
            float[] rows = { -0.33f, 0f, 0.33f };
            for (int i = 0; i < rows.Length; i++)
            {
                mb.Color = Paint.Shade(soil, 1.12f);
                mb.TaperedBox(new Vector3(0f, H - 0.03f, rows[i]), new Vector3(W - 0.2f, 0.06f, 0.26f), 0.02f, 0.5f);
            }
            float top = H + 0.03f;
            mb.WindGradient = true; mb.WindY0 = top; mb.WindY1 = top + 0.45f; mb.Wind = 0.7f;
            // front row: cabbages (soft heads with splayed outer leaves)
            var cabbageRamp = CanopyRamp(Paint.Hex("#CFE29A"), Paint.Hex("#93BC72"), Paint.Hex("#5F8A5A"), top, top + 0.24f);
            for (int i = 0; i < 5; i++)
            {
                var at = new Vector3(-0.96f + i * 0.48f + (mb.Random01() - 0.5f) * 0.06f, top, rows[0] + (mb.Random01() - 0.5f) * 0.05f);
                for (int k = 0; k < 5; k++)
                {
                    float a = (k * 72f + i * 31f) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(a), 0.45f, Mathf.Sin(a));
                    Leaf(mb, at + new Vector3(dir.x, 0f, dir.z) * 0.05f, dir, new Vector3(dir.x * -0.4f, 1f, dir.z * -0.4f).normalized, 0.2f,
                         Paint.Shade(Paint.Hex("#7EA866"), 0.92f + 0.12f * mb.Random01()));
                }
                SoftLump(mb, at + Vector3.up * 0.1f, new Vector3(0.13f, 0.12f, 0.13f), 0, i * 40f, cabbageRamp, 0.08f);
            }
            // middle row: carrots (orange shoulders under feathery tops)
            for (int i = 0; i < 7; i++)
            {
                var at = new Vector3(-1.02f + i * 0.34f + (mb.Random01() - 0.5f) * 0.05f, top - 0.01f, rows[1] + (mb.Random01() - 0.5f) * 0.06f);
                mb.Color = Pal.Pumpkin;
                mb.Cone(at, 0.045f, 0.06f, 5);
                for (int k = 0; k < 3; k++)
                {
                    float a = (k * 120f + i * 40f) * Mathf.Deg2Rad;
                    mb.Color = Paint.Shade(Paint.Hex("#6FA34E"), 0.9f + 0.2f * mb.Random01());
                    mb.Blade(at + Vector3.up * 0.04f, at + new Vector3(Mathf.Cos(a) * 0.09f, 0.28f + 0.06f * mb.Random01(), Mathf.Sin(a) * 0.06f), 0.07f);
                }
            }
            // back row: leeks / spring onions (tall blue-green blades on white stems)
            for (int i = 0; i < 6; i++)
            {
                var at = new Vector3(-0.95f + i * 0.38f + (mb.Random01() - 0.5f) * 0.05f, top - 0.01f, rows[2] + (mb.Random01() - 0.5f) * 0.05f);
                mb.Color = Paint.Hex("#EDEBD8");
                mb.Cylinder(at, 0.035f, 0.03f, 0.12f, 5);
                for (int k = 0; k < 3; k++)
                {
                    float a = (k * 120f + i * 25f) * Mathf.Deg2Rad;
                    mb.Color = Paint.Shade(Paint.Hex("#7FAE8C"), 0.92f + 0.14f * mb.Random01());
                    mb.Blade(at + Vector3.up * 0.1f, at + new Vector3(Mathf.Cos(a) * 0.12f, 0.42f + 0.08f * mb.Random01(), Mathf.Sin(a) * 0.08f), 0.05f);
                }
            }
            mb.Wind = 0f;
            // a pumpkin ripening at the back corner and a watering can left beside the bed's end, its spout over the edging
            mb.Color = Pal.Pumpkin;
            Pumpkin(mb, new Vector3(0.98f, top - 0.02f, 0.36f), 0.16f);
            var can = Paint.Hex("#7FA6A8");
            var canAt = new Vector3(W * 0.5f + 0.18f, 0f, -0.14f);
            mb.Color = can;
            mb.Cylinder(canAt, 0.1f, 0.09f, 0.2f, 8);
            mb.Segment(canAt + new Vector3(-0.07f, 0.06f, -0.03f), canAt + new Vector3(-0.24f, 0.24f, -0.08f), 0.025f, 0.018f, 4);
            mb.Color = Paint.Shade(can, 0.8f);
            mb.Segment(canAt + new Vector3(0.07f, 0.19f, 0f), canAt + new Vector3(0.1f, 0.3f, 0f), 0.014f, 0.014f, 3);
            mb.Segment(canAt + new Vector3(0.1f, 0.3f, 0f), canAt + new Vector3(-0.04f, 0.3f, 0f), 0.014f, 0.014f, 3);
            mb.Segment(canAt + new Vector3(-0.04f, 0.3f, 0f), canAt + new Vector3(-0.06f, 0.2f, 0f), 0.014f, 0.014f, 3);
            // grass creeping along the edging
            for (int i = 0; i < 4; i++)
                Tuft(mb, new Vector3(-1.0f + i * 0.66f + (mb.Random01() - 0.5f) * 0.2f, 0f, -Dp * 0.5f - 0.05f), 0.22f, 4, Pal.Leaf);
            return mb;
        }

        // ------------------------------------------------------------------ washing line (collider 3.4 × 0.3; posts at x ±1.6)

        static MeshBuilder BuildWashingLine(int b)
        {
            var mb = Builder(VariantSeed("prop_washing_line", b), 0.05f, 0.25f, 0.4f);
            const float X = 1.6f, Top = 1.95f, LineY = 1.82f, Sag = 0.14f;
            var wood = Color.Lerp(Pal.WoodGrey, Pal.Wood, 0.4f);
            // T posts
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = wood;
                mb.Push().Translate(s * X, 0f, 0f).Rotate(0f, 0f, s * -1.5f);
                mb.BoxOn(Vector3.zero, new Vector3(0.1f, Top, 0.1f));
                mb.Color = Paint.Shade(wood, 1.08f);
                mb.Box(new Vector3(0f, Top - 0.06f, 0.04f), new Vector3(0.09f, 0.07f, 0.36f));
                mb.Pop();
                Tuft(mb, new Vector3(s * X + 0.03f, 0f, 0.05f), 0.3f, 5, Pal.Leaf);
            }
            // the line (front strand carries the laundry)
            var a = new Vector3(-X, LineY, -0.06f);
            var c = new Vector3(X, LineY, -0.06f);
            mb.Color = Pal.RopeStraw;
            Rope(mb, a, c, Sag, 0.012f, 8, 3);
            Rope(mb, new Vector3(-X, LineY, 0.14f), new Vector3(X, LineY, 0.14f), Sag * 0.8f, 0.01f, 6, 3);
            // laundry: a sheet, a shirt, a striped towel and two socks, gently waving (wind grows downwards from the line)
            Color[] sheets = { Paint.Hex("#F6F0E2"), Paint.Hex("#F3E3C8"), Paint.Hex("#EEF2F4"), Paint.Hex("#F6E9EC") };
            Color[] shirts = { Paint.Hex("#7FA7D6"), Paint.Hex("#E3B26A"), Paint.Hex("#8DBB8A"), Paint.Hex("#C98FB4") };
            mb.WindGradient = true; mb.WindY0 = LineY; mb.WindY1 = LineY - 0.9f; mb.Wind = 1.6f;
            Cloth(mb, a, c, Sag, 0.06f, 0.36f, 0.78f, sheets[b], sheets[b], 3);
            Shirt(mb, a, c, Sag, 0.46f, shirts[b]);
            Cloth(mb, a, c, Sag, 0.67f, 0.8f, 0.5f, Paint.Hex("#E59A94"), Paint.Hex("#F6EEDC"), 4);
            Cloth(mb, a, c, Sag, 0.85f, 0.88f, 0.24f, Paint.Hex("#D8735E"), Paint.Hex("#D8735E"), 1);
            Cloth(mb, a, c, Sag, 0.9f, 0.93f, 0.22f, Paint.Hex("#D8735E"), Paint.Hex("#D8735E"), 1);
            mb.Wind = 0f;
            mb.WindGradient = false;
            // a wicker basket of folded laundry under the line by the left post (inside the collider)
            var basket = new Vector3(-X + 0.5f, 0f, 0.1f);
            mb.Color = Pal.WoodLight;
            mb.Push().Translate(basket).Scale(new Vector3(1.25f, 1f, 0.9f));
            mb.Lathe(new[] { new Vector2(0.17f, 0f), new Vector2(0.22f, 0.2f), new Vector2(0.235f, 0.23f), new Vector2(0.21f, 0.23f) }, 8, false, true, false,
                     new[] { Paint.Shade(Pal.WoodLight, 0.85f), Pal.WoodLight, Pal.Wood, Pal.Wood });
            mb.Pop();
            mb.Color = shirts[(b + 1) % shirts.Length];
            mb.Box(new Vector3(basket.x, 0.24f, basket.z), new Vector3(0.38f, 0.07f, 0.26f));
            mb.Color = sheets[(b + 2) % sheets.Length];
            mb.Box(new Vector3(basket.x + 0.02f, 0.3f, basket.z), new Vector3(0.32f, 0.06f, 0.22f));
            return mb;
        }

        /// <summary>Point on a sagging line from a to c at t (0..1).</summary>
        static Vector3 LineAt(Vector3 a, Vector3 c, float sag, float t) => SagPoint(a, c, sag, t);

        /// <summary>
        /// Rectangular cloth pegged to the line between t0 and t1, `length` long, in `strips` vertical folds that alternate
        /// slightly in depth (a soft wave); alternate strips take colour b (stripes) when it differs from a. Two pegs.
        /// </summary>
        static void Cloth(MeshBuilder mb, Vector3 a, Vector3 c, float sag, float t0, float t1, float length, Color colA, Color colB, int strips)
        {
            for (int i = 0; i < strips; i++)
            {
                float u0 = Mathf.Lerp(t0, t1, (float)i / strips), u1 = Mathf.Lerp(t0, t1, (float)(i + 1) / strips);
                var p0 = LineAt(a, c, sag, u0) + Vector3.down * 0.015f;
                var p1 = LineAt(a, c, sag, u1) + Vector3.down * 0.015f;
                float z0 = (i % 2 == 0 ? -0.03f : 0.025f), z1 = ((i + 1) % 2 == 0 ? -0.03f : 0.025f);
                var q0 = new Vector3(p0.x + 0.01f * (i % 2), p0.y - length, p0.z + z0 - 0.02f);
                var q1 = new Vector3(p1.x, p1.y - length + 0.03f * Mathf.Sin(i * 1.7f), p1.z + z1 - 0.02f);
                mb.Color = i % 2 == 0 ? colA : colB;
                Slab(mb, new[] { new Vector3(p0.x, p0.y, p0.z + z0 * 0.3f), new Vector3(p1.x, p1.y, p1.z + z1 * 0.3f), q1, q0 }, Vector3.back, 0.012f);
            }
            Pegs(mb, LineAt(a, c, sag, t0 + (t1 - t0) * 0.12f), LineAt(a, c, sag, t1 - (t1 - t0) * 0.12f));
        }

        static void Shirt(MeshBuilder mb, Vector3 a, Vector3 c, float sag, float t, Color col)
        {
            var mid = LineAt(a, c, sag, t);
            float w = 0.46f, h = 0.5f;
            var l = mid + new Vector3(-w * 0.5f, 0f, 0f);
            var r = mid + new Vector3(w * 0.5f, 0f, 0f);
            mb.Color = col;
            // body
            Slab(mb, new[] { l + Vector3.down * 0.02f, r + Vector3.down * 0.02f, r + new Vector3(-0.03f, -h, -0.02f), l + new Vector3(0.03f, -h, -0.02f) }, Vector3.back, 0.012f);
            // sleeves hanging down and out
            for (int s = -1; s <= 1; s += 2)
            {
                var sh = (s < 0 ? l : r) + Vector3.down * 0.04f;
                var poly = new[] { sh, sh + new Vector3(s * 0.2f, -0.06f, -0.01f), sh + new Vector3(s * 0.16f, -0.28f, -0.02f), sh + new Vector3(0f, -0.2f, -0.01f) };
                if (s > 0) System.Array.Reverse(poly);
                Slab(mb, poly, Vector3.back, 0.012f);
            }
            // collar
            mb.Color = Paint.Shade(col, 1.15f);
            Slab(mb, new[] { mid + new Vector3(-0.08f, -0.01f, -0.015f), mid + new Vector3(0.08f, -0.01f, -0.015f), mid + new Vector3(0f, -0.1f, -0.02f) }, Vector3.back, 0.008f);
            Pegs(mb, l + new Vector3(0.06f, 0f, 0f), r - new Vector3(0.06f, 0f, 0f));
        }

        static void Pegs(MeshBuilder mb, Vector3 p, Vector3 q)
        {
            var keep = mb.Color;
            float keepW = mb.Wind;
            mb.Wind = 0f;
            mb.Color = Pal.WoodLight;
            mb.Box(p + new Vector3(0f, -0.02f, -0.025f), new Vector3(0.025f, 0.08f, 0.025f));
            mb.Box(q + new Vector3(0f, -0.02f, -0.025f), new Vector3(0.025f, 0.08f, 0.025f));
            mb.Wind = keepW;
            mb.Color = keep;
        }
    }
}
