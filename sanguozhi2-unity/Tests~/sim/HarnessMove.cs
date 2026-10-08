// 调动武将（第二版）逻辑测试：tests/move.js 的 C# 版。
// 在构造的局面上检查：经由己方城池链的可达性（BFS）、路程、移动 / 输送规则与不可用原因；随机局面与 Floyd–Warshall 参照实现比对；
// 电脑调动（两步之内）不会被新规则拒绝。
using System;
using System.Collections.Generic;
using System.Linq;
using Sanguo;

class PM
{
    static int fails, checks;
    static void Ok(bool cond, string msg) { checks++; if (!cond) { fails++; Console.WriteLine("  FAIL: " + msg); } }
    static void Eq(string a, string b, string msg) { Ok(a == b, msg + "  got " + a + " want " + b); }
    static void Section(string name, Action fn)
    {
        Console.WriteLine("== " + name);
        try { fn(); } catch (Exception e) { fails++; Console.WriteLine("  FAILED: " + e); }
    }
    static GameState Fresh(string key = "liubei", int seed = 7) { UnityEngine.Random.InitState(seed); return GameState.Current = GameState.NewGame(key); }
    static void Give(GameState g, string[] keys, int f) { foreach (var k in keys) { var c = g.CityByKey(k); c.owner = f; g.AutoGovernor(c); } }
    static string Names(List<Route> routes) { return "[" + string.Join(",", routes.Select(r => r.city.name + r.hops)) + "]"; }
    static string Keys(IEnumerable<City> cs) { return "[" + string.Join(",", cs.Select(c => c.key)) + "]"; }
    static string S(string s) { return s ?? "null"; }

    // 参照实现：在 f 方子图上做 Floyd–Warshall，求所有点对最短路程
    static int[,] RefHops(GameState g, int f)
    {
        int n = g.cities.Count; const int INF = 1000000000;
        var d = new int[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) d[i, j] = i == j ? 0 : INF;
        foreach (var c in g.cities) { if (c.owner != f) continue; foreach (var j in c.links) if (g.cities[j].owner == f) d[c.id, j] = 1; }
        for (int k = 0; k < n; k++) for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) if (d[i, k] + d[k, j] < d[i, j]) d[i, j] = d[i, k] + d[k, j];
        return d;
    }

    static int Main()
    {
        BattleModel.specialsEnabled = true;   // 同网页版默认（游戏内暂时默认 false，见 BattleModel.specialsEnabled）
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Section("start: one city", () =>
        {
            var g = Fresh("liubei");
            var c = g.CityByKey("pingyuan");
            Ok(c.owner == g.player, "player owns 平原");
            Eq(g.RoutesFrom(c).Count.ToString(), "0", "no routes from the only city");
            Eq(S(Commands.MoveBlocked(c)), "只有一座城池——取得第二座城（出征攻取或「拉拢」敌将献城）后即可调动武将。", "move blocked reason (one city)");
            Eq(S(Commands.TransportBlocked(c)), "只有一座城池——取得第二座城（出征攻取或「拉拢」敌将献城）后即可输送金粮。", "transport blocked reason (one city)");
            // 董卓开局两城相邻：可以调动
            var g2 = Fresh("dong");
            var ly = g2.CityByKey("luoyang");
            Eq(Names(g2.RoutesFrom(ly)), "[长安1]", "dong: 洛阳 → 长安 adjacent");
            Eq(S(Commands.MoveBlocked(ly)), "null", "dong: move allowed");
            Eq(S(Commands.TransportBlocked(ly)), "null", "dong: transport allowed");
        });

        // 平原(我) — 北海(我) — 下邳(我) — 小沛(我)；濮阳为空城（不可穿行）；长沙(我) 与上述城池不相连。
        Section("constructed chain: reachability and distance", () =>
        {
            var g = Fresh("liubei");
            int P = g.player;
            Give(g, new[] { "beihai", "xiapi", "xiaopei", "changsha" }, P);
            var py = g.CityByKey("pingyuan");
            var routes = g.RoutesFrom(py);
            Eq(Names(routes), "[北海1,下邳2,小沛3]", "routes from 平原 (BFS order, hops)");
            Eq(string.Join(" ", routes.Select(r => string.Join(">", r.path.Select(i => g.cities[i].key)))), "pingyuan>beihai pingyuan>beihai>xiapi pingyuan>beihai>xiapi>xiaopei", "paths go through own cities only");
            Ok(!routes.Any(r => r.city.key == "changsha"), "disconnected own city 长沙 is not reachable");
            Ok(!routes.Any(r => r.city.key == "puyang"), "neutral 濮阳 is never a destination");
            Eq(Keys(Commands.MoveTargets(py).Select(r => r.city)), "[beihai,xiapi,xiaopei]", "Commands.MoveTargets");
            Eq(Keys(Commands.TransportTargets(py).Select(r => r.city)), "[beihai,xiapi,xiaopei]", "Commands.TransportTargets");
            Eq(g.RouteBetween(py, g.CityByKey("xiaopei")).Count.ToString(), "4", "RouteBetween length");
            Ok(g.RouteBetween(py, g.CityByKey("changsha")) == null, "RouteBetween unreachable");
            Ok(g.RouteBetween(py, py) == null, "RouteBetween same city");
            Eq(Names(g.RoutesFrom(py, P, 1)), "[北海1]", "maxHops 1");
            Eq(Names(g.RoutesFrom(py, P, 2)), "[北海1,下邳2]", "maxHops 2");
            Eq(Names(g.RoutesFrom(py, P, 0)), "[北海1,下邳2,小沛3]", "maxHops 0 = unlimited");
            Eq(Names(g.RoutesFrom(py, null, 2)), "[北海1,下邳2]", "maxHops with default faction");
            Eq(Keys(Commands.AiMoveTargets(py, P)), "[beihai,xiapi]", "Commands.AiMoveTargets: within 2 hops");
            Eq(Keys(Commands.AiMoveTargets(g.CityByKey("xiaopei"), P)), "[xiapi,beihai]", "AiMoveTargets from 小沛");
            Eq(g.RoutesFrom(g.CityByKey("puyang"), P).Count.ToString(), "0", "start city not owned → no routes");
            Eq(g.RoutesFrom(g.CityByKey("puyang")).Count.ToString(), "0", "neutral start → no routes");
            var cs = g.CityByKey("changsha");
            Eq(g.RoutesFrom(cs).Count.ToString(), "0", "长沙 isolated");
            Ok(S(Commands.MoveBlocked(cs)).Contains("隔着"), "isolated move reason: " + Commands.MoveBlocked(cs));
            Ok(S(Commands.TransportBlocked(cs)).Contains("隔着"), "isolated transport reason");
            // 若濮阳归我方，小沛的路程缩短为 2（经濮阳）
            Give(g, new[] { "puyang" }, P);
            Eq(g.RoutesFrom(py).First(r => r.city.key == "xiaopei").hops.ToString(), "2", "小沛 via own 濮阳 is 2 hops");
            // 同盟城不可穿行
            Give(g, new[] { "puyang" }, g.FactionByKey("cao").id);
            g.SetAlliance(P, g.FactionByKey("cao").id, g.MonthIndex + 12);
            Eq(g.RoutesFrom(py).First(r => r.city.key == "xiaopei").hops.ToString(), "3", "allied city is not traversable");
        });

        Section("move command", () =>
        {
            var g = Fresh("liubei");
            Give(g, new[] { "beihai", "xiapi", "changsha" }, g.player);
            City py = g.CityByKey("pingyuan"), xp = g.CityByKey("xiapi"), cs = g.CityByKey("changsha");
            Eq(S(Commands.MoveBlocked(py)), "null", "move allowed from 平原");
            var gy = g.generals.First(x => x.name == "关羽");
            var zf = g.generals.First(x => x.name == "张飞");
            var msg = Commands.Move(gy, xp);
            Ok(gy.city == xp.id && gy.moved, "moved 关羽 to non-adjacent 下邳: " + msg);
            Eq(xp.governor.ToString(), gy.id.ToString(), "关羽 became governor of empty 下邳");
            var before = g.ToJson();
            var msg2 = Commands.Move(zf, cs);
            Ok(g.ToJson() == before, "unreachable move leaves state unchanged");
            Ok(msg2.Contains("无法移驻"), "unreachable move message: " + msg2);
            Ok(zf.city == py.id && !zf.moved, "张飞 stays");
            Ok(Commands.Move(zf, py).Contains("无法移驻"), "move to own city rejected");
            foreach (var x in g.OfficersIn(py)) x.moved = true;
            Eq(S(Commands.MoveBlocked(py)), "本城武将本月都已行动，没有可调动的武将。", "all moved reason");
            var bh = g.CityByKey("beihai");
            Eq(g.OfficersIn(bh).Count().ToString(), "0", "北海 has no officers (given in setup)");
            Eq(S(Commands.MoveBlocked(bh)), "北海没有武将。", "no officers reason");
        });

        Section("transport command", () =>
        {
            var g = Fresh("liubei");
            Give(g, new[] { "beihai", "xiapi", "changsha" }, g.player);
            City py = g.CityByKey("pingyuan"), xp = g.CityByKey("xiapi"), cs = g.CityByKey("changsha");
            int g0 = py.gold, f0 = py.food, xg = xp.gold, xf = xp.food;
            var msg = Commands.Transport(py, xp, 100, 1000);
            Ok(py.gold == g0 - 100 && py.food == f0 - 1000 && xp.gold == xg + 100 && xp.food == xf + 1000, "transport to non-adjacent 下邳: " + msg);
            var before = g.ToJson();
            var msg2 = Commands.Transport(py, cs, 100, 1000);
            Ok(g.ToJson() == before, "unreachable transport leaves state unchanged");
            Ok(msg2.Contains("无法输送"), "unreachable transport message: " + msg2);
            py.gold = 0; py.food = 0;
            Eq(S(Commands.TransportBlocked(py)), "平原没有可输送的金粮。", "nothing to send reason");
        });

        Section("random ownership vs Floyd–Warshall", () =>
        {
            var rnd = new SeededRandom(2024);
            int compared = 0;
            for (int t = 0; t < 60; t++)
            {
                var g = Fresh("liubei", t + 1);
                int nf = g.factions.Count;
                foreach (var c in g.cities) c.owner = rnd.Next(-1, Math.Min(nf, 4));
                for (int f = 0; f < Math.Min(nf, 4); f++)
                {
                    var d = RefHops(g, f);
                    foreach (var c in g.cities)
                    {
                        if (c.owner != f) { Ok(g.RoutesFrom(c, f).Count == 0, "non-own start has no routes"); continue; }
                        var routes = g.RoutesFrom(c, f);
                        var want = g.cities.Where(x => x.id != c.id && x.owner == f && d[c.id, x.id] < 1000000000).Select(x => x.id).OrderBy(x => x);
                        Eq(string.Join(",", routes.Select(r => r.city.id).OrderBy(x => x)), string.Join(",", want), $"trial {t} faction {f} from {c.name}: reachable set");
                        int lastHops = 0;
                        foreach (var r in routes)
                        {
                            compared++;
                            if (r.hops != d[c.id, r.city.id]) Ok(false, $"hops {c.name}→{r.city.name} {r.hops} vs {d[c.id, r.city.id]}");
                            if (r.hops < lastHops) Ok(false, "routes not sorted by hops");
                            lastHops = r.hops;
                            if (r.path.Count != r.hops + 1 || r.path[0] != c.id || r.path[r.path.Count - 1] != r.city.id) Ok(false, "bad path ends " + string.Join(",", r.path));
                            for (int i = 1; i < r.path.Count; i++)
                            {
                                var a = g.cities[r.path[i - 1]]; var b = g.cities[r.path[i]];
                                if (a.links.IndexOf(b.id) < 0 || b.owner != f) { Ok(false, "path step not a link through own city"); break; }
                            }
                        }
                    }
                }
            }
            Console.WriteLine($"  compared {compared} routes");
            Ok(compared > 500, "enough routes compared");
        });

        Section("after first conquest the move command unlocks", () =>
        {
            var g = Fresh("liubei");
            var py = g.CityByKey("pingyuan");
            Ok(Commands.MoveBlocked(py) != null, "blocked before conquest");
            var target = g.CityByKey("puyang");
            var zf = g.generals.First(x => x.name == "张飞");
            var s = Conquest.Prepare(g.player, py, target, new List<General> { zf }, 500, 0);
            Conquest.AutoResolve(s);
            Conquest.Apply(s);
            Ok(s.attackerWon && target.owner == g.player, "took 濮阳");
            Eq(g.CityCount(g.player).ToString(), "2", "two cities now");
            Eq(S(Commands.MoveBlocked(py)), "null", "move unlocked from 平原 (unmoved generals remain)");
            Eq(S(Commands.MoveBlocked(target)), "本城武将本月都已行动，没有可调动的武将。", "濮阳: the attacker has already acted");
            Eq(Names(g.RoutesFrom(target)), "[平原1]", "route back from 濮阳");
        });

        Section("a defecting city also unlocks the move command", () =>
        {
            var g = Fresh("liubei");
            var py = g.CityByKey("pingyuan");
            var t = g.CityByKey("puyang");
            int cao = g.FactionByKey("cao").id;
            Ok(py.links.IndexOf(t.id) >= 0, "濮阳 is adjacent to 平原");
            t.owner = cao;
            var target = g.GeneralsOf(cao).First(x => !g.IsRuler(x));
            target.city = t.id;
            g.AutoGovernor(t);
            Eq(g.OfficersIn(t).Count().ToString(), "1", "濮阳 has a single officer");
            var agent = g.generals.First(x => x.name == "刘备");
            string msg;
            UnityEngine.Random.ValueOverride = () => 0;
            try { msg = Commands.Persuade(agent, target, g.player); } finally { UnityEngine.Random.ValueOverride = null; }
            Ok(msg.Contains("献城归降"), "persuade: " + msg);
            Eq(t.owner.ToString(), g.player.ToString(), t.name + " now ours");
            Eq(S(Commands.MoveBlocked(py)), "null", "move unlocked from 平原 after the defection");
        });

        // 电脑把后方武将调往路程 ≤ 2 的前线（含不相邻的城）；新规则下这些移动必然可达，Commands.Move 不会拒绝。
        // move.js 分两节：第一节跑网页版自带的 strategy-ai.js，第二节在独立上下文里确保第 6 步已接上 aiMoveTargets 再跑一遍；
        // 网页版已接线，两节跑的是同一批对局。C# 的 StrategyAI 已接线，故只跑一次，两节各做 move.js 的检查（检查数与 move.js 相同）。
        int aiMoves = 0, aiFar = 0, aiRejected = 0;
        Section("AI moves stay valid under the new rule", () =>
        {
            Commands.MoveObserver = (gen, from, to, accepted) =>
            {
                aiMoves++;
                if (from.links.IndexOf(to.id) < 0) aiFar++;
                if (!accepted) { aiRejected++; Console.WriteLine("  rejected: " + gen.name + " from " + from.name + " to " + to.name); }
            };
            try
            {
                for (int seed = 1; seed <= 3; seed++)
                {
                    UnityEngine.Random.InitState(seed);
                    var g = GameState.Current = GameState.NewGame("liubei");
                    g.player = -1;
                    for (int m = 0; m < 12 * 15; m++) { StrategyAI.RunAI(new List<string>()); StrategyAI.EndMonth(); }
                }
            }
            finally { Commands.MoveObserver = null; }
            Console.WriteLine($"  AI moves {aiMoves}, rejected {aiRejected}");
            Ok(aiMoves > 0, "AI moved generals");
            Eq(aiRejected.ToString(), "0", "no AI move rejected");
        });
        Section("optional AI wiring: 2-hop fronts", () =>
        {
            Console.WriteLine($"  AI moves {aiMoves} (non-adjacent {aiFar}), rejected {aiRejected}");
            Ok(aiMoves > 0 && aiFar > 0, "wired AI moves generals to non-adjacent cities");
            Eq(aiRejected.ToString(), "0", "no wired AI move rejected");
        });

        Console.WriteLine($"{checks} checks");
        Console.WriteLine(fails == 0 ? "ALL OK" : "FAILURES: " + fails);
        return fails;
    }
}
