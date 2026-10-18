// 战场地形（第三版 §2，Battle/BattleTerrain.cs）的无头测试：与网页版 js/battle-terrain.js 的参考值（Tests~/battlemap/refs.js 在 Node 中
// 构建经典 / 世界两张战略地图后，为全部（城池, 来向）生成的战场）逐项比对，并检查可玩性。
//   剧本：经典 = NewGame("liubei", "classic")、世界 = NewGame(参考值的首个势力, "world")；城池表（编号、键、经纬度、规模、文化、连线、
//   海路）先与参考值核对。地形模型 = TerrainModel.BuildHeadless(g, 区域)（地形 + 道路，与网页版完整建图的 terrainInfo 相同）。
//   逐张比对：角度 / 转向 / 压缩系数 / 城池地面高度 / 群落 / 文化 / 种子 / notes、ascii、桥、城门与布阵格、取样栅格各字段的
//   FNV-1a（Float32 / Uint8 字节，逐位一致）、height / farm 之和、道路格数；每座城池的 rawAscii。
//   可玩性（C# 自己的结果）：攻方布阵格 ≥ 6、前 8 个布阵格各自有一条到城门的通路（不经山岳、水域、城墙）、本城与城门位置、
//   水域不可通行（TerrainCost ≥ 99）；BattleModel 构造时 BattleTerrain.ApplyTo 返回 true 且写入的就是同一张图。
// 用法：mono battlemap.exe <参考值目录>        退出码 = 失败的检查项数
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Sanguo;
using UnityEngine;

static class HarnessBattleMap
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int fails;
    static double P(string s) { return double.Parse(s, Inv); }
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  OK   " : "  FAIL ") + name + "：" + detail);
        if (!ok) fails++;
    }
    static uint Fnv(byte[] b) { uint h = 0x811c9dc5; unchecked { for (int i = 0; i < b.Length; i++) { h ^= b[i]; h *= 0x01000193; } } return h; }
    static uint Fnv(float[] f) { var b = new byte[f.Length * 4]; Buffer.BlockCopy(f, 0, b, 0, b.Length); return Fnv(b); }
    // 量化到 1e-6 的 FNV（Int32 = JS Math.round(v × 1e6)，小端）
    static uint FnvQ(object f)
    {
        var fa = f as float[]; var ba = f as byte[];
        int n = fa != null ? fa.Length : ba.Length;
        var q = new int[n];
        for (int i = 0; i < n; i++) q[i] = (int)Math.Floor((fa != null ? (double)fa[i] : ba[i]) * 1e6 + 0.5);
        var b = new byte[n * 4]; Buffer.BlockCopy(q, 0, b, 0, b.Length);
        return Fnv(b);
    }
    static string Pl(List<Vector2Int> a) { return a.Count == 0 ? "-" : string.Join(";", a.Select(q => q.x + "," + q.y)); }
    static string Num(double v) { return v.ToString("R", Inv); }

    // 可玩性：from 出发经可通行格（平原 / 森林 / 山丘 / 河川 / 城门 / 本城；桥为平原）到任一城门
    static bool Pass(Terrain t) { return t == Terrain.Plain || t == Terrain.Forest || t == Terrain.Hill || t == Terrain.River || t == Terrain.Gate || t == Terrain.Castle; }
    static bool Reaches(BattleTerrainMap T, Vector2Int from)
    {
        int W = T.W, H = T.H;
        var seen = new bool[W, H]; var st = new Stack<Vector2Int>();
        if (!Pass(T.map[from.x, from.y])) return false;
        seen[from.x, from.y] = true; st.Push(from);
        while (st.Count > 0)
        {
            var a = st.Pop();
            if (T.map[a.x, a.y] == Terrain.Gate) return true;
            foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
            {
                int x = a.x + d.x, y = a.y + d.y;
                if (x < 0 || y < 0 || x >= W || y >= H || seen[x, y] || !Pass(T.map[x, y])) continue;
                seen[x, y] = true; st.Push(new Vector2Int(x, y));
            }
        }
        return false;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string dir = args.Length > 0 ? args[0] : "../out/battlemap";
        var lines = File.ReadAllLines(Path.Combine(dir, "refs.txt")).Where(l => l.Length > 0).ToList();
        var sw = Stopwatch.StartNew();
        Check("水域不可通行", Defs.TerrainCost(Terrain.Water) >= 99 && BattleTerrain.Water == (int)Terrain.Water && BattleTerrain.IsWater(Terrain.Water) && BattleTerrain.Name(Terrain.Water) == "水域",
            "TerrainCost(Water) = " + Defs.TerrainCost(Terrain.Water) + "，Name = " + BattleTerrain.Name(Terrain.Water));
        int totalMaps = 0;
        var tileMix = new int[9]; var noteCount = new SortedDictionary<string, int>();
        foreach (var which in new[] { "china", "world" })
        {
            Console.WriteLine("== " + which);
            var sc = lines.First(l => l.StartsWith("scenario " + which + " ")).Split(' ');
            UnityEngine.Random.InitState(1);
            var g = GameState.Current = which == "world" ? GameState.NewGame(sc[3], "world") : GameState.NewGame("liubei", "classic");
            // 城池表
            var rows = lines.Where(l => l.StartsWith("city " + which + " ")).Select(l => l.Split(' ')).ToList();
            int cityBad = 0;
            foreach (var r in rows)
            {
                int id = int.Parse(r[2]);
                var c = id < g.cities.Count ? g.cities[id] : null;
                string links = c == null ? "" : (c.links.Count == 0 ? "-" : string.Join(",", c.links));
                var seaL = c == null ? new List<int>() : c.links.Where(li => g.LinkIsSea(c.id, li)).ToList();
                string sea = seaL.Count == 0 ? "-" : string.Join(",", seaL);
                string cul = c == null ? "" : (string.IsNullOrEmpty(c.culture) ? "han" : c.culture), jcul = r[7] == "-" ? "han" : r[7];
                bool ok = c != null && c.key == r[3] && Math.Abs((double)(decimal)c.lon - P(r[4])) < 1e-9 && Math.Abs((double)(decimal)c.lat - P(r[5])) < 1e-9 &&
                    c.town == int.Parse(r[6]) && cul == jcul && links == r[8] && sea == r[9];
                if (!ok) { cityBad++; if (cityBad <= 5) Console.WriteLine("    城池 " + string.Join(" ", r) + " ≠ C# " + (c == null ? "无" : c.key + " " + c.lon + " " + c.lat + " " + c.town + " " + cul + " " + links + " " + sea)); }
            }
            Check("城池表（编号、键、经纬度、规模、文化、连线、海路）", rows.Count == g.cities.Count && cityBad == 0, g.cities.Count + " 座（网页版 " + rows.Count + "），不一致 " + cityBad);

            long t0 = sw.ElapsedMilliseconds;
            var tm = TerrainModel.BuildHeadless(g, which);
            BattleTerrain.ModelSource = () => tm;
            Console.WriteLine("  （无头地形模型 " + (sw.ElapsedMilliseconds - t0) + " ms）");

            // 逐张比对
            var byKey = new Dictionary<string, Dictionary<char, string>>();
            foreach (var l in lines)
            {
                if (l.Length < 3 || l[1] != ' ' || "TABPGQS".IndexOf(l[0]) < 0) continue;
                var p = l.Split(new[] { ' ' }, 5);
                if (p[1] != which) continue;
                string key = p[2] + " " + p[3];
                Dictionary<char, string> d;
                if (!byKey.TryGetValue(key, out d)) byKey[key] = d = new Dictionary<char, string>();
                d[l[0]] = p.Length > 4 ? p[4] : "";
            }
            int nMaps = 0, badT = 0, badA = 0, badB = 0, badP = 0, badG = 0, badQ = 0, badS = 0, inexact = 0, badPlay = 0, unreachAll = 0;
            double maxNumErr = 0, tGen = 0;
            var fieldDiff = new int[11];
            var shown = 0;
            Action<string, string, string> show = (kind, mine, js) =>
            {
                if (shown++ >= 12) return;
                Console.WriteLine("    [" + kind + "] C#: " + mine.Replace("\n", "/"));
                Console.WriteLine("    [" + kind + "] JS: " + js);
            };
            foreach (var kv in byKey)
            {
                var kp = kv.Key.Split(' ');
                int cid = int.Parse(kp[0]), fid = int.Parse(kp[1]);
                var c = g.cities[cid]; var from = fid >= 0 ? g.cities[fid] : null;
                var opts = new BattleTerrain.Options { city = c, fromCity = from, sea = from != null && g.LinkIsSea(from.id, c.id), view = tm };
                // 其余参数形式（refs.js：−2 方位角、−3 向量、−4 地图坐标 + 方位角 + 海路）
                if (fid == -2) opts.fromDir = 37;
                else if (fid == -3) { opts.hasFromVec = true; opts.fromVx = -1; opts.fromVy = 2; }
                else if (fid == -4) { double px, py; WorldGeo.CityPos(c, out px, out py); opts.city = null; opts.hasPos = true; opts.x = px; opts.y = py; opts.fromDir = 200; opts.sea = true; }
                var ts = sw.Elapsed.TotalMilliseconds;
                var T = BattleTerrain.Generate(opts);
                tGen += sw.Elapsed.TotalMilliseconds - ts;
                nMaps++;
                var d = kv.Value;
                string tag = which + " " + c.name + "<" + (from != null ? from.name : fid == -1 ? "西" : "参数" + fid) + " ";
                if (d['T'].StartsWith("null")) { if (T != null) { badT++; show("T " + tag, "非空", "null"); } continue; }
                if (T == null) { badT++; show("T " + tag, "null", d['T']); continue; }
                // T 行
                var t = d['T'].Split(' ');
                string notes = T.notes.Count == 0 ? "-" : string.Join(",", T.notes);
                double[] mineN = { T.kAtt, T.kNeed, T.h0, T.dirX, T.dirY }, jsN = { P(t[2]), P(t[3]), P(t[4]), P(t[12]), P(t[13]) };
                bool numOk = true;
                for (int i = 0; i < mineN.Length; i++)
                {
                    double e = Math.Abs(mineN[i] - jsN[i]);
                    maxNumErr = Math.Max(maxNumErr, e);
                    if (e > 1e-9) numOk = false; else if (mineN[i] != jsN[i]) inexact++;
                }
                bool tOk = numOk && T.angle == int.Parse(t[0]) && T.turned == int.Parse(t[1]) && T.biome == t[5] && T.culture == t[6] && T.seed.ToString(Inv) == t[7] &&
                    T.W == int.Parse(t[8]) && T.H == int.Parse(t[9]) && T.castle.x == int.Parse(t[10]) && T.castle.y == int.Parse(t[11]) && notes == t[14];
                if (!tOk) { badT++; show("T " + tag, string.Join(" ", T.angle, T.turned, Num(T.kAtt), Num(T.kNeed), Num(T.h0), T.biome, T.culture, T.seed, T.W, T.H, T.castle.x, T.castle.y, Num(T.dirX), Num(T.dirY), notes), d['T']); }
                // ascii
                string asc = BattleTerrain.Ascii(T).Replace("\n", "/");
                if (asc != d['A']) { badA++; show("A " + tag, asc, d['A']); }
                // 桥
                string br = T.bridges.Count == 0 ? "-" : string.Join(";", T.bridges.Select(b => b.x + "," + b.y + "," + b.axis));
                if (br != d['B']) { badB++; show("B " + tag, br, d['B']); }
                // 城门 / 布阵格
                string pl = Pl(T.gates) + " " + Pl(T.atkTiles) + " " + Pl(T.defTiles);
                if (pl != d['P']) { badP++; show("P " + tag, pl, d['P']); }
                // 取样栅格
                var G = T.grid;
                string gs = string.Join(" ", G.nu, G.nv, Num(G.u0), Num(G.v0), Num(G.step), Fnv(G.h), Fnv(G.slope), Fnv(G.water), Fnv(G.forest), Fnv(G.farm), Fnv(G.sand), Fnv(G.snow), Fnv(G.biome), Fnv(G.road), Fnv(G.river), Fnv(G.coast));
                string qs = string.Join(" ", new object[] { G.h, G.slope, G.water, G.forest, G.farm, G.sand, G.snow, G.biome, G.road, G.river, G.coast }.Select(f => FnvQ(f)));
                if (qs != d['Q']) { badQ++; show("Q " + tag, qs, d['Q']); }
                if (gs != d['G'])
                {
                    var ga = gs.Split(' '); var gj = d['G'].Split(' ');
                    for (int i = 5; i < ga.Length && i < gj.Length; i++) if (ga[i] != gj[i]) fieldDiff[i - 5]++;
                    badG++; if (qs != d['Q']) show("G " + tag, gs, d['G']);
                    // 诊断：参考值另存了这张图的栅格（refs.js 的 BM_DUMP）时，报告各字段的最大差与不同的取样点数
                    var gf = Path.Combine(dir, "grid-" + which + "-" + cid + "-" + fid + ".bin");
                    if (File.Exists(gf))
                    {
                        var bytes = File.ReadAllBytes(gf); int N = G.nu * G.nv, off = 0;
                        var parts = new List<string>();
                        foreach (var fld in new object[] { G.h, G.slope, G.water, G.forest, G.farm, G.sand, G.snow, G.biome, G.road, G.river, G.coast })
                        {
                            int nd = 0; double md = 0;
                            if (fld is float[] fa) { for (int i = 0; i < N; i++) { double jv = BitConverter.ToSingle(bytes, off + i * 4); if (BitConverter.ToInt32(bytes, off + i * 4) != BitConverter.ToInt32(BitConverter.GetBytes(fa[i]), 0)) { nd++; md = Math.Max(md, Math.Abs(jv - fa[i])); if (nd == 1) parts.Add("[#" + i + " C# " + fa[i].ToString("R", Inv) + " JS " + jv.ToString("R", Inv) + "]"); } } off += N * 4; }
                            else { var ba = (byte[])fld; for (int i = 0; i < N; i++) if (bytes[off + i] != ba[i]) { nd++; md = Math.Max(md, Math.Abs(bytes[off + i] - ba[i])); } off += N; }
                            parts.Add(nd + "/" + md.ToString("G3", Inv));
                        }
                        Console.WriteLine("    [G 诊断] 不同的点数 / 最大差（h slope water forest farm sand snow biome road river coast）：" + string.Join(" ", parts));
                    }
                }
                // height / farm 之和、道路格数
                double hs = 0, fs = 0; int rd = 0;
                for (int x = 0; x < T.W; x++) for (int y = 0; y < T.H; y++) { hs += T.height[x, y]; fs += T.farm[x, y]; if (T.road[x, y]) rd++; }
                var s = d['S'].Split(' ');
                if (Math.Abs(hs - P(s[0])) > 1e-9 || Math.Abs(fs - P(s[1])) > 1e-9 || rd != int.Parse(s[2])) { badS++; show("S " + tag, Num(hs) + " " + Num(fs) + " " + rd, d['S']); }
                // 可玩性
                bool play = T.atkTiles.Count >= 6 && T.map[T.castle.x, T.castle.y] == Terrain.Castle && T.gates.All(q => T.map[q.x, q.y] == Terrain.Gate) && T.defTiles.Count > 0;
                foreach (var q in T.atkTiles.Take(8)) if (!Reaches(T, q)) play = false;
                bool edgeOk = false; for (int y = 0; y < T.H && !edgeOk; y++) if (Reaches(T, new Vector2Int(0, y))) edgeOk = true;
                if (!play || !edgeOk) { badPlay++; if (badPlay <= 5) Console.WriteLine("    可玩性 " + tag + "：布阵格 " + T.atkTiles.Count + "\n" + BattleTerrain.Ascii(T)); }
                unreachAll += T.atkTiles.Count(q => !Reaches(T, q));
                for (int x = 0; x < T.W; x++) for (int y = 0; y < T.H; y++) tileMix[(int)T.map[x, y]]++;
                foreach (var n in T.notes) { string k = n.StartsWith("turned:") ? "turned" : n; noteCount[k] = (noteCount.ContainsKey(k) ? noteCount[k] : 0) + 1; }
            }
            totalMaps += nMaps;
            Console.WriteLine(string.Format(Inv, "  {0} 张战场，平均生成 {1:F1} ms", nMaps, tGen / Math.Max(1, nMaps)));
            Check("角度 / 转向 / 压缩 / 地面高度 / 群落 / 文化 / 种子 / notes", badT == 0, "不一致 " + badT + "，数值最大误差 " + maxNumErr.ToString("E1", Inv) + "（非逐位相同 " + inexact + "）");
            Check("ascii 逐格", badA == 0, "不一致 " + badA + " / " + nMaps);
            Check("桥", badB == 0, "不一致 " + badB);
            Check("城门、攻方 / 守方布阵格", badP == 0, "不一致 " + badP);
            // 逐字节差异按字段如实列出（只列有差的字段）；原因不在这里判断，要看逐点差用 BM_DUMP 导出栅格（见上面的 [G 诊断]）
            var gNames = new[] { "h", "slope", "water", "forest", "farm", "sand", "snow", "biome", "road", "river", "coast" };
            var gDiff = gNames.Select((f, i) => fieldDiff[i] > 0 ? f + " " + fieldDiff[i] : null).Where(x => x != null).ToArray();
            string gNote = badG == 0 ? "逐字节 FNV 也全部相同"
                : "逐字节 FNV 不同的 " + badG + " 张，按字段（张数）：" + string.Join("、", gDiff)
                  + "；原因未逐张诊断，可用 BM_DUMP=剧本:城:来 导出栅格看逐点差"
                  + (fieldDiff[4] > 0 ? "（farm 的已知来源之一：TerrainModel.Hyp 用 √(x² + y²)，网页版用 Math.hypot，见报告）" : "");
            Check("取样栅格（11 个字段量化到 1e-6 的 FNV）", badQ == 0, "不一致 " + badQ + "（" + gNote + "）");
            Check("height / farm 之和、道路格数", badS == 0, "不一致 " + badS);
            Check("可玩性（布阵格 ≥ 6、前 8 格与布阵边到城门有路、城门 / 本城就位）", badPlay == 0, "不满足 " + badPlay + "（全部布阵格中到不了城门的 " + unreachAll + "，只作统计）");

            // rawAscii
            int rawBad = 0, rawN = 0;
            foreach (var l in lines.Where(l => l.StartsWith("R " + which + " ")))
            {
                var p = l.Split(new[] { ' ' }, 4);
                var mine = BattleTerrain.RawAscii(tm, g.cities[int.Parse(p[2])]).Replace("\n", "/");
                rawN++;
                if (mine != p[3]) { rawBad++; if (rawBad <= 3) { Console.WriteLine("    [R] C#: " + mine); Console.WriteLine("    [R] JS: " + p[3]); } }
            }
            Check("rawAscii（世界地图窗口，不旋转）", rawN == g.cities.Count && rawBad == 0, rawN + " 座，不一致 " + rawBad);

            // BattleModel 接线：真实城池上 ApplyTo 返回 true，写入的地图 = Generate 的结果
            int applyOk = 0, applyN = 0;
            foreach (var c in g.cities)
            {
                if (c.links.Count == 0) continue;
                var src = g.cities[c.links[0]];
                var atk = g.generals.Where(x => x.faction >= 0).Take(2).ToList();
                var s = new BattleSetup { attacker = atk[0].faction, defender = c.owner, src = src, target = c };
                s.atk.AddRange(atk);
                var m = new BattleModel(s);
                applyN++;
                var T = m.terrain as BattleTerrainMap;
                var T2 = BattleTerrain.Generate(new BattleTerrain.Options { city = c, fromCity = src, sea = g.LinkIsSea(src.id, c.id), view = tm });
                bool ok = T != null && T2 != null && ReferenceEquals(m.map, T.map) && BattleTerrain.Ascii(T) == BattleTerrain.Ascii(T2) && m.castle.x == T.castle.x && m.castle.y == T.castle.y &&
                    m.burning != null && m.burning.GetLength(0) == T.W && m.height[3, 3] == (float)T.height[3, 3];
                if (ok) applyOk++;
            }
            Check("BattleModel 构造：BattleTerrain.ApplyTo 返回 true 并写入 map / height / burning / castle / terrain", applyN > 0 && applyOk == applyN, applyOk + " / " + applyN);
            // 地形模型的来源：ModelSource → 反射取 Game.I.Map.Model（这里是本文件末尾的同名替身）→ 无头构建（按 GameState 缓存）
            BattleTerrain.ModelSource = null;
            Sanguo.Game.I = new Sanguo.Game { Map = new Sanguo.MapView { Model = tm } };
            bool viaReflect = ReferenceEquals(BattleTerrain.CurrentModel(g), tm);
            Sanguo.Game.I = null;
            long th = sw.ElapsedMilliseconds;
            var hm = BattleTerrain.CurrentModel(g);
            th = sw.ElapsedMilliseconds - th;
            bool cached = hm != null && !ReferenceEquals(hm, tm) && ReferenceEquals(BattleTerrain.CurrentModel(g), hm);
            var Th = hm != null ? BattleTerrain.Generate(new BattleTerrain.Options { city = g.cities[0], fromCity = g.cities[g.cities[0].links[0]], view = hm }) : null;
            var Tt = BattleTerrain.Generate(new BattleTerrain.Options { city = g.cities[0], fromCity = g.cities[g.cities[0].links[0]], view = tm });
            Check("地形模型来源：反射取 Game.I.Map.Model / 无头构建并缓存（同图）", viaReflect && cached && Th != null && BattleTerrain.Ascii(Th) == BattleTerrain.Ascii(Tt),
                "反射 " + viaReflect + "，无头构建 " + th + " ms、缓存 " + cached);
        }
        int tiles = tileMix.Sum();
        string[] tn = { "平原", "森林", "山丘", "山岳", "河川", "城墙", "城门", "本城", "水域" };
        Console.WriteLine("  地块比例：" + string.Join("，", Enumerable.Range(0, 9).Select(i => tn[i] + " " + (100.0 * tileMix[i] / Math.Max(1, tiles)).ToString("F1", Inv) + "%")));
        Console.WriteLine("  notes：" + string.Join("，", noteCount.Select(k => k.Key + " " + k.Value)));
        Console.WriteLine("  共 " + totalMaps + " 张战场，" + sw.ElapsedMilliseconds + " ms");
        Console.WriteLine(fails == 0 ? "== 战场地形：全部通过" : "== 战场地形：失败 " + fails + " 项");
        return fails;
    }
}

// Game / MapView 的最小替身（只为测试 BattleTerrain 用反射取 Game.I.Map.Model；真正的 Game、MapView 不进本测试）
namespace Sanguo
{
    public class Game { public static Game I; public MapView Map; }
    public class MapView { public TerrainModel Model; }
}
