// Occlusion test for the dither fade of tall props (MapView.UpdateFades; the preview tool uses it too): does this prop
// really stand between the camera and a unit? A bounding box cannot tell — a tree's box is mostly empty air under and
// around the canopy, so a box test fades trees behind units and (shrunk to avoid that) misses canopies in front of
// them. Instead each mesh of the model is voxelised once, in its own space, into a coarse occupancy grid of its
// surface, and camera rays to several points of the unit's body are marched through it.
//
// A ray counts as blocked only when it meets the surface clearly in front of the body point (Margin). The prop fades
// when it hides a real part of the body — several points across its width, not just a thin pole crossing it — and
// stays faded while it hides any point (no flicker at the edge).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    internal sealed class PropOccluder
    {
        /// <summary>A body point is hidden when the ray meets the prop at least this far (m) in front of it.</summary>
        public const float Margin = 0.4f;
        /// <summary>Hidden body points that start a fade (out of BodySamples, spread over at least two columns).</summary>
        public const int FadeAt = 3;
        /// <summary>3 × 3 grid over the body plus the head.</summary>
        public const int BodySamples = 10;

        /// <summary>Occupancy grid of one mesh, in that mesh's local space (shared by every prop using the mesh).</summary>
        internal sealed class Grid
        {
            public Vector3 min;
            public float cell, inv;
            public int nx, ny, nz;
            public ulong[] bits;
            public Bounds bounds;

            public bool Solid(int x, int y, int z)
            {
                if ((uint)x >= (uint)nx || (uint)y >= (uint)ny || (uint)z >= (uint)nz) return false;
                int i = (z * ny + y) * nx + x;
                return (bits[i >> 6] & (1UL << (i & 63))) != 0;
            }

            void Set(Vector3 p)
            {
                int x = (int)((p.x - min.x) * inv), y = (int)((p.y - min.y) * inv), z = (int)((p.z - min.z) * inv);
                if ((uint)x >= (uint)nx || (uint)y >= (uint)ny || (uint)z >= (uint)nz) return;
                int i = (z * ny + y) * nx + x;
                bits[i >> 6] |= 1UL << (i & 63);
            }

            public static Grid Build(Mesh mesh)
            {
                if (mesh == null) return null;
                var v = mesh.vertices;
                var t = mesh.triangles;
                if (v == null || v.Length == 0 || t == null || t.Length < 3) return null;
                var b = new Bounds(v[0], Vector3.zero);
                for (int i = 1; i < v.Length; i++) b.Encapsulate(v[i]);
                var g = new Grid { bounds = b };
                var size = b.size;
                float big = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                // ~0.25 m cells for props, coarser for the giants (≤ 48 cells along the longest side)
                g.cell = Mathf.Max(0.25f, big / 48f);
                g.inv = 1f / g.cell;
                g.min = b.min - Vector3.one * (g.cell * 0.5f);
                g.nx = Mathf.Max(1, Mathf.CeilToInt((size.x + g.cell) * g.inv));
                g.ny = Mathf.Max(1, Mathf.CeilToInt((size.y + g.cell) * g.inv));
                g.nz = Mathf.Max(1, Mathf.CeilToInt((size.z + g.cell) * g.inv));
                g.bits = new ulong[(g.nx * g.ny * g.nz + 63) / 64];
                // mark every cell the surface passes through: sample each triangle finer than half a cell
                float step = g.cell * 0.55f;
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Vector3 a = v[t[k]], e1 = v[t[k + 1]] - a, e2 = v[t[k + 2]] - a;
                    float longest = Mathf.Max(e1.magnitude, Mathf.Max(e2.magnitude, (e2 - e1).magnitude));
                    int n = Mathf.Clamp(Mathf.CeilToInt(longest / step), 1, 400);
                    float inv = 1f / n;
                    for (int i = 0; i <= n; i++)
                        for (int j = 0; i + j <= n; j++)
                            g.Set(a + e1 * (i * inv) + e2 * (j * inv));
                }
                return g;
            }

            /// <summary>First solid cell along the local segment a→b within the parameter range [0, sMax]; s of the hit.</summary>
            public bool March(Vector3 a, Vector3 b, float sMax, out float s)
            {
                s = 0f;
                var d = b - a;
                // clip to the grid's box (slab test in local space)
                float t0 = 0f, t1 = sMax;
                var mn = bounds.min; var mx = bounds.max;
                for (int i = 0; i < 3; i++)
                {
                    float o = a[i], di = d[i];
                    if (Mathf.Abs(di) < 1e-9f) { if (o < mn[i] - cell || o > mx[i] + cell) return false; continue; }
                    float u0 = (mn[i] - cell - o) / di, u1 = (mx[i] + cell - o) / di;
                    if (u0 > u1) { float tmp = u0; u0 = u1; u1 = tmp; }
                    if (u0 > t0) t0 = u0;
                    if (u1 < t1) t1 = u1;
                    if (t0 > t1) return false;
                }
                float len = d.magnitude;
                if (len < 1e-6f) return false;
                float ds = cell * 0.45f / len;
                for (float u = t0; u <= t1; u += ds)
                {
                    var p = a + d * u;
                    if (Solid((int)((p.x - min.x) * inv), (int)((p.y - min.y) * inv), (int)((p.z - min.z) * inv))) { s = u; return true; }
                }
                return false;
            }
        }

        struct Part { public Grid grid; public Matrix4x4 toLocal; }

        readonly List<Part> parts = new List<Part>();
        /// <summary>World bounds of the voxelised parts.</summary>
        public Bounds Bounds;
        readonly Vector3[] samples = new Vector3[BodySamples];

        /// <summary>
        /// Voxelises the model's mesh renderers as they are placed now (call after positioning the prop). grids caches the
        /// per-mesh grids between props (pass one dictionary per map, or null). Null when there is nothing to test.
        /// </summary>
        public static PropOccluder Build(IList<Renderer> renderers, Dictionary<Mesh, Grid> grids)
        {
            if (renderers == null) return null;
            PropOccluder o = null;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                Grid g = null;
                if (grids == null || !grids.TryGetValue(mesh, out g))
                {
                    g = Grid.Build(mesh);
                    if (grids != null) grids[mesh] = g;
                }
                if (g == null) continue;
                var l2w = r.transform.localToWorldMatrix;
                if (o == null) o = new PropOccluder();
                o.parts.Add(new Part { grid = g, toLocal = l2w.inverse });
                // world bounds of the part's local box
                var lb = g.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var p = l2w.MultiplyPoint3x4(new Vector3((c & 1) != 0 ? lb.max.x : lb.min.x, (c & 2) != 0 ? lb.max.y : lb.min.y, (c & 4) != 0 ? lb.max.z : lb.min.z));
                    if (o.parts.Count == 1 && c == 0) o.Bounds = new Bounds(p, Vector3.zero);
                    else o.Bounds.Encapsulate(p);
                }
            }
            return o;
        }

        /// <summary>True when the segment from → to meets the prop's surface more than margin before reaching to.</summary>
        public bool Blocks(Vector3 from, Vector3 to, float margin = Margin)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len <= margin + 0.01f) return false;
            float sMax = 1f - margin / len;
            // cheap reject against the world bounds
            if (!SegmentHitsBox(Bounds, from, d, sMax)) return false;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var a = p.toLocal.MultiplyPoint3x4(from);
                var b = p.toLocal.MultiplyPoint3x4(to);
                if (p.grid.March(a, b, sMax, out _)) return true;
            }
            return false;
        }

        /// <summary>Slab test: does the segment from + d·s, s ∈ [0, sMax], touch the box?</summary>
        static bool SegmentHitsBox(Bounds b, Vector3 from, Vector3 d, float sMax)
        {
            float t0 = 0f, t1 = sMax;
            var mn = b.min; var mx = b.max;
            for (int i = 0; i < 3; i++)
            {
                float o = from[i], di = d[i];
                if (Mathf.Abs(di) < 1e-9f) { if (o < mn[i] || o > mx[i]) return false; continue; }
                float u0 = (mn[i] - o) / di, u1 = (mx[i] - o) / di;
                if (u0 > u1) { float tmp = u0; u0 = u1; u1 = tmp; }
                if (u0 > t0) t0 = u0;
                if (u1 < t1) t1 = u1;
                if (t0 > t1) return false;
            }
            return true;
        }

        /// <summary>
        /// Which of the unit's body points the prop hides from the camera: a 3 × 3 grid over the body (three heights,
        /// left / middle / right across the view) plus the head. Returns the hidden count; columns gets one bit per
        /// column with a hidden point (1 left, 2 middle, 4 right; the head counts as middle). Stops at stopAt.
        /// center / head: the unit's CenterPosition / HeadPosition; camRight: the camera's right vector.
        /// </summary>
        public int HiddenPoints(Vector3 cam, Vector3 camRight, Vector3 center, Vector3 head, out int columns, int stopAt = BodySamples)
        {
            var feet = new Vector3(center.x, center.y, 0f);
            var axis = head - feet;
            float h = Mathf.Max(0.3f, axis.magnitude);
            var side = new Vector3(camRight.x, camRight.y, 0f);
            side = side.sqrMagnitude > 1e-6f ? side.normalized * Mathf.Clamp(h * 0.12f, 0.1f, 0.9f) : Vector3.zero;
            int n = 0;
            for (int row = 0; row < 3; row++)
            {
                var c = feet + axis * (0.2f + row * 0.26f);
                samples[n++] = c - side;
                samples[n++] = c;
                samples[n++] = c + side;
            }
            samples[n] = feet + axis * 0.93f;
            int hidden = 0;
            columns = 0;
            for (int i = 0; i < BodySamples && hidden < stopAt; i++)
            {
                if (!Blocks(cam, samples[i])) continue;
                hidden++;
                columns |= i == BodySamples - 1 ? 2 : 1 << (i % 3);
            }
            return hidden;
        }

        /// <summary>
        /// The fade decision with hysteresis. A prop starts fading when it hides at least FadeAt body points across at
        /// least two of the three columns (a thin pole crossing the body leaves the unit readable and stays solid), and
        /// stays faded while it hides any point.
        /// </summary>
        public bool Hides(Vector3 cam, Vector3 camRight, Vector3 center, Vector3 head, bool fadedNow)
        {
            if (fadedNow) return HiddenPoints(cam, camRight, center, head, out _, 1) >= 1;
            int hidden = HiddenPoints(cam, camRight, center, head, out int cols);
            int spread = (cols & 1) + ((cols >> 1) & 1) + ((cols >> 2) & 1);
            return hidden >= FadeAt && spread >= 2;
        }
    }
}
