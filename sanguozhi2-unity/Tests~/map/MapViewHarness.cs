// 地图视图的无头测试（第三版）：用 UnityViewStub 替身执行 Art / CultureArt / MapView，与网页版参考值（refs.js）比对：
//   程序化纹理（地形细节 A / B 逐字节、涟漪校验和）、平滑网格工具（球 / 旋转体 / 管 / 曲面屋顶 / 树 / 旧城模型）的顶点数与球面校验和、
//   各文化城池模型（近景 / 远景）的顶点数、植被几何（各树种 lod 0 / 2、石块、灌丛、村舍、草丛、野花）的顶点数、
//   地形块四叉树（预建到第 2 级：各级各状态的块数、高度校验和）、各类网格的三角形数（地形块、水面、海床、道路、桥、关隘、海路、
//   城池近景 / 远景、旗帜、光圈、云、树木近景 / 特写 / 公告板、石块 / 灌丛 / 村舍），关隘位置；
//   并做同步 / 分片构建、刷新、选城、每帧更新（地形块选择与补建、实例化绘制、草丛）、行军、重建释放的冒烟测试。
// 用法：mono mapview.exe <参考值目录>        退出码 = 失败的检查项数
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Sanguo;
using UnityEngine;

static class MapViewHarness
{
    static int fails;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static double P(string s) { return double.Parse(s, Inv); }
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  OK   " : "  FAIL ") + name + "：" + detail);
        if (!ok) fails++;
    }
    static uint Fnv(byte[] a) { uint h = 0x811c9dc5; foreach (var b in a) { h ^= b; h *= 0x01000193; } return h; }

    // 用网页版的城池表（refs.txt 的 city 行）校正剧本：C# 剧本数据与网页版不一致时以网页版为准（地图比对需要同一张城池表）
    static int SyncCities(GameState g, List<string[]> rows, string which)
    {
        var js = rows.Where(r => r[0] == "city" && r[1] == which).ToList();
        int diff = 0;
        if (js.Count != g.cities.Count) diff++;
        for (int i = 0; i < Math.Min(js.Count, g.cities.Count); i++)
        {
            var r = js[i]; var c = g.cities[i];
            var links = r[8] == "-" ? new List<int>() : r[8].Split(',').Select(int.Parse).ToList();
            if (c.key != r[3] || Math.Abs(c.lon - P(r[4])) > 1e-4 || Math.Abs(c.lat - P(r[5])) > 1e-4 || c.town != int.Parse(r[6]) || (c.culture ?? "han") != r[7] || !c.links.SequenceEqual(links)) diff++;
        }
        if (diff == 0) return 0;
        // 以网页版为准重建城池表（保留同名城的归属）
        var byKey = g.cities.ToDictionary(c => c.key, c => c);
        var list = new List<City>();
        foreach (var r in js)
        {
            City old; byKey.TryGetValue(r[3], out old);
            var c = new City { id = int.Parse(r[2]), key = r[3], name = old != null ? old.name : r[3], lon = (float)P(r[4]), lat = (float)P(r[5]), town = int.Parse(r[6]), culture = r[7], owner = old != null ? old.owner : -1 };
            if (r[8] != "-") foreach (var s in r[8].Split(',')) c.links.Add(int.Parse(s));
            list.Add(c);
        }
        g.cities = list;
        g.seaLinks = new HashSet<int>();
        var sr = rows.First(x => x[0] == "sealinks" && x[1] == which);
        for (int k = 3; k < sr.Length; k++) { var ab = sr[k].Split('-'); int a = int.Parse(ab[0]), b = int.Parse(ab[1]); g.seaLinks.Add(Math.Min(a, b) * 4096 + Math.Max(a, b)); }
        return diff;
    }

    static void RunAll(IEnumerator it)
    {
        var st = new Stack<IEnumerator>(); st.Push(it);
        while (st.Count > 0) { var top = st.Peek(); if (!top.MoveNext()) { st.Pop(); continue; } var sub = top.Current as IEnumerator; if (sub != null) st.Push(sub); }
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "../out/map";
        var rows = File.ReadAllLines(Path.Combine(dir, "refs.txt")).Where(l => l.Length > 0).Select(l => l.Split(' ')).ToList();
        Func<string, string, string[]> row = (a, b) => rows.First(r => r[0] == a && r[1] == b);
        var camGo = new GameObject("Main Camera");
        Camera.main = camGo.AddComponent<Camera>();
        Camera.main.fieldOfView = 34; Camera.main.aspect = 16f / 9f; Camera.main.nearClipPlane = 0.5f; Camera.main.farClipPlane = 700;
        var update = typeof(MapView).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        var sw = Stopwatch.StartNew();

        // ---------------------------------------------------- 程序化纹理 --
        {
            var a = Art.TerrainDetailBytesA; var b = Art.TerrainDetailBytesB;
            var ja = File.ReadAllBytes(Path.Combine(dir, "detA.bin")); var jb = File.ReadAllBytes(Path.Combine(dir, "detB.bin"));
            int da = 0, db = 0, ma = 0, mb2 = 0;
            for (int i = 0; i < Math.Min(a.Length, ja.Length); i++) { int d = Math.Abs(a[i] - ja[i]); if (d > 0) da++; ma = Math.Max(ma, d); }
            for (int i = 0; i < Math.Min(b.Length, jb.Length); i++) { int d = Math.Abs(b[i] - jb[i]); if (d > 0) db++; mb2 = Math.Max(mb2, d); }
            Check("地形细节纹理 A（256²，逐字节）", a.Length == ja.Length && da == 0, "不同 " + da + " 字节，最大差 " + ma + "，FNV " + Fnv(a) + " / " + row("tex", "detA")[2]);
            Check("地形细节纹理 B（256²，逐字节）", b.Length == jb.Length && db == 0, "不同 " + db + " 字节，最大差 " + mb2 + "，FNV " + Fnv(b) + " / " + row("tex", "detB")[2]);
            var rp = Art.RippleBytes();
            Check("涟漪纹理（128²）", Fnv(rp).ToString() == row("tex", "ripple")[2], "FNV " + Fnv(rp) + " / " + row("tex", "ripple")[2]);
        }

        // ---------------------------------------------------- 平滑网格工具 --
        {
            Func<Action<MeshBuilder>, int> one = f => { var m = new MeshBuilder(); f(m); return m.Count; };
            var W = Color.white; var O = Vector3.zero;
            var mine = new Dictionary<string, int>
            {
                { "sphere0", one(m => m.Sphere(O, 1, W, 0)) },
                { "sphere1", one(m => m.Sphere(O, new Vector3(1, 2, 1), W, 1, new SphereOpt { rough = 0.1f, seed = 3 })) },
                { "sphere2", one(m => m.Sphere(O, 1, W, 2)) },
                { "lathe", one(m => m.Lathe(O, new[] { new LP(0.5f, 0), new LP(0.4f, 1, true), new LP(0, 1.4f) }, 12, W, true, true)) },
                { "tube", one(m => m.Tube(new[] { O, new Vector3(0.1f, 0.5f, 0), new Vector3(0.2f, 1, 0.1f) }, new[] { 0.1f, 0.08f, 0.05f }, 6, W, true)) },
                { "roof", one(m => m.ChineseRoof(O, 1.0f, 0.6f, 0.32f, new Color(0.3f, 0.3f, 0.4f))) },
                { "roofSmall", one(m => m.ChineseRoof(O, 0.4f, 0.3f, 0.2f, new Color(0.3f, 0.3f, 0.4f))) },
                { "tree", one(m => Models.Tree(m, O, 1, new Color(0.3f, 0.5f, 0.2f), false, 3)) },
                { "pine", one(m => Models.Tree(m, O, 1, new Color(0.3f, 0.5f, 0.2f), true, 3)) },
                { "city", one(m => Models.CityInto(m, 1.2f, Color.white, true)) },
            };
            int bad = 0;
            foreach (var r in rows.Where(r => r[0] == "mb"))
            {
                int v; mine.TryGetValue(r[1], out v);
                if (v != int.Parse(r[2])) { bad++; Console.WriteLine("    " + r[1] + "：顶点 " + v + " / 网页版 " + r[2]); }
            }
            Check("平滑网格工具（顶点数）", bad == 0, mine.Count + " 项，不一致 " + bad);
            var mb = new MeshBuilder();
            mb.Sphere(new Vector3(0.5f, 1, -0.2f), new Vector3(0.4f, 0.3f, 0.5f), new Color(0.5f, 0.6f, 0.3f), 1, new SphereOpt { seed = 7, rough = 0.12f, top = 0.1f, bottomShade = 0.3f, smoothN = 0.5f });
            double sp = 0, sn = 0, sc = 0;
            for (int i = 0; i < mb.Count; i++)
            {
                // 网页版的数组为 three 坐标（z 取反）
                sp += mb.v[i].x + 2.0 * mb.v[i].y + 3.0 * mb.v[i].z; sn += mb.n[i].x + 2.0 * mb.n[i].y + 3.0 * mb.n[i].z; sc += mb.c[i].r + 2.0 * mb.c[i].g + 3.0 * mb.c[i].b;
            }
            var R = row("mbsum", "sphere");
            Check("球面（rough + smoothN）校验和", mb.Count == int.Parse(R[2]) && Math.Abs(sp - P(R[3])) < 2e-3 && Math.Abs(sn - P(R[4])) < 2e-3 && Math.Abs(sc - P(R[5])) < 2e-3,
                string.Format(Inv, "位置 {0:F5} / {1:F5}，法线 {2:F5} / {3:F5}，颜色 {4:F5} / {5:F5}", sp, P(R[3]), sn, P(R[4]), sc, P(R[5])));
        }

        // ---------------------------------------------------- 文化模型 --
        {
            int bad = 0, n = 0;
            foreach (var r in rows.Where(r => r[0] == "culture"))
            {
                string cu = r[1]; bool cap = r[2] == "1";
                var city = new City { key = "test", town = 300 };
                if (cu == "arab-aksum") { cu = "arab"; city.key = "aksum"; }
                if (cu == "sarmatian-big") { cu = "sarmatian"; city.town = 400; }
                if (cu == "africa-kush") { cu = "africa"; city.key = "meroe"; }
                if (cu == "africa-aksum") { cu = "africa"; city.key = "aksum"; }
                var near = new MeshBuilder(); var far = new MeshBuilder();
                CultureArt.CityInto(near, 1.2f, cu, cap, city);
                CultureArt.CityFarInto(far, 1.2f, cu, cap);
                n++;
                if (near.Count != int.Parse(r[3]) || far.Count != int.Parse(r[4])) { bad++; Console.WriteLine("    " + r[1] + (cap ? "（都城）" : "") + "：近景 " + near.Count + " / " + r[3] + "，远景 " + far.Count + " / " + r[4]); }
            }
            Check("各文化城池模型（近景 / 远景顶点数）", n > 30 && bad == 0, n + " 项，不一致 " + bad);
        }
        Console.WriteLine("    （" + sw.ElapsedMilliseconds + " ms）");

        var go = new GameObject("Map");
        var mv = go.AddComponent<MapView>();
        int builds = 0;
        mv.Built += () => builds++;
        foreach (var which in new[] { "china", "world" })
        {
            Console.WriteLine("== " + which);
            GameState g;
            if (which == "world") { var fk = WorldScenarioData.Factions[0].Split('|')[0]; g = GameState.NewGame(fk, "world"); }
            else g = GameState.NewGame("liubei", "classic");
            int diff = SyncCities(g, rows, which);
            if (diff > 0) Console.WriteLine("    注意：C# 剧本的城池表与网页版不同（" + diff + " 处），按网页版城池表构建");
            // 构建结束时按镜头补建细块：与 refs.js 相同的固定镜头（three (60, 80, 40) 看向 (60, 0, -50)）
            Camera.main.transform.position = new Vector3(60, 80, -40);
            Camera.main.transform.rotation = Quaternion.LookRotation(new Vector3(60, 0, 50) - new Vector3(60, 80, -40));
            var oldMeshes = Mesh.Created.Where(m => !UnityEngine.Object.Destroyed.Contains(m)).ToList(); var oldMats = Material.Created.Where(m => !UnityEngine.Object.Destroyed.Contains(m)).ToList();
            var t0 = Stopwatch.StartNew();
            string err = null;
            int reports = 0; float lastP = -1; bool mono = true;
            try
            {
                if (which == "china") mv.Build(g, which);
                else RunAll(mv.BuildAsync(g, "auto", (p, label) => { reports++; if (p < lastP - 1e-6f) mono = false; lastP = p; }));
            }
            catch (Exception e) { err = e.ToString(); }
            long ms = t0.ElapsedMilliseconds;
            Check("构建（" + (which == "china" ? "同步 Build" : "分片 BuildAsync") + "）", err == null && mv.Root != null && mv.Region == which,
                err ?? string.Format("区域 {0}，{1} ms，树 {2}，石 {3}，村舍 {4}，城 {5}", mv.Region, ms, mv.TreeCount, mv.RockCount, mv.HouseCount, mv.Cities.Count));
            if (err != null) { Console.WriteLine(err); continue; }
            var spec = row("spec", which);
            Check("实例数（树 / 石 / 村舍）与关隘", mv.TreeCount == int.Parse(spec[3]) && mv.RockCount == int.Parse(spec[4]) && mv.HouseCount == int.Parse(spec[5]) && mv.Passes.Count == int.Parse(spec[7]),
                mv.TreeCount + " / " + spec[3] + "，" + mv.RockCount + " / " + spec[4] + "，" + mv.HouseCount + " / " + spec[5] + "，关隘 " + mv.Passes.Count + " / " + spec[7]);
            if (which == "world")
            {
                var S = mv.Stats;
                Check("分片与进度", !S.sync && S.slices > 10 && reports > 10 && mono && Math.Abs(lastP - 1) < 1e-6f,
                    string.Format("{0} 片，最长 {1:F1} ms（{2}），进度回调 {3} 次、单调到 1", S.slices, S.maxSliceMs, S.maxSliceStage, reports));
                Console.WriteLine("    各阶段 ms：" + string.Join("，", S.stageMs.Select(kv => kv.Key + " " + kv.Value.ToString("F0"))));
            }
            // 旧地图的网格 / 材质 / 纹理全部释放（共享的植被几何、旗帜网格、Art 的共享材质除外）
            if (which == "world")
            {
                int leakM = oldMeshes.Count(m => !UnityEngine.Object.Destroyed.Contains(m) && !(m.name ?? "").StartsWith("veg:") && m.name != "flag");
                int leakT = oldMats.Count(m => !UnityEngine.Object.Destroyed.Contains(m));
                Check("重建时释放旧网格 / 材质", leakM == 0 && leakT <= 16, "未释放网格 " + leakM + "，材质 " + leakT + "（共享材质 ≤ 16）");
            }
            Check("城池对象", mv.Cities.Count == g.cities.Count && mv.Cities.Values.All(c => c.labelAnchor != null && c.go != null && c.flag != null),
                mv.Cities.Count + " 座，标签锚点 / 旗帜齐全");
            Check("Built 事件", builds == (which == "china" ? 1 : 2), "触发 " + builds + " 次");

            // 关隘位置
            {
                var js = rows.Where(r => r[0] == "pass" && r[1] == which).ToList();
                double e = 0; int bad = 0;
                foreach (var r in js)
                {
                    var p = mv.Passes.FirstOrDefault(q => q.name == r[2]);
                    if (p == null) { bad++; continue; }
                    e = Math.Max(e, Math.Max(Math.Abs(p.x - P(r[3])), Math.Max(Math.Abs(p.z - P(r[4])), Math.Abs(p.y - P(r[5])))));
                }
                Check("关隘（名称、位置、标签高度）", bad == 0 && e < 1e-3 && js.Count == mv.Passes.Count, js.Count + " 处，缺 " + bad + "，最大误差 " + e.ToString("E1"));
            }
            // 地形块四叉树
            {
                int roots, ns; double cs;
                var st = mv.DebugFreshLod(out roots, out ns, out cs);
                string mine = roots + " " + string.Join(" ", st.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k + "=" + st[k]));
                var R = row("lod", which); string js = string.Join(" ", R.Skip(2));
                Check("地形块四叉树（预建到第 2 级）", mine == js, mine + " / 网页版 " + js);
                var S = row("lodsum", which);
                Check("地形块高度校验和", ns == int.Parse(S[2]) && Math.Abs(cs - P(S[3])) < 1e-3, ns + " 点，" + cs.ToString("F6", Inv) + " / " + S[3]);
            }
            // 植被几何
            {
                var mineV = new HashSet<string>(mv.DebugVeg());
                var js = rows.Where(r => r[0] == "veg").ToList();
                // refs.txt 按区域依次写出；本区域的 veg 行在本区域 spec 行之后、下一个 spec 行之前
                int i0 = rows.FindIndex(r => r[0] == "spec" && r[1] == which), i1 = rows.FindIndex(i0 + 1, r => r[0] == "spec");
                if (i1 < 0) i1 = rows.Count;
                var jsV = new HashSet<string>(rows.Skip(i0).Take(i1 - i0).Where(r => r[0] == "veg").Select(r => string.Join(" ", r)));
                var miss = jsV.Where(s => !mineV.Contains(s)).ToList(); var extra = mineV.Where(s => !jsV.Contains(s)).ToList();
                foreach (var s in miss) Console.WriteLine("    网页版：" + s + "   C#：" + string.Join("; ", mineV.Where(m => m.Split(' ')[1] == s.Split(' ')[1] && m.Split(' ')[2] == s.Split(' ')[2])));
                foreach (var s in extra) Console.WriteLine("    C# 多出：" + s);
                Check("植被几何（顶点数）", miss.Count == 0 && extra.Count == 0, jsV.Count + " 项，缺 " + miss.Count + "，多 " + extra.Count);
            }
            // 网格三角形数（地形块：构建结束时按镜头补建，与网页版相同的镜头）
            {
                var mine = mv.DebugTriangles();
                var js = rows.Where(r => r[0] == "mesh" && r[1] == which).ToDictionary(r => r[2], r => (long)P(r[3]));
                int bad = 0;
                foreach (var k in js.Keys.Union(mine.Keys).OrderBy(k => k, StringComparer.Ordinal))
                {
                    int a; long b; mine.TryGetValue(k, out a); js.TryGetValue(k, out b);
                    if (a == 0 && b == 0) continue;
                    if (k == "SelectRing") continue;
                    bool ok = a == b;
                    if (!ok) bad++;
                    Console.WriteLine("    " + (ok ? "     " : "  ≠  ") + k + " " + a + " / " + b);
                }
                Check("各类网格的三角形数", bad == 0, js.Count + " 类，不一致 " + bad);
            }
            Console.WriteLine("    网格数 " + Mesh.Created.Count(m => !UnityEngine.Object.Destroyed.Contains(m)) + "，顶点合计 " +
                Mesh.Created.Where(m => !UnityEngine.Object.Destroyed.Contains(m)).Sum(m => (long)m.vertexCount));

            // 查询接口
            {
                var R = rows.Where(r => r[0] == "ti" && rows.IndexOf(r) > rows.FindIndex(x => x[0] == "spec" && x[1] == which)).Take(200).ToList();
                double e = 0;
                foreach (var r in R) { var t = mv.TerrainInfo(P(r[1]), P(r[2])); e = Math.Max(e, Math.Abs(t.h - P(r[3]))); }
                Check("TerrainInfo（经 MapView）", R.Count > 0 && e < 1e-6, R.Count + " 点，h 最大误差 " + e.ToString("E1"));
            }

            // 刷新、选城、每帧更新、行军
            err = null;
            try
            {
                g.cities[0].owner = 0; mv.Refresh(g);
                mv.Select(g.cities[1].id); mv.Select(-1); mv.Select(g.cities[2].id);
                var c2 = mv.Cities[2].pos;
                // 战略视角：镜头在城池斜上方
                Camera.main.transform.position = new Vector3(c2.x, 40, c2.z - 30);
                Camera.main.transform.rotation = Quaternion.LookRotation(c2 - Camera.main.transform.position);
                Graphics.calls = Graphics.instances = 0;
                for (int f = 0; f < 120; f++) { Time.time += 1f / 60; Time.deltaTime = 1f / 60; update.Invoke(mv, null); }
                int calls = Graphics.calls, inst = Graphics.instances, shown = mv.DebugLodShown, grass = mv.DebugGrassCount;
                // 近看：草丛与特写树
                Camera.main.transform.position = new Vector3(c2.x + 3, c2.y + 8, c2.z - 9);
                Camera.main.transform.rotation = Quaternion.LookRotation(c2 - Camera.main.transform.position);
                for (int f = 0; f < 60; f++) { Time.time += 1f / 60; update.Invoke(mv, null); }
                int grassNear = mv.DebugGrassCount;
                Console.WriteLine("    更新：实例化绘制 " + calls / 120 + " 次 / 帧，实例 " + inst / 120 + " / 帧，地形块 " + shown + "，草丛 / 野花 " + grass + " → 近看 " + grassNear);
                if (shown <= 0) throw new Exception("没有可见的地形块");
                if (calls <= 0) throw new Exception("没有实例化绘制");
                // 高空：植被隐去
                Camera.main.transform.position = new Vector3(0, 300, 0);
                Camera.main.transform.rotation = Quaternion.Euler(80, 0, 0);
                Graphics.calls = 0;
                update.Invoke(mv, null);
                Console.WriteLine("    高空：实例化绘制 " + Graphics.calls + " 次");
                // 地图隐藏（战斗中）：不提交实例化绘制
                mv.Root.gameObject.SetActive(false);
                Graphics.calls = 0;
                update.Invoke(mv, null);
                if (Graphics.calls != 0) throw new Exception("地图隐藏时仍有实例化绘制");
                mv.Root.gameObject.SetActive(true);
                // 行军：陆路与（世界）海路
                var pairs = new List<int[]> { new[] { g.cities[0].id, g.cities[0].links[0] } };
                if (which == "world")
                {
                    var sea = g.cities.SelectMany(c => c.links.Where(l => g.LinkIsSea(c.id, l)).Select(l => new[] { c.id, l })).FirstOrDefault();
                    if (sea != null) { pairs.Add(sea); pairs.Add(new[] { sea[1], sea[0] }); }
                    Check("海路查询", sea != null && mv.LinkIsSea(sea[0], sea[1]) && mv.SeaLane(sea[0], sea[1]) != null, sea == null ? "无海路" : "航线 " + mv.SeaLane(sea[0], sea[1]).Length / 2 + " 点");
                    var lr = rows.Where(r => r[0] == "lane" && r[1] == "world").ToList();
                    double le = 0; int lbad = 0;
                    foreach (var r in lr)
                    {
                        var ab = r[2].Split('-'); var pts = mv.SeaLane(int.Parse(ab[0]), int.Parse(ab[1]));
                        if (pts == null || pts.Length / 2 != int.Parse(r[3])) { lbad++; continue; }
                        for (int k = 0; k < pts.Length; k++) le = Math.Max(le, Math.Abs(pts[k] - P(r[4 + k])));
                    }
                    Check("海上航线（经 MapView）", lbad == 0 && le < 1e-6, lr.Count + " 条，不一致 " + lbad + "，最大误差 " + le.ToString("E1"));
                }
                foreach (var p in pairs)
                {
                    var m = mv.March(g.cities[p[0]], g.cities[p[1]], Color.red, 1.2f);
                    int frames = 0; while (m.MoveNext() && frames < 1000) frames++;
                    if (frames >= 1000) throw new Exception("行军未结束");
                }
                float h = mv.Height(mv.Cities[0].mapX, mv.Cities[0].mapY), sy = mv.SurfaceY(mv.Cities[0].mapX, mv.Cities[0].mapY);
                if (float.IsNaN(h) || float.IsNaN(sy)) throw new Exception("高度 NaN");
                if (mv.RoadDist(mv.Cities[0].mapX + 1, mv.Cities[0].mapY) > 50) throw new Exception("城旁无路");
            }
            catch (Exception e) { err = e.ToString(); }
            Check("刷新 / 选城 / 更新 / 行军", err == null, err ?? "无异常");
        }
        Console.WriteLine("    （合计 " + sw.ElapsedMilliseconds + " ms）");
        Console.WriteLine(fails == 0 ? "== 地图视图：全部通过" : "== 地图视图：失败 " + fails + " 项");
        return fails;
    }
}
