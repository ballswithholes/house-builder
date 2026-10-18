// ==========================================================================
// 三国志-霸王的世界 · 单挑格斗 · 场地与特效（网页版 js/duel-game.js「视图：场地 / 特效」第三版的移植）
//   DuelArena     第三版场地：平滑、细致（与新地图同一画风）——
//                 地面：非均匀细分的高度场（场中 0.5 米一格，远处渐疏），平滑法线，Sanguo/DuelGround（地形着色器的顶点版，
//                 高度换算为「海拔」1.2 + y × 0.32）的程序化草地 / 泥土 / 农田 / 沙 / 岩石 / 雪混合；对决场地是踩实的泥土，
//                 四周起伏的草坡，远处是雾中的群山与一道天际山脊。
//                 景物：平滑树冠的阔叶树 / 松（叶簇明暗与法线起伏）、圆润的岩石、随风摆动的草丛与野花、芦苇、倒木与灌丛
//                 （Sanguo/DuelFoliage 实例化绘制，每批 ≤ 1023；风的幅度按种类）；断枪残旗与弃盾、旗杆（车削）与飘动的帅旗；
//                 城墙为砌砖纹理的城墙（Sanguo/DuelProp 的砖纹）、城门楼、火把；河川为 Sanguo/DuelWater 的水面。
//                 杂项（顶点色）用 Sanguo/DuelProp 的 LowPoly 模式（_HL = 1）。场地着色器的环境光 = 网页版 LowPoly 补丁的三色环境光：
//                 天空 / 地面取本场半球光，地平线取主场景的地平线色（全局 _DuelAmb*，DuelScreen 进入画面时设定）。
//                 两军：View/FigureCrowd 的实例化士兵群（各自的文化衣甲与势力色，待机呼吸，胜方欢呼）。
//                 植被与石块的网格、材质跨场次共用（不随单挑释放）；地面、远山、景物网格每场释放。
//   DuelParticles 面向镜头的粒子面片（火花 / 尘土 / 光点）
//   DuelTrail     刀光拖尾（第三版修订：样本之间按「绕刃根旋转」插入细分样本，横向四列顶点，按样本年龄淡出）
// 坐标：场地几何按网页版的 three 坐标计算（与 JS 逐式相同），写入 Unity 网格时 z 取反、三角形绕序反转；
//       MeshBuilder（View/Art.cs）本身接受 Unity 坐标（= 网页版代码里的 U(x, y, z) = (x, y, −z)）。
// 随机数（SeededRandom）的取用顺序与网页版逐次相同，所以同一对武将的场地布置与网页版一致。
// ==========================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Sanguo
{
    public sealed class DuelPalette
    {
        public string top, hor, low, sun, fog, key, rim, hemiS, hemiG, ground, ground2, lane, far, mid, leaf, rock;
        public Vector3 sunDir;     // three 坐标
        public float fogN, fogF, keyI, rimI, hemiI;
    }

    public sealed class DuelArena
    {
        public static readonly Dictionary<string, DuelPalette> Palettes = new Dictionary<string, DuelPalette>
        {
            { "plain", new DuelPalette { top = "#1f355e", hor = "#e89a5c", low = "#5a4232", sun = "#ffcf8a", sunDir = new Vector3(-0.35f, 0.14f, -1), fog = "#a87a5c", fogN = 30, fogF = 150, key = "#ffd9a8", keyI = 0.86f, rim = "#ff8c50", rimI = 0.8f, hemiS = "#8aa0c8", hemiG = "#4a3626", hemiI = 0.42f,
                ground = "#5c6e32", ground2 = "#7c6c3c", lane = "#7e6646", far = "#5a5a78", mid = "#44523a", leaf = "#3f6a2c", rock = "#7c7466" } },
            { "forest", new DuelPalette { top = "#16383c", hor = "#a8c890", low = "#2a3a26", sun = "#fff0c0", sunDir = new Vector3(0.3f, 0.45f, -1), fog = "#4c6a50", fogN = 14, fogF = 80, key = "#fff0c8", keyI = 0.82f, rim = "#c8f0a0", rimI = 0.6f, hemiS = "#7ca48c", hemiG = "#2a2a18", hemiI = 0.42f,
                ground = "#3a5a26", ground2 = "#4c4a28", lane = "#5e5038", far = "#3a5a4c", mid = "#24402a", leaf = "#2f5a24", rock = "#5e5c50" } },
            { "hill", new DuelPalette { top = "#2c4c80", hor = "#dcc8a8", low = "#6a5a4a", sun = "#fff0d0", sunDir = new Vector3(-0.5f, 0.3f, -1), fog = "#9c958c", fogN = 30, fogF = 160, key = "#fff0d8", keyI = 0.9f, rim = "#ffc890", rimI = 0.65f, hemiS = "#98a8c8", hemiG = "#4a3c2e", hemiI = 0.42f,
                ground = "#6c6440", ground2 = "#7c7056", lane = "#857052", far = "#646c80", mid = "#545642", leaf = "#4a6030", rock = "#706b60" } },
            { "river", new DuelPalette { top = "#2e5a92", hor = "#ecd6b2", low = "#5a7a8a", sun = "#fff4d8", sunDir = new Vector3(0.4f, 0.22f, -1), fog = "#9cacae", fogN = 30, fogF = 160, key = "#fff4e0", keyI = 0.88f, rim = "#ffd8a0", rimI = 0.6f, hemiS = "#a0b8d8", hemiG = "#4a4a3a", hemiI = 0.45f,
                ground = "#5e6c40", ground2 = "#857e66", lane = "#8a7e62", far = "#62788a", mid = "#4a6a4a", leaf = "#3e6a30", rock = "#8a8478" } },
            { "castle", new DuelPalette { top = "#121234", hor = "#d8643e", low = "#3a2222", sun = "#ff9a5a", sunDir = new Vector3(0.5f, 0.1f, -1), fog = "#5c3a3c", fogN = 24, fogF = 120, key = "#ffc088", keyI = 0.86f, rim = "#ff6a3a", rimI = 0.95f, hemiS = "#5a5a90", hemiG = "#3a2620", hemiI = 0.4f,
                ground = "#4c4238", ground2 = "#5a4e42", lane = "#62584a", far = "#3a2e44", mid = "#463a3c", leaf = "#2e4228", rock = "#5c544a" } },
        };
        // 地形 → 背景（= 网页版 terrainKind）。第三版战场的深水（Terrain.Water）上不会有部队，按平原
        public static string TerrainKind(Terrain t)
        {
            switch (t)
            {
                case Terrain.Forest: return "forest";
                case Terrain.Hill: case Terrain.Mountain: return "hill";
                case Terrain.River: return "river";
                case Terrain.Wall: case Terrain.Gate: case Terrain.Castle: return "castle";
            }
            return "plain";
        }

        static Color C(string h) { return DuelXf.C(h); }
        static Vector3 V3(float x, float y, float z) { return new Vector3(x, y, z); }
        static Vector3 U(float x, float y, float z) { return new Vector3(x, y, -z); }   // 网页版的 U()：three 坐标 → MeshBuilder 的 Unity 坐标
        static float Ss(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        // three 的 '#' + color.getHexString() 再 set 回来：每通道钳到 0..1 并量化为 8 位（实例颜色经字符串往返）
        static Color Q8(Color c) { return new Color(Mathf.Round(Mathf.Clamp01(c.r) * 255) / 255f, Mathf.Round(Mathf.Clamp01(c.g) * 255) / 255f, Mathf.Round(Mathf.Clamp01(c.b) * 255) / 255f, 1); }
        static Color Mul(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, 1); }
        static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant(); }

        // 背景两军的士兵群（预烘焙动作用；= 网页版 Arena.crowdSpecs）
        public static List<FigCrowdSpec> CrowdSpecs(string kind, string cA, string cB)
        {
            var output = new List<FigCrowdSpec>();
            var seen = new HashSet<string>();
            var cults = Figure.Cultures;
            foreach (var c in new[] { cA, cB })
                foreach (var type in new[] { "spear", "sword" })
                {
                    string k = c + type;
                    if (!seen.Contains(k) && c != null && cults.Contains(c)) { seen.Add(k); output.Add(new FigCrowdSpec { type = type, culture = c, seed = 0 }); }
                }
            return output;
        }

        public readonly string kind;
        public readonly DuelPalette pal;
        public readonly GameObject group;
        readonly int layer;
        readonly List<Object> owned = new List<Object>();
        readonly SeededRandom rnd;
        readonly Material env;
        readonly string[] cul;
        public readonly List<Vector3> torches = new List<Vector3>();   // three 坐标（同粒子）
        sealed class Banner { public Mesh mesh; public Vector3[] bs, cur; public float w, phase; }
        readonly List<Banner> banners = new List<Banner>();
        public FigureCrowd crowd;
        public readonly List<int>[] troops = { new List<int>(), new List<int>() };
        public bool cheered;
        public int groundTris;
        public GameObject ground;

        // env：网页版的 envMat（LowPoly）。场地自己的杂项改用 Sanguo/DuelProp（_HL = 1，与 LowPoly 光照相同，环境光按网页版的
        // LowPoly 补丁取主场景的地平线色）；env 只留给场外共用的物体
        public DuelArena(Transform parent, int layer, string kind, Color colA, Color colB, string glyphA, string glyphB, uint seed, string culA, string culB, Material env)
        {
            this.kind = kind; this.layer = layer; this.env = PropMaterial();
            pal = Palettes[kind];
            group = new GameObject("DuelArena"); group.layer = layer; group.transform.SetParent(parent, false);
            rnd = new SeededRandom(seed != 0 ? seed : 7u);
            cul = new[] { culA ?? "han", culB ?? "han" };
            // 天空由 DuelScreen 的天空盒（Sanguo/DuelSky）画
            BuildGround();
            BuildFarRidge();
            var mb = new MeshBuilder();      // 杂项（Unity 坐标）
            BuildProps(mb, colA, colB);
            FlushInstances();
            if (mb.Count > 0) AddMesh(mb.ToMesh("DuelProps"), env, true, true);
            BuildArmies(colA, colB);
            // 帅旗（势力色 + 姓氏）
            var glyphs = new[] { glyphA, glyphB }; var cols = new[] { colA, colB };
            float bx = 6.6f;
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1 : 1;
                AddBanner(sx * bx - 0.5f, -5.4f, 4.7f, 1.05f, 2.2f, cols[s], glyphs[s], false, s * 2.1f);
                AddBanner(sx * (bx + 3.8f), -9.6f, 4.6f, 0.8f, 1.7f, cols[s], glyphs[s], true, s * 1.3f + 0.7f);
                AddBanner(sx * (bx + 7.6f), -12.5f, 5.0f, 0.8f, 1.7f, cols[s], glyphs[s], true, s * 0.9f + 1.4f);
            }
        }
        GameObject AddMesh(Mesh mesh, Material mat, bool cast, bool recv)
        {
            owned.Add(mesh);
            var go = Art.MakeObject(mesh.name, mesh, mat, group.transform, cast);
            go.layer = layer;
            go.GetComponent<MeshRenderer>().receiveShadows = recv;
            return go;
        }
        float Rnd() { return (float)rnd.NextDouble(); }

        // ---------------------------------------------------------- 地形 --
        static float Noise(float x, float z)
        {
            return Mathf.PerlinNoise(x * 0.045f + 11, z * 0.045f + 5) * 0.6f + Mathf.PerlinNoise(x * 0.11f + 3, z * 0.11f + 9) * 0.3f + Mathf.PerlinNoise(x * 0.27f + 7, z * 0.27f + 1) * 0.1f;
        }
        // 地面高度（three 坐标）
        public float Height(float x, float z)
        {
            float ax = Mathf.Abs(x);
            float lane = (1 - Ss(2.4f, 5.5f, Mathf.Abs(z))) * (1 - Ss(13, 19, ax));
            float n = Noise(x, z) - 0.47f;
            float amp = kind == "hill" ? 2.6f : kind == "forest" ? 0.9f : kind == "castle" ? 0.25f : kind == "river" ? 0.7f : 1.1f;
            float h;
            if (z < 0)
            {
                float b = Mathf.Max(0, -z - 3);
                h = n * 2.6f * amp * Mathf.Min(1, b / 7) + b * 0.035f * amp;
                // 远处的群山（雾中）
                float far = Ss(38, 95, b) * (8 + 18 * Mathf.PerlinNoise(x * 0.018f + 5, 3.3f) + 9 * Mathf.PerlinNoise(x * 0.05f + 1, 7.1f)) * (kind == "hill" ? 1.7f : kind == "castle" ? 0.6f : 1);
                h += far;
                if (kind == "river")
                {
                    // 河道：z ∈ (−26, −3.4) 低于地面，两岸缓坡
                    float bed = Ss(-3.0f, -4.6f, z) * (1 - Ss(-24.5f, -27.5f, z));
                    h = h * (1 - bed) - 0.85f * bed;
                }
                if (kind == "castle") h *= Ss(13.5f, 16, -z) * 0.3f + (1 - Ss(1, 9, -z)) * 0.7f;
            }
            else
            {
                float f = Mathf.Max(0, z - 3);
                h = f * 0.05f * amp + n * 0.7f * amp * Mathf.Min(1, f / 3);
            }
            h += Mathf.Max(0, ax - 17) * 0.05f * amp;
            return h * (1 - lane);
        }
        static List<float> Axis(float lo, float hi, float c0, float c1, float fine, float coarse, float grow)
        {
            var output = new List<float>();
            for (float v = lo; v < hi;)
            {
                output.Add(v);
                float d = v < c0 ? c0 - v : v > c1 ? v - c1 : 0;
                v += Mathf.Min(coarse, fine + d * grow);
            }
            output.Add(hi);
            return output;
        }
        // three 的 computeVertexNormals（索引网格：面法线（未归一化的叉积）按顶点累加后归一化）；pos / idx 为 three 坐标与绕序
        static Vector3[] VertexNormals(Vector3[] pos, int[] idx)
        {
            var nr = new Vector3[pos.Length];
            for (int i = 0; i < idx.Length; i += 3)
            {
                Vector3 a = pos[idx[i]], b = pos[idx[i + 1]], c = pos[idx[i + 2]];
                var n = Vector3.Cross(c - b, a - b);
                nr[idx[i]] += n; nr[idx[i + 1]] += n; nr[idx[i + 2]] += n;
            }
            for (int i = 0; i < nr.Length; i++) { float l = nr[i].magnitude; nr[i] = l > 0 ? nr[i] / l : Vector3.up; }
            return nr;
        }
        // three 坐标的索引网格 → Unity 网格（z 取反、三角形绕序反转）
        static Mesh ThreeMesh(string name, Vector3[] pos, Vector3[] nrm, Color[] col, int[] idx, List<Vector4> uv2 = null)
        {
            int n = pos.Length;
            var v = new Vector3[n]; var nn = new Vector3[n];
            for (int i = 0; i < n; i++) { v[i] = new Vector3(pos[i].x, pos[i].y, -pos[i].z); nn[i] = new Vector3(nrm[i].x, nrm[i].y, -nrm[i].z); }
            var t = new int[idx.Length];
            for (int i = 0; i < idx.Length; i += 3) { t[i] = idx[i]; t[i + 1] = idx[i + 2]; t[i + 2] = idx[i + 1]; }
            var m = new Mesh { name = name };
            if (n > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = v; m.normals = nn; m.colors = col;
            if (uv2 != null) m.SetUVs(1, uv2);
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }
        void BuildGround()
        {
            var xs = Axis(-115, 115, -13, 13, 0.5f, 8, 0.14f); var zs = Axis(-150, 22, -6, 7, 0.5f, 8, 0.12f);
            int nx = xs.Count, nz = zs.Count, N = nx * nz;
            var pos = new Vector3[N]; var col = new Color[N]; var mat = new List<Vector4>(N);
            Color gc = C(pal.ground), gc2 = C(pal.ground2), lane = C(pal.lane);
            int i = 0;
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++, i++)
                {
                    float x = xs[ix], z = zs[iz], y = Height(x, z);
                    pos[i] = new Vector3(x, y, z);
                    var tc = Color.Lerp(gc, gc2, Mathf.Clamp01(Mathf.PerlinNoise(x * 0.09f + 2, z * 0.09f) * 1.3f - 0.15f));
                    // 对决场地：踩实的泥土（边缘带噪声，两头逐渐过渡到草）
                    float lk = (1 - Ss(1.0f + Mathf.PerlinNoise(x * 0.3f, 7) * 1.4f, 3.4f + Mathf.PerlinNoise(x * 0.21f, 3) * 1.5f, Mathf.Abs(z))) * (1 - Ss(11, 16, Mathf.Abs(x)));
                    tc = LerpU(tc, lane, lk * 0.92f);
                    float sand = 0, rockT = 0, snow = 0, farm = 1;
                    if (kind == "hill") { rockT = 0.55f; snow = Ss(17, 26, y); }
                    if (kind == "river")
                    {
                        float bank = (1 - Ss(-2.6f, -4.2f, z)) * Ss(-1.0f, -2.6f, z) + Ss(-23.5f, -26.5f, z) * (1 - Ss(-27, -30, z));
                        sand = Mathf.Max(sand, bank * 0.9f);
                        if (z < -3.2f && z > -26.8f) sand = 1;
                    }
                    if (kind == "castle") tc = LerpU(tc, lane, 0.4f * (1 - Ss(6, 12, -z)));
                    // 远处的农田（平原）
                    if (kind == "plain" && z < -30 && z > -75) farm = 1 - 0.8f * Ss(0.42f, 0.6f, Mathf.PerlinNoise(x * 0.03f + 9, z * 0.03f + 4) + Ss(-30, -40, z) * 0.25f - 0.2f);
                    tc.a = 1;
                    col[i] = tc;
                    mat.Add(new Vector4(sand, rockT, snow, farm));
                }
            var idx = new int[(nx - 1) * (nz - 1) * 6];
            int k = 0;
            for (int iz = 0; iz < nz - 1; iz++)
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    int a = iz * nx + ix, b = a + 1, c = a + nx, d = c + 1;
                    idx[k++] = a; idx[k++] = c; idx[k++] = b; idx[k++] = b; idx[k++] = c; idx[k++] = d;
                }
            var nrm = VertexNormals(pos, idx);
            var mesh = ThreeMesh("DuelGround", pos, nrm, col, idx, mat);
            ground = AddMesh(mesh, GroundMaterial(), false, true);
            groundTris = idx.Length / 3;
        }
        // three 的 Color.lerp（不钳制 t 以外的值；t 在 0..1）
        static Color LerpU(Color a, Color b, float t) { return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1); }

        // 天际线：最远处一道雾中的山脊（平滑带状网格）
        void BuildFarRidge()
        {
            int n = 160, rows = 4;
            var pos = new List<Vector3>(); var col = new List<Color>(); var idx = new List<int>();
            Color c0 = C(pal.far), c1 = C(pal.mid);
            float hgt = kind == "hill" ? 46 : kind == "castle" ? 22 : 30;
            for (int i = 0; i <= n; i++)
            {
                float x = -330 + 660f * i / n;
                float crest = hgt * (0.45f + 0.75f * Mathf.PerlinNoise(x * 0.008f + 3, 1.7f) + 0.25f * Mathf.PerlinNoise(x * 0.03f, 4.2f));
                float z = -185 + Mathf.Sin(x * 0.012f) * 18;
                for (int r = 0; r <= rows; r++)
                {
                    float u = (float)r / rows, y = -8 + (crest + 8) * (1 - (1 - u) * (1 - u));
                    pos.Add(new Vector3(x, y, z + u * 6));
                    col.Add(LerpU(c1, c0, 0.5f + 0.5f * u));
                }
            }
            for (int i = 0; i < n; i++)
                for (int r = 0; r < rows; r++)
                {
                    int a = i * (rows + 1) + r, b = a + 1, c = a + rows + 1, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
                }
            var P = pos.ToArray(); var I = idx.ToArray();
            AddMesh(ThreeMesh("DuelRidge", P, VertexNormals(P, I), col.ToArray(), I), env, false, false);
        }

        // ---------------------------------------------------------- 景物 --
        // 实例化景物：种类 → [{ x, y, z, s, yaw, sx?, sy?, col }]（three 坐标）
        sealed class Inst { public float x, y, z, s, yaw, sx = 1, sy = 1, sink; public Color col = Color.white; }
        readonly Dictionary<string, List<Inst>> inst = new Dictionary<string, List<Inst>>();
        readonly List<string> instOrder = new List<string>();
        sealed class VegBatch { public Mesh mesh; public Material mat; public Matrix4x4[][] m; public Vector4[][] c; public int[] n; public bool cast; public MaterialPropertyBlock[] mpb; }
        readonly List<VegBatch> veg = new List<VegBatch>();
        public int VegInstances { get { int s = 0; foreach (var l in inst.Values) s += l.Count; return s; } }

        // 放置（y 取地面高度、偏航随机）；opt 的字段在调用前已按网页版的顺序取过随机数
        void Place(string kind, float x, float z, float s, Inst opt)
        {
            var o = opt ?? new Inst();
            o.x = x; o.y = Height(x, z); o.z = z; o.s = s;
            o.yaw = Rnd() * Mathf.PI * 2;
            List<Inst> L;
            if (!inst.TryGetValue(kind, out L)) { L = new List<Inst>(); inst[kind] = L; instOrder.Add(kind); }
            L.Add(o);
        }
        void FlushInstances()
        {
            foreach (var kd in instOrder)
            {
                var L = inst[kd];
                if (L.Count == 0) continue;
                Mesh geo = kd.StartsWith("rock") ? DuelVeg.Rock(kd[4] - '0') : kd == "tuft" || kd == "reed" ? DuelVeg.Tuft(kd == "reed") : kd == "flower" ? DuelVeg.Flower() : DuelVeg.Tree(kd);
                var mat = DuelVeg.Material(kd.StartsWith("rock") ? "rock" : kd == "tuft" || kd == "reed" || kd == "flower" ? "grass" : "leaf");
                int nb = (L.Count + 1022) / 1023;
                var b = new VegBatch { mesh = geo, mat = mat, m = new Matrix4x4[nb][], c = new Vector4[nb][], n = new int[nb], cast = kd.StartsWith("rock"), mpb = new MaterialPropertyBlock[nb] };
                for (int k = 0; k < nb; k++)
                {
                    int cnt = Math.Min(1023, L.Count - k * 1023);
                    b.m[k] = new Matrix4x4[cnt]; b.c[k] = new Vector4[cnt]; b.n[k] = cnt;
                    for (int j = 0; j < cnt; j++)
                    {
                        var o = L[k * 1023 + j];
                        // three：位置 (x, y − sink·s, z)、Euler(0, yaw, 0)、缩放 (s·sx, s·sy, s·sx) → Unity（z 取反，绕 y 的角取反）
                        var p = new Vector3(o.x, o.y - o.sink * o.s, -o.z);
                        var q = Quaternion.AngleAxis(-o.yaw * Mathf.Rad2Deg, Vector3.up);
                        b.m[k][j] = Matrix4x4.TRS(p, q, new Vector3(o.s * o.sx, o.s * o.sy, o.s * o.sx));
                        b.c[k][j] = new Vector4(o.col.r, o.col.g, o.col.b, 1);
                    }
                    b.mpb[k] = new MaterialPropertyBlock();
                    b.mpb[k].SetVectorArray("_InstColor", b.c[k]);
                }
                veg.Add(b);
            }
        }
        // 每帧提交植被 / 石块（实例化；不支持实例化时逐个绘制）
        public void DrawVeg()
        {
            var root = group.transform.localToWorldMatrix;
            bool instanced = SystemInfo.supportsInstancing;
            foreach (var b in veg)
            {
                if (b.mesh == null || b.mat == null) continue;
                var sc = b.cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                for (int k = 0; k < b.m.Length; k++)
                {
                    if (instanced)
                    {
                        var ms = b.m[k];
                        if (!(root == Matrix4x4.identity)) { ms = new Matrix4x4[b.n[k]]; for (int j = 0; j < ms.Length; j++) ms[j] = root * b.m[k][j]; }
                        Graphics.DrawMeshInstanced(b.mesh, 0, b.mat, ms, b.n[k], b.mpb[k], sc, true, layer);
                    }
                    else
                    {
                        var one = new MaterialPropertyBlock();
                        for (int j = 0; j < b.n[k]; j++)
                        {
                            one.SetVector("_InstColor", b.c[k][j]);
                            Graphics.DrawMesh(b.mesh, root * b.m[k][j], b.mat, layer, null, 0, one, sc, true, null, false);
                        }
                    }
                }
            }
        }

        void Tree(float x, float z, float s, bool pine)
        {
            var leaf = C(pal.leaf);
            float j = (Rnd() - 0.5f) * 0.22f;
            string k = (pine ? (Rnd() < 0.5f ? "pine" : "spruce") : (Rnd() < 0.5f ? "broad" : "broad2")) + (z < -22 ? "F" : "");
            var cc = Mul(leaf, 1 + j);
            Place(k, x, z, s * (pine ? 5.2f : 5.6f), new Inst { col = Q8(cc), sink = 0.01f });
        }
        void Rock(float x, float z, float s, string col = null)
        {
            var c = Mul(C(col ?? pal.rock), 0.95f + Rnd() * 0.15f);
            string k = "rock" + Mathf.FloorToInt(Rnd() * 3);
            var o = new Inst { col = Q8(c), sink = 0.08f, sy = 0.85f + Rnd() * 0.4f };
            Place(k, x, z, s * 2.4f, o);
        }
        static readonly string[] FlowerCols = { "#f4f0e4", "#f2d24a", "#d8483a", "#b07ad8", "#f0a0c0" };
        void Grass(int n, float zMin, float zMax, float xr, float density = 0.97f)
        {
            Color g0 = C(pal.leaf), g1 = C(pal.ground2);
            for (int i = 0; i < n; i++)
            {
                float x = (Rnd() * 2 - 1) * xr, z = zMin + Rnd() * (zMax - zMin);
                float laneK = (1 - Ss(1.6f, 3.6f, Mathf.Abs(z))) * (1 - Ss(11, 15, Mathf.Abs(x)));
                if (Rnd() < laneK * density) continue;                                // 场中踩实，少草
                if (z > 2.5f && (Mathf.Abs(x) < 4 || Rnd() < 0.5f)) continue;          // 镜头近处稀疏
                if (z > 2.2f) continue;                                                // 第三版修订：镜头前最近的一带不放草
                float kk = Rnd();
                var c = Mul(LerpU(g0, g1, kk * 0.55f), 0.95f + Rnd() * 0.3f);
                float sc = (1.1f + Rnd() * 1.1f) * (z > 2.5f ? 0.7f : 1);
                Place("tuft", x, z, sc, new Inst { col = Q8(c) });
                if (Rnd() < 0.08f)
                {
                    var fc = C(FlowerCols[Mathf.FloorToInt(Rnd() * 5)]);
                    float fx = x + (Rnd() - 0.5f) * 0.6f, fz = z + (Rnd() - 0.5f) * 0.6f, fs = 1.6f + Rnd() * 0.8f;
                    Place("flower", fx, fz, fs, new Inst { col = fc });
                }
            }
        }
        void Scatter(int n, Action<float, float, float> fn, float zMin, float zMax, float xr = 30)
        {
            for (int i = 0; i < n; i++)
            {
                float x = (Rnd() * 2 - 1) * xr, z = zMin + Rnd() * (zMax - zMin);
                if (Mathf.Abs(z) < 2.4f && Mathf.Abs(x) < 12) continue;
                if (z > 2.2f && Mathf.Abs(x) < 5) continue;          // 镜头前方中央留空
                fn(x, z, Rnd());
            }
        }
        // 断枪残旗、弃盾（平原、丘陵）
        void Debris(MeshBuilder mb, int n, float z0, float z1, Color colA, Color colB)
        {
            Color wood = C("#5a3a22"), steel = C("#b8c0ca");
            for (int i = 0; i < n; i++)
            {
                float x = (Rnd() * 2 - 1) * 11, z = z0 - Rnd() * (z0 - z1);
                float a = (Rnd() - 0.5f) * 0.7f, y0 = Height(x, z) - 0.15f;
                float L = 1.5f + Rnd() * 0.6f;
                var top = U(x + Mathf.Sin(a) * L, y0 + Mathf.Cos(a) * L, z);
                mb.Tube(new[] { U(x, y0, z), U(x + Mathf.Sin(a) * L * 0.5f, y0 + Mathf.Cos(a) * L * 0.5f, z), top }, new[] { 0.024f, 0.022f, 0.02f }, 7, wood);
                mb.Lathe(top, new[] { new LP(0.022f, 0), new LP(0.034f, 0.05f), new LP(0, 0.26f) }, 8, steel);
                if (i % 3 == 0)
                {
                    var fc = i % 2 != 0 ? colA : colB;
                    // top 是 Unity 坐标：网页版 fx = top.x、fy = top.y − 0.1（z 用旗杆的 z）
                    float fx = top.x, fy = top.y - 0.1f;
                    Vector3 p0 = U(fx, fy, z), p1 = U(fx + 0.5f, fy - 0.1f, z + 0.03f), p2 = U(fx + 0.46f, fy - 0.45f, z + 0.05f), p3 = U(fx, fy - 0.38f, z);
                    var n0 = V3(0, 0, 1); var nb = V3(0, 0, -1);
                    mb.TriN(p0, p1, p2, n0, n0, n0, fc); mb.TriN(p0, p2, p3, n0, n0, n0, fc);
                    mb.TriN(p0, p2, p1, nb, nb, nb, fc); mb.TriN(p0, p3, p2, nb, nb, nb, fc);
                }
            }
            for (int i = 0; i < 2; i++)
            {   // 弃盾（斜倒在地上的圆盾：铜边、盾面、铜钉）
                float x = (Rnd() * 2 - 1) * 9, z = -3.4f - Rnd() * 2.6f, y = Height(x, z);
                Color c = Mul(i % 2 != 0 ? colA : colB, 0.7f), rim = C("#8a6a3a"), boss = C("#c9a24e");
                var sb = new MeshBuilder();
                float yaw = Rnd() * 6.28f;
                // 网页版：geo.rotateX(−1.25)、rotateY(yaw)、translate(x, y + 0.2, z)（three）→ Unity：T(x, y + 0.2, −z)·Ry(−yaw)·Rx(1.25)
                sb.M = Matrix4x4.Translate(new Vector3(x, y + 0.2f, -z)) * Matrix4x4.Rotate(Quaternion.AngleAxis(-yaw * Mathf.Rad2Deg, Vector3.up)) * Matrix4x4.Rotate(Quaternion.AngleAxis(1.25f * Mathf.Rad2Deg, Vector3.right));
                sb.Lathe(V3(0, 0, 0), new[] { new LP(0.36f, -0.01f, rim), new LP(0.37f, 0.02f, rim, true), new LP(0.33f, 0.04f, c), new LP(0.18f, 0.07f, c), new LP(0.08f, 0.08f, boss), new LP(0.05f, 0.12f, boss), new LP(0, 0.13f, boss) }, 20, c);
                AddMesh(sb.ToMesh("DuelShield"), env, true, true);
            }
        }
        void BuildProps(MeshBuilder mb, Color colA, Color colB)
        {
            switch (kind)
            {
                case "plain":
                    Grass(900, -14, 8, 24);
                    Scatter(16, (x, z, k) => Rock(x, z, 0.12f + k * 0.2f), -14, 6);
                    Debris(mb, 9, -2.8f, -6.4f, colA, colB);
                    for (int i = 0; i < 16; i++) { float x = (Rnd() * 2 - 1) * 42, z = -17 - Rnd() * 20, s = 0.85f + Rnd() * 0.55f; bool p = Rnd() < 0.35f; Tree(x, z, s, p); }
                    for (int i = 0; i < 10; i++)
                    {
                        float x = (Rnd() * 2 - 1) * 30, z = -9 - Rnd() * 9, s = 3 + Rnd() * 2;
                        var c = Q8(Mul(C(pal.leaf), 0.9f + Rnd() * 0.25f));
                        Place("shrub", x, z, s, new Inst { col = c });
                    }
                    break;
                case "forest":
                    for (int i = 0; i < 70; i++)
                    {
                        float x = (Rnd() * 2 - 1) * 44, z = -5.5f - Rnd() * 34;
                        if (z > -11 && Mathf.Abs(x) < 5.5f && Rnd() < 0.6f) continue;
                        float s = 0.9f + Rnd() * 0.7f; bool p = Rnd() < 0.55f;
                        Tree(x, z, s, p);
                    }
                    foreach (var x in new[] { -11.5f, -9.2f, 9.6f, 12.2f }) { float z = -3.8f - Rnd(), s = 1.1f + Rnd() * 0.25f; Tree(x, z, s, x > 0); }
                    Grass(800, -12, 8, 22, 0.9f);
                    for (int i = 0; i < 26; i++)
                    {
                        float x = (Rnd() * 2 - 1) * 26, z = -3.4f - Rnd() * 9;
                        if (Mathf.Abs(z) < 3 && Mathf.Abs(x) < 12) continue;
                        float s = 2.2f + Rnd() * 1.6f;
                        var c = Q8(Mul(C(pal.leaf), 0.95f + Rnd() * 0.3f));
                        Place("shrub", x, z, s, new Inst { col = c });
                    }
                    Scatter(10, (x, z, k) => Rock(x, z, 0.15f + k * 0.25f, "#6a6a5a"), -10, 5);
                    {   // 倒木（树干 + 断枝）
                        float y = Height(-3, -4.6f) + 0.18f;
                        mb.Tube(new[] { U(-4.6f, y, -4.4f), U(-3.2f, y + 0.05f, -4.6f), U(-1.4f, y + 0.02f, -4.9f) }, new[] { 0.24f, 0.22f, 0.17f }, 12, C("#5a4030"));
                        mb.Tube(new[] { U(-3.0f, y + 0.12f, -4.6f), U(-2.7f, y + 0.6f, -4.3f) }, new[] { 0.06f, 0.03f }, 7, C("#5a4030"));
                    }
                    break;
                case "hill":
                    for (int i = 0; i < 14; i++) { float x = (Rnd() * 2 - 1) * 30, z = -6 - Rnd() * 12, s = 0.5f + Rnd() * 1.2f; Rock(x, z, s); }
                    foreach (var r in new[] { new[] { -19f, -17, 4.2f }, new[] { -10f, -22, 5.5f }, new[] { 15f, -18, 5 }, new[] { 24f, -24, 6 }, new[] { 3f, -28, 5 } }) Rock(r[0], r[1], r[2], "#6a655c");
                    Scatter(26, (x, z, k) => Rock(x, z, 0.1f + k * 0.2f), -10, 6);
                    for (int i = 0; i < 20; i++) { float x = (Rnd() * 2 - 1) * 42, z = -12 - Rnd() * 24, s = 0.75f + Rnd() * 0.5f; Tree(x, z, s, true); }
                    Grass(650, -12, 8, 22, 0.95f);
                    Debris(mb, 5, -2.8f, -6, colA, colB);
                    break;
                case "river":
                    {
                        for (int i = 0; i < 120; i++)
                        {
                            float x = (Rnd() * 2 - 1) * 34, z = -2.9f - Rnd() * 1.4f, s = 1.0f + Rnd() * 0.6f;
                            var c = C(Rnd() < 0.5f ? "#a8a86a" : "#86985a");
                            Place("reed", x, z, s, new Inst { col = c });
                        }
                        for (int i = 0; i < 40; i++) { float x = (Rnd() * 2 - 1) * 40, z = -25.5f - Rnd() * 1.6f, s = 1.1f + Rnd() * 0.5f; Place("reed", x, z, s, new Inst { col = C("#8a9a5c") }); }
                        Scatter(30, (x, z, k) => Rock(x, z, 0.08f + k * 0.14f), -3, 6);
                        for (int i = 0; i < 26; i++) { float x = (Rnd() * 2 - 1) * 52, z = -28 - Rnd() * 12, s = 1 + Rnd() * 0.6f; bool p = Rnd() < 0.4f; Tree(x, z, s, p); }
                        Grass(650, -2.6f, 8, 24);
                        BuildWater();
                        break;
                    }
                case "castle":
                    {
                        // 城墙（砖石纹理）、垛口、马面、城门与门钉、城门楼（两重檐）、角楼、火把
                        Color stone = C("#7a7266"); float z = -12.5f, H = 6.6f;
                        var wall = new MeshBuilder();
                        wall.Box(U(0, H / 2, z), V3(110, H, 2.4f), stone);
                        // 两道循环的步进按 double 累加（= 网页版 JS 的数，免得 float 累积误差让垛口 / 扶壁位置漂移）
                        for (double xd = -55; xd <= 55; xd += 1.4) { float x = (float)xd; wall.Box(U(x, H + 0.35f, z + 1.0f), V3(0.8f, 0.7f, 0.4f), Mul(stone, 1.05f)); }
                        foreach (var x in new[] { -14f, 14f }) wall.Box(U(x, H / 2 + 0.7f, z + 0.2f), V3(4.4f, H + 1.4f, 3.2f), Mul(stone, 0.96f));
                        // 第三版修订：墙身每隔约 3 米一道扶壁（墙面有进深）
                        for (double xd = -48; xd <= 48; xd += 3.2) { if (Math.Abs(xd) < 4.6 || Math.Abs(Math.Abs(xd) - 14) < 2.8) continue; float x = (float)xd; wall.Box(U(x, H * 0.42f, z + 1.45f), V3(0.7f, H * 0.84f, 0.55f), Mul(stone, 0.93f)); }
                        wall.Box(U(0, 0.05f, z + 1.4f), V3(6.0f, 0.1f, 1.2f), Mul(stone, 0.9f));
                        // 网页版 addMesh(…, true, true)，但城墙整面都在主光阴影相机（场地 ±7 米）之外，从不受影：
                        // Unity 的阴影距离 30 米会把火把与扶壁的影子投到墙面上，所以这里不接收阴影（= 网页版的观感）
                        AddMesh(wall.ToMesh("DuelWall"), BrickMaterial(), true, false);
                        // 城门：门板与门钉
                        mb.Box(U(0, 1.9f, z + 1.25f), V3(3.2f, 3.8f, 0.1f), C("#2a1a10"));
                        foreach (var yy in new[] { 0.7f, 1.4f, 2.1f, 2.8f, 3.4f }) foreach (var xx in new[] { -1.1f, -0.37f, 0.37f, 1.1f }) mb.Sphere(U(xx, yy, z + 1.32f), V3(0.06f, 0.06f, 0.04f), C("#b8963c"), 1, new SphereOpt());
                        mb.Box(U(0, 4.0f, z + 1.27f), V3(3.8f, 0.34f, 0.14f), C("#4a3a2a"));
                        mb.Box(U(0, H + 1.2f, z), V3(8.5f, 2.4f, 3.4f), C("#7a2418"));
                        foreach (var x in new[] { -3.7f, -1.25f, 1.25f, 3.7f }) mb.Lathe(U(x, H, z + 1.72f), new[] { new LP(0.15f, 0), new LP(0.14f, 2.4f) }, 10, C("#5a1810"));
                        mb.ChineseRoof(U(0, H + 2.4f, z), 10.8f, 5.0f, 2.8f, C("#262a36"));
                        mb.Box(U(0, H + 3.5f, z), V3(5.6f, 1.2f, 2.4f), C("#7a2418"));
                        mb.ChineseRoof(U(0, H + 4.1f, z), 7.4f, 3.6f, 2.4f, C("#262a36"));
                        foreach (var x in new[] { -14f, 14f }) mb.ChineseRoof(U(x, H + 1.4f, z + 0.2f), 5.4f, 4.0f, 1.8f, C("#262a36"));
                        // 第三版修订：城头垂下的长幡（两军颜色），远处再一道城内的屋脊（视差层次）
                        var hang = new[] { new KeyValuePair<float, Color>(-8.2f, colA), new KeyValuePair<float, Color>(-5.6f, colB), new KeyValuePair<float, Color>(5.6f, colA), new KeyValuePair<float, Color>(8.2f, colB) };
                        foreach (var kv in hang)
                        {
                            float x = kv.Key; var c0 = kv.Value; c0.a = 1;
                            mb.Box(U(x, H - 1.5f, z + 1.28f), V3(0.95f, 2.9f, 0.05f), c0);
                            mb.Box(U(x, H - 3.0f, z + 1.29f), V3(0.95f, 0.12f, 0.06f), Mul(c0, 0.6f));
                            mb.Box(U(x, H - 0.06f, z + 1.29f), V3(1.15f, 0.1f, 0.1f), C("#3a2a1a"));
                        }
                        foreach (var x in new[] { -30f, -20f, 22f, 32f }) mb.ChineseRoof(U(x, H + 0.6f, z - 6), 7.0f, 4.0f, 2.0f, C("#2a2e38"));
                        foreach (var x in new[] { -3.6f, 3.6f })
                        {
                            mb.Tube(new[] { U(x, 0, -9.6f), U(x, 1.2f, -9.6f), U(x, 2.4f, -9.6f) }, new[] { 0.07f, 0.065f, 0.06f }, 8, C("#2a1e14"));
                            mb.Lathe(U(x, 2.4f, -9.6f), new[] { new LP(0.06f, 0), new LP(0.2f, 0.18f), new LP(0.22f, 0.26f), new LP(0.16f, 0.28f) }, 12, C("#2a2424"));
                            torches.Add(new Vector3(x, 2.85f, -9.6f));
                        }
                        Scatter(20, (x, zz, k) => Rock(x, zz, 0.08f + k * 0.14f), -8, 6);
                        Grass(350, -8, 8, 22, 1);
                        break;
                    }
            }
        }
        // 河川的水面：网页版 PlaneGeometry(160, 24, 40, 8)，平放、y = −0.22、中心 z = −15（three）；顶点色 r = 离岸远近
        void BuildWater()
        {
            int gx = 40, gz = 8;
            var v = new Vector3[(gx + 1) * (gz + 1)]; var c = new Color[v.Length]; var nr = new Vector3[v.Length];
            for (int iz = 0; iz <= gz; iz++)
                for (int ix = 0; ix <= gx; ix++)
                {
                    float x = -80 + 160f * ix / gx, z3 = -15 - 12 + 24f * iz / gz;
                    int i = iz * (gx + 1) + ix;
                    v[i] = new Vector3(x, -0.22f, -z3);
                    nr[i] = Vector3.up;
                    c[i] = new Color(Mathf.Clamp01(1 - Mathf.Min(-3.4f - z3, z3 + 26.6f) / 5), 0, 0, 1);
                }
            var t = new List<int>();
            for (int iz = 0; iz < gz; iz++)
                for (int ix = 0; ix < gx; ix++)
                {
                    int a = iz * (gx + 1) + ix, b = a + 1, d = a + gx + 1, e = d + 1;
                    // Unity 正面朝上：叉积 (b − a) × (c − a) 指向 +y
                    AddUp(t, v, a, b, d); AddUp(t, v, b, e, d);
                }
            var m = new Mesh { name = "DuelWater" };
            m.vertices = v; m.normals = nr; m.colors = c; m.SetTriangles(t, 0); m.RecalculateBounds();
            owned.Add(m);
            var mat = new Material(Art.LoadShader("DuelWater")) { name = "DuelWater" };
            mat.SetTexture("_Ripple", Art.Ripple);
            owned.Add(mat);
            var go = Art.MakeObject("DuelWater", m, mat, group.transform, false);
            go.layer = layer;
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }
        static void AddUp(List<int> t, Vector3[] v, int a, int b, int c)
        {
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y >= 0) { t.Add(a); t.Add(b); t.Add(c); } else { t.Add(a); t.Add(c); t.Add(b); }
        }

        // ------------------------------------------------------ 材质 --
        // 地面：地形着色器的顶点版（Sanguo/DuelGround：高度换算为「海拔」1.2 + y × 0.32，场地 y = 0 不当作海滩，远山山顶才见岩石）
        static Material groundMat, brickMat, propMat;
        // 杂项（顶点色 LowPoly）：Sanguo/DuelProp，_HL = 1、无砖纹
        static Material PropMaterial()
        {
            if (propMat != null) return propMat;
            propMat = new Material(Art.LoadShader("DuelProp")) { name = "DuelLowPoly" };
            propMat.SetFloat("_Brick", 0); propMat.SetFloat("_HL", 1); propMat.SetFloat("_Cull", (float)CullMode.Back);
            return propMat;
        }
        static Material GroundMaterial()
        {
            if (groundMat != null) return groundMat;
            groundMat = new Material(Art.LoadShader("DuelGround")) { name = "DuelGround" };
            groundMat.SetTexture("_DetA", Art.TerrainDetailA);
            groundMat.SetTexture("_DetB", Art.TerrainDetailB);
            return groundMat;
        }
        // 城墙砖石：竖直面为错缝砖（0.44 × 0.21 米），平顶为方砖；只作用于低饱和度的石色（Sanguo/DuelProp，_Brick = 1）
        static Material BrickMaterial()
        {
            if (brickMat != null) return brickMat;
            brickMat = new Material(Art.LoadShader("DuelProp")) { name = "DuelBrick" };
            brickMat.SetFloat("_Brick", 1); brickMat.SetFloat("_HL", 1); brickMat.SetFloat("_Cull", (float)CullMode.Back);
            return brickMat;
        }

        // ------------------------------------------------------ 两军阵列 --
        void BuildArmies(Color colA, Color colB)
        {
            var blocks = kind == "castle"
                ? new[] { new float[] { 1, -10, -4.5f, -12.2f, -12.8f, 6.6f, 2, 5 }, new float[] { 1, 4.5f, 10, -12.2f, -12.8f, 6.6f, 2, 5 }, new float[] { 0, -17, -9, -8.2f, -10.4f, 0, 3, 6 } }
                : new[] { new float[] { 0, -15.5f, -8.5f, -11, -14, 0, 3, 6 }, new float[] { 1, 8.5f, 15.5f, -11, -14, 0, 3, 6 } };
            var cols = new[] { Hex(colA), Hex(colB) };
            crowd = new FigureCrowd(new FigCrowdOptions { lod = "low", shadow = false, parent = group.transform });
            crowd.gameObject.layer = layer;
            foreach (var bk in blocks)
            {
                int s = (int)bk[0]; float x0 = bk[1], x1 = bk[2], z0 = bk[3], z1 = bk[4], y0 = bk[5]; int rows = (int)bk[6], n = (int)bk[7];
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < n; j++)
                    {
                        float x = x0 + (x1 - x0) * j / (n - 1) + (Rnd() - 0.5f) * 0.35f, z = z0 + (z1 - z0) * i / Mathf.Max(1, rows - 1) + (Rnd() - 0.5f) * 0.3f;
                        string type = (i + j) % 3 == 2 ? "sword" : "spear";
                        int id = crowd.Add(new FigCrowdSpec { type = type, culture = cul[s], color = cols[s], seed = (i * 7 + j) % 3 });
                        float y = y0 != 0 ? y0 : Height(x, z);
                        crowd.SetTransform(id, Figure.PosFromJs(x, y, z), Figure.YawFromJs(Math.Atan2(-x * 0.5, -z)), 1);
                        double ph = Rnd(), sp = 0.85 + Rnd() * 0.3;
                        crowd.Play(id, "idle", null, ph, sp);
                        troops[s].Add(id);
                    }
            }
            // 不支持实例化的设备：士兵群退回每人一个 Figure（挂在士兵群根下、缺省层），改到单挑的层，单挑镜头才看得见
            if (!SystemInfo.supportsInstancing) SetLayerAll(crowd.Root, layer);
        }
        static void SetLayerAll(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayerAll(c, layer);
        }
        // 胜方欢呼
        public void Cheer(int side, float t)
        {
            if (crowd == null || cheered) return;
            cheered = true;
            foreach (var id in troops[side])
            {
                double t0 = t - Rnd() * 0.4, sp = 0.9 + Rnd() * 0.25;
                crowd.Play(id, "cheer", t0, 0, sp);
            }
        }

        // ------------------------------------------------------ 帅旗 --
        // 旗面贴图：势力色底 + 白圆中写姓氏
        Texture BannerTexture(Color color, string glyph, bool pennant)
        {
            var r = new Raster(128, 256);
            r.FillColor = "#" + ColorUtility.ToHtmlStringRGB(color); r.FillRect(0, 0, 128, 256);
            r.FillColor = "rgba(0,0,0,.28)"; r.FillRect(0, 0, 10, 256);
            r.StrokeColor = "rgba(255,236,190,.75)"; r.LineWidth = 5; var br = new Path2D(); br.Rect(14, 10, 104, 216); r.Stroke(br);
            // 锯齿边
            r.FillColor = "rgba(0,0,0,.35)";
            for (int y = 0; y < 256; y += 16) { var t = new Path2D(); t.MoveTo(128, y); t.LineTo(116, y + 8); t.LineTo(128, y + 16); t.ClosePath(); r.Fill(t); }
            var bt = new Path2D(); bt.MoveTo(0, 256); for (int x = 0; x <= 128; x += 16) { bt.LineTo(x, 240); bt.LineTo(x + 8, 256); }
            bt.ClosePath(); r.Fill(bt);
            if (!pennant)
            {
                r.FillColor = "#f4ead2"; var cp = new Path2D(); cp.Arc(66, 112, 46, 0, Mathf.PI * 2); cp.ClosePath(); r.Fill(cp);
                r.StrokeColor = "rgba(0,0,0,.25)"; r.LineWidth = 3; r.Stroke(cp);
            }
            var tex = r.ToTexture(); tex.anisoLevel = 2; owned.Add(tex);
            if (pennant || string.IsNullOrEmpty(glyph) || UIKit.Title == null) return tex;
            return DuelGlyph.Render(tex, glyph, 66, 116, 66, C("#1a1210"), owned);
        }

        // 旗面：网页版 PlaneGeometry(w, h, 10, 8) 平移 (w/2, −h/2)（局部 x 0..w、y 0..−h），Lambert 双面；每帧按风吹形变并重算法线
        void AddBanner(float x, float z, float poleH, float w, float h, Color color, string glyph, bool pennant, float phase)
        {
            var tex = BannerTexture(color, glyph, pennant);
            var mat = new Material(Art.LoadShader("DuelProp")) { name = "DuelBanner", mainTexture = tex };
            mat.SetFloat("_HL", 0); mat.SetFloat("_Brick", 0); mat.SetFloat("_Cull", (float)CullMode.Off);
            owned.Add(mat);
            int gx = 10, gy = 8; var bs = new Vector3[(gx + 1) * (gy + 1)]; var uv = new Vector2[bs.Length]; var cs = new Color[bs.Length]; var tris = new List<int>();
            for (int iy = 0; iy <= gy; iy++)
                for (int ix = 0; ix <= gx; ix++)
                {
                    int i = iy * (gx + 1) + ix;
                    bs[i] = new Vector3(w * ix / gx, -h * iy / gy, 0);
                    uv[i] = new Vector2((float)ix / gx, 1 - (float)iy / gy);
                    cs[i] = Color.white;
                }
            for (int iy = 0; iy < gy; iy++)
                for (int ix = 0; ix < gx; ix++)
                {
                    // three 的 PlaneGeometry 正面朝 +z（three）= Unity −z：Unity 三角形 (a, c, b)、(b, c, d) 的叉积指向 −z
                    int a = iy * (gx + 1) + ix, b = a + 1, c = a + gx + 1, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var mesh = new Mesh { name = "DuelBanner" };
            mesh.MarkDynamic();
            var cur = new Vector3[bs.Length];
            for (int i = 0; i < bs.Length; i++) cur[i] = new Vector3(bs[i].x, bs[i].y, -bs[i].z);
            mesh.vertices = cur; mesh.uv = uv; mesh.colors = cs; mesh.SetTriangles(tris, 0); mesh.RecalculateNormals();
            mesh.bounds = new Bounds(new Vector3(w / 2, -h / 2, 0), new Vector3(w + 1, h + 1, 2));
            float y0 = Height(x, z);
            var cloth = AddMesh(mesh, mat, false, false);
            cloth.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            DuelXf.Pos(cloth.transform, x + 0.05f, y0 + poleH - 0.1f, z);
            var mb = new MeshBuilder();
            Color wood = C("#4a2c1a");
            mb.Lathe(U(x, y0 - 0.1f, z), new[] { new LP(0.06f, 0), new LP(0.05f, 0.3f), new LP(0.045f, poleH * 0.5f), new LP(0.04f, poleH + 0.2f) }, 10, wood);
            mb.Tube(new[] { U(x, y0 + poleH - 0.05f, z), U(x + w * 0.5f, y0 + poleH - 0.05f, z), U(x + w + 0.05f, y0 + poleH - 0.05f, z) }, new[] { 0.025f, 0.025f, 0.025f }, 6, wood);
            mb.Lathe(U(x, y0 + poleH + 0.1f, z), new[] { new LP(0.05f, 0), new LP(0.075f, 0.08f), new LP(0.05f, 0.16f), new LP(0, 0.42f) }, 10, C(DuelXf.GOLD));
            mb.Sphere(U(x, y0 + poleH + 0.02f, z), V3(0.09f, 0.11f, 0.09f), C(DuelXf.RED), 1, new SphereOpt { rough = 0.1f });
            AddMesh(mb.ToMesh("DuelBannerPole"), env, true, false);
            banners.Add(new Banner { mesh = mesh, bs = bs, cur = cur, w = w, phase = phase });
        }

        public void Update(float t)
        {
            foreach (var b in banners)
            {
                for (int i = 0; i < b.bs.Length; i++)
                {
                    float x = b.bs[i].x, y = b.bs[i].y;
                    float u = x / b.w;
                    float wave = Mathf.Sin(t * 3.0f + u * 4.4f + y * 0.6f + b.phase) * 0.16f + Mathf.Sin(t * 5.3f + u * 7.0f + b.phase * 2) * 0.05f;
                    b.cur[i] = new Vector3(x - u * u * 0.05f, y - u * u * 0.06f, -(wave * u));
                }
                b.mesh.vertices = b.cur;
                b.mesh.RecalculateNormals();
            }
            if (crowd != null) crowd.SetTime(t);
            DrawVeg();
        }

        public void Dispose()
        {
            if (crowd != null) { crowd.Dispose(); crowd = null; }
            if (group != null) Object.Destroy(group);
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
            veg.Clear();
        }
    }

    // 植被与石块的几何体与材质（= 网页版 treeGeo / rockGeo / tuftGeo / flowerGeo / vegMaterial；跨场次共用，不随单挑释放）
    // 顶点色 rgb = 几何本色，a = sgLeaf（0 = 保持原色：树干；1 = 乘实例颜色、带叶簇 / 石纹细节：树冠、石块、草叶、花瓣）
    public static class DuelVeg
    {
        static readonly Dictionary<string, Mesh> geo = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Vector3 V3(float x, float y, float z) { return new Vector3(x, y, z); }

        // 植被材质：Sanguo/DuelFoliage，参数照网页版 vegMaterial 的 defines 与 uniform——
        //   leaf：叶簇（SG_LEAF_F 3.6、SG_LEAF_N 0.42），风 0.012；rock：石纹（SG_ROCK、3.2、0.3），不摆动；
        //   grass：叶簇公式（14.0、0.1），风 0.22
        public static Material Material(string kind)
        {
            Material m;
            if (mats.TryGetValue(kind, out m) && m != null) return m;
            m = new Material(Art.LoadShader("DuelFoliage")) { name = "DuelVeg_" + kind, enableInstancing = true };
            m.SetFloat("_Kind", kind == "rock" ? 2 : 1);
            m.SetFloat("_LeafF", kind == "rock" ? 3.2f : kind == "leaf" ? 3.6f : 14.0f);
            m.SetFloat("_LeafN", kind == "rock" ? 0.3f : kind == "leaf" ? 0.42f : 0.1f);
            m.SetFloat("_WindK", kind == "grass" ? 0.22f : kind == "leaf" ? 0.012f : 0);
            mats[kind] = m;
            return m;
        }

        // 合并「保持原色」（树干）与「乘实例色、带叶簇细节」（树冠）两部分
        static Mesh Merge(string name, MeshBuilder keep, MeshBuilder tint)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            foreach (var part in new[] { keep, tint })
            {
                if (part == null) continue;
                int o = v.Count; float a = part == tint ? 1 : 0;
                v.AddRange(part.v); n.AddRange(part.n);
                foreach (var cc in part.c) c.Add(new Color(cc.r, cc.g, cc.b, a));
                foreach (var i in part.t) t.Add(i + o);
            }
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
        static Mesh Cached(string key) { Mesh m; return geo.TryGetValue(key, out m) && m != null ? m : null; }

        // 树（高约 1，实例缩放）：broad 圆冠阔叶 · broad2 双团阔叶 · pine 松 · spruce 杉 · shrub 灌丛；名称后缀 F = 远景简化版
        public static Mesh Tree(string kind)
        {
            var hit = Cached(kind); if (hit != null) return hit;
            bool far = kind.EndsWith("F"); string K = far ? kind.Substring(0, kind.Length - 1) : kind;
            var tk = new MeshBuilder(); var lf = new MeshBuilder();
            Color W = new Color(0.93f, 0.93f, 0.93f), W2 = new Color(0.8f, 0.82f, 0.8f), BARK = new Color(0.36f, 0.26f, 0.17f);
            Action<Vector3, Vector3, Color, int, float> crown = (c, r, col, seed, ro) =>
            {
                lf.Sphere(c, r, col, far ? 1 : 2, new SphereOpt { seed = seed, rough = ro, bottomShade = 0.34f, top = 0.08f });
                for (int k = 0; k < (far ? 2 : 4); k++)
                {
                    float a = seed * 1.7f + k * 1.57f, up = 0.2f + 0.32f * ((k + seed) % 2);
                    lf.Sphere(V3(c.x + Mathf.Cos(a) * r.x * 0.66f, c.y + up * r.y, c.z + Mathf.Sin(a) * r.z * 0.66f), V3(r.x * 0.48f, r.y * 0.5f, r.z * 0.48f), k % 2 != 0 ? W2 : W, far ? 0 : 1,
                        new SphereOpt { seed = seed + 7 * k + 1, rough = ro * 0.8f, bottomShade = 0.3f, top = 0.1f });
                }
            };
            Action<float, float> trunk = (h, r0) => tk.Tube(new[] { V3(0, 0, 0), V3(0.012f, h * 0.5f, 0.008f), V3(-0.01f, h, 0) }, new[] { r0, r0 * 0.82f, r0 * 0.6f }, far ? 5 : 9, BARK);
            Action<float, float, float, Color> tier = (y, r, h, col) => lf.Lathe(V3(0, y, 0), far
                ? new[] { new LP(0, h * 0.1f), new LP(r, 0, true), new LP(r * 0.45f, h * 0.55f), new LP(0, h) }
                : new[] { new LP(0, h * 0.12f), new LP(r * 0.9f, h * 0.035f), new LP(r, 0, true), new LP(r * 0.72f, h * 0.2f), new LP(r * 0.46f, h * 0.5f), new LP(r * 0.2f, h * 0.8f), new LP(0, h) },
                far ? 9 : 16, col);
            switch (K)
            {
                case "broad": trunk(0.46f, 0.06f); crown(V3(0, 0.68f, 0), V3(0.44f, 0.38f, 0.44f), W, 3, 0.1f); break;
                case "broad2": trunk(0.42f, 0.055f); crown(V3(0.06f, 0.6f, 0), V3(0.38f, 0.33f, 0.38f), W, 5, 0.1f); crown(V3(-0.16f, 0.82f, 0.05f), V3(0.28f, 0.25f, 0.28f), W2, 9, 0.1f); break;
                case "pine": trunk(0.34f, 0.05f); tier(0.2f, 0.44f, 0.6f, W2); tier(0.52f, 0.34f, 0.54f, W); tier(0.82f, 0.22f, 0.46f, W); break;
                case "spruce": trunk(0.22f, 0.045f); tier(0.12f, 0.34f, 0.5f, W2); tier(0.42f, 0.27f, 0.46f, W); tier(0.72f, 0.19f, 0.44f, W); break;
                default: crown(V3(0, 0.13f, 0), V3(0.3f, 0.2f, 0.3f), W, 17, 0.14f); break;   // shrub
            }
            var m = Merge("DuelTree_" + kind, tk, lf);
            geo[kind] = m;
            return m;
        }
        public static Mesh Rock(int v)
        {
            string key = "rock" + v;
            var hit = Cached(key); if (hit != null) return hit;
            var mb = new MeshBuilder(); var g = new Color(0.92f, 0.92f, 0.92f);
            var R = new[] { V3(0.34f, 0.22f, 0.28f), V3(0.26f, 0.3f, 0.24f), V3(0.42f, 0.15f, 0.3f) }[v];
            mb.Sphere(V3(0, R.y * 0.55f, 0), R, g, 2, new SphereOpt { seed = v * 13 + 5, rough = 0.22f, flat = -0.45f, top = 0.1f, bottomShade = 0.4f });
            if (v == 0) mb.Sphere(V3(0.27f, 0.07f, 0.12f), V3(0.14f, 0.1f, 0.12f), g, 1, new SphereOpt { seed = 77, rough = 0.2f, flat = -0.4f, bottomShade = 0.4f });
            var m = Merge("DuelRock" + v, null, mb);
            geo[key] = m;
            return m;
        }
        // 草丛：六片弯曲的草叶（叶根暗、叶尖亮，两面）；芦苇：高而细的七片叶与穗
        public static Mesh Tuft(bool reed)
        {
            string key = reed ? "reed" : "tuft";
            var hit = Cached(key); if (hit != null) return hit;
            var mb = new MeshBuilder(); var rnd = new SeededRandom(reed ? 19 : 91);
            Func<float> R = () => (float)rnd.NextDouble();
            Color dark = new Color(0.64f, 0.64f, 0.62f), lite = new Color(1.06f, 1.06f, 1.0f), mid = new Color(0.8f, 0.8f, 0.78f);
            int n = reed ? 7 : 6;
            for (int k = 0; k < n; k++)
            {
                float a = k * 0.8f + R() * 0.6f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float r0 = 0.02f + R() * 0.05f, h = reed ? 0.7f + R() * 0.5f : 0.13f + R() * 0.12f, lean = reed ? 0.06f + R() * 0.08f : 0.05f + R() * 0.07f, w = reed ? 0.016f : 0.026f;
                float bx = ca * r0, bz = sa * r0;
                // 两段弯曲的叶片
                Vector3 b0 = V3(bx - sa * w, 0, bz + ca * w), b1 = V3(bx + sa * w, 0, bz - ca * w);
                Vector3 m = V3(bx + ca * lean * 0.35f, h * 0.55f, bz + sa * lean * 0.35f), m0 = V3(m.x - sa * w * 0.6f, m.y, m.z + ca * w * 0.6f), m1 = V3(m.x + sa * w * 0.6f, m.y, m.z - ca * w * 0.6f);
                var tip = V3(bx + ca * lean, h, bz + sa * lean);
                Vector3 nf = V3(ca * 0.3f, 1, sa * 0.3f).normalized, nb = V3(-ca * 0.3f, 1, -sa * 0.3f).normalized;
                foreach (var nn in new[] { nf, nb })
                {
                    mb.TriN(b0, b1, m1, nn, nn, nn, dark, dark, mid); mb.TriN(b0, m1, m0, nn, nn, nn, dark, mid, mid);
                    mb.TriN(m0, m1, tip, nn, nn, nn, mid, mid, lite);
                }
                if (reed && k % 3 == 0) mb.Sphere(tip, V3(0.018f, 0.07f, 0.018f), new Color(0.62f, 0.48f, 0.32f), 0, new SphereOpt { rough = 0 });
            }
            var mesh = Merge("DuelTuft_" + key, null, mb);
            geo[key] = mesh;
            return mesh;
        }
        public static Mesh Flower()
        {
            var hit = Cached("flower"); if (hit != null) return hit;
            var stem = new MeshBuilder(); var pet = new MeshBuilder(); var up = V3(0, 1, 0); var green = new Color(0.3f, 0.5f, 0.2f);
            foreach (var s in new[] { new[] { 0f, 0, 0.17f }, new[] { 0.07f, 0.04f, 0.13f }, new[] { -0.05f, 0.06f, 0.15f }, new[] { 0.02f, -0.06f, 0.12f } })
            {
                float x = s[0], z = s[1], h = s[2];
                stem.TriN(V3(x - 0.01f, 0, z), V3(x + 0.01f, 0, z), V3(x, h, z), V3(0, 0.3f, 1), V3(0, 0.3f, 1), up, green);
                stem.TriN(V3(x + 0.01f, 0, z), V3(x - 0.01f, 0, z), V3(x, h, z), V3(0, 0.3f, -1), V3(0, 0.3f, -1), up, green);
                float r = 0.035f;
                for (int k = 0; k < 5; k++)
                {
                    float a0 = k * Mathf.PI * 2 / 5, a1 = a0 + Mathf.PI * 2 / 5;
                    pet.TriN(V3(x, h, z), V3(x + Mathf.Cos(a0) * r, h + 0.006f, z + Mathf.Sin(a0) * r), V3(x + Mathf.Cos(a1) * r, h + 0.006f, z + Mathf.Sin(a1) * r), up, up, up, new Color(1, 1, 1));
                }
                pet.TriN(V3(x, h + 0.012f, z), V3(x + 0.012f, h + 0.01f, z), V3(x, h + 0.01f, z + 0.012f), up, up, up, new Color(1.0f, 0.9f, 0.4f));
            }
            var m = Merge("DuelFlower", stem, pet);
            geo["flower"] = m;
            return m;
        }
    }

    // 把一个汉字画进底图（RenderTexture）：临时正交相机 + TextMesh，渲染一次后销毁临时对象
    public static class DuelGlyph
    {
        public const int TempLayer = 31;
        public static Texture Render(Texture2D bg, string glyph, float cx, float cy, float px, Color col, List<Object> owned)
        {
            int W = bg.width, H = bg.height;
            var rt = new RenderTexture(W, H, 16, RenderTextureFormat.ARGB32) { name = "DuelGlyphRT", anisoLevel = 2 };
            rt.Create();
            owned.Add(rt);
            var host = new GameObject("DuelGlyphTmp");
            host.transform.position = new Vector3(0, -5000, 0);
            try
            {
                var camGo = new GameObject("Cam"); camGo.transform.SetParent(host.transform, false);
                camGo.transform.localPosition = new Vector3(W / 2f, H / 2f, -10);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false; cam.orthographic = true; cam.orthographicSize = H / 2f; cam.aspect = (float)W / H;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.cullingMask = 1 << TempLayer; cam.nearClipPlane = 0.1f; cam.farClipPlane = 50; cam.targetTexture = rt;
                // 底图面片（0..W × 0..H，左上为贴图原点）
                var qm = new Mesh();
                qm.vertices = new[] { new Vector3(0, 0, 0), new Vector3(W, 0, 0), new Vector3(W, H, 0), new Vector3(0, H, 0) };
                qm.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                qm.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                var qmat = Art.NewUnlit(Color.white, bg);
                var q = Art.MakeObject("Bg", qm, qmat, host.transform, false); q.layer = TempLayer;
                // 字
                var tg = new GameObject("Glyph"); tg.layer = TempLayer; tg.transform.SetParent(host.transform, false);
                tg.transform.localPosition = new Vector3(cx, H - cy, -1);
                var tm = tg.AddComponent<TextMesh>();
                tm.font = UIKit.Title; tm.text = glyph; tm.fontSize = 128; tm.characterSize = px / 128f * 10f; tm.fontStyle = FontStyle.Bold;
                tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = col;
                tg.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Title.material;
                cam.Render();
                Object.Destroy(qm); Object.Destroy(qmat);
            }
            catch (Exception e) { Debug.LogWarning("DuelGlyph: " + e.Message); }
            Object.Destroy(host);
            return rt;
        }
    }

    // 面向镜头的粒子面片（= 网页版 Particles：位置 / 速度 / 重力 / 阻尼 / 触地反弹 / 淡入淡出 / 增长）
    public sealed class DuelParticles
    {
        public sealed class Part { public float x, y, z, vx, vy, vz, r, g, b, a, size, life, age, grav, drag, ac = 1, fadeIn, grow; public bool floor; }
        readonly int max;
        public readonly List<Part> parts = new List<Part>();
        readonly Mesh mesh; readonly Material mat; readonly Texture2D tex;
        readonly GameObject go;
        readonly Vector3[] v; readonly Color[] c; readonly Vector2[] uv;

        public DuelParticles(int max, bool additive, float soft, int queue, Transform parent, int layer)
        {
            this.max = max;
            // 软圆点（alpha = ((1 − d)²)^soft，同 SG.Gfx.softDotTexture 的 pow(a, uSoft)）
            int n = 64;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DuelDot" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1 - d); a = Mathf.Pow(a * a, soft);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px); tex.Apply();
            mat = additive ? new Material(Art.LoadShader("Additive")) : Art.NewUnlit(Color.white);
            mat.mainTexture = tex; mat.renderQueue = 3000 + queue;
            v = new Vector3[max * 4]; c = new Color[max * 4]; uv = new Vector2[max * 4];
            var tri = new int[max * 6];
            for (int i = 0; i < max; i++)
            {
                uv[i * 4] = new Vector2(0, 0); uv[i * 4 + 1] = new Vector2(1, 0); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(0, 1);
                tri[i * 6] = i * 4; tri[i * 6 + 1] = i * 4 + 2; tri[i * 6 + 2] = i * 4 + 1; tri[i * 6 + 3] = i * 4; tri[i * 6 + 4] = i * 4 + 3; tri[i * 6 + 5] = i * 4 + 2;
            }
            mesh = new Mesh { name = "DuelParticles" };
            mesh.MarkDynamic();
            mesh.vertices = v; mesh.colors = c; mesh.uv = uv; mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
            go = Art.MakeObject("Particles", mesh, mat, parent, false);
            go.layer = layer;
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }
        public void Add(Part p)
        {
            if (parts.Count >= max) parts.RemoveAt(0);
            p.age = 0;
            parts.Add(p);
        }
        // 位置为 three 坐标（与网页版相同的数值）；right / up 为镜头的 Unity 方向
        public void Update(float dt, Vector3 right, Vector3 up)
        {
            int n = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.age += dt;
                if (p.age >= p.life) continue;
                p.vy -= p.grav * dt;
                if (p.drag != 0) { float k = Mathf.Max(0, 1 - p.drag * dt); p.vx *= k; p.vy *= k; p.vz *= k; }
                p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
                if (p.floor && p.y < 0.02f) { p.y = 0.02f; p.vy = -p.vy * 0.3f; p.vx *= 0.6f; }
                parts[n++] = p;
            }
            parts.RemoveRange(n, parts.Count - n);
            for (int i = 0; i < max; i++)
            {
                int j = i * 4;
                if (i >= n) { v[j] = v[j + 1] = v[j + 2] = v[j + 3] = Vector3.zero; c[j] = c[j + 1] = c[j + 2] = c[j + 3] = Color.clear; continue; }
                var p = parts[i]; float t = p.age / p.life;
                float fin = p.fadeIn > 0 ? Mathf.Min(1, t / p.fadeIn) : 1;
                float a = p.a * (1 - Mathf.Pow(t, p.ac)) * fin;
                float s = p.size * (1 + p.grow * t) * 0.5f;
                var ctr = new Vector3(p.x, p.y, -p.z);
                Vector3 rr = right * s, uu = up * s;
                v[j] = ctr - rr - uu; v[j + 1] = ctr + rr - uu; v[j + 2] = ctr + rr + uu; v[j + 3] = ctr - rr + uu;
                var col = new Color(p.r, p.g, p.b, a);
                c[j] = c[j + 1] = c[j + 2] = c[j + 3] = col;
            }
            mesh.vertices = v; mesh.colors = c;
        }
        public int Count { get { return parts.Count; } }
        public void Dispose() { Object.Destroy(go); Object.Destroy(mesh); Object.Destroy(mat); Object.Destroy(tex); }
    }

    // 刀光拖尾（第三版修订）：每次推入新样本时，在上一样本与新样本之间按「绕刃根旋转」插入细分样本（刃尖走弧线而不是弦），
    // 不再是几段长直线拼成的白色纸扇；横向四列顶点：刃根一侧透明 → 中段淡 → 刃口亮 → 最外一道近白的亮芯；按样本年龄淡出。
    // 坐标为 Unity 世界坐标（几何运算在镜像下不变）
    public sealed class DuelTrail
    {
        readonly int n;
        readonly Vector3[] bs, tip; readonly float[] life;
        public int count;
        readonly Vector3[] pos; readonly Color[] col;
        readonly Mesh mesh; readonly Material mat; readonly GameObject go;
        public Color color = Color.white;
        public bool on;
        public bool Drawn { get; private set; }
        static readonly float[] W = { 0.12f, 0.55f, 0.9f, 1.0f }, K = { 0, 0.18f, 0.8f, 1.0f };
        public DuelTrail(int n, Transform parent, int layer)
        {
            this.n = n;
            bs = new Vector3[n]; tip = new Vector3[n]; life = new float[n];
            pos = new Vector3[n * 4]; col = new Color[n * 4];
            var idx = new List<int>();
            for (int i = 0; i < n - 1; i++) for (int c = 0; c < 3; c++) { int a = i * 4 + c, b = a + 1, d = a + 5, e = a + 4; idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(a); idx.Add(d); idx.Add(e); }
            mesh = new Mesh { name = "DuelTrail" };
            mesh.MarkDynamic();
            mesh.vertices = pos; mesh.colors = col; mesh.SetTriangles(idx, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
            mat = new Material(Art.LoadShader("Additive")); mat.renderQueue = 3020;   // 默认白色贴图：只用顶点色；双面
            go = Art.MakeObject("Trail", mesh, mat, parent, false);
            go.layer = layer;
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }
        void Shift(Vector3 b, Vector3 t, float lf)
        {
            for (int i = n - 1; i > 0; i--) { bs[i] = bs[i - 1]; tip[i] = tip[i - 1]; life[i] = life[i - 1]; }
            bs[0] = b; tip[0] = t; life[0] = lf;
            count = Mathf.Min(n, count + 1);
        }
        public void Push(Vector3 b, Vector3 t)
        {
            // 定格（顿帧、暂停）时刃口不动：不推入重复样本（否则拖尾会塌成一条线）
            if (count > 0 && (bs[0] - b).sqrMagnitude < 1e-8f && (tip[0] - t).sqrMagnitude < 1e-8f) return;
            if (on && count > 0 && life[0] > 0)
            {
                Vector3 b0 = bs[0], t0 = tip[0];
                Vector3 d0 = t0 - b0, d1 = t - b;
                float l0 = d0.magnitude, l1 = d1.magnitude;
                if (l0 > 1e-4f && l1 > 1e-4f)
                {
                    d0 /= l0; d1 /= l1;
                    float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d0, d1), -1, 1));
                    int ns = Mathf.Min(7, Mathf.Max(1, Mathf.CeilToInt(ang / 0.09f)));
                    if (ns > 1)
                    {
                        var q = Quaternion.FromToRotation(d0, d1);
                        for (int s = 1; s < ns; s++)
                        {
                            float f = (float)s / ns;
                            var qi = Quaternion.Slerp(Quaternion.identity, q, f);
                            var pb = Vector3.LerpUnclamped(b0, b, f);
                            var pt = qi * d0 * (l0 + (l1 - l0) * f) + pb;
                            Shift(pb, pt, 1);
                        }
                    }
                }
            }
            Shift(b, t, on ? 1 : 0);
        }
        public void Update(float dt)
        {
            bool any = false;
            for (int i = 0; i < n; i++) { life[i] = Mathf.Max(0, life[i] - dt * 3.2f); if (life[i] > 0) any = true; }
            bool draw = any && count > 1;
            int used = Mathf.Min(count, n);
            for (int i = 0; i < n; i++)
            {
                float age = (float)i / (n - 1), k = draw && i < used ? life[i] * Mathf.Pow(1 - age, 0.8f) : 0;
                Vector3 B = bs[i], T = tip[i];
                for (int q = 0; q < 4; q++)
                {
                    int o = i * 4 + q;
                    pos[o] = B + (T - B) * W[q];
                    float kk = k * K[q];
                    if (q == 3) { float wh = 0.55f * (1 - age); col[o] = new Color((color.r * (1 - wh) + wh) * kk, (color.g * (1 - wh) + wh) * kk, (color.b * (1 - wh) + wh) * kk, 1); }
                    else col[o] = new Color(color.r * kk, color.g * kk, color.b * kk, 1);
                }
            }
            // 只画已有的样本（= setDrawRange）：多余的样本退化到最后一个已用样本上
            if (draw) for (int i = used; i < n; i++) for (int q = 0; q < 4; q++) { pos[i * 4 + q] = pos[(used - 1) * 4 + q]; col[i * 4 + q] = Color.clear; }
            mesh.vertices = pos; mesh.colors = col;
            go.SetActive(draw);
            Drawn = draw;
        }
        public void Dispose() { Object.Destroy(go); Object.Destroy(mesh); Object.Destroy(mat); }
    }

    public static class DuelTextures
    {
        // 命中闪光的四芒星
        public static Texture2D Star()
        {
            int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "DuelStar" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ray = Mathf.Max(Mathf.Exp(-Mathf.Abs(u) * 18) * (1 - Mathf.Abs(v)), Mathf.Exp(-Mathf.Abs(v) * 18) * (1 - Mathf.Abs(u)));
                    float k = Mathf.Max(0, 1 - r * 2.2f);
                    float a = Mathf.Clamp01(ray * 1.1f + k * k);
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Round(a * 255) / 255f);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }
        // 正方形面片（中心在原点，xy 平面，边长 1）
        public static Mesh Quad(string name)
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }
        // 平滑的箭（杆 + 箭镞 + 尾羽），+X 为箭头方向，原点在箭尾（= 网页版 arrowGeometry；MeshBuilder 的 Unity 坐标与网页版同值）
        public static Mesh Arrow(bool big)
        {
            var mb = new MeshBuilder(); float L = big ? 1.0f : 0.82f, k = big ? 1.25f : 1;
            Color shaft = DuelXf.C("#8a6a40"), head = DuelXf.C("#c8ced8"), fl = DuelXf.C(big ? "#d84030" : "#ece6d6");
            mb.Tube(new[] { new Vector3(0, 0, 0), new Vector3(L * 0.5f, 0, 0), new Vector3(L, 0, 0) }, new[] { 0.007f * k, 0.007f * k, 0.007f * k }, 6, shaft);
            mb.Tube(new[] { new Vector3(L, 0, 0), new Vector3(L + 0.035f * k, 0, 0), new Vector3(L + 0.09f * k, 0, 0) }, new[] { 0.012f * k, 0.016f * k, 0.001f }, 6, head);
            foreach (var a in new[] { 0f, 2.09f, 4.19f })
            {
                float y = Mathf.Cos(a) * 0.028f * k, z = Mathf.Sin(a) * 0.028f * k;
                var nn = new Vector3(0, -Mathf.Sin(a), Mathf.Cos(a));
                mb.TriN(new Vector3(0.02f, 0, 0), new Vector3(0.16f * k, 0, 0), new Vector3(0.04f, y, z), nn, nn, nn, fl);
                mb.TriN(new Vector3(0.16f * k, 0, 0), new Vector3(0.02f, 0, 0), new Vector3(0.04f, y, z), -nn, -nn, -nn, fl);
            }
            return mb.ToMesh(big ? "DuelArrowFin" : "DuelArrow");
        }
    }
}
