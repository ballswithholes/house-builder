// `map … --nav`: the real NavGrid of the map (as GameSession builds it, flags from --flags) drawn as dots on the ground,
// to check that blocked water matches the water the terrain draws: red = blocked by water; inside a water's crossing
// rect (fords, bridges, docks, boardwalks) green = where a unit's centre can stand (clearance >= the default unit
// radius, 0.4 m), blue = walkable but too tight for a unit; orange = blocked by anything else within 3 m of water.
// Not a game view: a review aid only.
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Game;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Preview
{
    static class NavOverlay
    {
        public static void Build(Transform root, MapDef def, MapTerrain terrain, MapScene.Options opt)
        {
            if (def.water == null || def.water.Count == 0) return;
            var nav = new NavGrid(def, null, opt.Test);
            float cs = nav.CellSize;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color32>(); var tris = new List<int>();
            var red = new Color32(255, 40, 30, 255);
            var green = new Color32(40, 255, 60, 255);
            var tight = new Color32(40, 110, 255, 255);
            float radius = Lanternvale.Rules.RulesConstants.DefaultUnitRadius;
            var orange = new Color32(255, 170, 0, 200);
            int near = Mathf.CeilToInt(3f / cs);
            for (int cy = 0; cy < nav.Height; cy++)
                for (int cx = 0; cx < nav.Width; cx++)
                {
                    float x = (cx + 0.5f) * cs, y = (cy + 0.5f) * cs;
                    Color32 c;
                    if (nav.IsCellWater(cx, cy)) c = red;
                    else if (InCrossing(def, x, y)) { if (nav.IsCellBlocked(cx, cy)) continue; c = nav.Clearance(cx, cy) >= radius - 1e-4f ? green : tight; }
                    else if (nav.IsCellBlocked(cx, cy) && WaterNear(nav, cx, cy, near)) c = orange;
                    else continue;
                    float h = Mathf.Max(terrain.Height(x, y), terrain.BaseHeight(x, y) + MapTerrain.WaterLevel) + opt.NavLift;
                    float s = cs * 0.32f;
                    int b = verts.Count;
                    verts.Add(new Vector3(x - s, y - s, -h)); verts.Add(new Vector3(x - s, y + s, -h));
                    verts.Add(new Vector3(x + s, y + s, -h)); verts.Add(new Vector3(x + s, y - s, -h));
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                    cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            if (verts.Count == 0) return;
            var m = new Mesh { name = "nav overlay" };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetColors(cols); m.SetTriangles(tris, 0, true);
            var go = new GameObject("Nav Overlay");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials3D.AdditiveFor(WorldTextures.Dot);
        }

        static bool InCrossing(MapDef def, float x, float y)
        {
            foreach (var w in def.water)
            {
                if (w?.crossings == null || !w.blocksMovement) continue;
                foreach (var r in w.crossings)
                    if (r != null && Mathf.Abs(x - r.pos.x) <= r.size.x * 0.5f && Mathf.Abs(y - r.pos.y) <= r.size.y * 0.5f) return true;
            }
            return false;
        }

        static bool WaterNear(NavGrid nav, int cx, int cy, int n)
        {
            for (int dy = -n; dy <= n; dy++)
                for (int dx = -n; dx <= n; dx++)
                    if (nav.IsCellWater(cx + dx, cy + dy)) return true;
            return false;
        }
    }
}
