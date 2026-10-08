// 三国志II 霸王的大陆 · 世界地理查询（第二版 §4G）
// ← 网页版 js/world-geo.js（地图范围、地区、投影后的地理数据缓存）+ js/map-view.js 中与渲染无关的部分
//   （地理场栅格化、地形高度 / 地表颜色、树木分布、海域栅格与海上航线）。
// 本文件只用 Mathf / Color / Vector2，不碰场景、网格与材质：Tests~/map 用 UnityStub 无头编译，
// 与 Node 运行网页版得到的参考值逐项比对（投影、距离场、生物群落取样、高度、树木、航线）。
//
// 公开接口
//   WorldGeo.Project(lon, lat) / Unproject(x, y)     → MapProjection（GameState.cs）的薄包装
//   WorldGeo.X0 Y0 X1 Y1 W H、Bounds、China           整图范围、经典剧本镜头范围（地图坐标）
//   WorldGeo.Regions[key] / RegionRect(key | 经纬度框)  镜头飞行用的地区（world china europe med arabia iran india tarim korea seasia）
//   WorldGeo.Data                                     投影后的地理数据（缓存）：陆地环、河流、湖泊、山脉、高原、生物群落
//   MapSpec.Classic() / MapSpec.World()               地图构建范围（与网页版 classicSpec / worldSpec 相同）
//   GeoFields.For(spec)                               地理场（按范围缓存）：CoastDist InSea RiverDist（RW / RD）Sample RidgeSum
//   TerrainModel                                      Height SurfaceY IsForest GroundColor PlaceTrees BuildSeaMask SeaPath
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
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise((float)(x * f + 31.7 * i), (float)(y * f + 17.3 * i)); n += a; a *= 0.5; f *= 2.03; }
            return s / n;
        }
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
                Water = new MapBox { x0 = x0 - 60, z0 = z0 - 60, x1 = x1 + 60, z1 = z1 + 60, step = 2.5, tile = 24, band = 20, far = 300 },
                Trees = new MapBox { x0 = x0 - 8, z0 = z0 - 8, x1 = x1 + 8, z1 = z1 + 8 },
                Tile = 100, Fine = 1.0, Coarse = 4, FieldRes = 2, Clouds = 18,
            };
        }
        public static MapSpec ForRegion(string region) { return region == "world" ? World() : Classic(); }
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
        static IEnumerator BoxBlur(float[] arr, int nx, int nz, int r, int passes)
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

    // ================================================================ 地形模型 ==
    // 网页版 MapView 中与渲染无关的部分：高度、实际地表高度、地表颜色、树木分布、海域栅格与航线
    public sealed class TerrainModel
    {
        public readonly GeoFields Geo;
        public readonly MapSpec Spec;
        public readonly bool World;
        public readonly double[] CityX, CityZ;
        // 地形网格（GenGrid 之后有效）：hs[i * (nz + 1) + j]
        public float[] Hs; public double GX0, GZ0, GStep; public int GNx, GNz;

        // 岩石 / 雪色：Unity 用 C# 原值（网页版没有色调映射，略压暗为 0.55,0.53,0.5 / 0.88,0.9,0.94；测试时改成网页版的值比对）
        public static double[] Rock = { 0.58, 0.56, 0.53 }, Snow = { 0.95, 0.96, 0.98 };

        // 城池索引：4 单位一格，记下半径 3.4 内的城池编号
        readonly double cix0, ciz0; readonly int cinx, cinz; readonly List<int>[] cib;

        public TerrainModel(GeoFields geo, MapSpec spec, double[] cityX, double[] cityZ)
        {
            Geo = geo; Spec = spec; World = spec.Key == "world"; CityX = cityX; CityZ = cityZ;
            const double radius = 3.4, cell = 4;
            if (cityX != null && cityX.Length > 0)
            {
                double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity, x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
                for (int i = 0; i < cityX.Length; i++) { x0 = Math.Min(x0, cityX[i]); x1 = Math.Max(x1, cityX[i]); z0 = Math.Min(z0, cityZ[i]); z1 = Math.Max(z1, cityZ[i]); }
                x0 -= radius + 1; z0 -= radius + 1; x1 += radius + 1; z1 += radius + 1;
                cix0 = x0; ciz0 = z0;
                cinx = (int)Math.Ceiling((x1 - x0) / cell) + 1; cinz = (int)Math.Ceiling((z1 - z0) / cell) + 1;
                cib = new List<int>[cinx * cinz];
                for (int i = 0; i < cityX.Length; i++)
                {
                    int i0 = (int)Math.Floor((cityX[i] - radius - x0) / cell), i1 = (int)Math.Floor((cityX[i] + radius - x0) / cell);
                    int j0 = (int)Math.Floor((cityZ[i] - radius - z0) / cell), j1 = (int)Math.Floor((cityZ[i] + radius - z0) / cell);
                    for (int a = i0; a <= i1; a++) for (int c = j0; c <= j1; c++) { int o = c * cinx + a; (cib[o] ?? (cib[o] = new List<int>())).Add(i); }
                }
            }
        }
        List<int> CitiesAt(double x, double z)
        {
            if (cib == null) return null;
            int a = (int)Math.Floor((x - cix0) / 4), c = (int)Math.Floor((z - ciz0) / 4);
            if (a < 0 || c < 0 || a >= cinx || c >= cinz) return null;
            return cib[c * cinx + a];
        }
        public bool NearCity(double x, double z, double r)
        {
            var list = CitiesAt(x, z);
            if (list == null) return false;
            foreach (int i in list) { double dx = CityX[i] - x, dz = CityZ[i] - z; if (Math.Sqrt(dx * dx + dz * dz) < r) return true; }
            return false;
        }

        // ------------------------------------------------------ 高度 --
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
                if (P > 0.001 && wOld < 1) h += (1 - wOld) * P * (0.7 + n * 0.6);
                // 沙丘
                double des = f[GeoFields.F_DESERT];
                if (des > 0.02) h += des * (WorldGeo.Fbm(x * 0.3 + 40, z * 0.3 + 40, 2) - 0.45) * 0.8;
                // 山脉
                h += G.RidgeSum(x, z);
            }
            // 河流与湖泊（宽度系数 rW、河床深度 rD；旧版为 1 与 −0.35）
            double rd = G.RiverDist(x, z) / G.rW;
            if (rd < 2.2 && !sea)
            {
                double d = G.rD;
                double target = rd < 0.75 ? d : WorldGeo.Lerp(d * (0.1 / 0.35), h, (rd - 0.75) / 1.45);
                h = Math.Min(h, target);
            }
            // 城市周围平整
            var list = CitiesAt(x, z);
            if (list != null)
                for (int q = 0; q < list.Count; q++)
                {
                    int i = list[q];
                    double d = Math.Sqrt((x - CityX[i]) * (x - CityX[i]) + (z - CityZ[i]) * (z - CityZ[i]));
                    if (d < 2.6) { double k = WorldGeo.Smooth(1, 0, Math.Max(0, d - 1.2) / 1.4); h = WorldGeo.Lerp(h, WorldGeo.Clamp(h, 0.5, 1.2), k); }
                }
            return h;
        }

        // 地形网格的全部高度（地形阶段的前 60%）；sub(0..1) 报告进度
        public IEnumerator GenGrid(Action<float> sub)
        {
            var T = Spec.Terrain; double step = T.step, x0 = T.x0, z0 = T.z0;
            int nx = (int)Math.Ceiling((T.x1 - x0) / step), nz = (int)Math.Ceiling((T.z1 - z0) / step);
            int W = nz + 1;
            var hs = new float[(nx + 1) * W];
            for (int i = 0; i <= nx; i++)
            {
                for (int j = 0; j <= nz; j++)
                {
                    hs[i * W + j] = (float)Height(x0 + i * step, z0 + j * step);
                    if ((j & 127) == 127) yield return null;
                }
                if (sub != null) sub((float)i / nx);
                yield return null;
            }
            Hs = hs; GX0 = x0; GZ0 = z0; GStep = step; GNx = nx; GNz = nz;
        }

        // 实际渲染出的地表高度（按地形三角形插值）；超出地形网格时退回 Height()
        public double SurfaceY(double x, double z)
        {
            if (Hs == null) return Height(x, z);
            double fx0 = (x - GX0) / GStep, fz0 = (z - GZ0) / GStep;
            int i = (int)Math.Floor(fx0), j = (int)Math.Floor(fz0);
            if (i < 0 || j < 0 || i >= GNx || j >= GNz) return Height(x, z);
            double fx = fx0 - i, fz = fz0 - j; int W = GNz + 1; var hs = Hs;
            double ha = hs[i * W + j], hb = hs[i * W + j + 1], hc = hs[(i + 1) * W + j + 1], hd = hs[(i + 1) * W + j];
            if (((i + j) & 1) == 0)
            {
                if (fz >= fx) return ha + fz * (hb - ha) + fx * (hc - hb);
                return ha + fx * (hd - ha) + fz * (hc - hd);
            }
            if (fx + fz <= 1) return ha + fx * (hd - ha) + fz * (hb - ha);
            return hc + (1 - fx) * (hb - hc) + (1 - fz) * (hd - hc);
        }
        public bool IsForest(double x, double z, double h) { return h >= 0.6 && h < 3.2 && WorldGeo.Fbm(x * 0.09 + 100, z * 0.09 + 100, 3) > 0.6; }

        // ------------------------------------------------------ 地表颜色 --
        static readonly double[] LUSH = { 0.36, 0.62, 0.3 }, DRY = { 0.66, 0.64, 0.38 }, STEPPE = { 0.62, 0.55, 0.36 };
        public static readonly double[] SEABED = { 0.7, 0.66, 0.5 };
        static readonly double[] SAND = { 0.86, 0.8, 0.6 }, FOREST = { 0.22, 0.45, 0.24 }, UPLAND = { 0.56, 0.52, 0.4 };
        // 第二版的世界群落色
        static readonly double[] DESERT = { 0.84, 0.72, 0.47 }, STRAW = { 0.72, 0.66, 0.41 }, TROPIC = { 0.2, 0.55, 0.22 }, SAVANNA = { 0.62, 0.61, 0.34 };
        static readonly double[] MED_OLIVE = { 0.53, 0.58, 0.34 }, TAIGA = { 0.27, 0.45, 0.31 };
        // 世界高原（青藏、帕米尔、伊朗……）：高寒草甸 / 荒原的黄褐色
        static readonly double[] ALPINE = { 0.6, 0.55, 0.4 };
        static readonly double[] TROP_FOREST = { 0.12, 0.4, 0.16 }, TAIGA_FOREST = { 0.15, 0.32, 0.23 };
        readonly double[] gc = new double[3], grass = new double[3], fcol = new double[3], rock = new double[3];

        static void LerpInto(double[] c, double[] b, double t)
        {
            t = WorldGeo.Clamp01(t);
            c[0] += (b[0] - c[0]) * t; c[1] += (b[1] - c[1]) * t; c[2] += (b[2] - c[2]) * t;
        }
        static void Copy(double[] dst, double[] src) { dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2]; }
        // 沙漠在河流 / 湖泊两岸让位给绿洲
        double Oasis(double x, double z) { return 0.2 + 0.8 * WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(1.0, 4.5, Geo.RiverDist(x, z))); }

        public Color GroundColor(double x, double z, double h, double slope, double jitter, bool noForest)
        {
            var f = Geo.Sample(x, z);
            double north = WorldGeo.InvLerp(10, 95, z);
            Copy(grass, LUSH);
            LerpInto(grass, DRY, north * 0.85 * f[GeoFields.F_EAST]);
            LerpInto(grass, STEPPE, WorldGeo.InvLerp(30, 5, x) * 0.6 * f[GeoFields.F_OLD]);
            double med = f[GeoFields.F_MED], sav = f[GeoFields.F_SAV], st = f[GeoFields.F_STEPPE], bo = f[GeoFields.F_BOREAL], tr = f[GeoFields.F_TROP];
            double des = f[GeoFields.F_DESERT];
            double fOld = f[GeoFields.F_OLD], fPlat = f[GeoFields.F_PLAT];
            if (med > 0.001) LerpInto(grass, MED_OLIVE, med * 0.75);
            if (sav > 0.001) LerpInto(grass, SAVANNA, sav * 0.75);
            double oa = des > 0.001 || st > 0.001 ? Oasis(x, z) : 1;
            if (st > 0.001) LerpInto(grass, STRAW, st * 0.85 * (0.45 + 0.55 * oa));
            if (bo > 0.001) LerpInto(grass, TAIGA, bo * 0.8);
            if (tr > 0.001) LerpInto(grass, TROPIC, tr * 0.85);
            if (des > 0.001) { des *= oa; LerpInto(grass, DESERT, des); }
            var c = gc;
            Copy(c, h < -0.15 ? SEABED : h < 0.42 ? SAND : grass);
            double forest = noForest ? 0 : WorldGeo.Fbm(x * 0.09 + 100, z * 0.09 + 100, 3);
            double thr = 0.56 + des * 0.3 + st * 0.12 + sav * 0.05 - tr * 0.06 - bo * 0.05;
            if (h >= 0.42 && h < 3.5 && forest > thr)
            {
                var fc = FOREST;
                if (tr > 0.001 || bo > 0.001) { fc = fcol; Copy(fc, FOREST); LerpInto(fc, TROP_FOREST, tr); LerpInto(fc, TAIGA_FOREST, bo); }
                LerpInto(c, fc, WorldGeo.InvLerp(thr, thr + 0.08, forest));
            }
            // 旧版范围（wOld = 1）与旧版完全相同；范围外的高原：岩色偏向高寒荒原，雪线抬高 1.6
            double wNew = 1 - fOld, plat = fPlat / 3;
            if (World)
            {
                // 世界地图：旧版西部高原（x < 22）也按高寒荒原着色，与西边的青藏高原连成一体
                double wp = WorldGeo.Smooth(0, 1, WorldGeo.InvLerp(24, 8, x));
                if (wp > 0) { wNew = Math.Max(wNew, 0.9 * wp); plat = Math.Max(plat, wp); }
            }
            double alp = wNew > 0.001 ? wNew * WorldGeo.Clamp01(plat) : 0;
            if (h > 2.2) LerpInto(c, UPLAND, WorldGeo.InvLerp(2.2, 3.6, h));
            if (h > 3.6 || slope > 0.62)
            {
                var rk = Rock;
                if (alp > 0) { rk = rock; Copy(rk, Rock); LerpInto(rk, ALPINE, alp * (1 - WorldGeo.InvLerp(0.55, 0.8, slope))); }
                LerpInto(c, rk, Math.Max(WorldGeo.InvLerp(3.6, 5, h), WorldGeo.InvLerp(0.62, 0.8, slope)));
            }
            double snow = 6.2 + 1.6 * wNew;
            if (h > snow) LerpInto(c, Snow, WorldGeo.InvLerp(snow, snow + 1, h));
            // Art.Shade(c, jitter)
            if (jitter >= 0) return new Color((float)(c[0] + (1 - c[0]) * jitter), (float)(c[1] + (1 - c[1]) * jitter), (float)(c[2] + (1 - c[2]) * jitter), 1);
            double kk = 1 + jitter;
            return new Color((float)(c[0] * kk), (float)(c[1] * kk), (float)(c[2] * kk), 1);
        }

        // ------------------------------------------------------ 树木 --
        // 树种：broad0 / broad1（阔叶两种外形）| pine | palm | shrub | cypress
        public const int Broad0 = 0, Broad1 = 1, Pine = 2, Palm = 3, Shrub = 4, Cypress = 5, KindCount = 6;
        public struct TreeInst { public int kind; public float x, y, z, s, sy, yaw; public Color col; }

        static readonly double[] T_PINE = { 0.2, 0.42, 0.28 }, T_DEC_S = { 0.3, 0.58, 0.26 }, T_DEC_N = { 0.5, 0.6, 0.28 };
        static readonly double[] T_BOREAL = { 0.15, 0.36, 0.25 }, T_TROP = { 0.14, 0.48, 0.18 }, T_PALM = { 0.3, 0.58, 0.2 }, T_OLIVE = { 0.47, 0.54, 0.34 };
        static readonly double[] T_CYPRESS = { 0.15, 0.33, 0.2 }, T_SHRUB = { 0.55, 0.52, 0.3 }, T_ACACIA = { 0.42, 0.5, 0.22 };
        static Color ShadeC(double[] c, double k)
        {
            if (k >= 0) { double t = Math.Min(1, k); return new Color((float)(c[0] + (1 - c[0]) * t), (float)(c[1] + (1 - c[1]) * t), (float)(c[2] + (1 - c[2]) * t), 1); }
            double s = 1 - Math.Min(1, -k);
            return new Color((float)(c[0] * s), (float)(c[1] * s), (float)(c[2] * s), 1);
        }

        // 树木分布；rnd = 取 [0, 1) 随机数（Unity 用 System.Random(11)，测试注入网页版的 SeededRandom）。
        // 树种与叶色随群落变化（中国本部与旧版相同）；随机数的取用顺序与旧版相同
        public IEnumerator PlaceTrees(Func<double> rnd, List<TreeInst> outList, Action<float> sub)
        {
            var D = Spec.Trees;
            bool classic = !World;
            var xs = new List<double>();
            for (double x = D.x0; x < D.x1; x += 1.15) xs.Add(x);
            int count = 0;
            for (int xi = 0; xi < xs.Count; xi++)
            {
                double x = xs[xi];
                for (double z = D.z0; z < D.z1; z += 1.15)
                {
                    double px = x + (rnd() - 0.5) * 0.9, pz = z + (rnd() - 0.5) * 0.9;
                    // 经典地图按 Height() 判定（与旧版相同）；世界地图用便宜得多的 SurfaceY()
                    double h = classic ? Height(px, pz) : SurfaceY(px, pz);
                    if (h <= 0.5 || h >= 3.2) continue;
                    var f = Geo.Sample(px, pz);
                    double des = f[GeoFields.F_DESERT], st = f[GeoFields.F_STEPPE], sav = f[GeoFields.F_SAV], med = f[GeoFields.F_MED], tr = f[GeoFields.F_TROP], bo = f[GeoFields.F_BOREAL];
                    double fEast = f[GeoFields.F_EAST];
                    bool biome = des + st + sav + med + tr + bo > 0.02;
                    double thr = biome ? 0.6 + des * 0.4 + st * 0.15 + sav * 0.04 - tr * 0.07 - bo * 0.05 : 0.6;
                    bool forest = h >= 0.6 && WorldGeo.Fbm(px * 0.09 + 100, pz * 0.09 + 100, 3) > thr;
                    double pSparse = biome ? Math.Max(0, 0.025 * (1 - 0.9 * des - 0.6 * st) + 0.035 * sav + 0.02 * med) : 0.025;
                    bool sparse = h < 2.5 && rnd() < pSparse;
                    bool oasisTree = false;
                    if (!forest && !sparse && des > 0.35 && h < 1.6)
                    {
                        double rd = Geo.RiverDist(px, pz) / Geo.rW;
                        oasisTree = rd > 1.3 && rd < 3.2 && rnd() < 0.3;
                    }
                    if (!forest && !sparse && !oasisTree) continue;
                    if (NearCity(px, pz, 3.4)) continue;
                    if (Geo.RiverDist(px, pz) < 1.4) continue;
                    double north = WorldGeo.InvLerp(10, 95, pz);
                    double v = rnd();
                    double s = 0.9 + rnd() * 0.6;
                    double jit = (rnd() - 0.5) * 0.12;
                    double hy = Math.Sin(px * 12.9898 + pz * 78.233) * 43758.5453;
                    double y = (classic ? SurfaceY(px, pz) : h) - 0.05, yaw = (hy - Math.Floor(hy)) * Math.PI * 2;
                    int kind; double sx = s, sy = s; Color col;
                    if (oasisTree || (des > 0.5 && v < 0.35)) { kind = Palm; sx = sy = s * 1.05; col = ShadeC(T_PALM, jit); }
                    else if (des > 0.45 || (st > 0.5 && !forest)) { kind = Shrub; col = ShadeC(T_SHRUB, jit); }
                    else if (tr > 0.45)
                    {
                        if (v < 0.3) { kind = Palm; sx = sy = s * 1.1; col = ShadeC(T_PALM, jit); }
                        else { kind = v < 0.65 ? Broad0 : Broad1; sx = s * 1.1; sy = s * 1.2; col = ShadeC(T_TROP, jit); }
                    }
                    else if (med > 0.45)
                    {
                        if (v < 0.3) { kind = Cypress; col = ShadeC(T_CYPRESS, jit); }
                        else { kind = v < 0.65 ? Broad0 : Broad1; sx = s * 0.8; sy = s * 0.6; col = ShadeC(T_OLIVE, jit); }
                    }
                    else if (sav > 0.45 && !forest) { kind = Broad1; sx = s * 1.25; sy = s * 0.55; col = ShadeC(T_ACACIA, jit); }
                    else if (bo > 0.4) { kind = Pine; sy = s * 1.15; col = ShadeC(T_BOREAL, jit); }
                    else
                    {
                        bool pine = v < 0.25 + north * 0.6 * fEast || h > 2;
                        if (pine) { kind = Pine; col = ShadeC(T_PINE, jit); }
                        else
                        {
                            kind = (count & 1) != 0 ? Broad1 : Broad0;
                            double t = WorldGeo.Clamp01(north * fEast);
                            var dc = new[] { T_DEC_S[0] + (T_DEC_N[0] - T_DEC_S[0]) * t, T_DEC_S[1] + (T_DEC_N[1] - T_DEC_S[1]) * t, T_DEC_S[2] + (T_DEC_N[2] - T_DEC_S[2]) * t };
                            col = ShadeC(dc, jit);
                        }
                    }
                    outList.Add(new TreeInst { kind = kind, x = (float)px, y = (float)y, z = (float)pz, s = (float)sx, sy = (float)sy, yaw = (float)yaw, col = col });
                    count++;
                }
                if ((xi & 1) == 1) { if (sub != null) sub(0.9f * xi / xs.Count); yield return null; }
            }
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
