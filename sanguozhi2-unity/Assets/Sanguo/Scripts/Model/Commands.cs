using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    public enum DevKind { Land, Industry, Town }

    // 战略指令（玩家与电脑共用）
    public static class Commands
    {
        static GameState G { get { return GameState.Current; } }
        static int R(int a, int b) { return Random.Range(a, b + 1); }

        public static string DevName(DevKind k) { return k == DevKind.Land ? "土地" : k == DevKind.Industry ? "产业" : "町"; }

        // ---------------------------------------------------------- 开发 --
        public static string Develop(City c, General g, DevKind kind)
        {
            if (c.gold < Balance.DevelopCost) return "金不足，无法开发。";
            c.gold -= Balance.DevelopCost;
            int gain = Balance.DevelopBase + g.pol / Balance.DevelopPolDiv + R(0, 4);
            int before;
            switch (kind)
            {
                case DevKind.Land: before = c.land; c.land = Mathf.Min(Balance.StatMax, c.land + gain); gain = c.land - before; break;
                case DevKind.Industry: before = c.industry; c.industry = Mathf.Min(Balance.StatMax, c.industry + gain); gain = c.industry - before; break;
                default: before = c.town; c.town = Mathf.Min(Balance.StatMax, c.town + gain); gain = c.town - before; break;
            }
            return string.Format("{0}主持{1}开发，{1} +{2}。", g.name, DevName(kind), gain);
        }

        // ---------------------------------------------------------- 征兵 --
        public static int RecruitMax(City c, General g)
        {
            int room = g.MaxTroops - g.troops;
            int byGold = Mathf.Min(c.gold, Balance.RecruitGoldMax) * Balance.TroopsPerGold;
            int byPop = c.population / 10;
            return Mathf.Max(0, Mathf.Min(room, Mathf.Min(byGold, byPop)));
        }
        public static string Recruit(City c, General g, int troops)
        {
            troops = Mathf.Min(troops, RecruitMax(c, g));
            if (troops <= 0) return "无法征兵（金、人口或带兵上限不足）。";
            int cost = Mathf.CeilToInt(troops / (float)Balance.TroopsPerGold);
            c.gold -= cost; c.population -= troops;
            int total = g.troops + troops;
            g.training = (g.training * g.troops + 30 * troops) / Mathf.Max(1, total);
            g.troops = total;
            return string.Format("{0}征得新兵 {1} 人（花费 {2} 金）。", g.name, troops, cost);
        }

        // ---------------------------------------------------------- 训练 --
        public static string Train(General g)
        {
            if (g.troops <= 0) return g.name + "麾下无兵，无法训练。";
            int before = g.training;
            g.training = Mathf.Min(100, g.training + Balance.TrainBase + g.war / 10 + R(0, 4));
            return string.Format("{0}操练兵马，训练度 {1} → {2}。", g.name, before, g.training);
        }

        // ---------------------------------------------------------- 搜索 --
        public static General SearchFound;
        public static string Search(City c, General g)
        {
            SearchFound = null;
            var f = G.factions[c.owner];
            var hidden = G.generals.Where(x => x.city == c.id && x.hidden && x.IsFree).ToList();
            float p = 0.22f + g.intel / 260f + f.virtue / 420f;
            if (hidden.Count > 0 && Random.value < p)
            {
                var h = hidden[Random.Range(0, hidden.Count)];
                h.hidden = false;
                SearchFound = h;
                return string.Format("{0}四处寻访，发现了在野的人才——{1}！", g.name, h.name);
            }
            float r = Random.value;
            if (r < 0.25f) { int v = R(60, 220); c.gold += v; return string.Format("{0}在城中搜索，发现了 {1} 金。", g.name, v); }
            if (r < 0.45f) { int v = R(800, 2500); c.food += v; return string.Format("{0}在城中搜索，发现了 {1} 粮。", g.name, v); }
            return g.name + "四处寻访，一无所获。";
        }

        // ---------------------------------------------------------- 登用 --
        public static float HireChance(General target, General recruiter, int faction)
        {
            var f = G.factions[faction];
            float p = 0.32f + f.virtue / 220f + recruiter.intel / 450f - (target.war + target.intel + target.pol) / 900f;
            if (target.faction >= 0) p -= target.loyalty / 160f; // 俘虏
            return Mathf.Clamp(p, 0.05f, 0.95f);
        }
        public static bool Hire(General target, General recruiter, int faction, int city)
        {
            if (Random.value > HireChance(target, recruiter, faction)) return false;
            var f = G.factions[faction];
            target.faction = faction; target.city = city; target.hidden = false;
            target.loyalty = Mathf.Clamp(60 + f.virtue / 4 + R(-5, 10), 40, 100);
            target.training = 40;
            target.moved = true;
            return true;
        }

        // ---------------------------------------------------------- 赏赐 --
        public static string Reward(City c, General g)
        {
            if (c.gold < Balance.RewardGold) return "金不足。";
            c.gold -= Balance.RewardGold;
            int before = g.loyalty;
            g.loyalty = Mathf.Min(100, g.loyalty + Balance.RewardLoyalty + R(0, 4));
            return string.Format("赏赐{0} {1} 金，忠诚 {2} → {3}。", g.name, Balance.RewardGold, before, g.loyalty);
        }

        // ---------------------------------------------------------- 移动 / 输送 --
        public static string Move(General g, City to)
        {
            var from = G.cities[g.city];
            g.city = to.id; g.moved = true;
            G.AutoGovernor(from); G.AutoGovernor(to);
            return string.Format("{0}率兵 {1} 人移驻{2}。", g.name, g.troops, to.name);
        }
        public static string Transport(City from, City to, int gold, int food)
        {
            gold = Mathf.Clamp(gold, 0, from.gold); food = Mathf.Clamp(food, 0, from.food);
            from.gold -= gold; from.food -= food; to.gold += gold; to.food += food;
            return string.Format("自{0}向{1}输送金 {2}、粮 {3}。", from.name, to.name, gold, food);
        }

        // ---------------------------------------------------------- 交易 --
        // 每 100 粮的价格（金）。秋收后粮价低。
        public static int FoodPrice()
        {
            int m = G.month;
            int baseP = 12;
            if (m >= 7 && m <= 9) baseP = 8; else if (m >= 4 && m <= 6) baseP = 16;
            return baseP;
        }
        public static string BuyFood(City c, int gold)
        {
            gold = Mathf.Clamp(gold, 0, c.gold);
            int food = gold * 100 / FoodPrice();
            c.gold -= gold; c.food += food;
            return string.Format("以 {0} 金购入粮 {1}。", gold, food);
        }
        public static string SellFood(City c, int food)
        {
            food = Mathf.Clamp(food, 0, c.food);
            int gold = food * FoodPrice() / 100 * 9 / 10;
            c.food -= food; c.gold += gold;
            return string.Format("卖出粮 {0}，得金 {1}。", food, gold);
        }

        // ---------------------------------------------------------- 外交策略 --
        public static float AllyChance(int f, int target, int gift)
        {
            var a = G.factions[f]; var b = G.factions[target];
            float p = 0.25f + a.virtue / 300f + gift / 1200f + (a.fame - b.fame) / 300f;
            if (G.CityCount(f) > G.CityCount(target) * 2) p += 0.15f; // 弱者愿与强者结盟
            return Mathf.Clamp(p, 0.05f, 0.9f);
        }
        public static string Ally(City from, int f, int target, int gift)
        {
            gift = Mathf.Min(gift, from.gold);
            from.gold -= gift;
            if (Random.value < AllyChance(f, target, gift))
            {
                G.SetAlliance(f, target, G.MonthIndex + Balance.AllianceMonths);
                return string.Format("{0}同意结盟！同盟期限 {1} 个月。", G.factions[target].name, Balance.AllianceMonths);
            }
            return string.Format("{0}拒绝了结盟的提议。", G.factions[target].name);
        }

        public static string Discord(General agent, General target)
        {
            if (G.IsRuler(target)) return "离间君主是不可能的。";
            float p = Mathf.Clamp(0.4f + (agent.intel - target.intel) / 100f, 0.1f, 0.9f);
            if (Random.value > p) return string.Format("{0}识破了离间之计。", target.name);
            int d = R(6, 16) * agent.intel / Mathf.Max(30, target.intel);
            d = Mathf.Clamp(d, 3, 25);
            target.loyalty = Mathf.Max(0, target.loyalty - d);
            return string.Format("离间成功！{0}的忠诚下降了 {1}（现为 {2}）。", target.name, d, target.loyalty);
        }

        public static float PersuadeChance(General agent, General target, int f)
        {
            if (G.IsRuler(target)) return 0f;
            float p = (100 - target.loyalty) / 110f * (0.45f + agent.intel / 180f) * (0.55f + G.factions[f].virtue / 220f);
            return Mathf.Clamp(p, 0f, 0.9f);
        }
        public static string Persuade(General agent, General target, int f)
        {
            if (Random.value > PersuadeChance(agent, target, f)) return string.Format("{0}拒绝了劝诱。", target.name);
            var oldCity = G.cities[target.city];
            int oldF = target.faction;
            bool alone = G.OfficersIn(oldCity).Count() == 1;
            target.faction = f;
            target.loyalty = 65 + R(0, 15);
            if (alone)
            {
                oldCity.owner = f;
                G.AutoGovernor(oldCity);
                G.CheckFactionDeath(oldF);
                return string.Format("{0}献城归降！{1}归入我方。", target.name, oldCity.name);
            }
            target.city = agent.city; target.troops /= 2; target.moved = true;
            G.AutoGovernor(oldCity); G.AutoGovernor(G.cities[agent.city]);
            return string.Format("{0}率部来投！", target.name);
        }
    }

    public static class GameStateExt
    {
        // 势力灭亡检查
        public static void CheckFactionDeath(this GameState g, int f)
        {
            if (f < 0 || !g.factions[f].alive) return;
            if (g.CityCount(f) > 0) return;
            g.factions[f].alive = false;
            foreach (var gen in g.GeneralsOf(f).ToList()) { gen.faction = -1; gen.troops = 0; gen.loyalty = 0; }
            g.Log(g.factions[f].name + "势力灭亡了。");
        }
    }
}
