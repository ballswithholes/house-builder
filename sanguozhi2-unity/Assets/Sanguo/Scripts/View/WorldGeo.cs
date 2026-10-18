// 三国志-霸王的世界 · 世界地理查询（第二版 §4G；第三版 DESIGN-V3 §1：平滑、细致的地形）
// ← 网页版 js/world-geo.js（地图范围、地区、投影后的地理数据缓存）+ js/map-view.js 中与渲染无关的部分
//   （地理场栅格化；第三版的高度栅格 / 主网格 / 远景栅格、城址压平、数据纹理、地表群落与农田、城外村落、
//    树木 / 石块 / 灌丛 / 村舍分布、道路与桥、关隘、TerrainInfo / SurfaceY / RoadDist、海域栅格与海上航线）。
// 本文件只用 Mathf / Color / Vector2，不碰场景、网格与材质：Tests~/map 用 UnityStub 无头编译，
// 与 Node 运行网页版得到的参考值逐项比对（投影、距离场、生物群落取样、高度、树木、航线）。
//
// 公开接口
//   WorldGeo.Project(lon, lat) / Unproject(x, y)     → MapProjection（GameState.cs）的薄包装
//   WorldGeo.X0 Y0 X1 Y1 W H、Bounds、China           整图范围、经典剧本镜头范围（地图坐标）
//   WorldGeo.Regions[key] / RegionRect(key | 经纬度框)  镜头飞行用的地区（world china europe med arabia iran india tarim korea seasia）
//   WorldGeo.Data                                     投影后的地理数据（缓存）：陆地环、河流、湖泊、山脉、高原、生物群落
//   MapSpec.Classic() / MapSpec.World()               地图构建范围（与网页版 classicSpec / worldSpec 相同）
//   MapSpec.Resolve(g, region) / ForRegion(region)    区域判定（auto → 城池全在旧版中国地图内为 china，否则 world；= 网页版 _spec）
//   GeoFields.For(spec)                               地理场（按范围缓存）：CoastDist InSea RiverDist（RW / RD）Sample RidgeSum
//   TerrainModel                                      Height SurfaceY TerrainInfo RoadDist GenTerrain PlaceTrees PlaceScatter PlanRoads PlanPasses
//                                                     BuildSeaMask SeaPath；BuildHeadless(g, region) = 无头的地形 + 道路（战场地形取样用；region 可为 auto）
//   WorldGeo.Perlin(x, y)                             网页版 SG.M.perlinNoise 的移植（double；地图的一切噪声都用它，与网页版逐点一致）
//   WorldGeo.CultureBase(code)                        网页版 SG.cultureBase（转发 Defs.CultureBase，唯一的回退表）：india → seasia，africa / moor → arab，基础文化返回自身，未知 → han
// 分步计算一律写成 IEnumerator：yield return null 为一个可让出的步点，yield return 子 IEnumerator 为嵌套（MapView 展开执行）。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    // 地图坐标矩形（Unity x / z）
    public struct MapRect
    {
        public float xMin, yMin, xMax, yMax;
        public MapRect(float x0, float y0, float x1, float y1) { xMin = x0; yMin = y0; xMax = x1; yMax = y1; }
        public float Cx { get { return (xMin + xMax) / 2; } }
        public float Cy { get { return (yMin + yMax) / 2; } }
        public MapRect Pad(float p) { return new MapRect(xMin - p, yMin - p, xMax + p, yMax + p); }
    }

    // 镜头飞行用的地区
    public sealed class WorldRegion
    {
        public string Key, Name;
        public float Lon0, Lon1, Lat0, Lat1;
        public MapRect Rect;
        public float Cx { get { return Rect.Cx; } }
        public float Cy { get { return Rect.Cy; } }
    }

    // 投影后的地理数据（js/world-geo.js 的 projected()）
    public sealed class GeoProjected
    {
        public sealed class Line { public string Name; public float W = 1, D = -0.35f; public double A, B, Wd, H, V; public double[] Xy; }
        public double[][] Land;
        public string[] LandNames;
        public Line[] Rivers, Ridges, Plateaus;
        public double[] Lakes;                       // x, y, r 三个一组
        public Dictionary<string, Line[]> Biomes = new Dictionary<string, Line[]>();
        public double[][] ChinaOld, EastAsia;
    }

    public static class WorldGeo
    {
        // float 常数还原成十进制写法的 double（2.2f → 2.2），与 JS 的数值一致
        public static double D(float f) { return (double)(decimal)f; }

        public static readonly double X0, Y0, X1, Y1, W, H;
        public static readonly MapRect China = new MapRect(WorldGeoData.ChinaXMin, WorldGeoData.ChinaYMin, WorldGeoData.ChinaXMax, WorldGeoData.ChinaYMax);
        public static readonly MapRect Bounds;

        static WorldGeo()
        {
            double x, y, yw, yc;
            MapProjection.Project(D(WorldGeoData.Lon0), 0, out x, out y); X0 = x;
            MapProjection.Project(D(WorldGeoData.Lon1), 0, out x, out y); X1 = x;
            // 西方剖面（经度 ≤ 62°）与中国剖面（经度 ≥ 97°）各取一次
            MapProjection.Project(0, D(WorldGeoData.Lat0), out x, out yw); MapProjection.Project(100, D(WorldGeoData.Lat0), out x, out yc); Y0 = Math.Min(yw, yc);
            MapProjection.Project(0, D(WorldGeoData.Lat1), out x, out yw); MapProjection.Project(100, D(WorldGeoData.Lat1), out x, out yc); Y1 = Math.Max(yw, yc);
            W = X1 - X0; H = Y1 - Y0;
            Bounds = new MapRect((float)X0, (float)Y0, (float)X1, (float)Y1);
        }

        public static Vector2 Project(double lon, double lat) { double x, y; MapProjection.Project(lon, lat, out x, out y); return new Vector2((float)x, (float)y); }
        public static Vector2 Unproject(double x, double y) { double lo, la; MapProjection.Unproject(x, y, out lo, out la); return new Vector2((float)lo, (float)la); }
        // 城池的地图坐标（经纬度先还原成十进制 double，与网页版 SG.mapPos 一致）
        public static void CityPos(City c, out double x, out double y) { MapProjection.Project(D(c.lon), D(c.lat), out x, out y); }

        // 经纬度方框 → 地图坐标外接矩形（投影非可分离，沿上下两边取样）
        public static MapRect RegionRect(double lon0, double lon1, double lat0, double lat1)
        {
            double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity, xMax = double.NegativeInfinity, yMax = double.NegativeInfinity;
            const int N = 16;
            for (int i = 0; i <= N; i++)
            {
                double lo = lon0 + (lon1 - lon0) * i / N;
                for (int k = 0; k < 2; k++)
                {
                    double x, y;
                    MapProjection.Project(lo, k == 0 ? lat0 : lat1, out x, out y);
                    xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x); yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y);
                }
            }
            return new MapRect((float)xMin, (float)yMin, (float)xMax, (float)yMax);
        }

        static Dictionary<string, WorldRegion> regions;
        public static Dictionary<string, WorldRegion> Regions
        {
            get
            {
                if (regions != null) return regions;
                var d = new Dictionary<string, WorldRegion>();
                foreach (var r in WorldGeoData.Regions)
                {
                    var reg = new WorldRegion { Key = r.Key, Name = r.Name, Lon0 = r.Lon0, Lon1 = r.Lon1, Lat0 = r.Lat0, Lat1 = r.Lat1 };
                    reg.Rect = r.Key == "china" ? China : r.Key == "world" ? Bounds : RegionRect(D(r.Lon0), D(r.Lon1), D(r.Lat0), D(r.Lat1));
                    d[r.Key] = reg;
                }
                regions = d;
                return d;
            }
        }
        // 地区键 → 地图矩形（未知键返回 false）
        public static bool TryRegionRect(string key, out MapRect rect)
        {
            WorldRegion r;
            if (key != null && Regions.TryGetValue(key, out r)) { rect = r.Rect; return true; }
            rect = default(MapRect); return false;
        }

        // ------------------------------------------------------------- 投影缓存 --
        static GeoProjected data;
        public static GeoProjected Data { get { if (data == null) data = BuildData(); return data; } }

        static double[] ProjRing(List<double> pts)
        {
            int n = pts.Count / 2; var o = new double[n * 2];
            for (int i = 0; i < n; i++) { double x, y; MapProjection.Project(pts[2 * i], pts[2 * i + 1], out x, out y); o[2 * i] = x; o[2 * i + 1] = y; }
            return o;
        }
        // 长边按 ≤ maxDeg 细分后再投影（闭合环）
        static double[] ProjRingDense(float[] pts, double maxDeg)
        {
            var dense = new List<double>();
            int n = pts.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double a0 = D(pts[2 * i]), a1 = D(pts[2 * i + 1]), b0 = D(pts[2 * j]), b1 = D(pts[2 * j + 1]);
                int k = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b0 - a0), Math.Abs(b1 - a1)) / maxDeg));
                for (int s = 0; s < k; s++) { dense.Add(a0 + (b0 - a0) * s / k); dense.Add(a1 + (b1 - a1) * s / k); }
            }
            return ProjRing(dense);
        }
        // 折线（不闭合）
        static double[] ProjLine(float[] pts, double maxDeg)
        {
            var dense = new List<double>();
            int n = pts.Length / 2;
            for (int i = 0; i < n - 1; i++)
            {
                double a0 = D(pts[2 * i]), a1 = D(pts[2 * i + 1]), b0 = D(pts[2 * i + 2]), b1 = D(pts[2 * i + 3]);
                int k = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b0 - a0), Math.Abs(b1 - a1)) / maxDeg));
                for (int s = 0; s < k; s++) { dense.Add(a0 + (b0 - a0) * s / k); dense.Add(a1 + (b1 - a1) * s / k); }
            }
            dense.Add(D(pts[2 * n - 2])); dense.Add(D(pts[2 * n - 1]));
            return ProjRing(dense);
        }

        static GeoProjected BuildData()
        {
            var P = new GeoProjected();
            var land = WorldGeoData.Land;
            P.Land = new double[land.Length][]; P.LandNames = new string[land.Length];
            for (int i = 0; i < land.Length; i++) { P.Land[i] = ProjRingDense(land[i].Pts, 2); P.LandNames[i] = land[i].Name; }
            var rv = WorldGeoData.Rivers;
            P.Rivers = new GeoProjected.Line[rv.Length];
            for (int i = 0; i < rv.Length; i++) P.Rivers[i] = new GeoProjected.Line { Name = rv[i].Name, W = rv[i].W, D = rv[i].D, Xy = ProjLine(rv[i].Pts, 1.5) };
            var lk = WorldGeoData.Lakes;
            P.Lakes = new double[lk.Length];
            for (int i = 0; i + 2 < lk.Length; i += 3)
            {
                double x, y; MapProjection.Project(D(lk[i]), D(lk[i + 1]), out x, out y);
                P.Lakes[i] = x; P.Lakes[i + 1] = y; P.Lakes[i + 2] = D(lk[i + 2]);
            }
            var rg = WorldGeoData.Ridges;
            P.Ridges = new GeoProjected.Line[rg.Length];
            for (int i = 0; i < rg.Length; i++) P.Ridges[i] = new GeoProjected.Line { Name = rg[i].Name, A = D(rg[i].A), B = D(rg[i].B), Wd = D(rg[i].W), Xy = ProjLine(rg[i].Pts, 2) };
            var pl = WorldGeoData.Plateaus;
            P.Plateaus = new GeoProjected.Line[pl.Length];
            for (int i = 0; i < pl.Length; i++) P.Plateaus[i] = new GeoProjected.Line { Name = pl[i].Name, H = D(pl[i].H), Xy = ProjRingDense(pl[i].Pts, 2) };
            foreach (var key in WorldGeoData.BiomeKeys)
            {
                var b = WorldGeoData.Biome(key) ?? new GeoBiome[0];
                var arr = new GeoProjected.Line[b.Length];
                for (int i = 0; i < b.Length; i++) arr[i] = new GeoProjected.Line { Name = b[i].Name, V = D(b[i].V), Xy = ProjRingDense(b[i].Pts, 2) };
                P.Biomes[key] = arr;
            }
            P.ChinaOld = new double[WorldGeoData.ChinaOld.Length][];
            for (int i = 0; i < P.ChinaOld.Length; i++) P.ChinaOld[i] = ProjRingDense(WorldGeoData.ChinaOld[i], 1);
            P.EastAsia = new double[WorldGeoData.EastAsia.Length][];
            for (int i = 0; i < P.EastAsia.Length; i++) P.EastAsia[i] = ProjRingDense(WorldGeoData.EastAsia[i], 1);
            return P;
        }

        // ------------------------------------------------------------- 数学小工具 --
        public static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        public static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * Clamp01(t); }
        public static double InvLerp(double a, double b, double v) { return a == b ? 0 : Clamp01((v - a) / (b - a)); }
        public static double Smooth(double from, double to, double t) { t = Clamp01(t); t = -2 * t * t * t + 3 * t * t; return to * t + from * (1 - t); }
        // JS Math.round（.5 向上取整）
        public static int JsRound(double v) { return (int)Math.Floor(v + 0.5); }
        // 0–1 → 0–255（四舍五入并截断；网页版 b8）
        public static byte B8(double v) { return v <= 0 ? (byte)0 : v >= 1 ? (byte)255 : (byte)JsRound(v * 255); }
        public static double SegDist(double px, double py, double ax, double ay, double bx, double by)
        {
            double abx = bx - ax, aby = by - ay;
            double t = Clamp01(((px - ax) * abx + (py - ay) * aby) / Math.Max(1e-4, abx * abx + aby * aby));
            double dx = px - (ax + abx * t), dy = py - (ay + aby * t);
            return Math.Sqrt(dx * dx + dy * dy);
        }
        public static double Fbm(double x, double y, int oct = 4)
        {
            double s = 0, a = 0.5, f = 1, n = 0;
            for (int i = 0; i < oct; i++) { s += a * Perlin(x * f + 31.7 * i, y * f + 17.3 * i); n += a; a *= 0.5; f *= 2.03; }
            return s / n;
        }

        // ------------------------------------------------------------- Perlin 噪声 --
        // 网页版 core.js 的 SG.M.perlinNoise（与 Unity Mathf.PerlinNoise 同尺度：(n + 0.69) / 1.483，整数坐标处 0.4652731），
        // 输入输出都是 double：地图在 Unity 里与网页版逐点相同（Unity 自带的 Mathf.PerlinNoise 是另一种实现）
        static readonly byte[] perm = BuildPerm();
        static byte[] BuildPerm()
        {
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            long s = 1337;
            for (int i = 255; i > 0; i--)
            {
                s = (s * 16807) % 2147483647;
                int j = (int)(s % (i + 1));
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            var o = new byte[512];
            for (int i = 0; i < 512; i++) o[i] = (byte)p[i & 255];
            return o;
        }
        static double Fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        static double Grad(int h, double x, double y)
        {
            switch (h & 7)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x; case 5: return -x; case 6: return y; default: return -y;
            }
        }
        public static double Perlin(double x, double y)
        {
            double fxi = Math.Floor(x), fyi = Math.Floor(y);
            int X = (int)((long)fxi & 255), Y = (int)((long)fyi & 255);
            double xf = x - fxi, yf = y - fyi;
            double u = Fade(xf), v = Fade(yf);
            int aa = perm[perm[X] + Y], ab = perm[perm[X] + Y + 1], ba = perm[perm[X + 1] + Y], bb = perm[perm[X + 1] + Y + 1];
            double x1 = Grad(aa, xf, yf) + (Grad(ba, xf - 1, yf) - Grad(aa, xf, yf)) * u;
            double x2 = Grad(ab, xf, yf - 1) + (Grad(bb, xf - 1, yf - 1) - Grad(ab, xf, yf - 1)) * u;
            double n = x1 + (x2 - x1) * v;
            return (n + 0.69) / 1.483;
        }

        // ------------------------------------------------------------- 文化回退 --
        // 网页版 core.js 的 SG.cultureBase：某模块没有某文化的专门外观时用它取回退文化。
        // 唯一的回退表在 Defs.CultureBase（由导出工具从 core.js 生成），这里只是转发，不另存一份
        public static string CultureBase(string code) { return Defs.CultureBase(code); }
    }

    // ================================================================ 构建范围 ==
    public sealed class MapBox { public double x0, z0, x1, z1, step, band, far; public int chunk, tile; }
    public sealed class MapSpec
    {
        public string Key;              // "china" | "world"
        public MapRect Bounds;          // 可玩范围（镜头范围）
        public MapBox Terrain, Skirt, Water, Trees;
        public double Tile, Fine, Coarse, FieldRes;
        public int Clouds;

        // 'china'：旧版中国地图（各范围与旧版完全相同）
        public static MapSpec Classic()
        {
            const double W = 112, H = 100;   // MapView.MapW / MapH
            return new MapSpec
            {
                Key = "china", Bounds = new MapRect(0, 0, (float)W, (float)H),
                Terrain = new MapBox { x0 = -26, z0 = -24, x1 = W + 26, z1 = H + 22, step = 1.25, chunk = 32 },
                Skirt = new MapBox { x0 = -210, z0 = -210, x1 = W + 210, z1 = H + 210, step = 5, tile = 36 },
                Water = new MapBox { x0 = -60, z0 = -60, x1 = W + 60, z1 = H + 60, step = 2.5, tile = 24, band = 20, far = 300 },
                Trees = new MapBox { x0 = -10, z0 = -6, x1 = W + 10, z1 = H + 6 },
                Tile = 80, Fine = 1.0, Coarse = 4, FieldRes = 2, Clouds = 9,
            };
        }
        // 'world'：整个欧亚大陆
        public static MapSpec World()
        {
            double x0 = Math.Floor(WorldGeo.X0 / 5) * 5, x1 = Math.Ceiling(WorldGeo.X1 / 5) * 5, z0 = Math.Floor(WorldGeo.Y0 / 5) * 5, z1 = Math.Ceiling(WorldGeo.Y1 / 5) * 5;
            return new MapSpec
            {
                Key = "world", Bounds = WorldGeo.Bounds,
                Terrain = new MapBox { x0 = x0 - 40, z0 = z0 - 40, x1 = x1 + 40, z1 = z1 + 40, step = 1.25, chunk = 40 },
                Skirt = new MapBox { x0 = x0 - 230, z0 = z0 - 230, x1 = x1 + 230, z1 = z1 + 230, step = 6, tile = 30 },
                Water = new MapBox { x0 = x0 - 60, z0 = z0 - 60, x1 = x1 + 60, z1 = z1 + 60, step = 2.5, tile = 40, band = 20, far = 300 },
                Trees = new MapBox { x0 = x0 - 8, z0 = z0 - 8, x1 = x1 + 8, z1 = z1 + 8 },
                Tile = 100, Fine = 1.0, Coarse = 4, FieldRes = 2, Clouds = 18,
            };
        }
        public static MapSpec ForRegion(string region) { return region == "world" ? World() : Classic(); }
        // 区域判定（= 网页版 map-view.js 的 _spec）："china" / "world" 原样返回；其余（"auto"、空）→
        // 所有城池都在旧版中国地图（112 × 100）外扩 20 之内时为 china，否则 world
        public static string Resolve(GameState g, string region)
        {
            if (region == "world" || region == "china") return region;
            const double W = 112, H = 100;   // MapView.MapW / MapH
            if (g != null && g.cities != null)
                foreach (var c in g.cities)
                {
                    double x, y; WorldGeo.CityPos(c, out x, out y);
                    if (x < -20 || x > W + 20 || y < -20 || y > H + 20) return "world";
                }
            return "china";
        }
    }

    // ===================================================== 地理场（栅格化）==
    // 1) 点在陆地内：投影后的陆地环按行分桶，精确的奇偶规则交点计数
    public sealed class EdgeIndex
    {
        readonly double[] E; readonly double band, y0; readonly int nb; readonly int[] start, idx;
        public EdgeIndex(double[][] rings, double band)
        {
            int ne = 0;
            foreach (var r in rings) ne += r.Length / 2;
            E = new double[ne * 4];
            int k = 0; double ya = double.PositiveInfinity, yb = double.NegativeInfinity;
            foreach (var r in rings)
            {
                int n = r.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    E[k++] = r[2 * i]; E[k++] = r[2 * i + 1]; E[k++] = r[2 * j]; E[k++] = r[2 * j + 1];
                    ya = Math.Min(ya, r[2 * i + 1]); yb = Math.Max(yb, r[2 * i + 1]);
                }
            }
            this.band = band;
            y0 = Math.Floor(ya) - 1;
            nb = (int)Math.Ceiling((yb + 1 - y0) / band) + 1;
            var cnt = new int[nb + 1];
            for (int e = 0; e < ne; e++) for (int b = Lo(e); b <= Hi(e); b++) cnt[b + 1]++;
            for (int b = 0; b < nb; b++) cnt[b + 1] += cnt[b];
            idx = new int[cnt[nb]];
            var fill = new int[nb];
            Array.Copy(cnt, fill, nb);
            for (int e = 0; e < ne; e++) for (int b = Lo(e); b <= Hi(e); b++) idx[fill[b]++] = e;
            start = cnt;
        }
        int Lo(int e) { return (int)Math.Floor((Math.Min(E[e * 4 + 1], E[e * 4 + 3]) - y0) / band); }
        int Hi(int e) { return (int)Math.Floor((Math.Max(E[e * 4 + 1], E[e * 4 + 3]) - y0) / band); }
        public bool Inside(double x, double y)
        {
            int b = (int)Math.Floor((y - y0) / band);
            if (b < 0 || b >= nb) return false;
            bool c = false;
            for (int p = start[b], q = start[b + 1]; p < q; p++)
            {
                int e = idx[p] * 4;
                double yi = E[e + 1], yj = E[e + 3];
                if ((yi > y) != (yj > y))
                {
                    double xi = E[e], xj = E[e + 2];
                    if (x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
                }
            }
            return c;
        }
    }

    // 2) 最近要素栅格（线段 / 圆）：在节点上记下最近的要素编号（种子 + 四遍扫描传播），
    //    查询时取周围四个节点各自的最近要素，精确计算点到要素的距离。
    //    要素 F：每个 5 个数 ax, ay, bx, by, r（r > 0 为圆，a = b = 圆心）
    public sealed class FeatureGrid
    {
        readonly double[] F; readonly int nf; readonly double res, x0, z0;
        readonly int nx, nz;
        int[] feat;
        public int Last = -1;
        public FeatureGrid(double[] F, int nf, double x0, double z0, double x1, double z1, double res)
        {
            this.F = F; this.nf = nf; this.res = res; this.x0 = x0; this.z0 = z0;
            nx = (int)Math.Ceiling((x1 - x0) / res) + 1; nz = (int)Math.Ceiling((z1 - z0) / res) + 1;
        }
        public static double FeatDist(double[] F, int k, double px, double py)
        {
            int o = k * 5;
            double ax = F[o], ay = F[o + 1], abx = F[o + 2] - ax, aby = F[o + 3] - ay;
            double L = abx * abx + aby * aby;
            double t = L > 1e-12 ? ((px - ax) * abx + (py - ay) * aby) / Math.Max(1e-4, L) : 0;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            double dx = px - (ax + abx * t), dy = py - (ay + aby * t);
            double d = Math.Sqrt(dx * dx + dy * dy) - F[o + 4];
            return d > 0 ? d : 0;
        }
        float[] dist;
        void Touch(int k, int i, int j)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz) return;
            int o = j * nx + i;
            double d = FeatDist(F, k, x0 + i * res, z0 + j * res);
            if (d < dist[o]) { dist[o] = (float)d; feat[o] = k; }
        }
        void Relax(int o, int q, double px, double py)
        {
            int k = feat[q];
            if (k < 0 || k == feat[o]) return;
            double d = FeatDist(F, k, px, py);
            if (d < dist[o]) { dist[o] = (float)d; feat[o] = k; }
        }
        public IEnumerator Build()
        {
            int N = nx * nz;
            feat = new int[N]; dist = new float[N];
            for (int o = 0; o < N; o++) { feat[o] = -1; dist[o] = float.PositiveInfinity; }
            double xm0 = x0 - 3 * res, zm0 = z0 - 3 * res, xm1 = x0 + (nx + 2) * res, zm1 = z0 + (nz + 2) * res;
            // 种子：沿每个要素取样，写入周围 3×3 个节点
            for (int k = 0; k < nf; k++)
            {
                int o = k * 5;
                double ax = F[o], ay = F[o + 1], bx = F[o + 2], by = F[o + 3], r = F[o + 4];
                bool skip = Math.Max(ax, bx) + r < xm0 || Math.Min(ax, bx) - r > xm1 || Math.Max(ay, by) + r < zm0 || Math.Min(ay, by) - r > zm1;
                if (!skip)
                {
                    if (r > 0)
                    {
                        double R = r + 1.5 * res;
                        int i0 = (int)Math.Floor((ax - R - x0) / res), i1 = (int)Math.Ceiling((ax + R - x0) / res);
                        int j0 = (int)Math.Floor((ay - R - z0) / res), j1 = (int)Math.Ceiling((ay + R - z0) / res);
                        for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) Touch(k, i, j);
                    }
                    else
                    {
                        double L = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                        int n = Math.Max(1, (int)Math.Ceiling(L / (res * 0.5)));
                        for (int s = 0; s <= n; s++)
                        {
                            double px = ax + (bx - ax) * s / n, py = ay + (by - ay) * s / n;
                            int ci = WorldGeo.JsRound((px - x0) / res), cj = WorldGeo.JsRound((py - z0) / res);
                            if (ci < -2 || cj < -2 || ci > nx + 1 || cj > nz + 1) continue;
                            for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++) Touch(k, ci + di, cj + dj);
                        }
                    }
                }
                if ((k & 63) == 63) yield return null;
            }
            // 传播：上→下（左→右、右→左），下→上（右→左、左→右）
            for (int j = 0; j < nz; j++)
            {
                double py = z0 + j * res; int row = j * nx;
                for (int i = 0; i < nx; i++)
                {
                    int o = row + i; double px = x0 + i * res;
                    if (i > 0) Relax(o, o - 1, px, py);
                    if (j > 0)
                    {
                        Relax(o, o - nx, px, py);
                        if (i > 0) Relax(o, o - nx - 1, px, py);
                        if (i < nx - 1) Relax(o, o - nx + 1, px, py);
                    }
                }
                for (int i = nx - 2; i >= 0; i--) Relax(row + i, row + i + 1, x0 + i * res, py);
                if ((j & 1) == 1) yield return null;
            }
            for (int j = nz - 1; j >= 0; j--)
            {
                double py = z0 + j * res; int row = j * nx;
                for (int i = nx - 1; i >= 0; i--)
                {
                    int o = row + i; double px = x0 + i * res;
                    if (i < nx - 1) Relax(o, o + 1, px, py);
                    if (j < nz - 1)
                    {
                        Relax(o, o + nx, px, py);
                        if (i < nx - 1) Relax(o, o + nx + 1, px, py);
                        if (i > 0) Relax(o, o + nx - 1, px, py);
                    }
                }
                for (int i = 1; i < nx; i++) Relax(row + i, row + i - 1, x0 + i * res, py);
                if ((j & 1) == 1) yield return null;
            }
            dist = null;
        }
        public bool Covers(double x, double z)
        {
            return x >= x0 && z >= z0 && x <= x0 + (nx - 1) * res && z <= z0 + (nz - 1) * res;
        }
        // 点到最近要素的距离（无要素时 999）；Last = 最近要素编号
        public double Query(double x, double z)
        {
            int i = (int)Math.Floor((x - x0) / res), j = (int)Math.Floor((z - z0) / res);
            if (i < 0) i = 0; else if (i > nx - 2) i = nx - 2;
            if (j < 0) j = 0; else if (j > nz - 2) j = nz - 2;
            int o = j * nx + i;
            int k0 = feat[o], k1 = feat[o + 1], k2 = feat[o + nx], k3 = feat[o + nx + 1];
            double best = 999, d; int bk = -1;
            if (k0 >= 0) { d = FeatDist(F, k0, x, z); if (d < best) { best = d; bk = k0; } }
            if (k1 >= 0 && k1 != k0) { d = FeatDist(F, k1, x, z); if (d < best) { best = d; bk = k1; } }
            if (k2 >= 0 && k2 != k0 && k2 != k1) { d = FeatDist(F, k2, x, z); if (d < best) { best = d; bk = k2; } }
            if (k3 >= 0 && k3 != k0 && k3 != k1 && k3 != k2) { d = FeatDist(F, k3, x, z); if (d < best) { best = d; bk = k3; } }
            Last = bk;
            return best;
        }
    }

    // 3) 山脉：桶索引（每条山脉取到各段的最近距离，exp(−d²/w) 衰减，与旧版公式相同）
    public sealed class RidgeIndex
    {
        readonly GeoProjected.Line[] ridges; readonly double cell, x0, z0; readonly int nx, nz;
        readonly List<int>[] b;
        public RidgeIndex(GeoProjected.Line[] ridges, double cell)
        {
            this.ridges = ridges; this.cell = cell;
            double xa = double.PositiveInfinity, za = double.PositiveInfinity, xb = double.NegativeInfinity, zb = double.NegativeInfinity;
            var R = new double[ridges.Length];
            for (int id = 0; id < ridges.Length; id++)
            {
                R[id] = Math.Sqrt(ridges[id].Wd * Math.Log(1e7)) + 0.5;
                var xy = ridges[id].Xy;
                for (int i = 0; i < xy.Length; i += 2)
                {
                    xa = Math.Min(xa, xy[i] - R[id]); xb = Math.Max(xb, xy[i] + R[id]);
                    za = Math.Min(za, xy[i + 1] - R[id]); zb = Math.Max(zb, xy[i + 1] + R[id]);
                }
            }
            x0 = xa; z0 = za;
            nx = Math.Max(1, (int)Math.Ceiling((xb - xa) / cell)); nz = Math.Max(1, (int)Math.Ceiling((zb - za) / cell));
            b = new List<int>[nx * nz];
            for (int id = 0; id < ridges.Length; id++)
            {
                var seen = new HashSet<int>();
                var xy = ridges[id].Xy;
                for (int s = 0; s + 3 < xy.Length; s += 2)
                {
                    double ax = xy[s], ay = xy[s + 1], bx = xy[s + 2], by = xy[s + 3];
                    int i0 = (int)Math.Floor((Math.Min(ax, bx) - R[id] - x0) / cell), i1 = (int)Math.Floor((Math.Max(ax, bx) + R[id] - x0) / cell);
                    int j0 = (int)Math.Floor((Math.Min(ay, by) - R[id] - z0) / cell), j1 = (int)Math.Floor((Math.Max(ay, by) + R[id] - z0) / cell);
                    for (int i = Math.Max(0, i0); i <= Math.Min(nx - 1, i1); i++)
                        for (int j = Math.Max(0, j0); j <= Math.Min(nz - 1, j1); j++)
                        {
                            int o = j * nx + i;
                            if (!seen.Add(o)) continue;
                            (b[o] ?? (b[o] = new List<int>())).Add(id);
                        }
                }
            }
            foreach (var l in b) if (l != null) l.Sort();
        }
        public double Sum(double x, double z)
        {
            int i = (int)Math.Floor((x - x0) / cell), j = (int)Math.Floor((z - z0) / cell);
            if (i < 0 || j < 0 || i >= nx || j >= nz) return 0;
            var list = b[j * nx + i];
            if (list == null) return 0;
            double h = 0, rf = -1;
            for (int q = 0; q < list.Count; q++)
            {
                var r = ridges[list[q]]; var xy = r.Xy;
                double d2 = double.PositiveInfinity;
                for (int s = 0; s + 3 < xy.Length; s += 2)
                {
                    double d = WorldGeo.SegDist(x, z, xy[s], xy[s + 1], xy[s + 2], xy[s + 3]);
                    if (d * d < d2) d2 = d * d;
                }
                double k = Math.Exp(-d2 / r.Wd);
                if (k < 1e-7) continue;
                if (rf < 0) rf = WorldGeo.Fbm(x * 0.2, z * 0.2, 2);
                h += k * (r.A + rf * r.B);
            }
            return h;
        }
    }

    // 4) 平滑场（高原、生物群落、旧版模型权重）+ 以上各索引 = 一个构建范围的地理场（网页版 Geo）
    public sealed class GeoFields
    {
        public const int F_OLD = 0, F_EAST = 1, F_PLAT = 2, F_DESERT = 3, F_STEPPE = 4, F_TROP = 5, F_SAV = 6, F_MED = 7, F_BOREAL = 8, NF = 9;

        public readonly MapSpec Spec;
        public readonly bool World;
        public bool Ready;
        public float rW = 1, rD = -0.35f;     // RiverDist 最近要素的宽度系数、河床深度
        readonly double fbx0, fbz0, fbx1, fbz1, cbx0, cbz0, cbx1, cbz1;
        EdgeIndex land;
        FeatureGrid coastFine, coastCoarse, riverFine, riverCoarse;
        float[] RW, RD;
        RidgeIndex ridges;
        float[][] fields;
        double fx0, fz0, fres; int fnx, fnz;
        readonly float[] f = new float[NF];

        static EdgeIndex landIndex;
        static readonly Dictionary<string, GeoFields> cache = new Dictionary<string, GeoFields>();

        public GeoFields(MapSpec spec)
        {
            Spec = spec;
            const double pad = 4;
            var Wt = spec.Water; var Sk = spec.Skirt;
            fbx0 = Wt.x0 - pad; fbz0 = Wt.z0 - pad; fbx1 = Wt.x1 + pad; fbz1 = Wt.z1 + pad;
            cbx0 = Sk.x0 - 8; cbz0 = Sk.z0 - 8; cbx1 = Sk.x1 + 8; cbz1 = Sk.z1 + 8;
            World = spec.Key == "world";
        }

        // 按构建范围缓存（标题背景与正式开局共用）；构建完成后 holder[0] 为结果
        public static IEnumerator For(MapSpec spec, GeoFields[] holder)
        {
            GeoFields g;
            if (!cache.TryGetValue(spec.Key, out g) || !g.Ready)
            {
                g = new GeoFields(spec);
                yield return g.Build();
                cache[spec.Key] = g;
            }
            holder[0] = g;
        }
        // 同步取得（测试用）
        public static GeoFields Get(MapSpec spec)
        {
            var h = new GeoFields[1];
            Run(For(spec, h));
            return h[0];
        }
        // 把嵌套的 IEnumerator 一次跑完
        public static void Run(IEnumerator it)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(it);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                var sub = top.Current as IEnumerator;
                if (sub != null) stack.Push(sub);
            }
        }

        IEnumerator Build()
        {
            var P = WorldGeo.Data;
            if (landIndex == null) { yield return null; landIndex = new EdgeIndex(P.Land, 2); }
            land = landIndex;
            yield return null;
            // 海岸要素：所有陆地环的边
            int n = 0, k = 0;
            foreach (var r in P.Land) n += r.Length / 2;
            var CF = new double[n * 5];
            foreach (var r in P.Land)
            {
                int m = r.Length / 2;
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m;
                    CF[k++] = r[2 * i]; CF[k++] = r[2 * i + 1]; CF[k++] = r[2 * j]; CF[k++] = r[2 * j + 1]; CF[k++] = 0;
                }
            }
            coastFine = new FeatureGrid(CF, n, fbx0, fbz0, fbx1, fbz1, Spec.Fine);
            yield return coastFine.Build();
            coastCoarse = new FeatureGrid(CF, n, cbx0, cbz0, cbx1, cbz1, Spec.Coarse);
            yield return coastCoarse.Build();
            // 河流要素：河段 + 湖（圆）；附带每个要素的宽度、深度
            int nr = P.Lakes.Length / 3;
            foreach (var r in P.Rivers) nr += r.Xy.Length / 2 - 1;
            var RF = new double[nr * 5]; RW = new float[nr]; RD = new float[nr];
            k = 0; int q = 0;
            foreach (var r in P.Rivers)
                for (int i = 0; i + 3 < r.Xy.Length; i += 2)
                {
                    RF[k++] = r.Xy[i]; RF[k++] = r.Xy[i + 1]; RF[k++] = r.Xy[i + 2]; RF[k++] = r.Xy[i + 3]; RF[k++] = 0;
                    RW[q] = r.W; RD[q] = r.D; q++;
                }
            for (int i = 0; i + 2 < P.Lakes.Length; i += 3)
            {
                RF[k++] = P.Lakes[i]; RF[k++] = P.Lakes[i + 1]; RF[k++] = P.Lakes[i]; RF[k++] = P.Lakes[i + 1]; RF[k++] = P.Lakes[i + 2];
                RW[q] = 1; RD[q] = -0.35f; q++;
            }
            riverFine = new FeatureGrid(RF, nr, fbx0, fbz0, fbx1, fbz1, Spec.Fine);
            yield return riverFine.Build();
            riverCoarse = new FeatureGrid(RF, nr, cbx0, cbz0, cbx1, cbz1, Spec.Coarse);
            yield return riverCoarse.Build();
            ridges = new RidgeIndex(P.Ridges, 8);
            yield return null;
            yield return BuildFields(P);
            Ready = true;
        }

        static void FillRing(float[] arr, int nx, int nz, double x0, double z0, double res, double[] ring, double value)
        {
            int n = ring.Length / 2;
            double ymin = double.PositiveInfinity, ymax = double.NegativeInfinity;
            for (int i = 0; i < n; i++) { ymin = Math.Min(ymin, ring[2 * i + 1]); ymax = Math.Max(ymax, ring[2 * i + 1]); }
            int j0 = Math.Max(0, (int)Math.Ceiling((ymin - z0) / res)), j1 = Math.Min(nz - 1, (int)Math.Floor((ymax - z0) / res));
            var xs = new List<double>();
            for (int j = j0; j <= j1; j++)
            {
                double y = z0 + j * res;
                xs.Clear();
                for (int a = 0, b = n - 1; a < n; b = a++)
                {
                    double ya = ring[2 * a + 1], yb = ring[2 * b + 1];
                    if ((ya > y) != (yb > y)) xs.Add(ring[2 * a] + (y - ya) * (ring[2 * b] - ring[2 * a]) / (yb - ya));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int i0 = Math.Max(0, (int)Math.Ceiling((xs[k] - x0) / res)), i1 = Math.Min(nx - 1, (int)Math.Floor((xs[k + 1] - x0) / res));
                    for (int i = i0; i <= i1; i++) { int o = j * nx + i; if (arr[o] < value) arr[o] = (float)value; }
                }
            }
        }
        // 三遍盒式模糊（横、纵各一遍为一轮）
        internal static IEnumerator BoxBlur(float[] arr, int nx, int nz, int r, int passes)
        {
            if (r <= 0) yield break;
            var tmp = new float[Math.Max(nx, nz)];
            double w = 2 * r + 1;
            for (int p = 0; p < passes; p++)
            {
                for (int j = 0; j < nz; j++)
                {
                    int row = j * nx;
                    double s = 0;
                    for (int k = -r; k <= r; k++) s += arr[row + Math.Min(nx - 1, Math.Max(0, k))];
                    for (int i = 0; i < nx; i++)
                    {
                        tmp[i] = (float)(s / w);
                        s += arr[row + Math.Min(nx - 1, i + r + 1)] - (double)arr[row + Math.Max(0, i - r)];
                    }
                    for (int i = 0; i < nx; i++) arr[row + i] = tmp[i];
                    if ((j & 31) == 31) yield return null;
                }
                for (int i = 0; i < nx; i++)
                {
                    double s = 0;
                    for (int k = -r; k <= r; k++) s += arr[Math.Min(nz - 1, Math.Max(0, k)) * nx + i];
                    for (int j = 0; j < nz; j++)
                    {
                        tmp[j] = (float)(s / w);
                        s += arr[Math.Min(nz - 1, j + r + 1) * nx + i] - (double)arr[Math.Max(0, j - r) * nx + i];
                    }
                    for (int j = 0; j < nz; j++) arr[j * nx + i] = tmp[j];
                    if ((i & 31) == 31) yield return null;
                }
            }
        }

        IEnumerator BuildFields(GeoProjected P)
        {
            double res = Spec.FieldRes;
            int nx = (int)Math.Ceiling((cbx1 - cbx0) / res) + 1, nz = (int)Math.Ceiling((cbz1 - cbz0) / res) + 1;
            fx0 = cbx0; fz0 = cbz0; fres = res; fnx = nx; fnz = nz;
            fields = new float[NF][];
            for (int i = 0; i < NF; i++) fields[i] = new float[nx * nz];
            Func<double, int> cells = u => Math.Max(1, WorldGeo.JsRound(u / res));
            // 旧版模型权重
            foreach (var r in P.ChinaOld) FillRing(fields[F_OLD], nx, nz, cbx0, cbz0, res, r, 1);
            yield return BoxBlur(fields[F_OLD], nx, nz, cells(4), 3);
            // 东亚干旱色权重（日本减半）
            foreach (var r in P.EastAsia) FillRing(fields[F_EAST], nx, nz, cbx0, cbz0, res, r, 1);
            double japanX; double jy; MapProjection.Project(129.8, 35, out japanX, out jy);
            var east = fields[F_EAST];
            for (int i = 0; i < nx; i++) if (cbx0 + i * res > japanX) for (int j = 0; j < nz; j++) east[j * nx + i] = (float)(east[j * nx + i] * 0.45);
            yield return BoxBlur(east, nx, nz, cells(6), 3);
            // 高原（取各高原高度的最大值）
            foreach (var p in P.Plateaus) FillRing(fields[F_PLAT], nx, nz, cbx0, cbz0, res, p.Xy, p.H);
            yield return null;
            yield return BoxBlur(fields[F_PLAT], nx, nz, cells(4), 3);
            // 生物群落
            var blurs = new[] { new object[] { F_DESERT, "desert", 4.0 }, new object[] { F_STEPPE, "steppe", 4.0 }, new object[] { F_TROP, "tropical", 6.0 },
                new object[] { F_SAV, "savanna", 6.0 }, new object[] { F_MED, "med", 4.0 } };
            foreach (var bl in blurs)
            {
                int fi = (int)bl[0];
                GeoProjected.Line[] rings;
                if (P.Biomes.TryGetValue((string)bl[1], out rings))
                    foreach (var r in rings) FillRing(fields[fi], nx, nz, cbx0, cbz0, res, r.Xy, r.V > 0 ? r.V : 1);
                yield return null;
                yield return BoxBlur(fields[fi], nx, nz, cells((double)bl[2]), 3);
            }
            // 北方针叶林：按纬度（斯堪的纳维亚、西欧更靠北才开始）
            double yA, yB, xd;
            MapProjection.Project(0, 48, out xd, out yA); MapProjection.Project(120, 48, out xd, out yB);
            double yMin = Math.Min(yA, yB);
            var bo = fields[F_BOREAL];
            for (int i = 0; i < nx; i++)
            {
                double x = cbx0 + i * res;
                for (int j = 0; j < nz; j++)
                {
                    double y = cbz0 + j * res;
                    if (y < yMin) continue;
                    double lon, lat; MapProjection.Unproject(x, y, out lon, out lat);
                    double thr = lon >= 25 ? 52 : lon <= 8 ? 58.5 : 58.5 - (lon - 8) / 17 * 6.5;
                    bo[j * nx + i] = (float)WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(thr, thr + 5, lat));
                }
                if ((i & 15) == 15) yield return null;
            }
        }

        public double CoastDist(double x, double z) { return coastFine.Covers(x, z) ? coastFine.Query(x, z) : coastCoarse.Query(x, z); }
        public bool InSea(double x, double z) { return !land.Inside(x, z); }
        // 到最近河流 / 湖泊的距离；rW / rD = 该河的宽度系数、河床深度
        public double RiverDist(double x, double z)
        {
            var g = riverFine.Covers(x, z) ? riverFine : riverCoarse;
            double d = g.Query(x, z);
            if (g.Last >= 0) { rW = RW[g.Last]; rD = RD[g.Last]; } else { rW = 1; rD = -0.35f; }
            return d;
        }
        public double RidgeSum(double x, double z) { return ridges.Sum(x, z); }
        // 双线性取样全部平滑场，写入共享数组并返回（调用方立即使用）
        public float[] Sample(double x, double z)
        {
            int nx = fnx, nz = fnz;
            double fx = (x - fx0) / fres, fz = (z - fz0) / fres;
            int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz);
            if (i < 0) { i = 0; fx = 0; } else if (i > nx - 2) { i = nx - 2; fx = nx - 1; }
            if (j < 0) { j = 0; fz = 0; } else if (j > nz - 2) { j = nz - 2; fz = nz - 1; }
            double tx = fx - i, tz = fz - j;
            int o = j * nx + i;
            double w00 = (1 - tx) * (1 - tz), w10 = tx * (1 - tz), w01 = (1 - tx) * tz, w11 = tx * tz;
            for (int k = 0; k < NF; k++)
            {
                var a = fields[k];
                f[k] = (float)(a[o] * w00 + a[o + 1] * w10 + a[o + nx] * w01 + a[o + nx + 1] * w11);
            }
            return f;
        }
    }

    // ================================================================ 小索引 ==
    // 点集的桶索引（网页版 makePointIndex）：At(x, z) → 半径 radius 内可能命中的点下标列表（或 null）
    public sealed class PointIndex
    {
        const double Cell = 4;
        readonly double x0, z0; readonly int nx, nz; readonly List<int>[] b;
        PointIndex(double x0, double z0, int nx, int nz) { this.x0 = x0; this.z0 = z0; this.nx = nx; this.nz = nz; b = new List<int>[nx * nz]; }
        public static PointIndex Make(IList<double> xs, IList<double> zs, double radius)
        {
            if (xs == null || xs.Count == 0) return null;
            double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity, x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
            for (int i = 0; i < xs.Count; i++) { x0 = Math.Min(x0, xs[i]); x1 = Math.Max(x1, xs[i]); z0 = Math.Min(z0, zs[i]); z1 = Math.Max(z1, zs[i]); }
            x0 -= radius + 1; z0 -= radius + 1; x1 += radius + 1; z1 += radius + 1;
            var p = new PointIndex(x0, z0, (int)Math.Ceiling((x1 - x0) / Cell) + 1, (int)Math.Ceiling((z1 - z0) / Cell) + 1);
            for (int i = 0; i < xs.Count; i++)
            {
                int i0 = (int)Math.Floor((xs[i] - radius - x0) / Cell), i1 = (int)Math.Floor((xs[i] + radius - x0) / Cell);
                int j0 = (int)Math.Floor((zs[i] - radius - z0) / Cell), j1 = (int)Math.Floor((zs[i] + radius - z0) / Cell);
                for (int a = i0; a <= i1; a++) for (int c = j0; c <= j1; c++) { int o = c * p.nx + a; (p.b[o] ?? (p.b[o] = new List<int>())).Add(i); }
            }
            return p;
        }
        public List<int> At(double x, double z)
        {
            int a = (int)Math.Floor((x - x0) / Cell), c = (int)Math.Floor((z - z0) / Cell);
            if (a < 0 || c < 0 || a >= nx || c >= nz) return null;
            return b[c * nx + a];
        }
    }
    // 线段集合的桶索引（网页版 makeSegIndex；每段 4 个数 ax, az, bx, bz）
    public sealed class SegIndex
    {
        const double Cell = 4;
        readonly double x0, z0; readonly int nx, nz; readonly List<int>[] b;
        SegIndex(double x0, double z0, int nx, int nz) { this.x0 = x0; this.z0 = z0; this.nx = nx; this.nz = nz; b = new List<int>[nx * nz]; }
        public static SegIndex Make(double[] S, double radius)
        {
            if (S == null || S.Length == 0) return null;
            double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity, x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
            for (int k = 0; k < S.Length; k += 4)
            {
                x0 = Math.Min(x0, Math.Min(S[k], S[k + 2])); x1 = Math.Max(x1, Math.Max(S[k], S[k + 2]));
                z0 = Math.Min(z0, Math.Min(S[k + 1], S[k + 3])); z1 = Math.Max(z1, Math.Max(S[k + 1], S[k + 3]));
            }
            x0 -= radius + 1; z0 -= radius + 1; x1 += radius + 1; z1 += radius + 1;
            var p = new SegIndex(x0, z0, (int)Math.Ceiling((x1 - x0) / Cell) + 1, (int)Math.Ceiling((z1 - z0) / Cell) + 1);
            for (int k = 0; k < S.Length; k += 4)
            {
                int i0 = (int)Math.Floor((Math.Min(S[k], S[k + 2]) - radius - x0) / Cell), i1 = (int)Math.Floor((Math.Max(S[k], S[k + 2]) + radius - x0) / Cell);
                int j0 = (int)Math.Floor((Math.Min(S[k + 1], S[k + 3]) - radius - z0) / Cell), j1 = (int)Math.Floor((Math.Max(S[k + 1], S[k + 3]) + radius - z0) / Cell);
                for (int a = i0; a <= i1; a++) for (int c = j0; c <= j1; c++) { int o = c * p.nx + a; (p.b[o] ?? (p.b[o] = new List<int>())).Add(k >> 2); }
            }
            return p;
        }
        public List<int> At(double x, double z)
        {
            int a = (int)Math.Floor((x - x0) / Cell), c = (int)Math.Floor((z - z0) / Cell);
            return a < 0 || c < 0 || a >= nx || c >= nz ? null : b[c * nx + a];
        }
    }

    // 地图坐标处的地形摘要（网页版 MapView.terrainInfo；第二阶段：战场地形取自城池周边）
    //   h 地表高度、slope（0 平 – 1 垂直）、water："sea" | "river" | "lake" | null、depth、forest、farm、sand、snow、
    //   rock（slope > 0.42 或 h > 4.2）、biome（temperate desert steppe tropical boreal med savanna alpine）、
    //   river（到河流的距离，按河宽归一）、coast（到海岸的距离，海中为负）、road（到道路的距离，无路 999）。
    //   valid = false：地形尚未生成（网页版返回 null）
    public struct TerrainInfo
    {
        public bool valid;
        public double h, slope;
        public string water;
        public double depth, forest, farm, sand, snow;
        public bool rock;
        public string biome;
        public double river, coast, road;
    }

    // 地表群落（网页版 groundInfo 的结果）：r, g, b 群落底色；forest 林地 0–1；sand 沙漠；barren 荒芜（岩石倾向）；snow 积雪；
    // des / st / tr / bo / med / sav / east 群落权重（植被、农田用）；river 到河流的距离（按河宽归一）
    public struct GroundInfo
    {
        public double r, g, b, forest, sand, barren, snow, des, st, tr, bo, med, sav, east, river;
    }

    // ================================================================ 地形模型 ==
    // 网页版 MapView 中与渲染无关的部分（第三版）：高度、1.25 高度栅格（按坡度平滑）、远景栅格（5.0，双三次）、
    // 0.625 主网格（Catmull-Rom 二倍上采样 + 限带细节噪声 + 去尖）、城址压平与镜头一侧限高、数据纹理（法线 / 凹凸遮蔽 / 高度、
    // 群落底色 + 林地、材质权重）、地表群落与农田、城外村落、树木 / 石块 / 灌丛 / 村舍分布、道路与桥、关隘、海域栅格与航线、
    // TerrainInfo / SurfaceY / RoadDist / Height。全部是纯计算（Tests~/map 无头逐项对照网页版）。
    public sealed class TerrainModel
    {
        public readonly GeoFields Geo;
        public readonly MapSpec Spec;
        public readonly bool World;
        public readonly IList<City> Cities;
        public readonly double[] CityX, CityZ;
        readonly PointIndex cityIdx, cityIdx10;

        // 1.25 高度栅格：Hs[i * (GNz + 1) + j]
        public float[] Hs; public double GX0, GZ0, GStep; public int GNx, GNz;
        // 0.625 主网格：MH[j * MNx + i]（MapView 的地形块顶点、SurfaceY）
        public float[] MH; public double MX0, MZ0, MRes; public int MNx, MNz;
        // 5.0 远景栅格：OH[j * ONx + i]
        public float[] OH; public double OX0, OZ0, ORes; public int ONx, ONz;
        public float[] CityBase;
        // 数据纹理（RGBA 字节，行优先）：N = 主网格分辨率（法线 xz、凹凸遮蔽、高度）；C / M = 1.25 间距（群落底色 + 林地、沙 / 荒芜 / 雪 / 1 − 农田）；
        // OC / OM = 远景栅格（底色 + 高度、材质权重）
        public byte[] NData, CData, MData, OCData, OMData;
        public int TexW, TexH;   // C / M 的尺寸（GNx + 1, GNz + 1）

        public sealed class Hamlet { public double x, z, yaw; public string culture; public int n, city; }
        public List<Hamlet> Hamlets = new List<Hamlet>();
        PointIndex hamletIdx;

        public TerrainModel(GeoFields geo, MapSpec spec, IList<City> cities)
        {
            Geo = geo; Spec = spec; World = spec.Key == "world"; Cities = cities ?? new List<City>();
            int n = Cities.Count;
            CityX = new double[n]; CityZ = new double[n];
            for (int i = 0; i < n; i++) WorldGeo.CityPos(Cities[i], out CityX[i], out CityZ[i]);
            cityIdx = PointIndex.Make(CityX, CityZ, 3.4);
            cityIdx10 = PointIndex.Make(CityX, CityZ, 10);
        }

        public bool NearCity(double x, double z, double r)
        {
            var list = cityIdx != null ? cityIdx.At(x, z) : null;
            if (list == null) return false;
            foreach (int i in list) if (Hyp(CityX[i] - x, CityZ[i] - z) < r) return true;
            return false;
        }
        // 到最近城池的距离（只查 3.4 以内；更远返回 99）
        public double CityNearDist(double x, double z)
        {
            var list = cityIdx != null ? cityIdx.At(x, z) : null;
            double d = 99;
            if (list != null) foreach (int i in list) d = Math.Min(d, Hyp(CityX[i] - x, CityZ[i] - z));
            return d;
        }
        static double Hyp(double x, double y) { x = Math.Abs(x); y = Math.Abs(y); double m = Math.Max(x, y); if (m == 0) return 0; double a = x / m, b = y / m; return Math.Sqrt(a * a + b * b) * m; }   // 与 V8 的 Math.hypot 逐位一致（两个参数）
        // GLSL 式 smoothstep（a > b 时反向）
        public static double SS(double a, double b, double x) { double t = (x - a) / (b - a); t = t < 0 ? 0 : t > 1 ? 1 : t; return t * t * (3 - 2 * t); }
        static double Perlin(double x, double y) { return WorldGeo.Perlin(x, y); }
        // 多项式平滑最小值
        static double SMin(double a, double b, double k) { double t = Math.Max(k - Math.Abs(a - b), 0) / k; return Math.Min(a, b) - t * t * k * 0.25; }

        // ------------------------------------------------------ 高度 --
        // Height(x, z)：程序化高度（未加细节噪声，地图坐标）
        public double Height(double x, double z)
        {
            var G = Geo;
            double coast = G.CoastDist(x, z);
            bool sea = G.InSea(x, z);
            double h = sea ? -0.4 - Math.Min(coast, 12) * 0.35 : 0.35 + Math.Min(coast, 10) * 0.08;
            double n = WorldGeo.Fbm(x * 0.045, z * 0.045);
            if (!sea) h += (n - 0.4) * 2.4;
            var f = G.Sample(x, z);
            double wOld = f[GeoFields.F_OLD];
            // 旧版西部高原（只在中国本部起作用），与世界高原按权重混合
            if (wOld > 0)
            {
                double p = WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(22, 2, x)) * 4.5 * (0.7 + n * 0.6);
                p += WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(30, 8, x)) * WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(10, 40, z)) * 2.0;
                // 世界地图：旧版的西部高原只保留青藏东缘（约北纬 25.5°–39.5°）
                if (World) p *= WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(8, 20, z)) * WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(96, 84, z));
                h += wOld >= 1 ? p : p * wOld;
            }
            if (!sea)
            {
                double P = f[GeoFields.F_PLAT];
                // 第三版：山脉 / 高原在海岸 2.4 单位内逐渐压低（免得山体直插海面成为锯齿状的断崖）
                double cr = 0.3 + 0.7 * SS(0, 2.4, coast);
                if (P > 0.001 && wOld < 1) h += (1 - wOld) * P * (0.7 + n * 0.6) * cr;
                // 沙丘
                double des = f[GeoFields.F_DESERT];
                if (des > 0.02) h += des * (WorldGeo.Fbm(x * 0.3 + 40, z * 0.3 + 40, 2) - 0.45) * 0.8;
                // 山脉
                h += G.RidgeSum(x, z) * cr;
            }
            // 河流与湖泊（宽度系数 rW、河床深度 rD）
            // 第三版：河岸宽度随两岸高度增加（山中的河谷平均坡度约 0.8），岸坡为上缘平缓的曲线
            if (!sea)
            {
                double rd = G.RiverDist(x, z) / G.rW;
                double bw = Math.Max(1.8, (h + 0.1) * 1.25 / G.rW);
                if (rd < 0.75 + bw)
                {
                    double d = G.rD;
                    double target = d;
                    if (rd >= 0.75) { double t = (rd - 0.75) / bw; target = WorldGeo.Lerp(d * (0.1 / 0.35), h, t * (2 - t)); }
                    h = Math.Min(h, target);
                }
            }
            // 城市周围平整
            var list = cityIdx != null ? cityIdx.At(x, z) : null;
            if (list != null)
                for (int q = 0; q < list.Count; q++)
                {
                    int i = list[q];
                    double d = Math.Sqrt((x - CityX[i]) * (x - CityX[i]) + (z - CityZ[i]) * (z - CityZ[i]));
                    if (d < 2.6) { double k = WorldGeo.Smooth(1, 0, Math.Max(0, d - 1.2) / 1.4); h = WorldGeo.Lerp(h, WorldGeo.Clamp(h, 0.5, 1.2), k); }
                }
            return h;
        }

        // 实际渲染出的地表高度：主网格范围内按 L0 地形块的三角形插值（与网格对角线一致），范围外为远景栅格的双三次插值
        public double SurfaceY(double x, double z)
        {
            if (MH == null) return Height(x, z);
            double fx0 = (x - MX0) / MRes, fz0 = (z - MZ0) / MRes;
            int i = (int)Math.Floor(fx0), j = (int)Math.Floor(fz0);
            if (i < 0 || j < 0 || i >= MNx - 1 || j >= MNz - 1) return OuterH(x, z);
            double fx = fx0 - i, fz = fz0 - j; int W = MNx, o = j * W + i;
            double ha = MH[o], hb = MH[o + W], hc = MH[o + W + 1], hd = MH[o + 1];
            if (((i + j) & 1) == 0)
            {
                if (fz >= fx) return ha + fz * (hb - ha) + fx * (hc - hb);
                return ha + fx * (hd - ha) + fz * (hc - hd);
            }
            if (fx + fz <= 1) return ha + fx * (hd - ha) + fz * (hb - ha);
            return hc + (1 - fx) * (hb - hc) + (1 - fz) * (hd - hc);
        }
        // 地形块顶点的高度：落在主网格格点上时取格点值，否则取远景栅格
        public double VertexH(double x, double z)
        {
            double fi = (x - MX0) / MRes, fj = (z - MZ0) / MRes;
            int i = WorldGeo.JsRound(fi), j = WorldGeo.JsRound(fj);
            if (i >= 0 && j >= 0 && i < MNx && j < MNz && Math.Abs(fi - i) < 1e-3 && Math.Abs(fj - j) < 1e-3) return MH[j * MNx + i];
            return OuterH(x, z);
        }
        // 远景栅格（5.0 间距）的 Catmull-Rom 双三次插值
        readonly double[] crx = new double[4], crz = new double[4];
        static double[] Crw(double t, double[] w)
        {
            double t2 = t * t, t3 = t2 * t;
            w[0] = (-t3 + 2 * t2 - t) / 2; w[1] = (3 * t3 - 5 * t2 + 2) / 2; w[2] = (-3 * t3 + 4 * t2 + t) / 2; w[3] = (t3 - t2) / 2;
            return w;
        }
        public double OuterH(double x, double z)
        {
            if (OH == null) return Height(x, z);
            double fx = WorldGeo.Clamp((x - OX0) / ORes, 0, ONx - 1), fz = WorldGeo.Clamp((z - OZ0) / ORes, 0, ONz - 1);
            int i = Math.Min(ONx - 2, (int)Math.Floor(fx)), j = Math.Min(ONz - 2, (int)Math.Floor(fz));
            double tx = fx - i, tz = fz - j; int W = ONx;
            var wx = Crw(tx, crx); var wz = Crw(tz, crz);
            double s = 0;
            for (int b = 0; b < 4; b++)
            {
                int jj = Math.Max(0, Math.Min(ONz - 1, j - 1 + b)) * W;
                double r = 0;
                for (int a = 0; a < 4; a++) r += wx[a] * OH[jj + Math.Max(0, Math.Min(W - 1, i - 1 + a))];
                s += wz[b] * r;
            }
            return s;
        }
        public bool IsForest(double x, double z, double h) { return h >= 0.6 && h < 3.2 && WorldGeo.Fbm(x * 0.09 + 100, z * 0.09 + 100, 3) > 0.6; }

        // 地图坐标处的地形摘要；surf：替代 SurfaceY 的高度函数（缺省 null = SurfaceY；BattleTerrain 用与状态无关的原始高度）
        public TerrainInfo TerrainInfo(double x, double z) { return TerrainInfo(x, z, null); }
        public TerrainInfo TerrainInfo(double x, double z, Func<double, double, double> surf)
        {
            var ti = new TerrainInfo();
            var G = Geo;
            if (G == null || MH == null) return ti;
            Func<double, double, double> S = surf ?? SurfaceY;
            double h = S(x, z), e = 0.6;
            double hx = (S(x + e, z) - S(x - e, z)) / (2 * e), hz = (S(x, z + e) - S(x, z - e)) / (2 * e);
            double slope = 1 - 1 / Math.Sqrt(1 + hx * hx + hz * hz);
            bool sea = G.InSea(x, z); double coast = G.CoastDist(x, z);
            var gi = Ground(x, z, h, false);
            double rd = gi.river;
            double farm = FarmAt(x, z, h, ref gi, slope);
            string water = h < -0.05 ? (sea ? "sea" : rd < 1.6 ? "river" : "lake") : null;
            string biome = "temperate"; double bw = 0.35;
            if (gi.des > bw) { biome = "desert"; bw = gi.des; }
            if (gi.st > bw) { biome = "steppe"; bw = gi.st; }
            if (gi.tr > bw) { biome = "tropical"; bw = gi.tr; }
            if (gi.bo > bw) { biome = "boreal"; bw = gi.bo; }
            if (gi.med > bw) { biome = "med"; bw = gi.med; }
            if (gi.sav > bw) { biome = "savanna"; bw = gi.sav; }
            if (gi.barren > 0.55 && h > 2.5) biome = "alpine";
            ti.valid = true;
            ti.h = h; ti.slope = slope; ti.water = water; ti.depth = Math.Max(0, -h);
            ti.forest = gi.forest; ti.farm = farm; ti.sand = gi.sand; ti.snow = gi.snow;
            ti.rock = slope > 0.42 || h > 4.2; ti.biome = biome; ti.river = rd; ti.coast = sea ? -coast : coast; ti.road = RoadDist(x, z);
            return ti;
        }
        // 到最近道路（陆路）的距离；未生成道路时 999
        public double RoadDist(double x, double z)
        {
            if (roadIdx == null) return 999;
            var list = roadIdx.At(x, z);
            if (list == null) return 999;
            double best = 999;
            var S = roadSegs;
            foreach (int k in list) { int o = k * 4; best = Math.Min(best, WorldGeo.SegDist(x, z, S[o], S[o + 1], S[o + 2], S[o + 3])); }
            return best;
        }

        // ------------------------------------------------------ 地表群落 --
        // 地表不逐面取色，而是生成「群落底色 + 材质权重」数据纹理，由地形着色器按坡度 / 高度在像素级混合（Sanguo/Terrain）
        static readonly double[] LUSH = { 0.36, 0.62, 0.3 }, DRY = { 0.66, 0.64, 0.38 }, STEPPE = { 0.62, 0.55, 0.36 };
        public static readonly double[] SEABED = { 0.7, 0.66, 0.5 };
        static readonly double[] FOREST = { 0.22, 0.45, 0.24 }, UPLAND = { 0.5, 0.5, 0.34 };
        static readonly double[] DESERT = { 0.84, 0.72, 0.47 }, STRAW = { 0.72, 0.66, 0.41 }, TROPIC = { 0.2, 0.55, 0.22 }, SAVANNA = { 0.62, 0.61, 0.34 };
        static readonly double[] MED_OLIVE = { 0.53, 0.58, 0.34 }, TAIGA = { 0.27, 0.45, 0.31 };
        static readonly double[] ALPINE = { 0.6, 0.55, 0.4 };
        static readonly double[] TROP_FOREST = { 0.12, 0.4, 0.16 }, TAIGA_FOREST = { 0.15, 0.32, 0.23 };
        readonly double[] grass = new double[3], fcol = new double[3];
        static void LerpInto(double[] c, double[] b, double t)
        {
            t = WorldGeo.Clamp01(t);
            c[0] += (b[0] - c[0]) * t; c[1] += (b[1] - c[1]) * t; c[2] += (b[2] - c[2]) * t;
        }
        public GroundInfo Ground(double x, double z, double h, bool noForest)
        {
            var G = Geo;
            var f = G.Sample(x, z);
            double north = WorldGeo.InvLerp(10, 95, z);
            var c = grass;
            c[0] = LUSH[0]; c[1] = LUSH[1]; c[2] = LUSH[2];
            LerpInto(c, DRY, north * 0.85 * f[GeoFields.F_EAST]);
            LerpInto(c, STEPPE, WorldGeo.InvLerp(30, 5, x) * 0.6 * f[GeoFields.F_OLD]);
            double med = f[GeoFields.F_MED], sav = f[GeoFields.F_SAV], st = f[GeoFields.F_STEPPE], bo = f[GeoFields.F_BOREAL], tr = f[GeoFields.F_TROP];
            double des = f[GeoFields.F_DESERT];
            double fOld = f[GeoFields.F_OLD], fPlat = f[GeoFields.F_PLAT], fEast = f[GeoFields.F_EAST];
            if (med > 0.001) LerpInto(c, MED_OLIVE, med * 0.75);
            if (sav > 0.001) LerpInto(c, SAVANNA, sav * 0.75);
            double rd = G.RiverDist(x, z) / G.rW;
            double oa = des > 0.001 || st > 0.001 ? 0.2 + 0.8 * WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(1.0, 4.5, rd * G.rW)) : 1;
            if (st > 0.001) LerpInto(c, STRAW, st * 0.85 * (0.45 + 0.55 * oa));
            if (bo > 0.001) LerpInto(c, TAIGA, bo * 0.8);
            if (tr > 0.001) LerpInto(c, TROPIC, tr * 0.85);
            if (des > 0.001) { des *= oa; LerpInto(c, DESERT, des); }
            // 林地（与树木分布同一噪声）
            double fw = 0;
            if (!noForest && h >= 0.42 && h < 3.5)
            {
                double forest = WorldGeo.Fbm(x * 0.09 + 100, z * 0.09 + 100, 3);
                double thr = 0.56 + des * 0.3 + st * 0.12 + sav * 0.05 - tr * 0.06 - bo * 0.05;
                fw = WorldGeo.InvLerp(thr - 0.02, thr + 0.1, forest);
                if (fw > 0)
                {
                    var fc = FOREST;
                    if (tr > 0.001 || bo > 0.001)
                    {
                        fc = fcol; fc[0] = FOREST[0]; fc[1] = FOREST[1]; fc[2] = FOREST[2];
                        LerpInto(fc, TROP_FOREST, tr); LerpInto(fc, TAIGA_FOREST, bo);
                    }
                    LerpInto(c, fc, fw * 0.75);
                }
            }
            // 高地与高原荒原（旧版范围外）；世界地图里旧版西部高原也按高寒荒原着色
            double wNew = 1 - fOld, plat = fPlat / 3;
            if (World)
            {
                double wp = WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(24, 8, x));
                if (wp > 0) { wNew = Math.Max(wNew, 0.9 * wp); plat = Math.Max(plat, wp); }
            }
            double alp = wNew > 0.001 ? wNew * WorldGeo.Clamp01(plat) : 0;
            if (h > 2.2) LerpInto(c, UPLAND, WorldGeo.InvLerp(2.2, 4.0, h) * 0.55);
            if (alp > 0) LerpInto(c, ALPINE, alp * WorldGeo.InvLerp(1.5, 3.5, h));
            double snow = 6.2 + 1.6 * wNew;
            var g = new GroundInfo();
            g.r = c[0]; g.g = c[1]; g.b = c[2];
            g.forest = fw; g.sand = WorldGeo.Clamp01(des * 1.1);
            g.barren = WorldGeo.Clamp01(alp * 0.7 + des * 0.5 + st * 0.15 + WorldGeo.InvLerp(3.0, 6.0, h) * 0.3);
            g.snow = WorldGeo.Clamp01((h - (snow - 0.3)) / 1.2);
            g.des = des; g.st = st; g.tr = tr; g.bo = bo; g.med = med; g.sav = sav; g.east = fEast; g.river = rd;
            return g;
        }

        // 农田权重 0–1：城郊、村落周围、大河两岸的平原（沙漠中的河谷为灌溉农田）
        public double FarmAt(double x, double z, double h, ref GroundInfo gi, double slope)
        {
            if (h < 0.42 || h > 2.6 || slope > 0.22) return 0;
            double w = 0;
            var cl = cityIdx10 != null ? cityIdx10.At(x, z) : null;
            // 第三版修订：草原上的城（毡帐营地）四周不是农田
            if (cl != null) foreach (int i in cl) { double d = Hyp(CityX[i] - x, CityZ[i] - z); w = Math.Max(w, SS(8.5, 4.2, d) * SS(1.7, 2.9, d) * (1 - 0.85 * WorldGeo.Clamp01(gi.st * 1.4))); }
            var hl = hamletIdx != null ? hamletIdx.At(x, z) : null;
            if (hl != null) foreach (int i in hl) { var o = Hamlets[i]; w = Math.Max(w, SS(3.4, 1.0, Hyp(o.x - x, o.z - z))); }
            double rv = SS(5.0, 1.5, gi.river) * SS(2.0, 1.1, h);
            double k = WorldGeo.Clamp01(0.5 - 0.5 * gi.st - 0.45 * gi.bo + 0.55 * gi.des - 0.2 * gi.tr);
            w = Math.Max(w, rv * k);
            return WorldGeo.Clamp01(w * (1 - gi.forest * 0.9) * (1 - gi.sand * 0.8 * (1 - rv)) * SS(0.22, 0.1, slope));
        }

        // ------------------------------------------------------ 地形 --
        // 城外村落的位置（在生成农田纹理之前确定）：每城 1–3 处，优先沿出城的道路，离城 4.2–6.4，平地、不近水
        IEnumerator PlanVillages()
        {
            var outL = new List<Hamlet>(); var rnd = new SeededRandom(29); var G = Geo;
            int n = Cities.Count;
            foreach (var c in Cities)
            {
                double cx = CityX[c.id], cz = CityZ[c.id];
                string culture = string.IsNullOrEmpty(c.culture) ? "han" : c.culture;
                int want = 1 + (c.town > 330 ? 1 : 0) + (c.town > 520 ? 1 : 0);
                var dirs = new List<double>();
                foreach (int li in c.links) if (li >= 0 && li < n && li != c.id) dirs.Add(Math.Atan2(CityZ[li] - cz, CityX[li] - cx));
                while (dirs.Count < 7) dirs.Add(rnd.NextDouble() * Math.PI * 2);
                int placed = 0;
                for (int k = 0; k < dirs.Count && placed < want; k++)
                {
                    double a = dirs[k] + (rnd.NextDouble() - 0.5) * 0.35, r = 4.2 + rnd.NextDouble() * 2.2;
                    double side = (rnd.NextDouble() < 0.5 ? -1 : 1) * (0.9 + rnd.NextDouble() * 0.6);
                    double hx = cx + Math.Cos(a) * r - Math.Sin(a) * side, hz = cz + Math.Sin(a) * r + Math.Cos(a) * side;
                    double h = Height(hx, hz);
                    if (h < 0.5 || h > 2.6 || G.InSea(hx, hz) || G.RiverDist(hx, hz) / G.rW < 1.8) continue;
                    if (Math.Abs(Height(hx + 0.9, hz) - Height(hx - 0.9, hz)) > 0.45 || Math.Abs(Height(hx, hz + 0.9) - Height(hx, hz - 0.9)) > 0.45) continue;
                    bool bad = false;
                    for (int q = 0; q < n && !bad; q++) if (q != c.id && Hyp(CityX[q] - hx, CityZ[q] - hz) < 4.2) bad = true;
                    foreach (var o in outL) if (!bad && Hyp(o.x - hx, o.z - hz) < 3.2) bad = true;
                    if (bad) continue;
                    outL.Add(new Hamlet { x = hx, z = hz, culture = culture, n = 3 + rnd.Next(4), yaw = a, city = c.id });
                    placed++;
                }
                if ((c.id & 7) == 7) yield return null;
            }
            Hamlets = outL;
            var xs = new List<double>(); var zs = new List<double>();
            foreach (var o in outL) { xs.Add(o.x); zs.Add(o.z); }
            hamletIdx = PointIndex.Make(xs, zs, 3.6);
        }

        // 按坡度加权的平滑（一遍）。下标 o = a·sa + b·sb；水面点（< 0.06）不动，陆地点不被压到水面以下。
        // dh（与四邻的最大高差）在 lo 以下不动，hi 以上按 amt 向 3×3 邻点的加权均值靠拢；紧邻水面的岸点不动
        static IEnumerator SlopeSmooth(float[] h, int na, int nb, int sa, int sb, double lo, double hi, double amt)
        {
            var src = (float[])h.Clone();
            for (int a = 1; a < na - 1; a++)
            {
                for (int b = 1; b < nb - 1; b++)
                {
                    int o = a * sa + b * sb; double c = src[o];
                    if (c < 0.06) continue;
                    double n1 = src[o - sa], n2 = src[o + sa], n3 = src[o - sb], n4 = src[o + sb];
                    double dh = Math.Max(Math.Max(Math.Abs(n1 - c), Math.Abs(n2 - c)), Math.Max(Math.Abs(n3 - c), Math.Abs(n4 - c)));
                    if (dh <= lo) continue;
                    double d1 = src[o - sa - sb], d2 = src[o - sa + sb], d3 = src[o + sa - sb], d4 = src[o + sa + sb];
                    if (Math.Min(Math.Min(Math.Min(n1, n2), Math.Min(n3, n4)), Math.Min(Math.Min(d1, d2), Math.Min(d3, d4))) < 0.06) continue;
                    double avg = (2 * (n1 + n2 + n3 + n4) + d1 + d2 + d3 + d4) / 12;
                    double v = c + (avg - c) * SS(lo, hi, dh) * amt;
                    h[o] = (float)(v < 0.06 ? 0.06 : v);
                }
                if ((a & 31) == 31) yield return null;
            }
        }
        // 去尖（一遍）：与 8 邻加权均值的偏差（≈ 曲率）在 lo 以下不动，hi 以上按 amt 靠拢；水面点与紧邻水面的岸点不动
        static IEnumerator CurveSmooth(float[] h, int na, int nb, int sa, int sb, double lo, double hi, double amt)
        {
            var src = (float[])h.Clone();
            for (int a = 1; a < na - 1; a++)
            {
                for (int b = 1; b < nb - 1; b++)
                {
                    int o = a * sa + b * sb; double c = src[o];
                    if (c < 0.06) continue;
                    double n1 = src[o - sa], n2 = src[o + sa], n3 = src[o - sb], n4 = src[o + sb], d1 = src[o - sa - sb], d2 = src[o - sa + sb], d3 = src[o + sa - sb], d4 = src[o + sa + sb];
                    if (Math.Min(Math.Min(Math.Min(n1, n2), Math.Min(n3, n4)), Math.Min(Math.Min(d1, d2), Math.Min(d3, d4))) < 0.06) continue;
                    double avg = (2 * (n1 + n2 + n3 + n4) + d1 + d2 + d3 + d4) / 12;
                    double dev = Math.Abs(avg - c);
                    if (dev <= lo) continue;
                    double v = c + (avg - c) * SS(lo, hi, dev) * amt;
                    h[o] = (float)(v < 0.06 ? 0.06 : v);
                }
                if ((a & 31) == 31) yield return null;
            }
        }

        // 地形阶段（网页版 _genTerrain 的 1–4 步）：村落规划、高度栅格 + 平滑、远景栅格、主网格（上采样 + 细节噪声 + 去尖）、
        // 城址压平、数据纹理。sub(0..1) 报告阶段内进度（到 0.9；其余由 MapView 的地形块预建报告）
        public IEnumerator GenTerrain(Action<float> sub)
        {
            Action<float> S = f => { if (sub != null) sub(f); };
            var T = Spec.Terrain; double step = T.step; var Sk = Spec.Skirt;
            yield return PlanVillages();
            // 1) 高度栅格（1.25）
            double x0 = T.x0, z0 = T.z0;
            int nx = (int)Math.Ceiling((T.x1 - x0) / step), nz = (int)Math.Ceiling((T.z1 - z0) / step), W = nz + 1;
            var hs = new float[(nx + 1) * W];
            for (int i = 0; i <= nx; i++)
            {
                for (int j = 0; j <= nz; j++)
                {
                    hs[i * W + j] = (float)Height(x0 + i * step, z0 + j * step);
                    if ((j & 127) == 127) yield return null;
                }
                S(0.3f * i / nx);
                yield return null;
            }
            // 1b) 按坡度加权的平滑（两遍）：陡坡、尖峰、刀切的岸壁向邻点均值靠拢；平地与水面不动
            for (int pass = 0; pass < 2; pass++) yield return SlopeSmooth(hs, nx + 1, nz + 1, W, 1, 0.5, 1.5, 0.8);
            Hs = hs; GX0 = x0; GZ0 = z0; GStep = step; GNx = nx; GNz = nz;
            // 2) 远景栅格（5.0），与主网格对齐；落在高度栅格内的点直接取值
            const double R = 5;
            double ox0 = x0 - Math.Ceiling((x0 - Sk.x0) / R) * R, oz0 = z0 - Math.Ceiling((z0 - Sk.z0) / R) * R;
            int onx = (int)Math.Ceiling((Sk.x1 - ox0) / R) + 1, onz = (int)Math.Ceiling((Sk.z1 - oz0) / R) + 1;
            var oh = new float[onx * onz];
            for (int j = 0; j < onz; j++)
            {
                double z = oz0 + j * R; int hj = WorldGeo.JsRound((z - z0) / step);
                for (int i = 0; i < onx; i++)
                {
                    double x = ox0 + i * R; int hi = WorldGeo.JsRound((x - x0) / step);
                    oh[j * onx + i] = hi >= 0 && hj >= 0 && hi <= nx && hj <= nz ? hs[hi * W + hj] : (float)Height(x, z);
                }
                if ((j & 3) == 3) { S(0.3f + 0.06f * j / onz); yield return null; }
            }
            OH = oh; OX0 = ox0; OZ0 = oz0; ORes = R; ONx = onx; ONz = onz;
            // 3) 主网格（0.625）：Catmull-Rom 二倍上采样（可分离），边缘 12 单位内过渡到远景栅格，再加细节噪声
            double res = step / 2; int mnx = 2 * nx + 1, mnz = 2 * nz + 1;
            var mh = new float[mnx * mnz];
            for (int j = 0; j <= nz; j++)
            {
                int row = 2 * j * mnx;
                for (int i = 0; i <= nx; i++)
                {
                    mh[row + 2 * i] = hs[i * W + j];
                    if (i < nx) mh[row + 2 * i + 1] = (float)CR(hs[Math.Max(0, i - 1) * W + j], hs[i * W + j], hs[(i + 1) * W + j], hs[Math.Min(nx, i + 2) * W + j]);
                }
                if ((j & 7) == 7) yield return null;
            }
            for (int j = 0; j < nz; j++)
            {
                int r0 = 2 * Math.Max(0, j - 1) * mnx, r1 = 2 * j * mnx, r2 = 2 * (j + 1) * mnx, r3 = 2 * Math.Min(nz, j + 2) * mnx, ro = (2 * j + 1) * mnx;
                for (int i = 0; i < mnx; i++) mh[ro + i] = (float)CR(mh[r0 + i], mh[r1 + i], mh[r2 + i], mh[r3 + i]);
                if ((j & 7) == 7) yield return null;
            }
            double X1 = x0 + nx * step, Z1 = z0 + nz * step; const double BL = 12;
            for (int j = 0; j < mnz; j++)
            {
                double z = z0 + j * res; int row = j * mnx;
                for (int i = 0; i < mnx; i++)
                {
                    double x = x0 + i * res; int o = row + i;
                    double h = mh[o];
                    double e = Math.Min(Math.Min(x - x0, X1 - x), Math.Min(z - z0, Z1 - z));
                    if (e < BL) h = WorldGeo.Lerp(OuterH(x, z), h, SS(0, BL, e));
                    // 细节噪声限带（波长都在 2.7 单位以上），脊状噪声的脊线圆滑（√(d² + ε)）
                    double a = (0.035 + 0.15 * SS(1.0, 2.8, h) + 0.16 * SS(2.8, 5.5, h)) * SS(0.25, 0.9, h);
                    if (a > 0.002 && h >= 0.06)
                    {
                        double cd = CityNearDist(x, z);
                        if (cd < 3.4) a *= SS(1.8, 3.4, cd);
                        h += a * 2.2 * ((Perlin(x * 0.36 + 3.1, z * 0.36 + 7.7) - 0.465) + 0.6 * (Perlin(x * 0.19 + 11.3, z * 0.19 + 5.1) - 0.465));
                        // 山地：圆脊噪声（山脊、沟壑）
                        double am = 0.4 * SS(2.2, 5.0, h);
                        if (am > 0.01)
                        {
                            double d1 = (Perlin(x * 0.24 + 21.7, z * 0.24 + 9.4) - 0.465) * 3.2;
                            double d2 = (Perlin(x * 0.37 + 4.3, z * 0.37 + 31.9) - 0.465) * 3.2;
                            double rr = 1 - Math.Min(1, Math.Sqrt(d1 * d1 + 0.16) - 0.4), rr2 = 1 - Math.Min(1, Math.Sqrt(d2 * d2 + 0.16) - 0.4);
                            h += am * ((rr * rr - 0.45) * 1.6 + (rr2 * rr2 - 0.45) * 0.45) * (cd < 3.4 ? SS(1.8, 3.4, cd) : 1);
                        }
                        if (h < 0.06) h = 0.06;
                    }
                    mh[o] = (float)h;
                }
                if ((j & 1) == 1) { S(0.36f + 0.1f * j / mnz); yield return null; }
            }
            // 3b) 去尖：曲率大的点（尖峰、折痕）向邻点均值靠拢，两遍
            for (int pass = 0; pass < 2; pass++) { yield return CurveSmooth(mh, mnz, mnx, mnx, 1, 0.1, 0.4, 0.8); S(0.46f + 0.02f * pass); }
            MH = mh; MX0 = x0; MZ0 = z0; MRes = res; MNx = mnx; MNz = mnz;
            // 3c) 城池：占地压平到城基高度；镜头一侧（−z）的视线楔形内限高
            yield return FitCities();
            // 4) 数据纹理：法线 / 凹凸遮蔽 / 高度（主网格分辨率）
            var nData = new byte[mnx * mnz * 4];
            var blur = (float[])mh.Clone();
            yield return GeoFields.BoxBlur(blur, mnx, mnz, 3, 2);
            double inv = 1 / (2 * res);
            for (int j = 0; j < mnz; j++)
            {
                int row = j * mnx, jm = Math.Max(0, j - 1) * mnx, jp = Math.Min(mnz - 1, j + 1) * mnx;
                for (int i = 0; i < mnx; i++)
                {
                    int o = row + i, q = o * 4;
                    double hx = ((double)mh[row + Math.Min(mnx - 1, i + 1)] - mh[row + Math.Max(0, i - 1)]) * inv;
                    double hz = ((double)mh[jp + i] - mh[jm + i]) * inv;
                    double l = 1 / Math.Sqrt(hx * hx + 1 + hz * hz);
                    nData[q] = WorldGeo.B8(0.5 - 0.5 * hx * l); nData[q + 1] = WorldGeo.B8(0.5 + 0.5 * hz * l);
                    nData[q + 2] = WorldGeo.B8(0.5 + ((double)mh[o] - blur[o]) * 0.9);
                    nData[q + 3] = WorldGeo.B8((mh[o] + 4.0) / 8);
                }
                if ((j & 15) == 15) { S(0.5f + 0.08f * j / mnz); yield return null; }
            }
            // 群落底色 + 林地（C）、材质权重（M：沙漠、荒芜、积雪、1 − 农田），1.25 间距
            int cw = nx + 1, chh = nz + 1;
            var cData = new byte[cw * chh * 4]; var mData = new byte[cw * chh * 4];
            for (int j = 0; j < chh; j++)
            {
                double z = z0 + j * step; int mj = 2 * j;
                for (int i = 0; i < cw; i++)
                {
                    double x = x0 + i * step; int mi = 2 * i, mo = mj * mnx + mi;
                    double h = mh[mo];
                    double hx = ((double)mh[mj * mnx + Math.Min(mnx - 1, mi + 1)] - mh[mj * mnx + Math.Max(0, mi - 1)]) * inv;
                    double hz = ((double)mh[Math.Min(mnz - 1, mj + 1) * mnx + mi] - mh[Math.Max(0, mj - 1) * mnx + mi]) * inv;
                    double slope = 1 - 1 / Math.Sqrt(1 + hx * hx + hz * hz);
                    var gi = Ground(x, z, h, false);
                    double farm = h > 0.4 ? FarmAt(x, z, h, ref gi, slope) : 0;
                    int q = (j * cw + i) * 4;
                    cData[q] = WorldGeo.B8(gi.r); cData[q + 1] = WorldGeo.B8(gi.g); cData[q + 2] = WorldGeo.B8(gi.b); cData[q + 3] = WorldGeo.B8(gi.forest);
                    mData[q] = WorldGeo.B8(gi.sand); mData[q + 1] = WorldGeo.B8(gi.barren); mData[q + 2] = WorldGeo.B8(gi.snow); mData[q + 3] = WorldGeo.B8(1 - farm);
                    if ((i & 255) == 255) yield return null;
                }
                S(0.58f + 0.28f * j / chh);
                yield return null;
            }
            // 远景的底色（A = 高度，供水面计算深浅）与材质权重
            var ocData = new byte[onx * onz * 4]; var omData = new byte[onx * onz * 4];
            for (int j = 0; j < onz; j++)
            {
                double z = oz0 + j * R;
                for (int i = 0; i < onx; i++)
                {
                    double x = ox0 + i * R, h = oh[j * onx + i]; int q = (j * onx + i) * 4;
                    var gi = Ground(x, z, h, true);
                    ocData[q] = WorldGeo.B8(gi.r); ocData[q + 1] = WorldGeo.B8(gi.g); ocData[q + 2] = WorldGeo.B8(gi.b); ocData[q + 3] = WorldGeo.B8((h + 4) / 8);
                    omData[q] = WorldGeo.B8(gi.sand); omData[q + 1] = WorldGeo.B8(gi.barren); omData[q + 2] = WorldGeo.B8(gi.snow); omData[q + 3] = 255;
                }
                if ((j & 7) == 7) { S(0.86f + 0.04f * j / onz); yield return null; }
            }
            NData = nData; CData = cData; MData = mData; OCData = ocData; OMData = omData; TexW = cw; TexH = chh;
            S(0.9f);
        }
        static double CR(double a, double b, double c, double d) { return (9 * (b + c) - a - d) / 16; }

        // 第三版：城池占地压平（城基 = 中心高度，半径 size·1.3 + 0.35 内完全压平，再 1.6 单位平滑过渡），
        // 并在镜头一侧（地图 −z）的楔形内限高：限高 = 城基 + 0.2 + 0.72 × 距离 + 横向超出城宽部分的平方
        IEnumerator FitCities()
        {
            var mh = MH; int W = MNx; double res = MRes;
            int n = Cities.Count;
            var bse = CityBase = new float[n];
            Func<double, double, double> at = (x, z) =>
            {
                double fx = WorldGeo.Clamp((x - MX0) / res, 0, MNx - 1.001), fz = WorldGeo.Clamp((z - MZ0) / res, 0, MNz - 1.001);
                int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz); double tx = fx - i, tz = fz - j; int o = j * W + i;
                return (mh[o] * (1 - tx) + mh[o + 1] * tx) * (1 - tz) + (mh[o + W] * (1 - tx) + mh[o + W + 1] * tx) * tz;
            };
            const double TAN = 0.72, AHEAD = 10;
            foreach (var c in Cities)
            {
                double cx = CityX[c.id], cz = CityZ[c.id];
                double size = WorldGeo.Lerp(0.9, 1.5, WorldGeo.InvLerp(180, 650, c.town));
                double y = Math.Max(0.3, at(cx, cz));
                bse[c.id] = (float)y;
                double R1 = size * 1.3 + 0.35, R2 = R1 + 1.6;
                int i0 = Math.Max(0, (int)Math.Floor((cx - R2 - 3 - MX0) / res)), i1 = Math.Min(MNx - 1, (int)Math.Ceiling((cx + R2 + 3 - MX0) / res));
                int j0 = Math.Max(0, (int)Math.Floor((cz - AHEAD - MZ0) / res)), j1 = Math.Min(MNz - 1, (int)Math.Ceiling((cz + R2 - MZ0) / res));
                for (int j = j0; j <= j1; j++)
                {
                    double z = MZ0 + j * res, dz = z - cz;
                    for (int i = i0; i <= i1; i++)
                    {
                        double x = MX0 + i * res, dx = x - cx; int o = j * W + i;
                        double h = mh[o];
                        double d = Math.Sqrt(dx * dx + dz * dz);
                        // 河流、海湾在城体以外不填平（水面一直流到城墙下）
                        if (d < R2 && (h >= 0.06 || d < R1 * 0.8)) h = WorldGeo.Lerp(h, y, SS(R2, R1, d));
                        if (dz < 0)
                        {
                            double e = Math.Max(0, Math.Abs(dx) - R1);
                            double cap = y + 0.2 + TAN * -dz + e * e * 0.9;
                            if (h > cap - 0.6) h = SMin(h, cap, 0.6);
                        }
                        mh[o] = (float)h;
                    }
                }
                if ((c.id & 15) == 15) yield return null;
            }
        }

        // 城池的模型尺寸（与网页版相同：town 180 → 0.9，650 → 1.5）
        public static float CitySize(City c) { return (float)WorldGeo.Lerp(0.9, 1.5, WorldGeo.InvLerp(180, 650, c.town)); }

        // ------------------------------------------------------ 树木 --
        // 一棵树 / 石块 / 灌丛 / 村舍的实例：kind（树种名 / "rock0" "rock1" / 村舍屋型）、位置、缩放（s 横向、sy 纵向）、
        // yaw（three 的绕 y 角，弧度；Unity 取反）、颜色
        public struct Inst { public string kind; public double x, y, z, s, sy, yaw; public float r, g, b; }

        static readonly double[] PINE = { 0.2, 0.42, 0.27 }, SPRUCE = { 0.15, 0.34, 0.25 }, DEC_S = { 0.3, 0.57, 0.25 }, DEC_N = { 0.48, 0.6, 0.28 }, CAMPHOR = { 0.24, 0.5, 0.24 };
        static readonly double[] TROP = { 0.14, 0.47, 0.18 }, PALM = { 0.32, 0.58, 0.2 }, OLIVE = { 0.52, 0.58, 0.4 }, CYPRESS = { 0.14, 0.31, 0.19 }, SHRUB = { 0.52, 0.52, 0.3 };
        static readonly double[] ACACIA = { 0.46, 0.52, 0.24 }, BAMBOO = { 0.44, 0.64, 0.3 }, WILLOW = { 0.5, 0.68, 0.32 }, POPLAR = { 0.4, 0.58, 0.25 }, BIRCH = { 0.52, 0.66, 0.3 };
        static readonly double[] STONEPINE = { 0.24, 0.42, 0.22 }, MAPLE = { 0.78, 0.38, 0.16 }, CEDAR = { 0.13, 0.3, 0.2 };
        static readonly double[] VILLAGE_TREE = { 0.36, 0.6, 0.27 };

        // 树木：树种随群落变化（每种群落数种），林地按噪声成片，平原上零星散树、河边柳杨、绿洲椰枣、村边果树。
        // 随机数 = SeededRandom(11)（与网页版逐个相同）；结果按生成顺序写入 outList
        public IEnumerator PlaceTrees(List<Inst> outList, Action<float> sub)
        {
            var rnd = new SeededRandom(11);
            var geo = Geo; var D = Spec.Trees;
            Action<string, double, double, double, double, double, double, double[], double> add = (kind, px, py, pz, s, sy, yaw, c, jit) =>
            {
                double k = 1 + jit;
                outList.Add(new Inst { kind = kind, x = px, y = py, z = pz, s = s, sy = sy, yaw = yaw, r = (float)Math.Min(1, c[0] * k), g = (float)Math.Min(1, c[1] * k), b = (float)Math.Min(1, c[2] * k) });
            };
            double japanX, japanY; MapProjection.Project(124.5, 35, out japanX, out japanY);
            const double SP = 0.82;
            var xs = new List<double>();
            for (double x = D.x0; x < D.x1; x += SP) xs.Add(x);
            for (int xi = 0; xi < xs.Count; xi++)
            {
                double x = xs[xi];
                for (double z = D.z0; z < D.z1; z += SP)
                {
                    double px = x + (rnd.NextDouble() - 0.5) * 0.75, pz = z + (rnd.NextDouble() - 0.5) * 0.75;
                    double r1 = rnd.NextDouble();
                    double h = SurfaceY(px, pz);
                    if (h <= 0.5 || h >= 3.6) continue;
                    var f = geo.Sample(px, pz);
                    double des = f[GeoFields.F_DESERT], st = f[GeoFields.F_STEPPE], sav = f[GeoFields.F_SAV], med = f[GeoFields.F_MED], tr = f[GeoFields.F_TROP], bo = f[GeoFields.F_BOREAL], east = f[GeoFields.F_EAST];
                    double thr = 0.56 + des * 0.3 + st * 0.12 + sav * 0.05 - tr * 0.06 - bo * 0.05;
                    double fw = WorldGeo.InvLerp(thr - 0.02, thr + 0.1, WorldGeo.Fbm(px * 0.09 + 100, pz * 0.09 + 100, 3));
                    bool forest = r1 < fw * 0.92;
                    double pSparse = Math.Max(0, 0.022 * (1 - 0.9 * des - 0.6 * st) + 0.03 * sav + 0.02 * med + 0.01 * tr);
                    bool sparse = !forest && h < 2.6 && r1 > 0.97 - pSparse;
                    double rd = 99;
                    bool oasisTree = false, riverTree = false;
                    if (!forest && !sparse)
                    {
                        if (h > 1.8 || r1 < 0.9) continue;
                        rd = geo.RiverDist(px, pz) / geo.rW;
                        oasisTree = des > 0.35 && rd > 1.3 && rd < 3.4 && r1 > 0.93;
                        riverTree = !oasisTree && rd > 1.4 && rd < 2.6 && r1 > 0.975;
                        if (!oasisTree && !riverTree) continue;
                    }
                    if (NearCity(px, pz, 3.2)) continue;
                    if (rd == 99) rd = geo.RiverDist(px, pz) / geo.rW;
                    if (rd < 1.3) continue;
                    const double e = 0.5;
                    double sl = Math.Abs(SurfaceY(px + e, pz) - SurfaceY(px - e, pz)) + Math.Abs(SurfaceY(px, pz + e) - SurfaceY(px, pz - e));
                    if (sl > 1.1) continue;
                    double north = WorldGeo.InvLerp(10, 95, pz);
                    double v = rnd.NextDouble(), s = 0.85 + rnd.NextDouble() * 0.6, jit = (rnd.NextDouble() - 0.5) * 0.16;
                    double hy = Math.Sin(px * 12.9898 + pz * 78.233) * 43758.5453;
                    double y = h - 0.04, yaw = (hy - Math.Floor(hy)) * Math.PI * 2;
                    if (oasisTree || (des > 0.5 && v < 0.35)) { add("palm", px, y, pz, s * 1.05, s * 1.05, yaw, PALM, jit); continue; }
                    if (riverTree && des < 0.3)
                    {
                        if (tr > 0.4) add("palm", px, y, pz, s, s, yaw, PALM, jit);
                        else if (med > 0.4) add("poplar", px, y, pz, s, s * 1.1, yaw, POPLAR, jit);
                        else add(v < 0.6 ? "willow" : "poplar", px, y, pz, s, s, yaw, v < 0.6 ? WILLOW : POPLAR, jit);
                        continue;
                    }
                    if (des > 0.45 || (st > 0.5 && !forest)) { bool pop = st > 0.4 && v < 0.12; add(pop ? "poplar" : "shrub", px, y, pz, s, s, yaw, pop ? POPLAR : SHRUB, jit); continue; }
                    if (tr > 0.45)
                    {
                        if (v < 0.22) add("palm", px, y, pz, s * 1.1, s * 1.1, yaw, PALM, jit);
                        else if (v < 0.34) add("bamboo", px, y, pz, s, s, yaw, BAMBOO, jit);
                        else add(v < 0.7 ? "broad" : "broad2", px, y, pz, s * 1.1, s * 1.2, yaw, TROP, jit);
                        continue;
                    }
                    if (med > 0.45)
                    {
                        if (v < 0.28) add("cypress", px, y, pz, s, s, yaw, CYPRESS, jit);
                        else if (v < 0.5) add("stonepine", px, y, pz, s, s, yaw, STONEPINE, jit);
                        else add("olive", px, y, pz, s * 0.9, s * 0.8, yaw, OLIVE, jit);
                        continue;
                    }
                    if (sav > 0.45 && !forest) { add(v < 0.75 ? "acacia" : "shrub", px, y, pz, s * 1.1, s, yaw, v < 0.75 ? ACACIA : SHRUB, jit); continue; }
                    if (bo > 0.4) { add(v < 0.62 ? "spruce" : v < 0.85 ? "birch" : "pine", px, y, pz, s, s * 1.12, yaw, v < 0.62 ? SPRUCE : v < 0.85 ? BIRCH : PINE, jit); continue; }
                    // 东亚 / 温带
                    if (px > japanX && east > 0.3)
                    {
                        if (v < 0.35) add("pine", px, y, pz, s, s, yaw, PINE, jit);
                        else if (v < 0.55) add("spruce", px, y, pz, s, s * 1.15, yaw, CEDAR, jit);
                        else if (v < 0.62) add("broad", px, y, pz, s, s, yaw, MAPLE, jit);
                        else add(v < 0.8 ? "broad" : "broad2", px, y, pz, s, s, yaw, DEC_S, jit);
                        continue;
                    }
                    double dryN = north * east;
                    if (h > 2.1) { add(v < 0.6 ? "pine" : "spruce", px, y, pz, s, s, yaw, v < 0.6 ? PINE : SPRUCE, jit); continue; }
                    if (dryN > 0.45)
                    {
                        if (v < 0.3) add("poplar", px, y, pz, s, s * 1.1, yaw, POPLAR, jit);
                        else if (v < 0.6) add("pine", px, y, pz, s, s, yaw, PINE, jit);
                        else add("broad", px, y, pz, s, s, yaw, DEC_N, jit);
                        continue;
                    }
                    if (z < 40 && v < 0.14) { add("bamboo", px, y, pz, s, s, yaw, BAMBOO, jit); continue; }
                    if (v < 0.24) add("pine", px, y, pz, s, s, yaw, PINE, jit);
                    else
                    {
                        var c = z < 45 ? CAMPHOR : DEC_S;
                        double k = WorldGeo.Clamp01(dryN * 1.4);
                        add(v < 0.62 ? "broad" : "broad2", px, y, pz, s, s, yaw, new[] { c[0] + (DEC_N[0] - c[0]) * k, c[1] + (DEC_N[1] - c[1]) * k, c[2] + (DEC_N[2] - c[2]) * k }, jit);
                    }
                }
                if (sub != null) sub(0.8f * xi / xs.Count);
                yield return null;
            }
            // 村边果树（新增文化 india / africa / moor 按 CultureBase 回退：天竺、非洲的村边是椰枣 / 椰树）
            foreach (var o in Hamlets)
            {
                for (int k = 0; k < 3; k++)
                {
                    double a = o.yaw + 1.3 + k * 1.9 + rnd.NextDouble() * 0.6, r = 1.3 + rnd.NextDouble() * 0.5;
                    double px = o.x + Math.Cos(a) * r, pz = o.z + Math.Sin(a) * r, h = SurfaceY(px, pz);
                    if (h < 0.45) continue;
                    string cb = WorldGeo.CultureBase(o.culture), cu = o.culture;
                    bool dry = cb == "persia" || cb == "arab" || cb == "tarim" || cb == "kushan";
                    string kind = cb == "seasia" || cb == "yi" || cb == "nanman" || cu == "africa" ? "palm" : dry ? (k != 0 ? "poplar" : "palm") : cb == "roman" || cu == "moor" ? (k != 0 ? "olive" : "cypress") : "broad";
                    var c = kind == "palm" ? PALM : kind == "poplar" ? POPLAR : kind == "olive" ? OLIVE : kind == "cypress" ? CYPRESS : VILLAGE_TREE;
                    add(kind, px, h - 0.04, pz, 0.75, 0.75, a, c, (rnd.NextDouble() - 0.5) * 0.12);
                }
            }
        }

        // 石块、灌丛与城外村舍（随机数 = SeededRandom(23)）
        public int RockCount, HouseCount;
        static readonly Dictionary<string, string> HouseStyle = new Dictionary<string, string>
        {
            { "han", "han" }, { "korea", "han" }, { "wa", "round" }, { "yi", "stilt" }, { "nanman", "stilt" }, { "seasia", "stilt" }, { "steppe", "yurt" }, { "tarim", "flat" }, { "kushan", "flat" },
            { "persia", "flat" }, { "arab", "flat" }, { "roman", "tile" }, { "sarmatian", "tile" }, { "celt", "round" }, { "german", "long" }, { "india", "flat" }, { "africa", "flat" }, { "moor", "flat" },
        };
        static readonly Dictionary<string, double[][]> RoofPal = new Dictionary<string, double[][]>
        {
            { "han", new[] { new[] { 0.36, 0.38, 0.42 }, new[] { 0.3, 0.32, 0.36 }, new[] { 0.42, 0.4, 0.38 }, new[] { 0.62, 0.52, 0.34 } } },
            { "tile", new[] { new[] { 0.72, 0.36, 0.24 }, new[] { 0.66, 0.32, 0.22 }, new[] { 0.76, 0.42, 0.28 } } },
            { "flat", new[] { new[] { 0.86, 0.76, 0.58 }, new[] { 0.8, 0.7, 0.52 }, new[] { 0.9, 0.82, 0.66 } } },
            { "round", new[] { new[] { 0.7, 0.6, 0.38 }, new[] { 0.62, 0.54, 0.34 } } },
            { "yurt", new[] { new[] { 0.95, 0.93, 0.86 }, new[] { 0.88, 0.84, 0.74 } } },
            { "stilt", new[] { new[] { 0.66, 0.56, 0.34 }, new[] { 0.58, 0.5, 0.3 } } },
            { "long", new[] { new[] { 0.62, 0.54, 0.32 }, new[] { 0.55, 0.48, 0.3 } } },
        };
        public IEnumerator PlaceScatter(List<Inst> rocks, List<Inst> shrubs, List<Inst> houses, Action<float> sub)
        {
            var rnd = new SeededRandom(23); var G = Geo; var D = Spec.Trees;
            int nRocks = 0;
            const double SP = 1.35;
            int xi = 0;
            for (double x = D.x0; x < D.x1; x += SP, xi++)
            {
                for (double z = D.z0; z < D.z1; z += SP)
                {
                    double px = x + (rnd.NextDouble() - 0.5) * 1.1, pz = z + (rnd.NextDouble() - 0.5) * 1.1, r1 = rnd.NextDouble();
                    if (r1 < 0.8) continue;
                    double h = SurfaceY(px, pz);
                    if (h < 0.15) continue;
                    const double e = 0.6;
                    double hx = (SurfaceY(px + e, pz) - SurfaceY(px - e, pz)) / (2 * e), hz = (SurfaceY(px, pz + e) - SurfaceY(px, pz - e)) / (2 * e);
                    double slope = 1 - 1 / Math.Sqrt(1 + hx * hx + hz * hz);
                    var f = G.Sample(px, pz);
                    double des = f[GeoFields.F_DESERT], st = f[GeoFields.F_STEPPE];
                    double p = 0.04 + 0.5 * SS(0.25, 0.55, slope) + 0.35 * SS(2.6, 4.8, h) + 0.25 * des + 0.1 * st;
                    if (h < 0.7 && G.CoastDist(px, pz) < 1.5) p += 0.25;
                    if (h > 5.6) p *= 0.3;
                    if (r1 < 1 - p * 0.2) continue;
                    if (NearCity(px, pz, 2.8)) continue;
                    if (G.RiverDist(px, pz) / G.rW < 1.2) continue;
                    int v = rnd.Next(3);
                    double s = (0.38 + rnd.NextDouble() * 0.55) * (1 + 0.4 * SS(3, 5, h)), yaw = rnd.NextDouble() * Math.PI * 2;
                    double warm = des * 0.8 + st * 0.3, k = 0.8 + rnd.NextDouble() * 0.25;
                    double cr = 0.5 + 0.12 * warm, cg = 0.48 + 0.03 * warm, cb = 0.45 - 0.08 * warm;
                    double sy = s * (0.7 + rnd.NextDouble() * 0.5);
                    rocks.Add(new Inst { kind = v == 2 ? "rock1" : "rock0", x = px, y = h - 0.06 * s, z = pz, s = s * (0.8 + 0.3 * v), sy = sy, yaw = yaw, r = (float)(cr * k), g = (float)(cg * k), b = (float)(cb * k) });
                    nRocks++;
                }
                if ((xi & 3) == 3) { if (sub != null) sub((float)(0.6 * (x - D.x0) / (D.x1 - D.x0))); yield return null; }
            }
            // 灌丛：草地、丘陵上零散分布（不在农田、林地、沙漠中心）
            xi = 0;
            for (double x = D.x0; x < D.x1; x += 1.6, xi++)
            {
                for (double z = D.z0; z < D.z1; z += 1.6)
                {
                    double px = x + (rnd.NextDouble() - 0.5) * 1.3, pz = z + (rnd.NextDouble() - 0.5) * 1.3, r1 = rnd.NextDouble();
                    if (r1 < 0.9) continue;
                    int ti = WorldGeo.JsRound((px - GX0) / GStep), tj = WorldGeo.JsRound((pz - GZ0) / GStep);
                    if (ti < 0 || tj < 0 || ti >= TexW || tj >= TexH) continue;
                    int q = (tj * TexW + ti) * 4;
                    double farm = 1 - MData[q + 3] / 255.0, sand = MData[q] / 255.0, snow = MData[q + 2] / 255.0, forest = CData[q + 3] / 255.0;
                    if (farm > 0.25 || snow > 0.3 || forest > 0.5 || sand > 0.7) continue;
                    double h = SurfaceY(px, pz);
                    if (h < 0.55 || h > 4.2) continue;
                    double p = 0.25 + 0.5 * SS(1.2, 3.0, h) + 0.25 * sand;
                    if (r1 < 1 - 0.1 * p) continue;
                    if (NearCity(px, pz, 2.8) || G.RiverDist(px, pz) / G.rW < 1.3) continue;
                    double k = 0.85 + rnd.NextDouble() * 0.3, s = 0.55 + rnd.NextDouble() * 0.6;
                    double cr = CData[q] / 255.0 * 0.62 * k, cg = CData[q + 1] / 255.0 * 0.72 * k, cb = CData[q + 2] / 255.0 * 0.55 * k;
                    double sy = s * (0.7 + rnd.NextDouble() * 0.5), yaw = rnd.NextDouble() * 6.28;
                    shrubs.Add(new Inst { kind = "shrub", x = px, y = h - 0.03, z = pz, s = s * 1.2, sy = sy, yaw = yaw, r = (float)cr, g = (float)cg, b = (float)cb });
                }
                if ((xi & 3) == 3) { if (sub != null) sub((float)(0.4 + 0.2 * (x - D.x0) / (D.x1 - D.x0))); yield return null; }
            }
            RockCount = nRocks;
            // 村落：每处 3–6 间，按文化取屋型与屋顶色
            int nHouses = 0;
            foreach (var o in Hamlets)
            {
                string style;
                if (!HouseStyle.TryGetValue(o.culture ?? "", out style) && !HouseStyle.TryGetValue(WorldGeo.CultureBase(o.culture), out style)) style = "han";
                var pal = RoofPal[style];
                var pts = new List<double[]>();
                for (int k = 0; k < o.n * 4 && pts.Count < o.n; k++)
                {
                    double a = rnd.NextDouble() * Math.PI * 2, r = Math.Sqrt(rnd.NextDouble()) * 1.15;
                    double px = o.x + Math.Cos(a) * r, pz = o.z + Math.Sin(a) * r;
                    bool near = false;
                    foreach (var pp in pts) if (Hyp(pp[0] - px, pp[1] - pz) < 0.5) { near = true; break; }
                    if (near) continue;
                    double h = SurfaceY(px, pz);
                    if (h < 0.4) continue;
                    pts.Add(new[] { px, pz, h });
                }
                foreach (var pp in pts)
                {
                    var c = pal[rnd.Next(pal.Length)]; double k = 0.92 + rnd.NextDouble() * 0.16;
                    double yaw = -o.yaw + Math.PI / 2 + (rnd.Next(2) != 0 ? 0 : Math.PI / 2) + (rnd.NextDouble() - 0.5) * 0.3;
                    double s = 0.9 + rnd.NextDouble() * 0.3;
                    houses.Add(new Inst { kind = style, x = pp[0], y = pp[2] - 0.02, z = pp[1], s = s, sy = s, yaw = yaw, r = (float)(c[0] * k), g = (float)(c[1] * k), b = (float)(c[2] * k) });
                    nHouses++;
                }
                if (sub != null) sub((float)(0.6 + 0.3 * nHouses / Math.Max(1, Hamlets.Count * 4)));
            }
            HouseCount = nHouses;
        }

        // ------------------------------------------------------ 道路、桥梁、关隘 --
        // 一条陆路（两城之间）：中心线各点 x, z（地图坐标）、h（地表）、y（路面）、nx / nz（左向单位向量）、v（纹理坐标）、gap（断开）
        public sealed class RoadLink
        {
            public int a, b, n;
            public double[] x, z, h, y, nx, nz, v; public bool[] gap;
            public float[] xz; public bool[] lineGap;   // 关隘定位用的折线（Float32，与网页版相同）
        }
        public sealed class BridgePlan { public double ax, az, bx, bz, deck; public bool stone; }
        public sealed class PassPlan { public string name, style; public double x, z, dx, dz, labelY; }
        public readonly List<RoadLink> Roads = new List<RoadLink>();
        public readonly List<BridgePlan> Bridges = new List<BridgePlan>();
        public readonly List<int[]> SeaPairs = new List<int[]>();
        public readonly List<PassPlan> Passes = new List<PassPlan>();
        public readonly List<string> PassesSkipped = new List<string>();
        double[] roadSegs; SegIndex roadIdx;
        public const double RoadHW = 0.27, RoadStep = 0.42, RoadLift = 0.055;
        static readonly HashSet<string> StoneBridge = new HashSet<string> { "han", "roman", "persia", "korea", "kushan", "arab", "sarmatian", "india", "africa" };

        // 道路：中心线（略带弯曲）、过水处架桥（两岸点上「离河距离」的梯度与道路方向基本相对才算横渡；L ≤ 5、不在海上），
        // 其余过水段断开；中心线分段（RoadDist / TerrainInfo）与折线（关隘定位）。isSea(a, b) 为真的连线记入 SeaPairs
        public IEnumerator PlanRoads(Func<int, int, bool> isSea, Action<float> sub)
        {
            Roads.Clear(); Bridges.Clear(); SeaPairs.Clear();
            var done = new HashSet<int>(); var segs = new List<double>();
            int work = 0, ci = 0, n0 = Cities.Count;
            foreach (var c in Cities)
            {
                ci++;
                if (work >= 400) { work = 0; if (sub != null) sub(0.5f * ci / n0); yield return null; }
                foreach (int li in c.links)
                {
                    if (li < 0 || li >= n0) continue;
                    var o = Cities[li];
                    int key = Math.Min(c.id, o.id) * 4096 + Math.Max(c.id, o.id);
                    if (!done.Add(key)) continue;
                    bool sea = false;
                    if (isSea != null) { try { sea = isSea(c.id, o.id); } catch (Exception) { sea = false; } }
                    if (sea) { SeaPairs.Add(new[] { Math.Min(c.id, o.id), Math.Max(c.id, o.id) }); continue; }
                    double ax = CityX[c.id], ay = CityZ[c.id], bx = CityX[o.id], by = CityZ[o.id];
                    double len = Hyp(bx - ax, by - ay);
                    int n = Math.Max(2, (int)Math.Ceiling(len / RoadStep));
                    double dx = (bx - ax) / len, dy = (by - ay) / len;
                    double bend = (Perlin(c.id, o.id) - 0.5) * len * 0.12;
                    var L = new RoadLink { a = c.id, b = o.id, n = n, x = new double[n + 1], z = new double[n + 1], h = new double[n + 1], y = new double[n + 1], nx = new double[n + 1], nz = new double[n + 1], v = new double[n + 1], gap = new bool[n + 1] };
                    for (int i = 0; i <= n; i++)
                    {
                        double f = (double)i / n, w = Math.Sin(f * Math.PI) * bend;
                        double px = ax + (bx - ax) * f - dy * w, pz = ay + (by - ay) * f + dx * w;
                        L.x[i] = px; L.z[i] = pz; L.h[i] = SurfaceY(px, pz);
                    }
                    for (int i = 0; i <= n; i++)
                    {
                        int i0 = Math.Max(0, i - 1), i1 = Math.Min(n, i + 1);
                        double tx = L.x[i1] - L.x[i0], tz = L.z[i1] - L.z[i0], tl = Hyp(tx, tz);
                        if (tl == 0) tl = 1;
                        L.nx[i] = -tz / tl; L.nz[i] = tx / tl;
                        L.y[i] = Math.Max(L.h[i], 0.1) + RoadLift;
                        L.v[i] = i > 0 ? L.v[i - 1] + Hyp(L.x[i] - L.x[i - 1], L.z[i] - L.z[i - 1]) * 0.7 : 0;
                    }
                    // 过水：连续的水面点（地表 < 0.06）
                    for (int i = 1; i < n; i++)
                    {
                        if (L.h[i] >= 0.06) continue;
                        int e = i;
                        while (e < n && L.h[e] < 0.06) e++;
                        int s0 = i - 1, s1 = e;
                        double Lw = Hyp(L.x[s1] - L.x[s0], L.z[s1] - L.z[s0]);
                        double mx = (L.x[s0] + L.x[s1]) / 2, mz = (L.z[s0] + L.z[s1]) / 2;
                        bool crossing = true;
                        {
                            var G0 = Geo; const double ee = 0.35;
                            if (G0.RiverDist(mx, mz) / G0.rW < 3)
                            {
                                double ll = Lw == 0 ? 1 : Lw;
                                double rx = (L.x[s1] - L.x[s0]) / ll, rz = (L.z[s1] - L.z[s0]) / ll;
                                Func<double, double, double[]> gr = (gx0, gz0) =>
                                {
                                    double gx = G0.RiverDist(gx0 + ee, gz0) - G0.RiverDist(gx0 - ee, gz0), gz = G0.RiverDist(gx0, gz0 + ee) - G0.RiverDist(gx0, gz0 - ee), l = Hyp(gx, gz);
                                    if (l == 0) l = 1;
                                    return new[] { gx / l, gz / l };
                                };
                                var g0 = gr(L.x[s0], L.z[s0]); var g1 = gr(L.x[s1], L.z[s1]);
                                crossing = (-(rx * g0[0] + rz * g0[1]) + (rx * g1[0] + rz * g1[1])) / 2 > 0.55;
                            }
                        }
                        if (Lw <= 5 && crossing && !Geo.InSea(mx, mz))
                        {
                            double deck = Math.Max(Math.Max(L.h[s0], L.h[s1]), 0.12) + 0.16;
                            for (int k = s0; k <= s1; k++) L.y[k] = Math.Max(L.y[k], deck + 0.03);
                            string culture = string.IsNullOrEmpty(c.culture) ? "han" : c.culture;
                            Bridges.Add(new BridgePlan { ax = L.x[s0], az = L.z[s0], bx = L.x[s1], bz = L.z[s1], deck = deck, stone = StoneBridge.Contains(culture) });
                        }
                        else for (int k = s0 + 1; k < s1; k++) L.gap[k] = true;
                        i = e;
                    }
                    // 中心线分段（供 RoadDist 使用）与折线（供关隘定位）
                    for (int k = 0; k < n; k += 4) { int q = Math.Min(n, k + 4); segs.Add(L.x[k]); segs.Add(L.z[k]); segs.Add(L.x[q]); segs.Add(L.z[q]); }
                    L.xz = new float[(n + 1) * 2]; L.lineGap = new bool[n + 1];
                    for (int k = 0; k <= n; k++)
                    {
                        L.xz[k * 2] = (float)L.x[k]; L.xz[k * 2 + 1] = (float)L.z[k];
                        L.lineGap[k] = L.gap[k] || L.h[k] < 0.1 || L.y[k] > L.h[k] + RoadLift + 0.01;
                    }
                    Roads.Add(L);
                    work += n + 1;
                }
            }
            roadSegs = segs.ToArray();
            roadIdx = SegIndex.Make(roadSegs, 2);
            yield return null;
        }

        // 关隘：名称、经度、纬度、样式（han = 中原关楼；west = 西方石门）
        public static readonly object[][] PassTable =
        {
            new object[] { "虎牢关", 113.25, 34.82, "han" }, new object[] { "函谷关", 111.0, 34.62, "han" }, new object[] { "剑阁", 105.55, 32.2, "han" }, new object[] { "散关", 106.95, 34.3, "han" },
            new object[] { "玉门关", 93.86, 40.35, "han" }, new object[] { "温泉关", 22.53, 38.8, "west" }, new object[] { "奇里乞亚门", 34.78, 37.28, "west" }, new object[] { "开伯尔山口", 71.08, 34.1, "west" },
        };
        // 关隘（第三版修订）：在史实位置 8 单位以内的道路点中挑选——离城 3.6 以上、不在桥上，两侧地势越高（鞍部）、离史实位置越近越好；
        // 附近没有道路则建在史实位置本身，门向顺着等高线。bounds：可玩范围（地图坐标）
        public void PlanPasses(MapRect bounds)
        {
            Passes.Clear(); PassesSkipped.Clear();
            Func<double, double, double, double, double, double> rise = (x, z, px, pz, y0) =>
            {
                double r = double.PositiveInfinity;
                for (int s = -1; s <= 1; s += 2)
                {
                    double best = -9;
                    foreach (double d in new[] { 1.2, 2.0, 2.8 }) best = Math.Max(best, SurfaceY(x + px * s * d, z + pz * s * d) - y0);
                    r = Math.Min(r, best);
                }
                return r;
            };
            foreach (var p in PassTable)
            {
                double qx, qy; MapProjection.Project((double)p[1], (double)p[2], out qx, out qy);
                if (qx < bounds.xMin - 5 || qx > bounds.xMax + 5 || qy < bounds.yMin - 5 || qy > bounds.yMax + 5) continue;
                PassPlan best = null; double bs = double.PositiveInfinity;
                foreach (var L in Roads)
                {
                    var xz = L.xz; int ln = L.n + 1;
                    for (int k = 1; k < ln - 1; k++)
                    {
                        if (L.lineGap[k] || L.lineGap[k - 1] || L.lineGap[k + 1]) continue;
                        double x = xz[k * 2], z = xz[k * 2 + 1], dq = Hyp(x - qx, z - qy);
                        if (dq > 8 || CityNearDist(x, z) < 3.6) continue;
                        double y0 = SurfaceY(x, z);
                        if (y0 < 0.12) continue;
                        double dx = (double)xz[k * 2 + 2] - xz[k * 2 - 2], dz = (double)xz[k * 2 + 3] - xz[k * 2 - 1];
                        double l = Hyp(dx, dz); if (l == 0) l = 1; dx /= l; dz /= l;
                        double sc = dq * 0.35 - WorldGeo.Clamp(rise(x, z, dz, -dx, y0), -1, 1.6) * 1.4;
                        if (sc < bs) { bs = sc; best = new PassPlan { x = x, z = z, dx = dx, dz = dz }; }
                    }
                }
                if (best == null && SurfaceY(qx, qy) > 0.12 && CityNearDist(qx, qy) >= 3.6)
                {
                    // 无路：门向顺着等高线（梯度的垂直方向）
                    const double e = 0.8;
                    double gx = SurfaceY(qx + e, qy) - SurfaceY(qx - e, qy), gz = SurfaceY(qx, qy + e) - SurfaceY(qx, qy - e);
                    double gl = Hyp(gx, gz);
                    best = gl > 1e-4 ? new PassPlan { x = qx, z = qy, dx = -gz / gl, dz = gx / gl } : new PassPlan { x = qx, z = qy, dx = 1, dz = 0 };
                }
                if (best == null) { PassesSkipped.Add((string)p[0]); continue; }
                best.name = (string)p[0]; best.style = (string)p[3] ?? "han";
                best.labelY = SurfaceY(best.x, best.z) + 1.0;
                Passes.Add(best);
            }
        }

        // 无头构建（BattleTerrain 等只需要 TerrainInfo / SurfaceY / RoadDist 时）：地形 + 道路，同步完成。
        // region = "auto" | "china" | "world"（与 MapView.Build 相同，auto 按城池位置判定）
        public static TerrainModel BuildHeadless(GameState g, string region)
        {
            var spec = MapSpec.ForRegion(MapSpec.Resolve(g, region));
            var geo = GeoFields.Get(spec);
            var tm = new TerrainModel(geo, spec, g.cities);
            GeoFields.Run(tm.GenTerrain(null));
            GeoFields.Run(tm.PlanRoads((a, b) => g.LinkIsSea(a, b), null));
            return tm;
        }

        // ------------------------------------------------------ 海路 --
        // 全图海域栅格（1 单位一格）：water = 海（含内海；不含河流湖泊，也不含高于 −0.3 的浅滩）；
        // cost = 水面 1、离陆地 2 格以内 1.5、陆地 14
        double sX0, sZ0; int sNx, sNz; byte[] sWater; float[] sCost;
        public IEnumerator BuildSeaMask()
        {
            const double res = 1.0; var T = Spec.Water;
            double X0 = Math.Floor(T.x0), Z0 = Math.Floor(T.z0);
            int nx = (int)Math.Ceiling(T.x1 - X0) + 1, nz = (int)Math.Ceiling(T.z1 - Z0) + 1, N = nx * nz;
            var water = new byte[N]; var cost = new float[N];
            for (int j = 0; j < nz; j++)
            {
                double z = Z0 + j * res;
                for (int i = 0; i < nx; i++) { double x = X0 + i * res; water[j * nx + i] = (byte)(Geo.InSea(x, z) && SurfaceY(x, z) < -0.3 ? 1 : 0); }
                if ((j & 1) == 1) yield return null;
            }
            // 陆地向外膨胀 2 格（先横后纵，可分离的最大值滤波）
            var tmp = new byte[N];
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    byte v = 0;
                    for (int d = -2; d <= 2 && v == 0; d++) { int ii = i + d; if (ii >= 0 && ii < nx && water[j * nx + ii] == 0) v = 1; }
                    tmp[j * nx + i] = v;
                }
                if ((j & 7) == 7) yield return null;
            }
            yield return null;
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    byte v = 0;
                    for (int d = -2; d <= 2 && v == 0; d++) { int jj = j + d; if (jj >= 0 && jj < nz && tmp[jj * nx + i] != 0) v = 1; }
                    int o = j * nx + i;
                    cost[o] = water[o] == 0 ? 14 : v != 0 ? 1.5f : 1;
                }
                if ((j & 7) == 7) yield return null;
            }
            sX0 = X0; sZ0 = Z0; sNx = nx; sNz = nz; sWater = water; sCost = cost;
            yield return null;
        }
        public void ReleaseSeaMask() { sWater = null; sCost = null; }

        // 海上航线：海域栅格上的 A*（水面代价 1，近岸略高，陆地 14），再按视线拉直、Chaikin 平滑。结果写入 outPts（x0, z0, x1, z1, …）
        public IEnumerator SeaPath(double ax, double az, double bx, double bz, List<double> outPts)
        {
            const double res = 1.0, margin = 28;
            double X0 = Math.Floor(Math.Min(ax, bx) - margin), Z0 = Math.Floor(Math.Min(az, bz) - margin);
            int nx = (int)Math.Ceiling((Math.Max(ax, bx) + margin - X0) / res) + 1, nz = (int)Math.Ceiling((Math.Max(az, bz) + margin - Z0) / res) + 1;
            int N = nx * nz;
            // 从全图海域栅格截取窗口；栅格外视为陆地
            var water = new byte[N]; var cost = new float[N];
            for (int o = 0; o < N; o++) cost[o] = 14;
            int oi = WorldGeo.JsRound((X0 - sX0) / res), oj = WorldGeo.JsRound((Z0 - sZ0) / res);
            for (int j = 0; j < nz; j++)
            {
                int gj = j + oj;
                if (gj >= 0 && gj < sNz)
                    for (int i = 0; i < nx; i++)
                    {
                        int gi = i + oi;
                        if (gi < 0 || gi >= sNx) continue;
                        int g = gj * sNx + gi;
                        water[j * nx + i] = sWater[g]; cost[j * nx + i] = sCost[g];
                    }
                if ((j & 31) == 31) yield return null;
            }
            Func<double, double, int> cell = (x, z) => (int)WorldGeo.Clamp(WorldGeo.JsRound((z - Z0) / res), 0, nz - 1) * nx + (int)WorldGeo.Clamp(WorldGeo.JsRound((x - X0) / res), 0, nx - 1);
            int start = cell(ax, az), goal = cell(bx, bz);
            int gx = goal % nx, gz = goal / nx;
            var gs = new float[N]; var from = new int[N]; var closed = new byte[N];
            for (int o = 0; o < N; o++) { gs[o] = float.PositiveInfinity; from[o] = -1; }
            // 二叉堆（与网页版相同的上浮 / 下沉顺序，路径逐格一致）
            var heap = new List<int>(); var hf = new List<double>();
            Action<int, double> push = (o, fv) =>
            {
                heap.Add(o); hf.Add(fv);
                int i = heap.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (hf[p] <= hf[i]) break;
                    int t = heap[p]; heap[p] = heap[i]; heap[i] = t; double tf = hf[p]; hf[p] = hf[i]; hf[i] = tf; i = p;
                }
            };
            Func<int> pop = () =>
            {
                int top = heap[0], last = heap.Count - 1;
                int lo = heap[last]; double lf = hf[last];
                heap.RemoveAt(last); hf.RemoveAt(last);
                if (heap.Count > 0)
                {
                    heap[0] = lo; hf[0] = lf;
                    int i = 0;
                    for (;;)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < heap.Count && hf[l] < hf[m]) m = l;
                        if (r < heap.Count && hf[r] < hf[m]) m = r;
                        if (m == i) break;
                        int t = heap[m]; heap[m] = heap[i]; heap[i] = t; double tf = hf[m]; hf[m] = hf[i]; hf[i] = tf; i = m;
                    }
                }
                return top;
            };
            gs[start] = 0;
            push(start, 0);
            int[] DI = { 1, -1, 0, 0, 1, 1, -1, -1 }, DJ = { 0, 0, 1, -1, 1, -1, 1, -1 };
            double[] DL = { 1, 1, 1, 1, Math.Sqrt(2), Math.Sqrt(2), Math.Sqrt(2), Math.Sqrt(2) };
            bool found = false; int pops = 0;
            while (heap.Count > 0)
            {
                int o = pop();
                if (closed[o] != 0) continue;
                closed[o] = 1;
                if ((++pops & 255) == 0) yield return null;
                if (o == goal) { found = true; break; }
                int i = o % nx, j = o / nx;
                for (int d = 0; d < 8; d++)
                {
                    int ii = i + DI[d], jj = j + DJ[d];
                    if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) continue;
                    int q = jj * nx + ii;
                    if (closed[q] != 0) continue;
                    double g = gs[o] + DL[d] * ((double)cost[o] + cost[q]) * 0.5;
                    if (g < gs[q]) { gs[q] = (float)g; from[q] = o; push(q, g + Math.Sqrt((double)(ii - gx) * (ii - gx) + (double)(jj - gz) * (jj - gz))); }
                }
            }
            var pts = new List<double>();
            if (found)
            {
                var rev = new List<double>();
                for (int o = goal; o >= 0; o = from[o]) { rev.Add(X0 + (o % nx) * res); rev.Add(Z0 + (o / nx) * res); }
                for (int k = rev.Count - 2; k >= 0; k -= 2) { pts.Add(rev[k]); pts.Add(rev[k + 1]); }
                pts[0] = ax; pts[1] = az; pts[pts.Count - 2] = bx; pts[pts.Count - 1] = bz;
                // 视线拉直：两点之间几乎全是水面（经过的陆地合计不超过 3 单位）时省去中间点
                Func<double, double, double, double, bool> clear = (x0, z0, x1, z1) =>
                {
                    int n = (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / (res * 0.5));
                    int landN = 0;
                    for (int s = 1; s < n; s++) if (water[cell(x0 + (x1 - x0) * s / n, z0 + (z1 - z0) * s / n)] == 0 && ++landN > 6) return false;
                    return true;
                };
                var outL = new List<double> { pts[0], pts[1] };
                int a = 0, n2 = pts.Count / 2, checks = 0;
                while (a < n2 - 1)
                {
                    int b = n2 - 1;
                    while (b > a + 1 && !clear(pts[2 * a], pts[2 * a + 1], pts[2 * b], pts[2 * b + 1])) { b--; if ((++checks & 255) == 0) yield return null; }
                    outL.Add(pts[2 * b]); outL.Add(pts[2 * b + 1]);
                    a = b;
                }
                pts = outL;
            }
            else { pts.Add(ax); pts.Add(az); pts.Add(bx); pts.Add(bz); }
            // Chaikin 平滑两遍（端点不动）
            for (int it = 0; it < 2 && pts.Count >= 6; it++)
            {
                var o = new List<double> { pts[0], pts[1] };
                for (int k = 0; k + 3 < pts.Count; k += 2)
                {
                    double x0 = pts[k], z0 = pts[k + 1], x1 = pts[k + 2], z1 = pts[k + 3];
                    o.Add(x0 * 0.75 + x1 * 0.25); o.Add(z0 * 0.75 + z1 * 0.25); o.Add(x0 * 0.25 + x1 * 0.75); o.Add(z0 * 0.25 + z1 * 0.75);
                }
                o.Add(pts[pts.Count - 2]); o.Add(pts[pts.Count - 1]);
                pts = o;
            }
            outPts.Clear(); outPts.AddRange(pts);
        }
    }
}
