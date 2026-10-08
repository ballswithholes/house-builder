using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    // 一场攻城战的参与者与结果
    public class BattleSetup
    {
        public int attacker, defender;   // 势力（defender 可为 -1 空城）
        public City src, target;
        public List<General> atk = new List<General>(), def = new List<General>();
        public int atkFood, atkGold;
        public bool attackerWon;
        public HashSet<int> routed = new HashSet<int>(); // 兵力耗尽或单挑落败的武将
        public List<General> captives = new List<General>();
        public string summary;
    }

    // 俘虏的下落：fate = "hired"（降服）| "released"（获释）| "executed"（处斩）；from = 原势力
    public class CaptiveFate
    {
        public const string Hired = "hired", Released = "released", Executed = "executed";
        public General gen;
        public int from;
        public string fate;
    }

    // 攻城结算（对应网页版 js/strategy-ai.js 的 Conquest）
    public static class Conquest
    {
        static GameState G { get { return GameState.Current; } }

        public static BattleSetup Prepare(int attacker, City src, City target, List<General> atk, int food, int gold)
        {
            var s = new BattleSetup { attacker = attacker, defender = target.owner, src = src, target = target, atkFood = food, atkGold = gold };
            s.atk.AddRange(atk);
            src.food -= food; src.gold -= gold;
            foreach (var g in atk) g.moved = true;
            s.def = PickDefenders(target);
            return s;
        }

        // 守城出战的武将：君主优先，其余按兵力，至多 MaxSortieGenerals 名
        public static List<General> PickDefenders(City target)
        {
            if (target.owner < 0) return new List<General>();
            return G.OfficersIn(target).OrderByDescending(x => G.IsRuler(x) ? 9999 : x.troops).Take(Balance.MaxSortieGenerals).ToList();
        }

        // 电脑之间（或委任）快速结算
        public static void AutoResolve(BattleSetup s)
        {
            if (s.def.Count == 0 || s.def.All(d => d.troops <= 0)) { s.attackerWon = true; s.summary = s.target.name + "无人防守，不战而下。"; return; }
            double pa = s.atk.Sum(Power), pd = s.def.Sum(Power) * (1 + s.target.Defense / 160.0);
            for (int round = 0; round < 12; round++)
            {
                double ra = pa / System.Math.Max(1.0, pa + pd);
                foreach (var d in s.def.Where(x => x.troops > 0)) d.troops = Mathf.Max(0, d.troops - (int)System.Math.Round(d.troops * 0.16 * ra * 2 * Random.Range(0.7f, 1.3f)));
                foreach (var a in s.atk.Where(x => x.troops > 0)) a.troops = Mathf.Max(0, a.troops - (int)System.Math.Round(a.troops * 0.16 * (1 - ra) * 2 * Random.Range(0.7f, 1.3f)));
                foreach (var x in s.atk.Concat(s.def)) if (x.troops < 80) { x.troops = 0; s.routed.Add(x.id); }
                pa = s.atk.Sum(Power); pd = s.def.Sum(Power) * (1 + s.target.Defense / 160.0);
                if (pa <= 1 || pd <= 1) break;
            }
            s.attackerWon = pd < pa * 0.6 || s.def.All(d => d.troops <= 0);
            s.summary = string.Format("{0}军{1}{2}！", G.factions[s.attacker].name, s.attackerWon ? "攻陷了" : "未能攻下", s.target.name);
        }

        // 按 double 计算（与网页版一致）
        static double Power(General g) { return g.troops * (0.55 + g.war / 220.0) * (0.7 + g.training / 330.0); }

        // 结算归属、撤退与俘虏
        public static void Apply(BattleSetup s)
        {
            // 防御：守方以城池当前归属为准（排队的进攻若遇城池易主，守军撤退须撤往真正守方的城）
            if (s.target.owner != s.defender) s.defender = s.target.owner;
            var A = s.attacker; var D = s.defender;
            pendingCaptives.Clear();
            if (s.attackerWon)
            {
                // 守军撤退或被俘
                var retreatTo = D >= 0 ? s.target.links.Select(i => G.cities[i]).FirstOrDefault(c => c.owner == D) : null;
                foreach (var d in G.OfficersIn(s.target).ToList())
                {
                    bool routed = s.routed.Contains(d.id) || d.troops <= 0;
                    if (retreatTo != null && (!routed || Random.value < 0.5)) { d.city = retreatTo.id; }
                    else if (routed || retreatTo == null) { if (Random.value < 0.75 || retreatTo == null) Capture(s, d); else d.city = retreatTo.id; }
                }
                s.target.owner = A;
                s.target.gold = s.target.gold / 2 + s.atkGold;
                s.target.food = s.target.food / 2 + s.atkFood;
                foreach (var a in s.atk) if (!s.routed.Contains(a.id) || a.troops > 0) a.city = s.target.id;
                foreach (var a in s.atk.Where(x => s.routed.Contains(x.id) && x.troops <= 0)) a.city = s.src.id;
                s.target.governor = -1;
                G.AutoGovernor(s.target);
                if (retreatTo != null) G.AutoGovernor(retreatTo);
                G.AutoGovernor(s.src);
                G.Log(string.Format("{0}攻陷{1}。", G.factions[A].name, s.target.name));
                if (D >= 0) { HandleRulerLoss(D); G.CheckFactionDeath(D); }
            }
            else
            {
                foreach (var a in s.atk)
                {
                    if (s.routed.Contains(a.id) && Random.value < 0.35 && D >= 0) Capture(s, a);
                    else a.city = s.src.id;
                }
                s.src.food += s.atkFood; s.src.gold += s.atkGold;
                G.AutoGovernor(s.src);
                if (D >= 0) G.Log(string.Format("{0}击退了{1}的进攻。", G.factions[D].name, G.factions[A].name));
                HandleRulerLoss(A);
            }
        }

        static void Capture(BattleSetup s, General g)
        {
            g.troops = 0;
            s.captives.Add(g);
            g.city = s.target.id;
            pendingCaptives.Add(g.id);
        }

        // 君主被俘或身亡后的继承
        public static void HandleRulerLoss(int f)
        {
            if (f < 0 || !G.factions[f].alive) return;
            var r = G.Ruler(f);
            bool lost = r.dead || r.faction != f || G.cities[r.city].owner != f;
            if (!lost) return;
            if (!r.dead && r.faction == f)
            {
                // 君主仍在，但所在城已失：转移到己方城池（世界剧本取路程最近的一座，免得君主一步跳到另一片大陆；经典剧本保持原样）
                var c = G.scenario == "world" ? NearestOwnCity(f, r.city) : G.CitiesOf(f).FirstOrDefault();
                if (c != null && !IsCaptive(r)) { r.city = c.id; G.AutoGovernor(c); return; }
            }
        }
        // 按连线路程（BFS）离 fromId 最近的势力 f 城池；连线上找不到时取 f 的第一座城，无城为 null
        public static City NearestOwnCity(int f, int fromId)
        {
            var seen = new HashSet<int> { fromId };
            var q = new List<int> { fromId };
            while (q.Count > 0)
            {
                var next = new List<int>();
                foreach (var id in q)
                    foreach (var j in G.cities[id].links)
                    {
                        if (!seen.Add(j)) continue;
                        if (G.cities[j].owner == f) return G.cities[j];
                        next.Add(j);
                    }
                q = next;
            }
            return G.CitiesOf(f).FirstOrDefault();
        }

        public static bool IsCaptive(General g) { return pendingCaptives.Contains(g.id); }
        // 本场战斗中被俘、尚待处置的武将：Capture 时登记，释放 / 处斩 / 处置完毕时移除，每场结算开始时清空
        public static HashSet<int> pendingCaptives = new HashSet<int>();

        public static void Succession(int f)
        {
            if (f < 0 || !G.factions[f].alive) return;
            var heir = G.GeneralsOf(f).Where(x => G.cities[x.city].owner == f).OrderByDescending(x => x.war + x.intel + x.pol).FirstOrDefault();
            if (heir == null)
            {
                foreach (var c in G.CitiesOf(f).ToList()) { c.owner = -1; c.governor = -1; }
                G.CheckFactionDeath(f);
                return;
            }
            G.factions[f].ruler = heir.id;
            heir.loyalty = 100;
            G.Log(heir.name + "继承了" + G.factions[f].name + "的基业。");
        }

        // 俘虏处置（AI）：先试图登用，否则释放为在野。
        // 返回每名俘虏的下落（供画面告知玩家其被俘武将的去向）
        public static List<CaptiveFate> AiDecideCaptives(BattleSetup s, int winner)
        {
            var recruiter = G.Ruler(winner);
            var outList = new List<CaptiveFate>();
            foreach (var c in s.captives)
            {
                int oldF = c.faction;
                bool wasRuler = oldF >= 0 && G.factions[oldF].ruler == c.id;
                if (wasRuler)
                {
                    if (Random.value < 0.5) { Execute(c); outList.Add(new CaptiveFate { gen = c, from = oldF, fate = CaptiveFate.Executed }); }
                    else { Release(c); outList.Add(new CaptiveFate { gen = c, from = oldF, fate = CaptiveFate.Released }); }
                    continue;
                }
                if (Commands.Hire(c, recruiter, winner, s.target.id)) outList.Add(new CaptiveFate { gen = c, from = oldF, fate = CaptiveFate.Hired });
                else { Release(c); outList.Add(new CaptiveFate { gen = c, from = oldF, fate = CaptiveFate.Released }); }
            }
            foreach (var c in s.captives) pendingCaptives.Remove(c.id);
            return outList;
        }

        public static void Release(General c)
        {
            pendingCaptives.Remove(c.id);
            int oldF = c.faction;
            bool wasRuler = oldF >= 0 && G.factions[oldF].ruler == c.id;
            var home = oldF >= 0 ? G.CitiesOf(oldF).FirstOrDefault() : null;
            if (home != null) { c.city = home.id; G.AutoGovernor(home); }
            else { c.faction = -1; c.loyalty = 0; }
            if (wasRuler && home == null) G.CheckFactionDeath(oldF);
        }

        public static void Execute(General c)
        {
            pendingCaptives.Remove(c.id);
            int oldF = c.faction;
            bool wasRuler = oldF >= 0 && G.factions[oldF].ruler == c.id;
            c.dead = true; c.troops = 0; c.faction = -1;
            G.Log(c.name + "被处斩。");
            if (wasRuler) Succession(oldF);
            // 处斩者不得仍挂太守之职；新君主坐镇其所在之城
            foreach (var city in G.cities) if (city.governor == c.id) G.AutoGovernor(city);
            if (wasRuler && G.factions[oldF].alive) G.AutoGovernor(G.cities[G.Ruler(oldF).city]);
        }
    }
}
