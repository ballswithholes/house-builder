// 三国志II 霸王的大陆 · 游戏状态（对应网页版 js/model.js 的 GameState 部分）
// 第二版新增：
//   剧本可换：Scenarios.Classic（手写 ScenarioData）/ Scenarios.World（自动生成 WorldScenarioData），Scenarios.Current = SG.ScenarioData。
//     GameState.NewGame(playerKey, scenarioId = null)：scenarioId "classic" | "world"（缺省 = 当前剧本）；G.scenario 为剧本键。
//   剧本附加列（城 culture；势力 culture / region；武将 culture / born）与 WorldLookup（= SG.WorldData）的查询在 Init() 里
//     写到对象上，不进存档：city.culture；faction.culture、faction.region；gen.culture、gen.born、gen.sex（"m" / "f"）、gen.female。
//   G.seaLinks（剧本 SeaLinks）；G.LinkIsSea(a, b)。
//   City.MapPos = MapProjection.Project(lon, lat)（中国本部与旧版公式完全相同）。
//   G.RoutesFrom(city, f?, maxHops) → 经由 f 方城池链可达的 f 方城（BFS）；G.RouteBetween(from, to, f?) → 城编号路线或 null。
//   存档：PlayerPrefs 键 "sanguozhi2_save_v2"，版本 2，JSON 结构与网页版相同（含 version、scenario）。
//     GameState.LoadError：最近一次读档失败的原因（中文，与网页版相同）；HasLegacySave：只有第一版存档；SaveInfo()：标题画面摘要。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Sanguo
{
    [Serializable]
    public class City
    {
        public int id;
        public string key, name;
        public float lon, lat;
        public int land, industry, town, population, gold, food;
        public int owner = -1;     // 势力编号，-1 为空城
        public int governor = -1;  // 太守（武将编号）
        [NonSerialized] public List<int> links = new List<int>();
        [NonSerialized] public string culture = "han";   // 文化（由剧本重建，不存档）

        // 地图坐标（经纬度经分段投影换算；中国本部与第一版公式相同）
        public Vector2 MapPos { get { return MapProjection.Project(lon, lat); } }
        public int Defense { get { return 20 + town / 12; } }
    }

    [Serializable]
    public class General
    {
        public int id;
        public string name;
        public int war, intel, pol;
        public int faction = -1;   // -1 为在野
        public int city;
        public int loyalty;
        public int troops;
        public int training = 50;
        public bool hidden;        // 隐士，须搜索发现
        public bool dead;
        public int formation;      // 当前阵型
        public bool moved;         // 本月已行动（移动/出征）
        // 由剧本重建、不存档的资料
        [NonSerialized] public string culture;           // GameState.Init 前为 null（同网页版：Specials / Portrait 据此按南蛮名单回退）
        [NonSerialized] public int? born;                // 生年（不详为 null）
        [NonSerialized] public string sex = "m";         // "m" / "f"
        [NonSerialized] public bool female;

        public int MaxTroops { get { return Balance.GeneralTroopBase + war * Balance.GeneralTroopPerWar; } }
        public bool IsFree { get { return faction < 0 && !dead; } }
    }

    [Serializable]
    public class Faction
    {
        public int id;
        public string key, name, color;
        public int ruler;
        public int virtue, fame;
        public bool alive = true;
        [NonSerialized] public string culture = "han";
        [NonSerialized] public string region = "zhongyuan";
        public Color Col { get { Color c; ColorUtility.TryParseHtmlString(color, out c); return c; } }
    }

    // 经由己方城池可达的一座城：hops 为路程（相邻 = 1），path 为城编号（含起点与终点）
    public class Route
    {
        public City city;
        public int hops;
        public List<int> path;
    }

    // ================================================================ 剧本 --
    public sealed class ScenarioRegion
    {
        public string Id, Name;
        public string[] Factions;
    }

    public sealed class Scenario
    {
        public string Id;
        public int StartYear, StartMonth;
        public string Title, Intro;
        public string[] Cities, Links, SeaLinks, Factions, Generals;
        public ScenarioRegion[] Regions;   // 选择君主时按地域分组（经典剧本为空）
    }

    public static class Scenarios
    {
        public static readonly Scenario Classic = new Scenario
        {
            Id = "classic", StartYear = ScenarioData.StartYear, StartMonth = ScenarioData.StartMonth, Title = ScenarioData.Title, Intro = ScenarioData.Intro,
            Cities = ScenarioData.Cities, Links = ScenarioData.Links, SeaLinks = new string[0], Factions = ScenarioData.Factions, Generals = ScenarioData.Generals,
            Regions = new ScenarioRegion[0],
        };
        public static readonly Scenario World = new Scenario
        {
            Id = "world", StartYear = WorldScenarioData.StartYear, StartMonth = WorldScenarioData.StartMonth, Title = WorldScenarioData.Title, Intro = WorldScenarioData.Intro,
            Cities = WorldScenarioData.Cities, Links = WorldScenarioData.Links, SeaLinks = WorldScenarioData.SeaLinks, Factions = WorldScenarioData.Factions,
            Generals = WorldScenarioData.Generals,
            Regions = WorldScenarioData.Regions.Select(l => { var p = l.Split('|'); return new ScenarioRegion { Id = p[0], Name = p[1], Factions = p[2].Split(',') }; }).ToArray(),
        };
        public static readonly Scenario[] All = { Classic, World };
        // 当前剧本（= 网页版 SG.ScenarioData）。新游戏 / 读档时切换
        public static Scenario Current = Classic;
        public static Scenario Get(string id) { foreach (var s in All) if (s.Id == id) return s; return null; }
    }

    // 世界剧本的查询（= 网页版 SG.WorldData）：经典剧本的城池 / 武将 / 势力也由此取得文化、生年、性别
    public static class WorldLookup
    {
        static Dictionary<string, string> cultG, cultC, cultF, regF;
        static Dictionary<string, int> bornClassic;
        static void Build()
        {
            if (cultG != null) return;
            var g = new Dictionary<string, string>(); var c = new Dictionary<string, string>();
            var f = new Dictionary<string, string>(); var r = new Dictionary<string, string>();
            foreach (var l in WorldScenarioData.Generals) { var p = l.Split('|'); g[p[0]] = Col(p, 9) ?? "han"; }
            foreach (var l in WorldScenarioData.Cities) { var p = l.Split('|'); c[p[0]] = Col(p, 10) ?? "han"; }
            foreach (var l in WorldScenarioData.Factions) { var p = l.Split('|'); f[p[0]] = Col(p, 6) ?? "han"; r[p[0]] = Col(p, 7) ?? "zhongyuan"; }
            var b = new Dictionary<string, int>();
            foreach (var l in WorldScenarioData.GeneralBorn) { var p = l.Split('|'); b[p[0]] = int.Parse(p[1], CultureInfo.InvariantCulture); }
            bornClassic = b; cultC = c; cultF = f; regF = r; cultG = g;
        }
        static string Col(string[] p, int i) { return p.Length > i && p[i].Length > 0 ? p[i] : null; }
        static string Get(Dictionary<string, string> d, string k, string def) { string v; return k != null && d.TryGetValue(k, out v) ? v : def; }
        public static string CultureOfGeneral(string name) { Build(); return Get(cultG, name, "han"); }
        public static string CultureOfCity(string key) { Build(); return Get(cultC, key, "han"); }
        public static string CultureOfFaction(string key) { Build(); return Get(cultF, key, "han"); }
        public static string RegionOfFaction(string key) { Build(); return Get(regF, key, "zhongyuan"); }
        // "m" / "f"：新增武将查 GeneralInfo，经典武将中祝融为女性
        public static string SexOf(string name)
        {
            var w = WorldScenarioData.InfoOfGeneral(name);
            return w != null ? w.Sex : (Array.IndexOf(WorldScenarioData.ClassicFemale, name) >= 0 ? "f" : "m");
        }
        // 生年或 null：经典武将查 GeneralBorn，新增武将查 GeneralInfo
        public static int? BornOf(string name)
        {
            Build();
            var w = WorldScenarioData.InfoOfGeneral(name);
            if (w != null) return w.Born;
            int b; return name != null && bornClassic.TryGetValue(name, out b) ? b : (int?)null;
        }
    }

    // ================================================================ 投影 --
    // 经纬度 ↔ 地图坐标（网页版 SG.project / SG.unproject，js/world-geo.js）。分段线性：中国本部每经度 5、每纬度 5.6
    // （100°E、23°N 为原点，与第一版公式相同），远方地区压缩；东经 62°–97° 之间两条纬度剖面线性过渡。
    // 断点取自 WorldGeoData（导出工具生成）；内部用 double 计算。
    public static class MapProjection
    {
        static double[] lonBp, xBp, chX, chY, weX, weY, latBp;
        static double slopeW, slopeE, trans0, trans1;

        // 由 float 常数还原成十进制写法的 double（2.2f → 2.2），与 JS 的数值一致
        static double D(float f) { return (double)(decimal)f; }
        static void Build()
        {
            if (lonBp != null) return;
            var seg = WorldGeoData.LonSeg;
            int n = seg.Length / 2;
            var lb = new double[n]; var sl = new double[n]; var xb = new double[n];
            int k97 = -1;
            for (int i = 0; i < n; i++) { lb[i] = D(seg[2 * i]); sl[i] = D(seg[2 * i + 1]); if (lb[i] == 97) k97 = i; }
            // 以 97°E → x = −15 为锚，向两侧累加
            xb[k97] = -15;
            for (int i = k97 + 1; i < n; i++) xb[i] = xb[i - 1] + (lb[i] - lb[i - 1]) * sl[i - 1];
            for (int i = k97 - 1; i >= 0; i--) xb[i] = xb[i + 1] - (lb[i + 1] - lb[i]) * sl[i];
            slopeW = sl[0]; slopeE = sl[n - 1];
            Func<float[], int, double[]> pick = (a, o) => { var r = new double[a.Length / 2]; for (int i = 0; i < r.Length; i++) r[i] = D(a[2 * i + o]); return r; };
            chX = pick(WorldGeoData.LatChina, 0); chY = pick(WorldGeoData.LatChina, 1);
            weX = pick(WorldGeoData.LatWest, 0); weY = pick(WorldGeoData.LatWest, 1);
            latBp = chX.Concat(weX).Distinct().OrderBy(v => v).ToArray();
            trans0 = D(WorldGeoData.TransLon0); trans1 = D(WorldGeoData.TransLon1);
            xBp = xb; lonBp = lb;
        }

        // 分段线性函数（断点升序，两端按端段斜率外推）
        static double Pl(double[] bx, double[] by, double v)
        {
            int n = bx.Length;
            if (v <= bx[0]) return by[0] + (v - bx[0]) * (by[1] - by[0]) / (bx[1] - bx[0]);
            if (v >= bx[n - 1]) return by[n - 1] + (v - bx[n - 1]) * (by[n - 1] - by[n - 2]) / (bx[n - 1] - bx[n - 2]);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (bx[m] <= v) lo = m; else hi = m; }
            return by[lo] + (v - bx[lo]) * (by[hi] - by[lo]) / (bx[hi] - bx[lo]);
        }
        static double LonToX(double lon)
        {
            if (lon <= lonBp[0]) return xBp[0] + (lon - lonBp[0]) * slopeW;
            int n = lonBp.Length;
            if (lon >= lonBp[n - 1]) return xBp[n - 1] + (lon - lonBp[n - 1]) * slopeE;
            return Pl(lonBp, xBp, lon);
        }
        static double XToLon(double x)
        {
            if (x <= xBp[0]) return lonBp[0] + (x - xBp[0]) / slopeW;
            int n = xBp.Length;
            if (x >= xBp[n - 1]) return lonBp[n - 1] + (x - xBp[n - 1]) / slopeE;
            return Pl(xBp, lonBp, x);
        }
        static double BlendT(double lon) { return lon <= trans0 ? 0 : lon >= trans1 ? 1 : (lon - trans0) / (trans1 - trans0); }
        static double LatToY(double lat, double t)
        {
            if (t >= 1) return Pl(chX, chY, lat);
            if (t <= 0) return Pl(weX, weY, lat);
            return (1 - t) * Pl(weX, weY, lat) + t * Pl(chX, chY, lat);
        }
        static double YToLat(double y, double t)
        {
            // 混合剖面在合并断点之间是线性的：先求各断点处的 y，再逐段查找
            int n = latBp.Length;
            double prevLat = latBp[0], prevY = LatToY(prevLat, t);
            if (y <= prevY) { double y1 = LatToY(latBp[1], t); return prevLat + (y - prevY) * (latBp[1] - prevLat) / (y1 - prevY); }
            for (int i = 1; i < n; i++)
            {
                double la = latBp[i], ya = LatToY(la, t);
                if (y <= ya) return prevLat + (y - prevY) * (la - prevLat) / (ya - prevY);
                prevLat = la; prevY = ya;
            }
            double la0 = latBp[n - 2], y0 = LatToY(la0, t);
            return prevLat + (y - prevY) * (prevLat - la0) / (prevY - y0);
        }

        // 经纬度 → 地图坐标（Unity x / z）
        public static void Project(double lon, double lat, out double x, out double y) { Build(); x = LonToX(lon); y = LatToY(lat, BlendT(lon)); }
        public static Vector2 Project(float lon, float lat) { double x, y; Project((double)lon, (double)lat, out x, out y); return new Vector2((float)x, (float)y); }
        // 上式的精确逆变换
        public static void Unproject(double x, double y, out double lon, out double lat) { Build(); lon = XToLon(x); lat = YToLat(y, BlendT(lon)); }
        public static Vector2 Unproject(float x, float y) { double lo, la; Unproject((double)x, (double)y, out lo, out la); return new Vector2((float)lo, (float)la); }
    }

    // 读档失败（消息为给玩家看的中文说明）
    public class SaveLoadException : Exception { public SaveLoadException(string msg) : base(msg) { } }

    // 存档摘要（标题画面显示用）
    public class SaveInfo { public string scenario; public int year, month; public string faction; }

    [Serializable]
    public class GameState
    {
        public int year, month;
        public int player = -1;
        public int tokens;
        public int seed;
        public List<City> cities = new List<City>();
        public List<General> generals = new List<General>();
        public List<Faction> factions = new List<Faction>();
        public List<int> alliance = new List<int>(); // n×n，值为同盟到期的月序号
        public List<string> log = new List<string>();
        public string scenario = Scenarios.Current.Id;   // 剧本键 "classic" | "world"
        [NonSerialized] public HashSet<int> seaLinks = new HashSet<int>();   // 海路（小编号 × 4096 + 大编号）

        public static GameState Current;

        public int MonthIndex { get { return year * 12 + month; } }
        // 本局所用的剧本
        public Scenario Scen { get { return Scenarios.Get(scenario) ?? Scenarios.Current; } }

        // ------------------------------------------------------------ 创建 --
        // scenarioId："classic" | "world"；缺省（null）时用当前剧本 Scenarios.Current
        public static GameState NewGame(string playerFactionKey, string scenarioId = null)
        {
            if (scenarioId != null && Scenarios.Get(scenarioId) != null) Scenarios.Current = Scenarios.Get(scenarioId);
            var S = Scenarios.Current;
            var g = new GameState { scenario = S.Id, year = S.StartYear, month = S.StartMonth, seed = UnityEngine.Random.Range(1, 999999) };
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in S.Cities)
            {
                var p = line.Split('|');
                g.cities.Add(new City
                {
                    id = g.cities.Count, key = p[0], name = p[1], lon = float.Parse(p[2], inv), lat = float.Parse(p[3], inv),
                    land = int.Parse(p[4], inv), industry = int.Parse(p[5], inv), town = int.Parse(p[6], inv), population = int.Parse(p[7], inv),
                    gold = int.Parse(p[8], inv), food = int.Parse(p[9], inv),
                });
            }
            foreach (var line in S.Factions)
            {
                var p = line.Split('|');
                g.factions.Add(new Faction { id = g.factions.Count, key = p[0], name = p[1], color = p[3], virtue = int.Parse(p[4], inv), fame = int.Parse(p[5], inv), ruler = -1 });
            }
            foreach (var line in S.Generals)
            {
                var p = line.Split('|');
                var gen = new General
                {
                    id = g.generals.Count, name = p[0], war = int.Parse(p[1], inv), intel = int.Parse(p[2], inv), pol = int.Parse(p[3], inv),
                    faction = p[4] == "-" ? -1 : g.FactionByKey(p[4]).id, city = g.CityByKey(p[5]).id,
                    loyalty = int.Parse(p[6], inv), troops = int.Parse(p[7], inv), hidden = p[8] == "1",
                };
                gen.training = gen.faction >= 0 ? 55 + UnityEngine.Random.Range(0, 20) : 40;
                g.generals.Add(gen);
            }
            // 君主
            for (int i = 0; i < g.factions.Count; i++)
            {
                var rulerName = S.Factions[i].Split('|')[2];
                var r = g.generals.FirstOrDefault(x => x.name == rulerName);
                if (r == null) throw new Exception("ruler not found: " + rulerName);
                g.factions[i].ruler = r.id;
            }
            // 城主归属
            foreach (var gen in g.generals)
                if (gen.faction >= 0) g.cities[gen.city].owner = gen.faction;
            g.Init();
            foreach (var c in g.cities) g.AutoGovernor(c);
            g.player = g.FactionByKey(playerFactionKey).id;
            int n = g.factions.Count;
            for (int i = 0; i < n * n; i++) g.alliance.Add(0);
            g.tokens = g.TokensFor(g.player);
            return g;
        }

        // 读档或新建后重建非序列化数据：连线、海路、文化 / 地域 / 生年 / 性别（剧本附加列与 WorldLookup）
        public void Init()
        {
            var S = Scen;
            var byKey = new Dictionary<string, City>();
            foreach (var c in cities) byKey[c.key] = c;
            Func<string, City> city = k => { City c; if (!byKey.TryGetValue(k, out c)) throw new Exception("city not in scenario: " + k); return c; };
            foreach (var c in cities) c.links = new List<int>();
            foreach (var l in S.Links)
            {
                var p = l.Split('-');
                var a = city(p[0]); var b = city(p[1]);
                a.links.Add(b.id); b.links.Add(a.id);
            }
            seaLinks = new HashSet<int>();
            foreach (var l in S.SeaLinks ?? new string[0])
            {
                var p = l.Split('-');
                seaLinks.Add(SeaKey(city(p[0]).id, city(p[1]).id));
            }
            var cityCult = new Dictionary<string, string>(); var facCult = new Dictionary<string, string>(); var facReg = new Dictionary<string, string>();
            var genCult = new Dictionary<string, string>(); var genBorn = new Dictionary<string, int>();
            foreach (var l in S.Cities) { var p = l.Split('|'); if (p.Length > 10 && p[10].Length > 0) cityCult[p[0]] = p[10]; }
            foreach (var l in S.Factions) { var p = l.Split('|'); if (p.Length > 6 && p[6].Length > 0) facCult[p[0]] = p[6]; if (p.Length > 7 && p[7].Length > 0) facReg[p[0]] = p[7]; }
            foreach (var l in S.Generals)
            {
                var p = l.Split('|');
                if (p.Length > 9 && p[9].Length > 0) genCult[p[0]] = p[9];
                int b; if (p.Length > 10 && int.TryParse(p[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) genBorn[p[0]] = b;
            }
            string v;
            foreach (var c in cities) c.culture = cityCult.TryGetValue(c.key, out v) ? v : WorldLookup.CultureOfCity(c.key);
            foreach (var f in factions)
            {
                f.culture = facCult.TryGetValue(f.key, out v) ? v : WorldLookup.CultureOfFaction(f.key);
                f.region = facReg.TryGetValue(f.key, out v) ? v : WorldLookup.RegionOfFaction(f.key);
            }
            foreach (var x in generals)
            {
                x.culture = genCult.TryGetValue(x.name, out v) ? v : WorldLookup.CultureOfGeneral(x.name);
                int b0; int? born = genBorn.TryGetValue(x.name, out b0) ? b0 : (int?)null;
                if (!(born > 0)) born = WorldLookup.BornOf(x.name);
                x.born = born > 0 ? born : null;
                var info = WorldScenarioData.InfoOfGeneral(x.name);
                var sex = info != null && !string.IsNullOrEmpty(info.Sex) ? info.Sex : WorldLookup.SexOf(x.name);
                x.sex = sex == "f" ? "f" : "m";
                x.female = sex == "f";
            }
        }

        static int SeaKey(int a, int b) { return Math.Min(a, b) * 4096 + Math.Max(a, b); }
        // 两城之间是否为海路（地图据此画航线、行军沿航线前进）
        public bool LinkIsSea(int a, int b) { return seaLinks != null && seaLinks.Contains(SeaKey(a, b)); }

        // ------------------------------------------------------------ 查询 --
        public City CityByKey(string k) { return cities.First(c => c.key == k); }
        public Faction FactionByKey(string k) { return factions.First(f => f.key == k); }
        public Faction PlayerFaction { get { return factions[player]; } }
        // 以下查询返回当时的快照（与网页版的数组相同；电脑每月要调用上万次，故用循环而非 LINQ）
        public IEnumerable<City> CitiesOf(int f) { var r = new List<City>(); foreach (var c in cities) if (c.owner == f) r.Add(c); return r; }
        public IEnumerable<General> GeneralsOf(int f) { var r = new List<General>(); foreach (var x in generals) if (x.faction == f && !x.dead) r.Add(x); return r; }
        public IEnumerable<General> GeneralsIn(int city, int f) { var r = new List<General>(); foreach (var x in generals) if (x.city == city && x.faction == f && !x.dead) r.Add(x); return r; }
        public IEnumerable<General> OfficersIn(City c) { return c.owner < 0 ? new List<General>() : GeneralsIn(c.id, c.owner); }
        public IEnumerable<General> FreeFoundIn(int city) { var r = new List<General>(); foreach (var x in generals) if (x.city == city && x.IsFree && !x.hidden) r.Add(x); return r; }
        public int TroopsIn(City c)
        {
            if (c.owner < 0) return 0;
            int s = 0;
            foreach (var x in generals) if (x.city == c.id && x.faction == c.owner && !x.dead) s += x.troops;
            return s;
        }
        public bool IsRuler(General g) { return g.faction >= 0 && factions[g.faction].ruler == g.id; }
        public General Ruler(int f) { return generals[factions[f].ruler]; }
        public int CityCount(int f) { int n = 0; foreach (var c in cities) if (c.owner == f) n++; return n; }

        // 经由 f 方城池相连可达的所有 f 方城（广度优先；不含出发城，不穿过他国、同盟或空城）。
        // hops 为路程（相邻 = 1），path 为城编号（含起点与终点）。按路程、再按发现顺序（links 顺序）排列，结果确定。
        // from 不属于 f 时返回空表。f 缺省为 from.owner。maxHops > 0 时只取路程不超过它的城。
        public List<Route> RoutesFrom(City from, int? f = null, int maxHops = 0)
        {
            int ff = f ?? (from != null ? from.owner : -1);
            var outList = new List<Route>();
            if (from == null || ff < 0 || from.owner != ff) return outList;
            int lim = maxHops > 0 ? maxHops : int.MaxValue;
            var prev = new Dictionary<int, int> { { from.id, -1 } };
            var frontier = new List<int> { from.id };
            for (int hops = 1; frontier.Count > 0 && hops <= lim; hops++)
            {
                var next = new List<int>();
                foreach (var id in frontier)
                {
                    foreach (var j in cities[id].links)
                    {
                        if (prev.ContainsKey(j) || cities[j].owner != ff) continue;
                        prev[j] = id;
                        next.Add(j);
                        var path = new List<int>();
                        for (int k = j; k >= 0; k = prev[k]) path.Add(k);
                        path.Reverse();
                        outList.Add(new Route { city = cities[j], hops = hops, path = path });
                    }
                }
                frontier = next;
            }
            return outList;
        }
        // from → to 的最短己方路线（城编号，含两端）；不可达或 to 即 from 时为 null
        public List<int> RouteBetween(City from, City to, int? f = null)
        {
            if (from == null || to == null || from.id == to.id) return null;
            var r = RoutesFrom(from, f).FirstOrDefault(x => x.city.id == to.id);
            return r != null ? r.path : null;
        }

        public int TokensFor(int f)
        {
            int n = CityCount(f);
            if (n <= 0) return 0;
            return Mathf.Min(Balance.TokensMax, Balance.TokensBase + (n - 1) / Balance.CitiesPerExtraToken);
        }

        public bool Allied(int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return false;
            return alliance[a * factions.Count + b] > MonthIndex;
        }
        public void SetAlliance(int a, int b, int untilMonth)
        {
            alliance[a * factions.Count + b] = untilMonth;
            alliance[b * factions.Count + a] = untilMonth;
        }

        public void AutoGovernor(City c)
        {
            if (c.owner < 0) { c.governor = -1; return; }
            var offs = OfficersIn(c).ToList();
            if (offs.Count == 0) { c.governor = -1; return; }
            var ruler = offs.FirstOrDefault(IsRuler);
            if (c.governor >= 0 && offs.Any(o => o.id == c.governor) && ruler == null) return;
            c.governor = (ruler ?? offs.OrderByDescending(o => o.pol + o.intel).First()).id;
        }

        // 势力灭亡检查
        public void CheckFactionDeath(int f)
        {
            if (f < 0 || !factions[f].alive) return;
            if (CityCount(f) > 0) return;
            factions[f].alive = false;
            foreach (var gen in GeneralsOf(f).ToList()) { gen.faction = -1; gen.troops = 0; gen.loyalty = 0; }
            Log(factions[f].name + "势力灭亡了。");
        }

        public void Log(string s)
        {
            log.Add(year + "年" + month + "月 " + s);
            if (log.Count > 80) log.RemoveAt(0);
        }

        // ------------------------------------------------------------ 存档 --
        public const string SaveKey = "sanguozhi2_save_v2";
        public const string LegacySaveKey = "sanguozhi2_save";
        public const int SaveVersion = 2;
        // 最近一次读档失败的原因（中文）
        public static string LoadError;
        // 第一版 Unity 存档文件（第二版改存 PlayerPrefs；只用于判断“只有旧存档”）
        public static string SavePath { get { try { return System.IO.Path.Combine(Application.persistentDataPath, "sanguozhi2_save.json"); } catch { return ""; } } }

        static bool LegacyExists()
        {
            try { if (PlayerPrefs.HasKey(LegacySaveKey)) return true; } catch { }
            try { var p = SavePath; return p.Length > 0 && System.IO.File.Exists(p); } catch { return false; }
        }
        static string ReadSave() { try { return PlayerPrefs.HasKey(SaveKey) ? PlayerPrefs.GetString(SaveKey, "") : null; } catch { return null; } }

        public static bool HasSave { get { var t = ReadSave(); return !string.IsNullOrEmpty(t); } }
        // 只有旧版（第一版）存档：读不了，标题画面提示“新版本，旧存档无法读取”
        public static bool HasLegacySave { get { return !HasSave && LegacyExists(); } }

        public bool Save()
        {
            try { PlayerPrefs.SetString(SaveKey, ToJson()); PlayerPrefs.Save(); return true; }
            catch (Exception e) { Debug.Log("save failed " + e); return false; }
        }
        public static void DeleteSave() { try { PlayerPrefs.DeleteKey(SaveKey); PlayerPrefs.Save(); } catch { } }

        // 存档摘要（不完整读档）：{ scenario, year, month, faction } 或 null
        public static SaveInfo GetSaveInfo()
        {
            try
            {
                var d = MiniJson.Parse(ReadSave() ?? "null") as Dictionary<string, object>;
                if (d == null || !(Field(d, "factions") is List<object>)) return null;
                var fs = (List<object>)d["factions"];
                int pl = AsInt(Field(d, "player"));
                var f = pl >= 0 && pl < fs.Count ? fs[pl] as Dictionary<string, object> : null;
                return new SaveInfo { scenario = Field(d, "scenario") as string ?? "classic", year = AsInt(Field(d, "year")), month = AsInt(Field(d, "month")), faction = f != null ? Field(f, "name") as string ?? "" : "" };
            }
            catch { return null; }
        }

        public static GameState Load()
        {
            LoadError = null;
            var prev = Scenarios.Current;
            try
            {
                var text = ReadSave();
                if (string.IsNullOrEmpty(text))
                {
                    LoadError = LegacyExists() ? "新版本，旧存档无法读取。" : "没有存档。";
                    return null;
                }
                var g = FromJson(text);
                if (g == null) LoadError = "存档已损坏，无法读取。";
                return g;
            }
            catch (Exception e)
            {
                Scenarios.Current = prev;
                Debug.Log("load failed " + e.Message);
                LoadError = e is SaveLoadException ? e.Message : "存档已损坏，无法读取。";
                return null;
            }
        }

        // JSON（与网页版 JSON.stringify(G) 的结构与键序相同；city.links 等由剧本重建的字段不写）
        public string ToJson()
        {
            var sb = new StringBuilder(1 << 16);
            sb.Append("{\"version\":").Append(SaveVersion).Append(",\"scenario\":"); MiniJson.Str(sb, scenario ?? "classic");
            sb.Append(",\"year\":").Append(year).Append(",\"month\":").Append(month).Append(",\"player\":").Append(player)
              .Append(",\"tokens\":").Append(tokens).Append(",\"seed\":").Append(seed).Append(",\"cities\":[");
            for (int i = 0; i < cities.Count; i++)
            {
                var c = cities[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(c.id).Append(",\"key\":"); MiniJson.Str(sb, c.key);
                sb.Append(",\"name\":"); MiniJson.Str(sb, c.name);
                sb.Append(",\"lon\":").Append(MiniJson.Num(c.lon)).Append(",\"lat\":").Append(MiniJson.Num(c.lat))
                  .Append(",\"land\":").Append(c.land).Append(",\"industry\":").Append(c.industry).Append(",\"town\":").Append(c.town)
                  .Append(",\"population\":").Append(c.population).Append(",\"gold\":").Append(c.gold).Append(",\"food\":").Append(c.food)
                  .Append(",\"owner\":").Append(c.owner).Append(",\"governor\":").Append(c.governor).Append('}');
            }
            sb.Append("],\"generals\":[");
            for (int i = 0; i < generals.Count; i++)
            {
                var x = generals[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(x.id).Append(",\"name\":"); MiniJson.Str(sb, x.name);
                sb.Append(",\"war\":").Append(x.war).Append(",\"intel\":").Append(x.intel).Append(",\"pol\":").Append(x.pol)
                  .Append(",\"faction\":").Append(x.faction).Append(",\"city\":").Append(x.city).Append(",\"loyalty\":").Append(x.loyalty)
                  .Append(",\"troops\":").Append(x.troops).Append(",\"training\":").Append(x.training)
                  .Append(",\"hidden\":").Append(x.hidden ? "true" : "false").Append(",\"dead\":").Append(x.dead ? "true" : "false")
                  .Append(",\"formation\":").Append(x.formation).Append(",\"moved\":").Append(x.moved ? "true" : "false").Append('}');
            }
            sb.Append("],\"factions\":[");
            for (int i = 0; i < factions.Count; i++)
            {
                var f = factions[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(f.id).Append(",\"key\":"); MiniJson.Str(sb, f.key);
                sb.Append(",\"name\":"); MiniJson.Str(sb, f.name);
                sb.Append(",\"color\":"); MiniJson.Str(sb, f.color);
                sb.Append(",\"ruler\":").Append(f.ruler).Append(",\"virtue\":").Append(f.virtue).Append(",\"fame\":").Append(f.fame)
                  .Append(",\"alive\":").Append(f.alive ? "true" : "false").Append('}');
            }
            sb.Append("],\"alliance\":[");
            for (int i = 0; i < alliance.Count; i++) { if (i > 0) sb.Append(','); sb.Append(alliance[i]); }
            sb.Append("],\"log\":[");
            for (int i = 0; i < log.Count; i++) { if (i > 0) sb.Append(','); MiniJson.Str(sb, log[i]); }
            sb.Append("]}");
            return sb.ToString();
        }

        static object Field(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v : null; }
        // JS 的 x | 0：数字取整（向零），其余为 0
        static int AsInt(object v) { if (v is double) { double d = (double)v; return double.IsNaN(d) || double.IsInfinity(d) ? 0 : (int)(long)Math.Truncate(d); } if (v is bool) return (bool)v ? 1 : 0; return 0; }
        static bool AsBool(object v) { if (v is bool) return (bool)v; if (v is double) return (double)v != 0; if (v is string) return ((string)v).Length > 0; return false; }
        static float AsFloat(object v) { return v is double ? (float)(double)v : 0f; }
        static string AsStr(object v, string def) { return v is string ? (string)v : v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture); }

        // 不合格的存档抛出带中文说明的 SaveLoadException；结构不对（不是存档）返回 null
        public static GameState FromJson(string text)
        {
            var d = MiniJson.Parse(text) as Dictionary<string, object>;
            if (d == null || !(Field(d, "cities") is List<object>) || !(Field(d, "generals") is List<object>) || !(Field(d, "factions") is List<object>)) return null;
            var ver = Field(d, "version");
            if (!(ver is double) || (double)ver != SaveVersion) throw new SaveLoadException("新版本，旧存档无法读取。");
            var sid = Field(d, "scenario") as string ?? "classic";
            var scen = Scenarios.Get(sid);
            if (scen == null) throw new SaveLoadException("存档所用的剧本（" + sid + "）不存在，无法读取。");
            // 城池必须与剧本一致（连线、海路、文化都按剧本重建）
            var keys = new HashSet<string>(scen.Cities.Select(l => l.Substring(0, l.IndexOf('|'))));
            var dc = (List<object>)d["cities"];
            if (dc.Count != keys.Count || dc.Any(c => !(c is Dictionary<string, object>) || !keys.Contains(Field((Dictionary<string, object>)c, "key") as string ?? "\u0001")))
                throw new SaveLoadException("存档与当前版本的剧本数据不符，无法读取。");
            Scenarios.Current = scen;
            var g = new GameState { scenario = sid };
            g.year = AsInt(Field(d, "year")); g.month = AsInt(Field(d, "month"));
            g.player = Field(d, "player") == null ? -1 : AsInt(Field(d, "player"));
            g.tokens = AsInt(Field(d, "tokens")); g.seed = AsInt(Field(d, "seed"));
            foreach (Dictionary<string, object> c in dc)
                g.cities.Add(new City
                {
                    id = AsInt(Field(c, "id")), key = AsStr(Field(c, "key"), ""), name = AsStr(Field(c, "name"), ""), lon = AsFloat(Field(c, "lon")), lat = AsFloat(Field(c, "lat")),
                    land = AsInt(Field(c, "land")), industry = AsInt(Field(c, "industry")), town = AsInt(Field(c, "town")), population = AsInt(Field(c, "population")),
                    gold = AsInt(Field(c, "gold")), food = AsInt(Field(c, "food")),
                    owner = Field(c, "owner") == null ? -1 : AsInt(Field(c, "owner")), governor = Field(c, "governor") == null ? -1 : AsInt(Field(c, "governor")),
                });
            foreach (var o in (List<object>)d["generals"])
            {
                var x = o as Dictionary<string, object> ?? new Dictionary<string, object>();
                g.generals.Add(new General
                {
                    id = AsInt(Field(x, "id")), name = AsStr(Field(x, "name"), ""), war = AsInt(Field(x, "war")), intel = AsInt(Field(x, "intel")), pol = AsInt(Field(x, "pol")),
                    faction = Field(x, "faction") == null ? -1 : AsInt(Field(x, "faction")), city = AsInt(Field(x, "city")), loyalty = AsInt(Field(x, "loyalty")),
                    troops = AsInt(Field(x, "troops")), training = Field(x, "training") == null ? 50 : AsInt(Field(x, "training")),
                    hidden = AsBool(Field(x, "hidden")), dead = AsBool(Field(x, "dead")), formation = AsInt(Field(x, "formation")), moved = AsBool(Field(x, "moved")),
                });
            }
            foreach (var o in (List<object>)d["factions"])
            {
                var x = o as Dictionary<string, object> ?? new Dictionary<string, object>();
                g.factions.Add(new Faction
                {
                    id = AsInt(Field(x, "id")), key = AsStr(Field(x, "key"), ""), name = AsStr(Field(x, "name"), ""), color = AsStr(Field(x, "color"), "#ffffff"),
                    ruler = Field(x, "ruler") == null ? -1 : AsInt(Field(x, "ruler")), virtue = AsInt(Field(x, "virtue")), fame = AsInt(Field(x, "fame")),
                    alive = Field(x, "alive") == null || AsBool(Field(x, "alive")),
                });
            }
            var al = Field(d, "alliance") as List<object>;
            if (al != null) foreach (var v in al) g.alliance.Add(AsInt(v));
            int n = g.factions.Count;
            while (g.alliance.Count < n * n) g.alliance.Add(0);
            var lg = Field(d, "log") as List<object>;
            if (lg != null) foreach (var v in lg) g.log.Add(AsStr(v, "null"));
            g.Init();
            return g;
        }
    }

    // 最小的 JSON 读写（存档用；只依赖 System）。对象 → Dictionary<string, object>，数组 → List<object>，数字 → double
    public static class MiniJson
    {
        public static void Str(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4")); else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
        // 浮点数：最短往返写法（116.4f → "116.4"）
        public static string Num(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "null";
            return ((double)(decimal)v).ToString("R", CultureInfo.InvariantCulture);
        }

        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("JSON: trailing characters");
            return v;
        }
        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }
        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                for (;;)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException("JSON: key expected");
                    var k = String(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("JSON: ':' expected");
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException("JSON: ',' or '}' expected");
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                for (;;)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException("JSON: ',' or ']' expected");
                }
            }
            if (c == '"') return String(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException("JSON: unexpected '" + c + "'");
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        static string String(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON: bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("JSON: unterminated string");
        }
    }
}
