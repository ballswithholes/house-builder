// Procedural low-poly modelling: every 3D model in the game (characters, creatures, props, terrain) is built with this.
//
// Author in the usual Unity local space — Y up, +Z forward, +X right, metres — and stand the result up in the world with
// World3D.Upright / World3D.Yaw (see World3D.cs). Primitives are placed through a transform stack (Push / Translate /
// Rotate / Scale / Pop). Each vertex carries:
//   colour rgb  = albedo (Color), with optional per-face jitter (hand-painted variation) and ground darkening (AO)
//   colour a    = emission (0 lit … 1 glowing; boosted at night by SceneLighting.NightGlow)
//   uv0         = texture coordinates (planar/cylindrical where it matters; most models use no texture)
//   uv1.x       = wind sway weight (Wind, or a gradient with WindGradient)
//   tangent.xyz = smoothed normal (for the constant-width ink outline of Materials3D.Outline)
//   bone        = Bone (rigid skinning: ToMesh(name, bindposes) returns a skinned mesh)
// Faces are flat-shaded unless a primitive is asked to be smooth. Winding follows Unity (clockwise = front); the
// "outward" hints make every primitive face the right way, also under mirroring transforms (Scale(-1, 1, 1) is fine:
// build one side and mirror it for the other).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public sealed class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>(512);
        readonly List<Vector3> normals = new List<Vector3>(512);
        readonly List<Color> colors = new List<Color>(512);
        readonly List<Vector2> uv0 = new List<Vector2>(512);
        readonly List<Vector2> uv1 = new List<Vector2>(512);
        readonly List<int> bones = new List<int>(512);
        readonly List<int> tris = new List<int>(1024);
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        Matrix4x4 matrix = Matrix4x4.identity;
        Matrix4x4 normalMatrix = Matrix4x4.identity;
        bool mirrored;   // the transform flips handedness: winding is reversed so faces still point outwards
        uint rng;

        // ---------------------------------------------------------------- state for the next primitives

        /// <summary>Albedo of the next primitives.</summary>
        public Color Color = Color.white;
        /// <summary>0 lit … 1 glowing (windows, lantern glass, crystals, eyes, magic).</summary>
        public float Emission;
        /// <summary>Wind weight of the next vertices (or the maximum with WindGradient).</summary>
        public float Wind;
        /// <summary>When true, the wind weight ramps from 0 at WindY0 to Wind at WindY1 (builder-root height).</summary>
        public bool WindGradient;
        public float WindY0, WindY1 = 1f;
        /// <summary>Bone index of the next vertices (skinned models).</summary>
        public int Bone;
        /// <summary>Random brightness variation per face (0.0–0.15 gives a hand-painted feel).</summary>
        public float Jitter;
        /// <summary>Darkens vertices near the ground (builder-root y below AOHeight) by up to AOStrength.</summary>
        public float AOStrength, AOHeight = 0.5f;

        public MeshBuilder(int seed = 1) { rng = (uint)(seed * 2654435761u + 12345u) | 1u; }

        public int VertexCount => verts.Count;
        public int TriangleCount => tris.Count / 3;
        public bool IsEmpty => tris.Count == 0;

        // ---------------------------------------------------------------- transform stack

        public Matrix4x4 Matrix
        {
            get => matrix;
            set { matrix = value; normalMatrix = value.inverse.transpose; mirrored = value.determinant < 0f; }
        }

        public MeshBuilder Push() { stack.Push(matrix); return this; }

        public MeshBuilder Pop()
        {
            Matrix = stack.Count > 0 ? stack.Pop() : Matrix4x4.identity;
            return this;
        }

        public MeshBuilder Translate(Vector3 t) { Matrix = matrix * Matrix4x4.Translate(t); return this; }
        public MeshBuilder Translate(float x, float y, float z) => Translate(new Vector3(x, y, z));
        public MeshBuilder Rotate(Quaternion q) { Matrix = matrix * Matrix4x4.Rotate(q); return this; }
        public MeshBuilder Rotate(float x, float y, float z) => Rotate(Quaternion.Euler(x, y, z));
        public MeshBuilder Scale(Vector3 s) { Matrix = matrix * Matrix4x4.Scale(s); return this; }
        public MeshBuilder Scale(float s) => Scale(new Vector3(s, s, s));

        /// <summary>Deterministic random in [0,1) (per builder seed).</summary>
        public float Random01()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }

        public float Range(float a, float b) => a + (b - a) * Random01();

        // ---------------------------------------------------------------- low level

        Color FaceColor()
        {
            if (Jitter <= 0f) return Color;
            float k = 1f + (Random01() * 2f - 1f) * Jitter;
            return new Color(Color.r * k, Color.g * k, Color.b * k, Color.a);
        }

        int AddVertex(Vector3 local, Vector3 localNormal, Color c, Vector2 uv)
        {
            var p = matrix.MultiplyPoint3x4(local);
            var n = normalMatrix.MultiplyVector(localNormal);
            n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            if (AOStrength > 0f && AOHeight > 0f)
            {
                float k = Mathf.Lerp(1f - AOStrength, 1f, Mathf.Clamp01(p.y / AOHeight));
                c = new Color(c.r * k, c.g * k, c.b * k, c.a);
            }
            float w = Wind;
            if (WindGradient) w = Wind * Mathf.Clamp01((p.y - WindY0) / Mathf.Max(0.001f, WindY1 - WindY0));
            verts.Add(p);
            normals.Add(n);
            colors.Add(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(Emission)));
            uv0.Add(uv);
            uv1.Add(new Vector2(w, 0f));
            bones.Add(Bone);
            return verts.Count - 1;
        }

        void AddTri(int i0, int i1, int i2)
        {
            tris.Add(i0);
            if (mirrored) { tris.Add(i2); tris.Add(i1); }
            else { tris.Add(i1); tris.Add(i2); }
        }

        /// <summary>Flat triangle, clockwise seen from its front (Unity convention).</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c) => Triangle(a, b, c, FaceColor());

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            var n = Vector3.Cross(b - a, c - a);
            int i0 = AddVertex(a, n, col, new Vector2(0f, 0f));
            int i1 = AddVertex(b, n, col, new Vector2(0f, 1f));
            int i2 = AddVertex(c, n, col, new Vector2(1f, 1f));
            AddTri(i0, i1, i2);
        }

        /// <summary>Flat triangle facing `outward` (the winding is fixed to match).</summary>
        public void TriangleFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) Triangle(a, c, b);
            else Triangle(a, b, c);
        }

        /// <summary>Flat quad a-b-c-d (a convex loop), facing `outward`.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var t = b; b = d; d = t; }
            var col = FaceColor();
            var n = Vector3.Cross(b - a, c - a);
            int i0 = AddVertex(a, n, col, new Vector2(0f, 0f));
            int i1 = AddVertex(b, n, col, new Vector2(0f, 1f));
            int i2 = AddVertex(c, n, col, new Vector2(1f, 1f));
            int i3 = AddVertex(d, n, col, new Vector2(1f, 0f));
            AddTri(i0, i1, i2);
            AddTri(i0, i2, i3);
        }

        /// <summary>Both sides of a flat quad (thin cloth, leaves, paper, banners).</summary>
        public void QuadTwoSided(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var n = Vector3.Cross(b - a, c - a);
            Quad(a, b, c, d, n);
            Quad(a, b, c, d, -n);
        }

        // ---------------------------------------------------------------- primitives

        /// <summary>Axis-aligned box (in the current transform). topColor tints the top face only (moss, snow, roofs).</summary>
        public void Box(Vector3 center, Vector3 size, Color? topColor = null)
        {
            var h = size * 0.5f;
            var c = center;
            Vector3 P(float x, float y, float z) => new Vector3(c.x + x * h.x, c.y + y * h.y, c.z + z * h.z);
            var keep = Color;
            if (topColor.HasValue) Color = topColor.Value;
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.up);
            Color = keep;
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward);
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), Vector3.back);
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right);
            Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), Vector3.left);
        }

        /// <summary>Box with its bottom face centred on `bottom` (props standing on the ground).</summary>
        public void BoxOn(Vector3 bottom, Vector3 size, Color? topColor = null) => Box(bottom + new Vector3(0f, size.y * 0.5f, 0f), size, topColor);

        /// <summary>Box whose top is narrowed (taper 0 = box … 1 = pyramid) — tapered torsos, chimneys, pedestals.</summary>
        public void TaperedBox(Vector3 bottom, Vector3 size, float taperX, float taperZ = -1f)
        {
            if (taperZ < 0f) taperZ = taperX;
            float bx = size.x * 0.5f, bz = size.z * 0.5f, tx = bx * (1f - taperX), tz = bz * (1f - taperZ), y = size.y;
            var b0 = bottom + new Vector3(-bx, 0, -bz); var b1 = bottom + new Vector3(bx, 0, -bz);
            var b2 = bottom + new Vector3(bx, 0, bz); var b3 = bottom + new Vector3(-bx, 0, bz);
            var t0 = bottom + new Vector3(-tx, y, -tz); var t1 = bottom + new Vector3(tx, y, -tz);
            var t2 = bottom + new Vector3(tx, y, tz); var t3 = bottom + new Vector3(-tx, y, tz);
            Quad(t0, t3, t2, t1, Vector3.up);
            Quad(b0, b1, b2, b3, Vector3.down);
            Quad(b0, b1, t1, t0, Vector3.back);
            Quad(b1, b2, t2, t1, Vector3.right);
            Quad(b2, b3, t3, t2, Vector3.forward);
            Quad(b3, b0, t0, t3, Vector3.left);
        }

        /// <summary>
        /// Gable roof: covers a `width` (X) × `depth` (Z) wall top at `bottom`, ridge `height` above it running along X;
        /// overhang extends the slopes past the walls on every side; thickness is the slab thickness. gables fills the
        /// triangular wall ends under the roof.
        /// </summary>
        public void Roof(Vector3 bottom, float width, float depth, float height, float overhang = 0.15f, float thickness = 0.08f, bool gables = true)
        {
            float gx = width * 0.5f, gz = depth * 0.5f;
            float hx = gx + overhang, hz = gz + overhang;
            float drop = height * overhang / Mathf.Max(0.01f, gz);
            Vector3 R(float x) => bottom + new Vector3(x, height, 0f);
            Vector3 E(float x, float side) => bottom + new Vector3(x, -drop, side * hz);
            var t = new Vector3(0f, -thickness, 0f);
            Quad(E(-hx, -1f), R(-hx), R(hx), E(hx, -1f), new Vector3(0f, 1f, -1f));
            Quad(E(-hx, 1f), E(hx, 1f), R(hx), R(-hx), new Vector3(0f, 1f, 1f));
            Quad(E(-hx, -1f) + t, E(hx, -1f) + t, R(hx) + t, R(-hx) + t, new Vector3(0f, -1f, 1f));
            Quad(E(-hx, 1f) + t, R(-hx) + t, R(hx) + t, E(hx, 1f) + t, new Vector3(0f, -1f, -1f));
            Quad(E(-hx, -1f), E(hx, -1f), E(hx, -1f) + t, E(-hx, -1f) + t, Vector3.back);
            Quad(E(-hx, 1f), E(-hx, 1f) + t, E(hx, 1f) + t, E(hx, 1f), Vector3.forward);
            Quad(E(-hx, -1f), E(-hx, -1f) + t, R(-hx) + t, R(-hx), Vector3.left);
            Quad(E(-hx, 1f), R(-hx), R(-hx) + t, E(-hx, 1f) + t, Vector3.left);
            Quad(E(hx, -1f), R(hx), R(hx) + t, E(hx, -1f) + t, Vector3.right);
            Quad(E(hx, 1f), E(hx, 1f) + t, R(hx) + t, R(hx), Vector3.right);
            if (!gables) return;
            float apex = height - thickness;
            TriangleFacing(bottom + new Vector3(-gx, 0f, -gz), bottom + new Vector3(-gx, apex, 0f), bottom + new Vector3(-gx, 0f, gz), Vector3.left);
            TriangleFacing(bottom + new Vector3(gx, 0f, -gz), bottom + new Vector3(gx, apex, 0f), bottom + new Vector3(gx, 0f, gz), Vector3.right);
        }

        /// <summary>
        /// Surface of revolution about the local Y axis. profile = (radius, y) from bottom to top. Caps close the ends
        /// where the radius is &gt; 0. smooth = shared normals (bodies, pots); flat = faceted (rocks, low-poly trunks).
        /// ringColors (optional, one per profile point) colour the rings (vertical gradients, stripes).
        /// </summary>
        public void Lathe(IList<Vector2> profile, int sides, bool smooth = false, bool capBottom = true, bool capTop = true,
                          IList<Color> ringColors = null, float angleOffsetDeg = 0f)
        {
            if (profile == null || profile.Count < 2) return;
            sides = Mathf.Max(3, sides);
            float a0 = angleOffsetDeg * Mathf.Deg2Rad;
            Vector3 Ring(int i, int j)
            {
                float a = a0 + j * Mathf.PI * 2f / sides;
                var p = profile[i];
                return new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x);
            }
            Color RingCol(int i) => ringColors != null && i < ringColors.Count ? ringColors[i] : Color;
            int n = profile.Count;
            if (smooth)
            {
                // profile normals in (r, y): perpendicular to the averaged segment tangents
                var pn = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    var t = Vector2.zero;
                    if (i > 0) t += (profile[i] - profile[i - 1]).normalized;
                    if (i < n - 1) t += (profile[i + 1] - profile[i]).normalized;
                    pn[i] = t.sqrMagnitude > 1e-8f ? new Vector2(t.y, -t.x).normalized : new Vector2(1f, 0f);
                }
                var idx = new int[n, sides + 1];
                for (int i = 0; i < n; i++)
                {
                    var c = RingCol(i);
                    for (int j = 0; j <= sides; j++)
                    {
                        float a = a0 + j * Mathf.PI * 2f / sides;
                        var nrm = new Vector3(Mathf.Cos(a) * pn[i].x, pn[i].y, Mathf.Sin(a) * pn[i].x);
                        idx[i, j] = AddVertex(Ring(i, j % sides), nrm, c, new Vector2((float)j / sides, (float)i / (n - 1)));
                    }
                }
                for (int i = 0; i < n - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int a = idx[i, j], b = idx[i + 1, j], c = idx[i + 1, j + 1], d = idx[i, j + 1];
                        AddTri(a, b, c);
                        AddTri(a, c, d);
                    }
            }
            else
            {
                var keep = Color;
                for (int i = 0; i < n - 1; i++)
                {
                    Color = Color.Lerp(RingCol(i), RingCol(i + 1), 0.5f);
                    for (int j = 0; j < sides; j++)
                    {
                        var a = Ring(i, j); var b = Ring(i + 1, j); var c = Ring(i + 1, j + 1); var d = Ring(i, j + 1);
                        var mid = (a + b + c + d) * 0.25f;
                        var radial = new Vector3(mid.x, 0f, mid.z);
                        radial = radial.sqrMagnitude > 1e-10f ? radial.normalized : Vector3.zero;
                        float dr = profile[i + 1].x - profile[i].x, dy = profile[i + 1].y - profile[i].y;
                        var outward = radial * dy - Vector3.up * dr;   // the profile normal (dy, −dr), swept around Y
                        if (outward.sqrMagnitude < 1e-10f) continue;
                        if (profile[i].x < 1e-5f) TriangleFacing(a, b, c, outward);
                        else if (profile[i + 1].x < 1e-5f) TriangleFacing(a, c, d, outward);
                        else Quad(a, b, c, d, outward);
                    }
                }
                Color = keep;
            }
            var keepC = Color;
            if (capBottom && profile[0].x > 1e-5f)
            {
                Color = RingCol(0);
                var center = new Vector3(0f, profile[0].y, 0f);
                for (int j = 0; j < sides; j++) TriangleFacing(center, Ring(0, j), Ring(0, j + 1), Vector3.down);
            }
            if (capTop && profile[n - 1].x > 1e-5f)
            {
                Color = RingCol(n - 1);
                var center = new Vector3(0f, profile[n - 1].y, 0f);
                for (int j = 0; j < sides; j++) TriangleFacing(center, Ring(n - 1, j + 1), Ring(n - 1, j), Vector3.up);
            }
            Color = keepC;
        }

        /// <summary>Frustum / cylinder standing on `bottom` (rTop = 0: cone).</summary>
        public void Cylinder(Vector3 bottom, float rBottom, float rTop, float height, int sides, bool smooth = false,
                             bool capBottom = true, bool capTop = true)
        {
            Push().Translate(bottom);
            Lathe(new[] { new Vector2(rBottom, 0f), new Vector2(rTop, height) }, sides, smooth, capBottom, capTop);
            Pop();
        }

        public void Cone(Vector3 bottom, float radius, float height, int sides, bool smooth = false) =>
            Cylinder(bottom, radius, 0f, height, sides, smooth, true, false);

        /// <summary>Ellipsoid (UV sphere) with the given radii.</summary>
        public void Sphere(Vector3 center, Vector3 radii, int sides = 10, int rings = 7, bool smooth = true)
        {
            rings = Mathf.Max(2, rings);
            var prof = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float phi = Mathf.PI * i / rings;
                prof[i] = new Vector2(Mathf.Sin(phi), -Mathf.Cos(phi));
            }
            prof[0].x = 0f; prof[rings].x = 0f;
            Push().Translate(center).Scale(radii);
            Lathe(prof, sides, smooth, false, false);
            Pop();
        }

        public void Sphere(Vector3 center, float radius, int sides = 10, int rings = 7, bool smooth = true) =>
            Sphere(center, new Vector3(radius, radius, radius), sides, rings, smooth);

        /// <summary>Capsule along Y: total height (≥ 2r) standing on `bottom`.</summary>
        public void Capsule(Vector3 bottom, float radius, float height, int sides = 10, bool smooth = true)
        {
            height = Mathf.Max(height, radius * 2f);
            const int cap = 3;
            var prof = new List<Vector2>();
            for (int i = 0; i <= cap; i++) { float a = Mathf.PI * 0.5f * i / cap; prof.Add(new Vector2(Mathf.Sin(a) * radius, radius - Mathf.Cos(a) * radius)); }
            for (int i = 0; i <= cap; i++) { float a = Mathf.PI * 0.5f * i / cap; prof.Add(new Vector2(Mathf.Cos(a) * radius, height - radius + Mathf.Sin(a) * radius)); }
            prof[0] = new Vector2(0f, 0f);
            prof[prof.Count - 1] = new Vector2(0f, height);
            Push().Translate(bottom);
            Lathe(prof, sides, smooth, false, false);
            Pop();
        }

        static readonly Vector3[] IcoVerts;
        static readonly int[] IcoFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        static MeshBuilder()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            IcoVerts = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < IcoVerts.Length; i++) IcoVerts[i] = IcoVerts[i].normalized;
        }

        static float Hash(Vector3 d, int seed)
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
        /// Faceted organic blob: an icosphere (detail 0 = 20 faces, 1 = 80, 2 = 320) with each vertex pushed in/out by
        /// up to `jitter` (fraction of the radius) — rocks, bushes, tree canopies, clouds, moss. flattenBottom squashes the
        /// lower half (rocks sitting on the ground).
        /// </summary>
        public void Blob(Vector3 center, Vector3 radii, int detail = 1, float jitter = 0.15f, int seed = 0, float flattenBottom = 0f)
        {
            var faces = new List<Vector3[]>();
            for (int i = 0; i < IcoFaces.Length; i += 3)
                faces.Add(new[] { IcoVerts[IcoFaces[i]], IcoVerts[IcoFaces[i + 1]], IcoVerts[IcoFaces[i + 2]] });
            for (int d = 0; d < Mathf.Clamp(detail, 0, 3); d++)
            {
                var next = new List<Vector3[]>(faces.Count * 4);
                foreach (var f in faces)
                {
                    var ab = ((f[0] + f[1]) * 0.5f).normalized;
                    var bc = ((f[1] + f[2]) * 0.5f).normalized;
                    var ca = ((f[2] + f[0]) * 0.5f).normalized;
                    next.Add(new[] { f[0], ab, ca });
                    next.Add(new[] { f[1], bc, ab });
                    next.Add(new[] { f[2], ca, bc });
                    next.Add(new[] { ab, bc, ca });
                }
                faces = next;
            }
            Vector3 Displace(Vector3 dir)
            {
                float k = 1f + (Hash(dir, seed) * 2f - 1f) * jitter;
                var p = Vector3.Scale(dir * k, radii);
                if (flattenBottom > 0f && p.y < 0f) p.y *= 1f - flattenBottom;
                return center + p;
            }
            foreach (var f in faces)
            {
                var a = Displace(f[0]); var b = Displace(f[1]); var c = Displace(f[2]);
                TriangleFacing(a, b, c, (a + b + c) / 3f - center);
            }
        }

        /// <summary>Tapered round limb from a to b (arms, legs, branches, rails, staves, ropes).</summary>
        public void Segment(Vector3 a, Vector3 b, float ra, float rb, int sides = 6, bool smooth = false, bool caps = true)
        {
            var axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Push().Translate(a).Rotate(Quaternion.FromToRotation(Vector3.up, axis / len));
            Lathe(new[] { new Vector2(ra, 0f), new Vector2(rb, len) }, sides, smooth, caps, caps);
            Pop();
        }

        /// <summary>Flat disc in the XZ plane facing +Y (or −Y with down).</summary>
        public void Disc(Vector3 center, float radius, int sides = 12, bool down = false)
        {
            for (int j = 0; j < sides; j++)
            {
                float a0 = j * Mathf.PI * 2f / sides, a1 = (j + 1) * Mathf.PI * 2f / sides;
                var p0 = center + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = center + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                TriangleFacing(center, p0, p1, down ? Vector3.down : Vector3.up);
            }
        }

        /// <summary>Flat rectangle in the XZ plane facing +Y.</summary>
        public void Plane(Vector3 center, float sizeX, float sizeZ, bool twoSided = false)
        {
            float x = sizeX * 0.5f, z = sizeZ * 0.5f;
            var a = center + new Vector3(-x, 0f, -z); var b = center + new Vector3(-x, 0f, z);
            var c = center + new Vector3(x, 0f, z); var d = center + new Vector3(x, 0f, -z);
            Quad(a, b, c, d, Vector3.up);
            if (twoSided) Quad(a, b, c, d, Vector3.down);
        }

        /// <summary>Ring/torus around the Y axis (rims, belts, halos, wreaths).</summary>
        public void Torus(Vector3 center, float radius, float tube, int segments = 12, int sides = 5)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                for (int j = 0; j < sides; j++)
                {
                    float b0 = j * Mathf.PI * 2f / sides, b1 = (j + 1) * Mathf.PI * 2f / sides;
                    Vector3 P(float a, float b) => center + new Vector3(Mathf.Cos(a) * (radius + Mathf.Cos(b) * tube), Mathf.Sin(b) * tube, Mathf.Sin(a) * (radius + Mathf.Cos(b) * tube));
                    var p00 = P(a0, b0); var p10 = P(a1, b0); var p11 = P(a1, b1); var p01 = P(a0, b1);
                    var mid = (p00 + p10 + p11 + p01) * 0.25f;
                    float am = (a0 + a1) * 0.5f;
                    var ringCenter = center + new Vector3(Mathf.Cos(am) * radius, 0f, Mathf.Sin(am) * radius);
                    Quad(p00, p10, p11, p01, mid - ringCenter);
                }
            }
        }

        /// <summary>Vertical prism of a convex polygon (x, z) from y0 to y1 (planks, walls, signs, gems in profile).</summary>
        public void Extrude(IList<Vector2> polygon, float y0, float y1, bool capBottom = true, bool capTop = true)
        {
            int n = polygon.Count;
            if (n < 3) return;
            var c2 = Vector2.zero;
            for (int i = 0; i < n; i++) c2 += polygon[i];
            c2 /= n;
            for (int i = 0; i < n; i++)
            {
                var p = polygon[i]; var q = polygon[(i + 1) % n];
                var mid = (p + q) * 0.5f - c2;
                Quad(new Vector3(p.x, y0, p.y), new Vector3(q.x, y0, q.y), new Vector3(q.x, y1, q.y), new Vector3(p.x, y1, p.y), new Vector3(mid.x, 0f, mid.y));
            }
            var ct = new Vector3(c2.x, y1, c2.y); var cb = new Vector3(c2.x, y0, c2.y);
            for (int i = 0; i < n; i++)
            {
                var p = polygon[i]; var q = polygon[(i + 1) % n];
                if (capTop) TriangleFacing(ct, new Vector3(p.x, y1, p.y), new Vector3(q.x, y1, q.y), Vector3.up);
                if (capBottom) TriangleFacing(cb, new Vector3(p.x, y0, p.y), new Vector3(q.x, y0, q.y), Vector3.down);
            }
        }

        /// <summary>Thin double-sided leaf/blade from base to tip (grass, leaves, feathers, flames, hair strands).</summary>
        public void Blade(Vector3 basePoint, Vector3 tip, float width, Vector3? sideways = null)
        {
            var axis = tip - basePoint;
            var side = sideways ?? Vector3.Cross(axis, Vector3.forward);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(axis, Vector3.right);
            side = side.normalized * (width * 0.5f);
            var mid = basePoint + axis * 0.45f;
            var n = Vector3.Cross(axis, side);
            TriangleFacing(basePoint - side * 0.6f, mid - side, tip, n);
            TriangleFacing(basePoint - side * 0.6f, tip, basePoint + side * 0.6f, n);
            TriangleFacing(basePoint + side * 0.6f, tip, mid + side, n);
            TriangleFacing(basePoint - side * 0.6f, mid - side, tip, -n);
            TriangleFacing(basePoint - side * 0.6f, tip, basePoint + side * 0.6f, -n);
            TriangleFacing(basePoint + side * 0.6f, tip, mid + side, -n);
        }

        /// <summary>Adds another builder's geometry (already in its own root space) through the current transform.</summary>
        public void Append(MeshBuilder other)
        {
            if (other == null) return;
            int baseIndex = verts.Count;
            for (int i = 0; i < other.verts.Count; i++)
            {
                verts.Add(matrix.MultiplyPoint3x4(other.verts[i]));
                var n = normalMatrix.MultiplyVector(other.normals[i]);
                normals.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
                colors.Add(other.colors[i]);
                uv0.Add(other.uv0[i]);
                uv1.Add(other.uv1[i]);
                bones.Add(other.bones[i]);
            }
            for (int i = 0; i < other.tris.Count; i += 3)
                AddTri(other.tris[i] + baseIndex, other.tris[i + 1] + baseIndex, other.tris[i + 2] + baseIndex);
        }

        public void Clear()
        {
            verts.Clear(); normals.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); bones.Clear(); tris.Clear();
            stack.Clear();
            Matrix = Matrix4x4.identity;
        }

        // ---------------------------------------------------------------- output

        /// <summary>
        /// The mesh. With bindposes, every vertex is rigidly bound to its Bone (use with a SkinnedMeshRenderer whose
        /// bones array matches the indices). Smoothed normals for the outline go into the tangents.
        /// </summary>
        public Mesh ToMesh(string name, Matrix4x4[] bindposes = null)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTangents(SmoothNormals());
            mesh.SetTriangles(tris, 0, true);
            if (bindposes != null)
            {
                var bw = new BoneWeight[verts.Count];
                for (int i = 0; i < bw.Length; i++) bw[i] = new BoneWeight { boneIndex0 = Mathf.Clamp(bones[i], 0, bindposes.Length - 1), weight0 = 1f };
                mesh.boneWeights = bw;
                mesh.bindposes = bindposes;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        List<Vector4> SmoothNormals()
        {
            var sum = new Dictionary<Vector3Int, Vector3>(verts.Count);
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.y * 2000f), Mathf.RoundToInt(p.z * 2000f));
            for (int i = 0; i < verts.Count; i++)
            {
                var k = Key(verts[i]);
                sum.TryGetValue(k, out var s);
                sum[k] = s + normals[i];
            }
            var result = new List<Vector4>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                var s = sum[Key(verts[i])];
                var n = s.sqrMagnitude > 1e-10f ? s.normalized : normals[i];
                result.Add(new Vector4(n.x, n.y, n.z, 1f));
            }
            return result;
        }
    }

    /// <summary>Colour helpers for hand-painted palettes.</summary>
    public static class Paint
    {
        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        /// <summary>Brightness multiply (k &lt; 1 darker, &gt; 1 lighter).</summary>
        public static Color Shade(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);

        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t);

        /// <summary>Hue shift (degrees), saturation and value multipliers.</summary>
        public static Color Hsv(Color c, float hueShiftDeg, float sat = 1f, float val = 1f)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            h = Mathf.Repeat(h + hueShiftDeg / 360f, 1f);
            var r = Color.HSVToRGB(h, Mathf.Clamp01(s * sat), Mathf.Clamp01(v * val));
            r.a = c.a;
            return r;
        }
    }

    /// <summary>Shared meshes: built once per key, reused by every instance.</summary>
    public static class MeshCache
    {
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        public static Mesh Get(string key, System.Func<Mesh> build)
        {
            if (meshes.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            if (m != null) { m.name = key; meshes[key] = m; }
            return m;
        }

        public static bool Has(string key) => meshes.TryGetValue(key, out var m) && m != null;

        /// <summary>Unit quad (−0.5..0.5) in the XY plane (= the ground), UVs 0..1, facing up (−Z). Decals, shadows, previews.</summary>
        public static Mesh GroundQuad => Get("lv_ground_quad", () =>
        {
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetColors(new List<Color> { new Color(1, 1, 1, 1), new Color(1, 1, 1, 1), new Color(1, 1, 1, 1), new Color(1, 1, 1, 1) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            // clockwise seen from above (−Z looking towards +Z)
            m.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        });

        /// <summary>A soft blob shadow on the ground under a model: child of `parent`, radius rx × ry metres.</summary>
        public static MeshRenderer AddShadow(Transform parent, float rx, float ry, float strength = 0.42f)
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(parent, false);
            // flat on the ground whatever the parent's rotation (keep parents that tilt — lean, fall — out of the chain)
            go.transform.position = parent != null ? new Vector3(parent.position.x, parent.position.y, -0.004f) : Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var ls = go.transform.lossyScale;
            go.transform.localScale = new Vector3(rx * 2f / Mathf.Max(1e-4f, ls.x), ry * 2f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z));
            go.AddComponent<MeshFilter>().sharedMesh = GroundQuad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Materials3D.Shadow;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (Mathf.Abs(strength - 0.42f) > 0.001f)
            {
                var b = new MaterialPropertyBlock();
                b.SetColor(Materials3D.ColorId, new Color(0.12f, 0.09f, 0.16f, strength));
                r.SetPropertyBlock(b);
            }
            return r;
        }
    }
}
