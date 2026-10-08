// ==========================================================================
// 三国志II 霸王的大陆 · 单挑格斗 · 逻辑核心（第二版 DESIGN-V2 §4E；网页版 js/duel-game.js 的移植）
//
// 纯 C#（不引用 UnityEngine），可在 Tests~/duel 的无头测试中运行：
//   DuelSim   固定 60Hz 步长的格斗逻辑（招式、判定、伤害、格挡 / 格开、怒气、绝技与弓将的连珠箭、60 秒计时）
//   DuelAI    电脑（难度由武力决定；武力 40 以上会看破一味连按的对手）
//   DuelLooks 外观与兵器设定（知名武将 > 文化缺省 > 能力值 + 姓名哈希）
//
// 数值一律用 double，运算顺序与 JS 逐式相同；规则相关的随机数只走 DuelSim.Rnd
// （Unity 中 = UnityEngine.Random.value；无头测试中 = 与 SG.Random 相同的 mulberry32 序列），
// 因此同一种子下与网页版 node tests/duel-bots.js 的结果逐场一致。
//
// 规则（DESIGN-V2 §4E）：体力 100；每击伤害 = 基础 × (0.55 + 武力/100) × (1 + (己武力 − 敌武力)/150)
// （后一项限制在 0.6–1.5，单击 1–40）；高武力出招略快、硬直略短；格挡减伤 80%；怒气满时可放绝技；
// 格开：被连段第二段打中后，在第三段打到前约 0.1 秒内按下格挡即可格开；限时 60 秒，KO 或时间到时体力高者胜
// （相同则挑战者胜，与 BattleModel.Duel 一致）。
// ==========================================================================
using System;
using System.Collections.Generic;

namespace Sanguo
{
    // ------------------------------------------------------------ 招式 --
    public sealed class DuelMove
    {
        public string name, kind, next;
        public double startup, active, recovery, dmg, dmgCharge, finisher, stun, push, reachK, hitstop, hmax, lunge;
        public bool big;
        public double[] hits;
        public double Total { get { return startup + active + recovery; } }
    }

    public sealed class DuelWeapon
    {
        public string key, name, style;
        public double reach, pow;
        public DuelWeapon(string key, string name, string style, double reach, double pow = 0) { this.key = key; this.name = name; this.style = style; this.reach = reach; this.pow = pow; }
    }

    // 操作输入（玩家键盘 / 触摸、电脑、测试脚本共用）
    public sealed class DuelHeld { public int x, dash; public bool up, light, heavy, guard, special; }
    public sealed class DuelTaps { public int light, up, special, heavy, dir; }
    public interface IDuelCtrl
    {
        DuelHeld Held { get; }
        DuelTaps Taps { get; }
        void Update(double dt, DuelSim sim);   // 每个逻辑步长之前调用（玩家输入为空实现）
    }

    // 逻辑事件（画面、声音、HUD 据此表现）
    public sealed class DuelEvent
    {
        public string type, move;
        public int who = -1, att = -1, def = -1, dmg, combo, winner = -1, id, n;
        public bool guarded, parry, big, counter, breaks, arrow, finisher, fin, hit;
        public double charge, x, y;
    }

    // 命中记录（兼容 BattleModel.Duel 的 rounds：who / dmg / hpA / hpB）
    public sealed class DuelLogEntry
    {
        public int who, dmg, hpA, hpB;
        public string move;
        public bool guarded;
        public double t;
        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}", who, dmg, hpA, hpB, move, guarded ? 1 : 0, t);
        }
    }

    public sealed class DuelOutcome
    {
        public int winner, hpA, hpB;
        public string kind;
        public double time;
        public List<DuelLogEntry> log;
    }

    // ------------------------------------------------------------ 外观 --
    // 知名武将的固定设定（未设的项为 null / 0 / false）
    public sealed class DuelLookFix
    {
        public string culture, weapon, weaponName, helm, helmColor, robe, skin, beard, cape, metal, armor, plume, hair, torso, coat, paint, shield;
        public double bulk, height;
        public bool goldTip, bareArms, bare, belly, bow, eyepatch, bells, female, rattan, hide, shieldSet;
    }
    public sealed class DuelCulture
    {
        public string helm, skin, hair, paint, torso, coat, shield, beard, helmColor, armor;
        public string[] weapons;
        public bool bareArms, hide, shells, magatama, fur, bow, gold, plaid;
    }
    public sealed class DuelLook
    {
        public string culture, weapon, weaponName, helm, helmColor, plume, armor, torso, coat, shield, paint, metal, cape, robe, skin, hair, beard;
        public bool plaid, fur, magatama, shells, gold, bare, belly, bareArms, hide, rattan, eyepatch, bow, bells, female, goldTip;
        public double bulk, height;
    }
    public sealed class DuelSpecialInfo { public string name, color; }

    public static class DuelLooks
    {
        // 文化 / 性别查询（= SG.WorldData.cultureOfGeneral / sexOf）；无头测试置 null 以与网页版的 node 测试环境一致
        public static Func<string, string> CultureLookup = name => WorldLookup.CultureOfGeneral(name);
        public static Func<string, string> SexLookup = name => WorldScenarioData.InfoOfGeneral(name) != null ? WorldLookup.SexOf(name) : null;

        // style：pole 双手长兵 / one 单手兵 / dual 双持；reach 判定距离（米）；pow 兵器威力系数（缺省 1）
        public static readonly Dictionary<string, DuelWeapon> Weapons = new Dictionary<string, DuelWeapon>();
        static void W(string k, string n, string s, double r, double p = 0) { Weapons[k] = new DuelWeapon(k, n, s, r, p); }
        static DuelLooks()
        {
            W("guandao", "青龙偃月刀", "pole", 2.0, 1.05);
            W("snake", "丈八蛇矛", "pole", 2.05);
            W("ji", "方天画戟", "pole", 2.0, 1.06);
            W("spear", "长枪", "pole", 2.05);
            W("poleblade", "大刀", "pole", 1.95, 1.07);
            W("axe", "大斧", "pole", 1.88, 1.12);
            W("bigblade", "斩马刀", "pole", 1.88, 1.14);
            W("sword", "长剑", "one", 1.78);
            W("dao", "环首刀", "one", 1.75);
            W("shortji", "短戟", "one", 1.8);
            W("mace", "骨朵", "one", 1.72, 0.97);
            W("twinsword", "双股剑", "dual", 1.72, 0.96);
            W("twinji", "双铁戟", "dual", 1.75, 0.97);
            W("twindao", "双刀", "dual", 1.68);
            // 异域兵器（DESIGN-V2 §6）
            W("gladius", "短剑", "one", 1.66, 1.03);
            W("lance", "长矛", "pole", 2.12);
            W("handaxe", "战斧", "one", 1.7, 1.03);
            W("tachi", "直刀", "one", 1.76);
        }

        // 知名武将的外观（未列出的按能力值与姓名哈希确定性生成）
        public static readonly Dictionary<string, DuelLookFix> Fixed = new Dictionary<string, DuelLookFix>
        {
            { "关羽", new DuelLookFix { weapon = "guandao", helm = "scarf", helmColor = "#2f7d4a", robe = "#2f7d4a", skin = "#b4503c", beard = "long", bulk = 1.06, height = 1.06, cape = "#24653b", metal = "#d4b25a" } },
            { "张飞", new DuelLookFix { weapon = "snake", helm = "han", armor = "#2c2c33", metal = "#55565e", beard = "bushy", skin = "#9c7258", bulk = 1.16, cape = "#3a1f1a" } },
            { "吕布", new DuelLookFix { weapon = "ji", helm = "pheasant", metal = "#e0b048", armor = "#a3302a", cape = "#b8302a", bulk = 1.08, height = 1.07, beard = "none" } },
            { "刘备", new DuelLookFix { weapon = "twinsword", helm = "crown", beard = "short", metal = "#d8b860" } },
            { "赵云", new DuelLookFix { weapon = "spear", weaponName = "涯角枪", helm = "plume", plume = "#f4f4f4", armor = "#e4e6ec", metal = "#c8d0da", cape = "#eef0f4", beard = "none" } },
            { "马超", new DuelLookFix { weapon = "spear", weaponName = "虎头湛金枪", goldTip = true, helm = "lion", armor = "#e2e4ea", metal = "#d0d6de", cape = "#f2f2f6", beard = "none" } },
            { "典韦", new DuelLookFix { weapon = "twinji", helm = "scarf", helmColor = "#c9a23a", skin = "#8e6448", beard = "bushy", bulk = 1.2, bareArms = true } },
            { "许褚", new DuelLookFix { weapon = "bigblade", helm = "bald", bare = true, bulk = 1.24, belly = true, beard = "stubble", skin = "#c99a74" } },
            { "黄忠", new DuelLookFix { weapon = "poleblade", weaponName = "凤嘴刀", bow = true, beard = "long", hair = "#e8e4dc", helm = "han" } },
            { "孙策", new DuelLookFix { weapon = "spear", weaponName = "霸王枪", helm = "plume", plume = "#d43a2a", beard = "none" } },
            { "孙坚", new DuelLookFix { weapon = "dao", weaponName = "古锭刀", helm = "scarf", helmColor = "#c8382c", beard = "short" } },
            { "太史慈", new DuelLookFix { weapon = "shortji", helm = "han", beard = "short" } },
            { "夏侯惇", new DuelLookFix { weapon = "spear", helm = "han", eyepatch = true, beard = "short" } },
            { "夏侯渊", new DuelLookFix { weapon = "dao", bow = true, beard = "short" } },
            { "张辽", new DuelLookFix { weapon = "poleblade", weaponName = "钩镰刀", beard = "short" } },
            { "徐晃", new DuelLookFix { weapon = "axe", weaponName = "大斧", beard = "short" } },
            { "潘凤", new DuelLookFix { weapon = "axe", weaponName = "开山大斧", beard = "bushy" } },
            { "颜良", new DuelLookFix { weapon = "poleblade", beard = "bushy", bulk = 1.1 } },
            { "文丑", new DuelLookFix { weapon = "spear", beard = "bushy", bulk = 1.1 } },
            { "华雄", new DuelLookFix { weapon = "poleblade", bulk = 1.12, beard = "bushy" } },
            { "庞德", new DuelLookFix { weapon = "poleblade", beard = "short" } },
            { "魏延", new DuelLookFix { weapon = "poleblade", skin = "#a8705a", beard = "short" } },
            { "周仓", new DuelLookFix { weapon = "poleblade", skin = "#7e5a40", beard = "bushy", bulk = 1.1 } },
            { "关平", new DuelLookFix { weapon = "poleblade", beard = "none" } },
            { "甘宁", new DuelLookFix { weapon = "dao", bells = true, beard = "short" } },
            { "凌统", new DuelLookFix { weapon = "twindao", beard = "none" } },
            { "周泰", new DuelLookFix { weapon = "dao", beard = "short" } },
            { "姜维", new DuelLookFix { weapon = "spear", beard = "short" } },
            { "张郃", new DuelLookFix { weapon = "spear", beard = "short" } },
            { "高顺", new DuelLookFix { weapon = "spear", beard = "short" } },
            { "纪灵", new DuelLookFix { weapon = "ji", weaponName = "三尖刀", beard = "bushy" } },
            { "张绣", new DuelLookFix { weapon = "spear", beard = "short" } },
            { "公孙瓒", new DuelLookFix { weapon = "spear", helm = "plume", plume = "#f0f0f0", armor = "#dcdce4", beard = "short" } },
            { "董卓", new DuelLookFix { weapon = "sword", bulk = 1.3, belly = true, beard = "bushy", helm = "crown" } },
            { "曹操", new DuelLookFix { weapon = "sword", weaponName = "倚天剑", beard = "short", helm = "crown" } },
            { "袁绍", new DuelLookFix { weapon = "sword", helm = "crown", beard = "short" } },
            { "诸葛亮", new DuelLookFix { weapon = "sword", helm = "guan", robe = "#e6e2d4", beard = "short" } },
            { "周瑜", new DuelLookFix { weapon = "sword", beard = "none" } },
            { "吕蒙", new DuelLookFix { weapon = "dao", beard = "short" } },
            // 南中诸族
            { "孟获", new DuelLookFix { culture = "nanman", weapon = "bigblade", bulk = 1.16, beard = "bushy" } },
            { "祝融", new DuelLookFix { culture = "nanman", weapon = "twindao", female = true, beard = "none" } },
            { "孟优", new DuelLookFix { culture = "nanman", weapon = "mace" } },
            { "兀突骨", new DuelLookFix { culture = "nanman", weapon = "mace", rattan = true, bulk = 1.3, height = 1.1, skin = "#6a4a36" } },
            { "带来洞主", new DuelLookFix { culture = "nanman", weapon = "spear" } },
            { "沙摩柯", new DuelLookFix { culture = "nanman", weapon = "mace", weaponName = "铁蒺藜骨朵", skin = "#8a4a34" } },
        };

        // 文化缺省外观（DESIGN-V2 §6）
        public static readonly Dictionary<string, DuelCulture> Cultures = new Dictionary<string, DuelCulture>
        {
            { "han", new DuelCulture { helm = "han", skin = "#e8b996", hair = "#16120f" } },
            { "nanman", new DuelCulture { helm = "topknot", skin = "#a9714c", hair = "#120e0b", bareArms = true, hide = true } },
            { "yi", new DuelCulture { helm = "yifeather", skin = "#a9714c", hair = "#120e0b", bareArms = true, paint = "#1e2c48", shells = true, weapons = new[] { "spear", "mace" } } },
            { "wa", new DuelCulture { helm = "mizura", skin = "#e2b48e", hair = "#141010", torso = "coat", coat = "#d8ccb0", weapons = new[] { "tachi", "tachi", "spear" }, shield = "wood", magatama = true, beard = "short" } },
            { "korea", new DuelCulture { helm = "feather", skin = "#e8bb98", hair = "#151111", weapons = new[] { "dao", "spear", "dao", "shortji" } } },
            { "steppe", new DuelCulture { helm = "fur", skin = "#d9a77e", hair = "#1b1410", torso = "coat", coat = "#7a5634", fur = true, weapons = new[] { "dao", "spear", "mace" }, bow = true } },
            { "seasia", new DuelCulture { helm = "goldcrown", skin = "#9b6a48", hair = "#120e0b", bareArms = true, gold = true, weapons = new[] { "spear", "dao" }, shield = "round", beard = "none" } },
            { "tarim", new DuelCulture { helm = "hutmao", skin = "#e3b494", hair = "#3a2416", torso = "coat", coat = "#9a4a4a", weapons = new[] { "sword", "spear" } } },
            { "kushan", new DuelCulture { helm = "pointed", skin = "#d6a07a", hair = "#24170f", torso = "coat", coat = "#5a4a86", weapons = new[] { "sword", "lance" }, gold = true } },
            { "persia", new DuelCulture { helm = "tiara", skin = "#d9a47c", hair = "#1a110c", torso = "scale", weapons = new[] { "lance", "mace", "sword" }, beard = "bushy" } },
            { "arab", new DuelCulture { helm = "kufiya", helmColor = "#ece4d0", skin = "#c58f66", hair = "#1a110c", torso = "coat", coat = "#e8dfc8", weapons = new[] { "spear", "sword" }, shield = "round", beard = "bushy" } },
            { "roman", new DuelCulture { helm = "galea", skin = "#e4b48e", hair = "#3a2618", torso = "segm", armor = "#b9bec6", weapons = new[] { "gladius", "gladius", "sword" }, shield = "scutum", beard = "none" } },
            { "celt", new DuelCulture { helm = "celtic", skin = "#f0c8a6", hair = "#c0682e", torso = "bare", paint = "#2a58b0", plaid = true, weapons = new[] { "sword" }, shield = "oval", beard = "long" } },
            { "german", new DuelCulture { helm = "suebian", skin = "#efc6a4", hair = "#c49a4c", torso = "coat", coat = "#6a5a46", fur = true, weapons = new[] { "handaxe", "spear", "handaxe" }, shield = "round", beard = "bushy" } },
            { "sarmatian", new DuelCulture { helm = "conical", skin = "#e2b08a", hair = "#2a1a10", torso = "scale", weapons = new[] { "lance", "lance", "sword" } } },
        };

        // FNV-1a（按 UTF-16 码元，同 JS charCodeAt）
        public static uint HashStr(string s)
        {
            uint h = 2166136261u;
            if (s == null) s = "";
            unchecked { for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; } }
            return h;
        }
        static string Or(string a, string b) { return string.IsNullOrEmpty(a) ? b : a; }

        public static double WarOf(General gen) { return gen == null ? 50 : DuelSim.Clamp(gen.war, 1, 120); }

        // 解析外观：知名设定 > 文化缺省 > 能力值 + 姓名哈希
        public static DuelLook LookOf(General gen, string culture = null)
        {
            string name = gen != null && !string.IsNullOrEmpty(gen.name) ? gen.name : "无名";
            DuelLookFix fix; if (!Fixed.TryGetValue(name, out fix)) fix = new DuelLookFix();
            bool known = Fixed.ContainsKey(name);
            string cul = Or(culture, Or(fix.culture, gen != null ? gen.culture : null));
            if (string.IsNullOrEmpty(cul) && CultureLookup != null) { try { cul = CultureLookup(name); } catch (Exception) { cul = null; } }
            if (cul == null || !Cultures.ContainsKey(cul)) cul = "han";
            bool female = fix.female || (gen != null && (gen.sex == "f" || gen.female));
            if (!female && fix.culture == null && SexLookup != null) { try { female = SexLookup(name) == "f"; } catch (Exception) { /* 忽略 */ } }
            var bs = Cultures[cul];
            double war = WarOf(gen);
            int intel = gen != null && gen.intel != 0 ? gen.intel : 50;
            uint h = HashStr(name);
            string weapon = fix.weapon;
            if (weapon == null)
            {
                if (war >= 85) weapon = new[] { "poleblade", "spear", "ji", "spear", "axe", "poleblade" }[h % 6];
                else if (intel > war + 15) weapon = "sword";
                else weapon = new[] { "spear", "sword", "dao", "spear", "shortji" }[h % 5];
                if (cul == "nanman") weapon = war >= 80 ? "bigblade" : "mace";
                else if (bs.weapons != null) weapon = bs.weapons[(h >> 5) % (uint)bs.weapons.Length];
            }
            var beards = new[] { "none", "short", "short", "long", "bushy" };
            var wp = Weapons[weapon];
            var look = new DuelLook
            {
                culture = cul,
                weapon = weapon,
                weaponName = Or(fix.weaponName, wp.name),
                helm = Or(fix.helm, cul == "han" && intel > war + 25 && war < 55 ? "guan" : bs.helm),
                helmColor = Or(fix.helmColor, bs.helmColor),
                plume = fix.plume,
                armor = Or(fix.armor, bs.armor),                  // null = 势力色
                torso = Or(fix.torso, fix.bare ? "bare" : bs.torso) ?? "zha",
                coat = Or(fix.coat, bs.coat),
                shield = wp.style == "one" ? (fix.shieldSet ? fix.shield : bs.shield) : null,
                paint = Or(fix.paint, bs.paint),
                plaid = bs.plaid, fur = bs.fur, magatama = bs.magatama, shells = bs.shells, gold = bs.gold,
                metal = Or(fix.metal, war >= 90 ? "#c9a24e" : "#8e939c"),
                cape = fix.cape,
                robe = fix.robe,
                skin = Or(fix.skin, bs.skin),
                hair = Or(fix.hair, bs.hair),
                beard = female ? "none" : Or(fix.beard, bs.beard != null && (h >> 3) % 4 != 0 ? bs.beard : beards[(h >> 3) % (uint)beards.Length]),
                bulk = fix.bulk != 0 ? fix.bulk : DuelSim.Clamp(0.95 + (war - 50) / 100 * 0.14 + ((int)((h >> 7) % 7) - 3) * 0.01, 0.9, 1.12),
                height = fix.height != 0 ? fix.height : (1 + ((int)((h >> 11) % 5) - 2) * 0.012),
                bare = fix.bare || (fix.torso == null && bs.torso == "bare"),
                belly = fix.belly,
                bareArms = fix.bareArms || bs.bareArms,
                hide = fix.hide || bs.hide || (cul == "nanman" && !fix.rattan),
                rattan = fix.rattan,
                eyepatch = fix.eyepatch,
                bow = fix.bow || (bs.bow && !known),
                bells = fix.bells,
                female = female,
                goldTip = fix.goldTip,
            };
            return look;
        }

        // 绝技名称与配色：优先必杀技（Specials.Of），否则按武将 / 兵器给兜底名称
        static readonly Dictionary<string, string> FallbackSpecial = new Dictionary<string, string>
        {
            { "关羽", "青龙偃月斩" }, { "张飞", "长坂怒吼" }, { "赵云", "七进七出" }, { "吕布", "天下无双" }, { "黄忠", "百步穿杨" }, { "典韦", "双戟护主" },
            { "马超", "锦马超枪" }, { "许褚", "虎痴裸衣" }, { "刘备", "双股连环" }, { "孙策", "小霸王" }, { "太史慈", "神亭酣斗" },
        };
        static readonly Dictionary<string, string> StyleSpecial = new Dictionary<string, string>
        {
            { "guandao", "偃月斩" }, { "snake", "蛇矛乱刺" }, { "ji", "画戟横扫" }, { "spear", "百鸟朝凤" }, { "poleblade", "力劈华山" }, { "axe", "开山裂石" }, { "bigblade", "斩马横行" },
            { "sword", "剑气纵横" }, { "dao", "旋风刀" }, { "shortji", "短戟连环" }, { "mace", "碎岳一击" }, { "twinsword", "双龙乱舞" }, { "twinji", "双戟乱舞" }, { "twindao", "双刀旋斩" },
            { "gladius", "盾阵突刺" }, { "lance", "铁骑冲阵" }, { "handaxe", "旋斧劈" }, { "tachi", "直刀一闪" },
        };
        public static DuelSpecialInfo SpecialOf(General gen, DuelLook look)
        {
            Special s = null;
            try { s = gen != null ? Specials.Of(gen) : null; } catch (Exception) { s = null; }
            string name = s != null && !string.IsNullOrEmpty(s.name) ? s.name : null;
            string fb;
            if (name == null && gen != null && gen.name != null && FallbackSpecial.TryGetValue(gen.name, out fb)) name = fb;
            if (name == null && StyleSpecial.TryGetValue(look.weapon, out fb)) name = fb;
            if (name == null) name = "奋武一击";
            string color = s != null && !string.IsNullOrEmpty(s.color) ? s.color : null;
            return new DuelSpecialInfo { name = name, color = color };
        }
    }

    // ------------------------------------------------------------ 武将 --
    public sealed class DuelFighter
    {
        public readonly int idx;
        public readonly General gen;
        public readonly double war;
        public readonly DuelLook look;
        public readonly DuelWeapon wpn;
        public double reach, spd, stunK, walk;
        public double x, y, vx, vy;
        public int face;
        public int hp = 100;
        public double rage;
        public string state = "idle";
        public double t;
        public int seq;
        public int hitIdx;          // 本招已命中段数
        public double charge;       // 重击蓄力 0..1
        public double stun;         // 当前硬直时长
        public double invuln;
        public int combo;           // 连击数（对手连续处于受击硬直）
        public bool chain;          // 连段输入
        public bool airUsed, airHit, tapHeavy, chargeFull, holdGuard;
        public int dashDir;
        public bool ko;
        public double landT;        // 0 = 未设（JS 的 undefined）
        public int swungSeq = -1;
        public IDuelCtrl ctrl;
        // 上一步的按住状态
        public int pX; public bool pUp, pLight, pHeavy, pSpecial, pGuard;
        public double parryAt = -9;  // 受击硬直中按下格挡的时刻（格开判定；每段硬直只算第一次按下）
        public int parrySeq = -1;
        public double bufLight = -9, bufJump = -9, bufSpecial = -9, bufHeavy = -9, bufDash = -9;
        public int lastTapDir; public double lastTapT = -9;
        public int stSwings, stHits, stGuarded, stTaken;
        public readonly bool bow;   // 弓将（黄忠、夏侯渊）：绝技为连珠箭（远程），不突进

        public DuelFighter(int idx, General gen, DuelLook look)
        {
            this.idx = idx; this.gen = gen; this.look = look;
            war = DuelLooks.WarOf(gen);
            DuelWeapon w; wpn = DuelLooks.Weapons.TryGetValue(look.weapon, out w) ? w : DuelLooks.Weapons["sword"];
            reach = wpn.reach;
            spd = DuelSim.ActSpeed(war) * DuelSim.StyleSpd(wpn.style);
            stunK = DuelSim.StunK(war);
            walk = DuelSim.WalkSpeed(war);
            x = idx == 0 ? -2.3 : 2.3;
            face = idx == 0 ? 1 : -1;
            bow = look.bow;
        }
        public bool Grounded { get { return y <= 0 && vy <= 0; } }
        // 出招阶段："startup" | "active" | "recovery" | null
        public string Phase
        {
            get
            {
                var mv = DuelSim.Mv(state);
                if (mv == null) return null;
                if (t < mv.startup) return "startup";
                if (t < mv.startup + mv.active) return "active";
                return "recovery";
            }
        }
    }

    // ------------------------------------------------------------ 对战 --
    public sealed class DuelSim
    {
        // ---- 常量
        public const double DT = 1.0 / 60;      // 逻辑步长
        public const double ARENA = 7.2;        // 场地半宽（米）
        public const double BODY = 0.4;         // 推挤半径
        public const double GRAV = 26;
        public const double JUMP_V = 7.4;
        public const double FRICTION = 14;
        public const double BUFFER = 0.16;      // 输入缓冲（秒）
        public const double TIME_LIMIT = 60;
        public const double CHARGE_MAX = 0.85;  // 满蓄所需时间
        public const double SPECIAL_FREEZE = 0.62;  // 绝技起手时对手定格的时间
        public const double PARRY_WIN = 0.1;    // 格开窗口

        // 规则随机数（[0, 1)）。Unity：UnityEngine.Random.value；测试：mulberry32
        static readonly Random sysRnd = new Random();
        public static Func<double> Rnd = () => sysRnd.NextDouble();

        public static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        public static double Clamp01(double v) { return Clamp(v, 0, 1); }
        public static double JsRound(double v) { return Math.Floor(v + 0.5); }

        // 招式时序（基准秒；出招时钟按 spd 缩放）。reachK × 兵器长度 = 判定距离
        public static readonly DuelMove Light1 = new DuelMove { name = "light1", kind = "light", startup = 0.10, active = 0.07, recovery = 0.19, dmg = 2.4, stun = 0.30, push = 2.6, reachK = 0.92, next = "light2", hitstop = 0.065, hmax = 0.85 };
        public static readonly DuelMove Light2 = new DuelMove { name = "light2", kind = "light", startup = 0.09, active = 0.07, recovery = 0.21, dmg = 2.4, stun = 0.32, push = 2.9, reachK = 0.95, next = "light3", hitstop = 0.07, hmax = 0.85 };
        public static readonly DuelMove Light3 = new DuelMove { name = "light3", kind = "light", startup = 0.15, active = 0.09, recovery = 0.44, dmg = 4, stun = 0.46, push = 5.0, reachK = 1.0, hitstop = 0.10, hmax = 1.45, big = true, lunge = 1.8 };
        public static readonly DuelMove Heavy = new DuelMove { name = "heavy", kind = "heavy", startup = 0.09, active = 0.10, recovery = 0.40, dmg = 4.8, dmgCharge = 5, stun = 0.5, push = 4.6, reachK = 1.08, hitstop = 0.12, hmax = 1.5, big = true, lunge = 2.2 };
        public static readonly DuelMove Air = new DuelMove { name = "air", kind = "air", startup = 0.05, active = 0.20, recovery = 0.10, dmg = 3, stun = 0.36, push = 3.0, reachK = 0.88, hitstop = 0.08, hmax = 9 };
        // 绝技：定格起手 → 突进 → 三段斩 + 终结一击（倒地）
        public static readonly DuelMove Special = new DuelMove { name = "special", kind = "special", startup = SPECIAL_FREEZE + 0.1, active = 0.62, recovery = 0.45, dmg = 4.5, finisher = 7, stun = 0.5, push = 1.6, reachK = 1.0, hitstop = 0.09, hmax = 9, big = true, hits = new[] { 0.0, 0.17, 0.34, 0.56 } };

        public static DuelMove Mv(string state)
        {
            switch (state)
            {
                case "light1": return Light1;
                case "light2": return Light2;
                case "light3": return Light3;
                case "heavy": return Heavy;
                case "air": return Air;
                case "special": return Special;
            }
            return null;
        }
        public static bool IsAttack(string s) { return Mv(s) != null; }
        public static double StyleSpd(string style) { return style == "one" ? 1.14 : style == "dual" ? 1.18 : 1.0; }

        // ---- 数值
        // 每击伤害 = 基础 × (0.55 + 武力/100) × (1 + (己武力 − 敌武力)/150)
        public static double Damage(double bs, double war, double foeWar)
        {
            double k = Clamp(1 + (war - foeWar) / 150, 0.6, 1.5);
            return bs * (0.55 + war / 100) * k;
        }
        public static double ActSpeed(double war) { return 0.9 + Clamp(war, 0, 110) / 100 * 0.22; }   // 出招速度（武力 100 → 1.12）
        public static double StunK(double war) { return 1.12 - Clamp(war, 0, 110) / 100 * 0.2; }      // 受击硬直倍率（武力 100 → 0.92）
        public static double WalkSpeed(double war) { return 2.3 * (0.92 + Clamp(war, 0, 110) / 100 * 0.16); }

        // ---- 状态
        public readonly DuelFighter[] f;
        public double time;               // 战斗计时（秒）
        public double clock;              // 逻辑总时钟（含定格），供输入缓冲与 AI 感知
        public double limit = TIME_LIMIT;
        public bool running;              // false：开场 / 结束演出（不接受操作）
        public bool hasResult; public int resultWinner = -1; public string resultKind;
        public double endT;               // 结果判定后经过的时间
        public double hitstop;
        public readonly double[] freeze = new double[2];   // 绝技起手：对手定格
        public readonly List<DuelEvent> events = new List<DuelEvent>();
        public readonly List<DuelLogEntry> log = new List<DuelLogEntry>();
        public int steps;
        public readonly List<Shot> shots = new List<Shot>();   // 飞行中的箭（弓将绝技）
        int shotId;
        // 供 AI 延迟感知的历史快照（环形缓冲，最多 90 条）
        public sealed class Snap { public double clock, x, y, charge; public string state, phase; public int seq; public bool atk; }
        public sealed class Shot { public int id, owner, n; public double x, y, vx; public bool fin, hit; }
        const int HistN = 90;
        readonly Snap[][] hist = { new Snap[HistN], new Snap[HistN] };
        readonly int[] histHead = new int[2], histCount = new int[2];

        static readonly DuelHeld NoHeld = new DuelHeld();

        public DuelSim(General genA, General genB, DuelLook lookA, DuelLook lookB)
        {
            f = new[] { new DuelFighter(0, genA, lookA), new DuelFighter(1, genB, lookB) };
            for (int i = 0; i < 2; i++) for (int k = 0; k < HistN; k++) hist[i][k] = new Snap();
        }
        public DuelFighter Other(DuelFighter x) { return f[1 - x.idx]; }
        public bool Actionable(DuelFighter x) { return x.state == "idle" || x.state == "walk"; }
        void Emit(DuelEvent e) { events.Add(e); if (events.Count > 200) events.RemoveAt(0); }

        // 推进一个逻辑步长
        public void Step(double dt = DT)
        {
            steps++;
            if (hitstop > 0) { hitstop -= dt; return; }
            clock += dt;
            DuelFighter a = f[0], b = f[1];
            if (running && !hasResult)
                foreach (var x in f) if (x.ctrl != null) x.ctrl.Update(dt, this);
            // 定格状态在步首统一判定（与更新顺序无关，避免先后手偏差）
            bool fz0 = freeze[0] > 0, fz1 = freeze[1] > 0;
            if (fz0) freeze[0] -= dt;
            if (fz1) freeze[1] -= dt;
            if (!fz0) UpdateFighter(a, b, dt);
            if (!fz1) UpdateFighter(b, a, dt);
            if (!fz0) Physics(a, dt);
            if (!fz1) Physics(b, dt);
            Separate(a, b);
            var ha = !fz0 ? HitCheck(a, b) : null;
            var hb = !fz1 ? HitCheck(b, a) : null;
            if (ha != null) Hit(a, b, ha);
            if (hb != null) Hit(b, a, hb);
            if (shots.Count > 0) UpdateShots(dt);
            if (running && !hasResult)
            {
                time += dt;
                foreach (var x in f) x.rage = Math.Min(100, x.rage + dt * 0.7);
                if (time >= limit) TimeUp();
            }
            if (hasResult) endT += dt;
            Record();
        }

        void Record()
        {
            foreach (var x in f)
            {
                int i = x.idx;
                int slot = (histHead[i] + histCount[i]) % HistN;
                if (histCount[i] == HistN) { slot = histHead[i]; histHead[i] = (histHead[i] + 1) % HistN; }
                else histCount[i]++;
                var s = hist[i][slot];
                var mv = Mv(x.state);
                string ph = x.Phase;
                s.clock = clock; s.state = x.state; s.x = x.x; s.y = x.y; s.seq = x.seq; s.phase = ph; s.atk = mv != null && ph != "recovery"; s.charge = x.charge;
            }
        }
        // 延迟 delay 秒之前看到的对手状态
        public Snap Seen(int idx, double delay)
        {
            int n = histCount[idx];
            if (n == 0) return null;
            double want = clock - delay;
            for (int k = n - 1; k >= 0; k--)
            {
                var s = hist[idx][(histHead[idx] + k) % HistN];
                if (s.clock <= want) return s;
            }
            return hist[idx][histHead[idx]];
        }

        void SetState(DuelFighter x, string s)
        {
            x.state = s; x.t = 0; x.seq++;
            x.hitIdx = 0; x.chain = false;
        }

        // 读取输入沿：按下瞬间或期间的点击计数
        DuelHeld ReadInput(DuelFighter x)
        {
            var c = x.ctrl;
            bool live = running && !hasResult && c != null;
            var h = live && c.Held != null ? c.Held : NoHeld;
            var taps = live ? c.Taps : null;
            double now = clock;
            if ((h.light && !x.pLight) || (taps != null && taps.light > 0)) x.bufLight = now;
            if ((h.up && !x.pUp) || (taps != null && taps.up > 0)) x.bufJump = now;
            if ((h.special && !x.pSpecial) || (taps != null && taps.special > 0)) x.bufSpecial = now;
            if ((h.heavy && !x.pHeavy) || (taps != null && taps.heavy > 0)) x.bufHeavy = now;
            bool xNew = h.x != 0 && h.x != x.pX;
            if (xNew || (taps != null && taps.dir != 0))
            {
                int dir = xNew ? h.x : taps.dir;
                if (x.lastTapDir == dir && now - x.lastTapT < 0.28) { x.bufDash = now; x.dashDir = dir; x.lastTapDir = 0; x.lastTapT = -9; }
                else { x.lastTapDir = dir; x.lastTapT = now; }
            }
            if (h.dash != 0) { x.bufDash = now; x.dashDir = h.dash; }
            if (taps != null) { taps.light = 0; taps.up = 0; taps.special = 0; taps.heavy = 0; taps.dir = 0; }
            x.holdGuard = h.guard;
            if (h.guard && !x.pGuard && x.state == "hitstun" && x.parrySeq != x.seq) { x.parrySeq = x.seq; x.parryAt = now; }
            x.pX = h.x; x.pUp = h.up; x.pLight = h.light; x.pHeavy = h.heavy; x.pSpecial = h.special; x.pGuard = h.guard;
            return h;
        }
        bool Fresh(double buf) { return clock - buf <= BUFFER; }

        void UpdateFighter(DuelFighter x, DuelFighter o, double dt)
        {
            var h = ReadInput(x);
            var mv = Mv(x.state);
            x.t += dt * (mv != null ? (x.state == "special" ? 1 : x.spd) : 1);
            if (x.invuln > 0) x.invuln -= dt;
            if (x.Grounded && !x.ko && x.state != "knockdown" && x.state != "down") x.face = o.x >= x.x ? 1 : -1;

            switch (x.state)
            {
                case "idle":
                case "walk":
                    {
                        if (Fresh(x.bufSpecial) && x.rage >= 100) { x.bufSpecial = -9; StartSpecial(x, o); break; }
                        if (Fresh(x.bufDash) && x.dashDir != 0) { x.bufDash = -9; SetState(x, "dash"); Emit(new DuelEvent { type = "dash", who = x.idx }); break; }
                        if (Fresh(x.bufJump))
                        {
                            x.bufJump = -9;
                            SetState(x, "jump"); x.vy = JUMP_V; x.airUsed = false; x.airHit = false;
                            x.vx = h.x * x.walk * 0.95;
                            Emit(new DuelEvent { type = "jump", who = x.idx });
                            break;
                        }
                        if (Fresh(x.bufLight)) { x.bufLight = -9; StartAttack(x, "light1"); break; }
                        if (Fresh(x.bufHeavy) && h.heavy) { x.bufHeavy = -9; SetState(x, "charge"); x.charge = 0; break; }
                        if (Fresh(x.bufHeavy) && !h.heavy) { x.bufHeavy = -9; SetState(x, "charge"); x.charge = 0; x.tapHeavy = true; break; }
                        if (h.guard) { SetState(x, "guard"); x.vx = 0; break; }
                        if (h.x != 0)
                        {
                            if (x.state != "walk") { x.state = "walk"; x.seq++; x.t = 0; }
                            bool fwd = h.x == x.face;
                            x.vx = h.x * x.walk * (fwd ? 1 : 0.72);
                        }
                        else if (x.state != "idle") { x.state = "idle"; x.seq++; x.t = 0; }
                        break;
                    }
                case "guard":
                    x.vx = 0;
                    if (Fresh(x.bufDash) && x.dashDir != 0 && !h.guard) { x.bufDash = -9; SetState(x, "dash"); Emit(new DuelEvent { type = "dash", who = x.idx }); break; }
                    if (Fresh(x.bufLight)) { x.bufLight = -9; StartAttack(x, "light1"); break; }
                    if (!h.guard) SetState(x, "idle");
                    break;
                case "guardstun":
                case "guardbreak":
                    if (x.t >= x.stun) SetState(x, h.guard && x.state == "guardstun" ? "guard" : "idle");
                    break;
                case "charge":
                    x.charge = Math.Min(1, x.t / CHARGE_MAX);
                    if (x.charge >= 1 && !x.chargeFull) { x.chargeFull = true; Emit(new DuelEvent { type = "chargeFull", who = x.idx }); }
                    if (((!h.heavy || x.tapHeavy) && x.t >= 0.12) || x.t >= CHARGE_MAX + 0.3)
                    {
                        double c = x.charge;
                        x.tapHeavy = false; x.chargeFull = false;
                        StartAttack(x, "heavy");
                        x.charge = c;
                    }
                    break;
                case "light1":
                case "light2":
                case "light3":
                case "heavy":
                    {
                        if (x.state != "light3" && x.state != "heavy" && Fresh(x.bufLight) && x.t > 0.03) { x.bufLight = -9; x.chain = true; }
                        double total = mv.startup + mv.active + mv.recovery;
                        if (mv.lunge != 0 && x.t >= mv.startup * 0.6 && x.t < mv.startup + mv.active) x.vx = x.face * mv.lunge;
                        if (x.chain && mv.next != null && x.t >= mv.startup + mv.active * 0.6) { StartAttack(x, mv.next); break; }
                        if (x.t >= total) SetState(x, "idle");
                        break;
                    }
                case "special":
                    {
                        // 起手定格之后突进到对手面前（弓将原地连射）
                        double dx = (o.x - x.x) * x.face;
                        if (!x.bow && x.t >= SPECIAL_FREEZE && x.t < mv.startup + mv.active) x.vx = dx > x.reach * 0.7 ? x.face * 10 : 0;
                        else x.vx = 0;
                        if (x.t >= mv.startup + mv.active + mv.recovery) SetState(x, "idle");
                        break;
                    }
                case "dash":
                    x.vx = x.dashDir * 7.4;
                    if (x.t > 0.07 && Fresh(x.bufLight)) { x.bufLight = -9; StartAttack(x, "light1"); x.vx *= 0.6; break; }
                    if (x.t >= 0.22) SetState(x, "idle");
                    break;
                case "jump":
                    x.vx = Clamp(x.vx + h.x * 9 * dt, -x.walk, x.walk);
                    if (!x.airUsed && Fresh(x.bufLight)) { x.bufLight = -9; x.airUsed = true; StartAttack(x, "air"); }
                    break;
                case "air":
                    {
                        double total = mv.startup + mv.active + mv.recovery;
                        if (x.t >= total) { x.state = "jump"; x.seq++; x.t = 0.3; }
                        break;
                    }
                case "land":
                    // 落地硬直：空中出过招则较长（命中 / 被挡 0.2 秒，挥空 0.3 秒），跳入后可被反击
                    if (x.t >= (x.landT != 0 ? x.landT : 0.07)) SetState(x, h.guard ? "guard" : "idle");
                    break;
                case "hitstun":
                    // 硬直结束时若按住格挡，直接进入格挡（无空隙）
                    if (x.t >= x.stun) { SetState(x, h.guard && x.Grounded ? "guard" : "idle"); x.combo = 0; o.combo = 0; }
                    break;
                case "knockdown":
                    break;   // 落地在 Physics 里处理
                case "down":
                    if (!x.ko && x.t >= 0.62) SetState(x, "getup");
                    break;
                case "getup":
                    x.invuln = Math.Max(x.invuln, 0.05);
                    if (x.t >= 0.42) { SetState(x, h.guard ? "guard" : "idle"); x.invuln = 0.2; o.combo = 0; }
                    break;
            }

            // 结果判定后的演出：胜者摆出胜利姿势，败者跪倒 / 倒地不起
            if (hasResult)
            {
                bool win = resultWinner == x.idx;
                if (win && endT > (resultKind == "ko" ? 1.0 : 0.5) && (x.state == "idle" || x.state == "walk") && x.Grounded) { SetState(x, "victory"); x.vx = 0; }
                if (!win && !x.ko && endT > 0.5 && (x.state == "idle" || x.state == "walk" || x.state == "guard") && x.Grounded) { SetState(x, "defeat"); x.vx = 0; }
            }
        }

        void StartAttack(DuelFighter x, string name)
        {
            SetState(x, name);
            x.stSwings++;
            if (name != "air") x.vx *= 0.3;
        }

        void StartSpecial(DuelFighter x, DuelFighter o)
        {
            SetState(x, "special");
            x.rage = 0; x.vx = 0;
            x.stSwings++;
            // 对手定格；弓将的定格延续到第一支箭射到为止（近战绝技靠突进追上对手）
            freeze[o.idx] = SPECIAL_FREEZE + (x.bow ? 0.12 + Math.Max(0, Math.Abs(o.x - x.x) - 0.6) / 24 : 0);
            Emit(new DuelEvent { type = "special", who = x.idx });
        }

        void Physics(DuelFighter x, double dt)
        {
            if (x.y > 0 || x.vy > 0)
            {
                x.vy -= GRAV * dt;
                x.y += x.vy * dt;
                if (x.y <= 0)
                {
                    x.y = 0; x.vy = 0;
                    if (x.state == "knockdown") { SetState(x, "down"); x.vx *= 0.35; Emit(new DuelEvent { type = "down", who = x.idx, x = x.x }); }
                    else if (x.state == "jump" || x.state == "air")
                    {
                        SetState(x, "land"); x.vx *= 0.3;
                        x.landT = x.airUsed ? (x.airHit ? 0.2 : 0.3) : 0.07;
                        Emit(new DuelEvent { type = "land", who = x.idx, x = x.x });
                    }
                    else if (x.state == "hitstun") x.vx *= 0.5;
                }
            }
            else
            {
                var m = Mv(x.state);
                if (x.state != "walk" && x.state != "dash" && !(x.state == "special" && x.vx != 0) && !(m != null && m.lunge != 0 && x.t < m.startup + m.active))
                {
                    double s = Math.Sign(x.vx), v = Math.Abs(x.vx) - FRICTION * dt;
                    x.vx = v > 0 ? s * v : 0;
                }
            }
            x.x += x.vx * dt;
            if (x.x < -ARENA) { x.x = -ARENA; if (x.vx < 0) x.vx = 0; }
            if (x.x > ARENA) { x.x = ARENA; if (x.vx > 0) x.vx = 0; }
        }

        // 推挤：双方不得重叠、不得互相穿越
        void Separate(DuelFighter a, DuelFighter b)
        {
            double d = b.x - a.x, min = BODY * 2;
            if (Math.Abs(d) >= min) return;
            double s = d == 0 ? (a.face > 0 ? 1 : -1) : Math.Sign(d);
            double over = min - Math.Abs(d);
            a.x -= s * over * 0.5; b.x += s * over * 0.5;
            if (a.x < -ARENA) { b.x += -ARENA - a.x; a.x = -ARENA; }
            if (a.x > ARENA) { b.x -= a.x - ARENA; a.x = ARENA; }
            if (b.x < -ARENA) { a.x += -ARENA - b.x; b.x = -ARENA; }
            if (b.x > ARENA) { a.x -= b.x - ARENA; b.x = ARENA; }
        }

        sealed class HitInfo { public DuelMove mv; public string name; public bool fin, arrow, hasP; public double charge, px, py; }

        // 判定命中（只判定不结算，双方同一步内的命中视为相打）
        HitInfo HitCheck(DuelFighter x, DuelFighter o)
        {
            var mv = Mv(x.state);
            if (mv == null) return null;
            if (x.state == "special")
            {
                int idx = x.hitIdx;
                if (idx >= mv.hits.Length || x.t < mv.startup + mv.hits[idx]) return null;
                x.hitIdx++;
                bool fin = idx == mv.hits.Length - 1;
                if (x.bow)
                {
                    // 放箭：箭矢独立飞行，命中在 UpdateShots 里结算
                    var sh = new Shot { id = ++shotId, owner = x.idx, x = x.x + x.face * 0.55, y = fin ? 1.3 : 1.42, vx = x.face * (fin ? 30 : 24), fin = fin, n = idx };
                    shots.Add(sh);
                    Emit(new DuelEvent { type = "arrow", who = x.idx, id = sh.id, fin = fin, n = idx });
                    return null;
                }
                Emit(new DuelEvent { type = "swing", who = x.idx, move = "special", n = idx });
                double dx0 = (o.x - x.x) * x.face;
                if (dx0 > -0.2 && dx0 <= x.reach + BODY + 0.4 && !Immune(o)) return new HitInfo { mv = mv, name = "special", fin = fin, charge = 0 };
                return null;
            }
            if (x.Phase != "active" || x.hitIdx > 0) return null;
            if (x.swungSeq != x.seq) { x.swungSeq = x.seq; Emit(new DuelEvent { type = "swing", who = x.idx, move = x.state, charge = x.charge }); }
            double dx = (o.x - x.x) * x.face;
            double reach = x.reach * mv.reachK + BODY * 0.5;
            bool heightOk = x.state == "air" ? Math.Abs(o.y - x.y) < 1.7 : o.y < mv.hmax;
            if (dx > -0.15 && dx <= reach && heightOk && !Immune(o)) { x.hitIdx = 1; return new HitInfo { mv = mv, name = x.state, fin = false, charge = x.state == "heavy" ? x.charge : 0 }; }
            return null;
        }
        bool Immune(DuelFighter o) { return o.invuln > 0 || o.state == "knockdown" || o.state == "down" || o.state == "getup" || o.ko; }

        // 箭矢飞行与命中（穿过倒地 / 无敌的对手，飞出场外后消失）
        void UpdateShots(double dt)
        {
            int n = 0;
            for (int i = 0; i < shots.Count; i++)
            {
                var sh = shots[i];
                DuelFighter x = f[sh.owner], o = f[1 - sh.owner];
                double x0 = sh.x;
                sh.x += sh.vx * dt;
                double dir = Math.Sign(sh.vx);
                bool crossed = (o.x - dir * BODY * 0.5 - x0) * dir >= -0.05 && (o.x - dir * BODY * 0.5 - sh.x) * dir <= 0;
                if (crossed && !Immune(o) && o.y < 1.9 && !sh.hit)
                {
                    sh.hit = true;
                    Hit(x, o, new HitInfo { mv = Special, name = "special", fin = sh.fin, charge = 0, hasP = true, px = o.x - dir * 0.25, py = sh.y + o.y * 0.6, arrow = true });
                    Emit(new DuelEvent { type = "arrowEnd", id = sh.id, hit = true });
                    continue;
                }
                if (Math.Abs(sh.x) > ARENA + 6) { Emit(new DuelEvent { type = "arrowEnd", id = sh.id, hit = false }); continue; }
                shots[n++] = sh;
            }
            shots.RemoveRange(n, shots.Count - n);
        }

        void Hit(DuelFighter x, DuelFighter o, HitInfo h)
        {
            var mv = h.mv; string name = h.name; bool finisher = h.fin; double chg = h.charge;
            double bs = mv.dmg * (x.wpn.pow != 0 ? x.wpn.pow : 1);
            if (name == "heavy") bs += mv.dmgCharge * chg;
            if (name == "special" && finisher) bs = mv.finisher;
            if (h.arrow) bs *= 0.8;                     // 箭矢可远距离命中，威力略低
            bool counter = o.state == "charge" || (Mv(o.state) != null && o.Phase == "startup" && o.state != "special");
            if (counter) bs *= 1.2;
            double dmgF = Damage(bs, x.war, o.war) * (0.92 + Rnd() * (1.08 - 0.92));
            bool guarded = (o.state == "guard" || o.state == "guardstun") && o.Grounded;
            bool parry = false;
            // 格开（看破连段）：被第二段打中后，在第三段打到前 PARRY_WIN 秒内按下格挡（只认这段硬直里的第一次按下）
            if (!guarded && name == "light3" && o.state == "hitstun" && o.Grounded && o.hp > 0 && o.parrySeq == o.seq && clock - o.parryAt <= PARRY_WIN) { guarded = true; parry = true; }
            // 连段中的后续一击，受击方按住格挡且武力远高于攻方时，有机会格开（武力差 30 → 18%，70 → 50%）
            if (!guarded && o.state == "hitstun" && o.holdGuard && o.Grounded && o.hp > 0 && name != "special")
            {
                double p = Clamp((o.war - x.war - 12) / 100, 0, 0.5);
                if (p > 0 && Rnd() < p) { guarded = true; parry = true; }
            }
            bool charged = name == "heavy" && chg >= 0.85;
            bool breaks = guarded && (charged || (name == "special" && finisher));
            if (guarded) dmgF *= 0.2;
            int dmg = (int)Clamp(JsRound(dmgF), guarded ? 0 : 1, 40);
            o.hp = Math.Max(0, o.hp - dmg);
            int dir = x.face;
            bool big = mv.big || charged;
            double hs = mv.hitstop * (guarded ? 0.6 : 1) * (charged ? 1.3 : 1);
            x.stHits++;
            if (name == "air") x.airHit = true;
            if (guarded) x.stGuarded++;
            o.stTaken++;

            bool knock = false;
            if (o.hp <= 0)
            {
                knock = true; o.ko = true;
            }
            else if (guarded)
            {
                if (breaks) { SetState(o, "guardbreak"); o.stun = 0.8 * o.stunK; Emit(new DuelEvent { type = "guardbreak", who = o.idx }); }
                else { SetState(o, "guardstun"); o.stun = parry ? 0.1 : (0.12 + mv.stun * 0.35) * o.stunK; }
                o.vx = dir * mv.push * (parry ? 0.15 : mv.big ? 0.5 : 0.85);     // 大招被挡时推开得少；格开几乎不退、立刻能还手
                o.rage = Math.Min(100, o.rage + 2 + dmg);
                x.rage = Math.Min(100, x.rage + 1.5);
            }
            else
            {
                knock = (name == "heavy" && chg >= 0.6) || (name == "special" && finisher) || o.y > 0.35;
                x.combo = o.state == "hitstun" ? x.combo + 1 : 1;
                o.rage = Math.Min(100, o.rage + 3 + dmg * 1.2);
                x.rage = Math.Min(100, x.rage + 3 + dmg * 0.8);
                if (!knock)
                {
                    SetState(o, "hitstun");
                    o.stun = mv.stun * o.stunK * (name == "heavy" ? 1 + chg * 0.3 : 1);
                    o.vx = dir * mv.push;
                    if (o.y > 0) o.vy = Math.Max(o.vy, 2);
                }
            }
            if (knock)
            {
                SetState(o, "knockdown");
                o.vx = dir * (o.ko ? 4.2 : 3.4); o.vy = o.ko ? 6.2 : 5.0; o.y = Math.Max(o.y, 0.01);
                o.chargeFull = false;
                hs = Math.Max(hs, o.ko ? 0.16 : 0.12);
                Emit(new DuelEvent { type = "knockdown", who = o.idx });
            }
            // 角落：对手退无可退时，反推攻击方
            if (!h.arrow && Math.Abs(o.x) >= ARENA - 0.05 && Math.Sign(o.x) == dir) x.vx = -dir * mv.push * 0.7;
            hitstop = Math.Max(hitstop, hs);
            log.Add(new DuelLogEntry { who = x.idx, dmg = dmg, hpA = f[0].hp, hpB = f[1].hp, move = name, guarded = guarded, t = JsRound(time * 100) / 100 });
            Emit(new DuelEvent
            {
                type = "hit", att = x.idx, def = o.idx, dmg = dmg, guarded = guarded, parry = parry, move = name, big = big, counter = counter, breaks = breaks, charge = chg, arrow = h.arrow,
                finisher = finisher, combo = guarded ? 0 : x.combo,
                x = h.hasP ? h.px : x.x + dir * Math.Min(Math.Abs(o.x - x.x) * 0.75, x.reach * 0.8),
                y = h.hasP ? h.py : o.y + (mv.kind == "air" ? 1.1 : 1.25),
            });
            if (o.ko && !hasResult)
            {
                hasResult = true; resultWinner = x.idx; resultKind = "ko";
                endT = 0;
                Emit(new DuelEvent { type = "ko", who = o.idx, winner = x.idx });
            }
        }

        public void TimeUp()
        {
            int winner = f[0].hp >= f[1].hp ? 0 : 1;     // 相同则挑战者胜（同 BattleModel.Duel）
            hasResult = true; resultWinner = winner; resultKind = "time";
            endT = 0;
            Emit(new DuelEvent { type = "timeup", winner = winner });
        }

        // 立即算完（观战跳过 / 无画面模拟）：双方交给电脑，直到分出胜负
        public void RunToEnd(int maxSteps = 60 * 75)
        {
            running = true;
            for (int i = 0; i < maxSteps && !hasResult; i++) Step(DT);
            if (!hasResult) TimeUp();
        }

        public DuelOutcome Outcome(string kind = null)
        {
            int w = hasResult ? resultWinner : (f[0].hp >= f[1].hp ? 0 : 1);
            string k = hasResult ? resultKind : "time";
            return new DuelOutcome { winner = w, kind = kind ?? k, hpA = f[0].hp, hpB = f[1].hp, log = new List<DuelLogEntry>(log), time = time };
        }

        // 不渲染、电脑对电脑快速模拟一整场（平衡测试 / 兜底），返回同 Play 的结果
        public static DuelOutcome Simulate(General genA, General genB, string cultureA = null, string cultureB = null)
        {
            var sim = new DuelSim(genA, genB, DuelLooks.LookOf(genA, cultureA), DuelLooks.LookOf(genB, cultureB));
            sim.f[0].ctrl = new DuelAI(sim.f[0]);
            sim.f[1].ctrl = new DuelAI(sim.f[1]);
            sim.RunToEnd();
            var r = sim.Outcome();
            r.time = JsRound(sim.time * 10) / 10;
            return r;
        }
    }

    // ------------------------------------------------------------ 电脑 --
    // 难度由武力决定：反应时间（延迟感知对手动作）、格挡率、连段、出手积极度、绝技使用
    public sealed class DuelAI : IDuelCtrl
    {
        readonly DuelFighter me;
        readonly double s, react, guardP, comboP, aggr, period, whiffP, antiP, punishP, pokeP, stunGuardP, rk;
        readonly DuelHeld held = new DuelHeld();
        readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }
        double next, guardUntil, chargeTo, backUntil, stunHold, pokeAt, cautAt, parryT = -1, mzAt;
        int planX, seenSeq = -1, comboSeq = -1, stunSeq = -1, punSeq = -1, lastGuarded, patSeq = -1, oSeq = -1, ctrSeq = -1, dashSeq = int.MinValue;
        bool stunGuard, zone, caut, patient, oHit, comboDone;
        double comboRoll;
        string aa;                // null / strike / guard / miss / done
        string oState = "";
        double mash = 0.3;        // 对手「没打中也照样连下去」的倾向（指数平均，0..1）

        static double Rnd() { return DuelSim.Rnd(); }
        static double Clamp(double v, double a, double b) { return DuelSim.Clamp(v, a, b); }

        public DuelAI(DuelFighter me)
        {
            this.me = me;
            // 技巧：武力 20 → 0.4、武力 60 → 0.7、武力 100 → 1
            s = 0.4 + 0.6 * Clamp((me.war - 20) / 80, 0, 1);
            react = 0.44 - 0.29 * s;      // 反应时间：武力 100 → 0.15 秒，武力 20 → 0.44 秒
            guardP = 0.10 + 0.70 * s;     // 看见来招时举防的概率
            comboP = 0.30 + 0.65 * s;     // 命中后接续连段
            aggr = 0.28 + 0.40 * s;       // 进入距离后出手的积极度
            period = 0.30 - 0.16 * s;     // 决策间隔
            whiffP = 0.32 * (1 - s);
            antiP = 0.06 + 0.86 * s;      // 识破跳入
            punishP = 0.12 + 0.78 * s;    // 抓对手收招的破绽
            pokeP = 0.08 + 0.52 * s;      // 对手走进攻击距离时迎击
            stunGuardP = 0.08 + 0.84 * s; // 受击 / 倒地后一恢复就举防
            // 读招：识破连按 / 不看命中的固定连段。武力 40 以下不会，越高越准
            rk = Clamp((me.war - 40) / 40, 0, 0.85);
        }
        // 识破程度 0..1：武力 × 对手连按的证据
        double Read(DuelFighter foe) { return RkVs(foe) * MashN(); }
        double MashN() { return Clamp((mash - 0.5) / 0.3, 0, 1); }
        // 对手武力高出越多，读招越不灵；武力 85 以上的名将连按起来也快而难读
        double RkVs(DuelFighter foe) { return rk * Clamp(1 + (me.war - foe.war) / 30, 0, 1) * (1 - 0.35 * Clamp((foe.war - 85) / 15, 0, 1)); }
        // 识破连按时不在对手的攻击距离里抢攻 → 改为举防，等他打完再还手
        bool HoldOff(DuelSim sim, DuelSim.Snap seen, double rd)
        {
            var foe = sim.Other(me);
            if (rd <= 0 || seen == null || seen.phase == "recovery" || !(seen.state == "walk" || seen.state == "idle" || seen.state == "dash" || DuelSim.IsAttack(seen.state))) return false;
            if (Math.Abs(foe.x - me.x) > foe.reach * DuelSim.Light1.reachK + DuelSim.BODY * 0.5 + 0.25 || Rnd() >= rd * 0.6) return false;
            guardUntil = Math.Max(guardUntil, sim.clock + 0.25 + Rnd() * 0.25); held.guard = true; held.x = 0; planX = 0;
            return true;
        }
        // 观察对手的出招习惯（记忆而非反应：只统计已发生的动作）
        void Observe(DuelSim sim)
        {
            var foe = sim.Other(me);
            if (DuelSim.IsAttack(foe.state) && foe.hitIdx > 0 && (me.state == "hitstun" || me.state == "knockdown")) oHit = true;
            if (foe.seq == oSeq) return;
            string prev = oState;
            if ((prev == "light1" || prev == "light2") && !oHit)
            {
                // 上一段挥空 / 被挡：接着出下一段 = 不看命中的连按；停手 = 有判断
                if (foe.state == DuelSim.Mv(prev).next) mash += (1 - mash) * 0.3; else mash -= mash * 0.12;
            }
            if (foe.state == "light1" && me.state != "hitstun" && Math.Abs(foe.x - me.x) > foe.reach * DuelSim.Light1.reachK + DuelSim.BODY * 0.5 + 0.08) mash += (1 - mash) * 0.12;   // 够不着也出手
            oSeq = foe.seq; oState = foe.state; oHit = false;
        }
        void Press(string k)
        {
            switch (k) { case "light": taps.light++; break; case "up": taps.up++; break; case "special": taps.special++; break; case "heavy": taps.heavy++; break; }
        }
        // 重击：同时按住，蓄力到 to 秒后松开
        void HeavyTo(double to) { Press("heavy"); held.heavy = true; chargeTo = to; }

        public void Update(double dt, DuelSim sim)
        {
            Observe(sim);
            Think(dt, sim);
            // 逼近过滤：向前走将进入对手的攻击距离、而对手正在逼近或出招时，高武力改为举防停步（不硬闯）
            var foe = sim.Other(me); var H = held; var T = taps;
            double dist = Math.Abs(foe.x - me.x), foeR = foe.reach * DuelSim.Light2.reachK + DuelSim.BODY * 0.5;
            if (dist >= foeR + 0.6) zone = false;
            if (H.x == me.face && !H.guard && H.dash == 0 && T.light == 0 && T.heavy == 0 && T.up == 0 && T.special == 0 && sim.Actionable(me) && dist < foeR + 0.4)
            {
                var seen = sim.Seen(foe.idx, react);
                bool pushy = seen != null && seen.state != "guard" && seen.phase != "recovery";
                if (!zone || sim.clock >= cautAt) { zone = true; cautAt = sim.clock + 0.35; caut = Rnd() < 0.1 + 0.75 * s; }
                if (pushy && caut)
                {
                    H.x = 0; planX = 0; H.guard = true;
                    guardUntil = Math.Max(guardUntil, sim.clock + 0.25 + Rnd() * 0.2);
                }
            }
        }

        void Think(double dt, DuelSim sim)
        {
            var foe = sim.Other(me); double now = sim.clock; var H = held;
            H.dash = 0;
            double dist = Math.Abs(foe.x - me.x);
            double reach = me.reach * DuelSim.Light1.reachK + DuelSim.BODY * 0.5 - 0.03;   // 轻击实际判定距离（略保守）
            var seen = sim.Seen(foe.idx, react);

            // 蓄力：到达预定蓄力量后松开
            if (me.state == "charge") { H.heavy = me.t < chargeTo; H.x = 0; H.guard = false; return; }
            H.heavy = false;
            // 受击 / 被挡 / 倒地：高武力会按住格挡，一恢复就举防（并有机会格开弱者的连段）
            if (me.state == "hitstun" || me.state == "guardstun" || me.state == "guardbreak" || me.state == "knockdown" || me.state == "down" || me.state == "getup")
            {
                if (stunSeq != me.seq)
                {
                    stunSeq = me.seq; stunGuard = Rnd() < stunGuardP; stunHold = 0.1 + Rnd() * 0.22;
                    // 格开：看破对手会接下一段（第三段最常见），算准下一击打到的时刻按下格挡；武力越高拿捏越准
                    parryT = -1;
                    var cur = DuelSim.Mv(foe.state);
                    double rk0 = RkVs(foe);
                    if (me.state == "hitstun" && me.Grounded && foe.state == "light2" && rk0 > 0)
                    {
                        double outK = 1 + 2 * Clamp((foe.reach - me.reach) / 0.25, 0, 1);   // 兵器较短、近不了身：更要靠格开
                        if (Rnd() < rk0 * (0.015 + 0.285 * MashN()) * outK)
                        {
                            double T = now + (Math.Max(0, cur.startup + cur.active * 0.6 - foe.t) + DuelSim.Mv(cur.next).startup) / foe.spd;
                            parryT = T - DuelSim.PARRY_WIN * 0.55 + (Rnd() * 2 - 1) * (0.02 + 0.09 * (1 - s));
                        }
                    }
                }
                // 挡下 / 格开对手的连段末段或重击：记下，硬直一结束就反击其收招破绽
                if (me.state == "guardstun" && ctrSeq != foe.seq && (foe.state == "light3" || foe.state == "heavy") && Rnd() < punishP * RkVs(foe) * (0.125 + 0.375 * Read(foe))) ctrSeq = foe.seq;
                if (parryT > 0 && me.state == "hitstun")
                {
                    H.guard = now >= parryT; H.x = 0;
                    if (H.guard) guardUntil = Math.Max(guardUntil, now + stunHold);
                    return;
                }
                H.guard = stunGuard; H.x = 0;
                if (stunGuard) guardUntil = Math.Max(guardUntil, now + stunHold);
                return;
            }
            // 连段：高武力只在命中时继续（命中确认），低武力随机
            if (me.state == "light1" || me.state == "light2")
            {
                if (comboSeq != me.seq) { comboSeq = me.seq; comboRoll = Rnd(); comboDone = false; }   // 每一段各自判定
                var mv = DuelSim.Mv(me.state);
                if (!comboDone && (me.hitIdx > 0 || me.t >= mv.startup + mv.active))     // 命中即确认；挥空则在判定结束时决定
                {
                    bool landed = me.hitIdx > 0 && foe.state == "hitstun";
                    // 对手被打退后还够不够得着下一段（够不着就别接，免得挥空露破绽；武力越高越会算）
                    double away = foe.vx * me.face > 0 ? foe.vx * foe.vx / (2 * DuelSim.FRICTION) : 0;
                    var nx = DuelSim.Mv(mv.next);
                    bool reachOk = dist + away <= me.reach * nx.reachK + DuelSim.BODY * 0.5 + 0.03 + nx.lunge * 0.12;
                    bool go = landed ? comboRoll < comboP && (reachOk || Rnd() > s) : comboRoll < comboP * (1 - s) * 0.6;
                    if (go) Press("light");
                    comboDone = true;
                }
                H.x = 0; H.guard = false;
                return;
            }
            comboDone = false;
            bool free = sim.Actionable(me) || me.state == "guard";
            // 自己的攻击被挡：高武力预料对手会反击，收招后先举防
            if (me.stGuarded != lastGuarded)
            {
                lastGuarded = me.stGuarded;
                var mv = DuelSim.Mv(me.state);
                double left = mv != null ? (mv.startup + mv.active + mv.recovery - me.t) / (me.state == "special" ? 1 : me.spd) : 0;
                if (Rnd() < 0.1 + 0.7 * s) guardUntil = Math.Max(guardUntil, now + left + 0.18 + Rnd() * 0.25);
            }
            // 反击：刚挡下 / 格开的大招还在收招（按招式节奏算好来得及）
            if (ctrSeq >= 0 && free)
            {
                var fm = DuelSim.Mv(foe.state);
                if (ctrSeq == foe.seq && fm != null && foe.Phase == "recovery")
                {
                    double left = (fm.startup + fm.active + fm.recovery - foe.t) / foe.spd;
                    double need = DuelSim.Light1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
                    if (left > need - 0.02 && dist <= reach + 1.1)
                    {
                        ctrSeq = -1; guardUntil = 0; H.guard = false;
                        if (dist <= reach) { Press("light"); H.x = 0; return; }
                        H.dash = me.face; Press("light"); return;
                    }
                }
                if (ctrSeq != foe.seq || fm == null) ctrSeq = -1;
            }
            // 对手被破防：立即追击
            if (foe.state == "guardbreak" && free && foe.t < foe.stun - DuelSim.Light1.startup / me.spd)
            {
                guardUntil = 0; H.guard = false;
                if (dist <= reach) { Press("light"); H.x = 0; return; }
                if (dist <= reach + 1.2 && me.state != "guard") { H.dash = me.face; Press("light"); return; }
                H.x = me.face; return;
            }

            // 对空：看见对手跳向自己（有反应延迟）→ 按武力决定是否识破：迎击下落中的对手，或挡下空中斩再反击落地硬直
            bool foeAir = foe.y > 0.02 || foe.state == "jump" || foe.state == "air";
            if (!foeAir && foe.state != "land") aa = null;
            if (aa == null && foeAir && seen != null && (seen.state == "jump" || seen.state == "air"))
            {
                bool toward = (me.x - foe.x) * foe.vx > 0.2 || dist < 1.4;
                if (toward && dist < foe.reach + 3) aa = Rnd() < antiP ? (Rnd() < 0.5 ? "strike" : "guard") : "miss";
            }
            if ((aa == "strike" || aa == "guard") && free)
            {
                if (foeAir)
                {
                    if (aa == "strike")
                    {
                        double lead = DuelSim.Light1.startup / me.spd + DuelSim.DT;
                        double ty = foe.y + foe.vy * lead - 0.5 * DuelSim.GRAV * lead * lead;
                        double dx = (foe.x + foe.vx * lead - me.x) * me.face;
                        if (foe.vy < 1.5 && ty < 0.8 && ty > -0.2 && dx > -0.1 && dx <= me.reach * DuelSim.Light1.reachK + DuelSim.BODY * 0.5 - 0.05)
                        {
                            Press("light"); aa = "done"; H.guard = false; H.x = 0; return;
                        }
                        if (Math.Abs(foe.x - me.x) < 0.9 && foe.y > 0.7) aa = "guard";   // 太近 / 越过头顶：改为格挡
                        else { H.guard = false; H.x = 0; return; }
                    }
                    H.guard = true; H.x = 0; return;
                }
                if (foe.state == "land")
                {      // 落地硬直：反击
                    if (dist <= reach * 1.05) { Press("light"); aa = "done"; H.guard = false; H.x = 0; return; }
                    if (dist <= reach + 1.2) { H.dash = me.face; Press("light"); aa = "done"; H.guard = false; return; }
                }
            }

            // 正在格挡而对手的连段还在继续（看到对手仍在出招）：不放下防御
            if ((me.state == "guard" || me.state == "guardstun") && seen != null && seen.atk && Math.Abs(seen.x - me.x) <= foe.reach + DuelSim.BODY + 0.6 && Rnd() < 0.3 + 0.7 * s)
                guardUntil = Math.Max(guardUntil, now + 0.12);
            // 看见对手出招：按格挡率决定是否举防
            if (seen != null && seen.atk && seen.seq != seenSeq)
            {
                seenSeq = seen.seq;
                bool threat = Math.Abs(seen.x - me.x) <= foe.reach + DuelSim.BODY + 0.5;
                if (threat && Rnd() < guardP) guardUntil = now + 0.2 + Rnd() * 0.25;
            }
            // 看见对手收招破绽（挥空 / 被挡）：抢攻，距离稍远则冲刺斩。识破连按时早有准备，反应更快
            double rd = Read(foe);
            var seenR = rd > 0 ? sim.Seen(foe.idx, react * (1 - 0.25 * rd)) : seen;
            if (seenR != null && seenR.phase == "recovery" && seenR.state != "air" && seenR.seq != punSeq && free && dist <= reach + 1.1)
            {
                punSeq = seenR.seq;
                // 对手收招还剩多少时间（熟知招式节奏）：来不及冲过去就不冒险
                var fm = DuelSim.Mv(foe.state);
                double left = fm != null && foe.seq == seenR.seq ? (fm.startup + fm.active + fm.recovery - foe.t) / (foe.state == "special" ? 1 : foe.spd) : 0;
                double need = DuelSim.Light1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
                if (left > need - 0.03 && Rnd() < punishP)
                {
                    guardUntil = 0; H.guard = false;
                    if (dist <= reach) { Press("light"); H.x = 0; return; }
                    H.dash = me.face; Press("light"); return;
                }
            }
            // 识破连按：对手一进入其攻击距离就会乱挥 → 预判举防等他打完，或后撤（冲刺）让他挥空，再抓收招
            if (rd > 0 && seen != null && free && now >= mzAt)
            {
                double foeR = foe.reach * DuelSim.Light1.reachK + DuelSim.BODY * 0.5;
                bool busy = seen.phase == "recovery" || seen.state == "hitstun" || seen.state == "guardstun" || seen.state == "guardbreak" ||
                  seen.state == "knockdown" || seen.state == "down" || seen.state == "getup" || seen.state == "land";
                if (!busy && dist < foeR + 0.5)
                {
                    mzAt = now + 0.3 + Rnd() * 0.2;
                    double r0 = Rnd(); bool room = Math.Abs(me.x - me.face * 1.6) < DuelSim.ARENA - 0.3;
                    if (r0 < rd * 0.6 && room && me.state != "guard")
                    {     // 后撤半步：他够不着照样挥，挥空就抓
                        guardUntil = 0; H.guard = false;
                        backUntil = now + 0.14 + Rnd() * 0.14; H.x = -me.face; return;
                    }
                    if (r0 < rd * 0.735) { guardUntil = Math.Max(guardUntil, now + 0.3 + Rnd() * 0.35); }
                }
            }
            // 迎击：按看到的对手位置与速度预判，对手将进入自己的攻击距离时抢先出手（兵器长者占便宜）
            if (seen != null && sim.Actionable(me) && now >= pokeAt && (seen.state == "walk" || seen.state == "dash" || seen.state == "idle"))
            {
                var prev = sim.Seen(foe.idx, react + 0.05);
                double vx = prev != null && seen.clock > prev.clock ? (seen.x - prev.x) / (seen.clock - prev.clock) : 0;
                double lead = DuelSim.Light1.startup / me.spd;
                double pd = Math.Abs(seen.x + vx * (react + lead) - (me.x + me.vx * lead * 0.3));
                if (pd <= reach)
                {
                    pokeAt = now + 0.3;
                    if (Rnd() < pokeP) { if (HoldOff(sim, seen, rd)) return; Press("light"); H.x = 0; planX = 0; return; }
                }
            }
            // 看见对手蓄力：高武力抢攻（打断蓄力）；蓄得浅就举防，蓄得深（将破防）就后撤；低武力傻站
            if (seen != null && seen.state == "charge" && dist < foe.reach * 1.08 + DuelSim.BODY * 0.5 + 0.9 && now >= next)
            {
                next = now + period;
                double r1 = Rnd();
                if (r1 < s * 0.6 && dist <= reach && sim.Actionable(me)) { Press("light"); return; }
                if (r1 < s * 0.9)
                {
                    if (seen.charge > 0.55) { backUntil = now + 0.4; guardUntil = 0; }
                    else guardUntil = Math.Max(guardUntil, now + 0.3);
                }
            }
            // 看见对手冲刺切入：兵器较长者迎头一击（抢先出手）
            if (seen != null && seen.state == "dash" && seen.seq != dashSeq && sim.Actionable(me))
            {
                dashSeq = seen.seq;
                if (dist <= reach + 0.5 && me.reach >= foe.reach && Rnd() < 0.15 + 0.45 * s) { Press("light"); return; }
            }
            bool canGuard = free || me.state == "guardstun";
            if (now < guardUntil && canGuard) { H.guard = true; H.x = 0; return; }
            H.guard = false;
            if (now < backUntil && sim.Actionable(me)) { H.x = -me.face; return; }
            // 空中：接近时出空中斩
            if (me.state == "jump")
            {
                if (!me.airUsed && dist < reach * 0.95 + 0.3 && foe.y < 1.2 && me.vy < 2) Press("light");
                H.x = dist > reach * 0.7 ? me.face : 0;
                return;
            }
            if (!sim.Actionable(me) && me.state != "guard") { H.x = 0; return; }
            // 对手正在出招、自己在其攻击距离边缘：高武力不硬闯，停步等其收招再反击
            if (seen != null && DuelSim.IsAttack(seen.state) && seen.phase != "recovery" && seen.state != "special" && dist < foe.reach + DuelSim.BODY * 0.5 + 0.7)
            {
                if (patSeq != seen.seq) { patSeq = seen.seq; patient = Rnd() < 0.15 + 0.8 * s; }
                if (patient) { planX = 0; H.x = 0; return; }
            }
            if (now < next)
            {
                // 走进攻击距离即停步，避免贴身
                if (planX == me.face && dist <= reach * 0.92) planX = 0;
                H.x = planX;
                return;
            }
            next = now + period * (0.7 + Rnd() * 0.6);
            planX = 0;
            H.x = 0;

            // 对手倒地：保持距离，伺机蓄力
            if (foe.state == "knockdown" || foe.state == "down" || foe.state == "getup")
            {
                if (dist > reach * 1.1) planX = me.face;
                else if (dist < reach * 0.6) planX = -me.face;
                else if (Rnd() < 0.3 * s) HeavyTo(0.3 + Rnd() * 0.5);
                H.x = planX;
                return;
            }
            // 绝技
            if (me.rage >= 100 && (me.bow ? dist > reach * 0.6 : dist <= reach + 1.8) && Rnd() < 0.3 + 0.55 * s) { Press("special"); return; }
            if (dist > reach * 1.02)
            {
                // 高武力：对手兵器够得着时不贸然闯入，在其攻击距离外踱步，等对手先出手露出破绽
                double foeR = foe.reach + DuelSim.BODY * 0.5 + 0.15;
                bool pushy = seen != null && (seen.state == "walk" || seen.state == "dash" || DuelSim.IsAttack(seen.state));
                if (pushy && dist < foeR + 0.5 && Rnd() < 0.1 + 0.65 * s)
                {
                    planX = dist < foeR + 0.12 ? -me.face : 0; H.x = planX;
                    return;
                }
                // 兵器较短：抓对手收招的破绽，或冲刺切入（冲刺中轻击 = 突进斩）
                double gap = foe.reach - me.reach;
                if (gap > 0.03 && dist < reach + 0.85)
                {
                    double k = Math.Min(1, 0.35 + gap / 0.3);
                    bool opening = seen != null && (seen.phase == "recovery" || seen.state == "hitstun" || seen.state == "guardstun");
                    if ((opening && Rnd() < (0.35 + 0.5 * s) * k) || Rnd() < (0.08 + 0.18 * s) * k) { H.dash = me.face; Press("light"); return; }
                }
                if (gap > -0.06 && dist < reach + 0.75 && Rnd() < 0.05 + 0.1 * s) { H.dash = me.face; Press("light"); return; }   // 兵器相当：偶尔冲刺抢攻
                if (dist < reach * 1.45 && Rnd() < whiffP) { Press("light"); return; }   // 冒失出手
                if (dist > 3.4 && Rnd() < 0.22 + 0.2 * s) { H.dash = me.face; return; }
                if (dist > 1.9 && dist < 3.3 && Rnd() < 0.05 + 0.05 * (1 - s)) { Press("up"); H.x = me.face; planX = me.face; return; }
                planX = me.face; H.x = planX;
                return;
            }
            // 贴得太近：长兵器施展不开，拉开距离
            if (dist < reach * 0.55 && Rnd() < 0.35 + 0.4 * s) { planX = -me.face; H.x = planX; next = now + 0.18 + Rnd() * 0.15; return; }
            // 进入攻击距离：高武力会预判举防
            if (dist <= foe.reach + DuelSim.BODY && Rnd() < guardP * 0.45) { guardUntil = now + 0.25 + Rnd() * 0.3; H.guard = true; return; }
            bool foeTurtles = (me.stHits >= 4 && (double)me.stGuarded / me.stHits > 0.45) || (seen != null && seen.state == "guard" && foe.state == "guard");
            if (foeTurtles && Rnd() < 0.25 + 0.4 * s) { HeavyTo(DuelSim.CHARGE_MAX + 0.05); return; }
            double r = Rnd();
            if (r < aggr)
            {
                if (HoldOff(sim, seen, rd)) return;
                if (Rnd() < 0.74) Press("light");
                else HeavyTo(0.12 + Rnd() * (0.25 + 0.45 * s));
            }
            else if (r < aggr + 0.3)
            {
                // 进退试探
                planX = dist < reach * 0.75 || Rnd() < 0.6 ? -me.face : me.face;
                next = now + 0.15 + Rnd() * 0.25;
            }
            else if (r < aggr + 0.42)
            {
                guardUntil = now + 0.2 + Rnd() * 0.3;
            }
            H.x = planX;
        }
    }
}
