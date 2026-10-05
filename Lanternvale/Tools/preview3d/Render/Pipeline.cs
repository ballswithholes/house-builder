// The frame order of the game's forward renderer, shared by the map and unit modes: sky (queue 1000), opaque and
// terrain (2000) into the G-buffer, lighting, the ink hulls, then the transparent queues (decals 2950, merged
// shadows 2955, additive 3000) back to front within a queue.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
    public static class Pipeline
    {
        public static void Render(Frame frame, List<DrawCall> draws, int inkRadius)
        {
            // per-renderer state the collector does not resolve: occluder cut-outs, the terrain's detail layer
            foreach (var d in draws)
            {
                var cuts = CutRegistry.Find(d.Mesh, d.M);
                if (cuts != null) { d.Cuts = cuts; frame.SetCuts(d.ObjId, cuts); }
                if (d.Kind == DrawKind.Terrain) TerrainDetail.Apply(d);
            }
            foreach (var d in draws.Where(d => d.Kind == DrawKind.Sky)) frame.Draw(d);
            foreach (var d in draws.Where(d => d.Kind == DrawKind.Opaque || d.Kind == DrawKind.Terrain)) frame.Draw(d);
            frame.Resolve();
            if (inkRadius > 0) frame.Ink(inkRadius, Materials3D.Ink);
            var transparent = draws.Where(d => d.Kind == DrawKind.LitTransparent || d.Kind == DrawKind.Shadow || d.Kind == DrawKind.Additive)
                                   .OrderBy(d => d.Order).ThenBy(d => d.Queue).ThenByDescending(d => d.SortDist).ToList();
            foreach (var d in transparent) frame.Draw(d);
        }
    }

    /// <summary>
    /// The occluder cut-outs (_Cut0.._Cut3) the map scene gave a renderer, as MapView writes them into its property
    /// block; draws find theirs by mesh and world matrix.
    /// </summary>
    public static class CutRegistry
    {
        static readonly Dictionary<Mesh, List<(Matrix4x4 m, Vector4[] cuts)>> map = new Dictionary<Mesh, List<(Matrix4x4, Vector4[])>>();

        public static void Set(Renderer r, Vector4[] cuts)
        {
            var mesh = r?.RenderMesh;
            if (mesh == null) return;
            if (!map.TryGetValue(mesh, out var list)) map[mesh] = list = new List<(Matrix4x4, Vector4[])>();
            var m = r.transform.localToWorldMatrix;
            list.RemoveAll(e => Same(e.m, m));
            list.Add((m, cuts));
        }

        public static Vector4[] Find(Mesh mesh, Matrix4x4 m)
        {
            if (mesh == null || !map.TryGetValue(mesh, out var list)) return null;
            foreach (var e in list) if (Same(e.m, m)) return e.cuts;
            return null;
        }

        static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Math.Abs(a[i >> 2, i & 3] - b[i >> 2, i & 3]) > 1e-5f) return false;
            return true;
        }
    }

    /// <summary>The terrain material's detail layer (_DetailTex, _DetailPlanarScale), keyed by its ground texture.</summary>
    public static class TerrainDetail
    {
        static readonly Dictionary<Texture, (Texture tex, float scale)> map = new Dictionary<Texture, (Texture, float)>();

        public static void Register(Texture ground, Texture detail, float scale)
        {
            if (ground == null) return;
            if (detail == null) map.Remove(ground);
            else map[ground] = (detail, scale);
        }

        public static void Apply(DrawCall d)
        {
            foreach (var kv in map)
            {
                if (Sampler.For(kv.Key) != d.Tex) continue;
                d.Tex3 = Sampler.For(kv.Value.tex);
                d.Planar3 = kv.Value.scale;
                return;
            }
        }
    }
}
