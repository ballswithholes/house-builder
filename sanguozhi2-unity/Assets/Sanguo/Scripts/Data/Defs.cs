// 三国志-霸王的世界 · 规则定义（对应网页版 js/data.js）
// 所有可调的数值都集中在这里（Balance），以便与原作比对后修正。
// 第三版：战场 18 × 13（DESIGN-V3 §2，原 14 × 10）；新增地块 Terrain.Water（= 8，湖海深水，不可通行，规则上等同山岳）；
//   计策扩展为 18 种（TacticDef 增加 js/tactics-data.js 的规则字段，目录见 Battle/Tactics.cs）；
//   文化代号与回退表 Defs.Cultures / Defs.CultureBase（= 网页版 SG.CULTURES / SG.cultureBase，core.js）。
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
        public const int BattleW = 18, BattleH = 13;  // 第三版 §2：战场 +25%（原 14 × 10）
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

    // Water（= 8，第三版）：湖、海等深水，不可通行（TerrainCost 99，规则上等同山岳），外观为水面；见 BattleTerrain
    public enum Terrain { Plain, Forest, Hill, Mountain, River, Wall, Gate, Castle, Water }

    public class FormationDef
    {
        public string Name, Desc;
        public float Atk, Def;
        public int Move;
        public int ReqInt, ReqWar;
        public FormationDef(string n, float a, float d, int m, int ri, int rw, string desc) { Name = n; Atk = a; Def = d; Move = m; ReqInt = ri; ReqWar = rw; Desc = desc; }
    }

    // Learned：第三版的可习得计策（网页版 kind 为字符串 = 编号 Id；规则见 BattleModel 的 NewTactic* 方法）
    public enum TacticKind { Fire, Rockfall, Confuse, Inspire, Learned }

    // 一种计策。原四计（Defs.Tactics，Kind 0..3）的规则与数值同第一版；第三版另带 js/tactics-data.js 的目录字段
    // （编号、等阶、目标、射程、特效、主色）。14 种可习得计策（Kind = Learned）由 Tactics.cs 按 TacticsData.g.cs 生成，
    // 规则字段的含义见 tactics-data.js 文件头（未设的数值为 0，未设的子表为 null）。
    public class TacticDef
    {
        public TacticKind Kind; public string Name, Desc; public int ReqInt; public float Power;
        // ---- 第三版目录字段 ----
        public string Id;                     // fire / rockfall / confuse / inspire / rumor / feint / …
        public int Tier = 1;                  // 等阶 1–4（Tactics.TierName）
        public string Target = "enemy";       // enemy / ally / self
        public int Range, Radius, Cd;         // 射程、波及半径、冷却日数（0 = 每次行动都可用）
        public string Need;                   // null / river（目标临河）/ cover（目标靠近林地 / 山丘）/ hill-self（施计者立于山丘，落石）
        public string Fx, Color, Lore;        // 特效风格（BattleView.SpecialFx）、主色、典故
        public bool Innate = true;            // 原四计：按智力自带，不占格
        // ---- 可习得计策的规则字段 ----
        public double Morale, MoraleI;        // 士气变化 = Morale + MoraleI × 智力（敌为负、友为正）
        public double Heal, HealI;            // 回复 (Heal + HealI × 智力) × 最大兵力
        public int Food;                      // 每回复 Food 兵耗粮 1（0 = 不耗粮）
        public bool Cure, Douse;              // 解除混乱、灭火
        public TacticRoll Roll;               // 整体成功率（null = 必定成功）
        public TacticDmg Dmg;                 // 计策伤害（null = 无伤害）
        public TacticConfuse Confuse;         // 受波及敌军另判混乱
        public TacticConfuse FoeConfuse;      // 自身为中心：周围敌军另判混乱
        public TacticMod Mod;                 // 限时加成 / 削弱
        // 第三版新增的计策（网页版 typeof kind === 'string'）
        public bool IsNew { get { return Kind == TacticKind.Learned; } }

        public TacticDef(TacticKind k, string n, int req, float pow, string desc) { Kind = k; Name = n; ReqInt = req; Power = pow; Desc = desc; }
        // 可习得计策（Tactics.cs 用；Desc 由 Tactics.Explain 补上）
        public TacticDef(TacticEntry e)
        {
            Kind = TacticKind.Learned; Name = e.Name; ReqInt = e.ReqInt; Power = 0; Innate = false;
            Apply(e);
            Morale = e.Morale; MoraleI = e.MoraleI; Heal = e.Heal; HealI = e.HealI; Food = e.Food; Cure = e.Cure != 0; Douse = e.Douse != 0;
            Roll = e.Roll; Dmg = e.Dmg; Confuse = e.Confuse; FoeConfuse = e.FoeConfuse; Mod = e.Mod;
        }
        // 目录字段（原四计的名称 / 需求智力 / 威力 / 说明保持 Defs 的值）
        internal void Apply(TacticEntry e)
        {
            Id = e.Id; Tier = e.Tier; Target = e.Target; Range = e.Range; Radius = e.Radius; Cd = e.Cd; Need = e.Need;
            Fx = e.Fx; Color = e.Color; Lore = e.Lore;
        }
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
        // 原四计补上第三版的目录字段（TacticsData.Original 与 Defs.Tactics 同序）
        static Defs()
        {
            foreach (var t in Tactics)
                foreach (var e in TacticsData.Original)
                    if (e.Kind == (int)t.Kind) { t.Apply(e); t.Innate = true; }
        }

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
                case Terrain.Gate: return "城门"; case Terrain.Water: return "水域"; default: return "本城";
            }
        }

        // ------------------------------------------------------------- 文化代号 --
        // DESIGN-V2 §6 的 15 种基础文化 + 第三版 india（天竺诸国）、africa（阿克苏姆、库施）、moor（毛里人 / 巴夸特）。
        // 某个模块没有某文化的专门外观时，用 CultureBase(code) 取它的回退文化（唯一的回退表，= 网页版 SG.cultureBase）：
        //   india → seasia，africa → arab，moor → arab；基础文化返回自身；未知 / 空 → han。
        // 已有专门外观的模块应先查 code 本身，查不到再查 CultureBase(code)。表由导出工具从 core.js 生成（WorldScenarioData）。
        public static string[] Cultures { get { return WorldScenarioData.CultureCodes; } }
        public static string CultureBase(string code)
        {
            if (string.IsNullOrEmpty(code)) return "han";
            int i = System.Array.IndexOf(WorldScenarioData.CultureCodes, code);
            return i >= 0 ? WorldScenarioData.CultureBases[i] : "han";
        }
    }
}
