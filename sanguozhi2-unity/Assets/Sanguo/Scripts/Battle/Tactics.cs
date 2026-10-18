// 三国志-霸王的世界 · 计策目录（对应网页版 js/tactics-data.js，DESIGN-V3 §4）
//
// 18 种计策，分 4 阶。原四计（火计、落石、混乱、激励）规则与数值不变：仍按智力自带（不占格），结算走 BattleModel
// 第一版的代码（Kind 为 TacticKind 的 0..3，即 Defs.Tactics 的四个对象）。其余 14 种为「可习得计策」：按智力与等级解锁
// （Progress.TacticSlots），Kind = TacticKind.Learned，由 BattleModel 的 TacticPlan / UseNewTactic 按规则字段结算；
// 特效沿用 BattleView.SpecialFx 的风格（Fx），不需要新的画面代码。数据由导出工具生成（Data/Generated/TacticsData.g.cs）。
//
// 公开接口
//   Tactics.All() → 全部 18 种                    Tactics.ById(id) → 计策 | null
//   Tactics.Innate() → 原四计（= Defs.Tactics）     Tactics.Learnable() → 14 种可习得计策
//   Tactics.Known(gen) → 该武将已会的计策（原四计按智力 + 已习得；顺序 = gen.tactics；Progress.enabled = false 时只有原四计）
//   Tactics.IsNew(t) → true：第三版新增
//   Tactics.Explain(t) → 中文效果说明（与数值一致；可习得计策的 Desc 即此）   Tactics.Rules(t) → 紧凑摘要（「初阶 · 射程4 · 冷却1日 · 需智力50」）
//   Tactics.TierName = { "", "初阶", "中阶", "高阶", "绝阶" }
// 控制层辅助（纯模型；界面据此画菜单、选目标、播放特效与飘字）
//   Tactics.MenuItems(model, u) → [TacticMenuRow]   策略菜单条目（不可用时注明原因；新计策右栏放等阶，摘要放说明行）
//   Tactics.AutoTarget(model, u, t) → 不必选择时的目标（自身为中心 / 激励：u；只有一个目标：它），否则 null（由界面在 TacticTargets 中选）
//   Tactics.Perform(model, u, t, target) → SpecialResult（原四计的 { 成功, 伤害 } 补成与新计策相同的结构）；不调用 Spend
//   Tactics.FxTargets(res, u)、Tactics.CurseWord(id)、Tactics.BuffWord(id)：特效目标与飘字（网页版 perform 的画面部分用）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sanguo
{
    // 策略菜单的一行（Tactics.MenuItems）
    public sealed class TacticMenuRow
    {
        public TacticDef tactic;
        public bool ok;
        public string why = "";             // 不可用的原因（智力不足、冷却中、范围内无目标…）
        public string tierLabel = "";       // 新计策：等阶名（初阶…）；原四计为空
        public string summary = "";         // 新计策：Rules 去掉等阶后的摘要；原四计为 Desc
        public string summaryShort = "";    // 矮屏（横屏手机）只留前三项
    }

    public static class Tactics
    {
        public static string[] TierName { get { return TacticsData.TierName; } }
        public static string TierNameOf(int t) { return t >= 0 && t < TacticsData.TierName.Length ? TacticsData.TierName[t] : ""; }

        // ------------------------------------------------------------ 目录 --
        static List<TacticDef> all;
        static Dictionary<string, TacticDef> byId;
        static void Build()
        {
            if (all != null) return;
            var list = new List<TacticDef>();
            var map = new Dictionary<string, TacticDef>();
            foreach (var t in Defs.Tactics) { list.Add(t); map[t.Id] = t; }       // 原四计（Defs 的静态构造已补上目录字段）
            foreach (var e in TacticsData.Learnable)
            {
                var t = new TacticDef(e);
                t.Desc = Explain(t);
                list.Add(t); map[t.Id] = t;
            }
            byId = map; all = list;
        }
        public static List<TacticDef> All() { Build(); return new List<TacticDef>(all); }
        public static TacticDef ById(string id) { if (id == null) return null; Build(); TacticDef t; return byId.TryGetValue(id, out t) ? t : null; }
        public static List<TacticDef> Innate() { Build(); return all.Where(t => t.Innate).ToList(); }
        public static List<TacticDef> Learnable() { Build(); return all.Where(t => !t.Innate).ToList(); }
        public static bool IsNew(TacticDef t) { return t != null && t.IsNew; }
        public static List<TacticDef> Known(General gen)
        {
            if (gen == null) return new List<TacticDef>();
            Func<TacticDef, bool> intelOk = t => gen.intel >= t.ReqInt;
            if (!Progress.enabled || gen.tactics == null) return Innate().Where(intelOk).ToList();
            var outList = new List<TacticDef>();
            foreach (var t in Innate()) if (intelOk(t)) outList.Add(t);          // 原四计按智力（与 TacticUsable 一致）
            foreach (var id in gen.tactics) { var t = ById(id); if (t != null && !t.Innate && !outList.Contains(t)) outList.Add(t); }
            return outList;
        }

        // ------------------------------------------------------------ 说明 --
        // Math.round：四舍五入（.5 向 +∞）
        static double JsRound(double x) { double r = Math.Floor(x); if (x - r >= 0.5) r += 1; return r; }
        static string N(double v) { return Specials.JsNum(v); }
        static string Pct(double v) { return N(JsRound(v * 100)) + "%"; }
        static string Signed(double v) { return v > 0 ? "+" + N(v) : v < 0 ? "−" + N(Math.Abs(v)) : "0"; }
        // JS 的 toFixed(n)：按双精度的精确值舍入（Specials.JsToFixed；不能用 decimal / G17 往返，Mono 会把 10.85 舍成 "10.9"）
        static string ToFixed(double v, int n) { return Specials.JsToFixed(v, n); }
        static string MoraleText(TacticDef t)
        {
            if (t.Morale == 0) return "";
            string per = t.MoraleI != 0 ? (t.MoraleI > 0 ? " + 智力/" : " − 智力/") + N(JsRound(1 / Math.Abs(t.MoraleI))) : "";
            return "士气 " + Signed(t.Morale) + per;
        }
        public static string NeedText(string n) { return n == "river" ? "目标须在河上或临河" : n == "cover" ? "目标须在林地 / 山丘或其旁" : n == "hill-self" ? "须立于山丘" : ""; }
        static string ModText(TacticMod m)
        {
            if (m == null) return "";
            var p = new List<string>();
            Func<double?, bool> on = v => v.HasValue && v.Value != 0;
            if (on(m.Atk)) p.Add("攻击 " + (m.Atk.Value >= 1 ? "+" : "−") + Pct(Math.Abs(m.Atk.Value - 1)));
            if (on(m.Def)) p.Add("防御 " + (m.Def.Value >= 1 ? "+" : "−") + Pct(Math.Abs(m.Def.Value - 1)));
            if (on(m.Counter)) p.Add("反击 " + (m.Counter.Value >= 1 ? "+" : "−") + Pct(Math.Abs(m.Counter.Value - 1)));
            if (on(m.Move)) p.Add("机动力 " + Signed(m.Move.Value));
            if (on(m.Dot)) p.Add("中毒（每日损兵 " + Pct(m.Dot.Value) + "）");
            return string.Join("、", p.ToArray()) + "（" + (m.Days.HasValue ? N(m.Days.Value) : "undefined") + " 日）";
        }
        public static string Explain(TacticDef t)
        {
            if (t == null) return "";
            if (!t.IsNew) return t.Desc ?? "";
            var parts = new List<string>();
            string tgt = t.Target == "enemy" ? (t.Range + " 格内一支敌军" + (t.Radius != 0 ? "及其周围 " + t.Radius + " 格的敌军" : ""))
                : t.Target == "ally" ? (t.Range + " 格内一支友军（含自身）") : ("自身周围 " + t.Radius + " 格内的友军");
            parts.Add((t.Target == "self" ? "对" : "对 ") + tgt);
            if (t.Dmg != null)
                parts.Add("造成 (" + N(t.Dmg.Base) + " + 智力×" + N(t.Dmg.Per) + ") 的计策伤害" + (t.Dmg.Forest != 0 ? "（林地 ×" + N(t.Dmg.Forest) + "）" : "") +
                    (t.Dmg.Splash != 0 ? "，周围敌军 ×" + N(t.Dmg.Splash) : "") + (t.Dmg.Pierce != 0 ? "，不受城防减免" : "，城门 / 本城内 ×0.6"));
            if (t.Heal != 0)
                parts.Add("回复 " + Pct(t.Heal) + " + 智力×" + Regex.Replace(ToFixed(t.HealI * 100, 2), @"\.?0+$", "") + "% 的最大兵力（不超过出阵兵力）" + (t.Food != 0 ? "，每回复 " + t.Food + " 兵耗粮 1" : ""));
            var mt = MoraleText(t);
            if (mt.Length > 0) parts.Add(mt);
            if (t.Mod != null) parts.Add(ModText(t.Mod));
            if (t.Confuse != null) parts.Add(t.Confuse.Base >= 1 ? "并使其混乱 " + N(t.Confuse.Turns) + " 日" : "可能陷入混乱 " + N(t.Confuse.Turns) + " 日（随智力差）");
            if (t.FoeConfuse != null) parts.Add("周围 " + t.Radius + " 格内的敌军可能混乱 " + N(t.FoeConfuse.Turns) + " 日（随智力差）");
            if (t.Cure) parts.Add("解除混乱");
            if (t.Douse) parts.Add("扑灭火势");
            string s = (t.Lore ?? "") + string.Join("，", parts.ToArray()) + "。";
            if (t.Roll != null)
                s += "成功率 " + Pct(t.Roll.Base) + " 起，随智力差增减（" + Pct(t.Roll.Min) + "–" + Pct(t.Roll.Max) + "）" + (t.Roll.Forest != 0 ? "，目标在林地时 +" + Pct(t.Roll.Forest) : "") +
                    (t.Roll.LowMorale != 0 ? "，目标士气低于 50 时 +" + Pct(t.Roll.LowMorale) : "") + "。";
            if (t.Need != null) s += NeedText(t.Need) + "。";
            if (t.Cd != 0) s += "用后冷却 " + t.Cd + " 日。";
            return s;
        }
        public static string Rules(TacticDef t)
        {
            if (t == null) return "";
            var p = new List<string> { TierNameOf(t.Tier) };
            if (t.Target == "self") p.Add("自身周围" + (t.Radius != 0 ? t.Radius : 1));
            else p.Add((t.Target == "ally" ? "友军 · " : "") + "射程" + t.Range + (t.Radius != 0 ? " · 范围" + t.Radius : ""));
            if (t.Dmg != null) p.Add("伤害 " + N(t.Dmg.Base) + "+智×" + N(t.Dmg.Per));
            if (t.Heal != 0) p.Add("回复" + Pct(t.Heal) + "起");
            if (t.Confuse != null || t.FoeConfuse != null || (!t.IsNew && t.Kind == TacticKind.Confuse)) p.Add("混乱");
            if (t.Mod != null && t.Mod.Dot.HasValue && t.Mod.Dot.Value != 0) p.Add("中毒");
            if (t.Roll != null) p.Add("成功率" + Pct(t.Roll.Base) + "±智力差");
            if (t.Need != null) p.Add(NeedText(t.Need));
            if (t.Cd != 0) p.Add("冷却" + t.Cd + "日");
            p.Add("需智力" + t.ReqInt);
            return string.Join(" · ", p.Where(x => !string.IsNullOrEmpty(x)).ToArray());
        }

        // ============================================================ 控制层辅助 --
        // 策略菜单条目（网页版 menuItems）：会的计策逐条列出；不可用时灰显并注明原因
        public static List<TacticMenuRow> MenuItems(BattleModel m, BUnit u)
        {
            var rows = new List<TacticMenuRow>();
            foreach (var t in m.KnownTactics(u))
            {
                string why; bool us = m.TacticUsable(u, t, out why);
                why = why ?? "";
                bool ok = us && m.TacticTargets(u, t).Count > 0;
                if (why == "" && !ok) why = "范围内无目标";
                var row = new TacticMenuRow { tactic = t, ok = ok, why = why };
                if (!t.IsNew) { row.summary = row.summaryShort = t.Desc ?? ""; }
                else
                {
                    var parts = Rules(t).Split(new[] { " · " }, StringSplitOptions.None).Skip(1).ToArray();
                    row.tierLabel = TierNameOf(t.Tier);
                    row.summary = string.Join(" · ", parts);
                    row.summaryShort = string.Join(" · ", parts.Take(3).ToArray());
                }
                rows.Add(row);
            }
            return rows;
        }
        // 不必让玩家选择时的目标：自身为中心（含激励）→ u；只有一个目标 → 它；无目标或需选择 → null
        public static BUnit AutoTarget(BattleModel m, BUnit u, TacticDef t)
        {
            var list = m.TacticTargets(u, t);
            if (list.Count == 0) return null;
            if (t.Target == "self" || (!t.IsNew && t.Kind == TacticKind.Inspire)) return u;
            return list.Count == 1 ? list[0] : null;
        }
        // 结算（网页版 perform 的规则部分）：新计策返回 UseNewTactic 的结果；原四计的 { 成功, 伤害 } 补成同一结构
        public static SpecialResult Perform(BattleModel m, BUnit u, TacticDef t, BUnit target)
        {
            if (m == null || u == null || t == null) return null;
            if (t.IsNew) return m.UseNewTactic(u, t, target);
            var r = m.UseTactic(u, t, target);
            var res = m.NewRes();
            res.success = r.Key; res.dmg = r.Value;
            res.tactic = t; res.kind = t.Id; res.name = t.Name; res.target = target;
            if (r.Value > 0) res.hits.Add(new SpecialHit { unit = target, dmg = r.Value, primary = true });
            if (!r.Key && target != null) res.resisted.Add(target);
            if (target != null && !target.alive) res.routed.Add(target);
            return res;
        }
        // 特效的目标（主目标、命中者、受影响者；都没有时为主目标或施计者）
        public static List<BUnit> FxTargets(SpecialResult res, BUnit u)
        {
            var outList = new List<BUnit>();
            Action<BUnit> add = x => { if (x != null && !outList.Contains(x)) outList.Add(x); };
            if (res.target != null && res.target != u) add(res.target);
            foreach (var h in res.hits) add(h.unit);
            foreach (var x in res.affected) add(x);
            if (outList.Count == 0) add(res.target ?? u);
            return outList;
        }
        // 飘字：削弱（res.cursed）与加成（res.buffed）的文字；没有时为 null
        public static string CurseWord(string id)
        {
            switch (id) { case "rumor": return "动摇"; case "feint": return "迟疑"; case "provoke": return "阵脚大乱"; case "sorcery": return "中毒"; default: return null; }
        }
        public static string CurseColor(string id) { return id == "sorcery" ? "#9be06a" : "#ffb04a"; }
        public static string BuffWord(string id)
        {
            switch (id) { case "rouse": return "士气高涨"; case "bazhen": return "八阵护体"; case "qimen": return "机动 +2"; case "supply": return "补给"; default: return null; }
        }
    }
}
