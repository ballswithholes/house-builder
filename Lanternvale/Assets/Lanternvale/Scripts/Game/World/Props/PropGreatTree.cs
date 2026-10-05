// Old Kusu: the great camphor tree of Lanternvale (~25 m), the village landmark. A massive flared trunk with fluted,
// slowly twisting bark ridges, burls and ivy (fits the 3.4 × 2.6 collider; its bulk extends backwards), knuckled buttress
// roots, a shrine plaque, a shimenawa straw rope with zigzag paper shide and straw tassels, four great limbs carrying a
// broad layered canopy held high, and two low limbs sweeping out over the sides of the plaza (leaves from ≈ 3.4 m, a
// second sacred rope swagged between them, paper lanterns that glow at night) so the tree reads as a tree from the game
// camera, whose frame never reaches the high canopy at zoom 3–6. Tiny glowing spirit motes in the leaves.
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
            // the paper lanterns on the low limbs: their own part, lit at night (MapView's night glow) by a mesh swap
            string lanternKey = art + "#lanterns" + b;
            var lanterns = rig.Part("Lanterns", LitMesh(lanternKey, false, lit => BuildKusuLanterns(b, lit)), Vector3.zero, Quaternion.identity);
            for (int i = 0; i < KusuLanternHangs.Length; i++)
            {
                KusuLantern(i, out _, out var c);
                rig.Light(c);
            }
            var model = rig.Done(1.7f);
            model.SetLit = MeshSwitch(lanterns, lanternKey, lit => BuildKusuLanterns(b, lit));
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

        /// <summary>Unit trunk radius profile (multiplied by KusuRx / KusuRz): a strong flare at the foot, a slow taper.</summary>
        static readonly Vector2[] KusuTrunk =
        {
            new Vector2(1.1f, 0f), new Vector2(0.97f, 0.35f), new Vector2(0.9f, 0.8f), new Vector2(0.86f, 1.4f), new Vector2(0.83f, 2.2f),
            new Vector2(0.8f, 3.1f), new Vector2(0.77f, 4.1f), new Vector2(0.73f, 5.2f), new Vector2(0.68f, 6.3f), new Vector2(0.62f, 7.3f),
        };

        /// <summary>Bark ridge amplitude (fraction of the radius) of the fluted, slowly twisting trunk.</summary>
        const float KusuRidge = 0.065f;

        /// <summary>Twist of the bark ridges (radians per metre of height).</summary>
        const float KusuTwist = 0.075f;

        static float KusuRadiusAt(float y)
        {
            for (int i = 0; i < KusuTrunk.Length - 1; i++)
                if (y <= KusuTrunk[i + 1].y)
                    return Mathf.Lerp(KusuTrunk[i].x, KusuTrunk[i + 1].x, (y - KusuTrunk[i].y) / (KusuTrunk[i + 1].y - KusuTrunk[i].y));
            return KusuTrunk[KusuTrunk.Length - 1].x;
        }

        /// <summary>Point just outside the trunk's ridges at angle deg (0 = +X, −90 = the front) and height y, `outBy` metres proud.</summary>
        static Vector3 KusuSurface(float deg, float y, float outBy = 0.04f)
        {
            float a = deg * Mathf.Deg2Rad, k = KusuRadiusAt(y) * (1f + KusuRidge);
            return new Vector3(Mathf.Cos(a) * (KusuRx * k + outBy), y, KusuZ + Mathf.Sin(a) * (KusuRz * k + outBy));
        }

        static Vector3 KusuOutward(float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) / KusuRx, 0f, Mathf.Sin(a) / KusuRz).normalized;
        }

        // the two low limbs (model space: from inside the trunk out to the drooping tips). They bring leaves and lanterns
        // down into the game camera's frame at the sides of the plaza (the high canopy never enters it at zoom 3–6), clear
        // of the trunk front, the stone lanterns (≈2.8 m) and the units standing on the plaza.
        static readonly Vector3[] KusuLowLeft =
        {
            new Vector3(-0.75f, 4.15f, 0.45f), new Vector3(-2.55f, 4.95f, -0.2f), new Vector3(-4.55f, 4.95f, -0.95f), new Vector3(-6.7f, 4.05f, -1.75f),
        };
        static readonly Vector3[] KusuLowRight =
        {
            new Vector3(0.75f, 4.55f, 0.45f), new Vector3(2.65f, 5.3f, -0.15f), new Vector3(4.75f, 5.2f, -0.9f), new Vector3(6.9f, 4.25f, -1.7f),
        };
        static readonly float[] KusuLowRadii = { 0.6f, 0.45f, 0.31f, 0.16f };

        /// <summary>Point along a low limb at t (0 = trunk … 3 = tip) and the limb's radius there.</summary>
        static Vector3 KusuLimbAt(Vector3[] limb, float t, out float radius)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, limb.Length - 2);
            float f = Mathf.Clamp01(t - i);
            radius = Mathf.Lerp(KusuLowRadii[i], KusuLowRadii[i + 1], f);
            return Vector3.Lerp(limb[i], limb[i + 1], f);
        }

        /// <summary>Paper lanterns hanging from the low limbs: (left limb?, t along it, cord length).</summary>
        static readonly (bool left, float t, float cord)[] KusuLanternHangs =
        {
            (true, 1.55f, 0.5f), (true, 2.55f, 0.8f), (false, 1.5f, 0.7f), (false, 2.6f, 0.55f),
        };

        const float KusuLanternR = 0.19f, KusuLanternH = 0.44f;

        static void KusuLantern(int i, out Vector3 hang, out Vector3 centre)
        {
            var h = KusuLanternHangs[i];
            var p = KusuLimbAt(h.left ? KusuLowLeft : KusuLowRight, h.t, out float r);
            hang = p + Vector3.down * (r * 0.85f);
            centre = hang + Vector3.down * (h.cord + KusuLanternH * 0.5f + 0.06f);
        }

        /// <summary>
        /// The paper lanterns on their cords (a separate part, so they glow at night without rebuilding the tree): red and
        /// cream chochin with dark caps, swaying a touch with the low limbs.
        /// </summary>
        static MeshBuilder BuildKusuLanterns(int b, bool lit)
        {
            var mb = Builder(VariantSeed("kusu_lanterns", b), 0.03f, 0f, 1f);
            mb.WindGradient = true; mb.WindY0 = 4.6f; mb.WindY1 = 9.5f; mb.Wind = 0.8f;
            Color[] papers = { Paint.Hex("#E06A4C"), Pal.Paper, Pal.Paper, Paint.Hex("#E06A4C") };
            for (int i = 0; i < KusuLanternHangs.Length; i++)
            {
                KusuLantern(i, out var hang, out var c);
                mb.Color = Paint.Hex("#4A3A2E");
                mb.Segment(hang, c + Vector3.up * (KusuLanternH * 0.5f + 0.05f), 0.014f, 0.014f, 3, false, false);
                PaperLantern(mb, c, KusuLanternR, KusuLanternH, papers[(i + b) % papers.Length], lit, Pal.TimberDark);
            }
            return mb;
        }

        static MeshBuilder BuildGreatTree(int b)
        {
            var mb = Builder(VariantSeed("prop_tree_great", b), 0.07f, 0.3f, 1.6f);
            var bark = Paint.Hex("#7E6957");
            var barkDark = Paint.Hex("#5F4C3A");
            var barkLight = Paint.Hex("#94806B");
            mb.Wind = 0f;
            // trunk: fluted bark ridges that twist slowly up the trunk (flat-shaded facets catch the light ridge by ridge),
            // flaring into buttress lobes towards the roots at the foot
            (Vector3 to, float r)[] roots =
            {
                (new Vector3(-1.5f, 0f, -0.3f), 0.42f), (new Vector3(1.45f, 0f, -0.32f), 0.4f), (new Vector3(-2.5f, 0f, 0.55f), 0.47f),
                (new Vector3(2.6f, 0f, 0.7f), 0.48f), (new Vector3(-1.8f, 0f, 2.3f), 0.44f), (new Vector3(1.6f, 0f, 2.5f), 0.44f), (new Vector3(0.2f, 0f, 2.7f), 0.42f),
            };
            var rootAngles = new float[roots.Length];
            for (int i = 0; i < roots.Length; i++)
                rootAngles[i] = Mathf.Atan2((roots[i].to.z - KusuZ) / KusuRz, roots[i].to.x / KusuRx) * Mathf.Rad2Deg;
            const int sides = 16;
            float[] ringY = { -0.08f, 0.3f, 0.75f, 1.3f, 2.0f, 2.8f, 3.7f, 4.6f, 5.5f, 6.4f, 7.3f };
            var rings = new Vector3[ringY.Length][];
            for (int r = 0; r < ringY.Length; r++)
            {
                float y = Mathf.Max(0f, ringY[r]);
                rings[r] = new Vector3[sides];
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides + KusuTwist * y;
                    float k = KusuRadiusAt(y) * (1f + KusuRidge * (j % 2 == 0 ? 1f : -0.7f) * (1f - 0.25f * y / 7.3f));
                    // buttress lobes over the roots (short in front, where the collider is shallow)
                    float lobe = 0f;
                    foreach (float ra in rootAngles)
                    {
                        float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, ra) / 28f;
                        lobe = Mathf.Max(lobe, Mathf.Exp(-d * d));
                    }
                    float front = Mathf.Clamp01(-Mathf.Sin(a));   // 1 straight towards the camera
                    k += lobe * 0.32f * Mathf.Exp(-y / 0.55f) * (1f - 0.75f * front);
                    rings[r][j] = new Vector3(Mathf.Cos(a) * KusuRx * k, ringY[r], KusuZ + Mathf.Sin(a) * KusuRz * k);
                }
            }
            for (int r = 0; r < ringY.Length - 1; r++)
            {
                float t = Mathf.Max(0f, ringY[r]) / 7.3f;
                var ringCol = t < 0.12f ? Color.Lerp(barkDark, bark, t / 0.12f) : Color.Lerp(bark, barkLight, Mathf.Clamp01((t - 0.12f) * 0.7f));
                for (int j = 0; j < sides; j++)
                {
                    int k = (j + 1) % sides;
                    var a0 = rings[r][j]; var a1 = rings[r][k]; var b1 = rings[r + 1][k]; var b0 = rings[r + 1][j];
                    var mid = (a0 + a1 + b0 + b1) * 0.25f;
                    var outward = new Vector3(mid.x, 0f, mid.z - KusuZ);
                    float jk = (j % 2 == 0 ? 1.04f : 0.9f) * (1f + (mb.Random01() * 2f - 1f) * 0.05f);
                    // ochre lichen flecks on a few ridges, catching the light
                    var col = mb.Random01() < 0.06f ? Color.Lerp(ringCol, Paint.Hex("#B9A56A"), 0.45f) : ringCol;
                    QuadC(mb, a0, a1, b1, b0, outward, Paint.Shade(col, jk));
                }
            }
            mb.Color = bark;
            Fan(mb, rings[ringY.Length - 1], new Vector3(0f, 7.3f, KusuZ), Vector3.up);
            // burls and knots on the bark
            (float deg, float y, Vector3 r)[] burls =
            {
                (-148f, 1.05f, new Vector3(0.34f, 0.3f, 0.26f)), (-28f, 3.25f, new Vector3(0.3f, 0.36f, 0.24f)), (-118f, 4.9f, new Vector3(0.26f, 0.3f, 0.22f)),
                (165f, 2.1f, new Vector3(0.36f, 0.32f, 0.28f)), (25f, 1.6f, new Vector3(0.3f, 0.26f, 0.24f)),
            };
            for (int i = 0; i < burls.Length; i++)
                FacetBlob(mb, KusuSurface(burls[i].deg, burls[i].y, -0.12f), burls[i].r, 0, 0.25f, b * 13 + i * 5, 0.1f, ByNormal(barkLight, bark, barkDark));
            // buttress roots: knuckled, snaking out — short in front (inside the collider), sprawling to the sides and back
            mb.Color = barkDark;
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                var flat = new Vector3(root.to.x, 0f, root.to.z - KusuZ);
                var dir = new Vector3(flat.x / KusuRx, 0f, flat.z / KusuRz).normalized;
                var side = Vector3.Cross(Vector3.up, dir).normalized * (i % 2 == 0 ? 0.18f : -0.18f);
                var from = new Vector3(dir.x * KusuRx * 0.62f, 1.45f, KusuZ + dir.z * KusuRz * 0.62f);
                var knee = Vector3.Lerp(from, root.to, 0.38f) + side; knee.y = 0.62f;
                var mid = Vector3.Lerp(from, root.to, 0.72f) - side * 0.6f; mid.y = 0.2f;
                mb.Segment(from, knee, root.r, root.r * 0.78f, 5, false, true);
                mb.Segment(knee, mid, root.r * 0.78f, root.r * 0.48f, 5, false, true);
                mb.Segment(mid, root.to + Vector3.down * 0.06f, root.r * 0.48f, 0.07f, 5, false, true);
            }
            // thin surface roots creeping over the paving at the sides and back
            (Vector3 a, Vector3 c, Vector3 e)[] creepers =
            {
                (new Vector3(-2.4f, 0.02f, 0.65f), new Vector3(-3.1f, 0.06f, 1.2f), new Vector3(-3.9f, -0.02f, 1.35f)),
                (new Vector3(2.5f, 0.02f, 0.8f), new Vector3(3.3f, 0.05f, 0.55f), new Vector3(4.1f, -0.02f, 0.75f)),
                (new Vector3(0.25f, 0.02f, 2.7f), new Vector3(0.75f, 0.05f, 3.3f), new Vector3(0.6f, -0.02f, 4.0f)),
            };
            foreach (var cr in creepers)
            {
                mb.Segment(cr.a, cr.c, 0.17f, 0.12f, 5, false, true);
                mb.Segment(cr.c, cr.e, 0.12f, 0.05f, 5, false, true);
            }
            // moss on the roots' backs and the trunk foot
            float[] mossAngles = { 150f, 205f, 335f, 30f, 90f };
            for (int i = 0; i < mossAngles.Length; i++)
            {
                float a = mossAngles[i] * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a) * KusuRx * 0.9f, 0.35f + (i % 2) * 0.4f, KusuZ + Mathf.Sin(a) * KusuRz * 0.9f);
                FacetBlob(mb, p, new Vector3(0.45f, 0.18f, 0.35f), 0, 0.25f, b * 9 + i, 0.2f, ByNormal(Pal.MossLight, Pal.Moss, Pal.LeafDark));
            }
            // ivy climbing the front flanks (one run continues above the rope)
            Ivy(mb, KusuSurface(-128f, 0.15f), KusuSurface(-118f, 2.3f), 9, 0.5f, KusuOutward(-123f));
            Ivy(mb, KusuSurface(-52f, 0.2f), KusuSurface(-62f, 1.9f), 7, 0.45f, KusuOutward(-57f));
            Ivy(mb, KusuSurface(-150f, 2.95f), KusuSurface(-140f, 4.4f), 7, 0.45f, KusuOutward(-145f));
            // the shrine plaque on the trunk front above the offering table (dark frame, pale board, inked characters, a
            // little gabled roof)
            {
                float z = KusuSurface(-90f, 1.45f, 0.03f).z;
                mb.Color = Pal.TimberDark;
                mb.Box(new Vector3(0f, 1.45f, z - 0.02f), new Vector3(0.38f, 0.54f, 0.05f));
                mb.Color = Paint.Hex("#EADBB6");
                mb.Box(new Vector3(0f, 1.44f, z - 0.05f), new Vector3(0.29f, 0.44f, 0.02f));
                mb.Color = Paint.Hex("#2E2A2A");
                for (int i = 0; i < 3; i++)
                    mb.Box(new Vector3(0f, 1.6f - i * 0.15f, z - 0.065f), new Vector3(0.1f - (i == 1 ? 0.02f : 0f), 0.085f, 0.012f));
                mb.Color = Paint.Shade(Pal.TimberDark, 1.1f);
                for (int s = -1; s <= 1; s += 2)
                    OBox(mb, new Vector3(s * 0.12f, 1.78f, z - 0.04f), new Vector3(0.27f, 0.035f, 0.12f), Quaternion.Euler(0f, 0f, s * -28f));
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
            Foliage(b, Paint.Hex("#5C8B4C"), out var top, out var leafSide, out var bottom, 5f);
            var ramp = CanopyRamp(top, leafSide, bottom, 10.6f, 25.2f);
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
            // spirit motes glinting among the lower tier's leaves (above ≈ 14 m the canopy never enters the game
            // camera's frame: its top ray dips ≥ 39°)
            mb.Emission = 1f;
            for (int i = 0; i < 12; i++)
            {
                var cl = clumps[(i * 5 + b) % 8];
                float a = mb.Random01() * Mathf.PI * 2f, e = Mathf.Lerp(-0.9f, 0.4f, mb.Random01());
                var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
                mb.Color = i % 3 == 0 ? Paint.Hex("#C8FFF0") : Pal.Glow;
                Gem(mb, cl.c + Vector3.Scale(dir, cl.r) * 1.05f, 0.16f);
            }
            mb.Emission = 0f;
            // the two low limbs sweeping out over the sides of the plaza, leaves drooping to ≈ 3.8 m (their own canopy
            // ramp, so their tops are sun-warm too), a gentle sway from their elbows out
            mb.WindGradient = true; mb.WindY0 = 4.6f; mb.WindY1 = 9.5f; mb.Wind = 0.8f;
            var lowRamp = CanopyRamp(top, leafSide, bottom, 3.3f, 8.2f);
            for (int s = 0; s < 2; s++)
            {
                var limb = s == 0 ? KusuLowLeft : KusuLowRight;
                float sx = s == 0 ? -1f : 1f;
                // a swollen collar where the limb leaves the trunk
                FacetBlob(mb, Vector3.Lerp(limb[0], limb[1], 0.42f), new Vector3(0.62f, 0.56f, 0.5f), 0, 0.15f, b * 3 + s, 0f, ByNormal(barkLight, bark, barkDark));
                for (int i = 0; i < limb.Length - 1; i++)
                {
                    mb.Color = i == 0 ? bark : Paint.Shade(bark, 0.96f);
                    mb.Segment(limb[i], limb[i + 1], KusuLowRadii[i], KusuLowRadii[i + 1], 7, false, true);
                }
                // twigs up into the leaves
                mb.Color = Paint.Shade(bark, 0.92f);
                var inner = KusuLimbAt(limb, 1.4f, out _);
                mb.Segment(limb[2], limb[2] + new Vector3(sx * 0.6f, 1.25f, 0.9f), 0.13f, 0.05f, 4, false, false);
                mb.Segment(limb[3], limb[3] + new Vector3(sx * 0.7f, 0.55f, 0.85f), 0.1f, 0.04f, 4, false, false);
                mb.Segment(inner, inner + new Vector3(sx * 0.2f, 1.35f, 0.7f), 0.14f, 0.05f, 4, false, false);
                // leaf clumps: the tip clump hangs lowest, the inner ones climb back towards the trunk
                (Vector3 c, Vector3 r)[] low =
                {
                    (new Vector3(sx * 6.75f, 4.5f + s * 0.15f, -1.95f), new Vector3(1.95f, 1.1f, 1.55f)),
                    (new Vector3(sx * 4.85f, 5.95f + s * 0.25f, -1.2f), new Vector3(1.65f, 1.08f, 1.4f)),
                    (new Vector3(sx * 7.85f, 4.6f + s * 0.15f, -0.5f), new Vector3(1.45f, 0.95f, 1.3f)),
                    (new Vector3(sx * 5.7f, 6.55f + s * 0.2f, 0.45f), new Vector3(1.6f, 1.1f, 1.4f)),
                };
                for (int i = 0; i < low.Length; i++)
                    SoftLump(mb, low[i].c, low[i].r, 1, i * 53f + s * 29f + b * 7f, lowRamp, 0.13f);
            }
            mb.Wind = 0f;
            mb.WindGradient = false;
            // shimenawa: a thick twisted straw rope around the trunk
            const float ropeY = 2.6f;
            float k0 = KusuRadiusAt(ropeY) * (1f + KusuRidge);
            float rx = KusuRx * k0 + 0.12f, rz = KusuRz * k0 + 0.12f;
            // one continuous twisted tube (alternating straw shades per twist) plus a thinner strand along its top
            const int segs = 20;
            void RopeRing(float lift, float radius, int sides, Color c0, Color c1)
            {
                Vector3[] Ring(int i)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    var c = new Vector3(Mathf.Cos(a) * rx, ropeY + lift + Mathf.Sin(a * 2f) * 0.02f, KusuZ + Mathf.Sin(a) * rz);
                    var n = new Vector3(Mathf.Cos(a) / rx, 0f, Mathf.Sin(a) / rz).normalized;
                    var ring = new Vector3[sides];
                    for (int j = 0; j < sides; j++)
                    {
                        float f = (j + 0.5f * (i % 2)) * Mathf.PI * 2f / sides;
                        ring[j] = c + (n * Mathf.Cos(f) + Vector3.up * Mathf.Sin(f)) * radius;
                    }
                    return ring;
                }
                var prev = Ring(0);
                for (int i = 0; i < segs; i++)
                {
                    var next = Ring(i + 1);
                    var col = i % 2 == 0 ? c0 : c1;
                    for (int j = 0; j < sides; j++)
                    {
                        int k = (j + 1) % sides;
                        var mid = (prev[j] + prev[k] + next[j] + next[k]) * 0.25f;
                        float a = (i + 0.5f) * Mathf.PI * 2f / segs;
                        var axis = new Vector3(Mathf.Cos(a) * rx, ropeY + lift, KusuZ + Mathf.Sin(a) * rz);
                        QuadC(mb, prev[j], prev[k], next[k], next[j], mid - axis, col);
                    }
                    prev = next;
                }
            }
            RopeRing(0f, 0.15f, 6, Pal.RopeStraw, Paint.Shade(Pal.RopeStraw, 0.86f));
            RopeRing(0.12f, 0.07f, 4, Paint.Shade(Pal.RopeStraw, 1.08f), Paint.Shade(Pal.RopeStraw, 1.0f));
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
            // a second, thinner sacred rope swagged between the low limbs in front of the trunk, three shide on it
            {
                var a = KusuLimbAt(KusuLowLeft, 1.3f, out _) + Vector3.down * 0.3f;
                var c = KusuLimbAt(KusuLowRight, 1.3f, out _) + Vector3.down * 0.3f;
                const float sag = 0.85f, bow = 0.8f;
                Vector3 At(float t) => SagPoint(a, c, sag, t) + Vector3.back * (bow * 4f * t * (1f - t));
                const int n = 8;
                for (int i = 0; i < n; i++)
                {
                    mb.Color = i % 2 == 0 ? Pal.RopeStraw : Paint.Shade(Pal.RopeStraw, 0.88f);
                    mb.Segment(At((float)i / n), At((float)(i + 1) / n), 0.07f, 0.07f, 5, false, i == 0 || i == n - 1);
                }
                float[] ts = { 0.3f, 0.5f, 0.7f };
                foreach (float t in ts)
                {
                    var p = At(t);
                    var tangent = At(t + 0.02f) - At(t - 0.02f);
                    Shide(mb, p + Vector3.down * 0.06f, new Vector3(tangent.x, 0f, tangent.z).normalized, Vector3.back, 0.48f);
                }
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
