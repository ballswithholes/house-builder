// 三国志-霸王的世界 · 游戏状态（对应网页版 js/model.js 的 GameState 部分与 SG.Progress）
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
// 第三版 §4 成长（经验、等级、可习得必杀与计策）：
//   武将新增存档字段：exp（经验）、level（1–10，由 exp 推出）、specials（已习得的「可习得必杀」编号 "lp:…"，不含专属必杀）、
//     tactics（已会的计策编号：按智力自带的原四计 + 习得的新计策）、record（GenRecord：battles / wins / routs / duels）。
//   存档：PlayerPrefs 键 "sanguozhi2_save_v3"、版本 3；读到第二版存档（键 "sanguozhi2_save_v2"，version 2）时补齐：
//     等级 1、经验 0、只有专属必杀、原四计按智力（G.migratedFrom = 2，不存档，界面可提示一次「存档已升级」）；
//     下次存档写入新键并删除第二版键。SaveInfo.version 为存档版本。
//   Progress（本文件末尾，接口见该处注释）：经验与等级、按武力 / 智力的格数与等阶上限、升级候选（3 选 1）、习得、
//     电脑自动习得、自动结算战斗的经验（Conquest.AutoResolve 内调用）、老练加成 VetK。
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
        // 第三版：成长（Progress.Ensure 补齐 tactics 等；level 恒由 exp 推出）
        public int exp;
        public int level = 1;
        public List<string> specials = new List<string>();   // 已习得的「可习得必杀」编号（专属必杀不在此列）
        public List<string> tactics;                         // 已会的计策编号（null = 尚未补齐：Ensure 按智力填入原四计）
        public GenRecord record;                             // 战绩（null = 尚未补齐）
        // 由剧本重建、不存档的资料
        [NonSerialized] public string culture;           // GameState.Init 前为 null（同网页版：Specials / Portrait 据此按南蛮名单回退）
        [NonSerialized] public int? born;                // 生年（不详为 null）
        [NonSerialized] public string sex = "m";         // "m" / "f"
        [NonSerialized] public bool female;

        public int MaxTroops { get { return Balance.GeneralTroopBase + war * Balance.GeneralTroopPerWar; } }
        public bool IsFree { get { return faction < 0 && !dead; } }
    }

    // 武将战绩（第三版）
    [Serializable]
    public class GenRecord
    {
        public int battles, wins, routs, duels;
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
    public class SaveInfo { public string scenario; public int year, month; public string faction; public int version; }

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
        [NonSerialized] public int migratedFrom;   // 第三版：读入的是第二版存档时为 2（界面提示一次「存档已升级」），否则 0

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
                Progress.Ensure(gen);
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
        public const string SaveKey = "sanguozhi2_save_v3";
        public const string V2SaveKey = "sanguozhi2_save_v2";     // 第二版存档：读取时迁移（补齐成长数据），下次存档写入新键
        public const string LegacySaveKey = "sanguozhi2_save";
        public const int SaveVersion = 3;
        // 最近一次读档失败的原因（中文）
        public static string LoadError;
        // 第一版 Unity 存档文件（第二版改存 PlayerPrefs；只用于判断“只有旧存档”）
        public static string SavePath { get { try { return System.IO.Path.Combine(Application.persistentDataPath, "sanguozhi2_save.json"); } catch { return ""; } } }

        static bool LegacyExists()
        {
            try { if (PlayerPrefs.HasKey(LegacySaveKey)) return true; } catch { }
            try { var p = SavePath; return p.Length > 0 && System.IO.File.Exists(p); } catch { return false; }
        }
        // 当前存档的文字：第三版键优先，否则第二版键（读取时迁移）
        static string ReadKey(string k) { try { if (!PlayerPrefs.HasKey(k)) return null; var t = PlayerPrefs.GetString(k, ""); return string.IsNullOrEmpty(t) ? null : t; } catch { return null; } }
        public static string SaveText() { return ReadKey(SaveKey) ?? ReadKey(V2SaveKey); }
        static string ReadSave() { return SaveText(); }

        public static bool HasSave { get { var t = ReadSave(); return !string.IsNullOrEmpty(t); } }
        // 只有旧版（第一版）存档：读不了，标题画面提示“新版本，旧存档无法读取”
        public static bool HasLegacySave { get { return !HasSave && LegacyExists(); } }

        public bool Save()
        {
            try { PlayerPrefs.SetString(SaveKey, ToJson()); }
            catch (Exception e) { Debug.Log("save failed " + e); return false; }
            try { PlayerPrefs.DeleteKey(V2SaveKey); } catch { /* 第二版存档已由新存档取代 */ }
            try { PlayerPrefs.Save(); } catch { }
            return true;
        }
        public static void DeleteSave()
        {
            try { PlayerPrefs.DeleteKey(SaveKey); } catch { }
            try { PlayerPrefs.DeleteKey(V2SaveKey); } catch { }
            try { PlayerPrefs.Save(); } catch { }
        }

        // 存档摘要（不完整读档）：{ scenario, year, month, faction, version } 或 null
        public static SaveInfo GetSaveInfo()
        {
            try
            {
                var d = MiniJson.Parse(ReadSave() ?? "null") as Dictionary<string, object>;
                if (d == null || !(Field(d, "factions") is List<object>)) return null;
                var fs = (List<object>)d["factions"];
                int pl = AsInt(Field(d, "player"));
                var f = pl >= 0 && pl < fs.Count ? fs[pl] as Dictionary<string, object> : null;
                return new SaveInfo { scenario = Field(d, "scenario") as string ?? "classic", year = AsInt(Field(d, "year")), month = AsInt(Field(d, "month")), faction = f != null ? Field(f, "name") as string ?? "" : "", version = AsInt(Field(d, "version")) };
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
                  .Append(",\"formation\":").Append(x.formation).Append(",\"moved\":").Append(x.moved ? "true" : "false")
                  .Append(",\"exp\":").Append(x.exp).Append(",\"level\":").Append(x.level).Append(",\"specials\":");
                StrList(sb, x.specials);
                sb.Append(",\"tactics\":"); StrList(sb, x.tactics);
                sb.Append(",\"record\":");
                if (x.record == null) sb.Append("null");
                else sb.Append("{\"battles\":").Append(x.record.battles).Append(",\"wins\":").Append(x.record.wins).Append(",\"routs\":").Append(x.record.routs)
                       .Append(",\"duels\":").Append(x.record.duels).Append('}');
                sb.Append('}');
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

        static void StrList(StringBuilder sb, List<string> a)
        {
            if (a == null) { sb.Append("null"); return; }
            sb.Append('[');
            for (int i = 0; i < a.Count; i++) { if (i > 0) sb.Append(','); MiniJson.Str(sb, a[i]); }
            sb.Append(']');
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
            // 第二版存档可读：补齐成长数据（Progress.Ensure：等级 1、经验 0、原四计按智力）
            if (!(ver is double) || ((double)ver != SaveVersion && (double)ver != 2)) throw new SaveLoadException("新版本，旧存档无法读取。");
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
                var gen = new General
                {
                    id = AsInt(Field(x, "id")), name = AsStr(Field(x, "name"), ""), war = AsInt(Field(x, "war")), intel = AsInt(Field(x, "intel")), pol = AsInt(Field(x, "pol")),
                    faction = Field(x, "faction") == null ? -1 : AsInt(Field(x, "faction")), city = AsInt(Field(x, "city")), loyalty = AsInt(Field(x, "loyalty")),
                    troops = AsInt(Field(x, "troops")), training = Field(x, "training") == null ? 50 : AsInt(Field(x, "training")),
                    hidden = AsBool(Field(x, "hidden")), dead = AsBool(Field(x, "dead")), formation = AsInt(Field(x, "formation")), moved = AsBool(Field(x, "moved")),
                };
                // 第三版成长字段（第二版存档没有：由 Progress.Ensure 补齐）。exp 先四舍五入，非数值或负数视为 0（同网页版）
                var ex = Field(x, "exp");
                gen.exp = ex is double && (double)ex >= 0 && !double.IsInfinity((double)ex) ? (int)Math.Min(Progress.JsRound((double)ex), ProgressData.Exp[ProgressData.MaxLevel]) : 0;
                var sl = Field(x, "specials") as List<object>;
                gen.specials = sl != null ? sl.Select(v => v as string ?? "\u0001").ToList() : null;
                var tl = Field(x, "tactics") as List<object>;
                gen.tactics = tl != null ? tl.Select(v => v as string ?? "\u0001").ToList() : null;
                var rd = Field(x, "record") as Dictionary<string, object>;
                if (rd != null)
                {
                    Func<string, int> rv = k => { var v = Field(rd, k); return v is double && (double)v >= 0 ? (int)(double)v : 0; };
                    gen.record = new GenRecord { battles = rv("battles"), wins = rv("wins"), routs = rv("routs"), duels = rv("duels") };
                }
                g.generals.Add(gen);
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
            foreach (var x in g.generals) Progress.Ensure(x);
            g.migratedFrom = (double)ver == SaveVersion ? 0 : (int)(double)ver;
            return g;
        }
    }

    // ================================================================ Progress --
    // 第三版 §4 成长：经验、等级、可习得必杀与计策（= 网页版 js/model.js 的 SG.Progress；纯逻辑，无头测试可测）。
    //
    //   经验与等级（每位武将，电脑势力同样成长）
    //     Progress.Exp[L]                 升到 L 级所需的累计经验（L = 1..10；表在 ProgressData，由导出工具生成）
    //     LevelFor(exp) → L               ExpInfo(gen) → { level, exp, cur, next, need, pct, max }（界面进度条用）
    //     Gain(gen, amount, why?) → ProgressResult { gen, exp, from, to, learned } | null
    //                                     加经验；电脑武将（非玩家势力）有空格即自动习得（learned）；玩家武将升级时写一条记录
    //     BattleExp(x, won, commander) → 一战所得经验：
    //       参战 10 + 每击杀 40 兵 1 + 每回复 80 兵 1 + 击溃敌部 12（敌主将再 +12）+ 单挑胜 25 / 负 5 +
    //       每次必杀 6 + 每次计策成功 4（失败 1）+ 胜方 25（主将再 +10），一战至多 BattleCap。
    //       战术战斗（BattleModel）在分出胜负时自动结算（model.progress）；自动结算的攻城战见 AutoBattle。
    //   格数与等阶（升级时解锁；原四计按智力自带、不占格）
    //     SpecialSlots(gen) / TacticSlots(gen) → SlotInfo { max, unlocked, used, open, tier, statTier, levelTier }
    //       必杀：武力 60/70/80/90 各 1 格（至多 4）；等阶上限 武力 60→1、70→2、80→3、92→4（≈(武力−40)/15）；
    //             第 k 格与第 k 阶在 2/4/6/8 级解锁。
    //       计策：智力 50/60/70/80/90 各 1 格（至多 5）；等阶上限 智力 50→1、65→2、80→3、92→4（≈(智力−35)/15）；
    //             第 k 格在 3/5/7/9/10 级、第 k 阶在 3/5/7/9 级解锁。
    //       第 k 格最高只能学 k 阶（tier = 下一招可学的最高等阶 = min(能力上限, 等级上限, 已学 + 1)），
    //       故招式按 1、2、3、4 阶逐级习得；推迟选择也不能一口气全学高阶。可习得的暗杀（kind assassinate）每人至多一招。
    //     Openings(gen) → { special, tactic, total }   现在可学的空格数
    //   升级选择（玩家从 3 个候选中选 1；电脑自动选）
    //     Candidates(gen, n = 3) → [LearnCandidate { type "special" | "tactic", id, tier, name, desc, rules, color, fx, special / tactic }]
    //       候选确定（按姓名、等级、已学、本局种子），不耗 Random；两类都有空格时两类都会出现；每类至少一个候选是当前可学的最高等阶。
    //     Learn(gen, cand) / Learn(gen, type, id) → bool    AiPick(gen, cands) → cand    AutoLearn(gen) → [cand]
    //     Pending(f) → [gen]                       f 势力中有空格且有候选的武将（玩家：战后逐个弹出升级对话框）
    //       对话框循环条件请用 Candidates(gen).Count > 0
    //     playerAutoLearn（缺省 true）：玩家武将也由 AiPick 自动习得；升级对话框上线时设为 false
    //     KnownSpecials(gen) / KnownTactics(gen)（专属必杀在前；= Specials.Known / Tactics.Known）
    //   自动结算的攻城战（Conquest.AutoResolve 内调用，不耗随机数）
    //     Snapshot(s) → 战前兵力；AutoBattle(s, snap) → [ProgressResult { …, side, won }]
    //       （按战损与战前战力分摊；结果同时写入 s.progress；s.progressDone 防重复）
    //   老练：战术战斗中每高 1 级，攻击、防御与计策威力各 +VetPerLevel（4%；1 级为 ×1，10 级 ×1.36）
    //     VetK(gen) → 乘数（enabled = false 时恒为 1）；只用于 BattleModel 的战术战斗，不影响自动结算（AutoResolve）
    //   Progress.enabled = false：不再获得经验，战斗只用专属必杀与原四计（平衡对比测试用）

    // 一战的经验来源（战术战斗中 BUnit.xp 累计；自动结算按战损分摊时为小数）
    public sealed class BattleXp
    {
        public double dmg, heal, routs, cmdRouts;
        public int duelWin, duelLoss, specials, tactics, tacFail;
        public BattleXp Clone() { return (BattleXp)MemberwiseClone(); }
    }
    // 格数与等阶（Progress.SpecialSlots / TacticSlots）
    public sealed class SlotInfo { public int max, unlocked, used, open, tier, statTier, levelTier; }
    public struct Openings { public int special, tactic, total; }
    // 经验进度（界面进度条用）
    public sealed class ExpInfo { public int level, exp, cur, next, need; public double pct; public bool max; }
    // 一个升级候选（Progress.Candidates）：可习得必杀（special）或计策（tactic）
    public sealed class LearnCandidate
    {
        public string type;       // "special" | "tactic"
        public string id;         // "lp:编号" | 计策编号
        public int tier;
        public string name, desc, rules, color, fx;
        public Special special;   // type = special 时
        public TacticDef tactic;  // type = tactic 时
        public string Kind { get { return special != null ? special.kind : tactic != null ? tactic.Id : ""; } }
    }
    // 经验结算的一条结果（Progress.Gain / AutoBattle、BattleModel.SettleProgress；界面战后统一读取 setup.progress）
    public sealed class ProgressResult
    {
        public General gen;
        public int exp;                 // 本次获得的经验
        public string why = "";
        public int from, to;            // 等级变化
        public List<LearnCandidate> learned = new List<LearnCandidate>();   // 电脑（或 playerAutoLearn）自动习得的招式
        public int side;                // 0 攻方 1 守方
        public bool won;
        public BattleXp xp;             // 战术战斗的经验来源（自动结算为 null）
    }
    // 自动结算前的兵力与战力快照
    public sealed class ProgressSnap { public int troops; public double power; }

    public static class Progress
    {
        public static bool enabled = true;
        // 玩家武将也自动习得（与电脑相同的选择）。升级对话框接好后（StrategyScreen）设为 false，
        // 玩家武将改为保留空格，由 Pending(G.player) / Candidates / Learn 逐个选择。
        public static bool playerAutoLearn = true;
        public const int MaxLevel = ProgressData.MaxLevel;
        public const int BattleCap = ProgressData.BattleCap;
        public const double VetPerLevel = ProgressData.VetPerLevel;
        public static int[] Exp { get { return ProgressData.Exp; } }
        public static string[] InnateIds { get { return ProgressData.InnateTactics; } }   // 与 Defs.Tactics 同序

        // Math.round：四舍五入（.5 向 +∞）
        internal static double JsRound(double x) { double r = Math.Floor(x); if (x - r >= 0.5) r += 1; return r; }
        static int CountAtLeast(int[] list, int v) { int n = 0; foreach (var t in list) if (v >= t) n++; return n; }
        // FNV-1a 32 位（按 UTF-16 码元，同 JS charCodeAt）
        static uint PHash(string str)
        {
            uint h = 0x811c9dc5u;
            unchecked { foreach (char c in str) { h ^= c; h *= 0x01000193u; } }
            return h;
        }
        static double GenPower(General x) { return Math.Max(0, x.troops) * (0.55 + x.war / 220.0) * (0.7 + x.training / 330.0); }
        static GameState G { get { return GameState.Current; } }

        // 老练加成（战术战斗）：每高 1 级 +4%；1 级与成长关闭时为 1
        public static double VetK(General gen)
        {
            if (!enabled || gen == null) return 1;
            int L = gen.level;
            return L > 1 ? 1 + VetPerLevel * (Math.Min(L, MaxLevel) - 1) : 1;
        }

        public static int LevelFor(int exp)
        {
            int L = 1;
            for (int i = 2; i <= MaxLevel; i++) if (exp >= Exp[i]) L = i;
            return L;
        }
        public static ExpInfo ExpInfo(General gen)
        {
            Ensure(gen);
            int L = gen.level; bool max = L >= MaxLevel;
            int cur = Exp[L], next = max ? cur : Exp[L + 1];
            double pct = max ? 1 : Math.Max(0, Math.Min(1, (gen.exp - cur) / (double)Math.Max(1, next - cur)));
            return new ExpInfo { level = L, exp = gen.exp, cur = cur, next = next, need = max ? 0 : next - gen.exp, pct = pct, max = max };
        }

        // 原四计中按智力会的（与 Defs.Tactics 的需求智力相同）
        public static List<string> InnateTactics(General gen)
        {
            var outList = new List<string>();
            var T = Defs.Tactics;
            for (int i = 0; i < InnateIds.Length; i++) if (i < T.Length && gen.intel >= T[i].ReqInt) outList.Add(InnateIds[i]);
            return outList;
        }
        public static bool IsInnateTactic(string id) { return Array.IndexOf(InnateIds, id) >= 0; }

        // 补齐 / 规范成长字段（幂等：已规范的数据原样不变，存档往返一致）
        public static General Ensure(General gen)
        {
            if (gen == null) return gen;
            if (!(gen.exp >= 0)) gen.exp = 0;
            gen.exp = Math.Min(gen.exp, Exp[MaxLevel]);
            gen.level = LevelFor(gen.exp);
            var sp0 = gen.specials ?? new List<string>();
            var sp = new List<string>();
            foreach (var id in sp0) if (id != null && !sp.Contains(id) && Specials.ById(id) != null) sp.Add(id);
            if (gen.specials == null || sp.Count != sp0.Count) gen.specials = sp;
            var t0 = gen.tactics ?? new List<string>();
            var tac = InnateTactics(gen);
            foreach (var id in t0) if (id != null && !IsInnateTactic(id) && !tac.Contains(id) && Tactics.ById(id) != null) tac.Add(id);
            if (gen.tactics == null || string.Join(",", tac.ToArray()) != string.Join(",", t0.ToArray())) gen.tactics = tac;
            if (gen.record == null) gen.record = new GenRecord();
            else
            {
                var r = gen.record;
                if (!(r.battles >= 0)) r.battles = 0; if (!(r.wins >= 0)) r.wins = 0; if (!(r.routs >= 0)) r.routs = 0; if (!(r.duels >= 0)) r.duels = 0;
            }
            return gen;
        }
        public static List<string> LearnedTactics(General gen) { Ensure(gen); return gen.tactics.Where(id => !IsInnateTactic(id)).ToList(); }

        public static SlotInfo SpecialSlots(General gen)
        {
            Ensure(gen);
            int w = gen.war;
            int max = CountAtLeast(ProgressData.SpecialSlotWar, w);
            int unlocked = Math.Min(max, CountAtLeast(ProgressData.SpecialSlotLevel, gen.level));
            int statTier = CountAtLeast(ProgressData.SpecialTierWar, w);
            int levelTier = CountAtLeast(ProgressData.SpecialTierLevel, gen.level);
            int used = gen.specials.Count;
            int tier = Math.Min(statTier, Math.Min(levelTier, used + 1));
            return new SlotInfo { max = max, unlocked = unlocked, used = used, open = Math.Max(0, unlocked - used), tier = tier, statTier = statTier, levelTier = levelTier };
        }
        public static SlotInfo TacticSlots(General gen)
        {
            Ensure(gen);
            int v = gen.intel;
            int max = CountAtLeast(ProgressData.TacticSlotInt, v);
            int unlocked = Math.Min(max, CountAtLeast(ProgressData.TacticSlotLevel, gen.level));
            int statTier = CountAtLeast(ProgressData.TacticTierInt, v);
            int levelTier = CountAtLeast(ProgressData.TacticTierLevel, gen.level);
            int used = LearnedTactics(gen).Count;
            int tier = Math.Min(statTier, Math.Min(levelTier, used + 1));
            return new SlotInfo { max = max, unlocked = unlocked, used = used, open = Math.Max(0, unlocked - used), tier = tier, statTier = statTier, levelTier = levelTier };
        }
        public static Openings OpeningsOf(General gen)
        {
            int s = SpecialSlots(gen).open, t = TacticSlots(gen).open;
            return new Openings { special = s, tactic = t, total = s + t };
        }

        // 可学的条目（未学、等阶不超过上限）
        public static List<Special> EligibleSpecials(General gen)
        {
            Ensure(gen);
            var sl = SpecialSlots(gen);
            if (sl.open <= 0) return new List<Special>();
            // 可习得的暗杀每人至多一招（得手即整支溃散，多招叠加时主将被刺、一战定局的机会过大）
            bool hasAssn = gen.specials.Any(id => { var k = Specials.ById(id); return k != null && k.kind == "assassinate"; });
            return Specials.Pool().Where(sp => sp.tier <= sl.tier && !gen.specials.Contains(sp.id) && !(hasAssn && sp.kind == "assassinate")).ToList();
        }
        public static List<TacticDef> EligibleTactics(General gen)
        {
            Ensure(gen);
            var sl = TacticSlots(gen);
            if (sl.open <= 0) return new List<TacticDef>();
            return Tactics.Learnable().Where(t => t.Tier <= sl.tier && !gen.tactics.Contains(t.Id) && gen.intel >= t.ReqInt).ToList();
        }
        public static List<LearnCandidate> Eligible(General gen, string type)
        {
            return type == "special" ? EligibleSpecials(gen).Select(x => Cand(x)).ToList() : EligibleTactics(gen).Select(x => Cand(x)).ToList();
        }
        public static LearnCandidate Cand(Special x) { return new LearnCandidate { type = "special", id = x.id, tier = x.tier, name = x.name, desc = x.desc, rules = Specials.Rules(x), color = x.color, fx = x.fx, special = x }; }
        public static LearnCandidate Cand(TacticDef x) { return new LearnCandidate { type = "tactic", id = x.Id, tier = x.Tier, name = x.Name, desc = x.Desc, rules = Tactics.Rules(x), color = x.Color, fx = x.Fx, tactic = x }; }

        sealed class DrawPool { public string type; public List<LearnCandidate> list; public int cap; public bool drawn; }
        public static List<LearnCandidate> Candidates(General gen, int n = 3)
        {
            n = n > 0 ? n : 3;
            Ensure(gen);
            int seed = G != null && G.seed != 0 ? G.seed : 0;
            var r = new SeededRandom(PHash(gen.name + "|" + gen.level + "|" + string.Join(",", gen.specials.ToArray()) + "|" + string.Join(",", gen.tactics.ToArray()) + "|" + seed));
            var pools = new List<DrawPool>();
            foreach (var type in new[] { "special", "tactic" })
            {
                var list = Eligible(gen, type);
                if (list.Count == 0) continue;
                int cap = type == "special" ? SpecialSlots(gen).tier : TacticSlots(gen).tier;
                pools.Add(new DrawPool { type = type, list = list, cap = cap });
            }
            var outList = new List<LearnCandidate>();
            if (pools.Count == 0) return outList;
            // 加权不放回抽取：本等阶上限的条目 ×4、低一阶 ×2、其余 ×1；每类的第一个候选必为本等阶上限的条目
            // （新解锁的等阶一定出现在候选里，电脑 AiPick 也就总能学到当前最高阶）
            Func<DrawPool, LearnCandidate> draw = p =>
            {
                int tot = 0;
                bool capOnly = !p.drawn && p.list.Any(x => x.tier >= p.cap);
                p.drawn = true;
                var w = new int[p.list.Count];
                for (int i = 0; i < w.Length; i++) { var x = p.list[i]; int k = x.tier >= p.cap ? 4 : capOnly ? 0 : x.tier == p.cap - 1 ? 2 : 1; w[i] = k; tot += k; }
                double roll = r.NextDouble() * tot;
                for (int i = 0; i < p.list.Count; i++)
                {
                    roll -= w[i];
                    if ((roll < 0 && w[i] > 0) || i == p.list.Count - 1) { var c = p.list[i]; p.list.RemoveAt(i); return c; }
                }
                return null;
            };
            // 两类都有空格时交替抽取（武力 / 智力相对更高的一类先抽），保证两类都出现
            if (pools.Count == 2 && gen.intel > gen.war) pools.Reverse();
            for (int guard = 0; outList.Count < n && guard < 32; guard++)
            {
                var live = pools.Where(p => p.list.Count > 0).ToList();
                if (live.Count == 0) break;
                var c = draw(live[outList.Count % live.Count]);
                if (c != null) outList.Add(c);
            }
            return outList;
        }
        public static bool Learn(General gen, LearnCandidate c) { return c != null && Learn(gen, c.type, c.id); }
        public static bool Learn(General gen, string type, string id)
        {
            if (gen == null || string.IsNullOrEmpty(id) || (type != "special" && type != "tactic")) return false;
            if (type == "special") { if (!EligibleSpecials(gen).Any(x => x.id == id)) return false; gen.specials.Add(id); }
            else { if (!EligibleTactics(gen).Any(x => x.Id == id)) return false; gen.tactics.Add(id); }
            return true;
        }
        // 电脑的选择：高阶优先；同阶按武将所长（武力系伤害 / 智力系 / 辅助）与姓名哈希
        static readonly HashSet<string> Support = new HashSet<string> { "rally", "command", "fortify", "haste", "heal" };
        public static LearnCandidate AiPick(General gen, List<LearnCandidate> cands)
        {
            if (cands == null || cands.Count == 0) return null;
            int W = gen.war, I = gen.intel, Pl = gen.pol;
            LearnCandidate best = null; double bs = -1e9;
            foreach (var c in cands)
            {
                double s = c.tier * 10;
                if (c.type == "special") s += Support.Contains(c.Kind) ? (Pl >= W - 5 ? 3 : 0) : (W >= Pl ? 3 : 0);
                else s += c.tactic != null && c.tactic.Dmg != null ? (I >= 80 ? 3 : 1) : 2;
                s += (PHash(gen.name + "|" + c.id) % 100) / 100.0;
                if (s > bs) { bs = s; best = c; }
            }
            return best;
        }
        public static List<LearnCandidate> AutoLearn(General gen)
        {
            var outList = new List<LearnCandidate>();
            for (int guard = 0; guard < 12 && OpeningsOf(gen).total > 0; guard++)
            {
                var c = AiPick(gen, Candidates(gen, 3));
                if (c == null || !Learn(gen, c)) break;
                outList.Add(c);
            }
            return outList;
        }
        public static bool IsPlayerGen(General gen) { var g = G; return g != null && g.player >= 0 && gen != null && gen.faction == g.player; }
        public static List<General> Pending(int f)
        {
            var g = G;
            if (g == null) return new List<General>();
            return g.generals.Where(x => x.faction == f && !x.dead && OpeningsOf(x).total > 0 && Candidates(x, 1).Count > 0).ToList();
        }
        public static List<Special> KnownSpecials(General gen) { return Specials.Known(gen); }
        public static List<TacticDef> KnownTactics(General gen) { return Tactics.Known(gen); }

        public static ProgressResult Gain(General gen, double amount, string why = null)
        {
            if (!enabled || gen == null || !(amount > 0)) return null;
            Ensure(gen);
            int from = gen.level;
            int add = (int)JsRound(amount);
            gen.exp = Math.Min(gen.exp + add, Exp[MaxLevel]);
            gen.level = LevelFor(gen.exp);
            var res = new ProgressResult { gen = gen, exp = add, why = why ?? "", from = from, to = gen.level };
            bool player = IsPlayerGen(gen);
            if (!player || playerAutoLearn) res.learned = AutoLearn(gen);
            if (player && gen.level > from && G != null)
                G.Log(gen.name + "升至 " + gen.level + " 级" + (res.learned.Count > 0 ? "，习得" + string.Concat(res.learned.Select(c => "「" + c.name + "」")) : "") + "。");
            return res;
        }
        // 一战所得经验（加法的分组与网页版相同，浮点结果逐位一致）。各来源的权重在网页版 battleExp 里就是字面常数（无数据表可导出），
        //   此处照抄；导出工具把 battleExp / autoBattle 对一组探针的结果写到 Tests~/sim/ref/progress-ref.txt，HarnessProgress 的 (exp-ref) 节逐行对照
        public static int BattleExp(double dmg, double heal, double routs, double cmdRouts, double duelWin, double duelLoss, double specials, double tactics, double tacFail, bool won, bool commander)
        {
            double e = 10;
            e += dmg / 40 + heal / 80;
            e += routs * 12 + cmdRouts * 12;
            e += duelWin * 25 + duelLoss * 5;
            e += specials * 6 + tactics * 4 + tacFail;
            if (won) e += 25 + (commander ? 10 : 0);
            return (int)JsRound(Math.Min(BattleCap, e));
        }
        public static int BattleExp(BattleXp x, bool won, bool commander)
        {
            x = x ?? new BattleXp();
            return BattleExp(x.dmg, x.heal, x.routs, x.cmdRouts, x.duelWin, x.duelLoss, x.specials, x.tactics, x.tacFail, won, commander);
        }
        // 战绩（战术战斗与自动结算共用）
        public static void Note(General gen, bool won, int routs, int duels)
        {
            Ensure(gen);
            var r = gen.record;
            r.battles++;
            if (won) r.wins++;
            r.routs += routs;
            r.duels += duels;
        }

        // ---- 自动结算的攻城战 ----
        public static Dictionary<int, ProgressSnap> Snapshot(BattleSetup s)
        {
            var m = new Dictionary<int, ProgressSnap>();
            foreach (var x in s.atk.Concat(s.def)) m[x.id] = new ProgressSnap { troops = Math.Max(0, x.troops), power = GenPower(x) };
            return m;
        }
        public static List<ProgressResult> AutoBattle(BattleSetup s, Dictionary<int, ProgressSnap> snap)
        {
            if (!enabled || s == null || s.progressDone || snap == null) return new List<ProgressResult>();
            s.progressDone = true;
            var outList = s.progress = new List<ProgressResult>();          // 与战术战斗的 setup.progress 同格式（界面战后统一读取）
            var sides = new[] { s.atk, s.def };
            ProgressSnap b0;
            // 无人防守，不战而下：攻方只得少量经验
            if (sides[1].Count == 0 || sides[1].All(d => !(snap.TryGetValue(d.id, out b0) && b0.troops > 0)))
            {
                foreach (var a in sides[0]) { var r = Gain(a, 5, "battle"); if (r != null) { r.side = 0; r.won = true; outList.Add(r); } }
                return outList;
            }
            for (int k = 0; k < 2; k++)
            {
                var mine = sides[k]; var foes = sides[1 - k];
                bool won = k == 0 ? s.attackerWon : !s.attackerWon;
                double loss = 0; int routs = 0, cmd = 0;
                for (int i = 0; i < foes.Count; i++)
                {
                    var e = foes[i]; ProgressSnap b;
                    if (!snap.TryGetValue(e.id, out b)) continue;
                    loss += Math.Max(0, b.troops - Math.Max(0, e.troops));
                    if (b.troops > 0 && (s.routed.Contains(e.id) || e.troops <= 0)) { routs++; if (i == 0) cmd++; }
                }
                double tot = 0;
                foreach (var a in mine) { ProgressSnap b; tot += snap.TryGetValue(a.id, out b) ? b.power : 0; }
                for (int i = 0; i < mine.Count; i++)
                {
                    var a = mine[i]; ProgressSnap b;
                    if (!snap.TryGetValue(a.id, out b) || !(b.troops > 0)) continue;
                    double share = tot > 0 ? b.power / tot : 1.0 / mine.Count;
                    int e = BattleExp(loss * share, 0, routs * share, cmd * share, 0, 0, 0, 0, 0, won, i == 0);
                    Note(a, won, (int)JsRound(routs * share), 0);
                    int from = a.level;
                    var r = Gain(a, e, "battle");
                    if (r != null) { r.side = k; r.won = won; outList.Add(r); }
                    else outList.Add(new ProgressResult { gen = a, exp = 0, from = from, to = from, side = k, won = won });
                }
            }
            return outList;
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
