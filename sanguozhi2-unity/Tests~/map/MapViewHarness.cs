// 地图视图的无头冒烟测试：用 UnityViewStub 替身执行 MapView 的同步 / 分片构建、刷新、选城、更新、行军与重建，
// 检查不抛异常、各类网格的三角形数与网页版一致（地形、远景、水面、道路、海路虚线、城池、光圈、旗帜）、
// 各文化城池模型的顶点数与网页版一致、分片构建的最长单步、重建后旧网格 / 材质全部释放。
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
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  OK   " : "  FAIL ") + name + "：" + detail);
        if (!ok) fails++;
    }

    static Dictionary<string, int> Tris(Transform root)
    {
        var d = new Dictionary<string, int>();
        Action<Transform> walk = null;
        walk = t =>
        {
            var mf = t.gameObject.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                string n = t.gameObject.name == "Flag" ? "Flags" : t.gameObject.name;
                int v; d.TryGetValue(n, out v); d[n] = v + mf.sharedMesh.triangles.Length / 3;
            }
            for (int i = 0; i < t.childCount; i++) walk(t.GetChild(i));
        };
        walk(root);
        return d;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "../out/map";
        var rows = File.ReadAllLines(Path.Combine(dir, "refs.txt")).Where(l => l.Length > 0).Select(l => l.Split(' ')).ToList();
        var camGo = new GameObject("Main Camera");
        Camera.main = camGo.AddComponent<Camera>();
        var update = typeof(MapView).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        // ---------------------------------------------------- 文化模型 --
        int cultBad = 0, cultN = 0;
        foreach (var r in rows.Where(r => r[0] == "culture"))
        {
            string cu = r[1]; bool cap = r[2] == "1" || r[2] == "true";
            var city = new City { key = "test", town = 300 };
            if (cu == "arab-aksum") { cu = "arab"; city.key = "aksum"; }
            if (cu == "sarmatian-big") { cu = "sarmatian"; city.town = 400; }
            var mb = new MeshBuilder();
            CultureArt.CityInto(mb, 1.2f, cu, cap, city);
            cultN++;
            if (mb.Count != int.Parse(r[3])) { cultBad++; Console.WriteLine("    " + r[1] + (cap ? "（都城）" : "") + "：顶点 " + mb.Count + " / 网页版 " + r[3]); }
        }
        Check("各文化城池模型（顶点数）", cultN > 20 && cultBad == 0, cultN + " 种，不一致 " + cultBad);

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
            var oldMeshes = Mesh.Created.ToList(); var oldMats = Material.Created.ToList();
            var sw = Stopwatch.StartNew();
            string err = null;
            int reports = 0; float lastP = -1; bool mono = true;
            try
            {
                if (which == "china") mv.Build(g);
                else
                {
                    var it = mv.BuildAsync(g, "auto", (p, label) => { reports++; if (p < lastP - 1e-6f) mono = false; lastP = p; });
                    var st = new Stack<IEnumerator>(); st.Push(it);
                    while (st.Count > 0) { var top = st.Peek(); if (!top.MoveNext()) { st.Pop(); continue; } var sub = top.Current as IEnumerator; if (sub != null) st.Push(sub); }
                }
            }
            catch (Exception e) { err = e.ToString(); }
            long ms = sw.ElapsedMilliseconds;
            Check("构建（" + (which == "china" ? "同步 Build" : "分片 BuildAsync") + "）", err == null && mv.Root != null && mv.Region == which,
                err ?? string.Format("区域 {0}，{1} ms，树 {2}，城 {3}", mv.Region, ms, mv.TreeCount, mv.Cities.Count));
            if (err != null) { Console.WriteLine(err); continue; }
            if (which == "world")
            {
                var S = mv.Stats;
                Check("分片与进度", !S.sync && S.slices > 10 && reports > 10 && mono && Math.Abs(lastP - 1) < 1e-6f,
                    string.Format("{0} 片，最长 {1:F1} ms（{2}），进度回调 {3} 次、单调到 1", S.slices, S.maxSliceMs, S.maxSliceStage, reports));
                Console.WriteLine("    各阶段 ms：" + string.Join("，", S.stageMs.Select(kv => kv.Key + " " + kv.Value.ToString("F0"))));
            }
            // 旧地图的网格 / 材质全部释放（共享的旗帜网格除外）
            if (oldMeshes.Count > 0)
            {
                int leakM = oldMeshes.Count(m => !UnityEngine.Object.Destroyed.Contains(m) && m.name != "flag");
                int leakT = oldMats.Count(m => !UnityEngine.Object.Destroyed.Contains(m));
                // Art 的共享材质（LowPoly / Water / Unlit 原型）只创建一次
                Check("重建时释放旧网格 / 材质", leakM == 0 && leakT <= 4, "未释放网格 " + leakM + "，材质 " + leakT + "（共享原型 ≤ 4）");
            }
            Check("城池对象", mv.Cities.Count == g.cities.Count && mv.Cities.Values.All(c => c.labelAnchor != null && c.go != null && c.flag != null),
                mv.Cities.Count + " 座，标签锚点 / 旗帜齐全");
            Check("Built 事件", builds == (which == "china" ? 1 : 2), "触发 " + builds + " 次");

            // 网格三角形数
            var mine = Tris(mv.Root);
            var js = rows.Where(r => r[0] == "mesh" && r[1] == which).ToDictionary(r => r[2], r => (int)double.Parse(r[3], CultureInfo.InvariantCulture));
            foreach (var k in new[] { "Terrain", "TerrainFar", "SeaBed", "Water", "WaterFar", "Roads", "SeaLanes", "Cities", "Territory", "Flags" })
            {
                int a, b; mine.TryGetValue(k, out a); js.TryGetValue(k, out b);
                Check("三角形 " + k, a == b, a + " / 网页版 " + b);
            }
            int ta, tb; mine.TryGetValue("Trees", out ta); js.TryGetValue("Trees", out tb);
            Check("三角形 Trees（随机数不同，±5%）", Math.Abs(ta - tb) <= tb * 0.05, ta + " / 网页版 " + tb);
            Console.WriteLine("    网格数 " + Mesh.Created.Count(m => !UnityEngine.Object.Destroyed.Contains(m)) + "，顶点合计 " +
                Mesh.Created.Where(m => !UnityEngine.Object.Destroyed.Contains(m)).Sum(m => (long)m.vertexCount));

            // 刷新、选城、每帧更新、行军
            err = null;
            try
            {
                g.cities[0].owner = 0; mv.Refresh(g);
                mv.Select(g.cities[1].id); mv.Select(-1); mv.Select(g.cities[2].id);
                Camera.main.transform.position = new Vector3(mv.Cities[2].pos.x, 60, mv.Cities[2].pos.z - 40);
                Camera.main.transform.rotation = Quaternion.Euler(55, 0, 0);
                for (int f = 0; f < 120; f++) { Time.time += 1f / 60; update.Invoke(mv, null); }
                Camera.main.transform.position = new Vector3(0, 300, 0);
                update.Invoke(mv, null);
                // 行军：陆路与（世界）海路
                var pairs = new List<int[]> { new[] { g.cities[0].id, g.cities[0].links[0] } };
                if (which == "world")
                {
                    var sea = g.cities.SelectMany(c => c.links.Where(l => g.LinkIsSea(c.id, l)).Select(l => new[] { c.id, l })).FirstOrDefault();
                    if (sea != null) { pairs.Add(sea); pairs.Add(new[] { sea[1], sea[0] }); }
                    Check("海路查询", sea != null && mv.LinkIsSea(sea[0], sea[1]) && mv.SeaLane(sea[0], sea[1]) != null, sea == null ? "无海路" : "航线 " + mv.SeaLane(sea[0], sea[1]).Length / 2 + " 点");
                }
                foreach (var p in pairs)
                {
                    var m = mv.March(g.cities[p[0]], g.cities[p[1]], Color.red, 1.2f);
                    int frames = 0; while (m.MoveNext() && frames < 1000) frames++;
                    if (frames >= 1000) throw new Exception("行军未结束");
                }
                float h = mv.Height(mv.Cities[0].mapX, mv.Cities[0].mapY), sy = mv.SurfaceY(mv.Cities[0].mapX, mv.Cities[0].mapY);
                if (float.IsNaN(h) || float.IsNaN(sy)) throw new Exception("高度 NaN");
            }
            catch (Exception e) { err = e.ToString(); }
            Check("刷新 / 选城 / 更新 / 行军", err == null, err ?? "无异常");
        }
        Console.WriteLine(fails == 0 ? "== 地图视图：全部通过" : "== 地图视图：失败 " + fails + " 项");
        return fails;
    }
}
