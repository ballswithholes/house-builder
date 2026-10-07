// Expansion props for the open zones (Docs/Expansion.md §10, builder props-wild): the RegisterWild hook and the helpers
// shared by PropWildHighlands (Amberfield), PropWildFen (Mirefen), PropWildPeaks (Skyreach) and PropWildTown
// (Brightwater). Recommended colliders and light offsets per key: Docs/ArtKeys.md, "3D-only model keys", props-wild block.
//
// Conventions (Docs/ThreeD.md §1, §7): Y-up model space in metres, front towards −Z, stood up by MapView. The expansion
// maps are deep (40–60 m) and props are seen from every side the camera allows, so buildings are CENTRED on their pivot
// with finished sides and backs, and their recommended colliders are centred ellipses that cover the walls (no offset).
// Helpers here are prefixed Wild… so they never collide with another builder's partial file.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static partial void RegisterWild(Dictionary<string, Recipe> r)
        {
            RegisterWildHighlands(r);
            RegisterWildFen(r);
            RegisterWildPeaks(r);
            RegisterWildTown(r);
        }

        // ------------------------------------------------------------------ assembly

        /// <summary>
        /// A ground part: a child renderer left out of PropModel.Renderers (fallen leaves, mud, lily pads at the water line):
        /// it never fades, cuts or tints with the prop and does not widen its bounds, occluder grid or blob shadow.
        /// </summary>
        static MeshFilter WildGroundPart(Rig rig, string name, Mesh mesh, Skin skin = Skin.Plain)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rig.Root, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = MaterialsFor(skin);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mf;
        }

        /// <summary>A static prop with glowing parts baked into one mesh (SetLit swaps lit/dark), plus wind sway.</summary>
        static PropModel WildLitSwaying(string art, int seed, System.Func<int, bool, MeshBuilder> build, float radius, params Vector3[] lights)
        {
            var m = LitBuilding(art, seed, build, radius, lights);
            m.Sways = true;
            return m;
        }

        // ------------------------------------------------------------------ bones and skulls (gnoll camp, dragon bones)

        static readonly Color WildBoneCol = Paint.Hex("#E9DDC2"), WildBoneDark = Paint.Hex("#BFAF92"), WildSocket = Paint.Hex("#3B302C");

        /// <summary>A long bone from a to b: a shaft with knobbly two-lobed ends.</summary>
        static void WildBone(MeshBuilder mb, Vector3 a, Vector3 b, float r, Color bone)
        {
            var keep = mb.Color;
            mb.Color = bone;
            mb.Segment(a, b, r, r * 0.85f, 5);
            var d = (b - a).normalized;
            var side = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            mb.Color = Paint.Shade(bone, 1.05f);
            Gem(mb, a + side * (r * 0.7f), r * 1.4f);
            Gem(mb, a - side * (r * 0.7f), r * 1.4f);
            Gem(mb, b + side * (r * 0.6f), r * 1.25f);
            Gem(mb, b - side * (r * 0.6f), r * 1.25f);
            mb.Color = keep;
        }

        /// <summary>A curved rib: a tapering chain of segments through points (≥ 2).</summary>
        static void WildRib(MeshBuilder mb, IList<Vector3> pts, float r0, float r1, int sides = 5)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float t0 = (float)i / (pts.Count - 1), t1 = (float)(i + 1) / (pts.Count - 1);
                mb.Segment(pts[i], pts[i + 1], Mathf.Lerp(r0, r1, t0), Mathf.Lerp(r0, r1, t1), sides, false, true);
            }
        }

        /// <summary>
        /// Beast skull (unit size ≈ 0.4 m long) resting at `at`, snout towards local −Z before `yaw`/`pitch`: domed cranium,
        /// tapering snout, dark eye sockets and nostrils, a row of teeth; optional curling horns.
        /// </summary>
        static void WildSkull(MeshBuilder mb, Vector3 at, float s, float yaw, float pitch, Color bone, float horn = 0f, Color? hornCol = null)
        {
            var keepC = mb.Color;
            float keepE = mb.Emission;
            mb.Push().Translate(at).Rotate(pitch, yaw, 0f).Scale(s);
            mb.Color = bone;
            mb.Sphere(new Vector3(0f, 0.15f, 0.05f), new Vector3(0.15f, 0.13f, 0.17f), 8, 5, false);
            // snout: a tapered box pointing −Z (box +Y → −Z, box +Z → +Y)
            mb.Push().Translate(0f, 0.09f, -0.06f).Rotate(-90f, 0f, 0f);
            mb.TaperedBox(Vector3.zero, new Vector3(0.17f, 0.21f, 0.13f), 0.35f, 0.25f);
            mb.Pop();
            mb.Color = Paint.Shade(bone, 0.92f);
            mb.Box(new Vector3(0f, 0.035f, -0.12f), new Vector3(0.13f, 0.05f, 0.2f));
            // sockets and nostrils
            mb.Emission = 0f;
            Ngon(mb, new Vector3(-0.065f, 0.17f, -0.125f), 0.042f, 6, new Vector3(-0.35f, 0.15f, -1f), Vector3.up, 0f, WildSocket);
            Ngon(mb, new Vector3(0.065f, 0.17f, -0.125f), 0.042f, 6, new Vector3(0.35f, 0.15f, -1f), Vector3.up, 0f, WildSocket);
            Ngon(mb, new Vector3(0f, 0.13f, -0.272f), 0.026f, 4, new Vector3(0f, 0.4f, -1f), Vector3.up, 45f, WildSocket);
            // teeth
            mb.Color = Paint.Shade(bone, 1.08f);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.045f + i * 0.03f;
                var root = new Vector3(x, 0.02f, -0.215f + Mathf.Abs(x) * 0.4f);
                mb.Segment(root, root + new Vector3(0f, -0.04f, 0f), 0.012f, 0.002f, 3, false, true);
            }
            if (horn > 0f)
            {
                mb.Color = hornCol ?? Paint.Hex("#5B4A3E");
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    var p0 = new Vector3(sd * 0.1f, 0.24f, 0.06f);
                    var p1 = p0 + new Vector3(sd * 0.12f, 0.08f, 0.06f) * horn;
                    var p2 = p1 + new Vector3(sd * 0.08f, 0.1f, 0.1f) * horn;
                    var p3 = p2 + new Vector3(sd * -0.02f, 0.06f, 0.1f) * horn;
                    WildRib(mb, new[] { p0, p1, p2, p3 }, 0.045f, 0.008f, 5);
                }
            }
            mb.Pop();
            mb.Color = keepC;
            mb.Emission = keepE;
        }

        // ------------------------------------------------------------------ cloth: pennant strings (bunting, prayer flags)

        /// <summary>
        /// A sagging cord from a to c with `count` little flags hanging under it, facing ±(cord × up): squares (prayer flags)
        /// or triangles (bunting). Colours cycle through `cols`. Flags carry the builder's current wind settings.
        /// </summary>
        static void WildPennants(MeshBuilder mb, Vector3 a, Vector3 c, float sag, int count, Color[] cols, float size, bool square, Color cord)
        {
            var keepC = mb.Color;
            mb.Color = cord;
            Rope(mb, a, c, sag, 0.012f, 8, 3);
            var along = (c - a).normalized;
            var normal = Vector3.Cross(along, Vector3.up).normalized;
            if (normal.z > 0f) normal = -normal;   // towards the camera side
            for (int i = 0; i < count; i++)
            {
                float t0 = (i + 0.18f) / count, t1 = (i + 0.82f) / count;
                var p = SagPoint(a, c, sag, t0);
                var q = SagPoint(a, c, sag, t1);
                mb.Color = cols[i % cols.Length];
                float drop = size * (square ? 1.1f : 1.25f);
                var sway = normal * (0.04f * ((i % 3) - 1));
                if (square)
                    Slab(mb, new[] { p, q, q + Vector3.down * drop + sway, p + Vector3.down * drop + sway }, normal, 0.008f);
                else
                    Slab(mb, new[] { p, q, (p + q) * 0.5f + Vector3.down * drop + sway }, normal, 0.008f);
            }
            mb.Color = keepC;
        }

        // ------------------------------------------------------------------ small dressing

        /// <summary>A short-grass tuft in golden, sage or frosty colours (closed thin cones: safe with outlines).</summary>
        static void WildTuft(MeshBuilder mb, Vector3 at, float h, int blades, Color a, Color b)
        {
            var keep = mb.Color;
            for (int i = 0; i < blades; i++)
            {
                float ang = (i + mb.Random01() * 0.5f) * Mathf.PI * 2f / blades;
                var tip = at + new Vector3(Mathf.Cos(ang) * h * 0.38f, h * (0.65f + 0.35f * mb.Random01()), Mathf.Sin(ang) * h * 0.3f);
                mb.Color = Color.Lerp(a, b, mb.Random01());
                mb.Segment(at + new Vector3(Mathf.Cos(ang) * 0.03f, 0f, Mathf.Sin(ang) * 0.03f), tip, h * 0.075f, 0.004f, 3, false, false);
            }
            mb.Color = keep;
        }

        /// <summary>Little upward-facing wildflowers scattered around `at` within radius r.</summary>
        static void WildFlowers(MeshBuilder mb, Vector3 at, float r, int count, Color[] petals, float size = 0.05f)
        {
            for (int i = 0; i < count; i++)
            {
                float a = mb.Random01() * Mathf.PI * 2f, d = Mathf.Sqrt(mb.Random01()) * r;
                var p = at + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d * 0.7f);
                float h = 0.08f + 0.12f * mb.Random01();
                mb.Color = Paint.Hex("#6E8F48");
                mb.Segment(p, p + Vector3.up * h, 0.008f, 0.006f, 3, false, false);
                var petal = petals[i % petals.Length];
                Bloom(mb, p + Vector3.up * (h + 0.01f), size * (0.8f + 0.4f * mb.Random01()), new Vector3(0f, 1f, -0.35f), petal,
                      Paint.Hex("#F3C94A"), mb.Random01() * 72f);
            }
        }

        /// <summary>Signed hash in [-1, 1] of a small integer pair (stable, for per-vertex jitter).</summary>
        static float WildHash(int a, int b, int seed)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFF) / 32767.5f - 1f;
            }
        }
    }
}
