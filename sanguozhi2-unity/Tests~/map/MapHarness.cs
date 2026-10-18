// 无头地图测试（第三版）：View/WorldGeo.cs（投影范围、地理场、地形模型、村落、树木 / 石块 / 灌丛 / 村舍、道路 / 关隘、
// TerrainInfo、海上航线）与网页版的参考值逐项比对。参考值由 Tests~/map/refs.js 在 Node 中运行网页版生成（refs.txt + *.bin）。
// 城池表取自参考值（与 C# 剧本数据无关）：C# 的 TerrainModel 用与网页版完全相同的城池构建。
// 用法：mono map.exe <参考值目录>        退出码 = 失败的检查项数
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Sanguo;

static class MapHarness
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int fails;
    static double P(string s) { return double.Parse(s, Inv); }
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  OK   " : "  FAIL ") + name + "：" + detail);
        if (!ok) fails++;
    }
    static float[] F32(string path) { var b = File.ReadAllBytes(path); var f = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, f, 0, b.Length); return f; }

    // 参考值的城池表 → City 对象（经纬度按 float 存，与 C# 剧本数据相同；WorldGeo.CityPos 先还原成十进制 double）
    public static List<City> CitiesFrom(List<string[]> rows, string which)
    {
        var list = new List<City>();
        foreach (var r in rows.Where(r => r[0] == "city" && r[1] == which))
        {
            var c = new City { id = int.Parse(r[2]), key = r[3], name = r[3], lon = float.Parse(r[4], Inv), lat = float.Parse(r[5], Inv), town = int.Parse(r[6]), culture = r[7] };
            if (r[8] != "-") foreach (var s in r[8].Split(',')) c.links.Add(int.Parse(s));
            list.Add(c);
        }
        return list;
    }
    public static HashSet<int> SeaFrom(List<string[]> rows, string which)
    {
        var h = new HashSet<int>();
        var r = rows.First(x => x[0] == "sealinks" && x[1] == which);
        for (int k = 3; k < r.Length; k++) { var ab = r[k].Split('-'); int a = int.Parse(ab[0]), b = int.Parse(ab[1]); h.Add(Math.Min(a, b) * 4096 + Math.Max(a, b)); }
        return h;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "../out/map";
        var rows = File.ReadAllLines(Path.Combine(dir, "refs.txt")).Where(l => l.Length > 0).Select(l => l.Split(' ')).ToList();
        var sw = Stopwatch.StartNew();

        // ---------------------------------------------------- 投影 --
        double eP = 0, eU = 0; int nP = 0;
        foreach (var r in rows.Where(r => r[0] == "proj"))
        {
            double x, y, lo, la;
            MapProjection.Project(P(r[1]), P(r[2]), out x, out y);
            MapProjection.Unproject(P(r[3]), P(r[4]), out lo, out la);
            eP = Math.Max(eP, Math.Max(Math.Abs(x - P(r[3])), Math.Abs(y - P(r[4]))));
            eU = Math.Max(eU, Math.Max(Math.Abs(lo - P(r[5])), Math.Abs(la - P(r[6]))));
            nP++;
        }
        Check("投影 project / unproject", nP > 100 && eP < 1e-9 && eU < 1e-9, string.Format("{0} 点，最大误差 {1:E1} / {2:E1}", nP, eP, eU));
        var b = rows.First(r => r[0] == "bounds");
        double eb = Math.Max(Math.Max(Math.Abs(WorldGeo.X0 - P(b[1])), Math.Abs(WorldGeo.Y0 - P(b[2]))), Math.Max(Math.Abs(WorldGeo.X1 - P(b[3])), Math.Abs(WorldGeo.Y1 - P(b[4]))));
        Check("整图范围", eb < 1e-9, string.Format("({0}, {1}) – ({2}, {3})，误差 {4:E1}", WorldGeo.X0, WorldGeo.Y0, WorldGeo.X1, WorldGeo.Y1, eb));
        double er = 0; int nr = 0;
        foreach (var r in rows.Where(r => r[0] == "region"))
        {
            MapRect rc;
            if (!WorldGeo.TryRegionRect(r[1], out rc)) { er = 999; continue; }
            er = Math.Max(er, Math.Max(Math.Max(Math.Abs(rc.xMin - P(r[2])), Math.Abs(rc.yMin - P(r[3]))), Math.Max(Math.Abs(rc.xMax - P(r[4])), Math.Abs(rc.yMax - P(r[5])))));
            nr++;
        }
        Check("镜头地区", nr == WorldGeoData.Regions.Length && er < 1e-3, nr + " 个，最大误差 " + er.ToString("E1"));
        double ePer = 0; int nPer = 0;
        foreach (var r in rows.Where(r => r[0] == "perlin")) { ePer = Math.Max(ePer, Math.Abs(WorldGeo.Perlin(P(r[1]), P(r[2])) - P(r[3]))); nPer++; }
        Check("Perlin（网页版 core.js）", nPer > 100 && ePer < 1e-12, nPer + " 点，最大误差 " + ePer.ToString("E1"));

        // ---------------------------------------------------- 两种范围 --
        foreach (var which in new[] { "china", "world" })
        {
            Console.WriteLine("== " + which);
            var spec = MapSpec.ForRegion(which);
            var specRow = rows.First(r => r[0] == "spec" && r[1] == which);
            int si = rows.IndexOf(specRow), ei = rows.FindIndex(si + 1, r => r[0] == "spec"); if (ei < 0) ei = rows.Count;
            var R = rows.GetRange(si, ei - si);
            var t0 = sw.ElapsedMilliseconds;
            var geo = GeoFields.Get(spec);
            long tGeo = sw.ElapsedMilliseconds - t0;
            var cities = CitiesFrom(rows, which);
            var sea = SeaFrom(rows, which);
            Check("城池数", cities.Count == int.Parse(specRow[2]), cities.Count + " / " + specRow[2]);
            {
                // 区域判定（网页版 _spec）：auto / 空 按城池位置；china / world 原样
                var gs = new GameState(); gs.cities = cities;
                string ra = MapSpec.Resolve(gs, "auto"), rn = MapSpec.Resolve(gs, null), rc = MapSpec.Resolve(gs, "china"), rw = MapSpec.Resolve(gs, "world");
                Check("区域判定 MapSpec.Resolve", ra == which && rn == which && rc == "china" && rw == "world", "auto → " + ra + "，空 → " + rn + "，china → " + rc + "，world → " + rw);
            }
            var tm = new TerrainModel(geo, spec, cities);

            // 采样点：地理场与程序化高度（构建前），实际地表（构建后）
            var pts = R.Where(r => r[0] == "pt").ToList();
            double eCoast = 0, eRiver = 0, eF = 0, eRidge = 0, eH = 0; int seaBad = 0, rwBad = 0, hBad = 0;
            foreach (var r in pts)
            {
                double x = P(r[1]), z = P(r[2]);
                eCoast = Math.Max(eCoast, Math.Abs(geo.CoastDist(x, z) - P(r[3])));
                if ((geo.InSea(x, z) ? 1 : 0) != int.Parse(r[4])) seaBad++;
                eRiver = Math.Max(eRiver, Math.Abs(geo.RiverDist(x, z) - P(r[5])));
                if (Math.Abs(geo.rW - P(r[6])) > 1e-6 || Math.Abs(geo.rD - P(r[7])) > 1e-6) rwBad++;
                var f = geo.Sample(x, z);
                for (int k = 0; k < GeoFields.NF; k++) eF = Math.Max(eF, Math.Abs(f[k] - P(r[8 + k])));
                eRidge = Math.Max(eRidge, Math.Abs(geo.RidgeSum(x, z) - P(r[17])));
                double dh = Math.Abs(tm.Height(x, z) - P(r[18]));
                eH = Math.Max(eH, dh); if (dh > 1e-4) hBad++;
            }
            Check("海岸距离", eCoast < 1e-6, pts.Count + " 点，最大误差 " + eCoast.ToString("E1"));
            Check("陆 / 海判定", seaBad == 0, "不一致 " + seaBad);
            Check("河流距离与宽度 / 深度", eRiver < 1e-6 && rwBad == 0, "最大误差 " + eRiver.ToString("E1") + "，宽深不一致 " + rwBad);
            Check("平滑场取样（高原、生物群落、旧版权重）", eF < 1e-5, "最大误差 " + eF.ToString("E1"));
            Check("山脉", eRidge < 2e-4, "最大误差 " + eRidge.ToString("E1"));
            Check("高度 Height", hBad == 0, "最大误差 " + eH.ToString("E1") + "，> 1e-4 的点 " + hBad);

            // 地形阶段
            t0 = sw.ElapsedMilliseconds;
            GeoFields.Run(tm.GenTerrain(null));
            long tTer = sw.ElapsedMilliseconds - t0;
            Console.WriteLine(string.Format("  （地理场 {0} ms，地形阶段 {1} ms：主网格 {2}×{3}，远景 {4}×{5}）", tGeo, tTer, tm.MNx, tm.MNz, tm.ONx, tm.ONz));
            // 城外村落
            var hj = R.Where(r => r[0] == "hamlet").ToList();
            int hamOk = 0;
            for (int i = 0; i < Math.Min(hj.Count, tm.Hamlets.Count); i++)
            {
                var r = hj[i]; var h = tm.Hamlets[i];
                if (Math.Abs(h.x - P(r[2])) < 1e-9 && Math.Abs(h.z - P(r[3])) < 1e-9 && h.culture == r[4] && h.n == int.Parse(r[5]) && h.city == int.Parse(r[7])) hamOk++;
            }
            Check("城外村落", hj.Count == tm.Hamlets.Count && hamOk == hj.Count, tm.Hamlets.Count + " 处（网页版 " + hj.Count + "），逐个一致 " + hamOk);
            // 主网格 / 远景栅格
            var mr = R.First(r => r[0] == "master");
            bool dimOk = Math.Abs(tm.MX0 - P(mr[2])) < 1e-9 && Math.Abs(tm.MZ0 - P(mr[3])) < 1e-9 && tm.MNx == int.Parse(mr[5]) && tm.MNz == int.Parse(mr[6])
                && Math.Abs(tm.OX0 - P(mr[7])) < 1e-9 && Math.Abs(tm.OZ0 - P(mr[8])) < 1e-9 && tm.ONx == int.Parse(mr[10]) && tm.ONz == int.Parse(mr[11]);
            Action<string, float[], float[], double> cmpGrid = (label, mine, js, tol) =>
            {
                int bad = 0; double e = 0;
                if (mine == null || mine.Length != js.Length) { Check(label, false, "尺寸不一致 " + (mine == null ? -1 : mine.Length) + " / " + js.Length); return; }
                for (int i = 0; i < js.Length; i++) { double d = Math.Abs(mine[i] - js[i]); if (d > e) e = d; if (d > tol) bad++; }
                Check(label, dimOk && bad <= js.Length / 5000, string.Format("{0} 个节点，最大误差 {1:E1}，> {2} 的 {3}", js.Length, e, tol, bad));
            };
            cmpGrid("远景栅格（5.0）", tm.OH, F32(Path.Combine(dir, "oh-" + which + ".bin")), 1e-4);
            cmpGrid("主网格（0.625，平滑 + 细节噪声 + 去尖 + 城址压平）", tm.MH, F32(Path.Combine(dir, "mh-" + which + ".bin")), 1e-4);
            double eBase = 0;
            foreach (var r in R.Where(r => r[0] == "citybase")) eBase = Math.Max(eBase, Math.Abs(tm.CityBase[int.Parse(r[2])] - P(r[3])));
            Check("城基高度", eBase < 1e-4, "最大误差 " + eBase.ToString("E1"));
            // 数据纹理（字节；±1 为舍入边界，允许）
            var tsz = R.First(r => r[0] == "texsize");
            Check("数据纹理尺寸", tm.TexW == int.Parse(tsz[2]) && tm.TexH == int.Parse(tsz[3]), tm.TexW + "×" + tm.TexH + " / " + tsz[2] + "×" + tsz[3]);
            foreach (var kv in new[] { new KeyValuePair<string, byte[]>("n", tm.NData), new KeyValuePair<string, byte[]>("c", tm.CData), new KeyValuePair<string, byte[]>("m", tm.MData), new KeyValuePair<string, byte[]>("oc", tm.OCData), new KeyValuePair<string, byte[]>("om", tm.OMData) })
            {
                var js = File.ReadAllBytes(Path.Combine(dir, "tex" + kv.Key + "-" + which + ".bin"));
                var mine = kv.Value;
                int big = 0, one = 0;
                if (mine == null || mine.Length != js.Length) { Check("数据纹理 " + kv.Key, false, "尺寸不一致"); continue; }
                for (int i = 0; i < js.Length; i++) { int d = Math.Abs(mine[i] - js[i]); if (d > 1) big++; else if (d == 1) one++; }
                Check("数据纹理 " + kv.Key, big <= js.Length / 5000, string.Format("{0} 字节，差 1 的 {1}，差 > 1 的 {2}", js.Length, one, big));
            }

            // 实际地表（构建后）
            double eS = 0; int sBad = 0;
            foreach (var r in pts) { double d = Math.Abs(tm.SurfaceY(P(r[1]), P(r[2])) - P(r[19])); eS = Math.Max(eS, d); if (d > 1e-4) sBad++; }
            Check("实际地表 SurfaceY", sBad <= pts.Count / 2000, "最大误差 " + eS.ToString("E1") + "，> 1e-4 的点 " + sBad);

            // 道路 / 桥 / 关隘
            t0 = sw.ElapsedMilliseconds;
            GeoFields.Run(tm.PlanRoads((a, c) => sea.Contains(Math.Min(a, c) * 4096 + Math.Max(a, c)), null));
            var rr = R.First(r => r[0] == "roads");
            int nPts = tm.Roads.Sum(l => l.n + 1);
            Check("道路", tm.Roads.Count == int.Parse(rr[2]) && nPts == int.Parse(rr[3]), string.Format("{0} 条 {1} 点（网页版 {2} 条 {3} 点）", tm.Roads.Count, nPts, rr[2], rr[3]));
            int jsSea = int.Parse(R.First(r => r[0] == "sealinks")[2]);
            Check("海路连线", tm.SeaPairs.Count == jsSea, tm.SeaPairs.Count + " / " + jsSea);
            var bnd = spec.Bounds;
            tm.PlanPasses(bnd);
            var pj = R.Where(r => r[0] == "pass").ToList();
            int passOk = 0;
            foreach (var r in pj)
            {
                var p = tm.Passes.FirstOrDefault(q => q.name == r[2]);
                if (p != null && Math.Abs(p.x - P(r[3])) < 1e-4 && Math.Abs(p.z - P(r[4])) < 1e-4 && Math.Abs(p.labelY - P(r[5])) < 1e-3) passOk++;
                else Console.WriteLine("    关隘 " + r[2] + "：" + (p == null ? "无" : p.x + ", " + p.z + ", " + p.labelY) + " / 网页版 " + r[3] + ", " + r[4] + ", " + r[5]);
            }
            Check("关隘", tm.Passes.Count == pj.Count && passOk == pj.Count, tm.Passes.Count + " 座（网页版 " + pj.Count + "），位置一致 " + passOk + "：" + string.Join("、", tm.Passes.Select(p => p.name)));

            // TerrainInfo（战场地形的取样源）
            var tis = R.Where(r => r[0] == "ti").ToList();
            int tiBad = 0, wBad = 0, bBad = 0; double eTi = 0;
            foreach (var r in tis)
            {
                var t = tm.TerrainInfo(P(r[1]), P(r[2]));
                string jw = r[5] == "-" ? null : r[5];
                double[] mine = { t.h, t.slope, t.depth, t.forest, t.farm, t.sand, t.snow, t.river, t.coast, Math.Min(t.road, 999) };
                double[] js = { P(r[3]), P(r[4]), P(r[6]), P(r[7]), P(r[8]), P(r[9]), P(r[10]), P(r[13]), P(r[14]), Math.Min(P(r[15]), 999) };
                double e = 0;
                for (int k = 0; k < mine.Length; k++) e = Math.Max(e, Math.Abs(mine[k] - js[k]));
                eTi = Math.Max(eTi, e);
                if (e > 2e-3) tiBad++;
                if (t.water != jw) wBad++;
                if (t.biome != r[12] || (t.rock ? "1" : "0") != r[11]) bBad++;
            }
            Check("TerrainInfo 数值", tiBad <= tis.Count / 500, tis.Count + " 点，最大误差 " + eTi.ToString("E1") + "，> 2e-3 的 " + tiBad);
            Check("TerrainInfo 水域 / 群落 / 岩石", wBad <= tis.Count / 1000 && bBad <= tis.Count / 1000, "水域不一致 " + wBad + "，群落 / 岩石不一致 " + bBad);

            // 树木：随机数与网页版相同（SeededRandom(11)），逐棵比对（按生成顺序）
            var trees = new List<TerrainModel.Inst>();
            t0 = sw.ElapsedMilliseconds;
            GeoFields.Run(tm.PlaceTrees(trees, null));
            long tTree = sw.ElapsedMilliseconds - t0;
            var tj = R.Where(r => r[0] == "tree").ToList();
            CompareInst("树木", trees, tj, 2, tTree);
            // 石块 / 灌丛 / 村舍
            var rocks = new List<TerrainModel.Inst>(); var shrubs = new List<TerrainModel.Inst>(); var houses = new List<TerrainModel.Inst>();
            GeoFields.Run(tm.PlaceScatter(rocks, shrubs, houses, null));
            var sc = R.Where(r => r[0] == "scatter").ToList();
            CompareInst("石块", rocks, sc.Where(r => r[2] == "Rocks").ToList(), 3, -1);
            CompareInst("灌丛", shrubs, sc.Where(r => r[2] == "Shrubs").ToList(), 3, -1);
            CompareInst("村舍", houses, sc.Where(r => r[2] == "Villages").ToList(), 3, -1);
            Check("石块 / 村舍计数", tm.RockCount == int.Parse(specRow[4]) && tm.HouseCount == int.Parse(specRow[5]), tm.RockCount + " / " + specRow[4] + "，" + tm.HouseCount + " / " + specRow[5]);

            // 海上航线
            var lanes = R.Where(r => r[0] == "lane").ToList();
            if (lanes.Count > 0)
            {
                t0 = sw.ElapsedMilliseconds;
                GeoFields.Run(tm.BuildSeaMask());
                int laneOk = 0; double eL = 0; var path = new List<double>();
                foreach (var r in lanes)
                {
                    var ab = r[2].Split('-'); int ia = int.Parse(ab[0]), ib = int.Parse(ab[1]);
                    GeoFields.Run(tm.SeaPath(tm.CityX[ia], tm.CityZ[ia], tm.CityX[ib], tm.CityZ[ib], path));
                    int n = int.Parse(r[3]);
                    if (path.Count == 2 * n)
                    {
                        double e = 0;
                        for (int k = 0; k < 2 * n; k++) e = Math.Max(e, Math.Abs(path[k] - P(r[4 + k])));
                        eL = Math.Max(eL, e);
                        if (e < 1e-6) laneOk++;
                    }
                    else Console.WriteLine("    航线 " + r[2] + "：点数 " + path.Count / 2 + " / " + n);
                }
                Check("海上航线", laneOk >= lanes.Count - 1, string.Format("{0} / {1} 条逐点一致，最大误差 {2:E1}（{3} ms）", laneOk, lanes.Count, eL, sw.ElapsedMilliseconds - t0));
                tm.ReleaseSeaMask();
            }
        }
        Console.WriteLine(fails == 0 ? "== 地图：全部通过" : "== 地图：失败 " + fails + " 项");
        return fails;
    }

    // 实例列表（树木：kind 列在 r[2]；石块 / 灌丛 / 村舍：列 off 起为位置等）按生成顺序逐个比对位置、高度、缩放、颜色
    static void CompareInst(string label, List<TerrainModel.Inst> mine, List<string[]> js, int off, long ms)
    {
        // 网页版按区块分组输出：两边都按 (x, z) 排序后配对
        var a = mine.OrderBy(t => (float)t.x).ThenBy(t => (float)t.z).ToList();   // 网页版的位置读自 Float32 实例矩阵
        var bl = js.Select(r => new { kind = off == 2 ? r[2] : r[3], x = P(r[off + 1]), y = P(r[off + 2]), z = P(r[off + 3]), s = P(r[off + 4]), sy = P(r[off + 5]), r = P(r[off + 6]), g = P(r[off + 7]), b = P(r[off + 8]) })
            .OrderBy(t => (float)t.x).ThenBy(t => (float)t.z).ToList();
        int same = 0, kindBad = 0;
        int n = Math.Min(a.Count, bl.Count);
        for (int i = 0; i < n; i++)
        {
            var p = a[i]; var q = bl[i];
            if (Math.Abs(p.x - q.x) < 1e-4 && Math.Abs(p.z - q.z) < 1e-4 && Math.Abs(p.y - q.y) < 2e-3 && Math.Abs(p.s - q.s) < 1e-4 && Math.Abs(p.sy - q.sy) < 1e-4
                && Math.Abs(p.r - q.r) < 1e-4 && Math.Abs(p.g - q.g) < 1e-4 && Math.Abs(p.b - q.b) < 1e-4) same++;
            if (off == 2 && p.kind != q.kind) { kindBad++; if (kindBad <= 3) Console.WriteLine("    种类不一致 (" + p.x + ", " + p.z + ")：" + p.kind + " / 网页版 " + q.kind + "（" + q.x + ", " + q.z + "）"); }
        }
        Check(label, a.Count == bl.Count && same >= bl.Count * 0.995 && kindBad <= bl.Count / 200,
            string.Format("{0} 个（网页版 {1}），位置 / 高度 / 缩放 / 颜色一致 {2}{3}{4}", a.Count, bl.Count, same, off == 2 ? "，种类不一致 " + kindBad : "", ms >= 0 ? "（" + ms + " ms）" : ""));
    }
}
