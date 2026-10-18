// ==========================================================================
// 三国志-霸王的世界 · 单挑格斗 · 逻辑核心（DESIGN-V2 §4E，第三版格开与手感：DESIGN-V3 §3；网页版 js/duel-game.js 规则与电脑部分的逐行移植）
//
// 纯 C#（不引用 UnityEngine），可在 Tests~/duel 的无头测试中运行：
//   DuelSim        固定 60Hz 步长的格斗逻辑（招式、判定、伤害、格挡 / 格开 / 反击、怒气、绝技与弓将的连珠箭、60 秒计时）
//   DuelAI         电脑（难度由武力决定；第三版 refine：回合感、对手习惯的记忆、场外蓄力 / 佯攻、抢回合与追击、时间压力）
//   DuelScriptBot  测试用的脚本玩家（jumper / masher / spam / guard / parry / heavy，= 网页版 SG.DuelGame.scriptBot）
//   DuelLooks      外观与兵器设定（知名武将 > 文化缺省 > 能力值 + 姓名哈希）
//
// 数值一律用 double，运算顺序与 JS 逐式相同；规则相关的随机数只走 DuelSim.Rnd
// （Unity 中 = UnityEngine.Random.value；无头测试中 = 与 SG.Random 相同的 mulberry32 序列），
// 因此同一种子下与网页版 node tests/duel-bots.js 的结果逐场一致。
//
// 规则（DESIGN-V2 §4E / DESIGN-V3 §3）
//   体力 100；每击伤害 = 基础 × (0.55 + 武力/100) × (1 + (己武力 − 敌武力)/150)（后一项限制在 0.6–1.5，单击 1–40）；
//   高武力出招略快、硬直略短；格挡减伤 80%；怒气随命中 / 受击积累，满时可放绝技；
//   格开（第三版，与按住格挡分开）：轻按 U，有效窗口 0.15–0.22 秒（武力越高越宽）；来招落在窗口内 → 对手被弹开硬直 0.6 秒
//   （重击 0.7），己方得到 0.7 秒反击窗口：此间第一击 ×1.5、不可格挡也不可格开。窗口内没有来招 → 0.4 秒落空破绽
//   （挨打算「破绽」×1.2）。冷却 2.5 秒（从按下算起），明显长于攻击与格挡。可在站立 / 走动 / 格挡 / 挡下一击的硬直中格开；
//   被连段打中时只能看破第三段：挨了第二段后须踩准节奏，按在第三段打到前 0.6 × 窗口之内才格开，按早了白费（「过早」）；
//   没等到第三段则硬直结束时短暂破绽。绝技与箭矢不可格开。按住格挡的「硬挡」（武力远胜者挨连段时有机会挡下后续一击）保留，
//   但不算格开。重击松开后有清晰的起手：轻点约 0.15 秒、满蓄约 0.27 秒（举刀一顿）；绝技起手（定格亮相与突进）无敌。
//   命中顿帧（hitstop）、反击与格开有更长的顿帧。限时 60 秒，KO 或时间到时体力高者胜（相同则挑战者胜，与 BattleModel.Duel 一致）。
//
// 事件（DuelSim.events，画面 / 声音 / HUD 据此表现；type）：
//   jump dash backstep land down knockdown guardbreak special swing arrow arrowEnd chargeFull
//   heavyGo（who、charge、su = 松手到打中的秒数：刃口闪光的起手预兆）
//   parryStart（who、stunned = 受击硬直中按下）parryWhiff（who）parryEarly（who：受击硬直中按早了）
//   parry（att 被格开者、def 格开者、move、route = stance | guard | hitstun、x、y）
//   hit（att、def、dmg、guarded、deflect 硬挡、riposte 反击、move、big、counter 破绽、breaks 破防、charge、arrow、hitH、back、finisher、combo、x、y）
//   ko（who、winner）timeup（winner）
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
        public string hitH = "mid";     // 受击部位（high 头 / mid 躯干），决定受击反应的方向
        public double[] hits;
        public double Total { get { return startup + active + recovery; } }
    }

    public sealed class DuelWeapon
    {
        public string key, name, style;
        public double reach, pow;
        public DuelWeapon(string key, string name, string style, double reach, double pow = 0) { this.key = key; this.name = name; this.style = style; this.reach = reach; this.pow = pow; }
    }

    // 操作输入（玩家键盘 / 触摸、电脑、测试脚本共用）。parry：U / 触摸「格开」（第三版）
    public sealed class DuelHeld { public int x, dash; public bool up, light, heavy, guard, special, parry; }
    public sealed class DuelTaps
    {
        public int light, up, special, heavy, dir, parry;
        public void Press(string k)
        {
            switch (k) { case "light": light++; break; case "up": up++; break; case "special": special++; break; case "heavy": heavy++; break; case "parry": parry++; break; }
        }
        public void Clear() { light = 0; up = 0; special = 0; heavy = 0; dir = 0; parry = 0; }
    }
    public interface IDuelCtrl
    {
        DuelHeld Held { get; }
        DuelTaps Taps { get; }
        void Update(double dt, DuelSim sim);   // 每个逻辑步长之前调用（玩家输入为空实现）
    }

    // 逻辑事件（画面、声音、HUD 据此表现；字段含义见文件头）
    public sealed class DuelEvent
    {
        public string type, move, route, hitH;
        public int who = -1, att = -1, def = -1, dmg, combo, winner = -1, id, n;
        public bool guarded, big, counter, breaks, arrow, finisher, fin, hit, deflect, riposte, back, stunned;
        public bool parry;             // 第二版的字段（受击中格开 / 硬挡）。第三版的格开是独立的 "parry" 事件，hit 事件的硬挡为 deflect；此字段恒为 false
        public double charge, su, x, y;
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

    // 单挑统计（成长系统按单挑表现给经验）：parryBy 按格开途径（stance 站立 / 走动、guard 举防或挡下一击后、hitstun 受击硬直中看破第三段）
    public sealed class DuelStats
    {
        public int swings, hits, guarded, taken, parryTries, parries, parried, counters, whiffs;
        public int byStance, byGuard, byHitstun;
        public DuelStats Copy() { return (DuelStats)MemberwiseClone(); }
        public void AddBy(string route) { if (route == "hitstun") byHitstun++; else if (route == "guard") byGuard++; else byStance++; }
    }

    public sealed class DuelOutcome
    {
        public int winner, hpA, hpB;
        public string kind;
        public double time;
        public List<DuelLogEntry> log;
        public DuelStats[] stats;      // a、b 各一项（第三版）
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
            // 第三版：天竺（坎达剑、长枪、长盾）、阿克苏姆 / 库施（长枪、长皮盾）、毛里（标枪、小圆盾）
            { "india", new DuelCulture { helm = "goldcrown", skin = "#a8724a", hair = "#141012", bareArms = true, gold = true, weapons = new[] { "sword", "spear", "sword" }, shield = "long", beard = "none" } },
            { "africa", new DuelCulture { helm = "kufiya", helmColor = "#ece6d8", skin = "#6c4026", hair = "#0c0a0a", torso = "coat", coat = "#ece6d8", weapons = new[] { "spear", "spear", "sword" }, shield = "hide", beard = "short" } },
            { "moor", new DuelCulture { helm = "kufiya", helmColor = "#e8dcc0", skin = "#b07e54", hair = "#141012", torso = "coat", coat = "#e0d4bc", weapons = new[] { "spear", "sword" }, shield = "round", beard = "short" } },
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
            if (cul == null || !Cultures.ContainsKey(cul)) { string cb = Defs.CultureBase(cul); cul = Cultures.ContainsKey(cb) ? cb : "han"; }   // 未知文化按 Defs.CultureBase 的回退表
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
        public double whiffT;       // 格开落空的破绽时长（0 = 未设）
        public int swungSeq = -1;
        public IDuelCtrl ctrl;
        // 上一步的按住状态
        public int pX; public bool pUp, pLight, pHeavy, pSpecial, pGuard, pParry;
        // 格开（第三版）
        public readonly double parryW;      // 有效窗口（秒）
        public double parryReadyAt;         // 冷却结束的时刻（sim.clock）
        public double counterUntil;         // 反击窗口截止（sim.clock）：此间第一击 ×1.5、不可挡
        public double hsParryAt = -9;       // 受击硬直中按下格开的时刻（只对连段第三段有效）
        public int hsParrySeq = -1;
        public string hitH = "mid";         // 最近一次受击的部位与方向（受击动画用）
        public bool hitBack;
        public string parryFrom = "";       // 进入格开架势前的状态（统计格开途径）
        public double bufLight = -9, bufJump = -9, bufSpecial = -9, bufHeavy = -9, bufDash = -9, bufParry = -9;
        public int lastTapDir; public double lastTapT = -9;
        public readonly DuelStats stats = new DuelStats();
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
            parryW = DuelSim.ParryWindow(war);
            bow = look.bow;
        }
        public bool Grounded { get { return y <= 0 && vy <= 0; } }
        // 出招阶段："startup" | "active" | "recovery" | null（重击的起手随蓄力）
        public string Phase
        {
            get
            {
                var mv = DuelSim.Mv(state);
                if (mv == null) return null;
                double su = state == "heavy" ? DuelSim.HeavySU(charge, spd) : mv.startup;
                if (t < su) return "startup";
                if (t < su + mv.active) return "active";
                return "recovery";
            }
        }
        // 第二版的统计字段（只读别名）
        public int stSwings { get { return stats.swings; } }
        public int stHits { get { return stats.hits; } }
        public int stGuarded { get { return stats.guarded; } }
        public int stTaken { get { return stats.taken; } }
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
        // 格开（DESIGN-V3 §3）：轻按 U（触摸「格开」），有效窗口 0.15–0.22 秒（武力越高越宽）。来招落在窗口内 → 对手被弹开硬直，
        // 己方得到反击窗口：此间第一击 ×1.5、不可格挡也不可格开。窗口内没有来招 → 落空破绽。冷却明显长于攻击与格挡。
        public const double PARRY_CD = 2.5;            // 格开冷却（从按下算起）
        public const double PARRY_WHIFF = 0.4;         // 落空破绽
        public const double PARRY_STAGGER = 0.6;       // 被格开者的硬直（重击 +0.1）
        public const double PARRY_RIPOSTE = 0.16;      // 格开成功后的弹刀姿势（可随时出招取消）
        public const double COUNTER_WIN = 0.7;         // 反击窗口
        public const double COUNTER_K = 1.5;           // 反击第一击的伤害倍率
        // 受击硬直中预先按下的格开（看破连段第三段）：只认第三段打到前 0.6 × 窗口内的按键（约 0.09–0.13 秒）——
        // 须踩准第三段的节奏；按早了白费（照样挨打，冷却照算）
        public const double HS_PARRY_K = 0.6;
        // 重击松手后的起手随蓄力变长：0.13 秒（基准，按出招速度缩放）+ 举刀亮刃的一顿 0.15 × 蓄力（实际秒数，不随出招速度缩短）
        // → 轻点 ≈ 0.12–0.15 秒（看不及，只能预判）、满蓄 ≈ 0.25–0.28 秒（人的反应来得及，满蓄破防的重击可以看准当场格开）
        public const double HEAVY_HOLD = 0.15;
        public const double HEAVY_PARRY_CAP = 0.45;    // 电脑看准格开满蓄重击的概率上限（武力 100 时）
        public const double BACKSTEP_T = 0.26;         // 后撤步（向后双击）

        // 规则随机数（[0, 1)）。Unity：UnityEngine.Random.value；测试：mulberry32
        static readonly Random sysRnd = new Random();
        public static Func<double> Rnd = () => sysRnd.NextDouble();

        public static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        public static double Clamp01(double v) { return Clamp(v, 0, 1); }
        public static double JsRound(double v) { return Math.Floor(v + 0.5); }

        // 格开的有效窗口：武力 30 → 0.15 秒，100 → 0.22 秒
        public static double ParryWindow(double war) { return 0.15 + 0.07 * Clamp((war - 30) / 70, 0, 1); }
        // 重击起手（招式时钟，即 × 出招速度 spd 后的基准秒）
        public static double HeavySU(double charge, double spd) { return 0.13 + HEAVY_HOLD * Clamp(double.IsNaN(charge) ? 0 : charge, 0, 1) * (spd != 0 && !double.IsNaN(spd) ? spd : 1); }
        // 当前招式的起手时长（重击随蓄力）
        public static double SuOf(DuelFighter f) { return f.state == "heavy" ? HeavySU(f.charge, f.spd) : Mv(f.state).startup; }
        // 当前招式的总时长（基准秒）
        public static double TotalOf(DuelFighter f) { var mv = Mv(f.state); return SuOf(f) + mv.active + mv.recovery; }

        // 招式时序（基准秒；出招时钟按 spd 缩放）。reachK × 兵器长度 = 判定距离；hitH：受击部位（high 头 / mid 躯干）
        public static readonly DuelMove Light1 = new DuelMove { name = "light1", kind = "light", startup = 0.10, active = 0.07, recovery = 0.19, dmg = 2.4, stun = 0.30, push = 2.6, reachK = 0.92, next = "light2", hitstop = 0.065, hmax = 0.85, hitH = "mid" };
        public static readonly DuelMove Light2 = new DuelMove { name = "light2", kind = "light", startup = 0.09, active = 0.07, recovery = 0.21, dmg = 2.4, stun = 0.32, push = 2.9, reachK = 0.95, next = "light3", hitstop = 0.07, hmax = 0.85, hitH = "high" };
        public static readonly DuelMove Light3 = new DuelMove { name = "light3", kind = "light", startup = 0.15, active = 0.09, recovery = 0.44, dmg = 4, stun = 0.46, push = 5.0, reachK = 1.0, hitstop = 0.10, hmax = 1.45, big = true, lunge = 1.8, hitH = "high" };
        // 重击：松开后有一段清晰的起手（刃口闪光预兆），可被看准格开
        public static readonly DuelMove Heavy = new DuelMove { name = "heavy", kind = "heavy", startup = 0.13, active = 0.10, recovery = 0.40, dmg = 4.8, dmgCharge = 5, stun = 0.5, push = 4.6, reachK = 1.08, hitstop = 0.12, hmax = 1.5, big = true, lunge = 2.2, hitH = "mid" };
        public static readonly DuelMove Air = new DuelMove { name = "air", kind = "air", startup = 0.05, active = 0.20, recovery = 0.10, dmg = 3, stun = 0.36, push = 3.0, reachK = 0.88, hitstop = 0.08, hmax = 9, hitH = "high" };
        // 绝技：定格起手 → 突进 → 三段斩 + 终结一击（倒地）
        public static readonly DuelMove Special = new DuelMove { name = "special", kind = "special", startup = SPECIAL_FREEZE + 0.1, active = 0.62, recovery = 0.45, dmg = 4.5, finisher = 7, stun = 0.5, push = 1.6, reachK = 1.0, hitstop = 0.09, hmax = 9, big = true, hitH = "mid", hits = new[] { 0.0, 0.17, 0.34, 0.56 } };

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
        public static bool IsAttack(string s) { return Mv(s) != null; }   // = 网页版 ATTACKS
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
        // 供 AI 延迟感知的历史快照（环形缓冲，最多 90 条；快照对象循环复用，不要跨步保存）
        public sealed class Snap { public double clock, x, y, t, charge; public string state, phase; public int seq; public bool atk; }
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
        public bool ParryReady(DuelFighter x) { return clock >= x.parryReadyAt; }
        public double ParryCd(DuelFighter x) { return Math.Max(0, x.parryReadyAt - clock); }   // 剩余冷却（秒）
        public bool Countering(DuelFighter x) { return x.counterUntil > clock; }
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
                s.clock = clock; s.state = x.state; s.x = x.x; s.y = x.y; s.seq = x.seq; s.t = x.t; s.phase = ph; s.atk = mv != null && ph != "recovery"; s.charge = x.charge;
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
            if ((h.parry && !x.pParry) || (taps != null && taps.parry > 0))
            {
                x.bufParry = now;
                // 受击硬直中按下格开：看破连段——只对随后的第三段有效（每段硬直只认第一次按下，同样进入冷却）
                if (x.state == "hitstun" && x.hsParrySeq != x.seq && ParryReady(x) && x.Grounded)
                {
                    x.hsParrySeq = x.seq; x.hsParryAt = now; x.parryReadyAt = now + PARRY_CD; x.stats.parryTries++; x.bufParry = -9;
                    Emit(new DuelEvent { type = "parryStart", who = x.idx, stunned = true });
                }
            }
            bool xNew = h.x != 0 && h.x != x.pX;
            if (xNew || (taps != null && taps.dir != 0))
            {
                int dir = xNew ? h.x : taps.dir;
                if (x.lastTapDir == dir && now - x.lastTapT < 0.28) { x.bufDash = now; x.dashDir = dir; x.lastTapDir = 0; x.lastTapT = -9; }
                else { x.lastTapDir = dir; x.lastTapT = now; }
            }
            if (h.dash != 0) { x.bufDash = now; x.dashDir = h.dash; }
            if (taps != null) taps.Clear();
            x.holdGuard = h.guard;
            x.pX = h.x; x.pUp = h.up; x.pLight = h.light; x.pHeavy = h.heavy; x.pSpecial = h.special; x.pGuard = h.guard; x.pParry = h.parry;
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
                        if (TryParry(x)) break;
                        if (Fresh(x.bufSpecial) && x.rage >= 100) { x.bufSpecial = -9; StartSpecial(x, o); break; }
                        if (Fresh(x.bufDash) && x.dashDir != 0) { x.bufDash = -9; StartDash(x); break; }
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
                    if (TryParry(x)) break;
                    if (Fresh(x.bufDash) && x.dashDir != 0 && !h.guard) { x.bufDash = -9; StartDash(x); break; }
                    if (Fresh(x.bufLight)) { x.bufLight = -9; StartAttack(x, "light1"); break; }
                    if (!h.guard) SetState(x, "idle");
                    break;
                case "guardstun":
                    // 挡下一击后可立即格开（看准连段的下一击）：举防 → 格开 → 反击
                    if (TryParry(x)) break;
                    if (x.t >= x.stun) SetState(x, h.guard ? "guard" : "idle");
                    break;
                case "guardbreak":
                    if (x.t >= x.stun) SetState(x, "idle");
                    break;
                case "parry":
                    // 格开架势：窗口内来招即弹开（在 Hit 里结算）；窗口过后露出破绽
                    x.vx *= 0.7;
                    if (x.t >= x.parryW) { SetState(x, "parrywhiff"); x.whiffT = PARRY_WHIFF; x.stats.whiffs++; Emit(new DuelEvent { type = "parryWhiff", who = x.idx }); }
                    break;
                case "parrywhiff":
                    if (x.t >= (x.whiffT != 0 ? x.whiffT : PARRY_WHIFF)) SetState(x, h.guard ? "guard" : "idle");
                    break;
                case "parryok":
                    // 弹刀姿势：随时可出招（反击）、跳、冲刺
                    x.vx = 0;
                    if (Fresh(x.bufSpecial) && x.rage >= 100) { x.bufSpecial = -9; StartSpecial(x, o); break; }
                    if (Fresh(x.bufLight)) { x.bufLight = -9; StartAttack(x, "light1"); break; }
                    if (Fresh(x.bufHeavy)) { x.bufHeavy = -9; SetState(x, "charge"); x.charge = 0; x.tapHeavy = !h.heavy; break; }
                    if (Fresh(x.bufDash) && x.dashDir != 0) { x.bufDash = -9; StartDash(x); break; }
                    if (x.t >= PARRY_RIPOSTE) SetState(x, h.guard ? "guard" : "idle");
                    break;
                case "parried":
                    // 被格开：兵器被弹开、身形不稳
                    if (x.t >= x.stun) SetState(x, h.guard && x.Grounded ? "guard" : "idle");
                    break;
                case "backstep":
                    // 后撤步：快速后跃拉开距离，后半段可接轻击（迎击追来的对手）
                    x.vx = x.dashDir * 6.8 * Math.Max(0.2, 1 - x.t / BACKSTEP_T);
                    if (x.t > 0.15 && Fresh(x.bufLight)) { x.bufLight = -9; StartAttack(x, "light1"); break; }
                    if (x.t >= BACKSTEP_T) SetState(x, h.guard ? "guard" : "idle");
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
                        Emit(new DuelEvent { type = "heavyGo", who = x.idx, charge = c, su = HeavySU(c, x.spd) / x.spd });   // 起手预兆：刃口闪光（su：到打中还有几秒）
                    }
                    break;
                case "light1":
                case "light2":
                case "light3":
                case "heavy":
                    {
                        if (x.state != "light3" && x.state != "heavy" && Fresh(x.bufLight) && x.t > 0.03) { x.bufLight = -9; x.chain = true; }
                        double su = SuOf(x), total = su + mv.active + mv.recovery;
                        // 突进：起手最后 0.4 段才开始（满蓄重击举刀那一顿原地不动，突进距离不变）
                        if (mv.lunge != 0 && x.t >= su - mv.startup * 0.4 && x.t < su + mv.active) x.vx = x.face * mv.lunge;
                        if (x.chain && mv.next != null && x.t >= su + mv.active * 0.6) { StartAttack(x, mv.next); break; }
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
                    // 硬直结束时若按住格挡，直接进入格挡（无空隙）；硬直中按了格开却没等到第三段 → 收势不及，短暂破绽
                    if (x.t >= x.stun)
                    {
                        bool wasted = x.hsParrySeq == x.seq;
                        SetState(x, h.guard && x.Grounded ? "guard" : "idle"); x.combo = 0; o.combo = 0;
                        if (wasted && x.Grounded) { SetState(x, "parrywhiff"); x.whiffT = PARRY_WHIFF * 0.6; x.stats.whiffs++; Emit(new DuelEvent { type = "parryWhiff", who = x.idx }); }
                        x.hsParrySeq = -1;
                    }
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

        // 格开：按下（缓冲内）且冷却已好 → 进入格开架势
        bool TryParry(DuelFighter x)
        {
            if (!Fresh(x.bufParry) || !ParryReady(x) || !x.Grounded) return false;
            x.bufParry = -9;
            x.parryFrom = x.state;
            SetState(x, "parry");
            x.parryReadyAt = clock + PARRY_CD;
            x.stats.parryTries++;
            x.chargeFull = false;
            Emit(new DuelEvent { type = "parryStart", who = x.idx });
            return true;
        }
        // 双击方向：朝对手为冲刺，背向对手为后撤步
        void StartDash(DuelFighter x)
        {
            SetState(x, x.dashDir == -x.face ? "backstep" : "dash");
            Emit(new DuelEvent { type = x.state == "backstep" ? "backstep" : "dash", who = x.idx });
        }

        void StartAttack(DuelFighter x, string name)
        {
            SetState(x, name);
            x.stats.swings++;
            if (name != "air") x.vx *= 0.3;
        }

        void StartSpecial(DuelFighter x, DuelFighter o)
        {
            SetState(x, "special");
            x.rage = 0; x.vx = 0;
            x.stats.swings++;
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
                if (x.state != "walk" && x.state != "dash" && x.state != "backstep" && !(x.state == "special" && x.vx != 0) && !(m != null && m.lunge != 0 && x.t < SuOf(x) + m.active))
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
        // 无敌：倒地 / 起身 / 绝技起手（定格亮相与突进期间不会被对手解冻后的出招打断）
        bool Immune(DuelFighter o) { return o.invuln > 0 || o.state == "knockdown" || o.state == "down" || o.state == "getup" || o.ko || (o.state == "special" && o.t < Special.startup); }

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

        // 这一击是否被格开：格开架势的窗口内（绝技与箭矢不可格开；反击的第一击不可格开）
        bool Parries(DuelFighter x, DuelFighter o, HitInfo h)
        {
            if (h.name == "special" || h.arrow || o.hp <= 0 || Countering(x)) return false;
            if (o.state == "parry") return o.t <= o.parryW + 1e-6;
            // 受击硬直中预先按下的格开：只对连段第三段有效，且须按在第三段打到前 HS_PARRY_K × 窗口之内（踩准节奏）
            return HsArmed(x, o, h) && clock - o.hsParryAt <= o.parryW * HS_PARRY_K + 1e-6;
        }
        bool HsArmed(DuelFighter x, DuelFighter o, HitInfo h) { return h.name == "light3" && o.state == "hitstun" && o.Grounded && o.hsParrySeq == o.seq; }
        // 格开成功：攻方兵器被弹开、硬直；守方弹刀，获得反击窗口
        void ParrySuccess(DuelFighter x, DuelFighter o, HitInfo h)
        {
            int dir = x.face;
            // 格开的途径：stance 站立 / 走动中按下 · guard 举防或挡下一击后按下 · hitstun 受击硬直中看破第三段
            string route = o.state == "hitstun" ? "hitstun" : o.parryFrom == "guard" || o.parryFrom == "guardstun" ? "guard" : "stance";
            o.stats.AddBy(route);
            SetState(x, "parried");
            x.stun = PARRY_STAGGER + (h.name == "heavy" ? 0.1 : 0);
            x.vx = -dir * 1.8; x.charge = 0; x.chargeFull = false;
            if (x.y > 0) x.vy = Math.Min(x.vy, 0);
            SetState(o, "parryok");
            o.vx = 0; o.hsParrySeq = -1;
            o.counterUntil = clock + COUNTER_WIN;
            o.rage = Math.Min(100, o.rage + 8); x.rage = Math.Min(100, x.rage + 2);
            o.stats.parries++; x.stats.parried++;
            x.combo = 0; o.combo = 0;
            hitstop = Math.Max(hitstop, 0.13);
            double gap = Math.Abs(o.x - x.x);
            Emit(new DuelEvent { type = "parry", att = x.idx, def = o.idx, move = h.name, route = route, x = o.x - dir * Math.Min(gap * 0.45, 0.75), y = o.y + (h.name == "air" ? 1.5 : 1.3) });
        }

        void Hit(DuelFighter x, DuelFighter o, HitInfo h)
        {
            var mv = h.mv; string name = h.name; bool finisher = h.fin; double chg = h.charge;
            if (Parries(x, o, h)) { ParrySuccess(x, o, h); return; }
            // 受击硬直中按早了：第三段照样打中（提示「过早」，计一次落空）
            if (HsArmed(x, o, h)) { o.hsParrySeq = -1; o.stats.whiffs++; Emit(new DuelEvent { type = "parryEarly", who = o.idx }); }
            double bs = mv.dmg * (x.wpn.pow != 0 ? x.wpn.pow : 1);
            if (name == "heavy") bs += mv.dmgCharge * chg;
            if (name == "special" && finisher) bs = mv.finisher;
            if (h.arrow) bs *= 0.8;                     // 箭矢可远距离命中，威力略低
            // 反击：格开成功后的第一击 ×1.5、不可格挡（用掉即止）
            bool riposte = Countering(x) && !h.arrow;
            if (riposte) { x.counterUntil = 0; x.stats.counters++; bs *= COUNTER_K; }
            // 破绽：打在对手出招起手 / 蓄力 / 格开落空上
            bool counter = !riposte && (o.state == "charge" || o.state == "parrywhiff" || (Mv(o.state) != null && o.Phase == "startup" && o.state != "special"));
            if (counter) bs *= 1.2;
            double dmgF = Damage(bs, x.war, o.war) * (0.92 + Rnd() * (1.08 - 0.92));
            bool guarded = !riposte && (o.state == "guard" || o.state == "guardstun") && o.Grounded;
            // 硬挡：被连段打中后按住格挡，武力远高于攻方时有机会挡下后续一击（武力差 30 → 18%，70 → 50%），挡下后立刻能还手
            bool deflect = false;
            if (!guarded && !riposte && o.state == "hitstun" && o.holdGuard && o.Grounded && o.hp > 0 && name != "special")
            {
                double p = Clamp((o.war - x.war - 12) / 100, 0, 0.5);
                if (p > 0 && Rnd() < p) { guarded = true; deflect = true; }
            }
            bool charged = name == "heavy" && chg >= 0.85;
            bool breaks = guarded && (charged || (name == "special" && finisher));
            if (guarded) dmgF *= 0.2;
            int dmg = (int)Clamp(JsRound(dmgF), guarded ? 0 : 1, 40);
            o.hp = Math.Max(0, o.hp - dmg);
            int dir = x.face;
            bool big = mv.big || charged || riposte;
            double hs = mv.hitstop * (guarded ? 0.6 : 1) * (charged ? 1.3 : 1);
            if (riposte) hs = Math.Max(hs * 1.4, 0.15);
            // 受击部位与方向（受击动画）：从背后挨打则向前扑
            o.hitH = mv.hitH ?? "mid"; o.hitBack = o.face == dir;
            x.stats.hits++;
            if (name == "air") x.airHit = true;
            if (guarded) x.stats.guarded++;
            o.stats.taken++;

            bool knock = false;
            if (o.hp <= 0)
            {
                knock = true; o.ko = true;
            }
            else if (guarded)
            {
                if (breaks) { SetState(o, "guardbreak"); o.stun = 0.8 * o.stunK; Emit(new DuelEvent { type = "guardbreak", who = o.idx }); }
                else { SetState(o, "guardstun"); o.stun = deflect ? 0.1 : (0.12 + mv.stun * 0.35) * o.stunK; }
                o.vx = dir * mv.push * (deflect ? 0.15 : mv.big ? 0.5 : 0.85);     // 大招被挡时推开得少：收招破绽可被反击；硬挡几乎不退、立刻能还手
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
                    o.stun = mv.stun * o.stunK * (name == "heavy" ? 1 + chg * 0.3 : 1) * (riposte ? 1.25 : 1);
                    o.vx = dir * mv.push * (riposte ? 1.15 : 1);
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
                type = "hit", att = x.idx, def = o.idx, dmg = dmg, guarded = guarded, deflect = deflect, riposte = riposte, move = name, big = big, counter = counter, breaks = breaks, charge = chg, arrow = h.arrow,
                hitH = o.hitH, back = o.hitBack, finisher = finisher, combo = guarded ? 0 : x.combo,
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
            return new DuelOutcome { winner = w, kind = kind ?? k, hpA = f[0].hp, hpB = f[1].hp, log = new List<DuelLogEntry>(log), time = time, stats = new[] { f[0].stats.Copy(), f[1].stats.Copy() } };
        }

        // 不渲染、电脑对电脑快速模拟一整场（平衡测试 / 跳过），返回同 Play 的结果
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
    // 难度由武力决定：反应时间（延迟感知对手动作）、格挡率、连段、出手积极度、绝技使用。
    // 第三版 refine（电脑像个像样的格斗对手）：挡下就轮到自己（抢回合，按对手的习惯改为格开 / 慢半拍 / 冲刺斩追击）、
    // 不往对手的出手节奏里撞、看准大招收招反击、对缩着举防的对手在他够不着处蓄满重击破防（他惯于格开时佯攻骗格开、
    // 趁他格开冷却再真打）、算准起身无敌结束的那一刻打到、时间紧迫时沉不住气；电脑对电脑时双方都按「急」打。
    public sealed class DuelAI : IDuelCtrl
    {
        readonly DuelFighter me;
        public readonly double s, gs, react, guardP, comboP, aggr, period, whiffP, antiP, punishP, pokeP, stunGuardP, parryP, parryErr, rk;
        readonly DuelHeld held = new DuelHeld();
        readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }

        // 格开
        public double parryAt = -1;
        public int hvSeq = -1;          // 看清的那一记重击（测试 (6) 据此计分母）
        int pwSeq = -1, stanceSeq = -1;
        bool stanceHold;
        double next, guardUntil, chargeTo, backUntil, stunHold, pokeAt, cautAt, mzAt;
        int planX, seenSeq = -1, comboSeq = -1, stunSeq = -1, punSeq = -1, lastGuarded, patSeq = -1, oSeq = -1, ctrSeq = -1, dashSeq = -1;
        bool stunGuard, zone, caut, patient, oHit, comboDone;
        double comboRoll;
        string aa;                      // null / strike / guard / miss / done
        string oState = "";
        // 读招：识破连按 / 不看命中的固定连段
        double mash = 0.3;              // 对手「没打中也照样连下去」的倾向（指数平均，0..1）
        double p3; int hsSeen = -1;     // 对手「挨了第二段就格开第三段」的倾向（指数平均，0..1）
        double baitUntil;               // 故意只打两段诱对手格开落空：此前料定，无需反应即可抓破绽
        // 回合感与对手的试探习惯（记忆而非反应：只统计已发生的动作，慢慢淡忘；武力越高越会用）
        double pkN = 0.5, pkT = 1;      // 自己站在对手轻击距离内、对手可出手时，对手主动起手的次数 / 时长 → 试探频率（次/秒）
        double repoke = 0.3;            // 对手的出手被挡后立刻再出手（抢回合）的倾向
        double turtle = 0.2;            // 近身时对手举防不动的时间比例
        double hvBad;                   // 自己的满蓄重击被格开 / 蓄力中被打的记忆（越多越少用）
        sealed class Blk { public int seq; public string move; }
        Blk blk;                        // 刚挡下的一击：防御硬直一结束就轮到自己
        double rpWatch;                 // 挡下后观察对手是否立刻再出手（截止时刻）
        double ttAt = -1;               // 抢回合的出手时刻（略有迟疑，武力越低越慢）
        double ttBad;                   // 抢回合的那一刀被挡 / 被格开 / 被抢先的记忆：越多越常改为慢半拍或不抢
        sealed class TtMine { public double t, end; public int hits, guarded, parried, taken, seq; public bool back, dash; }
        TtMine ttMine;
        string chgFar;                  // 场外蓄力：null / "approach" / "shell" / "oki"（在对手够不着处蓄满，待其走进重击距离再松手）
        double chgAt;
        int spSeq = -1; bool spGuard;
        int hitN; double quietAt;       // 最近一次交手（打中 / 被挡 / 挨打）的时刻：相持久了会按捺不住
        bool ttLate, ttDash, chaseFar;
        double hvWait, stopAt;
        string lastAtk = "";
        // 对手一见自己松开蓄力重击（六成以上）就按格开的倾向（记忆：每次松手看他按没按，慢慢淡忘）
        double hvPar = 0.15; int hvRel = -1;
        sealed class HvWatch { public double until; public int tries; }
        HvWatch hvWatch;
        bool fnt; double fntHold;       // 佯攻：蓄满了照样松手（够不着、挥空），骗他格开落空
        // 抢回合那一刀被他退开让空的记忆：越多越常改为冲刺斩追击（再追不上就少抢）
        double ttBack; bool ttChase; double chaseUntil; int kdSeq = -1; double okiPick = 1;

        static double Rnd() { return DuelSim.Rnd(); }
        static double Clamp(double v, double a, double b) { return DuelSim.Clamp(v, a, b); }
        const double BODY = DuelSim.BODY, CHARGE_MAX = DuelSim.CHARGE_MAX, DT = DuelSim.DT;
        static readonly DuelMove L1 = DuelSim.Light1, MvHeavy = DuelSim.Heavy;

        public DuelAI(DuelFighter me)
        {
            this.me = me;
            // 技巧：武力 20 → 0.4、武力 60 → 0.7、武力 100 → 1（下限较高：普通武将也会防守反击，强将近乎无隙可乘）
            s = 0.4 + 0.6 * Clamp((me.war - 20) / 80, 0, 1);
            // 临场判断（抢回合、不撞对手的出手节奏、场外蓄力、看绝技举防）：武力 25 以下 0.2、50 → 0.47、76 → 0.75，90 以上封顶 0.9
            gs = 0.2 + 0.7 * Clamp((me.war - 25) / 65, 0, 1);
            react = 0.44 - 0.29 * s;      // 反应时间：武力 100 → 0.15 秒，武力 20 → 0.44 秒
            guardP = 0.10 + 0.70 * s;     // 看见来招时举防的概率
            comboP = 0.30 + 0.65 * s;     // 命中后接续连段
            aggr = 0.28 + 0.40 * s;       // 进入距离后出手的积极度
            period = 0.30 - 0.16 * s;     // 决策间隔
            whiffP = 0.32 * (1 - s);
            antiP = 0.06 + 0.86 * s;      // 识破跳入（对空 / 挡下后反击落地硬直）
            punishP = 0.12 + 0.78 * s;    // 抓对手收招的破绽
            pokeP = 0.08 + 0.52 * s;      // 对手走进攻击距离时迎击
            stunGuardP = 0.08 + 0.84 * s; // 受击 / 倒地后一恢复就举防
            // 格开：武力 35 以下几乎不用；越高越常用、拿捏越准。按节奏预约的格开（连段的下一击），按下时刻的误差为三角分布
            // ±(0.18 + 0.25·(1−s)) 秒；受击硬直中看破第三段只认最后 0.6 × 窗口，但连段节奏固定、误差 × 0.65。
            // 重击则是反应：与看见其他来招同样的反应时间（react），看清即按——只有满蓄重击来得及
            parryP = 0.6 * Clamp((me.war - 35) / 65, 0, 1);
            parryErr = 0.18 + 0.25 * (1 - s);
            // 读招：识破连按 / 不看命中的固定连段。武力 40 以下不会，越高越准（武力 40 → 0、60 → 0.5、74 以上 → 0.85）
            rk = Clamp((me.war - 40) / 40, 0, 0.85);
        }
        // 他的格开还要多久才好：看得见他按格开（架势 / 冷却圈），有反应延迟——刚按下、还没看清的不算
        double FoeCd(DuelSim sim)
        {
            var fo = sim.Other(me);
            return fo.parryReadyAt - DuelSim.PARRY_CD <= sim.clock - react ? Math.Max(0, fo.parryReadyAt - sim.clock) : 0;
        }
        // 时间紧迫 0..1：剩下 45 秒起越来越沉不住气（剩 15 秒时最急）；落后（同分算守擂一方落后：超时判挑战者胜）更急，领先则耗得起。
        // 电脑对电脑（无画面模拟 / 观战）：两边都等对方先出手只会耗到时间——双方都按「急」打
        double Urg(DuelSim sim)
        {
            var foe = sim.Other(me);
            bool behind = me.hp < foe.hp || (me.hp == foe.hp && me.idx == 1);
            double u = Clamp((45 - (sim.limit - sim.time)) / 30, 0, 1) * (behind ? 1 : 0.35);
            return foe.ctrl is DuelAI ? Math.Max(u, 0.8) : u;
        }
        // 倒地 / 起身中的对手还要多久才打得到（倒地 0.62 秒 → 起身 0.42 秒 → 无敌 0.2 秒；招式节奏是看得出来的）
        static double HittableIn(DuelFighter foe)
        {
            if (foe.state == "knockdown") return 9;
            if (foe.state == "down") return Math.Max(0, 0.62 - foe.t) + 0.42 + 0.2;
            if (foe.state == "getup") return Math.Max(0, 0.42 - foe.t) + 0.2;
            return Math.Max(0, foe.invuln);
        }
        // 对手的试探频率（次/秒）与「此刻抢攻会撞上他出手」的风险 0..1
        double PokeRate() { return pkN / pkT; }
        double PokeRisk() { return 1 - Math.Exp(-PokeRate() * 0.2); }
        // 预约一次格开：瞄准有效区间的正中（站立格开 = 来招前半个窗口；受击硬直中 = 前 0.3 个窗口）按下，误差随武力；
        // 冷却赶不上就不预约。误差算出来早于此刻 = 本该更早按 → 只能此刻按（多半仍落在窗口里，仅限站立格开）
        bool PlanParry(DuelSim sim, double T, double k = 0.5, double ek = 1)
        {
            double now = sim.clock;
            double at = Math.Max(now, T - me.parryW * k + (Rnd() + Rnd() - 1) * parryErr * ek);
            if (sim.ParryCd(me) > at - now) return false;
            parryAt = at;
            return true;
        }
        // 对手正在出的连段若接下一段，下一击打到的时刻
        static double NextChainHit(DuelSim sim, DuelFighter foe)
        {
            var cur = DuelSim.Mv(foe.state);
            return sim.clock + (Math.Max(0, cur.startup + cur.active * 0.6 - foe.t) + DuelSim.Mv(cur.next).startup) / foe.spd;
        }
        // 识破程度 0..1：武力 × 对手连按的证据
        double Read(DuelFighter foe) { return RkVs(foe) * MashN(); }
        double MashN() { return Clamp((mash - 0.5) / 0.3, 0, 1); }   // 连按证据：0.5 以下视为正常打法
        // 对手武力高出越多（出招越快越难看清），读招越不灵；武力 85 以上的名将连按起来也快而难读
        double RkVs(DuelFighter foe) { return rk * Clamp(1 + (me.war - foe.war) / 30, 0, 1) * (1 - 0.35 * Clamp((foe.war - 85) / 15, 0, 1)); }
        // 识破连按时不在对手的攻击距离里抢攻（对手一进距离就会挥刀，抢攻只会被打断）→ 改为举防，等他打完再还手
        bool HoldOff(DuelSim sim, DuelSim.Snap seen, double rd)
        {
            var foe = sim.Other(me);
            if (rd <= 0 || seen == null || seen.phase == "recovery" || !(seen.state == "walk" || seen.state == "idle" || seen.state == "dash" || DuelSim.IsAttack(seen.state))) return false;
            if (Math.Abs(foe.x - me.x) > foe.reach * L1.reachK + BODY * 0.5 + 0.25 || Rnd() >= rd * 0.6) return false;
            guardUntil = Math.Max(guardUntil, sim.clock + 0.25 + Rnd() * 0.25); held.guard = true; held.x = 0; planX = 0;
            return true;
        }
        // 不往对手的出手节奏里撞：对手可出手、自己在其轻击距离内，而他惯于频频试探 → 此刻抢攻多半被他抢先打在起手上。
        // 武力越高越懂得改为举防等他出手（挡下后轮到自己）。far：冲刺切入之前（只决定不冲，不举防）
        bool Wary(DuelSim sim, DuelSim.Snap seen, bool far = false)
        {
            var foe = sim.Other(me);
            // 他此刻（或转眼之间）就能出手：硬直 / 收招的长短是看得出来的（自己打出来的硬直更清楚）
            double left = foe.state == "hitstun" || foe.state == "guardstun" ? foe.stun - foe.t : DuelSim.Mv(foe.state) != null && foe.Phase == "recovery" ? (DuelSim.TotalOf(foe) - foe.t) / foe.spd : -1;
            bool ready = (seen != null && (seen.state == "idle" || seen.state == "walk" || seen.state == "guard" || seen.state == "dash" || seen.atk)) || (left >= 0 && left < 0.12);
            if (!ready) return false;
            double foeR = foe.reach * L1.reachK + BODY * 0.5;
            if (!far && Math.Abs(foe.x - me.x) > foeR + 0.3) return false;
            // 名将也有一成多按捺不住；相持 2 秒以上越来越沉不住气
            double calm = Clamp(1 - (sim.clock - quietAt - 2) / 3, 0.35, 1) * (1 - 0.8 * Urg(sim));
            if (Rnd() >= Math.Min(0.85, (far ? gs * 2.6 : gs * gs * 2.4) * PokeRisk()) * calm) return false;
            if (far) return true;
            held.x = 0; planX = 0;
            // 他这一刀眼看就来（频频试探、此刻正好够得着）：偶尔看准节奏格开它（格开冷却长，失手要挨打，所以不常用），否则举防等他出手
            if (sim.ParryReady(me) && sim.Actionable(me) && seen != null && (seen.state == "idle" || seen.state == "walk" || seen.state == "guard") && Rnd() < parryP * PokeRisk() * 0.6) { parryAt = sim.clock + 0.03 + Rnd() * (0.06 + 0.1 * (1 - s)); return true; }
            guardUntil = Math.Max(guardUntil, sim.clock + 0.3 + Rnd() * 0.35);
            held.guard = true;
            return true;
        }
        // 对手缩着举防的程度 0..1（近身时举防的时间多、又很少主动出手）
        double ShellK() { return Clamp((turtle - 0.4) / 0.35, 0, 1) * Clamp(1.5 - 0.6 * PokeRate(), 0, 1); }
        // 蓄力的站位：他轻击够不着、自己满蓄重击（含突进）够得着
        void ChargeSpot(DuelFighter foe, out double lo, out double hi)
        {
            lo = foe.reach * L1.reachK + BODY * 0.5 + 0.06; hi = me.reach * MvHeavy.reachK + BODY * 0.5 + 0.22 - 0.08;
        }
        // 他惯于见刃光就格开、格开又已就绪：满蓄重击改为佯攻（在他够不着处蓄满松手）。看见他的格开在冷却、蓄满打到之前好不了 → 真打
        void FeintSpot(DuelFighter foe, out double lo, out double hi) { double l0, h0; ChargeSpot(foe, out l0, out h0); double hh = h0 + 0.08; lo = hh + 0.07; hi = hh + 0.32; }
        // 走到蓄力站位、蓄满、打到之前他的格开都好不了
        bool CdWin(DuelSim sim)
        {
            var foe = sim.Other(me); double lo, hi; ChargeSpot(foe, out lo, out hi);
            double walk = Math.Max(0, Math.Abs(foe.x - me.x) - hi + 0.05) / me.walk;
            return FoeCd(sim) > walk + CHARGE_MAX + DuelSim.HeavySU(1, me.spd) / me.spd + 0.06;
        }
        bool WantFeint(DuelSim sim) { return hvPar > 0.35 && FoeCd(sim) == 0; }
        // 场外蓄力：对手惯于走进来试探时，在他轻击够不着处先蓄满重击，待他走进重击距离（重击比轻击远、且带突进）再松手；
        // 对手一味举防不出手时，走到重击够得着而他轻击够不着处蓄满破防。满蓄重击被格开 / 蓄力中挨打的次数越多越少用
        bool FarCharge(DuelSim sim, DuelSim.Snap seen, double dist)
        {
            var foe = sim.Other(me); double now = sim.clock, g = gs;
            bool win = CdWin(sim);    // 他格不开（不受蓄力间隔的限制：机不可失）
            if ((now < chgAt && !win) || !sim.Actionable(me) || seen == null || !(seen.state == "walk" || seen.state == "idle" || seen.state == "guard" || (win && (seen.state == "parry" || seen.state == "parrywhiff")))) return false;
            double spLo, spHi; ChargeSpot(foe, out spLo, out spHi);
            double hvR = spHi + 0.08;
            double approach = Clamp((PokeRate() - 0.6) / 1.2, 0, 1), shell = ShellK();
            double pr = win ? 0 : hvPar;
            if (shell > 0 && !win && WantFeint(sim))
            {
                // 佯攻：蓄满了照样松手（够不着、挥空），他一按格开就落空、进冷却
                double fsLo, fsHi; FeintSpot(foe, out fsLo, out fsHi);
                if (dist < fsLo || dist > fsHi) return false;
                chgAt = now + 0.4;
                if (Rnd() >= (0.3 + 0.6 * g) * g * shell * Math.Min(1, pr * 1.5) * (1 - Urg(sim) * 0.5)) return false;
                chgAt = now + 0.8 + Rnd() * 1.2;
                chgFar = "shell"; fnt = true; fntHold = Rnd() * 0.22; HeavyTo(9);
                return true;
            }
            // 他正走过来：估计他走到重击距离还要多久（有反应延迟），约 0.4–1 秒（蓄得够深）才开始蓄
            var prev = sim.Seen(foe.idx, react + 0.12);
            double vc = prev != null && seen.clock > prev.clock ? (Math.Abs(prev.x - me.x) - Math.Abs(seen.x - me.x)) / (seen.clock - prev.clock) : 0;
            double tArr = vc > 0.8 ? (dist - vc * react - (hvR - 0.1)) / vc : 9;
            string mode = approach > 0 && dist > spLo + 0.1 && tArr > 0.4 && tArr < 1.0 ? "approach" : shell > 0 && dist >= spLo && dist <= spHi ? "shell" : null;
            if (mode == null) return false;
            if (!win) chgAt = now + 0.4;
            double p = (0.1 + 0.5 * g) * g * (mode == "shell" ? shell : approach) * (1 - 0.8 * hvBad) * (1 - 0.9 * pr);
            if (win && mode == "shell") p = Math.Max(p, (0.35 + 0.6 * g) * Math.Min(1, 0.4 + shell) * (1 - 0.5 * hvBad));
            if (Rnd() >= p) return false;
            chgAt = now + 2.2 + Rnd() * 2.2;
            chgFar = mode; fnt = false; HeavyTo(9);
            return true;
        }
        // 观察对手的出招习惯（记忆而非反应：只统计已发生的动作）
        void Observe(DuelSim sim)
        {
            var foe = sim.Other(me);
            // 对手在受击硬直中按格开（看得见的出手）/ 自己的第三段被格开：记下这个习惯，慢慢淡忘
            if (foe.state == "hitstun" && foe.hsParrySeq == foe.seq && hsSeen != foe.seq) { hsSeen = foe.seq; p3 += (1 - p3) * 0.6; }
            p3 *= 1 - DT * 0.05;
            // 试探频率：自己在对手轻击距离内、对手可出手的时长（约 14 秒的记忆）；近身时对手举防的时间比例
            double d = Math.Abs(foe.x - me.x), foeR = foe.reach * L1.reachK + BODY * 0.5;
            double fade = 1 - DT * 0.07;
            pkN *= fade; pkT *= fade; hvBad *= 1 - DT * 0.03;
            bool expo = d <= foeR + 0.15 && (sim.Actionable(me) || me.state == "guard");   // 自己站在他轻击距离内、没在出招
            if ((foe.state == "idle" || foe.state == "walk" || foe.state == "guard") && expo) pkT += DT;
            bool fg = foe.state == "guard" || foe.state == "guardstun";
            if (d < foeR + 1 && (fg || foe.state == "idle" || foe.state == "walk")) turtle += ((fg ? 1 : 0) - turtle) * DT * 0.5;
            // 挡下之后：对手被自己打中 → 无从得知；观察期过了也没出手 → 他不抢回合
            if (rpWatch != 0)
            {
                if (foe.state == "hitstun" || foe.state == "guardstun" || foe.state == "knockdown" || foe.state == "parried") rpWatch = 0;
                else if (sim.clock > rpWatch) { repoke -= repoke * 0.2; rpWatch = 0; }
            }
            // 抢回合那一刀的结果：打中 → 淡忘；被挡 / 被格开 / 被他抢先打中 → 记下
            int hn = me.stats.hits + me.stats.taken;
            if (hn != hitN) { hitN = hn; quietAt = sim.clock; }
            // 观察到收招后 0.35 秒：挥空（多半是他挡下就退开）也记下，退开后抓自己挥空的破绽算「被抢先」
            var tm = ttMine;
            if (tm != null)
            {
                var st = me.stats;
                if (tm.seq < 0 && me.state == "light1") tm.seq = me.seq;
                if (foe.state == "backstep" || ((foe.state == "walk" || foe.state == "idle") && foe.vx * me.face > 0.5)) tm.back = true;
                if (st.guarded > tm.guarded || st.parried > tm.parried || st.taken > tm.taken)
                {
                    ttBad += (1 - ttBad) * 0.4;
                    if (tm.end != 0) ttBack += (1 - ttBack) * 0.25;
                    ttMine = null;
                }
                else if (st.hits > tm.hits) { ttBad *= 0.75; if (!tm.dash) ttBack *= 0.7; ttMine = null; }
                else if (tm.seq >= 0 && tm.end == 0 && (me.seq != tm.seq || (me.Phase == "recovery" && me.hitIdx == 0))) { tm.end = sim.clock; ttBack += (1 - ttBack) * (tm.back ? 0.45 : 0.2); }
                else if (tm.end != 0 ? sim.clock > tm.end + 0.35 : tm.seq < 0 && sim.clock > tm.t + 0.3) ttMine = null;
            }
            ttBack *= 1 - DT * 0.01;
            // 自己松开的蓄力重击（六成以上，他的格开当时已就绪——事后看冷却圈就知道；佯攻也算）：他按没按格开
            if (me.state == "heavy" && me.seq != hvRel)
            {
                hvRel = me.seq;
                hvWatch = me.charge >= 0.6 && sim.ParryReady(foe) ? new HvWatch { until = sim.clock + DuelSim.HeavySU(me.charge, me.spd) / me.spd + 0.12, tries = foe.stats.parryTries } : null;
            }
            var hw = hvWatch;
            if (hw != null)
            {
                if (foe.stats.parryTries > hw.tries) { hvPar += (1 - hvPar) * 0.5; hvWatch = null; }
                else if (sim.clock > hw.until) { hvPar -= hvPar * 0.3; hvWatch = null; }
            }
            hvPar *= 1 - DT * 0.004;
            if (DuelSim.IsAttack(foe.state) && foe.hitIdx > 0 && (me.state == "hitstun" || me.state == "knockdown")) oHit = true;
            if (foe.seq == oSeq) return;
            string prevS = oState;
            // 主动起手（轻击 / 蓄力 / 冲刺斩）：计入试探频率；挡下后的观察期内起手 = 抢回合
            if ((foe.state == "light1" || foe.state == "charge") && (prevS == "idle" || prevS == "walk" || prevS == "guard" || prevS == "dash" || prevS == "backstep" || prevS == "land"))
            {
                if (expo || (d <= foeR + 0.4 && (me.state == "walk" || me.state == "idle" || me.state == "dash"))) pkN += 1;
                if (rpWatch != 0 && sim.clock <= rpWatch) { repoke += (1 - repoke) * 0.35; rpWatch = 0; }
            }
            if ((prevS == "light1" || prevS == "light2") && !oHit)
            {
                // 上一段挥空 / 被挡：接着出下一段 = 不看命中的连按；停手 = 有判断
                if (foe.state == DuelSim.Mv(prevS).next) mash += (1 - mash) * 0.3; else mash -= mash * 0.12;
            }
            if (foe.state == "light1" && me.state != "hitstun" && Math.Abs(foe.x - me.x) > foe.reach * L1.reachK + BODY * 0.5 + 0.08) mash += (1 - mash) * 0.12;   // 够不着也出手
            oSeq = foe.seq; oState = foe.state; oHit = false;
        }
        void Press(string k) { taps.Press(k); }
        // 冲刺斩：先冲刺，冲到够得着再出斩（冲刺中一出斩就失去冲势——同一帧按下只多走半步，够不着）
        void DashStrike(double now, bool far = false) { held.dash = me.face; chaseUntil = now + 0.3; chaseFar = far; }
        // 重击：同时按住，蓄力到 to 秒后松开（只点按会被当作轻点重击）
        // 举防中按下的重击要等放下防御（下一步）才起手：这期间一直按住，免得被当作轻点
        void HeavyTo(double to) { Press("heavy"); held.heavy = true; chargeTo = to; hvWait = 0.12; }

        public void Update(double dt, DuelSim sim)
        {
            Observe(sim);
            Think(dt, sim);
            if (parryAt >= 0 && sim.clock >= parryAt - 1e-9) { Press("parry"); parryAt = -1; }
            // 逼近过滤：向前走将进入对手的攻击距离、而对手正在逼近或出招时，高武力改为举防停步（不硬闯）
            var foe = sim.Other(me); var H = held; var T = taps;
            double dist = Math.Abs(foe.x - me.x), foeR = foe.reach * DuelSim.Light2.reachK + BODY * 0.5;
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
            double reach = me.reach * L1.reachK + BODY * 0.5 - 0.03;   // 轻击实际判定距离（略保守）
            var seen = sim.Seen(foe.idx, react);
            // 冲刺斩：冲到够得着（追退开的对手要更近）或冲势将尽再出斩
            if (now < chaseUntil && me.state == "dash") { if (dist <= reach - (chaseFar ? 0.35 : 0.1) || me.t >= 0.19) { chaseUntil = 0; Press("light"); } return; }

            // 上一步是否正在蓄力 / 满蓄重击起手（被打断或被格开时记下）
            string lastA = lastAtk;
            lastAtk = me.state == "charge" || (me.state == "heavy" && me.charge >= 0.85 && me.Phase == "startup") ? "charge" : "";
            // 蓄力：到达预定蓄力量后松开
            if (me.state == "charge")
            {
                H.x = 0; H.guard = false;
                hvWait = 0;
                if (chgFar == null) { H.heavy = me.t < chargeTo; return; }
                // 场外蓄力：按看到的对手位置与来势（有反应延迟）估计松手后打到时的距离——蓄满且他将进入重击距离才松手；
                // 他逼到轻击距离边上（再等就要挨他一刀）则提前松手；蓄满后他迟迟不来、或摆出格开架势，也就松手了
                double hvR = me.reach * MvHeavy.reachK + BODY * 0.5 + 0.22, foeR = foe.reach * L1.reachK + BODY * 0.5;
                var prev = sim.Seen(foe.idx, react + 0.12);
                double vc = seen != null && prev != null && seen.clock > prev.clock ? Math.Max(0, (Math.Abs(prev.x - me.x) - Math.Abs(seen.x - me.x)) / (seen.clock - prev.clock)) : 0;
                double dNow = (seen != null ? Math.Abs(seen.x - me.x) : dist) - vc * react;
                double dImp = dNow - vc * (DuelSim.HeavySU(Math.Min(1, me.t / CHARGE_MAX), me.spd) / me.spd);
                // 迎着走来的对手：蓄到六成以上（打中即击倒）就可以松手；对着缩着举防的对手：非蓄满（破防）不松手；
                // 倒地的对手：松手后打到时他刚好起身无敌结束
                bool full = me.t >= (chgFar == "shell" ? CHARGE_MAX : CHARGE_MAX * 0.65);
                double tImp = DuelSim.HeavySU(Math.Min(1, me.t / CHARGE_MAX), me.spd) / me.spd;
                bool okiGo = chgFar == "oki" && HittableIn(foe) <= tImp + 0.03;
                bool go = okiGo || (chgFar != "oki" && full && dImp <= hvR - 0.06) || ((dNow <= foeR - 0.05 || (vc > 0.5 && dNow <= foeR + 0.08)) && me.t >= 0.45) || me.t >= CHARGE_MAX + 0.26 ||
                    (fnt && me.t >= CHARGE_MAX + fntHold) ||
                    (seen != null && seen.state == "parry") || (seen != null && DuelSim.IsAttack(seen.state) && seen.phase != "recovery" && dist < foe.reach + 0.6 && me.t >= 0.3);
                H.heavy = !go;
                if (go) { chgFar = null; fnt = false; }
                return;
            }
            if (hvWait > 0 && (sim.Actionable(me) || me.state == "guard")) { hvWait -= dt; H.heavy = true; H.guard = false; H.x = 0; return; }
            hvWait = 0; H.heavy = false; chgFar = null; fnt = false;
            // 受击 / 被挡 / 倒地：高武力会按住格挡，一恢复就举防（并有机会格开弱者的连段）
            if (me.state == "hitstun" || me.state == "guardstun" || me.state == "guardbreak" || me.state == "knockdown" || me.state == "down" || me.state == "getup" ||
                me.state == "parried" || me.state == "parrywhiff")
            {
                if (stunSeq != me.seq)
                {
                    stunSeq = me.seq; stunGuard = Rnd() < stunGuardP; stunHold = 0.1 + Rnd() * 0.22;
                    parryAt = -1; ttAt = -1;
                    // 挡下的是哪一招：硬直一结束就轮到自己（见下方「抢回合」）；挨打则作罢
                    blk = me.state == "guardstun" && DuelSim.IsAttack(foe.state) && foe.state != "special" && foe.state != "air" ? new Blk { seq = foe.seq, move = foe.state } : null;
                    // 满蓄重击被格开 / 蓄力中被打：记下，往后少用
                    if ((me.state == "parried" || me.state == "hitstun" || me.state == "knockdown") && lastA == "charge") hvBad += (1 - hvBad) * 0.45;
                    double rk0 = RkVs(foe);
                    // 看破连段：被第二段打中，料定对手会接第三段 → 算准第三段打到的时刻按下格开（兵器较短、近不了身的更要靠格开）
                    if (me.state == "hitstun" && me.Grounded && foe.state == "light2" && rk0 > 0 && sim.ParryReady(me))
                    {
                        double outK = 1 + 2 * Clamp((foe.reach - me.reach) / 0.25, 0, 1);
                        if (Rnd() < parryP * (0.3 + 0.15 * MashN()) * (0.5 + 0.5 * rk0) * outK) PlanParry(sim, NextChainHit(sim, foe), DuelSim.HS_PARRY_K * 0.5, 0.65);
                    }
                    // 挡下连段的一击：料定对手还会接着打 → 格开下一击，然后反击（对手停手则落空，露出破绽）
                    if (me.state == "guardstun" && (foe.state == "light1" || foe.state == "light2") && sim.ParryReady(me) &&
                        Rnd() < parryP * (0.05 + 0.6 * MashN())) PlanParry(sim, NextChainHit(sim, foe));
                }
                // 挡下对手的连段末段或重击：记下，硬直一结束就反击其收招破绽
                if (me.state == "guardstun" && ctrSeq != foe.seq && (foe.state == "light3" || foe.state == "heavy") && Rnd() < punishP * RkVs(foe) * (0.125 + 0.375 * Read(foe))) ctrSeq = foe.seq;
                H.guard = stunGuard; H.x = 0;
                if (foe.state == "special" && seen != null && seen.state == "special" && seen.seq == foe.seq)
                {
                    if (spSeq != foe.seq) { spSeq = foe.seq; spGuard = Rnd() < 0.3 + 0.5 * gs; }
                    H.guard = H.guard || spGuard;
                }
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
                    bool reachOk = dist + away <= me.reach * nx.reachK + BODY * 0.5 + 0.03 + nx.lunge * 0.12;
                    bool go = landed ? comboRoll < comboP && (reachOk || Rnd() > s) : comboRoll < comboP * (1 - s) * 0.6;
                    // 对手惯于格开第三段且格开已就绪：只打两段就收（让他落空，再抓破绽）
                    if (go && me.state == "light2" && landed && sim.ParryReady(foe) && Rnd() < RkVs(foe) * p3 * 1.5) { go = false; baitUntil = sim.clock + 0.9; }
                    if (go) Press("light");
                    comboDone = true;
                }
                H.x = 0; H.guard = false;
                return;
            }
            comboDone = false;
            bool free = sim.Actionable(me) || me.state == "guard";
            // 格开成功：趁对手硬直反击（第一击 ×1.5 不可挡）——近则重击或连段，远则冲刺斩
            if (me.state == "parryok" || (sim.Countering(me) && free))
            {
                guardUntil = 0; H.guard = false; parryAt = -1;
                if (dist <= reach + 0.05)
                {
                    if (foe.state == "parried" && Rnd() < 0.25 + 0.4 * s) HeavyTo(0.14 + Rnd() * 0.12);
                    else Press("light");
                    H.x = 0;
                }
                else if (dist <= reach + 0.85)
                {
                    // 弹刀姿势里先出的是轻击（够不着就挥空）：先冲刺，冲刺中再出斩
                    DashStrike(now);
                }
                else H.x = me.face;
                return;
            }
            // 诱招得手：对手果然格开落空 → 立即抓破绽（早有预料，不必等看清）
            if (now < baitUntil && foe.state == "parrywhiff" && free)
            {
                baitUntil = 0; guardUntil = 0; H.guard = false;
                if (dist <= reach) { Press("light"); H.x = 0; return; }
                if (dist <= reach + 1.2) { DashStrike(now); return; }
            }
            // 已预约格开：举防不动，等那一刻
            if (parryAt >= 0) { blk = null; if (free) { H.guard = true; H.x = 0; } return; }
            if (me.state == "parry") { H.x = 0; H.guard = false; return; }
            // 对手放绝技（定格期间看得清清楚楚）：绝技不可格开，也躲不开突进——举防硬吃（被终结一击破防也比连挨四刀轻）
            if (foe.state == "special" && seen != null && seen.state == "special" && seen.seq == foe.seq)
            {
                if (spSeq != foe.seq) { spSeq = foe.seq; spGuard = Rnd() < 0.3 + 0.5 * gs; }
                if (spGuard && (free || me.state == "guardstun")) { blk = null; ttAt = -1; H.guard = true; H.x = 0; return; }
            }
            // 抢回合：挡下对手的一击、防御硬直刚结束（连段的下一击早该到了，没到就是打完了）——
            // 大招（第三段 / 重击）收招慢，按招式节奏算得出来得及就反击；轻击被挡后双方几乎同时能动：对手不惯于立刻再出手
            // 就趁他收刀抢先出手（略有迟疑，武力越低越慢、越常错过），惯于立刻再出手的则改为格开他那一刀或继续举防
            if (blk != null && free)
            {
                var b = blk; blk = null;
                bool same = foe.seq == b.seq && DuelSim.IsAttack(foe.state);
                double left = same ? Math.Max(0, (DuelSim.TotalOf(foe) - foe.t) / foe.spd) : 0;
                double need = L1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
                bool big = b.move == "light3" || b.move == "heavy";
                // 对手武力高出越多（出手快、收招也快）越难抢到回合；弱将（临场判断差）多半想不到
                double gapK = Clamp(1 + (me.war - foe.war) / 45, 0.3, 1) * Math.Min(1, gs / 0.6);
                rpWatch = now + left + 0.14;    // 他收刀后 0.14 秒内再出手 = 惯于立刻抢回合
                if (big && same)
                {
                    // 这一判定取代了防御硬直中「记下大招」的那一次（不重复掷骰）
                    bool go = left > need - 0.02 && dist <= reach + 1.0 && Rnd() < punishP * gapK * 0.6;
                    ctrSeq = -1;
                    if (go)
                    {
                        guardUntil = 0; H.guard = false;
                        if (dist <= reach) { Press("light"); H.x = 0; return; }
                        DashStrike(now); return;
                    }
                }
                if (!big && dist <= reach + 0.8 && foe.state != "special")
                {
                    double rp = repoke;
                    if (rp > 0.45 && sim.ParryReady(me) && Rnd() < parryP * (rp - 0.3) * 1.6 &&
                        PlanParry(sim, now + left + L1.startup / foe.spd + 0.03)) { guardUntil = 0; H.guard = true; H.x = 0; return; }
                    // 够不着（对手兵器长、挡下后又被推开）：冲刺斩抢回合
                    bool outR = dist > reach;
                    // 他惯于挡下就退开（站着出的这一刀多半挥空、反被他抓）：改为冲刺斩追上去（立刻出手，冲刺比后撤快）；连冲刺斩也追不上就少抢
                    double tb = ttBack; bool chase = Rnd() < tb * (0.5 + 0.5 * gs);
                    if (Rnd() < 0.85 * gs * gapK * (1 - 0.9 * rp) * (1 - 0.45 * ttBad) * (1 - 0.5 * tb * tb))
                    {
                        // 他惯于挡住（或格开）立刻还手的那一刀：多半改为慢半拍——举防等他放下防御再出手
                        ttLate = Rnd() < ttBad * 0.8;
                        guardUntil = 0; H.guard = ttLate; H.x = 0;
                        ttAt = now + (ttLate ? 0.28 + Rnd() * 0.27 : Rnd() * (0.05 + 0.14 * (1 - gs))); ttDash = outR || (chase && !ttLate); ttChase = ttDash && !outR;
                    }
                }
            }
            if (ttAt >= 0)
            {
                if (!free) ttAt = -1;
                else if (foe.state == "special") ttAt = -1;
                else if (now >= ttAt)
                {
                    ttAt = -1;
                    // 慢半拍出手时他已退开：近则冲刺斩，远了就作罢
                    if (dist > reach + (ttDash ? 0.85 : ttLate ? 0.7 : 9)) { H.guard = false; return; }
                    H.guard = false; H.x = 0;
                    // 追击退开的对手：先冲刺，冲到够得着再出斩（冲刺一出斩就失去冲势，只多走半步）
                    if (ttChase) DashStrike(now, true);
                    else if ((ttDash || ttLate) && dist > reach) DashStrike(now);
                    else Press("light");
                    ttMine = new TtMine { t = now, hits = me.stats.hits, guarded = me.stats.guarded, parried = me.stats.parried, taken = me.stats.taken, seq = -1, end = 0, back = false, dash = H.dash != 0 };
                    return;
                }
                else { H.guard = ttLate; H.x = 0; return; }
            }
            // 看清重击起手（刃口闪光）：反应时间与看见其他来招相同。看清即按（手上再慢 0–0.05 秒，武力越低越慢）；
            // 熟知招式节奏，算得出来不来得及——轻点重击（约 0.15 秒）看到时已打到，来不及就举防；满蓄重击（约 0.27 秒，会破防）
            // 武力高者常能当场格开，但至多不到一半（HEAVY_PARRY_CAP），其余照样被破防或挨打
            if (foe.state == "heavy" && foe.Phase == "startup" && foe.seq != hvSeq && free && sim.ParryReady(me))
            {
                var sn = sim.Seen(foe.idx, react);
                if (sn != null && sn.state == "heavy" && sn.seq == foe.seq)
                {
                    hvSeq = foe.seq;
                    double at = now + Rnd() * 0.05 * (1.6 - s), T = now + (DuelSim.SuOf(foe) - foe.t) / foe.spd;
                    if (at < T - 0.01 && dist <= foe.reach * MvHeavy.reachK + BODY * 0.5 + 0.6 && Rnd() < Math.Min(DuelSim.HEAVY_PARRY_CAP, parryP * (0.55 + 0.45 * foe.charge)))
                    {
                        parryAt = at; H.guard = true; H.x = 0; return;
                    }
                }
            }
            // 看见对手摆出格开架势：不往刀口上撞，停手等他落空（落空后由下面一段抓破绽）
            if (seen != null && seen.state == "parry" && free)
            {
                if (seen.seq != stanceSeq) { stanceSeq = seen.seq; stanceHold = Rnd() < 0.1 + 0.8 * s; }
                if (stanceHold) { planX = 0; H.x = 0; H.guard = false; guardUntil = 0; return; }
            }
            // 对手格开落空：抓破绽（按剩余破绽时间判断来不来得及，远则冲刺斩；时间充裕时用重击）
            if (seen != null && seen.state == "parrywhiff" && seen.seq != pwSeq && free)
            {
                pwSeq = seen.seq;
                double left = foe.state == "parrywhiff" && foe.seq == seen.seq ? DuelSim.PARRY_WHIFF - foe.t : 0;
                double need = L1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
                if (left > need - 0.02 && dist <= reach + 1.2 && Rnd() < punishP)
                {
                    guardUntil = 0; H.guard = false;
                    if (dist <= reach)
                    {
                        if (left > (0.12 + DuelSim.HeavySU(0.13 / CHARGE_MAX, me.spd)) / me.spd + 0.03 && Rnd() < 0.5 * s) HeavyTo(0.13); else Press("light");
                        H.x = 0; return;
                    }
                    DashStrike(now); return;
                }
            }
            // 自己的攻击被挡：高武力预料对手会反击，收招后先举防
            if (me.stats.guarded != lastGuarded)
            {
                lastGuarded = me.stats.guarded;
                var mv = DuelSim.Mv(me.state);
                double left = mv != null ? (DuelSim.TotalOf(me) - me.t) / (me.state == "special" ? 1 : me.spd) : 0;
                if (Rnd() < 0.1 + 0.7 * s) guardUntil = Math.Max(guardUntil, now + left + 0.18 + Rnd() * 0.25);
            }
            // 反击：刚挡下 / 格开的大招还在收招（按招式节奏算好来得及）
            if (ctrSeq >= 0 && free)
            {
                var fm = DuelSim.Mv(foe.state);
                if (ctrSeq == foe.seq && fm != null && foe.Phase == "recovery")
                {
                    double left = (DuelSim.TotalOf(foe) - foe.t) / foe.spd;
                    double need = L1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
                    if (left > need - 0.02 && dist <= reach + 1.1)
                    {
                        ctrSeq = -1; guardUntil = 0; H.guard = false;
                        if (dist <= reach) { Press("light"); H.x = 0; return; }
                        DashStrike(now); return;
                    }
                }
                if (ctrSeq != foe.seq || fm == null) ctrSeq = -1;
            }
            // 对手被破防：立即追击
            if (foe.state == "guardbreak" && free && foe.t < foe.stun - L1.startup / me.spd)
            {
                guardUntil = 0; H.guard = false;
                if (dist <= reach) { Press("light"); H.x = 0; return; }
                if (dist <= reach + 1.2 && me.state != "guard") { DashStrike(now); return; }
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
                        double lead = L1.startup / me.spd + DT;
                        double ty = foe.y + foe.vy * lead - 0.5 * DuelSim.GRAV * lead * lead;
                        double dx = (foe.x + foe.vx * lead - me.x) * me.face;
                        if (foe.vy < 1.5 && ty < 0.8 && ty > -0.2 && dx > -0.1 && dx <= me.reach * L1.reachK + BODY * 0.5 - 0.05)
                        {
                            Press("light"); aa = "done"; H.guard = false; H.x = 0; return;
                        }
                        if (Math.Abs(foe.x - me.x) < 0.9 && foe.y > 0.7) aa = "guard";   // 太近 / 越过头顶：改为格挡
                        else { H.guard = false; H.x = 0; return; }
                    }
                    H.guard = true; H.x = 0; return;
                }
                if (foe.state == "land")      // 落地硬直：反击
                {
                    if (dist <= reach * 1.05) { Press("light"); aa = "done"; H.guard = false; H.x = 0; return; }
                    if (dist <= reach + 1.2) { DashStrike(now); aa = "done"; H.guard = false; return; }
                }
            }

            // 正在格挡而对手的连段还在继续（看到对手仍在出招）：不放下防御
            if ((me.state == "guard" || me.state == "guardstun") && seen != null && seen.atk && Math.Abs(seen.x - me.x) <= foe.reach + BODY + 0.6 && Rnd() < 0.3 + 0.7 * s)
                guardUntil = Math.Max(guardUntil, now + 0.12);
            // 看见对手出招：按格挡率决定是否举防
            if (seen != null && seen.atk && seen.seq != seenSeq)
            {
                seenSeq = seen.seq;
                bool threat = Math.Abs(seen.x - me.x) <= foe.reach + BODY + 0.5;
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
                double left = fm != null && foe.seq == seenR.seq ? (DuelSim.TotalOf(foe) - foe.t) / (foe.state == "special" ? 1 : foe.spd) : 0;
                double need = L1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
                // 对手武力高出越多（收招也快）越常错过
                if (left > need - 0.03 && Rnd() < punishP * (0.5 + 0.5 * Clamp(1 + (me.war - foe.war) / 45, 0.3, 1)))
                {
                    guardUntil = 0; H.guard = false;
                    if (dist <= reach) { Press("light"); H.x = 0; return; }
                    DashStrike(now); return;
                }
            }
            // 识破连按：对手一进入其攻击距离就会乱挥 → 预判举防等他打完，或后撤（冲刺）让他挥空，再抓收招
            if (rd > 0 && seen != null && free && now >= mzAt)
            {
                double foeR = foe.reach * L1.reachK + BODY * 0.5;
                bool busy = seen.phase == "recovery" || seen.state == "hitstun" || seen.state == "guardstun" || seen.state == "guardbreak" ||
                    seen.state == "knockdown" || seen.state == "down" || seen.state == "getup" || seen.state == "land";
                if (!busy && dist < foeR + 0.5)
                {
                    mzAt = now + 0.3 + Rnd() * 0.2;
                    double r0 = Rnd(); bool room = Math.Abs(me.x - me.face * 1.6) < DuelSim.ARENA - 0.3;
                    if (r0 < rd * 0.6 && room && me.state != "guard")     // 后撤半步：他够不着照样挥，挥空就抓
                    {
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
                double lead = L1.startup / me.spd;
                double pd = Math.Abs(seen.x + vx * (react + lead) - (me.x + me.vx * lead * 0.3));
                if (pd <= reach)
                {
                    pokeAt = now + 0.3;
                    if (Rnd() < pokeP) { if (HoldOff(sim, seen, rd) || Wary(sim, seen)) return; Press("light"); H.x = 0; planX = 0; return; }
                }
            }
            // 看见对手蓄力：高武力抢攻（打断蓄力）；蓄得浅就举防，蓄得深（将破防）就后撤；低武力傻站
            if (seen != null && seen.state == "charge" && dist < foe.reach * 1.08 + BODY * 0.5 + 0.9 && now >= next)
            {
                next = now + period;
                double r0 = Rnd();
                if (r0 < s * 0.6 && dist <= reach && sim.Actionable(me)) { Press("light"); return; }
                if (r0 < s * 0.9)
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
            if (seen != null && DuelSim.IsAttack(seen.state) && seen.phase != "recovery" && seen.state != "special" && dist < foe.reach + BODY * 0.5 + 0.7)
            {
                if (patSeq != seen.seq) { patSeq = seen.seq; patient = Rnd() < (0.15 + 0.8 * s) * (1 - 0.4 * Urg(sim)); }
                if (patient) { planX = 0; H.x = 0; return; }
            }
            if (now < next)
            {
                // 走进攻击距离即停步，避免贴身（停在蓄力站位上：松开方向后还会滑行一段，提前松开）
                if (planX == me.face && (dist <= reach * 0.92 || dist - (me.vx * me.face > 0 ? me.vx * me.vx / (2 * DuelSim.FRICTION) : 0) <= stopAt)) planX = 0;
                H.x = planX;
                return;
            }
            next = now + period * (0.7 + Rnd() * 0.6);
            planX = 0; stopAt = 0;
            H.x = 0;

            // 对手倒地：退到他起身后轻击够不着、自己满蓄重击够得着处，算准他起身无敌结束的那一刻打到（起身就举防的会被破防，
            // 起身就往前走的正撞上去）；武力低的只会走近了等
            if (foe.state == "knockdown" || foe.state == "down" || foe.state == "getup")
            {
                double spLo, spHi; ChargeSpot(foe, out spLo, out spHi);
                // 他起身就格开（惯于见刃光就按、格开届时已好）：少用
                double parryOk = FoeCd(sim) < HittableIn(foe) + 0.1 ? hvPar : 0;
                // 每次击倒定一次（不在两个站位之间来回改主意）
                if (foe.state == "knockdown" && kdSeq != foe.seq) { kdSeq = foe.seq; okiPick = Rnd(); }
                bool oki = spHi >= spLo + 0.04 && okiPick < (0.2 + 0.6 * gs * (1 - 0.6 * hvBad)) * (1 - 0.8 * parryOk);
                if (dist > (oki ? spHi : reach * 1.1)) planX = me.face;
                else if (dist < (oki ? spLo : reach * 0.6)) planX = -me.face;
                else if (oki && foe.state != "knockdown" && sim.Actionable(me) && HittableIn(foe) > CHARGE_MAX * 0.75 + 0.27) { chgFar = "oki"; HeavyTo(9); }
                else if (!oki && Rnd() < 0.3 * s * (1 - 0.6 * parryOk)) HeavyTo(0.3 + Rnd() * 0.5);   // 走近了蓄一刀等他起身
                H.x = planX;
                return;
            }
            // 绝技
            if (me.rage >= 100 && (me.bow ? dist > reach * 0.6 : dist <= reach + 1.8) && Rnd() < 0.3 + 0.55 * s) { Press("special"); return; }
            if (dist > reach * 1.02)
            {
                if (FarCharge(sim, seen, dist)) return;
                // 对手缩着举防：停在蓄力的站位上（他够不着、自己的满蓄重击够得着），等蓄力的时机
                // 他惯于见刃光就格开而格开已就绪：改站佯攻的位置（他够不着、自己满蓄也差一点够不着）；看见他的格开在冷却：赶紧上前真打
                double shell = ShellK(); bool win = shell > 0 && CdWin(sim);
                double spLo, spHi;
                if (shell > 0 && !win && WantFeint(sim)) FeintSpot(foe, out spLo, out spHi); else ChargeSpot(foe, out spLo, out spHi);
                if (shell > 0 && Rnd() < (win ? 0.5 + 0.5 * gs : Math.Min(1, shell * (0.12 + gs)) * (1 - Urg(sim))))
                {
                    if (dist >= spLo && dist <= spHi) { planX = 0; H.x = 0; return; }
                    if (dist > spHi && spHi >= spLo + 0.04) stopAt = (spLo + spHi) / 2;   // 走过去，停在站位上
                    else if (dist < spLo && Math.Abs(me.x - me.face * 0.5) < DuelSim.ARENA - 0.3) { planX = -me.face; H.x = planX; next = now + 0.1 + Rnd() * 0.1; return; }
                }
                // 高武力：对手兵器够得着时不贸然闯入，在其攻击距离外踱步，等对手先出手露出破绽
                double foeR = foe.reach + BODY * 0.5 + 0.15;
                bool pushy = seen != null && (seen.state == "walk" || seen.state == "dash" || DuelSim.IsAttack(seen.state));
                if (pushy && dist < foeR + 0.5 && Rnd() < (0.1 + 0.65 * s) * (1 - 0.6 * Urg(sim)))
                {
                    planX = dist < foeR + 0.12 ? -me.face : 0; H.x = planX;
                    return;
                }
                // 兵器较短：抓对手收招的破绽，或冲刺切入（冲刺中轻击 = 突进斩）
                double gap = foe.reach - me.reach;
                if (gap > 0.03 && dist < reach + 0.85)
                {
                    double k = Math.Min(1, 0.35 + gap / 0.3);
                    // 破绽还剩多少（熟知招式节奏与硬直长短）：冲过去来不及就不算破绽
                    double lft = foe.state == "hitstun" || foe.state == "guardstun" ? foe.stun - foe.t : DuelSim.Mv(foe.state) != null && foe.Phase == "recovery" ? (DuelSim.TotalOf(foe) - foe.t) / foe.spd : 0;
                    bool opening = seen != null && (seen.phase == "recovery" || seen.state == "hitstun" || seen.state == "guardstun") && lft > 0.07 + L1.startup / me.spd + Math.Max(0, dist - reach) / 7.4;
                    if ((opening && Rnd() < (0.35 + 0.5 * s) * k) || (Rnd() < (0.08 + 0.18 * s) * k && !Wary(sim, seen, true))) { DashStrike(now); return; }
                }
                if (gap > -0.06 && dist < reach + 0.75 && Rnd() < 0.05 + 0.1 * s && !Wary(sim, seen, true)) { DashStrike(now); return; }   // 兵器相当：偶尔冲刺抢攻
                if (dist < reach * 1.45 && Rnd() < whiffP) { Press("light"); return; }   // 冒失出手
                if (dist > 3.4 && Rnd() < 0.22 + 0.2 * s) { H.dash = me.face; return; }
                if (dist > 1.9 && dist < 3.3 && Rnd() < 0.05 + 0.05 * (1 - s)) { Press("up"); H.x = me.face; planX = me.face; return; }
                planX = me.face; H.x = planX;
                return;
            }
            // 贴得太近：长兵器施展不开，拉开距离
            if (dist < reach * 0.55 && Rnd() < 0.35 + 0.4 * s) { planX = -me.face; H.x = planX; next = now + 0.18 + Rnd() * 0.15; return; }
            // 进入攻击距离：高武力会预判举防
            if (dist <= foe.reach + BODY && Rnd() < guardP * 0.45 * (1 - 0.5 * Urg(sim))) { guardUntil = now + 0.25 + Rnd() * 0.3; H.guard = true; return; }
            // 对手一味举防不出手：退到他轻击够不着处，再蓄满重击破防
            double shell2 = ShellK();
            {
                double sLo, sHi; ChargeSpot(foe, out sLo, out sHi);
                if (shell2 > 0 && dist < sLo && Rnd() < shell2 * gs && Math.Abs(me.x - me.face * 0.5) < DuelSim.ARENA - 0.3)
                {
                    planX = -me.face; H.x = planX; next = now + 0.12 + Rnd() * 0.12; return;
                }
            }
            var st = me.stats;
            bool foeTurtles = (st.hits >= 4 && (double)st.guarded / st.hits > 0.45) || (seen != null && seen.state == "guard" && foe.state == "guard");
            // 近身蓄满破防：对手爱试探（蓄力会被打断）、惯于格开（格开已就绪）就少用——临场判断差的弱将想不到
            double jk = Math.Min(1, gs / 0.6);
            if (foeTurtles && Rnd() < (0.25 + 0.4 * s) * (1 - 0.8 * hvBad * jk) * (1 - shell2 * jk) * (PokeRate() < 0.5 ? 1 : 1 - gs) * (CdWin(sim) ? 1 : 1 - 0.9 * hvPar * jk)) { HeavyTo(CHARGE_MAX + 0.05); return; }
            double r = Rnd();
            if (r < aggr)
            {
                if (HoldOff(sim, seen, rd) || Wary(sim, seen)) return;
                if (Rnd() < 1 - 0.26 * Math.Max(0, 1 - 3 * gs * PokeRisk())) Press("light");   // 对手爱试探：近身少用重击（蓄力会被打断）
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

    // ------------------------------------------------------------ 测试用脚本玩家 --
    // tests/duel-bots.js（Node）、tests/duel.html 与 Tests~/duel 共用的「模拟玩家」（固定套路，规则随机数走 DuelSim.Rnd，可复现）：
    //   jumper 反复跳入空中斩 · masher 走近连按轻击（怒气满放绝技）· spam 冷却一好、对手在兵器距离内就按格开，其余时间连按 ·
    //   guard 谨慎的防守反击（反应 0.2 秒：看见来招就挡，抓收招与落空破绽，平时隔一阵试探一下，命中才接连段）·
    //   parry 同 guard，另按人的节奏与反应格开：挨了第二段格开第三段（瞄准有效区间正中，按键误差正态 σ 0.045 秒）、
    //   挡下一击后格开连段的下一击（同上）、看见重击松手（反应 0.22 ± 0.04 秒）就按——只有满蓄重击来得及；得手即反击 ·
    //   heavy 同 parry，但试探时六成改用重击（一半轻点、一半满蓄）：量电脑格开重击的频率
    public sealed class DuelScriptBot : IDuelCtrl
    {
        readonly string kind; readonly int idx;
        readonly DuelHeld held = new DuelHeld(); readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }
        double nt, pAt = -1, relAt; int gsSeq = -1, hvSeq = -1;
        const double HRT = 0.22, SD = 0.045;
        public DuelScriptBot(string kind, int idx = 0) { this.kind = kind; this.idx = idx; }
        static double Rnd() { return DuelSim.Rnd(); }
        public static double Gz() { return (Rnd() + Rnd() + Rnd() + Rnd() - 2) * 1.732; }   // 近似标准正态
        static double NextHit(DuelSim sim, DuelFighter o, DuelMove cur) { return sim.clock + (Math.Max(0, cur.startup + cur.active * 0.6 - o.t) + DuelSim.Mv(cur.next).startup) / o.spd; }

        public void Update(double dt, DuelSim sim)
        {
            var me = sim.f[idx]; var o = sim.f[1 - idx]; double d = Math.Abs(o.x - me.x), now = sim.clock; var H = held; var T = taps;
            if (kind == "jumper")
            {
                H.x = me.face;
                if (now < nt) return;
                if (sim.Actionable(me) && d < 3.2 && d > 1.2) { T.up++; nt = now + 0.1; }
                else if (me.state == "jump" && d < me.reach + 0.3) { T.light++; nt = now + 0.1; }
                else if (me.state != "jump" && d < me.reach) { T.light++; nt = now + 0.1; }
                return;
            }
            if (kind == "masher" || kind == "spam")
            {
                H.x = d > me.reach * 0.9 ? me.face : 0;
                if (kind == "spam" && sim.ParryReady(me) && d < o.reach + 0.5 && (sim.Actionable(me) || me.state == "guardstun" || me.state == "hitstun")) T.parry++;
                else if (now > nt && d < me.reach * 1.1) { T.light++; nt = now + 0.12; }
                if (me.rage >= 100) T.special++;
                return;
            }
            bool useParry = kind == "parry" || kind == "heavy";
            double reach = me.reach * DuelSim.Light1.reachK + DuelSim.BODY * 0.5 - 0.03;
            if (me.state == "charge") { H.heavy = now < relAt; H.x = 0; H.guard = false; return; }
            H.heavy = false;
            if (pAt >= 0 && now >= pAt) { T.parry++; pAt = -1; }
            H.x = 0;
            if (me.state == "parryok" || sim.Countering(me)) { H.guard = false; if (d <= reach + 0.05) { if (now > nt) { T.light++; nt = now + 0.08; } } else H.x = me.face; return; }
            if ((me.state == "light1" || me.state == "light2") && me.hitIdx > 0 && o.state == "hitstun") { T.light++; return; }
            if (useParry && me.state == "hitstun" && me.seq != gsSeq && o.state == "light2" && sim.ParryReady(me))
            {
                gsSeq = me.seq;
                pAt = Math.Max(now, NextHit(sim, o, DuelSim.Light2) - me.parryW * DuelSim.HS_PARRY_K * 0.5 + Gz() * SD);
            }
            if (useParry && me.state == "guardstun" && me.seq != gsSeq)
            {
                gsSeq = me.seq;
                if ((o.state == "light1" || o.state == "light2") && sim.ParryReady(me)) pAt = Math.Max(now, NextHit(sim, o, DuelSim.Mv(o.state)) - me.parryW * 0.5 + Gz() * SD);
            }
            // 重击：对手蓄力蓄得久（看得见）才打算格开；松手（刃口闪光）后过了反应时间 HRT ± 0.04 秒才按得下去
            if (useParry && o.state == "heavy" && o.seq != hvSeq)
            {
                hvSeq = o.seq;
                if (o.charge >= 0.6 && sim.ParryReady(me) && d < o.reach * DuelSim.Heavy.reachK + 1.2)
                {
                    double at = now - o.t / o.spd + Math.Max(0.12, HRT + 0.04 * Gz());
                    pAt = pAt >= 0 ? Math.Min(pAt, at) : at;
                }
            }
            var seen = sim.Seen(o.idx, 0.2);
            if (pAt >= 0) { H.guard = me.state != "hitstun"; return; }
            if (seen != null && seen.atk && d < o.reach + 0.8) { H.guard = true; return; }
            if (seen != null && o.seq == seen.seq && (seen.phase == "recovery" || seen.state == "parrywhiff" || seen.state == "parried") && d <= reach + 0.1 && sim.Actionable(me)) { H.guard = false; T.light++; return; }
            H.guard = false;
            if (me.rage >= 100 && d < reach + 1) { T.special++; return; }
            if (d > reach * 0.95) H.x = me.face;
            else if (now > nt && sim.Actionable(me))
            {
                if (kind == "heavy" && Rnd() < 0.6) { T.heavy++; H.heavy = true; relAt = now + (Rnd() < 0.5 ? 0.13 : 0.78 + Rnd() * 0.12); }
                else T.light++;
                nt = now + 0.45 + Rnd() * 0.3;
            }
        }
    }
}
