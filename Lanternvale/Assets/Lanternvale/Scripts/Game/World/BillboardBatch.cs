// A pooled batch of quads in ONE dynamic mesh (one draw call): camera-facing billboards (glows, particles) or quads
// with any axes (flat ground markers). Vertices are world positions (keep the GameObject's transform at identity).
// Fill it every frame between Begin() and End(); nothing is allocated after construction.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    internal sealed class BillboardBatch
    {
        public readonly GameObject GameObject;
        public readonly MeshRenderer Renderer;
        readonly Mesh mesh;
        readonly List<Vector3> verts;
        readonly List<Color32> colors;
        readonly int capacity;
        int count, uploaded;
        Vector3 min, max, last;
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        public int Capacity => capacity;
        public int Count => count;

        public BillboardBatch(string name, Transform parent, Material material, int capacity, int sortingOrder = 0)
        {
            this.capacity = Mathf.Max(1, capacity);
            GameObject = new GameObject(name);
            GameObject.transform.SetParent(parent, false);
            GameObject.transform.localPosition = Vector3.zero;
            GameObject.transform.localRotation = Quaternion.identity;
            GameObject.transform.localScale = Vector3.one;

            int n = this.capacity * 4;
            verts = new List<Vector3>(n);
            colors = new List<Color32>(n);
            var uvs = new List<Vector2>(n);
            var normals = new List<Vector3>(n);
            var tris = new List<int>(this.capacity * 6);
            for (int i = 0; i < this.capacity; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    verts.Add(Vector3.zero);
                    colors.Add(Clear);
                    normals.Add(World3D.Up);
                }
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                int b = i * 4;
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);

            GameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer = GameObject.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.shadowCastingMode = ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Renderer.sortingOrder = sortingOrder;
            Renderer.enabled = false;
        }

        public void Begin()
        {
            count = 0;
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        }

        /// <summary>
        /// Adds a quad centred on c spanning ±right (u) and ±up (v); right/up are HALF extents in world units. Pass
        /// camera right/up (scaled) for a billboard, or ground axes for a flat quad. False when the batch is full.
        /// </summary>
        public bool Add(Vector3 c, Vector3 right, Vector3 up, Color32 color)
        {
            if (count >= capacity) return false;
            int b = count * 4;
            verts[b] = c - right - up;
            verts[b + 1] = c - right + up;
            verts[b + 2] = c + right + up;
            verts[b + 3] = c + right - up;
            colors[b] = color; colors[b + 1] = color; colors[b + 2] = color; colors[b + 3] = color;
            float r = Mathf.Abs(right.x) + Mathf.Abs(up.x), s = Mathf.Abs(right.y) + Mathf.Abs(up.y), t = Mathf.Abs(right.z) + Mathf.Abs(up.z);
            if (c.x - r < min.x) min.x = c.x - r;
            if (c.y - s < min.y) min.y = c.y - s;
            if (c.z - t < min.z) min.z = c.z - t;
            if (c.x + r > max.x) max.x = c.x + r;
            if (c.y + s > max.y) max.y = c.y + s;
            if (c.z + t > max.z) max.z = c.z + t;
            last = c;
            count++;
            return true;
        }

        /// <summary>Uploads this frame's quads (unused ones collapse to nothing).</summary>
        public void End()
        {
            for (int i = count; i < uploaded; i++)
            {
                int b = i * 4;
                for (int k = 0; k < 4; k++)
                {
                    verts[b + k] = last;
                    colors[b + k] = Clear;
                }
            }
            bool any = count > 0;
            if (any || uploaded > 0)
            {
                mesh.SetVertices(verts, 0, verts.Count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                mesh.SetColors(colors, 0, colors.Count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                if (any)
                {
                    var b = new Bounds();
                    b.SetMinMax(min, max);
                    mesh.bounds = b;
                }
            }
            uploaded = count;
            if (Renderer.enabled != any) Renderer.enabled = any;
        }

        public void SetVisible(bool on)
        {
            if (GameObject.activeSelf != on) GameObject.SetActive(on);
        }

        public void Dispose()
        {
            if (mesh != null) Object.Destroy(mesh);
        }
    }
}
