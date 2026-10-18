// ==========================================================================
// 三国志-霸王的世界 · 战场地形取自城池周边（网页版 js/battle-terrain.js 的逐行移植，DESIGN-V3 §2）
//
// 战场地形取自城池在世界地图上的真实周边：以城池为中心，在地图坐标中取一个窗口，采样战略地图
// （TerrainModel.TerrainInfo：高度、坡度、群落、河流、湖海、海岸、道路、林地、农田），换算为战场地块。
// 窗口按进攻方来向旋转：出发城 → 目标城的方向对准攻方的布阵边（棋盘 x = 0 的西边），攻方从那一边进入，
// 本城在 (W − 3, ⌊H / 2⌋)（与 BattleModel 旧版布局相同：城墙 x = W−5 … W−1、y = cy−2 … cy+2，三座城门在西、南、北）。
// 可复现：结果只取决于 (剧本的世界地图, 城池, 来向)。取样时高度取程序化的 TerrainModel.Height（在主网格同样的格点上、
//   同样的三角形插值），而不是渲染出的地表——后者在建图时按各城当时的规模压平城址、在镜头一侧限高，会随游戏状态与建图时机而变。
//
// 棋盘坐标 → 地图坐标：P = C + (x − cx)·s·d + (y − cy)·s·p
//   C 城池地图坐标，d 进攻方向（出发城 → 目标城，单位向量），p = (−d.y, d.x)（d 逆时针 90°，
//   棋盘不镜像：自西向东进攻时棋盘 x = 地图东、棋盘 y = 地图北），s = 每格 0.5 地图单位（约 10 公里）。
//
// 地块（Terrain 的原有值，规则不变）+ 新增 Water = 8（深水：湖、海；不可通行，规则上等同山岳，
// Defs.TerrainCost 的 default 分支返回 99；外观为水面）。
//   湖海占格子一半以上 → 水域；
//   河川：取样栅格上河流中心线经过的格子连成四连通的河道（世界地图上宽于约 1.5 格的大河再加横向一格，河宽 ≤ 2）；
//     流到城下的河沿城外一圈（护城河）绕城、从城池背后的东边出棋盘；河道在棋盘中间断头时 3 格内接上河、水或棋盘边；
//   桥：道路过河处，按转换前的地图判定——桥面横跨河道（两端陆地、两侧是河或水），长 ≤ 2 格，相距 2 格以内不重复架桥；
//     城门前的护城河上必有桥（规则为平原，T.bridges）；
//   坡度 + 相对城池的高差 → 山岳 / 山丘（山岳不超过陆地的 28%、山丘不超过 40%）；林地 → 森林；道路上的山岳降为山丘（山口）。
// 可玩性保证：城外一圈无山无水、城门前通畅；攻方布阵边（x ≤ 1）山岳降为山丘，至少 6 个可布阵格（不足时把水域改为
// 浅滩 = 河川，即「登陆」）；从布阵边、以及前 8 个布阵格各自到城门必有通路（按代价最小的路线把山岳改为山丘、
// 水域改为浅滩）；被山岭 / 水域整个围住的谷地（≥ 4 格）开出山口。
// 渡海进攻（sea = 海路相连，或来向一侧大半是湖海）：攻方在城前的海岸登陆——海路时来向取最近岸的方向
// （每 10° 试一次，近岸与转角加权，可转到背面：turned 最大 ±180°）；陆路而来向一侧大半是水时窗口在 ±100° 内转向。
// 攻方一侧沿进攻方向压缩（kAtt = max(0.3, 所需)），使布阵边落在岸边；压缩到 0.3 仍不够时，海岸以西的海面改为浅滩（"shallows"）；
// 岸线弯曲时逐行把第一块陆地之前的海面改为浅滩（第三版定稿的登陆修正）。
//
// 公开接口
//   BattleTerrain.Water                              = 8（新增的深水地块，= Terrain.Water）
//   BattleTerrain.Generate(opts) → BattleTerrainMap | null
//       opts：city（城池；或 hasPos + x / y 地图坐标）、fromCity（出发城；或 hasFromPos + fromX / fromY）| fromDir（攻方来自的方位，
//       度数，地图坐标 0 = 东、90 = 北）| hasFromVec + fromVx / fromVy（由城池指向攻方的向量）；都缺省时视为自西而来。
//       sea、W、H（缺省 Balance.BattleW / BattleH）、view（TerrainModel；缺省见 CurrentModel）。没有可用的地形模型时返回 null。
//   BattleTerrain.ApplyTo(model, setup) → bool
//       用 setup.target / setup.src（sea = GameState.Current.LinkIsSea(src, target)）生成并写入 model.map / height / burning /
//       castle / terrain（BattleTerrainMap）并返回 true；返回 false 时什么也不改（调用方退回旧的随机地形）。
//   BattleTerrain.Ascii(T) → string          调试用的字符图（上 = 棋盘 y 大的一侧）。
//   BattleTerrain.RawAscii(view, city) → string   世界地图窗口（北在上、不旋转，取实际地表）的同一套分类，对照用。
//   BattleTerrain.IsWater(t) / Name(t)        水域判断 / 地块名（含「水域」）
//   BattleTerrain.BIOMES                      群落名表（grid.biome 的下标）
//   BattleTerrain.ModelSource                 取战略地图地形模型的接线（Game 设为 () => Map.Model；见 CurrentModel）
//
// 地形模型的来源（CurrentModel）：ModelSource → 反射取 Game.I.Map.Model（本文件不能直接引用 Game / MapView：
// 规则与地图的无头测试只编译规则文件与 View/WorldGeo.cs）→ 都没有时 TerrainModel.BuildHeadless(GameState, "auto")（按 GameState 缓存）。
// 规则测试（Tests~/sim）用 Tests~/sim/BattleTerrainStub.cs 替身（恒返回 false，同网页版 node 测试）；本文件的测试在 Tests~/battlemap/。
// ==========================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace Sanguo
{
    // 取样栅格：棋盘外各延伸 MX = 16 / MY = 14 格（战场画面的远景），步长 0.5 格。下标 o = j * nu + i，
    // 棋盘坐标 u = u0 + i * step（格子中心为整数）、v = v0 + j * step。
    //   h（地图单位，绝对高度；画面取相对 h0 的高度）、slope、water（0 无 1 河 2 湖 3 海）、forest、farm、sand、snow、
    //   biome（BattleTerrain.BIOMES 下标）、road（到道路的距离）、river（到河流的距离，按河宽归一）、coast（到海岸的距离，海中为负）
    public sealed class BattleTerrainGrid
    {
        public int nu, nv;
        public double u0, v0, step;
        public float[] h, slope, forest, farm, sand, snow, road, river, coast;
        public byte[] water, biome;
        public bool synth;   // 由地块类型合成的栅格（画面在没有取样结果时自行合成；本文件不生成）
    }

    // 桥（规则为平原）：axis = "x" 桥面沿棋盘 x、"y" 沿棋盘 y
    public struct BattleBridge { public int x, y; public string axis; }

    // 战场地形的生成结果（网页版 T）
    public sealed class BattleTerrainMap
    {
        public int version = 1, W, H;
        public double s;                         // 每格的地图单位
        public Terrain[,] map;
        public double[,] height;                 // 旧版 model.height 的语义（供旧画面）
        public Vector2Int castle;
        public List<Vector2Int> gates = new List<Vector2Int>();
        public string atkEdge = "west";
        public List<Vector2Int> atkTiles = new List<Vector2Int>(), defTiles = new List<Vector2Int>();
        public List<BattleBridge> bridges = new List<BattleBridge>();
        public bool[,] road;
        public double[,] farm;                   // 0..1
        public double dirX, dirY;                // 进攻方向（单位向量，地图坐标）
        public int angle, turned;                // 进攻方向（度）、相对名义来向的转角（度）
        public int cityId = -1, fromId = -1;     // 城池 / 出发城编号（无 → −1）
        public string cityName, fromName;
        public double cityX, cityY, fromX, fromY;
        public bool hasFrom;
        public double h0;                        // 城池地面高度（地图单位）
        public string biome, culture;            // 主要群落、城池文化
        public BattleTerrainGrid grid;
        public double kAtt, kNeed;               // 攻方一侧的压缩系数、所需的压缩（< kAtt 时有浅滩）
        public List<string> notes = new List<string>();   // "ford" | "pass" | "landing" | "shallows" | "moat" | "joined" | "turned:…"
        public double ms;
        public uint seed;
        public bool HasNote(string n) { return notes.Contains(n); }
    }

    public static class BattleTerrain
    {
        public const int Water = 8;
        const Terrain WATER = Terrain.Water;
        public static readonly string[] BIOMES = { "temperate", "steppe", "desert", "tropical", "savanna", "med", "boreal", "alpine" };
        public const double S_TILE = 0.5;         // 每格的地图单位
        public const int MX = 16, MY = 14;        // 棋盘外的取样边距（格）：战场画面的远景（BattleView 的 EXT_X / EXT_Y）
        const double STEP = 0.5;                  // 取样间距（格）
        const double RIV_T = 0.4;                 // 河流中心线距离（按河宽归一）小于此值 → 河川
        const double D2R = Math.PI / 180;
        const double K_MIN = 0.3;                 // 渡海登陆时攻方一侧的最大压缩（1 / 0.3 ≈ 3.3 倍）

        // 生成参数（网页版 opts）
        public sealed class Options
        {
            public City city;                     // 目标城池（或 hasPos + x / y：地图坐标）
            public bool hasPos; public double x, y;
            public City fromCity;                 // 出发城（或 hasFromPos + fromX / fromY）
            public bool hasFromPos; public double fromX, fromY;
            public double fromDir = double.NaN;   // 攻方来自的方位（度，地图坐标 0 = 东、90 = 北）
            public bool hasFromVec; public double fromVx, fromVy;   // 由城池指向攻方的向量
            public bool sea;                      // 海路相连
            public int W, H;                      // 0 = Balance.BattleW / BattleH
            public TerrainModel view;             // 缺省 CurrentModel(GameState.Current)
        }

        static int JsRound(double v) { return (int)Math.Floor(v + 0.5); }
        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double Or0(double v) { return double.IsNaN(v) || v == 0 ? 0 : v; }   // JS 的 `v || 0`（NaN、−0 → +0）
        // V8 的 Math.hypot（按最大值缩放 + Kahan 求和），与 Math.Sqrt(a² + b²) 末位可能不同
        static double JsHypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            double max = Math.Max(a, b);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            foreach (double v in new[] { a, b })
            {
                double n = v / max, summand = n * n - comp, pre = sum + summand;
                comp = (pre - sum) - summand; sum = pre;
            }
            return Math.Sqrt(sum) * max;
        }
        static uint Hash(int a, int b)
        {
            unchecked
            {
                uint h = ((uint)a * 0x9e3779b1u) ^ (((uint)b + 0x7f4a7c15u) * 0x85ebca6bu);
                h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
                return h;
            }
        }
        static int BiomeIdx(string b) { int i = b == null ? -1 : Array.IndexOf(BIOMES, b); return i >= 0 ? i : 0; }

        // ------------------------------------------------------------ 地形模型 --
        // 取战略地图地形模型的接线（Game.cs：BattleTerrain.ModelSource = () => Map != null ? Map.Model : null;）
        public static Func<TerrainModel> ModelSource;
        static TerrainModel headless; static GameState headlessFor; static string headlessRegion;
        static bool Ready(TerrainModel m) { return m != null && m.MH != null && m.Geo != null; }
        public static TerrainModel CurrentModel(GameState g)
        {
            TerrainModel m = null;
            if (ModelSource != null) { try { m = ModelSource(); } catch (Exception) { m = null; } }
            if (!Ready(m)) m = ReflectModel();
            if (Ready(m)) return m;
            // 没有建成的战略地图：无头构建地形 + 道路（同步，较慢；按 GameState 缓存）
            if (g == null || g.cities == null || g.cities.Count == 0) return null;
            string region = MapSpec.Resolve(g, "auto");
            if (headless != null && headlessFor == g && headlessRegion == region) return headless;
            try { headless = TerrainModel.BuildHeadless(g, region); headlessFor = g; headlessRegion = region; }
            catch (Exception e) { UnityEngine.Debug.LogWarning("BattleTerrain：无头构建地形失败 " + e); headless = null; headlessFor = null; return null; }
            return headless;
        }
        // Game.I.Map.Model（反射：规则 / 地图的无头测试不编译 Game、MapView）
        static TerrainModel ReflectModel()
        {
            try
            {
                var gt = typeof(BattleTerrain).Assembly.GetType("Sanguo.Game");
                if (gt == null) return null;
                var fi = gt.GetField("I", BindingFlags.Public | BindingFlags.Static);
                object game = fi != null ? fi.GetValue(null) : null;
                if (game == null) return null;
                var mf = gt.GetField("Map", BindingFlags.Public | BindingFlags.Instance);
                object map = mf != null ? mf.GetValue(game) : null;
                if (map == null) return null;
                var tf = map.GetType().GetField("Model", BindingFlags.Public | BindingFlags.Instance);
                return tf != null ? tf.GetValue(map) as TerrainModel : null;
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------ 取样 --
        static bool PosOf(City c, bool has, double x, double y, out double px, out double py)
        {
            if (c != null) { WorldGeo.CityPos(c, out px, out py); return true; }
            px = x; py = y;
            return has;
        }
        // 进攻方向（度，出发 → 目标），取整到 1°
        static int Approach(double Cx, double Cy, Options o)
        {
            double dx = 1, dy = 0, fx, fy;
            bool f = PosOf(o.fromCity, o.hasFromPos, o.fromX, o.fromY, out fx, out fy);
            if (f && (fx != Cx || fy != Cy)) { dx = Cx - fx; dy = Cy - fy; }
            else if (!double.IsNaN(o.fromDir) && !double.IsInfinity(o.fromDir)) { dx = -Math.Cos(o.fromDir * D2R); dy = -Math.Sin(o.fromDir * D2R); }
            else if (o.hasFromVec && (o.fromVx != 0 || o.fromVy != 0)) { dx = -o.fromVx; dy = -o.fromVy; }
            int a = JsRound(Math.Atan2(dy, dx) / D2R);
            return ((a % 360) + 360) % 360;
        }

        // 取样栅格：棋盘坐标 u（0 … W−1 为格子中心）∈ [−MX − 0.5, W − 0.5 + MX]，步长 STEP
        // k：攻方一侧（u < cx）沿进攻方向的压缩系数（渡海登陆时 < 1，见 CoastDist）
        static BattleTerrainGrid SampleGrid(TerrainModel view, Func<double, double, double> surf, double Cx, double Cy, int ang, int W, int H, int cx, int cy, double k)
        {
            double dX = Math.Cos(ang * D2R), dY = Math.Sin(ang * D2R), pX = -dY, pY = dX;
            if (!(k > 0)) k = 1;
            int nu = JsRound((W + 2 * MX) / STEP) + 1, nv = JsRound((H + 2 * MY) / STEP) + 1;
            double u0 = -MX - 0.5, v0 = -MY - 0.5; int N = nu * nv;
            var G = new BattleTerrainGrid
            {
                nu = nu, nv = nv, u0 = u0, v0 = v0, step = STEP,
                h = new float[N], slope = new float[N], water = new byte[N], forest = new float[N],
                farm = new float[N], sand = new float[N], snow = new float[N], biome = new byte[N],
                road = new float[N], river = new float[N], coast = new float[N],
            };
            for (int j = 0; j < nv; j++)
            {
                double v = v0 + j * STEP;
                for (int i = 0; i < nu; i++)
                {
                    double u = u0 + i * STEP, a = (u - cx) * S_TILE * (u < cx ? k : 1);
                    double mx = Cx + a * dX + (v - cy) * S_TILE * pX;
                    double my = Cy + a * dY + (v - cy) * S_TILE * pY;
                    var ti = view.TerrainInfo(mx, my, surf);
                    if (!ti.valid) return null;
                    int o = j * nu + i;
                    G.h[o] = (float)ti.h; G.slope[o] = (float)ti.slope;
                    G.water[o] = (byte)(ti.water == "sea" ? 3 : ti.water == "lake" ? 2 : ti.water == "river" ? 1 : 0);
                    G.forest[o] = (float)Or0(ti.forest); G.farm[o] = (float)Or0(ti.farm); G.sand[o] = (float)Or0(ti.sand); G.snow[o] = (float)Or0(ti.snow);
                    G.biome[o] = (byte)BiomeIdx(ti.biome);
                    G.road[o] = (float)Math.Min(double.IsNaN(ti.road) ? 999 : ti.road, 999); G.river[o] = (float)Math.Min(double.IsNaN(ti.river) ? 99 : ti.river, 99);
                    G.coast[o] = (float)Or0(ti.coast);
                }
            }
            return G;
        }
        // 渡海进攻：从城池沿来向回溯，第一处连续 0.4 单位以上的湖海的距离（找不到 → −1）
        static double CoastDist(TerrainModel view, Func<double, double, double> surf, double Cx, double Cy, int ang, double maxD, bool seaOnly)
        {
            double dX = Math.Cos(ang * D2R), dY = Math.Sin(ang * D2R);
            Func<double, bool> wet = t => { var ti = view.TerrainInfo(Cx - dX * t, Cy - dY * t, surf); return ti.valid && (ti.water == "sea" || (!seaOnly && ti.water == "lake")); };
            for (double t = 0.4; t <= maxD; t += 0.2) if (wet(t) && wet(t + 0.2) && wet(t + 0.4)) return t;
            return -1;
        }
        // 只看来向一侧（x ≤ 6）的格子中心有多少是湖海：渡海进攻时用来转向
        static double AttackSideWater(TerrainModel view, Func<double, double, double> surf, double Cx, double Cy, int ang, int W, int H, int cx, int cy)
        {
            double dX = Math.Cos(ang * D2R), dY = Math.Sin(ang * D2R), pX = -dY, pY = dX;
            int n = 0; double w = 0;
            for (int x = 0; x <= 6; x++)
                for (int y = 0; y < H; y++)
                {
                    double mx = Cx + (x - cx) * S_TILE * dX + (y - cy) * S_TILE * pX, my = Cy + (x - cx) * S_TILE * dY + (y - cy) * S_TILE * pY;
                    var ti = view.TerrainInfo(mx, my, surf);
                    if (!ti.valid) return 0;
                    n++;
                    if (ti.water == "sea" || ti.water == "lake") w += x <= 1 ? 2 : 1;
                    if (x <= 1) n++;
                }
            return n != 0 ? w / n : 0;
        }

        // 格子 (x, y) 的 3×3 取样（中心权重 4、边 2、角 1）
        struct TileStat { public double h, slope, forest, farm, sea, riv, rdC, rdMin, road; public int biome; }
        static TileStat Stats(BattleTerrainGrid G, int x, int y)
        {
            int ci = JsRound((x - G.u0) / G.step), cj = JsRound((y - G.v0) / G.step), nu = G.nu;
            double h = 0, sl = 0, fo = 0, fa = 0, sea = 0, riv = 0, rdMin = 99, road = 999;
            for (int b = -1; b <= 1; b++)
                for (int a = -1; a <= 1; a++)
                {
                    int o = (cj + b) * nu + ci + a;
                    double w = (a != 0 ? 1 : 2) * (b != 0 ? 1 : 2) / 16.0;
                    h += G.h[o] * w; sl += G.slope[o] * w; fo += G.forest[o] * w; fa += G.farm[o] * w;
                    if (G.water[o] >= 2) sea += w; else if (G.water[o] == 1) riv += w;
                    if (G.river[o] < rdMin) rdMin = G.river[o];
                    if (a == 0 || b == 0) road = Math.Min(road, G.road[o]);
                }
            int oc = cj * nu + ci;
            return new TileStat { h = h, slope = sl, forest = fo, farm = fa, sea = sea, riv = riv, rdC = G.river[oc], rdMin = rdMin, road = road, biome = G.biome[oc] };
        }

        // 河流中心线经过的格子：取样点到河流的距离（按河宽归一）沿 u 或 v 呈 V 形的局部极小（二阶差 > 0.04，排除与该轴平行的河段），
        // 且在河道内（< 0.75）。axis[x, y]：1 = 横跨方向为 x（河大致沿 y 流）、2 = 横跨方向为 y；0 = 不在中心线上
        static int[,] RiverCourse(BattleTerrainGrid G, int W, int H)
        {
            int nu = G.nu, nv = G.nv; var R = G.river; var Wt = G.water;
            var axis = new int[W, H]; var best = new double[W, H];
            for (int j = 1; j < nv - 1; j++)
                for (int i = 1; i < nu - 1; i++)
                {
                    int o = j * nu + i; double r = R[o];
                    if (r >= 0.75 || Wt[o] >= 2) continue;
                    int x = JsRound(G.u0 + i * G.step), y = JsRound(G.v0 + j * G.step);
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    double a = R[o - 1], b = R[o + 1], c = R[o - nu], d = R[o + nu];
                    double cu = r <= a && r < b ? a + b - 2 * r : 0, cv = r <= c && r < d ? c + d - 2 * r : 0;
                    double k = Math.Max(cu, cv);
                    if (k <= 0.04 || k <= best[x, y]) continue;
                    best[x, y] = k;
                    axis[x, y] = cu >= cv ? 1 : 2;
                }
            return axis;
        }

        struct Rug { public int x, y, i; public double r; }
        static void SortDesc(List<Rug> list) { list.Sort((p, q) => { int c = q.r.CompareTo(p.r); return c != 0 ? c : p.i.CompareTo(q.i); }); }   // 稳定（同 JS 的 Array.sort）

        static Terrain[,] Classify(BattleTerrainGrid G, int W, int H, double h0, out TileStat[,] st)
        {
            var map = new Terrain[W, H]; st = new TileStat[W, H]; var rug = new double[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var s = st[x, y] = Stats(G, x, y);
                    double rel = s.h - h0;
                    double r = rug[x, y] = s.slope + 0.1 * Math.Max(0, rel - 0.3);
                    Terrain t = Terrain.Plain;
                    if (s.sea >= 0.5) t = WATER;
                    else if (s.rdC < RIV_T || s.riv >= 0.45) t = Terrain.River;
                    else if (r >= 0.36) t = Terrain.Mountain;
                    else if (r >= 0.2) t = (s.forest > 0.6 && r < 0.25) ? Terrain.Forest : Terrain.Hill;
                    else if (s.forest >= 0.4) t = Terrain.Forest;
                    map[x, y] = t;
                }
            // 山岳不超过陆地格的 28%：只保留最陡的，其余降为山丘
            int land = 0; var mts = new List<Rug>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (map[x, y] != WATER) land++;
                    if (map[x, y] == Terrain.Mountain) mts.Add(new Rug { x = x, y = y, r = rug[x, y], i = mts.Count });
                }
            int cap = (int)Math.Floor(land * 0.28);
            if (mts.Count > cap)
            {
                SortDesc(mts);
                for (int i = cap; i < mts.Count; i++) map[mts[i].x, mts[i].y] = Terrain.Hill;
            }
            // 山丘不超过陆地格的 40%：最平缓的降为平原（林地仍为森林）
            var hills = new List<Rug>();
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (map[x, y] == Terrain.Hill) hills.Add(new Rug { x = x, y = y, r = rug[x, y], i = hills.Count });
            int capH = (int)Math.Floor(land * 0.4);
            if (hills.Count > capH)
            {
                SortDesc(hills);
                for (int i = capH; i < hills.Count; i++) { var q = hills[i]; map[q.x, q.y] = st[q.x, q.y].forest >= 0.4 ? Terrain.Forest : Terrain.Plain; }
            }
            // 河道：取样栅格上的河流中心线（到河流距离沿某一轴呈 V 形的局部极小）经过的格子连成一条四连通的河道；
            // 世界地图上宽于约 1.5 格的大河再加上横向离中心线较近的一格（河宽 ≤ 2）。中心线以外的河边格子按陆地分类
            // （不出现山岳 / 山丘：河岸）。这样河道不会因为按行 / 列截宽而断成一个个水塘。
            var axis = RiverCourse(G, W, H); var on = new int[W, H];
            Func<int, int, bool> band = (x, y) => x >= 0 && y >= 0 && x < W && y < H && map[x, y] == Terrain.River;
            Func<int, int, bool> isR = (x, y) => x >= 0 && y >= 0 && x < W && y < H && on[x, y] == 1;
            var wide = new List<Vector2Int>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    int ax = axis[x, y];
                    if (ax == 0 || map[x, y] == WATER) { on[x, y] = 0; continue; }
                    on[x, y] = 1;
                    int dx = ax == 1 ? 1 : 0, dy = ax == 1 ? 0 : 1;
                    int run = 1;
                    for (int k = 1; band(x + dx * k, y + dy * k); k++) run++;
                    for (int k = 1; band(x - dx * k, y - dy * k); k++) run++;
                    if (run >= 3)
                    {
                        double a = band(x + dx, y + dy) ? st[x + dx, y + dy].rdC : 99, b = band(x - dx, y - dy) ? st[x - dx, y - dy].rdC : 99;
                        if (Math.Min(a, b) < 99) wide.Add(a <= b ? new Vector2Int(x + dx, y + dy) : new Vector2Int(x - dx, y - dy));
                    }
                }
            foreach (var q in wide) on[q.x, q.y] = 1;
            // 斜向相接的河格补成四连通（补离中心线较近的那一格）
            for (int x = 0; x < W - 1; x++)
                for (int y = 0; y < H - 1; y++)
                {
                    bool a = isR(x, y), b = isR(x + 1, y + 1), c = isR(x + 1, y), d = isR(x, y + 1);
                    int kx = -1, ky = -1;
                    if (a && b && !c && !d) { if (st[x + 1, y].rdC <= st[x, y + 1].rdC) { kx = x + 1; ky = y; } else { kx = x; ky = y + 1; } }
                    else if (c && d && !a && !b) { if (st[x, y].rdC <= st[x + 1, y + 1].rdC) { kx = x; ky = y; } else { kx = x + 1; ky = y + 1; } }
                    if (kx >= 0 && map[kx, ky] != WATER) on[kx, ky] = 1;
                }
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (on[x, y] != 0) { map[x, y] = Terrain.River; continue; }
                    if (map[x, y] != Terrain.River) continue;
                    // 河边（原河带内、不在河道上）：平原或森林
                    map[x, y] = st[x, y].forest >= 0.4 ? Terrain.Forest : Terrain.Plain;
                }
            return map;
        }

        // 代价最小的通路：从布阵边（x = 0）到任一城门；返回路线（含两端）
        // starts：起点格（缺省为 x = 0 整列）
        static double Cost(Terrain t)
        {
            return t == Terrain.Plain || t == Terrain.Gate ? 1 : t == Terrain.Forest || t == Terrain.Hill ? 2 : t == Terrain.River ? 3 : t == Terrain.Mountain ? 10 : t == WATER ? 14 : double.PositiveInfinity;
        }
        static readonly int[] D4X = { 1, -1, 0, 0 }, D4Y = { 0, 0, 1, -1 };
        static List<Vector2Int> CheapestPath(Terrain[,] map, int W, int H, List<Vector2Int> goals, List<Vector2Int> starts)
        {
            int N = W * H;
            var D = new double[N]; var prev = new int[N]; var done = new bool[N];
            for (int i = 0; i < N; i++) { D[i] = double.PositiveInfinity; prev[i] = -1; }
            if (starts != null) foreach (var q in starts) { double c = Cost(map[q.x, q.y]); if (c < double.PositiveInfinity) D[q.y * W + q.x] = c; }
            else for (int y = 0; y < H; y++) { double c = Cost(map[0, y]); if (c < double.PositiveInfinity) D[y * W] = c; }
            var goalSet = new HashSet<int>();
            foreach (var g in goals) goalSet.Add(g.y * W + g.x);
            for (;;)
            {
                int bi = -1; double bd = double.PositiveInfinity;
                for (int i = 0; i < N; i++) if (!done[i] && D[i] < bd) { bd = D[i]; bi = i; }
                if (bi < 0) return null;
                done[bi] = true;
                if (goalSet.Contains(bi))
                {
                    var path = new List<Vector2Int>();
                    for (int k = bi; k >= 0; k = prev[k]) path.Add(new Vector2Int(k % W, k / W));
                    path.Reverse();
                    return path;
                }
                int x = bi % W, y0 = bi / W;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + D4X[d], ny = y0 + D4Y[d];
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int ni = ny * W + nx;
                    if (done[ni]) continue;
                    double c = Cost(map[nx, ny]);
                    if (double.IsPositiveInfinity(c)) continue;
                    if (bd + c < D[ni]) { D[ni] = bd + c; prev[ni] = bi; }
                }
            }
        }

        // 与游戏状态无关的地表：MapView 的主网格在建图时按各城当时的规模压平城址、并在镜头一侧限高，
        // 若直接取渲染出的地表，同一 (城池, 来向) 的战场会随建图时的状态（城池规模、何时建图）而变。
        // 这里在主网格同样的格点上取程序化高度 view.Height（只含与状态无关的城址平整），按同样的三角形插值；
        // 取样期间以它代替 SurfaceY（TerrainInfo 的高度、坡度、林地、水域都由它导出）。
        static Func<double, double, double> RawSurface(TerrainModel view, double Cx, double Cy, double R)
        {
            bool m = view.MH != null;
            double res = m && view.MRes > 0 ? view.MRes : 0.625, x0 = m ? view.MX0 : 0, z0 = m ? view.MZ0 : 0;
            int i0 = (int)Math.Floor((Cx - R - x0) / res), j0 = (int)Math.Floor((Cy - R - z0) / res);
            int nx = (int)Math.Ceiling((Cx + R - x0) / res) - i0 + 1, nz = (int)Math.Ceiling((Cy + R - z0) / res) - j0 + 1;
            var h = new float[nx * nz];
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) h[j * nx + i] = (float)view.Height(x0 + (i0 + i) * res, z0 + (j0 + j) * res);
            return (x, z) =>
            {
                double fx0 = (x - x0) / res - i0, fz0 = (z - z0) / res - j0;
                int i = (int)Math.Floor(fx0), j = (int)Math.Floor(fz0);
                if (i < 0 || j < 0 || i >= nx - 1 || j >= nz - 1) return view.Height(x, z);
                double fx = fx0 - i, fz = fz0 - j; int o = j * nx + i;
                double ha = h[o], hb = h[o + nx], hc = h[o + nx + 1], hd = h[o + 1];
                if (((i + i0 + j + j0) & 1) == 0)
                {
                    if (fz >= fx) return ha + fz * (hb - ha) + fx * (hc - hb);
                    return ha + fx * (hd - ha) + fz * (hc - hd);
                }
                if (fx + fz <= 1) return ha + fx * (hd - ha) + fz * (hb - ha);
                return hc + (1 - fx) * (hb - hc) + (1 - fz) * (hd - hc);
            };
        }

        // ------------------------------------------------------------ 生成 --
        public static BattleTerrainMap Generate(Options opts)
        {
            if (opts == null) opts = new Options();
            var sw = Stopwatch.StartNew();
            int W = opts.W > 0 ? opts.W : Balance.BattleW, H = opts.H > 0 ? opts.H : Balance.BattleH;
            var view = opts.view != null ? opts.view : CurrentModel(GameState.Current);
            double Cx, Cy;
            bool hasC = PosOf(opts.city, opts.hasPos, opts.x, opts.y, out Cx, out Cy);
            if (!Ready(view) || !hasC || W < 10 || H < 7) return null;
            int cx = W - 3, cy = H >> 1;
            double R = JsHypot((cx + MX + 2) * S_TILE, (Math.Max(cy, H - cy) + MY + 2) * S_TILE) + 1;
            var surf = RawSurface(view, Cx, Cy, R);
            return GenerateIn(opts, sw, W, H, view, surf, Cx, Cy, cx, cy);
        }

        static void Note(List<string> notes, string n) { if (notes.IndexOf(n) < 0) notes.Add(n); }

        static BattleTerrainMap GenerateIn(Options opts, Stopwatch sw, int W, int H, TerrainModel view, Func<double, double, double> surf, double Cx, double Cy, int cx, int cy)
        {
            int ang = Approach(Cx, Cy, opts), ang0 = ang;
            double kAtt = 1, kNeed = 1, xCoast = -1;
            var notes = new List<string>();
            // 渡海进攻：来向一侧大半是湖海时，攻方在城前的海岸登陆——攻方一侧沿进攻方向压缩，使布阵边落在岸边；
            // 来向上找不到海岸时，转向最近的可登陆海岸（±100° 内）
            try
            {
                // 海路（opts.sea）：攻方从海上来，取与名义来向最接近的近岸方向（每 10° 试一次，近岸与转角加权）
                if (opts.sea)
                {
                    int bestA = 0; double bestScore = 0; bool has = false;
                    for (int k = 0; k < 36; k++)
                    {
                        int a = (ang0 + k * 10) % 360, da = Math.Min(k * 10, 360 - k * 10);
                        double dc0 = CoastDist(view, surf, Cx, Cy, a, (cx + 1) * S_TILE, true);
                        if (dc0 <= 0) continue;
                        double score = dc0 + da * 0.025;
                        if (!has || score < bestScore) { has = true; bestA = a; bestScore = score; }
                    }
                    if (has && bestA != ang) { ang = bestA; notes.Add("turned:" + ((((ang - ang0 + 540) % 360) - 180)).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                }
                double w0 = AttackSideWater(view, surf, Cx, Cy, ang, W, H, cx, cy);
                double dc = w0 > 0.5 || opts.sea ? CoastDist(view, surf, Cx, Cy, ang, (cx + 1) * S_TILE, opts.sea) : -1;
                if (dc > 0)
                {
                    // 压缩不超过 1 / K_MIN 倍（地形不至于被拉得面目全非）；海岸仍在布阵边以内时，岸前的海面改为浅滩（见下）
                    kNeed = dc / ((cx - 1) * S_TILE);
                    kAtt = Clamp(kNeed, K_MIN, 1);
                    xCoast = cx - dc / (S_TILE * kAtt);
                    notes.Add("landing");
                }
                else if (w0 > 0.55 && !opts.sea)
                {
                    int bestA = ang; double bestW = w0;
                    foreach (int k in new[] { 20, 40, 60, 80, 100 })
                    {
                        foreach (int sgn in new[] { 1, -1 })
                        {
                            int a = (ang0 + sgn * k + 360) % 360; double w = AttackSideWater(view, surf, Cx, Cy, a, W, H, cx, cy);
                            if (w < bestW - 0.05) { bestA = a; bestW = w; }
                        }
                        if (bestW < 0.4) break;
                    }
                    if (bestA != ang) { ang = bestA; notes.Add("turned:" + ((((ang - ang0 + 540) % 360) - 180)).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                }
            }
            catch (Exception) { /* 取样失败时按原方向 */ }
            var G = SampleGrid(view, surf, Cx, Cy, ang, W, H, cx, cy, kAtt);
            if (G == null) return null;
            int oc = JsRound((cy - G.v0) / STEP) * G.nu + JsRound((cx - G.u0) / STEP);
            double h0 = G.h[oc];
            TileStat[,] st;
            var map = Classify(G, W, H, h0, out st);

            // 登陆：压缩到 K_MIN 仍不够时，海岸（xCoast）以西的海面为浅滩（河川：可涉水通行）
            if (xCoast > 0.5)
            {
                int n = 0;
                for (int x = 0; x < W && x < xCoast; x++) for (int y = 0; y < H; y++) if (map[x, y] == WATER) { map[x, y] = Terrain.River; n++; }
                if (n != 0) notes.Add("shallows");
            }
            // 登陆（续）：岸线弯曲时只按中线算的 xCoast 会在其余各行留下深水，全军只能挤过一格渡口。逐行找第一块陆地 coast(y)，
            // 其前的海面改为浅滩——中间 cy ± 3 行一律如此（整行是水则铺到城前），其余行在 coast(y) ≤ W − 6 时同样处理
            if (notes.IndexOf("landing") >= 0)
            {
                int n = 0;
                for (int y = 0; y < H; y++)
                {
                    int coast = -1;
                    for (int x = 0; x < W; x++) if (map[x, y] != WATER && map[x, y] != Terrain.River) { coast = x; break; }
                    if (coast < 0) coast = W - 5;
                    if (Math.Abs(y - cy) > 3 && coast > W - 6) continue;
                    for (int x = 0; x < coast; x++) if (map[x, y] == WATER) { map[x, y] = Terrain.River; n++; }
                }
                if (n != 0 && notes.IndexOf("shallows") < 0) notes.Add("shallows");
            }

            // 城池（与 BattleModel 旧版相同的布局）
            Func<int, int, bool> inC = (x, y) => x >= W - 5 && y >= cy - 2 && y <= cy + 2;
            bool cutRiver = false, cutEast = false;
            for (int x = W - 5; x < W; x++) for (int y = cy - 2; y <= cy + 2; y++) if (map[x, y] == Terrain.River) { cutRiver = true; if (x == W - 1) cutEast = true; }
            for (int x = W - 5; x < W; x++)
                for (int y = cy - 2; y <= cy + 2; y++)
                {
                    bool edge = x == W - 5 || y == cy - 2 || y == cy + 2 || x == W - 1;
                    map[x, y] = edge ? Terrain.Wall : Terrain.Plain;
                }
            var gates = new List<Vector2Int> { new Vector2Int(W - 5, cy), new Vector2Int(W - 3, cy - 2), new Vector2Int(W - 3, cy + 2) };
            foreach (var g in gates) map[g.x, g.y] = Terrain.Gate;
            map[cx, cy] = Terrain.Castle;
            // 城外一圈：无山、无深水；西门外的通道
            for (int x = W - 6; x < W; x++)
                for (int y = cy - 3; y <= cy + 3; y++)
                {
                    if (y < 0 || y >= H || inC(x, y)) continue;
                    if (map[x, y] == Terrain.Mountain) map[x, y] = Terrain.Hill;
                    else if (map[x, y] == WATER) map[x, y] = Terrain.Plain;
                }
            for (int x = W - 8; x < W - 5; x++) if (map[x, cy] == Terrain.Mountain) map[x, cy] = Terrain.Hill;
            foreach (var gq in new[] { new Vector2Int(W - 6, cy), new Vector2Int(W - 3, cy - 3), new Vector2Int(W - 3, cy + 3) })
                if (gq.y >= 0 && gq.y < H && (map[gq.x, gq.y] == Terrain.Mountain || map[gq.x, gq.y] == WATER)) map[gq.x, gq.y] = Terrain.Plain;
            // 流到城下的河：沿城外一圈（护城河）绕城，从东边（城池背后的棋盘边）流出，不在城墙下断成水塘
            var ring = new List<Vector2Int>();
            for (int x = W - 1; x >= W - 6; x--) ring.Add(new Vector2Int(x, cy - 3));
            for (int y = cy - 2; y <= cy + 2; y++) ring.Add(new Vector2Int(W - 6, y));
            for (int x = W - 6; x < W; x++) ring.Add(new Vector2Int(x, cy + 3));
            Func<int, int, bool> isRiv = (x, y) => x < 0 || y < 0 || x >= W || y >= H || map[x, y] == Terrain.River || map[x, y] == WATER;
            var ringIdx = new List<int>();
            for (int k = 0; k < ring.Count; k++) if (map[ring[k].x, ring[k].y] == Terrain.River) ringIdx.Add(k);
            // 环上的河格是河的尽头（城下断流）：城外的河邻 ≤ 1；或两格宽的河头（两格互为河邻、各自另有至多一个河邻）
            Func<int, int, int, int, List<Vector2Int>> rivN = (x, y, sx, sy) =>
            {
                var outL = new List<Vector2Int>();
                for (int d = 0; d < 4; d++)
                {
                    int a = x + D4X[d], b = y + D4Y[d];
                    if (a == sx && b == sy) continue;
                    if (!inC(a, b) && isRiv(a, b)) outL.Add(new Vector2Int(a, b));
                }
                return outL;
            };
            Func<int, bool> deadEnd = k =>
            {
                var q = ring[k]; var Nb = rivN(q.x, q.y, int.MinValue, int.MinValue);
                if (Nb.Count <= 1) return true;
                if (Nb.Count != 2) return false;
                foreach (var r in Nb)
                    if (ring.Contains(r) && rivN(r.x, r.y, q.x, q.y).Count <= 1) return true;
                return false;
            };
            bool anyDead = false;
            foreach (int k in ringIdx) if (deadEnd(k)) { anyDead = true; break; }
            if (ringIdx.Count > 0 && (cutRiver || anyDead))
            {
                int a = ringIdx[0], b = ringIdx[ringIdx.Count - 1];
                // 只有一处入口（或河本来从城池背后流出）：引到较近的东端出棋盘
                if (a == b || cutEast || deadEnd(a) && deadEnd(b) && b - a < 3) { if (a < ring.Count - 1 - b) a = 0; else b = ring.Count - 1; }
                for (int k = a; k <= b; k++) { var q = ring[k]; if (map[q.x, q.y] != WATER) map[q.x, q.y] = Terrain.River; }
                notes.Add("moat");
            }
            // 河道在棋盘中间断头（世界地图的河在取样时偶有缺口、或被压缩 / 截宽）：沿来向延伸，3 格内接上河、水或棋盘边就补上
            {
                Func<int, int, bool> wetT = (x, y) => x < 0 || y < 0 || x >= W || y >= H || map[x, y] == Terrain.River || map[x, y] == WATER;
                Func<int, int, bool> solid = (x, y) => inC(x, y) || map[x, y] == Terrain.Wall || map[x, y] == Terrain.Gate || map[x, y] == Terrain.Castle;
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (map[x, y] != Terrain.River || x == 0 || y == 0 || x == W - 1 || y == H - 1) continue;
                        int nbN = 0, nbX = 0, nbY = 0;
                        for (int d = 0; d < 4; d++) if (wetT(x + D4X[d], y + D4Y[d])) { if (nbN == 0) { nbX = D4X[d]; nbY = D4Y[d]; } nbN++; }
                        if (nbN != 1 || map[x + nbX, y + nbY] == WATER) continue;
                        int dx = -nbX, dy = -nbY;
                        List<Vector2Int> bestPath = null; double bestD = 0;
                        // 直行，或先直行再向两侧偏一格（取到河流中心线较近的一路）
                        foreach (int side in new[] { 0, 1, -1 })
                        {
                            var path = new List<Vector2Int>();
                            int px = x, py = y; bool ok = false;
                            for (int k = 1; k <= 3; k++)
                            {
                                if (side != 0 && k == 2) { px += dy * side; py += dx * side; } else { px += dx; py += dy; }
                                if (px < 0 || py < 0 || px >= W || py >= H) { ok = path.Count > 0 || k == 1; break; }
                                if (solid(px, py)) break;
                                if (map[px, py] == Terrain.River || map[px, py] == WATER) { ok = true; break; }
                                path.Add(new Vector2Int(px, py));
                                bool touch = false;
                                for (int e = 0; e < 4 && !touch; e++)
                                {
                                    int a = px + D4X[e], b = py + D4Y[e];
                                    if (a == x && b == y) continue;
                                    if (path.Contains(new Vector2Int(a, b))) continue;
                                    if (a >= 0 && b >= 0 && a < W && b < H && (map[a, b] == Terrain.River || map[a, b] == WATER) && !(Math.Abs(a - x) + Math.Abs(b - y) <= 1)) touch = true;
                                }
                                if (touch) { ok = true; break; }
                            }
                            if (!ok || path.Count == 0) continue;
                            double sum = 0; foreach (var q in path) sum += st[q.x, q.y].rdC;
                            double dd = sum / path.Count;
                            if (bestPath == null || path.Count < bestPath.Count || (path.Count == bestPath.Count && dd < bestD)) { bestPath = path; bestD = dd; }
                        }
                        if (bestPath != null) { foreach (var q in bestPath) map[q.x, q.y] = Terrain.River; Note(notes, "joined"); }
                    }
            }

            // 道路：山岳降为山丘（山口），过河处架桥
            var road = new bool[W, H]; var farm = new double[W, H]; var bridges = new List<BattleBridge>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (inC(x, y)) continue;
                    var s = st[x, y];
                    farm[x, y] = s.farm;
                    if (s.road < 0.2)
                    {
                        road[x, y] = true;
                        if (map[x, y] == Terrain.Mountain) { map[x, y] = Terrain.Hill; Note(notes, "pass"); }
                    }
                }
            // 桥：按转换前的地图判定（新建的桥不当作陆地），桥面沿一轴横跨河道：两端是陆地、两侧（垂直于桥面）仍是河或水，
            // 桥长 ≤ 2 格（河宽）；离已有的桥 2 格以内不再架桥（每处过河只一座，不会沿河连成长堤或 L 形）。
            // 城门前的护城河上必有桥（西门、南门、北门）。
            var pre = (Terrain[,])map.Clone();
            Func<int, int, bool> wetP = (x, y) => x < 0 || y < 0 || x >= W || y >= H || pre[x, y] == Terrain.River || pre[x, y] == WATER;
            Func<int, int, bool> landP = (x, y) => x >= 0 && y >= 0 && x < W && y < H && pre[x, y] != Terrain.River && pre[x, y] != WATER && pre[x, y] != Terrain.Mountain && pre[x, y] != Terrain.Wall;
            Func<int, int, bool> near = (x, y) => { foreach (var b in bridges) if (Math.Max(Math.Abs(b.x - x), Math.Abs(b.y - y)) <= 2) return true; return false; };
            Func<int, int, string, List<Vector2Int>> span = (x, y, ax) =>
            {
                int dx = ax == "x" ? 1 : 0, dy = 1 - dx;
                Func<int, int, bool> flank = (a, b) => wetP(a - dy, b - dx) && wetP(a + dy, b + dx);
                if (pre[x, y] != Terrain.River || !flank(x, y)) return null;
                if (landP(x - dx, y - dy) && landP(x + dx, y + dy)) return new List<Vector2Int> { new Vector2Int(x, y) };
                if (landP(x - dx, y - dy) && x + dx < W && y + dy < H && pre[x + dx, y + dy] == Terrain.River && flank(x + dx, y + dy) && landP(x + 2 * dx, y + 2 * dy))
                    return new List<Vector2Int> { new Vector2Int(x, y), new Vector2Int(x + dx, y + dy) };
                return null;
            };
            Action<List<Vector2Int>, string> build = (deck, ax) => { foreach (var q in deck) { map[q.x, q.y] = Terrain.Plain; bridges.Add(new BattleBridge { x = q.x, y = q.y, axis = ax }); } };
            {
                int[] gx = { W - 6, W - 3, W - 3 }, gy = { cy, cy - 3, cy + 3 }; string[] gax = { "x", "y", "y" };
                for (int i = 0; i < 3; i++)
                {
                    if (gy[i] < 0 || gy[i] >= H || pre[gx[i], gy[i]] != Terrain.River) continue;
                    build(new List<Vector2Int> { new Vector2Int(gx[i], gy[i]) }, gax[i]);
                }
            }
            var cand = new List<Rug>();
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (pre[x, y] == Terrain.River && road[x, y] && st[x, y].road <= 0.14) cand.Add(new Rug { x = x, y = y, r = st[x, y].road, i = cand.Count });
            cand.Sort((p, q) => { int c = p.r.CompareTo(q.r); return c != 0 ? c : p.i.CompareTo(q.i); });   // 升序、稳定
            foreach (var q in cand)
            {
                if (near(q.x, q.y)) continue;
                var sx = span(q.x, q.y, "x"); var sy = span(q.x, q.y, "y");
                var deck = sx != null && sy != null ? (sx.Count <= sy.Count ? sx : sy) : sx ?? sy;
                if (deck == null) continue;
                bool bad = false; foreach (var e in deck) if (near(e.x, e.y)) { bad = true; break; }
                if (bad) continue;
                build(deck, deck == sx ? "x" : "y");
            }

            // 攻方布阵边
            for (int y = 0; y < H; y++) for (int x = 0; x < 2; x++) if (map[x, y] == Terrain.Mountain) map[x, y] = Terrain.Hill;
            var spots0 = new List<Vector2Int>();
            for (int i = 0; i < H; i++)
            {
                int y = (H >> 1) + ((i % 2 == 0) ? (i >> 1) : -((i + 1) >> 1));
                if (y >= 0 && y < H) { spots0.Add(new Vector2Int(1, y)); spots0.Add(new Vector2Int(0, y)); }
            }
            Func<Terrain, bool> pass = t => t == Terrain.Plain || t == Terrain.Forest || t == Terrain.Hill || t == Terrain.River || t == Terrain.Gate || t == Terrain.Castle;
            int okSpots = 0; foreach (var q in spots0) if (pass(map[q.x, q.y])) okSpots++;
            foreach (var q in spots0)
            {
                if (okSpots >= 6) break;
                if (map[q.x, q.y] == WATER) { map[q.x, q.y] = Terrain.River; okSpots++; Note(notes, "landing"); }
            }
            // 通路：从整条布阵边、再从前 8 个布阵格各自出发，代价最小的路线上山岳 → 山丘、水域 → 浅滩（河川）
            Action<List<Vector2Int>> carve = path =>
            {
                if (path == null) return;
                foreach (var q in path)
                {
                    if (map[q.x, q.y] == Terrain.Mountain) { map[q.x, q.y] = Terrain.Hill; Note(notes, "pass"); }
                    else if (map[q.x, q.y] == WATER) { map[q.x, q.y] = Terrain.River; Note(notes, "ford"); }
                }
            };
            carve(CheapestPath(map, W, H, gates, null));
            {
                var first = new List<Vector2Int>();
                foreach (var q in spots0) if (pass(map[q.x, q.y])) { first.Add(q); if (first.Count >= 8) break; }
                foreach (var q in first) carve(CheapestPath(map, W, H, gates, new List<Vector2Int> { q }));
            }
            // 被山岭 / 水域整个围住的谷地（≥ 4 格、到不了城门）：开一条最省的山口或浅滩连出来
            for (int iter = 0; iter < 4; iter++)
            {
                var reach = new bool[W * H]; var st2 = new List<Vector2Int>(gates);
                foreach (var g in gates) reach[g.y * W + g.x] = true;
                while (st2.Count > 0)
                {
                    var a = st2[st2.Count - 1]; st2.RemoveAt(st2.Count - 1);
                    for (int d = 0; d < 4; d++)
                    {
                        int x = a.x + D4X[d], y = a.y + D4Y[d];
                        if (x < 0 || y < 0 || x >= W || y >= H || reach[y * W + x] || !pass(map[x, y])) continue;
                        reach[y * W + x] = true; st2.Add(new Vector2Int(x, y));
                    }
                }
                List<Vector2Int> comp = null;
                var seen = new bool[W * H];
                for (int x = 0; x < W && comp == null; x++)
                    for (int y = 0; y < H && comp == null; y++)
                    {
                        if (reach[y * W + x] || seen[y * W + x] || !pass(map[x, y]) || inC(x, y)) continue;
                        var c = new List<Vector2Int> { new Vector2Int(x, y) }; var q = new List<Vector2Int> { new Vector2Int(x, y) };
                        seen[y * W + x] = true;
                        while (q.Count > 0)
                        {
                            var a = q[q.Count - 1]; q.RemoveAt(q.Count - 1);
                            for (int d = 0; d < 4; d++)
                            {
                                int xx = a.x + D4X[d], yy = a.y + D4Y[d];
                                if (xx < 0 || yy < 0 || xx >= W || yy >= H || seen[yy * W + xx] || !pass(map[xx, yy]) || inC(xx, yy)) continue;
                                seen[yy * W + xx] = true; c.Add(new Vector2Int(xx, yy)); q.Add(new Vector2Int(xx, yy));
                            }
                        }
                        if (c.Count >= 4) comp = c;
                    }
                if (comp == null) break;
                carve(CheapestPath(map, W, H, gates, comp));
            }

            // 旧版 model.height 的语义（平原的微起伏、丘陵 0.45、山岳 1.1、河 −0.15）
            var height = new double[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var t = map[x, y];
                    height[x, y] = t == Terrain.Mountain ? 1.1 : t == Terrain.Hill ? 0.45 : t == Terrain.River || t == WATER ? -0.15 : inC(x, y) ? 0.05 : Clamp((st[x, y].h - h0) * 0.12, -0.05, 0.12);
                }
            var T = new BattleTerrainMap { W = W, H = H, s = S_TILE, map = map, height = height, castle = new Vector2Int(cx, cy), gates = gates, bridges = bridges, road = road, farm = farm };
            foreach (var q in spots0) if (pass(map[q.x, q.y])) T.atkTiles.Add(q);
            foreach (var q in new[] { new Vector2Int(cx, cy), new Vector2Int(W - 4, cy), new Vector2Int(W - 6, cy), new Vector2Int(W - 4, cy - 1), new Vector2Int(W - 4, cy + 1), new Vector2Int(W - 2, cy - 1), new Vector2Int(W - 2, cy + 1) })
                if (pass(map[q.x, q.y])) T.defTiles.Add(q);
            // 主要群落
            var bc = new int[BIOMES.Length];
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) bc[st[x, y].biome]++;
            int bi = 0; for (int i = 1; i < bc.Length; i++) if (bc[i] > bc[bi]) bi = i;
            var city = opts.city; var from = opts.fromCity;
            double fpx, fpy; bool fp = PosOf(from, opts.hasFromPos, opts.fromX, opts.fromY, out fpx, out fpy);
            // 城池文化（= CultureArt.CultureOfCity：city.culture，缺省汉地）
            string culture = "han";
            if (city != null && !string.IsNullOrEmpty(city.name)) culture = string.IsNullOrEmpty(city.culture) ? "han" : city.culture;
            T.dirX = Math.Cos(ang * D2R); T.dirY = Math.Sin(ang * D2R); T.angle = ang; T.kAtt = kAtt; T.kNeed = kNeed;
            T.turned = ang != ang0 ? (((ang - ang0 + 540) % 360) - 180) : 0;
            T.cityId = city != null ? city.id : -1; T.cityName = city != null ? city.name : null; T.cityX = Cx; T.cityY = Cy;
            if (fp) { T.hasFrom = true; T.fromId = from != null ? from.id : -1; T.fromName = from != null ? from.name : null; T.fromX = fpx; T.fromY = fpy; }
            T.h0 = h0; T.biome = BIOMES[bi]; T.culture = culture; T.grid = G; T.notes = notes;
            T.seed = Hash(city != null ? city.id : JsRound(Cx * 7 + Cy * 13), ang);
            T.ms = sw.Elapsed.TotalMilliseconds;
            return T;
        }

        // ------------------------------------------------------------ 接线 --
        // BattleModel 构造时调用：取自城池真实周边、按来向旋转；返回 false 时调用方退回旧的随机地形
        public static bool ApplyTo(BattleModel model, BattleSetup setup)
        {
            if (model == null || setup == null || setup.target == null) return false;
            BattleTerrainMap T = null;
            var src = setup.src;
            bool sea = false;
            try { var g = GameState.Current; sea = src != null && g != null && g.LinkIsSea(src.id, setup.target.id); } catch (Exception) { sea = false; }
            try { T = Generate(new Options { city = setup.target, fromCity = src, sea = sea, W = model.W, H = model.H }); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("BattleTerrain.Generate " + e); T = null; }
            if (T == null) return false;
            model.map = T.map;
            model.height = new float[T.W, T.H];
            for (int x = 0; x < T.W; x++) for (int y = 0; y < T.H; y++) model.height[x, y] = (float)T.height[x, y];
            model.burning = new int[T.W, T.H];
            model.castle = new Vector2Int(T.castle.x, T.castle.y);
            model.terrain = T;
            return true;
        }

        // ------------------------------------------------------------ 调试 --
        static char Ch(Terrain t)
        {
            switch (t)
            {
                case Terrain.Plain: return '.'; case Terrain.Forest: return 'f'; case Terrain.Hill: return 'h'; case Terrain.Mountain: return 'M';
                case Terrain.River: return '='; case Terrain.Wall: return '#'; case Terrain.Gate: return 'G'; case Terrain.Castle: return 'C';
                case Terrain.Water: return '~';
            }
            return '?';
        }
        // 字符图（上 = 棋盘 y 大的一侧）：. 平原 f 森林 h 山丘 M 山岳 = 河川 # 城墙 G 城门 C 本城 ~ 水域 b 桥 + 道路（平原上）
        public static string Ascii(BattleTerrainMap T) { return Ascii(T.W, T.H, T.map, T.road, T.bridges); }
        static string Ascii(int W, int H, Terrain[,] map, bool[,] road, List<BattleBridge> bridges)
        {
            var br = new HashSet<int>();
            if (bridges != null) foreach (var b in bridges) br.Add(b.y * W + b.x);
            var sb = new System.Text.StringBuilder();
            for (int y = H - 1; y >= 0; y--)
            {
                for (int x = 0; x < W; x++)
                {
                    var t = map[x, y];
                    char c = Ch(t);
                    if (br.Contains(y * W + x)) c = 'b';
                    else if (t == Terrain.Plain && road != null && road[x, y]) c = '+';
                    sb.Append(c);
                }
                if (y > 0) sb.Append('\n');
            }
            return sb.ToString();
        }
        // 世界地图窗口（北在上，不旋转，取实际地表），同一套分类
        public static string RawAscii(TerrainModel view, City city, int W = 18, int H = 13)
        {
            if (!Ready(view) || city == null) return "";
            double Cx, Cy; WorldGeo.CityPos(city, out Cx, out Cy);
            int cx = W - 3, cy = H >> 1;
            var G = SampleGrid(view, null, Cx, Cy, 0, W, H, cx, cy, 1);
            if (G == null) return "";
            int oc = JsRound((cy - G.v0) / STEP) * G.nu + JsRound((cx - G.u0) / STEP);
            TileStat[,] st;
            var map = Classify(G, W, H, G.h[oc], out st);
            return Ascii(W, H, map, null, null);
        }

        public static bool IsWater(Terrain t) { return (int)t == Water; }
        // 地块名（含「水域」）
        public static string Name(Terrain t) { return (int)t == Water ? "水域" : Defs.TerrainName(t); }
    }
}
