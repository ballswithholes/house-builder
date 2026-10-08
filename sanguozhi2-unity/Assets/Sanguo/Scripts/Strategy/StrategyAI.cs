using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    // 月末结算与电脑势力的行动（对应网页版 js/strategy-ai.js 的 StrategyAI）
    public static class StrategyAI
    {
        static GameState G { get { return GameState.Current; } }
        // 收支公式按 double 计算，系数取十进制写法（与网页版逐位一致；float 会在 .5 处舍入不同）
        static double D(float f) { return (double)(decimal)f; }
        static readonly double GoldPerIndustry = D(Balance.GoldPerIndustry), GoldPerTown = D(Balance.GoldPerTown), FoodPerLand = D(Balance.FoodPerLand), PopGrowthPerTown = D(Balance.PopGrowthPerTown);
        static int RInt(double v) { return (int)System.Math.Round(v); }   // = Mathf.RoundToInt

        // ---------------------------------------------------------- 月末 --
        public static List<string> EndMonth()
        {
            var news = new List<string>();
            foreach (var c in G.cities)
            {
                if (c.owner < 0) { c.population += RInt(c.population * 0.001); continue; }
                var offs = G.OfficersIn(c).ToList();
                c.gold += RInt(c.industry * GoldPerIndustry + c.town * GoldPerTown);
                c.gold -= offs.Count * Balance.SalaryPerGeneral;
                int troops = offs.Sum(o => o.troops);
                c.food -= troops / Balance.TroopsPerFood;
                if (G.month == Balance.HarvestMonth)
                {
                    int harvest = RInt(c.land * FoodPerLand * Random.Range(0.85f, 1.15f));
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
                c.population = Mathf.Min(2000000, c.population + RInt((double)c.population * c.town * PopGrowthPerTown / 100));
            }
            // 低忠诚武将出奔
            foreach (var g in G.generals.Where(x => x.faction >= 0 && !x.dead).ToList())
            {
                // （本循环内某势力灭亡时，其余武将 faction 已为 -1，跳过以免越界）
                if (g.faction < 0) continue;
                if (G.IsRuler(g) || g.loyalty >= 40) continue;
                double p = (40 - g.loyalty) / 400.0 * (1.2 - G.factions[g.faction].virtue / 100.0);
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

        // 月末排队、待玩家应战的电脑进攻，在轮到它时重新核对（同月先结算的进攻可能已使城池易主、守军溃散或被俘）。
        // 返回 true：仍是对玩家的进攻（守军已按城中现有武将重建），交玩家应战。
        // 返回 false：已不再针对玩家——按电脑之间结算（兵力不足则撤兵），或攻方已无力出征而取消（退还军粮）。
        // 用法（战略画面）：foreach (var b in RunAI(news)) { if (!RecheckBattle(b, news)) continue; 玩家应战(b); }
        public static bool RecheckBattle(BattleSetup s, List<string> news)
        {
            int A = s.attacker; var t = s.target;
            System.Action refund = () => { if (s.src.owner == A) { s.src.food += s.atkFood; s.src.gold += s.atkGold; } };
            s.atk = s.atk.Where(a => !a.dead && a.faction == A && a.city == s.src.id && a.troops > 0).ToList();
            if (!G.factions[A].alive || s.src.owner != A || s.atk.Count == 0) { refund(); return false; }
            if (t.owner == A || (t.owner >= 0 && G.Allied(t.owner, A))) { refund(); return false; }
            s.defender = t.owner;
            s.def = Conquest.PickDefenders(t);
            s.routed = new HashSet<int>(); s.captives = new List<General>();
            if (t.owner == G.player) return true;
            // 城已落入他人之手：兵力仍占优（或城已空）才继续进攻，否则撤兵
            int sum = s.atk.Sum(a => a.troops);
            int defTroops = G.TroopsIn(t);
            bool empty = t.owner < 0 || defTroops == 0;
            if (!empty && sum / System.Math.Max(1.0, defTroops * (1 + t.Defense / 200.0)) < AttackNeed(A, t)) { refund(); return false; }
            ResolveAI(s, news);
            return false;
        }

        // 攻城所需兵力比（打城少者更积极）
        static double AttackNeed(int f, City t) { return t.owner >= 0 && G.CityCount(t.owner) < G.CityCount(f) ? 1.4 : 1.6; }

        // 电脑之间（或已不涉及玩家的）攻城：结算、处置俘虏、写入月末消息
        static void ResolveAI(BattleSetup s, List<string> news)
        {
            int f = s.attacker; var t = s.target;
            Conquest.AutoResolve(s);
            Conquest.Apply(s);
            if (s.captives.Count > 0) { if (s.attackerWon) Conquest.AiDecideCaptives(s, f); else if (s.defender >= 0) Conquest.AiDecideCaptives(s, s.defender); }
            if (s.attackerWon && (s.defender >= 0 || IsNear(t)))
                news.Add(s.defender >= 0 ? string.Format("{0}攻陷了{1}的{2}。", G.factions[f].name, G.factions[s.defender].name, t.name) : string.Format("{0}占领了{1}。", G.factions[f].name, t.name));
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
            var S = G.Scen;
            bool grace = G.MonthIndex < S.StartYear * 12 + S.StartMonth + Balance.PlayerGraceMonths;
            var targets = c.links.Select(i => G.cities[i]).Where(n => n.owner != f && !G.Allied(n.owner, f) && !(grace && n.owner == G.player)).ToList();
            foreach (var t in targets.OrderBy(t => G.TroopsIn(t)))
            {
                var avail = offs.Where(o => !o.moved && o.troops > 600).OrderByDescending(o => o.troops * (o.war + 50)).ToList();
                bool keepRuler = true;
                if (avail.Count == 0) break;
                int defTroops = G.TroopsIn(t);
                var squad = new List<General>();
                int sum = 0;
                // 世界剧本：城中只剩君主一人时君主不出城（许多小国只有君主一名武将，否则孤身君主会连夺空城、
                // 沿海路与草原链远走他乡）。经典剧本保持原样。
                bool loneRuler = G.scenario == "world" && offs.Count == 1;
                foreach (var o in avail)
                {
                    if (squad.Count >= Balance.MaxSortieGenerals) break;
                    if (keepRuler && G.IsRuler(o) && (avail.Count > 1 || loneRuler)) continue;
                    squad.Add(o); sum += o.troops;
                }
                // 至少留一人守城
                if (squad.Count == offs.Count && squad.Count > 1) { sum -= squad[squad.Count - 1].troops; squad.RemoveAt(squad.Count - 1); }
                double ratio = sum / System.Math.Max(1.0, defTroops * (1 + t.Defense / 200.0));
                double need = AttackNeed(f, t);
                bool empty = t.owner < 0 || defTroops == 0;
                if (squad.Count == 0 || (!empty && ratio < need)) continue;
                if (empty) squad = squad.Take(1).ToList();
                int food = Mathf.Min(c.food / 2, sum / 8 + 200);
                if (!empty && food < sum / 20) continue;
                var s = Conquest.Prepare(f, c, t, squad, food, 0);
                used++;
                if (t.owner == G.player && !empty) { playerBattles.Add(s); }
                else ResolveAI(s, news);
                return used;
            }
            // 2. 征兵
            var weak = offs.Where(o => !o.moved && o.troops < o.MaxTroops * 0.7).OrderBy(o => o.troops).FirstOrDefault();
            if (weak != null && c.gold > 220) { Commands.Recruit(c, weak, Commands.RecruitMax(c, weak)); used++; if (used >= 2) return used; }
            // 3. 搜索 / 登用
            var free = G.FreeFoundIn(c.id).FirstOrDefault();
            if (free != null) { Commands.Hire(free, gov, f, c.id); return used + 1; }
            if (Random.value < 0.15 && G.generals.Any(x => x.city == c.id && x.hidden && x.IsFree))
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
                // 路程 ≤ 2 的己方城（第二版：经由己方城池调动；电脑只看两步之内）
                var front = Commands.AiMoveTargets(c, f).OrderByDescending(n => Threat(n, f)).FirstOrDefault();
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
