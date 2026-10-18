using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sanguo
{
    // 三国志-霸王的世界 · 界面文字的纯文字部分（第三版 §4 成长、§5 头像无处不在；← 网页版 ui.js 的 UI.faces / UI.face，
    // strategy-screen.js 的 openTiers / slotText / report 等）。不依赖 UGUI，可在无头测试（Tests~/ui）中与网页版逐字对照。
    //
    // 行内头像与小标签的「标记」：网页版直接在 HTML 里插 <img>（头像）或 <span class="sg-lv">（标签）；
    // UGUI 的 Text 不能内嵌图片，改为在文字里放标记，由 UIKit.SetText 换成占位（同宽的透明字符），
    // 再由 InlineDeco 组件按 TextGenerator 的字符位置在占位处叠放头像 / 标签（见 UIKit.cs）。
    //   UIText.Face(gen)            → 行内小头像标记（\u000E F 编号 \u000F）
    //   UIText.Tag(style, text)     → 小标签标记（\u000E B 样式:文字 \u000F）：Lv、「新」、君 / 守、等阶、指令名……
    //   UIText.Faces(text, opts)    → 文字中提到的武将（以及势力名 → 其君主）前加行内小头像（= UI.faces，规则逐条相同）
    //   UIText.Plain(text)          → 去掉标记（头像去掉，标签留下文字）：无界面时 / 测试用
    //   UIText.Marks(text)          → 标记列表（测试用）
    public enum TagStyle
    {
        Lv,             // .sg-lv：金色渐变底、深色字（等级「Lv5」）
        LvRow,          // .sg-genrow-name .sg-lv：城池面板武将行（字略小）
        New,            // .sg-lv-new：朱红底「新」（可习得新招，闪烁）
        Ruler,          // .sg-tag.sg-tag-ruler：「君」
        Gov,            // .sg-tag.sg-tag-gov：「守」
        SpTier,         // .sg-sp-tier：可习得必杀的等阶（淡金底）
        SpSig,          // .sg-sp-tier.is-sig：「专属」（朱红底）
        KindSpecial,    // .sg-learn-kind.is-special：「必杀」
        KindTactic,     // .sg-learn-kind.is-tactic：「计策」
        Tier1, Tier2, Tier3, Tier4,   // .sg-learn-tier.t1..t4：习得对话框右栏的等阶
        Cmd,            // .sg-say-cmd：对话说话人姓名下的指令名（金色描边）
    }

    // UIText.Faces 的选项（= UI.faces 的 opts）
    public sealed class FacesOpts
    {
        public IEnumerable<General> gens;      // 只给这些武将加头像（null = 不限）
        public IEnumerable<string> skip;       // 不加头像的姓名（如对话的说话人）
        public bool factions = true;           // 势力名 → 其君主的头像
        public int max = 8;                    // 至多几个头像
    }

    public static class UIText
    {
        public const char Open = '\u000E', Close = '\u000F';

        // ---------------------------------------------------------- 标记 --
        public static string Face(General g) { return g == null ? "" : Open + "F" + g.id.ToString(System.Globalization.CultureInfo.InvariantCulture) + Close; }
        public static string Tag(TagStyle s, string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Open + "B" + ((int)s).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + text.Replace(Open, ' ').Replace(Close, ' ') + Close;
        }
        public static bool HasMarks(string s) { return s != null && s.IndexOf(Open) >= 0; }

        // 一个标记：kind 'F'（头像，id = 武将编号）或 'B'（标签，style、text）；start / length 为在原文中的位置
        public struct Mark { public char kind; public int id; public TagStyle style; public string text; public int start, length; }
        public static List<Mark> Marks(string s)
        {
            var o = new List<Mark>();
            if (s == null) return o;
            int i = 0;
            while ((i = s.IndexOf(Open, i)) >= 0)
            {
                int e = s.IndexOf(Close, i + 1);
                if (e < 0) break;
                var m = Parse(s.Substring(i + 1, e - i - 1));
                if (m.kind != '\0') { m.start = i; m.length = e - i + 1; o.Add(m); }
                i = e + 1;
            }
            return o;
        }
        static Mark Parse(string body)
        {
            var m = new Mark();
            if (body.Length < 2) return m;
            if (body[0] == 'F')
            {
                int id;
                if (int.TryParse(body.Substring(1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out id) && id >= 0) { m.kind = 'F'; m.id = id; }
            }
            else if (body[0] == 'B')
            {
                int c = body.IndexOf(':');
                int st;
                if (c > 1 && int.TryParse(body.Substring(1, c - 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out st))
                { m.kind = 'B'; m.style = (TagStyle)st; m.text = body.Substring(c + 1); }
            }
            return m;
        }
        // 按标记逐段改写：face(id) → 头像的替代文字，tag(style, text) → 标签的替代文字；其余原样
        public static string Rewrite(string s, Func<int, string> face, Func<TagStyle, string, string> tag)
        {
            if (!HasMarks(s)) return s;
            var sb = new StringBuilder(s.Length + 32);
            int pos = 0;
            foreach (var m in Marks(s))
            {
                sb.Append(s, pos, m.start - pos);
                sb.Append(m.kind == 'F' ? face(m.id) : tag(m.style, m.text));
                pos = m.start + m.length;
            }
            sb.Append(s, pos, s.Length - pos);
            return sb.ToString();
        }
        // 去掉标记：头像去掉，标签留下文字
        public static string Plain(string s) { return Rewrite(s, id => "", (st, t) => t); }

        // 流排（← 矮屏上城池情报的 .sg-cs 各项：项内不折行，一行放不下时整项移到下一行）：items 依次排入宽 avail 的行，
        // 同一行的项之间用 sep 隔开；width(s) 量一行文字的宽度（量整行，粗体、标记等都算在内）。返回以 "\n" 分行的文字。
        // 单项本身比 avail 宽时独占一行（由 Text 自行折行）；空项跳过
        public static string FlowItems(IList<string> items, string sep, float avail, Func<string, float> width)
        {
            var sb = new StringBuilder();
            string line = null;
            foreach (var it in items)
            {
                if (string.IsNullOrEmpty(it)) continue;
                if (line == null) { line = it; continue; }
                string next = line + sep + it;
                if (width(next) <= avail) line = next;
                else { sb.Append(line).Append('\n'); line = it; }
            }
            if (line != null) sb.Append(line);
            return sb.ToString();
        }

        // ---------------------------------------------------------- 占位（UIKit.ToSlots / InlineDeco） --
        // 头像：<size=S><color=#feXXXX00>　</color></size>（透明的全角空格，XXXX = 武将编号）
        // 标签：<size=B><color=#fdSS0000><b> 文字 </b></color></size>（透明的同字文字，两侧各一个空格作内边距，SS = 样式）
        public static string SlotFace(int id, int size)
        {
            return "<size=" + size + "><color=#fe" + (id & 0xffff).ToString("x4") + "00>　</color></size>";
        }
        public static string SlotTag(TagStyle st, string text, int size)
        {
            return "<size=" + size + "><color=#fd" + ((int)st & 0xff).ToString("x2") + "0000><b> " + (text ?? "").Replace("<", "‹").Replace(">", "›") + " </b></color></size>";
        }
        // 文字中完整的占位（打字机截取时没打完的不算）：kind 'F'（first = last = 全角空格的下标）或 'B'（first / last = 两侧空格的下标）
        public struct Slot { public char kind; public int id; public TagStyle style; public string text; public int size, first, last; }
        static readonly Regex FaceSlotRe = new Regex("<size=(\\d+)><color=#fe([0-9a-f]{4})00>　</color></size>", RegexOptions.CultureInvariant);
        static readonly Regex TagSlotRe = new Regex("<size=(\\d+)><color=#fd([0-9a-f]{2})0000><b> ([^<]+) </b></color></size>", RegexOptions.CultureInvariant);
        public static List<Slot> FindSlots(string s, out int faces, out int tags)
        {
            var o = new List<Slot>();
            faces = tags = 0;
            if (string.IsNullOrEmpty(s) || s.IndexOf("<size=", StringComparison.Ordinal) < 0) return o;
            foreach (Match m in FaceSlotRe.Matches(s))
            {
                int p = m.Index + m.Value.IndexOf('　');
                o.Add(new Slot { kind = 'F', id = Convert.ToInt32(m.Groups[2].Value, 16), size = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), first = p, last = p });
                faces++;
            }
            foreach (Match m in TagSlotRe.Matches(s))
            {
                int p = m.Index + m.Value.IndexOf("<b> ", StringComparison.Ordinal) + 3;
                string txt = m.Groups[3].Value;
                o.Add(new Slot { kind = 'B', style = (TagStyle)Convert.ToInt32(m.Groups[2].Value, 16), text = txt, size = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), first = p, last = p + 1 + txt.Length });
                tags++;
            }
            return o;
        }
        // 原文下标 → 去掉富文本标签后的下标（Unity 认得的标签：b i size color material quad；其余 <…> 照字显示）
        static readonly string[] UnityTags = { "b", "i", "size", "color", "material", "quad" };
        public static int[] VisIndex(string s)
        {
            s = s ?? "";
            var vis = new int[s.Length + 1];
            int n = 0, i = 0;
            while (i < s.Length)
            {
                if (s[i] == '<')
                {
                    int e = s.IndexOf('>', i);
                    if (e > i)
                    {
                        string tag = s.Substring(i + 1, e - i - 1);
                        if (tag.StartsWith("/", StringComparison.Ordinal)) tag = tag.Substring(1);
                        int q = tag.IndexOf('=');
                        string nm = q >= 0 ? tag.Substring(0, q) : tag;
                        if (Array.IndexOf(UnityTags, nm) >= 0)
                        {
                            for (int k = i; k <= e; k++) vis[k] = n;
                            i = e + 1;
                            continue;
                        }
                    }
                }
                vis[i] = n; n++; i++;
            }
            vis[s.Length] = n;
            return vis;
        }

        // 富文本的前 n 个可见字（打字机）：完整保留 b / i / color / size 标签，并补上尚未闭合的标签；total = 全部可见字数
        static readonly string[] PrefixTags = { "b", "i", "color", "size" };
        public static string RichPrefix(string text, int n, out int total)
        {
            text = text ?? "";
            var sb = new StringBuilder();
            var open = new List<string>();
            int count = 0, i = 0;
            while (i < text.Length)
            {
                if (text[i] == '<')
                {
                    int e = text.IndexOf('>', i);
                    if (e > i)
                    {
                        string tag = text.Substring(i + 1, e - i - 1);
                        bool close = tag.StartsWith("/", StringComparison.Ordinal);
                        string nm = (close ? tag.Substring(1) : tag).Split('=')[0];
                        if (Array.IndexOf(PrefixTags, nm) >= 0)
                        {
                            if (close) { int k = open.LastIndexOf(nm); if (k >= 0) open.RemoveAt(k); }
                            else open.Add(nm);
                            sb.Append(text, i, e - i + 1);
                            i = e + 1;
                            continue;
                        }
                    }
                }
                if (count < n) sb.Append(text[i]);
                count++; i++;
            }
            total = count;
            for (int k = open.Count - 1; k >= 0; k--) sb.Append("</").Append(open[k]).Append('>');
            return sb.ToString();
        }

        // 按编号找武将（编号即 generals 的下标；不一致时逐个找）
        public static General GenById(GameState g, int id)
        {
            if (g == null || g.generals == null || id < 0) return null;
            if (id < g.generals.Count && g.generals[id] != null && g.generals[id].id == id) return g.generals[id];
            foreach (var x in g.generals) if (x != null && x.id == id) return x;
            return null;
        }

        // ---------------------------------------------------------- UI.faces --
        // 文字中提到的武将（以及势力名 → 其君主）前加行内小头像；每个名字只加第一次出现。只处理标签之外的文字。
        // 与城同名的势力（罗马、夫余、鄯善……）按上下文区分：名字后接「攻陷 / 占领 / 击退 / 未能 / 军 / 势力 / 继承 / 同意 / 拒绝 / 的」
        // 或前接「离开了」时视为势力（配君主头像），否则视为城名（「攻陷了罗马」「罗马无人防守」不配头像）。
        sealed class Entry { public General gen; public City city; public Faction fac; }
        sealed class Index { public GameState g; public string key; public Dictionary<string, Entry> map; public Regex re; }
        static Index faceIdx;
        static Index FaceIndex(GameState g)
        {
            if (g == null || g.generals == null) return null;
            string key = g.generals.Count + "|" + (g.factions != null ? g.factions.Count : 0) + "|" + (g.cities != null ? g.cities.Count : 0);
            if (faceIdx != null && faceIdx.g == g && faceIdx.key == key) return faceIdx;
            var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var x in g.generals) if (x != null && x.name != null && x.name.Length >= 2 && !map.ContainsKey(x.name)) { map[x.name] = new Entry { gen = x }; order.Add(x.name); }
            if (g.cities != null) foreach (var c in g.cities) if (c != null && c.name != null && c.name.Length >= 2 && !map.ContainsKey(c.name)) { map[c.name] = new Entry { city = c }; order.Add(c.name); }
            if (g.factions != null)
                foreach (var f in g.factions)
                {
                    if (f == null || f.name == null || f.name.Length < 2) continue;
                    Entry e;
                    if (!map.TryGetValue(f.name, out e)) { map[f.name] = new Entry { fac = f }; order.Add(f.name); }
                    else if (e.city != null && e.fac == null) e.fac = f;   // 与城同名：保留两者，替换时看上下文
                }
            // 长名优先（稳定排序：同长时保持登记顺序，同 JS 的 Array.prototype.sort）
            var names = order.Select((n, i) => new KeyValuePair<string, int>(n, i)).OrderByDescending(p => p.Key.Length).ThenBy(p => p.Value).Select(p => p.Key).ToList();
            Regex re = names.Count > 0 ? new Regex(string.Join("|", names.Select(n => Regex.Escape(n)).ToArray()), RegexOptions.CultureInvariant) : null;
            faceIdx = new Index { g = g, key = key, map = map, re = re };
            return faceIdx;
        }
        static readonly Regex FacAfter = new Regex("^(攻陷|占领|击退|未能|军|势力|继承|同意|拒绝|的)", RegexOptions.CultureInvariant);
        static readonly Regex FacBefore = new Regex("离开了$", RegexOptions.CultureInvariant);
        // 富文本标签与已有的标记不参与替换（网页版：标签与 HTML 实体）
        static readonly Regex SegSplit = new Regex("(<[^>]*>|&[#\\w]+;|\u000E[^\u000F]*\u000F)", RegexOptions.CultureInvariant);

        public static string Faces(string text, FacesOpts opts = null) { return Faces(GameState.Current, text, opts); }
        public static string Faces(GameState g, string text, FacesOpts opts)
        {
            if (text == null) return null;
            var idx = FaceIndex(g);
            if (idx == null || idx.re == null) return text;
            opts = opts ?? new FacesOpts();
            var skip = new HashSet<string>(opts.skip ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            HashSet<string> only = opts.gens != null ? new HashSet<string>(opts.gens.Where(x => x != null).Select(x => x.name), StringComparer.Ordinal) : null;
            bool facs = opts.factions;
            int max = opts.max;
            var used = new HashSet<string>(StringComparer.Ordinal);
            int n = 0;
            var parts = SegSplit.Split(text);
            var sb = new StringBuilder(text.Length + 64);
            for (int i = 0; i < parts.Length; i++)
            {
                var seg = parts[i];
                if (i % 2 == 1 || seg.Length == 0) { sb.Append(seg); continue; }
                sb.Append(idx.re.Replace(seg, mt =>
                {
                    string m = mt.Value;
                    Entry e;
                    if (!idx.map.TryGetValue(m, out e) || n >= max || skip.Contains(m) || used.Contains(m)) return m;
                    int off = mt.Index;
                    if (e.city != null)
                    {   // 城名；与城同名的势力只在上下文像势力时配君主头像
                        int a = off + m.Length;
                        string after = seg.Substring(a, Math.Min(2, seg.Length - a));
                        int b = Math.Max(0, off - 3);
                        string before = seg.Substring(b, off - b);
                        bool asFac = e.fac != null && (FacAfter.IsMatch(after) || FacBefore.IsMatch(before));
                        if (!asFac) return m;
                    }
                    var gen = e.gen;
                    if (e.fac != null)
                    {
                        if (!facs || only != null) return m;
                        try { gen = g.Ruler(e.fac.id); } catch (Exception) { gen = null; }
                        if (gen == null || used.Contains(gen.name) || skip.Contains(gen.name)) return m;
                        used.Add(gen.name);
                    }
                    else if (only != null && !only.Contains(m)) return m;
                    used.Add(m);
                    n++;
                    return Face(gen) + m;
                }));
            }
            return sb.ToString();
        }
        // 测试 / 调试：清除姓名索引
        public static void ResetIndex() { faceIdx = null; }
    }

    // 成长界面的文字（← strategy-screen.js 的 openTiers / slotText / stripLore / tierText 与 report 的去名规则）
    public static class GrowthText
    {
        public const string GoodHex = "#73d98c", MutedHex = "#a8a194";
        static string Good(string s) { return "<color=" + GoodHex + ">" + s + "</color>"; }

        // 空格可学到的等阶：第 k 格至多 k 阶，另受武力 / 智力与等级限制 → 「二阶」或「初阶至中阶」（开着多格时）
        public static string OpenTiers(SlotInfo sl, Func<int, string> tierName, out int top)
        {
            top = 0;
            if (sl == null || !(sl.open > 0) || !(sl.tier > 0)) return "";
            top = Math.Max(sl.tier, Math.Min(sl.statTier, Math.Min(sl.levelTier, sl.used + sl.open)));
            return top > sl.tier ? tierName(sl.tier) + "至" + tierName(top) : tierName(sl.tier);
        }
        public static string OpenTiers(SlotInfo sl, Func<int, string> tierName) { int top; return OpenTiers(sl, tierName, out top); }
        public static string SpecialTier(int t) { return Specials.TierNameOf(t); }
        public static string TacticTier(int t) { return Tactics.TierNameOf(t); }

        // 格数说明：「已学 1 / 4 · 可习得 1（二阶）· 上限四阶」「已学 1 / 3 · 6 级可学下一招 · 上限三阶」
        // 括号里是眼下空格可学到的等阶（受等级与已学数限制）；「上限」是此人武力 / 智力最终能学到的等阶
        public static string SlotText(General gen, string type)
        {
            if (!Progress.enabled || gen == null) return "";
            bool sp = type == "special";
            var sl = sp ? Progress.SpecialSlots(gen) : Progress.TacticSlots(gen);
            string stat = sp ? "武力" : "智力";
            if (sl.max <= 0) return stat + " " + (sp ? ProgressData.SpecialSlotWar[0] : ProgressData.TacticSlotInt[0]) + " 以上方可习得";
            var lvls = sp ? ProgressData.SpecialSlotLevel : ProgressData.TacticSlotLevel;
            Func<int, string> tierName = sp ? (Func<int, string>)SpecialTier : TacticTier;
            var p = new List<string> { "已学 " + sl.used + " / " + sl.max };
            int top;
            string ot = OpenTiers(sl, tierName, out top);
            if (sl.open > 0) p.Add(Good("可习得 " + sl.open + (ot.Length > 0 ? "（" + ot + "）" : "")));
            else if (sl.unlocked < sl.max) p.Add(lvls[sl.unlocked] + " 级可学下一招");
            else if (sl.used >= sl.max) p.Add("已满");
            // 最终上限：第 k 格至多 k 阶，满级时等级不再限制，故为 min(武力 / 智力等阶, 格数)
            int cap = Math.Min(sl.statTier, sl.max);
            if (sl.used < sl.max && cap > top) p.Add("上限" + tierName(cap));
            return string.Join(" · ", p.ToArray());
        }

        // 有空格可学
        public static bool CanLearn(General gen)
        {
            if (!Progress.enabled || gen == null) return false;
            try { return Progress.OpeningsOf(gen).total > 0 && Progress.Candidates(gen, 1).Count > 0; } catch (Exception) { return false; }
        }
        public static string TierText(LearnCandidate c)
        {
            if (c == null) return "";
            return c.type == "special" ? Specials.TierNameOf(c.tier) : Tactics.TierNameOf(c.tier);
        }
        // 说明去掉开头的典故（lore）：典故另行显示
        public static string StripLore(string desc, string lore)
        {
            desc = desc ?? "";
            return !string.IsNullOrEmpty(lore) && desc.StartsWith(lore, StringComparison.Ordinal) ? desc.Substring(lore.Length) : desc;
        }
        // 指令结果是第三人称（「刘备主持土地开发……」）：由本人汇报时省去句首的自己的名字
        static readonly Regex PunctHead = new Regex("^[，。！？、：]", RegexOptions.CultureInvariant);
        public static string ReportText(General who, string text)
        {
            text = text ?? "";
            if (who != null && !string.IsNullOrEmpty(who.name) && text.Length > who.name.Length + 1 && text.StartsWith(who.name, StringComparison.Ordinal)
                && !PunctHead.IsMatch(text.Substring(who.name.Length))) text = text.Substring(who.name.Length);
            return text;
        }
        // 计策的数值摘要以等阶开头（右栏已有），去掉
        static readonly Regex TierHead = new Regex("^(初阶|中阶|高阶|绝阶) · ", RegexOptions.CultureInvariant);
        public static string RulesNoTier(string rules) { return TierHead.Replace(rules ?? "", ""); }
        // 矮屏（横屏手机）：说明只留第一句（截断到 30 字）
        public static string Brief(string body)
        {
            body = body ?? "";
            var first = body.Split('。')[0];
            string brief = first.Length > 0 ? first : body;
            var cps = Cps(brief);
            if (cps.Count > 30) brief = string.Concat(cps.Take(30).ToArray()) + "…";
            return brief;
        }
        // 按码位拆分（JS 的 Array.from(string)）
        public static List<string> Cps(string s)
        {
            var o = new List<string>();
            if (s == null) return o;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { o.Add(s.Substring(i, 2)); i++; }
                else o.Add(s[i].ToString());
            }
            return o;
        }
    }
}
