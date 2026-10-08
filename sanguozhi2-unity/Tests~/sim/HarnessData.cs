// (D) 生成数据自检：用与导出工具相同的遍历重算 SpecialsData / WorldScenarioData / WorldGeoData 的哈希，
// 与文件里的 Hash 常量比对（确认 C# 数据与 JS 完全一致：字符串转义、浮点字面量、条目顺序）；
// 再做一致性检查：世界剧本的经典部分与 ScenarioData 相同、连线端点存在、海路 ⊂ 连线、地域覆盖全部势力、
// 每位世界武将都有手写必杀技、招式名唯一、参数名属于其机制且在范围内、地理点数组成对。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sanguo;

class PD
{
    static int Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Specials();
        World();
        Geo();
        Console.WriteLine(Check.Fails == 0 ? "ALL OK" : "FAILURES: " + Check.Fails);
        return Check.Fails;
    }

    static void Specials()
    {
        Console.WriteLine("== specials data: " + SpecialsData.Summary);
        var h = new DataHash();
        foreach (var e in SpecialsData.Entries)
        {
            h.S(e.Gen); h.S(e.Name); h.S(e.Kind); h.S(e.Stat); h.S(e.Fx); h.S(e.Color); h.S(e.Cry); h.S(e.Desc); h.S(e.Lore);
            h.I(e.Keys.Length);
            for (int i = 0; i < e.Keys.Length; i++) { h.S(e.Keys[i]); h.N(e.Values[i]); }
        }
        h.I(SpecialsData.Kinds.Length);
        foreach (var k in SpecialsData.Kinds)
        {
            h.S(k.Kind); h.S(k.Label); h.S(k.Stat); h.S(k.Target); h.S(k.Fx); h.S(k.Color); h.I(k.Params.Length);
            for (int i = 0; i < k.Params.Length; i++) { h.S(k.Params[i]); h.N(k.Def[i]); h.N(k.Min[i]); h.N(k.Max[i]); h.N(k.Step[i]); }
        }
        h.SA(SpecialsData.FxStyles);
        h.N(SpecialsData.FloodRiver); h.N(SpecialsData.FloodDry); h.N(SpecialsData.ChargeCap); h.N(SpecialsData.AreaTotal);
        h.I(SpecialsData.EpithetKeys.Length);
        for (int i = 0; i < SpecialsData.EpithetKeys.Length; i++) { h.S(SpecialsData.EpithetKeys[i]); h.SA(SpecialsData.Epithets[i]); }
        h.I(SpecialsData.NounKeys.Length);
        for (int i = 0; i < SpecialsData.NounKeys.Length; i++) { h.S(SpecialsData.NounKeys[i]); h.SA(SpecialsData.Nouns[i]); }
        h.I(SpecialsData.FlavorKeys.Length);
        for (int i = 0; i < SpecialsData.FlavorKeys.Length; i++) { h.S(SpecialsData.FlavorKeys[i]); h.S(SpecialsData.Flavors[i]); }
        h.SA(SpecialsData.Nanman); h.SA(SpecialsData.MetaKeys); h.SA(SpecialsData.Stats);
        Check.That(h.Value == SpecialsData.Hash, $"SpecialsData hash {h.Hex} != 0x{SpecialsData.Hash:x8}");
        Check.That(SpecialsData.Entries.Length == SpecialsData.EntryCount && SpecialsData.Kinds.Length == SpecialsData.KindCount, "SpecialsData counts");

        // 招式名唯一、武将名唯一、机制存在、参数名属于机制且在范围内
        var names = new HashSet<string>(); var gens = new HashSet<string>();
        int bad = 0;
        foreach (var e in SpecialsData.Entries)
        {
            if (!names.Add(e.Name)) { bad++; Check.That(false, "duplicate special name " + e.Name); }
            if (!gens.Add(e.Gen)) { bad++; Check.That(false, "duplicate general " + e.Gen); }
            var k = SpecialsData.KindOf(e.Kind);
            if (!Check.That(k != null, e.Gen + ": unknown kind " + e.Kind)) continue;
            for (int i = 0; i < e.Keys.Length; i++)
            {
                int j = k.IndexOf(e.Keys[i]);
                if (!Check.That(j >= 0, $"{e.Gen}: param {e.Keys[i]} not in {e.Kind}")) continue;
                Check.That(e.Values[i] >= k.Min[j] - 1e-6f && e.Values[i] <= k.Max[j] + 1e-6f, $"{e.Gen}: {e.Keys[i]}={e.Values[i]} outside [{k.Min[j]}, {k.Max[j]}]");
            }
            Check.That(e.Stat == null || SpecialsData.Stats.Contains(e.Stat), e.Gen + ": bad stat " + e.Stat);
            Check.That(e.Fx == null || SpecialsData.FxStyles.Contains(e.Fx), e.Gen + ": bad fx " + e.Fx);
            Check.That(e.Desc != null || e.Lore != null, e.Gen + ": neither desc nor lore");
            Check.That(SpecialsData.Get(e.Gen) == e, e.Gen + ": Get() lookup");
        }
        // 词库：每种能力、每种机制都有词
        foreach (var s in SpecialsData.Stats) Check.That(Array.IndexOf(SpecialsData.EpithetKeys, s) >= 0 && Array.IndexOf(SpecialsData.FlavorKeys, s) >= 0, "epithet/flavor bank for " + s);
        foreach (var k in SpecialsData.Kinds) Check.That(Array.IndexOf(SpecialsData.NounKeys, k.Kind) >= 0, "noun bank for " + k.Kind);
        Console.WriteLine($"  {SpecialsData.Entries.Length} entries, {names.Count} unique names, {SpecialsData.Kinds.Length} kinds, hash {h.Hex}");
    }

    static void World()
    {
        Console.WriteLine("== world scenario data: " + WorldScenarioData.Summary);
        var h = new DataHash();
        h.I(WorldScenarioData.StartYear); h.I(WorldScenarioData.StartMonth); h.S(WorldScenarioData.Title); h.S(WorldScenarioData.Intro);
        foreach (var t in new[] { WorldScenarioData.Cities, WorldScenarioData.Links, WorldScenarioData.SeaLinks, WorldScenarioData.Factions,
                     WorldScenarioData.Generals, WorldScenarioData.Regions, WorldScenarioData.GeneralBorn, WorldScenarioData.ClassicFemale,
                     WorldScenarioData.NanmanGenerals, WorldScenarioData.NanmanCities, WorldScenarioData.NanmanFactions })
            h.SA(t);
        h.I(WorldScenarioData.GeneralInfo.Length);
        foreach (var w in WorldScenarioData.GeneralInfo)
        {
            h.S(w.Name); h.S(w.Full); h.S(w.Orig); h.S(w.Role); h.S(w.Note); h.S(w.Liberty); h.I(w.Born); h.I(w.Died);
            h.S(w.Attested); h.S(w.Culture); h.S(w.Sex);
        }
        h.I(WorldScenarioData.FactionInfo.Length);
        foreach (var w in WorldScenarioData.FactionInfo)
        {
            h.S(w.Key); h.S(w.Name); h.S(w.Full); h.S(w.Orig); h.S(w.Ruler); h.S(w.Capital); h.S(w.Culture); h.S(w.Region); h.S(w.Note); h.S(w.Liberty);
        }
        h.I(WorldScenarioData.CityInfo.Length);
        foreach (var w in WorldScenarioData.CityInfo) { h.S(w.Key); h.S(w.Full); h.S(w.Orig); h.S(w.Modern); h.S(w.Note); h.S(w.Culture); }
        Check.That(h.Value == WorldScenarioData.Hash, $"WorldScenarioData hash {h.Hex} != 0x{WorldScenarioData.Hash:x8}");

        // 经典部分与 ScenarioData 一致（world-data.js 原样复制，只在行尾加列）
        Func<string[], string[], string, bool> prefix = (w, c, what) =>
        {
            bool ok = w.Length >= c.Length;
            for (int i = 0; ok && i < c.Length; i++) ok = w[i] == c[i] || w[i].StartsWith(c[i] + "|", StringComparison.Ordinal);
            return Check.That(ok, "world " + what + " do not start with ScenarioData." + what);
        };
        prefix(WorldScenarioData.Cities, ScenarioData.Cities, "Cities");
        prefix(WorldScenarioData.Links, ScenarioData.Links, "Links");
        prefix(WorldScenarioData.Factions, ScenarioData.Factions, "Factions");
        prefix(WorldScenarioData.Generals, ScenarioData.Generals, "Generals");

        var inv = CultureInfo.InvariantCulture;
        var cities = new HashSet<string>();
        foreach (var l in WorldScenarioData.Cities)
        {
            var p = l.Split('|');
            Check.That(p.Length == 11, "city columns: " + l);
            Check.That(cities.Add(p[0]), "duplicate city " + p[0]);
            float lon, lat; int n;
            Check.That(float.TryParse(p[2], NumberStyles.Float, inv, out lon) && float.TryParse(p[3], NumberStyles.Float, inv, out lat), "city lon/lat " + p[0]);
            for (int i = 4; i <= 9; i++) Check.That(int.TryParse(p[i], NumberStyles.Integer, inv, out n) && n >= 0, "city number column " + i + " " + p[0]);
        }
        var links = new HashSet<string>();
        foreach (var l in WorldScenarioData.Links)
        {
            var ab = l.Split('-');
            Check.That(ab.Length == 2 && cities.Contains(ab[0]) && cities.Contains(ab[1]) && ab[0] != ab[1], "bad link " + l);
            Check.That(links.Add(l), "duplicate link " + l);
        }
        foreach (var l in WorldScenarioData.SeaLinks) Check.That(links.Contains(l), "sea link not in Links: " + l);
        var factions = new HashSet<string>();
        foreach (var l in WorldScenarioData.Factions)
        {
            var p = l.Split('|');
            Check.That(p.Length == 8, "faction columns: " + l);
            Check.That(factions.Add(p[0]), "duplicate faction " + p[0]);
        }
        var gens = new HashSet<string>();
        foreach (var l in WorldScenarioData.Generals)
        {
            var p = l.Split('|');
            Check.That(p.Length == 11, "general columns: " + l);
            Check.That(gens.Add(p[0]), "duplicate general " + p[0]);
            Check.That(p[4] == "-" || factions.Contains(p[4]), "general faction " + l);
            Check.That(cities.Contains(p[5]), "general city " + l);
            Check.That(SpecialsData.Get(p[0]) != null, "no hand-made special for " + p[0]);
        }
        foreach (var l in WorldScenarioData.Factions) { var p = l.Split('|'); Check.That(gens.Contains(p[2]), "faction ruler missing " + l); }
        // 地域：覆盖全部势力，每个势力恰好一次
        var seen = new Dictionary<string, int>();
        foreach (var r in WorldScenarioData.Regions)
        {
            var p = r.Split('|');
            if (!Check.That(p.Length == 3, "region columns " + r)) continue;
            foreach (var f in p[2].Split(',')) { Check.That(factions.Contains(f), "region faction " + f); seen[f] = seen.TryGetValue(f, out var c) ? c + 1 : 1; }
        }
        foreach (var f in factions) Check.That(seen.TryGetValue(f, out var c) && c == 1, "faction in regions " + f + " x" + (seen.ContainsKey(f) ? seen[f] : 0));
        foreach (var w in WorldScenarioData.GeneralInfo)
        {
            Check.That(gens.Contains(w.Name), "GeneralInfo for unknown general " + w.Name);
            Check.That(w.Sex == "m" || w.Sex == "f", "sex " + w.Name);
            Check.That(WorldScenarioData.InfoOfGeneral(w.Name) == w, "InfoOfGeneral " + w.Name);
        }
        foreach (var w in WorldScenarioData.FactionInfo)
        {
            Check.That(factions.Contains(w.Key) && cities.Contains(w.Capital) && gens.Contains(w.Ruler), "FactionInfo " + w.Key);
            Check.That(WorldScenarioData.InfoOfFaction(w.Key) == w, "InfoOfFaction " + w.Key);
        }
        foreach (var w in WorldScenarioData.CityInfo) Check.That(cities.Contains(w.Key) && WorldScenarioData.InfoOfCity(w.Key) == w, "CityInfo " + w.Key);
        foreach (var b in WorldScenarioData.GeneralBorn) { var p = b.Split('|'); Check.That(p.Length == 2 && gens.Contains(p[0]), "GeneralBorn " + b); }
        int female = WorldScenarioData.GeneralInfo.Count(w => w.Female) + WorldScenarioData.ClassicFemale.Length;
        Console.WriteLine($"  {cities.Count} cities, {links.Count} links ({WorldScenarioData.SeaLinks.Length} sea), {factions.Count} factions, {gens.Count} generals ({female} female), hash {h.Hex}");
    }

    static void Geo()
    {
        Console.WriteLine("== world geo data: " + WorldGeoData.Summary);
        var h = new DataHash();
        h.NA(WorldGeoData.LonSeg); h.NA(WorldGeoData.LonBreakX); h.NA(WorldGeoData.LatChina); h.NA(WorldGeoData.LatWest);
        h.N(WorldGeoData.TransLon0); h.N(WorldGeoData.TransLon1);
        foreach (var v in new[] { WorldGeoData.Lon0, WorldGeoData.Lon1, WorldGeoData.Lat0, WorldGeoData.Lat1,
                     WorldGeoData.ChinaXMin, WorldGeoData.ChinaYMin, WorldGeoData.ChinaXMax, WorldGeoData.ChinaYMax }) h.N(v);
        h.I(WorldGeoData.Regions.Length);
        foreach (var r in WorldGeoData.Regions) { h.S(r.Key); h.S(r.Name); h.N(r.Lon0); h.N(r.Lon1); h.N(r.Lat0); h.N(r.Lat1); }
        h.I(WorldGeoData.Land.Length); foreach (var r in WorldGeoData.Land) { h.S(r.Name); h.NA(r.Pts); }
        h.I(WorldGeoData.Rivers.Length); foreach (var r in WorldGeoData.Rivers) { h.S(r.Name); h.N(r.W); h.N(r.D); h.NA(r.Pts); }
        h.NA(WorldGeoData.Lakes);
        h.I(WorldGeoData.Ridges.Length); foreach (var r in WorldGeoData.Ridges) { h.S(r.Name); h.N(r.A); h.N(r.B); h.N(r.W); h.NA(r.Pts); }
        h.I(WorldGeoData.Plateaus.Length); foreach (var r in WorldGeoData.Plateaus) { h.S(r.Name); h.N(r.H); h.NA(r.Pts); }
        foreach (var k in WorldGeoData.BiomeKeys)
        {
            var list = WorldGeoData.Biome(k);
            h.S(k); h.I(list.Length);
            foreach (var b in list) { h.S(b.Name); h.N(b.V); h.NA(b.Pts); }
        }
        h.I(WorldGeoData.ChinaOld.Length); foreach (var r in WorldGeoData.ChinaOld) h.NA(r);
        h.I(WorldGeoData.EastAsia.Length); foreach (var r in WorldGeoData.EastAsia) h.NA(r);
        Check.That(h.Value == WorldGeoData.Hash, $"WorldGeoData hash {h.Hex} != 0x{WorldGeoData.Hash:x8}");

        Action<string, float[], int> pts = (who, a, min) => Check.That(a.Length % 2 == 0 && a.Length >= 2 * min, who + ": point array length " + a.Length);
        foreach (var r in WorldGeoData.Land) pts(r.Name, r.Pts, 3);
        foreach (var r in WorldGeoData.Plateaus) pts(r.Name, r.Pts, 3);
        foreach (var k in WorldGeoData.BiomeKeys) foreach (var b in WorldGeoData.Biome(k)) pts(b.Name, b.Pts, 3);
        foreach (var r in WorldGeoData.Rivers) pts(r.Name, r.Pts, 2);
        foreach (var r in WorldGeoData.Ridges) pts(r.Name, r.Pts, 2);
        Check.That(WorldGeoData.Lakes.Length % 3 == 0, "lakes triples");
        Check.That(WorldGeoData.LonSeg.Length == 2 * WorldGeoData.LonBreakX.Length, "LonSeg / LonBreakX lengths");
        for (int i = 1; i < WorldGeoData.LonBreakX.Length; i++)
            Check.That(Math.Abs(WorldGeoData.LonBreakX[i] - WorldGeoData.LonBreakX[i - 1] - (WorldGeoData.LonSeg[2 * i] - WorldGeoData.LonSeg[2 * i - 2]) * WorldGeoData.LonSeg[2 * i - 1]) < 1e-3f,
                "LonBreakX[" + i + "] follows LonSeg");
        int n = WorldGeoData.Land.Sum(r => r.Pts.Length / 2);
        Console.WriteLine($"  {WorldGeoData.Land.Length} land rings ({n} points), {WorldGeoData.Rivers.Length} rivers, {WorldGeoData.Ridges.Length} ridges, hash {h.Hex}");
    }
}
