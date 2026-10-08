// 三国志II 霸王的大陆 · 必杀技目录（对应网页版 js/specials.js 的规则 / 数据部分；特写、菜单与特效属于战斗控制层，另行移植）
//
// 数据：Data/Generated/SpecialsData.g.cs（手写条目、机制表、兜底生成器词库，由导出工具生成）。
// 规则：Battle/BattleModel.cs（SpecialUsable / SpecialTargets / UseSpecial / AiSpecial）。
//
// 公开接口
//   Specials.Of(gen)              → 该武将的必杀技（已补全默认参数的 Special），按姓名缓存；没有手写条目时由兜底生成器
//                                   确定性地生成（Auto = true），并保证 (机制, 参数) 不与他人撞车
//   Specials.Of(name, war, intel, pol, culture)   同上（不必有 General 对象）
//   Specials.All(gens?)           → 全部条目（名单内武将 + 全部手写条目）。gens 缺省为 GameState.Current.generals，再缺省为当前剧本武将表
//   Specials.Check(gens?)         → 自检报告（总数、手写 / 生成、重名、参数撞车、数据问题、缺失）
//   Specials.Validate(entry, who) → 条目问题列表（空 = 合格）
//   Specials.Signature(sp)        → (kind, stat, 全部数值参数) 签名，用于唯一性校验
//   Specials.NeedsTarget(sp) / TargetSide(sp)（"enemy" | "ally" | "self"）
//   Specials.Explain(sp)          → 由数值生成的效果说明；Specials.Rules(sp) → 紧凑数值摘要；Specials.KindName(kind)
//   Specials.UiColor(sp | "#hex") → 界面文字用色（主色偏暗时提亮）
//   Specials.CultureOf(gen)、Specials.Fallback(gen)（调试用：忽略手写条目，直接生成）、Specials.ResetCache()
// 数值一律按 JS 的 double 计算（参数由 float 常数还原为十进制写法），使说明文字、签名与网页版逐字相同。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sanguo
{
    // 一招必杀技（补全默认参数后的结果；不可变）。数值参数统一为 double，整数参数为整数值；本机制没有的参数为 0。
    public sealed class Special
    {
        public string id, gen, name, kind, stat;
        public string color, fx, cry, lore, desc;
        public bool auto;                     // true：兜底生成器生成（没有手写条目）
        public List<string> problems = new List<string>();
        public double range, power, morale, pierce, confuse, turns, splash, dash, push, strikes, radius, burn,
                      heal, cure, atk, def, chance, drain, counter, count, move, dot;

        public double Get(string k)
        {
            switch (k)
            {
                case "range": return range; case "power": return power; case "morale": return morale; case "pierce": return pierce;
                case "confuse": return confuse; case "turns": return turns; case "splash": return splash; case "dash": return dash;
                case "push": return push; case "strikes": return strikes; case "radius": return radius; case "burn": return burn;
                case "heal": return heal; case "cure": return cure; case "atk": return atk; case "def": return def;
                case "chance": return chance; case "drain": return drain; case "counter": return counter; case "count": return count;
                case "move": return move; case "dot": return dot;
            }
            return 0;
        }
        internal void Set(string k, double v)
        {
            switch (k)
            {
                case "range": range = v; break; case "power": power = v; break; case "morale": morale = v; break; case "pierce": pierce = v; break;
                case "confuse": confuse = v; break; case "turns": turns = v; break; case "splash": splash = v; break; case "dash": dash = v; break;
                case "push": push = v; break; case "strikes": strikes = v; break; case "radius": radius = v; break; case "burn": burn = v; break;
                case "heal": heal = v; break; case "cure": cure = v; break; case "atk": atk = v; break; case "def": def = v; break;
                case "chance": chance = v; break; case "drain": drain = v; break; case "counter": counter = v; break; case "count": count = v; break;
                case "move": move = v; break; case "dot": dot = v; break;
            }
        }
        public int Range { get { return (int)range; } }
        public int Radius { get { return (int)radius; } }
        public int Turns { get { return (int)turns; } }
        public int Strikes { get { return (int)strikes; } }
        public int Count { get { return (int)count; } }
        public int Burn { get { return (int)burn; } }
        public int Move { get { return (int)move; } }
    }

    // 一份自检报告（Specials.Check）
    public sealed class SpecialsReport
    {
        public int total, hand, auto;
        public List<KeyValuePair<string, List<string>>> dupNames = new List<KeyValuePair<string, List<string>>>();
        public List<KeyValuePair<string, List<string>>> dupSigs = new List<KeyValuePair<string, List<string>>>();
        public List<string> problems = new List<string>();
        public List<string> missing = new List<string>();
        public bool ok;
    }

    public static class Specials
    {
        // ------------------------------------------------------------ 机制表 --
        // 由 float 常数还原成十进制写法的 double（2.4f → 2.4），与 JS 的数值一致
        static double D(float f) { return (double)(decimal)f; }

        sealed class KindInfo
        {
            public SpecialKindDef def;
            public string[] keys;
            public double[] dflt, min, max, step;
        }
        static Dictionary<string, KindInfo> kinds;
        static KindInfo K(string kind)
        {
            if (kinds == null)
            {
                var d = new Dictionary<string, KindInfo>();
                foreach (var k in SpecialsData.Kinds)
                    d[k.Kind] = new KindInfo
                    {
                        def = k, keys = k.Params, dflt = k.Def.Select(D).ToArray(), min = k.Min.Select(D).ToArray(),
                        max = k.Max.Select(D).ToArray(), step = k.Step.Select(D).ToArray(),
                    };
                kinds = d;
            }
            KindInfo r; return kind != null && kinds.TryGetValue(kind, out r) ? r : null;
        }
        public static bool KindExists(string kind) { return K(kind) != null; }
        public static bool HasParam(string kind, string param) { var k = K(kind); return k != null && Array.IndexOf(k.keys, param) >= 0; }
        public static string[] KindKeys { get { return SpecialsData.Kinds.Select(k => k.Kind).ToArray(); } }

        public static readonly double FloodRiver = D(SpecialsData.FloodRiver), FloodDry = D(SpecialsData.FloodDry);
        public static readonly double ChargeCap = D(SpecialsData.ChargeCap), AreaTotal = D(SpecialsData.AreaTotal);

        static readonly HashSet<string> Nanman = new HashSet<string>(SpecialsData.Nanman);
        public static string CultureOf(General gen) { return gen == null ? "han" : CultureOf(gen.name, gen.culture); }
        static string CultureOf(string name, string culture) { return !string.IsNullOrEmpty(culture) ? culture : (name != null && Nanman.Contains(name) ? "nanman" : "han"); }

        // ------------------------------------------------------------ 小工具（JS 语义）--
        // Math.round：四舍五入（.5 向 +∞）
        static double JsRound(double x) { double r = Math.Floor(x); if (x - r >= 0.5) r += 1; return r; }
        static double Fix(double v) { return JsRound(v * 1000) / 1000; }
        static double Quant(double v, double step) { return step >= 1 ? JsRound(v) : JsRound(v / step) * step; }
        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double Clamp01(double v) { return Clamp(v, 0, 1); }
        // String(number)：最短往返写法
        internal static string JsNum(double v)
        {
            if (v == 0) return "0";
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
        static string Fmt(double v) { return JsNum(Fix(v)); }
        static string Pct(double v) { return JsNum(JsRound(v * 100)) + "%"; }
        static string Signed(double v) { return v > 0 ? "+" + JsNum(v) : v < 0 ? "−" + JsNum(Math.Abs(v)) : "0"; }

        // FNV-1a 32 位（按 UTF-16 码元，同 JS charCodeAt）
        static uint Hash(string str)
        {
            uint h = 0x811c9dc5u;
            unchecked { foreach (char c in str) { h ^= c; h *= 0x01000193u; } }
            return h;
        }

        static double[] HexToRgb(string hex)
        {
            var h = hex.Replace("#", "");
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            int n = int.Parse(h.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new[] { ((n >> 16) & 255) / 255.0, ((n >> 8) & 255) / 255.0, (n & 255) / 255.0 };
        }
        static string RgbToHex(double r, double g, double b)
        {
            Func<double, string> c = v => ((int)JsRound(Clamp01(v) * 255)).ToString("x2");
            return "#" + c(r) + c(g) + c(b);
        }
        static double[] HexToHsl(string hex)
        {
            var rgb = HexToRgb(hex);
            double r = rgb[0], g = rgb[1], b = rgb[2];
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2;
            if (mx == mn) return new[] { 0, 0, l };
            double d = mx - mn, s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            double h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return new[] { h / 6, s, l };
        }
        static string HslToHex(double h, double s, double l)
        {
            Func<double, double, double, double> f = (p, q, t) =>
            {
                t = (t % 1 + 1) % 1;
                return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            };
            if (s == 0) return RgbToHex(l, l, l);
            double qq = l < 0.5 ? l * (1 + s) : l + s - l * s, pp = 2 * l - qq;
            return RgbToHex(f(pp, qq, h + 1.0 / 3), f(pp, qq, h), f(pp, qq, h - 1.0 / 3));
        }
        static bool IsHex(string s) { if (s == null || s.Length != 7 || s[0] != '#') return false; for (int i = 1; i < 7; i++) if (Uri.IsHexDigit(s[i]) == false) return false; return true; }

        // 界面文字用色：招式主色偏暗时提亮（深色面板上保持可读），特效仍用原色
        public static string UiColor(Special sp) { return UiColor(sp != null && sp.color != null ? sp.color : "#f3c969"); }
        public static string UiColor(string hex)
        {
            if (!IsHex(hex)) return "#f3c969";
            var hsl = HexToHsl(hex);
            return hsl[2] >= 0.62 ? hex : HslToHex(hsl[0], Math.Min(1, hsl[1] * 1.05), 0.68);
        }

        // ============================================================ 原始条目 --
        // 手写条目或兜底生成的条目（数值参数按书写顺序）
        public sealed class Raw
        {
            public string name, kind, stat, fx, color, cry, desc, lore;
            public List<string> keys = new List<string>();
            public List<double> values = new List<double>();
            public bool Has(string k) { return keys.IndexOf(k) >= 0; }
            public double Get(string k) { int i = keys.IndexOf(k); return i >= 0 ? values[i] : double.NaN; }
            public void Put(string k, double v) { int i = keys.IndexOf(k); if (i >= 0) values[i] = v; else { keys.Add(k); values.Add(v); } }
            public Raw Clone() { var r = (Raw)MemberwiseClone(); r.keys = new List<string>(keys); r.values = new List<double>(values); return r; }
            public static Raw From(SpecialEntry e)
            {
                var r = new Raw { name = e.Name, kind = e.Kind, stat = e.Stat, fx = e.Fx, color = e.Color, cry = e.Cry, desc = e.Desc, lore = e.Lore };
                for (int i = 0; i < e.Keys.Length; i++) { r.keys.Add(e.Keys[i]); r.values.Add(D(e.Values[i])); }
                return r;
            }
        }

        // ============================================================ 校验与补全 --
        public static List<string> Validate(SpecialEntry e, string who) { return Validate(e == null ? null : Raw.From(e), who); }
        public static List<string> Validate(Raw e, string who)
        {
            var outList = new List<string>();
            string tag = who != null && who.Length > 0 ? who + "：" : "";
            if (e == null) { outList.Add(tag + "条目不是对象"); return outList; }
            if (e.name == null || e.name.Trim().Length == 0) outList.Add(tag + "缺少招式名 name");
            else
            {
                if (e.name.IndexOf('·') >= 0) outList.Add(tag + "招式名不得含「·」（留给兜底生成的名称）");
                if (e.name.Length > 7) outList.Add(tag + "招式名过长（" + e.name.Length + " 字）");
            }
            var k = K(e.kind);
            if (k == null) { outList.Add(tag + "未知机制 kind = " + e.kind); return outList; }
            Func<string, bool> hasText = v => v != null && v.Trim().Length > 0;
            if (!hasText(e.desc) && !hasText(e.lore)) outList.Add(tag + "缺少中文说明 desc（或典故 lore）");
            if (e.lore != null)
            {
                var t = e.lore.Trim();
                if (t.Length == 0 || "。！？」".IndexOf(t[t.Length - 1]) < 0) outList.Add(tag + "lore 应为完整的一句话（以句号结尾）");
            }
            if (e.color != null && !IsHex(e.color)) outList.Add(tag + "颜色应为 #rrggbb：" + e.color);
            if (e.fx != null && Array.IndexOf(SpecialsData.FxStyles, e.fx) < 0) outList.Add(tag + "未知特效 fx = " + e.fx);
            if (e.stat != null && Array.IndexOf(SpecialsData.Stats, e.stat) < 0) outList.Add(tag + "stat 应为 war / intel / pol：" + e.stat);
            for (int i = 0; i < e.keys.Count; i++)
            {
                string key = e.keys[i];
                int pi = Array.IndexOf(k.keys, key);
                if (pi < 0) { outList.Add(tag + k.def.Label + "（" + e.kind + "）没有参数 " + key); continue; }
                double v = e.values[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) { outList.Add(tag + "参数 " + key + " 应为数字：" + JsNum(v)); continue; }
                if (v < k.min[pi] - 1e-9 || v > k.max[pi] + 1e-9) outList.Add(tag + "参数 " + key + " = " + JsNum(v) + " 超出范围 [" + JsNum(k.min[pi]) + ", " + JsNum(k.max[pi]) + "]");
                if (k.step[pi] >= 1 && JsRound(v) != v) outList.Add(tag + "参数 " + key + " 应为整数：" + JsNum(v));
            }
            if (e.kind == "rampage")
            {
                int si = Array.IndexOf(k.keys, "strikes"), pwi = Array.IndexOf(k.keys, "power");
                double s = e.Has("strikes") && !double.IsNaN(e.Get("strikes")) ? e.Get("strikes") : k.dflt[si];
                double pw = e.Has("power") && !double.IsNaN(e.Get("power")) ? e.Get("power") : k.dflt[pwi];
                if (s * pw > 2.65) outList.Add(tag + "连斩总威力 strikes×power = " + JsNum(Fix(s * pw)) + " 过高（建议 ≤ 2.5）");
            }
            return outList;
        }

        // 补全默认参数并夹到范围内
        static Special Normalize(Raw e, string owner, bool auto)
        {
            var k = K(e.kind) ?? K("smite");
            string kind = K(e.kind) != null ? e.kind : "smite";
            var sp = new Special
            {
                id = "sp:" + owner, gen = owner, name = !string.IsNullOrEmpty(e.name) ? e.name : owner + "·绝技", kind = kind,
                stat = e.stat != null && Array.IndexOf(SpecialsData.Stats, e.stat) >= 0 ? e.stat : k.def.Stat,
            };
            for (int i = 0; i < k.keys.Length; i++)
            {
                double v = e.Get(k.keys[i]);
                if (double.IsNaN(v) || double.IsInfinity(v)) v = k.dflt[i];
                v = Clamp(v, k.min[i], k.max[i]);
                sp.Set(k.keys[i], k.step[i] >= 1 ? JsRound(v) : Fix(v));
            }
            sp.color = IsHex(e.color) ? e.color.ToLowerInvariant() : k.def.Color;
            sp.fx = e.fx != null && Array.IndexOf(SpecialsData.FxStyles, e.fx) >= 0 ? e.fx : k.def.Fx;
            sp.cry = e.cry ?? "";
            // 说明：手写 desc 优先；否则「典故 lore + 由数值生成的效果说明」，效果说明永远与数值一致
            sp.lore = e.lore != null ? e.lore.Trim() : "";
            sp.desc = e.desc != null && e.desc.Trim().Length > 0 ? e.desc : sp.lore + Explain(sp);
            sp.auto = auto;
            sp.problems = auto ? new List<string>() : Validate(e, owner);
            return sp;
        }

        public static string Signature(Special sp)
        {
            var k = K(sp.kind);
            if (k == null) return sp.kind;
            var keys = k.keys.ToArray();
            Array.Sort(keys, string.CompareOrdinal);
            return sp.kind + "|" + sp.stat + "|" + string.Join(",", keys.Select(x => x + "=" + Fmt(sp.Get(x))).ToArray());
        }

        // ============================================================ 说明文字 --
        public static string KindName(string kind) { var k = K(kind); return k != null ? k.def.Label : kind; }
        public static string TargetSide(Special sp) { var k = sp != null ? K(sp.kind) : null; return k != null ? k.def.Target : "enemy"; }
        public static bool NeedsTarget(Special sp) { return TargetSide(sp) != "self"; }

        public static string Explain(Special sp)
        {
            Func<double, string> near = r => r <= 1 ? "相邻" : " " + JsNum(r) + " 格内";
            string mor = sp.morale < 0 ? "，敌军士气 " + Signed(sp.morale) : "";
            string conf = sp.confuse > 0 ? "，并可能陷入混乱" : "";
            string pierce = sp.pierce != 0 ? "（无视地形）" : "";
            // 伤害说法随所依能力而变：武力系以普通攻击为基准，智力 / 政治系以计策为基准
            bool war = (sp.stat ?? "war") == "war";
            Func<double, string> dmg = x => war ? "约 " + Fmt(x) + " 倍普通攻击的伤害" : "计策级伤害 ×" + Fmt(x);
            Func<double, string> amt = x => war ? "约 " + Fmt(x) + " 倍" : " ×" + Fmt(x) + " 的";
            const string cap = "（同时命中多支时总伤害封顶）";
            string statName = sp.stat == "intel" ? "智力" : sp.stat == "pol" ? "政治" : "武力";
            string scaled = "（" + statName + " 100 时的数值，每低 40 点弱一成）";
            Func<double, string> N = JsNum;
            switch (sp.kind)
            {
                case "smite": return "对" + near(sp.range) + "一支敌军全力一击，造成" + dmg(sp.power) + pierce + mor + conf + "。";
                case "cleave": return "横扫相邻敌军：主目标受" + dmg(sp.power) + "，其余相邻敌军受" + amt(sp.power * sp.splash) + cap + mor + conf + "。";
                case "charge":
                    return "沿直线冲向 " + N(sp.range) + " 格内的敌军（途中须无阻挡），造成" + dmg(sp.power) + "，每冲过一格再增 " + Pct(sp.dash) +
                        (sp.push != 0 ? "，并将其击退一格（退路受阻或敌军据守本城、城门时改为伤害 +25%）" : "") + "，加成合计至多 +" + Pct(ChargeCap - 1) + mor + "。";
                case "rampage": return "在" + (sp.range <= 1 ? "相邻" : " " + N(sp.range) + " 格内的") + "敌军之间往来冲杀，连斩 " + N(sp.strikes) + " 次，每斩" + dmg(sp.power) + "（目标溃散则转斩下一支）" + mor + "。";
                case "volley":
                    return "向 " + N(sp.range) + " 格内的敌军发射，造成" + dmg(sp.power) + pierce +
                        (sp.radius > 0 ? "，周围一格的敌军受" + amt(sp.power * sp.splash) + "溅射" + cap : "") + mor + "。";
                case "blaze":
                    return "对 " + N(sp.range) + " 格内的敌军纵火，造成" + dmg(sp.power) + (sp.radius > 0 ? "，周围 " + N(sp.radius) + " 格的敌军受" + amt(sp.power * sp.splash) + "火焚" + cap : "") +
                        "（林地 ×1.5、河上 ×0.5）" + (sp.burn > 0 ? "，燃起 " + N(sp.burn) + " 日大火" : "") + mor + "。";
                case "storm":
                    return N(sp.range) + " 格内任选一处，周围 " + N(sp.radius) + " 格内的所有敌军受天候重创（" + dmg(sp.power) + "）" + cap +
                        (sp.burn > 0 ? "，脚下燃起 " + N(sp.burn) + " 日大火" : "") + mor + conf + "。";
                case "flood": return "引水灌向 " + N(sp.range) + " 格内一处，周围 " + N(sp.radius) + " 格的敌军受水攻（" + dmg(sp.power) + "，近河 ×" + N(FloodRiver) + "、否则 ×" + N(FloodDry) + "）" + cap + "，并扑灭火势" + mor + conf + "。";
                case "roar": return "一声怒吼，周围 " + N(sp.radius) + " 格内的敌军" + (sp.power > 0 ? "受震伤、" : "") + "士气 " + Signed(sp.morale) + "（武力 100 时的降幅，每低 40 点弱一成）" + conf + "。";
                case "rally":
                    return "鼓舞周围 " + N(sp.radius) + " 格内的友军：士气 +" + N(sp.morale) + (sp.heal > 0 ? "，回复 " + Pct(sp.heal) + " 最大兵力" : "") +
                        (sp.cure != 0 ? "，解除混乱" : "") + (sp.atk > 1 ? "，攻击 +" + Pct(sp.atk - 1) + "（" + N(sp.turns) + " 日）" : "") + scaled + "。";
                case "command":
                    return "号令周围 " + N(sp.radius) + " 格内的友军：攻击 +" + Pct(sp.atk - 1) + (sp.def > 1 ? "、防御 +" + Pct(sp.def - 1) : "") +
                        "，持续 " + N(sp.turns) + " 日" + (sp.morale > 0 ? "，士气 +" + N(sp.morale) : "") + scaled + "。";
                case "scheme":
                    return "对 " + N(sp.range) + " 格内一处施展奇谋，" + (sp.radius > 0 ? "周围 " + N(sp.radius) + " 格内的" : "") + "敌军可能陷入混乱 " + N(sp.turns) +
                        " 日（成功率随智力差变化）" + (sp.power > 0 ? "，并受少量伤害" : "") + mor + "。";
                case "drain": return "击溃" + near(sp.range) + "敌军，造成" + dmg(sp.power) + "，并将约 " + Pct(sp.drain) + " 的伤亡收编为己方兵力（随政治增减）" + mor + "。";
                case "fortify":
                    return (sp.radius > 0 ? "自身与周围 " + N(sp.radius) + " 格内的友军" : "自身") + "防御 +" + Pct(sp.def - 1) +
                        (sp.counter > 1 ? "、反击 +" + Pct(sp.counter - 1) : "") + "，持续 " + N(sp.turns) + " 日" + (sp.morale > 0 ? "，士气 +" + N(sp.morale) : "") + scaled + "。";
                case "haste": return "令周围 " + N(sp.radius) + " 格内至多 " + N(sp.count) + " 支已行动的友军再次行动" + (sp.move > 0 ? "，当日机动力 +" + N(sp.move) : "") + "。";
                case "heal":
                    return "为 " + (sp.range > 0 ? N(sp.range) + " 格内" : "自身") + (sp.radius > 0 ? "一支友军及其周围 " + N(sp.radius) + " 格的友军" : "一支友军") +
                        "疗伤，回复 " + Pct(sp.heal) + " 最大兵力" + (sp.morale > 0 ? "、士气 +" + N(sp.morale) : "") + scaled + (sp.cure != 0 ? "，并解除混乱" : "") + "。";
                case "assassinate":
                    return (sp.stat == "intel" ? "遣死士潜入 " : "潜入 ") + N(sp.range) + " 格内的敌阵刺杀敌将，得手则该部当即溃散（基础成功率 " + Pct(sp.chance) + "，随" + (sp.stat == "intel" ? "智力" : "武力") + "差变化，主将减半）；" +
                        (sp.power > 0 ? "失手仍造成" + dmg(sp.power) : "失手则无功而返") + mor + "。";
                case "poison": return "向 " + N(sp.range) + " 格内一处施毒" + (sp.radius > 0 ? "，周围一格的敌军同时中毒" : "") + "：每日损兵约 " + Pct(sp.dot) + "，持续 " + N(sp.turns) + " 日" + mor + "。";
                default: return "";
            }
        }

        // 紧凑数值摘要（「单体重击 · 射程1 · 威力×2.4 · 士气−20 · 无视地形 · 依武力」）
        public static string Rules(Special sp)
        {
            if (sp == null) return "";
            var k = K(sp.kind);
            var parts = new List<string> { k != null ? k.def.Label : sp.kind };
            string statName = sp.stat == "war" ? "武力" : sp.stat == "intel" ? "智力" : sp.stat == "pol" ? "政治" : null;
            Func<string, bool> P = key => k != null && Array.IndexOf(k.keys, key) >= 0;
            if (P("range")) parts.Add(sp.range == 0 ? "自身" : "射程" + JsNum(sp.range));
            if (P("radius") && sp.radius > 0) parts.Add((k.def.Target == "self" ? "周围" : "范围") + JsNum(sp.radius));
            if (P("strikes")) parts.Add(JsNum(sp.strikes) + "连斩");
            if (P("power") && sp.power > 0) parts.Add("威力×" + Fmt(sp.power));
            if (P("splash") && (sp.kind == "cleave" || sp.radius > 0)) parts.Add("溅射×" + Fmt(sp.splash));
            if (P("dash") && sp.dash > 0) parts.Add("每格+" + Pct(sp.dash));
            if (sp.push != 0) parts.Add("击退");
            if (sp.pierce != 0) parts.Add("无视地形");
            if (P("chance")) parts.Add("成功率" + Pct(sp.chance));
            if (P("heal") && sp.heal > 0) parts.Add("回复" + Pct(sp.heal));
            if (P("atk") && sp.atk > 1) parts.Add("攻+" + Pct(sp.atk - 1));
            if (P("def") && sp.def > 1) parts.Add("防+" + Pct(sp.def - 1));
            if (P("counter") && sp.counter > 1) parts.Add("反击+" + Pct(sp.counter - 1));
            if (P("drain")) parts.Add("收编" + Pct(sp.drain));
            if (P("dot")) parts.Add("每日" + Pct(sp.dot));
            if (P("count")) parts.Add("再动" + JsNum(sp.count));
            if (P("move") && sp.move > 0) parts.Add("机动+" + JsNum(sp.move));
            if (P("burn") && sp.burn > 0) parts.Add("燃烧" + JsNum(sp.burn) + "日");
            if (P("confuse") && sp.confuse > 0) parts.Add("混乱" + Pct(sp.confuse));
            if (P("morale") && sp.morale != 0) parts.Add("士气" + Signed(sp.morale));
            if (sp.cure != 0) parts.Add("解除混乱");
            if (P("turns") && (sp.kind == "scheme" || sp.kind == "poison" || sp.kind == "command" || sp.kind == "fortify" || sp.atk > 1 || sp.confuse > 0)) parts.Add(JsNum(sp.turns) + "日");
            if (statName != null) parts.Add("依" + statName);
            return string.Join(" · ", parts.ToArray());
        }

        // ============================================================ 兜底生成器 --
        // 为没有手写条目的武将确定性地生成一招：机制取决于能力倾向，参数由能力强弱 + 姓名哈希决定，
        // 名称为「姓名·称号+招式」（含姓名，因此不会与他人或手写条目重名）。
        static double Lerp(double a, double b, double t) { return a + (b - a) * Clamp01(t); }
        static Raw Gen(string kind, double s, SeededRandom r)
        {
            var e = new Raw();
            Action<string, double> P = (k, v) => e.Put(k, v);
            switch (kind)
            {
                case "smite":
                    {
                        int range = r.Next(5) == 0 ? 2 : 1;
                        P("range", range); P("power", Lerp(1.9, 2.35, s) - (range - 1) * 0.15); P("morale", -(8 + r.Next(13)));
                        P("pierce", r.Next(3) == 0 ? 1 : 0); P("confuse", r.Next(5) == 0 ? 0.15 + r.Next(3) * 0.05 : 0); P("turns", 1);
                        break;
                    }
                case "cleave":
                    P("power", Lerp(1.7, 1.95, s)); P("splash", 0.5 + r.Next(6) * 0.05); P("morale", -(6 + r.Next(10))); P("confuse", r.Next(6) == 0 ? 0.15 : 0); P("turns", 1);
                    break;
                case "charge":
                    P("range", 2 + r.Next(3)); P("power", Lerp(1.6, 1.85, s)); P("dash", 0.08 + r.Next(5) * 0.01); P("push", r.Next(4) == 0 ? 0 : 1); P("morale", -(8 + r.Next(10)));
                    break;
                case "rampage":
                    {
                        int strikes = 3 + r.Next(4);
                        P("range", 1 + r.Next(2) + (strikes >= 5 ? 1 : 0)); P("strikes", strikes); P("power", Lerp(2.0, 2.35, s) / strikes); P("morale", -(4 + r.Next(7)));
                        break;
                    }
                case "volley":
                    {
                        int radius = r.Next(3) == 0 ? 1 : 0;
                        P("range", 3 + r.Next(3)); P("power", radius != 0 ? Lerp(1.4, 1.65, s) : Lerp(1.8, 2.2, s)); P("radius", radius);
                        P("splash", 0.5 + r.Next(5) * 0.05); P("pierce", r.Next(4) == 0 ? 1 : 0); P("morale", -(4 + r.Next(9)));
                        break;
                    }
                case "blaze":
                    {
                        int radius = r.Next(4) == 0 ? 0 : 1;
                        P("range", 3 + r.Next(2)); P("radius", radius); P("power", radius != 0 ? Lerp(1.25, 1.45, s) : Lerp(1.5, 1.7, s));
                        P("splash", 0.6 + r.Next(5) * 0.05); P("burn", 1 + r.Next(3)); P("morale", -(6 + r.Next(8)));
                        break;
                    }
                case "storm":
                    P("range", 4 + r.Next(3)); P("radius", r.Next(3) == 0 ? 1 : 2); P("power", Lerp(0.65, 0.85, s)); P("burn", r.Next(4) == 0 ? 1 : 0);
                    P("confuse", r.Next(3) == 0 ? 0.15 + r.Next(3) * 0.05 : 0); P("turns", 1); P("morale", -(8 + r.Next(8)));
                    break;
                case "flood":
                    P("range", 3 + r.Next(3)); P("radius", r.Next(4) == 0 ? 2 : 1); P("power", Lerp(0.9, 1.1, s)); P("confuse", r.Next(4) == 0 ? 0.15 : 0); P("turns", 1); P("morale", -(10 + r.Next(8)));
                    break;
                case "roar":
                    P("radius", r.Next(4) == 0 ? 1 : 2); P("power", Lerp(0.3, 0.55, s)); P("morale", -JsRound(Lerp(18, 28, s)) - r.Next(4)); P("confuse", Lerp(0.2, 0.4, s)); P("turns", 1);
                    break;
                case "rally":
                    P("radius", 1 + r.Next(2)); P("morale", JsRound(Lerp(18, 30, s)) + r.Next(4)); P("heal", Lerp(0.04, 0.1, s)); P("cure", 1); P("atk", r.Next(3) == 0 ? 1.1 : 1); P("turns", 2);
                    break;
                case "command":
                    P("radius", 1 + r.Next(2)); P("atk", Lerp(1.15, 1.28, s)); P("def", 1 + r.Next(3) * 0.05); P("turns", 2); P("morale", 5 + r.Next(8));
                    break;
                case "scheme":
                    P("range", 3 + r.Next(3)); P("radius", r.Next(3) == 0 ? 0 : 1); P("chance", Lerp(0.45, 0.6, s)); P("turns", r.Next(3) == 0 ? 1 : 2);
                    P("morale", -(6 + r.Next(8))); P("power", r.Next(3) == 0 ? 0.2 : 0);
                    break;
                case "drain":
                    P("range", 1); P("power", Lerp(1.6, 1.95, s)); P("drain", 0.35 + r.Next(5) * 0.05); P("morale", -(8 + r.Next(8)));
                    break;
                case "fortify":
                    P("radius", r.Next(3) == 0 ? 0 : 1); P("def", Lerp(1.25, 1.45, s)); P("counter", 1 + r.Next(5) * 0.1); P("turns", 2); P("morale", 6 + r.Next(8));
                    break;
                case "haste":
                    P("radius", 1 + r.Next(2)); P("count", s > 0.6 ? 2 : 1 + r.Next(2)); P("move", r.Next(2)); P("morale", 3 + r.Next(6));
                    break;
                case "heal":
                    P("range", 1 + r.Next(3)); P("radius", r.Next(3) == 0 ? 0 : 1); P("heal", Lerp(0.12, 0.22, s)); P("morale", 6 + r.Next(8)); P("cure", 1);
                    break;
                case "assassinate":
                    P("range", 1 + r.Next(2)); P("chance", Lerp(0.2, 0.32, s)); P("power", 0.4 + r.Next(4) * 0.1); P("morale", -(10 + r.Next(10)));
                    break;
                case "poison":
                    P("range", 3 + r.Next(2)); P("radius", r.Next(2)); P("power", Lerp(0.25, 0.45, s)); P("dot", Lerp(0.04, 0.07, s)); P("turns", 3); P("morale", -(5 + r.Next(8)));
                    break;
            }
            return e;
        }
        static double Strength(int v) { return Clamp01((v - 45) / 55.0); }

        static string PickKind(int W, int I, int Pl, SeededRandom r)
        {
            string[] pool;
            if (W >= I + 10 && W >= Pl) pool = W >= 85 ? new[] { "smite", "cleave", "charge", "rampage", "volley", "roar", "drain", "smite", "charge" }
                : new[] { "smite", "cleave", "charge", "volley", "roar", "drain", "assassinate", "fortify" };
            else if (Math.Abs(W - I) < 10 && W >= 60 && I >= 60) pool = new[] { "charge", "volley", "command", "scheme", "fortify", "haste", "blaze" };
            else if (I >= Pl - 5) pool = I >= 85 ? new[] { "blaze", "storm", "flood", "scheme", "poison", "assassinate", "blaze", "scheme" }
                : new[] { "blaze", "scheme", "poison", "flood", "heal", "haste" };
            else pool = new[] { "rally", "command", "heal", "fortify", "haste" };
            return pool[r.Next(pool.Length)];
        }

        static string[] Bank(string key) { int i = Array.IndexOf(SpecialsData.EpithetKeys, key); return i >= 0 ? SpecialsData.Epithets[i] : null; }

        static Raw FallbackRaw(string name, int war, int intel, int pol, string cultureIn)
        {
            string culture = CultureOf(name, cultureIn);
            var r = new SeededRandom(Hash(name + "|" + culture + "|" + war + "/" + intel + "/" + pol));
            string kind = PickKind(war, intel, pol, r);
            var k = K(kind);
            // 机制默认能力之外：政治系招式由智力更高者施展时依智力；暗杀由谋士施展时依智力（鸩毒、行刺）
            string stat = k.def.Stat;
            if (k.def.Stat == "pol" && intel > pol + 10) stat = "intel";
            if (kind == "assassinate" && intel > war) stat = "intel";
            int sv = stat == "intel" ? intel : stat == "pol" ? pol : war;
            var e = Gen(kind, Strength(sv), r);
            // 细微的个人差异（让 (机制, 参数) 组合不易相同）
            if (e.Has("power") && e.Get("power") > 0) e.Put("power", e.Get("power") + (r.Next(9) - 4) * 0.01);
            else if (e.Has("chance")) e.Put("chance", e.Get("chance") + (r.Next(7) - 3) * 0.01);
            else if (e.Has("def")) e.Put("def", e.Get("def") + (r.Next(7) - 3) * 0.01);
            else if (e.Has("atk")) e.Put("atk", e.Get("atk") + (r.Next(7) - 3) * 0.01);
            for (int i = 0; i < e.keys.Count; i++)
            {
                int pi = Array.IndexOf(k.keys, e.keys[i]);
                if (pi < 0) continue;
                double v = e.values[i];
                e.values[i] = k.step[pi] >= 1 ? JsRound(Clamp(v, k.min[pi], k.max[pi])) : Fix(Clamp(Quant(v, k.step[pi]), k.min[pi], k.max[pi]));
            }
            var bank = culture == "han" ? Bank(stat) : (Bank(culture) ?? Bank(stat));
            var nouns = SpecialsData.Nouns[Array.IndexOf(SpecialsData.NounKeys, kind)];
            string noun = nouns[r.Next(nouns.Length)];
            string epi = bank[r.Next(bank.Length)];
            if (epi.Length + noun.Length > 5) epi = epi.Substring(0, 1);
            var hsl = HexToHsl(k.def.Color);
            int r1 = r.Next(41), r2 = r.Next(4), r3 = r.Next(9);
            string color = HslToHex(hsl[0] + (r1 - 20) / 360.0, Clamp(hsl[1] * (0.85 + r2 * 0.05), 0.35, 1), Clamp(hsl[2] + (r3 - 4) * 0.012, 0.45, 0.8));
            string fx = k.def.Fx;
            if (kind == "volley" && e.Get("radius") == 0) fx = "arrow";
            if (kind == "storm" && e.Get("burn") > 0) fx = "wind";
            var entry = e.Clone();
            entry.name = name + "·" + epi + noun; entry.kind = kind; entry.stat = stat; entry.color = color; entry.fx = fx;
            int fi = Array.IndexOf(SpecialsData.FlavorKeys, stat);
            entry.desc = (fi >= 0 ? SpecialsData.Flavors[fi] : "") + Explain(Normalize(entry, name, true));
            return entry;
        }

        // ============================================================ 解析与缓存 --
        static readonly Dictionary<string, Special> cache = new Dictionary<string, Special>();   // 武将名 → 条目
        static readonly Dictionary<string, string> sigOwner = new Dictionary<string, string>();   // 签名 → 武将名
        static bool synced;
        static void Sync()
        {
            if (synced) return;
            synced = true;
            cache.Clear(); sigOwner.Clear();
            foreach (var e in SpecialsData.Entries)
            {
                var sp = Normalize(Raw.From(e), e.Gen, false);
                cache[e.Gen] = sp;
                var sig = Signature(sp);
                if (!sigOwner.ContainsKey(sig)) sigOwner[sig] = e.Gen;
            }
        }
        // 清空缓存（兜底生成的去重状态随之重置；测试用）
        public static void ResetCache() { synced = false; Sync(); }

        public static Special Of(General gen) { return gen == null ? null : Of(gen.name, gen.war, gen.intel, gen.pol, gen.culture); }
        public static Special Of(string name, int war, int intel, int pol, string culture = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Sync();
            Special hit;
            if (cache.TryGetValue(name, out hit)) return hit;
            var e = FallbackRaw(name, war, intel, pol, culture);
            var sp = Normalize(e, name, true);
            // 与他人（含手写条目）的 (机制, 参数) 撞车时，按确定的步长微调威力 / 成功率直到唯一
            var k = K(sp.kind);
            string tweak = new[] { "power", "chance", "def", "atk", "heal", "morale" }.FirstOrDefault(x => Array.IndexOf(k.keys, x) >= 0 && (x != "power" || sp.power > 0)) ?? "morale";
            string owner;
            for (int i = 1; i < 60 && sigOwner.TryGetValue(Signature(sp), out owner) && owner != name; i++)
            {
                int pi = Array.IndexOf(k.keys, tweak);
                double step = (k.step[pi] >= 1 ? 1 : 0.01) * (i % 2 != 0 ? Math.Ceiling(i / 2.0) : -Math.Ceiling(i / 2.0));
                double v = Clamp(e.Get(tweak) + step, k.min[pi], k.max[pi]);
                var e2 = e.Clone();
                e2.Put(tweak, k.step[pi] >= 1 ? JsRound(v) : Fix(v));
                sp = Normalize(e2, name, true);
            }
            cache[name] = sp;
            sigOwner[Signature(sp)] = name;
            return sp;
        }
        // 调试用：忽略手写条目，直接生成（不进缓存）
        public static Special Fallback(General gen) { return Normalize(FallbackRaw(gen.name, gen.war, gen.intel, gen.pol, gen.culture), gen.name, true); }

        // 名单：给定武将，或 GameState.Current.generals，或当前剧本的武将表（"姓名|武|智|政|势力|…"）
        static IEnumerable<General> Roster(IEnumerable<General> gens)
        {
            if (gens != null) return gens;
            var g = GameState.Current;
            if (g != null && g.generals != null && g.generals.Count > 0) return g.generals;
            return Scenarios.Current.Generals.Select(line =>
            {
                var p = line.Split('|');
                return new General { name = p[0], war = int.Parse(p[1], CultureInfo.InvariantCulture), intel = int.Parse(p[2], CultureInfo.InvariantCulture), pol = int.Parse(p[3], CultureInfo.InvariantCulture) };
            }).ToList();
        }

        public static List<Special> All(IEnumerable<General> gens = null)
        {
            Sync();
            var seen = new HashSet<string>();
            var outList = new List<Special>();
            foreach (var g in Roster(gens)) { if (g == null || string.IsNullOrEmpty(g.name) || !seen.Add(g.name)) continue; outList.Add(Of(g)); }
            foreach (var e in SpecialsData.Entries) if (seen.Add(e.Gen)) outList.Add(cache[e.Gen]);
            return outList;
        }

        public static SpecialsReport Check(IEnumerable<General> gens = null)
        {
            var list = All(gens);
            var byName = new Dictionary<string, List<string>>(); var bySig = new Dictionary<string, List<string>>();
            var nameOrder = new List<string>(); var sigOrder = new List<string>();
            foreach (var sp in list)
            {
                if (!byName.ContainsKey(sp.name)) { byName[sp.name] = new List<string>(); nameOrder.Add(sp.name); }
                byName[sp.name].Add(sp.gen);
                var s = Signature(sp);
                if (!bySig.ContainsKey(s)) { bySig[s] = new List<string>(); sigOrder.Add(s); }
                bySig[s].Add(sp.gen);
            }
            var rep = new SpecialsReport { total = list.Count };
            foreach (var n in nameOrder) if (byName[n].Count > 1) rep.dupNames.Add(new KeyValuePair<string, List<string>>(n, byName[n]));
            foreach (var s in sigOrder) if (bySig[s].Count > 1) rep.dupSigs.Add(new KeyValuePair<string, List<string>>(s, bySig[s]));
            foreach (var sp in list) rep.problems.AddRange(sp.problems);
            foreach (var g in Roster(gens)) if (g != null && !string.IsNullOrEmpty(g.name) && Of(g) == null) rep.missing.Add(g.name);
            rep.hand = list.Count(sp => !sp.auto);
            rep.auto = list.Count - rep.hand;
            rep.ok = rep.dupNames.Count == 0 && rep.dupSigs.Count == 0 && rep.problems.Count == 0 && rep.missing.Count == 0;
            return rep;
        }
    }
}
