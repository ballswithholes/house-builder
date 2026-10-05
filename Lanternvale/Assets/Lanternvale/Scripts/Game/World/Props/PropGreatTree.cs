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
            r["prop_tree_great"] = GreatTree;
        }

        /// <summary>
        /// Old Kusu plus its plaza. The paving is a child renderer deliberately left OUT of PropModel.Renderers: it is
        /// ground dressing, so it must not dither out with the tree when a unit walks behind the trunk, must not widen the
        /// tree's bounds / occluder grid / blob shadow, and takes the shader defaults (no tint, ink 2.2 px).
        /// </summary>
        static PropModel GreatTree(string art, int seed)
        {
            int b = Bucket(seed);
            var rig = new Rig(art);
            rig.Body(Cached(art + "#" + b, () => BuildGreatTree(b)));
            var model = rig.Done(1.7f);
            model.Sways = true;
            var plaza = new GameObject("Plaza");
            plaza.transform.SetParent(rig.Root, false);
            plaza.AddComponent<MeshFilter>().sharedMesh = Cached(art + "#plaza" + b, () => BuildKusuPlaza(b));
            var mr = plaza.AddComponent<MeshRenderer>();
            mr.sharedMaterials = MaterialsFor(Skin.Outlined);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return model;
        }

        // ------------------------------------------------------------------ the plaza: flagstone ring, stepping stones, offerings

        static readonly Color[] KusuStones =
        {
            Paint.Hex("#DCC6A2"), Paint.Hex("#D2B994"), Paint.Hex("#E3D3B6"), Paint.Hex("#CFB9A4"), Paint.Hex("#C9BFAF"), Paint.Hex("#D8BFA4"),
            Paint.Hex("#BDB6AA"),
        };

        /// <summary>
        /// Irregular flat stone (star-shaped polygon in XZ) standing `h` above the ground, sunk a little below it: one flat
        /// colour on top (no per-triangle jitter, which would show the fan) and slightly darker, worn edges.
        /// </summary>
        static void FlagStone(MeshBuilder mb, List<Vector2> poly, float h, Color col)
        {
            int n = poly.Count;
            var c = Vector2.zero;
            foreach (var p in poly) c += p;
            c /= n;
            var top = new Vector3(c.x, h, c.y);
            var edge = Paint.Shade(col, 0.84f);
            for (int i = 0; i < n; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % n];
                // a worn bevel: the flat top is inset a touch and its rim slopes straight down into the turf
                var pi = Vector2.Lerp(p, c, 0.07f);
                var qi = Vector2.Lerp(q, c, 0.07f);
                Tri(mb, top, new Vector3(pi.x, h, pi.y), new Vector3(qi.x, h, qi.y), Vector3.up, col);
                var outward = new Vector3((p + q).x * 0.5f - c.x, 0.3f, (p + q).y * 0.5f - c.y);
                QuadC(mb, new Vector3(pi.x, h, pi.y), new Vector3(qi.x, h, qi.y), new Vector3(q.x, -0.02f, q.y), new Vector3(p.x, -0.02f, p.y), outward, edge);
            }
        }

        /// <summary>A rounded, irregular stepping stone (7-gon with uneven radii, slightly elongated) centred on c.</summary>
        static void RoughStone(MeshBuilder mb, Vector2 c, float r, Color col, int sides = 7)
        {
            var poly = new List<Vector2>();
            float spin = mb.Random01() * 50f, stretch = 1.05f + 0.25f * mb.Random01();
            for (int j = 0; j < sides; j++)
            {
                float a = (spin + j * 360f / sides + (mb.Random01() - 0.5f) * (126f / sides)) * Mathf.Deg2Rad;
                float rr = r * (0.78f + 0.32f * mb.Random01());
                poly.Add(new Vector2(c.x + Mathf.Cos(a) * rr * stretch, c.y + Mathf.Sin(a) * rr * 0.88f));
            }
            FlagStone(mb, poly, 0.03f + 0.012f * mb.Random01(), col);
        }

        static MeshBuilder BuildKusuPlaza(int b)
        {
            var mb = Builder(VariantSeed("kusu_plaza", b), 0.05f, 0f, 1f);
            var centre = new Vector2(0f, KusuZ);
            const float rxIn = 2.65f, rzIn = 1.95f, rxOut = 4.5f, rzOut = 3.75f, gap = 0.07f;
            Color StoneCol(float mossiness)
            {
                var c = KusuStones[(int)(mb.Random01() * KusuStones.Length) % KusuStones.Length];
                c = Paint.Shade(c, 0.95f + 0.1f * mb.Random01());
                return mb.Random01() < mossiness ? Color.Lerp(c, Pal.MossLight, 0.3f + 0.2f * mb.Random01()) : c;
            }
            Vector2 P(float a, float s)
            {
                float rx = Mathf.Lerp(rxIn, rxOut, s), rz = Mathf.Lerp(rzIn, rzOut, s);
                return centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * rz);
            }
            // one hand-cut flagstone over the annulus patch [a0, a1] × [s0, s1]: inset by the joint, corners nudged, one
            // corner clipped
            void Patch(float a0, float a1, float s0, float s1, float moss)
            {
                var corners = new[] { P(a0, s0), P(a1, s0), P(a1, s1), P(a0, s1) };
                var mid = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
                var poly = new List<Vector2>();
                int cut = (int)(mb.Random01() * 4f);
                for (int k = 0; k < 4; k++)
                {
                    var p = corners[k] + (mid - corners[k]).normalized * gap * 1.4f;
                    p += new Vector2(mb.Random01() - 0.5f, mb.Random01() - 0.5f) * 0.08f;
                    if (k == cut)
                    {
                        var prev = corners[(k + 3) % 4];
                        var next = corners[(k + 1) % 4];
                        poly.Add(Vector2.Lerp(p, prev + (mid - prev).normalized * gap * 1.4f, 0.25f));
                        poly.Add(Vector2.Lerp(p, next + (mid - next).normalized * gap * 1.4f, 0.25f));
                    }
                    else poly.Add(p);
                }
                FlagStone(mb, poly, 0.03f + 0.015f * mb.Random01(), StoneCol(moss));
            }
            // the ring: three bands of flagstones of uneven length around the trunk (joints staggered between bands);
            // some outer stones are split across the band, a few are missing so grass shows through
            const int bands = 3;
            for (int band = 0; band < bands; band++)
            {
                float s0 = (float)band / bands, s1 = (float)(band + 1) / bands;
                float rxm = Mathf.Lerp(rxIn, rxOut, (s0 + s1) * 0.5f), rzm = Mathf.Lerp(rzIn, rzOut, (s0 + s1) * 0.5f);
                float circumference = Mathf.PI * (3f * (rxm + rzm) - Mathf.Sqrt((3f * rxm + rzm) * (rxm + 3f * rzm)));
                float step = Mathf.PI * 2f * (0.9f + band * 0.05f) / circumference;
                float start = mb.Random01() * step, a = start, end = start + Mathf.PI * 2f;
                while (a < end - step * 0.3f)
                {
                    float a1 = Mathf.Min(end, a + step * (0.6f + 0.75f * mb.Random01()));
                    if (end - a1 < step * 0.45f) a1 = end;
                    float moss = band == 0 ? 0.4f : 0.16f;
                    if (Mathf.Sin((a + a1) * 0.5f) > 0.3f) moss += 0.2f;   // more moss in the tree's shade, behind the trunk
                    if (mb.Random01() < 0.05f) { a = a1; continue; }
                    if (band == bands - 1 && mb.Random01() < 0.3f)
                    {
                        float sm = Mathf.Lerp(s0, s1, 0.45f + 0.1f * mb.Random01());
                        Patch(a, a1, s0, sm, moss);
                        Patch(a, a1, sm, s1, moss);
                    }
                    else Patch(a, a1, s0, s1, moss);
                    a = a1;
                }
            }
            // loose stones and pebbles scattered just outside the ring soften its edge
            for (int i = 0; i < 11; i++)
            {
                float a = (i * 32.7f + mb.Random01() * 15f) * Mathf.Deg2Rad;
                if (Mathf.Sin(a) < -0.85f) continue;   // keep the front entrance clear
                var c = P(a, 1.12f + 0.12f * mb.Random01());
                RoughStone(mb, c, 0.12f + 0.12f * mb.Random01(), StoneCol(0.25f), 5);
            }
            // stepping stones from the ring down to the village road (world y ≈ 5.6 → local z ≈ −6.6)
            float z = KusuZ - rzOut - 0.45f;
            int row = 0;
            while (z > -6.7f)
            {
                int count = row < 3 ? 2 : 1;
                for (int k = 0; k < count; k++)
                {
                    float x = count == 2 ? (k == 0 ? -0.42f : 0.42f) + (row % 2 == 0 ? 0.08f : -0.08f) : (row % 2 == 0 ? 0.22f : -0.22f);
                    float r = (count == 2 ? 0.33f : 0.4f) * (0.9f + 0.18f * mb.Random01());
                    RoughStone(mb, new Vector2(x, z + (mb.Random01() - 0.5f) * 0.1f), r, StoneCol(0.15f), 9);
                }
                z -= row < 3 ? 0.78f : 0.95f;
                row++;
            }
            // moss cushions creeping over the joints, and grass tufts in them
            var mossRamp = CanopyRamp(Pal.MossLight, Pal.Moss, Pal.LeafDark, 0f, 0.09f);
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + mb.Random01() * 25f) * Mathf.Deg2Rad;
                var p = P(a, mb.Random01());
                SoftLump(mb, new Vector3(p.x, 0.03f, p.y), new Vector3(0.22f + 0.1f * mb.Random01(), 0.085f, 0.17f), 0, i * 41f, mossRamp, 0.15f);
            }
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + mb.Random01() * 20f) * Mathf.Deg2Rad;
                float s = (i % 3 + 1) / 3f;
                var p = centre + new Vector2(Mathf.Cos(a) * Mathf.Lerp(rxIn, rxOut, s), Mathf.Sin(a) * Mathf.Lerp(rzIn, rzOut, s));
                Tuft(mb, new Vector3(p.x, 0f, p.y), 0.14f + 0.08f * mb.Random01(), 3, i % 2 == 0 ? Pal.Leaf : Pal.Sage);
            }
            KusuOfferings(mb, b);
            return mb;
        }

        /// <summary>A low stone offering table at the foot of the trunk: sake bottles, rice balls, mikan, a vase of sakaki.</summary>
        static void KusuOfferings(MeshBuilder mb, int b)
        {
            const float z = -1.05f, top = 0.34f;
            var stone = Paint.Hex("#C9BDA8");
            mb.Color = Paint.Shade(stone, 0.9f);
            mb.BoxOn(new Vector3(-0.32f, 0f, z), new Vector3(0.14f, top - 0.07f, 0.26f));
            mb.BoxOn(new Vector3(0.32f, 0f, z), new Vector3(0.14f, top - 0.07f, 0.26f));
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, top - 0.07f, z), new Vector3(0.95f, 0.07f, 0.34f), Paint.Shade(stone, 1.06f));
            // two white sake bottles with a blue band
            var white = Paint.Hex("#F3EFE6");
            var blue = Paint.Hex("#5F7FB8");
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = white;
                mb.Push().Translate(s * 0.34f, top, z + 0.04f);
                mb.Lathe(new[] { new Vector2(0.045f, 0f), new Vector2(0.065f, 0.08f), new Vector2(0.06f, 0.14f), new Vector2(0.022f, 0.2f), new Vector2(0.026f, 0.24f) }, 7, false, true, true,
                         new[] { white, white, blue, white, white });
                mb.Pop();
            }
            // a dish of three rice balls (white triangles with a nori band)
            mb.Color = Paint.Hex("#E8E0D0");
            mb.Cylinder(new Vector3(-0.1f, top, z - 0.02f), 0.13f, 0.15f, 0.025f, 8);
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(-0.17f + i * 0.07f, top + 0.025f, z - 0.02f + (i == 1 ? 0.05f : -0.02f));
                mb.Color = Paint.Hex("#FBF8F0");
                mb.Push().Translate(at).Rotate(0f, i * 30f - 30f, 0f);
                mb.Lathe(new[] { new Vector2(0.045f, 0f), new Vector2(0.05f, 0.03f), new Vector2(0.03f, 0.07f), new Vector2(0.008f, 0.085f) }, 3, false, true, true);
                mb.Color = Paint.Hex("#2F3A33");
                mb.Box(new Vector3(0f, 0.02f, -0.03f), new Vector3(0.04f, 0.035f, 0.012f));
                mb.Pop();
            }
            // three mikan on a little stand
            mb.Color = Pal.Wood;
            mb.Cylinder(new Vector3(0.14f, top, z), 0.07f, 0.06f, 0.05f, 6);
            var mikan = Paint.Hex("#F29A3A");
            for (int i = 0; i < 3; i++)
            {
                mb.Color = Paint.Shade(mikan, 0.95f + 0.1f * i);
                var at = new Vector3(0.14f + (i - 1) * 0.045f, top + 0.08f + (i == 1 ? 0.05f : 0f), z + (i == 1 ? 0f : 0.02f));
                mb.Sphere(at, new Vector3(0.045f, 0.04f, 0.045f), 6, 4, false);
            }
            mb.Color = Pal.LeafDark;
            mb.Box(new Vector3(0.14f, top + 0.18f, z), new Vector3(0.03f, 0.012f, 0.02f));
            // a celadon vase with sakaki sprigs at the back of the table
            var celadon = Paint.Hex("#9FC4B2");
            mb.Color = celadon;
            mb.Push().Translate(0.02f, top, z + 0.1f);
            mb.Lathe(new[] { new Vector2(0.04f, 0f), new Vector2(0.06f, 0.06f), new Vector2(0.035f, 0.14f), new Vector2(0.045f, 0.17f) }, 7, false, true, false);
            mb.Pop();
            var sprigBase = new Vector3(0.02f, top + 0.16f, z + 0.1f);
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + b * 20f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a) * 0.5f, 1f, Mathf.Sin(a) * 0.35f - 0.2f);
                mb.Color = Paint.Shade(Paint.Hex("#3F6E48"), 0.9f + 0.2f * mb.Random01());
                Leaf(mb, sprigBase, dir, new Vector3(dir.x, 0.3f, -1f).normalized, 0.14f, mb.Color);
            }
            // a folded paper charm leaning on the table leg and a little cairn of river stones beside it
            mb.Color = Pal.Paper;
            Slab(mb, new[] { new Vector3(0.43f, 0.02f, z - 0.15f), new Vector3(0.53f, 0.02f, z - 0.15f), new Vector3(0.5f, 0.22f, z - 0.12f), new Vector3(0.44f, 0.22f, z - 0.12f) }, Vector3.back, 0.01f);
            float cy = 0f;
            for (int i = 0; i < 3; i++)
            {
                float r = 0.1f - i * 0.025f;
                mb.Color = Paint.Shade(Pal.StoneLight, 0.9f + 0.08f * i);
                mb.Sphere(new Vector3(-0.62f, cy + r * 0.5f, z - 0.05f), new Vector3(r, r * 0.5f, r * 0.9f), 6, 3, false);
                cy += r * 0.95f;
            }
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
            // canopy: a broad layered dome of soft leaf clumps (smooth shading, canopy-wide warm-top / cool-underside ramp)
            Foliage(b, Paint.Hex("#5C8B4C"), out var top, out var side, out var bottom, 5f);
            var ramp = CanopyRamp(top, side, bottom, 10.6f, 25.2f);
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
                SoftLump(mb, clumps[i].c, clumps[i].r, 1, i * 37f + b * 11f, ramp, 0.12f);
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
