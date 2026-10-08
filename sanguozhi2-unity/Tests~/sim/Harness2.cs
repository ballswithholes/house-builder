// tests/sim.js (2)(3) 的 C# 版：
//   (2) 随机玩家模拟：6 个势力 × 4 个种子 × 25 年，玩家每月随机下达指令（随机玩家用 SeededRandom，与 JS 同一序列）；
//   (3) 存档往返：JSON 结构与网页版相同（不含 city.links），PlayerPrefs 读写、读档失败的中文原因、世界剧本存档。
// 环境变量 SIM_DUMP=路径：把 (3) 的 14 个月对局 JSON 写到该文件（与 JS 的 JSON.stringify(G) 逐字比对用）。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;

class P2
{
    static int Main()
    {
        BattleModel.specialsEnabled = true;   // 同网页版默认（游戏内暂时默认 false，见 BattleModel.specialsEnabled）
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        int fails = 0;
        // ========================================================= (2) 随机玩家模拟 --
        Console.WriteLine("== (2) random-player simulation: 6 factions x 4 seeds x 25 years ==");
        foreach (var key in new[] { "liubei", "cao", "dong", "kongrong", "menghuo", "lukang" })
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                UnityEngine.Random.InitState(seed * 13 + key.Length);
                var g = GameState.Current = GameState.NewGame(key);
                var rnd = new SeededRandom(seed);
                int month = 0, attacks = 0, defends = 0, execs = 0, hires = 0, releases = 0;
                try
                {
                    for (; month < 12 * 25 && g.PlayerFaction.alive; month++)
                    {
                        g.tokens = g.TokensFor(g.player);
                        int guard = 0;
                        while (g.tokens > 0 && guard++ < 30)
                        {
                            var cities = g.CitiesOf(g.player).ToList(); if (cities.Count == 0) break;
                            var c = cities[rnd.Next(cities.Count)];
                            var offs = g.OfficersIn(c).ToList(); if (offs.Count == 0) { g.tokens--; continue; }
                            var gen = offs[rnd.Next(offs.Count)];
                            int op = rnd.Next(9);
                            switch (op)
                            {
                                case 0: Commands.Develop(c, gen, (DevKind)rnd.Next(3)); break;
                                case 1: Commands.Recruit(c, gen, Commands.RecruitMax(c, gen)); break;
                                case 2: Commands.Train(gen); break;
                                case 3: Commands.Search(c, gen); if (Commands.SearchFound != null && Commands.Hire(Commands.SearchFound, gen, g.player, c.id)) hires++; break;
                                case 4: { var d = c.links.Select(i => g.cities[i]).FirstOrDefault(x => x.owner == g.player); if (d != null && !gen.moved) Commands.Move(gen, d); break; }
                                case 5:
                                    {
                                        General t = null;
                                        foreach (var x in c.links.Select(i => g.cities[i]).Where(x => x.owner >= 0 && x.owner != g.player).ToList())
                                        {
                                            t = g.OfficersIn(x).FirstOrDefault(y => !g.IsRuler(y));
                                            if (t != null) break;
                                        }
                                        if (t != null) { if (rnd.Next(2) == 0) Commands.Discord(gen, t); else Commands.Persuade(gen, t, g.player); }
                                        break;
                                    }
                                case 6:
                                    {
                                        var cand = g.factions.Where(x => x.alive && x.id != g.player).Select(x => new { x, k = rnd.Next(2147483647) }).ToList();
                                        var f = cand.OrderBy(o => o.k).Select(o => o.x).FirstOrDefault();
                                        if (f != null) Commands.Ally(c, g.player, f.id, 100);
                                        break;
                                    }
                                case 7: Commands.BuyFood(c, Math.Min(c.gold, 50)); break;
                                default:
                                    {
                                        var ts = c.links.Select(i => g.cities[i]).Where(x => x.owner != g.player && !g.Allied(x.owner, g.player)).ToList();
                                        var t = ts.OrderBy(x => g.TroopsIn(x)).FirstOrDefault();
                                        var squad = offs.Where(x => !x.moved && x.troops > 0).Take(5).ToList();
                                        if (t != null && squad.Count > 0)
                                        {
                                            attacks++;
                                            var s = Conquest.Prepare(g.player, c, t, squad, Math.Min(c.food, 2000), 0);
                                            Conquest.AutoResolve(s); Conquest.Apply(s);
                                            foreach (var cap in s.captives.ToList())
                                            {
                                                if (!s.attackerWon) { Conquest.AiDecideCaptives(s, s.defender); break; }
                                                int ch = rnd.Next(3); bool ruler = cap.faction >= 0 && g.factions[cap.faction].ruler == cap.id;
                                                if (ch == 0 && !ruler) { if (!Commands.Hire(cap, g.Ruler(g.player), g.player, s.target.id)) { Conquest.Release(cap); releases++; } }
                                                else if (ch == 2) { Conquest.Execute(cap); execs++; }
                                                else { Conquest.Release(cap); releases++; }
                                            }
                                            foreach (var f in g.factions) if (f.alive && g.CityCount(f.id) == 0) g.CheckFactionDeath(f.id);
                                        }
                                        break;
                                    }
                            }
                            g.tokens--;
                        }
                        var news = new List<string>();
                        var pb = StrategyAI.RunAI(news);
                        foreach (var s in pb)
                        {
                            defends++;
                            Conquest.AutoResolve(s); Conquest.Apply(s);
                            if (s.captives.Count > 0) Conquest.AiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
                            foreach (var f in g.factions) if (f.alive && g.CityCount(f.id) == 0) g.CheckFactionDeath(f.id);
                            if (!g.PlayerFaction.alive) break;
                        }
                        if (g.PlayerFaction.alive) StrategyAI.EndMonth();
                        Sim.Consistency(g);
                    }
                    Console.WriteLine($"{key} seed {seed}: {month} mo, alive={(g.PlayerFaction.alive ? "true" : "false")} cities={g.CityCount(g.player)} gens={g.GeneralsOf(g.player).Count()} atk={attacks} def={defends} hires={hires} execs={execs} releases={releases}");
                }
                catch (Exception e) { fails++; Console.WriteLine($"{key} seed {seed} FAILED month {month}: {e}"); }
            }
        }

        // ============================================================ (3) 存档往返 --
        Console.WriteLine("== (3) save/load round trip ==");
        try
        {
            GameState.DeleteSave(); UnityEngine.PlayerPrefs.DeleteAll();
            if (GameState.HasSave) throw new Exception("HasSave with empty PlayerPrefs");
            if (GameState.Load() != null || GameState.LoadError != "没有存档。") throw new Exception("Load() without a save: " + GameState.LoadError);
            UnityEngine.Random.InitState(99);
            var g = GameState.Current = GameState.NewGame("cao");
            for (int i = 0; i < 14; i++) { StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth(); }
            var text = g.ToJson();
            var dump = Environment.GetEnvironmentVariable("SIM_DUMP");
            if (!string.IsNullOrEmpty(dump)) System.IO.File.WriteAllText(dump, text);
            if (text.IndexOf("\"links\"") >= 0) throw new Exception("save JSON must not contain city.links");
            var back = GameState.FromJson(text);
            if (back.ToJson() != text) throw new Exception("JSON round trip mismatch");
            for (int i = 0; i < g.cities.Count; i++) if (string.Join(",", back.cities[i].links) != string.Join(",", g.cities[i].links)) throw new Exception("links not rebuilt");
            if (!g.Save() || !GameState.HasSave) throw new Exception("Save() failed");
            var loaded = GameState.Load();
            if (loaded == null || loaded.ToJson() != text || loaded.MonthIndex != g.MonthIndex) throw new Exception("Load() mismatch: " + GameState.LoadError);
            var info = GameState.GetSaveInfo();
            if (info == null || info.scenario != "classic" || info.year != g.year || info.month != g.month || info.faction != g.PlayerFaction.name) throw new Exception("GetSaveInfo mismatch");
            g.Log("测试日志");
            if (g.log.Count == 0 || g.log[g.log.Count - 1].IndexOf("测试日志") < 0) throw new Exception("Log(text) did not append");
            GameState.DeleteSave();
            if (GameState.HasSave) throw new Exception("DeleteSave() failed");
            // 读档失败的原因（与网页版相同的中文说明）
            UnityEngine.PlayerPrefs.SetString(GameState.LegacySaveKey, "{}");
            if (!GameState.HasLegacySave || GameState.Load() != null || GameState.LoadError != "新版本，旧存档无法读取。") throw new Exception("legacy save: " + GameState.LoadError);
            UnityEngine.PlayerPrefs.DeleteKey(GameState.LegacySaveKey);
            Func<string, string> loadErr = t => { UnityEngine.PlayerPrefs.SetString(GameState.SaveKey, t); var r = GameState.Load(); return r == null ? GameState.LoadError : "(loaded)"; };
            var cases = new[]
            {
                new[] { text.Replace("\"version\":2", "\"version\":1"), "新版本，旧存档无法读取。" },
                new[] { text.Replace("\"scenario\":\"classic\"", "\"scenario\":\"mars\""), "存档所用的剧本（mars）不存在，无法读取。" },
                new[] { text.Replace("\"key\":\"luoyang\"", "\"key\":\"luoyang2\""), "存档与当前版本的剧本数据不符，无法读取。" },
                new[] { text.Substring(0, text.Length / 2), "存档已损坏，无法读取。" },
                new[] { "[1,2,3]", "存档已损坏，无法读取。" },
            };
            foreach (var cs in cases) { var e = loadErr(cs[0]); if (e != cs[1]) throw new Exception("load error: got " + e + ", want " + cs[1]); }
            if (Scenarios.Current != Scenarios.Classic) throw new Exception("failed load must not switch the scenario");
            // 世界剧本：存档带剧本键，读档时切换剧本并重建连线 / 海路 / 文化
            UnityEngine.Random.InitState(5);
            var w = GameState.Current = GameState.NewGame("roma", "world");
            for (int i = 0; i < 3; i++) { StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth(); }
            var wt = w.ToJson();
            Scenarios.Current = Scenarios.Classic;
            if (!w.Save()) throw new Exception("world Save failed");
            var wl = GameState.Load();
            if (wl == null || wl.scenario != "world" || Scenarios.Current != Scenarios.World || wl.ToJson() != wt) throw new Exception("world load mismatch: " + GameState.LoadError);
            if (wl.seaLinks.Count != w.seaLinks.Count || wl.cities.Where((c, i) => c.culture != w.cities[i].culture).Any() || wl.generals.Where((x, i) => x.culture != w.generals[i].culture || x.born != w.generals[i].born || x.sex != w.generals[i].sex).Any())
                throw new Exception("world derived fields not rebuilt");
            if (GameState.GetSaveInfo().scenario != "world") throw new Exception("GetSaveInfo scenario");
            GameState.DeleteSave(); Scenarios.Current = Scenarios.Classic;
            Console.WriteLine($"save/load OK ({text.Length} bytes, log {g.log.Count} entries)");
        }
        catch (Exception e) { fails++; Scenarios.Current = Scenarios.Classic; Console.WriteLine("save/load FAILED: " + e); }

        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
}
