// ==========================================================================
// 三国志II 霸王的大陆 · 单挑格斗 · 场地与特效（网页版 js/duel-game.js「视图：场地 / 特效」的移植）
//   DuelArena     背景随地形：平原 / 森林 / 丘陵（山地）/ 河川 / 城墙（城门、本城）；两军阵列、帅旗（势力色 + 姓氏）
//   DuelParticles 面向镜头的粒子面片（火花 / 尘土 / 光点）
//   DuelTrail     刀光拖尾
// 场地几何同网页版用 three 坐标书写（DuelPB），士兵与城楼屋顶用 Unity 坐标的 MeshBuilder。
// ==========================================================================
using System.Collections.Generic;
using UnityEngine;

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
        static Color Sh(Color c, float k) { return DuelXf.Sh(c, k); }
        static Color Sh(string c, float k) { return DuelXf.Sh(c, k); }
        static Color Mix(Color a, Color b, float t) { return DuelXf.Mix(a, b, t); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Vector2 P2(float x, float y) { return new Vector2(x, y); }

        public readonly string kind;
        public readonly DuelPalette pal;
        public readonly GameObject group;
        readonly int layer;
        readonly List<Object> owned = new List<Object>();
        readonly SeededRandom rnd;
        readonly Material env;
        public readonly List<Vector3> torches = new List<Vector3>();   // three 坐标（同粒子）
        sealed class Banner { public Mesh mesh; public Vector3[] bs, cur; public float w, phase; }
        readonly List<Banner> banners = new List<Banner>();

        public DuelArena(Transform parent, int layer, string kind, Color colA, Color colB, string glyphA, string glyphB, uint seed, Material env)
        {
            this.kind = kind; this.layer = layer; this.env = env;
            pal = Palettes[kind];
            group = new GameObject("DuelArena"); group.layer = layer; group.transform.SetParent(parent, false);
            rnd = new SeededRandom(seed != 0 ? seed : 7u);
            // 地面 + 远山 + 景物
            var pb = new DuelPB();
            BuildGround(pb);
            BuildRidges(pb);
            var extra = new MeshBuilder();   // Unity 坐标（士兵、城楼屋顶）
            BuildProps(pb, extra, colA, colB);
            AddMesh(pb.ToMesh("DuelArena"), env, true, true);
            if (extra.Count > 0) AddMesh(extra.ToMesh("DuelArenaExtra"), env, true, true);
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

        // 地面：中间平坦的对决场地，两侧与后方起伏
        void BuildGround(DuelPB pb)
        {
            float X0 = -44, X1 = 44, Z0 = -44, Z1 = 10, S = 2;
            float hill = kind == "hill" ? 2.2f : kind == "forest" ? 0.8f : kind == "castle" ? 0.2f : 1;
            System.Func<float, float, float> h = (x, z) =>
            {
                if (kind == "river")
                {
                    if (z < -3.4f && z > -26) return -0.8f;
                    if (z <= -26) return (-26 - z) * 0.15f + Mathf.PerlinNoise(x * 0.08f + 3, z * 0.08f) * 2;
                }
                if (Mathf.Abs(z) < 2.6f && Mathf.Abs(x) < 13) return 0;
                float back = z < 0 ? Mathf.Max(0, -z - 3) : Mathf.Max(0, z - 3) * 0.4f;
                float n = Mathf.PerlinNoise(x * 0.07f + 11, z * 0.07f + 5) - 0.45f;
                return back * 0.09f * hill + n * 1.6f * Mathf.Min(1, back / 4) * hill + Mathf.Max(0, Mathf.Abs(x) - 13) * 0.05f * hill;
            };
            Color gc = C(pal.ground), gc2 = C(pal.ground2), lane = C(pal.lane);
            System.Func<float, float, Color> colAt = (cx, cz) =>
            {
                var col = Mix(gc, gc2, Mathf.Clamp01(Mathf.PerlinNoise(cx * 0.15f, cz * 0.15f) * 1.2f - 0.1f));
                float laneK = 1 - Mathf.SmoothStep(0, 1, (Mathf.Abs(cz) - 1.2f) / 2.2f);
                if (Mathf.Abs(cx) < 14) col = Mix(col, lane, laneK);
                return col;
            };
            for (float x = X0; x < X1; x += S)
                for (float z = Z0; z < Z1; z += S)
                {
                    Vector3 a = V(x, h(x, z), z), b = V(x, h(x, z + S), z + S), c = V(x + S, h(x + S, z + S), z + S), d = V(x + S, h(x + S, z), z);
                    float cz = z + S / 2, cx = x + S / 2;
                    var col = Mix(gc, gc2, Mathf.Clamp01(Mathf.PerlinNoise(cx * 0.15f, cz * 0.15f) * 1.2f - 0.1f));
                    float laneK = 1 - Mathf.SmoothStep(0, 1, (Mathf.Abs(cz) - 1.2f) / 2.2f);
                    if (Mathf.Abs(cx) < 14) col = Mix(col, lane, laneK);
                    bool tiles = kind == "castle" && Mathf.Abs(cz) < 3.6f && Mathf.Abs(cx) < 16;
                    if (tiles) col = ((Mathf.FloorToInt(cx / 2) + Mathf.FloorToInt(cz / 2)) & 1) != 0 ? Sh(lane, 0.05f) : Sh(lane, -0.06f);
                    if (kind == "river" && cz < -3.4f && cz > -26) col = C("#5a6a5a");
                    float j = (Rnd() - 0.5f) * 0.08f;
                    // 比武道与前景在画面上很大：逐顶点平滑着色，免得满是逐块接缝与色带
                    if (cz > -3 && !tiles)
                    {
                        Color ca = colAt(a.x, a.z), cb = colAt(b.x, b.z), cc = colAt(c.x, c.z), cd = colAt(d.x, d.z);
                        pb.TriV(a, b, c, ca, cb, cc); pb.TriV(a, c, d, ca, cc, cd);
                        continue;
                    }
                    pb.Tri(a, b, c, Sh(col, j)); pb.Tri(a, c, d, Sh(col, j - 0.03f));
                }
        }
        // 远山（两层剪影，雾中淡去）
        void BuildRidges(DuelPB pb)
        {
            var layers = kind == "castle" ? new[] { new object[] { -60f, 7f, pal.mid }, new object[] { -110f, 16f, pal.far } }
                : kind == "hill" ? new[] { new object[] { -46f, 12f, pal.mid }, new object[] { -95f, 30f, pal.far } }
                : new[] { new object[] { -55f, 8f, pal.mid }, new object[] { -105f, 22f, pal.far } };
            for (int li = 0; li < layers.Length; li++)
            {
                float z = (float)layers[li][0], hgt = (float)layers[li][1]; Color c = C((string)layers[li][2]);
                bool has = false; Vector3 prev = Vector3.zero;
                for (float x = -200; x <= 200; x += 12)
                {
                    float n = Mathf.PerlinNoise(x * 0.012f + li * 7, li * 3.1f);
                    float y = hgt * (0.35f + n * 0.9f) + (li == 1 ? Mathf.Max(0, 1 - Mathf.Abs(x + 30) / 60) * hgt * 0.5f : 0);
                    var p = V(x, y, z + Mathf.Sin(x * 0.05f) * 6);
                    if (has)
                    {
                        pb.Quad(V(prev.x, -4, prev.z), V(p.x, -4, p.z), p, prev, Sh(c, (n - 0.5f) * 0.15f));
                        pb.Tri(prev, V((prev.x + p.x) / 2, (prev.y + p.y) / 2 - hgt * 0.15f, (prev.z + p.z) / 2 + 3), p, Sh(c, -0.08f));
                    }
                    prev = p; has = true;
                }
            }
        }
        void Tree(DuelPB pb, float x, float z, float s, bool pine)
        {
            float y = 0;
            Color leaf = C(pal.leaf);
            float j = (Rnd() - 0.5f) * 0.15f;
            pb.Prism(x, z, y, y + 1.2f * s, 0.14f * s, 0.1f * s, 5, "#4a3424");
            if (pine)
            {
                pb.Prism(x, z, y + 0.8f * s, y + 2.6f * s, 1.0f * s, 0, 7, Sh(leaf, j - 0.05f));
                pb.Prism(x, z, y + 1.7f * s, y + 3.4f * s, 0.75f * s, 0, 7, Sh(leaf, j + 0.04f));
                pb.Prism(x, z, y + 2.6f * s, y + 4.1f * s, 0.45f * s, 0, 7, Sh(leaf, j + 0.1f));
            }
            else
            {
                pb.Blob(x, y + 2.0f * s, z, 1.2f * s, 1.0f * s, 1.2f * s, Sh(leaf, j), (int)(x * 31 + z * 7));
                pb.Blob(x + 0.5f * s, y + 2.6f * s, z + 0.2f * s, 0.8f * s, 0.7f * s, 0.8f * s, Sh(leaf, j + 0.08f), (int)(x * 13 + z));
            }
        }
        void Rock(DuelPB pb, float x, float z, float s, Color? col = null)
        {
            pb.Blob(x, s * 0.35f, z, s, s * 0.7f, s * 0.9f, col ?? C(pal.rock), (int)(x * 17 + z * 5));
        }
        // 两军阵列（Unity 坐标的 MeshBuilder：Unity z = −three z）
        void Army(MeshBuilder mb, float x0, float x1, float z0, float z1, Color team, float y, int rows, int cols)
        {
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    float x = x0 + (x1 - x0) * (cols > 1 ? (float)j / (cols - 1) : 0.5f) + (Rnd() - 0.5f) * 0.4f;
                    float z = z0 + (z1 - z0) * (rows > 1 ? (float)i / (rows - 1) : 0.5f) + (Rnd() - 0.5f) * 0.3f;
                    Models.Soldier(mb, new Vector3(x, y, -z), team, true, 3.3f);
                }
        }
        void BuildProps(DuelPB pb, MeshBuilder mb, Color colA, Color colB)
        {
            System.Action<int, System.Action<float, float, float>, float, float, float> scatter = (n, fn, zMin, zMax, xr) =>
            {
                for (int i = 0; i < n; i++)
                {
                    float x = (Rnd() * 2 - 1) * xr, z = zMin + Rnd() * (zMax - zMin);
                    if (Mathf.Abs(z) < 2.2f && Mathf.Abs(x) < 12) continue;
                    if (z > 2.2f && Mathf.Abs(x) < 5) continue;          // 镜头前方中央留空
                    fn(x, z, Rnd());
                }
            };
            System.Action<float, float, float> tuft = (x, z, k) =>
            {
                var g = Mix(C(pal.leaf), C(pal.ground2), k * 0.6f);
                for (int i = 0; i < 3; i++) pb.Rod(V(x + (i - 1) * 0.06f, 0, z), V(x + (i - 1) * 0.16f, 0.32f + k * 0.25f, z + (k - 0.5f) * 0.1f), 0.04f, 0, 3, Sh(g, i * 0.05f));
            };
            if (kind != "castle")
            {
                Army(mb, -16, -8.5f, -11, -14, colA, 0, 3, 7);
                Army(mb, 8.5f, 16, -11, -14, colB, 0, 3, 7);
            }
            switch (kind)
            {
                case "plain":
                    scatter(130, tuft, -16, 6, 30);
                    scatter(18, (x, z, k) => Rock(pb, x, z, 0.2f + k * 0.35f), -14, 6, 30);
                    for (int i = 0; i < 9; i++)
                    {           // 战场上插着的断枪残旗
                        float x = (Rnd() * 2 - 1) * 11, z = -2.8f - Rnd() * 3.5f;
                        float a = (Rnd() - 0.5f) * 0.6f;
                        pb.Rod(V(x, 0, z), V(x + Mathf.Sin(a) * 1.5f, Mathf.Cos(a) * 1.5f, z), 0.025f, 0.02f, 4, "#5a3a22");
                        if (i % 3 == 0) pb.Slab(new[] { P2(x + Mathf.Sin(a) * 1.4f, Mathf.Cos(a) * 1.4f), P2(x + Mathf.Sin(a) * 1.4f + 0.45f, Mathf.Cos(a) * 1.4f - 0.1f), P2(x + Mathf.Sin(a) * 1.4f + 0.4f, Mathf.Cos(a) * 1.4f - 0.4f), P2(x + Mathf.Sin(a) * 1.2f, Mathf.Cos(a) * 1.2f - 0.3f) }, z - 0.01f, z + 0.01f, i % 2 != 0 ? colA : colB);
                    }
                    for (int i = 0; i < 14; i++) { float x = (Rnd() * 2 - 1) * 40, z = -16 - Rnd() * 18, s = 0.9f + Rnd() * 0.6f; Tree(pb, x, z, s, Rnd() < 0.4f); }
                    break;
                case "forest":
                    for (int i = 0; i < 70; i++)
                    {
                        float x = (Rnd() * 2 - 1) * 42, z = -5.5f - Rnd() * 32;
                        if (z > -11 && Mathf.Abs(x) < 5.5f && Rnd() < 0.6f) continue;
                        float s = 0.9f + Rnd() * 0.8f; Tree(pb, x, z, s, Rnd() < 0.55f);
                    }
                    foreach (var x in new[] { -11.5f, -9.2f, 9.6f, 12.2f }) { float z = -3.6f - Rnd(), s = 1.2f + Rnd() * 0.3f; Tree(pb, x, z, s, x > 0); }
                    scatter(90, tuft, -12, 6, 30);
                    scatter(16, (x, z, k) => pb.Blob(x, 0.15f, z, 0.4f + k * 0.4f, 0.25f, 0.4f, Sh(pal.leaf, 0.1f), (int)(x * 3)), -10, 5, 30);
                    pb.Rod(V(-4.5f, 0.2f, -4.4f), V(-1.2f, 0.25f, -4.9f), 0.22f, 0.18f, 6, "#5a4030");   // 倒木
                    break;
                case "hill":
                    for (int i = 0; i < 12; i++) { float x = (Rnd() * 2 - 1) * 30, z = -6 - Rnd() * 12, s = 0.6f + Rnd() * 1.4f; Rock(pb, x, z, s, Sh(pal.rock, (Rnd() - 0.5f) * 0.2f)); }
                    foreach (var r3 in new[] { new[] { -19f, -17, 5 }, new[] { -10f, -22, 7 }, new[] { 15f, -18, 6 }, new[] { 24f, -24, 8 }, new[] { 3f, -28, 6 } }) Rock(pb, r3[0], r3[1], r3[2], Sh(pal.rock, -0.1f));
                    scatter(26, (x, z, k) => Rock(pb, x, z, 0.15f + k * 0.25f), -10, 6, 30);
                    for (int i = 0; i < 16; i++) { float x = (Rnd() * 2 - 1) * 40, z = -12 - Rnd() * 20, s = 0.8f + Rnd() * 0.5f; Tree(pb, x, z, s, true); }
                    scatter(60, tuft, -12, 6, 30);
                    break;
                case "river":
                    {
                        for (int i = 0; i < 70; i++)
                        {     // 芦苇
                            float x = (Rnd() * 2 - 1) * 30, z = -3.0f - Rnd() * 0.9f;
                            float dx = (Rnd() - 0.5f) * 0.3f, hh = 0.9f + Rnd() * 0.7f;
                            pb.Rod(V(x, -0.3f, z), V(x + dx, hh, z), 0.025f, 0.006f, 3, Rnd() < 0.5f ? "#9a9a5a" : "#7a8a4a");
                        }
                        scatter(40, (x, z, k) => Rock(pb, x, z, 0.1f + k * 0.18f), -3, 6, 30);
                        for (int i = 0; i < 26; i++) { float x = (Rnd() * 2 - 1) * 50, z = -28 - Rnd() * 10, s = 1 + Rnd() * 0.6f; Tree(pb, x, z, s, Rnd() < 0.4f); }
                        scatter(50, tuft, 1.5f, 6, 30);
                        // 水面（共享的水面材质，不释放）：120 × 23，24 × 6 段，y = −0.12，three z 中心 −14.8
                        var wm = new Mesh { name = "DuelWater" };
                        int nx = 24, nz = 6; var vs = new List<Vector3>(); var cs = new List<Color>(); var ts = new List<int>();
                        for (int iz = 0; iz <= nz; iz++)
                            for (int ix = 0; ix <= nx; ix++)
                            {
                                float x = -60 + 120f * ix / nx, z3 = -14.8f - 11.5f + 23f * iz / nz;
                                vs.Add(new Vector3(x, -0.12f, -z3));
                                cs.Add(new Color(Mathf.Clamp01(1 - (-3.4f - z3) / 6), 0, 0, 1));
                            }
                        for (int iz = 0; iz < nz; iz++)
                            for (int ix = 0; ix < nx; ix++)
                            {
                                int a0 = iz * (nx + 1) + ix, a1 = a0 + 1, b0 = a0 + nx + 1, b1 = b0 + 1;
                                // Unity 坐标：z3 增大 = Unity z 减小；顺时针（从上方看）为正面
                                ts.Add(a0); ts.Add(a1); ts.Add(b0); ts.Add(a1); ts.Add(b1); ts.Add(b0);
                            }
                        wm.SetVertices(vs); wm.SetColors(cs); wm.SetTriangles(ts, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
                        var wgo = AddMesh(wm, Art.Water, false, false);
                        wgo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        break;
                    }
                case "castle":
                    {
                        // 城墙、城门楼、火把
                        string stone = "#6e665c"; float z = -12.5f, H = 6.6f;
                        pb.Box(0, H / 2, z, 100, H, 2.4f, stone);
                        for (float x = -50; x <= 50; x += 1.4f) pb.Box(x, H + 0.35f, z + 1.0f, 0.8f, 0.7f, 0.4f, Sh(stone, 0.06f));
                        for (float x = -50; x <= 50; x += 6) pb.Box(x, H / 2, z + 1.25f, 0.12f, H, 0.05f, Sh(stone, -0.12f));
                        for (float y = 1; y < H; y += 1.1f) pb.Box(0, y, z + 1.22f, 100, 0.04f, 0.05f, Sh(stone, -0.08f));
                        pb.Box(0, 2.0f, z + 1.26f, 3.4f, 4.0f, 0.1f, "#1e140e");                 // 城门
                        foreach (var yy in new[] { 0.8f, 1.6f, 2.4f, 3.2f }) foreach (var xx in new[] { -1.2f, -0.4f, 0.4f, 1.2f }) pb.Box(xx, yy, z + 1.33f, 0.12f, 0.12f, 0.06f, "#8a7040");
                        pb.Box(0, 4.15f, z + 1.27f, 3.8f, 0.3f, 0.12f, Sh(stone, -0.2f));
                        foreach (var x in new[] { -14f, 14f }) pb.Box(x, H / 2 + 0.7f, z + 0.2f, 4.4f, H + 1.4f, 3.2f, Sh(stone, -0.04f));
                        // 城门楼（Unity 坐标：z 取反）
                        mb.Box(new Vector3(0, H + 1.2f, -z), new Vector3(8.5f, 2.4f, 3.4f), C("#7a2418"));
                        foreach (var x in new[] { -3.7f, -1.25f, 1.25f, 3.7f }) mb.Box(new Vector3(x, H + 1.2f, -z - 1.72f), new Vector3(0.28f, 2.4f, 0.1f), C("#4a140e"));
                        mb.ChineseRoof(new Vector3(0, H + 2.4f, -z), 10.8f, 5.0f, 2.8f, C("#262a36"));
                        mb.Box(new Vector3(0, H + 3.5f, -z), new Vector3(5.6f, 1.2f, 2.4f), C("#7a2418"));
                        mb.ChineseRoof(new Vector3(0, H + 4.1f, -z), 7.4f, 3.6f, 2.4f, C("#262a36"));
                        foreach (var x in new[] { -14f, 14f }) mb.ChineseRoof(new Vector3(x, H + 1.4f, -z - 0.2f), 5.4f, 4.0f, 1.8f, C("#262a36"));
                        // 城头守军（b 方）与城下攻军（a 方）
                        Army(mb, -10, -4.5f, -12.2f, -12.8f, colB, H, 2, 5);
                        Army(mb, 4.5f, 10, -12.2f, -12.8f, colB, H, 2, 5);
                        Army(mb, -17, -9, -8.2f, -10.4f, colA, 0, 3, 6);
                        foreach (var x in new[] { -3.6f, 3.6f })
                        {
                            pb.Prism(x, -9.6f, 0, 2.4f, 0.07f, 0.06f, 5, "#2a1e14");
                            pb.Prism(x, -9.6f, 2.4f, 2.65f, 0.14f, 0.24f, 6, "#2a2424");
                            torches.Add(new Vector3(x, 2.85f, -9.6f));
                        }
                        scatter(20, (x, zz, k) => Rock(pb, x, zz, 0.12f + k * 0.2f), -8, 6, 30);
                        break;
                    }
            }
        }

        // 旗面贴图：势力色底 + 白圆中写姓氏（底图用 Raster 绘制，字用 TextMesh 渲染进 RenderTexture）
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

        void AddBanner(float x, float z, float poleH, float w, float h, Color color, string glyph, bool pennant, float phase)
        {
            var tex = BannerTexture(color, glyph, pennant);
            var mat = Art.NewUnlit(new Color(0.92f, 0.92f, 0.92f, 1), tex); owned.Add(mat);
            mat.renderQueue = 2450;   // 不透明旗面：先于透明物体绘制
            // 6 × 6 段的旗面（three 局部坐标：x 0..w，y 0..−h），每帧按风吹形变
            int n = 6; var bs = new Vector3[(n + 1) * (n + 1)]; var uv = new Vector2[bs.Length]; var tris = new List<int>();
            for (int iy = 0; iy <= n; iy++)
                for (int ix = 0; ix <= n; ix++)
                {
                    int i = iy * (n + 1) + ix;
                    bs[i] = new Vector3(w * ix / n, -h * iy / n, 0);
                    uv[i] = new Vector2((float)ix / n, 1 - (float)iy / n);
                }
            for (int iy = 0; iy < n; iy++)
                for (int ix = 0; ix < n; ix++)
                {
                    int a = iy * (n + 1) + ix, b = a + 1, c = a + n + 1, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var mesh = new Mesh { name = "DuelBanner" };
            mesh.MarkDynamic();
            var cur = new Vector3[bs.Length];
            for (int i = 0; i < bs.Length; i++) cur[i] = DuelXf.P(bs[i]);
            mesh.vertices = cur; mesh.uv = uv; mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            mesh.bounds = new Bounds(new Vector3(w / 2, -h / 2, 0), new Vector3(w + 1, h + 1, 2));
            var cloth = AddMesh(mesh, mat, false, false);
            cloth.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            DuelXf.Pos(cloth.transform, x + 0.05f, poleH - 0.1f, z);
            var pb = new DuelPB();
            pb.Prism(x, z, 0, poleH + 0.1f, 0.05f, 0.04f, 6, "#4a2c1a");
            pb.Rod(V(x, poleH - 0.05f, z), V(x + w + 0.05f, poleH - 0.05f, z), 0.025f, 0.025f, 4, "#4a2c1a");
            pb.Prism(x, z, poleH + 0.1f, poleH + 0.42f, 0.06f, 0, 5, DuelModels.GOLD);
            pb.Blob(x, poleH + 0.02f, z, 0.08f, 0.1f, 0.08f, DuelModels.RED, 41);
            AddMesh(pb.ToMesh("DuelBannerPole"), env, true, false);
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
            }
        }

        public void Dispose()
        {
            if (group != null) Object.Destroy(group);
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
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
            catch (System.Exception e) { Debug.LogWarning("DuelGlyph: " + e.Message); }
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

    // 刀光拖尾：记录兵器刃根与刃尖的轨迹（Unity 世界坐标），生成渐隐的叠加色带
    public sealed class DuelTrail
    {
        readonly int n;
        readonly Vector3[] bs, tip; readonly float[] life;
        int count;
        readonly Vector3[] pos; readonly Color[] col;
        readonly Mesh mesh; readonly Material mat; readonly GameObject go;
        public Color color = Color.white;
        public bool on;
        public DuelTrail(int n, Transform parent, int layer)
        {
            this.n = n;
            bs = new Vector3[n]; tip = new Vector3[n]; life = new float[n];
            pos = new Vector3[n * 2]; col = new Color[n * 2];
            var idx = new List<int>();
            for (int i = 0; i < n - 1; i++) { int a = i * 2, b = a + 1, c = a + 2, d = a + 3; idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(a); idx.Add(d); idx.Add(c); }
            mesh = new Mesh { name = "DuelTrail" };
            mesh.MarkDynamic();
            mesh.vertices = pos; mesh.colors = col; mesh.SetTriangles(idx, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
            mat = new Material(Art.LoadShader("Additive")); mat.renderQueue = 3020;   // 默认白色贴图：只用顶点色
            go = Art.MakeObject("Trail", mesh, mat, parent, false);
            go.layer = layer;
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }
        public void Push(Vector3 b, Vector3 t)
        {
            for (int i = n - 1; i > 0; i--) { bs[i] = bs[i - 1]; tip[i] = tip[i - 1]; life[i] = life[i - 1]; }
            bs[0] = b; tip[0] = t; life[0] = on ? 1 : 0;
            count = Mathf.Min(n, count + 1);
        }
        public void Update(float dt)
        {
            bool any = false;
            for (int i = 0; i < n; i++) { life[i] = Mathf.Max(0, life[i] - dt * 2.5f); if (life[i] > 0) any = true; }
            bool draw = any && count > 1;
            for (int i = 0; i < n; i++)
            {
                float k = draw && i < count ? life[i] * (1 - (float)i / n) : 0;
                Vector3 B = bs[i], T = tip[i];
                // 刃根一侧较暗，刃尖最亮
                pos[i * 2] = B + (T - B) * 0.15f; pos[i * 2 + 1] = T;
                col[i * 2] = new Color(color.r * k * 0.25f, color.g * k * 0.25f, color.b * k * 0.25f, 1);
                col[i * 2 + 1] = new Color(color.r * k, color.g * k, color.b * k, 1);
            }
            mesh.vertices = pos; mesh.colors = col;
            go.SetActive(draw);
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
    }
}
