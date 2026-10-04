using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    // 月末结算与电脑势力的行动
    public static class StrategyAI
    {
        static GameState G { get { return GameState.Current; } }

        // ---------------------------------------------------------- 月末 --
        public static List<string> EndMonth()
        {
            var news = new List<string>();
            foreach (var c in G.cities)
            {
                if (c.owner < 0) { c.population += Mathf.RoundToInt(c.population * 0.001f); continue; }
                var offs = G.OfficersIn(c).ToList();
                c.gold += Mathf.RoundToInt(c.industry * Balance.GoldPerIndustry + c.town * Balance.GoldPerTown);
                c.gold -= offs.Count * Balance.SalaryPerGeneral;
                int troops = offs.Sum(o => o.troops);
                c.food -= troops / Balance.TroopsPerFood;
                if (G.month == Balance.HarvestMonth)
                {
                    int harvest = Mathf.RoundToInt(c.land * Balance.FoodPerLand * Random.Range(0.85f, 1.15f));
                    c.food += harvest;
                    if (c.owner == G.player) news.Add(string.Format("{0}秋收，得粮 {1}。", c.name, harvest));
                }
                if (c.food < 0)
                {
                    c.food = 0;
                    foreach (var o in offs) o.troops = o.troops * 9 / 10;
                    if (c.owner == G.player) news.Add(c.name + "粮草断绝，士兵逃亡！");
                }
                if (c.gold < 0)
                {
                    c.gold = 0;
                    foreach (var o in offs) if (!G.IsRuler(o)) o.loyalty = Mathf.Max(0, o.loyalty - 3);
                }
                c.population = Mathf.Min(2000000, c.population + Mathf.RoundToInt(c.population * c.town * Balance.PopGrowthPerTown / 100f));
            }
            // 低忠诚武将出奔
            foreach (var g in G.generals.Where(x => x.faction >= 0 && !x.dead).ToList())
            {
                if (G.IsRuler(g) || g.loyalty >= 40) continue;
                float p = (40 - g.loyalty) / 400f * (1.2f - G.factions[g.faction].virtue / 100f);
                if (Random.value < p)
                {
                    var c = G.cities[g.city];
                    int f = g.faction;
                    g.faction = -1; g.troops = 0; g.loyalty = 0;
                    G.AutoGovernor(c);
                    if (f == G.player) news.Add(g.name + "不满待遇，弃官而去！");
                    G.Log(g.name + "离开了" + G.factions[f].name + "。");
                    if (!G.OfficersIn(c).Any()) { c.owner = -1; G.CheckFactionDeath(f); }
                }
            }
            foreach (var g in G.generals) g.moved = false;
            G.month++;
            if (G.month > 12) { G.month = 1; G.year++; }
            G.tokens = G.TokensFor(G.player);
            return news;
        }

        // ---------------------------------------------------------- 电脑行动 --
        // 返回电脑对玩家发起的进攻（由控制器交给玩家应战）
        public static List<BattleSetup> RunAI(List<string> news)
        {
            var playerBattles = new List<BattleSetup>();
            foreach (var f in G.factions.ToList())
            {
                if (!f.alive || f.id == G.player) continue;
                int tokens = G.TokensFor(f.id);
                var cities = G.CitiesOf(f.id).OrderBy(c => -Threat(c, f.id)).ToList();
                foreach (var c in cities)
                {
                    if (tokens <= 0 || c.owner != f.id) break;
                    var offs = G.OfficersIn(c).Where(o => !o.moved).ToList();
                    if (offs.Count == 0) continue;
                    tokens -= ActCity(f.id, c, offs, playerBattles, news);
                }
            }
            return playerBattles;
        }

        static float Threat(City c, int f)
        {
            float t = 0;
            foreach (var n in c.links.Select(i => G.cities[i]))
                if (n.owner >= 0 && n.owner != f && !G.Allied(n.owner, f)) t += G.TroopsIn(n);
            return t;
        }

        // 返回消耗的令牌数
        static int ActCity(int f, City c, List<General> offs, List<BattleSetup> playerBattles, List<string> news)
        {
            int used = 0;
            var gov = c.governor >= 0 ? G.generals[c.governor] : offs[0];
            // 1. 进攻
            bool grace = G.MonthIndex < ScenarioData.StartYear * 12 + ScenarioData.StartMonth + Balance.PlayerGraceMonths;
            var targets = c.links.Select(i => G.cities[i]).Where(n => n.owner != f && !G.Allied(n.owner, f) && !(grace && n.owner == G.player)).ToList();
            foreach (var t in targets.OrderBy(t => G.TroopsIn(t)))
            {
                var avail = offs.Where(o => !o.moved && o.troops > 600).OrderByDescending(o => o.troops * (o.war + 50)).ToList();
                bool keepRuler = true;
                if (avail.Count == 0) break;
                int defTroops = G.TroopsIn(t);
                var squad = new List<General>();
                int sum = 0;
                foreach (var o in avail)
                {
                    if (squad.Count >= Balance.MaxSortieGenerals) break;
                    if (keepRuler && G.IsRuler(o) && avail.Count > 1) continue;
                    squad.Add(o); sum += o.troops;
                }
                // 至少留一人守城
                if (squad.Count == offs.Count && squad.Count > 1) { sum -= squad[squad.Count - 1].troops; squad.RemoveAt(squad.Count - 1); }
                float ratio = sum / Mathf.Max(1f, defTroops * (1f + t.Defense / 200f));
                float need = t.owner >= 0 && G.CityCount(t.owner) < G.CityCount(f) ? 1.4f : 1.6f;
                bool empty = t.owner < 0 || defTroops == 0;
                if (squad.Count == 0 || (!empty && ratio < need)) continue;
                if (empty) squad = squad.Take(1).ToList();
                int food = Mathf.Min(c.food / 2, sum / 8 + 200);
                if (!empty && food < sum / 20) continue;
                var s = Conquest.Prepare(f, c, t, squad, food, 0);
                used++;
                if (t.owner == G.player && !empty) { playerBattles.Add(s); }
                else
                {
                    Conquest.AutoResolve(s);
                    Conquest.Apply(s);
                    if (s.captives.Count > 0) { if (s.attackerWon) Conquest.AiDecideCaptives(s, f); else if (s.defender >= 0) Conquest.AiDecideCaptives(s, s.defender); }
                    if (s.attackerWon && (s.defender >= 0 || IsNear(t)))
                        news.Add(s.defender >= 0 ? string.Format("{0}攻陷了{1}的{2}。", G.factions[f].name, G.factions[s.defender].name, t.name) : string.Format("{0}占领了{1}。", G.factions[f].name, t.name));
                }
                return used;
            }
            // 2. 征兵
            var weak = offs.Where(o => !o.moved && o.troops < o.MaxTroops * 0.7f).OrderBy(o => o.troops).FirstOrDefault();
            if (weak != null && c.gold > 220) { Commands.Recruit(c, weak, Commands.RecruitMax(c, weak)); used++; if (used >= 2) return used; }
            // 3. 搜索 / 登用
            var free = G.FreeFoundIn(c.id).FirstOrDefault();
            if (free != null) { Commands.Hire(free, gov, f, c.id); return used + 1; }
            if (Random.value < 0.15f && G.generals.Any(x => x.city == c.id && x.hidden && x.IsFree))
            {
                Commands.Search(c, gov);
                if (Commands.SearchFound != null) Commands.Hire(Commands.SearchFound, gov, f, c.id);
                return used + 1;
            }
            // 4. 开发
            if (c.gold > 160)
            {
                DevKind k = c.land <= c.industry && c.land <= c.town ? DevKind.Land : c.industry <= c.town ? DevKind.Industry : DevKind.Town;
                Commands.Develop(c, gov, k);
                used++;
            }
            // 5. 训练
            var lowTrain = offs.Where(o => o.troops > 0 && o.training < 70).OrderBy(o => o.training).FirstOrDefault();
            if (lowTrain != null && used < 2) { Commands.Train(lowTrain); used++; }
            // 6. 后方武将调往前线
            if (Threat(c, f) <= 0 && offs.Count > 1)
            {
                var front = c.links.Select(i => G.cities[i]).Where(n => n.owner == f).OrderByDescending(n => Threat(n, f)).FirstOrDefault();
                if (front != null && Threat(front, f) > 0)
                {
                    var mover = offs.Where(o => !G.IsRuler(o) && o.id != c.governor).OrderByDescending(o => o.troops).FirstOrDefault();
                    if (mover != null) { Commands.Move(mover, front); used++; }
                }
            }
            return Mathf.Max(1, used);
        }

        static bool IsNear(City t) { return t.links.Any(i => G.cities[i].owner == G.player); }
    }
}
