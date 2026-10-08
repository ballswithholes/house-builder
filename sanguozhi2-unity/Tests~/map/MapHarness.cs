// 无头地图测试：View/WorldGeo.cs（投影范围、地理场、地形模型、树木分布、海上航线）与网页版的参考值比对。
// 参考值由 Tests~/map/refs.js 在 Node 中运行网页版生成（refs.txt + hs-*.bin）。
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

    // 网页版 SG.SeededRandom（mulberry32）：让树木分布的随机数序列与网页版逐个相同
    sealed class JsRandom
    {
        uint a;
        public JsRandom(int seed) { a = (uint)(seed ^ unchecked((int)0x9e3779b9)); }
        public double Next()
        {
            a += 0x6D2B79F5u;
            uint t = (a ^ (a >> 15)) * (1u | a);
            t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    static void CityXY(string[] lines, out double[] xs, out double[] zs)
    {
        xs = new double[lines.Length]; zs = new double[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var p = lines[i].Split('|');
            double x, y; MapProjection.Project(P(p[2]), P(p[3]), out x, out y);
            xs[i] = x; zs[i] = y;
        }
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

        // ---------------------------------------------------- 两种范围 --
        foreach (var which in new[] { "china", "world" })
        {
            Console.WriteLine("== " + which);
            var spec = MapSpec.ForRegion(which);
            var t0 = sw.ElapsedMilliseconds;
            var geo = GeoFields.Get(spec);
            long tGeo = sw.ElapsedMilliseconds - t0;
            double[] cx, cz;
            CityXY(which == "world" ? WorldScenarioData.Cities : ScenarioData.Cities, out cx, out cz);
            var specRow = rows.First(r => r[0] == "spec" && r[1] == which);
            Check("城池数", cx.Length == int.Parse(specRow[2]), cx.Length + " / " + specRow[2]);
            var tm = new TerrainModel(geo, spec, cx, cz);

            // 采样点（网格之前：SurfaceY 退回 Height，与网页版构建后的 surfaceY 稍后再比）
            var pts = rows.Where(r => r[0] == "pt").ToList();
            // refs.txt 中两个范围的 pt 行依次排列：按 spec 行的位置切分
            int si = rows.IndexOf(specRow), ei = rows.FindIndex(si + 1, r => r[0] == "spec"); if (ei < 0) ei = rows.Count;
            pts = rows.GetRange(si, ei - si).Where(r => r[0] == "pt").ToList();
            var colors = rows.GetRange(si, ei - si).Where(r => r[0] == "color").ToList();
            var trees = rows.GetRange(si, ei - si).Where(r => r[0] == "tree").ToList();
            var lanes = rows.GetRange(si, ei - si).Where(r => r[0] == "lane").ToList();
            var grid = rows.GetRange(si, ei - si).First(r => r[0] == "grid");

            t0 = sw.ElapsedMilliseconds;
            GeoFields.Run(tm.GenGrid(null));
            long tGrid = sw.ElapsedMilliseconds - t0;
            Console.WriteLine(string.Format("  （地理场 {0} ms，地形网格 {1}×{2} 高度 {3} ms）", tGeo, tm.GNx + 1, tm.GNz + 1, tGrid));

            double eCoast = 0, eRiver = 0, eF = 0, eRidge = 0, eH = 0, eS = 0; int seaBad = 0, rwBad = 0, hBad = 0;
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
                eH = Math.Max(eH, dh); if (dh > 1e-3) hBad++;
                eS = Math.Max(eS, Math.Abs(tm.SurfaceY(x, z) - P(r[19])));
            }
            Check("海岸距离", eCoast < 1e-6, pts.Count + " 点，最大误差 " + eCoast.ToString("E1"));
            Check("陆 / 海判定", seaBad == 0, "不一致 " + seaBad);
            Check("河流距离与宽度 / 深度", eRiver < 1e-6 && rwBad == 0, "最大误差 " + eRiver.ToString("E1") + "，宽深不一致 " + rwBad);
            Check("平滑场取样（高原、生物群落、旧版权重）", eF < 1e-5, "最大误差 " + eF.ToString("E1"));
            Check("山脉", eRidge < 2e-4, "最大误差 " + eRidge.ToString("E1"));
            Check("高度 Height", hBad <= pts.Count / 500, "最大误差 " + eH.ToString("E1") + "，> 1e-3 的点 " + hBad);
            Check("实际地表 SurfaceY", eS < 2e-3, "最大误差 " + eS.ToString("E1"));

            // 地形网格
            bool gOk = Math.Abs(tm.GX0 - P(grid[2])) < 1e-9 && Math.Abs(tm.GZ0 - P(grid[3])) < 1e-9 && tm.GNx == int.Parse(grid[5]) && tm.GNz == int.Parse(grid[6]);
            var bytes = File.ReadAllBytes(Path.Combine(dir, "hs-" + which + ".bin"));
            int nH = bytes.Length / 4, badH = 0; double eG = 0;
            if (nH != tm.Hs.Length) gOk = false;
            else
                for (int i = 0; i < nH; i++)
                {
                    double d = Math.Abs(BitConverter.ToSingle(bytes, i * 4) - tm.Hs[i]);
                    if (d > eG) eG = d;
                    if (d > 1e-3) badH++;
                }
            Check("地形网格高度", gOk && badH <= nH / 2000, string.Format("{0} 个节点，最大误差 {1:E1}，> 1e-3 的节点 {2}", nH, eG, badH));

            // 水面格子（四角都高于 1.2 的格子不出网格）：数目与网页版的 Water 网格一致
            {
                var Wt = spec.Water; double st = Wt.step;
                int wnx = (int)Math.Ceiling((Wt.x1 - Wt.x0) / st), wnz = (int)Math.Ceiling((Wt.z1 - Wt.z0) / st), q = 0;
                Func<int, int, float> hw = (a2, b2) => (float)tm.Height(Wt.x0 + a2 * st, Wt.z0 + b2 * st);
                for (int i = 0; i < wnx; i++) for (int j = 0; j < wnz; j++)
                        if (!(hw(i, j) > 1.2 && hw(i + 1, j) > 1.2 && hw(i, j + 1) > 1.2 && hw(i + 1, j + 1) > 1.2)) q++;
                var hwb = File.ReadAllBytes(Path.Combine(dir, "hw-" + which + ".bin"));
                int bad = 0; double emax = 0;
                for (int i = 0; i <= wnx; i++) for (int j = 0; j <= wnz; j++)
                    {
                        double d = Math.Abs(BitConverter.ToSingle(hwb, (i * (wnz + 1) + j) * 4) - hw(i, j));
                        if (d > emax) emax = d;
                        if (d > 1e-3 && bad++ < 3) Console.WriteLine(string.Format("    水面网格 ({0:F2}, {1:F2})：{2} / 网页版 {3}", Wt.x0 + i * st, Wt.z0 + j * st, hw(i, j), BitConverter.ToSingle(hwb, (i * (wnz + 1) + j) * 4)));
                    }
                Check("水面网格高度", bad == 0, "最大误差 " + emax.ToString("E1") + "，> 1e-3 的节点 " + bad);
                var wr = rows.GetRange(si, ei - si).FirstOrDefault(r => r[0] == "mesh" && r[2] == "Water");
                int jsq = wr == null ? -1 : (int)(P(wr[3]) / 2);
                Check("水面格子", q == jsq, q + " / 网页版 " + jsq);
            }
            // 地表颜色（网页版的岩石 / 雪色）
            var rock0 = TerrainModel.Rock; var snow0 = TerrainModel.Snow;
            TerrainModel.Rock = new[] { 0.55, 0.53, 0.5 }; TerrainModel.Snow = new[] { 0.88, 0.9, 0.94 };
            double eC = 0; int cBad = 0;
            foreach (var r in colors)
            {
                double x = P(r[1]), z = P(r[2]), h = P(r[3]), dd = P(r[4]), jit = P(r[5]);
                const double e = 0.01;
                double ax = x - e, ay = h + dd, az = z - e, bx = x - e, by = h - dd, bz = z + 2 * e, qx = x + 2 * e, qy = h, qz = z - e;
                double mx = (ax + bx + qx) / 3, my = (ay + by + qy) / 3, mz = (az + bz + qz) / 3;
                double ux = bx - ax, uy = by - ay, uz = bz - az, vx = qx - ax, vy = qy - ay, vz = qz - az;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double ln = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                double slope = 1 - Math.Abs(ny / (ln > 0 ? ln : 1));
                var c = tm.GroundColor(mx, mz, my, slope, (jit / 0.06 + 0.5 - 0.5) * 0.06, false);
                double d = Math.Max(Math.Abs(c.r - P(r[6])), Math.Max(Math.Abs(c.g - P(r[7])), Math.Abs(c.b - P(r[8]))));
                eC = Math.Max(eC, d); if (d > 2e-3) cBad++;
            }
            TerrainModel.Rock = rock0; TerrainModel.Snow = snow0;
            Check("地表颜色 GroundColor", cBad <= colors.Count / 200, colors.Count + " 例，最大误差 " + eC.ToString("E1") + "，> 2e-3 的 " + cBad);

            // 树木：注入网页版的随机数，与网页版逐棵比对（排序后按位置配对）
            var mine = new List<TerrainModel.TreeInst>();
            var rnd = new JsRandom(11);
            t0 = sw.ElapsedMilliseconds;
            GeoFields.Run(tm.PlaceTrees(rnd.Next, mine, null));
            long tTree = sw.ElapsedMilliseconds - t0;
            int jsCount = int.Parse(specRow[3]);
            var a = mine.Select(t => new[] { (double)t.x, t.y, t.z, t.s, t.sy }).OrderBy(t => t[0]).ThenBy(t => t[2]).ToList();
            var bl = trees.Select(r => new[] { P(r[2]), P(r[3]), P(r[4]), P(r[5]), P(r[6]) }).OrderBy(t => t[0]).ThenBy(t => t[2]).ToList();
            int same = 0;
            {
                // 按位置配对（容差 1e-4）
                int i = 0, j = 0;
                while (i < a.Count && j < bl.Count)
                {
                    if (Math.Abs(a[i][0] - bl[j][0]) < 1e-4 && Math.Abs(a[i][2] - bl[j][2]) < 1e-4)
                    {
                        if (Math.Abs(a[i][1] - bl[j][1]) < 2e-3 && Math.Abs(a[i][3] - bl[j][3]) < 1e-4 && Math.Abs(a[i][4] - bl[j][4]) < 1e-4) same++;
                        i++; j++;
                    }
                    else if (a[i][0] < bl[j][0] || (Math.Abs(a[i][0] - bl[j][0]) < 1e-4 && a[i][2] < bl[j][2])) i++;
                    else j++;
                }
            }
            Check("树木分布", Math.Abs(mine.Count - jsCount) <= Math.Max(2, jsCount / 100) && same >= jsCount * 0.97,
                string.Format("{0} 棵（网页版 {1}），位置 / 高度 / 缩放一致 {2}（{3} ms）", mine.Count, jsCount, same, tTree));
            var sys = new System.Random(11); var mine2 = new List<TerrainModel.TreeInst>();
            GeoFields.Run(tm.PlaceTrees(sys.NextDouble, mine2, null));
            Console.WriteLine("  （Unity 用 System.Random(11)：" + mine2.Count + " 棵）");

            // 海上航线
            if (lanes.Count > 0)
            {
                t0 = sw.ElapsedMilliseconds;
                GeoFields.Run(tm.BuildSeaMask());
                int laneOk = 0; double eL = 0; var path = new List<double>();
                foreach (var r in lanes)
                {
                    var ab = r[2].Split('-'); int ia = int.Parse(ab[0]), ib = int.Parse(ab[1]);
                    GeoFields.Run(tm.SeaPath(cx[ia], cz[ia], cx[ib], cz[ib], path));
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
}
