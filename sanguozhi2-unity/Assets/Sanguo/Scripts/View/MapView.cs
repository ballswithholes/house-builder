// 三国志-霸王的世界 · 战略地图 ← 网页版 js/map-view.js（第三版 DESIGN-V3 §1：平滑、细致的地形）
//
// 地形与一切地理计算在 View/WorldGeo.cs 的 TerrainModel（无头可测，与网页版逐点一致）；本文件只负责把它画出来：
//   · 地形：四叉树分块 LOD（每块 32×32 格，块边长 20 × 2^级，镜头到块包围盒的距离 < 2 × 块边长时细分，四个子块都已生成才切换；
//     按需在帧间补建，块边有下垂的裙边，不露缝）。着色全部来自数据纹理（法线 / 凹凸遮蔽 / 高度、群落色 + 林地、材质权重）+
//     Sanguo/Terrain 着色器（Art.TerrainMaterial），与块的细分级别无关。
//   · 水面：平的网格（y = 0），Sanguo/MapWater 按像素取地形高度算深浅、岸边浪花、涟漪与高光、菲涅尔天空反射。
//   · 植被与小物件（Graphics.DrawMeshInstanced，按 100 单位的区块分组，区块级视锥剔除）：十四种平滑树（镜头 42 以内为模型、
//     18 以内为特写模型，其余为公告板 Sanguo/TreeSprite）、石块、灌丛、城外村舍（近景平滑 / 远景简化）、镜头附近的草丛与野花。
//   · 道路（Sanguo/Road：柔边路面、车辙）、桥（石拱桥 / 木桥）、关隘（中原关楼 / 西方石门，贴地的城墙段）——砖石用 Art.MasonryMaterial。
//   · 城池：各文化的平滑细致模型（CultureArt.CityInto，砖石材质），镜头较远时换成远景简化模型（CultureArt.CityFarInto）。
//   · 势力光圈、旗帜、选择光圈、柔边公告板云团（Sanguo/Cloud）、海上虚线航线（Sanguo/SeaLane）。
//
// 公开接口（与第二版相同，另加第三版的地形查询）
//   Build(g) / Build(g, region)        同步构建（region = "auto" | "china" | "world"）
//   BuildAsync(g, region, progress)    协程：切成约 SliceMs（默认 8ms）的分片，progress(0..1, 阶段名)
//   Refresh(g) Select(cityId) Pick(cam, screen) → cityId | -1   March(from, to, color, seconds)
//   Height(x, z)          程序化高度（未加细节噪声）
//   SurfaceY(x, z)        实际渲染的地表高度（L0 地形块的三角形插值）
//   TerrainInfo(x, z)     → TerrainInfo { h, slope, water: "sea"|"river"|"lake"|null, depth, forest, farm, sand, snow, rock, biome, river, coast, road }
//   RoadDist(x, z)        到最近陆路的距离（无路 999）
//   IsForest(x, z, h) LinkIsSea(a, b) SeaLane(a, b)
//   Passes（已建成的关隘：名称、地图坐标、标签高度）PassesSkipped
//   Root  Cities[id] → CityVisual { city, go, flag, labelAnchor, pos, size, mapX, mapY, LabelPos }
//   Region（"china" | "world"）Bounds TreeCount RockCount HouseCount Stats BuildVersion
//   Built 事件：每次构建完成后触发（城池对象已重建，城名标签需要重新挂到新的 labelAnchor 上）
//   Model（TerrainModel：战场地形取样等可直接用；BattleTerrain 用 Model.TerrainInfo(x, z, surf) 与 Model.MX0 / MZ0 / MRes）
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sanguo
{
    // 执行顺序排在缺省（0）之后：镜头（CameraRig.Update）先移动，本帧的地形分块、实例 LOD 与视锥剔除再按新位置计算
    // （网页版 game.js 同样先 rig.update 再 map.update）
    [DefaultExecutionOrder(100)]
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
        public int TreeCount, RockCount, HouseCount;
        public int BuildVersion;
        public float SliceMs = 8;
        public Camera Cam;                     // 地形分块、云、植被所用的镜头（缺省 Camera.main）
        public event Action Built;

        public class CityVisual
        {
            public City city; public GameObject go; public Renderer flag; public Renderer ring; public Transform labelAnchor; public Vector3 pos;
            public float size, mapX, mapY;
            public Vector3 LabelPos { get { return labelAnchor != null ? labelAnchor.position : pos + Vector3.up * (size * 1.6f + 0.6f); } }
        }
        // 已建成的关隘（地图坐标，y = 标签高度）
        public sealed class PassInfo { public string name; public float x, z, y; }
        public readonly List<PassInfo> Passes = new List<PassInfo>();
        public readonly List<string> PassesSkipped = new List<string>();

        // 构建统计（调试 / 性能检查）
        public class BuildStats
        {
            public float totalMs, maxSliceMs; public string maxSliceStage = ""; public int slices; public bool sync;
            public readonly Dictionary<string, float> stageMs = new Dictionary<string, float>();
        }
        public BuildStats Stats;

        GameState state;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> mats = new List<Material>();
        readonly List<Texture> texs = new List<Texture>();
        readonly List<Cloud> clouds = new List<Cloud>();
        readonly Dictionary<int, double[]> lanes = new Dictionary<int, double[]>();
        sealed class Cloud { public Transform tr; public Renderer rend; public Material mat; }
        TerrainLOD lod;
        TerrainMaps maps;
        Material terrainMat;
        readonly List<TreeTile> treeTiles = new List<TreeTile>();
        readonly List<InstTile> scatter = new List<InstTile>();
        readonly List<CityTile> cityTiles = new List<CityTile>();
        bool treesHidden;
        bool cloudBox;                          // 世界地图：云在镜头周围 ±(170, 115) 的方框内循环
        const float CloudBoxX = 170, CloudBoxZ = 115;
        const float CloudFadeNear = 44, CloudFadeFar = 64;
        const float CloudHideY = 70, CloudShowDY = 28;   // 镜头高度 ≤ 70 时云全部隐去，≥ 98 时全显
        bool fogSaved; float fogStart0, fogEnd0;
        // 势力光圈：贴合地表的方形面片，合并为一个网格（RGBA 顶点色）
        Mesh ringMesh; Color[] ringCols; readonly Dictionary<int, int> ringStart = new Dictionary<int, int>();
        const int RingN = 7; const float RingR = 2.9f;
        // 选择光圈
        GameObject selectRing; Mesh selectMesh; Vector2[] selectUv0, selectUv; int selected = -1; float selectAngle;
        Material flagMat;
        static Mesh flagMesh;
        // 草丛与野花（镜头附近，每帧分摊重铺）
        InstList grassL, flowerL; string grassKey; IEnumerator grassJob;
        object buildToken;
        float prog; string progLabel = "";
        float stageBase, stageW;
        float time;
        Plane[] frustum = new Plane[6];

        static int SeaKey(int a, int b) { return Math.Min(a, b) * 4096 + Math.Max(a, b); }
        static double Hyp(double x, double y) { x = Math.Abs(x); y = Math.Abs(y); double m = Math.Max(x, y); if (m == 0) return 0; double a = x / m, b = y / m; return Math.Sqrt(a * a + b * b) * m; }   // 与 V8 的 Math.hypot 逐位一致（两个参数）
        static double Now(Stopwatch sw) { return sw.Elapsed.TotalMilliseconds; }
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static double NowMs() { return clock.Elapsed.TotalMilliseconds; }

        // ------------------------------------------------------ 查询 --
        public float Height(float x, float z) { return Model != null ? (float)Model.Height(x, z) : 0; }
        public double Height(double x, double z) { return Model != null ? Model.Height(x, z) : 0; }
        public float SurfaceY(float x, float z) { return Model != null ? (float)Model.SurfaceY(x, z) : 0; }
        public double SurfaceY(double x, double z) { return Model != null ? Model.SurfaceY(x, z) : 0; }
        public TerrainInfo TerrainInfo(double x, double z) { return Model != null ? Model.TerrainInfo(x, z) : new TerrainInfo(); }
        public TerrainInfo TerrainInfo(double x, double z, Func<double, double, double> surf) { return Model != null ? Model.TerrainInfo(x, z, surf) : new TerrainInfo(); }
        public double RoadDist(double x, double z) { return Model != null ? Model.RoadDist(x, z) : 999; }
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
            Art.RunNested(Steps(g, region));
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
            Art.EnsureAutoQuality();
            state = g;
            region = MapSpec.Resolve(g, region);   // auto → 所有城池都在旧版中国地图内时为 china，否则 world
            Spec = MapSpec.ForRegion(region);
            Region = Spec.Key;
            Bounds = Rect.MinMaxRect(Spec.Bounds.xMin, Spec.Bounds.yMin, Spec.Bounds.xMax, Spec.Bounds.yMax);
            Stats = new BuildStats();
            Root = new GameObject("StrategyMap").transform;
            Root.SetParent(transform, false);
            stageBase = 0; prog = 0;
            // 旧地图的释放（大量网格 / 材质）单独成一步
            if (hadRoot) { progLabel = "测绘山川"; yield return null; }
            var holder = new GeoFields[1];
            yield return Stage("geo", "测绘山川", 0.16f, GeoFields.For(Spec, holder));
            Geo = holder[0];
            Model = new TerrainModel(Geo, Spec, g.cities);
            yield return Stage("terrain", "堆砌地形", 0.3f, GenTerrain());
            yield return Stage("water", "江海", 0.04f, GenWater());
            yield return Stage("trees", "林木", 0.12f, GenTrees());
            yield return Stage("scatter", "山石村落", 0.06f, GenScatter());
            yield return Stage("roads", "道路关隘", 0.07f, GenRoads(g));
            yield return Stage("cities", "城池", 0.17f, GenCities(g));
            yield return Stage("misc", "云", 0.08f, GenMisc(g));
        }

        IEnumerator GenMisc(GameState g)
        {
            yield return BuildClouds();
            BuildSelectRing(); Refresh(g); yield return null;
            InitGrass(); yield return null;
            // 按当前镜头预先补齐近处的细地形块（之后每帧按需补建）
            var cam = ViewCam;
            if (cam != null) for (int k = 0; k < 40 && lod.Update(cam, 6) > 0; k++) { Sub(0.3f + k / 60f); yield return null; }
        }

        // ------------------------------------------------------ 网格工具 --
        GameObject Obj(string name, Mesh mesh, Material mat, bool cast, bool receive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = receive;
            return go;
        }
        Mesh TrackMesh(Mesh m) { meshes.Add(m); return m; }
        Material TrackMat(Material m) { mats.Add(m); return m; }
        Mesh BuilderMesh(MeshBuilder mb, string name) { return TrackMesh(mb.ToMesh(name)); }

        // ------------------------------------------------------ 地形 --
        IEnumerator GenTerrain()
        {
            var S = Spec.Skirt;
            yield return Model.GenTerrain(Sub);
            // 数据纹理
            var M = Model;
            Func<byte[], int, int, Texture2D> tex = (d, w, h) => { var t = Art.DataTexture(d, w, h, false, true); texs.Add(t); return t; };
            Func<double, double, double, int, int, Vector4> xf = (gx0, gz0, r, w, h) => new Vector4((float)((0.5 - gx0 / r) / w), (float)((0.5 - gz0 / r) / h), (float)(1 / (r * w)), (float)(1 / (r * h)));
            double X1 = M.GX0 + M.GNx * M.GStep, Z1 = M.GZ0 + M.GNz * M.GStep;
            maps = new TerrainMaps
            {
                n = tex(M.NData, M.MNx, M.MNz), c = tex(M.CData, M.TexW, M.TexH), m = tex(M.MData, M.TexW, M.TexH), oc = tex(M.OCData, M.ONx, M.ONz), om = tex(M.OMData, M.ONx, M.ONz),
                xfN = xf(M.MX0, M.MZ0, M.MRes, M.MNx, M.MNz), xfC = xf(M.GX0, M.GZ0, M.GStep, M.TexW, M.TexH), xfO = xf(M.OX0, M.OZ0, M.ORes, M.ONx, M.ONz),
                inner = new Vector4((float)M.GX0, (float)M.GZ0, (float)X1, (float)Z1),
            };
            yield return null;
            // 细节纹理、地形材质、四叉树（预建到第 2 级，之后按镜头补建）
            yield return Art.TerrainDetailGen();
            terrainMat = TrackMat(Art.TerrainMaterial(maps));
            lod = new TerrainLOD(this, terrainMat);
            lod.Init(M.GX0, M.GZ0, S.x0, S.z0, S.x1, S.z1, M.GX0, M.GZ0, X1, Z1);
            int k = 0;
            var pre = lod.Prebuild(2);
            while (pre.MoveNext()) { if ((++k & 3) == 0) Sub(0.9f + Mathf.Min(0.1f, k / 600f)); yield return null; }
            // 远海海床：深水下方（与深海地形同高），免得透过水面看到天空
            var mb = new MeshBuilder();
            var sb = TerrainModel.SEABED;
            var bed = new Color((float)(sb[0] * 0.8), (float)(sb[1] * 0.85), (float)(sb[2] * 0.9));
            float E = 1600, by = -4.62f, cxm = (float)((S.x0 + S.x1) / 2), czm = (float)((S.z0 + S.z1) / 2);
            mb.Quad(new Vector3(cxm - E, by, czm - E), new Vector3(cxm - E, by, czm + E), new Vector3(cxm + E, by, czm + E), new Vector3(cxm + E, by, czm - E), bed);
            Obj("SeaBed", BuilderMesh(mb, "seabed"), Art.LowPoly, false, true);
        }

        // 四叉树分块 LOD ← 网页版 TerrainLOD（每块 32×32 格；镜头到块包围盒的距离 < LOD_K × 块边长时细分）
        const int NODE_Q = 32;
        const double FINE = 0.625, LOD_K = 2.0, DEEP_Y = -4.55;
        const int N_NONE = 0, N_BUILT = 2, N_EMPTY = 3;
        sealed class LodNode
        {
            public int level, minLevel, state, lastUsed; public double x0, z0, size, step, dist; public LodNode parent; public LodNode[] children;
            public float minY = -5, maxY = 14; public GameObject go; public MeshRenderer mr; public Mesh mesh;
        }
        static int[] nodeIndex; static int[][] nodeEdges;
        static int[] NodeIndex()
        {
            if (nodeIndex != null) return nodeIndex;
            int n = NODE_Q, V = n + 1; var idx = new List<int>();
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    // a = (i, j) b = (i, j+1) c = (i+1, j+1) d = (i+1, j)（i → x，j → z）；对角线与 SurfaceY 相同（Unity 绕序）
                    int a = j * V + i, b = (j + 1) * V + i, c = (j + 1) * V + i + 1, d = j * V + i + 1;
                    if (((i + j) & 1) == 0) { idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(a); idx.Add(c); idx.Add(d); }
                    else { idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(c); idx.Add(d); }
                }
            // 裙边：四条边各 V 个顶点，向下复制；双面
            var edges = new int[4][];
            for (int e = 0; e < 4; e++) edges[e] = new int[V];
            for (int k = 0; k < V; k++) { edges[0][k] = k; edges[1][k] = n * V + k; edges[2][k] = k * V; edges[3][k] = k * V + n; }
            int s = V * V;
            foreach (var E in edges)
            {
                for (int k = 0; k < n; k++)
                {
                    int a = E[k], b = E[k + 1], c = s + k + 1, d = s + k;
                    idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(a); idx.Add(c); idx.Add(d); idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(a); idx.Add(d); idx.Add(c);
                }
                s += V;
            }
            nodeEdges = edges;
            nodeIndex = idx.ToArray();
            return nodeIndex;
        }
        sealed class TerrainLOD
        {
            readonly MapView view; readonly Material mat;
            public readonly List<LodNode> roots = new List<LodNode>();
            readonly List<LodNode> queue = new List<LodNode>();
            List<LodNode> shown = new List<LodNode>(), want = new List<LodNode>();
            public int frame, builtCount, maxNodes = 380;
            public int statNodes, statTris, statBuilt;
            double ix0, iz0, ix1, iz1;
            float[] tmp;
            Plane[] planes = new Plane[6];
            Vector3 cam;
            public TerrainLOD(MapView v, Material m) { view = v; mat = m; }
            public int QueueCount { get { return queue.Count; } }
            // lattice：块网格原点（= 主网格原点）；outer / inner：地图坐标
            public void Init(double lx, double lz, double ox0, double oz0, double ox1, double oz1, double ix0, double iz0, double ix1, double iz1)
            {
                this.ix0 = ix0; this.iz0 = iz0; this.ix1 = ix1; this.iz1 = iz1;
                double w = Math.Max(ox1 - ox0, oz1 - oz0);
                int R = Math.Max(3, (int)Math.Ceiling(Math.Log(w / (NODE_Q * FINE) / 2.5, 2)));
                double S = NODE_Q * FINE * Math.Pow(2, R);
                for (int kx = (int)Math.Floor((ox0 - lx) / S); kx * S + lx < ox1; kx++)
                    for (int kz = (int)Math.Floor((oz0 - lz) / S); kz * S + lz < oz1; kz++)
                        roots.Add(Node(R, lx + kx * S, lz + kz * S, null));
            }
            LodNode Node(int level, double x0, double z0, LodNode parent)
            {
                double size = NODE_Q * FINE * Math.Pow(2, level);
                bool touches = x0 < ix1 && x0 + size > ix0 && z0 < iz1 && z0 + size > iz0;
                return new LodNode { level = level, x0 = x0, z0 = z0, size = size, step = size / NODE_Q, minLevel = touches ? 0 : 3, parent = parent, state = N_NONE, minY = parent != null ? parent.minY : -5, maxY = parent != null ? parent.maxY : 14 };
            }
            void Split(LodNode nd)
            {
                double h = nd.size / 2;
                nd.children = new[] { Node(nd.level - 1, nd.x0, nd.z0, nd), Node(nd.level - 1, nd.x0 + h, nd.z0, nd), Node(nd.level - 1, nd.x0, nd.z0 + h, nd), Node(nd.level - 1, nd.x0 + h, nd.z0 + h, nd) };
            }
            // 生成一块的网格
            public void Build(LodNode nd)
            {
                int n = NODE_Q, V = n + 1, G = n + 3; double step = nd.step;
                var M = view.Model;
                var hh = tmp ?? (tmp = new float[G * G]);
                float mn = float.PositiveInfinity, mx = float.NegativeInfinity;
                for (int j = -1; j <= V; j++)
                {
                    double z = nd.z0 + j * step;
                    for (int i = -1; i <= V; i++)
                    {
                        float h = (float)M.VertexH(nd.x0 + i * step, z);
                        hh[(j + 1) * G + i + 1] = h;
                        if (i >= 0 && i < V && j >= 0 && j < V) { if (h < mn) mn = h; if (h > mx) mx = h; }
                    }
                }
                nd.minY = mn; nd.maxY = mx;
                if (mx < DEEP_Y) { nd.state = N_EMPTY; return; }
                int nv = V * V + 4 * V;
                var P = new Vector3[nv]; var N = new Vector3[nv];
                double inv2 = 1 / (2 * step);
                for (int j = 0; j < V; j++)
                    for (int i = 0; i < V; i++)
                    {
                        int o = j * V + i, q = (j + 1) * G + i + 1;
                        double hx = ((double)hh[q + 1] - hh[q - 1]) * inv2, hz = ((double)hh[q + G] - hh[q - G]) * inv2;
                        double l = 1 / Math.Sqrt(hx * hx + 1 + hz * hz);
                        P[o] = new Vector3((float)(nd.x0 + i * step), hh[q], (float)(nd.z0 + j * step));
                        N[o] = new Vector3((float)(-hx * l), (float)l, (float)(-hz * l));
                    }
                var idx = NodeIndex(); float sd = (float)(0.25 + step * 0.9);
                int s = V * V;
                foreach (var E in nodeEdges)
                {
                    for (int k = 0; k < V; k++) { int a = E[k]; P[s + k] = new Vector3(P[a].x, P[a].y - sd, P[a].z); N[s + k] = N[a]; }
                    s += V;
                }
                var mesh = new Mesh { name = "TerrainL" + nd.level };
                mesh.vertices = P; mesh.normals = N;
                mesh.SetTriangles(idx, 0);
                mesh.bounds = new Bounds(new Vector3((float)(nd.x0 + nd.size / 2), (mn - sd + mx) / 2, (float)(nd.z0 + nd.size / 2)), new Vector3((float)nd.size, mx - (mn - sd), (float)nd.size));
                var go = new GameObject("TerrainL" + nd.level);
                go.transform.SetParent(view.Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
                mr.enabled = false;
                nd.go = go; nd.mr = mr; nd.mesh = mesh;
                nd.state = N_BUILT;
                builtCount++;
            }
            void Free(LodNode nd)
            {
                if (nd.mesh != null) { UnityEngine.Object.Destroy(nd.go); UnityEngine.Object.Destroy(nd.mesh); nd.mesh = null; nd.go = null; nd.mr = null; builtCount--; }
                nd.state = N_NONE;
            }
            // 预先生成到 level 级（每块一步）
            public IEnumerator Prebuild(int level)
            {
                foreach (var r in roots) { var it = Rec(r, level); while (it.MoveNext()) yield return null; }
            }
            IEnumerator Rec(LodNode nd, int level)
            {
                if (nd.state == N_NONE) { Build(nd); yield return null; }
                if (nd.state == N_EMPTY || nd.level <= Math.Max(level, nd.minLevel)) yield break;
                if (nd.children == null) Split(nd);
                foreach (var c in nd.children) { var it = Rec(c, level); while (it.MoveNext()) yield return null; }
            }
            Bounds BoxOf(LodNode nd)
            {
                var b = new Bounds();
                b.SetMinMax(new Vector3((float)nd.x0, nd.minY - 0.5f, (float)nd.z0), new Vector3((float)(nd.x0 + nd.size), nd.maxY + 0.2f, (float)(nd.z0 + nd.size)));
                return b;
            }
            void Select(LodNode nd, List<LodNode> wantL)
            {
                if (nd.state == N_EMPTY) return;
                var b = BoxOf(nd);
                if (!GeometryUtility.TestPlanesAABB(planes, b)) return;
                double d = Math.Sqrt(b.SqrDistance(cam));
                if (nd.level > nd.minLevel && d < LOD_K * nd.size)
                {
                    if (nd.children == null) Split(nd);
                    bool ready = true;
                    foreach (var c in nd.children) if (c.state == N_NONE) { ready = false; c.dist = d; queue.Add(c); }
                    if (ready) { foreach (var c in nd.children) Select(c, wantL); return; }
                }
                if (nd.state == N_BUILT) { wantL.Add(nd); nd.lastUsed = frame; }
            }
            // 每帧：选择可见块、按距离补建缺少的细块（每帧最多 budgetMs），超出缓存上限时释放最久未用的块。返回待建块数
            public int Update(Camera camera, double budgetMs)
            {
                frame++;
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                cam = camera.transform.position;
                want.Clear();
                queue.Clear();
                foreach (var r in roots) Select(r, want);
                foreach (var nd in shown) if (nd.mr != null && nd.lastUsed != frame) nd.mr.enabled = false;
                int tris = 0;
                foreach (var nd in want) { nd.mr.enabled = true; tris += NODE_Q * NODE_Q * 2; }
                var prev = shown; shown = want; want = prev;
                statNodes = shown.Count; statTris = tris;
                int pending = queue.Count;
                if (pending > 0)
                {
                    queue.Sort((a, b) => a.dist != b.dist ? a.dist.CompareTo(b.dist) : b.level.CompareTo(a.level));
                    double t0 = NowMs();
                    foreach (var nd in queue)
                    {
                        if (nd.state != N_NONE) continue;
                        Build(nd);
                        statBuilt++;
                        if (NowMs() - t0 > budgetMs) break;
                    }
                }
                if (builtCount > maxNodes) Evict();
                return pending;
            }
            void Evict()
            {
                var list = new List<LodNode>();
                Action<LodNode> rec = null;
                rec = nd => { if (nd.children == null) return; foreach (var c in nd.children) { if (c.state == N_BUILT && c.lastUsed < frame - 30) list.Add(c); rec(c); } };
                foreach (var r in roots) rec(r);
                list.Sort((a, b) => a.lastUsed != b.lastUsed ? a.lastUsed.CompareTo(b.lastUsed) : a.level.CompareTo(b.level));
                for (int k = 0; k < list.Count && builtCount > maxNodes * 0.8; k++) Free(list[k]);
            }
            public void Dispose()
            {
                Action<LodNode> rec = null;
                rec = nd => { if (nd.mesh != null) UnityEngine.Object.Destroy(nd.mesh); if (nd.children != null) foreach (var c in nd.children) rec(c); };
                foreach (var r in roots) rec(r);
                roots.Clear(); shown.Clear();
            }
            // 测试 / 调试：各级各状态的块数（"级:状态" → 数）与已建块的高度校验和（DFS 顺序，每块取 30 个顶点的 y）
            public Dictionary<string, int> Census(out int samples, out double checksum)
            {
                var d = new Dictionary<string, int>(); int ns = 0; double cs = 0;
                Action<LodNode> rec = null;
                rec = nd =>
                {
                    string k = nd.level + ":" + nd.state; int c; d.TryGetValue(k, out c); d[k] = c + 1;
                    if (nd.mesh != null) { var P = nd.mesh.vertices; for (int v = 0; v * 3 + 1 < 33 * 33 * 3; v += 37) { cs += P[v].y; ns++; } }
                    if (nd.children != null) foreach (var ch in nd.children) rec(ch);
                };
                foreach (var r in roots) rec(r);
                samples = ns; checksum = cs;
                return d;
            }
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
                for (int j = 0; j <= nz; j++) hg[i * W + j] = (float)Model.SurfaceY(x0 + i * step, z0 + j * step);
                if ((i & 7) == 7) { Sub(0.5f * i / nx); yield return null; }
            }
            var mat = TrackMat(new Material(Art.LoadShader("MapWater")) { name = "MapWater" });
            mat.SetTexture("_Ripple", Art.Ripple);
            mat.SetTexture("_MapN", maps.n); mat.SetTexture("_OutC", maps.oc);
            mat.SetVector("_XfN", maps.xfN); mat.SetVector("_XfO", maps.xfO); mat.SetVector("_Inner", maps.inner);
            var vs = new List<Vector3>(); var ids = new List<int>();
            // 一个格子 a(xa,za) b(xa,za+sz) c(xa+sx,za+sz) d(xa+sx,za)，平面 y = 0
            Action<double, double, double, double> quad = (xa, za, sx, sz) =>
            {
                int s = vs.Count;
                vs.Add(new Vector3((float)xa, 0, (float)za)); vs.Add(new Vector3((float)xa, 0, (float)(za + sz)));
                vs.Add(new Vector3((float)(xa + sx), 0, (float)(za + sz))); vs.Add(new Vector3((float)(xa + sx), 0, (float)za));
                ids.Add(s); ids.Add(s + 1); ids.Add(s + 2); ids.Add(s); ids.Add(s + 2); ids.Add(s + 3);
            };
            Action<string> flush = name =>
            {
                if (vs.Count == 0) return;
                var m = new Mesh { name = name };
                if (vs.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                var nn = new List<Vector3>(vs.Count); for (int k = 0; k < vs.Count; k++) nn.Add(Vector3.up);
                m.SetVertices(vs); m.SetNormals(nn); m.SetTriangles(ids, 0); m.RecalculateBounds();
                meshes.Add(m);
                Obj(name, m, mat, false, false);
                vs.Clear(); ids.Clear();
            };
            int tile = Wt.tile; const float LAND = 1.2f;
            Func<int, int, int, int, bool> land = (ia, ja, ib, jb) => hg[ia * W + ja] > LAND && hg[ib * W + jb] > LAND;
            for (int ti = 0; ti < nx; ti += tile)
            {
                for (int tj = 0; tj < nz; tj += tile)
                {
                    for (int i = ti; i < Math.Min(nx, ti + tile); i++)
                        for (int j = tj; j < Math.Min(nz, tj + tile); j++)
                        {
                            if (hg[i * W + j] > LAND && hg[(i + 1) * W + j] > LAND && hg[i * W + j + 1] > LAND && hg[(i + 1) * W + j + 1] > LAND) continue;
                            quad(x0 + i * step, z0 + j * step, step, step);
                        }
                    yield return null;
                    flush("Water");
                    yield return null;
                }
                Sub(0.5f + 0.4f * ti / nx);
            }
            // 外海第一圈（宽 band，沿内圈边界按 step 细分）与最外圈的四个矩形
            double band = Wt.band, far = Wt.far;
            for (int i = 0; i < nx; i++)
            {
                double xa = x0 + i * step;
                if (!land(i, 0, i + 1, 0)) quad(xa, z0 - band, step, band);
                if (!land(i, nz, i + 1, nz)) quad(xa, ez1, step, band);
            }
            int nb = WorldGeo.JsRound(band / step);
            for (int j = -nb; j < nz + nb; j++)
            {
                double za = z0 + j * step; bool onEdge = j >= 0 && j < nz;
                if (!(onEdge && land(0, j, 0, j + 1))) quad(x0 - band, za, band, step);
                if (!(onEdge && land(nx, j, nx, j + 1))) quad(ex1, za, band, step);
            }
            double bx0 = x0 - band, bz0 = z0 - band, bx1 = ex1 + band, bz1 = ez1 + band;
            Action<double, double, double, double> rect = (ax, az, bxx, bzz) =>
            {
                int cxn = Math.Max(1, WorldGeo.JsRound((bxx - ax) / 20)), czn = Math.Max(1, WorldGeo.JsRound((bzz - az) / 20));
                double sx = (bxx - ax) / cxn, sz = (bzz - az) / czn;
                for (int i = 0; i < cxn; i++) for (int j = 0; j < czn; j++) quad(ax + i * sx, az + j * sz, sx, sz);
            };
            rect(bx0 - far, bz0 - far, bx0, bz1 + far);
            rect(bx1, bz0 - far, bx1 + far, bz1 + far);
            rect(bx0, bz0 - far, bx1, bz0);
            rect(bx0, bz1, bx1, bz1 + far);
            flush("WaterFar");
            yield return null;
        }

        // ------------------------------------------------------ 实例化 --
        // 一组实例（≤ 1023 个一批）：矩阵 + 实例颜色（+ 公告板图集格），Graphics.DrawMeshInstanced 绘制。
        // 数组按需分配：capacity = 预计的实例数（区块 / 树种组的实例总数），每批长度 = min(1023, 剩余容量)；
        // 超出预计（或未给容量）时本批按倍数增长（最少 16）——不为只有几个实例的组预留整批 1023 个
        sealed class InstList
        {
            const int Chunk = 1023, MinLen = 16;
            readonly List<Matrix4x4[]> m = new List<Matrix4x4[]>();
            readonly List<Vector4[]> c = new List<Vector4[]>();
            readonly List<float[]> cell = new List<float[]>();
            readonly List<MaterialPropertyBlock> mpb = new List<MaterialPropertyBlock>();
            readonly bool hasCell;
            readonly int cap;
            public int Count;
            bool dirty = true;
            public InstList(bool cells = false, int capacity = 0) { hasCell = cells; cap = capacity; }
            public void Clear() { Count = 0; dirty = true; }
            public void Add(Matrix4x4 mat, Vector4 col, float cl = 0)
            {
                int k = Count / Chunk, i = Count % Chunk;
                if (k >= m.Count)
                {
                    int rest = cap - k * Chunk, len = Math.Min(Chunk, rest > 0 ? rest : MinLen);
                    m.Add(new Matrix4x4[len]); c.Add(new Vector4[len]); if (hasCell) cell.Add(new float[len]); mpb.Add(new MaterialPropertyBlock());
                }
                else if (i >= m[k].Length)
                {
                    // 本批加倍；属性块里数组的长度设定后不能再变，换一个新的属性块
                    int len = Math.Min(Chunk, Math.Max(i + 1, m[k].Length * 2));
                    var mm = m[k]; Array.Resize(ref mm, len); m[k] = mm;
                    var cc = c[k]; Array.Resize(ref cc, len); c[k] = cc;
                    if (hasCell) { var ce = cell[k]; Array.Resize(ref ce, len); cell[k] = ce; }
                    mpb[k] = new MaterialPropertyBlock();
                }
                m[k][i] = mat; c[k][i] = col; if (hasCell) cell[k][i] = cl;
                Count++; dirty = true;
            }
            public void Draw(Mesh mesh, Material mat, ShadowCastingMode sh, bool recv, int layer, Camera cam)
            {
                if (Count == 0 || mesh == null || mat == null) return;
                int nChunk = (Count + Chunk - 1) / Chunk;
                for (int k = 0; k < nChunk; k++)
                {
                    int n = Math.Min(Chunk, Count - k * Chunk);
                    if (dirty) { mpb[k].SetVectorArray("_InstColor", c[k]); if (hasCell) mpb[k].SetFloatArray("_Cell", cell[k]); }
                    Graphics.DrawMeshInstanced(mesh, 0, mat, m[k], n, mpb[k], sh, recv, layer, cam);
                }
                dirty = false;
            }
        }
        // 三 → Unity：three 的绕 y 角 yaw（弧度）对应 Unity 的 −yaw
        static Matrix4x4 InstMatrix(double x, double y, double z, double s, double sy, double yaw)
        {
            return Matrix4x4.TRS(new Vector3((float)x, (float)y, (float)z), Quaternion.Euler(0, (float)(-yaw * 180 / Math.PI), 0), new Vector3((float)s, (float)sy, (float)s));
        }
        // 区块（100 单位）× 种类的分组（与网页版 _instBuckets 相同的键与插入顺序）
        sealed class Bucket { public string kind; public double cx, cz; public readonly List<TerrainModel.Inst> items = new List<TerrainModel.Inst>(); }
        List<Bucket> Buckets(List<TerrainModel.Inst> list)
        {
            var T = Spec.Terrain; const double tile = 100;
            var map = new Dictionary<string, Bucket>(); var order = new List<Bucket>();
            foreach (var it in list)
            {
                long bi = (long)Math.Floor((it.x - T.x0) / tile), bj = (long)Math.Floor((it.z - T.z0) / tile);
                string key = bi + "," + bj + "," + it.kind;
                Bucket b;
                if (!map.TryGetValue(key, out b)) { b = new Bucket { kind = it.kind, cx = (bi + 0.5) * tile + T.x0, cz = (bj + 0.5) * tile + T.z0 }; map[key] = b; order.Add(b); }
                b.items.Add(it);
            }
            return order;
        }
        static Bounds TileBounds(double cx, double cz, double r)
        {
            float w = (float)(2 * (r + 2));
            return new Bounds(new Vector3((float)cx, 5, (float)cz), new Vector3(w, 16, w));
        }

        // 石块 / 灌丛 / 村舍的区块：近景（平滑）与远景（简化）两个几何体，实例按到镜头的距离分到两边
        sealed class InstTile
        {
            public string name; public Mesh near, far; public Material mat; public bool shadow;
            public int n; public Matrix4x4[] mats; public Vector4[] cols; public float[] xz;
            public double cx, cz, r; public InstList nearL, farL;
            public string mode = "far", key = ""; public Bounds bounds; public bool hidden;
        }
        // 树木区块：每树种一个近景实例组（平滑模型，投影）与特写组（lod 2），另加一个远景公告板组（所有树种，一次绘制）
        sealed class TreeTile
        {
            public double cx, cz, r; public int n;
            public Matrix4x4[] mats, smats; public Vector4[] cols; public float[] cells, xz; public byte[] kind;
            public string[] kinds; public int[] gcount; public Mesh[] near, hero;
            public InstList[] nearL, heroL; public InstList farL;
            public string mode = "far", key = ""; public Bounds bounds; public bool hidden;
        }

        // ------------------------------------------------------ 树木 --
        IEnumerator GenTrees()
        {
            var list = new List<TerrainModel.Inst>();
            yield return Model.PlaceTrees(list, Sub);
            TreeCount = list.Count;
            treesHidden = false;
            // 区块 → 树种组（同一区块的各树种依插入顺序）
            var buckets = Buckets(list);
            var tiles = new Dictionary<string, List<Bucket>>(); var order = new List<string>();
            foreach (var b in buckets)
            {
                string key = b.cx + "," + b.cz;
                List<Bucket> g;
                if (!tiles.TryGetValue(key, out g)) { tiles[key] = g = new List<Bucket>(); order.Add(key); }
                g.Add(b);
            }
            foreach (var key in order)
            {
                var groups = tiles[key];
                var t = new TreeTile { cx = groups[0].cx, cz = groups[0].cz };
                int n = 0; double r = 0;
                foreach (var b in groups) n += b.items.Count;
                t.n = n; t.mats = new Matrix4x4[n]; t.smats = new Matrix4x4[n]; t.cols = new Vector4[n]; t.cells = new float[n]; t.xz = new float[n * 2]; t.kind = new byte[n];
                t.kinds = new string[groups.Count]; t.gcount = new int[groups.Count]; t.near = new Mesh[groups.Count]; t.hero = new Mesh[groups.Count];
                t.nearL = new InstList[groups.Count]; t.heroL = new InstList[groups.Count]; t.farL = new InstList(true, n);
                int i = 0;
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var b = groups[gi];
                    int cell = Math.Max(0, Array.IndexOf(MapVeg.SpriteKinds, b.kind));
                    var sz = MapVeg.SpriteSize(b.kind);
                    foreach (var it in b.items)
                    {
                        t.mats[i] = InstMatrix(it.x, it.y, it.z, it.s, it.sy, it.yaw);
                        t.smats[i] = Matrix4x4.TRS(new Vector3((float)it.x, (float)it.y, (float)it.z), Quaternion.identity, new Vector3((float)(sz[0] * it.s), (float)(sz[1] * it.sy), 1));
                        t.cols[i] = new Vector4(it.r, it.g, it.b, 1); t.cells[i] = cell; t.xz[i * 2] = (float)it.x; t.xz[i * 2 + 1] = (float)it.z; t.kind[i] = (byte)gi;
                        r = Math.Max(r, Hyp(it.x - t.cx, it.z - t.cz));
                        i++;
                    }
                    t.kinds[gi] = b.kind; t.gcount[gi] = b.items.Count;
                    t.near[gi] = MapVeg.TreeGeometry(b.kind, 0);
                    t.nearL[gi] = new InstList(false, b.items.Count); t.heroL[gi] = new InstList(false, b.items.Count);
                }
                t.r = r + 1;
                t.bounds = TileBounds(t.cx, t.cz, r);
                for (int k = 0; k < n; k++) t.farL.Add(t.smats[k], t.cols[k], t.cells[k]);
                treeTiles.Add(t);
                yield return null;
            }
            // 特写模型（lod 2）预先生成（每种一步），首次拉近镜头时不卡顿
            var kinds = new HashSet<string>();
            foreach (var t in treeTiles) foreach (var k in t.kinds) kinds.Add(k);
            foreach (var k in kinds) { MapVeg.TreeGeometry(k, 2); yield return null; }
            MapVeg.SpriteAtlas();
        }

        // ------------------------------------------------------ 石块、灌丛、村舍 --
        IEnumerator GenScatter()
        {
            var rocks = new List<TerrainModel.Inst>(); var shrubs = new List<TerrainModel.Inst>(); var houses = new List<TerrainModel.Inst>();
            yield return Model.PlaceScatter(rocks, shrubs, houses, Sub);
            RockCount = Model.RockCount; HouseCount = Model.HouseCount;
            yield return InstTiles(rocks, (k, l) => MapVeg.RockGeometry(k == "rock1" ? 1 : 0, l), Art.Foliage("rock"), "Rocks", false);
            yield return InstTiles(shrubs, (k, l) => MapVeg.TreeGeometry(k, l), Art.Foliage("leaf"), "Shrubs", false);
            yield return InstTiles(houses, (k, l) => MapVeg.HouseGeometry(k), Art.Foliage("plain"), "Villages", true);
        }
        IEnumerator InstTiles(List<TerrainModel.Inst> list, Func<string, int, Mesh> geo, Material mat, string name, bool shadow)
        {
            foreach (var b in Buckets(list))
            {
                int n = b.items.Count;
                var t = new InstTile { name = name, near = geo(b.kind, 0), far = geo(b.kind, 1), mat = mat, shadow = shadow, n = n, mats = new Matrix4x4[n], cols = new Vector4[n], xz = new float[n * 2], cx = b.cx, cz = b.cz, nearL = new InstList(false, n), farL = new InstList(false, n) };
                double r = 0;
                for (int i = 0; i < n; i++)
                {
                    var it = b.items[i];
                    t.mats[i] = InstMatrix(it.x, it.y, it.z, it.s, it.sy, it.yaw);
                    t.cols[i] = new Vector4(it.r, it.g, it.b, 1);
                    t.xz[i * 2] = (float)it.x; t.xz[i * 2 + 1] = (float)it.z;
                    r = Math.Max(r, Hyp(it.x - b.cx, it.z - b.cz));
                }
                t.r = r + 1;
                t.bounds = TileBounds(b.cx, b.cz, r);
                for (int i = 0; i < n; i++) t.farL.Add(t.mats[i], t.cols[i]);
                scatter.Add(t);
                yield return null;
            }
        }

        // ------------------------------------------------------ 道路、桥、关隘、海路 --
        static Texture2D roadTex;
        IEnumerator GenRoads(GameState g)
        {
            yield return Model.PlanRoads((a, b) => g.LinkIsSea(a, b), Sub);
            var roadMat = TrackMat(new Material(Art.LoadShader("Road")) { name = "Roads" });
            if (roadTex == null) roadTex = MapVeg.RoadTexture();
            roadMat.mainTexture = roadTex;
            // 路面：贴地的柔边条带（纹理带两道车辙，边缘透明渐隐）
            var P = new List<Vector3>(); var Nn = new List<Vector3>(); var UV = new List<Vector2>(); var CO = new List<Color>(); var ID = new List<int>();
            var roadCol = new Color(0.74f, 0.62f, 0.45f);
            const float HW = (float)TerrainModel.RoadHW;
            Action flush = () =>
            {
                if (P.Count == 0) return;
                var m = new Mesh { name = "roads" };
                if (P.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(P); m.SetNormals(Nn); m.SetUVs(0, UV); m.SetColors(CO); m.SetTriangles(ID, 0); m.RecalculateBounds();
                meshes.Add(m);
                Obj("Roads", m, roadMat, false, true);
                P.Clear(); Nn.Clear(); UV.Clear(); CO.Clear(); ID.Clear();
            };
            int work = 0;
            foreach (var L in Model.Roads)
            {
                for (int i = 0; i < L.n; i++)
                {
                    if (L.gap[i] || L.gap[i + 1]) continue;
                    int s = P.Count;
                    // 左（+n）/ 右（−n）：a 左、a 右、b 左、b 右；三角形 (a左, b左, a右)、(a右, b左, b右) 朝上
                    P.Add(new Vector3((float)(L.x[i] + L.nx[i] * HW), (float)L.y[i], (float)(L.z[i] + L.nz[i] * HW)));
                    P.Add(new Vector3((float)(L.x[i] - L.nx[i] * HW), (float)L.y[i], (float)(L.z[i] - L.nz[i] * HW)));
                    P.Add(new Vector3((float)(L.x[i + 1] + L.nx[i + 1] * HW), (float)L.y[i + 1], (float)(L.z[i + 1] + L.nz[i + 1] * HW)));
                    P.Add(new Vector3((float)(L.x[i + 1] - L.nx[i + 1] * HW), (float)L.y[i + 1], (float)(L.z[i + 1] - L.nz[i + 1] * HW)));
                    UV.Add(new Vector2(0, (float)L.v[i])); UV.Add(new Vector2(1, (float)L.v[i])); UV.Add(new Vector2(0, (float)L.v[i + 1])); UV.Add(new Vector2(1, (float)L.v[i + 1]));
                    for (int k = 0; k < 4; k++) { Nn.Add(Vector3.up); CO.Add(roadCol); }
                    ID.Add(s); ID.Add(s + 2); ID.Add(s + 1); ID.Add(s + 1); ID.Add(s + 2); ID.Add(s + 3);
                }
                work += L.n + 1;
                if (P.Count > 30000) { flush(); yield return null; }
                if (work >= 400) { work = 0; yield return null; }
            }
            flush();
            // 桥（汉、罗马、波斯等为石拱桥，其余为木桥）
            if (Model.Bridges.Count > 0)
            {
                var bm = new MeshBuilder();
                foreach (var b in Model.Bridges) MapVeg.BridgeInto(bm, b);
                yield return null;
                Obj("Bridges", BuilderMesh(bm, "bridges"), Art.MasonryMaterial(), true, true);
            }
            yield return null;
            // 关隘（虎牢关、函谷关、剑阁……）
            Model.PlanPasses(new MapRect(Bounds.xMin, Bounds.yMin, Bounds.xMax, Bounds.yMax));
            Passes.Clear(); PassesSkipped.Clear(); PassesSkipped.AddRange(Model.PassesSkipped);
            if (Model.Passes.Count > 0)
            {
                var pm = new MeshBuilder();
                foreach (var p in Model.Passes)
                {
                    MapVeg.PassInto(pm, Model, p);
                    Passes.Add(new PassInfo { name = p.name, x = (float)p.x, z = (float)p.z, y = (float)p.labelY });
                    yield return null;
                }
                Obj("Passes", BuilderMesh(pm, "passes"), Art.MasonryMaterial(), true, true);
            }
            // 海路：沿水面绕开陆地的虚线航线
            if (Model.SeaPairs.Count > 0)
            {
                yield return Model.BuildSeaMask();
                var DP = new List<Vector3>(); var DO = new List<Vector2>();
                var path = new List<double>();
                for (int k = 0; k < Model.SeaPairs.Count; k++)
                {
                    int a = Model.SeaPairs[k][0], b = Model.SeaPairs[k][1];
                    yield return Model.SeaPath(Model.CityX[a], Model.CityZ[a], Model.CityX[b], Model.CityZ[b], path);
                    var pts = path.ToArray();
                    lanes[SeaKey(a, b)] = pts;
                    Dashes(pts, DP, DO);
                    Sub(0.6f + 0.4f * (k + 1) / Model.SeaPairs.Count);
                    yield return null;
                }
                Model.ReleaseSeaMask();   // 只在生成航线时使用
                if (DP.Count > 0)
                {
                    var m = new Mesh { name = "seaLanes" };
                    if (DP.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                    var ids = new int[DP.Count]; for (int i = 0; i < ids.Length; i++) ids[i] = i;
                    m.SetVertices(DP); m.SetUVs(0, DO); m.SetTriangles(ids, 0); m.RecalculateBounds();
                    var bb = m.bounds; bb.Expand(2); m.bounds = bb;
                    meshes.Add(m);
                    var mat = TrackMat(new Material(Art.LoadShader("SeaLane")) { name = "SeaLanes" });
                    Obj("SeaLanes", m, mat, false, false);
                }
            }
        }
        // 沿折线生成虚线（三角形），离城池 2.4 以内不画、经过陆地的线段不画。顶点只存中线位置，半宽方向放在 uv（Sanguo/SeaLane 按镜头距离缩放）
        void Dashes(double[] pts, List<Vector3> P, List<Vector2> O)
        {
            const double DASH = 1.0, GAP = 0.7, PERIOD = DASH + GAP;
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
                double Ls = cum[seg + 1] - cum[seg], t = Ls > 1e-9 ? Math.Min(1, Math.Max(0, (s - cum[seg]) / Ls)) : 0;
                return new[] { pts[2 * seg] + (pts[2 * seg + 2] - pts[2 * seg]) * t, pts[2 * seg + 1] + (pts[2 * seg + 3] - pts[2 * seg + 1]) * t };
            };
            Action<double, double, double, double> quad = (sx, sz, ex, ez) =>
            {
                double Ls = Hyp(ex - sx, ez - sz);
                if (Ls < 1e-6) return;
                double mx = (sx + ex) / 2, mz = (sz + ez) / 2;
                if (Hyp(mx - ax, mz - az) <= 2.4 || Hyp(mx - bx, mz - bz) <= 2.4) return;
                if (!Geo.InSea(mx, mz)) return;
                float px = (float)(-(ez - sz) / Ls), pz = (float)((ex - sx) / Ls);
                float ys = (float)Math.Max(0.13, Model.SurfaceY(sx, sz) + 0.12), ye = (float)Math.Max(0.13, Model.SurfaceY(ex, ez) + 0.12);
                var S = new Vector3((float)sx, ys, (float)sz); var E = new Vector3((float)ex, ye, (float)ez);
                var pl = new Vector2(px, pz); var mi = new Vector2(-px, -pz);
                P.Add(S); P.Add(E); P.Add(E); O.Add(pl); O.Add(pl); O.Add(mi);
                P.Add(S); P.Add(E); P.Add(S); O.Add(pl); O.Add(mi); O.Add(mi);
            };
            int nd = (int)Math.Ceiling(total / PERIOD);
            for (int d = 0; d < nd; d++)
            {
                double s0 = d * PERIOD, s1 = Math.Min(total, s0 + DASH);
                if (s1 <= s0) break;
                var p = at(s0);
                int seg0 = seg;
                for (int k = seg0 + 1; k < n - 1 && cum[k] < s1; k++) { quad(p[0], p[1], pts[2 * k], pts[2 * k + 1]); p = new[] { pts[2 * k], pts[2 * k + 1] }; }
                var e = at(s1);
                quad(p[0], p[1], e[0], e[1]);
            }
        }

        // ------------------------------------------------------ 城池 --
        // 区块（60 单位）→ 近景细致模型（砖石材质，投影）/ 远景简化模型（LowPoly）；Update 中按镜头距离切换
        sealed class CityTile { public MeshBuilder near = new MeshBuilder(), far = new MeshBuilder(); public readonly List<GameObject> nearGo = new List<GameObject>(), farGo = new List<GameObject>(); public double cx, cz; public int lod = 1; }
        IEnumerator GenCities(GameState g)
        {
            int n = g.cities.Count;
            const double tileSize = 60; var T = Spec.Terrain;
            var bodies = new Dictionary<long, CityTile>();
            const int BodyVerts = 24000;
            Action<CityTile, bool> flushBody = (tile, isNear) =>
            {
                var mb = isNear ? tile.near : tile.far;
                if (mb.Count == 0) return;
                var go = Obj(isNear ? "Cities" : "CitiesFar", BuilderMesh(mb, isNear ? "cities" : "citiesFar"), isNear ? Art.MasonryMaterial() : Art.LowPoly, isNear, isNear);
                go.SetActive(!isNear);
                (isNear ? tile.nearGo : tile.farGo).Add(go);
                if (isNear) tile.near = new MeshBuilder(); else tile.far = new MeshBuilder();
            };
            if (flagMesh == null) flagMesh = FlagMesh();
            flagMat = TrackMat(new Material(Art.LowPoly) { enableInstancing = false });
            // 势力光圈
            var ringPos = new List<Vector3>(); var ringUv = new List<Vector2>(); var ringIdx = new List<int>();
            ringStart.Clear();
            int k = 0;
            foreach (var c in g.cities)
            {
                float mx = (float)Model.CityX[c.id], mz = (float)Model.CityZ[c.id];
                float y = Mathf.Max(0.3f, (float)Model.SurfaceY(Model.CityX[c.id], Model.CityZ[c.id]));
                float size = TerrainModel.CitySize(c);
                // 城池模型按文化（CultureArt）；都城变体：汉地为洛阳、长安，其他文化为各势力的初始都城
                string culture = CultureArt.CultureOfCity(c);
                bool capital = CultureArt.IsCapital(c, culture);
                float yaw = (c.id * 37) % 20 - 10;
                // 城体：烘焙到所在区块的合并网格中（平移 × 绕 y 旋转）
                long ti = (long)Math.Floor((mx - T.x0) / tileSize), tj = (long)Math.Floor((mz - T.z0) / tileSize), tk = ti * 100000 + tj;
                CityTile tile;
                if (!bodies.TryGetValue(tk, out tile)) { bodies[tk] = tile = new CityTile { cx = T.x0 + (ti + 0.5) * tileSize, cz = T.z0 + (tj + 0.5) * tileSize }; cityTiles.Add(tile); }
                var xfm = Matrix4x4.TRS(new Vector3(mx, y, mz), Quaternion.Euler(0, yaw, 0), Vector3.one);
                tile.near.M = xfm; CultureArt.CityInto(tile.near, size, culture, capital, c); tile.near.M = Matrix4x4.identity;
                tile.far.M = xfm; CultureArt.CityFarInto(tile.far, size, culture, capital); tile.far.M = Matrix4x4.identity;
                if (tile.near.Count >= BodyVerts) { Sub(0.8f * k / n); yield return null; flushBody(tile, true); }
                if (tile.far.Count >= BodyVerts) flushBody(tile, false);

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

                Cities[c.id] = new CityVisual { city = c, go = go, flag = fr, ring = null, labelAnchor = anchor, pos = new Vector3(mx, y, mz), size = size, mapX = mx, mapY = mz };
                Sub(0.8f * (++k) / n);
                yield return null;
            }
            foreach (var tile in cityTiles) { flushBody(tile, true); yield return null; flushBody(tile, false); yield return null; }

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
            var col = new List<Color>(pos.Count);
            for (int k = 0; k < pos.Count; k++) col.Add(Color.white);
            mesh.Clear();
            mesh.SetVertices(pos); mesh.SetUVs(0, uv); mesh.SetColors(col); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds();
            return uv.ToArray();
        }

        // ------------------------------------------------------ 云 --
        // 柔边的公告板云团（每朵 5–8 片朝向镜头的面片，共用一张程序生成的云团贴图）；不投影；位置 y 26–34；
        // 战略视角（镜头低于约 95）整体隐去，只在标题 / 全图的远景里出现；移动设备减半
        IEnumerator BuildClouds()
        {
            var rnd = new SeededRandom(5);
            bool world = Region == "world";
            cloudBox = world;
            int n = Application.isMobilePlatform ? (Spec.Clouds + 1) / 2 : Spec.Clouds;
            var shader = Art.LoadShader("Cloud"); var tex = MapVeg.CloudTexture();
            for (int i = 0; i < n; i++)
            {
                int puffs = 5 + rnd.Next(4);
                var P = new List<Vector3>(); var Cn = new List<Vector2>(); var SS = new List<Vector2>(); var idx = new List<int>();
                for (int k = 0; k < puffs; k++)
                {
                    double u = puffs > 1 ? (double)k / (puffs - 1) : 0.5;
                    double ox = (u - 0.5) * 8 + (rnd.NextDouble() - 0.5) * 1.6, oz = (rnd.NextDouble() - 0.5) * 3.2;
                    double mid = 1 - Math.Abs(u - 0.5) * 2;
                    double r = 2.2 + mid * 1.6 + rnd.NextDouble() * 0.9, oy = 0.2 + mid * 1.1 + rnd.NextDouble() * 0.5;
                    double shadeK = 0.9 + rnd.NextDouble() * 0.1;
                    int b = P.Count;
                    foreach (var cn in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                    {
                        P.Add(new Vector3((float)ox, (float)oy, (float)oz)); Cn.Add(cn); SS.Add(new Vector2((float)r, (float)shadeK));
                    }
                    idx.Add(b); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
                }
                var mesh = new Mesh { name = "cloud" };
                mesh.SetVertices(P); mesh.SetUVs(0, Cn); mesh.SetUVs(1, SS); mesh.SetTriangles(idx, 0);
                mesh.bounds = new Bounds(new Vector3(0, 1, 0), new Vector3(24, 24, 24));
                meshes.Add(mesh);
                var mat = TrackMat(new Material(shader) { name = "CloudBillboard" });
                mat.mainTexture = tex;
                var go = Obj("Cloud", mesh, mat, false, false);
                float px, py, pz;
                if (world)
                {
                    var c = ViewCenter();
                    px = c.x + (float)(rnd.NextDouble() * 2 - 1) * CloudBoxX; pz = c.y + (float)(rnd.NextDouble() * 2 - 1) * CloudBoxZ;
                }
                else { px = (float)rnd.NextDouble() * MapW; pz = (float)rnd.NextDouble() * MapH; }
                py = 26 + (float)rnd.NextDouble() * 8;
                go.transform.position = new Vector3(px, py, pz);
                clouds.Add(new Cloud { tr = go.transform, rend = go.GetComponent<Renderer>(), mat = mat });
                yield return null;
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
            selectAngle = 0;
        }

        // ------------------------------------------------------ 草丛与野花 --
        const int GRASS_MAX = 3600, FLOWER_MAX = 500;
        void InitGrass() { grassL = new InstList(); flowerL = new InstList(); grassKey = null; grassJob = null; }
        void UpdateGrass(Camera cam)
        {
            if (grassL == null || Model == null || Model.CData == null) return;
            var vc = ViewCenter();
            var cp = cam.transform.position;
            double dist = Math.Sqrt((cp.x - vc.x) * (cp.x - vc.x) + cp.y * cp.y + (cp.z - vc.y) * (cp.z - vc.y));
            if (dist > 82) { if (grassL.Count > 0 || flowerL.Count > 0) { grassL.Clear(); flowerL.Clear(); } grassKey = null; grassJob = null; return; }
            double R = WorldGeo.Clamp(dist * 0.62, 12, 34);
            string key = WorldGeo.JsRound(vc.x / 4.0) + "," + WorldGeo.JsRound(vc.y / 4.0) + "," + WorldGeo.JsRound(R / 4);
            if (key != grassKey) { grassKey = key; grassJob = GrassFill(vc.x, vc.y, R); }
            // 重新铺设分摊到几帧（每帧约 2 ms）
            if (grassJob == null) return;
            double t0 = NowMs();
            while (NowMs() - t0 < 2) { if (!grassJob.MoveNext()) { grassJob = null; break; } }
        }
        static readonly float[][] FlowerCols = { new[] { 0.98f, 0.96f, 0.9f }, new[] { 1f, 0.86f, 0.3f }, new[] { 0.92f, 0.3f, 0.26f }, new[] { 0.72f, 0.5f, 0.9f }, new[] { 0.98f, 0.62f, 0.78f }, new[] { 0.5f, 0.62f, 1f } };
        // 逐格的伪随机数（= 网页版 _grassFill 里的 rn 闭包；用 ref 传状态，不为每格分配委托）
        static double GrassRn(ref int hsh)
        {
            hsh = unchecked((hsh ^ (int)((uint)hsh >> 15)) * 0x2c1b3c6d);
            hsh ^= (int)((uint)hsh >> 12);
            return ((uint)hsh % 10000) / 10000.0;
        }
        // 铺设用的临时表（各次铺设共用；新的铺设开始时旧的已被放弃，不会同时进行）
        readonly List<Matrix4x4> grassG = new List<Matrix4x4>(), grassF = new List<Matrix4x4>();
        readonly List<Vector4> grassGC = new List<Vector4>(), grassFC = new List<Vector4>();
        // 镜头附近的草丛与野花（每格 0.8，按群落与坡度取舍）；完成后一次换上新实例
        IEnumerator GrassFill(double cx0, double cz0, double R)
        {
            var M = Model; const double cell = 0.8;
            var G = grassG; var GC = grassGC; var F = grassF; var FC = grassFC;
            G.Clear(); GC.Clear(); F.Clear(); FC.Clear();
            double work = 0;
            int i0 = (int)Math.Floor((cx0 - R) / cell), i1 = (int)Math.Ceiling((cx0 + R) / cell), j0 = (int)Math.Floor((cz0 - R) / cell), j1 = (int)Math.Ceiling((cz0 + R) / cell);
            for (int i = i0; i <= i1 && G.Count < GRASS_MAX; i++)
            {
                for (int j = j0; j <= j1 && G.Count < GRASS_MAX; j++)
                {
                    if (work > 160) { work = 0; yield return null; }
                    int hsh = unchecked((int)(long)(i * 73856093L) ^ (int)(long)(j * 19349663L)) ;
                    hsh = unchecked(hsh * (int)0x9e3779b1u);
                    double x = (i + GrassRn(ref hsh)) * cell, z = (j + GrassRn(ref hsh)) * cell;
                    double d = Hyp(x - cx0, z - cz0);
                    work += 0.08;
                    if (d > R) continue;
                    int ti = WorldGeo.JsRound((x - M.GX0) / M.GStep), tj = WorldGeo.JsRound((z - M.GZ0) / M.GStep);
                    if (ti < 0 || tj < 0 || ti >= M.TexW || tj >= M.TexH) continue;
                    int q = (tj * M.TexW + ti) * 4;
                    double sand = M.MData[q] / 255.0, snow = M.MData[q + 2] / 255.0, farm = 1 - M.MData[q + 3] / 255.0, forest = M.CData[q + 3] / 255.0;
                    double p = (1 - sand) * (1 - snow) * (1 - farm * 0.85) * (1 - forest * 0.55);
                    if (p < 0.15 || GrassRn(ref hsh) > p * 0.8) continue;
                    work++;
                    double h = M.SurfaceY(x, z);
                    if (h < 0.5 || h > 3.4) continue;
                    double sl = Math.Abs(M.SurfaceY(x + 0.4, z) - h) + Math.Abs(M.SurfaceY(x, z + 0.4) - h);
                    if (sl > 0.5) continue;
                    double fade = 1 - TerrainModel.SS(R * 0.72, R, d);
                    double s = (0.75 + GrassRn(ref hsh) * 0.6) * (0.35 + 0.65 * fade) * (1 + forest * 0.3), yaw = GrassRn(ref hsh) * 6.283;
                    double kk = 0.92 + GrassRn(ref hsh) * 0.3;
                    bool flower = GrassRn(ref hsh) < 0.075 * (1 - forest) && F.Count < FLOWER_MAX;
                    var mt = InstMatrix(x, h - 0.02, z, s, s, yaw);
                    if (flower) { var fc = FlowerCols[((uint)hsh >> 3) % FlowerCols.Length]; F.Add(mt); FC.Add(new Vector4(fc[0], fc[1], fc[2], 1)); }
                    else
                    {
                        G.Add(mt);
                        GC.Add(new Vector4((float)Math.Min(1, M.CData[q] / 255.0 * kk * 1.08), (float)Math.Min(1, M.CData[q + 1] / 255.0 * kk * 1.1), (float)Math.Min(1, M.CData[q + 2] / 255.0 * kk), 1));
                    }
                }
            }
            grassL.Clear(); flowerL.Clear();
            for (int k = 0; k < G.Count; k++) grassL.Add(G[k], GC[k]);
            for (int k = 0; k < F.Count; k++) flowerL.Add(F[k], FC[k]);
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
            if (!(dt >= 0)) dt = 0;
            time += dt;
            // 选择光圈：贴图按 1 ± 0.06 缩放脉动并缓慢旋转
            if (selectRing != null && selectRing.activeSelf && selectUv0 != null)
            {
                float s = 1 + Mathf.Sin(time * 3f) * 0.06f;
                selectAngle += dt * 25;
                float ca = Mathf.Cos(-selectAngle * Mathf.Deg2Rad), sa = Mathf.Sin(-selectAngle * Mathf.Deg2Rad);
                for (int i = 0; i < selectUv0.Length; i++)
                {
                    float u = (selectUv0[i].x - 0.5f) / s, v = (selectUv0[i].y - 0.5f) / s;
                    selectUv[i] = new Vector2(0.5f + u * ca - v * sa, 0.5f + u * sa + v * ca);
                }
                selectMesh.uv = selectUv;
            }
            var cam = ViewCam;
            bool vis = Root.gameObject.activeInHierarchy;
            UpdateClouds(cam, dt);
            if (cam != null && vis)
            {
                // 地形分块：选择可见块、补建细块（每帧最多 3 ms；大量缺块时——如镜头瞬移——放宽到 8 ms）
                if (lod != null) lod.Update(cam, lod.QueueCount > 24 ? 8 : 3);
                UpdateInstLOD(cam);
                UpdateGrass(cam);
                DrawInstances(cam);
            }
            // 世界地图：镜头拉远时雾随之推远。RenderSettings 的雾是全局的：地图隐藏时（战斗及其中的交锋 / 单挑）
            // 只恢复一次原值，之后不再写，免得盖掉交锋、单挑自己设的雾距
            if (Region == "world" && cam != null && RenderSettings.fog && vis)
            {
                if (!fogSaved) { fogSaved = true; fogStart0 = RenderSettings.fogStartDistance; fogEnd0 = RenderSettings.fogEndDistance; }
                float k = Mathf.Max(1, cam.transform.position.y / 105f);
                RenderSettings.fogStartDistance = fogStart0 * k; RenderSettings.fogEndDistance = fogEnd0 * k;
            }
            else if (fogSaved && !vis) RestoreFog();
        }
        void UpdateClouds(Camera cam, float dt)
        {
            Vector2 vc = cloudBox && clouds.Count > 0 ? ViewCenter() : Vector2.zero;
            // 镜头高度低于约 95（战略视角）时云整体淡出
            float cloudG = cam != null ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((cam.transform.position.y - CloudHideY) / CloudShowDY)) : 1;
            foreach (var c in clouds)
            {
                var p = c.tr.position;
                p.x += 0.6f * dt; p.z += 0.15f * dt;
                if (cloudBox)
                {
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
                    float a = f * f * (3 - 2 * f) * cloudG;
                    c.mat.SetFloat("_Opacity", a);
                    bool shown = a > 0.01f;
                    if (c.rend.enabled != shown) c.rend.enabled = shown;
                }
            }
        }
        // 树木 / 石块 / 村舍：按区块离镜头的距离切换近景（平滑）与远景（简化）；镜头很高时整类隐去；城池近景 / 远景模型
        void UpdateInstLOD(Camera cam)
        {
            var cp = cam.transform.position;
            double cx = cp.x, cy = cp.y, cz = cp.z;
            bool hideTrees = cy > (treesHidden ? 165 : 180);
            treesHidden = hideTrees;
            bool hideSmall = cy > 120;
            // 镜头移动约 3 单位以上才重新分配（每次只改动跨越边界的区块）；每帧最多约 2.5 ms
            string key = WorldGeo.JsRound(cx / 4) + "," + WorldGeo.JsRound(cz / 4) + "," + WorldGeo.JsRound(cy / 4);
            double t0 = NowMs();
            foreach (var t in treeTiles)
            {
                if (!hideTrees && t.key != key && NowMs() - t0 > 2.5) { t.hidden = false; continue; }
                TreeTileLOD(t, hideTrees, 42, key, cx, cy, cz);
            }
            foreach (var t in scatter)
            {
                if (!hideSmall && t.key != key && NowMs() - t0 > 2.5) { t.hidden = false; continue; }
                t.hidden = hideSmall;
                if (hideSmall || t.key == key) continue;
                t.key = key;
                const double R = 50;
                double dh = Hyp(t.cx - cx, t.cz - cz);
                double dmin = Hyp(Math.Max(0, dh - t.r), cy), dmax = Hyp(dh + t.r, cy);
                if (dmin > R) { if (t.mode != "far") { t.farL.Clear(); for (int i = 0; i < t.n; i++) t.farL.Add(t.mats[i], t.cols[i]); t.nearL.Clear(); t.mode = "far"; } continue; }
                if (dmax < R) { if (t.mode != "near") { t.nearL.Clear(); for (int i = 0; i < t.n; i++) t.nearL.Add(t.mats[i], t.cols[i]); t.farL.Clear(); t.mode = "near"; } continue; }
                // 区块跨越近景边界：逐实例分配
                t.nearL.Clear(); t.farL.Clear();
                double R2 = R * R - cy * cy;
                for (int i = 0; i < t.n; i++)
                {
                    double dx = t.xz[i * 2] - cx, dz = t.xz[i * 2 + 1] - cz;
                    if (dx * dx + dz * dz < R2) t.nearL.Add(t.mats[i], t.cols[i]); else t.farL.Add(t.mats[i], t.cols[i]);
                }
                t.mode = "split";
            }
            // 城池：近景细致模型 / 远景简化模型
            foreach (var t in cityTiles)
            {
                double d = Math.Sqrt((t.cx - cx) * (t.cx - cx) + (t.cz - cz) * (t.cz - cz) + cy * cy);
                int l = d < (t.lod == 0 ? 92 : 98) ? 0 : 1;
                if (l == t.lod) continue;
                t.lod = l;
                foreach (var go in t.nearGo) go.SetActive(l == 0);
                foreach (var go in t.farGo) go.SetActive(l == 1);
            }
        }
        // 树木区块：镜头 R 以内的树用平滑模型（按树种分到各近景组），18 以内用特写模型，其余写入公告板组
        const double TREE_HERO_R = 18;
        void TreeTileLOD(TreeTile t, bool hide, double R, string key, double cx, double cy, double cz)
        {
            t.hidden = hide;
            if (hide || t.key == key) return;
            t.key = key;
            double dh = Hyp(t.cx - cx, t.cz - cz);
            bool allFar = Hyp(Math.Max(0, dh - t.r), cy) > R;
            if (allFar && t.mode == "far") return;
            double R2 = R * R - cy * cy;
            bool anyHero = Hyp(Math.Max(0, dh - t.r), cy) < TREE_HERO_R; double RH2 = TREE_HERO_R * TREE_HERO_R - cy * cy;
            foreach (var l in t.nearL) l.Clear();
            foreach (var l in t.heroL) l.Clear();
            t.farL.Clear();
            for (int i = 0; i < t.n; i++)
            {
                double dx = t.xz[i * 2] - cx, dz = t.xz[i * 2 + 1] - cz;
                if (!allFar && dx * dx + dz * dz < R2)
                {
                    int g = t.kind[i];
                    if (anyHero && dx * dx + dz * dz < RH2)
                    {
                        if (t.hero[g] == null) t.hero[g] = MapVeg.TreeGeometry(t.kinds[g], 2);
                        t.heroL[g].Add(t.mats[i], t.cols[i]);
                    }
                    else t.nearL[g].Add(t.mats[i], t.cols[i]);
                }
                else t.farL.Add(t.smats[i], t.cols[i], t.cells[i]);
            }
            t.mode = allFar ? "far" : "split";
        }
        // 每帧提交实例化绘制（区块级视锥剔除）
        void DrawInstances(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            int layer = Root.gameObject.layer;
            var leaf = Art.Foliage("leaf"); var sprite = MapVeg.SpriteMaterial(); var quad = MapVeg.SpriteQuad();
            foreach (var t in treeTiles)
            {
                if (t.hidden || !GeometryUtility.TestPlanesAABB(frustum, t.bounds)) continue;
                for (int g = 0; g < t.near.Length; g++)
                {
                    t.nearL[g].Draw(t.near[g], leaf, ShadowCastingMode.On, true, layer, cam);
                    if (t.hero[g] != null) t.heroL[g].Draw(t.hero[g], leaf, ShadowCastingMode.On, true, layer, cam);
                }
                t.farL.Draw(quad, sprite, ShadowCastingMode.Off, false, layer, cam);
            }
            foreach (var t in scatter)
            {
                if (t.hidden || !GeometryUtility.TestPlanesAABB(frustum, t.bounds)) continue;
                t.nearL.Draw(t.near, t.mat, t.shadow ? ShadowCastingMode.On : ShadowCastingMode.Off, true, layer, cam);
                t.farL.Draw(t.far, t.mat, ShadowCastingMode.Off, true, layer, cam);
            }
            if (grassL != null)
            {
                var gm = Art.Foliage("grass");
                grassL.Draw(MapVeg.TuftGeometry(), gm, ShadowCastingMode.Off, true, layer, cam);
                flowerL.Draw(MapVeg.FlowerGeometry(), gm, ShadowCastingMode.Off, true, layer, cam);
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
                double x, z;
                if (lane == null) { x = a.mapX + (b.mapX - a.mapX) * k; z = a.mapY + (b.mapY - a.mapY) * k; }
                else
                {
                    double Ln = cum[cum.Length - 1] * k;
                    int s = 1;
                    while (s < cum.Length - 1 && cum[s] < Ln) s++;
                    double tt = cum[s] > cum[s - 1] ? (Ln - cum[s - 1]) / (cum[s] - cum[s - 1]) : 0;
                    double x0 = lane[2 * s - 2], z0 = lane[2 * s - 1], x1 = lane[2 * s], z1 = lane[2 * s + 1];
                    x = x0 + (x1 - x0) * tt; z = z0 + (z1 - z0) * tt;
                    go.transform.rotation = Quaternion.Euler(0, (float)(Math.Atan2(x1 - x0, z1 - z0) * 180 / Math.PI), 0);
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

        // ------------------------------------------------------ 调试 --
        // 各类网格的三角形数（按对象名累计；实例组 = 几何 × 当前实例数），与网页版测试页的统计口径相同
        public Dictionary<string, int> DebugTriangles()
        {
            var d = new Dictionary<string, int>();
            Action<string, int> add = (k, v) => { int c; d.TryGetValue(k, out c); d[k] = c + v; };
            if (Root != null)
            {
                Action<Transform> walk = null;
                walk = tr =>
                {
                    var mf = tr.gameObject.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) add(tr.gameObject.name == "Flag" ? "Flags" : tr.gameObject.name, mf.sharedMesh.triangles.Length / 3);
                    for (int i = 0; i < tr.childCount; i++) walk(tr.GetChild(i));
                };
                walk(Root);
            }
            foreach (var t in treeTiles)
            {
                for (int g = 0; g < t.near.Length; g++)
                {
                    add("Trees", t.nearL[g].Count * t.near[g].triangles.Length / 3);
                    if (t.hero[g] != null) add("TreesHero", t.heroL[g].Count * t.hero[g].triangles.Length / 3);
                }
                add("TreeSprites", t.farL.Count * 2);
            }
            foreach (var t in scatter) { add(t.name, t.nearL.Count * t.near.triangles.Length / 3); add(t.name + "Far", t.farL.Count * t.far.triangles.Length / 3); }
            if (grassL != null) { add("Grass", grassL.Count * MapVeg.TuftGeometry().triangles.Length / 3); add("Flowers", flowerL.Count * MapVeg.FlowerGeometry().triangles.Length / 3); }
            return d;
        }
        // 测试：植被几何的顶点数（与网页版测试页的口径相同：先把镜头贴近第一个树木区块的中心，建出特写网格）
        public List<string> DebugVeg()
        {
            var L = new List<string>();
            var t0 = treeTiles.Count > 0 ? treeTiles[0] : null;
            if (t0 != null) TreeTileLOD(t0, false, 42, "refs", t0.cx, 2, t0.cz);
            var seen = new HashSet<string>();
            foreach (var t in treeTiles)
                for (int k = 0; k < t.kinds.Length; k++)
                    if (seen.Add(t.kinds[k])) L.Add("veg tree " + t.kinds[k] + " 0 " + t.near[k].vertexCount);
            if (t0 != null) for (int k = 0; k < t0.kinds.Length; k++) if (t0.hero[k] != null) L.Add("veg tree " + t0.kinds[k] + " 2 " + t0.hero[k].vertexCount);
            var sc = new HashSet<string>();
            foreach (var t in scatter)
                if (sc.Add(t.name + ":" + t.near.vertexCount)) L.Add("veg " + t.name + " " + t.near.vertexCount + " 1 " + t.far.vertexCount);
            L.Add("veg grass tuft 0 " + MapVeg.TuftGeometry().vertexCount);
            L.Add("veg grass flower 0 " + MapVeg.FlowerGeometry().vertexCount);
            return L;
        }
        // 测试：区块的实例数（树木 / 小物件）与每帧提交的实例化绘制
        public int DebugTreeTiles { get { return treeTiles.Count; } }
        public int DebugScatterTiles { get { return scatter.Count; } }
        public int DebugGrassCount { get { return grassL != null ? grassL.Count + flowerL.Count : 0; } }
        // 测试：地形四叉树（各级各状态的块数、已建块的高度校验和）与当前镜头下的选择
        public Dictionary<string, int> DebugLodCensus(out int roots, out int samples, out double checksum)
        {
            roots = lod != null ? lod.roots.Count : 0;
            if (lod == null) { samples = 0; checksum = 0; return new Dictionary<string, int>(); }
            return lod.Census(out samples, out checksum);
        }
        public int DebugLodShown { get { return lod != null ? lod.statNodes : 0; } }
        // 测试：在一份新的四叉树上预建到第 2 级（与构建时相同），返回各级各状态的块数与高度校验和
        public Dictionary<string, int> DebugFreshLod(out int roots, out int samples, out double checksum)
        {
            var M = Model; var S = Spec.Skirt;
            double X1 = M.GX0 + M.GNx * M.GStep, Z1 = M.GZ0 + M.GNz * M.GStep;
            var l = new TerrainLOD(this, terrainMat);
            l.Init(M.GX0, M.GZ0, S.x0, S.z0, S.x1, S.z1, M.GX0, M.GZ0, X1, Z1);
            var it = l.Prebuild(2); while (it.MoveNext()) { }
            roots = l.roots.Count;
            var d = l.Census(out samples, out checksum);
            Action<LodNode> rec = null;
            rec = nd => { if (nd.go != null) Destroy(nd.go); if (nd.children != null) foreach (var c in nd.children) rec(c); };
            foreach (var r in l.roots) rec(r);
            l.Dispose();
            return d;
        }

        // ------------------------------------------------------ 释放 --
        void Dispose()
        {
            if (Root != null) Destroy(Root.gameObject);
            Root = null;
            if (lod != null) { lod.Dispose(); lod = null; }
            foreach (var m in meshes) if (m != null) Destroy(m);
            foreach (var m in mats) if (m != null) Destroy(m);
            foreach (var t in texs) if (t != null) Destroy(t);
            meshes.Clear(); mats.Clear(); texs.Clear(); clouds.Clear(); lanes.Clear(); ringStart.Clear();
            treeTiles.Clear(); scatter.Clear(); cityTiles.Clear(); Passes.Clear(); PassesSkipped.Clear();
            Cities.Clear();
            maps = null; terrainMat = null; grassL = null; flowerL = null; grassJob = null; grassKey = null;
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

    // ================================================================ 植被几何与贴图 ==
    // ← 网页版 map-view.js 的 treeGeometry / rockGeometry / tuftGeometry / flowerGeometry / houseGeometry / spriteTexture /
    //    cloudTexture / roadTexture / bridgeInto / passInto。几何体缓存共用（名称以 "veg:" 开头，不随地图释放）。
    // 顶点色 a = sgLeaf：0 = 保持原色（树干、墙体），1 = 乘实例颜色（叶片、屋顶）
    public static class MapVeg
    {
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static LP L(float r, float y, bool sharp = false) { return new LP(r, y, sharp); }
        static readonly Color TRUNK = new Color(0.38f, 0.27f, 0.16f), BIRCH_BARK = new Color(0.86f, 0.84f, 0.78f);
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        // 合并「保持原色」与「乘实例色」两部分为一个网格（sgLeaf 写在顶点色的 a）
        static Mesh MergeLeaf(MeshBuilder keep, MeshBuilder tint, string name)
        {
            var m = new Mesh { name = "veg:" + name };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            Action<MeshBuilder, float> add = (mb, leaf) =>
            {
                if (mb == null) return;
                int b = v.Count;
                v.AddRange(mb.v); n.AddRange(mb.n);
                foreach (var col in mb.c) c.Add(new Color(col.r, col.g, col.b, leaf));
                foreach (int i in mb.t) t.Add(b + i);
            };
            add(keep, 0); add(tint, 1);
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        // 树种（高约 1）。lod 0 = 近景（平滑细分），1 = 远景（简化），2 = 特写（二级细分的树冠 + 三团叶簇）
        // broad 圆冠阔叶 | broad2 双团阔叶 | pine 松 | spruce 云杉 / 杉 | cypress 柏 | poplar 杨 | palm 棕榈 | acacia 金合欢 | shrub 灌丛
        // willow 柳 | olive 橄榄 | bamboo 竹 | birch 桦 | stonepine 伞松
        public static Mesh TreeGeometry(string kind, int lod)
        {
            string key = kind + ":" + lod;
            Mesh cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            var tk = new MeshBuilder(); var lf = new MeshBuilder();
            Color W = new Color(0.93f, 0.93f, 0.93f), W2 = new Color(0.82f, 0.82f, 0.82f);
            bool hero = lod == 2;
            int d = hero ? 2 : lod != 0 ? 0 : 1, ts = hero ? 9 : lod != 0 ? 4 : 6, cs = hero ? 18 : lod != 0 ? 6 : 10;
            var O = Vector3.zero;
            Action<Vector3, Vector3, Color, int, float> crown = (c, r, col, seed, ro) =>
            {
                // 法线向椭球法线混合（中远景的树冠轮廓仍有起伏，明暗却不再显出一块块小面）
                lf.Sphere(c, r, col, d, new SphereOpt { seed = seed, rough = ro, bottomShade = 0.32f, top = 0.08f, smoothN = hero ? 0.4f : 0.75f });
                if (!hero || r.x < 0.12f) return;
                for (int k = 0; k < 3; k++)
                {
                    float a = seed * 1.7f + k * 2.09f, up = 0.25f + 0.3f * ((k + seed) % 2);
                    var cc = V(c.x + Mathf.Cos(a) * r.x * 0.62f, c.y + up * r.y, c.z + Mathf.Sin(a) * r.z * 0.62f);
                    lf.Sphere(cc, V(r.x * 0.5f, r.y * 0.52f, r.z * 0.5f), k == 1 ? W2 : W, 1, new SphereOpt { seed = seed + 7 * k + 1, rough = ro * 0.8f, bottomShade = 0.28f, top = 0.1f });
                }
            };
            Action<float, float, Color> trunk = (h, r0, col) => tk.Lathe(O, new[] { L(r0, 0), L(r0 * 0.7f, h) }, ts, col);
            Action<float, float, float, Color> tier = (y, r, h, col) => lf.Lathe(V(0, y, 0), hero
                ? new[] { L(0, h * 0.12f), L(r * 0.9f, h * 0.035f), L(r, 0, true), L(r * 0.72f, h * 0.2f), L(r * 0.46f, h * 0.5f), L(r * 0.2f, h * 0.8f), L(0, h) }
                : new[] { L(0, h * 0.1f), L(r, 0, true), L(r * 0.45f, h * 0.55f), L(0, h) }, cs, col);
            if (lod == 1)
            {
                // 远景：极简形体（无树干；十几到二十几个三角形）
                Action<Vector3, Vector3> blob = (c, r) => lf.Sphere(c, r, W, 0, new SphereOpt { bottomShade = 0.3f, top = 0.1f });
                Action<float, float, float> spire = (y, r, h) => lf.Lathe(V(0, y, 0), new[] { L(r, 0, true), L(0, h) }, 5, W);
                switch (kind)
                {
                    case "pine": spire(0.18f, 0.42f, 1.0f); break;
                    case "spruce": spire(0.1f, 0.34f, 1.1f); break;
                    case "cypress": lf.Lathe(V(0, 0.08f, 0), new[] { L(0.17f, 0.15f), L(0.16f, 0.7f), L(0, 1.25f) }, 5, W); break;
                    case "poplar": blob(V(0, 0.75f, 0), V(0.17f, 0.55f, 0.17f)); break;
                    case "birch": blob(V(0, 0.8f, 0), V(0.27f, 0.42f, 0.27f)); break;
                    case "shrub": blob(V(0, 0.13f, 0), V(0.3f, 0.2f, 0.3f)); break;
                    case "acacia": case "stonepine": blob(V(0, kind == "acacia" ? 0.66f : 0.9f, 0), V(0.48f, 0.19f, 0.44f)); break;
                    case "bamboo": blob(V(0, 0.62f, 0), V(0.22f, 0.42f, 0.22f)); break;
                    case "palm":
                        {
                            var top = V(0.2f, 0.92f, 0); var upv = Vector3.up;
                            tk.Lathe(V(0, 0, 0), new[] { L(0.05f, 0), L(0.04f, 0.5f) }, 3, new Color(0.5f, 0.4f, 0.27f));
                            tk.Lathe(V(0.06f, 0.5f, 0), new[] { L(0.04f, 0), L(0.035f, 0.42f) }, 3, new Color(0.5f, 0.4f, 0.27f));
                            for (int k = 0; k < 5; k++)
                            {
                                float a = k * 1.2566f + 0.3f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                                lf.TriN(top, V(top.x + ca * 0.5f - sa * 0.1f, top.y - 0.2f, top.z + sa * 0.5f + ca * 0.1f), V(top.x + ca * 0.5f + sa * 0.1f, top.y - 0.2f, top.z + sa * 0.5f - ca * 0.1f), upv, upv, upv, W);
                            }
                            break;
                        }
                    default: blob(V(0, 0.66f, 0), V(0.44f, 0.4f, 0.44f)); break;
                }
                var g1 = MergeLeaf(tk, lf, key);
                cache[key] = g1;
                return g1;
            }
            switch (kind)
            {
                case "broad": trunk(0.42f, 0.065f, TRUNK); crown(V(0, 0.66f, 0), V(0.44f, 0.4f, 0.44f), W, 3, 0.09f); break;
                case "broad2":
                    trunk(0.4f, 0.065f, TRUNK);
                    crown(V(0.06f, 0.6f, 0), V(0.38f, 0.34f, 0.38f), W, 5, 0.09f);
                    crown(V(-0.15f, 0.8f, 0.06f), V(0.28f, 0.25f, 0.28f), W2, 9, 0.09f);
                    break;
                case "pine": trunk(0.3f, 0.065f, TRUNK); tier(0.2f, 0.44f, 0.62f, W2); tier(0.55f, 0.33f, 0.56f, W); break;
                case "spruce": trunk(0.2f, 0.05f, TRUNK); tier(0.12f, 0.34f, 0.5f, W2); tier(0.42f, 0.27f, 0.46f, W); tier(0.72f, 0.19f, 0.42f, W); break;
                case "cypress": trunk(0.14f, 0.045f, TRUNK); lf.Lathe(V(0, 0.1f, 0), new[] { L(0, 0), L(0.15f, 0.12f), L(0.19f, 0.42f), L(0.13f, 0.85f), L(0.05f, 1.1f), L(0, 1.2f) }, hero ? 16 : 8, W); break;
                case "poplar": trunk(0.24f, 0.05f, TRUNK); crown(V(0, 0.78f, 0), V(0.17f, 0.58f, 0.17f), W, 7, 0.06f); break;
                case "birch": trunk(0.5f, 0.045f, BIRCH_BARK); crown(V(0, 0.82f, 0), V(0.27f, 0.42f, 0.27f), W, 11, 0.12f); break;
                case "shrub": crown(V(0, 0.13f, 0), V(0.3f, 0.2f, 0.3f), W, 17, 0.14f); break;
                case "willow":
                    trunk(0.4f, 0.07f, TRUNK);
                    crown(V(0, 0.72f, 0), V(0.42f, 0.34f, 0.42f), W, 19, 0.09f);
                    lf.Lathe(V(0, 0.18f, 0), new[] { L(0.36f, 0), L(0.45f, 0.25f), L(0.44f, 0.5f, true) }, cs, W2);
                    break;
                case "olive":
                    tk.Tube(new[] { V(0, 0, 0), V(0.05f, 0.2f, 0), V(-0.03f, 0.38f, 0.02f) }, new[] { 0.06f, 0.05f, 0.04f }, ts, TRUNK);
                    crown(V(-0.05f, 0.52f, 0), V(0.34f, 0.24f, 0.32f), W, 23, 0.16f);
                    crown(V(0.14f, 0.44f, 0.1f), V(0.2f, 0.16f, 0.2f), W2, 29, 0.12f);
                    break;
                case "stonepine":
                    tk.Tube(new[] { V(0, 0, 0), V(0.04f, 0.4f, 0), V(0.02f, 0.8f, 0) }, new[] { 0.06f, 0.05f, 0.04f }, ts, TRUNK);
                    crown(V(0.02f, 0.92f, 0), V(0.48f, 0.2f, 0.48f), W, 31, 0.12f);
                    break;
                case "acacia":
                    tk.Tube(new[] { V(0, 0, 0), V(0.04f, 0.3f, 0), V(0.16f, 0.55f, 0.04f) }, new[] { 0.05f, 0.04f, 0.03f }, ts, TRUNK);
                    tk.Tube(new[] { V(0.04f, 0.3f, 0), V(-0.12f, 0.56f, -0.04f) }, new[] { 0.03f, 0.025f }, ts, TRUNK);
                    crown(V(0.02f, 0.66f, 0), V(0.46f, 0.18f, 0.42f), W, 37, 0.16f);
                    crown(V(0.2f, 0.62f, 0.12f), V(0.26f, 0.13f, 0.24f), W2, 39, 0.12f);
                    break;
                case "bamboo":
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * 2.4f, r = k != 0 ? 0.1f : 0, x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r, h = 0.8f + (k % 3) * 0.15f;
                        tk.Lathe(V(x, 0, z), new[] { L(0.022f, 0), L(0.016f, h) }, hero ? 6 : 4, new Color(0.48f, 0.6f, 0.3f));
                        lf.Sphere(V(x * 1.6f, h - 0.08f, z * 1.6f), V(0.14f, 0.26f, 0.14f), k % 2 != 0 ? W : W2, hero ? 1 : 0, new SphereOpt { seed = 41 + k, rough = 0.1f, bottomShade = 0.3f });
                    }
                    break;
                case "palm":
                    {
                        var pts = new[] { V(0, 0, 0), V(0.05f, 0.3f, 0), V(0.13f, 0.62f, 0), V(0.24f, 0.92f, 0) };
                        tk.Tube(pts, new[] { 0.055f, 0.047f, 0.04f, 0.035f }, ts, new Color(0.5f, 0.4f, 0.27f));
                        var top = pts[3]; int nf = hero ? 9 : 7; var upv = Vector3.up;
                        for (int k = 0; k < nf; k++)
                        {
                            float a = k * Mathf.PI * 2 / nf + 0.3f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                            int seg = hero ? 5 : 3;
                            Vector3 prevL = Vector3.zero, prevR = Vector3.zero, prevC = top; bool first = true;
                            for (int s = 1; s <= seg; s++)
                            {
                                float u = (float)s / seg, Lr = 0.56f * u, droop = 0.12f * u - 0.38f * u * u, w = 0.12f * Mathf.Sin(Mathf.PI * Mathf.Min(1, u * 1.15f));
                                var c = V(top.x + ca * Lr, top.y + droop, top.z + sa * Lr);
                                var l = V(c.x - sa * w, c.y - 0.02f, c.z + ca * w); var rr = V(c.x + sa * w, c.y - 0.02f, c.z - ca * w);
                                var nn = V(ca * 0.25f, 1, sa * 0.25f).normalized;
                                var col = k % 2 != 0 ? W : W2;
                                if (first) { lf.TriN(prevC, l, c, upv, nn, nn, col); lf.TriN(prevC, c, rr, upv, nn, nn, col); first = false; }
                                else { lf.TriN(prevL, l, c, nn, nn, nn, col); lf.TriN(prevL, c, prevC, nn, nn, nn, col); lf.TriN(prevC, c, rr, nn, nn, nn, col); lf.TriN(prevC, rr, prevR, nn, nn, nn, col); }
                                prevL = l; prevR = rr; prevC = c;
                            }
                        }
                        crown(V(top.x, top.y - 0.03f, 0), V(0.07f, 0.06f, 0.07f), W2, 3, 0);
                        break;
                    }
            }
            var g = MergeLeaf(tk, lf, key);
            cache[key] = g;
            return g;
        }
        // 石块（两种外形）：整体乘实例颜色
        public static Mesh RockGeometry(int v, int lod)
        {
            string key = "rock" + v + ":" + lod;
            Mesh cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            var mb = new MeshBuilder();
            var g = new Color(0.92f, 0.92f, 0.92f);
            var R = new[] { V(0.34f, 0.22f, 0.28f), V(0.26f, 0.3f, 0.24f), V(0.42f, 0.15f, 0.3f) }[v];
            mb.Sphere(V(0, R.y * 0.55f, 0), R, g, lod != 0 ? 0 : 1, new SphereOpt { seed = v * 13 + 5, rough = 0.24f, flat = -0.45f, top = 0.1f, bottomShade = 0.4f });
            if (lod == 0 && v == 0) mb.Sphere(V(0.27f, 0.07f, 0.12f), V(0.14f, 0.1f, 0.12f), g, 1, new SphereOpt { seed = 77, rough = 0.2f, flat = -0.4f, bottomShade = 0.4f });
            var m = MergeLeaf(null, mb, key);
            cache[key] = m;
            return m;
        }
        // 草丛：六片弯曲的草叶（两面），叶根暗、叶尖亮
        public static Mesh TuftGeometry()
        {
            Mesh cached;
            if (cache.TryGetValue("tuft", out cached)) return cached;
            var mb = new MeshBuilder();
            var rnd = new SeededRandom(91);
            Color dark = new Color(0.5f, 0.5f, 0.5f), lite = new Color(1.06f, 1.06f, 1.0f);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 1.05f + (float)rnd.NextDouble() * 0.6f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float r0 = 0.02f + (float)rnd.NextDouble() * 0.05f, h = 0.13f + (float)rnd.NextDouble() * 0.12f, lean = 0.05f + (float)rnd.NextDouble() * 0.07f, w = 0.026f;
                float bx = ca * r0, bz = sa * r0;
                var b0 = V(bx - sa * w, 0, bz + ca * w); var b1 = V(bx + sa * w, 0, bz - ca * w);
                var tip = V(bx + ca * lean, h, bz + sa * lean);
                var nf = V(ca * 0.3f, 1, sa * 0.3f).normalized; var nb = V(-ca * 0.3f, 1, -sa * 0.3f).normalized;
                mb.TriN(b0, b1, tip, nf, nf, nf, dark, dark, lite);
                mb.TriN(b0, b1, tip, nb, nb, nb, dark, dark, lite);
            }
            var m = MergeLeaf(null, mb, "tuft");
            cache["tuft"] = m;
            return m;
        }
        // 野花：花茎 + 五瓣花（花瓣乘实例颜色）
        public static Mesh FlowerGeometry()
        {
            Mesh cached;
            if (cache.TryGetValue("flower", out cached)) return cached;
            var stem = new MeshBuilder(); var pet = new MeshBuilder(); var up = Vector3.up;
            var green = new Color(0.3f, 0.5f, 0.2f);
            foreach (var q in new[] { new[] { 0, 0, 0.17f }, new[] { 0.07f, 0.04f, 0.13f }, new[] { -0.05f, 0.06f, 0.15f } })
            {
                float x = q[0], z = q[1], h = q[2];
                stem.TriN(V(x - 0.01f, 0, z), V(x + 0.01f, 0, z), V(x, h, z), V(0, 0.3f, 1), V(0, 0.3f, 1), up, green);
                stem.TriN(V(x + 0.01f, 0, z), V(x - 0.01f, 0, z), V(x, h, z), V(0, 0.3f, -1), V(0, 0.3f, -1), up, green);
                var c = V(x, h, z); const float r = 0.035f;
                for (int k = 0; k < 5; k++)
                {
                    float a0 = k * Mathf.PI * 2 / 5, a1 = a0 + Mathf.PI * 2 / 5;
                    pet.TriN(c, V(x + Mathf.Cos(a0) * r, h + 0.006f, z + Mathf.Sin(a0) * r), V(x + Mathf.Cos(a1) * r, h + 0.006f, z + Mathf.Sin(a1) * r), up, up, up, Color.white);
                }
                pet.TriN(V(x, h + 0.012f, z), V(x + 0.012f, h + 0.01f, z), V(x, h + 0.01f, z + 0.012f), up, up, up, new Color(1.0f, 0.9f, 0.4f));
            }
            var m = MergeLeaf(stem, pet, "flower");
            cache["flower"] = m;
            return m;
        }
        // 村舍（屋顶 / 帐顶乘实例颜色）：han 白墙灰瓦 | flat 平顶土屋 | tile 红瓦坡顶 | round 圆形茅屋 | yurt 毡帐 | stilt 高脚屋 | long 长屋
        public static Mesh HouseGeometry(string style)
        {
            string key = "house:" + style;
            Mesh cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            var wall = new MeshBuilder(); var roof = new MeshBuilder();
            var W = new Color(0.95f, 0.95f, 0.95f);
            Action<float, float, float, float, float> gableRoof = (w, d, y, h, e) =>
            {
                float hw = w / 2 + e, hd = d / 2 + e;
                Vector3 r0 = V(-hw, y + h, 0), r1 = V(hw, y + h, 0);
                Vector3 s0 = V(-hw, y - 0.01f, -hd), s1 = V(hw, y - 0.01f, -hd), n0 = V(-hw, y - 0.01f, hd), n1 = V(hw, y - 0.01f, hd);
                roof.Quad(s0, r0, r1, s1, W); roof.Quad(n1, r1, r0, n0, new Color(0.8f, 0.8f, 0.8f));
                wall.Tri(V(-w / 2, y, d / 2), V(-w / 2, y + h * 0.92f, 0), V(-w / 2, y, -d / 2), new Color(0.86f, 0.82f, 0.74f));
                wall.Tri(V(w / 2, y, -d / 2), V(w / 2, y + h * 0.92f, 0), V(w / 2, y, d / 2), new Color(0.86f, 0.82f, 0.74f));
            };
            Action<float, float, float, Color> door = (w, h, d, c) => wall.Box(V(0, h / 2, -d / 2 - 0.004f), V(w, h, 0.01f), c);
            var doorC = new Color(0.25f, 0.18f, 0.12f);
            switch (style)
            {
                case "han":
                    wall.Box(V(0, 0.1f, 0), V(0.34f, 0.2f, 0.24f), new Color(0.9f, 0.87f, 0.8f));
                    door(0.07f, 0.12f, 0.24f, doorC);
                    gableRoof(0.34f, 0.24f, 0.2f, 0.13f, 0.04f);
                    roof.Box(V(0, 0.335f, 0), V(0.44f, 0.025f, 0.03f), new Color(0.7f, 0.7f, 0.7f));
                    break;
                case "tile":
                    wall.Box(V(0, 0.1f, 0), V(0.32f, 0.2f, 0.24f), new Color(0.93f, 0.89f, 0.8f));
                    door(0.07f, 0.12f, 0.24f, doorC);
                    gableRoof(0.32f, 0.24f, 0.2f, 0.09f, 0.03f);
                    break;
                case "flat":
                    wall.Box(V(0, 0.09f, 0), V(0.3f, 0.18f, 0.26f), new Color(0.86f, 0.76f, 0.58f));
                    door(0.07f, 0.11f, 0.26f, doorC);
                    roof.Box(V(0, 0.19f, 0), V(0.32f, 0.03f, 0.28f), W);
                    wall.Box(V(0.08f, 0.25f, 0.05f), V(0.12f, 0.1f, 0.12f), new Color(0.82f, 0.72f, 0.55f));
                    break;
                case "round":
                    wall.Lathe(V(0, 0, 0), new[] { L(0.15f, 0), L(0.15f, 0.11f) }, 9, new Color(0.72f, 0.62f, 0.46f));
                    roof.Lathe(V(0, 0.1f, 0), new[] { L(0, -0.01f), L(0.2f, 0, true), L(0.08f, 0.13f), L(0, 0.2f) }, 9, W);
                    door(0.06f, 0.09f, 0.3f, doorC);
                    break;
                case "yurt":
                    wall.Lathe(V(0, 0, 0), new[] { L(0.16f, 0), L(0.16f, 0.1f) }, 10, new Color(0.92f, 0.9f, 0.84f));
                    roof.Lathe(V(0, 0.1f, 0), new[] { L(0.165f, 0, true), L(0.12f, 0.06f), L(0.04f, 0.1f), L(0, 0.105f) }, 10, W);
                    door(0.06f, 0.08f, 0.32f, new Color(0.62f, 0.25f, 0.18f));
                    break;
                case "stilt":
                    for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) wall.Box(V(sx * 0.12f, 0.06f, sz * 0.09f), V(0.025f, 0.12f, 0.025f), new Color(0.4f, 0.3f, 0.2f));
                    wall.Box(V(0, 0.18f, 0), V(0.3f, 0.12f, 0.22f), new Color(0.66f, 0.52f, 0.34f));
                    gableRoof(0.3f, 0.22f, 0.24f, 0.16f, 0.05f);
                    break;
                case "long":
                    wall.Box(V(0, 0.06f, 0), V(0.5f, 0.12f, 0.2f), new Color(0.62f, 0.5f, 0.36f));
                    gableRoof(0.5f, 0.2f, 0.12f, 0.17f, 0.04f);
                    break;
            }
            var m = MergeLeaf(wall, roof, key);
            cache[key] = m;
            return m;
        }

        // ---------------------------------------------------------- 远景树：公告板 --
        public static readonly string[] SpriteKinds = { "broad", "broad2", "pine", "spruce", "cypress", "poplar", "birch", "shrub", "willow", "olive", "stonepine", "acacia", "bamboo", "palm" };
        // 面片宽、高（与 lod 0 模型的外形一致，单位 = 实例缩放 1）
        public static double[] SpriteSize(string kind)
        {
            switch (kind)
            {
                case "broad": case "broad2": case "willow": return new[] { 1.0, 1.12 };
                case "pine": return new[] { 0.95, 1.3 };
                case "spruce": return new[] { 0.75, 1.3 };
                case "cypress": return new[] { 0.44, 1.38 };
                case "poplar": return new[] { 0.42, 1.36 };
                case "birch": return new[] { 0.62, 1.28 };
                case "shrub": return new[] { 0.66, 0.36 };
                case "olive": return new[] { 0.8, 0.82 };
                case "stonepine": return new[] { 1.04, 1.14 };
                case "acacia": return new[] { 1.0, 0.86 };
                case "bamboo": return new[] { 0.62, 1.1 };
                case "palm": return new[] { 1.2, 1.1 };
                default: return new[] { 1.0, 1.1 };
            }
        }
        static Texture2D spriteTex; static Material spriteMat; static Mesh spriteQuad;
        // 图集（4 × 4 格，每格 128²）：各树种的剪影、受光与叶簇斑点（Raster 绘制，与网页版 Canvas 的画法相同）
        public static Texture2D SpriteAtlas()
        {
            if (spriteTex != null) return spriteTex;
            const int C = 128, cols = 4, rows = 4;
            var g = new Raster(C * cols, C * rows);
            var rnd = new SeededRandom(321);
            Func<double, double, double, double, RGBA> rgb = (r, gg, b, a) => new RGBA((float)(r / 255), (float)(gg / 255), (float)(b / 255), (float)a);
            Action<double, double, double, double, double> puff = (x, y, rx, ry, k) =>
            {
                // 渐变坐标为用户空间（当前变换 = 平移到格子）：Raster 的渐变按设备坐标取值，需设好 toGrad
                var gr = Paint.Radial(x, y, Math.Max(rx, ry) * 1.05);
                gr.fx = x - rx * 0.35; gr.fy = y - ry * 0.4;
                gr.toGrad = g.Transform.Inverse();
                gr.AddStop(0, rgb(JsR(248 * k), JsR(248 * k), JsR(244 * k), 1));
                gr.AddStop(0.55, rgb(JsR(196 * k), JsR(198 * k), JsR(190 * k), 1));
                gr.AddStop(1, rgb(JsR(112 * k), JsR(116 * k), JsR(108 * k), 1));
                g.FillStyle = gr;
                g.BeginPath(); g.Ellipse(x, y, rx, ry, 0, 0, Math.PI * 2); g.Fill();
            };
            Action<double, double, double, double, int> crown = (cx, cy, rx, ry, n) =>
            {
                for (int i = 0; i < n; i++)
                {
                    double a = rnd.NextDouble() * Math.PI * 2, d = Math.Sqrt(rnd.NextDouble()) * 0.45;
                    double px = cx + Math.Cos(a) * rx * d, py = cy + Math.Sin(a) * ry * d, prx = rx * (0.48 + rnd.NextDouble() * 0.2), pry = ry * (0.48 + rnd.NextDouble() * 0.2), pk = 0.9 + rnd.NextDouble() * 0.1;
                    puff(px, py, prx, pry, pk);
                }
                puff(cx, cy, rx * 0.78, ry * 0.8, 1);
            };
            double ox = 0, oy = 0;
            // source-atop 的斑点：只画在已有像素上，保持原 alpha
            Action<double, double, double, double, int> speckle = (x0, y0, x1, y1, n) =>
            {
                for (int i = 0; i < n; i++)
                {
                    double x = x0 + rnd.NextDouble() * (x1 - x0), y = y0 + rnd.NextDouble() * (y1 - y0), r = 1 + rnd.NextDouble() * 2.5;
                    bool darkS = rnd.NextDouble() < 0.5;
                    Atop(g, ox + x, oy + y, r, darkS ? new RGBA(60 / 255f, 64 / 255f, 58 / 255f, 0.28f) : new RGBA(1, 1, 250 / 255f, 0.22f), (int)ox, (int)oy, C);
                }
            };
            Action<double, double, double, double, string> trunk = (x, y0, y1, w, col) => { g.FillColor = col ?? "rgb(120,92,62)"; g.FillRect(x - w / 2, y1, w, y0 - y1); };
            Action<double, double, double, double> tier = (cx, yb, w, h) =>
            {
                var gr = Paint.Linear(cx - w / 2, 0, cx + w / 2, 0);
                gr.toGrad = g.Transform.Inverse();
                gr.AddStop(0, rgb(236, 238, 232, 1)); gr.AddStop(0.5, rgb(190, 194, 186, 1)); gr.AddStop(1, rgb(110, 114, 106, 1));
                g.FillStyle = gr;
                g.BeginPath(); g.MoveTo(cx - w / 2, yb); g.QuadraticCurveTo(cx - w * 0.12, yb - h * 0.5, cx, yb - h); g.QuadraticCurveTo(cx + w * 0.12, yb - h * 0.5, cx + w / 2, yb);
                g.QuadraticCurveTo(cx, yb + h * 0.08, cx - w / 2, yb); g.Fill();
            };
            for (int i = 0; i < SpriteKinds.Length; i++)
            {
                string kind = SpriteKinds[i];
                ox = (i % cols) * C; oy = Math.Floor((double)i / cols) * C;
                g.Save(); g.Translate(ox, oy);
                g.BeginPath(); g.Rect(0, 0, C, C); g.Clip();
                double Wd = C, H = C, bx = Wd / 2, by = H - 2;
                switch (kind)
                {
                    case "broad": case "broad2": case "olive":
                        trunk(bx, by, H * 0.45, Wd * 0.07, null);
                        crown(bx, H * 0.4, Wd * 0.42, H * 0.34, kind == "broad2" ? 7 : 5);
                        speckle(0, 0, Wd, H * 0.8, 90);
                        break;
                    case "willow":
                        trunk(bx, by, H * 0.5, Wd * 0.08, null);
                        crown(bx, H * 0.38, Wd * 0.42, H * 0.3, 5);
                        g.StrokeColor = "rgba(150,154,146,0.9)"; g.LineWidth = 2.2;
                        for (int k = 0; k < 16; k++) { double x = Wd * 0.12 + k * Wd * 0.05; g.BeginPath(); g.MoveTo(x, H * 0.45); double ex = x + (rnd.NextDouble() - 0.5) * 4, ey = H * (0.66 + rnd.NextDouble() * 0.12); g.LineTo(ex, ey); g.Stroke(); }
                        speckle(0, 0, Wd, H * 0.8, 70);
                        break;
                    case "pine": trunk(bx, by, H * 0.7, Wd * 0.07, null); tier(bx, H * 0.84, Wd * 0.96, H * 0.5); tier(bx, H * 0.56, Wd * 0.72, H * 0.52); speckle(0, 0, Wd, H, 60); break;
                    case "spruce": trunk(bx, by, H * 0.8, Wd * 0.06, null); tier(bx, H * 0.9, Wd * 0.96, H * 0.42); tier(bx, H * 0.66, Wd * 0.76, H * 0.4); tier(bx, H * 0.44, Wd * 0.54, H * 0.42); speckle(0, 0, Wd, H, 50); break;
                    case "cypress":
                        {
                            trunk(bx, by, H * 0.85, Wd * 0.1, null);
                            var gr = Paint.Linear(bx - Wd * 0.4, 0, bx + Wd * 0.4, 0);
                            gr.toGrad = g.Transform.Inverse();
                            gr.AddStop(0, rgb(232, 234, 228, 1)); gr.AddStop(0.5, rgb(186, 190, 182, 1)); gr.AddStop(1, rgb(104, 108, 100, 1));
                            g.FillStyle = gr; g.BeginPath(); g.Ellipse(bx, H * 0.5, Wd * 0.42, H * 0.44, 0, 0, Math.PI * 2); g.Fill();
                            speckle(0, 0, Wd, H, 50);
                            break;
                        }
                    case "poplar": case "birch":
                        trunk(bx, by, H * 0.6, Wd * (kind == "birch" ? 0.12 : 0.1), kind == "birch" ? "rgb(236,232,222)" : null);
                        crown(bx, H * 0.42, Wd * 0.42, H * 0.38, 5);
                        speckle(0, 0, Wd, H * 0.85, 60);
                        break;
                    case "shrub": crown(bx, H * 0.55, Wd * 0.46, H * 0.4, 6); speckle(0, 0, Wd, H, 70); break;
                    case "stonepine": case "acacia":
                        g.StrokeColor = "rgb(120,92,62)"; g.LineWidth = Wd * 0.05;
                        g.BeginPath(); g.MoveTo(bx, by); g.LineTo(bx + Wd * 0.03, H * 0.42); g.Stroke();
                        if (kind == "acacia") { g.BeginPath(); g.MoveTo(bx, H * 0.75); g.LineTo(bx - Wd * 0.2, H * 0.4); g.Stroke(); }
                        crown(bx, H * (kind == "acacia" ? 0.3 : 0.24), Wd * 0.47, H * 0.2, 6);
                        speckle(0, 0, Wd, H * 0.5, 60);
                        break;
                    case "bamboo":
                        g.StrokeColor = "rgb(150,180,110)"; g.LineWidth = 2.5;
                        for (int k = 0; k < 6; k++) { double x = bx + (k - 2.5) * Wd * 0.08; g.BeginPath(); g.MoveTo(x, by); g.QuadraticCurveTo(x, H * 0.4, x + (k - 2.5) * 5, H * 0.1); g.Stroke(); }
                        for (int k = 0; k < 9; k++) { double px = bx + (rnd.NextDouble() - 0.5) * Wd * 0.6, py = H * (0.15 + rnd.NextDouble() * 0.5); puff(px, py, Wd * 0.12, H * 0.1, 0.95); }
                        break;
                    case "palm":
                        {
                            g.StrokeColor = "rgb(140,110,76)"; g.LineWidth = Wd * 0.06;
                            g.BeginPath(); g.MoveTo(bx, by); g.QuadraticCurveTo(bx + Wd * 0.02, H * 0.6, bx + Wd * 0.12, H * 0.22); g.Stroke();
                            double tx = bx + Wd * 0.12, ty = H * 0.22;
                            for (int k = 0; k < 9; k++)
                            {
                                double a = -Math.PI * 0.95 + k * Math.PI * 0.95 / 4, Lr = Wd * 0.46;
                                double ex = tx + Math.Cos(a) * Lr, ey = ty + Math.Sin(a) * Lr * 0.5 + Lr * 0.35;
                                g.StrokeColor = k % 2 != 0 ? "rgb(210,214,204)" : "rgb(170,174,164)"; g.LineWidth = 7;
                                g.BeginPath(); g.MoveTo(tx, ty); g.QuadraticCurveTo((tx + ex) / 2, ty - Lr * 0.15, ex, ey); g.Stroke();
                            }
                            break;
                        }
                }
                g.Restore();
            }
            spriteTex = g.ToTexture(true);
            spriteTex.wrapMode = TextureWrapMode.Clamp;
            spriteTex.filterMode = FilterMode.Trilinear;
            return spriteTex;
        }
        static double JsR(double v) { return Math.Floor(v + 0.5); }
        // 圆点按 source-atop 叠到栅格上（只在已有像素上着色、保持原 alpha；4×4 超采样的覆盖率），限制在格子 (cx0, cy0, C) 内
        static void Atop(Raster g, double x, double y, double r, RGBA c, int cx0, int cy0, int C)
        {
            int x0 = Math.Max(cx0, (int)Math.Floor(x - r)), x1 = Math.Min(cx0 + C - 1, (int)Math.Ceiling(x + r)), y0 = Math.Max(cy0, (int)Math.Floor(y - r)), y1 = Math.Min(cy0 + C - 1, (int)Math.Ceiling(y + r));
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++) { double dx = px + (sx + 0.5) / 4 - x, dy = py + (sy + 0.5) / 4 - y; if (dx * dx + dy * dy <= r * r) hit++; }
                    if (hit == 0) continue;
                    float cov = hit / 16f, sa = c.a * cov;
                    int o = (py * g.W + px) * 4;
                    float da = g.Px[o + 3];
                    g.Px[o] = c.r * sa * da + g.Px[o] * (1 - sa);
                    g.Px[o + 1] = c.g * sa * da + g.Px[o + 1] * (1 - sa);
                    g.Px[o + 2] = c.b * sa * da + g.Px[o + 2] * (1 - sa);
                }
        }
        public static Material SpriteMaterial()
        {
            if (spriteMat != null) return spriteMat;
            spriteMat = new Material(Art.LoadShader("TreeSprite")) { name = "TreeSprites", enableInstancing = true };
            spriteMat.mainTexture = SpriteAtlas();
            spriteMat.SetColor("_Light", new Color(1.02f, 1.0f, 0.94f));
            return spriteMat;
        }
        // 公告板面片：宽 1、高 1，底边中点在原点（PlaneGeometry(1, 1).translate(0, 0.5, 0)）
        public static Mesh SpriteQuad()
        {
            if (spriteQuad != null) return spriteQuad;
            var m = new Mesh { name = "veg:sprite" };
            m.vertices = new[] { V(-0.5f, 0, 0), V(-0.5f, 1, 0), V(0.5f, 1, 0), V(0.5f, 0, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(2, 2, 2));
            spriteQuad = m;
            return m;
        }

        // 云团贴图（128²）：径向渐隐 × 两级值噪声（蓬松的边），上亮下灰（RGB），alpha 柔边
        static Texture2D cloudTex;
        public static Texture2D CloudTexture()
        {
            if (cloudTex != null) return cloudTex;
            const int N = 128;
            Func<double, double, double> hh = (x, y) => { double v = Math.Sin(x * 127.1 + y * 311.7) * 43758.5453; return v - Math.Floor(v); };
            Func<double, double, double> vn = (x, y) =>
            {
                double xi = Math.Floor(x), yi = Math.Floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
                return (hh(xi, yi) * (1 - u) + hh(xi + 1, yi) * u) * (1 - v) + (hh(xi, yi + 1) * (1 - u) + hh(xi + 1, yi + 1) * u) * v;
            };
            var d = new byte[N * N * 4];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    double px = (x + 0.5) / N * 2 - 1, py = (y + 0.5) / N * 2 - 1;   // py：-1 上 → 1 下
                    double r = Math.Sqrt(px * px + py * 1.05 * py * 1.05);
                    double nz = vn(px * 3 + 7, py * 3 + 3) * 0.65 + vn(px * 7 + 1, py * 7 + 9) * 0.35;
                    double a = 1 - WorldGeo.Smooth(0, 1, WorldGeo.Clamp01((r - 0.25 + (nz - 0.5) * 0.5) / 0.7));
                    a *= WorldGeo.Clamp01(1.25 - Math.Max(0, py) * 0.5);   // 底部略收
                    double lit = WorldGeo.Clamp01(0.5 - py * 0.55 + (nz - 0.5) * 0.35);   // 上亮下灰
                    int o = ((N - 1 - y) * N + x) * 4;   // 画布第 0 行在上（v = 1）
                    d[o] = (byte)JsR(255 * (0.74 + 0.26 * lit)); d[o + 1] = (byte)JsR(255 * (0.76 + 0.24 * lit)); d[o + 2] = (byte)JsR(255 * (0.82 + 0.18 * lit)); d[o + 3] = (byte)JsR(255 * WorldGeo.Clamp01(a));
                }
            cloudTex = Art.DataTexture(d, N, N, false, true);
            return cloudTex;
        }
        // 路面纹理：u 横跨路面（两侧透明渐隐、两道车辙），v 沿路重复
        public static Texture2D RoadTexture()
        {
            const int W = 32, H = 64;
            var d = new byte[W * H * 4]; var rnd = new SeededRandom(57);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double u = (x + 0.5) / W; int o = (y * W + x) * 4;
                    double rut = Math.Exp(-Math.Pow((u - 0.33) / 0.05, 2)) + Math.Exp(-Math.Pow((u - 0.67) / 0.05, 2));
                    double gv = 0.9 + 0.1 * Math.Sin((double)y / H * Math.PI * 8 + x * 0.7) * 0.5 + (rnd.NextDouble() - 0.5) * 0.12 - rut * 0.16;
                    d[o] = d[o + 1] = d[o + 2] = WorldGeo.B8(gv);
                    d[o + 3] = WorldGeo.B8(TerrainModel.SS(0.0, 0.24, u) * TerrainModel.SS(1.0, 0.76, u) * 0.92);
                }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true, true) { wrapModeU = TextureWrapMode.Clamp, wrapModeV = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(d[i * 4], d[i * 4 + 1], d[i * 4 + 2], d[i * 4 + 3]);
            t.SetPixels32(px); t.Apply(true, false);
            return t;
        }

        static Color C3(float r, float g, float b) { return new Color(r, g, b); }
        static Matrix4x4 TRy(Vector3 t, float yawRad, float scale = 1)
        {
            return Matrix4x4.TRS(t, Quaternion.Euler(0, yawRad * Mathf.Rad2Deg, 0), new Vector3(scale, scale, scale));
        }
        // 桥：a、b 为两岸的路面点（地图坐标），deck 为桥面高度；stone = 石拱桥，否则木桥
        public static void BridgeInto(MeshBuilder mb, TerrainModel.BridgePlan p)
        {
            float ax = (float)p.ax, az = (float)p.az, bx = (float)p.bx, bz = (float)p.bz, deck = (float)p.deck;
            float dx = bx - ax, dz = bz - az, Ln = Mathf.Sqrt(dx * dx + dz * dz) + 0.5f;
            mb.M = TRy(V((ax + bx) / 2, deck, (az + bz) / 2), Mathf.Atan2(dx, dz));
            if (p.stone)
            {
                Color st = C3(0.6f, 0.57f, 0.51f), st2 = C3(0.5f, 0.47f, 0.42f);
                mb.Box(V(0, -0.05f, 0), V(0.62f, 0.14f, Ln), st);
                for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(sx * 0.29f, 0.07f, 0), V(0.05f, 0.1f, Ln), C3(0.68f, 0.65f, 0.58f));
                int spans = Math.Max(1, WorldGeo.JsRound((Ln - 0.5f) / 1.7)); float sl = (Ln - 0.5f) / spans;
                for (int k = 0; k <= spans; k++)
                {
                    float z = -Ln / 2 + 0.25f + k * sl;
                    if (k > 0 && k < spans) mb.Box(V(0, -(deck + 0.4f) / 2 - 0.1f, z), V(0.56f, deck + 0.4f, 0.24f), st2);
                }
                for (int k = 0; k < spans; k++)
                {
                    float zm = -Ln / 2 + 0.25f + (k + 0.5f) * sl, r = sl / 2; const int m = 7;
                    for (int q = 0; q < m; q++)
                    {
                        float t0 = (float)q / m * Mathf.PI, t1 = (float)(q + 1) / m * Mathf.PI, tm = (t0 + t1) / 2;
                        float y = -deck - 0.05f + Mathf.Sin(tm) * Mathf.Min(deck + 0.02f, r * 0.9f), z = zm - Mathf.Cos(tm) * r * 0.98f;
                        mb.Box(V(0, Mathf.Min(-0.12f, y), z), V(0.58f, 0.07f, Mathf.PI * r / m * 1.05f), st2);
                    }
                }
            }
            else
            {
                Color wd = C3(0.52f, 0.4f, 0.27f), wd2 = C3(0.42f, 0.32f, 0.22f);
                mb.Box(V(0, -0.03f, 0), V(0.56f, 0.05f, Ln), wd);
                int n = Math.Max(2, WorldGeo.JsRound(Ln / 0.9));
                for (int k = 0; k <= n; k++)
                {
                    float z = -Ln / 2 + k * Ln / n;
                    for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(sx * 0.27f, (0.16f - deck - 0.3f) / 2, z), V(0.05f, deck + 0.46f, 0.05f), wd2);
                }
                for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(sx * 0.27f, 0.13f, 0), V(0.035f, 0.035f, Ln), wd2);
            }
            mb.M = Matrix4x4.identity;
        }
        static Color Shade3(Color c, float k) { return new Color(Mathf.Min(1, c.r * (1 + k)), Mathf.Min(1, c.g * (1 + k)), Mathf.Min(1, c.b * (1 + k))); }
        // 关隘：两侧贴地的斜切墙段（每段 0.45，墙顶高出地面 0.42；下坡、遇水、断崖即止）+ 关城（中原关楼 / 西方石门）
        public static void PassInto(MeshBuilder mb, TerrainModel view, TerrainModel.PassPlan p)
        {
            float x = (float)p.x, z = (float)p.z, dx = (float)p.dx, dz = (float)p.dz;
            Func<float, float, float> S = (a, b) => (float)view.SurfaceY(a, b);
            float yaw = Mathf.Atan2(dx, dz), y0 = S(x, z); const float K = 0.78f;
            bool han = p.style != "west";
            Color stone = han ? C3(0.66f, 0.63f, 0.57f) : C3(0.78f, 0.72f, 0.6f), dark = C3(0.16f, 0.12f, 0.1f);
            float px = dz, pz = -dx;   // 横向
            const float HT = 0.42f, TH = 0.15f, SEG = 0.45f;
            for (int s = -1; s <= 1; s += 2)
            {
                float oa = 0.62f, ga = S(x + px * s * oa, z + pz * s * oa);
                for (int k = 0; k < 7; k++)
                {
                    float ob = oa + SEG, bx = x + px * s * ob, bz = z + pz * s * ob, gb = S(bx, bz);
                    if (gb < 0.05f || gb < y0 - 0.5f || Mathf.Abs(gb - ga) > 0.9f) break;
                    float ax = x + px * s * oa, az = z + pz * s * oa;
                    Func<float, float, float, float, Vector3> P = (wx, wz, y, t) => V(wx + dx * t, y, wz + dz * t);
                    Vector3 a0 = P(ax, az, ga - 0.35f, -TH), a1 = P(ax, az, ga - 0.35f, TH), a2 = P(ax, az, ga + HT, TH), a3 = P(ax, az, ga + HT, -TH);
                    Vector3 b0 = P(bx, bz, gb - 0.35f, -TH), b1 = P(bx, bz, gb - 0.35f, TH), b2 = P(bx, bz, gb + HT, TH), b3 = P(bx, bz, gb + HT, -TH);
                    bool sd = s > 0;
                    // 两个侧面、墙顶、末端（朝向按 s 翻转，保持外法线）
                    if (sd) { mb.Quad(a0, a3, b3, b0, stone); mb.Quad(a1, b1, b2, a2, stone); mb.Quad(a3, a2, b2, b3, Shade3(stone, 0.06f)); }
                    else { mb.Quad(a0, b0, b3, a3, stone); mb.Quad(a1, a2, b2, b1, stone); mb.Quad(a3, b3, b2, a2, Shade3(stone, 0.06f)); }
                    float nb = S(x + px * s * (ob + SEG), z + pz * s * (ob + SEG));
                    if (k == 6 || nb < 0.05f || nb < y0 - 0.5f || Mathf.Abs(nb - gb) > 0.9f) { if (sd) mb.Quad(b0, b3, b2, b1, stone); else mb.Quad(b0, b1, b2, b3, stone); }
                    // 垛口（外侧）
                    foreach (var f in new[] { 0.25f, 0.75f })
                    {
                        float cx = ax + (bx - ax) * f, cz = az + (bz - az) * f, cy = ga + (gb - ga) * f + HT;
                        mb.M = TRy(V(cx - dx * 0.1f, cy + 0.05f, cz - dz * 0.1f), yaw);
                        mb.Box(V(0, 0, 0), V(0.13f, 0.1f, 0.06f), stone);
                        mb.M = Matrix4x4.identity;
                    }
                    oa = ob; ga = gb;
                }
            }
            mb.M = TRy(V(x, y0, z), yaw, K);
            mb.Box(V(0, -0.32f, 0), V(han ? 1.7f : 1.9f, 0.6f, han ? 0.8f : 0.72f), stone, 0);   // 基座（斜坡上不悬空）
            if (han)
            {
                // 关城：墩台 + 门洞 + 两层关楼
                mb.Box(V(0, 0.3f, 0), V(1.7f, 0.95f, 0.8f), stone, 0.06f);
                for (int f = -1; f <= 1; f += 2) mb.Box(V(0, 0.12f, f * 0.405f), V(0.36f, 0.5f, 0.02f), dark);
                for (int i = -3; i <= 3; i++) if (i != 0) for (int f = -1; f <= 1; f += 2) mb.Box(V(i * 0.24f, 0.84f, f * 0.36f), V(0.12f, 0.12f, 0.08f), stone);
                Color red = C3(0.62f, 0.2f, 0.16f), roof = C3(0.24f, 0.27f, 0.33f);
                mb.Box(V(0, 0.98f, 0), V(1.05f, 0.3f, 0.5f), red);
                mb.ChineseRoof(V(0, 1.13f, 0), 1.5f, 0.82f, 0.34f, roof);
                mb.Box(V(0, 1.35f, 0), V(0.62f, 0.22f, 0.34f), red);
                mb.ChineseRoof(V(0, 1.46f, 0), 0.98f, 0.6f, 0.3f, roof);
                mb.Box(V(0.0f, 1.0f, -0.26f), V(0.5f, 0.14f, 0.02f), C3(0.12f, 0.12f, 0.16f));   // 匾额
            }
            else
            {
                // 石门：拱门 + 两座方塔
                mb.Box(V(0, 0.32f, 0), V(1.0f, 0.95f, 0.7f), stone, 0.06f);
                for (int f = -1; f <= 1; f += 2) mb.Box(V(0, 0.16f, f * 0.355f), V(0.34f, 0.56f, 0.02f), dark);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    mb.Box(V(sx * 0.68f, 0.48f, 0), V(0.5f, 1.3f, 0.62f), C3(0.72f, 0.66f, 0.55f), 0.06f);
                    foreach (var t in new[] { -0.16f, 0.16f }) for (int f = -1; f <= 1; f += 2) mb.Box(V(sx * 0.68f + t, 1.18f, f * 0.24f), V(0.12f, 0.12f, 0.12f), C3(0.72f, 0.66f, 0.55f));
                }
                mb.Flag(V(0, 0.8f, 0), 0.6f, 0.32f, 0.22f, C3(0.66f, 0.16f, 0.14f));
            }
            mb.M = Matrix4x4.identity;
        }
    }
}
