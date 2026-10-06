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

    public static class Conquest
    {
        static GameState G { get { return GameState.Current; } }

        public static BattleSetup Prepare(int attacker, City src, City target, List<General> atk, int food, int gold)
        {
            var s = new BattleSetup { attacker = attacker, defender = target.owner, src = src, target = target, atkFood = food, atkGold = gold };
            s.atk.AddRange(atk);
            src.food -= food; src.gold -= gold;
            foreach (var g in atk) g.moved = true;
            if (target.owner >= 0) s.def.AddRange(G.OfficersIn(target).OrderByDescending(x => G.IsRuler(x) ? 9999 : x.troops).Take(Balance.MaxSortieGenerals));
            return s;
        }

        // 电脑之间（或委任）快速结算
        public static void AutoResolve(BattleSetup s)
        {
            if (s.def.Count == 0 || s.def.All(d => d.troops <= 0)) { s.attackerWon = true; s.summary = s.target.name + "无人防守，不战而下。"; return; }
            float pa = s.atk.Sum(Power), pd = s.def.Sum(Power) * (1f + s.target.Defense / 160f);
            for (int round = 0; round < 12; round++)
            {
                float ra = pa / Mathf.Max(1f, pa + pd);
                foreach (var d in s.def.Where(x => x.troops > 0)) d.troops = Mathf.Max(0, d.troops - Mathf.RoundToInt(d.troops * 0.16f * ra * 2f * Random.Range(0.7f, 1.3f)));
                foreach (var a in s.atk.Where(x => x.troops > 0)) a.troops = Mathf.Max(0, a.troops - Mathf.RoundToInt(a.troops * 0.16f * (1 - ra) * 2f * Random.Range(0.7f, 1.3f)));
                foreach (var x in s.atk.Concat(s.def)) if (x.troops < 80) { x.troops = 0; s.routed.Add(x.id); }
                pa = s.atk.Sum(Power); pd = s.def.Sum(Power) * (1f + s.target.Defense / 160f);
                if (pa <= 1 || pd <= 1) break;
            }
            s.attackerWon = pd < pa * 0.6f || s.def.All(d => d.troops <= 0);
            s.summary = string.Format("{0}军{1}{2}！", G.factions[s.attacker].name, s.attackerWon ? "攻陷了" : "未能攻下", s.target.name);
        }

        static float Power(General g) { return g.troops * (0.55f + g.war / 220f) * (0.7f + g.training / 330f); }

        // 结算归属、撤退与俘虏
        public static void Apply(BattleSetup s)
        {
            var A = s.attacker; var D = s.defender;
            pendingCaptives.Clear();
            if (s.attackerWon)
            {
                // 守军撤退或被俘
                var retreatTo = D >= 0 ? s.target.links.Select(i => G.cities[i]).FirstOrDefault(c => c.owner == D) : null;
                foreach (var d in G.OfficersIn(s.target).ToList())
                {
                    bool routed = s.routed.Contains(d.id) || d.troops <= 0;
                    if (retreatTo != null && (!routed || Random.value < 0.5f)) { d.city = retreatTo.id; }
                    else if (routed || retreatTo == null) { if (Random.value < 0.75f || retreatTo == null) Capture(s, d); else d.city = retreatTo.id; }
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
                    if (s.routed.Contains(a.id) && Random.value < 0.35f && D >= 0) Capture(s, a);
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
                // 君主仍在，但所在城已失：转移到己方城池
                var c = G.CitiesOf(f).FirstOrDefault();
                if (c != null && !IsCaptive(r)) { r.city = c.id; G.AutoGovernor(c); return; }
            }
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

        // 俘虏处置（AI）：先试图登用，否则释放为在野
        public static void AiDecideCaptives(BattleSetup s, int winner)
        {
            var recruiter = G.Ruler(winner);
            foreach (var c in s.captives)
            {
                int oldF = c.faction;
                bool wasRuler = oldF >= 0 && G.factions[oldF].ruler == c.id;
                if (wasRuler)
                {
                    if (Random.value < 0.5f) Execute(c); else Release(c);
                    continue;
                }
                if (!Commands.Hire(c, recruiter, winner, s.target.id)) Release(c);
            }
            foreach (var c in s.captives) pendingCaptives.Remove(c.id);
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
