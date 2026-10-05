// Geometry for the 3D effects: dynamic ribbons (projectile trails, beams, slash arcs) and the low-poly projectile models
// (arrow, throwing dagger, throwing axe). Ribbons are pooled by FxSystem and rebuilt in place every frame without
// allocating (fixed-size vertex arrays, unused points collapsed onto the last one).
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    /// <summary>
    /// A strip of quads through up to MaxPoints centre points, each with its own half-width vector (camera-facing or
    /// any direction), colour and texture u. World space: the GameObject stays at the origin. Texture v runs 0 → 1
    /// across the strip (from centre − side to centre + side).
    /// </summary>
    internal sealed class FxRibbon
    {
        public const int MaxPoints = 32;

        public readonly GameObject go;
        public readonly MeshRenderer renderer;
        public bool inUse;
        readonly Mesh mesh;
        readonly Vector3[] verts = new Vector3[MaxPoints * 2];
        readonly Color[] cols = new Color[MaxPoints * 2];
        readonly Vector2[] uvs = new Vector2[MaxPoints * 2];
        int count;
        bool linear;

        public FxRibbon(Transform parent)
        {
            go = new GameObject("Ribbon");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            mesh = new Mesh { name = "lv_fx_ribbon" };
            mesh.MarkDynamic();
            var tris = new int[(MaxPoints - 1) * 6];
            for (int i = 0; i < MaxPoints - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3, k = i * 6;
                tris[k] = a; tris[k + 1] = c; tris[k + 2] = b;
                tris[k + 3] = b; tris[k + 4] = c; tris[k + 5] = d;
            }
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.uv = uvs;
            mesh.triangles = tris;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
        }

        public void Begin()
        {
            count = 0;
            linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        }

        public int Count => count;

        /// <summary>Adds a point: the strip passes from centre − halfSide to centre + halfSide here.</summary>
        public void Add(Vector3 centre, Vector3 halfSide, Color c, float u)
        {
            if (count >= MaxPoints) return;
            int i = count * 2;
            verts[i] = centre - halfSide;
            verts[i + 1] = centre + halfSide;
            // vertex colours are not colour-space converted by the additive shader: convert like sprite colours are
            if (linear) { float a = c.a; c = c.linear; c.a = a; }
            cols[i] = c; cols[i + 1] = c;
            uvs[i] = new Vector2(u, 0f);
            uvs[i + 1] = new Vector2(u, 1f);
            count++;
        }

        /// <summary>Adds a point whose side faces the camera at camPos, for a strip running along `tangent`.</summary>
        public void AddFacing(Vector3 centre, Vector3 tangent, Vector3 camPos, Vector3 camRight, float halfWidth, Color c, float u)
        {
            var side = Vector3.Cross(tangent, camPos - centre);
            float m = side.sqrMagnitude;
            side = m > 1e-10f ? side * (halfWidth / Mathf.Sqrt(m)) : camRight * halfWidth;
            Add(centre, side, c, u);
        }

        /// <summary>Uploads the strip; unused points collapse onto the last one (invisible). Fewer than 2 points hide it.</summary>
        public void End()
        {
            if (count < 2) { renderer.enabled = false; return; }
            int last = (count - 1) * 2;
            var clear = cols[last];
            clear.a = 0f;
            for (int i = count * 2; i < verts.Length; i += 2)
            {
                verts[i] = verts[last];
                verts[i + 1] = verts[last + 1];
                cols[i] = clear; cols[i + 1] = clear;
                uvs[i] = uvs[last]; uvs[i + 1] = uvs[last + 1];
            }
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            renderer.enabled = true;
        }

        public void Hide()
        {
            count = 0;
            renderer.enabled = false;
        }
    }

    /// <summary>Projectile models (built once, cached) and the strip textures of ribbons.</summary>
    internal static class FxModels
    {
        /// <summary>Arrow along +Z (tip at +0.47, nock at −0.45), Y up; sized up a little so it reads in flight.</summary>
        public static Mesh Arrow => MeshCache.Get("fxmodel_arrow", () =>
        {
            var b = new MeshBuilder(7) { Jitter = 0.04f };
            b.Color = Paint.Hex("#9a6b43");
            b.Segment(new Vector3(0f, 0f, -0.45f), new Vector3(0f, 0f, 0.34f), 0.02f, 0.018f, 5);
            b.Color = Paint.Hex("#c9ccd4");
            b.Segment(new Vector3(0f, 0f, 0.32f), new Vector3(0f, 0f, 0.48f), 0.05f, 0.004f, 4);
            for (int k = 0; k < 3; k++)
            {
                float a = (k * 120f + 90f) * Mathf.Deg2Rad;
                var side = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                b.Color = k == 0 ? Paint.Hex("#d9583f") : Paint.Hex("#f2ead8");
                b.Blade(new Vector3(0f, 0f, -0.44f) + side * 0.036f, new Vector3(0f, 0f, -0.24f) + side * 0.03f, 0.06f, side);
            }
            return b.ToMesh("fx_arrow");
        });

        /// <summary>Throwing dagger along +Z (point at +0.34), flat blade in the XZ plane.</summary>
        public static Mesh Dagger => MeshCache.Get("fxmodel_dagger", () =>
        {
            var b = new MeshBuilder(11) { Jitter = 0.04f };
            b.Push().Scale(new Vector3(1.7f, 0.35f, 1f));
            b.Color = Paint.Hex("#d6dae2");
            b.Segment(new Vector3(0f, 0f, 0.0f), new Vector3(0f, 0f, 0.34f), 0.035f, 0.002f, 4);
            b.Pop();
            b.Color = Paint.Hex("#b08a3e");
            b.Box(new Vector3(0f, 0f, -0.005f), new Vector3(0.14f, 0.035f, 0.03f));
            b.Color = Paint.Hex("#5a3b2a");
            b.Segment(new Vector3(0f, 0f, -0.14f), new Vector3(0f, 0f, -0.01f), 0.02f, 0.02f, 6);
            b.Color = Paint.Hex("#b08a3e");
            b.Sphere(new Vector3(0f, 0f, -0.15f), 0.028f, 6, 4, false);
            return b.ToMesh("fx_dagger");
        });

        /// <summary>Throwing axe along +Z (head at the +Z end, blade towards +X).</summary>
        public static Mesh Axe => MeshCache.Get("fxmodel_axe", () =>
        {
            var b = new MeshBuilder(13) { Jitter = 0.05f };
            b.Color = Paint.Hex("#8a5a36");
            b.Segment(new Vector3(0f, 0f, -0.28f), new Vector3(0f, 0f, 0.26f), 0.024f, 0.022f, 6);
            b.Color = Paint.Hex("#c4c8d0");
            b.Box(new Vector3(0.07f, 0f, 0.17f), new Vector3(0.12f, 0.035f, 0.1f));
            b.Color = Paint.Hex("#e4e7ee");
            b.Box(new Vector3(0.15f, 0f, 0.17f), new Vector3(0.05f, 0.028f, 0.2f));
            b.Color = Paint.Hex("#5a3b2a");
            b.Box(new Vector3(0f, 0f, -0.2f), new Vector3(0.055f, 0.055f, 0.1f));
            return b.ToMesh("fx_axe");
        });

        static Texture2D slashStrip;

        /// <summary>
        /// Strip texture of a weapon arc: across v (0 inner edge → 1 outer edge) the glow builds towards a bright, crisp
        /// outer rim; along u it is uniform.
        /// </summary>
        public static Texture2D SlashStrip
        {
            get
            {
                if (slashStrip != null) return slashStrip;
                const int w = 8, h = 64;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "lv_slashstrip", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h;
                    float body = Mathf.Pow(v, 1.8f) * 0.75f;
                    float rim = Mathf.Exp(-(v - 0.88f) * (v - 0.88f) / 0.0035f);
                    float edge = Mathf.Clamp01((1f - v) * 14f);   // soft outer cut
                    float a = Mathf.Clamp01(Mathf.Max(body, rim) * edge);
                    byte g = (byte)(Mathf.Lerp(0.8f, 1f, rim) * 255f);
                    for (int x = 0; x < w; x++) px[y * w + x] = new Color32(g, g, g, (byte)(a * 255f));
                }
                t.SetPixels32(px);
                t.Apply(false, true);
                slashStrip = t;
                return t;
            }
        }
    }
}
