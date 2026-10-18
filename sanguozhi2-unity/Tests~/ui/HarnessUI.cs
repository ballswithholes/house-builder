// 界面文字的无头测试（Tests~/ui/run.sh）：UI/UIText.cs 的 UIText.Faces（= ui.js 的 UI.faces）、GrowthText（= strategy-screen.js 的
// openTiers / slotText / stripLore / tierText，以及 learnDialog / report 里的文字规则）对一组确定的输入算出结果，
// 输出格式与 ui-ref.js 逐行相同；run.sh 再与 node ui-ref.js 的输出逐行比较。另有不依赖网页版的自检（标记往返、占位式样）。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sanguo
{
    static class HarnessUI
    {
        static readonly List<string> outp = new List<string>();
        static void Put(string s) { outp.Add((s ?? "").Replace("\n", "\\n")); }
        static string Strip(string s) { return Regex.Replace(s ?? "", "<[^>]*>", ""); }
        static string Canon(GameState g, string s)
        {
            return UIText.Rewrite(s, id => { var x = UIText.GenById(g, id); return "[[F:" + (x != null ? x.name : "?" + id) + "]]"; }, (st, t) => "[[B:" + t + "]]");
        }
        static string FacesC(GameState g, string s, FacesOpts o) { return Canon(g, UIText.Faces(g, s, o)); }

        static List<string> Synth(GameState g)
        {
            var L = new List<string>();
            var fs = g.factions; var cs = g.cities; var gs = g.generals;
            for (int i = 0; i < fs.Count; i++)
            {
                var f = fs[i]; var c = cs[(i * 7) % cs.Count]; var c2 = cs[(i * 13 + 5) % cs.Count];
                var a = gs[(i * 11) % gs.Count]; var b = gs[(i * 17 + 3) % gs.Count];
                L.Add(f.name + "攻陷了" + c.name + "，" + a.name + "被俘。");
                L.Add(c.name + "被" + f.name + "军占领，" + b.name + "离开了" + f.name + "。");
                L.Add(f.name + "的" + a.name + "与" + b.name + "同赴" + c2.name + "；" + f.name + "势力扩张。");
                L.Add("攻陷了" + f.name + "，" + f.name + "无人防守，" + c.name + "拒绝" + f.name + "。");
                L.Add(a.name + "、" + b.name + "、" + a.name + "在<b>" + b.name + "</b>处与" + f.name + "同意结盟");
            }
            foreach (var c in cs) L.Add(c.name + "继承了" + c.name + "，" + c.name + "的城防加强。离开了" + c.name);
            return L;
        }
        static void FacesLines(GameState g, List<string> lines)
        {
            var ru = g.factions.Select(f => g.Ruler(f.id)).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                var s = lines[i];
                Put("F0 " + FacesC(g, s, null));
                if (i % 3 == 0) Put("F1 " + FacesC(g, s, new FacesOpts { max = 2 }));
                if (i % 4 == 1) Put("F2 " + FacesC(g, s, new FacesOpts { factions = false, max = 8 }));
                if (i % 5 == 2) { var gg = g.generals[(i * 7) % g.generals.Count]; Put("F3 " + FacesC(g, s, new FacesOpts { gens = new[] { gg, g.generals[(i * 3) % g.generals.Count] }, max = 12 })); }
                if (i % 6 == 3) { var sk = ru[i % ru.Count]; Put("F4 " + FacesC(g, s, new FacesOpts { skip = sk != null ? new[] { sk.name } : new string[0], max = 10 })); }
            }
        }

        static int fails;
        static void Check(bool ok, string what) { if (!ok) { fails++; Console.WriteLine("  FAIL " + what); } }

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            // ---------------------------------------------------------- 1. UIText.Faces --
            foreach (var spec in new[] { new object[] { "classic", "liubei", 1, 18 }, new object[] { "world", "roma", 3, 14 } })
            {
                string scen = (string)spec[0], key = (string)spec[1]; int seed = (int)spec[2], months = (int)spec[3];
                UnityEngine.Random.InitState(seed);
                var g = GameState.Current = GameState.NewGame(key, scen);
                g.player = -1;
                UIText.ResetIndex();
                Put("== " + scen + " seed " + seed);
                FacesLines(g, Synth(g));
                for (int m = 0; m < months; m++)
                {
                    var news = new List<string>();
                    StrategyAI.RunAI(news);
                    news.AddRange(StrategyAI.EndMonth());
                    Put("M" + m + " " + news.Count);
                    var lines = new List<string> { string.Join("\n", news.ToArray()) };
                    lines.AddRange(news);
                    FacesLines(g, lines);
                }
                FacesLines(g, g.log.ToList());
            }

            // ---------------------------------------------------------- 2. 成长文字 --
            foreach (var spec in new[] { new[] { "classic", "liubei" }, new[] { "world", "roma" } })
            {
                UnityEngine.Random.InitState(7);
                var g = GameState.Current = GameState.NewGame(spec[1], spec[0]);
                g.seed = 12345;
                Put("== growth " + spec[0]);
                for (int i = 0; i < g.generals.Count; i++)
                {
                    var gen = g.generals[i];
                    if (i % 3 == 2) continue;
                    gen.exp = Progress.Exp[1 + (i % 10)] + (i % 7) * 3;
                    Progress.Ensure(gen);
                    int k = i % 4;
                    for (int j = 0; j < k; j++) { var c = Progress.Candidates(gen, 3); if (c.Count == 0) break; Progress.Learn(gen, c[(i + j) % c.Count]); }
                    var ss = Progress.SpecialSlots(gen); var ts = Progress.TacticSlots(gen);
                    int st, tt;
                    string so = GrowthText.OpenTiers(ss, GrowthText.SpecialTier, out st), to = GrowthText.OpenTiers(ts, GrowthText.TacticTier, out tt);
                    Put(string.Join("|", new[] { gen.name, gen.level.ToString(), Strip(GrowthText.SlotText(gen, "special")), Strip(GrowthText.SlotText(gen, "tactic")), so, st.ToString(), to, tt.ToString(), GrowthText.CanLearn(gen) ? "1" : "0" }));
                    foreach (var c in Progress.Candidates(gen, 3))
                    {
                        string lore = c.type == "special" ? (c.special != null ? c.special.lore : null) : (c.tactic != null ? c.tactic.Lore : null);
                        string body = GrowthText.StripLore(c.desc, lore);
                        Put("  " + string.Join("|", new[] { c.type, c.id, GrowthText.TierText(c), body, GrowthText.RulesNoTier(c.rules), GrowthText.Brief(body) }));
                    }
                }
                Put("pending " + Progress.Pending(g.factions[0].id).Count + " " + Progress.Pending(g.factions[1].id).Count);
            }

            // ---------------------------------------------------------- 3. 汇报去名 --
            {
                var g = GameState.Current;
                var fakes = new List<string> { "刘备主持土地开发，土地增至 50。", "刘备，你好", "刘备", "刘备。", "关羽训练部队", "刘备！大喜", "刘备、关羽出征" };
                var who = g.generals.FirstOrDefault(x => x.name == "刘备") ?? g.generals[0];
                fakes.AddRange(g.generals.Take(40).Select(x => x.name + "搜索人才，一无所获。"));
                foreach (var t0 in fakes) Put("R " + GrowthText.ReportText(who, t0));
            }
            Console.Write(string.Join("\n", outp.ToArray()) + "\n");

            // ---------------------------------------------------------- 4. 自检（不经网页版） --
            var gs0 = GameState.Current;
            var gA = gs0.generals[3];
            string mk = "前" + UIText.Face(gA) + gA.name + UIText.Tag(TagStyle.Lv, "Lv5") + "后" + UIText.Tag(TagStyle.Cmd, "开发");
            var marks = UIText.Marks(mk);
            Check(marks.Count == 3 && marks[0].kind == 'F' && marks[0].id == gA.id && marks[1].kind == 'B' && marks[1].style == TagStyle.Lv && marks[1].text == "Lv5" && marks[2].style == TagStyle.Cmd, "标记解析");
            Check(UIText.Plain(mk) == "前" + gA.name + "Lv5后开发", "Plain：" + UIText.Plain(mk));
            Check(!UIText.HasMarks(UIText.Plain(mk)), "Plain 后无标记");
            Check(UIText.Marks(UIText.Faces(gs0, "前" + UIText.Tag(TagStyle.Lv, gA.name) + "后", null)).Count(x => x.kind == 'F') == 0, "标签文字里的姓名不加头像");
            Check(UIText.Tag(TagStyle.Lv, "") == "" && UIText.Face(null) == "", "空标签 / 空头像");
            Check(GrowthText.ReportText(null, "刘备开发") == "刘备开发", "无说话人时不去名");
            Check(GrowthText.Brief(new string('字', 40) + "。后") == new string('字', 30) + "…", "Brief 截断 30 字");
            Check(GrowthText.Cps("𠀀a").Count == 2, "码位拆分（代理对）");
            // 城池情报的流排（矮屏）：等宽量法（去标记后每字 1、头像标记 2），项内不折行、放不下整项换行
            Func<string, float> mono = s => Regex.Replace(UIText.Plain(s), "<[^>]+>", "").Length + 2 * UIText.Marks(s).Count(x => x.kind == 'F');
            var fl = new List<string> { "太守 " + UIText.Face(gA) + "AB", "人口 12", "土地 <b>3</b>", "", "町 <b>4</b>" };
            string flow = UIText.FlowItems(fl, "  ", 16, mono);
            Check(flow == "太守 " + UIText.Face(gA) + "AB  人口 12\n土地 <b>3</b>  町 <b>4</b>", "流排：" + UIText.Plain(flow).Replace("\n", "|"));
            Check(UIText.FlowItems(new[] { "一二三四五六", "七" }, " ", 4, mono) == "一二三四五六\n七", "流排：超宽单项独占一行");
            Check(UIText.FlowItems(new string[0], " ", 4, mono) == "", "流排：空");
            // 占位（UIKit.ToSlots 的式样）与打字机：每个前缀里只认完整的占位；头像在打到它时出现，标签在整段打完时出现
            int slotCases = 0, prefixCases = 0;
            var rnd = new SeededRandom(99);
            for (int k = 0; k < 300; k++)
            {
                var sb = new StringBuilder();
                var want = new List<string>();
                int parts = 1 + (int)(rnd.NextDouble() * 7);
                for (int p = 0; p < parts; p++)
                {
                    double r = rnd.NextDouble();
                    if (r < 0.3) { var x = gs0.generals[(int)(rnd.NextDouble() * gs0.generals.Count)]; sb.Append(UIText.Face(x)).Append(x.name); want.Add("F" + x.id); }
                    else if (r < 0.5) { var st = (TagStyle)(int)(rnd.NextDouble() * 14); string tx = new[] { "Lv5", "新", "君", "专属", "二阶", "开发", "<x>" }[(int)(rnd.NextDouble() * 7)]; sb.Append(UIText.Tag(st, tx)); want.Add("B" + (int)st + ":" + tx.Replace("<", "‹").Replace(">", "›")); }
                    else if (r < 0.65) sb.Append("<color=#a8a194>灰字</color>");
                    else if (r < 0.75) sb.Append("<b>粗</b>\n");
                    else sb.Append("文字　城 3　将 5");
                }
                string src = sb.ToString();
                int fsz = 18 + (int)(rnd.NextDouble() * 14);
                string disp = UIText.Rewrite(src, id => UIText.SlotFace(id, (int)Math.Round(fsz * 1.62)), (st, tx) => UIText.SlotTag(st, tx, Math.Max(11, (int)Math.Round(fsz * 0.7))));
                int nf, nt;
                var slots = UIText.FindSlots(disp, out nf, out nt);
                var got = slots.OrderBy(x => x.first).Select(x => x.kind == 'F' ? "F" + x.id : "B" + (int)x.style + ":" + x.text).ToList();
                Check(got.SequenceEqual(want), "占位往返 " + k + "：" + string.Join(",", want) + " ≠ " + string.Join(",", got));
                var vis = UIText.VisIndex(disp);
                foreach (var x in slots) Check(disp[x.first] == (x.kind == 'F' ? '　' : ' ') && disp[x.last] == (x.kind == 'F' ? '　' : ' '), "占位字符的位置 " + k);
                slotCases++;
                int total;
                UIText.RichPrefix(disp, int.MaxValue, out total);
                Check(total == vis[disp.Length], "可见字数一致 " + k);
                for (int n = 0; n <= total; n++)
                {
                    int pf, pt;
                    string pre = UIText.RichPrefix(disp, n, out total);
                    var ps = UIText.FindSlots(pre, out pf, out pt);
                    int ef = slots.Count(x => x.kind == 'F' && vis[x.first] < n), et = slots.Count(x => x.kind == 'B' && vis[x.last] < n);
                    Check(pf == ef && pt == et, "打字机前缀 " + k + " / " + n + "：头像 " + pf + "（应 " + ef + "）标签 " + pt + "（应 " + et + "）");
                    prefixCases++;
                }
            }
            Console.Error.WriteLine("占位 " + slotCases + " 例、打字机前缀 " + prefixCases + " 例");
            Console.Error.WriteLine(fails == 0 ? "自检通过" : "自检失败 " + fails + " 项");
            return fails;
        }
    }
}
