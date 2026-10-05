// The land of a map (World / MapView): one heightfield, flat at z = 0 over the walkable rect plus a margin, rolling into
// hills behind (y > depth), gentle banks and meadows in front (y < 0) and continuing far to the left and right so a
// yawed camera never sees the world's edge. Rendered as a few chunked, faceted (low-poly) meshes with the map's ground
// texture blending softly into the surrounding meadow (Lanternvale/Terrain), vertex-colour variation (meadow swathes,
// worn paths, shade under trees and buildings, sunny crests, field patches on far hills), ground cover (grass tufts,
// flowers, stones, bushes, sparse trees) outside the walkable area, and, when the map has a bridge, a brook under it.
//
// Heights are metres above the ground plane (world z = −height). Inside the walkable rect the height is exactly 0.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    internal sealed class MapTerrain
    {
        // flat margins around the walkable rect (props and building backs sit there)
        public const float FlatSide = 4f, FlatBack = 4.5f, FlatFront = 3f;

        readonly MapDef def;
        readonly float W, D;
        readonly float s1, s2, s3, s4, s5, s6;
        readonly List<Mesh> owned;
        readonly Transform root;

        struct Blot { public Vector2 p; public float r; public Color tint; public float k; }
        readonly List<Blot> blots = new List<Blot>();
        readonly List<Rect> paths = new List<Rect>();
        readonly List<Vector2> corridors = new List<Vector2>();   // x: −1 left / +1 right, y: centre

        // style
        Color innerTint = Color.white, sideTint = Color.white, pathTint = new Color(1.2f, 0.92f, 0.8f);
        float blendStart = 1.5f, blendEnd = 7f;
        bool fields, forestBehind, cliffsBehind;

        // brook (maps with a bridge)
        public bool HasStream { get; private set; }
        float streamX, streamY;
        Material waterMat;
        float waterScroll;

        public Material Material { get; private set; }
        public Color GroundAverage { get; private set; }

        public MapTerrain(MapDef def, Transform parent, List<Mesh> owned)
        {
            this.def = def;
            this.owned = owned;
            W = Mathf.Max(1f, def.width);
            D = Mathf.Max(1f, def.depth);
            var rng = new System.Random(StableHash(def.id) ^ 0x5bd1e995);
            s1 = (float)rng.NextDouble() * 200f; s2 = (float)rng.NextDouble() * 200f;
            s3 = (float)rng.NextDouble() * 200f; s4 = (float)rng.NextDouble() * 200f;
            s5 = (float)rng.NextDouble() * 200f; s6 = (float)rng.NextDouble() * 200f;
            root = new GameObject("Terrain").transform;
            root.SetParent(parent, false);
            Analyse();
        }

        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                if (s != null) foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ================================================================== map analysis

        void Analyse()
        {
            string ground = string.IsNullOrEmpty(def.ground) ? "ground_meadow" : def.ground;
            var gt = string.IsNullOrEmpty(def.groundTint) ? Color.white : Ui.Hex(def.groundTint);
            // the lights are bright on up-facing ground: a little under white keeps the painted ground as painted
            innerTint = gt * 0.86f;
            if (ground.Contains("forest"))
            {
                sideTint = new Color(0.66f, 0.8f, 0.64f);
                blendStart = 4f; blendEnd = 15f;
            }
            else if (ground.Contains("shrine"))
            {
                sideTint = Color.Lerp(gt, new Color(0.82f, 0.9f, 0.78f), 0.5f) * 0.82f;
                blendStart = 1.2f; blendEnd = 6f;
            }
            else
            {
                sideTint = new Color(0.94f, 0.98f, 0.88f);
                blendStart = 1.5f; blendEnd = 7.5f;
            }
            foreach (var l in def.layers)
            {
                if (l == null || string.IsNullOrEmpty(l.art)) continue;
                if (l.art.Contains("hills") || l.art.Contains("village")) fields = true;
                if (l.art.Contains("forest_near")) forestBehind = true;
                if (l.art.Contains("cliff")) cliffsBehind = true;
            }

            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art)) continue;
                var pos = new Vector2(p.pos.x, p.pos.y);
                float sc = p.scale > 0f ? p.scale : 1f;
                string a = p.art;
                if (a.StartsWith("decal_path"))
                {
                    float h = ArtLibrary.Height(a, 4f) * sc, w = h * 2f;
                    paths.Add(new Rect(pos.x - w * 0.5f, pos.y - h * 0.5f, w, h));
                }
                else if (a.Contains("tree_great")) blots.Add(new Blot { p = pos + new Vector2(0f, 1f), r = 10f * sc, tint = new Color(0.66f, 0.74f, 0.78f), k = 0.75f });
                else if (a.Contains("tree_dead")) blots.Add(new Blot { p = pos, r = 2f * sc, tint = new Color(0.8f, 0.8f, 0.84f), k = 0.5f });
                else if (a.Contains("tree")) blots.Add(new Blot { p = pos + new Vector2(0f, 0.3f), r = 3.6f * sc, tint = new Color(0.68f, 0.76f, 0.78f), k = 0.7f });
                else if (a.Contains("bush")) blots.Add(new Blot { p = pos, r = 1.7f * sc, tint = new Color(0.78f, 0.84f, 0.84f), k = 0.55f });
                else if (p.collider != null && p.collider.w >= 2.5f)   // buildings, tents, carts: a little contact darkening
                    blots.Add(new Blot { p = pos + new Vector2(0f, p.collider.h * 0.3f), r = p.collider.w * 0.62f * sc, tint = new Color(0.82f, 0.8f, 0.84f), k = 0.5f });
                if (a.Contains("bridge") && !HasStream)
                {
                    HasStream = true;
                    streamX = pos.x;
                    streamY = pos.y;
                }
            }
            foreach (var t in def.transitions)
            {
                if (t == null) continue;
                if (t.pos.x <= 3.5f) corridors.Add(new Vector2(-1f, t.pos.y));
                else if (t.pos.x >= W - 3.5f) corridors.Add(new Vector2(1f, t.pos.y));
            }
        }

        // ================================================================== heights

        /// <summary>Centre of the brook at depth y.</summary>
        public float StreamX(float y)
        {
            float t = y - streamY;
            return streamX + 1.4f * Mathf.Sin(t * 0.2f) + 3.2f * Mathf.Sin(t * 0.043f);
        }

        /// <summary>Half width of the brook at depth y.</summary>
        public float StreamHalfWidth(float y)
        {
            float o = Mathf.Max(0f, Mathf.Max(-y, y - D));
            return 1.25f + 0.7f * Smooth(0f, 12f, o) - 0.5f * Smooth(40f, 110f, y - D);
        }

        /// <summary>0 inside the walkable depth range … 1 a few metres outside it (where the brook bed may be carved).</summary>
        float StreamDepthFactor(float y) => Smooth(0f, 2.5f, Mathf.Max(0f, Mathf.Max(-y, y - D)));

        /// <summary>Height (m) of the land at a ground point (0 everywhere inside the walkable rect).</summary>
        public float Height(float x, float y)
        {
            float h = BaseHeight(x, y);
            if (HasStream)
            {
                float f = StreamDepthFactor(y);
                if (f > 0f)
                {
                    float u = Mathf.Abs(x - StreamX(y)) / StreamHalfWidth(y);
                    if (u < 1.6f) h -= 0.6f * f * (1f - Smooth(0.55f, 1.5f, u));
                }
            }
            return h;
        }

        /// <summary>Height without the brook's bed.</summary>
        public float BaseHeight(float x, float y)
        {
            float ox = Mathf.Max(0f, Mathf.Max(-FlatSide - x, x - (W + FlatSide)));
            float ob = Mathf.Max(0f, y - (D + FlatBack));
            float of = Mathf.Max(0f, -FlatFront - y);
            if (ox <= 0f && ob <= 0f && of <= 0f) return 0f;

            float n1 = Mathf.PerlinNoise(x * 0.045f + s1, y * 0.045f + s2);
            float n2 = Mathf.PerlinNoise(x * 0.012f + s3, y * 0.012f + s4);
            float n3 = Mathf.PerlinNoise(x * 0.11f + s5, y * 0.11f + s6);
            float valley = 0f;
            if (HasStream)
            {
                float hw = StreamHalfWidth(y);
                valley = 1f - Smooth(hw + 1f, hw + 8f + ob * 0.25f, Mathf.Abs(x - StreamX(y)));
            }

            float h = 0f;
            if (ob > 0f)
            {
                float e = Smooth(0f, 12f, ob);
                float a = 15f * (1f - Mathf.Exp(-ob / 26f)) + 26f * Smooth(45f, 150f, ob) * (0.4f + n2);
                if (cliffsBehind) a *= 0.7f;
                h += e * a * (0.5f + 0.95f * n1) * (1f - 0.8f * valley);
            }
            if (ox > 0f)
            {
                float e = Smooth(0f, 16f, ox);
                float a = 1.0f + 7f * Smooth(12f, 70f, ox) + 14f * Smooth(60f, 140f, ox) * (0.4f + n2);
                float side = e * a * (0.45f + 1.0f * n1);
                for (int i = 0; i < corridors.Count; i++)
                {
                    var c = corridors[i];
                    if ((c.x < 0f && x < 0f) || (c.x > 0f && x > W))
                        side *= 1f - 0.85f * (1f - Smooth(2.5f, 8f, Mathf.Abs(y - c.y))) * (1f - Smooth(70f, 120f, ox));
                }
                h += side;
            }
            if (of > 0f)
            {
                float bank = -0.55f * Smooth(0f, 7f, of);
                float rolls = (n1 - 0.5f) * 1.4f * Smooth(5f, 16f, of);
                float rise = 5f * Smooth(16f, 48f, of) * (0.5f + n2);
                h += (bank + rolls + rise) * (1f - 0.6f * valley);
            }
            float o = Mathf.Max(ox, Mathf.Max(ob, of));
            h += (n3 - 0.5f) * 0.6f * Smooth(0f, 8f, o);
            return h;
        }

        /// <summary>Distance (m) of a ground point outside the walkable rect (0 inside).</summary>
        public float DistanceOutside(float x, float y)
        {
            float dx = Mathf.Max(0f, Mathf.Max(-x, x - W));
            float dy = Mathf.Max(0f, Mathf.Max(-y, y - D));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>True when nothing (ground cover, backdrop trees) should stand here: brook, transition roads.</summary>
        public bool Reserved(float x, float y, float margin)
        {
            if (HasStream && Mathf.Abs(x - StreamX(y)) < StreamHalfWidth(y) + 0.6f + margin) return true;
            for (int i = 0; i < corridors.Count; i++)
            {
                var c = corridors[i];
                bool side = c.x < 0f ? x < 1f : x > W - 1f;
                if (side && Mathf.Abs(y - c.y) < 3.2f + margin) return true;
            }
            return false;
        }

        // ================================================================== colours

        void Sample(float x, float y, float h, out Color tint, out Vector2 blendDetail)
        {
            float dOut = DistanceOutside(x, y);
            float edge = Mathf.PerlinNoise(x * 0.15f + s5, y * 0.15f + s6);
            float blend = Smooth(blendStart, blendEnd, dOut + (edge - 0.5f) * 3.2f);

            // map ground: soft swathes, warm/cool variation
            float v = Mathf.PerlinNoise(x * 0.06f + s1, y * 0.06f + s2);
            float w = Mathf.PerlinNoise(x * 0.028f + s3, y * 0.028f + s4);
            var inner = innerTint * (0.9f + 0.17f * v);
            inner = new Color(inner.r * Mathf.Lerp(0.97f, 1.04f, w), inner.g * Mathf.Lerp(1.02f, 1.0f, w), inner.b * Mathf.Lerp(0.96f, 0.92f, w));

            // surroundings: meadow swathes, sunny crests, cooler hollows, field patches far behind
            float v2 = Mathf.PerlinNoise(x * 0.05f + s4, y * 0.05f + s1);
            var side = sideTint * (0.86f + 0.24f * v2);
            float crest = Smooth(1.5f, 16f, h);
            side = new Color(side.r * (1f + 0.1f * crest), side.g * (1f + 0.07f * crest), side.b * (1f - 0.04f * crest));
            float ob = y - D;
            if (fields && ob > 22f)
            {
                float fx = Mathf.Floor((x + s2) / 23f), fy = Mathf.Floor((y + s3) / 17f);
                float pick = Mathf.PerlinNoise(fx * 0.73f + 0.31f, fy * 0.91f + 0.17f);
                Color patch = pick < 0.38f ? new Color(1.12f, 1.05f, 0.78f) : pick < 0.55f ? new Color(0.9f, 1.02f, 0.86f)
                            : pick < 0.68f ? new Color(1.06f, 0.98f, 0.86f) : Color.white;
                float k = Smooth(22f, 45f, ob) * 0.85f;
                side = new Color(side.r * Mathf.Lerp(1f, patch.r, k), side.g * Mathf.Lerp(1f, patch.g, k), side.b * Mathf.Lerp(1f, patch.b, k));
            }
            if (forestBehind && ob > 4f)
            {
                float k = Smooth(4f, 12f, ob);
                side = new Color(side.r * (1f - 0.28f * k), side.g * (1f - 0.18f * k), side.b * (1f - 0.22f * k));
            }
            var c = Color.Lerp(inner, side, blend);

            // shade under trees, contact darkening around buildings
            for (int i = 0; i < blots.Count; i++)
            {
                var b = blots[i];
                float dx = x - b.p.x, dy = (y - b.p.y) * 1.25f;
                float d2 = (dx * dx + dy * dy) / (b.r * b.r);
                if (d2 >= 1f) continue;
                float f = (1f - d2) * (1f - d2) * b.k;
                c = new Color(c.r * Mathf.Lerp(1f, b.tint.r, f), c.g * Mathf.Lerp(1f, b.tint.g, f), c.b * Mathf.Lerp(1f, b.tint.b, f));
            }
            // worn halo around painted paths
            for (int i = 0; i < paths.Count; i++)
            {
                var r = paths[i];
                float dx = Mathf.Max(0f, Mathf.Max(r.xMin - x, x - r.xMax));
                float dy = Mathf.Max(0f, Mathf.Max(r.yMin + r.height * 0.25f - y, y - (r.yMax - r.height * 0.25f)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 1.2f) continue;
                float f = (1f - d / 1.2f) * 0.5f;
                c = new Color(c.r * Mathf.Lerp(1f, 1.04f, f), c.g * Mathf.Lerp(1f, 1.0f, f), c.b * Mathf.Lerp(1f, 0.9f, f));
            }
            // roads continuing out of the map at its transitions
            for (int i = 0; i < corridors.Count; i++)
            {
                var cr = corridors[i];
                float ox = cr.x < 0f ? -x : x - W;
                if (ox < -1f) continue;
                float wob = Mathf.Sin(ox * 0.09f + s1) * 1.2f;
                float band = 1f - Smooth(0.9f, 2.0f, Mathf.Abs(y - cr.y - wob));
                float f = band * Smooth(-1f, 1.5f, ox) * (1f - Smooth(40f, 80f, ox)) * 0.85f;
                if (f <= 0f) continue;
                c = new Color(c.r * Mathf.Lerp(1f, pathTint.r, f), c.g * Mathf.Lerp(1f, pathTint.g, f), c.b * Mathf.Lerp(1f, pathTint.b, f));
            }
            // damp banks along the brook
            if (HasStream)
            {
                float u = Mathf.Abs(x - StreamX(y)) - StreamHalfWidth(y);
                if (u < 2.2f)
                {
                    float f = (1f - Smooth(-0.5f, 2.2f, u)) * 0.6f;
                    c = new Color(c.r * Mathf.Lerp(1f, 0.78f, f), c.g * Mathf.Lerp(1f, 0.88f, f), c.b * Mathf.Lerp(1f, 0.86f, f));
                }
            }

            // detail fades with distance (and on steep slopes) into the textures' average colour
            float strength = 1f - 0.55f * Smooth(30f, 110f, dOut);
            float hx = BaseHeight(x + 1f, y) - BaseHeight(x - 1f, y), hy = BaseHeight(x, y + 1f) - BaseHeight(x, y - 1f);
            float steep = Mathf.Sqrt(hx * hx + hy * hy) * 0.5f;
            strength *= Mathf.Lerp(1f, 0.5f, Smooth(0.35f, 1.1f, steep));
            tint = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 0f);
            blendDetail = new Vector2(blend, strength);
        }

        // ================================================================== mesh

        static List<float> Lines(float innerMin, float innerMax, float outerMin, float outerMax, float growth, float maxStep)
        {
            var mid = new List<float>();
            int n = Mathf.Max(1, Mathf.CeilToInt(innerMax - innerMin));
            float st = (innerMax - innerMin) / n;
            for (int i = 0; i <= n; i++) mid.Add(innerMin + i * st);
            var low = new List<float>();
            float s = 1.2f, v = innerMin;
            while (v > outerMin) { s = Mathf.Min(maxStep, s * growth); v = Mathf.Max(outerMin, v - s); low.Add(v); }
            low.Reverse();
            low.AddRange(mid);
            s = 1.2f; v = innerMax;
            while (v < outerMax) { s = Mathf.Min(maxStep, s * growth); v = Mathf.Min(outerMax, v + s); low.Add(v); }
            return low;
        }

        public void Build(Material material, float groundTile)
        {
            Material = material;
            var xs = Lines(-8f, W + 8f, -170f, W + 170f, 1.12f, 10f);
            var ys = Lines(-5f, D + 6f, -52f, D + 215f, 1.12f, 10f);
            int nx = xs.Count, ny = ys.Count;
            var pos = new Vector3[nx * ny];
            var col = new Color32[nx * ny];
            var bd = new Vector2[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], y = ys[j];
                    float h = Height(x, y);
                    int k = j * nx + i;
                    pos[k] = new Vector3(x, y, -h);
                    Sample(x, y, h, out var c, out var b);
                    col[k] = c;
                    bd[k] = b;
                }

            int chunkCols = Mathf.Max(8, Mathf.CeilToInt((nx - 1) / 6f));
            var verts = new List<Vector3>(chunkCols * ny * 6);
            var norms = new List<Vector3>(chunkCols * ny * 6);
            var cols = new List<Color32>(chunkCols * ny * 6);
            var uvs = new List<Vector2>(chunkCols * ny * 6);
            var tris = new List<int>(chunkCols * ny * 6);
            for (int c0 = 0; c0 < nx - 1; c0 += chunkCols)
            {
                int c1 = Mathf.Min(nx - 1, c0 + chunkCols);
                verts.Clear(); norms.Clear(); cols.Clear(); uvs.Clear(); tris.Clear();
                for (int j = 0; j < ny - 1; j++)
                    for (int i = c0; i < c1; i++)
                    {
                        int k00 = j * nx + i, k10 = k00 + 1, k01 = k00 + nx, k11 = k01 + 1;
                        // alternate the diagonal: a faceted, hand-cut look on the hills
                        if (((i + j) & 1) == 0)
                        {
                            Tri(pos, col, bd, k00, k01, k11, verts, norms, cols, uvs, tris);
                            Tri(pos, col, bd, k00, k11, k10, verts, norms, cols, uvs, tris);
                        }
                        else
                        {
                            Tri(pos, col, bd, k00, k01, k10, verts, norms, cols, uvs, tris);
                            Tri(pos, col, bd, k01, k11, k10, verts, norms, cols, uvs, tris);
                        }
                    }
                var m = new Mesh { name = "lv_terrain_" + c0 };
                if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(verts);
                m.SetNormals(norms);
                m.SetColors(cols);
                m.SetUVs(0, uvs);
                m.SetTriangles(tris, 0, true);
                m.UploadMeshData(true);
                owned.Add(m);
                var go = new GameObject("Terrain Chunk " + c0);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                Quiet(r);
            }
        }

        static void Tri(Vector3[] pos, Color32[] col, Vector2[] bd, int a, int b, int c,
                        List<Vector3> verts, List<Vector3> norms, List<Color32> cols, List<Vector2> uvs, List<int> tris)
        {
            var pa = pos[a]; var pb = pos[b]; var pc = pos[c];
            // vertices in grid order (x right, y into the scene) wind clockwise seen from above: the normal points up (−Z)
            var n = Vector3.Cross(pb - pa, pc - pa);
            n = n.sqrMagnitude > 1e-12f ? n.normalized : World3D.Up;
            if (n.z > 0f) n = -n;
            int i0 = verts.Count;
            verts.Add(pa); verts.Add(pb); verts.Add(pc);
            norms.Add(n); norms.Add(n); norms.Add(n);
            cols.Add(col[a]); cols.Add(col[b]); cols.Add(col[c]);
            uvs.Add(bd[a]); uvs.Add(bd[b]); uvs.Add(bd[c]);
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ================================================================== ground cover

        /// <summary>Local Y-up point (x, height, depth) of a ground point: geometry built for a World3D.Upright root.</summary>
        Vector3 Local(float x, float y, float lift = 0f) => new Vector3(x, Height(x, y) + lift, y);

        /// <summary>Grass, flowers, stones and bushes outside the walkable area, sparse trees on the hills.</summary>
        public void BuildGroundCover(bool hillTrees)
        {
            var rng = new System.Random(StableHash(def.id) * 7 + 11);
            float R() => (float)rng.NextDouble();
            float x0 = -46f, x1 = W + 46f;
            const float chunkW = 34f;
            var grass = Ui.Hex("#7fa35a");
            var grassDark = Ui.Hex("#5e8445");
            if (def.ground != null && def.ground.Contains("forest")) { grass = Ui.Hex("#5f8a4c"); grassDark = Ui.Hex("#456f3d"); }
            if (def.ground != null && def.ground.Contains("shrine")) { grass = Ui.Hex("#86a070"); grassDark = Ui.Hex("#5f7a5c"); }
            var flowers = new[] { Ui.Hex("#fff6e6"), Ui.Hex("#ffd86b"), Ui.Hex("#f2a7c3"), Ui.Hex("#b9a6f0"), Ui.Hex("#ffb38a") };

            for (float cx = x0; cx < x1; cx += chunkW)
            {
                var mb = new MeshBuilder(StableHash(def.id) + (int)cx) { Jitter = 0.06f };
                float cxe = Mathf.Min(x1, cx + chunkW);
                // density: lush along the front bank (closest to the camera), lighter elsewhere
                for (float gx = cx; gx < cxe; gx += 1.4f)
                    for (float gy = -24f; gy < D + 26f; gy += 1.4f)
                    {
                        float x = gx + R() * 1.4f, y = gy + R() * 1.4f;
                        float dOut = DistanceOutside(x, y);
                        if (dOut < 0.9f) continue;
                        if (Reserved(x, y, 0.2f)) continue;
                        float density = y < 0f ? Mathf.Lerp(0.55f, 0.18f, Smooth(2f, 22f, dOut)) : Mathf.Lerp(0.32f, 0.08f, Smooth(2f, 20f, dOut));
                        float clump = Mathf.PerlinNoise(x * 0.21f + s2, y * 0.21f + s5);
                        density *= 0.4f + 1.2f * clump;
                        float roll = R();
                        if (roll > density) continue;
                        float pick = R();
                        if (pick < 0.68f) Tuft(mb, rng, Local(x, y), Mathf.Lerp(0.32f, 0.72f, R()), grass, grassDark, null);
                        else if (pick < 0.86f) Tuft(mb, rng, Local(x, y), Mathf.Lerp(0.3f, 0.55f, R()), grass, grassDark, flowers);
                        else if (pick < 0.95f) Stone(mb, rng, Local(x, y, -0.04f), Mathf.Lerp(0.14f, 0.42f, R()));
                        else if (dOut > 3f) Bush(mb, rng, Local(x, y), Mathf.Lerp(0.5f, 0.95f, R()), grassDark);
                    }
                Emit(mb, "Ground Cover " + cx);
            }

            if (!hillTrees) return;
            // sparse clumps of trees on the hills (behind and to the sides), never on the walkable strip's doorstep
            var trees = new MeshBuilder(StableHash(def.id) + 99) { Jitter = 0.07f };
            var leaf = new[] { Ui.Hex("#6f9a52"), Ui.Hex("#5d8c4c"), Ui.Hex("#83a85a"), Ui.Hex("#4f7b4a") };
            int count = 0;
            for (float gx = -120f; gx < W + 120f; gx += 7f)
                for (float gy = -40f; gy < D + 150f; gy += 7f)
                {
                    float x = gx + R() * 7f, y = gy + R() * 7f;
                    float dOut = DistanceOutside(x, y);
                    if (dOut < (y > D ? 6.5f : 11f)) continue;   // a few close behind the strip, the sides stay open
                    if (y < -8f && dOut < 26f) continue;         // keep the camera's side open
                    if (Reserved(x, y, 3f)) continue;
                    float clump = Mathf.PerlinNoise(x * 0.035f + s6, y * 0.035f + s3);
                    if (clump < 0.52f || R() > (clump - 0.45f) * 1.6f) continue;
                    float size = Mathf.Lerp(0.7f, 1.25f, R()) * Mathf.Lerp(1f, 1.5f, Smooth(30f, 120f, dOut));
                    if (R() < 0.3f) Pine(trees, rng, Local(x, y, -0.2f), size * 7f, Paint.Shade(leaf[3], 0.9f));
                    else RoundTree(trees, rng, Local(x, y, -0.2f), size * 5.5f, leaf[rng.Next(leaf.Length)]);
                    if (++count > 260) break;
                }
            Emit(trees, "Hill Trees");
        }

        void Emit(MeshBuilder mb, string name)
        {
            if (mb.IsEmpty) return;
            var m = mb.ToMesh("lv_" + name.Replace(' ', '_').ToLowerInvariant());
            owned.Add(m);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localRotation = World3D.Upright;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Materials3D.LowPoly;
            Quiet(r);
        }

        internal static void Tuft(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color col, Color dark, Color[] flowers)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0.9f; mb.WindGradient = true; mb.WindY0 = b.y; mb.WindY1 = b.y + height;
            int blades = 5 + rng.Next(4);
            for (int i = 0; i < blades; i++)
            {
                float a = R() * Mathf.PI * 2f;
                float lean = 0.15f + R() * 0.3f;
                float hh = height * (0.65f + 0.35f * R());
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var basePt = b + dir * (0.04f + 0.06f * R());
                var tip = basePt + dir * (lean * hh) + Vector3.up * hh;
                mb.Color = Color.Lerp(dark, col, R());
                mb.Blade(basePt, tip, 0.07f + 0.05f * R(), new Vector3(-dir.z, 0f, dir.x));
            }
            if (flowers != null)
            {
                int heads = 2 + rng.Next(3);
                var fc = flowers[rng.Next(flowers.Length)];
                for (int i = 0; i < heads; i++)
                {
                    float a = R() * Mathf.PI * 2f;
                    var p = b + new Vector3(Mathf.Cos(a) * 0.12f, height * (0.7f + 0.3f * R()), Mathf.Sin(a) * 0.12f);
                    mb.Color = Paint.Shade(fc, 0.92f + 0.12f * R());
                    mb.Blob(p, new Vector3(0.055f, 0.04f, 0.055f), 0, 0.1f, rng.Next(1000));
                }
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        internal static void Stone(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            var grey = Color.Lerp(new Color(0.6f, 0.59f, 0.57f), new Color(0.55f, 0.58f, 0.5f), R());
            mb.Color = Paint.Shade(grey, 0.9f + 0.2f * R());
            mb.Wind = 0f;
            mb.Blob(b + new Vector3(0f, size * 0.32f, 0f), new Vector3(size, size * 0.62f, size * (0.7f + 0.3f * R())), 0, 0.22f, rng.Next(1000), 0.55f);
        }

        internal static void Bush(MeshBuilder mb, System.Random rng, Vector3 b, float size, Color col)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0.18f; mb.WindGradient = true; mb.WindY0 = b.y; mb.WindY1 = b.y + size * 1.4f;
            int n = 2 + rng.Next(2);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3((R() - 0.5f) * size * 1.1f, size * (0.55f + 0.3f * R()), (R() - 0.5f) * size * 0.8f);
                mb.Color = Paint.Shade(col, 0.85f + 0.3f * R());
                mb.Blob(b + off, new Vector3(size, size * 0.8f, size) * (0.75f + 0.3f * R()), 1, 0.16f, rng.Next(1000), 0.3f);
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        internal static void RoundTree(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color leaf)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Ui.Hex("#6b5040");
            float trunkH = height * 0.42f;
            mb.Cylinder(b, height * 0.045f, height * 0.03f, trunkH + 0.4f, 6);
            mb.Wind = 0.25f; mb.WindGradient = true; mb.WindY0 = b.y + trunkH; mb.WindY1 = b.y + height;
            float cr = height * 0.36f;
            mb.Color = Paint.Shade(leaf, 0.92f + 0.14f * R());
            mb.Blob(b + new Vector3(0f, trunkH + cr * 0.75f, 0f), new Vector3(cr, cr * 0.9f, cr), 1, 0.14f, rng.Next(1000), 0.25f);
            if (R() < 0.6f)
            {
                mb.Color = Paint.Shade(leaf, 1.05f + 0.1f * R());
                float s = cr * (0.55f + 0.2f * R());
                mb.Blob(b + new Vector3((R() - 0.5f) * cr, trunkH + cr * 1.35f, (R() - 0.5f) * cr * 0.6f), new Vector3(s, s * 0.85f, s), 0, 0.14f, rng.Next(1000));
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        internal static void Pine(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color leaf)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Ui.Hex("#5e4636");
            mb.Cylinder(b, height * 0.04f, height * 0.025f, height * 0.3f, 6);
            mb.Wind = 0.2f; mb.WindGradient = true; mb.WindY0 = b.y + height * 0.2f; mb.WindY1 = b.y + height;
            float r = height * 0.24f;
            for (int i = 0; i < 3; i++)
            {
                float y0 = height * (0.2f + i * 0.22f);
                float rr = r * (1f - i * 0.24f);
                mb.Color = Paint.Shade(leaf, 0.9f + 0.08f * i + 0.08f * R());
                mb.Cylinder(b + new Vector3(0f, y0, 0f), rr, 0f, height * (0.42f - i * 0.04f), 7, false, true, false);
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        // ================================================================== brook

        /// <summary>The brook (maps with a bridge): a translucent, gently flowing ribbon lying in its carved bed.</summary>
        public void BuildStream(Color horizon)
        {
            if (!HasStream) return;
            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var uvs = new List<Vector2>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float[] us = { -1f, -0.55f, 0f, 0.55f, 1f };
            float[] alphas = { 0f, 0.62f, 0.7f, 0.62f, 0f };
            var deep = Color.Lerp(new Color(0.34f, 0.55f, 0.6f), horizon, 0.18f);
            var shallow = Color.Lerp(new Color(0.55f, 0.72f, 0.7f), horizon, 0.18f);
            float y = -52f, along = 0f;
            int rows = 0;
            while (y <= D + 120f)
            {
                float sx = StreamX(y), hw = StreamHalfWidth(y);
                float f = StreamDepthFactor(y);
                float hc = BaseHeight(sx, y);
                float wh = hc - 0.12f * f + 0.012f * (1f - f);
                float fade = (1f - Smooth(D + 50f, D + 115f, y)) * Smooth(-52f, -40f, y);
                for (int k = 0; k < us.Length; k++)
                {
                    verts.Add(new Vector3(sx + us[k] * hw, y, -wh));
                    norms.Add(World3D.Up);
                    var c = Color.Lerp(shallow, deep, 1f - Mathf.Abs(us[k]));
                    c.a = alphas[k] * fade;
                    cols.Add(c);
                    uvs.Add(new Vector2(us[k] * hw / 3f + 0.5f, along / 3f));
                }
                rows++;
                float step = y > -6f && y < D + 6f ? 0.8f : 2f;
                float nextY = y + step;
                along += Vector2.Distance(new Vector2(sx, y), new Vector2(StreamX(nextY), nextY));
                y = nextY;
            }
            int per = us.Length;
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < per - 1; k++)
                {
                    int a = r * per + k, b = a + 1, c = a + per, d = c + 1;
                    // x right, y into the scene: (a, c, d) and (a, d, b) wind clockwise seen from above
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b);
                }
            var m = new Mesh { name = "lv_brook" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetColors(cols);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, true);
            owned.Add(m);
            var go = new GameObject("Brook");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var rr = go.AddComponent<MeshRenderer>();
            waterMat = Materials3D.LitTransparent(WorldTextures.Water);
            waterMat.SetFloat(Materials3D.EmissionId, 0.12f);
            rr.sharedMaterial = waterMat;
            rr.sortingOrder = -12;   // under the painted decals (MapView: −8 … −5)
            Quiet(rr);
        }

        /// <summary>Flows the brook's ripples (one material property per frame).</summary>
        public void Update(float dt)
        {
            if (waterMat == null) return;
            waterScroll = Mathf.Repeat(waterScroll - dt * 0.12f, 1f);
            waterMat.mainTextureOffset = new Vector2(Mathf.Sin(Time.time * 0.3f) * 0.02f, waterScroll);
        }
    }
}
