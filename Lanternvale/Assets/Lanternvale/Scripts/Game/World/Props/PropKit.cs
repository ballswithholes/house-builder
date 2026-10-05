// Prop library kit: the TryBuild / TryHas registry, model assembly (Rig), lit/unlit mesh variants, the palette and the
// modelling helpers shared by the recipes (PropBuildings, PropVillage, PropNature, PropGreatTree, PropShrine,
// PropForeground, PropGarden), and the soft-foliage pass (smooth normals + colour ramps applied after ToMesh).
//
// Conventions (Docs/ThreeD.md §1, §7): models are authored in Y-up local space, metres, FRONT FACING −Z (towards the
// camera); the root is stood up with World3D.Upright. The solid base of every prop fits its nav collider ellipse
// (w along X, h along Z, centred on the pivot): buildings may extend backwards (+Z); roofs, eaves, canopies overhang.
// Meshes are cached per (art key, seed bucket = seed mod 4, lit state); SetLit swaps the cached lit/dark mesh.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        // ------------------------------------------------------------------ registry

        delegate PropModel Recipe(string artKey, int seed);

        static Dictionary<string, Recipe> recipes;

        static Recipe Resolve(string artKey)
        {
            if (string.IsNullOrEmpty(artKey)) return null;
            if (recipes == null)
            {
                var r = new Dictionary<string, Recipe>(StringComparer.Ordinal);
                RegisterBuildings(r);
                RegisterVillage(r);
                RegisterNature(r);
                RegisterGreatTree(r);
                RegisterShrine(r);
                RegisterForeground(r);
                RegisterGarden(r);
                recipes = r;
            }
            if (recipes.TryGetValue(artKey, out var recipe)) return recipe;
            // any chest art (prop_chest_*, *_open) uses the chest model
            if (artKey.StartsWith("prop_chest", StringComparison.Ordinal) && recipes.TryGetValue("prop_chest", out recipe)) return recipe;
            return null;
        }

        static partial void TryHas(string artKey, ref bool has)
        {
            if (Resolve(artKey) != null) has = true;
        }

        static partial void TryBuild(string artKey, int seed, ref PropModel model)
        {
            var recipe = Resolve(artKey);
            if (recipe == null) return;
            try { model = recipe(artKey, seed); }
            catch (Exception e)
            {
                Debug.LogError($"[Lanternvale] PropModels: building '{artKey}' failed: {e}");
                model = null;
            }
        }

        // ------------------------------------------------------------------ variants & caching

        /// <summary>Seed bucket (0..3): mesh variants are cached per bucket.</summary>
        static int Bucket(int seed) => ((seed % 4) + 4) % 4;

        /// <summary>Stable random seed for a model variant (MeshBuilder rng, blob shapes).</summary>
        static int VariantSeed(string art, int bucket)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in art) h = h * 31 + c;
                return (h & 0x7fffffff) % 100000 + bucket * 7919 + 1;
            }
        }

        static Mesh Cached(string key, Func<MeshBuilder> build) => MeshCache.Get(key, () =>
        {
            var mb = build();
            var mesh = mb.ToMesh(key);
            SoftenParts(mb, mesh);
            return mesh;
        });

        // ------------------------------------------------------------------ soft (smooth-shaded) foliage

        /// <summary>Underside squash of soft foliage lumps (unit-sphere y below 0 is multiplied by this).</summary>
        const float SoftUnder = 0.8f;

        /// <summary>A range of builder vertices forming one soft lump (PropNature.SoftLump), re-shaded after ToMesh.</summary>
        sealed class SoftPart
        {
            public int start, end;
            public Matrix4x4 toUnit;                // builder root space → the lump's unit sphere
            public Func<float, Color> ramp;         // colour by root-space height
            public float jitter, ao, aoHeight;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshBuilder, List<SoftPart>> softParts =
            new System.Runtime.CompilerServices.ConditionalWeakTable<MeshBuilder, List<SoftPart>>();

        static void RegisterSoft(MeshBuilder mb, SoftPart part) => softParts.GetOrCreateValue(mb).Add(part);

        /// <summary>
        /// MeshBuilder only makes flat or lathe-smooth normals, so soft lumps get theirs here: the analytic ellipsoid
        /// normal at every vertex (coincident corners of the faceted icosphere share it → smooth shading over a lumpy
        /// silhouette) and a per-vertex colour (canopy ramp × the lump's own top-light × faint position noise × AO).
        /// </summary>
        static void SoftenParts(MeshBuilder mb, Mesh mesh)
        {
            if (!softParts.TryGetValue(mb, out var parts) || parts.Count == 0) return;
            softParts.Remove(mb);
            var v = mesh.vertices;
            var n = mesh.normals;
            var col = mesh.colors;
            if (n.Length != v.Length || col.Length != v.Length) return;
            foreach (var part in parts)
            {
                var toUnitT = part.toUnit.transpose;
                for (int i = part.start; i < part.end && i < v.Length; i++)
                {
                    var p = v[i];
                    var u = part.toUnit.MultiplyPoint3x4(p);
                    var g = u;
                    if (g.y < 0f) g.y /= SoftUnder * SoftUnder;
                    var nn = toUnitT.MultiplyVector(g);
                    n[i] = nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up;
                    float t = Mathf.Clamp01((u.y + SoftUnder) / (1f + SoftUnder));
                    float k = Mathf.Lerp(0.84f, 1.04f, Mathf.SmoothStep(0f, 1f, t));
                    k *= 1f + (Hash01(p * 1.7f, 7) * 2f - 1f) * part.jitter * 0.5f;
                    if (part.ao > 0f && part.aoHeight > 0f) k *= Mathf.Lerp(1f - part.ao, 1f, Mathf.Clamp01(p.y / part.aoHeight));
                    var c = part.ramp(p.y);
                    col[i] = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), col[i].a);
                }
            }
            mesh.normals = n;
            mesh.colors = col;
        }

        static MeshBuilder Builder(int seed, float jitter = 0.06f, float ao = 0.3f, float aoHeight = 0.6f) =>
            new MeshBuilder(seed) { Jitter = jitter, AOStrength = ao, AOHeight = aoHeight };

        /// <summary>SetLit for a part whose glowing pieces are baked into its mesh: swaps the cached lit / dark variant.</summary>
        static Action<bool> MeshSwitch(MeshFilter mf, string key, Func<bool, MeshBuilder> build) =>
            lit => { if (mf != null) mf.sharedMesh = Cached(key + (lit ? "|lit" : "|dark"), () => build(lit)); };

        static Mesh LitMesh(string key, bool lit, Func<bool, MeshBuilder> build) =>
            Cached(key + (lit ? "|lit" : "|dark"), () => build(lit));

        /// <summary>SetLit for a separate glowing child (flames): shown when lit.</summary>
        static Action<bool> ActiveSwitch(GameObject go) => lit => { if (go != null) go.SetActive(lit); };

        // ------------------------------------------------------------------ assembly

        enum Skin { Outlined, OutlinedTwoSided, Plain, PlainTwoSided }

        static Material[] MaterialsFor(Skin skin)
        {
            switch (skin)
            {
                case Skin.OutlinedTwoSided: return Materials3D.WithOutline(true);
                case Skin.Plain: return new[] { Materials3D.LowPoly };
                case Skin.PlainTwoSided: return new[] { Materials3D.LowPolyDoubleSided };
                default: return Materials3D.WithOutline();
            }
        }

        /// <summary>Builds a PropModel: an upright root carrying the main mesh, plus child parts (moving / glowing).</summary>
        sealed class Rig
        {
            public readonly PropModel Model;
            public Transform Root => Model.Root.transform;

            public Rig(string name)
            {
                var go = new GameObject(name);
                go.transform.rotation = World3D.Upright;
                Model = new PropModel { Root = go };
            }

            /// <summary>The main mesh, on the root itself.</summary>
            public MeshFilter Body(Mesh mesh, Skin skin = Skin.Outlined) => Attach(Model.Root, mesh, skin);

            public MeshFilter Part(string name, Mesh mesh, Vector3 localPos, Quaternion localRot, Skin skin = Skin.Outlined, Transform parent = null)
            {
                var t = Node(name, localPos, localRot, parent);
                return Attach(t.gameObject, mesh, skin);
            }

            /// <summary>An empty child transform (model space when parent is null).</summary>
            public Transform Node(string name, Vector3 localPos, Quaternion localRot, Transform parent = null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent != null ? parent : Root, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = localRot;
                go.transform.localScale = Vector3.one;
                return go.transform;
            }

            MeshFilter Attach(GameObject go, Mesh mesh, Skin skin)
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = MaterialsFor(skin);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                Model.Renderers.Add(r);
                return mf;
            }

            public void Light(Vector3 modelPoint) => Model.LightAnchors.Add(modelPoint);

            /// <summary>Fills LocalBounds (from the meshes, model space), Height and Radius; extra widens the bounds (moving parts).</summary>
            public PropModel Done(float radius, float height = -1f, Bounds? extra = null)
            {
                var toModel = Root.worldToLocalMatrix;
                bool any = false;
                var b = new Bounds();
                foreach (var r in Model.Renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var m = toModel * r.transform.localToWorldMatrix;
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f));
                        var p = m.MultiplyPoint3x4(c);
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                        else b.Encapsulate(p);
                    }
                }
                if (extra.HasValue)
                {
                    if (!any) { b = extra.Value; any = true; }
                    else b.Encapsulate(extra.Value);
                }
                if (!any) b = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
                Model.LocalBounds = b;
                Model.Height = height > 0f ? height : b.max.y;
                Model.Radius = radius;
                return Model;
            }
        }

        // ------------------------------------------------------------------ palette (sRGB vertex colours)

        static class Pal
        {
            static Color H(string hex) => Paint.Hex(hex);

            public static readonly Color Cream = H("#F4E8D0"), Plaster = H("#EEDDBF"), PlasterRose = H("#E8B9A4"), PlasterWarm = H("#F0D2A8");
            public static readonly Color Timber = H("#6E4B36"), TimberDark = H("#553A2B"), Wood = H("#9A7050"), WoodLight = H("#BE9468"), WoodGrey = H("#94897C");
            public static readonly Color Stone = H("#A99F92"), StoneLight = H("#C6BDAE"), StoneDark = H("#867D74"), StoneCool = H("#A19EA9");
            public static readonly Color Moss = H("#7D9A55"), MossLight = H("#9DB866"), Sage = H("#A8BC8A"), Leaf = H("#6E9A4E"), LeafDark = H("#4E7440"), Forest = H("#3F6146");
            public static readonly Color Thatch = H("#D7AC62"), ThatchDark = H("#AE8443"), ThatchLight = H("#E9C680");
            public static readonly Color Terracotta = H("#C9785A"), TerracottaDark = H("#A95E46"), Teal = H("#5E9C94"), TileBlue = H("#6E8FBF"), DustyBlue = H("#8FA7C4");
            public static readonly Color Honey = H("#E8B04F"), Glow = H("#FFD583"), GlowDeep = H("#FFB65C"), GlassDark = H("#56637A"), GlassNight = H("#3F4A5E");
            public static readonly Color Iron = H("#4F4A55"), Gold = H("#E3B34C"), Brass = H("#C9A04A"), Vermilion = H("#D9553A"), RopeStraw = H("#D9C189"), Paper = H("#F8EFDC");
            public static readonly Color Violet = H("#C9A8FF"), VioletDeep = H("#8F6BD6"), Blight = H("#8E8899"), BlightDark = H("#5F5A69"), Ink = H("#3A2F42");
            public static readonly Color Earth = H("#8C6E52"), Burlap = H("#C9AE82"), Water = H("#86B9C9"), Apple = H("#D2493A"), Pear = H("#C8C25A"), Pumpkin = H("#E08A3A");
            public static readonly Color Fire = H("#FF9A3C"), FireCore = H("#FFE08A"), Ember = H("#FF7A2E"), Charcoal = H("#3B3330"), Teal2 = H("#8FF0D2");

            /// <summary>Flower colours (window boxes, bushes, wildflowers).</summary>
            public static readonly Color[] Flowers =
            {
                H("#F0708A"), H("#FFD45E"), H("#FFFFFF"), H("#B48CE0"), H("#F59A5B"), H("#E9536A"), H("#8FB8F0"), H("#FFB3C8"),
            };

            /// <summary>Per-bucket accents.</summary>
            public static readonly Color[] Shutters = { H("#5E9C94"), H("#6F8FC4"), H("#7E9B5A"), H("#C46B5B") };
        }

        // ------------------------------------------------------------------ small maths

        /// <summary>Front (−Z) edge of a collider ellipse (semi-axes a along X, b along Z) at x; 0 outside.</summary>
        static float EllipseFrontZ(float a, float b, float x)
        {
            float t = x / a;
            return t >= 1f || t <= -1f ? 0f : -b * Mathf.Sqrt(1f - t * t);
        }

        static Color FaceCol(MeshBuilder mb)
        {
            float k = 1f + (mb.Random01() * 2f - 1f) * mb.Jitter;
            var c = mb.Color;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        static Color Pick(Color[] colors, MeshBuilder mb) => colors[Mathf.Min(colors.Length - 1, (int)(mb.Random01() * colors.Length))];

        /// <summary>Flat triangle with an explicit colour, wound to face `outward`.</summary>
        static void Tri(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) mb.Triangle(a, c, b, col);
            else mb.Triangle(a, b, c, col);
        }

        /// <summary>Flat quad a-b-c-d with an explicit colour (does not consume the builder's random sequence).</summary>
        static void QuadC(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col)
        {
            Tri(mb, a, b, c, outward, col);
            Tri(mb, a, c, d, outward, col);
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Box with an arbitrary rotation about its centre.</summary>
        static void OBox(MeshBuilder mb, Vector3 center, Vector3 size, Quaternion rot, Color? top = null)
        {
            mb.Push().Translate(center).Rotate(rot);
            mb.Box(Vector3.zero, size, top);
            mb.Pop();
        }

        /// <summary>Rectangular beam from a to b, w wide and h thick (h along upHint, default Y).</summary>
        static void Beam(MeshBuilder mb, Vector3 a, Vector3 b, float w, float h, Vector3? upHint = null)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var dir = d / len;
            var up = upHint ?? Vector3.up;
            if (Mathf.Abs(Vector3.Dot(dir, up.normalized)) > 0.98f) up = Mathf.Abs(dir.z) < 0.9f ? Vector3.forward : Vector3.right;
            mb.Push().Translate((a + b) * 0.5f).Rotate(Quaternion.LookRotation(dir, up));
            mb.Box(Vector3.zero, new Vector3(w, h, len));
            mb.Pop();
        }

        /// <summary>Octahedron (8 tris): berries, flowers, pebbles, sparkles.</summary>
        static void Gem(MeshBuilder mb, Vector3 c, float r) => mb.Sphere(c, new Vector3(r, r, r), 4, 2, false);

        static void Gem(MeshBuilder mb, Vector3 c, Vector3 r) => mb.Sphere(c, r, 4, 2, false);

        /// <summary>Flat slab: a convex polygon (3D points in one plane) extruded `thickness` behind `normal`. Signs, paper, cloth, boards.</summary>
        static void Slab(MeshBuilder mb, IList<Vector3> poly, Vector3 normal, float thickness)
        {
            int n = poly.Count;
            if (n < 3) return;
            normal = normal.normalized;
            var back = -normal * thickness;
            var c = Vector3.zero;
            for (int i = 0; i < n; i++) c += poly[i];
            c /= n;
            var front = FaceCol(mb);
            var rear = FaceCol(mb);
            for (int i = 0; i < n; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % n];
                Tri(mb, c, p, q, normal, front);
                Tri(mb, c + back, p + back, q + back, -normal, rear);
                mb.Quad(p, q, q + back, p + back, (p + q) * 0.5f - c);
            }
        }

        /// <summary>Regular polygon facing `normal` (single-sided): round windows, discs on walls, emblems.</summary>
        static void Ngon(MeshBuilder mb, Vector3 center, float radius, int sides, Vector3 normal, Vector3 upInPlane, float angleOffsetDeg = 0f, Color? color = null)
        {
            normal = normal.normalized;
            var u = Vector3.ProjectOnPlane(upInPlane, normal).normalized;
            var r = Vector3.Cross(u, normal).normalized;
            var col = color ?? FaceCol(mb);
            float a0 = angleOffsetDeg * Mathf.Deg2Rad;
            for (int i = 0; i < sides; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / sides, b = a0 + (i + 1) * Mathf.PI * 2f / sides;
                var p = center + (r * Mathf.Cos(a) + u * Mathf.Sin(a)) * radius;
                var q = center + (r * Mathf.Cos(b) + u * Mathf.Sin(b)) * radius;
                Tri(mb, center, p, q, normal, col);
            }
        }

        /// <summary>Points of a regular polygon (for Slab).</summary>
        static Vector3[] NgonPoints(Vector3 center, float rx, float ry, int sides, Vector3 right, Vector3 up, float angleOffsetDeg = 0f, float fromDeg = 0f, float toDeg = 360f)
        {
            bool full = Mathf.Abs(toDeg - fromDeg) >= 359.9f;
            int count = full ? sides : sides + 1;
            var pts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float a = (fromDeg + angleOffsetDeg + (toDeg - fromDeg) * i / sides) * Mathf.Deg2Rad;
                pts[i] = center + right * (Mathf.Cos(a) * rx) + up * (Mathf.Sin(a) * ry);
            }
            return pts;
        }

        /// <summary>
        /// Skin between successive cross-section rings (same point count, convex-ish); caps fan the end rings.
        /// Thatch roofs, curved beams, cloth, rope.
        /// </summary>
        static void Loft(MeshBuilder mb, IList<Vector3[]> rings, bool capStart = true, bool capEnd = true, Func<Vector3, Color> colorOf = null)
        {
            int count = rings.Count;
            if (count < 2) return;
            int n = rings[0].Length;
            var cents = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var s = Vector3.zero;
                foreach (var p in rings[i]) s += p;
                cents[i] = s / n;
            }
            for (int i = 0; i < count - 1; i++)
            {
                var A = rings[i];
                var B = rings[i + 1];
                var axis = cents[i + 1] - cents[i];
                for (int j = 0; j < n; j++)
                {
                    int k = (j + 1) % n;
                    var mid = (A[j] + A[k] + B[j] + B[k]) * 0.25f;
                    float t = axis.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(mid - cents[i], axis) / axis.sqrMagnitude) : 0f;
                    var outward = mid - (cents[i] + axis * t);
                    if ((A[j] - A[k]).sqrMagnitude < 1e-10f && (B[j] - B[k]).sqrMagnitude < 1e-10f) continue;
                    if (colorOf == null) { mb.Quad(A[j], A[k], B[k], B[j], outward); continue; }
                    var nrm = Vector3.Cross(A[k] - A[j], B[k] - A[j]);
                    if (nrm.sqrMagnitude < 1e-12f) nrm = Vector3.Cross(B[k] - A[j], B[j] - A[j]);
                    if (Vector3.Dot(nrm, outward) < 0f) nrm = -nrm;
                    var c0 = colorOf(nrm.normalized);
                    float jk = 1f + (mb.Random01() * 2f - 1f) * mb.Jitter;
                    QuadC(mb, A[j], A[k], B[k], B[j], outward, new Color(c0.r * jk, c0.g * jk, c0.b * jk, 1f));
                }
            }
            if (capStart) Fan(mb, rings[0], cents[0], cents[0] - cents[1], colorOf);
            if (capEnd) Fan(mb, rings[count - 1], cents[count - 1], cents[count - 1] - cents[count - 2], colorOf);
        }

        static void Fan(MeshBuilder mb, Vector3[] ring, Vector3 c, Vector3 outward, Func<Vector3, Color> colorOf = null)
        {
            var col = colorOf != null ? colorOf(outward.normalized) : FaceCol(mb);
            for (int j = 0; j < ring.Length; j++)
            {
                var p = ring[j];
                var q = ring[(j + 1) % ring.Length];
                if ((p - q).sqrMagnitude < 1e-10f) continue;
                Tri(mb, c, p, q, outward, col);
            }
        }

        // ------------------------------------------------------------------ faceted blobs coloured by normal

        static readonly Dictionary<int, List<Vector3[]>> icoCache = new Dictionary<int, List<Vector3[]>>();

        static List<Vector3[]> Ico(int detail)
        {
            detail = Mathf.Clamp(detail, 0, 2);
            if (icoCache.TryGetValue(detail, out var faces)) return faces;
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized;
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            faces = new List<Vector3[]>();
            for (int i = 0; i < f.Length; i += 3) faces.Add(new[] { v[f[i]], v[f[i + 1]], v[f[i + 2]] });
            for (int d = 0; d < detail; d++)
            {
                var next = new List<Vector3[]>(faces.Count * 4);
                foreach (var tri in faces)
                {
                    var ab = ((tri[0] + tri[1]) * 0.5f).normalized;
                    var bc = ((tri[1] + tri[2]) * 0.5f).normalized;
                    var ca = ((tri[2] + tri[0]) * 0.5f).normalized;
                    next.Add(new[] { tri[0], ab, ca });
                    next.Add(new[] { tri[1], bc, ab });
                    next.Add(new[] { tri[2], ca, bc });
                    next.Add(new[] { ab, bc, ca });
                }
                faces = next;
            }
            icoCache[detail] = faces;
            return faces;
        }

        static float Hash01(Vector3 d, int seed)
        {
            int x = Mathf.RoundToInt(d.x * 1000f), y = Mathf.RoundToInt(d.y * 1000f), z = Mathf.RoundToInt(d.z * 1000f);
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(seed * 2654435761u);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>
        /// Faceted blob (jittered icosphere: detail 0 = 20, 1 = 80, 2 = 320 faces) whose faces are coloured by their
        /// local-space normal (mossy rock tops, sun-lit canopy tops, darker undersides). flattenBottom squashes the
        /// lower half; sink pushes it below y = center.y - radii.y (ground contact).
        /// </summary>
        static void FacetBlob(MeshBuilder mb, Vector3 center, Vector3 radii, int detail, float jitter, int seed, float flattenBottom, Func<Vector3, Color> colorOf)
        {
            Vector3 Displace(Vector3 dir)
            {
                float k = 1f + (Hash01(dir, seed) * 2f - 1f) * jitter;
                var p = Vector3.Scale(dir * k, radii);
                if (flattenBottom > 0f && p.y < 0f) p.y *= 1f - flattenBottom;
                return center + p;
            }
            foreach (var f in Ico(detail))
            {
                var a = Displace(f[0]);
                var b = Displace(f[1]);
                var c = Displace(f[2]);
                var n = Vector3.Cross(b - a, c - a);
                var outward = (a + b + c) / 3f - center;
                if (Vector3.Dot(n, outward) < 0f) { var t = b; b = c; c = t; n = -n; }
                var col = colorOf(n.normalized);
                float k = 1f + (mb.Random01() * 2f - 1f) * mb.Jitter;
                mb.Triangle(a, b, c, new Color(col.r * k, col.g * k, col.b * k, 1f));
            }
        }

        /// <summary>Colour by normal: top (n.y ≥ from) blends to `top`, undersides to `bottom`.</summary>
        static Func<Vector3, Color> ByNormal(Color top, Color side, Color bottom, float from = 0.3f)
        {
            return n =>
            {
                if (n.y >= from) return Color.Lerp(side, top, Mathf.SmoothStep(0f, 1f, (n.y - from) / (1f - from)));
                if (n.y < -0.15f) return Color.Lerp(side, bottom, Mathf.Clamp01((-n.y - 0.15f) / 0.6f));
                return side;
            };
        }

        /// <summary>Rock with a moss cap: faces whose normal points up past `threshold` are mossy.</summary>
        static Func<Vector3, Color> Mossy(Color rock, Color moss, float threshold = 0.55f, Color? under = null)
        {
            var dark = under ?? Paint.Shade(rock, 0.8f);
            return n =>
            {
                if (n.y > threshold) return Color.Lerp(moss, Paint.Shade(moss, 1.12f), (n.y - threshold) / (1f - threshold));
                if (n.y < -0.2f) return dark;
                return rock;
            };
        }

        // ------------------------------------------------------------------ ropes, cords, lanterns

        static Vector3 SagPoint(Vector3 a, Vector3 b, float sag, float t) => Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));

        /// <summary>Sagging rope / cord from a to b.</summary>
        static void Rope(MeshBuilder mb, Vector3 a, Vector3 b, float sag, float radius, int segments = 6, int sides = 5)
        {
            var prev = a;
            for (int i = 1; i <= segments; i++)
            {
                var p = SagPoint(a, b, sag, (float)i / segments);
                mb.Segment(prev, p, radius, radius, sides, false, true);
                prev = p;
            }
        }

        /// <summary>Ribbed paper lantern centred on c (height h, radius r) with wooden caps and a tassel; glows when lit.</summary>
        static void PaperLantern(MeshBuilder mb, Vector3 c, float r, float h, Color paper, bool lit, Color cap)
        {
            var keepC = mb.Color;
            float keepE = mb.Emission, keepJ = mb.Jitter;
            mb.Jitter = 0.03f;
            mb.Color = lit ? Color.Lerp(paper, Pal.Glow, 0.55f) : paper;
            mb.Emission = lit ? 0.95f : 0f;
            mb.Push().Translate(c);
            mb.Lathe(new[]
            {
                new Vector2(r * 0.58f, -h * 0.5f), new Vector2(r * 0.9f, -h * 0.3f), new Vector2(r, 0f),
                new Vector2(r * 0.9f, h * 0.3f), new Vector2(r * 0.58f, h * 0.5f),
            }, 8, false, true, true);
            mb.Emission = 0f;
            mb.Color = cap;
            mb.Cylinder(new Vector3(0f, h * 0.5f - 0.01f, 0f), r * 0.62f, r * 0.5f, h * 0.12f, 8);
            mb.Cylinder(new Vector3(0f, -h * 0.5f - h * 0.1f, 0f), r * 0.5f, r * 0.62f, h * 0.11f, 8);
            mb.Color = Pal.Vermilion;
            mb.Cylinder(new Vector3(0f, -h * 0.5f - h * 0.32f, 0f), r * 0.08f, r * 0.22f, h * 0.22f, 5);
            mb.Pop();
            mb.Color = keepC;
            mb.Emission = keepE;
            mb.Jitter = keepJ;
        }

        // ------------------------------------------------------------------ building parts (walls face −Z at z = wallZ)

        /// <summary>
        /// Window in a front wall (outer face at z = wallZ, facing −Z): glass (glowing when lit), frame, mullions, sill and
        /// optional open shutters. Use a pushed rotation to put it on another wall.
        /// </summary>
        static void Window(MeshBuilder mb, float x, float y, float w, float h, float wallZ, bool lit, Color frame, Color? shutters = null, bool sill = true, bool mullions = true)
        {
            var keepC = mb.Color;
            float keepE = mb.Emission;
            float z = wallZ;
            // glass (explicit colours: lit and dark variants keep the same random sequence)
            mb.Emission = lit ? 1f : 0f;
            QuadC(mb, new Vector3(x - w * 0.5f, y - h * 0.5f, z - 0.01f), new Vector3(x - w * 0.5f, y + h * 0.5f, z - 0.01f),
                  new Vector3(x + w * 0.5f, y + h * 0.5f, z - 0.01f), new Vector3(x + w * 0.5f, y - h * 0.5f, z - 0.01f), Vector3.back,
                  lit ? Pal.Glow : Pal.GlassDark);
            if (lit)
                QuadC(mb, new Vector3(x - w * 0.3f, y - h * 0.5f, z - 0.015f), new Vector3(x - w * 0.3f, y + h * 0.1f, z - 0.015f),
                      new Vector3(x + w * 0.3f, y + h * 0.1f, z - 0.015f), new Vector3(x + w * 0.3f, y - h * 0.5f, z - 0.015f), Vector3.back, Pal.GlowDeep);
            mb.Emission = 0f;
            mb.Color = frame;
            const float t = 0.08f, d = 0.09f;
            mb.Box(new Vector3(x, y + h * 0.5f + t * 0.5f, z - 0.02f), new Vector3(w + t * 2f, t, d));
            mb.Box(new Vector3(x, y - h * 0.5f - t * 0.5f, z - 0.02f), new Vector3(w + t * 2f, t, d));
            mb.Box(new Vector3(x - w * 0.5f - t * 0.5f, y, z - 0.02f), new Vector3(t, h, d));
            mb.Box(new Vector3(x + w * 0.5f + t * 0.5f, y, z - 0.02f), new Vector3(t, h, d));
            if (mullions)
            {
                mb.Box(new Vector3(x, y, z - 0.025f), new Vector3(0.045f, h, 0.04f));
                mb.Box(new Vector3(x, y + h * 0.08f, z - 0.025f), new Vector3(w, 0.045f, 0.04f));
            }
            if (sill) mb.Box(new Vector3(x, y - h * 0.5f - t - 0.025f, z - 0.06f), new Vector3(w + 0.28f, 0.06f, 0.16f));
            if (shutters.HasValue)
            {
                mb.Color = shutters.Value;
                float sw = w * 0.5f + 0.02f;
                for (int s = -1; s <= 1; s += 2)
                {
                    float sx = x + s * (w * 0.5f + t + sw * 0.5f + 0.01f);
                    mb.Box(new Vector3(sx, y, z - 0.03f), new Vector3(sw, h + 0.04f, 0.05f));
                    mb.Color = Paint.Shade(shutters.Value, 0.78f);
                    mb.Box(new Vector3(sx, y + h * 0.22f, z - 0.06f), new Vector3(sw * 0.86f, 0.035f, 0.02f));
                    mb.Box(new Vector3(sx, y - h * 0.22f, z - 0.06f), new Vector3(sw * 0.86f, 0.035f, 0.02f));
                    mb.Color = shutters.Value;
                }
            }
            mb.Color = keepC;
            mb.Emission = keepE;
        }

        /// <summary>Round window (porthole) in a front wall at z = wallZ.</summary>
        static void RoundWindow(MeshBuilder mb, float x, float y, float radius, float wallZ, bool lit, Color frame)
        {
            var keepC = mb.Color;
            float keepE = mb.Emission;
            mb.Emission = lit ? 1f : 0f;
            Ngon(mb, new Vector3(x, y, wallZ - 0.01f), radius, 12, Vector3.back, Vector3.up, 15f, lit ? Pal.Glow : Pal.GlassDark);
            if (lit) Ngon(mb, new Vector3(x, y - radius * 0.2f, wallZ - 0.014f), radius * 0.55f, 10, Vector3.back, Vector3.up, 0f, Pal.GlowDeep);
            mb.Emission = 0f;
            mb.Color = frame;
            mb.Push().Translate(new Vector3(x, y, wallZ - 0.03f)).Rotate(90f, 0f, 0f);
            mb.Torus(Vector3.zero, radius + 0.04f, 0.065f, 12, 4);
            mb.Pop();
            mb.Box(new Vector3(x, y, wallZ - 0.03f), new Vector3(0.05f, radius * 2f, 0.04f));
            mb.Box(new Vector3(x, y, wallZ - 0.03f), new Vector3(radius * 2f, 0.05f, 0.04f));
            mb.Color = keepC;
            mb.Emission = keepE;
        }

        /// <summary>Plank door in a front wall (outer face z = wallZ), bottom at y0; h includes the arch when arched.</summary>
        static void Door(MeshBuilder mb, float x, float y0, float w, float h, float wallZ, Color wood, Color frame, bool arched, Color? knob = null)
        {
            var keepC = mb.Color;
            float z = wallZ - 0.03f;
            float rect = arched ? h - w * 0.5f : h;
            mb.Color = wood;
            mb.Box(new Vector3(x, y0 + rect * 0.5f, z), new Vector3(w, rect, 0.07f));
            if (arched)
                Slab(mb, NgonPoints(new Vector3(x, y0 + rect, z - 0.035f), w * 0.5f, w * 0.5f, 6, Vector3.right, Vector3.up, 0f, 0f, 180f), Vector3.back, 0.07f);
            // plank lines and iron straps
            mb.Color = Paint.Shade(wood, 0.72f);
            for (int i = 1; i < 3; i++)
                mb.Box(new Vector3(x - w * 0.5f + w * i / 3f, y0 + rect * 0.5f, z - 0.04f), new Vector3(0.025f, rect * 0.96f, 0.02f));
            mb.Color = Pal.Iron;
            mb.Box(new Vector3(x - w * 0.12f, y0 + rect * 0.25f, z - 0.045f), new Vector3(w * 0.7f, 0.06f, 0.02f));
            mb.Box(new Vector3(x - w * 0.12f, y0 + rect * 0.78f, z - 0.045f), new Vector3(w * 0.7f, 0.06f, 0.02f));
            mb.Color = knob ?? Pal.Brass;
            Gem(mb, new Vector3(x + w * 0.32f, y0 + rect * 0.5f, z - 0.07f), 0.045f);
            // frame
            mb.Color = frame;
            mb.Box(new Vector3(x - w * 0.5f - 0.06f, y0 + rect * 0.5f, wallZ - 0.02f), new Vector3(0.12f, rect, 0.1f));
            mb.Box(new Vector3(x + w * 0.5f + 0.06f, y0 + rect * 0.5f, wallZ - 0.02f), new Vector3(0.12f, rect, 0.1f));
            if (arched)
            {
                for (int i = 0; i < 6; i++)
                {
                    float a0 = Mathf.PI * i / 6f, a1 = Mathf.PI * (i + 1) / 6f;
                    float r = w * 0.5f + 0.06f;
                    var p = new Vector3(x + Mathf.Cos(a0) * r, y0 + rect + Mathf.Sin(a0) * r, wallZ - 0.02f);
                    var q = new Vector3(x + Mathf.Cos(a1) * r, y0 + rect + Mathf.Sin(a1) * r, wallZ - 0.02f);
                    Beam(mb, p, q, 0.12f, 0.1f, Vector3.back);
                }
            }
            else mb.Box(new Vector3(x, y0 + rect + 0.06f, wallZ - 0.02f), new Vector3(w + 0.24f, 0.12f, 0.1f));
            mb.Color = keepC;
        }

        /// <summary>
        /// Five-petal bloom (single-sided, facing `normal`): rounded kite petals, slightly cupped, around a raised centre.
        /// ≈ 15 tris; reads as a flower at close zoom and as a soft colour dot from afar.
        /// </summary>
        static void Bloom(MeshBuilder mb, Vector3 c, float r, Vector3 normal, Color petal, Color heart, float spinDeg = 0f)
        {
            normal = normal.normalized;
            var u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(normal, u);
            Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return u * Mathf.Cos(a) + v * Mathf.Sin(a); }
            var lift = normal * (r * 0.18f);
            for (int i = 0; i < 5; i++)
            {
                float a = spinDeg + i * 72f;
                float k = 1f + (mb.Random01() * 2f - 1f) * mb.Jitter;
                var col = new Color(petal.r * k, petal.g * k, petal.b * k, 1f);
                var tip = c + Dir(a) * r + lift;
                var left = c + Dir(a - 27f) * (r * 0.62f) + lift * 0.5f;
                var right = c + Dir(a + 27f) * (r * 0.62f) + lift * 0.5f;
                Tri(mb, c, left, tip, normal, col);
                Tri(mb, c, tip, right, normal, col);
            }
            var top = c + normal * (r * 0.22f);
            for (int i = 0; i < 4; i++)
            {
                float a = spinDeg + 20f + i * 90f;
                Tri(mb, c + Dir(a) * (r * 0.3f), c + Dir(a + 90f) * (r * 0.3f), top, normal, heart);
            }
        }

        /// <summary>Heart-shaped leaf (single-sided, facing `normal`) from its stem point `at` towards `dir`.</summary>
        static void Leaf(MeshBuilder mb, Vector3 at, Vector3 dir, Vector3 normal, float size, Color col)
        {
            dir = dir.normalized;
            var s = Vector3.Cross(normal, dir).normalized;
            float w = size * 0.42f;
            var tip = at + dir * size;
            var r1 = at + dir * (size * 0.22f) + s * w;
            var r2 = at + dir * (size * 0.62f) + s * (w * 0.72f);
            var l1 = at + dir * (size * 0.22f) - s * w;
            var l2 = at + dir * (size * 0.62f) - s * (w * 0.72f);
            var notch = at + dir * (size * 0.06f);
            Tri(mb, notch, r1, r2, normal, col);
            Tri(mb, notch, r2, tip, normal, col);
            Tri(mb, notch, tip, l2, normal, Paint.Shade(col, 0.92f));
            Tri(mb, notch, l2, l1, normal, Paint.Shade(col, 0.92f));
        }

        /// <summary>Window box: a planter with rounded greenery, trailing leaves and five-petal blooms (top at y).</summary>
        static void FlowerBox(MeshBuilder mb, float x, float y, float width, float wallZ, Color box, int flowerSeed)
        {
            var keepC = mb.Color;
            mb.Color = box;
            mb.Box(new Vector3(x, y - 0.11f, wallZ - 0.12f), new Vector3(width, 0.22f, 0.24f));
            mb.Color = Paint.Shade(box, 0.8f);
            mb.Box(new Vector3(x, y - 0.2f, wallZ - 0.245f), new Vector3(width + 0.04f, 0.04f, 0.02f));
            // greenery: a row of soft leafy mounds spilling over the front edge
            var greens = ByNormal(Paint.Hex("#9DC163"), Pal.Leaf, Pal.LeafDark, 0.2f);
            int mounds = Mathf.Max(2, Mathf.RoundToInt(width / 0.36f));
            for (int i = 0; i < mounds; i++)
            {
                float fx = x - width * 0.5f + width * (i + 0.5f) / mounds;
                FacetBlob(mb, new Vector3(fx, y + 0.02f, wallZ - 0.14f), new Vector3(width * 0.5f / mounds + 0.05f, 0.1f, 0.13f), 0, 0.12f, flowerSeed * 7 + i, 0.3f, greens);
            }
            // trailing leaves hanging over the box front
            var front = new Vector3(0f, 0.25f, -1f).normalized;
            for (int i = 0; i < mounds + 1; i++)
            {
                float fx = x - width * 0.5f + width * (i + 0.25f + 0.5f * mb.Random01()) / (mounds + 1);
                var at = new Vector3(fx, y - 0.01f, wallZ - 0.25f);
                var col = mb.Random01() < 0.5f ? Pal.Leaf : Paint.Hex("#86AE57");
                Leaf(mb, at, new Vector3((mb.Random01() - 0.5f) * 0.6f, -1f, -0.15f), front, 0.11f + 0.03f * mb.Random01(), col);
            }
            // blooms on top, facing up and out to the street
            int n = Mathf.Max(3, Mathf.RoundToInt(width / 0.2f));
            var face = new Vector3(0f, 0.75f, -0.66f).normalized;
            for (int i = 0; i < n; i++)
            {
                float fx = x - width * 0.5f + width * (i + 0.3f + 0.4f * mb.Random01()) / n;
                var petal = Pal.Flowers[(flowerSeed + i * (i % 2 == 0 ? 1 : 3)) % Pal.Flowers.Length];
                var heart = petal.r > 0.95f && petal.g > 0.8f ? Paint.Hex("#E08A2E") : Paint.Hex("#F6CF4A");
                Bloom(mb, new Vector3(fx, y + 0.11f + mb.Random01() * 0.05f, wallZ - 0.16f - mb.Random01() * 0.07f), 0.075f + 0.02f * mb.Random01(), face, petal, heart, mb.Random01() * 72f);
            }
            mb.Color = keepC;
        }

        /// <summary>
        /// Ivy climbing a wall from `from` to `to` (points on the wall face): a thin wandering stem with a few side shoots
        /// and clusters of two or three heart-shaped leaves angled off the wall. `leaves` ≈ the number of leaves.
        /// </summary>
        static void Ivy(MeshBuilder mb, Vector3 from, Vector3 to, int leaves, float spread, Vector3 wallNormal)
        {
            var keepC = mb.Color;
            var n = wallNormal.normalized;
            var axis = (to - from).normalized;
            var side = Vector3.Cross(n, axis).normalized;
            int nodes = Mathf.Max(3, Mathf.RoundToInt(leaves / 1.9f));
            float phase = mb.Random01() * 6.28f;
            var pts = new Vector3[nodes + 1];
            for (int i = 0; i <= nodes; i++)
            {
                float t = (float)i / nodes;
                float wob = Mathf.Sin(phase + t * 7.2f) * spread * 0.4f * (1.1f - t * 0.5f);
                pts[i] = Vector3.Lerp(from, to, t) + side * wob + n * 0.03f;
            }
            var stem = Paint.Hex("#5F6A3A");
            mb.Color = stem;
            for (int i = 0; i < nodes; i++)
                mb.Segment(pts[i], pts[i + 1], Mathf.Lerp(0.024f, 0.012f, (float)i / nodes), Mathf.Lerp(0.024f, 0.012f, (float)(i + 1) / nodes), 3, false, false);
            Color[] greens = { Pal.Leaf, Pal.LeafDark, Paint.Hex("#86AE57"), Pal.Moss };
            for (int i = 0; i < nodes; i++)
            {
                float t = (i + 0.5f) / nodes;
                var at = Vector3.Lerp(pts[i], pts[i + 1], 0.5f);
                float size = Mathf.Lerp(0.2f, 0.13f, t);
                float sign = i % 2 == 0 ? 1f : -1f;
                // a side shoot every other node, carrying its own leaf
                if (i % 2 == 1 && i < nodes - 1)
                {
                    var shootEnd = at + side * (sign * spread * 0.5f) + axis * (spread * 0.25f) + n * 0.01f;
                    mb.Color = stem;
                    mb.Segment(at, shootEnd, 0.012f, 0.008f, 3, false, false);
                    var c0 = greens[(i + 1) % greens.Length];
                    mb.Color = c0;
                    Leaf(mb, shootEnd, side * sign + axis * 0.4f + n * 0.5f, (n + axis * 0.2f).normalized, size * 0.85f, FaceCol(mb));
                }
                int count = 2 + (mb.Random01() < 0.6f ? 1 : 0);
                for (int k = 0; k < count; k++)
                {
                    float a = (k - (count - 1) * 0.5f) * 62f + (mb.Random01() - 0.5f) * 25f + sign * 12f;
                    var dir = side * Mathf.Sin(a * Mathf.Deg2Rad) + axis * (Mathf.Cos(a * Mathf.Deg2Rad) * 0.55f - 0.25f) + n * 0.45f;
                    mb.Color = greens[(int)(mb.Random01() * greens.Length) % greens.Length];
                    var face = (n + axis * 0.25f - dir.normalized * 0.2f).normalized;
                    Leaf(mb, at, dir, face, size * (0.85f + 0.3f * mb.Random01()), FaceCol(mb));
                }
            }
            mb.Color = keepC;
        }

        /// <summary>Stone chimney rising from `bottom` (inside a roof), slightly crooked, with a cap and a pot.</summary>
        static void Chimney(MeshBuilder mb, Vector3 bottom, float w, float d, float height, Color stone, float leanDeg)
        {
            var keepC = mb.Color;
            mb.Push().Translate(bottom).Rotate(0f, 0f, leanDeg);
            mb.Color = stone;
            mb.TaperedBox(Vector3.zero, new Vector3(w, height, d), 0.08f);
            mb.Color = Paint.Shade(stone, 0.85f);
            mb.Box(new Vector3(0f, height * 0.45f, 0f), new Vector3(w * 1.02f, 0.08f, d * 1.02f));
            mb.Color = Paint.Shade(stone, 1.08f);
            mb.Box(new Vector3(0f, height + 0.06f, 0f), new Vector3(w * 1.12f, 0.14f, d * 1.12f));
            mb.Color = Pal.TerracottaDark;
            mb.Cylinder(new Vector3(0f, height + 0.13f, 0f), w * 0.22f, w * 0.18f, 0.26f, 6);
            mb.Pop();
            mb.Color = keepC;
        }

        /// <summary>
        /// Gable roof of overlapping tile courses (ridge along X) over a wall top centred at `bottom` (width × depth):
        /// a base slab plus `courses` tilted rows per slope, a ridge cap; scallops adds round tile tabs on the front slope.
        /// </summary>
        static void CourseRoof(MeshBuilder mb, Vector3 bottom, float width, float depth, float height, float overhang, int courses,
                               Color tile, Color tileAlt, Color ridge, bool scallops = false, bool backCourses = true)
        {
            var keepC = mb.Color;
            float hz = depth * 0.5f + overhang;
            float drop = height * overhang / Mathf.Max(0.01f, depth * 0.5f);
            float wx = width + overhang * 2f;
            mb.Color = Paint.Shade(tile, 0.8f);
            mb.Roof(bottom, width, depth, height, overhang, 0.1f, false);
            for (int side = -1; side <= 1; side += 2)
            {
                if (side > 0 && !backCourses) continue;
                var eave = bottom + new Vector3(0f, -drop, side * hz);
                var top = bottom + new Vector3(0f, height, 0f);
                var s = top - eave;
                float len = s.magnitude;
                var dir = s / len;
                var n = new Vector3(0f, hz, side * (height + drop)).normalized;
                if (n.y < 0f) n = -n;
                float courseLen = len / courses;
                for (int k = 0; k < courses; k++)
                {
                    mb.Color = (k % 2 == 0) ? tile : tileAlt;
                    var c = eave + dir * (courseLen * (k + 0.55f)) + n * 0.06f;
                    var rot = Quaternion.LookRotation(dir, n) * Quaternion.Euler(6f, 0f, 0f);
                    OBox(mb, c, new Vector3(wx + 0.04f, 0.06f, courseLen * 1.12f), rot);
                    if (scallops && side < 0)
                    {
                        // round tile tabs along the course's lower edge
                        var edge = eave + dir * (courseLen * k) + n * 0.1f;
                        int tabs = Mathf.Max(4, Mathf.RoundToInt(wx / 0.34f));
                        float tw = wx / tabs;
                        var col = Paint.Shade(mb.Color, 1.06f);
                        for (int i = 0; i < tabs; i++)
                        {
                            float tx = -wx * 0.5f + tw * (i + 0.5f);
                            var center = new Vector3(bottom.x + tx, edge.y, edge.z) + dir * (tw * 0.42f);
                            var pts = NgonPoints(center, tw * 0.5f, tw * 0.5f, 4, Vector3.right, -dir, 0f, 0f, 180f);
                            for (int j = 0; j < pts.Length - 1; j++) Tri(mb, center, pts[j], pts[j + 1], n, col);
                        }
                    }
                }
            }
            mb.Color = ridge;
            mb.Box(new Vector3(bottom.x, bottom.y + height + 0.05f, bottom.z), new Vector3(wx + 0.1f, 0.16f, 0.26f));
            mb.Color = keepC;
        }

        /// <summary>Triangular gable wall under a roof (in the plane x = const), facing ±X.</summary>
        static void GableWall(MeshBuilder mb, float x, float y0, float z0, float z1, float height, float side)
        {
            var col = FaceCol(mb);
            Tri(mb, new Vector3(x, y0, z0), new Vector3(x, y0 + height, (z0 + z1) * 0.5f), new Vector3(x, y0, z1), new Vector3(side, 0f, 0f), col);
        }

        /// <summary>Plank wall/skirt facing −Z: a box with darker plank seams.</summary>
        static void PlankWall(MeshBuilder mb, Vector3 bottomCenter, float width, float height, float thick, Color wood, float plank = 0.22f, bool vertical = true)
        {
            var keepC = mb.Color;
            mb.Color = wood;
            mb.BoxOn(bottomCenter, new Vector3(width, height, thick));
            mb.Color = Paint.Shade(wood, 0.7f);
            float z = bottomCenter.z - thick * 0.5f - 0.008f;
            if (vertical)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(width / plank));
                for (int i = 1; i < n; i++)
                    mb.Box(new Vector3(bottomCenter.x - width * 0.5f + width * i / n, bottomCenter.y + height * 0.5f, z), new Vector3(0.022f, height * 0.98f, 0.016f));
            }
            else
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(height / plank));
                for (int i = 1; i < n; i++)
                    mb.Box(new Vector3(bottomCenter.x, bottomCenter.y + height * i / n, z), new Vector3(width * 0.99f, 0.022f, 0.016f));
            }
            mb.Color = keepC;
        }

        /// <summary>Gabled dormer on a front roof slope (slopeZ(y) = the slope's z at height y): walls, little roof, window.</summary>
        static void Dormer(MeshBuilder mb, float x, float width, float y0, float wallH, Func<float, float> slopeZ, Color wall, Color roof, Color frame, bool lit, bool round)
        {
            var keepC = mb.Color;
            float zf = slopeZ(y0 + 0.15f) - 0.06f, zb = slopeZ(y0 + wallH + 0.6f) + 0.3f;
            float mid = (zf + zb) * 0.5f, rh = width * 0.45f;
            mb.Color = wall;
            mb.BoxOn(new Vector3(x, y0, mid), new Vector3(width, wallH, zb - zf));
            Tri(mb, new Vector3(x - width * 0.5f, y0 + wallH, zf), new Vector3(x, y0 + wallH + rh - 0.06f, zf), new Vector3(x + width * 0.5f, y0 + wallH, zf), Vector3.back, wall);
            mb.Push().Translate(x, y0 + wallH, mid).Rotate(0f, 90f, 0f);
            mb.Color = roof;
            mb.Roof(Vector3.zero, zb - zf, width, rh, 0.12f, 0.08f, false);
            mb.Pop();
            if (round) RoundWindow(mb, x, y0 + wallH * 0.5f, width * 0.27f, zf, lit, frame);
            else Window(mb, x, y0 + wallH * 0.5f, width * 0.5f, wallH * 0.58f, zf, lit, frame, null, false, false);
            mb.Color = keepC;
        }

        /// <summary>Oak barrel with iron hoops standing at `at` (radius at the belly r, height h).</summary>
        static void BarrelGeom(MeshBuilder mb, Vector3 at, float r, float h, Color wood, bool cheap = false)
        {
            var keepC = mb.Color;
            var iron = Pal.Iron;
            float rb = r * 0.86f;
            var prof = new List<Vector2>();
            var cols = new List<Color>();
            void P(float rr, float y, Color c) { prof.Add(new Vector2(rr, y)); cols.Add(c); }
            float R(float y) { float t = y / h; return Mathf.Lerp(rb, r, Mathf.Sin(t * Mathf.PI)) + 0.01f; }
            float[] hoops = cheap ? new[] { 0.18f, 0.82f } : new[] { 0.1f, 0.3f, 0.7f, 0.9f };
            P(rb, 0f, wood);
            foreach (float hf in hoops)
            {
                // duplicated points (zero-length bands are skipped) give crisp iron hoops
                float y0 = h * hf - 0.035f, y1 = h * hf + 0.035f;
                P(R(y0), y0, wood); P(R(y0), y0, iron); P(R(y1), y1, iron); P(R(y1), y1, wood);
            }
            P(rb, h, wood);
            P(rb * 0.9f, h, Paint.Shade(wood, 1.12f));
            P(rb * 0.9f, h - 0.04f, Paint.Shade(wood, 1.12f));
            mb.Push().Translate(at);
            mb.Lathe(prof, cheap ? 8 : 10, false, true, true, cols, 18f);
            mb.Pop();
            mb.Color = keepC;
        }

        /// <summary>Wooden crate (size s) with a frame and a diagonal brace on the front, standing at `at`.</summary>
        static void CrateGeom(MeshBuilder mb, Vector3 at, float s, Color wood, float yawDeg = 0f, bool cheap = false)
        {
            var keepC = mb.Color;
            mb.Push().Translate(at).Rotate(0f, yawDeg, 0f);
            mb.Color = Paint.Shade(wood, 0.9f);
            mb.BoxOn(Vector3.zero, new Vector3(s * 0.94f, s * 0.94f, s * 0.94f));
            mb.Color = wood;
            float t = s * 0.12f, h = s * 0.5f;
            if (cheap)
            {
                mb.Box(new Vector3(0f, s - t * 0.5f, 0f), new Vector3(s, t, s));
                Beam(mb, new Vector3(-h + t, t, -h), new Vector3(h - t, s - t, -h), t * 0.9f, t * 0.6f, Vector3.back);
                mb.Pop();
                mb.Color = keepC;
                return;
            }
            // front frame + brace, back frame, side top/bottom rails
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * (h - t * 0.3f);
                mb.Box(new Vector3(0f, t * 0.5f, z), new Vector3(s, t, t * 0.6f));
                mb.Box(new Vector3(0f, s - t * 0.5f, z), new Vector3(s, t, t * 0.6f));
                mb.Box(new Vector3(-h + t * 0.5f, h, z), new Vector3(t, s - 2f * t, t * 0.6f));
                mb.Box(new Vector3(h - t * 0.5f, h, z), new Vector3(t, s - 2f * t, t * 0.6f));
                Beam(mb, new Vector3(-h + t, t, z), new Vector3(h - t, s - t, z), t * 0.9f, t * 0.6f, new Vector3(0f, 0f, side));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (h - t * 0.3f);
                mb.Box(new Vector3(x, t * 0.5f, 0f), new Vector3(t * 0.6f, t, s - 0.01f));
                mb.Box(new Vector3(x, s - t * 0.5f, 0f), new Vector3(t * 0.6f, t, s - 0.01f));
            }
            mb.Pop();
            mb.Color = keepC;
        }

        /// <summary>Lumpy burlap sack sitting at `at`.</summary>
        static void Sack(MeshBuilder mb, Vector3 at, float r, int seed)
        {
            var keepC = mb.Color;
            FacetBlob(mb, at + new Vector3(0f, r * 0.75f, 0f), new Vector3(r, r * 0.85f, r * 0.85f), 1, 0.1f, seed, 0.55f,
                      ByNormal(Paint.Shade(Pal.Burlap, 1.08f), Pal.Burlap, Paint.Shade(Pal.Burlap, 0.75f)));
            mb.Color = Paint.Shade(Pal.Burlap, 0.85f);
            mb.Cylinder(at + new Vector3(0f, r * 1.4f, 0f), r * 0.32f, r * 0.42f, r * 0.45f, 6);
            mb.Color = Pal.RopeStraw;
            mb.Cylinder(at + new Vector3(0f, r * 1.42f, 0f), r * 0.36f, r * 0.36f, r * 0.1f, 6);
            mb.Color = keepC;
        }

        /// <summary>Little grass tuft made of closed thin cones (safe with outlines).</summary>
        static void Tuft(MeshBuilder mb, Vector3 at, float height, int blades, Color col)
        {
            var keepC = mb.Color;
            for (int i = 0; i < blades; i++)
            {
                float a = (i + mb.Random01() * 0.5f) * Mathf.PI * 2f / blades;
                var tip = at + new Vector3(Mathf.Cos(a) * height * 0.35f, height * (0.7f + 0.3f * mb.Random01()), Mathf.Sin(a) * height * 0.35f);
                mb.Color = Paint.Shade(col, 0.9f + 0.2f * mb.Random01());
                mb.Segment(at + new Vector3(Mathf.Cos(a) * 0.03f, 0f, Mathf.Sin(a) * 0.03f), tip, height * 0.08f, 0.004f, 3, false, false);
            }
            mb.Color = keepC;
        }
    }
}
