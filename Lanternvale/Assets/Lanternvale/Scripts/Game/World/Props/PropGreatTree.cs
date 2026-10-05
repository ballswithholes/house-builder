// Old Kusu: the great camphor tree of Lanternvale (~25 m), the village landmark. A massive flared trunk (fits the
// 3.4 × 1.6 collider; its bulk extends backwards), buttress roots, four great limbs, a broad layered canopy held high
// (lowest leaves ≈ 10 m, forward reach ≈ 6.5 m so units in front stay visible), a shimenawa straw rope with zigzag paper
// shide and straw tassels, and tiny glowing spirit motes in the leaves.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterGreatTree(Dictionary<string, Recipe> r)
        {
            r["prop_tree_great"] = (art, seed) => Swaying(art, seed, BuildGreatTree, 1.7f);
        }

        const float KusuZ = 0.45f, KusuRx = 1.55f, KusuRz = 1.25f;

        /// <summary>Unit trunk radius profile (multiplied by KusuRx / KusuRz).</summary>
        static readonly Vector2[] KusuTrunk =
        {
            new Vector2(1f, 0f), new Vector2(0.9f, 0.55f), new Vector2(0.86f, 1.6f), new Vector2(0.85f, 3.5f), new Vector2(0.8f, 5.6f), new Vector2(0.7f, 7.3f),
        };

        static float KusuRadiusAt(float y)
        {
            for (int i = 0; i < KusuTrunk.Length - 1; i++)
                if (y <= KusuTrunk[i + 1].y)
                    return Mathf.Lerp(KusuTrunk[i].x, KusuTrunk[i + 1].x, (y - KusuTrunk[i].y) / (KusuTrunk[i + 1].y - KusuTrunk[i].y));
            return KusuTrunk[KusuTrunk.Length - 1].x;
        }

        static MeshBuilder BuildGreatTree(int b)
        {
            var mb = Builder(VariantSeed("prop_tree_great", b), 0.07f, 0.3f, 1.6f);
            var bark = Paint.Hex("#7E6957");
            var barkDark = Paint.Hex("#66533F");
            mb.Wind = 0f;
            // trunk
            mb.Color = bark;
            mb.Push().Translate(0f, 0f, KusuZ).Scale(new Vector3(KusuRx, 1f, KusuRz));
            mb.Lathe(KusuTrunk, 10, false, false, false, new[] { barkDark, bark, bark, Paint.Shade(bark, 1.06f), bark, bark }, 9f);
            mb.Pop();
            // buttress roots: short in front (inside the collider), sprawling to the sides and back
            (Vector3 to, float r)[] roots =
            {
                (new Vector3(-1.5f, 0f, -0.3f), 0.42f), (new Vector3(1.45f, 0f, -0.32f), 0.4f), (new Vector3(-2.4f, 0f, 0.55f), 0.45f),
                (new Vector3(2.5f, 0f, 0.7f), 0.47f), (new Vector3(-1.7f, 0f, 2.2f), 0.42f), (new Vector3(1.5f, 0f, 2.4f), 0.42f), (new Vector3(0.2f, 0f, 2.6f), 0.4f),
            };
            mb.Color = barkDark;
            foreach (var root in roots)
            {
                var flat = new Vector3(root.to.x, 0f, root.to.z - KusuZ);
                var dir = new Vector3(flat.x / KusuRx, 0f, flat.z / KusuRz).normalized;
                var from = new Vector3(dir.x * KusuRx * 0.62f, 1.25f, KusuZ + dir.z * KusuRz * 0.62f);
                var mid = Vector3.Lerp(from, root.to, 0.5f) + Vector3.up * 0.05f;
                mid.y = Mathf.Lerp(from.y, 0f, 0.62f);
                mb.Segment(from, mid, root.r, root.r * 0.6f, 6, false, true);
                mb.Segment(mid, root.to + Vector3.down * 0.06f, root.r * 0.6f, 0.07f, 6, false, true);
            }
            // moss on the roots' backs and the trunk foot
            float[] mossAngles = { 150f, 205f, 335f, 30f, 90f };
            for (int i = 0; i < mossAngles.Length; i++)
            {
                float a = mossAngles[i] * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a) * KusuRx * 0.9f, 0.35f + (i % 2) * 0.4f, KusuZ + Mathf.Sin(a) * KusuRz * 0.9f);
                FacetBlob(mb, p, new Vector3(0.45f, 0.18f, 0.35f), 0, 0.25f, b * 9 + i, 0.2f, ByNormal(Pal.MossLight, Pal.Moss, Pal.LeafDark));
            }
            // great limbs and their branches (wind starts above ~9 m)
            mb.WindGradient = true; mb.WindY0 = 9f; mb.WindY1 = 25f; mb.Wind = 1.5f;
            var fork = new Vector3(0f, 6.9f, KusuZ);
            (Vector3 end, float r0, float r1)[] limbs =
            {
                (new Vector3(-5.6f, 12.2f, 0.4f), 0.95f, 0.4f), (new Vector3(5.8f, 11.8f, 1.0f), 0.98f, 0.4f),
                (new Vector3(-1.4f, 15.5f, 2.2f), 0.9f, 0.36f), (new Vector3(1.6f, 15.8f, -1.4f), 0.88f, 0.36f),
            };
            mb.Color = bark;
            foreach (var l in limbs)
            {
                var start = fork + (l.end - fork).normalized * 0.3f + Vector3.down * 0.9f;
                mb.Segment(start, l.end, l.r0, l.r1, 8, false, true);
            }
            mb.Color = Paint.Shade(bark, 0.95f);
            (int limb, Vector3 to)[] branches =
            {
                (0, new Vector3(-8.0f, 13.6f, 0.8f)), (0, new Vector3(-5.0f, 14.6f, -2.3f)), (1, new Vector3(8.2f, 13.0f, 1.3f)), (1, new Vector3(4.6f, 14.2f, -2.6f)),
                (2, new Vector3(-3.6f, 18.6f, 1.2f)), (2, new Vector3(0.6f, 19.5f, 4.0f)), (3, new Vector3(3.8f, 19.0f, -0.4f)), (3, new Vector3(-0.6f, 20.5f, -1.0f)),
            };
            foreach (var br in branches) mb.Segment(limbs[br.limb].end, br.to, limbs[br.limb].r1 * 0.95f, 0.12f, 6, false, true);
            // canopy: a broad layered dome of faceted leaf clumps
            Foliage(b, Paint.Hex("#5C8B4C"), out var top, out var side, out var bottom, 5f);
            var col = ByNormal(top, side, bottom, 0.22f);
            int seed = VariantSeed("kusu_canopy", b);
            (Vector3 c, Vector3 r)[] clumps =
            {
                // lower tier (≈ 12 m)
                (new Vector3(-7.6f, 12.8f, 0.6f), new Vector3(3.6f, 2.5f, 3.2f)), (new Vector3(7.7f, 12.5f, 1.2f), new Vector3(3.6f, 2.5f, 3.2f)),
                (new Vector3(-4.2f, 13.6f, -2.4f), new Vector3(3.5f, 2.7f, 2.9f)), (new Vector3(4.4f, 13.4f, -2.6f), new Vector3(3.4f, 2.6f, 2.9f)),
                (new Vector3(0.2f, 13.3f, -3.6f), new Vector3(3.3f, 2.5f, 2.7f)), (new Vector3(-4.4f, 13.4f, 4.0f), new Vector3(3.4f, 2.6f, 3.0f)),
                (new Vector3(4.7f, 13.2f, 4.3f), new Vector3(3.4f, 2.6f, 3.0f)), (new Vector3(0.2f, 13.8f, 5.6f), new Vector3(3.4f, 2.6f, 2.8f)),
                // middle tier (≈ 17 m)
                (new Vector3(-4.6f, 17.6f, 0.6f), new Vector3(3.9f, 3.0f, 3.4f)), (new Vector3(4.9f, 17.2f, 0.9f), new Vector3(3.9f, 3.0f, 3.4f)),
                (new Vector3(0.1f, 17.0f, -2.2f), new Vector3(3.8f, 3.0f, 3.1f)), (new Vector3(0.5f, 18.0f, 3.4f), new Vector3(3.7f, 3.0f, 3.2f)),
                // crown (≈ 21–25 m)
                (new Vector3(-1.6f, 21.0f, 0.5f), new Vector3(3.5f, 2.8f, 3.2f)), (new Vector3(2.3f, 20.6f, 1.0f), new Vector3(3.2f, 2.6f, 3.0f)),
                (new Vector3(0.3f, 23.1f, 0.9f), new Vector3(2.6f, 2.0f, 2.4f)),
            };
            for (int i = 0; i < clumps.Length; i++)
                FacetBlob(mb, clumps[i].c, clumps[i].r, 1, 0.2f, seed + i * 17, 0.28f, col);
            // spirit motes glinting among the leaves
            mb.Emission = 1f;
            for (int i = 0; i < 18; i++)
            {
                var cl = clumps[(i * 5 + b) % clumps.Length];
                float a = mb.Random01() * Mathf.PI * 2f, e = Mathf.Lerp(-0.9f, 0.4f, mb.Random01());
                var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
                mb.Color = i % 3 == 0 ? Paint.Hex("#C8FFF0") : Pal.Glow;
                Gem(mb, cl.c + Vector3.Scale(dir, cl.r) * 1.05f, 0.16f);
            }
            mb.Emission = 0f;
            mb.Wind = 0f;
            mb.WindGradient = false;
            // shimenawa: a thick twisted straw rope around the trunk
            const float ropeY = 2.6f;
            float k = KusuRadiusAt(ropeY);
            float rx = KusuRx * k + 0.1f, rz = KusuRz * k + 0.1f;
            const int segs = 20;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                var p0 = new Vector3(Mathf.Cos(a0) * rx, ropeY + Mathf.Sin(a0 * 2f) * 0.02f, KusuZ + Mathf.Sin(a0) * rz);
                var p1 = new Vector3(Mathf.Cos(a1) * rx, ropeY + Mathf.Sin(a1 * 2f) * 0.02f, KusuZ + Mathf.Sin(a1) * rz);
                mb.Color = i % 2 == 0 ? Pal.RopeStraw : Paint.Shade(Pal.RopeStraw, 0.86f);
                mb.Segment(p0, p1, 0.15f, 0.15f, 6, false, true);
                mb.Color = Paint.Shade(Pal.RopeStraw, 1.08f);
                mb.Segment(p0 + Vector3.up * 0.12f, p1 + Vector3.up * 0.12f, 0.07f, 0.07f, 5, false, false);
            }
            // shide (zigzag paper streamers) and straw tassels hanging from the front of the rope
            float[] shide = { 205f, 235f, 270f, 305f, 335f };
            for (int i = 0; i < shide.Length; i++)
            {
                float a = shide[i] * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(a) * (rx + 0.1f), ropeY - 0.12f, KusuZ + Mathf.Sin(a) * (rz + 0.1f));
                var outward = new Vector3(Mathf.Cos(a) / rx, 0f, Mathf.Sin(a) / rz).normalized;
                var across = Vector3.Cross(Vector3.up, outward).normalized;
                Shide(mb, at, across, outward, 0.6f);
            }
            float[] tassels = { 220f, 252f, 288f, 320f };
            mb.Color = Pal.RopeStraw;
            foreach (float deg in tassels)
            {
                float a = deg * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(a) * (rx + 0.08f), ropeY - 0.1f, KusuZ + Mathf.Sin(a) * (rz + 0.08f));
                mb.Cylinder(at + Vector3.down * 0.42f, 0.09f, 0.05f, 0.42f, 5);
                mb.Color = Paint.Shade(Pal.RopeStraw, 0.9f);
                mb.Cylinder(at + Vector3.down * 0.12f, 0.055f, 0.055f, 0.06f, 5);
                mb.Color = Pal.RopeStraw;
            }
            return mb;
        }

        /// <summary>Zigzag paper streamer (shide) hanging from `at`: four folded paper steps (thin slabs).</summary>
        static void Shide(MeshBuilder mb, Vector3 at, Vector3 side, Vector3 outward, float length)
        {
            var keepC = mb.Color;
            mb.Color = Pal.Paper;
            float step = length / 4f, w = 0.13f;
            var p = at;
            for (int i = 0; i < 4; i++)
            {
                float dir = i % 2 == 0 ? 1f : -1f;
                var a = p + side * (dir * 0.02f);
                var c = a + Vector3.down * step;
                var poly = new[] { a, a + side * (dir * w), c + side * (dir * w), c };
                Slab(mb, poly, outward, 0.012f);
                p = c + side * (dir * w);
            }
            mb.Color = keepC;
        }
    }
}
