// 三国志II 霸王的大陆 · 规则定义
// 所有可调的数值都集中在这里（Balance），以便与原作比对后修正。
using System.Collections.Generic;

namespace Sanguo
{
    public static class Balance
    {
        // ---- 令牌 ----
        public const int TokensBase = 3;          // 只有一座城时每月 3 枚令牌
        public const int CitiesPerExtraToken = 2; // 每多 2 座城 +1 枚
        public const int TokensMax = 10;

        // ---- 内政 ----
        public const int DevelopCost = 50;        // 开发一次所需金
        public const int DevelopBase = 6;         // 基础增量
        public const int DevelopPolDiv = 6;       // 政治 / 6 的额外增量
        public const int StatMax = 999;           // 土地 / 产业 / 町 上限
        public const int TrainBase = 8;
        public const int TroopsPerGold = 5;       // 征兵：每 1 金得兵 5 人
        public const int RecruitGoldMax = 500;    // 单次征兵最多花费
        public const int GeneralTroopBase = 1000; // 武将带兵上限 = Base + 武力 × PerWar
        public const int GeneralTroopPerWar = 40;
        public const int RewardGold = 100;        // 赏赐金额
        public const int RewardLoyalty = 8;

        // ---- 月度收支 ----
        public const float GoldPerIndustry = 0.12f;  // 每月金收入 = 产业 × 系数 + 町 × 系数
        public const float GoldPerTown = 0.05f;
        public const float FoodPerLand = 2.2f;       // 秋收（7 月）粮收入 = 土地 × 系数
        public const int HarvestMonth = 7;
        public const int TroopsPerFood = 40;         // 每月每 40 兵消耗 1 粮
        public const int SalaryPerGeneral = 2;
        public const float PopGrowthPerTown = 0.0006f;

        // ---- 战斗 ----
        public const int BattleDays = 30;
        public const int BattleW = 14, BattleH = 10;
        public const int MaxSortieGenerals = 5;
        public const float DamageK = 0.11f;
        public const float CounterRatio = 0.5f;
        public const int BattleFoodPerTroops = 250;  // 战场上每 250 兵每日耗 1 粮
        public const int ActionPointsBase = 2;       // 每日行动力 = Base + 人望 / Div
        public const int ActionPointsFameDiv = 20;
        public const int ActionPointsMax = 7;

        // ---- 外交 ----
        public const int AllianceMonths = 12;
        public const int AllianceGift = 200;
        public const int PlayerGraceMonths = 3;      // 开局若干个月内电脑不会进攻玩家
    }

    public enum Terrain { Plain, Forest, Hill, Mountain, River, Wall, Gate, Castle }

    public class FormationDef
    {
        public string Name, Desc;
        public float Atk, Def;
        public int Move;
        public int ReqInt, ReqWar;
        public FormationDef(string n, float a, float d, int m, int ri, int rw, string desc) { Name = n; Atk = a; Def = d; Move = m; ReqInt = ri; ReqWar = rw; Desc = desc; }
    }

    public enum TacticKind { Fire, Rockfall, Confuse, Inspire }

    public class TacticDef
    {
        public TacticKind Kind; public string Name, Desc; public int ReqInt; public float Power;
        public TacticDef(TacticKind k, string n, int req, float pow, string desc) { Kind = k; Name = n; ReqInt = req; Power = pow; Desc = desc; }
    }

    // 确定性的种子随机数（网页版 SG.SeededRandom 的 C# 版，mulberry32）。用于战场地形生成与必杀技兜底生成器，
    // 使 Unity 版与网页版在同一种子下得到完全相同的战场与招式。
    //   NextDouble() ∈ [0, 1)；Next(max) / Next(min, max) 不含 max；Next() ∈ [0, int.MaxValue)
    public sealed class SeededRandom
    {
        uint a;
        public SeededRandom(int seed) { a = unchecked((uint)seed ^ 0x9e3779b9u); }
        public SeededRandom(uint seed) { a = seed ^ 0x9e3779b9u; }
        public double NextDouble()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
        public int Next() { return (int)System.Math.Floor(NextDouble() * int.MaxValue); }
        public int Next(int max) { return (int)System.Math.Floor(NextDouble() * max); }
        public int Next(int min, int max) { return min + (int)System.Math.Floor(NextDouble() * (max - min)); }
    }

    public static class Defs
    {
        // 阵型：攻击倍率、防御倍率、机动力加成、所需智力 / 武力
        public static readonly FormationDef[] Formations =
        {
            new FormationDef("方圆", 0.85f, 1.35f, 0,  0,  0,  "坚守之阵，防御大增，攻击略减"),
            new FormationDef("长蛇", 0.95f, 0.90f, 2,  0,  0,  "行军之阵，机动力 +2"),
            new FormationDef("鱼鳞", 1.15f, 1.00f, 0, 40,  0,  "中央突破，攻击提升"),
            new FormationDef("鹤翼", 1.10f, 1.10f, 0, 60,  0,  "两翼包抄，攻守兼备"),
            new FormationDef("偃月", 1.25f, 0.90f, 0,  0, 75,  "主将当先，攻击大增"),
            new FormationDef("锋矢", 1.40f, 0.75f, 1,  0, 85,  "全军突击，攻击极大，防御薄弱"),
            new FormationDef("雁行", 1.05f, 1.05f, 1, 70,  0,  "雁阵展开，机动 +1"),
            new FormationDef("衡轭", 1.00f, 1.25f, 0, 80,  0,  "首尾相应，防御提升"),
        };

        public static readonly TacticDef[] Tactics =
        {
            new TacticDef(TacticKind.Fire,     "火计", 50, 1.0f, "放火烧敌，林地中威力倍增"),
            new TacticDef(TacticKind.Rockfall, "落石", 60, 1.2f, "自山丘落石，须立于山地"),
            new TacticDef(TacticKind.Confuse,  "混乱", 70, 0f,   "扰乱敌军，使其无法行动"),
            new TacticDef(TacticKind.Inspire,  "激励", 40, 0f,   "鼓舞己方士气"),
        };

        public static float TerrainDef(Terrain t)
        {
            switch (t)
            {
                case Terrain.Forest: return 1.15f;
                case Terrain.Hill: return 1.25f;
                case Terrain.River: return 0.8f;
                case Terrain.Gate: return 1.6f;
                case Terrain.Castle: return 1.8f;
                default: return 1f;
            }
        }
        public static int TerrainCost(Terrain t)
        {
            switch (t)
            {
                case Terrain.Plain: case Terrain.Castle: case Terrain.Gate: return 1;
                case Terrain.Forest: case Terrain.Hill: return 2;
                case Terrain.River: return 3;
                default: return 99; // 山与城墙不可通行
            }
        }
        public static string TerrainName(Terrain t)
        {
            switch (t)
            {
                case Terrain.Plain: return "平原"; case Terrain.Forest: return "森林"; case Terrain.Hill: return "山丘";
                case Terrain.Mountain: return "山岳"; case Terrain.River: return "河川"; case Terrain.Wall: return "城墙";
                case Terrain.Gate: return "城门"; default: return "本城";
            }
        }
    }
}
