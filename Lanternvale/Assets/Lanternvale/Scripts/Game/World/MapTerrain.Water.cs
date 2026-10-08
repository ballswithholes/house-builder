// MapTerrain: MapDef.water — rivers (polylines with a half width) and ponds (closed polygons) with fords and bridges.
//
// The nav grid blocks every point within halfWidth of a river's polyline and the inside of a pond's shore (crossing
// rects stay walkable). MapTerrain draws them through the same points, smoothed (Catmull-Rom), so the banks curve
// softly and never stray more than a hand's breadth from the blocked cells on gentle bends; a pond's shore is the very
// loop the nav grid blocks (Lanternvale.Util.Spline.PondShore). MapTerrain
// carves a bed there (0.45 m; 0.12 m for water that doesn't block), lays a translucent, flowing surface just under the
// ground (WaterLevel), darkens the damp banks and keeps ground cover and trees off them. A river that meets the map's
// edge flows on out of it, through a valley in the hills, to the horizon. A crossing with a bridge prop over it keeps
// the deep bed (the bridge spans it); any other crossing is a ford: the bed rises to a gravel bar just under the
// surface, stepping stones break it and a data path fades out into the shallows on either bank.
//
// The fen's surroundings (BiomeStyle.WaterTable) also get a still water table just under the ground: pools wherever its
// hummocky land dips below it.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    internal sealed partial class MapTerrain
    {
        /// <summary>The water surface (m above the ground plane) on the map; the fen's pools around it.</summary>
        public const float WaterLevel = -0.1f, FenWaterLevel = -0.14f;
        const float RiverDepth = 0.45f, ShallowDepth = 0.12f;

        sealed class WaterBody
        {
            public bool closed, blocks;
            public float hw;
            public List<Vector2> pts;                                // river: the polyline (extended out of the map); pond: polygon
            public float[] along;                                     // river: arc length at each point
            public readonly List<Rect> fords = new List<Rect>(), bridges = new List<Rect>();
            public float xMin, xMax, yMin, yMax;                      // bounds of the water itself
            public int extendStart, extendEnd;                        // river: points added beyond the data's ends
            public readonly List<Rect> segBlocks = new List<Rect>();  // bounds of every SegBlock segments (BodyEdge skips far ones)
        }

        const int SegBlock = 12;

        readonly List<WaterBody> waters = new List<WaterBody>();
        Material riverMat, stillMat;
        float riverScroll;

        void AnalyseWater()
        {
            if (def.water == null) return;
            foreach (var wd in def.water)
            {
                if (wd == null || wd.points == null) continue;
                var b = new WaterBody { closed = wd.closed, blocks = wd.blocksMovement, hw = Mathf.Max(0.3f, wd.halfWidth) };
                b.pts = new List<Vector2>();
                foreach (var p in wd.points) b.pts.Add(new Vector2(p.x, p.y));
                if (b.closed ? b.pts.Count < 3 : b.pts.Count < 2) continue;
                if (!b.closed)
                {
                    // a river meeting the edge flows on out of the map, to the horizon
                    Vector2 first = b.pts[0], last = b.pts[b.pts.Count - 1];
                    ExtendRiver(b.pts, true);
                    ExtendRiver(b.pts, false);
                    // its banks curve softly through the data's points (Catmull-Rom; the nav grid blocks the polyline
                    // itself, which the curve never leaves by more than a hand's breadth on gentle bends)
                    b.pts = Decimate(CatmullRom(b.pts, 0.5f));
                    b.extendStart = Nearest(b.pts, first);
                    b.extendEnd = b.pts.Count - 1 - Nearest(b.pts, last);
                    b.along = new float[b.pts.Count];
                    for (int i = 1; i < b.pts.Count; i++) b.along[i] = b.along[i - 1] + Vector2.Distance(b.pts[i - 1], b.pts[i]);
                }
                else b.pts = PondShore(wd.points);
                float pad = b.closed ? 0f : b.hw;
                b.xMin = b.yMin = float.MaxValue;
                b.xMax = b.yMax = float.MinValue;
                foreach (var p in b.pts)
                {
                    b.xMin = Mathf.Min(b.xMin, p.x - pad); b.xMax = Mathf.Max(b.xMax, p.x + pad);
                    b.yMin = Mathf.Min(b.yMin, p.y - pad); b.yMax = Mathf.Max(b.yMax, p.y + pad);
                }
                int segs = b.closed ? b.pts.Count : b.pts.Count - 1;
                for (int s0 = 0; s0 < segs; s0 += SegBlock)
                {
                    float xa = float.MaxValue, xb = float.MinValue, ya = float.MaxValue, yb = float.MinValue;
                    for (int i = s0; i <= Mathf.Min(segs, s0 + SegBlock); i++)
                    {
                        var p = b.pts[i % b.pts.Count];
                        xa = Mathf.Min(xa, p.x); xb = Mathf.Max(xb, p.x); ya = Mathf.Min(ya, p.y); yb = Mathf.Max(yb, p.y);
                    }
                    b.segBlocks.Add(Rect.MinMaxRect(xa, ya, xb, yb));
                }
                if (wd.crossings != null)
                    foreach (var c in wd.crossings)
                    {
                        if (c == null) continue;
                        var r = new Rect(c.pos.x - c.size.x * 0.5f, c.pos.y - c.size.y * 0.5f, c.size.x, c.size.y);
                        bool bridged = false;
                        foreach (var p in def.props)
                            if (p != null && p.art != null && p.art.Contains("bridge") &&
                                p.pos.x > r.xMin - 1.5f && p.pos.x < r.xMax + 1.5f && p.pos.y > r.yMin - 1.5f && p.pos.y < r.yMax + 1.5f)
                                bridged = true;
                        (bridged ? b.bridges : b.fords).Add(r);
                    }
                waters.Add(b);
            }
        }

        /// <summary>A river end within 3 m of the edge runs on outwards (and keeps going far beyond the map).</summary>
        void ExtendRiver(List<Vector2> pts, bool start)
        {
            var e = start ? pts[0] : pts[pts.Count - 1];
            var q = start ? pts[1] : pts[pts.Count - 2];
            float dl = e.x, dr = W - e.x, df = e.y, db = D - e.y;
            float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(df, db));
            if (m > 3f) return;
            Vector2 outward = m == dl ? Vector2.left : m == dr ? Vector2.right : m == df ? Vector2.down : Vector2.up;
            // leave along the river's own direction, turning to head straight out
            var dir = (e - q).sqrMagnitude > 1e-4f ? (e - q).normalized : outward;
            if (Vector2.Dot(dir, outward) < 0.2f) dir = outward;
            var a = e + dir * 6f;
            var b2 = a + Vector2.Lerp(dir, outward, 0.6f).normalized * 30f;
            var c = b2 + outward * 60f + new Vector2(outward.y, outward.x) * 12f * Mathf.Sin(e.x * 0.3f + e.y);
            var d = c + outward * 90f;
            var add = new[] { a, b2, c, d };
            if (start) { for (int i = 0; i < add.Length; i++) pts.Insert(0, add[i]); }
            else pts.AddRange(add);
        }

        /// <summary>Thins a dense curve far outside the map (one point every ~4 m there), keeping it fine near the map.</summary>
        List<Vector2> Decimate(List<Vector2> pts)
        {
            var o = new List<Vector2>(pts.Count);
            float acc = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (i > 0) acc += Vector2.Distance(pts[i - 1], p);
                bool near = p.x > -14f && p.x < W + 14f && p.y > -14f && p.y < D + 14f;
                if (i == 0 || i == pts.Count - 1 || near || acc >= 4f) { o.Add(p); acc = 0f; }
            }
            return o;
        }

        static int Nearest(List<Vector2> pts, Vector2 q)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = (pts[i] - q).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>A pond's soft shore: the closed Catmull-Rom loop through its corners that the nav grid blocks too.</summary>
        static List<Vector2> PondShore(List<Lanternvale.Util.Vec2> corners)
        {
            var ring = Lanternvale.Util.Spline.PondShore(corners);
            var o = new List<Vector2>(ring.Count);
            foreach (var q in ring) o.Add(new Vector2(q.x, q.y));
            return o;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Signed distance (m) from the nearest water's shore: negative in the water.</summary>
        float WaterEdge(float x, float y) => WaterEdge(x, y, out _);

        float WaterEdge(float x, float y, out WaterBody nearest)
        {
            float best = float.MaxValue;
            nearest = null;
            const float reach = 12f;
            for (int i = 0; i < waters.Count; i++)
            {
                var b = waters[i];
                if (x < b.xMin - reach || x > b.xMax + reach || y < b.yMin - reach || y > b.yMax + reach) continue;
                float e = BodyEdge(b, x, y);
                if (e < best) { best = e; nearest = b; }
            }
            return best;
        }

        float BodyEdge(WaterBody b, float x, float y)
        {
            var q = new Vector2(x, y);
            int n = b.pts.Count;
            bool inside = false;
            if (b.closed)
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    var pi = b.pts[i];
                    var pj = b.pts[j];
                    if ((pi.y > y) != (pj.y > y) && x < (pj.x - pi.x) * (y - pi.y) / (pj.y - pi.y) + pi.x) inside = !inside;
                }
            // the nearest segment, skipping blocks of segments that can't beat the best so far
            float best = float.MaxValue;
            int segs = b.closed ? n : n - 1;
            for (int k = 0; k < b.segBlocks.Count; k++)
            {
                var r = b.segBlocks[k];
                float dx = Mathf.Max(0f, Mathf.Max(r.xMin - x, x - r.xMax)), dy = Mathf.Max(0f, Mathf.Max(r.yMin - y, y - r.yMax));
                if (dx * dx + dy * dy >= best * best) continue;
                int i1 = Mathf.Min(segs, (k + 1) * SegBlock);
                for (int i = k * SegBlock; i < i1; i++) best = Mathf.Min(best, SegmentDistance(q, b.pts[i], b.pts[(i + 1) % n]));
            }
            if (b.closed) return inside ? -best : best;
            return best - b.hw;
        }

        /// <summary>0..1: inside one of the body's fords (soft over 0.6 m).</summary>
        static float FordFactor(WaterBody b, float x, float y)
        {
            float f = 0f;
            for (int i = 0; i < b.fords.Count; i++)
            {
                var r = b.fords[i];
                float dx = Mathf.Max(r.xMin - x, x - r.xMax), dy = Mathf.Max(r.yMin - y, y - r.yMax);
                f = Mathf.Max(f, 1f - Smooth(-0.1f, 0.6f, Mathf.Max(dx, dy)));
            }
            return f;
        }

        /// <summary>How deep (m) the water's bed is carved into the land here.</summary>
        float WaterCarve(float x, float y)
        {
            float carve = 0f;
            for (int i = 0; i < waters.Count; i++)
            {
                var b = waters[i];
                if (x < b.xMin - 1f || x > b.xMax + 1f || y < b.yMin - 1f || y > b.yMax + 1f) continue;
                float e = BodyEdge(b, x, y) + Wobble(x, y, b.hw);
                if (e >= 0f) continue;
                float depth = b.blocks ? RiverDepth : ShallowDepth;
                // the bed shelves from the shore down to full depth
                float k = Smooth(0f, Mathf.Clamp(b.hw * 0.75f, 0.4f, 1.3f), -e);
                float c = depth * k;
                // a ford: the bed rises to a gravel bar a hand's breadth under the surface (the level stays put, so the
                // land never pokes through the water)
                if (b.fords.Count > 0) c = Mathf.Lerp(c, Mathf.Min(c, -WaterLevel + 0.07f), FordFactor(b, x, y));
                carve = Mathf.Max(carve, c);
            }
            return carve;
        }

        /// <summary>The shore's gentle irregularity (a few centimetres, never far from the data's line).</summary>
        float Wobble(float x, float y, float hw) => (Mathf.PerlinNoise(x * 0.42f + s5, y * 0.42f + s3) - 0.5f) * Mathf.Min(0.25f, hw * 0.12f);

        /// <summary>0..1: how much the hills open into a valley for the water (rivers leaving the map).</summary>
        float WaterValley(float x, float y, float o)
        {
            float v = 0f;
            for (int i = 0; i < waters.Count; i++)
            {
                var b = waters[i];
                float reach = 10f + o * 0.25f;
                if (x < b.xMin - reach || x > b.xMax + reach || y < b.yMin - reach || y > b.yMax + reach) continue;
                float e = BodyEdge(b, x, y);
                v = Mathf.Max(v, 1f - Smooth(1f, 8f + o * 0.25f, e));
            }
            return v;
        }

        /// <summary>
        /// Damp, a little darker banks along the shore; under the water a clean, sunlit bed of sand and pebbles (the
        /// ground's own texture mostly washed out), so the water reads light and clear like the brook, not as a dark ditch.
        /// </summary>
        Color WaterBankTint(float x, float y, Color c, ref float strength)
        {
            float e = WaterEdge(x, y, out var b);
            if (b == null || e > 2.4f) return c;
            float f = (1f - Smooth(-0.3f, 2.4f, e)) * 0.45f;
            c = new Color(c.r * Mathf.Lerp(1f, 0.8f, f), c.g * Mathf.Lerp(1f, 0.88f, f), c.b * Mathf.Lerp(1f, 0.84f, f));
            if (e < 0.15f)
            {
                float deep = Smooth(-0.05f, 1.1f, -e) * (b.fords.Count > 0 ? 1f - FordFactor(b, x, y) * 0.6f : 1f);
                // the bed: pale sand at the shelving edge, a cool green-blue further in (both as tints of the ground)
                Color shore = new Color(1.02f, 0.94f, 0.78f), mid = new Color(0.66f, 0.84f, 0.8f);
                if (style.Id == Biomes.Fen) { shore = new Color(0.86f, 0.84f, 0.7f); mid = new Color(0.56f, 0.64f, 0.56f); }        // peat
                else if (style.Id == Biomes.Peaks || style.Id == Biomes.IceCave) { shore = new Color(0.94f, 0.98f, 1f); mid = new Color(0.68f, 0.84f, 0.96f); }
                else if (walled) { shore = new Color(0.8f, 0.78f, 0.72f); mid = new Color(0.46f, 0.6f, 0.62f); }
                var bed = Color.Lerp(shore, mid, Smooth(0.2f, 1f, deep));
                c = Color.Lerp(c, bed, Smooth(0f, 0.35f, deep));
                strength *= 1f - 0.65f * Smooth(0f, 0.6f, deep);
            }
            return c;
        }

        // ------------------------------------------------------------------ surfaces

        /// <summary>Rivers, ponds, fords (MapDef.water) and the fen's pools: translucent water surfaces.</summary>
        void BuildWaters(Color horizon)
        {
            // clear, sunlit water like the brook's: light teal over its pale bed
            var deep = Color.Lerp(new Color(0.36f, 0.6f, 0.66f), horizon, 0.18f);
            var shallow = Color.Lerp(new Color(0.6f, 0.8f, 0.78f), horizon, 0.18f);
            if (style.Id == Biomes.Fen)
            {
                // tea-dark peat water
                deep = Color.Lerp(new Color(0.2f, 0.28f, 0.27f), horizon, 0.12f);
                shallow = Color.Lerp(new Color(0.38f, 0.47f, 0.41f), horizon, 0.12f);
            }
            else if (style.Id == Biomes.Peaks || style.Id == Biomes.IceCave)
            {
                deep = Color.Lerp(new Color(0.36f, 0.56f, 0.68f), horizon, 0.2f);
                shallow = Color.Lerp(new Color(0.66f, 0.82f, 0.88f), horizon, 0.2f);
            }
            else if (walled)
            {
                deep = new Color(0.16f, 0.24f, 0.26f);
                shallow = new Color(0.32f, 0.42f, 0.42f);
            }
            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var sverts = new List<Vector3>();
            var scols = new List<Color32>();
            var suvs = new List<Vector2>();
            var stris = new List<int>();
            foreach (var b in waters)
            {
                if (b.closed) AddPond(b, deep, shallow, sverts, scols, suvs, stris);
                else AddRiver(b, deep, shallow, verts, cols, uvs, tris);
            }
            if (style.WaterTable) AddWaterTable(deep, shallow, sverts, scols, suvs, stris);
            if (verts.Count > 0)
            {
                riverMat = Materials3D.LitTransparent(WorldTextures.Water);
                riverMat.SetFloat(Materials3D.EmissionId, 0.12f);
                EmitWater("Rivers", verts, cols, uvs, tris, riverMat);
            }
            if (sverts.Count > 0)
            {
                stillMat = Materials3D.LitTransparent(WorldTextures.Water);
                stillMat.SetFloat(Materials3D.EmissionId, 0.05f);
                EmitWater("Ponds", sverts, scols, suvs, stris, stillMat);
            }
            BuildFordStones();
        }

        void EmitWater(string name, List<Vector3> verts, List<Color32> cols, List<Vector2> uvs, List<int> tris, Material mat)
        {
            var norms = new List<Vector3>(verts.Count);
            for (int i = 0; i < verts.Count; i++) norms.Add(World3D.Up);
            var m = new Mesh { name = "lv_water_" + name.ToLowerInvariant() };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetColors(cols);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, true);
            owned.Add(m);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.sortingOrder = -12;   // under the painted decals (MapView: −8 … −5)
            Quiet(r);
        }

        /// <summary>The water level at a point of a body: just under the ground.</summary>
        float LevelAt(WaterBody b, Vector2 p)
        {
            float land = BaseHeight(p.x, p.y);
            return land + WaterLevel;
        }

        void AddRiver(WaterBody b, Color deep, Color shallow, List<Vector3> verts, List<Color32> cols, List<Vector2> uvs, List<int> tris)
        {
            float[] us = { -1.3f, -1f, -0.62f, 0f, 0.62f, 1f, 1.3f };
            float[] alphas = { 0.5f, 0.62f, 0.7f, 0.74f, 0.7f, 0.62f, 0.5f };
            float L = b.along[b.along.Length - 1];
            // where the data starts and ends along the line (beyond them: the river out of the map, fading far away)
            float s0 = b.along[b.extendStart], s1 = b.along[b.pts.Count - 1 - b.extendEnd];
            int rows = 0, start = verts.Count;
            float s = 0f;
            int seg = 0;
            while (true)
            {
                while (seg < b.pts.Count - 2 && b.along[seg + 1] < s) seg++;
                float sl = Mathf.Max(1e-5f, b.along[seg + 1] - b.along[seg]);
                var p = Vector2.Lerp(b.pts[seg], b.pts[seg + 1], (s - b.along[seg]) / sl);
                // the tangent, smoothed over the joints so the ribbon turns softly
                var t0 = b.pts[seg + 1] - b.pts[seg];
                var tan = t0.normalized;
                float into = (s - b.along[seg]) / sl;
                if (into < 0.3f && seg > 0) tan = Vector2.Lerp((b.pts[seg] - b.pts[seg - 1]).normalized, tan, 0.5f + into / 0.6f).normalized;
                else if (into > 0.7f && seg + 2 < b.pts.Count) tan = Vector2.Lerp(tan, (b.pts[seg + 2] - b.pts[seg + 1]).normalized, (into - 0.7f) / 0.6f).normalized;
                // across the flow, to its right: the rows then wind like the brook's (clockwise seen from above), so the
                // surface faces up (the transparent shader lights back faces from below)
                var nrm = new Vector2(tan.y, -tan.x);
                float outside = Mathf.Max(s0 - s, s - s1);
                float fade = 1f - Smooth(60f, 140f, outside);
                float hw = b.hw * (1f + 0.35f * Smooth(10f, 60f, outside));
                for (int k = 0; k < us.Length; k++)
                {
                    var q = p + nrm * (us[k] * hw);
                    float lv = LevelAt(b, q);
                    verts.Add(new Vector3(q.x, q.y, -lv));
                    var c = Color.Lerp(shallow, deep, 1f - Mathf.Abs(us[k]) / 1.3f);
                    // a lighter lip at the shore
                    if (Mathf.Abs(us[k]) >= 1f) c = Color.Lerp(c, Color.white, 0.12f);
                    c.a = alphas[k] * fade;
                    cols.Add(c);
                    uvs.Add(new Vector2(us[k] * hw / 3f + 0.5f, s / 3f));
                }
                rows++;
                if (s >= L) break;
                bool near = p.x > -6f && p.x < W + 6f && p.y > -6f && p.y < D + 6f;
                s = Mathf.Min(L, s + (near ? 0.6f : 2.5f));
            }
            int per = us.Length;
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < per - 1; k++)
                {
                    int a = start + r * per + k, b2 = a + 1, c = a + per, d = c + 1;
                    // consistent winding seen from above (both sides drawn by the transparent material's culling off)
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b2);
                }
        }

        void AddPond(WaterBody b, Color deep, Color shallow, List<Vector3> verts, List<Color32> cols, List<Vector2> uvs, List<int> tris)
        {
            const float cell = 0.5f;
            float x0 = b.xMin - 0.6f, y0 = b.yMin - 0.6f;
            int nx = Mathf.CeilToInt((b.xMax + 0.6f - x0) / cell), ny = Mathf.CeilToInt((b.yMax + 0.6f - y0) / cell);
            var idx = new int[(nx + 1) * (ny + 1)];
            var edge = new float[(nx + 1) * (ny + 1)];
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * cell, y = y0 + j * cell;
                    edge[j * (nx + 1) + i] = BodyEdge(b, x, y) + Wobble(x, y, 2f);
                    idx[j * (nx + 1) + i] = -1;
                }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k00 = j * (nx + 1) + i, k10 = k00 + 1, k01 = k00 + nx + 1, k11 = k01 + 1;
                    if (Mathf.Min(Mathf.Min(edge[k00], edge[k10]), Mathf.Min(edge[k01], edge[k11])) > 0.35f) continue;
                    int a = Vert(k00, i, j), b10 = Vert(k10, i + 1, j), c = Vert(k01, i, j + 1), d = Vert(k11, i + 1, j + 1);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b10);
                }

            int Vert(int k, int i, int j)
            {
                if (idx[k] >= 0) return idx[k];
                float x = x0 + i * cell, y = y0 + j * cell;
                float inner = -edge[k];
                verts.Add(new Vector3(x, y, -(BaseHeight(x, y) + WaterLevel)));
                var col = Color.Lerp(shallow, deep, Smooth(0f, 2.2f, inner));
                if (inner < 0.15f) col = Color.Lerp(col, Color.white, 0.12f);
                col.a = Mathf.Lerp(0.45f, 0.62f, Smooth(0f, 1.5f, inner));
                cols.Add(col);
                uvs.Add(new Vector2(x / 4.5f, y / 4.5f));
                idx[k] = verts.Count - 1;
                return idx[k];
            }
        }

        /// <summary>The fen's still water table under its hummocky surroundings (the ground hides it everywhere else).</summary>
        void AddWaterTable(Color deep, Color shallow, List<Vector3> verts, List<Color32> cols, List<Vector2> uvs, List<int> tris)
        {
            var xs = Lines(-6f, W + 6f, -150f, W + 150f, 1.15f, 8f, 2f);
            var ys = Lines(-6f, D + 6f, -50f, D + 170f, 1.15f, 8f, 2f);
            int nx = xs.Count, ny = ys.Count, b0 = verts.Count;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], y = ys[j];
                    verts.Add(new Vector3(x, y, -FenWaterLevel));
                    // deeper (darker) where the land lies further under it
                    float under = FenWaterLevel - BaseHeight(x, y);
                    var c = Color.Lerp(shallow, deep, Smooth(0f, 0.4f, under));
                    float far = DistanceOutside(x, y);
                    c.a = 0.78f * (1f - Smooth(90f, 150f, far));
                    cols.Add(c);
                    uvs.Add(new Vector2(x / 3f, y / 3f));
                }
            for (int j = 0; j < ny - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = b0 + j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b);
                }
        }

        /// <summary>Flat stepping stones across each ford, just breaking the shallow water's surface.</summary>
        void BuildFordStones()
        {
            var mb = new MeshBuilder(StableHash(def.id) + 404) { Jitter = 0.04f };
            var rng = new System.Random(StableHash(def.id) + 405);
            float R() => (float)rng.NextDouble();
            foreach (var b in waters)
                foreach (var r in b.fords)
                {
                    bool alongY = r.height >= r.width;
                    float len = alongY ? r.height : r.width, wid = alongY ? r.width : r.height;
                    int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.62f));
                    for (int i = 0; i < n; i++)
                        for (int lane = -1; lane <= 1; lane += 2)
                        {
                            if (wid < 1.6f && lane > 0) continue;
                            float t = (i + 0.5f) / n;
                            float off = (wid < 1.6f ? 0f : lane * wid * 0.22f) + (R() - 0.5f) * 0.18f + (i % 2 == 0 ? 0.12f : -0.12f);
                            float x = alongY ? r.center.x + off : r.xMin + t * len;
                            float y = alongY ? r.yMin + t * len : r.center.y + off;
                            if (BodyEdge(b, x, y) > 0.1f) continue;   // only in the water
                            float sz = Mathf.Lerp(0.26f, 0.38f, R());
                            var grey = Color.Lerp(new Color(0.62f, 0.6f, 0.56f), new Color(0.52f, 0.56f, 0.5f), R());
                            mb.Color = Paint.Shade(grey, 0.92f + 0.16f * R());
                            mb.Blob(new Vector3(x, Height(x, y) + 0.04f, y), new Vector3(sz, 0.1f, sz * (0.75f + 0.25f * R())), 0, 0.2f, rng.Next(1000), 0.5f);
                        }
                }
            Emit(mb, "Ford Stones", true);
        }
    }
}
