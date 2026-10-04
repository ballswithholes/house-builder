using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        // 地图坐标（经纬度换算）
        public Vector2 MapPos { get { return new Vector2((lon - 100f) * 5f, (lat - 23f) * 5.6f); } }
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
        public Color Col { get { Color c; ColorUtility.TryParseHtmlString(color, out c); return c; } }
    }

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

        public static GameState Current;

        public int MonthIndex { get { return year * 12 + month; } }

        // ------------------------------------------------------------ 创建 --
        public static GameState NewGame(string playerFactionKey)
        {
            var g = new GameState { year = ScenarioData.StartYear, month = ScenarioData.StartMonth, seed = UnityEngine.Random.Range(1, 999999) };
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in ScenarioData.Cities)
            {
                var p = line.Split('|');
                g.cities.Add(new City
                {
                    id = g.cities.Count, key = p[0], name = p[1], lon = float.Parse(p[2], inv), lat = float.Parse(p[3], inv),
                    land = int.Parse(p[4]), industry = int.Parse(p[5]), town = int.Parse(p[6]), population = int.Parse(p[7]),
                    gold = int.Parse(p[8]), food = int.Parse(p[9]),
                });
            }
            foreach (var line in ScenarioData.Factions)
            {
                var p = line.Split('|');
                g.factions.Add(new Faction { id = g.factions.Count, key = p[0], name = p[1], color = p[3], virtue = int.Parse(p[4]), fame = int.Parse(p[5]), ruler = -1 });
            }
            foreach (var line in ScenarioData.Generals)
            {
                var p = line.Split('|');
                var gen = new General
                {
                    id = g.generals.Count, name = p[0], war = int.Parse(p[1]), intel = int.Parse(p[2]), pol = int.Parse(p[3]),
                    faction = p[4] == "-" ? -1 : g.FactionByKey(p[4]).id, city = g.CityByKey(p[5]).id,
                    loyalty = int.Parse(p[6]), troops = int.Parse(p[7]), hidden = p[8] == "1",
                };
                gen.training = gen.faction >= 0 ? 55 + UnityEngine.Random.Range(0, 20) : 40;
                g.generals.Add(gen);
            }
            // 君主
            for (int i = 0; i < g.factions.Count; i++)
            {
                var rulerName = ScenarioData.Factions[i].Split('|')[2];
                var r = g.generals.First(x => x.name == rulerName);
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

        // 读档或新建后重建非序列化数据
        public void Init()
        {
            foreach (var c in cities) c.links = new List<int>();
            foreach (var l in ScenarioData.Links)
            {
                var p = l.Split('-');
                var a = CityByKey(p[0]); var b = CityByKey(p[1]);
                a.links.Add(b.id); b.links.Add(a.id);
            }
        }

        // ------------------------------------------------------------ 查询 --
        public City CityByKey(string k) { return cities.First(c => c.key == k); }
        public Faction FactionByKey(string k) { return factions.First(f => f.key == k); }
        public Faction PlayerFaction { get { return factions[player]; } }
        public IEnumerable<City> CitiesOf(int f) { return cities.Where(c => c.owner == f); }
        public IEnumerable<General> GeneralsOf(int f) { return generals.Where(x => x.faction == f && !x.dead); }
        public IEnumerable<General> GeneralsIn(int city, int f) { return generals.Where(x => x.city == city && x.faction == f && !x.dead); }
        public IEnumerable<General> OfficersIn(City c) { return c.owner < 0 ? Enumerable.Empty<General>() : GeneralsIn(c.id, c.owner); }
        public IEnumerable<General> FreeFoundIn(int city) { return generals.Where(x => x.city == city && x.IsFree && !x.hidden); }
        public int TroopsIn(City c) { return OfficersIn(c).Sum(x => x.troops); }
        public bool IsRuler(General g) { return g.faction >= 0 && factions[g.faction].ruler == g.id; }
        public General Ruler(int f) { return generals[factions[f].ruler]; }
        public int CityCount(int f) { return cities.Count(c => c.owner == f); }

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

        public void Log(string s)
        {
            log.Add(year + "年" + month + "月 " + s);
            if (log.Count > 80) log.RemoveAt(0);
        }

        // ------------------------------------------------------------ 存档 --
        public static string SavePath { get { return System.IO.Path.Combine(Application.persistentDataPath, "sanguozhi2_save.json"); } }
        public static bool HasSave { get { try { return System.IO.File.Exists(SavePath); } catch { return false; } } }
        public void Save() { System.IO.File.WriteAllText(SavePath, JsonUtility.ToJson(this)); }
        public static GameState Load()
        {
            var g = JsonUtility.FromJson<GameState>(System.IO.File.ReadAllText(SavePath));
            g.Init();
            return g;
        }
    }
}
