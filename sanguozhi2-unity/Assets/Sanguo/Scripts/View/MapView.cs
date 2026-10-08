using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    // 战略地图：程序化生成的低多边形地形（海岸线、河流、湖泊、山脉、地形块、水面、树木、道路、海上航线、
    // 城池与旗帜、势力光圈、云、选择光圈、点选、行军动画）。← 网页版 js/map-view.js（第二版 §4G）
    // 地理计算（投影、距离场、生物群落、高度、地表颜色、树木分布、航线）在 View/WorldGeo.cs（无头可测）。
    //
    // 公开接口
    //   Build(g)                          同步构建（区域 "auto"：所有城池都在旧版中国地图内时为 "china"，否则 "world"）
    //   Build(g, region)                  同上，region = "auto" | "china" | "world"
    //   BuildAsync(g, region, progress)   协程：同样的构建，切成约 SliceMs（默认 8ms）的分片，progress(0..1, 阶段名) 报告进度
    //   Refresh(g) Select(cityId) Pick(cam, screen) → cityId | -1   March(from, to, color, seconds)
    //   Height(x, z) SurfaceY(x, z) IsForest(x, z, h) LinkIsSea(a, b)
    //   Root  Cities[id] → CityVisual { city, go, flag, labelAnchor, pos, size, mapX, mapY, LabelPos }
    //   Region（"china" | "world"）Bounds（可玩范围，地图坐标；CameraRig.UseMap 用它）TreeCount Stats BuildVersion
    //   Built 事件：每次构建完成后触发（城池对象已重建，城名标签需要重新挂到新的 labelAnchor 上）
    // 分块地形（经典 32×32、世界 40×40 格，Unity 视锥剔除），深海格子不出网格；树木按区块合并网格（镜头很高时隐藏）。
    // 海路：GameState.LinkIsSea(a, b) 为真时两城之间画海上虚线航线（海域栅格上的 A*，绕开陆地），不画道路。
    public class MapView : MonoBehaviour
    {
        public const float MapW = 112, MapH = 100;
        public Transform Root;
        public readonly Dictionary<int, CityVisual> Cities = new Dictionary<int, CityVisual>();
        public string Region = "china";
        public Rect Bounds = new Rect(0, 0, MapW, MapH);
        public MapSpec Spec;
        public GeoFields Geo;
        public TerrainModel Model;
        public int TreeCount;
        public int BuildVersion;
        public float SliceMs = 8;
        public Camera Cam;                     // 云的淡出、树木隐藏、雾所用的镜头（缺省 Camera.main）
        public event Action Built;

        public class CityVisual
        {
            public City city; public GameObject go; public Renderer flag; public Renderer ring; public Transform labelAnchor; public Vector3 pos;
            public float size, mapX, mapY;
            public Vector3 LabelPos { get { return labelAnchor != null ? labelAnchor.position : pos + Vector3.up * (size * 1.6f + 0.6f); } }
        }

        // 构建统计（调试 / 性能检查）
        public class BuildStats
        {
            public float totalMs, maxSliceMs; public string maxSliceStage = ""; public int slices; public bool sync;
            public readonly Dictionary<string, float> stageMs = new Dictionary<string, float>();
        }
        public BuildStats Stats;

        GameState state;
        double[] cityX, cityZ;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> mats = new List<Material>();
        readonly List<GameObject> treeObjs = new List<GameObject>();
        readonly List<Cloud> clouds = new List<Cloud>();
        readonly Dictionary<int, double[]> lanes = new Dictionary<int, double[]>();
        sealed class Cloud { public Transform tr; public Renderer rend; public Material mat; public bool fade; }
        bool treesHidden;
        bool cloudBox;                          // 世界地图：云在镜头周围 ±(170, 115) 的方框内循环
        const float CloudBoxX = 170, CloudBoxZ = 115;
        const float CloudFadeNear = 41, CloudFadeFar = 55;
        bool fogSaved; float fogStart0, fogEnd0;
        // 势力光圈：贴合地表的方形面片，合并为一个网格（RGBA 顶点色）
        Mesh ringMesh; Color[] ringCols; readonly Dictionary<int, int> ringStart = new Dictionary<int, int>();
        const int RingN = 10; const float RingR = 2.9f;
        // 选择光圈
        GameObject selectRing; Mesh selectMesh; Vector2[] selectUv0, selectUv; int selected = -1;
        Material flagMat;
        static Mesh flagMesh;
        object buildToken;
        float prog; string progLabel = "";
        float stageBase, stageW;
        float time;

        static int SeaKey(int a, int b) { return Math.Min(a, b) * 4096 + Math.Max(a, b); }

        // ------------------------------------------------------ 查询 --
        public float Height(float x, float z) { return Model != null ? (float)Model.Height(x, z) : 0; }
        public float SurfaceY(float x, float z) { return Model != null ? (float)Model.SurfaceY(x, z) : 0; }
        public bool IsForest(float x, float z, float h) { return Model != null && Model.IsForest(x, z, h); }
        public bool LinkIsSea(int a, int b) { return state != null && state.LinkIsSea(a, b); }
        // 海路的航线（地图坐标 x0, z0, x1, z1, …，从编号小的城到编号大的城）；没有时为 null
        public double[] SeaLane(int a, int b) { double[] p; return lanes.TryGetValue(SeaKey(a, b), out p) ? p : null; }

        Camera ViewCam { get { return Cam != null ? Cam : Camera.main; } }

        // ------------------------------------------------------ 构建 --
        public void Build(GameState g) { Build(g, "auto"); }
        public void Build(GameState g, string region)
        {
            CancelBuild();
            var sw = Stopwatch.StartNew();
            GeoFields.Run(Steps(g, region));
            Stats.totalMs = Stats.maxSliceMs = (float)sw.Elapsed.TotalMilliseconds;
            Stats.slices = 1; Stats.sync = true;
            Finish();
        }

        public IEnumerator BuildAsync(GameState g, string region, Action<float, string> progress)
        {
            CancelBuild();
            var token = new object();
            buildToken = token;
            Action report = () => { if (progress != null) { try { progress(Mathf.Clamp01(prog), progLabel); } catch (Exception e) { UnityEngine.Debug.LogWarning(e); } } };
            prog = 0; progLabel = "测绘山川";
            report();
            var stack = new Stack<IEnumerator>();
            stack.Push(Steps(g, region));
            var sw = Stopwatch.StartNew();
            double t0 = 0, maxSlice = 0; string maxStage = ""; int slices = 0;
            float budget = SliceMs > 0 ? SliceMs : 8;
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                var sub = top.Current as IEnumerator;
                if (sub != null) { stack.Push(sub); continue; }
                double t = sw.Elapsed.TotalMilliseconds;
                if (t - t0 >= budget)
                {
                    if (t - t0 > maxSlice) { maxSlice = t - t0; maxStage = progLabel; }
                    slices++;
                    report();
                    yield return null;
                    if (buildToken != token) yield break;   // 被新的构建取代
                    t0 = sw.Elapsed.TotalMilliseconds;
                }
            }
            maxSlice = Math.Max(maxSlice, sw.Elapsed.TotalMilliseconds - t0); slices++;
            buildToken = null;
            Stats.totalMs = (float)sw.Elapsed.TotalMilliseconds; Stats.maxSliceMs = (float)maxSlice; Stats.maxSliceStage = maxStage; Stats.slices = slices; Stats.sync = false;
            prog = 1; report();
            Finish();
        }
        void CancelBuild() { buildToken = null; }
        void Finish()
        {
            BuildVersion++;
            if (Built != null) { try { Built(); } catch (Exception e) { UnityEngine.Debug.LogException(e); } }
        }

        // 区域：auto → 所有城池都在旧版中国地图内时为 china，否则 world
        static string ResolveRegion(GameState g, string region)
        {
            if (region == "world" || region == "china") return region;
            foreach (var c in g.cities)
            {
                double x, y; WorldGeo.CityPos(c, out x, out y);
                if (x < -20 || x > MapW + 20 || y < -20 || y > MapH + 20) return "world";
            }
            return "china";
        }

        void Sub(float frac) { prog = stageBase + stageW * Mathf.Clamp01(frac); }
        IEnumerator Stage(string key, string label, float w, IEnumerator body)
        {
            var sw = Stopwatch.StartNew();
            progLabel = label; stageW = w; prog = stageBase;
            yield return body;
            stageBase += w; prog = stageBase;
            Stats.stageMs[key] = (float)sw.Elapsed.TotalMilliseconds;
        }

        IEnumerator Steps(GameState g, string region)
        {
            bool hadRoot = Root != null;
            Dispose();
            state = g;
            region = ResolveRegion(g, region);
            Spec = MapSpec.ForRegion(region);
            Region = Spec.Key;
            Bounds = Rect.MinMaxRect(Spec.Bounds.xMin, Spec.Bounds.yMin, Spec.Bounds.xMax, Spec.Bounds.yMax);
            Stats = new BuildStats();
            Root = new GameObject("StrategyMap").transform;
            Root.SetParent(transform, false);
            int n = g.cities.Count;
            cityX = new double[n]; cityZ = new double[n];
            for (int i = 0; i < n; i++) WorldGeo.CityPos(g.cities[i], out cityX[i], out cityZ[i]);
            stageBase = 0; prog = 0;
            // 旧地图的释放（大量网格 / 材质）单独成一步
            if (hadRoot) { progLabel = "测绘山川"; yield return null; }
            var holder = new GeoFields[1];
            yield return Stage("geo", "测绘山川", 0.24f, GeoFields.For(Spec, holder));
            Geo = holder[0];
            Model = new TerrainModel(Geo, Spec, cityX, cityZ);
            yield return Stage("terrain", "堆砌地形", 0.3f, GenTerrain());
            yield return Stage("skirt", "远景", 0.04f, GenSkirt());
            yield return Stage("water", "江海", 0.1f, GenWater());
            yield return Stage("trees", "林木", 0.14f, GenTrees());
            yield return Stage("roads", "道路航线", 0.05f, GenRoads(g));
            yield return Stage("cities", "城池", 0.1f, GenCities(g));
            yield return Stage("misc", "云", 0.03f, GenMisc(g));
        }

        IEnumerator GenMisc(GameState g)
        {
            BuildClouds();
            yield return null;
            BuildSelectRing();
            Refresh(g);
            yield return null;
        }

        // ------------------------------------------------------ 网格工具 --
        static int[] seq;
        static int[] Seq(int n)
        {
            if (seq == null || seq.Length < n) { seq = new int[Math.Max(n, 65536)]; for (int i = 0; i < seq.Length; i++) seq[i] = i; }
            return seq;
        }
        // 平面着色三角形的累加器（每个三角形独立顶点，顶点色用 Color32，不带 UV）
        sealed class FlatAcc
        {
            public readonly List<Vector3> v, n; public readonly List<Color32> c;
            public FlatAcc(int cap = 1024) { v = new List<Vector3>(cap); n = new List<Vector3>(cap); c = new List<Color32>(cap); }
            public int Count { get { return v.Count; } }
            public void Tri(Vector3 a, Vector3 b, Vector3 cc, Color32 col)
            {
                var nn = Vector3.Cross(b - a, cc - a);
                float l = nn.magnitude;
                nn = l > 1e-12f ? nn / l : Vector3.up;
                v.Add(a); v.Add(b); v.Add(cc);
                n.Add(nn); n.Add(nn); n.Add(nn);
                c.Add(col); c.Add(col); c.Add(col);
            }
        }
        Mesh NewMesh(string name, List<Vector3> v, List<Vector3> nrm, List<Color32> col, List<Vector2> uv = null)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            if (nrm != null) m.SetNormals(nrm);
            if (col != null) m.SetColors(col);
            if (uv != null) m.SetUVs(0, uv);
            m.SetIndices(Seq(v.Count), 0, v.Count, MeshTopology.Triangles, 0);
            m.RecalculateBounds();
            meshes.Add(m);
            return m;
        }
        GameObject Obj(string name, Mesh mesh, Material mat, bool cast, bool receive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = receive;
            return go;
        }
        Mesh TrackMesh(Mesh m) { meshes.Add(m); return m; }
        Material TrackMat(Material m) { mats.Add(m); return m; }

        // 一个地形三角形（Unity 坐标 a, b, c）：按重心取色
        void TerrainTri(FlatAcc acc, System.Random rnd, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, bool noForest)
        {
            double mx = ((double)ax + bx + cx) / 3, my = ((double)ay + by + cy) / 3, mz = ((double)az + bz + cz) / 3;
            double ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double ln = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            double slope = 1 - Math.Abs(ny / (ln > 0 ? ln : 1));
            var col = Model.GroundColor(mx, mz, my, slope, (rnd.NextDouble() - 0.5) * 0.06, noForest);
            acc.Tri(new Vector3(ax, ay, az), new Vector3(bx, by, bz), new Vector3(cx, cy, cz), col);
        }

        // ------------------------------------------------------ 地形 --
        IEnumerator GenTerrain()
        {
            yield return Model.GenGrid(f => Sub(0.6f * f));
            var T = Spec.Terrain;
            double step = T.step, x0 = T.x0, z0 = T.z0;
            int nx = Model.GNx, nz = Model.GNz, W = nz + 1;
            var hs = Model.Hs;
            var rnd = new System.Random(7);
            // 分块，避免单个网格过大，并让视锥剔除生效；四角都在深海（−4.6，远海海床同高）的格子不画
            int chunk = T.chunk; const double DEEP = -4.59;   // 与网页版相同：Float32 高度与 double 常数比较
            int nChunks = ((nx + chunk - 1) / chunk) * ((nz + chunk - 1) / chunk), done = 0;
            for (int ci = 0; ci < nx; ci += chunk)
                for (int cj = 0; cj < nz; cj += chunk)
                {
                    int iEnd = Math.Min(nx, ci + chunk), jEnd = Math.Min(nz, cj + chunk);
                    FlatAcc acc = null;
                    for (int i = ci; i < iEnd; i++)
                    {
                        // 每 10 行让出一次
                        if (i > ci && (i - ci) % 10 == 0) yield return null;
                        for (int j = cj; j < jEnd; j++)
                        {
                            float ha = hs[i * W + j], hb = hs[i * W + j + 1], hc = hs[(i + 1) * W + j + 1], hd = hs[(i + 1) * W + j];
                            if (ha <= DEEP && hb <= DEEP && hc <= DEEP && hd <= DEEP) continue;
                            if (acc == null) acc = new FlatAcc(chunk * chunk * 6);
                            float xa = (float)(x0 + i * step), xb = (float)(x0 + (i + 1) * step), za = (float)(z0 + j * step), zb = (float)(z0 + (j + 1) * step);
                            if (((i + j) & 1) == 0)
                            {
                                TerrainTri(acc, rnd, xa, ha, za, xa, hb, zb, xb, hc, zb, false);
                                TerrainTri(acc, rnd, xa, ha, za, xb, hc, zb, xb, hd, za, false);
                            }
                            else
                            {
                                TerrainTri(acc, rnd, xa, ha, za, xa, hb, zb, xb, hd, za, false);
                                TerrainTri(acc, rnd, xa, hb, zb, xb, hc, zb, xb, hd, za, false);
                            }
                        }
                    }
                    if (acc != null)
                    {
                        yield return null;
                        Obj("Terrain", NewMesh("terrain", acc.v, acc.n, acc.c), Art.LowPoly, true, true);
                    }
                    Sub(0.6f + 0.4f * (++done) / nChunks);
                    yield return null;
                }
        }

        // 远景地形：主地形网格之外的粗网格，使远处与旋转视角下不露出空洞（略低于主网格，重叠处被遮住）
        IEnumerator GenSkirt()
        {
            var S = Spec.Skirt;
            double fx0 = Model.GX0, fz0 = Model.GZ0, fx1 = Model.GX0 + Model.GNx * Model.GStep, fz1 = Model.GZ0 + Model.GNz * Model.GStep;
            double step = S.step, X0 = S.x0, Z0 = S.z0;
            int nx = (int)Math.Ceiling((S.x1 - X0) / step), nz = (int)Math.Ceiling((S.z1 - Z0) / step), W = nz + 1;
            var hs = new float[(nx + 1) * W];
            for (int k = 0; k < hs.Length; k++) hs[k] = float.NaN;
            Func<int, int, float> H = (i, j) =>
            {
                float h = hs[i * W + j];
                if (float.IsNaN(h)) { h = (float)(Model.Height(X0 + i * step, Z0 + j * step) - 0.3); hs[i * W + j] = h; }
                return h;
            };
            var rnd = new System.Random(17);
            const double DEEP = -4.89; int tile = S.tile;
            int nTiles = ((nx + tile - 1) / tile) * ((nz + tile - 1) / tile), done = 0;
            var cells = new List<int>();
            for (int ti = 0; ti < nx; ti += tile)
                for (int tj = 0; tj < nz; tj += tile)
                {
                    cells.Clear();
                    for (int i = ti; i < Math.Min(nx, ti + tile); i++)
                        for (int j = tj; j < Math.Min(nz, tj + tile); j++)
                        {
                            double xa = X0 + i * step, za = Z0 + j * step;
                            if (xa >= fx0 && xa + step <= fx1 && za >= fz0 && za + step <= fz1) continue;   // 主网格内
                            float a = H(i, j), b = H(i, j + 1), c = H(i + 1, j + 1), d = H(i + 1, j);
                            if (a <= DEEP && b <= DEEP && c <= DEEP && d <= DEEP) continue;
                            cells.Add(i); cells.Add(j);
                        }
                    if (cells.Count > 0)
                    {
                        var acc = new FlatAcc(cells.Count * 3);
                        for (int q = 0; q < cells.Count; q += 2)
                        {
                            int i = cells[q], j = cells[q + 1];
                            float xa = (float)(X0 + i * step), za = (float)(Z0 + j * step), xb = (float)(X0 + (i + 1) * step), zb = (float)(Z0 + (j + 1) * step);
                            float ha = H(i, j), hb = H(i, j + 1), hc = H(i + 1, j + 1), hd = H(i + 1, j);
                            if (((i + j) & 1) == 0)
                            {
                                TerrainTri(acc, rnd, xa, ha, za, xa, hb, zb, xb, hc, zb, true);
                                TerrainTri(acc, rnd, xa, ha, za, xb, hc, zb, xb, hd, za, true);
                            }
                            else
                            {
                                TerrainTri(acc, rnd, xa, ha, za, xa, hb, zb, xb, hd, za, true);
                                TerrainTri(acc, rnd, xa, hb, zb, xb, hc, zb, xb, hd, za, true);
                            }
                        }
                        Obj("TerrainFar", NewMesh("terrainFar", acc.v, acc.n, acc.c), Art.LowPoly, false, true);
                    }
                    Sub((float)(++done) / nTiles);
                    yield return null;
                }
            // 远海海床：深水下方（与深海地形 −4.6 同高），免得透过水面看到天空
            var mb = new MeshBuilder();
            var sb = TerrainModel.SEABED;
            var bed = new Color((float)sb[0], (float)sb[1], (float)sb[2]);
            float E = 1600, by = -4.62f, cxm = (float)((S.x0 + S.x1) / 2), czm = (float)((S.z0 + S.z1) / 2);
            mb.Quad(new Vector3(cxm - E, by, czm - E), new Vector3(cxm - E, by, czm + E), new Vector3(cxm + E, by, czm + E), new Vector3(cxm + E, by, czm - E), bed);
            Obj("SeaBed", TrackMesh(mb.ToMesh("seabed")), Art.LowPoly, false, true);
        }

        // ------------------------------------------------------ 水面 --
        IEnumerator GenWater()
        {
            var Wt = Spec.Water;
            double step = Wt.step, x0 = Wt.x0, z0 = Wt.z0;
            int nx = (int)Math.Ceiling((Wt.x1 - x0) / step), nz = (int)Math.Ceiling((Wt.z1 - z0) / step), W = nz + 1;
            double ex1 = x0 + nx * step, ez1 = z0 + nz * step;
            var hg = new float[(nx + 1) * W];
            for (int i = 0; i <= nx; i++)
            {
                for (int j = 0; j <= nz; j++) hg[i * W + j] = (float)Model.Height(x0 + i * step, z0 + j * step);
                Sub(0.7f * i / nx);
                yield return null;
            }
            // 顶点色 r = 岸边系数（水浅处颜色浅）；远海为 0
            Func<float, float> shore = h => Mathf.Clamp01((h + 2.2f) / 2.2f);
            var vs = new List<Vector3>(); var ns = new List<Vector3>(); var cs = new List<Color32>(); var ids = new List<int>();
            Action<double, double, double, double, float, float, float, float> quad = (xa, za, sx, sz, ca, cb, cc, cd) =>
            {
                int s = vs.Count;
                vs.Add(new Vector3((float)xa, 0, (float)za)); vs.Add(new Vector3((float)xa, 0, (float)(za + sz)));
                vs.Add(new Vector3((float)(xa + sx), 0, (float)(za + sz))); vs.Add(new Vector3((float)(xa + sx), 0, (float)za));
                for (int k = 0; k < 4; k++) ns.Add(Vector3.up);
                cs.Add(new Color(ca, 0, 0, 1)); cs.Add(new Color(cb, 0, 0, 1)); cs.Add(new Color(cc, 0, 0, 1)); cs.Add(new Color(cd, 0, 0, 1));
                ids.Add(s); ids.Add(s + 1); ids.Add(s + 2); ids.Add(s); ids.Add(s + 2); ids.Add(s + 3);
            };
            Action<string> flush = name =>
            {
                if (vs.Count == 0) return;
                var m = new Mesh { name = name };
                if (vs.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(vs); m.SetNormals(ns); m.SetColors(cs); m.SetTriangles(ids, 0); m.RecalculateBounds();
                meshes.Add(m);
                Obj(name, m, Art.Water, false, false);
                vs.Clear(); ns.Clear(); cs.Clear(); ids.Clear();
            };
            int tile = Wt.tile;
            for (int ti = 0; ti < nx; ti += tile)
            {
                for (int tj = 0; tj < nz; tj += tile)
                {
                    for (int i = ti; i < Math.Min(nx, ti + tile); i++)
                    {
                        if (i > ti && (i - ti) % 8 == 0) yield return null;
                        for (int j = tj; j < Math.Min(nz, tj + tile); j++)
                        {
                            float h00 = hg[i * W + j], h01 = hg[i * W + j + 1], h11 = hg[(i + 1) * W + j + 1], h10 = hg[(i + 1) * W + j];
                            // 四角都远高于水面的格子略过以节省顶点
                            if (h00 > 1.2 && h10 > 1.2 && h01 > 1.2 && h11 > 1.2) continue;   // 城池周围平整到恰好 1.2：按 double 比较（与网页版一致）
                            quad(x0 + i * step, z0 + j * step, step, step, shore(h00), shore(h01), shore(h11), shore(h10));
                        }
                    }
                    flush("Water");
                    yield return null;
                }
                Sub(0.7f + 0.25f * ti / nx);
                yield return null;
            }
            // 外海第一圈：宽 band，沿内圈边界按 step 细分（与内圈边界顶点完全重合）
            double band = Wt.band, far = Wt.far;
            int nb = (int)Math.Round(band / step);
            Func<int, int, float> inA = (i, j) => shore(hg[i * W + j]);
            Func<int, int, int, int, bool> land = (ia, ja, ib, jb) => hg[ia * W + ja] > 1.2 && hg[ib * W + jb] > 1.2;
            for (int i = 0; i < nx; i++)
            {
                double xa = x0 + i * step;
                if (!land(i, 0, i + 1, 0)) quad(xa, z0 - band, step, band, 0, inA(i, 0), inA(i + 1, 0), 0);
                if (!land(i, nz, i + 1, nz)) quad(xa, ez1, step, band, inA(i, nz), 0, 0, inA(i + 1, nz));
            }
            for (int j = -nb; j < nz + nb; j++)
            {
                double za = z0 + j * step;
                bool onEdge = j >= 0 && j < nz;
                // 边界上（0 ≤ 行号 ≤ nz）的顶点取岸边系数，其余为远海
                bool in0 = j >= 0 && j <= nz, in1 = j + 1 >= 0 && j + 1 <= nz;
                if (!(onEdge && land(0, j, 0, j + 1))) quad(x0 - band, za, band, step, 0, 0, in1 ? inA(0, j + 1) : 0, in0 ? inA(0, j) : 0);
                if (!(onEdge && land(nx, j, nx, j + 1))) quad(ex1, za, band, step, in0 ? inA(nx, j) : 0, in1 ? inA(nx, j + 1) : 0, 0, 0);
            }
            // 最外圈：四个互不重叠的矩形，约 20 的格子
            double bx0 = x0 - band, bz0 = z0 - band, bx1 = ex1 + band, bz1 = ez1 + band;
            Action<double, double, double, double> rect = (ax, az, bxx, bzz) =>
            {
                int cxn = Math.Max(1, WorldGeo.JsRound((bxx - ax) / 20)), czn = Math.Max(1, WorldGeo.JsRound((bzz - az) / 20));
                double sx = (bxx - ax) / cxn, sz = (bzz - az) / czn;
                for (int i = 0; i < cxn; i++) for (int j = 0; j < czn; j++) quad(ax + i * sx, az + j * sz, sx, sz, 0, 0, 0, 0);
            };
            rect(bx0 - far, bz0 - far, bx0, bz1 + far);
            rect(bx1, bz0 - far, bx1 + far, bz1 + far);
            rect(bx0, bz0 - far, bx1, bz0);
            yield return null;
            rect(bx0, bz1, bx1, bz1 + far);
            flush("WaterFar");
            yield return null;
        }

        // ------------------------------------------------------ 树木 --
        // 树种几何（共用）：顶点 + 基色；叶片顶点（leaf）的颜色乘以每棵树的叶色，树干保持原色
        sealed class TreeGeo { public Vector3[] v; public Color[] c; public bool[] leaf; }
        static TreeGeo[] treeGeo;
        static readonly Color Trunk = new Color(0.38f, 0.27f, 0.16f);
        static TreeGeo TreeGeometry(int kind)
        {
            if (treeGeo == null) treeGeo = new TreeGeo[TerrainModel.KindCount];
            if (treeGeo[kind] != null) return treeGeo[kind];
            var trunk = new MeshBuilder(); var leaf = new MeshBuilder();
            Color W = Color.white, W2 = new Color(0.88f, 0.88f, 0.88f);
            var p = Vector3.zero;
            switch (kind)
            {
                case TerrainModel.Broad0:
                case TerrainModel.Broad1:
                    trunk.Cylinder(p, 0.06f, 0.05f, 0.35f, 4, Trunk, false);
                    leaf.Blob(new Vector3(0, 0.65f, 0), new Vector3(0.42f, 0.4f, 0.42f), W, kind == TerrainModel.Broad0 ? 3 : 11);
                    break;
                case TerrainModel.Pine:
                    trunk.Cylinder(p, 0.06f, 0.05f, 0.35f, 4, Trunk, false);
                    leaf.Cone(new Vector3(0, 0.25f, 0), 0.42f, 0.7f, 6, W2);
                    leaf.Cone(new Vector3(0, 0.6f, 0), 0.32f, 0.6f, 6, W);
                    break;
                case TerrainModel.Cypress:
                    trunk.Cylinder(p, 0.05f, 0.04f, 0.2f, 4, Trunk, false);
                    leaf.Cone(new Vector3(0, 0.12f, 0), 0.19f, 1.15f, 6, W);
                    break;
                case TerrainModel.Shrub:
                    leaf.Blob(new Vector3(0, 0.16f, 0), new Vector3(0.3f, 0.2f, 0.3f), W, 5);
                    break;
                default:
                    {
                        // 椰树：略弯的树干 + 六片下垂的羽叶（正反两面）
                        float x = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            trunk.Cylinder(new Vector3(x, k * 0.24f, 0), 0.05f - k * 0.004f, 0.046f - k * 0.004f, 0.25f, 4, Trunk, false);
                            x += 0.02f + k * 0.015f;
                        }
                        var top = new Vector3(x, 0.98f, 0);
                        for (int k = 0; k < 6; k++)
                        {
                            float a = k * Mathf.PI / 3 + 0.3f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                            var mid = new Vector3(top.x + ca * 0.28f, top.y + 0.1f, top.z + sa * 0.28f); var tip = new Vector3(top.x + ca * 0.58f, top.y - 0.2f, top.z + sa * 0.58f);
                            var ml = new Vector3(mid.x - sa * 0.11f, mid.y, mid.z + ca * 0.11f); var mr = new Vector3(mid.x + sa * 0.11f, mid.y, mid.z - ca * 0.11f);
                            var c = k % 2 != 0 ? W : W2;
                            leaf.Tri(top, ml, tip, c); leaf.Tri(top, tip, mr, c);
                            leaf.Tri(top, tip, ml, c); leaf.Tri(top, mr, tip, c);
                        }
                        leaf.Blob(new Vector3(top.x, top.y - 0.02f, 0), new Vector3(0.07f, 0.06f, 0.07f), W2, 2);
                        break;
                    }
            }
            int na = trunk.Count, nb = leaf.Count;
            var g = new TreeGeo { v = new Vector3[na + nb], c = new Color[na + nb], leaf = new bool[na + nb] };
            for (int i = 0; i < na; i++) { g.v[i] = trunk.v[i]; g.c[i] = trunk.c[i]; }
            for (int i = 0; i < nb; i++) { g.v[na + i] = leaf.v[i]; g.c[na + i] = leaf.c[i]; g.leaf[na + i] = true; }
            treeGeo[kind] = g;
            return g;
        }

        // 按区块合并的树木网格；树种与叶色随群落变化（中国本部与旧版相同）
        IEnumerator GenTrees()
        {
            var list = new List<TerrainModel.TreeInst>();
            var rnd = new System.Random(11);
            yield return Model.PlaceTrees(rnd.NextDouble, list, Sub);
            TreeCount = list.Count;
            treesHidden = false;
            double tileSize = Spec.Tile; var T = Spec.Terrain;
            // 区块 → 树
            var buckets = new Dictionary<long, List<int>>();
            for (int k = 0; k < list.Count; k++)
            {
                var t = list[k];
                long key = (long)Math.Floor((t.x - T.x0) / tileSize) * 100000 + (long)Math.Floor((t.z - T.z0) / tileSize);
                List<int> b;
                if (!buckets.TryGetValue(key, out b)) buckets[key] = b = new List<int>();
                b.Add(k);
            }
            const int MaxVerts = 30000;
            var acc = new FlatAcc(8192);
            Action flush = () =>
            {
                if (acc.Count == 0) return;
                treeObjs.Add(Obj("Trees", NewMesh("trees", acc.v, acc.n, acc.c), Art.LowPoly, true, true));
                acc = new FlatAcc(8192);
            };
            int done = 0, cnt = 0;
            foreach (var b in buckets.Values)
            {
                foreach (int k in b)
                {
                    if ((++cnt & 255) == 0) yield return null;   // 每约 250 棵让出一次
                    var t = list[k];
                    var g = TreeGeometry(t.kind);
                    if (acc.Count + g.v.Length > MaxVerts) flush();
                    // 位置 × 绕 y 旋转（网页版 three 的 yaw → Unity −yaw）× 缩放 (s, sy, s)
                    var M = Matrix4x4.TRS(new Vector3(t.x, t.y, t.z), Quaternion.Euler(0, -t.yaw * Mathf.Rad2Deg, 0), new Vector3(t.s, t.sy, t.s));
                    Color32 trunkC = Trunk;
                    for (int i = 0; i + 2 < g.v.Length; i += 3)
                    {
                        Color c = g.leaf[i] ? g.c[i] * t.col : Trunk;
                        c.a = 1;
                        acc.Tri(M.MultiplyPoint3x4(g.v[i]), M.MultiplyPoint3x4(g.v[i + 1]), M.MultiplyPoint3x4(g.v[i + 2]), g.leaf[i] ? (Color32)c : trunkC);
                    }
                }
                flush();
                Sub(0.9f + 0.1f * (++done) / buckets.Count);
                yield return null;
            }
            flush();
        }

        // ------------------------------------------------------ 道路与海路 --
        IEnumerator GenRoads(GameState g)
        {
            var mb = new MeshBuilder();
            var col = new Color(0.78f, 0.68f, 0.5f);
            var done = new HashSet<int>();
            var cities = g.cities;
            var seaPairs = new List<int[]>();
            const int RoadVerts = 30000;
            Action flush = () =>
            {
                if (mb.Count > 0) Obj("Roads", TrackMesh(mb.ToMesh("roads")), Art.LowPoly, false, true);
                mb = new MeshBuilder();
            };
            int work = 0, ci = 0;
            foreach (var c in cities)
            {
                ci++;
                if (work >= 300) { work = 0; Sub(0.2f * ci / cities.Count); yield return null; }
                if (mb.Count >= RoadVerts) { flush(); yield return null; }
                foreach (var li in c.links)
                {
                    if (li < 0 || li >= cities.Count) continue;
                    var o = cities[li];
                    if (!done.Add(SeaKey(c.id, o.id))) continue;
                    if (g.LinkIsSea(c.id, o.id)) { seaPairs.Add(new[] { Math.Min(c.id, o.id), Math.Max(c.id, o.id) }); continue; }
                    double ax = cityX[c.id], ay = cityZ[c.id], bx = cityX[o.id], by = cityZ[o.id];
                    double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    int n = (int)Math.Ceiling(len / 0.9);
                    double dx = (bx - ax) / len, dy = (by - ay) / len;
                    double sx = -dy * 0.22, sy = dx * 0.22;
                    // 道路略带弯曲
                    double bend = (Mathf.PerlinNoise(c.id, o.id) - 0.5) * len * 0.12;
                    Vector3 prevL = Vector3.zero, prevR = Vector3.zero;
                    for (int i = 0; i <= n; i++)
                    {
                        double f = (double)i / n;
                        double w = Math.Sin(f * Math.PI) * bend;
                        double px = ax + (bx - ax) * f - dy * w, py = ay + (by - ay) * f + dx * w;
                        float h = (float)(Math.Max(0.12, Model.SurfaceY(px, py)) + 0.07);
                        var l = new Vector3((float)(px + sx), h, (float)(py + sy)); var r = new Vector3((float)(px - sx), h, (float)(py - sy));
                        if (i > 0) mb.Quad(prevL, l, r, prevR, col);
                        prevL = l; prevR = r;
                    }
                    work += n + 1;
                }
            }
            flush();
            yield return null;
            // 海路：沿水面绕开陆地的虚线航线
            if (seaPairs.Count > 0)
            {
                yield return Model.BuildSeaMask();
                var P = new List<Vector3>();
                var path = new List<double>();
                for (int k = 0; k < seaPairs.Count; k++)
                {
                    int a = seaPairs[k][0], b = seaPairs[k][1];
                    yield return Model.SeaPath(cityX[a], cityZ[a], cityX[b], cityZ[b], path);
                    var pts = path.ToArray();
                    lanes[SeaKey(a, b)] = pts;
                    Dashes(pts, P);
                    Sub(0.2f + 0.8f * (k + 1) / seaPairs.Count);
                    yield return null;
                }
                Model.ReleaseSeaMask();   // 只在生成航线时使用
                if (P.Count > 0)
                {
                    var cols = new List<Color32>(P.Count); var uvs = new List<Vector2>(P.Count);
                    for (int i = 0; i < P.Count; i++) { cols.Add(new Color32(255, 255, 255, 255)); uvs.Add(Vector2.zero); }
                    var m = NewMesh("seaLanes", P, null, cols, uvs);
                    var mat = TrackMat(Art.NewUnlit(new Color(0.97f, 0.94f, 0.84f, 0.85f)));
                    mat.renderQueue = 3003;
                    Obj("SeaLanes", m, mat, false, false);
                }
            }
        }
        // 沿折线生成虚线（三角形），离城池 2.4 以内不画。按弧长逐段（周期 DASH + GAP）取点
        void Dashes(double[] pts, List<Vector3> P)
        {
            const double DASH = 1.0, GAP = 0.7, Wd = 0.16, PERIOD = DASH + GAP;
            int n = pts.Length / 2;
            if (n < 2) return;
            var cum = new double[n];
            for (int k = 1; k < n; k++) cum[k] = cum[k - 1] + Hyp(pts[2 * k] - pts[2 * k - 2], pts[2 * k + 1] - pts[2 * k - 1]);
            double total = cum[n - 1];
            if (!(total > 0) || double.IsInfinity(total)) return;
            double ax = pts[0], az = pts[1], bx = pts[2 * n - 2], bz = pts[2 * n - 1];
            int seg = 0;
            Func<double, double[]> at = s =>
            {
                while (seg < n - 2 && cum[seg + 1] < s) seg++;
                double L = cum[seg + 1] - cum[seg], t = L > 1e-9 ? Math.Min(1, Math.Max(0, (s - cum[seg]) / L)) : 0;
                return new[] { pts[2 * seg] + (pts[2 * seg + 2] - pts[2 * seg]) * t, pts[2 * seg + 1] + (pts[2 * seg + 3] - pts[2 * seg + 1]) * t };
            };
            Action<double, double, double, double> quad = (sx, sz, ex, ez) =>
            {
                double L = Hyp(ex - sx, ez - sz);
                if (L < 1e-6) return;
                double mx = (sx + ex) / 2, mz = (sz + ez) / 2;
                if (Hyp(mx - ax, mz - az) <= 2.4 || Hyp(mx - bx, mz - bz) <= 2.4) return;
                double px = -(ez - sz) / L * Wd, pz = (ex - sx) / L * Wd;
                float ys = (float)Math.Max(0.13, Model.SurfaceY(sx, sz) + 0.12), ye = (float)Math.Max(0.13, Model.SurfaceY(ex, ez) + 0.12);
                P.Add(new Vector3((float)(sx + px), ys, (float)(sz + pz))); P.Add(new Vector3((float)(ex + px), ye, (float)(ez + pz))); P.Add(new Vector3((float)(ex - px), ye, (float)(ez - pz)));
                P.Add(new Vector3((float)(sx + px), ys, (float)(sz + pz))); P.Add(new Vector3((float)(ex - px), ye, (float)(ez - pz))); P.Add(new Vector3((float)(sx - px), ys, (float)(sz - pz)));
            };
            int nd = (int)Math.Ceiling(total / PERIOD);
            for (int d = 0; d < nd; d++)
            {
                double s0 = d * PERIOD, s1 = Math.Min(total, s0 + DASH);
                if (s1 <= s0) break;
                // 起点、途经的折线顶点、终点
                var p = at(s0);
                int seg0 = seg;
                for (int k = seg0 + 1; k < n - 1 && cum[k] < s1; k++) { quad(p[0], p[1], pts[2 * k], pts[2 * k + 1]); p = new[] { pts[2 * k], pts[2 * k + 1] }; }
                var e = at(s1);
                quad(p[0], p[1], e[0], e[1]);
            }
        }
        static double Hyp(double x, double y) { return Math.Sqrt(x * x + y * y); }

        // ------------------------------------------------------ 城池 --
        IEnumerator GenCities(GameState g)
        {
            int n = g.cities.Count;
            double tileSize = Spec.Tile; var T = Spec.Terrain;
            var bodies = new Dictionary<long, MeshBuilder>();   // 区块 → 合并的城体（视锥剔除按区块）
            const int BodyVerts = 24000;
            Action<MeshBuilder> flushBody = mb => Obj("Cities", TrackMesh(mb.ToMesh("cities")), Art.LowPoly, true, true);
            if (flagMesh == null) flagMesh = FlagMesh();
            flagMat = TrackMat(new Material(Art.LowPoly) { enableInstancing = false });
            // 势力光圈
            var ringPos = new List<Vector3>(); var ringUv = new List<Vector2>(); var ringIdx = new List<int>();
            ringStart.Clear();
            int k = 0;
            foreach (var c in g.cities)
            {
                float mx = (float)cityX[c.id], mz = (float)cityZ[c.id];
                float y = Mathf.Max(0.3f, (float)Model.Height(cityX[c.id], cityZ[c.id]));
                float size = Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(180, 650, c.town));
                // 城池模型按文化（CultureArt）；汉地与未知文化用原模型，都城变体：汉地为洛阳、长安，其他文化为各势力的初始都城
                string culture = CultureArt.CultureOfCity(c);
                bool capital = CultureArt.IsCapital(c, culture);
                float yaw = (c.id * 37) % 20 - 10;
                // 城体：烘焙到所在区块的合并网格中（平移 × 绕 y 旋转）
                long tk = (long)Math.Floor((mx - T.x0) / tileSize) * 100000 + (long)Math.Floor((mz - T.z0) / tileSize);
                MeshBuilder mb;
                if (!bodies.TryGetValue(tk, out mb)) bodies[tk] = mb = new MeshBuilder();
                mb.M = Matrix4x4.TRS(new Vector3(mx, y, mz), Quaternion.Euler(0, yaw, 0), Vector3.one);
                CultureArt.CityInto(mb, size, culture, capital, c);
                mb.M = Matrix4x4.identity;
                if (mb.Count >= BodyVerts) { bodies.Remove(tk); Sub(0.8f * k / n); yield return null; flushBody(mb); }

                var go = new GameObject("City_" + c.name);
                go.transform.SetParent(Root, false);
                go.transform.position = new Vector3(mx, y, mz);
                go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                var col = go.AddComponent<SphereCollider>(); col.radius = 2.6f; col.center = Vector3.up;
                var flag = new GameObject("Flag");
                flag.transform.SetParent(go.transform, false);
                flag.transform.localPosition = new Vector3(size * 0.9f, 0, size * 0.9f);
                flag.AddComponent<MeshFilter>().sharedMesh = flagMesh;
                var fr = flag.AddComponent<MeshRenderer>(); fr.sharedMaterial = flagMat;
                flag.AddComponent<FlagWave>();
                var anchor = new GameObject("Label").transform; anchor.SetParent(go.transform, false); anchor.localPosition = Vector3.up * (size * 1.6f + 0.6f);

                // 光圈：贴地，但不爬上相邻山坡（高出城基的部分留在地形下面）
                int start = ringPos.Count;
                for (int i = 0; i <= RingN; i++)
                    for (int j = 0; j <= RingN; j++)
                    {
                        float ux = mx - RingR + 2 * RingR * i / RingN, uz = mz - RingR + 2 * RingR * j / RingN;
                        ringPos.Add(new Vector3(ux, Mathf.Min(Mathf.Max((float)Model.SurfaceY(ux, uz), 0.02f), y + 0.45f) + 0.12f, uz));
                        ringUv.Add(new Vector2((float)i / RingN, (float)j / RingN));
                    }
                for (int i = 0; i < RingN; i++)
                    for (int j = 0; j < RingN; j++)
                    {
                        int a = start + i * (RingN + 1) + j, b = a + 1, cc = a + RingN + 2, d = a + RingN + 1;
                        ringIdx.Add(a); ringIdx.Add(b); ringIdx.Add(cc); ringIdx.Add(a); ringIdx.Add(cc); ringIdx.Add(d);
                    }
                ringStart[c.id] = start;

                Cities[c.id] = new CityVisual
                {
                    city = c, go = go, flag = fr, ring = null, labelAnchor = anchor, pos = new Vector3(mx, y, mz),
                    size = size, mapX = mx, mapY = mz,
                };
                Sub(0.8f * (++k) / n);
                yield return null;
            }
            foreach (var mb in bodies.Values) { flushBody(mb); yield return null; }

            ringMesh = new Mesh { name = "territory" };
            ringMesh.SetVertices(ringPos); ringMesh.SetUVs(0, ringUv);
            ringCols = new Color[ringPos.Count];
            ringMesh.colors = ringCols;
            ringMesh.SetTriangles(ringIdx, 0);
            ringMesh.RecalculateBounds();
            meshes.Add(ringMesh);
            var ringMat = TrackMat(Art.NewUnlit(Color.white, Art.Ring));
            ringMat.renderQueue = 3001;
            Obj("Territory", ringMesh, ringMat, false, false);
            yield return null;
        }

        static Mesh FlagMesh()
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0, 1.1f, 0), new Vector3(0.08f, 2.2f, 0.08f), new Color(0.35f, 0.25f, 0.15f));
            var a = new Vector3(0.04f, 2.15f, 0); var b = new Vector3(0.95f, 2.05f, 0); var c = new Vector3(0.95f, 1.45f, 0); var d = new Vector3(0.04f, 1.5f, 0);
            mb.Quad(a, b, c, d, Color.white); mb.Quad(a, d, c, b, Color.white);
            mb.Cone(new Vector3(0, 2.2f, 0), 0.08f, 0.16f, 5, new Color(0.9f, 0.75f, 0.3f));
            return mb.ToMesh("flag");
        }
        // FlatDisc(r)：带 UV 的方形面片（y = 0）
        public static Mesh FlatDisc(float r)
        {
            var mb = new MeshBuilder();
            mb.FlatQuad(new Vector3(-r, 0, -r), new Vector3(-r, 0, r), new Vector3(r, 0, r), new Vector3(r, 0, -r), Color.white);
            return mb.ToMesh("disc");
        }

        // 贴合地表的方形面片（地图坐标中心 cx, cz；半径 r；n×n 格；抬高 lift；地表高度上限 maxY），写入 mesh；返回 uv
        Vector2[] DrapedDisc(Mesh mesh, float cx, float cz, float r, int n, float lift, float maxY)
        {
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    float ux = cx - r + 2 * r * i / n, uz = cz - r + 2 * r * j / n;
                    pos.Add(new Vector3(ux, Mathf.Min(Mathf.Max((float)Model.SurfaceY(ux, uz), 0.02f), maxY) + lift, uz));
                    uv.Add(new Vector2((float)i / n, (float)j / n));
                }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int a = i * (n + 1) + j, b = a + 1, c = a + n + 2, d = a + n + 1;
                    idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(a); idx.Add(c); idx.Add(d);
                }
            mesh.Clear();
            mesh.SetVertices(pos); mesh.SetUVs(0, uv); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds();
            return uv.ToArray();
        }

        // ------------------------------------------------------ 云 --
        static Shader fadeShader; static bool fadeChecked;
        static Shader FadeShader()
        {
            if (fadeChecked) return fadeShader;
            fadeChecked = true;
            var s = Resources.Load<Shader>("Shaders/LowPolyFade");
            if (s == null) s = Shader.Find("Sanguo/LowPolyFade");
            fadeShader = s != null && s.isSupported ? s : null;
            return fadeShader;
        }
        void BuildClouds()
        {
            var rnd = new System.Random(5);
            bool world = Region == "world";
            cloudBox = world;
            var vc = world ? ViewCenter() : Vector2.zero;
            var sh = FadeShader();
            for (int i = 0; i < Spec.Clouds; i++)
            {
                var mb = new MeshBuilder();
                int puffs = 4 + rnd.Next(4);
                for (int k = 0; k < puffs; k++)
                {
                    var off = new Vector3((float)(rnd.NextDouble() - 0.5) * 7, (float)rnd.NextDouble() * 0.8f, (float)(rnd.NextDouble() - 0.5) * 3);
                    float r = 1.4f + (float)rnd.NextDouble() * 1.6f;
                    mb.Blob(off, new Vector3(r, r * 0.6f, r * 0.85f), new Color(1, 1, 1), k);
                }
                // 每朵云一个材质：镜头拉近（战略视角）时离镜头近的云连同云影一起淡出隐去，不再整片挡住城池
                var mat = TrackMat(sh != null ? new Material(sh) : Art.NewLowPoly());
                mat.SetFloat("_Emission", 0.35f);
                var go = Obj("Cloud", TrackMesh(mb.ToMesh("cloud")), mat, true, false);
                float px, py, pz;
                if (world)
                {
                    px = vc.x + ((float)rnd.NextDouble() * 2 - 1) * CloudBoxX; pz = vc.y + ((float)rnd.NextDouble() * 2 - 1) * CloudBoxZ;
                    py = 15 + (float)rnd.NextDouble() * 5;
                }
                else { px = (float)rnd.NextDouble() * MapW; py = 15 + (float)rnd.NextDouble() * 5; pz = (float)rnd.NextDouble() * MapH; }   // 与旧版相同的取数顺序
                go.transform.position = new Vector3(px, py, pz);
                clouds.Add(new Cloud { tr = go.transform, rend = go.GetComponent<Renderer>(), mat = mat, fade = sh != null });
            }
        }
        // 镜头视线与地面（y = 0）的交点，地图坐标
        Vector2 ViewCenter()
        {
            var fb = new Vector2(Bounds.center.x, Bounds.center.y);
            var cam = ViewCam;
            if (cam == null) return fb;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            float d;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out d)) return fb;
            var p = ray.GetPoint(d);
            return new Vector2(p.x, p.z);
        }

        void BuildSelectRing()
        {
            var mat = TrackMat(Art.NewUnlit(new Color(1f, 0.85f, 0.35f, 0.95f), Art.Ring));
            mat.renderQueue = 3002;
            selectMesh = TrackMesh(new Mesh { name = "selectRing" });
            selectRing = Obj("SelectRing", selectMesh, mat, false, false);
            selectRing.SetActive(false);
        }

        // ------------------------------------------------------ 更新 --
        public void Refresh(GameState g)
        {
            state = g;
            var mpb = new MaterialPropertyBlock();
            foreach (var cv in Cities.Values)
            {
                if (cv.city.id < g.cities.Count) cv.city = g.cities[cv.city.id];
                var c = cv.city;
                bool owned = c.owner >= 0 && c.owner < g.factions.Count;
                var col = owned ? g.factions[c.owner].Col : new Color(0.75f, 0.75f, 0.72f);
                mpb.SetColor("_Color", col);
                cv.flag.SetPropertyBlock(mpb);
                cv.flag.enabled = owned;
                float a = owned ? (c.owner == g.player ? 0.85f : 0.6f) : 0.25f;
                int start;
                if (ringCols != null && ringStart.TryGetValue(c.id, out start))
                {
                    var rc = new Color(col.r, col.g, col.b, a);
                    for (int k = 0; k < (RingN + 1) * (RingN + 1); k++) ringCols[start + k] = rc;
                }
            }
            if (ringMesh != null) ringMesh.colors = ringCols;
        }

        public void Select(int cityId)
        {
            selected = cityId;
            if (selectRing == null) return;
            CityVisual cv;
            if (cityId < 0 || !Cities.TryGetValue(cityId, out cv)) { selectRing.SetActive(false); return; }
            selectUv0 = DrapedDisc(selectMesh, cv.mapX, cv.mapY, 3.4f, 14, 0.18f, cv.pos.y + 0.45f);
            selectUv = new Vector2[selectUv0.Length];
            selectRing.SetActive(true);
        }

        public int Pick(Camera cam, Vector2 screen)
        {
            if (Root == null || Cities.Count == 0) return -1;
            RaycastHit hit;
            if (Physics.Raycast(cam.ScreenPointToRay(screen), out hit, 1000))
                foreach (var kv in Cities) if (hit.collider.gameObject == kv.Value.go) return kv.Key;
            // 无碰撞时选最近的城
            var ray = cam.ScreenPointToRay(screen);
            float d; var plane = new Plane(Vector3.up, Vector3.up * 0.5f);
            if (plane.Raycast(ray, out d))
            {
                var p = ray.GetPoint(d);
                var best = Cities.Values.OrderBy(c => Vector3.Distance(c.pos, p)).First();
                if (Vector3.Distance(best.pos, p) < 4f) return best.city.id;
            }
            return -1;
        }

        void Update()
        {
            if (Root == null) return;
            float dt = Time.deltaTime;
            time += dt;
            // 选择光圈：贴图按 1 ± 0.06 缩放脉动
            if (selectRing != null && selectRing.activeSelf && selectUv0 != null)
            {
                float s = 1 + Mathf.Sin(time * 3f) * 0.06f;
                for (int i = 0; i < selectUv0.Length; i++) selectUv[i] = new Vector2(0.5f + (selectUv0[i].x - 0.5f) / s, 0.5f + (selectUv0[i].y - 0.5f) / s);
                selectMesh.uv = selectUv;
            }
            var cam = ViewCam;
            Vector2 vc = cloudBox && clouds.Count > 0 ? ViewCenter() : Vector2.zero;
            foreach (var c in clouds)
            {
                var p = c.tr.position;
                p.x += 0.6f * dt; p.z += 0.15f * dt;
                if (cloudBox)
                {
                    // 世界地图：在镜头周围的方框内循环
                    float dx = p.x - vc.x, dz = p.z - vc.y;
                    if (dx > CloudBoxX) p.x -= 2 * CloudBoxX; else if (dx < -CloudBoxX) p.x += 2 * CloudBoxX;
                    if (dz > CloudBoxZ) p.z -= 2 * CloudBoxZ; else if (dz < -CloudBoxZ) p.z += 2 * CloudBoxZ;
                }
                else
                {
                    if (p.x > MapW + 30) p.x = -30;
                    if (p.z > MapH + 30) p.z = -30;
                }
                c.tr.position = p;
                // 离镜头越近越透明：标题 / 全图视角几乎不透明，战略视角里离镜头近的云完全隐去
                if (cam != null)
                {
                    float f = Mathf.Clamp01((Vector3.Distance(p, cam.transform.position) - CloudFadeNear) / (CloudFadeFar - CloudFadeNear));
                    float a = f * f * (3 - 2 * f);
                    bool shown = a > 0.01f;
                    if (c.rend.enabled != shown) c.rend.enabled = shown;
                    if (c.fade)
                    {
                        var col = c.mat.color; col.a = a; c.mat.color = col;
                    }
                    else c.tr.localScale = Vector3.one * Mathf.Max(0.01f, a);   // 没有半透明着色器时以缩放代替淡出
                    var mode = a * a > 1f / 18f ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (c.rend.shadowCastingMode != mode) c.rend.shadowCastingMode = mode;
                }
            }
            // 镜头很高时（只有世界地图能拉到那么远）不画树木：树在那个距离只有一两个像素，却占三角形总数的大半
            if (treeObjs.Count > 0 && cam != null)
            {
                bool hide = cam.transform.position.y > (treesHidden ? 165 : 180);
                if (hide != treesHidden) { treesHidden = hide; foreach (var t in treeObjs) if (t != null) t.SetActive(!hide); }
            }
            // 世界地图：镜头拉远时雾随之推远（地图隐藏时——如战斗——恢复原值）
            if (Region == "world" && cam != null && RenderSettings.fog)
            {
                if (!fogSaved) { fogSaved = true; fogStart0 = RenderSettings.fogStartDistance; fogEnd0 = RenderSettings.fogEndDistance; }
                float k = Root.gameObject.activeInHierarchy ? Mathf.Max(1, cam.transform.position.y / 105f) : 1;
                RenderSettings.fogStartDistance = fogStart0 * k; RenderSettings.fogEndDistance = fogEnd0 * k;
            }
        }
        void RestoreFog()
        {
            if (fogSaved) { RenderSettings.fogStartDistance = fogStart0; RenderSettings.fogEndDistance = fogEnd0; }
            fogSaved = false;
        }

        // 行军动画：一支小部队沿道路（海路则沿航线）移动
        public IEnumerator March(City from, City to, Color team, float seconds = 1.6f)
        {
            CityVisual a, b;
            if (Root == null || !Cities.TryGetValue(from.id, out a) || !Cities.TryGetValue(to.id, out b)) yield break;
            var root = Root;
            var mb = new MeshBuilder();
            Models.Commander(mb, Vector3.zero, team, 2.4f);
            for (int i = 0; i < 6; i++) Models.Soldier(mb, new Vector3((i % 3 - 1) * 0.55f, 0, -0.9f - (i / 3) * 0.55f), team, true, 2.2f);
            var mesh = mb.ToMesh("army");
            var go = Art.MakeObject("Army", mesh, Art.LowPoly, root);
            // 航线（若有）：按弧长取点
            int ia = from.id, ib = to.id;
            double[] lane = SeaLane(ia, ib);
            if (lane != null && ia > ib) { var r = new double[lane.Length]; for (int k = 0; k < lane.Length; k += 2) { r[k] = lane[lane.Length - 2 - k]; r[k + 1] = lane[lane.Length - 1 - k]; } lane = r; }
            double[] cum = null;
            if (lane != null)
            {
                cum = new double[lane.Length / 2];
                for (int k = 1; k < cum.Length; k++) cum[k] = cum[k - 1] + Hyp(lane[2 * k] - lane[2 * k - 2], lane[2 * k + 1] - lane[2 * k - 1]);
            }
            float heading = Mathf.Atan2(b.mapX - a.mapX, b.mapY - a.mapY) * Mathf.Rad2Deg;
            go.transform.rotation = Quaternion.Euler(0, heading, 0);
            Action<float> place = t =>
            {
                float k = Mathf.SmoothStep(0, 1, t);
                double x, z, yawDeg = heading;
                if (lane == null) { x = a.mapX + (b.mapX - a.mapX) * k; z = a.mapY + (b.mapY - a.mapY) * k; }
                else
                {
                    double L = cum[cum.Length - 1] * k;
                    int s = 1;
                    while (s < cum.Length - 1 && cum[s] < L) s++;
                    double tt = cum[s] > cum[s - 1] ? (L - cum[s - 1]) / (cum[s] - cum[s - 1]) : 0;
                    double x0 = lane[2 * s - 2], z0 = lane[2 * s - 1], x1 = lane[2 * s], z1 = lane[2 * s + 1];
                    x = x0 + (x1 - x0) * tt; z = z0 + (z1 - z0) * tt;
                    yawDeg = Math.Atan2(x1 - x0, z1 - z0) * Mathf.Rad2Deg;
                    go.transform.rotation = Quaternion.Euler(0, (float)yawDeg, 0);
                }
                float y = Mathf.Max(0.2f, (float)Model.SurfaceY(x, z)) + Mathf.Abs(Mathf.Sin(t * 30)) * 0.08f;
                go.transform.position = new Vector3((float)x, y, (float)z);
            };
            float tm = 0;
            place(0);
            while (tm < 1 && Root == root)
            {
                place(tm);
                yield return null;
                tm += Mathf.Min(0.25f, Time.deltaTime) / seconds;
            }
            if (go != null) Destroy(go);
            Destroy(mesh);
        }

        // ------------------------------------------------------ 释放 --
        void Dispose()
        {
            if (Root != null) Destroy(Root.gameObject);
            Root = null;
            foreach (var m in meshes) if (m != null) Destroy(m);
            foreach (var m in mats) if (m != null) Destroy(m);
            meshes.Clear(); mats.Clear(); treeObjs.Clear(); clouds.Clear(); lanes.Clear(); ringStart.Clear();
            Cities.Clear();
            ringMesh = null; ringCols = null; selectRing = null; selectMesh = null; selectUv0 = null; flagMat = null;
            selected = -1;
            RestoreFog();
        }
        void OnDestroy() { CancelBuild(); Dispose(); }
    }

    public class FlagWave : MonoBehaviour
    {
        float phase;
        void Start() { phase = UnityEngine.Random.value * 10; }
        void Update() { transform.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 1.7f + phase) * 14f, 0); }
    }
}
